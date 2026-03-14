using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Infrastructure.Jobs;

namespace SimplerJiangAiAgent.Api.Tests;

public class ActiveWatchlistServiceTests
{
    [Fact]
    public async Task UpsertAsync_ShouldNormalizeAndUpdateExistingSymbol()
    {
        await using var dbContext = CreateDbContext();
        var service = new ActiveWatchlistService(dbContext);

        var created = await service.UpsertAsync("600000", "浦发银行", "manual", "first", true);
        var updated = await service.UpsertAsync("sh600000", "浦发银行", "plan", "second", false);

        Assert.Equal(created.Id, updated.Id);
        Assert.Single(dbContext.ActiveWatchlists);
        Assert.Equal("sh600000", updated.Symbol);
        Assert.Equal("plan", updated.SourceTag);
        Assert.Equal("second", updated.Note);
        Assert.False(updated.IsEnabled);
    }

    [Fact]
    public async Task TouchAsync_ShouldPreserveExistingMetadataWhenPayloadMissing()
    {
        await using var dbContext = CreateDbContext();
        var service = new ActiveWatchlistService(dbContext);

        await service.UpsertAsync("000001", "平安银行", "history", "keep", false);
        var touched = await service.TouchAsync("sz000001");

        Assert.Equal("history", touched.SourceTag);
        Assert.Equal("keep", touched.Note);
        Assert.True(touched.IsEnabled);
        Assert.Equal("sz000001", touched.Symbol);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new AppDbContext(options);
    }
}