# GOAL-AGENT-NEW-001 详细规划报告（2026-03-25）

### 本轮结论
1. 这次把 `GOAL-AGENT-NEW-001` 从“方向描述”重写成“可执行规格”。
2. 新规划不再只说“参考 TradingAgents”，而是明确写清楚：到底照它什么、改它什么、先做什么、后做什么。
3. 这轮仍然只做规划，不写前后端实现。
4. 额外新增一份源码分析备忘：`.automation/reports/GOAL-AGENT-NEW-001-TRADINGAGENTS-ANALYSIS-20260325.md`。
5. 从本次开始，后续新增或重写的规划/报告类文档默认只保留中文版，不再额外产出英文版。

### 文档拆分结果
1. 这份文件继续保留为“本轮规划报告”和背景说明，不再承担全部任务细节。
2. 从这次开始，`GOAL-AGENT-NEW-001` 的执行型规划已拆到 `.automation/plan/GOAL-AGENT-NEW-001/` 目录。
3. 总需求与约束文件：`.automation/plan/GOAL-AGENT-NEW-001/README.md`。
4. 分任务文件：`.automation/plan/GOAL-AGENT-NEW-001/GOAL-AGENT-NEW-001-P0.md` 到 `.automation/plan/GOAL-AGENT-NEW-001/GOAL-AGENT-NEW-001-R7.md`。
5. 其中 `R7` 现在单独承担“测试目标、回归测试、多维验收与门禁”，不再只放在总文档末尾做收尾说明。

### 这轮规划要解决的核心问题
把之前那个过于模糊的目标，收敛成 4 个必须明确的问题：

1. 我们到底要做哪个用户可见模块。
2. TradingAgents 里哪些运行机制必须保留。
3. 当前仓库哪些底座能复用，哪些不能再沿用旧方向。
4. 后续开发必须按什么顺序推进，才能避免越做越乱。

### 从 `TradingAgents-main` 源码读出来的关键事实
#### 1. 它的骨架不是“聊天”，而是分阶段图执行
根据：
1. `noupload/TradingAgents-main/tradingagents/graph/setup.py`
2. `noupload/TradingAgents-main/tradingagents/graph/conditional_logic.py`
3. `noupload/TradingAgents-main/tradingagents/graph/propagation.py`

真实顺序是：
1. 分析员串行跑。
2. 多空研究员辩论。
3. 研究经理裁决并给出投资计划。
4. 交易员形成交易提案。
5. 三类风险角色继续辩论。
6. 组合经理最终拍板。

对本仓库的 V1 改造约束：
1. 大阶段顺序仍然必须保持 `Analyst Team -> Research Debate -> Trader Proposal -> Risk Debate -> Portfolio Decision`。
2. 但阶段内部若角色之间不存在严格前置依赖，应允许并行执行以压缩等待时间。
3. 因此 `Analyst Team` 下的四类 analyst 在 V1 应改为并行运行，`Risk Debate` 下的三类 risk analyst 在各自拿到 trader proposal 后也应并行生成首轮观点。
4. bull / bear debate、manager 治理出口、portfolio 决策顺序仍然不能并行化，否则会破坏真实制衡链路。

这意味着：
1. 新模块绝对不能做成“一个聊天框 + 几个角色标签”的假多 Agent。
2. `stage` 必须是产品主叙事，不是内部实现细节。

#### 2. 它真正暴露给用户的是 workbench
根据：
1. `noupload/TradingAgents-main/cli/main.py`
2. 附件里的 CLI 截图

它始终把四类内容并排摆着：
1. team/agent 进度，
2. message/tool feed，
3. current report，
4. 统计信息。

这意味着：
1. 我们的新 UI 必须是工作台。
2. `Current Report` 必须是主阅读区，不是个附属小卡片。

#### 3. 它内部是长文本，但我们不能照搬成长文本黑盒
根据：
1. `noupload/TradingAgents-main/tradingagents/agents/researchers/bull_researcher.py`
2. `noupload/TradingAgents-main/tradingagents/agents/managers/research_manager.py`
3. `noupload/TradingAgents-main/tradingagents/agents/trader/trader.py`
4. `noupload/TradingAgents-main/tradingagents/agents/risk_mgmt/aggressive_debator.py`
5. `noupload/TradingAgents-main/tradingagents/agents/managers/portfolio_manager.py`

它内部很多内容其实是长文本辩论和长文本总结。

但对于本仓库来说：
1. 如果也照搬成长文本主导，会让持久化、回放、动作联动、测试验证都变脆。

所以本仓库要做的是：
1. 保留它的阶段顺序、角色责任、辩论结构、工作台叙事。
2. 把输出层改造成结构化对象，接到现有图表、本地事实、证据抽屉和交易计划链路。

#### 4. 它的 memory 很轻，而且按角色拆分
根据：
1. `noupload/TradingAgents-main/tradingagents/agents/utils/memory.py`

这意味着后面如果补 research memory：
1. 应该按角色建。
2. 应该可审计。
3. 不能做成隐藏的自由推理黑箱。

### 当前仓库复用盘点
#### 能继续复用的
1. 股票页右侧扩展区还在，`StockInfoTab.vue` 只是留空，不是删掉整个插槽。
2. 交易计划链路还活着。
3. `StockAgentAnalysisHistory` 还在，可以作为分析结果持久化接缝。
4. 现有 MCP、本地事实、图表、市场上下文、证据链路都还是底座。
5. 一些旧组件比如 `StockAgentCard.vue` 仍然可以作为 evidence / metrics / action 呈现参考。

#### 新增参考基线：`TradingAgents-MCPmode-main`
它不是简单“给 TradingAgents 接了几个工具”，而是给我们提供了 4 个非常具体的参考做法，后续规划必须显式吸收：
1. 把 `Company Overview Analyst -> 并行 Analyst Team -> Debate -> Manager -> Risk` 做成真正的阶段化工作流，而不是 prompt 拼接流水账。
2. 把 MCP 管理上升到独立基础设施层，至少包含：配置文件、客户端管理、工具发现、按角色权限分配、失败回退。
3. 把“哪些角色可以直接调 MCP”做成显式开关，而不是写死在 prompt 里。
4. 如果现有 MCP 无法覆盖某个 analyst 的职责，不允许让 LLM 硬编，必须补齐或改造 MCP 能力本身。

#### 明确不能再当主方向复用的
1. 旧 Stock Copilot 聊天产品叙事。
2. 旧 GOAL-AGENT-002 的 one-to-one assistant 体验。
3. 已移除的公开会话/多 Agent 前后端接口。

### 新模块的产品定义
#### 模块暂定名
`Trading Workbench`

#### 模块位置
1. 股票页右侧扩展区。
2. 只设计这个模块，不改整个股票页。

#### 交互形态定义
1. 整体产品形态仍然是 `Trading Workbench`，不是 one-to-one assistant。
2. 但 `follow-up` 的表现层允许采用“群聊式讨论线程”，让用户更直观看到上下文延续和不同角色的连续发言。
3. 群聊只是一种 UI 呈现，不是系统真实模型；底层真实单位仍然必须是 `session -> turn -> stage -> role`。
4. 因此产品应定义为：`工作台骨架 + 受控多 Agent 讨论线程 + Current Report/Final Decision 收口`。

#### 核心用户故事
用户在某只股票详情页里，可以：
1. 发起一个多角色研究 session。
2. 看见真实阶段推进。
3. 看见每个角色的消息与工具活动。
4. 持续阅读中央 `Current Report`。
5. 继续追问且承接同一 session。
6. 把最终结论交给图表、证据、本地事实、交易计划等已有工作流。

### 必须突出强调的两个核心
这两个核心不是“附加特性”，而是整个方案是否还忠于 `TradingAgents-main` 的分水岭。如果后续开发把这两点做弱了，整个模块就会退化成普通聊天助手或伪多 Agent 面板。

#### 核心一：多轮获取信息
这里说的“多轮”，不是单纯支持用户连续发几句话，而是要让系统在同一研究 session 里，持续补信息、补证据、补争论、补决策，而不是每问一次就从零开始。

必须明确做到：
1. 第一轮提问生成基线研究 session，后续追问默认续接这个 session，而不是新开隐藏会话。
2. 后续追问可以不是“重新完整分析”，而是根据意图只补某个维度，例如：补市场分析、补新闻、重跑研究辩论、只重跑风险评审、或在保留旧结论的前提下追加证据。
3. 每一轮都必须继承上一轮已经产出的：stage 状态、role 输出、tool 结果、证据引用、current report 快照、final decision 快照。
4. 每一轮追加的新信息必须能回写到同一个 session 时间线里，而不是只留在前端临时态。
5. Follow-up 的处理必须是“基于现有研究状态继续向前走”，而不是“再生成一条新的聊天回答”。

这在产品上具体表现为：
1. 用户第一次问“这只票值不值得看”，系统完成完整一轮研究。
2. 用户第二次问“把最近三天公告和板块异动补进去”，系统只追加缺失信息并更新 report。
3. 用户第三次问“如果明天低开 3%，风险组怎么改判断”，系统保留前面 analyst 和 researcher 结果，只让 trader / risk / manager 基于已有上下文继续推进。

绝对不允许出现：
1. 每轮追问都重置上下文，导致前面 debate 和 report 丢失。
2. 前端表面写着“继续当前会话”，后端实际重新跑了一个新 session。
3. 只保留最终答案，不保留中间新增信息是如何被获得、被讨论、被纳入结论的。

#### 核心二：多 Agent 相互讨论、相互制衡
这里说的“多 Agent”不是多个角色各写一段话，而是不同角色拿着不同职责、不同证据范围、不同立场，在既定顺序里相互挑战、相互限制，然后再由上层角色作治理决策。

必须明确做到：
1. `Analyst Team` 先独立产出不同维度的输入，不能提前混成一个总回答。
2. `Bull Researcher` 和 `Bear Researcher` 必须形成真实来回辩论，不能只是各说一次就结束。
3. `Research Manager` 不是把多空意见做拼接摘要，而是要作裁决、收敛分歧、形成投资计划。
4. `Trader` 不能直接替代 `Research Manager`，只能在研究结论基础上形成交易提案。
5. `Risk Team` 不能只给一个“风险提示”，而要有激进、中性、保守三种不同风格相互制衡。
6. `Portfolio Manager` 必须是治理出口，负责在研究、交易、风险三层意见中作最终授权或否决。

这在产品上必须可见：
1. 用户能看到谁支持、谁反对、谁在修正、谁在拍板。
2. 用户能看到分歧点、争议证据、风险边界，而不是只看到一个结论。
3. 当前 `Current Report` 必须随着 debate 和 risk review 演进，而不是最后突然冒出一个答案。

绝对不允许出现：
1. 一个模型一次性写出“牛方 + 熊方 + 风险 + 决策”，假装经过讨论。
2. bull / bear / risk 三方使用完全相同的数据和话术，只换角色名。
3. manager 没有治理动作，只是把所有内容重新润色一遍。
4. UI 上只有几个角色卡片，实际上运行链路里没有 debate loop 和治理顺序。

### 明确功能范围
#### 1. Session 层
必须支持：
1. 每个股票 workspace 有自己的 active session。
2. 显式 session id。
3. 显式 current stage。
4. 显式 current turn。
5. follow-up 默认继续当前 session。
6. 显式“新建会话”。
7. 同股票历史 session 回放。

绝对不能：
1. 每次追问偷偷新开 session。
2. 不同股票共用一个会话。
3. 把历史辩论压扁成一个看不见过程的结果块。

#### 1.5 Turn 层
必须支持：
1. 每次用户首问或追问都形成一个显式 `turn`。
2. 每个 turn 必须记录：用户问题、continuationMode、复用范围、重跑范围、开始时间、结束时间、变化摘要。
3. Feed 必须按 turn 分段显示，而不是把所有消息混成一条无边界长聊天记录。
4. 当前 turn 必须能驱动当前 stage、当前 feed、当前 report 更新。
5. 同一 session 下必须允许 Turn 1、Turn 2、Turn 3 持续追加，并支持回放和比较。

绝对不能：
1. 把 turn 只当成后端字段，前端完全不可见。
2. 用户追问后只追加一条聊天回答，却看不出这轮到底复用了什么、更新了什么。
3. 用自由聊天流替代 turn 级状态管理。

#### 2. Stage 层
V1 固定阶段：
1. Analyst Team
2. Research Debate
3. Trader Proposal
4. Risk Debate
5. Portfolio Decision

每个 stage 都必须有：
1. 状态。
2. 起止时间。
3. 当前 active role 或 active role set。
4. 摘要。
5. degraded flags。
6. executionMode（serial / parallel）。

执行约束（性能保护）：
- **并行化（Parallel Execution）**：对于互相不依赖的角色必须严格并行执行。例如 `Analyst Team` 下的 4 个基础数据分析师（Market, Social, News, Fundamentals）应完全并行运行；`Risk Debate` 下的 3 种风险评估角色（激进、中立、保守）也应完全并行独立生成，大幅压缩用户的总等待时间。
- **阶段内依赖边界**：并行只允许发生在同一 stage 内部且输入边界清晰的角色之间；任何需要引用对手观点、治理结论或上层裁决的角色，仍然必须保持串行推进。

#### 3. Role 层
V1 吸收 `TradingAgents-MCPmode` 的 15 角色架构，固定以下角色：
0. Company Overview Analyst (作为第 0 阶段，预查公司基础面供后续并行使用)
1. Market Analyst
2. Social Sentiment Analyst
3. News Analyst
4. Fundamentals Analyst
5. Shareholder Analyst (新增：股东架构分析)
6. Product Analyst (新增：产品业务分析)
7. Bull Researcher
8. Bear Researcher
9. Research Manager
10. Trader
11. Aggressive Risk Analyst
12. Neutral Risk Analyst
13. Conservative Risk Analyst
14. Portfolio Manager (等同于 Risk Manager)

#### 4. Message / Tool Feed
统一时间线要能混排：
1. 角色消息。
2. 工具调用开始。
3. 工具结果。
4. 工具实时进度。
5. 降级事件。
6. 阶段切换事件。
7. 用户 follow-up。

呈现形态：
1. Feed 允许采用“群聊式讨论线程”展示，增强上下文连续感。
2. 但 Feed 必须按 `turn` 分段，并在每个 turn 顶部显示：用户问题、continuationMode、复用范围、重跑范围。
3. analyst 类角色更适合输出结构化消息卡或工具摘要，不宜无限制自由长聊。
4. bull / bear / risk 类角色可以更接近讨论式消息，但仍必须受角色职责和输入边界约束。
5. manager 类角色必须承担治理和收口职责，不能退化成普通群聊成员。

体验约束（UX 保护）：
- **微流式反馈（Micro-streaming）**：为了掩盖多 Agent 执行的长延迟，除了状态变更为 `running`/`completed` 外，必须给 Feed 流加上工具调用的实时状态（例如：“Market Analyst 正在读取近30天日K...”），用细粒度的 UI 动效让用户感知到系统正在积极工作。
- **最小进度粒度**：Feed 不能只在角色结束时才刷新；至少要覆盖 `role started`、`tool dispatched`、`tool progress`、`tool completed`、`role summary ready` 这五类事件，确保用户在长耗时阶段内持续看到前进信号。

#### 5. Current Report
中央主舞台，运行过程中持续更新。

需要覆盖的报告块：
1. Market Analysis
2. Social Sentiment Analysis
3. News Analysis
4. Fundamentals Analysis
5. Research Debate Summary
6. Trader Proposal
7. Risk Review Summary
8. Portfolio Decision

#### 6. Final Decision
最终输出必须结构化为：
1. 结论。
2. 置信度区间。
3. thesis。
4. 支撑证据。
5. 反证。
6. 风险限制。
7. 失效条件。
8. 下一步动作。

#### 7. 动作交接
必须直接支持：
1. 看日 K。
2. 看分时。
3. 打开证据抽屉。
4. 打开本地事实上下文。
5. 起草交易计划。

#### 8. 回放
必须支持：
1. 同股票历史 session 列表。
2. 每个 session 的最终决策快照。
3. 阶段式回放。
4. 与当前 session 的摘要对比。

### V1 明确不做
1. 真正下单或模拟交易执行。
2. 多股票组合看板塞进这个模块里。
3. 用户自己编辑 agent graph。
4. 展示 raw chain-of-thought。
5. 复刻 TradingAgents 的 CLI 或微信入口。

### 编码前必须先锁死的 contract
编码前必须先把这些对象定义清楚：
1. Session Contract
2. Turn Contract
3. Stage Contract
4. Role Message Contract
5. Tool Activity Contract
6. Current Report Contract
7. Final Decision Contract

### 这个模块的 UI 设计
下面是模块级 Markdown 线框图。

#### 桌面端
```md
+--------------------------------------------------------------------------------------+
| Trading Workbench                                                                    |
| Symbol: 贵州茅台  Session: #TW-20260325-01  Turn: 3  Status: Running · Risk Debate   |
| [继续当前会话] [新建会话] [会话回放] [起草交易计划]                                  |
+-----------------------------------+-----------------------------------------------+
| Team Progress                     | Discussion Feed                              |
|                                   |                                               |
| Analyst Team                      | Turn 3 · Follow-up                            |
|  - Market Analyst      reused     | 用户：如果明天低开 3%，风险组怎么改判断？     |
|  - Sentiment Analyst   reused     | 系统：继续当前 session；复用前序研究，仅重跑风控 |
|  - News Analyst        reused     | 10:31 Aggressive Risk：低开 3% 仍可观察分批介入 |
|  - Fundamentals        reused     | 10:32 Conservative Risk：低开说明承接偏弱     |
|                                   | 10:33 Neutral Risk：等待首小时量价确认        |
| Research Debate                   | 10:33 Tool：Risk scenario stress check -> running |
|  - Bull Researcher    reused      | 10:34 Tool：正在评估低开 3% 的回撤容忍度      |
|  - Bear Researcher    reused      | 10:35 Portfolio Manager：最终建议暂缓立即执行 |
|  - Research Manager   reused      | 10:35 系统：Risk Review / Portfolio Decision 已更新 |
|                                   |                                               |
| Trader Proposal                   | [展开当前 turn 详情]                          |
|  - Trader             reused      |                                               |
|                                   |                                               |
| Risk Debate                       |                                               |
|  - Aggressive Risk   completed    | 10:37 Aggressive Risk -> 风险收益比偏正向     |
|  - Neutral Risk      completed    | 10:38 Neutral Risk -> 平衡仓位与回撤后转谨慎  |
|  - Conservative Risk completed    | 10:38 Conservative Risk -> 强调估值回撤边界   |
|                                   |                                               |
| Portfolio Decision                |                                               |
|  - Portfolio Manager completed    |                                               |
+-----------------------------------+-----------------------------------------------+
| Current Report                                                                        |
| ------------------------------------------------------------------------------------ |
| Stage: Risk Debate                                                                    |
| Headline: 本轮仅重跑风险评估后，结论调整为等待确认后再考虑介入                       |
|                                                                                      |
| Summary                                                                               |
| - Turn 3 复用了前序 analyst / research / trader 结论                                 |
| - 风险组围绕“低开 3%”进行了追加讨论                                                  |
| - Portfolio Manager 将执行节奏从“可分批介入”调整为“等待确认”                        |
|                                                                                      |
| Evidence | Disagreements | Risk Limits | Invalidation | Next Actions                 |
| [查看证据] [查看分歧] [查看风控] [查看失效条件] [看日K] [看分时] [起草交易计划]     |
+--------------------------------------------------------------------------------------+
| Follow-up                                                                              |
| [像在研究群聊里继续追问，默认延续当前 session 与当前 turn 上下文]                   |
| Mode: [继续当前研究 v]  Reuse: [自动识别]  Rerun: [自动识别]                         |
| [发送追问] [仅重新跑风险评估] [仅刷新新闻分析] [同 session 全量重跑]                 |
+--------------------------------------------------------------------------------------+
```

#### 移动端
```md
1. 顶部：symbol + session 状态 + 主动作
2. Tabs：Progress | Chat Feed | Report | Follow-up
3. 运行中默认落在 Report tab
4. Progress：团队/角色堆叠卡
5. Chat Feed：按 turn 分段的群聊式讨论线程
6. Report：当前报告 + 动作按钮
7. Follow-up：聊天式输入区 + continuation 选项 + 复用/重跑预览
```

#### UI 规则
1. 它整体必须像交易研究工作台，而不是客服聊天窗。
2. `Current Report` 是视觉中心。
3. 桌面端要持续可见 progress 和 feed。
4. 跟进输入框必须明确写出“默认延续当前 session”。
5. 必须单独提供“新建会话”，防止隐式重开。
6. 当 stage 内存在并行角色时，progress 必须显式展示“并行执行中”，而不是误导用户认为系统卡在单一角色。
7. feed 面板必须支持微流式刷新和轻量动效，不能只在整段文本生成完成后一次性刷出。
8. feed 可以采用群聊式消息外观，但必须是“受控多 Agent 讨论线程”，不能放任所有角色自由越权发言。
9. 每个 turn 都必须在 UI 上可见，至少显示：用户问题、continuationMode、复用范围、重跑范围、变化摘要。
10. 最终面向用户的结论可以由 manager 角色以一条消息收口，但 authoritative 结果仍必须落在 `Current Report` 和 `Final Decision`。

### 非常详细的开发拆分
下面的拆分不再只写“做什么模块”，而是按 `TradingAgents-main` 的真实功能逻辑和开发依赖顺序，把每个阶段拆到足够细，尽量降低后续开发偏离的空间。

#### P0-Pre. MCP 前置改造闸门与实测验证
目标：
在开始任何后续实现工作前，先把所有“需要补充 / 优化 / 修改 MCP”的事项提升为最高优先级任务，并通过真实调用和真实模型验证，确认 MCP 能拿到数据、Prompt 已明确写入 MCP 使用方式、LLM 也确实理解并执行这套规则。**未通过本阶段，不允许进入 P0 及后续任何实现任务。**

详细步骤：
1. 先汇总一张 `MCP 前置任务总表`，把所有需要补充、优化、修改、新增、扩展字段、补权限、补降级协议、补错误处理的 MCP 事项放在所有任务最前列，不允许散落在后续章节里才处理。
2. 对所有 `local_required` 和关键 analyst 依赖的 MCP 做真实可用性检查：必须实际调用接口或工具，验证是否真能拿到数据，而不是只根据代码里“看起来有 service / endpoint”就判定可用。
3. 对每个关键 MCP 记录最小实测结果：是否成功、返回是否为空、关键字段是否齐全、时间戳是否新鲜、evidence / source 是否可回溯、失败时错误码是否可识别。
4. 对 `Company Overview`、`Fundamentals`、`Shareholder`、`Product`、`News`、`Market` 等角色入口能力逐一判断：当前是“直接可用”“需要优化字段”“需要改 adapter”“需要新增 MCP”“只能 fallback”。
5. 在正式开发前，逐个检查角色 Prompt / 系统 Prompt 是否已经明确写入 MCP 使用规则：先用哪个 MCP、何时允许 fallback、何时必须停止、哪些角色禁止直接调用工具。没有写进 Prompt 的，视为未完成。
6. 必须用**环境内可用的 LLM key 实测**这套 Prompt 契约：至少验证模型是否会优先选择系统 MCP、是否理解 MCP 使用顺序、是否能在本地 MCP 无数据时再走 fallback、是否会避免越权调用不属于当前角色的工具。
7. 对 Prompt 实测不能只看“模型回复看起来像懂了”，必须看真实 tool call / runner 轨迹，确认模型的工具选择与文档约束一致。
8. 若发现某个 MCP 拿不到数据、Prompt 没写清楚 MCP 用法、或者 LLM 实测不能稳定理解并执行 MCP 规则，则后续任务必须标记为 blocked，先回到 MCP / Prompt 层修正，不允许硬着头皮继续做工作流或 UI。

本阶段交付物：
1. `MCP 前置任务总表`（位于全部任务最前列）。
2. `关键 MCP 实测可用性报告`。
3. `Prompt 中 MCP 使用规则检查表`。
4. `基于环境内 LLM key 的 MCP 理解与调用实测报告`。

完成标准：
1. 后续阶段开始前，关键 MCP 已经完成真实拿数验证，而不是停留在代码推断。
2. Prompt 中已经明确写入 MCP 使用顺序、fallback 条件、停止条件和角色权限边界。
3. 使用环境内 LLM key 的实测结果证明：模型能理解并执行 MCP 使用 Prompt，而不是只在文档中看起来正确。
4. 任一关键 MCP、Prompt 或 LLM 实测未通过时，后续任务不得开工。

#### P0. TradingAgents 对齐规格与底座盘点
目标：
先把“什么必须照着 TradingAgents 做”锁死，再谈实现，避免先写代码、后补逻辑。

必须对齐的 TradingAgents 依据：
1. `tradingagents/graph/setup.py`
2. `tradingagents/graph/conditional_logic.py`
3. `tradingagents/graph/trading_graph.py`
4. `cli/main.py`
5. `agents/utils/agent_states.py`
6. `agents/utils/memory.py`

详细步骤：
1. 把 `TradingAgents-main` 的完整运行阶段顺序冻结成一张对齐表，明确 analyst、research、trader、risk、manager 五大阶段的先后关系。
2. 增加一张 `TradingAgents-main vs TradingAgents-MCPmode-main` 对照表，明确后者新增了哪些关键工程化能力：Phase 0 公司概览、并行 analyst 扩容、MCP Manager、角色级 MCP 权限控制、辩论轮次配置。
2. 把每个角色的职责、输入、输出、允许使用的数据范围整理成角色职责表。
3. 把 CLI 中的可见结构拆成工作台结构表，明确 progress、messages/tools、current report、stats 四块在我们产品里分别落到哪里。
4. 把 TradingAgents 的 debate loop 和 risk loop 单独列为“不可省略逻辑”，写成实现约束，而不是写成可选增强项。
5. 把当前仓库里能复用的底座列成资产清单：股票页扩展位、图表、资讯、市场上下文、本地事实、交易计划、历史持久化接缝。
6. 把当前仓库现有 MCP 能力做成一张 `现有 MCP 能力盘点表`，至少覆盖：K 线 / 分时、公司信息、财务报表、新闻公告、本地事实、证据链接、市场概览、板块信息。
7. 对照 15 个角色的职责，做一张 `角色 -> 所需 MCP 能力 -> 当前是否满足 -> 改造责任归属` 的缺口矩阵；凡是不满足的，不允许在后续实现中跳过，必须形成 MCP 改造任务。
6. 把当前仓库里绝不能再沿用的旧方向列成禁用清单：旧聊天 Copilot、伪会话、伪多 Agent 卡片式输出。
7. 把“多轮获取信息”和“多 Agent 相互讨论、相互制衡”写成顶层验收原则，后续每个切片都必须回指这两条原则。
8. 把术语统一：session、turn、stage、role message、tool event、current report、final decision、replay，不允许前后端各自命名。
9. 把移动端和桌面端的 pane 优先级锁死，避免后面 UI 实现时又回到聊天主舞台。
10. 把 `follow-up`、`partial rerun`、`full rerun in same session`、`start new session` 这四个概念提前区分清楚。
11. 把后续需要新增的数据库对象、接口对象、前端状态对象先列出来，但此阶段不写代码。
12. 把所有“看起来像 TradingAgents、实际上不是 TradingAgents” 的假实现模式列成偏离警戒线。

交付物：
1. 角色职责对齐表。
2. 阶段顺序对齐表。
3. UI pane 对齐表。
4. 底座复用清单。
5. 偏离警戒线清单。
6. 现有 MCP 能力盘点表。
7. 角色到 MCP 能力缺口矩阵。

完成标准：
1. 后续开发已经不需要再争论“是不是聊天助手”。
2. 后续开发已经不需要再争论“bull/bear/risk debate 是不是可选”。
3. 后续每个切片都有明确的上游依赖和下游交付物。
4. 后续开发已经不需要再争论“某个角色缺数据时是先糊 prompt 还是先改造 MCP”。答案必须是先改造 MCP。

#### P0.5 MCP Foundation 与能力改造计划
目标：
参考 `TradingAgents-MCPmode-main` 的工程做法，把 MCP 接入从“角色偶尔可调用的工具集合”提升为工作流基础设施层；同时把本仓库现有 MCP 能力是否满足 15 角色目标做成明确的改造计划。

必须参考的 MCPmode 依据：
1. `TradingAgents-MCPmode-main/README.md`
2. `TradingAgents-MCPmode-main/MCP工具集成与多智能体工作流设计指南.md`

详细步骤：
1. 设计本仓库自己的 `MCP Manager` / `Tool Gateway` 抽象层，职责至少包括：加载 MCP 配置、初始化客户端、发现工具、按角色下发工具、统一超时与错误处理。
2. 明确 MCP 配置来源，至少支持：后端配置文件、环境变量、必要时的前端受控开关；不允许把工具地址和权限直接散落在各个 role prompt 里。
3. 设计角色级 MCP 权限模型，参考 MCPmode 的做法，支持类似 `MARKET_ANALYST_MCP=true`、`TRADER_MCP=false` 的显式开关。
4. 规定 MCP 权限是后端执行层约束，不是前端展示层约束；即使前端误传，也不能让无权限角色拿到工具。
5. 对现有 MCP 做能力审计，逐项判断是否满足以下目标：公司概览、K 线 / 分时、技术指标、新闻公告、公司基本面、股东结构、产品业务、市场概览、板块上下文、本地事实查询。
6. 对不满足的能力建立改造优先级：
  - P0：会阻断 15 角色主流程的缺口，必须先改。
  - P1：会影响分析质量但有暂时替代方案的缺口。
  - P2：可后续增强的扩展能力。
7. 明确改造原则：如果某角色的目标数据在现有 MCP 中不存在，必须优先新增或扩展 MCP 接口，而不是改 prompt 让 LLM 凭网页搜索或常识补全。
8. 对 `Shareholder Analyst` 和 `Product Analyst` 单独给出结论：若本仓库当前没有对应 MCP，必须新增股东结构和产品业务信息的 MCP / adapter，不能只把这两个角色保留在文档层。
9. 为 `Company Overview Analyst` 规定专用能力集合，至少要能稳定返回标准化的 `company_details`，供后续并行 analyst 使用。
10. 为每个 MCP 结果定义结构化标准，保证后续可以写入 evidence、tool events 和 report block，而不是把原始文本直接塞给 LLM。
12. 定义 MCP 失败与降级协议：超时、空数据、字段缺失、脏数据、过期数据都必须返回标准化 degraded 标记。
13. **增加 MCP 服务不可用的硬失败规则**：如果某个被当前阶段标记为 `local_required` 的 MCP 服务处于不可连接、不可鉴权、持续超时或明确宕机状态，则必须立即中止本轮请求，禁止静默跳过或继续后续角色执行。
14. 当发生上述硬失败时，后端必须返回明确的 machine-readable 错误码与失败 MCP 信息，前端必须弹出阻断式提示弹框，明确告诉用户“哪个 MCP 服务不可用、当前请求已停止、建议稍后重试或联系管理员”，而不是只在 feed 里埋一条日志。
15. 只有 `external_gated` 或可选增强类工具失败时，才允许按降级逻辑继续；`local_required` 的核心 MCP 不可用不属于可静默降级范畴。
16. 定义 MCP 观测性：记录 serverName、toolName、requestSummary、latency、errorCode、traceId、cacheHit，供 feed、日志与验收使用。
17. 规定 Web Search 永远只是 fallback，不计入“现有能力已满足”；只要某角色长期依赖 Web Search，仍视为 MCP 能力缺口未解决。

本阶段交付物：
1. MCP Manager / Tool Gateway 设计文档。
2. 角色级 MCP 权限模型。
3. MCP 能力审计清单。
4. MCP 改造优先级 backlog。
5. MCP 结果标准结构与降级协议。

本阶段不允许偏离：
1. 不允许把 MCP 接入分散到每个 role 的私有实现里而没有统一管理层。
2. 不允许用“先让 Agent 联网搜，后面再补 MCP”作为默认路线。
3. 不允许把文档里新增的角色留给未来空实现；缺口必须形成明确的 MCP 改造任务。
4. 不允许把核心 MCP 服务不可用当成普通降级静默处理；对 `local_required` 工具必须 fail-fast 并中止请求。

#### P0.6 基于当前仓库代码的初版 MCP 能力盘点表
以下盘点不是概念推测，而是根据当前仓库已存在的后端实现整理出来的第一版事实表。它的目的不是证明“已经足够”，而是明确：我们现在到底已经有什么、哪些只是后端能力尚未封装成 MCP、哪些还完全不存在。

| 能力域 | 当前代码依据 | 当前接入形态 | 现状判断 | 对 15 角色的意义 | 必要改造 |
| --- | --- | --- | --- | --- | --- |
| 股票搜索 / 标的识别 | `StocksModule` 的 `/api/stocks/search`，`IStockSearchService` | 普通 API，不是 MCP envelope | 已有 | 可支撑 `Company Overview Analyst` 的 symbol 识别入口 | 建议补 `StockSearchResolveMcp` 或纳入 `CompanyOverviewMcp` |
| K 线行情 | `IStockDataService.GetKLineAsync`、`StockCopilotMcpService.GetKlineAsync` | 已封装为 `StockKlineMcp` | 已有且成熟 | 可直接支撑 `Market Analyst` | 保持，后续纳入新工作流工具网关 |
| 分时行情 | `IStockDataService.GetMinuteLineAsync`、`StockCopilotMcpService.GetMinuteAsync` | 已封装为 `StockMinuteMcp` | 已有且成熟 | 可直接支撑盘中结构和执行节奏分析 | 保持 |
| 技术策略 / 指标信号 | `StockCopilotMcpService.GetStrategyAsync` | 已封装为 `StockStrategyMcp` | 已有且成熟 | 可支撑 `Market Analyst` 的技术信号判断 | 保持 |
| 本地新闻 / 公告 / 市场 / 板块事实 | `QueryLocalFactDatabaseTool`、`/api/news`、`StockCopilotMcpService.GetNewsAsync` | 已封装为 `StockNewsMcp`，并有本地事实查询 | 已有且成熟 | 可支撑 `News Analyst`，也可为市场/板块上下文提供证据 | 保持，并补工作流层 level/role 映射 |
| 外部搜索 fallback | `StockCopilotMcpService.SearchAsync` | 已封装为 `StockSearchMcp`，`external_gated` | 已有但受限 | 可作为 analyst 的外部兜底 | 必须挂到显式审批/权限策略下 |
| 公司详情摘要 | `/api/stocks/detail`、`/api/stocks/detail/cache`、`GetQuoteAsync`、`GetIntradayMessagesAsync` | 普通 API / detail 聚合，不是 MCP envelope | 部分已有 | 可作为 `Company Overview Analyst` 的基础拼装来源 | 需要封装为专用 `CompanyOverviewMcp` |
| 公司基本面快照 | `/api/stocks/fundamental-snapshot`、`IStockFundamentalSnapshotService`、`EastmoneyFundamentalSnapshotService` | 普通 API，不是 MCP envelope | 已有基础能力 | 可支撑 `Fundamentals Analyst` | 需要封装为 `StockFundamentalsMcp` |
| 本地基本面事实 | `QueryLocalFactDatabaseTool.QueryAsync` 返回 `FundamentalFacts` | 本地事实数据，不是独立 MCP tool | 已有基础能力 | 可为 `Fundamentals Analyst` 和 `Company Overview Analyst` 提供 grounding | 需要并入 fundamentals/company overview MCP |
| 股东结构 / 股东户数 / 股权集中度 | `EastmoneyCompanyProfileParser`、`EastmoneyFundamentalSnapshotService`、`ShareholderResearch/PageAjax` | 解析能力存在，但未形成独立 MCP | 部分已有 | 可支撑 `Shareholder Analyst` 的首批事实 | 需要新增 `StockShareholderMcp` |
| 产品业务 / 主营结构 / 产品线 | 当前未发现稳定的专用 service / endpoint / MCP | 无 | 明显缺口 | `Product Analyst` 当前没有稳定 grounded 数据源 | 必须新增 `StockProductMcp` 或等价 adapter |
| 市场上下文 | `IStockMarketContextService`、本地 market/sector facts、`StockCopilotMcpMetaDto.marketContext` | 作为元数据或服务存在，不是独立 MCP | 部分已有 | 可为 `Market Analyst`、风险层和组合决策提供背景 | 需要决定是否单独封装 `MarketContextMcp` |
| 角色级策略分类 | `StockCopilotMcpMetaDto.PolicyClass`、`local_required` / `external_gated` | 元数据与 acceptance 规则已有 | 已有基础约束 | 很适合迁移到新 workbench 的权限模型 | 需要扩展到 15 角色和 tool group 级别 |

从当前代码可得出的直接结论：
1. 当前仓库已经有一套可复用的 MCP 形态核心：`StockKlineMcp`、`StockMinuteMcp`、`StockStrategyMcp`、`StockNewsMcp`、`StockSearchMcp`。
2. 当前仓库已经有两块非常重要但**尚未封装成 MCP** 的能力：公司详情聚合、基本面快照。
3. `Shareholder Analyst` 不是从零开始，已有东方财富股东研究解析能力，但还缺独立 MCP 封装。
4. `Product Analyst` 目前仍是实质缺口，未发现足够稳定的产品/主营业务专用 adapter。

#### P0.7 基于当前仓库代码的 15 角色缺口矩阵（初版）
本矩阵用于回答一个更直接的问题：如果今天就开始做 15 角色工作流，哪些角色已经有数据底座，哪些角色只能部分实现，哪些角色必须先改造 MCP 才能开工。

| 角色 | 当前可复用能力 | 覆盖度 | 主要缺口 | 结论 |
| --- | --- | --- | --- | --- |
| Company Overview Analyst | `/api/stocks/search`、`/api/stocks/detail`、`/api/stocks/fundamental-snapshot`、本地 `StockCompanyProfiles` | 部分覆盖 | 没有独立 `CompanyOverviewMcp`，缺标准化 `company_details` contract | 需改造后可落地 |
| Market Analyst | `StockKlineMcp`、`StockMinuteMcp`、`StockStrategyMcp`、市场上下文服务 | 高覆盖 | 缺统一 MCP Manager 下发，不是能力缺口 | 可直接复用 |
| Social Sentiment Analyst | 本地新闻、公告、外部搜索 fallback | 低到中覆盖 | 缺真实社媒/社区情绪专源，当前更多是新闻近似替代 | 需要新增社媒 MCP 或先降级实现 |
| News Analyst | `StockNewsMcp`、`QueryLocalFactDatabaseTool`、market/sector/stock level buckets | 高覆盖 | 需要工作流层 role-to-level 策略 | 可直接复用 |
| Fundamentals Analyst | 基本面快照、本地 `FundamentalFacts`、公司档案 | 中覆盖 | 没有独立 `StockFundamentalsMcp`，数据仍偏 snapshot/facts，缺更完整财报层级 | 需改造后可落地 |
| Shareholder Analyst | 东方财富股东研究解析、`ShareholderCount`、股权集中度等事实 | 中覆盖 | 没有独立 `StockShareholderMcp`，字段覆盖仍偏窄 | 需改造后可落地 |
| Product Analyst | 未发现稳定产品业务 MCP / endpoint | 低覆盖 | 缺主营业务、产品线、收入构成、业务区域等 grounding 来源 | 必须新增 MCP |
| Bull Researcher | 吃 analyst 输出即可 | 高覆盖 | 无数据 tool 缺口 | 可直接落地 |
| Bear Researcher | 吃 analyst 输出即可 | 高覆盖 | 无数据 tool 缺口 | 可直接落地 |
| Research Manager | 吃 debate 和 analyst 汇总即可 | 高覆盖 | 无数据 tool 缺口 | 可直接落地 |
| Trader | 吃 investment plan、K 线/策略结论即可 | 高覆盖 | 无直接 MCP 缺口，重点在 orchestration | 可直接落地 |
| Aggressive Risk Analyst | 吃 trader proposal 与上游证据即可 | 高覆盖 | 无直接 MCP 缺口 | 可直接落地 |
| Neutral Risk Analyst | 吃 trader proposal 与上游证据即可 | 高覆盖 | 无直接 MCP 缺口 | 可直接落地 |
| Conservative Risk Analyst | 吃 trader proposal 与上游证据即可 | 高覆盖 | 无直接 MCP 缺口 | 可直接落地 |
| Portfolio Manager | 吃 risk debate、proposal、market context 即可 | 高覆盖 | 无直接 MCP 缺口 | 可直接落地 |

按当前代码现状，角色实施优先级应调整为：
1. **可先落地组**：Market、News、Bull、Bear、Research Manager、Trader、三类 Risk、Portfolio Manager。
2. **需要先补 MCP 封装组**：Company Overview、Fundamentals、Shareholder。
3. **需要先补数据源再谈角色组**：Product。
4. **需要定义降级策略的组**：Social Sentiment。

因此，当前仓库真实的 MCP 改造 backlog 至少应包含：
1. `CompanyOverviewMcp`：统一封装搜索、详情、基本面摘要，输出标准化 `company_details`。
2. `StockFundamentalsMcp`：把 `fundamental-snapshot` 与本地 `FundamentalFacts` 合并成 analyst 可直接消费的 envelope。
3. `StockShareholderMcp`：把东方财富股东研究解析结果独立封装成 MCP 工具。
4. `StockProductMcp`：新增主营业务 / 产品线 / 收入构成 / 产业链定位 adapter。
5. `SocialSentimentMcp`：若短期无法建设真实社媒源，至少要定义“新闻近似替代 + external_gated fallback”的降级模式。

#### P1. Agent 提示词与输出语言契约 (Prompt & Language Contract)
目标：
提取 `TradingAgents-main` 源码中的真实提示词（System Prompt）模板，将其固化为开发时不可偏离的“角色设定契约”，并严格要求所有功能的最终展示和输出结果必须为中文，杜绝开发在写 Prompt 时自由发挥或引发语言与语气的偏离。

必须对齐的 TradingAgents 提示词依据：
1. `analysts/market_analyst.py`
2. `analysts/news_analyst.py`
3. `analysts/social_media_analyst.py`
4. `analysts/fundamentals_analyst.py`
5. `researchers/bull_researcher.py`
6. `researchers/bear_researcher.py`
7. `managers/research_manager.py`
8. `trader/trader.py`
9. `risk_mgmt/aggressive_debator.py`
10. `risk_mgmt/neutral_debator.py`
11. `risk_mgmt/conservative_debator.py`
12. `managers/portfolio_manager.py`

源码提示词必须先拆成两层理解，不允许只摘一句口号：
1. **Analyst 共用外层系统壳**：四个 analyst 都不是裸 Prompt，而是包在同一个协作型 system shell 里。其中文语义必须完整保留为：
  - 你是一个乐于协作的 AI 助手，正在与其他助手一起完成任务。
  - 你必须使用系统提供的工具推动问题求解。**特别注意：你被配置了本系统内置的高优本地 MCP 工具（如K线数据、本地事实库、公司财务数据等）。你必须优先调用这些本地 MCP 工具获取数据。只有当本地工具明确返回数据缺失、为空或不正确时，才允许你使用通用互联网 Web Search 工具作为兜底补充。**
  - 如果你无法独立完整回答，也没关系，其他拥有不同工具的助手会继续接手；你必须先把自己能做的部分做完。
  - 如果你或其他助手已经拿到了最终交付，尤其是 `FINAL TRANSACTION PROPOSAL: **BUY/HOLD/SELL**` 这种终局结论，必须在响应前缀中显式写出这个标记，让整个团队知道可以停止。
  - 你可以访问给定工具集合、当前日期和标的上下文，这些都必须视为 Prompt 的正式输入，而不是实现时可省略的信息。
2. **角色内层任务提示词**：每个角色还有自己真正的职责 Prompt，后续开发必须按角色逐字对齐其语义，不允许混成统一模板。
3. **Prompt 不是写完就算完成**：在进入后续实现前，必须用环境内可用的 LLM key 做真实调用验证，确认模型确实理解“优先调用本地 MCP、何时 fallback、何时停止、哪些角色无工具权限”这些 Prompt 规则；如果模型在实测中没有按 Prompt 使用 MCP，则 Prompt 契约视为未通过，必须继续修正。

以下是必须落到文档中的“全量中文翻译版角色提示词契约”：

1. `Market Analyst` 提示词全量中文化：
  - 你是一个负责分析金融市场的交易助手。
  - 你的角色是针对当前市场状态或交易策略，从给定指标列表中选择“最相关”的指标。
  - 你的目标是最多选择 8 个指标，这些指标必须彼此互补，不能冗余。
  - 你可选的类别和指标包括：
    - 移动平均类：`close_50_sma`、`close_200_sma`、`close_10_ema`。
    - MACD 类：`macd`、`macds`、`macdh`。
    - 动量类：`rsi`。
    - 波动率类：`boll`、`boll_ub`、`boll_lb`、`atr`。
    - 成交量类：`vwma`。
  - 你必须理解这些指标各自用途：例如 50 日均线用于中期趋势和动态支撑阻力，200 日均线用于长期趋势确认，10 日 EMA 用于短期动量转折；MACD 系列用于趋势变化、交叉和背离；RSI 用于超买超卖；布林带用于波动和突破判断；ATR 用于止损和仓位波动控制；VWMA 用于结合成交量确认趋势。
  - 你必须选择多样且互补的指标，避免冗余，例如不要同时选择高度重叠的指标组合。
  - 你还必须简要说明这些指标为何适用于当前市场背景。
  - 调用工具时必须使用源码里定义的精确指标参数名，否则工具会失败。
  - 你必须先调用 `get_stock_data` 获取生成指标所需的 CSV，再调用 `get_indicators` 并传入具体指标名。
  - 你必须撰写一份非常详细、足够细腻的趋势报告。
  - 你必须给出具体、可执行、带支撑证据的洞见，帮助交易者做决策。
  - 你必须在报告末尾追加一张 Markdown 表格，把关键点整理得清晰易读。

2. `News Analyst` 提示词全量中文化：
  - 你是一个新闻研究员，负责分析过去一周内的最新新闻与趋势。
  - 你必须写出一份关于当前世界状态的综合报告，这份报告必须与交易和宏观经济直接相关。
  - 你可使用的工具包括：`get_news(query, start_date, end_date)`，用于公司定向新闻或特定主题新闻搜索；`get_global_news(curr_date, look_back_days, limit)`，用于更广泛的宏观经济新闻搜索。
  - 你必须输出具体、可执行、带证据支撑的见解，帮助交易者做出更好决策。
  - 你必须在报告末尾追加一张 Markdown 表格整理关键点。

3. `Social Sentiment Analyst` 提示词全量中文化：
  - 你是一个社交媒体与公司相关新闻研究员/分析师。
  - 你的任务是分析过去一周内，围绕某家公司产生的社交媒体帖子、近期公司新闻与公众情绪。
  - 系统会给你一个公司名称，你的目标是写出一份综合性的长报告，详细说明你的分析、洞见，以及这些信息对交易者和投资者的含义。
  - 你必须观察社交媒体上人们如何谈论这家公司，分析每天围绕这家公司的情绪变化，并结合近期公司新闻形成判断。
  - 你必须使用 `get_news(query, start_date, end_date)` 搜索与公司相关的新闻和社交讨论。
  - 你必须尽量覆盖尽可能多的来源，从社交讨论到情绪到新闻都要看。
  - 你必须给出具体、可执行、带证据支撑的见解，帮助交易者做决策。
  - 你必须在报告末尾追加一张 Markdown 表格整理关键点。

4. `Fundamentals Analyst` 提示词全量中文化：
  - 你是一个负责公司基本面研究的研究员。
  - 你的任务是分析一家公司过去一周内可获得的基本面信息。
  - 你必须写出一份综合报告，覆盖财务文件、公司画像、基础财务指标与历史财务表现，以建立对公司基本面全貌的理解，并服务于交易判断。
  - 你必须尽可能写得详细。
  - 你必须给出具体、可执行、带证据支撑的见解，帮助交易者做决策。
  - 你必须在报告末尾追加一张 Markdown 表格整理关键点。
  - 你必须使用以下工具体系：`get_fundamentals` 用于综合公司分析，`get_balance_sheet`、`get_cashflow`、`get_income_statement` 用于具体财务报表。

5. `Bull Researcher` 提示词全量中文化：
  - 你是一个看多分析师，立场是为投资这只股票建立支持论证。
  - 你的任务是构建一个强有力、基于证据的看多论点，重点强调成长潜力、竞争优势和积极市场信号。
  - 你必须利用提供的研究和数据回应质疑，并有效反驳看空观点。
  - 你必须重点覆盖：成长潜力、竞争优势、正面指标、对 Bear 观点的逐点反驳、以及带有互动感的辩论式表达。
  - 你可使用的输入包括：市场研究报告、社交情绪报告、世界事务新闻、公司基本面报告、当前辩论历史、上一条 Bear 论点、以及相似场景的历史反思与经验教训。
  - 你必须利用这些信息给出有说服力的看多论点，反驳 Bear 的担忧，并展现看多立场为何更有优势。
  - 你还必须回应历史反思，并从过往错误中学习。

6. `Bear Researcher` 提示词全量中文化：
  - 你是一个看空分析师，立场是反对投资这只股票。
  - 你的目标是提出一套论证充分、逻辑完备的看空观点，重点强调风险、挑战和负面指标。
  - 你必须利用提供的研究和数据突出潜在下行因素，并有效反驳看多观点。
  - 你必须重点覆盖：风险与挑战、竞争弱点、负面指标、对 Bull 观点的逐点拆解、以及直接参与式辩论表达。
  - 你可使用的输入包括：市场研究报告、社交情绪报告、世界事务新闻、公司基本面报告、当前辩论历史、上一条 Bull 论点、以及相似场景的历史反思与经验教训。
  - 你必须利用这些信息形成强有力的看空论证，反驳 Bull 的主张，并展现投资该股票的风险和弱点。
  - 你还必须回应历史反思，并从过往错误中学习。

7. `Research Manager` 提示词全量中文化：
  - 你既是辩论主持者，也是研究阶段的裁决者。
  - 你的职责是批判性评估这一轮辩论，并作出明确决策：支持 Bear、支持 Bull，或者只有在论据极其充分时才选择 Hold。
  - 你必须简洁总结双方最关键的论点，重点聚焦最有说服力的证据与推理。
  - 你的建议必须清晰、可执行，并明确落在 Buy、Sell 或 Hold 上。
  - 你不能因为双方都有一些道理就机械地默认 Hold；你必须基于最强论据做出立场承诺。
  - 你还必须为 Trader 制定详细投资计划，至少包括：你的结论、做出结论的理由、以及落实这一结论的策略动作。
  - 你必须参考过去类似情境中的错误反思，用这些经验修正自己的决策方式。
  - 你的表达要自然、口语化，不使用特殊格式。
  - 你的正式输入包括：历史错误反思、标的上下文、以及完整辩论历史。

8. `Trader` 提示词全量中文化：
  - 你是一个根据市场数据做投资决策的交易代理。
  - 基于分析结果，你必须给出明确的买入、卖出或持有建议。
  - 你的回答结尾必须用坚定措辞收口，并且必须始终以 `FINAL TRANSACTION PROPOSAL: **BUY/HOLD/SELL**` 结束，用于确认最终交易建议。
  - 你必须吸取过去决策中的经验教训来强化本次分析。
  - 你的输入并不是开放世界，而是受 Research Manager 产出的 `investment_plan` 约束：系统会告诉你这是基于技术趋势、宏观指标、社交情绪等多方分析形成的投资计划，你必须以此为基础做下一步交易决策。
  - 如果没有历史记忆，源码里会写入 `No past memories found.`，这也必须视为正式分支处理，而不是实现时省略。

9. `Aggressive Risk Analyst` 提示词全量中文化：
  - 你是激进型风险分析师。
  - 你的角色是积极主张高收益、高风险机会，强调大胆策略与竞争优势。
  - 当你评估 Trader 的计划时，你必须聚焦潜在上行空间、成长潜力与创新收益，即使这些伴随更高风险。
  - 你必须使用市场数据和情绪分析来强化自己的主张，并挑战保守派和中性派的观点。
  - 你必须逐点回应 Conservative 与 Neutral 的论点，用数据驱动的反驳和说服性推理指出他们哪里过于谨慎，或者错过关键机会。
  - 如果其他观点暂时还没有回应，你也必须先基于已有数据独立提出自己的论证。
  - 你的表达重点是辩论和说服，而不是单纯罗列数据。
  - 你的输出必须像自然说话一样，不使用特殊格式。

10. `Neutral Risk Analyst` 提示词全量中文化：
  - 你是中性风险分析师。
  - 你的角色是提供平衡视角，同时衡量 Trader 计划的潜在收益与潜在风险。
  - 你优先考虑更圆融的方案，要综合更广泛的市场趋势、经济变化与分散化策略。
  - 你必须同时挑战 Aggressive 与 Conservative，指出他们哪里过于乐观或过于谨慎。
  - 你必须利用市场研究、情绪、世界事务、基本面等输入，为一个中庸且可持续的调整方案做论证。
  - 如果其他观点尚未出现，你也必须先基于现有数据独立完成论证。
  - 你必须通过批判双方论点，说明温和风险策略如何兼顾成长与防守。
  - 你的输出必须像自然说话一样，不使用特殊格式。

11. `Conservative Risk Analyst` 提示词全量中文化：
  - 你是保守型风险分析师。
  - 你的首要目标是保护资产、降低波动、确保稳定可靠的增长。
  - 你优先考虑稳定性、安全性和风险缓释，必须认真评估潜在亏损、经济下行和市场波动。
  - 当你评估 Trader 计划时，你必须批判其中高风险成分，指出哪些地方会让组合暴露于过度风险，以及哪些更谨慎的替代方案能保障长期收益。
  - 你必须主动反驳 Aggressive 与 Neutral 的论点，指出他们忽略了哪些威胁，或没有把可持续性放在足够高的位置。
  - 如果其他观点尚未出现，你也必须先基于已有数据独立提出自己的论证。
  - 你必须通过质疑他们的乐观预期、放大潜在下行来论证低风险路径为何更安全。
  - 你的输出必须像自然说话一样，不使用特殊格式。

12. `Portfolio Manager` 提示词全量中文化：
  - 你是组合经理，你的任务是综合风险分析师的辩论并给出最终交易决策。
  - 你必须参考标的上下文。
  - 你必须使用固定评级刻度，且只能选择其中一个：`Buy`、`Overweight`、`Hold`、`Underweight`、`Sell`。
  - 这些评级的含义必须保留：
    - `Buy`：强烈建议建仓或加仓。
    - `Overweight`：前景偏正面，建议逐步提升敞口。
    - `Hold`：维持当前仓位，无需动作。
    - `Underweight`：降低敞口，部分止盈。
    - `Sell`：退出仓位或避免进入。
  - 你的正式输入包括：Trader 提出的计划、过去决策的经验教训、以及完整的风险辩论历史。
  - 你的输出结构必须严格至少包含三部分：
    - `Rating`：明确给出五选一评级。
    - `Executive Summary`：简要动作计划，必须覆盖入场策略、仓位控制、关键风险位和时间周期。
    - `Investment Thesis`：详细论证，必须锚定在分析师辩论和历史反思上。
  - 你必须果断，并确保每个结论都建立在分析师给出的具体证据上。

详细规则与开发限制：
1. **禁止再写“省略版 Prompt”**：后续文档、任务拆分、后端实现都不得再用 `...` 省略 TradingAgents 的角色意图，否则视为未对齐源码。
2. **角色核心目标不可改**：Bull 必须是看多辩手，Bear 必须是看空辩手，Research Manager 必须是裁决者，Trader 必须给交易建议，Risk 三角必须各自保持立场差异，Portfolio Manager 必须做最终评级与拍板。
3. **共用系统壳不可删**：Analyst 角色的“协作、多助手接力、工具优先、最终前缀停止信号、当前日期与标的上下文注入”这些要求不能在本仓库实现时丢掉。
4. **强制中文输出指令必须升级为顶优先级**：在所有底层 Prompt 模板末尾，必须显式追加类似如下中文约束：`你必须使用专业、清晰、自然的中文输出全部分析、辩论、摘要、表格和最终结论；除工具参数名、指标参数名、固定协议标记外，不得输出英文大段正文。`
5. **固定协议标记必须保留原文**：`FINAL TRANSACTION PROPOSAL: **BUY/HOLD/SELL**` 属于停机协议和链路信号，不翻译、不改写、不替换。
6. **Market Analyst 的工具参数名必须保留英文原样**：例如 `close_50_sma`、`macd`、`boll_ub`、`vwma` 等，这些是代码参数，不允许中文化后再映射。
7. **输出格式严格收敛**：
  - Analyst 类输出必须体现证据、理由、结论，并带 Markdown 表格收尾。
  - Debate 类输出必须体现主张、针对对手的反驳、以及当前仍未解决的问题。
  - Research Manager 必须产出投资计划，而不是只写一段总结。
  - Trader 必须显式落到 `BUY/HOLD/SELL`。
  - Portfolio Manager 必须显式落到 `Buy/Overweight/Hold/Underweight/Sell` 五档之一。
8. **禁止客套和聊天废话**：不得出现 `Hello`、`Sure`、`Let me help` 之类客服式句式；语气应专业、直接、分析导向。
9. **必须保留上下文拼接链**：源码里多处把 `market_report`、`sentiment_report`、`news_report`、`fundamentals_report`、`history`、`current_response`、`past_memory_str` 注入后续角色 Prompt。我们的实现必须保留这条上下文继承链，不允许随意裁掉任何一环。
10. **必须把角色差异做成后端 contract，而不是前端文案皮肤**：如果 Bull、Bear、Risk 三方最终读取的是同一套简化 Prompt，仅靠 UI 换名字，这视为违背本节契约。
11. **必须把中文最终输出纳入验收**：R7 验收时需要新增一条明确检查：工作台 feed、current report、final decision、replay 视图中的正文输出必须为中文，且不能夹杂大段英文原始回答。

完成标准：
1. 后端落地代码里的 Prompt 必须能逐角色追溯到上述中文全译契约，而不是只有一句摘要。
2. 开发不得因为“实现麻烦”而把多个独立立场角色合并成一个中立 Agent。
3. 产品里的 feed、report、decision、replay 不再出现大段英文响应。
4. 任何实现 PR 都必须能回答“这个角色的 Prompt 对应本节哪一条”，否则视为未完成 Prompt 对齐。

#### R1. 真实多轮 Session Contract
目标：
把“多轮获取信息”落成真实后端 contract，使 follow-up 真正承接同一 session 的研究状态，而不是前端视觉假连续。

必须对齐的 TradingAgents 依据：
1. `agents/utils/agent_states.py` 的状态载体思想。
2. `graph/setup.py` 的阶段顺序。
3. `graph/conditional_logic.py` 的 loop 决策逻辑。

详细步骤：
1. 设计 `ResearchSession` 聚合，至少包含：sessionId、symbol、name、status、currentStage、currentTurnIndex、createdAt、updatedAt、latestDecisionSnapshot、degradedFlags。
2. 设计 `ResearchTurn` 聚合，区分首轮研究和 follow-up 研究，不允许只用一张 message 表糊过去。
3. 给 turn 增加 `continuationMode`，至少区分：继续当前研究、补充证据、重跑研究辩论、重跑风险评审、同 session 全量重跑、显式新建 session。
4. 给 turn 增加 `userPrompt`、`reuseScope`、`rerunScope`、`changeSummary`，确保前端能把 turn 显示成一段明确的追问轮次，而不是一条孤立消息。
5. 设计 `StageSnapshot`，记录每轮 turn 开始前和结束后的阶段状态，保证回放时能看见每轮变更了什么。
6. 设计 `RoleState` 快照，记录每个角色在当前 session 下的最新状态、最近一次输出、最近一次更新时间。
7. 设计 `CurrentReportSnapshot`，保证每轮 follow-up 都能在旧 report 基础上增量更新，而不是覆盖成不可追溯的新文案。
8. 设计 `DecisionSnapshot`，保证 portfolio manager 的每次结论都可追溯、可比较。
9. 明确 session 级锁，防止同一股票同一 session 被并发 follow-up 冲坏状态。
10. 明确“活跃 session 选择规则”：同一股票默认只有一个 active session，但可以回放旧 session。
11. 明确“显式新建会话”规则：只有用户明确点新建，或者显式声明重新开始，才允许创建新 session。
12. 明确“续接当前会话”规则：只要用户未显式重开，所有 follow-up 都必须落到当前 active session。
13. 明确“局部补信息”规则：如果用户只要求补新闻、补公告、补风险讨论，不应强制全量重跑 analyst team。
14. 明确“同 session 内全量重跑”规则：允许用户在保留 session 连续性的同时发起一次全流程重跑，并将其作为新 turn 记录，而不是新 session。
15. 设计 session 读取接口：获取 active session 摘要、获取指定 session 详情、获取指定 session 的 replay 数据。
16. 设计 turn 提交接口：提交新问题时，必须包含 sessionId 或明确声明新建。
17. 设计 session 列表接口：按 symbol 读取历史 session，默认返回最后更新时间、当前阶段、最终结论快照。
18. 设计错误和降级传播规则：工具失败、外部数据缺失、角色未完成时，状态必须从 tool -> role -> stage -> session 向上可见。
19. 设计数据库落地方式，确保 session / turn / stage / role message / report snapshot 可以分层持久化，而不是塞进单个大 JSON。
20. 明确与旧 GOAL-AGENT-002 数据隔离，不复用旧聊天 session 表做伪兼容。
21. 为 R2-R7 预留稳定 ID 体系，确保 tool event、evidence、report block、decision block 都能跨 turn 引用。
22. 明确 turn 与 feed 的映射关系：一个 turn 可以在 UI 上表现为一段群聊式消息线程，但后台仍然必须能精确追溯到其 stage、role、tool 和 decision 变化。

本阶段交付物：
1. Session/Turn/Stage/Decision contract 文档。
2. API contract 文档。
3. 数据库对象草图。
4. continuationMode 规则表。

本阶段不允许偏离：
1. 不允许把 follow-up 简化成“把历史消息拼进 prompt”。
2. 不允许每次追问都新开 session 再伪装成继续当前会话。
3. 不允许只持久化 final answer 而不持久化研究过程状态。

#### R2. MCP Adapter Matrix 与缺口补齐 (本地 MCP 优先策略)
目标：
把 TradingAgents 中各角色的数据边界严格映射到本仓库已有的 grounded 能力（现有 MCP 及本地数据方法）。**所有 Agent 都被允许联网搜索，但必须遵循“本地 MCP 优先”原则**：强制要求它们在取证时首选本系统的专用 MCP 接口，只有当我们的本地数据缺失或明确不正确时，才允许调用全网搜索引擎兜底。

必须对齐的 TradingAgents 依据：
1. `tradingagents/graph/trading_graph.py`
2. `agents/utils/core_stock_tools.py`
3. `agents/utils/technical_indicators_tools.py`
4. `agents/utils/news_data_tools.py`
5. `agents/utils/fundamental_data_tools.py`

详细步骤：
1. 先按 MCPmode 的思路把 analyst 能力拆成 7 类：Company Overview、Market、Social Media、News、Fundamentals、Shareholder、Product。
2. 为 `Company Overview Analyst` 建能力矩阵：必须具备标准化识别标的、交易所、市场、行业、主营概览、别名映射的能力，并产出统一 `company_details`。如果现有 MCP 无法稳定输出该对象，必须先改造它，再开始主流程开发。
3. 为 `Market Analyst` 建能力矩阵并**强绑定本地行情系统**：强制首选本系统现有的 K线 MCP、分时接口、预运算技术指标。仅当本地数据缺失或报错时才允许使用 Web Search 兜底。
4. 为 `Social Sentiment Analyst` 建能力矩阵：首选本系统内可能存在的社交/情绪接口。如果当前缺乏真实且完备的内部社媒源，允许其通过全网搜索“感受情绪”，但必须首先确认内部数据确不可用。
5. 为 `News Analyst` 建能力矩阵并**强绑定本地事实库**：必须首选系统现有的 `QueryLocalFactDatabaseTool`、新闻源 MCP 和东财公告解析，仅在取不到相关近期新闻时允许联网搜索补充。
6. 为 `Fundamentals Analyst` 建能力矩阵：强制首选系统现有的公司数据 MCP、财报和估值接口。当且仅当未收录标的公司或具体科目缺失时，允许使用网络搜索挖掘财报信息。
7. 为 `Shareholder Analyst` 建能力矩阵：应优先调用股东结构、机构持仓、十大股东、股权变化类 MCP；如果本仓库不存在这类能力，则必须新增对应 MCP / adapter，而不是让该角色退化成无数据空角色。
8. 为 `Product Analyst` 建能力矩阵：应优先调用公司主营业务、产品线、收入构成、业务区域、产业链定位类 MCP；如果本仓库不存在这类能力，则必须新增对应 MCP / adapter。
9. 为 `Bull Researcher` 和 `Bear Researcher` 明确输入：它们的论据只来自 Analyst outputs、上一轮对手论点、历史记忆。**在系统调度层彻底剥夺这两个角色的 Tool Call 权限**，不允许它们自己再去绕开分析师查底层数据。
10. 为 `Research Manager` 明确输入来自 bull/bear 的最终辩论结果和 analyst 汇总，同样**不提供额外数据查询工具**。
11. 为 `Trader` 明确输入来自 research manager 的投资计划和 buy/sell evidence，不直接替代前序研究。
12. 为三类 `Risk Analyst` 明确输入来自 trader proposal、前序研究摘要和约束条件，不能直接改写 trader 原始结论而不留下痕迹。
13. 为 `Portfolio Manager` 明确输入来自 risk team 的讨论收敛结果和 trader proposal，不直接跳过风险层。
14. 对每个角色标注工具调用策略：`local_required`、`local_preferred`、`external_gated`、`disabled_in_v1`。**特别地，需要支持基于配置（如环境变量或前端传参）控制具体角色的 MCP 权限开关（如 `MARKET_ANALYST_MCP=true`, `BEAR_RESEARCHER_MCP=false`），默认设定下除了 Analyst 外其他综合角色必须关掉直接调 MCP 的权限**。
15. MCP 权限开关不能只是布尔值，还应支持按 MCP server / tool group 细分授权，例如：行情类、公司信息类、新闻类、财务类、股东类、产品类。
16. 对每个工具定义标准 envelope：requestSummary、resultSummary、evidenceRefs、warnings、degradedFlags、traceId、latency、sourceTier。
17. 对 evidence 定义统一最小字段：title、source、publishedAt、url、excerpt、readMode、readStatus、localFactId、symbolScope、level。
18. 对 freshness 定义硬规则：市场/新闻/公告/板块上下文各自的可接受时间窗，超过时要明确标记 degraded。
19. 对缺失能力给出明确补齐路线，例如：本仓库若缺 Social Media 真数据，就先定义 adapter 占位和 `disabled_in_v1` 行为，而不是让 LLM 装作已有结论。
20. 把 role -> tool -> evidence -> report section 的映射写成矩阵表，作为后续测试基线。
21. **定义防互联网过度依赖策略 (MCP-First Policy)**：为每个 adapter 规定失败后的回退次序：**必须首选本地事实库 (LocalFactDatabase) 或指定的系统 MCP -> 只有当返回无数据或报错时，才允许使用通用 Web Search 联网检索兜底**。
22. 规定除了指定的 Analyst 外，其他后置节点（Researcher、Manager、Trader、Risk）绝对没有使用查询类工具的权限，防止数据链路短路。
23. 规定 tool result 如何进入 `Messages & Tools` feed，以及如何反向写入 `Current Report`。
24. 写清楚每个角色在 V1 里“有能力输出什么、无权输出什么”，防止角色越权。
25. 增加一张 `现有 MCP 改造任务表`，明确哪些能力是“直接复用”、哪些是“扩展字段”、哪些是“新增 MCP / adapter”、哪些是“允许临时 fallback 到 Web Search”。
26. 在进入后续工作流开发前，必须先完成一轮真实 MCP 取数验证 + Prompt 检查 + 环境内 LLM key 实测，确认模型会按 Prompt 优先调用本地 MCP，而不是绕过系统能力直接联网或乱用工具。
27. 明确验收标准：只有当角色职责对应的 MCP 能力已存在并可被 runner 稳定调用时，才允许该角色进入开发完成态；否则该角色只能标记为 blocked，不允许假装支持。

本阶段交付物：
1. 角色能力矩阵。
2. Adapter 缺口与本地工具映射清单。
3. Tool envelope 规范。
4. Evidence 最小字段规范。
5. Freshness / degradation 规则表。
6. 现有 MCP 改造任务表。

本阶段不允许偏离：
1. 不允许所有角色共享一个大而全的工具池。
2. 不允许给非信息采集节点（Researcher、Trader、Manager 等）配置任何外部搜索或数据抓取工具！
3. 不允许 Agent 在未尝试调用本地 MCP 或本地数据的情况下，直接绕过系统能力强行走通用 Web Search 获取数据。Web Search 只能作为 fallback。
4. 不允许在文档里引入 `Shareholder Analyst`、`Product Analyst`、`Company Overview Analyst`，却不落实其对应的 MCP 改造责任。

#### R3. 后端多角色 Graph Orchestration
目标：
严格按 TradingAgents 的执行顺序，落地真实 staged runtime，让“相互讨论、相互制衡”在后端成为真实控制流，而不是前端视觉效果。

必须对齐的 TradingAgents 依据：
1. `graph/setup.py`
2. `graph/conditional_logic.py`
3. `graph/propagation.py`
4. `agents/researchers/bull_researcher.py`
5. `agents/researchers/bear_researcher.py`
6. `agents/managers/research_manager.py`
7. `agents/trader/trader.py`
8. `agents/risk_mgmt/aggressive_debator.py`
9. `agents/risk_mgmt/conservative_debator.py`
10. `agents/risk_mgmt/neutral_debator.py`
11. `agents/managers/portfolio_manager.py`

详细步骤：
1. 先实现 session runner 骨架，能够按固定阶段驱动 role 执行，而不是先写一个大 prompt orchestrator。
2. **新增第 0 阶段预处理 (Phase 0)**：在执行并行 Analyst 之前，必须先由 `Company Overview Analyst` (第 0 阶段) 抓取并生成 `company_details` 占位符。这保证所有后续并行 Analyst 的上下文拥有统一、准确的公司基础画像，避免各自发散盲搜。
3. 实现 `Analyst Team` 子流程 (Phase 1) 扩容：扩充至 6 个 Analyst（Market, Sentiment, News, Fundamentals, Shareholder, Product），它们必须接收第 0 阶段的 `company_details` 作为基础输入，在同一 stage 内完美并行执行。
4. runner 不得直接把所有已发现工具无差别塞给角色；必须通过 `MCP Manager / Tool Gateway` 按角色、按权限、按 tool group 精确下发工具集合。
5. 每个 analyst 执行后立即写出 role message、tool events、report block，并更新 progress 状态。
6. analyst team 完成后，进入 `Research Debate`，先由 bull researcher 基于 analyst outputs 给出 bullish case。
7. bear researcher 必须能读取 analyst outputs 和 bull case，形成 bearish rebuttal，而不是平行写另一段不相干内容。
8. 按 `conditional_logic.py` 的思路实现 bull/bear 来回辩论轮次控制，**提取辩论轮次为前端/执行层的可配参数 (例如 `debateRoundCount`)**，让深浅模式可控。
9. 每一轮 debate 必须产出结构化对象：claim、supportingEvidence、counterEvidence、targetOfRebuttal、unresolvedQuestions。
10. debate 达到停止条件后，再由 `Research Manager` 读取双方最终材料，形成投资计划，而不是在 bull/bear 首轮结束后直接跳 trader。
11. `Research Manager` 输出必须包含：主结论、采纳的牛方要点、采纳的熊方要点、未解决分歧、进入 trader 的执行前提。
12. `Trader` 只能消费投资计划和 buy/sell evidence，形成交易提案，不得越级重做 analyst 或 research manager 的工作。
13. trader proposal 必须结构化输出：方向、仓位建议、触发条件、失效条件、关注风险、执行节奏。
14. trader 完成后进入 `Risk Debate`，三类 risk analyst 必须先基于同一版 trader proposal 并行生成首轮观点，再进入可控轮次的相互补充或反驳。
15. aggressive risk 侧重高收益容忍和进攻性仓位；conservative risk 侧重回撤控制和防守；neutral risk 负责在两者之间给出平衡方案。
16. risk loop 不能只让三方各说一次，必须允许后一位角色引用前一位角色的观点进行补充或反驳。同理投资辩论，**提取风险辩论轮次为可配参数 (`riskDebateRoundCount`)**。
17. risk loop 达到停止条件后，再由 `Portfolio Manager` (或统一术语的 `Risk Manager`) 统一读取 trader proposal 与 risk discussion，给出最终治理决策。
18. portfolio manager 输出必须显式包含：是否批准、批准范围、反对原因、折中方案、后续跟踪条件。
19. 全流程中，每个 role 执行前后都要更新 `RoleState` 与 `StageState`，保证 UI 侧能实时呈现进度。
20. 全流程中，每个 role 输出都要绑定 evidenceRefs 和 toolCallRefs，保证 report 与 feed 可追踪。
21. runner 必须统一处理 MCP 失败、fallback、降级、重试和超时，禁止每个 role 私下各写一套工具容错逻辑。
22. 对 `local_required` MCP 增加 fail-fast：一旦连接失败、鉴权失败或超过重试上限，runner 必须立即终止当前 turn，不得再推进到后续 stage，更不允许用旧缓存结果假装本轮成功。
23. 任何一个 role 失败或降级时，必须显式进入 stage summary 和 session degradedFlags，而不是静默吞掉。
24. 全流程必须支持 turn 级继续执行：当 follow-up 只要求补某一段时，runner 要能从正确 stage 接着跑，而不是必然回到 analyst 起点。
25. 对每个阶段定义持久化边界：阶段开始、阶段内 role 完成、阶段结束、session 完成，都要可恢复。
26. 对每个阶段定义回放材料，确保 replay 不依赖重新调用 LLM 就能重现进度和 report 演化。
27. 在 runner 内定义统一的微流式事件总线，把 `role started`、`tool dispatched`、`tool progress`、`tool completed`、`summary ready` 实时写入 feed，不能等整段 role 输出完成后再补记日志。
28. 为并行 stage 定义完成栅栏：只有 stage 内必需角色全部返回或被明确降级，才允许推进到下一 stage，防止部分结果提前穿透到 manager 决策。

本阶段交付物：
1. 多阶段 runner。
2. bull/bear debate loop。
3. risk debate loop。
4. manager 治理出口。
5. 可持久化的 role/stage/report/event 数据。

本阶段不允许偏离：
1. 不允许把 debate loop 砍成“一人一段”。
2. 不允许 trader 跳过 research manager。
3. 不允许 manager 变成简单摘要器。

#### R4. Trading Workbench UI
目标：
让前端严格围绕 TradingAgents 的工作台叙事来设计，不回到聊天气泡主视图。

必须对齐的 TradingAgents 依据：
1. `cli/main.py` 的版面结构。
2. 附件中的 CLI 截图和流程示意图。

详细步骤：
1. 在股票页右侧扩展区建立模块壳体，不侵入左侧终端和现有图表主视区。
2. 顶部固定 session header，显示 symbol、sessionId、当前 stage、整体状态、最近更新时间。
3. 顶部动作区必须至少有：继续当前会话、新建会话、查看回放、起草交易计划，并显式显示当前 turn 编号。
4. 左侧实现 `Team Progress` 面板，按五个 stage 分组显示角色状态，而不是仅列一个扁平角色表。
5. `Team Progress` 每个角色至少显示：角色名、所属 team、当前状态、最近更新时间、简短职责；并在并行阶段显示执行批次或 active group。
6. 中间实现 `Discussion Feed`，采用群聊式讨论线程外观，但底层仍按时间混排 role message、tool start、tool result、degraded event、stage transition、user follow-up。
7. Feed 必须按 turn 分段；每个 turn 顶部显示用户问题、continuationMode、复用范围、重跑范围、变化摘要。
8. 时间线每个节点都要支持最小态摘要和展开态详情，避免默认塞满长文本；工具型节点要支持运行中状态文案与轻量 loading 动效。
9. 中央实现 `Current Report` 主舞台，必须始终显示当前 stage 对应的最新报告块。
10. `Current Report` 需要支持 report section 切换或折叠，但默认聚焦当前最重要的 section。
11. report block 至少显示：headline、summary、key points、evidence、disagreements、risk limits、invalidations、next actions。
12. 在 report 舞台上显式展示“当前正在被哪一方观点推动”或“当前主要分歧是什么”，让 debate 真可见。
13. 底部实现聊天式 follow-up composer，必须明确提示“默认延续当前 session”，并给出 continuationMode 与复用/重跑预览。
14. follow-up composer 旁边要给出局部 rerun 入口，例如：只补新闻、只重跑风险评估、全量重跑但保留同 session。
15. 完成态要有 final decision block，但不能盖掉 progress、feed、report 三块，只能作为 report 的最终收口。
16. 回放态要能切到历史 session，并重新展示其 progress、feed、report、decision，而不是只给一条摘要。
17. 移动端不能简单把桌面端缩窄，必须改成 `Progress | Chat Feed | Report | Follow-up` 四个 tab。
18. 空态要明确提示这不是聊天助手，而是多角色研究工作台。
19. 运行中状态必须有清晰的 stage/role 高亮，不允许用户看不出来当前卡在谁。
20. 错误态和降级态必须保留对 feed 与 report 的可见影响，不能只弹 toast。
21. 如果本轮请求依赖的关键 MCP 服务不可用，前端必须弹出阻断式 modal，对用户明确说明：当前请求已停止、失败的 MCP 名称、建议操作；不能静默停在 loading，也不能只在时间线里塞一条不显眼的错误消息。
22. 所有动作按钮都必须从当前 session 读取上下文，不允许跳到与当前 decision 不一致的旧数据。
23. 当某个 stage 内存在并行角色时，UI 必须能同时显示多个 running 状态和各自的最近微流式事件，而不是把并行执行压扁成一个笼统的“处理中”。
24. manager 角色给用户的最终答复可以表现为群聊中的一条收口消息，但它只能引用 authoritative report/decision，不能独立脱离工作台状态存在。

本阶段交付物：
1. 工作台布局。
2. progress 面板。
3. feed 面板。
4. current report 主舞台。
5. follow-up 输入区。
6. replay 视图。

本阶段不允许偏离：
1. 不允许把主舞台做回聊天气泡流。
2. 不允许只显示 final answer，不显示过程。
3. 不允许 UI 上看不出 bull/bear/risk/manager 的治理关系。

#### R5. Debate / Risk / Approval Persistence
目标：
把“讨论”和“制衡”持久化为真实对象，使后续多轮追问、回放和比较都能基于真实历史，而不是重算 prompt。

必须对齐的 TradingAgents 依据：
1. `researchers/bull_researcher.py`
2. `researchers/bear_researcher.py`
3. `managers/research_manager.py`
4. `risk_mgmt/aggressive_debator.py`
5. `risk_mgmt/conservative_debator.py`
6. `risk_mgmt/neutral_debator.py`
7. `managers/portfolio_manager.py`

详细步骤：
1. 为 bull/bear debate 建独立持久化对象，不允许只把它们压进一段 report 文本里。
2. 每条 debate message 至少记录：speakerRole、roundIndex、claim、supportingEvidenceRefs、counterTargetRole、counterPoints、openQuestions。
3. research manager 的收敛结果必须单独建对象，记录它采纳了哪些 bull/bear 要点，以及拒绝了哪些论点。
4. trader proposal 必须版本化持久化，允许后续 follow-up 在同一 session 内产生 proposal v2、v3，而不是覆盖旧提案。
5. aggressive / conservative / neutral 三类 risk 输出必须分别存储，并能标识它们引用的是哪一版 trader proposal。
6. risk discussion 也必须记录 roundIndex、targetOfResponse、核心约束点，保证“相互制衡”可回放。
7. portfolio manager 的最终批准结果必须单独持久化为 decision snapshot，而不是从 report 文本里临时解析。
8. 每个 role 输出都必须绑定 stageId、turnId、sessionId，避免跨轮追问时串数据。
9. 每个 debate / risk / decision 对象都必须能追溯到其 evidenceRefs 和 toolCallRefs。
10. 为 replay 预留比较字段，支持后续查看“第 1 轮 vs 第 3 轮”哪些分歧已经收敛、哪些风险仍未解决。
11. 为 follow-up 预留引用入口，使用户追问时可以针对上一轮某个 bull 点、某个 bear 点、某个风险意见继续发问。
12. 为 UI 提供结构化查询接口，避免前端再从大段 report 文本里做脆弱解析。
13. 对 role memory 的接入也要只读这些结构化对象，不直接读原始 prompt 拼接残留。
14. 为删除/归档/重跑策略设边界：新 turn 只能追加新对象或标记 superseded，不直接物理覆盖旧历史。
15. 定义“authoritative object”规则：最终交易计划草稿只能来自明确的 manager/trader/risk 对象链，不允许从任意文字块兜底拼装。
16. 为群聊式 feed 预留 messageThread 映射：同一条可见聊天消息必须能追溯到 role output、tool refs、turnId 和 stageId，防止前端展示层与真实状态脱钩。

本阶段交付物：
1. debate 持久化模型。
2. risk 持久化模型。
3. manager decision 持久化模型。
4. proposal versioning 方案。

本阶段不允许偏离：
1. 不允许把 discussion 压成一段 markdown 然后声称已支持回放。
2. 不允许 follow-up 时覆盖旧 proposal 和旧 decision。
3. 不允许前端靠字符串解析恢复 debate 结构。

#### R6. Grounded Report 与动作交接
目标：
把 TradingAgents 的 `Current Report` 能力做成结构化、可动作化、可测试的工作台主输出，而不是只保留长文案摘要。

必须对齐的 TradingAgents 依据：
1. `cli/main.py` 中 `current_report` 的持续更新方式。
2. 各角色源码中的输出逻辑顺序。

详细步骤：
1. 先定义 report block 模型，按 stage 切成：Market、Social、News、Fundamentals、Research Debate、Trader Proposal、Risk Review、Portfolio Decision 八类块。
2. 每个 report block 至少包含：headline、summary、keyPoints、evidenceRefs、counterEvidenceRefs、disagreements、riskLimits、invalidations、recommendedActions。
3. 明确 `Current Report` 的更新策略：不是每来一条消息就重写整份报告，而是按 stage 和 role 对应更新所属 block；微流式事件只更新运行态提示，不得污染 authoritative report 内容。
4. analyst 阶段输出要更新前四个 analysis block，不得直接写入 trader 或 decision block。
5. bull/bear debate 更新 `Research Debate` block，必须突出当前主分歧、核心支撑证据、仍未解决的问题。
6. research manager 更新 debate 收敛结果和投资计划，不直接覆盖 analyst 原始分析块。
7. trader 更新 `Trader Proposal` block，明确方向、执行条件、仓位、节奏、失效条件。
8. risk team 更新 `Risk Review` block，必须同时展示激进、中性、保守三个视角的收敛结果。
9. portfolio manager 更新 `Portfolio Decision` block，成为 final decision 的 authoritative 来源。
10. 为 final decision 定义统一 schema：direction、rating、confidence、thesis、supportingEvidence、counterEvidence、riskLimits、invalidations、nextActions。
11. 为 nextActions 定义标准动作类型：查看日 K、查看分时、查看证据、查看本地事实、起草交易计划、回到某一争议点继续追问。
12. 为每个 nextAction 定义参数契约，确保按钮点击后能确定性跳到正确的图表/抽屉/计划入口。
13. 定义 `manager reply` 规范：可在群聊式 feed 中生成一条面向用户的最终回复，但该回复只是 `Portfolio Decision` 或 `Research Manager` 结论的用户可读投影，不得取代 authoritative schema。
14. 对 degraded 和 failure 状态定义 report 表现方式：哪些 block 标记为不完整、哪些结论降级为观察态、哪些动作被禁用。
15. 定义 evidence 在 report 中的展示层级：摘要优先、按需展开全文，避免把 report 弄成原始抓取堆叠。
16. 让 report 既能服务当前运行，也能作为 replay 的主阅读入口，不依赖临时前端拼接。
17. 为交易计划 handoff 规定来源链路：只能从 authoritative final decision + trader proposal + risk limits 生成，不可跳过风控。
18. 为图表和证据动作 handoff 规定来源链路：必须带上当前 sessionId / turnId / reportBlockId，保证从哪里来、跳到哪里去都是可追的。
19. 为后续测试定义 report 断言基线：给定一轮 session，应能断言哪些 block 已完成、哪些 block 含分歧、哪些动作可点击。

本阶段交付物：
1. report block schema。
2. final decision schema。
3. nextAction schema。
4. 交易计划 handoff 规则。

本阶段不允许偏离：
1. 不允许只给一段最终总结就算 `Current Report`。
2. 不允许交易计划直接绕过 risk / manager 输出。
3. 不允许 report 与 evidence / tool event 脱节。

#### R7. Replay / Browser / Desktop 验收
目标：
用真实链路证明这不是文档上的多角色，而是一个能持续多轮取证、真实辩论、真实制衡、可回放、可动作交接的产品闭环。

必须对齐的 TradingAgents 依据：
1. CLI 的运行态感知方式。
2. staged graph 的完整执行链路。

详细步骤：
1. 先准备标准验收脚本，覆盖首轮研究、follow-up 补信息、局部 rerun、全量 rerun、历史 replay、交易计划 handoff 六类核心路径。
2. 验证首轮研究路径：从股票搜索进入，创建 active session，按 analyst -> debate -> trader -> risk -> manager 顺序完成完整一轮。
3. 验证工作台运行态：进度面板、feed、current report 都要随着阶段推进实时变化，而不是最后一次性刷出全部内容。
4. 验证群聊式追问呈现：Feed 必须能按 turn 分段展示用户问题、角色回复、工具进度和 manager 收口消息，但 turn/stage/report 边界不能丢失。
5. 验证 bull/bear debate：前端必须能看到双方观点往返和收敛，而不是只看到最终 manager 结论。
6. 验证 risk debate：前端必须能看到 aggressive / neutral / conservative 三方制衡结果和最终收敛。
7. 验证 follow-up 补信息路径：在同一 session 下请求补新闻或补市场上下文，系统不能新开 session，且旧 debate / report / decision 不得丢失。
8. 验证局部 rerun 路径：只重跑 risk review 时，analyst 和 research debate 结果应被复用，不得整条链路强制重跑。
9. 验证同 session 全量重跑路径：允许生成新 turn，但 sessionId 不变，旧 turn 仍可回放。
10. 验证 replay：从历史 session 列表选择旧 session 后，应能重建其 progress、feed、report、decision，而不是只显示最终摘要。
11. 验证比较：至少要能看出某次 follow-up 前后，decision / risk limit / disagreement 是否发生了变化。
12. 验证动作交接：从 final decision 起草交易计划时，必须携带当前 session 的 authoritative decision 数据。
13. 验证图表联动：从 report 或 nextAction 点击“看日 K / 看分时”时，页面跳转必须与当前 session 结论一致。
14. 验证证据抽屉：点击 evidence 后，能看到当前结论所依赖的实际证据，而不是泛化来源列表。
15. 先跑相关单元测试和定向集成测试，再做 Browser MCP 验收，顺序不能反。
16. Browser MCP 验收必须在 backend-served 页面完成，至少覆盖：创建 session、观察 stage 推进、执行一次 follow-up、打开 replay、起草交易计划。
17. 验证浏览器控制台和后端日志无新增 runtime error。
18. 如果后续实现影响桌面宿主启动、打包、路径或入口，再补 packaged desktop 验证，确认工作台在打包版里同样可用。
19. 在业务流验收前，先执行一轮 `MCP readiness` 验收：逐个验证关键 MCP 能否真实拿到数据、Prompt 是否已写入 MCP 使用方式、以及基于环境内 LLM key 的实测是否证明模型会按约束优先使用 MCP。
20. 验证 degraded 场景：当某类 adapter 缺失或外部源失败时，系统仍能以明确降级方式完成研究，而不是悄悄给出看似完整的结论。
21. 验证 hard-fail 场景：当 `local_required` MCP 服务不可用时，请求必须被立即中止，前端必须弹出阻断式提示弹框，且不得进入后续 stage。
22. 验证连续多轮后 session 没有状态污染，例如上一轮 bull 观点不会在无引用的情况下神秘覆盖下一轮 bear 观点。
23. 验证数据一致性：feed、report、decision、plan handoff 引用的 sessionId / turnId / reportBlockId 必须一致。
24. 验证并行 stage：`Analyst Team` 与 `Risk Debate` 必须能同时看到多个角色进入 running，且 stage 总耗时应明显小于对应角色串行执行的时间总和。
25. 验证微流式反馈：在长耗时工具调用期间，feed 必须出现实时进度节点或状态文案，而不是在数十秒后一次性补齐结果。
26. 验证 manager 收口消息：群聊中的最终结论消息必须与 `Current Report` / `Final Decision` 一致，不能出现展示层与 authoritative 结构化结果不一致的情况。

本阶段交付物：
1. 单元测试和集成测试清单。
2. Browser MCP 验收脚本。
3. 真实运行链路验收报告。

本阶段不允许偏离：
1. 不允许只用静态页面或假数据截图证明完成。
2. 不允许只测首轮，不测 follow-up 和 replay。
3. 不允许只验证最终答案，不验证 debate、risk、manager 的治理链路。

### 绝不能再犯的错误
1. 用静态卡片顺序伪装多角色。
2. follow-up 悄悄新开 session。
3. 运行中只显示最终答案，不显示 current report。
4. 暴露 raw chain-of-thought。
5. 证据和工具事件脱节。
6. 角色可以绕过 grounded adapter 自由发挥。
7. 重新回到旧的 one-to-one assistant 产品叙事。
8. 把群聊式表现层误当成真实执行模型，导致 turn、stage、report、decision 边界消失。

### 验收清单
1. 模块表现为分阶段多角色 workbench。
2. follow-up 默认继续当前 session。
3. team / role / stage 推进是实时产品状态。
4. 工具调用 grounded 且可审计。
5. current report 在运行过程中持续演进，并作为主阅读区。
6. final decision 能接入现有图表、证据、市场上下文和交易计划链路。

### 本轮工件变更
1. 把本规划报告重写为详细可执行版本。
2. 新增一份 TradingAgents 源码分析报告，便于后续实现时持续对照。

### 本轮校验
1. 工件差异自检：
  - 命令：`git diff --check`
  - 结果：已执行，通过；终端里仅有仓库内既有文件的 CRLF/LF 警告，无新增格式错误。
2. 代码/运行态测试：
  - 结果：本轮仅规划，不适用。
3. Browser MCP：
  - 结果：本轮仅规划，不适用。

### 下个对话应该先确认什么
下个对话不要立刻开写代码，先确认：
1. 上面这版 V1 功能范围是否接受。
2. 模块名和 UI 布局是否接受。
3. 哪些角色或 pane 还要删减。