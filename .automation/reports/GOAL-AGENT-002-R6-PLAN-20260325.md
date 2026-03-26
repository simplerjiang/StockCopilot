# GOAL-AGENT-002-R6 Plan (2026-03-25)

## EN

### Scope

- This is a review-driven hardening slice after the R5 close-out.
- The main user-facing Copilot loop already works, but the formal review found two concrete residual issues and one regression gap.
- R6 does not redesign the product again. It fixes contract alignment, layout correctness, and missing coverage around the real auto-loop path.

### Review Findings Converted Into Scope

1. Main evidence cards and session continuity cards currently aggregate evidence from `toolPayloads` only.
2. Real backend auto-loop turns primarily return `ToolResults` and summary/count metadata, so the UI can show a false-empty evidence state even when tools already executed successfully.
3. The secondary detail template currently nests the history-turn block inside the `Replay 基线` grid, which is a DOM/layout correctness issue rather than just a readability concern.
4. Current front-end regression cases cover manual tool execution payload rendering, but do not lock the real path where draft turns arrive with `ToolResults` and without `toolPayloads`.

### Execution Plan

1. Contract alignment
   - Decide the minimal display contract for auto-loop evidence on draft turns.
   - Prefer fixing the root cause: either enrich the turn payload with display-ready evidence summaries, or explicitly adapt the front-end evidence layer to consume a backend-supported evidence structure instead of depending on `toolPayloads`.
   - Ensure both the active turn evidence cards and the `会话上下文承接` / historical-turn summaries use the same source of truth.

2. Template structure repair
   - Move the historical-turn secondary card out of the `Replay 基线` grid block.
   - Keep `Replay 基线` as one card and `历史 turn 列表` as a sibling card inside the secondary-details container.
   - Recheck responsive layout and section semantics after the move.

3. Regression coverage
   - Add a targeted front-end regression where `/api/stocks/copilot/turns/draft` returns completed `ToolResults` but no `toolPayloads`.
   - Lock the expected user-visible behavior: the main evidence area should still show evidence summaries, and historical-turn evidence counts should not collapse to zero incorrectly.
   - Add a regression that guards the repaired secondary-details DOM structure if practical.

4. Validation
   - Run the relevant front-end targeted unit tests first.
   - Re-run the nearest browser/runtime validation on the backend-served page if UI behavior changes.
   - Sync automation report and task/state artifacts after development.

### Acceptance Criteria

- A successful auto-loop draft turn can populate user-visible evidence summaries without requiring a later manual tool click.
- Historical turn continuity cards no longer undercount evidence because `toolPayloads` are absent.
- `Replay 基线` and `历史 turn 列表` render as separate secondary cards with stable layout semantics.
- Regression coverage explicitly protects the no-`toolPayloads` auto-loop path.

### Planning Validation

- No runtime code changed in this planning round.
- The required follow-up for this round is artifact consistency only: task queue, state pointer, and roadmap/report text must all point at `GOAL-AGENT-002-R6`.

## ZH

### 范围

- 这是 R5 收口之后的一轮 review-driven hardening 切片。
- 当前用户可见的 Copilot 主闭环已经能跑通，但 formal review 明确找到了 2 个残余问题和 1 个回归缺口。
- R6 不是再做一轮产品重设计，而是把真实 auto-loop 路径上的数据契约、布局结构和测试保护补完整。

### 本轮 review 结论转任务

1. 主舞台的 `证据摘要卡` 和会话连续性卡片当前只从 `toolPayloads` 聚合 evidence。
2. 真实后端 auto-loop turn 主要返回的是 `ToolResults` 和计数/摘要元数据，因此当前 UI 会出现“工具已经执行成功，但证据区仍然为空”的假阴性。
3. 次级详情模板里，`历史 turn 列表` 被错误插进了 `Replay 基线` 的 grid 内部，这不是单纯的代码可读性问题，而是实际 DOM/布局结构错误。
4. 现有前端回归只锁住了手动执行工具后的 payload 展示，没有锁住“draft 首屏只有 `ToolResults`、没有 `toolPayloads`”这条真实主路径。

### 执行计划

1. 证据契约打通
   - 先明确 auto-loop turn 的最小可展示证据契约。
   - 优先修根因：要么后端 turn payload 补 display-ready evidence summary，要么前端证据层改为消费后端正式支持的 evidence 结构，而不是继续硬依赖 `toolPayloads`。
   - 当前轮 `证据摘要卡` 和 `会话上下文承接` / 历史 turn 摘要必须统一使用同一套真实数据源。

2. 模板结构修复
   - 把 `历史 turn 列表` secondary card 从 `Replay 基线` grid 块中移出去。
   - 保持 `Replay 基线` 和 `历史 turn 列表` 作为详情区里的并列卡片，而不是嵌套关系。
   - 修完后重新确认响应式布局和区块语义。

3. 回归覆盖补强
   - 新增一条前端定向回归：`/api/stocks/copilot/turns/draft` 直接返回已完成 `ToolResults`，但不带 `toolPayloads`。
   - 锁定用户可见行为：主证据区仍然要显示 evidence summary，历史 turn evidence 计数也不能被错误打成 0。
   - 如果实现成本合适，再补一条详情区 DOM 结构保护回归。

4. 验证顺序
   - 先跑相关前端定向单测。
   - 如果 UI 行为发生变化，再补做 backend-served 页面上的 browser/runtime 验证。
   - 开发完成后同步更新 automation report 与 task/state。

### 验收标准

- 成功 auto-loop 的 draft turn 不需要再手动点工具，也能在主舞台显示用户可读证据摘要。
- 历史 turn 承接卡不会再因为缺少 `toolPayloads` 而把 evidence 计数错误归零。
- `Replay 基线` 与 `历史 turn 列表` 会作为两个独立 secondary card 稳定渲染。
- 回归测试明确覆盖无 `toolPayloads` 的 auto-loop 主路径。

### 规划轮校验说明

- 本轮只是规划落盘，没有修改运行时代码。
- 因此这一轮需要完成的校验是工件一致性：任务队列、state 指针、README/报告文本都必须已经指向 `GOAL-AGENT-002-R6`。