using SimplerJiangAiAgent.Api.Modules.Market.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Models;

public sealed record StockSignalDto(
    string Symbol,
    string Name,
    DateTime GeneratedAt,
    string Recommendation,
    int Confidence,
    int EventImpactScore,
    int TrendScore,
    int AlignmentScore,
    IReadOnlyList<string> Signals,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> CounterEvidence
);

public sealed record StockPositionGuidanceRequestDto(
    string Symbol,
    string? Source,
    string RiskLevel,
    decimal Capital,
    decimal CurrentPositionPercent = 0
);

public sealed record StockPositionGuidanceDto(
    string Symbol,
    string Name,
    DateTime GeneratedAt,
    string RiskLevel,
    string Action,
    decimal TargetPositionPercent,
    decimal MaxDrawdownPercent,
    decimal StopLossPercent,
    decimal TakeProfitPercent,
    decimal MarketStageMultiplier,
    StockMarketContextDto? MarketContext,
    IReadOnlyList<string> Reasons
);
