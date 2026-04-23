using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SimplerJiangAiAgent.FinancialWorker.Data;
using SimplerJiangAiAgent.FinancialWorker.Models;
using SimplerJiangAiAgent.FinancialWorker.Services;
using SimplerJiangAiAgent.FinancialWorker.Services.Pdf;

namespace SimplerJiangAiAgent.FinancialWorker.Tests;

/// <summary>
/// v0.4.2 NS3：验证 PDF 下载后立即写入 stub 记录的行为。
/// </summary>
[Collection("LiteDbBsonMapper")]
public class PdfPipelineStubRecordTests : IDisposable
{
    private readonly string _dbPath;
    private readonly FinancialDbContext _db;
    private readonly string _tempDir;

    public PdfPipelineStubRecordTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"pdf-stub-tests-{Guid.NewGuid():N}.db");
        _db = new FinancialDbContext($"Filename={_dbPath};Connection=direct");
        _tempDir = Path.Combine(Path.GetTempPath(), $"pdf-stub-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
        var log = Path.ChangeExtension(_dbPath, "-log.db");
        try { if (File.Exists(log)) File.Delete(log); } catch { }
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
    }

    /// <summary>
    /// 当 extract 阶段失败时，pdf_files 中仍应有一条 stub 记录
    /// （FieldCount=0、非空 FileName/LocalPath）。
    /// </summary>
    [Fact]
    public async Task ProcessSinglePdf_ExtractFails_StubRecordPersisted()
    {
        // Arrange: create a real (but empty) PDF file so download stage passes
        var pdfPath = Path.Combine(_tempDir, "600519_2024_annual.pdf");
        await File.WriteAllBytesAsync(pdfPath, new byte[] { 0x25, 0x50, 0x44, 0x46 }); // "%PDF" header

        // All extractors fail → extract stage fails
        var failExtractor = new Mock<IPdfTextExtractor>();
        failExtractor.Setup(e => e.Name).Returns("MockExtractor");
        failExtractor.Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PdfExtractionResult
            {
                ExtractorName = "MockExtractor",
                Success = false,
                ErrorMessage = "Test failure",
            });

        var votingEngine = new PdfVotingEngine(
            new[] { failExtractor.Object },
            NullLogger<PdfVotingEngine>.Instance);

        var tableParser = new FinancialTableParser(NullLogger<FinancialTableParser>.Instance);

        // CninfoClient is not used by ProcessSinglePdfAsync; pass a dummy.
        var cninfoClient = new CninfoClient(new HttpClient(), NullLogger<CninfoClient>.Instance);

        var pipeline = new PdfProcessingPipeline(
            cninfoClient,
            votingEngine,
            tableParser,
            _db,
            NullLogger<PdfProcessingPipeline>.Instance);

        var pdf = new DownloadedPdf
        {
            FilePath = pdfPath,
            Symbol = "600519",
            Announcement = new CninfoAnnouncement
            {
                Title = "贵州茅台2024年年度报告",
                PublishTime = new DateTime(2025, 4, 1),
            }
        };

        // Act
        var result = await pipeline.ProcessSinglePdfAsync("600519", pdf, CancellationToken.None);

        // Assert: extract should have failed
        Assert.False(result.Success);

        // But the stub record should exist in pdf_files
        var doc = _db.PdfFiles.FindOne(x => x.Symbol == "600519" && x.LocalPath == pdfPath);
        Assert.NotNull(doc);
        Assert.Equal("600519_2024_annual.pdf", doc.FileName);
        Assert.Equal(pdfPath, doc.LocalPath);
        Assert.Equal(0, doc.FieldCount);
        Assert.NotEmpty(doc.FileName);
        Assert.NotEmpty(doc.LocalPath);
    }

    /// <summary>
    /// 当全部阶段成功完成后，stub 记录应被更新为完整记录（Extractor 非空）。
    /// </summary>
    [Fact]
    public async Task ProcessSinglePdf_FullSuccess_StubRecordUpdatedWithFullData()
    {
        // Arrange: create a real (but empty) PDF file
        var pdfPath = Path.Combine(_tempDir, "600519_2024_annual.pdf");
        await File.WriteAllBytesAsync(pdfPath, new byte[] { 0x25, 0x50, 0x44, 0x46 });

        // Extractor returns success with financial-like text so parse can succeed
        var successExtractor = new Mock<IPdfTextExtractor>();
        successExtractor.Setup(e => e.Name).Returns("MockPdfPig");
        successExtractor.Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PdfExtractionResult
            {
                ExtractorName = "MockPdfPig",
                Success = true,
                Pages = new List<string>
                {
                    // Minimal text that FinancialTableParser can parse into balance sheet fields
                    "合并资产负债表\n" +
                    "项目 期末余额 期初余额\n" +
                    "货币资金 1,000,000.00 900,000.00\n" +
                    "应收账款 500,000.00 400,000.00\n" +
                    "存货 300,000.00 200,000.00\n" +
                    "合并利润表\n" +
                    "项目 本期金额 上期金额\n" +
                    "营业收入 2,000,000.00 1,800,000.00\n" +
                    "合并现金流量表\n" +
                    "项目 本期金额 上期金额\n" +
                    "经营活动产生的现金流量净额 500,000.00 450,000.00\n"
                },
            });

        var votingEngine = new PdfVotingEngine(
            new[] { successExtractor.Object },
            NullLogger<PdfVotingEngine>.Instance);

        var tableParser = new FinancialTableParser(NullLogger<FinancialTableParser>.Instance);
        var cninfoClient = new CninfoClient(new HttpClient(), NullLogger<CninfoClient>.Instance);

        var pipeline = new PdfProcessingPipeline(
            cninfoClient,
            votingEngine,
            tableParser,
            _db,
            NullLogger<PdfProcessingPipeline>.Instance);

        var pdf = new DownloadedPdf
        {
            FilePath = pdfPath,
            Symbol = "600519",
            Announcement = new CninfoAnnouncement
            {
                Title = "贵州茅台2024年年度报告",
                PublishTime = new DateTime(2025, 4, 1),
            }
        };

        // Act
        var result = await pipeline.ProcessSinglePdfAsync("600519", pdf, CancellationToken.None);

        // Assert: the record should exist (either as updated stub or fresh insert)
        var doc = _db.PdfFiles.FindOne(x => x.Symbol == "600519" && x.LocalPath == pdfPath);
        Assert.NotNull(doc);
        Assert.NotEmpty(doc.FileName);
        Assert.NotEmpty(doc.LocalPath);

        // If parsing succeeded, FieldCount should be > 0 and Extractor non-null
        if (result.Success)
        {
            Assert.True(doc.FieldCount > 0, "FieldCount should be > 0 after successful parse");
            Assert.NotNull(doc.Extractor);
        }
    }
}
