namespace SimplerJiangAiAgent.Api.Modules.Market.Models;

public sealed record MarketSentimentSummaryDto(
    DateTime SnapshotTime,
    string SessionPhase,
    string StageLabel,
    decimal StageScore,
    int MaxLimitUpStreak,
    int LimitUpCount,
    int LimitDownCount,
    int BrokenBoardCount,
    decimal BrokenBoardRate,
    int Advancers,
    int Decliners,
    int FlatCount,
    decimal TotalTurnover,
    decimal Top3SectorTurnoverShare,
    decimal Top10SectorTurnoverShare,
    decimal DiffusionScore,
    decimal ContinuationScore,
    string StageLabelV2,
    decimal StageConfidence,
    decimal Top3SectorTurnoverShare5dAvg,
    decimal Top10SectorTurnoverShare5dAvg,
    decimal LimitUpCount5dAvg,
    decimal BrokenBoardRate5dAvg,
    bool IsDegraded = false,
    string? DegradeReason = null);

public sealed record MarketSentimentHistoryPointDto(
    DateTime TradingDate,
    DateTime SnapshotTime,
    string StageLabel,
    decimal StageScore,
    int LimitUpCount,
    int LimitDownCount,
    int BrokenBoardCount);

public sealed record SectorRotationListItemDto(
    string BoardType,
    string SectorCode,
    string SectorName,
    decimal ChangePercent,
    decimal MainNetInflow,
    decimal? BreadthScore,
    decimal ContinuityScore,
    decimal StrengthScore,
    string NewsSentiment,
    int NewsHotCount,
    string? LeaderSymbol,
    string? LeaderName,
    decimal? LeaderChangePercent,
    int RankNo,
    DateTime SnapshotTime,
    int RankChange5d,
    int RankChange10d,
    int RankChange20d,
    decimal StrengthAvg5d,
    decimal StrengthAvg10d,
    decimal StrengthAvg20d,
    decimal? DiffusionRate,
    int? AdvancerCount,
    int? DeclinerCount,
    int? FlatMemberCount,
    int? LimitUpMemberCount,
    decimal LeaderStabilityScore,
    decimal MainlineScore,
    bool IsMainline);

public sealed record SectorRotationPageDto(
    string BoardType,
    int Page,
    int PageSize,
    int Total,
    string Sort,
    DateTime? SnapshotTime,
    IReadOnlyList<SectorRotationListItemDto> Items,
    bool IsDegraded = false,
    string? DegradeReason = null);

public sealed record RealtimeSectorBoardItemDto(
    string BoardType,
    string SectorCode,
    string SectorName,
    decimal ChangePercent,
    decimal MainNetInflow,
    decimal SuperLargeNetInflow,
    decimal LargeNetInflow,
    decimal MediumNetInflow,
    decimal SmallNetInflow,
    decimal TurnoverAmount,
    decimal TurnoverShare,
    int RankNo,
    DateTime SnapshotTime);

public sealed record RealtimeSectorBoardPageDto(
    string BoardType,
    int Take,
    string Sort,
    DateTime SnapshotTime,
    IReadOnlyList<RealtimeSectorBoardItemDto> Items);

public sealed record SectorRotationLeaderDto(
    int RankInSector,
    string Symbol,
    string Name,
    decimal ChangePercent,
    decimal TurnoverAmount,
    bool IsLimitUp,
    bool IsBrokenBoard);

public sealed record SectorRotationNewsDto(
    string Title,
    string? TranslatedTitle,
    string Source,
    string Sentiment,
    DateTime PublishTime,
    string? Url);

public sealed record SectorRotationHistoryPointDto(
    DateTime TradingDate,
    DateTime SnapshotTime,
    decimal ChangePercent,
    decimal? BreadthScore,
    decimal ContinuityScore,
    decimal StrengthScore,
    int RankNo,
    decimal? DiffusionRate,
    int? AdvancerCount,
    int? DeclinerCount,
    int? FlatMemberCount,
    int? LimitUpMemberCount,
    int RankChange5d,
    int RankChange10d,
    int RankChange20d,
    decimal StrengthAvg5d,
    decimal StrengthAvg10d,
    decimal StrengthAvg20d,
    decimal LeaderStabilityScore,
    decimal MainlineScore,
    bool IsMainline);

public sealed record SectorRotationTrendDto(
    string BoardType,
    string SectorCode,
    string Window,
    IReadOnlyList<SectorRotationHistoryPointDto> Points);

public sealed record SectorRotationDetailDto(
    SectorRotationListItemDto Snapshot,
    IReadOnlyList<SectorRotationHistoryPointDto> History,
    IReadOnlyList<SectorRotationLeaderDto> Leaders,
    IReadOnlyList<SectorRotationNewsDto> News);

public sealed record StockMarketContextDto(
    string? StageLabel,
    decimal StageConfidence,
    string? StockSectorName,
    string? MainlineSectorName,
    string? SectorCode,
    decimal MainlineScore,
    decimal SuggestedPositionScale,
    string? ExecutionFrequencyLabel,
    bool CounterTrendWarning,
    bool IsMainlineAligned);
