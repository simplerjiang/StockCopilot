using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SimplerJiangAiAgent.Api.Modules.Market;
using SimplerJiangAiAgent.Api.Modules.Market.Services;

namespace SimplerJiangAiAgent.Api.Tests;

/// <summary>
/// V048-S2 #71 + #78 集成测试：
///   #71 — /api/* 未命中应返 404，而不是被 SPA fallback 吞成 index.html (200)
///   #78 — /api/market/sync 并发请求应立刻返 429 而不是 30s 阻塞
/// </summary>
public sealed class V048S2EndpointTests : IClassFixture<V048S2EndpointTests.Factory>
{
    private readonly Factory _factory;

    public V048S2EndpointTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task UnknownApiPath_Returns404_NotSpaFallbackHtml()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/bogus/path");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        // 不应是 SPA fallback 返回的 text/html
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        Assert.DoesNotContain("text/html", contentType, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnknownApiPath_PostMethod_Also404()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/news/archive/cleanup", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MarketSync_ConcurrentRequests_OneSucceeds_OthersReturn429Immediately()
    {
        // 用 stub 模拟较慢的 SyncAsync（200ms），让 5 路并发能命中并发窗口
        var stub = new SlowStubSectorRotationIngestionService(TimeSpan.FromMilliseconds(300));
        _factory.IngestionStub = stub;

        var client = _factory.CreateClient();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => client.PostAsync("/api/market/sync", content: null))
            .ToArray();
        var responses = await Task.WhenAll(tasks);
        sw.Stop();

        // 全部 5 路必须在 5 秒内返回（实际应该在 ~300ms 上下）
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
            $"5 路并发耗时 {sw.Elapsed.TotalSeconds:F2}s 超过 5s 上限");

        var okCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var throttledCount = responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests);

        // 恰好 1 个 200，其余 4 个 429
        Assert.Equal(1, okCount);
        Assert.Equal(4, throttledCount);

        // 429 响应体应包含 throttled 标识
        var throttledBody = await responses.First(r => r.StatusCode == HttpStatusCode.TooManyRequests)
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("throttled", throttledBody.GetProperty("status").GetString());
    }

    [Fact]
    public async Task MarketSync_AfterCompletion_GateReleased_NextCallSucceeds()
    {
        var stub = new SlowStubSectorRotationIngestionService(TimeSpan.FromMilliseconds(50));
        _factory.IngestionStub = stub;

        var client = _factory.CreateClient();

        var first = await client.PostAsync("/api/market/sync", content: null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // 等门闸释放后应能再次成功
        var second = await client.PostAsync("/api/market/sync", content: null);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public ISectorRotationIngestionService? IngestionStub { get; set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var dataRoot = Path.Combine(Path.GetTempPath(), "sjai-tests", nameof(V048S2EndpointTests), Guid.NewGuid().ToString("N"));

            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:DataRootPath"] = dataRoot
                });
            });
            builder.ConfigureServices(services =>
            {
                ApiTestDatabaseIsolation.UseIsolatedSqlite(services, dataRoot);

                // 始终替换为可控 stub，按测试时间点读取 IngestionStub 字段（默认 50ms 延迟）
                var existing = services.Where(d => d.ServiceType == typeof(ISectorRotationIngestionService)).ToList();
                foreach (var d in existing) services.Remove(d);
                services.AddScoped<ISectorRotationIngestionService>(_ =>
                    IngestionStub ?? new SlowStubSectorRotationIngestionService(TimeSpan.FromMilliseconds(50)));
            });
        }
    }

    private sealed class SlowStubSectorRotationIngestionService : ISectorRotationIngestionService
    {
        private readonly TimeSpan _delay;
        public SlowStubSectorRotationIngestionService(TimeSpan delay) => _delay = delay;

        public async Task SyncAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(_delay, cancellationToken);
        }
    }
}
