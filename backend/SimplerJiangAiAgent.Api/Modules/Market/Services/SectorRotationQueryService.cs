using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Modules.Market.Models;

namespace SimplerJiangAiAgent.Api.Modules.Market.Services;

public sealed class SectorRotationQueryService : ISectorRotationQueryService
{
    private readonly AppDbContext _dbContext;
    private readonly SectorRotationOptions _options;

    public SectorRotationQueryService(AppDbContext dbContext, IOptions<SectorRotationOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
    }

    public async Task<MarketSentimentSummaryDto?> GetLatestSummaryAsync(CancellationToken cancellationToken = default)
    {
        var latest = await _dbContext.MarketSentimentSnapshots
            .AsNoTracking()
            .OrderByDescending(x => x.SnapshotTime)
            .FirstOrDefaultAsync(cancellationToken);

        return latest is null ? null : MapSummary(latest);
    }

    public async Task<IReadOnlyList<MarketSentimentHistoryPointDto>> GetHistoryAsync(int days, CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.MarketSentimentSnapshots
            .AsNoTracking()
            .OrderByDescending(x => x.SnapshotTime)
            .Take(Math.Max(20, days * 6))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => x.TradingDate.Date)
            .Select(group => group.OrderByDescending(x => x.SnapshotTime).First())
            .OrderByDescending(x => x.TradingDate)
            .Take(Math.Clamp(days, 1, 60))
            .OrderBy(x => x.TradingDate)
            .Select(item => new MarketSentimentHistoryPointDto(
                item.TradingDate,
                item.SnapshotTime,
                item.StageLabel,
                item.StageScore,
                item.LimitUpCount,
                item.LimitDownCount,
                item.BrokenBoardCount))
            .ToArray();
    }

    public async Task<SectorRotationPageDto> GetSectorPageAsync(string boardType, int page, int pageSize, string sort, CancellationToken cancellationToken = default)
    {
        var normalizedBoardType = NormalizeBoardType(boardType);
        var latestSnapshotTime = await _dbContext.SectorRotationSnapshots
            .AsNoTracking()
            .Where(x => x.BoardType == normalizedBoardType)
            .MaxAsync(x => (DateTime?)x.SnapshotTime, cancellationToken);

        if (latestSnapshotTime is null)
        {
            return new SectorRotationPageDto(normalizedBoardType, 1, pageSize, 0, NormalizeSort(sort), null, Array.Empty<SectorRotationListItemDto>());
        }

        var query = _dbContext.SectorRotationSnapshots
            .AsNoTracking()
            .Where(x => x.BoardType == normalizedBoardType && x.SnapshotTime == latestSnapshotTime.Value);

        query = ApplySort(query, sort);
        var total = await query.CountAsync(cancellationToken);
        var safePageSize = Math.Clamp(pageSize, 1, 50);
        var safePage = Math.Max(1, page);
        var items = await query
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        return new SectorRotationPageDto(
            normalizedBoardType,
            safePage,
            safePageSize,
            total,
            NormalizeSort(sort),
            latestSnapshotTime,
            items.Select(MapSectorItem).ToArray());
    }

    public async Task<SectorRotationDetailDto?> GetSectorDetailAsync(string sectorCode, string boardType, string window, CancellationToken cancellationToken = default)
    {
        var normalizedBoardType = NormalizeBoardType(boardType);
        var latest = await _dbContext.SectorRotationSnapshots
            .AsNoTracking()
            .Where(x => x.SectorCode == sectorCode && x.BoardType == normalizedBoardType)
            .OrderByDescending(x => x.SnapshotTime)
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is null)
        {
            return null;
        }

        var historyWindow = ParseWindow(window);
        var historyRows = await _dbContext.SectorRotationSnapshots
            .AsNoTracking()
            .Where(x => x.SectorCode == sectorCode && x.BoardType == normalizedBoardType)
            .OrderByDescending(x => x.SnapshotTime)
            .Take(Math.Max(12, historyWindow * 4))
            .ToListAsync(cancellationToken);

        var history = MapHistoryPoints(historyRows, historyWindow);

        var leaders = await _dbContext.SectorRotationLeaderSnapshots
            .AsNoTracking()
            .Where(x => x.SectorRotationSnapshotId == latest.Id)
            .OrderBy(x => x.RankInSector)
            .Take(Math.Max(1, _options.LeaderTake))
            .Select(x => new SectorRotationLeaderDto(x.RankInSector, x.Symbol, x.Name, x.ChangePercent, x.TurnoverAmount, x.IsLimitUp, x.IsBrokenBoard))
            .ToListAsync(cancellationToken);

        var news = await _dbContext.LocalSectorReports
            .AsNoTracking()
            .Where(x => x.Level == "sector" && (x.SectorName == latest.SectorName || (x.SectorName != null && x.SectorName.Contains(latest.SectorName))))
            .OrderByDescending(x => x.PublishTime)
            .Take(Math.Max(1, _options.DetailNewsTake))
            .Select(x => new SectorRotationNewsDto(x.Title, x.TranslatedTitle, x.Source, x.AiSentiment, x.PublishTime, x.Url))
            .ToListAsync(cancellationToken);

        return new SectorRotationDetailDto(MapSectorItem(latest), history, leaders, news);
    }

    public async Task<SectorRotationTrendDto?> GetSectorTrendAsync(string sectorCode, string boardType, string window, CancellationToken cancellationToken = default)
    {
        var normalizedBoardType = NormalizeBoardType(boardType);
        var historyWindow = ParseWindow(window);
        var historyRows = await _dbContext.SectorRotationSnapshots
            .AsNoTracking()
            .Where(x => x.SectorCode == sectorCode && x.BoardType == normalizedBoardType)
            .OrderByDescending(x => x.SnapshotTime)
            .Take(Math.Max(24, historyWindow * 4))
            .ToListAsync(cancellationToken);

        if (historyRows.Count == 0)
        {
            return null;
        }

        return new SectorRotationTrendDto(normalizedBoardType, sectorCode, NormalizeWindow(window), MapHistoryPoints(historyRows, historyWindow));
    }

    public async Task<IReadOnlyList<SectorRotationLeaderDto>> GetLeadersAsync(string sectorCode, string boardType, int take, CancellationToken cancellationToken = default)
    {
        var normalizedBoardType = NormalizeBoardType(boardType);
        var latestSnapshotId = await _dbContext.SectorRotationSnapshots
            .AsNoTracking()
            .Where(x => x.SectorCode == sectorCode && x.BoardType == normalizedBoardType)
            .OrderByDescending(x => x.SnapshotTime)
            .Select(x => (long?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestSnapshotId is null)
        {
            return Array.Empty<SectorRotationLeaderDto>();
        }

        return await _dbContext.SectorRotationLeaderSnapshots
            .AsNoTracking()
            .Where(x => x.SectorRotationSnapshotId == latestSnapshotId.Value)
            .OrderBy(x => x.RankInSector)
            .Take(Math.Clamp(take, 1, 20))
            .Select(x => new SectorRotationLeaderDto(x.RankInSector, x.Symbol, x.Name, x.ChangePercent, x.TurnoverAmount, x.IsLimitUp, x.IsBrokenBoard))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SectorRotationListItemDto>> GetMainlineAsync(string boardType, string window, int take, CancellationToken cancellationToken = default)
    {
        var normalizedBoardType = NormalizeBoardType(boardType);
        var latestSnapshotTime = await _dbContext.SectorRotationSnapshots
            .AsNoTracking()
            .Where(x => x.BoardType == normalizedBoardType)
            .MaxAsync(x => (DateTime?)x.SnapshotTime, cancellationToken);

        if (latestSnapshotTime is null)
        {
            return Array.Empty<SectorRotationListItemDto>();
        }

        var trendWindow = NormalizeWindow(window);
        var query = _dbContext.SectorRotationSnapshots
            .AsNoTracking()
            .Where(x => x.BoardType == normalizedBoardType && x.SnapshotTime == latestSnapshotTime.Value);

        query = trendWindow switch
        {
            "5d" => query.OrderByDescending(x => x.IsMainline).ThenByDescending(x => x.StrengthAvg5d).ThenByDescending(x => x.MainlineScore),
            "20d" => query.OrderByDescending(x => x.IsMainline).ThenByDescending(x => x.StrengthAvg20d).ThenByDescending(x => x.MainlineScore),
            _ => query.OrderByDescending(x => x.IsMainline).ThenByDescending(x => x.StrengthAvg10d).ThenByDescending(x => x.MainlineScore)
        };

        return await query
            .Take(Math.Clamp(take, 1, 20))
            .Select(x => MapSectorItem(x))
            .ToListAsync(cancellationToken);
    }

    private static MarketSentimentSummaryDto MapSummary(Data.Entities.MarketSentimentSnapshot item)
    {
        return new MarketSentimentSummaryDto(
            item.SnapshotTime,
            item.SessionPhase,
            item.StageLabel,
            item.StageScore,
            item.MaxLimitUpStreak,
            item.LimitUpCount,
            item.LimitDownCount,
            item.BrokenBoardCount,
            item.BrokenBoardRate,
            item.Advancers,
            item.Decliners,
            item.FlatCount,
            item.TotalTurnover,
            item.Top3SectorTurnoverShare,
            item.Top10SectorTurnoverShare,
            item.DiffusionScore,
            item.ContinuationScore,
            string.IsNullOrWhiteSpace(item.StageLabelV2) ? item.StageLabel : item.StageLabelV2,
            item.StageConfidence,
            item.Top3SectorTurnoverShare5dAvg,
            item.Top10SectorTurnoverShare5dAvg,
            item.LimitUpCount5dAvg,
            item.BrokenBoardRate5dAvg);
    }

    private static SectorRotationListItemDto MapSectorItem(Data.Entities.SectorRotationSnapshot item)
    {
        return new SectorRotationListItemDto(
            item.BoardType,
            item.SectorCode,
            item.SectorName,
            item.ChangePercent,
            item.MainNetInflow,
            item.BreadthScore,
            item.ContinuityScore,
            item.StrengthScore,
            item.NewsSentiment,
            item.NewsHotCount,
            item.LeaderSymbol,
            item.LeaderName,
            item.LeaderChangePercent,
            item.RankNo,
            item.SnapshotTime,
            item.RankChange5d,
            item.RankChange10d,
            item.RankChange20d,
            item.StrengthAvg5d,
            item.StrengthAvg10d,
            item.StrengthAvg20d,
            item.DiffusionRate,
            item.AdvancerCount,
            item.DeclinerCount,
            item.FlatMemberCount,
            item.LimitUpMemberCount,
            item.LeaderStabilityScore,
            item.MainlineScore,
            item.IsMainline);
    }

    private static SectorRotationHistoryPointDto[] MapHistoryPoints(IReadOnlyList<Data.Entities.SectorRotationSnapshot> historyRows, int historyWindow)
    {
        return historyRows
            .GroupBy(x => x.TradingDate.Date)
            .Select(group => group.OrderByDescending(x => x.SnapshotTime).First())
            .OrderByDescending(x => x.TradingDate)
            .Take(historyWindow)
            .OrderBy(x => x.TradingDate)
            .Select(item => new SectorRotationHistoryPointDto(
                item.TradingDate,
                item.SnapshotTime,
                item.ChangePercent,
                item.BreadthScore,
                item.ContinuityScore,
                item.StrengthScore,
                item.RankNo,
                item.DiffusionRate,
                item.AdvancerCount,
                item.DeclinerCount,
                item.FlatMemberCount,
                item.LimitUpMemberCount,
                item.RankChange5d,
                item.RankChange10d,
                item.RankChange20d,
                item.StrengthAvg5d,
                item.StrengthAvg10d,
                item.StrengthAvg20d,
                item.LeaderStabilityScore,
                item.MainlineScore,
                item.IsMainline))
            .ToArray();
    }

    private static IQueryable<Data.Entities.SectorRotationSnapshot> ApplySort(IQueryable<Data.Entities.SectorRotationSnapshot> query, string sort)
    {
        return NormalizeSort(sort) switch
        {
            "change" => query.OrderByDescending(x => x.ChangePercent).ThenBy(x => x.RankNo),
            "flow" => query.OrderByDescending(x => x.MainNetInflow).ThenBy(x => x.RankNo),
            "breadth" => query.OrderByDescending(x => x.BreadthScore).ThenBy(x => x.RankNo),
            "continuity" => query.OrderByDescending(x => x.ContinuityScore).ThenBy(x => x.RankNo),
            "mainline" => query.OrderByDescending(x => x.IsMainline).ThenByDescending(x => x.MainlineScore).ThenBy(x => x.RankNo),
            _ => query.OrderByDescending(x => x.StrengthScore).ThenBy(x => x.RankNo)
        };
    }

    private static string NormalizeBoardType(string boardType)
    {
        return SectorBoardTypes.All.Contains(boardType, StringComparer.OrdinalIgnoreCase)
            ? boardType.ToLowerInvariant()
            : SectorBoardTypes.Concept;
    }

    private static string NormalizeSort(string sort)
    {
        return sort?.Trim().ToLowerInvariant() switch
        {
            "change" => "change",
            "changepercent" => "change",
            "mainnetinflow" => "flow",
            "flow" => "flow",
            "breadth" => "breadth",
            "continuity" => "continuity",
            "mainline" => "mainline",
            _ => "strength"
        };
    }

    private static string NormalizeWindow(string window)
    {
        return window?.Trim().ToLowerInvariant() switch
        {
            "5d" => "5d",
            "20d" => "20d",
            _ => "10d"
        };
    }

    private static int ParseWindow(string window)
    {
        return window?.Trim().ToLowerInvariant() switch
        {
            "5d" => 5,
            "20d" => 20,
            _ => 10
        };
    }
}
