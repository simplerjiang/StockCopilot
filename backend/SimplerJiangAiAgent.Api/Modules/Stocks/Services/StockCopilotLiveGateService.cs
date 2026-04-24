using System.Text;
using System.Text.Json;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;
using SimplerJiangAiAgent.Api.Modules.Market.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services.IntentClassification;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface IStockCopilotLiveGateService
{
    Task<StockCopilotLiveGateResultDto> RunAsync(StockCopilotLiveGateRequestDto request, CancellationToken cancellationToken = default);
}

public sealed class StockCopilotLiveGateService : IStockCopilotLiveGateService
{
    private const int MaxPlannedToolCalls = 6;
    private const int MaxExternalSearchCalls = 1;
    private const int DefaultReplaySampleTake = 40;
    private const int MaxTotalLatencyMs = 15000;
    private const int MaxPollingSteps = 2000;
    private const int MaxJsonRepairAttempts = 1;
    private const double JsonRepairTemperature = 0.2;
    private const int MaxRepairRawSnippetLength = 600;

    private readonly IStockChatHistoryService _chatHistoryService;
    private readonly ILlmService _llmService;
    private readonly IMcpToolGateway _mcpToolGateway;
    private readonly IRoleToolPolicyService _roleToolPolicyService;
    private readonly IMcpServiceRegistry _mcpServiceRegistry;
    private readonly IStockAgentRoleContractRegistry _roleContractRegistry;
    private readonly IStockMarketContextService _marketContextService;
    private readonly IStockCopilotAcceptanceService _acceptanceService;
    private readonly IQuestionIntentClassifier _intentClassifier;
    private readonly IEvidencePackBuilder _evidencePackBuilder;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StockCopilotLiveGateService> _logger;

    public StockCopilotLiveGateService(
        IStockChatHistoryService chatHistoryService,
        ILlmService llmService,
        IMcpToolGateway mcpToolGateway,
        IRoleToolPolicyService roleToolPolicyService,
        IMcpServiceRegistry mcpServiceRegistry,
        IStockAgentRoleContractRegistry roleContractRegistry,
        IStockMarketContextService marketContextService,
        IStockCopilotAcceptanceService acceptanceService,
        IQuestionIntentClassifier intentClassifier,
        IEvidencePackBuilder evidencePackBuilder,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<StockCopilotLiveGateService> logger)
    {
        _chatHistoryService = chatHistoryService;
        _llmService = llmService;
        _mcpToolGateway = mcpToolGateway;
        _roleToolPolicyService = roleToolPolicyService;
        _mcpServiceRegistry = mcpServiceRegistry;
        _roleContractRegistry = roleContractRegistry;
        _marketContextService = marketContextService;
        _acceptanceService = acceptanceService;
        _intentClassifier = intentClassifier;
        _evidencePackBuilder = evidencePackBuilder;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<StockCopilotLiveGateResultDto> RunAsync(StockCopilotLiveGateRequestDto request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Question);

        var normalizedSymbol = StockSymbolNormalizer.Normalize(request.Symbol);
        var trimmedQuestion = request.Question.Trim();
        var sessionTitle = string.IsNullOrWhiteSpace(request.SessionTitle)
            ? BuildSessionTitle(trimmedQuestion)
            : request.SessionTitle.Trim();
        var provider = string.IsNullOrWhiteSpace(request.Provider) ? "active" : request.Provider.Trim();

        var session = await _chatHistoryService.CreateSessionAsync(normalizedSymbol, sessionTitle, request.SessionKey, cancellationToken);
        var marketContext = await _marketContextService.GetLatestAsync(normalizedSymbol, cancellationToken);
        var checklist = _roleContractRegistry.BuildChecklist();
        var intentTask = _intentClassifier.ClassifyAsync(trimmedQuestion, normalizedSymbol, cancellationToken);
        var prompt = BuildPrompt(normalizedSymbol, trimmedQuestion, request.AllowExternalSearch, marketContext, checklist);

        var intent = await intentTask;
        string? evidenceContext = null;
        if (intent.RequiresRag || intent.RequiresFinancialData)
        {
            var evidencePack = await _evidencePackBuilder.BuildAsync(
                normalizedSymbol, trimmedQuestion, intent.Type, cancellationToken);
            evidenceContext = _evidencePackBuilder.FormatAsPromptContext(evidencePack);
            var conclusionConstraint = BuildStructuredConclusionConstraint(intent.Type);
            if (!string.IsNullOrWhiteSpace(evidenceContext))
            {
                prompt = InjectEvidenceIntoPrompt(prompt, evidenceContext + conclusionConstraint);
            }
            else if (intent.Type is IntentType.Valuation or IntentType.Risk or IntentType.FinancialAnalysis)
            {
                prompt = InjectEvidenceIntoPrompt(prompt,
                    "\n[系统提示：该问题需要财报数据支撑，但当前没有找到相关财报证据。" +
                    "请在回答中明确说明缺少财报依据，建议用户先采集该股票的财报数据。]\n" + conclusionConstraint);
            }
        }

        var (planJson, rawModelResponse, llmTraceId, parseError) = await RequestPlanWithRepairAsync(
            provider,
            request.Model,
            request.Temperature,
            prompt,
            normalizedSymbol,
            trimmedQuestion,
            request.AllowExternalSearch,
            marketContext,
            checklist,
            cancellationToken);

        if (planJson is null)
        {
            return await BuildParseFailureResultAsync(
                normalizedSymbol,
                trimmedQuestion,
                provider,
                request.Model,
                session,
                checklist,
                marketContext,
                prompt,
                rawModelResponse,
                llmTraceId,
                parseError ?? "LLM 计划解析失败。",
                cancellationToken);
        }

        var parsedPlan = ParsePlan(planJson.Value);
        var validation = ValidateToolCalls(parsedPlan.ToolCalls, normalizedSymbol, trimmedQuestion, request.AllowExternalSearch);
        var execution = await ExecuteApprovedToolsAsync(normalizedSymbol, trimmedQuestion, request.TaskId, validation.ApprovedCalls, cancellationToken);

        string finalAnswerText;
        if (execution.ToolResults.Any(t => string.Equals(t.Status, "completed", StringComparison.OrdinalIgnoreCase)))
        {
            finalAnswerText = await SynthesizeAnalysisAsync(
                provider,
                request.Model,
                normalizedSymbol,
                trimmedQuestion,
                parsedPlan.FinalAnswerDraft,
                execution.ToolResults,
                evidenceContext,
                cancellationToken);
        }
        else
        {
            finalAnswerText = parsedPlan.FinalAnswerDraft;
        }

        var finalAnswer = BuildFinalAnswer(
            finalAnswerText,
            llmTraceId,
            execution.ToolResults,
            validation.RejectedCalls,
            execution.StopReason,
            execution.ForcedClose);
        var toolCalls = validation.AllToolCalls;
        var turn = new StockCopilotTurnDto(
            TurnId: $"turn-live-{Guid.NewGuid():N}",
            SessionKey: session.SessionKey,
            Symbol: normalizedSymbol,
            UserQuestion: trimmedQuestion,
            CreatedAt: DateTime.UtcNow,
            Status: finalAnswer.Status,
            PlannerSummary: BuildPlannerSummary(parsedPlan, validation),
            GovernorSummary: BuildGovernorSummary(parsedPlan, validation),
            MarketContext: marketContext,
            PlanSteps: BuildPlanSteps(toolCalls, marketContext, execution.StepStatuses, finalAnswer.Status),
            ToolCalls: toolCalls,
            ToolResults: execution.ToolResults,
            FinalAnswer: finalAnswer,
            FollowUpActions: BuildFollowUpActions(toolCalls, finalAnswer),
            LoopBudget: new StockCopilotLoopBudgetDto(1, MaxPlannedToolCalls, MaxExternalSearchCalls, MaxTotalLatencyMs, MaxPollingSteps),
            LoopExecution: new StockCopilotLoopExecutionDto(
                1,
                execution.ToolExecutions.Count,
                execution.ToolExecutions.Count(item => string.Equals(item.PolicyClass, "external_gated", StringComparison.OrdinalIgnoreCase)),
                execution.TotalLatencyMs,
                Math.Min(MaxPollingSteps, toolCalls.Count + 2),
                finalAnswer.Status,
                execution.StopReason ?? (validation.RejectedCalls.Count > 0 ? "validation_rejections" : "completed"),
                execution.ForcedClose),
            LlmTraceId: llmTraceId);

        var sessionDto = new StockCopilotSessionDto(
            session.SessionKey,
            normalizedSymbol,
            session.Title,
            session.CreatedAt,
            session.UpdatedAt,
            [turn],
            checklist);

        var acceptance = await _acceptanceService.BuildBaselineAsync(
            new StockCopilotAcceptanceBaselineRequestDto(
                normalizedSymbol,
                turn,
                execution.ToolExecutions,
                DefaultReplaySampleTake),
            cancellationToken);

        return new StockCopilotLiveGateResultDto(
            sessionDto,
            acceptance,
            llmTraceId,
            provider,
            request.Model,
            prompt,
            rawModelResponse,
            validation.RejectedCalls);
    }

    private async Task<string> SynthesizeAnalysisAsync(
        string provider,
        string? model,
        string symbol,
        string question,
        string plannerDraft,
        IReadOnlyList<StockCopilotToolResultDto> toolResults,
        string? evidenceContext,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("你是一个专业的股票分析师。基于以下工具执行结果，为用户提供深度分析。");
        sb.AppendLine($"\n## 目标股票: {symbol}");
        sb.AppendLine($"## 用户问题: {question}");

        if (!string.IsNullOrWhiteSpace(evidenceContext))
        {
            sb.AppendLine(evidenceContext);
        }

        sb.AppendLine("\n## 工具执行结果:");
        foreach (var tool in toolResults.Where(t => string.Equals(t.Status, "completed", StringComparison.OrdinalIgnoreCase)))
        {
            sb.AppendLine($"\n### [{tool.ToolName}]");
            if (tool.EvidenceCount > 0 && tool.Evidence?.Count > 0)
            {
                foreach (var ev in tool.Evidence.Take(10))
                {
                    sb.AppendLine($"- {ev.Point}");
                    if (!string.IsNullOrWhiteSpace(ev.Excerpt))
                        sb.AppendLine($"  摘要: {ev.Excerpt}");
                }
            }
            if (!string.IsNullOrWhiteSpace(tool.Summary))
                sb.AppendLine($"工具摘要: {tool.Summary}");
        }

        if (evidenceContext?.Contains("输出格式要求") == true)
        {
            sb.AppendLine("\n请严格按照以下格式输出：");
            sb.AppendLine("### 结论\n简明扼要的核心判断。");
            sb.AppendLine("### 依据\n支撑结论的关键数据点，引用上方工具数据。");
            sb.AppendLine("### 假设\n结论成立的前提条件。");
            sb.AppendLine("### 引用来源\n列出所有引用的数据来源。");
        }
        else
        {
            sb.AppendLine("\n请基于以上数据提供深度分析，直接回答用户的问题。分析必须基于实际数据，不要凭空推测。");
        }

        var request = new LlmChatRequest(
            sb.ToString(),
            model,
            Temperature: 0.3f,
            TraceId: $"live-gate-synthesis-{symbol}",
            MaxOutputTokens: 2048);

        try
        {
            var result = await _llmService.ChatAsync(provider, request, ct);
            return result?.Content ?? plannerDraft;
        }
        catch
        {
            return plannerDraft;
        }
    }

    private async Task<(JsonElement? PlanJson, string RawResponse, string TraceId, string? ParseError)> RequestPlanWithRepairAsync(
        string provider,
        string? model,
        double? temperature,
        string prompt,
        string symbol,
        string question,
        bool allowExternalSearch,
        StockMarketContextDto? marketContext,
        StockCopilotRoleContractChecklistDto checklist,
        CancellationToken cancellationToken)
    {
        var initialResult = await _llmService.ChatAsync(
            provider,
            new LlmChatRequest(prompt, model, temperature, false, ResponseFormat: LlmResponseFormats.Json),
            cancellationToken);

        var currentRawResponse = initialResult.Content?.Trim() ?? string.Empty;
        var currentTraceId = initialResult.TraceId ?? string.Empty;

        if (StockAgentJsonParser.TryParse(currentRawResponse, out var parsedPlan, out var parseError) && parsedPlan is not null)
        {
            return (parsedPlan, currentRawResponse, currentTraceId, null);
        }

        var currentParseError = parseError ?? "LLM 计划解析失败。";
        for (var attempt = 0; attempt < MaxJsonRepairAttempts; attempt++)
        {
            var repairPrompt = BuildRepairPrompt(symbol, question, allowExternalSearch, marketContext, checklist, currentRawResponse, currentParseError);
            var repairResult = await _llmService.ChatAsync(
                provider,
                new LlmChatRequest(
                    repairPrompt,
                    model,
                    Math.Min(temperature ?? JsonRepairTemperature, JsonRepairTemperature),
                    false,
                    ResponseFormat: LlmResponseFormats.Json),
                cancellationToken);

            currentRawResponse = repairResult.Content?.Trim() ?? string.Empty;
            currentTraceId = repairResult.TraceId ?? currentTraceId;

            if (StockAgentJsonParser.TryParse(currentRawResponse, out parsedPlan, out parseError) && parsedPlan is not null)
            {
                return (parsedPlan, currentRawResponse, currentTraceId, null);
            }

            currentParseError = parseError ?? "LLM 计划解析失败。";
        }

        var finalError = MaxJsonRepairAttempts > 0
            ? $"LLM 计划解析失败，repair 后仍不是有效 JSON：{currentParseError}"
            : currentParseError;
        return (null, currentRawResponse, currentTraceId, finalError);
    }

    private string BuildRepairPrompt(
        string symbol,
        string question,
        bool allowExternalSearch,
        StockMarketContextDto? marketContext,
        StockCopilotRoleContractChecklistDto checklist,
        string rawModelResponse,
        string? parseError)
    {
        var builder = new StringBuilder();
        builder.AppendLine("上次输出不是有效 JSON，现在只做一次 JSON repair。");
        AppendRequestContext(builder, symbol, question, allowExternalSearch, marketContext);
        builder.AppendLine();
        builder.AppendLine("只输出一个 JSON object。第一字符必须是 {，最后一个字符必须是 }。不要解释、markdown、代码块、自然语言或思考过程。");
        builder.AppendLine("必须字段：plannerSummary, governorSummary, finalAnswerDraft, toolCalls。toolCalls 项字段：roleId, toolName, purpose, inputSummary。");
        builder.AppendLine($"硬限制：最多 {MaxPlannedToolCalls} 个 toolCalls；最多 {MaxExternalSearchCalls} 个 StockSearchMcp；先本地后外部；direct=false/disabled 角色不得取数。");
        builder.AppendLine("若无工具需求，toolCalls=[]。不要输出“好的，请提供您需要我处理的请求。”之类的对话文本。");
        builder.AppendLine();
        builder.AppendLine("工具注册表：");
        AppendToolRegistrySummary(builder);
        builder.AppendLine("角色约束：");
        AppendRoleConstraintSummary(builder, checklist);
        if (!string.IsNullOrWhiteSpace(parseError))
        {
            builder.AppendLine($"上一次解析错误：{parseError}");
        }

        builder.AppendLine("上一次无效输出片段：");
        builder.AppendLine(BuildRepairRawSnippet(rawModelResponse));
        return builder.ToString().Trim();
    }

    private async Task<StockCopilotLiveGateResultDto> BuildParseFailureResultAsync(
        string symbol,
        string question,
        string provider,
        string? model,
        Data.Entities.StockChatSession session,
        StockCopilotRoleContractChecklistDto checklist,
        StockMarketContextDto? marketContext,
        string prompt,
        string rawModelResponse,
        string llmTraceId,
        string parseError,
        CancellationToken cancellationToken)
    {
        var finalAnswer = new StockCopilotFinalAnswerDto(
            Status: "failed",
            Summary: $"live gate 已调用真实 LLM，但返回计划无法解析：{parseError}",
            GroundingMode: "llm_plan_parse_failed",
            ConfidenceScore: 0.05m,
            NeedsToolExecution: false,
            Constraints:
            [
                "本次仅记录真实 LLM-AUDIT 轨迹，不伪造任何工具计划。",
                $"LLM traceId={llmTraceId}",
                "若要继续，需要修正 prompt 或模型输出格式后重新执行。"
            ]);

        var turn = new StockCopilotTurnDto(
            TurnId: $"turn-live-{Guid.NewGuid():N}",
            SessionKey: session.SessionKey,
            Symbol: symbol,
            UserQuestion: question,
            CreatedAt: DateTime.UtcNow,
            Status: "failed",
            PlannerSummary: "LLM live gate 已触发，但未能产出可解析工具计划。",
            GovernorSummary: "governor 未执行任何工具，因为模型输出未通过 JSON 计划解析。",
            MarketContext: marketContext,
            PlanSteps:
            [
                new StockCopilotPlanStepDto(
                    "planner-1",
                    "planner",
                    "生成 live gate 计划",
                    "要求模型返回受控工具计划 JSON。",
                    "failed",
                    Array.Empty<string>(),
                    null)
            ],
            ToolCalls: Array.Empty<StockCopilotToolCallDto>(),
            ToolResults: Array.Empty<StockCopilotToolResultDto>(),
            FinalAnswer: finalAnswer,
            FollowUpActions:
            [
                new StockCopilotFollowUpActionDto(
                    "action-retry-live-gate",
                    "重试 live gate",
                    "retry_live_gate",
                    null,
                    "修正 prompt 或 provider 配置后重新执行 live gate。",
                    true,
                    null)
            ],
            LoopBudget: new StockCopilotLoopBudgetDto(1, MaxPlannedToolCalls, MaxExternalSearchCalls, MaxTotalLatencyMs, MaxPollingSteps),
            LoopExecution: new StockCopilotLoopExecutionDto(0, 0, 0, 0, 1, "failed", "llm_plan_parse_failed", true),
            LlmTraceId: llmTraceId);

        var sessionDto = new StockCopilotSessionDto(
            session.SessionKey,
            symbol,
            session.Title,
            session.CreatedAt,
            session.UpdatedAt,
            [turn],
            checklist);

        var acceptance = await _acceptanceService.BuildBaselineAsync(
            new StockCopilotAcceptanceBaselineRequestDto(symbol, turn, Array.Empty<StockCopilotToolExecutionMetricDto>(), DefaultReplaySampleTake),
            cancellationToken);

        return new StockCopilotLiveGateResultDto(
            sessionDto,
            acceptance,
            llmTraceId,
            provider,
            model,
            prompt,
            rawModelResponse,
            Array.Empty<StockCopilotRejectedToolCallDto>());
    }

    private ValidationResult ValidateToolCalls(
        IReadOnlyList<ParsedToolCall> plannedCalls,
        string symbol,
        string question,
        bool allowExternalSearch)
    {
        var allToolCalls = new List<StockCopilotToolCallDto>();
        var approvedCalls = new List<ApprovedToolCall>();
        var rejectedCalls = new List<StockCopilotRejectedToolCallDto>();
        var seenCalls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var approvedLocalCount = 0;
        var approvedExternalCount = 0;

        for (var index = 0; index < plannedCalls.Count; index++)
        {
            var plannedCall = plannedCalls[index];
            var callId = $"live-call-{index + 1}";

            if (index >= MaxPlannedToolCalls)
            {
                Reject("tool.plan_budget_exceeded", "tool plan 超出最大工具调用预算。", plannedCall.ToolName, "unknown");
                continue;
            }

            if (string.IsNullOrWhiteSpace(plannedCall.RoleId))
            {
                Reject("role.missing", "roleId 不能为空。", plannedCall.ToolName, "unknown");
                continue;
            }

            if (string.IsNullOrWhiteSpace(plannedCall.ToolName))
            {
                Reject(McpErrorCodes.ToolNotRegistered, "toolName 不能为空。", "unknown", "unknown");
                continue;
            }

            StockCopilotRoleContractDto contract;
            try
            {
                contract = _roleContractRegistry.GetRequired(plannedCall.RoleId);
            }
            catch
            {
                Reject("role.contract_not_registered", $"角色 {plannedCall.RoleId} 未注册，不能执行工具计划。", plannedCall.ToolName, "unknown");
                continue;
            }

            McpToolRegistration registration;
            try
            {
                registration = _mcpServiceRegistry.GetRequired(plannedCall.ToolName);
            }
            catch
            {
                Reject(McpErrorCodes.ToolNotRegistered, $"工具 {plannedCall.ToolName} 未注册，不能执行。", plannedCall.ToolName, "unknown");
                continue;
            }

            var normalizedInput = NormalizeInputSummary(plannedCall.InputSummary);
            var dedupeKey = $"{contract.RoleId}|{registration.ToolName}|{normalizedInput}";
            if (!seenCalls.Add(dedupeKey))
            {
                Reject("tool.duplicate", $"重复的工具计划已被拒绝：{registration.ToolName}。", registration.ToolName, registration.PolicyClass);
                continue;
            }

            var authorization = _roleToolPolicyService.AuthorizeRole(contract.RoleId, registration.ToolName);
            if (!authorization.IsAllowed)
            {
                Reject(authorization.ErrorCode, authorization.Reason ?? $"{contract.RoleId} 未被授权调用 {registration.ToolName}。", registration.ToolName, registration.PolicyClass);
                continue;
            }

            if (!contract.AllowsDirectQueryTools)
            {
                Reject(McpErrorCodes.RoleNotAuthorized, contract.Reason ?? $"{contract.RoleId} 不允许直接调用查询工具。", registration.ToolName, registration.PolicyClass);
                continue;
            }

            if (string.Equals(registration.PolicyClass, "external_gated", StringComparison.OrdinalIgnoreCase) && !allowExternalSearch)
            {
                Reject("tool.external_search_not_allowed", "本轮未开启 allowExternalSearch，external_gated 工具被拒绝。", registration.ToolName, registration.PolicyClass);
                continue;
            }

            if (string.Equals(registration.PolicyClass, "external_gated", StringComparison.OrdinalIgnoreCase) && approvedLocalCount == 0)
            {
                Reject("tool.local_first_required", "必须先执行至少一个 local_required 工具，才能进入 external_gated 搜索。", registration.ToolName, registration.PolicyClass);
                continue;
            }

            if (string.Equals(registration.PolicyClass, "external_gated", StringComparison.OrdinalIgnoreCase) && approvedExternalCount >= MaxExternalSearchCalls)
            {
                Reject("tool.external_search_budget_exceeded", "external_gated 搜索预算已耗尽。", registration.ToolName, registration.PolicyClass);
                continue;
            }

            var input = ParseInputSummary(plannedCall.InputSummary);
            if (input.TryGetValue("symbol", out var requestedSymbol)
                && !string.IsNullOrWhiteSpace(requestedSymbol)
                && !string.Equals(StockSymbolNormalizer.Normalize(requestedSymbol), symbol, StringComparison.OrdinalIgnoreCase))
            {
                Reject("tool.symbol_out_of_scope", $"当前 live gate 只允许查询 {symbol}，不能切换到 {requestedSymbol}。", registration.ToolName, registration.PolicyClass);
                continue;
            }

            var toolCall = new StockCopilotToolCallDto(
                CallId: callId,
                StepId: $"tool-{index + 1}",
                ToolName: registration.ToolName,
                PolicyClass: registration.PolicyClass,
                Purpose: string.IsNullOrWhiteSpace(plannedCall.Purpose) ? BuildDefaultPurpose(registration.ToolName) : plannedCall.Purpose.Trim(),
                InputSummary: BuildEffectiveInputSummary(registration.ToolName, plannedCall.InputSummary, symbol, question),
                ApprovalStatus: "approved",
                BlockedReason: null);

            allToolCalls.Add(toolCall);
            approvedCalls.Add(new ApprovedToolCall(callId, contract.RoleId, registration, toolCall));
            if (string.Equals(registration.PolicyClass, "external_gated", StringComparison.OrdinalIgnoreCase))
            {
                approvedExternalCount += 1;
            }
            else
            {
                approvedLocalCount += 1;
            }

            void Reject(string? errorCode, string reason, string toolName, string policyClass)
            {
                var safeToolName = string.IsNullOrWhiteSpace(toolName) ? "unknown" : toolName.Trim();
                var safePolicy = string.IsNullOrWhiteSpace(policyClass) ? "unknown" : policyClass.Trim();
                allToolCalls.Add(new StockCopilotToolCallDto(
                    CallId: callId,
                    StepId: $"tool-{index + 1}",
                    ToolName: safeToolName,
                    PolicyClass: safePolicy,
                    Purpose: string.IsNullOrWhiteSpace(plannedCall.Purpose) ? BuildDefaultPurpose(safeToolName) : plannedCall.Purpose.Trim(),
                    InputSummary: string.IsNullOrWhiteSpace(plannedCall.InputSummary) ? $"symbol={symbol}" : plannedCall.InputSummary.Trim(),
                    ApprovalStatus: "blocked",
                    BlockedReason: reason));
                rejectedCalls.Add(new StockCopilotRejectedToolCallDto(
                    CallId: callId,
                    RoleId: string.IsNullOrWhiteSpace(plannedCall.RoleId) ? "unknown" : plannedCall.RoleId.Trim(),
                    ToolName: safeToolName,
                    PolicyClass: safePolicy,
                    ApprovalStatus: "blocked",
                    Reason: reason,
                    ErrorCode: errorCode));
            }
        }

        return new ValidationResult(allToolCalls, approvedCalls, rejectedCalls);
    }

    private async Task<ExecutionResult> ExecuteApprovedToolsAsync(
        string symbol,
        string question,
        string? taskId,
        IReadOnlyList<ApprovedToolCall> approvedCalls,
        CancellationToken cancellationToken)
    {
        var stepStatuses = approvedCalls.ToDictionary(item => item.ToolCall.StepId, _ => "approved", StringComparer.OrdinalIgnoreCase);

        // Split into local (parallelizable) and external_gated (must run after locals)
        var localCalls = new List<ApprovedToolCall>();
        var externalCalls = new List<ApprovedToolCall>();
        foreach (var call in approvedCalls)
        {
            if (string.Equals(call.Registration.PolicyClass, "external_gated", StringComparison.OrdinalIgnoreCase))
                externalCalls.Add(call);
            else
                localCalls.Add(call);
        }

        var toolResults = new List<StockCopilotToolResultDto>();
        var toolExecutions = new List<StockCopilotToolExecutionMetricDto>();
        var totalLatencyMs = 0L;
        string? stopReason = null;
        var forcedClose = false;

        // Phase 1: Execute all local tools in parallel
        if (localCalls.Count > 0)
        {
            foreach (var call in localCalls)
                stepStatuses[call.ToolCall.StepId] = "calling_tools";

            var localTasks = localCalls.Select(call =>
                ExecuteApprovedToolAsync(symbol, question, taskId, call, cancellationToken));
            var localOutcomes = await Task.WhenAll(localTasks);

            foreach (var (outcome, call) in localOutcomes.Zip(localCalls))
            {
                toolResults.Add(outcome.ToolResult);
                toolExecutions.Add(outcome.ExecutionMetric);
                stepStatuses[call.ToolCall.StepId] = string.Equals(outcome.ToolResult.Status, "completed", StringComparison.OrdinalIgnoreCase)
                    ? "completed"
                    : "failed";
            }

            // For parallel execution, wall-clock time is the max, not the sum
            totalLatencyMs = localOutcomes.Max(o => Math.Max(0, o.ExecutionMetric.LatencyMs));
        }

        // Phase 2: Execute external_gated tools sequentially (need local results first)
        foreach (var call in externalCalls)
        {
            if (totalLatencyMs >= MaxTotalLatencyMs)
            {
                stopReason = "time_budget_reached";
                forcedClose = true;
                break;
            }

            stepStatuses[call.ToolCall.StepId] = "calling_tools";
            var outcome = await ExecuteApprovedToolAsync(symbol, question, taskId, call, cancellationToken);
            toolResults.Add(outcome.ToolResult);
            toolExecutions.Add(outcome.ExecutionMetric);
            stepStatuses[call.ToolCall.StepId] = string.Equals(outcome.ToolResult.Status, "completed", StringComparison.OrdinalIgnoreCase)
                ? "completed"
                : "failed";
            totalLatencyMs += Math.Max(0, outcome.ExecutionMetric.LatencyMs);
        }

        return new ExecutionResult(toolResults, toolExecutions, stepStatuses, totalLatencyMs, stopReason, forcedClose);
    }

    private async Task<ToolExecutionOutcome> ExecuteApprovedToolAsync(
        string symbol,
        string question,
        string? taskId,
        ApprovedToolCall approvedCall,
        CancellationToken cancellationToken)
    {
        var input = ParseInputSummary(approvedCall.ToolCall.InputSummary);
        var window = ParseWindowOptions(input);
        var toolTaskId = string.IsNullOrWhiteSpace(taskId)
            ? approvedCall.CallId
            : $"{taskId.Trim()}-{approvedCall.CallId}";

        try
        {
            return approvedCall.Registration.ToolName switch
            {
                StockMcpToolNames.CompanyOverview => MapOutcome(
                    approvedCall,
                    await _mcpToolGateway.GetCompanyOverviewAsync(symbol, toolTaskId, window, cancellationToken),
                    envelope => $"公司概况：{envelope.Data.Name}，主营={envelope.Data.MainBusiness ?? "缺失"}，经营范围={envelope.Data.BusinessScope ?? "缺失"}。"),
                StockMcpToolNames.Product => MapOutcome(
                    approvedCall,
                    await _mcpToolGateway.GetProductAsync(symbol, toolTaskId, window, cancellationToken),
                    envelope => $"产品事实 {envelope.Data.FactCount} 条，行业={envelope.Data.Industry ?? "缺失"}，地区={envelope.Data.Region ?? "缺失"}。"),
                StockMcpToolNames.Fundamentals => MapOutcome(
                    approvedCall,
                    await _mcpToolGateway.GetFundamentalsAsync(symbol, toolTaskId, window, cancellationToken),
                    envelope => $"基本面事实总数={envelope.Data.FactCount}，当前返回={envelope.Data.Facts.Count}。"),
                StockMcpToolNames.Shareholder => MapOutcome(
                    approvedCall,
                    await _mcpToolGateway.GetShareholderAsync(symbol, toolTaskId, window, cancellationToken),
                    envelope => $"股东事实 {envelope.Data.FactCount} 条，股东户数={envelope.Data.ShareholderCount?.ToString() ?? "缺失"}。"),
                StockMcpToolNames.MarketContext => MapOutcome(
                    approvedCall,
                    await _mcpToolGateway.GetMarketContextAsync(symbol, toolTaskId, window, cancellationToken),
                    envelope => $"本地市场上下文：个股行业={envelope.Data.StockSectorName ?? "未知"}，主线={envelope.Data.MainlineSectorName ?? "未知"}，主线强度={envelope.Data.MainlineScore?.ToString("0.##") ?? "未知"}。"),
                StockMcpToolNames.SocialSentiment => MapOutcome(
                    approvedCall,
                    await _mcpToolGateway.GetSocialSentimentAsync(symbol, toolTaskId, window, cancellationToken),
                    envelope => $"本地情绪证据聚合状态={envelope.Data.Status}，证据数={envelope.Data.EvidenceCount}。"),
                StockMcpToolNames.Kline => MapOutcome(
                    approvedCall,
                    await _mcpToolGateway.GetKlineAsync(
                        symbol,
                        input.GetValueOrDefault("interval", "day"),
                        ParseInt(input, "count", 60),
                        input.GetValueOrDefault("source"),
                        toolTaskId,
                        window,
                        cancellationToken),
                    envelope => $"K 线窗口={envelope.Data.WindowSize}，趋势={envelope.Data.TrendState}，5D={envelope.Data.Return5dPercent:0.##}%。"),
                StockMcpToolNames.Minute => MapOutcome(
                    approvedCall,
                    await _mcpToolGateway.GetMinuteAsync(symbol, input.GetValueOrDefault("source"), toolTaskId, window, cancellationToken),
                    envelope => $"分时 session={envelope.Data.SessionPhase}，点位={envelope.Data.WindowSize}。"),
                StockMcpToolNames.Strategy => MapOutcome(
                    approvedCall,
                    await _mcpToolGateway.GetStrategyAsync(
                        symbol,
                        input.GetValueOrDefault("interval", "day"),
                        ParseInt(input, "count", 60),
                        input.GetValueOrDefault("source"),
                        ParseStrategies(input.GetValueOrDefault("strategies")),
                        toolTaskId,
                        window,
                        cancellationToken),
                    envelope => $"策略信号 {envelope.Data.Signals.Count} 条。"),
                StockMcpToolNames.News => MapOutcome(
                    approvedCall,
                    await _mcpToolGateway.GetNewsAsync(symbol, input.GetValueOrDefault("level", "stock"), toolTaskId, window, cancellationToken),
                    envelope => $"本地新闻 {envelope.Data.ItemCount} 条，最新时间={envelope.Data.LatestPublishedAt:yyyy-MM-dd HH:mm:ss}。"),
                StockMcpToolNames.Search => MapOutcome(
                    approvedCall,
                    await _mcpToolGateway.SearchAsync(input.GetValueOrDefault("query", question), true, toolTaskId, cancellationToken),
                    envelope => $"外部搜索 provider={envelope.Data.Provider}，结果 {envelope.Data.ResultCount} 条。"),
                StockMcpToolNames.FinancialReport => MapOutcome(
                    approvedCall,
                    await _mcpToolGateway.GetFinancialReportAsync(symbol, ParseInt(input, "periods", 4), toolTaskId, cancellationToken),
                    envelope => $"财报数据: {envelope.Data?.PeriodCount ?? 0}期"),
                StockMcpToolNames.FinancialTrend => MapOutcome(
                    approvedCall,
                    await _mcpToolGateway.GetFinancialTrendAsync(symbol, ParseInt(input, "periods", 8), toolTaskId, cancellationToken),
                    envelope => $"财务趋势: {envelope.Data?.PeriodCount ?? 0}期"),
                StockMcpToolNames.FinancialReportRag => await ExecuteRagToolAsync(approvedCall, symbol, input.GetValueOrDefault("query", question), toolTaskId, cancellationToken),
                _ => BuildFailedOutcome(approvedCall, $"暂不支持工具 {approvedCall.Registration.ToolName} 的 live gate 执行。")
            };
        }
        catch (Exception ex)
        {
            return BuildFailedOutcome(approvedCall, ex.Message);
        }
    }

    private static ToolExecutionOutcome MapOutcome<T>(
        ApprovedToolCall approvedCall,
        StockCopilotMcpEnvelopeDto<T> envelope,
        Func<StockCopilotMcpEnvelopeDto<T>, string> summaryBuilder)
    {
        var summary = summaryBuilder(envelope);
        var toolResult = new StockCopilotToolResultDto(
            approvedCall.CallId,
            approvedCall.Registration.ToolName,
            "completed",
            envelope.TraceId,
            envelope.Evidence.Count,
            envelope.Features.Count,
            envelope.Warnings,
            envelope.DegradedFlags,
            envelope.Evidence,
            summary);

        var executionMetric = new StockCopilotToolExecutionMetricDto(
            approvedCall.CallId,
            approvedCall.Registration.ToolName,
            approvedCall.Registration.PolicyClass,
            envelope.LatencyMs,
            envelope.Evidence.Count,
            envelope.Features.Count,
            envelope.Warnings,
            envelope.DegradedFlags);

        return new ToolExecutionOutcome(toolResult, executionMetric);
    }

    private static ToolExecutionOutcome BuildFailedOutcome(ApprovedToolCall approvedCall, string message)
    {
        var warnings = new[] { message };
        var toolResult = new StockCopilotToolResultDto(
            approvedCall.CallId,
            approvedCall.Registration.ToolName,
            "failed",
            null,
            0,
            0,
            warnings,
            Array.Empty<string>(),
            Array.Empty<StockCopilotMcpEvidenceDto>(),
            $"{approvedCall.Registration.ToolName} 执行失败：{message}");
        var executionMetric = new StockCopilotToolExecutionMetricDto(
            approvedCall.CallId,
            approvedCall.Registration.ToolName,
            approvedCall.Registration.PolicyClass,
            0,
            0,
            0,
            warnings,
            Array.Empty<string>());
        return new ToolExecutionOutcome(toolResult, executionMetric);
    }

    private async Task<ToolExecutionOutcome> ExecuteRagToolAsync(
        ApprovedToolCall approvedCall, string symbol, string question,
        string taskId, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var citations = await _mcpToolGateway.SearchFinancialReportRagAsync(symbol, question, 5, ct);
        sw.Stop();

        if (citations.Count == 0)
        {
            // Fire-and-forget: trigger PDF collection so next query can succeed
            // Strip sh/sz prefix for worker API (same logic as StocksModule.NormalizeFinancialWorkerSymbol)
            var workerSymbol = symbol;
            if (workerSymbol.Length == 8
                && (workerSymbol.StartsWith("sh", StringComparison.OrdinalIgnoreCase)
                    || workerSymbol.StartsWith("sz", StringComparison.OrdinalIgnoreCase)))
            {
                workerSymbol = workerSymbol[2..];
            }
            if (!string.IsNullOrEmpty(workerSymbol))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var workerBaseUrl = _configuration["FinancialWorker:BaseUrl"] ?? "http://localhost:5120";
                        var client = _httpClientFactory.CreateClient();
                        client.Timeout = TimeSpan.FromMinutes(5);
                        await client.PostAsync($"{workerBaseUrl}/api/pdf-collect/{Uri.EscapeDataString(workerSymbol)}", null);
                        _logger.LogInformation("[RAG] Triggered PDF collection for {Symbol} (fire-and-forget)", workerSymbol);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[RAG] Failed to trigger PDF collection for {Symbol}", workerSymbol);
                    }
                });
            }

            var degradedWarnings = new[] { "财报RAG暂无数据，已触发后台索引，稍后重试可获取" };
            var degradedResult = new StockCopilotToolResultDto(
                approvedCall.CallId,
                approvedCall.Registration.ToolName,
                "completed",
                null,
                0,
                0,
                degradedWarnings,
                Array.Empty<string>(),
                Array.Empty<StockCopilotMcpEvidenceDto>(),
                "财报RAG: 暂无数据，已触发后台索引");
            var degradedMetric = new StockCopilotToolExecutionMetricDto(
                approvedCall.CallId,
                approvedCall.Registration.ToolName,
                approvedCall.Registration.PolicyClass,
                sw.ElapsedMilliseconds,
                0,
                0,
                degradedWarnings,
                Array.Empty<string>());
            return new ToolExecutionOutcome(degradedResult, degradedMetric);
        }

        var evidence = citations.Select(c => new StockCopilotMcpEvidenceDto(
            Point: c.Text.Length > 200 ? c.Text[..200] + "…" : c.Text,
            Title: $"{c.ReportDate} {c.Section ?? c.BlockKind}",
            Source: c.Source,
            PublishedAt: null,
            CrawledAt: null,
            Url: null,
            Excerpt: c.Text.Length > 300 ? c.Text[..300] : c.Text,
            Summary: null,
            ReadMode: "rag",
            ReadStatus: "ok",
            IngestedAt: null,
            LocalFactId: null,
            SourceRecordId: c.ChunkId,
            Level: null,
            Sentiment: null,
            Target: symbol,
            Tags: Array.Empty<string>())).ToList();

        var summary = $"财报RAG: 找到{citations.Count}条相关证据";
        var toolResult = new StockCopilotToolResultDto(
            approvedCall.CallId,
            approvedCall.Registration.ToolName,
            "completed",
            taskId,
            evidence.Count,
            0,
            Array.Empty<string>(),
            Array.Empty<string>(),
            evidence,
            summary);

        var executionMetric = new StockCopilotToolExecutionMetricDto(
            approvedCall.CallId,
            approvedCall.Registration.ToolName,
            approvedCall.Registration.PolicyClass,
            sw.ElapsedMilliseconds,
            evidence.Count,
            0,
            Array.Empty<string>(),
            Array.Empty<string>());

        return new ToolExecutionOutcome(toolResult, executionMetric);
    }

    private string BuildPrompt(
        string symbol,
        string question,
        bool allowExternalSearch,
        StockMarketContextDto? marketContext,
        StockCopilotRoleContractChecklistDto checklist)
    {
        var builder = new StringBuilder();
        AppendRequestContext(builder, symbol, question, allowExternalSearch, marketContext);
        builder.AppendLine();
        builder.AppendLine("你是 Stock Copilot 的 live gate planner。只为当前请求生成一次受控 MCP 工具计划，不直接给投资建议。");
        builder.AppendLine();
        builder.AppendLine("输出要求：");
        builder.AppendLine("- 只输出一个 JSON object；第一字符必须是 {，最后一个字符必须是 }；不要解释、markdown、代码块、自然语言或思考过程。");
        builder.AppendLine("- schema={\"plannerSummary\":\"string\",\"governorSummary\":\"string\",\"finalAnswerDraft\":\"string\",\"toolCalls\":[{\"roleId\":\"string\",\"toolName\":\"string\",\"purpose\":\"string\",\"inputSummary\":\"key=value; key=value\"}]}");
        builder.AppendLine("- toolCalls 字段必须存在，可为空数组；若无工具需求必须返回 toolCalls=[]。");
        builder.AppendLine("- finalAnswerDraft 只能写计划草案与执行约束；未执行的工具结论不能当作事实。");
        builder.AppendLine();
        builder.AppendLine("核心约束：");
        builder.AppendLine($"1. roleId 与 toolName 只能来自下方注册表；最多 {MaxPlannedToolCalls} 个 toolCalls，其中最多 {MaxExternalSearchCalls} 个 StockSearchMcp。");
        builder.AppendLine("2. 先本地后外部：local_required / local_preferred 先走本地；external_gated 只有 allowExternalSearch=true 且本地链路先执行后才可规划。");
        builder.AppendLine("3. 若必需本地工具不可用、关键事实不足或角色被 disabled/blocked/direct=false，必须停止该角色，不得改 symbol、伪造 toolName、伪造 fallback 或越权绕过。");
        builder.AppendLine("4. 对返回 evidence 的 MCP，可在 inputSummary 里传 evidenceSkip/evidenceTake；对 StockFundamentalsMcp 还可传 factSkip/factTake。");
        builder.AppendLine("5. 不要反问，不要输出“好的，请提供您需要我处理的请求。”之类的对话文本。");
        builder.AppendLine();
        builder.AppendLine("工具注册表：");
        AppendToolRegistrySummary(builder);
        builder.AppendLine("角色约束：");
        AppendRoleConstraintSummary(builder, checklist);
        builder.AppendLine();
        builder.AppendLine("你生成的 finalAnswerDraft 必须明确说明：这是 live gate 的计划草案，真正可采信的事实以后端 tool results 为准。不要在 finalAnswerDraft 中编造尚未执行的工具结论。");
        return builder.ToString().Trim();
    }

    private static void AppendRequestContext(
        StringBuilder builder,
        string symbol,
        string question,
        bool allowExternalSearch,
        StockMarketContextDto? marketContext)
    {
        builder.AppendLine("当前请求：");
        builder.AppendLine($"symbol={symbol}");
        builder.AppendLine($"question={question}");
        builder.AppendLine($"allowExternalSearch={allowExternalSearch}");
        if (marketContext is not null)
        {
            builder.AppendLine($"marketContext=stageConfidence:{marketContext.StageConfidence:0.##}; mainlineSector:{marketContext.MainlineSectorName ?? "unknown"}; stockSector:{marketContext.StockSectorName ?? "unknown"}; mainlineScore:{marketContext.MainlineScore:0.##}");
        }
    }

    private void AppendToolRegistrySummary(StringBuilder builder)
    {
        var registrations = _mcpServiceRegistry.List();
        var localRequired = registrations
            .Where(item => string.Equals(item.PolicyClass, "local_required", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.ToolName);
        var localPreferred = registrations
            .Where(item => string.Equals(item.PolicyClass, "local_preferred", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.ToolName);
        var externalGated = registrations
            .Where(item => string.Equals(item.PolicyClass, "external_gated", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.ToolName);

        builder.AppendLine($"- local_required={string.Join(", ", localRequired)}");
        if (localPreferred.Any())
        {
            builder.AppendLine($"- local_preferred={string.Join(", ", localPreferred)}");
        }

        builder.AppendLine($"- external_gated={string.Join(", ", externalGated)}");

        builder.AppendLine();
        builder.AppendLine("FinancialReportRag 使用指南：");
        builder.AppendLine("- RAG 数据库包含已解析的上市公司年报/半年报/季报全文（利润表、资产负债表、现金流量表、管理层讨论与分析、公司治理、业务概述等章节）。");
        builder.AppendLine("- query 参数应使用具体的财务术语（如'营业收入 净利润'、'应收账款 坏账准备'、'研发费用 比例'），而非用户原始问题。");
        builder.AppendLine("- 每个角色应根据自身分析需要生成针对性查询，例如：基本面分析师应查询'营收增长 毛利率 ROE'，公司概览分析师应查询'主营业务 行业地位 竞争优势'。");
        builder.AppendLine("- 支持多组查询：用分号分隔多个查询（如 query='营业收入 净利润; 资产负债率 现金流; 管理层分析 风险提示'），系统会分别搜索并合并结果。");
    }

    private static void AppendRoleConstraintSummary(StringBuilder builder, StockCopilotRoleContractChecklistDto checklist)
    {
        foreach (var role in checklist.Roles.Where(item => item.AllowsDirectQueryTools))
        {
            var preferredTools = role.PreferredMcpSequence.Count == 0
                ? "none"
                : string.Join(">", role.PreferredMcpSequence);
            builder.AppendLine($"- {role.RoleId}: direct=true; access={role.ToolAccessMode}; minEvidence={role.MinimumEvidenceCount}; preferred={preferredTools}");
        }

        var noDirectRoles = checklist.Roles
            .Where(item => !item.AllowsDirectQueryTools)
            .Select(item => item.RoleId)
            .ToArray();
        if (noDirectRoles.Length > 0)
        {
            builder.AppendLine($"- {string.Join(", ", noDirectRoles)}: direct=false; access=disabled; preferred=none");
        }
    }

    private static string BuildRepairRawSnippet(string rawModelResponse)
    {
        if (string.IsNullOrWhiteSpace(rawModelResponse))
        {
            return "(empty)";
        }

        var normalized = rawModelResponse.Replace("\r", string.Empty).Trim();
        if (normalized.Length <= MaxRepairRawSnippetLength)
        {
            return normalized;
        }

        return $"{normalized[..MaxRepairRawSnippetLength]}...[truncated {normalized.Length - MaxRepairRawSnippetLength} chars]";
    }

    private static ParsedPlan ParsePlan(JsonElement root)
    {
        var plannerSummary = ReadString(root, "plannerSummary") ?? "LLM 未提供 plannerSummary。";
        var governorSummary = ReadString(root, "governorSummary") ?? "LLM 未提供 governorSummary。";
        var finalAnswerDraft = ReadString(root, "finalAnswerDraft") ?? "LLM 未提供 finalAnswerDraft。";
        var toolCalls = new List<ParsedToolCall>();

        if (root.TryGetProperty("toolCalls", out var toolCallsNode) && toolCallsNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in toolCallsNode.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                toolCalls.Add(new ParsedToolCall(
                    ReadString(item, "roleId") ?? string.Empty,
                    ReadString(item, "toolName") ?? string.Empty,
                    ReadString(item, "purpose") ?? string.Empty,
                    ReadString(item, "inputSummary") ?? string.Empty));
            }
        }

        return new ParsedPlan(plannerSummary.Trim(), governorSummary.Trim(), SanitizeDraftText(finalAnswerDraft), toolCalls);
    }

    private static string BuildPlannerSummary(ParsedPlan parsedPlan, ValidationResult validation)
    {
        var approvedCount = validation.ApprovedCalls.Count;
        var rejectedCount = validation.RejectedCalls.Count;
        return $"{parsedPlan.PlannerSummary} 当前 live gate 计划中，已批准 {approvedCount} 个工具，拦截 {rejectedCount} 个工具。".Trim();
    }

    private static string BuildGovernorSummary(ParsedPlan parsedPlan, ValidationResult validation)
    {
        if (validation.RejectedCalls.Count == 0)
        {
            return $"{parsedPlan.GovernorSummary} governor 未发现越权工具。".Trim();
        }

        var rejectionSummary = string.Join("；", validation.RejectedCalls.Select(item => $"{item.ToolName}:{item.Reason}"));
        return $"{parsedPlan.GovernorSummary} governor 已拦截 {validation.RejectedCalls.Count} 个工具计划：{rejectionSummary}".Trim();
    }

    private static IReadOnlyList<StockCopilotPlanStepDto> BuildPlanSteps(
        IReadOnlyList<StockCopilotToolCallDto> toolCalls,
        StockMarketContextDto? marketContext,
        IReadOnlyDictionary<string, string> stepStatuses,
        string finalStatus)
    {
        var steps = new List<StockCopilotPlanStepDto>
        {
            new(
                "planner-1",
                "planner",
                "生成 live gate 工具计划",
                marketContext is null
                    ? "基于问题生成受控工具计划，并让 governor 在后端强校验。"
                    : $"基于问题与当前本地市场上下文（主线={marketContext.MainlineSectorName ?? "无"}，置信度={marketContext.StageConfidence:0.##}）生成受控工具计划，并让 governor 在后端强校验。",
                "completed",
                Array.Empty<string>(),
                null)
        };

        steps.AddRange(toolCalls.Select(call => new StockCopilotPlanStepDto(
            call.StepId,
            string.Equals(call.ApprovalStatus, "approved", StringComparison.OrdinalIgnoreCase) ? "planner" : "governor",
            BuildToolStepTitle(call.ToolName),
            $"{call.Purpose}。输入：{call.InputSummary}",
            stepStatuses.GetValueOrDefault(call.StepId, call.ApprovalStatus),
            ["planner-1"],
            call.ToolName)));

        steps.Add(new StockCopilotPlanStepDto(
            "commander-1",
            "commander",
            "收口 live gate 回执",
            "只根据 LLM 计划、后端校验结果和已执行 tool results 输出验收结论。",
            finalStatus,
            steps.Where(item => item.ToolName is not null).Select(item => item.StepId).ToArray(),
            null));

        return steps;
    }

    private static StockCopilotFinalAnswerDto BuildFinalAnswer(
        string finalAnswerDraft,
        string llmTraceId,
        IReadOnlyList<StockCopilotToolResultDto> toolResults,
        IReadOnlyList<StockCopilotRejectedToolCallDto> rejectedCalls,
        string? stopReason,
        bool forcedClose)
    {
        var completedCount = toolResults.Count(item => string.Equals(item.Status, "completed", StringComparison.OrdinalIgnoreCase));
        var failedCount = toolResults.Count(item => string.Equals(item.Status, "failed", StringComparison.OrdinalIgnoreCase));
        var totalEvidenceCount = toolResults.Sum(item => item.EvidenceCount);
        var warningCount = toolResults.Sum(item => item.Warnings.Count);
        var status = completedCount > 0 && failedCount == 0 && rejectedCalls.Count == 0 && !forcedClose
            ? "done"
            : completedCount > 0 || rejectedCalls.Count > 0
                ? "done_with_gaps"
                : "failed";
        var confidence = Math.Max(0.08m, Math.Min(0.9m, decimal.Round(0.22m + completedCount * 0.09m + totalEvidenceCount * 0.03m - failedCount * 0.08m - rejectedCalls.Count * 0.05m - warningCount * 0.02m, 2)));
        var toolReceipt = toolResults.Count == 0
            ? "本轮没有执行任何被授权的工具。"
            : "工具回执：" + string.Join("；", toolResults.Select(item => $"{item.ToolName}[{item.Status}] traceId={item.TraceId ?? "n/a"} {item.Summary}"));
        var rejectionReceipt = rejectedCalls.Count == 0
            ? string.Empty
            : " 已拦截工具：" + string.Join("；", rejectedCalls.Select(item => $"{item.RoleId}/{item.ToolName} -> {item.Reason}"));
        var summary = $"{finalAnswerDraft}\n\n{toolReceipt}{rejectionReceipt}".Trim();

        var constraints = new List<string>
        {
            "这是 live gate 验收结论，不是多轮产品化对话结果。",
            "真正可采信事实以后端执行成功的 tool result、traceId 和 evidence 为准。",
            $"LLM traceId={llmTraceId}"
        };

        if (rejectedCalls.Count > 0)
        {
            constraints.Add($"本轮拦截了 {rejectedCalls.Count} 个不合规工具计划。\n任何被拒绝的工具都没有进入执行阶段。".Replace("\\n", "\n"));
        }

        if (!string.IsNullOrWhiteSpace(stopReason))
        {
            constraints.Add($"本轮提前收口原因：{stopReason}。" );
        }

        return new StockCopilotFinalAnswerDto(
            Status: status,
            Summary: summary,
            GroundingMode: "llm_plan_with_tool_receipts",
            ConfidenceScore: confidence,
            NeedsToolExecution: false,
            Constraints: constraints);
    }

    private static IReadOnlyList<StockCopilotFollowUpActionDto> BuildFollowUpActions(
        IReadOnlyList<StockCopilotToolCallDto> toolCalls,
        StockCopilotFinalAnswerDto finalAnswer)
    {
        var actions = new List<StockCopilotFollowUpActionDto>();
        foreach (var toolName in toolCalls.Select(item => item.ToolName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            actions.Add(new StockCopilotFollowUpActionDto(
                $"action-{toolName}",
                $"查看 {toolName} 回执",
                "inspect_tool_receipt",
                toolName,
                $"核对 {toolName} 的 traceId、warnings 与 evidence。",
                true,
                null));
        }

        actions.Add(new StockCopilotFollowUpActionDto(
            "action-next-round",
            "发起下一轮修正",
            "retry_live_gate",
            null,
            "如果需要更严格的工具计划，可以在修正 prompt 或授权后重新运行 live gate。",
            !string.Equals(finalAnswer.Status, "done", StringComparison.OrdinalIgnoreCase),
            string.Equals(finalAnswer.Status, "done", StringComparison.OrdinalIgnoreCase) ? "当前 live gate 已完成，可直接进入人工复核。" : null));

        return actions;
    }

    private static string BuildToolStepTitle(string toolName)
    {
        return toolName switch
        {
            StockMcpToolNames.CompanyOverview => "读取公司概况",
            StockMcpToolNames.Product => "读取产品业务事实",
            StockMcpToolNames.Fundamentals => "读取基本面事实",
            StockMcpToolNames.Shareholder => "读取股东结构事实",
            StockMcpToolNames.MarketContext => "读取市场环境",
            StockMcpToolNames.SocialSentiment => "读取情绪代理",
            StockMcpToolNames.Kline => "读取 K 线结构",
            StockMcpToolNames.Minute => "读取分时结构",
            StockMcpToolNames.Strategy => "读取策略信号",
            StockMcpToolNames.News => "读取本地新闻",
            StockMcpToolNames.Search => "执行外部搜索兜底",
            _ => $"执行 {toolName}"
        };
    }

    private static string BuildDefaultPurpose(string toolName)
    {
        return toolName switch
        {
            StockMcpToolNames.CompanyOverview => "确认公司基础画像",
            StockMcpToolNames.Product => "确认主营和产品业务事实",
            StockMcpToolNames.Fundamentals => "确认基本面财务事实",
            StockMcpToolNames.Shareholder => "确认股东结构",
            StockMcpToolNames.MarketContext => "确认市场阶段和主线",
            StockMcpToolNames.SocialSentiment => "确认情绪代理和证据缺口",
            StockMcpToolNames.Kline => "确认 K 线趋势结构",
            StockMcpToolNames.Minute => "确认盘中结构",
            StockMcpToolNames.Strategy => "确认确定性策略信号",
            StockMcpToolNames.News => "确认本地新闻证据",
            StockMcpToolNames.Search => "在本地证据不足时做外部兜底",
            _ => "执行工具查询"
        };
    }

    private static string BuildEffectiveInputSummary(string toolName, string inputSummary, string symbol, string question)
    {
        var input = ParseInputSummary(inputSummary);
        input["symbol"] = symbol;
        if (string.Equals(toolName, StockMcpToolNames.Search, StringComparison.OrdinalIgnoreCase))
        {
            input["query"] = input.GetValueOrDefault("query", question);
        }

        return string.Join("; ", input.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Select(item => $"{item.Key}={item.Value}"));
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

    private static string NormalizeInputSummary(string inputSummary)
    {
        var input = ParseInputSummary(inputSummary);
        return string.Join(";", input.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Select(item => $"{item.Key}={item.Value}"));
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

    private static IReadOnlyList<string>? ParseStrategies(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var items = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return items.Length == 0 ? null : items;
    }

    private static string BuildStructuredConclusionConstraint(IntentType intentType)
    {
        if (intentType is not (IntentType.Valuation or IntentType.Risk
            or IntentType.FinancialAnalysis or IntentType.PerformanceAttribution))
            return "";

        return """

=== 输出格式要求 ===
由于本次问题涉及财务分析，finalAnswerDraft 必须包含以下四个结构化段落（用 markdown 标题分隔）：

### 结论
简明扼要的核心判断（1-2句话）。

### 依据
支撑结论的关键数据点和事实，必须引用上方提供的财报证据或工具数据。每条依据标注来源。

### 假设
结论成立的前提条件。如果前提变化，结论可能失效。

### 引用来源
列出所有引用的数据来源（财报期间、数据源、工具名称）。

如果上方没有提供足够的财报证据，在"依据"部分明确说明数据不足，不要编造数据。
=== 格式要求结束 ===
""";
    }

    private static string InjectEvidenceIntoPrompt(string prompt, string evidenceContext)
    {
        return prompt + "\n\n" + evidenceContext;
    }

    private static string BuildSessionTitle(string question)
    {
        var compact = question.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return compact.Length <= 24 ? compact : compact[..24];
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var node) && node.ValueKind == JsonValueKind.String
            ? node.GetString()
            : null;
    }

    private static string SanitizeDraftText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "LLM 未提供 finalAnswerDraft。";
        }

        var cleaned = value
            .Replace("**My Thought Process**", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("My Thought Process", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("思考过程", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        return string.IsNullOrWhiteSpace(cleaned)
            ? "LLM 未提供可用的 finalAnswerDraft。"
            : cleaned;
    }

    private sealed record ParsedPlan(
        string PlannerSummary,
        string GovernorSummary,
        string FinalAnswerDraft,
        IReadOnlyList<ParsedToolCall> ToolCalls);

    private sealed record ParsedToolCall(
        string RoleId,
        string ToolName,
        string Purpose,
        string InputSummary);

    private sealed record ApprovedToolCall(
        string CallId,
        string RoleId,
        McpToolRegistration Registration,
        StockCopilotToolCallDto ToolCall);

    private sealed record ValidationResult(
        IReadOnlyList<StockCopilotToolCallDto> AllToolCalls,
        IReadOnlyList<ApprovedToolCall> ApprovedCalls,
        IReadOnlyList<StockCopilotRejectedToolCallDto> RejectedCalls);

    private sealed record ToolExecutionOutcome(
        StockCopilotToolResultDto ToolResult,
        StockCopilotToolExecutionMetricDto ExecutionMetric);

    private sealed record ExecutionResult(
        IReadOnlyList<StockCopilotToolResultDto> ToolResults,
        IReadOnlyList<StockCopilotToolExecutionMetricDto> ToolExecutions,
        IReadOnlyDictionary<string, string> StepStatuses,
        long TotalLatencyMs,
        string? StopReason,
        bool ForcedClose);
}