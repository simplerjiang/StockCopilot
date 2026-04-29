using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Modules.Macro;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class MacroEnvironmentServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;

    public MacroEnvironmentServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task GetCurrentAsync_NoData_ReturnsNull()
    {
        var service = new MacroEnvironmentService(_db);

        var result = await service.GetCurrentAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCurrentAsync_WithDepositRates_ReturnsPolicySignal()
    {
        _db.MacroDepositRates.AddRange(
            new MacroDepositRate { Date = new DateOnly(2024, 1, 1), Fixed1Y = 1.5m },
            new MacroDepositRate { Date = new DateOnly(2024, 6, 1), Fixed1Y = 1.35m });
        await _db.SaveChangesAsync();

        var service = new MacroEnvironmentService(_db);

        var result = await service.GetCurrentAsync();

        Assert.NotNull(result);
        Assert.NotNull(result.PolicySignal);
        Assert.Equal(1.35m, result.DepositRate1Y);
    }

    [Fact]
    public async Task GetCurrentAsync_RateDecrease_ReturnsBiasLoose()
    {
        _db.MacroDepositRates.AddRange(
            new MacroDepositRate { Date = new DateOnly(2024, 1, 1), Fixed1Y = 1.5m },
            new MacroDepositRate { Date = new DateOnly(2024, 6, 1), Fixed1Y = 1.35m });
        await _db.SaveChangesAsync();

        var service = new MacroEnvironmentService(_db);

        var result = await service.GetCurrentAsync();

        Assert.NotNull(result);
        Assert.Equal("偏宽松", result.PolicySignal);
    }

    [Fact]
    public async Task GetCurrentAsync_M2Trend_CorrectDirection()
    {
        _db.MacroMoneySupplies.AddRange(
            new MacroMoneySupply { Granularity = "month", Date = new DateOnly(2024, 3, 1), M2YoY = 8.0m },
            new MacroMoneySupply { Granularity = "month", Date = new DateOnly(2024, 4, 1), M2YoY = 8.5m });
        await _db.SaveChangesAsync();

        var service = new MacroEnvironmentService(_db);

        var result = await service.GetCurrentAsync();

        Assert.NotNull(result);
        Assert.Equal("上行", result.M2Trend); // diff=0.5 > 0.3 → 上行
    }

    [Fact]
    public async Task GetCurrentAsync_SingleRate_NeutralPolicy()
    {
        _db.MacroDepositRates.Add(
            new MacroDepositRate { Date = new DateOnly(2024, 1, 1), Fixed1Y = 1.5m });
        await _db.SaveChangesAsync();

        var service = new MacroEnvironmentService(_db);

        var result = await service.GetCurrentAsync();

        Assert.NotNull(result);
        Assert.Equal("中性", result.PolicySignal);
        Assert.Equal(1.5m, result.DepositRate1Y);
    }
}
