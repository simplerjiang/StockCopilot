using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Microsoft.Extensions.Logging;

namespace SimplerJiangAiAgent.FinancialWorker.Services.Pdf;

public class PdfPigExtractor : IPdfTextExtractor
{
    private readonly ILogger<PdfPigExtractor> _logger;
    public string Name => "PdfPig";

    public PdfPigExtractor(ILogger<PdfPigExtractor> logger) => _logger = logger;

    public Task<PdfExtractionResult> ExtractAsync(string pdfFilePath, CancellationToken ct = default)
    {
        var result = new PdfExtractionResult { ExtractorName = Name, FileSizeBytes = new FileInfo(pdfFilePath).Length };
        try
        {
            using var document = PdfDocument.Open(pdfFilePath);

            foreach (var page in document.GetPages())
            {
                ct.ThrowIfCancellationRequested();
                var text = page.Text;
                result.Pages.Add(text ?? "");

                // PdfPig can extract word-level positioning for table detection
                var words = page.GetWords().ToList();
                var table = TryExtractTable(words, page.Number);
                if (table != null)
                    result.Tables.Add(table);
            }

            result.Success = true;
            _logger.LogDebug("PdfPig 提取完成: {Pages} 页, {Tables} 个表格", result.PageCount, result.Tables.Count);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            _logger.LogWarning(ex, "PdfPig 提取失败: {File}", pdfFilePath);
        }
        return Task.FromResult(result);
    }

    /// <summary>
    /// 简单的表格检测：基于 Y 坐标聚类行，X 坐标聚类列
    /// </summary>
    private static ExtractedTable? TryExtractTable(List<Word> words, int pageNumber)
    {
        if (words.Count < 10) return null;

        // 按 Y 坐标分组（相近的 Y 归为同一行，容差 3pt）
        var rows = new List<List<Word>>();
        var sorted = words.OrderByDescending(w => w.BoundingBox.Bottom).ToList();

        List<Word>? currentRow = null;
        double lastY = double.MaxValue;

        foreach (var word in sorted)
        {
            var y = word.BoundingBox.Bottom;
            if (currentRow == null || Math.Abs(y - lastY) > 3)
            {
                currentRow = new List<Word>();
                rows.Add(currentRow);
                lastY = y;
            }
            currentRow.Add(word);
        }

        // 只有行数足够（>3）才认为是表格
        if (rows.Count < 4) return null;

        var table = new ExtractedTable { PageNumber = pageNumber };
        foreach (var row in rows)
        {
            var cells = row.OrderBy(w => w.BoundingBox.Left)
                          .Select(w => w.Text)
                          .ToList();
            table.Rows.Add(cells);
        }

        return table;
    }
}
