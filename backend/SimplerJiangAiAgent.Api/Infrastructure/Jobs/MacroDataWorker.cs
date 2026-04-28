using System.Globalization;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Services;

namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs;

public sealed class MacroDataWorker : BackgroundService
{
    private readonly IBaostockClientFactory _clientFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MacroDataWorker> _logger;

    public MacroDataWorker(IBaostockClientFactory clientFactory,
        IServiceScopeFactory scopeFactory, ILogger<MacroDataWorker> logger)
    {
        _clientFactory = clientFactory;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for Baostock connection pool and calendar to be ready
        await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);

        await RefreshAllAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Next run: 1st of next month at 04:00
            var now = DateTime.Now;
            var nextMonth = new DateTime(now.Year, now.Month, 1).AddMonths(1).AddHours(4);
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

            await RefreshAllAsync(stoppingToken);
        }
    }

    private async Task RefreshAllAsync(CancellationToken ct)
    {
        _logger.LogInformation("MacroDataWorker: starting macro data refresh");

        await SafeRefreshAsync("DepositRate", RefreshDepositRatesAsync, ct);
        await SafeRefreshAsync("LoanRate", RefreshLoanRatesAsync, ct);
        await SafeRefreshAsync("MoneySupplyMonth", RefreshMoneySupplyMonthAsync, ct);
        await SafeRefreshAsync("MoneySupplyYear", RefreshMoneySupplyYearAsync, ct);
        // Shibor: QueryShiborDataAsync is not yet implemented in Baostock.NET client

        _logger.LogInformation("MacroDataWorker: macro data refresh completed");
    }

    private async Task SafeRefreshAsync(string type, Func<CancellationToken, Task> action, CancellationToken ct)
    {
        try
        {
            await action(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MacroDataWorker: failed to refresh {Type}", type);
        }
    }

    private async Task RefreshDepositRatesAsync(CancellationToken ct)
    {
        await using var lease = await _clientFactory.GetClientAsync(ct);
        var rows = new List<MacroDepositRate>();

        await foreach (var row in lease.Client.QueryDepositRateDataAsync(ct: ct))
        {
            if (string.IsNullOrEmpty(row.PubDate)) continue;
            if (!DateOnly.TryParseExact(row.PubDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                continue;

            rows.Add(new MacroDepositRate
            {
                Date = date,
                DemandDeposit = ParseDecimal(row.DemandDepositRate),
                Fixed3M = ParseDecimal(row.FixedDepositRate3Month),
                Fixed6M = ParseDecimal(row.FixedDepositRate6Month),
                Fixed1Y = ParseDecimal(row.FixedDepositRate1Year),
                Fixed2Y = ParseDecimal(row.FixedDepositRate2Year),
                Fixed3Y = ParseDecimal(row.FixedDepositRate3Year),
                Fixed5Y = ParseDecimal(row.FixedDepositRate5Year),
                CreatedAt = DateTime.UtcNow
            });
        }

        if (rows.Count > 0)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.MacroDepositRates.RemoveRange(db.MacroDepositRates);
            db.MacroDepositRates.AddRange(rows);
            await db.SaveChangesAsync(ct);
        }

        _logger.LogInformation("Macro {Type}: {Count} rows", "DepositRate", rows.Count);
    }

    private async Task RefreshLoanRatesAsync(CancellationToken ct)
    {
        await using var lease = await _clientFactory.GetClientAsync(ct);
        var rows = new List<MacroLoanRate>();

        await foreach (var row in lease.Client.QueryLoanRateDataAsync(ct: ct))
        {
            if (string.IsNullOrEmpty(row.PubDate)) continue;
            if (!DateOnly.TryParseExact(row.PubDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                continue;

            rows.Add(new MacroLoanRate
            {
                Date = date,
                Loan6M = ParseDecimal(row.LoanRate6Month),
                Loan6MTo1Y = ParseDecimal(row.LoanRate6MonthTo1Year),
                Loan1YTo3Y = ParseDecimal(row.LoanRate1YearTo3Year),
                Loan3YTo5Y = ParseDecimal(row.LoanRate3YearTo5Year),
                Loan5YPlus = ParseDecimal(row.LoanRateAbove5Year),
                CreatedAt = DateTime.UtcNow
            });
        }

        if (rows.Count > 0)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.MacroLoanRates.RemoveRange(db.MacroLoanRates);
            db.MacroLoanRates.AddRange(rows);
            await db.SaveChangesAsync(ct);
        }

        _logger.LogInformation("Macro {Type}: {Count} rows", "LoanRate", rows.Count);
    }

    private async Task RefreshMoneySupplyMonthAsync(CancellationToken ct)
    {
        await using var lease = await _clientFactory.GetClientAsync(ct);
        var rows = new List<MacroMoneySupply>();

        await foreach (var row in lease.Client.QueryMoneySupplyDataMonthAsync(ct: ct))
        {
            if (string.IsNullOrEmpty(row.StatYear) || string.IsNullOrEmpty(row.StatMonth)) continue;
            if (!int.TryParse(row.StatYear, out var year) || !int.TryParse(row.StatMonth, out var month))
                continue;

            rows.Add(new MacroMoneySupply
            {
                Granularity = "month",
                Date = new DateOnly(year, month, 1),
                M0 = ParseDecimal(row.M0Month),
                M0YoY = ParseDecimal(row.M0YOY),
                M0MoM = ParseDecimal(row.M0ChainRelative),
                M1 = ParseDecimal(row.M1Month),
                M1YoY = ParseDecimal(row.M1YOY),
                M1MoM = ParseDecimal(row.M1ChainRelative),
                M2 = ParseDecimal(row.M2Month),
                M2YoY = ParseDecimal(row.M2YOY),
                M2MoM = ParseDecimal(row.M2ChainRelative),
                CreatedAt = DateTime.UtcNow
            });
        }

        if (rows.Count > 0)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var existing = db.MacroMoneySupplies.Where(x => x.Granularity == "month");
            db.MacroMoneySupplies.RemoveRange(existing);
            db.MacroMoneySupplies.AddRange(rows);
            await db.SaveChangesAsync(ct);
        }

        _logger.LogInformation("Macro {Type}: {Count} rows", "MoneySupplyMonth", rows.Count);
    }

    private async Task RefreshMoneySupplyYearAsync(CancellationToken ct)
    {
        await using var lease = await _clientFactory.GetClientAsync(ct);
        var rows = new List<MacroMoneySupply>();

        await foreach (var row in lease.Client.QueryMoneySupplyDataYearAsync(ct: ct))
        {
            if (string.IsNullOrEmpty(row.StatYear)) continue;
            if (!int.TryParse(row.StatYear, out var year)) continue;

            rows.Add(new MacroMoneySupply
            {
                Granularity = "year",
                Date = new DateOnly(year, 1, 1),
                M0 = ParseDecimal(row.M0Year),
                M0YoY = ParseDecimal(row.M0YearYOY),
                M0MoM = null,
                M1 = ParseDecimal(row.M1Year),
                M1YoY = ParseDecimal(row.M1YearYOY),
                M1MoM = null,
                M2 = ParseDecimal(row.M2Year),
                M2YoY = ParseDecimal(row.M2YearYOY),
                M2MoM = null,
                CreatedAt = DateTime.UtcNow
            });
        }

        if (rows.Count > 0)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var existing = db.MacroMoneySupplies.Where(x => x.Granularity == "year");
            db.MacroMoneySupplies.RemoveRange(existing);
            db.MacroMoneySupplies.AddRange(rows);
            await db.SaveChangesAsync(ct);
        }

        _logger.LogInformation("Macro {Type}: {Count} rows", "MoneySupplyYear", rows.Count);
    }

    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : null;
    }
}
