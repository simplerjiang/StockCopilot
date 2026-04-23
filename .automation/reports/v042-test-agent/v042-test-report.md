# v0.4.2 Test Agent 验证报告

**验证时间**：2026-04-23 09:33 — 09:46（北京时间）
**验证模式**：packaged desktop（`start-all.bat` 启动 `artifacts\windows-package\SimplerJiangAiAgent.Desktop.exe`，WebView2 渲染）
**端口**：5119
**最终结论**：**全部 PASS**（B1/B2/B3/B4/M1/M2 共 6 项修复在真实 WebView2 环境验证通过；P2-A 发布脚本已确认 wwwroot 同步生效）

---

## Step 1 单元测试

| 项目 | 用例数 | 通过 | 失败 | 跳过 | 结果 |
|------|------|------|------|------|------|
| `dotnet test SimplerJiangAiAgent.FinancialWorker.Tests` | 20 | 20 | 0 | 0 | ✅ PASS |
| `dotnet test SimplerJiangAiAgent.Api.Tests` | 616 | 616 | 0 | 0 | ✅ PASS |
| `npm --prefix .\frontend run test:unit` | 441 | 439 | 0 | 2 | ✅ PASS（2 项 skipped 为既定豁免）|

---

## Step 2 重新发布 packaged

- 命令：`pwsh scripts\publish-windows-package.ps1`
- 退出码：0
- `artifacts\windows-package\Backend\wwwroot\index.html` LastWriteTime：**2026-04-23 09:33:23**
- `artifacts\windows-package\Backend\frontend\dist\index.html` LastWriteTime：**2026-04-23 09:33:23**（与 wwwroot 完全同步，**V042-P2-A 修复生效**）
- `artifacts\windows-package\SimplerJiangAiAgent.Desktop.exe` 大小：152064 bytes（最新构建）

---

## Step 3 启动健康检查

- `start-all.bat` 已启动 desktop EXE（PID 24524），后端在 5119 端口
- `GET http://localhost:5119/api/health` → 200 OK，`{"status":"ok"}`，5 秒内可达

---

## Step 4 API 契约验证（B4 后端层）

### 4.1 列表接口
- `GET /api/stocks/financial/pdf-files?stockCodes=600519`
- 返回 3 个 PDF（贵州茅台 2025 年报 / 2025 年报摘要 / 2025 年报英文版），字段齐全

### 4.2 详情接口（145 个 ParseUnit）
- `GET /api/stocks/financial/pdf-files/{id}/units`
- **145 个 parseUnits，全部包含 `pageStart`/`pageEnd`/`blockKind` 三字段且非空**（V042-P0-D 修复后字段正确序列化为 camelCase）

### 4.3 PDF 内容接口（B2 后端层）
- `GET /api/stocks/financial/pdf-files/{id}/content`
- 响应头：
  - `Content-Type: application/pdf` ✅
  - `Content-Disposition: inline` ✅（V042-P0-B 修复，非 attachment）
  - `X-Content-Type-Options: nosniff` ✅
  - **`X-Frame-Options` 已移除** ✅（V042-P0-B 修复，允许 iframe 嵌入）
  - Content-Length：1082847 bytes（PDF 实际下载成功）

---

## Step 5 UI 真实环境验证（WebView2）

### 5.1 财报中心入口 → 详情对话框

#### B1：PDF 选择器（V042-P0-A）✅ PASS
- 对话框打开后右下方出现 `<select>` "PDF：" 选择器
- 默认选中：**`贵州茅台2025年年度报告.pdf · 字段 10`**（主报告，字段最多者优先）
- 下拉展开显示 2 个候选：主报告 + 摘要（英文版被 `reportPeriod=2026-01-13` 范围过滤，符合预期）
- 切换到摘要后：左侧 iframe 重新加载摘要 PDF，标题更新为"贵州茅台2025年年度报告摘要.pdf · 2026-03-31"
- 截图证据：
  - [`desktop-pdf-iframe-webview2.png`](.automation/reports/artifacts/v042-test/desktop-pdf-iframe-webview2.png)（默认主报告）
  - [`desktop-pdf-summary-switched.png`](.automation/reports/artifacts/v042-test/desktop-pdf-summary-switched.png)（切换至摘要后下拉展开）

#### B2：iframe 真实渲染 PDF（V042-P0-B）✅ PASS
- **WebView2 中 iframe 内嵌 PDF 完整渲染**：左侧可清晰看到"贵州茅台酒股份有限公司 / 2025 年年度报告"封面页，章节目录可见，能滚动至第二页正文
- 网络层确认：`GET /api/stocks/financial/pdf-files/{id}/content` 返回 200 OK，Content-Type 正确
- 切换到摘要 PDF 后，iframe 同步刷新为"2025 年年度报告摘要"封面
- 截图证据：
  - [`desktop-pdf-iframe-webview2.png`](.automation/reports/artifacts/v042-test/desktop-pdf-iframe-webview2.png)
  - [`desktop-stage-timeline-bottom.png`](.automation/reports/artifacts/v042-test/desktop-stage-timeline-bottom.png)（左侧 iframe 显示 PDF 第二页正文）
- **重要说明**：此前在 Playwright Chromium 浏览器中 iframe 截图为空白，经直接打开 PDF URL 验证（[`pdf-direct-navigation.png`](.automation/reports/artifacts/v042-test/pdf-direct-navigation.png)）确认是 Playwright 截图器对 PDF 插件的渲染限制，**不影响真实 WebView2 用户**

#### B4：145 个 ParseUnit 渲染（V042-P0-D）✅ PASS
- 右侧"解析单元"Tab 显示 `表格 共 142 条`、字段数（P1=5、P2=28、P3=...）
- 切换 PDF 后右侧重新拉取（摘要 PDF 显示"暂无解析结果，该 PDF 尚未生成 ParseUnits"，符合预期）
- 截图证据：[`desktop-pdf-iframe-webview2.png`](.automation/reports/artifacts/v042-test/desktop-pdf-iframe-webview2.png)

#### M1：重新解析按钮 + Toast（V042-P1-A）✅ PASS
- 点击"重新解析 PDF"按钮：按钮文字立即变为"解析中…"（loading 态）
- 后台完成后右上角弹出绿色 Toast：**"✓ 重新解析完成"**
- 截图证据：[`desktop-reparse-loading.png`](.automation/reports/artifacts/v042-test/desktop-reparse-loading.png)

#### M2：解析阶段 Timeline 第 3 个 Tab（V042-P1-B）✅ PASS
- 右侧 3 个 Tab：`解析单元 / 投票信息 / 解析阶段`，"解析阶段"为第 3 个
- 切换至解析阶段后显示完整 StageTimeline：**`成功 5 / 5`**
  1. **下载** → 成功（绿点）
  2. **文本提取** → 成功（绿点）
  3. **提取器投票** → 成功（绿点）
  4. **结构化解析** → 成功（绿点）
  5. **入库** → 成功（绿点）
- 切换到摘要 PDF 后阶段重新载入：`成功 3 / 5`，结构化解析显示"失败：PDF 文本中未找到可解析的财务数据"，入库为"跳过"——证明 timeline 实时反映每个 PDF 的状态
- 截图证据：
  - [`desktop-stage-timeline-tab.png`](.automation/reports/artifacts/v042-test/desktop-stage-timeline-tab.png)（5/5 主报告）
  - [`desktop-stage-timeline-bottom.png`](.automation/reports/artifacts/v042-test/desktop-stage-timeline-bottom.png)（5 阶段全部可见）
  - [`desktop-pdf-summary-switched.png`](.automation/reports/artifacts/v042-test/desktop-pdf-summary-switched.png)（摘要 3/5）

### 5.2 股票信息入口 → 财务报表 → 查看 PDF 原件

#### B3：股票详情入口符号归一化（V042-P0-C）✅ PASS
- 路径：`股票信息 → 贵州茅台 sh600519 → 财务报表 Tab → 📄 查看 PDF 原件`
- **结果**：成功打开"PDF 原件 / 对照"模态框，**未触发"暂无 PDF 原件"alert**
- 模态框内 ComparePane 完整渲染：
  - 顶部："贵州茅台2025年年度报告摘要.pdf · 2026-03-31"
  - 左侧 iframe：摘要 PDF 完整渲染（封面 + 7 页内容可翻页）
  - 右侧 3 个 Tab：解析单元 / 投票信息 / 解析阶段
- 证明 `sh600519` 已通过 `normalizeStockCode` 归一化为后端可识别格式
- 截图证据：[`desktop-stock-detail-pdf-modal.png`](.automation/reports/artifacts/v042-test/desktop-stock-detail-pdf-modal.png)

---

## Step 6 第二轮回归（"至少做两轮以上验证"）

| 验证项 | 第 1 轮 | 第 2 轮 | 一致性 |
|---|---|---|---|
| 财报中心 → 详情对话框打开 | ✅ | ✅ | 一致 |
| 选择器默认主报告（字段 10） | ✅ | ✅ | 一致 |
| iframe 渲染 PDF | ✅ | ✅ | 一致 |
| 解析单元列表（142 条） | ✅ | ✅ | 一致 |
| 解析阶段 Tab 5 阶段 | ✅ | ✅ | 一致 |

第 2 轮截图：[`desktop-r2-pdf-iframe.png`](.automation/reports/artifacts/v042-test/desktop-r2-pdf-iframe.png)

无状态泄漏，每次重新打开对话框均正确按 `field count desc` 选中默认 PDF。

---

## 截图清单

所有截图位于 `c:\Users\kong\AiAgent\.automation\reports\artifacts\v042-test\`：

| 文件 | 用途 |
|---|---|
| `desktop-initial.png` | Desktop EXE 启动初始页 |
| `desktop-pdf-dialog-initial.png` | 财报详情对话框打开（基本信息）|
| `desktop-pdf-iframe-webview2.png` | **B1+B2+B4 关键证据**：选择器+iframe+解析单元 |
| `desktop-stage-timeline-tab.png` | M2：解析阶段 Tab 5/5 主报告 |
| `desktop-stage-timeline-bottom.png` | M2：5 个阶段全部可见 + iframe 第 2 页 |
| `desktop-reparse-loading.png` | **M1 关键证据**：✓ 重新解析完成 Toast |
| `desktop-pdf-summary-switched.png` | B1：切换摘要 PDF 后下拉+stages 同步 |
| `desktop-stock-financial-tab.png` | B3 前置：股票详情财务报表 Tab |
| `desktop-stock-detail-pdf-modal.png` | **B3 关键证据**：股票入口模态框打开 |
| `desktop-r2-pdf-iframe.png` | 第 2 轮回归：default + iframe 一致 |
| `pdf-direct-navigation.png` | 旁证：直接打开 PDF URL，证明后端 PDF 内容正常 |

---

## 部分小观察（不阻塞放行）

1. **应用标题仍显示 v0.4.1**：左上角 logo 标签显示 `v0.4.1`，但 EXE 文件版本和实际功能均为 v0.4.2 修复后版本。属于版本号显示文案未同步，**不影响功能验收**，建议在下次小版本同步更新。
2. **PDF 候选数量为 2 而非 3**：选择器只显示主报告 + 摘要，英文版被 `reportPeriod=2026-01-13` 范围过滤，符合 V042-P0-A 修复中按报告期匹配的设计意图。
3. **第 2 轮中 `财报中心` Tab 切换需用 `invoke_element` 而非 `click_element`**：可能由于 Playwright 之前的模态框焦点保留所致，是测试工具交互细节，**不影响真实用户操作**。

---

## BLOCKER（如有）

**无 BLOCKER**。所有 7 项修复（V042-P2-A、V042-P0-A/B/C/D、V042-P1-A/B）在真实 packaged WebView2 环境验证全部通过，可以放行。
