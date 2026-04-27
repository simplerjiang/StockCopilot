namespace SimplerJiangAiAgent.Api.Modules.Stocks.Models;

// ── Report block DTO ─────────────────────────────────────────────────

public sealed record ResearchReportBlockDto(
    long Id,
    long TurnId,
    string BlockType,
    int VersionIndex,
    string? Headline,
    string? Summary,
    string? KeyPointsJson,
    string? EvidenceRefsJson,
    string? CounterEvidenceRefsJson,
    string? DisagreementsJson,
    string? RiskLimitsJson,
    string? InvalidationsJson,
    string? RecommendedActionsJson,
    string Status,
    string? DegradedFlagsJson,
    string? MissingEvidence,
    string? ConfidenceImpact,
    string? SourceStageType,
    long? SourceArtifactId,
    DateTime CreatedAt,
    DateTime UpdatedAt);

// ── NextAction contract DTO ──────────────────────────────────────────

public sealed record NextActionDto(
    string ActionType,
    string Label,
    string? TargetSurface,
    long SessionId,
    long TurnId,
    long? ReportBlockId,
    long? DecisionId,
    string? ArtifactRefsJson,
    string? ReasonSummary,
    bool RequiresNewFocus);

// ── Enhanced final decision DTO ──────────────────────────────────────

public sealed record ResearchFinalDecisionDto(
    long Id,
    long TurnId,
    string? Rating,
    string? Action,
    string? ExecutiveSummary,
    string? InvestmentThesis,
    decimal? Confidence,
    string? ConfidenceExplanation,
    string? SupportingEvidenceJson,
    string? CounterEvidenceJson,
    string? RiskConsensus,
    string? DissentJson,
    string? InvalidationConditionsJson,
    IReadOnlyList<NextActionDto> NextActions,
    DateTime CreatedAt);

// ── Full report aggregate DTO ────────────────────────────────────────

public sealed record ResearchTurnReportDto(
    long TurnId,
    IReadOnlyList<ResearchReportBlockDto> Blocks,
    ResearchFinalDecisionDto? FinalDecision,
    IReadOnlyList<RagCitationDto> RagCitations);
