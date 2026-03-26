using System.Net;
using Microsoft.Extensions.Options;
using SimplerJiangAiAgent.Api.Modules.Market.Models;
using SimplerJiangAiAgent.Api.Modules.Market.Services;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class StockCopilotMcpServiceTests
{
    [Fact]
    public async Task GetCompanyOverviewAsync_ShouldExposeQuoteAndProfileEvidence()
    {
        var service = CreateService();

        var result = await service.GetCompanyOverviewAsync("sh600000", "task-company-overview");

        Assert.Equal(StockMcpToolNames.CompanyOverview, result.ToolName);
        Assert.Equal("local_required", result.RolePolicyClass);
        Assert.Equal("local", result.SourceTier);
        Assert.Equal("sh600000", result.Data.Symbol);
        Assert.Equal("浦发银行", result.Data.Name);
        Assert.Equal("银行", result.Data.SectorName);
        Assert.True(result.Data.FundamentalFactCount > 0);
        Assert.NotEmpty(result.Evidence);
    }

    [Fact]
    public async Task GetCompanyOverviewAsync_ShouldExposeMainBusinessAndBusinessScope()
    {
        var service = CreateService(
            queryTool: new FakeQueryLocalFactDatabaseTool(
                fundamentalFacts: new[]
                {
                    new LocalFundamentalFactDto("主营业务", "商业银行及相关金融服务", "东方财富公司概况"),
                    new LocalFundamentalFactDto("经营范围", "吸收公众存款、发放贷款、办理结算", "东方财富公司概况")
                }));

        var result = await service.GetCompanyOverviewAsync("sh600000", "task-company-overview-business");

        Assert.Equal("商业银行及相关金融服务", result.Data.MainBusiness);
        Assert.Equal("吸收公众存款、发放贷款、办理结算", result.Data.BusinessScope);
        Assert.Contains(result.Evidence, item => item.Summary?.Contains("主营业务=商业银行及相关金融服务", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task GetProductAsync_ShouldExposeBusinessScopeIndustryAndRegion()
    {
        var snapshot = new StockFundamentalSnapshotDto(
            DateTime.UtcNow.AddMinutes(-25),
            new[]
            {
                new StockFundamentalFactDto("经营范围", "新能源汽车及动力电池研发、生产、销售", "东方财富公司概况"),
                new StockFundamentalFactDto("所属行业", "汽车整车", "东方财富公司概况"),
                new StockFundamentalFactDto("证监会行业", "汽车制造业", "东方财富公司概况"),
                new StockFundamentalFactDto("所属地区", "广东", "东方财富公司概况"),
                new StockFundamentalFactDto("营业收入", "6023亿元", "东方财富最新财报")
            });
        var service = CreateService(
            queryTool: new EmptyQueryLocalFactDatabaseTool(),
            fundamentalSnapshotService: new StaticFundamentalSnapshotService(snapshot));

        var result = await service.GetProductAsync("sz002594", "task-product-success");

        Assert.Equal(StockMcpToolNames.Product, result.ToolName);
        Assert.Equal("local_required", result.RolePolicyClass);
        Assert.Equal("local", result.SourceTier);
        Assert.Equal("新能源汽车及动力电池研发、生产、销售", result.Data.BusinessScope);
        Assert.Equal("汽车整车", result.Data.Industry);
        Assert.Equal("汽车制造业", result.Data.CsrcIndustry);
        Assert.Equal("广东", result.Data.Region);
        Assert.Equal(4, result.Data.FactCount);
        Assert.DoesNotContain(result.Data.Facts, item => item.Label == "营业收入");
        Assert.Contains(result.Evidence, item => item.Level == "product");
    }

    [Fact]
    public async Task GetProductAsync_WhenBusinessFactsInsufficient_ShouldReturnNoProductFactsEnvelope()
    {
        var snapshot = new StockFundamentalSnapshotDto(
            DateTime.UtcNow.AddMinutes(-25),
            new[]
            {
                new StockFundamentalFactDto("所属行业", "银行", "东方财富公司概况"),
                new StockFundamentalFactDto("所属地区", "上海", "东方财富公司概况")
            });
        var service = CreateService(
            queryTool: new EmptyQueryLocalFactDatabaseTool(),
            fundamentalSnapshotService: new StaticFundamentalSnapshotService(snapshot));

        var result = await service.GetProductAsync("sh600000", "task-product-empty");

        Assert.Equal("no_product_facts", result.ErrorCode);
        Assert.Contains("no_product_facts", result.DegradedFlags);
        Assert.Null(result.Data.MainBusiness);
        Assert.Null(result.Data.BusinessScope);
        Assert.Equal(2, result.Data.FactCount);
        Assert.Contains(result.Data.Facts, item => item.Label == "所属行业");
    }

    [Fact]
    public async Task GetFundamentalsAsync_ShouldExposeStructuredFacts()
    {
        var service = CreateService();

        var result = await service.GetFundamentalsAsync("sh600000", "task-fundamentals");

        Assert.Equal(StockMcpToolNames.Fundamentals, result.ToolName);
        Assert.Equal("local_required", result.RolePolicyClass);
        Assert.Equal("local", result.SourceTier);
        Assert.True(result.Data.FactCount > 0);
        Assert.Contains(result.Data.Facts, item => item.Label == "机构目标价");
        Assert.All(result.Evidence, item => Assert.Equal("fundamental", item.Level));
    }

    [Fact]
    public async Task GetFundamentalsAsync_WhenLocalFactsSparse_ShouldMergeSnapshotFacts()
    {
        var snapshot = new StockFundamentalSnapshotDto(
            DateTime.UtcNow.AddMinutes(-20),
            new[]
            {
                new StockFundamentalFactDto("营业收入", "1860亿元", "东方财富最新财报"),
                new StockFundamentalFactDto("归属净利润", "412亿元", "东方财富最新财报"),
                new StockFundamentalFactDto("ROE", "11.2%", "东方财富最新财报")
            });
        var service = CreateService(
            queryTool: new FakeQueryLocalFactDatabaseTool(
                fundamentalFacts: new[]
                {
                    new LocalFundamentalFactDto("机构目标价", "11.5", "东方财富")
                }),
            fundamentalSnapshotService: new StaticFundamentalSnapshotService(snapshot));

        var result = await service.GetFundamentalsAsync("sh600000", "task-fundamentals-merge");

        Assert.Contains(result.Data.Facts, item => item.Label == "机构目标价");
        Assert.Contains(result.Data.Facts, item => item.Label == "营业收入");
        Assert.Contains(result.Data.Facts, item => item.Label == "归属净利润");
        Assert.Contains(result.Data.Facts, item => item.Label == "ROE");
    }

    [Fact]
    public async Task GetFundamentalsAsync_WhenNoFactsAvailable_ShouldReturnDegradedEnvelope()
    {
        var service = CreateService(
            queryTool: new EmptyQueryLocalFactDatabaseTool(),
            fundamentalSnapshotService: new NullFundamentalSnapshotService());

        var result = await service.GetFundamentalsAsync("sh600000", "task-fundamentals-empty");

        Assert.Equal("no_fundamental_facts", result.ErrorCode);
        Assert.Equal("no_data", result.FreshnessTag);
        Assert.Empty(result.Data.Facts);
        Assert.Contains("no_fundamental_facts", result.DegradedFlags);
    }

    [Fact]
    public async Task GetShareholderAsync_ShouldExposeShareholderFacts()
    {
        var service = CreateService();

        var result = await service.GetShareholderAsync("sh600000", "task-shareholder");

        Assert.Equal(StockMcpToolNames.Shareholder, result.ToolName);
        Assert.Equal("local_required", result.RolePolicyClass);
        Assert.Equal("local", result.SourceTier);
        Assert.Equal(100000, result.Data.ShareholderCount);
        Assert.Contains(result.Data.Facts, item => item.Label.Contains("股东", StringComparison.Ordinal));
        Assert.All(result.Evidence, item => Assert.Equal("shareholder", item.Level));
    }

    [Fact]
    public async Task GetShareholderAsync_WhenOnlyLiveSnapshotAvailable_ShouldReturnSnapshotFacts()
    {
        var liveSnapshot = new StockFundamentalSnapshotDto(
            DateTime.UtcNow.AddHours(-1),
            new[]
            {
                new StockFundamentalFactDto("股东户数", "125000", "东方财富股东研究")
            });
        var service = CreateService(
            queryTool: new EmptyQueryLocalFactDatabaseTool(),
            fundamentalSnapshotService: new StaticFundamentalSnapshotService(liveSnapshot));

        var result = await service.GetShareholderAsync("sh600000", "task-shareholder-live");

        Assert.Equal(100000, result.Data.ShareholderCount);
        Assert.Single(result.Data.Facts);
        Assert.Equal("fresh", result.FreshnessTag);
        Assert.Empty(result.DegradedFlags);
    }

    [Fact]
    public async Task FundamentalsAndShareholder_ShouldSplitShareholderFactsAcrossTools()
    {
        var snapshot = new StockFundamentalSnapshotDto(
            DateTime.UtcNow.AddMinutes(-15),
            new[]
            {
                new StockFundamentalFactDto("营业收入", "1860亿元", "东方财富最新财报"),
                new StockFundamentalFactDto("归属净利润", "412亿元", "东方财富最新财报"),
                new StockFundamentalFactDto("股东户数", "125000", "东方财富股东研究"),
                new StockFundamentalFactDto("户均持股", "2.5万股", "东方财富股东研究"),
                new StockFundamentalFactDto("股权集中度", "较集中", "东方财富股东研究")
            });
        var service = CreateService(
            queryTool: new FakeQueryLocalFactDatabaseTool(fundamentalFacts: new[]
            {
                new LocalFundamentalFactDto("机构目标价", "11.5", "东方财富")
            }),
            fundamentalSnapshotService: new StaticFundamentalSnapshotService(snapshot));

        var fundamentals = await service.GetFundamentalsAsync("sh600000", "task-fundamentals-split");
        var shareholder = await service.GetShareholderAsync("sh600000", "task-shareholder-split");

        Assert.DoesNotContain(fundamentals.Data.Facts, item => item.Label.Contains("股东", StringComparison.Ordinal) || item.Label.Contains("户均", StringComparison.Ordinal) || item.Label.Contains("集中度", StringComparison.Ordinal) || item.Label.Contains("持股", StringComparison.Ordinal));
        Assert.Contains(fundamentals.Data.Facts, item => item.Label == "营业收入");
        Assert.Contains(shareholder.Data.Facts, item => item.Label == "股东户数");
        Assert.Contains(shareholder.Data.Facts, item => item.Label == "户均持股");
        Assert.Contains(shareholder.Data.Facts, item => item.Label == "股权集中度");
    }

    [Fact]
    public async Task GetMarketContextAsync_ShouldReturnStructuredData()
    {
        var service = CreateService();

        var result = await service.GetMarketContextAsync("sh600000", "task-market-context");

        Assert.Equal(StockMcpToolNames.MarketContext, result.ToolName);
        Assert.Equal("local_required", result.RolePolicyClass);
        Assert.True(result.Data.Available);
        Assert.Equal("sh600000", result.Data.Symbol);
        Assert.Equal("主升", result.Data.StageLabel);
        Assert.Equal("银行", result.Data.MainlineSectorName);
        Assert.Equal(2, result.Evidence.Count);
    }

    [Fact]
    public async Task GetSocialSentimentAsync_WhenOnlyMarketProxyAvailable_ShouldReturnDegradedContract()
    {
        var service = CreateService(
            queryTool: new EmptyQueryLocalFactDatabaseTool(),
            sectorRotationQueryService: new FakeSectorRotationQueryService());

        var result = await service.GetSocialSentimentAsync("sh600000", "task-social-degraded");

        Assert.Equal(StockMcpToolNames.SocialSentiment, result.ToolName);
        Assert.Equal("local_required", result.RolePolicyClass);
        Assert.Equal("degraded", result.Data.Status);
        Assert.False(result.Data.Blocked);
        Assert.Equal("market_proxy_only", result.Data.ApproximationMode);
        Assert.Equal("利好", result.Data.OverallSentiment);
        Assert.NotNull(result.Data.MarketProxy);
        Assert.Contains("no_live_social_source", result.DegradedFlags);
        Assert.Contains("degraded.market_proxy_only", result.DegradedFlags);
    }

    [Fact]
    public async Task GetSocialSentimentAsync_WhenEvidenceInsufficient_ShouldReturnBlockedNoData()
    {
        var service = CreateService(
            queryTool: new EmptyQueryLocalFactDatabaseTool(),
            sectorRotationQueryService: new NullSectorRotationQueryService());

        var result = await service.GetSocialSentimentAsync("sh600000", "task-social-blocked");

        Assert.Equal(StockMcpToolNames.SocialSentiment, result.ToolName);
        Assert.Equal("blocked", result.Data.Status);
        Assert.True(result.Data.Blocked);
        Assert.Equal("no_data", result.Data.BlockedReason);
        Assert.Equal("blocked.no_data", result.ErrorCode);
        Assert.Equal("none", result.Data.ApproximationMode);
        Assert.Null(result.Data.OverallSentiment);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public async Task GetKlineAsync_ShouldWrapDataInLocalRequiredEnvelope()
    {
        var service = CreateService();

        var result = await service.GetKlineAsync("sh600000", "day", 60, null, "task-1");

        Assert.Equal("StockKlineMcp", result.ToolName);
        Assert.Equal("task-1", result.TaskId);
        Assert.Equal("local_required", result.Meta.PolicyClass);
        Assert.Equal("local_required", result.RolePolicyClass);
        Assert.Equal("local", result.SourceTier);
        Assert.Equal(result.Cache.Hit, result.CacheHit);
        Assert.Equal(result.DegradedFlags.FirstOrDefault(), result.ErrorCode);
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
        Assert.Equal("fresh", result.FreshnessTag);
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
        Assert.Equal("external_gated", result.RolePolicyClass);
        Assert.Equal("external", result.SourceTier);
        Assert.Equal("external_search_unavailable", result.ErrorCode);
        Assert.Equal("no_data", result.FreshnessTag);
        Assert.Contains("external_search_unavailable", result.DegradedFlags);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public async Task GetNewsAsync_WhenNoLocalEvidence_ShouldReturnNoDataFreshnessAndDegradedFlag()
    {
        var service = CreateService(queryTool: new EmptyQueryLocalFactDatabaseTool());

        var result = await service.GetNewsAsync("sh600000", "stock", "task-news-empty");

        Assert.Equal("no_data", result.FreshnessTag);
        Assert.Equal("no_local_news_evidence", result.ErrorCode);
        Assert.Empty(result.Evidence);
        Assert.Contains("no_local_news_evidence", result.DegradedFlags);
    }

    [Fact]
    public async Task GetKlineAsync_WhenKlineSeriesEmpty_ShouldReturnStableEnvelope()
    {
        var service = CreateService(dataService: new FakeStockDataService(kLines: Array.Empty<KLinePointDto>()));

        var result = await service.GetKlineAsync("sh600000", "day", 60, null, "task-kline-empty");

        Assert.Equal("StockKlineMcp", result.ToolName);
        Assert.Equal("local_required", result.RolePolicyClass);
        Assert.Equal("local", result.SourceTier);
        Assert.Equal(0, result.Data.WindowSize);
        Assert.Empty(result.Data.Bars);
        Assert.NotNull(result.Data.KeyLevels);
    }

    [Fact]
    public async Task GetKlineAsync_WhenQuoteFetchFails_ShouldBubbleUpstreamFailure()
    {
        var service = CreateService(dataService: new ThrowingStockDataService());

        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetKlineAsync("sh600000", "day", 60, null, "task-kline-fail"));
    }

    [Fact]
    public async Task GetMinuteAsync_ShouldWrapDataInLocalRequiredEnvelope()
    {
        var service = CreateService();

        var result = await service.GetMinuteAsync("sh600000", null, "task-minute-success");

        Assert.Equal("StockMinuteMcp", result.ToolName);
        Assert.Equal("local_required", result.RolePolicyClass);
        Assert.Equal("local", result.SourceTier);
        Assert.Equal("minute", result.Meta.Interval);
        Assert.NotEmpty(result.Data.Points);
        Assert.NotNull(result.Data.Vwap);
    }

    [Fact]
    public async Task GetMinuteAsync_WhenMinuteSeriesEmpty_ShouldReturnStableEnvelope()
    {
        var service = CreateService(dataService: new FakeStockDataService(minuteLines: Array.Empty<MinuteLinePointDto>()));

        var result = await service.GetMinuteAsync("sh600000", null, "task-minute-empty");

        Assert.Equal("StockMinuteMcp", result.ToolName);
        Assert.Equal("local_required", result.RolePolicyClass);
        Assert.Equal("local", result.SourceTier);
        Assert.Equal(0, result.Data.WindowSize);
        Assert.Empty(result.Data.Points);
        Assert.Null(result.Data.Vwap);
        Assert.Null(result.Data.OpeningDrivePercent);
        Assert.Null(result.Data.AfternoonDriftPercent);
        Assert.Null(result.Data.IntradayRangePercent);
    }

    [Fact]
    public async Task GetMinuteAsync_WhenQuoteFetchFails_ShouldBubbleUpstreamFailure()
    {
        var service = CreateService(dataService: new ThrowingStockDataService());

        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetMinuteAsync("sh600000", null, "task-minute-fail"));
    }

    [Fact]
    public async Task GetStrategyAsync_WhenKlineWindowEmpty_ShouldReturnEmptySignalSet()
    {
        var service = CreateService(dataService: new FakeStockDataService(kLines: Array.Empty<KLinePointDto>()));

        var result = await service.GetStrategyAsync("sh600000", "day", 60, null, new[] { "ma", "macd" }, "task-strategy-empty");

        Assert.Equal("StockStrategyMcp", result.ToolName);
        Assert.Equal("local_required", result.RolePolicyClass);
        Assert.Equal("local", result.SourceTier);
        Assert.Equal("day", result.Data.Interval);
        Assert.Equal(new[] { "ma", "macd" }, result.Data.RequestedStrategies);
        Assert.Empty(result.Data.Signals);
    }

    [Fact]
    public async Task GetStrategyAsync_WhenQuoteFetchFails_ShouldBubbleUpstreamFailure()
    {
        var service = CreateService(dataService: new ThrowingStockDataService());

        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetStrategyAsync("sh600000", "day", 60, null, new[] { "ma" }, "task-strategy-fail"));
    }

    [Fact]
    public async Task SearchAsync_WhenProviderEnabledAndResponseSucceeds_ShouldExposeExternalEvidence()
    {
        var publishedAt = DateTime.UtcNow.AddHours(-2);
        var payload = $$"""
{"results":[{"title":"浦发银行公告速览","url":"https://news.example.com/pingan","source":"trusted-feed","content":"公告显示公司维持稳健经营。","published_date":"{{publishedAt:O}}","score":0.87}]}
""";
        var service = CreateService(
            new StockCopilotSearchOptions { Enabled = true, Provider = "tavily", ApiKey = "test-key", BaseUrl = "https://search.example.com" },
            httpClientFactory: new FakeHttpClientFactory(new StaticHttpMessageHandler(HttpStatusCode.OK, payload)));

        var result = await service.SearchAsync("浦发银行 公告", true, "task-search-success");

        Assert.Equal("StockSearchMcp", result.ToolName);
        Assert.Equal("external_gated", result.RolePolicyClass);
        Assert.Equal("external", result.SourceTier);
        Assert.Null(result.ErrorCode);
        Assert.Equal("fresh", result.FreshnessTag);
        Assert.Equal(1, result.Data.ResultCount);
        Assert.Single(result.Data.Results);
        var evidence = Assert.Single(result.Evidence);
        Assert.Equal("trusted-feed", evidence.Source);
        Assert.Equal("公告显示公司维持稳健经营。", evidence.Excerpt);
    }

    [Fact]
    public async Task SearchAsync_WhenProviderEnabledAndResponseIsEmpty_ShouldReturnNoDataEnvelope()
    {
        var service = CreateService(
            new StockCopilotSearchOptions { Enabled = true, Provider = "tavily", ApiKey = "test-key", BaseUrl = "https://search.example.com" },
            httpClientFactory: new FakeHttpClientFactory(new StaticHttpMessageHandler(HttpStatusCode.OK, "{\"results\":[]}")));

        var result = await service.SearchAsync("浦发银行 公告", true, "task-search-empty");

        Assert.Equal("StockSearchMcp", result.ToolName);
        Assert.Equal("external_gated", result.RolePolicyClass);
        Assert.Equal("external", result.SourceTier);
        Assert.Null(result.ErrorCode);
        Assert.Equal("no_data", result.FreshnessTag);
        Assert.Empty(result.Data.Results);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public async Task SearchAsync_WhenProviderReturnsFailureStatus_ShouldExposeDegradedEnvelope()
    {
        var service = CreateService(
            new StockCopilotSearchOptions { Enabled = true, Provider = "tavily", ApiKey = "test-key", BaseUrl = "https://search.example.com" },
            httpClientFactory: new FakeHttpClientFactory(new StaticHttpMessageHandler(HttpStatusCode.BadGateway, "{}")));

        var result = await service.SearchAsync("浦发银行 公告", true, "task-search-fail");

        Assert.Equal("external_search_failed", result.ErrorCode);
        Assert.Equal("external", result.SourceTier);
        Assert.Equal("no_data", result.FreshnessTag);
        Assert.Contains("external_search_failed", result.DegradedFlags);
        Assert.Empty(result.Data.Results);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public async Task GetNewsAsync_WhenLocalQueryFails_ShouldBubbleUpstreamFailure()
    {
        var service = CreateService(queryTool: new ThrowingQueryLocalFactDatabaseTool());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetNewsAsync("sh600000", "stock", "task-news-fail"));
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

    private static StockCopilotMcpService CreateService(
        StockCopilotSearchOptions? options = null,
        IStockDataService? dataService = null,
        IQueryLocalFactDatabaseTool? queryTool = null,
        IStockFundamentalSnapshotService? fundamentalSnapshotService = null,
        ISectorRotationQueryService? sectorRotationQueryService = null,
        IHttpClientFactory? httpClientFactory = null)
    {
        return new StockCopilotMcpService(
            dataService ?? new FakeStockDataService(),
            queryTool ?? new FakeQueryLocalFactDatabaseTool(),
            new FakeStockMarketContextService(),
            sectorRotationQueryService ?? new FakeSectorRotationQueryService(),
            new StockAgentFeatureEngineeringService(),
            fundamentalSnapshotService ?? new StaticFundamentalSnapshotService(new StockFundamentalSnapshotDto(
                new DateTime(2026, 3, 21, 9, 0, 0, DateTimeKind.Utc),
                new[]
                {
                    new StockFundamentalFactDto("机构目标价", "11.5", "东方财富"),
                    new StockFundamentalFactDto("股东户数", "100000", "东方财富股东研究"),
                    new StockFundamentalFactDto("户均持股", "2.5万股", "东方财富股东研究")
                })),
            httpClientFactory ?? new FakeHttpClientFactory(),
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
        private readonly IReadOnlyList<LocalFundamentalFactDto> _fundamentalFacts;

        public FakeQueryLocalFactDatabaseTool(
            string? excerpt = "公告摘要",
            string? summary = "公告摘要",
            IReadOnlyList<LocalFundamentalFactDto>? fundamentalFacts = null)
        {
            _excerpt = excerpt;
            _summary = summary;
            _fundamentalFacts = fundamentalFacts ?? new[] { new LocalFundamentalFactDto("机构目标价", "11.5", "东方财富") };
        }

        public Task<LocalFactPackageDto> QueryAsync(string symbol, CancellationToken cancellationToken = default)
        {
            var publishedAt = DateTime.UtcNow.AddHours(-1);
            var ingestedAt = DateTime.UtcNow.AddMinutes(-30);
            return Task.FromResult(new LocalFactPackageDto(
                symbol,
                "浦发银行",
                "银行",
                new[]
                {
                    new LocalNewsItemDto(1, "stock_news:1", "浦发银行公告", null, "上交所公告", "announcement", "announcement", "利好", publishedAt, ingestedAt, "https://example.com/a", _excerpt, _summary, "local_fact", "summary_only", ingestedAt, "个股:浦发银行", new[] { "公告" })
                },
                Array.Empty<LocalNewsItemDto>(),
                Array.Empty<LocalNewsItemDto>(),
                DateTime.UtcNow.AddHours(-2),
                _fundamentalFacts));
        }

        public Task<LocalNewsBucketDto> QueryLevelAsync(string symbol, string level, CancellationToken cancellationToken = default)
        {
            var publishedAt = DateTime.UtcNow.AddHours(-1);
            var ingestedAt = DateTime.UtcNow.AddMinutes(-30);
            return Task.FromResult(new LocalNewsBucketDto(symbol, level, "银行", new[]
            {
                new LocalNewsItemDto(1, "stock_news:1", "浦发银行公告", null, "上交所公告", "announcement", level, "利好", publishedAt, ingestedAt, "https://example.com/a", _excerpt, _summary, "local_fact", "summary_only", ingestedAt, "个股:浦发银行", new[] { "公告" })
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

    private sealed class EmptyQueryLocalFactDatabaseTool : IQueryLocalFactDatabaseTool
    {
        public Task<LocalFactPackageDto> QueryAsync(string symbol, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LocalFactPackageDto(symbol, "浦发银行", "银行", Array.Empty<LocalNewsItemDto>(), Array.Empty<LocalNewsItemDto>(), Array.Empty<LocalNewsItemDto>(), null, Array.Empty<LocalFundamentalFactDto>()));
        }

        public Task<LocalNewsBucketDto> QueryLevelAsync(string symbol, string level, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LocalNewsBucketDto(symbol, level, "银行", Array.Empty<LocalNewsItemDto>()));
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

    private sealed class ThrowingQueryLocalFactDatabaseTool : IQueryLocalFactDatabaseTool
    {
        public Task<LocalFactPackageDto> QueryAsync(string symbol, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("local query failed");
        }

        public Task<LocalNewsBucketDto> QueryLevelAsync(string symbol, string level, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("local query failed");
        }

        public Task<LocalNewsBucketDto> QueryMarketAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("local query failed");
        }

        public Task<LocalNewsArchivePageDto> QueryArchiveAsync(string? keyword, string? level, string? sentiment, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("local query failed");
        }
    }

    private sealed class ThrowingStockDataService : IStockDataService
    {
        public Task<StockQuoteDto> GetQuoteAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
        {
            throw new HttpRequestException("quote failed");
        }

        public Task<MarketIndexDto> GetMarketIndexAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<KLinePointDto>> GetKLineAsync(string symbol, string interval, int count, string? source = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<MinuteLinePointDto>> GetMinuteLineAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<IntradayMessageDto>> GetIntradayMessagesAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeStockMarketContextService : IStockMarketContextService
    {
        public Task<StockMarketContextDto?> GetLatestAsync(string symbol, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<StockMarketContextDto?>(new StockMarketContextDto("主升", 72m, "银行", "银行", "BK001", 80m, 0.8m, "积极执行", false, true));
        }
    }

    private sealed class FakeSectorRotationQueryService : ISectorRotationQueryService
    {
        public Task<MarketSentimentSummaryDto?> GetLatestSummaryAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<MarketSentimentSummaryDto?>(new MarketSentimentSummaryDto(
                new DateTime(2026, 3, 26, 10, 0, 0, DateTimeKind.Utc),
                "盘中",
                "主升",
                78m,
                3,
                42,
                6,
                8,
                12m,
                3200,
                1200,
                400,
                12345m,
                28m,
                54m,
                72m,
                68m,
                "主升",
                81m,
                24m,
                48m,
                36m,
                10m));
        }

        public Task<IReadOnlyList<MarketSentimentHistoryPointDto>> GetHistoryAsync(int days, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<SectorRotationPageDto> GetSectorPageAsync(string boardType, int page, int pageSize, string sort, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<SectorRotationDetailDto?> GetSectorDetailAsync(string sectorCode, string boardType, string window, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<SectorRotationTrendDto?> GetSectorTrendAsync(string sectorCode, string boardType, string window, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<SectorRotationLeaderDto>> GetLeadersAsync(string sectorCode, string boardType, int take, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<SectorRotationListItemDto>> GetMainlineAsync(string boardType, string window, int take, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class NullSectorRotationQueryService : ISectorRotationQueryService
    {
        public Task<MarketSentimentSummaryDto?> GetLatestSummaryAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<MarketSentimentSummaryDto?>(null);
        }

        public Task<IReadOnlyList<MarketSentimentHistoryPointDto>> GetHistoryAsync(int days, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<SectorRotationPageDto> GetSectorPageAsync(string boardType, int page, int pageSize, string sort, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<SectorRotationDetailDto?> GetSectorDetailAsync(string sectorCode, string boardType, string window, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<SectorRotationTrendDto?> GetSectorTrendAsync(string sectorCode, string boardType, string window, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<SectorRotationLeaderDto>> GetLeadersAsync(string sectorCode, string boardType, int take, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<SectorRotationListItemDto>> GetMainlineAsync(string boardType, string window, int take, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public FakeHttpClientFactory(HttpMessageHandler? handler = null)
        {
            _handler = handler ?? new FakeHttpMessageHandler();
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, disposeHandler: false) { BaseAddress = new Uri("https://example.com/") };
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

    private sealed class StaticHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _payload;

        public StaticHttpMessageHandler(HttpStatusCode statusCode, string payload)
        {
            _statusCode = statusCode;
            _payload = payload;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_payload)
            });
        }
    }

    private sealed class StaticFundamentalSnapshotService : IStockFundamentalSnapshotService
    {
        private readonly StockFundamentalSnapshotDto? _snapshot;

        public StaticFundamentalSnapshotService(StockFundamentalSnapshotDto? snapshot)
        {
            _snapshot = snapshot;
        }

        public Task<StockFundamentalSnapshotDto?> GetSnapshotAsync(string symbol, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_snapshot);
        }
    }

    private sealed class NullFundamentalSnapshotService : IStockFundamentalSnapshotService
    {
        public Task<StockFundamentalSnapshotDto?> GetSnapshotAsync(string symbol, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<StockFundamentalSnapshotDto?>(null);
        }
    }
}