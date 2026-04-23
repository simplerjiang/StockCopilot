using LiteDB;
using SimplerJiangAiAgent.FinancialWorker.Data;
using SimplerJiangAiAgent.FinancialWorker.Models;

namespace SimplerJiangAiAgent.FinancialWorker.Tests;

/// <summary>
/// v0.4.1 §5.1 + §9.1：PdfFileDocument / PdfParseUnit 模型与 LiteDB 序列化测试。
/// 验收覆盖：3 类 block_kind / 三字段非空校验 / page_start=0 拒收 / 集合 round-trip。
/// </summary>
[Collection("LiteDbBsonMapper")] // 与 FinancialDataOrchestratorPathTests 共享集合：避免 BsonMapper.Global 并发污染
public class PdfFileDocumentTests : IDisposable
{
    private readonly string _dbPath;
    private readonly FinancialDbContext _db;

    public PdfFileDocumentTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"pdf-file-tests-{Guid.NewGuid():N}.db");
        _db = new FinancialDbContext($"Filename={_dbPath};Connection=direct");
    }

    public void Dispose()
    {
        _db.Dispose();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
        var log = Path.ChangeExtension(_dbPath, "-log.db");
        try { if (File.Exists(log)) File.Delete(log); } catch { }
    }

    [Fact]
    public void PdfBlockKind_HasThreeMembers_RequiredByPlanSection9_1()
    {
        // v0.4.1 §9.1：block_kind 必须支持 narrative_section / table / figure_caption。
        var values = Enum.GetValues<PdfBlockKind>();
        Assert.Contains(PdfBlockKind.NarrativeSection, values);
        Assert.Contains(PdfBlockKind.Table, values);
        Assert.Contains(PdfBlockKind.FigureCaption, values);
    }

    [Theory]
    [InlineData(PdfBlockKind.NarrativeSection, 5, 8, true)]   // 跨页区间
    [InlineData(PdfBlockKind.Table, 12, 12, true)]            // 单页区间
    [InlineData(PdfBlockKind.FigureCaption, 1, 1, true)]      // 单页区间
    [InlineData(PdfBlockKind.NarrativeSection, 0, 5, false)]  // page_start=0 拒收
    [InlineData(PdfBlockKind.Table, 1, 0, false)]             // page_end < page_start 拒收
    [InlineData(PdfBlockKind.NarrativeSection, -1, 1, false)] // 负数 拒收
    public void PdfParseUnit_IsValid_RejectsMissingPage(
        PdfBlockKind kind, int pageStart, int pageEnd, bool expected)
    {
        var unit = new PdfParseUnit
        {
            BlockKind = kind,
            PageStart = pageStart,
            PageEnd = pageEnd,
            SectionName = "test",
            FieldCount = 1,
        };
        Assert.Equal(expected, unit.IsValid);
    }

    [Fact]
    public void PdfFileDocument_RoundTripsThroughLiteDb_WithAllRequiredFields()
    {
        var doc = new PdfFileDocument
        {
            Symbol = "600519",
            FileName = "600519_2024_annual.pdf",
            Title = "贵州茅台2024年年度报告",
            LocalPath = @"C:\App_Data\financial-reports\600519\600519_2024_annual.pdf",
            AccessKey = "600519_2024_annual.pdf",
            ReportPeriod = "2024-12-31",
            ReportType = "Annual",
            Extractor = "PdfPig",
            VoteConfidence = "Unanimous",
            FieldCount = 42,
            LastError = null,
            LastParsedAt = new DateTime(2026, 4, 22, 10, 0, 0, DateTimeKind.Utc),
            LastReparsedAt = null,
            ParseUnits = new List<PdfParseUnit>
            {
                new() { BlockKind = PdfBlockKind.NarrativeSection, PageStart = 5, PageEnd = 8, SectionName = "BalanceSheet", FieldCount = 18 },
                new() { BlockKind = PdfBlockKind.Table, PageStart = 12, PageEnd = 12, SectionName = "明细表", FieldCount = 24 },
                new() { BlockKind = PdfBlockKind.FigureCaption, PageStart = 3, PageEnd = 3, SectionName = "FigureCaption", Snippet = "图1：营收构成" },
            },
        };

        _db.PdfFiles.Insert(doc);

        var loaded = _db.PdfFiles.FindOne(x => x.Symbol == "600519");
        Assert.NotNull(loaded);
        Assert.Equal(doc.FileName, loaded.FileName);
        Assert.Equal(doc.Title, loaded.Title);
        Assert.Equal(doc.LocalPath, loaded.LocalPath);
        Assert.Equal(doc.ReportPeriod, loaded.ReportPeriod);
        Assert.Equal(doc.ReportType, loaded.ReportType);
        Assert.Equal(doc.Extractor, loaded.Extractor);
        Assert.Equal(doc.VoteConfidence, loaded.VoteConfidence);
        Assert.Equal(doc.FieldCount, loaded.FieldCount);
        Assert.Null(loaded.LastError);
        // LiteDB 默认按本地时区存储 DateTime，转回 UTC 比较
        Assert.Equal(doc.LastParsedAt, loaded.LastParsedAt.ToUniversalTime());
        Assert.Null(loaded.LastReparsedAt);

        // 三类 block_kind 全部 round-trip 成功
        Assert.Equal(3, loaded.ParseUnits.Count);
        Assert.Contains(loaded.ParseUnits, u => u.BlockKind == PdfBlockKind.NarrativeSection && u.PageStart == 5 && u.PageEnd == 8);
        Assert.Contains(loaded.ParseUnits, u => u.BlockKind == PdfBlockKind.Table && u.PageStart == 12 && u.PageEnd == 12);
        Assert.Contains(loaded.ParseUnits, u => u.BlockKind == PdfBlockKind.FigureCaption && u.PageStart == 3 && u.PageEnd == 3);
        Assert.All(loaded.ParseUnits, u => Assert.True(u.IsValid));
    }

    [Fact]
    public void PdfFileDocument_FullTextPages_RoundTripsThroughLiteDb()
    {
        var doc = new PdfFileDocument
        {
            Symbol = "000001",
            FileName = "000001_2024_annual.pdf",
            Title = "平安银行2024年年度报告",
            LocalPath = @"C:\App_Data\financial-reports\000001\000001_2024_annual.pdf",
            AccessKey = "000001_2024_annual.pdf",
            ReportPeriod = "2024-12-31",
            ReportType = "Annual",
            FullTextPages = new List<PdfPageText>
            {
                new() { PageNumber = 1, Text = "第一页内容" },
                new() { PageNumber = 2, Text = "第二页内容" },
                new() { PageNumber = 3, Text = "第三页内容" },
            },
        };

        _db.PdfFiles.Insert(doc);

        var loaded = _db.PdfFiles.FindOne(x => x.Symbol == "000001");
        Assert.NotNull(loaded);
        Assert.Equal(3, loaded.FullTextPages.Count);

        // 1-based sequential page numbers
        for (int i = 0; i < loaded.FullTextPages.Count; i++)
        {
            Assert.Equal(i + 1, loaded.FullTextPages[i].PageNumber);
            Assert.False(string.IsNullOrEmpty(loaded.FullTextPages[i].Text));
        }

        Assert.Equal("第一页内容", loaded.FullTextPages[0].Text);
        Assert.Equal("第二页内容", loaded.FullTextPages[1].Text);
        Assert.Equal("第三页内容", loaded.FullTextPages[2].Text);
    }

    [Fact]
    public void PdfFileDocument_UpsertUniqueIndex_OnSymbolAndLocalPath()
    {
        var doc1 = new PdfFileDocument
        {
            Symbol = "600519",
            FileName = "a.pdf",
            LocalPath = @"C:\pdfs\a.pdf",
            ReportPeriod = "2024-12-31",
            ReportType = "Annual",
        };
        _db.PdfFiles.Insert(doc1);

        // 同 Symbol+LocalPath 第二次 Insert 必须违反唯一索引
        var doc2 = new PdfFileDocument
        {
            Symbol = "600519",
            FileName = "a.pdf",
            LocalPath = @"C:\pdfs\a.pdf",
            ReportPeriod = "2024-12-31",
            ReportType = "Annual",
        };

        Assert.Throws<LiteException>(() => _db.PdfFiles.Insert(doc2));
    }

    [Fact]
    public void PdfParseUnit_ExtractedTextAndParsedFields_RoundTripThroughLiteDb()
    {
        var doc = new PdfFileDocument
        {
            Symbol = "300750",
            FileName = "300750_2024_annual.pdf",
            Title = "宁德时代2024年年度报告",
            LocalPath = @"C:\App_Data\financial-reports\300750\300750_2024_annual.pdf",
            AccessKey = "300750_2024_annual.pdf",
            ReportPeriod = "2024-12-31",
            ReportType = "Annual",
            ParseUnits = new List<PdfParseUnit>
            {
                new()
                {
                    BlockKind = PdfBlockKind.NarrativeSection,
                    PageStart = 5,
                    PageEnd = 8,
                    SectionName = "BalanceSheet",
                    FieldCount = 3,
                    ExtractedText = "合并资产负债表\n货币资金 1000\n应收账款 500",
                    ParsedFields = new Dictionary<string, object?>
                    {
                        ["TotalAssets"] = 100000.0,
                        ["CashAndEquivalents"] = 1000.0,
                        ["NullField"] = null,
                    }
                },
                new()
                {
                    BlockKind = PdfBlockKind.Table,
                    PageStart = 12,
                    PageEnd = 12,
                    SectionName = "明细表",
                    FieldCount = 2,
                    // No ExtractedText/ParsedFields — backward compat
                },
            },
        };

        _db.PdfFiles.Insert(doc);

        var loaded = _db.PdfFiles.FindOne(x => x.Symbol == "300750");
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.ParseUnits.Count);

        var bs = loaded.ParseUnits.First(u => u.SectionName == "BalanceSheet");
        Assert.Equal("合并资产负债表\n货币资金 1000\n应收账款 500", bs.ExtractedText);
        Assert.NotNull(bs.ParsedFields);
        Assert.Equal(3, bs.ParsedFields!.Count);
        Assert.Equal(100000.0, Convert.ToDouble(bs.ParsedFields["TotalAssets"]));
        Assert.Equal(1000.0, Convert.ToDouble(bs.ParsedFields["CashAndEquivalents"]));

        // Unit without the new fields should have null (backward compat)
        var table = loaded.ParseUnits.First(u => u.SectionName == "明细表");
        Assert.Null(table.ExtractedText);
        Assert.Null(table.ParsedFields);
    }

    [Fact]
    public void PdfFileDocument_VotingCandidates_RoundTripsThroughLiteDb()
    {
        var doc = new PdfFileDocument
        {
            Symbol = "601318",
            FileName = "601318_2024_annual.pdf",
            Title = "中国平安2024年年度报告",
            LocalPath = @"C:\App_Data\financial-reports\601318\601318_2024_annual.pdf",
            AccessKey = "601318_2024_annual.pdf",
            ReportPeriod = "2024-12-31",
            ReportType = "Annual",
            VotingNotes = "PdfPig and iText7 agree, Docnet failed",
            VotingCandidates = new List<VotingCandidate>
            {
                new() { Extractor = "PdfPig", Success = true, PageCount = 120, SampleText = "中国平安保险（集团）", IsWinner = true },
                new() { Extractor = "iText7", Success = true, PageCount = 120, SampleText = "中国平安保险（集团）", IsWinner = false },
                new() { Extractor = "Docnet", Success = false, PageCount = 0, SampleText = null, IsWinner = false },
            },
        };

        _db.PdfFiles.Insert(doc);

        var loaded = _db.PdfFiles.FindOne(x => x.Symbol == "601318");
        Assert.NotNull(loaded);
        Assert.Equal("PdfPig and iText7 agree, Docnet failed", loaded.VotingNotes);
        Assert.Equal(3, loaded.VotingCandidates.Count);

        var winner = loaded.VotingCandidates.Single(c => c.IsWinner);
        Assert.Equal("PdfPig", winner.Extractor);
        Assert.True(winner.Success);
        Assert.Equal(120, winner.PageCount);
        Assert.Equal("中国平安保险（集团）", winner.SampleText);

        var failed = loaded.VotingCandidates.Single(c => c.Extractor == "Docnet");
        Assert.False(failed.Success);
        Assert.Equal(0, failed.PageCount);
        Assert.Null(failed.SampleText);
        Assert.False(failed.IsWinner);
    }
}
