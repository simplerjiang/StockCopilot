using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public class EastmoneyStockParserTests
{
    [Fact]
    public void ParseQuote_ShouldScalePriceAndParseFundamentals()
    {
        var json = "{\"data\":{\"f58\":\"示例\",\"f43\":1103,\"f60\":1112,\"f170\":-81,\"f117\":341717900958.0,\"f162\":683,\"f10\":125}}";
        var quote = EastmoneyStockParser.ParseQuote("sh600000", json);

        Assert.Equal(11.03m, quote.Price);
        Assert.Equal(-0.81m, quote.ChangePercent);
        Assert.Equal(341717900958.0m, quote.FloatMarketCap);
        Assert.Equal(6.83m, quote.PeRatio);
        Assert.Equal(1.25m, quote.VolumeRatio);
    }

    [Fact]
    public void ParseTrends_ShouldParseLines()
    {
        var json = "{\"data\":{\"trends\":[\"2026-01-20 09:15,11.12,11.10,11.12,11.10,690,0.00,11.120\"]}}";
        var list = EastmoneyStockParser.ParseTrends("sh600000", json);

        Assert.Single(list);
        Assert.Equal(11.12m, list[0].Price);
    }

    [Fact]
    public void ParseIntradayMessages_ShouldBuildRecentEastmoneyTapeMessages()
    {
        var json = "{\"data\":{\"details\":[\"14:56:37,10.26,164,24,1\",\"14:56:40,10.27,298,27,2\"]}}";

        var list = EastmoneyStockParser.ParseIntradayMessages("sh600000", json, new DateTimeOffset(2026, 3, 14, 7, 0, 0, TimeSpan.Zero));

        Assert.Equal(2, list.Count);
        Assert.Equal("东方财富逐笔", list[0].Source);
        Assert.Contains("14:56:40 买盘 298手 @ 10.27", list[0].Title);
        Assert.Contains("14:56:37 卖盘 164手 @ 10.26", list[1].Title);
    }
}
