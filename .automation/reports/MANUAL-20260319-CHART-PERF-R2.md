# Stock detail slimming benchmark follow-up / 股票详情瘦身基准补充报告

## Development (EN)
- Slimmed `GET /api/stocks/detail` into a non-chart summary endpoint:
  - it now returns quote + messages + optional fundamental snapshot only
  - it no longer requests K-line or minute-line data
  - persistence on the detail path now keeps only basic quote/profile/message data
- Narrowed `GET /api/stocks/detail/cache` one step further for compatibility:
  - default cache responses are now summary-only as well
  - legacy DB-backed K-line/minute replay is preserved only behind explicit `includeLegacyCharts=true`
- Updated `StockSyncService.SaveDetailAsync(...)` so the detail flow no longer writes minute/K-line rows to the database, while leaving the existing code and tables in place for future reuse.
- Added/updated tests to lock the new persistence contract:
  - `StockSyncServiceTests`
  - `HighFrequencyQuoteServiceTests`
- Captured current benchmark timings on the backend-served frontend with Browser MCP:
  - first-open parallel fetch total: 6708ms
  - `detail-summary`: 2090ms, 5268 bytes
  - `chart-day`: 6705ms, 29328 bytes
  - `chart-month`: 7ms, 29365 bytes
  - `chart-year`: 4ms, 23959 bytes

## 开发内容（ZH）
- 将 `GET /api/stocks/detail` 收口成非图表专用的摘要接口：
  - 现在只返回 quote + messages + 可选的 fundamental snapshot
  - 不再请求 K 线 / 分时数据
  - 详情路径的持久化也只保留基础行情、公司资料和消息
- 继续把 `GET /api/stocks/detail/cache` 往兼容层收口：
  - 默认缓存响应现在也只回放基础摘要
  - 旧的数据库 K 线 / 分时回放仅保留为显式 `includeLegacyCharts=true` 兼容开关
- 调整 `StockSyncService.SaveDetailAsync(...)`，让详情流不再往数据库写入分时/K线行，但保留现有代码和表结构，方便未来回用。
- 补齐/更新回归测试，锁定新的持久化契约：
  - `StockSyncServiceTests`
  - `HighFrequencyQuoteServiceTests`
- 通过 Browser MCP 在后端托管前端上补了一轮当前基准：
  - 首开并行请求总耗时：6708ms
  - `detail-summary`：2090ms，5268 字节
  - `chart-day`：6705ms，29328 字节
  - `chart-month`：7ms，29365 字节
  - `chart-year`：4ms，23959 字节

## Validation (EN)
- Backend test command:
  - `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "StockSyncServiceTests|HighFrequencyQuoteServiceTests|StockDetailCacheQueriesTests"`
- Backend test result:
  - total 9, failed 0, passed 9
- Frontend test command:
  - `npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
- Frontend test result:
  - total 44, failed 0, passed 44
- Browser MCP benchmark command:
  - ran a browser-side async fetch benchmark against `http://localhost:5119/`
- Browser MCP benchmark result:
  - `firstOpenMs`: 6708
  - `detail-summary`: 2090ms / 5268 bytes
  - `chart-day`: 6705ms / 29328 bytes
  - `chart-month`: 7ms / 29365 bytes
  - `chart-year`: 4ms / 23959 bytes
- Browser MCP smoke result after cache narrowing:
  - backend-served page at `http://localhost:5119/` opened successfully
  - stock terminal rendered normally with summary cache + live chart flow
  - browser console errors: 0

## 验证（ZH）
- 后端测试命令：
  - `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "StockSyncServiceTests|HighFrequencyQuoteServiceTests|StockDetailCacheQueriesTests"`
- 后端测试结果：
  - 总计 9，失败 0，通过 9
- 前端测试命令：
  - `npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
- 前端测试结果：
  - 总计 44，失败 0，通过 44
- Browser MCP 基准命令：
  - 在 `http://localhost:5119/` 上执行浏览器侧异步 fetch 基准
- Browser MCP 基准结果：
  - `firstOpenMs`：6708
  - `detail-summary`：2090ms / 5268 字节
  - `chart-day`：6705ms / 29328 字节
  - `chart-month`：7ms / 29365 字节
  - `chart-year`：4ms / 23959 字节
- Browser MCP 补充回归结果：
  - `http://localhost:5119/` 页面可正常打开
  - 股票终端在“摘要缓存 + 实时图表”模式下正常渲染
  - 浏览器 console 错误数：0

## Issues / 问题
- The follow-up benchmark had to be run on the backend-served frontend page instead of a blank browser tab; once the page was opened properly, the fetch benchmark ran cleanly.
- This benchmark reflects the current slimmed state. The old chart-heavy `/api/stocks/detail` behavior was intentionally removed rather than preserved for regression timing.
- The extra review pass found one remaining ambiguity: `/api/stocks/detail/cache` was still replaying legacy chart rows by default. That is now narrowed to summary-only by default so the cache endpoint no longer silently resumes chart responsibilities.
- 补充基准一开始跑在空白页上，无法直接取到同源页面上下文；打开后端托管前端后，基准才稳定执行。
- 本次基准反映的是当前瘦身后的状态；旧版图表型 `/api/stocks/detail` 已被主动收口，不再作为回归耗时样本保留。
- 这次复审还发现一个剩余歧义：`/api/stocks/detail/cache` 之前默认仍会回放旧图表行。现已补成“默认只回放摘要”，避免缓存接口在默认路径上重新承担图表职责。