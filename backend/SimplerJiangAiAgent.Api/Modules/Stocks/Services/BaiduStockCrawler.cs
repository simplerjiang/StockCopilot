using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public sealed class BaiduStockCrawler : IStockCrawlerSource
{
    public string SourceName => "百度";

    public Task<StockQuoteDto?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        // TODO: 接入公开接口并解析数据
        var quote = new StockQuoteDto(
            symbol,
            string.Empty,
            0m,
            0m,
            0m,
            0m,
            0m,
            0m,
            0m,
            0m,
            DateTime.UtcNow,
            Array.Empty<StockNewsDto>(),
            Array.Empty<StockIndicatorDto>()
        );
        return Task.FromResult(quote);
    }

    public Task<MarketIndexDto> GetMarketIndexAsync(string symbol, CancellationToken cancellationToken = default)
    {
        return GetQuoteAsync(symbol, cancellationToken)
            .ContinueWith(task =>
            {
                var quote = task.Result;
                return new MarketIndexDto(
                    quote.Symbol,
                    quote.Name,
                    quote.Price,
                    quote.Change,
                    quote.ChangePercent,
                    quote.Timestamp
                );
            }, cancellationToken);
    }

    public Task<IReadOnlyList<KLinePointDto>> GetKLineAsync(string symbol, string interval, int count, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<KLinePointDto> result = Array.Empty<KLinePointDto>();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<MinuteLinePointDto>> GetMinuteLineAsync(string symbol, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MinuteLinePointDto> result = Array.Empty<MinuteLinePointDto>();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<IntradayMessageDto>> GetIntradayMessagesAsync(string symbol, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IntradayMessageDto> result = Array.Empty<IntradayMessageDto>();
        return Task.FromResult(result);
    }
}
