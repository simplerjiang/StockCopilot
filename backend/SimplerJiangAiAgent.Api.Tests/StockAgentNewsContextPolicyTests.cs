using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;
using Xunit;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class StockAgentNewsContextPolicyTests
{
    [Fact]
    public void Apply_PrefersTrustedSourcesAndRecentWindow()
    {
        var now = new DateTime(2026, 3, 2, 12, 0, 0);
        var messages = new List<IntradayMessageDto>
        {
            new("A股公告：回购", "上交所公告", now.AddHours(-2), "https://trusted"),
            new("公司公告2", "新浪财经", now.AddHours(-3), "https://trusted-2"),
            new("公司公告3", "东方财富", now.AddHours(-4), "https://trusted-3"),
            new("公司公告4", "腾讯财经", now.AddHours(-5), "https://trusted-4"),
            new("公司公告5", "财联社", now.AddHours(-6), "https://trusted-5"),
            new("公司公告6", "证券时报", now.AddHours(-7), "https://trusted-6"),
            new("传闻消息", "某论坛", now.AddHours(-1), "https://untrusted")
        };

        var result = StockAgentNewsContextPolicy.Apply(messages, now);

        Assert.False(result.Policy.ExpandedWindow);
        Assert.Equal(72, result.Policy.ActualLookbackHours);
        Assert.Equal(72, result.Policy.ActualLookbackHours);
        Assert.All(result.Messages, item => Assert.DoesNotContain("论坛", item.Source));
        Assert.Equal("上交所公告", result.Messages[0].Source);
    }

    [Fact]
    public void Apply_ExpandsToSevenDaysWhenRecentMessagesInsufficient()
    {
        var now = new DateTime(2026, 3, 2, 12, 0, 0);
        var messages = new List<IntradayMessageDto>
        {
            new("公司公告1", "新浪财经", now.AddHours(-100), null),
            new("公司公告2", "新浪财经", now.AddHours(-120), null),
            new("公司公告3", "新浪财经", now.AddHours(-140), null)
        };

        var result = StockAgentNewsContextPolicy.Apply(messages, now);

        Assert.True(result.Policy.ExpandedWindow);
        Assert.Equal(168, result.Policy.ActualLookbackHours);
        Assert.Equal(3, result.Messages.Count);
    }

    [Fact]
    public void Apply_RemovesMessagesOlderThanSevenDays()
    {
        var now = new DateTime(2026, 3, 2, 12, 0, 0);
        var messages = new List<IntradayMessageDto>
        {
            new("近期消息", "新浪财经", now.AddHours(-30), null),
            new("过期消息", "新浪财经", now.AddDays(-10), null)
        };

        var result = StockAgentNewsContextPolicy.Apply(messages, now);

        Assert.Single(result.Messages);
        Assert.Equal("近期消息", result.Messages[0].Title);
        Assert.Equal(1, result.Policy.CandidateCount);
    }
}
