# GOAL-AGENT-002-R4 Development Report (2026-03-24)

## EN

### Scope

- Complete the GOAL-AGENT-002-R4 slice for Copilot acceptance and replay metrics.
- Turn the existing Copilot session runtime into a measurable product surface so prompt and runtime changes can be judged by observable quality signals instead of subjective inspection alone.

### Actions

- Added Copilot acceptance DTOs in `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Models/StockAgentRuntimeModels.cs` for request payloads, per-metric output, and the final acceptance baseline envelope.
- Added `IStockCopilotAcceptanceService` and `StockCopilotAcceptanceService` in `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/StockCopilotAcceptanceService.cs`.
- Implemented acceptance scoring for:
  - tool efficiency,
  - evidence coverage,
  - local-first hit rate,
  - external-search trigger rate,
  - final-answer traceability,
  - action readiness,
  - tool latency score,
  - overall score,
  - highlight summaries.
- Reused the existing replay calibration service so the Copilot baseline also shows the symbol-level replay baseline instead of inventing a second historical metric path.
- Registered the service and exposed `POST /api/stocks/copilot/acceptance/baseline` in `backend/SimplerJiangAiAgent.Api/Modules/Stocks/StocksModule.cs`.
- Wired per-workspace acceptance state into `frontend/src/modules/stocks/StockInfoTab.vue`, including loading/error handling, request cancellation, and automatic refresh after draft generation, replay-turn switching, and tool execution.
- Added the new `Copilot 质量基线` and `Replay 基线` cards to `frontend/src/modules/stocks/StockCopilotSessionPanel.vue`.
- Added backend unit coverage in `backend/SimplerJiangAiAgent.Api.Tests/StockCopilotAcceptanceServiceTests.cs`.
- Added frontend coverage in `frontend/src/modules/stocks/StockInfoTab.spec.js`.
- During Browser MCP validation, found a real runtime formatting bug where replay percentages were rendered as `10000%` because replay baseline values were already on a `0-100` scale. Fixed the backend highlight text, frontend formatter, and test/mocked replay values together.

### Test Commands And Results

- Command: `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "FullyQualifiedName~StockCopilotAcceptanceServiceTests|FullyQualifiedName~StockCopilotSessionServiceTests|FullyQualifiedName~StockAgentReplayCalibrationServiceTests"`
- Result: passed, 6/6.

- Command: `npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
- Result: passed, 57/57.

- Command: `npm --prefix .\frontend run build`
- Result: passed. Vite emitted the existing large-chunk warning only.

- Browser MCP validation: open `http://localhost:5121/?tab=stock-info`, query `sh600000`, submit a Copilot question, inspect the newly rendered acceptance cards, execute `StockNewsMcp`, and verify the metrics refresh.
- Result: passed. The page showed `Copilot 质量基线`, `Replay 基线`, replay traceability as `100%`, and no `10000%` regression remained in the fresh runtime.

### Runtime Observations

- The feature is working end to end on a fresh backend-served runtime, not only in unit tests.
- A stale process on `http://localhost:5119` initially served older assets after the frontend rebuild, which masked the percentage-formatting fix during one validation round.
- Re-validating on a clean runtime at `http://localhost:5121` removed that ambiguity and confirmed the source fix was correct.

### Issues

- No blocking issue remains for R4.
- Shared browser sessions can accumulate historical console noise, so acceptance decisions should continue to prefer fresh runtime checks and direct interaction traces over old aggregated console history.

## ZH

### 范围

- 完成 GOAL-AGENT-002-R4 的 Copilot 验收与 replay 指标切片。
- 把现有 Copilot 会话 runtime 提升成“可度量的产品面”，让后续 prompt/runtime 改动不再只能靠主观感觉判断，而是能直接看到质量信号是否变好。

### 本轮动作

- 在 `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Models/StockAgentRuntimeModels.cs` 新增 Copilot acceptance 所需 DTO，包括请求、单项指标和最终 baseline 返回结构。
- 在 `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/StockCopilotAcceptanceService.cs` 新增 `IStockCopilotAcceptanceService` 与 `StockCopilotAcceptanceService`。
- 实现了以下验收指标计算：
  - 工具效率，
  - evidence 覆盖率，
  - local-first 命中率，
  - 外部搜索触发率，
  - 最终回答可追溯度，
  - 动作卡就绪度，
  - 工具延迟得分，
  - overall score，
  - highlights 摘要。
- 直接复用既有 replay calibration 服务，把 symbol 级 replay baseline 一起挂进 Copilot baseline，而不是再发明第二套历史指标链路。
- 在 `backend/SimplerJiangAiAgent.Api/Modules/Stocks/StocksModule.cs` 注册服务并开放 `POST /api/stocks/copilot/acceptance/baseline`。
- 在 `frontend/src/modules/stocks/StockInfoTab.vue` 增加按 workspace 管理的 acceptance state，包括 loading/error、请求取消，以及在生成草案、切换 replay turn、执行工具后自动刷新。
- 在 `frontend/src/modules/stocks/StockCopilotSessionPanel.vue` 新增 `Copilot 质量基线` 与 `Replay 基线` 两张卡片。
- 在 `backend/SimplerJiangAiAgent.Api.Tests/StockCopilotAcceptanceServiceTests.cs` 增加后端单测。
- 在 `frontend/src/modules/stocks/StockInfoTab.spec.js` 增加前端回归覆盖。
- Browser MCP 实测过程中发现了一个真实运行时问题：replay 百分比被重复乘以 100，页面一度显示 `10000%`。随后已同步修复后端 highlight 文案、前端百分比 formatter，以及测试和 mock 数据的取值语义。

### 测试命令与结果

- 命令：`dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "FullyQualifiedName~StockCopilotAcceptanceServiceTests|FullyQualifiedName~StockCopilotSessionServiceTests|FullyQualifiedName~StockAgentReplayCalibrationServiceTests"`
- 结果：通过，6/6。

- 命令：`npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
- 结果：通过，57/57。

- 命令：`npm --prefix .\frontend run build`
- 结果：通过，仅保留已有的 Vite chunk 过大 warning。

- Browser MCP 验证：打开 `http://localhost:5121/?tab=stock-info`，查询 `sh600000`，提交一轮 Copilot 问题，查看新增验收卡片，再执行 `StockNewsMcp` 并确认指标刷新。
- 结果：通过。页面可见 `Copilot 质量基线`、`Replay 基线`，replay traceability 显示为 `100%`，fresh runtime 中不再出现 `10000%` 回归。

### 运行态观察

- 本轮能力已经在 fresh backend-served runtime 中完成端到端验证，不只是单测通过。
- 之前 `http://localhost:5119` 上有一个 stale 进程在前端 rebuild 后仍提供旧 bundle，一度让页面看起来像是修复未生效。
- 改用干净的 `http://localhost:5121` 重新验收后，已确认这是运行态陈旧资产问题，不是源码修复失效。

### 当前问题

- R4 已无新的阻塞项。
- 共享浏览器会话仍可能累积历史 console 噪音，后续验收应继续优先参考 fresh runtime 和定向交互链路，而不是直接把历史累计日志当作当前失败证据。