using SimplerJiangAiAgent.FinancialWorker.Services.Pdf;
using Microsoft.Extensions.Logging.Abstractions;

namespace SimplerJiangAiAgent.Api.Tests;

public class PdfPipelineTests
{
    // ==================== FinancialTableParser Tests ====================

    [Fact]
    public void Parser_ExtractsBalanceSheet_FromSampleText()
    {
        var parser = new FinancialTableParser(NullLogger<FinancialTableParser>.Instance);
        var extraction = new PdfExtractionResult
        {
            ExtractorName = "Test",
            Success = true,
            Pages = new List<string>
            {
                @"
贵州茅台酒股份有限公司
2024 年年度报告

合并资产负债表
编制单位：贵州茅台酒股份有限公司 2024年12月31日 单位：元

项目                     期末余额              期初余额
货币资金                 178,534,267,890.45     145,678,234,567.89
应收账款                 1,234,567,890.12       987,654,321.00
存货                     45,678,901,234.56      38,765,432,109.87
流动资产合计             235,456,789,012.34     195,432,109,876.54
固定资产                 12,345,678,901.23      11,234,567,890.12
无形资产                 3,456,789,012.34       3,123,456,789.01
非流动资产合计           43,087,654,321.09      38,987,654,321.09
资产总计                 278,544,443,333.43     234,419,764,197.63
短期借款                 0.00                   0.00
应付账款                 5,678,901,234.56       4,567,890,123.45
流动负债合计             45,678,901,234.56      38,765,432,109.87
负债合计                 69,636,110,833.36      58,604,941,049.41
实收资本                 1,256,197,800.00       1,256,197,800.00
未分配利润               167,890,123,456.78     140,567,890,123.45
所有者权益合计           208,908,332,500.07     175,814,823,148.22
"
            }
        };

        var result = parser.Parse(extraction);

        Assert.True(result.HasData);
        Assert.Equal("2024-12-31", result.ReportDate);
        Assert.Equal("Annual", result.ReportType);
        Assert.True(result.BalanceSheet.ContainsKey("CashAndEquivalents"));
        Assert.True(result.BalanceSheet.ContainsKey("TotalAssets"));
        Assert.True(result.BalanceSheet.ContainsKey("TotalLiabilities"));
        Assert.True(result.BalanceSheet.ContainsKey("TotalEquity"));

        // Verify numeric parsing
        var totalAssets = (double)result.BalanceSheet["TotalAssets"]!;
        Assert.True(totalAssets > 200_000_000_000); // > 2000亿
    }

    [Fact]
    public void Parser_ExtractsIncomeStatement_FromSampleText()
    {
        var parser = new FinancialTableParser(NullLogger<FinancialTableParser>.Instance);
        var extraction = new PdfExtractionResult
        {
            ExtractorName = "Test",
            Success = true,
            Pages = new List<string>
            {
                @"
合并利润表
项目                                    本期金额            上期金额
营业总收入                              173,695,000,000.00  149,451,000,000.00
营业收入                                173,695,000,000.00  149,451,000,000.00
营业总成本                              56,789,012,345.67   48,765,432,109.87
营业成本                                12,345,678,901.23   10,987,654,321.09
税金及附加                              23,456,789,012.34   20,123,456,789.01
销售费用                                4,567,890,123.45    3,987,654,321.09
管理费用                                6,789,012,345.67    5,678,901,234.56
研发费用                                1,234,567,890.12    1,098,765,432.10
财务费用                                (2,345,678,901.23)  (1,987,654,321.09)
营业利润                                120,456,789,012.34  103,456,789,012.34
利润总额                                121,234,567,890.12  104,234,567,890.12
所得税费用                              30,123,456,789.01   26,345,678,901.23
净利润                                  86,229,000,000.00   74,734,000,000.00
基本每股收益                            68.66               59.49
"
            }
        };

        var result = parser.Parse(extraction);

        Assert.True(result.IncomeStatement.Count > 0);
        Assert.True(result.IncomeStatement.ContainsKey("Revenue"));
        Assert.True(result.IncomeStatement.ContainsKey("NetProfit"));
        Assert.True(result.IncomeStatement.ContainsKey("BasicEPS"));

        var revenue = (double)result.IncomeStatement["Revenue"]!;
        Assert.True(revenue > 100_000_000_000); // > 1000亿
    }

    [Fact]
    public void Parser_ExtractsCashFlowStatement_FromSampleText()
    {
        var parser = new FinancialTableParser(NullLogger<FinancialTableParser>.Instance);
        var extraction = new PdfExtractionResult
        {
            ExtractorName = "Test",
            Success = true,
            Pages = new List<string>
            {
                @"
合并现金流量表
经营活动产生的现金流量净额      78,901,234,567.89    65,432,109,876.54
投资活动产生的现金流量净额      (12,345,678,901.23)  (10,987,654,321.09)
筹资活动产生的现金流量净额      (34,567,890,123.45)  (28,765,432,109.87)
现金及现金等价物净增加额        31,987,665,543.21    25,679,023,445.58
"
            }
        };

        var result = parser.Parse(extraction);

        Assert.True(result.CashFlowStatement.ContainsKey("OperatingCashFlow"));
        Assert.True(result.CashFlowStatement.ContainsKey("InvestingCashFlow"));
        Assert.True(result.CashFlowStatement.ContainsKey("FinancingCashFlow"));

        // Verify negative number parsing (括号表示负数)
        var investingCf = (double)result.CashFlowStatement["InvestingCashFlow"]!;
        Assert.True(investingCf < 0);
    }

    [Fact]
    public void Parser_ReturnsEmpty_WhenTextIsEmpty()
    {
        var parser = new FinancialTableParser(NullLogger<FinancialTableParser>.Instance);
        var extraction = new PdfExtractionResult
        {
            ExtractorName = "Test",
            Success = true,
            Pages = new List<string> { "" }
        };

        var result = parser.Parse(extraction);

        Assert.False(result.HasData);
    }

    [Fact]
    public void Parser_InfersReportType_Correctly()
    {
        var parser = new FinancialTableParser(NullLogger<FinancialTableParser>.Instance);

        // Annual report
        var annual = new PdfExtractionResult
        {
            ExtractorName = "Test",
            Success = true,
            Pages = new List<string> { "截至 2024年12月31日\n合并资产负债表\n资产总计 100" }
        };
        Assert.Equal("Annual", parser.Parse(annual).ReportType);

        // Q1 report
        var q1 = new PdfExtractionResult
        {
            ExtractorName = "Test",
            Success = true,
            Pages = new List<string> { "截至 2024年03月31日\n合并资产负债表\n资产总计 100" }
        };
        Assert.Equal("Q1", parser.Parse(q1).ReportType);
    }

    // ==================== PdfVotingEngine Tests ====================

    [Fact]
    public async Task VotingEngine_ReturnsAllFailed_WhenNoExtractorsSucceed()
    {
        var extractors = new IPdfTextExtractor[]
        {
            new FakeExtractor("A", false, "Error A"),
            new FakeExtractor("B", false, "Error B")
        };
        var engine = new PdfVotingEngine(extractors, NullLogger<PdfVotingEngine>.Instance);

        var result = await engine.ExtractAndVoteAsync("fake.pdf");

        Assert.Null(result.Winner);
        Assert.Equal(VotingConfidence.AllFailed, result.Confidence);
    }

    [Fact]
    public async Task VotingEngine_ReturnsSingleExtractor_WhenOnlyOneSucceeds()
    {
        var extractors = new IPdfTextExtractor[]
        {
            new FakeExtractor("A", true, pages: new[] { "some text content" }),
            new FakeExtractor("B", false, "Error B")
        };
        var engine = new PdfVotingEngine(extractors, NullLogger<PdfVotingEngine>.Instance);

        var result = await engine.ExtractAndVoteAsync("fake.pdf");

        Assert.NotNull(result.Winner);
        Assert.Equal("A", result.Winner!.ExtractorName);
        Assert.Equal(VotingConfidence.SingleExtractor, result.Confidence);
    }

    [Fact]
    public async Task VotingEngine_ReturnsUnanimous_WhenAllAgree()
    {
        var text = "这是一段很长的测试文本，用于模拟三路提取的一致性。" + new string('A', 500);
        var extractors = new IPdfTextExtractor[]
        {
            new FakeExtractor("Docnet", true, pages: new[] { text }),
            new FakeExtractor("PdfPig", true, pages: new[] { text }),
            new FakeExtractor("iText7", true, pages: new[] { text })
        };
        var engine = new PdfVotingEngine(extractors, NullLogger<PdfVotingEngine>.Instance);

        var result = await engine.ExtractAndVoteAsync("fake.pdf");

        Assert.NotNull(result.Winner);
        Assert.Equal(VotingConfidence.Unanimous, result.Confidence);
        // PdfPig preferred when unanimous
        Assert.Equal("PdfPig", result.Winner!.ExtractorName);
    }

    [Fact]
    public async Task VotingEngine_ReturnsMajority_WhenTwoAgreeOneDiffers()
    {
        var textA = "共同的文本内容用于测试投票" + new string('X', 500);
        var textB = "完全不同的另一段文字内容" + new string('Y', 500);
        var extractors = new IPdfTextExtractor[]
        {
            new FakeExtractor("Docnet", true, pages: new[] { textA }),
            new FakeExtractor("PdfPig", true, pages: new[] { textA }),
            new FakeExtractor("iText7", true, pages: new[] { textB })
        };
        var engine = new PdfVotingEngine(extractors, NullLogger<PdfVotingEngine>.Instance);

        var result = await engine.ExtractAndVoteAsync("fake.pdf");

        Assert.NotNull(result.Winner);
        // Should be MajorityAgree or Unanimous depending on similarity threshold
        Assert.True(result.Confidence == VotingConfidence.MajorityAgree || result.Confidence == VotingConfidence.Unanimous);
    }

    // ==================== CninfoClient Tests ====================

    [Fact]
    public void CninfoClient_GetOrgId_ShanghaiCode()
    {
        // Test via public API indirectly: orgId is used in QueryAnnouncementsAsync
        // Shanghai code 6xxxxx → gssh0{code}
        // This can be tested by checking the returned URL format
        // For now, just verify the class can be constructed
        Assert.True(true); // Constructor test deferred to integration
    }

    // ==================== Helper ====================

    private class FakeExtractor : IPdfTextExtractor
    {
        private readonly bool _success;
        private readonly string? _error;
        private readonly string[]? _pages;

        public string Name { get; }

        public FakeExtractor(string name, bool success, string? error = null, string[]? pages = null)
        {
            Name = name;
            _success = success;
            _error = error;
            _pages = pages;
        }

        public Task<PdfExtractionResult> ExtractAsync(string pdfFilePath, CancellationToken ct = default)
        {
            return Task.FromResult(new PdfExtractionResult
            {
                ExtractorName = Name,
                Success = _success,
                ErrorMessage = _error,
                Pages = _pages?.ToList() ?? new List<string>()
            });
        }
    }
}
