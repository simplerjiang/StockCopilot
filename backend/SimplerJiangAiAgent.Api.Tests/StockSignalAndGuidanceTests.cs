using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;
using SimplerJiangAiAgent.Api.Modules.Market.Models;
using Xunit;

namespace SimplerJiangAiAgent.Api.Tests;

public class StockSignalAndGuidanceTests
{
    [Fact]
    public void SignalService_BuildsEvidenceAndCounterEvidence()
    {
        var service = new StockSignalService();
        var quote = new StockQuoteDto(
            "sh600000",
            "浦发银行",
            10.2m,
            0.2m,
            1.5m,
            4.2m,
            6.8m,
            10.3m,
            9.9m,
            0.5m,
            DateTime.Now,
            Array.Empty<StockNewsDto>(),
            Array.Empty<StockIndicatorDto>());

        var kline = new List<KLinePointDto>
        {
            new(DateTime.Today.AddDays(-4), 9.6m, 9.8m, 9.9m, 9.5m, 1000),
            new(DateTime.Today.AddDays(-3), 9.8m, 10.0m, 10.1m, 9.7m, 1200),
            new(DateTime.Today.AddDays(-2), 10.0m, 10.1m, 10.2m, 9.9m, 1300),
            new(DateTime.Today.AddDays(-1), 10.1m, 10.15m, 10.2m, 10.0m, 1100),
            new(DateTime.Today, 10.15m, 10.2m, 10.3m, 10.1m, 1500)
        };

        var detail = new StockDetailDto(quote, kline, Array.Empty<MinuteLinePointDto>(), Array.Empty<IntradayMessageDto>());
        var impact = new StockNewsImpactDto(
            quote.Symbol,
            quote.Name,
            DateTime.Now,
            new StockNewsImpactSummaryDto(3, 1, 1, "利好偏多", 80, 35),
            new List<StockNewsImpactItemDto>
            {
                new("回购计划", "上交所", DateTime.Now, null, "公告", 1.25m, 1.2m, "股份回购", 2, "利好", 60, "x")
            });

        var result = service.Evaluate(detail, impact);

        Assert.NotEmpty(result.Signals);
        Assert.NotEmpty(result.Evidence);
        Assert.NotEmpty(result.CounterEvidence);
        Assert.InRange(result.Confidence, 0, 100);
    }

    [Fact]
    public void PositionGuidanceService_ReturnsTargetByRiskProfile()
    {
        var guidanceService = new StockPositionGuidanceService();
        var quote = new StockQuoteDto(
            "sh600000",
            "浦发银行",
            10.2m,
            0.2m,
            1.5m,
            4.2m,
            6.8m,
            10.3m,
            9.9m,
            0.5m,
            DateTime.Now,
            Array.Empty<StockNewsDto>(),
            Array.Empty<StockIndicatorDto>());

        var signal = new StockSignalDto(
            quote.Symbol,
            quote.Name,
            DateTime.Now,
            "偏多",
            78,
            60,
            55,
            70,
            new[] { "建议偏多" },
            new[] { "证据" },
            new[] { "反证" });

        var marketContext = new StockMarketContextDto(
            "主升",
            82m,
            "银行",
            "银行",
            "BKYH",
            76m,
            0.8m,
            "积极执行",
            false,
            true);

        var result = guidanceService.Build(quote, signal, "balanced", 30m, marketContext);

        Assert.Equal("balanced", result.RiskLevel);
        Assert.True(result.TargetPositionPercent >= 0 && result.TargetPositionPercent <= 100);
        Assert.Contains(result.Action, new[] { "加仓", "减仓", "持有" });
        Assert.NotEmpty(result.Reasons);
        Assert.Equal(0.8m, result.MarketStageMultiplier);
        Assert.NotNull(result.MarketContext);
    }
}
