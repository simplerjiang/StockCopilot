# StockCopilot

一个面向 **A 股研究场景** 的本地优先桌面助手（local-first trading research workstation）。

它把日常会反复切换的几类工作收进一个桌面应用里：**看盘、看市场、整理资讯、生成交易计划、沉淀本地数据**。项目当前重点不在“自动下单”，而在于把研究与决策前的准备流程做得更集中、更可控、更容易持续迭代。

## 项目定位

`StockCopilot` 是一个完整的多端协作工程，而不是单一脚本或 Demo：

- **Backend**：ASP.NET Core 8 Web API，负责数据同步、存储、配置与业务接口
- **Frontend**：Vue 3 + Vite 工作台界面，负责图表、资讯展示和交互体验
- **Desktop Shell**：.NET 8 WinForms + WebView2，将前后端封装为本地桌面应用
- **Local-first runtime**：支持本地 SQLite 运行，并保留切换数据库提供方的能力
- **Delivery pipeline**：支持 Windows 安装包、便携包、更新检测与发布链路

## 核心能力

- **股票终端**：支持分时、日 K、月 K、年 K、多种图表叠加与研究准备信息查看
- **市场总览**：通过情绪轮动页查看市场阶段、主线板块、比较窗口与实时总览
- **本地资讯库**：把个股、板块、市场多层级资讯沉淀到本地，便于检索与回看
- **交易计划**：支持草稿、总览、提醒与上下文联动，形成基础研究闭环
- **桌面交付**：安装后即可运行，不需要用户手动分别启动前后端与数据库

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

### 股票推荐 · LLM 助手

左侧展示推荐前的实时市场快照（指数、资金、板块排行），右侧为 LLM 对话助手，支持一键获取每日新闻、股票推荐和行情分析。

![股票推荐](docs/screenshots/08-stock-recommend-1920x1080.png)

## 当前状态

**v0.1.0** — 首个公开版本，已具备以下可交付能力：

- Windows 桌面程序可安装、可启动、可本地运行
- 发布版可从 GitHub Releases 分发
- 桌面版本已接入更新检测
- 本地数据默认保存在用户目录，便于升级时保留配置与使用痕迹
- 15+ 新闻数据源自动聚合（RSS、财联社电报、东方财富、新浪滚动等）
- 多源新闻多样性保障与 7 天自动清理
- LLM 驱动的市场分析与股票推荐助手

如果你想看的是一个**正在持续迭代的产品型工程**，这个仓库能反映我在以下方向上的实践：

- 本地优先应用设计
- 桌面端封装与交付
- 后端、前端、桌面端协同开发
- 将研究流程产品化、工作台化

## 安装

推荐直接从 GitHub Releases 下载当前版本的安装包或便携包：

- `SimplerJiangAiAgent-Setup-*.exe`
- `SimplerJiangAiAgent-portable-*.zip`

发布页：<https://github.com/simplerjiang/StockCopilot/releases>

## 本地运行说明

如果你希望直接验证打包后的桌面程序，可以在仓库根目录运行：

```powershell
.\start-all.bat
```

这个脚本会重新打包并启动桌面版程序，用于验证最终交付形态，而不是仅验证浏览器开发页。

## 配置说明

项目本身不强绑定某一个固定的 LLM 服务提供方。首次启动后，请根据自己的环境配置：

- 接口地址
- 模型名称
- API Key

建议在本地开发或个人使用时填写你自己的兼容 OpenAI 接口配置，不要依赖仓库历史文档中的示例值。

## 适合谁

- 想把行情、资讯、板块观察和计划整理收进一个本地工具的人
- 更偏好本地数据、本地配置、本地可控运行方式的人
- 想研究“桌面应用 + 本地后端 + 前端工作台”这类产品形态的人

## 下一阶段：UI 工作台重构（非 Agent）

为解决当前“界面不够美观、信息拥挤、操作路径偏长、布局可控性不足”的问题，项目已启动非 Agent 范围的 UI 重构规划。

本轮将重点改造：
- 顶层壳层与导航层级
- 股票信息页主工作区（允许调整显示位置、宽度、高度）
- 情绪轮动、全量资讯库、股票推荐与管理页的一致性体验
- 空态/错误态/加载态与组件视觉规范统一

说明：Agent 工作台及多 Agent 交互链路不在本次 UI 重构范围。

详细规划见 [`.automation/reports/GOAL-UI-NEW-001-PLAN-20260328.md`](.automation/reports/GOAL-UI-NEW-001-PLAN-20260328.md)。

## 下一阶段：量化引擎（规划中）

GOAL-AGENT-NEW-001 完成后，项目将优先接入量化引擎，通过 MCP 接口为 LLM Agent 提供可复现、可回测、可风控的多因子评分与信号能力。

核心思路：
- **量化引擎负责「算」**：因子计算、组合打分、风险闸门、回测仿真
- **LLM Agent 负责「说」**：解释结果、编排流程、生成可读报告
- **两者通过 MCP 协议解耦**：Agent 调用量化工具，但不直接生成交易分数

已规划 8 类因子组合包（趋势、动量、反转、波动率、资金流、事件情绪、行业轮动、防御型）和 12 个 Quant MCP 端点。

详细交付计划见 [`docs/quant-engine-mcp-delivery-plan.md`](docs/quant-engine-mcp-delivery-plan.md)，概念设计见 [`docs/quant-engine-mcp-design.md`](docs/quant-engine-mcp-design.md)。

## 补充说明

完整开发记录、自动化说明和内部任务拆解仍保留在 `README.llm.md` 中；本 README 更侧重对外展示项目定位、架构和实际能力。