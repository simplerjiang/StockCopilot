using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SimplerJiangAiAgent.FinancialWorker.Services;

public class CninfoClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CninfoClient> _logger;
    private readonly string _reportsDir;

    public CninfoClient(HttpClient httpClient, ILogger<CninfoClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        // Find repo root (same pattern as other services)
        var repoRoot = FindRepoRoot();
        _reportsDir = Path.Combine(repoRoot, "App_Data", "financial-reports");
    }

    /// <summary>
    /// 查询 cninfo 公告列表
    /// </summary>
    public async Task<List<CninfoAnnouncement>> QueryAnnouncementsAsync(
        string symbol, string category = "category_ndbg_szsh", int pageSize = 30, CancellationToken ct = default)
    {
        var orgId = GetOrgId(symbol);
        var plate = symbol.StartsWith("6") ? "sh" : "sz";
        var column = "szse";

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["stock"] = $"{symbol},{orgId}",
            ["tabName"] = "fulltext",
            ["pageSize"] = pageSize.ToString(),
            ["pageNum"] = "1",
            ["column"] = column,
            ["category"] = category,
            ["plate"] = plate,
            ["seDate"] = ""
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "http://www.cninfo.com.cn/new/hisAnnouncement/query")
        {
            Content = content
        };
        request.Headers.Add("Referer", "http://www.cninfo.com.cn/");
        request.Headers.Add("Accept", "application/json");

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);

        var results = new List<CninfoAnnouncement>();
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
                    results.Add(ann);
            }
        }

        _logger.LogInformation("cninfo 查询 {Symbol} 分类 {Category}: {Count} 条公告", symbol, category, results.Count);
        return results;
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
        try
        {
            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length < 1024) // PDF too small, likely an error page
            {
                _logger.LogWarning("PDF 文件过小 ({Size} bytes)，可能是错误页面: {Url}", bytes.Length, url);
                return null;
            }

            await File.WriteAllBytesAsync(filePath, bytes, ct);
            _logger.LogInformation("PDF 下载完成: {Path} ({Size:N0} bytes)", filePath, bytes.Length);
            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF 下载失败: {Url}", url);
            return null;
        }
    }

    /// <summary>
    /// 查询并下载最近的年报/半年报 PDF
    /// </summary>
    public async Task<List<DownloadedPdf>> DownloadRecentReportsAsync(
        string symbol, int maxCount = 5, CancellationToken ct = default)
    {
        var results = new List<DownloadedPdf>();

        // 年报
        var annuals = await QueryAnnouncementsAsync(symbol, "category_ndbg_szsh", maxCount, ct);
        // 半年报
        var semis = await QueryAnnouncementsAsync(symbol, "category_bndbg_szsh", maxCount, ct);

        var all = annuals.Concat(semis)
            .OrderByDescending(a => a.PublishTime)
            .Take(maxCount)
            .ToList();

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

        return results;
    }

    private static string GetOrgId(string code)
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

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            if (Directory.GetFiles(dir, "*.sln").Length > 0 || Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..");
    }
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
