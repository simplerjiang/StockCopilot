using SimplerJiangAiAgent.Api.Modules.Market.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Models;

public sealed record StockAgentEvidenceFeatureSummaryDto(
    int TotalEvidenceCount,
    int HighQualityEvidenceCount,
    int RecentEvidenceCount,
    int WeakEvidenceCount,
    decimal CoverageScore,
    decimal ConflictScore,
    bool ExpandedWindow,
    decimal FreshnessHours,
    IReadOnlyList<string> SentimentBreakdown);

public sealed record StockAgentTrendFeatureSummaryDto(
    string TrendState,
    decimal Return5dPercent,
    decimal Return20dPercent,
    decimal Ma5,
    decimal Ma20,
    decimal AtrPercent,
    decimal BreakoutDistancePercent,
    decimal Vwap,
    string SessionPhase);

public sealed record StockAgentValuationFeatureSummaryDto(
    string PeBand,
    decimal? PeRatio,
    string FloatMarketCapBand,
    decimal? FloatMarketCap,
    decimal? VolumeRatio,
    int? ShareholderCount);

public sealed record StockAgentRiskFeatureSummaryDto(
    decimal VolatilityScore,
    bool HighTurnoverRisk,
    bool VolumeSpikeRisk,
    bool CounterTrendRisk,
    IReadOnlyList<string> Flags);

public sealed record StockAgentDeterministicFeaturesDto(
    StockAgentEvidenceFeatureSummaryDto Evidence,
    StockAgentTrendFeatureSummaryDto Trend,
    StockAgentValuationFeatureSummaryDto Valuation,
    StockAgentRiskFeatureSummaryDto Risk,
    int MarketNoiseFilteredCount,
    IReadOnlyList<string> DegradedFlags);

public sealed record StockAgentPreparedContextDto(
    StockAgentLocalFactPackageDto LocalFacts,
    StockAgentDeterministicFeaturesDto Features);

public sealed record StockCopilotMcpCacheDto(
    bool Hit,
    string Source,
    DateTime GeneratedAt);

public sealed record StockCopilotMcpFeatureDto(
    string Name,
    string Label,
    string ValueType,
    decimal? NumberValue,
    string? TextValue,
    string? Unit,
    string? Description);

public sealed record StockCopilotMcpEvidenceDto(
    string Point,
    string? Title,
    string Source,
    DateTime? PublishedAt,
    DateTime? CrawledAt,
    string? Url,
    string? Excerpt,
    string? Summary,
    string ReadMode,
    string ReadStatus,
    DateTime? IngestedAt,
    long? LocalFactId,
    string? SourceRecordId,
    string? Level,
    string? Sentiment,
    string? Target,
    IReadOnlyList<string> Tags);

public sealed record StockCopilotMcpMetaDto(
    string Version,
    string PolicyClass,
    string ToolName,
    string? Symbol,
    string? Interval,
    string? Query,
    StockMarketContextDto? MarketContext);

public sealed record StockCopilotMcpEnvelopeDto<T>(
    string TraceId,
    string TaskId,
    string ToolName,
    long LatencyMs,
    StockCopilotMcpCacheDto Cache,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> DegradedFlags,
    T Data,
    IReadOnlyList<StockCopilotMcpEvidenceDto> Evidence,
    IReadOnlyList<StockCopilotMcpFeatureDto> Features,
    StockCopilotMcpMetaDto Meta);

public sealed record StockCopilotKeyLevelsDto(
    decimal? Support,
    decimal? Resistance,
    decimal? Ma5,
    decimal? Ma20,
    decimal? BreakoutTrigger,
    decimal? BreakdownTrigger);

public sealed record StockCopilotKlineDataDto(
    string Symbol,
    string Interval,
    int WindowSize,
    IReadOnlyList<KLinePointDto> Bars,
    StockCopilotKeyLevelsDto KeyLevels,
    string TrendState,
    decimal Return5dPercent,
    decimal Return20dPercent,
    decimal AtrPercent,
    decimal BreakoutDistancePercent);

public sealed record StockCopilotMinuteDataDto(
    string Symbol,
    string SessionPhase,
    int WindowSize,
    IReadOnlyList<MinuteLinePointDto> Points,
    decimal? Vwap,
    decimal? OpeningDrivePercent,
    decimal? AfternoonDriftPercent,
    decimal? IntradayRangePercent);

public sealed record StockCopilotStrategySignalDto(
    string Strategy,
    string Timeframe,
    string Signal,
    decimal? NumericValue,
    string? State,
    string Description);

public sealed record StockCopilotStrategyDataDto(
    string Symbol,
    string Interval,
    IReadOnlyList<string> RequestedStrategies,
    IReadOnlyList<StockCopilotStrategySignalDto> Signals);

public sealed record StockCopilotNewsDataDto(
    string Symbol,
    string Level,
    int ItemCount,
    DateTime? LatestPublishedAt);

public sealed record StockCopilotSearchResultDto(
    string Title,
    string Url,
    string Source,
    decimal? Score,
    DateTime? PublishedAt,
    string? Excerpt);

public sealed record StockCopilotSearchDataDto(
    string Query,
    string Provider,
    bool TrustedOnly,
    int ResultCount,
    IReadOnlyList<StockCopilotSearchResultDto> Results);

public sealed record StockCopilotTurnDraftRequestDto(
    string Symbol,
    string Question,
    string? SessionKey,
    string? SessionTitle,
    string? TaskId,
    bool AllowExternalSearch);

public sealed record StockCopilotPlanStepDto(
    string StepId,
    string Owner,
    string Title,
    string Description,
    string Status,
    IReadOnlyList<string> DependsOn,
    string? ToolName);

public sealed record StockCopilotToolCallDto(
    string CallId,
    string StepId,
    string ToolName,
    string PolicyClass,
    string Purpose,
    string InputSummary,
    string ApprovalStatus,
    string? BlockedReason);

public sealed record StockCopilotToolResultDto(
    string CallId,
    string ToolName,
    string Status,
    string? TraceId,
    int EvidenceCount,
    int FeatureCount,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> DegradedFlags,
    IReadOnlyList<StockCopilotMcpEvidenceDto> Evidence,
    string Summary);

public sealed record StockCopilotFinalAnswerDto(
    string Status,
    string Summary,
    string GroundingMode,
    decimal? ConfidenceScore,
    bool NeedsToolExecution,
    IReadOnlyList<string> Constraints);

public sealed record StockCopilotLoopBudgetDto(
    int MaxRounds,
    int MaxToolCalls,
    int MaxExternalSearchCalls,
    int MaxTotalLatencyMs,
    int MaxPollingSteps);

public sealed record StockCopilotLoopExecutionDto(
    int CompletedRounds,
    int ToolCallsExecuted,
    int ExternalSearchCallsExecuted,
    long TotalLatencyMs,
    int ConsumedPollingSteps,
    string Status,
    string? StopReason,
    bool ForcedClose);

public sealed record StockCopilotFollowUpActionDto(
    string ActionId,
    string Label,
    string ActionType,
    string? ToolName,
    string Description,
    bool Enabled,
    string? BlockedReason);

public sealed record StockCopilotTurnDto(
    string TurnId,
    string SessionKey,
    string Symbol,
    string UserQuestion,
    DateTime CreatedAt,
    string Status,
    string PlannerSummary,
    string GovernorSummary,
    StockMarketContextDto? MarketContext,
    IReadOnlyList<StockCopilotPlanStepDto> PlanSteps,
    IReadOnlyList<StockCopilotToolCallDto> ToolCalls,
    IReadOnlyList<StockCopilotToolResultDto> ToolResults,
    StockCopilotFinalAnswerDto FinalAnswer,
    IReadOnlyList<StockCopilotFollowUpActionDto> FollowUpActions,
    StockCopilotLoopBudgetDto? LoopBudget = null,
    StockCopilotLoopExecutionDto? LoopExecution = null);

public sealed record StockCopilotToolExecutionMetricDto(
    string CallId,
    string ToolName,
    string PolicyClass,
    long LatencyMs,
    int EvidenceCount,
    int FeatureCount,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> DegradedFlags);

public sealed record StockCopilotAcceptanceBaselineRequestDto(
    string Symbol,
    StockCopilotTurnDto Turn,
    IReadOnlyList<StockCopilotToolExecutionMetricDto> ToolExecutions,
    int ReplaySampleTake);

public sealed record StockCopilotAcceptanceMetricDto(
    string Key,
    string Label,
    decimal Value,
    string Unit,
    string Status,
    string Description);

public sealed record StockCopilotAcceptanceBaselineDto(
    string Symbol,
    string SessionKey,
    string TurnId,
    DateTime GeneratedAt,
    decimal OverallScore,
    int ApprovedToolCallCount,
    int ExecutedToolCallCount,
    decimal AverageLatencyMs,
    int WarningCount,
    int DegradedFlagCount,
    IReadOnlyList<string> Highlights,
    IReadOnlyList<StockCopilotAcceptanceMetricDto> Metrics,
    StockAgentReplayBaselineDto ReplayBaseline);

public sealed record StockCopilotSessionDto(
    string SessionKey,
    string Symbol,
    string Title,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<StockCopilotTurnDto> Turns);

public sealed record StockAgentReplayHorizonMetricDto(
    int HorizonDays,
    int SampleCount,
    decimal HitRate,
    decimal AverageReturnPercent,
    decimal BrierScore,
    decimal BullWinRate,
    decimal BearWinRate,
    decimal BaseWinRate);

public sealed record StockAgentReplaySampleDto(
    long HistoryId,
    string Symbol,
    DateTime CreatedAt,
    string Direction,
    decimal ConfidenceScore,
    decimal BullProbability,
    decimal BaseProbability,
    decimal BearProbability,
    decimal? Return1dPercent,
    decimal? Return3dPercent,
    decimal? Return5dPercent,
    decimal? Return10dPercent,
    bool EvidenceTraceable,
    bool EvidencePolluted,
    bool RevisionExplained,
    string? Summary,
    string? TraceId);

public sealed record StockAgentReplayBaselineDto(
    string Scope,
    DateTime GeneratedAt,
    int SampleCount,
    decimal TraceableEvidenceRate,
    decimal ParseRepairRate,
    decimal PollutedEvidenceRate,
    decimal RevisionCompletenessRate,
    IReadOnlyList<StockAgentReplayHorizonMetricDto> Horizons,
    IReadOnlyList<StockAgentReplaySampleDto> Samples);

public sealed class StockCopilotSearchOptions
{
    public const string SectionName = "StockCopilot:Search";

    public bool Enabled { get; set; }
    public string Provider { get; set; } = "tavily";
    public string? ApiKey { get; set; }
    public string BaseUrl { get; set; } = "https://api.tavily.com";
}