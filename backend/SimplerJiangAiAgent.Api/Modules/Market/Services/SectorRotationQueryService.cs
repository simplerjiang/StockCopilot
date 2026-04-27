using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Modules.Market.Models;

namespace SimplerJiangAiAgent.Api.Modules.Market.Services;

public sealed class SectorRotationQueryService : ISectorRotationQueryService
{
    private const string SectorMembersUnavailableReason = "sector_members_unavailable";

    private readonly AppDbContext _dbContext;
    private readonly SectorRotationOptions _options;

    public SectorRotationQueryService(AppDbContext dbContext, IOptions<SectorRotationOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
    }

    public async Task<MarketSentimentSummaryDto?> GetLatestSummaryAsync(CancellationToken cancellationToken = default)
    {
        var recentRows = await _dbContext.MarketSentimentSnapshots
            .AsNoTracking()
            .OrderByDescending(x => x.SnapshotTime)
            .Take(24)
            .ToListAsync(cancellationToken);

        var latest = SelectBestLatestSummarySnapshot(recentRows);
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
            .Select(group => SelectBestSummarySnapshot(group) ?? group.OrderByDescending(x => x.SnapshotTime).First())
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
        var safePageSize = Math.Clamp(pageSize, 1, 50);
        var safePage = Math.Max(1, page);
        var latestMarketSnapshot = await _dbContext.MarketSentimentSnapshots
            .AsNoTracking()
            .OrderByDescending(x => x.SnapshotTime)
            .Select(x => new LatestMarketSnapshotMarker(x.SnapshotTime, x.SourceTag, x.RawJson))
            .FirstOrDefaultAsync(cancellationToken);
        var latestSnapshotTime = await _dbContext.SectorRotationSnapshots
            .AsNoTracking()
            .Where(x => x.BoardType == normalizedBoardType)
            .MaxAsync(x => (DateTime?)x.SnapshotTime, cancellationToken);

        if (ShouldSuppressStaleSectorPage(latestMarketSnapshot, latestSnapshotTime))
        {
            var latestStatus = ReadSnapshotStatus(latestMarketSnapshot!.SourceTag, latestMarketSnapshot.RawJson);
            return new SectorRotationPageDto(
                normalizedBoardType,
                safePage,
                safePageSize,
                0,
                NormalizeSort(sort),
                latestMarketSnapshot.SnapshotTime,
                Array.Empty<SectorRotationListItemDto>(),
                latestStatus.IsDegraded,
                latestStatus.DegradeReason);
        }

        if (latestSnapshotTime is null)
        {
            return new SectorRotationPageDto(normalizedBoardType, safePage, safePageSize, 0, NormalizeSort(sort), null, Array.Empty<SectorRotationListItemDto>());
        }

        var latestRows = await _dbContext.SectorRotationSnapshots
            .AsNoTracking()
            .Where(x => x.BoardType == normalizedBoardType && x.SnapshotTime == latestSnapshotTime.Value)
            .ToListAsync(cancellationToken);

        var orderedRows = ApplySort(latestRows, sort).ToArray();
        var total = orderedRows.Length;
        var items = orderedRows
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToArray();
        var hasUnavailableMemberData = latestRows.Any(HasUnavailableMemberData);

        return new SectorRotationPageDto(
            normalizedBoardType,
            safePage,
            safePageSize,
            total,
            NormalizeSort(sort),
            latestSnapshotTime,
            items.Select(MapSectorItem).ToArray(),
            hasUnavailableMemberData,
            hasUnavailableMemberData ? SectorMembersUnavailableReason : null);
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

        var hasUnavailableMemberData = HasUnavailableMemberData(latest) || historyRows.Any(HasUnavailableMemberData);
        return new SectorRotationDetailDto(
            MapSectorItem(latest),
            history,
            leaders,
            news,
            hasUnavailableMemberData,
            hasUnavailableMemberData ? SectorMembersUnavailableReason : null);
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
        var latestRows = await _dbContext.SectorRotationSnapshots
            .AsNoTracking()
            .Where(x => x.BoardType == normalizedBoardType && x.SnapshotTime == latestSnapshotTime.Value)
            .ToListAsync(cancellationToken);

        return ApplyMainlineSort(latestRows, trendWindow)
            .Take(Math.Clamp(take, 1, 20))
            .Select(MapSectorItem)
            .ToArray();
    }

    private static IEnumerable<Data.Entities.SectorRotationSnapshot> ApplyMainlineSort(
        IEnumerable<Data.Entities.SectorRotationSnapshot> rows,
        string trendWindow)
    {
        return trendWindow switch
        {
            "5d" => rows.OrderByDescending(x => x.IsMainline).ThenByDescending(x => x.StrengthAvg5d).ThenByDescending(x => x.MainlineScore),
            "20d" => rows.OrderByDescending(x => x.IsMainline).ThenByDescending(x => x.StrengthAvg20d).ThenByDescending(x => x.MainlineScore),
            _ => rows.OrderByDescending(x => x.IsMainline).ThenByDescending(x => x.StrengthAvg10d).ThenByDescending(x => x.MainlineScore)
        };
    }

    private static MarketSentimentSummaryDto MapSummary(Data.Entities.MarketSentimentSnapshot item)
    {
        var status = ReadSnapshotStatus(item);

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
            item.BrokenBoardRate5dAvg,
            status.IsDegraded,
            status.DegradeReason);
    }

    private static Data.Entities.MarketSentimentSnapshot? SelectBestLatestSummarySnapshot(
        IReadOnlyList<Data.Entities.MarketSentimentSnapshot> rows)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        var latestTradingDate = rows.Max(item => item.TradingDate.Date);
        var latestTradingDateRows = rows
            .Where(item => item.TradingDate.Date == latestTradingDate)
            .ToArray();

        return latestTradingDateRows
            .OrderByDescending(item => item.SnapshotTime)
            .FirstOrDefault();
    }

    private static Data.Entities.MarketSentimentSnapshot? SelectBestSummarySnapshot(
        IEnumerable<Data.Entities.MarketSentimentSnapshot> rows)
    {
        return rows
            .OrderByDescending(GetSummaryIntegrityScore)
            .ThenByDescending(item => item.SnapshotTime)
            .FirstOrDefault();
    }

    private static int GetSummaryIntegrityScore(Data.Entities.MarketSentimentSnapshot item)
    {
        var status = ReadSnapshotStatus(item);
        if (status.IsCriticalSummaryIncomplete)
        {
            return 0;
        }

        var score = 0;
        var breadthTotal = item.Advancers + item.Decliners + item.FlatCount;
        if (breadthTotal > 0)
        {
            score += 3;
        }

        if (item.TotalTurnover > 0)
        {
            score += 2;
        }

        if (item.Top3SectorTurnoverShare > 0 || item.Top10SectorTurnoverShare > 0)
        {
            score += 2;
        }

        if (item.LimitUpCount > 0 || item.LimitDownCount > 0 || item.BrokenBoardCount > 0 || item.MaxLimitUpStreak > 0)
        {
            score += 1;
        }

        return score;
    }

    private static bool ShouldSuppressStaleSectorPage(LatestMarketSnapshotMarker? latestMarketSnapshot, DateTime? latestSectorSnapshotTime)
    {
        if (latestMarketSnapshot is null)
        {
            return false;
        }

        var status = ReadSnapshotStatus(latestMarketSnapshot.SourceTag, latestMarketSnapshot.RawJson);
        if (!status.IsDegraded)
        {
            return false;
        }

        return latestSectorSnapshotTime is null || latestSectorSnapshotTime.Value < latestMarketSnapshot.SnapshotTime;
    }

    private static MarketSnapshotStatus ReadSnapshotStatus(Data.Entities.MarketSentimentSnapshot item)
    {
        var status = ReadSnapshotStatus(item.SourceTag, item.RawJson);
        // Bug #6: override isDegraded when core data is actually present.
        // Handles old snapshots that were written with overly aggressive isDegraded=true.
        if (status.IsDegraded
            && (item.Advancers > 0 || item.Decliners > 0 || item.LimitUpCount > 0 || item.LimitDownCount > 0))
        {
            return new MarketSnapshotStatus(false, status.IsCriticalSummaryIncomplete, status.DegradeReason);
        }
        return status;
    }

    private static MarketSnapshotStatus ReadSnapshotStatus(string? sourceTag, string? rawJson)
    {
        var isDegraded = !string.IsNullOrWhiteSpace(sourceTag)
            && sourceTag.Contains("partial", StringComparison.OrdinalIgnoreCase);
        var isCriticalSummaryIncomplete = false;
        string? degradeReason = isDegraded ? "sync_incomplete" : null;

        if (!string.IsNullOrWhiteSpace(rawJson))
        {
            try
            {
                using var document = JsonDocument.Parse(rawJson);
                if (document.RootElement.TryGetProperty("status", out var statusElement))
                {
                    if (statusElement.TryGetProperty("isDegraded", out var isDegradedElement)
                        && isDegradedElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        isDegraded = isDegradedElement.GetBoolean();
                    }

                    if (statusElement.TryGetProperty("isCriticalSummaryIncomplete", out var isCriticalElement)
                        && isCriticalElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        isCriticalSummaryIncomplete = isCriticalElement.GetBoolean();
                    }

                    if (statusElement.TryGetProperty("degradeReason", out var degradeReasonElement)
                        && degradeReasonElement.ValueKind == JsonValueKind.String)
                    {
                        degradeReason = degradeReasonElement.GetString();
                    }
                }
            }
            catch (JsonException)
            {
                // Ignore malformed legacy payloads and fall back to source tag based status.
            }
        }

        return new MarketSnapshotStatus(isDegraded, isCriticalSummaryIncomplete, string.IsNullOrWhiteSpace(degradeReason) ? null : degradeReason);
    }

    private sealed record LatestMarketSnapshotMarker(DateTime SnapshotTime, string SourceTag, string? RawJson);

    private sealed record MarketSnapshotStatus(bool IsDegraded, bool IsCriticalSummaryIncomplete, string? DegradeReason);

    private static SectorRotationListItemDto MapSectorItem(Data.Entities.SectorRotationSnapshot item)
    {
        var hasUnavailableMemberData = HasUnavailableMemberData(item);

        return new SectorRotationListItemDto(
            item.BoardType,
            item.SectorCode,
            item.SectorName,
            item.ChangePercent,
            item.MainNetInflow,
            hasUnavailableMemberData ? null : item.BreadthScore,
            item.ContinuityScore,
            item.StrengthScore,
            item.NewsSentiment,
            item.NewsHotCount,
            hasUnavailableMemberData ? null : item.LeaderSymbol,
            hasUnavailableMemberData ? null : item.LeaderName,
            hasUnavailableMemberData ? null : item.LeaderChangePercent,
            item.RankNo,
            item.SnapshotTime,
            item.RankChange5d,
            item.RankChange10d,
            item.RankChange20d,
            item.StrengthAvg5d,
            item.StrengthAvg10d,
            item.StrengthAvg20d,
            hasUnavailableMemberData ? null : item.DiffusionRate,
            hasUnavailableMemberData ? null : item.AdvancerCount,
            hasUnavailableMemberData ? null : item.DeclinerCount,
            hasUnavailableMemberData ? null : item.FlatMemberCount,
            hasUnavailableMemberData ? null : item.LimitUpMemberCount,
            item.LeaderStabilityScore,
            item.MainlineScore,
            item.IsMainline);
    }

    private static bool HasUnavailableMemberData(Data.Entities.SectorRotationSnapshot item)
    {
        if (item.AdvancerCount.GetValueOrDefault() != 0
            || item.DeclinerCount.GetValueOrDefault() != 0
            || item.FlatMemberCount.GetValueOrDefault() != 0)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(item.LeaderSymbol) || !string.IsNullOrWhiteSpace(item.LeaderName))
        {
            return false;
        }

        return IsUnavailableMemberMetric(item.BreadthScore) && IsUnavailableMemberMetric(item.DiffusionRate);
    }

    private static bool IsUnavailableMemberMetric(decimal? value)
    {
        return value is null or 0m or 50m;
    }

    private static SectorRotationHistoryPointDto[] MapHistoryPoints(IReadOnlyList<Data.Entities.SectorRotationSnapshot> historyRows, int historyWindow)
    {
        return historyRows
            .GroupBy(x => x.TradingDate.Date)
            .Select(group => group.OrderByDescending(x => x.SnapshotTime).First())
            .OrderByDescending(x => x.TradingDate)
            .Take(historyWindow)
            .OrderBy(x => x.TradingDate)
            .Select(item =>
            {
                var hasUnavailableMemberData = HasUnavailableMemberData(item);
                return new SectorRotationHistoryPointDto(
                    item.TradingDate,
                    item.SnapshotTime,
                    item.ChangePercent,
                    hasUnavailableMemberData ? null : item.BreadthScore,
                    item.ContinuityScore,
                    item.StrengthScore,
                    item.RankNo,
                    hasUnavailableMemberData ? null : item.DiffusionRate,
                    hasUnavailableMemberData ? null : item.AdvancerCount,
                    hasUnavailableMemberData ? null : item.DeclinerCount,
                    hasUnavailableMemberData ? null : item.FlatMemberCount,
                    hasUnavailableMemberData ? null : item.LimitUpMemberCount,
                    item.RankChange5d,
                    item.RankChange10d,
                    item.RankChange20d,
                    item.StrengthAvg5d,
                    item.StrengthAvg10d,
                    item.StrengthAvg20d,
                    item.LeaderStabilityScore,
                    item.MainlineScore,
                    item.IsMainline);
            })
            .ToArray();
    }

    private static IEnumerable<Data.Entities.SectorRotationSnapshot> ApplySort(IEnumerable<Data.Entities.SectorRotationSnapshot> rows, string sort)
    {
        return NormalizeSort(sort) switch
        {
            "change" => rows.OrderByDescending(x => x.ChangePercent).ThenBy(x => x.RankNo),
            "flow" => rows.OrderByDescending(x => x.MainNetInflow).ThenBy(x => x.RankNo),
            "breadth" => rows.OrderByDescending(x => HasUnavailableMemberData(x) ? -1m : x.BreadthScore ?? -1m).ThenBy(x => x.RankNo),
            "continuity" => rows.OrderByDescending(x => x.ContinuityScore).ThenBy(x => x.RankNo),
            "mainline" => rows.OrderByDescending(x => x.IsMainline).ThenByDescending(x => x.MainlineScore).ThenBy(x => x.RankNo),
            _ => rows.OrderByDescending(x => x.StrengthScore).ThenBy(x => x.RankNo)
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
            "30d" => "30d",
            "60d" => "60d",
            _ => "10d"
        };
    }

    private static int ParseWindow(string window)
    {
        return window?.Trim().ToLowerInvariant() switch
        {
            "5d" => 5,
            "20d" => 20,
            "30d" => 30,
            "60d" => 60,
            _ => 10
        };
    }

    public async Task<string> GetMainlineTrendSummaryAsync(int days, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days);
        var snapshots = await _dbContext.SectorRotationSnapshots
            .AsNoTracking()
            .Where(s => s.TradingDate >= cutoff && s.IsMainline)
            .OrderByDescending(s => s.TradingDate)
            .ToListAsync(cancellationToken);

        if (snapshots.Count == 0) return "暂无板块趋势历史数据。";

        var grouped = snapshots
            .GroupBy(s => s.TradingDate.Date)
            .OrderByDescending(g => g.Key)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"=== 近{days}天板块趋势摘要 ===");

        // 每5天采样一个数据点
        for (int i = 0; i < grouped.Count; i += 5)
        {
            var dayGroup = grouped[i];
            var topSectors = dayGroup
                .OrderByDescending(s => s.MainlineScore)
                .Take(5)
                .Select(s => $"{s.SectorName}({s.MainlineScore:F0})")
                .ToList();
            sb.AppendLine($"{dayGroup.Key:MM-dd}: 主线=[{string.Join(",", topSectors)}]");
        }

        // 板块强度变化方向
        if (grouped.Count >= 2)
        {
            var latest = grouped.First().Average(s => s.MainlineScore);
            var earliest = grouped.Last().Average(s => s.MainlineScore);
            var direction = latest > earliest + 5 ? "上升" : latest < earliest - 5 ? "下降" : "震荡";
            sb.AppendLine($"主线强度趋势: {direction} (最新均分{latest:F0}, 最早均分{earliest:F0})");
        }

        var result = sb.ToString();
        if (result.Length > 2000)
            result = result[..1997] + "...";

        return result;
    }
}
