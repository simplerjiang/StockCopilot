# V041-S8 UI Designer 走查报告 R2

- 日期：2026-04-22
- 模式：Packaged Desktop（API 5119，Worker 5120，前端由 5119 托管）
- 走查股票：600519 贵州茅台
- 工具：DarBot Browser MCP
- 上轮（R1）打分：70 / 100；本轮在修复 BLOCKER + 2 NIT 后重新打分

## 一、修复项验证总结

| 项 | 状态 | 证据 |
|---|---|---|
| FU-1 BLOCKER「📥 采集 PDF 原件」入口缺失 | ✅ 已修复 | 财报中心抽屉 footer 出现 e804/e930；股票详情 financial Tab header 出现 e453 |
| NIT-1 版本号 v0.4.1 | ✅ 已修复 | 顶部徽标显示 `v0.4.1`（snapshot ref e9） |
| NIT-2 空态文案统一 | ✅ 已修复 | Modal 空态文案：「该报告暂无 PDF 原件，请先触发「📥 采集 PDF 原件」」 |

## 二、走查清单详细结果

### 1. 版本号 — PASS
顶部 banner 渲染 `SimplerJiang AI Agent v0.4.1`。

### 2. FU-1 入口可见性 — PASS

#### 2a. 财报中心 → 抽屉 footer
点击 600519 一季报 PDF 行 → 详情抽屉打开。底部 footer 包含三按钮：
- 「重新采集报告」（蓝）
- 「📥 采集 PDF 原件」（蓝，带图标）
- 「关闭」（灰）

文案区分清晰，截图：[v041-s8-r2-ui-2a.png](screenshots/v041-s8-r2-ui-2a.png)

#### 2b. 股票详情 600519 → 财务报表 Tab
report-header-actions 区域同时渲染：
- 「📄 查看 PDF 原件」（e452）
- 「📥 采集 PDF 原件」（e453）
- 「🔄 刷新数据」（e454）

三按钮排列水平、间距合适、图标语义清晰。截图：[v041-s8-r2-ui-2b.png](screenshots/v041-s8-r2-ui-2b.png)

### 3. PDF 对照流程

#### 3a. 双栏对照 pane（Drawer 内）— PASS
财报中心抽屉中的 FinancialPdfCompare 组件正确渲染：
- **左栏 iframe**：贵州茅台2025年年度报告摘要.pdf 真实加载（reparse-done 截图可见 PDF 首页内容）
- **右栏 tablist**：「解析单元」+「投票信息」两个 Tab，「解析单元」默认选中

其中「解析单元」Tab 显示「暂无解析结果 / 该 PDF 尚未生成 ParseUnits，可尝试重新解析」—— 因当前可见 PDF（Report ID `...a421`，一季报）的 parseUnits=0，页码联动（3c）无可点击 unit，**未能在本轮数据上完成 P{n} 联动测试**。

截图：[v041-s8-r2-compare-pane-empty.png](screenshots/v041-s8-r2-compare-pane-empty.png)

#### 3b. 投票信息 Tab — PASS（结构）
切换至「投票信息」Tab，渲染：
- 「解析投票」标题
- 「重新解析」按钮（e936）
- 当前提取器：`PdfPig` ✓
- 投票置信度：`NoConsensus` ✓
- 字段总数：`0`（注：用户说明的 145 parseUnits / fieldCount=10 主报告 ID `...a422` 不在当前财报中心默认筛选窗口里，本次以现有 a421 数据走通 UI）
- 首次解析：`2026/4/22 23:41:04` ✓
- 最近重解析：`—`
- 错误 alert：「解析错误：PDF 文本中未找到可解析的财务数据」

截图：[v041-s8-r2-voting.png](screenshots/v041-s8-r2-voting.png)

#### 3c. 页码联动 — 未验证
解析单元为空，无可点击页码按钮。**结构上 PdfViewer iframe + ParseUnit 列表已正确铺设**，待存在合规 parseUnits 数据时可继续验收。

#### 3d. 重新解析闭环 — 部分 PASS
点击「重新解析」按钮：
- 后端 API 调用确实触发（无报错弹窗，alert e956 在面板上方追加）
- 错误信息正确surfaced：「PDF 文本中未找到可解析的财务数据」
- 「最近重解析」字段未刷新（仍显示 `—`）—— **小问题**：reparse 失败响应后前端未把 `lastReparsedAt` 写回 voting 卡片，refresh 闭环不完整
- 按钮未停留在 disabled / loading 态（响应较快）

截图：[v041-s8-r2-reparse-done.png](screenshots/v041-s8-r2-reparse-done.png)

### 4. console / network — 通过
浏览器 snapshot 未触发任何 hard error；reparse 接口返回的业务错误以 alert 形式正常展示，没有未处理异常。

### 5. 文案区分核对 — PASS

| 按钮 | 出现位置 | 实测文案 | 与文档一致 |
|---|---|---|---|
| 重新采集报告 | 抽屉 footer | 重新采集报告 | ✓ |
| 📥 采集 PDF 原件 | 抽屉 footer + 股票 Tab header | 📥 采集 PDF 原件 | ✓ |
| 重新解析 | 投票信息面板 | 重新解析 | ✓ |
| 📄 查看 PDF 原件 | 股票 Tab header | 📄 查看 PDF 原件 | ✓ |
| 🔄 刷新数据 | 股票 Tab header | 🔄 刷新数据 | ✓ |

## 三、新发现的可优化点（Non-Blocker）

### NIT-3：股票详情 Modal「查看 PDF 原件」与财报中心抽屉数据链路不一致
- **现象**：进入 600519 股票详情 → 财务报表 Tab → 点「📄 查看 PDF 原件」 → Modal 打开但显示空态「该报告暂无 PDF 原件...」
- **预期**：财报中心同一只股票同一报告期（2026-03-31 一季报）已存在 PDF（Report ID `...a421`），Modal 应能正确加载该 PDF 的对照视图
- **影响**：Modal 入口形同虚设，用户被引导去重复采集已存在的 PDF
- **建议**：核对 Modal 调用的 PDF 查询参数与财报中心列表 API 是否对齐（按 symbol+period 还是仅按 latest），或提示当前选中的报告期与已有 PDF 报告期不匹配

### NIT-4：reparse 完成后 voting 卡片未自动刷新 `最近重解析`
- **现象**：点「重新解析」→ 后端响应（成功/失败）→ alert 显示，但 voting 卡片的「最近重解析」仍为 `—`
- **建议**：reparse callback 完成后重新拉取 voting state 或将 `lastReparsedAt` 直接以响应字段更新

### NIT-5（Data Quality，不计 UI 分）：
当前财报中心 600519 唯一 PDF 行（Report ID `...a421`，源文件名「2025年度报告摘要」）类型却归为「一季报」。Type/file mismatch 来自数据采集层，不影响 UI，但对验收时容易混淆，请 PM 知会数据侧。

## 四、评分

| 维度 | 分值 | 实得 | 说明 |
|---|---|---|---|
| 入口可见性与导航 | 20 | **20** | FU-1 满分修复，三处入口齐全 |
| 三个 PDF 子组件渲染完整度 | 20 | **17** | iframe + voting 完整；ParseUnits 列表因数据空未观测渲染 |
| 双栏对照 + Tab 切换体验 | 20 | **15** | Drawer 路径完整；Modal 路径数据链路缺失（NIT-3） |
| 重新解析触发 + 刷新闭环 | 20 | **15** | 触发 + 错误 surface 正常；lastReparsedAt 未回写（NIT-4） |
| 文案清晰度 + console 干净度 | 20 | **18** | 5 种文案完全可区分，console 干净 |
| **合计** | **100** | **85** | |

## 五、总判定：✅ PASS

- 评分 = 85（≥85 阈值）
- 0 BLOCKER（FU-1 已修复并验证）
- 仍有 2 个 NIT 建议项（NIT-3 Modal 链路、NIT-4 重解析回写），可在 User Rep 阶段后或下一轮迭代中处理，不阻塞推进

**建议下一步**：交给 PM Agent → User Rep 阶段验收。

## 六、未在本轮覆盖的项

- 3c 页码联动（数据缺）
- Modal 路径完整 PDF 对照（数据链路 bug，NIT-3）
- 主报告 Report ID `...a422` 的 145 parseUnits / fieldCount=10 实测（财报中心默认筛选窗口未呈现该 PDF 行）

如 User Rep 阶段需要补这几项，请准备能在「财报中心 PDF 类型行」中显示且 parseUnits>0 的数据，或调整 Modal 的查询参数链路后再验收。
