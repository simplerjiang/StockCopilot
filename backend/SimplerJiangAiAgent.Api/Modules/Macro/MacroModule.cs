using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SimplerJiangAiAgent.Api.Data;

namespace SimplerJiangAiAgent.Api.Modules.Macro;

public sealed class MacroModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/macro").WithTags("Macro");

        group.MapGet("/", () => Results.Ok(new
        {
            indicators = new object[]
            {
                new { id = "deposit-rate", name = "存款基准利率", granularity = "event", available = true },
                new { id = "loan-rate", name = "贷款基准利率", granularity = "event", available = true },
                new { id = "money-supply-month", name = "月度货币供应量(M0/M1/M2)", granularity = "month", available = true },
                new { id = "money-supply-year", name = "年度货币供应量(M0/M1/M2)", granularity = "year", available = true },
                new { id = "shibor", name = "上海银行间同业拆放利率", granularity = "daily", available = false, note = "Baostock.NET 客户端方法未实现" }
            }
        }))
        .WithName("ListMacroIndicators")
        .WithOpenApi();

        group.MapGet("/{indicator}", async (
            string indicator,
            DateOnly? from,
            DateOnly? to,
            AppDbContext db,
            CancellationToken ct) =>
        {
            return indicator.ToLowerInvariant() switch
            {
                "deposit-rate" => Results.Ok(await QueryDepositRates(db, from, to, ct)),
                "loan-rate" => Results.Ok(await QueryLoanRates(db, from, to, ct)),
                "money-supply-month" => Results.Ok(await QueryMoneySupply(db, "month", from, to, ct)),
                "money-supply-year" => Results.Ok(await QueryMoneySupply(db, "year", from, to, ct)),
                "shibor" => Results.Ok(await QueryShibor(db, from, to, ct)),
                _ => Results.NotFound(new { error = "unknown_indicator", message = $"未知宏观指标: {indicator}" })
            };
        })
        .WithName("GetMacroIndicator")
        .WithOpenApi();

        group.MapGet("/summary", async (
            IMacroEnvironmentService macroService,
            CancellationToken ct) =>
        {
            var macro = await macroService.GetCurrentAsync(ct);
            if (macro is null)
                return Results.Ok(new { available = false, message = "宏观数据尚未采集" });

            return Results.Ok(new
            {
                available = true,
                policySignal = macro.PolicySignal,
                depositRate1Y = macro.DepositRate1Y,
                loanRate1Y = macro.LoanRate1Y,
                m2YoY = macro.M2YoY,
                m2Trend = macro.M2Trend,
                liquiditySignal = macro.LiquiditySignal,
                latestRateChange = macro.LatestRateChange,
                hasRecentChange = macro.HasRecentChange
            });
        })
        .WithName("GetMacroSummary")
        .WithOpenApi();
    }

    private static async Task<object> QueryDepositRates(AppDbContext db, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var query = db.MacroDepositRates.AsNoTracking().OrderBy(x => x.Date).AsQueryable();
        if (from.HasValue) query = query.Where(x => x.Date >= from.Value);
        if (to.HasValue) query = query.Where(x => x.Date <= to.Value);
        var data = await query.ToListAsync(ct);
        return new
        {
            indicator = "deposit-rate",
            name = "存款基准利率",
            count = data.Count,
            latestDate = data.LastOrDefault()?.Date.ToString("yyyy-MM-dd"),
            data = data.Select(r => new
            {
                date = r.Date.ToString("yyyy-MM-dd"),
                r.DemandDeposit,
                r.Fixed3M,
                r.Fixed6M,
                r.Fixed1Y,
                r.Fixed2Y,
                r.Fixed3Y,
                r.Fixed5Y
            })
        };
    }

    private static async Task<object> QueryLoanRates(AppDbContext db, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var query = db.MacroLoanRates.AsNoTracking().OrderBy(x => x.Date).AsQueryable();
        if (from.HasValue) query = query.Where(x => x.Date >= from.Value);
        if (to.HasValue) query = query.Where(x => x.Date <= to.Value);
        var data = await query.ToListAsync(ct);
        return new
        {
            indicator = "loan-rate",
            name = "贷款基准利率",
            count = data.Count,
            latestDate = data.LastOrDefault()?.Date.ToString("yyyy-MM-dd"),
            data = data.Select(r => new
            {
                date = r.Date.ToString("yyyy-MM-dd"),
                r.Loan6M,
                r.Loan6MTo1Y,
                r.Loan1YTo3Y,
                r.Loan3YTo5Y,
                r.Loan5YPlus
            })
        };
    }

    private static async Task<object> QueryMoneySupply(AppDbContext db, string granularity, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var query = db.MacroMoneySupplies.AsNoTracking()
            .Where(x => x.Granularity == granularity)
            .OrderBy(x => x.Date)
            .AsQueryable();
        if (from.HasValue) query = query.Where(x => x.Date >= from.Value);
        if (to.HasValue) query = query.Where(x => x.Date <= to.Value);
        var data = await query.ToListAsync(ct);
        var name = granularity == "month" ? "月度货币供应量(M0/M1/M2)" : "年度货币供应量(M0/M1/M2)";
        return new
        {
            indicator = $"money-supply-{granularity}",
            name,
            count = data.Count,
            latestDate = data.LastOrDefault()?.Date.ToString("yyyy-MM-dd"),
            data = data.Select(r => new
            {
                date = r.Date.ToString("yyyy-MM-dd"),
                r.M0,
                r.M0YoY,
                r.M0MoM,
                r.M1,
                r.M1YoY,
                r.M1MoM,
                r.M2,
                r.M2YoY,
                r.M2MoM
            })
        };
    }

    private static async Task<object> QueryShibor(AppDbContext db, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var query = db.MacroShibors.AsNoTracking().OrderBy(x => x.Date).AsQueryable();
        if (from.HasValue) query = query.Where(x => x.Date >= from.Value);
        if (to.HasValue) query = query.Where(x => x.Date <= to.Value);
        var data = await query.ToListAsync(ct);
        return new
        {
            indicator = "shibor",
            name = "上海银行间同业拆放利率",
            count = data.Count,
            latestDate = data.LastOrDefault()?.Date.ToString("yyyy-MM-dd"),
            data = data.Select(r => new
            {
                date = r.Date.ToString("yyyy-MM-dd"),
                r.Overnight,
                r.Week1,
                r.Week2,
                r.Month1,
                r.Month3,
                r.Month6,
                r.Month9,
                r.Year1
            })
        };
    }
}
