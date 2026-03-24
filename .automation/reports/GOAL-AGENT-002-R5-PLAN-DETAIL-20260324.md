# GOAL-AGENT-002-R5 Detailed Task Breakdown (2026-03-24)

## EN

### Scope

- This file is a development-order breakdown for R5 only.
- It intentionally does not start implementation.
- The goal is to prevent the conversational Copilot redesign from losing critical backend/runtime work while focusing on UI shape.

### Execution Order

1. R5-A: Controlled backend loop and grounded answer state machine.
2. R5-B: Trading-plan gating hardening and commander completeness checks.
3. R5-C: Evidence summary cleanup and expandable evidence contract.
4. R5-D: Chat-first main surface redesign.
5. R5-E: Detail drawer and metric demotion.
6. R5-F: Session continuity and multi-turn follow-ups.
7. R5-G: Unit tests, Browser MCP validation, and report closure.

### R5-A Controlled Backend Loop

- Goal: replace the current draft-only Copilot turn flow with a bounded tool-calling loop.
- Inputs to define:
  - round budget,
  - total tool-call budget,
  - external-search budget,
  - total time budget,
  - state-polling budget.
- Output requirements:
  - turn status must move through `drafting`, `calling_tools_round_n`, `finalizing_answer`, `done`, `done_with_gaps`, or `failed`.
  - `needs_tool_execution` must no longer remain as a long-lived user-visible end state.
  - each completed turn must include a grounded final answer or an explicit gap explanation.
- Critical constraint:
  - the user's requested `4000` maximum polling count should be modeled as a state/progress budget only, not as 4000 real tool executions.

### R5-B Trading Plan Gating Hardening

- Goal: fix the current false-ready path for `draft_trading_plan`.
- Required changes:
  - front-end gating must require grounded final answer status,
  - front-end gating must require complete commander-backed analysis history,
  - back-end must reject incomplete history payloads more explicitly,
  - partial agent results must not be persisted as if they were valid commander-complete history.
- Bug coverage:
  - closes Bug 9 root cause rather than only hiding the button.

### R5-C Evidence Cleanup

- Goal: make Copilot evidence readable enough for direct user consumption.
- Required changes:
  - prefer structured summary/excerpt over raw scraped text,
  - filter navigation/header/footer pollution,
  - preserve readMode/readStatus/url/title/source/publishedAt,
  - keep full text available behind expand/collapse.
- Bug coverage:
  - closes Bug 10 root cause.

### R5-D Chat-First Main Surface

- Goal: turn the current right-side card wall into a coherent chat workbench.
- Required visible sections in each turn group:
  - user question,
  - Copilot state header,
  - structured reasoning summary,
  - compact MCP call timeline,
  - evidence summary cards,
  - final answer block,
  - next-step actions.
- Non-goals:
  - do not expose raw chain-of-thought,
  - do not keep timeline/tool/evidence/metrics as separate primary walls.

### R5-E Detail Drawer And Metrics Demotion

- Goal: keep acceptance/replay/audit value without letting them dominate the default UI.
- Required detail content:
  - acceptance baseline,
  - replay baseline,
  - raw tool payloads,
  - traceId,
  - warning/degraded flags,
  - historical turns.

### R5-F Session Continuity

- Goal: make the product behave like a real Copilot conversation instead of isolated one-off drafts.
- Required behavior:
  - follow-up questions reuse prior turn context,
  - prior evidence and conclusions remain referable,
  - actions remain attached to the answer they come from,
  - switching replay/history must not break the current conversational model.

### R5-G Validation And Closure

- Required tests before development is considered complete:
  - backend targeted tests for state progression, gating, and evidence cleanup,
  - frontend targeted tests for chat rendering and action enablement,
  - Browser MCP validation on backend-served runtime,
  - bilingual dev report update,
  - automation task/state sync.

### Acceptance Checklist

- A user can ask a question and reliably receive a final answer in the same turn.
- The UI no longer looks like a debug dashboard by default.
- `draft_trading_plan` is unavailable until final answer and commander completeness are both satisfied.
- Evidence cards are readable without opening raw payloads.
- Acceptance/replay still exist, but live in a secondary detail layer.

## ZH

### 范围

- 本文件只用于 R5 的开发顺序拆分。
- 当前只做规划落盘，不进入实现。
- 目的就是防止这次“聊天式 Copilot 重构”只改了前端外观，却漏掉真正决定体验的后端闭环、gating 和证据质量。

### 执行顺序

1. R5-A：后端受控 loop 与 grounded final answer 状态机。
2. R5-B：交易计划 gating 加固与 commander 完整性校验。
3. R5-C：Evidence 摘要清洗与可展开证据模型。
4. R5-D：聊天优先主视图重排。
5. R5-E：详情抽屉与质量指标下沉。
6. R5-F：连续会话与多轮 follow-up 承接。
7. R5-G：单测、Browser MCP 验证与自动化收口。

### R5-A 后端受控 Loop

- 目标：把当前只会生成草案的 Copilot turn，升级为有边界的工具调用 loop。
- 需要明确的预算：
  - 内部轮次上限，
  - 工具调用总上限，
  - 外部搜索上限，
  - 单轮总耗时预算，
  - 状态推进轮询预算。
- 输出要求：
  - turn 状态必须能进入 `drafting`、`calling_tools_round_n`、`finalizing_answer`、`done`、`done_with_gaps` 或 `failed`；
  - `needs_tool_execution` 不能再作为长期停留的用户可见终态；
  - 每一轮结束都必须有 grounded final answer，或者明确的缺口说明。
- 关键约束：
  - 用户提到的 `4000` 最大轮询数，只能建模为状态推进预算，不能真的做成 4000 次工具执行。

### R5-B 交易计划 Gating 加固

- 目标：修掉 `draft_trading_plan` 的假成功链路。
- 必做项：
  - 前端 gating 必须依赖 grounded final answer；
  - 前端 gating 必须依赖 commander 完整 history；
  - 后端对不完整 history 的拒绝要更明确；
  - 不能再把只有部分 agentResults 的结果保存成看似有效的 history。
- 覆盖问题：
  - 直接闭合 Bug 9 的根因，而不是只把按钮藏起来。

### R5-C Evidence 摘要清洗

- 目标：让 Copilot 的证据卡能直接给用户阅读，而不是展示抓取噪音。
- 必做项：
  - 优先用结构化 `summary/excerpt`，而不是原始抓取文本；
  - 过滤导航、页头页脚和站点残留；
  - 保留 `readMode/readStatus/url/title/source/publishedAt`；
  - 全文保留在展开层，不丢审计能力。
- 覆盖问题：
  - 直接闭合 Bug 10 的根因。

### R5-D 聊天优先主视图

- 目标：把现在的右侧卡片墙改成真正的聊天工作台。
- 每个 turn group 默认可见内容：
  - 用户问题，
  - Copilot 状态头，
  - 结构化思维摘要，
  - MCP 紧凑调用轨迹，
  - 证据摘要卡，
  - 最终回答块，
  - 下一步动作。
- 非目标：
  - 不暴露原始 chain-of-thought；
  - 不保留 timeline/tool/evidence/metrics 四处平铺的主界面。

### R5-E 详情抽屉与指标下沉

- 目标：保留 acceptance/replay/audit 价值，但不再霸占主界面。
- 详情层内容：
  - acceptance baseline，
  - replay baseline，
  - 原始 tool payload，
  - traceId，
  - warning/degraded flags，
  - 历史 turn 列表。

### R5-F 连续会话承接

- 目标：让 Stock Copilot 像真正的 Copilot 对话，而不是一次性草案生成器。
- 必做行为：
  - 后续追问可以沿用前一轮上下文；
  - 前一轮证据与结论仍可被引用；
  - 动作继续挂在对应答案下面；
  - replay/history 切换不能打断当前对话模型。

### R5-G 验证与收口

- 开发完成前必须具备：
  - 后端定向单测，锁定状态推进、gating 与 evidence 清洗；
  - 前端定向单测，锁定聊天流渲染与动作可用性；
  - Browser MCP 在 backend-served runtime 的真实链路验收；
  - 双语开发报告更新；
  - automation task/state 同步。

### 验收清单

- 用户提问后，同一轮里能够稳定得到最终回答。
- 默认 UI 不再像调试面板。
- `draft_trading_plan` 在 final answer grounded 且 commander 完整前不能启用。
- 证据卡默认就是可读的，不需要用户先打开原始 payload。
- acceptance/replay 仍然保留，但已经退到次级详情层。

### 用户特别提示
- 如果遇到不确定的需求方向，可以直接参考"vscode-copilot-chat-main"这个文件夹的代码，我们的要求就是参照Github-copilot-chat的设计来实现的。