# v0.4.2 R3 Test Agent 验证报告

- **角色**：Test Agent（零信任、双轮验证）
- **目标包**：packaged Windows desktop（.NET 8 + 前端 Vite build → wwwroot 托管）
- **健康端点**：`http://localhost:5119`
- **样本股**：`sh600519` 贵州茅台（id 段：69e8ec0e/0f/10）
- **执行时间**：2026-04-23 10:22 ~ 10:30（UTC+8）
- **限制声明**：DarBot Browser MCP 的 `take_screenshot(filename=...)` 在当前会话中**未将文件落盘**（已确认 `.automation/reports/artifacts/v042-r3-test/` 与工作区根均无产物）。所有截图均为会话内图像引用，可在对话上下文中查看；无法作为持久化 PNG/JPEG 归档。本报告以 API 响应、源码摘录、UI 快照 yaml 文本作为核心证据。

---

## Step 1 republish 结果

- 命令：`scripts/publish-windows-package.ps1 -Force`
- 结果：**PASS**，退出码 `0`
- wwwroot 时间戳：`artifacts/windows-package/Backend/wwwroot/index.html` 更新到 `2026-04-23 10:22:02`（再次 `start-all.bat` 之后刷新到 `10:22:20`）。
- 启动后历史包进程（PID 5400 / 24524 / 27444）已先 Kill，避免文件锁。

## Step 2 健康检查

- `GET http://localhost:5119/api/health` → **HTTP 200**
- 桌面壳（Desktop EXE）正常托管，标题栏显示 `SimplerJiang AI Agent`，状态 `已连接`。

## Step 3 API 契约验证（`sh600519`）

请求：
```
GET /api/stocks/financial/pdf-files?symbol=600519&page=1&pageSize=20
```

响应（实测原始数据）：

| fileName | reportType | fieldCount | lastReparsedAt |
|---|---|---|---|
| 贵州茅台2025年年度报告摘要.pdf | **Q1**❌ | 0 | 2026-04-23T09:56:23 |
| 贵州茅台2025年年度报告（英文版）.pdf | Unknown | 0 | (空) |
| 贵州茅台2025年年度报告.pdf | **Annual**✅ | 10 | 2026-04-23T10:28:42 |

PDF 详情端点：
- `GET /api/stocks/financial/pdf-files/{id}` → `parseUnits` 数 **145**（与 `fieldCount=10` 是不同维度，145 是单元数）。
- `GET /api/stocks/financial/pdf-files/{id}/content` → `Content-Type: application/pdf`，**无 `X-Frame-Options` 响应头**（满足 iframe 嵌入需求）。

> **N2 部分通过**：主报告（年度报告）现在分类为 `Annual` ✅；但同样含「年度报告」字样的 **摘要 PDF 仍被分类为 Q1** ❌。说明 `Semi→Q3→Q1→Annual` 优先级链对「摘要」文件名规则未覆盖（"摘要"二字未被识别为年度衍生品）。

## Step 4 UI Smoke（packaged + DarBot）

### N1 版本号 — **PASS**
- 顶栏快照：`generic [ref=e9]: v0.4.2`，与 `package.json`/`Directory.Build.props` 一致。

### N3 财报中心抽屉 = 顶部按钮 + Teleport Modal（无内嵌 ComparePane） — **PASS**
- 路径：`财报中心` → 任一卡片 → 抽屉打开 → 顶部 `📄 PDF 原件对照` 按钮。
- 抽屉本体快照中**无 `iframe`/`fc-compare` 节点**（已用 `grep` 全文确认），点击按钮后弹出 `dialog "PDF 原件对照"`，宽度肉眼 ≈ 屏幕 85%，高度 ≈ 90%（1366×768 截图实测对话框横跨 ≈ 1145px）。
- iframe 实际加载真实 PDF（侧栏第 1 页缩略图 + 「贵州茅台酒股份有限公司 2025 年年度报告」红字封面）。

### N4 股票详情 → 财务报表 → PDF 原件对照（`pageSize 10→20` + `smartPick`） — **PASS**
- 路径：`股票信息` → 侧栏点击 `贵州茅台 sh600519` → `📊 财务报表` Tab → `📄 查看 PDF 原件`。
- Modal 下拉 `combobox "PDF：" [ref=e513]` 选项快照：
  ```
  - option "贵州茅台2025年年度报告摘要.pdf · 字段 0 · 摘要"
  - option "贵州茅台2025年年度报告（英文版）.pdf · 字段 0 · 摘要"
  - option "贵州茅台2025年年度报告.pdf · 字段 10" [selected]
  ```
- ✅ **三项全部出现**（pageSize 升到 20 已生效，对比 GOAL 中提到 10 时会被截断）。
- ✅ **默认选中主报告**（字段 10，最高 fieldCount，smartPick 命中）。
- ✅ 右侧 `解析单元 共142条`、表格 `P1 字段5` … 等结构化解析全部渲染。

### B1 smartPick = 字段最多的主报告 — **PASS**（仅股票详情入口）
- 同上 N4，在股票详情入口 smartPick 选中字段 10 的主报告 ✅。
- ⚠️ **回归提醒**：在「财报中心抽屉」入口里，**点击「重新解析 PDF」之后**，drawer 的 `currentPdfId` 会被 smartPick 重新选成 `字段 0` 的「摘要 PDF」（详见下方 B4 BLOCKER）。两条入口路径的 smartPick 行为不一致。

### B4 重新解析后 `lastReparsedAt` 刷新 — **MIXED PASS / 半通过**
- ✅ 后端 `lastReparsedAt` 实际刷新（主报告从 `09:41:25` → `09:56:23` → `10:25:29` → `10:28:42`，每次重新解析都 +1 分钟级别推进）。
- ✅ 在股票详情 Modal 内点击重新解析后，下拉仍保持「主报告」选中，VotingPanel 时间戳同步更新为 `10:28:42`（从 yaml 的 `重新解析完成` toast + 顶部按钮重新可点状态推断）。
- ❌ 在「财报中心抽屉」内点击重新解析后，drawer 的「当前 PDF」自动从主报告（字段 10，时间 `10:25:29`）切回到「摘要 PDF」（字段 0，时间 `09:56:23`）。
  - VotingPanel 显示的 `最近解析时间` 看似变了（`09:41:25 → 09:56:23`），但**实际是因为 drawer 切了一个完全不同的 PDF，而不是当前 PDF 的时间被刷新**。
  - 用户视角：点了「重新解析」却看到字段数从 10 变成 0、时间反而倒退。这是**用户可见的回归**。

### M1 「重新解析」按钮 disabled + 文案「解析中…」+ spinner — **CODE PASS / 视觉未捕获**
- 源码确认（`frontend/src/modules/financial/FinancialReportComparePane.vue` L325–L340）：
  ```vue
  <button
    class="fc-compare-reparse-btn"
    data-testid="fc-compare-reparse-btn"
    :class="{ 'fc-compare-reparse-btn--loading': reparsing }"
    :disabled="reparsing || !currentPdfId"
    @click="handleReparse"
  >
    <span v-if="reparsing" class="fc-compare-reparse-spinner"
          aria-hidden="true" data-testid="fc-compare-reparse-spinner"></span>
    {{ reparsing ? '解析中…' : '重新解析 PDF' }}
  </button>
  ```
  覆盖三要素：`disabled` ✓、`解析中…` ✓、`spinner` ✓、`--loading` class ✓。
- 单测覆盖：`FinancialReportComparePane.spec.js:437` `expect(topBtn.classes()).toContain('fc-compare-reparse-btn--loading')`。
- ⚠️ **运行时未能在 packaged build 中视觉捕获 spinner**：连续两次点击重新解析，从点击到 `重新解析完成` toast 出现 < 1s（PDF 已缓存解析过，瞬时返回），DarBot 的 `take_screenshot` 总是落在 `reparsing=false` 之后。建议在更大/未缓存的 PDF 上人工肉眼验证，或在 vitest 层依赖现有单测。

---

## 双轮回归（按 Test Agent 强制规范）

| 项 | 第一轮 | 第二轮 |
|---|---|---|
| republish | exit 0、wwwroot 10:22:02 | start-all 重启后 wwwroot 10:22:20，依然 OK |
| API list 3 项 | 主报告 reportType=Q1（旧数据） | 重新解析后 reportType=Annual（已修） |
| Stock-detail Modal 3 选项 | 默认主报告 字段 10 | 第二次进入仍默认主报告 字段 10 |
| 重新解析按钮 | 弹 `重新解析完成` toast、字段数 10、时间 10:25:29 | 再次点击 → 字段仍 10、时间 10:28:42（推进） |
| Drawer 入口 smartPick | 进入即选主报告 | 重新解析后**切回 摘要 字段 0**（B4 BLOCKER） |

---

## 结论

| 编号 | 描述 | 结果 |
|---|---|---|
| N1 | 版本号升 0.4.2 | ✅ PASS |
| N3 | 抽屉 Teleport Modal、85vw×90vh、无内嵌 iframe | ✅ PASS |
| N4 | listPdfFiles pageSize=20、Modal 三项 + smartPick 主报告 | ✅ PASS（股票详情入口） |
| B1 | smartPick = 字段最多 | ✅ PASS（股票详情入口）<br>⚠️ 财报中心抽屉路径反例存在 |
| B4 | 重新解析后 lastReparsedAt 刷新 | ⚠️ MIXED：股票详情 ✅；财报中心抽屉 ❌ smartPick 回退导致用户看到 0 字段旧时间 |
| N2 | 文件名 reportType（Semi→Q3→Q1→Annual） | ⚠️ MIXED：主报告 Annual ✅；摘要 PDF 仍被分类 Q1 ❌ |
| M1 | 按钮 disabled+解析中…+spinner | ✅ CODE PASS（源码 + 单测）<br>⚠️ 运行时视觉未抓到（reparse < 1s） |

### BLOCKERS（必须 R4 解决）

1. **B4 / B1 财报中心抽屉路径回归**：抽屉内点击「重新解析 PDF」后，`smartPick` 把 `currentPdfId` 切回 0 字段的摘要 PDF，导致 VotingPanel 显示倒退（10 字段 → 0 字段、`10:25:29` → `09:56:23`）。需要将「重新解析后」的入口策略改为**保留当前选中 PDF**，仅在初次进入抽屉时调用 smartPick。涉及 `frontend/src/modules/financial/FinancialDetailDrawer.vue` 第 214–222 行附近 `listPdfFiles({page:1,pageSize:20})` 重拉后的赋值逻辑。
2. **N2 摘要 PDF 仍被分类为 Q1**：「贵州茅台2025年年度报告摘要.pdf」内含「年度报告」字样，应优先匹配 `Annual`（或新增 `AnnualSummary` 子类型），现错误命中 `Q1` 规则。需要在 backend 文件名分类器中给「摘要」加显式判断。

### 非阻塞观察

- M1 在 packaged build 中无法用浏览器自动化捕获 < 1s 的 loading 状态。建议：(a) 接受单测覆盖；或 (b) 在 reparse handler 加 `await sleep(300)` 仅 dev 模式 debug，方便人工验收。
- DarBot Browser MCP 的 `take_screenshot(filename=...)` 在本会话不落盘到指定路径；若 R4 想保留 PNG 归档，建议用 Playwright 直驱或改用 `mcp_winapp_take_screenshot`（针对桌面壳）。

### 放行建议

**🔴 不予放行 R3 → R4（拒绝通过）**：
- 已发现 2 个用户可见 BLOCKER（B4 抽屉回退 + N2 摘要错分类），按 Test Agent 零信任原则不能盖章。
- 建议 PM Agent 把这两项作为 R4 修复目标重新派发给 Dev Agent，修复后再走一次 republish + 双轮 smoke。

---

## 附录：本轮使用的 DarBot 关键引用快照（节选）

```yaml
# Stock-detail Modal 下拉
- combobox "PDF：" [ref=e513] [cursor=pointer]:
  - option "贵州茅台2025年年度报告摘要.pdf · 字段 0 · 摘要"
  - option "贵州茅台2025年年度报告（英文版）.pdf · 字段 0 · 摘要"
  - option "贵州茅台2025年年度报告.pdf · 字段 10" [selected]

# 重新解析完成 toast
- generic [ref=e1316]: 重新解析完成

# Reparse 按钮源码
class="fc-compare-reparse-btn" data-testid="fc-compare-reparse-btn"
:class="{ 'fc-compare-reparse-btn--loading': reparsing }"
:disabled="reparsing || !currentPdfId"
{{ reparsing ? '解析中…' : '重新解析 PDF' }}
```

— 报告完 —
