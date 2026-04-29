# v0.6.0 — AI 分析回测验证

Status: Approved · Sprint Active

---

## 1. 目标

回答三个核心问题：

1. AI 对某只股票的判断，过去命中率多少，可信不可信？
2. AI 对某个板块的看法，过去整体表现如何？
3. 改了 Prompt / Agent，能不能在记分卡上看到分数变化？

---

## 2. 现有基础

| 组件 | 说明 |
|------|------|
| `StockAgentReplayCalibrationService` | 已有基本 replay，计算 1/3/5/10 日 horizon 命中率 |
| `StockAgentAnalysisHistory` | 分析记录含 ResultJson（commander 方向/置信度/目标价/止损价） |
| `KLinePoints` | 日线 OHLCV 数据（Baostock 主源 + 东财备源） |

---

## 3. 分阶段规划

### 阶段一：后验验证（本 Sprint，v0.6.0）

对已有的分析历史记录，回溯验证预测是否正确。不重新跑分析，只检查结果。

- 输入：`StockAgentAnalysisHistory` 已落库的记录
- 产出：每条分析对应一条 `BacktestResult`，含多窗口命中判定
- 前提：分析日之后需有足够交易日 K 线数据

### 阶段二：PIT 时点回测（v0.7.0+，未来）

冻结时点数据，重新跑完整分析管道，与真实行情对比。需要 PIT 切片层。

- 需要解决：数据快照冻结、SourcePublishTime 补全、RAG 时点过滤
- 本 Sprint 不涉及

---

## 4. v0.6.0 Story 拆分

### V060-S1: BacktestResult 实体 + Schema（M 级）

- 新建 `BacktestResult` 实体
- 字段：
  - `Id`, `AnalysisHistoryId`, `Symbol`, `AnalysisDate`
  - `PredictedDirection`, `Confidence`, `TargetPrice`, `StopLoss`
  - `Window1dActual`, `Window3dActual`, `Window5dActual`, `Window10dActual`
  - `IsCorrect1d`, `IsCorrect3d`, `IsCorrect5d`, `IsCorrect10d`
  - `TargetHit`, `StopTriggered`
  - `CalcStatus` (pending / calculated / insufficient_data)
  - `CreatedAt`, `UpdatedAt`
- SQLite 自动建表
- **验收**：实体编译通过 + Migration 生成表

### V060-S2: BacktestService 核心计算（L 级）

- `RunAsync(long historyId)`：单条回测
  1. 读 `AnalysisHistory`，解析 `ResultJson` 提取方向/置信度/目标价/止损
  2. 取分析日之后 10 个交易日 K 线
  3. 按 4 窗口计算实际涨跌幅
  4. 判定预测准确性（偏多:收益>0=正确，偏空:收益<0=正确，中性:±2%内=正确）
  5. 检查目标价/止损价是否在窗口内触及
  6. 落库 `BacktestResult`
- `RunBatchAsync(symbol?, from?, to?)`：批量回测
  - 跳过已 calculated 的记录
  - 顺序处理，避免并发写冲突
- 与 `StockAgentReplayCalibrationService` 的关系：互补，不替代
  - Replay 做宏观统计；Backtest 做每条记录的详细验证
- **验收**：单元测试覆盖各方向判定 + 边界（数据不足、停牌）

### V060-S3: 回测 REST API（M 级）

- `POST /api/backtest/run/{historyId}` — 触发单条回测
- `POST /api/backtest/run-batch` — 批量回测 (body: symbol?, from?, to?)
- `GET /api/backtest/stats` — 汇总统计 (?symbol=&from=&to=)
  - 返回：totalAnalyses, windows (1d/3d/5d/10d 各自的 count/accuracy/insufficientData),
    targetHitRate, stopTriggerRate, confidenceCorrelation
- `GET /api/backtest/results` — 明细列表 (?symbol=&window=&page=&size=)
- **验收**：API 可调用 + JSON 格式正确

### V060-S4: 前端回测仪表板（L 级）

- 新增 `BacktestDashboard.vue` 页面
- 记分卡：多窗口命中率卡片（1d/3d/5d/10d）
- 置信度-准确率散点图或表格
- 按股票分组统计
- 触发批量回测按钮
- **验收**：UI 可操作 + 数据正确

### V060-S5: K 线图回测标注（M 级）

- 在个股 K 线图上标注历史分析点
  - 看多：绿色向上箭头
  - 看空：红色向下箭头
  - 中性：灰色圆点
- 点击标注可查看分析详情 + 回测结果
- **验收**：标注可见 + 交互有效

### V060-S6: 自动回测 Worker（M 级）

- `BacktestWorker` (BackgroundService)
- 每日收盘后自动扫描未回测的历史记录
- 逐条计算并落库
- **验收**：Worker 启动后自动处理积压记录

---

## 5. 评判规则

### 方向命中率

- "偏多/看多"：N 日后收盘价 > 分析日收盘价 = 命中
- "偏空/看空"：N 日后收盘价 < 分析日收盘价 = 命中
- "中性/观察"：N 日内波动 ±2% 以内 = 命中

### 目标价触达率

- 看多：窗口内最高价 ≥ 目标价 = 触达
- 看空：窗口内最低价 ≤ 目标价 = 触达

### 止损触发率

- 看多：窗口内最低价 ≤ 止损价 = 触发
- 看空：窗口内最高价 ≥ 止损价 = 触发

### 数据不足

- 上市不足 N 日 / 停牌期间：标记 `insufficient_data`

---

## 6. 批次规划

### 批次 1（核心闭环）

S1 实体 → S2 计算引擎 → S3 API → S6 Worker

**目标**：后端完整闭环，API 可用

### 批次 2（用户体验）

S4 仪表板 → S5 K 线标注

**目标**：用户可视化查看回测结果
