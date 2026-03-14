using Microsoft.Extensions.Options;

namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs;

public sealed class LocalFactIngestionWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LocalFactIngestionWorker> _logger;
    private readonly StockSyncOptions _options;

    public LocalFactIngestionWorker(
        IServiceProvider serviceProvider,
        ILogger<LocalFactIngestionWorker> logger,
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
                using var scope = _serviceProvider.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ILocalFactIngestionService>();
                await service.SyncAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "同步本地事实库失败");
            }

            var delay = TimeSpan.FromSeconds(Math.Max(30, _options.IntervalSeconds));
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
}