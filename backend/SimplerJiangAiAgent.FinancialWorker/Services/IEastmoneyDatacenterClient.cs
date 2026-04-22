using SimplerJiangAiAgent.FinancialWorker.Models;

namespace SimplerJiangAiAgent.FinancialWorker.Services;

/// <summary>
/// Minimal interface for the Eastmoney datacenter client, exposing only methods used by
/// <see cref="FinancialDataOrchestrator"/>. Introduced for testability.
/// </summary>
public interface IEastmoneyDatacenterClient
{
    Task<List<FinancialReport>> FetchFinancialReportsAsync(
        string symbol, DateTime? startDate = null, CancellationToken ct = default);

    Task<List<DividendRecord>> FetchDividendsAsync(
        string symbol, CancellationToken ct = default);

    Task<List<MarginTradingRecord>> FetchMarginTradingAsync(
        string symbol, int pageSize = 50, CancellationToken ct = default);
}
