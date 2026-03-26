# GOAL-017-R1 Plan - Normalized Market-Data Layer + Quant Contracts

## Goal
先落量化双引擎的最小公共底座：统一行情输入归一化层、统一 feature/signal/comparison contract、统一主/影子引擎元数据语义，为后续 `Skender primary` 与 `Lean shadow` 的真正接入提供稳定边界。

## Why R1 First
1. 如果没有统一 normalized input 层，后续双引擎差异会变成“输入差异 + 算法差异”的混合噪声，无法定位。
2. 如果没有统一 contract，图表、MCP、Agent、交易计划会分别读取不同结构，后面会形成四套局部模型。
3. 旧 `Stock Copilot / GOAL-AGENT-002` 相关 `StockCopilot*Dto` 命名仍可能在仓库残留，但按当前状态它们已不再是必须兼容的产品层前提；本设计应以领域能力和当前主线目标为准，而不是继续围绕旧 Copilot 命名收敛。

## Scope
1. 统一行情归一化输入模型
2. 统一量化结果 contract
3. 统一主/影子引擎元数据与降级语义
4. 明确与现有 MCP、图表策略注册表、Agent 上下文、交易计划链路的接缝

## Out of Scope
1. 本阶段不真正接入 `Skender.Stock.Indicators`
2. 本阶段不真正接入 LEAN runtime
3. 本阶段不在前端增加新 UI，仅定义后端和 MCP 可消费 contract
4. 本阶段不重写现有 chart strategy registry 计算逻辑，仅定义其未来如何映射到后端 contract

## Existing Assets To Reuse
1. 现有图表策略注册表：`frontend/src/modules/stocks/charting/chartStrategyRegistry.js`
2. 现有领域内策略信号与 MCP envelope 经验，可作为 contract 设计参考，但不要求继续保留旧 `StockCopilot*` 命名。
3. 现有 K 线、分时、新闻 MCP envelope 方向：`traceId/taskId/toolName/latency/cache/warnings/degradedFlags/data/evidence/features/meta`
4. 现有 Agent 需要的 K 线/分时窗口与本地事实上下文：`StockAgentOrchestrator`

## R1 Deliverables
1. `NormalizedBar` / `NormalizedBarSeries` 设计
2. `QuantFeatureSnapshotDto` 设计
3. `QuantStrategySignalDto` 设计
4. `QuantEngineComparisonDto` 设计
5. `AgentQuantContextDto` 设计
6. `QuantEngineDescriptorDto` 设计
7. 旧残留 DTO 到新 contract 的迁移与清理策略
8. 测试与验收基线

## Normalized Input Model
### 1. NormalizedBar
- Purpose: 成为 Skender、Lean、MCP、Agent、交易计划共享的最小行情 bar 模型。
- Fields:
  - `Symbol`: 统一证券代码，例如 `sh600000`
  - `Timeframe`: `minute | day | week | month | year`
  - `Timestamp`: Unix milliseconds
  - `Open`
  - `High`
  - `Low`
  - `Close`
  - `Volume`
  - `Turnover`
  - `Source`: `eastmoney | tencent | cache | merge | replay`
  - `SessionDate`: 交易日
  - `SessionPhase`: `preopen | morning | midday | afternoon | close | offhours`
  - `AdjustedFlags`: `none | forward_adjusted | backward_adjusted | mixed`
  - `IsSynthetic`: 是否由 merge/rebuild/repair 生成

### 2. NormalizedBarSeries
- Purpose: 在 bar 之上再封一层窗口与质量上下文，避免后续所有服务自己补 metadata。
- Fields:
  - `Symbol`
  - `Timeframe`
  - `Bars`
  - `WindowSize`
  - `RequestedCount`
  - `ActualCount`
  - `SourceSummary`
  - `MissingSegments`
  - `DegradedFlags`
  - `LastUpdatedAt`

### 3. Normalization Rules
1. 所有时间统一使用 Unix 毫秒，避免前后端、Skender、Lean 在时间精度上各自处理。
2. 分时与 K 线都必须保留 `Source` 与 `IsSynthetic`，便于解释“这是原始行情”还是“由 merge/cache 生成”。
3. 午休、停牌、节假日、缺 bar 不在引擎内部偷偷吞掉；必须通过 `MissingSegments` 或 `DegradedFlags` 暴露出来。
4. 对 replay 数据与 live 数据使用相同 normalized model，避免未来 replay contract 分叉。

## Quant Output Contracts
### 1. QuantEngineDescriptorDto
- Purpose: 描述当前结果来自哪个引擎与什么模式。
- Fields:
  - `EngineKey`: `skender | lean | custom | chart-registry`
  - `EngineRole`: `primary | shadow | developer-only`
  - `EngineVersion`
  - `ExecutionMode`: `live | cached | replay | backtest | synthetic`
  - `PolicyClass`: `primary-visible | shadow-hidden | replay-only`

### 2. QuantFeatureValueDto
- Purpose: 表达单个可命名特征，而不是只给一坨匿名 features。
- Fields:
  - `FeatureKey`: 例如 `ma_5`, `macd_diff`, `rsi_6`, `vwap_distance_percent`, `opening_range_breakout`
  - `DisplayName`
  - `Category`: `trend | oscillator | volatility | intraday-structure | breakout | liquidity | risk`
  - `Timeframe`
  - `NumericValue`
  - `State`: 例如 `golden`, `overbought`, `neutral`, `breakout`, `weakness`
  - `Unit`: `price | percent | ratio | score | count | none`
  - `Lookback`
  - `Description`

### 3. QuantFeatureSnapshotDto
- Purpose: 一次计算产生的特征快照。
- Fields:
  - `Engine`
  - `Symbol`
  - `Timeframe`
  - `ComputedAt`
  - `WindowSize`
  - `WarmupState`: `ready | insufficient_history | partial_ready`
  - `DegradedFlags`
  - `Features`: `IReadOnlyList<QuantFeatureValueDto>`
  - `EvidenceRefs`: 关联行情/本地事实/策略依据的引用键

### 4. QuantStrategySignalDto
- Purpose: 表达“可被图表、Agent、交易计划复用”的统一信号，不再局限于当前 `Strategy/Signal/NumericValue/State/Description` 六元组。
- Fields:
  - `Engine`
  - `SignalKey`: 唯一键，例如 `macd.cross.bullish.day`
  - `StrategyKey`: `macd | kdj | td | vwap | breakout | gap`
  - `DisplayName`
  - `Timeframe`
  - `Direction`: `bullish | bearish | neutral | mixed`
  - `Strength`: `weak | medium | strong`
  - `SignalState`: 例如 `golden`, `death`, `breakout`, `gap_up`, `oversold`
  - `NumericValue`
  - `TriggerPrice`
  - `InvalidationPrice`
  - `TriggeredAt`
  - `Description`
  - `SupportingFeatureKeys`
  - `DegradedFlags`

### 5. QuantEngineComparisonDto
- Purpose: 专门服务于 shadow parity、debug、calibration，而不是让业务层自己算 diff。
- Fields:
  - `ComparisonKey`
  - `Symbol`
  - `Timeframe`
  - `PrimaryEngineKey`
  - `ShadowEngineKey`
  - `FeatureOrSignalKey`
  - `PrimaryValue`
  - `ShadowValue`
  - `Delta`
  - `AgreementState`: `equal | near | diverged | incomparable`
  - `Threshold`
  - `AnalysisWindow`
  - `Notes`

### 6. AgentQuantContextDto
- Purpose: 专门喂给 Agent，控制它看到的是“压缩后的确定性量化上下文”，而不是原始长数组。
- Fields:
  - `Symbol`
  - `PrimaryEngine`
  - `TimeframeSummaries`
  - `HeadlineFeatures`: 每个 timeframe 3-7 个关键特征
  - `LatestSignals`: 最近有效信号
  - `ConflictSummary`: 信号冲突、主次矛盾、影子差异摘要
  - `RiskFlags`
  - `CalibrationHints`: 例如“同类信号历史样本不足”“影子引擎明显分歧”
  - `EvidenceRefs`

## Mapping Strategy From Existing DTOs
1. 如果仓库中仍残留旧 `StockCopilotKLineDataDto`、`StockCopilotMinuteDataDto`、`StockCopilotStrategySignalDto`、`StockCopilotMcpEnvelopeDto`，只允许作为迁移期间的临时 mapper 输入。
2. 新 contract 不再以保留旧 `StockCopilot*` 命名为目标。
3. 中期方向是让 `Quant*` 与领域 MCP contract 成为唯一主模型，再由必要的兼容层做一次性迁移或清理。

## Service Boundaries
### 1. IMarketDataNormalizer
- Input: 现有 `KLinePointDto`, `MinuteLinePointDto`, quote/source/session context
- Output: `NormalizedBarSeries`

### 2. IQuantContractAssembler
- Input: normalized series + raw signal/feature compute result
- Output: `QuantFeatureSnapshotDto`, `QuantStrategySignalDto`, `AgentQuantContextDto`

### 3. IQuantComparisonService
- Input: primary/shadow feature or signal snapshots
- Output: `QuantEngineComparisonDto`

### 4. IAgentQuantContextBuilder
- Input: feature snapshots, signals, market/news context
- Output: `AgentQuantContextDto`

## Integration With Existing Systems
### 1. Chart Integration
1. 前端 chart strategy registry 继续负责 UI 选择、分 view gating、marker/overlay 渲染。
2. 后端统一 contract 负责把策略结果变成可审计的结构化信号。
3. 后续只允许“前端展示自定义视图”，不允许“前端和后端各算一版同名信号且互不承认”。

### 2. MCP Integration
1. `StockKlineMcp` 和 `StockMinuteMcp` 读取 `NormalizedBarSeries`。
2. `StockStrategyMcp` 读取 `QuantFeatureSnapshotDto` 与 `QuantStrategySignalDto`。
3. `features` / `data` / `meta` 字段复用 R4 已定的 envelope，不再新增第二套 MCP schema。

### 3. Agent Integration
1. `StockAgentOrchestrator` 后续不再默认吞完整长数组，只保留必要价格窗口和 `AgentQuantContextDto`。
2. prompt 中引用的趋势、背离、关键位、分时强弱，应优先从 `AgentQuantContextDto` 来。
3. 若 `WarmupState != ready` 或 `DegradedFlags` 非空，Agent 必须显式降级自信度。

### 4. Trading Plan Integration
1. 交易计划草稿可以读取 `QuantStrategySignalDto` 作为确定性触发/失效候选。
2. 计划触发 worker 和 review worker 后续可读取同一套 signal key，而不是各自 hardcode 价格逻辑。

## Acceptance Rules
1. 新 contract 不能要求前端先改完才能落地；必须允许通过 mapper 与现有 DTO 共存。
2. 所有新 DTO 都必须显式包含 `Engine` 或 `EngineKey`，不能把“主/影子来源”藏进 description 文本。
3. 所有 comparison 输出都必须只进入开发者模式、report 或 calibration；默认用户视图不展示双主结果。

## Tests
1. 合成 K 线 / 分时样本测试 normalized timestamp、session、missing segment、synthetic merge 标记。
2. DTO 迁移测试：如仍存在旧残留 DTO，必须证明其可被稳定迁移到 `Quant*` contract；不再把保留旧对外导出作为默认目标。
3. MCP contract 测试：`StockStrategyMcp` 能返回结构化 feature/signal 摘要，而不是仅字符串描述。
4. Agent contract 测试：当 `WarmupState=insufficient_history` 或存在 `degradedFlags` 时，context builder 输出明确风险提示。

## DoD
1. 形成独立的 R1 设计文件，并同步任务台账、README 与双语报告。
2. 明确 normalized input、feature/signal/comparison/agent context 五类 contract。
3. 明确旧残留 DTO 的迁移/清理路径，而不是默认长期兼容。
4. 后续开发者可以直接按本文件新增 DTO、service interface 和定向测试，而不需要再次重拆边界。
