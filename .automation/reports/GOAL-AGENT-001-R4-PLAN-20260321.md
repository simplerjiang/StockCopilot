# GOAL-AGENT-001-R4 Planning Report (2026-03-21)

## EN
### Summary
Planned the next Stock Copilot infrastructure slice after GOAL-AGENT-001 R1/R2/R3: a Copilot-style MCP tool layer dedicated to market charts, minute-line workflows, indicator strategies, news evidence, and gated external search.

The purpose is not to add another report generator. The purpose is to give the future stock copilot a small, explicit, auditable action space similar to a coding copilot:
1. ask for the right market data,
2. ask for the right strategy features,
3. ask for the right news evidence,
4. use external search only when local evidence is still insufficient.

### Why This Slice Exists
The current GOAL-AGENT-001 R1/R2/R3 planning already defines evidence traceability, role narrowing, and replay calibration. What is still missing is the actual tool runtime layer that a Copilot-like stock assistant can call.

Without this layer, the system still depends too heavily on prompt stuffing and preset orchestration. With this layer, the future agent can behave more like a bounded tool-using copilot:
1. inspect K-line structure on demand,
2. inspect minute-line intraday structure on demand,
3. ask deterministic strategy engines for signals such as TD Sequential, RSI, KDJ, MACD, VWAP strength, divergence, and breakout state,
4. fetch local-first news evidence with article-read status,
5. use Tavily or other external search only as a policy-gated fallback.

### Scope of R4
R4 is the Stock Copilot MCP tool runtime foundation.

It covers five MCP families:
1. `StockKlineMcp`
2. `StockMinuteMcp`
3. `StockStrategyMcp`
4. `StockNewsMcp`
5. `StockSearchMcp` (Tavily-gated fallback)

These are domain MCPs for the future planner/governor/runtime layer inside GOAL-AGENT-001, not public protocols yet.

### Design Goals
1. Local-First remains mandatory for CN-A stock facts.
2. One MCP should solve one bounded domain question.
3. Tool outputs must be machine-usable by planner, governor, commander, replay, and developer audit.
4. Deterministic chart/indicator math must come from code, not from LLM arithmetic.
5. External search must be explicitly gated and observable.
6. Every MCP should produce enough metadata for stop policy and degraded-path control.

### R4 Tool Breakdown
#### 1. StockKlineMcp
Purpose:
Serve structured K-line windows and derived chart context for day/week/month/year views.

Primary actions:
1. `GetKlineWindow`
2. `GetKlineKeyLevels`
3. `GetKlinePatternContext`
4. `GetKlineMultiTimeframeSummary`

Minimum outputs:
1. requested symbol, interval, window size
2. normalized OHLCV bars
3. latest trend anchors and support/resistance candidates
4. data freshness, source, degraded flags

Implementation notes:
1. Reuse current `/api/stocks/chart` and K-line adapter path where possible.
2. Do not force the LLM to read raw bar arrays longer than needed; prefer summarized deterministic features plus a bounded raw window.
3. Use the existing chart registry outputs when strategy overlays already exist in code.

#### 2. StockMinuteMcp
Purpose:
Serve intraday minute-line structure and session-aware execution context.

Primary actions:
1. `GetMinuteWindow`
2. `GetMinuteSessionProfile`
3. `GetMinuteFlowContext`
4. `GetMinuteExecutionRisk`

Minimum outputs:
1. bounded minute-line window
2. VWAP / intraday range / opening drive / afternoon drift style features
3. session phase and timestamp coverage
4. degraded flags when minute data is stale or missing

Implementation notes:
1. Must align with China A-share session semantics.
2. Should expose compact deterministic intraday features instead of giant raw arrays whenever possible.
3. Must distinguish `盘前 / 盘中 / 盘后` and partial-session data quality.

#### 3. StockStrategyMcp
Purpose:
Expose deterministic strategy engines for K-line and minute-line signals as bounded tool calls instead of embedding all strategy logic into prompts.

Initial strategy families:
1. TD Sequential / 九转
2. RSI
3. KDJ
4. MACD
5. MA crossovers
6. VWAP strength
7. divergence / breakout / false breakout / gap

Primary actions:
1. `EvaluateStrategies`
2. `GetStrategySignals`
3. `GetStrategyEvidence`
4. `ExplainStrategyState`

Minimum outputs:
1. normalized signal list
2. signal strength / confidence source / timeframe / bar index
3. view gating (`minute_only | kline_only | multi_view`)
4. underlying deterministic features used to produce the signal

Implementation notes:
1. This MCP should wrap the existing chart strategy registry instead of re-implementing signal logic from scratch.
2. Every returned signal must map back to a deterministic code path.
3. The runtime should allow requesting a small subset of strategies, not always all strategies.

#### 4. StockNewsMcp
Purpose:
Provide local-first news and announcement evidence for stock/sector/market tasks.

Primary actions:
1. `GetStockNewsEvidence`
2. `GetSectorNewsEvidence`
3. `GetMarketNewsEvidence`
4. `ReadArticleEvidence`
5. `GetAnnouncementEvidence`

Minimum outputs:
1. evidence objects from GOAL-AGENT-001-R1
2. readMode / readStatus / trustTier / relevanceScore
3. local source tag and ingestion timestamp
4. missing-evidence reasons when only metadata is available

Implementation notes:
1. This MCP is the default news path for Copilot-like workflows.
2. It must prefer local facts, local summaries, and backend article reads.
3. It must not silently mix local evidence and external search results under one unlabeled list.

#### 5. StockSearchMcp
Purpose:
Provide external web search as a gated fallback when local evidence is insufficient.

Primary actions:
1. `SearchExternalEvidence`
2. `SearchTrustedSourcesOnly`
3. `FetchSearchResultPage`
4. `SummarizeExternalResult`

Planned provider:
1. Tavily as the first search backend.

Policy constraints:
1. external-only, never first-line for CN-A facts
2. require planner insufficiency signal plus governor approval
3. require allowlist / trust-tier filtering before commander can use the result for high-confidence output
4. all external results must be normalized back into the same evidence schema

### Runtime / Governance Plan
R4 is not just five endpoints. It also needs a shared MCP runtime contract.

Required shared fields on every MCP response:
1. `traceId`
2. `taskId`
3. `toolName`
4. `latencyMs`
5. `cache.hit/source/generatedAt`
6. `warnings[]`
7. `degradedFlags[]`
8. `data`
9. `evidence[]`
10. `features[]`
11. `meta.version`

Required policy classes:
1. `local_required`
2. `local_preferred`
3. `external_gated`
4. `workflow_mutating`
5. `developer_only`

Planned tool-class mapping:
1. `StockKlineMcp` -> `local_required`
2. `StockMinuteMcp` -> `local_required`
3. `StockStrategyMcp` -> `local_required`
4. `StockNewsMcp` -> `local_required`
5. `StockSearchMcp` -> `external_gated`

### Recommended Delivery Order
1. R4.1 Runtime envelope + governor hooks
2. R4.2 `StockKlineMcp` + `StockMinuteMcp`
3. R4.3 `StockStrategyMcp` over existing chart registry and signal engines
4. R4.4 `StockNewsMcp` over local facts + article-read pipeline
5. R4.5 `StockSearchMcp` with Tavily gating and evidence normalization

### Acceptance Criteria
1. The future planner can call chart/news/search capabilities through bounded MCPs rather than raw prompt stuffing.
2. Strategy results are deterministic and traceable to code, not to free-form LLM math.
3. News evidence remains Local-First and uses the unified evidence object.
4. Tavily search is available but clearly gated, observable, and downgraded by policy when needed.
5. Developer traces can show which MCP was called, why, with what degraded flags, and what evidence/features it returned.

### Validation For This Planning Round
1. Update task ledger and active planning state.
2. Validate `.automation/tasks.json` and `.automation/state.json` as JSON.
3. Run diagnostics on changed planning files.

## ZH
### 摘要
本轮把 GOAL-AGENT-001 的下一段能力层单独规划成 `R4`：不是继续堆提示词，而是为未来“像 Copilot 一样会按需调用工具”的股票助手补齐一层真正可执行、可审计、可降级的 MCP 工具运行时。

R4 的核心不是再做一个研报生成器，而是把下面 5 类能力变成可被 planner / governor / commander 统一调用的领域工具：
1. 股票 K 线 MCP
2. 股票分时图 MCP
3. K 线/分时策略 MCP（九转、RSI、KDJ、MACD、VWAP 等）
4. 新闻证据 MCP
5. 搜索 MCP（Tavily 作为受控外部兜底）

### 为什么现在要补这层
GOAL-AGENT-001 之前的 R1/R2/R3 已经把“证据对象、职责收口、回放校准”定义出来了，但未来如果想把多 Agent 改造成更像 Copilot 的交互方式，仍然缺一层真正的工具运行时。

没有这层，系统还是主要依赖：
1. 大 prompt 填充上下文，
2. 固定编排，
3. 模型自己推断下一步要看什么。

补上这层以后，未来的股票 Copilot 才能像编码 Copilot 一样：
1. 按需取 K 线窗口，
2. 按需取分时结构，
3. 按需向策略引擎要信号，
4. 按需读取本地新闻证据，
5. 只有本地证据不足时才向外部搜索兜底。

### R4 范围
R4 是 GOAL-AGENT-001 的“Copilot 风格 MCP 工具层”基础设施。

本轮规划的 5 个 MCP 家族：
1. `StockKlineMcp`
2. `StockMinuteMcp`
3. `StockStrategyMcp`
4. `StockNewsMcp`
5. `StockSearchMcp`

这几个都是面向后续 GOAL-AGENT-001 planner/governor/runtime 的内部领域 MCP，不是现在就对外发布的公共协议。

### 设计目标
1. 继续坚持 CN-A Local-First。
2. 一个 MCP 只回答一个边界明确的问题。
3. 所有输出都要能被 planner、governor、commander、replay、开发者审计复用。
4. 图表/指标计算必须来自代码，不能交给 LLM 心算。
5. 外部搜索只能是显式受控兜底。
6. 每个 MCP 都要返回足够的元信息，支持 stop policy 和 degraded-path 控制。

### 五类 MCP 规划
#### 1. StockKlineMcp
用途：
给未来 Copilot 提供结构化 K 线窗口、多周期图形上下文和关键位摘要。

核心动作：
1. `GetKlineWindow`
2. `GetKlineKeyLevels`
3. `GetKlinePatternContext`
4. `GetKlineMultiTimeframeSummary`

最低输出：
1. 请求的 symbol / interval / window
2. 规范化 OHLCV bars
3. 当前趋势锚点与支撑/阻力候选
4. 数据新鲜度、来源、降级标记

实现原则：
1. 尽量复用现有 `/api/stocks/chart` 和 K 线适配层。
2. 不把超长 bar 数组直接塞给模型，优先输出确定性摘要特征 + 有界原始窗口。
3. 如果现有图表 registry 已有相关输出，就直接包装而不是重算。

#### 2. StockMinuteMcp
用途：
提供盘中分时结构、日内执行节奏和 session-aware 语义。

核心动作：
1. `GetMinuteWindow`
2. `GetMinuteSessionProfile`
3. `GetMinuteFlowContext`
4. `GetMinuteExecutionRisk`

最低输出：
1. 有界分时窗口
2. VWAP / 开盘驱动 / 午后漂移 / 区间结构等确定性特征
3. 当前 session phase 与覆盖情况
4. 分时数据陈旧或缺失时的 degraded flags

实现原则：
1. 必须对齐中国 A 股交易时段语义。
2. 优先暴露紧凑特征，不默认暴露巨大原始数组。
3. 必须明确区分 `盘前 / 盘中 / 盘后` 和半日/缺口数据质量。

#### 3. StockStrategyMcp
用途：
把现有 K 线/分时策略引擎包装成可调用 MCP，而不是把策略判断继续塞进 prompt。

首批策略：
1. TD Sequential / 九转
2. RSI
3. KDJ
4. MACD
5. MA 金叉死叉
6. VWAP 强弱
7. 背离 / 突破 / 假突破 / 缺口

核心动作：
1. `EvaluateStrategies`
2. `GetStrategySignals`
3. `GetStrategyEvidence`
4. `ExplainStrategyState`

最低输出：
1. 标准化信号列表
2. 信号强度 / 时间周期 / bar index / 置信来源
3. view gating（仅分时 / 仅 K 线 / 多视图）
4. 支撑这些信号的确定性 features

实现原则：
1. 包装现有 chart strategy registry，不重写一套新策略。
2. 每个信号都必须能回溯到明确代码路径。
3. 允许运行时只请求部分策略，不要求每次全量计算。

#### 4. StockNewsMcp
用途：
为个股/板块/大盘任务提供 Local-First 的新闻和公告证据层。

核心动作：
1. `GetStockNewsEvidence`
2. `GetSectorNewsEvidence`
3. `GetMarketNewsEvidence`
4. `ReadArticleEvidence`
5. `GetAnnouncementEvidence`

最低输出：
1. GOAL-AGENT-001-R1 定义的 evidence object
2. `readMode / readStatus / trustTier / relevanceScore`
3. 本地 sourceTag 与 ingestion 时间
4. 只有 metadata 时的缺失原因

实现原则：
1. 这是未来 Copilot 的默认新闻链路。
2. 优先本地事实、本地摘要、后端正文读取。
3. 不允许把本地证据和外部搜索结果默默混成一份无标签列表。

#### 5. StockSearchMcp
用途：
在本地证据仍不足时，提供受控外部搜索兜底。

核心动作：
1. `SearchExternalEvidence`
2. `SearchTrustedSourcesOnly`
3. `FetchSearchResultPage`
4. `SummarizeExternalResult`

计划外部搜索后端：
1. Tavily 作为第一搜索 provider。

策略约束：
1. 只能作为 external fallback，不能反客为主
2. 必须先有 planner 的“信息不足”信号，再由 governor 放行
3. 进入 commander 的高置信输出前，必须做 allowlist / trust-tier 过滤
4. 所有外部结果都必须回写成统一 evidence schema

### 运行时与治理约束
R4 不只是 5 个端点，而是一层共享 MCP 运行时 contract。

每个 MCP 都必须返回：
1. `traceId`
2. `taskId`
3. `toolName`
4. `latencyMs`
5. `cache.hit/source/generatedAt`
6. `warnings[]`
7. `degradedFlags[]`
8. `data`
9. `evidence[]`
10. `features[]`
11. `meta.version`

每个 MCP 还必须声明统一的 policy class：
1. `local_required`
2. `local_preferred`
3. `external_gated`
4. `workflow_mutating`
5. `developer_only`

本轮建议映射：
1. `StockKlineMcp` -> `local_required`
2. `StockMinuteMcp` -> `local_required`
3. `StockStrategyMcp` -> `local_required`
4. `StockNewsMcp` -> `local_required`
5. `StockSearchMcp` -> `external_gated`

### 推荐交付顺序
1. R4.1 共享运行时 envelope + governor hook
2. R4.2 `StockKlineMcp` + `StockMinuteMcp`
3. R4.3 `StockStrategyMcp`，包装现有图表 registry 和信号引擎
4. R4.4 `StockNewsMcp`，包装本地事实和 article-read 链路
5. R4.5 `StockSearchMcp`，接入 Tavily 和外部证据归一化

### 验收标准
1. 后续 planner 不再只能靠大 prompt 塞上下文，而是能通过有边界的 MCP 调用图表/新闻/搜索能力。
2. 策略结果全部来自确定性代码，可回溯，不依赖 LLM 自由计算。
3. 新闻证据继续 Local-First，并统一回到 evidence object。
4. Tavily 可用，但明确受控、可观测、可降级。
5. 开发者 trace 能看到：调用了哪个 MCP、为何调用、有哪些 degraded flags、拿回了什么 evidence/features。

### 本轮规划验证
1. 同步任务台账和当前计划状态。
2. 校验 `.automation/tasks.json` 与 `.automation/state.json` 为合法 JSON。
3. 检查本次规划文件诊断状态。