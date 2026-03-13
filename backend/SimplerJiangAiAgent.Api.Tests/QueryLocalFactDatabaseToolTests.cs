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

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new AppDbContext(options);
    }
}