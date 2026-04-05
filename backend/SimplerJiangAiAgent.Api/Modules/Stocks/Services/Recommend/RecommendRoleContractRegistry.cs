using SimplerJiangAiAgent.Api.Data.Entities;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services.Recommend;

public sealed record RecommendRoleContract(
    string RoleId,
    string DisplayName,
    string SystemPrompt,
    string OutputSchemaDescription,
    IReadOnlyList<string> ToolHints,
    int MaxToolCalls = 5);

public interface IRecommendRoleContractRegistry
{
    RecommendRoleContract GetContract(string roleId);
    IReadOnlyList<string> GetStageRoleIds(RecommendStageType stage);
}

public sealed class RecommendRoleContractRegistry : IRecommendRoleContractRegistry
{
    private readonly Dictionary<string, RecommendRoleContract> _contracts;
    private readonly Dictionary<RecommendStageType, IReadOnlyList<string>> _stageRoles;

    public RecommendRoleContractRegistry()
    {
        _contracts = BuildContracts().ToDictionary(c => c.RoleId);
        _stageRoles = new Dictionary<RecommendStageType, IReadOnlyList<string>>
        {
            [RecommendStageType.MarketScan] = [RecommendAgentRoleIds.MacroAnalyst, RecommendAgentRoleIds.SectorHunter, RecommendAgentRoleIds.SmartMoneyAnalyst],
            [RecommendStageType.SectorDebate] = [RecommendAgentRoleIds.SectorBull, RecommendAgentRoleIds.SectorBear, RecommendAgentRoleIds.SectorJudge],
            [RecommendStageType.StockPicking] = [RecommendAgentRoleIds.LeaderPicker, RecommendAgentRoleIds.GrowthPicker, RecommendAgentRoleIds.ChartValidator],
            [RecommendStageType.StockDebate] = [RecommendAgentRoleIds.StockBull, RecommendAgentRoleIds.StockBear, RecommendAgentRoleIds.RiskReviewer],
            [RecommendStageType.FinalDecision] = [RecommendAgentRoleIds.Director],
        };
    }

    public RecommendRoleContract GetContract(string roleId)
    {
        if (_contracts.TryGetValue(roleId, out var contract))
            return contract;
        throw new KeyNotFoundException($"未注册的推荐角色: {roleId}");
    }

    public IReadOnlyList<string> GetStageRoleIds(RecommendStageType stage)
    {
        return _stageRoles.TryGetValue(stage, out var roles) ? roles : [];
    }

    private static IEnumerable<RecommendRoleContract> BuildContracts()
    {
        // Stage 1: 市场扫描
        yield return new RecommendRoleContract(
            RecommendAgentRoleIds.MacroAnalyst, "宏观环境分析师",
            RecommendPromptTemplates.MacroAnalyst,
            """{ "sentiment": "bullish|neutral|cautious", "keyDrivers": [{"event":"","impact":"","source":"","publishedAt":""}], "globalContext": "", "policySignals": [""] }""",
            ["web_search", "market_context", "stock_news"],
            MaxToolCalls: 5);

        yield return new RecommendRoleContract(
            RecommendAgentRoleIds.SectorHunter, "热点板块猎手",
            RecommendPromptTemplates.SectorHunter,
            """{ "candidateSectors": [{"name":"","code":"","changePercent":0,"netInflow":0,"catalysts":[""],"reason":""}] }""",
            ["web_search_news", "market_context", "web_search"],
            MaxToolCalls: 5);

        yield return new RecommendRoleContract(
            RecommendAgentRoleIds.SmartMoneyAnalyst, "资金流向分析师",
            RecommendPromptTemplates.SmartMoneyAnalyst,
            """{ "mainCapitalFlow": {}, "northboundFlow": {}, "resonanceSectors": [{"name":"","reason":""}], "anomalies": [{"description":"","severity":""}] }""",
            ["market_context", "web_search"],
            MaxToolCalls: 5);

        // Stage 2: 板块辩论
        yield return new RecommendRoleContract(
            RecommendAgentRoleIds.SectorBull, "板块多头",
            RecommendPromptTemplates.SectorBull,
            """{ "sectorClaims": [{"sectorName":"","bullPoints":[{"claim":"","evidence":"","source":""}]}] }""",
            ["web_search"],
            MaxToolCalls: 3);

        yield return new RecommendRoleContract(
            RecommendAgentRoleIds.SectorBear, "板块空头",
            RecommendPromptTemplates.SectorBear,
            """{ "sectorRisks": [{"sectorName":"","bearPoints":[{"rebuttal":"","evidence":"","source":""}],"riskRating":"high|medium|low"}] }""",
            ["web_search"],
            MaxToolCalls: 3);

        yield return new RecommendRoleContract(
            RecommendAgentRoleIds.SectorJudge, "板块裁决官",
            RecommendPromptTemplates.SectorJudge,
            """{ "selectedSectors": [{"name":"","code":"","reason":"","keyRisk":""}], "eliminatedSectors": [{"name":"","reason":""}] }""",
            [],
            MaxToolCalls: 0);

        // Stage 3: 选股精选
        yield return new RecommendRoleContract(
            RecommendAgentRoleIds.LeaderPicker, "龙头猎手",
            RecommendPromptTemplates.LeaderPicker,
            """{ "picks": [{"symbol":"","name":"","sectorName":"","pickType":"leader","reason":"","metrics":{}}] }""",
            ["stock_search", "stock_kline", "stock_fundamentals", "stock_financial_report", "stock_financial_trend", "web_search"],
            MaxToolCalls: 8);

        yield return new RecommendRoleContract(
            RecommendAgentRoleIds.GrowthPicker, "潜力股猎手",
            RecommendPromptTemplates.GrowthPicker,
            """{ "picks": [{"symbol":"","name":"","sectorName":"","pickType":"growth","triggerCondition":"","reason":""}] }""",
            ["stock_search", "stock_kline", "stock_fundamentals", "stock_financial_report", "stock_financial_trend", "web_search_news", "web_read_url"],
            MaxToolCalls: 8);

        yield return new RecommendRoleContract(
            RecommendAgentRoleIds.ChartValidator, "技术面验证师",
            RecommendPromptTemplates.ChartValidator,
            """{ "validations": [{"symbol":"","name":"","technicalScore":0,"supportLevel":0,"resistanceLevel":0,"volumeAssessment":"","trendState":"","strategySignals":[],"verdict":"pass|caution|fail"}] }""",
            ["stock_kline", "stock_minute", "stock_strategy"],
            MaxToolCalls: 6);

        // Stage 4: 个股辩论
        yield return new RecommendRoleContract(
            RecommendAgentRoleIds.StockBull, "推荐多头",
            RecommendPromptTemplates.StockBull,
            """{ "bullCases": [{"symbol":"","name":"","buyLogic":"","catalysts":[{"event":"","timeline":""}],"evidenceSources":[]}] }""",
            ["web_search"],
            MaxToolCalls: 3);

        yield return new RecommendRoleContract(
            RecommendAgentRoleIds.StockBear, "推荐空头",
            RecommendPromptTemplates.StockBear,
            """{ "bearCases": [{"symbol":"","name":"","risks":[{"risk":"","severity":"","evidence":""}],"counterArguments":[]}] }""",
            ["web_search"],
            MaxToolCalls: 3);

        yield return new RecommendRoleContract(
            RecommendAgentRoleIds.RiskReviewer, "风控审查员",
            RecommendPromptTemplates.RiskReviewer,
            """{ "assessments": [{"symbol":"","name":"","riskLevel":"high|medium|low","invalidConditions":[],"maxLossEstimate":"","recommendation":"approve|conditional|reject"}] }""",
            [],
            MaxToolCalls: 0);

        // Stage 5: 推荐决策
        yield return new RecommendRoleContract(
            RecommendAgentRoleIds.Director, "推荐总监",
            RecommendPromptTemplates.Director,
            """{ "summary":"","sectorCards":[],"stockCards":[],"riskWarnings":[],"confidence":0,"validUntil":"","toolCallStats":{} }""",
            [],
            MaxToolCalls: 0);
    }
}
