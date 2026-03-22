using SimplerJiangAiAgent.Api.Modules.Market.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface IStockCopilotSessionService
{
    Task<StockCopilotSessionDto> BuildDraftTurnAsync(StockCopilotTurnDraftRequestDto request, CancellationToken cancellationToken = default);
}

public sealed class StockCopilotSessionService : IStockCopilotSessionService
{
    private static readonly string[] KlineKeywords = ["k线", "日k", "周k", "月k", "年k", "走势", "结构", "形态", "支撑", "压力"];
    private static readonly string[] MinuteKeywords = ["分时", "盘口", "承接", "早盘", "午后", "盘中", "vwap"];
    private static readonly string[] NewsKeywords = ["新闻", "公告", "消息", "研报", "催化", "事件", "资讯"];
    private static readonly string[] SearchKeywords = ["联网", "搜索", "外部", "网页", "网搜", "查一下"];

    private readonly IStockChatHistoryService _chatHistoryService;
    private readonly IStockMarketContextService _marketContextService;

    public StockCopilotSessionService(IStockChatHistoryService chatHistoryService, IStockMarketContextService marketContextService)
    {
        _chatHistoryService = chatHistoryService;
        _marketContextService = marketContextService;
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
        var planSteps = BuildPlanSteps(proposals, marketContext);
        var toolCalls = BuildToolCalls(proposals);
        var followUpActions = BuildFollowUpActions(proposals);

        var turn = new StockCopilotTurnDto(
            TurnId: $"turn-{Guid.NewGuid():N}",
            SessionKey: session.SessionKey,
            Symbol: normalizedSymbol,
            UserQuestion: trimmedQuestion,
            CreatedAt: DateTime.UtcNow,
            Status: "draft",
            PlannerSummary: BuildPlannerSummary(proposals),
            GovernorSummary: BuildGovernorSummary(proposals),
            MarketContext: marketContext,
            PlanSteps: planSteps,
            ToolCalls: toolCalls,
            ToolResults: Array.Empty<StockCopilotToolResultDto>(),
            FinalAnswer: BuildPendingFinalAnswer(proposals),
            FollowUpActions: followUpActions);

        return new StockCopilotSessionDto(
            session.SessionKey,
            normalizedSymbol,
            session.Title,
            session.CreatedAt,
            session.UpdatedAt,
            [turn]);
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
            Constraints: constraints);
    }

    private static IReadOnlyList<StockCopilotPlanStepDto> BuildPlanSteps(IReadOnlyList<ToolProposal> proposals, StockMarketContextDto? marketContext)
    {
        var steps = new List<StockCopilotPlanStepDto>
        {
            new(
                StepId: "planner-1",
                Owner: "planner",
                Title: "确认问题与市场环境",
                Description: marketContext is null
                    ? "先锁定问题意图，并在缺少市场上下文时走保守路径。"
                    : $"先锁定问题意图，并记录当前市场阶段={marketContext.StageLabel}、主线={marketContext.MainlineSectorName ?? "无"}。",
                Status: "planned",
                DependsOn: Array.Empty<string>(),
                ToolName: null)
        };

        steps.AddRange(proposals.Select((proposal, index) => new StockCopilotPlanStepDto(
            StepId: $"tool-{index + 1}",
            Owner: proposal.ApprovalStatus == "blocked" ? "governor" : "planner",
            Title: proposal.StepTitle,
            Description: proposal.StepDescription,
            Status: proposal.ApprovalStatus,
            DependsOn: ["planner-1"],
            ToolName: proposal.ToolName)));

        steps.Add(new StockCopilotPlanStepDto(
            StepId: "commander-1",
            Owner: "commander",
            Title: "汇总工具结果并形成最终回答",
            Description: "commander 只能基于已返回 tool result、evidence 与 degradedFlags 形成最终判断。",
            Status: "pending_tool_results",
            DependsOn: steps.Where(step => step.ToolName is not null).Select(step => step.StepId).ToArray(),
            ToolName: null));

        return steps;
    }

    private static IReadOnlyList<StockCopilotToolCallDto> BuildToolCalls(IReadOnlyList<ToolProposal> proposals)
    {
        return proposals.Select((proposal, index) => new StockCopilotToolCallDto(
            CallId: $"call-{index + 1}",
            StepId: $"tool-{index + 1}",
            ToolName: proposal.ToolName,
            PolicyClass: proposal.PolicyClass,
            Purpose: proposal.Purpose,
            InputSummary: proposal.InputSummary,
            ApprovalStatus: proposal.ApprovalStatus,
            BlockedReason: proposal.BlockedReason)).ToArray();
    }

    private static IReadOnlyList<StockCopilotFollowUpActionDto> BuildFollowUpActions(IReadOnlyList<ToolProposal> proposals)
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

        actions.Add(new StockCopilotFollowUpActionDto("action-plan", "起草交易计划", "draft_trading_plan", null, "在有 grounded final answer 后再进入计划草稿。", false, "需要先完成工具执行并得到最终判断。"));
        return actions;
    }

    private static IReadOnlyList<ToolProposal> BuildToolProposals(string symbol, string question, IReadOnlyList<string> strategies, bool allowExternalSearch)
    {
        var proposals = new List<ToolProposal>();
        var normalized = question.ToLowerInvariant();

        if (ContainsAny(normalized, KlineKeywords) || strategies.Count > 0)
        {
            proposals.Add(new ToolProposal(
                "StockKlineMcp",
                "local_required",
                "读取 K 线结构",
                "用近 60 根 K 线确认趋势、支撑与压力。",
                "检查价格结构与关键位",
                $"symbol={symbol}; interval=day; count=60",
                "approved",
                null));
        }

        if (ContainsAny(normalized, MinuteKeywords))
        {
            proposals.Add(new ToolProposal(
                "StockMinuteMcp",
                "local_required",
                "读取分时结构",
                "检查盘中承接、VWAP 与 session phase。",
                "检查盘中执行环境",
                $"symbol={symbol}",
                "approved",
                null));
        }

        if (strategies.Count > 0)
        {
            proposals.Add(new ToolProposal(
                "StockStrategyMcp",
                "local_required",
                "计算策略信号",
                $"检查 {string.Join('/', strategies)} 等确定性信号。",
                "读取策略引擎结果",
                $"symbol={symbol}; interval=day; strategies={string.Join(',', strategies)}",
                "approved",
                null));
        }

        if (ContainsAny(normalized, NewsKeywords) || !proposals.Any())
        {
            proposals.Add(new ToolProposal(
                "StockNewsMcp",
                "local_required",
                "读取本地新闻证据",
                "优先核对本地公告、新闻与 readStatus。",
                "读取 Local-First 证据链",
                $"symbol={symbol}; level=stock",
                "approved",
                null));
        }

        if (ContainsAny(normalized, SearchKeywords))
        {
            proposals.Add(new ToolProposal(
                "StockSearchMcp",
                "external_gated",
                "外部搜索兜底",
                allowExternalSearch
                    ? "在 Local-First 证据不足时，允许受控外部搜索作为兜底。"
                    : "检测到外部搜索意图，但当前未获 governor 授权。",
                "补充外部证据",
                $"query={question.Trim()}",
                allowExternalSearch ? "approved" : "blocked",
                allowExternalSearch ? null : "external_gated 工具需要显式授权后才能执行。"));
        }

        return proposals;
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
        string ToolName,
        string PolicyClass,
        string StepTitle,
        string StepDescription,
        string Purpose,
        string InputSummary,
        string ApprovalStatus,
        string? BlockedReason);
}