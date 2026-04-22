using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using SimplerJiangAiAgent.FinancialWorker.Models;

namespace SimplerJiangAiAgent.FinancialWorker.Services;

/// <summary>
/// 从 Eastmoney emweb API 采集三大报表 + 财务指标
/// </summary>
public class EastmoneyFinanceClient : IEastmoneyFinanceClient
{
    private readonly HttpClient _http;
    private readonly ILogger<EastmoneyFinanceClient> _logger;
    private readonly ConcurrentDictionary<string, int> _companyTypeCache = new();

    private const string BaseUrl = "https://emweb.securities.eastmoney.com/PC_HSF10/NewFinanceAnalysis";

    public EastmoneyFinanceClient(HttpClient http, ILogger<EastmoneyFinanceClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    // ─── CompanyType 检测 ───────────────────────────────────────────

    /// <summary>
    /// 检测公司类型。4=一般企业, 1=银行, 2=保险, 3=券商
    /// </summary>
    public async Task<int> DetectCompanyTypeAsync(string symbol, CancellationToken ct = default)
    {
        if (_companyTypeCache.TryGetValue(symbol, out var cached))
            return cached;

        try
        {
            var prefix = GetMarketPrefixLower(symbol);
            var url = $"{BaseUrl}/Index?type=web&code={prefix}{symbol}";
            var html = await _http.GetStringAsync(url, ct);

            // <input id="hidctype" ... value="4" ...>
            var match = Regex.Match(html, @"id\s*=\s*[""']hidctype[""'][^>]*value\s*=\s*[""'](\d+)[""']",
                RegexOptions.IgnoreCase);

            var companyType = match.Success && int.TryParse(match.Groups[1].Value, out var v) ? v : 4;
            _companyTypeCache[symbol] = companyType;
            _logger.LogDebug("CompanyType for {Symbol}: {Type}", symbol, companyType);
            return companyType;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DetectCompanyType failed for {Symbol}, defaulting to 4", symbol);
            _companyTypeCache[symbol] = 4;
            return 4;
        }
    }

    // ─── 三大报表 ─────────────────────────────────────────────────

    /// <summary>
    /// 并行拉取三大报表并按 REPORT_DATE 合并
    /// </summary>
    public async Task<List<FinancialReport>> FetchFinancialReportsAsync(
        string symbol, int companyType, string? endDate = null, CancellationToken ct = default)
    {
        var prefix = GetMarketPrefix(symbol);
        var code = $"{prefix}{symbol}";
        var datePart = string.IsNullOrEmpty(endDate) ? "" : endDate;

        var balanceUrl = $"{BaseUrl}/zcfzbAjaxNew?companyType={companyType}&reportDateType=0&reportType=1&endDate={datePart}&code={code}";
        var incomeUrl = $"{BaseUrl}/lrbAjaxNew?companyType={companyType}&reportDateType=0&reportType=1&endDate={datePart}&code={code}";
        var cashFlowUrl = $"{BaseUrl}/xjllbAjaxNew?companyType={companyType}&reportDateType=0&reportType=1&endDate={datePart}&code={code}";

        // 并行请求三表
        var balanceTask = FetchDataArrayAsync(balanceUrl, ct);
        var incomeTask = FetchDataArrayAsync(incomeUrl, ct);
        var cashTask = FetchDataArrayAsync(cashFlowUrl, ct);

        await Task.WhenAll(balanceTask, incomeTask, cashTask);

        var balanceData = balanceTask.Result;   // date → dict
        var incomeData = incomeTask.Result;
        var cashData = cashTask.Result;

        if (balanceData.Count == 0 && incomeData.Count == 0 && cashData.Count == 0)
        {
            _logger.LogInformation("No financial report data returned for {Symbol}", symbol);
            return [];
        }

        // 以资产负债表报告期为基准合并
        var reportDates = balanceData.Count > 0
            ? balanceData.Keys.ToList()
            : incomeData.Count > 0
                ? incomeData.Keys.ToList()
                : cashData.Keys.ToList();

        var results = new List<FinancialReport>();
        foreach (var date in reportDates)
        {
            var report = new FinancialReport
            {
                Symbol = symbol,
                ReportDate = date,
                ReportType = ParseReportType(date),
                CompanyType = companyType,
                SourceChannel = "emweb",
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

        _logger.LogInformation("Fetched {Count} financial reports for {Symbol}", results.Count, symbol);
        return results;
    }

    // ─── 主要财务指标 ──────────────────────────────────────────────

    /// <summary>
    /// 拉取主要财务指标
    /// </summary>
    public async Task<List<FinancialIndicator>> FetchIndicatorsAsync(
        string symbol, CancellationToken ct = default)
    {
        var prefix = GetMarketPrefix(symbol);
        var url = $"{BaseUrl}/ZYZBAjaxNew?type=0&code={prefix}{symbol}";

        var dataByDate = await FetchDataArrayAsync(url, ct);
        if (dataByDate.Count == 0)
        {
            _logger.LogInformation("No indicator data returned for {Symbol}", symbol);
            return [];
        }

        var results = new List<FinancialIndicator>();
        foreach (var (date, metrics) in dataByDate)
        {
            results.Add(new FinancialIndicator
            {
                Symbol = symbol,
                ReportDate = date,
                Metrics = metrics,
                SourceChannel = "emweb",
                CollectedAt = DateTime.UtcNow,
            });
        }

        _logger.LogInformation("Fetched {Count} indicator periods for {Symbol}", results.Count, symbol);
        return results;
    }

    // ─── 辅助方法（委托到 FinanceClientHelper）────────────────────

    /// <summary>6开头 = SH, 0/3开头 = SZ</summary>
    public static string GetMarketPrefix(string symbol) =>
        FinanceClientHelper.GetMarketPrefix(symbol);

    /// <summary>小写前缀，用于 HTML 页面 URL</summary>
    public static string GetMarketPrefixLower(string symbol) =>
        FinanceClientHelper.GetMarketPrefixLower(symbol);

    /// <summary>根据报告期月份判断报告类型</summary>
    public static string ParseReportType(string reportDate) =>
        FinanceClientHelper.ParseReportType(reportDate);

    /// <summary>将 JsonElement 对象的所有属性展平为字典</summary>
    public static Dictionary<string, object?> ParseJsonToDict(JsonElement element) =>
        FinanceClientHelper.ParseJsonToDict(element);

    // ─── 内部 ─────────────────────────────────────────────────────

    /// <summary>
    /// 请求一个 API，解析 data[] 数组，返回 date → dict 映射
    /// </summary>
    private async Task<Dictionary<string, Dictionary<string, object?>>> FetchDataArrayAsync(
        string url, CancellationToken ct)
    {
        var result = new Dictionary<string, Dictionary<string, object?>>();
        try
        {
            using var response = await _http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Eastmoney CDN blocked: {Url} returned Content-Type '{ContentType}', likely 302 redirect to anti-crawl page",
                    url, contentType);
                return result;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("data", out var dataEl) ||
                dataEl.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

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
            _logger.LogWarning(ex, "HTTP error fetching {Url}", url);
            throw; // 5xx 等由调用者处理降级
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "JSON parse error for {Url}", url);
            // 返回空
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Request timed out for {Url}", url);
            throw;
        }

        return result;
    }
}
