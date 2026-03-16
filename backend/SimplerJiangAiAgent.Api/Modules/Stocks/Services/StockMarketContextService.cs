using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Modules.Market.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface IStockMarketContextService
{
    Task<StockMarketContextDto?> GetLatestAsync(string symbol, CancellationToken cancellationToken = default);
}

public sealed class StockMarketContextService : IStockMarketContextService
{
    private readonly AppDbContext _dbContext;

    public StockMarketContextService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<StockMarketContextDto?> GetLatestAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var normalizedSymbol = StockSymbolNormalizer.Normalize(symbol);
        if (string.IsNullOrWhiteSpace(normalizedSymbol))
        {
            return null;
        }

        var latestSentiment = await _dbContext.MarketSentimentSnapshots
            .AsNoTracking()
            .OrderByDescending(item => item.SnapshotTime)
            .FirstOrDefaultAsync(cancellationToken);
        if (latestSentiment is null)
        {
            return null;
        }

        var stockSectorName = await _dbContext.StockQuoteSnapshots
            .AsNoTracking()
            .Where(item => item.Symbol == normalizedSymbol && item.SectorName != null && item.SectorName != string.Empty)
            .OrderByDescending(item => item.Timestamp)
            .Select(item => item.SectorName)
            .FirstOrDefaultAsync(cancellationToken)
            ?? await _dbContext.StockCompanyProfiles
                .AsNoTracking()
                .Where(item => item.Symbol == normalizedSymbol && item.SectorName != null && item.SectorName != string.Empty)
                .OrderByDescending(item => item.UpdatedAt)
                .Select(item => item.SectorName)
                .FirstOrDefaultAsync(cancellationToken);

        var latestSectorSnapshotTime = await _dbContext.SectorRotationSnapshots
            .AsNoTracking()
            .MaxAsync(item => (DateTime?)item.SnapshotTime, cancellationToken);

        if (latestSectorSnapshotTime is null)
        {
            return BuildNeutral(latestSentiment, stockSectorName);
        }

        var mainline = await _dbContext.SectorRotationSnapshots
            .AsNoTracking()
            .Where(item => item.SnapshotTime == latestSectorSnapshotTime.Value)
            .OrderByDescending(item => item.IsMainline)
            .ThenByDescending(item => item.MainlineScore)
            .ThenBy(item => item.RankNo)
            .FirstOrDefaultAsync(cancellationToken);

        var matchedSector = string.IsNullOrWhiteSpace(stockSectorName)
            ? null
            : await _dbContext.SectorRotationSnapshots
                .AsNoTracking()
                .Where(item => item.SnapshotTime == latestSectorSnapshotTime.Value)
                .OrderByDescending(item => item.IsMainline)
                .ThenByDescending(item => item.MainlineScore)
                .ThenBy(item => item.RankNo)
                .FirstOrDefaultAsync(item =>
                    item.SectorName == stockSectorName
                    || item.SectorName.Contains(stockSectorName)
                    || stockSectorName.Contains(item.SectorName), cancellationToken);

        var effectiveStage = string.IsNullOrWhiteSpace(latestSentiment.StageLabelV2)
            ? latestSentiment.StageLabel
            : latestSentiment.StageLabelV2;
        var stageConfidence = latestSentiment.StageConfidence > 0 ? latestSentiment.StageConfidence : latestSentiment.StageScore;
        var isMainlineAligned = mainline is not null
            && matchedSector is not null
            && (matchedSector.Id == mainline.Id
                || string.Equals(matchedSector.SectorCode, mainline.SectorCode, StringComparison.OrdinalIgnoreCase)
                || string.Equals(matchedSector.SectorName, mainline.SectorName, StringComparison.OrdinalIgnoreCase));
        var suggestedScale = ResolveSuggestedPositionScale(effectiveStage, stageConfidence, isMainlineAligned);
        var executionFrequency = ResolveExecutionFrequency(effectiveStage, stageConfidence, isMainlineAligned);
        var counterTrendWarning = effectiveStage == "退潮"
            || (mainline is not null && matchedSector is not null && !isMainlineAligned && (effectiveStage == "主升" || effectiveStage == "分歧"));

        return new StockMarketContextDto(
            effectiveStage,
            decimal.Round(stageConfidence, 2),
            stockSectorName,
            mainline?.SectorName,
            matchedSector?.SectorCode,
            decimal.Round(mainline?.MainlineScore ?? matchedSector?.MainlineScore ?? 0m, 2),
            decimal.Round(suggestedScale, 4),
            executionFrequency,
            counterTrendWarning,
            isMainlineAligned);
    }

    internal static decimal ResolveSuggestedPositionScale(string stageLabel, decimal stageConfidence, bool isMainlineAligned)
    {
        var baseScale = stageLabel switch
        {
            "主升" => 0.85m,
            "分歧" => 0.60m,
            "退潮" => 0.20m,
            _ => 0.35m
        };

        var confidenceBias = (Math.Clamp(stageConfidence, 0m, 100m) - 50m) / 200m;
        var alignmentBias = isMainlineAligned ? 0.08m : -0.08m;
        return Math.Clamp(baseScale + confidenceBias + alignmentBias, 0.05m, 1m);
    }

    private static string ResolveExecutionFrequency(string stageLabel, decimal stageConfidence, bool isMainlineAligned)
    {
        if (stageLabel == "主升" && stageConfidence >= 68m && isMainlineAligned)
        {
            return "积极执行";
        }

        if (stageLabel == "分歧")
        {
            return isMainlineAligned ? "精选执行" : "降低频率";
        }

        if (stageLabel == "退潮")
        {
            return "防守等待";
        }

        return "低频试错";
    }

    private static StockMarketContextDto BuildNeutral(Data.Entities.MarketSentimentSnapshot latestSentiment, string? stockSectorName)
    {
        var effectiveStage = string.IsNullOrWhiteSpace(latestSentiment.StageLabelV2)
            ? latestSentiment.StageLabel
            : latestSentiment.StageLabelV2;

        return new StockMarketContextDto(
            effectiveStage,
            decimal.Round(latestSentiment.StageConfidence > 0 ? latestSentiment.StageConfidence : latestSentiment.StageScore, 2),
            stockSectorName,
            null,
            null,
            0m,
            decimal.Round(ResolveSuggestedPositionScale(effectiveStage, latestSentiment.StageConfidence, false), 4),
            ResolveExecutionFrequency(effectiveStage, latestSentiment.StageConfidence, false),
            effectiveStage == "退潮",
            false);
    }
}