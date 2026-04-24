namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services.IntentClassification;

public enum IntentType
{
    Valuation,
    Risk,
    FinancialAnalysis,
    PerformanceAttribution,
    TechnicalAnalysis,
    MarketOverview,
    StockPicking,
    General,
    Clarification
}

public enum SuggestedPipeline
{
    Research,
    Recommend,
    LiveGate,
    DirectReply
}

public record QuestionIntent(
    IntentType Type,
    double Confidence,
    bool RequiresRag,
    bool RequiresFinancialData,
    SuggestedPipeline Pipeline,
    string? Reason = null
);
