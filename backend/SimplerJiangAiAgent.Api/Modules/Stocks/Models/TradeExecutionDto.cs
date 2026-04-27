namespace SimplerJiangAiAgent.Api.Modules.Stocks.Models;

// 录入交易请求
public sealed record TradeExecutionCreateDto(
    long? PlanId,
    string Symbol,
    string Name,
    string Direction,
    string TradeType,
    decimal ExecutedPrice,
    int Quantity,
    DateTime ExecutedAt,
    decimal? Commission,
    string? UserNote,
    string? PlanAction = null,
    string? ExecutionAction = null,
    IReadOnlyList<string>? DeviationTags = null,
    string? DeviationNote = null,
    string? AbandonReason = null
);

// 修改交易请求
public sealed record TradeExecutionUpdateDto(
    decimal ExecutedPrice,
    int Quantity,
    DateTime ExecutedAt,
    decimal? Commission,
    string? UserNote,
    string? PlanAction = null,
    string? ExecutionAction = null,
    IReadOnlyList<string>? DeviationTags = null,
    string? DeviationNote = null,
    string? AbandonReason = null
);

public sealed record ResetAllTradesRequestDto(string? ConfirmText);

// 交易记录响应
public sealed record TradeExecutionItemDto(
    long Id,
    long? PlanId,
    string? PlanTitle,
    string Symbol,
    string Name,
    string Direction,
    string TradeType,
    decimal ExecutedPrice,
    int Quantity,
    DateTime ExecutedAt,
    decimal? Commission,
    string? UserNote,
    DateTime CreatedAt,
    decimal? CostBasis,
    decimal? RealizedPnL,
    decimal? ReturnRate,
    string ComplianceTag,
    string? AgentDirection,
    decimal? AgentConfidence,
    string? MarketStageAtTrade,
    string? PlanSourceAgent,
    string? PlanAction,
    string? ExecutionAction,
    IReadOnlyList<string> DeviationTags,
    string? DeviationNote,
    string? AbandonReason,
    TradingPlanScenarioStatusDto? ScenarioSnapshot,
    TradingPlanPositionContextDto? PositionSnapshot,
    string? CoachTip
);

// 盈亏汇总响应
public sealed record TradeSummaryDto(
    string Period,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    int TotalTrades,
    int WinCount,
    int LossCount,
    decimal WinRate,
    decimal TotalPnL,
    decimal AveragePnL,
    decimal ProfitLossRatio,
    int DayTradeCount,
    decimal DayTradePnL,
    int PlannedTradeCount,
    decimal ComplianceRate,
    decimal MaxSingleLoss
);

// 持仓项响应
public sealed record PositionItemDto(
    long Id,
    string Symbol,
    string Name,
    int Quantity,
    decimal AverageCost,
    decimal TotalCost,
    decimal? LatestPrice,
    decimal? MarketValue,
    decimal? UnrealizedPnL,
    decimal? UnrealizedReturnRate,
    decimal? PositionRatio
);

// 持仓快照响应
public sealed record PortfolioSnapshotDto(
    decimal TotalCapital,
    decimal TotalCost,
    decimal TotalMarketValue,
    decimal TotalUnrealizedPnL,
    decimal AvailableCash,
    decimal TotalPositionRatio,
    IReadOnlyList<PositionItemDto> Positions
);

// 本金设置请求/响应
public sealed record PortfolioSettingsDto(decimal TotalCapital, DateTime UpdatedAt);
public sealed record PortfolioSettingsUpdateDto(decimal TotalCapital);

// 实盘胜率响应
public sealed record TradeWinRateDto(
    int TotalTrades,
    int WinCount,
    decimal WinRate,
    decimal AveragePnL,
    decimal AverageReturnRate,
    IReadOnlyList<TradeExecutionItemDto> RecentTrades
);

// 计划偏差响应
public sealed record PlanDeviationDto(
    long PlanId,
    string? PlanTitle,
    string PlanDirection,
    decimal? TriggerPrice,
    IReadOnlyList<TradeExecutionItemDto> Executions,
    decimal? AverageExecutedPrice,
    decimal? PriceDeviation
);

// Agent合规统计
public sealed record ComplianceStatsDto(
    int TotalTrades,
    int FollowedPlanCount,
    int DeviatedFromPlanCount,
    int UnplannedCount,
    decimal FollowedRate,
    decimal DeviatedRate,
    decimal UnplannedRate
);

// 交易查询参数
public sealed record TradeQueryParams(
    string? Symbol,
    DateTime? From,
    DateTime? To,
    string? Type
);

// 风险暴露响应
public sealed record PortfolioExposureDto(
    decimal TotalExposure,
    decimal PendingExposure,
    decimal CombinedExposure,
    IReadOnlyList<SymbolExposureDto> SymbolExposures,
    IReadOnlyList<SectorExposureDto> SectorExposures,
    MarketExecutionModeDto? CurrentMode = null
);

public sealed record SymbolExposureDto(string Symbol, string Name, decimal Exposure, decimal MarketValue);
public sealed record SectorExposureDto(string SectorName, decimal Exposure, decimal MarketValue);

// LLM 上下文格式的持仓快照
public sealed record PortfolioContextDto(
    decimal TotalCapital,
    decimal TotalMarketValue,
    decimal TotalPositionRatio,
    decimal AvailableCash,
    decimal TotalUnrealizedPnL,
    IReadOnlyList<PortfolioContextPositionDto> Positions
);

public sealed record PortfolioContextPositionDto(
    string Symbol,
    string Name,
    int Quantity,
    decimal AverageCost,
    decimal? LatestPrice,
    decimal? MarketValue,
    decimal? UnrealizedPnL,
    decimal? PositionRatio
);

// 市场阶段 → 执行模式映射
public sealed record MarketExecutionModeDto(
    string MarketStage,
    string ExecutionMode,
    decimal PositionScale,
    string ConfirmationLevel,
    string? WarningMessage
);

// 复盘生成请求
public sealed record TradeReviewGenerateDto(
    string Type,
    DateTime? From,
    DateTime? To
);

// 复盘响应
public sealed record TradeReviewItemDto(
    long Id,
    string ReviewType,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    int TradeCount,
    decimal TotalPnL,
    decimal WinRate,
    decimal ComplianceRate,
    string ReviewContent,
    DateTime CreatedAt
);

public static class MarketExecutionModeMapper
{
    public static MarketExecutionModeDto GetMode(string? stageLabel)
    {
        return stageLabel switch
        {
            "主升" or "主升期" => new("主升", "积极", 1.0m, "normal", null),
            "分歧" or "分歧期" => new("分歧", "谨慎", 0.7m, "confirm", "当前市场分歧，建议谨慎操作"),
            "退潮" or "退潮期" => new("退潮", "防守", 0.4m, "strong-confirm", "当前市场退潮，建议仓位已自动下调至 40%，确定继续？"),
            "混沌" or "混沌期" => new("混沌", "观望", 0.3m, "discouraged", "当前市场混沌，不建议新建计划"),
            _ => new(stageLabel ?? "未知", "谨慎", 0.7m, "confirm", null)
        };
    }
}

// 交易行为统计响应
public sealed record TradeBehaviorStatsDto(
    int Trades7Days,
    int Trades30Days,
    decimal AvgDailyTrades7Days,
    decimal AvgDailyTrades30Days,
    int PlannedTrades30Days,
    int TotalTrades30Days,
    decimal? PlanExecutionRate,
    int CurrentLossStreak,
    int MaxLossStreak30Days,
    int ChasingBuyCount30Days,
    decimal ChasingBuyRate,
    bool IsOverTrading,
    int? DisciplineScore,
    IReadOnlyList<BehaviorAlertDto> ActiveAlerts
);

public sealed record BehaviorAlertDto(
    string AlertType,
    string Severity,
    string Message
);
