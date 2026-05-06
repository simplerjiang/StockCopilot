using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;
using System.Text.Json;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class ResearchArtifactTests
{
    // ── SplitRoleOutput parser tests ────────────────────────────────────

    [Theory]
    [InlineData("[bull_researcher]\n{\"claim\":\"看多\"}", "bull_researcher", "{\"claim\":\"看多\"}")]
    [InlineData("[trader]\nsome text", "trader", "some text")]
    [InlineData("no brackets here", null, "no brackets here")]
    [InlineData("[]empty", null, "[]empty")]
    public void SplitRoleOutput_ParsesCorrectly(string input, string? expectedRole, string expectedContent)
    {
        var (roleId, content) = ResearchRunner.SplitRoleOutput(input);
        Assert.Equal(expectedRole, roleId);
        Assert.Equal(expectedContent, content);
    }

    // ── Debate artifact persistence via runner ──────────────────────────

    [Fact]
    public async Task Runner_ResearchDebate_PersistsDebateMessagesAndVerdict()
    {
        await using var db = CreateDbContext();
        var (session, turn) = await SeedSessionAndTurn(db);

        // Stubs that produce structured JSON output
        var executor = new FakeRoleExecutor(roleId =>
        {
            if (roleId == StockAgentRoleIds.CompanyOverviewAnalyst)
                return "{\"content\":\"overview data\"}";
            if (roleId == StockAgentRoleIds.BullResearcher)
                return JsonSerializer.Serialize(new { content = JsonSerializer.Serialize(new
                {
                    claim = "Strong growth ahead",
                    supporting_evidence_refs = new[] { "kline_trend", "revenue_growth" },
                    open_questions = new[] { "Will regulation impact margins?" }
                })});
            if (roleId == StockAgentRoleIds.BearResearcher)
                return JsonSerializer.Serialize(new { content = JsonSerializer.Serialize(new
                {
                    claim = "Overvalued at current levels",
                    counter_target_role = "bull_researcher",
                    counter_points = new[] { "PE ratio above sector average" }
                })});
            if (roleId == StockAgentRoleIds.ResearchManager)
                return JsonSerializer.Serialize(new { content = JsonSerializer.Serialize(new
                {
                    claim = "Bull thesis is adopted with caveats",
                    adopted_bull_points = new[] { "Strong growth ahead" },
                    adopted_bear_points = new[] { "PE ratio concern noted" },
                    shelved_disputes = new[] { "Regulation impact" },
                    research_conclusion = "Net positive with CONVERGED view",
                    investment_plan_draft = new { action = "buy", confidence = 0.72 },
                    converged = true
                })});
            // Analyst team roles
            return "{\"content\":\"analyst data\"}";
        });

        var eventBus = new ResearchEventBus();
        var runner = new ResearchRunner(db, executor, eventBus, new NullReportService(), new StubFollowUpRoutingService(), NullLogger<ResearchRunner>.Instance, NullGpuTaskQueue.Instance);

        await runner.RunTurnAsync(turn.Id);

        // Verify debate messages were persisted
        var debateMessages = await db.ResearchDebateMessages
            .Where(d => d.SessionId == session.Id)
            .OrderBy(d => d.CreatedAt)
            .ToListAsync();

        Assert.True(debateMessages.Count >= 3, $"Expected at least 3 debate messages, got {debateMessages.Count}");
        Assert.Contains(debateMessages, m => m.Side == ResearchDebateSide.Bull && m.Claim.Contains("Strong growth"));
        Assert.Contains(debateMessages, m => m.Side == ResearchDebateSide.Bear && m.CounterTargetRole == "bull_researcher");
        Assert.Contains(debateMessages, m => m.Side == ResearchDebateSide.Manager);

        // Verify manager verdict
        var verdicts = await db.ResearchManagerVerdicts
            .Where(v => v.SessionId == session.Id)
            .ToListAsync();

        Assert.NotEmpty(verdicts);
        var verdict = verdicts[0];
        Assert.True(verdict.IsConverged);
        Assert.NotNull(verdict.AdoptedBullPointsJson);
        Assert.NotNull(verdict.AdoptedBearPointsJson);
        Assert.Contains("Strong growth", verdict.AdoptedBullPointsJson);
        Assert.NotNull(verdict.ResearchConclusion);
    }

    // ── Trader proposal persistence ─────────────────────────────────────

    [Fact]
    public async Task Runner_TraderProposal_PersistsVersionedProposal()
    {
        await using var db = CreateDbContext();
        var (session, turn) = await SeedSessionAndTurn(db);

        var executor = new FakeRoleExecutor(roleId =>
        {
            if (roleId == StockAgentRoleIds.Trader)
                return JsonSerializer.Serialize(new { content = JsonSerializer.Serialize(new
                {
                    direction = "long",
                    entry_plan = new { price = "25.50", trigger = "breakout above MA20" },
                    exit_plan = new { stop_loss = "24.00", take_profit = "28.00" },
                    position_sizing = new { max_position_pct = 5 },
                    rationale = "Based on bull thesis with strong technicals"
                })});
            if (roleId == StockAgentRoleIds.ResearchManager)
                return JsonSerializer.Serialize(new { content = JsonSerializer.Serialize(new
                {
                    claim = "CONVERGED", research_conclusion = "CONVERGED", converged = true
                })});
            return "{\"content\":\"data\"}";
        });

        var eventBus = new ResearchEventBus();
        var runner = new ResearchRunner(db, executor, eventBus, new NullReportService(), new StubFollowUpRoutingService(), NullLogger<ResearchRunner>.Instance, NullGpuTaskQueue.Instance);

        await runner.RunTurnAsync(turn.Id);

        var proposals = await db.ResearchTraderProposals
            .Where(p => p.SessionId == session.Id)
            .ToListAsync();

        Assert.Single(proposals);
        var proposal = proposals[0];
        Assert.Equal("long", proposal.Direction);
        Assert.Equal(TraderProposalStatus.Active, proposal.Status);
        Assert.Equal(1, proposal.Version);
        Assert.NotNull(proposal.EntryPlanJson);
        Assert.NotNull(proposal.Rationale);
    }

    // ── Risk assessment persistence ─────────────────────────────────────

    [Fact]
    public async Task Runner_RiskDebate_PersistsThreeTierAssessments()
    {
        await using var db = CreateDbContext();
        var (session, turn) = await SeedSessionAndTurn(db);

        var executor = new FakeRoleExecutor(roleId =>
        {
            if (roleId == StockAgentRoleIds.AggressiveRiskAnalyst)
                return JsonSerializer.Serialize(new { content = JsonSerializer.Serialize(new
                {
                    risk_limits = new[] { "Max loss 3%" },
                    invalidations = new[] { "Below MA60" },
                    proposal_assessment = "accept",
                    analysis = "Risk-reward favorable"
                })});
            if (roleId == StockAgentRoleIds.NeutralRiskAnalyst)
                return JsonSerializer.Serialize(new { content = JsonSerializer.Serialize(new
                {
                    risk_limits = new[] { "Max loss 2%" },
                    proposal_assessment = "modify",
                    analysis = "Reduce position size"
                })});
            if (roleId == StockAgentRoleIds.ConservativeRiskAnalyst)
                return JsonSerializer.Serialize(new { content = JsonSerializer.Serialize(new
                {
                    risk_limits = new[] { "Max loss 1%" },
                    invalidations = new[] { "Any negative news" },
                    proposal_assessment = "reject",
                    analysis = "Too risky in current market CONVERGED"
                })});
            if (roleId == StockAgentRoleIds.ResearchManager)
                return JsonSerializer.Serialize(new { content = JsonSerializer.Serialize(new
                {
                    claim = "CONVERGED", research_conclusion = "CONVERGED", converged = true
                })});
            return "{\"content\":\"data\"}";
        });

        var eventBus = new ResearchEventBus();
        var runner = new ResearchRunner(db, executor, eventBus, new NullReportService(), new StubFollowUpRoutingService(), NullLogger<ResearchRunner>.Instance, NullGpuTaskQueue.Instance);

        await runner.RunTurnAsync(turn.Id);

        var risks = await db.ResearchRiskAssessments
            .Where(r => r.SessionId == session.Id)
            .OrderBy(r => r.Tier)
            .ToListAsync();

        Assert.True(risks.Count >= 3, $"Expected at least 3 risk assessments, got {risks.Count}");
        Assert.Contains(risks, r => r.Tier == RiskAnalystTier.Aggressive && r.ProposalAssessment == "accept");
        Assert.Contains(risks, r => r.Tier == RiskAnalystTier.Neutral && r.ProposalAssessment == "modify");
        Assert.Contains(risks, r => r.Tier == RiskAnalystTier.Conservative && r.ProposalAssessment == "reject");
    }

    // ── Proposal version supersession ───────────────────────────────────

    [Fact]
    public async Task Runner_SecondTurn_SupersedesPriorProposal()
    {
        await using var db = CreateDbContext();
        var (session, turn1) = await SeedSessionAndTurn(db);

        var proposalContent = JsonSerializer.Serialize(new { content = JsonSerializer.Serialize(new
        {
            direction = "long", rationale = "first attempt"
        })});
        var executor = new FakeRoleExecutor(roleId =>
        {
            if (roleId == StockAgentRoleIds.Trader) return proposalContent;
            if (roleId == StockAgentRoleIds.ResearchManager)
                return JsonSerializer.Serialize(new { content = "{\"claim\":\"CONVERGED\",\"converged\":true}" });
            return "{\"content\":\"data\"}";
        });

        var eventBus = new ResearchEventBus();
        var runner = new ResearchRunner(db, executor, eventBus, new NullReportService(), new StubFollowUpRoutingService(), NullLogger<ResearchRunner>.Instance, NullGpuTaskQueue.Instance);
        await runner.RunTurnAsync(turn1.Id);

        // Create second turn
        var turn2 = new ResearchTurn
        {
            SessionId = session.Id, TurnIndex = 1, UserPrompt = "update plan",
            Status = ResearchTurnStatus.Queued, ContinuationMode = ResearchContinuationMode.PartialRerun,
            RerunScope = "3",
            RequestedAt = DateTime.UtcNow
        };
        db.ResearchTurns.Add(turn2);
        session.ActiveTurnId = turn2.Id;
        session.Status = ResearchSessionStatus.Idle;
        await db.SaveChangesAsync();

        var proposal2Content = JsonSerializer.Serialize(new { content = JsonSerializer.Serialize(new
        {
            direction = "short", rationale = "revised view"
        })});
        var executor2 = new FakeRoleExecutor(roleId =>
        {
            if (roleId == StockAgentRoleIds.Trader) return proposal2Content;
            if (roleId == StockAgentRoleIds.ResearchManager)
                return JsonSerializer.Serialize(new { content = "{\"claim\":\"CONVERGED\",\"converged\":true}" });
            return "{\"content\":\"data\"}";
        });
        var runner2 = new ResearchRunner(db, executor2, eventBus, new NullReportService(), new StubFollowUpRoutingService(), NullLogger<ResearchRunner>.Instance, NullGpuTaskQueue.Instance);
        await runner2.RunTurnAsync(turn2.Id);

        var proposals = await db.ResearchTraderProposals
            .Where(p => p.SessionId == session.Id)
            .OrderBy(p => p.Version)
            .ToListAsync();

        Assert.Equal(2, proposals.Count);
        Assert.Equal(TraderProposalStatus.Superseded, proposals[0].Status);
        Assert.Equal(TraderProposalStatus.Active, proposals[1].Status);
        Assert.Equal(2, proposals[1].Version);
        // CRITICAL fix: SupersededByProposalId must be set
        Assert.Equal(proposals[1].Id, proposals[0].SupersededByProposalId);
    }

    // ── Artifact query service ──────────────────────────────────────────

    [Fact]
    public async Task ArtifactService_GetTurnArtifacts_ReturnsAllTypes()
    {
        await using var db = CreateDbContext();
        var (session, turn) = await SeedSessionAndTurn(db);

        var stage = new ResearchStageSnapshot
        {
            TurnId = turn.Id, StageType = ResearchStageType.ResearchDebate,
            ExecutionMode = ResearchStageExecutionMode.Debate,
            Status = ResearchStageStatus.Completed, StageRunIndex = 0,
            StartedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow
        };
        db.ResearchStageSnapshots.Add(stage);
        await db.SaveChangesAsync();

        db.ResearchDebateMessages.Add(new ResearchDebateMessage
        {
            SessionId = session.Id, TurnId = turn.Id, StageId = stage.Id,
            Side = ResearchDebateSide.Bull, RoleId = "bull_researcher",
            RoundIndex = 0, Claim = "test claim", CreatedAt = DateTime.UtcNow
        });
        db.ResearchManagerVerdicts.Add(new ResearchManagerVerdict
        {
            SessionId = session.Id, TurnId = turn.Id, StageId = stage.Id,
            RoundIndex = 0, ResearchConclusion = "test conclusion",
            IsConverged = true, CreatedAt = DateTime.UtcNow
        });
        db.ResearchTraderProposals.Add(new ResearchTraderProposal
        {
            SessionId = session.Id, TurnId = turn.Id, StageId = stage.Id,
            Version = 1, Status = TraderProposalStatus.Active,
            Direction = "long", CreatedAt = DateTime.UtcNow
        });
        db.ResearchRiskAssessments.Add(new ResearchRiskAssessment
        {
            SessionId = session.Id, TurnId = turn.Id, StageId = stage.Id,
            RoleId = "aggressive_risk_analyst", Tier = RiskAnalystTier.Aggressive,
            RoundIndex = 0, ProposalAssessment = "accept", CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new ResearchArtifactService(db);
        var artifacts = await service.GetTurnArtifactsAsync(turn.Id);

        Assert.NotNull(artifacts);
        Assert.Single(artifacts.DebateMessages);
        Assert.Single(artifacts.ManagerVerdicts);
        Assert.Single(artifacts.TraderProposals);
        Assert.Single(artifacts.RiskAssessments);
        Assert.Equal("test claim", artifacts.DebateMessages[0].Claim);
        Assert.True(artifacts.ManagerVerdicts[0].IsConverged);
        Assert.Equal("long", artifacts.TraderProposals[0].Direction);
        Assert.Equal("accept", artifacts.RiskAssessments[0].ProposalAssessment);
    }

    [Fact]
    public async Task ArtifactService_GetTurnArtifacts_ReturnsNullForMissingTurn()
    {
        await using var db = CreateDbContext();
        var service = new ResearchArtifactService(db);
        var result = await service.GetTurnArtifactsAsync(999);
        Assert.Null(result);
    }

    [Fact]
    public async Task ArtifactService_GetDebateHistory_ReturnsOrderedAcrossTurns()
    {
        await using var db = CreateDbContext();
        var (session, turn) = await SeedSessionAndTurn(db);
        var stage = new ResearchStageSnapshot
        {
            TurnId = turn.Id, StageType = ResearchStageType.ResearchDebate,
            ExecutionMode = ResearchStageExecutionMode.Debate,
            Status = ResearchStageStatus.Completed, StageRunIndex = 0,
            StartedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow
        };
        db.ResearchStageSnapshots.Add(stage);
        await db.SaveChangesAsync();

        db.ResearchDebateMessages.Add(new ResearchDebateMessage
        {
            SessionId = session.Id, TurnId = turn.Id, StageId = stage.Id,
            Side = ResearchDebateSide.Bull, RoleId = "bull_researcher",
            RoundIndex = 0, Claim = "round 0 bull", CreatedAt = DateTime.UtcNow
        });
        db.ResearchDebateMessages.Add(new ResearchDebateMessage
        {
            SessionId = session.Id, TurnId = turn.Id, StageId = stage.Id,
            Side = ResearchDebateSide.Bear, RoleId = "bear_researcher",
            RoundIndex = 1, Claim = "round 1 bear", CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new ResearchArtifactService(db);
        var debates = await service.GetDebateHistoryAsync(session.Id);

        Assert.Equal(2, debates.Count);
        Assert.Equal(0, debates[0].RoundIndex);
        Assert.Equal(1, debates[1].RoundIndex);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static async Task<(ResearchSession Session, ResearchTurn Turn)> SeedSessionAndTurn(AppDbContext db)
    {
        var session = new ResearchSession
        {
            SessionKey = Guid.NewGuid().ToString("N"), Symbol = "sz000001",
            Name = "Test", Status = ResearchSessionStatus.Idle,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.ResearchSessions.Add(session);
        await db.SaveChangesAsync();

        var turn = new ResearchTurn
        {
            SessionId = session.Id, TurnIndex = 0, UserPrompt = "分析该股票",
            Status = ResearchTurnStatus.Queued,
            ContinuationMode = ResearchContinuationMode.NewSession,
            RequestedAt = DateTime.UtcNow
        };
        db.ResearchTurns.Add(turn);
        await db.SaveChangesAsync();

        session.ActiveTurnId = turn.Id;
        await db.SaveChangesAsync();

        return (session, turn);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options);
    }

    /// <summary>Fake executor that returns configurable JSON per roleId.</summary>
    private sealed class FakeRoleExecutor : IResearchRoleExecutor
    {
        private readonly Func<string, string> _outputFactory;

        public FakeRoleExecutor(Func<string, string> outputFactory) => _outputFactory = outputFactory;

        public Task<RoleExecutionResult> ExecuteRoleAsync(RoleExecutionContext context, CancellationToken cancellationToken = default)
        {
            var output = _outputFactory(context.RoleId);
            return Task.FromResult(new RoleExecutionResult(
                context.RoleId, ResearchRoleStatus.Completed, output,
                $"trace-{context.RoleId}", Array.Empty<string>(), null, null));
        }
    }

    /// <summary>No-op report service for tests that don't need report generation.</summary>
    private sealed class NullReportService : IResearchReportService
    {
        public Task GenerateBlocksFromStageAsync(long sessionId, long turnId, ResearchStageType stageType,
            IReadOnlyList<string> outputs, IReadOnlyList<string> degradedFlags, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<ResearchTurnReportDto?> GetTurnReportAsync(long turnId, CancellationToken ct = default)
            => Task.FromResult<ResearchTurnReportDto?>(null);
        public Task<ResearchFinalDecisionDto?> GetFinalDecisionAsync(long turnId, CancellationToken ct = default)
            => Task.FromResult<ResearchFinalDecisionDto?>(null);
    }
}
