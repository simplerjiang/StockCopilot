using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class DividendTests : IClassFixture<DividendTests.Factory>
{
    private readonly Factory _factory;

    public DividendTests(Factory factory)
    {
        _factory = factory;
        SeedData(factory);
    }

    private static bool _seeded;
    private static readonly object _seedLock = new();

    private static void SeedData(Factory factory)
    {
        if (_seeded) return;
        lock (_seedLock)
        {
            if (_seeded) return;
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();

            db.StockDividendRecords.AddRange(
                new StockDividendRecord
                {
                    StockCode = "sh600519",
                    StockName = "贵州茅台",
                    ExDividendDate = new DateOnly(2024, 7, 12),
                    RecordDate = new DateOnly(2024, 7, 11),
                    DividendPerShare = 30.876m,
                    DividendPerShareAfterTax = 27.789m,
                    CreatedAt = DateTime.UtcNow
                },
                new StockDividendRecord
                {
                    StockCode = "sh600519",
                    StockName = "贵州茅台",
                    ExDividendDate = new DateOnly(2023, 7, 14),
                    RecordDate = new DateOnly(2023, 7, 13),
                    DividendPerShare = 25.979m,
                    DividendPerShareAfterTax = 23.381m,
                    CreatedAt = DateTime.UtcNow
                },
                new StockDividendRecord
                {
                    StockCode = "sh600519",
                    StockName = "贵州茅台",
                    ExDividendDate = new DateOnly(2022, 7, 15),
                    RecordDate = new DateOnly(2022, 7, 14),
                    DividendPerShare = 21.675m,
                    DividendPerShareAfterTax = 19.508m,
                    CreatedAt = DateTime.UtcNow
                },
                new StockDividendRecord
                {
                    StockCode = "sz000001",
                    StockName = "平安银行",
                    ExDividendDate = new DateOnly(2024, 7, 5),
                    RecordDate = new DateOnly(2024, 7, 4),
                    DividendPerShare = 0.74m,
                    DividendPerShareAfterTax = 0.666m,
                    CreatedAt = DateTime.UtcNow
                }
            );

            db.SaveChanges();
            _seeded = true;
        }
    }

    [Fact]
    public async Task GetDividends_ReturnsCorrectFormat()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/stocks/dividends/sh600519");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("sh600519", body.GetProperty("symbol").GetString());
        Assert.Equal(3, body.GetProperty("count").GetInt32());
        Assert.True(body.TryGetProperty("data", out var data));

        var first = data[0];
        // Ordered by ExDividendDate descending
        Assert.Equal("2024-07-12", first.GetProperty("exDividendDate").GetString());
        Assert.Equal("2024-07-11", first.GetProperty("recordDate").GetString());
        Assert.Equal(30.876m, first.GetProperty("dividendPerShare").GetDecimal());
        Assert.Equal(27.789m, first.GetProperty("dividendPerShareAfterTax").GetDecimal());
    }

    [Fact]
    public async Task GetDividends_WithYear_FiltersCorrectly()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/stocks/dividends/sh600519?year=2023");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("sh600519", body.GetProperty("symbol").GetString());
        Assert.Equal(1, body.GetProperty("count").GetInt32());

        var data = body.GetProperty("data");
        Assert.Equal("2023-07-14", data[0].GetProperty("exDividendDate").GetString());
    }

    [Fact]
    public async Task GetDividends_InvalidSymbol_Returns400()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/stocks/dividends/INVALID");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetDividends_DifferentStock_ReturnsOnlyThatStock()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/stocks/dividends/sz000001");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("sz000001", body.GetProperty("symbol").GetString());
        Assert.Equal(1, body.GetProperty("count").GetInt32());
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var dataRoot = Path.Combine(Path.GetTempPath(), "sjai-tests", nameof(DividendTests), Guid.NewGuid().ToString("N"));

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
            });
        }
    }
}
