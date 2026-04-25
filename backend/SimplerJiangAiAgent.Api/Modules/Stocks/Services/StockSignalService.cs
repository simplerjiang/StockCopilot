using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface IStockSignalService
{
    StockSignalDto Evaluate(StockDetailDto detail, StockNewsImpactDto impact);
}

public sealed class StockSignalService : IStockSignalService
{
    public StockSignalDto Evaluate(StockDetailDto detail, StockNewsImpactDto impact)
    {
        var eventImpactScore = Math.Clamp((impact.Summary.Positive - impact.Summary.Negative) * 20, -100, 100);
        var trendScore = CalculateTrendScore(detail.KLines, detail.Quote.ChangePercent);
        var alignmentScore = CalculateAlignmentScore(eventImpactScore, trendScore);
        var confidence = Math.Clamp((Math.Abs(eventImpactScore) + Math.Abs(trendScore) + alignmentScore) / 3, 0, 100);

        // 最小样本量保护：证据过少时 cap 置信度上限
        var totalEvidenceCount = impact.Summary.Positive + impact.Summary.Negative;
        if (totalEvidenceCount < 3)
            confidence = Math.Min(confidence, 20);
        else if (totalEvidenceCount < 5)
            confidence = Math.Min(confidence, 35);

        var recommendation = BuildRecommendation(eventImpactScore, trendScore, alignmentScore);

        var evidence = BuildEvidence(detail, impact, trendScore, alignmentScore, totalEvidenceCount);
        var counterEvidence = BuildCounterEvidence(detail, impact, trendScore, alignmentScore);
        var signals = BuildSignals(recommendation, eventImpactScore, trendScore, alignmentScore);

        return new StockSignalDto(
            detail.Quote.Symbol,
            detail.Quote.Name,
            DateTime.Now,
            recommendation,
            confidence,
            eventImpactScore,
            trendScore,
            alignmentScore,
            signals,
            evidence,
            counterEvidence
        );
    }

    private static int CalculateTrendScore(IReadOnlyList<KLinePointDto> kLines, decimal changePercent)
    {
        if (kLines.Count < 5)
        {
            return Math.Clamp((int)Math.Round(changePercent * 5), -100, 100);
        }

        var recent = kLines.TakeLast(5).ToArray();
        var closes = recent.Select(item => item.Close).ToArray();
        var first = closes.First();
        var last = closes.Last();
        var returnPercent = first == 0 ? 0 : (last - first) / first * 100m;
        var upDays = 0;
        for (var i = 1; i < closes.Length; i++)
        {
            if (closes[i] >= closes[i - 1])
            {
                upDays++;
            }
        }

        var score = returnPercent * 6 + upDays * 5;
        return Math.Clamp((int)Math.Round(score), -100, 100);
    }

    private static int CalculateAlignmentScore(int eventImpactScore, int trendScore)
    {
        if (eventImpactScore == 0 || trendScore == 0)
        {
            return 50;
        }

        var sameDirection = Math.Sign(eventImpactScore) == Math.Sign(trendScore);
        var distance = Math.Abs(Math.Abs(eventImpactScore) - Math.Abs(trendScore));
        var baseScore = sameDirection ? 80 : 20;
        return Math.Clamp(baseScore - distance / 4, 0, 100);
    }

    private static string BuildRecommendation(int eventImpactScore, int trendScore, int alignmentScore)
    {
        var total = eventImpactScore + trendScore + (alignmentScore - 50);
        if (total >= 40)
        {
            return "偏多";
        }

        if (total <= -40)
        {
            return "偏空";
        }

        return "观望";
    }

    private static IReadOnlyList<string> BuildSignals(string recommendation, int eventImpactScore, int trendScore, int alignmentScore)
    {
        var list = new List<string>
        {
            $"事件分:{eventImpactScore}",
            $"趋势分:{trendScore}",
            $"历史对齐:{alignmentScore}",
            $"建议:{recommendation}"
        };
        return list;
    }

    private static IReadOnlyList<string> BuildEvidence(StockDetailDto detail, StockNewsImpactDto impact, int trendScore, int alignmentScore, int totalEvidenceCount)
    {
        var list = new List<string>();
        if (totalEvidenceCount < 3)
        {
            list.Add($"⚠️ 样本不足（仅{totalEvidenceCount}条证据，置信度已下调）");
        }
        else if (totalEvidenceCount < 5)
        {
            list.Add($"⚠️ 样本偏少（{totalEvidenceCount}条证据，置信度已下调）");
        }
        if (impact.Summary.Positive > impact.Summary.Negative)
        {
            list.Add($"利好事件多于利空（{impact.Summary.Positive}:{impact.Summary.Negative}）");
        }
        if (trendScore > 0)
        {
            list.Add($"近5日趋势偏强（趋势分:{trendScore}）");
        }
        if (alignmentScore >= 60)
        {
            list.Add($"事件与价格方向一致（对齐:{alignmentScore}）");
        }
        if (detail.Quote.ChangePercent > 0)
        {
            list.Add($"当日涨幅为正（{detail.Quote.ChangePercent:0.00}%）");
        }
        return list;
    }

    private static IReadOnlyList<string> BuildCounterEvidence(StockDetailDto detail, StockNewsImpactDto impact, int trendScore, int alignmentScore)
    {
        var list = new List<string>();
        if (impact.Summary.Negative > 0)
        {
            list.Add($"仍存在利空事件（{impact.Summary.Negative}条）");
        }
        if (trendScore < 0)
        {
            list.Add($"近5日趋势偏弱（趋势分:{trendScore}）");
        }
        if (alignmentScore < 50)
        {
            list.Add($"事件与价格方向背离（对齐:{alignmentScore}）");
        }
        if (detail.Quote.TurnoverRate >= 8)
        {
            list.Add($"换手率偏高（{detail.Quote.TurnoverRate:0.00}%），短线波动风险增加");
        }
        return list;
    }
}
