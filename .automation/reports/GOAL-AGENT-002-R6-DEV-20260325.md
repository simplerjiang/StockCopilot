# GOAL-AGENT-002-R6 Development Report (2026-03-25)

## EN

### Scope

- Finish the R6 hardening slice opened by the formal post-R5 review.
- Fix the false-empty evidence state on the real auto-loop path.
- Repair the secondary audit layout so history is no longer nested inside the replay grid.
- Add explicit regression coverage for draft turns that ship completed `ToolResults` without `toolPayloads`.

### Actions

- Updated backend Copilot turn contract:
  - `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Models/StockAgentRuntimeModels.cs`
  - `StockCopilotToolResultDto` now carries `Evidence` alongside `EvidenceCount`, warnings, degraded flags, and summary.
- Updated backend draft turn assembly:
  - `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/StockCopilotSessionService.cs`
  - successful tool executions now project MCP envelope evidence directly into `ToolResults`,
  - failed tool executions return an empty evidence list instead of leaving the new contract undefined.
- Updated frontend tool-result compatibility layer:
  - `frontend/src/modules/stocks/stockInfoTabCopilot.js`
  - client-executed manual tool results now also persist `evidence`, so auto-loop and manual-click paths use the same evidence shape.
- Updated the Copilot session panel:
  - `frontend/src/modules/stocks/StockCopilotSessionPanel.vue`
  - main evidence cards and session continuity cards now aggregate from `toolResults.evidence` first and fall back to `toolPayloads` only for older/manual paths,
  - the `历史 turn 列表` card is now a sibling secondary card instead of being incorrectly nested inside the `Replay 基线` grid.
- Updated targeted frontend regressions:
  - `frontend/src/modules/stocks/StockInfoTab.copilot.cases.js`
  - added an R6 regression that submits two turns in the same session and verifies:
    - the active turn renders evidence directly from `ToolResults.evidence`,
    - history continuity still shows a non-zero evidence count without `toolPayloads`,
    - the history card is not nested inside the replay card.

### Tests

- Backend targeted tests:
  - `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "FullyQualifiedName~StockCopilotSessionServiceTests|FullyQualifiedName~StockCopilotAcceptanceServiceTests"`
  - Result: passed, 6/6.
- Frontend targeted unit tests:
  - `npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.copilot.spec.js`
  - Result: passed, 11/11.
- Frontend build:
  - `npm --prefix .\frontend run build`
  - Result: passed.

### Browser / Runtime Validation

- Runtime reachability:
  - `Test-NetConnection localhost -Port 5119 | Select-Object -ExpandProperty TcpTestSucceeded`
  - Result: `True`.
- Backend-served page smoke check:
  - opened `http://localhost:5119/?tab=stock-info`
  - Result: page opened successfully.
- Browser console check:
  - error messages = 0,
  - warning messages = 0.

### Issues / Notes

- No broader backend behavior changed outside the Copilot draft-turn contract, so the verification stayed focused on session/acceptance tests rather than the full backend suite.
- The frontend production build still reports the existing large-chunk warning from Vite output. That is unchanged by R6 and was not expanded in this slice.

## ZH

### 范围

- 完成 post-R5 formal review 打开的 R6 hardening 切片。
- 修复真实 auto-loop 路径里“工具已执行，但证据区仍然为空”的假阴性。
- 修正次级审计区布局，避免历史卡继续嵌套在 replay grid 内部。
- 明确补上“draft 只返回已完成 `ToolResults`、没有 `toolPayloads`”这条真实路径的回归保护。

### 本轮动作

- 更新后端 Copilot turn 契约：
  - `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Models/StockAgentRuntimeModels.cs`
  - `StockCopilotToolResultDto` 现在除了 `EvidenceCount`、warnings、degraded flags 和 summary 之外，还直接携带 `Evidence`。
- 更新后端 draft turn 组装：
  - `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/StockCopilotSessionService.cs`
  - 成功执行的工具会把 MCP envelope 里的 evidence 直接投影进 `ToolResults`，
  - 失败执行则明确返回空 evidence 列表，避免新契约悬空。
- 更新前端工具结果兼容层：
  - `frontend/src/modules/stocks/stockInfoTabCopilot.js`
  - 前端手动点击执行工具后生成的 `toolResults` 现在也会带上 `evidence`，让 auto-loop 路径和手动工具路径统一成同一套 evidence 形状。
- 更新 Copilot 会话面板：
  - `frontend/src/modules/stocks/StockCopilotSessionPanel.vue`
  - 主舞台证据卡和 `会话上下文承接` 现在优先从 `toolResults.evidence` 聚合 evidence，仅在旧路径或手动路径缺字段时回退到 `toolPayloads`，
  - `历史 turn 列表` 已从 `Replay 基线` grid 中移出，恢复为独立 sibling secondary card。
- 更新前端定向回归：
  - `frontend/src/modules/stocks/StockInfoTab.copilot.cases.js`
  - 新增 R6 用例，通过同一 session 连续提交两轮问题，锁定：
    - 当前轮无需手动点工具，也能直接从 `ToolResults.evidence` 渲染证据摘要，
    - 历史承接 evidence 计数在没有 `toolPayloads` 时也不会归零，
    - history card 不再错误嵌套在 replay card 内。

### 测试

- 后端定向测试：
  - `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "FullyQualifiedName~StockCopilotSessionServiceTests|FullyQualifiedName~StockCopilotAcceptanceServiceTests"`
  - 结果：通过，6/6。
- 前端定向单测：
  - `npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.copilot.spec.js`
  - 结果：通过，11/11。
- 前端 build：
  - `npm --prefix .\frontend run build`
  - 结果：通过。

### Browser / Runtime 验证

- 运行端口连通性：
  - `Test-NetConnection localhost -Port 5119 | Select-Object -ExpandProperty TcpTestSucceeded`
  - 结果：`True`。
- backend-served 页面 smoke 检查：
  - 打开 `http://localhost:5119/?tab=stock-info`
  - 结果：页面可成功打开。
- 浏览器控制台检查：
  - error = 0，
  - warning = 0。

### 说明

- 本轮后端改动只触及 Copilot draft-turn 契约，因此验证保持在 session / acceptance 定向测试，没有扩跑整套后端测试。
- 前端生产 build 仍会出现既有的 Vite chunk 过大 warning，这不是 R6 新引入的问题，本轮没有继续扩做 chunk 拆分。 