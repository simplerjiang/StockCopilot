# 20260423 人工测试 Bug 清单

> 状态：已分析根因，等待 v0.4.2 完成后排期修复。

---

## BUG-1: "采集PDF原件"提示成功但实际无PDF

**现象**：点击"采集PDF原件"后提示成功，但卡片仍显示无PDF原件。过程无进度反馈。

**根因**：
1. **前端不检查返回值**：`FinancialReportTab.vue:L584` 和 `FinancialDetailDrawer.vue:L280` 的 `onCollectPdf()` 完全忽略返回值（`processedCount`/`downloadedCount`），只要不抛异常就显示"采集完成"。
2. **后端静默返回空结果**：`PdfProcessingPipeline.cs:L63-68` cninfo 返回 0 个公告时只在 `result.Notes` 记录原因，前端不展示。
3. **无阶段进度**：5 阶段管线全程同步，前端仅有 spinner。

**涉及文件**：
- 前端：`frontend/src/modules/stocks/FinancialReportTab.vue`（onCollectPdf）
- 前端：`frontend/src/modules/financial/FinancialDetailDrawer.vue`（onCollectPdf）
- 后端：`backend/SimplerJiangAiAgent.FinancialWorker/Services/Pdf/PdfProcessingPipeline.cs`（ProcessAsync）

**修复方案**：前端检查 `processedCount`，为 0 时展示具体原因；增加采集结果摘要。
**严重度**：中 | **级别**：M

---

## BUG-2: 金额单位不一致（⚠️ 高危）

**现象**：财报数字单位不合理，不同报告金额差异过大。

**根因**：
1. **多通道单位不统一**：emweb/datacenter 存储单位是**元**；ths 经 `ParseChineseNumber()` 转换后存储单位是**万元**（"3.5亿"→35000万）。
2. **数据库无单位标记**：`FinancialReport` 模型无 `DataUnit` 字段。
3. **前端假设统一**：`formatLargeNumber()` 按元处理（÷1e8→亿），降级到 ths 通道时**金额差 10000 倍**。

**涉及文件**：
- 后端：`backend/SimplerJiangAiAgent.FinancialWorker/Services/FinanceClientHelper.cs:L64`（ParseChineseNumber，万元基准）
- 后端：`backend/SimplerJiangAiAgent.FinancialWorker/Services/ThsFinanceClient.cs:L185`
- 后端：`backend/SimplerJiangAiAgent.FinancialWorker/Services/EastmoneyFinanceClient.cs`（原始值=元）
- 后端：`backend/SimplerJiangAiAgent.FinancialWorker/Models/FinancialReport.cs`（无 DataUnit）

**修复方案**：ths 通道统一转为元（万元值 ×10000），所有通道存储单位一致。
**严重度**：高 | **级别**：S（数据正确性）

---

## BUG-3: "重新采集报告"无过程反馈

**现象**：点击后无进度，只有最终成功/失败提示。

**根因**：
1. **返回值被忽略**：`onRecollect()` 丢弃 `CollectionResult`（含 channel、reportCount、fallbackReason、durationMs）。
2. **同步长请求**：串行 3 数据源，Api 代理超时仅 60 秒，易超时。

**涉及文件**：
- 前端：`frontend/src/modules/financial/FinancialDetailDrawer.vue`（onRecollect）
- 后端：`backend/SimplerJiangAiAgent.FinancialWorker/Services/FinancialDataOrchestrator.cs`（CollectAsync）
- 后端：`backend/SimplerJiangAiAgent.Api/Modules/Stocks/StocksModule.cs`（代理超时 60s）

**修复方案**：展示采集结果摘要（通道、报告数、耗时、降级原因）。
**严重度**：中 | **级别**：M

---

## BUG-4: 选择2023年仍只显示2025数据

**现象**：选择 2023 年无法获取历史数据。

**根因**：
1. **采集器只拉最新数据**：Eastmoney API `endDate` 默认空，只返最近 4-5 期（2024~2025），本地无 2023 数据。
2. **选年份不触发采集**：前端筛选只在本地 LiteDB 过滤，不自动触发历史采集。
3. **无空态提示**。

**涉及文件**：
- 后端：`backend/SimplerJiangAiAgent.FinancialWorker/Services/EastmoneyFinanceClient.cs:L64-72`
- 后端：`backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/FinancialDataReadService.cs:L284-294`

**修复方案**：后端支持按年份范围采集；前端空态引导采集历史数据。
**严重度**：高 | **级别**：M

---

## 优先级排序

1. **BUG-2**（S级紧急）→ V043-P0-A
2. **BUG-4**（M级）→ V043-P1-B
3. **BUG-1**（M级）→ V043-P1-C
4. **BUG-3**（M级）→ V043-P1-D

> 计划在 v0.4.2 完成后，作为 v0.4.3 排期修复。

---

## 20260423 补充反馈（PDF 管线设计问题）

> 已纳入 v0.4.3 Sprint 规划。

### P1: PDF 全文不保存
- **现象**：提取的文字内容在解析后丢弃，无法给 RAG/LLM 使用
- **计划**：V043-S1 增加 `FullTextPages` 持久化

### P2: 前端价格显示不统一
- **现象**：有的显示元，有的显示完整数字，缺少统一缩写
- **计划**：V043-S2 统一 `formatMoneyDisplay()`

### P3: PDF 下载后解析失败导致前端看不到 PDF
- **现象**：PDF 下载成功但解析失败时，前端列表中看不到该 PDF
- **计划**：V043-S3 下载阶段即创建数据库记录

### P4: 对照面板右侧内容空洞
- **现象**：解析单元只有 snippet 无实际内容，投票无透明化，阶段无详细日志
- **计划**：V043-S4（解析内容）+ V043-S5（投票透明化）+ V043-S6（阶段折叠日志）

### P5: 大多数股票采集不到 PDF
- **现象**：cninfo 采集成功率低，多数财报无 PDF 可查
- **计划**：V043-S7 提升采集成功率
