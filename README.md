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
- /api/stocks/messages 盘中消息（占位）
- /api/stocks/detail 组合详情
- /api/stocks/detail/cache 组合详情（缓存）
- /api/stocks/sync 手动触发同步
- /api/news 本地事实新闻查询（按 symbol + level=stock/sector/market 精准过滤，前端展示使用批量 AI 清洗后的翻译/情绪/标签）
- /api/stocks/news/impact 资讯影响评估（公告/研报/新闻分级、来源可信度、同主题合并去重）
- /api/stocks/signals 事件驱动信号（证据/反证、历史对齐）
- /api/stocks/position-guidance 个性化风险与仓位建议
- /api/stocks/agents 多Agent分析（默认模型已切到 `gemini-3.1-flash-lite-preview-thinking-high`，支持前端显式触发 `Pro 深度分析` 并路由到 `gemini-3.1-pro-preview-thinking-medium`，普通分析严格禁用 Pro）
- /api/admin/login 管理员登录
- /api/admin/llm/settings/{provider} LLM 配置读取/更新（需管理员 token）
- /api/admin/llm/test/{provider} LLM 调用测试（需管理员 token）
- 统一 AI 对话审计日志：所有 AI 请求/响应/错误统一记录到 `backend/SimplerJiangAiAgent.Api/App_Data/logs/llm-requests.txt`（含 traceId、耗时与截断保护）

### 前端
- Stock Tabs 基础框架
- LLM 设置页签（管理员配置）
- K线/分时图 AI 关键价位叠加（突破线/支撑线，来源于多Agent分析）
- 股票信息页多Agent面板支持双档位触发：标准分析 / Pro 深度分析
- 治理开发者模式：参数说明、治理链路 Trace 查询、以及按 traceId 聚合的 LLM 对话会话前端可视化（请求/返回/异常一一对应，支持 JSON 美化）
- 全量资讯库：支持按关键字、层级（大盘/板块/个股）和情绪筛选本地 AI 清洗资讯，并可直接跳转原文

### 桌面端
- WinForms 容器 + WebView2 载入前端

## 数据同步与配置
- 后台定时任务按 appsettings.json 的 StockSync 配置抓取并落库
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

也支持环境变量覆盖：

```powershell
$env:OPENAI_API_KEY="你的运行时_API_KEY"
```

或按 provider 名称使用更明确的变量：

```powershell
$env:LLM__OPENAI__APIKEY="你的运行时_API_KEY"
```

本地忽略文件示例：

```json
{
	"providers": {
		"openai": {
			"provider": "openai",
			"apiKey": "你的运行时_API_KEY"
		}
	}
}
```

### 启动方式
在项目根目录运行：

```powershell
opencode
```

首次建议执行：
- `/models` 确认已选中 `zai/glm-5`
- `/init` 让 OpenCode 读取仓库结构与规则

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
- [x] GOAL-013 双轨数据中枢（Local+Global Dual-Track）与 LLM 职能调度中心（已完成 Step 2：本地事实库、受控外网路由、新闻精准过滤、Step 2.2 Task 4 的标准/Pro 模型分流、Step 2.3 的新浪板块资讯抓取/大盘多源聚合/无选股即可查看的大盘资讯与完整查询历史展示、Step 2.4 的本地事实批量 AI 清洗/翻译/标签隔离投喂、Step 2.5 的大盘资讯内嵌交互/外媒 RSS 时效清洗/本地事实 AI 重试补漏，以及 Step 2.6 的纯财经大盘源切换、活跃 RSS 替换与 `全量资讯库` 归档工作台）
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
- [ ] GOAL-008 交易计划引擎（盘前计划、盘中触发、失效条件）
	- 盘前自动生成候选池：主线板块、关键价位、预期催化、风险提示。
	- 盘中只执行“已定义触发”：避免临时主观冲动单。
	- 计划失效自动提示：跌破条件、量价背离、消息反转时触发撤销或降级。
	- 支持计划复用与版本记录：便于复盘同类行情策略表现。
- [ ] GOAL-009 情绪周期 + 板块轮动面板（主升/分歧/退潮识别）
	- 情绪温度指标：连板高度、涨停/跌停家数、炸板率、成交额结构。
	- 板块轮动强度：主线持续性、扩散度、资金净流入与领涨梯队稳定性。
	- 阶段标签：主升、分歧、退潮，直接映射仓位建议与交易频率。
	- 支持对比近5/10/20交易日，识别“短热度”与“中期趋势”差异。
- [ ] GOAL-010 执行风控闸门（单票/单日/连续亏损约束）
	- 单票风险：最大亏损阈值、最大回撤、止损触发后禁加仓。
	- 账户日内风险：单日最大回撤触发后自动进入防守模式。
	- 连续亏损保护：N连亏后强制降仓、降低开仓频率。
	- 时段约束：尾盘禁追高、临停风险提示、异常波动熔断提示。
- [ ] GOAL-011 复盘闭环（自动归因、周月改进清单）
	- 每笔交易自动归因：买点类型、卖点类型、是否遵守规则、盈亏来源。
	- 违规行为识别：追涨杀跌、逆势加仓、无计划交易等行为统计。
	- 周/月策略报告：胜率、盈亏比、回撤、执行一致性、最需优化项。
	- 输出“下一周期可执行改进清单”，形成持续迭代闭环。

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
