using SimplerJiangAiAgent.FinancialWorker.Models;

namespace SimplerJiangAiAgent.FinancialWorker.Services;

/// <summary>
/// Minimal interface for the Eastmoney emweb client, exposing only methods used by
/// <see cref="FinancialDataOrchestrator"/>. Introduced for testability.
/// </summary>
public interface IEastmoneyFinanceClient
{
    Task<int> DetectCompanyTypeAsync(string symbol, CancellationToken ct = default);

    Task<List<FinancialReport>> FetchFinancialReportsAsync(
        string symbol, int companyType, string? endDate = null, CancellationToken ct = default);

    Task<List<FinancialIndicator>> FetchIndicatorsAsync(
        string symbol, CancellationToken ct = default);
}
