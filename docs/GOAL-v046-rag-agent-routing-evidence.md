# GOAL v0.4.6 — 多 Agent 路由与财报 RAG 闭环

> 形成时间：2026-04-24
> 目标：让简单 prompt 自动路由到正确取证链路，子 Agent 真正接上财报 RAG，最终回答有可验证证据。

## 1. 背景

当前系统已具备：
- 多 Agent / 多角色路由能力（Research / Recommend / LiveGate）
- 子角色主动取数工具（MCP Gateway / LocalFact / 外部搜索）
- 财报 RAG 基础层（v0.4.2-v0.4.3 已构建）：
  - SQLite FTS5 + BM25 全文检索
  - Embedding 向量存储 + Ollama Embedder
  - Hybrid Retriever（BM25 + Vector + RRF）
  - RagContextEnricher 证据组装
  - Citation UI 组件（前端）
  - REST API（/api/rag/search, /api/rag/context）

但尚未形成闭环：
- 简单 prompt 不一定稳定进入财报分析链路
- 子角色取数偏向新闻/市场/概况，未接上财报 embedding 证据
- 回答阶段没有"必须附财报证据"的约束

## 2. 核心缺口

| 编号 | 缺口 | 当前状态 | 目标 |
|------|------|---------|------|
| G1 | 统一意图路由 | 各链路独立路由，无统一入口 | 简单问题自动判断进入正确链路 |
| G2 | 财报 RAG → Agent 工具 | RagContextEnricher 存在但未注册为 MCP 工具 | 子角色可通过工具调用财报 RAG |
| G3 | Evidence Pack | 各角色自行拼零散工具结果 | 统一证据包（财报+指标+新闻+引用） |
| G4 | 估值强制取证 | 裸 LLM 可直接回答估值问题 | 估值/风险类问题必须先取证再回答 |

## 3. 开发计划

### Phase 1: 意图路由层

**S0: 问题意图分类器**
- 输入：用户问题 + 当前股票上下文
- 输出：意图类型（估值/风险/财报解释/业绩归因/通用/追问）
- 实现：基于规则 + LLM fallback
- 验收：10 个测试问题 ≥ 80% 正确分类

**S1: 路由决策表**
- 意图 → 链路映射：哪类问题进 Research / Recommend / LiveGate / 直接回答
- 哪类问题必须触发财报检索
- 哪类问题需要追问
- 验收：路由决策覆盖所有意图类型

### Phase 2: 财报 RAG 接入 Agent

**S2: 注册财报 RAG 为 MCP 工具**
- 在 McpToolGateway 中注册 `SearchFinancialReportAsync` 工具
- 调用 RagContextEnricher（已存在 /api/rag/context）
- 支持 symbol + query + topK 参数
- 返回证据块 + 引用元数据
- 验收：子角色工具列表包含 SearchFinancialReport

**S3: Research 子角色接入 RAG**
- ResearchRoleExecutor 的角色工具序列中添加 FinancialReportSearch
- 对估值/风险/财报类意图的角色，PreferredMcpSequence 包含 RAG 工具
- 验收：Research 分析报告包含财报引用

**S4: Recommend 子流程接入 RAG**
- RecommendToolDispatcher 添加财报 RAG 工具调用
- 对涉及基本面的推荐，自动调取财报证据
- 验收：推荐报告 citations 包含财报来源

### Phase 3: 证据约束与输出

**S5: Evidence Pack 统一组装**
- 创建 EvidencePackBuilder 服务
- 输入：symbol, query, intentType, reportPeriod
- 输出：{ ragChunks, financialMetrics, localFacts, searchResults, citations }
- Research/Recommend/LiveGate 统一使用
- 验收：3 条链路的证据来源一致

**S6: 估值问题强制取证**
- 意图路由判定为"估值"时，必须先执行 EvidencePack
- 无证据时降级策略：告知用户"需要先采集该股票财报"
- 验收：估值问题回答必包含 ≥1 条财报引用

**S7: 结论格式标准化**
- 对估值/风险类问题，输出格式包含：结论 / 依据 / 假设 / 引用来源
- LLM prompt 模板添加结构化输出约束
- 验收：输出包含 4 个结构化字段

### Phase 4: 验收

**S8: 全链路验收**
- dotnet test + vitest 全绿
- 5 个真实问题 E2E 测试
- Research/Recommend/LiveGate 均可触发 RAG
- Citation 显示正确

## 4. 关键文件参考

| 模块 | 文件 |
|------|------|
| Research Runner | backend/.../Services/ResearchRunner.cs |
| Research Role Executor | backend/.../Services/ResearchRoleExecutor.cs |
| Recommend Tool Dispatcher | backend/.../Services/Recommend/RecommendToolDispatcher.cs |
| LiveGate | backend/.../Services/StockCopilotLiveGateService.cs |
| MCP Tool Gateway | backend/.../Services/Mcp/McpToolGateway.cs |
| RAG Context Enricher | backend/.../Services/RagContextEnricher.cs |
| Hybrid Retriever | backend/FinancialWorker/Services/Rag/HybridRetriever.cs |
| RagDbContext | backend/FinancialWorker/Data/RagDbContext.cs |
| Citation UI | frontend/src/modules/financial/RagCitationChip.vue |
