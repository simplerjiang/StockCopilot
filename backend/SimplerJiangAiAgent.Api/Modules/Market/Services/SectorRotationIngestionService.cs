using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure.Jobs;
using SimplerJiangAiAgent.Api.Modules.Market.Models;
using System.Text.Json;
using System.Globalization;

namespace SimplerJiangAiAgent.Api.Modules.Market.Services;

public sealed class SectorRotationIngestionService : ISectorRotationIngestionService
{
    private static readonly SemaphoreSlim SyncLock = new(1, 1);
    private static readonly TimeZoneInfo ChinaTimeZone = ResolveChinaTimeZone();
    private static readonly TimeSpan MarketOpenTime = new(9, 30, 0);

    private readonly AppDbContext _dbContext;
    private readonly IEastmoneySectorRotationClient _client;
    private readonly SectorRotationOptions _options;
    private readonly ILogger<SectorRotationIngestionService> _logger;

    public SectorRotationIngestionService(
        AppDbContext dbContext,
        IEastmoneySectorRotationClient client,
        IOptions<SectorRotationOptions> options,
        ILogger<SectorRotationIngestionService> logger)
    {
        _dbContext = dbContext;
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SyncAsync(CancellationToken cancellationToken = default)
    {
        await SyncAsync(DateTimeOffset.UtcNow, cancellationToken);
    }

    internal async Task SyncAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        await SyncLock.WaitAsync(cancellationToken);
        try
        {
            var localNow = TimeZoneInfo.ConvertTime(utcNow, ChinaTimeZone);
            var tradingDate = ResolveMarketPoolTradingDate(localNow);
            var persistedTradingDate = tradingDate.ToDateTime(TimeOnly.MinValue);
            var snapshotTime = utcNow.UtcDateTime;
            var reports = await _dbContext.LocalSectorReports
                .AsNoTracking()
                .Where(x => x.Level == "sector" && x.PublishTime >= utcNow.UtcDateTime.AddDays(-7))
                .ToListAsync(cancellationToken);

            var boardTasks = SectorBoardTypes.All.ToDictionary(
                boardType => boardType,
                boardType => FetchWithFallbackAsync(
                    () => _client.GetBoardRankingsAsync(boardType, _options.BoardPageSize, cancellationToken),
                    Array.Empty<EastmoneySectorBoardRow>(),
                    $"板块榜单 {boardType}"));
            var breadthTask = FetchWithFallbackAsync(
                () => _client.GetMarketBreadthAsync(_options.BreadthSampleSize, cancellationToken),
                new EastmoneyMarketBreadthSnapshot(0, 0, 0, 0m),
                "市场广度");
            var limitUpTask = FetchWithFallbackAsync(() => _client.GetLimitUpCountAsync(tradingDate, cancellationToken), 0, "涨停池");
            var limitDownTask = FetchWithFallbackAsync(() => _client.GetLimitDownCountAsync(tradingDate, cancellationToken), 0, "跌停池");
            var brokenBoardTask = FetchWithFallbackAsync(() => _client.GetBrokenBoardCountAsync(tradingDate, cancellationToken), 0, "炸板池");
            var maxStreakTask = FetchWithFallbackAsync(() => _client.GetMaxLimitUpStreakAsync(tradingDate, cancellationToken), 0, "连板高度");

            await Task.WhenAll(boardTasks.Values.Cast<Task>()
                .Append(breadthTask)
                .Append(limitUpTask)
                .Append(limitDownTask)
                .Append(brokenBoardTask)
                .Append(maxStreakTask));

            var allRows = boardTasks.SelectMany(pair => pair.Value.Result.Value).ToList();
            var breadthSnapshot = breadthTask.Result.Value;
            var limitUpCount = limitUpTask.Result.Value;
            var limitDownCount = limitDownTask.Result.Value;
            var brokenBoardCount = brokenBoardTask.Result.Value;
            var maxLimitUpStreak = maxStreakTask.Result.Value;
            var sectorTurnoverBase = allRows.Sum(x => Math.Max(0, x.TurnoverAmount));
            var totalTurnoverBase = breadthSnapshot.TotalTurnover > 0
                ? breadthSnapshot.TotalTurnover
                : sectorTurnoverBase;
            var (top3Share, top10Share) = ComputeTurnoverConcentration(allRows, totalTurnoverBase);
            var brokenBoardRate = limitUpCount > 0
                ? brokenBoardCount / (decimal)Math.Max(1, limitUpCount) * 100m
                : 0m;
            var stageScore = ComputeStageScore(
                breadthSnapshot.Advancers,
                breadthSnapshot.Decliners,
                breadthSnapshot.FlatCount,
                limitUpCount,
                limitDownCount,
                brokenBoardRate,
                maxLimitUpStreak);
            var stageLabel = ResolveStageLabel(stageScore);

            var marketSnapshot = new MarketSentimentSnapshot
            {
                TradingDate = persistedTradingDate,
                SnapshotTime = snapshotTime,
                SessionPhase = ResolveSessionPhase(localNow),
                StageLabel = stageLabel,
                StageScore = stageScore,
                MaxLimitUpStreak = maxLimitUpStreak,
                LimitUpCount = limitUpCount,
                LimitDownCount = limitDownCount,
                BrokenBoardCount = brokenBoardCount,
                BrokenBoardRate = decimal.Round(brokenBoardRate, 2),
                Advancers = breadthSnapshot.Advancers,
                Decliners = breadthSnapshot.Decliners,
                FlatCount = breadthSnapshot.FlatCount,
                TotalTurnover = decimal.Round(totalTurnoverBase, 2),
                Top3SectorTurnoverShare = top3Share,
                Top10SectorTurnoverShare = top10Share,
                SourceTag = "eastmoney",
                RawJson = JsonSerializer.Serialize(new
                {
                    breadth = breadthSnapshot,
                    limitUp = limitUpCount,
                    limitDown = limitDownCount,
                    brokenBoard = brokenBoardCount,
                    maxStreak = maxLimitUpStreak
                }),
                CreatedAt = snapshotTime
            };

            var shouldPersistMarketSnapshot = ShouldPersistMarketSnapshot(
                marketSnapshot,
                breadthTask.Result.Succeeded,
                limitUpTask.Result.Succeeded,
                limitDownTask.Result.Succeeded,
                brokenBoardTask.Result.Succeeded,
                maxStreakTask.Result.Succeeded);

            if (shouldPersistMarketSnapshot)
            {
                // Log if any supplementary sources failed but we still persist
                if (!limitUpTask.Result.Succeeded || !limitDownTask.Result.Succeeded || !brokenBoardTask.Result.Succeeded || !maxStreakTask.Result.Succeeded)
                {
                    _logger.LogWarning(
                        "MarketSentimentSnapshot 以降级模式落库：部分数据源失败。limitUp={LimitUpSucceeded}, limitDown={LimitDownSucceeded}, brokenBoard={BrokenBoardSucceeded}, maxStreak={MaxStreakSucceeded}",
                        limitUpTask.Result.Succeeded,
                        limitDownTask.Result.Succeeded,
                        brokenBoardTask.Result.Succeeded,
                        maxStreakTask.Result.Succeeded);
                }
                _dbContext.MarketSentimentSnapshots.Add(marketSnapshot);
            }
            else
            {
                _logger.LogWarning(
                    "跳过 MarketSentimentSnapshot 落库：关键市场数据抓取不完整。breadth={BreadthSucceeded}, limitUp={LimitUpSucceeded}, limitDown={LimitDownSucceeded}, brokenBoard={BrokenBoardSucceeded}, maxStreak={MaxStreakSucceeded}, turnover={TotalTurnover}, advancers={Advancers}, decliners={Decliners}",
                    breadthTask.Result.Succeeded,
                    limitUpTask.Result.Succeeded,
                    limitDownTask.Result.Succeeded,
                    brokenBoardTask.Result.Succeeded,
                    maxStreakTask.Result.Succeeded,
                    marketSnapshot.TotalTurnover,
                    marketSnapshot.Advancers,
                    marketSnapshot.Decliners);
            }

            var currentSectorSnapshots = new List<SectorRotationSnapshot>();

            foreach (var row in allRows)
            {
                    var members = (await FetchWithFallbackAsync(
                        () => _client.GetSectorLeadersAsync(row.SectorCode, Math.Max(_options.LeaderTake, _options.SectorMemberTake), cancellationToken),
                        Array.Empty<EastmoneySectorLeaderRow>(),
                        $"板块龙头 {row.SectorCode}")).Value.ToList();
                    var leaders = members.Take(Math.Max(1, _options.LeaderTake)).ToList();
                var newsInsight = BuildNewsInsight(row.SectorName, reports);
                    var advancerCount = members.Count(x => x.ChangePercent > 0);
                    var declinerCount = members.Count(x => x.ChangePercent < 0);
                    var flatMemberCount = Math.Max(0, members.Count - advancerCount - declinerCount);
                    var limitUpMemberCount = members.Count(x => x.IsLimitUp);
                    var diffusionRate = members.Count > 0
                        ? decimal.Round(advancerCount / (decimal)members.Count * 100m, 2)
                        : 50m;
                    var breadthScore = members.Count > 0
                        ? decimal.Round((advancerCount + limitUpMemberCount * 0.5m) / members.Count * 100m, 2)
                    : 50m;
                var continuityScore = decimal.Round(Clamp(50m + row.ChangePercent * 5m + (leaders.FirstOrDefault()?.ChangePercent ?? 0m) * 2m, 0m, 100m), 2);
                var flowScore = totalTurnoverBase > 0
                    ? Clamp(row.MainNetInflow / totalTurnoverBase * 1000m + 50m, 0m, 100m)
                    : 50m;
                var strengthScore = decimal.Round(Clamp(breadthScore * 0.35m + continuityScore * 0.35m + flowScore * 0.30m, 0m, 100m), 2);

                    var snapshot = new SectorRotationSnapshot
                {
                    TradingDate = persistedTradingDate,
                    SnapshotTime = snapshotTime,
                    BoardType = row.BoardType,
                    SectorCode = row.SectorCode,
                    SectorName = row.SectorName,
                    ChangePercent = row.ChangePercent,
                    MainNetInflow = row.MainNetInflow,
                    SuperLargeNetInflow = row.SuperLargeNetInflow,
                    LargeNetInflow = row.LargeNetInflow,
                    MediumNetInflow = row.MediumNetInflow,
                    SmallNetInflow = row.SmallNetInflow,
                    TurnoverAmount = row.TurnoverAmount,
                    TurnoverShare = row.TurnoverShare,
                    BreadthScore = breadthScore,
                    ContinuityScore = continuityScore,
                    StrengthScore = strengthScore,
                    NewsSentiment = newsInsight.sentiment,
                    NewsHotCount = newsInsight.hotCount,
                    LeaderSymbol = leaders.FirstOrDefault()?.Symbol,
                    LeaderName = leaders.FirstOrDefault()?.Name,
                    LeaderChangePercent = leaders.FirstOrDefault()?.ChangePercent,
                    RankNo = row.RankNo,
                    Momentum5d = null,
                    Momentum10d = null,
                    Momentum20d = null,
                    RankChange5d = 0,
                    RankChange10d = 0,
                    RankChange20d = 0,
                    StrengthAvg5d = strengthScore,
                    StrengthAvg10d = strengthScore,
                    StrengthAvg20d = strengthScore,
                    DiffusionRate = diffusionRate,
                    AdvancerCount = advancerCount,
                    DeclinerCount = declinerCount,
                    FlatMemberCount = flatMemberCount,
                    LimitUpMemberCount = limitUpMemberCount,
                    LeaderStabilityScore = 0m,
                    MainlineScore = 0m,
                    IsMainline = false,
                    SourceTag = "eastmoney",
                    RawJson = row.RawJson,
                    CreatedAt = snapshotTime,
                    Leaders = leaders.Select(item => new SectorRotationLeaderSnapshot
                    {
                        RankInSector = item.RankInSector,
                        Symbol = item.Symbol,
                        Name = item.Name,
                        ChangePercent = item.ChangePercent,
                        TurnoverAmount = item.TurnoverAmount,
                        IsLimitUp = item.IsLimitUp,
                        IsBrokenBoard = item.IsBrokenBoard,
                        CreatedAt = snapshotTime
                    }).ToList()
                };

                currentSectorSnapshots.Add(snapshot);
                _dbContext.SectorRotationSnapshots.Add(snapshot);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await ApplyRollingMetricsAsync(shouldPersistMarketSnapshot ? marketSnapshot : null, currentSectorSnapshots, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "同步 GOAL-009-R1 市场情绪与板块轮动快照失败");
            throw;
        }
        finally
        {
            SyncLock.Release();
        }
    }

    internal static DateOnly ResolveMarketPoolTradingDate(DateTimeOffset localNow)
    {
        var candidate = DateOnly.FromDateTime(localNow.DateTime);
        if (ChinaAStockMarketClock.IsTradingDay(candidate) && localNow.TimeOfDay >= MarketOpenTime)
        {
            return candidate;
        }

        do
        {
            candidate = candidate.AddDays(-1);
        }
        while (!ChinaAStockMarketClock.IsTradingDay(candidate));

        return candidate;
    }

    internal static (decimal Top3Share, decimal Top10Share) ComputeTurnoverConcentration(
        IReadOnlyList<EastmoneySectorBoardRow> rows,
        decimal totalTurnoverBase)
    {
        if (totalTurnoverBase <= 0)
        {
            return (0m, 0m);
        }

        var topRows = rows
            .Where(x => x.TurnoverAmount > 0)
            .OrderByDescending(x => x.TurnoverAmount)
            .ToList();

        var top3Share = Clamp(topRows.Take(3).Sum(x => x.TurnoverAmount) / totalTurnoverBase * 100m, 0m, 100m);
        var top10Share = Clamp(topRows.Take(10).Sum(x => x.TurnoverAmount) / totalTurnoverBase * 100m, 0m, 100m);
        return (decimal.Round(top3Share, 2), decimal.Round(top10Share, 2));
    }

    private async Task ApplyRollingMetricsAsync(
        MarketSentimentSnapshot? marketSnapshot,
        IReadOnlyList<SectorRotationSnapshot> currentSectorSnapshots,
        CancellationToken cancellationToken)
    {
        foreach (var snapshot in currentSectorSnapshots)
        {
            var dailyHistory = await LoadDailySectorHistoryAsync(snapshot.SectorCode, snapshot.BoardType, snapshot.TradingDate, cancellationToken);
            snapshot.Momentum5d = RoundAverage(dailyHistory.Take(5).Select(item => item.ChangePercent));
            snapshot.Momentum10d = RoundAverage(dailyHistory.Take(10).Select(item => item.ChangePercent));
            snapshot.Momentum20d = RoundAverage(dailyHistory.Take(20).Select(item => item.ChangePercent));
            snapshot.StrengthAvg5d = RoundAverage(dailyHistory.Take(5).Select(item => item.StrengthScore)) ?? snapshot.StrengthScore;
            snapshot.StrengthAvg10d = RoundAverage(dailyHistory.Take(10).Select(item => item.StrengthScore)) ?? snapshot.StrengthScore;
            snapshot.StrengthAvg20d = RoundAverage(dailyHistory.Take(20).Select(item => item.StrengthScore)) ?? snapshot.StrengthScore;
            snapshot.RankChange5d = ComputeRankChange(dailyHistory, 5);
            snapshot.RankChange10d = ComputeRankChange(dailyHistory, 10);
            snapshot.RankChange20d = ComputeRankChange(dailyHistory, 20);
            snapshot.LeaderStabilityScore = ComputeLeaderStability(dailyHistory, snapshot.LeaderSymbol, snapshot.LeaderName);
            snapshot.MainlineScore = ComputeMainlineScore(snapshot);
        }

        foreach (var group in currentSectorSnapshots.GroupBy(item => item.BoardType, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group
                .OrderByDescending(item => item.MainlineScore)
                .ThenByDescending(item => item.StrengthAvg10d)
                .ThenBy(item => item.RankNo)
                .ToList();

            for (var index = 0; index < ordered.Count; index += 1)
            {
                var item = ordered[index];
                item.IsMainline = index < 3 && item.MainlineScore >= 60m && item.StrengthAvg5d >= 55m;
            }
        }

        if (marketSnapshot is null)
        {
            return;
        }

        var marketHistory = await LoadDailyMarketHistoryAsync(marketSnapshot.TradingDate, cancellationToken);
        marketSnapshot.Top3SectorTurnoverShare5dAvg = RoundAverage(marketHistory.Take(5).Select(item => item.Top3SectorTurnoverShare)) ?? marketSnapshot.Top3SectorTurnoverShare;
        marketSnapshot.Top10SectorTurnoverShare5dAvg = RoundAverage(marketHistory.Take(5).Select(item => item.Top10SectorTurnoverShare)) ?? marketSnapshot.Top10SectorTurnoverShare;
        marketSnapshot.LimitUpCount5dAvg = RoundAverage(marketHistory.Take(5).Select(item => (decimal)item.LimitUpCount)) ?? marketSnapshot.LimitUpCount;
        marketSnapshot.BrokenBoardRate5dAvg = RoundAverage(marketHistory.Take(5).Select(item => item.BrokenBoardRate)) ?? marketSnapshot.BrokenBoardRate;

        var topSectors = currentSectorSnapshots
            .OrderByDescending(item => item.MainlineScore)
            .ThenBy(item => item.RankNo)
            .Take(10)
            .ToList();
        marketSnapshot.DiffusionScore = ComputeDiffusionScore(marketSnapshot, topSectors);
        marketSnapshot.ContinuationScore = ComputeContinuationScore(topSectors);
        var (stageLabelV2, stageConfidence) = ResolveStageV2(marketSnapshot.StageScore, marketSnapshot.DiffusionScore, marketSnapshot.ContinuationScore);
        marketSnapshot.StageLabelV2 = stageLabelV2;
        marketSnapshot.StageConfidence = stageConfidence;
    }

    private async Task<FetchResult<T>> FetchWithFallbackAsync<T>(Func<Task<T>> action, T fallback, string sourceName)
    {
        try
        {
            return new FetchResult<T>(await action(), true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GOAL-009 数据源失败，已降级跳过: {SourceName}", sourceName);
            return new FetchResult<T>(fallback, false);
        }
    }

    private static bool ShouldPersistMarketSnapshot(
        MarketSentimentSnapshot marketSnapshot,
        bool breadthSucceeded,
        bool limitUpSucceeded,
        bool limitDownSucceeded,
        bool brokenBoardSucceeded,
        bool maxStreakSucceeded)
    {
        // Core requirement: breadth data must succeed (provides Advancers/Decliners for stage score)
        if (!breadthSucceeded)
        {
            return false;
        }

        var breadthTotal = marketSnapshot.Advancers + marketSnapshot.Decliners + marketSnapshot.FlatCount;
        // Require non-zero breadth and turnover; supplementary sources (limitUp/limitDown/brokenBoard/maxStreak)
        // are best-effort — their fallback defaults (0) are acceptable for snapshot persistence.
        return breadthTotal > 0
            && marketSnapshot.TotalTurnover > 0;
    }

    private sealed record FetchResult<T>(T Value, bool Succeeded);

    private static (string sentiment, int hotCount) BuildNewsInsight(string sectorName, IReadOnlyList<LocalSectorReport> reports)
    {
        var matched = reports
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.SectorName)
                && (string.Equals(item.SectorName, sectorName, StringComparison.OrdinalIgnoreCase)
                    || item.SectorName.Contains(sectorName, StringComparison.OrdinalIgnoreCase)
                    || sectorName.Contains(item.SectorName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (matched.Count == 0)
        {
            return ("中性", 0);
        }

        var positive = matched.Count(item => item.AiSentiment == "利好");
        var negative = matched.Count(item => item.AiSentiment == "利空");
        var sentiment = positive > negative ? "利好" : negative > positive ? "利空" : "中性";
        return (sentiment, matched.Count);
    }

    private static decimal ComputeStageScore(int advancers, int decliners, int flatCount, int limitUpCount, int limitDownCount, decimal brokenBoardRate, int maxLimitUpStreak)
    {
        var total = Math.Max(1, advancers + decliners + flatCount);
        var breadthContribution = (advancers - decliners) / (decimal)total * 30m;
        var score = 45m
            + maxLimitUpStreak * 4m
            + Math.Clamp(limitUpCount - limitDownCount, -30, 30) * 1.2m
            + breadthContribution
            - brokenBoardRate * 0.35m;

        return decimal.Round(Clamp(score, 0m, 100m), 2);
    }

    private static string ResolveStageLabel(decimal stageScore)
    {
        if (stageScore >= 70m)
        {
            return "主升";
        }

        if (stageScore >= 52m)
        {
            return "分歧";
        }

        if (stageScore <= 28m)
        {
            return "退潮";
        }

        return "混沌";
    }

    private static string ResolveSessionPhase(DateTimeOffset localNow)
    {
        var time = localNow.TimeOfDay;
        if (time < MarketOpenTime)
        {
            return "盘前";
        }

        if (ChinaAStockMarketClock.IsTradingSession(localNow))
        {
            return "盘中";
        }

        return "盘后";
    }

    private static decimal Clamp(decimal value, decimal min, decimal max)
    {
        return Math.Min(Math.Max(value, min), max);
    }

    private async Task<List<SectorRotationSnapshot>> LoadDailySectorHistoryAsync(string sectorCode, string boardType, DateTime tradingDate, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.SectorRotationSnapshots
            .AsNoTracking()
            .Where(item => item.SectorCode == sectorCode && item.BoardType == boardType && item.TradingDate <= tradingDate)
            .OrderByDescending(item => item.SnapshotTime)
            .Take(240)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(item => item.TradingDate.Date)
            .Select(group => group.OrderByDescending(item => item.SnapshotTime).First())
            .OrderByDescending(item => item.TradingDate)
            .Take(20)
            .ToList();
    }

    private async Task<List<MarketSentimentSnapshot>> LoadDailyMarketHistoryAsync(DateTime tradingDate, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.MarketSentimentSnapshots
            .AsNoTracking()
            .Where(item => item.TradingDate <= tradingDate)
            .OrderByDescending(item => item.SnapshotTime)
            .Take(120)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(item => item.TradingDate.Date)
            .Select(group => group.OrderByDescending(item => item.SnapshotTime).First())
            .OrderByDescending(item => item.TradingDate)
            .Take(5)
            .ToList();
    }

    private static int ComputeRankChange(IReadOnlyList<SectorRotationSnapshot> rows, int window)
    {
        var sample = rows.Take(window).ToList();
        if (sample.Count < 2)
        {
            return 0;
        }

        return sample[^1].RankNo - sample[0].RankNo;
    }

    private static decimal ComputeLeaderStability(IReadOnlyList<SectorRotationSnapshot> rows, string? leaderSymbol, string? leaderName)
    {
        if (rows.Count == 0 || (string.IsNullOrWhiteSpace(leaderSymbol) && string.IsNullOrWhiteSpace(leaderName)))
        {
            return 0m;
        }

        var matched = rows.Count(item =>
            (!string.IsNullOrWhiteSpace(leaderSymbol) && string.Equals(item.LeaderSymbol, leaderSymbol, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(leaderName) && string.Equals(item.LeaderName, leaderName, StringComparison.OrdinalIgnoreCase)));

        return decimal.Round(Clamp(matched / (decimal)rows.Count * 100m, 0m, 100m), 2);
    }

    private static decimal ComputeMainlineScore(SectorRotationSnapshot snapshot)
    {
        var rankScore = Clamp(50m + snapshot.RankChange10d * 6m, 0m, 100m);
        var value = snapshot.StrengthAvg10d * 0.30m
            + snapshot.DiffusionRate * 0.20m
            + snapshot.ContinuityScore * 0.15m
            + snapshot.LeaderStabilityScore * 0.15m
            + rankScore * 0.20m;
        return decimal.Round(Clamp(value, 0m, 100m), 2);
    }

    private static decimal ComputeDiffusionScore(MarketSentimentSnapshot marketSnapshot, IReadOnlyList<SectorRotationSnapshot> topSectors)
    {
        var total = Math.Max(1, marketSnapshot.Advancers + marketSnapshot.Decliners + marketSnapshot.FlatCount);
        var marketBreadth = Clamp(50m + (marketSnapshot.Advancers - marketSnapshot.Decliners) / (decimal)total * 50m, 0m, 100m);
        var sectorDiffusion = RoundAverage(topSectors.Select(item => item.DiffusionRate)) ?? 50m;
        return decimal.Round(Clamp(marketBreadth * 0.55m + sectorDiffusion * 0.45m, 0m, 100m), 2);
    }

    private static decimal ComputeContinuationScore(IReadOnlyList<SectorRotationSnapshot> topSectors)
    {
        if (topSectors.Count == 0)
        {
            return 50m;
        }

        var strength5 = RoundAverage(topSectors.Select(item => item.StrengthAvg5d)) ?? 50m;
        var strength10 = RoundAverage(topSectors.Select(item => item.StrengthAvg10d)) ?? 50m;
        var mainline = RoundAverage(topSectors.Select(item => item.MainlineScore)) ?? 50m;
        return decimal.Round(Clamp(strength5 * 0.4m + strength10 * 0.35m + mainline * 0.25m, 0m, 100m), 2);
    }

    private static (string StageLabelV2, decimal StageConfidence) ResolveStageV2(decimal stageScore, decimal diffusionScore, decimal continuationScore)
    {
        var composite = Clamp(stageScore * 0.4m + diffusionScore * 0.3m + continuationScore * 0.3m, 0m, 100m);
        var label = ResolveStageLabel(composite);
        var confidence = Clamp(55m + Math.Abs(composite - 50m) * 0.9m + Math.Abs(diffusionScore - continuationScore) * 0.15m, 0m, 100m);
        return (label, decimal.Round(confidence, 2));
    }

    private static decimal? RoundAverage(IEnumerable<decimal> values)
    {
        var numbers = values.ToArray();
        if (numbers.Length == 0)
        {
            return null;
        }

        return decimal.Round(numbers.Average(), 2);
    }

    private static TimeZoneInfo ResolveChinaTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("China Standard Time", TimeSpan.FromHours(8), "China Standard Time", "China Standard Time");
        }
    }
}
