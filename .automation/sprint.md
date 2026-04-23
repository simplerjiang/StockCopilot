# Sprint 看板

> 当前 Sprint 最多 3 个活跃 Story。完成的 Story 归档到 `/memories/repo/sprints/`。

## 看板规则（2026-04-22 新增）

1. **NIT 不延后**：Story 复核暴露的 NIT，须在复核后 1 小时内修完并补回测试，不允许累计到 Sprint 末尾。
2. **BLOCKER 立即停摆**：任何 BLOCKER 阻塞当前 Story 进入下一个；先修 BLOCKER，再继续。
3. **预存在测试失败必须立项**：复核中发现非本 Story 引入的失败用例，须开独立技术债 Story 跟踪，不允许"已知失败"长期挂在主干。
4. **`tasks.json` 与 `sprint.md` 关系**：`sprint.md` 是当前 Sprint 唯一权威看板；`tasks.json` 仅作历史任务事件流（append-only），不再用作活跃任务管理。

---

## 已完成 Sprint 摘要

| Sprint | 完成日期 | Stories | 测试 | 要点 |
|--------|----------|---------|------|------|
| v0.4.2 必修 | 2026-04-23 | 7/7 | User Rep 95/100 | BLOCKER 修复：smartPickPdfId / X-Frame-Options / publish 脚本 |
| v0.4.2N PDF 管线重构 | 2026-04-24 | 8/8 | 1126 tests, 0 fail | 全文持久化 / 价格缩写 / 投票透明化 / cninfo 修复 |
| v0.4.2 财报 RAG Lite | 2026-04-24 | 8/8 | 1147 tests, 0 fail | SQLite FTS5 / jieba 分词 / 三层切块 / BM25 检索 / REST API |

---

## Backlog（技术债）

- **V040-S6-FU-1**（高）：财报中心「毛利润」字段缺失。后端补算 `营业总收入 - 营业总成本`。
- **V040-S6-FU-2**（高）：后端 keyword 参数无效，前端 pageSize=100 临时兜底。需后端实现 symbol/name 模糊匹配。
- **V040-S3-FU-1**（中）：渠道 Tag 配色不统一，需抽 `SOURCE_CHANNEL_STYLE` 全局 token。
- **V040-DEBT-4**（候选）：`StockSearchService` 无排序无市场过滤。
- **V041-DEBT-1**（中）：`FinancialDbContext` BsonMapper.Global 并发 race。
- **V041-DEBT-2**（低）：Api vs Worker RuntimePaths 不一致。
- **V041-DEBT-3**（中）：PdfFileDetail 缺 voting candidates 数组。

---

## v0.4.3 Sprint（Hybrid Retrieval 与 AI 集成增强）

### Sprint 目标
**在 Lite RAG 基础上增加向量检索（Ollama embedding + sqlite-vec），实现 BM25+向量混合召回 + Citation 引用链路 + AI 路径集成。**

### 核心约束（GOAL-v043 §7）
- Embedding：本地 Ollama HTTP（默认 bge-m3），不引入 ONNX / 云端 API
- 向量存储：sqlite-vec 扩展（financial-rag.db 内新增 chunk_embeddings 表）
- 混合检索：BM25 + 向量 + RRF（Reciprocal Rank Fusion），不引入独立 reranker
- 沿用 v0.4.2 三接口（IChunker / IRetriever / IEmbedder），不引入 SK / KM
- 前置门禁：v0.4.2.1 评估集 nDCG@5 baseline，Hybrid 须比 Lite RAG 提升 ≥15%

### Stories

#### V043-S0: v0.4.2.1 评估集（前置门禁）
- **状态**：TODO | **级别**：M
- **描述**：人工标注 30 条财报 Q&A（经营情况 10 / 风险 5 / 募资 5 / 会计政策 5 / 分红 5），每条标注 1-3 个正确 chunk 作为 ground truth。提供 CLI 评估脚本 `tools/RagEval`，输出 nDCG@5 / Recall@10 / MRR 报告。
- **验收**：评估集 JSON 存在；BM25 baseline 报告已生成；nDCG@5 有数值。

#### V043-S1: sqlite-vec 扩展 + chunk_embeddings 表
- **状态**：TODO | **级别**：M
- **描述**：在 `financial-rag.db` 中启用 sqlite-vec 扩展，新增 `chunk_embeddings` 虚表（chunk_id, embedding FLOAT[dim]）。dim 从配置读取（默认 1024）。
- **验收**：Worker 启动时加载 sqlite-vec 并创建虚表；dotnet test 验证插入/查询向量。

#### V043-S2: OllamaEmbedder 实现
- **状态**：TODO | **级别**：M
- **描述**：实现 `IEmbedder` → Ollama HTTP API（`http://localhost:11434/api/embeddings`）。默认模型 bge-m3。检测 Ollama 是否在线，不可用时 `IsAvailable=false` 并 fallback 到纯 BM25。
- **验收**：Ollama 在线时生成向量；离线时优雅降级不崩溃。

#### V043-S3: Embedding Pipeline 集成
- **状态**：TODO | **级别**：M | **依赖**：S1, S2
- **描述**：chunk 入库后异步调用 `IEmbedder`，将向量写入 `chunk_embeddings`。支持批量 embedding。Ollama 不可用时跳过 embedding，仅保留 BM25 索引。
- **验收**：PDF 解析后 chunk_embeddings 有记录；Ollama 离线时 chunks 仍正常入库。

#### V043-S4: HybridRetriever（BM25 + 向量 + RRF）
- **状态**：TODO | **级别**：M | **依赖**：S1, S2, S3
- **描述**：扩展 `IRetriever`，内部组合 BM25（FTS5）+ 向量（sqlite-vec knn）+ RRF 合并排序。Embedding 不可用时自动降级为纯 BM25。
- **验收**：Hybrid 模式 nDCG@5 比 BM25 baseline 提升 ≥15%；降级后行为与 v0.4.2 一致。

#### V043-S5: Search Mode 扩展
- **状态**：TODO | **级别**：S | **依赖**：S4
- **描述**：`POST /api/rag/search` 增加 `mode` 参数（bm25 / vector / hybrid，默认 hybrid）。返回中增加 `mode` 字段标识实际使用的检索模式（含降级场景）。
- **验收**：3 种 mode 可切换；降级时返回实际 mode。

#### V043-S6: Citation DTO + AI 路径注入
- **状态**：TODO | **级别**：M | **依赖**：S4
- **描述**：定义 Citation DTO（chunk_id / text / section / page_start / page_end / score / source_file）。在股票信息页 AI 分析、Research、Recommend 调用 LLM 前注入 hybrid 检索结果，响应中保留 citation 数组。
- **验收**：AI 分析结果含 citations；citation 包含页码和来源。

#### V043-S7: 前端 Citation Chip
- **状态**：TODO | **级别**：M | **依赖**：S6
- **描述**：AI 分析结果中的 citation 显示为可点击 chip（[报告名 P.xx]），点击后打开 PDF Viewer 并定位到 `page_start`。缺 page 字段的旧数据显示"原文页码不可用"。
- **验收**：citation chip 可点击跳转；旧数据降级提示。

#### V043-S8: Embedding Model 管理 UI
- **状态**：TODO | **级别**：M | **依赖**：S2
- **描述**：LLM 设置页面新增 Embedding 模型管理子面板：检测 Ollama 在线状态、列出已安装 embedding 模型、一键 `ollama pull <model>` 安装、切换模型时提示重建索引。
- **验收**：UI 可检测 Ollama / 安装模型 / 切换模型；切换时触发重建提示。

#### V043-S9: v0.4.3 全链路验收
- **状态**：TODO | **级别**：M | **依赖**：S0~S8
- **验收标准**：
  - dotnet test + vitest 全绿
  - nDCG@5 评估报告：Hybrid ≥ BM25 baseline × 1.15
  - POST /api/rag/search?mode=hybrid 可调用
  - AI 分析结果含 citation 并可跳转 PDF
  - Ollama 离线时全链路降级为 BM25 不崩溃
  - 更新 README.UserAgentTest.md

---

## v0.4.4 Backlog（产品级 P0/P1，非财报路线图）

- **V044-P0-A**：Agent 推荐「完成」状态语义错乱（失败标完成）
- **V044-P0-B**：情绪轮动核心榜单全线「数据不可用」
- **V044-P1-C**：股票详情基本面 3 字段空
- **V044-P1-D**：主力净流入跨页数值矛盾
- **V044-P1-E**：Worker 运行中但日志面板 0 条
- **V044-P1-F**：失败态按钮文案「重新连接」→「再试一次」
- **V044-NIT**：财报单渠道 / 单位混乱 / 资讯英文未译 / 加载中常驻

---

## 历史归档

- v0.4.2 RAG Lite → 本看板"已完成 Sprint 摘要"
- v0.4.2N PDF 管线重构 → 本看板"已完成 Sprint 摘要"
- v0.4.2 必修 → 本看板"已完成 Sprint 摘要"
- v0.4.1 PDF 原件对照 → `/memories/repo/sprints/v041-pdf-compare-reparse.md`
- v0.4.0 财报中心基础设施 → `/memories/repo/sprints/v040-financial-center.md`
- v0.3.2 散户热度反向指标 → `/memories/repo/sprints/v0.3.2-retail-heat-contrarian.md`
- `tasks.json` 自 2026-04-22 起仅作 append-only 历史事件流
