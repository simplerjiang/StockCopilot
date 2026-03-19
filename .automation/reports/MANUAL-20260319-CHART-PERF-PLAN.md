# Stock Chart Refresh Performance Plan / 股票图表刷新性能规划

## Plan (EN)
- Objective: close the responsiveness gap between the stock terminal and `stock-and-fund-chrome-master` for minute/day/month/year chart refreshes.
- Diagnosis: the slow path is not primarily the Eastmoney or Tencent market-data endpoints. The main issue is that the frontend chart refresh path was bound to `/api/stocks/detail`, which also waits on messages, optional fundamentals, market-context reads, and possible persistence work.
- Scope decision:
  - add a lightweight backend chart contract that returns only quote + K-line + minute-line data
  - route initial chart readiness to the lightweight chart path while keeping cache replay and optional extras independent
  - make `日K图 / 月K图 / 年K图` switching request chart data only

## 计划（ZH）
- 目标：把股票终端的分时图、日K图、月K图、年K图刷新链路收窄到接近 `stock-and-fund-chrome-master` 的响应级别。
- 诊断结论：慢点不主要在东方财富或腾讯接口本身，而在于前端把图表刷新绑定到了 `/api/stocks/detail`，导致图表要等待盘中消息、可选基本面、市场上下文以及潜在持久化等非关键路径。
- 范围决策：
  - 新增只返回 quote + K线 + 分时 的轻量后端图表契约
  - 首屏图表 readiness 改走轻量图表路径，同时保留缓存回显与异步补齐慢数据
  - `日K图 / 月K图 / 年K图` 切换时只请求图表数据

## Validation Plan (EN)
- Unit tests first on `StockInfoTab.spec.js`
- Browser MCP second on the backend-served frontend at `http://localhost:5119/`
- Acceptance evidence:
  - chart switches remain interactive
  - `月K图/年K图` switching no longer triggers heavyweight detail-related requests
  - frontend console stays free of runtime errors

## 验证计划（ZH）
- 先跑 `StockInfoTab.spec.js` 定向单测
- 再对后端托管前端 `http://localhost:5119/` 做 Browser MCP 验收
- 验收证据：
  - 图表切换保持可交互
  - `月K图/年K图` 切换不再触发重量级详情请求
  - 前端 console 无运行时错误