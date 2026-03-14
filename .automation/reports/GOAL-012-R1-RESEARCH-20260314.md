# GOAL-012-R1 Chart Component Investigation Report (2026-03-14)

## EN
### Purpose
Document the follow-up investigation after the Dev2 chart terminal refactor, so PM can decide the next chart-component task without re-running the same comparison work.

### Scope
- Reviewed whether `SuHangWeb/tradingview-vue` is a practical replacement for the current chart layer.
- Screened alternative Vue-friendly TradingView-style or financial-chart packages.
- Compared them against the current repo baseline: Vue 3 + Vite + `lightweight-charts` + local `minuteLines` / `kLines` data + future overlay requirements.

### Current Baseline In Repo
- The chart terminal has already been refactored into a single main viewport with tabs `分时图 / 日K图 / 月K图 / 年K图`.
- The rendering logic is already isolated behind `frontend/src/modules/stocks/charting/useStockChartAdapter.js`.
- Public data compatibility is preserved with existing `minuteLines`, `kLines`, `basePrice`, `interval`, and `aiLevels` flows.

### Investigation Result
#### 1. `SuHangWeb/tradingview-vue`
- Rejected.
- Why:
  - It is effectively an older Vue 2 / Vue CLI style project template, not a clean modern Vue 3 package.
  - It relies on embedded static TradingView assets and template-project structure.
  - It is not a good drop-in dependency for the current Vue 3 + Vite repo.
  - Integration cost is high while long-term control is weak.

#### 2. `vue-tradingview-widgets`
- Not recommended as the main stock terminal chart engine.
- Why:
  - It is a wrapper around TradingView widgets rather than a controllable local-data chart engine.
  - Good for embedding ready-made widgets like screeners, tickers, symbol overview, market overview.
  - Poor fit for this repo's core need: consume local `minuteLines` and `kLines`, control overlays, keep chart state in our own terminal workflow.

#### 3. `vue3-apexcharts`
- Viable generic chart library, but not the best main replacement target.
- Why:
  - It supports candlestick charts and annotations.
  - It is a general-purpose chart wrapper, not a specialized trading terminal engine.
  - It is more suitable for dashboards and reporting views than for a professional stock chart terminal with future indicator layering.

#### 4. `klinecharts`
- The only external candidate worth deeper prototype validation.
- Why:
  - It is purpose-built for financial K-line charts.
  - Published docs/readme emphasize indicators, drawing models, style configuration, and extensibility.
  - It is much closer to the future target shape of this repo than widget wrappers or generic chart libraries.
  - It appears to be the strongest option if PM wants a real library-switch experiment.

#### 5. Current `lightweight-charts`
- Still the most practical short-term choice.
- Why:
  - Already integrated and validated in this repo.
  - Good performance and clean financial-series support.
  - Plugin/extensibility path exists, and the new adapter layer already isolates the implementation.
  - Lowest migration risk while Dev1 is still advancing Step 4.2.
  - Current architecture now allows future overlays without needing an immediate library switch.

### Final Recommendation
- Do not adopt `SuHangWeb/tradingview-vue`.
- Do not use `vue-tradingview-widgets` as the main chart terminal replacement.
- Do not prioritize `vue3-apexcharts` for the stock terminal main chart.
- Keep `lightweight-charts + internal adapter` as the active production path.
- If PM wants a next-step experiment, only prototype `klinecharts`, and do it behind the existing `charting/**` adapter boundary.

### Decision Rationale
- The real goal is not library replacement by itself.
- The real goal is a more extensible, more professional chart terminal that can later support:
  - TD Sequential / 神奇九转
  - KDJ crossover
  - more indicator lines
  - future drawing / marker / overlay controls
- The current adapter architecture already moves the repo toward that goal with low risk.
- A forced migration to the wrong third-party wrapper would increase cost and reduce control.

### Suggested Next Tasks For PM
1. Approve staying on `lightweight-charts` and schedule feature-layer upgrades on top of the adapter.
2. Or schedule a narrow `klinecharts` prototype spike within `StockCharts.vue` only, with no parent contract changes.
3. If no library switch is approved, prioritize professional terminal features on the current base:
   - volume sub-pane
   - MA / BOLL / KDJ overlays
   - richer crosshair info panel
   - marker and signal-layer API

## ZH
### 目的
记录 Dev2 图表终端改造后的补充组件调研结论，供 PM 直接据此分派下一步任务，避免重复做同一轮选型排查。

### 范围
- 评估 `SuHangWeb/tradingview-vue` 是否适合作为当前图表层替代方案。
- 筛查若干 Vue 友好的 TradingView 风格 / 金融图表候选库。
- 对比本仓库当前基线：Vue 3 + Vite + `lightweight-charts` + 本地 `minuteLines` / `kLines` 数据 + 后续策略叠加需求。

### 仓库当前基线
- 图表终端已经完成单主图视口 + `分时图 / 日K图 / 月K图 / 年K图` 统一 Tab 改造。
- 图表渲染逻辑已经隔离到 `frontend/src/modules/stocks/charting/useStockChartAdapter.js`。
- 当前 `minuteLines`、`kLines`、`basePrice`、`interval`、`aiLevels` 等数据与交互 contract 已保持兼容。

### 调研结论
#### 1. `SuHangWeb/tradingview-vue`
- 结论：不采用。
- 原因：
  - 本质上更像旧的 Vue 2 / Vue CLI 模板项目，不是干净的现代 Vue 3 组件包。
  - 依赖内嵌静态 TradingView 资源和模板式工程结构。
  - 与当前 Vue 3 + Vite 仓库不匹配，不适合直接接入。
  - 集成成本高，长期可控性却不理想。

#### 2. `vue-tradingview-widgets`
- 结论：不适合作为主图终端内核。
- 原因：
  - 它是 TradingView 官方 widget 的 Vue 封装，不是我们可完全控制的本地数据图表引擎。
  - 更适合嵌入现成 screener、ticker、overview 这类小组件。
  - 不适合本仓库核心场景：吃本地 `minuteLines` / `kLines`，自行控制叠加线、状态流和终端交互。

#### 3. `vue3-apexcharts`
- 结论：可用，但不应作为股票终端主图优先替代。
- 原因：
  - 它支持蜡烛图和 annotation。
  - 但本质仍是通用图表库，不是面向专业交易终端的金融图表引擎。
  - 更适合报表或看板类场景，不够贴合当前主图终端后续的指标和策略叠加需求。

#### 4. `klinecharts`
- 结论：唯一值得继续做原型验证的外部候选。
- 原因：
  - 它是专门面向金融 K 线的图表库。
  - 文档和 README 明确强调 indicators、drawing models、style configuration、extensibility。
  - 与本仓库未来目标形态比 widget 封装或通用图表库更接近。
  - 如果 PM 仍希望做一次真正有意义的换库试验，优先级最高的就是它。

#### 5. 当前 `lightweight-charts`
- 结论：短期内仍是最务实的生产方案。
- 原因：
  - 已在本仓库稳定接入并完成验证。
  - 性能和金融序列支持都足够好。
  - 本身具备插件/扩展空间，而新的 adapter 层已把实现隔离出来。
  - 在 Dev1 仍推进 Step 4.2 的前提下，迁移风险最低。
  - 现有架构已经为未来 overlay 扩展铺路，不需要立刻强行换库。

### 最终建议
- 不采用 `SuHangWeb/tradingview-vue`。
- 不把 `vue-tradingview-widgets` 作为主图终端替代方案。
- 不优先采用 `vue3-apexcharts` 作为股票终端主图内核。
- 继续以 `lightweight-charts + 自建 adapter` 作为当前生产主路径。
- 如果 PM 要求下一步继续做组件试验，只建议在现有 `charting/**` 边界内做 `klinecharts` 原型，不要直接侵入父组件 contract。

### 决策依据
- 真正目标不是“换库”本身。
- 真正目标是获得一个更专业、更可扩展的图表终端，为后续支持以下能力打基础：
  - 神奇九转
  - KDJ 金叉
  - 更多指标线
  - 未来画线、标记、图层控制
- 当前 adapter 架构已经以较低风险把仓库推向这个目标。
- 如果现在强行迁移到不合适的第三方封装，只会增加成本并降低可控性。

### 给 PM 的建议下一步
1. 批准继续保留 `lightweight-charts`，下一轮直接在 adapter 之上做功能增强。
2. 或安排一个严格收口的 `klinecharts` 原型试验，只在 `StockCharts.vue` 及其 charting 适配层内完成，不改父层 contract。
3. 如果本轮不换库，建议优先补专业终端能力：
   - 成交量副图
   - MA / BOLL / KDJ 叠加
   - 更完整的十字线信息面板
   - 标记层 / 信号层 API