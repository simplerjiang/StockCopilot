using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SimplerJiangAiAgent.FinancialWorker.Data;
using SimplerJiangAiAgent.FinancialWorker.Models;
using SimplerJiangAiAgent.FinancialWorker.Services;
using SimplerJiangAiAgent.FinancialWorker.Services.Pdf;

namespace SimplerJiangAiAgent.FinancialWorker.Tests;

/// <summary>
/// V040-S2 Cycle A: 4-path coverage for FinancialDataOrchestrator covering
/// success / degraded / fail / pdf-supplement, plus assertion that the new
/// CollectionLog fields are persisted.
/// </summary>
[Collection("LiteDbBsonMapper")] // 与 PdfFileDocumentTests 共享集合：避免 BsonMapper.Global 并发污染
public class FinancialDataOrchestratorPathTests : IDisposable
{
    private const string Symbol = "600519";

    private readonly string _dbPath;
    private readonly FinancialDbContext _db;
    private readonly Mock<IEastmoneyFinanceClient> _emweb = new(MockBehavior.Strict);
    private readonly Mock<IEastmoneyDatacenterClient> _datacenter = new(MockBehavior.Strict);
    private readonly Mock<IThsFinanceClient> _ths = new(MockBehavior.Strict);

    public FinancialDataOrchestratorPathTests()
    {
        _dbPath = Path.Combine(
            Path.GetTempPath(),
            $"financial-worker-tests-{Guid.NewGuid():N}.db");
        _db = new FinancialDbContext($"Filename={_dbPath};Connection=direct");
    }

    public void Dispose()
    {
        _db.Dispose();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best-effort cleanup */ }
        var log = Path.ChangeExtension(_dbPath, "-log.db");
        try { if (File.Exists(log)) File.Delete(log); } catch { /* best-effort */ }
    }

    private FinancialDataOrchestrator BuildOrchestrator(IPdfProcessingPipeline? pipeline = null)
    {
        // Datacenter dividends/margin trading are called only on the success path's CollectExtraDataAsync.
        // Provide loose default returns so strict mocks do not blow up when invoked.
        _datacenter.Setup(x => x.FetchDividendsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DividendRecord>());
        _datacenter.Setup(x => x.FetchMarginTradingAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarginTradingRecord>());

        return new FinancialDataOrchestrator(
            _emweb.Object,
            _datacenter.Object,
            _ths.Object,
            _db,
            NullLogger<FinancialDataOrchestrator>.Instance,
            pipeline);
    }

    private static FinancialReport MakeReport(string date, string channel) => new()
    {
        Symbol = Symbol,
        ReportDate = date,
        ReportType = "Annual",
        CompanyType = 4,
        SourceChannel = channel,
        BalanceSheet = new() { ["TOTAL_ASSETS"] = 1000m },
        IncomeStatement = new() { ["TOTAL_OPERATE_INCOME"] = 500m },
        CashFlow = new() { ["NETCASH_OPERATE"] = 200m },
    };

    private CollectionLog GetLatestLog() =>
        _db.Logs.FindAll().OrderByDescending(l => l.Timestamp).First();

    // ────────────────────────────────────────────────────────────────────
    // Path 1: emweb success
    // ────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task CollectAsync_EmwebSuccess_PopulatesMainSourceAndPersistsLog()
    {
        _emweb.Setup(x => x.DetectCompanyTypeAsync(Symbol, It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);
        _emweb.Setup(x => x.FetchFinancialReportsAsync(Symbol, 4, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FinancialReport>
            {
                MakeReport("2025-12-31", "emweb"),
                MakeReport("2025-09-30", "emweb"),
            });
        _emweb.Setup(x => x.FetchIndicatorsAsync(Symbol, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FinancialIndicator>());

        var orch = BuildOrchestrator();

        var result = await orch.CollectAsync(Symbol);

        Assert.True(result.Success);
        Assert.Equal("emweb", result.Channel);
        Assert.Equal("emweb", result.MainSourceChannel);
        Assert.Empty(result.FallbackChannels);
        Assert.Null(result.PdfSummarySupplement);
        Assert.False(result.IsDegraded);
        Assert.Equal(2, result.ReportCount);
        Assert.Contains("2025-12-31", result.ReportPeriods);
        Assert.NotEmpty(result.ReportTitles);

        var log = GetLatestLog();
        Assert.Equal("emweb", log.MainSourceChannel);
        Assert.Empty(log.FallbackChannels);
        Assert.Null(log.PdfSummarySupplement);
        Assert.False(log.IsDegraded);
        Assert.Equal(result.ReportPeriods, log.ReportPeriods);
        Assert.Equal(result.ReportTitles, log.ReportTitles);
        Assert.Equal(result.Warnings, log.Warnings);

        _datacenter.Verify(x => x.FetchFinancialReportsAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
        _ths.Verify(x => x.FetchFinancialReportsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ────────────────────────────────────────────────────────────────────
    // Path 2: emweb empty → datacenter success (degraded)
    // ────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task CollectAsync_EmwebEmpty_DatacenterSuccess_FlagsDegradedAndRecordsFallback()
    {
        _emweb.Setup(x => x.DetectCompanyTypeAsync(Symbol, It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);
        _emweb.Setup(x => x.FetchFinancialReportsAsync(Symbol, 4, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FinancialReport>());

        _datacenter.Setup(x => x.FetchFinancialReportsAsync(Symbol, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FinancialReport>
            {
                MakeReport("2025-12-31", "datacenter"),
            });

        var orch = BuildOrchestrator();

        var result = await orch.CollectAsync(Symbol);

        Assert.True(result.Success);
        Assert.Equal("datacenter", result.Channel);
        Assert.Equal("datacenter", result.MainSourceChannel);
        Assert.Equal(new[] { "emweb" }, result.FallbackChannels);
        Assert.True(result.IsDegraded);
        Assert.False(string.IsNullOrEmpty(result.DegradeReason));
        Assert.Null(result.PdfSummarySupplement);

        var log = GetLatestLog();
        Assert.Equal("datacenter", log.Channel);
        Assert.Equal("datacenter", log.MainSourceChannel);
        Assert.Equal(new[] { "emweb" }, log.FallbackChannels);
        Assert.True(log.IsDegraded);
        Assert.Null(log.PdfSummarySupplement);
        Assert.Equal(result.ReportPeriods, log.ReportPeriods);

        _ths.Verify(x => x.FetchFinancialReportsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ────────────────────────────────────────────────────────────────────
    // Path 3: all three channels empty, no PDF pipeline → fail
    // ────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task CollectAsync_AllChannelsEmpty_NoPdf_FailsWithFallbackList()
    {
        _emweb.Setup(x => x.DetectCompanyTypeAsync(Symbol, It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);
        _emweb.Setup(x => x.FetchFinancialReportsAsync(Symbol, 4, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FinancialReport>());
        _datacenter.Setup(x => x.FetchFinancialReportsAsync(Symbol, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FinancialReport>());
        _ths.Setup(x => x.FetchFinancialReportsAsync(Symbol, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FinancialReport>());

        var orch = BuildOrchestrator(pipeline: null);

        var result = await orch.CollectAsync(Symbol);

        Assert.False(result.Success);
        Assert.Equal("none", result.Channel);
        Assert.Null(result.MainSourceChannel);
        Assert.Equal(new[] { "emweb", "datacenter", "ths" }, result.FallbackChannels);
        Assert.True(result.IsDegraded);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        Assert.Null(result.PdfSummarySupplement);

        var log = GetLatestLog();
        Assert.False(log.Success);
        Assert.Equal("none", log.Channel);
        Assert.Null(log.MainSourceChannel);
        Assert.Equal(new[] { "emweb", "datacenter", "ths" }, log.FallbackChannels);
        Assert.False(string.IsNullOrWhiteSpace(log.ErrorMessage));
        Assert.Null(log.PdfSummarySupplement);
    }

    // ────────────────────────────────────────────────────────────────────
    // Path 4: all three channels empty + PDF pipeline rescues with 2 tables
    // ────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task CollectAsync_AllChannelsEmpty_PdfPipelineSupplements_ReportsPdfChannel()
    {
        _emweb.Setup(x => x.DetectCompanyTypeAsync(Symbol, It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);
        _emweb.Setup(x => x.FetchFinancialReportsAsync(Symbol, 4, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FinancialReport>());
        _datacenter.Setup(x => x.FetchFinancialReportsAsync(Symbol, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FinancialReport>());
        _ths.Setup(x => x.FetchFinancialReportsAsync(Symbol, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FinancialReport>());

        var pdf = new Mock<IPdfProcessingPipeline>(MockBehavior.Strict);
        pdf.Setup(p => p.ProcessAsync(Symbol, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PdfPipelineResult
            {
                Symbol = Symbol,
                DownloadedCount = 2,
                ParsedCount = 2,
            });

        var orch = BuildOrchestrator(pipeline: pdf.Object);

        var result = await orch.CollectAsync(Symbol);

        Assert.True(result.Success);
        Assert.Equal("pdf", result.Channel);
        Assert.Equal("pdf", result.MainSourceChannel);
        Assert.Equal("pdf:2_tables_appended", result.PdfSummarySupplement);
        Assert.Equal(new[] { "emweb", "datacenter", "ths" }, result.FallbackChannels);
        Assert.Equal(2, result.ReportCount);

        var log = GetLatestLog();
        Assert.True(log.Success);
        Assert.Equal("pdf", log.Channel);
        Assert.Equal("pdf", log.MainSourceChannel);
        Assert.Equal("pdf:2_tables_appended", log.PdfSummarySupplement);
        Assert.Equal(new[] { "emweb", "datacenter", "ths" }, log.FallbackChannels);

        pdf.Verify(p => p.ProcessAsync(Symbol, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
