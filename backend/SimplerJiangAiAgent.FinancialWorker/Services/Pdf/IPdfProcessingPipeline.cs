namespace SimplerJiangAiAgent.FinancialWorker.Services.Pdf;

/// <summary>
/// Minimal interface for the PDF processing pipeline, exposing only methods used by
/// <see cref="FinancialDataOrchestrator"/>. Introduced for testability.
/// </summary>
public interface IPdfProcessingPipeline
{
    Task<PdfPipelineResult> ProcessAsync(string symbol, int maxReports = 3, CancellationToken ct = default);
}
