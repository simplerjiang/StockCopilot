# 简历项目介绍 — StockCopilot（AI 股票研究助手）

> 本文件为简历写作素材，提供中英双语版本，可按需裁剪引用。

---

## 一、项目介绍（中文版）

### 项目名称
**StockCopilot — 本地化 AI 股票研究工作站**

### 一句话描述
基于 13 个 LLM 智能体协同的本地优先（Local-First）A 股研究工作站，将行情监控、新闻聚合、技术分析与 AI 决策辅助整合于一体，确保数据可控、推理可解释。

### 背景与目标
个人股票研究者在日常操作中面临工具分散、数据依赖云端、AI 推理黑盒等挑战。本项目以"数据主权 + 可解释 AI"为核心理念，构建一套完全本地运行的智能研究平台，帮助交易者在统一界面内完成从信息收集到决策执行的全流程闭环。

### 核心功能
- **多源行情采集**：聚合东方财富、新浪、腾讯、百度等 15+ 数据源，含自动降级与健康评分机制
- **K 线终端**：专业级 K 线与分时图（KLineCharts v10），支持日/周/月线及多信号覆盖（TD Sequential、MACD 金叉、支撑阻力）
- **AI 新闻处理管道**：批量 LLM 翻译、情感标注、影响分类，构建本地事实数据库（Local Fact DB）
- **13 智能体推荐系统**：宏观 → 板块辩论 → 选股 → 个股辩论 → 终裁，五阶段流水线，SSE 实时进度推送
- **交易计划管理**：草稿/执行/追踪全生命周期，含新闻触发合规审查
- **市场情绪看板**：沪深指数、板块轮动（5/10/20 日）、市场情绪分类（亢奋/牛市/中性/熊市/恐慌）
- **开发者审计模式**：全量 LLM 请求/响应日志，前端可视化 traceId 追踪

### 技术栈概览

| 层级 | 技术选型 |
|------|---------|
| 桌面壳 | .NET 8 WinForms + WebView2 |
| 后端   | ASP.NET Core 8 Web API + EF Core |
| 数据库 | SQLite（WAL 模式）/ SQL Server / MySQL（可配置） |
| 前端   | Vue 3 + Vite + Composition API |
| 图表   | KLineCharts v10 + ECharts |
| AI 接入 | OpenAI 兼容接口 + Google Antigravity（OAuth 2.0 + PKCE） |
| 测试   | Vitest（前端单元）+ Playwright（E2E）+ xUnit（后端） |

---

## 二、技术亮点

### 1. 13 智能体多角色协同推荐架构

设计并实现五阶段、13 个 LLM 智能体的协同推荐流水线（3,000+ LOC）：

- **阶段一（并行）**：宏观分析师、板块猎手、聪明钱分析师同步执行
- **阶段二（辩论）**：板块多空方最多 3 轮辩论 + 裁判智能体仲裁
- **阶段三（并行+串行）**：龙头选股、成长选股并行 → 图形验证串行
- **阶段四（辩论）**：个股多空方辩论 + 风险审查员
- **阶段五（终裁）**：导演智能体输出结构化推荐卡（含置信度、目标价、止损位、有效期）

全流程通过 **Server-Sent Events（SSE）** 实时向前端推送执行状态，用户可在推荐生成过程中实时观察每个智能体的思考进展。

### 2. 本地事实数据库 + MCP 工具网关

构建**模型上下文协议（MCP）**工具网关，使智能体在推理时可按需查询本地结构化数据，而非依赖 LLM 互联网调用：

- 本地新闻与公告数据库实时检索
- 市场情绪、板块轮动历史数据查询
- 公司基本面快照获取
- 历史研报与交易计划上下文注入

这一设计显著降低了幻觉风险，并使推理过程完全可审计。

### 3. 可插拔 LLM 提供商抽象层

通过 `ILlmProvider` 接口统一封装多家 LLM：

- **OpenAI 兼容层**：支持 GPT-4o、Claude（via OpenRouter）、Gemini（via 代理）
- **Google Antigravity 层**：Claude Opus/Sonnet、Gemini 3、GPT-OSS，含 OAuth 2.0 + PKCE 鉴权与三端点自动降级（沙箱 → 预发 → 生产）
- 按请求动态路由（标准模型 vs Pro 模型），无需修改业务代码

### 4. 高可靠多源爬虫与自动降级

- **复合爬虫策略**：东方财富为主，腾讯/新浪/百度为备，任一源失败自动切换
- **数据源治理**：`NewsSourceRegistry` 追踪每个源的解析成功率、新鲜度延迟、时间戳覆盖率
- **健康评分系统**：后台工作者定期评分并自动暂停低质量源，防止脏数据污染本地 DB

### 5. 专业级 K 线图表引擎（KLineCharts v10）

- 自研图表注册表（Chart Registry），支持 MA、MACD、KDJ、布林带、成交量等多指标统一管理
- 实现 TD Sequential 信号标注（6-9 阶段可见，分色预警）
- MACD 金叉/死叉信号标记，含合成 K 线 fixture 单元测试锁定计算逻辑
- 图例控制器与全屏管理器，状态与浏览器 `fullscreenchange` 生命周期绑定

### 6. 全量 LLM 审计日志与开发者模式

- 所有 LLM 请求/响应以 `traceId` 为索引写入本地日志文件
- 前端开发者模式可视化展示请求链路、执行时长、模型名称、Token 消耗
- 为合规审查与调试提供完整的可观测性

---

## 三、解决痛点

| 痛点 | 现状问题 | 本项目方案 |
|------|---------|-----------|
| **工具分散** | 行情、新闻、研报、交易计划分散在多个 App/网站，切换成本高 | 统一桌面工作站，一个界面覆盖全流程 |
| **数据依赖云端** | 云端工具断网即瘫，隐私数据上传第三方，API 限速频繁 | Local-First 架构，所有数据本地存储，离线可用 |
| **AI 推理黑盒** | ChatGPT/Kimi 等通用 AI 无法访问实时行情，推理依据不透明 | MCP 工具网关注入本地实时数据，每步推理可溯源 |
| **单一视角偏差** | 单个 AI 提问易受用户提问方式影响，存在确认偏误 | 多空辩论架构强制对立观点碰撞，最终裁判综合 |
| **新闻信息过载** | 每日数百条新闻难以筛选，人工判断耗时 | AI 批量翻译+情感标注+影响分类，本地事实库快速检索 |
| **图表工具不足** | 免费 K 线工具信号少，高级工具订阅费用高 | 自研图表注册表，TD 序列、MACD 信号等免费开源 |
| **交易计划执行散乱** | 交易计划存于笔记或记忆中，缺乏触发与追踪 | 结构化交易计划管理 + 新闻触发合规审查闭环 |

---

## 四、项目介绍（英文版，适用于英文简历）

### Project: StockCopilot — Local-First AI Stock Research Workstation

**One-liner**: A local-first Windows desktop application that orchestrates 13 LLM agents across a 5-stage debate pipeline to deliver explainable, auditable stock recommendations for Chinese A-share markets.

**What I built**:
- Designed and implemented a **13-agent collaborative recommendation system** (5 stages: market scan → sector debate → stock picking → stock debate → final decision) with real-time SSE progress streaming to the frontend.
- Built a **pluggable LLM provider abstraction** (`ILlmProvider`) supporting OpenAI-compatible APIs and Google Antigravity (OAuth 2.0 + PKCE, 3-endpoint auto-fallback), enabling zero-code provider switching.
- Architected a **Model Context Protocol (MCP) tool gateway** so agents query a local fact database at inference time — eliminating hallucinations from stale internet context and making every inference step fully auditable.
- Developed a **multi-source crawler pipeline** (15+ sources: Eastmoney, Sina, Tencent, Baidu) with automatic failover, source health scoring, and batch AI news enrichment (translation, sentiment tagging, impact classification).
- Integrated **KLineCharts v10** with a custom chart registry supporting MA, MACD, KDJ, Bollinger Bands, TD Sequential signals, and MACD crossover markers — all locked by unit test fixtures.
- Built the full **Vue 3 + ASP.NET Core 8** stack with EF Core (SQLite WAL mode), background workers (quote sync, news ingestion, source governance), and a WinForms + WebView2 desktop shell.

**Tech**: C# / ASP.NET Core 8, Vue 3 / Vite, EF Core, SQLite, KLineCharts, ECharts, OpenAI API, OAuth 2.0 + PKCE, SSE, MCP, Vitest, Playwright, xUnit

---

## 五、精简版（适合简历条目格式）

**StockCopilot | 个人开源项目 | 2024–至今**
`Vue 3` `ASP.NET Core 8` `LLM` `KLineCharts` `SQLite` `SSE`

- 设计并实现 **13 智能体五阶段协同推荐系统**，含多空辩论架构与 SSE 实时进度推送
- 构建 **MCP 工具网关**，使 LLM 智能体在推理时按需查询本地行情/新闻/交易计划数据，推理可溯源可审计
- 实现 **可插拔 LLM 提供商层**，统一封装 OpenAI、Google Antigravity（OAuth 2.0 + PKCE），支持三端点自动降级
- 搭建 **多源爬虫管道**（15+ 数据源）含自动降级与健康评分，批量 AI 新闻翻译/情感标注构建本地事实数据库
- 集成 **KLineCharts v10** 图表注册表，实现 TD Sequential、MACD 金叉信号，含合成数据单元测试覆盖
- 全量 **LLM 审计日志**（traceId 追踪），前端开发者模式可视化完整请求链路

---

*生成日期：2026-04-03 | 项目地址：https://github.com/simplerjiang/StockCopilot*
