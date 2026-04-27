using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SimplerJiangAiAgent.Api.Infrastructure.Jobs;
using SimplerJiangAiAgent.Api.Modules.Stocks.Contracts;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public class FinancialReportsEndpointTests : IClassFixture<FinancialReportsEndpointTests.Factory>
{
    private readonly Factory _factory;

    public FinancialReportsEndpointTests(Factory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetFinancialReports_WithFullQuery_BindsParametersAndReturns200()
    {
        var stub = new StubFinancialDataReadService();
        _factory.Stub = stub;
        var client = _factory.CreateClient();

        var response = await client.GetAsync(
            "/api/stocks/financial/reports?symbol=600519&reportType=annual&page=1&pageSize=10&sort=reportDate%20desc");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("total", out _), "response missing 'total'");
        Assert.True(body.TryGetProperty("page", out _), "response missing 'page'");
        Assert.True(body.TryGetProperty("pageSize", out _), "response missing 'pageSize'");
        Assert.True(body.TryGetProperty("items", out _), "response missing 'items'");

        Assert.NotNull(stub.LastQuery);
        Assert.Equal("600519", stub.LastQuery!.Symbol);
        Assert.Equal("annual", stub.LastQuery.ReportType);
        Assert.Equal(1, stub.LastQuery.Page);
        Assert.Equal(10, stub.LastQuery.PageSize);
        Assert.Equal("reportDate desc", stub.LastQuery.Sort);
    }

    [Theory]
    [InlineData("/api/news?symbol=not-a-stock")]
    [InlineData("/api/stocks/financial/summary/not-a-stock")]
    [InlineData("/api/stocks/financial/trend/not-a-stock")]
    public async Task SymbolRoutes_WithInvalidSymbol_ReturnInvalidSymbolBadRequest(string path)
    {
        _factory.Stub = new StubFinancialDataReadService();
        _factory.LocalFactIngestionStub = new StubLocalFactIngestionService();
        _factory.LocalFactQueryStub = new StubQueryLocalFactDatabaseTool();
        var client = _factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_symbol", body.GetProperty("error").GetString());
        Assert.Equal("无效的股票代码格式", body.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("600519")]
    [InlineData("sh600519")]
    public async Task LocalNews_WithValidPlainAndPrefixedSymbols_Returns200(string symbol)
    {
        var ingestionStub = new StubLocalFactIngestionService();
        var queryStub = new StubQueryLocalFactDatabaseTool();
        _factory.LocalFactIngestionStub = ingestionStub;
        _factory.LocalFactQueryStub = queryStub;
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/news?symbol={symbol}&level=stock");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(symbol, ingestionStub.LastEnsureFreshSymbol);
        Assert.Equal(symbol, queryStub.LastLevelSymbol);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(symbol, body.GetProperty("symbol").GetString());
        Assert.Equal("stock", body.GetProperty("level").GetString());
    }

    [Theory]
    [InlineData("summary", "600519")]
    [InlineData("summary", "sh600519")]
    [InlineData("trend", "600519")]
    [InlineData("trend", "sh600519")]
    public async Task FinancialSummaryAndTrend_WithValidPlainAndPrefixedSymbols_Return200(string endpoint, string symbol)
    {
        var stub = new StubFinancialDataReadService();
        _factory.Stub = stub;
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/stocks/financial/{endpoint}/{symbol}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(symbol, body.GetProperty("symbol").GetString());

        if (endpoint == "summary")
        {
            Assert.Equal(symbol, stub.LastReportSummarySymbol);
        }
        else
        {
            Assert.Equal(symbol, stub.LastTrendSummarySymbol);
        }
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public StubFinancialDataReadService? Stub { get; set; }
        public StubLocalFactIngestionService LocalFactIngestionStub { get; set; } = new();
        public StubQueryLocalFactDatabaseTool LocalFactQueryStub { get; set; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var dataRoot = Path.Combine(Path.GetTempPath(), "sjai-tests", nameof(FinancialReportsEndpointTests), Guid.NewGuid().ToString("N"));

            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:DataRootPath"] = dataRoot
                });
            });
            builder.ConfigureServices(services =>
            {
                ApiTestDatabaseIsolation.UseIsolatedSqlite(services, dataRoot);
                services.RemoveAll<IHostedService>();

                var existing = services.Where(d => d.ServiceType == typeof(IFinancialDataReadService)).ToList();
                foreach (var d in existing)
                {
                    services.Remove(d);
                }

                services.AddScoped<IFinancialDataReadService>(_ => Stub ?? new StubFinancialDataReadService());

                var existingIngestion = services.Where(d => d.ServiceType == typeof(ILocalFactIngestionService)).ToList();
                foreach (var d in existingIngestion)
                {
                    services.Remove(d);
                }

                var existingQueryTool = services.Where(d => d.ServiceType == typeof(IQueryLocalFactDatabaseTool)).ToList();
                foreach (var d in existingQueryTool)
                {
                    services.Remove(d);
                }

                services.AddScoped<ILocalFactIngestionService>(_ => LocalFactIngestionStub);
                services.AddScoped<IQueryLocalFactDatabaseTool>(_ => LocalFactQueryStub);
            });
        }
    }

    public sealed class StubFinancialDataReadService : IFinancialDataReadService
    {
        public FinancialReportListQuery? LastQuery { get; private set; }
        public string? LastReportSummarySymbol { get; private set; }
        public string? LastTrendSummarySymbol { get; private set; }

        public SimplerJiangAiAgent.Api.Modules.Stocks.Contracts.PagedResult<FinancialReportListItem> ListReports(FinancialReportListQuery query)
        {
            LastQuery = query;
            return new SimplerJiangAiAgent.Api.Modules.Stocks.Contracts.PagedResult<FinancialReportListItem>(Array.Empty<FinancialReportListItem>(), 0, query.Page, query.PageSize);
        }

        public List<Dictionary<string, object?>> GetReports(string symbol, int limit = 20) => new();
        public List<Dictionary<string, object?>> GetIndicators(string symbol, int limit = 20) => new();
        public List<Dictionary<string, object?>> GetDividends(string symbol) => new();
        public List<Dictionary<string, object?>> GetMarginTrading(string symbol, int limit = 100) => new();
        public List<FinancialCollectionLogEntry> GetCollectionLogs(string? symbol = null, int limit = 50) => new();
        public Dictionary<string, object?>? GetConfig() => null;
        public FinancialReportSummary? GetReportSummary(string symbol, int periods = 4)
        {
            LastReportSummarySymbol = symbol;
            return new FinancialReportSummary { Symbol = symbol };
        }

        public FinancialTrendSummary? GetTrendSummary(string symbol, int periods = 8)
        {
            LastTrendSummarySymbol = symbol;
            return new FinancialTrendSummary { Symbol = symbol };
        }

        public FinancialReportDetail? GetReportById(string id) => null;
        public void Dispose() { }
    }

    public sealed class StubLocalFactIngestionService : ILocalFactIngestionService
    {
        public string? LastEnsureFreshSymbol { get; private set; }

        public Task SyncAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnsureMarketFreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task EnsureFreshAsync(string symbol, CancellationToken cancellationToken = default)
        {
            LastEnsureFreshSymbol = symbol;
            return Task.CompletedTask;
        }
    }

    public sealed class StubQueryLocalFactDatabaseTool : IQueryLocalFactDatabaseTool
    {
        public string? LastLevelSymbol { get; private set; }

        public Task<LocalFactPackageDto> QueryAsync(string symbol, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LocalFactPackageDto(symbol, null, null, Array.Empty<LocalNewsItemDto>(), Array.Empty<LocalNewsItemDto>(), Array.Empty<LocalNewsItemDto>(), null, Array.Empty<LocalFundamentalFactDto>()));
        }

        public Task<LocalNewsBucketDto> QueryLevelAsync(string symbol, string level, CancellationToken cancellationToken = default)
        {
            LastLevelSymbol = symbol;
            return Task.FromResult(new LocalNewsBucketDto(symbol, level, null, Array.Empty<LocalNewsItemDto>()));
        }

        public Task<LocalNewsBucketDto> QueryMarketAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LocalNewsBucketDto(string.Empty, "market", null, Array.Empty<LocalNewsItemDto>()));
        }

        public Task<LocalNewsArchivePageDto> QueryArchiveAsync(string? keyword, string? level, string? sentiment, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LocalNewsArchivePageDto(page, pageSize, 0, keyword, level, sentiment, Array.Empty<LocalNewsArchiveItemDto>()));
        }
    }
}
