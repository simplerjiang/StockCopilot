using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure.Storage;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface IStockAgentReplayCalibrationService
{
    Task<StockAgentReplayBaselineDto> BuildBaselineAsync(string? symbol, int take, CancellationToken cancellationToken = default);
}

public sealed class StockAgentReplayCalibrationService : IStockAgentReplayCalibrationService
{
    private static readonly Regex TraceRegex = new("traceId=([^\\s]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RepairRegex = new("parse_error agent=", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly string[] PollutedEvidenceKeywords =
    {
        "cointelegraph", "crypto", "bitcoin", "ethereum", "tesla", "apple", "nvidia"
    };

    private readonly AppDbContext _dbContext;
    private readonly AppRuntimePaths _runtimePaths;

    public StockAgentReplayCalibrationService(AppDbContext dbContext, AppRuntimePaths runtimePaths)
    {
        _dbContext = dbContext;
        _runtimePaths = runtimePaths;
    }

    public async Task<StockAgentReplayBaselineDto> BuildBaselineAsync(string? symbol, int take, CancellationToken cancellationToken = default)
    {
        var normalizedSymbol = string.IsNullOrWhiteSpace(symbol) ? null : StockSymbolNormalizer.Normalize(symbol);
        var safeTake = Math.Clamp(take, 10, 200);

        var query = _dbContext.StockAgentAnalysisHistories
            .AsNoTracking()
            .OrderByDescending(item => item.CreatedAt)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(normalizedSymbol))
        {
            query = query.Where(item => item.Symbol == normalizedSymbol);
        }

        var histories = await query.Take(safeTake).ToListAsync(cancellationToken);
        var symbolSet = histories.Select(item => item.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var klineMap = await LoadKlineMapAsync(symbolSet, cancellationToken);
        var repairRate = await CalculateRepairRateAsync(cancellationToken);

        var samples = histories
            .Select(item => BuildSample(item, klineMap.TryGetValue(item.Symbol, out var bars) ? bars : Array.Empty<KLinePointEntity>()))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToArray();

        var horizons = new[]
        {
            BuildHorizonMetric(1, samples),
            BuildHorizonMetric(3, samples),
            BuildHorizonMetric(5, samples),
            BuildHorizonMetric(10, samples)
        };

        var traceableRate = samples.Length == 0 ? 0m : decimal.Round(samples.Count(item => item.EvidenceTraceable) * 100m / samples.Length, 2);
        var pollutedRate = samples.Length == 0 ? 0m : decimal.Round(samples.Count(item => item.EvidencePolluted) * 100m / samples.Length, 2);
        var revisionRate = samples.Length == 0 ? 0m : decimal.Round(samples.Count(item => item.RevisionExplained) * 100m / samples.Length, 2);

        return new StockAgentReplayBaselineDto(
            string.IsNullOrWhiteSpace(normalizedSymbol) ? "all" : normalizedSymbol,
            DateTime.UtcNow,
            samples.Length,
            traceableRate,
            repairRate,
            pollutedRate,
            revisionRate,
            horizons,
            samples.Take(30).ToArray());
    }

    private async Task<Dictionary<string, KLinePointEntity[]>> LoadKlineMapAsync(IReadOnlyList<string> symbols, CancellationToken cancellationToken)
    {
        if (symbols.Count == 0)
        {
            return new Dictionary<string, KLinePointEntity[]>(StringComparer.OrdinalIgnoreCase);
        }

        var rows = await _dbContext.KLinePoints
            .AsNoTracking()
            .Where(item => item.Interval == "day" && symbols.Contains(item.Symbol))
            .OrderBy(item => item.Date)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(item => item.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    private async Task<decimal> CalculateRepairRateAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_runtimePaths.LogsPath, "llm-requests.txt");
        if (!File.Exists(path))
        {
            return 0m;
        }

        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        if (lines.Length == 0)
        {
            return 0m;
        }

        var requestCount = lines.Count(line => line.Contains("[LLM-AUDIT]", StringComparison.OrdinalIgnoreCase) && line.Contains("stage=request", StringComparison.OrdinalIgnoreCase));
        if (requestCount == 0)
        {
            return 0m;
        }

        var repairCount = lines.Count(line => RepairRegex.IsMatch(line));
        return decimal.Round(repairCount * 100m / requestCount, 2);
    }

    private static StockAgentReplaySampleDto? BuildSample(Data.Entities.StockAgentAnalysisHistory entry, IReadOnlyList<KLinePointEntity> dailyBars)
    {
        if (string.IsNullOrWhiteSpace(entry.ResultJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(entry.ResultJson);
            if (!TryGetCommanderResult(doc.RootElement, out var commander))
            {
                return null;
            }

            var direction = ReadString(commander, "directional_bias")
                ?? ExtractDirection(ReadString(commander, "analysis_opinion"), ReadString(commander, "summary"));
            var confidence = ReadDecimal(commander, "confidence_score") ?? 50m;
            var (bull, @base, bear) = ReadProbabilities(commander, direction, confidence);
            var summary = ReadString(commander, "summary") ?? ReadString(commander, "analysis_opinion");
            var traceId = ExtractTraceId(doc.RootElement);
            var evidence = ReadEvidence(commander);
            var evidenceTraceable = evidence.Any(item => !string.IsNullOrWhiteSpace(item.Source) && item.PublishedAt.HasValue && (!string.IsNullOrWhiteSpace(item.Url) || item.LocalFactId.HasValue || !string.IsNullOrWhiteSpace(item.SourceRecordId)));
            var evidencePolluted = evidence.Any(item => PollutedEvidenceKeywords.Any(keyword => string.Join(' ', item.Source, item.Title, item.Url, item.Point).Contains(keyword, StringComparison.OrdinalIgnoreCase)));
            var revisionExplained = !ReadBool(commander, "revision", "required") || !string.IsNullOrWhiteSpace(ReadString(commander, "revision", "reason"));

            var returns = CalculateFutureReturns(entry.CreatedAt, dailyBars);

            return new StockAgentReplaySampleDto(
                entry.Id,
                entry.Symbol,
                entry.CreatedAt,
                direction,
                confidence,
                bull,
                @base,
                bear,
                returns.Return1d,
                returns.Return3d,
                returns.Return5d,
                returns.Return10d,
                evidenceTraceable,
                evidencePolluted,
                revisionExplained,
                summary,
                traceId);
        }
        catch
        {
            return null;
        }
    }

    private static StockAgentReplayHorizonMetricDto BuildHorizonMetric(int horizonDays, IReadOnlyList<StockAgentReplaySampleDto> samples)
    {
        var realized = samples
            .Select(item => new { Sample = item, Return = GetReturn(item, horizonDays) })
            .Where(item => item.Return.HasValue)
            .ToArray();
        if (realized.Length == 0)
        {
            return new StockAgentReplayHorizonMetricDto(horizonDays, 0, 0m, 0m, 0m, 0m, 0m, 0m);
        }

        var hitCount = 0;
        decimal totalReturn = 0m;
        decimal totalBrier = 0m;
        var bullTotal = 0;
        var bearTotal = 0;
        var baseTotal = 0;
        var bullWins = 0;
        var bearWins = 0;
        var baseWins = 0;

        foreach (var item in realized)
        {
            var outcome = ClassifyOutcome(item.Return!.Value);
            var prediction = ClassifyPrediction(item.Sample.Direction);
            if (prediction == outcome)
            {
                hitCount++;
            }

            totalReturn += item.Return.Value;
            totalBrier += CalculateBrier(item.Sample, outcome);

            switch (prediction)
            {
                case "bull":
                    bullTotal++;
                    if (outcome == "bull")
                    {
                        bullWins++;
                    }
                    break;
                case "bear":
                    bearTotal++;
                    if (outcome == "bear")
                    {
                        bearWins++;
                    }
                    break;
                default:
                    baseTotal++;
                    if (outcome == "base")
                    {
                        baseWins++;
                    }
                    break;
            }
        }

        return new StockAgentReplayHorizonMetricDto(
            horizonDays,
            realized.Length,
            decimal.Round(hitCount * 100m / realized.Length, 2),
            decimal.Round(totalReturn / realized.Length, 2),
            decimal.Round(totalBrier / realized.Length, 4),
            bullTotal == 0 ? 0m : decimal.Round(bullWins * 100m / bullTotal, 2),
            bearTotal == 0 ? 0m : decimal.Round(bearWins * 100m / bearTotal, 2),
            baseTotal == 0 ? 0m : decimal.Round(baseWins * 100m / baseTotal, 2));
    }

    private static decimal? GetReturn(StockAgentReplaySampleDto sample, int horizonDays)
    {
        return horizonDays switch
        {
            1 => sample.Return1dPercent,
            3 => sample.Return3dPercent,
            5 => sample.Return5dPercent,
            10 => sample.Return10dPercent,
            _ => null
        };
    }

    private static decimal CalculateBrier(StockAgentReplaySampleDto sample, string outcome)
    {
        var bullTarget = outcome == "bull" ? 1m : 0m;
        var baseTarget = outcome == "base" ? 1m : 0m;
        var bearTarget = outcome == "bear" ? 1m : 0m;
        var bullProb = sample.BullProbability / 100m;
        var baseProb = sample.BaseProbability / 100m;
        var bearProb = sample.BearProbability / 100m;

        return ((bullProb - bullTarget) * (bullProb - bullTarget)
            + (baseProb - baseTarget) * (baseProb - baseTarget)
            + (bearProb - bearTarget) * (bearProb - bearTarget)) / 3m;
    }

    private static string ClassifyPrediction(string direction)
    {
        return direction switch
        {
            "看多" or "加仓" or "试仓" or "bull" => "bull",
            "看空" or "减仓" or "清仓" or "bear" => "bear",
            _ => "base"
        };
    }

    private static string ClassifyOutcome(decimal returnPercent)
    {
        if (returnPercent >= 1m)
        {
            return "bull";
        }
        if (returnPercent <= -1m)
        {
            return "bear";
        }

        return "base";
    }

    private static (decimal? Return1d, decimal? Return3d, decimal? Return5d, decimal? Return10d) CalculateFutureReturns(DateTime createdAt, IReadOnlyList<KLinePointEntity> dailyBars)
    {
        if (dailyBars.Count == 0)
        {
            return (null, null, null, null);
        }

        var ordered = dailyBars.OrderBy(item => item.Date).ToArray();
        var entryBar = ordered.LastOrDefault(item => item.Date <= createdAt.Date) ?? ordered.FirstOrDefault(item => item.Date >= createdAt.Date);
        if (entryBar is null || entryBar.Close == 0m)
        {
            return (null, null, null, null);
        }

        return (
            CalculateReturn(entryBar, ordered, 1),
            CalculateReturn(entryBar, ordered, 3),
            CalculateReturn(entryBar, ordered, 5),
            CalculateReturn(entryBar, ordered, 10));
    }

    private static decimal? CalculateReturn(KLinePointEntity entryBar, IReadOnlyList<KLinePointEntity> ordered, int horizonDays)
    {
        var targetDate = entryBar.Date.Date.AddDays(horizonDays);
        var futureBar = ordered.FirstOrDefault(item => item.Date.Date >= targetDate);
        if (futureBar is null)
        {
            return null;
        }

        return decimal.Round((futureBar.Close - entryBar.Close) / entryBar.Close * 100m, 2);
    }

    private static bool TryGetCommanderResult(JsonElement root, out JsonElement commander)
    {
        commander = default;
        if (TryGetPropertyIgnoreCase(root, "agents", out var agents) && agents.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in agents.EnumerateArray())
            {
                var agentId = ReadString(item, "agentId");
                if (!string.Equals(agentId, "commander", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TryGetPropertyIgnoreCase(item, "data", out var data) && data.ValueKind == JsonValueKind.Object)
                {
                    commander = data;
                    return true;
                }
            }
        }

        if (ReadString(root, "agent") == "commander")
        {
            commander = root;
            return true;
        }

        return false;
    }

    private static (decimal Bull, decimal Base, decimal Bear) ReadProbabilities(JsonElement commander, string direction, decimal confidence)
    {
        if (TryGetPropertyIgnoreCase(commander, "probabilities", out var probabilities) && probabilities.ValueKind == JsonValueKind.Object)
        {
            var bull = ReadDecimal(probabilities, "bull") ?? 0m;
            var @base = ReadDecimal(probabilities, "base") ?? 0m;
            var bear = ReadDecimal(probabilities, "bear") ?? 0m;
            var sum = bull + @base + bear;
            if (sum > 0m)
            {
                return (decimal.Round(bull * 100m / sum, 2), decimal.Round(@base * 100m / sum, 2), decimal.Round(bear * 100m / sum, 2));
            }
        }

        var capped = Math.Clamp(confidence, 0m, 100m);
        return direction switch
        {
            "看多" or "加仓" or "试仓" => (decimal.Round(Math.Min(85m, 35m + capped * 0.5m), 2), decimal.Round(Math.Max(10m, 100m - Math.Min(85m, 35m + capped * 0.5m) - 15m), 2), 15m),
            "看空" or "减仓" or "清仓" => (15m, decimal.Round(Math.Max(10m, 100m - Math.Min(85m, 35m + capped * 0.5m) - 15m), 2), decimal.Round(Math.Min(85m, 35m + capped * 0.5m), 2)),
            _ => (decimal.Round(Math.Max(15m, 30m - capped * 0.1m), 2), decimal.Round(Math.Min(70m, 40m + capped * 0.2m), 2), decimal.Round(Math.Max(15m, 30m - capped * 0.1m), 2))
        };
    }

    private static IReadOnlyList<(string Point, string? Title, string Source, DateTime? PublishedAt, string? Url, long? LocalFactId, string? SourceRecordId)> ReadEvidence(JsonElement commander)
    {
        if (!TryGetPropertyIgnoreCase(commander, "evidence", out var evidence) || evidence.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<(string, string?, string, DateTime?, string?, long?, string?)>();
        }

        return evidence.EnumerateArray()
            .Select((JsonElement item) => (
                ReadString(item, "point") ?? string.Empty,
                ReadString(item, "title"),
                ReadString(item, "source") ?? string.Empty,
                DateTime.TryParse(ReadString(item, "publishedAt"), out var publishedAt) ? publishedAt : (DateTime?)null,
                ReadString(item, "url"),
                ReadLong(item, "localFactId"),
                ReadString(item, "sourceRecordId")))
            .ToArray();
    }

    private static string? ExtractTraceId(JsonElement root)
    {
        if (TryGetPropertyIgnoreCase(root, "agents", out var agents) && agents.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in agents.EnumerateArray())
            {
                var traceId = ReadString(item, "traceId");
                if (!string.IsNullOrWhiteSpace(traceId))
                {
                    return traceId;
                }
            }
        }

        var raw = root.GetRawText();
        var match = TraceRegex.Match(raw);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string ExtractDirection(string? analysisOpinion, string? summary)
    {
        var text = string.Join(' ', new[] { analysisOpinion, summary }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (text.Contains("减仓", StringComparison.OrdinalIgnoreCase) || text.Contains("清仓", StringComparison.OrdinalIgnoreCase) || text.Contains("看空", StringComparison.OrdinalIgnoreCase))
        {
            return "减仓";
        }
        if (text.Contains("加仓", StringComparison.OrdinalIgnoreCase) || text.Contains("试仓", StringComparison.OrdinalIgnoreCase) || text.Contains("看多", StringComparison.OrdinalIgnoreCase))
        {
            return "加仓";
        }

        return "观察";
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement root, string propertyName, out JsonElement value)
    {
        value = default;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        return TryGetPropertyIgnoreCase(root, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string? ReadString(JsonElement root, string objectName, string propertyName)
    {
        return TryGetPropertyIgnoreCase(root, objectName, out var obj) && obj.ValueKind == JsonValueKind.Object
            ? ReadString(obj, propertyName)
            : null;
    }

    private static bool ReadBool(JsonElement root, string objectName, string propertyName)
    {
        return TryGetPropertyIgnoreCase(root, objectName, out var obj)
            && obj.ValueKind == JsonValueKind.Object
            && TryGetPropertyIgnoreCase(obj, propertyName, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            && value.GetBoolean();
    }

    private static decimal? ReadDecimal(JsonElement root, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(root, propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }
        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), out number))
        {
            return number;
        }

        return null;
    }

    private static long? ReadLong(JsonElement root, string propertyName)
    {
        return TryGetPropertyIgnoreCase(root, propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : null;
    }
}