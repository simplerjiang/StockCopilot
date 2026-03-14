using Microsoft.Extensions.Options;

namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs;

public sealed class StockSyncWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StockSyncWorker> _logger;
    private readonly StockSyncOptions _options;

    public StockSyncWorker(
        IServiceProvider serviceProvider,
        ILogger<StockSyncWorker> logger,
        IOptions<StockSyncOptions> options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "同步股票数据失败");
            }

            var delay = TimeSpan.FromSeconds(Math.Max(10, _options.IntervalSeconds));
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task SyncOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IStockSyncService>();
        await service.SyncOnceAsync(cancellationToken);
    }
}
