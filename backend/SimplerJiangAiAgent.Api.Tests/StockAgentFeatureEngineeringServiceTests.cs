using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class StockAgentFeatureEngineeringServiceTests
{
    [Fact]
    public void Prepare_ShouldFilterMarketNoiseAndComputeDeterministicFeatures()
    {
        var service = new StockAgentFeatureEngineeringService();
        var quote = new StockQuoteDto(
            "sh600000",
            "浦发银行",
            10.5m,
            0.2m,
            1.9m,
            13.2m,
            8.6m,
            10.8m,
            10.2m,
            0.1m,
            new DateTime(2026, 3, 21, 10, 0, 0),
            Array.Empty<StockNewsDto>(),
            Array.Empty<StockIndicatorDto>(),
            320_000_000_000m,
            3.4m,
            120000,
            "银行");
        var kLines = Enumerable.Range(0, 25)
            .Select(index => new KLinePointDto(new DateTime(2026, 2, 20).AddDays(index), 9m + index * 0.03m, 9.1m + index * 0.03m, 9.3m + index * 0.03m, 8.9m + index * 0.03m, 1000 + index * 10))
            .ToArray();
        var minuteLines = new[]
        {
            new MinuteLinePointDto(new DateOnly(2026, 3, 21), new TimeSpan(9, 30, 0), 10.20m, 10.20m, 100),
            new MinuteLinePointDto(new DateOnly(2026, 3, 21), new TimeSpan(10, 0, 0), 10.35m, 10.28m, 160),
            new MinuteLinePointDto(new DateOnly(2026, 3, 21), new TimeSpan(14, 30, 0), 10.48m, 10.36m, 210)
        };
        var messages = new[]
        {
            new IntradayMessageDto("浦发银行公告", "上交所公告", new DateTime(2026, 3, 21, 8, 30, 0), "https://example.com/a")
        };
        var localFacts = new StockAgentLocalFactPackageDto(
            "sh600000",
            "浦发银行",
            "银行",
            new[]
            {
                new StockAgentLocalNewsItemDto(1, "stock_news:1", "浦发银行公告", null, "上交所公告", "announcement", "announcement", "利好", new DateTime(2026, 3, 21, 8, 30, 0), new DateTime(2026, 3, 21, 8, 31, 0), "https://example.com/a", "公告摘要", "公告摘要", "url_fetched", "full_text_read", new DateTime(2026, 3, 21, 8, 31, 0), "个股:浦发银行", new[] { "公告" })
            },
            Array.Empty<StockAgentLocalNewsItemDto>(),
            new[]
            {
                new StockAgentLocalNewsItemDto(2, "market:1", "美股 Tesla 大涨", null, "Seeking Alpha", "seeking-alpha", "market", "中性", new DateTime(2026, 3, 21, 7, 0, 0), new DateTime(2026, 3, 21, 7, 5, 0), "https://example.com/noise", null, null, "title_only", "title_only", new DateTime(2026, 3, 21, 7, 5, 0), null, new[] { "美股个股" })
            },
            new DateTime(2026, 3, 21, 7, 0, 0),
            new[] { new StockAgentLocalFundamentalFactDto("机构目标价", "11.50", "东方财富") });

        var prepared = service.Prepare(
            "sh600000",
            quote,
            kLines,
            minuteLines,
            messages,
            new StockAgentNewsPolicyDto(72, 72, false, 1, 1),
            localFacts,
            new DateTime(2026, 3, 21, 10, 0, 0));

        Assert.Empty(prepared.LocalFacts.MarketReports);
        Assert.Equal(1, prepared.Features.MarketNoiseFilteredCount);
        Assert.True(prepared.Features.Evidence.CoverageScore > 0m);
        Assert.Equal("上涨", prepared.Features.Trend.TrendState);
        Assert.True(prepared.Features.Trend.Vwap > 0m);
        Assert.Contains("market_noise_filtered", prepared.Features.DegradedFlags);
    }
}