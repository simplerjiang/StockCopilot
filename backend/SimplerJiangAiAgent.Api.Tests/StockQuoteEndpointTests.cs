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

public sealed class StockQuoteEndpointTests : IClassFixture<StockQuoteEndpointTests.Factory>
{
    private readonly Factory _factory;

    public StockQuoteEndpointTests(Factory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetQuote_WithBaiduSourceAndNoData_ReturnsNotFoundInsteadOfPlaceholderQuote()
    {
        _factory.StockDataServiceStub = new StubStockDataService((_, source, _) =>
            Task.FromResult<StockQuoteDto?>(string.Equals(source, "百度", StringComparison.OrdinalIgnoreCase) ? null : CreateQuote("sh600519")));
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/stocks/quote?symbol=600519&source=%E7%99%BE%E5%BA%A6");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("not_found", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetQuote_WithUnsupportedSource_ReturnsBadRequest()
    {
        _factory.StockDataServiceStub = new StubStockDataService((_, source, _) =>
            throw new UnsupportedStockSourceException(source ?? string.Empty));
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/stocks/quote?symbol=600519&source=%E7%81%AB%E6%98%9F");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("unsupported_source", body.GetProperty("error").GetString());
        Assert.Contains("火星", body.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetQuote_WithoutExplicitSource_ReturnsOkFromDefaultPath()
    {
        _factory.StockDataServiceStub = new StubStockDataService((symbol, _, _) => Task.FromResult<StockQuoteDto?>(CreateQuote(symbol)));
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/stocks/quote?symbol=600519");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("sh600519", body.GetProperty("symbol").GetString());
        Assert.Equal(100m, body.GetProperty("price").GetDecimal());
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public StubStockDataService? StockDataServiceStub { get; set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var dataRoot = Path.Combine(Path.GetTempPath(), "sjai-tests", nameof(StockQuoteEndpointTests), Guid.NewGuid().ToString("N"));

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
                services.AddScoped<IStockDataService>(_ => StockDataServiceStub ?? new StubStockDataService((symbol, _, _) => Task.FromResult<StockQuoteDto?>(CreateQuote(symbol))));
            });
        }
    }

    public sealed class StubStockDataService : IStockDataService
    {
        private readonly Func<string, string?, CancellationToken, Task<StockQuoteDto?>> _getQuote;

        public StubStockDataService(Func<string, string?, CancellationToken, Task<StockQuoteDto?>> getQuote)
        {
            _getQuote = getQuote;
        }

        public Task<StockQuoteDto?> GetQuoteAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
        {
            return _getQuote(symbol, source, cancellationToken);
        }

        public Task<MarketIndexDto> GetMarketIndexAsync(string symbol, string? source = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<KLinePointDto>> GetKLineAsync(string symbol, string interval, int count, string? source = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<MinuteLinePointDto>> GetMinuteLineAsync(string symbol, string? source = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<IntradayMessageDto>> GetIntradayMessagesAsync(string symbol, string? source = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private static StockQuoteDto CreateQuote(string symbol)
    {
        return new StockQuoteDto(symbol, "贵州茅台", 100m, 1m, 1m, 1m, 20m, 101m, 99m, 0m, DateTime.UtcNow, Array.Empty<StockNewsDto>(), Array.Empty<StockIndicatorDto>());
    }
}