using SimplerJiangAiAgent.Api.Modules.Market.Models;

namespace SimplerJiangAiAgent.Api.Modules.Market.Services;

public interface IEastmoneyRealtimeMarketClient
{
    Task<IReadOnlyList<BatchStockQuoteDto>> GetBatchQuotesAsync(IReadOnlyList<string> symbols, CancellationToken cancellationToken = default);
    Task<MarketCapitalFlowSnapshotDto?> GetMainCapitalFlowAsync(CancellationToken cancellationToken = default);
    Task<NorthboundFlowSnapshotDto?> GetNorthboundFlowAsync(CancellationToken cancellationToken = default);
    Task<MarketBreadthDistributionDto?> GetBreadthDistributionAsync(CancellationToken cancellationToken = default);
}