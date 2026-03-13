using SimplerJiangAiAgent.Api.Infrastructure.Jobs;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class LocalFactIngestionServiceTests
{
    [Fact]
    public void BuildFallbackMarketReports_WhenKeywordsMissing_ShouldFallbackToRecentMessages()
    {
        var messages = new[]
        {
            new IntradayMessageDto("题材轮动观察", "新浪", new DateTime(2026, 3, 12, 9, 30, 0), "https://example.com/market")
        };

        var result = LocalFactIngestionService.BuildFallbackMarketReports(messages, new DateTime(2026, 3, 12, 9, 35, 0));

        Assert.Single(result);
        Assert.Equal("market", result[0].Level);
        Assert.Equal("sina-roll-market-fallback", result[0].SourceTag);
    }

    [Fact]
    public void BuildFallbackMarketReports_PrefersKeywordMatchedMessages()
    {
        var messages = new[]
        {
            new IntradayMessageDto("题材轮动观察", "新浪", new DateTime(2026, 3, 12, 9, 30, 0), "https://example.com/a"),
            new IntradayMessageDto("北向资金午评：指数震荡", "新浪", new DateTime(2026, 3, 12, 10, 0, 0), "https://example.com/b")
        };

        var result = LocalFactIngestionService.BuildFallbackMarketReports(messages, new DateTime(2026, 3, 12, 10, 5, 0));

        Assert.Single(result);
        Assert.Equal("北向资金午评：指数震荡", result[0].Title);
    }
}