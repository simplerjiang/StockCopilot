# GOAL-AGENT-NEW-001-P0 对齐规格与底座盘点

## 任务目标
1. 锁定 TradingAgents 对齐边界，防止后续实现回退到旧 Copilot 方向。
2. 盘点当前仓库可复用底座、不可复用方向和 MCP 能力缺口。
3. 统一术语、阶段顺序、角色职责、工作台 pane 结构和偏离警戒线。

## P0 闸门结论
1. 这个目标的产品定义已经冻结为 `Trading Workbench`，不是聊天助手，也不是已被手动删除的旧 GOAL-AGENT-002 会话面板变体。
2. 用户可见主叙事必须是 `stage` 推进、角色分工、讨论与治理，而不是一条最终回答。
3. P0 的完成不等于开始写前后端代码；它的职责是先把“什么必须保留、什么可以复用、什么必须禁止”写成后续不能偏离的规格。
4. R1-R7 只有在 P0 的术语、顺序、角色边界、工作台结构和底座盘点都冻结后才允许进入实现。

## 上游依赖
1. 无。P0 是整个目标的前置门禁。

## 下游影响
1. 为 R1 提供 contract 术语和状态边界。
2. 为 R2 提供角色到能力矩阵。
3. 为 R3/R4/R5/R6 提供统一产品叙事和不可偏离逻辑。

## 核心工作项
1. 冻结阶段顺序：`Analyst Team -> Research Debate -> Trader Proposal -> Risk Debate -> Portfolio Decision`。
2. 对照 `TradingAgents-main` 与 `TradingAgents-MCPmode-main`，输出角色职责表、工作台结构表、不可省略逻辑表。
3. 盘点本仓库可复用资产：股票页扩展区、图表、MCP、本地事实、交易计划、历史持久化接缝。
4. 盘点不可再沿用的旧方向：已被手动删除的旧 Stock Copilot 聊天叙事、伪 session、伪多角色卡片式输出。
5. 冻结术语：session、turn、stage、role message、tool event、current report、final decision、replay。
6. 输出偏离警戒线：哪些实现“看起来像 TradingAgents，实际上不是”。

## 详细执行拆解

### 一、参考实现对齐拆解
1. 先读 `TradingAgents-main` 的 graph、agent、CLI/workbench 呈现，不允许只看 README 或截图就下产品定义。
2. 把“用户能看到的骨架”和“内部运行顺序”拆开记录，前者决定 R4，后者决定 R1/R3/R5/R6。
3. 明确哪些特征必须保留到本仓库：阶段推进、角色分工、对抗式 debate、治理出口、运行态 report、用户可感知 progress。
4. 明确哪些特征不能照搬：长文本黑盒、CLI 布局、原始 memory 结构、未结构化的自由总结。

### 二、本仓库底座盘点拆解
1. 盘点现有股票页右侧扩展区和 `StockInfoTab` 的接入方式，确定新模块不重做整个股票终端，只占用既有扩展位。
2. 盘点图表、证据、本地事实、市场上下文、交易计划、历史分析结果等现有能力，判断哪些可作为 handoff 接口，哪些只能作为旧实现遗留参考。
3. 对每条可复用能力都写出“可直接复用 / 需封装 / 仅可参考 / 明确禁用”四类结论，防止开发期出现“仓库里有旧代码所以顺手复用”的漂移。
4. 对旧 `Stock Copilot / GOAL-AGENT-002` 相关命名实现统一降级为遗留态，不允许再作为产品层合同来源。

### 三、冻结术语与偏离警戒线拆解
1. 术语冻结的目标不是统一措辞，而是约束对象边界，后续 DTO、接口、数据库表、前端状态都必须沿用同一词汇表。
2. 每个术语必须包含边界说明，例如 session 何时创建、turn 何时递增、stage 是否固定、tool event 与 role message 的区别。
3. 偏离警戒线必须写到足够具体，让 reviewer 能一眼判断某实现是否“只是看起来像多 Agent”。
4. 对最容易偏移的点单独标红：聊天流回潮、假 debate、假并行、假 grounded、前端动画式阶段推进。

### 四、P0 对下游的强制输出
1. 给 R1 提供稳定术语和阶段顺序，避免 contract 层再次讨论“是否需要 turn/stage/replay”。
2. 给 R2 提供角色缺口矩阵，避免工具层在开发中途才发现 Product/Shareholder/Social 没有真实数据源。
3. 给 R3 提供阶段治理关系，避免 runner 把 bull/bear/manager 或 risk 三角色错误串并行化。
4. 给 R4 提供 workbench 结构基线，避免 UI 重新退回聊天主舞台。
5. 给 R5/R6 提供 authoritative object 清单，避免 debate/risk/report/decision 仍然依赖长文本反解析。

## P0 完成后的引用规则
1. R1-R7 如需引用产品定义、角色边界、工作台结构、禁用清单，应直接引用本文件，不得在子任务文件中重新发明一个相互冲突的版本。
2. 若后续切片发现 P0 与真实代码有不一致，必须先回改 P0，再继续开发，不能只在单个子任务里偷偷偏移。
3. 任何新补充的角色、阶段、pane 或工具权限，都必须先判断是否破坏本文件冻结的治理关系；若破坏，则视为超出 GOAL-AGENT-NEW-001 当前范围。

## TradingAgents 对齐基线

### 必须对齐的源码事实
1. `TradingAgents-main` 的核心不是自由聊天，而是 staged graph：analyst 输入、bull/bear debate、manager 裁决、trader proposal、risk debate、portfolio decision 逐段推进。
2. `TradingAgents-main` 真正暴露给用户的是 workbench，而不是“一个输入框 + 一个回答区”。用户持续看到 progress、message/tool feed、current report 和 stats。
3. `TradingAgents-main` 内部大量使用长文本，但本仓库不能照搬成长文本黑盒。这里必须把阶段输出、消息、工具活动、报告块和最终决策转成结构化对象。
4. `TradingAgents-MCPmode-main` 不是简单接工具，而是提供了工程化强化方向：Phase 0 公司概览、并行 analyst、独立 MCP 管理层、角色级权限控制、真实阶段化工作流。

### 用户可见主定义
1. 模块名称冻结为 `Trading Workbench`。
2. 模块位置冻结为股票详情页右侧扩展区，不重做整个股票页终端。
3. 工作台骨架必须长期存在：`Team Progress + Discussion Feed + Current Report + Follow-up`。
4. Feed 允许采用群聊式表现层，但底层真实模型必须仍然是 `session -> turn -> stage -> role`。
5. `Current Report` 是中央主舞台，不能退化为“最终回答之后附带出现的一块摘要卡”。

## 阶段顺序对齐表

| 顺序 | 用户可见主阶段 | V1 执行要求 | 必须保留的治理关系 | 不能偷懒的点 |
| --- | --- | --- | --- | --- |
| 0 | Company Overview Preflight | 作为预查步骤执行，服务于后续 analyst 输入；默认不单列为主阶段标题 | 只提供基础公司上下文，不替代 analyst team | 不能跳过 symbol/公司基础识别后直接进 debate |
| 1 | Analyst Team | `Market / Social / News / Fundamentals / Shareholder / Product` 在输入边界清晰时并行执行 | 先独立产出，不得提前合成统一结论 | 不能把 analyst 输出提前压成一个总回答 |
| 2 | Research Debate | `Bull Researcher -> Bear Researcher -> Research Manager` 串行推进，必要时保留多轮 debate | Bull 与 Bear 必须真实对抗，Research Manager 必须作裁决 | 不能用一个模型一次性写出多空双方和裁决 |
| 3 | Trader Proposal | `Trader` 在研究结论基础上形成交易提案 | Trader 不得替代 Research Manager | 不能让 trader 直接跳过研究阶段生成最终结论 |
| 4 | Risk Debate | `Aggressive / Neutral / Conservative Risk Analyst` 首轮并行生成，随后由治理出口收敛 | 三类风险立场必须相互制衡 | 不能只写一段通用风险提示 |
| 5 | Portfolio Decision | `Portfolio Manager` 基于研究、交易和风险结果作最终授权或否决 | 必须是 authoritative 决策出口 | 不能只做润色型摘要 |

## 角色职责对齐表

| 角色 | 主要职责 | 正式输入 | 正式输出 | 工具权限结论 |
| --- | --- | --- | --- | --- |
| Company Overview Analyst | 统一识别标的、公司基础信息和后续 analyst 公共上下文 | symbol、搜索结果、公司详情、基础面摘要 | `company_details` 预查对象 | `local_required/local_preferred` |
| Market Analyst | 负责 K 线、分时、技术指标和市场结构分析 | 公司预查、K 线、分时、指标、市场上下文 | market analysis block | `local_required` |
| Social Sentiment Analyst | 负责社媒/情绪近似分析与情绪噪音判别 | 公司预查、内部情绪源或受控外部 fallback | sentiment analysis block | `local_preferred`，缺口未补前必须显式降级 |
| News Analyst | 负责新闻、公告、市场/板块/个股事实研判 | 公司预查、本地事实、新闻公告、必要时外部补充 | news analysis block | `local_required`，外部只作 fallback |
| Fundamentals Analyst | 负责财务快照、估值和基本面研判 | 公司预查、基本面快照、财报相关事实 | fundamentals analysis block | `local_required/local_preferred` |
| Shareholder Analyst | 负责股东结构、集中度、机构/户数变化判断 | 公司预查、股东结构数据 | shareholder analysis block | 需要独立 MCP 补齐 |
| Product Analyst | 负责主营业务、产品线、收入构成和产业链定位 | 公司预查、产品业务数据 | product analysis block | 当前缺口，必须新增 MCP |
| Bull Researcher | 基于 analyst 输出构建看多论证 | analyst outputs、上一轮 bear 观点、可审计 memory | bull debate artifact | 禁止直接取数 |
| Bear Researcher | 基于 analyst 输出构建看空论证 | analyst outputs、上一轮 bull 观点、可审计 memory | bear debate artifact | 禁止直接取数 |
| Research Manager | 主持研究阶段、裁决分歧并形成投资计划 | analyst outputs、bull/bear debate、历史修正 | research decision + investment plan | 禁止直接取数 |
| Trader | 将研究计划转成交易提案 | research decision、市场结构、约束条件 | trader proposal | 默认禁止直接取数 |
| Aggressive Risk Analyst | 从高收益偏好角度挑战或支持 trader proposal | trader proposal、研究摘要、风险约束 | aggressive risk artifact | 禁止直接取数 |
| Neutral Risk Analyst | 从平衡收益/回撤角度评审 trader proposal | trader proposal、研究摘要、风险约束 | neutral risk artifact | 禁止直接取数 |
| Conservative Risk Analyst | 从防守和失效条件角度评审 trader proposal | trader proposal、研究摘要、风险约束 | conservative risk artifact | 禁止直接取数 |
| Portfolio Manager | 汇总风险辩论并作最终评级与拍板 | trader proposal、risk debate、market context | final decision | 禁止直接取数 |

## 工作台 Pane 对齐表

| TradingAgents 可见层 | 本仓库落点 | 强制要求 | 不能退化成什么 |
| --- | --- | --- | --- |
| Progress / team status | 左侧 `Team Progress` | 必须显示 stage、role、状态、并行执行信息、复用标记 | 不能只有几个静态角色头像 |
| Messages / tools | 中部 `Discussion Feed` | 必须混排 role message、tool event、阶段切换、降级事件和 follow-up | 不能只有最终文本回答 |
| Current Report | 中央主舞台 | 必须持续更新，authoritative 输出以 report 为准 | 不能做成小侧卡 |
| Stats / footer | 运行指标和状态条 | 至少保留 turn、session、latency、degraded flag、active stage 级信息 | 不能完全隐身，导致工作态不可感知 |
| User input | `Follow-up` 区 | 必须显式写明“默认续接当前 session”并支持 partial/full rerun | 不能伪装成普通客服输入框 |

## 统一术语冻结

| 术语 | 冻结定义 | 边界说明 |
| --- | --- | --- |
| session | 同一标的下连续研究过程的顶层容器 | 只有显式新建时才更换 session |
| turn | 一次首问或 follow-up 形成的增量研究轮次 | 不能只是一条聊天消息 |
| stage | 固定治理阶段的状态对象 | 不是前端临时标签 |
| role message | 角色在某一 turn/stage 下的正式发言或摘要 | 需要可回放、可引用 |
| tool event | 工具调用开始、进度、完成、失败或降级事件 | 需要进入 feed 和日志 |
| current report | 运行中持续更新的 authoritative 报告快照 | 不是最终答案的别名 |
| final decision | 由 Portfolio Manager 治理收口的结构化结论 | 需要能交接给交易计划和图表动作 |
| replay | 对历史 session/turn/stage/decision 的可视化回放 | 不能靠重新拼 prompt 重建 |

## 可复用底座清单

### 可直接复用
1. 股票详情页右侧扩展位仍然存在，`frontend/src/modules/stocks/StockInfoTab.vue` 仍是新工作台的承载点。
2. 图表、分时、日 K、策略信号和顶部市场总览链路已经稳定存在，可作为后续动作交接面。
3. `StockAgentAnalysisHistories` 以及相关历史服务仍然存在，可作为研究历史、交易计划联动和 replay 数据接缝之一。
4. 交易计划主链路、`TradingPlans` / `TradingPlanEvents` 和关联服务仍然存在，可承接 final decision 的后续动作。
5. 本地事实查询 `QueryLocalFactDatabaseTool`、市场上下文 `IStockMarketContextService`、`/api/news` 和 market/sector/stock 三层事实仍可复用。
6. 现有 MCP 形态已具备统一 envelope 雏形：`StockKlineMcp`、`StockMinuteMcp`、`StockStrategyMcp`、`StockNewsMcp`、`StockSearchMcp`。
7. 基本面快照接口 `/api/stocks/fundamental-snapshot` 与详情缓存 `/api/stocks/detail/cache` 仍可作为后续 `CompanyOverviewMcp` / `StockFundamentalsMcp` 的上游能力。

### 删除状态约束
1. 旧 `Stock Copilot / GOAL-AGENT-002` 已按用户当前决定视为手动彻底删除，不再作为新工作台的实现前提。
2. 即便仓库里仍残留 `StockCopilot*`、`/api/stocks/copilot*`、`/api/stocks/mcp*`、旧会话 DTO、旧 orchestrator 或相关测试，也只按待清理遗留处理，不视为必须沿用的底座。
3. 旧前端卡片组件如 `StockAgentCard.vue` 最多只能作为视觉或信息排布参考，不能决定新产品骨架。

## 禁用清单
1. 禁止恢复已被手动删除的旧 `Stock Copilot` / `GOAL-AGENT-002` 聊天叙事作为默认产品形态。
2. 禁止把多角色系统实现成“一个回答里分几段写 Bull / Bear / Risk / Manager”。
3. 禁止把 manager 角色实现成只做润色或摘要的文案层。
4. 禁止让 follow-up 在后端偷偷新开 session，再在前端伪装成续聊。
5. 禁止只保留 final answer，不保留 turn/stage/role/tool/report 的真实状态对象。
6. 禁止把群聊式 UI 外观误当成底层真实执行模型。

## 偏离警戒线
1. 如果 UI 主舞台重新回到聊天流，而 `Current Report` 退到附属区域，视为偏离。
2. 如果 bull/bear/risk 三组角色读取同一套 prompt 和同一份中立结论，只换显示名，视为偏离。
3. 如果阶段推进在后端不可见、不可持久化，只在前端动画里“看起来像在跑”，视为偏离。
4. 如果后置角色继续直接调用查询类工具，绕过 analyst 的 grounded 输入边界，视为偏离。
5. 如果 Product/Shareholder/Social 角色没有真实数据支撑却仍在 UI 中宣称已完成分析，视为偏离。

## 初版 MCP 能力盘点

| 能力域 | 当前依据 | 现状判断 | P0 结论 |
| --- | --- | --- | --- |
| 股票搜索与标的识别 | `/api/stocks/search`、搜索服务 | 已有普通 API | 需封装到 `CompanyOverviewMcp` |
| K 线行情 | `StockKlineMcp` | 已成熟 | 可直接复用 |
| 分时行情 | `StockMinuteMcp` | 已成熟 | 可直接复用 |
| 技术指标/策略信号 | `StockStrategyMcp` | 已成熟 | 可直接复用 |
| 新闻/公告/本地事实 | `StockNewsMcp`、`QueryLocalFactDatabaseTool` | 已成熟 | 可直接复用，需做 role-to-level 映射 |
| 外部搜索 fallback | `StockSearchMcp` | 已有但受控不足 | 只能作为 `external_gated` fallback |
| 公司详情聚合 | `/api/stocks/detail`、`/api/stocks/detail/cache` | 能力存在但未 MCP 化 | 需新增 `CompanyOverviewMcp` |
| 基本面快照 | `/api/stocks/fundamental-snapshot` | 能力存在但未 MCP 化 | 需新增 `StockFundamentalsMcp` |
| 股东结构 | 东方财富股东解析能力 | 部分能力存在 | 需新增 `StockShareholderMcp` |
| 产品业务 | 当前未发现稳定 adapter | 明显缺口 | 必须新增 `StockProductMcp` |
| 市场上下文 | `IStockMarketContextService`、本地 market/sector facts | 能力存在但未独立工具化 | 需决定独立 `MarketContextMcp` 或并入相关 MCP |
| 社媒情绪 | 现有内部源不足 | 缺口 | 需定义真实源或明确降级模式 |

## 角色缺口矩阵结论

| 角色分组 | 结论 | 后续动作 |
| --- | --- | --- |
| 可直接落地组 | Market、News、Bull、Bear、Research Manager、Trader、三类 Risk、Portfolio Manager | 进入 R1/R2 时可直接按 contract 和权限模型展开 |
| 需先补 MCP 封装组 | Company Overview、Fundamentals、Shareholder | R2 必须优先补齐，再进入主流程开发 |
| 需先补真实数据源组 | Product | 没有真实能力前只能 blocked，不允许假实现 |
| 需定义明确降级策略组 | Social Sentiment | R2 需先写清“内部源优先，外部受控 fallback”的策略 |

## 交付物
1. 角色职责对齐表。
2. 阶段顺序对齐表。
3. 工作台 pane 对齐表。
4. 底座复用清单。
5. 禁用清单和偏离警戒线。
6. 初版 MCP 能力盘点与角色缺口矩阵。
7. P0 执行报告：`.automation/reports/GOAL-AGENT-NEW-001-P0-DEV-20260325.md`。

## 本轮验证脚本
1. `Get-Content .\.automation\tasks.json -Raw -Encoding UTF8 | ConvertFrom-Json | Out-Null`
2. `Get-Content .\.automation\state.json -Raw -Encoding UTF8 | ConvertFrom-Json | Out-Null`
3. `Get-ChildItem .\backend -Recurse -File | Where-Object FullName -NotMatch '\\bin\\|\\obj\\' | Select-String -Pattern 'StockAgentAnalysisHistory|StockKlineMcp|StockMinuteMcp|StockStrategyMcp|StockNewsMcp|StockSearchMcp|QueryLocalFactDatabaseTool|IStockMarketContextService|fundamental-snapshot|detail/cache' | Select-Object -First 20 Path, LineNumber, Line`
4. 诊断检查本轮修改的 markdown/json 文件无新的解析错误。

## 测试目标
1. 文档级一致性检查：阶段、角色、术语在总文件和各任务文件中一致。
2. 能力盘点准确性检查：引用的现有服务、API、MCP 必须能在当前代码库中找到依据。
3. 规划正确性检查：任何后续任务都不能引入与 P0 冲突的产品定义。

## 回归测试要求
1. 防止需求回退为聊天助手。
2. 防止后续任务重新引入“manager 只是摘要器”的错误方向。
3. 防止旧 README 或旧任务残留文案覆盖 TradingAgents 对齐边界。

## 完成标准
1. 后续任务不再需要争论“是不是聊天助手”。
2. 后续任务不再需要争论 debate/risk loop 是否可选。
3. 角色能力缺口能直接生成 R2 backlog，而不是到开发时才发现缺数据。
4. R1-R7 可以直接引用本文件中的术语、顺序、角色边界、禁用清单和底座盘点，而不需要再回头重新定义主叙事。