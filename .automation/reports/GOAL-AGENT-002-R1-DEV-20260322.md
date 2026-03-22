# GOAL-AGENT-002-R1 Development Report (2026-03-22)

## EN
### Summary
Completed the first executable slice of GOAL-AGENT-002-R1 by turning the session contract from planning text into backend code.

This round did not build the full Copilot UI yet. Instead, it established the backend shape that future UI and runtime work can rely on:
1. session/turn/plan/tool/result/final-answer/follow-up DTOs,
2. a draft orchestration service that maps a user question into a planner/governor timeline,
3. a backend endpoint that returns the draft session payload for future panel integration.

### What Changed
1. Extended `StockAgentRuntimeModels.cs` with the new R1 contract models:
   - `StockCopilotTurnDraftRequestDto`
   - `StockCopilotPlanStepDto`
   - `StockCopilotToolCallDto`
   - `StockCopilotToolResultDto`
   - `StockCopilotFinalAnswerDto`
   - `StockCopilotFollowUpActionDto`
   - `StockCopilotTurnDto`
   - `StockCopilotSessionDto`
2. Added `IStockCopilotSessionService` and `StockCopilotSessionService`.
3. Added `POST /api/stocks/copilot/turns/draft`.
4. Registered the new session service in `StocksModule`.
5. Added focused tests for planner/governor draft behavior.

### Runtime Behavior Of The New Draft Service
The new service is intentionally bounded and deterministic:
1. it normalizes the symbol and creates/reuses a chat session key,
2. it classifies the user question into K-line / minute / strategy / news / search intents,
3. it proposes plan steps and tool calls,
4. it applies a governor-style gate to `StockSearchMcp`,
5. it returns a pending final-answer contract that explicitly requires tool execution before grounded output,
6. it returns follow-up actions for later UI wiring.

This is the correct R1 boundary: planner/governor timeline first, actual tool execution and full panel UX later.

### Validation
Because the local API process was already running and locking the default `bin/Debug/net8.0` outputs, the validation used an isolated build output directory instead of stopping the running server.

Commands:
```powershell
dotnet build .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --no-restore -p:OutDir=C:\Users\kong\AiAgent\.automation\tmp\goal-agent002-r1\
dotnet vstest .\.automation\tmp\goal-agent002-r1\SimplerJiangAiAgent.Api.Tests.dll --Tests:"SimplerJiangAiAgent.Api.Tests.StockCopilotSessionServiceTests.BuildDraftTurnAsync_ShouldCreateSessionAndDraftTimeline,SimplerJiangAiAgent.Api.Tests.StockCopilotSessionServiceTests.BuildDraftTurnAsync_ShouldBlockExternalSearchWithoutApproval,SimplerJiangAiAgent.Api.Tests.StockCopilotSessionServiceTests.BuildDraftTurnAsync_ShouldApproveExternalSearchWhenExplicitlyAllowed"
```

Results:
1. isolated build succeeded,
2. targeted tests passed: 3 passed, 0 failed, 0 skipped,
3. diagnostics reported no errors on touched files.

### Notes
1. Browser MCP was not run because this slice is backend-only and does not yet change the user-facing panel behavior.
2. The next natural step on the product-layer track is GOAL-AGENT-002-R2, which can now consume the new `/api/stocks/copilot/turns/draft` payload.

## ZH
### 摘要
本轮完成了 GOAL-AGENT-002-R1 的第一可执行切片：把 session contract 从规划文档真正落成了后端代码。

这一步还没有去做完整 Copilot UI，而是先把后续 UI 和 runtime 都要依赖的后端骨架补齐：
1. session / turn / plan / tool / result / final answer / follow-up DTO，
2. 把用户问题映射成 planner/governor 时间线草案的组装服务，
3. 供后续右侧 Copilot 面板直接消费的后端草案接口。

### 本轮改动
1. 在 `StockAgentRuntimeModels.cs` 新增 R1 contract 模型：
   - `StockCopilotTurnDraftRequestDto`
   - `StockCopilotPlanStepDto`
   - `StockCopilotToolCallDto`
   - `StockCopilotToolResultDto`
   - `StockCopilotFinalAnswerDto`
   - `StockCopilotFollowUpActionDto`
   - `StockCopilotTurnDto`
   - `StockCopilotSessionDto`
2. 新增 `IStockCopilotSessionService` 与 `StockCopilotSessionService`。
3. 新增 `POST /api/stocks/copilot/turns/draft` 接口。
4. 在 `StocksModule` 中注册新的 session service。
5. 新增针对 planner/governor 草案行为的定向单测。

### 新草案服务的运行方式
这个服务当前是有意保持“受控且确定性”的：
1. 先规范 symbol，并创建或复用 chat session key，
2. 根据用户问题识别 K 线 / 分时 / 策略 / 新闻 / 搜索意图，
3. 生成 plan steps 和 tool calls，
4. 对 `StockSearchMcp` 应用 governor 风格的外部搜索闸门，
5. 返回明确标记“必须先执行工具，才能给 grounded final answer”的 final-answer contract，
6. 同时返回后续 UI 可以直接挂接的 follow-up actions。

这正是 R1 应该完成的边界：先把 planner/governor 时间线固定下来，真正的工具执行和完整面板 UX 留给后续切片。

### 校验
因为本地 API 进程正在运行并锁住默认 `bin/Debug/net8.0` 输出目录，本轮没有去停服务，而是改用独立输出目录完成验证。

命令：
```powershell
dotnet build .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --no-restore -p:OutDir=C:\Users\kong\AiAgent\.automation\tmp\goal-agent002-r1\
dotnet vstest .\.automation\tmp\goal-agent002-r1\SimplerJiangAiAgent.Api.Tests.dll --Tests:"SimplerJiangAiAgent.Api.Tests.StockCopilotSessionServiceTests.BuildDraftTurnAsync_ShouldCreateSessionAndDraftTimeline,SimplerJiangAiAgent.Api.Tests.StockCopilotSessionServiceTests.BuildDraftTurnAsync_ShouldBlockExternalSearchWithoutApproval,SimplerJiangAiAgent.Api.Tests.StockCopilotSessionServiceTests.BuildDraftTurnAsync_ShouldApproveExternalSearchWhenExplicitlyAllowed"
```

结果：
1. 隔离构建成功，
2. 定向测试通过：3 通过，0 失败，0 跳过，
3. 修改文件诊断为 0 错误。

### 备注
1. 本轮没有做 Browser MCP，因为这是纯后端 contract 切片，还没有改变用户可见面板行为。
2. 当前产品层主线的自然下一步是 GOAL-AGENT-002-R2，前端可以直接开始消费新的 `/api/stocks/copilot/turns/draft` 载荷。
