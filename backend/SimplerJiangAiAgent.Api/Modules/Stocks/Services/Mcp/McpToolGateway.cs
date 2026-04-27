using System.Diagnostics;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services.Recommend.WebSearch;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface IMcpToolGateway
{
    Task<StockCopilotMcpEnvelopeDto<StockCopilotCompanyOverviewDataDto>> GetCompanyOverviewAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotProductDataDto>> GetProductAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotFundamentalsDataDto>> GetFundamentalsAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotShareholderDataDto>> GetShareholderAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotMarketContextDataDto>> GetMarketContextAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotSocialSentimentDataDto>> GetSocialSentimentAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotKlineDataDto>> GetKlineAsync(string symbol, string interval, int count, string? source, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotMinuteDataDto>> GetMinuteAsync(string symbol, string? source, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotStrategyDataDto>> GetStrategyAsync(string symbol, string interval, int count, string? source, IReadOnlyList<string>? strategies, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotNewsDataDto>> GetNewsAsync(string symbol, string level, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotSearchDataDto>> SearchAsync(string query, bool trustedOnly, string? taskId, CancellationToken cancellationToken = default);
    Task<WebSearchResult> WebSearchAsync(string query, WebSearchOptions? options = null, CancellationToken cancellationToken = default);
    Task<WebSearchResult> WebSearchNewsAsync(string query, WebSearchOptions? options = null, CancellationToken cancellationToken = default);
    Task<WebReadResult> WebReadUrlAsync(string url, int maxChars = 8000, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotFinancialReportDataDto>> GetFinancialReportAsync(string symbol, int periods, string? taskId, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotFinancialTrendDataDto>> GetFinancialTrendAsync(string symbol, int periods, string? taskId, CancellationToken cancellationToken = default);
    Task<List<RagCitationDto>> SearchFinancialReportRagAsync(string symbol, string query, int topK = 5, CancellationToken cancellationToken = default);
    Task<List<RagCitationDto>> SearchAnnouncementRagAsync(string symbol, string query, int topK = 5, CancellationToken cancellationToken = default);
}

public sealed class McpToolGateway : IMcpToolGateway
{
    private readonly IStockCopilotMcpService _stockCopilotMcpService;
    private readonly IMcpServiceRegistry _registry;
    private readonly IRoleToolPolicyService _roleToolPolicyService;
    private readonly IWebSearchService _webSearchService;
    private readonly RagContextEnricher _ragContextEnricher;
    private readonly ILogger<McpToolGateway> _logger;

    public McpToolGateway(
        IStockCopilotMcpService stockCopilotMcpService,
        IMcpServiceRegistry registry,
        IRoleToolPolicyService roleToolPolicyService,
        IWebSearchService webSearchService,
        RagContextEnricher ragContextEnricher,
        ILogger<McpToolGateway> logger)
    {
        _stockCopilotMcpService = stockCopilotMcpService;
        _registry = registry;
        _roleToolPolicyService = roleToolPolicyService;
        _webSearchService = webSearchService;
        _ragContextEnricher = ragContextEnricher;
        _logger = logger;
    }

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotCompanyOverviewDataDto>> GetCompanyOverviewAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
    {
        EnsureSystemToolAccess(StockMcpToolNames.CompanyOverview);
        return ExecuteWithLoggingAsync(StockMcpToolNames.CompanyOverview, symbol,
            () => _stockCopilotMcpService.GetCompanyOverviewAsync(symbol, taskId, window, cancellationToken));
    }

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotProductDataDto>> GetProductAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
    {
        EnsureSystemToolAccess(StockMcpToolNames.Product);
        return ExecuteWithLoggingAsync(StockMcpToolNames.Product, symbol,
            () => _stockCopilotMcpService.GetProductAsync(symbol, taskId, window, cancellationToken));
    }

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotFundamentalsDataDto>> GetFundamentalsAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
    {
        EnsureSystemToolAccess(StockMcpToolNames.Fundamentals);
        return ExecuteWithLoggingAsync(StockMcpToolNames.Fundamentals, symbol,
            () => _stockCopilotMcpService.GetFundamentalsAsync(symbol, taskId, window, cancellationToken));
    }

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotShareholderDataDto>> GetShareholderAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
    {
        EnsureSystemToolAccess(StockMcpToolNames.Shareholder);
        return ExecuteWithLoggingAsync(StockMcpToolNames.Shareholder, symbol,
            () => _stockCopilotMcpService.GetShareholderAsync(symbol, taskId, window, cancellationToken));
    }

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotMarketContextDataDto>> GetMarketContextAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
    {
        EnsureSystemToolAccess(StockMcpToolNames.MarketContext);
        return ExecuteWithLoggingAsync(StockMcpToolNames.MarketContext, symbol,
            () => _stockCopilotMcpService.GetMarketContextAsync(symbol, taskId, window, cancellationToken));
    }

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotSocialSentimentDataDto>> GetSocialSentimentAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
    {
        EnsureSystemToolAccess(StockMcpToolNames.SocialSentiment);
        return ExecuteWithLoggingAsync(StockMcpToolNames.SocialSentiment, symbol,
            () => _stockCopilotMcpService.GetSocialSentimentAsync(symbol, taskId, window, cancellationToken));
    }

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotKlineDataDto>> GetKlineAsync(string symbol, string interval, int count, string? source, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
    {
        EnsureSystemToolAccess(StockMcpToolNames.Kline);
        return ExecuteWithLoggingAsync(StockMcpToolNames.Kline, symbol,
            () => _stockCopilotMcpService.GetKlineAsync(symbol, interval, count, source, taskId, window, cancellationToken));
    }

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotMinuteDataDto>> GetMinuteAsync(string symbol, string? source, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
    {
        EnsureSystemToolAccess(StockMcpToolNames.Minute);
        return ExecuteWithLoggingAsync(StockMcpToolNames.Minute, symbol,
            () => _stockCopilotMcpService.GetMinuteAsync(symbol, source, taskId, window, cancellationToken));
    }

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotStrategyDataDto>> GetStrategyAsync(string symbol, string interval, int count, string? source, IReadOnlyList<string>? strategies, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
    {
        EnsureSystemToolAccess(StockMcpToolNames.Strategy);
        return ExecuteWithLoggingAsync(StockMcpToolNames.Strategy, symbol,
            () => _stockCopilotMcpService.GetStrategyAsync(symbol, interval, count, source, strategies, taskId, window, cancellationToken));
    }

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotNewsDataDto>> GetNewsAsync(string symbol, string level, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
    {
        EnsureSystemToolAccess(StockMcpToolNames.News);
        return ExecuteWithLoggingAsync(StockMcpToolNames.News, symbol,
            () => _stockCopilotMcpService.GetNewsAsync(symbol, level, taskId, window, cancellationToken));
    }

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotSearchDataDto>> SearchAsync(string query, bool trustedOnly, string? taskId, CancellationToken cancellationToken = default)
    {
        EnsureSystemToolAccess(StockMcpToolNames.Search);
        return ExecuteWithLoggingAsync(StockMcpToolNames.Search, query,
            () => _stockCopilotMcpService.SearchAsync(query, trustedOnly, taskId, cancellationToken));
    }

    public Task<WebSearchResult> WebSearchAsync(string query, WebSearchOptions? options = null, CancellationToken cancellationToken = default)
    {
        EnsureSystemToolAccess(StockMcpToolNames.WebSearch);
        return ExecuteWithLoggingAsync(StockMcpToolNames.WebSearch, query,
            () => _webSearchService.SearchAsync(query, SearchType.Web, options, cancellationToken));
    }

    public Task<WebSearchResult> WebSearchNewsAsync(string query, WebSearchOptions? options = null, CancellationToken cancellationToken = default)
    {
        EnsureSystemToolAccess(StockMcpToolNames.WebSearchNews);
        return ExecuteWithLoggingAsync(StockMcpToolNames.WebSearchNews, query,
            () => _webSearchService.SearchAsync(query, SearchType.News, options, cancellationToken));
    }

    public Task<WebReadResult> WebReadUrlAsync(string url, int maxChars = 8000, CancellationToken cancellationToken = default)
    {
        EnsureSystemToolAccess(StockMcpToolNames.WebReadUrl);
        return ExecuteWithLoggingAsync(StockMcpToolNames.WebReadUrl, url,
            () => _webSearchService.ReadUrlAsync(url, maxChars, cancellationToken));
    }

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotFinancialReportDataDto>> GetFinancialReportAsync(string symbol, int periods, string? taskId, CancellationToken cancellationToken = default)
    {
        EnsureSystemToolAccess(StockMcpToolNames.FinancialReport);
        return ExecuteWithLoggingAsync(StockMcpToolNames.FinancialReport, symbol,
            () => _stockCopilotMcpService.GetFinancialReportAsync(symbol, periods, taskId, cancellationToken));
    }

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotFinancialTrendDataDto>> GetFinancialTrendAsync(string symbol, int periods, string? taskId, CancellationToken cancellationToken = default)
    {
        EnsureSystemToolAccess(StockMcpToolNames.FinancialTrend);
        return ExecuteWithLoggingAsync(StockMcpToolNames.FinancialTrend, symbol,
            () => _stockCopilotMcpService.GetFinancialTrendAsync(symbol, periods, taskId, cancellationToken));
    }

    private async Task<T> ExecuteWithLoggingAsync<T>(string toolName, string key, Func<Task<T>> action)
    {
        _logger.LogDebug("MCP tool {Tool} starting for {Key}", toolName, key);
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await action();
            sw.Stop();
            _logger.LogInformation("MCP tool {Tool} completed for {Key} in {ElapsedMs}ms", toolName, key, sw.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "MCP tool {Tool} failed for {Key} after {ElapsedMs}ms", toolName, key, sw.ElapsedMilliseconds);
            throw;
        }
    }

    public async Task<List<RagCitationDto>> SearchFinancialReportRagAsync(string symbol, string query, int topK = 5, CancellationToken cancellationToken = default)
    {
        EnsureSystemToolAccess(StockMcpToolNames.FinancialReportRag);
        return await ExecuteWithLoggingAsync(StockMcpToolNames.FinancialReportRag, symbol,
            () => _ragContextEnricher.EnrichAsync(query, symbol, topK, cancellationToken, sourceType: "financial_report"));
    }

    public async Task<List<RagCitationDto>> SearchAnnouncementRagAsync(string symbol, string query, int topK = 5, CancellationToken cancellationToken = default)
    {
        EnsureSystemToolAccess(StockMcpToolNames.AnnouncementRag);
        return await ExecuteWithLoggingAsync(StockMcpToolNames.AnnouncementRag, symbol,
            () => _ragContextEnricher.EnrichAsync(query, symbol, topK, cancellationToken, sourceType: "announcement"));
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