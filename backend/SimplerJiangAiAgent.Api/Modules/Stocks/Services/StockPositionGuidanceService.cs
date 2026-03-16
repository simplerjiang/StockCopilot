using SimplerJiangAiAgent.Api.Modules.Market.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface IStockPositionGuidanceService
{
    StockPositionGuidanceDto Build(StockQuoteDto quote, StockSignalDto signal, string riskLevel, decimal currentPositionPercent, StockMarketContextDto? marketContext);
}

public sealed class StockPositionGuidanceService : IStockPositionGuidanceService
{
    public StockPositionGuidanceDto Build(StockQuoteDto quote, StockSignalDto signal, string riskLevel, decimal currentPositionPercent, StockMarketContextDto? marketContext)
    {
        var profile = NormalizeRiskLevel(riskLevel);
        var baseTarget = profile switch
        {
            "conservative" => 25m,
            "balanced" => 45m,
            _ => 65m
        };

        var signalBias = signal.Recommendation switch
        {
            "偏多" => 15m,
            "偏空" => -20m,
            _ => -5m
        };

        var confidenceBias = (signal.Confidence - 50) / 2m;
    var rawTarget = Math.Clamp(baseTarget + signalBias + confidenceBias, 0, 100);
    var marketStageMultiplier = marketContext?.SuggestedPositionScale ?? 1m;
    var target = Math.Clamp(rawTarget * marketStageMultiplier, 0, 100);

        var action = (target - currentPositionPercent) switch
        {
            > 8m => "加仓",
            < -8m => "减仓",
            _ => "持有"
        };

        var maxDrawdown = profile switch
        {
            "conservative" => 6m,
            "balanced" => 10m,
            _ => 14m
        };

        var stopLoss = profile switch
        {
            "conservative" => 4m,
            "balanced" => 6m,
            _ => 8m
        };

        var takeProfit = profile switch
        {
            "conservative" => 10m,
            "balanced" => 14m,
            _ => 18m
        };

        var reasons = new List<string>
        {
            $"风险档位:{profile}",
            $"信号建议:{signal.Recommendation}（置信度:{signal.Confidence}）",
            $"当前仓位:{currentPositionPercent:0.##}% 目标仓位:{target:0.##}%",
            $"当日涨跌:{quote.ChangePercent:0.##}% 换手:{quote.TurnoverRate:0.##}%"
        };

        if (marketContext is not null)
        {
            reasons.Add($"市场阶段:{marketContext.StageLabel}（置信度:{marketContext.StageConfidence:0.##}）");
            reasons.Add($"市场仓位系数:{marketStageMultiplier:0.##}");
            reasons.Add($"执行节奏:{marketContext.ExecutionFrequencyLabel}");
            if (marketContext.CounterTrendWarning)
            {
                reasons.Add("当前标的与市场主线不完全同频，执行时应降低追价频率。");
            }
        }

        return new StockPositionGuidanceDto(
            quote.Symbol,
            quote.Name,
            DateTime.Now,
            profile,
            action,
            Math.Round(target, 2),
            maxDrawdown,
            stopLoss,
            takeProfit,
            Math.Round(marketStageMultiplier, 4),
            marketContext,
            reasons
        );
    }

    private static string NormalizeRiskLevel(string? riskLevel)
    {
        var value = riskLevel?.Trim().ToLowerInvariant();
        return value switch
        {
            "conservative" or "稳健" => "conservative",
            "aggressive" or "激进" => "aggressive",
            _ => "balanced"
        };
    }
}
