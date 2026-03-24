# MANUAL-20260324 Minute Chart Non-Scalable Fix

## English
- Scope: fix minute-chart interaction mismatch where mouse zoom/scroll changed minute volume and minute TD9 markers, but the custom minute price line did not scale together.
- Root cause: the minute price line is rendered by the custom SVG layer in `frontend/src/modules/stocks/StockCharts.vue`, while minute volume and marker layers are rendered by `klinecharts`. That left minute panes responding to built-in zoom/scroll while the SVG layer stayed visually fixed.
- Change:
  - In `frontend/src/modules/stocks/charting/useStockChartAdapter.js`, disable `setZoomEnabled(false)` and `setScrollEnabled(false)` only for the minute chart instance.
  - Keep day/month/year K charts unchanged.
  - In `frontend/src/modules/stocks/StockCharts.spec.js`, add a regression test asserting minute charts disable zoom/scroll while K-line charts do not.
- Verification:
  - Unit test command: `npm --prefix .\frontend run test:unit -- StockCharts.spec.js`
  - Result: passed, `23/23` tests.
  - Build command: `npm --prefix .\frontend run build`
  - Result: success.
  - Browser validation:
    - Opened `http://localhost:5119/?tab=stock-info`
    - Queried `sh603099`
    - Switched to minute chart via script click
    - Confirmed minute-only controls rendered on the live page: `分时`, `量能`, `昨收基线`, `VWAP`, `分时九转`
- Known unrelated runtime noise:
  - Existing console/network errors for `/api/stocks/plans*` were still present during browser validation and were not touched by this fix.

## 中文
- 范围：修复分时图交互错位问题。此前鼠标缩放/拖动会影响分时成交量和分时九转 marker，但自定义分时主线不会同步缩放，导致显示异常。
- 根因：分时主线由 `frontend/src/modules/stocks/StockCharts.vue` 里的自定义 SVG 层渲染；分时成交量和 marker 则由 `klinecharts` 内部图层渲染。因此内建缩放只作用在后者，不作用在自定义主线。
- 修改：
  - 在 `frontend/src/modules/stocks/charting/useStockChartAdapter.js` 中，仅对分时图实例关闭 `setZoomEnabled(false)` 和 `setScrollEnabled(false)`。
  - 日K、月K、年K 保持原有交互不变。
  - 在 `frontend/src/modules/stocks/StockCharts.spec.js` 中新增回归测试，锁定“仅分时图禁用缩放/滚动、K线图不受影响”的行为。
- 验证：
  - 单测命令：`npm --prefix .\frontend run test:unit -- StockCharts.spec.js`
  - 结果：通过，`23/23`。
  - 构建命令：`npm --prefix .\frontend run build`
  - 结果：成功。
  - 浏览器验证：
    - 打开 `http://localhost:5119/?tab=stock-info`
    - 查询 `sh603099`
    - 通过脚本切换到分时图
    - 确认 live 页面已渲染分时专用控制项：`分时`、`量能`、`昨收基线`、`VWAP`、`分时九转`
- 已知无关噪音：
  - 浏览器验证时仍存在 `/api/stocks/plans*` 相关 console/network 报错，本次修复未涉及该部分。