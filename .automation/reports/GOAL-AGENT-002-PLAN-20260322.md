# GOAL-AGENT-002 Planning Report (2026-03-22)

## EN
### Summary
Planned the next Stock Copilot phase after GOAL-AGENT-001 R1-R4: move from tool availability to a real Copilot-style product layer.

GOAL-AGENT-001 already finished the hard backend foundations:
1. traceable evidence objects,
2. narrower agent roles and commander hardening,
3. replay calibration baseline,
4. domain MCP runtime for K-line, minute, strategy, news, and gated search.

What is still missing is the user-facing and runtime-orchestration layer that makes those capabilities behave like a bounded copilot instead of a prompt-heavy report generator.

### Why This Slice Exists
R4 solved "what tools exist". It did not solve:
1. how a session is represented,
2. how planner/governor/commander cooperate inside one user turn,
3. how tool calls become visible and auditable in the UI,
4. how final answers become action-oriented instead of essay-like,
5. how runtime instability and thought-leak regressions are blocked before the product layer is exposed.

This new parent goal therefore focuses on the Stock Copilot product layer, not on adding more raw model output.

### Parent Goal
`GOAL-AGENT-002` = Stock Copilot sessionized orchestration and product surface.

### Delivery Breakdown
#### P0. Runtime stability and output safety gate
Purpose:
Stabilize the existing runtime before exposing a richer Copilot session UX.

Scope:
1. close high-severity runtime blockers from `.automation/buglist.md`,
2. ensure key market/chart endpoints remain healthy in Browser MCP validation,
3. stop raw reasoning / non-JSON thought leaks from reaching user-facing outputs,
4. tighten developer-mode audit rendering so raw model traces are not shown as primary UI content.

Primary acceptance:
1. `情绪轮动` no longer fails on the sectors API path,
2. stock chart views actually use the lightweight chart endpoint again,
3. recommendation and developer-mode surfaces do not expose uncontrolled reasoning text,
4. backend remains stable across query + chart interactions.

#### R1. Session contract and planner/governor timeline
Purpose:
Define one Stock Copilot conversation turn as a structured runtime flow.

Main outputs:
1. `StockCopilotSessionDto`
2. `StockCopilotTurnDto`
3. `StockCopilotPlanStepDto`
4. `StockCopilotToolCallDto`
5. `StockCopilotToolResultDto`
6. `StockCopilotFinalAnswerDto`
7. `StockCopilotFollowUpActionDto`

Key rules:
1. planner proposes steps,
2. governor approves bounded tool use,
3. commander can only conclude from returned tool results and evidence,
4. degraded flags systematically lower confidence and action strength,
5. no new facts may appear in final output unless they came from tool results or saved evidence.

#### R2. Stock Copilot panel UX
Purpose:
Turn the right-side stock panel from static analysis cards into a session-based Copilot surface.

Main outputs:
1. question input,
2. visible plan/timeline,
3. tool-call cards,
4. evidence/source panel,
5. follow-up action chips,
6. session replay of the latest turn.

Key rule:
The first user-visible form must feel like "ask -> inspect plan -> inspect tools -> get bounded answer", not "click button and read a long report".

#### R3. Action-oriented workflow integration
Purpose:
Convert Copilot outputs into bounded next actions across chart, market context, news, and trading-plan workflows.

Main outputs:
1. action cards such as "看 60 日 K 线结构" / "检查今日分时承接" / "查看主线板块共振" / "起草交易计划",
2. shared answer contract for judgment, triggers, invalidations, risk limits, and next-step suggestions,
3. linkages from Copilot answers to chart overlays, news evidence, market context, and plan draft flows.

Key rule:
Copilot should suggest actionable next moves, not just summarize.

#### R4. Copilot acceptance and replay metrics
Purpose:
Evaluate the new Copilot layer with auditable product metrics, not only backend correctness.

Main outputs:
1. tool-call efficiency metrics,
2. evidence coverage metrics,
3. local-first hit rate,
4. external-search trigger rate,
5. final-answer traceability metrics,
6. action-card usage and quality metrics,
7. replayable session acceptance baseline.

Key rule:
Later prompt/runtime changes must be answerable with measurable regressions or improvements.

### Relationship With GOAL-017
GOAL-017 remains valid, but it is the quant foundation track, not the immediate product-layer track.

Recommended priority:
1. GOAL-AGENT-002-P0
2. GOAL-AGENT-002-R1
3. GOAL-AGENT-002-R2
4. GOAL-AGENT-002-R3
5. GOAL-AGENT-002-R4
6. GOAL-017-R1 and later quant slices in parallel once the session contract is stable

This ordering prevents the team from adding more tool/math depth before the Copilot session runtime and UX are coherent.

### Architectural Constraints
1. CN-A facts remain Local-First.
2. Deterministic chart/strategy math stays in code, not in free-form LLM reasoning.
3. No user-facing chain-of-thought exposure.
4. Final answers must stay grounded in tool results, evidence, and degraded-path policy.
5. Product UX should expose plan and tool usage without exposing raw internal reasoning.

### Validation For This Planning Round
1. Add GOAL-AGENT-002 and its slices to `.automation/tasks.json`.
2. Update `.automation/state.json` so the active planning run points to GOAL-AGENT-002.
3. Update `.automation/chatgpt_directives.md` so GOAL-AGENT-001 is archived and GOAL-AGENT-002 becomes the active product-layer taskbook.
4. Update `README.llm.md` so the roadmap reflects completed GOAL-AGENT-001 and the new GOAL-AGENT-002 track.
5. Validate changed JSON files and check diagnostics on touched markdown files.

## ZH
### 摘要
本轮把 R4 之后的下一阶段正式规划成 `GOAL-AGENT-002`：不再继续补“还有哪些工具”，而是开始补“这些工具如何被组织成真正像 Copilot 的股票协驾产品层”。

`GOAL-AGENT-001` 已经完成四块后端底座：
1. 可追溯 evidence object，
2. 子 Agent 职责收口与 commander 硬化，
3. replay 校准基线，
4. 股票 K 线 / 分时 / 策略 / 新闻 / 搜索 MCP 运行时。

现在缺的不是更多提示词，而是会话化 runtime、planner/governor 协作、工具调用可视化、动作化输出，以及上线前的稳定性与 thought-leak 闸门。

### 为什么现在要补这层
R4 解决的是“有哪些工具”，没有解决：
1. 一轮 Copilot 会话如何建模，
2. planner / governor / commander 如何在一个 turn 内协作，
3. 工具调用如何在 UI 中可见且可审计，
4. 最终回答如何从“长文章”变成“可执行下一步”，
5. 当前高优先级稳定性与原始推理泄漏问题如何先收口。

因此新父目标的重点是“股票 Copilot 产品层”，不是再堆一轮研报输出。

### 父任务定义
`GOAL-AGENT-002` = 股票 Copilot 会话化编排与产品层。

### 切片拆分
#### P0 运行态稳定性与输出安全闸门
目标：
在开放更强的 Copilot 会话交互前，先把现有运行态和输出安全收口。

范围：
1. 收掉 `.automation/buglist.md` 中直接阻塞 Copilot 的高优先级问题，
2. 保证关键市场/图表接口在 Browser MCP 下可稳定工作，
3. 阻断 raw reasoning / 非 JSON thought leak 落到面向用户的结果里，
4. 收紧 developer mode 的审计展示，避免原始模型脏输出直接成为主 UI 文案。

首要验收：
1. `情绪轮动` 不再因 sectors API 路径而失败，
2. 股票图表重新真正走轻量 `/api/stocks/chart` 链路，
3. 股票推荐与开发者模式不再暴露无控制推理文本，
4. 查股与图表交互后后端保持稳定。

#### R1 会话 Contract 与 planner/governor 时间线
目标：
把一次股票 Copilot 对话 turn 固定为结构化 runtime 流程。

主要输出：
1. `StockCopilotSessionDto`
2. `StockCopilotTurnDto`
3. `StockCopilotPlanStepDto`
4. `StockCopilotToolCallDto`
5. `StockCopilotToolResultDto`
6. `StockCopilotFinalAnswerDto`
7. `StockCopilotFollowUpActionDto`

关键规则：
1. planner 只负责提出步骤，
2. governor 负责审批边界明确的工具调用，
3. commander 只能基于已返回的 tool result 和 evidence 下结论，
4. degraded flags 必须系统性压低置信度与动作强度，
5. final output 不允许凭空新增未出现在 tool result 中的事实。

#### R2 股票 Copilot 面板 UX
目标：
把右侧股票侧栏从静态分析卡片升级成真正会话化的 Copilot 面板。

主要输出：
1. 问题输入框，
2. 可见计划/时间线，
3. 工具调用卡片，
4. 证据与来源面板，
5. 下一步动作 chips，
6. 最近一轮会话回放。

关键要求：
首个用户可见形态必须是“提问 -> 看计划 -> 看工具 -> 看受控回答”，而不是“点按钮读长报告”。

#### R3 动作化工作流集成
目标：
把 Copilot 输出接到图表、市场上下文、新闻证据和交易计划等可执行工作流上。

主要输出：
1. `看 60 日 K 线结构`、`检查今日分时承接`、`查看主线板块共振`、`起草交易计划` 等动作卡，
2. judgment / triggers / invalidations / risk limits / next-step suggestion 统一 answer contract，
3. Copilot 回答与图表叠加、新闻证据、市场环境、计划草稿之间的联动。

关键要求：
Copilot 要给出“下一步怎么做”，而不是只做总结。

#### R4 Copilot 验收与 replay 指标
目标：
把新的 Copilot 层纳入可审计产品指标，而不只验证后端接口对不对。

主要输出：
1. 工具调用效率指标，
2. evidence 覆盖率，
3. local-first 命中率，
4. 外部搜索触发率，
5. final answer traceability 指标，
6. action card 使用质量指标，
7. 可回放的 session 验收基线。

关键要求：
后续任何 prompt/runtime 变更都要能回答“变好了还是变差了”。

### 与 GOAL-017 的关系
`GOAL-017` 仍然成立，但它是量化底座线，不是当前最先收口的产品层主线。

推荐优先级：
1. `GOAL-AGENT-002-P0`
2. `GOAL-AGENT-002-R1`
3. `GOAL-AGENT-002-R2`
4. `GOAL-AGENT-002-R3`
5. `GOAL-AGENT-002-R4`
6. `GOAL-017-R1` 及后续量化切片在会话 contract 稳定后并行推进

这样可以避免在 Copilot 的 session runtime 与 UX 还没成型时，就先把工具/指标深度继续做得越来越重。

### 持续生效的架构约束
1. CN-A 事实继续坚持 Local-First。
2. 图表/策略确定性计算继续放在代码里，不交给 LLM 自由推导。
3. 不允许在用户可见界面暴露 chain-of-thought。
4. 最终回答必须锚定 tool result、evidence 与 degraded-path policy。
5. 产品层可以展示 plan 和 tool usage，但不能把原始内部推理直接展示给用户。

### 本轮规划校验
1. 在 `.automation/tasks.json` 新增 `GOAL-AGENT-002` 与子切片。
2. 更新 `.automation/state.json`，把当前 planning run 指向 `GOAL-AGENT-002`。
3. 更新 `.automation/chatgpt_directives.md`，把 `GOAL-AGENT-001` 切到归档态，并将 `GOAL-AGENT-002` 设为当前产品层任务书。
4. 更新 `README.llm.md`，让路线图反映“GOAL-AGENT-001 已完成 + GOAL-AGENT-002 已规划”。
5. 对修改过的 JSON 做有效性校验，并检查 Markdown 文件诊断。