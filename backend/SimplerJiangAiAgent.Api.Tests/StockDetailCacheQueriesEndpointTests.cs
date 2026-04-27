using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class StockDetailCacheQueriesEndpointTests : IClassFixture<StockDetailCacheQueriesEndpointTests.Factory>
{
    private readonly Factory _factory;

    public StockDetailCacheQueriesEndpointTests(Factory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DetailCacheLegacyCharts_FiltersZeroHighLowKLinesAndKeepsZeroVolumeValidRows()
    {
        await SeedCacheRowsAsync();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/stocks/detail/cache?symbol=sh600001&includeLegacyCharts=true&interval=day&count=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var kLines = body.GetProperty("kLines").EnumerateArray().ToArray();
        var item = Assert.Single(kLines);
        Assert.StartsWith("2026-04-26T", item.GetProperty("date").GetString(), StringComparison.Ordinal);
        Assert.Equal(12m, item.GetProperty("high").GetDecimal());
        Assert.Equal(8m, item.GetProperty("low").GetDecimal());
        Assert.Equal(0m, item.GetProperty("volume").GetDecimal());
    }

    private async Task SeedCacheRowsAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.EnsureCreatedAsync();

        await dbContext.StockQuoteSnapshots.Where(item => item.Symbol == "sh600001").ExecuteDeleteAsync();
        await dbContext.KLinePoints.Where(item => item.Symbol == "sh600001").ExecuteDeleteAsync();

        dbContext.StockQuoteSnapshots.Add(new StockQuoteSnapshot
        {
            Symbol = "sh600001",
            Name = "cache-probe",
            Price = 10m,
            Change = 0m,
            ChangePercent = 0m,
            PeRatio = 10m,
            FloatMarketCap = 0m,
            VolumeRatio = 0m,
            Timestamp = new DateTime(2026, 4, 26, 10, 0, 0)
        });
        dbContext.KLinePoints.AddRange(
            new KLinePointEntity { Symbol = "sh600001", Interval = "day", Date = new DateTime(2026, 4, 25), Open = 10m, Close = 10m, High = 0m, Low = 0m, Volume = 100m },
            new KLinePointEntity { Symbol = "sh600001", Interval = "day", Date = new DateTime(2026, 4, 26), Open = 11m, Close = 11m, High = 12m, Low = 8m, Volume = 0m });

        await dbContext.SaveChangesAsync();
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var dataRoot = Path.Combine(Path.GetTempPath(), "sjai-tests", nameof(StockDetailCacheQueriesEndpointTests), Guid.NewGuid().ToString("N"));

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