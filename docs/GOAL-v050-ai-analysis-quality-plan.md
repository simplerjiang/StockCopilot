# GOAL v0.5.0 AI 分析质量增强计划（数据韧性 + 回测 MVP 版）

Status: Draft / Discussion Only

Scope note: 本文档仅用于 PM, Dev Agent, Test Agent, UI Agent 讨论 v0.5.0 方向。暂不进入 `.automation/sprint.md`,不代表已经排期,不修改当前 v0.4.8 Sprint。

## 0. 与上一版的差异

上一版强调"证据绑定 + 降级门禁"。用户反馈两个关键修正:

1. **不接受"数据降级"作为首选项。** 数据是命脉,请求不到第一反应必须是"多次重试 + 换源 + 缓存 + 替代字段",不是给前端一句"source unavailable"。
2. **要让系统具备真正的回测能力。** 只加门禁等于让模型更谨慎,但回答不到"它过去到底准不准"。回测才是把 AI 投资分析从"看起来像研报"推到"被历史数据检验过"的关键。

因此本版把主线改为两条:

- 主线 A:**数据韧性 (Data Resilience)** —— 把多源/多次重试/缓存/补采变成默认行为,degraded 只能是兜底标签,不是工作流出口。
- 主线 B:**AI 回测 MVP (Decision Backtest)** —— 让 Research / Recommend / LiveGate 的每个最终结论都被记录成可回测信号,并用 A 股真实交易规则验证它历史表现如何。

旧版的"证据绑定 / A 股口径红线 / 可复算估值"被压缩为辅线 C,作为回测的输入质量保障。

## 1. 当前仓库已有的地基

确认这些机制存在,新计划必须复用,不重建:

- [`CompositeStockCrawler.TryGetAsync`](../backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/CompositeStockCrawler.cs):行情/K线已有"一个源失败试下一个源"的多源逻辑。
- [`ResearchRoleExecutor`](../backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/ResearchRoleExecutor.cs):`LlmRetryDelaysMs` / `ToolRetryDelaysMs` 已存在,工具调用按角色重试。
- [`StockAgentReplayCalibrationService`](../backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/StockAgentReplayCalibrationService.cs):已有 1/3/5/10 日方向命中率回放。
- [`TradeAccountingService`](../backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/TradeAccountingService.cs):已有真实交易盈亏、胜率、平均收益统计。
- [`/api/stocks/agents/signal-track-record`](../backend/SimplerJiangAiAgent.Api/Modules/Stocks/StocksModule.cs):已有"信号历史命中率 + 用户实盘胜率"双线接口。
- [`StockAgentReplayBaselineDto / StockAgentReplaySampleDto`](../backend/SimplerJiangAiAgent.Api/Modules/Stocks/Models/StockAgentRuntimeModels.cs):已有 horizon/sample DTO。
- [`EvidencePackBuilder`](../backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/IntentClassification/EvidencePackBuilder.cs):已聚合 RAG / FinancialReport / LocalFact。
- [`RagContextEnricher`](../backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/RagContextEnricher.cs):已有 hybrid 检索,但失败会静默返空。
- [`docs/GOAL-018-trading-discipline-closed-loop.md`](GOAL-018-trading-discipline-closed-loop.md):已规划信号/实盘双线胜率。
- [`docs/GOAL-NEW-FEATURES-competitive-plan.md`](GOAL-NEW-FEATURES-competitive-plan.md):已提过 BacktestResult 早期草案。

结论:v0.5.0 不是从零做回测,而是把已有零件升级为"真正能用历史行情评判 AI 判断质量"的回路。

## 2. 设计原则

1. **数据韧性优先于降级标签。** 任何数据请求至少经过:本源重试 → 换源 → 历史缓存 → 替代字段 → 补采队列;只有全部失败才允许 degraded,且 degraded 必须带"已尝试源 + 错误原因 + 下次补采时间"。
2. **回测是系统的"反馈回路",不是事后报告。** 每个 AI 最终结论必须沉淀为结构化 DecisionSignal;回测结果反哺 prompt、置信度、Agent 权重。
3. **A 股交易规则是回测的硬约束。** T+1、涨跌停不可成交、停牌、手数、佣金印花税、滑点、ST/创业板/科创板/北交所差异、前复权口径,必须在 MVP 里就有。
4. **小改动大效果。** 不重写多 Agent 编排,不引入新框架;改动落点是数据层的 resilience 包装、新增 1 个 Backtest 模块、prompt 输出加几个薄字段。
5. **诚实优先。** 重试失败、缓存陈旧、回测样本不足,都必须在面向用户的回答中如实出现,不允许"看起来很完整"掩盖问题。

## 3. 主线 A:数据韧性

### A1 多源重试编排器

把现有 `CompositeStockCrawler.TryGetAsync` 升级为通用 `IDataResiliencePolicy`:

| 阶段 | 行为 |
|---|---|
| 1. 同源重试 | 短间隔重试(200ms / 800ms / 2s),处理瞬时 5xx / TLS / DNS 抖动 |
| 2. 换源 | 按预设源序列轮询:行情 = 东方财富 → 新浪 → 腾讯 → 百度 → 已缓存最新;财报 = 东财 → 巨潮 → 同花顺;公告 PDF = cninfo → 东财 |
| 3. 历史缓存 | 命中 LiteDB / SQLite 中最近一次成功结果,标 `staleSeconds` 让上层决定是否可用 |
| 4. 替代字段 | 例如 PE 拿不到时,用 EPS×PEBand / 同行均值给出范围值,并标 `derived=true` |
| 5. 补采入队 | 失败的 (source, symbol, field) 写入补采队列,由 Worker 以更长退避继续尝试,下次访问可命中 |

要点:

- 该策略**默认开启**于行情、K线、财报指标、公告 PDF、新闻。
- 保留 `traceId`,每一阶段记录到 `DataFetchAttempt` 表,方便回放"为什么这次拿到的是缓存而不是实时"。
- LLM 看到的上下文必须知道:本数据点来自源 X,第几次尝试,是否 stale,是否 derived。这比简单 degraded 标签信息密度高得多。

### A2 取消"静默返空"

逐项排查目前会静默返空的代码点(已知至少:[`RagContextEnricher`](../backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/RagContextEnricher.cs)、[`EastmoneyAnnouncementParser`](../backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/EastmoneyAnnouncementParser.cs) 等返回 `Array.Empty<>` 的位置)。统一改为:

- 抛出可识别的 `DataFetchException(source, reason)`,由 A1 策略捕获并进入下一阶段;
- 真正全部失败时返回 `DataFetchResult { Items, Status, Attempts, StaleSeconds, DerivedFlag }`,而不是空列表。

### A3 RAG hybrid 退化的根因修复

现状:bug #69 提到 hybrid 检索经常退化为 bm25。这是数据韧性范畴内最该解决的"假完整":retrieval 看起来返回了结果,但向量通道挂掉。修复方向:

- 嵌入服务请求加 A1 同款重试 + 换源(Ollama bge-m3 → 内部缓存向量 → 关键词 BM25 兜底,**且必须在响应里如实写明用的是哪条**)。
- hybrid 模式失败必须先做向量重试,不能首次失败就静默切到 bm25。
- 召回数据必须带 `retrievalMode` 字段,下游 prompt 要能感知"我现在拿到的是不是真正的语义检索"。

### A4 数据新鲜度仪表盘(最小版)

新增 `/api/stocks/health/data-freshness`:

- 行情、财报、公告、RAG embedding、新闻爬虫的最新成功时间和最近 24h 成功率。
- UI 在分析页面顶部显示一行 chip:`行情<2s · 财报 12h 前 · 公告 3 小时前 · RAG 完整`,让用户随时看到数据状态。
- 这条信息也写入 prompt context,让 LLM 不用瞎猜数据有多新。

## 4. 主线 B:AI 回测 MVP

### B1 DecisionSignal 抽取与落库

每次 Research / Recommend / LiveGate 给出最终结论时,自动从输出抽出标准化信号入库:

| 字段 | 含义 |
|---|---|
| pipeline | research / recommend / livegate / stockAgent |
| sessionId, turnId, traceId | 关联原始结论 |
| symbol | 已归一(sh600519 / sz000001 / bj430510) |
| asOfDate | 信号生效日期,**严禁使用未来数据** |
| asOfPrice | 当时收盘价或最新价 |
| action | strong_buy / buy / watch / hold / reduce / sell / strong_sell / insufficient |
| confidence | 0~1 |
| targetPrice / stopLoss / takeProfit | 可空 |
| timeHorizon | days |
| thesis | 简短论点摘要 |
| evidenceIds | 关联到 RAG/财报/新闻的 id 列表 |
| dataFreshness | 来自 A4 |
| backtestEligible | 是否符合回测条件 |

落库到 `DecisionSignals` 表。整套回测体系完全以这张表为入口,跟 LLM 文本解耦。

### B2 A 股日线回测引擎

最小可用引擎,只做日线级,不做盘中:

- **入场**:信号日 (asOfDate) 收盘后产生,按 T+1 次日开盘价撮合;若次日开盘涨停(>=9.95% 主板,20% 创业板/科创板,30% 北交所,ST 5%),标 `missed_limit_up`。
- **出场**:按优先级判断
  1. 触发止损(次日及以后任意 K 线 low <= stopLoss → 当日收盘价或止损价较差者);
  2. 触发止盈(high >= targetPrice → 目标价);
  3. 持有到 timeHorizon 截止 → 收盘价;
  4. 停牌期间不可成交,自动顺延。
- **成本**:佣金万 2.5(最低 5 元)+ 印花税卖出千 1 + 滑点 0.1%(可配)。
- **复权**:用前复权 K 线,避免分红除权造成的"假止损"。
- **指标输出**:实际收益率、年化、最大回撤、持有天数、是否跑赢沪深300/中证500/所属行业指数、目标价是否触达、止损是否触发。
- **样本不足**:单股票样本 < 5 时,必须返回 `low_sample` 标志,不允许给出胜率以免误导。

### B3 三类回测口径

| 口径 | 衡量什么 | 用法 |
|---|---|---|
| 信号回测 | action 是否带来正收益、跑赢基准 | 衡量 Agent 选股能力 |
| 目标价回测 | targetPrice 是否在 timeHorizon 内触达 | 衡量估值/技术目标的真实性 |
| 风险提示回测 | thesis 中标记的"关键风险"是否真的在 timeHorizon 内导致下跌 / 高波动 / 触止损 | 衡量风险识别能力 |

每条 DecisionSignal 在到期后自动跑这三套口径,写入 `BacktestResults`。

### B4 回测反哺 prompt 与置信度

回测不是只做报告。结果必须回流:

- 给每个 Agent / pipeline 维护滚动 90 天 / 30 笔最小样本的 `realizedAccuracy`、`realizedReturn`、`realizedMaxDD`、`brierScore`。
- prompt 注入"你过去 30 笔类似信号的实际表现",让 LLM 在自评 confidence 时被锚定。
- Commander/Director 阶段如果某 Agent 长期 brierScore 差,**自动降权**(先做日志可见,后续再做硬权重)。
- 行业分组:白酒 / 银行 / 周期 / 科技 / 北交所 各自维护一组校准曲线,不混用。

### B5 用户端入口

最小 UI(不新增页面):

- 个股页:在 AI 结论旁边加一行 "该 pipeline 过去 30 笔同类信号 5 日胜率 X%,平均收益 Y%,目标价触达率 Z%"。样本不足显示"样本不足,仅供参考"。
- Recommend 卡片:每条 pick 显示历史目标价触达率 + 对应行业基准胜率。
- LiveGate 答复:当用户问"这只股票该不该买",答复末尾必须附带 pipeline 的历史校准数据。

## 5. 辅线 C:证据与 A 股口径(继承上一版,但弱化)

只保留对回测必要的最小集合:

- C1 主张级 evidenceIds(让回测能定位"当时是基于哪条证据下的判断")。
- C2 A 股口径红线(标的归一、单位、Q1/YTD/TTM、指数 vs 个股) —— 是回测样本干净的前提。
- C3 targetPrice / stopLoss 必须可计算,否则不计入 backtestEligible。
- C4 风险矩阵 / 反证清单 暂列 P2,可在 v0.5.1 再做。

注意:上一版的"数据缺失就降级为 watch"被替换为 "数据韧性主线 A 用尽所有手段后仍缺失,才标 insufficient",且必须写明已尝试哪些源。

## 6. 取舍声明

明确不做:

- 不做盘中分钟级回测(成本高、收益低)。
- 不做组合层 portfolio 回测(先把单信号回测做扎实)。
- 不做策略参数搜索 / 网格优化(避免过拟合幻觉)。
- 不引入新 Python 量化框架(用现有 .NET + SQLite 即可,沿用项目栈)。

明确要做:

- 必须实现 T+1、涨跌停、停牌、复权、ST 规则。
- 必须有"样本不足"显式标志,禁止用 3 个样本算胜率。
- 必须把回测结果对外暴露给用户和 LLM,否则等于没做。

## 7. 建议 Sprint 拆分

仅供讨论,尚未写入 `.automation/sprint.md`。

| Sprint | 标题 | 类型 | 核心交付 |
|---|---|---|---|
| S1 | 数据韧性策略 + 取消静默返空 | M | `IDataResiliencePolicy`,行情/财报/公告/RAG 接入;`DataFetchAttempt` 落库 |
| S2 | RAG hybrid 根因修复 + 数据新鲜度仪表盘 | M | hybrid 不再静默退化;`/health/data-freshness` 与 UI chip |
| S3 | DecisionSignal 抽取与落库 | M | Research / Recommend / LiveGate 终态信号入 `DecisionSignals` |
| S4 | A 股日线回测引擎 + 三类口径 | L | `BacktestEngine`,T+1/涨跌停/复权/成本;`BacktestResults` |
| S5 | 校准回流 + UI 历史表现展示 | M | prompt 注入历史表现;个股/推荐/LiveGate UI 显示 pipeline 校准 |

依赖关系:S1/S2 → S3 → S4 → S5。S1 与 S3 可并行启动,但 S4 必须等 S3 出数据。

## 8. 验收标准

数据韧性:

- 断网/断单源压测下,行情/财报/公告至少一条可用源时不返回空;全部失败时返回的 envelope 含已尝试源清单。
- RAG hybrid 在向量服务挂掉时先重试至少 2 次,再退化到 BM25,且响应里 `retrievalMode` 真实反映。
- 数据新鲜度 chip 在 UI 上能正确显示并随时间更新。

回测正确性:

- 给定贵州茅台 sh600519 历史 N 条信号,T+1 撮合、涨跌停规则、复权口径与手工核对一致(误差 <0.1%)。
- 给定平安银行 sz000001 上一段下跌行情,止损/到期出场逻辑正确触发。
- 北交所 / ST / 科创板各 1 个样本,涨跌停阈值正确生效。
- 样本 < 5 时不显示胜率。

反哺闭环:

- prompt context 中能看到 pipeline 的历史校准(最近 90 天/30 笔)。
- UI 个股页显示历史胜率 + 平均收益 + 目标价触达率。
- Recommend 输出的每个 pick 旁有历史校准信息。

## 9. 给各 Agent 的提示

Dev Agent:

- 优先在数据层加包装,不修改 LLM 编排。
- BacktestEngine 必须用纯函数 + 单元测试覆盖 T+1 / 涨跌停 / 复权 / 止损止盈优先级 / 停牌顺延。
- DecisionSignal 抽取要尽量从已有 commander / director 输出中解析,不要新增 LLM 调用。

Test Agent:

- 必须用真实历史 K 线做回归(仓库已有 KLinePoints 入库)。
- 必须验证"断单源"场景,行情仍能从备源拿到。
- 必须验证"样本不足不出胜率"这条硬规则。

UI Agent:

- 数据新鲜度 chip、历史校准条都不新增页面,原位嵌入。
- 历史胜率显示样式需给两套:样本足 / 样本不足。

PM Agent:

- 讨论确认后再把 S1-S5 写进 `.automation/sprint.md`。
- L 级(S4)必须走 Dev → Test → UI Designer → User Rep 流程。
- 验收必须按规则 6(数据正确性必须交叉验证)落地,对接外部 K 线源对比抽样。
