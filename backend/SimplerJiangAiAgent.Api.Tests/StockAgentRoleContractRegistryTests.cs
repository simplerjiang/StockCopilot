using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class StockAgentRoleContractRegistryTests
{
    private readonly StockAgentRoleContractRegistry _registry = new();

    [Fact]
    public void BuildChecklist_ShouldCoverFrozenFifteenRoles_AndPromoteProductToLocalRequired()
    {
        var checklist = _registry.BuildChecklist();

        Assert.Equal(15, checklist.Roles.Count);
        Assert.Equal(StockAgentRoleIds.All, checklist.Roles.Select(item => item.RoleId).ToArray());

        var product = _registry.GetRequired(StockAgentRoleIds.ProductAnalyst);

        Assert.Equal("local_required", product.ToolAccessMode);
        Assert.True(product.AllowsDirectQueryTools);
        Assert.Equal(StockMcpToolNames.Product, product.PreferredMcpSequence[0]);
        Assert.Contains("StockProductMcp", product.FallbackRule);
        Assert.Contains("最小产品事实", product.StopRule);
        Assert.Null(product.Reason);
    }

    [Fact]
    public void Contracts_ShouldLockMarketLocalFirst_AndNewsExternalFallbackRule()
    {
        var market = _registry.GetRequired(StockAgentRoleIds.MarketAnalyst);
        var news = _registry.GetRequired(StockAgentRoleIds.NewsAnalyst);

        Assert.Equal("local_required", market.ToolAccessMode);
        Assert.Equal(
            new[] { StockMcpToolNames.MarketContext, StockMcpToolNames.Kline, StockMcpToolNames.Minute, StockMcpToolNames.Strategy },
            market.PreferredMcpSequence);
        Assert.DoesNotContain(StockMcpToolNames.Search, market.PreferredMcpSequence);
        Assert.Contains("不允许自动 external fallback", market.FallbackRule);

        Assert.Equal("local_required", news.ToolAccessMode);
        Assert.Equal(StockMcpToolNames.News, news.PreferredMcpSequence[0]);
        Assert.Equal(StockMcpToolNames.Search, news.PreferredMcpSequence[^1]);
        Assert.Contains("StockSearchMcp", news.FallbackRule);
        Assert.Contains("local-first", news.StopRule, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Contracts_ShouldDisableDirectQueryTools_ForResearchersManagersTraderRiskAndPortfolio()
    {
        var nonQueryRoleIds = new[]
        {
            StockAgentRoleIds.BullResearcher,
            StockAgentRoleIds.BearResearcher,
            StockAgentRoleIds.ResearchManager,
            StockAgentRoleIds.Trader,
            StockAgentRoleIds.AggressiveRiskAnalyst,
            StockAgentRoleIds.NeutralRiskAnalyst,
            StockAgentRoleIds.ConservativeRiskAnalyst,
            StockAgentRoleIds.PortfolioManager
        };

        foreach (var roleId in nonQueryRoleIds)
        {
            var contract = _registry.GetRequired(roleId);

            Assert.Equal("disabled", contract.ToolAccessMode);
            Assert.False(contract.AllowsDirectQueryTools);
            Assert.Empty(contract.PreferredMcpSequence);
            Assert.NotNull(contract.Reason);
        }
    }
}