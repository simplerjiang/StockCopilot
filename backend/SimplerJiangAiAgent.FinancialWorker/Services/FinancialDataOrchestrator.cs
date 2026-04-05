using System.Diagnostics;
using SimplerJiangAiAgent.FinancialWorker.Data;
using SimplerJiangAiAgent.FinancialWorker.Models;
using SimplerJiangAiAgent.FinancialWorker.Services.Pdf;

namespace SimplerJiangAiAgent.FinancialWorker.Services;

/// <summary>
/// 三通道降级编排器：emweb → datacenter → ths，可选 PDF 补充
/// </summary>
public class FinancialDataOrchestrator
{
    private readonly EastmoneyFinanceClient _emweb;
    private readonly EastmoneyDatacenterClient _datacenter;
    private readonly ThsFinanceClient _ths;
    private readonly FinancialDbContext _db;
    private readonly ILogger<FinancialDataOrchestrator> _logger;
    private readonly PdfProcessingPipeline? _pdfPipeline;

    // 数据源优先级：emweb(3) > datacenter(2) > ths(1)
    private static readonly Dictionary<string, int> ChannelPriority = new()
    {
        ["emweb"] = 3,
        ["datacenter"] = 2,
        ["ths"] = 1,
    };

    public FinancialDataOrchestrator(
        EastmoneyFinanceClient emweb,
        EastmoneyDatacenterClient datacenter,
        ThsFinanceClient ths,
        FinancialDbContext db,
        ILogger<FinancialDataOrchestrator> logger,
        PdfProcessingPipeline? pdfPipeline = null)
    {
        _emweb = emweb;
        _datacenter = datacenter;
        _ths = ths;
        _db = db;
        _logger = logger;
        _pdfPipeline = pdfPipeline;
    }

    // ─── 单 symbol 采集 ────────────────────────────────────────────

    public async Task<CollectionResult> CollectAsync(string symbol, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var result = new CollectionResult { Symbol = symbol };

        // --- Channel 1: emweb ---
        try
        {
            var companyType = await _emweb.DetectCompanyTypeAsync(symbol, ct);
            var reports = await _emweb.FetchFinancialReportsAsync(symbol, companyType, ct: ct);

            if (reports.Count > 0)
            {
                var saved = SaveReports(reports, "emweb");

                // Indicators 只有 emweb 才有，失败不影响整体
                var indicatorCount = 0;
                try
                {
                    var indicators = await _emweb.FetchIndicatorsAsync(symbol, ct);
                    indicatorCount = SaveIndicators(indicators);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[{Symbol}] emweb indicators failed, skipping", symbol);
                }

                result.Success = true;
                result.Channel = "emweb";
                result.ReportCount = saved;
                result.IndicatorCount = indicatorCount;
                await CollectExtraDataAsync(symbol, result, ct);
                sw.Stop();
                result.DurationMs = sw.ElapsedMilliseconds;
                WriteLog(result);
                return result;
            }

            // emweb 返回空数据 → 降级
            _logger.LogWarning("[{Symbol}] emweb returned empty data, degrading", symbol);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (ct.IsCancellationRequested) throw;
            _logger.LogWarning(ex, "[{Symbol}] emweb failed: {Msg}, degrading", symbol, ex.Message);
            result.DegradeReason = $"emweb: {ex.GetType().Name}: {ex.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{Symbol}] emweb unexpected error, degrading", symbol);
            result.DegradeReason = $"emweb: {ex.GetType().Name}: {ex.Message}";
        }

        // --- Channel 2: datacenter ---
        try
        {
            var reports = await _datacenter.FetchFinancialReportsAsync(symbol, ct: ct);

            if (reports.Count > 0)
            {
                var saved = SaveReports(reports, "datacenter");

                result.Success = true;
                result.Channel = "datacenter";
                result.IsDegraded = true;
                result.DegradeReason ??= "emweb empty data";
                result.ReportCount = saved;
                await CollectExtraDataAsync(symbol, result, ct);
                sw.Stop();
                result.DurationMs = sw.ElapsedMilliseconds;
                WriteLog(result);
                return result;
            }

            _logger.LogWarning("[{Symbol}] datacenter returned empty data, degrading to ths", symbol);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (ct.IsCancellationRequested) throw;
            _logger.LogWarning(ex, "[{Symbol}] datacenter failed: {Msg}, degrading to ths", symbol, ex.Message);
            result.DegradeReason = $"emweb+datacenter failed; datacenter: {ex.GetType().Name}: {ex.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{Symbol}] datacenter unexpected error, degrading to ths", symbol);
            result.DegradeReason = $"emweb+datacenter failed; datacenter: {ex.GetType().Name}: {ex.Message}";
        }

        // --- Channel 3: ths ---
        try
        {
            var reports = await _ths.FetchFinancialReportsAsync(symbol, ct: ct);

            if (reports.Count > 0)
            {
                var saved = SaveReports(reports, "ths");

                result.Success = true;
                result.Channel = "ths";
                result.IsDegraded = true;
                result.DegradeReason ??= "emweb+datacenter empty data";
                result.ReportCount = saved;
                await CollectExtraDataAsync(symbol, result, ct);
                sw.Stop();
                result.DurationMs = sw.ElapsedMilliseconds;
                WriteLog(result);
                return result;
            }

            _logger.LogWarning("[{Symbol}] ths also returned empty data, all channels exhausted", symbol);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (ct.IsCancellationRequested) throw;
            _logger.LogWarning(ex, "[{Symbol}] ths failed: {Msg}", symbol, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{Symbol}] ths unexpected error", symbol);
        }

        // --- PDF 补充（所有 API 通道均失败时尝试） ---
        await TryPdfSupplementAsync(symbol, result, ct);

        if (!result.Success)
        {
            // 全部失败
            result.Channel ??= "none";
            result.IsDegraded = true;
            result.ErrorMessage ??= "All channels (API + PDF) failed or returned empty data";
            result.DegradeReason ??= "all channels exhausted";
        }

        sw.Stop();
        result.DurationMs = sw.ElapsedMilliseconds;
        WriteLog(result);
        return result;
    }

    private async Task TryPdfSupplementAsync(string symbol, CollectionResult result, CancellationToken ct)
    {
        if (_pdfPipeline == null) return;
        // 仅在 API 报表数不足时启用 PDF 补充
        if (result.ReportCount >= 2) return;

        try
        {
            var pdfResult = await _pdfPipeline.ProcessAsync(symbol, 3, ct);
            if (pdfResult.ParsedCount > 0)
            {
                _logger.LogInformation("[Orchestrator] PDF 补充了 {Count} 份报表: {Symbol}", pdfResult.ParsedCount, symbol);
                result.ReportCount += pdfResult.ParsedCount;
                result.Success = true;
                if (string.IsNullOrEmpty(result.Channel) || result.Channel == "none")
                    result.Channel = "pdf";
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Orchestrator] PDF 管线异常，不影响主流程: {Symbol}", symbol);
        }
    }

    // ─── 批量采集 ──────────────────────────────────────────────────

    public async Task<List<CollectionResult>> CollectBatchAsync(
        IEnumerable<string> symbols, CancellationToken ct = default)
    {
        var results = new List<CollectionResult>();
        var isFirst = true;

        foreach (var symbol in symbols)
        {
            ct.ThrowIfCancellationRequested();

            if (!isFirst)
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(1000, 2000)), ct);
            isFirst = false;

            try
            {
                var r = await CollectAsync(symbol, ct);
                results.Add(r);
                _logger.LogInformation(
                    "[Batch] {Symbol}: {Status} via {Channel} ({Reports} reports, {Ms}ms)",
                    symbol, r.Success ? "OK" : "FAIL", r.Channel, r.ReportCount, r.DurationMs);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Batch] Unexpected error for {Symbol}", symbol);
                results.Add(new CollectionResult
                {
                    Symbol = symbol,
                    Success = false,
                    Channel = "none",
                    ErrorMessage = ex.Message,
                });
            }
        }

        return results;
    }

    // ─── 存储逻辑 ──────────────────────────────────────────────────

    private int SaveReports(List<FinancialReport> reports, string channel)
    {
        var newPriority = ChannelPriority.GetValueOrDefault(channel, 0);
        var savedCount = 0;

        foreach (var report in reports)
        {
            var existing = _db.Reports.FindOne(r =>
                r.Symbol == report.Symbol && r.ReportDate == report.ReportDate);

            if (existing != null)
            {
                var existingPriority = ChannelPriority.GetValueOrDefault(existing.SourceChannel, 0);
                if (newPriority >= existingPriority)
                {
                    existing.BalanceSheet = report.BalanceSheet;
                    existing.IncomeStatement = report.IncomeStatement;
                    existing.CashFlow = report.CashFlow;
                    existing.SourceChannel = report.SourceChannel;
                    existing.CompanyType = report.CompanyType;
                    existing.UpdatedAt = DateTime.UtcNow;
                    _db.Reports.Update(existing);
                    savedCount++;
                }
                // else: 已有更高优先级数据，跳过
            }
            else
            {
                _db.Reports.Insert(report);
                savedCount++;
            }
        }

        return savedCount;
    }

    private int SaveIndicators(List<FinancialIndicator> indicators)
    {
        var savedCount = 0;
        foreach (var ind in indicators)
        {
            var existing = _db.Indicators.FindOne(i =>
                i.Symbol == ind.Symbol && i.ReportDate == ind.ReportDate);

            if (existing != null)
            {
                existing.Metrics = ind.Metrics;
                existing.CollectedAt = DateTime.UtcNow;
                _db.Indicators.Update(existing);
            }
            else
            {
                _db.Indicators.Insert(ind);
            }
            savedCount++;
        }
        return savedCount;
    }

    // ─── 额外数据采集 ──────────────────────────────────────────────────

    private async Task CollectExtraDataAsync(string symbol, CollectionResult result, CancellationToken ct)
    {
        // 分红数据（仅 datacenter 有）
        try
        {
            var dividends = await _datacenter.FetchDividendsAsync(symbol, ct);
            result.DividendCount = SaveDividends(symbol, dividends);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{Symbol}] Dividend collection failed, skipping", symbol);
        }

        // 融资融券数据（仅 datacenter 有）
        try
        {
            var margin = await _datacenter.FetchMarginTradingAsync(symbol, ct: ct);
            result.MarginTradingCount = SaveMarginTrading(symbol, margin);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{Symbol}] Margin trading collection failed, skipping", symbol);
        }
    }

    private int SaveDividends(string symbol, List<DividendRecord> dividends)
    {
        if (dividends.Count == 0) return 0;

        // 按 Symbol 全量替换
        _db.Dividends.DeleteMany(d => d.Symbol == symbol);
        _db.Dividends.InsertBulk(dividends);
        return dividends.Count;
    }

    private int SaveMarginTrading(string symbol, List<MarginTradingRecord> records)
    {
        if (records.Count == 0) return 0;

        var savedCount = 0;
        foreach (var rec in records)
        {
            var existing = _db.MarginTrading.FindOne(m =>
                m.Symbol == rec.Symbol && m.TradeDate == rec.TradeDate);

            if (existing == null)
            {
                _db.MarginTrading.Insert(rec);
                savedCount++;
            }
        }
        return savedCount;
    }

    // ─── 日志 ──────────────────────────────────────────────────────

    private void WriteLog(CollectionResult result)
    {
        _db.Logs.Insert(new CollectionLog
        {
            Symbol = result.Symbol,
            CollectionType = "FinancialReport",
            Channel = result.Channel,
            IsDegraded = result.IsDegraded,
            DegradeReason = result.DegradeReason,
            Success = result.Success,
            ErrorMessage = result.ErrorMessage,
            DurationMs = result.DurationMs,
            RecordCount = result.ReportCount,
        });
    }
}

// ─── 结果模型 ──────────────────────────────────────────────────────

public class CollectionResult
{
    public string Symbol { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Channel { get; set; } = string.Empty;
    public bool IsDegraded { get; set; }
    public string? DegradeReason { get; set; }
    public int ReportCount { get; set; }
    public int IndicatorCount { get; set; }
    public int DividendCount { get; set; }
    public int MarginTradingCount { get; set; }
    public long DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
}
