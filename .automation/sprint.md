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

---

## Backlog（技术债）

- ~~**V040-S3-FU-1**（中）：已在 v0.4.4 S5 修复。~~
- **V040-DEBT-4**（候选）：`StockSearchService` 无排序无市场过滤。
- **V041-DEBT-1**（中）：`FinancialDbContext` BsonMapper.Global 并发 race。
- **V041-DEBT-2**（低）：Api vs Worker RuntimePaths 不一致。
- **V041-DEBT-3**（中）：PdfFileDetail 缺 voting candidates 数组。

---

## v0.4.5 Sprint（数据质量与 Worker 稳定性）

### Sprint 目标
**修复财报数值单位不一致、cninfo 采集失败、Worker 自动关停问题，增加财报中心采集能力。**

详细计划：[GOAL-v045-data-quality-and-worker-stability.md](../docs/GOAL-v045-data-quality-and-worker-stability.md)

| Story | 来源 | 标题 | 分级 | 验收标准 | 状态 |
|---|---|---|---|---|---|
| S0 | Bug#1 | 财报数值单位审计与修复 | M | 同公司同年度不同渠道数值一致（<1%差异） | TODO |
| S1 | Bug#5 | Worker 自动重启机制 | M | 异常退出 30s 内重启，连续失败 3 次告警 | TODO |
| S2 | Bug#2 | cninfo PDF 采集修复 | M | PDF 下载成功率 ≥ 80% | TODO |
| S3 | Bug#3 | 采集中心日期选择器修复 | S | 日期下拉可见可选 | TODO |
| S4 | Bug#4 | 财报中心集成采集面板 | L | 财报中心可直接触发采集 | TODO |
| S5 | — | 全链路验收 | S | dotnet test + vitest 全绿 + E2E 验证 | TODO |

---

## 历史归档

- v0.4.4 产品质量修复 → 本看板"已完成 Sprint 摘要"
- v0.4.2 RAG Lite → 本看板"已完成 Sprint 摘要"
- v0.4.2N PDF 管线重构 → 本看板"已完成 Sprint 摘要"
- v0.4.2 必修 → 本看板"已完成 Sprint 摘要"
- v0.4.1 PDF 原件对照 → `/memories/repo/sprints/v041-pdf-compare-reparse.md`
- v0.4.0 财报中心基础设施 → `/memories/repo/sprints/v040-financial-center.md`
- v0.3.2 散户热度反向指标 → `/memories/repo/sprints/v0.3.2-retail-heat-contrarian.md`
- `tasks.json` 自 2026-04-22 起仅作 append-only 历史事件流
