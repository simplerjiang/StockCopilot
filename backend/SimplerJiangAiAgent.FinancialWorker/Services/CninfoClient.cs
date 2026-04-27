using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SimplerJiangAiAgent.FinancialWorker.Services;

public class CninfoClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CninfoClient> _logger;
    private readonly string _reportsDir;
    private readonly ConcurrentDictionary<string, string> _orgIdCache = new();

    public CninfoClient(HttpClient httpClient, ILogger<CninfoClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _reportsDir = FinancialWorkerRuntimePaths.ResolveFinancialReportsPath();
        Directory.CreateDirectory(_reportsDir);
    }

    /// <summary>
    /// 查询 cninfo 公告列表（自动翻页）
    /// </summary>
    public async Task<List<CninfoAnnouncement>> QueryAnnouncementsAsync(
        string symbol, string category = "category_ndbg_szsh", int pageSize = 30, CancellationToken ct = default,
        int maxTotal = 200)
    {
        var orgId = await ResolveOrgIdAsync(symbol, ct);
        var plate = symbol.StartsWith("6") ? "sh" : "sz";
        var column = symbol.StartsWith("6") ? "sse" : "szse";

        const int maxRetries = 3;
        var allResults = new List<CninfoAnnouncement>();
        int pageNum = 1;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            List<CninfoAnnouncement>? pageResults = null;
            int totalCount = -1;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["stock"] = $"{symbol},{orgId}",
                        ["tabName"] = "fulltext",
                        ["pageSize"] = pageSize.ToString(),
                        ["pageNum"] = pageNum.ToString(),
                        ["column"] = column,
                        ["category"] = category,
                        ["plate"] = plate,
                        ["seDate"] = ""
                    });

                    var request = new HttpRequestMessage(HttpMethod.Post, "http://www.cninfo.com.cn/new/hisAnnouncement/query")
                    {
                        Content = content
                    };
                    request.Headers.Add("Referer", "http://www.cninfo.com.cn/new/disclosure");
                    request.Headers.Add("Origin", "http://www.cninfo.com.cn");
                    request.Headers.Add("Accept", "application/json");

                    var response = await _httpClient.SendAsync(request, ct);
                    response.EnsureSuccessStatusCode();

                    var json = await response.Content.ReadAsStringAsync(ct);
                    var doc = JsonDocument.Parse(json);

                    // 解析总条数
                    if (doc.RootElement.TryGetProperty("totalAnnouncement", out var ta) && ta.ValueKind == JsonValueKind.Number)
                        totalCount = ta.GetInt32();
                    else if (doc.RootElement.TryGetProperty("totalRecordNum", out var trn) && trn.ValueKind == JsonValueKind.Number)
                        totalCount = trn.GetInt32();

                    pageResults = new List<CninfoAnnouncement>();
                    if (doc.RootElement.TryGetProperty("announcements", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in arr.EnumerateArray())
                        {
                            var ann = new CninfoAnnouncement
                            {
                                AnnouncementId = item.GetProperty("announcementId").GetString() ?? "",
                                AdjunctUrl = item.GetProperty("adjunctUrl").GetString() ?? "",
                                Title = item.GetProperty("announcementTitle").GetString() ?? "",
                                PublishTime = item.TryGetProperty("announcementTime", out var ts) && ts.ValueKind == JsonValueKind.Number
                                    ? DateTimeOffset.FromUnixTimeMilliseconds(ts.GetInt64()).DateTime
                                    : DateTime.MinValue
                            };
                            if (!string.IsNullOrEmpty(ann.AdjunctUrl))
                                pageResults.Add(ann);
                        }
                    }

                    break; // 成功，跳出 retry 循环
                }
                catch (Exception ex) when (attempt < maxRetries && !ct.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "cninfo 查询失败 (第 {Attempt}/{Max} 次): {Symbol} {Category} page={Page}",
                        attempt, maxRetries, symbol, category, pageNum);
                    await Task.Delay(1000 * attempt, ct);
                }
            }

            // retry 全部失败
            if (pageResults == null)
            {
                _logger.LogError("cninfo 查询最终失败: {Symbol} {Category} page={Page}，已重试 {Max} 次",
                    symbol, category, pageNum, maxRetries);
                break;
            }

            allResults.AddRange(pageResults);

            _logger.LogInformation("cninfo 查询 {Symbol} 分类 {Category} (column={Column}) page={Page}: 本页 {PageCount} 条, 累计 {Total} 条, 总计 {TotalCount}",
                symbol, category, column, pageNum, pageResults.Count, allResults.Count, totalCount);

            // 判断是否还需要翻页
            if (pageResults.Count == 0)
                break;
            if (totalCount >= 0 && allResults.Count >= totalCount)
                break;
            if (allResults.Count >= maxTotal)
                break;

            pageNum++;
            await Task.Delay(500, ct); // cninfo 频率限制
        }

        return allResults;
    }

    /// <summary>
    /// 下载 PDF 文件到本地
    /// </summary>
    public async Task<string?> DownloadPdfAsync(string symbol, CninfoAnnouncement announcement, CancellationToken ct = default)
    {
        var year = announcement.PublishTime.Year > 2000
            ? announcement.PublishTime.Year.ToString()
            : "unknown";
        var dir = Path.Combine(_reportsDir, symbol, year);
        Directory.CreateDirectory(dir);

        var fileName = SanitizeFileName(announcement.Title) + ".pdf";
        var filePath = Path.Combine(dir, fileName);

        if (File.Exists(filePath))
        {
            _logger.LogDebug("PDF 已存在，跳过下载: {Path}", filePath);
            return filePath;
        }

        var url = $"http://static.cninfo.com.cn/{announcement.AdjunctUrl}";

        const int maxRetries = 2;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Referer", "http://www.cninfo.com.cn/");
                request.Headers.Add("Accept", "*/*");

                var response = await _httpClient.SendAsync(request, ct);
                response.EnsureSuccessStatusCode();

                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                if (bytes.Length < 1024)
                {
                    _logger.LogWarning("PDF 文件过小 ({Size} bytes)，可能是错误页面: {Url}", bytes.Length, url);
                    return null;
                }

                await File.WriteAllBytesAsync(filePath, bytes, ct);
                _logger.LogInformation("PDF 下载完成: {Path} ({Size:N0} bytes)", filePath, bytes.Length);
                return filePath;
            }
            catch (Exception ex) when (attempt < maxRetries && !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "PDF 下载失败 (第 {Attempt}/{Max} 次): {Url}", attempt, maxRetries, url);
                await Task.Delay(1000 * attempt, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PDF 下载最终失败: {Url}", url);
                return null;
            }
        }
        return null;
    }

    /// <summary>
    /// 查询并下载最近的年报/半年报 PDF
    /// </summary>
    public async Task<List<DownloadedPdf>> DownloadRecentReportsAsync(
        string symbol, int maxCount = 5, CancellationToken ct = default)
    {
        var results = new List<DownloadedPdf>();

        // 年报
        var annuals = await QueryAnnouncementsAsync(symbol, "category_ndbg_szsh", ct: ct);
        // 半年报
        var semis = await QueryAnnouncementsAsync(symbol, "category_bndbg_szsh", ct: ct);
        // 一季报
        var q1 = await QueryAnnouncementsAsync(symbol, "category_yjdbg_szsh", ct: ct);
        // 三季报
        var q3 = await QueryAnnouncementsAsync(symbol, "category_sjdbg_szsh", ct: ct);

        var all = annuals.Concat(semis).Concat(q1).Concat(q3)
            .OrderByDescending(a => a.PublishTime)
            .Take(maxCount)
            .ToList();

        _logger.LogInformation("[cninfo] {Symbol}: 查询完成 — 年报 {Annual} 条, 半年报 {Semi} 条, 一季报 {Q1} 条, 三季报 {Q3} 条, 合并后取 top {Max}",
            symbol, annuals.Count, semis.Count, q1.Count, q3.Count, maxCount);

        foreach (var ann in all)
        {
            ct.ThrowIfCancellationRequested();
            var path = await DownloadPdfAsync(symbol, ann, ct);
            if (path != null)
            {
                results.Add(new DownloadedPdf
                {
                    FilePath = path,
                    Announcement = ann,
                    Symbol = symbol
                });
            }
            // Rate limit: cninfo may block frequent requests
            await Task.Delay(500, ct);
        }

        _logger.LogInformation("[cninfo] {Symbol}: 下载完成 — {Downloaded}/{Total} 成功",
            symbol, results.Count, all.Count);

        return results;
    }

    private async Task<string> ResolveOrgIdAsync(string code, CancellationToken ct)
    {
        if (_orgIdCache.TryGetValue(code, out var cached))
            return cached;

        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["keyWord"] = code
            });

            var request = new HttpRequestMessage(HttpMethod.Post,
                "http://www.cninfo.com.cn/new/information/topSearch/query")
            {
                Content = content
            };
            request.Headers.Add("Referer", "http://www.cninfo.com.cn/");
            request.Headers.Add("Accept", "application/json");

            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);

            // cninfo topSearch API 返回裸数组 [{code,orgId,...}]，也可能返回 {keyBoardList:[...]}
            var list = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement
                : doc.RootElement.TryGetProperty("keyBoardList", out var kbl) && kbl.ValueKind == JsonValueKind.Array
                    ? kbl
                    : (JsonElement?)null;

            if (list.HasValue)
            {
                foreach (var item in list.Value.EnumerateArray())
                {
                    if (item.TryGetProperty("code", out var codeProp) && codeProp.GetString() == code
                        && item.TryGetProperty("orgId", out var orgIdProp))
                    {
                        var orgId = orgIdProp.GetString() ?? GetDefaultOrgId(code);
                        _orgIdCache.TryAdd(code, orgId);
                        _logger.LogInformation("[cninfo] OrgId 动态解析: {Code} → {OrgId}", code, orgId);
                        return orgId;
                    }
                }
            }

            _logger.LogWarning("[cninfo] 未找到 {Code} 的 orgId，使用默认格式", code);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[cninfo] 动态获取 {Code} 的 orgId 失败，回退到默认格式", code);
        }

        var fallback = GetDefaultOrgId(code);
        _orgIdCache.TryAdd(code, fallback);
        return fallback;
    }

    private static string GetDefaultOrgId(string code)
    {
        // Shanghai (6xxx) → gssh0{code}, Shenzhen → gssz0{code}
        var prefix = code.StartsWith("6") ? "gssh0" : "gssz0";
        return $"{prefix}{code}";
    }

    private static string SanitizeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(title.Where(c => !invalid.Contains(c)).ToArray());
        return clean.Length > 100 ? clean[..100] : clean;
    }

}

internal static class FinancialWorkerRuntimePaths
{
    private const string DataRootEnvironmentVariable = "SJAI_DATA_ROOT";
    private const string ApplicationFolderName = "SimplerJiangAiAgent";

    public static string ResolveDataRoot()
    {
        var environmentOverride = Environment.GetEnvironmentVariable(DataRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentOverride))
        {
            return Path.GetFullPath(environmentOverride);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = AppContext.BaseDirectory;
        }

        return Path.Combine(localAppData, ApplicationFolderName);
    }

    public static string ResolveAppDataPath()
    {
        return Path.Combine(ResolveDataRoot(), "App_Data");
    }

    public static string ResolveFinancialDatabasePath()
    {
        return Path.Combine(ResolveAppDataPath(), "financial-data.db");
    }

    public static string ResolveFinancialReportsPath()
    {
        return Path.Combine(ResolveAppDataPath(), "financial-reports");
    }

    public static string ResolveRagDatabasePath() =>
        Path.Combine(ResolveAppDataPath(), "financial-rag.db");
}

public class CninfoAnnouncement
{
    public string AnnouncementId { get; set; } = "";
    public string AdjunctUrl { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime PublishTime { get; set; }
}

public class DownloadedPdf
{
    public string FilePath { get; set; } = "";
    public CninfoAnnouncement Announcement { get; set; } = new();
    public string Symbol { get; set; } = "";
}
