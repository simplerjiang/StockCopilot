using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.Extensions.Logging;

namespace SimplerJiangAiAgent.FinancialWorker.Services.Pdf;

public class DocnetExtractor : IPdfTextExtractor
{
    private readonly ILogger<DocnetExtractor> _logger;
    public string Name => "Docnet";

    public DocnetExtractor(ILogger<DocnetExtractor> logger) => _logger = logger;

    public Task<PdfExtractionResult> ExtractAsync(string pdfFilePath, CancellationToken ct = default)
    {
        var result = new PdfExtractionResult { ExtractorName = Name, FileSizeBytes = new FileInfo(pdfFilePath).Length };
        try
        {
            using var docReader = DocLib.Instance.GetDocReader(pdfFilePath, new PageDimensions(1080, 1920));
            var pageCount = docReader.GetPageCount();

            for (var i = 0; i < pageCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                using var page = docReader.GetPageReader(i);
                var text = page.GetText();
                result.Pages.Add(text ?? "");
            }

            result.Success = true;
            _logger.LogDebug("Docnet 提取完成: {Pages} 页", pageCount);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            _logger.LogWarning(ex, "Docnet 提取失败: {File}", pdfFilePath);
        }
        return Task.FromResult(result);
    }
}
