using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;
using Xunit;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class StockAgentPromptBuilderTests
{
    [Fact]
    public void BuildPrompt_CommanderContainsGoal007StructuredFields()
    {
        var prompt = StockAgentPromptBuilder.BuildPrompt(
            StockAgentKind.Commander,
            "{}",
            Array.Empty<StockAgentResultDto>());

        Assert.Contains("\"action\"", prompt);
        Assert.Contains("\"targetPrice\"", prompt);
        Assert.Contains("\"evidence\"", prompt);
        Assert.Contains("\"triggers\"", prompt);
        Assert.Contains("\"invalidations\"", prompt);
        Assert.Contains("\"riskLimits\"", prompt);
    }

    [Fact]
    public void BuildRepairPrompt_TrendContainsStructuredRiskFields()
    {
        var prompt = StockAgentPromptBuilder.BuildRepairPrompt(StockAgentKind.TrendAnalysis, "bad");

        Assert.Contains("\"confidence\"", prompt);
        Assert.Contains("\"evidence\"", prompt);
        Assert.Contains("\"triggers\"", prompt);
        Assert.Contains("\"invalidations\"", prompt);
        Assert.Contains("\"riskLimits\"", prompt);
    }

    [Fact]
    public void BuildPrompt_StockNewsContainsFreshnessAndEvidenceConstraints()
    {
        var prompt = StockAgentPromptBuilder.BuildPrompt(
            StockAgentKind.StockNews,
            "{}",
            Array.Empty<StockAgentResultDto>());

        Assert.Contains("最近72小时", prompt);
        Assert.Contains("扩窗到7天", prompt);
        Assert.Contains("\"crawledAt\"", prompt);
    }

    [Fact]
    public void BuildPrompt_SectorNewsContainsFreshnessAndEvidenceConstraints()
    {
        var prompt = StockAgentPromptBuilder.BuildPrompt(
            StockAgentKind.SectorNews,
            "{}",
            Array.Empty<StockAgentResultDto>());

        Assert.Contains("最近72小时", prompt);
        Assert.Contains("扩窗到7天", prompt);
        Assert.Contains("\"crawledAt\"", prompt);
    }
}
