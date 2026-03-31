using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class QueryLocalFactDatabaseToolTests
{
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

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new AppDbContext(options);
    }

    private static QueryLocalFactDatabaseTool CreateTool(AppDbContext dbContext)
    {
        return new QueryLocalFactDatabaseTool(dbContext, new NoOpLocalFactArticleReadService());
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
}