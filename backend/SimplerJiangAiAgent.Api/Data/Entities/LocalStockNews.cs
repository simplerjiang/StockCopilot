namespace SimplerJiangAiAgent.Api.Data.Entities;

public sealed class LocalStockNews
{
    public long Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? SectorName { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
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