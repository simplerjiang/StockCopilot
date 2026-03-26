using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface IMcpToolGateway
{
    Task<StockCopilotMcpEnvelopeDto<StockCopilotCompanyOverviewDataDto>> GetCompanyOverviewAsync(string symbol, string? taskId, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotProductDataDto>> GetProductAsync(string symbol, string? taskId, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotFundamentalsDataDto>> GetFundamentalsAsync(string symbol, string? taskId, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotShareholderDataDto>> GetShareholderAsync(string symbol, string? taskId, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotMarketContextDataDto>> GetMarketContextAsync(string symbol, string? taskId, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotSocialSentimentDataDto>> GetSocialSentimentAsync(string symbol, string? taskId, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotKlineDataDto>> GetKlineAsync(string symbol, string interval, int count, string? source, string? taskId, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotMinuteDataDto>> GetMinuteAsync(string symbol, string? source, string? taskId, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotStrategyDataDto>> GetStrategyAsync(string symbol, string interval, int count, string? source, IReadOnlyList<string>? strategies, string? taskId, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotNewsDataDto>> GetNewsAsync(string symbol, string level, string? taskId, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotSearchDataDto>> SearchAsync(string query, bool trustedOnly, string? taskId, CancellationToken cancellationToken = default);
}

public sealed class McpToolGateway : IMcpToolGateway
{
    private readonly IStockCopilotMcpService _stockCopilotMcpService;
    private readonly IMcpServiceRegistry _registry;
    private readonly IRoleToolPolicyService _roleToolPolicyService;

    public McpToolGateway(
        IStockCopilotMcpService stockCopilotMcpService,
        IMcpServiceRegistry registry,
        IRoleToolPolicyService roleToolPolicyService)
    {
        _stockCopilotMcpService = stockCopilotMcpService;
        _registry = registry;
        _roleToolPolicyService = roleToolPolicyService;
    }

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotCompanyOverviewDataDto>> GetCompanyOverviewAsync(string symbol, string? taskId, CancellationToken cancellationToken = default)
    {
        EnsureSystemToolAccess(StockMcpToolNames.CompanyOverview);
        return _stockCopilotMcpService.GetCompanyOverviewAsync(symbol, taskId, cancellationToken);
    }

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotProductDataDto>> GetProductAsync(string symbol, string? taskId, CancellationToken cancellationToken = default)
    {
        EnsureSystemToolAccess(StockMcpToolNames.Product);
        return _stockCopilotMcpService.GetProductAsync(symbol, taskId, cancellationToken);
    }

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotFundamentalsDataDto>> GetFundamentalsAsync(string symbol, string? taskId, CancellationToken cancellationToken = default)
    {
        EnsureSystemToolAccess(StockMcpToolNames.Fundamentals);
        return _stockCopilotMcpService.GetFundamentalsAsync(symbol, taskId, cancellationToken);
    }

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotShareholderDataDto>> GetShareholderAsync(string symbol, string? taskId, CancellationToken cancellationToken = default)
    {
        EnsureSystemToolAccess(StockMcpToolNames.Shareholder);
        return _stockCopilotMcpService.GetShareholderAsync(symbol, taskId, cancellationToken);
    }

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotMarketContextDataDto>> GetMarketContextAsync(string symbol, string? taskId, CancellationToken cancellationToken = default)
    {
        EnsureSystemToolAccess(StockMcpToolNames.MarketContext);
        return _stockCopilotMcpService.GetMarketContextAsync(symbol, taskId, cancellationToken);
    }

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotSocialSentimentDataDto>> GetSocialSentimentAsync(string symbol, string? taskId, CancellationToken cancellationToken = default)
    {
        EnsureSystemToolAccess(StockMcpToolNames.SocialSentiment);
        return _stockCopilotMcpService.GetSocialSentimentAsync(symbol, taskId, cancellationToken);
    }

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotKlineDataDto>> GetKlineAsync(string symbol, string interval, int count, string? source, string? taskId, CancellationToken cancellationToken = default)
    {
        EnsureSystemToolAccess(StockMcpToolNames.Kline);
        return _stockCopilotMcpService.GetKlineAsync(symbol, interval, count, source, taskId, cancellationToken);
    }

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotMinuteDataDto>> GetMinuteAsync(string symbol, string? source, string? taskId, CancellationToken cancellationToken = default)
    {
        EnsureSystemToolAccess(StockMcpToolNames.Minute);
        return _stockCopilotMcpService.GetMinuteAsync(symbol, source, taskId, cancellationToken);
    }

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotStrategyDataDto>> GetStrategyAsync(string symbol, string interval, int count, string? source, IReadOnlyList<string>? strategies, string? taskId, CancellationToken cancellationToken = default)
    {
        EnsureSystemToolAccess(StockMcpToolNames.Strategy);
        return _stockCopilotMcpService.GetStrategyAsync(symbol, interval, count, source, strategies, taskId, cancellationToken);
    }

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotNewsDataDto>> GetNewsAsync(string symbol, string level, string? taskId, CancellationToken cancellationToken = default)
    {
        EnsureSystemToolAccess(StockMcpToolNames.News);
        return _stockCopilotMcpService.GetNewsAsync(symbol, level, taskId, cancellationToken);
    }

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotSearchDataDto>> SearchAsync(string query, bool trustedOnly, string? taskId, CancellationToken cancellationToken = default)
    {
        EnsureSystemToolAccess(StockMcpToolNames.Search);
        return _stockCopilotMcpService.SearchAsync(query, trustedOnly, taskId, cancellationToken);
    }

    private void EnsureSystemToolAccess(string toolName)
    {
        _registry.GetRequired(toolName);
        var result = _roleToolPolicyService.AuthorizeSystemEndpoint(toolName);
        if (!result.IsAllowed)
        {
            throw new InvalidOperationException(result.ErrorCode ?? McpErrorCodes.SystemEndpointNotAuthorized);
        }
    }
}