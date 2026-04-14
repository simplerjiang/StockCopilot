namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs.ForumScraping;

public sealed class ForumScrapingWorker : BackgroundService
{
    private static readonly TimeZoneInfo ChinaTimeZone = ResolveChinaTimeZone();

    // Beijing time schedule: 09:00, 12:00, 15:30
    private static readonly TimeSpan[] ScheduleTimes =
    [
        new(9, 0, 0),
        new(12, 0, 0),
        new(15, 30, 0)
    ];

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ForumScrapingWorker> _logger;

    public ForumScrapingWorker(IServiceProvider serviceProvider, ILogger<ForumScrapingWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = ComputeDelayUntilNext(DateTime.UtcNow);
            _logger.LogDebug("论坛帖子采集 Worker: 下次执行延迟 {Delay}", delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            // 周末跳过采集
            var chinaTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ChinaTimeZone);
            if (chinaTime.DayOfWeek == DayOfWeek.Saturday || chinaTime.DayOfWeek == DayOfWeek.Sunday)
            {
                _logger.LogInformation("ForumScrapingWorker: Skipping weekend");
                continue;
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IForumScrapingService>();
                await service.ScrapeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "论坛帖子采集 Worker 执行失败");
            }
        }
    }

    internal static TimeSpan ComputeDelayUntilNext(DateTime utcNow)
    {
        var chinaTime = TimeZoneInfo.ConvertTimeFromUtc(utcNow, ChinaTimeZone);
        var today = chinaTime.Date;
        var now = chinaTime.TimeOfDay;

        // Find the next schedule time today
        foreach (var scheduleTime in ScheduleTimes)
        {
            if (now < scheduleTime)
            {
                var target = today + scheduleTime;
                var targetUtc = TimeZoneInfo.ConvertTimeToUtc(target, ChinaTimeZone);
                return targetUtc - utcNow;
            }
        }

        // All today's slots passed; wait for first slot tomorrow
        var tomorrowFirst = today.AddDays(1) + ScheduleTimes[0];
        var tomorrowFirstUtc = TimeZoneInfo.ConvertTimeToUtc(tomorrowFirst, ChinaTimeZone);
        return tomorrowFirstUtc - utcNow;
    }

    private static TimeZoneInfo ResolveChinaTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("China Standard Time", TimeSpan.FromHours(8), "China Standard Time", "China Standard Time");
        }
    }
}
