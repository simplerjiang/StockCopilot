using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class ResetAllTradesEndpointTests : IClassFixture<ResetAllTradesEndpointTests.Factory>
{
    private readonly Factory _factory;

    public ResetAllTradesEndpointTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task ResetAllTrades_EmptyBody_ReturnsConfirmationRequired_AndDoesNotDelete()
    {
        var stub = new StubTradeAccountingService();
        _factory.TradeAccountingStub = stub;
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/trades/reset-all", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("confirmation_required", body.GetProperty("error").GetString());
        Assert.Equal("请提供确认文本 RESET_ALL_TRADES", body.GetProperty("message").GetString());
        Assert.Equal(0, stub.ResetAllCallCount);
    }

    [Fact]
    public async Task ResetAllTrades_EmptyObject_ReturnsConfirmationRequired_AndDoesNotDelete()
    {
        var stub = new StubTradeAccountingService();
        _factory.TradeAccountingStub = stub;
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/trades/reset-all", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("confirmation_required", body.GetProperty("error").GetString());
        Assert.Equal("请提供确认文本 RESET_ALL_TRADES", body.GetProperty("message").GetString());
        Assert.Equal(0, stub.ResetAllCallCount);
    }

    [Fact]
    public async Task ResetAllTrades_WrongConfirmText_ReturnsConfirmationRequired_AndDoesNotDelete()
    {
        var stub = new StubTradeAccountingService();
        _factory.TradeAccountingStub = stub;
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/trades/reset-all", new { confirmText = "DELETE" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("confirmation_required", body.GetProperty("error").GetString());
        Assert.Equal("请提供确认文本 RESET_ALL_TRADES", body.GetProperty("message").GetString());
        Assert.Equal(0, stub.ResetAllCallCount);
    }

    [Fact]
    public async Task ResetAllTrades_CorrectConfirmText_DeletesAndReturnsCompatibleCounts()
    {
        var stub = new StubTradeAccountingService();
        _factory.TradeAccountingStub = stub;
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/trades/reset-all", new { confirmText = "RESET_ALL_TRADES" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Equal(3, body.GetProperty("deletedTradeCount").GetInt32());
        Assert.Equal(2, body.GetProperty("deletedPositionCount").GetInt32());
        Assert.Equal(1, body.GetProperty("deletedReviewCount").GetInt32());
        Assert.Equal(1, stub.ResetAllCallCount);
    }

    [Theory]
    [InlineData("/api/trades/summary")]
    [InlineData("/api/trades/summary?period=")]
    [InlineData("/api/trades/summary?period=%20%20")]
    public async Task TradesSummary_MissingOrBlankPeriod_ReturnsBadRequestJson(string url)
    {
        var stub = new StubTradeAccountingService();
        _factory.TradeAccountingStub = stub;
        var client = _factory.CreateClient();

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("missing_period", body.GetProperty("error").GetString());
        Assert.Equal("请提供 period 参数", body.GetProperty("message").GetString());
        Assert.Equal(0, stub.GetTradeSummaryCallCount);
    }

    [Fact]
    public async Task TradesSummary_InvalidPeriod_ReturnsBadRequestJson()
    {
        var stub = new StubTradeAccountingService();
        _factory.TradeAccountingStub = stub;
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/trades/summary?period=bogus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_period", body.GetProperty("error").GetString());
        Assert.Contains("不支持的 period 参数", body.GetProperty("message").GetString());
        Assert.Equal(0, stub.GetTradeSummaryCallCount);
    }

    [Fact]
    public async Task TradesSummary_DayPeriod_ReturnsOk()
    {
        var stub = new StubTradeAccountingService();
        _factory.TradeAccountingStub = stub;
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/trades/summary?period=day");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("day", body.GetProperty("period").GetString());
        Assert.Equal(1, stub.GetTradeSummaryCallCount);
        Assert.Equal("day", stub.LastSummaryPeriod);
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public StubTradeAccountingService? TradeAccountingStub { get; set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var dataRoot = Path.Combine(Path.GetTempPath(), "sjai-tests", nameof(ResetAllTradesEndpointTests), Guid.NewGuid().ToString("N"));

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

                var existingTradeAccounting = services.Where(d => d.ServiceType == typeof(ITradeAccountingService)).ToList();
                foreach (var d in existingTradeAccounting)
                {
                    services.Remove(d);
                }

                services.AddScoped<ITradeAccountingService>(_ => TradeAccountingStub ?? new StubTradeAccountingService());
            });
        }
    }

    public sealed class StubTradeAccountingService : ITradeAccountingService
    {
        public int ResetAllCallCount { get; private set; }
        public int GetTradeSummaryCallCount { get; private set; }
        public string? LastSummaryPeriod { get; private set; }

        public Task<TradeExecution> RecordTradeAsync(TradeExecutionCreateDto dto) => throw new NotImplementedException();
        public Task<TradeExecution> UpdateTradeAsync(long id, TradeExecutionUpdateDto dto) => throw new NotImplementedException();
        public Task DeleteTradeAsync(long id) => throw new NotImplementedException();
        public Task<IReadOnlyList<TradeExecutionItemDto>> GetTradesAsync(string? symbol, DateTime? from, DateTime? to, string? type) => throw new NotImplementedException();
        public Task<TradeSummaryDto> GetTradeSummaryAsync(string period, DateTime? from, DateTime? to)
        {
            GetTradeSummaryCallCount += 1;
            LastSummaryPeriod = period;
            return Task.FromResult(new TradeSummaryDto(period, DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        }
        public Task<TradeWinRateDto> GetWinRateAsync(DateTime? from, DateTime? to, string? symbol) => throw new NotImplementedException();
        public Task RecalculatePositionAsync(string symbol) => throw new NotImplementedException();

        public Task<(int deletedTrades, int deletedPositions, int deletedReviews)> ResetAllTradesAsync()
        {
            ResetAllCallCount += 1;
            return Task.FromResult((3, 2, 1));
        }
    }
}