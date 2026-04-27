using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;
using Xunit;

namespace SimplerJiangAiAgent.Api.Tests;

public class StockNewsImpactServiceTests
{
    [Fact]
    public void Evaluate_UsesKeywordScoringToSummarizeImpact()
    {
        var service = new StockNewsImpactService();
        var messages = new List<IntradayMessageDto>
        {
            new("公司宣布回购计划", "新浪", DateTime.Today, null),
            new("公司被罚并遭遇诉讼", "新浪", DateTime.Today, null),
            new("公司召开业绩说明会", "新浪", DateTime.Today, null)
        };

        var result = service.Evaluate("sh000001", "测试公司", messages);

        Assert.Equal(1, result.Summary.Positive);
        Assert.Equal(1, result.Summary.Negative);
        Assert.Equal(1, result.Summary.Neutral);
        Assert.Equal(3, result.Events.Count);

        Assert.All(result.Events, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.EventType));
            Assert.True(item.TypeWeight > 0);
            Assert.True(item.SourceCredibility > 0);
            Assert.False(string.IsNullOrWhiteSpace(item.Theme));
            Assert.True(item.MergedCount >= 1);
        });
    }

    [Fact]
    public void Evaluate_MergesSameThemeEvents()
    {
        var service = new StockNewsImpactService();
        var now = DateTime.Now;
        var messages = new List<IntradayMessageDto>
        {
            new("公司公告：回购计划获批", "上交所公告", now.AddHours(-1), "https://a"),
            new("公司回购计划正式实施", "新浪财经", now.AddHours(-2), "https://b"),
            new("公司被罚并遭遇诉讼", "新浪财经", now.AddHours(-1), "https://c")
        };

        var result = service.Evaluate("sh000001", "测试公司", messages);

        Assert.True(result.Events.Count <= 3);
        Assert.Contains(result.Events, item => item.MergedCount >= 2 && item.Theme == "股份回购");
    }

    [Fact]
    public void Evaluate_SameThemeStrongPositiveWithNeutralMessages_DoesNotDiluteStrongSignal()
    {
        var service = new StockNewsImpactService();
        var now = DateTime.Now;
        var messages = new List<IntradayMessageDto>
        {
            new("公司公告：业绩增长盈利利好超预期", "上交所公告", now.AddHours(-1), null),
            new("公司公告：一季度业绩说明会", "上交所公告", now.AddHours(-2), null),
            new("公司公告：二季度业绩说明会", "上交所公告", now.AddHours(-3), null),
            new("公司公告：三季度业绩说明会", "上交所公告", now.AddHours(-4), null),
            new("公司公告：年度业绩交流会", "上交所公告", now.AddHours(-5), null),
            new("公司公告：投资者业绩问答", "上交所公告", now.AddHours(-6), null)
        };

        var result = service.Evaluate("sh000001", "测试公司", messages);

        Assert.DoesNotContain(result.Events, item => item.Theme == "业绩表现" && item.MergedCount == messages.Count && item.ImpactScore == 0 && item.Category == "中性");
        var positiveEvent = Assert.Single(result.Events, item => item.Theme == "业绩表现" && item.Category == "利好");
        Assert.True(positiveEvent.ImpactScore > 0);

        var neutralEvent = result.Events.SingleOrDefault(item => item.Theme == "业绩表现" && item.Category == "中性");
        Assert.True(neutralEvent is not null || positiveEvent.MergedCount == messages.Count);
    }

    [Fact]
    public void Evaluate_SameThemePositiveMessages_MergesWithStrongestPositiveScoreInsteadOfAverage()
    {
        var service = new StockNewsImpactService();
        var now = DateTime.Now;
        var messages = new List<IntradayMessageDto>
        {
            new("公司公告：业绩增长盈利利好超预期", "上交所公告", now.AddHours(-1), null),
            new("公司公告：业绩增长", "上交所公告", now.AddHours(-2), null),
            new("公司公告：盈利改善", "上交所公告", now.AddHours(-3), null),
            new("公司公告：业绩增长进展", "上交所公告", now.AddHours(-4), null),
            new("公司公告：盈利能力提升", "上交所公告", now.AddHours(-5), null),
            new("公司公告：业绩增长更新", "上交所公告", now.AddHours(-6), null)
        };

        var result = service.Evaluate("sh000001", "测试公司", messages);

        var merged = Assert.Single(result.Events, item => item.Theme == "业绩表现" && item.Category == "利好");
        Assert.Equal(messages.Count, merged.MergedCount);
        Assert.Equal(100, merged.ImpactScore);
        Assert.NotEqual(44, merged.ImpactScore);
    }

    [Fact]
    public void Evaluate_SameThemeStrongNegativeWithNeutralMessages_DoesNotDiluteStrongSignal()
    {
        var service = new StockNewsImpactService();
        var now = DateTime.Now;
        var messages = new List<IntradayMessageDto>
        {
            new("公司公告：业绩下滑亏损利空降级", "上交所公告", now.AddHours(-1), null),
            new("公司公告：一季度业绩说明会", "上交所公告", now.AddHours(-2), null),
            new("公司公告：二季度业绩说明会", "上交所公告", now.AddHours(-3), null),
            new("公司公告：三季度业绩说明会", "上交所公告", now.AddHours(-4), null),
            new("公司公告：年度业绩交流会", "上交所公告", now.AddHours(-5), null),
            new("公司公告：投资者业绩问答", "上交所公告", now.AddHours(-6), null)
        };

        var result = service.Evaluate("sh000001", "测试公司", messages);

        Assert.DoesNotContain(result.Events, item => item.Theme == "业绩表现" && item.MergedCount == messages.Count && item.ImpactScore == 0 && item.Category == "中性");
        var negativeEvent = Assert.Single(result.Events, item => item.Theme == "业绩表现" && item.Category == "利空");
        Assert.True(negativeEvent.ImpactScore < 0);

        var neutralEvent = result.Events.SingleOrDefault(item => item.Theme == "业绩表现" && item.Category == "中性");
        Assert.True(neutralEvent is not null || negativeEvent.MergedCount == messages.Count);
    }

    [Fact]
    public void Evaluate_SameThemeNegativeMessages_MergesWithStrongestNegativeScoreInsteadOfAverage()
    {
        var service = new StockNewsImpactService();
        var now = DateTime.Now;
        var messages = new List<IntradayMessageDto>
        {
            new("公司公告：业绩下滑亏损利空降级", "上交所公告", now.AddHours(-1), null),
            new("公司公告：业绩下滑", "上交所公告", now.AddHours(-2), null),
            new("公司公告：亏损扩大", "上交所公告", now.AddHours(-3), null),
            new("公司公告：业绩下滑进展", "上交所公告", now.AddHours(-4), null),
            new("公司公告：亏损风险提示", "上交所公告", now.AddHours(-5), null),
            new("公司公告：业绩下滑更新", "上交所公告", now.AddHours(-6), null)
        };

        var result = service.Evaluate("sh000001", "测试公司", messages);

        var merged = Assert.Single(result.Events, item => item.Theme == "业绩表现" && item.Category == "利空");
        Assert.Equal(messages.Count, merged.MergedCount);
        Assert.Equal(-100, merged.ImpactScore);
        Assert.NotEqual(-44, merged.ImpactScore);
    }

    [Fact]
    public void Evaluate_PureNeutralSameThemeMessages_RemainsNeutral()
    {
        var service = new StockNewsImpactService();
        var now = DateTime.Now;
        var messages = new List<IntradayMessageDto>
        {
            new("公司公告：一季度业绩说明会", "上交所公告", now.AddHours(-1), null),
            new("公司公告：二季度业绩说明会", "上交所公告", now.AddHours(-2), null),
            new("公司公告：三季度业绩说明会", "上交所公告", now.AddHours(-3), null)
        };

        var result = service.Evaluate("sh000001", "测试公司", messages);

        var merged = Assert.Single(result.Events, item => item.Theme == "业绩表现");
        Assert.Equal(messages.Count, merged.MergedCount);
        Assert.Equal("中性", merged.Category);
        Assert.Equal(0, merged.ImpactScore);
    }
}
