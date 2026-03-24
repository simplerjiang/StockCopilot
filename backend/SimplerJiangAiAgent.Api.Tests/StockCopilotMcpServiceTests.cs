using System.Net;
using Microsoft.Extensions.Options;
using SimplerJiangAiAgent.Api.Modules.Market.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class StockCopilotMcpServiceTests
{
    [Fact]
    public async Task GetKlineAsync_ShouldWrapDataInLocalRequiredEnvelope()
    {
        var service = CreateService();

        var result = await service.GetKlineAsync("sh600000", "day", 60, null, "task-1");

        Assert.Equal("StockKlineMcp", result.ToolName);
        Assert.Equal("task-1", result.TaskId);
        Assert.Equal("local_required", result.Meta.PolicyClass);
        Assert.Equal("sh600000", result.Data.Symbol);
        Assert.NotEmpty(result.Features);
    }

    [Fact]
    public async Task GetNewsAsync_ShouldExposeLocalEvidenceObjects()
    {
        var service = CreateService();

        var result = await service.GetNewsAsync("sh600000", "stock", "task-news");

        Assert.Equal("StockNewsMcp", result.ToolName);
        Assert.Single(result.Evidence);
        Assert.Equal("上交所公告", result.Evidence[0].Source);
        Assert.Equal("local_required", result.Meta.PolicyClass);
    }

    [Fact]
    public async Task GetNewsAsync_ShouldStripNavigationNoiseFromEvidenceSnippet()
    {
        var service = CreateService(queryTool: new FakeQueryLocalFactDatabaseTool(
            excerpt: "财经 焦点 股票 新股 期指 期权 行情 数据 全球 美股 港股 基金 这是一条真正的公告摘要，说明本次事项未见新增重大风险。",
            summary: "财经 焦点 股票 新股 期指 期权 行情 数据 全球 美股 港股 基金"));

        var result = await service.GetNewsAsync("sh600000", "stock", "task-news-clean");

        var evidence = Assert.Single(result.Evidence);
        Assert.Equal("这是一条真正的公告摘要，说明本次事项未见新增重大风险。", evidence.Excerpt);
        Assert.Equal(evidence.Excerpt, evidence.Summary);
    }

    [Fact]
    public async Task SearchAsync_WhenProviderDisabled_ShouldReturnExternalGatedDegradedEnvelope()
    {
        var service = CreateService(new StockCopilotSearchOptions { Enabled = false, Provider = "tavily" });

        var result = await service.SearchAsync("浦发银行 最新公告", true, "task-search");

        Assert.Equal("StockSearchMcp", result.ToolName);
        Assert.Equal("external_gated", result.Meta.PolicyClass);
        Assert.Contains("external_search_unavailable", result.DegradedFlags);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public async Task GetStrategyAsync_ShouldTreatEqualClosesAsTdContinuation()
    {
        var closes = new decimal[] { 10m, 11m, 12m, 13m, 14m, 15m, 16m, 17m, 18m, 15m, 16m, 18m, 19m };
        var service = CreateService(dataService: new FakeStockDataService(
            kLines: closes.Select((close, index) => new KLinePointDto(
                new DateTime(2026, 4, 1).AddDays(index),
                close,
                close,
                close + 0.2m,
                close - 0.2m,
                1000m + index * 50m)).ToArray()));

        var result = await service.GetStrategyAsync("sh600000", "day", 60, null, new[] { "td" }, "task-td");

        var tdSignal = Assert.Single(result.Data.Signals);
        Assert.Equal("td", tdSignal.Strategy);
        Assert.Equal("setup_up", tdSignal.Signal);
        Assert.Equal(9m, tdSignal.NumericValue);
    }

    private static StockCopilotMcpService CreateService(StockCopilotSearchOptions? options = null, IStockDataService? dataService = null, IQueryLocalFactDatabaseTool? queryTool = null)
    {
        return new StockCopilotMcpService(
            dataService ?? new FakeStockDataService(),
            queryTool ?? new FakeQueryLocalFactDatabaseTool(),
            new FakeStockMarketContextService(),
            new StockAgentFeatureEngineeringService(),
            new FakeHttpClientFactory(),
            Options.Create(options ?? new StockCopilotSearchOptions { Enabled = false, Provider = "tavily" }));
    }

    private sealed class FakeStockDataService : IStockDataService
    {
        private readonly IReadOnlyList<KLinePointDto> _kLines;
        private readonly IReadOnlyList<MinuteLinePointDto> _minuteLines;

        public FakeStockDataService(
            IReadOnlyList<KLinePointDto>? kLines = null,
            IReadOnlyList<MinuteLinePointDto>? minuteLines = null)
        {
            _kLines = kLines ?? Enumerable.Range(0, 30)
                .Select(index => new KLinePointDto(new DateTime(2026, 2, 1).AddDays(index), 9.8m + index * 0.02m, 9.9m + index * 0.02m, 10m + index * 0.02m, 9.7m + index * 0.02m, 1000 + index * 20))
                .ToArray();
            _minuteLines = minuteLines ?? new[]
            {
                new MinuteLinePointDto(new DateOnly(2026, 3, 21), new TimeSpan(9, 30, 0), 10.2m, 10.2m, 100),
                new MinuteLinePointDto(new DateOnly(2026, 3, 21), new TimeSpan(10, 0, 0), 10.3m, 10.25m, 180),
                new MinuteLinePointDto(new DateOnly(2026, 3, 21), new TimeSpan(14, 50, 0), 10.45m, 10.32m, 220)
            };
        }

        public Task<StockQuoteDto> GetQuoteAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new StockQuoteDto(symbol, "浦发银行", 10.5m, 0.2m, 1.9m, 13m, 8m, 10.7m, 10.1m, 0.1m, new DateTime(2026, 3, 21, 10, 0, 0), Array.Empty<StockNewsDto>(), Array.Empty<StockIndicatorDto>(), 320_000_000_000m, 2.8m, 100000, "银行"));
        }

        public Task<MarketIndexDto> GetMarketIndexAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new MarketIndexDto(symbol, "上证指数", 3200m, 10m, 0.3m, new DateTime(2026, 3, 21, 10, 0, 0)));
        }

        public Task<IReadOnlyList<KLinePointDto>> GetKLineAsync(string symbol, string interval, int count, string? source = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_kLines);
        }

        public Task<IReadOnlyList<MinuteLinePointDto>> GetMinuteLineAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_minuteLines);
        }

        public Task<IReadOnlyList<IntradayMessageDto>> GetIntradayMessagesAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<IntradayMessageDto> data = new[]
            {
                new IntradayMessageDto("浦发银行公告", "上交所公告", new DateTime(2026, 3, 21, 8, 30, 0), "https://example.com/a")
            };
            return Task.FromResult(data);
        }
    }

    private sealed class FakeQueryLocalFactDatabaseTool : IQueryLocalFactDatabaseTool
    {
        private readonly string? _excerpt;
        private readonly string? _summary;

        public FakeQueryLocalFactDatabaseTool(string? excerpt = "公告摘要", string? summary = "公告摘要")
        {
            _excerpt = excerpt;
            _summary = summary;
        }

        public Task<LocalFactPackageDto> QueryAsync(string symbol, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LocalFactPackageDto(
                symbol,
                "浦发银行",
                "银行",
                new[]
                {
                    new LocalNewsItemDto(1, "stock_news:1", "浦发银行公告", null, "上交所公告", "announcement", "announcement", "利好", new DateTime(2026, 3, 21, 8, 30, 0), new DateTime(2026, 3, 21, 8, 31, 0), "https://example.com/a", _excerpt, _summary, "local_fact", "summary_only", new DateTime(2026, 3, 21, 8, 31, 0), "个股:浦发银行", new[] { "公告" })
                },
                Array.Empty<LocalNewsItemDto>(),
                Array.Empty<LocalNewsItemDto>(),
                new DateTime(2026, 3, 21, 7, 0, 0),
                new[] { new LocalFundamentalFactDto("机构目标价", "11.5", "东方财富") }));
        }

        public Task<LocalNewsBucketDto> QueryLevelAsync(string symbol, string level, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LocalNewsBucketDto(symbol, level, "银行", new[]
            {
                new LocalNewsItemDto(1, "stock_news:1", "浦发银行公告", null, "上交所公告", "announcement", level, "利好", new DateTime(2026, 3, 21, 8, 30, 0), new DateTime(2026, 3, 21, 8, 31, 0), "https://example.com/a", _excerpt, _summary, "local_fact", "summary_only", new DateTime(2026, 3, 21, 8, 31, 0), "个股:浦发银行", new[] { "公告" })
            }));
        }

        public Task<LocalNewsBucketDto> QueryMarketAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LocalNewsBucketDto("market", "market", null, Array.Empty<LocalNewsItemDto>()));
        }

        public Task<LocalNewsArchivePageDto> QueryArchiveAsync(string? keyword, string? level, string? sentiment, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LocalNewsArchivePageDto(page, pageSize, 0, keyword, level, sentiment, Array.Empty<LocalNewsArchiveItemDto>()));
        }
    }

    private sealed class FakeStockMarketContextService : IStockMarketContextService
    {
        public Task<StockMarketContextDto?> GetLatestAsync(string symbol, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<StockMarketContextDto?>(new StockMarketContextDto("主升", 72m, "银行", "银行", "BK001", 80m, 0.8m, "积极执行", false, true));
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new FakeHttpMessageHandler()) { BaseAddress = new Uri("https://example.com/") };
        }
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"results\":[]}")
            });
        }
    }
}