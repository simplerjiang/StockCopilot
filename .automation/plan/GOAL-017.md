# GOAL-017 Plan - Quant Dual-Engine Architecture + Agent/Chart Integration

## Goal
构建一层可复用的量化特征与策略能力，把现有分时图、K 线图、交易计划与多 Agent 分析真正打通；采用 `Skender primary + Lean shadow` 的双引擎分层路线，避免把主产品链路直接拖成重型量化平台。

## Problem Statement
1. 现有系统已经能稳定获取 K 线、分时、消息、本地事实与多 Agent 分析结果，但图表策略、后端特征计算、Agent 推理、交易计划触发仍然没有统一 contract。
2. 前端图表已具备策略注册表与 marker/overlay 渲染能力，但当前信号更多是 UI 层能力，尚未上升为后端可审计、可复用、可回放的量化能力层。
3. Agent 目前仍直接消费一部分原始 K 线/分时数组，缺少“代码先算确定性特征，再交给 LLM 解释”的系统性中间层。
4. 如果直接把 Lean 作为主链路量化平台，会显著增加产品主运行时复杂度、部署成本与调试负担；如果只用轻量指标库，又无法为未来 replay/backtest/calibration 提供足够强的研究底座。

## Architecture Direction
1. 主运行时量化引擎：`Skender.Stock.Indicators` 或同类轻量 .NET 指标库，作为 `Primary Engine`。
2. Shadow / Replay / Calibration 引擎：LEAN，作为 `Shadow Engine`，默认不直接参与线上主决策。
3. 统一输入：先把 quote/minute/kline/session/news context 归一化成内部标准模型，再分别喂给 Primary/Shadow 引擎。
4. 统一输出：所有量化结果必须进入统一的 `FeatureSnapshot` / `StrategySignal` / `EngineComparison` contract，而不是让图表、Agent、计划逻辑直接读取不同引擎的原始结构。
5. 单一主口径：前端图表、Agent、交易计划默认只消费 `primary` 结果；`shadow` 结果主要用于差异分析、回放和校准。

## Engine Roles
1. Skender primary
   - 在线或准实时指标计算
   - 多周期趋势/波动/关键位/背离等特征生成
   - 给图表输出 overlay、marker、signal
   - 给 Agent 输出确定性量化特征
   - 给交易计划输出触发与失效条件候选
2. Lean shadow
   - 历史 replay
   - 回测与参数实验
   - 复杂策略组合验证
   - 信号一致性比对
   - 命中率、收益分布、Brier score 等校准工作

## Core Output Contracts
1. `NormalizedBar`
   - symbol, timeframe, timestamp, open, high, low, close, volume, turnover, session tags, adjusted flags
2. `FeatureSnapshot`
   - engine, symbol, timeframe, computedAt, features, warmupState, degradedFlags, evidenceRefs
3. `StrategySignal`
   - engine, signalKey, strategyName, timeframe, direction, strength, trigger, invalidation, supportingFeatures, emittedAt
4. `EngineComparison`
   - featureKey/signalKey, primaryValue, shadowValue, delta, agreementState, threshold, analysisWindow
5. `AgentQuantContext`
   - feature summary, latest signals, conflict summary, calibration hints, evidence links

## Work Breakdown
1. R1 Normalized market-data layer + quant contract
   - 统一 minute/day/month/year bar 输入模型
   - 定义 feature/signal/comparison DTO
   - 统一 engine metadata、warmup、degradedFlags、source 信息
2. R2 Skender primary runtime integration
   - 在后端新增 quant feature service / signal service
   - 先覆盖 MA/EMA/MACD/RSI/KDJ/ATR/BOLL/Donchian/VWAP 等基础指标
   - 补齐 A 股盘中特征：开盘区间、午后漂移、量价背离、VWAP 偏离、假突破、缩量横盘等
   - 输出给图表 registry、Agent、交易计划三方复用
3. R3 Lean shadow replay/calibration integration
   - 引入统一 adapter，把相同 normalized data 喂给 Lean
   - 首批只做 shadow run，不直接控制 UI/Agent/交易计划
   - 建立 signal parity、feature parity 与收益回放基线
4. R4 Product integration
   - 前端图表新增主/影子引擎对比的开发者可视化入口
   - Agent 通过 `StockStrategyMcp` / `StockKlineMcp` / `StockMinuteMcp` 读取量化上下文
   - 交易计划草稿、触发、失效与复核逻辑接入统一量化信号 contract

## Integration Rules
1. 不允许让 Agent 直接根据长数组“自己算 MACD/RSI/趋势”；指标和结构状态必须先由代码计算。
2. 不允许在产品默认路径里让 Primary 与 Shadow 同时参与最终决策；必须有单一主口径。
3. 前端默认只显示主引擎结果；影子结果只进入开发者模式、报告或校准视图。
4. 所有量化特征和信号都要带 `engine`、`computedAt`、`warmupState`、`degradedFlags`，便于追溯。
5. Strategy MCP 必须包装现有 chart strategy registry，而不是重新发明一套前端独占策略体系。

## Expected Benefits
1. 图表不再只是展示价格，而成为“量化信号 + Agent 解释 + 交易计划条件”的统一画布。
2. Agent 从“读取原始数组 + 自由发挥”升级为“消费确定性特征 + 综合解释”，幻觉和伪精确度会下降。
3. 交易计划可直接基于统一 signal contract 做触发、失效、复核与回放。
4. 后续 replay/calibration 可以真正评估“某类量化信号 + Agent 结论”的历史有效性，而不是只看文本输出。

## Risks
1. 同名指标在 Skender 与 Lean 之间可能存在 warmup、平滑、缺失 bar 处理差异；必须通过 `EngineComparison` 明确记录，而不是假设完全一致。
2. 若没有统一 normalized input 层，未来双引擎差异会变成“输入差异 + 算法差异”的混合噪声，无法诊断。
3. 若让 shadow 结果直接进入线上主决策，会导致 Agent、图表与交易计划口径混乱。

## DoD
1. 形成独立的量化双引擎规划任务，并拆出至少 4 个可执行切片。
2. README / tasks / reports / state 四类自动化文件同步更新。
3. 规划明确 `Skender primary + Lean shadow` 的边界、contract 与接入路径。
4. 后续开发阶段可以直接据此拆解为后端、前端、Agent/MCP、replay/calibration 四条支线。