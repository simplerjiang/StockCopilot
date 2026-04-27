namespace SimplerJiangAiAgent.Api.Data.Entities;

public enum ResearchTurnStatus
{
    Draft,
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

public enum ResearchContinuationMode
{
    ContinueSession,
    NewSession,
    PartialRerun,
    FullRerun
}

public sealed class ResearchTurn
{
    public long Id { get; set; }
    public long SessionId { get; set; }
    public int TurnIndex { get; set; }
    public string UserPrompt { get; set; } = string.Empty;
    public ResearchTurnStatus Status { get; set; }
    public ResearchContinuationMode ContinuationMode { get; set; }
    public string? ReuseScope { get; set; }
    public string? RerunScope { get; set; }
    public string? ChangeSummary { get; set; }
    public string? RoutingDecision { get; set; }
    public string? RoutingReasoning { get; set; }
    public decimal? RoutingConfidence { get; set; }
    public int? RoutingStageIndex { get; set; }
    public string? StopReason { get; set; }
    public string? DegradedFlagsJson { get; set; }
    public string? RagCitationsJson { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public ResearchSession Session { get; set; } = null!;
    public ICollection<ResearchStageSnapshot> StageSnapshots { get; set; } = new List<ResearchStageSnapshot>();
    public ICollection<ResearchFeedItem> FeedItems { get; set; } = new List<ResearchFeedItem>();
}
