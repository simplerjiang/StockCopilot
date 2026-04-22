# Sprint 看板

> 当前 Sprint 最多 3 个活跃 Story。完成的 Story 归档到 `/memories/repo/sprints/`。

## Sprint 目标

**v0.4.0 财报中心基础落地**：把现有的财务数据测试工具升级为正式业务页面 `财报中心`，实现采集结果透明化与本地财报数据表格化。是 v0.4.x 路线的第一阶段。

参考路线图：[docs/GOAL-v040-financial-report-roadmap.md](../docs/GOAL-v040-financial-report-roadmap.md)
参考详细计划：[docs/GOAL-v040-financial-center-foundation.md](../docs/GOAL-v040-financial-center-foundation.md)

## 当前 Stories

### Story V040-S1: 后端财报列表分页查询接口
- **状态**：TODO
- **级别**：M
- **验收标准**：
  - 新增 `GET /api/financial/reports?symbol=&reportType=&startDate=&endDate=&page=&pageSize=&sort=` 分页接口
  - 新增 `GET /api/financial/reports/{id}` 详情接口（返回三表概览 + 元数据）
  - 排序支持 `reportDate desc/asc`、`updatedAt desc/asc`
  - 分页响应包含 `total / pageSize / page / items`
  - 单元测试覆盖：空集 / 单页 / 多页 / 筛选组合 / 不存在的 id
- **依赖**：无

### Story V040-S2: 采集结果透明化（后端）
- **状态**：TODO
- **级别**：S
- **验收标准**：
  - `/api/stocks/financial/collect/{symbol}` 响应增加：`reportPeriod / reportTitle / sourceChannel / fallbackReason / pdfSummary`
  - 同字段落库到 `collection_logs`
  - 替换"只报数量"的旧响应格式（保持向后兼容字段，但补齐新字段）
  - 单元测试覆盖：成功 / 降级 / 失败 / PDF 补充触发 4 个路径
- **依赖**：无

### Story V040-S3: 财报中心前端页面骨架
- **状态**：TODO
- **级别**：M
- **验收标准**：
  - 新增路由 `/financial-center` 与左侧导航入口
  - 表格列：股票代码 / 名称 / 报告期 / 类型 / 来源渠道 / 采集时间 / 操作
  - 筛选区：股票多选 / 报告期范围 / 报告类型多选 / 关键词
  - 分页 + 排序（点击表头切换）
  - 详情抽屉入口（详情内容由 V040-S5 实现）
  - 浏览器验收（Browser MCP）：实际筛选/分页/排序操作 + 控制台无错误
- **依赖**：V040-S1

### Story V040-S4: 采集结果透明化（前端 UI 接入）
- **状态**：TODO
- **级别**：S
- **验收标准**：
  - `FinancialDataTestPanel.vue` 与 `FinancialReportTab.vue` 显示新字段
  - 替换"只报数量"展示
  - 降级原因用 Tag 着色（emweb/datacenter/ths/pdf 不同色）
  - 单元测试 + Browser 抽测
- **依赖**：V040-S2

### Story V040-S5: 详情抽屉（轻量版，不含 PDF 预览）
- **状态**：TODO
- **级别**：S
- **验收标准**：
  - 抽屉显示：报告期 / 标题 / 来源 / 采集时间 / 三表概览（前 5 个关键字段）/ 元数据
  - "重新采集"按钮触发 `POST /api/stocks/financial/collect/{symbol}`
  - PDF 预览不在本 Story（v0.4.1 实现）
  - 占位区域写明"PDF 原件预览将在 v0.4.1 提供"
- **依赖**：V040-S1, V040-S3

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

## 历史归档

- v0.3.2 散户热度反向指标 → `/memories/repo/sprints/v0.3.2-retail-heat-contrarian.md`
- v0.3.2 S7 市场数据不可用恢复（开发完成，盘中验收 4/21–4/23 独立跟踪）
# Sprint 看板

> 当前 Sprint 最多 3 个活跃 Story。完成的 Story 归档到 `/memories/repo/sprints/`。

## Sprint 目标

v0.3.2：实现散户热度反向指标（Retail Heat Contrarian Index），通过爬取 A 股论坛社区帖子量生成量化反向信号。

## 当前 Stories

### Story S0: 爬虫可行性 Spike
- **状态**：DONE
- **结果**：东方财富 ✅、新浪 ✅、淘股吧 ⚠️（有条件可行）；雪球/同花顺/百度 ❌

### Story S1: 数据模型
- **状态**：DONE
- **结果**：ForumPostCounts 表 + SchemaInitializer，SQLite/SQL Server 双路径

### Story S2: 爬虫引擎 + 定时采集
- **状态**：DONE
- **结果**：3 个 Scraper + ForumScrapingWorker (9:00/12:00/15:30)，周末自动跳过

### Story S3: 热度指数计算服务
- **状态**：DONE
- **结果**：RetailHeatIndexService，日增量 delta 计算 + MA20 + HeatRatio 信号分级

### Story S4: SocialSentiment MCP 集成
- **状态**：DONE
- **结果**：论坛热度数据注入 SocialSentimentMcp，status 从 degraded 升级到 ok

### Story S5: K线图热度子窗格
- **状态**：DONE
- **结果**：klinecharts 自定义指标 RETAIL_HEAT，温度色阶 bar 图，tooltip 含平台数

### Story S6: 验收修复
- **状态**：DONE
- **结果**：日增量替代累计总量、前后端阈值对齐、反向指标警告、ST 排除提示、周末跳过

### Story S7: 市场数据不可用恢复（P0）
- **状态**：IN_PROGRESS
- **结果**：代码改造与离线验证已完成；日检脚本已更新（exit 1=partial/exit 4=failed）；DataAuditPanel 已展示 sourceHealthy/businessComplete；仅剩盘中 3 轮验收（4/21-4/23）
- **复核备注**：2026-04-20 复核：当前仍处于验收收口阶段，状态保持 IN_PROGRESS，不改为 DONE
