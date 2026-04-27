using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class ResearchSessionEndpointTests : IClassFixture<ResearchSessionEndpointTests.Factory>
{
    private readonly Factory _factory;

    public ResearchSessionEndpointTests(Factory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/api/stocks/research/active-session")]
    [InlineData("/api/stocks/research/active-session?symbol=")]
    [InlineData("/api/stocks/research/active-session?symbol=%20%20")]
    [InlineData("/api/stocks/research/sessions")]
    [InlineData("/api/stocks/research/sessions?symbol=")]
    [InlineData("/api/stocks/research/sessions?symbol=%20%20")]
    public async Task ResearchSessionEndpoints_WithMissingOrBlankSymbol_ReturnMissingSymbolJson(string path)
    {
        var stub = new StubResearchSessionService();
        _factory.Stub = stub;
        var client = _factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("missing_symbol", body.GetProperty("error").GetString());
        Assert.Equal("股票代码不能为空", body.GetProperty("message").GetString());
        Assert.Equal(0, stub.GetActiveSessionCallCount);
        Assert.Equal(0, stub.ListSessionsCallCount);
    }

    [Theory]
    [InlineData("/api/stocks/research/active-session?symbol=600519", true)]
    [InlineData("/api/stocks/research/sessions?symbol=600519", false)]
    public async Task ResearchSessionEndpoints_WithValidSymbol_DoNotReturnBadRequest(string path, bool activeSessionEndpoint)
    {
        var stub = new StubResearchSessionService();
        _factory.Stub = stub;
        var client = _factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        if (activeSessionEndpoint)
        {
            Assert.Equal(1, stub.GetActiveSessionCallCount);
            Assert.Equal("600519", stub.LastActiveSessionSymbol);
        }
        else
        {
            Assert.Equal(1, stub.ListSessionsCallCount);
            Assert.Equal("600519", stub.LastListSessionsSymbol);
        }
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public StubResearchSessionService? Stub { get; set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var dataRoot = Path.Combine(Path.GetTempPath(), "sjai-tests", nameof(ResearchSessionEndpointTests), Guid.NewGuid().ToString("N"));

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
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IResearchSessionService>();
                services.AddScoped<IResearchSessionService>(_ => Stub ?? new StubResearchSessionService());
            });
        }
    }

    public sealed class StubResearchSessionService : IResearchSessionService
    {
        public int GetActiveSessionCallCount { get; private set; }
        public int ListSessionsCallCount { get; private set; }
        public string? LastActiveSessionSymbol { get; private set; }
        public string? LastListSessionsSymbol { get; private set; }

        public Task<ResearchActiveSessionDto?> GetActiveSessionAsync(string symbol, CancellationToken cancellationToken = default)
        {
            GetActiveSessionCallCount += 1;
            LastActiveSessionSymbol = symbol;
            return Task.FromResult<ResearchActiveSessionDto?>(null);
        }

        public Task<IReadOnlyList<ResearchSessionSummaryDto>> ListSessionsAsync(string symbol, int limit = 20, CancellationToken cancellationToken = default)
        {
            ListSessionsCallCount += 1;
            LastListSessionsSymbol = symbol;
            return Task.FromResult<IReadOnlyList<ResearchSessionSummaryDto>>(Array.Empty<ResearchSessionSummaryDto>());
        }

        public Task<ResearchSessionDetailDto?> GetSessionDetailAsync(long sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ResearchSessionDetailDto?>(null);

        public Task<ResearchTurnSubmitResponseDto> SubmitTurnAsync(ResearchTurnSubmitRequestDto request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<bool> CancelActiveTurnAsync(long sessionId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}