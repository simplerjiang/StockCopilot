using SimplerJiangAiAgent.Api.Modules.Stocks.Services;
using Xunit;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class StockAgentModelRoutingPolicyTests
{
    [Fact]
    public void ResolveModel_ReturnsDefaultModel_ForStandardAnalysis()
    {
        var result = StockAgentModelRoutingPolicy.ResolveModel(null, false);

        Assert.Equal(StockAgentModelRoutingPolicy.DefaultModel, result);
    }

    [Fact]
    public void ResolveModel_ReturnsProModel_WhenProEnabled()
    {
        var result = StockAgentModelRoutingPolicy.ResolveModel(StockAgentModelRoutingPolicy.DefaultModel, true);

        Assert.Equal(StockAgentModelRoutingPolicy.ProModel, result);
    }

    [Fact]
    public void ResolveModel_DowngradesExplicitProModel_WhenProDisabled()
    {
        var result = StockAgentModelRoutingPolicy.ResolveModel(StockAgentModelRoutingPolicy.ProModel, false);

        Assert.Equal(StockAgentModelRoutingPolicy.DefaultModel, result);
    }

    [Fact]
    public void ResolveModel_PreservesExplicitNonProModel_WhenProDisabled()
    {
        const string customModel = "gemini-3.1-flash-lite-preview-thinking-custom";

        var result = StockAgentModelRoutingPolicy.ResolveModel(customModel, false);

        Assert.Equal(customModel, result);
    }
}