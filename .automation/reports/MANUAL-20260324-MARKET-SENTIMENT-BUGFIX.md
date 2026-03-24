# MANUAL-20260324-MARKET-SENTIMENT-BUGFIX

## EN

### Scope

- Fix the user-facing bugs on the market sentiment / sector rotation page that were logged as Bugs 11-14.
- Keep the change focused on the actual broken user experience instead of rewriting the backend ingestion path in the same round.

### Actions

- Updated `frontend/src/modules/market/MarketSentimentTab.vue`.
- Added summary fallback logic so stale or empty persisted breadth values no longer render as fake zeros when realtime breadth is already available.
- Changed the turnover-share display from fake `0.00%` to an explicit pending state when the persisted snapshot is not synced yet.
- Added a summary notice explaining when realtime breadth is being used as a fallback and when turnover-share is still pending.
- Changed realtime board merge behavior so the selected page order is preserved for non-realtime sorts such as `综合强度`, instead of being silently reordered by external `rankNo`.
- Split the displayed ranking semantics into:
  - current page order: `当前第N`
  - external reference rank: `东财#N` or `快照#N`
- Added sparse-snapshot detection and honest degraded states for sectors whose detail payload only contains a change snapshot without leaders/news/member breakdown.
- Added `快照有限` badges on sparse rows and a warning banner in the detail panel.
- Reordered the page so the board toolbar, board list, and sector detail panel appear before the secondary overview metrics and history sections.
- Added frontend regression coverage in `frontend/src/modules/market/MarketSentimentTab.spec.js` for:
  - keeping strength ordering stable when realtime rank differs,
  - falling back to realtime breadth values,
  - honest sparse-snapshot labeling.

### Test Commands And Results

- Command: `npm --prefix .\frontend run test:unit -- src/modules/market/MarketSentimentTab.spec.js`
- Result: passed, 5/5.

- Command: `npm --prefix .\frontend run build`
- Result: passed. Only the existing Vite chunk-size warning remained.

- Command: `.\start-all.bat`
- Result: passed. The packaged desktop startup flow rebuilt and launched successfully.

### Browser MCP Validation

- Runtime used: `http://localhost:5119/?tab=market-sentiment`.
- Verified after restart:
  - the summary card now shows realtime-backed breadth values instead of fake zero values,
  - `热门板块成交占比` shows `待同步` instead of a misleading `0.00%`,
  - the page shows an explicit data notice when breadth is being backfilled from realtime data,
  - board cards now separate current display order from external Eastmoney rank,
  - sparse rows show `快照有限`,
  - clicking a sparse row shows a limited-data warning instead of pretending the detail panel is complete,
  - the board and detail workflow appears before the secondary metrics sections,
  - browser console errors remained at 0.

### Outcome

- Bugs 11-14 were fixed on the user-facing page and revalidated in the live runtime.
- The persisted `/api/market/sentiment/latest` payload still appears incomplete in this environment, but the page no longer exposes that incompleteness as authoritative zero-valued metrics.

## ZH

### 范围

- 修复已经记录到 Bug 11-14 的 `情绪轮动` 页面用户可见问题。
- 本轮只修真正影响用户判断和操作的页面层问题，不把后端持久化链路重写混进同一次改动里。

### 本轮动作

- 修改了 `frontend/src/modules/market/MarketSentimentTab.vue`。
- 为顶部 `情绪总览` 增加显示层回退逻辑：当持久化 summary 的涨跌家数/涨跌停明显缺失时，页面自动使用 realtime breadth 补足，而不是继续显示 0 值假数据。
- 把 `热门板块成交占比` 从误导性的 `0.00%` 改成明确的 `待同步` 状态。
- 新增 summary 提示文案，告诉用户当前哪些指标是“实时补足”，哪些仍在等待同步。
- 调整实时板块榜 merge 逻辑：在 `综合强度` 这类非实时排序模式下，不再被外部 `rankNo` 偷偷重排列表顺序。
- 把排名口径拆开显示：
  - 当前页面顺位：`当前第N`
  - 外部参考顺位：`东财#N` 或 `快照#N`
- 增加稀疏快照识别逻辑，对只有涨幅快照、没有龙头/新闻/成员拆解的板块做诚实降级展示。
- 为稀疏板块卡片增加 `快照有限` 标记，并在详情区增加有限数据警示条。
- 重排页面结构，把榜单工具条、板块列表和详情区上移到主工作流前面，把实时总览和历史趋势等次级信息后置。
- 在 `frontend/src/modules/market/MarketSentimentTab.spec.js` 新增回归覆盖，锁定：
  - 实时 rank 与强度排序冲突时不得打乱当前排序，
  - summary 可回退到 realtime breadth，
  - 稀疏快照要诚实标记。

### 测试命令与结果

- 命令：`npm --prefix .\frontend run test:unit -- src/modules/market/MarketSentimentTab.spec.js`
- 结果：通过，5/5。

- 命令：`npm --prefix .\frontend run build`
- 结果：通过，仅保留已有 Vite chunk-size warning。

- 命令：`.\start-all.bat`
- 结果：通过，打包桌面启动链重新构建并成功拉起。

### Browser MCP 验证

- 使用运行态：`http://localhost:5119/?tab=market-sentiment`。
- 重启后已确认：
  - `情绪总览` 不再显示与 realtime 冲突的 0 值假数据，
  - `热门板块成交占比` 改为 `待同步`，不再伪装成真实 0 值，
  - 页面会明确提示当前广度数据是实时补足，
  - 板块卡片已把“当前顺位”和“东财参考排名”拆开显示，
  - 稀疏板块卡片已显示 `快照有限`，
  - 点击稀疏板块后，详情区会显示有限数据警示，而不是继续伪装成完整详情，
  - 首屏先展示榜单和详情主流程，次级指标区已被后移，
  - 浏览器 console error 为 0。

### 结果

- Bug 11-14 的页面层问题已经完成修复，并在真实运行态中复测通过。
- 当前环境下 `/api/market/sentiment/latest` 的持久化载荷本身仍有不完整现象，但页面已经不再把这些缺口直接暴露成“看起来像正式指标”的 0 值假数据。
