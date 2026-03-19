using SimplerJiangAiAgent.Api.Modules.Market.Models;

namespace SimplerJiangAiAgent.Api.Modules.Market.Services;

public interface IRealtimeMarketOverviewService
{
    Task<IReadOnlyList<BatchStockQuoteDto>> GetBatchQuotesAsync(IReadOnlyList<string> symbols, CancellationToken cancellationToken = default);
    Task<MarketRealtimeOverviewDto> GetOverviewAsync(IReadOnlyList<string>? indexSymbols = null, CancellationToken cancellationToken = default);
}