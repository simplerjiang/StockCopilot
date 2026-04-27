using SimplerJiangAiAgent.Api.Modules.Stocks;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class Batch4BugFixTests
{
    // ── #62: Phantom quote detection ──

    [Fact]
    public void IsPhantomQuote_AllZeroPriceWithEmptyName_ReturnsTrue()
    {
        var quote = new StockQuoteDto(
            Symbol: "sh999999",
            Name: "",
            Price: 0m,
            Change: 0m,
            ChangePercent: 0m,
            TurnoverRate: 0m,
            PeRatio: 0m,
            High: 0m,
            Low: 0m,
            Speed: 0m,
            Timestamp: DateTime.UtcNow,
            News: Array.Empty<StockNewsDto>(),
            Indicators: Array.Empty<StockIndicatorDto>());

        Assert.True(StocksModule.IsPhantomQuote(quote));
    }

    [Fact]
    public void IsPhantomQuote_AllZeroPriceWithSymbolEchoName_ReturnsTrue()
    {
        var quote = new StockQuoteDto(
            Symbol: "sh999999",
            Name: "999999",
            Price: 0m,
            Change: 0m,
            ChangePercent: 0m,
            TurnoverRate: 0m,
            PeRatio: 0m,
            High: 0m,
            Low: 0m,
            Speed: 0m,
            Timestamp: DateTime.UtcNow,
            News: Array.Empty<StockNewsDto>(),
            Indicators: Array.Empty<StockIndicatorDto>());

        Assert.True(StocksModule.IsPhantomQuote(quote));
    }

    [Fact]
    public void IsPhantomQuote_RealStockWithZeroPrice_ReturnsFalse_WhenNameIsReal()
    {
        // Suspended stock: price=0 but has a real company name
        var quote = new StockQuoteDto(
            Symbol: "sh600000",
            Name: "浦发银行",
            Price: 0m,
            Change: 0m,
            ChangePercent: 0m,
            TurnoverRate: 0m,
            PeRatio: 0m,
            High: 0m,
            Low: 0m,
            Speed: 0m,
            Timestamp: DateTime.UtcNow,
            News: Array.Empty<StockNewsDto>(),
            Indicators: Array.Empty<StockIndicatorDto>());

        Assert.False(StocksModule.IsPhantomQuote(quote));
    }

    [Fact]
    public void IsPhantomQuote_NormalStock_ReturnsFalse()
    {
        var quote = new StockQuoteDto(
            Symbol: "sh600519",
            Name: "贵州茅台",
            Price: 1800m,
            Change: 10m,
            ChangePercent: 0.56m,
            TurnoverRate: 0.3m,
            PeRatio: 30m,
            High: 1810m,
            Low: 1790m,
            Speed: 0.1m,
            Timestamp: DateTime.UtcNow,
            News: Array.Empty<StockNewsDto>(),
            Indicators: Array.Empty<StockIndicatorDto>());

        Assert.False(StocksModule.IsPhantomQuote(quote));
    }

    // ── #70: RAG future date filtering ──

    [Fact]
    public void IsFutureReportDate_TodayOrPast_ReturnsFalse()
    {
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8)).AddDays(-1);
        Assert.False(RagContextEnricher.IsFutureReportDate(yesterday.ToString("yyyy-MM-dd")));
    }

    [Fact]
    public void IsFutureReportDate_FutureDate_ReturnsTrue()
    {
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8)).AddDays(30);
        Assert.True(RagContextEnricher.IsFutureReportDate(futureDate.ToString("yyyy-MM-dd")));
    }

    [Fact]
    public void IsFutureReportDate_EmptyOrNull_ReturnsFalse()
    {
        Assert.False(RagContextEnricher.IsFutureReportDate(null));
        Assert.False(RagContextEnricher.IsFutureReportDate(""));
        Assert.False(RagContextEnricher.IsFutureReportDate("   "));
    }

    [Fact]
    public void IsFutureReportDate_InvalidFormat_ReturnsFalse()
    {
        Assert.False(RagContextEnricher.IsFutureReportDate("not-a-date"));
    }

    // ── #37: Garbage tag detection ──

    [Theory]
    [InlineData("无荒隔靶点", true)]
    [InlineData("N/A", true)]
    [InlineData("null", true)]
    [InlineData("undefined", true)]
    [InlineData("a", true)]
    [InlineData("", true)]
    [InlineData("政策红利", false)]
    [InlineData("贵州茅台", false)]
    [InlineData("板块轮动", false)]
    public void IsGarbageTag_DetectsKnownGarbagePatterns(string tag, bool expected)
    {
        Assert.Equal(expected, LocalFactAiTargetPolicy.IsGarbageTag(tag));
    }
}
