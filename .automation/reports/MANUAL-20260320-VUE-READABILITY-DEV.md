# MANUAL-20260320-VUE-READABILITY

## English

### Scope
- Split long Vue pages into smaller child components without changing data flow or user-visible behavior.

### Actions
- Extracted `frontend/src/modules/stocks/StockAgentCard.vue` from `StockAgentPanels.vue` to isolate per-agent rendering, evidence cards, metrics, tags, and raw JSON toggles.
- Extracted `frontend/src/modules/market/MarketRealtimeOverview.vue` from `MarketSentimentTab.vue` to isolate the realtime deck for indices, capital flow, and breadth buckets.
- Extracted `frontend/src/modules/stocks/StockSourceLoadProgress.vue` from `StockInfoTab.vue` to reuse the stock loading progress block in both loaded and empty terminal states.
- Updated parent pages to delegate rendering to the new child components while preserving existing state ownership and event flow.

### Verification
- Command: `npm --prefix .\frontend run test:unit -- src/modules/stocks/StockAgentPanels.spec.js src/modules/market/MarketSentimentTab.spec.js src/modules/stocks/StockInfoTab.spec.js`
- Result: passed, 57/57 tests.
- Browser MCP:
  - `http://localhost:5119/?tab=stock-info` rendered normally after the split.
  - `http://localhost:5119/?tab=market-sentiment` rendered the extracted realtime overview section, including `资金与广度` and `涨跌分布桶`.

### Issues
- Browser MCP captured an existing runtime/backend issue on `GET /api/market/sectors?boardType=concept&page=1&pageSize=12&sort=strength` during market page validation.
- The page still showed the extracted realtime overview correctly, so this is recorded as a pre-existing service-state issue rather than a refactor regression.

### 2026-03-24 Follow-up
- Continued the same readability track on `frontend/src/modules/stocks/StockInfoTab.vue`, which had grown to 4915 lines.
- Extracted three new stock-only helper modules:
  - `frontend/src/modules/stocks/stockInfoTabWorkspace.js`
  - `frontend/src/modules/stocks/stockInfoTabCopilot.js`
  - `frontend/src/modules/stocks/stockInfoTabPlanHelpers.js`
- Moved workspace factory state, stock-load stage constants, Copilot tool/result helpers, and trading-plan gating rules out of the main SFC while keeping state ownership and request orchestration inside `StockInfoTab.vue`.
- Result: `StockInfoTab.vue` dropped from 4915 lines to 4558 lines without changing user-visible behavior.
- Verification:
  - Command: `npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
  - Result: passed, 59/59 tests.

### 2026-03-24 Second Follow-up
- Extracted the trading-plan template area from `frontend/src/modules/stocks/StockInfoTab.vue` into two child components:
  - `frontend/src/modules/stocks/StockTradingPlanSection.vue`
  - `frontend/src/modules/stocks/StockTradingPlanModal.vue`
- Kept plan fetching, save/delete/resume actions, and workspace ownership in the parent component; the new child components only render the current-plan panel and the modal form, then emit user actions back to `StockInfoTab.vue`.
- Result: `StockInfoTab.vue` dropped again from 4558 lines to 4428 lines.
- Verification:
  - Command: `npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
  - Result: passed, 59/59 tests.

### 2026-03-24 Third Follow-up
- Continued the same split on the remaining high-noise regions of `frontend/src/modules/stocks/StockInfoTab.vue`.
- Added one shared formatting/helper module and four rendering-only child components:
  - `frontend/src/modules/stocks/stockInfoTabFormatting.js`
  - `frontend/src/modules/stocks/StockSearchToolbar.vue`
  - `frontend/src/modules/stocks/StockTopMarketOverview.vue`
  - `frontend/src/modules/stocks/StockMarketNewsPanel.vue`
  - `frontend/src/modules/stocks/StockNewsImpactPanel.vue`
- Rewired `StockInfoTab.vue` to delegate the search toolbar, top realtime market belt, market-news reader, and sidebar news-impact panel to those files while keeping fetch orchestration, caching, timers, and workspace ownership in the parent.
- Verification:
  - Command: `npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
  - Result: passed, 59/59 tests.

### 2026-03-24 Fourth Follow-up
- Continued the split by extracting the remaining summary-heavy template blocks from `frontend/src/modules/stocks/StockInfoTab.vue`:
  - `frontend/src/modules/stocks/StockTerminalSummary.vue`
  - `frontend/src/modules/stocks/StockTradingPlanBoard.vue`
- Replaced the inline terminal summary and root trading-plan board with the new child components, then removed the stale search/market/news/summary style blocks that had already moved out of the parent.
- Result: `StockInfoTab.vue` dropped further to 3484 lines.
- Verification:
  - Command: `npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
  - Result: passed, 59/59 tests.
  - Command: `Invoke-WebRequest http://localhost:5119/?tab=stock-info`
  - Result: returned HTTP 200 after launching the local app.
  - Browser check: opened `http://localhost:5119/?tab=stock-info`; console error count was 0.
  - Limitation: this VS Code session does not expose page-content browser tools, so the browser pass was limited to reachability and console regression checks rather than DOM-level click assertions.

### 2026-03-24 Style-Constraint Cleanup
- Performed a systematic pass on the newly extracted stock child components to close the remaining layout-coupling gaps left behind by the parent split.
- Restored or localized the most failure-prone constraints in:
  - `frontend/src/modules/stocks/StockSearchToolbar.vue`
  - `frontend/src/modules/stocks/StockTopMarketOverview.vue`
  - `frontend/src/modules/stocks/StockNewsImpactPanel.vue`
  - `frontend/src/modules/stocks/StockTradingPlanModal.vue`
- Scope of the cleanup:
  - sticky toolbar shell and history ribbon scroll limits
  - search dropdown max-height, internal scroll, and context-menu positioning
  - top market belt button/layout and mobile wrapping behavior
  - news-impact bucket scrolling and narrow-screen layout fallback
  - trading-plan modal self-contained backdrop, overflow, and responsive form grid
- Verification:
  - Command: `npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
  - Result: passed, 59/59 tests.

### 2026-03-24 Trading-Plan Style Decoupling Follow-up
- Continued the same cleanup for the remaining trading-plan surfaces that still depended on parent-scoped CSS:
  - `frontend/src/modules/stocks/StockTradingPlanSection.vue`
  - `frontend/src/modules/stocks/StockTradingPlanBoard.vue`
- Localized plan card, badge, action-row, list-scroll, and responsive layout rules inside those child components, then removed the stale trading-plan descendant styles from `frontend/src/modules/stocks/StockInfoTab.vue`.
- During that pass, the top of `StockInfoTab.vue` was accidentally damaged; the repair restored the extracted imports from `stockInfoTabPlanHelpers.js`, `stockInfoTabWorkspace.js`, `stockInfoTabFormatting.js`, and `tradingPlanReview`, and reattached the workspace/realtime computed block to the correct place in the parent component.
- Verification:
  - Command: `npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
  - Result: passed, 59/59 tests.

## 中文

### 范围
- 在不改数据流和交互行为的前提下，把过长的 Vue 页面拆成更容易阅读的小组件。

### 动作
- 从 `frontend/src/modules/stocks/StockAgentPanels.vue` 中提取 `StockAgentCard.vue`，收口单个 Agent 卡片的证据、指标、标签和原始 JSON 展示。
- 从 `frontend/src/modules/market/MarketSentimentTab.vue` 中提取 `MarketRealtimeOverview.vue`，收口实时总览区的指数快照、资金与广度、涨跌分布桶。
- 从 `frontend/src/modules/stocks/StockInfoTab.vue` 中提取 `StockSourceLoadProgress.vue`，复用股票加载进度区块，覆盖已加载和空态两种终端展示。
- 父页面只保留状态、请求和事件编排，模板阅读密度明显下降。

### 验证
- 命令：`npm --prefix .\frontend run test:unit -- src/modules/stocks/StockAgentPanels.spec.js src/modules/market/MarketSentimentTab.spec.js src/modules/stocks/StockInfoTab.spec.js`
- 结果：通过，57/57。
- Browser MCP：
  - `http://localhost:5119/?tab=stock-info` 页面渲染正常。
  - `http://localhost:5119/?tab=market-sentiment` 页面中拆出的实时总览区正常显示，`资金与广度`、`涨跌分布桶` 都能看到。

### 问题
- Browser MCP 验证情绪页时，`/api/market/sectors?boardType=concept&page=1&pageSize=12&sort=strength` 返回了现有服务端错误。
- 拆出的实时总览区仍然正常渲染，因此记录为当前运行环境中的已有后端问题，不判定为本次模板拆分回归。

### 2026-03-24 Follow-up
- 沿着同一条可读性拆分主线，继续处理已经膨胀到 4915 行的 `frontend/src/modules/stocks/StockInfoTab.vue`。
- 新增 3 个仅供股票页使用的辅助模块：
  - `frontend/src/modules/stocks/stockInfoTabWorkspace.js`
  - `frontend/src/modules/stocks/stockInfoTabCopilot.js`
  - `frontend/src/modules/stocks/stockInfoTabPlanHelpers.js`
- 把 workspace 工厂、加载阶段常量、Copilot tool/result 辅助逻辑，以及交易计划 gating 规则从主 SFC 中移出；页面状态归属、请求编排和模板行为保持原状。
- 结果：`StockInfoTab.vue` 行数从 4915 降到 4558，用户可见行为未改。
- 验证：
  - 命令：`npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
  - 结果：通过，59/59。

### 2026-03-24 交易计划纯逻辑继续外移 Follow-up
- 继续沿着同一条可读性拆分主线收缩 `frontend/src/modules/stocks/StockInfoTab.vue`，把仍然留在父组件里的交易计划纯函数和展示辅助逻辑抽离到新模块：
  - `frontend/src/modules/stocks/stockInfoTabTradingPlans.js`
- 本次移出的内容包括：交易计划与市场上下文归一化、表单默认值工厂、实时上下文 symbol 拼装、最新告警摘要、复盘文案与标题、以及交易计划编辑/恢复 gating 判断。
- 同时从 `StockInfoTab.vue` 移除了已由 `StockTradingPlanSection.vue`、`StockTradingPlanBoard.vue`、`StockTradingPlanModal.vue` 自己持有的残留交易计划样式，避免父级继续为子组件承担 scoped CSS 依赖。
- 处理中曾因 `buildRealtimeContextSymbols(...)` 的新签名遗漏调用点，导致运行态出现 `domesticSymbols is not iterable`；随后已把 `DOMESTIC_REALTIME_CONTEXT_SYMBOLS` 与 `GLOBAL_REALTIME_CONTEXT_SYMBOLS` 显式传回调用点，回归恢复。
- 验证：
  - 命令：`npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
  - 结果：首次发现 2 个失败用例并定位到 realtime context helper 签名错配；修复后再次执行，59/59 通过。
  - 命令：`start-all.bat`
  - 结果：完成前端 build、后端 publish、桌面宿主 publish，并成功拉起 packaged desktop；本地 `http://localhost:5119/` 监听恢复正常。
  - 命令：`Invoke-WebRequest http://localhost:5119/?tab=stock-info`
  - 结果：返回 HTTP 200。
  - Browser MCP：打开 `http://localhost:5119/?tab=stock-info` 后，首屏关键请求 `/api/news?level=market`、`/api/market/realtime/overview`、`/api/stocks/plans*` 全部返回 200，console `error/warn` 为 0，并实测通过 `展开阅读 -> 关闭` 与 `顶部市场总览带 隐藏 -> 显示` 两组基础交互。

### 2026-03-24 第二次 Follow-up
- 继续把 `frontend/src/modules/stocks/StockInfoTab.vue` 中的交易计划模板区域拆成两个子组件：
  - `frontend/src/modules/stocks/StockTradingPlanSection.vue`
  - `frontend/src/modules/stocks/StockTradingPlanModal.vue`
- 父组件仍保留交易计划的请求、保存、删除、恢复以及 workspace 状态归属；新子组件只负责渲染当前计划面板和弹窗表单，并把用户动作通过事件抛回 `StockInfoTab.vue`。
- 结果：`StockInfoTab.vue` 行数从 4558 进一步降到 4428。
- 验证：
  - 命令：`npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
  - 结果：通过，59/59。

### 2026-03-24 交易计划纯逻辑继续外移 Follow-up
- 继续沿着同一条可读性拆分主线压缩 `frontend/src/modules/stocks/StockInfoTab.vue`，把仍残留在父组件中的交易计划纯函数和展示辅助逻辑提取到新模块：
  - `frontend/src/modules/stocks/stockInfoTabTradingPlans.js`
- 本次外移的内容包括：交易计划与市场上下文归一化、表单默认值工厂、实时上下文 symbol 组装、最新告警摘要、复盘文案与标题，以及交易计划编辑/恢复的 gating 判断。
- 同时从 `StockInfoTab.vue` 清除了已经由 `StockTradingPlanSection.vue`、`StockTradingPlanBoard.vue`、`StockTradingPlanModal.vue` 自持的残留交易计划样式，避免父级继续保留对子组件的 scoped CSS 依赖。
- 处理中一度因为 `buildRealtimeContextSymbols(...)` 新签名有旧调用点未同步，触发 `domesticSymbols is not iterable`；随后已经把 `DOMESTIC_REALTIME_CONTEXT_SYMBOLS` 与 `GLOBAL_REALTIME_CONTEXT_SYMBOLS` 显式传回调用点并恢复回归。
- 验证：
  - 命令：`npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
  - 结果：首次执行发现 2 个失败用例并定位到 realtime context helper 签名错配；修复后再次执行，59/59 通过。
  - 命令：`start-all.bat`
  - 结果：完成前端 build、后端 publish、桌面宿主 publish，并成功拉起 packaged desktop；本地 `http://localhost:5119/` 监听恢复正常。
  - 命令：`Invoke-WebRequest http://localhost:5119/?tab=stock-info`
  - 结果：返回 HTTP 200。
  - Browser MCP：打开 `http://localhost:5119/?tab=stock-info` 后，首屏关键请求 `/api/news?level=market`、`/api/market/realtime/overview`、`/api/stocks/plans*` 全部返回 200，console `error/warn` 为 0，并实测通过 `展开阅读 -> 关闭` 与 `顶部市场总览带 隐藏 -> 显示` 两组基础交互。

### 2026-03-24 继续拆分页面内 JS Helper
- `frontend/src/modules/stocks/StockInfoTab.vue` 里剩余的大块 JS 仍可继续拆；这次先处理不涉及请求编排顺序的无状态 helper，避免把 refactor 变成行为改写。
- 新增两个 sidecar 模块：
  - `frontend/src/modules/stocks/stockInfoTabRequestUtils.js`
  - `frontend/src/modules/stocks/stockInfoTabViewHelpers.js`
- 外移内容包括：
  - abort/retry/request message 解析等请求辅助逻辑
  - 本地资讯与 realtime overview 归一化
  - 聊天上下文拼装
  - AI 价位解析辅助
  - 历史表格排序值计算
  - 价格涨跌 class 与百分比展示等纯视图 helper
- 结果：父组件继续只保留响应式状态、watch、生命周期和真正的业务编排，页面脚本阅读成本进一步下降。
- 验证：
  - 命令：`npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
  - 结果：通过，59/59。
  - 命令：`start-all.bat`
  - 结果：本地 packaged desktop 与 backend 重新拉起成功。
  - 命令：`Invoke-WebRequest http://localhost:5119/?tab=stock-info`
  - 结果：返回 HTTP 200。
  - Browser MCP：重新打开 `http://localhost:5119/?tab=stock-info`，console `error/warn` 为 0。

### 2026-03-24 继续拆分页面内 JS Helper
- `frontend/src/modules/stocks/StockInfoTab.vue` 里剩余的大块 JS 仍然可以继续拆；这次先处理不涉及请求编排顺序的无状态 helper，避免把重构变成行为改写。
- 新增两个 sidecar 模块：
  - `frontend/src/modules/stocks/stockInfoTabRequestUtils.js`
  - `frontend/src/modules/stocks/stockInfoTabViewHelpers.js`
- 本次外移的内容包括：
  - abort/retry/response message 解析等请求辅助逻辑
  - 本地资讯与 realtime overview 归一化
  - 聊天上下文拼装
  - AI 价位解析辅助
  - 历史表格排序值计算
  - 价格涨跌 class 与百分比展示等纯视图 helper
- 结果：父组件进一步收敛为响应式状态、watch、生命周期和真正的业务编排层，脚本阅读成本继续下降。
- 验证：
  - 命令：`npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
  - 结果：通过，59/59。
  - 命令：`start-all.bat`
  - 结果：本地 packaged desktop 与 backend 重新拉起成功。
  - 命令：`Invoke-WebRequest http://localhost:5119/?tab=stock-info`
  - 结果：返回 HTTP 200。
  - Browser MCP：重新打开 `http://localhost:5119/?tab=stock-info`，console `error/warn` 为 0。

### 2026-03-24 继续拆分 Copilot 编排与请求流
- 继续沿着同一条主线处理 `frontend/src/modules/stocks/StockInfoTab.vue` 中仍然过长的业务编排代码，这一轮优先抽离两块中耦合但边界清晰的运行时逻辑：
  - `frontend/src/modules/stocks/stockInfoTabCopilotRuntime.js`
  - `frontend/src/modules/stocks/stockInfoTabDataRequests.js`
- `stockInfoTabCopilotRuntime.js` 收口了股票页里的 Copilot/chat 相关编排，包括：
  - draft turn 生成
  - acceptance baseline 拉取
  - approved tool 执行
  - follow-up action 激活
  - chat session 创建/加载
  - chat history adapter
- `stockInfoTabDataRequests.js` 收口了较独立的数据请求流，包括：
  - market news
  - stock local news / news impact
  - trading plan list / alerts
  - realtime overview
  - sources / history / refresh history
- 父组件 `StockInfoTab.vue` 这次保留了最重的 quote/chart 与多 Agent 主流程，但把 Copilot/chat 编排和一大块 request flow 下沉为可单独阅读的运行时模块，进一步降低了主 SFC 的脚本噪音。
- 验证：
  - 命令：`npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
  - 结果：通过，59/59。
  - Browser MCP：重新打开 `http://localhost:5119/?tab=stock-info`，首屏请求 `/api/stocks/sources`、`/api/stocks/history`、`/api/news?level=market`、`/api/stocks/plans`、`/api/stocks/plans/alerts`、`/api/market/realtime/overview` 均返回 200，console `error/warn` 为 0，页面主结构正常渲染。

### 2026-03-24 继续拆分 Copilot 编排与请求流
- 继续沿着同一条主线处理 `frontend/src/modules/stocks/StockInfoTab.vue` 中仍然过长的业务编排代码，这一轮优先抽离两块中耦合但边界清晰的运行时逻辑：
  - `frontend/src/modules/stocks/stockInfoTabCopilotRuntime.js`
  - `frontend/src/modules/stocks/stockInfoTabDataRequests.js`
- `stockInfoTabCopilotRuntime.js` 集中收口了股票页里的 Copilot/chat 编排，包括：
  - draft turn 生成
  - acceptance baseline 拉取
  - approved tool 执行
  - follow-up action 激活
  - chat session 创建/加载
  - chat history adapter
- `stockInfoTabDataRequests.js` 集中收口了相对独立的数据请求流，包括：
  - 大盘资讯
  - 个股本地资讯 / 资讯影响
  - 交易计划列表 / 告警
  - realtime overview
  - 来源 / 历史记录 / 历史刷新
- 父组件 `StockInfoTab.vue` 这次保留了最重的 quote/chart 与多 Agent 主流程，但把 Copilot/chat 编排和一大块 request flow 下沉为可单独阅读的运行时模块，进一步降低主 SFC 的脚本噪音。
- 验证：
  - 命令：`npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
  - 结果：通过，59/59。
  - Browser MCP：重新打开 `http://localhost:5119/?tab=stock-info`，首屏请求 `/api/stocks/sources`、`/api/stocks/history`、`/api/news?level=market`、`/api/stocks/plans`、`/api/stocks/plans/alerts`、`/api/market/realtime/overview` 均返回 200，console `error/warn` 为 0，页面主结构正常渲染。

### 2026-03-24 第三次 Follow-up
- 继续处理 `frontend/src/modules/stocks/StockInfoTab.vue` 里剩余噪音最高、独立度也最高的几个区域。
- 新增 1 个共享格式化/辅助模块和 4 个纯渲染子组件：
  - `frontend/src/modules/stocks/stockInfoTabFormatting.js`
  - `frontend/src/modules/stocks/StockSearchToolbar.vue`
  - `frontend/src/modules/stocks/StockTopMarketOverview.vue`
  - `frontend/src/modules/stocks/StockMarketNewsPanel.vue`
  - `frontend/src/modules/stocks/StockNewsImpactPanel.vue`
- `StockInfoTab.vue` 现在把搜索工具条、顶部实时市场带、大盘资讯阅读区、右侧资讯影响面板都委托给这些文件；父层继续保留请求编排、缓存、定时器和 workspace 状态归属。
- 验证：
  - 命令：`npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
  - 结果：通过，59/59。

### 2026-03-24 第四次 Follow-up
- 继续把 `frontend/src/modules/stocks/StockInfoTab.vue` 中仍然信息密度很高的摘要块拆出去：
  - `frontend/src/modules/stocks/StockTerminalSummary.vue`
  - `frontend/src/modules/stocks/StockTradingPlanBoard.vue`
- 用新子组件替换了终端摘要区和根级交易计划总览区，并把父组件里已经迁出的搜索、市场、资讯、摘要样式一并清理掉。
- 结果：`StockInfoTab.vue` 行数进一步降到 3484。
- 验证：
  - 命令：`npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
  - 结果：通过，59/59。
  - 命令：`Invoke-WebRequest http://localhost:5119/?tab=stock-info`
  - 结果：本地启动后返回 HTTP 200。
  - 浏览器检查：打开 `http://localhost:5119/?tab=stock-info`，控制台 error 数量为 0。
  - 限制：当前 VS Code 会话未开启可读取页面内容的 browser chat tools，因此这轮浏览器验收只能做到可达性和控制台回归，不能继续做 DOM 级点击断言。

### 2026-03-24 样式约束清扫
- 对这轮拆出来的股票子组件做了一次系统性样式补漏，收口拆分后最容易漏掉的布局约束。
- 本次补齐或本地化的目标文件：
  - `frontend/src/modules/stocks/StockSearchToolbar.vue`
  - `frontend/src/modules/stocks/StockTopMarketOverview.vue`
  - `frontend/src/modules/stocks/StockNewsImpactPanel.vue`
  - `frontend/src/modules/stocks/StockTradingPlanModal.vue`
- 清扫范围：
  - sticky 工具条外壳与历史条滚动上限
  - 搜索下拉最大高度、内部滚动和右键菜单定位
  - 顶部市场带按钮布局与窄屏换行
  - 资讯影响 bucket 列表滚动与窄屏降级
  - 交易计划弹层自身的 backdrop、overflow 与响应式表单栅格
- 验证：
  - 命令：`npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
  - 结果：通过，59/59。

### 2026-03-24 交易计划样式去父依赖 Follow-up
- 继续把剩余仍依赖父级 scoped CSS 的交易计划区域补齐为自包含组件：
  - `frontend/src/modules/stocks/StockTradingPlanSection.vue`
  - `frontend/src/modules/stocks/StockTradingPlanBoard.vue`
- 这次把交易计划卡片、状态徽标、操作区、列表滚动和响应式布局约束都本地化到子组件里，并从 `frontend/src/modules/stocks/StockInfoTab.vue` 移除了残留的交易计划后代样式。
- 处理中一度误伤了 `StockInfoTab.vue` 顶部脚本区，随后已经把 `stockInfoTabPlanHelpers.js`、`stockInfoTabWorkspace.js`、`stockInfoTabFormatting.js` 和 `tradingPlanReview` 的导入，以及 workspace/realtime 相关 computed 绑定区恢复到正确位置。
- 验证：
  - 命令：`npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
  - 结果：通过，59/59。

### 2026-03-24 Quote/Agent Runtime Follow-up
- Continued shrinking `frontend/src/modules/stocks/StockInfoTab.vue` by extracting the two heaviest remaining runtime clusters that were still local to the page:
  - `frontend/src/modules/stocks/stockInfoTabQuoteRuntime.js`
  - `frontend/src/modules/stocks/stockInfoTabAgentRuntime.js`
- `stockInfoTabQuoteRuntime.js` now owns the quote/chart refresh orchestration, including:
  - first-load quote flow
  - cache + live chart parallel path
  - Tencent/Eastmoney progress stages
  - lightweight `refreshChartData(...)`
  - message/fundamental side requests bound to the same request token
- `stockInfoTabAgentRuntime.js` now owns the multi-agent execution and history flow, including:
  - agent history list fetch
  - agent history detail load
  - history save request
  - `runAgents(...)` sequential orchestration and commander-history gating
- `StockInfoTab.vue` keeps the same public methods and workspace ownership, but the remaining page script is more focused on reactive state, watchers, and cross-runtime wiring.
- Verification:
  - Command: `npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
  - Result: passed, 59/59.
  - Browser MCP: opening `http://localhost:5119/?tab=stock-info` in this round hit local `ERR_CONNECTION_REFUSED` on `/api/stocks/plans*`, so browser acceptance was blocked by the current service state rather than by a frontend compile/runtime regression.

### 2026-03-24 Quote/Agent Runtime Follow-up
- 继续压缩 `frontend/src/modules/stocks/StockInfoTab.vue`，把页面里剩余最重的两块运行时逻辑继续下沉到 sidecar 模块：
  - `frontend/src/modules/stocks/stockInfoTabQuoteRuntime.js`
  - `frontend/src/modules/stocks/stockInfoTabAgentRuntime.js`
- `stockInfoTabQuoteRuntime.js` 负责收口行情与图表刷新编排，包括：
  - 首次查股的 quote 流程
  - cache + live chart 并行路径
  - 腾讯 / 东财加载进度阶段
  - 轻量 `refreshChartData(...)`
  - 与同一 request token 绑定的消息 / 基本面补充请求
- `stockInfoTabAgentRuntime.js` 负责收口多 Agent 执行与历史流，包括：
  - Agent 历史列表拉取
  - Agent 历史详情加载
  - 历史保存请求
  - `runAgents(...)` 顺序编排与 commander 完整性 gating
- `StockInfoTab.vue` 继续保留原有对外方法名和 workspace 状态归属，但页面脚本进一步聚焦到响应式状态、watch 和跨 runtime 的连接层。
- 验证：
  - 命令：`npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
  - 结果：通过，59/59。
  - Browser MCP：本轮打开 `http://localhost:5119/?tab=stock-info` 时，本地运行环境对 `/api/stocks/plans*` 返回 `ERR_CONNECTION_REFUSED`，因此浏览器验收被当前服务态阻塞；未观察到由这次前端拆分引入的静态错误或单测回归。