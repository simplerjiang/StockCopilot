using System.Diagnostics;
using LiteDB;
using SimplerJiangAiAgent.FinancialWorker.Data;
using SimplerJiangAiAgent.FinancialWorker.Models;
using SimplerJiangAiAgent.FinancialWorker.Services.Rag;
using Microsoft.Extensions.Logging;

namespace SimplerJiangAiAgent.FinancialWorker.Services.Pdf;

/// <summary>
/// PDF 完整处理管线: 下载 → 提取 → 投票 → 解析 → 存储。
/// v0.4.1 §S2：每次处理都会构造 5 阶段（download/extract/vote/parse/persist）<see cref="PdfStageLog"/> 并整体覆盖到 <see cref="PdfFileDocument.StageLogs"/>。
/// </summary>
public class PdfProcessingPipeline : IPdfProcessingPipeline
{
    private const string StageDownload = "download";
    private const string StageExtract = "extract";
    private const string StageVote = "vote";
    private const string StageParse = "parse";
    private const string StagePersist = "persist";

    private const string StatusSuccess = "success";
    private const string StatusFailed = "failed";
    private const string StatusSkipped = "skipped";

    private readonly CninfoClient _cninfoClient;
    private readonly PdfVotingEngine _votingEngine;
    private readonly FinancialTableParser _tableParser;
    private readonly FinancialDbContext _db;
    private readonly ILogger<PdfProcessingPipeline> _logger;
    private readonly RagDbContext _ragDb;
    private readonly IChunker _chunker;
    private readonly IChineseTokenizer _tokenizer;
    private readonly IEmbedder _embedder;

    public PdfProcessingPipeline(
        CninfoClient cninfoClient,
        PdfVotingEngine votingEngine,
        FinancialTableParser tableParser,
        FinancialDbContext db,
        ILogger<PdfProcessingPipeline> logger,
        RagDbContext ragDb,
        IChunker chunker,
        IChineseTokenizer tokenizer,
        IEmbedder embedder)
    {
        _cninfoClient = cninfoClient;
        _votingEngine = votingEngine;
        _tableParser = tableParser;
        _db = db;
        _logger = logger;
        _ragDb = ragDb;
        _chunker = chunker;
        _tokenizer = tokenizer;
        _embedder = embedder;
    }

    /// <summary>
    /// 处理单个股票的 PDF 报表
    /// </summary>
    public async Task<PdfPipelineResult> ProcessAsync(string symbol, int maxReports = 3, CancellationToken ct = default)
    {
        var result = new PdfPipelineResult { Symbol = symbol };
        var sw = Stopwatch.StartNew();

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

    /// <inheritdoc />
    public Task<PdfReparseOutcome> ReparseAsync(string id, CancellationToken ct = default)
    {
        var outcome = new PdfReparseOutcome();
        if (string.IsNullOrWhiteSpace(id))
        {
            outcome.DocumentFound = false;
            outcome.Error = "id 不能为空";
            return Task.FromResult(outcome);
        }

        ObjectId objectId;
        try
        {
            objectId = new ObjectId(id);
        }
        catch
        {
            outcome.DocumentFound = false;
            outcome.Error = "id 不是合法的 LiteDB ObjectId";
            return Task.FromResult(outcome);
        }

        var doc = _db.PdfFiles.FindById(objectId);
        if (doc == null)
        {
            outcome.DocumentFound = false;
            return Task.FromResult(outcome);
        }

        outcome.DocumentFound = true;
        outcome.Symbol = doc.Symbol;
        outcome.FileName = doc.FileName;
        outcome.LocalPath = doc.LocalPath;

        if (string.IsNullOrWhiteSpace(doc.LocalPath) || !File.Exists(doc.LocalPath))
        {
            outcome.PhysicalFileMissing = true;
            outcome.Success = false;
            outcome.Error = "PDF 物理文件丢失";

            // 仍然刷新 stageLogs（download=failed），让前端能看到失败原因
            var now = DateTime.UtcNow;
            var stageLogs = new List<PdfStageLog>
            {
                new() { Stage = StageDownload, Status = StatusFailed, ElapsedMs = 0, Message = "本地 PDF 文件丢失", Timestamp = now },
                new() { Stage = StageExtract, Status = StatusSkipped, ElapsedMs = 0, Message = null, Timestamp = now },
                new() { Stage = StageVote, Status = StatusSkipped, ElapsedMs = 0, Message = null, Timestamp = now },
                new() { Stage = StageParse, Status = StatusSkipped, ElapsedMs = 0, Message = null, Timestamp = now },
                new() { Stage = StagePersist, Status = StatusSkipped, ElapsedMs = 0, Message = null, Timestamp = now },
            };
            doc.LastReparsedAt = now;
            doc.LastError = outcome.Error;
            doc.StageLogs = stageLogs;
            try { _db.PdfFiles.Update(doc); }
            catch (Exception ex) { _logger.LogError(ex, "[PDF] 物理文件丢失场景下回写 PdfFileDocument 失败: {Id}", id); }
            return Task.FromResult(outcome);
        }

        return ReparseInternalAsync(doc, outcome, ct);
    }

    private async Task<PdfReparseOutcome> ReparseInternalAsync(PdfFileDocument doc, PdfReparseOutcome outcome, CancellationToken ct)
    {
        var pdf = new DownloadedPdf
        {
            FilePath = doc.LocalPath,
            Symbol = doc.Symbol,
            Announcement = new CninfoAnnouncement
            {
                Title = doc.Title ?? string.Empty,
                PublishTime = TryParseReportPeriod(doc.ReportPeriod) ?? DateTime.UtcNow,
            }
        };

        try
        {
            var fileResult = await ProcessSinglePdfAsync(doc.Symbol, pdf, ct);
            outcome.Success = fileResult.Success;
            outcome.Error = fileResult.Error;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // 兜底：ProcessSinglePdfAsync 已经在每阶段 try-catch 不应抛出，但留个保险
            _logger.LogError(ex, "[PDF] Reparse 主流程异常: {Id}", doc.Id);
            outcome.Success = false;
            outcome.Error = ex.Message;
        }

        return outcome;
    }

    private static DateTime? TryParseReportPeriod(string? reportPeriod)
    {
        if (string.IsNullOrWhiteSpace(reportPeriod)) return null;
        return DateTime.TryParse(reportPeriod, out var dt) ? dt : null;
    }

    internal async Task<PdfFileResult> ProcessSinglePdfAsync(string symbol, DownloadedPdf pdf, CancellationToken ct)
    {
        var fileName = Path.GetFileName(pdf.FilePath);
        _logger.LogDebug("[PDF] 正在处理: {File}", fileName);

        var stageLogs = new List<PdfStageLog>();
        PdfVotingResult? votingResult = null;
        ParsedFinancialStatements? parsed = null;
        List<PdfParseUnit> parseUnits = new();
        List<PdfPageText> fullTextPages = new();
        PdfFileResult outcome;

        // ── Stage 1: download ──
        // 在 ProcessSinglePdfAsync 进入时，PDF 已位于本地（DownloadRecentReportsAsync 或 ReparseAsync 提供）。
        // 此阶段做存在性校验，并标记 status；缺失则后续阶段 skipped。
        var downloadOk = TryRunStage(stageLogs, StageDownload, () =>
        {
            if (string.IsNullOrWhiteSpace(pdf.FilePath) || !File.Exists(pdf.FilePath))
                throw new FileNotFoundException("PDF 物理文件不存在", pdf.FilePath);
            return "已存在本地";
        });

        if (downloadOk)
        {
            AddStageDetails(stageLogs, StageDownload, new Dictionary<string, string>
            {
                ["filePath"] = pdf.FilePath ?? "",
                ["fileSize"] = File.Exists(pdf.FilePath) ? new FileInfo(pdf.FilePath!).Length.ToString() : "0"
            });
        }

        if (!downloadOk)
        {
            AppendSkipped(stageLogs, StageExtract, StageVote, StageParse, StagePersist);
            outcome = new PdfFileResult
            {
                FileName = fileName,
                Success = false,
                Error = "PDF 物理文件不存在",
            };
            UpsertPdfFileDocument(symbol, pdf, votingResult, parsed, parseUnits, fullTextPages, outcome, stageLogs);
            return outcome;
        }

        // After stage 1 download succeeds, immediately persist a stub record
        // so frontend can see the PDF file even if later stages fail.
        UpsertPdfFileDocumentStub(symbol, pdf, stageLogs.ToList());

        // ── Stage 2: extract（含三路提取，时间归到此阶段） ──
        var extractOk = await TryRunStageAsync(stageLogs, StageExtract, async () =>
        {
            votingResult = await _votingEngine.ExtractAndVoteAsync(pdf.FilePath, ct);
            if (votingResult.Winner == null)
                throw new InvalidOperationException("三路提取均失败");
            return $"extractor={votingResult.Winner.ExtractorName}";
        });

        if (votingResult != null)
        {
            var extractDetails = new Dictionary<string, string>();
            if (votingResult.AllExtractions != null)
            {
                foreach (var ext in votingResult.AllExtractions)
                {
                    var key = ext.ExtractorName;
                    extractDetails[$"{key}.success"] = ext.Success.ToString();
                    extractDetails[$"{key}.pages"] = ext.PageCount.ToString();
                }
            }
            AddStageDetails(stageLogs, StageExtract, extractDetails);
        }

        if (!extractOk || votingResult == null || votingResult.Winner == null)
        {
            AppendSkipped(stageLogs, StageVote, StageParse, StagePersist);
            outcome = new PdfFileResult
            {
                FileName = fileName,
                Success = false,
                Error = "三路提取均失败",
                VotingConfidence = votingResult?.Confidence.ToString(),
            };
            UpsertPdfFileDocument(symbol, pdf, votingResult, parsed, parseUnits, fullTextPages, outcome, stageLogs);
            return outcome;
        }

        // ── Stage 3: vote（投票决策已在 ExtractAndVoteAsync 中完成，这里只记录结果） ──
        TryRunStage(stageLogs, StageVote, () => $"confidence={votingResult.Confidence}");

        if (votingResult != null)
        {
            AddStageDetails(stageLogs, StageVote, new Dictionary<string, string>
            {
                ["winner"] = votingResult.Winner?.ExtractorName ?? "none",
                ["confidence"] = votingResult.Confidence.ToString(),
                ["notes"] = votingResult.Notes ?? ""
            });
        }

        // v0.4.2 NS1：投票完成后，捕获获胜提取器的每页全文，供 RAG/LLM 使用。
        if (votingResult.Winner?.Pages != null)
        {
            for (int i = 0; i < votingResult.Winner.Pages.Count; i++)
            {
                fullTextPages.Add(new PdfPageText
                {
                    PageNumber = i + 1,
                    Text = votingResult.Winner.Pages[i]
                });
            }
        }

        // ── Stage 4: parse ──
        var parseOk = TryRunStage(stageLogs, StageParse, () =>
        {
            parsed = _tableParser.Parse(votingResult.Winner!);
            if (!parsed.HasData)
                throw new InvalidOperationException("PDF 文本中未找到可解析的财务数据");
            return $"fields={parsed.BalanceSheet.Count + parsed.IncomeStatement.Count + parsed.CashFlowStatement.Count}";
        });

        if (parsed != null)
        {
            AddStageDetails(stageLogs, StageParse, new Dictionary<string, string>
            {
                ["balanceSheet"] = parsed.BalanceSheet.Count.ToString(),
                ["incomeStatement"] = parsed.IncomeStatement.Count.ToString(),
                ["cashFlow"] = parsed.CashFlowStatement.Count.ToString(),
                ["reportDate"] = parsed.ReportDate ?? "",
                ["reportType"] = parsed.ReportType ?? ""
            });
        }

        if (!parseOk || parsed == null || !parsed.HasData)
        {
            AppendSkipped(stageLogs, StagePersist);
            outcome = new PdfFileResult
            {
                FileName = fileName,
                Success = false,
                Error = "PDF 文本中未找到可解析的财务数据",
                VotingConfidence = votingResult.Confidence.ToString(),
                ExtractorUsed = votingResult.Winner!.ExtractorName,
            };
            UpsertPdfFileDocument(symbol, pdf, votingResult, parsed, parseUnits, fullTextPages, outcome, stageLogs);
            return outcome;
        }

        // ── Stage 5: persist（写 financial_reports；pdf_files 在 finally 落） ──
        var reportDate = parsed.ReportDate ?? pdf.Announcement.PublishTime.ToString("yyyy-MM-dd");

        var persistOk = TryRunStage(stageLogs, StagePersist, () =>
        {
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
            return "saved";
        });

        outcome = new PdfFileResult
        {
            FileName = fileName,
            Success = persistOk,
            Error = persistOk ? null : "财报数据落库失败",
            ReportDate = reportDate,
            ReportType = parsed.ReportType,
            VotingConfidence = votingResult.Confidence.ToString(),
            ExtractorUsed = votingResult.Winner!.ExtractorName,
            FieldCount = parsed.BalanceSheet.Count + parsed.IncomeStatement.Count + parsed.CashFlowStatement.Count
        };

        // v0.4.1 §5.1 + §9.1：构造解析单元并落 pdf_files 集合（含 page_start / page_end / block_kind）。
        parseUnits = PdfParseUnitBuilder.Build(votingResult.Winner!, parsed);
        UpsertPdfFileDocument(symbol, pdf, votingResult, parsed, parseUnits, fullTextPages, outcome, stageLogs);

        // v0.4.2 S4: Auto-chunk and store in RAG database
        try
        {
            var pdfDoc = _db.PdfFiles.FindOne(x => x.Symbol == symbol && x.LocalPath == (pdf.FilePath ?? ""));
            if (pdfDoc != null)
            {
                await ChunkAndStoreInRag(pdfDoc);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PDF][RAG] 切块入库失败: {File}", pdf.FilePath);
        }

        return outcome;
    }

    private bool TryRunStage(List<PdfStageLog> logs, string stage, Func<string?> action)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var msg = action();
            sw.Stop();
            logs.Add(new PdfStageLog { Stage = stage, Status = StatusSuccess, ElapsedMs = sw.ElapsedMilliseconds, Message = msg, Timestamp = DateTime.UtcNow });
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            sw.Stop();
            logs.Add(new PdfStageLog { Stage = stage, Status = StatusFailed, ElapsedMs = sw.ElapsedMilliseconds, Message = ex.Message, Timestamp = DateTime.UtcNow });
            _logger.LogWarning(ex, "[PDF] 阶段 {Stage} 失败", stage);
            return false;
        }
    }

    private async Task<bool> TryRunStageAsync(List<PdfStageLog> logs, string stage, Func<Task<string?>> action)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var msg = await action();
            sw.Stop();
            logs.Add(new PdfStageLog { Stage = stage, Status = StatusSuccess, ElapsedMs = sw.ElapsedMilliseconds, Message = msg, Timestamp = DateTime.UtcNow });
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            sw.Stop();
            logs.Add(new PdfStageLog { Stage = stage, Status = StatusFailed, ElapsedMs = sw.ElapsedMilliseconds, Message = ex.Message, Timestamp = DateTime.UtcNow });
            _logger.LogWarning(ex, "[PDF] 阶段 {Stage} 失败", stage);
            return false;
        }
    }

    private static void AddStageDetails(List<PdfStageLog> logs, string stage, Dictionary<string, string> details)
    {
        var log = logs.FindLast(l => l.Stage == stage);
        if (log != null)
            log.Details = details;
    }

    private static void AppendSkipped(List<PdfStageLog> logs, params string[] stages)
    {
        var now = DateTime.UtcNow;
        foreach (var s in stages)
        {
            logs.Add(new PdfStageLog { Stage = s, Status = StatusSkipped, ElapsedMs = 0, Message = null, Timestamp = now });
        }
    }

    /// <summary>
    /// v0.4.2 N2：按 PDF 文件名 / 公告标题里的中文关键词推断 ReportType。
    /// 比依赖 parsed.ReportDate 的 InferReportType 更可靠，因为年报正文里
    /// 经常出现 Q1 比较列日期会污染日期推断。返回 null 表示无关键词命中，
    /// 让上层回退到 outcome / parsed / "Unknown"。
    /// </summary>
    public static string? InferReportTypeFromFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        var name = fileName;
        // 注意顺序：先匹配更长更具体的关键词。
        // 「半年度报告」字面包含「年度报告」、「半年报」字面包含「年报」，
        // 所以必须先判 Semi 再判 Annual，否则半年报会被误判为 Annual。
        // 季度同理放在 Annual 之前，避免「年度报告」误命中季度文件名。
        if (name.Contains("半年度报告", StringComparison.Ordinal)
            || name.Contains("半年报", StringComparison.Ordinal)
            || name.Contains("中期报告", StringComparison.Ordinal)
            || name.Contains("Semi-Annual", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Half-Year", StringComparison.OrdinalIgnoreCase))
        {
            return "Semi";
        }
        if (name.Contains("第三季度", StringComparison.Ordinal)
            || name.Contains("三季度报告", StringComparison.Ordinal)
            || name.Contains("三季报", StringComparison.Ordinal))
        {
            return "Q3";
        }
        if (name.Contains("第一季度", StringComparison.Ordinal)
            || name.Contains("一季度报告", StringComparison.Ordinal)
            || name.Contains("一季报", StringComparison.Ordinal))
        {
            return "Q1";
        }
        if (name.Contains("年度报告", StringComparison.Ordinal)
            || name.Contains("年报", StringComparison.Ordinal)
            || name.IndexOf("Annual", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Annual";
        }
        return null;
    }

    /// <summary>
    /// v0.4.2 NS3：下载完成后立即写入一条 stub 记录到 pdf_files 集合，
    /// 使得即使后续阶段失败，前端仍能看到 PDF 文件条目。
    /// 若该 Symbol + LocalPath 已有完整记录（如之前全阶段跑完），不覆盖。
    /// </summary>
    private void UpsertPdfFileDocumentStub(string symbol, DownloadedPdf pdf, List<PdfStageLog> stageLogs)
    {
        try
        {
            var localPath = pdf.FilePath ?? string.Empty;
            var existing = _db.PdfFiles.FindOne(x => x.Symbol == symbol && x.LocalPath == localPath);
            if (existing != null)
                return; // Already has a record, don't overwrite with stub

            var fileName = Path.GetFileName(localPath);
            var accessKey = PdfAccessKey.From(localPath);

            var reportPeriod = pdf.Announcement.PublishTime.ToString("yyyy-MM-dd");
            var fileNameType = InferReportTypeFromFileName(fileName)
                ?? InferReportTypeFromFileName(pdf.Announcement.Title);
            var reportType = fileNameType ?? "Unknown";

            var doc = new PdfFileDocument
            {
                Symbol = symbol,
                FileName = fileName,
                Title = pdf.Announcement.Title ?? string.Empty,
                LocalPath = localPath,
                AccessKey = accessKey,
                ReportPeriod = reportPeriod,
                ReportType = reportType,
                Extractor = null,
                VoteConfidence = null,
                FieldCount = 0,
                LastError = null,
                LastParsedAt = DateTime.UtcNow,
                LastReparsedAt = null,
                ParseUnits = new List<PdfParseUnit>(),
                FullTextPages = new List<PdfPageText>(),
                StageLogs = stageLogs,
                VotingCandidates = new List<VotingCandidate>(),
            };
            _db.PdfFiles.Insert(doc);
            _logger.LogInformation("PDF stub record persisted immediately after download: {Symbol}/{FileName}", symbol, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PDF] pdf_files stub 写入失败: {File}", pdf.FilePath);
        }
    }

    /// <summary>
    /// v0.4.1 §5.1：将 PDF 详情持久化到 pdf_files 集合。
    /// 同一份 PDF（按 Symbol + LocalPath 唯一）已存在时刷新 LastReparsedAt 与解析快照。
    /// AccessKey 始终通过 <see cref="PdfAccessKey.From"/> 重算（v0.4.1 §S2 最小迁移）。
    /// stageLogs 整体覆盖（不累加历史）。
    /// </summary>
    private void UpsertPdfFileDocument(
        string symbol,
        DownloadedPdf pdf,
        PdfVotingResult? voting,
        ParsedFinancialStatements? parsed,
        List<PdfParseUnit> parseUnits,
        List<PdfPageText> fullTextPages,
        PdfFileResult outcome,
        List<PdfStageLog> stageLogs)
    {
        try
        {
            // v0.4.2 NS5: Build voting candidates
            var votingCandidates = new List<VotingCandidate>();
            if (voting?.AllExtractions != null)
            {
                var winnerName = voting.Winner?.ExtractorName;
                foreach (var ext in voting.AllExtractions)
                {
                    votingCandidates.Add(new VotingCandidate
                    {
                        Extractor = ext.ExtractorName,
                        Success = ext.Success,
                        PageCount = ext.PageCount,
                        TextLength = ext.Success ? ext.FullText.Length : 0,
                        SampleText = ext.Success && ext.FullText.Length > 0
                            ? ext.FullText.Substring(0, Math.Min(200, ext.FullText.Length))
                            : null,
                        IsWinner = ext.ExtractorName == winnerName,
                    });
                }
            }

            var fileName = Path.GetFileName(pdf.FilePath);
            var localPath = pdf.FilePath ?? string.Empty;
            var accessKey = PdfAccessKey.From(localPath);
            var existing = _db.PdfFiles.FindOne(x => x.Symbol == symbol && x.LocalPath == localPath);
            var now = DateTime.UtcNow;

            var fieldCount = outcome.FieldCount;
            var reportPeriod = outcome.ReportDate
                ?? parsed?.ReportDate
                ?? pdf.Announcement.PublishTime.ToString("yyyy-MM-dd");
            // v0.4.2 N2 修复：parsed.ReportType 依赖 PDF 文本中第一个匹配的日期，
            // 年报里出现 Q1 比较列时会被错判为 "Q1"。文件名里的「年度报告/年报/半年报/
            // 三季报/一季报」是更可靠的信号，作为最高优先级覆盖。
            var fileNameType = InferReportTypeFromFileName(fileName)
                ?? InferReportTypeFromFileName(pdf.Announcement.Title);
            var reportType = fileNameType
                ?? outcome.ReportType
                ?? parsed?.ReportType
                ?? "Unknown";

            if (existing == null)
            {
                var doc = new PdfFileDocument
                {
                    Symbol = symbol,
                    FileName = fileName,
                    Title = pdf.Announcement.Title ?? string.Empty,
                    LocalPath = localPath,
                    AccessKey = accessKey,
                    ReportPeriod = reportPeriod,
                    ReportType = reportType,
                    Extractor = outcome.ExtractorUsed ?? voting?.Winner?.ExtractorName,
                    VoteConfidence = outcome.VotingConfidence ?? voting?.Confidence.ToString(),
                    FieldCount = fieldCount,
                    LastError = outcome.Success ? null : outcome.Error,
                    LastParsedAt = now,
                    LastReparsedAt = null,
                    ParseUnits = parseUnits ?? new List<PdfParseUnit>(),
                    FullTextPages = fullTextPages ?? new List<PdfPageText>(),
                    StageLogs = stageLogs ?? new List<PdfStageLog>(),
                    VotingCandidates = votingCandidates,
                    VotingNotes = voting?.Notes,
                };
                _db.PdfFiles.Insert(doc);
            }
            else
            {
                existing.FileName = fileName;
                existing.Title = pdf.Announcement.Title ?? existing.Title;
                existing.AccessKey = accessKey; // v0.4.1 §S2：每次写入按新算法刷新
                existing.ReportPeriod = reportPeriod;
                existing.ReportType = reportType;
                existing.Extractor = outcome.ExtractorUsed ?? voting?.Winner?.ExtractorName ?? existing.Extractor;
                existing.VoteConfidence = outcome.VotingConfidence ?? voting?.Confidence.ToString() ?? existing.VoteConfidence;
                existing.FieldCount = fieldCount;
                existing.LastError = outcome.Success ? null : outcome.Error;
                existing.LastReparsedAt = now;
                existing.ParseUnits = parseUnits ?? existing.ParseUnits;
                existing.FullTextPages = fullTextPages ?? existing.FullTextPages;
                existing.StageLogs = stageLogs ?? new List<PdfStageLog>(); // 整体覆盖
                existing.VotingCandidates = votingCandidates;
                existing.VotingNotes = voting?.Notes;
                _db.PdfFiles.Update(existing);
            }
        }
        catch (Exception ex)
        {
            // 不破坏主流程：落库失败仅记日志。
            _logger.LogError(ex, "[PDF] pdf_files 集合写入失败: {File}", pdf.FilePath);
        }
    }


    /// <summary>
    /// v0.4.2 S4: Chunk a PDF document and store in RAG database.
    /// Deletes existing chunks for this source before inserting new ones.
    /// </summary>
    private async Task ChunkAndStoreInRag(PdfFileDocument doc)
    {
        var sourceId = doc.Id.ToString();

        // Delete old embeddings + chunks for this document (supports reparse)
        _ragDb.DeleteEmbeddingsBySourceId(sourceId);
        _ragDb.DeleteChunksBySourceId(sourceId);

        // Chunk the document
        var chunks = _chunker.Chunk(doc);
        if (chunks.Count == 0)
        {
            _logger.LogDebug("[PDF][RAG] 文档无可切块内容: {Symbol} {File}", doc.Symbol, doc.FileName);
            return;
        }

        // Tokenize each chunk's text for FTS5
        foreach (var chunk in chunks)
        {
            chunk.TokenizedText = _tokenizer.Tokenize(chunk.Text);
        }

        // Bulk insert
        _ragDb.InsertChunks(chunks);
        _logger.LogInformation("[PDF][RAG] 已切块入库: {Symbol} {File} → {Count} chunks",
            doc.Symbol, doc.FileName, chunks.Count);

        // v0.4.3 S3: Generate embeddings if embedder is available
        if (_embedder.IsAvailable)
        {
            try
            {
                var embeddings = new List<(string ChunkId, float[] Embedding, string ModelName)>();
                foreach (var chunk in chunks)
                {
                    var embedding = await _embedder.EmbedAsync(chunk.Text);
                    if (embedding != null)
                    {
                        embeddings.Add((chunk.ChunkId, embedding, "ollama"));
                    }
                }
                if (embeddings.Count > 0)
                {
                    _ragDb.UpsertEmbeddings(embeddings);
                    _logger.LogInformation("[PDF][RAG] 已生成向量: {Symbol} {File} → {Count} embeddings",
                        doc.Symbol, doc.FileName, embeddings.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[PDF][RAG] 向量生成失败（不影响 BM25 索引）: {File}", doc.FileName);
            }
        }
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
