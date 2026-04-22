using System.Text.Json;
using SimplerJiangAiAgent.FinancialWorker.Models;

namespace SimplerJiangAiAgent.FinancialWorker.Services;

/// <summary>
/// 从 Eastmoney datacenter API 采集三大报表（emweb CDN 拦截时的第一备用通道）
/// </summary>
public class EastmoneyDatacenterClient : IEastmoneyDatacenterClient
{
    private readonly HttpClient _http;
    private readonly ILogger<EastmoneyDatacenterClient> _logger;

    private const string BaseUrl = "https://datacenter-web.eastmoney.com/api/data/v1/get";

    private static readonly Dictionary<string, string> ReportNames = new()
    {
        ["BALANCE"] = "RPT_DMSK_FN_BALANCE",
        ["INCOME"] = "RPT_DMSK_FN_INCOME",
        ["CASHFLOW"] = "RPT_DMSK_FN_CASHFLOW",
    };

    public EastmoneyDatacenterClient(HttpClient http, ILogger<EastmoneyDatacenterClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// 并行拉取三大报表并按 REPORT_DATE 合并
    /// </summary>
    public async Task<List<FinancialReport>> FetchFinancialReportsAsync(
        string symbol, DateTime? startDate = null, CancellationToken ct = default)
    {
        var start = startDate ?? new DateTime(2020, 1, 1);

        var balanceTask = FetchDataArrayAsync(ReportNames["BALANCE"], symbol, start, ct);
        var incomeTask = FetchDataArrayAsync(ReportNames["INCOME"], symbol, start, ct);
        var cashTask = FetchDataArrayAsync(ReportNames["CASHFLOW"], symbol, start, ct);

        await Task.WhenAll(balanceTask, incomeTask, cashTask);

        var balanceData = balanceTask.Result;
        var incomeData = incomeTask.Result;
        var cashData = cashTask.Result;

        if (balanceData.Count == 0 && incomeData.Count == 0 && cashData.Count == 0)
        {
            _logger.LogInformation("Datacenter: no financial report data returned for {Symbol}", symbol);
            return [];
        }

        // 合并所有报告期
        var allDates = new HashSet<string>(balanceData.Keys);
        foreach (var k in incomeData.Keys) allDates.Add(k);
        foreach (var k in cashData.Keys) allDates.Add(k);

        var results = new List<FinancialReport>();
        foreach (var date in allDates.OrderByDescending(d => d))
        {
            var report = new FinancialReport
            {
                Symbol = symbol,
                ReportDate = date,
                ReportType = FinanceClientHelper.ParseReportType(date),
                CompanyType = 4, // datacenter 不区分公司类型
                SourceChannel = "datacenter",
                CollectedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            if (balanceData.TryGetValue(date, out var bs))
                report.BalanceSheet = bs;
            if (incomeData.TryGetValue(date, out var inc))
                report.IncomeStatement = inc;
            if (cashData.TryGetValue(date, out var cf))
                report.CashFlow = cf;

            results.Add(report);
        }

        _logger.LogInformation("Datacenter: fetched {Count} financial reports for {Symbol}", results.Count, symbol);
        return results;
    }

    /// <summary>采集分红送配数据</summary>
    public async Task<List<DividendRecord>> FetchDividendsAsync(
        string symbol, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}?sortColumns=REPORT_DATE&sortTypes=-1&pageSize=50&pageNumber=1" +
                  $"&reportName=RPT_SHAREBONUS_DET&columns=ALL" +
                  $"&filter=(SECURITY_CODE=%22{symbol}%22)";

        var dataArray = await FetchRawDataArrayAsync(url, "RPT_SHAREBONUS_DET", ct);
        var results = new List<DividendRecord>();

        foreach (var item in dataArray)
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            var cashPer10 = GetNullableDecimal(item, "PRETAX_BONUS_RMB");
            var bonusPer10 = GetNullableDecimal(item, "BONUS_RATIO");
            var convertPer10 = GetNullableDecimal(item, "IT_RATIO");

            results.Add(new DividendRecord
            {
                Symbol = symbol,
                Plan = GetDateStr(item, "PLAN_NOTICE_DATE"),
                RecordDate = GetDateStr(item, "EQUITY_RECORD_DATE"),
                ExDividendDate = GetDateStr(item, "EX_DIVIDEND_DATE"),
                DividendPerShare = cashPer10 / 10m,
                BonusSharePerShare = bonusPer10 / 10m,
                ConvertedSharePerShare = convertPer10 / 10m,
                RawData = FinanceClientHelper.ParseJsonToDict(item),
                CollectedAt = DateTime.UtcNow,
            });
        }

        _logger.LogInformation("Datacenter: fetched {Count} dividend records for {Symbol}",
            results.Count, symbol);
        return results;
    }

    /// <summary>采集融资融券数据</summary>
    public async Task<List<MarginTradingRecord>> FetchMarginTradingAsync(
        string symbol, int pageSize = 50, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}?sortColumns=date&sortTypes=-1&pageSize={pageSize}&pageNumber=1" +
                  $"&reportName=RPTA_WEB_RZRQ_GGMX&columns=ALL" +
                  $"&filter=(SCODE=%22{symbol}%22)";

        var dataArray = await FetchRawDataArrayAsync(url, "RPTA_WEB_RZRQ_GGMX", ct);
        var results = new List<MarginTradingRecord>();

        foreach (var item in dataArray)
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            results.Add(new MarginTradingRecord
            {
                Symbol = symbol,
                TradeDate = GetDateStr(item, "DATE"),
                MarginBalance = GetNullableDecimal(item, "RZYE"),
                MarginBuy = GetNullableDecimal(item, "RZMRE"),
                MarginRepay = GetNullableDecimal(item, "RZCHE"),
                ShortSellingVolume = GetNullableDecimal(item, "RQYL"),
                ShortSellingSell = GetNullableDecimal(item, "RQMCL"),
                ShortSellingRepay = GetNullableDecimal(item, "RQCHL"),
                TotalBalance = GetNullableDecimal(item, "RZRQYE"),
                CollectedAt = DateTime.UtcNow,
            });
        }

        _logger.LogInformation("Datacenter: fetched {Count} margin trading records for {Symbol}",
            results.Count, symbol);
        return results;
    }

    // ─── 内部 ─────────────────────────────────────────────────────

    /// <summary>
    /// 请求 datacenter API，解析 result.data[] 数组，返回 date → dict 映射
    /// </summary>
    private async Task<Dictionary<string, Dictionary<string, object?>>> FetchDataArrayAsync(
        string reportName, string symbol, DateTime startDate, CancellationToken ct)
    {
        var result = new Dictionary<string, Dictionary<string, object?>>();

        var startStr = startDate.ToString("yyyy-MM-dd");
        var url = $"{BaseUrl}?sortColumns=REPORT_DATE&sortTypes=-1&pageSize=50&pageNumber=1" +
                  $"&reportName={reportName}&columns=ALL" +
                  $"&filter=(SECURITY_CODE=%22{symbol}%22)(REPORT_DATE>=%27{startStr}%27)";

        try
        {
            using var response = await _http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Datacenter CDN blocked: {Url} returned Content-Type '{ContentType}'",
                    url, contentType);
                return result;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            // 检查 success 和 code
            if (doc.RootElement.TryGetProperty("success", out var successEl) &&
                successEl.ValueKind == JsonValueKind.True)
            {
                // ok
            }
            else
            {
                var msg = doc.RootElement.TryGetProperty("message", out var msgEl)
                    ? msgEl.GetString() : "unknown";
                _logger.LogWarning("Datacenter API returned success!=true for {ReportName}, message={Msg}",
                    reportName, msg);
                return result;
            }

            if (doc.RootElement.TryGetProperty("code", out var codeEl) &&
                codeEl.TryGetInt32(out var code) && code != 0)
            {
                _logger.LogWarning("Datacenter API returned code={Code} for {ReportName}", code, reportName);
                return result;
            }

            // 读取 result.data 数组
            if (!doc.RootElement.TryGetProperty("result", out var resultEl) ||
                resultEl.ValueKind != JsonValueKind.Object)
                return result;

            if (!resultEl.TryGetProperty("data", out var dataEl) ||
                dataEl.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var item in dataEl.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                if (!item.TryGetProperty("REPORT_DATE", out var rdEl) ||
                    rdEl.ValueKind != JsonValueKind.String)
                    continue;

                var rawDate = rdEl.GetString();
                if (string.IsNullOrEmpty(rawDate) || rawDate.Length < 10)
                    continue;

                var date = rawDate[..10]; // "yyyy-MM-dd"
                var dict = FinanceClientHelper.ParseJsonToDict(item);
                result[date] = dict;
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "HTTP error fetching datacenter {ReportName} for {Symbol}", reportName, symbol);
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "JSON parse error for datacenter {ReportName}", reportName);
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Request timed out for datacenter {ReportName}", reportName);
            throw;
        }

        return result;
    }

    /// <summary>请求 datacenter API 并返回 result.data[] 中的 JsonElement 克隆列表</summary>
    private async Task<List<JsonElement>> FetchRawDataArrayAsync(
        string url, string reportLabel, CancellationToken ct)
    {
        var items = new List<JsonElement>();

        using var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Datacenter CDN blocked for {Report}: Content-Type '{CT}'",
                reportLabel, contentType);
            return items;
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("success", out var successEl) ||
            successEl.ValueKind != JsonValueKind.True)
        {
            _logger.LogWarning("Datacenter API success!=true for {Report}", reportLabel);
            return items;
        }

        if (!doc.RootElement.TryGetProperty("result", out var resultEl) ||
            resultEl.ValueKind != JsonValueKind.Object)
            return items;

        if (!resultEl.TryGetProperty("data", out var dataEl) ||
            dataEl.ValueKind != JsonValueKind.Array)
            return items;

        foreach (var item in dataEl.EnumerateArray())
            items.Add(item.Clone());

        return items;
    }

    private static string GetDateStr(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString();
            return s != null && s.Length >= 10 ? s[..10] : s ?? string.Empty;
        }
        return string.Empty;
    }

    private static decimal? GetNullableDecimal(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var v) &&
            v.ValueKind == JsonValueKind.Number &&
            v.TryGetDecimal(out var d))
            return d;
        return null;
    }

}
