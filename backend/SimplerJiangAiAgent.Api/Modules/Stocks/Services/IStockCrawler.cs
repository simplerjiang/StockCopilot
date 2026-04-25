using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface IStockCrawler
{
    string SourceName { get; }

    // 获取股票行情与新闻/指标
    Task<StockQuoteDto?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default);

    // 获取大盘指数信息
    Task<MarketIndexDto> GetMarketIndexAsync(string symbol, CancellationToken cancellationToken = default);

    // 获取K线数据（interval: day/week/month）
    Task<IReadOnlyList<KLinePointDto>> GetKLineAsync(string symbol, string interval, int count, CancellationToken cancellationToken = default);

    // 获取分时数据
    Task<IReadOnlyList<MinuteLinePointDto>> GetMinuteLineAsync(string symbol, CancellationToken cancellationToken = default);

    // 获取盘中消息（当前占位）
    Task<IReadOnlyList<IntradayMessageDto>> GetIntradayMessagesAsync(string symbol, CancellationToken cancellationToken = default);
}
