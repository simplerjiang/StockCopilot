using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface IResearchRunner
{
    Task RunTurnAsync(long turnId, CancellationToken cancellationToken = default);
}

public sealed class ResearchRunner : IResearchRunner
{
    private const int MaxDebateRounds = 3;

    private static readonly IReadOnlyList<StageDefinition> Pipeline =
    [
        new(ResearchStageType.CompanyOverviewPreflight, ResearchStageExecutionMode.Sequential,
            [StockAgentRoleIds.CompanyOverviewAnalyst]),
        new(ResearchStageType.AnalystTeam, ResearchStageExecutionMode.Parallel,
            [StockAgentRoleIds.MarketAnalyst, StockAgentRoleIds.SocialSentimentAnalyst,
             StockAgentRoleIds.NewsAnalyst, StockAgentRoleIds.FundamentalsAnalyst,
             StockAgentRoleIds.ShareholderAnalyst, StockAgentRoleIds.ProductAnalyst]),
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
    private readonly ILogger<ResearchRunner> _logger;
    private string? _positionContext;

    public ResearchRunner(
        AppDbContext dbContext,
        IResearchRoleExecutor roleExecutor,
        IResearchEventBus eventBus,
        IResearchReportService reportService,
        ILogger<ResearchRunner> logger)
    {
        _dbContext = dbContext;
        _roleExecutor = roleExecutor;
        _eventBus = eventBus;
        _reportService = reportService;
        _logger = logger;
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

        // Load user position context for prompt injection
        var position = await _dbContext.StockPositions
            .FirstOrDefaultAsync(p => p.Symbol == session.Symbol, CancellationToken.None);
        _positionContext = position is not null && position.QuantityLots > 0
            ? $"用户当前持仓：{position.QuantityLots} 手，均价 {position.AverageCostPrice:F2} 元"
            : null;

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
            _logger.LogError(ex, "Turn {TurnId} failed", turnId);
            turn.Status = ResearchTurnStatus.Failed;
            turn.StopReason = ex.Message;
            turn.CompletedAt = DateTime.UtcNow;
            session.Status = ResearchSessionStatus.Failed;
            session.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(CancellationToken.None);

            _eventBus.Publish(new ResearchEvent(
                ResearchEventType.TurnFailed, session.Id, turn.Id, null, null, null,
                $"Turn failed: {ex.Message}", null, DateTime.UtcNow));
        }
        finally
        {
            await PersistFeedItemsAsync(turn.Id, CancellationToken.None);
        }
    }

    private async Task<StageResult> RunStageAsync(
        ResearchSession session, ResearchTurn turn,
        StageDefinition stageDef, int stageRunIndex,
        IReadOnlyList<string> upstreamArtifacts,
        CancellationToken cancellationToken)
    {
        var stage = new ResearchStageSnapshot
        {
            TurnId = turn.Id,
            StageType = stageDef.StageType,
            StageRunIndex = stageRunIndex,
            ExecutionMode = stageDef.ExecutionMode,
            Status = ResearchStageStatus.Running,
            ActiveRoleIdsJson = JsonSerializer.Serialize(stageDef.RoleIds),
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
            var pr = await RunParallelAsync(session, turn, stage, stageDef.RoleIds, 0, upstreamArtifacts, cancellationToken);
            outputs.AddRange(pr.Outputs);
            degradedFlags.AddRange(pr.DegradedFlags);
            failed = pr.Failed;
        }
        else
        {
            foreach (var roleId in stageDef.RoleIds)
            {
                var r = await ExecuteAndPersistRoleAsync(session, turn, stage, roleId, 0, upstreamArtifacts, cancellationToken);
                if (r.Status == ResearchRoleStatus.Failed) { failed = true; break; }
                if (r.Status == ResearchRoleStatus.Degraded) degradedFlags.AddRange(r.DegradedFlags);
                if (r.OutputContentJson is not null) outputs.Add($"[{roleId}]\n{r.OutputContentJson}");
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

    private async Task<DebateResult> RunDebateAsync(
        ResearchSession session, ResearchTurn turn, ResearchStageSnapshot stage,
        StageDefinition stageDef, IReadOnlyList<string> upstreamArtifacts,
        CancellationToken cancellationToken)
    {
        var allOutputs = new List<string>();
        var degradedFlags = new List<string>();

        for (var round = 0; round < MaxDebateRounds; round++)
        {
            var roundContext = new List<string>(upstreamArtifacts);
            roundContext.AddRange(allOutputs);

            var rr = await RunParallelAsync(session, turn, stage, stageDef.RoleIds, round, roundContext, cancellationToken);
            if (rr.Failed) return new DebateResult(allOutputs, degradedFlags, true);

            degradedFlags.AddRange(rr.DegradedFlags);
            allOutputs.AddRange(rr.Outputs);

            if (round > 0 && rr.Outputs.Count > 0)
            {
                var converged = rr.Outputs.Any(o =>
                    o.Contains("CONVERGED", StringComparison.OrdinalIgnoreCase) ||
                    o.Contains("收敛", StringComparison.Ordinal) ||
                    o.Contains("\"converged\":true", StringComparison.OrdinalIgnoreCase) ||
                    o.Contains("\"converged\": true", StringComparison.OrdinalIgnoreCase));
                if (converged)
                {
                    _logger.LogInformation("Debate {StageType} converged at round {Round}", stageDef.StageType, round + 1);
                    break;
                }
            }
        }

        return new DebateResult(allOutputs, degradedFlags, false);
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
                turn.UserPrompt, upstreamArtifacts, _positionContext));
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
            turn.UserPrompt, upstreamArtifacts, _positionContext);

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
                Rating = rating, Action = action, ExecutiveSummary = summary,
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
            session.LatestRating = rating;
            session.LatestDecisionHeadline = summary?.Length > 200 ? summary[..200] : summary;
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

    private const int DebateParticipantsPerRound = 3;
    private const int RiskAnalystsPerRound = 3;

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

    private sealed record StageDefinition(ResearchStageType StageType, ResearchStageExecutionMode ExecutionMode, IReadOnlyList<string> RoleIds);
    private sealed record StageResult(ResearchStageStatus Status, List<string> Outputs, List<string> DegradedFlags);
    private sealed record DebateResult(List<string> Outputs, List<string> DegradedFlags, bool Failed);
    private sealed record ParallelResult(List<string> Outputs, List<string> DegradedFlags, bool Failed);
}
