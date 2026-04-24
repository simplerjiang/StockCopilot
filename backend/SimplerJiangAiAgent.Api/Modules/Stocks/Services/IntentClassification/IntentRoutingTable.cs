namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services.IntentClassification;

public static class IntentRoutingTable
{
    private static readonly Dictionary<IntentType, RoutingRule> Rules = new()
    {
        [IntentType.Valuation] = new(SuggestedPipeline.Research, RequiresRag: true, RequiresFinancialData: true),
        [IntentType.Risk] = new(SuggestedPipeline.Research, RequiresRag: true, RequiresFinancialData: true),
        [IntentType.FinancialAnalysis] = new(SuggestedPipeline.Research, RequiresRag: true, RequiresFinancialData: true),
        [IntentType.PerformanceAttribution] = new(SuggestedPipeline.Research, RequiresRag: true, RequiresFinancialData: false),
        [IntentType.TechnicalAnalysis] = new(SuggestedPipeline.LiveGate, RequiresRag: false, RequiresFinancialData: false),
        [IntentType.MarketOverview] = new(SuggestedPipeline.LiveGate, RequiresRag: false, RequiresFinancialData: false),
        [IntentType.StockPicking] = new(SuggestedPipeline.Recommend, RequiresRag: false, RequiresFinancialData: false),
        [IntentType.General] = new(SuggestedPipeline.LiveGate, RequiresRag: false, RequiresFinancialData: false),
        [IntentType.Clarification] = new(SuggestedPipeline.DirectReply, RequiresRag: false, RequiresFinancialData: false),
    };

    public static QuestionIntent Resolve(IntentType type, double confidence, string? reason = null)
    {
        var rule = Rules.GetValueOrDefault(type, new(SuggestedPipeline.LiveGate, false, false));
        return new QuestionIntent(type, confidence, rule.RequiresRag, rule.RequiresFinancialData, rule.Pipeline, reason);
    }

    public static RoutingRule GetRule(IntentType type)
        => Rules.GetValueOrDefault(type, new(SuggestedPipeline.LiveGate, false, false));

    public record RoutingRule(SuggestedPipeline Pipeline, bool RequiresRag, bool RequiresFinancialData);
}
