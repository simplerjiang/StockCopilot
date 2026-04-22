using SimplerJiangAiAgent.FinancialWorker.Models;

namespace SimplerJiangAiAgent.FinancialWorker.Services;

/// <summary>
/// Minimal interface for the THS finance client, exposing only methods used by
/// <see cref="FinancialDataOrchestrator"/>. Introduced for testability.
/// </summary>
public interface IThsFinanceClient
{
    Task<List<FinancialReport>> FetchFinancialReportsAsync(
        string symbol, int maxPeriods = 20, CancellationToken ct = default);
}
