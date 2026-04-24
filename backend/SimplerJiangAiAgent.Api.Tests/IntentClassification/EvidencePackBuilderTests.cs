using Microsoft.Extensions.Logging.Abstractions;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services.IntentClassification;
using Xunit;

namespace SimplerJiangAiAgent.Api.Tests.IntentClassification;

public class EvidencePackBuilderTests
{
    [Fact]
    public void FormatAsPromptContext_WithRagChunks_IncludesFinancialReportSection()
    {
        var pack = new EvidencePack(
            "600519", "营收增长分析", IntentType.FinancialAnalysis,
            RagChunks: new List<RagCitationDto>
            {
                new() { ReportDate = "2025-12-31", ReportType = "年报", Section = "营业收入", Text = "公司实现营业收入1720亿元", Score = 0.95 }
            },
            FinancialMetrics: new List<FinancialMetricSummary>
            {
                new("2025-12-31", "年报", "ths", new Dictionary<string, object?> { ["TotalRevenue"] = 172054000000L })
            },
            LocalFacts: new LocalFactSummary(3, 1, 2, new[] { "[04-20] 贵州茅台Q1业绩预告" }),
            DegradedSources: new List<string>()
        );

        var builder = new EvidencePackBuilder(null!, null!, null!, NullLogger<EvidencePackBuilder>.Instance);
        var context = builder.FormatAsPromptContext(pack);

        Assert.Contains("财报原文摘录", context);
        Assert.Contains("结构化财务指标", context);
        Assert.Contains("近期新闻摘要", context);
        Assert.Contains("172054000000", context);
    }

    [Fact]
    public void FormatAsPromptContext_WithDegradedSources_ShowsWarning()
    {
        var pack = new EvidencePack(
            "600519", "test", IntentType.Valuation,
            new List<RagCitationDto>(),
            new List<FinancialMetricSummary>(),
            null,
            new List<string> { "RAG", "FinancialReport" }
        );

        var builder = new EvidencePackBuilder(null!, null!, null!, NullLogger<EvidencePackBuilder>.Instance);
        var context = builder.FormatAsPromptContext(pack);

        Assert.Contains("不可用", context);
        Assert.Contains("RAG", context);
    }

    [Fact]
    public void EvidencePack_HasRagEvidence_TrueWhenChunksExist()
    {
        var pack = new EvidencePack("600519", "test", IntentType.Valuation,
            new List<RagCitationDto> { new() { Text = "chunk" } },
            new List<FinancialMetricSummary>(), null, new List<string>());
        Assert.True(pack.HasRagEvidence);
    }

    [Fact]
    public void EvidencePack_HasRagEvidence_FalseWhenEmpty()
    {
        var pack = new EvidencePack("600519", "test", IntentType.Valuation,
            new List<RagCitationDto>(), new List<FinancialMetricSummary>(), null, new List<string>());
        Assert.False(pack.HasRagEvidence);
    }

    [Fact]
    public void IntentRoutingTable_ValuationRequiresRag()
    {
        var rule = IntentRoutingTable.GetRule(IntentType.Valuation);
        Assert.True(rule.RequiresRag);
        Assert.True(rule.RequiresFinancialData);
    }
}
