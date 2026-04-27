using System.Text.Json;
using System.Net;
using Microsoft.Extensions.Options;
using SimplerJiangAiAgent.Api.Infrastructure.Jobs;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;
using SimplerJiangAiAgent.Api.Infrastructure.Serialization;
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
    public async Task GetCompanyOverviewAsync_WhenQuoteUnavailable_ShouldDegradeWithoutZeroPrice()
    {
        var service = CreateService(dataService: new FakeStockDataService(quoteAvailable: false));

        var result = await service.GetCompanyOverviewAsync("sh600000", "task-company-overview-no-quote");

        Assert.Equal("sh600000", result.Data.Symbol);
        Assert.Equal("浦发银行", result.Data.Name);
        Assert.Null(result.Data.Price);
        Assert.Null(result.Data.ChangePercent);
        Assert.Null(result.Data.QuoteTimestamp);
        Assert.Contains("quote_unavailable", result.DegradedFlags);
        Assert.Contains(result.Warnings, warning => warning.Contains("行情数据不可用", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Evidence, item => item.Summary?.Contains("现价=0", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task GetProductAsync_ShouldExposeBusinessScopeIndustryAndRegion()
    {
        var snapshot = new StockFundamentalSnapshotDto(
            DateTime.UtcNow.AddMinutes(-25),
            new[]
            {
                new StockFundamentalFactDto("主营业务", "新能源汽车及动力电池业务", "东方财富公司概况"),
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
        Assert.Equal("新能源汽车及动力电池业务", result.Data.BusinessScope);
        Assert.Equal("新能源汽车及动力电池业务", result.Data.MainBusiness);
        Assert.Equal("汽车整车", result.Data.Industry);
        Assert.Equal("汽车制造业", result.Data.CsrcIndustry);
        Assert.Equal("广东", result.Data.Region);
        Assert.Equal(5, result.Data.FactCount);
        Assert.DoesNotContain(result.Data.Facts, item => item.Label == "营业收入");
        Assert.Contains(result.Data.Facts, item => item.Label == "经营范围" && item.Value.Contains("研发、生产、销售", StringComparison.Ordinal));
        Assert.Contains(result.Evidence, item => item.Level == "product");
    }

    [Fact]
    public async Task GetProductAsync_WhenLlmReturnsMarketRecognitionDirections_ShouldSurfaceLlmSummaryInsteadOfHardcodedKeywords()
    {
        const string registeredBusinessScope = "光通信器件、通信设备、电子元器件的研发、生产、销售及技术服务。";
        var llmService = new FakeLlmService((provider, request, _) =>
        {
            Assert.Equal("cheap-route", provider);
            Assert.Equal("gpt-4.1-nano", request.Model);
            return Task.FromResult(new LlmChatResult("{\"marketRecognitionDirections\":[\"高速铜缆\",\"北美算力映射\"],\"reason\":\"最近新闻与搜索证据都把公司放在高速互联与海外算力链方向。\"}"));
        });

        var service = CreateService(
            queryTool: new FakeQueryLocalFactDatabaseTool(
                name: "中际旭创",
                sectorName: "通信设备",
                fundamentalFacts: new[]
                {
                    new LocalFundamentalFactDto("主营业务", "高速光模块及光通信器件业务", "东方财富公司概况"),
                    new LocalFundamentalFactDto("经营范围", registeredBusinessScope, "东方财富公司概况"),
                    new LocalFundamentalFactDto("所属行业", "通信设备", "东方财富公司概况"),
                    new LocalFundamentalFactDto("所属地区", "山东", "东方财富公司概况")
                },
                stockNews: new[]
                {
                    CreateLocalNewsItem(
                        101,
                        "stock",
                        DateTime.UtcNow.AddHours(-1),
                        DateTime.UtcNow.AddMinutes(-30),
                        "公司在 AI 算力链条中的高速光模块需求提升。",
                        "公司在 AI 算力链条中的高速光模块需求提升。",
                        title: "中际旭创 AI 算力需求持续提升",
                        aiTarget: "个股:中际旭创",
                        aiTags: new[] { "算力" })
                },
                sectorReports: new[]
                {
                    CreateLocalNewsItem(
                        201,
                        "sector",
                        DateTime.UtcNow.AddHours(-2),
                        DateTime.UtcNow.AddMinutes(-40),
                        "CPO 光模块板块维持活跃。",
                        "CPO 光模块板块维持活跃。",
                        title: "CPO 光模块板块持续活跃",
                        aiTarget: "板块:CPO",
                        aiTags: new[] { "光模块" })
                }),
            fundamentalSnapshotService: new NullFundamentalSnapshotService(),
            llmService: llmService,
            syncOptions: new StockSyncOptions
            {
                AiProvider = "cheap-route",
                AiModel = "gpt-4.1-nano"
            });

        var result = await service.GetProductAsync("sz300308", "task-product-market-direction");

        var summaryEvidence = result.Evidence[0];
        var summary = Assert.IsType<string>(summaryEvidence.Summary);
        Assert.Equal("产品业务概览", summaryEvidence.Title);
        Assert.Contains("市场认可方向=高速铜缆 / 北美算力映射", summary, StringComparison.Ordinal);
        Assert.Contains("业务摘要=高速光模块及光通信器件业务", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("经营范围=", summary, StringComparison.Ordinal);
        Assert.Contains("市场归纳LLM", summaryEvidence.Source, StringComparison.Ordinal);
        Assert.Contains(result.Data.Facts, item => item.Label == "经营范围" && item.Value == registeredBusinessScope);
    }

    [Fact]
    public async Task GetProductAsync_WhenLlmFails_ShouldFallbackToReadableBusinessIndustrySummary()
    {
        const string registeredBusinessScope = "企业金融服务、零售金融服务、资金业务、金融市场业务。";
        var service = CreateService(
            queryTool: new FakeQueryLocalFactDatabaseTool(
                name: "浦发银行",
                sectorName: "银行",
                fundamentalFacts: new[]
                {
                    new LocalFundamentalFactDto("主营业务", "商业银行及相关金融服务", "东方财富公司概况"),
                    new LocalFundamentalFactDto("经营范围", registeredBusinessScope, "东方财富公司概况"),
                    new LocalFundamentalFactDto("所属行业", "银行", "东方财富公司概况"),
                    new LocalFundamentalFactDto("所属地区", "上海", "东方财富公司概况")
                },
                stockNews: new[]
                {
                    CreateLocalNewsItem(
                        301,
                        "stock",
                        DateTime.UtcNow.AddHours(-2),
                        DateTime.UtcNow.AddMinutes(-20),
                        "银行板块维持稳健。",
                        "银行板块维持稳健。",
                        title: "浦发银行经营稳健")
                }),
            fundamentalSnapshotService: new NullFundamentalSnapshotService(),
            llmService: new FakeLlmService((_, _, _) => throw new InvalidOperationException("llm failed")));

        var result = await service.GetProductAsync("sh600000", "task-product-llm-fallback");

        var summary = Assert.IsType<string>(result.Evidence[0].Summary);
        Assert.DoesNotContain("市场认可方向=", summary, StringComparison.Ordinal);
        Assert.Contains("业务摘要=商业银行及相关金融服务", summary, StringComparison.Ordinal);
        Assert.Contains("所属行业=银行", summary, StringComparison.Ordinal);
        Assert.Contains("所属地区=上海", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("经营范围=", summary, StringComparison.Ordinal);
        Assert.Contains(result.Data.Facts, item => item.Label == "经营范围" && item.Value == registeredBusinessScope);
    }

    [Fact]
    public async Task GetProductAsync_WhenExternalSearchAvailable_ShouldIncludeSearchEvidenceInLlmPrompt()
    {
        var publishedAt = DateTime.UtcNow.AddHours(-3);
        var llmService = new FakeLlmService((_, _, _) => Task.FromResult(new LlmChatResult("{\"marketRecognitionDirections\":[],\"reason\":\"证据不足\"}")));
        var payload = $$"""
{"results":[{"title":"高速铜缆概念热度提升","url":"https://news.example.com/copper","source":"trusted-feed","content":"多篇报道把公司与高速铜缆方向关联。","published_date":"{{publishedAt:O}}","score":0.92}]}
""";
        var service = CreateService(
            new StockCopilotSearchOptions { Enabled = true, Provider = "tavily", ApiKey = "test-key", BaseUrl = "https://search.example.com" },
            queryTool: new FakeQueryLocalFactDatabaseTool(
                name: "中际旭创",
                sectorName: "通信设备",
                fundamentalFacts: new[]
                {
                    new LocalFundamentalFactDto("主营业务", "高速光模块及光通信器件业务", "东方财富公司概况"),
                    new LocalFundamentalFactDto("经营范围", "光通信器件、通信设备研发生产销售", "东方财富公司概况"),
                    new LocalFundamentalFactDto("所属行业", "通信设备", "东方财富公司概况")
                },
                stockNews: new[]
                {
                    CreateLocalNewsItem(
                        401,
                        "stock",
                        DateTime.UtcNow.AddHours(-1),
                        DateTime.UtcNow.AddMinutes(-10),
                        "高速互联需求继续提升。",
                        "高速互联需求继续提升。",
                        title: "中际旭创高速互联需求提升")
                }),
            fundamentalSnapshotService: new NullFundamentalSnapshotService(),
            httpClientFactory: new FakeHttpClientFactory(new StaticHttpMessageHandler(HttpStatusCode.OK, payload)),
            llmService: llmService);

        await service.GetProductAsync("sz300308", "task-product-search-prompt");

        Assert.NotNull(llmService.LastRequest);
        Assert.Contains("高速铜缆概念热度提升", llmService.LastRequest!.Prompt, StringComparison.Ordinal);
        Assert.Contains("trusted-feed", llmService.LastRequest.Prompt, StringComparison.Ordinal);
        Assert.Contains("多篇报道把公司与高速铜缆方向关联", llmService.LastRequest.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetProductAsync_WhenMainBusinessMissingButIndustryExists_ShouldUseIndustrySummaryInsteadOfRegisteredScope()
    {
        const string registeredBusinessScope = "吸收公众存款；发放短期、中期和长期贷款；办理国内外结算；办理票据承兑与贴现；发行金融债券；代理发行、代理兑付、承销政府债券；买卖政府债券、金融债券；从事同业拆借；买卖、代理买卖外汇；从事银行卡业务；提供信用证服务及担保；代理收付款项及代理保险业务；提供保管箱服务；经国务院银行业监督管理机构批准的其他业务。";
        var snapshot = new StockFundamentalSnapshotDto(
            DateTime.UtcNow.AddMinutes(-25),
            new[]
            {
                new StockFundamentalFactDto("经营范围", registeredBusinessScope, "东方财富公司概况"),
                new StockFundamentalFactDto("所属行业", "银行", "东方财富公司概况"),
                new StockFundamentalFactDto("所属地区", "上海", "东方财富公司概况")
            });
        var service = CreateService(
            queryTool: new EmptyQueryLocalFactDatabaseTool(),
            fundamentalSnapshotService: new StaticFundamentalSnapshotService(snapshot));

        var result = await service.GetProductAsync("sh600000", "task-product-industry-summary");

        Assert.Null(result.Data.MainBusiness);
        Assert.Equal("以银行相关业务为主", result.Data.BusinessScope);
        Assert.NotEqual(registeredBusinessScope, result.Data.BusinessScope);
        Assert.DoesNotContain("no_product_facts", result.DegradedFlags);
        Assert.Contains(result.Data.Facts, item => item.Label == "经营范围" && item.Value == registeredBusinessScope);
        var summaryEvidence = result.Evidence[0];
        var summary = Assert.IsType<string>(summaryEvidence.Summary);
        Assert.Equal("产品业务概览", summaryEvidence.Title);
        Assert.Contains($"业务摘要={result.Data.BusinessScope}", summary, StringComparison.Ordinal);
        Assert.Contains("所属行业=银行", summary, StringComparison.Ordinal);
        Assert.Contains("所属地区=上海", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("经营范围=", summary, StringComparison.Ordinal);
        Assert.Contains(result.Evidence, item => item.Title == "经营范围" && item.Summary == registeredBusinessScope);
    }

    [Fact]
    public async Task GetProductAsync_WhenOnlyRegisteredBusinessScopeAvailable_ShouldKeepFirstSummaryReadable()
    {
        const string registeredBusinessScope = "一般项目：电子专用材料研发；电子专用材料制造；电子专用材料销售；货物进出口；技术进出口；以自有资金从事投资活动。";
        var snapshot = new StockFundamentalSnapshotDto(
            DateTime.UtcNow.AddMinutes(-25),
            new[]
            {
                new StockFundamentalFactDto("经营范围", registeredBusinessScope, "东方财富公司概况")
            });
        var service = CreateService(
            queryTool: new EmptyQueryLocalFactDatabaseTool(),
            fundamentalSnapshotService: new StaticFundamentalSnapshotService(snapshot));

        var result = await service.GetProductAsync("sh600000", "task-product-registered-scope-only");

        var summaryEvidence = result.Evidence[0];
        var summary = Assert.IsType<string>(summaryEvidence.Summary);
        Assert.Equal("产品业务概览", summaryEvidence.Title);
        Assert.Equal("产品事实已收录，详见后续经营范围原始事实", summary);
        Assert.DoesNotContain(registeredBusinessScope, summary, StringComparison.Ordinal);
        Assert.DoesNotContain("经营范围=", summary, StringComparison.Ordinal);
        Assert.Contains(result.Data.Facts, item => item.Label == "经营范围" && item.Value == registeredBusinessScope);
        Assert.Contains(result.Evidence, item => item.Title == "经营范围" && item.Summary == registeredBusinessScope);
    }

    [Fact]
    public async Task GetProductAsync_WhenBusinessFactsInsufficient_ShouldReturnNoProductFactsEnvelope()
    {
        var snapshot = new StockFundamentalSnapshotDto(
            DateTime.UtcNow.AddMinutes(-25),
            new[]
            {
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
        Assert.Equal(1, result.Data.FactCount);
        Assert.Contains(result.Data.Facts, item => item.Label == "所属地区");
    }

    [Fact]
    public async Task GetCompanyOverviewAsync_WithoutEvidenceTake_ShouldReturnAllEvidence()
    {
        var service = CreateService(
            queryTool: new FakeQueryLocalFactDatabaseTool(
                stockNews: CreateLocalNewsItems(4),
                fundamentalFacts: new[]
                {
                    new LocalFundamentalFactDto("主营业务", "商业银行及相关金融服务", "东方财富公司概况"),
                    new LocalFundamentalFactDto("经营范围", "吸收公众存款、发放贷款、办理结算", "东方财富公司概况")
                }));

        var result = await service.GetCompanyOverviewAsync("sh600000", "task-company-overview-all-evidence");

        Assert.Equal(5, result.Evidence.Count);
        Assert.Contains(result.Evidence, item => item.Level == "overview");
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
    public async Task GetFundamentalsAsync_ShouldApplyFactAndEvidenceWindowWithLatestFirstOrdering()
    {
        var snapshot = new StockFundamentalSnapshotDto(
            DateTime.UtcNow.AddMinutes(-20),
            new[]
            {
                new StockFundamentalFactDto("所属行业", "银行", "东方财富公司概况"),
                new StockFundamentalFactDto("归属净利润", "128亿元", "东方财富最新财报"),
                new StockFundamentalFactDto("最新财报期", "2025年报", "东方财富最新财报"),
                new StockFundamentalFactDto("营业收入", "1680亿元", "东方财富最新财报")
            });
        var service = CreateService(
            queryTool: new FakeQueryLocalFactDatabaseTool(
                fundamentalFacts: new[]
                {
                    new LocalFundamentalFactDto("机构目标价", "11.5", "东方财富")
                }),
            fundamentalSnapshotService: new StaticFundamentalSnapshotService(snapshot));

        var result = await service.GetFundamentalsAsync(
            "sh600000",
            "task-fundamentals-window",
            new StockCopilotMcpWindowOptions(1, 2, 1, 2));

        Assert.Equal(5, result.Data.FactCount);
        Assert.Equal(new[] { "营业收入", "归属净利润" }, result.Data.Facts.Select(item => item.Label).ToArray());
        Assert.Equal(2, result.Evidence.Count);
        Assert.Equal(new[] { "营业收入", "归属净利润" }, result.Evidence.Select(item => item.Title).ToArray());
    }

    [Fact]
    public async Task GetFundamentalsAsync_WithoutFactTake_ShouldReturnAllFactsAndEvidence()
    {
        var snapshot = new StockFundamentalSnapshotDto(
            DateTime.UtcNow.AddMinutes(-20),
            new[]
            {
                new StockFundamentalFactDto("所属行业", "银行", "东方财富公司概况"),
                new StockFundamentalFactDto("归属净利润", "128亿元", "东方财富最新财报"),
                new StockFundamentalFactDto("最新财报期", "2025年报", "东方财富最新财报"),
                new StockFundamentalFactDto("营业收入", "1680亿元", "东方财富最新财报")
            });
        var service = CreateService(
            queryTool: new FakeQueryLocalFactDatabaseTool(
                fundamentalFacts: new[]
                {
                    new LocalFundamentalFactDto("机构目标价", "11.5", "东方财富")
                }),
            fundamentalSnapshotService: new StaticFundamentalSnapshotService(snapshot));

        var result = await service.GetFundamentalsAsync("sh600000", "task-fundamentals-all");

        Assert.Equal(5, result.Data.FactCount);
        Assert.Equal(5, result.Data.Facts.Count);
        Assert.Equal(5, result.Evidence.Count);
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
        var service = CreateService(marketContextService: new EmptyLabelStockMarketContextService());

        var result = await service.GetMarketContextAsync("sh600000", "task-market-context");

        Assert.Equal(StockMcpToolNames.MarketContext, result.ToolName);
        Assert.Equal("local_required", result.RolePolicyClass);
        Assert.True(result.Data.Available);
        Assert.Equal("sh600000", result.Data.Symbol);
        Assert.NotNull(result.Meta.MarketContext);
        Assert.Equal("银行", result.Data.MainlineSectorName);
        Assert.Equal(2, result.Evidence.Count);

        var json = JsonSerializer.Serialize(result, CreateWebJsonOptions());
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");
        var metaMarketContext = doc.RootElement.GetProperty("meta").GetProperty("marketContext");

        Assert.False(data.TryGetProperty("stageLabel", out _));
        Assert.False(data.TryGetProperty("executionFrequencyLabel", out _));
        Assert.False(data.TryGetProperty("suggestedPositionScale", out _));
        Assert.False(data.TryGetProperty("isMainlineAligned", out _));
        Assert.False(data.TryGetProperty("counterTrendWarning", out _));
        Assert.False(metaMarketContext.TryGetProperty("stageLabel", out _));
        Assert.False(metaMarketContext.TryGetProperty("executionFrequencyLabel", out _));
        Assert.False(metaMarketContext.TryGetProperty("suggestedPositionScale", out _));
        Assert.False(metaMarketContext.TryGetProperty("isMainlineAligned", out _));
        Assert.False(metaMarketContext.TryGetProperty("counterTrendWarning", out _));
    }

    [Fact]
    public async Task GetMarketContextAsync_ShouldPopulateOverviewFields()
    {
        var overviewService = new FakeRealtimeMarketOverviewService();
        var service = CreateService(
            marketContextService: new EmptyLabelStockMarketContextService(),
            overviewService: overviewService);

        var result = await service.GetMarketContextAsync("sh600000", "task-market-overview");

        // Data fields
        Assert.NotNull(result.Data.Indices);
        Assert.Equal(3, result.Data.Indices!.Count);
        Assert.Equal("sh000001", result.Data.Indices[0].Symbol);
        Assert.Equal(3200m, result.Data.Indices[0].Price);
        Assert.NotNull(result.Data.MainCapitalFlow);
        Assert.Equal(-50m, result.Data.MainCapitalFlow!.MainNetInflow);
        Assert.NotNull(result.Data.NorthboundFlow);
        Assert.Equal(30m, result.Data.NorthboundFlow!.TotalNetInflow);
        Assert.NotNull(result.Data.Breadth);
        Assert.Equal(2100, result.Data.Breadth!.Advancers);
        Assert.Equal(2500, result.Data.Breadth!.Decliners);
        Assert.Equal(45, result.Data.Breadth!.LimitUpCount);
        Assert.Equal(8, result.Data.Breadth!.LimitDownCount);

        // Evidence: 2 from sector rotation + 4 from overview (indices, capital, northbound, breadth)
        Assert.Equal(6, result.Evidence.Count);
        Assert.Contains(result.Evidence, e => e.SourceRecordId?.Contains("indices") == true);
        Assert.Contains(result.Evidence, e => e.SourceRecordId?.Contains("main_capital_flow") == true);
        Assert.Contains(result.Evidence, e => e.SourceRecordId?.Contains("northbound_flow") == true);
        Assert.Contains(result.Evidence, e => e.SourceRecordId?.Contains("breadth") == true);

        // Features: should contain index and capital features
        Assert.Contains(result.Features, f => f.Name == "mainCapitalNetInflow");
        Assert.Contains(result.Features, f => f.Name == "northboundNetInflow");
        Assert.Contains(result.Features, f => f.Name == "advancers");
        Assert.Contains(result.Features, f => f.Name == "decliners");
    }

    [Fact]
    public async Task GetCompanyOverviewAsync_ShouldSerializeUtcDatesWithSingleChinaShift()
    {
        var quoteTimestampUtc = new DateTime(2026, 3, 21, 10, 0, 0, DateTimeKind.Utc);
        var fundamentalUpdatedAtUtc = new DateTime(2026, 3, 21, 9, 0, 0, DateTimeKind.Utc);
        var service = CreateService(
            dataService: new FakeStockDataService(
                quote: new StockQuoteDto(
                    "sh600000",
                    "浦发银行",
                    10.5m,
                    0.2m,
                    1.9m,
                    13m,
                    8m,
                    10.7m,
                    10.1m,
                    0.1m,
                    quoteTimestampUtc,
                    Array.Empty<StockNewsDto>(),
                    Array.Empty<StockIndicatorDto>(),
                    320_000_000_000m,
                    2.8m,
                    100000,
                    "银行")),
            queryTool: new FakeQueryLocalFactDatabaseTool(
                fundamentalUpdatedAt: new DateTime(2026, 3, 21, 8, 0, 0, DateTimeKind.Utc)),
            fundamentalSnapshotService: new StaticFundamentalSnapshotService(new StockFundamentalSnapshotDto(
                fundamentalUpdatedAtUtc,
                new[]
                {
                    new StockFundamentalFactDto("机构目标价", "11.5", "东方财富"),
                    new StockFundamentalFactDto("股东户数", "100000", "东方财富股东研究")
                })));

        var result = await service.GetCompanyOverviewAsync("sh600000", "task-company-overview-serialize");

        Assert.Equal(quoteTimestampUtc, result.Data.QuoteTimestamp);
        Assert.Equal(fundamentalUpdatedAtUtc, result.Data.FundamentalUpdatedAt);

        var json = JsonSerializer.Serialize(result, CreateWebJsonOptions());
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");
        var evidence = doc.RootElement.GetProperty("evidence")[0];

        Assert.Equal("2026-03-21T18:00:00.0000000+08:00", data.GetProperty("quoteTimestamp").GetString());
        Assert.Equal("2026-03-21T17:00:00.0000000+08:00", data.GetProperty("fundamentalUpdatedAt").GetString());
        Assert.Equal(data.GetProperty("quoteTimestamp").GetString(), evidence.GetProperty("publishedAt").GetString());
    }

    [Fact]
    public async Task GetSocialSentimentAsync_ShouldSerializeLatestEvidenceAtWithoutExtraChinaShift()
    {
        var latestPublishedAtUtc = new DateTime(2026, 3, 26, 16, 30, 0, DateTimeKind.Utc);
        var ingestedAtUtc = new DateTime(2026, 3, 26, 16, 0, 0, DateTimeKind.Utc);
        var service = CreateService(
            queryTool: new FakeQueryLocalFactDatabaseTool(
                publishedAt: latestPublishedAtUtc,
                ingestedAt: ingestedAtUtc));

        var result = await service.GetSocialSentimentAsync("sh600000", "task-social-latest-evidence");

        var json = JsonSerializer.Serialize(result, CreateWebJsonOptions());
        using var doc = JsonDocument.Parse(json);
        var latestEvidenceAt = doc.RootElement.GetProperty("data").GetProperty("latestEvidenceAt").GetString();
        var latestEvidenceTimestamp = GetLatestEvidenceTimestampString(doc.RootElement.GetProperty("evidence"));

        Assert.Equal("2026-03-27T00:30:00.0000000+08:00", latestEvidenceAt);
        Assert.Equal(latestEvidenceTimestamp, latestEvidenceAt);
    }

    [Fact]
    public async Task GetSocialSentimentAsync_ShouldRefreshMarketFactsExplicitly()
    {
        var ingestionService = new CountingLocalFactIngestionService();
        var service = CreateService(
            queryTool: new FakeQueryLocalFactDatabaseTool(
                marketReports: new[]
                {
                    CreateLocalNewsItem(
                        901,
                        "market",
                        DateTime.UtcNow.AddHours(-1),
                        DateTime.UtcNow.AddMinutes(-15),
                        "大盘情绪回暖",
                        "大盘情绪回暖",
                        title: "大盘情绪回暖",
                        aiTarget: "大盘")
                }),
            sectorRotationQueryService: new NullSectorRotationQueryService(),
            localFactIngestionService: ingestionService);

        var result = await service.GetSocialSentimentAsync("sh601998", "research:1:20:sh601998::SocialSentimentMcp");

        Assert.Equal(1, ingestionService.EnsureMarketFreshCallCount);
        Assert.Equal(1, ingestionService.EnsureFreshCallCount);
        Assert.Equal(1, result.Data.MarketReports.TotalCount);
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
        Assert.NotNull(result.Data.MarketProxy);
        Assert.Contains("no_live_social_source", result.DegradedFlags);
        Assert.Contains("degraded.market_proxy_only", result.DegradedFlags);
        Assert.Contains(result.Warnings, item => item.Contains("本地情绪相关证据聚合工具", StringComparison.Ordinal));

        var json = JsonSerializer.Serialize(result, CreateWebJsonOptions());
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("data").TryGetProperty("overallSentiment", out _));
        Assert.False(doc.RootElement.GetProperty("data").GetProperty("marketProxy").TryGetProperty("overallSentiment", out _));
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
        Assert.Empty(result.Evidence);

        var json = JsonSerializer.Serialize(result, CreateWebJsonOptions());
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("data").TryGetProperty("overallSentiment", out _));
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
    public async Task GetKlineAsync_ShouldNotRefreshMarketFactsExplicitly()
    {
        var ingestionService = new CountingLocalFactIngestionService();
        var service = CreateService(localFactIngestionService: ingestionService);

        var result = await service.GetKlineAsync("sh601999", "day", 60, null, "research:1:22:sh601999::StockKlineMcp");

        Assert.Equal(1, ingestionService.EnsureFreshCallCount);
        Assert.Equal(0, ingestionService.EnsureMarketFreshCallCount);
        Assert.Equal("sh601999", result.Data.Symbol);
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
    public async Task GetNewsAsync_WhenLevelMarket_ShouldRefreshMarketFactsExplicitly()
    {
        var ingestionService = new CountingLocalFactIngestionService();
        var service = CreateService(
            queryTool: new FakeQueryLocalFactDatabaseTool(
                marketReports: new[]
                {
                    CreateLocalNewsItem(
                        902,
                        "market",
                        DateTime.UtcNow.AddHours(-2),
                        DateTime.UtcNow.AddMinutes(-20),
                        "市场快报",
                        "市场快报",
                        title: "市场快报",
                        aiTarget: "大盘")
                }),
            localFactIngestionService: ingestionService);

        var result = await service.GetNewsAsync("sh600000", "market", "research:1:21:sh600000::StockNewsMcp");

        Assert.Equal(1, ingestionService.EnsureMarketFreshCallCount);
        Assert.Equal(0, ingestionService.EnsureFreshCallCount);
        Assert.Equal("market", result.Data.Level);
        Assert.Single(result.Evidence);
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
    public async Task GetKlineAsync_ParallelBundle_ShouldReturnSameDataAsBeforeRefactor()
    {
        // Verifies that the parallel FetchSymbolDataBundleAsync produces equivalent output
        var service = CreateService();

        var result = await service.GetKlineAsync("sh600000", "day", 60, null, "task-kline-parallel");

        Assert.Equal("StockKlineMcp", result.ToolName);
        Assert.Equal("sh600000", result.Data.Symbol);
        Assert.Equal("day", result.Data.Interval);
        Assert.Equal(30, result.Data.WindowSize);
        Assert.NotEmpty(result.Data.Bars);
        Assert.NotNull(result.Data.KeyLevels);
        Assert.NotEmpty(result.Features);
        Assert.NotEmpty(result.Evidence);
    }

    [Fact]
    public async Task GetMinuteAsync_ParallelBundle_ShouldReturnSameDataAsBeforeRefactor()
    {
        var service = CreateService();

        var result = await service.GetMinuteAsync("sh600000", null, "task-minute-parallel");

        Assert.Equal("StockMinuteMcp", result.ToolName);
        Assert.Equal("sh600000", result.Data.Symbol);
        Assert.NotEmpty(result.Data.Points);
        Assert.NotNull(result.Data.Vwap);
        Assert.NotEmpty(result.Features);
        Assert.NotEmpty(result.Evidence);
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
    public async Task SearchAsync_ShouldSerializePublishedDateWithSingleChinaShift()
    {
        var publishedAtUtc = new DateTime(2026, 3, 21, 10, 0, 0, DateTimeKind.Utc);
        var payload = $$"""
{"results":[{"title":"浦发银行公告速览","url":"https://news.example.com/pingan","source":"trusted-feed","content":"公告显示公司维持稳健经营。","published_date":"{{publishedAtUtc:O}}","score":0.87}]}
""";
        var service = CreateService(
            new StockCopilotSearchOptions { Enabled = true, Provider = "tavily", ApiKey = "test-key", BaseUrl = "https://search.example.com" },
            httpClientFactory: new FakeHttpClientFactory(new StaticHttpMessageHandler(HttpStatusCode.OK, payload)));

        var result = await service.SearchAsync("浦发银行 公告", true, "task-search-serialize");

        var json = JsonSerializer.Serialize(result, CreateWebJsonOptions());
        using var doc = JsonDocument.Parse(json);
        var publishedAt = doc.RootElement.GetProperty("data").GetProperty("results")[0].GetProperty("publishedAt").GetString();
        var evidencePublishedAt = doc.RootElement.GetProperty("evidence")[0].GetProperty("publishedAt").GetString();

        Assert.Equal("2026-03-21T18:00:00.0000000+08:00", publishedAt);
        Assert.Equal(publishedAt, evidencePublishedAt);
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

    [Fact]
    public async Task GetKlineAsync_WhenQuoteUnavailable_ShouldReturnDegradedEnvelopeWithoutZeroQuoteFallback()
    {
        var service = CreateService(dataService: new FakeStockDataService(quoteAvailable: false));

        var result = await service.GetKlineAsync("sh600000", "day", 60, null, "task-kline-no-quote");

        Assert.Equal("sh600000", result.Data.Symbol);
        Assert.NotEmpty(result.Data.Bars);
        Assert.Contains("quote_unavailable", result.DegradedFlags);
        Assert.Contains(result.Warnings, warning => warning.Contains("行情数据不可用", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetKlineAsync_WhenQuoteAvailable_ShouldReturnNormalEnvelopeWithoutQuoteUnavailableWarning()
    {
        var service = CreateService();

        var result = await service.GetKlineAsync("sh600000", "day", 60, null, "task-kline-with-quote");

        Assert.Equal("sh600000", result.Data.Symbol);
        Assert.NotEmpty(result.Data.Bars);
        Assert.DoesNotContain("quote_unavailable", result.DegradedFlags);
        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("行情数据不可用", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SharedResearchTaskScope_ShouldDeduplicateSymbolRefreshAcrossTools()
    {
        const string symbol = "sh600111";
        const string taskScope = "research:1:10:sh600111";
        var ingestionService = new CountingLocalFactIngestionService(TimeSpan.FromMilliseconds(50));
        var service = CreateService(localFactIngestionService: ingestionService);

        var companyTask = service.GetCompanyOverviewAsync(symbol, $"{taskScope}::{StockMcpToolNames.CompanyOverview}");
        var fundamentalsTask = service.GetFundamentalsAsync(symbol, $"{taskScope}::{StockMcpToolNames.Fundamentals}");
        var newsTask = service.GetNewsAsync(symbol, "stock", $"{taskScope}::{StockMcpToolNames.News}");

        await Task.WhenAll(companyTask, fundamentalsTask, newsTask);
        var companyResult = await companyTask;
        var fundamentalsResult = await fundamentalsTask;
        var newsResult = await newsTask;

        Assert.Equal(1, ingestionService.EnsureFreshCallCount);
        Assert.Equal(symbol, companyResult.Data.Symbol);
        Assert.Equal(symbol, fundamentalsResult.Data.Symbol);
        Assert.Equal(symbol, newsResult.Data.Symbol);
    }

    [Fact]
    public async Task SharedResearchTaskScope_ShouldDeduplicateBundleAcrossKlineMinuteStrategy()
    {
        const string symbol = "sh600112";
        const string taskScope = "research:1:11:sh600112";
        var ingestionService = new CountingLocalFactIngestionService(TimeSpan.FromMilliseconds(50));
        var dataService = new CountingStockDataService(TimeSpan.FromMilliseconds(50));
        var queryTool = new CountingQueryLocalFactDatabaseTool();
        var service = CreateService(
            dataService: dataService,
            queryTool: queryTool,
            localFactIngestionService: ingestionService);

        var klineTask = service.GetKlineAsync(symbol, "day", 60, null, $"{taskScope}::{StockMcpToolNames.Kline}");
        var minuteTask = service.GetMinuteAsync(symbol, null, $"{taskScope}::{StockMcpToolNames.Minute}");
        var strategyTask = service.GetStrategyAsync(symbol, "day", 60, null, new[] { "ma" }, $"{taskScope}::{StockMcpToolNames.Strategy}");

        await Task.WhenAll(klineTask, minuteTask, strategyTask);
        var klineResult = await klineTask;
        var minuteResult = await minuteTask;
        var strategyResult = await strategyTask;

        Assert.Equal(1, ingestionService.EnsureFreshCallCount);
        Assert.Equal(1, dataService.GetQuoteCallCount);
        Assert.Equal(1, dataService.GetKLineCallCount);
        Assert.Equal(1, dataService.GetMinuteLineCallCount);
        Assert.Equal(1, dataService.GetIntradayMessagesCallCount);
        Assert.Equal(1, queryTool.QueryCallCount);
        Assert.NotEmpty(klineResult.Data.Bars);
        Assert.NotEmpty(minuteResult.Data.Points);
        Assert.NotEmpty(strategyResult.Data.Signals);
    }

    [Fact]
    public async Task DifferentResearchTaskScopes_ShouldNotReuseBundleAcrossTurns()
    {
        const string symbol = "sh600113";
        var dataService = new CountingStockDataService();
        var queryTool = new CountingQueryLocalFactDatabaseTool();
        var service = CreateService(dataService: dataService, queryTool: queryTool);

        await service.GetKlineAsync(symbol, "day", 60, null, "research:1:12:sh600113::StockKlineMcp");
        await service.GetKlineAsync(symbol, "day", 60, null, "research:1:13:sh600113::StockKlineMcp");

        Assert.Equal(2, dataService.GetQuoteCallCount);
        Assert.Equal(2, dataService.GetKLineCallCount);
        Assert.Equal(2, dataService.GetMinuteLineCallCount);
        Assert.Equal(2, dataService.GetIntradayMessagesCallCount);
        Assert.Equal(2, queryTool.QueryCallCount);
    }

    private static StockCopilotMcpService CreateService(
        StockCopilotSearchOptions? options = null,
        IStockDataService? dataService = null,
        IQueryLocalFactDatabaseTool? queryTool = null,
        IStockFundamentalSnapshotService? fundamentalSnapshotService = null,
        ISectorRotationQueryService? sectorRotationQueryService = null,
        IHttpClientFactory? httpClientFactory = null,
        IStockMarketContextService? marketContextService = null,
        ILlmService? llmService = null,
        StockSyncOptions? syncOptions = null,
        IRealtimeMarketOverviewService? overviewService = null,
        ILocalFactIngestionService? localFactIngestionService = null)
    {
        return new StockCopilotMcpService(
            dataService ?? new FakeStockDataService(),
            queryTool ?? new FakeQueryLocalFactDatabaseTool(),
            marketContextService ?? new FakeStockMarketContextService(),
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
            Options.Create(options ?? new StockCopilotSearchOptions { Enabled = false, Provider = "tavily" }),
            llmService,
            Options.Create(syncOptions ?? new StockSyncOptions()),
                overviewService,
                localFactIngestionService);
    }

    private static JsonSerializerOptions CreateWebJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new ChinaDateTimeJsonConverter());
        options.Converters.Add(new ChinaNullableDateTimeJsonConverter());
        return options;
    }

    private sealed class FakeStockDataService : IStockDataService
    {
        private readonly IReadOnlyList<KLinePointDto> _kLines;
        private readonly IReadOnlyList<MinuteLinePointDto> _minuteLines;
        private readonly StockQuoteDto? _quote;

        public FakeStockDataService(
            StockQuoteDto? quote = null,
            IReadOnlyList<KLinePointDto>? kLines = null,
            IReadOnlyList<MinuteLinePointDto>? minuteLines = null,
            bool quoteAvailable = true)
        {
            _quote = quoteAvailable
                ? quote ?? new StockQuoteDto("sh600000", "浦发银行", 10.5m, 0.2m, 1.9m, 13m, 8m, 10.7m, 10.1m, 0.1m, new DateTime(2026, 3, 21, 10, 0, 0), Array.Empty<StockNewsDto>(), Array.Empty<StockIndicatorDto>(), 320_000_000_000m, 2.8m, 100000, "银行")
                : null;
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

        public Task<StockQuoteDto?> GetQuoteAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_quote is null ? null : _quote with { Symbol = symbol });
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
        private readonly string? _name;
        private readonly string? _sectorName;
        private readonly IReadOnlyList<LocalFundamentalFactDto> _fundamentalFacts;
        private readonly IReadOnlyList<LocalNewsItemDto> _stockNews;
        private readonly IReadOnlyList<LocalNewsItemDto> _sectorReports;
        private readonly IReadOnlyList<LocalNewsItemDto> _marketReports;
        private readonly DateTime _publishedAt;
        private readonly DateTime _ingestedAt;
        private readonly DateTime? _fundamentalUpdatedAt;

        public FakeQueryLocalFactDatabaseTool(
            string? excerpt = "公告摘要",
            string? summary = "公告摘要",
            string? name = "浦发银行",
            string? sectorName = "银行",
            IReadOnlyList<LocalFundamentalFactDto>? fundamentalFacts = null,
            IReadOnlyList<LocalNewsItemDto>? stockNews = null,
            IReadOnlyList<LocalNewsItemDto>? sectorReports = null,
            IReadOnlyList<LocalNewsItemDto>? marketReports = null,
            DateTime? publishedAt = null,
            DateTime? ingestedAt = null,
            DateTime? fundamentalUpdatedAt = null)
        {
            _excerpt = excerpt;
            _summary = summary;
            _name = name;
            _sectorName = sectorName;
            _fundamentalFacts = fundamentalFacts ?? new[] { new LocalFundamentalFactDto("机构目标价", "11.5", "东方财富") };
            _publishedAt = publishedAt ?? DateTime.UtcNow.AddHours(-1);
            _ingestedAt = ingestedAt ?? DateTime.UtcNow.AddMinutes(-30);
            _fundamentalUpdatedAt = fundamentalUpdatedAt ?? DateTime.UtcNow.AddHours(-2);
            _stockNews = stockNews ?? new[] { CreateLocalNewsItem(1, "stock", _publishedAt, _ingestedAt, _excerpt, _summary) };
            _sectorReports = sectorReports ?? Array.Empty<LocalNewsItemDto>();
            _marketReports = marketReports ?? Array.Empty<LocalNewsItemDto>();
        }

        public Task<LocalFactPackageDto> QueryAsync(string symbol, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LocalFactPackageDto(
                symbol,
                _name,
                _sectorName,
                _stockNews,
                _sectorReports,
                _marketReports,
                _fundamentalUpdatedAt,
                _fundamentalFacts));
        }

        public Task<LocalNewsBucketDto> QueryLevelAsync(string symbol, string level, CancellationToken cancellationToken = default)
        {
            var items = level switch
            {
                "sector" => _sectorReports,
                "market" => _marketReports,
                _ => _stockNews
            };
            return Task.FromResult(new LocalNewsBucketDto(symbol, level, _sectorName, items));
        }

        public Task<LocalNewsBucketDto> QueryMarketAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LocalNewsBucketDto("market", "market", null, _marketReports));
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

    private sealed class CountingStockDataService : IStockDataService
    {
        private readonly TimeSpan _delay;
        private readonly StockQuoteDto _quote = new("sh600000", "浦发银行", 10.5m, 0.2m, 1.9m, 13m, 8m, 10.7m, 10.1m, 0.1m, new DateTime(2026, 3, 21, 10, 0, 0), Array.Empty<StockNewsDto>(), Array.Empty<StockIndicatorDto>(), 320_000_000_000m, 2.8m, 100000, "银行");
        private readonly IReadOnlyList<KLinePointDto> _kLines = Enumerable.Range(0, 30)
            .Select(index => new KLinePointDto(new DateTime(2026, 2, 1).AddDays(index), 9.8m + index * 0.02m, 9.9m + index * 0.02m, 10m + index * 0.02m, 9.7m + index * 0.02m, 1000 + index * 20))
            .ToArray();
        private readonly IReadOnlyList<MinuteLinePointDto> _minuteLines = new[]
        {
            new MinuteLinePointDto(new DateOnly(2026, 3, 21), new TimeSpan(9, 30, 0), 10.2m, 10.2m, 100),
            new MinuteLinePointDto(new DateOnly(2026, 3, 21), new TimeSpan(10, 0, 0), 10.3m, 10.25m, 180),
            new MinuteLinePointDto(new DateOnly(2026, 3, 21), new TimeSpan(14, 50, 0), 10.45m, 10.32m, 220)
        };

        private int _getQuoteCallCount;
        private int _getKLineCallCount;
        private int _getMinuteLineCallCount;
        private int _getIntradayMessagesCallCount;

        public CountingStockDataService(TimeSpan? delay = null)
        {
            _delay = delay ?? TimeSpan.Zero;
        }

        public int GetQuoteCallCount => _getQuoteCallCount;

        public int GetKLineCallCount => _getKLineCallCount;

        public int GetMinuteLineCallCount => _getMinuteLineCallCount;

        public int GetIntradayMessagesCallCount => _getIntradayMessagesCallCount;

        public async Task<StockQuoteDto> GetQuoteAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _getQuoteCallCount);
            await MaybeDelayAsync(cancellationToken);
            return _quote with { Symbol = symbol };
        }

        public Task<MarketIndexDto> GetMarketIndexAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public async Task<IReadOnlyList<KLinePointDto>> GetKLineAsync(string symbol, string interval, int count, string? source = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _getKLineCallCount);
            await MaybeDelayAsync(cancellationToken);
            return _kLines;
        }

        public async Task<IReadOnlyList<MinuteLinePointDto>> GetMinuteLineAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _getMinuteLineCallCount);
            await MaybeDelayAsync(cancellationToken);
            return _minuteLines;
        }

        public async Task<IReadOnlyList<IntradayMessageDto>> GetIntradayMessagesAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _getIntradayMessagesCallCount);
            await MaybeDelayAsync(cancellationToken);
            return new[]
            {
                new IntradayMessageDto("浦发银行公告", "上交所公告", new DateTime(2026, 3, 21, 8, 30, 0), "https://example.com/a")
            };
        }

        private Task MaybeDelayAsync(CancellationToken cancellationToken)
        {
            return _delay > TimeSpan.Zero ? Task.Delay(_delay, cancellationToken) : Task.CompletedTask;
        }
    }

    private sealed class CountingQueryLocalFactDatabaseTool : IQueryLocalFactDatabaseTool
    {
        private int _queryCallCount;

        public int QueryCallCount => _queryCallCount;

        public Task<LocalFactPackageDto> QueryAsync(string symbol, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _queryCallCount);
            return Task.FromResult(new LocalFactPackageDto(
                symbol,
                "浦发银行",
                "银行",
                new[]
                {
                    CreateLocalNewsItem(1, "stock", DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddMinutes(-30), "公告摘要", "公告摘要")
                },
                Array.Empty<LocalNewsItemDto>(),
                Array.Empty<LocalNewsItemDto>(),
                DateTime.UtcNow.AddHours(-2),
                new[]
                {
                    new LocalFundamentalFactDto("机构目标价", "11.5", "东方财富")
                }));
        }

        public Task<LocalNewsBucketDto> QueryLevelAsync(string symbol, string level, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LocalNewsBucketDto(
                symbol,
                level,
                "银行",
                new[]
                {
                    CreateLocalNewsItem(1, level, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddMinutes(-30), "公告摘要", "公告摘要")
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

    private sealed class CountingLocalFactIngestionService : ILocalFactIngestionService
    {
        private readonly TimeSpan _delay;
        private int _ensureFreshCallCount;
        private int _ensureMarketFreshCallCount;

        public CountingLocalFactIngestionService(TimeSpan? delay = null)
        {
            _delay = delay ?? TimeSpan.Zero;
        }

        public int EnsureFreshCallCount => _ensureFreshCallCount;

        public int EnsureMarketFreshCallCount => _ensureMarketFreshCallCount;

        public Task SyncAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public async Task EnsureMarketFreshAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _ensureMarketFreshCallCount);
            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cancellationToken);
            }
        }

        public async Task EnsureFreshAsync(string symbol, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _ensureFreshCallCount);
            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cancellationToken);
            }
        }
    }

    private sealed class FakeStockMarketContextService : IStockMarketContextService
    {
        public Task<StockMarketContextDto?> GetLatestAsync(string symbol, CancellationToken cancellationToken = default)
            => GetLatestAsync(symbol, null, cancellationToken);

        public Task<StockMarketContextDto?> GetLatestAsync(string symbol, string? sectorNameHint, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<StockMarketContextDto?>(new StockMarketContextDto("主升", 72m, "银行", "银行", "BK001", 80m, 0.8m, "积极执行", false, true));
        }
    }

    private sealed class EmptyLabelStockMarketContextService : IStockMarketContextService
    {
        public Task<StockMarketContextDto?> GetLatestAsync(string symbol, CancellationToken cancellationToken = default)
            => GetLatestAsync(symbol, null, cancellationToken);

        public Task<StockMarketContextDto?> GetLatestAsync(string symbol, string? sectorNameHint, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<StockMarketContextDto?>(new StockMarketContextDto(string.Empty, 72m, "银行", "银行", "BK001", 80m, 0.8m, string.Empty, false, true));
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

        public Task<string> GetMainlineTrendSummaryAsync(int days, CancellationToken cancellationToken = default)
        {
            return Task.FromResult("=== 近30天板块趋势摘要 ===\n03-26: 主线=[银行(90),保险(85),券商(80)]\n");
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

        public Task<string> GetMainlineTrendSummaryAsync(int days, CancellationToken cancellationToken = default)
        {
            return Task.FromResult("暂无板块趋势历史数据。");
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

    private sealed class FakeLlmService : ILlmService
    {
        private readonly Func<string, LlmChatRequest, CancellationToken, Task<LlmChatResult>> _handler;

        public FakeLlmService(Func<string, LlmChatRequest, CancellationToken, Task<LlmChatResult>>? handler = null)
        {
            _handler = handler ?? ((_, _, _) => Task.FromResult(new LlmChatResult("{\"marketRecognitionDirections\":[],\"reason\":\"默认空结果\"}")));
        }

        public string? LastProvider { get; private set; }

        public LlmChatRequest? LastRequest { get; private set; }

        public async Task<LlmChatResult> ChatAsync(string provider, LlmChatRequest request, CancellationToken cancellationToken = default)
        {
            LastProvider = provider;
            LastRequest = request;
            return await _handler(provider, request, cancellationToken);
        }
    }

    private sealed class NullFundamentalSnapshotService : IStockFundamentalSnapshotService
    {
        public Task<StockFundamentalSnapshotDto?> GetSnapshotAsync(string symbol, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<StockFundamentalSnapshotDto?>(null);
        }
    }

    private static string? GetLatestEvidenceTimestampString(JsonElement evidence)
    {
        string? latestRaw = null;
        DateTimeOffset? latestValue = null;

        foreach (var item in evidence.EnumerateArray())
        {
            foreach (var propertyName in new[] { "publishedAt", "crawledAt", "ingestedAt" })
            {
                if (!item.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var raw = property.GetString();
                if (string.IsNullOrWhiteSpace(raw) || !DateTimeOffset.TryParse(raw, out var parsed))
                {
                    continue;
                }

                if (!latestValue.HasValue || parsed > latestValue.Value)
                {
                    latestValue = parsed;
                    latestRaw = raw;
                }
            }
        }

        return latestRaw;
    }

    private static IReadOnlyList<LocalNewsItemDto> CreateLocalNewsItems(int count)
    {
        return Enumerable.Range(1, count)
            .Select(index => CreateLocalNewsItem(
                index,
                "stock",
                DateTime.UtcNow.AddHours(-index),
                DateTime.UtcNow.AddMinutes(-index * 10),
                $"公告摘要{index}",
                $"公告摘要{index}"))
            .ToArray();
    }

    private static LocalNewsItemDto CreateLocalNewsItem(
        int id,
        string level,
        DateTime publishedAt,
        DateTime ingestedAt,
        string? excerpt,
        string? summary,
        string? title = null,
        string? translatedTitle = null,
        string? source = null,
        string? aiTarget = null,
        IReadOnlyList<string>? aiTags = null)
    {
        return new LocalNewsItemDto(
            id,
            $"{level}_news:{id}",
            title ?? $"浦发银行公告{id}",
            translatedTitle,
            source ?? "上交所公告",
            "announcement",
            level,
            "利好",
            publishedAt,
            ingestedAt,
            $"https://example.com/{id}",
            excerpt,
            summary,
            "local_fact",
            "summary_only",
            ingestedAt,
            aiTarget ?? "个股:浦发银行",
            aiTags ?? new[] { "公告" },
            true);
    }

    private sealed class FakeRealtimeMarketOverviewService : IRealtimeMarketOverviewService
    {
        public Task<IReadOnlyList<BatchStockQuoteDto>> GetBatchQuotesAsync(IReadOnlyList<string> symbols, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<BatchStockQuoteDto>>(Array.Empty<BatchStockQuoteDto>());

        public Task<MarketRealtimeOverviewDto> GetOverviewAsync(IReadOnlyList<string>? indexSymbols = null, CancellationToken cancellationToken = default)
        {
            var snapshotTime = new DateTime(2026, 3, 28, 10, 0, 0, DateTimeKind.Utc);
            return Task.FromResult(new MarketRealtimeOverviewDto(
                snapshotTime,
                new[]
                {
                    new BatchStockQuoteDto("sh000001", "上证指数", 3200m, 10m, 0.31m, 3210m, 3180m, 0.5m, 0m, 300_000_000_000m, 1.0m, snapshotTime),
                    new BatchStockQuoteDto("sz399001", "深证成指", 10500m, -30m, -0.28m, 10550m, 10450m, 0.6m, 0m, 400_000_000_000m, 0.9m, snapshotTime),
                    new BatchStockQuoteDto("sz399006", "创业板指", 2100m, 5m, 0.24m, 2120m, 2080m, 0.7m, 0m, 100_000_000_000m, 1.1m, snapshotTime)
                },
                new MarketCapitalFlowSnapshotDto(snapshotTime, new DateOnly(2026, 3, 28), "亿元", -50m, 20m, 15m, -40m, -45m, Array.Empty<MarketCapitalFlowPointDto>()),
                new NorthboundFlowSnapshotDto(snapshotTime, "2026-03-28", "亿元", 20m, 100m, 10m, 80m, 30m, Array.Empty<NorthboundFlowPointDto>()),
                new MarketBreadthDistributionDto(new DateOnly(2026, 3, 28), 2100, 2500, 200, 45, 8, Array.Empty<MarketBreadthBucketDto>())));
        }
    }
}