# Minute Built-in Render Fix / 分时内建渲染修复

## English

### Scope
- Remove the custom SVG and HTML minute overlay layer from `StockCharts.vue`.
- Restore the minute chart to use the built-in `klinecharts` area/line rendering.
- Keep the minute-only zoom and scroll lock introduced in the prior fix.

### Changes
- `frontend/src/modules/stocks/StockCharts.vue`
  - Removed the custom minute SVG overlay and right-axis overlay markup.
  - Removed the related overlay state and fullscreen resize residue.
  - Restored the existing chart interaction methods and lifecycle wiring after cleanup.
- `frontend/src/modules/stocks/charting/useStockChartAdapter.js`
  - Restored built-in minute area line/fill colors based on minute trend.
  - Kept minute x-axis labels visible through the chart style configuration.
- `frontend/src/modules/stocks/StockCharts.spec.js`
  - Replaced the SVG-specific assertions with built-in-rendering assertions.
  - Verified that the legacy overlay DOM nodes are absent.

### Validation
1. Unit test
   - Command: `npm --prefix .\frontend run test:unit -- StockCharts.spec.js`
   - Result: Passed, 23/23 tests.
2. Frontend build
   - Command: `npm --prefix .\frontend run build`
   - Result: Passed.
3. Runtime launch
   - Command: `.\start-all.bat`
   - Result: Completed successfully, packaged desktop and backend startup reported healthy.
4. Local port check
   - Command: `netstat -ano | Select-String ':5119'`
   - Result: Port 5119 is listening after startup.

### Browser validation note
- The stock info page was opened at `http://localhost:5119/?tab=stock-info`.
- Full DOM-level browser validation was blocked because page-content chat browser tools are not enabled in the current VS Code session.
- Console access from the integrated browser path was not reliable enough to prove the minute chart interaction state, so the implementation was validated by targeted unit coverage plus successful build/startup checks.

## 中文

### 范围
- 移除 `StockCharts.vue` 里的分时自绘 SVG 和 HTML 叠加层。
- 分时价格线与面积改回由 `klinecharts` 内建渲染。
- 保留上一轮已经加上的“仅分时禁用缩放和滚动”。

### 改动
- `frontend/src/modules/stocks/StockCharts.vue`
  - 删除分时自绘 SVG 图层和右侧价格轴自绘图层模板。
  - 删除相关 overlay 状态和全屏切换后的残留同步逻辑。
  - 在清理过程中把组件原有的交互函数和生命周期挂载逻辑一并恢复到稳定状态。
- `frontend/src/modules/stocks/charting/useStockChartAdapter.js`
  - 恢复分时图使用组件内建 area/line 的颜色与填充。
  - 保持分时 x 轴文字通过图表样式正常显示。
- `frontend/src/modules/stocks/StockCharts.spec.js`
  - 把原先针对自绘 SVG 的断言改成针对内建渲染的断言。
  - 验证旧 overlay DOM 节点已经不存在。

### 验证
1. 单元测试
   - 命令：`npm --prefix .\frontend run test:unit -- StockCharts.spec.js`
   - 结果：通过，23/23。
2. 前端构建
   - 命令：`npm --prefix .\frontend run build`
   - 结果：通过。
3. 本地启动
   - 命令：`.\start-all.bat`
   - 结果：成功，打包桌面端和后端启动健康检查通过。
4. 端口确认
   - 命令：`netstat -ano | Select-String ':5119'`
   - 结果：启动后 5119 端口处于监听状态。

### 浏览器验收说明
- 已打开 `http://localhost:5119/?tab=stock-info` 页面。
- 由于当前 VS Code 会话未开启可读取页面内容的 chat browser 工具，无法做 DOM 级别的自动化页面验收。
- 集成浏览器控制台输出不足以可靠证明分时图交互状态，因此本轮以定向单测覆盖、成功构建和成功启动作为主要验证依据。