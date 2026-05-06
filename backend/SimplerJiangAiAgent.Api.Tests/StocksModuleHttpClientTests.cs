using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using LiteDB;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SimplerJiangAiAgent.Api.Infrastructure.Storage;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Modules.Market.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class StocksModuleHttpClientTests
{
    [Fact]
    public void Register_AppliesConfiguredTimeoutToStockCrawlerClients()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["StockCrawler:HttpTimeoutSeconds"] = "9"
            })
            .Build();

        new StocksModule().Register(services, configuration);

        using var provider = services.BuildServiceProvider();
        var eastmoneyCrawler = provider.GetRequiredService<EastmoneyStockCrawler>();
        var tencentCrawler = provider.GetRequiredService<TencentStockCrawler>();

        Assert.Equal(TimeSpan.FromSeconds(9), GetHttpClientTimeout(eastmoneyCrawler));
        Assert.Equal(TimeSpan.FromSeconds(9), GetHttpClientTimeout(tencentCrawler));
    }

    [Fact]
    public void Register_ClampsStockCrawlerTimeoutToSafeLowerBound()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["StockCrawler:HttpTimeoutSeconds"] = "1"
            })
            .Build();

        new StocksModule().Register(services, configuration);

        using var provider = services.BuildServiceProvider();
        var eastmoneyCrawler = provider.GetRequiredService<EastmoneyStockCrawler>();

        Assert.Equal(TimeSpan.FromSeconds(5), GetHttpClientTimeout(eastmoneyCrawler));
    }

    [Fact]
    public void Register_RegistersFinancialDataReadService()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

        new StocksModule().Register(services, configuration);

        var descriptor = Assert.Single(services, service => service.ServiceType == typeof(IFinancialDataReadService));
        Assert.Equal(typeof(FinancialDataReadService), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Theory]
    [InlineData(null, "http://localhost:5120")]
    [InlineData("", "http://localhost:5120")]
    [InlineData("http://localhost:6120/", "http://localhost:6120")]
    [InlineData("https://worker.internal/api", "https://worker.internal/api")]
    public void ResolveFinancialWorkerBaseUrl_UsesConfiguredValueOrFallback(string? configuredBaseUrl, string expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FinancialWorker:BaseUrl"] = configuredBaseUrl
            })
            .Build();

        var result = StocksModule.ResolveFinancialWorkerBaseUrl(configuration);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("sh600000", "600000")]
    [InlineData("SZ000001", "000001")]
    [InlineData("600519", "600519")]
    public void NormalizeFinancialWorkerSymbol_StripsExchangePrefixForWorkerCalls(string input, string expected)
    {
        var result = StocksModule.NormalizeFinancialWorkerSymbol(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("http://localhost:5120", "api/config", "http://localhost:5120/api/config")]
    [InlineData("http://localhost:5120/api", "api/config", "http://localhost:5120/api/config")]
    [InlineData("http://localhost:5120/api", "health", "http://localhost:5120/api/health")]
    public void BuildFinancialWorkerUri_AvoidsDuplicatingApiSegment(string baseUrl, string relativePath, string expected)
    {
        var result = StocksModule.BuildFinancialWorkerUri(baseUrl, relativePath);

        Assert.Equal(expected, result.ToString());
    }

    [Theory]
    [InlineData("api/collect/600519", 60)]
    [InlineData("/api/collect/000001", 60)]
    [InlineData("health", 10)]
    [InlineData("api/config", 10)]
    [InlineData("api/embedding/backfill", 1800)]
    public void ResolveFinancialWorkerProxyTimeout_UsesLongerTimeoutOnlyForCollect(string relativePath, int expectedSeconds)
    {
        var result = StocksModule.ResolveFinancialWorkerProxyTimeout(relativePath);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task MapEndpoints_StockMarketContextRoute_ReturnsBadRequestForMissingOrBlankSymbol(string? symbol)
    {
        var result = await StocksModule.GetLatestMarketContextResultAsync(symbol, new RecordingStockMarketContextService(), CancellationToken.None);

        var response = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal("symbol 不能为空", document.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task MapEndpoints_StockMarketContextRoute_ReturnsNotFoundWhenContextMissing()
    {
        var marketContextService = new RecordingStockMarketContextService();
        var result = await StocksModule.GetLatestMarketContextResultAsync("sh600519", marketContextService, CancellationToken.None);

        var response = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal("sh600519", marketContextService.LastSymbol);
        Assert.True(string.IsNullOrWhiteSpace(response.Body));
    }

    [Fact]
    public async Task MapEndpoints_StockMarketContextRoute_ReturnsLatestContextPayload()
    {
        var expected = new StockMarketContextDto("主升", 82m, "白酒", "白酒", "BK0896", 91m, 0.85m, "积极执行", false, true);
        var marketContextService = new RecordingStockMarketContextService(expected);
        var result = await StocksModule.GetLatestMarketContextResultAsync("  sh600519  ", marketContextService, CancellationToken.None);

        var response = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("sh600519", marketContextService.LastSymbol);

        var payload = JsonSerializer.Deserialize<StockMarketContextDto>(response.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(payload);
        Assert.Equal(expected.StageLabel, payload!.StageLabel);
        Assert.Equal(expected.StageConfidence, payload.StageConfidence);
        Assert.Equal(expected.StockSectorName, payload.StockSectorName);
        Assert.Equal(expected.MainlineSectorName, payload.MainlineSectorName);
        Assert.Equal(expected.SectorCode, payload.SectorCode);
        Assert.Equal(expected.MainlineScore, payload.MainlineScore);
        Assert.Equal(expected.SuggestedPositionScale, payload.SuggestedPositionScale);
        Assert.Equal(expected.ExecutionFrequencyLabel, payload.ExecutionFrequencyLabel);
        Assert.Equal(expected.CounterTrendWarning, payload.CounterTrendWarning);
        Assert.Equal(expected.IsMainlineAligned, payload.IsMainlineAligned);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ValidateTradingPlanDraftRequest_ReturnsBadRequestForInvalidAnalysisHistoryId(long analysisHistoryId)
    {
        var result = StocksModule.ValidateTradingPlanDraftRequest(new Modules.Stocks.Models.TradingPlanDraftRequestDto("sh600519", analysisHistoryId));

        Assert.NotNull(result);

        var response = await ExecuteResultAsync(result!);

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal("analysisHistoryId 无效", document.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task GetTradingPlansResultAsync_WhenEnrichmentFails_StillReturnsPlanListPayload()
    {
        var createdAt = new DateTime(2026, 4, 21, 1, 2, 3, DateTimeKind.Utc);
        var plans = new[]
        {
            new TradingPlan
            {
                Id = 101,
                PlanKey = "plan-101",
                Title = "平安银行计划",
                Symbol = "sz000001",
                Name = "平安银行",
                Direction = TradingPlanDirection.Long,
                Status = TradingPlanStatus.Pending,
                SourceAgent = "manual",
                CreatedAt = createdAt,
                UpdatedAt = createdAt
            },
            new TradingPlan
            {
                Id = 102,
                PlanKey = "plan-102",
                Title = "万科A计划",
                Symbol = "sz000002",
                Name = "万科A",
                Direction = TradingPlanDirection.Long,
                Status = TradingPlanStatus.Pending,
                SourceAgent = "manual",
                CreatedAt = createdAt,
                UpdatedAt = createdAt
            }
        };

        var result = await StocksModule.GetTradingPlansResultAsync(
            null,
            50,
            new StubTradingPlanService(plans),
            new ThrowingStockMarketContextService(new Dictionary<string, StockMarketContextDto?>(StringComparer.OrdinalIgnoreCase)
            {
                ["sz000001"] = new StockMarketContextDto("主升", 80m, "银行", "银行", "BKYH", 88m, 0.7m, "积极执行", false, true)
            },
            new[] { "sz000002" }),
            new ThrowingTradeExecutionInsightService(),
            NullLogger.Instance,
            CancellationToken.None);

        var response = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);

        using var document = JsonDocument.Parse(response.Body);
        var items = document.RootElement;
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal(101, items[0].GetProperty("id").GetInt64());
        Assert.Equal("sz000001", items[0].GetProperty("symbol").GetString());
        Assert.Equal("主升", items[0].GetProperty("currentMarketContext").GetProperty("stageLabel").GetString());
        Assert.Equal(102, items[1].GetProperty("id").GetInt64());
        Assert.Equal("sz000002", items[1].GetProperty("symbol").GetString());
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("currentMarketContext").ValueKind);
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("executionSummary").ValueKind);
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("executionSummary").ValueKind);
    }

    [Fact]
    public void GetCollectionLogs_MapsLiteDbDocumentsToStableCamelCaseContract()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "stocks-financial-logs-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var runtimePaths = CreateRuntimePaths(dataRoot);
            runtimePaths.EnsureWritableDirectories();

            var dbPath = Path.Combine(runtimePaths.AppDataPath, "financial-data.db");
            var timestamp = new DateTime(2026, 4, 7, 10, 11, 12, DateTimeKind.Utc);
            var id = ObjectId.NewObjectId();

            using (var db = new LiteDatabase($"Filename={dbPath};Connection=shared"))
            {
                db.GetCollection("collection_logs").Insert(new BsonDocument
                {
                    ["_id"] = id,
                    ["Symbol"] = "sh600519",
                    ["CollectionType"] = "FinancialReport",
                    ["Channel"] = "emweb",
                    ["IsDegraded"] = true,
                    ["DegradeReason"] = "fallback",
                    ["Success"] = true,
                    ["ErrorMessage"] = "worker-note",
                    ["DurationMs"] = 1234L,
                    ["RecordCount"] = 4,
                    ["Timestamp"] = timestamp
                });
            }

            FinancialCollectionLogEntry entry;
            using (var service = new FinancialDataReadService(runtimePaths, NullLogger<FinancialDataReadService>.Instance))
            {
                entry = Assert.Single(service.GetCollectionLogs("600519", 10));
            }

            Assert.Equal(id.ToString(), entry.Id);
            Assert.Equal("sh600519", entry.Symbol);
            Assert.Equal("FinancialReport", entry.CollectionType);
            Assert.Equal("emweb", entry.Channel);
            Assert.True(entry.IsDegraded);
            Assert.Equal("fallback", entry.DegradeReason);
            Assert.True(entry.Success);
            Assert.Equal("worker-note", entry.ErrorMessage);
            Assert.Equal(1234L, entry.DurationMs);
            Assert.Equal(4, entry.RecordCount);
            Assert.Equal(timestamp, entry.Timestamp?.ToUniversalTime());

            var payload = JsonSerializer.Serialize(entry, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            Assert.Equal("sh600519", root.GetProperty("symbol").GetString());
            Assert.Equal("FinancialReport", root.GetProperty("collectionType").GetString());
            Assert.Equal("emweb", root.GetProperty("channel").GetString());
            Assert.True(root.GetProperty("isDegraded").GetBoolean());
            Assert.Equal("fallback", root.GetProperty("degradeReason").GetString());
            Assert.True(root.GetProperty("success").GetBoolean());
            Assert.Equal("worker-note", root.GetProperty("errorMessage").GetString());
            Assert.Equal(4, root.GetProperty("recordCount").GetInt32());
            Assert.Equal(1234L, root.GetProperty("durationMs").GetInt64());
            Assert.Equal(timestamp, root.GetProperty("timestamp").GetDateTimeOffset().UtcDateTime);
            Assert.False(root.TryGetProperty("Symbol", out _));
            Assert.False(root.TryGetProperty("Channel", out _));
            Assert.False(root.TryGetProperty("RecordCount", out _));
        }
        finally
        {
            if (Directory.Exists(dataRoot))
            {
                Directory.Delete(dataRoot, recursive: true);
            }
        }
    }

    private static TimeSpan GetHttpClientTimeout(object instance)
    {
        var field = instance.GetType().GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var httpClient = Assert.IsType<HttpClient>(field!.GetValue(instance));
        return httpClient.Timeout;
    }

    private static AppRuntimePaths CreateRuntimePaths(string dataRoot)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:DataRootPath"] = dataRoot
            })
            .Build();

        return new AppRuntimePaths(new FakeHostEnvironment { ContentRootPath = dataRoot }, configuration);
    }

    private static async Task<(int StatusCode, string Body)> ExecuteResultAsync(IResult result)
    {
        using var serviceProvider = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        httpContext.Response.Body = new MemoryStream();

        await result.ExecuteAsync(httpContext);

        httpContext.Response.Body.Position = 0;
        using var reader = new StreamReader(httpContext.Response.Body);
        return (httpContext.Response.StatusCode, await reader.ReadToEndAsync());
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "SimplerJiangAiAgent.Api.Tests";
        public string ContentRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "stocks-module-http-client-tests", Guid.NewGuid().ToString("N"));
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class RecordingStockMarketContextService : IStockMarketContextService
    {
        private readonly StockMarketContextDto? _result;

        public RecordingStockMarketContextService(StockMarketContextDto? result = null)
        {
            _result = result;
        }

        public string? LastSymbol { get; private set; }

        public Task<StockMarketContextDto?> GetLatestAsync(string symbol, CancellationToken cancellationToken = default)
        {
            LastSymbol = symbol;
            return Task.FromResult(_result);
        }

        public Task<StockMarketContextDto?> GetLatestAsync(string symbol, string? sectorNameHint, CancellationToken cancellationToken = default)
        {
            LastSymbol = symbol;
            return Task.FromResult(_result);
        }
    }

    private sealed class StubTradingPlanService : ITradingPlanService
    {
        private readonly IReadOnlyList<TradingPlan> _plans;

        public StubTradingPlanService(IReadOnlyList<TradingPlan> plans)
        {
            _plans = plans;
        }

        public Task<IReadOnlyList<TradingPlan>> GetListAsync(string? symbol, int take = 20, CancellationToken cancellationToken = default)
            => Task.FromResult(_plans);

        public Task<TradingPlan?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TradingPlanSaveResult> CreateAsync(TradingPlanCreateDto request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TradingPlan?> UpdateAsync(long id, TradingPlanUpdateDto request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TradingPlan?> CancelAsync(long id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TradingPlan?> ResumeAsync(long id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class ThrowingTradeExecutionInsightService : ITradeExecutionInsightService
    {
        public Task<IReadOnlyDictionary<long, TradingPlanRuntimeInsightDto>> GetPlanInsightsAsync(IReadOnlyCollection<TradingPlan> plans, bool useLiveQuote = false, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("insight enrichment exploded");

        public Task<TradingPlanRuntimeInsightDto?> GetPlanInsightAsync(TradingPlan plan, bool useLiveQuote = true, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TradingPlanPortfolioSummaryDto> GetPortfolioSummaryAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task EnrichTradeExecutionAsync(TradeExecution trade, bool useLiveQuote = true, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class ThrowingStockMarketContextService : IStockMarketContextService
    {
        private readonly IReadOnlyDictionary<string, StockMarketContextDto?> _results;
        private readonly HashSet<string> _symbolsThatThrow;

        public ThrowingStockMarketContextService(IReadOnlyDictionary<string, StockMarketContextDto?> results, IEnumerable<string> symbolsThatThrow)
        {
            _results = results;
            _symbolsThatThrow = new HashSet<string>(symbolsThatThrow, StringComparer.OrdinalIgnoreCase);
        }

        public Task<StockMarketContextDto?> GetLatestAsync(string symbol, CancellationToken cancellationToken = default)
            => GetLatestAsync(symbol, null, cancellationToken);

        public Task<StockMarketContextDto?> GetLatestAsync(string symbol, string? sectorNameHint, CancellationToken cancellationToken = default)
        {
            if (_symbolsThatThrow.Contains(symbol))
            {
                throw new InvalidOperationException($"market context failed for {symbol}");
            }

            return Task.FromResult(_results.TryGetValue(symbol, out var result) ? result : null);
        }
    }
}