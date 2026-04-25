namespace SimplerJiangAiAgent.Api.Data.Entities;

public sealed class SectorRotationSnapshot
{
    public long Id { get; set; }
    public DateTime TradingDate { get; set; }
    public DateTime SnapshotTime { get; set; }
    public string BoardType { get; set; } = string.Empty;
    public string SectorCode { get; set; } = string.Empty;
    public string SectorName { get; set; } = string.Empty;
    public decimal ChangePercent { get; set; }
    public decimal MainNetInflow { get; set; }
    public decimal SuperLargeNetInflow { get; set; }
    public decimal LargeNetInflow { get; set; }
    public decimal MediumNetInflow { get; set; }
    public decimal SmallNetInflow { get; set; }
    public decimal TurnoverAmount { get; set; }
    public decimal TurnoverShare { get; set; }
    public decimal? BreadthScore { get; set; }
    public decimal ContinuityScore { get; set; }
    public decimal StrengthScore { get; set; }
    public string NewsSentiment { get; set; } = "中性";
    public int NewsHotCount { get; set; }
    public string? LeaderSymbol { get; set; }
    public string? LeaderName { get; set; }
    public decimal? LeaderChangePercent { get; set; }
    public int RankNo { get; set; }
    public decimal? Momentum5d { get; set; }
    public decimal? Momentum10d { get; set; }
    public decimal? Momentum20d { get; set; }
    public int RankChange5d { get; set; }
    public int RankChange10d { get; set; }
    public int RankChange20d { get; set; }
    public decimal StrengthAvg5d { get; set; }
    public decimal StrengthAvg10d { get; set; }
    public decimal StrengthAvg20d { get; set; }
    public decimal? DiffusionRate { get; set; }
    public int? AdvancerCount { get; set; }
    public int? DeclinerCount { get; set; }
    public int? FlatMemberCount { get; set; }
    public int? LimitUpMemberCount { get; set; }
    public decimal LeaderStabilityScore { get; set; }
    public decimal MainlineScore { get; set; }
    public bool IsMainline { get; set; }
    public string SourceTag { get; set; } = string.Empty;
    public string? RawJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<SectorRotationLeaderSnapshot> Leaders { get; set; } = new();
}
