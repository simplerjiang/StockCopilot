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
1. Dev2 下一步并行任务：在 GOAL-012-R2 收口后立即启动 GOAL-012-R3 图表策略注册表与多策略叠加工程，继续与 Dev1 主线隔离推进。
2. Dev1 当前返工主线已继续推进：Step 4.3 盘中定量规则盯盘引擎的 reviewer blocker 已于 2026-03-14 修复并通过复测；Step 4.4 已于 2026-03-15 完成“突发事件动态定性复核”最小闭环，并在同日补齐 reviewer follow-up 修复后通过复测验收，Step 4.5 继续保留为后续 backlog，不提前扩 scope。
3. GOAL-009-R1 已于 2026-03-15 完成并通过本轮补丁验收。下一开发切片定为 GOAL-009-R2：在现有快照表上补 5/10/20 日持续性、扩散度、排名变化和主线分数，并继续复用 `SectorRotationWorker` / `SectorRotationIngestionService` 做滚动回算；其后再做 GOAL-009-R3，把本地市场阶段上下文接回 `/api/stocks/plans/draft`、`/api/stocks/position-guidance` 与交易计划展示面，但仍保持 Step 4 高频主链为确定性本地规则。
4. Step 4.5：继续保留为后续 backlog，不提前扩 scope。
5. 已完成步骤（Step 4.1 / 4.2）仅保留归档说明与复用约束，不再按“进行中”处理。

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

## Dev2 并行任务：图表终端升级归档与下一步策略工程

### 已归档完成：GOAL-012-R2 `klinecharts` 受控替换试验

状态标签：`已完成（归档基线）`

1. Dev2 已完成 `klinecharts` 底层替换，父层 contract 维持兼容，当前生产路径继续沿用 `frontend/src/modules/stocks/charting/**` 适配层。
2. 已建立现成扩展边界：
   - `useStockChartAdapter.js` 作为父层入口
   - `klinechartsRegistry.js` 作为 indicator / overlay / marker 注册点
   - `StockCharts.vue` 已具备按视图切换与按钮式 feature toggle UI
3. R2 之后的所有策略能力，一律在现有 registry 边界上扩展，禁止重新把计算和渲染逻辑硬编码回 `StockCharts.vue`。

### Dev2 下一步任务：GOAL-012-R3 图表策略注册表与多策略叠加工程

状态标签：`待开发（R2 收口后立即开始）`

#### 目标
在不干扰 Dev1 Step 4.3 主线的前提下，把当前 `klinecharts` 终端升级为“可注册、可开关、可按视图适配”的策略图层系统。目标不是只补一个 KDJ 或九转，而是一次性建立后续所有技术策略的统一接入协议、统一按钮交互和统一渲染出口。

#### 输入
1. 当前图表终端基线：
   - `frontend/src/modules/stocks/StockCharts.vue`
   - `frontend/src/modules/stocks/charting/chartViews.js`
   - `frontend/src/modules/stocks/charting/useStockChartAdapter.js`
   - `frontend/src/modules/stocks/charting/klinechartsRegistry.js`
2. 当前已存在能力：
   - `minute/day/month/year` 单主图视图切换
   - MA / VOL / AI 价位线 / base line 的 registry 驱动能力
   - `featureVisibilityByView` + `toggleFeature(...)` 的按钮交互模型
3. 用户已确认需求：
   - 分时图、日 K 图要支持更多策略
   - 接口必须可扩展
   - 点击按钮即可切换显示策略
   - 已知策略需要全部进入计划，不再只讨论个别指标

#### 输出
1. 一个统一的 chart strategy registry contract。
2. 一组按类别组织的按钮/芯片式策略开关 UI。
3. 第一批可运行的策略叠加实现。
4. 一份按阶段推进的策略目录，允许后续继续扩展而不改父层 contract。
5. 对应前端单测、build 与浏览器交互验证。

#### 与 Dev1 的隔离边界
1. Dev2 只允许改：
   - `frontend/src/modules/stocks/StockCharts.vue`
   - `frontend/src/modules/stocks/charting/**`
   - `frontend/package.json`
   - 必要的前端测试文件
2. Dev2 禁止改：
   - Step 4.3 的后端触发/告警服务
   - `TradingPlan` 实体/API 与计划状态机
   - `StockAgentAnalysisHistory` 与 agent contract
3. 若确需触碰 `StockInfoTab.vue`，只能做兼容式挂载调整，禁止把策略逻辑上推到页面层。

#### 统一策略接口要求
1. 所有策略必须注册到统一 registry，不允许在 `StockCharts.vue` 内写分散的 if/else 分支。
2. 每个策略定义至少包含：
   - `id`
   - `label`
   - `category`
   - `kind`：`overlay` / `indicator` / `marker` / `signal`
   - `supportedViews`：如 `minute` / `day` / `month` / `year`
   - `defaultVisible`
   - `requires`：所需数据列，如 `price` / `volume` / `basePrice`
   - `compute(context)`：返回标准化渲染结果
3. `compute(context)` 的输出必须落到统一结果结构，例如：
   - `indicators`
   - `overlays`
   - `markers`
   - `signals`
   - `legend`
4. 策略计算与渲染必须解耦：
   - strategy 文件只负责计算标准化结果
   - registry 负责注册与启停
   - `klinechartsRegistry.js` 负责把标准化结果映射到具体图表 API

#### 策略目录（全部纳入计划）
1. 趋势/均线类：
   - MA5 / MA10 / MA20 / MA60
   - VWAP / 分时均价线
   - BOLL
   - Donchian Channel
2. 动量/摆动类：
   - MACD
   - KDJ
   - RSI
   - ATR
3. 事件/信号标记类：
   - 神奇九转 / TD Sequential
   - MA 金叉 / 死叉
   - MACD 金叉 / 死叉
   - KDJ 金叉 / 死叉
   - 放量突破 / 缩量假突破
   - 缺口高开 / 低开 / 回补缺口
   - 量价背离
   - 分时 VWAP 回踩企稳 / 跌破转弱
   - 开盘区间突破（ORB）
4. 已存在能力也要并入同一体系：
   - AI 支撑线 / 突破线
   - 分时昨收基准线
   - 成交量副图

#### 视图适配原则
1. 不是所有策略都在所有视图展示：
   - `minute` 优先：VWAP、分时量价背离、ORB、分时金叉类
   - `day` 优先：MA、BOLL、MACD、KDJ、RSI、九转、突破类
   - `month/year` 先保留中长周期兼容指标：MA、BOLL、MACD、RSI、Donchian
2. UI 只显示当前视图可用的策略按钮；不支持的策略不要渲染成不可用垃圾按钮。

#### 实施阶段
1. Phase A：策略注册表底座
   - 抽出 `strategyRegistry` / `strategyDefinitions`
   - 统一 feature state、button config、view gating
   - 补 strategy compute contract 单测
2. Phase B：基础指标落地
   - MA 扩展为 5/10/20/60
   - VWAP / base line 规范化
   - BOLL、MACD、KDJ、RSI、ATR、Donchian
3. Phase C：信号标记层
   - 九转
   - 金叉/死叉
   - 放量突破 / 假突破
   - 缺口与量价背离
   - ORB 与 VWAP 强弱信号
4. Phase D：交互与验收
   - 按分类展示按钮/芯片
   - 支持单独开关和默认组合
   - 保证切换 `minute/day/month/year` 时状态与图层同步

#### 测试与验收
1. 单测必须覆盖：
   - registry 对不同 view 的可用策略过滤
   - 按钮点击后的 active/inactive 状态
   - 关键策略的计算结果排序与映射正确
   - 九转 / 金叉 / 突破类 marker 不重复落点
2. 必须执行：
   - 前端定向单测
   - `npm --prefix frontend run build`
   - 后端托管页面 Browser MCP 验证
3. Browser MCP 不允许只看静态渲染：
   - 至少切换 `分时图 / 日K图 / 月K图 / 年K图`
   - 至少点击多类策略按钮并等待图层变化
   - 检查前端 console 与后端日志无新错误

#### 约束
1. 本任务只做前端图表策略层，不把策略结论回写交易计划，不与 Step 4.3 状态机耦合。
2. 除既有 AI 价位线外，新增策略优先使用前端已有 `minuteLines` / `kLines` 数据计算；需要新后端数据时必须单独立项，不得隐式扩 scope。
3. 所有策略必须先走统一 registry，再走统一 renderer，避免后续增加第 11、12 个策略时继续堆分支。
4. 若某些高级策略在当前数据条件下无法可靠落地，允许在 R3 中先完成接口与按钮占位，但必须在回执里写清楚数据前提与未实现原因。

---

## 后续 backlog（当前不要提前做，但保留完整任务定义）

### Step 4.3：盘中定量规则盯盘引擎

状态标签：`已完成（返工收口）`

### 当前说明
Step 4.3 的 reviewer blocker 已于 2026-03-14 收口完成。本节保留为归档基线与复用约束：后续如无新的 reviewer 缺陷，不要再把 Step 4.3 退回“进行中”处理。

### 本轮返工结论
1. 触发引擎现已以 `ActiveWatchlist` 作为执行边界，只处理白名单内且本地缓存可用的 `Pending` 计划。
2. 量价背离 warning 已改为基于持续条件时间窗的去重，不再依赖原始 `MetadataJson` 精确字符串相等。
3. 前端短轮询已同时覆盖“当前交易计划”和“交易计划总览”，无须依赖 SignalR。

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
2. 触发引擎只处理 `ActiveWatchlist` 白名单内计划，不允许绕开白名单全表扫 `Pending` 计划。
3. 连续轮询下不会重复触发同一计划，也不会对同一持续背离/同一类 warning 反复落事件。
4. 告警与状态变化可通过短轮询稳定展示到“当前计划区 + 交易计划总览”，不依赖 SignalR。
5. 已重新完成后端定向单测、前端定向单测、frontend build、`TradingPlanEvents` SQLCMD 校验，以及后端托管页面刷新验证。

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
