using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using Microsoft.Extensions.Logging;

namespace SimplerJiangAiAgent.FinancialWorker.Services.Pdf;

public class IText7Extractor : IPdfTextExtractor
{
    private readonly ILogger<IText7Extractor> _logger;
    public string Name => "iText7";

    public IText7Extractor(ILogger<IText7Extractor> logger) => _logger = logger;

    public Task<PdfExtractionResult> ExtractAsync(string pdfFilePath, CancellationToken ct = default)
    {
        var result = new PdfExtractionResult { ExtractorName = Name, FileSizeBytes = new FileInfo(pdfFilePath).Length };
        try
        {
            using var reader = new PdfReader(pdfFilePath);
            using var pdfDoc = new PdfDocument(reader);
            var pageCount = pdfDoc.GetNumberOfPages();

            for (var i = 1; i <= pageCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                var page = pdfDoc.GetPage(i);
                var strategy = new LocationTextExtractionStrategy();
                var text = PdfTextExtractor.GetTextFromPage(page, strategy);
                result.Pages.Add(text ?? "");
            }

            result.Success = true;
            _logger.LogDebug("iText7 提取完成: {Pages} 页", pageCount);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            _logger.LogWarning(ex, "iText7 提取失败: {File}", pdfFilePath);
        }
        return Task.FromResult(result);
    }
}
