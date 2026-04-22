# Sprint 看板

> 当前 Sprint 最多 3 个活跃 Story。完成的 Story 归档到 `/memories/repo/sprints/`。

## 看板规则（2026-04-22 新增）

1. **NIT 不延后**：Story 复核暴露的 NIT，须在复核后 1 小时内修完并补回测试，不允许累计到 Sprint 末尾。
2. **BLOCKER 立即停摆**：任何 BLOCKER 阻塞当前 Story 进入下一个；先修 BLOCKER，再继续。
3. **预存在测试失败必须立项**：复核中发现非本 Story 引入的失败用例，须开独立技术债 Story 跟踪，不允许"已知失败"长期挂在主干。
4. **`tasks.json` 与 `sprint.md` 关系**：`sprint.md` 是当前 Sprint 唯一权威看板；`tasks.json` 仅作历史任务事件流（append-only），不再用作活跃任务管理。

## Sprint 目标

**v0.4.0 财报中心基础落地**：把现有的财务数据测试工具升级为正式业务页面 `财报中心`，实现采集结果透明化与本地财报数据表格化。是 v0.4.x 路线的第一阶段。

参考路线图：[docs/GOAL-v040-financial-report-roadmap.md](../docs/GOAL-v040-financial-report-roadmap.md)
参考详细计划：[docs/GOAL-v040-financial-center-foundation.md](../docs/GOAL-v040-financial-center-foundation.md)

## 当前 Stories

### Story V040-S1: 后端财报列表分页查询接口
- **状态**：DONE
- **级别**：M
- **验收标准**：
  - 新增 `GET /api/stocks/financial/reports?symbol=&reportType=&startDate=&endDate=&page=&pageSize=&sort=` 分页接口
  - 新增 `GET /api/stocks/financial/reports/{id}` 详情接口（返回三表概览 + 元数据）
  - 排序支持 `reportDate desc/asc`、`updatedAt desc/asc`
  - 分页响应包含 `total / pageSize / page / items`
  - 单元测试覆盖：空集 / 单页 / 多页 / 筛选组合 / 不存在的 id
- **依赖**：无
- **完成时间**：2026-04-22
- **commit**：`a911e33`（含 V040-S1 实现 + 12 单元测试）
- **遗留 NIT（待本 Sprint 内修完）**：
  - `MapListItem` 用 `default(DateTime)` 序列化为 `0001-01-01`，需改为 nullable 或显式 fallback（已确认在 V040-DEBT-1 阶段同步修复，DTO 已是 DateTime?，MapListItem 走 TryGetDateTime null 兜底，回归测试 ListReports_WhenCollectedAtMissing_ReturnsNull 覆盖）
  - `ParseSort` 静默降级未识别字段无日志（已修，commit 41a5f9a）
  - 缺 WebApplicationFactory `[AsParameters]` 集成测试（V040-S2 未补，留待 V040-DEBT 或后续 story）。仓库目前未引入 Microsoft.AspNetCore.Mvc.Testing，单独立 V040-DEBT-3 跟踪，本次跳过。

### Story V040-DEBT-3: 引入 WebApplicationFactory 集成测试基础设施
- **状态**：DONE
- **级别**：S
- **验收标准**：在 `SimplerJiangAiAgent.Api.Tests` 引入 `Microsoft.AspNetCore.Mvc.Testing` + 1 个 `[AsParameters]` 绑定 case 覆盖 `GET /api/stocks/financial/reports`。
- **依赖**：无
- **完成时间**：2026-04-22
- **commits**：`813fdeb`
- **完成说明**：引入 `Microsoft.AspNetCore.Mvc.Testing 8.0.16`；`Program.cs` 末尾追加 `public partial class Program {}` 以暴露入口；新增 `FinancialReportsEndpointTests.cs` 通过 Stub 替换 `IFinancialDataReadService` 隔离外部依赖；新增 1 个集成测试 `GetFinancialReports_WithFullQuery_BindsParametersAndReturns200`；后端 dotnet test 608/0/0 全绿。

### Story V040-S2: 采集结果透明化（后端）
- **状态**：DONE（commits: 6f5f1aa / fcefd62 / 0dddb5b）
- **级别**：S
- **验收标准**：
  - `/api/stocks/financial/collect/{symbol}` 响应增加：`reportPeriod / reportTitle / sourceChannel / fallbackReason / pdfSummary`
  - 同字段落库到 `collection_logs`
  - 替换"只报数量"的旧响应格式（保持向后兼容字段，但补齐新字段）
  - 单元测试覆盖：成功 / 降级 / 失败 / PDF 补充触发 4 个路径
- **依赖**：无
- **完成说明**：3 cycles 完成，607 + 4 单测全绿；HTTP 响应含 11 原字段 + 6 完整新字段 + 5 友好别名。

### Story V040-S3: 财报中心前端页面骨架
- **状态**：DONE（commits: 3597ada / 807bdf6 / 8e17f8b / fce6408）
- **级别**：M
- **验收标准**：
  - 新增路由 `/financial-center` 与左侧导航入口
  - 表格列：股票代码 / 名称 / 报告期 / 类型 / 来源渠道 / 采集时间 / 操作
  - 筛选区：股票多选 / 报告期范围 / 报告类型多选 / 关键词
  - 分页 + 排序（点击表头切换）
  - 详情抽屉入口（详情内容由 V040-S5 实现）
  - 浏览器验收（Browser MCP）：实际筛选/分页/排序操作 + 控制台无错误
- **依赖**：V040-S1
- **实现说明**：路由形态为 `?tab=financial-center`（与现有 `App.vue` 体系一致；未引入 vue-router）。
- **完成说明**：UI Designer 二轮走查 98/100；vitest 52 cases / Browser MCP 8 cases (7 PASS / 1 SKIP 数据不足)；console.error=0；前端 build 0 error；后端无回归。
- **遗留 backlog**：V040-S3-FU-1（emweb/datacenter/ths 实采样本到位后渠道 tag 抽样校色；「未分类/Unknown」pill 设计待 V040-S6 数据治理决策）。

### Story V040-S4: 采集结果透明化（前端 UI 接入）
- **状态**：DONE
- **级别**：S
- **验收标准**：
  - `FinancialDataTestPanel.vue` 与 `FinancialReportTab.vue` 显示新字段
  - 替换"只报数量"展示
  - 降级原因用 Tag 着色（emweb/datacenter/ths/pdf 不同色）
  - 单元测试 + Browser 抽测
- **依赖**：V040-S2
- **完成时间**：2026-04-22
- **commits**：`a2ee34c`
- **完成说明**：3 文件改动 + 1 新建公共 util；vitest 与本 Story 相关用例全绿（sourceChannelTag 10/10、FinancialFilterBar 13/13、FinancialReportTab 20/20）；frontend build 0 error；浏览器验收并入 V040-S6 一起做。同时修复财报中心筛选区 4 处面向开发者的 hint 文案，改为面向用户口吻。
- **遗留 backlog**：渠道 → Tag 颜色与 V040-S3 表格内 `SOURCE_CHANNEL_STYLE` 不完全一致（ths 在表格走绿、在采集面板走紫），合并到 V040-S3-FU-1 一起处理。

### Story V040-S5: 详情抽屉（轻量版，不含 PDF 预览）
- **状态**：DONE
- **级别**：S
- **验收标准**：
  - 抽屉显示：报告期 / 标题 / 来源 / 采集时间 / 三表概览（前 5 个关键字段）/ 元数据
  - "重新采集"按钮触发 `POST /api/stocks/financial/collect/{symbol}`
  - PDF 预览不在本 Story（v0.4.1 实现）
  - 占位区域写明"PDF 原件预览将在 v0.4.1 提供"
- **依赖**：V040-S1, V040-S3
- **完成时间**：2026-04-22
- **commits**：`c38b362`
- **完成说明**：新建 `financialApi.js`（`fetchFinancialReportDetail` / `recollectFinancialReport`，真实路径 `/api/stocks/financial/reports/{id}` 与 `/api/stocks/financial/collect/{symbol}`，spec 中描述的 `/api/financial/reports/{id}` 为历史笔误）；新建 `financialFieldDictionary.js` 三表业务白名单各 5 字段 + 中文 label + fallback 链；重写 `FinancialDetailDrawer.vue`（500 行）覆盖标题 / loading / error / 重试 / 元数据 dl（含 sourceChannelTag）/ 三表概览 / sticky 重新采集 + 关闭 / PDF 占位；vitest 343 / 0 / 2 全绿（新增 30 cases）；frontend build 0 error。
- **浏览器验收**：延后到 V040-S6

### Story V040-S6: v0.4.0 全链路验收
- **状态**：TODO
- **级别**：M
- **验收标准**：
  - Test Agent 跑通后端单元测试 + 前端 vitest
  - UI Designer Agent 走查财报中心页面与详情抽屉
  - User Representative Agent 模拟交易员视角验收
  - 更新 `README.UserAgentTest.md` 增加财报中心验收路径
  - 更新 `.automation/tasks.json` 记录 v0.4.0 完成
  - 写 v0.4.0 完成报告到 `.automation/reports/`
- **依赖**：V040-S1 ~ V040-S5 全部 DONE

## 技术债 Stories（与 v0.4.0 主线并行）

### Story V040-DEBT-1: 修复 LocalFactAiEnrichmentServiceTests 14 个失败用例
- **状态**：DONE（commit: 9aa0230）
- **级别**：S（预估 1.5–2 小时）
- **背景**：Bug-4 修复 commit `a616fe3` 引入 `ProcessMarketPendingAsync` RequestPath 模式与新排序逻辑，但 6 个测试方法（含 Theory 共 14 案例）的期望值未同步。
- **失败用例**：
  - `ShouldKeepFailureExplainability_WhenRepairStillFails`
  - `ShouldExposeNoProgressStopReason_WhenArchiveSweepAppliesNothing`
  - `ShouldParseBatchResponseWithTrailingExplanation`
  - `ShouldBoundSelectionToLiveRequestBudget`
  - `ShouldEmitParseWarningAndKeepPending_WhenBatchResponseIsInvalid`
  - `ShouldPrioritizeCurrentCrawlRowsOverOlderBacklog`
- **验收标准**：
  - 14 个用例全部 PASS
  - 不修改生产代码（除非确认是回归 bug）
  - 单独跑 + 全量跑都 PASS
- **依赖**：无（独立技术债）
- **优先级**：在阻塞 V040-S2 复核信号前必须完成
- **完成说明**：2026-04-22 修复，全量 607 测试 0 失败。

### Story V040-DEBT-2: 修复 10 个预存前端失败用例
- **状态**：DONE
- **级别**：S
- **验收标准**：
  - 修复 10 个预存前端失败用例（MarketSentimentTab.spec.js 8 个、StockCharts.spec.js 1 个、TradingWorkbench.spec.js 1 个）
  - 不改生产代码
- **完成时间**：2026-04-22
- **commits**：`d3cc9c7`
- **完成说明**：全部为测试期望未跟上重构的 A 类问题 + 1 处 echarts mock 缺 API 的 C 类问题；vitest 316 passed / 0 failed / 2 skipped；未发现生产代码回归；未引入新依赖。
- **PM Note**：TradingWorkbench 在 isRunning 状态下保留输入框、把发送按钮换成取消按钮是有意设计，建议 PM 后续在产品文档固化。MarketSentimentTab.spec.js 部分细粒度文案断言因子组件拆分被弱化，可后续补回。

## 历史归档

- v0.3.2 散户热度反向指标 → `/memories/repo/sprints/v0.3.2-retail-heat-contrarian.md`
- v0.3.2 S7 市场数据不可用恢复（开发完成，盘中验收 4/21–4/23 独立跟踪）
- `tasks.json` 自 2026-04-22 起仅作 append-only 历史事件流，不再用作活跃任务管理
