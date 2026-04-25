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
| v0.4.3 | 2026-04-24 | S0–S9 全部 DONE | — | Hybrid Retrieval + AI 集成 |
| v0.4.4 | 2026-04-24 | S0–S8 + HF-1 全部 DONE | 1158 tests, 0 fail | 推荐状态修复 / 情绪轮动恢复 / 基本面补全 / SQLite 稳定性 |
| v0.4.5 | 2026-04-24 | S0–S5 全部 DONE | 1158 tests, 0 fail | 数值单位修复 / Worker 重启 / cninfo headers / 采集面板集成 |
| v0.4.6 | 2026-04-24 | S0–S8 + HF-1 + HF-2 全部 DONE | — | 多 Agent 路由 / 财报 RAG 闭环 / LiveGate 合成 / 散户热度图表 / cninfo 修复 / 论坛重试 |
| v0.4.7 | 2026-04-24 | S1–S5 全部 DONE | 768+466 tests, 0 fail | AI 分析 JSON 修复 / 公告 PDF 爬取+RAG / MCP 注册 |

---

## Backlog（技术债）

- ~~**V040-S3-FU-1**（中）：已在 v0.4.4 S5 修复。~~
- **V040-DEBT-4**（候选）：`StockSearchService` 无排序无市场过滤。
- **V041-DEBT-1**（中）：`FinancialDbContext` BsonMapper.Global 并发 race。
- **V041-DEBT-2**（低）：Api vs Worker RuntimePaths 不一致。
- **V041-DEBT-3**（中）：PdfFileDetail 缺 voting candidates 数组。

---

## v0.4.6 Sprint（多 Agent 路由与财报 RAG 闭环）✅ 已归档

### Sprint 目标
**让简单 prompt 自动路由到正确取证链路，子 Agent 真正接上财报 RAG，最终回答有可验证证据。**

详细计划：[GOAL-v046-rag-agent-routing-evidence.md](../docs/GOAL-v046-rag-agent-routing-evidence.md)

| Story | 标题 | 分级 | 验收标准 | 状态 |
|---|---|---|---|---|
| S0 | 问题意图分类器 | M | 10 个测试问题 ≥ 80% 正确分类 | DONE |
| S1 | 路由决策表 | M | 路由覆盖所有意图类型 | DONE |
| S2 | 注册财报 RAG 为 MCP 工具 | M | 子角色工具列表包含 SearchFinancialReport | DONE |
| S3 | Research 子角色接入 RAG | M | Research 报告包含财报引用 | DONE |
| S4 | Recommend 子流程接入 RAG | M | 推荐报告含财报 citation | DONE |
| S5 | Evidence Pack 统一组装 | M | 3 条链路证据来源一致 | DONE |
| S6 | 估值问题强制取证 | M | 估值回答含 ≥1 条财报引用 | DONE |
| S7 | 结论格式标准化 | S | 输出含结论/依据/假设/引用 4 字段 | DONE |
| S8 | 全链路验收 | S | tests 全绿 + 5 个 E2E 问题验证 | DONE |

### 验收记录
- **一轮验收（User Rep）**：不通过 — LiveGate 返回计划草案而非真实分析
- **P0-1 修复**：添加 SynthesizeAnalysisAsync 第二次 LLM 调用（commit `bf67a8c`）
- **二轮验收（User Rep）**：通过 ✅ — B+ 评级，PE/PB/ROE 等真实数据，结构化分析
- **遗留项**：FinancialTrendMcp LiveGate 适配、RAG 在部分场景失败、PE/PB 精确计算
- **HF-2 修复**：LiveGate 添加 FinancialReport/FinancialTrend/FinancialReportRag 工具 dispatch（commit `b3bfe09`）
- **验证**：FinancialTrendMcp completed, PE≈22.06x, PB≈7.41x, 0 个"暂不支持"错误

---

## v0.4.7 Sprint（AI 分析质量 + 公告 PDF RAG 扩展）

### Sprint 目标
**修复 AI 分析页面 JSON 渲染问题；爬取东方财富个股公告 PDF 扩展 RAG 数据库。**

| Story | 标题 | 分级 | 验收标准 | 状态 |
|---|---|---|---|---|
| S1 | AI 分析 JSON 渲染修复 | S | AI 分析页面不再直接输出原始 JSON，嵌套 JSON 也能正确转义渲染 | DONE |
| S2 | 东方财富公告 PDF 爬取 | M | 盘中消息带中"东方财富网公告"的 PDF 能自动下载入库 | DONE |
| S3 | 公告 PDF RAG 入库 | M | 下载的公告 PDF 经过 embedding 进入 RAG 数据库，可被检索 | DONE |
| S4 | 公告 RAG MCP 工具注册 | M | LLM 能通过 MCP 工具检索公告内容，回答引用公告来源 | DONE |
| S5 | 全链路验收 | S | AI 分析无 JSON 泄漏 + 公告 RAG 可检索 + tests 全绿 | DONE |

### 验收记录
- **S1 验收**：后端 StripMarkdownCodeFences + 前端 stripCodeFence/markdownToSafeHtml 增强，11+466 tests 通过
- **S2 验收**：AnnouncementPdfCollector 双层 PDF URL 策略（直接构造+HTML 解析），90 tests 通过，目录遍历修复
- **S3 验收**：AnnouncementPdfProcessor PdfPig 提取+段落切块+jieba 分词，source_type='announcement'，106 tests 通过
- **S4 验收**：SearchAnnouncementRag MCP 注册到 Research/Recommend/LiveGate，sourceType 过滤+citation Source 修复，662 tests 通过
- **S5 全链路**：768 后端 + 466 前端测试全绿，编译零错误，全链路贯通

---

---

## v0.4.8 Sprint（回归测试 111 条 Bug 修复 - 交易员可信度底线）

### Sprint 目标

**2026-04-24 两轮回归测试累计发现 111 条活跃 Bug（10+ Blocker）。本 Sprint 先修 3 条最致命、直接阻断交易员信任的 Blocker 组；其余降级为 Backlog。** 详情见 [.automation/buglist.md](buglist.md)。

### Sprint 规则

- 本 Sprint 为 L 级（Dev → Test → UI Designer → User Rep 双轮验收 → 写双语报告）
- 每 Story 完成后立即 Test 验证才进下一 Story
- 任一 Story 触发新 Blocker 立即停摆，先修新 Blocker

### 活跃 Story（上限 3）

| Story | 标题 | 分级 | 覆盖 Bug | 验收标准 | 状态 |
|---|---|---|---|---|---|
| V048-S1 | 交易账务闭环一致性 | L | #88/#89/#90/#91/#92 | 持仓账务 `成本+浮盈=市值` 自洽；交易流水→持仓→可用资金三者联动；0 笔交易健康度显示 N/A 而非 100%；持仓行点击 drill-down 跳回股票信息并自动搜索；快速录入代码→名称自动补齐 | TODO |
| V048-S2 | 核心稳定性四件套 | L | #71/#78/#82/#85 | `/api/*` 未命中路径返 404 而非 SPA index.html（监控恢复可见性）；`/api/market/sync` 加 Semaphore + 429 throttle 避免并发 30s 全超时；股票推荐 SSE 切页回来自动按 sessionId 续上（或显著 reconnect 提示）；单角色运行 >3 分钟或 >工具上限 80% 时显示 ETA 警示与一键终止 | TODO |
| V048-S3 | 财报数据语义完整 | L | #94/#95/#96/#107/#80 | PDF 来源标签与详情抽屉一致（有 PDF 才标 PDF）；茅台一季报明确标"累计(YTD)"或转为单季口径；关键字段缺失时不标"PDF 解析成功"；Embedding 能力不可用时股票信息/财报中心页主动显示降级横幅；PDF Q1 补齐"资产总计/负债总计"字段 | TODO |

### Backlog（本 Sprint 不做，P1 优先）

> **已关闭（WONT-FIX）**：#111 治理 Trace 脱敏撒谎 / #112 Dev Mode 一键开启无审计 / #63 SQL 注入载荷回显 lastUserIntent。原因：本地桌面单用户环境，prompt/追问直出即预期行为，不做文案修正、不做脱敏实现、不做开关门禁。#110 隔离策略失效保留为独立功能 Debt。

- **V048-DEBT-3**（P1 Blocker）：#97 平安银行公告被绑 sh000001 上证指数 symbol
- **V048-DEBT-5**（P1 Major）：#65/#68 symbol 归一 `sh600519` vs `600519` 跨接口不一致
- **V048-DEBT-6**（P1 Major）：#66/#67/#22 Research 写端点 405 + 历史 47 条全 Failed
- **V048-DEBT-7**（P1 Major）：#72 归档清洗 runId=1 卡 round_budget_reached 自动调度器失效
- **V048-DEBT-8**（P2 Major）：#69 RAG hybrid 退化为 bm25 即便 Ollama 在线
- **V048-DEBT-9**（P2 Major）：#76 Recommend lastUserIntent 中文 UTF-8/GBK 双重乱码
- **V048-DEBT-10**（P2 Minor）：#101/#55 北向资金 3 处口径打架
- **V048-DEBT-11**（P2 Minor）：#44/#103/#104 盘中消息带重复 + 凌晨时间戳无盘后标签
- **V048-DEBT-12**（P2 Minor）：#45 最近查询 `invalid/invalid/0%` 垃圾条目 + 无清理入口
- **V048-DEBT-13**（P3 Minor）：#48/#49 侧栏"交易计划""全局总览"Tab 空壳
- **V048-DEBT-14**（P3 Minor）：#50 推荐历史近 20 条 15 失败 4 降级 仅 1 完成
- **V048-DEBT-15**（P3 Minor）：#37/#11 LLM 幻觉标签"无荒隔靶点" + AI 标的打错
- **V048-DEBT-16**（P3 Minor）：手册缺口 4 条（财报单位断言、情绪轮动完整性、资讯标签一致、交易日志卡片收敛）
- **V048-DEBT-17**（P4 其余 60+ 条）：待 S1-S3 完成后分批排期

## 历史归档

- v0.4.6 多 Agent 路由与财报 RAG 闭环 → 本看板"已完成 Sprint 摘要"
- v0.4.5 数据质量与 Worker 稳定性 → 本看板"已完成 Sprint 摘要"
- v0.4.4 产品质量修复 → 本看板"已完成 Sprint 摘要"
- v0.4.2 RAG Lite → 本看板"已完成 Sprint 摘要"
- v0.4.2N PDF 管线重构 → 本看板"已完成 Sprint 摘要"
- v0.4.2 必修 → 本看板"已完成 Sprint 摘要"
- v0.4.1 PDF 原件对照 → `/memories/repo/sprints/v041-pdf-compare-reparse.md`
- v0.4.0 财报中心基础设施 → `/memories/repo/sprints/v040-financial-center.md`
- v0.3.2 散户热度反向指标 → `/memories/repo/sprints/v0.3.2-retail-heat-contrarian.md`
- `tasks.json` 自 2026-04-22 起仅作 append-only 历史事件流
