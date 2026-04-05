namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public sealed record McpToolRegistration(string ToolName, string PolicyClass);

public interface IMcpServiceRegistry
{
    IReadOnlyList<McpToolRegistration> List();
    bool TryGet(string toolName, out McpToolRegistration? registration);
    McpToolRegistration GetRequired(string toolName);
}

public sealed class McpServiceRegistry : IMcpServiceRegistry
{
    private static readonly IReadOnlyList<McpToolRegistration> Registrations =
    [
        new(StockMcpToolNames.CompanyOverview, "local_required"),
        new(StockMcpToolNames.Product, "local_required"),
        new(StockMcpToolNames.Fundamentals, "local_required"),
        new(StockMcpToolNames.Shareholder, "local_required"),
        new(StockMcpToolNames.MarketContext, "local_required"),
        new(StockMcpToolNames.SocialSentiment, "local_required"),
        new(StockMcpToolNames.Kline, "local_required"),
        new(StockMcpToolNames.Minute, "local_required"),
        new(StockMcpToolNames.Strategy, "local_required"),
        new(StockMcpToolNames.News, "local_required"),
        new(StockMcpToolNames.Search, "external_gated"),
        new(StockMcpToolNames.WebSearch, "external_gated"),
        new(StockMcpToolNames.WebSearchNews, "external_gated"),
        new(StockMcpToolNames.WebReadUrl, "external_gated"),
        new(StockMcpToolNames.FinancialReport, "local_required"),
        new(StockMcpToolNames.FinancialTrend, "local_required")
    ];

    private readonly Dictionary<string, McpToolRegistration> _registrations = Registrations
        .ToDictionary(item => item.ToolName, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<McpToolRegistration> List()
    {
        return Registrations;
    }

    public bool TryGet(string toolName, out McpToolRegistration? registration)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            registration = null;
            return false;
        }

        return _registrations.TryGetValue(toolName.Trim(), out registration);
    }

    public McpToolRegistration GetRequired(string toolName)
    {
        if (TryGet(toolName, out var registration) && registration is not null)
        {
            return registration;
        }

        throw new InvalidOperationException($"{McpErrorCodes.ToolNotRegistered}:{toolName}");
    }
}