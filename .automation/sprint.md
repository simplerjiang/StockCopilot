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
