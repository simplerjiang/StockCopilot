using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface IResearchReportService
{
    /// <summary>Generate report block(s) from a completed stage's outputs.</summary>
    Task GenerateBlocksFromStageAsync(
        long sessionId, long turnId,
        ResearchStageType stageType, IReadOnlyList<string> outputs,
        IReadOnlyList<string> degradedFlags,
        CancellationToken cancellationToken = default);

    /// <summary>Get full report (all blocks + final decision) for a turn.</summary>
    Task<ResearchTurnReportDto?> GetTurnReportAsync(long turnId, CancellationToken cancellationToken = default);

    /// <summary>Get enhanced final decision with structured nextActions for a turn.</summary>
    Task<ResearchFinalDecisionDto?> GetFinalDecisionAsync(long turnId, CancellationToken cancellationToken = default);
}

public sealed class ResearchReportService : IResearchReportService
{
    private readonly AppDbContext _dbContext;

    public ResearchReportService(AppDbContext dbContext) => _dbContext = dbContext;

    // ── Role → BlockType mapping ─────────────────────────────────────

    private static readonly IReadOnlyDictionary<string, ReportBlockType> RoleToBlockType =
        new Dictionary<string, ReportBlockType>
        {
            [StockAgentRoleIds.CompanyOverviewAnalyst] = ReportBlockType.CompanyOverview,
            [StockAgentRoleIds.MarketAnalyst] = ReportBlockType.Market,
            [StockAgentRoleIds.SocialSentimentAnalyst] = ReportBlockType.Social,
            [StockAgentRoleIds.NewsAnalyst] = ReportBlockType.News,
            [StockAgentRoleIds.FundamentalsAnalyst] = ReportBlockType.Fundamentals,
            [StockAgentRoleIds.ShareholderAnalyst] = ReportBlockType.Shareholder,
            [StockAgentRoleIds.ProductAnalyst] = ReportBlockType.Product,
        };

    // ── Block generation from stage outputs ──────────────────────────

    public async Task GenerateBlocksFromStageAsync(
        long sessionId, long turnId,
        ResearchStageType stageType, IReadOnlyList<string> outputs,
        IReadOnlyList<string> degradedFlags,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var hasDegradation = degradedFlags.Count > 0;
        var degradedJson = hasDegradation ? JsonSerializer.Serialize(degradedFlags) : null;

        // Remove any existing blocks for this turn+stage to avoid unique-index violations on reruns
        var stageTypeStr = stageType.ToString();
        var existing = await _dbContext.ResearchReportBlocks
            .Where(b => b.TurnId == turnId && b.SourceStageType == stageTypeStr)
            .ToListAsync(cancellationToken);
        if (existing.Count > 0)
            _dbContext.ResearchReportBlocks.RemoveRange(existing);

        switch (stageType)
        {
            case ResearchStageType.CompanyOverviewPreflight:
                await BuildCompanyOverviewBlockAsync(sessionId, turnId, outputs, hasDegradation, degradedJson, now, cancellationToken);
                break;

            case ResearchStageType.AnalystTeam:
                foreach (var output in outputs)
                {
                    var (roleId, content) = SplitRoleOutput(output);
                    if (roleId is null || !RoleToBlockType.TryGetValue(roleId, out var blockType))
                        continue;

                    var parsed = ParseAnalystBlock(content);
                    var roleDegraded = hasDegradation && degradedFlags.Any(f => f.Contains(roleId, StringComparison.OrdinalIgnoreCase));

                    _dbContext.ResearchReportBlocks.Add(new ResearchReportBlock
                    {
                        SessionId = sessionId, TurnId = turnId,
                        BlockType = blockType, VersionIndex = 0,
                        Headline = Truncate(parsed.Headline, 500),
                        Summary = parsed.Summary,
                        KeyPointsJson = parsed.KeyPointsJson,
                        EvidenceRefsJson = parsed.EvidenceRefsJson,
                        CounterEvidenceRefsJson = parsed.CounterEvidenceRefsJson,
                        Status = roleDegraded ? ReportBlockStatus.Degraded : ReportBlockStatus.Complete,
                        DegradedFlagsJson = roleDegraded ? degradedJson : null,
                        SourceStageType = stageType.ToString(),
                        CreatedAt = now, UpdatedAt = now
                    });
                }
                break;

            case ResearchStageType.ResearchDebate:
                BuildDebateBlock(sessionId, turnId, outputs, hasDegradation, degradedJson, now);
                break;

            case ResearchStageType.TraderProposal:
                BuildTraderProposalBlock(sessionId, turnId, outputs, hasDegradation, degradedJson, now);
                break;

            case ResearchStageType.RiskDebate:
                BuildRiskReviewBlock(sessionId, turnId, outputs, hasDegradation, degradedJson, now);
                break;

            case ResearchStageType.PortfolioDecision:
                BuildPortfolioDecisionBlock(sessionId, turnId, outputs, hasDegradation, degradedJson, now);
                break;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    // ── Query methods ────────────────────────────────────────────────

    public async Task<ResearchTurnReportDto?> GetTurnReportAsync(long turnId, CancellationToken cancellationToken = default)
    {
        var turn = await _dbContext.ResearchTurns
            .AsNoTracking().FirstOrDefaultAsync(t => t.Id == turnId, cancellationToken);
        if (turn is null) return null;

        var blocks = await _dbContext.ResearchReportBlocks
            .AsNoTracking()
            .Where(b => b.TurnId == turnId)
            .OrderBy(b => b.BlockType).ThenBy(b => b.VersionIndex)
            .Select(b => new ResearchReportBlockDto(
                b.Id, b.TurnId, b.BlockType.ToString(), b.VersionIndex,
                b.Headline, b.Summary, b.KeyPointsJson, b.EvidenceRefsJson,
                b.CounterEvidenceRefsJson, b.DisagreementsJson,
                b.RiskLimitsJson, b.InvalidationsJson, b.RecommendedActionsJson,
                b.Status.ToString(), b.DegradedFlagsJson, b.MissingEvidence,
                b.ConfidenceImpact, b.SourceStageType, b.SourceArtifactId,
                b.CreatedAt, b.UpdatedAt))
            .ToArrayAsync(cancellationToken);

        var decision = await GetFinalDecisionAsync(turnId, cancellationToken);

        var ragCitations = !string.IsNullOrEmpty(turn.RagCitationsJson)
            ? JsonSerializer.Deserialize<List<RagCitationDto>>(turn.RagCitationsJson) ?? new List<RagCitationDto>()
            : new List<RagCitationDto>();

        return new ResearchTurnReportDto(turnId, blocks, decision, ragCitations);
    }

    public async Task<ResearchFinalDecisionDto?> GetFinalDecisionAsync(long turnId, CancellationToken cancellationToken = default)
    {
        var snapshot = await _dbContext.ResearchDecisionSnapshots
            .AsNoTracking()
            .Where(d => d.TurnId == turnId)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (snapshot is null) return null;

        var nextActions = BuildNextActionsFromDecision(snapshot);

        return new ResearchFinalDecisionDto(
            snapshot.Id, snapshot.TurnId,
            snapshot.Rating, snapshot.Action, snapshot.ExecutiveSummary,
            snapshot.InvestmentThesis, snapshot.Confidence, snapshot.ConfidenceExplanation,
            snapshot.SupportingEvidenceJson, snapshot.CounterEvidenceJson,
            snapshot.RiskConsensus, snapshot.DissentJson,
            snapshot.InvalidationConditionsJson,
            nextActions, snapshot.CreatedAt);
    }

    // ── Block builders ───────────────────────────────────────────────

    private async Task BuildCompanyOverviewBlockAsync(
        long sessionId,
        long turnId,
        IReadOnlyList<string> outputs,
        bool hasDegradation,
        string? degradedJson,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var parsedOutput = outputs.Count > 0
            ? ParseAnalystBlock(SplitRoleOutput(outputs[0]).Content)
            : new ParsedBlock(null, null, null, null, null, null, null);

        var roleState = await _dbContext.ResearchRoleStates
            .AsNoTracking()
            .Where(rs => rs.Stage.TurnId == turnId
                && rs.Stage.StageType == ResearchStageType.CompanyOverviewPreflight
                && rs.RoleId == StockAgentRoleIds.CompanyOverviewAnalyst)
            .OrderByDescending(rs => rs.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var companyOverview = TryReadToolData(roleState?.OutputRefsJson, StockMcpToolNames.CompanyOverview);
        var marketContext = TryReadToolData(roleState?.OutputRefsJson, StockMcpToolNames.MarketContext);

        var companyName = ReadString(companyOverview, "name");
        var sectorName = ReadString(companyOverview, "sectorName");
        var mainBusiness = ReadString(companyOverview, "mainBusiness");
        var businessScope = ReadString(companyOverview, "businessScope");
        var quoteTimestamp = ReadString(companyOverview, "quoteTimestamp");
        var marketSummary = BuildMarketContextSummary(marketContext);

        var keyPoints = new List<string>();
        var coverageItems = new (string Label, string? Value)[]
        {
            ("公司名称", companyName),
            ("所属行业", sectorName),
            ("主营业务", mainBusiness),
            ("经营范围", businessScope),
            ("最新价", ReadFormattedNumber(companyOverview, "price", "元")),
            ("涨跌幅", ReadFormattedPercent(companyOverview, "changePercent")),
            ("流通市值", ReadFormattedNumber(companyOverview, "floatMarketCap", null)),
            ("市盈率", ReadFormattedNumber(companyOverview, "peRatio", null)),
            ("量比", ReadFormattedNumber(companyOverview, "volumeRatio", null)),
            ("股东户数", ReadFormattedInt(companyOverview, "shareholderCount")),
            ("市场环境", marketSummary)
        };

        foreach (var item in coverageItems.Where(item => !string.IsNullOrWhiteSpace(item.Value)))
        {
            keyPoints.Add($"{item.Label}: {item.Value}");
        }

        var missingFields = coverageItems.Where(item => string.IsNullOrWhiteSpace(item.Value)).Select(item => item.Label).ToArray();
        var summaryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(parsedOutput.Summary))
        {
            summaryParts.Add(parsedOutput.Summary!);
        }
        if (!string.IsNullOrWhiteSpace(mainBusiness))
        {
            summaryParts.Add($"主营业务：{mainBusiness}");
        }
        if (!string.IsNullOrWhiteSpace(businessScope))
        {
            summaryParts.Add($"经营范围：{businessScope}");
        }
        if (!string.IsNullOrWhiteSpace(marketSummary))
        {
            summaryParts.Add($"市场环境：{marketSummary}");
        }
        summaryParts.Add($"数据覆盖：已获取 {keyPoints.Count}/{coverageItems.Length} 项，并全部展示。{(missingFields.Length > 0 ? $"缺失字段：{string.Join('、', missingFields)}。" : string.Empty)}");
        if (!string.IsNullOrWhiteSpace(quoteTimestamp))
        {
            summaryParts.Add($"行情时间：{quoteTimestamp}");
        }

        _dbContext.ResearchReportBlocks.Add(new ResearchReportBlock
        {
            SessionId = sessionId,
            TurnId = turnId,
            BlockType = ReportBlockType.CompanyOverview,
            VersionIndex = 0,
            Headline = Truncate(!string.IsNullOrWhiteSpace(companyName) ? $"{companyName} 公司概览" : parsedOutput.Headline ?? "公司概览", 500),
            Summary = string.Join("\n\n", summaryParts.Where(part => !string.IsNullOrWhiteSpace(part))),
            KeyPointsJson = keyPoints.Count > 0 ? JsonSerializer.Serialize(keyPoints) : parsedOutput.KeyPointsJson,
            EvidenceRefsJson = JsonSerializer.Serialize(new[] { StockMcpToolNames.CompanyOverview, StockMcpToolNames.MarketContext }),
            Status = hasDegradation ? ReportBlockStatus.Degraded : ReportBlockStatus.Complete,
            DegradedFlagsJson = hasDegradation ? degradedJson : null,
            SourceStageType = ResearchStageType.CompanyOverviewPreflight.ToString(),
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    private void BuildDebateBlock(
        long sessionId, long turnId, IReadOnlyList<string> outputs,
        bool hasDegradation, string? degradedJson, DateTime now)
    {
        string? headline = null, summary = null;
        var keyPoints = new List<string>();
        var disagreements = new List<string>();
        var roleSummaries = new List<string>();

        foreach (var output in outputs)
        {
            var (roleId, content) = SplitRoleOutput(output);
            if (roleId is null) continue;

            var parsed = ParseGenericBlock(content);

            if (roleId == StockAgentRoleIds.ResearchManager)
            {
                headline = parsed.Headline ?? "研究辩论裁决";
                summary = parsed.Summary;
            }
            else if (parsed.Summary is not null)
            {
                roleSummaries.Add($"**{roleId}**: {parsed.Summary}");
            }

            if (parsed.KeyPointsJson is not null) keyPoints.Add(parsed.KeyPointsJson);
        }

        // Aggregate from other roles when designated role has no summary
        if (summary is null && roleSummaries.Count > 0)
            summary = string.Join("\n\n", roleSummaries);

        _dbContext.ResearchReportBlocks.Add(new ResearchReportBlock
        {
            SessionId = sessionId, TurnId = turnId,
            BlockType = ReportBlockType.ResearchDebate, VersionIndex = 0,
            Headline = Truncate(headline ?? "研究辩论", 500),
            Summary = summary,
            KeyPointsJson = FlattenJsonFragments(keyPoints),
            DisagreementsJson = disagreements.Count > 0 ? JsonSerializer.Serialize(disagreements) : null,
            Status = hasDegradation ? ReportBlockStatus.Degraded : ReportBlockStatus.Complete,
            DegradedFlagsJson = hasDegradation ? degradedJson : null,
            SourceStageType = ResearchStageType.ResearchDebate.ToString(),
            CreatedAt = now, UpdatedAt = now
        });
    }

    private void BuildTraderProposalBlock(
        long sessionId, long turnId, IReadOnlyList<string> outputs,
        bool hasDegradation, string? degradedJson, DateTime now)
    {
        string? headline = null, summary = null, riskLimits = null;

        foreach (var output in outputs)
        {
            var (roleId, content) = SplitRoleOutput(output);
            if (roleId != StockAgentRoleIds.Trader) continue;

            var parsed = ParseGenericBlock(content);
            headline = parsed.Headline ?? "交易方案";
            summary = parsed.Summary;
            riskLimits = parsed.RiskLimitsJson;
        }

        _dbContext.ResearchReportBlocks.Add(new ResearchReportBlock
        {
            SessionId = sessionId, TurnId = turnId,
            BlockType = ReportBlockType.TraderProposal, VersionIndex = 0,
            Headline = Truncate(headline ?? "交易提案", 500),
            Summary = summary, RiskLimitsJson = riskLimits,
            Status = hasDegradation ? ReportBlockStatus.Degraded : ReportBlockStatus.Complete,
            DegradedFlagsJson = hasDegradation ? degradedJson : null,
            SourceStageType = ResearchStageType.TraderProposal.ToString(),
            CreatedAt = now, UpdatedAt = now
        });
    }

    private void BuildRiskReviewBlock(
        long sessionId, long turnId, IReadOnlyList<string> outputs,
        bool hasDegradation, string? degradedJson, DateTime now)
    {
        string? headline = null, summary = null;
        var riskLimits = new List<string>();
        var invalidations = new List<string>();
        var disagreements = new List<string>();
        var roleSummaries = new List<string>();

        foreach (var output in outputs)
        {
            var (roleId, content) = SplitRoleOutput(output);
            if (roleId is null) continue;

            var parsed = ParseGenericBlock(content);

            if (parsed.RiskLimitsJson is not null) riskLimits.Add(parsed.RiskLimitsJson);
            if (parsed.InvalidationsJson is not null) invalidations.Add(parsed.InvalidationsJson);

            // Use conservative analyst's view as the block summary
            if (roleId == StockAgentRoleIds.ConservativeRiskAnalyst)
            {
                headline = "风险审查";
                summary = parsed.Summary;
            }
            else if (parsed.Summary is not null)
            {
                roleSummaries.Add($"**{roleId}**: {parsed.Summary}");
            }
        }

        // Aggregate from other roles when designated role has no summary
        if (summary is null && roleSummaries.Count > 0)
            summary = string.Join("\n\n", roleSummaries);

        _dbContext.ResearchReportBlocks.Add(new ResearchReportBlock
        {
            SessionId = sessionId, TurnId = turnId,
            BlockType = ReportBlockType.RiskReview, VersionIndex = 0,
            Headline = Truncate(headline ?? "风险审查", 500),
            Summary = summary,
            RiskLimitsJson = FlattenJsonFragments(riskLimits),
            InvalidationsJson = FlattenJsonFragments(invalidations),
            DisagreementsJson = disagreements.Count > 0 ? JsonSerializer.Serialize(disagreements) : null,
            Status = hasDegradation ? ReportBlockStatus.Degraded : ReportBlockStatus.Complete,
            DegradedFlagsJson = hasDegradation ? degradedJson : null,
            SourceStageType = ResearchStageType.RiskDebate.ToString(),
            CreatedAt = now, UpdatedAt = now
        });
    }

    private void BuildPortfolioDecisionBlock(
        long sessionId, long turnId, IReadOnlyList<string> outputs,
        bool hasDegradation, string? degradedJson, DateTime now)
    {
        string? headline = null, summary = null;
        string? evidenceRefs = null, counterEvidence = null;
        string? riskLimits = null, invalidations = null;

        foreach (var output in outputs)
        {
            var (_, content) = SplitRoleOutput(output);
            var parsed = ParseGenericBlock(content);
            headline = parsed.Headline;
            summary = parsed.Summary;
            evidenceRefs = parsed.EvidenceRefsJson;
            counterEvidence = parsed.CounterEvidenceRefsJson;
            riskLimits = parsed.RiskLimitsJson;
            invalidations = parsed.InvalidationsJson;
        }

        _dbContext.ResearchReportBlocks.Add(new ResearchReportBlock
        {
            SessionId = sessionId, TurnId = turnId,
            BlockType = ReportBlockType.PortfolioDecision, VersionIndex = 0,
            Headline = Truncate(headline ?? "投资组合决策", 500),
            Summary = summary,
            EvidenceRefsJson = evidenceRefs,
            CounterEvidenceRefsJson = counterEvidence,
            RiskLimitsJson = riskLimits,
            InvalidationsJson = invalidations,
            Status = hasDegradation ? ReportBlockStatus.Degraded : ReportBlockStatus.Complete,
            DegradedFlagsJson = hasDegradation ? degradedJson : null,
            SourceStageType = ResearchStageType.PortfolioDecision.ToString(),
            CreatedAt = now, UpdatedAt = now
        });
    }

    // ── NextAction builder ───────────────────────────────────────────

    internal static IReadOnlyList<NextActionDto> BuildNextActionsFromDecision(ResearchDecisionSnapshot snapshot)
    {
        var actions = new List<NextActionDto>();

        // Parse stored next actions JSON if available
        if (snapshot.NextActionsJson is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(snapshot.NextActionsJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        var actionType = item.TryGetProperty("action_type", out var at) ? at.GetString() : null;
                        var label = item.TryGetProperty("label", out var lb) ? lb.GetString() : null;
                        if (actionType is null || label is null) continue;

                        actions.Add(new NextActionDto(
                            actionType, label,
                            item.TryGetProperty("target_surface", out var ts) ? ts.GetString() : null,
                            snapshot.SessionId, snapshot.TurnId,
                            null, snapshot.Id,
                            item.TryGetProperty("artifact_refs", out var ar) ? ar.GetRawText() : null,
                            item.TryGetProperty("reason", out var rs) ? rs.GetString() :
                                item.TryGetProperty("reason_summary", out var rsum) ? rsum.GetString() :
                                item.TryGetProperty("rationale", out var rat) ? rat.GetString() : null,
                            item.TryGetProperty("requires_new_focus", out var rnf) && rnf.GetBoolean()));
                    }
                }
            }
            catch { /* ignore malformed JSON */ }
        }

        // If LLM didn't produce structured actions, generate sensible defaults
        if (actions.Count == 0)
        {
            actions.Add(new NextActionDto(
                nameof(NextActionType.ViewDailyChart), "看日K线",
                "kline-chart", snapshot.SessionId, snapshot.TurnId,
                null, snapshot.Id, null,
                "确认技术面趋势与决策一致性", false));

            actions.Add(new NextActionDto(
                nameof(NextActionType.ViewMinuteChart), "看分时",
                "minute-chart", snapshot.SessionId, snapshot.TurnId,
                null, snapshot.Id, null,
                "观察盘中量价走势", false));

            if (snapshot.Rating is not null && snapshot.Rating != "hold")
            {
                actions.Add(new NextActionDto(
                    nameof(NextActionType.DraftTradingPlan), "起草交易计划",
                    "trading-plan", snapshot.SessionId, snapshot.TurnId,
                    null, snapshot.Id, null,
                    "基于投资决策起草可执行的交易计划", true));
            }

            actions.Add(new NextActionDto(
                nameof(NextActionType.ViewEvidence), "查看证据",
                "evidence-panel", snapshot.SessionId, snapshot.TurnId,
                null, snapshot.Id, null,
                "审查支撑决策的关键证据", false));

            actions.Add(new NextActionDto(
                nameof(NextActionType.ViewLocalFacts), "查看本地事实",
                "local-facts", snapshot.SessionId, snapshot.TurnId,
                null, snapshot.Id, null,
                "核实本地事实数据时效性", false));
        }

        return actions;
    }

    // ── JSON flatten helper ─────────────────────────────────────────

    /// <summary>Flatten a list of JSON fragments (which may be arrays or scalars) into a single JSON array string.</summary>
    private static string? FlattenJsonFragments(List<string> fragments)
    {
        if (fragments.Count == 0) return null;
        var items = new List<string>();
        foreach (var frag in fragments)
        {
            try
            {
                using var doc = JsonDocument.Parse(frag);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in doc.RootElement.EnumerateArray())
                        items.Add(el.GetRawText());
                }
                else
                {
                    items.Add(frag);
                }
            }
            catch { items.Add(JsonSerializer.Serialize(frag)); }
        }
        return $"[{string.Join(",", items)}]";
    }

    // ── Parsing helpers ──────────────────────────────────────────────

    private static (string? RoleId, string Content) SplitRoleOutput(string output)
        => ResearchRunner.SplitRoleOutput(output);

    private record ParsedBlock(
        string? Headline, string? Summary,
        string? KeyPointsJson, string? EvidenceRefsJson,
        string? CounterEvidenceRefsJson, string? RiskLimitsJson,
        string? InvalidationsJson);

    private static ParsedBlock ParseAnalystBlock(string content)
    {
        try
        {
            var jsonStart = content.IndexOf('{');
            if (jsonStart < 0) return new(null, content, null, null, null, null, null);
            var jsonText = ResearchRunner.UnwrapContentWrapper(content[jsonStart..]);

            // Additional safety: trim to last } to handle trailing text
            var jsonEnd = jsonText.LastIndexOf('}');
            if (jsonEnd >= 0 && jsonEnd < jsonText.Length - 1)
                jsonText = jsonText[..(jsonEnd + 1)];

            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            // Build summary: try explicit fields first, then synthesize from fundamentals schema
            string? summary = null;
            if (root.TryGetProperty("summary", out var s))
                summary = s.GetString();
            else if (root.TryGetProperty("analysis", out var a2))
                summary = a2.GetString();
            else if (root.TryGetProperty("qualityView", out var qv) || root.TryGetProperty("valuationView", out _))
            {
                // Fundamentals analyst output: synthesize summary from qualityView + valuationView
                var quality = root.TryGetProperty("qualityView", out var qv2) ? qv2.GetString() : null;
                var valuation = root.TryGetProperty("valuationView", out var vv) ? vv.GetString() : null;
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(quality)) parts.Add($"财务质量: {quality}");
                if (!string.IsNullOrWhiteSpace(valuation)) parts.Add($"估值判断: {valuation}");
                if (root.TryGetProperty("metrics", out var metrics) && metrics.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in metrics.EnumerateObject())
                    {
                        var val = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.GetRawText();
                        if (!string.IsNullOrWhiteSpace(val)) parts.Add($"{prop.Name}: {val}");
                    }
                }
                summary = parts.Count > 0 ? string.Join("; ", parts) : null;
            }

            summary ??= root.TryGetProperty("businessScope", out var businessScope) ? businessScope.GetString() :
                root.TryGetProperty("industryPosition", out var industryPosition) ? industryPosition.GetString() :
                root.TryGetProperty("institutionActivity", out var institutionActivity) ? institutionActivity.GetString() : content;

            return new(
                root.TryGetProperty("headline", out var h) ? h.GetString() : null,
                summary,
                root.TryGetProperty("key_points", out var kp) ? kp.GetRawText() :
                    root.TryGetProperty("highlights", out var highlights) ? highlights.GetRawText() :
                    root.TryGetProperty("riskSignals", out var riskSignals) ? riskSignals.GetRawText() :
                    root.TryGetProperty("topHolderChanges", out var holderChanges) ? holderChanges.GetRawText() : null,
                root.TryGetProperty("evidence_refs", out var er) ? er.GetRawText() :
                    root.TryGetProperty("supporting_evidence_refs", out var ser) ? ser.GetRawText() :
                    root.TryGetProperty("evidenceTable", out var evidenceTable) ? evidenceTable.GetRawText() : null,
                root.TryGetProperty("counter_evidence", out var ce) ? ce.GetRawText() :
                    root.TryGetProperty("risks", out var risks) ? risks.GetRawText() : null,
                root.TryGetProperty("risk_limits", out var rl) ? rl.GetRawText() : null,
                root.TryGetProperty("invalidations", out var inv) ? inv.GetRawText() : null
            );
        }
        catch
        {
            var readable = ExtractReadableContent(content);
            return new(null, readable, null, null, null, null, null);
        }
    }

    private static ParsedBlock ParseGenericBlock(string content)
    {
        try
        {
            var jsonStart = content.IndexOf('{');
            if (jsonStart < 0) return new(null, content, null, null, null, null, null);
            var jsonText = ResearchRunner.UnwrapContentWrapper(content[jsonStart..]);

            // Additional safety: trim to last } to handle trailing text
            var jsonEnd = jsonText.LastIndexOf('}');
            if (jsonEnd >= 0 && jsonEnd < jsonText.Length - 1)
                jsonText = jsonText[..(jsonEnd + 1)];

            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            var headline = root.TryGetProperty("headline", out var h) ? h.GetString() :
                    root.TryGetProperty("executive_summary", out var es) ? es.GetString() : null;

            var summary = root.TryGetProperty("summary", out var s) ? s.GetString() :
                    root.TryGetProperty("analysis", out var a) ? a.GetString() :
                    root.TryGetProperty("rationale", out var r) ? r.GetString() :
                    root.TryGetProperty("recommendation", out var rec) ? rec.GetString() :
                    root.TryGetProperty("conclusion", out var conc) ? conc.GetString() :
                    root.TryGetProperty("verdict", out var vrd) ? vrd.GetString() :
                    root.TryGetProperty("assessment", out var ass) ? ass.GetString() :
                    root.TryGetProperty("overview", out var ov) ? ov.GetString() : null;

            // If no named summary field, try first substantial string property
            if (summary is null)
            {
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        var text = prop.Value.GetString();
                        if (text is not null && text.Length > 30)
                        {
                            summary = text;
                            break;
                        }
                    }
                }
            }

            return new(
                headline,
                summary,
                root.TryGetProperty("key_points", out var kp) ? kp.GetRawText() : null,
                root.TryGetProperty("evidence_refs", out var er) ? er.GetRawText() :
                    root.TryGetProperty("supporting_evidence", out var se) ? se.GetRawText() : null,
                root.TryGetProperty("counter_evidence", out var ce) ? ce.GetRawText() : null,
                root.TryGetProperty("risk_limits", out var rl) ? rl.GetRawText() : null,
                root.TryGetProperty("invalidations", out var inv) ? inv.GetRawText() : null
            );
        }
        catch
        {
            // Instead of returning raw JSON, try to extract decoded text
            var readable = ExtractReadableContent(content);
            return new(null, readable, null, null, null, null, null);
        }
    }

    /// <summary>Best-effort extract readable text from raw content, unwrapping JSON wrappers.</summary>
    private static string? ExtractReadableContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        try
        {
            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                using var doc = JsonDocument.Parse(content[jsonStart..(jsonEnd + 1)]);
                // Extract from {"content":"..."} wrapper
                if (doc.RootElement.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                {
                    var inner = c.GetString();
                    if (!string.IsNullOrWhiteSpace(inner)) return inner;
                }
            }
        }
        catch { /* ignore */ }
        return content;
    }

    private static JsonElement TryReadToolData(string? outputRefsJson, string toolName)
    {
        if (string.IsNullOrWhiteSpace(outputRefsJson))
        {
            return default;
        }

        try
        {
            using var doc = JsonDocument.Parse(outputRefsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return default;
            }

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("toolName", out var toolNameNode) || !string.Equals(toolNameNode.GetString(), toolName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!item.TryGetProperty("resultJson", out var resultNode) || resultNode.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                using var resultDoc = JsonDocument.Parse(resultNode.GetString()!);
                if (resultDoc.RootElement.TryGetProperty("data", out var dataNode))
                {
                    return JsonDocument.Parse(dataNode.GetRawText()).RootElement.Clone();
                }
            }
        }
        catch
        {
        }

        return default;
    }

    private static string? BuildMarketContextSummary(JsonElement marketContext)
    {
        if (marketContext.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var parts = new List<string>();
        if (marketContext.TryGetProperty("mainlineSectorName", out var mainlineSectorName) && mainlineSectorName.ValueKind == JsonValueKind.String)
        {
            parts.Add($"主线板块={mainlineSectorName.GetString()}");
        }
        if (marketContext.TryGetProperty("stockSectorName", out var stockSectorName) && stockSectorName.ValueKind == JsonValueKind.String)
        {
            parts.Add($"个股板块={stockSectorName.GetString()}");
        }
        if (marketContext.TryGetProperty("stageConfidence", out var stageConfidence) && stageConfidence.ValueKind == JsonValueKind.Number)
        {
            parts.Add($"阶段置信度={stageConfidence}");
        }

        return parts.Count > 0 ? string.Join("，", parts) : null;
    }

    private static string? ReadString(JsonElement node, string propertyName)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return node.TryGetProperty(propertyName, out var property) ? property.ToString() : null;
    }

    private static string? ReadFormattedNumber(JsonElement node, string propertyName, string? unit)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!node.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        var text = property.ToString();
        return string.IsNullOrWhiteSpace(unit) ? text : $"{text}{unit}";
    }

    private static string? ReadFormattedPercent(JsonElement node, string propertyName)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!node.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return $"{property}%";
    }

    private static string? ReadFormattedInt(JsonElement node, string propertyName)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!node.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return property.ToString();
    }

    /// <summary>Truncate a string to fit a DB column width, avoiding DbUpdateException on overflow.</summary>
    private static string? Truncate(string? value, int maxLength)
        => value is not null && value.Length > maxLength ? value[..maxLength] : value;
}
