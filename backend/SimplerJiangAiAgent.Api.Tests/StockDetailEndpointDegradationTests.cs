using System.Diagnostics;
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

public sealed class StockDetailEndpointDegradationTests : IClassFixture<StockDetailEndpointDegradationTests.Factory>
{
    private readonly Factory _factory;

    public StockDetailEndpointDegradationTests(Factory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task StockDetail_WhenIntradayMessagesTimeout_ReturnsQuoteWithDegradedMessages()
    {
        _factory.MessagesResult = async (_, _, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
            return new IntradayMessagesResultDto(Array.Empty<IntradayMessageDto>());
        };
        var client = _factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(8);

        var stopwatch = Stopwatch.StartNew();
        var response = await client.GetAsync("/api/stocks/detail?symbol=sh000001&persist=false&includeFundamentalSnapshot=false");
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(6), $"detail should degrade quickly, elapsed={stopwatch.Elapsed}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("sh000001", body.GetProperty("quote").GetProperty("symbol").GetString());
        Assert.Empty(body.GetProperty("messages").EnumerateArray());
        Assert.True(body.GetProperty("messagesDegraded").GetBoolean());
        Assert.Contains("超时", body.GetProperty("warning").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task IntradayMessages_WhenRefreshOrEnrichmentFails_ReturnsDegradedJson()
    {
        _factory.MessagesResult = (_, _, _) => throw new InvalidOperationException("LLM unavailable");
        var client = _factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);

        var stopwatch = Stopwatch.StartNew();
        var response = await client.GetAsync("/api/stocks/messages?symbol=sz000518");
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"messages should fail fast, elapsed={stopwatch.Elapsed}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(body.GetProperty("messages").EnumerateArray());
        Assert.True(body.GetProperty("degraded").GetBoolean());
        Assert.Contains("失败", body.GetProperty("warning").GetString(), StringComparison.Ordinal);
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public Func<string, string?, CancellationToken, Task<IntradayMessagesResultDto>>? MessagesResult { get; set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var dataRoot = Path.Combine(Path.GetTempPath(), "sjai-tests", nameof(StockDetailEndpointDegradationTests), Guid.NewGuid().ToString("N"));

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
                services.RemoveAll<IStockDataService>();
                services.AddScoped<IStockDataService>(_ => new StubStockDataService(
                    (symbol, _, _) => Task.FromResult<StockQuoteDto?>(CreateQuote(symbol)),
                    MessagesResult ?? ((_, _, _) => Task.FromResult(new IntradayMessagesResultDto(Array.Empty<IntradayMessageDto>())))));
            });
        }
    }

    private sealed class StubStockDataService : IStockDataService
    {
        private readonly Func<string, string?, CancellationToken, Task<StockQuoteDto?>> _getQuote;
        private readonly Func<string, string?, CancellationToken, Task<IntradayMessagesResultDto>> _getMessagesResult;

        public StubStockDataService(
            Func<string, string?, CancellationToken, Task<StockQuoteDto?>> getQuote,
            Func<string, string?, CancellationToken, Task<IntradayMessagesResultDto>> getMessagesResult)
        {
            _getQuote = getQuote;
            _getMessagesResult = getMessagesResult;
        }

        public Task<StockQuoteDto?> GetQuoteAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
        {
            return _getQuote(symbol, source, cancellationToken);
        }

        public async Task<IReadOnlyList<IntradayMessageDto>> GetIntradayMessagesAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
        {
            var result = await GetIntradayMessagesResultAsync(symbol, source, cancellationToken);
            return result.Messages;
        }

        public Task<IntradayMessagesResultDto> GetIntradayMessagesResultAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
        {
            return _getMessagesResult(symbol, source, cancellationToken);
        }

        public Task<MarketIndexDto> GetMarketIndexAsync(string symbol, string? source = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<KLinePointDto>> GetKLineAsync(string symbol, string interval, int count, string? source = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<MinuteLinePointDto>> GetMinuteLineAsync(string symbol, string? source = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private static StockQuoteDto CreateQuote(string symbol)
    {
        return new StockQuoteDto(symbol, symbol, 100m, 1m, 1m, 0m, 0m, 101m, 99m, 0m, DateTime.UtcNow, Array.Empty<StockNewsDto>(), Array.Empty<StockIndicatorDto>());
    }
}