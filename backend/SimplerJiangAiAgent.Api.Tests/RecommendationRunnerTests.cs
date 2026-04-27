using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services.Recommend;
using Xunit;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class RecommendationRunnerTests
{
    [Fact]
    public async Task RunTurnAsync_AssignsStageRunIndexesUniqueAndInExecutionOrder()
    {
        await using var db = CreateDbContext();
        using var eventBus = new RecommendEventBus();
        using var provider = CreateServiceProvider();
        var runner = CreateRunner(db, eventBus, provider);
        var (_, turn) = await CreateSessionAndTurnAsync(db, RecommendContinuationMode.NewSession);

        await runner.RunTurnAsync(turn.Id);

        var snapshots = await db.RecommendationStageSnapshots
            .Where(snapshot => snapshot.TurnId == turn.Id)
            .OrderBy(snapshot => snapshot.StageRunIndex)
            .ToArrayAsync();

        var indexes = snapshots.Select(snapshot => snapshot.StageRunIndex).ToArray();
        Assert.Equal(Enumerable.Range(0, snapshots.Length).ToArray(), indexes);
        Assert.Equal(indexes.Length, indexes.Distinct().Count());

        var stockPickingIndexes = snapshots
            .Where(snapshot => snapshot.StageType == RecommendStageType.StockPicking)
            .Select(snapshot => snapshot.StageRunIndex)
            .ToArray();
        Assert.Equal([2, 3], stockPickingIndexes);

        var stockDebateIndexes = snapshots
            .Where(snapshot => snapshot.StageType == RecommendStageType.StockDebate)
            .Select(snapshot => snapshot.StageRunIndex)
            .ToArray();
        Assert.Equal([4, 5], stockDebateIndexes);
    }

    [Fact]
    public async Task RunPartialTurnAsync_ContinuesAfterExistingRunIndexesWithoutDuplicates()
    {
        await using var db = CreateDbContext();
        using var eventBus = new RecommendEventBus();
        using var provider = CreateServiceProvider();
        var runner = CreateRunner(db, eventBus, provider);
        var (session, previousTurn) = await CreateSessionAndTurnAsync(db, RecommendContinuationMode.NewSession);
        previousTurn.Status = RecommendTurnStatus.Completed;
        db.RecommendationStageSnapshots.AddRange(
            CreateCompletedSnapshot(previousTurn.Id, RecommendStageType.MarketScan, 0),
            CreateCompletedSnapshot(previousTurn.Id, RecommendStageType.SectorDebate, 1));

        var partialTurn = new RecommendationTurn
        {
            SessionId = session.Id,
            TurnIndex = 1,
            UserPrompt = "从选股阶段继续",
            Status = RecommendTurnStatus.Queued,
            ContinuationMode = RecommendContinuationMode.PartialRerun,
            RequestedAt = DateTime.UtcNow
        };
        db.RecommendationTurns.Add(partialTurn);
        await db.SaveChangesAsync();

        db.RecommendationStageSnapshots.AddRange(
            CreateCompletedSnapshot(partialTurn.Id, RecommendStageType.MarketScan, 0),
            CreateCompletedSnapshot(partialTurn.Id, RecommendStageType.SectorDebate, 1));
        await db.SaveChangesAsync();

        await runner.RunPartialTurnAsync(partialTurn.Id, fromStageIndex: 2);

        var indexes = await db.RecommendationStageSnapshots
            .Where(snapshot => snapshot.TurnId == partialTurn.Id)
            .OrderBy(snapshot => snapshot.StageRunIndex)
            .Select(snapshot => snapshot.StageRunIndex)
            .ToArrayAsync();

        Assert.Equal(Enumerable.Range(0, indexes.Length).ToArray(), indexes);
        Assert.Equal(indexes.Length, indexes.Distinct().Count());
    }

    private static RecommendationRunner CreateRunner(
        AppDbContext db,
        IRecommendEventBus eventBus,
        ServiceProvider provider)
    {
        var executor = provider.GetRequiredService<IRecommendationRoleExecutor>();
        return new RecommendationRunner(
            db,
            executor,
            eventBus,
            new FakeLlmService(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RecommendationRunner>.Instance);
    }

    private static ServiceProvider CreateServiceProvider() => new ServiceCollection()
        .AddSingleton<IRecommendationRoleExecutor, SuccessfulRoleExecutor>()
        .BuildServiceProvider();

    private static async Task<(RecommendationSession Session, RecommendationTurn Turn)> CreateSessionAndTurnAsync(
        AppDbContext db,
        RecommendContinuationMode continuationMode)
    {
        var session = new RecommendationSession
        {
            SessionKey = Guid.NewGuid().ToString("N"),
            Status = RecommendSessionStatus.Idle,
            LastUserIntent = "推荐测试",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.RecommendationSessions.Add(session);
        await db.SaveChangesAsync();

        var turn = new RecommendationTurn
        {
            SessionId = session.Id,
            TurnIndex = 0,
            UserPrompt = "推荐测试",
            Status = RecommendTurnStatus.Queued,
            ContinuationMode = continuationMode,
            RequestedAt = DateTime.UtcNow
        };
        db.RecommendationTurns.Add(turn);
        await db.SaveChangesAsync();

        session.ActiveTurnId = turn.Id;
        await db.SaveChangesAsync();
        return (session, turn);
    }

    private static RecommendationStageSnapshot CreateCompletedSnapshot(
        long turnId,
        RecommendStageType stageType,
        int runIndex) => new()
    {
        TurnId = turnId,
        StageType = stageType,
        StageRunIndex = runIndex,
        ExecutionMode = RecommendStageExecutionMode.Sequential,
        Status = RecommendStageStatus.Completed,
        Summary = stageType.ToString(),
        StartedAt = DateTime.UtcNow,
        CompletedAt = DateTime.UtcNow
    };

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options);
    }

    private sealed class SuccessfulRoleExecutor : IRecommendationRoleExecutor
    {
        public Task<RecommendRoleExecutionResult> ExecuteAsync(
            RecommendRoleExecutionContext context,
            CancellationToken ct = default) => Task.FromResult(new RecommendRoleExecutionResult(
                context.RoleId,
                true,
                $"{{\"roleId\":\"{context.RoleId}\"}}",
                null,
                null,
                $"trace-{context.RoleId}",
                0));
    }

    private sealed class FakeLlmService : ILlmService
    {
        public Task<LlmChatResult> ChatAsync(
            string provider,
            LlmChatRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(new LlmChatResult("{}", "trace-direct"));
    }
}