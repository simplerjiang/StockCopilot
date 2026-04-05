using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SimplerJiangAiAgent.Api.Modules.Stocks;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services.Recommend.WebSearch;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class StockMcpGatewayPhaseATests
{
    private static readonly IWebSearchService StubWebSearch = new StubWebSearchService();
    private static RoleToolPolicyService CreatePolicy()
    {
        return new RoleToolPolicyService(new McpServiceRegistry(), new StockAgentRoleContractRegistry());
    }

    [Fact]
    public void Registry_ShouldExposeBuiltInToolDefinitions()
    {
        var registry = new McpServiceRegistry();

        var tools = registry.List();

        Assert.Collection(
            tools,
            item => Assert.Equal((StockMcpToolNames.CompanyOverview, "local_required"), (item.ToolName, item.PolicyClass)),
            item => Assert.Equal((StockMcpToolNames.Product, "local_required"), (item.ToolName, item.PolicyClass)),
            item => Assert.Equal((StockMcpToolNames.Fundamentals, "local_required"), (item.ToolName, item.PolicyClass)),
            item => Assert.Equal((StockMcpToolNames.Shareholder, "local_required"), (item.ToolName, item.PolicyClass)),
            item => Assert.Equal((StockMcpToolNames.MarketContext, "local_required"), (item.ToolName, item.PolicyClass)),
            item => Assert.Equal((StockMcpToolNames.SocialSentiment, "local_required"), (item.ToolName, item.PolicyClass)),
            item => Assert.Equal((StockMcpToolNames.Kline, "local_required"), (item.ToolName, item.PolicyClass)),
            item => Assert.Equal((StockMcpToolNames.Minute, "local_required"), (item.ToolName, item.PolicyClass)),
            item => Assert.Equal((StockMcpToolNames.Strategy, "local_required"), (item.ToolName, item.PolicyClass)),
            item => Assert.Equal((StockMcpToolNames.News, "local_required"), (item.ToolName, item.PolicyClass)),
            item => Assert.Equal((StockMcpToolNames.Search, "external_gated"), (item.ToolName, item.PolicyClass)),
            item => Assert.Equal((StockMcpToolNames.WebSearch, "external_gated"), (item.ToolName, item.PolicyClass)),
            item => Assert.Equal((StockMcpToolNames.WebSearchNews, "external_gated"), (item.ToolName, item.PolicyClass)),
            item => Assert.Equal((StockMcpToolNames.WebReadUrl, "external_gated"), (item.ToolName, item.PolicyClass)),
            item => Assert.Equal((StockMcpToolNames.FinancialReport, "local_required"), (item.ToolName, item.PolicyClass)),
            item => Assert.Equal((StockMcpToolNames.FinancialTrend, "local_required"), (item.ToolName, item.PolicyClass)));
    }

    [Fact]
    public void RolePolicy_ShouldAllowSystemEndpoints_AndRespectPhaseFContracts()
    {
        var policy = CreatePolicy();

        var systemResult = policy.AuthorizeSystemEndpoint(StockMcpToolNames.News);
        var productResult = policy.AuthorizeRole(StockAgentRoleIds.ProductAnalyst, StockMcpToolNames.Product);
        var marketContextResult = policy.AuthorizeRole(StockAgentRoleIds.MarketAnalyst, StockMcpToolNames.MarketContext);
        var marketKlineResult = policy.AuthorizeRole(StockAgentRoleIds.MarketAnalyst, StockMcpToolNames.Kline);
        var marketNewsResult = policy.AuthorizeRole(StockAgentRoleIds.MarketAnalyst, StockMcpToolNames.News);
        var fundamentalsOverviewResult = policy.AuthorizeRole(StockAgentRoleIds.FundamentalsAnalyst, StockMcpToolNames.CompanyOverview);
        var portfolioResult = policy.AuthorizeRole(StockAgentRoleIds.PortfolioManager, StockMcpToolNames.News);

        Assert.True(systemResult.IsAllowed);
        Assert.True(productResult.IsAllowed);
        Assert.True(marketContextResult.IsAllowed);
        Assert.True(marketKlineResult.IsAllowed);
        Assert.False(marketNewsResult.IsAllowed);
        Assert.Equal(McpErrorCodes.RoleNotAuthorized, marketNewsResult.ErrorCode);
        Assert.True(fundamentalsOverviewResult.IsAllowed);
        Assert.False(portfolioResult.IsAllowed);
        Assert.Equal(McpErrorCodes.RoleNotAuthorized, portfolioResult.ErrorCode);
    }

    [Fact]
    public void RolePolicy_ShouldStayInSyncWithRoleContracts()
    {
        var registry = new McpServiceRegistry();
        var contractRegistry = new StockAgentRoleContractRegistry();
        var policy = new RoleToolPolicyService(registry, contractRegistry);

        foreach (var contract in contractRegistry.List())
        {
            var expectedAllowedTools = contract.AllowsDirectQueryTools &&
                !string.Equals(contract.ToolAccessMode, "blocked", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(contract.ToolAccessMode, "disabled", StringComparison.OrdinalIgnoreCase)
                    ? contract.PreferredMcpSequence.ToHashSet(StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var tool in registry.List())
            {
                var result = policy.AuthorizeRole(contract.RoleId, tool.ToolName);

                Assert.Equal(expectedAllowedTools.Contains(tool.ToolName), result.IsAllowed);
            }
        }
    }

    [Fact]
    public async Task Gateway_ShouldDelegateToUnderlyingCopilotService()
    {
        var gateway = new McpToolGateway(new RecordingStockCopilotMcpService(), new McpServiceRegistry(), CreatePolicy(), StubWebSearch, Microsoft.Extensions.Logging.Abstractions.NullLogger<McpToolGateway>.Instance);

        var result = await gateway.GetNewsAsync("sh600000", "stock", "task-gateway");

        Assert.Equal(StockMcpToolNames.News, result.ToolName);
        Assert.Equal("task-gateway", result.TaskId);
    }

    [Fact]
    public async Task Gateway_ShouldDelegateToProductTool()
    {
        var service = new RecordingStockCopilotMcpService();
        var gateway = new McpToolGateway(service, new McpServiceRegistry(), CreatePolicy(), StubWebSearch, Microsoft.Extensions.Logging.Abstractions.NullLogger<McpToolGateway>.Instance);

        var result = await gateway.GetProductAsync("sz002594", "task-product", new StockCopilotMcpWindowOptions(1, 5));

        Assert.Equal(StockMcpToolNames.Product, result.ToolName);
        Assert.Equal("task-product", result.TaskId);
        Assert.Equal(1, service.LastWindow?.EvidenceSkip);
        Assert.Equal(5, service.LastWindow?.EvidenceTake);
    }

    [Fact]
    public async Task Gateway_ShouldDelegateToPhaseDTools()
    {
        var gateway = new McpToolGateway(new RecordingStockCopilotMcpService(), new McpServiceRegistry(), CreatePolicy(), StubWebSearch, Microsoft.Extensions.Logging.Abstractions.NullLogger<McpToolGateway>.Instance);

        var marketContext = await gateway.GetMarketContextAsync("sh600000", "task-market-context");
        var socialSentiment = await gateway.GetSocialSentimentAsync("sh600000", "task-social-sentiment");

        Assert.Equal(StockMcpToolNames.MarketContext, marketContext.ToolName);
        Assert.Equal("task-market-context", marketContext.TaskId);
        Assert.Equal(StockMcpToolNames.SocialSentiment, socialSentiment.ToolName);
        Assert.Equal("task-social-sentiment", socialSentiment.TaskId);
    }

    [Fact]
    public void Register_ShouldAddPhaseAServices()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        new StocksModule().Register(services, configuration);

        Assert.Contains(services, item => item.ServiceType == typeof(IStockCopilotMcpService) && item.ImplementationType == typeof(StockCopilotMcpService));
        Assert.Contains(services, item => item.ServiceType == typeof(IStockCopilotSessionService) && item.ImplementationType == typeof(StockCopilotSessionService));
        Assert.Contains(services, item => item.ServiceType == typeof(IStockChatHistoryService) && item.ImplementationType == typeof(StockChatHistoryService));
        Assert.Contains(services, item => item.ServiceType == typeof(IMcpToolGateway) && item.ImplementationType == typeof(McpToolGateway));
        Assert.Contains(services, item => item.ServiceType == typeof(IMcpServiceRegistry) && item.ImplementationType == typeof(McpServiceRegistry));
        Assert.Contains(services, item => item.ServiceType == typeof(IRoleToolPolicyService) && item.ImplementationType == typeof(RoleToolPolicyService));
        Assert.Contains(services, item => item.ServiceType == typeof(IStockAgentRoleContractRegistry) && item.ImplementationType == typeof(StockAgentRoleContractRegistry));
    }

    [Fact]
    public void MapEndpoints_ShouldExposePhaseDMcpRoutes()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "SimplerJiangAiAgent.Api",
            "Modules",
            "Stocks",
            "StocksModule.cs"));

        var source = File.ReadAllText(sourcePath);

        Assert.Contains("/mcp/market-context", source, StringComparison.Ordinal);
        Assert.Contains("/mcp/social-sentiment", source, StringComparison.Ordinal);
        Assert.Contains("/mcp/product", source, StringComparison.Ordinal);
    }

    private sealed class RecordingStockCopilotMcpService : IStockCopilotMcpService
    {
        public StockCopilotMcpWindowOptions? LastWindow { get; private set; }

        public Task<StockCopilotMcpEnvelopeDto<StockCopilotCompanyOverviewDataDto>> GetCompanyOverviewAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<StockCopilotMcpEnvelopeDto<StockCopilotProductDataDto>> GetProductAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
        {
            LastWindow = window;
            return Task.FromResult(new StockCopilotMcpEnvelopeDto<StockCopilotProductDataDto>(
                "trace-product",
                taskId ?? "task-product",
                StockMcpToolNames.Product,
                12,
                new StockCopilotMcpCacheDto(false, "test", DateTime.UtcNow),
                Array.Empty<string>(),
                Array.Empty<string>(),
                new StockCopilotProductDataDto(symbol, DateTime.UtcNow, null, "新能源汽车及动力电池研发、生产、销售", "汽车整车", "汽车制造业", "广东", 4, "东方财富公司概况", new[]
                {
                    new StockCopilotProductFactDto("经营范围", "新能源汽车及动力电池研发、生产、销售", "东方财富公司概况")
                }),
                Array.Empty<StockCopilotMcpEvidenceDto>(),
                Array.Empty<StockCopilotMcpFeatureDto>(),
                new StockCopilotMcpMetaDto("v1", "local_required", StockMcpToolNames.Product, symbol, null, null, null)));
        }

        public Task<StockCopilotMcpEnvelopeDto<StockCopilotFundamentalsDataDto>> GetFundamentalsAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<StockCopilotMcpEnvelopeDto<StockCopilotShareholderDataDto>> GetShareholderAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<StockCopilotMcpEnvelopeDto<StockCopilotMarketContextDataDto>> GetMarketContextAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new StockCopilotMcpEnvelopeDto<StockCopilotMarketContextDataDto>(
                "trace-market-context",
                taskId ?? "task-market-context",
                StockMcpToolNames.MarketContext,
                10,
                new StockCopilotMcpCacheDto(false, "test", DateTime.UtcNow),
                Array.Empty<string>(),
                Array.Empty<string>(),
                new StockCopilotMarketContextDataDto(symbol, true, 80m, "银行", "银行", "BK001", 90m),
                Array.Empty<StockCopilotMcpEvidenceDto>(),
                Array.Empty<StockCopilotMcpFeatureDto>(),
                new StockCopilotMcpMetaDto("v1", "local_required", StockMcpToolNames.MarketContext, symbol, null, null, null)));
        }

            public Task<StockCopilotMcpEnvelopeDto<StockCopilotSocialSentimentDataDto>> GetSocialSentimentAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new StockCopilotMcpEnvelopeDto<StockCopilotSocialSentimentDataDto>(
                "trace-social-sentiment",
                taskId ?? "task-social-sentiment",
                StockMcpToolNames.SocialSentiment,
                18,
                new StockCopilotMcpCacheDto(false, "test", DateTime.UtcNow),
                new[] { "degraded" },
                new[] { "no_live_social_source" },
                new StockCopilotSocialSentimentDataDto(symbol, "degraded", false, null, "market_proxy_only", 1, DateTime.UtcNow, new StockCopilotSentimentCountDto(0, 0, 0, 0, null), new StockCopilotSentimentCountDto(0, 0, 0, 0, null), new StockCopilotSentimentCountDto(0, 0, 0, 0, null), new StockCopilotSocialSentimentMarketProxyDto("主升", 81m, DateTime.UtcNow)),
                Array.Empty<StockCopilotMcpEvidenceDto>(),
                Array.Empty<StockCopilotMcpFeatureDto>(),
                new StockCopilotMcpMetaDto("v1", "local_required", StockMcpToolNames.SocialSentiment, symbol, null, null, null)));
        }

        public Task<StockCopilotMcpEnvelopeDto<StockCopilotKlineDataDto>> GetKlineAsync(string symbol, string interval, int count, string? source, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<StockCopilotMcpEnvelopeDto<StockCopilotMinuteDataDto>> GetMinuteAsync(string symbol, string? source, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<StockCopilotMcpEnvelopeDto<StockCopilotStrategyDataDto>> GetStrategyAsync(string symbol, string interval, int count, string? source, IReadOnlyList<string>? strategies, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<StockCopilotMcpEnvelopeDto<StockCopilotNewsDataDto>> GetNewsAsync(string symbol, string level, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new StockCopilotMcpEnvelopeDto<StockCopilotNewsDataDto>(
                "trace-news",
                taskId ?? "task-news",
                StockMcpToolNames.News,
                12,
                new StockCopilotMcpCacheDto(false, "test", DateTime.UtcNow),
                Array.Empty<string>(),
                Array.Empty<string>(),
                new StockCopilotNewsDataDto(symbol, level, 1, DateTime.UtcNow),
                Array.Empty<StockCopilotMcpEvidenceDto>(),
                Array.Empty<StockCopilotMcpFeatureDto>(),
                new StockCopilotMcpMetaDto("v1", "local_required", StockMcpToolNames.News, symbol, null, level, null)));
        }

        public Task<StockCopilotMcpEnvelopeDto<StockCopilotSearchDataDto>> SearchAsync(string query, bool trustedOnly, string? taskId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<StockCopilotMcpEnvelopeDto<StockCopilotFinancialReportDataDto>> GetFinancialReportAsync(string symbol, int periods, string? taskId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<StockCopilotMcpEnvelopeDto<StockCopilotFinancialTrendDataDto>> GetFinancialTrendAsync(string symbol, int periods, string? taskId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubWebSearchService : IWebSearchService
    {
        public Task<WebSearchResult> SearchAsync(string query, SearchType type, WebSearchOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new WebSearchResult(Array.Empty<WebSearchItem>(), "stub", false));

        public Task<WebReadResult> ReadUrlAsync(string url, int maxChars = 8000, CancellationToken ct = default)
            => Task.FromResult(new WebReadResult("", url, 0, false));

        public WebSearchHealthStatus GetHealthStatus()
            => new("stub", false, false, false);
    }
}