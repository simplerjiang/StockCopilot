# GOAL-AGENT-002 Removal Dev Report - 2026-03-25

## EN

### Summary

Removed the user-facing Stock Copilot, stock assistant chat, and multi-agent analysis surfaces from both frontend and backend public routes. The stock terminal now keeps the right-side area as an empty reserved extension slot for a future redesign.

### Actions

- Removed stock-page UI integration for Copilot sidebar, stock assistant chat, and multi-agent panels from `frontend/src/modules/stocks/StockInfoTab.vue`.
- Removed the stock Copilot developer tab from `frontend/src/App.vue`.
- Deleted obsolete frontend files:
  - `frontend/src/modules/stocks/CopilotPanel.vue`
  - `frontend/src/modules/stocks/StockCopilotSessionPanel.vue`
  - `frontend/src/modules/stocks/StockAgentPanels.vue`
  - `frontend/src/modules/stocks/stockInfoTabAgentRuntime.js`
  - `frontend/src/modules/stocks/stockInfoTabCopilotRuntime.js`
  - `frontend/src/modules/stocks/stockInfoTabCopilot.js`
  - `frontend/src/modules/stocks/stockInfoTabPlanHelpers.js`
  - `frontend/src/modules/admin/StockCopilotDeveloperMode.vue`
- Deleted obsolete frontend tests for removed features and updated remaining StockInfoTab tests to assert the reserved placeholder behavior.
- Removed public backend endpoint exposure for:
  - `/api/stocks/agents*`
  - `/api/stocks/mcp*`
  - `/api/stocks/copilot*`
  - `/api/stocks/chat/sessions*`
- Removed corresponding DI registrations from `backend/SimplerJiangAiAgent.Api/Modules/Stocks/StocksModule.cs`.
- Updated `.automation/tasks.json`, `.automation/state.json`, and `README.md` to reflect that GOAL-AGENT-002 is retired and the feature surface has been removed.

### Validation

- Command: `npm --prefix .\frontend run test:unit -- StockInfoTab`
- Result: passed, 32/32 tests.

- Command: `npm --prefix .\frontend run build`
- Result: passed.

- Command: `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --no-restore`
- Result: passed, 215/215 tests.

### Notes

- The internal backend history service for prior agent analysis data was not fully deleted because the current trading-plan draft service still depends on it. Public routes were removed, and the remaining internal dependency should be reevaluated when the replacement design is implemented.

## ZH

### 摘要

已将用户可见的 Stock Copilot、股票助手聊天、多 Agent 分析模块从前端和后端公开入口中移除。股票终端右侧现在只保留一个预留空白区，等待后续重新设计后再接入新方案。

### 本次操作

- 从 `frontend/src/modules/stocks/StockInfoTab.vue` 中移除了 Copilot 侧栏、股票助手聊天窗口、多 Agent 面板及其运行时绑定。
- 从 `frontend/src/App.vue` 中移除了“股票 Copilot 开发模式”标签页。
- 删除了已废弃的前端组件、运行时文件和对应测试。
- 更新保留的 StockInfoTab 测试，使其断言新的“预留空白区”行为，而不是旧的 AI 侧栏行为。
- 从 `backend/SimplerJiangAiAgent.Api/Modules/Stocks/StocksModule.cs` 中移除了 `agents`、`mcp`、`copilot`、`chat/sessions` 这几组公开接口的路由映射与对应注册。
- 更新 `.automation/tasks.json`、`.automation/state.json`、`README.md`，明确 GOAL-AGENT-002 已退役，当前功能面已移除。

### 验证命令与结果

- `npm --prefix .\frontend run test:unit -- StockInfoTab`
  - 结果：通过，32/32。

- `npm --prefix .\frontend run build`
  - 结果：通过。

- `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --no-restore`
  - 结果：通过，215/215。

### 备注

- 后端内部的 agent history 服务没有被彻底删除，因为当前交易计划草稿服务仍依赖这条内部链路。公开接口已经全部撤掉；等后续新方案明确后，再决定是否把这部分内部实现继续拆除或重构。