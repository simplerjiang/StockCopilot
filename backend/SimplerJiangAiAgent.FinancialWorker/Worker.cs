using SimplerJiangAiAgent.FinancialWorker.Data;
using SimplerJiangAiAgent.FinancialWorker.Models;
using SimplerJiangAiAgent.FinancialWorker.Services;

namespace SimplerJiangAiAgent.FinancialWorker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly FinancialDbContext _db;
    private readonly FinancialDataOrchestrator _orchestrator;

    private static readonly TimeZoneInfo ChinaTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");

    // 采集窗口：收盘后 15:30 ~ 23:00 (UTC+8)
    private static readonly TimeSpan WindowStart = new(15, 30, 0);
    private static readonly TimeSpan WindowEnd = new(23, 0, 0);

    private DateTime? _lastCollectionDateUtc;

    public Worker(ILogger<Worker> logger, FinancialDbContext db, FinancialDataOrchestrator orchestrator)
    {
        _logger = logger;
        _db = db;
        _orchestrator = orchestrator;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "FinancialWorker started. DB: Reports={Reports}, Indicators={Indicators}, Dividends={Dividends}, MarginTrading={Margin}",
            _db.Reports.Count(), _db.Indicators.Count(), _db.Dividends.Count(), _db.MarginTrading.Count());

        var config = _db.Config.FindById(1) ?? new FinancialCollectionConfig();
        _logger.LogInformation(
            "Config: Enabled={Enabled}, Scope={Scope}, Frequency={Freq}, Watchlist=[{Symbols}]",
            config.Enabled, config.Scope, config.Frequency,
            string.Join(",", config.WatchlistSymbols));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunScheduleCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduler cycle error, will retry next cycle");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }

        _logger.LogInformation("FinancialWorker stopped");
    }

    private async Task RunScheduleCycleAsync(CancellationToken ct)
    {
        // 重新读取配置（可能被 API 修改）
        var config = _db.Config.FindById(1) ?? new FinancialCollectionConfig();

        if (!config.Enabled)
            return;

        if (string.Equals(config.Frequency, "Manual", StringComparison.OrdinalIgnoreCase))
            return;

        var nowChina = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ChinaTimeZone);

        // 检查是否在采集窗口内
        if (nowChina.TimeOfDay < WindowStart || nowChina.TimeOfDay > WindowEnd)
            return;

        // 检查是否已采集过
        if (AlreadyCollected(config.Frequency, nowChina))
            return;

        // 确定采集范围
        var symbols = ResolveSymbols(config);
        if (symbols.Count == 0)
        {
            _logger.LogWarning("No symbols to collect (Scope={Scope}, WatchlistCount={Count})",
                config.Scope, config.WatchlistSymbols.Count);
            return;
        }

        _logger.LogInformation("Scheduled collection starting: {Count} symbols, Frequency={Freq}",
            symbols.Count, config.Frequency);

        var results = await _orchestrator.CollectBatchAsync(symbols, ct);

        var ok = results.Count(r => r.Success);
        var fail = results.Count - ok;
        _logger.LogInformation("Scheduled collection done: {Ok} succeeded, {Fail} failed", ok, fail);

        // 记录本次采集时间
        _lastCollectionDateUtc = DateTime.UtcNow;
    }

    private bool AlreadyCollected(string frequency, DateTime nowChina)
    {
        if (_lastCollectionDateUtc == null)
            return false;

        var lastChina = TimeZoneInfo.ConvertTimeFromUtc(_lastCollectionDateUtc.Value, ChinaTimeZone);

        return frequency.ToUpperInvariant() switch
        {
            "DAILY" => lastChina.Date == nowChina.Date,
            "WEEKLY" => IsoWeek(lastChina) == IsoWeek(nowChina) && lastChina.Year == nowChina.Year,
            _ => false,
        };
    }

    private static List<string> ResolveSymbols(FinancialCollectionConfig config)
    {
        // Phase 1: 仅支持 Watchlist 模式
        if (string.Equals(config.Scope, "All", StringComparison.OrdinalIgnoreCase))
        {
            // TODO: Phase 2 支持全量股票列表
            return config.WatchlistSymbols;
        }

        return config.WatchlistSymbols;
    }

    private static int IsoWeek(DateTime dt)
    {
        var day = System.Globalization.CultureInfo.InvariantCulture.Calendar
            .GetWeekOfYear(dt, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        return day;
    }
}
