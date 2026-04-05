namespace SimplerJiangAiAgent.FinancialWorker.Services.Pdf;

/// <summary>
/// PDF 文本提取结果
/// </summary>
public class PdfExtractionResult
{
    public string ExtractorName { get; set; } = "";
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>按页存储的文本内容</summary>
    public List<string> Pages { get; set; } = new();

    /// <summary>完整纯文本（所有页拼接）</summary>
    public string FullText => string.Join("\n", Pages);

    /// <summary>提取的表格数据（每个表格是行列矩阵）</summary>
    public List<ExtractedTable> Tables { get; set; } = new();

    public int PageCount => Pages.Count;
    public long FileSizeBytes { get; set; }
}

public class ExtractedTable
{
    public int PageNumber { get; set; }
    public List<List<string>> Rows { get; set; } = new();
    public string? NearbyHeading { get; set; }
}

/// <summary>
/// PDF 文本提取器接口
/// </summary>
public interface IPdfTextExtractor
{
    string Name { get; }
    Task<PdfExtractionResult> ExtractAsync(string pdfFilePath, CancellationToken ct = default);
}
