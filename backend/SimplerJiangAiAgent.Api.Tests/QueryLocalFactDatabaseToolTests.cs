using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using System.Runtime.CompilerServices;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class QueryLocalFactDatabaseToolTests
{
    private static readonly ConditionalWeakTable<AppDbContext, DbContextOptions<AppDbContext>> ContextOptionsMap = new();

    [Fact]
    public async Task QueryAsync_ShouldReturnStockSectorAndMarketBuckets()
    {
        await using var dbContext = CreateDbContext();
        dbContext.LocalStockNews.Add(new LocalStockNews
        {
            Symbol = "sh600000",
            Name = "浦发银行",
            SectorName = "银行",
            Title = "浦发银行公告",
            TranslatedTitle = "浦发银行公告",
            Category = "announcement",
            Source = "东方财富公告",
            SourceTag = "eastmoney-announcement",
            AiSentiment = "利好",
            AiTarget = "个股:浦发银行",
            AiTags = "[\"财报业绩\",\"政策红利\"]",
            IsAiProcessed = true,
            PublishTime = new DateTime(2026, 3, 12, 9, 30, 0),
            CrawledAt = new DateTime(2026, 3, 12, 9, 31, 0),
            Url = "https://example.com/a"
        });
        dbContext.LocalSectorReports.AddRange(
            new LocalSectorReport
            {
                Symbol = "sh600000",
                SectorName = "银行",
                Level = "sector",
                Title = "银行板块震荡上行",
                TranslatedTitle = null,
                Source = "新浪",
                SourceTag = "sina-roll-sector",
                AiSentiment = "中性",
                AiTarget = "板块:银行",
                AiTags = "[\"行业周期\"]",
                IsAiProcessed = true,
                PublishTime = new DateTime(2026, 3, 12, 9, 0, 0),
                CrawledAt = new DateTime(2026, 3, 12, 9, 31, 0)
            },
            new LocalSectorReport
            {
                Symbol = null,
                SectorName = "大盘环境",
                Level = "market",
                Title = "A股早评：指数震荡",
                TranslatedTitle = null,
                Source = "新浪",
                SourceTag = "sina-roll-market",
                AiSentiment = "中性",
                AiTarget = "大盘",
                AiTags = "[\"资金面\"]",
                IsAiProcessed = true,
                PublishTime = new DateTime(2026, 3, 12, 8, 50, 0),
                CrawledAt = new DateTime(2026, 3, 12, 9, 31, 0)
            });
        await dbContext.SaveChangesAsync();

        var tool = CreateTool(dbContext);
        var result = await tool.QueryAsync("600000");

        Assert.Equal("sh600000", result.Symbol);
        Assert.Equal("浦发银行", result.Name);
        Assert.Equal("银行", result.SectorName);
        Assert.Single(result.StockNews);
        Assert.Single(result.SectorReports);
        Assert.Single(result.MarketReports);
        Assert.Equal("利好", result.StockNews[0].Sentiment);
        Assert.Contains(result.StockNews[0].ReadMode, new[] { "local_fact", "url_unavailable", "url_fetched" });
        Assert.Contains(result.StockNews[0].ReadStatus, new[] { "unverified", "title_only", "summary_only", "fetch_failed" });
        Assert.Equal("个股:浦发银行", result.StockNews[0].AiTarget);
        Assert.Contains("财报业绩", result.StockNews[0].AiTags);
        Assert.Equal("中性", result.SectorReports[0].Sentiment);
        Assert.Equal("中性", result.MarketReports[0].Sentiment);
    }

    [Fact]
    public async Task QueryAsync_ShouldFallbackToSectorRotationSnapshotAndFundamentalFacts()
    {
        await using var dbContext = CreateDbContext();
        dbContext.StockCompanyProfiles.Add(new StockCompanyProfile
        {
            Symbol = "sz000021",
            Name = "深科技",
            SectorName = "半导体",
            FundamentalUpdatedAt = new DateTime(2026, 3, 17, 7, 5, 0, DateTimeKind.Utc),
            FundamentalFactsJson = StockFundamentalSnapshotMapper.SerializeFacts(new[]
            {
                new StockFundamentalFactDto("营收", "112.78亿", "东方财富"),
                new StockFundamentalFactDto("机构目标价", "35.50", "东方财富")
            }),
            UpdatedAt = new DateTime(2026, 3, 17, 7, 5, 0, DateTimeKind.Utc)
        });
        dbContext.SectorRotationSnapshots.Add(new SectorRotationSnapshot
        {
            TradingDate = new DateTime(2026, 3, 17),
            SnapshotTime = new DateTime(2026, 3, 17, 7, 10, 0, DateTimeKind.Utc),
            BoardType = "concept",
            SectorCode = "BK001",
            SectorName = "半导体",
            RankNo = 1,
            StrengthScore = 86m,
            StrengthAvg5d = 80m,
            StrengthAvg10d = 75m,
            DiffusionRate = 78m,
            MainlineScore = 82m,
            IsMainline = true,
            NewsSentiment = "利好",
            SourceTag = "test",
            CreatedAt = new DateTime(2026, 3, 17, 7, 10, 0, DateTimeKind.Utc)
        });
        await dbContext.SaveChangesAsync();

        var snapshot = await dbContext.SectorRotationSnapshots.SingleAsync();
        dbContext.SectorRotationLeaderSnapshots.Add(new SectorRotationLeaderSnapshot
        {
            SectorRotationSnapshotId = snapshot.Id,
            RankInSector = 1,
            Symbol = "sz000021",
            Name = "深科技",
            ChangePercent = 9.98m,
            TurnoverAmount = 123000000m,
            IsLimitUp = true,
            IsBrokenBoard = false
        });
        await dbContext.SaveChangesAsync();

        var tool = CreateTool(dbContext);
        var result = await tool.QueryAsync("sz000021");

        Assert.Equal("深科技", result.Name);
        Assert.Equal("半导体", result.SectorName);
        Assert.NotEmpty(result.SectorReports);
        Assert.Equal("本地板块轮动快照", result.SectorReports[0].Source);
        Assert.Equal("利好", result.SectorReports[0].Sentiment);
        Assert.Equal(2, result.FundamentalFacts.Count);
        Assert.Equal("机构目标价", result.FundamentalFacts[1].Label);
    }

    [Fact]
    public async Task QueryAsync_ShouldFallbackToLatestLeaderSnapshotWhenSectorNameDoesNotMatch()
    {
        await using var dbContext = CreateDbContext();
        dbContext.StockCompanyProfiles.Add(new StockCompanyProfile
        {
            Symbol = "sh603259",
            Name = "药明康德",
            SectorName = "医疗行业",
            UpdatedAt = new DateTime(2026, 3, 17, 6, 0, 0, DateTimeKind.Utc)
        });
        dbContext.SectorRotationSnapshots.Add(new SectorRotationSnapshot
        {
            TradingDate = new DateTime(2026, 3, 17),
            SnapshotTime = new DateTime(2026, 3, 17, 6, 22, 58, DateTimeKind.Utc),
            BoardType = "concept",
            SectorCode = "BK0611",
            SectorName = "上证50_",
            RankNo = 19,
            StrengthScore = 70m,
            StrengthAvg5d = 68m,
            StrengthAvg10d = 65m,
            DiffusionRate = 61m,
            MainlineScore = 75.38m,
            IsMainline = false,
            NewsSentiment = "中性",
            SourceTag = "test",
            CreatedAt = new DateTime(2026, 3, 17, 6, 22, 58, DateTimeKind.Utc)
        });
        dbContext.SectorRotationSnapshots.Add(new SectorRotationSnapshot
        {
            TradingDate = new DateTime(2026, 3, 17),
            SnapshotTime = new DateTime(2026, 3, 17, 6, 52, 15, DateTimeKind.Utc),
            BoardType = "concept",
            SectorCode = "BK1645",
            SectorName = "昨日打二板以上表现",
            RankNo = 1,
            StrengthScore = 88m,
            StrengthAvg5d = 80m,
            StrengthAvg10d = 77m,
            DiffusionRate = 79m,
            MainlineScore = 89.44m,
            IsMainline = true,
            NewsSentiment = "利好",
            SourceTag = "test",
            CreatedAt = new DateTime(2026, 3, 17, 6, 52, 15, DateTimeKind.Utc)
        });
        await dbContext.SaveChangesAsync();

        var snapshot = await dbContext.SectorRotationSnapshots.SingleAsync(item => item.SectorName == "上证50_");
        dbContext.SectorRotationLeaderSnapshots.Add(new SectorRotationLeaderSnapshot
        {
            SectorRotationSnapshotId = snapshot.Id,
            RankInSector = 5,
            Symbol = "603259",
            Name = "药明康德",
            ChangePercent = 2.01m,
            TurnoverAmount = 98000000m,
            IsLimitUp = false,
            IsBrokenBoard = false,
            CreatedAt = new DateTime(2026, 3, 17, 6, 22, 58, DateTimeKind.Utc)
        });
        await dbContext.SaveChangesAsync();

        var tool = CreateTool(dbContext);
        var result = await tool.QueryAsync("sh603259");

        Assert.NotEmpty(result.SectorReports);
        Assert.Contains("上证50_", result.SectorReports[0].Title);
        Assert.Equal("本地板块轮动快照", result.SectorReports[0].Source);
    }

    [Fact]
    public async Task QueryAsync_ShouldPreferNewestMatchingSectorSnapshot()
    {
        await using var dbContext = CreateDbContext();
        dbContext.StockCompanyProfiles.Add(new StockCompanyProfile
        {
            Symbol = "sz000021",
            Name = "深科技",
            SectorName = "消费电子",
            UpdatedAt = new DateTime(2026, 3, 17, 8, 0, 0, DateTimeKind.Utc)
        });
        dbContext.SectorRotationSnapshots.AddRange(
            new SectorRotationSnapshot
            {
                TradingDate = new DateTime(2026, 3, 16),
                SnapshotTime = new DateTime(2026, 3, 16, 14, 0, 0, DateTimeKind.Utc),
                BoardType = "industry",
                SectorCode = "BK100",
                SectorName = "消费电子",
                RankNo = 12,
                MainlineScore = 50m,
                DiffusionRate = 40m,
                SourceTag = "test",
                CreatedAt = new DateTime(2026, 3, 16, 14, 0, 0, DateTimeKind.Utc)
            },
            new SectorRotationSnapshot
            {
                TradingDate = new DateTime(2026, 3, 17),
                SnapshotTime = new DateTime(2026, 3, 17, 9, 0, 0, DateTimeKind.Utc),
                BoardType = "industry",
                SectorCode = "BK100",
                SectorName = "消费电子",
                RankNo = 5,
                MainlineScore = 78m,
                DiffusionRate = 66m,
                SourceTag = "test",
                CreatedAt = new DateTime(2026, 3, 17, 9, 0, 0, DateTimeKind.Utc)
            });
        await dbContext.SaveChangesAsync();

        var tool = CreateTool(dbContext);
        var result = await tool.QueryAsync("sz000021");

        Assert.NotEmpty(result.SectorReports);
        Assert.Contains("排名第5", result.SectorReports[0].Title);
    }

    [Fact]
    public async Task QueryAsync_ShouldFallbackToMarketContextWhenNoSectorSnapshotExists()
    {
        await using var dbContext = CreateDbContext();
        dbContext.StockCompanyProfiles.Add(new StockCompanyProfile
        {
            Symbol = "sz000021",
            Name = "深科技",
            SectorName = "消费电子",
            UpdatedAt = new DateTime(2026, 3, 17, 8, 0, 0, DateTimeKind.Utc)
        });
        dbContext.LocalSectorReports.Add(new LocalSectorReport
        {
            Symbol = null,
            SectorName = "大盘环境",
            Level = "market",
            Title = "市场风险偏好边际回暖",
            Source = "测试源",
            SourceTag = "market-fallback-test",
            AiSentiment = "中性",
            AiTarget = "大盘",
            AiTags = "[\"资金面\"]",
            PublishTime = new DateTime(2026, 3, 17, 8, 30, 0, DateTimeKind.Utc),
            CrawledAt = new DateTime(2026, 3, 17, 8, 31, 0, DateTimeKind.Utc)
        });
        await dbContext.SaveChangesAsync();

        var tool = CreateTool(dbContext);
        var result = await tool.QueryAsync("sz000021");

        Assert.NotEmpty(result.SectorReports);
        Assert.Equal("本地市场环境摘要", result.SectorReports[0].Source);
        Assert.Contains("消费电子板块暂无专属本地资讯", result.SectorReports[0].Title);
    }

    [Fact]
    public async Task QueryMarketAsync_ShouldReturnMarketBucketWithoutSymbol()
    {
        await using var dbContext = CreateDbContext();
        dbContext.LocalSectorReports.Add(new LocalSectorReport
        {
            Symbol = null,
            SectorName = "大盘环境",
            Level = "market",
            Title = "全球风险偏好回升",
            Source = "WSJ US Business",
            SourceTag = "wsj-us-business-rss",
            PublishTime = DateTime.UtcNow.AddHours(-2),
            CrawledAt = DateTime.UtcNow.AddHours(-1),
            Url = "https://example.com/market"
        });
        await dbContext.SaveChangesAsync();

        var tool = CreateTool(dbContext);
        var result = await tool.QueryMarketAsync();

        Assert.Equal(string.Empty, result.Symbol);
        Assert.Equal("market", result.Level);
        Assert.Equal("大盘环境", result.SectorName);
        Assert.Single(result.Items);
        Assert.Equal("全球风险偏好回升", result.Items[0].Title);
    }

    [Fact]
    public async Task QueryArchiveAsync_ShouldMergeAndFilterAcrossLevels()
    {
        await using var dbContext = CreateDbContext();
        dbContext.LocalStockNews.Add(new LocalStockNews
        {
            Symbol = "sh600000",
            Name = "浦发银行",
            SectorName = "银行",
            Title = "Fed outlook keeps markets cautious",
            TranslatedTitle = "美联储前景偏谨慎，市场维持观望",
            Category = "company_news",
            Source = "CNBC Finance",
            SourceTag = "cnbc-finance-rss",
            AiSentiment = "中性",
            AiTarget = "个股:浦发银行",
            AiTags = "[\"宏观货币\"]",
            PublishTime = new DateTime(2026, 3, 13, 8, 0, 0, DateTimeKind.Utc),
            CrawledAt = new DateTime(2026, 3, 13, 8, 5, 0, DateTimeKind.Utc),
            Url = "https://example.com/stock"
        });
        dbContext.LocalSectorReports.AddRange(
            new LocalSectorReport
            {
                Symbol = "sh600000",
                SectorName = "银行",
                Level = "sector",
                Title = "Banking sector holds up",
                TranslatedTitle = "银行板块维持强势",
                Source = "Seeking Alpha",
                SourceTag = "seeking-alpha-rss",
                AiSentiment = "利好",
                AiTarget = "板块:银行",
                AiTags = "[\"行业周期\"]",
                PublishTime = new DateTime(2026, 3, 13, 7, 30, 0, DateTimeKind.Utc),
                CrawledAt = new DateTime(2026, 3, 13, 7, 35, 0, DateTimeKind.Utc),
                Url = "https://example.com/sector"
            },
            new LocalSectorReport
            {
                Symbol = null,
                SectorName = "大盘环境",
                Level = "market",
                Title = "Global demand cools in March",
                TranslatedTitle = "全球需求在 3 月放缓",
                Source = "TechCrunch",
                SourceTag = "techcrunch-rss",
                AiSentiment = "利空",
                AiTarget = "大盘",
                AiTags = "[\"海外映射\"]",
                PublishTime = new DateTime(2026, 3, 13, 7, 0, 0, DateTimeKind.Utc),
                CrawledAt = new DateTime(2026, 3, 13, 7, 5, 0, DateTimeKind.Utc),
                Url = "https://example.com/market"
            });
        await dbContext.SaveChangesAsync();

        var tool = CreateTool(dbContext);
        var result = await tool.QueryArchiveAsync("市场", null, "中性", 1, 20);

        Assert.Equal(1, result.Total);
        var item = Assert.Single(result.Items);
        Assert.Equal("stock", item.Level);
        Assert.Equal("美联储前景偏谨慎，市场维持观望", item.TranslatedTitle);
        Assert.Equal("中性", item.Sentiment);
    }

    [Fact]
    public async Task QueryArchiveAsync_ShouldRespectLevelAndPagination()
    {
        await using var dbContext = CreateDbContext();
        dbContext.LocalSectorReports.AddRange(
            new LocalSectorReport
            {
                Symbol = null,
                SectorName = "大盘环境",
                Level = "market",
                Title = "Market 1",
                Source = "CNBC Finance",
                SourceTag = "cnbc-finance-rss",
                AiSentiment = "中性",
                PublishTime = new DateTime(2026, 3, 13, 10, 0, 0, DateTimeKind.Utc),
                CrawledAt = new DateTime(2026, 3, 13, 10, 1, 0, DateTimeKind.Utc)
            },
            new LocalSectorReport
            {
                Symbol = null,
                SectorName = "大盘环境",
                Level = "market",
                Title = "Market 2",
                Source = "The Hill",
                SourceTag = "thehill-rss",
                AiSentiment = "中性",
                PublishTime = new DateTime(2026, 3, 13, 9, 0, 0, DateTimeKind.Utc),
                CrawledAt = new DateTime(2026, 3, 13, 9, 1, 0, DateTimeKind.Utc)
            });
        await dbContext.SaveChangesAsync();

        var tool = CreateTool(dbContext);
        var result = await tool.QueryArchiveAsync(null, "market", null, 2, 1);

        Assert.Equal(2, result.Total);
        var item = Assert.Single(result.Items);
        Assert.Equal("market", item.Level);
        Assert.Equal("Market 2", item.Title);
    }

    [Fact]
    public async Task QueryArchiveAsync_ShouldReturnReadabilitySummaryForFilteredArchive()
    {
        await using var dbContext = CreateDbContext();
        dbContext.LocalStockNews.AddRange(
            new LocalStockNews
            {
                Symbol = "sh600000",
                Name = "浦发银行",
                SectorName = "银行",
                Title = "浦发银行公告",
                Source = "测试源",
                SourceTag = "stock-test",
                AiSentiment = "中性",
                PublishTime = new DateTime(2026, 4, 26, 10, 0, 0, DateTimeKind.Utc),
                CrawledAt = new DateTime(2026, 4, 26, 10, 1, 0, DateTimeKind.Utc),
                Url = "https://example.com/stock",
                ReadMode = "url_fetched"
            },
            new LocalStockNews
            {
                Symbol = "sz000001",
                Name = "平安银行",
                SectorName = "银行",
                Title = "平安银行快讯",
                Source = "测试源",
                SourceTag = "stock-test",
                AiSentiment = "中性",
                PublishTime = new DateTime(2026, 4, 26, 9, 0, 0, DateTimeKind.Utc),
                CrawledAt = new DateTime(2026, 4, 26, 9, 1, 0, DateTimeKind.Utc),
                ReadMode = "url_unavailable"
            },
            new LocalStockNews
            {
                Symbol = "sh601398",
                Name = "工商银行",
                SectorName = "银行",
                Title = "工商银行资讯",
                Source = "测试源",
                SourceTag = "stock-test",
                AiSentiment = "利好",
                PublishTime = new DateTime(2026, 4, 26, 8, 0, 0, DateTimeKind.Utc),
                CrawledAt = new DateTime(2026, 4, 26, 8, 1, 0, DateTimeKind.Utc),
                ReadMode = "local_fact"
            });
        await dbContext.SaveChangesAsync();

        var tool = CreateTool(dbContext);
        var result = await tool.QueryArchiveAsync(null, "stock", null, 1, 1);

        Assert.Equal(3, result.Total);
        Assert.Single(result.Items);
        Assert.Equal(2, result.ReadableTotal);
        Assert.Equal(0.6667m, result.ReadableRate);
        Assert.Equal(1, result.UrlUnavailableTotal);
        Assert.Equal(0.3333m, result.UrlUnavailableRate);
    }

    [Fact]
    public async Task QueryArchiveAsync_ShouldRepairLegacyNavigationChromeInArchiveSnippetsAndPersistIt()
    {
        await using var dbContext = CreateDbContext();
        const string navChrome = "财经 焦点 股票 新股 期指 期权 行情 数据 ";
        dbContext.LocalStockNews.Add(new LocalStockNews
        {
            Symbol = "sh600000",
            Name = "浦发银行",
            SectorName = "银行",
            Title = "浦发银行公告",
            Category = "company_news",
            Source = "测试源",
            SourceTag = "legacy-stock-test",
            ArticleExcerpt = navChrome + "这是一条真正的公告正文，说明公司订单进展稳定。",
            ArticleSummary = navChrome + "这是一条真正的公告摘要，说明本次事项未见新增重大风险。",
            PublishTime = new DateTime(2026, 4, 26, 10, 0, 0, DateTimeKind.Utc),
            CrawledAt = new DateTime(2026, 4, 26, 10, 1, 0, DateTimeKind.Utc)
        });
        dbContext.LocalSectorReports.Add(new LocalSectorReport
        {
            Symbol = null,
            SectorName = "大盘环境",
            Level = "market",
            Title = "市场摘要",
            Source = "测试源",
            SourceTag = "legacy-market-test",
            ArticleExcerpt = navChrome + "板块资金回流，风险偏好回暖，成交额同步放大。",
            ArticleSummary = navChrome + "板块资金回流，风险偏好回暖。",
            PublishTime = new DateTime(2026, 4, 26, 9, 0, 0, DateTimeKind.Utc),
            CrawledAt = new DateTime(2026, 4, 26, 9, 1, 0, DateTimeKind.Utc)
        });
        await dbContext.SaveChangesAsync();

        var tool = CreateTool(dbContext);
        var result = await tool.QueryArchiveAsync(null, null, null, 1, 20);

        Assert.Equal(2, result.Total);
        var stockItem = Assert.Single(result.Items, item => item.SourceRecordId == "stock_news:1");
        Assert.Equal("这是一条真正的公告正文，说明公司订单进展稳定。", stockItem.Excerpt);
        Assert.Equal("这是一条真正的公告摘要，说明本次事项未见新增重大风险。", stockItem.Summary);
        var marketItem = Assert.Single(result.Items, item => item.SourceRecordId == "sector_report:1");
        Assert.Equal("板块资金回流，风险偏好回暖，成交额同步放大。", marketItem.Excerpt);
        Assert.Equal("板块资金回流，风险偏好回暖。", marketItem.Summary);

        dbContext.ChangeTracker.Clear();
        var persistedStock = await dbContext.LocalStockNews.SingleAsync();
        Assert.Equal(stockItem.Excerpt, persistedStock.ArticleExcerpt);
        Assert.Equal(stockItem.Summary, persistedStock.ArticleSummary);
        var persistedReport = await dbContext.LocalSectorReports.SingleAsync();
        Assert.Equal(marketItem.Excerpt, persistedReport.ArticleExcerpt);
        Assert.Equal(marketItem.Summary, persistedReport.ArticleSummary);
    }

    [Fact]
    public async Task QueryArchiveAsync_ShouldKeepNaturalFinanceAndStockTextInArchiveSnippets()
    {
        await using var dbContext = CreateDbContext();
        const string naturalExcerpt = "财经政策影响股票市场，公司管理层表示订单交付保持稳定，后续将继续关注成本。";
        const string naturalSummary = "财经政策影响股票市场，公司管理层表示订单交付保持稳定。";
        dbContext.LocalStockNews.Add(new LocalStockNews
        {
            Symbol = "sh600000",
            Name = "浦发银行",
            SectorName = "银行",
            Title = "自然正文样本",
            Category = "company_news",
            Source = "测试源",
            SourceTag = "natural-stock-test",
            ArticleExcerpt = naturalExcerpt,
            ArticleSummary = naturalSummary,
            PublishTime = new DateTime(2026, 4, 26, 10, 0, 0, DateTimeKind.Utc),
            CrawledAt = new DateTime(2026, 4, 26, 10, 1, 0, DateTimeKind.Utc)
        });
        await dbContext.SaveChangesAsync();

        var tool = CreateTool(dbContext);
        var result = await tool.QueryArchiveAsync(null, "stock", null, 1, 20);

        var item = Assert.Single(result.Items);
        Assert.Equal(naturalExcerpt, item.Excerpt);
        Assert.Equal(naturalSummary, item.Summary);

        dbContext.ChangeTracker.Clear();
        var persisted = await dbContext.LocalStockNews.SingleAsync();
        Assert.Equal(naturalExcerpt, persisted.ArticleExcerpt);
        Assert.Equal(naturalSummary, persisted.ArticleSummary);
    }

    [Fact]
    public async Task QueryArchiveAsync_ShouldRepairLegacyMismatchedAiTargetAndPersistIt()
    {
        await using var dbContext = CreateDbContext();
        dbContext.LocalStockNews.Add(new LocalStockNews
        {
            Symbol = "sh09969",
            Name = "诺诚健华",
            SectorName = "创新药",
            Title = "诺诚健华发布新药临床进展公告",
            Category = "company_news",
            Source = "测试源",
            SourceTag = "legacy-ai-target-test",
            AiSentiment = "中性",
            AiTarget = "中国电力设备",
            IsAiProcessed = true,
            PublishTime = new DateTime(2026, 4, 24, 10, 0, 0, DateTimeKind.Utc),
            CrawledAt = new DateTime(2026, 4, 24, 10, 1, 0, DateTimeKind.Utc)
        });
        await dbContext.SaveChangesAsync();

        var tool = CreateTool(dbContext);
        var result = await tool.QueryArchiveAsync(null, "stock", null, 1, 20);

        var item = Assert.Single(result.Items);
        Assert.Equal("诺诚健华", item.AiTarget);
        Assert.NotEqual("中国电力设备", item.AiTarget);

        dbContext.ChangeTracker.Clear();
        var persisted = await dbContext.LocalStockNews.SingleAsync();
        Assert.Equal("诺诚健华", persisted.AiTarget);
    }

    [Fact]
    public async Task QueryArchiveAsync_ShouldRepairMoutaiArchiveLabelsFromSingleStockFactSource()
    {
        await using var dbContext = CreateDbContext();
        dbContext.StockCompanyProfiles.Add(new StockCompanyProfile
        {
            Symbol = "sh600519",
            Name = "贵州茅台",
            SectorName = "酿酒行业",
            UpdatedAt = new DateTime(2026, 4, 26, 8, 0, 0, DateTimeKind.Utc)
        });
        dbContext.LocalStockNews.Add(new LocalStockNews
        {
            Symbol = "sh600519",
            Name = "中国石化",
            SectorName = "酿酒行业",
            Title = "贵州茅台：2026年第一季度主要经营数据公告",
            Category = "announcement",
            Source = "东方财富公告",
            SourceTag = "eastmoney-announcement",
            AiSentiment = "中性",
            AiTarget = "中国石化",
            AiTags = "[\"中国石化\",\"行业预期\"]",
            IsAiProcessed = true,
            PublishTime = new DateTime(2026, 4, 24, 11, 49, 32, DateTimeKind.Utc),
            CrawledAt = new DateTime(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc)
        });
        await dbContext.SaveChangesAsync();

        var tool = CreateTool(dbContext);
        var result = await tool.QueryArchiveAsync("600519", "stock", null, 1, 20);

        var item = Assert.Single(result.Items);
        Assert.Equal("sh600519", item.Symbol);
        Assert.Equal("贵州茅台", item.Name);
        Assert.Equal("酿酒行业", item.SectorName);
        Assert.Equal("贵州茅台", item.AiTarget);
        Assert.DoesNotContain("中国石化", item.AiTags);
        Assert.Contains("行业预期", item.AiTags);

        dbContext.ChangeTracker.Clear();
        var persisted = await dbContext.LocalStockNews.SingleAsync();
        Assert.Equal("贵州茅台", persisted.Name);
        Assert.Equal("贵州茅台", persisted.AiTarget);
        var persistedTags = System.Text.Json.JsonSerializer.Deserialize<string[]>(persisted.AiTags ?? "[]") ?? [];
        Assert.DoesNotContain("中国石化", persistedTags);
        Assert.Contains("行业预期", persistedTags);
    }

    [Fact]
    public async Task QueryArchiveAsync_ShouldRepairLegacyEastmoneyAnnouncementLocalTimeAndPersistIt()
    {
        await using var dbContext = CreateDbContext();
        dbContext.LocalStockNews.Add(new LocalStockNews
        {
            Symbol = "sh600519",
            Name = "贵州茅台",
            SectorName = "白酒",
            Title = "贵州茅台公告",
            Category = "announcement",
            Source = "东方财富公告",
            SourceTag = "eastmoney-announcement",
            ExternalId = "AN202604241821562475",
            PublishTime = new DateTime(2026, 4, 24, 19, 49, 32, DateTimeKind.Unspecified),
            CrawledAt = new DateTime(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc),
            Url = "https://example.com/announcement"
        });
        await dbContext.SaveChangesAsync();

        var tool = CreateTool(dbContext);
        var result = await tool.QueryArchiveAsync(null, "stock", null, 1, 20);

        var item = Assert.Single(result.Items);
        Assert.Equal(new DateTime(2026, 4, 24, 11, 49, 32, DateTimeKind.Utc), item.PublishTime);

        dbContext.ChangeTracker.Clear();
        var persisted = await dbContext.LocalStockNews.SingleAsync();
        Assert.Equal(new DateTime(2026, 4, 24, 11, 49, 32, DateTimeKind.Utc), persisted.PublishTime);
    }

    [Fact]
    public async Task QueryArchiveAsync_ShouldSortAfterLegacyEastmoneyRepair()
    {
        await using var dbContext = CreateDbContext();
        dbContext.LocalStockNews.AddRange(
            new LocalStockNews
            {
                Symbol = "sh600519",
                Name = "贵州茅台",
                SectorName = "白酒",
                Title = "旧东财公告脏时间",
                Category = "announcement",
                Source = "东方财富公告",
                SourceTag = "eastmoney-announcement",
                PublishTime = new DateTime(2026, 4, 24, 19, 49, 32, DateTimeKind.Unspecified),
                CrawledAt = new DateTime(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc)
            },
            new LocalStockNews
            {
                Symbol = "sh600000",
                Name = "浦发银行",
                SectorName = "银行",
                Title = "真正更晚的正常新闻",
                Category = "company_news",
                Source = "新浪",
                SourceTag = "sina-company-news",
                PublishTime = new DateTime(2026, 4, 24, 12, 30, 0, DateTimeKind.Utc),
                CrawledAt = new DateTime(2026, 4, 24, 12, 35, 0, DateTimeKind.Utc)
            });
        await dbContext.SaveChangesAsync();

        var tool = CreateTool(dbContext);
        var result = await tool.QueryArchiveAsync(null, "stock", null, 1, 20);

        Assert.Equal(2, result.Total);
        Assert.Equal("真正更晚的正常新闻", result.Items[0].Title);
        Assert.Equal("旧东财公告脏时间", result.Items[1].Title);
        Assert.Equal(new DateTime(2026, 4, 24, 11, 49, 32, DateTimeKind.Utc), result.Items[1].PublishTime);
    }

    [Fact]
    public async Task QueryArchiveAsync_ShouldNotChangeNormalHistoricalEastmoneyAnnouncement()
    {
        await using var dbContext = CreateDbContext();
        var originalPublishTime = new DateTime(2026, 4, 24, 11, 49, 32, DateTimeKind.Utc);
        dbContext.LocalStockNews.Add(new LocalStockNews
        {
            Symbol = "sh600519",
            Name = "贵州茅台",
            SectorName = "白酒",
            Title = "正常东财公告",
            Category = "announcement",
            Source = "东方财富公告",
            SourceTag = "eastmoney-announcement",
            PublishTime = originalPublishTime,
            CrawledAt = new DateTime(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc)
        });
        await dbContext.SaveChangesAsync();

        var tool = CreateTool(dbContext);
        var result = await tool.QueryArchiveAsync(null, "stock", null, 1, 20);

        Assert.Equal(originalPublishTime, Assert.Single(result.Items).PublishTime);
        dbContext.ChangeTracker.Clear();
        Assert.Equal(originalPublishTime, (await dbContext.LocalStockNews.SingleAsync()).PublishTime);
    }

    [Fact]
    public async Task QueryAsync_ShouldFilterOutWeakStockMatchesAndHideDistortedChineseTranslation()
    {
        await using var dbContext = CreateDbContext();
        dbContext.StockCompanyProfiles.Add(new StockCompanyProfile
        {
            Symbol = "sz000021",
            Name = "深科技",
            SectorName = "半导体",
            UpdatedAt = new DateTime(2026, 3, 18, 8, 0, 0, DateTimeKind.Utc)
        });
        dbContext.LocalStockNews.AddRange(
            new LocalStockNews
            {
                Symbol = "sz000021",
                Name = "深科技",
                SectorName = "半导体",
                Title = "深科技与某客户签署战略合作协议",
                TranslatedTitle = "深科技与某客户签署战略合作协议（失真改写）",
                Source = "测试源",
                SourceTag = "stock-1",
                AiSentiment = "利好",
                AiTarget = "个股:深科技",
                PublishTime = new DateTime(2026, 3, 18, 8, 10, 0, DateTimeKind.Utc),
                CrawledAt = new DateTime(2026, 3, 18, 8, 11, 0, DateTimeKind.Utc)
            },
            new LocalStockNews
            {
                Symbol = "sz000021",
                Name = "深科技",
                SectorName = "半导体",
                Title = "半导体板块震荡，市场观望情绪升温",
                Source = "测试源",
                SourceTag = "stock-2",
                AiSentiment = "中性",
                AiTarget = "板块:半导体",
                PublishTime = new DateTime(2026, 3, 18, 8, 5, 0, DateTimeKind.Utc),
                CrawledAt = new DateTime(2026, 3, 18, 8, 6, 0, DateTimeKind.Utc)
            });
        await dbContext.SaveChangesAsync();

        var tool = CreateTool(dbContext);
        var result = await tool.QueryAsync("sz000021");

        var item = Assert.Single(result.StockNews);
        Assert.Equal("深科技与某客户签署战略合作协议", item.Title);
        Assert.Null(item.TranslatedTitle);
    }

    [Fact]
    public async Task QueryAsync_ShouldPersistPreparedArticleFieldsWhenArticleReadServiceMutatesTrackedEntities()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var root = new InMemoryDatabaseRoot();

        await using (var seedContext = CreateDbContext(databaseName, root))
        {
            seedContext.LocalStockNews.Add(new LocalStockNews
            {
                Symbol = "sh600000",
                Name = "浦发银行",
                SectorName = "银行",
                Title = "浦发银行公告",
                Source = "测试源",
                SourceTag = "stock-test",
                AiSentiment = "利好",
                AiTarget = "个股:浦发银行",
                PublishTime = new DateTime(2026, 3, 18, 9, 0, 0, DateTimeKind.Utc),
                CrawledAt = new DateTime(2026, 3, 18, 9, 1, 0, DateTimeKind.Utc),
                Url = "https://example.com/stock"
            });
            await seedContext.SaveChangesAsync();

            var tool = CreateTool(seedContext, new MutatingLocalFactArticleReadService());
            var result = await tool.QueryAsync("600000");

            var item = Assert.Single(result.StockNews);
            Assert.Equal("prepared excerpt", item.Excerpt);
            Assert.Equal("prepared summary", item.Summary);
            Assert.Equal("url_fetched", item.ReadMode);
            Assert.Equal("full_text_read", item.ReadStatus);
            Assert.NotNull(item.IngestedAt);
        }

        await using var verifyContext = CreateDbContext(databaseName, root);
        var saved = await verifyContext.LocalStockNews.SingleAsync();
        Assert.Equal("prepared excerpt", saved.ArticleExcerpt);
        Assert.Equal("prepared summary", saved.ArticleSummary);
        Assert.Equal("url_fetched", saved.ReadMode);
        Assert.Equal("full_text_read", saved.ReadStatus);
        Assert.NotNull(saved.IngestedAt);
    }

    [Fact]
    public async Task QueryAsync_ConcurrentCalls_ShouldMaterializeDifferentTrackedEntitiesPerCall()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var root = new InMemoryDatabaseRoot();

        await using var dbContext = CreateDbContext(databaseName, root);
        dbContext.LocalStockNews.Add(new LocalStockNews
        {
            Symbol = "sh600000",
            Name = "浦发银行",
            SectorName = "银行",
            Title = "浦发银行公告",
            Source = "测试源",
            SourceTag = "stock-test",
            AiSentiment = "利好",
            AiTarget = "个股:浦发银行",
            PublishTime = new DateTime(2026, 3, 18, 9, 0, 0, DateTimeKind.Utc),
            CrawledAt = new DateTime(2026, 3, 18, 9, 1, 0, DateTimeKind.Utc)
        });
        await dbContext.SaveChangesAsync();

        var articleReadService = new ConcurrentCaptureLocalFactArticleReadService();
        var tool = CreateTool(dbContext, articleReadService);

        var results = await Task.WhenAll(
            tool.QueryAsync("600000"),
            tool.QueryAsync("600000"));

        Assert.All(results, result => Assert.Single(result.StockNews));
        Assert.Equal(2, articleReadService.StockItems.Count);
        Assert.NotSame(articleReadService.StockItems[0], articleReadService.StockItems[1]);
    }

    private static AppDbContext CreateDbContext(string? databaseName = null, InMemoryDatabaseRoot? root = null)
    {
        databaseName ??= Guid.NewGuid().ToString("N");
        root ??= new InMemoryDatabaseRoot();

        // V040-DEBT-1: 全量回归 20+ DbContextOptions 后 EF 触发 ManyServiceProvidersCreatedWarning 抛错；
        // 这是测试基础设施警告（每个测试 new options 是 InMemory 测试的常规模式），不是生产问题，按 EF 文档忽略。
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName, root)
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        var context = new AppDbContext(options);
        ContextOptionsMap.Add(context, options);
        return context;
    }

    private static QueryLocalFactDatabaseTool CreateTool(
        AppDbContext dbContext,
        ILocalFactArticleReadService? articleReadService = null)
    {
        return new QueryLocalFactDatabaseTool(
            GetOptions(dbContext),
            articleReadService ?? new NoOpLocalFactArticleReadService());
    }

    private static DbContextOptions<AppDbContext> GetOptions(AppDbContext dbContext)
    {
        return ContextOptionsMap.TryGetValue(dbContext, out var options)
            ? options
            : throw new InvalidOperationException("Missing AppDbContext options for test instance.");
    }

    private sealed class NoOpLocalFactArticleReadService : ILocalFactArticleReadService
    {
        public Task PrepareAsync(IReadOnlyList<LocalStockNews> items, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task PrepareAsync(IReadOnlyList<LocalSectorReport> items, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class MutatingLocalFactArticleReadService : ILocalFactArticleReadService
    {
        public Task PrepareAsync(IReadOnlyList<LocalStockNews> items, CancellationToken cancellationToken = default)
        {
            foreach (var item in items)
            {
                item.ArticleExcerpt = "prepared excerpt";
                item.ArticleSummary = "prepared summary";
                item.ReadMode = "url_fetched";
                item.ReadStatus = "full_text_read";
                item.IngestedAt = new DateTime(2026, 3, 18, 9, 2, 0, DateTimeKind.Utc);
            }

            return Task.CompletedTask;
        }

        public Task PrepareAsync(IReadOnlyList<LocalSectorReport> items, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class ConcurrentCaptureLocalFactArticleReadService : ILocalFactArticleReadService
    {
        private readonly TaskCompletionSource _bothCallsReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _stockPrepareCount;

        public List<LocalStockNews> StockItems { get; } = new();

        public async Task PrepareAsync(IReadOnlyList<LocalStockNews> items, CancellationToken cancellationToken = default)
        {
            lock (StockItems)
            {
                if (items.Count > 0)
                {
                    StockItems.Add(items[0]);
                }
            }

            if (Interlocked.Increment(ref _stockPrepareCount) >= 2)
            {
                _bothCallsReached.TrySetResult();
            }

            await _bothCallsReached.Task.WaitAsync(cancellationToken);
        }

        public Task PrepareAsync(IReadOnlyList<LocalSectorReport> items, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}