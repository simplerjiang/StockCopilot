using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

internal static class StockDetailCacheQueries
{
    private const int DefaultMinutePointLimit = 241;

    public static async Task<IReadOnlyList<KLinePointDto>> GetRecentKLinesAsync(
        AppDbContext dbContext,
        string symbol,
        string interval,
        int take,
        CancellationToken cancellationToken = default)
    {
        var points = await dbContext.KLinePoints
            .AsNoTracking()
            .Where(x => x.Symbol == symbol && x.Interval == interval)
            .Where(StockKLinePointFilters.HasUsableHighLowEntity)
            .OrderByDescending(x => x.Date)
            .Take(take)
            .Select(x => new KLinePointDto(x.Date, x.Open, x.Close, x.High, x.Low, x.Volume))
            .ToListAsync(cancellationToken);

        return points
            .OrderBy(x => x.Date)
            .ToArray();
    }

    public static async Task<IReadOnlyList<MinuteLinePointDto>> GetLatestMinuteLinesAsync(
        AppDbContext dbContext,
        string symbol,
        int take = DefaultMinutePointLimit,
        CancellationToken cancellationToken = default)
    {
        var latestDate = await dbContext.MinuteLinePoints
            .AsNoTracking()
            .Where(x => x.Symbol == symbol)
            .OrderByDescending(x => x.Date)
            .Select(x => (DateOnly?)x.Date)
            .FirstOrDefaultAsync(cancellationToken);

        if (!latestDate.HasValue)
        {
            return Array.Empty<MinuteLinePointDto>();
        }

        var latestDayPoints = await dbContext.MinuteLinePoints
            .AsNoTracking()
            .Where(x => x.Symbol == symbol && x.Date == latestDate.Value)
            .Select(x => new MinuteLinePointDto(x.Date, x.Time, x.Price, x.AveragePrice, x.Volume))
            .ToListAsync(cancellationToken);

        return latestDayPoints
            .OrderBy(x => x.Time)
            .Take(take)
            .ToArray();
    }
}