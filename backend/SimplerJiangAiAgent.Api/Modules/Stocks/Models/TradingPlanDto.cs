namespace SimplerJiangAiAgent.Api.Modules.Stocks.Models;

public sealed record TradingPlanDraftRequestDto(
    string Symbol,
    long AnalysisHistoryId
);

public sealed record TradingPlanDraftDto(
    string Symbol,
    string Name,
    string Direction,
    string Status,
    decimal? TriggerPrice,
    decimal? InvalidPrice,
    decimal? StopLossPrice,
    decimal? TakeProfitPrice,
    decimal? TargetPrice,
    string? ExpectedCatalyst,
    string? InvalidConditions,
    string? RiskLimits,
    string? AnalysisSummary,
    long AnalysisHistoryId,
    string SourceAgent,
    string? UserNote
);

public sealed record TradingPlanCreateDto(
    string Symbol,
    string Name,
    string? Direction,
    decimal? TriggerPrice,
    decimal? InvalidPrice,
    decimal? StopLossPrice,
    decimal? TakeProfitPrice,
    decimal? TargetPrice,
    string? ExpectedCatalyst,
    string? InvalidConditions,
    string? RiskLimits,
    string? AnalysisSummary,
    long AnalysisHistoryId,
    string? SourceAgent,
    string? UserNote
);

public sealed record TradingPlanUpdateDto(
    string? Name,
    string? Direction,
    decimal? TriggerPrice,
    decimal? InvalidPrice,
    decimal? StopLossPrice,
    decimal? TakeProfitPrice,
    decimal? TargetPrice,
    string? ExpectedCatalyst,
    string? InvalidConditions,
    string? RiskLimits,
    string? AnalysisSummary,
    string? SourceAgent,
    string? UserNote
);

public sealed record TradingPlanItemDto(
    long Id,
    string Symbol,
    string Name,
    string Direction,
    string Status,
    decimal? TriggerPrice,
    decimal? InvalidPrice,
    decimal? StopLossPrice,
    decimal? TakeProfitPrice,
    decimal? TargetPrice,
    string? ExpectedCatalyst,
    string? InvalidConditions,
    string? RiskLimits,
    string? AnalysisSummary,
    long AnalysisHistoryId,
    string SourceAgent,
    string? UserNote,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? TriggeredAt,
    DateTime? InvalidatedAt,
    DateTime? CancelledAt,
    bool? WatchlistEnsured
);