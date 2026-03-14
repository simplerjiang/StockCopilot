namespace SimplerJiangAiAgent.Api.Data.Entities;

public sealed class ActiveWatchlist
{
    public long Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string SourceTag { get; set; } = "manual";
    public string? Note { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastQuoteSyncAt { get; set; }
}