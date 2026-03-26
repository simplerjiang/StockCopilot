# SimplerJiangAiAgent

面向真实交易决策的智能化炒股助手。目标是把“行情 + 事件 + 指标 + 研报 + 资金面 + 你的交易习惯”整合到一个可追踪、可解释、可持续演进的系统里，输出更可靠的交易辅助判断。

## 项目架构
- 高内聚数据底层（Local-First）：系统级多渠道爬虫 + 本地数据库闭环，剥离LLM的直接联网依赖，保障事实绝对准确。
- 终端界面双轨制：专业看盘主终端（大图大字、极致响应） + AI协驾副屏（自然语言投研）。
- 后端：ASP.NET 8 Web API（模块化、可扩展）
- 前端：Vue 3 + Vite（网格化解耦，模块隔离）
- 桌面：WinForms (.NET 8) + WebView2（内嵌前端）

## 已实现功能
### 后端
- /api/stocks/market 大盘指数
- /api/stocks/market/cache 大盘指数（缓存）
- /api/stocks/quote 个股行情
- /api/stocks/kline 个股K线
- /api/stocks/minute 个股分时
- /api/stocks/chart 个股轻量图表（quote + K线 + 分时）
- /api/stocks/messages 盘中消息（占位）
- /api/stocks/detail 组合详情
- /api/stocks/detail/cache 组合详情缓存（默认只回放基础摘要；仅在显式 `includeLegacyCharts=true` 时才回放旧 K 线/分时表）
- /api/stocks/plans 交易计划查询/创建/更新/删除/取消/恢复观察，以及 /api/stocks/plans/draft 后端草稿生成；支持不传 `symbol` 直接获取最近交易计划总览
- `StockCompanyProfiles` 现支持持久化基本面快照事实 JSON 与刷新时间，`/api/stocks/detail/cache` 可直接回放数据库中的基本面事实，`/api/stocks/detail` 再做实时东财刷新并回写数据库；另已补充 `/api/stocks/fundamental-snapshot` 轻量接口，供前端独立展示东财基本面刷新进度
- 行情双源策略已正式收口到后端：默认 `分时 -> 东方财富优先 / 腾讯回退`，默认 `日K/周K/月K/年K -> 东方财富优先 / 腾讯回退`；当调用方显式传入 `source` 时仍按指定源执行
- /api/stocks/sync 手动触发同步
- /api/news 本地事实新闻查询（按 symbol + level=stock/sector/market 精准过滤，前端展示使用批量 AI 清洗后的翻译/情绪/标签）
- /api/stocks/news/impact 资讯影响评估（公告/研报/新闻分级、来源可信度、同主题合并去重）
- /api/stocks/signals 事件驱动信号（证据/反证、历史对齐）
- /api/stocks/position-guidance 个性化风险与仓位建议（现已叠加 GOAL-009 本地市场阶段 multiplier、主线对齐与执行节奏提示）
- /api/market/sentiment/latest、/api/market/sentiment/history、/api/market/sectors、/api/market/sectors/realtime、/api/market/sectors/{sectorCode}、/api/market/sectors/{sectorCode}/trend、/api/market/mainline 本地情绪周期、实时板块榜与板块轮动接口
- /api/stocks/quotes/batch 与 /api/market/realtime/overview 已新增东财实时批量行情、主力资金、北向资金和涨跌分布聚合，采用后端缓存、超时和降级策略统一对外暴露
- /api/stocks/agents 多Agent分析（默认模型已切到 `gemini-3.1-flash-lite-preview-thinking-high`，支持前端显式触发 `Pro 深度分析` 并路由到 `gemini-3.1-pro-preview-thinking-medium`，普通分析严格禁用 Pro）
- /api/admin/login 管理员登录
- /api/admin/llm/settings/{provider} LLM 配置读取/更新（需管理员 token）
- /api/admin/llm/test/{provider} LLM 调用测试（需管理员 token）
- 统一 AI 对话审计日志：所有 AI 请求/响应/错误统一记录到 `backend/SimplerJiangAiAgent.Api/App_Data/logs/llm-requests.txt`（含 traceId、耗时与截断保护）

### 前端
- Stock Tabs 基础框架
- LLM 设置页签（管理员配置）
- K线/分时图 AI 关键价位叠加（突破线/支撑线，来源于多Agent分析）
- 专业看盘终端已支持图表区 `全屏 / 退出全屏` 切换，放大后保留原有时间周期、策略按钮与浮动小标交互
- 股票信息页多Agent面板支持双档位触发：标准分析 / Pro 深度分析
- 股票终端切股加载优化：优先使用 `/api/stocks/detail/cache` 秒开缓存摘要（quote / messages / fundamental snapshot），后台再补最新图表，并阻断旧请求覆盖新标的；旧 K 线/分时缓存回放仅保留为显式兼容开关，不再作为默认详情链路
- 股票信息页图表刷新已拆为轻量 `/api/stocks/chart` 链路：首屏图表不再等待 `/api/stocks/detail` 聚合返回，`日K图 / 月K图 / 年K图` 切换只请求图表数据，不再重走消息、基本面和市场上下文加载链路
- 股票信息页“基本面快照”已支持展示东财公司概况/股东研究抽取出的富文本事实；首次打开先读数据库缓存，实时刷新完成后自动回写，下一次打开可直接秒开
- 股票信息卡片已新增真实分阶段加载进度：查询时会分别展示“缓存回显 / 腾讯行情 / 东方财富基本面”状态，能直观看到快数据与慢数据谁还在路上
- 股票信息页右侧现已新增“市场实时上下文”卡片：复用 `/api/market/realtime/overview` 展示当前标的、上证/深成/创业板对照、主力净流入、北向净流入与涨跌家数，并支持独立刷新与本地显隐开关
- 股票信息页交易计划总览现已新增“市场快链路”条带：把主力/北向/涨跌家数与三大指数快照压缩到总览顶部，便于在跨股票计划面板里先看环境再看计划
- 股票信息页已支持从 commander 历史分析一键起草交易计划：后端基于 `StockAgentAnalysisHistory` 生成草稿，确定性预填止损/止盈/目标价，用户在弹窗中确认/补录价格后可保存为 `Pending`；已保存计划支持继续编辑、硬删除，并会同时显示在“当前交易计划”和跨股票“交易计划总览”区块，同时自动加入 `ActiveWatchlist`
- 股票信息页交易计划现已支持 Step 4.4“突发新闻动态定性复核”：后端独立 worker 会对 `ActiveWatchlist` 内 `Pending` 计划结合本地个股快讯与 LLM 结构化复核结果输出 `ReviewRequired` / `NewsReviewed` 事件；前端在“交易计划总览”和“当前交易计划”中展示待复核状态、关联新闻与复核原因，并提供“恢复观察”手动确认入口
- 顶层已新增“情绪轮动”页签：支持市场阶段摘要、板块分页榜、5/10/20 日 compare window、主线 badge、趋势详情、广度拆解，以及 `主升 / 分歧 / 退潮 / 混沌` 的本地阶段识别
- “情绪轮动”页签现已叠加实时总览卡片，直接展示指数快照、主力/北向资金、涨跌分布桶，并支持前端本地开关与独立刷新，不影响原有板块轮动面板
- “情绪轮动”页签现已叠加东财实时板块榜：按涨幅或主力净流入重排现有榜单，优先透出实时强势概念/行业，同时保留原有本地轮动详情与失败隔离
- 股票推荐页现已新增“推荐前市场快照”：在触发推荐前先展示实时指数、主力/北向、涨跌家数与概念快榜；当实时板块榜为空时会明确降级提示，继续只使用指数与资金快照
- 交易计划流已接入 GOAL-009 市场上下文：草稿/编辑弹窗、当前计划卡、交易计划总览和仓位建议会展示阶段、置信度、主线对齐、建议仓位比例与执行节奏，但不会自动改写用户确认的计划价格
- 治理开发者模式：参数说明、治理链路 Trace 查询、以及按 traceId 聚合的 LLM 对话会话前端可视化（请求/返回/异常一一对应，支持 JSON 美化）
- 全量资讯库：支持按关键字、层级（大盘/板块/个股）和情绪筛选本地 AI 清洗资讯，并可直接跳转原文

### 桌面端
- WinForms 容器 + WebView2 载入前端

## 数据同步与配置
- 后台定时任务按 appsettings.json 的 StockSync 配置抓取并落库
- 行情接口默认走双源自动回退策略：分时优先东方财富以提升实时覆盖和字段完整度，K 线同样优先东方财富；任一优先源为空或异常时自动降级到腾讯等后备来源
- GOAL-013 已新增本地事实采集链路：东方财富公告/公司资料 + 新浪公司新闻会进入 `LocalStockNews`；板块资讯改为新浪财经搜索页 HTML 定向抓取；大盘环境已切换为新浪纯财经流 `pageid=153&lid=2509` + CNBC / Seeking Alpha / CoinTelegraph / TechCrunch / The Hill 五路 RSS 聚合写入 `LocalSectorReports`，并在采集阶段直接过滤 `自媒体` 等污染来源。Step 2.4 进一步在本地事实入库后增加 `gemini-2.5-flash-lite` 批量 AI 清洗层，补齐中文翻译、`AiSentiment`、`AiTarget`、`AiTags` 与 `IsAiProcessed` 增量重试机制；这些廉价 AI 标签仅用于 `/api/news` 与 `/api/news/archive` 展示，不直接投喂 Stock Agents，避免污染高阶分析上下文。Step 2.6 新增 `全量资讯库` 页签，对本地 AI 清洗资讯提供关键字 / 层级 / 情绪筛选、分页、译题优先展示与原文跳转
- 默认账号：admin / admin123（可在 backend/SimplerJiangAiAgent.Api/appsettings.json 的 Admin 段落中修改）

## 日志位置
- LLM 对话审计日志文件：`backend/SimplerJiangAiAgent.Api/App_Data/logs/llm-requests.txt`
- 前端“治理开发者模式”中的 LLM 对话记录，读取的也是这份后端日志文件（经 `/api/admin/source-governance/llm-logs` 聚合后返回）

## 测试
- 后端单元测试：dotnet test backend/SimplerJiangAiAgent.Api.Tests/SimplerJiangAiAgent.Api.Tests.csproj
- 前端单元测试：cd frontend && npm run test:unit

## OpenCode（VS Code + CLI）接入
本仓库已提供 OpenCode 项目级配置文件：`opencode.json` 与 `AGENTS.md`。

### 已配置内容
- 使用 OpenAI 兼容 provider 方式接入 GLM-5
- 项目指令文件：`.github/copilot-instructions.md` + `AGENTS.md`
- 默认模型：`zai/glm-5`

### 环境变量（必须）
请在终端设置你的 GLM-5 key（不要写入仓库）：

PowerShell:

```powershell
$env:GLM_API_KEY="你的GLM-5_API_KEY"
```

如果需要长期生效，请在你的用户环境变量里配置 `GLM_API_KEY`。

### 后端 LLM Key 本地存放
后端运行时的 LLM key 也不要写入仓库。当前仓库采用两层配置：
- 受控默认配置：`backend/SimplerJiangAiAgent.Api/App_Data/llm-settings.json`，只保存非敏感参数
- 本地忽略覆盖：`backend/SimplerJiangAiAgent.Api/App_Data/llm-settings.local.json`，只保存 `apiKey`

发布版（打包后的桌面程序）不再把这些文件写回安装目录，而是统一写到当前 Windows 用户本地目录：
- `%LOCALAPPDATA%\SimplerJiangAiAgent\App_Data\llm-settings.json`
- `%LOCALAPPDATA%\SimplerJiangAiAgent\App_Data\llm-settings.local.json`

其中：
- `llm-settings.json` 会由安装包内置的默认非敏感配置在首次启动时自动复制到本地目录
- `llm-settings.local.json` 不会随安装包分发，用户需要自己通过管理员页面保存 key，或手工写入本地文件

当前支持两个可切换通道：
- `default`：现有默认 OpenAI 兼容通道
- `gemini_official`：Google Gemini 官方 OpenAI 兼容通道

通过 `activeProviderKey` 控制当前激活通道；管理员页面也可直接切换，无需手工改文件。

也支持环境变量覆盖：

```powershell
$env:OPENAI_API_KEY="你的运行时_API_KEY"
```

或按 provider 名称使用更明确的变量：

```powershell
$env:LLM__DEFAULT__APIKEY="你的默认通道_API_KEY"
$env:LLM__GEMINI_OFFICIAL__APIKEY="你的 Gemini 官方_API_KEY"
```

本地忽略文件示例：

```json
{
	"activeProviderKey": "default",
	"providers": {
		"default": {
			"provider": "default",
			"providerType": "openai",
			"apiKey": "你的默认通道_API_KEY"
		},
		"gemini_official": {
			"provider": "gemini_official",
			"providerType": "openai",
			"apiKey": "你的 Gemini 官方_API_KEY"
		}
	}
}
```

### 启动方式
在项目根目录运行：

```powershell
opencode
```

如果需要本地快速打包最新代码并启动和 GitHub 发布物一致的桌面 EXE，直接运行：

```powershell
.\start-all.bat
```

说明：
- `start-all.bat` 会先停止当前仓库残留的桌面/后端实例，再执行 `scripts\publish-windows-package.ps1` 打包最新代码，然后直接启动 `artifacts\windows-package\SimplerJiangAiAgent.Desktop.exe`
- 这个入口现在验证的是“GitHub 用户下载后会运行的桌面 EXE”，不是浏览器联调页
- 脚本会等待打包版桌面拉起内置后端，并用 `http://localhost:5119/api/health` 确认 packaged runtime 已经就绪

首次建议执行：
- `/models` 确认已选中 `zai/glm-5`
- `/init` 让 OpenCode 读取仓库结构与规则

### Windows 打包（当前阶段）
当前仓库已提供一个基础打包脚本，用于生成“桌面 EXE + 同目录后端 + 前端静态资源”的可运行目录：

```powershell
.\scripts\publish-windows-package.ps1
```

脚本会执行：
- 前端 build
- 后端 publish
- 桌面程序 publish
- 将前端 `dist` 复制到后端发布目录
- 仅复制默认的 `llm-settings.json`，不会打包本地 `llm-settings.local.json`

当前阶段已实现的开箱即用能力：
- 用户机器不需要安装数据库
- 桌面程序会自动启动同目录后端
- 数据库、日志、LLM 本地配置统一落到 `%LOCALAPPDATA%\SimplerJiangAiAgent`
- 若首次启动尚未配置任何可用 LLM Key，桌面端会自动落到 `LLM 设置` 页签，并在首页顶部显示引导横幅

当前阶段已明确接受的宿主化边界：
- 长期目标不是强行追求“磁盘上绝对只有一个文件”，而是追求“一个主 EXE 统一控制启动与关闭 + 用户无需预装 SDK/.NET runtime + 应用可自带必要附属文件”
- 后续 GOAL-016-R6 会把当前“桌面 EXE 拉起独立 Backend 进程”的形态，收敛成“桌面宿主进程内直接托管 ASP.NET Core 后端”的单宿主、单进程架构
- 如果继续采用 WebView2，则需要在交付链路中明确 Fixed Version WebView2 Runtime 随包发布与升级策略，而不是继续依赖系统预装状态

当前仍需用户自己准备的内容：
- LLM Key，需要用户在首次使用时自行配置

### Windows 安装器（Setup.exe）
当前仓库已经补上 Inno Setup 安装器脚本与构建包装脚本：

```powershell
.\scripts\build-windows-installer.ps1
```

脚本会先生成 `artifacts\windows-package` 可运行目录，然后调用 Inno Setup 编译：
- 安装器脚本：`scripts\windows-installer.iss`
- 默认输出目录：`artifacts\installer`

前提：
- 本机需先安装 Inno Setup 6，并确保 `ISCC.exe` 在 PATH 中，或位于默认安装目录

安装器交付内容：
- `Setup.exe` 会把桌面端、后端和前端静态资源一起安装到目标目录
- 运行期数据库、日志、LLM 本地设置仍然写入 `%LOCALAPPDATA%\SimplerJiangAiAgent`
- 用户的 `llm-settings.local.json` 不会被打进安装包，也不会被升级覆盖

## 多 Agent 自动化开发与测试
入口与说明： [.automation/README.md](.automation/README.md)

核心目标：让自动化“长时间持续改进”更可靠、更可回滚、更可审计。设计原则参考长时间代理系统的一般实践：
- 任务切分清晰，避免超大变更
- 每次运行有清晰的计划、变更、测试和日志
- 可回滚的 git checkpoint，失败立即止损
- 强制执行测试顺序并记录结果

## 当前目标清单（与 .automation/tasks.json 同步）
- [x] GOAL-002 多源事件与权重评估（公告/研报/新闻分级、来源可信度、同主题去重）
- [x] GOAL-003 事件驱动信号与可解释输出（证据/反证、历史对齐）
- [x] GOAL-004 个性化风控与仓位建议
- [x] GOAL-005 专业行情图升级（K线+成交量副图、分时专业渲染、数据精确映射）
- [x] GOAL-006 图表增强（二期：分时成交量副图 + K线 MA5/MA10 叠加线）
- [x] GOAL-012 界面重构与“专业看盘/AI辅屏”解耦（股票信息页已拆为 TerminalView 主终端 + CopilotPanel 侧栏，并支持专注模式）
- [x] GOAL-012-R1 图表终端扩展性升级（Dev2 并行任务已完成：分时图已并入“分时图 / 日K图 / 月K图 / 年K图”统一 Tab 单主图终端，并新增 `frontend/src/modules/stocks/charting/**` 适配层作为未来神奇九转、KDJ 金叉等策略叠加扩展点；选型结论为保留 `lightweight-charts` 并以自建 adapter 获得 TradingView 风格可扩展性，且已通过前端单测、build 与 Browser MCP 交互验收，同时继续与 Dev1 的 Step 4.2 交易计划主线隔离推进）
- [x] GOAL-012-R2 `klinecharts` 受控替换试验（已在 `frontend/src/modules/stocks/charting/**` 内完成底层引擎从 `lightweight-charts` 到 `klinecharts` 的受控替换，保持 `minuteLines` / `kLines` / `interval` / `aiLevels` / `update:interval` 父层 contract 不变，并补齐 `klinechartsRegistry.js` 作为 MA/VOL/AI 价位线与后续策略标记层的 registry 入口；`frontend/package.json` / lockfile 已精确锁定 `10.0.0-beta1` 且移除旧 `lightweight-charts` 依赖。后续回归已补齐日K 时间戳毫秒化、月线/年线真实数据渲染、分时成交量按“手”显示，并将图表图例升级为可点击开关，可直接切换分时主线/量能/昨收基线/AI 价位与 K 线蜡烛/量能/MA5/MA10/AI 价位；前端单测、build 与后端托管页面的查股 + 图例点击 Browser MCP 验收已通过）
- [x] GOAL-012-R3 统一策略注册表与多信号叠加（Phase A/B/C 已完成并验收：`chartStrategyRegistry.js` / `chartPanes.js` 已将图表能力收敛为统一 strategy registry + grouped chips + render plan。当前已接入并验证策略：MA5/10/20/60、VWAP、BOLL、Donchian、MACD、RSI、ATR、KDJ、ORB，以及完整 Phase C 信号 `MA5/MA10 金叉/死叉`、`TD九转`、`MACD金叉/死叉`、`KDJ金叉/死叉`、`放量突破/假突破`、`缺口`、`量价背离`、`VWAP强弱`；其中 `TD九转` 现已收敛为只显示 `6/7/8/9`，并按 `6-7` 弱提示、`8-9` 强提示做视觉分层；其余日K专属信号只在 `日K图` 展示，分时专属信号只在 `分时图` 展示。可读性层也已收口：图表头部浮动小标按颜色区分当前激活策略，鼠标悬浮可查看“介绍 / 解释 / 用法”，并支持 `隐藏小标 / 显示小标` 总开关，以及 `全屏 / 退出全屏` 放大控制；K 线鼠标悬浮时也会直接显示开高低收、成交量、MA5/MA10、涨跌额与涨跌幅。对于 RSI/KDJ/BOLL/Donchian/MACD 这类多线指标，tooltip 已补齐“颜色对照”；KDJ 也已完成真实图面修复，不再让 render-plan 聚合器错误地去重/排序 `[9,3,3]` 参数，而是在同一 KDJ 副图挂载 `K/D/J` 三条受控单线。整套 R3 已通过前端定向单测、build 与后端托管页面 Browser MCP 点击验收。）
- [x] GOAL-013 双轨数据中枢（Local+Global Dual-Track）与 LLM 职能调度中心（已完成 Step 2：本地事实库、受控外网路由、新闻精准过滤、Step 2.2 Task 4 的标准/Pro 模型分流、Step 2.3 的新浪板块资讯抓取/大盘多源聚合/无选股即可查看的大盘资讯与完整查询历史展示、Step 2.4 的本地事实批量 AI 清洗/翻译/标签隔离投喂、Step 2.5 的大盘资讯内嵌交互/外媒 RSS 时效清洗/本地事实 AI 重试补漏，以及 Step 2.6 的纯财经大盘源切换、活跃 RSS 替换与 `全量资讯库` 归档工作台）
- [x] GOAL-AGENT-001 多 Agent 分析链路重构规划（已于 2026-03-22 收口全部执行切片：R1 evidence object 与正文链路、R2 子 Agent 职责收窄与 commander hardening、R3 replay 校准基线、R4 Copilot 风格 MCP 工具运行时。系统已具备 evidence traceability、A 股上下文抗污染、replay baseline 与 `/api/stocks/mcp/*` 域内工具层。）
- [x] GOAL-AGENT-001-R1 证据可追溯底座（URL-first evidence object、正文抓取/摘要/readMode/readStatus、evidence 归一化与 commander 采信闸门）
- [x] GOAL-AGENT-001-R2 Agent 职责重切与推理收口（stock/sector/financial/trend 边界重划、marketReports 抗污染、代码先算特征、commander 覆盖率/冲突/降级惩罚）
- [x] GOAL-AGENT-001-R3 回放校准闭环与验收基线（历史回放样本、1/3/5/10 日收益对齐、命中率/Brier score/分组胜率、开发者可观测验收指标）
- [x] GOAL-AGENT-001-R4 Copilot 风格 MCP 工具运行时基础层（股票 K 线 MCP、分时图 MCP、策略 MCP、新闻 MCP、搜索 MCP/Tavily 受控兜底；统一 tool envelope、governor policy class、trace/cache/degradedFlags/evidence/features 输出，作为后续把多 Agent 改造成类似 Copilot 的直接能力层）
- [x] GOAL-AGENT-002 股票 Copilot 会话化编排与产品层（历史实现已于 2026-03-25 被用户终止，并已被手动删除默认入口与产品方向。后续文档与开发不得再把 GOAL-AGENT-002 视为当前主线、可复用 UI 模型或必须保留的产品层；相关报告仅保留为历史归档。）
- [ ] GOAL-015 深度盘面属性扩充与 Agent 指挥体系重构（Step 3 已继续完成“基本面快照富事实 + 数据库缓存优先刷新”增强：`StockCompanyProfiles` 新增 `FundamentalFactsJson/FundamentalUpdatedAt`，详情页先读 `/api/stocks/detail/cache` 的数据库快照，再由 `/api/stocks/detail` 实时抓东财公司概况/股东研究并回写；本轮进一步补上股票信息卡片真实加载进度，将“缓存回显 / 腾讯行情 / 东方财富基本面”拆成可视化阶段，并新增 `/api/stocks/fundamental-snapshot` 轻量接口配合前端独立显示东财刷新状态。剩余主要是 Edge/UI 验收与更大范围联调。）
- [ ] GOAL-016 单机可安装版与本地数据底座重构规划（本轮先完成规划，不急于编码；目标是把当前“桌面壳 + 本地后端 + 外部 SQL Server”的开发形态，收敛为可以发给不同 Windows 用户安装使用的单机应用。总体路线采用“桌面宿主 EXE + 后端内嵌启动 + 前端静态资源随包发布 + 主事务库 SQLite + 冷数据 Parquet + 本地分析 DuckDB”的分层架构，兼顾免安装数据库、长期大数据量增长和后续回测/统计能力。规划分 4 个切片：R1 数据库提供者抽象与 SQLite 落地；R2 高频行情/历史事实冷热分层与归档；R3 WinForms/WebView2 桌面宿主化与本地自启动；R4 安装器、升级、数据目录与发布流程收口。）
- [ ] GOAL-016-R6 单宿主单进程 packaged runtime 收口（2026-03-22 已补充详细设计：接受“一个主 EXE + 应用自带附属文件”的交付形态，不再把“绝对单文件”作为硬目标；真正的硬目标改为“单 EXE 统一控制启动与关闭、用户无需预装 SDK/.NET runtime、后端不再作为独立后台进程存在”。实施路线为：把 ASP.NET Core 从独立 `Backend/` 进程改成由 WinForms 宿主进程内直接启动和停止；保留 localhost + WebView2 的现有前端访问契约；重做 `publish-windows-package` 与安装器链路，使桌面宿主成为唯一主入口，并为 WebView2 Fixed Version Runtime 制定随包发布与升级策略。）
- [ ] GOAL-017 量化双引擎与 Agent/图表协同规划（本轮先完成规划，不急于编码；目标是在现有分时图、K线图、交易计划与多 Agent 分析之间补上一层统一的量化特征与策略能力。总体路线采用 `Skender primary + Lean shadow`：用轻量 .NET 指标库承担在线主引擎，用 Lean 承担 shadow/replay/calibration；图表、Agent、交易计划默认只消费 primary 结果，shadow 结果主要用于开发者模式、回放、校准与研究。规划分 4 个切片：R1 统一 normalized market-data 输入层与 feature/signal/comparison contract；R2 Skender 主运行时整合；R3 Lean shadow replay/calibration 整合；R4 图表、MCP、Agent、交易计划产品层整合。）
- [ ] GOAL-017-R1 归一化行情输入层与量化 Contract 设计（先锁定双引擎共享底座，不急于真正接入 Skender 或 Lean。范围包括：统一 `NormalizedBar/NormalizedBarSeries`、定义 `QuantFeatureSnapshotDto/QuantStrategySignalDto/QuantEngineComparisonDto/AgentQuantContextDto`，补齐 `warmupState/degradedFlags/engine role/execution mode` 语义。由于 `Stock Copilot / GOAL-AGENT-002` 已被手动删除，后续不再把 `StockCopilot*Dto` 当成必须兼容的产品层前提；如仓库仍残留同名类型，只按待清理遗留处理。）
- [x] MANUAL-20260319-EXTENSION-INTERFACE `stock-and-fund-chrome-master` 接口吸收规划（已完成 R1-R5 全链路收口：后端新增 `/api/stocks/quotes/batch`、`/api/market/realtime/overview`、`/api/market/sectors/realtime`，接入东财批量行情、主力资金、北向资金、涨跌分布与实时板块榜；默认分时来源已切到东方财富优先；前端已同步落地到 `情绪轮动`、`股票推荐`、`股票信息` 与交易计划总览等高频决策入口，并通过定向单测与浏览器验收。整体策略仍是不做整包替换，而是只吸收扩展里仍有价值的公开端点；作者自建 `110.40.187.161` 云服务继续排除在正式依赖之外。）
- [x] MANUAL-20260319-EXTENSION-INTERFACE-R1 实时行情后端切片（新增 Eastmoney realtime adapter 与聚合服务，提供批量行情和市场总览 API；验证覆盖批量行情、主力资金、北向资金、涨跌分布解析，以及本地运行时 smoke test。）
- [x] MANUAL-20260319-CHART-PERF 股票图表刷新性能收口（已定位慢点不在第三方行情源本身，而在前端把图表刷新绑定到 `/api/stocks/detail` 重聚合链路；现已新增 `/api/stocks/chart` 轻量接口，并把 `StockInfoTab` 首屏图表和 `日K/月K/年K` 切换改为只请求图表数据。定向单测 43/43 通过，Browser MCP 已确认切换 `月K图/年K图` 时只出现 `/api/stocks/chart?...interval=month|year`，不再触发 `/api/stocks/detail/cache`、`/api/stocks/messages` 与 `/api/stocks/fundamental-snapshot`。）
- [x] ISSUE-20260310 提示词增强（新闻抗污染策略 + 新闻库定时采集约束 + 白盒 MCP/Skill 任务执行规范）
- [x] ISSUE-20260310-P0 动态来源治理基座（LLM每日候选源发现 + 自动新增爬取地址/流程 + 爬虫失效自动修复发布 + 程序化验证与自动隔离）
- [x] ISSUE-20260310-P0-R1 P0剩余计划：开发者模式可视化收口（治理仪表盘 + 最小查询接口 + 过滤/详情展开/trace跳转 + 可观测审计）
- [ ] ISSUE-20260310-P1 建立事实vs情绪资讯矩阵（来源分层、小作文高波动标识、FactChecker假消息防伪与降级）
- [ ] ISSUE-20260310-P2 交易作息驱动(盘前/盘中/盘后)的知识库与题材生命周期（酝酿/发酵/退潮）推演引擎
- [ ] ISSUE-20260310-P3 白盒能力与交易员化自动SOP（授权AI根据盘中异动自动触发研报溯源/资金复盘的动态任务）

## 下一阶段候选目标（逐项讨论、逐项设计）

- [x] GOAL-007 LLM 联网个股研判与交易目标建议（核心差异化，阶段一已完成）
	- 输出结构统一：结论、证据来源、置信度、触发条件、失效条件、风险上限、目标动作。
	- 证据链可追溯：新闻/公告/财报/资金/技术面必须带来源与时间戳。
	- 决策动作标准化：观察/试仓/加仓/减仓/清仓 + 目标价/止盈/止损/仓位建议。
	- 低置信度自动降级：不满足阈值时仅给“观察”，避免过度交易。
	- 图表联动增强：K线与分时图叠加 AI 突破线/支撑线（优先 commander 目标/止损，缺失时回退 trend 预测区间）。
	- 阶段验收标准：多Agent结果在后端统一补齐结构字段；前端可展示操作计划/证据/触发失效/风险上限；自动化测试需验证点击交互、响应等待与日志无异常。
- [x] GOAL-AGENT-001 多 Agent 分析链路重构规划
	- 总体评价：当前分析内容与最终返回内容“合理，但偏松”。它已经适合做信息整合、结构化展示和 UI 呈现，但还不足以直接充当高置信交易判断。当前最大的问题不是不会说，而是说得太像、太满、太统一。
	- 核心问题 1，证据绑定太弱：虽然提示词要求 `source` 和 `publishedAt`，但没有强制模型只能引用上下文里真实存在的事实项，也没有要求返回可回溯证据对象。真实日志里频繁出现“华尔街日报”“路透社”“北向资金连续净流入”“社交媒体综合统计”这类像研报的话术，说明内容容易“像真的”，但不够可验证。
	- 核心问题 2，子 Agent 分工不够专：个股资讯、板块资讯、基本面、走势 4 个 Agent 目前都在输出 `signals / triggers / invalidations / riskLimits`，导致大家都在做半个 commander，信息增量不高，反而容易制造“伪共识”。
	- 核心问题 3，上下文本身有污染：`stock_news` 与 `sector_news` 提示词都允许直接吃 `localFacts.marketReports`，但当前大盘环境里混入了不少 Seeking Alpha、CoinTelegraph、加密和海外个股内容，会把模型注意力从 A 股单票拉向泛宏观杂讯。
	- 核心问题 4，概率与置信度不够校准：真实日志里多次出现机械化 `0.88` 置信度和模板化上涨/下跌/震荡概率表达，更像模型习惯值，不像经过历史命中率回归后的校准结果。
	- 核心问题 5，异常场景还不够保守：子 Agent 全部失败、超时、HTML 返回页、非 JSON 思维泄漏等 degraded path 已经在日志中出现，但系统仍可能产出“格式完整但过度自信”的最终结论。
	- Step 1 证据 schema 重构：这里不再坚持“把 evidenceId 暴露给用户并做成硬约束”，而是改成“把可回溯证据对象做成硬约束”。外部展示以 `url` 为主，不再把内部 evidenceId 当成人类可读核心；同时引入 evidence object，至少包含 `source`、`publishedAt`、`url`、`title`、`excerpt`、`readMode`、`readStatus`、`ingestedAt`，内部如需关联数据库可保留 `localFactId`，但不要求直接暴露给用户。
	- Step 2 正文获取链路：优先由后端抓取原文正文、清洗正文、抽取摘要或关键段落，再把“已读内容”传给 LLM；禁止把“让 LLM 自己去网上随意读链接”作为主链。若正文抓取失败，必须明确标记 `readStatus=metadata_only` 或 `fetch_failed`，并自动降低证据权重。
	- Step 2.1 全文阅读触发条件：允许“要求阅读全文”，但只对公告、财报、监管文件、重大合同、业绩预告、直接影响交易计划失效条件的新闻触发；普通快讯、二手转载、板块情绪消息不默认全文抓取，避免延迟、成本和噪声失控。
	- Step 3 Agent 职责重切：`stock_news` 只负责个股事件事实、时间与直接影响；`sector_news` 只负责板块/大盘 regime 与外部共振；`financial_analysis` 只负责慢变量基本面、估值锚与财报质量；`trend_analysis` 只负责多周期价格结构、量价信号与关键位；`commander` 只能综合已有证据，不允许再凭空新增未经上游引用的新新闻结论。
	- Step 4 上下文净化：继续坚持 README 中的 Local-First 原则，把大盘/板块/个股事实池明确隔离；对 A 股场景中无关的海外宏观、加密货币、泛美股 RSS 噪音建立隔离或降权策略，避免污染个股分析上下文。
	- Step 5 代码先算特征：把可确定的特征先在代码里算好，再让 LLM 做解释与综合，例如新闻新鲜度、事件覆盖率、正反证数量、历史结论漂移、趋势状态、波动风险、估值偏离、量价异动。LLM 不负责“凭感觉编数据”。
	- Step 6 置信度重构：把 `confidence_score` 从自由发挥改为半规则化产物，至少受到四类因素约束：证据覆盖度、证据冲突度、证据新鲜度、链路降级状态。出现 JSON 修复、正文缺失、证据不可追溯、观点冲突大时，必须自动降级到 `观望/中性/低置信度`。
	- Step 7 输出 contract 重构：最终 commander 输出应围绕“观点而非命令”，统一包含 `analysis_opinion`、多时间尺度方向判断、`bull/base/bear` 或等价概率分布、核心驱动、反证、触发条件、失效条件、风险上限、关键价位与可点击证据 URL；避免只给单句结论或机械化 `0.88` 置信度。
	- Step 8 回放与验收基线：基于现有 `llm-requests.txt`、历史 commander 结果和本地事实库，构建一批可回放样本，统计证据可追溯率、解析修复率、低质量来源混入率、置信度分布、改判解释完整率，并进一步把过去 1/3/5/10 日实际收益与当时的方向、概率、置信度对齐，计算命中率、Brier score 和分组胜率，形成真实校准回路。
	- 与现有目标关系：本目标是 GOAL-015 的上游规划约束，也是 ISSUE-20260310-P1/P2 在 Agent 决策层的具体落地拆分。先把这个规划写清楚，再逐步拆成可开发的后续 R1/R2/R3 子任务。
	- R1 证据可追溯底座：先落 evidence object、URL-first 展示字段、正文抓取与本地摘要、`readMode/readStatus`、evidence 归一化与 commander 证据采信闸门。没有可回溯证据对象的判断，默认不能进入高置信结论。
	- R2 Agent 职责重切与推理收口：再收口 4 个子 Agent 的职责边界，减少重复结论输出；对 `marketReports` 做 A 股场景净化；先由后端计算 freshness/coverage/conflict/trend/valuation 等确定性特征，再让 LLM 解释；同时把 commander 的覆盖率惩罚、冲突惩罚、degraded path 降级做成系统逻辑。
	- R3 回放校准闭环与验收基线：最后建立历史 replay、收益对齐、命中率/Brier score/分组胜率指标与可观测验收面板，把“格式化观点”推进成“可被持续校准的分析系统”。
	- R4 Copilot 风格 MCP 工具层：把股票 Copilot 后续最常用的图表与证据能力单独收口成领域 MCP。首批范围包括 `StockKlineMcp`、`StockMinuteMcp`、`StockStrategyMcp`、`StockNewsMcp`、`StockSearchMcp`（Tavily 兜底），并统一 `traceId/taskId/toolName/cache/degradedFlags/evidence/features` 输出，让未来 planner/governor/commander 能像 Copilot 一样按需调用工具，而不是继续依赖大 prompt 填充所有上下文。
- [x] GOAL-AGENT-002 股票 Copilot 会话化编排与产品层（已归档并按用户决定手动删除；当前不再作为候选目标，后续只允许作为历史报告背景，不允许恢复为活跃需求。）
- [ ] GOAL-008 交易计划引擎（盘前计划、盘中触发、失效条件）
	- Step 4.0 已完成：股票切换与加载性能深度优化，后端 `/api/stocks/detail` 并发化，前端先读 `/api/stocks/detail/cache` 做秒开渲染，并加入快速切股的旧响应抑制。
	- Step 4.1 已完成：新增 `ActiveWatchlist` 高频白名单与 `HighFrequencyQuoteService`，仅在 A 股交易时段轮询白名单股票并持续回写 quote/minute/messages 到本地缓存表，为后续交易计划触发与纪律执行提供稳定底座；已通过后端全量单测、EF migration 应用与 SQLCMD 表/索引校验。
	- Step 4.2 已完成，并在同日补齐 R1 可用性增强：新增 `TradingPlan` 实体、`/api/stocks/plans*` 接口、后端基于 commander 历史结果的交易计划草稿生成、前端“基于此分析起草交易计划”按钮与可编辑弹窗、当前计划列表，以及保存后自动 upsert `ActiveWatchlist`；R1 进一步补齐已保存计划编辑/删除、跨股票“交易计划总览”，并将止损/止盈/目标价按 commander 图表字段 -> financial `institutionTargetPrice` -> trend forecast 极值的优先级做确定性默认填充。同时补齐对本地旧版 `TradingPlans` 表的兼容补列、索引和 `PlanKey`/`Title` 默认约束，已通过后端定向单测、前端定向单测、前端 build、SQLCMD 结构校验与后端托管页面 Browser MCP 实测保存。
	- Step 4.3 已于 2026-03-14 完成返工并通过复测：`TradingPlanTriggerService` 现已以 `ActiveWatchlist` 作为执行边界，量价背离 warning 改为按持续条件时间窗去重，前端短轮询同时覆盖“当前交易计划”和“交易计划总览”。本轮已重新通过后端定向单测、前端定向单测、frontend build、`TradingPlanEvents` 的 SQLCMD 字段/索引校验，以及后端托管页面的刷新链路验证；本步仍不引入 SignalR 或 LLM 语义复核。
	- Step 4.4 已于 2026-03-15 完成并通过复测验收：新增独立 `TradingPlanReviewWorker` / `TradingPlanReviewService`，只对白名单内 `Pending` 计划做突发新闻语义复核，不进入 Step 4.3 的高频确定性主链；当本地个股快讯与计划失效条件形成高置信度冲突时，计划状态改为 `ReviewRequired` 并写入结构化 `TradingPlanEvents.MetadataJson`（新闻标题、reason、confidence 等），前端清晰展示“待复核”与复核结论，并支持人工点击“恢复观察”回到 `Pending`。同时已补齐对本地旧版 `TradingPlanEvents` 表 `VersionId/Strategy/Reason/CreatedAt` 历史非空列的兼容映射与落库填充，避免真实数据库在新增事件时 500；reviewer follow-up 还补齐了 `confidence` 小数/字符串解析与“每计划最新一条告警摘要”聚合，避免有效复核被静默跳过或在总览中被高频事件挤掉。
	- Dev2 并行前端支线：GOAL-012-R2 已完成 `klinecharts` 受控替换；GOAL-012-R3 现已进入 Phase C 首批信号层落地，在既有 `charting/**` 适配层上实现统一 strategy registry、grouped chips、render plan 与 marker overlay 出口，当前已支持 MA/VWAP/BOLL/Donchian、MACD/KDJ/RSI/ATR、ORB、MA5/MA10 金叉死叉及既有 AI/昨收/量能图层，并新增图表头部浮动小标、颜色识别、hover 说明与总开关。后续仍将继续补齐神奇九转、突破/假突破、缺口、量价背离与 VWAP 强弱，并保持按钮化切换与父层 contract 稳定。
	- 盘前自动生成候选池：主线板块、关键价位、预期催化、风险提示。
	- 盘中只执行“已定义触发”：避免临时主观冲动单。
	- 计划失效自动提示：跌破条件、量价背离、消息反转时触发撤销或降级。
	- 支持计划复用与版本记录：便于复盘同类行情策略表现。

- [x] GOAL-009 情绪周期 + 板块轮动面板（主升/分歧/退潮识别）
	- 已于 2026-03-15 完成 GOAL-009-R1/R2/R3 全量交付。当前系统已落地本地市场情绪快照、分页板块榜、龙头梯队、`/api/market/*` API、后台 worker 和顶层“情绪轮动”页签，并补齐 5/10/20 日持续性、扩散度、排名变化、主线分数、`StageLabelV2/StageConfidence`、`/api/market/sectors/{sectorCode}/trend` 与 `/api/market/mainline`。
	- 交易计划流现已接入这些本地阶段标签：`/api/stocks/plans/draft`、`/api/stocks/position-guidance`、当前计划卡、计划总览和编辑弹窗都会展示阶段、主线对齐、建议仓位比例与执行节奏，但不会自动改写用户计划价格。
	- GOAL-009 全程保持了 R1 的稳定性底线：非交易日自动回退最近交易日抓取涨跌停池、成交集中度按全市场成交额口径计算、东财单点上游失败时按数据源粒度降级，避免空表首次同步直接 500。若上游源短时失败，页面会稳定显示空态或零值快照，而不是崩溃。
	- 情绪温度指标：连板高度、涨停/跌停家数、炸板率、上涨/下跌家数、成交额集中度。
	- 板块轮动强度：主线持续性、扩散度、资金净流入与领涨梯队稳定性。
	- 阶段标签：主升、分歧、退潮、混沌，直接映射仓位建议与交易频率。
	- 支持对比近5/10/20交易日，识别“短热度”与“中期趋势”差异，并在独立“情绪轮动”页签中提供分页与详情钻取。
- [ ] GOAL-010 执行风控闸门（单票/单日/连续亏损约束）
	- 单票风险：最大亏损阈值、最大回撤、止损触发后禁加仓。
	- 账户日内风险：单日最大回撤触发后自动进入防守模式。
	- 连续亏损保护：N连亏后强制降仓、降低开仓频率。
	- 时段约束：尾盘禁追高、临停风险提示、异常波动熔断提示。
- [ ] GOAL-011 复盘闭环（自动归因、周月改进清单）
- [ ] GOAL-014 跨网网络穿透与前端分离部署（基于 Cloudflare Tunnels 实现无公网IP家庭后端+公司办公前端映射方案）
	- 每笔交易自动归因：买点类型、卖点类型、是否遵守规则、盈亏来源。
	- 违规行为识别：追涨杀跌、逆势加仓、无计划交易等行为统计。
	- 周/月策略报告：胜率、盈亏比、回撤、执行一致性、最需优化项。
	- 输出“下一周期可执行改进清单”，形成持续迭代闭环。
- [ ] GOAL-016 单机可安装版与本地数据底座重构
	- 目标：把当前开发态系统重构为“安装一次即可运行”的 Windows 单机应用，不再要求用户自行安装 SQL Server，也不要求手工分别启动前后端。
	- 数据底座：主业务库改为 SQLite，承载配置、交易计划、热数据缓存、最近窗口查询与应用事务；海量历史分时/K线/事实归档改为 Parquet 分区文件；需要本地大范围扫描、回测、聚合时由 DuckDB 直接读取 Parquet。这样既满足免安装，也避免将所有长期历史堆进一个持续膨胀的事务库。
	- 架构形态：WinForms/WebView2 桌面程序升级为真正的宿主进程，由桌面 EXE 内嵌启动 ASP.NET Core 后端；前端产物随安装包发布；数据库与归档文件统一放入用户本地数据目录，而不是依赖开发机脚本或外部数据库实例。
	- 交付边界：这里的“单 EXE”指“一个主 EXE 作为唯一用户入口并统一控制整个应用生命周期”，不再把“磁盘上绝对只有一个文件”作为硬约束；允许应用携带自身管理的附属文件、前端静态资源、原生依赖和 Fixed Version WebView2 Runtime。
	- 运行时边界：对用户和测试者的硬要求是“不需要额外安装 SDK 或 .NET runtime，也不需要手工管理后台进程”；如果继续采用 WebView2，则必须把 WebView2 runtime 策略纳入安装与升级链路，而不是继续依赖系统预装状态。
	- R1 数据库提供者抽象与 SQLite 落地：移除启动期对 `OBJECT_ID/COL_LENGTH/sys.indexes` 等 SQL Server 方言的硬依赖，统一为 provider-aware schema/migration 机制；先让现有表结构完整跑通 SQLite，并补齐数据目录、连接串与迁移策略。
	- R2 冷热分层与归档：重新定义 `MinuteLinePoints`、`KLinePoints`、`StockQuoteSnapshots`、`LocalStockNews`、`StockAgentAnalysisHistories` 的保留策略，热路径只保最近窗口，冷路径按 `symbol/date` 分区落 Parquet；同时补充归档、回放、清理和索引元数据。
	- R3 桌面宿主化：让 `SimplerJiangAiAgent.Desktop` 在应用内启动本地后端与静态前端，替代当前依赖 `start-all.bat` 和 `http://localhost:5119` 外部开发流程的方式；同时补齐端口管理、健康检查、首次初始化和异常日志。
	- R4 安装与发布：建立 `dotnet publish`、前端 build、资源复制、数据库初始化、WebView2 runtime 策略和安装器产物；定义升级时的数据迁移、备份与回滚规则，最终输出可发给不同用户安装的 `Setup.exe + 主程序` 交付链路。
	- R6 单宿主单进程 packaged runtime：把当前桌面 EXE 通过 `Process.Start` 拉起独立 `Backend/` 进程的形态，重构为桌面宿主进程内直接托管 ASP.NET Core Host。优先保留现有 localhost + WebView2 契约，避免大规模重写前端；桌面关闭时通过同进程 host lifecycle 统一停止所有后台服务。该切片的重点是宿主化、生命周期、打包结构与 WebView2 runtime 策略，不是强行做绝对单文件产物。
	- 容量前提：按未来大数据量设计，假设监控股票数和保留年限持续增长，不再以当前本地几万行数据做选型；事务数据与海量历史必须物理分层，否则单库体积、备份、恢复和统计扫描都会成为长期瓶颈。

- [ ] GOAL-017 量化双引擎与 Agent/图表协同规划
	- 目标：在现有分时、日K、月K、年K、图表策略注册表、多 Agent 分析和交易计划流之间补上一层统一的量化特征与策略能力，让图表、Agent、计划三者共享同一套可追溯 signal contract。
	- 引擎路线：采用 `Skender primary + Lean shadow`。前者负责在线主运行时的指标计算、盘中特征提取、策略信号生成；后者负责 shadow run、replay、回测、参数实验和 calibration，不默认直接参与线上主决策。
	- R1 当前已补成详细设计：先锁定双引擎共享底座，不急于接包或写复杂逻辑；核心是 normalized input、统一 feature/signal/comparison/agent context DTO、以及和现有 `StockCopilot*Dto` 的兼容映射。
	- 单一主口径：前端图表、Agent、交易计划默认只消费 primary 结果；shadow 结果仅进入开发者模式、差异分析、回放报告与校准基线，避免线上同时出现两套决策口径。
	- R1 归一化输入层与 contract：统一 minute/day/month/year 行情 bar 模型，定义 `FeatureSnapshot`、`StrategySignal`、`EngineComparison`、`AgentQuantContext` 等 DTO，并显式保留 `engine/computedAt/warmupState/degradedFlags`。
	- R2 Skender 主运行时整合：在后端新增 quant feature/signal service，先覆盖 MA/EMA/MACD/RSI/KDJ/ATR/BOLL/Donchian/VWAP 等指标，再扩展 A 股盘中特征如开盘区间、午后漂移、量价背离、假突破和缩量横盘。
	- R3 Lean 影子 replay/calibration：使用统一 normalized market data adapter 把同一份输入喂给 Lean，首批只做 shadow 对比与 replay/backtest/calibration，用于 signal parity、命中率、收益分布和 Brier score 等历史校验。
	- R4 产品整合：通过后续 `StockKlineMcp` / `StockMinuteMcp` / `StockStrategyMcp` 把量化上下文喂给 Agent；前端图表继续复用现有 strategy registry 渲染主引擎结果，并在开发者模式中暴露主/影子差异；交易计划则接入统一 signal contract 做草稿、触发、失效与复核。

## 核心差异化：LLM 联网投研决策中枢（GOAL-007）
你提出的方向将作为下一阶段最优先事项：尽可能与 LLM 大模型结合，由模型联网获取信息后输出个股优劣判断与目标操作建议。

为保证“理性、可控、可解释”，设计上采用以下原则：
- 规则先行：先由系统规则约束（风险预算、仓位上限、止损纪律、黑名单条件），再允许模型在边界内决策。
- 联网证据化：模型输出必须引用结构化证据来源（公告/财报/新闻/资金面/技术面），并记录抓取时间。
- 评分标准化：统一输出多维评分（基本面、事件面、资金面、技术面、风险面），避免只给结论不讲依据。
- 操作目标化：输出明确动作模板（观察/试仓/加仓/减仓/清仓）、触发价位区间、失效条件与时效窗口。
- 风险对冲：每次建议必须含反证与不确定性说明，低置信度建议默认降级为“观察”。
- 人机协同：系统默认“辅助决策，不自动下单”，关键动作需人工确认。

> 说明：LLM 在给定严格规则与证据约束后，可以显著提升一致性与纪律性；系统目标是“尽可能理性”，并通过风控与复盘持续逼近稳定决策。

## 未来目标（智能化炒股助手愿景）
以下目标不是口号，而是系统可以逐步落地的路线图：

### 更全面的数据与事件理解
- 多源行情与盘口：深度盘口、逐笔成交、资金流向、筹码分布
- 多源新闻与公告：公告、研报、新闻、社媒情绪、主题热度
- 产业链图谱：上下游关联、同主题联动、资金共振

### 更强的分析与解释能力
- 量化指标集成：多时间尺度趋势、波动、拐点、背离检测
- 事件驱动分析：公告影响评估、预期差建模
- 可解释信号：每个建议都给出依据与反证

### 更贴近个人交易风格
- 个人偏好与风险画像
- 持仓与策略协同：仓位、止损、止盈、风控联动
- 交易复盘与反馈：用复盘训练个人策略偏好

### 更接近“真实交易”的辅助系统
- 策略沙盒与回测：支持多策略并行评估
- 预警系统：关键价位、资金异动、情绪突变
- 组合层面辅助：行业分散、风险暴露、相关性控制

### 安全与合规
- 默认只做决策辅助，不自动下单
- 关键建议需二次确认
- 记录每次建议的证据与来源
