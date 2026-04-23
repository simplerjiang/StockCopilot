# Sprint 看板

> 当前 Sprint 最多 3 个活跃 Story。完成的 Story 归档到 `/memories/repo/sprints/`。

## 看板规则（2026-04-22 新增）

1. **NIT 不延后**：Story 复核暴露的 NIT，须在复核后 1 小时内修完并补回测试，不允许累计到 Sprint 末尾。
2. **BLOCKER 立即停摆**：任何 BLOCKER 阻塞当前 Story 进入下一个；先修 BLOCKER，再继续。
3. **预存在测试失败必须立项**：复核中发现非本 Story 引入的失败用例，须开独立技术债 Story 跟踪，不允许"已知失败"长期挂在主干。
4. **`tasks.json` 与 `sprint.md` 关系**：`sprint.md` 是当前 Sprint 唯一权威看板；`tasks.json` 仅作历史任务事件流（append-only），不再用作活跃任务管理。

---

# v0.4.1 Sprint（PDF 原件对照与手动重新解析）

## Sprint 目标

**让用户在软件内直接看到 PDF 原件，并且能把原件和解析结果并排对照，同时支持手动重新解析。**

## 核心约束

- **page_start / page_end / block_kind 必须持久化**（§9.1 硬约束）：每个解析单元落库须含三字段（1-based 页码 + 枚举 `narrative_section` / `table` / `figure_caption`），为 v0.4.3 citation→PDF 跳转铺路。
- 不允许 v0.4.1 阶段交付「PDF 详情接口返回的解析单元缺三字段」的实现。

## 完成定义

1. 能看原件（软件内嵌 PDF 预览，桌面 + 浏览器双环境兼容）
2. 能对照（左 PDF 原件 / 右结构化或文本或投票信息双栏）
3. 能手动重新解析（单文件触发，完成后详情自动刷新，三字段同步更新）
4. 能定位失败层级（下载 / 提取 / 投票 / 解析 / 落库 5 阶段可视化）
5. 同一能力在「财报中心」与「股票信息 → 财务报表」两处可用
6. PDF 详情接口每个解析单元含 `page_start` / `page_end` / `block_kind`，重新解析后字段刷新

## Stories

### V041-S1: 后端 PDF 详情持久化模型 + 解析单元三字段
- **状态**：DONE | **级别**：M | **完成时间**：2026-04-22 | **commits**：`c0d2fb4`
- 选 LiteDB（与现有 FinancialDbContext 一致），新建 `PdfFileDocument` + `PdfParseUnitBuilder`；page=0 拒收；三字段强校验；PdfProcessingPipeline 接入落库。Worker tests 4→20 全绿，Api.Tests 608/0/0 不变。
- **风险备注**：V041-DEBT-1 LiteDB BsonMapper.Global 并发 race 已用 [Collection] 串行兜底，建议后续注入私有实例彻底隔离。

### V041-S2: 后端 4 个正式接口 + 阶段日志扩展
- **状态**：DONE | **级别**：M | **完成时间**：2026-04-22 | **commits**：`7efb444`
- 4 接口（list/detail/content/reparse）+ 5 阶段 stageLogs 整体覆盖 + AccessKey SHA256(16)+.pdf；reparse 通过 IHttpClientFactory 代理到 FinancialWorker:5120（5 分钟超时）；content 沙箱化 Path.GetFullPath + StartsWith 校验；6 个 [Fact] 全绿，Api.Tests 614/0/0、FinancialWorker.Tests 20/20，0 回归。
- **风险备注**：(1) Test Agent NIT — Api `AppRuntimePaths` 多识别 `Database:DataRootPath`，Worker `FinancialWorkerRuntimePaths` 仅看 `SJAI_DATA_ROOT` env，部署时统一前需注意；(2) AccessKey 旧记录在下次写入/reparse 时刷新，无后台批量迁移。

### V041-S3: 前端 FinancialPdfViewer.vue 共享组件
- **状态**：DONE | **级别**：M | **完成时间**：2026-04-22 | **commits**：`c0d2fb4`
- 原生 `<iframe>` 方案（WebView2+Chromium 双端，不引入 pdfjs-dist）；含 loadTimeoutMs 超时兜底、失败下载链接、page 跳转参数；vitest 370→377 全绿，bundle 0 增长。
- **待接入**：V041-S5 把本组件集成到对照面板。桌面 packaged 真机加载留 V041-S8 验收。

### V041-S4: 前端 FinancialPdfParsePreview + FinancialPdfVotingPanel
- **状态**：DONE | **级别**：M | **完成时间**：2026-04-22 | **commits**：`345d59c`
- 两个共享组件 + blockKindTag 工具 + financialApi 4 个 PDF 函数封装；ParsePreview 按 table/narrative_section/figure_caption/unknown 分组渲染含页码锚点 emit jump-to-page；VotingPanel 解耦 emit reparse 不直调 API；21 新用例（6+6+9）vitest 398/2，0 回归。
- **风险备注**：voting candidates 数组当前后端未暴露，VotingPanel 用「候选提取器排序与投票明细将在后续版本暴露」灰色脚注诚实标注；后端补 `candidates: [{extractor, score, vote}]` 后再扩展面板（追到 V041-DEBT-3）。

### V041-S5: 前端 FinancialReportComparePane 双栏对照（财报中心抽屉接入）
- **状态**：DONE | **级别**：M | **完成时间**：2026-04-22 | **commits**：`36e873f`
- 左 PdfViewer（透传 page prop 联动 jump-to-page）/ 右栏 Tab 切换 ParsePreview ↔ VotingPanel；reparsePdfFile 调用唯一收敛在 VotingPanel 内 emit 触发；reparse 成功整体替换 internalDetail，三字段 props 自动透传刷新；Drawer 通过 resolvePdfFileId（detail 字段优先 → listPdfFiles fallback by reportPeriod 匹配）注入 pdfFileId；文案严格区分「重新解析 PDF」vs「重新采集报告」。新增 9 vitest（7 ComparePane + 2 Drawer），共 33 files / 407 passed / 2 skipped。
- **风险备注**：Firefox 老版本可能不响应 iframe `#page=N`（属 S8 真机验收）；切换抽屉 item 时 reparse 中态用 watch 重置兜底未做 abort（成本/收益不划算）；浏览器 packaged 验收留 V041-S8 一并完成。

### V041-S6: 前端 股票详情 FinancialReportTab 轻量入口 + 重新解析按钮
- **状态**：DONE | **级别**：S | **完成时间**：2026-04-22 | **commits**：`9ace466`
- 方案 A：report-header 内嵌「📄 查看 PDF 原件」按钮 + Teleport Modal 渲染 FinancialReportComparePane 复用 S5；pdfFileId 走 listPdfFiles({symbol,reportType,pageSize:5}) 按 reportPeriod===reportDate 命中 fallback 首条，与 S5 Drawer 同款 token race-cancel；onComparePaneRefresh 仅 console.debug 不刷 trend/summary（无可回写字段）。新增 4 vitest，34 files / 418 passed / 2 skipped。
- **风险备注**：refresh 后未触发 fetchData() 局部刷新（已留 TODO）；summary.periods 为空时 fallback 取首条可能与用户期望的「当前展示报告」不完全一致，属 S 级最小实现可接受。

### V041-S7: 前端 阶段级失败可视化（5 阶段 timeline）
- **状态**：DONE | **级别**：S | **完成时间**：2026-04-22 | **commits**：`a46321d`
- FinancialPdfStageTimeline.vue 固定 5 阶段顺序 download→extract→vote→parse→persist；4 状态徽标 success/failed/skipped/pending；durationMs 自适应 ms/s 格式；compact 模式折叠 message 为 tooltip；顶部摘要含成功阶段数+总耗时+最后失败阶段（按 STAGE_ORDER 倒序找）；空 stageLogs 显示 5 个 pending + 「尚未解析」。新增 7 vitest（含大小写兼容 + compact tooltip）。
- **风险备注**：组件未集成（S8 接入时 ComparePane 紧凑场景 compact=true）；后端契约假设 stage 仅 5 个固定字符串，新增需同步扩 STAGE_ORDER；lastFailedStage 按顺序倒序找而非按 occurredAt（后端当前每阶段一条 log，无歧义）。

### V041-S8: v0.4.1 全链路验收
- **状态**：DONE | **级别**：M | **完成时间**：2026-04-23 | **commits**：`7c8e7e5` + V042 修复
- **验收标准**：
  - Test Agent：dotnet test + vitest 全绿
  - UI Designer：走查 ComparePane / timeline / 股票详情入口
  - User Rep：模拟交易员验收 6 项完成定义
  - **§9.2 页码锚点硬验证**：触发重解析 → 详情接口 parseUnits 的 page_start/page_end/block_kind 全部刷新且非空
  - 桌面 packaged + 浏览器双环境验证 PDF 内嵌预览
  - 更新 README.UserAgentTest.md + tasks.json + 写 v0.4.1 完成报告
- **R1 验收结论（2026-04-22）**：UI Designer R1 70/100（CONDITIONAL，1 BLOCKER + 2 NIT）→ FU-1 修复入口 + 版本 + 文案 → R2 85/100（PASS，2 新 NIT）→ FU-2 修 NIT → User Rep R1 38/100（**REJECT**），暴露 4 BLOCKER + 2 MAJOR 在前端集成层（后端 §9.2 接口 145/0 全绿不动）。
- **未通过项**：B1 ComparePane 选错 PDF 无切换器 / B2 packaged iframe 不渲染 / B3+B4 NIT-3+NIT-4 修复未在 packaged 生效（疑 dist 未更新，已确认 packaged `Backend/wwwroot/index.html` 仍为 2026-04-14 旧版，未随 publish 同步） / M1 reparse 按钮无 loading / M2 StageTimeline 未集成到 ComparePane
- **R3 验收结论（2026-04-23）**：全量修复后 republish（index.html 2026-04-23），Test Agent PASS（Api 616/0/0, Worker 39/0/0, vitest 443/0/2），User Rep R3 **95/100 PASS**。B1 19/20、B2 14/15、B3+B4 10/10、M1 12/15、M2 15/15、§9.2 10/10、两处入口 10/10、UX 5/5。2 MINOR 不阻塞（首次 PDF 灰屏缺 loading、reparse spinner 缓存太快未充分验证）。
- **commits**：`7c8e7e5`（FU-1 + FU-2 合并）

---

# v0.4.0 Sprint（已完成 2026-04-22）

## 交付摘要

| Story | Commit | 一句话说明 |
|-------|--------|-----------|
| V040-S1 | a911e33 | 财报列表分页+详情接口，支持筛选/排序/分页 |
| V040-S2 | 6f5f1aa/fcefd62/0dddb5b | 采集结果透明化后端，新增 reportPeriod/sourceChannel/fallbackReason 等 6 字段 |
| V040-S3 | 3597ada/807bdf6/8e17f8b/fce6408 | 财报中心前端骨架，UI Designer 98/100 |
| V040-S4 | 96d12a6 | 前端接入新字段+渠道Tag着色+筛选区hint文案优化 |
| V040-S5 | e187f8c | 详情抽屉轻量版，三表概览+重新采集+PDF占位 |
| V040-S6 | e7f278f | 全链路验收，R1 REJECT→R2 PASS 87/100（B-1字典/B-2keyword/B-3版本号修复）|
| V040-DEBT-1 | 9aa0230 | 修复 LocalFact 14 个失败用例 |
| V040-DEBT-2 | b67584b | 修复前端 10 个预存失败用例 |
| V040-DEBT-3 | aee132c | 引入 WebApplicationFactory 集成测试基础设施 |
| V040-S6-FU-1 D-1 | 5ae089f | 修复 000001 名称误显示为上证指数 |

## 版本结论

User Rep PASS 87/100；全产品巡检 CONDITIONAL_PASS 65/100（情绪轮动/推荐模块待修）。v0.4.0 财报中心子模块已发布，产品级 P0/P1 归 v0.4.4 backlog。

---

## Backlog（v0.4.1+ 待排）

- **V040-S6-FU-1**（优先级：高）：财报中心「毛利润」字段缺失。建议方案：(a) 后端补算 `营业总收入 - 营业总成本` 落库字段；(b) 前端 `pickFieldValue` 支持表达式 fallback 临时兜底。ths 渠道已确认无 `*营业总成本` 字段，需后端先补。
- **V040-S6-FU-2**（优先级：高）：后端 `GET /api/stocks/financial/reports` keyword 参数完全无效，目前前端二次过滤兜底仅限 100 条。需后端实现 symbol/name 模糊匹配，移除前端 pageSize 临时拉到 100 的逻辑。
- **V040-S3-FU-1**（优先级：中）：渠道 Tag 配色在表格列与采集面板/抽屉不统一（ths 表格绿、面板紫）。建议抽单一 `SOURCE_CHANNEL_STYLE` 作为全局 token，两处复用。至少需 emweb/datacenter/ths/pdf 四渠道真实样本到位后对比定色。
- **V040-DEBT-4**（候选）：后端 `StockSearchService` 无排序无市场过滤，腾讯 s3 API 返回 sh000001+sz000001 时下游裸消费 data[0] 会踩坑。建议后端补 market 优先级排序：沪深 A > 指数 > 其他。前端 `pickStockMatch` 已做兜底，但其他消费者（股票详情页等）仍有风险。
- **V041-DEBT-1**（优先级：中）：`FinancialDbContext` 使用 `BsonMapper.Global` 全局单例，并发测试存在 race 风险（当前用 `[Collection]` 串行兜底）。建议注入私有 `BsonMapper` 实例，彻底隔离测试与生产路径。
- **V041-DEBT-2**（优先级：低）：Api `AppRuntimePaths` 与 FinancialWorker `FinancialWorkerRuntimePaths` 的 DataRoot 解析逻辑不一致（前者多识别 `Database:DataRootPath` 配置项）。当前默认走 LOCALAPPDATA 无影响；若未来部署用配置文件覆盖根目录，可能让 Api 沙箱误判 PDF 路径越权。建议统一到一个 RuntimePaths helper。
- **V041-DEBT-3**（优先级：中）：后端 PdfFileDetail 仅暴露单字符串 `extractor` / `voteConfidence`，未提供 voting candidates 数组。建议后端在 PdfFileDocument 增补 `VotingCandidates: [{ Extractor, Score, FieldCount }]`，前端 FinancialPdfVotingPanel 已预留占位文案，候选数据到位后扩展面板即可（无需重构组件契约）。

## v0.4.2 Sprint 必修（v0.4.1 验收 REJECT 遗留）

- **V042-P0-A** (BLOCKER B1)：FinancialReportComparePane 默认按 LastParsedAt desc 取 items[0]，会选到摘要/英文版而非主报告。需新增 PDF 选择器（下拉/Tab），按 fieldCount > 0 优先 + 文件名启发式（不含「摘要」「英文」）+ 用户可手动切换。
- **V042-P0-B** (BLOCKER B2)：packaged WebView2 内 PDF iframe 不渲染。排查方向：(1) PDF content URL 在 desktop 内置浏览器路径解析；(2) WebView2 PDF viewer 是否需要显式启用；(3) Content-Security-Policy / X-Frame-Options。建议先在 packaged 环境单独验证 `<iframe src="http://localhost:5119/api/.../content">` 是否能渲染。
- **V042-P0-C** (BLOCKER B3)：NIT-3 修复（股票详情 listPdfFiles 仅传 symbol）在 vitest 通过但 packaged 未生效。先确认 publish 是否真的重新构建前端 dist，再决定是 publish 流程 bug 还是 fallback 逻辑还有遗漏。
- **V042-P0-D** (BLOCKER B4)：NIT-4 修复（silentRefresh + resolvePdfId 不抖动）同上，packaged 未生效。
- **V042-P1-A** (MAJOR M1)：ComparePane reparse 按钮无 loading 反馈。VotingPanel 已有 reparsing prop，但 ComparePane 未正确透传或顶部按钮没复用 panel 状态。
- **V042-P1-B** (MAJOR M2)：FinancialPdfStageTimeline 组件已交付但未集成到 ComparePane。建议在右栏新增「解析阶段」Tab 或固定底部展示 stageLogs。
- **V042-P2-A**：复查 publish-windows-package.ps1 的前端构建步骤，确认每次都强制 `npm run build` 且把 dist 同步到 packaged Backend/frontend/dist 与 wwwroot。已观察到 packaged `Backend/wwwroot/index.html` 时间戳停留在 2026-04-14（与 dist 不同步），疑似 publish 脚本只刷新 dist 未刷新 wwwroot。

## v0.4.3 Backlog（20260423 人工测试 Bug）

> 来源：`.automation/buglist.md`。等待 v0.4.2 完成后开始。

- **V043-P0-A** (BUG-2 金额单位不一致)：ths 通道 `ParseChineseNumber` 输出万元，emweb 输出元，混合时差 10000 倍。修复：统一所有通道存储单位为元。**级别 S，数据正确性**。
- **V043-P1-B** (BUG-4 历史数据无法获取)：采集器只拉最近 4-5 期，选 2023 年无数据。修复：后端支持 endDate 参数；前端空态引导。**级别 M**。
- **V043-P1-C** (BUG-1 PDF采集误报成功)：前端不检查 processedCount。修复：检查返回值，展示采集结果摘要。**级别 M**。
- **V043-P1-D** (BUG-3 重新采集无反馈)：前端忽略 CollectionResult。修复：展示通道/报告数/耗时/降级原因。**级别 M**。

## v0.4.4 Backlog（产品级 P0/P1，非财报路线图）

> 来自 v0.4.0 全产品巡检（CONDITIONAL_PASS 65/100）。待 v0.4.1~v0.4.3 完成后排期。

- **V044-P0-A**：13-Agent 推荐「完成」状态语义错乱。20 条历史唯一「完成」实为失败 694 秒，Agent 反问用户。排查入口：`StockRecommendationHub` session 状态写入逻辑，确认「失败」与「完成」分支是否共用路径。修法：失败绝不标完成，需区分「完成/失败/用户中止」三态。
- **V044-P0-B**：情绪轮动核心榜单全线「数据不可用」。排查入口：`MarketSentimentSyncService` + `BoardSnapshot` ingestion 链路；先确认 `board_ranking` 集合是否真为空，再看 `MarketSentimentTab` 前端是否有空态短路。至少补最近一次有效快照时间戳作为空态提示。
- **V044-P1-C**：股票详情基本面 3 字段空（流通市值/股东户数/所属板块）。核对腾讯/东财字段名与单位换算，缺失走 `--` 兜底。
- **V044-P1-D**：主力净流入跨页数值矛盾（-69.22 亿 vs -0.0 亿）。疑单位换算 bug，建议抽 `formatMoneyYi(value, sourceUnit)` 统一路径后排查。
- **V044-P1-E**：Worker 运行中但日志面板 0 条。核查 Serilog sink → SignalR/SSE → 前端订阅链路，确认 sink 未被环境变量禁用。
- **V044-P1-F**：失败态按钮文案「重新连接」改为「重新推荐」或「再试一次」，语义对用户透明。
- **V044-NIT**：N-1 财报样本仅 ths 单渠道 / N-2 财报字段单位混乱（万/亿/元无标识）/ N-3 资讯卡英文未译 / N-4「市场数据加载中」常驻。批量小修。

## 历史归档

- v0.3.2 散户热度反向指标 → `/memories/repo/sprints/v0.3.2-retail-heat-contrarian.md`
- v0.3.2 S7 市场数据不可用恢复（开发完成，盘中验收 4/21–4/23 独立跟踪）
- `tasks.json` 自 2026-04-22 起仅作 append-only 历史事件流，不再用作活跃任务管理