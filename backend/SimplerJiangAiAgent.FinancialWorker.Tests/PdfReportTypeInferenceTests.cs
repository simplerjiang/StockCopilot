using SimplerJiangAiAgent.FinancialWorker.Services.Pdf;

namespace SimplerJiangAiAgent.FinancialWorker.Tests;

/// <summary>
/// v0.4.2 R3 N2 修复：PdfProcessingPipeline.InferReportTypeFromFileName 单元测试。
/// User Rep R2 报告：年度报告 PDF 被错判为 Q1，导致 reportType=Annual 过滤返回 0 条。
/// 根因是 parsed.ReportType 依赖 PDF 文本第一个匹配日期，年报内 Q1 比较列污染推断。
/// 现在改成优先按文件名 / 公告标题里的中文关键词推断。
/// </summary>
public class PdfReportTypeInferenceTests
{
    [Theory]
    [InlineData("贵州茅台2024年年度报告.pdf", "Annual")]
    [InlineData("600519_2024_年报.pdf", "Annual")]
    [InlineData("600519-2024-Annual-Report.PDF", "Annual")]
    [InlineData("贵州茅台2024年半年度报告.pdf", "Semi")]
    [InlineData("600519_2024_半年报.pdf", "Semi")]
    [InlineData("600519_2024_中期报告.pdf", "Semi")]
    [InlineData("贵州茅台2024年第三季度报告.pdf", "Q3")]
    [InlineData("600519_2024_三季报.pdf", "Q3")]
    [InlineData("贵州茅台2024年第一季度报告.pdf", "Q1")]
    [InlineData("600519_2024_一季报.pdf", "Q1")]
    // v0.4.2 R3 BLOCKER2：摘要 / 英文版 PDF 仍是「年度报告」的派生形式，必须归 Annual。
    // 否则 reportType=Annual 过滤拿不到摘要，旧数据也会停留在错误的 Q1 / Unknown。
    [InlineData("贵州茅台2025年年度报告摘要.pdf", "Annual")]
    [InlineData("贵州茅台2025年年度报告（英文版）.pdf", "Annual")]
    [InlineData("贵州茅台2024年半年度报告摘要.pdf", "Semi")]
    [InlineData("600519_2024_Annual_Summary.pdf", "Annual")]
    public void InferReportTypeFromFileName_RecognisesKeywords(string fileName, string expected)
    {
        var result = PdfProcessingPipeline.InferReportTypeFromFileName(fileName);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("600519_2024.pdf")]              // 无关键词
    [InlineData("公司公告.pdf")]                  // 无关键词
    public void InferReportTypeFromFileName_NoKeywordReturnsNull(string? fileName)
    {
        var result = PdfProcessingPipeline.InferReportTypeFromFileName(fileName);
        Assert.Null(result);
    }

    [Fact]
    public void InferReportTypeFromFileName_AnnualBeatsQuarterly_WhenBothPresent()
    {
        // 年度报告应该优先匹配，避免被「Q1 比较列」式的次级关键词覆盖
        var result = PdfProcessingPipeline.InferReportTypeFromFileName("贵州茅台2024年年度报告(含一季度对比).pdf");
        Assert.Equal("Annual", result);
    }
}
