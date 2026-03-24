# MANUAL-20260324-MINUTE-TD9-DEV

## EN

### Scope

- Implement minute TD9 on the existing intraday chart.
- When minute TD9 is enabled, refresh the minute chart every second.

### Actions

- Added `minuteTdSequential` to `frontend/src/modules/stocks/charting/chartStrategyRegistry.js`.
- Reused the existing TD setup marker builder on minute records instead of creating a second counting algorithm.
- Updated `frontend/src/modules/stocks/StockCharts.vue` so the component now emits:
  - `view-change`
  - `strategy-visibility-change`
- Updated `frontend/src/modules/stocks/StockInfoTab.vue` to track:
  - current chart view via `chartActiveView`
  - minute TD9 toggle state via `minuteTdSequentialEnabled`
  - dedicated second-level polling via `minuteTdSequentialRefreshTimer`
- Tightened chart request composition so minute data is always requested whenever the active chart view is minute, even when the shared interval model remains `day`.
- Gated the 1-second refresh strictly to the intended conditions:
  - active chart view is `minute`
  - `minuteTdSequential` is enabled
  - there is an active stock symbol
- Kept the existing `refreshChartData(...)` request path, `quoteRequestToken`, and `AbortController` concurrency protection instead of introducing a parallel fetch flow.

### Tests And Validation

- Command: `npm --prefix .\frontend run test:unit -- src/modules/stocks/StockCharts.spec.js`
- Result: passed, 22/22.
- Coverage added: minute TD sequential markers render in minute view and `strategy-visibility-change` is emitted correctly.

- Command: `npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
- Result: passed, 60/60.
- Coverage added: chart refresh runs every second only after minute TD9 is enabled in minute view, and does not run when only the toggle is on while the chart is still on day view.

- Browser MCP validation:
  - opened `http://localhost:5119/?tab=stock-info`
  - queried `sh603099`
  - switched to minute view
  - confirmed `分时九转` is present in the real runtime UI
  - toggled it on and verified the button class becomes `active`
  - verified repeated `/api/stocks/chart?symbol=sh603099&interval=day&includeQuote=false&includeMinute=true` requests in runtime network activity

### Runtime Notes

- The final live-page interaction needed script-based clicking because the Copilot sidebar overlay intercepted normal pointer clicks on the chart toolbar.
- The only console error observed during validation was an existing `404` on `/api/stocks/detail/cache?symbol=sh603099`, which is not introduced by this feature.

### Stricter Browser MCP Acceptance

- Added an in-page fetch audit wrapper during Browser MCP validation to record only `/api/stocks/chart` calls after enabling `分时九转`.
- On `http://localhost:5119/?tab=stock-info` with `sh603099` in minute view:
  - request timestamps produced intervals of `996ms` and `1007ms`
  - all sampled refresh requests were `200 OK`
  - sampled request URL stayed on `/api/stocks/chart?symbol=sh603099&interval=day&includeQuote=false&includeMinute=true`
- Added a canvas-signature audit on the minute chart host:
  - before enabling minute TD9, combined canvas checksum was `9105487`
  - after enabling minute TD9, combined canvas checksum changed to `9195313`
  - the post-enable signature remained stable during the following 3.5 seconds of 1-second polling, which indicates the marker layer stayed rendered across refreshes instead of being wiped out
- Verified stop behavior as well:
  - after disabling `分时九转`, the button state returned to inactive
  - during the next `2.5s`, there were `0` new `/api/stocks/chart` requests
  - no new business error was introduced; the only remaining runtime error was the existing cache `404`

## ZH

### 范围

- 把分时九转正式接进现有分时图。
- 当开启分时九转时，让分时图进入每秒刷新。

### 本轮动作

- 在 `frontend/src/modules/stocks/charting/chartStrategyRegistry.js` 新增 `minuteTdSequential`。
- 没有另外发明一套分时九转公式，而是直接复用现有 TD setup marker 生成逻辑，在分时 records 上运行。
- 更新 `frontend/src/modules/stocks/StockCharts.vue`，新增两个向父层抛出的事件：
  - `view-change`
  - `strategy-visibility-change`
- 更新 `frontend/src/modules/stocks/StockInfoTab.vue`，新增并接管：
  - `chartActiveView`
  - `minuteTdSequentialEnabled`
  - `minuteTdSequentialRefreshTimer`
- 调整图表请求拼装逻辑：只要当前活动图表视图是分时，就强制请求 `includeMinute=true`，避免 1 秒刷新时只拿到日 K 数据。
- 1 秒刷新严格只在以下条件全部满足时才生效：
  - 当前图表视图是 `minute`
  - `minuteTdSequential` 已开启
  - 当前存在激活股票 symbol
- 继续复用原有 `refreshChartData(...)`、`quoteRequestToken` 和 `AbortController`，没有新开第二条图表请求链路。

### 测试与验证

- 命令：`npm --prefix .\frontend run test:unit -- src/modules/stocks/StockCharts.spec.js`
- 结果：通过，22/22。
- 新覆盖点：分时九转 marker 能在 minute view 生成，并且策略开关会正确发出 `strategy-visibility-change`。

- 命令：`npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
- 结果：通过，60/60。
- 新覆盖点：只有在“分时视图 + 分时九转开启”同时成立时，图表才会每秒刷新；如果只是开了分时九转但还停在日视图，则不会进入秒级刷新。

- Browser MCP 验证：
  - 打开 `http://localhost:5119/?tab=stock-info`
  - 查询 `sh603099`
  - 切到分时图
  - 确认真实运行态里可见 `分时九转`
  - 将其切到开启状态，并确认按钮 class 进入 `active`
  - 在网络请求里确认持续出现 `/api/stocks/chart?symbol=sh603099&interval=day&includeQuote=false&includeMinute=true`

### 运行态说明

- 最后一步浏览器实测没有直接用普通点击完成，是因为 Copilot 侧栏遮挡了图表工具条，导致指针事件被拦截，所以改用脚本点击收尾验收。
- 验证期间唯一的 console error 是既有 `/api/stocks/detail/cache?symbol=sh603099` 404，不是这次分时九转功能引入的新问题。

### 更严格的 Browser MCP 验收

- 在 Browser MCP 页面内临时加了 fetch 打点，只记录开启 `分时九转` 后的 `/api/stocks/chart` 请求。
- 在 `http://localhost:5119/?tab=stock-info`、`sh603099`、分时图场景下，采样到的请求间隔分别为 `996ms` 和 `1007ms`，说明运行态确实是按约 1 秒频率在刷新。
- 采样到的刷新请求全部是 `200 OK`，且 URL 始终保持为 `/api/stocks/chart?symbol=sh603099&interval=day&includeQuote=false&includeMinute=true`。
- 同时对分时图 canvas 做了签名采样：
  - 开启前组合 checksum 为 `9105487`
  - 开启后组合 checksum 变为 `9195313`
  - 开启后的签名在后续 `3.5s` 秒级轮询期间保持稳定，说明 marker 图层在刷新过程中持续存在，没有被轮询刷掉
- 关闭 `分时九转` 后也补做了 stop-gating 验证：
  - 按钮状态恢复为 inactive
  - 后续 `2.5s` 内新增 `/api/stocks/chart` 请求数为 `0`
  - 运行态没有新增业务报错，仍只剩既有的缓存 `404`