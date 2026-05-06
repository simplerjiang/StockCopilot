using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;
using System.Text.Json;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class ResearchReportTests
{
    // ── Report block generation from AnalystTeam stage ───────────────

    [Fact]
    public async Task GenerateBlocks_CompanyOverviewStage_CreatesCoverageBlock()
    {
        await using var db = CreateDbContext();
        var (session, turn) = await SeedSessionAndTurn(db);

        var stage = new ResearchStageSnapshot
        {
            TurnId = turn.Id,
            StageType = ResearchStageType.CompanyOverviewPreflight,
            StageRunIndex = 0,
            ExecutionMode = ResearchStageExecutionMode.Sequential,
            Status = ResearchStageStatus.Completed,
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };
        db.ResearchStageSnapshots.Add(stage);
        await db.SaveChangesAsync();

        var toolRefs = JsonSerializer.Serialize(new[]
        {
            new
            {
                toolName = StockMcpToolNames.CompanyOverview,
                status = "Completed",
                summary = "company",
                resultJson = JsonSerializer.Serialize(new
                {
                    data = new
                    {
                        name = "平安银行",
                        sectorName = "银行",
                        mainBusiness = "银行业务",
                        businessScope = "公司金融、零售金融",
                        price = 12.3,
                        changePercent = 1.5,
                        floatMarketCap = 1000000000,
                        peRatio = 8.2,
                        volumeRatio = 1.8,
                        shareholderCount = 123456,
                        quoteTimestamp = "2026-03-28 10:00:00"
                    }
                }),
                errorMessage = (string?)null,
                degradedFlags = Array.Empty<string>()
            },
            new
            {
                toolName = StockMcpToolNames.MarketContext,
                status = "Completed",
                summary = "market",
                resultJson = JsonSerializer.Serialize(new
                {
                    data = new
                    {
                        stockSectorName = "银行",
                        mainlineSectorName = "金融",
                        stageConfidence = 78
                    }
                }),
                errorMessage = (string?)null,
                degradedFlags = Array.Empty<string>()
            }
        });

        db.ResearchRoleStates.Add(new ResearchRoleState
        {
            StageId = stage.Id,
            RoleId = StockAgentRoleIds.CompanyOverviewAnalyst,
            RunIndex = 0,
            Status = ResearchRoleStatus.Completed,
            OutputRefsJson = toolRefs,
            OutputContentJson = JsonSerializer.Serialize(new { content = JsonSerializer.Serialize(new { summary = "公司基础画像完整" }) }),
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new ResearchReportService(db);
        var wrappedOutput = JsonSerializer.Serialize(new { content = JsonSerializer.Serialize(new { summary = "公司基础画像完整" }) });
        await service.GenerateBlocksFromStageAsync(
            session.Id,
            turn.Id,
            ResearchStageType.CompanyOverviewPreflight,
            new[] { $"[{StockAgentRoleIds.CompanyOverviewAnalyst}]\n{wrappedOutput}" },
            Array.Empty<string>());

        var block = await db.ResearchReportBlocks.FirstOrDefaultAsync(b => b.TurnId == turn.Id && b.BlockType == ReportBlockType.CompanyOverview);
        Assert.NotNull(block);
        var keyPoints = JsonSerializer.Deserialize<string[]>(block!.KeyPointsJson ?? "[]") ?? Array.Empty<string>();
        Assert.Contains("数据覆盖", block!.Summary);
        Assert.Contains(keyPoints, item => item.Contains("量比", StringComparison.Ordinal));
        Assert.Contains("平安银行", block.Headline);
    }

    [Fact]
    public async Task GenerateBlocks_AnalystTeam_CreatesMarketSocialNewsFundamentalsBlocks()
    {
        await using var db = CreateDbContext();
        var (session, turn) = await SeedSessionAndTurn(db);

        var service = new ResearchReportService(db);

        var outputs = new List<string>
        {
            $"[{StockAgentRoleIds.MarketAnalyst}]\n" + JsonSerializer.Serialize(new
            {
                headline = "市场趋势偏多",
                summary = "近期均线多头排列",
                key_points = new[] { "MA20上穿MA60", "成交量放大" },
                evidence_refs = new[] { "kline_trend", "volume" }
            }),
            $"[{StockAgentRoleIds.SocialSentimentAnalyst}]\n" + JsonSerializer.Serialize(new
            {
                headline = "社交情绪中性偏多",
                summary = "股吧讨论活跃度上升"
            }),
            $"[{StockAgentRoleIds.NewsAnalyst}]\n" + JsonSerializer.Serialize(new
            {
                headline = "近期利好消息",
                summary = "公司发布正面业绩预告",
                key_points = new[] { "Q3净利润同比增长30%" }
            }),
            $"[{StockAgentRoleIds.FundamentalsAnalyst}]\n" + JsonSerializer.Serialize(new
            {
                headline = "基本面稳健",
                summary = "PE低于行业均值",
                evidence_refs = new[] { "pe_ratio", "revenue_growth" }
            })
        };

        await service.GenerateBlocksFromStageAsync(
            session.Id, turn.Id, ResearchStageType.AnalystTeam,
            outputs, Array.Empty<string>());

        var blocks = await db.ResearchReportBlocks
            .Where(b => b.TurnId == turn.Id)
            .OrderBy(b => b.BlockType)
            .ToListAsync();

        Assert.Equal(4, blocks.Count);
        Assert.Contains(blocks, b => b.BlockType == ReportBlockType.Market && b.Headline == "市场趋势偏多");
        Assert.Contains(blocks, b => b.BlockType == ReportBlockType.Social && b.Headline == "社交情绪中性偏多");
        Assert.Contains(blocks, b => b.BlockType == ReportBlockType.News && b.Headline == "近期利好消息");
        Assert.Contains(blocks, b => b.BlockType == ReportBlockType.Fundamentals && b.Headline == "基本面稳健");

        var marketBlock = blocks.First(b => b.BlockType == ReportBlockType.Market);
        Assert.Equal(ReportBlockStatus.Complete, marketBlock.Status);
        Assert.NotNull(marketBlock.KeyPointsJson);
        Assert.NotNull(marketBlock.EvidenceRefsJson);
    }

    // ── Fundamentals summary synthesis from qualityView/valuationView ──

    [Fact]
    public async Task GenerateBlocks_FundamentalsWithOnlyValuationView_ShouldNotCrash()
    {
        await using var db = CreateDbContext();
        var (session, turn) = await SeedSessionAndTurn(db);

        var service = new ResearchReportService(db);

        // Simulate fundamentals analyst returning only valuationView (no qualityView)
        var outputs = new List<string>
        {
            $"[{StockAgentRoleIds.FundamentalsAnalyst}]\n" + JsonSerializer.Serialize(new
            {
                headline = "估值偏低",
                valuationView = "当前PE低于历史中位数",
                highlights = new[] { "ROE持续改善", "毛利率稳定" },
                risks = new[] { "行业竞争加剧" },
                evidenceTable = new[] { new { metric = "PE", value = "8.5" } }
            })
        };

        await service.GenerateBlocksFromStageAsync(
            session.Id, turn.Id, ResearchStageType.AnalystTeam,
            outputs, Array.Empty<string>());

        var block = await db.ResearchReportBlocks.FirstOrDefaultAsync(b => b.TurnId == turn.Id && b.BlockType == ReportBlockType.Fundamentals);

        Assert.NotNull(block);
        Assert.Contains("估值判断", block!.Summary);
        Assert.DoesNotContain("{\"content\"", block.Summary ?? "");
        Assert.NotNull(block.KeyPointsJson);
        Assert.NotNull(block.CounterEvidenceRefsJson);
        Assert.NotNull(block.EvidenceRefsJson);
    }

    [Fact]
    public async Task GenerateBlocks_FundamentalsWithBothViews_SynthesizesSummary()
    {
        await using var db = CreateDbContext();
        var (session, turn) = await SeedSessionAndTurn(db);

        var service = new ResearchReportService(db);

        var outputs = new List<string>
        {
            $"[{StockAgentRoleIds.FundamentalsAnalyst}]\n" + JsonSerializer.Serialize(new
            {
                headline = "基本面良好",
                qualityView = "财务质量优秀，盈利能力强",
                valuationView = "估值合理偏低",
                metrics = new { PE = "8.5", PB = "1.2" },
                highlights = new[] { "ROE持续改善" },
                risks = new[] { "行业竞争加剧" }
            })
        };

        await service.GenerateBlocksFromStageAsync(
            session.Id, turn.Id, ResearchStageType.AnalystTeam,
            outputs, Array.Empty<string>());

        var block = await db.ResearchReportBlocks.FirstOrDefaultAsync(b => b.TurnId == turn.Id && b.BlockType == ReportBlockType.Fundamentals);

        Assert.NotNull(block);
        Assert.Contains("财务质量", block!.Summary);
        Assert.Contains("估值判断", block.Summary);
    }

    // ── Report block for degraded stage ──────────────────────────────

    [Fact]
    public async Task GenerateBlocks_DegradedStage_MarksBlockAsDegraded()
    {
        await using var db = CreateDbContext();
        var (session, turn) = await SeedSessionAndTurn(db);

        var service = new ResearchReportService(db);

        var outputs = new List<string>
        {
            $"[{StockAgentRoleIds.MarketAnalyst}]\n" + JsonSerializer.Serialize(new { headline = "市场数据", summary = "部分数据缺失" })
        };
        var degradedFlags = new List<string> { "market_analyst: tool_timeout" };

        await service.GenerateBlocksFromStageAsync(
            session.Id, turn.Id, ResearchStageType.AnalystTeam,
            outputs, degradedFlags);

        var block = await db.ResearchReportBlocks.FirstOrDefaultAsync(b => b.TurnId == turn.Id);

        Assert.NotNull(block);
        Assert.Equal(ReportBlockStatus.Degraded, block!.Status);
        Assert.NotNull(block.DegradedFlagsJson);
        Assert.Contains("market_analyst", block.DegradedFlagsJson);
    }

    // ── Debate block generation ──────────────────────────────────────

    [Fact]
    public async Task GenerateBlocks_ResearchDebate_CreatesDebateBlock()
    {
        await using var db = CreateDbContext();
        var (session, turn) = await SeedSessionAndTurn(db);

        var service = new ResearchReportService(db);

        var outputs = new List<string>
        {
            $"[{StockAgentRoleIds.BullResearcher}]\n" + JsonSerializer.Serialize(new { headline = "看多论据", summary = "技术面支持上涨" }),
            $"[{StockAgentRoleIds.BearResearcher}]\n" + JsonSerializer.Serialize(new { headline = "看空论据", summary = "估值偏高" }),
            $"[{StockAgentRoleIds.ResearchManager}]\n" + JsonSerializer.Serialize(new { headline = "裁决结论", summary = "采纳多方观点，关注风险" })
        };

        await service.GenerateBlocksFromStageAsync(
            session.Id, turn.Id, ResearchStageType.ResearchDebate,
            outputs, Array.Empty<string>());

        var block = await db.ResearchReportBlocks.FirstOrDefaultAsync(b => b.TurnId == turn.Id);

        Assert.NotNull(block);
        Assert.Equal(ReportBlockType.ResearchDebate, block!.BlockType);
        Assert.Equal("裁决结论", block.Headline);
        Assert.Equal(ReportBlockStatus.Complete, block.Status);
    }

    // ── TraderProposal block generation ──────────────────────────────

    [Fact]
    public async Task GenerateBlocks_TraderProposal_CreatesProposalBlock()
    {
        await using var db = CreateDbContext();
        var (session, turn) = await SeedSessionAndTurn(db);

        var service = new ResearchReportService(db);

        var outputs = new List<string>
        {
            $"[{StockAgentRoleIds.Trader}]\n" + JsonSerializer.Serialize(new
            {
                headline = "做多方案",
                summary = "建议分批建仓",
                risk_limits = new[] { "最大亏损3%" }
            })
        };

        await service.GenerateBlocksFromStageAsync(
            session.Id, turn.Id, ResearchStageType.TraderProposal,
            outputs, Array.Empty<string>());

        var block = await db.ResearchReportBlocks.FirstOrDefaultAsync(b => b.TurnId == turn.Id);

        Assert.NotNull(block);
        Assert.Equal(ReportBlockType.TraderProposal, block!.BlockType);
        Assert.Equal("做多方案", block.Headline);
        Assert.NotNull(block.RiskLimitsJson);
    }

    // ── RiskReview block generation ──────────────────────────────────

    [Fact]
    public async Task GenerateBlocks_RiskDebate_CreatesRiskReviewBlock()
    {
        await using var db = CreateDbContext();
        var (session, turn) = await SeedSessionAndTurn(db);

        var service = new ResearchReportService(db);

        var outputs = new List<string>
        {
            $"[{StockAgentRoleIds.AggressiveRiskAnalyst}]\n" + JsonSerializer.Serialize(new
            {
                risk_limits = new[] { "最大亏损3%" },
                invalidations = new[] { "跌破MA60" }
            }),
            $"[{StockAgentRoleIds.NeutralRiskAnalyst}]\n" + JsonSerializer.Serialize(new
            {
                risk_limits = new[] { "最大亏损2%" }
            }),
            $"[{StockAgentRoleIds.ConservativeRiskAnalyst}]\n" + JsonSerializer.Serialize(new
            {
                summary = "风险过高，建议观望",
                risk_limits = new[] { "最大亏损1%" },
                invalidations = new[] { "任何负面新闻" }
            })
        };

        await service.GenerateBlocksFromStageAsync(
            session.Id, turn.Id, ResearchStageType.RiskDebate,
            outputs, Array.Empty<string>());

        var block = await db.ResearchReportBlocks.FirstOrDefaultAsync(b => b.TurnId == turn.Id);

        Assert.NotNull(block);
        Assert.Equal(ReportBlockType.RiskReview, block!.BlockType);
        Assert.NotNull(block.RiskLimitsJson);
        Assert.NotNull(block.InvalidationsJson);
    }

    // ── PortfolioDecision block generation ───────────────────────────

    [Fact]
    public async Task GenerateBlocks_PortfolioDecision_CreatesDecisionBlock()
    {
        await using var db = CreateDbContext();
        var (session, turn) = await SeedSessionAndTurn(db);

        var service = new ResearchReportService(db);

        var outputs = new List<string>
        {
            $"[{StockAgentRoleIds.PortfolioManager}]\n" + JsonSerializer.Serialize(new
            {
                headline = "投资决策：看好",
                executive_summary = "综合分析后建议买入",
                supporting_evidence = new[] { "均线多头", "业绩增长" },
                counter_evidence = new[] { "估值偏高" },
                risk_limits = new[] { "止损3%" },
                invalidations = new[] { "业绩不及预期" }
            })
        };

        await service.GenerateBlocksFromStageAsync(
            session.Id, turn.Id, ResearchStageType.PortfolioDecision,
            outputs, Array.Empty<string>());

        var block = await db.ResearchReportBlocks.FirstOrDefaultAsync(b => b.TurnId == turn.Id);

        Assert.NotNull(block);
        Assert.Equal(ReportBlockType.PortfolioDecision, block!.BlockType);
        Assert.Equal("投资决策：看好", block.Headline);
        Assert.NotNull(block.EvidenceRefsJson);
        Assert.NotNull(block.CounterEvidenceRefsJson);
        Assert.NotNull(block.RiskLimitsJson);
    }

    // ── GetTurnReport aggregate query ────────────────────────────────

    [Fact]
    public async Task GetTurnReport_ReturnsAllBlocksAndDecision()
    {
        await using var db = CreateDbContext();
        var (session, turn) = await SeedSessionAndTurn(db);

        // Seed blocks
        db.ResearchReportBlocks.Add(new ResearchReportBlock
        {
            SessionId = session.Id, TurnId = turn.Id,
            BlockType = ReportBlockType.Market, VersionIndex = 0,
            Headline = "Market", Status = ReportBlockStatus.Complete,
            SourceStageType = "AnalystTeam",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        db.ResearchReportBlocks.Add(new ResearchReportBlock
        {
            SessionId = session.Id, TurnId = turn.Id,
            BlockType = ReportBlockType.PortfolioDecision, VersionIndex = 0,
            Headline = "Decision", Status = ReportBlockStatus.Complete,
            SourceStageType = "PortfolioDecision",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        db.ResearchDecisionSnapshots.Add(new ResearchDecisionSnapshot
        {
            SessionId = session.Id, TurnId = turn.Id,
            Rating = "buy", Action = "Buy", ExecutiveSummary = "Test",
            Confidence = 0.8m, CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new ResearchReportService(db);
        var report = await service.GetTurnReportAsync(turn.Id);

        Assert.NotNull(report);
        Assert.Equal(2, report!.Blocks.Count);
        Assert.NotNull(report.FinalDecision);
        Assert.Equal("buy", report.FinalDecision!.Rating);
    }

    [Fact]
    public async Task GetTurnReport_ReturnsNull_ForMissingTurn()
    {
        await using var db = CreateDbContext();
        var service = new ResearchReportService(db);
        var result = await service.GetTurnReportAsync(999);
        Assert.Null(result);
    }

    // ── NextAction default generation ────────────────────────────────

    [Fact]
    public void BuildNextActions_NoStoredActions_GeneratesDefaults()
    {
        var snapshot = new ResearchDecisionSnapshot
        {
            Id = 1, SessionId = 10, TurnId = 20,
            Rating = "buy", CreatedAt = DateTime.UtcNow
        };

        var actions = ResearchReportService.BuildNextActionsFromDecision(snapshot);

        Assert.True(actions.Count >= 4, $"Expected at least 4 default actions, got {actions.Count}");
        Assert.Contains(actions, a => a.ActionType == nameof(NextActionType.ViewDailyChart));
        Assert.Contains(actions, a => a.ActionType == nameof(NextActionType.ViewMinuteChart));
        Assert.Contains(actions, a => a.ActionType == nameof(NextActionType.DraftTradingPlan));
        Assert.Contains(actions, a => a.ActionType == nameof(NextActionType.ViewEvidence));
        Assert.Contains(actions, a => a.ActionType == nameof(NextActionType.ViewLocalFacts));
        Assert.All(actions, a => Assert.Equal(10L, a.SessionId));
        Assert.All(actions, a => Assert.Equal(20L, a.TurnId));
    }

    [Fact]
    public void BuildNextActions_HoldRating_NoDraftTradingPlan()
    {
        var snapshot = new ResearchDecisionSnapshot
        {
            Id = 1, SessionId = 10, TurnId = 20,
            Rating = "hold", CreatedAt = DateTime.UtcNow
        };

        var actions = ResearchReportService.BuildNextActionsFromDecision(snapshot);

        Assert.DoesNotContain(actions, a => a.ActionType == nameof(NextActionType.DraftTradingPlan));
    }

    [Fact]
    public void BuildNextActions_WithStoredActions_ParsesFromJson()
    {
        var storedActions = JsonSerializer.Serialize(new[]
        {
            new { action_type = "ViewDailyChart", label = "查看K线", target_surface = "chart", reason = "确认趋势" }
        });

        var snapshot = new ResearchDecisionSnapshot
        {
            Id = 1, SessionId = 10, TurnId = 20,
            NextActionsJson = storedActions, CreatedAt = DateTime.UtcNow
        };

        var actions = ResearchReportService.BuildNextActionsFromDecision(snapshot);

        Assert.Single(actions);
        Assert.Equal("ViewDailyChart", actions[0].ActionType);
        Assert.Equal("查看K线", actions[0].Label);
        Assert.Equal("chart", actions[0].TargetSurface);
        Assert.Equal("确认趋势", actions[0].ReasonSummary);
    }

    // ── Full pipeline report generation via runner ───────────────────

    [Fact]
    public async Task Runner_FullPipeline_GeneratesReportBlocks()
    {
        await using var db = CreateDbContext();
        var (session, turn) = await SeedSessionAndTurn(db);

        var executor = new FakeRoleExecutor(roleId =>
        {
            if (roleId == StockAgentRoleIds.MarketAnalyst)
                return JsonSerializer.Serialize(new { headline = "市场分析", summary = "趋势向好" });
            if (roleId == StockAgentRoleIds.SocialSentimentAnalyst)
                return JsonSerializer.Serialize(new { headline = "社交情绪", summary = "中性" });
            if (roleId == StockAgentRoleIds.NewsAnalyst)
                return JsonSerializer.Serialize(new { headline = "新闻动态", summary = "利好出尽" });
            if (roleId == StockAgentRoleIds.FundamentalsAnalyst)
                return JsonSerializer.Serialize(new { headline = "基本面", summary = "稳健" });
            if (roleId == StockAgentRoleIds.ResearchManager)
                return JsonSerializer.Serialize(new { content = JsonSerializer.Serialize(new
                {
                    claim = "CONVERGED", research_conclusion = "CONVERGED", converged = true
                })});
            if (roleId == StockAgentRoleIds.Trader)
                return JsonSerializer.Serialize(new { content = JsonSerializer.Serialize(new
                {
                    direction = "long", rationale = "基于多方共识"
                })});
            if (roleId == StockAgentRoleIds.PortfolioManager)
                return JsonSerializer.Serialize(new { content = JsonSerializer.Serialize(new
                {
                    rating = "buy", executive_summary = "综合研判买入",
                    confidence = 0.8, headline = "建议买入"
                })});
            return JsonSerializer.Serialize(new { content = "data" });
        });

        var eventBus = new ResearchEventBus();
        var reportService = new ResearchReportService(db);
        var runner = new ResearchRunner(db, executor, eventBus, reportService, new StubFollowUpRoutingService(), NullLogger<ResearchRunner>.Instance, NullGpuTaskQueue.Instance);

        await runner.RunTurnAsync(turn.Id);

        // Verify report blocks were generated
        var blocks = await db.ResearchReportBlocks
            .Where(b => b.TurnId == turn.Id)
            .ToListAsync();

        // Should have blocks from AnalystTeam (4), ResearchDebate (1), TraderProposal (1),
        // RiskDebate (1), PortfolioDecision (1) = at least 4+1+1+1+1=8
        Assert.True(blocks.Count >= 4, $"Expected at least 4 report blocks, got {blocks.Count}");
        Assert.Contains(blocks, b => b.BlockType == ReportBlockType.Market);

        // Verify decision snapshot was also created
        var decision = await db.ResearchDecisionSnapshots
            .FirstOrDefaultAsync(d => d.TurnId == turn.Id);
        Assert.NotNull(decision);
        Assert.Equal("buy", decision!.Rating);
    }

    // ── Enhanced decision fields ─────────────────────────────────────

    [Fact]
    public async Task Runner_PortfolioDecision_ExtractsEnhancedFields()
    {
        await using var db = CreateDbContext();
        var (session, turn) = await SeedSessionAndTurn(db);

        var executor = new FakeRoleExecutor(roleId =>
        {
            if (roleId == StockAgentRoleIds.PortfolioManager)
                return JsonSerializer.Serialize(new { content = JsonSerializer.Serialize(new
                {
                    rating = "strong_buy",
                    action = "Buy",
                    executive_summary = "明确买入信号",
                    investment_thesis = "基于技术+基本面双重验证",
                    confidence = 0.9,
                    confidence_explanation = "高证据覆盖度，低冲突强度",
                    supporting_evidence = new[] { "均线多头", "业绩增长" },
                    counter_evidence = new[] { "估值略高" },
                    risk_consensus = "三方共识：可控风险",
                    dissent = new[] { "保守派建议减仓" },
                    next_actions = new[]
                    {
                        new { action_type = "ViewDailyChart", label = "看K线", target_surface = "chart" }
                    },
                    invalidation_conditions = new[] { "跌破MA60" }
                })});
            if (roleId == StockAgentRoleIds.ResearchManager)
                return JsonSerializer.Serialize(new { content = JsonSerializer.Serialize(new
                {
                    claim = "CONVERGED", research_conclusion = "CONVERGED", converged = true
                })});
            return JsonSerializer.Serialize(new { content = "data" });
        });

        var eventBus = new ResearchEventBus();
        var reportService = new ResearchReportService(db);
        var runner = new ResearchRunner(db, executor, eventBus, reportService, new StubFollowUpRoutingService(), NullLogger<ResearchRunner>.Instance, NullGpuTaskQueue.Instance);

        await runner.RunTurnAsync(turn.Id);

        var decision = await db.ResearchDecisionSnapshots
            .FirstOrDefaultAsync(d => d.TurnId == turn.Id);

        Assert.NotNull(decision);
        Assert.Equal("strong_buy", decision!.Rating);
        Assert.Equal("基于技术+基本面双重验证", decision.InvestmentThesis);
        Assert.Equal("高证据覆盖度，低冲突强度", decision.ConfidenceExplanation);
        Assert.NotNull(decision.SupportingEvidenceJson);
        Assert.NotNull(decision.CounterEvidenceJson);
        Assert.NotNull(decision.RiskConsensus);
        Assert.NotNull(decision.DissentJson);
        Assert.NotNull(decision.NextActionsJson);
        Assert.NotNull(decision.InvalidationConditionsJson);
    }

    // ── GetFinalDecision with NextActions ────────────────────────────

    [Fact]
    public async Task GetFinalDecision_IncludesStructuredNextActions()
    {
        await using var db = CreateDbContext();
        var (session, turn) = await SeedSessionAndTurn(db);

        var nextActions = JsonSerializer.Serialize(new[]
        {
            new { action_type = "DraftTradingPlan", label = "起草交易计划", target_surface = "trading-plan", reason = "执行买入决策" }
        });

        db.ResearchDecisionSnapshots.Add(new ResearchDecisionSnapshot
        {
            SessionId = session.Id, TurnId = turn.Id,
            Rating = "buy", Confidence = 0.85m,
            NextActionsJson = nextActions,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new ResearchReportService(db);
        var decision = await service.GetFinalDecisionAsync(turn.Id);

        Assert.NotNull(decision);
        Assert.Equal("buy", decision!.Rating);
        Assert.Single(decision.NextActions);
        Assert.Equal("DraftTradingPlan", decision.NextActions[0].ActionType);
        Assert.Equal("起草交易计划", decision.NextActions[0].Label);
    }

    // ── Fundamentals qualityView/valuationView parsing ─────────────

    [Fact]
    public async Task GenerateBlocks_FundamentalsWithQualityAndValuationView_ShouldSynthesizeSummary()
    {
        await using var db = CreateDbContext();
        var (session, turn) = await SeedSessionAndTurn(db);

        var service = new ResearchReportService(db);

        var fundamentalsJson = JsonSerializer.Serialize(new
        {
            headline = "基本面承压",
            qualityView = "承压",
            valuationView = "偏贵",
            metrics = new { revenue = "100亿", revenueYoY = "-5%", netProfit = "8亿", roe = "6%" },
            highlights = new[] { "毛利率回升", "研发投入增长" },
            risks = new[] { "负债率偏高", "现金流紧张" },
            evidenceTable = new[] { new { metric = "ROE", value = "6%", period = "2025Q3", source = "东方财富", assessment = "偏低" } }
        });
        var outputs = new List<string>
        {
            $"[{StockAgentRoleIds.FundamentalsAnalyst}]\n{fundamentalsJson}"
        };

        await service.GenerateBlocksFromStageAsync(
            session.Id, turn.Id, ResearchStageType.AnalystTeam,
            outputs, Array.Empty<string>());

        var block = await db.ResearchReportBlocks
            .FirstOrDefaultAsync(b => b.TurnId == turn.Id && b.BlockType == ReportBlockType.Fundamentals);

        Assert.NotNull(block);
        Assert.Equal("基本面承压", block!.Headline);
        Assert.NotNull(block.Summary);
        Assert.Contains("财务质量: 承压", block.Summary);
        Assert.Contains("估值判断: 偏贵", block.Summary);
        Assert.Contains("revenue: 100亿", block.Summary);
        Assert.NotNull(block.KeyPointsJson);
        Assert.NotNull(block.EvidenceRefsJson);
        Assert.NotNull(block.CounterEvidenceRefsJson);
    }

    // ── Error scenario tests ─────────────────────────────────────────

    [Fact]
    public async Task GenerateBlocks_MalformedJson_CreatesBlockWithRawContent()
    {
        await using var db = CreateDbContext();
        var (session, turn) = await SeedSessionAndTurn(db);

        var service = new ResearchReportService(db);
        var outputs = new List<string>
        {
            $"[{StockAgentRoleIds.MarketAnalyst}]\n invalid json {{"
        };

        await service.GenerateBlocksFromStageAsync(
            session.Id, turn.Id, ResearchStageType.AnalystTeam,
            outputs, Array.Empty<string>());

        var block = await db.ResearchReportBlocks.FirstOrDefaultAsync(b => b.TurnId == turn.Id);
        Assert.NotNull(block);
        Assert.Equal(ReportBlockType.Market, block!.BlockType);
        // Headline is null when JSON is malformed, but block is still created
        Assert.Null(block.Headline);
    }

    [Fact]
    public async Task GenerateBlocks_EmptyOutputs_CreatesNoBlocks()
    {
        await using var db = CreateDbContext();
        var (session, turn) = await SeedSessionAndTurn(db);

        var service = new ResearchReportService(db);

        await service.GenerateBlocksFromStageAsync(
            session.Id, turn.Id, ResearchStageType.AnalystTeam,
            Array.Empty<string>(), Array.Empty<string>());

        var blocks = await db.ResearchReportBlocks.Where(b => b.TurnId == turn.Id).ToListAsync();
        Assert.Empty(blocks);
    }

    [Fact]
    public void BuildNextActions_MalformedJsonFallsBackToDefaults()
    {
        var snapshot = new ResearchDecisionSnapshot
        {
            Id = 1, SessionId = 10, TurnId = 20,
            Rating = "buy",
            NextActionsJson = "not valid json {{{",
            CreatedAt = DateTime.UtcNow
        };

        var actions = ResearchReportService.BuildNextActionsFromDecision(snapshot);

        // Malformed JSON should fall back to defaults
        Assert.True(actions.Count >= 4, $"Expected defaults, got {actions.Count}");
        Assert.Contains(actions, a => a.ActionType == nameof(NextActionType.ViewDailyChart));
    }

    // ── Helpers ──────────────────────────────────────────────────────

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

    // ── Headline truncation ─────────────────────────────────────────

    [Fact]
    public async Task GenerateBlocks_LongHeadline_TruncatedTo500()
    {
        await using var db = CreateDbContext();
        var (session, turn) = await SeedSessionAndTurn(db);

        var longHeadline = new string('X', 600);
        var json = JsonSerializer.Serialize(new { headline = longHeadline, summary = "ok" });
        var outputs = new List<string> { $"[{StockAgentRoleIds.MarketAnalyst}]\n{json}" };

        var service = new ResearchReportService(db);
        await service.GenerateBlocksFromStageAsync(
            session.Id, turn.Id, ResearchStageType.AnalystTeam,
            outputs, Array.Empty<string>());

        var block = await db.ResearchReportBlocks
            .FirstOrDefaultAsync(b => b.TurnId == turn.Id && b.BlockType == ReportBlockType.Market);

        Assert.NotNull(block);
        Assert.Equal(500, block!.Headline!.Length);
    }

    // ── Block deduplication on rerun ─────────────────────────────────

    [Fact]
    public async Task GenerateBlocks_SecondCall_ReplacesExistingBlocks()
    {
        await using var db = CreateDbContext();
        var (session, turn) = await SeedSessionAndTurn(db);

        var service = new ResearchReportService(db);

        var json1 = JsonSerializer.Serialize(new { headline = "Initial", summary = "v1" });
        var outputs1 = new List<string> { $"[{StockAgentRoleIds.MarketAnalyst}]\n{json1}" };
        await service.GenerateBlocksFromStageAsync(
            session.Id, turn.Id, ResearchStageType.AnalystTeam,
            outputs1, Array.Empty<string>());

        // Call again with updated content — should replace, not duplicate
        var json2 = JsonSerializer.Serialize(new { headline = "Updated", summary = "v2" });
        var outputs2 = new List<string> { $"[{StockAgentRoleIds.MarketAnalyst}]\n{json2}" };
        await service.GenerateBlocksFromStageAsync(
            session.Id, turn.Id, ResearchStageType.AnalystTeam,
            outputs2, Array.Empty<string>());

        var blocks = await db.ResearchReportBlocks
            .Where(b => b.TurnId == turn.Id && b.BlockType == ReportBlockType.Market)
            .ToListAsync();

        Assert.Single(blocks);
        Assert.Equal("Updated", blocks[0].Headline);
    }
}
