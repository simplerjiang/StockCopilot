using SimplerJiangAiAgent.FinancialWorker.Services.Announcement;

namespace SimplerJiangAiAgent.FinancialWorker.Tests;

public class AnnouncementPdfCollectorTests : IDisposable
{
    private readonly string _tempDir;

    public AnnouncementPdfCollectorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ann-pdf-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
    }

    // ──────── PDF URL 提取 ────────

    [Fact]
    public void ExtractPdfUrlFromHtml_HrefAttribute_ReturnsPdfUrl()
    {
        var html = """
            <html><body>
            <a href="https://pdf.dfcfw.com/pdf/H2_AN202504001_1.pdf" target="_blank">下载公告</a>
            </body></html>
            """;

        var result = AnnouncementPdfCollector.ExtractPdfUrlFromHtml(html);

        Assert.Equal("https://pdf.dfcfw.com/pdf/H2_AN202504001_1.pdf", result);
    }

    [Fact]
    public void ExtractPdfUrlFromHtml_SrcAttribute_ReturnsPdfUrl()
    {
        var html = """
            <html><body>
            <iframe src="https://pdf.dfcfw.com/pdf/H2_AN202504002_1.pdf"></iframe>
            </body></html>
            """;

        var result = AnnouncementPdfCollector.ExtractPdfUrlFromHtml(html);

        Assert.Equal("https://pdf.dfcfw.com/pdf/H2_AN202504002_1.pdf", result);
    }

    [Fact]
    public void ExtractPdfUrlFromHtml_ScriptVariable_ReturnsPdfUrl()
    {
        var html = """
            <html><body>
            <script>var pdfUrl = "https://pdf.dfcfw.com/pdf/H2_AN202504003_1.pdf";</script>
            </body></html>
            """;

        var result = AnnouncementPdfCollector.ExtractPdfUrlFromHtml(html);

        Assert.Equal("https://pdf.dfcfw.com/pdf/H2_AN202504003_1.pdf", result);
    }

    [Fact]
    public void ExtractPdfUrlFromHtml_NoPdfLink_ReturnsNull()
    {
        var html = "<html><body><p>No PDF here</p></body></html>";

        var result = AnnouncementPdfCollector.ExtractPdfUrlFromHtml(html);

        Assert.Null(result);
    }

    // ──────── JSON 解析 ────────

    [Fact]
    public void ParseAnnouncementList_ValidJson_ParsesCorrectly()
    {
        var json = """
            {
                "data": {
                    "list": [
                        {
                            "title": "关于2024年度利润分配方案的公告",
                            "art_code": "AN202504281649438286",
                            "display_time": "2025-04-28 18:30:00"
                        },
                        {
                            "title": "2024年年度报告",
                            "art_code": "AN202504281649438287",
                            "display_time": "2025-04-28 17:00:00"
                        }
                    ]
                }
            }
            """;

        var result = AnnouncementPdfCollector.ParseAnnouncementList(json);

        Assert.Equal(2, result.Count);
        Assert.Equal("关于2024年度利润分配方案的公告", result[0].Title);
        Assert.Equal("AN202504281649438286", result[0].ArtCode);
        Assert.Equal(new DateTime(2025, 4, 28, 10, 30, 0, DateTimeKind.Utc), result[0].PublishTime);
        Assert.Equal("2024年年度报告", result[1].Title);
        Assert.Equal(new DateTime(2025, 4, 28, 9, 0, 0, DateTimeKind.Utc), result[1].PublishTime);
    }

    [Fact]
    public void ParseAnnouncementList_EmptyList_ReturnsEmpty()
    {
        var json = """{"data": {"list": []}}""";

        var result = AnnouncementPdfCollector.ParseAnnouncementList(json);

        Assert.Empty(result);
    }

    [Fact]
    public void ParseAnnouncementList_MissingData_ReturnsEmpty()
    {
        var json = """{"error": "not found"}""";

        var result = AnnouncementPdfCollector.ParseAnnouncementList(json);

        Assert.Empty(result);
    }

    [Fact]
    public void ParseAnnouncementList_SkipsEntriesWithoutArtCode()
    {
        var json = """
            {
                "data": {
                    "list": [
                        { "title": "无code的公告" },
                        { "title": "有code", "art_code": "AN001", "display_time": "2025-01-01" }
                    ]
                }
            }
            """;

        var result = AnnouncementPdfCollector.ParseAnnouncementList(json);

        Assert.Single(result);
        Assert.Equal("AN001", result[0].ArtCode);
    }

    // ──────── 去重 (DownloadTracker) ────────

    [Fact]
    public void DownloadTracker_LoadSave_RoundTrip()
    {
        var tracker = DownloadTracker.Load(_tempDir);
        Assert.False(tracker.IsDownloaded("AN001"));

        tracker.MarkDownloaded("AN001");
        tracker.MarkDownloaded("AN002");
        tracker.Save(_tempDir);

        // 重新加载
        var tracker2 = DownloadTracker.Load(_tempDir);
        Assert.True(tracker2.IsDownloaded("AN001"));
        Assert.True(tracker2.IsDownloaded("AN002"));
        Assert.False(tracker2.IsDownloaded("AN003"));
    }

    [Fact]
    public void DownloadTracker_Load_EmptyDirectory_ReturnsEmptyTracker()
    {
        var tracker = DownloadTracker.Load(_tempDir);

        Assert.False(tracker.IsDownloaded("AN_ANY"));
    }

    [Fact]
    public void DownloadTracker_CaseInsensitive()
    {
        var tracker = DownloadTracker.Load(_tempDir);
        tracker.MarkDownloaded("AN001abc");

        Assert.True(tracker.IsDownloaded("AN001ABC"));
        Assert.True(tracker.IsDownloaded("an001abc"));
    }

    // ──────── 文件名清理 ────────

    [Fact]
    public void SanitizeFileName_RemovesInvalidChars()
    {
        var result = AnnouncementPdfCollector.SanitizeFileName("关于:2024/年度\"利润\"分配<方案>的公告");

        Assert.DoesNotContain(":", result);
        Assert.DoesNotContain("/", result);
        Assert.DoesNotContain("\"", result);
        Assert.DoesNotContain("<", result);
        Assert.DoesNotContain(">", result);
        Assert.Contains("关于", result);
        Assert.Contains("公告", result);
    }

    [Fact]
    public void SanitizeFileName_TruncatesLongNames()
    {
        var longTitle = new string('A', 200);

        var result = AnnouncementPdfCollector.SanitizeFileName(longTitle);

        Assert.Equal(80, result.Length);
    }
}
