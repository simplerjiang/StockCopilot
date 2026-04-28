using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Macro;

public interface IMacroEnvironmentService
{
    Task<MacroEnvironmentContextDto?> GetCurrentAsync(CancellationToken ct = default);
}

public sealed class MacroEnvironmentService : IMacroEnvironmentService
{
    private readonly AppDbContext _db;

    public MacroEnvironmentService(AppDbContext db) => _db = db;

    public async Task<MacroEnvironmentContextDto?> GetCurrentAsync(CancellationToken ct = default)
    {
        var depositRates = await _db.MacroDepositRates
            .OrderByDescending(x => x.Date)
            .Take(2)
            .ToListAsync(ct);

        var loanRates = await _db.MacroLoanRates
            .OrderByDescending(x => x.Date)
            .Take(2)
            .ToListAsync(ct);

        var m2Data = await _db.MacroMoneySupplies
            .Where(x => x.Granularity == "month")
            .OrderByDescending(x => x.Date)
            .Take(3)
            .ToListAsync(ct);

        if (depositRates.Count == 0 && loanRates.Count == 0 && m2Data.Count == 0)
            return null;

        string? latestRateChange = null;
        bool hasRecentChange = false;
        var latestDeposit = depositRates.FirstOrDefault();
        var prevDeposit = depositRates.Count > 1 ? depositRates[1] : null;

        if (latestDeposit != null && prevDeposit != null && latestDeposit.Fixed1Y != prevDeposit.Fixed1Y)
        {
            var direction = latestDeposit.Fixed1Y > prevDeposit.Fixed1Y ? "↑" : "↓";
            latestRateChange = $"{latestDeposit.Date:yyyy-MM-dd} 一年期存款利率 {prevDeposit.Fixed1Y}%→{latestDeposit.Fixed1Y}%{direction}";
            if (latestDeposit.Date >= DateOnly.FromDateTime(DateTime.Now.AddDays(-30)))
                hasRecentChange = true;
        }

        string? m2Trend = null;
        if (m2Data.Count >= 2)
        {
            var latest = m2Data[0].M2YoY;
            var prev = m2Data[1].M2YoY;
            if (latest.HasValue && prev.HasValue)
            {
                var diff = latest.Value - prev.Value;
                m2Trend = diff > 0.3m ? "上行" : diff < -0.3m ? "下行" : "平稳";
            }
        }

        string? liquiditySignal = null;
        var latestM = m2Data.FirstOrDefault();
        if (latestM?.M1YoY.HasValue == true && latestM?.M2YoY.HasValue == true)
        {
            var spread = latestM.M1YoY!.Value - latestM.M2YoY!.Value;
            liquiditySignal = spread > 0 ? $"M1-M2差值+{spread:F1}%，资金活化"
                : $"M1-M2差值{spread:F1}%，资金沉淀";
        }

        string policySignal = "中性";
        if (latestDeposit?.Fixed1Y < prevDeposit?.Fixed1Y) policySignal = "偏宽松";
        if (latestDeposit?.Fixed1Y > prevDeposit?.Fixed1Y) policySignal = "偏紧缩";

        return new MacroEnvironmentContextDto(
            PolicySignal: policySignal,
            LatestRateChange: latestRateChange,
            DepositRate1Y: latestDeposit?.Fixed1Y,
            LoanRate1Y: loanRates.FirstOrDefault()?.Loan6MTo1Y,
            M2YoY: latestM?.M2YoY,
            M2Trend: m2Trend,
            LiquiditySignal: liquiditySignal,
            HasRecentChange: hasRecentChange);
    }
}
