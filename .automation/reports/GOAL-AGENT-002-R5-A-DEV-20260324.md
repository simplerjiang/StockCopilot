# GOAL-AGENT-002-R5-A Development Report (2026-03-24)

## EN

### Scope

- Complete the backend slice for the conversational Copilot redesign.
- Replace the old draft-only turn generation path with a bounded controlled loop that executes approved MCP tools and always closes into a grounded answer or an explicit gap state.

### Actions

- Extended the runtime contract in `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Models/StockAgentRuntimeModels.cs` with:
  - `StockCopilotLoopBudgetDto`,
  - `StockCopilotLoopExecutionDto`,
  - optional `LoopBudget` / `LoopExecution` fields on `StockCopilotTurnDto`.
- Reworked `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/StockCopilotSessionService.cs` so `BuildDraftTurnAsync(...)` no longer returns only planner/governor draft data.
- Injected `IStockCopilotMcpService` into `StockCopilotSessionService` and added a controlled execution loop with explicit budgets for:
  - max rounds,
  - max tool calls,
  - max external search calls,
  - max total latency,
  - max polling steps.
- Modeled the user-requested `4000` polling upper bound as a progress/state budget, not as 4000 real tool executions.
- Added deterministic loop closure logic so the backend now returns one of:
  - `done`,
  - `done_with_gaps`,
  - `failed`,
  instead of leaving the user-visible answer in long-lived `needs_tool_execution`.
- Added MCP result mapping for K-line, minute, strategy, news, and search tools so executed loop steps are written back as `StockCopilotToolResultDto` items.
- Added grounded final-answer synthesis based on returned evidence, features, warnings, degraded flags, and simple bullish/bearish signal hints.
- Preserved the Local-First constraint in final answers when external search remains blocked.
- Updated follow-up action generation so the backend `draft_trading_plan` action is only marked enabled after a grounded `done` answer.

### Tests

- Updated `backend/SimplerJiangAiAgent.Api.Tests/StockCopilotSessionServiceTests.cs` to cover:
  - approved-tool auto execution and grounded `done`,
  - blocked external search preserving Local-First constraints,
  - approved external search executing inside the loop,
  - weak evidence closing as `done_with_gaps`.
- Kept `StockCopilotAcceptanceServiceTests` in the filter to ensure the new turn contract remains compatible with the acceptance baseline path.

### Test Command And Result

- Command: `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "FullyQualifiedName~StockCopilotSessionServiceTests|FullyQualifiedName~StockCopilotAcceptanceServiceTests"`
- Result: passed, 6/6.

### Issues / Notes

- This slice intentionally does not yet change the frontend chat layout.
- The frontend can now receive loop budget, loop execution, executed tool results, and a grounded final answer from the draft endpoint, but the chat-first presentation and stricter trading-plan gating still belong to later R5 slices.
- Browser MCP was not run for this slice because no frontend behavior was changed yet and the scope is backend-only.

## ZH

### 范围

- 完成会话式 Copilot 重构的后端切片。
- 把原先只会生成草案的 turn 生成链路，升级成有边界的受控 loop：自动执行已批准 MCP 工具，并且每轮都必须收口成 grounded answer 或明确的 gap 状态。

### 本轮动作

- 在 `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Models/StockAgentRuntimeModels.cs` 扩展运行时契约，新增：
  - `StockCopilotLoopBudgetDto`，
  - `StockCopilotLoopExecutionDto`，
  - `StockCopilotTurnDto` 上可选的 `LoopBudget` / `LoopExecution` 字段。
- 重写 `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/StockCopilotSessionService.cs`，使 `BuildDraftTurnAsync(...)` 不再只返回 planner/governor 草案。
- 给 `StockCopilotSessionService` 注入 `IStockCopilotMcpService`，并新增受控执行 loop，明确了以下预算：
  - 最大轮次，
  - 最大工具调用数，
  - 最大外部搜索次数，
  - 最大总延迟预算，
  - 最大状态推进轮询步数。
- 把用户提出的 `4000` 上限实现为状态/进度预算，而不是 4000 次真实工具执行。
- 新增确定性 loop 收口逻辑，后端现在会直接返回：
  - `done`，
  - `done_with_gaps`，
  - `failed`，
  而不会再把用户可见回答长期停在 `needs_tool_execution`。
- 为 K 线、分时、策略、新闻、搜索五类 MCP 增加 loop 结果映射，把执行结果写回 `StockCopilotToolResultDto`。
- 基于 evidence、features、warnings、degraded flags 和简化的多空信号提示，增加了 grounded final answer 的后端合成逻辑。
- 当外部搜索仍被阻止时，保留了 final answer 中的 Local-First 约束说明。
- 调整 follow-up action 生成逻辑，后端侧的 `draft_trading_plan` 只有在 grounded `done` 时才会标记为可用。

### 测试

- 更新 `backend/SimplerJiangAiAgent.Api.Tests/StockCopilotSessionServiceTests.cs`，覆盖：
  - 已批准工具自动执行并收口到 grounded `done`；
  - 外部搜索被阻止时仍保留 Local-First 约束；
  - 允许外部搜索时 loop 内会执行 `StockSearchMcp`；
  - 弱证据场景会收口到 `done_with_gaps`。
- 同时把 `StockCopilotAcceptanceServiceTests` 放进同一组过滤，确保新的 turn contract 不会破坏现有 acceptance baseline 链路。

### 测试命令与结果

- 命令：`dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "FullyQualifiedName~StockCopilotSessionServiceTests|FullyQualifiedName~StockCopilotAcceptanceServiceTests"`
- 结果：通过，6/6。

### 当前说明

- 本切片刻意没有改前端聊天布局。
- 当前前端已经可以从 draft 接口拿到 loop budget、loop execution、已执行 tool results 和 grounded final answer，但聊天式主视图和更严格的交易计划 gating 仍属于后续 R5 子任务。
- 本轮没有跑 Browser MCP，因为这次变更是纯后端切片，尚未修改前端交互呈现。