using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface IStockFundamentalSnapshotService
{
    Task<StockFundamentalSnapshotDto?> GetSnapshotAsync(string symbol, CancellationToken cancellationToken = default);
}
