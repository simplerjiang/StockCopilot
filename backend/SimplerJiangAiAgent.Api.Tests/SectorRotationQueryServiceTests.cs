using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Modules.Market.Models;
using SimplerJiangAiAgent.Api.Modules.Market.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class SectorRotationQueryServiceTests
{
    [Fact]
    public async Task GetLatestSummaryAsync_PrefersLatestUsableSnapshotOverNewerBrokenRow()
    {
        await using var dbContext = CreateDbContext();
        dbContext.MarketSentimentSnapshots.AddRange(
            new MarketSentimentSnapshot
            {
                TradingDate = new DateTime(2026, 3, 15),
                SnapshotTime = new DateTime(2026, 3, 15, 6, 35, 0, DateTimeKind.Utc),
                SessionPhase = "盘中",
                StageLabel = "主升",
                StageScore = 78.6m,
                MaxLimitUpStreak = 5,
                LimitUpCount = 153,
                LimitDownCount = 3,
                BrokenBoardCount = 21,
                BrokenBoardRate = 13.7m,
                Advancers = 4992,
                Decliners = 299,
                FlatCount = 15,
                TotalTurnover = 18234m,
                Top3SectorTurnoverShare = 26.4m,
                Top10SectorTurnoverShare = 58.8m,
                SourceTag = "test",
                CreatedAt = DateTime.UtcNow
            },
            new MarketSentimentSnapshot
            {
                TradingDate = new DateTime(2026, 3, 15),
                SnapshotTime = new DateTime(2026, 3, 15, 6, 36, 0, DateTimeKind.Utc),
                SessionPhase = "盘中",
                StageLabel = "主升",
                StageScore = 77.2m,
                MaxLimitUpStreak = 0,
                LimitUpCount = 0,
                LimitDownCount = 1,
                BrokenBoardCount = 0,
                BrokenBoardRate = 0m,
                Advancers = 0,
                Decliners = 0,
                FlatCount = 0,
                TotalTurnover = 0m,
                Top3SectorTurnoverShare = 0m,
                Top10SectorTurnoverShare = 0m,
                SourceTag = "test",
                CreatedAt = DateTime.UtcNow
            });
        await dbContext.SaveChangesAsync();

        var service = new SectorRotationQueryService(dbContext, Options.Create(new SectorRotationOptions()));

        var result = await service.GetLatestSummaryAsync();

        Assert.NotNull(result);
        Assert.Equal(153, result!.LimitUpCount);
        Assert.Equal(4992, result.Advancers);
        Assert.Equal(26.4m, result.Top3SectorTurnoverShare);
        Assert.Equal(new DateTime(2026, 3, 15, 6, 35, 0, DateTimeKind.Utc), result.SnapshotTime);
    }

    [Fact]
    public async Task GetHistoryAsync_PrefersUsableDailySnapshotWhenLatestRowIsBroken()
    {
        await using var dbContext = CreateDbContext();
        dbContext.MarketSentimentSnapshots.AddRange(
            new MarketSentimentSnapshot
            {
                TradingDate = new DateTime(2026, 3, 14),
                SnapshotTime = new DateTime(2026, 3, 14, 6, 35, 0, DateTimeKind.Utc),
                SessionPhase = "盘后",
                StageLabel = "分歧",
                StageScore = 55m,
                LimitUpCount = 88,
                LimitDownCount = 6,
                BrokenBoardCount = 18,
                Advancers = 3120,
                Decliners = 1800,
                FlatCount = 120,
                TotalTurnover = 13500m,
                Top3SectorTurnoverShare = 24.8m,
                Top10SectorTurnoverShare = 54.3m,
                SourceTag = "test",
                CreatedAt = DateTime.UtcNow
            },
            new MarketSentimentSnapshot
            {
                TradingDate = new DateTime(2026, 3, 15),
                SnapshotTime = new DateTime(2026, 3, 15, 6, 35, 0, DateTimeKind.Utc),
                SessionPhase = "盘中",
                StageLabel = "主升",
                StageScore = 78.6m,
                LimitUpCount = 153,
                LimitDownCount = 3,
                BrokenBoardCount = 21,
                Advancers = 4992,
                Decliners = 299,
                FlatCount = 15,
                TotalTurnover = 18234m,
                Top3SectorTurnoverShare = 26.4m,
                Top10SectorTurnoverShare = 58.8m,
                SourceTag = "test",
                CreatedAt = DateTime.UtcNow
            },
            new MarketSentimentSnapshot
            {
                TradingDate = new DateTime(2026, 3, 15),
                SnapshotTime = new DateTime(2026, 3, 15, 6, 36, 0, DateTimeKind.Utc),
                SessionPhase = "盘中",
                StageLabel = "主升",
                StageScore = 77.2m,
                LimitUpCount = 0,
                LimitDownCount = 1,
                BrokenBoardCount = 0,
                Advancers = 0,
                Decliners = 0,
                FlatCount = 0,
                TotalTurnover = 0m,
                Top3SectorTurnoverShare = 0m,
                Top10SectorTurnoverShare = 0m,
                SourceTag = "test",
                CreatedAt = DateTime.UtcNow
            });
        await dbContext.SaveChangesAsync();

        var service = new SectorRotationQueryService(dbContext, Options.Create(new SectorRotationOptions()));

        var result = await service.GetHistoryAsync(10);

        Assert.Equal(2, result.Count);
        Assert.Equal(new DateTime(2026, 3, 15), result[1].TradingDate);
        Assert.Equal(153, result[1].LimitUpCount);
        Assert.Equal(3, result[1].LimitDownCount);
        Assert.Equal(21, result[1].BrokenBoardCount);
    }

    [Fact]
    public async Task GetSectorPageAsync_UsesLatestSnapshotAndRequestedSort()
    {
        await using var dbContext = CreateDbContext();
        dbContext.SectorRotationSnapshots.AddRange(
            new SectorRotationSnapshot
            {
                TradingDate = new DateTime(2026, 3, 14),
                SnapshotTime = new DateTime(2026, 3, 14, 6, 30, 0, DateTimeKind.Utc),
                BoardType = SectorBoardTypes.Concept,
                SectorCode = "BKOLD",
                SectorName = "旧快照",
                ChangePercent = 9.9m,
                MainNetInflow = 999m,
                BreadthScore = 90,
                ContinuityScore = 90,
                StrengthScore = 90,
                NewsSentiment = "利好",
                RankNo = 1,
                SourceTag = "test",
                CreatedAt = DateTime.UtcNow
            },
            new SectorRotationSnapshot
            {
                TradingDate = new DateTime(2026, 3, 15),
                SnapshotTime = new DateTime(2026, 3, 15, 6, 35, 0, DateTimeKind.Utc),
                BoardType = SectorBoardTypes.Concept,
                SectorCode = "BK001",
                SectorName = "机器人",
                ChangePercent = 4.2m,
                MainNetInflow = 120m,
                BreadthScore = 80,
                ContinuityScore = 70,
                StrengthScore = 85,
                NewsSentiment = "利好",
                RankNo = 2,
                SourceTag = "test",
                CreatedAt = DateTime.UtcNow
            },
            new SectorRotationSnapshot
            {
                TradingDate = new DateTime(2026, 3, 15),
                SnapshotTime = new DateTime(2026, 3, 15, 6, 35, 0, DateTimeKind.Utc),
                BoardType = SectorBoardTypes.Concept,
                SectorCode = "BK002",
                SectorName = "算力",
                ChangePercent = 6.8m,
                MainNetInflow = 80m,
                BreadthScore = 76,
                ContinuityScore = 66,
                StrengthScore = 73,
                NewsSentiment = "中性",
                RankNo = 1,
                SourceTag = "test",
                CreatedAt = DateTime.UtcNow
            });
        await dbContext.SaveChangesAsync();

        var service = new SectorRotationQueryService(dbContext, Options.Create(new SectorRotationOptions()));

        var result = await service.GetSectorPageAsync(SectorBoardTypes.Concept, 1, 20, "change");

        Assert.Equal(2, result.Total);
        Assert.Equal("BK002", result.Items[0].SectorCode);
        Assert.DoesNotContain(result.Items, item => item.SectorCode == "BKOLD");
    }

    [Fact]
    public async Task GetSectorPageAndMainlineAsync_WorkWithSqliteDecimalOrdering()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = CreateSqliteDbContext(connection);
        dbContext.SectorRotationSnapshots.AddRange(
            new SectorRotationSnapshot
            {
                TradingDate = new DateTime(2026, 3, 15),
                SnapshotTime = new DateTime(2026, 3, 15, 7, 0, 0, DateTimeKind.Utc),
                BoardType = SectorBoardTypes.Concept,
                SectorCode = "BK001",
                SectorName = "机器人",
                ChangePercent = 4.2m,
                MainNetInflow = 120m,
                BreadthScore = 80m,
                ContinuityScore = 72m,
                StrengthScore = 85m,
                RankNo = 2,
                StrengthAvg10d = 71m,
                MainlineScore = 79m,
                IsMainline = true,
                NewsSentiment = "利好",
                SourceTag = "test",
                CreatedAt = DateTime.UtcNow
            },
            new SectorRotationSnapshot
            {
                TradingDate = new DateTime(2026, 3, 15),
                SnapshotTime = new DateTime(2026, 3, 15, 7, 0, 0, DateTimeKind.Utc),
                BoardType = SectorBoardTypes.Concept,
                SectorCode = "BK002",
                SectorName = "算力",
                ChangePercent = 6.8m,
                MainNetInflow = 80m,
                BreadthScore = 76m,
                ContinuityScore = 66m,
                StrengthScore = 73m,
                RankNo = 1,
                StrengthAvg10d = 62m,
                MainlineScore = 61m,
                IsMainline = false,
                NewsSentiment = "中性",
                SourceTag = "test",
                CreatedAt = DateTime.UtcNow
            });
        await dbContext.SaveChangesAsync();

        var service = new SectorRotationQueryService(dbContext, Options.Create(new SectorRotationOptions()));

        var page = await service.GetSectorPageAsync(SectorBoardTypes.Concept, 1, 20, "change");
        var mainline = await service.GetMainlineAsync(SectorBoardTypes.Concept, "10d", 5);

        Assert.Equal(new[] { "BK002", "BK001" }, page.Items.Select(item => item.SectorCode).ToArray());
        Assert.Equal(new[] { "BK001", "BK002" }, mainline.Select(item => item.SectorCode).ToArray());
    }

    [Fact]
    public async Task GetSectorDetailAsync_ReturnsLatestDetailWithLeadersAndNews()
    {
        await using var dbContext = CreateDbContext();
        var snapshot = new SectorRotationSnapshot
        {
            TradingDate = new DateTime(2026, 3, 15),
            SnapshotTime = new DateTime(2026, 3, 15, 6, 35, 0, DateTimeKind.Utc),
            BoardType = SectorBoardTypes.Industry,
            SectorCode = "BK1001",
            SectorName = "半导体",
            ChangePercent = 3.5m,
            MainNetInflow = 456m,
            BreadthScore = 78,
            ContinuityScore = 69,
            StrengthScore = 81,
            NewsSentiment = "利好",
            NewsHotCount = 4,
            LeaderSymbol = "688001",
            LeaderName = "华芯",
            LeaderChangePercent = 8.8m,
            RankNo = 1,
            SourceTag = "test",
            CreatedAt = DateTime.UtcNow
        };
        dbContext.SectorRotationSnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync();

        dbContext.SectorRotationLeaderSnapshots.AddRange(
            new SectorRotationLeaderSnapshot
            {
                SectorRotationSnapshotId = snapshot.Id,
                RankInSector = 1,
                Symbol = "688001",
                Name = "华芯",
                ChangePercent = 8.8m,
                TurnoverAmount = 120m,
                IsLimitUp = false,
                CreatedAt = DateTime.UtcNow
            },
            new SectorRotationLeaderSnapshot
            {
                SectorRotationSnapshotId = snapshot.Id,
                RankInSector = 2,
                Symbol = "688002",
                Name = "芯源",
                ChangePercent = 6.1m,
                TurnoverAmount = 90m,
                IsLimitUp = false,
                CreatedAt = DateTime.UtcNow
            });
        dbContext.LocalSectorReports.Add(new LocalSectorReport
        {
            SectorName = "半导体",
            Level = "sector",
            Title = "Semiconductor demand improved",
            TranslatedTitle = "半导体景气度回暖",
            Source = "证券时报",
            SourceTag = "test",
            PublishTime = new DateTime(2026, 3, 15, 4, 0, 0, DateTimeKind.Utc),
            CrawledAt = DateTime.UtcNow,
            AiSentiment = "利好",
            Url = "https://example.test/news/1"
        });
        await dbContext.SaveChangesAsync();

        var service = new SectorRotationQueryService(
            dbContext,
            Options.Create(new SectorRotationOptions { LeaderTake = 5, DetailNewsTake = 5 }));

        var result = await service.GetSectorDetailAsync("BK1001", SectorBoardTypes.Industry, "10d");

        Assert.NotNull(result);
        Assert.Equal("半导体", result!.Snapshot.SectorName);
        Assert.Equal(2, result.Leaders.Count);
        Assert.Single(result.News);
        Assert.Equal("半导体景气度回暖", result.News[0].TranslatedTitle);
    }

    [Fact]
    public async Task GetSectorTrendAndMainlineAsync_ReturnsRollingFields()
    {
        await using var dbContext = CreateDbContext();
        dbContext.SectorRotationSnapshots.AddRange(
            new SectorRotationSnapshot
            {
                TradingDate = new DateTime(2026, 3, 14),
                SnapshotTime = new DateTime(2026, 3, 14, 7, 0, 0, DateTimeKind.Utc),
                BoardType = SectorBoardTypes.Concept,
                SectorCode = "BK001",
                SectorName = "机器人",
                ChangePercent = 3.2m,
                MainNetInflow = 100m,
                BreadthScore = 80m,
                ContinuityScore = 72m,
                StrengthScore = 78m,
                RankNo = 3,
                StrengthAvg5d = 70m,
                StrengthAvg10d = 68m,
                StrengthAvg20d = 62m,
                RankChange5d = 2,
                RankChange10d = 3,
                RankChange20d = 4,
                DiffusionRate = 74m,
                AdvancerCount = 18,
                DeclinerCount = 6,
                FlatMemberCount = 2,
                LimitUpMemberCount = 2,
                LeaderStabilityScore = 66m,
                MainlineScore = 72m,
                IsMainline = true,
                NewsSentiment = "利好",
                SourceTag = "test",
                CreatedAt = DateTime.UtcNow
            },
            new SectorRotationSnapshot
            {
                TradingDate = new DateTime(2026, 3, 15),
                SnapshotTime = new DateTime(2026, 3, 15, 7, 0, 0, DateTimeKind.Utc),
                BoardType = SectorBoardTypes.Concept,
                SectorCode = "BK001",
                SectorName = "机器人",
                ChangePercent = 4.8m,
                MainNetInflow = 120m,
                BreadthScore = 84m,
                ContinuityScore = 76m,
                StrengthScore = 82m,
                RankNo = 1,
                StrengthAvg5d = 76m,
                StrengthAvg10d = 71m,
                StrengthAvg20d = 65m,
                RankChange5d = 2,
                RankChange10d = 4,
                RankChange20d = 6,
                DiffusionRate = 81m,
                AdvancerCount = 22,
                DeclinerCount = 5,
                FlatMemberCount = 1,
                LimitUpMemberCount = 3,
                LeaderStabilityScore = 70m,
                MainlineScore = 79m,
                IsMainline = true,
                NewsSentiment = "利好",
                SourceTag = "test",
                CreatedAt = DateTime.UtcNow
            },
            new SectorRotationSnapshot
            {
                TradingDate = new DateTime(2026, 3, 15),
                SnapshotTime = new DateTime(2026, 3, 15, 7, 0, 0, DateTimeKind.Utc),
                BoardType = SectorBoardTypes.Concept,
                SectorCode = "BK002",
                SectorName = "算力",
                ChangePercent = 2.6m,
                MainNetInflow = 80m,
                BreadthScore = 70m,
                ContinuityScore = 64m,
                StrengthScore = 68m,
                RankNo = 2,
                StrengthAvg5d = 66m,
                StrengthAvg10d = 62m,
                StrengthAvg20d = 58m,
                RankChange5d = 1,
                RankChange10d = 2,
                RankChange20d = 1,
                DiffusionRate = 68m,
                AdvancerCount = 15,
                DeclinerCount = 8,
                FlatMemberCount = 3,
                LimitUpMemberCount = 1,
                LeaderStabilityScore = 55m,
                MainlineScore = 61m,
                IsMainline = false,
                NewsSentiment = "中性",
                SourceTag = "test",
                CreatedAt = DateTime.UtcNow
            });
        await dbContext.SaveChangesAsync();

        var service = new SectorRotationQueryService(dbContext, Options.Create(new SectorRotationOptions()));

        var trend = await service.GetSectorTrendAsync("BK001", SectorBoardTypes.Concept, "10d");
        var mainline = await service.GetMainlineAsync(SectorBoardTypes.Concept, "10d", 5);

        Assert.NotNull(trend);
        Assert.Equal(2, trend!.Points.Count);
        Assert.Equal(81m, trend.Points[^1].DiffusionRate);
        Assert.Equal(79m, trend.Points[^1].MainlineScore);
        Assert.Single(mainline, item => item.SectorCode == "BK001" && item.IsMainline);
        Assert.Equal("BK001", mainline[0].SectorCode);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static AppDbContext CreateSqliteDbContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        var dbContext = new AppDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }
}
