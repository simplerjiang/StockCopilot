# Stock Chart Refresh Performance Development Report / 股票图表刷新性能开发报告

## Development (EN)
- Added `GET /api/stocks/chart` as a lightweight backend contract that returns only quote + K-line + minute-line data for chart rendering.
- Refactored `frontend/src/modules/stocks/StockInfoTab.vue` so initial stock loading now separates:
  - cached detail replay for immediate UI hydration
  - lightweight live chart refresh via `/api/stocks/chart`
  - asynchronous extras such as messages, fundamentals, and market overview
- Narrowed interval switching further:
  - `日K图 / 月K图 / 年K图` changes now call only `/api/stocks/chart`
  - removed the remaining `/api/stocks/detail/cache` fetch from interval-only refreshes
- Added a regression test to lock the interval-switch contract so it cannot silently regress back to the heavyweight path.

## 开发内容（ZH）
- 新增 `GET /api/stocks/chart` 轻量后端接口，只返回图表渲染需要的 quote + K线 + 分时数据。
- 重构 `frontend/src/modules/stocks/StockInfoTab.vue` 的查股链路，当前已拆成：
  - 先用缓存详情做秒开回显
  - 再通过 `/api/stocks/chart` 刷新实时图表
  - 消息、基本面、市场上下文继续异步补齐
- 进一步收窄周期切换链路：
  - `日K图 / 月K图 / 年K图` 切换现在只请求 `/api/stocks/chart`
  - 已移除周期切换时残留的 `/api/stocks/detail/cache` 读取
- 新增回归测试，锁定“切周期只走轻量图表接口”的契约，避免后续悄悄回退到重链路。

## Validation (EN)
- Frontend unit test command:
  - `npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
- Frontend unit test result:
  - total 43, failed 0, passed 43
- Browser MCP runtime validation:
  - started the local stack and verified `http://localhost:5119/api/health` returned `{"status":"ok"}`
  - opened the backend-served frontend at `http://localhost:5119/`
  - entered the stock terminal and selected `浦发银行 (sh600000)`
  - switched `月K图` and `年K图`
  - network evidence showed only:
    - `/api/stocks/chart?symbol=sh600000&interval=month`
    - `/api/stocks/chart?symbol=sh600000&interval=year`
  - no new `/api/stocks/detail/cache`, `/api/stocks/messages`, or `/api/stocks/fundamental-snapshot` requests were triggered by those interval switches
  - browser console error count: 0

## 验证（ZH）
- 前端单测命令：
  - `npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
- 前端单测结果：
  - 总计 43，失败 0，通过 43
- Browser MCP 运行时验收：
  - 启动本地服务并确认 `http://localhost:5119/api/health` 返回 `{"status":"ok"}`
  - 打开后端托管前端 `http://localhost:5119/`
  - 进入股票终端并选择 `浦发银行 (sh600000)`
  - 切换 `月K图` 和 `年K图`
  - 网络证据只出现：
    - `/api/stocks/chart?symbol=sh600000&interval=month`
    - `/api/stocks/chart?symbol=sh600000&interval=year`
  - 上述周期切换没有再触发新的 `/api/stocks/detail/cache`、`/api/stocks/messages`、`/api/stocks/fundamental-snapshot`
  - 浏览器 console 错误数：0

## Issues / 问题
- During the first implementation pass, interval-only refresh still fetched `/api/stocks/detail/cache`. That behavior was correct but unnecessary, so it was removed in the same task and locked with a regression test.
- 第一版实现里，周期切换仍会额外读取一次 `/api/stocks/detail/cache`。这个行为虽然不影响正确性，但会引入不必要的缓存/聚合开销，因此已在同一任务中移除，并用回归测试锁定。