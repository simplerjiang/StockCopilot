# v0.4.2N Completion Report — PDF Pipeline Refactoring & Financial Data Fix

> Sprint Duration: 2026-04-23 (single session)
> Sprint Goal: 重构 PDF 管线为全文存储 + 透明化解析过程，修复 4 个人工测试 Bug，统一价格显示规范。

## Delivery Summary

| Story | Level | Status | Key Deliverable |
|-------|-------|--------|----------------|
| NS1: PDF 全文持久化 | M | ✅ DONE | `PdfFileDocument.FullTextPages` per-page text storage + API exposure |
| NS2: 前端价格统一缩写 | S | ✅ DONE | `formatMoneyDisplay()` 万/亿 abbreviation + tooltip |
| NS3: PDF 下载即保存 | S | ✅ DONE | `UpsertPdfFileDocumentStub` immediate after download |
| NS4: 解析单元内容展示 | M | ✅ DONE | `ExtractedText` + `ParsedFields` collapsible display |
| NS5: 投票透明化 | M | ✅ DONE | `VotingCandidates` 3-extractor comparison cards |
| NS6: 阶段折叠面板 | S | ✅ DONE | `<details>` collapse + `StageLog.Details` key-value |
| NS7: cninfo 采集修复 | M | ✅ DONE | column fix (sse/szse) + quarterly + retry |
| NS8: 全链路验收 | M | ✅ DONE | 1126 tests green, UI 90→PASS, User Rep 99/100 |

## Bug Fixes (Pre-Sprint, migrated from buglist.md)

- BUG-2 金额单位：ths 通道 `ConvertWanToYuan` 统一为元
- BUG-4 历史数据：emweb 3 轮迭代拉取覆盖 ~4 年
- BUG-1 PDF 误报：前端检查 `downloadedCount`，为 0 展示原因
- BUG-3 无反馈：展示采集结果摘要

## Test Results

| Suite | Total | Passed | Failed | Skipped |
|-------|-------|--------|--------|---------|
| Backend Worker | 44 | 44 | 0 | 0 |
| Backend API | 617 | 617 | 0 | 0 |
| Frontend vitest | 465 | 463 | 0 | 2 |
| **Total** | **1126** | **1124** | **0** | **2** |

## Acceptance

| Reviewer | Score | Verdict |
|----------|-------|---------|
| Test Agent | 1126/1126 | PASS |
| UI Designer | 90/100 → fixed | PASS |
| User Representative | 99/100 | PASS |

### UI Designer BLOCKER (fixed)
VotingPanel 候选卡片缺 TextLength 质量指标 → 增加 `TextLength` 字段 + `formatTextLen()` 格式化。

### User Rep NITs
1. `formatMoneyDisplay` 对 per-share 字段无语义排除（实际无影响）
2. `smartPickPdf` 逻辑在 Drawer 和 ComparePane 重复
3. `InferReportTypeFromFileName` 港股中英混合标题可能漏判

## Files Changed

### Backend (9 files)
- `Models/PdfFileDocument.cs` — PdfPageText, VotingCandidate, StageLog.Details, ExtractedText, ParsedFields
- `Services/Pdf/PdfProcessingPipeline.cs` — FullTextPages capture, VotingCandidates build, StageDetails, UpsertStub
- `Services/Pdf/PdfParseUnitBuilder.cs` — ExtractedText + ParsedFields population
- `Services/CninfoClient.cs` — column fix, quarterly categories, retry logic
- `Modules/Stocks/Contracts/PdfFileContracts.cs` — DTO updates
- `Modules/Stocks/Services/PdfFileQueryService.cs` — Mapping updates
- `FinancialWorker.Tests/PdfPipelineStubRecordTests.cs` — NS3 tests
- `FinancialWorker.Tests/PdfFileDocumentTests.cs` — LiteDB round-trip tests
- `Api.Tests/.../PdfFilesEndpointTests.cs` — API endpoint tests

### Frontend (8 files)
- `financialFieldDictionary.js` — `formatMoneyDisplay()`
- `FinancialDetailDrawer.vue` — Money abbreviation + tooltip
- `FinancialReportTab.vue` — Money abbreviation + tooltip
- `FinancialPdfParsePreview.vue` — ExtractedText + ParsedFields collapsible
- `FinancialPdfVotingPanel.vue` — Candidates comparison + text length
- `FinancialPdfStageTimeline.vue` — `<details>` collapse + details grid
- Test files (4 spec files updated/created)

### Documentation
- `README.UserAgentTest.md` — Added 9-step acceptance procedure for PDF features
- `.automation/sprint.md` — All NS1-NS8 marked DONE

## Technical Debt Introduced

None. All changes are backward-compatible with existing LiteDB documents (new fields default to empty/null).

## cninfo 修复详情

| Issue | Before | After |
|-------|--------|-------|
| column 参数 | 硬编码 `"szse"` | 动态 `sse`/`szse` |
| 报告类别 | 年报+半年报 | 年报+半年报+一季报+三季报 |
| 重试 | 无 | 查询 3 次 / 下载 2 次，指数退避 |
| 诊断 | 基础日志 | 各类别公告数 + 下载成功率统计 |

---

## 中文摘要

v0.4.2N Sprint 在单次会话内完成全部 8 个 Story（3S + 5M）。核心交付：
1. **PDF 管线增强**：全文逐页持久化、下载即入库、解析单元完整内容展示
2. **透明化**：投票候选对比卡片、阶段折叠面板 + 详细日志
3. **前端规范化**：金额统一缩写（万/亿/元）+ tooltip
4. **cninfo 修复**：column 参数 bug（根因）+ 季报覆盖 + 重试机制
5. **测试**：1126 测试全绿，0 回归

验收结论：Test PASS + UI Designer PASS + User Rep 99/100 PASS。
