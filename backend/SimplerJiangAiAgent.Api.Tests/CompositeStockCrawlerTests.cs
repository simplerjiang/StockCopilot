using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class CompositeStockCrawlerTests
{
    [Fact]
    public async Task GetQuoteAsync_ShouldPreferEastmoneyFundamentalsOverEarlierSources()
    {
        var crawlers = new IStockCrawlerSource[]
        {
            new FakeCrawlerSource(
                "腾讯",
                new StockQuoteDto(
                    "sz000021",
                    "深科技",
                    30.92m,
                    -0.33m,
                    -1.06m,
                    5.29m,
                    11.11m,
                    32.20m,
                    30.26m,
                    0.76m,
                    DateTime.UtcNow,
                    Array.Empty<StockNewsDto>(),
                    Array.Empty<StockIndicatorDto>(),
                    1m,
                    0m,
                    1,
                    "旧板块")),
            new FakeCrawlerSource(
                "东方财富",
                new StockQuoteDto(
                    "sz000021",
                    "深科技",
                    30.92m,
                    -0.33m,
                    -1.06m,
                    0m,
                    47.43m,
                    0m,
                    0m,
                    0m,
                    DateTime.UtcNow.AddSeconds(1),
                    Array.Empty<StockNewsDto>(),
                    Array.Empty<StockIndicatorDto>(),
                    48588590647.56m,
                    0m,
                    225861,
                    "消费电子"))
        };

        var crawler = new CompositeStockCrawler(crawlers);

        var quote = await crawler.GetQuoteAsync("sz000021");

        Assert.Equal(48588590647.56m, quote.FloatMarketCap);
        Assert.Equal(47.43m, quote.PeRatio);
        Assert.Equal(225861, quote.ShareholderCount);
        Assert.Equal("消费电子", quote.SectorName);
        Assert.Equal(30.92m, quote.Price);
        Assert.Equal(32.20m, quote.High);
        Assert.Equal(30.26m, quote.Low);
        Assert.Equal(5.29m, quote.TurnoverRate);
    }

    private sealed class FakeCrawlerSource : IStockCrawlerSource
    {
        private readonly StockQuoteDto _quote;

        public FakeCrawlerSource(string sourceName, StockQuoteDto quote)
        {
            SourceName = sourceName;
            _quote = quote;
        }

        public string SourceName { get; }

        public Task<StockQuoteDto> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_quote);
        }

        public Task<MarketIndexDto> GetMarketIndexAsync(string symbol, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new MarketIndexDto(symbol, symbol, 0m, 0m, 0m, DateTime.UtcNow));
        }

        public Task<IReadOnlyList<KLinePointDto>> GetKLineAsync(string symbol, string interval, int count, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<KLinePointDto>>(Array.Empty<KLinePointDto>());
        }

        public Task<IReadOnlyList<MinuteLinePointDto>> GetMinuteLineAsync(string symbol, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<MinuteLinePointDto>>(Array.Empty<MinuteLinePointDto>());
        }

        public Task<IReadOnlyList<IntradayMessageDto>> GetIntradayMessagesAsync(string symbol, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<IntradayMessageDto>>(Array.Empty<IntradayMessageDto>());
        }
    }
}