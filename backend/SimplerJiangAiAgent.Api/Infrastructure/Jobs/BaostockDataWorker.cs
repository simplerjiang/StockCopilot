using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Services;

namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs;

public sealed class BaostockDataWorker : BackgroundService
{
    private readonly IBaostockClientFactory _clientFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BaostockDataWorker> _logger;

    public BaostockDataWorker(IBaostockClientFactory clientFactory,
        IServiceScopeFactory scopeFactory, ILogger<BaostockDataWorker> logger)
    {
        _clientFactory = clientFactory;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for calendar service and other workers to initialize
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        await RefreshIndexConstituentsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var nextMonth = new DateTime(now.Year, now.Month, 1).AddMonths(1).AddHours(18);
            var delay = nextMonth - now;
            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            await RefreshIndexConstituentsAsync(stoppingToken);
        }
    }

    private async Task RefreshIndexConstituentsAsync(CancellationToken ct)
    {
        try
        {
            await using var lease = await _clientFactory.GetClientAsync(ct);
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            await RefreshSingleIndexAsync(db, "HS300",
                lease.Client.QueryHs300StocksAsync(ct: ct), ct);
            await RefreshSingleIndexAsync(db, "SZ50",
                lease.Client.QuerySz50StocksAsync(ct: ct), ct);
            await RefreshSingleIndexAsync(db, "ZZ500",
                lease.Client.QueryZz500StocksAsync(ct: ct), ct);

            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Index constituent refresh completed");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh index constituents from Baostock");
        }
    }

    private async Task RefreshSingleIndexAsync(AppDbContext db,
        string indexCode, IAsyncEnumerable<Baostock.NET.Models.IndexConstituentRow> stream,
        CancellationToken ct)
    {
        var newRows = new List<IndexConstituentSnapshot>();
        await foreach (var row in stream.WithCancellation(ct))
        {
            newRows.Add(new IndexConstituentSnapshot
            {
                IndexCode = indexCode,
                StockCode = row.Code,
                StockName = row.CodeName,
                UpdateDate = row.UpdateDate,
                CreatedAt = DateTime.UtcNow
            });
        }

        if (newRows.Count > 0)
        {
            var existing = db.IndexConstituents.Where(x => x.IndexCode == indexCode);
            db.IndexConstituents.RemoveRange(existing);
            db.IndexConstituents.AddRange(newRows);
            _logger.LogInformation("Refreshed {Index}: {Count} constituents", indexCode, newRows.Count);
        }
    }
}
