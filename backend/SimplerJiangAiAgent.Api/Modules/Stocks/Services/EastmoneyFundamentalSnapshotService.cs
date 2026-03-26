using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public sealed class EastmoneyFundamentalSnapshotService : IStockFundamentalSnapshotService
{
    private readonly HttpClient _httpClient;

    public EastmoneyFundamentalSnapshotService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<StockFundamentalSnapshotDto?> GetSnapshotAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var normalized = StockSymbolNormalizer.Normalize(symbol);
        var marketPrefix = normalized.StartsWith("sh", StringComparison.OrdinalIgnoreCase) ? "SH" : "SZ";
        var code = normalized[2..];
        var surveyUrl = $"https://emweb.securities.eastmoney.com/PC_HSF10/CompanySurvey/CompanySurveyAjax?code={marketPrefix}{code}";
        var shareholderUrl = $"https://emweb.securities.eastmoney.com/PC_HSF10/ShareholderResearch/PageAjax?code={marketPrefix}{code}";
        var financeUrl = $"https://emweb.securities.eastmoney.com/PC_HSF10/NewFinanceAnalysis/ZYZBAjaxNew?type=0&code={marketPrefix}{code}";

        var surveyTask = TryGetStringAsync(surveyUrl, cancellationToken);
        var shareholderTask = TryGetStringAsync(shareholderUrl, cancellationToken);
        var financeTask = TryGetStringAsync(financeUrl, cancellationToken);
        await Task.WhenAll(surveyTask, shareholderTask, financeTask);

        var surveyJson = await surveyTask;
        if (string.IsNullOrWhiteSpace(surveyJson))
        {
            return null;
        }

        var dto = EastmoneyCompanyProfileParser.Parse(normalized, surveyJson, await shareholderTask);
        var financeJson = await financeTask;
        var allFacts = new List<StockFundamentalFactDto>(dto.Facts);
        
        if (!string.IsNullOrWhiteSpace(financeJson))
        {
            var financeFacts = EastmoneyCompanyProfileParser.ParseFinanceFacts(financeJson);
            allFacts.AddRange(financeFacts);
        }

        if (allFacts.Count == 0)
        {
            return null;
        }

        return new StockFundamentalSnapshotDto(DateTime.UtcNow, allFacts);
    }

    private async Task<string?> TryGetStringAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.GetStringAsync(url, cancellationToken);
        }
        catch
        {
            return null;
        }
    }
}
