# K-Line Hover Enhancement Report / K线悬浮增强报告

## Plan (EN)
- Objective: make K-line bars show hover details including percentage change.
- Approach: fix hover-tip positioning to use the chart shell as the coordinate space, enrich hover content with price change plus change percent, and lock behavior with a unit test.

## 计划（ZH）
- 目标：让 K 线条形在鼠标悬浮时显示包含涨跌幅在内的详细信息。
- 做法：修正 hover-tip 的定位参考系到图表壳层内部，补充涨跌额与涨跌幅，并用单测锁定行为。

## Development (EN)
- Moved the hover tooltip into `chart-shell` so crosshair coordinates map to the same positioned container.
- Added explicit `涨跌` output for K-line hover state alongside existing OHLC, volume, MA5, MA10, and `涨跌幅`.
- Added a `StockCharts.spec.js` regression that simulates `onCrosshairChange` and asserts the hover card content.
- Synced README and `.automation/tasks.json` notes with the new hover capability.

## 开发结果（ZH）
- 将 hover 提示框移动到 `chart-shell` 内部，使 crosshair 坐标与渲染容器一致。
- 在 K 线悬浮信息中补充了明确的 `涨跌`，并保留开高低收、成交量、MA5、MA10 和 `涨跌幅`。
- 在 `StockCharts.spec.js` 中新增回归用例，直接模拟 `onCrosshairChange` 验证悬浮卡片文案。
- 已同步更新 README 与 `.automation/tasks.json` 对该能力的说明。

## Validation (EN)
- Diagnostics: no errors in `useStockChartAdapter.js`, `StockCharts.vue`, and `StockCharts.spec.js`.
- Command: `npm --prefix .\frontend run test:unit -- StockCharts.spec.js`
- Result: passed, 18/18 tests.
- Command: `npm --prefix .\frontend run build`
- Result: passed.
- Browser MCP: confirmed the backend-served page loaded the updated chart terminal DOM and visible chart canvases. The MCP environment did not deterministically trigger `klinecharts` canvas crosshair hover, so runtime hover text verification was covered by the new unit test rather than canvas automation.

## 验证（ZH）
- 诊断检查：`useStockChartAdapter.js`、`StockCharts.vue`、`StockCharts.spec.js` 均无错误。
- 命令：`npm --prefix .\frontend run test:unit -- StockCharts.spec.js`
- 结果：通过，18/18。
- 命令：`npm --prefix .\frontend run build`
- 结果：通过。
- Browser MCP：已确认后端托管页面加载到新的图表终端 DOM，且可见 chart canvas 已渲染；但当前 MCP 环境下未能稳定触发 `klinecharts` 的 canvas crosshair hover，因此运行时悬浮文案由新增单测负责锁定，而不是依赖 canvas 自动化。
