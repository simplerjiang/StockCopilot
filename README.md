# StockCopilot

一个面向 **A 股研究与交易纪律执行** 的本地优先桌面工作台（local-first trading research workstation）。

它把日常会反复切换的几类工作收进一个桌面应用里：**股票终端、市场轮动、资讯归档、交易计划、交易日志、推荐协驾，以及 LLM / 治理 / 财务运维**。项目当前重点仍是“辅助决策 + 纪律闭环”，而不是自动下单。

## 项目定位

`StockCopilot` 是一个完整的多端协作工程，而不是单一脚本或 Demo：

- **Backend**：ASP.NET Core 8 Web API，负责数据同步、存储、配置与业务接口
- **Frontend**：Vue 3 + Vite 工作台界面，负责图表、资讯展示和交互体验
- **Desktop Shell**：.NET 8 WinForms + WebView2，将前后端封装为本地桌面应用
- **Local-first runtime**：支持本地 SQLite 运行，并保留切换数据库提供方的能力
- **Delivery pipeline**：支持 Windows 安装包、便携包、更新检测与发布链路

## 核心能力

- **股票终端与个股工作区**：支持 cache-first 详情加载、分时/日 K/月 K/年 K、图表 chip 与全屏、基本面快照、市场上下文、新闻影响、财报页、交易计划起草/编辑/复核
- **情绪轮动与市场总览**：通过情绪轮动页查看市场阶段、主线板块、比较窗口、实时板块榜和市场快照
- **本地资讯库**：沉淀并检索个股、板块、市场多层级资讯，支持 AI 清洗状态展示和待处理批量清洗
- **股票推荐**：多 Agent 推荐系统，覆盖市场扫描、板块分析、选股、辩论、决策五大阶段，支持 SSE 进度、会话历史、追问与 traceId
- **交易计划 + 交易日志闭环**：支持计划草稿、总览、提醒、市场上下文、交易录入、持仓总览、胜率、做T 盈亏、AI 复盘与复盘历史
- **开发者治理与运维面板**：包括 LLM 设置、Antigravity / Ollama 本地模型管理、LLM 审计日志与 trace 查询、财务数据测试面板
- **桌面交付**：安装后即可运行，不需要用户手动分别启动前后端与数据库
- **财报数据中心**：通过独立 Financial Worker 采集季度/年度财务数据，并在股票信息页内提供可刷新财务报表展示

## 技术栈

### Backend

- .NET 8
- ASP.NET Core Web API
- Entity Framework Core
- SQLite / SQL Server / MySQL（按配置切换）
- Swagger / OpenAPI

### Frontend

- Vue 3
- Vite
- ECharts
- KLineCharts
- Vitest / Playwright（前端测试与自动化能力）

### Desktop

- .NET 8 WinForms
- WebView2
- Windows 本地打包与安装分发

## 项目结构

```text
backend/   ASP.NET Core API、数据同步、业务模块、存储与配置
frontend/  Vue 3 工作台、图表与交互页面
desktop/   Windows 桌面壳，负责本地运行与嵌入前端
scripts/   打包、发布、自动化辅助脚本
docs/      截图与补充文档
```

## 截图

### 首页 · 市场总览 + 股票终端

主界面集成了市场总览（三大指数、全球指数、主力资金、涨跌广度、封板温度）和个股看盘终端，支持快速搜索和一键切换。

![首页总览](docs/screenshots/09-homepage-1920x1080.png)

### 多源新闻聚合

右侧边栏实时展示来自 **财联社电报、新浪滚动、CNBC、Seeking Alpha、Investing.com** 等 15+ 数据源的市场资讯，带情绪标签和 AI 分析标注。

![新闻影响面板](docs/screenshots/02-news-impact-1920x1080.png)

### 个股深度分析

支持个股事实（公告、板块上下文）和 AI 生成的资讯影响分析，利好/利空/中性分类清晰。

![个股分析](docs/screenshots/04-kline-detail-1920x1080.png)

### 情绪轮动

把涨停强度、炸板率与板块扩散度压成同一屏，快速判断主升、分歧还是退潮，并可查看每个板块的龙头分布和近期趋势。

![情绪轮动](docs/screenshots/06-sentiment-top-1920x1080.png)

### 全量资讯库

统一检索本地事实库中已清洗的个股、板块与大盘资讯。支持评级筛选、情绪过滤与原文跳转，目前已累积 500+ 条资讯。

![全量资讯库](docs/screenshots/07-news-archive-1920x1080.png)

### 股票推荐 · 多 Agent 辩论系统

13 个 LLM Agent 协作完成从市场扫描到个股推荐的全流程分析。支持实时 SSE 进度推送、辩论过程可视化、团队进度面板、推荐报告卡片（含置信度、目标价、止损位）、历史会话管理与追问。

![股票推荐](docs/screenshots/08-stock-recommend-1920x1080.png)

## 当前主分支状态

当前主分支的工作台已经包含 8 个顶层页签：**股票信息、情绪轮动、全量资讯库、交易日志、股票推荐、LLM 设置、治理开发者模式、财务数据测试**，以及管理面板中的**财务工作者监控**。

最近已落地主线包括：

- 股票信息页的 cache-first 详情链路、图表终端、基本面快照、市场上下文、财务报表和交易计划工作区
- 交易计划生命周期：草稿、总览、提醒、新闻复核、ActiveWatchlist 驱动的触发/复核链路
- 交易日志 / 纪律闭环基线：持仓总览、胜率、做T 盈亏、风险敞口、健康度、AI 复盘与复盘历史
- 股票推荐页的推荐前市场快照、SSE 进度、会话历史、追问与 traceId
- 治理开发者模式的 trace 查询与 LLM 审计日志查看
- 财务数据中心与独立 Financial Worker
- Ollama 本地模型启停、模型拉取、keepAlive 管理，以及 `num_ctx / keep_alive / num_predict / temperature / top_k / top_p / min_p / stop / think` 等请求级高级参数
- **v0.3.0**：修复本地模型完整 AI 分析卡住问题（NumPredict 256→2048、Research 场景 MaxOutputTokens=4096 + ResponseFormat=Json + 180s 超时保护）；修复前端轮询取消风暴；Research 实体 Unicode 支持 CJK；JSON 渲染容错
- **v0.3.1**：修复图表 hover tooltip 不显示的问题（适配 klinecharts v10 API），K 线蜡烛悬浮显示完整 OHLC + 涨跌幅 + 最高最低价；分时图悬浮显示价格 + 涨跌幅 + 量比
- **v0.3.2**：散户热度反向指标——基于东方财富/新浪/淘股吧三平台论坛帖量计算散户关注热度，K 线图子窗格展示热度曲线与信号标注；支持 60 个交易日历史回填与零填充；实时进度条显示回填状态
- **v0.3.3**：SocialSentimentMcp 增强——ForumPostCount/HeatRatio/HeatSignal/PlatformCount 四维特征输出至 LLM，交易提示模板添加散户情绪反向参考步骤
- **v0.3.4**：FinancialWorker 进程监控——主程序自动检测并管理 Worker 进程生命周期（心跳 10s、崩溃自动重启）；管理面板增加「工作者」标签，支持启动/停止/重启控制与运行时长显示；新增运行时日志控制台（内存环形缓冲区 + 增量轮询 + 级别筛选 + 自动滚动）

当前最新发布版本为 **v0.3.4**（2026-04-14），详见 [GitHub Releases](https://github.com/simplerjiang/StockCopilot/releases)。

## 安装

推荐直接从 GitHub Releases 下载当前安装包或便携包：

- `SimplerJiangAiAgent-Setup-*.exe`
- `SimplerJiangAiAgent-portable-*.zip`

发布页：<https://github.com/simplerjiang/StockCopilot/releases>

## 本地运行说明

先选定本轮是在做源码验证还是打包桌面验证，不要在同一轮验证里混用。

源码验证：直接启动当前源码的 backend-served app，并以源码启动日志里的实际端口为准访问 `http://localhost:<port>`；不要假定 `5119`；不要使用 `.\start-all.bat`。

如果你希望验证打包后的桌面程序，可以在仓库根目录运行：

```powershell
.\start-all.bat
```

这个脚本会停止当前仓库残留进程，重新打包并启动桌面版程序，用于验证最终交付形态，而不是仅验证浏览器开发页。

打包桌面验证固定检查 `http://localhost:5119/api/health`，不要改成 `/health`。

如果需要从源码验证切到打包桌面验证，或反过来切换，先停掉旧模式留下的 repo-owned 进程，再按新模式重新读取当前端口。

如果重新打包失败且提示 `artifacts\windows-package` 下文件被占用，先结束该目录下旧的桌面或后端进程，再重跑 `scripts\publish-windows-package.ps1`。

## 配置说明

项目不强绑定单一 LLM 提供方。首次启动后，请根据自己的环境配置接口地址、模型名称和 API Key。

如果你想使用本地模型，也可以在 `LLM 设置` 页里直接管理 Ollama：查看状态、启动/停止、拉取模型、开启 keepAlive，并保存请求级高级参数（如 `num_ctx`、`keep_alive`、`num_predict`、`temperature`、`top_k`、`top_p`、`min_p`、`stop`、`think`）。

当前仓库已为 Ollama 请求级参数提供显式默认值：`num_ctx=2048`、`keep_alive=-1`、`num_predict=2048`、`temperature=0.3`、`top_k=64`、`top_p=0.95`、`min_p=0.0`、`stop=[]`、`think=false`。研究分析（Research）场景自动覆盖为 `num_predict=4096` 以确保结构化输出完整。

基于这台 RTX 5060 8GB 机器的本地压测，推荐优先使用：`gemma4:e2b`（5.1B / `Q4_K_M`）配合 `num_ctx=2048` 作为质量和响应速度的平衡点；如果你更看重纯速度而能接受更小模型，可选 `llama3.2:3b`（`Q4_K_M`）配合 `num_ctx=2048`。`gemma4:latest`（8B / `Q4_K_M`）在这台机器上明显更吃显存，除非你明确接受更高延迟，否则不建议作为默认本地模型。

## 相关文档

- 内部实现与目标台账：`README.llm.md`
- 面向回归执行的中文手册：`README.UserAgentTest.md`

## 下一阶段：竞品对标功能（规划中）

参考国内同类开源项目竞品分析，规划以下四项功能，按优先级顺序交付：

### P1：AI 回测验证（多窗口）

量化 AI 建议的历史准确率。系统已存储所有 commander 预测结果，将与实际日 K 线走势比对，按 1日/3日/5日/10日 四个窗口计算命中率，并在 K 线图上标注正确/错误标记。

### P2：策略问股扩展

在现有 9 种策略（RSI/KDJ/MACD 等）基础上增加**缠论**（笔/段/中枢/背驰）和**波浪理论**（推动浪/调整浪/黄金比例目标），并支持多策略收敛评分和策略教学模式。

### P3：筹码分布分析

基于日K量价数据本地计算换手率衰减筹码分布，可视化展示获利盘/套牢盘比例和平均成本，与 K 线日期联动，并将筹码摘要注入 Commander 分析上下文。

### P4：邮件推送 + 定时自动分析

每个交易日收盘后（15:35）自动分析自选股并将报告发送到指定邮箱。SMTP 配置和定时开关集成到现有设置页（同步将"LLM 设置"页改名为"设置"）。

详细设计见 [`docs/GOAL-NEW-FEATURES-competitive-plan.md`](docs/GOAL-NEW-FEATURES-competitive-plan.md)。

## 补充说明

完整开发记录、自动化说明和内部任务拆解仍保留在 `README.llm.md` 中；本 README 更侧重对外展示项目定位、架构和实际能力。