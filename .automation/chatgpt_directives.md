# 给 ChatGPT-5.4 (开发人员) 的当前有效任务书

> 致 ChatGPT-5.4：
> 本文件已按 2026-03-14 当前状态瘦身。目标是让你优先关注仍未完成、仍需验收、或仍然影响后续开发的内容。
>
> 规则：
> 1. 只围绕“当前有效任务”和“仍然生效的架构约束”行动。
> 2. 已完成、已验收、或已被后续纠偏覆盖的历史内容，不要再当作当前开发清单重复展开。
> 3. 若需要追溯历史细节，请去 `.automation/reports/` 查对应阶段报告，而不是把旧回执继续堆回本文件。

---

## 当前状态快照

### 已完成并已归档
1. GOAL-012 / Step 1：看盘终端与 AI 面板物理隔离、主界面重构，已完成。
2. GOAL-013 / Step 2 系列：本地资讯中枢、资讯库、LLM 入库前清洗、多源新闻管线、全量资讯库 UI，已完成并归档。
3. Step 3：股票基本面扩维、Prompt 重构、Commander 结构化输出，已完成。
4. Step 4.0：股票详情缓存秒开、并发加载、旧响应覆盖防护、active provider 路由，已完成。
5. Step 4.1：高频监控白名单机制补齐，已开发并完成当前补齐回合。
6. Step 4.2：人机协同交易计划，已完成最小闭环并通过本轮验证。

### 当前活跃范围
1. Dev2 并行任务：执行 GOAL-012-R2 `klinecharts` 受控替换试验，必须继续与主线隔离推进。
2. Dev1 下一步主线：Step 4.3 盘中定量规则盯盘引擎，作为 GOAL-008 当前优先开发目标。
3. Step 4.4 - Step 4.5：继续保留为后续 backlog，不提前扩 scope。
4. 已完成步骤（Step 4.1 / 4.2）仅保留归档说明与复用约束，不再按“进行中”处理。

---

## 仍然生效的全局架构约束

1. 图表与 AI 必须保持解耦。左侧终端优先给图表和核心行情，AI 面板只能作为侧栏或辅助区域，不能重新侵入主看盘区。
2. 国内 A 股事实必须坚持 Local-First：公告、个股资讯、板块资讯、大盘事实优先由本地 C# 采集和数据库查询提供；海外宏观与国际政经只在明确需要时才允许外网搜索。
3. Step 4 必须坚持三层分工：
   - Pro 层：分析、解释、起草计划、语义复核
   - System 层：缓存、轮询、触发检测、风险门控、告警派发
   - Human 层：最终确认、修改、采纳或取消计划
4. Step 4 的目标不是“全自动 AI 交易”，而是“AI 帮用户起草计划，系统帮用户执行纪律，人类保留最终确认权”。
5. Pro 模型不能进入盘中高频主阻断热路径。盘中第一优先级必须是确定性的 C# 规则链路。
6. 交易计划草稿必须以后端为真源，前端只能展示和编辑，不能用临时 agent JSON 直接当正式入库依据。
7. 交易计划的触发价、失效价只允许落库确定性数值；取不到明确数值时必须返回空值并要求用户手工补录，禁止从自然语言里臆测价格。
8. 创建交易计划后，必须自动把该 symbol upsert 进入 `ActiveWatchlist`，供后续高频缓存与触发器复用。

---

## Step 4.1：高频监控白名单机制补齐

状态标签：`已完成（归档基线）`

### 当前说明
本节保留为 Step 4.1 的归档基线与复用约束。若无新的 reviewer 缺陷，不要重新把本节作为当前开发主线展开。

### 目标
补齐 Step 4.1 剩余缺口，使 `ActiveWatchlist + HighFrequencyQuoteService` 真正满足“盘中高频缓存底座”的原始任务定义，并达到可正式验收状态。

### 输入
1. 已存在的 `ActiveWatchlist` 实体、migration、schema initializer。
2. 已存在的 `HighFrequencyQuoteService` 与白名单 CRUD API。
3. 已存在的 quote / minute / messages 回写链路。
4. 当前 Reviewer 发现的两个缺口：盘中快讯来源为空实现、A 股法定休市日未门控。

### 输出
1. 一个可验证的盘中快讯来源实现，或明确的稳定降级方案。
2. 一个可复用的 A 股交易日历能力，用于过滤法定休市日。
3. 补齐后的单测、报告和新的 Step 4.1 补齐回执。

### 已确认完成
1. 已建立 `ActiveWatchlist` 表，并有 migration + schema initializer 双路径。
2. 已实现 `HighFrequencyQuoteService`，并注册后台 worker。
3. 已对白名单标的回写 quote / minute / messages 到本地缓存表。
4. 已有相关定向单测通过。

### 核心任务
1. 补齐盘中快讯来源：
   - 当前 `EastmoneyStockCrawler.GetIntradayMessagesAsync(...)` 仍是空实现。
   - 需要补“明确、可验证”的盘中快讯来源；如果东财没有稳定接口，必须改成“稳定快讯源 + 来源标记 + 可切换优先级”的降级方案，并更新报告与回执表述。
2. 补齐 A 股法定休市日门控：
   - 当前只过滤周末和盘中时段，没有过滤法定休市日。
   - 需要补 A 股假期交易日历，或抽象出 `ITradingCalendar` 并给出默认中国 A 股实现。

### 验收
1. 单测至少覆盖：
   - 节假日不轮询
   - 盘中快讯来源不是空实现
   - 快讯源异常时仍保留 quote / minute 主流程落库
2. 更新 `.automation/reports/GOAL-008-DEV-STEP41-20260314.md`，把状态写成真实状态。
3. 补齐后再提交新的 Step 4.1 补齐回执。

---

## Step 4.2：人机协同交易计划

状态标签：`已完成（归档基线）`

### 当前说明
本节保留为 Step 4.2 的归档基线与复用约束。后续如需继续推进，只能在 Step 4.3-4.5 或兼容修复范围内复用本节结果，不要回退为重新开发状态。

### 目标
复用现有 commander 归一化结果，形成“后端生成草稿 -> 用户编辑确认 -> Pending 入库 -> 自动进 ActiveWatchlist”的最小闭环。

### 输入
1. `StockAgentAnalysisHistory` 中已保存的多 Agent 历史。
2. commander 已归一化的 `summary`、`analysis_opinion`、`trigger_conditions`、`invalid_conditions`、`risk_warning`、`triggers`、`invalidations`、`riskLimits`。
3. 现有 `StockAgentPanels.vue`、`StockInfoTab.vue`、`StocksModule.cs`、`IActiveWatchlistService.UpsertAsync(...)`。

### 输出
1. `TradingPlan` 实体、服务、API 和前端起草/保存闭环。
2. 用户可编辑并保存的 `Pending` 交易计划。
3. 自动进入 `ActiveWatchlist` 的联动结果。
4. 配套后端与前端测试。

### 必须遵守的原则
1. 不要再新增一轮 LLM 去“猜”计划字段。
2. authoritative draft 必须由后端基于 `StockAgentAnalysisHistory` 生成。
3. 人工确认是必须步骤，用户必须能修改价格和文本。
4. 仅完成 Step 4.2，不要提前推进 Step 4.3 / 4.4 / 4.5。

### 当前已可复用基础
1. `StockAgentPanels.vue` 已能展示 commander 的 `summary`、`analysis_opinion`、`triggers`、`invalidations`、`riskLimits`。
2. `StockInfoTab.vue` 已有 workspace 状态、agent history 切换和弹窗样式基础。
3. `StocksModule.cs` 已有 `/api/stocks/agents/history` 与 `/api/stocks/watchlist` 端点风格可复用。
4. `StockAgentAnalysisHistory` 已保存多 Agent 历史，是计划草稿的后端真源。
5. `IActiveWatchlistService.UpsertAsync(...)` 已可复用为计划保存后的联动入口。

### 核心任务
1. 新增 `TradingPlan` 实体、枚举、DbSet、migration、schema initializer。
2. 新增 `ITradingPlanService`：查询、创建、更新、取消。
3. 新增 `ITradingPlanDraftService`：从 `StockAgentAnalysisHistory` 中抽取 commander 并生成 draft。
4. 新增 `/api/stocks/plans` 与 `/api/stocks/plans/draft` API。
5. 在 `StockAgentPanels.vue` 增加“基于此分析起草交易计划”按钮与事件。
6. 在 `StockInfoTab.vue` 增加计划草稿弹窗、编辑表单、保存逻辑、当前计划列表。
7. 补测试：draft 映射、保存后自动入 `ActiveWatchlist`、空价格允许手工补录、前端按钮与弹窗流程。

### 输出定义

#### 1. `TradingPlan` 建议字段
1. `Id`
2. `Symbol`
3. `Name`
4. `Direction`：先支持 `Long` / `Short`
5. `Status`：先支持 `Pending` / `Triggered` / `Invalid` / `Cancelled`
6. `TriggerPrice`
7. `InvalidPrice`
8. `StopLossPrice`
9. `ExpectedCatalyst`
10. `InvalidConditions`
11. `RiskLimits`
12. `AnalysisSummary`
13. `AnalysisHistoryId`
14. `SourceAgent`
15. `UserNote`
16. `CreatedAt` / `UpdatedAt`
17. `TriggeredAt` / `InvalidatedAt` / `CancelledAt`

#### 2. Draft 生成规则
1. `AnalysisSummary` <- `analysis_opinion`，为空则退回 `summary`
2. `ExpectedCatalyst` <- `trigger_conditions`
3. `InvalidConditions` <- `invalid_conditions`
4. `RiskLimits` <- 优先 join `riskLimits[]`，为空再退回 `risk_warning`
5. `TriggerPrice` <- 仅从确定性数值来源提取，如 `chart.breakoutPrice`
6. `InvalidPrice` <- 仅从确定性数值来源提取，如 `chart.supportPrice`
7. `Direction` <- 若 commander 给出明确方向则采用，否则默认 `Long`
8. `Status` 固定为 `Pending`

#### 3. API 要求
1. `GET /api/stocks/plans?symbol=xxx`
2. `GET /api/stocks/plans/{id}`
3. `POST /api/stocks/plans/draft`
   - 入参：`symbol`, `analysisHistoryId`
4. `POST /api/stocks/plans`
   - 创建 `Pending` 计划，并自动进入 `ActiveWatchlist`
5. `PUT /api/stocks/plans/{id}`
6. `POST /api/stocks/plans/{id}/cancel`

#### 4. 保存计划后的联动
创建成功后必须执行：
1. `watchlistService.UpsertAsync(symbol, name, "trading-plan", $"plan:{id}", true)`
2. 返回结果中应包含 `watchlistEnsured = true`

### 前端实现要求
1. `StockAgentPanels.vue`
   - 仅在 commander 成功数据存在时显示“基于此分析起草交易计划”按钮
   - 通过事件通知父组件，不要在子组件内直接请求 API
2. `StockInfoTab.vue`
   - 增加 `planDraftLoading`、`planSaving`、`planError`、`planModalOpen`、`planForm`、`planList`
   - 若当前没有 `selectedAgentHistoryId`，先调用现有 agent history 保存接口
   - 再调用 `/api/stocks/plans/draft`
   - 用户确认后调用 `/api/stocks/plans`
   - 保存成功后刷新当前股票页的计划列表
3. 当前页最小计划展示
   - 标题：`当前交易计划`
   - 展示最近 1-3 条计划
   - 显示状态、触发价、失效价、风险摘要

### 验收
1. commander 分析可一键生成草稿
2. 用户可手动修改价格与文本
3. 计划以 `Pending` 入库
4. symbol 自动进入 `ActiveWatchlist`
5. 当前股票页能立即看到新计划
6. 新增测试全部通过

---

## Dev2 并行任务：图表终端组件升级与多周期 Tab 改造

状态标签：`已完成，待 PM 分派后续任务`

### 最新回执（2026-03-14）
1. Dev2 图表终端改造已完成并验证：主图已改为统一 Tab 单视口，按钮为 `分时图 / 日K图 / 月K图 / 年K图`。
2. 已完成组件选型补充调研，结论已写入 `.automation/reports/GOAL-012-R1-RESEARCH-20260314.md`。
3. 调研结论：
   - `SuHangWeb/tradingview-vue` 不适合当前仓库，原因是 Vue 2 / 模板工程属性过重、集成成本高、对当前 Vue 3 + Vite 形态不友好。
   - `vue-tradingview-widgets` 更适合嵌 TradingView 官方 widget，不适合作为本地 `minuteLines` / `kLines` 主图内核。
   - `vue3-apexcharts` 可用但偏通用报表图，不是股票终端主图优先解。
   - 若 PM 仍想继续试验换库，唯一值得继续原型验证的候选是 `klinecharts`。
4. 当前建议：保持 `lightweight-charts + 自建 charting adapter` 作为生产路径；若安排下一步任务，应优先在现有适配层之上补成交量副图、MA/BOLL/KDJ、标记层等专业终端能力，或单独安排 `klinecharts` POC。

### PM 决策（2026-03-14）
1. 用户已明确要求替换组件，因此 Dev2 下一步不再继续抽象调研，而是执行 `klinecharts` 受控替换试验。
2. `TradingView-vue` 不进入下一步开发：当前调研已确认它不适合当前 Vue 3 + Vite 工程。
3. 下一步替换目标确定为 `klinecharts`，但必须继续通过现有 `charting/**` 适配层完成，不得直接打散父层 contract。

### Dev2 下一步任务：GOAL-012-R2 `klinecharts` 受控替换试验

状态标签：`待开发`

#### 目标
在不干扰 Dev1 的 Step 4.2 交易计划主线的前提下，把当前图表适配层底层引擎从 `lightweight-charts` 受控替换为 `klinecharts`，验证其是否足以成为未来专业终端的主图内核。

#### 输入
1. 当前图表终端基线：
   - `frontend/src/modules/stocks/StockCharts.vue`
   - `frontend/src/modules/stocks/charting/chartViews.js`
   - `frontend/src/modules/stocks/charting/useStockChartAdapter.js`
2. 当前对外 contract：`minuteLines`、`kLines`、`basePrice`、`interval`、`aiLevels`、`update:interval`
3. 已完成的组件调研结论：
   - `TradingView-vue` 不适用
   - `vue-tradingview-widgets` 不适合作为本地主图内核
   - `vue3-apexcharts` 不适合作为股票终端主图优先方案
   - `klinecharts` 是唯一值得继续替换试验的候选
4. 公开组件资料依据：
   - `klinecharts` README 明确声明内置多个 indicators、line drawing models、rich style configuration、highly scalable
   - npm 元数据显示当前最新版本为 `10.0.0-beta1`

#### 输出
1. 基于 `klinecharts` 的新图表适配器实现。
2. 维持不变的父层数据与事件 contract。
3. 针对未来神奇九转、KDJ 金叉、BOLL、标记层的 overlay / indicator 注册入口。
4. 前端单测、build、浏览器切换验证，以及一份“是否正式切生产”的回执结论。

#### 与 Dev1 的隔离边界
1. Dev2 只允许改：
   - `frontend/src/modules/stocks/StockCharts.vue`
   - `frontend/src/modules/stocks/charting/**`
   - `frontend/package.json`
   - 必要的前端测试文件
2. Dev2 禁止改：
   - `TradingPlan` 后端实体/API
   - Step 4.2 的 draft/save 流
   - `StockAgentAnalysisHistory` contract
   - Dev1 正在推进的交易计划表单状态机
3. 若必须触碰 `StockInfoTab.vue`，只能保持现有 props / emits / slot contract 向后兼容。

#### 核心任务
1. 安装并精确锁定 `klinecharts` 版本：
   - 不允许使用浮动版本
   - 由于当前最新为 beta，必须在回执中记录实际锁定版本与风险判断
2. 新建 `klinecharts` 适配器，而不是直接把逻辑堆回 `StockCharts.vue`：
   - 建议新增 `frontend/src/modules/stocks/charting/useKLineChartsAdapter.js`
   - 原 `useStockChartAdapter.js` 可演进为统一入口或引擎分发层
3. 保持父层 contract 不变：
   - `StockCharts.vue` 对外 props / emits 保持兼容
   - `chartViews.js` 的 `minute/day/month/year` 视图定义继续复用
4. 完成单主图切换适配：
   - `分时图` 映射到分钟数据视图
   - `日K图 / 月K图 / 年K图` 映射到 K 线视图
   - 不允许回退到双图并列布局
5. 第一阶段只迁移已有能力：
   - 主图渲染
   - AI 支撑/突破线
   - 现有 hover / crosshair 信息
6. 第二阶段预留扩展点：
   - overlay registry
   - indicator registry
   - marker / signal layer API
   - 为神奇九转、KDJ 金叉、BOLL、MA 等后续能力预留挂载方式

#### 文档约束
1. 若 `klinecharts` 在分钟图、交叉线、性能或扩展点上不达标，Dev2 不得硬切生产，必须在回执中给出回退结论。
2. 若 `klinecharts` 达标，也必须确保不破坏当前 `StockCharts.vue` 父层 contract，避免影响 Dev1。

#### 验收
1. 图表主引擎切换为 `klinecharts`，且 `分时图 / 日K图 / 月K图 / 年K图` 仍正常工作。
2. `minuteLines`、`kLines`、`interval`、`aiLevels` 现有数据 contract 不变。
3. 适配层具备清晰的 overlay / indicator 扩展入口。
4. Dev2 改动不影响 Dev1 的 Step 4.2 交易计划流。
5. 至少完成：
   - 前端单测
   - `npm --prefix frontend run build`
   - 浏览器交互验证（切换分时图、日K图、月K图、年K图）

### 目标
在不干扰 Dev1 正在推进的 Step 4.2 交易计划主线的前提下，独立升级股票终端图表区，寻找更高扩展性的 K 线/分时图组件方案，并把分时图放入与 K 线多周期一致的 Tab 切换中，为未来神奇九转、KDJ 金叉等策略叠加打基础。

### 输入
1. 当前前端图表实现：`frontend/src/modules/stocks/StockCharts.vue` 基于 `lightweight-charts`，分时图与 K 线图是并列渲染。
2. 当前终端容器：`TerminalView.vue` / `StockInfoTab.vue`。
3. 未来扩展需求：神奇九转、KDJ 金叉、更多指标叠加、更多周期与图层控制。
4. 用户明确方向：尝试 `TradingView-vue` 或 TradingView 风格、具备高扩展性的 Vue 图表组件封装。

### 输出
1. 一个不影响 Step 4.2 的前端图表层改造方案。
2. 分时图 / 日K图 / 月K图 / 年K图 的统一 Tab 切换 UI。
3. 一个具备后续指标扩展能力的图表适配层或组件抽象。
4. 组件选型结论、前端测试和可继续扩展的接口设计。

### 与 Dev1 的隔离边界
1. Dev1 负责 Step 4.2 主线：`TradingPlan` 实体、后端 API、计划弹窗、保存流、`ActiveWatchlist` 联动。
2. Dev2 负责图表终端升级：优先改 `StockCharts.vue`、新增 `frontend/src/modules/stocks/charting/**`、必要时调整 `frontend/package.json`。
3. Dev2 禁止改动：
   - `TradingPlan` 后端实体与 API contract
   - `StockAgentAnalysisHistory` / Step 4.2 草稿生成逻辑
   - Dev1 正在开发中的交易计划表单状态流
4. 若必须触碰 `StockInfoTab.vue`，只能保持现有 props / emits contract 向后兼容，且改动应限制在图表挂载方式，不得侵入交易计划区域。

### 核心任务
1. 调研并优先尝试高扩展性图表组件方案：
   - 首选方向：`TradingView-vue` 或 TradingView 风格的 Vue 封装组件
   - 对比当前 `lightweight-charts` 方案，重点评估：多指标叠加能力、绘图扩展点、Tab/周期切换能力、Vue 3 兼容性、未来策略标记扩展成本
2. 重构图表区交互：
   - 将分时图并入主图 Tab 切换
   - 切换按钮统一为：`分时图`、`日K图`、`月K图`、`年K图`
   - 确保切换后主图只保留一个图表视图，避免现有并列双图布局继续占用空间
3. 建立扩展性抽象：
   - 为未来神奇九转、KDJ 金叉、更多指标线、标注层提供统一挂载入口
   - 优先通过图表适配层或独立 `charting` 子目录组织，而不是把所有逻辑继续堆进 `StockCharts.vue`
4. 保持现有数据源兼容：
   - 分时图继续使用 `minuteLines`
   - K 线继续基于现有 `/api/stocks/kline` 数据
   - 不要求 Dev2 改后端接口语义

### 组件选型要求
1. 若 `TradingView-vue` 可用且扩展性明显优于当前实现，应优先采用。
2. 若 `TradingView-vue` 在 Vue 3、许可证、可控性或后续策略叠加上存在明显缺陷，Dev2 必须在回执中明确写出对比结论，并给出保留 `lightweight-charts + 自建适配层` 的理由。
3. 最终目标不是“换库本身”，而是“获得更好的扩展性与更专业的图表交互形态”。

### 验收
1. 图表切换按钮变为：`分时图 / 日K图 / 月K图 / 年K图`。
2. 分时图被纳入同一 Tab 切换容器，不再与 K 线图长期并列占位。
3. 图表层代码具备明确扩展点，可继续叠加神奇九转、KDJ 金叉等策略图层。
4. Dev2 改动不破坏 Dev1 的 Step 4.2 主线开发。
5. 前端单测、构建、必要的浏览器交互验证通过。

---

## 后续 backlog（当前不要提前做，但保留完整任务定义）

### Step 4.3：盘中定量规则盯盘引擎

状态标签：`待开发（下一步）`

### 目标
在已有 `TradingPlan` 和 `ActiveWatchlist` 基础上，建立纯 C# 的盘中确定性触发链路。这里是系统层职责，绝不让 LLM 进入主阻断热路径。

### 输入
1. 已落库的 `TradingPlan`。
2. Step 4.1 的高频行情缓存与 `ActiveWatchlist`。
3. 明确的 `TriggerPrice` / `InvalidPrice` 等数值规则。

### 输出
1. `TradingPlanTriggerService`。
2. `Pending -> Triggered / Invalid` 的确定性状态迁移。
3. 一套可追溯的计划事件/告警落库结构。
4. 一个供前端短轮询消费的计划状态/告警查询接口。

### 核心任务
1. 编写 `TradingPlanTriggerService`，以后台轮询方式运行，但只消费本地缓存，不触发新的外部行情抓取。
2. 只读取 `Pending` 状态的计划，并结合 `ActiveWatchlist`、`StockQuoteSnapshots`、`MinuteLinePoints` 做判断；不在 Step 4.3 处理 `Cancelled` / `Invalid` / `Triggered` 之外的新业务状态。
3. 明确 V1 状态机与优先级：
   - Long 计划中，若 `InvalidPrice` 有值且最新价 `<= InvalidPrice`，优先标记为 `Invalid`
   - 否则，若 `TriggerPrice` 有值且最新价 `>= TriggerPrice`，标记为 `Triggered`
   - 同一轮中命中失效与触发冲突时，`Invalid` 优先于 `Triggered`
4. 增加最小事件审计：
   - 新增计划事件/告警实体，至少记录 `PlanId`、`EventType`、`Severity`、`Message`、`SnapshotPrice`、`OccurredAt`
   - 每次状态迁移必须落一条事件，避免只有状态变化没有审计链路
5. 增加基础量价背离告警，但告警不直接改计划状态：
   - 读取最近 30 分钟分时数据
   - 检测 `PriceSlope > 0 && VolumeSlope < 0` 或同等级确定性背离信号
   - 仅落 `Warning` 级事件，不直接标记 `Invalid`
6. 前端交付方式收敛为短轮询，不在本轮引入 SignalR：
   - 新增计划状态/告警查询接口
   - 复用 `StockInfoTab.vue` 现有定时刷新模式做短轮询
   - 在当前交易计划区块和交易计划总览中显示最新状态/告警摘要

### 约束
1. Step 4.3 只做确定性规则，不要引入 LLM 判定，也不要提前实现 Step 4.4 的语义复核。
2. 当前仓库没有现成 SignalR 基础设施，V1 统一使用“事件持久化 + 前端短轮询”，不要额外扩实时推送栈。
3. 状态迁移必须可追溯，避免计划在没有事件、时间戳和价格快照的情况下静默变化。
4. 任何触发与失效都必须基于明确数值条件，而不是自然语言猜测。
5. 需要保证幂等：同一计划在连续轮询下不能重复写入同类事件，也不能重复触发同一次状态迁移。

### 验收
1. `Pending -> Triggered / Invalid` 的状态迁移正确，且 `Invalid` 优先级明确高于 `Triggered`。
2. 连续轮询下不会重复触发同一计划，也不会重复落同类状态迁移事件。
3. 告警与状态变化可通过短轮询稳定展示到前端，不依赖 SignalR。
4. 至少完成后端定向单测、前端定向单测、frontend build，以及一次后端托管页面的 Browser MCP 告警/状态刷新验证。

### Step 4.4：突发事件的动态定性复核

状态标签：`未开始`

### 目标
对白名单股票的高频突发新闻做辅助语义复核，但只作为告警和复核层，不得替代主执行链路。

### 输入
1. 白名单股票的高频突发新闻。
2. 该股票当前 `Pending` 计划的 `InvalidConditions`。
3. 可控的 Flash / Pro 模型调用能力。

### 输出
1. 结构化新闻复核结果，如 `isPlanThreatened`、`reason`、`confidence`。
2. 高优先级告警或 `ReviewRequired` 状态，而非自动交易动作。

### 核心任务
1. 当高频新闻爬虫捕获到白名单股票的个股突发新闻时，后端抽取该条新闻内容。
2. 将新闻内容与该股票当前 `Pending` 计划的 `InvalidConditions` 一并发送给 `gemini-3.1-flash-lite` 或后续批准的 Pro 模型。
3. 模型输出必须限制为结构化辅助判断，例如：
   - `isPlanThreatened`
   - `reason`
   - `confidence`
4. 若模型判断新闻显著威胁原计划，V1 默认行为应为：
   - 打出高优先级告警
   - 将计划标记为 `ReviewRequired` 或等价状态
   - 等待人类确认，而不是直接自动失效或自动交易

### 约束
1. 禁止在 Step 4.4 中让模型直接输出买卖动作。
2. 禁止让 Step 4.4 覆盖 Step 4.3 的主阻断规则。
3. 若模型不可用或超时，系统必须降级为“不做语义复核，但主规则链路继续工作”。

### 验收
1. 新闻复核结果是结构化且可追踪的。
2. 模型异常不会阻塞主盯盘流程。
3. `ReviewRequired` 的状态和告警展示对用户清晰可见。

### Step 4.5：战术纪律执行板

状态标签：`未开始`

### 目标
将当日计划、触发结果、失效结果和纪律约束以强视觉方式呈现给用户，强化“计划先于冲动”的交易纪律体验。

### 输入
1. `TradingPlan` 的状态流转结果。
2. Step 4.3 / Step 4.4 产生的触发、失效、复核状态与原因。
3. 当前前端看盘终端与侧栏布局约束。

### 输出
1. 专门的《今日计划与纪律执行板》页面或抽屉。
2. 对不同计划状态的强视觉区分。
3. 对 `Invalid` / `ReviewRequired` 等关键状态的明确原因展示。

### 核心任务
1. 建立专门的《今日计划与纪律执行板》页面或抽屉。
2. 使用卡片展示所有交易计划，至少区分：
   - `Pending`
   - `Triggered`
   - `Invalid`
   - `Cancelled`
   - 如后续引入则包括 `ReviewRequired`
3. 视觉要求：
   - `Pending` 使用灰/黄底
   - `Triggered` 使用绿色
   - `Invalid` 必须做强视觉阻断，例如红色横线、红框、红色警示语
4. 对失效计划明确展示原因，例如：
   - 跌破核心均线，禁止操作
   - 触发失效价，风控拒绝
   - 突发利空，等待复核

### 约束
1. 这不是普通列表页，重点是纪律约束感，而不是信息堆砌。
2. `Invalid` 状态必须足够醒目，避免用户忽视。
3. 展示层必须与 Step 4.3 / 4.4 的状态机一致，不能前后端各自定义一套语义。

### 验收
1. 用户能快速识别哪些计划还能观察，哪些已禁止操作。
2. 状态和原因展示完整，不出现“卡片变红但不知道为什么”的情况。
3. 页面在多计划并存时仍可读，且不会压缩主看盘终端的核心区域。

---

## 历史归档摘要（不再作为当前开发清单）

1. Step 1、Step 2、Step 3、Step 4.0 的详细任务书与阶段回执，已完成并归档到 `.automation/reports/`。
2. Step 2.x 期间的大量临时排障说明、旧数据源试错、旧回执内容，不再保留在本文件正文中；如需复盘，请查报告，不要重新写回这里。
3. 以下旧结论已被后续实现或后续纠偏覆盖，因此只保留结果，不再保留原始长篇描述：
   - 10jqka 接口不适合作为当前稳定抓取主源
   - 旧的板块资讯硬匹配方案已淘汰
   - 早期新闻情绪标签的历史争议已被后续 LLM 清洗链路和事实库设计替代
   - Step 4.0 之前的性能问题已完成修复，不再作为当前任务重点

---

## 本次新增维护规则

1. `chatgpt_directives.md` 只保留当前活跃任务、仍生效约束和必要 backlog；已完成阶段必须压缩为归档摘要，详细过程统一下沉到 `.automation/reports/`。
