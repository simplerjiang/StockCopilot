# MANUAL-20260324-MINUTE-TD9-PLAN

## EN

### Request

- Add intraday TD9 to the existing minute chart.
- When minute TD9 is enabled, the minute chart should refresh every second.

### Current Baseline

- The minute chart is rendered through the shared charting stack:
  - `StockInfoTab.vue` fetches `/api/stocks/chart` and passes `minuteLines` into `StockCharts.vue`.
  - `StockCharts.vue` delegates rendering to `useStockChartAdapter.js` and `chartStrategyRegistry.js`.
- TD Sequential already exists, but only for day K:
  - `chartStrategyRegistry.js` defines `tdSequential`.
  - `supportedViews` is currently `['day']`, so minute view cannot enable it.
- Chart refresh is not coupled to minute-feature visibility:
  - it refreshes on initial load, symbol search, interval switch, and global refresh timers.
  - there is no timer that activates only when a specific minute strategy is enabled.

### Proposed Implementation Shape

1. Add a dedicated minute-view strategy instead of broadening the existing day-only one.
2. Reuse the existing TD counting logic on normalized minute records.
3. Keep the 1-second refresh narrowly scoped:
   - only when the active chart view is `minute`,
   - only when the minute TD9 feature is enabled,
   - only for the active stock workspace.
4. Reuse the existing `refreshChartData(...)` concurrency guard (`quoteRequestToken` + `AbortController`) instead of introducing a second fetch path.

### Concrete Slices

#### Slice 1: Minute TD9 Overlay

- Add `minuteTdSequential` in `frontend/src/modules/stocks/charting/chartStrategyRegistry.js`.
- Keep it separate from the day strategy so help text, default visibility, and future thresholds can diverge cleanly.
- Use the same marker family as current TD markers, but tune label copy for intraday context if needed.

#### Slice 2: Visibility And View State Propagation

- `StockCharts.vue` currently owns `activeView` and `featureVisibilityByView` internally.
- To drive 1-second refresh from `StockInfoTab.vue`, expose minimal parent-visible signals:
  - chart view changes,
  - minute strategy visibility changes.
- Prefer emitting events rather than lifting the entire chart state unless later features require shared ownership.

#### Slice 3: 1-Second Refresh Gating

- Add a dedicated minute-TD9 refresh timer in `StockInfoTab.vue`.
- Start the timer only when all conditions are true:
  - current stock exists,
  - active chart view is `minute`,
  - minute TD9 is enabled,
  - no chart request is already running.
- Stop the timer immediately when:
  - switching away from minute view,
  - disabling minute TD9,
  - changing symbol/workspace,
  - unmounting the page.
- Keep existing broader refresh timers unchanged unless they conflict with this timer.

### Risks And Constraints

- A 1-second poll is expensive if applied globally; it must not run for day/month/year views.
- Minute data upstream may not update every second; the UI refresh loop should tolerate repeated identical payloads.
- The chart component currently owns feature state locally, so parent-child event wiring is the main structural change.

### Validation Plan

- Frontend unit tests:
  - minute TD9 appears in minute strategy groups,
  - enabling it produces markers on synthetic minute data,
  - 1-second timer starts and stops under the intended gating conditions,
  - timer is cleaned up on unmount.
- Closest verification commands after implementation:
  - `npm --prefix .\frontend run test:unit -- src/modules/stocks/StockCharts.spec.js src/modules/stocks/StockInfoTab.spec.js`
- If UI behavior changes as expected, add Browser MCP acceptance on the backend-served stock page.

### Result Of This Planning Round

- No code implementation started in this round.
- Scope is now formalized in automation tasks and README planning notes.

## ZH

### 用户需求

- 把“分时九转”放进现有分时图。
- 当开启分时九转时，分时图要每秒更新一次。

### 当前基线

- 分时图已经走统一图表栈：
  - `StockInfoTab.vue` 拉 `/api/stocks/chart`，把 `minuteLines` 传给 `StockCharts.vue`。
  - `StockCharts.vue` 再通过 `useStockChartAdapter.js` 和 `chartStrategyRegistry.js` 渲染。
- 仓库里已经有 TD 九转，但目前只支持日 K：
  - `chartStrategyRegistry.js` 中已有 `tdSequential`。
  - 但它的 `supportedViews` 只有 `['day']`，所以分时视图现在根本开不出来。
- 图表刷新目前也没有和“分时策略开关”挂钩：
  - 现在只会在首次查股、切周期、手动/全局自动刷新时请求图表。
  - 不存在“某个分时信号一开，就进入秒级轮询”的单独机制。

### 建议实现形态

1. 不直接把现有日 K `tdSequential` 粗暴扩展到全部视图，而是新增一个独立的 `minuteTdSequential`。
2. 计数逻辑尽量复用现有 TD 逻辑，只是在分时 records 上运行。
3. 1 秒刷新要严格限域：
  - 只有当前图表视图是 `minute`；
  - 只有“分时九转”开关已打开；
  - 只针对当前激活股票 workspace。
4. 继续复用现有 `refreshChartData(...)` 的并发保护，不再另开一套图表请求路径。

### 具体切片

#### 切片 1：分时九转信号层

- 在 `frontend/src/modules/stocks/charting/chartStrategyRegistry.js` 新增 `minuteTdSequential`。
- 和日 K `tdSequential` 分离，后续文案、默认开关、阈值策略都更容易独立演进。
- 先复用现有 marker 风格，必要时再针对分时密度微调标记文案或偏移量。

#### 切片 2：把视图/开关状态抬给父层

- `StockCharts.vue` 现在把 `activeView` 和 `featureVisibilityByView` 完全关在组件内部。
- 但 1 秒刷新控制器更适合放在 `StockInfoTab.vue`，因为真正的请求函数在那里。
- 因此建议最小改造：
  - `StockCharts.vue` 向父层发出当前 view 变化事件；
  - `StockCharts.vue` 向父层发出分时策略可见性变化事件。
- 优先用事件，不急着把整份图表状态全部 lifting 到父层。

#### 切片 3：分时九转专用 1 秒轮询

- 在 `StockInfoTab.vue` 增加独立的 minute-TD9 refresh timer。
- 仅当以下条件同时成立时启动：
  - 当前有激活股票；
  - 图表正在看分时；
  - 分时九转已开启；
  - 当前没有正在进行中的图表请求。
- 以下情况要立即停掉：
  - 切离分时；
  - 关闭分时九转；
  - 切股票或切 workspace；
  - 组件卸载。
- 现有更粗粒度的自动刷新先保持不动，避免把已有逻辑一起搅乱。

### 风险点

- 1 秒轮询如果做成全局，会直接放大后端和上游压力，所以必须严格 gated。
- 上游分时数据未必每秒都变化，前端需要接受“请求成功但 payload 相同”的情况。
- 目前真正需要动结构的点，不是 TD 公式，而是图表子组件到父组件的状态传递。

### 验证计划

- 前端单测至少覆盖：
  - 分时策略列表里出现 `minuteTdSequential`；
  - 合成分时数据能画出 TD9 markers；
  - 满足条件时 1 秒定时器启动，不满足时停止；
  - 页面卸载时定时器被清理。
- 实现后的最近验证命令：
  - `npm --prefix .\frontend run test:unit -- src/modules/stocks/StockCharts.spec.js src/modules/stocks/StockInfoTab.spec.js`
- 如果前端单测通过，再补 Browser MCP 对 backend-served 股票页的真实交互验收。

### 本轮结果

- 这轮只做规划，没有开始写功能代码。
- 需求已经正式写入 automation task 和 README 近期规划，后续可以直接按切片进入开发。