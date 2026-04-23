# v0.4.2 R3.1 Test Agent 验证报告

> 角色: Test Agent (零信任)
> 时间窗口: 2026-04-23 10:30 ~ 10:50 (本地)
> 包路径: `C:\Users\kong\AiAgent\artifacts\windows-package\`
> 端口: 5119 (packaged `start-all.bat` 模式)
> 验证目标: R3.1 修复 (BLOCKER 1 二阶段补丁 + N2 旧数据 reparse + reportType 一致性)
> 证据目录: `.automation\reports\artifacts\v042-r3-1-test\`

---

## Step 1 republish: PASS

- 命令: `scripts\publish-windows-package.ps1`
- 结果: 退出码 0，未被锁文件中断 (验证前已 kill 残留 dotnet 进程)
- 验证: `artifacts\windows-package\wwwroot\index.html` LastWriteTime = `2026/4/23 10:41:16` (本次发布产物)
- 结论: 包内已包含 R3.1 前端 + 后端代码

## Step 2 健康: PASS

- 启动: `start-all.bat` (packaged 模式)
- 健康检查: `GET http://localhost:5119/api/health` → **200 OK**
- 端口确认: 后端绑定 5119，前端经后端 wwwroot 托管 (非 Vite dev server)

## Step 3 旧数据 reparse: PASS

依据 N2 规则 (文件名含「年度报告」但 `reportType ≠ Annual` 视为脏数据)，扫描 600519 全部 PDF：

| ID | fileName | reparse 前 reportType | reparse 后 reportType |
|---|---|---|---|
| `69e8ec1070628c01e811a424` | 贵州茅台2025年年度报告**摘要**.pdf | `Q1` (脏) | **`Annual`** ✅ |
| `69e8ec1070628c01e811a423` | 贵州茅台2025年年度报告（**英文版**）.pdf | `Unknown` (脏) | **`Annual`** ✅ |
| `69e8ec0e70628c01e811a422` | 贵州茅台2025年年度报告.pdf | `Annual` (干净) | `Annual` (未触) |

证据: `step3-list-before.json` / `step3-list-after.json`

## Step 4 API smoke: PASS

| 查询 | 期望 | 实测 | 判定 |
|---|---|---|---|
| `GET /api/stocks/financial/pdf-files?symbol=600519&reportType=Annual&page=1&pageSize=10` | ≥ 3 条年报 PDF | **3 条** (主报告 + 摘要 + 英文版) | ✅ |
| `GET /api/stocks/financial/pdf-files?symbol=600519&reportType=Q1&page=1&pageSize=10` | 0 条假阳性 | **0 条** | ✅ |

> 关键回归点: Step 3 reparse 后，**没有任何 600519 PDF 残留为 Q1**。N2 旧数据脏类型已彻底纠正。

## Step 5 BLOCKER 1 UI 验证: **PARTIAL — 半通过**

测试路径: 财报中心列表 → 600519 主报告详情 → "在 Modal 中查看 / 重新解析 PDF" → 切换到 **摘要** PDF → 点击 "重新解析 PDF"

### ✅ 子断言 1 (选中保持「摘要」): PASS

| 时刻 | 模态标题 | 下拉选中 |
|---|---|---|
| reparse 前 (`step5-01-…before-reparse.png`) | `贵州茅台2025年年度报告摘要.pdf · 2026-03-31` | `…摘要.pdf · 字段 0 · 摘要` [selected] |
| reparse 后 (`step5-02-…after-reparse-error-toast.png`) | **`贵州茅台2025年年度报告摘要.pdf · 2026-03-31`** (未变) | **`…摘要.pdf · 字段 0 · 摘要` [selected]** (未变) |

→ B1 二阶段补丁有效，模态选中**没有被切回主报告**。

### ❌ 子断言 2 (「最近重解析」时间刷新): **FAIL** — 阻塞性回归

| 数据来源 | 字段 | 值 |
|---|---|---|
| 后端 API `GET /pdf-files/69e8ec1070628c01e811a424` | `lastReparsedAt` | **`2026-04-23T10:46:50.622+08:00`** ✅ 已更新 |
| 前端抽屉 (modal 旁) "最近重解析" | 渲染值 | **`2026/4/23 10:42:23`** ❌ 仍是 Step 3 批量 reparse 的旧时间 |
| 网络日志 | `POST /pdf-files/69e8ec1070628c01e811a424/reparse` | 200 OK |
| 网络日志 | `GET /pdf-files/69e8ec1070628c01e811a424` (post-reparse refresh) | **缺失 — 未发起** |

**5 秒等待 + 全局时钟推进到 10:47:55 后再快照，抽屉数值依然停留在 10:42:23**，证据: `step5-02-modal-after-reparse-error-toast.png`。

### 🔬 Round 2 交叉验证 (主报告路径)

为定位故障范围，将模态切换到 `贵州茅台2025年年度报告.pdf · 字段 10` (主报告，可解析成功路径) 并触发重新解析:

| 数据来源 | 字段 | 值 |
|---|---|---|
| 后端 API `GET /pdf-files/69e8ec0e70628c01e811a422` | `lastReparsedAt` | `2026-04-23T10:49:08.206+08:00` |
| 前端抽屉 "最近重解析" | 渲染值 | **`2026/4/23 10:49:08`** ✅ 同步刷新 |

证据: `step5-03-modal-mainreport-after-reparse-refreshed.png`

→ **结论: BLOCKER 1 二阶段修复对「主报告 / 解析成功」路径有效，但对「摘要 / 解析失败 (PDF 文本中未找到可解析的财务数据)」路径未生效。前端在收到 reparse 200 + 错误内容时，跳过了抽屉/模态的 lastReparsedAt 重新拉取。**

---

## 结论: **REJECT — BLOCKER 1 半修复，不放行 R3**

### BLOCKER 列表 (R3.1 之后仍存在)

#### 🔴 BLOCKER 1 (回归): 「最近重解析」时间在解析失败路径不刷新

- 路径: 模态从抽屉打开 → 选中非默认 PDF (e.g. 摘要) → 重新解析 → 后端返回 200 但解析单元 0 (alert "PDF 文本中未找到可解析的财务数据")
- 现象: 抽屉「最近重解析」字段保持旧值；前端跳过 GET 刷新；用户无法判断"刚才那次重解析到底成功记录了没有"
- 后端实际行为: `lastReparsedAt` **已**正确更新 → 这是纯前端 BUG (responseHandler 在 alert 分支提前 return，未走 onReparseSuccess 的 refetch 流)
- 影响: 用户连续点击「重新解析」会看到时间字段静止，会以为按钮"卡死"或后端不响应；与 R3 BLOCKER 1 想解决的核心 UX 问题同源
- 复现 100%: 摘要 / 英文版 (任何字段=0 的 PDF)
- 证据: `step5-01-…`, `step5-02-…`, `step5-zhaiyao-detail-after-reparse.json`

### PASS 项 (本轮已验证)

- ✅ R3 N1 reportType detector 推导正确 (年报摘要/英文版 → Annual)
- ✅ R3 N2 旧数据 reparse 全量纠正 (600519 无 Q1 假阳性)
- ✅ R3 BLOCKER 1 子项 A: 模态选中不再被切回主报告
- ✅ packaged 模式发布链 + 健康检查
- ✅ Round 2: 主报告 (解析成功) 路径下抽屉时间正常刷新

### Test Agent 不放行的依据

按零信任原则，BLOCKER 1 完整描述包含 **「时间字段刷新」**。当前修复只覆盖一半场景 (解析成功)，对解析失败场景 (摘要类零字段 PDF) 仍存在 UI 静止 BUG。R3 不能"半修"放行。

请退回 Dev Agent，要求：
1. 在 reparse response handler 中，无论 `parseUnits.length === 0` 与否，都触发抽屉/模态的 `refetchPdfMeta()` (拉取最新 `lastReparsedAt`)
2. 错误 alert "PDF 文本中未找到可解析的财务数据" 与时间刷新解耦：alert 仍展示 + 时间字段同步
3. 修复后再启动一次包发布 + Test Agent 二轮验证

### 附: 全部证据清单

| 文件 | 内容 |
|---|---|
| `step3-list-before.json` | reparse 前 600519 全 PDF 列表 (含脏 reportType) |
| `step3-list-after.json` | reparse 后 600519 全 PDF 列表 (全部 Annual) |
| `step5-zhaiyao-detail-after-reparse.json` | 后端 API 响应，证明 `lastReparsedAt` 已更新到 10:46:50 |
| `step5-01-modal-zhaiyao-before-reparse.png` | reparse 前模态截图 (摘要选中、抽屉时间 10:42:23) |
| `step5-02-modal-after-reparse-error-toast.png` | reparse 后模态截图 (摘要仍选中 ✅，抽屉时间 **仍 10:42:23 ❌**，alert 红条可见) |
| `step5-03-modal-mainreport-after-reparse-refreshed.png` | Round 2 主报告 reparse 后截图 (抽屉时间正常更新到 10:49:08) |
| `_capture_edge.ps1` | 截图辅助脚本 (DarBot MCP `take_screenshot` 不落盘的解决方案) |
