namespace SimplerJiangAiAgent.Api.Data.Entities;

public sealed class LocalSectorReport
{
    public long Id { get; set; }
    public string? Symbol { get; set; }
    public string? SectorName { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string SourceTag { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public DateTime PublishTime { get; set; }
    public DateTime CrawledAt { get; set; }
    public string? Url { get; set; }
    public bool IsAiProcessed { get; set; }
    public string? TranslatedTitle { get; set; }
    public string AiSentiment { get; set; } = "中性";
    public string? AiTarget { get; set; }
    public string? AiTags { get; set; }
}