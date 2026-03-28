namespace SimplerJiangAiAgent.Api.Modules.Stocks.Models;

public sealed record ResearchTurnSubmitRequestDto(
    string Symbol,
    string UserPrompt,
    string? SessionKey,
    string? ContinuationMode);

public sealed record ResearchTurnSubmitResponseDto(
    long SessionId,
    long TurnId,
    string SessionKey,
    string Status);

public sealed record ResearchSessionSummaryDto(
    long Id,
    string SessionKey,
    string Symbol,
    string Name,
    string Status,
    string? LatestRating,
    string? LatestDecisionHeadline,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record ResearchSessionDetailDto(
    long Id,
    string SessionKey,
    string Symbol,
    string Name,
    string Status,
    string? ActiveStage,
    string? LastUserIntent,
    string? LatestRating,
    string? LatestDecisionHeadline,
    IReadOnlyList<ResearchTurnSummaryDto> Turns,
    IReadOnlyList<ResearchReportSnapshotDto> Reports,
    IReadOnlyList<ResearchDecisionSnapshotDto> Decisions,
    IReadOnlyList<ResearchStageSnapshotDto> StageSnapshots,
    IReadOnlyList<ResearchFeedItemDto> FeedItems,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record ResearchTurnSummaryDto(
    long Id,
    int TurnIndex,
    string UserPrompt,
    string Status,
    string ContinuationMode,
    DateTime RequestedAt,
    DateTime? CompletedAt);

public sealed record ResearchStageSnapshotDto(
    long Id,
    string StageType,
    int StageRunIndex,
    string ExecutionMode,
    string Status,
    string? Summary,
    IReadOnlyList<ResearchRoleStateDto> RoleStates,
    DateTime? StartedAt,
    DateTime? CompletedAt);

public sealed record ResearchRoleStateDto(
    long Id,
    string RoleId,
    int RunIndex,
    string Status,
    string? ErrorCode,
    string? ErrorMessage,
    string? LlmTraceId,
    DateTime? StartedAt,
    DateTime? CompletedAt);

public sealed record ResearchFeedItemDto(
    long Id,
    long TurnId,
    string ItemType,
    string? RoleId,
    string Content,
    string? TraceId,
    DateTime CreatedAt);

public sealed record ResearchReportSnapshotDto(
    long Id,
    long TurnId,
    int VersionIndex,
    bool IsFinal,
    string? ReportBlocksJson,
    DateTime CreatedAt);

public sealed record ResearchDecisionSnapshotDto(
    long Id,
    long TurnId,
    string? Rating,
    string? Action,
    string? ExecutiveSummary,
    decimal? Confidence,
    DateTime CreatedAt);

public sealed record ResearchActiveSessionDto(
    long SessionId,
    string SessionKey,
    string Symbol,
    string Status,
    string? ActiveStage,
    ResearchTurnSummaryDto? LatestTurn);
