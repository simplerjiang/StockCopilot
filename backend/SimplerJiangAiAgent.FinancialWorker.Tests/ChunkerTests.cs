using LiteDB;
using SimplerJiangAiAgent.FinancialWorker.Models;
using SimplerJiangAiAgent.FinancialWorker.Services.Rag;

namespace SimplerJiangAiAgent.FinancialWorker.Tests;

public class ChunkerTests
{
    private readonly FinancialReportChunker _chunker = new();

    private PdfFileDocument CreateTestDoc(List<PdfPageText>? pages = null, List<PdfParseUnit>? units = null)
    {
        return new PdfFileDocument
        {
            Id = ObjectId.NewObjectId(),
            Symbol = "600519",
            ReportPeriod = "2024-12-31",
            ReportType = "Annual",
            FullTextPages = pages ?? new(),
            ParseUnits = units ?? new()
        };
    }

    [Fact]
    public void Chunk_EmptyDocument_ReturnsEmpty()
    {
        var doc = CreateTestDoc();
        var chunks = _chunker.Chunk(doc);
        Assert.Empty(chunks);
    }

    [Fact]
    public void Chunk_TableUnit_CreatesTableChunk()
    {
        var doc = CreateTestDoc(units: new List<PdfParseUnit>
        {
            new()
            {
                BlockKind = PdfBlockKind.Table,
                PageStart = 5,
                PageEnd = 7,
                SectionName = "BalanceSheet",
                ParsedFields = new Dictionary<string, object?>
                {
                    ["总资产"] = 1505_0000_0000m,
                    ["总负债"] = 500_0000_0000m
                }
            }
        });

        var chunks = _chunker.Chunk(doc);
        Assert.Single(chunks);
        Assert.Equal("table", chunks[0].BlockKind);
        Assert.Equal("BalanceSheet", chunks[0].Section);
        Assert.Equal(5, chunks[0].PageStart);
        Assert.Equal(7, chunks[0].PageEnd);
        Assert.Contains("总资产", chunks[0].Text);
    }

    [Fact]
    public void Chunk_ShortNarrative_SingleChunk()
    {
        var doc = CreateTestDoc(units: new List<PdfParseUnit>
        {
            new()
            {
                BlockKind = PdfBlockKind.NarrativeSection,
                PageStart = 1,
                PageEnd = 2,
                SectionName = "管理层讨论",
                ExtractedText = "贵州茅台酒股份有限公司2024年度报告。公司实现营业收入1505亿元。"
            }
        });

        var chunks = _chunker.Chunk(doc);
        Assert.Single(chunks);
        Assert.Equal("prose", chunks[0].BlockKind);
        Assert.Contains("营业收入", chunks[0].Text);
    }

    [Fact]
    public void Chunk_LongText_SplitsIntoMultiple()
    {
        // Generate text > 800 chars with paragraph breaks
        var longText = string.Join("\n\n", Enumerable.Range(1, 20)
            .Select(i => $"第{i}段：贵州茅台酒股份有限公司是中国最大的白酒生产企业之一，主要从事茅台酒及系列产品的生产和销售。公司2024年实现营业收入约1505亿元人民币。"));

        var doc = CreateTestDoc(units: new List<PdfParseUnit>
        {
            new()
            {
                BlockKind = PdfBlockKind.NarrativeSection,
                PageStart = 1,
                PageEnd = 5,
                SectionName = "经营情况",
                ExtractedText = longText
            }
        });

        var chunks = _chunker.Chunk(doc);
        Assert.True(chunks.Count > 1, $"Expected >1 chunks but got {chunks.Count}");
        Assert.All(chunks, c => Assert.Equal("prose", c.BlockKind));
        Assert.All(chunks, c => Assert.Equal("600519", c.Symbol));
    }

    [Fact]
    public void Chunk_WithHeadings_SplitsBySections()
    {
        var text = "一、公司基本情况\n贵州茅台概述。\n\n二、主营业务分析\n收入利润情况。\n\n三、风险因素\n市场风险。";

        var doc = CreateTestDoc(units: new List<PdfParseUnit>
        {
            new()
            {
                BlockKind = PdfBlockKind.NarrativeSection,
                PageStart = 1,
                PageEnd = 3,
                ExtractedText = text
            }
        });

        var chunks = _chunker.Chunk(doc);
        Assert.True(chunks.Count >= 3, $"Expected >=3 heading-split chunks but got {chunks.Count}");
    }

    [Fact]
    public void Chunk_FallbackToFullTextPages_WhenNoParseUnits()
    {
        var doc = CreateTestDoc(pages: new List<PdfPageText>
        {
            new() { PageNumber = 1, Text = "第一页内容：公司简介和概述。" },
            new() { PageNumber = 2, Text = "第二页内容：财务数据分析。" }
        });

        var chunks = _chunker.Chunk(doc);
        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.Equal("600519", c.Symbol));
    }

    [Fact]
    public void Chunk_SetsMetadataCorrectly()
    {
        var doc = CreateTestDoc(units: new List<PdfParseUnit>
        {
            new()
            {
                BlockKind = PdfBlockKind.NarrativeSection,
                PageStart = 1,
                PageEnd = 1,
                ExtractedText = "测试内容"
            }
        });

        var chunks = _chunker.Chunk(doc);
        Assert.Single(chunks);
        var c = chunks[0];
        Assert.Equal("financial_report", c.SourceType);
        Assert.Equal(doc.Id.ToString(), c.SourceId);
        Assert.Equal("600519", c.Symbol);
        Assert.Equal("2024-12-31", c.ReportDate);
        Assert.Equal("Annual", c.ReportType);
        Assert.Equal("", c.TokenizedText); // Not tokenized yet (done by pipeline)
    }
}
