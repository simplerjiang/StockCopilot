# Extension Interface R4 Development Report / 扩展接口 R4 开发报告

## Development (EN)
- Scope: continue sinking the new realtime market overview into another trading surface after the market page rollout.
- Added a new `市场实时上下文` card to `frontend/src/modules/stocks/StockInfoTab.vue`.
- The card reuses `/api/market/realtime/overview` and requests the current stock symbol together with the default market indexes, so the user can compare the active symbol against the broader tape in the same sidebar.
- Displayed fields include:
  - current stock snapshot
  - Shanghai / Shenzhen / ChiNext benchmark snapshots
  - main-capital net inflow
  - northbound net inflow
  - advancers / decliners and limit-up / limit-down counts
- The panel has its own refresh action, localStorage-backed visibility flag `stock_realtime_context_enabled`, and failure isolation so the stock detail page remains usable even if the realtime overview request fails.

## 开发内容（ZH）
- 范围：在市场页落地之后，继续把新的实时市场总览下沉到另一个高频看盘入口。
- 在 `frontend/src/modules/stocks/StockInfoTab.vue` 新增了 `市场实时上下文` 卡片。
- 该卡片复用 `/api/market/realtime/overview`，请求时把当前股票代码和默认市场指数一起带上，让用户能在同一侧栏里直接对照“当前标的 vs 大盘”。
- 展示内容包括：
  - 当前股票快照
  - 上证 / 深成 / 创业板基准快照
  - 主力净流入
  - 北向净流入
  - 涨跌家数与涨停 / 跌停统计
- 这块面板带有独立刷新、本地显隐开关 `stock_realtime_context_enabled`，并且做了失败隔离，不会拖垮原有股票详情页。

## Validation (EN)
- Frontend unit test command:
  - `npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
- Frontend unit test result:
  - total 42, failed 0, passed 42
- Browser MCP runtime validation:
  - rebuilt the local frontend with `start-all.bat`
  - opened `http://localhost:5119/`
  - switched to the `股票信息` tab
  - queried `sz000021`
  - confirmed runtime rendering of `市场实时上下文`, `当前标的`, `上证指数`, and the realtime flow pill

## 验证（ZH）
- 前端单测命令：
  - `npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
- 前端单测结果：
  - 总计 42，失败 0，通过 42
- Browser MCP 运行时验收：
  - 使用 `start-all.bat` 重建本地前端
  - 打开 `http://localhost:5119/`
  - 切换到 `股票信息` 页签
  - 查询 `sz000021`
  - 实测确认 `市场实时上下文`、`当前标的`、`上证指数` 和实时资金流 pill 已渲染

## Issues / 问题
- The first browser check was still serving the previous frontend bundle. Re-running `start-all.bat` rebuilt the assets, and the new stock-terminal card then appeared correctly.
- 首次浏览器验收仍然命中了旧前端包。重新执行 `start-all.bat` 后完成了前端重建，新卡片随即正常出现。