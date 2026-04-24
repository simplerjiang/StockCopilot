using SimplerJiangAiAgent.Api.Modules.Market.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface IStockCopilotSessionService
{
    Task<StockCopilotSessionDto> BuildDraftTurnAsync(StockCopilotTurnDraftRequestDto request, CancellationToken cancellationToken = default);
}

public sealed class StockCopilotSessionService : IStockCopilotSessionService
{
    private const int DefaultMaxRounds = 3;
    private const int DefaultMaxToolCalls = 6;
    private const int DefaultMaxExternalSearchCalls = 2;
    private const int DefaultMaxTotalLatencyMs = 12000;
    private const int DefaultMaxPollingSteps = 4000;

    private static readonly string[] KlineKeywords = ["k线", "日k", "周k", "月k", "年k", "走势", "结构", "形态", "支撑", "压力"];
    private static readonly string[] MinuteKeywords = ["分时", "盘口", "承接", "早盘", "午后", "盘中", "vwap"];
    private static readonly string[] NewsKeywords = ["新闻", "公告", "消息", "研报", "催化", "事件", "资讯"];
    private static readonly string[] SearchKeywords = ["联网", "搜索", "外部", "网页", "网搜", "查一下"];

    private readonly IStockChatHistoryService _chatHistoryService;
    private readonly IStockCopilotMcpService _copilotMcpService;
    private readonly IStockMarketContextService _marketContextService;
    private readonly IStockAgentRoleContractRegistry _roleContractRegistry;

    public StockCopilotSessionService(IStockChatHistoryService chatHistoryService, IStockCopilotMcpService copilotMcpService, IStockMarketContextService marketContextService, IStockAgentRoleContractRegistry roleContractRegistry)
    {
        _chatHistoryService = chatHistoryService;
        _copilotMcpService = copilotMcpService;
        _marketContextService = marketContextService;
        _roleContractRegistry = roleContractRegistry;
    }

    public async Task<StockCopilotSessionDto> BuildDraftTurnAsync(StockCopilotTurnDraftRequestDto request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Question);

        var normalizedSymbol = StockSymbolNormalizer.Normalize(request.Symbol);
        var trimmedQuestion = request.Question.Trim();
        var sessionTitle = string.IsNullOrWhiteSpace(request.SessionTitle)
            ? BuildSessionTitle(trimmedQuestion)
            : request.SessionTitle.Trim();

        var session = await _chatHistoryService.CreateSessionAsync(normalizedSymbol, sessionTitle, request.SessionKey, cancellationToken);
        var marketContext = await _marketContextService.GetLatestAsync(normalizedSymbol, cancellationToken);
        var strategies = DetectStrategies(trimmedQuestion);
        var proposals = BuildToolProposals(normalizedSymbol, trimmedQuestion, strategies, request.AllowExternalSearch);
        var loopBudget = new StockCopilotLoopBudgetDto(
            DefaultMaxRounds,
            DefaultMaxToolCalls,
            DefaultMaxExternalSearchCalls,
            DefaultMaxTotalLatencyMs,
            DefaultMaxPollingSteps);
        var loopResult = await ExecuteControlledLoopAsync(
            normalizedSymbol,
            trimmedQuestion,
            strategies,
            proposals,
            request.TaskId,
            loopBudget,
            cancellationToken);
        var planSteps = BuildPlanSteps(proposals, marketContext, loopResult.StepStatuses, loopResult.CommanderStepStatus);
        var toolCalls = BuildToolCalls(proposals);
        var followUpActions = BuildFollowUpActions(proposals, loopResult.FinalAnswer);
        var roleContractChecklist = _roleContractRegistry.BuildChecklist();

        var turn = new StockCopilotTurnDto(
            TurnId: $"turn-{Guid.NewGuid():N}",
            SessionKey: session.SessionKey,
            Symbol: normalizedSymbol,
            UserQuestion: trimmedQuestion,
            CreatedAt: DateTime.UtcNow,
            Status: loopResult.TurnStatus,
            PlannerSummary: BuildPlannerSummary(proposals),
            GovernorSummary: BuildGovernorSummary(proposals),
            MarketContext: marketContext,
            PlanSteps: planSteps,
            ToolCalls: toolCalls,
            ToolResults: loopResult.ToolResults,
            FinalAnswer: loopResult.FinalAnswer,
            FollowUpActions: followUpActions,
            LoopBudget: loopBudget,
            LoopExecution: loopResult.LoopExecution);

        return new StockCopilotSessionDto(
            session.SessionKey,
            normalizedSymbol,
            session.Title,
            session.CreatedAt,
            session.UpdatedAt,
            [turn],
            roleContractChecklist);
    }

    private static string BuildSessionTitle(string question)
    {
        var compact = question.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return compact.Length <= 24 ? compact : compact[..24];
    }

    private static string BuildPlannerSummary(IReadOnlyList<ToolProposal> proposals)
    {
        if (proposals.Count == 0)
        {
            return "本轮问题未命中明确工具类型，先建议从本地新闻与市场环境切入。";
        }

        return $"planner 已把问题拆成 {proposals.Count} 个受控工具步骤，先执行 local-first 工具，再决定是否需要外部搜索。";
    }

    private static string BuildGovernorSummary(IReadOnlyList<ToolProposal> proposals)
    {
        var blocked = proposals.Count(item => item.ApprovalStatus == "blocked");
        if (blocked == 0)
        {
            return "governor 已放行当前 draft 中的本地工具调用。";
        }

        return $"governor 当前拦截了 {blocked} 个外部工具调用，需显式授权后才能继续。";
    }

    private static StockCopilotFinalAnswerDto BuildPendingFinalAnswer(IReadOnlyList<ToolProposal> proposals)
    {
        var blockedExternal = proposals.Any(item => item.ToolName == "StockSearchMcp" && item.ApprovalStatus == "blocked");
        var constraints = new List<string>
        {
            "最终回答只能引用 tool result 或已保存 evidence 中出现的事实。",
            "degradedFlags 会系统性压低 confidence 与动作强度。"
        };
        if (blockedExternal)
        {
            constraints.Add("外部搜索尚未获批，当前只能依赖 Local-First 证据链。");
        }

        return new StockCopilotFinalAnswerDto(
            Status: "needs_tool_execution",
            Summary: "当前只是会话编排草案；需要先执行已批准工具步骤，才能生成 grounded final answer。",
            GroundingMode: "tool_results_required",
            ConfidenceScore: null,
            NeedsToolExecution: true,
            Constraints: constraints,
            RagCitations: Array.Empty<StockCopilotMcpEvidenceDto>());
    }

    private static IReadOnlyList<StockCopilotPlanStepDto> BuildPlanSteps(
        IReadOnlyList<ToolProposal> proposals,
        StockMarketContextDto? marketContext,
        IReadOnlyDictionary<string, string>? stepStatuses = null,
        string commanderStatus = "pending_tool_results")
    {
        var steps = new List<StockCopilotPlanStepDto>
        {
            new(
                StepId: "planner-1",
                Owner: "planner",
                Title: "确认问题与市场环境",
                Description: marketContext is null
                    ? "先锁定问题意图，并在缺少市场上下文时走保守路径。"
                    : $"先锁定问题意图，并记录当前本地市场上下文的主线={marketContext.MainlineSectorName ?? "无"}、置信度={marketContext.StageConfidence:0.##}。",
                Status: stepStatuses?.GetValueOrDefault("planner-1") ?? "planned",
                DependsOn: Array.Empty<string>(),
                ToolName: null)
        };

        steps.AddRange(proposals.Select(proposal => new StockCopilotPlanStepDto(
            StepId: proposal.StepId,
            Owner: proposal.ApprovalStatus == "blocked" ? "governor" : "planner",
            Title: proposal.StepTitle,
            Description: proposal.StepDescription,
            Status: stepStatuses?.GetValueOrDefault(proposal.StepId) ?? proposal.ApprovalStatus,
            DependsOn: ["planner-1"],
            ToolName: proposal.ToolName)));

        steps.Add(new StockCopilotPlanStepDto(
            StepId: "commander-1",
            Owner: "commander",
            Title: "汇总工具结果并形成最终回答",
            Description: "commander 只能基于已返回 tool result、evidence 与 degradedFlags 形成最终判断。",
            Status: commanderStatus,
            DependsOn: steps.Where(step => step.ToolName is not null).Select(step => step.StepId).ToArray(),
            ToolName: null));

        return steps;
    }

    private static IReadOnlyList<StockCopilotToolCallDto> BuildToolCalls(IReadOnlyList<ToolProposal> proposals)
    {
        return proposals.Select(proposal => new StockCopilotToolCallDto(
            CallId: proposal.CallId,
            StepId: proposal.StepId,
            ToolName: proposal.ToolName,
            PolicyClass: proposal.PolicyClass,
            Purpose: proposal.Purpose,
            InputSummary: proposal.InputSummary,
            ApprovalStatus: proposal.ApprovalStatus,
            BlockedReason: proposal.BlockedReason)).ToArray();
    }

    private static IReadOnlyList<StockCopilotFollowUpActionDto> BuildFollowUpActions(IReadOnlyList<ToolProposal> proposals, StockCopilotFinalAnswerDto finalAnswer)
    {
        var actions = new List<StockCopilotFollowUpActionDto>();

        if (proposals.Any(item => item.ToolName == "StockKlineMcp"))
        {
            actions.Add(new StockCopilotFollowUpActionDto("action-kline", "看 60 日 K 线结构", "inspect_chart", "StockKlineMcp", "补看支撑、压力与趋势状态。", true, null));
        }

        if (proposals.Any(item => item.ToolName == "StockMinuteMcp"))
        {
            actions.Add(new StockCopilotFollowUpActionDto("action-minute", "检查今日分时承接", "inspect_intraday", "StockMinuteMcp", "补看开盘驱动、VWAP 与午后漂移。", true, null));
        }

        if (proposals.Any(item => item.ToolName == "StockStrategyMcp"))
        {
            actions.Add(new StockCopilotFollowUpActionDto("action-strategy", "查看策略信号细节", "inspect_strategy", "StockStrategyMcp", "检查 RSI/KDJ/MACD/TD 等确定性信号。", true, null));
        }

        if (proposals.Any(item => item.ToolName == "StockNewsMcp"))
        {
            actions.Add(new StockCopilotFollowUpActionDto("action-news", "查看本地新闻证据", "inspect_news", "StockNewsMcp", "核对公告、消息与 article-read 状态。", true, null));
        }

        var planEnabled = string.Equals(finalAnswer.Status, "done", StringComparison.OrdinalIgnoreCase) && !finalAnswer.NeedsToolExecution;
        actions.Add(new StockCopilotFollowUpActionDto(
            "action-plan",
            "起草交易计划",
            "draft_trading_plan",
            null,
            "在有 grounded final answer 后再进入计划草稿。",
            planEnabled,
            planEnabled ? null : "需要先完成受控 loop 并得到 grounded final answer。"));
        return actions;
    }

    private static IReadOnlyList<ToolProposal> BuildToolProposals(string symbol, string question, IReadOnlyList<string> strategies, bool allowExternalSearch)
    {
        var proposals = new List<ToolProposal>();
        var normalized = question.ToLowerInvariant();

        void AddProposal(
            string toolName,
            string policyClass,
            string stepTitle,
            string stepDescription,
            string purpose,
            string inputSummary,
            string approvalStatus,
            string? blockedReason,
            int round)
        {
            var sequence = proposals.Count + 1;
            proposals.Add(new ToolProposal(
                StepId: $"tool-{sequence}",
                CallId: $"call-{sequence}",
                Round: round,
                ToolName: toolName,
                PolicyClass: policyClass,
                StepTitle: stepTitle,
                StepDescription: stepDescription,
                Purpose: purpose,
                InputSummary: inputSummary,
                ApprovalStatus: approvalStatus,
                BlockedReason: blockedReason));
        }

        if (ContainsAny(normalized, KlineKeywords) || strategies.Count > 0)
        {
            AddProposal(
                "StockKlineMcp",
                "local_required",
                "读取 K 线结构",
                "用近 60 根 K 线确认趋势、支撑与压力。",
                "检查价格结构与关键位",
                $"symbol={symbol}; interval=day; count=60",
                "approved",
                null,
                1);
        }

        if (ContainsAny(normalized, MinuteKeywords))
        {
            AddProposal(
                "StockMinuteMcp",
                "local_required",
                "读取分时结构",
                "检查盘中承接、VWAP 与 session phase。",
                "检查盘中执行环境",
                $"symbol={symbol}",
                "approved",
                null,
                1);
        }

        if (strategies.Count > 0)
        {
            AddProposal(
                "StockStrategyMcp",
                "local_required",
                "计算策略信号",
                $"检查 {string.Join('/', strategies)} 等确定性信号。",
                "读取策略引擎结果",
                $"symbol={symbol}; interval=day; strategies={string.Join(',', strategies)}",
                "approved",
                null,
                2);
        }

        if (ContainsAny(normalized, NewsKeywords) || !proposals.Any())
        {
            AddProposal(
                "StockNewsMcp",
                "local_required",
                "读取本地新闻证据",
                "优先核对本地公告、新闻与 readStatus。",
                "读取 Local-First 证据链",
                $"symbol={symbol}; level=stock",
                "approved",
                null,
                2);
        }

        if (ContainsAny(normalized, SearchKeywords))
        {
            AddProposal(
                "StockSearchMcp",
                "external_gated",
                "外部搜索兜底",
                allowExternalSearch
                    ? "在 Local-First 证据不足时，允许受控外部搜索作为兜底。"
                    : "检测到外部搜索意图，但当前未获 governor 授权。",
                "补充外部证据",
                $"query={question.Trim()}",
                allowExternalSearch ? "approved" : "blocked",
                allowExternalSearch ? null : "external_gated 工具需要显式授权后才能执行。",
                3);
        }

        return proposals;
    }

    private async Task<ControlledLoopResult> ExecuteControlledLoopAsync(
        string symbol,
        string question,
        IReadOnlyList<string> strategies,
        IReadOnlyList<ToolProposal> proposals,
        string? taskId,
        StockCopilotLoopBudgetDto loopBudget,
        CancellationToken cancellationToken)
    {
        var approvedProposals = proposals
            .Where(item => string.Equals(item.ApprovalStatus, "approved", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Round)
            .ThenBy(item => item.CallId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (approvedProposals.Length == 0)
        {
            return new ControlledLoopResult(
                TurnStatus: "done_with_gaps",
                ToolResults: Array.Empty<StockCopilotToolResultDto>(),
                FinalAnswer: BuildPendingFinalAnswer(proposals) with
                {
                    Status = "done_with_gaps",
                    Summary = "没有可执行的已批准工具步骤；当前只能返回受限回答。",
                    GroundingMode = "gaps_only",
                    NeedsToolExecution = false,
                    ConfidenceScore = 0.18m,
                    Constraints = [.. BuildPendingFinalAnswer(proposals).Constraints, "当前 turn 没有任何已批准工具，因此被强制收口。"]
                },
                StepStatuses: new Dictionary<string, string>
                {
                    ["planner-1"] = "completed"
                },
                CommanderStepStatus: "done_with_gaps",
                LoopExecution: new StockCopilotLoopExecutionDto(0, 0, 0, 0, 1, "done_with_gaps", "no_approved_tools", true));
        }

        var stepStatuses = new Dictionary<string, string>
        {
            ["planner-1"] = "completed"
        };
        var toolResults = new List<StockCopilotToolResultDto>();
        var snapshots = new List<ToolExecutionSnapshot>();
        var totalLatencyMs = 0L;
        var toolCallsExecuted = 0;
        var externalSearchCallsExecuted = 0;
        var completedRounds = 0;
        var consumedPollingSteps = 1;
        string? stopReason = null;
        var forcedClose = false;

        foreach (var roundGroup in approvedProposals.GroupBy(item => item.Round).OrderBy(group => group.Key))
        {
            if (roundGroup.Key > loopBudget.MaxRounds)
            {
                stopReason = "round_budget_reached";
                forcedClose = true;
                break;
            }

            foreach (var proposal in roundGroup)
            {
                if (toolCallsExecuted >= loopBudget.MaxToolCalls)
                {
                    stopReason = "tool_budget_reached";
                    forcedClose = true;
                    break;
                }

                if (totalLatencyMs >= loopBudget.MaxTotalLatencyMs)
                {
                    stopReason = "time_budget_reached";
                    forcedClose = true;
                    break;
                }

                if (string.Equals(proposal.PolicyClass, "external_gated", StringComparison.OrdinalIgnoreCase)
                    && externalSearchCallsExecuted >= loopBudget.MaxExternalSearchCalls)
                {
                    stopReason = "external_search_budget_reached";
                    forcedClose = true;
                    break;
                }

                stepStatuses[proposal.StepId] = $"calling_tools_round_{roundGroup.Key}";
                consumedPollingSteps += 1;

                var outcome = await ExecuteProposalAsync(symbol, question, strategies, proposal, taskId, cancellationToken);
                toolResults.Add(outcome.ToolResult);
                snapshots.Add(outcome.Snapshot);
                stepStatuses[proposal.StepId] = outcome.ToolResult.Status == "completed" ? "completed" : "failed";
                totalLatencyMs += outcome.Snapshot.LatencyMs;
                toolCallsExecuted += 1;
                if (string.Equals(proposal.PolicyClass, "external_gated", StringComparison.OrdinalIgnoreCase))
                {
                    externalSearchCallsExecuted += 1;
                }
            }

            completedRounds = roundGroup.Key;
            if (forcedClose)
            {
                break;
            }
        }

        consumedPollingSteps += 1;
        var finalAnswer = BuildGroundedFinalAnswer(question, proposals, snapshots, stopReason, forcedClose);
        var commanderStatus = string.Equals(finalAnswer.Status, "done", StringComparison.OrdinalIgnoreCase)
            ? "done"
            : string.Equals(finalAnswer.Status, "failed", StringComparison.OrdinalIgnoreCase)
                ? "failed"
                : "done_with_gaps";

        return new ControlledLoopResult(
            TurnStatus: finalAnswer.Status,
            ToolResults: toolResults,
            FinalAnswer: finalAnswer,
            StepStatuses: stepStatuses,
            CommanderStepStatus: commanderStatus,
            LoopExecution: new StockCopilotLoopExecutionDto(
                completedRounds,
                toolCallsExecuted,
                externalSearchCallsExecuted,
                totalLatencyMs,
                Math.Min(consumedPollingSteps, loopBudget.MaxPollingSteps),
                finalAnswer.Status,
                stopReason ?? (string.Equals(finalAnswer.Status, "done", StringComparison.OrdinalIgnoreCase) ? "evidence_sufficient" : "forced_close"),
                forcedClose));
    }

    private async Task<ToolExecutionOutcome> ExecuteProposalAsync(
        string symbol,
        string question,
        IReadOnlyList<string> strategies,
        ToolProposal proposal,
        string? taskId,
        CancellationToken cancellationToken)
    {
        try
        {
            var input = ParseInputSummary(proposal.InputSummary);
            var window = ParseWindowOptions(input);
            var toolTaskId = string.IsNullOrWhiteSpace(taskId) ? proposal.CallId : $"{taskId.Trim()}-{proposal.CallId}";

            return proposal.ToolName switch
            {
                "StockKlineMcp" => MapOutcome(
                    proposal,
                    await _copilotMcpService.GetKlineAsync(
                        symbol,
                        input.GetValueOrDefault("interval", "day"),
                        ParseInt(input, "count", 60),
                        null,
                        toolTaskId,
                        window,
                        cancellationToken)),
                "StockMinuteMcp" => MapOutcome(
                    proposal,
                    await _copilotMcpService.GetMinuteAsync(symbol, null, toolTaskId, window, cancellationToken)),
                "StockStrategyMcp" => MapOutcome(
                    proposal,
                    await _copilotMcpService.GetStrategyAsync(
                        symbol,
                        input.GetValueOrDefault("interval", "day"),
                        ParseInt(input, "count", 90),
                        null,
                        strategies.Count == 0 ? null : strategies,
                        toolTaskId,
                        window,
                        cancellationToken)),
                "StockNewsMcp" => MapOutcome(
                    proposal,
                    await _copilotMcpService.GetNewsAsync(symbol, input.GetValueOrDefault("level", "stock"), toolTaskId, window, cancellationToken)),
                "StockSearchMcp" => MapOutcome(
                    proposal,
                    await _copilotMcpService.SearchAsync(input.GetValueOrDefault("query", question), true, toolTaskId, cancellationToken)),
                _ => BuildFailedOutcome(proposal, $"暂不支持工具 {proposal.ToolName} 的受控 loop 执行。")
            };
        }
        catch (Exception ex)
        {
            return BuildFailedOutcome(proposal, ex.Message);
        }
    }

    private static ToolExecutionOutcome MapOutcome(ToolProposal proposal, StockCopilotMcpEnvelopeDto<StockCopilotKlineDataDto> envelope)
    {
        var summary = $"K 线 {envelope.Data.WindowSize} 根，趋势={envelope.Data.TrendState}，5D={envelope.Data.Return5dPercent:0.##}% / 20D={envelope.Data.Return20dPercent:0.##}%。";
        return BuildCompletedOutcome(
            proposal,
            envelope,
            summary,
            bullishHints: envelope.Data.Return5dPercent > 0m ? 1 : 0,
            bearishHints: envelope.Data.Return5dPercent < 0m ? 1 : 0,
            trendState: envelope.Data.TrendState,
            newsItemCount: null,
            searchResultCount: null);
    }

    private static ToolExecutionOutcome MapOutcome(ToolProposal proposal, StockCopilotMcpEnvelopeDto<StockCopilotMinuteDataDto> envelope)
    {
        var summary = $"分时 {envelope.Data.WindowSize} 个点位，session={envelope.Data.SessionPhase}，VWAP={FormatNumber(envelope.Data.Vwap)}。";
        return BuildCompletedOutcome(
            proposal,
            envelope,
            summary,
            bullishHints: envelope.Data.AfternoonDriftPercent is > 0m ? 1 : 0,
            bearishHints: envelope.Data.AfternoonDriftPercent is < 0m ? 1 : 0,
            trendState: envelope.Data.SessionPhase,
            newsItemCount: null,
            searchResultCount: null);
    }

    private static ToolExecutionOutcome MapOutcome(ToolProposal proposal, StockCopilotMcpEnvelopeDto<StockCopilotStrategyDataDto> envelope)
    {
        var bullishHints = envelope.Data.Signals.Count(item => IsBullishSignal(item.Signal, item.State));
        var bearishHints = envelope.Data.Signals.Count(item => IsBearishSignal(item.Signal, item.State));
        var summary = $"策略信号 {envelope.Data.Signals.Count} 条，多头提示 {bullishHints} 条，空头提示 {bearishHints} 条。";
        return BuildCompletedOutcome(
            proposal,
            envelope,
            summary,
            bullishHints,
            bearishHints,
            trendState: null,
            newsItemCount: null,
            searchResultCount: null);
    }

    private static ToolExecutionOutcome MapOutcome(ToolProposal proposal, StockCopilotMcpEnvelopeDto<StockCopilotNewsDataDto> envelope)
    {
        var summary = $"本地新闻 {envelope.Data.ItemCount} 条，最近发布时间 {envelope.Data.LatestPublishedAt:yyyy-MM-dd HH:mm:ss}。";
        return BuildCompletedOutcome(
            proposal,
            envelope,
            summary,
            bullishHints: 0,
            bearishHints: 0,
            trendState: null,
            newsItemCount: envelope.Data.ItemCount,
            searchResultCount: null);
    }

    private static ToolExecutionOutcome MapOutcome(ToolProposal proposal, StockCopilotMcpEnvelopeDto<StockCopilotSearchDataDto> envelope)
    {
        var summary = $"外部搜索 provider={envelope.Data.Provider}，结果 {envelope.Data.ResultCount} 条。";
        return BuildCompletedOutcome(
            proposal,
            envelope,
            summary,
            bullishHints: 0,
            bearishHints: 0,
            trendState: null,
            newsItemCount: null,
            searchResultCount: envelope.Data.ResultCount);
    }

    private static ToolExecutionOutcome BuildCompletedOutcome<T>(
        ToolProposal proposal,
        StockCopilotMcpEnvelopeDto<T> envelope,
        string summary,
        int bullishHints,
        int bearishHints,
        string? trendState,
        int? newsItemCount,
        int? searchResultCount)
    {
        return new ToolExecutionOutcome(
            new StockCopilotToolResultDto(
                proposal.CallId,
                proposal.ToolName,
                "completed",
                envelope.TraceId,
                envelope.Evidence.Count,
                envelope.Features.Count,
                envelope.Warnings,
                envelope.DegradedFlags,
                envelope.Evidence,
                summary),
            new ToolExecutionSnapshot(
                proposal.CallId,
                proposal.ToolName,
                envelope.LatencyMs,
                envelope.Evidence.Count,
                envelope.Features.Count,
                envelope.Warnings.Count,
                envelope.DegradedFlags.Count,
                bullishHints,
                bearishHints,
                trendState,
                newsItemCount,
                searchResultCount,
                summary,
                envelope.TraceId));
    }

    private static ToolExecutionOutcome BuildFailedOutcome(ToolProposal proposal, string message)
    {
        return new ToolExecutionOutcome(
            new StockCopilotToolResultDto(
                proposal.CallId,
                proposal.ToolName,
                "failed",
                null,
                0,
                0,
                [message],
                Array.Empty<string>(),
                Array.Empty<StockCopilotMcpEvidenceDto>(),
                $"{proposal.ToolName} 执行失败：{message}"),
            new ToolExecutionSnapshot(
                proposal.CallId,
                proposal.ToolName,
                0,
                0,
                0,
                1,
                0,
                0,
                0,
                null,
                null,
                null,
                message,
                null));
    }

    private static StockCopilotFinalAnswerDto BuildGroundedFinalAnswer(
        string question,
        IReadOnlyList<ToolProposal> proposals,
        IReadOnlyList<ToolExecutionSnapshot> snapshots,
        string? stopReason,
        bool forcedClose)
    {
        if (snapshots.Count == 0)
        {
            return BuildPendingFinalAnswer(proposals) with
            {
                Status = "failed",
                Summary = "受控 loop 没有成功执行任何工具，当前无法形成 grounded final answer。",
                GroundingMode = "failed",
                ConfidenceScore = 0.05m,
                NeedsToolExecution = false,
                Constraints = [.. BuildPendingFinalAnswer(proposals).Constraints, "当前没有任何可用 tool result。"]
            };
        }

        var completedCount = snapshots.Count(item => item.EvidenceCount > 0 || item.FeatureCount > 0 || !string.IsNullOrWhiteSpace(item.TraceId));
        var totalEvidenceCount = snapshots.Sum(item => item.EvidenceCount);
        var totalWarnings = snapshots.Sum(item => item.WarningCount);
        var totalDegradedFlags = snapshots.Sum(item => item.DegradedFlagCount);
        var bullishHints = snapshots.Sum(item => item.BullishHints);
        var bearishHints = snapshots.Sum(item => item.BearishHints);
        var trendState = snapshots.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.TrendState))?.TrendState;
        var newsItemCount = snapshots.Where(item => item.NewsItemCount.HasValue).Select(item => item.NewsItemCount!.Value).DefaultIfEmpty().Max();
        var searchResultCount = snapshots.Where(item => item.SearchResultCount.HasValue).Select(item => item.SearchResultCount!.Value).DefaultIfEmpty().Max();

        var direction = bullishHints > bearishHints
            ? "中性偏多"
            : bearishHints > bullishHints
                ? "中性偏空"
                : "中性";
        var directionReason = trendState is null
            ? "当前技术结构没有形成单边优势。"
            : $"当前技术结构更接近 {trendState}。";
        var newsReason = newsItemCount > 0
            ? $"本地公告/新闻已命中 {newsItemCount} 条可核对证据。"
            : "本地新闻证据较弱，结论主要依赖已返回的确定性特征。";
        var searchReason = searchResultCount > 0
            ? $"外部搜索补到了 {searchResultCount} 条兜底结果。"
            : "本轮没有依赖额外外部搜索结果。";
        var status = (!forcedClose && totalEvidenceCount > 0 && totalWarnings == 0)
            ? "done"
            : "done_with_gaps";
        var confidence = Math.Max(0.12m, Math.Min(0.92m, decimal.Round(0.35m + totalEvidenceCount * 0.08m + completedCount * 0.05m - totalWarnings * 0.05m - totalDegradedFlags * 0.07m, 2)));
        var summary = $"围绕“{question}”的受控 loop 已完成 {snapshots.Count} 个工具步骤。{directionReason}{newsReason}{searchReason} 当前方向判断为 {direction}。";

        var constraints = new List<string>
        {
            "最终回答只基于本轮 tool result、traceId 与 evidence。",
            $"本轮共拿到 {totalEvidenceCount} 条 evidence、{completedCount} 个可追溯结果。"
        };

        if (!string.IsNullOrWhiteSpace(stopReason))
        {
            constraints.Add($"当前 loop 收口原因：{stopReason}。");
        }

        if (totalWarnings > 0 || totalDegradedFlags > 0)
        {
            constraints.Add($"当前还有 {totalWarnings} 条 warning、{totalDegradedFlags} 条 degraded flag，结论已按保守路径收口。");
        }

        if (proposals.Any(item => item.ToolName == "StockSearchMcp" && string.Equals(item.ApprovalStatus, "blocked", StringComparison.OrdinalIgnoreCase)))
        {
            constraints.Add("外部搜索尚未获批，当前 grounded final answer 仍然只基于 Local-First 证据链。");
        }

        if (status == "done_with_gaps")
        {
            constraints.Add("当前回答已收口，但仍保留证据缺口或预算触顶说明。\n若要继续，需要进入下一轮会话或补充工具授权。".Replace("\\n", "\n"));
        }

        return new StockCopilotFinalAnswerDto(
            Status: status,
            Summary: summary,
            GroundingMode: status == "done" ? "grounded" : "grounded_with_gaps",
            ConfidenceScore: confidence,
            NeedsToolExecution: false,
            Constraints: constraints,
            RagCitations: Array.Empty<StockCopilotMcpEvidenceDto>());
    }

    private static Dictionary<string, string> ParseInputSummary(string inputSummary)
    {
        return string.IsNullOrWhiteSpace(inputSummary)
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : inputSummary
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(item => item.Split('=', 2))
                .Where(parts => parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
                .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
    }

    private static int ParseInt(IReadOnlyDictionary<string, string> input, string key, int fallback)
    {
        return input.TryGetValue(key, out var value) && int.TryParse(value, out var parsed)
            ? parsed
            : fallback;
    }

    private static StockCopilotMcpWindowOptions? ParseWindowOptions(IReadOnlyDictionary<string, string> input)
    {
        var evidenceSkip = 0;
        var evidenceTake = 0;
        var factSkip = 0;
        var factTake = 0;
        var hasEvidenceSkip = input.TryGetValue("evidenceSkip", out var evidenceSkipRaw) && int.TryParse(evidenceSkipRaw, out evidenceSkip);
        var hasEvidenceTake = input.TryGetValue("evidenceTake", out var evidenceTakeRaw) && int.TryParse(evidenceTakeRaw, out evidenceTake);
        var hasFactSkip = input.TryGetValue("factSkip", out var factSkipRaw) && int.TryParse(factSkipRaw, out factSkip);
        var hasFactTake = input.TryGetValue("factTake", out var factTakeRaw) && int.TryParse(factTakeRaw, out factTake);

        if (!hasEvidenceSkip && !hasEvidenceTake && !hasFactSkip && !hasFactTake)
        {
            return null;
        }

        return new StockCopilotMcpWindowOptions(
            hasEvidenceSkip ? evidenceSkip : 0,
            hasEvidenceTake ? evidenceTake : null,
            hasFactSkip ? factSkip : 0,
            hasFactTake ? factTake : null);
    }

    private static bool IsBullishSignal(string signal, string? state)
    {
        return signal.Contains("golden", StringComparison.OrdinalIgnoreCase)
            || signal.Contains("breakout", StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, "bullish", StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, "positive", StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, "overbought_breakout", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBearishSignal(string signal, string? state)
    {
        return signal.Contains("death", StringComparison.OrdinalIgnoreCase)
            || signal.Contains("breakdown", StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, "bearish", StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, "negative", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatNumber(decimal? value)
    {
        return value.HasValue ? value.Value.ToString("0.##") : "--";
    }

    private static IReadOnlyList<string> DetectStrategies(string question)
    {
        var normalized = question.ToLowerInvariant();
        var strategies = new List<string>();
        AddIfContains(normalized, strategies, "rsi", ["rsi"]);
        AddIfContains(normalized, strategies, "kdj", ["kdj"]);
        AddIfContains(normalized, strategies, "macd", ["macd"]);
        AddIfContains(normalized, strategies, "td", ["td", "九转"]);
        AddIfContains(normalized, strategies, "vwap", ["vwap"]);
        AddIfContains(normalized, strategies, "ma", ["ma", "均线"]);
        AddIfContains(normalized, strategies, "breakout", ["突破"]);
        AddIfContains(normalized, strategies, "gap", ["缺口"]);
        AddIfContains(normalized, strategies, "divergence", ["背离"]);
        return strategies;
    }

    private static void AddIfContains(string normalizedQuestion, List<string> strategies, string strategy, IReadOnlyList<string> keywords)
    {
        if (keywords.Any(normalizedQuestion.Contains) && !strategies.Contains(strategy, StringComparer.OrdinalIgnoreCase))
        {
            strategies.Add(strategy);
        }
    }

    private static bool ContainsAny(string question, IReadOnlyList<string> keywords)
    {
        return keywords.Any(question.Contains);
    }

    private sealed record ToolProposal(
        string StepId,
        string CallId,
        int Round,
        string ToolName,
        string PolicyClass,
        string StepTitle,
        string StepDescription,
        string Purpose,
        string InputSummary,
        string ApprovalStatus,
        string? BlockedReason);

    private sealed record ToolExecutionSnapshot(
        string CallId,
        string ToolName,
        long LatencyMs,
        int EvidenceCount,
        int FeatureCount,
        int WarningCount,
        int DegradedFlagCount,
        int BullishHints,
        int BearishHints,
        string? TrendState,
        int? NewsItemCount,
        int? SearchResultCount,
        string Summary,
        string? TraceId);

    private sealed record ToolExecutionOutcome(
        StockCopilotToolResultDto ToolResult,
        ToolExecutionSnapshot Snapshot);

    private sealed record ControlledLoopResult(
        string TurnStatus,
        IReadOnlyList<StockCopilotToolResultDto> ToolResults,
        StockCopilotFinalAnswerDto FinalAnswer,
        IReadOnlyDictionary<string, string> StepStatuses,
        string CommanderStepStatus,
        StockCopilotLoopExecutionDto LoopExecution);
}