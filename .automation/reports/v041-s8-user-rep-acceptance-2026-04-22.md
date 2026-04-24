# v0.4.1 Sprint 用户代表验收报告（S8 最终验收）

- **日期**：2026-04-23
- **角色**：User Representative Agent（专业 A 股交易员视角）
- **环境**：Packaged Desktop（已 republish 含 NIT-3/NIT-4 修复），http://localhost:5119/
- **验收对象**：贵州茅台 600519，主报告 PDF id `69e8ec0e70628c01e811a422`（145 parseUnits，fieldCount=10）
- **工具**：DarBot Browser MCP

---

## 总判定：REJECT — 拒绝发布 v0.4.1

**总分：38 / 100**

NIT-3、NIT-4 两项关键回归并未真正修复，且新发现一个比 NIT-3 更严重的「财报中心默认选错 PDF」阻塞缺陷。以专业交易员视角看，v0.4.1 的核心价值主张「看原件 + 能对照」目前在两个入口都不能完成闭环，不可发布。

---

## 1. 五维度评分明细

| # | 维度 | 评分 | 主要扣分点 |
|---|------|------|-----------|
| 1 | 核心可达性（按钮入口） | 14 / 20 | 5 个按钮在股票信息财务报表 Tab 内确实清晰可见、可点击。但「查看 PDF 原件」点开后进入空态、「采集 PDF 原件」对已采集报告无视觉提示。 |
| 2 | 对照体验（双栏 + Tab + 页码联动） | 4 / 20 | 财报中心入口左栏 PDF 区域**整块灰白**（截图证实 iframe 未渲染任何内容），右栏要么报错要么暂无解析结果，**无法做任何字段对照**。无法测试页码联动。 |
| 3 | 数据透明度（投票信息 / 解析单元 / 字段数） | 8 / 20 | 投票信息面板有 `当前提取器/置信度/字段数/首次解析/最近重解析/解析错误`，结构合理；但因为对错了 PDF（摘要 fieldCount=0），交易员看到的全是「0 字段、解析失败」，反而**让人对系统失去信心**（实际主报告有 10 字段 145 parseUnits）。 |
| 4 | 闭环信任（NIT-3 / NIT-4） | 2 / 20 | 两个 NIT **都没真正修复**——详见第 2 节。 |
| 5 | 日常工具舒适度 | 10 / 20 | 财报中心列表筛选、列表渲染流畅，桌面端无明显卡顿；但只要走到 PDF 对照就崩，作为交易员的「看原件」工具不可用。 |

---

## 2. NIT-3 / NIT-4 修复验证（关键判断）

### NIT-3：股票详情 → 查看 PDF 原件 不再空态  ❌ **未修复**

- 操作：股票信息 → 选 sh600519 → 财务报表 Tab → 点「📄 查看 PDF 原件」
- 期望：Modal 内显示 600519 已采集的 3 份 PDF（至少主报告 145 parseUnits）
- 实际：Modal 标题为「PDF 原件 / 对照」，内部唯一可见内容是黄色 alert：
  > 该报告暂无 PDF 原件，请先触发「📥 采集 PDF 原件」
- 与之矛盾的事实：`GET /api/stocks/financial/pdf-files?symbol=600519&page=1&pageSize=10` 同时返回 3 条记录，主报告 fieldCount=10、parseUnits=145。
- 截图：[v041-nit3-stock-detail-empty.png](.automation/reports/artifacts/v041-nit3-stock-detail-empty.png)（空 Modal）

### NIT-4：重新解析后 lastReparsedAt 立即更新  ❌ **未修复**

- 操作：财报中心 → 600519 一季报 详情 → 投票信息 Tab → 点「重新解析」
- 期望：完成后右栏「最近重解析」字段刷新到当前时间
- 实际（等待 12s 后）：UI 仍显示旧值 `2026/4/22 23:46:42`
- 后端确实更新成功：`GET /api/stocks/financial/pdf-files/{id}` 返回 `lastReparsedAt: 2026-04-23T00:10:49`
- 结论：**后端正确，前端不刷新**，闭环信号缺失。NIT-4 fix 在该入口未生效。

---

## 3. 真实交易员场景模拟结论

**目标流程**：在结构化财报中找到「营业收入 172,054,171,890.91」→ 打开 PDF 原件 → 跳到对应表格页 → 肉眼核对。

**实际走到第 2 步即断**：

- **入口 A（股票信息财务报表 Tab）**：Modal 直接空态，无任何可对照内容。流程在第 1 步就断了。
- **入口 B（财报中心详情）**：dialog 打开了，但：
  - 自动选中的是「贵州茅台2025年年度报告**摘要**.pdf」(fieldCount=0)，而不是有 145 parseUnits 的「贵州茅台2025年年度报告.pdf」主报告；
  - 左栏 iframe 整块**灰白**，PDF 没渲染（截图证实）；
  - 没有任何 PDF 切换器（picker），交易员无法手动选到正确的 PDF；
  - 右栏「解析单元」Tab 显示「解析结果加载失败 / PDF 文本中未找到可解析的财务数据」。
- **结论**：当前版本**无法支持任何字段—原文核对**操作。这正是 v0.4.1 被定义为「v0.4 关键修复版本」的核心场景，全军覆没。

---

## 4. 新发现问题清单（按严重度）

### BLOCKER（阻断发布）

1. **B1 — 财报中心 dialog 默认选中错误 PDF（新发现，比 NIT-3 更严重）**
   - 600519 一季报关联 3 个 PDF：主报告（id `..a422`，fieldCount=10、parseUnits=145）、英文版（`..a423`，0/0）、摘要（`..a424`，0/0）。
   - 财报中心 dialog 自动选了「摘要」(a424)，让用户看到「字段=0 / 解析失败」，**完全误导**对解析质量的判断。
   - dialog 没有 PDF 选择器，无法手动换到主报告。
   - 修复建议：默认按 fieldCount/parseUnits 数量降序选 PDF；若仍多份则在右栏顶部加 PDF 切换 tab/select。

2. **B2 — 财报中心 dialog 内 PDF 预览 iframe 完全不渲染（截图证实左栏全灰）**
   - 即便选错了 PDF，iframe 也应能渲染 PDF 二进制；目前完全空白，怀疑 iframe `src` URL 错误或后端 `/api/stocks/financial/pdf-files/{id}/raw` 类接口未返回 PDF 二进制。
   - 需要 Dev 排查 iframe src + 网络请求。

3. **B3 — NIT-3 未真正修复**
   - 股票详情 → 查看 PDF 原件 仍是空态。详见第 2 节。

4. **B4 — NIT-4 未真正修复**
   - 重新解析后前端 lastReparsedAt 不刷新。详见第 2 节。

### MAJOR

5. **M1 — 「重新解析」按钮无 loading 状态**
   - 点击后按钮态无变化，用户不知道是否点中、是否在跑。需加 disabled+spinner，并在完成后做 toast 反馈。

6. **M2 — stageLogs 5 阶段时间线 UI 缺失**
   - 投票信息面板只展示了汇总（提取器/置信度/字段数/首次/最近重解析/错误），并未将 5 阶段（download/extract/vote/parse/persist）以可视化时间线展示。这是 sprint 完成定义第 4 项「能定位失败层级」的核心，目前只能算接口层 OK，UI 接入未完成。

### MINOR

7. **m1 — Modal 错误提示让用户做错操作**
   - NIT-3 空态 alert 写「请先触发『📥 采集 PDF 原件』」，但 600519 实际已采集 3 份。若用户照做会重复触发采集。

8. **m2 — 财报中心 dialog 摘要文件名展示在标题栏，但下方真实 PDF 渲染区为空，文字与画面矛盾**

### NIT

9. **n1 — 财务表格基础数据可疑（v0.4.0 已知问题，仅记录不计分）**
   - 财务报表 Tab 表格中 600519「营业收入 1688.38万 / 净利润 853.10万」明显单位错误（实际亿级），但属于 v0.4.0 范围的财务摘要展示问题，不计入 v0.4.1 评分。

---

## 5. 接口层验证回顾（§9.2 硬约束）

直接 API 调用结果，以下两项**通过**：

| 检查 | 结果 |
|------|------|
| `pdf-files` 列表三字段（extractor/voteConfidence/fieldCount）非空 | ✅ 3/3 |
| 主报告 parseUnits 数量 = 145 | ✅ |
| stageLogs 5 阶段返回完整 | ✅ |
| `lastReparsedAt` 在后端正确更新 | ✅ |

接口层质量没有问题。问题全部在前端/UI 集成。

---

## 6. 可发布的下一步建议

按优先级（v0.4.2 必修）：

1. 修 B1：dialog 默认 PDF 选择策略 + PDF 切换器
2. 修 B2：iframe src 排查
3. 修 B3：股票详情 → 查看 PDF 原件 真正连上 pdf-files 列表
4. 修 B4：reparse 完成后调用 PDF 详情接口刷新 lastReparsedAt
5. 修 M1：重新解析按钮 loading + toast
6. 接 M2：stageLogs 5 阶段 timeline UI 落到 ComparePane（V041-S8 范围）

完成后再走一次本验收脚本。

---

## 附录：测试足迹

- 主流程：股票信息 → 600519 → 财务报表 Tab → 查看 PDF 原件 ❌
- 主流程：财报中心 → 600519 一季报 → 详情 → 查看 PDF / 重新解析 ❌
- API 直查：`/api/stocks/financial/pdf-files?symbol=600519` ✅
- API 直查：`/api/stocks/financial/pdf-files/{id}` ✅
- 截图：`v041-nit3-stock-detail-empty.png`、`v041-fc-wrong-pdf-empty-parse.png`（已临时保存到 darbot 输出目录）

---

# v0.4.1 Sprint User Representative Acceptance Report (S8 Final, English)

- **Date**: 2026-04-23
- **Verdict**: **REJECT** — Total: **38 / 100**
- **Reason**: Both NIT-3 (stock detail PDF empty state) and NIT-4 (lastReparsedAt UI refresh) are NOT actually fixed in this republish. A new BLOCKER discovered: financial-center dialog auto-picks the WRONG PDF (summary, fieldCount=0) instead of the main report (145 parseUnits, fieldCount=10), and the embedded PDF iframe renders completely blank.

## Score breakdown

| Dimension | Score | Notes |
|-----------|-------|-------|
| 1. Reachability of 5 buttons | 14 / 20 | Buttons visible, but viewing PDF lands in empty state. |
| 2. Side-by-side compare experience | 4 / 20 | Left iframe blank; right pane shows parse error. Cannot do any field cross-check. |
| 3. Data transparency | 8 / 20 | Voting panel structure is fine, but values shown reflect wrong PDF, misleading the user. |
| 4. Closed-loop trust (NIT fixes) | 2 / 20 | NIT-3 and NIT-4 both still broken. |
| 5. Daily-tool comfort | 10 / 20 | Listing/filtering OK; but core PDF compare flow is unusable. |

## NIT verification

- **NIT-3 NOT fixed**: stock detail Modal shows "该报告暂无 PDF 原件" alert despite 3 PDFs available in backend.
- **NIT-4 NOT fixed**: After reparse, backend updates `lastReparsedAt` to `2026-04-23T00:10:49` but frontend still shows old `2026-04-22 23:46:42`.

## Trader-scenario simulation

Goal: cross-check `Operating Revenue 172,054,171,890.91` between structured table and PDF original.
Result: **Flow blocked at step 2** in BOTH entry points (stock detail Modal empty; financial-center dialog wrong PDF + blank iframe).

## New issues

- **BLOCKER**:
  - B1 financial-center dialog defaults to wrong PDF (summary instead of main report) with no picker
  - B2 PDF iframe renders blank in financial-center dialog
  - B3 NIT-3 not fixed
  - B4 NIT-4 not fixed
- **MAJOR**:
  - M1 reparse button has no loading/feedback state
  - M2 stageLogs 5-phase timeline UI not integrated into ComparePane
- **MINOR / NIT**: misleading empty-state copy; financial table units off (v0.4.0 known issue, excluded from scoring).

## Decision

**REJECT v0.4.1**. Backend (§9.2 contract) is solid — the regression and unfixed bugs all live in the frontend integration. v0.4.2 must address B1–B4 + M1, then re-run this acceptance.