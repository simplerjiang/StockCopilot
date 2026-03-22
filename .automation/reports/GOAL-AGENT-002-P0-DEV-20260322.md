# GOAL-AGENT-002-P0 Development Report - 2026-03-22

## English

### Scope

- Completed the buglist stabilization and output-safety slice for GOAL-AGENT-002 P0.

### Actions

- Added SQLite-compatible idempotent column patching to `MarketSentimentSchemaInitializer` so local SQLite fallback can serve the newer market sentiment and sector rotation fields without crashing `/api/market/sectors`.
- Removed `社媒优化` and `社媒爬虫` from the root navigation in `frontend/src/App.vue`.
- Fixed `JsonFileLlmSettingsStore` so optional fields are overwritten even when the new value is an explicit empty string.
- Added `LocalFactDisplayPolicy` and applied it to local-fact enrichment/query paths so already-clear Chinese titles do not display distorted translated copies, and stock-news buckets require strong stock relevance instead of accepting generic board/market items.
- Reworked `ChatWindow.vue` history persistence into a serialized save queue, stopped chunk-by-chunk save storms during streaming, and sanitized assistant content before saving or rendering.
- Tightened `SourceGovernanceReadService` and the developer-mode Vue page so request content is redacted and reasoning-style response scaffolding is replaced by either extracted structured JSON or the redacted message `返回内容包含中间推理，已脱敏。`.
- Added backend regression tests for settings clearing, local-fact filtering/title suppression, and developer-mode log sanitization.
- Updated frontend chat-related tests to wait for async session setup and serialized save behavior.

### Test Commands And Results

- Command: `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "JsonFileLlmSettingsStoreTests|SourceGovernanceReadServiceTests|QueryLocalFactDatabaseToolTests"`
- Result: passed, 20/20 targeted backend tests green.
- Command: `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "SourceGovernanceReadServiceTests"`
- Result: passed, 7/7 targeted sanitization tests green after tightening developer-mode redaction.
- Command: `npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js src/modules/stocks/StockRecommendTab.spec.js`
- Result: passed, 51/51 targeted frontend tests green.
- Command: `.\start-all.bat`
- Result: frontend rebuilt successfully and the backend served the updated app on `http://localhost:5119`.
- Browser MCP validation: opened `http://localhost:5119/?tab=source-governance-dev&v=bugfix-check-2`, confirmed top navigation no longer shows `社媒优化` or `社媒爬虫`, logged into Developer Mode with default admin credentials, enabled the dashboard, and verified the LLM log panel now displays `请求内容已脱敏；界面仅保留必要元数据与结构化 JSON。` plus `返回内容包含中间推理，已脱敏。` instead of raw “My Thought Process / Defining the Scope” text.

### Issues

- Browser MCP initially showed stale frontend state with the removed social tabs still visible. A fresh navigation to the rebuilt app confirmed the source change was correct and the running page updated after reload.
- Targeted backend tests were temporarily blocked twice by a running `SimplerJiangAiAgent.Api.exe` process locking the build output. The process was stopped before rerunning tests.

### Follow-up Recheck

- A second independent verification pass found one remaining runtime issue: long-running `/api/stocks/mcp/kline` and `/api/stocks/mcp/strategy` requests could surface as blank 500s when the client or browser tooling canceled the request before the backend completed.
- Added `StockMcpEndpointExecutor` and routed `/api/stocks/mcp/kline`, `/api/stocks/mcp/minute`, `/api/stocks/mcp/strategy`, `/api/stocks/mcp/news`, and `/api/stocks/mcp/search` through it so `RequestAborted` now returns HTTP 499 with `{ "message": "请求已取消" }` instead of bubbling into an unhandled 500.
- Added `StockMcpEndpointExecutorTests` to lock three cases: normal success returns 200, canceled requests return 499, and unrelated `OperationCanceledException` still rethrows when the request token was not canceled.

### Follow-up Test Commands And Results

- Command: `dotnet build .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj -c Debug -o .\.automation\tmp\recheck-backend2\out` then `dotnet vstest .\.automation\tmp\recheck-backend2\out\SimplerJiangAiAgent.Api.Tests.dll --TestCaseFilter:"FullyQualifiedName~StockMcpEndpointExecutorTests"`
- Result: passed, 3/3 new backend tests green.
- Command: Browser stock page recheck on a fresh session key `sh600000-1774155740107`, then `GET /api/stocks/chat/sessions/{sessionKey}/messages`
- Result: passed, chat save/load returned 200 and the new session persisted both the user message and the assistant reply.
- Command: timeout probe `Invoke-WebRequest http://localhost:5000/api/stocks/mcp/strategy?...taskId=cancel-probe-2 -TimeoutSec 1`
- Result: client timed out as expected, and backend log recorded `GET /api/stocks/mcp/strategy -> 499 (1015ms)` instead of 500.
- Command: `Invoke-WebRequest http://localhost:5000/api/stocks/mcp/kline?...taskId=final-check-kline` and `Invoke-WebRequest http://localhost:5000/api/stocks/mcp/strategy?...taskId=final-check-strategy`
- Result: both normal MCP calls returned 200 on the rebuilt backend (`StockKlineMcp` latency 4883ms, `StockStrategyMcp` latency 3729ms).

## 中文

### 范围

- 完成 GOAL-AGENT-002 P0 的 buglist 稳定性与输出安全收口。

### 实施内容

- 在 `MarketSentimentSchemaInitializer` 中补齐 SQLite 幂等补列逻辑，使本地 SQLite 回退场景也能承载新版本 `情绪轮动` / 板块轮动字段，不再因为旧表缺列导致 `/api/market/sectors` 500。
- 在 `frontend/src/App.vue` 移除了顶层导航中的 `社媒优化` 与 `社媒爬虫`。
- 修复 `JsonFileLlmSettingsStore`，让可选字段在新值为显式空字符串时也会真正写回，从而支持清空 `Project` / `Organization` / `BaseUrl` / `Model` / `SystemPrompt`。
- 新增 `LocalFactDisplayPolicy`，并接入本地事实 enrichment/query 流程：已经是清晰中文的标题不再展示失真翻译副本，个股资讯桶只接受与标的强相关的资讯，不再混入泛板块/大盘项。
- 重构 `ChatWindow.vue` 历史保存逻辑为串行保存队列，停止流式返回期间按 chunk 疯狂保存，同时在保存和渲染前对助手文本做推理脚手架清洗。
- 收紧 `SourceGovernanceReadService` 与治理开发者模式前端展示：请求内容统一脱敏，响应中若包含 reasoning-style 脚手架，则优先提取结构化 JSON，否则显示 `返回内容包含中间推理，已脱敏。`。
- 新增后端回归测试，覆盖设置清空、本地事实筛选/标题抑制、开发者模式日志脱敏。
- 同步调整前端聊天相关测试，使其等待异步会话初始化和串行保存后的稳定态。

### 测试命令与结果

- 命令：`dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "JsonFileLlmSettingsStoreTests|SourceGovernanceReadServiceTests|QueryLocalFactDatabaseToolTests"`
- 结果：通过，后端定向 20/20 全绿。
- 命令：`dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "SourceGovernanceReadServiceTests"`
- 结果：通过，开发者模式脱敏补强后定向 7/7 全绿。
- 命令：`npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js src/modules/stocks/StockRecommendTab.spec.js`
- 结果：通过，前端定向 51/51 全绿。
- 命令：`.\start-all.bat`
- 结果：前端重新构建成功，后端在 `http://localhost:5119` 提供最新页面。
- Browser MCP 验证：打开 `http://localhost:5119/?tab=source-governance-dev&v=bugfix-check-2`，确认顶层导航已不再出现 `社媒优化` / `社媒爬虫`；使用默认管理员账号登录并开启 Developer Mode 后，LLM 日志面板展示为 `请求内容已脱敏；界面仅保留必要元数据与结构化 JSON。` 与 `返回内容包含中间推理，已脱敏。`，不再直接外露 `My Thought Process / Defining the Scope` 等中间推理文本。

### 问题记录

- Browser MCP 初次校验命中了浏览器旧页面状态，因此一度仍看到已删除的社媒占位页签；重新导航到最新构建后的地址后，页面已正确更新。
- 后端定向测试两次被正在运行的 `SimplerJiangAiAgent.Api.exe` 锁住构建输出；停止进程后重跑已全部通过。

### 复核补修

- 第二轮独立复核又发现一个剩余运行态问题：当 `/api/stocks/mcp/kline` 与 `/api/stocks/mcp/strategy` 这类长请求被客户端或浏览器工具提前取消时，后端会把 `RequestAborted` 直接冒泡成空白 500。
- 为此新增 `StockMcpEndpointExecutor`，并让 `/api/stocks/mcp/kline`、`/api/stocks/mcp/minute`、`/api/stocks/mcp/strategy`、`/api/stocks/mcp/news`、`/api/stocks/mcp/search` 统一经过这层执行器，把请求取消映射为 HTTP 499 和 `{ "message": "请求已取消" }`，不再误报 500。
- 同步新增 `StockMcpEndpointExecutorTests`，锁定三类行为：正常成功返回 200、已取消请求返回 499、与请求 token 无关的 `OperationCanceledException` 继续抛出，避免误吞真正异常。

### 复核测试命令与结果

- 命令：`dotnet build .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj -c Debug -o .\.automation\tmp\recheck-backend2\out`，随后 `dotnet vstest .\.automation\tmp\recheck-backend2\out\SimplerJiangAiAgent.Api.Tests.dll --TestCaseFilter:"FullyQualifiedName~StockMcpEndpointExecutorTests"`
- 结果：通过，新增后端定向 3/3 全绿。
- 命令：Browser MCP 在股票页新建会话 `sh600000-1774155740107`，随后回读 `GET /api/stocks/chat/sessions/{sessionKey}/messages`
- 结果：通过，聊天保存与回读均返回 200，新会话内持久化了用户消息与助手回复。
- 命令：`Invoke-WebRequest http://localhost:5000/api/stocks/mcp/strategy?...taskId=cancel-probe-2 -TimeoutSec 1`
- 结果：客户端按预期超时，但后端日志记录为 `GET /api/stocks/mcp/strategy -> 499 (1015ms)`，不再是 500。
- 命令：`Invoke-WebRequest http://localhost:5000/api/stocks/mcp/kline?...taskId=final-check-kline` 与 `Invoke-WebRequest http://localhost:5000/api/stocks/mcp/strategy?...taskId=final-check-strategy`
- 结果：两条正常 MCP 请求均返回 200；`StockKlineMcp` 延迟 4883ms，`StockStrategyMcp` 延迟 3729ms。