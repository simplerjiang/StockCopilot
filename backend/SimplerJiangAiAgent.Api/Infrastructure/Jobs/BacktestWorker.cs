using SimplerJiangAiAgent.Api.Modules.Backtest;

namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs;

public sealed class BacktestWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BacktestWorker> _logger;

    public BacktestWorker(IServiceProvider serviceProvider, ILogger<BacktestWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Startup delay — let other workers finish first
        await Task.Delay(TimeSpan.FromSeconds(120), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunBacktestScanAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BacktestWorker error");
            }

            // Run every 6 hours
            await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
        }
    }

    private async Task RunBacktestScanAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var backtestService = scope.ServiceProvider.GetRequiredService<IBacktestService>();

        _logger.LogInformation("BacktestWorker: starting scan");
        var result = await backtestService.RunBatchAsync(null, null, null, ct);
        _logger.LogInformation("BacktestWorker: completed. Total={Total}, Success={Success}, Skipped={Skipped}, Failed={Failed}",
            result.Total, result.Success, result.Skipped, result.Failed);
    }
}
