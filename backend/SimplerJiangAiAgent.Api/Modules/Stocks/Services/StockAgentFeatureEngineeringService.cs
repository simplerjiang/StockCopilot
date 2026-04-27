using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface IStockAgentFeatureEngineeringService
{
    StockAgentPreparedContextDto Prepare(
        string symbol,
        StockQuoteDto? quote,
        IReadOnlyList<KLinePointDto> kLines,
        IReadOnlyList<MinuteLinePointDto> minuteLines,
        IReadOnlyList<IntradayMessageDto> messages,
        StockAgentNewsPolicyDto newsPolicy,
        StockAgentLocalFactPackageDto localFacts,
        DateTime requestTime);
}

public sealed class StockAgentFeatureEngineeringService : IStockAgentFeatureEngineeringService
{
    private static readonly string[] MarketNoiseKeywords =
    {
        "cointelegraph", "crypto", "bitcoin", "ethereum", "btc", "eth", "狗狗币", "加密",
        "seeking alpha", "tesla", "apple", "nvidia", "meta", "nasdaq", "nyse", "美股个股"
    };

    public StockAgentPreparedContextDto Prepare(
        string symbol,
        StockQuoteDto? quote,
        IReadOnlyList<KLinePointDto> kLines,
        IReadOnlyList<MinuteLinePointDto> minuteLines,
        IReadOnlyList<IntradayMessageDto> messages,
        StockAgentNewsPolicyDto newsPolicy,
        StockAgentLocalFactPackageDto localFacts,
        DateTime requestTime)
    {
        var normalizedSymbol = StockSymbolNormalizer.Normalize(symbol);
        var filteredMarketReports = IsChinaAShareSymbol(normalizedSymbol)
            ? localFacts.MarketReports.Where(item => !IsMarketNoise(item)).ToArray()
            : localFacts.MarketReports.ToArray();
        var filteredCount = Math.Max(0, localFacts.MarketReports.Count - filteredMarketReports.Length);

        var filteredLocalFacts = filteredCount == 0
            ? localFacts
            : localFacts with { MarketReports = filteredMarketReports };

        var allEvidence = filteredLocalFacts.StockNews
            .Concat(filteredLocalFacts.SectorReports)
            .Concat(filteredLocalFacts.MarketReports)
            .ToArray();

        var sentimentBreakdown = allEvidence
            .GroupBy(item => item.Sentiment ?? "中性", StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => $"{group.Key}:{group.Count()}")
            .ToArray();

        var highQualityCount = allEvidence.Count(item => IsHighQualityEvidence(item.ReadStatus));
        var weakEvidenceCount = allEvidence.Count(item => IsWeakEvidence(item.ReadStatus));
        var recentEvidenceCount = allEvidence.Count(item => item.PublishTime >= requestTime.AddHours(-72));
        var freshnessHours = allEvidence.Length == 0
            ? 999m
            : decimal.Round((decimal)Math.Max(0, (requestTime - allEvidence.Max(item => item.PublishTime)).TotalHours), 2);
        var coverageScore = Math.Clamp(highQualityCount * 22m + recentEvidenceCount * 8m + filteredLocalFacts.FundamentalFacts.Count * 6m, 0m, 100m);

        var positiveCount = allEvidence.Count(item => string.Equals(item.Sentiment, "利好", StringComparison.OrdinalIgnoreCase));
        var negativeCount = allEvidence.Count(item => string.Equals(item.Sentiment, "利空", StringComparison.OrdinalIgnoreCase));
        var conflictScore = positiveCount + negativeCount == 0
            ? 0m
            : decimal.Round(Math.Min(positiveCount, negativeCount) * 200m / (positiveCount + negativeCount), 2);

        var ma5 = CalculateAverage(kLines.TakeLast(5).Select(item => item.Close));
        var ma20 = CalculateAverage(kLines.TakeLast(20).Select(item => item.Close));
        var return5d = CalculateReturnPercent(kLines.TakeLast(5).Select(item => item.Close).ToArray());
        var return20d = CalculateReturnPercent(kLines.TakeLast(20).Select(item => item.Close).ToArray());
        var atrPercent = CalculateAtrPercent(kLines);
        var latestClose = kLines.LastOrDefault()?.Close ?? quote?.Price ?? 0m;
        var breakoutReference = kLines.TakeLast(20).DefaultIfEmpty().Max(item => item?.High ?? latestClose);
        var breakoutDistance = latestClose == 0m
            ? 0m
            : decimal.Round((breakoutReference - latestClose) / latestClose * 100m, 2);
        var trendState = ResolveTrendState(ma5, ma20, return5d, atrPercent);

        var vwap = CalculateVwap(minuteLines);
        var sessionPhase = ResolveSessionPhase(requestTime, minuteLines);

        var peRatio = quote?.PeRatio ?? 0m;
        var floatMarketCap = quote?.FloatMarketCap ?? 0m;
        var turnoverRate = quote?.TurnoverRate ?? 0m;
        var volumeRatio = quote?.VolumeRatio ?? 0m;
        var peBand = ResolvePeBand(peRatio);
        var marketCapBand = ResolveMarketCapBand(floatMarketCap);
        var highTurnoverRisk = turnoverRate >= 12m;
        var volumeSpikeRisk = volumeRatio >= 3m;
        var counterTrendRisk = positiveCount > 0 && negativeCount > 0;

        var degradedFlags = new List<string>();
        if (quote is null)
        {
            degradedFlags.Add("quote_unavailable");
        }
        if (filteredCount > 0)
        {
            degradedFlags.Add("market_noise_filtered");
        }
        if (newsPolicy.ExpandedWindow)
        {
            degradedFlags.Add("expanded_news_window");
        }
        if (highQualityCount == 0)
        {
            degradedFlags.Add("no_high_quality_evidence");
        }
        if (filteredLocalFacts.StockNews.Count == 0)
        {
            degradedFlags.Add("stock_news_missing");
        }
        if (kLines.Count < 20)
        {
            degradedFlags.Add("insufficient_kline_window");
        }
        if (minuteLines.Count == 0)
        {
            degradedFlags.Add("minute_line_missing");
        }

        var features = new StockAgentDeterministicFeaturesDto(
            new StockAgentEvidenceFeatureSummaryDto(
                allEvidence.Length,
                highQualityCount,
                recentEvidenceCount,
                weakEvidenceCount,
                decimal.Round(coverageScore, 2),
                conflictScore,
                newsPolicy.ExpandedWindow,
                freshnessHours,
                sentimentBreakdown),
            new StockAgentTrendFeatureSummaryDto(
                trendState,
                return5d,
                return20d,
                ma5,
                ma20,
                atrPercent,
                breakoutDistance,
                vwap,
                sessionPhase),
            new StockAgentValuationFeatureSummaryDto(
                peBand,
                peRatio > 0 ? peRatio : null,
                marketCapBand,
                floatMarketCap > 0 ? floatMarketCap : null,
                volumeRatio > 0 ? volumeRatio : null,
                quote?.ShareholderCount),
            new StockAgentRiskFeatureSummaryDto(
                decimal.Round(Math.Min(100m, atrPercent * 8m + turnoverRate * 2m), 2),
                highTurnoverRisk,
                volumeSpikeRisk,
                counterTrendRisk,
                BuildRiskFlags(highTurnoverRisk, volumeSpikeRisk, counterTrendRisk, trendState)),
            filteredCount,
            degradedFlags);

        return new StockAgentPreparedContextDto(filteredLocalFacts, features);
    }

    private static IReadOnlyList<string> BuildRiskFlags(bool highTurnoverRisk, bool volumeSpikeRisk, bool counterTrendRisk, string trendState)
    {
        var flags = new List<string>();
        if (highTurnoverRisk)
        {
            flags.Add("high_turnover");
        }
        if (volumeSpikeRisk)
        {
            flags.Add("volume_spike");
        }
        if (counterTrendRisk)
        {
            flags.Add("evidence_conflict");
        }
        if (string.Equals(trendState, "震荡", StringComparison.OrdinalIgnoreCase))
        {
            flags.Add("range_bound");
        }

        return flags;
    }

    private static bool IsChinaAShareSymbol(string symbol)
    {
        return symbol.StartsWith("sh", StringComparison.OrdinalIgnoreCase)
            || symbol.StartsWith("sz", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMarketNoise(StockAgentLocalNewsItemDto item)
    {
        var haystack = string.Join(' ', new[]
        {
            item.Title,
            item.TranslatedTitle,
            item.Source,
            item.SourceTag,
            item.Category,
            item.AiTarget,
            string.Join(' ', item.AiTags)
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

        return MarketNoiseKeywords.Any(keyword => haystack.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsHighQualityEvidence(string? readStatus)
    {
        return string.Equals(readStatus, "full_text_read", StringComparison.OrdinalIgnoreCase)
            || string.Equals(readStatus, "summary_only", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWeakEvidence(string? readStatus)
    {
        return string.Equals(readStatus, "metadata_only", StringComparison.OrdinalIgnoreCase)
            || string.Equals(readStatus, "unverified", StringComparison.OrdinalIgnoreCase)
            || string.Equals(readStatus, "fetch_failed", StringComparison.OrdinalIgnoreCase);
    }

    private static decimal CalculateAverage(IEnumerable<decimal> values)
    {
        var items = values.ToArray();
        return items.Length == 0 ? 0m : decimal.Round(items.Average(), 4);
    }

    private static decimal CalculateReturnPercent(IReadOnlyList<decimal> closes)
    {
        if (closes.Count < 2 || closes[0] == 0m)
        {
            return 0m;
        }

        return decimal.Round((closes[^1] - closes[0]) / closes[0] * 100m, 2);
    }

    private static decimal CalculateAtrPercent(IReadOnlyList<KLinePointDto> kLines)
    {
        if (kLines.Count < 2)
        {
            return 0m;
        }

        var window = kLines.TakeLast(Math.Min(14, kLines.Count)).ToArray();
        var ranges = new List<decimal>();
        decimal? previousClose = null;
        foreach (var item in window)
        {
            var trueRange = item.High - item.Low;
            if (previousClose.HasValue)
            {
                trueRange = new[]
                {
                    item.High - item.Low,
                    Math.Abs(item.High - previousClose.Value),
                    Math.Abs(item.Low - previousClose.Value)
                }.Max();
            }

            ranges.Add(trueRange);
            previousClose = item.Close;
        }

        var latestClose = window[^1].Close;
        if (latestClose == 0m)
        {
            return 0m;
        }

        return decimal.Round(ranges.Average() / latestClose * 100m, 2);
    }

    private static decimal CalculateVwap(IReadOnlyList<MinuteLinePointDto> minuteLines)
    {
        if (minuteLines.Count == 0)
        {
            return 0m;
        }

        decimal totalAmount = 0m;
        decimal totalVolume = 0m;
        foreach (var item in minuteLines)
        {
            if (item.Volume <= 0m)
            {
                continue;
            }

            totalAmount += item.Price * item.Volume;
            totalVolume += item.Volume;
        }

        return totalVolume <= 0m ? 0m : decimal.Round(totalAmount / totalVolume, 4);
    }

    private static string ResolveSessionPhase(DateTime requestTime, IReadOnlyList<MinuteLinePointDto> minuteLines)
    {
        if (minuteLines.Count == 0)
        {
            return "no_data";
        }

        var localTime = requestTime.TimeOfDay;
        if (localTime < new TimeSpan(9, 30, 0))
        {
            return "pre_market";
        }
        if (localTime <= new TimeSpan(11, 30, 0))
        {
            return "morning_session";
        }
        if (localTime < new TimeSpan(13, 0, 0))
        {
            return "midday_break";
        }
        if (localTime <= new TimeSpan(15, 0, 0))
        {
            return "afternoon_session";
        }

        return "post_market";
    }

    private static string ResolvePeBand(decimal peRatio)
    {
        if (peRatio <= 0m)
        {
            return "unknown";
        }
        if (peRatio < 15m)
        {
            return "cheap";
        }
        if (peRatio <= 35m)
        {
            return "fair";
        }

        return "expensive";
    }

    private static string ResolveMarketCapBand(decimal marketCap)
    {
        if (marketCap <= 0m)
        {
            return "unknown";
        }
        if (marketCap < 100_000_000_000m)
        {
            return "small_mid";
        }
        if (marketCap < 500_000_000_000m)
        {
            return "large";
        }

        return "mega";
    }

    private static string ResolveTrendState(decimal ma5, decimal ma20, decimal return5d, decimal atrPercent)
    {
        if (ma5 <= 0m || ma20 <= 0m)
        {
            return "未知";
        }

        if (ma5 > ma20 && return5d > 1m)
        {
            return "上涨";
        }

        if (ma5 < ma20 && return5d < -1m)
        {
            return "下跌";
        }

        return atrPercent >= 4m ? "震荡" : "盘整";
    }
}