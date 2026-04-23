# Sprint 看板

> 当前 Sprint 最多 3 个活跃 Story。完成的 Story 归档到 `/memories/repo/sprints/`。

## 看板规则（2026-04-22 新增）

1. **NIT 不延后**：Story 复核暴露的 NIT，须在复核后 1 小时内修完并补回测试，不允许累计到 Sprint 末尾。
2. **BLOCKER 立即停摆**：任何 BLOCKER 阻塞当前 Story 进入下一个；先修 BLOCKER，再继续。
3. **预存在测试失败必须立项**：复核中发现非本 Story 引入的失败用例，须开独立技术债 Story 跟踪，不允许"已知失败"长期挂在主干。
4. **`tasks.json` 与 `sprint.md` 关系**：`sprint.md` 是当前 Sprint 唯一权威看板；`tasks.json` 仅作历史任务事件流（append-only），不再用作活跃任务管理。

---

## v0.4.2 Sprint 必修（已完成 2026-04-23）

> 全部在 v0.4.1 R3 修复周期解决，User Rep R3 95/100 PASS。

| ID | 优先级 | 状态 | 说明 |
|----|--------|------|------|
| V042-P0-A | BLOCKER | ✅ DONE | `smartPickPdfId()` fieldCount 优先 + 摘要排除 + 手动切换 |
| V042-P0-B | BLOCKER | ✅ DONE | 移除 X-Frame-Options + 绝对 URL + application/pdf |
| V042-P0-C | BLOCKER | ✅ DONE | publish 脚本同步 dist + wwwroot |
| V042-P0-D | BLOCKER | ✅ DONE | 同 P0-C |
| V042-P1-A | MAJOR | ✅ DONE | reparsing ref + spinner + disabled 联动 |
| V042-P1-B | MAJOR | ✅ DONE | StageTimeline「解析阶段」tab 集成 |
| V042-P2-A | P2 | ✅ DONE | publish 脚本已修复前端构建+双路径同步 |

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

## v0.4.2N Sprint（PDF 管线重构 + 财报数据修复）

### Sprint 目标
**重构 PDF 管线为全文存储 + 透明化解析过程，修复 4 个人工测试 Bug，统一价格显示规范。**

### 已完成（BUG 修复）
- ✅ V043-P0-A (BUG-2)：ths `ConvertWanToYuan` 统一为元
- ✅ V043-P1-B (BUG-4)：emweb 3 轮迭代拉取覆盖 ~4 年
- ✅ V043-P1-C (BUG-1)：前端检查 `downloadedCount`，为 0 展示原因
- ✅ V043-P1-D (BUG-3)：展示采集结果摘要

### Stories

#### V042-NS1: PDF 全文持久化（后端）
- **状态**：DONE | **级别**：M
- **验收**：PdfFileDocument.FullTextPages 持久化 + API detail 接口返回每页文本。Worker 41/0/0, Api 617/0/0。

#### V042-NS2: 前端价格显示统一缩写
- **状态**：DONE | **级别**：S
- **验收**：`formatMoneyDisplay()` 统一缩写 + title tooltip。Drawer + ReportTab 覆盖。vitest 452/0/2。

#### V042-NS3: PDF 下载即保存记录（后端）
- **状态**：DONE | **级别**：S
- **验收**：download 阶段后立即 UpsertPdfFileDocumentStub，解析失败仍可见。Worker 41/0/0, Api 616/0/0。

#### V042-NS4: 对照面板解析单元实际内容展示
- **状态**：DONE | **级别**：M
- **验收**：PdfParseUnit 增 ExtractedText+ParsedFields，前端折叠展示。Worker 43/0, Api 617/0, vitest 456/0。

#### V042-NS5: 投票透明化（前端+后端）
- **状态**：DONE | **级别**：M
- **验收**：VotingCandidates+VotingNotes 持久化，VotingPanel 展示提取器对比+胜出/失败标记。vitest 460/0。

#### V042-NS6: 解析阶段折叠面板 + 进度日志
- **状态**：DONE | **级别**：S
- **验收**：StageLog 增 Details 字段，StageTimeline 改 <details> 折叠面板。Worker 44/0, Api 617/0, vitest 463/0。

#### V042-NS7: cninfo PDF 采集成功率提升
- **状态**：DONE | **级别**：M
- **验收**：修复 column 参数（上海 sse/深圳 szse）+ 增加 Q1/Q3 季报 + 重试机制。Worker 44/0/0。实际采集率需盘中验证。

#### V042-NS8: v0.4.3 全链路验收
- **状态**：DONE | **级别**：M | **依赖**：NS1~NS7
- **验收**：Test Agent PASS（Worker 44/0, Api 617/0, vitest 465/0/2）；UI Designer 90/100 CONDITIONAL→BLOCKER 修复后 PASS；README.UserAgentTest.md 已更新 9 步验收流程。

---

## v0.4.2 Sprint（财报 RAG Lite）

### Sprint 目标
**实现财报 Lite RAG：PDF 正文切块入库 + BM25 检索 + REST 查询接口。不引入 SK/KM/Python。**

### 核心约束（GOAL-v042 §16）
- 存储：SQLite + FTS5（`financial-rag.db`），与 LiteDB 物理隔离
- 分词：jieba.NET 预切词 + 空格 join
- 切块：三层（H1/H2/H3 标题 → 段落兜底 512-800 字 → 表格独立通道）
- 不引入 SK / KM / Python sidecar
- schema 预留 source_type 枚举位（v0.4.2 只写 financial_report）

### Stories

#### V042-S1: SQLite RAG 存储层
- **状态**：TODO | **级别**：M
- **描述**：在 FinancialWorker 中创建 `financial-rag.db`，包含 `chunks` 表 + FTS5 虚表。使用 `Microsoft.Data.Sqlite`。Schema 按 §16.2 含 chunk_id/source_type/source_id/symbol/report_date/report_type/section/block_kind/page_start/page_end/text/created_at。
- **验收**：Worker 启动时自动创建 DB + 表；dotnet test 验证 CRUD。

#### V042-S2: jieba.NET 中文分词集成
- **状态**：TODO | **级别**：S
- **描述**：引入 jieba.NET NuGet 包，创建 `ChineseTokenizer` 服务。入库时将中文文本预分词（jieba cut）→ 空格 join 后写入 FTS5。
- **验收**：分词服务能对中文财报文本正确切词；与 FTS5 配合实现中文 BM25 检索。

#### V042-S3: IChunker 切块服务
- **状态**：TODO | **级别**：M | **依赖**：S1, S2
- **描述**：实现 `IChunker.Chunk(PdfFileDocument doc)` → `List<FinancialChunk>`。三层策略：(1) H1/H2/H3 标题切；(2) 超 800 字段落兜底切（512-800 字，80 字重叠）；(3) 表格独立存 JSON，prose 中保留指针。
- **验收**：给定一份多章节财报文本，输出正确的 chunks 列表；每 chunk 含 section/page_start/page_end/block_kind。

#### V042-S4: Pipeline 自动切块入库
- **状态**：TODO | **级别**：M | **依赖**：S3
- **描述**：在 `PdfProcessingPipeline.ProcessSinglePdfAsync` 的 persist 阶段后，调用 IChunker 切块并写入 `financial-rag.db`。Reparse 时先删除旧 chunks 再插入新 chunks。
- **验收**：PDF 解析完成后 chunks 表有记录；reparse 后 chunks 更新。

#### V042-S5: IRetriever 检索服务
- **状态**：TODO | **级别**：M | **依赖**：S1, S2
- **描述**：实现 `IRetriever.RetrieveAsync(query, symbol?, reportDate?, reportType?, topK)` → `List<RetrievedChunk>`。使用 FTS5 MATCH + bm25() 排序 + metadata WHERE 过滤。
- **验收**：中文查询能命中相关 chunks；metadata 过滤正确；返回含 score/page_start/page_end/section。

#### V042-S6: REST API endpoint
- **状态**：TODO | **级别**：S | **依赖**：S5
- **描述**：在主 API 增加 `POST /api/financial/rag/search`，入参 `{ query, symbol?, reportDate?, reportType?, topK }`，返回带 score 的 chunk 列表。
- **验收**：curl 可调用；返回格式含 chunk_id/text/section/page_start/page_end/score。

#### V042-S7: IEmbedder 占位接口
- **状态**：TODO | **级别**：S
- **描述**：定义 `IEmbedder` 接口 + 空实现 `NoOpEmbedder`。v0.4.3 启用 sqlite-vec 时接入实际 embedding。
- **验收**：接口定义存在；空实现注册到 DI；不影响现有功能。

#### V042-S8: v0.4.2 全链路验收
- **状态**：TODO | **级别**：M | **依赖**：S1~S7
- **验收标准**：
  - dotnet test + vitest 全绿
  - `financial-rag.db` 在数据目录下自动创建
  - POST /api/financial/rag/search 可调用并返回结果
  - 中文查询召回率人工抽测 ≥ 50%（10 条 query）
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

- v0.4.1 PDF 原件对照 → `/memories/repo/sprints/v041-pdf-compare-reparse.md`
- v0.4.0 财报中心基础设施 → `/memories/repo/sprints/v040-financial-center.md`
- v0.3.2 散户热度反向指标 → `/memories/repo/sprints/v0.3.2-retail-heat-contrarian.md`
- `tasks.json` 自 2026-04-22 起仅作 append-only 历史事件流
