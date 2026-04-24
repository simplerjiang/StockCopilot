using Microsoft.Extensions.Logging;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services.IntentClassification;
using Xunit;

namespace SimplerJiangAiAgent.Api.Tests.IntentClassification;

public class QuestionIntentClassifierRuleTests
{
    [Theory]
    [InlineData("这个股票预期估值多少", IntentType.Valuation)]
    [InlineData("贵不贵", IntentType.Valuation)]
    [InlineData("目标价是多少", IntentType.Valuation)]
    [InlineData("PE太高了吧", IntentType.Valuation)]
    [InlineData("风险在哪", IntentType.Risk)]
    [InlineData("会不会暴雷", IntentType.Risk)]
    [InlineData("商誉减值风险", IntentType.Risk)]
    [InlineData("财报怎么看", IntentType.FinancialAnalysis)]
    [InlineData("营收增长多少", IntentType.FinancialAnalysis)]
    [InlineData("ROE趋势", IntentType.FinancialAnalysis)]
    [InlineData("为什么涨停了", IntentType.PerformanceAttribution)]
    [InlineData("业绩变化原因", IntentType.PerformanceAttribution)]
    [InlineData("K线怎么看", IntentType.TechnicalAnalysis)]
    [InlineData("MACD金叉", IntentType.TechnicalAnalysis)]
    [InlineData("大盘怎么样", IntentType.MarketOverview)]
    [InlineData("板块轮动", IntentType.MarketOverview)]
    [InlineData("推荐什么股票", IntentType.StockPicking)]
    [InlineData("买什么好", IntentType.StockPicking)]
    [InlineData("hi", IntentType.Clarification)]
    public void RuleBasedClassification_MatchesExpectedIntent(string question, IntentType expectedType)
    {
        var classifier = CreateClassifier();
        var result = classifier.ClassifyByRules(question);
        Assert.NotNull(result);
        Assert.Equal(expectedType, result.Type);
        Assert.True(result.Confidence >= 0.8);
    }

    [Fact]
    public void ClassifyByRules_AmbiguousQuestion_ReturnsLowConfidence()
    {
        var classifier = CreateClassifier();
        var result = classifier.ClassifyByRules("这个公司怎么样");
        Assert.NotNull(result);
        Assert.True(result.Confidence < 0.8);
    }

    [Fact]
    public void RoutingTable_Valuation_RequiresRag()
    {
        var intent = IntentRoutingTable.Resolve(IntentType.Valuation, 0.9);
        Assert.True(intent.RequiresRag);
        Assert.True(intent.RequiresFinancialData);
        Assert.Equal(SuggestedPipeline.Research, intent.Pipeline);
    }

    [Fact]
    public void RoutingTable_TechnicalAnalysis_NoRag()
    {
        var intent = IntentRoutingTable.Resolve(IntentType.TechnicalAnalysis, 0.9);
        Assert.False(intent.RequiresRag);
        Assert.Equal(SuggestedPipeline.LiveGate, intent.Pipeline);
    }

    [Fact]
    public void RoutingTable_StockPicking_RecommendPipeline()
    {
        var intent = IntentRoutingTable.Resolve(IntentType.StockPicking, 0.9);
        Assert.Equal(SuggestedPipeline.Recommend, intent.Pipeline);
    }

    [Fact]
    public void RoutingTable_AllIntentTypes_HaveRules()
    {
        foreach (IntentType type in Enum.GetValues<IntentType>())
        {
            var rule = IntentRoutingTable.GetRule(type);
            Assert.NotNull(rule);
        }
    }

    [Fact]
    public void RoutingTable_Risk_RequiresRagAndFinancialData()
    {
        var intent = IntentRoutingTable.Resolve(IntentType.Risk, 0.9);
        Assert.True(intent.RequiresRag);
        Assert.True(intent.RequiresFinancialData);
        Assert.Equal(SuggestedPipeline.Research, intent.Pipeline);
    }

    [Fact]
    public void RoutingTable_Clarification_DirectReply()
    {
        var intent = IntentRoutingTable.Resolve(IntentType.Clarification, 0.9);
        Assert.False(intent.RequiresRag);
        Assert.Equal(SuggestedPipeline.DirectReply, intent.Pipeline);
    }

    [Fact]
    public void RoutingTable_PerformanceAttribution_RequiresRagNoFinancial()
    {
        var intent = IntentRoutingTable.Resolve(IntentType.PerformanceAttribution, 0.9);
        Assert.True(intent.RequiresRag);
        Assert.False(intent.RequiresFinancialData);
        Assert.Equal(SuggestedPipeline.Research, intent.Pipeline);
    }

    [Theory]
    [InlineData(IntentType.Valuation, true)]
    [InlineData(IntentType.Risk, true)]
    [InlineData(IntentType.FinancialAnalysis, true)]
    [InlineData(IntentType.PerformanceAttribution, true)]
    [InlineData(IntentType.TechnicalAnalysis, false)]
    [InlineData(IntentType.MarketOverview, false)]
    [InlineData(IntentType.StockPicking, false)]
    [InlineData(IntentType.General, false)]
    public void StructuredConclusionConstraint_AppliesToFinancialIntentsOnly(IntentType type, bool expectConstraint)
    {
        var rule = IntentRoutingTable.GetRule(type);
        if (expectConstraint)
        {
            Assert.True(rule.RequiresRag || rule.RequiresFinancialData,
                $"{type} should require evidence for structured conclusion");
        }
        else
        {
            Assert.False(rule.RequiresRag && rule.RequiresFinancialData,
                $"{type} should not require both RAG and financial data");
        }
    }

    private static QuestionIntentClassifier CreateClassifier()
    {
        var loggerFactory = LoggerFactory.Create(_ => { });
        var logger = loggerFactory.CreateLogger<QuestionIntentClassifier>();
        return new QuestionIntentClassifier(null!, logger);
    }
}
