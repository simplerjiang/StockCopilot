using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public static class StockAgentRoleIds
{
    public const string CompanyOverviewAnalyst = "company_overview_analyst";
    public const string MarketAnalyst = "market_analyst";
    public const string SocialSentimentAnalyst = "social_sentiment_analyst";
    public const string NewsAnalyst = "news_analyst";
    public const string FundamentalsAnalyst = "fundamentals_analyst";
    public const string ShareholderAnalyst = "shareholder_analyst";
    public const string ProductAnalyst = "product_analyst";
    public const string BullResearcher = "bull_researcher";
    public const string BearResearcher = "bear_researcher";
    public const string ResearchManager = "research_manager";
    public const string Trader = "trader";
    public const string AggressiveRiskAnalyst = "aggressive_risk_analyst";
    public const string NeutralRiskAnalyst = "neutral_risk_analyst";
    public const string ConservativeRiskAnalyst = "conservative_risk_analyst";
    public const string PortfolioManager = "portfolio_manager";

    public static readonly IReadOnlyList<string> All =
    [
        CompanyOverviewAnalyst,
        MarketAnalyst,
        SocialSentimentAnalyst,
        NewsAnalyst,
        FundamentalsAnalyst,
        ShareholderAnalyst,
        ProductAnalyst,
        BullResearcher,
        BearResearcher,
        ResearchManager,
        Trader,
        AggressiveRiskAnalyst,
        NeutralRiskAnalyst,
        ConservativeRiskAnalyst,
        PortfolioManager
    ];
}

public interface IStockAgentRoleContractRegistry
{
    IReadOnlyList<StockCopilotRoleContractDto> List();
    StockCopilotRoleContractDto GetRequired(string roleId);
    StockCopilotRoleContractChecklistDto BuildChecklist();
}

public sealed class StockAgentRoleContractRegistry : IStockAgentRoleContractRegistry
{
    public const string ChecklistId = "stock-prompt-mcp-contract-checklist";
    public const string ChecklistVersion = "phase-f-20260326-r1";
    public const string SourceTaskId = "GOAL-AGENT-NEW-001-P0-Pre-Phase-F";

    private static readonly IReadOnlyList<StockCopilotRoleContractDto> Contracts =
    [
        new(
            StockAgentRoleIds.CompanyOverviewAnalyst,
            "Company Overview Analyst",
            "analyst",
            "local_required",
            [StockMcpToolNames.CompanyOverview, StockMcpToolNames.FinancialReportRag, StockMcpToolNames.MarketContext, StockMcpToolNames.News, StockMcpToolNames.Search],
            "必须先完成 CompanyOverviewMcp 的本地公司识别；只有本地主营描述或别名字段仍不足时，才允许在 governor 批准后使用 StockSearchMcp 做受控补充。",
            "若 symbol 解析失败、CompanyOverviewMcp 不可用或关键公司字段为空，则停止 analyst fan-out。",
            2,
            true,
            null),
        new(
            StockAgentRoleIds.MarketAnalyst,
            "Market Analyst",
            "analyst",
            "local_required",
            [StockMcpToolNames.MarketContext, StockMcpToolNames.Kline, StockMcpToolNames.Minute, StockMcpToolNames.Strategy],
            "仅允许复用已存在的本地 market context 或 company overview 上下文；不允许自动 external fallback，也不允许把网页搜索当作市场结构替代。",
            "任一 local_required 市场工具不可用时立即停止，不得退化为 Web Search 分析。",
            3,
            true,
            null),
        new(
            StockAgentRoleIds.SocialSentimentAnalyst,
            "Social Sentiment Analyst",
            "analyst",
            "local_preferred",
            [StockMcpToolNames.SocialSentiment, StockMcpToolNames.News, StockMcpToolNames.MarketContext, StockMcpToolNames.Search],
            "优先使用 SocialSentimentMcp 的本地 proxy 与本地新闻情绪；仅当本地 proxy 证据不足且 governor 明确批准时，才允许 StockSearchMcp 作为 external_gated 补充。",
            "若本地 proxy 返回 blocked/no_data 且未获外部批准，则停止并保持 degraded 或 blocked。",
            2,
            true,
            null),
        new(
            StockAgentRoleIds.NewsAnalyst,
            "News Analyst",
            "analyst",
            "local_required",
            [StockMcpToolNames.News, StockMcpToolNames.MarketContext, StockMcpToolNames.Search],
            "必须先执行 StockNewsMcp 的本地事实收集；只有当本地证据数低于 2、时间窗不新鲜或存在明确缺口时，才允许受控使用 StockSearchMcp。",
            "StockNewsMcp 不可用或本地新闻链路失败时立即停止；未获外部批准不得越过 local-first。",
            2,
            true,
            null),
        new(
            StockAgentRoleIds.FundamentalsAnalyst,
            "Fundamentals Analyst",
            "analyst",
            "local_required",
            [StockMcpToolNames.Fundamentals, StockMcpToolNames.FinancialReport, StockMcpToolNames.FinancialTrend, StockMcpToolNames.FinancialReportRag, StockMcpToolNames.CompanyOverview, StockMcpToolNames.MarketContext],
            "只允许复用本地 company overview 与 market context 作为上下文，本角色不触发 external search。",
            "StockFundamentalsMcp 无数据时立即停止，不得用新闻或推断替代基本面。",
            2,
            true,
            null),
        new(
            StockAgentRoleIds.ShareholderAnalyst,
            "Shareholder Analyst",
            "analyst",
            "local_required",
            [StockMcpToolNames.Shareholder, StockMcpToolNames.CompanyOverview, StockMcpToolNames.News],
            "仅允许复用本地 company overview 与 local news 作为上下文，不允许 external search 替代股东结构数据。",
            "StockShareholderMcp 缺失时立即停止，不得把 fundamentals 或 product 文本伪装成股东分析。",
            2,
            true,
            null),
        new(
            StockAgentRoleIds.ProductAnalyst,
            "Product Analyst",
            "analyst",
            "local_required",
            [StockMcpToolNames.Product, StockMcpToolNames.CompanyOverview, StockMcpToolNames.MarketContext, StockMcpToolNames.News],
            "必须先执行 StockProductMcp；CompanyOverviewMcp、MarketContextMcp 与 StockNewsMcp 只能做上下文补充，不能替代经营范围/主营业务等产品事实。",
            "若 StockProductMcp 未返回经营范围或主营业务等最小产品事实，则立即停止，不得伪造 Product Analyst 结论。",
            2,
            true,
            null),
        new(
            StockAgentRoleIds.BullResearcher,
            "Bull Researcher",
            "researcher",
            "disabled",
            Array.Empty<string>(),
            "不允许直接调用查询工具；只能消费 analyst outputs、debate history 与 current report snapshot。",
            "若缺少上游 analyst artifacts，则停止并请求补齐，不得私自取数。",
            3,
            false,
            "researcher 角色无直接查询工具权限。"),
        new(
            StockAgentRoleIds.BearResearcher,
            "Bear Researcher",
            "researcher",
            "disabled",
            Array.Empty<string>(),
            "不允许直接调用查询工具；只能消费 analyst outputs、debate history 与 current report snapshot。",
            "若缺少上游 analyst artifacts，则停止并请求补齐，不得私自取数。",
            3,
            false,
            "researcher 角色无直接查询工具权限。"),
        new(
            StockAgentRoleIds.ResearchManager,
            "Research Manager",
            "manager",
            "disabled",
            Array.Empty<string>(),
            "只读 analyst outputs 与 bull/bear debate，不得重新查询工具。",
            "缺少 bull/bear 冲突点或 analyst artifacts 时停止，不得用摘要替代裁决。",
            4,
            false,
            "manager 角色无直接查询工具权限。"),
        new(
            StockAgentRoleIds.Trader,
            "Trader",
            "trader",
            "disabled",
            Array.Empty<string>(),
            "只读 research decision、market structure artifact 与约束条件，不得直接取数。",
            "research decision 未完成时停止，不得越过 research debate。",
            3,
            false,
            "trader 角色无直接查询工具权限。"),
        new(
            StockAgentRoleIds.AggressiveRiskAnalyst,
            "Aggressive Risk Analyst",
            "risk",
            "disabled",
            Array.Empty<string>(),
            "只读 trader proposal、research summary 与 risk inputs，不得直接取数。",
            "trader proposal 缺失时停止，不得把通用风险提示当作评审。",
            3,
            false,
            "risk 角色无直接查询工具权限。"),
        new(
            StockAgentRoleIds.NeutralRiskAnalyst,
            "Neutral Risk Analyst",
            "risk",
            "disabled",
            Array.Empty<string>(),
            "只读 trader proposal、research summary 与 risk inputs，不得直接取数。",
            "trader proposal 缺失时停止，不得把通用风险提示当作评审。",
            3,
            false,
            "risk 角色无直接查询工具权限。"),
        new(
            StockAgentRoleIds.ConservativeRiskAnalyst,
            "Conservative Risk Analyst",
            "risk",
            "disabled",
            Array.Empty<string>(),
            "只读 trader proposal、research summary 与 risk inputs，不得直接取数。",
            "trader proposal 缺失时停止，不得把通用风险提示当作评审。",
            3,
            false,
            "risk 角色无直接查询工具权限。"),
        new(
            StockAgentRoleIds.PortfolioManager,
            "Portfolio Manager",
            "portfolio",
            "disabled",
            Array.Empty<string>(),
            "只读 trader proposal、risk debate 与 current report，不得重新查询工具。",
            "缺少 trader proposal 或 risk debate 时停止，不得提前生成 final decision。",
            4,
            false,
            "portfolio 角色无直接查询工具权限。")
    ];

    private readonly Dictionary<string, StockCopilotRoleContractDto> _contracts = Contracts
        .ToDictionary(item => item.RoleId, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<StockCopilotRoleContractDto> List()
    {
        return Contracts;
    }

    public StockCopilotRoleContractDto GetRequired(string roleId)
    {
        if (string.IsNullOrWhiteSpace(roleId))
        {
            throw new ArgumentException("roleId 不能为空", nameof(roleId));
        }

        if (_contracts.TryGetValue(roleId.Trim(), out var contract))
        {
            return contract;
        }

        throw new InvalidOperationException($"role.contract_not_registered:{roleId}");
    }

    public StockCopilotRoleContractChecklistDto BuildChecklist()
    {
        return new StockCopilotRoleContractChecklistDto(
            ChecklistId,
            ChecklistVersion,
            SourceTaskId,
            DateTime.UtcNow,
            Contracts);
    }
}