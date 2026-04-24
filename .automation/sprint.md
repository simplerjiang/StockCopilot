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

---

## Backlog（技术债）

- ~~**V040-S3-FU-1**（中）：已在 v0.4.4 S5 修复。~~
- **V040-DEBT-4**（候选）：`StockSearchService` 无排序无市场过滤。
- **V041-DEBT-1**（中）：`FinancialDbContext` BsonMapper.Global 并发 race。
- **V041-DEBT-2**（低）：Api vs Worker RuntimePaths 不一致。
- **V041-DEBT-3**（中）：PdfFileDetail 缺 voting candidates 数组。

---

## v0.4.6 Sprint（多 Agent 路由与财报 RAG 闭环）

### Sprint 目标
**让简单 prompt 自动路由到正确取证链路，子 Agent 真正接上财报 RAG，最终回答有可验证证据。**

详细计划：[GOAL-v046-rag-agent-routing-evidence.md](../docs/GOAL-v046-rag-agent-routing-evidence.md)

| Story | 标题 | 分级 | 验收标准 | 状态 |
|---|---|---|---|---|
| S0 | 问题意图分类器 | M | 10 个测试问题 ≥ 80% 正确分类 | TODO |
| S1 | 路由决策表 | M | 路由覆盖所有意图类型 | TODO |
| S2 | 注册财报 RAG 为 MCP 工具 | M | 子角色工具列表包含 SearchFinancialReport | TODO |
| S3 | Research 子角色接入 RAG | M | Research 报告包含财报引用 | TODO |
| S4 | Recommend 子流程接入 RAG | M | 推荐报告含财报 citation | TODO |
| S5 | Evidence Pack 统一组装 | M | 3 条链路证据来源一致 | TODO |
| S6 | 估值问题强制取证 | M | 估值回答含 ≥1 条财报引用 | TODO |
| S7 | 结论格式标准化 | S | 输出含结论/依据/假设/引用 4 字段 | TODO |
| S8 | 全链路验收 | S | tests 全绿 + 5 个 E2E 问题验证 | TODO |

---

## 历史归档

- v0.4.5 数据质量与 Worker 稳定性 → 本看板"已完成 Sprint 摘要"
- v0.4.4 产品质量修复 → 本看板"已完成 Sprint 摘要"
- v0.4.2 RAG Lite → 本看板"已完成 Sprint 摘要"
- v0.4.2N PDF 管线重构 → 本看板"已完成 Sprint 摘要"
- v0.4.2 必修 → 本看板"已完成 Sprint 摘要"
- v0.4.1 PDF 原件对照 → `/memories/repo/sprints/v041-pdf-compare-reparse.md`
- v0.4.0 财报中心基础设施 → `/memories/repo/sprints/v040-financial-center.md`
- v0.3.2 散户热度反向指标 → `/memories/repo/sprints/v0.3.2-retail-heat-contrarian.md`
- `tasks.json` 自 2026-04-22 起仅作 append-only 历史事件流
