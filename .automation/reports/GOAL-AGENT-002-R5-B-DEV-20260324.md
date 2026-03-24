# GOAL-AGENT-002-R5-B Development Report (2026-03-24)

## EN

### Scope

- Harden the `draft_trading_plan` chain so Copilot cannot enter trading-plan drafting from a false-ready state.
- Close Bug 9 at the root cause level instead of only hiding the button.

### Actions

- Added `StockAgentHistoryValidation` in `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/StockAgentHistoryValidation.cs`.
- The new validator checks whether a trading-plan-eligible history contains a complete commander-backed multi-agent package:
  - `stock_news`,
  - `sector_news`,
  - `financial_analysis`,
  - `trend_analysis`,
  - `commander`.
- The validator also requires `commander` to succeed and return a valid `data` object.
- Extended `StockAgentHistoryItemDto` and `StockAgentHistoryDetailDto` with:
  - `IsCommanderComplete`,
  - `CommanderBlockedReason`.
- Updated `/api/stocks/agents/history` list/detail responses to surface commander-completeness metadata.
- Updated `/api/stocks/agents/history` create flow to reject incomplete agent payloads instead of persisting partial results as valid analysis history.
- Updated `TradingPlanDraftService.BuildDraftAsync(...)` to reject incomplete analysis history with an explicit error before any trading-plan draft is produced.
- Updated `frontend/src/modules/stocks/StockInfoTab.vue` so Copilot `draft_trading_plan` gating now requires:
  - approved tool steps already completed,
  - grounded final answer status `done`,
  - a commander-complete saved history when one is selected,
  - or a commander-complete local multi-agent result set before saving a new history.
- Updated the activation path so `runAgents()` no longer persists partial results, and `openTradingPlanDraft()` performs a final completeness check before posting to `/api/stocks/plans/draft`.

### Tests

- Backend targeted test command:
  - `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "FullyQualifiedName~TradingPlanServicesTests"`
  - Result: passed, 19/19.
- Frontend targeted test command:
  - `npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
  - Result: passed, 59/59.

### Browser MCP Validation

- Runtime used: `http://localhost:5119/?tab=stock-info`.
- Verified with Browser MCP:
  - queried `sh600000`,
  - submitted a real Copilot question,
  - observed `POST /api/stocks/copilot/turns/draft` => 200,
  - observed `POST /api/stocks/copilot/acceptance/baseline` => 200,
  - console error count remained 0.
- Limitation:
  - the existing 5119 runtime still exposed an older `action-plan` copy/title string after the code change, which indicates the live backend-served runtime reused a stale frontend bundle or needs a full restart.
  - Because of that, browser validation is sufficient as runtime smoke evidence for request flow and absence of new JS errors, but not yet strong enough to treat current copy-level UI text as final acceptance proof.

### Issues / Notes

- This slice intentionally focuses on plan gating and commander completeness only.
- The chat-first visual redesign still belongs to later R5 frontend slices.

## ZH

### 范围

- 加固 `draft_trading_plan` 链路，防止 Copilot 在“假就绪”状态下进入交易计划起草。
- 这次不是简单把按钮藏起来，而是直接修掉 Bug 9 的根因。

### 本轮动作

- 在 `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/StockAgentHistoryValidation.cs` 新增 `StockAgentHistoryValidation`。
- 新规则会校验可用于交易计划的 history 是否包含完整的 commander 多Agent包：
  - `stock_news`，
  - `sector_news`，
  - `financial_analysis`，
  - `trend_analysis`，
  - `commander`。
- 同时要求 `commander` 必须成功返回，并且携带有效 `data` 对象。
- 扩展 `StockAgentHistoryItemDto` 和 `StockAgentHistoryDetailDto`，新增：
  - `IsCommanderComplete`，
  - `CommanderBlockedReason`。
- 更新 `/api/stocks/agents/history` 的列表和详情返回，让前端能直接看到 commander 完整性状态。
- 更新 `/api/stocks/agents/history` 保存逻辑：不完整的 agent payload 现在会被明确拒绝，不再把部分结果落成看似有效的 analysis history。
- 更新 `TradingPlanDraftService.BuildDraftAsync(...)`：在真正生成交易计划草稿之前，先拒绝不完整 history，并给出明确错误。
- 更新 `frontend/src/modules/stocks/StockInfoTab.vue`：Copilot 的 `draft_trading_plan` 现在必须同时满足：
  - 已批准工具步骤已经完成，
  - final answer 是 grounded `done`，
  - 如果选中了 history，则该 history 必须 commander 完整，
  - 如果要从当前本地 agentResults 现存结果保存 history，则该结果集也必须 commander 完整。
- 更新动作执行路径：`runAgents()` 不再保存部分结果；`openTradingPlanDraft()` 在调用 `/api/stocks/plans/draft` 前会再做一次完整性校验。

### 测试

- 后端定向测试命令：
  - `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "FullyQualifiedName~TradingPlanServicesTests"`
  - 结果：通过，19/19。
- 前端定向测试命令：
  - `npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
  - 结果：通过，59/59。

### Browser MCP 验证

- 使用运行态：`http://localhost:5119/?tab=stock-info`。
- 通过 Browser MCP 已确认：
  - 查询了 `sh600000`，
  - 提交了真实 Copilot 问题，
  - 观察到 `POST /api/stocks/copilot/turns/draft` => 200，
  - 观察到 `POST /api/stocks/copilot/acceptance/baseline` => 200，
  - console 错误数保持为 0。
- 当前限制：
  - 现有 5119 运行态在代码变更后仍然暴露旧的 `action-plan` 文案/title，说明 live backend-served runtime 还复用了旧前端 bundle，或者需要一次完整重启。
  - 因此，这次 Browser MCP 已足够证明请求链路和运行时无新增 JS 错误，但还不足以把当前 copy-level 文案结果当成最终验收证据。

### 当前说明

- 本切片只处理计划 gating 与 commander 完整性。
- 真正的聊天式主视图重排仍属于后续 R5 前端切片。