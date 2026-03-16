namespace SimplerJiangAiAgent.Api.Data.Entities;

public sealed class MarketSentimentSnapshot
{
    public long Id { get; set; }
    public DateTime TradingDate { get; set; }
    public DateTime SnapshotTime { get; set; }
    public string SessionPhase { get; set; } = string.Empty;
    public string StageLabel { get; set; } = string.Empty;
    public decimal StageScore { get; set; }
    public int MaxLimitUpStreak { get; set; }
    public int LimitUpCount { get; set; }
    public int LimitDownCount { get; set; }
    public int BrokenBoardCount { get; set; }
    public decimal BrokenBoardRate { get; set; }
    public int Advancers { get; set; }
    public int Decliners { get; set; }
    public int FlatCount { get; set; }
    public decimal TotalTurnover { get; set; }
    public decimal Top3SectorTurnoverShare { get; set; }
    public decimal Top10SectorTurnoverShare { get; set; }
    public decimal DiffusionScore { get; set; }
    public decimal ContinuationScore { get; set; }
    public string StageLabelV2 { get; set; } = string.Empty;
    public decimal StageConfidence { get; set; }
    public decimal Top3SectorTurnoverShare5dAvg { get; set; }
    public decimal Top10SectorTurnoverShare5dAvg { get; set; }
    public decimal LimitUpCount5dAvg { get; set; }
    public decimal BrokenBoardRate5dAvg { get; set; }
    public string SourceTag { get; set; } = string.Empty;
    public string? RawJson { get; set; }
    public DateTime CreatedAt { get; set; }
}
