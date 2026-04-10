# GOAL: 研究流水线上下文增强计划

**创建日期**: 2026-04-09  
**触发场景**: S71 会话 (sh603099) 分析质量审查  
**目标**: 解决分析师上下文缺失导致的低质量/无方向分析输出

---

## 一、问题诊断

### S71 实际表现

通过审查 Session 71 (sh603099) 的全部角色输出，发现以下系统性问题：

| 问题 | 严重度 | 影响范围 |
|------|--------|---------|
| **分析师不知道自己在分析哪只股票** — company_overview_analyst 输出"中国旅游相关公司（股票代码可能为600000或相关代码）"，fundamentals_analyst 猜测"长城汽车 (600188)"，product_analyst 猜测"长白山"。实际目标是 sh603099 (长白山旅游) | 🔴 致命 | 所有角色 |
| **分析师不知道当前时间** — market_analyst 提到"2024年（根据上下文推断）"，所有角色无法判断数据的时效性 | 🔴 致命 | 所有角色 |
| **分析师对自己获取的数据缺乏信心** — 几乎所有角色都表示"信息中未明确给出"、"请提供更明确的股票代码" | 🟡 严重 | 所有分析师角色 |
| **下游角色完全失效** — bull/bear researcher、risk analysts 全部退化为通用客服式回复 | 🔴 致命 | 研究辩论 + 风险评估 |

### 根因分析

#### 1. 用户内容中仅有 Symbol 编码，缺少股票名称

**当前 `BuildUserContent()` 输出**:
```
## 目标个股: sh603099
## 用户意图: 分析一下 预估股价
```

问题: 本地模型（如 Gemma-12B）通常无法从 `sh603099` 推断出公司名称"长白山旅游"。需要在 prompt 中显式注入**股票名称**。

#### 2. 缺少当前时间戳

模型不知道"现在是什么时候"，因此：
- 无法判断 K 线数据是否是最新的
- 无法判断新闻是否过时
- 无法给出"今天/本周/近期"的时间锚点
- 分析师猜测数据年份（2024），与实际时间 2026 年严重偏差

#### 3. MCP 工具返回的 JSON 数据在压缩后丢失关键信息

`PromptGovernancePlan` 对本地模型有严格的上下文压缩策略（`UpstreamBudget` ≈ 总预算的 45%），但压缩算法没有优先保留 `symbol`、`name` 等关键标识字段，导致 LLM 看到的是一堆去除标识的数值数据。

#### 4. 系统 Prompt 缺少"你正在分析的股票是XXX"的锚定语句

`AnalystSystemShell` 只说"你是一个严谨专业的股票研究工作台 AI 助手"，没有用角色设定强化目标股票的认知。

---

## 二、改进方案

### 方案 A: 上下文注入增强（推荐，工作量 S-M）

**核心思路**: 在现有 `BuildUserContent()` 和 System Prompt 中注入关键上下文，零成本提升分析质量。

#### A1: User Content 注入股票名称 + 当前时间
**位置**: `ResearchRoleExecutor.BuildUserContent()`

```diff
 var sb = new StringBuilder();
-sb.AppendLine($"## 目标个股: {context.Symbol}");
+sb.AppendLine($"## 目标个股: {context.Symbol} ({context.StockName})");
+sb.AppendLine($"## 当前时间: {DateTime.Now:yyyy-MM-dd HH:mm} (北京时间)");
 sb.AppendLine($"## 用户意图: {context.UserPrompt}");
```

**需要**: 在 `RoleExecutionContext` 中新增 `StockName` 字段，从 session 或 CompanyOverview 阶段产出中获取。

#### A2: 系统 Prompt 增加目标锚定
**位置**: `TradingWorkbenchPromptTemplates.AnalystSystemShell`

在系统 prompt 中强化：
```
重要：本次分析的目标个股信息将在用户消息中以"## 目标个股"形式给出，
包含股票代码和名称。你的所有分析必须围绕该目标个股展开，
严禁猜测或混淆其他股票。
```

#### A3: CompanyOverview Preflight 产出共享股票名称
**位置**: `ResearchRunner` 管道逻辑

CompanyOverview 阶段完成后，提取 `companyName` 并注入到后续所有角色的 `RoleExecutionContext` 中，确保即使 MCP 工具数据被压缩，股票名称也始终存在于 prompt 顶部。

#### A4: 压缩算法保留关键标识字段
**位置**: `ResearchRoleExecutor.CompactJsonElement()`

在 JSON 压缩逻辑中，对以下字段设为"不可压缩"白名单：
- `symbol`, `name`, `companyName`, `shortName`
- `price`, `changePercent`, `quoteTimestamp`
- `peRatio`, `roe`, `revenue`, `netProfit`

#### 预期效果
- 所有分析师在 prompt 开头就能看到: `## 目标个股: sh603099 (长白山旅游)`
- 所有分析师知道当前时间
- 即使 MCP 数据被压缩，关键标识不会丢失
- 下游角色（researcher/trader/risk）也能从压缩后的上游产出中找到目标股票信息

**工作量估算**: M 级（涉及 3-4 个文件修改，需要单元测试覆盖）

---

### 方案 B: 结构化上下文摘要卡片（推荐，与 A 互补，工作量 M）

**核心思路**: 在 CompanyOverview 阶段完成后，自动生成一张"股票上下文卡片"（纯文本），注入到所有后续角色的 prompt 中。

#### 卡片模板
```
═══════════════════════════════════════
📋 研究目标概况
─────────────────────────────────────
• 股票代码: sh603099
• 公司名称: 长白山旅游股份有限公司
• 简称: 长白山
• 行业: 旅游景区
• 当前价格: ¥15.20 (涨跌幅: +2.35%)
• 当前时间: 2026-04-09 14:33 (北京时间)
• 市盈率(PE): 45.6x
• 流通市值: 85.3亿
─────────────────────────────────────
• 用户意图: 分析一下，预估股价
═══════════════════════════════════════
```

#### 实现方式
- 在 `ResearchRunner.RunStageAsync()` 中，CompanyOverview 阶段完成后，从其 MCP 工具结果中提取关键字段
- 生成简洁的上下文卡片（约 300-500 字符，不占用太多 token 预算）
- 卡片作为 `RoleExecutionContext.StockContextCard` 注入到所有后续角色
- `BuildUserContent()` 在最顶部渲染此卡片

**工作量估算**: M 级

---

### 方案 C: RAG 知识库集成（长期，工作量 L）

**核心思路**: 将部分数据（K线历史、分时图模式、行业对比数据）构建为向量化 RAG 知识库，分析师按需检索。

#### C1: K线数据 RAG
- 将近 120 个交易日的日 K 数据按"趋势段"切分为文本片段
- 每个片段包含: 时间段、价格范围、量能变化、趋势标签
- 分析师查询: "sh603099 最近的趋势突破点"

#### C2: 分时数据模式识别
- 典型分时图模式（早盘急拉、午后跳水、尾盘突击等）预标注
- 存储为 RAG 文档: `{symbol}_{date}_minute_pattern`

#### C3: 行业/大盘上下文 RAG
- 定期快照大盘指数变化、板块轮动数据
- 存储为周度摘要文档

#### RAG 技术选型
- **嵌入模型**: 使用本地 Ollama 的 embedding 模型（如 nomic-embed-text）
- **向量数据库**: SQLite + 向量扩展（sqlite-vec）或 内存中的简单余弦相似度
- **检索粒度**: 每个角色最多检索 3-5 个相关片段，控制 token 消耗

#### 优点
- 分析师能获取远超单次 MCP 调用的历史深度
- 可存储预计算的技术指标解读，减少 LLM 重复计算
- 知识库可持续积累，覆盖更多股票

#### 缺点
- 工程复杂度高，需要嵌入模型、向量存储、检索管道
- 数据更新频率需要设计（日/周/实时）
- 本地模型可能不善于利用检索到的长文本

**工作量估算**: L 级（需多轮迭代）

---

### 方案 D: 增强 MCP 工具返回内容（补充，工作量 S）

**核心思路**: 让 MCP 工具在返回结果中主动附带更多上下文。

#### D1: 所有 MCP 工具返回中附带时间戳和股票名称
当前 `StockCopilotMcpEnvelopeDto` 包含 `symbol`，但某些内部数据 DTO 可能在序列化后不够明确。确保每个工具返回顶层都有:
```json
{
  "symbol": "sh603099",
  "stockName": "长白山",
  "dataTimestamp": "2026-04-09T14:33:00+08:00",
  "data": { ... }
}
```

#### D2: MarketContextMcp 返回增强
当前返回大盘信息，可增加:
- 当前日期/时间
- 交易状态（盘中/已收盘/非交易日）
- 该股所属板块涨跌幅

**工作量估算**: S 级

---

## 三、推荐实施路径

| 优先级 | 方案 | 预期收益 | 工作量 |
|--------|------|---------|--------|
| **P0** | A1 + A2: 注入股票名称 + 时间戳到 User Content 和 System Prompt | 立即解决"不知道分析谁"和"不知道什么时候"的致命问题 | S |
| **P1** | A3: CompanyOverview 共享股票名称 | 确保即使 Preflight 之后也能传递名称 | S |
| **P1** | D1 + D2: MCP 工具返回增强 | 从数据源头改善上下文完整性 | S |
| **P2** | A4: 压缩白名单 | 防止关键信息在压缩中丢失 | S |
| **P2** | B: 结构化上下文卡片 | 为下游角色提供一目了然的研究概况 | M |
| **P3** | C: RAG 知识库 | 长期知识积累和深度分析能力提升 | L |

**建议**: 先做 P0 + P1（2-3 个文件改动），立竿见影地修复分析失效问题，然后评估是否继续 P2/P3。

---

## 四、实施状态（2026-04-09 完成）

### 已实施的变更

| 优先级 | 变更 | 文件 | 状态 |
|--------|------|------|------|
| P0 | `RoleExecutionContext` 新增 `StockName` 字段 | `ResearchRoleExecutor.cs` | ✅ |
| P0 | `BuildUserContent()` 注入 `## 目标个股: {Symbol} ({StockName})` + `## 当前时间` | `ResearchRoleExecutor.cs` | ✅ |
| P0 | `AnalystSystemShell` 增加目标锚定规则（第6条） | `TradingWorkbenchPromptTemplates.cs` | ✅ |
| P0 | `BackOfficePrefix` 增加目标锚定说明 | `TradingWorkbenchPromptTemplates.cs` | ✅ |
| P1 | CompanyOverview 完成后 `TryExtractStockName()` 提取并传递 `StockName` | `ResearchRunner.cs` | ✅ |
| P1 | K线数据 `NarrativeSummary`：每日开高低收涨跌幅成交量（最近30日） | `StockCopilotMcpService.cs` + `StockAgentRuntimeModels.cs` | ✅ |
| P1 | 分时数据 `NarrativeSummary`：30分钟区间汇总（高低涨跌量） | `StockCopilotMcpService.cs` + `StockAgentRuntimeModels.cs` | ✅ |
| P2 | 压缩白名单 `ProtectedJsonPropertyNames`（symbol, name, price 等关键字段不截断） | `ResearchRoleExecutor.cs` | ✅ |
| P3 | RAG 知识库 | — | ⏳ 下一 Sprint |

### 测试结果

- 90 个 Research 相关单元测试全部通过 (0 失败)
- 所有新增字段均为 optional + default null，向后兼容

### 受影响文件清单

| 文件 | 修改内容 |
|------|---------|
| `ResearchRoleExecutor.cs` | `RoleExecutionContext.StockName`, `BuildUserContent()` 增强, `ProtectedJsonPropertyNames` 白名单, `CompactJsonElement()` 保留保护字段 |
| `TradingWorkbenchPromptTemplates.cs` | `AnalystSystemShell` 第6条目标锚定, `BackOfficePrefix` 目标锚定 |
| `ResearchRunner.cs` | `_resolvedStockName` 字段, `TryExtractStockName()` 方法, 两处 `RoleExecutionContext` 构造传递 StockName |
| `StockCopilotMcpService.cs` | `BuildKlineNarrativeSummary()`, `FormatVolume()`, `BuildMinuteNarrativeSummary()`, K线和分时 DTO 构造时填充 NarrativeSummary |
| `StockAgentRuntimeModels.cs` | `StockCopilotKlineDataDto.NarrativeSummary`, `StockCopilotMinuteDataDto.NarrativeSummary` |
