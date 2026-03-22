# GOAL-AGENT-001 Development Report - 2026-03-22

## English

### Scope

- Completed GOAL-AGENT-001 remaining slices: R2 commander hardening, R3 replay calibration baseline, and R4 stock Copilot MCP runtime foundation.

### Actions

- Added deterministic feature models and runtime models for evidence, trend, valuation, risk, replay metrics, and MCP envelopes.
- Added `StockAgentFeatureEngineeringService` to precompute A-share context hygiene, evidence coverage/conflict/freshness, trend, VWAP, valuation bands, and degraded flags before LLM interpretation.
- Updated stock/sector/financial/trend/commander orchestration contracts so child agents are narrower and commander owns directional output, `directional_bias`, and `probabilities`.
- Added systematic commander penalties for low coverage, conflict, expanded evidence window, and degraded paths.
- Propagated LLM `traceId` into stock-agent result DTOs for replay and audit linkage.
- Added `StockAgentReplayCalibrationService` and `/api/stocks/agents/replay/baseline` to align historical commander outputs with future 1/3/5/10-day returns and derive hit rate, average return, Brier score, grouped win rates, traceable evidence rate, polluted evidence rate, parse repair rate, and revision completeness.
- Added `StockCopilotMcpService` plus `/api/stocks/mcp/kline`, `/api/stocks/mcp/minute`, `/api/stocks/mcp/strategy`, `/api/stocks/mcp/news`, and `/api/stocks/mcp/search` with auditable envelope fields including `traceId`, `taskId`, `warnings`, `degradedFlags`, `cache`, `evidence`, `features`, and `meta`.
- Added backend tests for feature engineering, replay calibration, and MCP envelopes.
- Added Development startup database fallback in `Program.cs` so local `dotnet run --launch-profile http` automatically falls back to SQLite when the configured SQL Server is unreachable.
- Added frontend `股票 Copilot 开发模式` tab and `StockCopilotDeveloperMode.vue` to surface replay, K-line, minute, strategy, news, and search diagnostics directly in the app.
- Refined the frontend Stock Copilot developer page into a clearer runtime dashboard with request-preview chips, summary signal cards, trace/degraded badges, stronger loading and error states, and better responsive styling.
- Expanded the frontend test coverage for the developer page to assert loading state, input sanitization, empty-symbol validation, and failed-request error handling.

### Test Commands And Results

- Command: `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --no-restore`
- Result: passed, 181/181 tests green.
- Command: `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --no-restore --filter "QueryLocalFactDatabaseToolTests|StockCopilotMcpServiceTests"`
- Result: passed, 11/11 targeted tests green after SQLite compatibility fix.
- Command: `npm --prefix .\frontend run test:unit -- src/modules/admin/StockCopilotDeveloperMode.spec.js`
- Result: passed, 4/4 tests green after expanding interaction coverage.
- Runtime smoke: launched backend with process-level SQLite overrides before the fallback work, then verified:
	- `/api/health` -> `{"status":"ok"}`
	- `/api/stocks/agents/replay/baseline?symbol=sh600000&take=20` -> success, `scope=sh600000`, `horizonCount=4`
	- `/api/stocks/mcp/kline?symbol=sh600000&interval=day&count=30&taskId=smoke-kline` -> success, `toolName=StockKlineMcp`, `barCount=30`
	- `/api/stocks/mcp/minute?symbol=sh600000&taskId=smoke-minute` -> success, `toolName=StockMinuteMcp`, `pointCount=256`
	- `/api/stocks/mcp/strategy?symbol=sh600000&interval=day&count=60&strategies=ma,macd,rsi,td&taskId=smoke-strategy` -> success, `signalCount=4`
	- `/api/stocks/mcp/news?symbol=sh600000&level=stock&taskId=smoke-news` -> success, `toolName=StockNewsMcp`, `evidenceCount=20`
	- `/api/stocks/mcp/search?q=浦发银行 最新公告&trustedOnly=true&taskId=smoke-search` -> success with designed degraded fallback, `provider=tavily`, `resultCount=0`, `degradedFlags=external_search_unavailable`
- Runtime smoke: launched `dotnet run --project .\backend\SimplerJiangAiAgent.Api\SimplerJiangAiAgent.Api.csproj --launch-profile http` without SQLite overrides after the fallback work, and `/api/health` returned `{"status":"ok"}` on `http://localhost:5119`.
- Command: `.\start-all.bat`
- Result: frontend built successfully, backend started on `http://localhost:5119`, and the served HTML referenced the updated asset bundle containing the new Stock Copilot developer page.
- Browser MCP validation: opened `http://localhost:5119/?tab=stock-copilot-dev` and confirmed the page rendered `股票 Copilot 开发模式` with live cards for replay, K-line, minute, strategy, news, and search. Observed status summary included `StockKlineMcp`, `StockMinuteMcp`, `signals: 8`, `evidence: 20`, and degraded search marker `external_search_unavailable`.
- Browser MCP re-validation after the UI refinement: confirmed the refreshed page renders the new request-preview region, top summary cards (`当前标的`, `已加载模块`, `降级模块`, `可见 Trace`), and per-card trace/degraded metadata. A manual browser-side refresh returned the page to `6/6` loaded modules with visible `traceId`, `taskId`, and `external_search_unavailable` metadata.

### Issues

- Fixed one runtime-only issue discovered during live smoke: SQLite cannot translate `decimal ORDER BY` in `QueryLocalFactDatabaseTool.BuildSectorFallbackReportsAsync(...)` and `StockMarketContextService.GetLatestAsync(...)`. The fix moves `MainlineScore` ordering to in-memory sorting on the already filtered candidate set.
- Development startup is no longer blocked by local SQL Server availability during normal dev runs because the app now auto-falls back to SQLite when the configured SQL Server connection cannot be opened.
- Browser MCP validation initially showed a stale page state without the new tab; after rebuilding and navigating directly to `?tab=stock-copilot-dev`, the new developer page rendered correctly.

## 中文

### 范围

- 完成 GOAL-AGENT-001 剩余切片：R2 的 commander 收口与硬化、R3 的 replay 校准基线、R4 的股票 Copilot MCP 运行时基础层。

### 实施内容

- 新增确定性特征与运行时模型，覆盖 evidence、趋势、估值、风险、replay 指标与 MCP envelope。
- 新增 `StockAgentFeatureEngineeringService`，在进入 LLM 前先做 A 股上下文净化、证据覆盖率/冲突/新鲜度、趋势、VWAP、估值分层和 degraded flags 计算。
- 收窄 stock/sector/financial/trend/commander 的职责边界，让子 Agent 不再输出半套 commander 结论，并由 commander 统一负责方向、`directional_bias` 和 `probabilities`。
- 新增 coverage/conflict/expanded-window/degraded-path 系统级惩罚逻辑。
- 将 LLM `traceId` 回传到 stock-agent 结果 DTO，方便 replay 与审计串联。
- 新增 `StockAgentReplayCalibrationService` 与 `/api/stocks/agents/replay/baseline`，把历史 commander 结果与未来 1/3/5/10 日收益对齐，输出命中率、平均收益、Brier score、分组胜率、证据可追溯率、污染混入率、解析修复率和改判解释完整率。
- 新增 `StockCopilotMcpService` 与 `/api/stocks/mcp/kline`、`/api/stocks/mcp/minute`、`/api/stocks/mcp/strategy`、`/api/stocks/mcp/news`、`/api/stocks/mcp/search`，统一输出可审计 envelope，包括 `traceId`、`taskId`、`warnings`、`degradedFlags`、`cache`、`evidence`、`features`、`meta` 等字段。
- 补齐后端单测，覆盖 feature engineering、replay calibration 和 MCP envelope。
- 在 `Program.cs` 增加 Development 启动数据库回退逻辑，当配置中的 SQL Server 无法连通时，`dotnet run --launch-profile http` 会自动切到 SQLite。
- 在前端新增 `股票 Copilot 开发模式` 标签页和 `StockCopilotDeveloperMode.vue`，把 replay、K 线、分时、策略、新闻、搜索六类诊断直接暴露到应用内。
- 将前端 Stock Copilot 开发页整理成更清晰的运行态仪表盘，增加请求预览、顶部摘要卡、trace/degraded 元信息、明确的加载态与失败态，以及更完整的响应式样式。
- 扩展该页前端单测，覆盖加载态、输入规范化、空 symbol 校验和请求失败展示。

### 测试命令与结果

- 命令：`dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --no-restore`
- 结果：通过，181/181 全绿。
- 命令：`dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --no-restore --filter "QueryLocalFactDatabaseToolTests|StockCopilotMcpServiceTests"`
- 结果：通过，修复 SQLite 兼容问题后定向 11/11 全绿。
- 命令：`npm --prefix .\frontend run test:unit -- src/modules/admin/StockCopilotDeveloperMode.spec.js`
- 结果：通过，扩展交互断言后 4/4 全绿。
- 运行态 smoke：在回退逻辑落地前，因 `appsettings.Development.json` 默认指向 SQL Server，本轮先使用进程级 SQLite 覆盖启动后端，并实测：
	- `/api/health` -> `{"status":"ok"}`
	- `/api/stocks/agents/replay/baseline?symbol=sh600000&take=20` -> 成功，`scope=sh600000`，`horizonCount=4`
	- `/api/stocks/mcp/kline?symbol=sh600000&interval=day&count=30&taskId=smoke-kline` -> 成功，`toolName=StockKlineMcp`，`barCount=30`
	- `/api/stocks/mcp/minute?symbol=sh600000&taskId=smoke-minute` -> 成功，`toolName=StockMinuteMcp`，`pointCount=256`
	- `/api/stocks/mcp/strategy?symbol=sh600000&interval=day&count=60&strategies=ma,macd,rsi,td&taskId=smoke-strategy` -> 成功，`signalCount=4`
	- `/api/stocks/mcp/news?symbol=sh600000&level=stock&taskId=smoke-news` -> 成功，`toolName=StockNewsMcp`，`evidenceCount=20`
	- `/api/stocks/mcp/search?q=浦发银行 最新公告&trustedOnly=true&taskId=smoke-search` -> 成功，按设计降级，`provider=tavily`，`resultCount=0`，`degradedFlags=external_search_unavailable`
- 运行态 smoke：在回退逻辑完成后，直接执行 `dotnet run --project .\backend\SimplerJiangAiAgent.Api\SimplerJiangAiAgent.Api.csproj --launch-profile http`，未再附加 SQLite 覆盖，`http://localhost:5119/api/health` 返回 `{"status":"ok"}`。
- 命令：`.\start-all.bat`
- 结果：前端构建成功，后端在 `http://localhost:5119` 启动成功，首页实际引用了包含新 Stock Copilot 开发页代码的最新静态资源包。
- Browser MCP 验证：打开 `http://localhost:5119/?tab=stock-copilot-dev`，确认页面真实渲染出 `股票 Copilot 开发模式`，并展示 replay、K 线、分时、策略、新闻、搜索六块实时结果卡片。页面可见 `StockKlineMcp`、`StockMinuteMcp`、`signals: 8`、`evidence: 20`，以及搜索降级标记 `external_search_unavailable`。
- Browser MCP 二次验证：在 UI 整理后，确认页面展示了新增的“请求预览”区域、顶部摘要卡（`当前标的`、`已加载模块`、`降级模块`、`可见 Trace`）以及每张卡片的 `traceId` / `taskId` / degraded 元信息。浏览器内手动触发一次“刷新诊断”后，页面回到 `6/6` 已加载状态，并继续可见 `external_search_unavailable` 等诊断信息。

### 问题记录

- 本轮运行态新发现并已修复一处兼容问题：SQLite 不支持把 `decimal` 直接下推到 `ORDER BY`，导致 `QueryLocalFactDatabaseTool.BuildSectorFallbackReportsAsync(...)` 与 `StockMarketContextService.GetLatestAsync(...)` 在 MCP 真机请求时返回 500。现已改为在过滤后的候选集上做内存排序。
- 本地 Development 启动已不再依赖本机 SQL Server 可用性，正常 `dotnet run --launch-profile http` 在 SQL Server 不可达时会自动回退到 SQLite。
- Browser MCP 验证初始命中了浏览器旧页面状态；重新构建并直接导航到 `?tab=stock-copilot-dev` 后，新开发模式页已正常展示。