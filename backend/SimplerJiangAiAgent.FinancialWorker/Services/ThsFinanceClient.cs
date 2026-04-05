using System.Text.Json;
using SimplerJiangAiAgent.FinancialWorker.Models;

namespace SimplerJiangAiAgent.FinancialWorker.Services;

/// <summary>
/// 从同花顺 basic API 采集三大报表（第三降级通道）
/// </summary>
public class ThsFinanceClient
{
    private readonly HttpClient _http;
    private readonly ILogger<ThsFinanceClient> _logger;

    private const string BaseUrl = "https://basic.10jqka.com.cn/api/stock/finance";
    private const int DefaultMaxPeriods = 20;

    public ThsFinanceClient(HttpClient http, ILogger<ThsFinanceClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// 并行拉取三大报表并按 reportDate 合并
    /// </summary>
    public async Task<List<FinancialReport>> FetchFinancialReportsAsync(
        string symbol, int maxPeriods = DefaultMaxPeriods, CancellationToken ct = default)
    {
        var debtUrl = $"{BaseUrl}/{symbol}_debt.json";
        var benefitUrl = $"{BaseUrl}/{symbol}_benefit.json";
        var cashUrl = $"{BaseUrl}/{symbol}_cash.json";

        var debtTask = FetchFlashDataAsync(debtUrl, maxPeriods, ct);
        var benefitTask = FetchFlashDataAsync(benefitUrl, maxPeriods, ct);
        var cashTask = FetchFlashDataAsync(cashUrl, maxPeriods, ct);

        await Task.WhenAll(debtTask, benefitTask, cashTask);

        var debtData = debtTask.Result;
        var benefitData = benefitTask.Result;
        var cashData = cashTask.Result;

        if (debtData.Count == 0 && benefitData.Count == 0 && cashData.Count == 0)
        {
            _logger.LogInformation("THS: no financial report data returned for {Symbol}", symbol);
            return [];
        }

        // 合并所有报告期
        var allDates = new HashSet<string>(debtData.Keys);
        foreach (var k in benefitData.Keys) allDates.Add(k);
        foreach (var k in cashData.Keys) allDates.Add(k);

        var results = new List<FinancialReport>();
        foreach (var date in allDates.OrderByDescending(d => d))
        {
            var report = new FinancialReport
            {
                Symbol = symbol,
                ReportDate = date,
                ReportType = FinanceClientHelper.ParseReportType(date),
                CompanyType = 4,
                SourceChannel = "ths",
                CollectedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            if (debtData.TryGetValue(date, out var bs))
                report.BalanceSheet = bs;
            if (benefitData.TryGetValue(date, out var inc))
                report.IncomeStatement = inc;
            if (cashData.TryGetValue(date, out var cf))
                report.CashFlow = cf;

            results.Add(report);
        }

        _logger.LogInformation("THS: fetched {Count} financial reports for {Symbol}", results.Count, symbol);
        return results;
    }

    // ─── 内部 ─────────────────────────────────────────────────────

    private async Task<Dictionary<string, Dictionary<string, object?>>> FetchFlashDataAsync(
        string url, int maxPeriods, CancellationToken ct)
    {
        var empty = new Dictionary<string, Dictionary<string, object?>>();
        try
        {
            using var response = await _http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("THS: {Url} returned Content-Type '{ContentType}', expected JSON", url, contentType);
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var outerDoc = JsonDocument.Parse(json);

            // BUG-1 fix: flashData is a JSON string, needs double-parse
            if (!outerDoc.RootElement.TryGetProperty("flashData", out var flashEl) ||
                flashEl.ValueKind != JsonValueKind.String)
            {
                _logger.LogWarning("THS: flashData missing or not a string in response from {Url}", url);
                return empty;
            }

            var flashJson = flashEl.GetString();
            if (string.IsNullOrEmpty(flashJson))
            {
                _logger.LogWarning("THS: flashData is empty string from {Url}", url);
                return empty;
            }

            using var innerDoc = JsonDocument.Parse(flashJson);
            var root = innerDoc.RootElement;

            // --- title ---
            if (!root.TryGetProperty("title", out var titleEl) || titleEl.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("THS: title missing from flashData in {Url}", url);
                return empty;
            }

            // BUG-2 fix: title[0] is string header, title[1..N] can be string or array
            var titleCount = titleEl.GetArrayLength();
            var fieldNames = new string[titleCount]; // index 0 unused
            for (var i = 1; i < titleCount; i++)
            {
                var t = titleEl[i];
                fieldNames[i] = t.ValueKind == JsonValueKind.Array
                    ? (t[0].GetString() ?? "")
                    : (t.GetString() ?? "");
            }

            // --- report ---
            if (!root.TryGetProperty("report", out var reportEl) || reportEl.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("THS: report missing from flashData in {Url}", url);
                return empty;
            }

            // BUG-4 fix: dates are in report[0][], not a separate reportDate field
            var reportArr = reportEl;
            if (reportArr.GetArrayLength() < 2)
            {
                _logger.LogWarning("THS: report array too short from {Url}", url);
                return empty;
            }

            var dateRow = reportArr[0];
            if (dateRow.ValueKind != JsonValueKind.Array || dateRow.GetArrayLength() == 0)
            {
                _logger.LogWarning("THS: report[0] (dates) missing or empty from {Url}", url);
                return empty;
            }

            // BUG-5 fix: YoY field is report_yoy, not yoy
            JsonElement? yoyArr = null;
            if (root.TryGetProperty("report_yoy", out var yoyEl) && yoyEl.ValueKind == JsonValueKind.Array)
                yoyArr = yoyEl;

            // BUG-3 fix: data is transposed — report[fieldIdx][periodIdx]
            var periodCount = Math.Min(dateRow.GetArrayLength(), maxPeriods);
            var fieldCount = reportArr.GetArrayLength(); // includes index 0 (dates)

            var result = new Dictionary<string, Dictionary<string, object?>>();
            for (var periodIdx = 0; periodIdx < periodCount; periodIdx++)
            {
                var date = dateRow[periodIdx].GetString();
                if (string.IsNullOrEmpty(date)) continue;

                var dict = new Dictionary<string, object?>();

                for (var fieldIdx = 1; fieldIdx < fieldCount && fieldIdx < titleCount; fieldIdx++)
                {
                    var name = fieldNames[fieldIdx];
                    if (string.IsNullOrEmpty(name)) continue;

                    var fieldRow = reportArr[fieldIdx];
                    if (fieldRow.ValueKind == JsonValueKind.Array && periodIdx < fieldRow.GetArrayLength())
                    {
                        dict[name] = FinanceClientHelper.ParseChineseNumber(ParseJsonValue(fieldRow[periodIdx]));
                    }

                    // Append YoY
                    if (yoyArr.HasValue && fieldIdx < yoyArr.Value.GetArrayLength())
                    {
                        var yoyFieldRow = yoyArr.Value[fieldIdx];
                        if (yoyFieldRow.ValueKind == JsonValueKind.Array && periodIdx < yoyFieldRow.GetArrayLength())
                        {
                            dict[$"{name}_同比"] = FinanceClientHelper.ParseChineseNumber(ParseJsonValue(yoyFieldRow[periodIdx]));
                        }
                    }
                }

                result[date] = dict;
            }

            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "THS: JSON parse error from {Url}", url);
            return empty;
        }
        // HttpRequestException 向上抛，让调用方感知网络层失败
    }

    private static string ConvertDateFormat(string date)
    {
        // "20250930" → "2025-09-30"
        if (date.Length == 8 &&
            int.TryParse(date.AsSpan(0, 4), out _) &&
            int.TryParse(date.AsSpan(4, 2), out _) &&
            int.TryParse(date.AsSpan(6, 2), out _))
        {
            return $"{date[..4]}-{date[4..6]}-{date[6..8]}";
        }

        return date; // 已经是标准格式或不可识别，原样返回
    }

    private static object? ParseJsonValue(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.Number => el.GetDouble(),
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => el.ToString(),
        };
    }

}
