using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace SimplerJiangAiAgent.FinancialWorker.Services.Announcement;

/// <summary>
/// 从东方财富公告列表爬取公告 PDF 文件。
/// 策略：先尝试直接构造 dfcfw PDF URL，不可用时解析 HTML 页面提取 PDF 链接。
/// </summary>
public class AnnouncementPdfCollector
{
    private static readonly TimeSpan RequestInterval = TimeSpan.FromMilliseconds(500);

    // 直接构造 PDF URL 的模板
    private const string DirectPdfUrlTemplate = "https://pdf.dfcfw.com/pdf/H2_{0}_1.pdf";

    // HTML 中提取 PDF URL 的正则
    private static readonly Regex PdfUrlInHtmlRegex = new(
        @"(?:href|src)\s*=\s*[""']?(https?://[^""'\s>]+\.pdf)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // JavaScript 变量中的 PDF URL
    private static readonly Regex PdfUrlInScriptRegex = new(
        @"[""'](https?://[^""']+\.pdf)[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly ILogger<AnnouncementPdfCollector> _logger;
    private readonly string _baseDir;

    public AnnouncementPdfCollector(HttpClient httpClient, ILogger<AnnouncementPdfCollector> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _baseDir = Path.Combine(FinancialWorkerRuntimePaths.ResolveAppDataPath(), "announcement-pdfs");
        Directory.CreateDirectory(_baseDir);
    }

    /// <summary>
    /// 采集指定股票最近的公告 PDF
    /// </summary>
    /// <param name="symbol">股票代码，如 "sh600519"</param>
    /// <param name="maxCount">最多采集数量</param>
    /// <returns>已下载的 PDF 文件信息列表</returns>
    public async Task<List<DownloadedAnnouncementPdf>> CollectAsync(
        string symbol, int maxCount = 10, CancellationToken ct = default)
    {
        var results = new List<DownloadedAnnouncementPdf>();

        // 从 symbol 提取纯数字代码 (sh600519 → 600519)
        var code = symbol.Length > 2 && !char.IsDigit(symbol[0])
            ? symbol[2..]
            : symbol;

        // 1. 查询东方财富公告列表
        var announcements = await QueryAnnouncementListAsync(code, maxCount, ct);
        if (announcements.Count == 0)
        {
            _logger.LogInformation("[AnnouncementPdf] {Symbol}: 无公告数据", symbol);
            return results;
        }

        _logger.LogInformation("[AnnouncementPdf] {Symbol}: 查询到 {Count} 条公告", symbol, announcements.Count);

        // 2. 加载去重记录
        var tracker = DownloadTracker.Load(_baseDir);

        foreach (var ann in announcements)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(ann.ArtCode))
                continue;

            // 去重
            if (tracker.IsDownloaded(ann.ArtCode))
            {
                _logger.LogDebug("[AnnouncementPdf] 跳过已下载: {ArtCode} {Title}", ann.ArtCode, ann.Title);
                continue;
            }

            try
            {
                var filePath = await DownloadAnnouncementPdfAsync(symbol, code, ann, ct);
                if (filePath != null)
                {
                    tracker.MarkDownloaded(ann.ArtCode);
                    results.Add(new DownloadedAnnouncementPdf(
                        filePath, symbol, ann.Title, ann.ArtCode, ann.PublishTime,
                        $"https://data.eastmoney.com/notices/detail/{code}/{ann.ArtCode}.html"));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AnnouncementPdf] 下载失败，跳过: {ArtCode} {Title}", ann.ArtCode, ann.Title);
            }

            // 速率限制
            await Task.Delay(RequestInterval, ct);
        }

        // 保存去重记录
        tracker.Save(_baseDir);

        _logger.LogInformation("[AnnouncementPdf] {Symbol}: 下载完成 — {Downloaded}/{Total}",
            symbol, results.Count, announcements.Count);

        return results;
    }

    /// <summary>
    /// 查询东方财富公告 API（复用 EastmoneyAnnouncementParser 的 API 调用逻辑）
    /// </summary>
    private async Task<List<EastmoneyAnnouncement>> QueryAnnouncementListAsync(
        string code, int pageSize, CancellationToken ct)
    {
        var url = $"https://np-anotice-stock.eastmoney.com/api/security/ann" +
                  $"?sr=-1&page_size={pageSize}&page_index=1" +
                  $"&ann_type=SHA,SZA&client_source=web" +
                  $"&stock_list={code}&f_node=0&s_node=0";

        const int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Referer", "https://data.eastmoney.com/");

                var response = await _httpClient.SendAsync(request, ct);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(ct);
                return ParseAnnouncementList(json);
            }
            catch (Exception ex) when (attempt < maxRetries && !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "[AnnouncementPdf] API 查询失败 (第 {Attempt}/{Max} 次): {Code}",
                    attempt, maxRetries, code);
                await Task.Delay(1000 * attempt, ct);
            }
        }

        _logger.LogError("[AnnouncementPdf] API 查询最终失败: {Code}", code);
        return new List<EastmoneyAnnouncement>();
    }

    /// <summary>
    /// 解析东方财富公告 API 返回的 JSON
    /// </summary>
    internal static List<EastmoneyAnnouncement> ParseAnnouncementList(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("data", out var dataNode)
            || dataNode.ValueKind != JsonValueKind.Object
            || !dataNode.TryGetProperty("list", out var listNode)
            || listNode.ValueKind != JsonValueKind.Array)
        {
            return new List<EastmoneyAnnouncement>();
        }

        var result = new List<EastmoneyAnnouncement>();
        foreach (var item in listNode.EnumerateArray())
        {
            var title = item.TryGetProperty("title", out var titleNode)
                ? titleNode.GetString() ?? string.Empty
                : string.Empty;
            if (string.IsNullOrWhiteSpace(title))
                continue;

            var artCode = item.TryGetProperty("art_code", out var artCodeNode)
                ? artCodeNode.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(artCode))
                continue;

            var publishText = item.TryGetProperty("display_time", out var displayNode)
                ? displayNode.GetString()
                : null;
            var publishTime = ParseEastmoneyTime(publishText);

            result.Add(new EastmoneyAnnouncement
            {
                Title = title,
                ArtCode = artCode,
                PublishTime = publishTime
            });
        }

        return result;
    }

    /// <summary>
    /// 下载单个公告 PDF
    /// </summary>
    private async Task<string?> DownloadAnnouncementPdfAsync(
        string symbol, string code, EastmoneyAnnouncement ann, CancellationToken ct)
    {
        // 生成目标文件路径
        var dir = Path.Combine(_baseDir, symbol);
        Directory.CreateDirectory(dir);

        var fileName = $"{SanitizeFileName(ann.ArtCode)}_{SanitizeFileName(ann.Title)}.pdf";
        var filePath = Path.Combine(dir, fileName);

        // 已存在则跳过
        if (File.Exists(filePath))
        {
            _logger.LogDebug("[AnnouncementPdf] PDF 已存在: {Path}", filePath);
            return filePath;
        }

        // 策略 1：直接构造 PDF URL
        var directUrl = string.Format(DirectPdfUrlTemplate, ann.ArtCode);
        if (await TryHeadRequestAsync(directUrl, ct))
        {
            var downloaded = await DownloadFileAsync(directUrl, filePath, ct);
            if (downloaded) return filePath;
        }

        // 策略 2：解析公告 HTML 页面提取 PDF URL
        await Task.Delay(RequestInterval, ct);
        var htmlUrl = $"https://data.eastmoney.com/notices/detail/{code}/{ann.ArtCode}.html";
        var pdfUrl = await ExtractPdfUrlFromHtmlAsync(htmlUrl, ct);
        if (!string.IsNullOrWhiteSpace(pdfUrl))
        {
            var downloaded = await DownloadFileAsync(pdfUrl, filePath, ct);
            if (downloaded) return filePath;
        }

        _logger.LogWarning("[AnnouncementPdf] 无法获取 PDF: {ArtCode} {Title}", ann.ArtCode, ann.Title);
        return null;
    }

    /// <summary>
    /// 用 HEAD 请求验证 URL 是否有效
    /// </summary>
    private async Task<bool> TryHeadRequestAsync(string url, CancellationToken ct)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Head, url);
            var response = await _httpClient.SendAsync(request, ct);
            return response.IsSuccessStatusCode
                   && response.Content.Headers.ContentLength > 1024;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 从公告 HTML 页面提取 PDF 下载 URL
    /// </summary>
    internal static string? ExtractPdfUrlFromHtml(string html)
    {
        // 先检查 href/src 属性中的 PDF 链接
        var match = PdfUrlInHtmlRegex.Match(html);
        if (match.Success)
            return match.Groups[1].Value;

        // 再检查 JavaScript 中的 PDF 链接
        var scriptMatch = PdfUrlInScriptRegex.Match(html);
        if (scriptMatch.Success)
            return scriptMatch.Groups[1].Value;

        return null;
    }

    private async Task<string?> ExtractPdfUrlFromHtmlAsync(string htmlUrl, CancellationToken ct)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, htmlUrl);
            request.Headers.Add("Referer", "https://data.eastmoney.com/");

            var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var html = await response.Content.ReadAsStringAsync(ct);
            return ExtractPdfUrlFromHtml(html);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[AnnouncementPdf] HTML 页面获取失败: {Url}", htmlUrl);
            return null;
        }
    }

    /// <summary>
    /// 下载文件到本地（带重试）
    /// </summary>
    private async Task<bool> DownloadFileAsync(string url, string filePath, CancellationToken ct)
    {
        const int maxRetries = 2;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Referer", "https://data.eastmoney.com/");

                var response = await _httpClient.SendAsync(request, ct);
                response.EnsureSuccessStatusCode();

                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                if (bytes.Length < 1024)
                {
                    _logger.LogWarning("[AnnouncementPdf] 文件过小 ({Size} bytes)，可能非 PDF: {Url}",
                        bytes.Length, url);
                    return false;
                }

                // 先写临时文件，成功后再移动
                var tmpPath = filePath + ".tmp";
                await File.WriteAllBytesAsync(tmpPath, bytes, ct);
                File.Move(tmpPath, filePath, overwrite: true);

                _logger.LogInformation("[AnnouncementPdf] PDF 下载完成: {Path} ({Size:N0} bytes)",
                    filePath, bytes.Length);
                return true;
            }
            catch (Exception ex) when (attempt < maxRetries && !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "[AnnouncementPdf] 下载失败 (第 {Attempt}/{Max} 次): {Url}",
                    attempt, maxRetries, url);
                await Task.Delay(1000 * attempt, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AnnouncementPdf] 下载最终失败: {Url}", url);
                return false;
            }
        }

        return false;
    }

    internal static string SanitizeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(title.Where(c => !invalid.Contains(c)).ToArray());
        return clean.Length > 80 ? clean[..80] : clean;
    }

    private static readonly TimeZoneInfo ChinaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");

    private static DateTime ParseEastmoneyTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DateTime.UtcNow;

        var normalized = value.Trim();
        // 东方财富时间可能有多余的冒号后缀
        var lastColon = normalized.LastIndexOf(':');
        if (lastColon > "yyyy-MM-dd HH:mm:ss".Length - 1)
            normalized = normalized[..lastColon];

        if (DateTime.TryParse(normalized, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), ChinaTimeZone);

        return DateTime.UtcNow;
    }
}

/// <summary>东方财富公告列表中的单条公告</summary>
public class EastmoneyAnnouncement
{
    public string Title { get; set; } = string.Empty;
    public string ArtCode { get; set; } = string.Empty;
    public DateTime PublishTime { get; set; }
}

/// <summary>已下载的公告 PDF 信息</summary>
public record DownloadedAnnouncementPdf(
    string FilePath,
    string Symbol,
    string Title,
    string ArtCode,
    DateTime PublishTime,
    string SourceUrl);

/// <summary>追踪已下载的 art_code，避免重复下载</summary>
public class DownloadTracker
{
    private HashSet<string> _downloaded;

    private DownloadTracker(HashSet<string> downloaded)
    {
        _downloaded = downloaded;
    }

    public bool IsDownloaded(string artCode) => _downloaded.Contains(artCode);

    public void MarkDownloaded(string artCode) => _downloaded.Add(artCode);

    public static DownloadTracker Load(string baseDir)
    {
        var path = Path.Combine(baseDir, "downloaded.json");
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var list = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                return new DownloadTracker(new HashSet<string>(list, StringComparer.OrdinalIgnoreCase));
            }
            catch
            {
                // 文件损坏时重建
            }
        }
        return new DownloadTracker(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    public void Save(string baseDir)
    {
        var path = Path.Combine(baseDir, "downloaded.json");
        Directory.CreateDirectory(baseDir);
        var json = JsonSerializer.Serialize(_downloaded.ToList(),
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
