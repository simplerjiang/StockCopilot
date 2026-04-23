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

## v0.4.4 Sprint（产品质量修复）

### Sprint 目标
**修复 P0/P1 产品 Bug + 高优技术债清理，提升产品可用性和数据准确性。**

| Story | 来源 | 标题 | 分级 | 验收标准 | 状态 |
|---|---|---|---|---|---|
| S0 | V044-P0-A | Agent 推荐状态语义修复 | S | 失败/异常的推荐任务不标记为「完成」，显示正确的失败状态 | TODO |
| S1 | V044-P0-B | 情绪轮动数据恢复 | M | 情绪轮动核心榜单有数据展示，不显示「数据不可用」 | TODO |
| S2 | V044-P1-C | 股票详情基本面字段补全 | S | 股票详情页 3 个空字段有值显示 | TODO |
| S3 | V044-P1-D | 主力净流入跨页数值一致性 | M | 列表页与详情页主力净流入数值一致 | TODO |
| S4 | V044-P1-E | Worker 日志面板修复 | S | Worker 运行中时日志面板显示日志条目 | TODO |
| S5 | V044-P1-F+NIT | UX 文案与样式统一 | S | 失败态按钮「再试一次」；渠道 Tag 统一；单位格式化；资讯英文翻译；加载态收敛 | TODO |
| S6 | V040-S6-FU-1 | 财报毛利润补算 | S | 财报中心「毛利润」= 营业总收入 - 营业总成本，有值展示 | TODO |
| S7 | V040-S6-FU-2 | 财报 keyword 搜索 | M | 搜索框按代码/名称模糊匹配，返回正确结果 | TODO |
| S8 | — | 全链路验收 | S | dotnet test + vitest 全绿；浏览器验收关键页面 | TODO |

---

## 历史归档

- v0.4.2 RAG Lite → 本看板"已完成 Sprint 摘要"
- v0.4.2N PDF 管线重构 → 本看板"已完成 Sprint 摘要"
- v0.4.2 必修 → 本看板"已完成 Sprint 摘要"
- v0.4.1 PDF 原件对照 → `/memories/repo/sprints/v041-pdf-compare-reparse.md`
- v0.4.0 财报中心基础设施 → `/memories/repo/sprints/v040-financial-center.md`
- v0.3.2 散户热度反向指标 → `/memories/repo/sprints/v0.3.2-retail-heat-contrarian.md`
- `tasks.json` 自 2026-04-22 起仅作 append-only 历史事件流
