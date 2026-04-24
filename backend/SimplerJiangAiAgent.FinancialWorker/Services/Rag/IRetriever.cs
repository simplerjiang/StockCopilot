namespace SimplerJiangAiAgent.FinancialWorker.Services.Rag;

public interface IRetriever
{
    Task<List<RetrievedChunk>> RetrieveAsync(
        string query,
        string? symbol = null,
        string? reportDate = null,
        string? reportType = null,
        int topK = 5,
        CancellationToken ct = default);
}

public class RetrievedChunk
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
}
