# Minute Zoom And TD9 Tuning / 分时缩放与九转位置微调

## English

### Scope
- Re-enable minute chart zoom and scroll interactions.
- Move minute TD Sequential markers closer to the minute price line.
- When a minute TD sequence reaches 9, hide the earlier 6/7/8 markers from that completed run.
- Keep day-view TD Sequential marker placement unchanged.

### Changes
- `frontend/src/modules/stocks/charting/useStockChartAdapter.js`
  - Removed the minute-only `setZoomEnabled(false)` and `setScrollEnabled(false)` calls.
- `frontend/src/modules/stocks/charting/chartStrategyRegistry.js`
  - Added separate TD marker price offsets for day and minute views.
  - Kept day TD marker spacing unchanged.
  - Tightened minute TD marker offsets again so minute buy/sell labels render closer to the intraday line.
  - Collapsed completed minute TD runs so a finished 9 only shows the 9 marker instead of stacking 6/7/8/9 together.
- `frontend/src/modules/stocks/StockCharts.spec.js`
  - Updated the interaction test to verify minute zoom/scroll are no longer forcibly disabled.
  - Added assertions that minute TD sell markers now use the tighter positions.
  - Added assertions that completed minute TD runs only keep the 9 marker.

### Validation
1. Unit test
   - Command: `npm --prefix .\frontend run test:unit -- StockCharts.spec.js`
   - Result: Passed, 23/23 tests.
2. Frontend build
   - Command: `npm --prefix .\frontend run build`
   - Result: Passed.

### Notes
- The marker-position tuning is minute-only. Day K TD markers still use the previous spacing so existing day-view behavior is preserved.
- The run-collapsing rule is also minute-only. Day K TD still keeps the existing 6-9 progression.
- Browser-side visual verification was not automated in this round because page-content browser chat tools are not enabled in the current VS Code session.

## 中文

### 范围
- 恢复分时图的缩放与滚动交互。
- 将分时九转 marker 下压到更贴近分时价格线的位置。
- 当分时九转某一轮已经走到 9 时，隐藏这一轮更早的 6/7/8。
- 保持日线 TD 九转的位置不变。

### 改动
- `frontend/src/modules/stocks/charting/useStockChartAdapter.js`
  - 删除分时图专用的 `setZoomEnabled(false)` 与 `setScrollEnabled(false)`。
- `frontend/src/modules/stocks/charting/chartStrategyRegistry.js`
  - 为 TD 九转增加日线与分时两套独立的 marker 价格偏移。
  - 日线继续沿用原来的偏移，不改变原有表现。
  - 分时九转进一步改用更紧的偏移，使买卖 marker 更贴近分时线。
  - 分时已完成到 9 的同一轮序列只保留 9，不再叠着显示 6/7/8/9。
- `frontend/src/modules/stocks/StockCharts.spec.js`
  - 更新交互测试，验证分时图不再被强制禁用缩放/滚动。
  - 增加分钟九转卖点 marker 新位置的断言。
  - 增加“完成到 9 后仅保留 9” 的断言。

### 验证
1. 单元测试
   - 命令：`npm --prefix .\frontend run test:unit -- StockCharts.spec.js`
   - 结果：通过，23/23。
2. 前端构建
   - 命令：`npm --prefix .\frontend run build`
   - 结果：通过。

### 说明
- 这次九转位置调整只作用于分时图，日K 的 TD 九转位置保持不变，避免影响原有日线判断习惯。
- 这次“9 出现后折叠掉 6/7/8”也只作用于分时图，不改日K 现有 6-9 展示方式。
- 由于当前 VS Code 会话没有开启可读取页面内容的 browser chat 工具，本轮没有做自动化页面级视觉验收。