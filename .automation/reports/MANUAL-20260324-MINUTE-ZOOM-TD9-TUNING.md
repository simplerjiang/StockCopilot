# Minute Zoom And TD9 Tuning / 分时缩放与九转位置微调

## English

### Scope
- Re-enable minute chart zoom and scroll interactions.
- Move minute TD Sequential markers closer to the minute price line.
- Keep day-view TD Sequential marker placement unchanged.

### Changes
- `frontend/src/modules/stocks/charting/useStockChartAdapter.js`
  - Removed the minute-only `setZoomEnabled(false)` and `setScrollEnabled(false)` calls.
- `frontend/src/modules/stocks/charting/chartStrategyRegistry.js`
  - Added separate TD marker price offsets for day and minute views.
  - Kept day TD marker spacing unchanged.
  - Tightened minute TD marker offsets so minute buy/sell labels render much closer to the intraday line.
- `frontend/src/modules/stocks/StockCharts.spec.js`
  - Updated the interaction test to verify minute zoom/scroll are no longer forcibly disabled.
  - Added assertions that minute TD sell markers now use the tighter positions.

### Validation
1. Unit test
   - Command: `npm --prefix .\frontend run test:unit -- StockCharts.spec.js`
   - Result: Passed, 23/23 tests.
2. Frontend build
   - Command: `npm --prefix .\frontend run build`
   - Result: Passed.

### Notes
- The marker-position tuning is minute-only. Day K TD markers still use the previous spacing so existing day-view behavior is preserved.
- Browser-side visual verification was not automated in this round because page-content browser chat tools are not enabled in the current VS Code session.

## 中文

### 范围
- 恢复分时图的缩放与滚动交互。
- 将分时九转 marker 下压到更贴近分时价格线的位置。
- 保持日线 TD 九转的位置不变。

### 改动
- `frontend/src/modules/stocks/charting/useStockChartAdapter.js`
  - 删除分时图专用的 `setZoomEnabled(false)` 与 `setScrollEnabled(false)`。
- `frontend/src/modules/stocks/charting/chartStrategyRegistry.js`
  - 为 TD 九转增加日线与分时两套独立的 marker 价格偏移。
  - 日线继续沿用原来的偏移，不改变原有表现。
  - 分时九转改用更紧的偏移，使买卖 6-9 更贴近分时线。
- `frontend/src/modules/stocks/StockCharts.spec.js`
  - 更新交互测试，验证分时图不再被强制禁用缩放/滚动。
  - 增加分钟九转卖点 marker 新位置的断言。

### 验证
1. 单元测试
   - 命令：`npm --prefix .\frontend run test:unit -- StockCharts.spec.js`
   - 结果：通过，23/23。
2. 前端构建
   - 命令：`npm --prefix .\frontend run build`
   - 结果：通过。

### 说明
- 这次九转位置调整只作用于分时图，日K 的 TD 九转位置保持不变，避免影响原有日线判断习惯。
- 由于当前 VS Code 会话没有开启可读取页面内容的 browser chat 工具，本轮没有做自动化页面级视觉验收。