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

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class MacroModuleTests : IClassFixture<MacroModuleTests.Factory>
{
    private readonly Factory _factory;

    public MacroModuleTests(Factory factory)
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

            db.MacroDepositRates.AddRange(
                new MacroDepositRate { Date = new DateOnly(2020, 1, 1), DemandDeposit = 0.35m, Fixed1Y = 1.5m },
                new MacroDepositRate { Date = new DateOnly(2021, 6, 15), DemandDeposit = 0.35m, Fixed1Y = 1.5m },
                new MacroDepositRate { Date = new DateOnly(2022, 9, 1), DemandDeposit = 0.25m, Fixed1Y = 1.65m }
            );

            db.MacroLoanRates.Add(
                new MacroLoanRate { Date = new DateOnly(2020, 1, 1), Loan6M = 4.35m }
            );

            db.SaveChanges();
            _seeded = true;
        }
    }

    [Fact]
    public async Task ListIndicators_Returns5Indicators()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/macro/");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var indicators = body.GetProperty("indicators");
        Assert.Equal(5, indicators.GetArrayLength());
    }

    [Fact]
    public async Task GetDepositRate_ReturnsCorrectFormat()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/macro/deposit-rate");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("deposit-rate", body.GetProperty("indicator").GetString());
        Assert.Equal("存款基准利率", body.GetProperty("name").GetString());
        Assert.True(body.GetProperty("count").GetInt32() >= 0);
        Assert.True(body.TryGetProperty("data", out _));
    }

    [Fact]
    public async Task GetUnknownIndicator_Returns404()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/macro/unknown-thing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetIndicator_WithDateRange_FiltersCorrectly()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/macro/deposit-rate?from=2021-01-01&to=2021-12-31");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var count = body.GetProperty("count").GetInt32();
        Assert.Equal(1, count);

        var data = body.GetProperty("data");
        var first = data[0];
        Assert.Equal("2021-06-15", first.GetProperty("date").GetString());
    }

    [Theory]
    [InlineData("Deposit-Rate")]
    [InlineData("deposit-rate")]
    [InlineData("DEPOSIT-RATE")]
    public async Task GetIndicator_CaseInsensitive(string indicator)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/macro/{indicator}");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("deposit-rate", body.GetProperty("indicator").GetString());
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var dataRoot = Path.Combine(Path.GetTempPath(), "sjai-tests", nameof(MacroModuleTests), Guid.NewGuid().ToString("N"));

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
