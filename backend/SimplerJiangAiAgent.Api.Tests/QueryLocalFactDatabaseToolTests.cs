using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
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

        var tool = new QueryLocalFactDatabaseTool(dbContext);
        var result = await tool.QueryAsync("600000");

        Assert.Equal("sh600000", result.Symbol);
        Assert.Equal("浦发银行", result.Name);
        Assert.Equal("银行", result.SectorName);
        Assert.Single(result.StockNews);
        Assert.Single(result.SectorReports);
        Assert.Single(result.MarketReports);
        Assert.Equal("利好", result.StockNews[0].Sentiment);
        Assert.Equal("个股:浦发银行", result.StockNews[0].AiTarget);
        Assert.Contains("财报业绩", result.StockNews[0].AiTags);
        Assert.Equal("中性", result.SectorReports[0].Sentiment);
        Assert.Equal("中性", result.MarketReports[0].Sentiment);
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
            PublishTime = new DateTime(2026, 3, 13, 8, 0, 0),
            CrawledAt = new DateTime(2026, 3, 13, 8, 5, 0),
            Url = "https://example.com/market"
        });
        await dbContext.SaveChangesAsync();

        var tool = new QueryLocalFactDatabaseTool(dbContext);
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

        var tool = new QueryLocalFactDatabaseTool(dbContext);
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

        var tool = new QueryLocalFactDatabaseTool(dbContext);
        var result = await tool.QueryArchiveAsync(null, "market", null, 2, 1);

        Assert.Equal(2, result.Total);
        var item = Assert.Single(result.Items);
        Assert.Equal("market", item.Level);
        Assert.Equal("Market 2", item.Title);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new AppDbContext(options);
    }
}