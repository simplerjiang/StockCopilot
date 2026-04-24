namespace SimplerJiangAiAgent.FinancialWorker.Models;

public class FinancialChunk
{
    public string ChunkId { get; set; } = Guid.NewGuid().ToString("N");
    public string SourceType { get; set; } = "financial_report";
    public string SourceId { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string ReportDate { get; set; } = "";
    public string? ReportType { get; set; }
    public string? Section { get; set; }
    public string BlockKind { get; set; } = "prose";
    public int? PageStart { get; set; }
    public int? PageEnd { get; set; }
    public string Text { get; set; } = "";
    public string TokenizedText { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
