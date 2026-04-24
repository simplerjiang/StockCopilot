namespace SimplerJiangAiAgent.Api.Modules.Stocks.Models;

/// <summary>
/// RAG-sourced financial report citation (v0.4.3 S6).
/// Represents a chunk from financial-rag.db used as evidence in AI analysis.
/// </summary>
public class RagCitationDto
{
    public string ChunkId { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string ReportDate { get; set; } = "";
    public string? ReportType { get; set; }
    public string? Section { get; set; }
    public string BlockKind { get; set; } = "prose";
    public int? PageStart { get; set; }
    public int? PageEnd { get; set; }
    public string Text { get; set; } = "";
    public double Score { get; set; }
    public string Source { get; set; } = "financial-report-rag";
}

public class RagContextRequest
{
    public string Query { get; set; } = "";
    public string? Symbol { get; set; }
    public int? TopK { get; set; }
}
