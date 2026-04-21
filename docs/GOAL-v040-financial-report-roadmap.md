# v0.4.x 路线图：财报中心、PDF 对照解析与财报 RAG 演进

> 主版本入口：v0.4.x 统一路线图  
> 当前主版本：v0.4.0  
> 适用仓库：`SimplerJiangAiAgentSecond`  
> 关联历史文档：`docs/GOAL-COMPANYPDF-HUMANLAN.md`、`docs/GOAL-v037-financial-report-transparency.md`

---

## 1. 为什么从 v0.3.7 升级为 v0.4.0

原本按 `v0.3.7` 规划时，问题被定义为“把财务数据测试能力变成正式页面，并补足透明化”。

但进一步梳理后可以确认，这不是一个单点小版本工作，而是一条完整产品线：

1. **产品层改造**
   - 从管理员测试工具升级为正式业务页面 `财报中心`
   - 还要兼顾股票页 `财务报表` Tab 的统一体验

2. **数据层改造**
   - 采集结果从“数量提示”升级为“内容摘要 + 报表列表 + PDF 明细”
   - 补齐正式查询接口、详情接口、阶段日志

3. **PDF 可验证性改造**
   - 软件内显示 PDF 原件
   - 与解析结果对照
   - 允许用户手动重新解析

4. **AI / RAG 基座改造**
   - 需要重新定义财报的结构化直查与叙述型 RAG 边界
   - 需要明确 .NET 技术路线，而不是只借用 Python/LangChain 语境

因此，这个范围已经更接近一个 **小型路线图版本群**，不适合继续塞进 `v0.3.7` 这种单版本心智模型。

> 结论：从现在起统一升级为 **v0.4.x 路线图**，按 `v0.4.0`、`v0.4.1`、`v0.4.2`、`v0.4.3` 分段落地。

---

## 2. v0.4.x 总目标

v0.4.x 的总体目标不是再去扩张更多数据源，而是把现有财报采集链路升级成一条**用户可见、可验证、可复盘、可逐步接入 AI 检索**的正式产品线。

### 总体目标拆分

1. **v0.4.0：财报中心基础落地**
   - 正式页面化
   - 采集结果透明化
   - 本地财报数据表格化

2. **v0.4.1：PDF 原件对照与手动重新解析**
   - PDF 原件内嵌预览
   - 解析结果对照
   - 手动重解析
   - 单股票页与财报中心双入口统一

3. **v0.4.2：财报 RAG Lite 与 .NET 技术栈定型**
   - 叙述型文本切块
   - 本地 Lite RAG
   - .NET 侧技术路线定型

4. **v0.4.3：Hybrid Retrieval 与 AI 集成增强（预研级）**
   - 关键词 + 向量混合召回
   - 财报证据引用接入 AI 分析 / Recommend / Research

---

## 3. 当前代码现状基线

当前仓库已经具备的基线能力如下：

### 3.1 前端

- `frontend/src/modules/admin/FinancialDataTestPanel.vue`
  - 管理员测试页
  - 能看配置、手动采集、聚合日志、Worker 健康

- `frontend/src/modules/admin/FinancialWorkerPanel.vue`
  - Worker 进程与运行日志监控页

- `frontend/src/modules/stocks/FinancialReportTab.vue`
  - 单股票财报视图
  - 可查看趋势、摘要、分红，并可触发采集

### 3.2 后端

- `backend/SimplerJiangAiAgent.FinancialWorker/Program.cs`
  - 独立 FinancialWorker 服务
  - 默认端口 `5120`

- `backend/SimplerJiangAiAgent.FinancialWorker/Services/FinancialDataOrchestrator.cs`
  - 三通道降级：`emweb -> datacenter -> ths`
  - API 失败时尝试 PDF 补充

- `backend/SimplerJiangAiAgent.FinancialWorker/Services/Pdf/*`
  - `PdfVotingEngine`
  - `PdfProcessingPipeline`
  - `FinancialTableParser`
  - `DocnetExtractor` / `PdfPigExtractor` / `IText7Extractor`

- `backend/SimplerJiangAiAgent.Api/Modules/Stocks/StocksModule.cs`
  - `/api/stocks/financial/trend/{symbol}`
  - `/api/stocks/financial/summary/{symbol}`
  - `/api/stocks/financial/collect/{symbol}`
  - `/api/stocks/financial/logs`
  - `/api/stocks/financial/worker/*`
  - `/api/stocks/financial/config`

- `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/FinancialDataReadService.cs`
  - 主 API 从本地 `financial-data.db` 直读财报数据

### 3.3 当前缺口

1. 没有正式业务页面 `财报中心`
2. 没有财报列表分页查询与详情查询
3. 没有 PDF 原件预览入口
4. 没有 PDF 过程结果持久化详情模型
5. 没有“手动重解析”能力
6. 没有财报文本的 RAG 数据模型

---

## 4. 版本拆分总览

| 版本 | 核心目标 | 关键词 | 交付物 |
|---|---|---|---|
| `v0.4.0` | 正式页面化与结果透明化 | 财报中心、表格、采集摘要 | 正式页面、列表接口、透明化结果 |
| `v0.4.1` | PDF 原件对照与重新解析 | PDF Viewer、双栏对照、重解析 | 原件预览、详情模型、重解析接口 |
| `v0.4.2` | 财报 RAG Lite 与 .NET 技术栈 | Lite RAG、FTS/BM25、Semantic Kernel 分析 | 文本切块、检索接口、技术选型落定 |
| `v0.4.3` | Hybrid Retrieval + AI 集成 | Embedding、Hybrid、Evidence 注入 | 混合检索、AI 证据引用、实验性增强 |

---

## 5. 版本推进原则

### 原则 1：先白盒，再智能化

先让用户看得见 PDF 原件、解析结果、失败原因、阶段日志，再讨论更深的 RAG 和 Agent 集成。

### 原则 2：结构化与检索分层

- 数值型财务数据：继续结构化直查
- 叙述型财报内容：逐步进入 RAG

### 原则 3：桌面产品优先轻量部署

v0.4.x 不默认引入需要独立服务进程的重型向量基础设施，优先本地单文件和现有运行目录体系。

### 原则 4：单股票页与财报中心共用能力

不能出现：

- 财报中心能看 PDF
- 股票页看不到

这两处入口要共享同一套详情能力，只是默认布局不同。

---

## 6. 各版本文档索引

- `docs/GOAL-v040-financial-center-foundation.md`
- `docs/GOAL-v041-pdf-compare-and-reparse.md`
- `docs/GOAL-v042-financial-rag-and-dotnet-stack.md`
- `docs/GOAL-v043-hybrid-retrieval-and-ai-integration.md`

---

## 7. 推荐实施顺序

### 第一阶段：v0.4.0

先完成“看得见结果”的基础层：

1. 财报中心页面
2. 列表表格
3. 正式查询接口
4. 采集摘要增强

### 第二阶段：v0.4.1

在基础页面稳定后再加入更重的 PDF 详情能力：

1. PDF 原件显示
2. 对照解析
3. 手动重解析
4. 阶段失败追踪

### 第三阶段：v0.4.2

完成财报文本可检索的最小闭环：

1. 叙述文本切块
2. Lite RAG
3. .NET 路线固定
4. 最小接口验证

### 第四阶段：v0.4.3

将财报检索能力接入 AI 路径，并预研更强召回。

---

## 8. 最终决策摘要

v0.4.x 的路线应明确为：

1. **v0.4.0**：先把“财务数据测试”升级为正式页面 `财报中心`
2. **v0.4.1**：让 PDF 原件与解析结果可对照、可重解析
3. **v0.4.2**：用 .NET 友好的方式落地财报 Lite RAG
4. **v0.4.3**：再考虑向量化与 AI 深度集成

> 一句话总结：**先做可信财报中心，再做可用财报检索，最后再做更强 AI。**
