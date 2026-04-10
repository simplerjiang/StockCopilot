using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;
using SimplerJiangAiAgent.Api.Infrastructure.Logging;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services.Recommend;

public interface IRecommendationRunner
{
    Task RunTurnAsync(long turnId, CancellationToken ct = default);
    Task RunPartialTurnAsync(long turnId, int fromStageIndex, CancellationToken ct = default);
    Task<string> GenerateDirectAnswerAsync(long sessionId, string userMessage, CancellationToken ct = default);
}

public sealed class RecommendationRunner : IRecommendationRunner
{
    private readonly AppDbContext _db;
    private readonly IRecommendationRoleExecutor _roleExecutor;
    private readonly IRecommendEventBus _eventBus;
    private readonly ILlmService _llmService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecommendationRunner> _logger;
    private readonly ISessionFileLogger? _sessionLogger;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public RecommendationRunner(
        AppDbContext db,
        IRecommendationRoleExecutor roleExecutor,
        IRecommendEventBus eventBus,
        ILlmService llmService,
        IServiceScopeFactory scopeFactory,
        ILogger<RecommendationRunner> logger)
        : this(db, roleExecutor, eventBus, llmService, scopeFactory, logger, null)
    {
    }

    public RecommendationRunner(
        AppDbContext db,
        IRecommendationRoleExecutor roleExecutor,
        IRecommendEventBus eventBus,
        ILlmService llmService,
        IServiceScopeFactory scopeFactory,
        ILogger<RecommendationRunner> logger,
        ISessionFileLogger? sessionLogger)
    {
        _db = db;
        _roleExecutor = roleExecutor;
        _eventBus = eventBus;
        _llmService = llmService;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _sessionLogger = sessionLogger;
    }

    public async Task RunTurnAsync(long turnId, CancellationToken ct = default)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var effectiveCt = linkedCts.Token;

        var turn = await _db.RecommendationTurns
            .Include(t => t.Session)
            .FirstOrDefaultAsync(t => t.Id == turnId, effectiveCt);

        if (turn is null)
        {
            _logger.LogWarning("Turn {TurnId} not found", turnId);
            return;
        }

        var session = turn.Session;
        turn.Status = RecommendTurnStatus.Running;
        turn.StartedAt = DateTime.UtcNow;
        session.Status = RecommendSessionStatus.Running;
        session.ActiveTurnId = turnId;
        await _db.SaveChangesAsync(effectiveCt);

        _eventBus.Publish(new RecommendEvent(
            RecommendEventType.TurnStarted, session.Id, turnId, null, null, null, null,
            $"Turn {turn.TurnIndex} 开始执行", null, DateTime.UtcNow));

        _sessionLogger?.LogTurnStart("recommend", session.Id, turn.Id, turn.TurnIndex,
            session.SessionKey, null, turn.UserPrompt ?? "");

        var pipeline = RecommendStageDefinitions.GetPipeline();
        var upstreamArtifacts = new List<StageArtifact>();
        var turnDegraded = false;

        try
        {
            foreach (var pipelineStage in pipeline)
            {
                var upstreamJson = upstreamArtifacts.Count > 0
                    ? JsonSerializer.Serialize(upstreamArtifacts, JsonOpts)
                    : null;

                var (stageSuccess, stageDegraded) = await ExecutePipelineStageAsync(
                    session, turn, pipelineStage, upstreamJson, upstreamArtifacts, effectiveCt);

                if (stageDegraded) turnDegraded = true;

                if (!stageSuccess)
                {
                    // Full stage failure → abort pipeline
                    turn.Status = RecommendTurnStatus.Failed;
                    turn.CompletedAt = DateTime.UtcNow;
                    session.Status = RecommendSessionStatus.Failed;
                    session.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync(effectiveCt);

                    _eventBus.Publish(new RecommendEvent(
                        RecommendEventType.TurnFailed, session.Id, turnId, null, null, null, null,
                        "流水线中止：阶段全部角色失败", null, DateTime.UtcNow));
                    return;
                }
            }

            turn.Status = RecommendTurnStatus.Completed;
            turn.CompletedAt = DateTime.UtcNow;
            session.Status = turnDegraded ? RecommendSessionStatus.Degraded : RecommendSessionStatus.Completed;
            session.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(effectiveCt);

            _eventBus.Publish(new RecommendEvent(
                RecommendEventType.TurnCompleted, session.Id, turnId, null, null, null, null,
                $"Turn {turn.TurnIndex} 执行完成", null, DateTime.UtcNow));
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            turn.Status = RecommendTurnStatus.Failed;
            turn.CompletedAt = DateTime.UtcNow;
            session.Status = RecommendSessionStatus.Failed;
            session.UpdatedAt = DateTime.UtcNow;
            _eventBus.Publish(new RecommendEvent(
                RecommendEventType.SystemNotice, session.Id, turn.Id,
                null, null, null, null,
                "推荐流水线执行超时（8分钟限制），已自动终止。", null, DateTime.UtcNow));
            await _db.SaveChangesAsync(CancellationToken.None);
            _eventBus.MarkTurnTerminal(turn.Id);
        }
        catch (OperationCanceledException)
        {
            turn.Status = RecommendTurnStatus.Cancelled;
            turn.CompletedAt = DateTime.UtcNow;
            session.Status = RecommendSessionStatus.Failed;
            session.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recommendation turn {TurnId} failed unexpectedly", turnId);
            turn.Status = RecommendTurnStatus.Failed;
            turn.CompletedAt = DateTime.UtcNow;
            session.Status = RecommendSessionStatus.Failed;
            session.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);

            _eventBus.Publish(new RecommendEvent(
                RecommendEventType.TurnFailed, session.Id, turnId, null, null, null, null,
                $"Turn 异常终止: {ex.Message}", null, DateTime.UtcNow));
        }
        finally
        {
            _sessionLogger?.LogTurnEnd(session.Id, turn.Id, turn.Status.ToString());
            if (turn.Status is RecommendTurnStatus.Failed or RecommendTurnStatus.Cancelled)
            {
                await FailRunningSnapshotsAsync(turn.Id);
            }
            await PersistFeedItemsAsync(turn.Id, CancellationToken.None);
            if (turn.Status is RecommendTurnStatus.Completed or RecommendTurnStatus.Failed or RecommendTurnStatus.Cancelled)
            {
                _eventBus.MarkTurnTerminal(turn.Id);
            }
        }
    }

    /// <summary>
    /// Run a turn starting from the given pipeline stage index, reusing upstream artifacts from the previous turn.
    /// </summary>
    public async Task RunPartialTurnAsync(long turnId, int fromStageIndex, CancellationToken ct = default)
    {
        var turn = await _db.RecommendationTurns
            .Include(t => t.Session)
            .FirstOrDefaultAsync(t => t.Id == turnId, ct);

        if (turn is null)
        {
            _logger.LogWarning("Turn {TurnId} not found for partial rerun", turnId);
            return;
        }

        var session = turn.Session;
        turn.Status = RecommendTurnStatus.Running;
        turn.StartedAt = DateTime.UtcNow;
        session.Status = RecommendSessionStatus.Running;
        session.ActiveTurnId = turnId;
        await _db.SaveChangesAsync(ct);

        _eventBus.Publish(new RecommendEvent(
            RecommendEventType.TurnStarted, session.Id, turnId, null, null, null, null,
            $"Turn {turn.TurnIndex} 部分重跑（从阶段 {fromStageIndex} 开始）", null, DateTime.UtcNow));

        var pipeline = RecommendStageDefinitions.GetPipeline();
        var upstreamArtifacts = new List<StageArtifact>();
        var turnDegraded = false;

        // Load artifacts from the previous completed turn for stages before fromStageIndex
        var previousTurn = await _db.RecommendationTurns
            .AsNoTracking()
            .Where(t => t.SessionId == session.Id && t.Id != turnId && t.Status == RecommendTurnStatus.Completed)
            .OrderByDescending(t => t.TurnIndex)
            .FirstOrDefaultAsync(ct);

        if (previousTurn is not null)
        {
            var previousSnapshots = await _db.RecommendationStageSnapshots
                .AsNoTracking()
                .Where(ss => ss.TurnId == previousTurn.Id && ss.Status == RecommendStageStatus.Completed)
                .OrderBy(ss => ss.StageRunIndex)
                .ToListAsync(ct);

            foreach (var snap in previousSnapshots)
            {
                if ((int)snap.StageType < fromStageIndex && !string.IsNullOrWhiteSpace(snap.Summary))
                {
                    upstreamArtifacts.Add(new StageArtifact(snap.StageType.ToString(), snap.Summary));
                }
            }
        }

        try
        {
            foreach (var pipelineStage in pipeline)
            {
                if (pipelineStage.StageIndex < fromStageIndex)
                {
                    _eventBus.Publish(new RecommendEvent(
                        RecommendEventType.SystemNotice, session.Id, turnId, null, pipelineStage.Steps[0].StageType.ToString(), null, null,
                        $"阶段 {pipelineStage.StageIndex} 复用上轮结果", null, DateTime.UtcNow));
                    continue;
                }

                var upstreamJson = upstreamArtifacts.Count > 0
                    ? JsonSerializer.Serialize(upstreamArtifacts, JsonOpts)
                    : null;

                var (stageSuccess, stageDegraded) = await ExecutePipelineStageAsync(
                    session, turn, pipelineStage, upstreamJson, upstreamArtifacts, ct);

                if (stageDegraded) turnDegraded = true;

                if (!stageSuccess)
                {
                    turn.Status = RecommendTurnStatus.Failed;
                    turn.CompletedAt = DateTime.UtcNow;
                    session.Status = RecommendSessionStatus.Failed;
                    session.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync(ct);

                    _eventBus.Publish(new RecommendEvent(
                        RecommendEventType.TurnFailed, session.Id, turnId, null, null, null, null,
                        "部分重跑中止：阶段全部角色失败", null, DateTime.UtcNow));
                    return;
                }
            }

            turn.Status = RecommendTurnStatus.Completed;
            turn.CompletedAt = DateTime.UtcNow;
            session.Status = turnDegraded ? RecommendSessionStatus.Degraded : RecommendSessionStatus.Completed;
            session.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            _eventBus.Publish(new RecommendEvent(
                RecommendEventType.TurnCompleted, session.Id, turnId, null, null, null, null,
                $"Turn {turn.TurnIndex} 部分重跑完成", null, DateTime.UtcNow));
        }
        catch (OperationCanceledException)
        {
            turn.Status = RecommendTurnStatus.Cancelled;
            turn.CompletedAt = DateTime.UtcNow;
            session.Status = RecommendSessionStatus.Failed;
            session.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Partial rerun turn {TurnId} failed unexpectedly", turnId);
            turn.Status = RecommendTurnStatus.Failed;
            turn.CompletedAt = DateTime.UtcNow;
            session.Status = RecommendSessionStatus.Failed;
            session.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);

            _eventBus.Publish(new RecommendEvent(
                RecommendEventType.TurnFailed, session.Id, turnId, null, null, null, null,
                $"部分重跑异常终止: {ex.Message}", null, DateTime.UtcNow));
        }
        finally
        {
            if (turn.Status is RecommendTurnStatus.Failed or RecommendTurnStatus.Cancelled)
            {
                await FailRunningSnapshotsAsync(turn.Id);
            }
            await PersistFeedItemsAsync(turn.Id, CancellationToken.None);
            if (turn.Status is RecommendTurnStatus.Completed or RecommendTurnStatus.Failed or RecommendTurnStatus.Cancelled)
            {
                _eventBus.MarkTurnTerminal(turn.Id);
            }
        }
    }

    /// <summary>
    /// Generate a direct answer from existing debate records without running any agents.
    /// </summary>
    public async Task<string> GenerateDirectAnswerAsync(long sessionId, string userMessage, CancellationToken ct = default)
    {
        // Load the latest completed turn's role outputs
        var latestTurn = await _db.RecommendationTurns
            .AsNoTracking()
            .Where(t => t.SessionId == sessionId && t.Status == RecommendTurnStatus.Completed)
            .OrderByDescending(t => t.TurnIndex)
            .FirstOrDefaultAsync(ct);

        var debateContext = "暂无辩论记录。";
        if (latestTurn is not null)
        {
            var roleOutputs = await _db.RecommendationRoleStates
                .AsNoTracking()
                .Where(rs => rs.Stage.TurnId == latestTurn.Id && rs.Status == RecommendRoleStatus.Completed)
                .Select(rs => new { rs.RoleId, rs.OutputContentJson })
                .ToListAsync(ct);

            if (roleOutputs.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var ro in roleOutputs)
                {
                    var truncated = ro.OutputContentJson?.Length > 500
                        ? ro.OutputContentJson[..500] + "..."
                        : ro.OutputContentJson ?? "";
                    sb.AppendLine($"[{ro.RoleId}]: {truncated}");
                }
                debateContext = sb.ToString();
            }
        }

        var prompt = $"""
            你是推荐系统的助手。用户在一次完整的推荐分析后提出了追问。
            请基于以下辩论记录，直接回答用户的问题。用中文回答，简洁专业。

            ## 辩论记录摘要
            {debateContext}

            ## 用户追问
            {userMessage}
            """;

        var result = await _llmService.ChatAsync("active", new LlmChatRequest(prompt, null, 0.3), ct);
        return result.Content ?? "无法生成回答。";
    }

    private async Task FailRunningSnapshotsAsync(long turnId)
    {
        try
        {
            var runningSnapshots = await _db.RecommendationStageSnapshots
                .Where(ss => ss.TurnId == turnId && ss.Status == RecommendStageStatus.Running)
                .ToListAsync(CancellationToken.None);

            if (runningSnapshots.Count == 0) return;

            foreach (var snapshot in runningSnapshots)
            {
                snapshot.Status = RecommendStageStatus.Failed;
                snapshot.CompletedAt ??= DateTime.UtcNow;
            }
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogInformation("Marked {Count} running stage snapshots as Failed for turn {TurnId}",
                runningSnapshots.Count, turnId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to mark running snapshots as Failed for turn {TurnId}", turnId);
        }
    }

    /// <summary>
    /// Execute all steps in a pipeline stage. Returns (success, degraded).
    /// success=false only if the entire stage failed.
    /// </summary>
    private async Task<(bool Success, bool Degraded)> ExecutePipelineStageAsync(
        RecommendationSession session, RecommendationTurn turn,
        RecommendPipelineStage pipelineStage, string? upstreamJson,
        List<StageArtifact> upstreamArtifacts, CancellationToken ct)
    {
        // A pipeline stage may contain multiple steps (e.g. Parallel then Sequential)
        // All steps share the same StageType; we create one snapshot per step.
        string? stepCarryOver = upstreamJson;
        var anyDegraded = false;

        foreach (var step in pipelineStage.Steps)
        {
            var snapshot = new RecommendationStageSnapshot
            {
                TurnId = turn.Id,
                StageType = step.StageType,
                StageRunIndex = pipelineStage.StageIndex,
                ExecutionMode = step.ExecutionMode,
                Status = RecommendStageStatus.Running,
                ActiveRoleIdsJson = JsonSerializer.Serialize(step.RoleIds, JsonOpts),
                StartedAt = DateTime.UtcNow
            };
            _db.RecommendationStageSnapshots.Add(snapshot);
            await _db.SaveChangesAsync(ct);

            _eventBus.Publish(new RecommendEvent(
                RecommendEventType.StageStarted, session.Id, turn.Id, snapshot.Id, step.StageType.ToString(), null, null,
                $"阶段 {step.StageType} ({step.ExecutionMode}) 开始", null, DateTime.UtcNow));

            List<RecommendRoleExecutionResult> results;
            try
            {
                results = step.ExecutionMode switch
                {
                    RecommendStageExecutionMode.Parallel =>
                        await ExecuteParallelAsync(session.Id, turn, snapshot, step, stepCarryOver, ct),
                    RecommendStageExecutionMode.Sequential =>
                        await ExecuteSequentialAsync(session.Id, turn, snapshot, step, stepCarryOver, ct),
                    RecommendStageExecutionMode.Debate =>
                        await ExecuteDebateAsync(session.Id, turn, snapshot, step, stepCarryOver, ct),
                    _ => throw new InvalidOperationException($"Unsupported execution mode: {step.ExecutionMode}")
                };
            }
            catch (OperationCanceledException)
            {
                snapshot.Status = RecommendStageStatus.Failed;
                snapshot.CompletedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(CancellationToken.None);
                throw;
            }
            catch (Exception)
            {
                snapshot.Status = RecommendStageStatus.Failed;
                snapshot.CompletedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(CancellationToken.None);
                throw;
            }

            // Evaluate step result
            var successResults = results.Where(r => r.Success).ToList();
            var allFailed = successResults.Count == 0;

            if (allFailed)
            {
                snapshot.Status = RecommendStageStatus.Failed;
                snapshot.CompletedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);

                _eventBus.Publish(new RecommendEvent(
                    RecommendEventType.StageFailed, session.Id, turn.Id, snapshot.Id, step.StageType.ToString(), null, null,
                    $"阶段 {step.StageType} 全部角色失败", null, DateTime.UtcNow));
                return (false, false);
            }

            var hasDegraded = results.Any(r => !r.Success);
            snapshot.Status = hasDegraded ? RecommendStageStatus.Degraded : RecommendStageStatus.Completed;
            snapshot.Summary = BuildStageSummary(successResults);
            snapshot.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            // Carry forward for next step within same pipeline stage
            stepCarryOver = snapshot.Summary;

            if (hasDegraded) anyDegraded = true;

            _eventBus.Publish(new RecommendEvent(
                RecommendEventType.StageCompleted, session.Id, turn.Id, snapshot.Id, step.StageType.ToString(), null, null,
                $"阶段 {step.StageType} ({step.ExecutionMode}) 完成", null, DateTime.UtcNow));

            if (hasDegraded)
            {
                _eventBus.Publish(new RecommendEvent(
                    RecommendEventType.DegradedNotice, session.Id, turn.Id, snapshot.Id, step.StageType.ToString(), null, null,
                    $"阶段 {step.StageType} 部分角色降级", null, DateTime.UtcNow));
            }
        }

        // After all steps, add final step's summary to upstream artifacts for next pipeline stage
        if (stepCarryOver is not null)
        {
            var stageType = pipelineStage.Steps[^1].StageType;
            upstreamArtifacts.Add(new StageArtifact(stageType.ToString(), stepCarryOver));
        }

        return (true, anyDegraded);
    }

    private async Task<List<RecommendRoleExecutionResult>> ExecuteParallelAsync(
        long sessionId, RecommendationTurn turn, RecommendationStageSnapshot snapshot,
        RecommendStageStep step, string? upstreamJson, CancellationToken ct)
    {
        var tasks = step.RoleIds.Select(async roleId =>
        {
            var roleState = await CreateRoleStateAsync(snapshot.Id, roleId, upstreamJson, ct);
            var context = new RecommendRoleExecutionContext(
                roleId, $"你是推荐系统的 {roleId} 角色。请按角色职责输出分析结果。",
                turn.UserPrompt, upstreamJson, sessionId, turn.Id, snapshot.Id, snapshot.StageType.ToString());

            // Each parallel task gets its own scope to avoid DbContext concurrency issues
            using var scope = _scopeFactory.CreateScope();
            var scopedExecutor = scope.ServiceProvider.GetRequiredService<IRecommendationRoleExecutor>();
            var result = await scopedExecutor.ExecuteAsync(context, ct);

            await UpdateRoleStateAsync(roleState, result, ct);
            return result;
        });

        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    private async Task<List<RecommendRoleExecutionResult>> ExecuteSequentialAsync(
        long sessionId, RecommendationTurn turn, RecommendationStageSnapshot snapshot,
        RecommendStageStep step, string? upstreamJson, CancellationToken ct)
    {
        var results = new List<RecommendRoleExecutionResult>();
        var currentInput = upstreamJson;

        foreach (var roleId in step.RoleIds)
        {
            var roleState = await CreateRoleStateAsync(snapshot.Id, roleId, currentInput, ct);
            var context = new RecommendRoleExecutionContext(
                roleId, $"你是推荐系统的 {roleId} 角色。请按角色职责输出分析结果。",
                turn.UserPrompt, currentInput, sessionId, turn.Id, snapshot.Id, snapshot.StageType.ToString());
            var result = await _roleExecutor.ExecuteAsync(context, ct);
            await UpdateRoleStateAsync(roleState, result, ct);
            results.Add(result);

            // Chain: previous output becomes next input
            if (result.Success && result.OutputJson is not null)
                currentInput = result.OutputJson;
        }

        return results;
    }

    private async Task<List<RecommendRoleExecutionResult>> ExecuteDebateAsync(
        long sessionId, RecommendationTurn turn, RecommendationStageSnapshot snapshot,
        RecommendStageStep step, string? upstreamJson, CancellationToken ct)
    {
        var results = new List<RecommendRoleExecutionResult>();
        var maxRounds = step.MaxDebateRounds > 0 ? step.MaxDebateRounds : 3;
        var debateHistory = new List<string>();

        // Debate roles: alternating speakers. The last role (if 3) is the judge.
        var hasJudge = step.RoleIds.Count >= 3;
        var debaters = hasJudge ? step.RoleIds.Take(step.RoleIds.Count - 1).ToList() : step.RoleIds.ToList();
        var judgeRoleId = hasJudge ? step.RoleIds[^1] : null;

        for (int round = 0; round < maxRounds; round++)
        {
            foreach (var roleId in debaters)
            {
                var debateContext = upstreamJson;
                if (debateHistory.Count > 0)
                {
                    debateContext = (upstreamJson ?? "") + "\n\n## 辩论历史\n" + string.Join("\n---\n", debateHistory);
                }

                var roleState = await CreateRoleStateAsync(snapshot.Id, roleId, debateContext, ct);
                var context = new RecommendRoleExecutionContext(
                    roleId, $"你是推荐系统的 {roleId} 角色。这是第 {round + 1} 轮辩论，请提出你的观点。",
                    turn.UserPrompt, debateContext, sessionId, turn.Id, snapshot.Id, snapshot.StageType.ToString());

                RecommendRoleExecutionResult result;
                using (var scope = _scopeFactory.CreateScope())
                {
                    var scopedExecutor = scope.ServiceProvider.GetRequiredService<IRecommendationRoleExecutor>();
                    result = await scopedExecutor.ExecuteAsync(context, ct);
                }

                await UpdateRoleStateAsync(roleState, result, ct);
                results.Add(result);

                if (result.Success && result.OutputJson is not null)
                    debateHistory.Add($"[{roleId} 第{round + 1}轮]: {result.OutputJson}");
            }

            // Judge evaluates after each round
            if (judgeRoleId is not null)
            {
                var judgeInput = (upstreamJson ?? "") + "\n\n## 辩论记录\n" + string.Join("\n---\n", debateHistory);
                var judgeState = await CreateRoleStateAsync(snapshot.Id, judgeRoleId, judgeInput, ct);
                var judgeContext = new RecommendRoleExecutionContext(
                    judgeRoleId, $"你是推荐系统的裁判角色。请评估第 {round + 1} 轮辩论并决定是否需要继续辩论。如果已达成共识，在输出中包含 \"CONSENSUS_REACHED\"。",
                    turn.UserPrompt, judgeInput, sessionId, turn.Id, snapshot.Id, snapshot.StageType.ToString());

                RecommendRoleExecutionResult judgeResult;
                using (var scope = _scopeFactory.CreateScope())
                {
                    var scopedExecutor = scope.ServiceProvider.GetRequiredService<IRecommendationRoleExecutor>();
                    judgeResult = await scopedExecutor.ExecuteAsync(judgeContext, ct);
                }

                await UpdateRoleStateAsync(judgeState, judgeResult, ct);
                results.Add(judgeResult);

                // Early termination if judge signals consensus
                if (judgeResult.Success && judgeResult.OutputJson?.Contains("CONSENSUS_REACHED", StringComparison.OrdinalIgnoreCase) == true)
                    break;
            }
        }

        return results;
    }

    private async Task<RecommendationRoleState> CreateRoleStateAsync(
        long stageId, string roleId, string? inputJson, CancellationToken ct)
    {
        // Count existing run indices for this role in this stage
        var runIndex = await _db.RecommendationRoleStates
            .CountAsync(r => r.StageId == stageId && r.RoleId == roleId, ct);

        var state = new RecommendationRoleState
        {
            StageId = stageId,
            RoleId = roleId,
            RunIndex = runIndex,
            Status = RecommendRoleStatus.Running,
            InputRefsJson = inputJson,
            StartedAt = DateTime.UtcNow
        };
        _db.RecommendationRoleStates.Add(state);
        await _db.SaveChangesAsync(ct);
        return state;
    }

    private async Task UpdateRoleStateAsync(
        RecommendationRoleState state, RecommendRoleExecutionResult result, CancellationToken ct)
    {
        state.Status = result.Success ? RecommendRoleStatus.Completed : RecommendRoleStatus.Failed;
        state.OutputContentJson = result.OutputJson;
        state.LlmTraceId = result.LlmTraceId;
        state.ErrorCode = result.ErrorCode;
        state.ErrorMessage = result.ErrorMessage;
        state.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private async Task PersistFeedItemsAsync(long turnId, CancellationToken ct)
    {
        var events = _eventBus.Snapshot(turnId)
            .OrderBy(evt => evt.Timestamp)
            .ToArray();

        var existing = await _db.RecommendationFeedItems
            .Where(item => item.TurnId == turnId)
            .ToListAsync(ct);
        if (existing.Count > 0)
        {
            _db.RecommendationFeedItems.RemoveRange(existing);
        }

        foreach (var evt in events)
        {
            _db.RecommendationFeedItems.Add(new RecommendationFeedItem
            {
                TurnId = evt.TurnId,
                StageId = evt.StageId,
                RoleId = evt.RoleId,
                ItemType = MapEventToFeedType(evt.EventType),
                Content = evt.Summary,
                MetadataJson = JsonSerializer.Serialize(new
                {
                    eventType = evt.EventType.ToString(),
                    stageType = evt.StageType,
                    detailJson = evt.DetailJson
                }, JsonOpts),
                TraceId = evt.TraceId,
                CreatedAt = evt.Timestamp
            });
        }

        if (events.Length > 0 || existing.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
        }
    }

    private static RecommendFeedItemType MapEventToFeedType(RecommendEventType eventType) => eventType switch
    {
        RecommendEventType.TurnStarted => RecommendFeedItemType.UserFollowUp,
        RecommendEventType.RoleStarted or RecommendEventType.RoleSummaryReady or
        RecommendEventType.RoleCompleted or RecommendEventType.RoleFailed => RecommendFeedItemType.RoleMessage,
        RecommendEventType.ToolDispatched or RecommendEventType.ToolCompleted => RecommendFeedItemType.ToolEvent,
        RecommendEventType.StageStarted or RecommendEventType.StageCompleted or RecommendEventType.StageFailed => RecommendFeedItemType.StageTransition,
        RecommendEventType.DegradedNotice => RecommendFeedItemType.DegradedNotice,
        RecommendEventType.TurnFailed => RecommendFeedItemType.ErrorNotice,
        _ => RecommendFeedItemType.SystemNotice
    };

    private static string BuildStageSummary(List<RecommendRoleExecutionResult> successResults)
    {
        var parts = successResults
            .Where(r => r.OutputJson is not null)
            .Select(r => $"[{r.RoleId}]: {r.OutputJson}");
        return string.Join("\n\n", parts);
    }

    private sealed record StageArtifact(string Stage, string Content);
}
