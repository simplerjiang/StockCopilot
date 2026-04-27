using SimplerJiangAiAgent.FinancialWorker.Data;
using SimplerJiangAiAgent.FinancialWorker.Models;
using SimplerJiangAiAgent.FinancialWorker.Services.Announcement;
using SimplerJiangAiAgent.FinancialWorker.Services.Rag;
using Microsoft.Extensions.Logging.Abstractions;

namespace SimplerJiangAiAgent.FinancialWorker.Tests;

public class AnnouncementPdfProcessorTests : IDisposable
{
    private readonly string _ragDbPath;
    private readonly RagDbContext _ragDb;

    public AnnouncementPdfProcessorTests()
    {
        _ragDbPath = Path.Combine(Path.GetTempPath(), $"rag-test-{Guid.NewGuid():N}.db");
        _ragDb = new RagDbContext($"Data Source={_ragDbPath}");
    }

    public void Dispose()
    {
        _ragDb.Dispose();
        try { if (File.Exists(_ragDbPath)) File.Delete(_ragDbPath); } catch { }
    }

    // ──── ChunkText tests ────

    [Fact]
    public void ChunkText_EmptyText_ReturnsEmpty()
    {
        var result = AnnouncementPdfProcessor.ChunkText("");
        Assert.Empty(result);
    }

    [Fact]
    public void ChunkText_NullText_ReturnsEmpty()
    {
        var result = AnnouncementPdfProcessor.ChunkText(null!);
        Assert.Empty(result);
    }

    [Fact]
    public void ChunkText_WhitespaceOnly_ReturnsEmpty()
    {
        var result = AnnouncementPdfProcessor.ChunkText("   \n\n   ");
        Assert.Empty(result);
    }

    [Fact]
    public void ChunkText_ShortText_SingleChunk()
    {
        var text = "公司决定每股派发现金红利0.5元。";
        var result = AnnouncementPdfProcessor.ChunkText(text);
        Assert.Single(result);
        Assert.Equal(text, result[0]);
    }

    [Fact]
    public void ChunkText_LongText_SplitsByDoubleNewline()
    {
        var para1 = new string('A', 1200);
        var para2 = new string('B', 1200);
        var text = para1 + "\n\n" + para2;

        var result = AnnouncementPdfProcessor.ChunkText(text);

        Assert.Equal(2, result.Count);
        Assert.Equal(para1, result[0]);
        Assert.Equal(para2, result[1]);
    }

    [Fact]
    public void ChunkText_LongTextNoDoubleNewline_SplitsBySingleNewline()
    {
        var line1 = new string('A', 1200);
        var line2 = new string('B', 1200);
        var text = line1 + "\n" + line2;

        var result = AnnouncementPdfProcessor.ChunkText(text);

        Assert.Equal(2, result.Count);
        Assert.Equal(line1, result[0]);
        Assert.Equal(line2, result[1]);
    }

    // ──── InferAnnouncementType tests ────

    [Theory]
    [InlineData("关于2024年度利润分配及分红派息的公告", "分红")]
    [InlineData("关于持股5%以上股东减持计划的预披露公告", "减持")]
    [InlineData("关于回购公司股份进展的公告", "回购")]
    [InlineData("关于公司股份质押的公告", "质押")]
    [InlineData("2024年度业绩预告", "业绩预告")]
    public void InferAnnouncementType_MatchesKnownPatterns(string title, string expected)
    {
        var result = AnnouncementPdfProcessor.InferAnnouncementType(title);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void InferAnnouncementType_NoMatch_ReturnsNull()
    {
        var result = AnnouncementPdfProcessor.InferAnnouncementType("公司章程修订对照表");
        Assert.Null(result);
    }

    [Fact]
    public void InferAnnouncementType_Empty_ReturnsNull()
    {
        Assert.Null(AnnouncementPdfProcessor.InferAnnouncementType(""));
        Assert.Null(AnnouncementPdfProcessor.InferAnnouncementType(null!));
    }

    // ──── ProcessAsync dedup tests ────

    [Fact]
    public async Task ProcessAsync_DuplicateArtCode_SkipsSecondTime()
    {
        // Pre-insert a chunk with the same art_code
        _ragDb.InsertChunk(new FinancialChunk
        {
            ChunkId = "ann_EXISTING_0",
            SourceType = "announcement",
            SourceId = "EXISTING",
            Symbol = "sh600519",
            ReportDate = "2025-01-01",
            BlockKind = "prose",
            Text = "existing",
            TokenizedText = "existing",
        });

        var processor = new AnnouncementPdfProcessor(
            _ragDb,
            new JiebaTokenizer(),
            new NoOpEmbedder(),
            NullLogger<AnnouncementPdfProcessor>.Instance);

        var pdfs = new List<DownloadedAnnouncementPdf>
        {
            new("dummy.pdf", "sh600519", "Test", "EXISTING", DateTime.Now, "http://example.com")
        };

        // Should skip because art_code already in DB
        var count = await processor.ProcessAsync(pdfs);
        Assert.Equal(0, count);
    }

    // ──── Chunk field correctness ────

    [Fact]
    public void ChunkFields_CorrectSourceTypeAndId()
    {
        var tokenizer = new JiebaTokenizer();
        var text = "公司决定每股派发现金红利0.5元。";
        var tokenized = tokenizer.Tokenize(text);

        var chunk = new FinancialChunk
        {
            ChunkId = "ann_TEST001_0",
            SourceType = "announcement",
            SourceId = "TEST001",
            Symbol = "sh600519",
            ReportDate = "2025-04-24",
            ReportType = "分红",
            Section = "0",
            BlockKind = "prose",
            Text = text,
            TokenizedText = tokenized,
        };

        Assert.Equal("announcement", chunk.SourceType);
        Assert.Equal("TEST001", chunk.SourceId);
        Assert.Equal("sh600519", chunk.Symbol);
        Assert.Equal("分红", chunk.ReportType);
        Assert.Equal("prose", chunk.BlockKind);
        Assert.NotEmpty(chunk.TokenizedText);
        Assert.Contains(" ", chunk.TokenizedText); // jieba should split into tokens
    }

    // ──── Integration: write to RAG DB ────

    [Fact]
    public void InsertAnnouncementChunk_WritesToDb()
    {
        var chunk = new FinancialChunk
        {
            ChunkId = "ann_DBTEST_0",
            SourceType = "announcement",
            SourceId = "DBTEST",
            Symbol = "sh603099",
            ReportDate = "2025-04-20",
            ReportType = "减持",
            Section = "0",
            BlockKind = "prose",
            Text = "持股5%以上股东计划在未来6个月内减持不超过公司总股本的2%。",
            TokenizedText = "持股 5% 以上 股东 计划 未来 6 个月 减持 不 超过 公司 总 股本 2%",
        };

        _ragDb.InsertChunk(chunk);

        var count = _ragDb.CountChunks("DBTEST");
        Assert.Equal(1, count);
    }
}
