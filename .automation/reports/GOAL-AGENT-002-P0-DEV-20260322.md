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

### 2026-03-22 Additional Bugfix Round

- Fixed the still-open market sentiment page failure by tracing the runtime 500s to `SectorRotationQueryService`: EF Core SQLite cannot translate `decimal` expressions inside `ORDER BY / THEN BY`.
- Reworked `/api/market/sectors` and `/api/market/mainline` query paths to load the latest snapshot rows first, then sort and page in memory, which preserves behavior while avoiding the SQLite translation failure.
- Added a SQLite-backed regression test in `SectorRotationQueryServiceTests` so this exact failure is covered by a real SQLite provider instead of only the in-memory provider.
- Aligned `SourceGovernanceDeveloperMode.spec.js` with the current UI contract (`请求摘要 / 返回摘要`) to remove the reproducible front-end test regression.
- Expanded reasoning-scaffold sanitization across three layers: `ChatWindow.vue`, `SourceGovernanceReadService`, and `SourceGovernanceDeveloperMode.vue` now strip title-style prefixes such as `Considering the Request`, `Analyzing the Scenario`, `Refining the Strategy`, and `before answering`.
- Added a backend regression test that keeps useful answer text after stripping a reasoning title scaffold while still redacting reasoning-only output.

### 2026-03-22 Additional Validation

- Command: `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~SectorRotationQueryServiceTests|FullyQualifiedName~SourceGovernanceReadServiceTests"`
- Result: passed, 12/12 targeted backend tests green.

- Command: `npm --prefix .\frontend run test:unit -- src/modules/admin/SourceGovernanceDeveloperMode.spec.js src/modules/stocks/StockRecommendTab.spec.js`
- Result: passed, 12/12 targeted frontend tests green.

- Command: run backend in Development mode, then request `GET http://localhost:5119/api/market/sectors?boardType=concept&page=1&pageSize=3&sort=strength` and `GET http://localhost:5119/api/market/mainline?boardType=concept&window=10d&take=3`
- Result: passed; both endpoints returned HTTP 200 with non-empty sector payloads after the SQLite sorting fix.

- Browser MCP validation: navigated to `http://localhost:5119/?tab=market-sentiment` on a fresh session and confirmed the page now renders the sector leaderboard, snapshot cards, and right-side detail panel instead of the previous full-page `情绪轮动数据加载失败` state.

### 2026-03-22 Remaining Risk

- While rechecking the stock page in Browser MCP, the backend connection dropped again and the page fell into `ERR_CONNECTION_REFUSED` for multiple `/api/stocks/*` requests. The chart blank issue could not be cleanly re-verified in this round because the run hit the broader runtime-stability problem already tracked as bug 8.

### 2026-03-22 Stock Runtime Retry Follow-up

- Added bounded retry handling in `frontend/src/modules/stocks/StockInfoTab.vue` for stock-tab internal GET requests (`/api/stocks/chart`, `/api/stocks/detail/cache`, `/api/stocks/plans`, `/api/stocks/plans/alerts`, `/api/stocks/quote`, sidebar/news/realtime reads, etc.).
- The retry path only applies to transient connection-style failures such as `Failed to fetch` / `ERR_CONNECTION_REFUSED`, honors `AbortController`, and keeps the retry window short so normal errors still surface quickly.
- Added two regression tests in `frontend/src/modules/stocks/StockInfoTab.spec.js`:
	- transient first-load chart failure retries and still renders stock detail
	- transient trading-plan board failure retries and still renders the board content
- Rebuilt and relaunched the packaged app via `start-all.bat`, then rechecked `http://localhost:5119/?tab=stock-info` in Browser MCP.
- In the packaged recheck, selecting `浦发银行 sh600000` now reached the active stock workspace, the page showed `浦发银行（sh600000）`, and browser-side DOM checks confirmed there was no `暂无 K 线数据`, no `暂无分时数据`, and no visible `ERR_CONNECTION_REFUSED` state.
- During the same packaged interaction window, command-line health probes to `http://localhost:5119/api/health` returned `{"status":"ok"}` before and after the stock interaction.

### 2026-03-22 Stock Runtime Retry Validation

- Command: `Set-Location .\frontend; npx vitest run src/modules/stocks/StockInfoTab.spec.js`
- Result: passed, 51/51 targeted stock-tab tests green, including the two new transient-failure retry regressions.
- Command: `C:\Users\kong\AiAgent\start-all.bat`
- Result: passed, rebuilt frontend/backend/desktop package and relaunched the packaged desktop successfully.
- Browser MCP validation: reopened `http://localhost:5119/?tab=stock-info`, selected `浦发银行 sh600000`, confirmed the active stock workspace and `专业图表终端` rendered without `暂无 K 线数据` / `暂无分时数据`, and verified packaged backend health remained `ok` during the interaction.

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

### 2026-03-22 本轮追加修复

- 针对仍未关闭的情绪轮动页 500，已把运行态根因定位到 `SectorRotationQueryService`：EF Core 的 SQLite provider 不支持把 `decimal` 表达式翻译进 `ORDER BY / THEN BY`。
- 已把 `/api/market/sectors` 与 `/api/market/mainline` 改成“先取最新快照，再在内存排序与分页”的兼容实现，保留原有排序语义，同时绕开 SQLite 翻译限制。
- 在 `SectorRotationQueryServiceTests` 中新增真实 SQLite provider 回归测试，不再只依赖 InMemory provider。
- 将 `SourceGovernanceDeveloperMode.spec.js` 的断言文案同步为当前页面契约 `请求摘要 / 返回摘要`，消除可稳定复现的前端单测回归。
- 在 `ChatWindow.vue`、`SourceGovernanceReadService`、`SourceGovernanceDeveloperMode.vue` 三层同时扩展 reasoning 标题脚手架清洗，覆盖 `Considering the Request`、`Analyzing the Scenario`、`Refining the Strategy`、`before answering` 等标题式泄露。
- 新增后端回归测试，锁定“去掉 reasoning 标题但保留真实答案”的行为，同时确保纯推理输出仍会被脱敏成固定提示。

### 2026-03-22 本轮追加验证

- 命令：`dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~SectorRotationQueryServiceTests|FullyQualifiedName~SourceGovernanceReadServiceTests"`
- 结果：通过，后端定向 12/12 全绿。

- 命令：`npm --prefix .\frontend run test:unit -- src/modules/admin/SourceGovernanceDeveloperMode.spec.js src/modules/stocks/StockRecommendTab.spec.js`
- 结果：通过，前端定向 12/12 全绿。

- 命令：Development 模式启动后端后，访问 `GET http://localhost:5119/api/market/sectors?boardType=concept&page=1&pageSize=3&sort=strength` 与 `GET http://localhost:5119/api/market/mainline?boardType=concept&window=10d&take=3`
- 结果：通过；SQLite 排序修复后，两条接口均返回 HTTP 200，且正文含非空板块数据。

- Browser MCP：新会话打开 `http://localhost:5119/?tab=market-sentiment`，已确认页面能正常渲染板块榜、顶部快照和右侧详情栏，不再出现整页 `情绪轮动数据加载失败`。

### 2026-03-22 剩余风险

- 在 Browser MCP 继续复核股票页时，后端连接再次掉线，多个 `/api/stocks/*` 请求出现 `ERR_CONNECTION_REFUSED`。因此本轮没能在稳定运行态下继续关闭图表空白问题；当前阻塞点更接近已记录的 bug 8 运行态不稳定，而不是单一图表渲染链路。

### 2026-03-22 股票运行态重试补强

- 在 `frontend/src/modules/stocks/StockInfoTab.vue` 为股票页内部 GET 请求补上了有界重试，包括 `/api/stocks/chart`、`/api/stocks/detail/cache`、`/api/stocks/plans`、`/api/stocks/plans/alerts`、`/api/stocks/quote` 以及侧栏资讯/实时总览等读取链路。
- 重试仅针对 `Failed to fetch` / `ERR_CONNECTION_REFUSED` 这类短时连接错误，且保留 `AbortController` 取消语义，避免把正常业务错误吞掉。
- 在 `frontend/src/modules/stocks/StockInfoTab.spec.js` 中新增两条前端回归测试：
	- 首次图表请求瞬时失败后自动重试并成功显示个股详情
	- 交易计划总览首次请求瞬时失败后自动重试并成功恢复内容
- 重新执行 `start-all.bat` 完成前端、后端和桌面壳打包重启后，再次用 Browser MCP 复核 `http://localhost:5119/?tab=stock-info`。
- 打包复测里，点击 `浦发银行 sh600000` 后已能进入激活的个股工作区，页面出现 `浦发银行（sh600000）`，且 DOM 检查确认不再出现 `暂无 K 线数据`、`暂无分时数据`、`ERR_CONNECTION_REFUSED` 可见失败态。
- 同一交互窗口前后，命令行两次探测 `http://localhost:5119/api/health` 均返回 `{"status":"ok"}`。

### 2026-03-22 股票运行态重试验证

- 命令：`Set-Location .\frontend; npx vitest run src/modules/stocks/StockInfoTab.spec.js`
- 结果：通过，股票页定向 51/51 全绿，包含两条新增的瞬时失败重试回归测试。
- 命令：`C:\Users\kong\AiAgent\start-all.bat`
- 结果：通过，已成功重建并重启打包后的前端/后端/桌面程序。
- Browser MCP 验证：重新打开 `http://localhost:5119/?tab=stock-info`，选择 `浦发银行 sh600000`，确认激活工作区与 `专业图表终端` 已正常渲染，不再出现 `暂无 K 线数据` / `暂无分时数据`，且交互期间打包后端健康检查持续为 `ok`。