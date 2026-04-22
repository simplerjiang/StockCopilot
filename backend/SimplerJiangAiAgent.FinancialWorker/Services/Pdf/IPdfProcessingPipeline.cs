namespace SimplerJiangAiAgent.FinancialWorker.Services.Pdf;

/// <summary>
/// Minimal interface for the PDF processing pipeline, exposing only methods used by
/// <see cref="FinancialDataOrchestrator"/> 与 v0.4.1 §S2 reparse 接口。
/// </summary>
public interface IPdfProcessingPipeline
{
    Task<PdfPipelineResult> ProcessAsync(string symbol, int maxReports = 3, CancellationToken ct = default);

    /// <summary>
    /// v0.4.1 §S2：根据 PdfFileDocument.Id（LiteDB ObjectId 字符串）单文件重解析。
    /// 物理文件丢失时 <see cref="PdfReparseOutcome.PhysicalFileMissing"/> 置 true；
    /// 文档不存在时 <see cref="PdfReparseOutcome.DocumentFound"/> 为 false。
    /// 异常会在内部捕获并以 status=failed 回写到 stage 日志，方法不向外抛出（除 <see cref="OperationCanceledException"/>）。
    /// </summary>
    Task<PdfReparseOutcome> ReparseAsync(string id, CancellationToken ct = default);
}

/// <summary>
/// v0.4.1 §S2：Reparse 的结构化结果（Api 层将其映射为 200/404 响应）。
/// </summary>
public class PdfReparseOutcome
{
    public bool DocumentFound { get; set; }
    public bool PhysicalFileMissing { get; set; }
    public bool Success { get; set; }
    public string? Symbol { get; set; }
    public string? FileName { get; set; }
    public string? LocalPath { get; set; }
    public string? Error { get; set; }
}
