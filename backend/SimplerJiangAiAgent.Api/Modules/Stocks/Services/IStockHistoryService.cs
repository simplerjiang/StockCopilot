using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface IStockHistoryService
{
    Task UpsertAsync(StockQuoteDto quote, CancellationToken cancellationToken = default);
    Task<StockQueryHistory?> RecordAsync(StockHistoryRecordRequestDto request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockQueryHistory>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockQueryHistory>> RefreshAsync(string? source = null, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
}
