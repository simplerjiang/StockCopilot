# GOAL-AGENT-002-R5 Planning Report (2026-03-24)

## EN

### Scope

- Re-open GOAL-AGENT-002 after a product-level review of the current Stock Copilot experience.
- Replace the current card-heavy right-side panel direction with a true conversational Copilot redesign.
- Produce a concrete UI specification in Markdown so the next implementation round has an explicit target instead of incremental UI drift.

### Why Re-Open The Goal

- The current implementation proved that the MCP runtime, planner/governor draft contract, tool execution, replay baseline, and acceptance metrics can all be wired together.
- It did not produce the product shape the user actually wants.
- The current UI feels like a debug dashboard rather than a Copilot conversation surface.
- Users do not reliably receive a clear final answer after asking a question.
- Important content is split across too many parallel cards: timeline, tool grid, evidence panel, acceptance metrics, replay metrics, and actions.

### Product Decision

Stock Copilot should be redesigned as a chat-first workbench.

The default UI should behave like this:

1. user asks a question,
2. Copilot shows a short structured reasoning summary,
3. Copilot calls MCP tools and shows the call trace inline,
4. Copilot shows compact evidence cards,
5. Copilot outputs a grounded final answer,
6. Copilot offers the next actions under that answer.

This is materially different from the current approach, where the user is asked to interpret multiple side-by-side cards before getting a result.

### New Slice

`GOAL-AGENT-002-R5` = Conversational Copilot redesign and grounded answer loop.

### Planned Outputs

1. Chat-style Stock Copilot UI.
2. Inline MCP trace records.
3. Expandable evidence cards with short summary + full content reveal.
4. A required grounded final answer for each completed turn.
5. Context-preserving multi-turn conversation behavior.
6. Acceptance/replay metrics moved out of the main surface into a collapsible detail layer.

### Non-Goals

1. Do not expose raw chain-of-thought.
2. Do not keep the current metrics wall and card grid as the main layout.
3. Do not add more raw tools before the conversation loop is coherent.

### Artifacts Updated

- `.automation/tasks.json`
- `.automation/state.json`
- `README.md`
- `docs/stock-copilot-chat-redesign-20260324.md`

## ZH

### 范围

- 在新的产品复核后，重新打开 GOAL-AGENT-002。
- 把当前“右侧堆卡片”的 Stock Copilot 方向，改成真正的对话式 Copilot 重构方案。
- 产出一份明确的 Markdown 页面设计文档，避免下一轮继续在错误形态上做局部修补。

### 为什么要重开

- 当前实现已经证明 MCP runtime、planner/governor 草案 contract、工具执行、replay baseline、acceptance metrics 可以串起来。
- 但它没有形成用户真正要的产品形态。
- 现在的 UI 更像调试面板，不像 Copilot 对话框。
- 用户提问后不能稳定得到清晰的最终回答。
- 重要内容被 timeline、tool grid、evidence、acceptance、replay、actions 多块并列区域拆散，阅读负担过高。

### 产品决策

Stock Copilot 下一轮必须改成“聊天优先”的工作台。

默认流程应当变成：

1. 用户提问，
2. Copilot 显示简短结构化思维摘要，
3. Copilot 内嵌展示 MCP 调用记录，
4. Copilot 展示简写证据卡，
5. Copilot 输出 grounded final answer，
6. Copilot 在回答下提供下一步动作。

这和当前“用户自己阅读多块并列卡片后再拼结果”的模式是根本不同的。

### 新切片

`GOAL-AGENT-002-R5` = 对话式 Copilot 重构与 grounded answer 闭环。

### 计划产物

1. 聊天式 Stock Copilot UI。
2. 内嵌 MCP 调用轨迹。
3. 简写摘要 + 全文展开的证据卡。
4. 每轮完成时必须产出的 grounded final answer。
5. 支持上下文连续的多轮会话。
6. acceptance/replay 指标下沉到可折叠详情层，不再霸占主界面。

### 非目标

1. 不暴露原始 chain-of-thought。
2. 不保留当前指标墙和卡片墙作为主布局。
3. 在会话闭环未成型前，不继续扩张更多裸工具。

### 本轮同步的文件

- `.automation/tasks.json`
- `.automation/state.json`
- `README.md`
- `docs/stock-copilot-chat-redesign-20260324.md`