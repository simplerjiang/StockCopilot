using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;
using SimplerJiangAiAgent.Api.Infrastructure.Logging;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface IResearchRunner
{
    Task RunTurnAsync(long turnId, CancellationToken cancellationToken = default);
}

public sealed class ResearchRunner : IResearchRunner
{
    private const int MaxDebateRounds = 3;

    private static readonly IReadOnlyList<string> FullAnalystTeamRoleIds =
    [
        StockAgentRoleIds.MarketAnalyst,
        StockAgentRoleIds.SocialSentimentAnalyst,
        StockAgentRoleIds.NewsAnalyst,
        StockAgentRoleIds.FundamentalsAnalyst,
        StockAgentRoleIds.ShareholderAnalyst,
        StockAgentRoleIds.ProductAnalyst
    ];

    private static readonly IReadOnlyList<StageDefinition> Pipeline =
    [
        new(ResearchStageType.CompanyOverviewPreflight, ResearchStageExecutionMode.Sequential,
            [StockAgentRoleIds.CompanyOverviewAnalyst]),
        new(ResearchStageType.AnalystTeam, ResearchStageExecutionMode.Parallel,
            FullAnalystTeamRoleIds),
        new(ResearchStageType.ResearchDebate, ResearchStageExecutionMode.Debate,
            [StockAgentRoleIds.BullResearcher, StockAgentRoleIds.BearResearcher,
             StockAgentRoleIds.ResearchManager]),
        new(ResearchStageType.TraderProposal, ResearchStageExecutionMode.Sequential,
            [StockAgentRoleIds.Trader]),
        new(ResearchStageType.RiskDebate, ResearchStageExecutionMode.Debate,
            [StockAgentRoleIds.AggressiveRiskAnalyst, StockAgentRoleIds.NeutralRiskAnalyst,
             StockAgentRoleIds.ConservativeRiskAnalyst]),
        new(ResearchStageType.PortfolioDecision, ResearchStageExecutionMode.Sequential,
            [StockAgentRoleIds.PortfolioManager]),
    ];

    private readonly AppDbContext _dbContext;
    private readonly IResearchRoleExecutor _roleExecutor;
    private readonly IResearchEventBus _eventBus;
    private readonly IResearchReportService _reportService;
    private readonly IResearchFollowUpRoutingService _followUpRoutingService;
    private readonly ILogger<ResearchRunner> _logger;
    private readonly ILlmSettingsStore? _llmSettingsStore;
    private readonly ISessionFileLogger? _sessionLogger;
    private string? _positionContext;
    private string? _resolvedStockName;

    public ResearchRunner(
        AppDbContext dbContext,
        IResearchRoleExecutor roleExecutor,
        IResearchEventBus eventBus,
        IResearchReportService reportService,
        IResearchFollowUpRoutingService followUpRoutingService,
        ILogger<ResearchRunner> logger)
        : this(dbContext, roleExecutor, eventBus, reportService, followUpRoutingService, logger, null, null)
    {
    }

    public ResearchRunner(
        AppDbContext dbContext,
        IResearchRoleExecutor roleExecutor,
        IResearchEventBus eventBus,
        IResearchReportService reportService,
        IResearchFollowUpRoutingService followUpRoutingService,
        ILogger<ResearchRunner> logger,
        ILlmSettingsStore? llmSettingsStore)
        : this(dbContext, roleExecutor, eventBus, reportService, followUpRoutingService, logger, llmSettingsStore, null)
    {
    }

    public ResearchRunner(
        AppDbContext dbContext,
        IResearchRoleExecutor roleExecutor,
        IResearchEventBus eventBus,
        IResearchReportService reportService,
        IResearchFollowUpRoutingService followUpRoutingService,
        ILogger<ResearchRunner> logger,
        ILlmSettingsStore? llmSettingsStore,
        ISessionFileLogger? sessionLogger)
    {
        _dbContext = dbContext;
        _roleExecutor = roleExecutor;
        _eventBus = eventBus;
        _reportService = reportService;
        _followUpRoutingService = followUpRoutingService;
        _logger = logger;
        _llmSettingsStore = llmSettingsStore;
        _sessionLogger = sessionLogger;
    }

    public async Task RunTurnAsync(long turnId, CancellationToken cancellationToken = default)
    {
        // Load turn without cancellation token so we can always set status on cancel
        var turn = await _dbContext.ResearchTurns
            .Include(t => t.Session)
            .FirstOrDefaultAsync(t => t.Id == turnId, CancellationToken.None)
            ?? throw new InvalidOperationException($"Turn {turnId} not found");

        var session = turn.Session;
        turn.Status = ResearchTurnStatus.Running;
        turn.StartedAt = DateTime.UtcNow;
        session.Status = ResearchSessionStatus.Running;
        session.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        // Publish user query as the turn-started message (shows as user bubble in feed)
        var userQueryText = turn.TurnIndex == 0
            ? (session.Name ?? session.Symbol)
            : (turn.UserPrompt ?? "继续分析");

        _eventBus.Publish(new ResearchEvent(
            ResearchEventType.TurnStarted, session.Id, turn.Id, null, null, null,
            userQueryText, null, DateTime.UtcNow));

        var upstreamArtifacts = new List<string>();
        var turnDegraded = false;
        var fromStageIndex = 0;

        // ── Async LLM routing refinement for follow-up turns ──
        // The HTTP handler used an instant heuristic; now refine with full LLM routing
        if (turn.TurnIndex > 0 && turn.ContinuationMode == ResearchContinuationMode.ContinueSession
            && turn.RoutingConfidence is null or < 0.8m)
        {
            try
            {
                var llmRouting = await _followUpRoutingService.DecideAsync(session.Id, turn.UserPrompt ?? "", cancellationToken);
                if (llmRouting.ContinuationMode != turn.ContinuationMode || llmRouting.FromStageIndex != turn.RoutingStageIndex)
                {
                    _logger.LogInformation("LLM routing refined follow-up: {From} -> {To} (stage {Stage}, confidence {Conf})",
                        turn.ContinuationMode, llmRouting.ContinuationMode, llmRouting.FromStageIndex, llmRouting.Confidence);
                    turn.ContinuationMode = llmRouting.ContinuationMode;
                    turn.RerunScope = llmRouting.FromStageIndex?.ToString();
                    turn.ReuseScope = llmRouting.ReuseScope;
                    turn.ChangeSummary = llmRouting.ChangeSummary;
                    turn.RoutingDecision = llmRouting.ContinuationMode.ToString();
                    turn.RoutingReasoning = llmRouting.Reasoning;
                    turn.RoutingConfidence = llmRouting.Confidence;
                    turn.RoutingStageIndex = llmRouting.FromStageIndex;
                    await _dbContext.SaveChangesAsync(CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background LLM routing refinement failed for turn {TurnId}; using heuristic result", turnId);
            }
        }

        // Load user position context for prompt injection
        var position = await _dbContext.StockPositions
            .FirstOrDefaultAsync(p => p.Symbol == session.Symbol, CancellationToken.None);
        _positionContext = position is not null && position.QuantityLots > 0
            ? $"用户当前持仓：{position.QuantityLots} 手，均价 {position.AverageCostPrice:F2} 元"
            : null;

        // Resolve stock name from DB (company profile or quote snapshot) before pipeline starts
        _resolvedStockName = (await _dbContext.StockCompanyProfiles
            .Where(p => p.Symbol == session.Symbol)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(CancellationToken.None))
            ?? (await _dbContext.StockQuoteSnapshots
                .Where(q => q.Symbol == session.Symbol)
                .OrderByDescending(q => q.Timestamp)
                .Select(q => q.Name)
                .FirstOrDefaultAsync(CancellationToken.None));

        _sessionLogger?.LogTurnStart("research", session.Id, turn.Id, turn.TurnIndex,
            session.Symbol, _resolvedStockName, turn.UserPrompt ?? session.Name ?? "");

        // ── Determine effective start stage ──
        var effectiveRerunFrom = 0;
        if (int.TryParse(turn.RerunScope, out var rerunFrom) && rerunFrom > 0 && rerunFrom < Pipeline.Count)
        {
            effectiveRerunFrom = rerunFrom;
        }
        else if (turn.ContinuationMode == ResearchContinuationMode.ContinueSession && turn.TurnIndex > 0)
        {
            // ContinueSession: reuse all upstream artifacts, only re-run PortfolioDecision
            effectiveRerunFrom = Pipeline.Count - 1;
            _logger.LogInformation("ContinueSession mode: skipping to PortfolioDecision (stage {Stage})", effectiveRerunFrom);
        }

        // ── Partial rerun / ContinueSession: reuse artifacts from previous turn for skipped stages ──
        if (effectiveRerunFrom > 0)
        {
            var previousTurnId = await _dbContext.ResearchTurns
                .Where(t => t.SessionId == session.Id && t.Id < turn.Id &&
                    (t.Status == ResearchTurnStatus.Completed || t.Status == ResearchTurnStatus.Failed))
                .OrderByDescending(t => t.Id)
                .Select(t => (long?)t.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (previousTurnId.HasValue)
            {
                fromStageIndex = effectiveRerunFrom;

                // Load upstream artifacts from previous turn's completed stages
                var previousOutputs = await _dbContext.ResearchRoleStates
                    .Where(rs => rs.Stage.TurnId == previousTurnId.Value &&
                                rs.Stage.StageRunIndex < fromStageIndex &&
                                rs.OutputContentJson != null)
                    .OrderBy(rs => rs.Stage.StageRunIndex).ThenBy(rs => rs.Id)
                    .Select(rs => new { rs.RoleId, rs.OutputContentJson })
                    .ToListAsync(cancellationToken);

                foreach (var rs in previousOutputs)
                    upstreamArtifacts.Add($"[{rs.RoleId}]\n{rs.OutputContentJson}");

                // Create "Reused" stage snapshots for skipped stages
                for (var si = 0; si < fromStageIndex; si++)
                {
                    var sd = Pipeline[si];
                    _dbContext.ResearchStageSnapshots.Add(new ResearchStageSnapshot
                    {
                        TurnId = turn.Id,
                        StageType = sd.StageType,
                        StageRunIndex = si,
                        ExecutionMode = sd.ExecutionMode,
                        Status = ResearchStageStatus.Skipped,
                        Summary = "Reused from previous turn",
                        StartedAt = DateTime.UtcNow,
                        CompletedAt = DateTime.UtcNow
                    });
                }
                await _dbContext.SaveChangesAsync(cancellationToken);

                // Copy report blocks from previous turn for skipped stages
                var skippedStageTypes = Pipeline.Take(fromStageIndex).Select(s => s.StageType.ToString()).ToHashSet();
                var previousBlocks = await _dbContext.ResearchReportBlocks
                    .AsNoTracking()
                    .Where(b => b.TurnId == previousTurnId.Value && skippedStageTypes.Contains(b.SourceStageType!))
                    .ToListAsync(cancellationToken);

                foreach (var block in previousBlocks)
                {
                    _dbContext.ResearchReportBlocks.Add(new Data.Entities.ResearchReportBlock
                    {
                        SessionId = session.Id,
                        TurnId = turn.Id,
                        BlockType = block.BlockType,
                        VersionIndex = block.VersionIndex,
                        Headline = block.Headline,
                        Summary = block.Summary,
                        KeyPointsJson = block.KeyPointsJson,
                        EvidenceRefsJson = block.EvidenceRefsJson,
                        CounterEvidenceRefsJson = block.CounterEvidenceRefsJson,
                        DisagreementsJson = block.DisagreementsJson,
                        RiskLimitsJson = block.RiskLimitsJson,
                        InvalidationsJson = block.InvalidationsJson,
                        RecommendedActionsJson = block.RecommendedActionsJson,
                        Status = block.Status,
                        DegradedFlagsJson = block.DegradedFlagsJson,
                        MissingEvidence = block.MissingEvidence,
                        ConfidenceImpact = block.ConfidenceImpact,
                        SourceStageType = block.SourceStageType,
                        SourceArtifactId = block.SourceArtifactId,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                await _dbContext.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Partial rerun from stage {FromStage}: reused {ArtifactCount} artifacts from turn {PrevTurn}",
                    fromStageIndex, upstreamArtifacts.Count, previousTurnId.Value);
            }
            else
            {
                _logger.LogWarning("Rerun/ContinueSession requested but no previous completed turn found; running full pipeline");
            }
        }

        try
        {
            for (var i = fromStageIndex; i < Pipeline.Count; i++)
            {
                var stageDef = Pipeline[i];
                cancellationToken.ThrowIfCancellationRequested();

                session.ActiveStage = stageDef.StageType.ToString();
                session.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);

                var stageResult = await RunStageAsync(session, turn, stageDef, i, upstreamArtifacts, cancellationToken);

                if (stageResult.Status == ResearchStageStatus.Failed)
                {
                    turn.Status = ResearchTurnStatus.Failed;
                    turn.StopReason = $"Stage {stageDef.StageType} failed";
                    turn.CompletedAt = DateTime.UtcNow;
                    session.Status = ResearchSessionStatus.Failed;
                    session.UpdatedAt = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync(cancellationToken);

                    _eventBus.Publish(new ResearchEvent(
                        ResearchEventType.TurnFailed, session.Id, turn.Id, null, null, null,
                        $"Turn failed at {stageDef.StageType}", null, DateTime.UtcNow));

                    await PersistFeedItemsAsync(turn.Id, CancellationToken.None);
                    return;
                }

                if (stageResult.Status == ResearchStageStatus.Degraded)
                    turnDegraded = true;

                upstreamArtifacts.AddRange(stageResult.Outputs);

                // Fallback: extract stock name from LLM output if MCP tool extraction missed it
                if (stageDef.StageType == ResearchStageType.CompanyOverviewPreflight && _resolvedStockName is null)
                {
                    _resolvedStockName = TryExtractStockName(stageResult.Outputs);
                }

                // R5: persist structured debate/risk/proposal artifacts from role outputs
                await PersistStructuredArtifactsAsync(session, turn, stageDef, stageResult, cancellationToken);

                // R6: generate report block(s) from completed stage
                await GenerateReportBlocksSafe(session.Id, turn.Id, stageDef, stageResult, cancellationToken);

                if (stageDef.StageType == ResearchStageType.PortfolioDecision && stageResult.Outputs.Count > 0)
                    await CreateDecisionSnapshotAsync(session, turn, stageResult.Outputs[0], cancellationToken);
            }

            turn.Status = ResearchTurnStatus.Completed;
            turn.CompletedAt = DateTime.UtcNow;
            session.Status = turnDegraded ? ResearchSessionStatus.Degraded : ResearchSessionStatus.Completed;
            session.ActiveStage = null;
            session.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            _eventBus.Publish(new ResearchEvent(
                ResearchEventType.TurnCompleted, session.Id, turn.Id, null, null, null,
                "分析完成", null, DateTime.UtcNow));
        }
        catch (OperationCanceledException)
        {
            turn.Status = ResearchTurnStatus.Cancelled;
            turn.CompletedAt = DateTime.UtcNow;
            session.Status = ResearchSessionStatus.Idle;
            session.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            var innerMsg = ex.InnerException?.Message;
            var fullMsg = innerMsg is not null ? $"{ex.Message} -> {innerMsg}" : ex.Message;
            _logger.LogError(ex, "Turn {TurnId} failed: {Detail}", turnId, fullMsg);
            turn.Status = ResearchTurnStatus.Failed;
            turn.StopReason = fullMsg.Length > 2000 ? fullMsg[..2000] : fullMsg;
            turn.CompletedAt = DateTime.UtcNow;
            session.Status = ResearchSessionStatus.Failed;
            session.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(CancellationToken.None);

            _eventBus.Publish(new ResearchEvent(
                ResearchEventType.TurnFailed, session.Id, turn.Id, null, null, null,
                $"Turn failed: {fullMsg}", null, DateTime.UtcNow));
        }
        finally
        {
            _sessionLogger?.LogTurnEnd(session.Id, turn.Id, turn.Status.ToString());
            await PersistFeedItemsAsync(turn.Id, CancellationToken.None);
        }
    }

    private async Task<StageResult> RunStageAsync(
        ResearchSession session, ResearchTurn turn,
        StageDefinition stageDef, int stageRunIndex,
        IReadOnlyList<string> upstreamArtifacts,
        CancellationToken cancellationToken)
    {
        var effectiveRoleIds = await ResolveEffectiveRoleIdsAsync(stageDef, cancellationToken);

        var stage = new ResearchStageSnapshot
        {
            TurnId = turn.Id,
            StageType = stageDef.StageType,
            StageRunIndex = stageRunIndex,
            ExecutionMode = stageDef.ExecutionMode,
            Status = ResearchStageStatus.Running,
            ActiveRoleIdsJson = JsonSerializer.Serialize(effectiveRoleIds),
            StartedAt = DateTime.UtcNow
        };
        _dbContext.ResearchStageSnapshots.Add(stage);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _eventBus.Publish(new ResearchEvent(
            ResearchEventType.StageStarted, session.Id, turn.Id, stage.Id, null, null,
            $"Stage {stageDef.StageType} ({stageDef.ExecutionMode})", null, DateTime.UtcNow));

        var outputs = new List<string>();
        var degradedFlags = new List<string>();
        var failed = false;

        if (stageDef.ExecutionMode == ResearchStageExecutionMode.Debate)
        {
            var dr = await RunDebateAsync(session, turn, stage, stageDef, upstreamArtifacts, cancellationToken);
            outputs.AddRange(dr.Outputs);
            degradedFlags.AddRange(dr.DegradedFlags);
            failed = dr.Failed;
        }
        else if (stageDef.ExecutionMode == ResearchStageExecutionMode.Parallel)
        {
            var pr = await RunParallelAsync(session, turn, stage, effectiveRoleIds, 0, upstreamArtifacts, cancellationToken);
            outputs.AddRange(pr.Outputs);
            degradedFlags.AddRange(pr.DegradedFlags);
            failed = pr.Failed;
        }
        else
        {
            foreach (var roleId in effectiveRoleIds)
            {
                var r = await ExecuteAndPersistRoleAsync(session, turn, stage, roleId, 0, upstreamArtifacts, cancellationToken);
                if (r.Status == ResearchRoleStatus.Failed) { failed = true; break; }
                if (r.Status == ResearchRoleStatus.Degraded) degradedFlags.AddRange(r.DegradedFlags);
                if (r.OutputContentJson is not null) outputs.Add($"[{roleId}]\n{r.OutputContentJson}");

                // Extract stock name from MCP tool data when CompanyOverview completes
                if (stageDef.StageType == ResearchStageType.CompanyOverviewPreflight && _resolvedStockName is null)
                {
                    _resolvedStockName = TryExtractStockNameFromToolRefs(r.OutputRefsJson);
                }
            }
        }

        stage.Status = failed ? ResearchStageStatus.Failed
            : degradedFlags.Count > 0 ? ResearchStageStatus.Degraded
            : ResearchStageStatus.Completed;
        stage.CompletedAt = DateTime.UtcNow;
        stage.Summary = $"{outputs.Count} outputs, {degradedFlags.Count} degraded";
        if (degradedFlags.Count > 0) stage.DegradedFlagsJson = JsonSerializer.Serialize(degradedFlags);
        if (failed) stage.StopReason = "Role failure with required tools";
        await _dbContext.SaveChangesAsync(cancellationToken);

        _eventBus.Publish(new ResearchEvent(
            failed ? ResearchEventType.StageFailed : ResearchEventType.StageCompleted,
            session.Id, turn.Id, stage.Id, null, null,
            $"Stage {stageDef.StageType} {stage.Status}", null, DateTime.UtcNow));

        return new StageResult(stage.Status, outputs, degradedFlags);
    }

    private async Task<IReadOnlyList<string>> ResolveEffectiveRoleIdsAsync(
        StageDefinition stageDef,
        CancellationToken cancellationToken)
    {
        if (stageDef.StageType != ResearchStageType.AnalystTeam || _llmSettingsStore is null)
        {
            return stageDef.RoleIds;
        }

        try
        {
            var activeProviderKey = await _llmSettingsStore.GetActiveProviderKeyAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(activeProviderKey))
            {
                return stageDef.RoleIds;
            }

            var settings = await _llmSettingsStore.GetProviderAsync(activeProviderKey, cancellationToken);
            if (!OllamaRuntimeDefaults.IsOllamaProvider(activeProviderKey, settings?.ProviderType))
            {
                return stageDef.RoleIds;
            }

            _logger.LogInformation(
                "Using full AnalystTeam profile for local Ollama provider {Provider}",
                activeProviderKey);

            return stageDef.RoleIds;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve active provider for AnalystTeam profile selection; using full analyst team");
            return stageDef.RoleIds;
        }
    }

    private async Task<DebateResult> RunDebateAsync(
        ResearchSession session, ResearchTurn turn, ResearchStageSnapshot stage,
        StageDefinition stageDef, IReadOnlyList<string> upstreamArtifacts,
        CancellationToken cancellationToken)
    {
        var allOutputs = new List<string>();
        var degradedFlags = new List<string>();
        ResearchDebateRoundSignal? previousResearchRound = null;
        RiskDebateRoundSignal? previousRiskRound = null;

        for (var round = 0; round < MaxDebateRounds; round++)
        {
            var roundContext = new List<string>(upstreamArtifacts);
            roundContext.AddRange(allOutputs);

            var rr = await RunParallelAsync(session, turn, stage, stageDef.RoleIds, round, roundContext, cancellationToken);
            if (rr.Failed) return new DebateResult(allOutputs, degradedFlags, true);

            degradedFlags.AddRange(rr.DegradedFlags);
            allOutputs.AddRange(rr.Outputs);

            var currentResearchRound = stageDef.StageType == ResearchStageType.ResearchDebate
                ? TryParseResearchDebateRoundSignal(rr.Outputs)
                : null;
            var currentRiskRound = stageDef.StageType == ResearchStageType.RiskDebate
                ? TryParseRiskDebateRoundSignal(rr.Outputs)
                : null;

            if (round > 0 && rr.Outputs.Count > 0)
            {
                if (HasExplicitConvergenceSignal(rr.Outputs))
                {
                    _logger.LogInformation("Debate {StageType} converged at round {Round}", stageDef.StageType, round + 1);
                    break;
                }

                var repetitiveLowValue = round == 1 && stageDef.StageType switch
                {
                    ResearchStageType.ResearchDebate => previousResearchRound is not null
                        && currentResearchRound is not null
                        && ShouldEarlyStopResearchDebate(previousResearchRound, currentResearchRound),
                    ResearchStageType.RiskDebate => previousRiskRound is not null
                        && currentRiskRound is not null
                        && ShouldEarlyStopRiskDebate(previousRiskRound, currentRiskRound),
                    _ => false
                };

                if (repetitiveLowValue)
                {
                    _logger.LogInformation(
                        "Debate {StageType} early-stopped after round {Round} due to repetitive low-value outputs",
                        stageDef.StageType,
                        round + 1);
                    break;
                }
            }

            previousResearchRound = currentResearchRound;
            previousRiskRound = currentRiskRound;
        }

        return new DebateResult(allOutputs, degradedFlags, false);
    }

    private static bool HasExplicitConvergenceSignal(IReadOnlyList<string> outputs)
    {
        return outputs.Any(o =>
            o.Contains("CONVERGED", StringComparison.OrdinalIgnoreCase) ||
            o.Contains("收敛", StringComparison.Ordinal) ||
            o.Contains("\"converged\":true", StringComparison.OrdinalIgnoreCase) ||
            o.Contains("\"converged\": true", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<ParallelResult> RunParallelAsync(
        ResearchSession session, ResearchTurn turn, ResearchStageSnapshot stage,
        IReadOnlyList<string> roleIds, int runIndex,
        IReadOnlyList<string> upstreamArtifacts,
        CancellationToken cancellationToken)
    {
        // Phase 1: Create RoleState records sequentially (DbContext is NOT thread-safe)
        var roleStates = new List<ResearchRoleState>();
        var execContexts = new List<RoleExecutionContext>();

        foreach (var roleId in roleIds)
        {
            var roleState = new ResearchRoleState
            {
                StageId = stage.Id,
                RoleId = roleId,
                RunIndex = runIndex,
                Status = ResearchRoleStatus.Running,
                StartedAt = DateTime.UtcNow
            };
            _dbContext.ResearchRoleStates.Add(roleState);
            roleStates.Add(roleState);

            execContexts.Add(new RoleExecutionContext(
                session.Id, turn.Id, stage.Id, session.Symbol, roleId,
                turn.UserPrompt, upstreamArtifacts, _positionContext, _resolvedStockName));
        }
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Phase 2: Execute roles in parallel (IO-bound LLM + MCP calls, no DbContext access)
        var executionTasks = execContexts.Select(ctx =>
            _roleExecutor.ExecuteRoleAsync(ctx, cancellationToken));
        var results = await Task.WhenAll(executionTasks);

        // Phase 3: Persist results sequentially
        var outputs = new List<string>();
        var degradedFlags = new List<string>();
        var failed = false;

        for (var i = 0; i < results.Length; i++)
        {
            var r = results[i];
            var roleState = roleStates[i];

            roleState.Status = r.Status;
            roleState.OutputContentJson = r.OutputContentJson;
            roleState.OutputRefsJson = r.OutputRefsJson;
            roleState.LlmTraceId = r.LlmTraceId;
            roleState.DegradedFlagsJson = r.DegradedFlags.Count > 0 ? JsonSerializer.Serialize(r.DegradedFlags) : null;
            roleState.ErrorCode = r.ErrorCode;
            roleState.ErrorMessage = r.ErrorMessage;
            roleState.CompletedAt = DateTime.UtcNow;

            if (r.Status == ResearchRoleStatus.Failed) failed = true;
            if (r.Status == ResearchRoleStatus.Degraded) degradedFlags.AddRange(r.DegradedFlags);
            if (r.OutputContentJson is not null) outputs.Add($"[{r.RoleId}]\n{r.OutputContentJson}");
        }
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new ParallelResult(outputs, degradedFlags, failed);
    }

    private async Task<RoleExecutionResult> ExecuteAndPersistRoleAsync(
        ResearchSession session, ResearchTurn turn, ResearchStageSnapshot stage,
        string roleId, int runIndex, IReadOnlyList<string> upstreamArtifacts,
        CancellationToken cancellationToken)
    {
        var roleState = new ResearchRoleState
        {
            StageId = stage.Id,
            RoleId = roleId,
            RunIndex = runIndex,
            Status = ResearchRoleStatus.Running,
            StartedAt = DateTime.UtcNow
        };
        _dbContext.ResearchRoleStates.Add(roleState);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var context = new RoleExecutionContext(
            session.Id, turn.Id, stage.Id, session.Symbol, roleId,
            turn.UserPrompt, upstreamArtifacts, _positionContext, _resolvedStockName);

        var result = await _roleExecutor.ExecuteRoleAsync(context, cancellationToken);

        roleState.Status = result.Status;
        roleState.OutputContentJson = result.OutputContentJson;
        roleState.OutputRefsJson = result.OutputRefsJson;
        roleState.LlmTraceId = result.LlmTraceId;
        roleState.DegradedFlagsJson = result.DegradedFlags.Count > 0 ? JsonSerializer.Serialize(result.DegradedFlags) : null;
        roleState.ErrorCode = result.ErrorCode;
        roleState.ErrorMessage = result.ErrorMessage;
        roleState.CompletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return result;
    }

    private async Task CreateDecisionSnapshotAsync(
        ResearchSession session, ResearchTurn turn,
        string outputContent, CancellationToken cancellationToken)
    {
        try
        {
            // Strip [roleId]\n prefix if present from stage output collection
            var jsonStart = outputContent.IndexOf('{');
            if (jsonStart > 0)
                outputContent = outputContent[jsonStart..];

            using var doc = JsonDocument.Parse(outputContent);
            var root = doc.RootElement;
            string? innerContent = null;
            if (root.TryGetProperty("content", out var c))
                innerContent = c.GetString();

            using var decDoc = innerContent is not null ? JsonDocument.Parse(innerContent) : null;
            var dec = decDoc?.RootElement ?? root;

            var rating = dec.TryGetProperty("rating", out var r) ? r.GetString() : null;
            var action = dec.TryGetProperty("action", out var a) ? a.GetString() : null;
            var summary = dec.TryGetProperty("executive_summary", out var s) ? s.GetString() : null;
            var confidence = dec.TryGetProperty("confidence", out var cv) && cv.TryGetDecimal(out var d) ? d : (decimal?)null;

            var decision = new ResearchDecisionSnapshot
            {
                SessionId = session.Id, TurnId = turn.Id,
                Rating = rating?.Length > 32 ? rating[..32] : rating,
                Action = action?.Length > 64 ? action[..64] : action,
                ExecutiveSummary = summary,
                InvestmentThesis = dec.TryGetProperty("investment_thesis", out var it) ? it.GetString() : null,
                FinalDecisionJson = outputContent, Confidence = confidence,
                ConfidenceExplanation = dec.TryGetProperty("confidence_explanation", out var cex) ? cex.GetString() : null,
                SupportingEvidenceJson = dec.TryGetProperty("supporting_evidence", out var se) ? se.GetRawText() : null,
                CounterEvidenceJson = dec.TryGetProperty("counter_evidence", out var ce) ? ce.GetRawText() : null,
                RiskConsensus = dec.TryGetProperty("risk_consensus", out var rc2) ? rc2.GetString() : null,
                DissentJson = dec.TryGetProperty("dissent", out var dis) ? dis.GetRawText() : null,
                NextActionsJson = dec.TryGetProperty("next_actions", out var na) ? na.GetRawText() : null,
                InvalidationConditionsJson = dec.TryGetProperty("invalidation_conditions", out var ic) ? ic.GetRawText() : null,
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.ResearchDecisionSnapshots.Add(decision);
            session.LatestRating = rating?.Length > 32 ? rating[..32] : rating;
            session.LatestDecisionHeadline = summary?.Length > 512 ? summary[..512] : summary;
            session.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse decision, storing raw");
            _dbContext.ResearchDecisionSnapshots.Add(new ResearchDecisionSnapshot
            {
                SessionId = session.Id, TurnId = turn.Id,
                FinalDecisionJson = outputContent, CreatedAt = DateTime.UtcNow
            });
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task PersistStructuredArtifactsAsync(
        ResearchSession session, ResearchTurn turn,
        StageDefinition stageDef, StageResult stageResult,
        CancellationToken cancellationToken)
    {
        if (stageResult.Status == ResearchStageStatus.Failed || stageResult.Outputs.Count == 0)
            return;

        try
        {
            var stageSnapshot = await _dbContext.ResearchStageSnapshots
                .Where(s => s.TurnId == turn.Id && s.StageType == stageDef.StageType)
                .OrderByDescending(s => s.StageRunIndex)
                .FirstOrDefaultAsync(cancellationToken);

            if (stageSnapshot is null) return;

            switch (stageDef.StageType)
            {
                case ResearchStageType.ResearchDebate:
                    PersistDebateArtifacts(session, turn, stageSnapshot, stageResult.Outputs);
                    break;
                case ResearchStageType.TraderProposal:
                    await PersistTraderProposalAsync(session, turn, stageSnapshot, stageResult.Outputs, cancellationToken);
                    break;
                case ResearchStageType.RiskDebate:
                    PersistRiskArtifacts(session, turn, stageSnapshot, stageResult.Outputs);
                    break;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Detach dirty R5 entities so they don't poison subsequent SaveChangesAsync calls
            foreach (var entry in _dbContext.ChangeTracker.Entries().ToList())
                if (entry.State == Microsoft.EntityFrameworkCore.EntityState.Added
                    || entry.State == Microsoft.EntityFrameworkCore.EntityState.Deleted
                    || entry.State == Microsoft.EntityFrameworkCore.EntityState.Modified)
                {
                    var typeName = entry.Entity.GetType().Name;
                    if (typeName is "ResearchDebateMessage" or "ResearchTraderProposal" or "ResearchRiskAssessment")
                        entry.State = Microsoft.EntityFrameworkCore.EntityState.Detached;
                }
            _logger.LogError(ex, "Failed to persist R5 artifacts for stage {StageType}", stageDef.StageType);
        }
    }

    private async Task GenerateReportBlocksSafe(
        long sessionId, long turnId,
        StageDefinition stageDef, StageResult stageResult,
        CancellationToken cancellationToken)
    {
        if (stageResult.Status == ResearchStageStatus.Failed || stageResult.Outputs.Count == 0)
            return;

        try
        {
            await _reportService.GenerateBlocksFromStageAsync(
                sessionId, turnId, stageDef.StageType,
                stageResult.Outputs, stageResult.DegradedFlags, cancellationToken);
        }
        catch (Exception ex)
        {
            // Detach dirty report-block entities so they don't poison subsequent SaveChangesAsync calls
            foreach (var entry in _dbContext.ChangeTracker.Entries<Data.Entities.ResearchReportBlock>().ToList())
                if (entry.State != Microsoft.EntityFrameworkCore.EntityState.Unchanged)
                    entry.State = Microsoft.EntityFrameworkCore.EntityState.Detached;

            _logger.LogError(ex, "Failed to generate R6 report blocks for stage {StageType}", stageDef.StageType);
            // Surface degradation without breaking the pipeline
            _eventBus.Publish(new ResearchEvent(
                ResearchEventType.DegradedNotice, sessionId, turnId, null, null, null,
                $"Report block generation failed for {stageDef.StageType}: {ex.Message}",
                null, DateTime.UtcNow));
        }
    }

    private void PersistDebateArtifacts(
        ResearchSession session, ResearchTurn turn,
        ResearchStageSnapshot stage, List<string> outputs)
    {
        var roundIndex = 0;
        foreach (var output in outputs)
        {
            var (roleId, content) = SplitRoleOutput(output);
            if (roleId is null) continue;

            var side = roleId switch
            {
                StockAgentRoleIds.BullResearcher => ResearchDebateSide.Bull,
                StockAgentRoleIds.BearResearcher => ResearchDebateSide.Bear,
                StockAgentRoleIds.ResearchManager => ResearchDebateSide.Manager,
                _ => (ResearchDebateSide?)null
            };

            if (side is null) continue;

            // Parse structured fields from LLM JSON output
            var (claim, evidenceRefs, counterTarget, counterPoints, openQuestions, traceId) = ParseDebateContent(content);

            _dbContext.ResearchDebateMessages.Add(new ResearchDebateMessage
            {
                SessionId = session.Id, TurnId = turn.Id, StageId = stage.Id,
                Side = side.Value, RoleId = roleId, RoundIndex = roundIndex / DebateParticipantsPerRound,
                Claim = claim ?? content,
                SupportingEvidenceRefsJson = evidenceRefs,
                CounterTargetRole = counterTarget,
                CounterPointsJson = counterPoints,
                OpenQuestionsJson = openQuestions,
                LlmTraceId = traceId,
                CreatedAt = DateTime.UtcNow
            });

            // If this is the research manager, also create a verdict
            if (side == ResearchDebateSide.Manager)
            {
                var (adopted, shelved, conclusion, planDraft, isConverged, verdictTraceId) = ParseManagerVerdict(content);
                _dbContext.ResearchManagerVerdicts.Add(new ResearchManagerVerdict
                {
                    SessionId = session.Id, TurnId = turn.Id, StageId = stage.Id,
                    RoundIndex = roundIndex / DebateParticipantsPerRound,
                    AdoptedBullPointsJson = adopted?.bull,
                    AdoptedBearPointsJson = adopted?.bear,
                    ShelvedDisputesJson = shelved,
                    ResearchConclusion = conclusion,
                    InvestmentPlanDraftJson = planDraft,
                    IsConverged = isConverged,
                    LlmTraceId = verdictTraceId,
                    CreatedAt = DateTime.UtcNow
                });
            }

            roundIndex++;
        }
    }

    private async Task PersistTraderProposalAsync(
        ResearchSession session, ResearchTurn turn,
        ResearchStageSnapshot stage, List<string> outputs,
        CancellationToken cancellationToken)
    {
        // Only supersede proposals from prior turns
        var priorActive = _dbContext.ResearchTraderProposals
            .Where(p => p.SessionId == session.Id && p.TurnId != turn.Id && p.Status == TraderProposalStatus.Active)
            .ToList();

        var newVersion = priorActive.Count > 0 ? priorActive.Max(p => p.Version) + 1 : 1;

        foreach (var output in outputs)
        {
            var (roleId, content) = SplitRoleOutput(output);
            if (roleId != StockAgentRoleIds.Trader) continue;

            var (direction, entryPlan, exitPlan, sizing, rationale, traceId) = ParseTraderProposal(content);

            var proposal = new ResearchTraderProposal
            {
                SessionId = session.Id, TurnId = turn.Id, StageId = stage.Id,
                Version = newVersion,
                Status = TraderProposalStatus.Active,
                Direction = direction, EntryPlanJson = entryPlan,
                ExitPlanJson = exitPlan, PositionSizingJson = sizing,
                Rationale = rationale, LlmTraceId = traceId,
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.ResearchTraderProposals.Add(proposal);

            // Save to obtain the new proposal's Id for linking
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Mark prior active proposals as superseded with backlink
            foreach (var prior in priorActive)
            {
                prior.Status = TraderProposalStatus.Superseded;
                prior.SupersededByProposalId = proposal.Id;
            }
        }
    }

    private void PersistRiskArtifacts(
        ResearchSession session, ResearchTurn turn,
        ResearchStageSnapshot stage, List<string> outputs)
    {
        // Link risk assessments to the current active trader proposal
        var activeProposalId = _dbContext.ResearchTraderProposals
            .Where(p => p.SessionId == session.Id && p.Status == TraderProposalStatus.Active)
            .OrderByDescending(p => p.Version)
            .Select(p => (long?)p.Id)
            .FirstOrDefault();

        var roundIndex = 0;
        foreach (var output in outputs)
        {
            var (roleId, content) = SplitRoleOutput(output);
            if (roleId is null) continue;

            var tier = roleId switch
            {
                StockAgentRoleIds.AggressiveRiskAnalyst => RiskAnalystTier.Aggressive,
                StockAgentRoleIds.NeutralRiskAnalyst => RiskAnalystTier.Neutral,
                StockAgentRoleIds.ConservativeRiskAnalyst => RiskAnalystTier.Conservative,
                _ => (RiskAnalystTier?)null
            };

            if (tier is null) continue;

            var (riskLimits, invalidations, assessment, analysis, traceId) = ParseRiskAssessment(content);

            _dbContext.ResearchRiskAssessments.Add(new ResearchRiskAssessment
            {
                SessionId = session.Id, TurnId = turn.Id, StageId = stage.Id,
                RoleId = roleId, Tier = tier.Value,
                RoundIndex = roundIndex / RiskAnalystsPerRound,
                RiskLimitsJson = riskLimits, InvalidationsJson = invalidations,
                ProposalAssessment = assessment, AnalysisContent = analysis,
                ResponseToArtifactId = activeProposalId,
                LlmTraceId = traceId,
                CreatedAt = DateTime.UtcNow
            });

            roundIndex++;
        }
    }

    /// <summary>Split "[roleId]\ncontent" format from stage outputs.</summary>
    internal static (string? RoleId, string Content) SplitRoleOutput(string output)
    {
        if (output.StartsWith('['))
        {
            var closeBracket = output.IndexOf(']');
            if (closeBracket > 1)
            {
                var roleId = output[1..closeBracket];
                var content = output[(closeBracket + 1)..].TrimStart('\n', '\r');
                return (roleId, content);
            }
        }
        return (null, output);
    }

    /// <summary>Best-effort parse of debate LLM JSON output.</summary>
    private static (string? Claim, string? EvidenceRefs, string? CounterTarget, string? CounterPoints, string? OpenQuestions, string? TraceId) ParseDebateContent(string content)
    {
        try
        {
            var jsonStart = content.IndexOf('{');
            if (jsonStart < 0) return (content, null, null, null, null, null);
            var jsonText = UnwrapContentWrapper(content[jsonStart..]);
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;
            return (
                root.TryGetProperty("claim", out var cl) ? cl.GetString() : content,
                root.TryGetProperty("supporting_evidence_refs", out var e) ? e.GetRawText() : null,
                root.TryGetProperty("counter_target_role", out var ct) ? ct.GetString() : null,
                root.TryGetProperty("counter_points", out var cp) ? cp.GetRawText() : null,
                root.TryGetProperty("open_questions", out var oq) ? oq.GetRawText() : null,
                root.TryGetProperty("trace_id", out var t) ? t.GetString() : null
            );
        }
        catch { return (content, null, null, null, null, null); }
    }

    private static ResearchDebateRoundSignal? TryParseResearchDebateRoundSignal(IReadOnlyList<string> outputs)
    {
        foreach (var output in outputs)
        {
            var (roleId, content) = SplitRoleOutput(output);
            if (roleId != StockAgentRoleIds.ResearchManager)
                continue;

            try
            {
                var jsonStart = content.IndexOf('{');
                if (jsonStart < 0) return null;

                var jsonText = UnwrapContentWrapper(content[jsonStart..]);
                using var doc = JsonDocument.Parse(jsonText);
                var root = doc.RootElement;

                return new ResearchDebateRoundSignal(
                    NormalizeResearchDecision(TryGetStringProperty(root, "decision") ?? TryGetStringProperty(root, "research_conclusion")),
                    NormalizeConfidence(TryGetStringProperty(root, "decisionConfidence") ?? TryGetStringProperty(root, "confidence")),
                    NormalizeRecommendation(TryGetStringProperty(root, "recommendation")));
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static bool ShouldEarlyStopResearchDebate(ResearchDebateRoundSignal previous, ResearchDebateRoundSignal current)
    {
        return previous.Decision == ResearchDecisionSignal.Hold
            && current.Decision == ResearchDecisionSignal.Hold
            && previous.Confidence == ResearchConfidenceSignal.Low
            && current.Confidence == ResearchConfidenceSignal.Low
            && previous.Recommendation == current.Recommendation
            && current.Recommendation is ResearchRecommendationSignal.WaitForData
                or ResearchRecommendationSignal.Observe
                or ResearchRecommendationSignal.Unknown;
    }

    /// <summary>Best-effort parse of manager verdict from JSON.</summary>
    private static ((string? bull, string? bear)? Adopted, string? Shelved, string? Conclusion, string? PlanDraft, bool IsConverged, string? TraceId) ParseManagerVerdict(string content)
    {
        try
        {
            var jsonStart = content.IndexOf('{');
            if (jsonStart < 0) return (null, null, content, null, false, null);
            var jsonText = UnwrapContentWrapper(content[jsonStart..]);
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;
            var bull = root.TryGetProperty("adopted_bull_points", out var bp) ? bp.GetRawText() : null;
            var bear = root.TryGetProperty("adopted_bear_points", out var brp) ? brp.GetRawText() : null;
            var shelved = root.TryGetProperty("shelved_disputes", out var sd) ? sd.GetRawText() : null;
            var conclusion = root.TryGetProperty("research_conclusion", out var rc) ? rc.GetString() : null;
            var planDraft = root.TryGetProperty("investment_plan_draft", out var ip) ? ip.GetRawText() : null;
            var isConverged = (root.TryGetProperty("converged", out var cv) && cv.GetBoolean())
                || (conclusion?.Contains("CONVERGED", StringComparison.OrdinalIgnoreCase) ?? false)
                || (conclusion?.Contains("收敛", StringComparison.Ordinal) ?? false);
            var traceId = root.TryGetProperty("trace_id", out var t) ? t.GetString() : null;
            return ((bull, bear), shelved, conclusion, planDraft, isConverged, traceId);
        }
        catch { return (null, null, content, null, false, null); }
    }

    /// <summary>Best-effort parse of trader proposal from JSON.</summary>
    private static (string? Direction, string? EntryPlan, string? ExitPlan, string? Sizing, string? Rationale, string? TraceId) ParseTraderProposal(string content)
    {
        try
        {
            var jsonStart = content.IndexOf('{');
            if (jsonStart < 0) return (null, null, null, null, content, null);
            var jsonText = UnwrapContentWrapper(content[jsonStart..]);
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;
            return (
                root.TryGetProperty("direction", out var d) ? d.GetString() : null,
                root.TryGetProperty("entry_plan", out var ep) ? ep.GetRawText() : null,
                root.TryGetProperty("exit_plan", out var xp) ? xp.GetRawText() : null,
                root.TryGetProperty("position_sizing", out var ps) ? ps.GetRawText() : null,
                root.TryGetProperty("rationale", out var r) ? r.GetString() : null,
                root.TryGetProperty("trace_id", out var t) ? t.GetString() : null
            );
        }
        catch { return (null, null, null, null, content, null); }
    }

    /// <summary>Best-effort parse of risk assessment from JSON.</summary>
    private static (string? RiskLimits, string? Invalidations, string? Assessment, string? Analysis, string? TraceId) ParseRiskAssessment(string content)
    {
        try
        {
            var jsonStart = content.IndexOf('{');
            if (jsonStart < 0) return (null, null, null, content, null);
            var jsonText = UnwrapContentWrapper(content[jsonStart..]);
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;
            return (
                root.TryGetProperty("risk_limits", out var rl) ? rl.GetRawText() : null,
                root.TryGetProperty("invalidations", out var inv) ? inv.GetRawText() : null,
                root.TryGetProperty("proposal_assessment", out var pa) ? pa.GetString() : null,
                root.TryGetProperty("analysis", out var a) ? a.GetString() : null,
                root.TryGetProperty("trace_id", out var t) ? t.GetString() : null
            );
        }
        catch { return (null, null, null, content, null); }
    }

    private static RiskDebateRoundSignal? TryParseRiskDebateRoundSignal(IReadOnlyList<string> outputs)
    {
        var roleSignals = new Dictionary<string, RiskDebateRoleSignal>(StringComparer.Ordinal);

        foreach (var output in outputs)
        {
            var (roleId, content) = SplitRoleOutput(output);
            if (roleId is null || !RiskDebateRoleIds.Contains(roleId))
                continue;

            try
            {
                var jsonStart = content.IndexOf('{');
                if (jsonStart < 0)
                    continue;

                var jsonText = UnwrapContentWrapper(content[jsonStart..]);
                using var doc = JsonDocument.Parse(jsonText);
                var root = doc.RootElement;

                roleSignals[roleId] = new RiskDebateRoleSignal(
                    NormalizeRiskStance(TryGetStringProperty(root, "riskStance")),
                    NormalizeRecommendation(TryGetStringProperty(root, "recommendation")),
                    NormalizeRiskAssessment(
                        TryGetStringProperty(root, "proposal_assessment"),
                        TryGetStringProperty(root, "riskAssessment") ?? TryGetStringProperty(root, "proposalAssessment")));
            }
            catch
            {
                return null;
            }
        }

        return roleSignals.Count > 0 ? new RiskDebateRoundSignal(roleSignals) : null;
    }

    private static bool ShouldEarlyStopRiskDebate(RiskDebateRoundSignal previous, RiskDebateRoundSignal current)
    {
        if (!IsLowValueRiskRound(previous) || !IsLowValueRiskRound(current))
            return false;

        foreach (var roleId in RiskDebateRoleIds)
        {
            if (!previous.RoleSignals.TryGetValue(roleId, out var previousRole)
                || !current.RoleSignals.TryGetValue(roleId, out var currentRole))
            {
                return false;
            }

            if (previousRole.Stance != currentRole.Stance || previousRole.Assessment != currentRole.Assessment)
                return false;

            if (previousRole.Recommendation != ResearchRecommendationSignal.Unknown
                && currentRole.Recommendation != ResearchRecommendationSignal.Unknown
                && previousRole.Recommendation != currentRole.Recommendation)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLowValueRiskRound(RiskDebateRoundSignal signal)
    {
        return RiskDebateRoleIds.All(roleId =>
            signal.RoleSignals.TryGetValue(roleId, out var roleSignal)
            && roleSignal.Stance != RiskStanceSignal.Unknown
            && roleSignal.Assessment == RiskAssessmentSignal.DataInsufficient
            && roleSignal.Recommendation is ResearchRecommendationSignal.WaitForData
                or ResearchRecommendationSignal.Observe
                or ResearchRecommendationSignal.Unknown);
    }

    private static string? TryGetStringProperty(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.GetRawText(),
            _ => null
        };
    }

    private static ResearchDecisionSignal NormalizeResearchDecision(string? decision)
    {
        var normalized = NormalizeText(decision);
        if (string.IsNullOrEmpty(normalized)) return ResearchDecisionSignal.Unknown;
        if (normalized.Contains("观望") || normalized.Contains("hold") || normalized.Contains("observe")) return ResearchDecisionSignal.Hold;
        if (normalized.Contains("看多") || normalized.Contains("bull") || normalized == "buy") return ResearchDecisionSignal.Bullish;
        if (normalized.Contains("看空") || normalized.Contains("bear") || normalized == "sell") return ResearchDecisionSignal.Bearish;
        return ResearchDecisionSignal.Unknown;
    }

    private static ResearchConfidenceSignal NormalizeConfidence(string? confidence)
    {
        var normalized = NormalizeText(confidence);
        if (string.IsNullOrEmpty(normalized)) return ResearchConfidenceSignal.Unknown;
        if (normalized.Contains("低") || normalized.Contains("low")) return ResearchConfidenceSignal.Low;
        if (normalized.Contains("高") || normalized.Contains("high")) return ResearchConfidenceSignal.High;
        if (normalized.Contains("中") || normalized.Contains("medium")) return ResearchConfidenceSignal.Medium;
        return ResearchConfidenceSignal.Unknown;
    }

    private static ResearchRecommendationSignal NormalizeRecommendation(string? recommendation)
    {
        var normalized = NormalizeText(recommendation);
        if (string.IsNullOrEmpty(normalized)) return ResearchRecommendationSignal.Unknown;

        var waitTerms = new[] { "等待", "wait", "获取", "补充", "更多", "再进行", "之后再" };
        var dataTerms = new[] { "数据", "财务", "指标", "报表", "量化", "market", "price", "report", "information" };
        if (ContainsAny(normalized, waitTerms) && ContainsAny(normalized, dataTerms)) return ResearchRecommendationSignal.WaitForData;
        if (normalized.Contains("观望") || normalized.Contains("hold") || normalized.Contains("观察")) return ResearchRecommendationSignal.Observe;
        if (ContainsAny(normalized, ["买", "卖", "配置", "执行", "介入", "buy", "sell", "allocate", "execute"])) return ResearchRecommendationSignal.Act;
        return ResearchRecommendationSignal.Unknown;
    }

    private static RiskStanceSignal NormalizeRiskStance(string? riskStance)
    {
        var normalized = NormalizeText(riskStance);
        if (string.IsNullOrEmpty(normalized)) return RiskStanceSignal.Unknown;
        if (normalized.Contains("激进") || normalized.Contains("aggressive")) return RiskStanceSignal.Aggressive;
        if (normalized.Contains("中性") || normalized.Contains("neutral")) return RiskStanceSignal.Neutral;
        if (normalized.Contains("保守") || normalized.Contains("conservative")) return RiskStanceSignal.Conservative;
        return RiskStanceSignal.Unknown;
    }

    private static RiskAssessmentSignal NormalizeRiskAssessment(string? proposalAssessment, string? riskAssessment)
    {
        var proposalNormalized = NormalizeText(proposalAssessment);
        if (!string.IsNullOrEmpty(proposalNormalized))
        {
            if (ContainsAny(proposalNormalized, ["reject", "拒绝", "否决", "不建议", "暂停"])) return RiskAssessmentSignal.Reject;
            if (ContainsAny(proposalNormalized, ["accept", "通过", "接受", "modify", "调整", "修改"])) return RiskAssessmentSignal.Guarded;
        }

        var assessmentNormalized = NormalizeText(riskAssessment);
        if (string.IsNullOrEmpty(assessmentNormalized)) return RiskAssessmentSignal.Unknown;

        if (ContainsAny(assessmentNormalized, ["缺乏", "无法", "不足", "missing", "lack", "insufficient"])
            && ContainsAny(assessmentNormalized, ["数据", "量化", "信息", "data", "information", "quant"]))
        {
            return RiskAssessmentSignal.DataInsufficient;
        }

        if (ContainsAny(assessmentNormalized, ["不建议", "暂停", "拒绝", "avoid", "reject", "donot"]))
            return RiskAssessmentSignal.Reject;

        if (ContainsAny(assessmentNormalized, ["可控", "可接受", "接受", "controlled", "acceptable"]))
            return RiskAssessmentSignal.Guarded;

        return RiskAssessmentSignal.Unknown;
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return string.Concat(value.Where(ch => !char.IsWhiteSpace(ch))).ToLowerInvariant();
    }

    private static bool ContainsAny(string value, IEnumerable<string> terms)
    {
        foreach (var term in terms)
        {
            if (value.Contains(term, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>If the raw JSON has a {"content":"..."} wrapper, extract the inner JSON string. No leaked JsonDocument.</summary>
    internal static string UnwrapContentWrapper(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
            {
                var inner = c.GetString();
                if (inner is not null)
                {
                    var innerStart = inner.IndexOf('{');
                    var innerEnd = inner.LastIndexOf('}');
                    if (innerStart >= 0 && innerEnd > innerStart)
                        return inner[innerStart..(innerEnd + 1)];
                }
            }
        }
        catch { /* not valid JSON or no wrapper — return as-is */ }
        return rawJson;
    }

    private static readonly string[] RiskDebateRoleIds =
    [
        StockAgentRoleIds.AggressiveRiskAnalyst,
        StockAgentRoleIds.NeutralRiskAnalyst,
        StockAgentRoleIds.ConservativeRiskAnalyst
    ];

    private const int DebateParticipantsPerRound = 3;
    private const int RiskAnalystsPerRound = 3;

    private sealed record ResearchDebateRoundSignal(
        ResearchDecisionSignal Decision,
        ResearchConfidenceSignal Confidence,
        ResearchRecommendationSignal Recommendation);

    private sealed record RiskDebateRoundSignal(
        IReadOnlyDictionary<string, RiskDebateRoleSignal> RoleSignals);

    private sealed record RiskDebateRoleSignal(
        RiskStanceSignal Stance,
        ResearchRecommendationSignal Recommendation,
        RiskAssessmentSignal Assessment);

    private enum ResearchDecisionSignal
    {
        Unknown,
        Bullish,
        Bearish,
        Hold
    }

    private enum ResearchConfidenceSignal
    {
        Unknown,
        Low,
        Medium,
        High
    }

    private enum ResearchRecommendationSignal
    {
        Unknown,
        WaitForData,
        Observe,
        Act
    }

    private enum RiskStanceSignal
    {
        Unknown,
        Aggressive,
        Neutral,
        Conservative
    }

    private enum RiskAssessmentSignal
    {
        Unknown,
        DataInsufficient,
        Guarded,
        Reject
    }

    private async Task PersistFeedItemsAsync(long turnId, CancellationToken cancellationToken)
    {
        var events = _eventBus.Drain(turnId);
        foreach (var evt in events)
        {
            var feedType = MapEventToFeedType(evt.EventType);

            // Override feed type when metadata signals a UserFollowUp
            if (evt.DetailJson is not null &&
                evt.DetailJson.Contains("\"feedItemType\":\"UserFollowUp\"", StringComparison.Ordinal))
            {
                feedType = ResearchFeedItemType.UserFollowUp;
            }

            _dbContext.ResearchFeedItems.Add(new ResearchFeedItem
            {
                TurnId = evt.TurnId,
                StageId = evt.StageId,
                RoleId = evt.RoleId,
                ItemType = feedType,
                Content = evt.Summary,
                MetadataJson = evt.DetailJson,
                TraceId = evt.TraceId,
                CreatedAt = evt.Timestamp
            });
        }
        if (events.Count > 0)
            await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static ResearchFeedItemType MapEventToFeedType(ResearchEventType t) => t switch
    {
        ResearchEventType.TurnStarted => ResearchFeedItemType.UserFollowUp,
        ResearchEventType.RoleStarted or ResearchEventType.RoleSummaryReady or
        ResearchEventType.RoleCompleted or ResearchEventType.RoleFailed => ResearchFeedItemType.RoleMessage,
        ResearchEventType.ToolDispatched or ResearchEventType.ToolProgress or
        ResearchEventType.ToolCompleted => ResearchFeedItemType.ToolEvent,
        ResearchEventType.StageStarted or ResearchEventType.StageCompleted or
        ResearchEventType.StageFailed => ResearchFeedItemType.StageTransition,
        ResearchEventType.DegradedNotice => ResearchFeedItemType.DegradedNotice,
        ResearchEventType.TurnFailed => ResearchFeedItemType.ErrorNotice,
        _ => ResearchFeedItemType.SystemNotice
    };

    private static string? TryExtractStockName(IReadOnlyList<string> outputs)
    {
        foreach (var output in outputs)
        {
            // Output format: "[company_overview_analyst]\n{json}"
            var jsonStart = output.IndexOf('\n');
            if (jsonStart < 0) continue;
            var json = output[(jsonStart + 1)..];
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("companyName", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                {
                    var name = nameEl.GetString();
                    if (!string.IsNullOrWhiteSpace(name)) return name;
                }
                if (doc.RootElement.TryGetProperty("shortName", out var shortEl) && shortEl.ValueKind == JsonValueKind.String)
                {
                    var name = shortEl.GetString();
                    if (!string.IsNullOrWhiteSpace(name)) return name;
                }
            }
            catch (JsonException) { /* not valid JSON, skip */ }
        }
        return null;
    }

    /// <summary>
    /// Extract stock name from MCP tool results stored in OutputRefsJson.
    /// The CompanyOverviewMcp tool returns an envelope with data.name containing the stock name.
    /// </summary>
    private static string? TryExtractStockNameFromToolRefs(string? outputRefsJson)
    {
        if (string.IsNullOrWhiteSpace(outputRefsJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(outputRefsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

            foreach (var refElement in doc.RootElement.EnumerateArray())
            {
                // Match the CompanyOverviewMcp tool
                if (!refElement.TryGetProperty("toolName", out var toolNameEl) ||
                    toolNameEl.GetString() != StockMcpToolNames.CompanyOverview)
                    continue;

                if (!refElement.TryGetProperty("status", out var statusEl) ||
                    !string.Equals(statusEl.GetString(), "Completed", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!refElement.TryGetProperty("resultJson", out var resultJsonEl) ||
                    resultJsonEl.ValueKind != JsonValueKind.String)
                    continue;

                var resultJson = resultJsonEl.GetString();
                if (string.IsNullOrWhiteSpace(resultJson)) continue;

                using var resultDoc = JsonDocument.Parse(resultJson);
                // Navigate: envelope.data.name
                if (resultDoc.RootElement.TryGetProperty("data", out var dataEl) &&
                    dataEl.TryGetProperty("name", out var nameEl) &&
                    nameEl.ValueKind == JsonValueKind.String)
                {
                    var name = nameEl.GetString();
                    if (!string.IsNullOrWhiteSpace(name)) return name;
                }

                // Fallback: top-level "name" in case envelope structure varies
                if (resultDoc.RootElement.TryGetProperty("name", out var topNameEl) &&
                    topNameEl.ValueKind == JsonValueKind.String)
                {
                    var name = topNameEl.GetString();
                    if (!string.IsNullOrWhiteSpace(name)) return name;
                }
            }
        }
        catch (JsonException) { /* malformed JSON, skip */ }
        return null;
    }

    private sealed record StageDefinition(ResearchStageType StageType, ResearchStageExecutionMode ExecutionMode, IReadOnlyList<string> RoleIds);
    private sealed record StageResult(ResearchStageStatus Status, List<string> Outputs, List<string> DegradedFlags);
    private sealed record DebateResult(List<string> Outputs, List<string> DegradedFlags, bool Failed);
    private sealed record ParallelResult(List<string> Outputs, List<string> DegradedFlags, bool Failed);
}
