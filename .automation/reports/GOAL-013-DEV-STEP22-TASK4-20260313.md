# GOAL-013 Step 2.2 Task 4 Dev Report (2026-03-13)

## EN
- Scope: complete Task 4 under GOAL-013 Step 2.2, covering default-model upgrade, explicit Pro analysis trigger, backend routing guardrails, and validation.
- Backend changes:
  - Added `IsPro` to stock-agent request DTOs.
  - Added `StockAgentModelRoutingPolicy` in `StockAgentOrchestrator`.
  - Standard path now resolves to `gemini-3.1-flash-lite-preview-thinking-high`.
  - Pro path now resolves to `gemini-3.1-pro-preview-thinking-medium`.
  - Non-Pro requests are prevented from using the Pro model even if a caller passes that model name.
- Frontend changes:
  - Added separate `启动多Agent` and `Pro 深度分析` buttons.
  - `StockInfoTab` now forwards `isPro` in every single-agent request.
- Config changes:
  - Updated runtime default provider model in `backend/SimplerJiangAiAgent.Api/App_Data/llm-settings.json` to `gemini-3.1-flash-lite-preview-thinking-high`.
- Tests run:
  - `dotnet test backend/SimplerJiangAiAgent.Api.Tests/SimplerJiangAiAgent.Api.Tests.csproj --filter "StockAgentModelRoutingPolicyTests"` -> passed (4/4)
  - `npm --prefix frontend run test:unit -- StockAgentPanels.spec.js StockInfoTab.spec.js` -> passed (21/21)
  - `npm --prefix frontend run build` -> passed
  - `node frontend/scripts/edge-check-goal013.mjs` -> passed
- Notes:
  - Edge validation was initially timing out on slower local stock/news responses; the script timeout was widened from 30s to 90s and revalidated successfully.

## ZH
- 范围：完成 GOAL-013 Step 2.2 的任务4，包括默认模型升级、显式 Pro 分析入口、后端模型路由约束与完整验证。
- 后端改动：
  - 为股票 Agent 请求 DTO 增加 `IsPro`。
  - 在 `StockAgentOrchestrator` 内新增 `StockAgentModelRoutingPolicy`。
  - 普通分析统一路由到 `gemini-3.1-flash-lite-preview-thinking-high`。
  - Pro 分析统一路由到 `gemini-3.1-pro-preview-thinking-medium`。
  - 非 Pro 请求即使误传 Pro 模型名，也会在后端被降级，禁止越权走 Pro。
- 前端改动：
  - 多Agent面板新增 `启动多Agent` 与 `Pro 深度分析` 两个独立按钮。
  - `StockInfoTab` 在逐个 Agent 请求时统一透传 `isPro`。
- 配置改动：
  - 将 `backend/SimplerJiangAiAgent.Api/App_Data/llm-settings.json` 的默认模型改为 `gemini-3.1-flash-lite-preview-thinking-high`。
- 已执行验证：
  - `dotnet test backend/SimplerJiangAiAgent.Api.Tests/SimplerJiangAiAgent.Api.Tests.csproj --filter "StockAgentModelRoutingPolicyTests"` -> 通过（4/4）
  - `npm --prefix frontend run test:unit -- StockAgentPanels.spec.js StockInfoTab.spec.js` -> 通过（21/21）
  - `npm --prefix frontend run build` -> 通过
  - `node frontend/scripts/edge-check-goal013.mjs` -> 通过
- 备注：
  - Edge 校验首次因本机股票详情/本地新闻接口响应较慢而超时，已将脚本等待窗口从 30 秒调整为 90 秒，并复测通过。