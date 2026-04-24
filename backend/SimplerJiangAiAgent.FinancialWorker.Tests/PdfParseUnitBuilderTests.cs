using SimplerJiangAiAgent.FinancialWorker.Models;
using SimplerJiangAiAgent.FinancialWorker.Services.Pdf;

namespace SimplerJiangAiAgent.FinancialWorker.Tests;

/// <summary>
/// v0.4.1 §9.1 验收：解析单元 page_start / page_end / block_kind 三字段构造与兜底。
/// 覆盖：3 类 block_kind / 跨页区间 / 单页区间 / 缺页降级（page_start=0 拒收）。
/// </summary>
public class PdfParseUnitBuilderTests
{
    private static PdfExtractionResult MakeExtraction(params (string text, ExtractedTable? table)[] pages)
    {
        var result = new PdfExtractionResult { ExtractorName = "PdfPig", Success = true };
        for (var i = 0; i < pages.Length; i++)
        {
            result.Pages.Add(pages[i].text);
            if (pages[i].table != null) result.Tables.Add(pages[i].table!);
        }
        return result;
    }

    [Fact]
    public void Build_NarrativeSection_CrossPage_HasPageStartLessThanPageEnd()
    {
        // 资产负债表起始页 = 2，下一节（利润表）起始页 = 6，预期 page_start=2、page_end=5（跨页）。
        var extraction = MakeExtraction(
            ("封面", null),
            ("合并资产负债表 项目 金额", null),
            ("货币资金 1,000", null),
            ("应收账款 500", null),
            ("资产总计 100000", null),
            ("合并利润表 项目 金额", null),
            ("营业收入 50000", null),
            ("合并现金流量表 项目 金额", null),
            ("经营活动现金流入 8000", null));

        var parsed = new ParsedFinancialStatements
        {
            BalanceSheet = new() { ["TotalAssets"] = 100000.0 },
            IncomeStatement = new() { ["Revenue"] = 50000.0 },
            CashFlowStatement = new() { ["OperatingCashInflow"] = 8000.0 },
        };

        var units = PdfParseUnitBuilder.Build(extraction, parsed);

        var balance = Assert.Single(units, u => u.SectionName == "BalanceSheet");
        Assert.Equal(PdfBlockKind.NarrativeSection, balance.BlockKind);
        Assert.Equal(2, balance.PageStart);
        Assert.Equal(5, balance.PageEnd);
        Assert.True(balance.PageEnd > balance.PageStart, "BalanceSheet 必须跨页");
        Assert.True(balance.IsValid);

        var income = Assert.Single(units, u => u.SectionName == "IncomeStatement");
        Assert.Equal(6, income.PageStart);
        Assert.Equal(7, income.PageEnd);

        var cashflow = Assert.Single(units, u => u.SectionName == "CashFlowStatement");
        Assert.Equal(8, cashflow.PageStart);
        Assert.Equal(9, cashflow.PageEnd); // 延伸到末页
    }

    [Fact]
    public void Build_TableUnit_SinglePage_PageStartEqualsPageEnd()
    {
        var extraction = MakeExtraction(
            ("合并资产负债表 货币资金 1000", null),
            ("一些表格内容", new ExtractedTable
            {
                PageNumber = 2,
                Rows = new List<List<string>> {
                    new() { "项目", "金额" },
                    new() { "存货", "300" },
                },
                NearbyHeading = "存货明细"
            }));

        var parsed = new ParsedFinancialStatements
        {
            BalanceSheet = new() { ["CashAndEquivalents"] = 1000.0 },
        };

        var units = PdfParseUnitBuilder.Build(extraction, parsed);
        var table = Assert.Single(units, u => u.BlockKind == PdfBlockKind.Table);
        Assert.Equal(2, table.PageStart);
        Assert.Equal(table.PageStart, table.PageEnd);
        Assert.Equal("存货明细", table.SectionName);
        Assert.Equal(2, table.FieldCount); // Rows.Count
        Assert.True(table.IsValid);
    }

    [Fact]
    public void Build_RejectsParseUnitWithPageStartZero()
    {
        // 非法 PageNumber=0 的 Table 必须被丢弃（缺页降级 → 拒收）。
        var extraction = MakeExtraction(
            ("合并资产负债表 货币资金 1000", new ExtractedTable
            {
                PageNumber = 0, // ← 非法
                Rows = new List<List<string>> { new() { "x", "y" } },
            }));
        var parsed = new ParsedFinancialStatements
        {
            BalanceSheet = new() { ["CashAndEquivalents"] = 1000.0 },
        };

        var units = PdfParseUnitBuilder.Build(extraction, parsed);

        Assert.DoesNotContain(units, u => u.BlockKind == PdfBlockKind.Table);
        Assert.All(units, u =>
        {
            Assert.True(u.PageStart >= 1, $"PageStart 必须 >= 1，实际为 {u.PageStart}");
            Assert.True(u.IsValid);
        });
    }

    [Fact]
    public void Build_FallbackPageRange_WhenSectionMarkerMissing()
    {
        // 解析结果说有数据，但页文本里完全找不到任何区段标记 → 回退为整文件范围（1..PageCount）。
        var extraction = MakeExtraction(
            ("纯叙述文本第一页", null),
            ("纯叙述文本第二页", null));
        var parsed = new ParsedFinancialStatements
        {
            BalanceSheet = new() { ["TotalAssets"] = 1.0 },
        };

        var units = PdfParseUnitBuilder.Build(extraction, parsed);

        var balance = Assert.Single(units, u => u.SectionName == "BalanceSheet");
        Assert.Equal(1, balance.PageStart);
        Assert.Equal(2, balance.PageEnd);
        Assert.True(balance.IsValid);
        Assert.Contains("fallback", balance.Snippet ?? string.Empty);
    }

    [Fact]
    public void Build_NoUnitProducedForEmptySection()
    {
        var extraction = MakeExtraction(
            ("合并资产负债表 货币资金 1000", null));
        var parsed = new ParsedFinancialStatements
        {
            BalanceSheet = new() { ["CashAndEquivalents"] = 1.0 },
            // IncomeStatement / CashFlowStatement 留空，不产生 Unit
        };

        var units = PdfParseUnitBuilder.Build(extraction, parsed);

        Assert.Single(units);
        Assert.Equal("BalanceSheet", units[0].SectionName);
    }

    [Fact]
    public void BuildFigureCaption_ReturnsNullForInvalidPage()
    {
        Assert.Null(PdfParseUnitBuilder.BuildFigureCaption(0, 1));
        Assert.Null(PdfParseUnitBuilder.BuildFigureCaption(-3, 5));

        var caption = PdfParseUnitBuilder.BuildFigureCaption(7, 7, "图1：营收构成");
        Assert.NotNull(caption);
        Assert.Equal(PdfBlockKind.FigureCaption, caption!.BlockKind);
        Assert.Equal(7, caption.PageStart);
        Assert.Equal(7, caption.PageEnd);
        Assert.Equal("图1：营收构成", caption.Snippet);
        Assert.True(caption.IsValid);
    }

    [Fact]
    public void Build_AllProducedUnits_HaveAllThreeRequiredFields()
    {
        // v0.4.1 §9.1 硬约束：所有产出的 ParseUnit 都必须含三字段。
        var extraction = MakeExtraction(
            ("合并资产负债表 货币资金 1000", null),
            ("内容", new ExtractedTable { PageNumber = 2, Rows = new List<List<string>> { new() { "a", "b" } } }),
            ("合并利润表 营业收入 500", null));

        var parsed = new ParsedFinancialStatements
        {
            BalanceSheet = new() { ["CashAndEquivalents"] = 1000.0 },
            IncomeStatement = new() { ["Revenue"] = 500.0 },
        };

        var units = PdfParseUnitBuilder.Build(extraction, parsed);

        Assert.NotEmpty(units);
        Assert.All(units, u =>
        {
            Assert.True(u.PageStart >= 1, "page_start 1-based");
            Assert.True(u.PageEnd >= u.PageStart, "page_end >= page_start");
            Assert.True(Enum.IsDefined(typeof(PdfBlockKind), u.BlockKind), "block_kind 合法");
        });
    }
}
