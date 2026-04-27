namespace SimplerJiangAiAgent.FinancialWorker.Models;

public class RagSearchRequest
{
    public string Query { get; set; } = "";
    public string? Symbol { get; set; }
    public string? ReportDate { get; set; }
    public string? ReportType { get; set; }
    public int? TopK { get; set; }
    public string? Mode { get; set; }  // "bm25" | "vector" | "hybrid" (default: hybrid)
    public string? SourceType { get; set; }  // "financial_report" | "announcement" (default: null = all)
}

public class RagSearchResponse
{
    public string Query { get; set; } = "";
    public int TotalResults { get; set; }
    public string Mode { get; set; } = "hybrid";  // Actual mode used (may differ from requested)
    public List<RagSearchResultItem> Results { get; set; } = new();
}

public class RagSearchResultItem
{
    public string ChunkId { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string ReportDate { get; set; } = "";
    public string? ReportType { get; set; }
    public string? Section { get; set; }
    public string BlockKind { get; set; } = "prose";
    public int? PageStart { get; set; }
    public int? PageEnd { get; set; }
    public string Text { get; set; } = "";
    public double Score { get; set; }
    public string? SourceFile { get; set; }  // For future: link to PDF file
}
