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
- **股票推荐**：13 Agent 多角色辩论推荐系统，覆盖市场扫描、板块分析、选股、辩论、决策五大阶段，支持实时进度追踪和会话追问
- **交易计划**：支持草稿、总览、提醒与上下文联动，形成基础研究闭环
- **桌面交付**：安装后即可运行，不需要用户手动分别启动前后端与数据库
- **财报数据中心**：自动采集上市公司季度/年度财务数据（资产负债表、利润表、现金流量表、分红、融资融券），支持东方财富、同花顺、巨潮 PDF 三级数据源降级；通过三引擎 PDF 解析管线（PdfPig/Docnet/iText7）自动提取非结构化报表数据；MCP 工具直接注入 LLM 研报分析链路

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

## 当前状态

**v0.2.2** — 财报数据采集系统全 12 步完成（Worker + 三级数据源 + PDF 管线 + MCP + 前端报表 Tab + 管理面板），详见下方变更记录。

---

### v0.2.2 (2026-04-05)

财务数据采集系统上线，主要更新：

- **独立 Worker 进程**：`FinancialWorker` 运行在 5120 端口，定时调度财务数据采集
- **三级数据源降级**：东方财富 emweb（priority=3）→ 东方财富 datacenter（priority=2）→ 同花顺（priority=1），采集失败自动降级
- **PDF 解析管线**：巨潮公告 PDF 下载 + PdfPig/Docnet/iText7 三引擎投票提取 + 结构化表格解析（30+ 字段映射）
- **本地 LiteDB 存储**：财务报表、分红数据、融资融券数据独立存储在 `App_Data/financial-data.db`
- **MCP 工具注入**：`FinancialReportMcp` / `FinancialTrendMcp` 已接入 FundamentalsAnalyst、LeaderPicker、GrowthPicker 角色
- **前端财务报表 Tab**：侧边栏第 5 个标签页，仿理杏仁风格展示核心指标、趋势表、三张报表切换、分红信息
- **管理员测试面板**：支持 Worker 健康检查、配置管理、手动采集触发、日志查看
- **安全**：写端点全部受 AdminAuthFilter 保护
- 后端 455 测试 / 前端 180 测试全部通过

---

### v0.2.1 (2026-04-02)

#### 新功能
- **Antigravity LLM Provider** — 接入 Google Antigravity API，支持 Claude Opus/Sonnet、Gemini 3、GPT-OSS 等模型
  - OAuth 2.0 + PKCE 认证流程，本地回调 + token 自动刷新
  - 3 端点智能降级（daily sandbox → autopush sandbox → production）
  - SSE 流式输出支持
  - Google Search Grounding（联网搜索）— 仅 Gemini 模型
  - 自动模型映射：外部模型名自动匹配 Antigravity 可用模型
- **新闻 AI 清洗状态指示器** — 前端新增 🤖已清洗 / ⏳待清洗 badge
- **市场趋势数据增强** — 推荐系统现在使用 30 天板块轮动历史，而非仅当天快照

#### Bug 修复
- **B25**: Antigravity 单端点超时从 180s 降至 30s，最坏总超时从 540s 降至 90s
- **B26**: 推荐系统 provider 路由硬编码 `"default"` 改为 `"active"`，现在跟随用户设置
- **B21**: 股票分析 AI 结果使用 Markdown 渲染代替原始 JSON 展示
- **B24**: Antigravity 支持 Google Search Grounding 联网搜索
- **B27**: 板块轮动查询支持 30d/60d 窗口，推荐提示词区分实时信号（72h）和趋势数据（30天）

---

### v0.2.0

多 Agent 推荐系统上线，主要更新：

- **13-Agent 推荐系统**：覆盖市场扫描 → 板块分析 → 选股 → 辩论 → 决策五大阶段，每个阶段多角色并行分析
- **实时分析进度**：SSE 推送各角色执行状态，前端实时展示团队进度面板和辩论过程
- **推荐报告**：结构化报告卡片，包含入选板块、推荐个股、置信度评分、目标价、止损位和有效期
- **会话管理**：支持历史会话回看、追问功能和降级报告展示
- **看 K 线 / 深度分析**：推荐报告中个股卡片可一键跳转到股票终端查看 K 线图
- Windows 桌面程序可安装、可启动、可本地运行
- 发布版可从 GitHub Releases 分发
- 桌面版本已接入更新检测
- 本地数据默认保存在用户目录，便于升级时保留配置与使用痕迹
- 15+ 新闻数据源自动聚合（RSS、财联社电报、东方财富、新浪滚动等）
- 多源新闻多样性保障与 7 天自动清理

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

## 下一阶段：交易纪律闭环（首要目标）

当前系统在「看」的能力上已较完善（多 Agent 分析、K 线图表、情绪轮动、新闻事实库），但在「做」的纪律管理上几乎为零——没有交易执行记录、没有信号命中率回显、没有仓位总览、没有行为模式反馈。更关键的是，当前 LLM Agent 完全不知道用户的真实持仓情况，给出的建议脱离用户实际处境。

**核心判断：胜率 ≈ 70% 纪律 + 30% 分析准确度。** 当前系统只覆盖了 30% 的部分。

GOAL-018 将分 6 个切片交付：
- **R1** 交易执行记录、做T支持、持仓追踪与自动收益核算（P0）
- **R2** 信号胜率与实盘胜率双线（P0）
- **R3** LLM 交易教练——复盘与反思（P0）
- **R4** 持仓上下文注入 LLM Agent 与风险暴露管控（P0）
- **R5** 市场阶段→执行纪律联动（P1）
- **R6** 交易行为模式反馈 / 冷静仪表盘（P2）

详细设计见 [`docs/GOAL-018-trading-discipline-closed-loop.md`](docs/GOAL-018-trading-discipline-closed-loop.md)。

## 下一阶段：量化引擎（规划中）

GOAL-AGENT-NEW-001 完成后，项目将优先接入量化引擎，通过 MCP 接口为 LLM Agent 提供可复现、可回测、可风控的多因子评分与信号能力。

核心思路：
- **量化引擎负责「算」**：因子计算、组合打分、风险闸门、回测仿真
- **LLM Agent 负责「说」**：解释结果、编排流程、生成可读报告
- **两者通过 MCP 协议解耦**：Agent 调用量化工具，但不直接生成交易分数

已规划 8 类因子组合包（趋势、动量、反转、波动率、资金流、事件情绪、行业轮动、防御型）和 12 个 Quant MCP 端点。

详细交付计划见 [`docs/quant-engine-mcp-delivery-plan.md`](docs/quant-engine-mcp-delivery-plan.md)，概念设计见 [`docs/quant-engine-mcp-design.md`](docs/quant-engine-mcp-design.md)。

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