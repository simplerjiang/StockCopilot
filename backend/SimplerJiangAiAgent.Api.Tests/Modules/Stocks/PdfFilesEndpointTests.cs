using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SimplerJiangAiAgent.Api.Modules.Stocks.Contracts;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

/// <summary>
/// v0.4.1 §S2：PDF 文件 4 接口集成测试。
/// 通过 WebApplicationFactory + Stub 注入 IPdfFileQueryService / IPdfReparseGateway，
/// 验证 200 / 404 / 403 / 重解析失败路径与 §9.2 三字段硬断言。
/// </summary>
public class PdfFilesEndpointTests : IClassFixture<PdfFilesEndpointTests.Factory>
{
    private readonly Factory _factory;

    public PdfFilesEndpointTests(Factory factory)
    {
        _factory = factory;
        // 默认重置 stub，避免互相污染
        _factory.QueryStub = new StubPdfFileQueryService();
        _factory.GatewayStub = new StubPdfReparseGateway();
        _factory.FinancialWorkerHandler = null;
    }

    [Fact]
    public async Task GetPdfFiles_WithSymbolFilter_Returns200WithList()
    {
        var stub = new StubPdfFileQueryService();
        stub.NextList = new PagedResult<PdfFileListItem>(
            new[]
            {
                MakeListItem("000000000000000000000001", "600519", "茅台2024年报"),
                MakeListItem("000000000000000000000002", "600519", "茅台2024Q3"),
            }, Total: 2, Page: 1, PageSize: 20);
        _factory.QueryStub = stub;

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/stocks/financial/pdf-files?symbol=600519&reportType=Annual&page=1&pageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("total").GetInt32());
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(20, body.GetProperty("pageSize").GetInt32());
        var items = body.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());

        Assert.NotNull(stub.LastListQuery);
        Assert.Equal("600519", stub.LastListQuery!.Symbol);
        Assert.Equal("Annual", stub.LastListQuery.ReportType);
        Assert.Equal(1, stub.LastListQuery.Page);
        Assert.Equal(20, stub.LastListQuery.PageSize);

        // 列表项不应包含 ParseUnits 字段
        var first = items[0];
        Assert.False(first.TryGetProperty("parseUnits", out _), "list item must not include parseUnits");
        Assert.True(first.TryGetProperty("stageLogs", out _), "list item must include stageLogs summary");
    }

    [Fact]
    public async Task GetPdfFile_WithValidId_Returns200WithParseUnits()
    {
        var stub = new StubPdfFileQueryService();
        var detail = MakeDetail("000000000000000000000001", "600519", new[]
        {
            new PdfParseUnitDto("narrative_section", 5, 8, "BalanceSheet", 18, null),
            new PdfParseUnitDto("table", 12, 12, "FixedAsset", 24, null),
            new PdfParseUnitDto("figure_caption", 3, 3, "FigureCaption", 0, "图1"),
        });
        stub.DetailById["000000000000000000000001"] = detail;
        _factory.QueryStub = stub;

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/stocks/financial/pdf-files/000000000000000000000001");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var parseUnits = body.GetProperty("parseUnits");
        Assert.Equal(3, parseUnits.GetArrayLength());

        // §9.2 硬断言：每个 parseUnit 三字段非空
        foreach (var unit in parseUnits.EnumerateArray())
        {
            var blockKind = unit.GetProperty("blockKind").GetString();
            Assert.False(string.IsNullOrWhiteSpace(blockKind), "blockKind must be non-empty");
            var pageStart = unit.GetProperty("pageStart").GetInt32();
            var pageEnd = unit.GetProperty("pageEnd").GetInt32();
            Assert.True(pageStart >= 1, $"pageStart must be >=1 (got {pageStart})");
            Assert.True(pageEnd >= pageStart, $"pageEnd must be >= pageStart (got {pageEnd} < {pageStart})");
        }
    }

    [Fact]
    public async Task GetPdfFile_WithUnknownId_Returns404()
    {
        _factory.QueryStub = new StubPdfFileQueryService(); // empty
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/stocks/financial/pdf-files/000000000000000000000099");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetPdfFileContent_WithValidId_ReturnsPdfStream()
    {
        var stub = new StubPdfFileQueryService();
        // 准备一个临时 PDF 文件用于流响应
        var tmp = Path.Combine(Path.GetTempPath(), $"pdf-endpoint-test-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(tmp, Encoding.ASCII.GetBytes("%PDF-1.4\n%fake-pdf-bytes\n%%EOF"));
        try
        {
            stub.ContentById["000000000000000000000001"] = new PdfFileContentResolution
            {
                Status = "found",
                FullPath = tmp,
                AccessKey = "abcdef0123456789.pdf",
            };
            _factory.QueryStub = stub;

            var client = _factory.CreateClient();
            var response = await client.GetAsync("/api/stocks/financial/pdf-files/000000000000000000000001/content");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(response.Content.Headers.ContentType);
            Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);

            Assert.True(response.Content.Headers.TryGetValues("Content-Disposition", out var disp), "Content-Disposition header missing");
            Assert.Contains("inline", string.Join(";", disp!), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("abcdef0123456789.pdf", string.Join(";", disp!), StringComparison.OrdinalIgnoreCase);

            var bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.True(bytes.Length > 0);
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }

    [Fact]
    public async Task GetPdfFileContent_WithPathTraversalAttempt_Returns403OrSafe()
    {
        var stub = new StubPdfFileQueryService();
        // 模拟 ResolveContent 沙箱判断：localPath 越权 → 返回 forbidden
        stub.ContentById["000000000000000000000099"] = new PdfFileContentResolution
        {
            Status = "forbidden",
            AccessKey = null,
        };
        _factory.QueryStub = stub;

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/stocks/financial/pdf-files/000000000000000000000099/content");

        // 沙箱违反必须 403；不接受 200 给出文件
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReparsePdfFile_OnFailure_Returns200WithStageLogsAndLastError()
    {
        var stub = new StubPdfFileQueryService();
        var detailWithFailedStages = MakeDetail("000000000000000000000001", "600519", new[]
        {
            new PdfParseUnitDto("narrative_section", 5, 8, "BalanceSheet", 18, null),
        }, lastError: "三路提取均失败", stageLogs: new[]
        {
            new PdfStageLogDto("download", "success", 0, "已存在本地", DateTime.UtcNow),
            new PdfStageLogDto("extract", "failed", 1234, "三路提取均失败", DateTime.UtcNow),
            new PdfStageLogDto("vote", "skipped", 0, null, DateTime.UtcNow),
            new PdfStageLogDto("parse", "skipped", 0, null, DateTime.UtcNow),
            new PdfStageLogDto("persist", "skipped", 0, null, DateTime.UtcNow),
        });
        stub.DetailById["000000000000000000000001"] = detailWithFailedStages;
        _factory.QueryStub = stub;

        // Stub gateway：模拟 reparse 返回失败
        _factory.GatewayStub = new StubPdfReparseGateway
        {
            NextResult = new PdfReparseGatewayResult
            {
                DocumentFound = true,
                PhysicalFileMissing = false,
                Success = false,
                Error = "三路提取均失败",
            }
        };

        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/stocks/financial/pdf-files/000000000000000000000001/reparse", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("success").GetBoolean(), "success must be false on failed reparse");
        Assert.Equal("三路提取均失败", body.GetProperty("error").GetString());

        var detail = body.GetProperty("detail");
        Assert.Equal("三路提取均失败", detail.GetProperty("lastError").GetString());

        var stageLogs = detail.GetProperty("stageLogs");
        Assert.Equal(5, stageLogs.GetArrayLength());

        var stages = new List<string>();
        foreach (var log in stageLogs.EnumerateArray())
        {
            stages.Add(log.GetProperty("stage").GetString() ?? "");
        }
        Assert.Equal(new[] { "download", "extract", "vote", "parse", "persist" }, stages);
    }

    // V041-S8-FU-1: 触发 PDF 原件采集（代理到 FinancialWorker 的 /api/pdf-collect/{symbol}）
    [Fact]
    public async Task CollectPdfFiles_ProxiesToFinancialWorker_Returns200()
    {
        HttpRequestMessage? captured = null;
        _factory.FinancialWorkerHandler = req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"success\":true,\"processedCount\":2}",
                    Encoding.UTF8,
                    "application/json")
            };
        };

        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/stocks/financial/pdf-files/collect/sh603099", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Equal(2, body.GetProperty("processedCount").GetInt32());

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Contains("/api/pdf-collect/", captured.RequestUri!.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        // sh/sz 前缀应被剥掉，只剩 6 位数字
        Assert.EndsWith("/603099", captured.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task CollectPdfFiles_OnWorkerFailure_PropagatesStatusCode()
    {
        _factory.FinancialWorkerHandler = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(
                "{\"error\":\"PDF 下载失败\",\"errorMessage\":\"PDF 下载失败\"}",
                Encoding.UTF8,
                "application/json")
        };

        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/stocks/financial/pdf-files/collect/600519", content: null);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("PDF 下载失败", body.GetProperty("error").GetString());
        Assert.Equal("PDF 下载失败", body.GetProperty("errorMessage").GetString());
    }

    // ── helpers ──

    private static PdfFileListItem MakeListItem(string id, string symbol, string title)
    {
        return new PdfFileListItem(
            Id: id,
            Symbol: symbol,
            FileName: "test.pdf",
            Title: title,
            ReportPeriod: "2024-12-31",
            ReportType: "Annual",
            Extractor: "PdfPig",
            VoteConfidence: "Unanimous",
            FieldCount: 42,
            LastParsedAt: DateTime.UtcNow,
            LastReparsedAt: null,
            LastError: null,
            AccessKey: "abcdef0123456789.pdf",
            StageLogs: new[]
            {
                new PdfStageLogDto("download", "success", 0, null, DateTime.UtcNow),
                new PdfStageLogDto("extract", "success", 1200, null, DateTime.UtcNow),
                new PdfStageLogDto("vote", "success", 0, null, DateTime.UtcNow),
                new PdfStageLogDto("parse", "success", 30, null, DateTime.UtcNow),
                new PdfStageLogDto("persist", "success", 5, null, DateTime.UtcNow),
            });
    }

    private static PdfFileDetail MakeDetail(
        string id,
        string symbol,
        IReadOnlyList<PdfParseUnitDto> parseUnits,
        string? lastError = null,
        IReadOnlyList<PdfStageLogDto>? stageLogs = null)
    {
        return new PdfFileDetail(
            Id: id,
            Symbol: symbol,
            FileName: "test.pdf",
            Title: "测试报告",
            ReportPeriod: "2024-12-31",
            ReportType: "Annual",
            Extractor: "PdfPig",
            VoteConfidence: "Unanimous",
            FieldCount: parseUnits.Sum(u => u.FieldCount),
            LastParsedAt: DateTime.UtcNow,
            LastReparsedAt: stageLogs is null ? null : DateTime.UtcNow,
            LastError: lastError,
            AccessKey: "abcdef0123456789.pdf",
            ParseUnits: parseUnits,
            StageLogs: stageLogs ?? Array.Empty<PdfStageLogDto>());
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public StubPdfFileQueryService QueryStub { get; set; } = new();
        public StubPdfReparseGateway GatewayStub { get; set; } = new();
        // V041-S8-FU-1: 用于拦截 ProxyFinancialWorkerAsync 出去的 HTTP 调用
        public Func<HttpRequestMessage, HttpResponseMessage>? FinancialWorkerHandler { get; set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                foreach (var d in services.Where(s => s.ServiceType == typeof(IPdfFileQueryService)).ToList())
                {
                    services.Remove(d);
                }
                foreach (var d in services.Where(s => s.ServiceType == typeof(IPdfReparseGateway)).ToList())
                {
                    services.Remove(d);
                }
                services.AddScoped<IPdfFileQueryService>(_ => QueryStub);
                services.AddSingleton<IPdfReparseGateway>(_ => GatewayStub);

                // 拦截 IHttpClientFactory 以便测试 ProxyFinancialWorkerAsync 行为（V041-S8-FU-1）
                foreach (var d in services.Where(s => s.ServiceType == typeof(IHttpClientFactory)).ToList())
                {
                    services.Remove(d);
                }
                services.AddSingleton<IHttpClientFactory>(_ => new StubHttpClientFactory(this));
            });
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly Factory _factory;
        public StubHttpClientFactory(Factory factory) { _factory = factory; }
        public HttpClient CreateClient(string name)
        {
            var handler = new StubHttpMessageHandler(req =>
            {
                var fn = _factory.FinancialWorkerHandler;
                if (fn is null)
                {
                    return new HttpResponseMessage(HttpStatusCode.NotImplemented)
                    {
                        Content = new StringContent("{\"error\":\"no handler set\"}")
                    };
                }
                return fn(req);
            });
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;
        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> factory) { _factory = factory; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_factory(request));
    }

    public sealed class StubPdfFileQueryService : IPdfFileQueryService
    {
        public PdfFileListQuery? LastListQuery { get; private set; }
        public PagedResult<PdfFileListItem> NextList { get; set; } =
            new PagedResult<PdfFileListItem>(Array.Empty<PdfFileListItem>(), 0, 1, 20);
        public Dictionary<string, PdfFileDetail> DetailById { get; } = new();
        public Dictionary<string, PdfFileContentResolution> ContentById { get; } = new();

        public PagedResult<PdfFileListItem> List(PdfFileListQuery query)
        {
            LastListQuery = query;
            return NextList;
        }

        public PdfFileDetail? GetById(string id) => DetailById.TryGetValue(id, out var v) ? v : null;

        public PdfFileContentResolution ResolveContent(string id)
            => ContentById.TryGetValue(id, out var v) ? v : new PdfFileContentResolution { Status = "not_found" };

        public void Dispose() { }
    }

    public sealed class StubPdfReparseGateway : IPdfReparseGateway
    {
        public string? LastId { get; private set; }
        public PdfReparseGatewayResult NextResult { get; set; } = new()
        {
            DocumentFound = true,
            Success = true,
        };

        public Task<PdfReparseGatewayResult> ReparseAsync(string id, CancellationToken ct = default)
        {
            LastId = id;
            return Task.FromResult(NextResult);
        }
    }
}
