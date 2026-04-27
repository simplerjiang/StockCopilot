using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services.Recommend;
using Xunit;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class RecommendationSessionServiceTests
{
    [Fact]
    public void RecommendEventBus_SnapshotSince_UsesMonotonicSequenceCursor()
    {
        using var eventBus = new RecommendEventBus();

        eventBus.Publish(CreateEvent(7, RecommendEventType.StageStarted, "阶段开始"));
        eventBus.Publish(CreateEvent(7, RecommendEventType.RoleStarted, "角色开始"));

        var initial = eventBus.SnapshotSince(7, 0);

        Assert.Equal(2, initial.Count);
        Assert.True(initial[0].Sequence > 0);
        Assert.True(initial[1].Sequence > initial[0].Sequence);
        Assert.Equal(RecommendEventType.StageStarted, initial[0].Event.EventType);
        Assert.Equal(RecommendEventType.RoleStarted, initial[1].Event.EventType);

        eventBus.Publish(CreateEvent(7, RecommendEventType.RoleCompleted, "角色完成"));

        var resumed = eventBus.SnapshotSince(7, initial[1].Sequence);

        var resumedEvent = Assert.Single(resumed);
        Assert.True(resumedEvent.Sequence > initial[1].Sequence);
        Assert.Equal(RecommendEventType.RoleCompleted, resumedEvent.Event.EventType);
    }

    [Fact]
    public void RecommendEventBus_SnapshotHistory_SurvivesDrainUntilCleanup()
    {
        using var eventBus = new RecommendEventBus();

        eventBus.Publish(CreateEvent(8, RecommendEventType.StageStarted, "阶段开始"));
        eventBus.Publish(CreateEvent(8, RecommendEventType.TurnCompleted, "回合完成"));

        var drained = eventBus.Drain(8);
        var snapshot = eventBus.Snapshot(8);
        var resumable = eventBus.SnapshotSince(8, 0);

        Assert.Equal(2, drained.Count);
        Assert.Empty(eventBus.Drain(8));
        Assert.Equal(2, snapshot.Count);
        Assert.Equal(2, resumable.Count);

        eventBus.Cleanup(8);

        Assert.Empty(eventBus.Snapshot(8));
        Assert.Empty(eventBus.SnapshotSince(8, 0));
    }

    [Fact]
    public async Task RecommendEventBus_MarkTurnTerminal_CleansUpAfterRetentionWindow()
    {
        using var eventBus = new RecommendEventBus();

        eventBus.Publish(CreateEvent(9, RecommendEventType.TurnCompleted, "回合完成"));
        eventBus.MarkTurnTerminal(9, TimeSpan.Zero);

        for (var attempt = 0; attempt < 20 && eventBus.Snapshot(9).Count > 0; attempt++)
        {
            await Task.Delay(10);
        }

        Assert.Empty(eventBus.Snapshot(9));
    }

    [Fact]
    public async Task SubmitFollowUpAsync_SetsActiveTurnId_ToSavedTurnId()
    {
        await using var db = CreateDbContext();
        var eventBus = new RecommendEventBus();
        var router = new StaticFollowUpRouter(new FollowUpPlan(
            "全量重跑",
            FollowUpStrategy.FullRerun,
            [],
            null,
            "测试策略",
            null,
            0.8m));
        var service = CreateService(db, eventBus, router);

        var session = await service.CreateSessionAsync("先来一轮推荐");

        var (turn, _) = await service.SubmitFollowUpAsync(session.Id, "换个方向再看一次");
        var storedSession = await db.RecommendationSessions.AsNoTracking().FirstAsync(item => item.Id == session.Id);

        Assert.True(turn.Id > 0);
        Assert.Equal(turn.Id, storedSession.ActiveTurnId);
        Assert.Equal(1, turn.TurnIndex);
    }

    [Fact]
    public async Task GetSessionDetailAsync_MergesPersistedAndLiveFeedItems()
    {
        await using var db = CreateDbContext();
        var eventBus = new RecommendEventBus();
        var service = CreateService(db, eventBus, new StaticFollowUpRouter(new FollowUpPlan(
            "全量重跑",
            FollowUpStrategy.FullRerun,
            [],
            null,
            "测试策略",
            null,
            0.8m)));

        var session = await service.CreateSessionAsync("推荐半导体方向");
        var turn = await db.RecommendationTurns.FirstAsync(item => item.SessionId == session.Id);

        db.RecommendationFeedItems.Add(new RecommendationFeedItem
        {
            TurnId = turn.Id,
            ItemType = RecommendFeedItemType.RoleMessage,
            RoleId = RecommendAgentRoleIds.MacroAnalyst,
            Content = "{\"summary\":\"偏强\"}",
            MetadataJson = JsonSerializer.Serialize(new
            {
                eventType = "RoleSummaryReady",
                stageType = "MarketScan",
                detailJson = "{\"summary\":\"偏强\"}"
            }),
            TraceId = "trace-persisted",
            CreatedAt = DateTime.UtcNow.AddSeconds(-5)
        });

        session.Status = RecommendSessionStatus.Running;
        turn.Status = RecommendTurnStatus.Running;
        session.ActiveTurnId = turn.Id;
        await db.SaveChangesAsync();

        eventBus.Publish(new RecommendEvent(
            RecommendEventType.ToolCompleted,
            session.Id,
            turn.Id,
            null,
            "MarketScan",
            RecommendAgentRoleIds.MacroAnalyst,
            "trace-live",
            "工具 web_search 返回完成",
            "{\"toolName\":\"web_search\"}",
            DateTime.UtcNow));

        var detail = await service.GetSessionDetailAsync(session.Id);

        Assert.NotNull(detail);
        var detailTurn = Assert.Single(detail!.Turns);
        Assert.Equal(2, detailTurn.FeedItems.Count);

        var persisted = Assert.Single(detailTurn.FeedItems.Where(item => item.TraceId == "trace-persisted"));
        Assert.Equal("RoleSummaryReady", persisted.EventType);
        Assert.Equal("MarketScan", persisted.StageType);

        var live = Assert.Single(detailTurn.FeedItems.Where(item => item.TraceId == "trace-live"));
        Assert.Equal("ToolCompleted", live.EventType);
        Assert.Equal("MarketScan", live.StageType);
        Assert.Equal(2, detail.FeedItems.Count);
    }

    [Fact]
    public async Task GetSessionDetailAsync_NormalizesDuplicateStageRunIndexesForRead()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, new RecommendEventBus(), new StaticFollowUpRouter(new FollowUpPlan(
            "全量重跑",
            FollowUpStrategy.FullRerun,
            [],
            null,
            "测试策略",
            null,
            0.8m)));

        var session = await service.CreateSessionAsync("推荐新能源方向");
        var turn = await db.RecommendationTurns.FirstAsync(item => item.SessionId == session.Id);

        db.RecommendationStageSnapshots.AddRange(
            CreateSnapshot(turn.Id, RecommendStageType.MarketScan, 0, 1),
            CreateSnapshot(turn.Id, RecommendStageType.StockPicking, 2, 2),
            CreateSnapshot(turn.Id, RecommendStageType.StockPicking, 2, 3),
            CreateSnapshot(turn.Id, RecommendStageType.StockDebate, 3, 4));
        await db.SaveChangesAsync();

        var detail = await service.GetSessionDetailAsync(session.Id);

        Assert.NotNull(detail);
        var detailTurn = Assert.Single(detail!.Turns);
        Assert.Equal([0, 2, 3, 4], detailTurn.StageSnapshots.Select(snapshot => snapshot.StageRunIndex).ToArray());
        Assert.Equal(detailTurn.StageSnapshots.Count, detailTurn.StageSnapshots.Select(snapshot => snapshot.StageRunIndex).Distinct().Count());
    }

    [Fact]
    public async Task RecommendZombieCleanupWorker_RepairsHistoricalDuplicateStageRunIndexes()
    {
        await using var db = CreateDbContext();

        var session = new RecommendationSession
        {
            SessionKey = Guid.NewGuid().ToString("N"),
            Status = RecommendSessionStatus.Completed,
            LastUserIntent = "历史重复样本",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.RecommendationSessions.Add(session);
        await db.SaveChangesAsync();

        var turn = new RecommendationTurn
        {
            SessionId = session.Id,
            TurnIndex = 0,
            UserPrompt = "历史重复样本",
            Status = RecommendTurnStatus.Completed,
            ContinuationMode = RecommendContinuationMode.NewSession,
            RequestedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };
        db.RecommendationTurns.Add(turn);
        await db.SaveChangesAsync();

        db.RecommendationStageSnapshots.AddRange(
            CreateSnapshot(turn.Id, RecommendStageType.MarketScan, 0, 1),
            CreateSnapshot(turn.Id, RecommendStageType.StockPicking, 2, 2),
            CreateSnapshot(turn.Id, RecommendStageType.StockPicking, 2, 3),
            CreateSnapshot(turn.Id, RecommendStageType.StockDebate, 3, 4));
        await db.SaveChangesAsync();

        var repaired = await RecommendZombieCleanupWorker.RepairDuplicateStageRunIndexesAsync(
            db,
            NullLogger<RecommendZombieCleanupWorker>.Instance,
            CancellationToken.None);

        var indexes = await db.RecommendationStageSnapshots
            .Where(snapshot => snapshot.TurnId == turn.Id)
            .OrderBy(snapshot => snapshot.StageRunIndex)
            .Select(snapshot => snapshot.StageRunIndex)
            .ToArrayAsync();

        Assert.True(repaired > 0);
        Assert.Equal([0, 1, 2, 3], indexes);
    }

    [Fact]
    public async Task RecommendSessionSchemaInitializer_RepairsDirtySqliteDatabaseAndCreatesUniqueIndex()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateSqliteDbContext(connection);

        await db.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE RecommendationStageSnapshots (
                Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                TurnId              INTEGER NOT NULL,
                StageType           TEXT    NOT NULL,
                StageRunIndex       INTEGER NOT NULL,
                ExecutionMode       TEXT    NOT NULL,
                Status              TEXT    NOT NULL,
                ActiveRoleIdsJson   TEXT    NULL,
                Summary             TEXT    NULL,
                StartedAt           TEXT    NULL,
                CompletedAt         TEXT    NULL
            );

            CREATE TABLE RecommendationRoleStates (
                Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                StageId             INTEGER NOT NULL,
                RoleId              TEXT    NOT NULL,
                RunIndex            INTEGER NOT NULL,
                Status              TEXT    NOT NULL,
                ToolPolicyClass     TEXT    NULL,
                InputRefsJson       TEXT    NULL,
                OutputRefsJson      TEXT    NULL,
                OutputContentJson   TEXT    NULL,
                ErrorCode           TEXT    NULL,
                ErrorMessage        TEXT    NULL,
                LlmTraceId          TEXT    NULL,
                StartedAt           TEXT    NULL,
                CompletedAt         TEXT    NULL
            );

            INSERT INTO RecommendationStageSnapshots
                (Id, TurnId, StageType, StageRunIndex, ExecutionMode, Status, StartedAt, CompletedAt)
            VALUES
                (10, 42, 'MarketScan', 0, 'Sequential', 'Completed', '2026-04-01T00:00:00Z', '2026-04-01T00:01:00Z'),
                (11, 42, 'StockPicking', 2, 'Parallel', 'Completed', '2026-04-01T00:02:00Z', '2026-04-01T00:03:00Z'),
                (12, 42, 'StockPicking', 2, 'Sequential', 'Completed', '2026-04-01T00:04:00Z', '2026-04-01T00:05:00Z'),
                (13, 42, 'StockDebate', 3, 'Debate', 'Completed', '2026-04-01T00:06:00Z', '2026-04-01T00:07:00Z');

            INSERT INTO RecommendationRoleStates
                (Id, StageId, RoleId, RunIndex, Status)
            VALUES
                (100, 12, 'ChartValidator', 0, 'Completed');
        ");

        await RecommendSessionSchemaInitializer.EnsureAsync(db);

        var indexes = await db.RecommendationStageSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.TurnId == 42)
            .OrderBy(snapshot => snapshot.StageRunIndex)
            .Select(snapshot => new { snapshot.Id, snapshot.StageRunIndex })
            .ToArrayAsync();
        Assert.Equal(
            [new { Id = 10L, StageRunIndex = 0 }, new { Id = 11L, StageRunIndex = 1 }, new { Id = 12L, StageRunIndex = 2 }, new { Id = 13L, StageRunIndex = 3 }],
            indexes);

        var duplicateGroups = await db.RecommendationStageSnapshots
            .AsNoTracking()
            .GroupBy(snapshot => new { snapshot.TurnId, snapshot.StageRunIndex })
            .CountAsync(group => group.Count() > 1);
        Assert.Equal(0, duplicateGroups);

        var roleStageId = await db.RecommendationRoleStates
            .AsNoTracking()
            .Where(role => role.Id == 100)
            .Select(role => role.StageId)
            .SingleAsync();
        Assert.Equal(12, roleStageId);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT [unique] FROM pragma_index_list('RecommendationStageSnapshots') WHERE name = 'UX_RecommendationStageSnapshots_TurnId_StageRunIndex';";
        var unique = Assert.IsType<long>(await command.ExecuteScalarAsync());
        Assert.Equal(1L, unique);
    }

    private static RecommendationSessionService CreateService(AppDbContext db, IRecommendEventBus eventBus, IRecommendFollowUpRouter router) =>
        new(db, eventBus, router, NullLogger<RecommendationSessionService>.Instance);

    private static RecommendEvent CreateEvent(long turnId, RecommendEventType eventType, string summary) =>
        new(eventType, 1, turnId, null, "MarketScan", RecommendAgentRoleIds.MacroAnalyst, null, summary, null, DateTime.UtcNow);

    private static RecommendationStageSnapshot CreateSnapshot(
        long turnId,
        RecommendStageType stageType,
        int runIndex,
        int minuteOffset) => new()
    {
        TurnId = turnId,
        StageType = stageType,
        StageRunIndex = runIndex,
        ExecutionMode = RecommendStageExecutionMode.Sequential,
        Status = RecommendStageStatus.Completed,
        Summary = stageType.ToString(),
        StartedAt = DateTime.UtcNow.AddMinutes(minuteOffset),
        CompletedAt = DateTime.UtcNow.AddMinutes(minuteOffset + 1)
    };

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options);
    }

    private static AppDbContext CreateSqliteDbContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        return new AppDbContext(options);
    }

    private sealed class StaticFollowUpRouter : IRecommendFollowUpRouter
    {
        private readonly FollowUpPlan _plan;

        public StaticFollowUpRouter(FollowUpPlan plan) => _plan = plan;

        public Task<FollowUpPlan> RouteAsync(long sessionId, string userMessage, CancellationToken ct = default) =>
            Task.FromResult(_plan);
    }
}