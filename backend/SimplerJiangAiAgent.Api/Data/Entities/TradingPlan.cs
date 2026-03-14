namespace SimplerJiangAiAgent.Api.Data.Entities;

public enum TradingPlanDirection
{
    Long = 1,
    Short = 2
}

public enum TradingPlanStatus
{
    Draft = 0,
    Pending = 1,
    Triggered = 2,
    Invalid = 3,
    Cancelled = 4
}

public sealed class TradingPlan
{
    public long Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TradingPlanDirection Direction { get; set; } = TradingPlanDirection.Long;
    public TradingPlanStatus Status { get; set; } = TradingPlanStatus.Pending;
    public decimal? TriggerPrice { get; set; }
    public decimal? InvalidPrice { get; set; }
    public decimal? StopLossPrice { get; set; }
    public decimal? TakeProfitPrice { get; set; }
    public decimal? TargetPrice { get; set; }
    public string? ExpectedCatalyst { get; set; }
    public string? InvalidConditions { get; set; }
    public string? RiskLimits { get; set; }
    public string? AnalysisSummary { get; set; }
    public long AnalysisHistoryId { get; set; }
    public string SourceAgent { get; set; } = "commander";
    public string? UserNote { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? TriggeredAt { get; set; }
    public DateTime? InvalidatedAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    public StockAgentAnalysisHistory? AnalysisHistory { get; set; }
}