using SimplerJiangAiAgent.FinancialWorker.Data;
using SimplerJiangAiAgent.FinancialWorker.Models;
using Microsoft.Extensions.Logging;

namespace SimplerJiangAiAgent.FinancialWorker.Services.Pdf;

/// <summary>
/// PDF 完整处理管线: 下载 → 提取 → 投票 → 解析 → 存储
/// </summary>
public class PdfProcessingPipeline
{
    private readonly CninfoClient _cninfoClient;
    private readonly PdfVotingEngine _votingEngine;
    private readonly FinancialTableParser _tableParser;
    private readonly FinancialDbContext _db;
    private readonly ILogger<PdfProcessingPipeline> _logger;

    public PdfProcessingPipeline(
        CninfoClient cninfoClient,
        PdfVotingEngine votingEngine,
        FinancialTableParser tableParser,
        FinancialDbContext db,
        ILogger<PdfProcessingPipeline> logger)
    {
        _cninfoClient = cninfoClient;
        _votingEngine = votingEngine;
        _tableParser = tableParser;
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// 处理单个股票的 PDF 报表
    /// </summary>
    public async Task<PdfPipelineResult> ProcessAsync(string symbol, int maxReports = 3, CancellationToken ct = default)
    {
        var result = new PdfPipelineResult { Symbol = symbol };
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("[PDF] 开始处理 {Symbol}, 最多 {Max} 份报表", symbol, maxReports);
            var downloads = await _cninfoClient.DownloadRecentReportsAsync(symbol, maxReports, ct);
            result.DownloadedCount = downloads.Count;

            if (downloads.Count == 0)
            {
                result.Notes = "cninfo 未找到可下载的 PDF 公告";
                _logger.LogWarning("[PDF] {Symbol} 无可下载PDF", symbol);
                return result;
            }

            foreach (var pdf in downloads)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var processResult = await ProcessSinglePdfAsync(symbol, pdf, ct);
                    if (processResult.Success)
                        result.ParsedCount++;
                    result.FileResults.Add(processResult);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[PDF] 处理文件失败: {File}", pdf.FilePath);
                    result.FileResults.Add(new PdfFileResult
                    {
                        FileName = Path.GetFileName(pdf.FilePath),
                        Success = false,
                        Error = ex.Message
                    });
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            result.Notes = $"PDF 管线异常: {ex.Message}";
            _logger.LogError(ex, "[PDF] {Symbol} 管线异常", symbol);
        }
        finally
        {
            sw.Stop();
            result.DurationMs = sw.ElapsedMilliseconds;

            _db.Logs.Insert(new CollectionLog
            {
                Symbol = symbol,
                CollectionType = "Pdf",
                Channel = "cninfo-pdf",
                Success = result.ParsedCount > 0,
                RecordCount = result.ParsedCount,
                DurationMs = result.DurationMs,
                ErrorMessage = result.Notes,
            });
        }

        return result;
    }

    private async Task<PdfFileResult> ProcessSinglePdfAsync(string symbol, DownloadedPdf pdf, CancellationToken ct)
    {
        var fileName = Path.GetFileName(pdf.FilePath);
        _logger.LogDebug("[PDF] 正在处理: {File}", fileName);

        // 三路提取 + 投票
        var votingResult = await _votingEngine.ExtractAndVoteAsync(pdf.FilePath, ct);
        if (votingResult.Winner == null)
        {
            return new PdfFileResult
            {
                FileName = fileName,
                Success = false,
                Error = "三路提取均失败",
                VotingConfidence = votingResult.Confidence.ToString()
            };
        }

        // 解析财务表格
        var parsed = _tableParser.Parse(votingResult.Winner);
        if (!parsed.HasData)
        {
            return new PdfFileResult
            {
                FileName = fileName,
                Success = false,
                Error = "PDF 文本中未找到可解析的财务数据",
                VotingConfidence = votingResult.Confidence.ToString(),
                ExtractorUsed = votingResult.Winner.ExtractorName
            };
        }

        // 存储到 LiteDB (priority=0，低于 API 数据)
        var reportDate = parsed.ReportDate ?? pdf.Announcement.PublishTime.ToString("yyyy-MM-dd");
        var report = new FinancialReport
        {
            Symbol = symbol,
            ReportDate = reportDate,
            ReportType = parsed.ReportType,
            BalanceSheet = parsed.BalanceSheet,
            IncomeStatement = parsed.IncomeStatement,
            CashFlow = parsed.CashFlowStatement,
            SourceChannel = "pdf",
            CollectedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        SaveIfNoBetterData(report);

        return new PdfFileResult
        {
            FileName = fileName,
            Success = true,
            ReportDate = reportDate,
            ReportType = parsed.ReportType,
            VotingConfidence = votingResult.Confidence.ToString(),
            ExtractorUsed = votingResult.Winner.ExtractorName,
            FieldCount = parsed.BalanceSheet.Count + parsed.IncomeStatement.Count + parsed.CashFlowStatement.Count
        };
    }

    private void SaveIfNoBetterData(FinancialReport report)
    {
        var existing = _db.Reports.FindOne(r => r.Symbol == report.Symbol && r.ReportDate == report.ReportDate);

        if (existing != null)
        {
            // PDF priority=0, API sources have higher priority (emweb=3, datacenter=2, ths=1)
            if (existing.SourceChannel != "pdf")
            {
                _logger.LogDebug("已有更高优先级数据 ({Source})，跳过 PDF 写入: {Symbol} {Date}",
                    existing.SourceChannel, report.Symbol, report.ReportDate);
                return;
            }

            existing.BalanceSheet = report.BalanceSheet;
            existing.IncomeStatement = report.IncomeStatement;
            existing.CashFlow = report.CashFlow;
            existing.UpdatedAt = DateTime.UtcNow;
            _db.Reports.Update(existing);
        }
        else
        {
            _db.Reports.Insert(report);
        }
    }
}

public class PdfPipelineResult
{
    public string Symbol { get; set; } = "";
    public int DownloadedCount { get; set; }
    public int ParsedCount { get; set; }
    public long DurationMs { get; set; }
    public string? Notes { get; set; }
    public List<PdfFileResult> FileResults { get; set; } = new();
}

public class PdfFileResult
{
    public string FileName { get; set; } = "";
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? ReportDate { get; set; }
    public string? ReportType { get; set; }
    public string? VotingConfidence { get; set; }
    public string? ExtractorUsed { get; set; }
    public int FieldCount { get; set; }
}
