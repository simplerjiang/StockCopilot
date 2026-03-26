# GOAL-AGENT-NEW-001-R4 Trading Workbench 前端工作台

## 任务目标
1. 以 TradingAgents 的工作台叙事而不是聊天窗来呈现多角色研究过程。
2. 在股票详情页右侧落地 progress、feed、current report、follow-up、replay。
3. 让 turn/stage/role/report/decision 都成为用户可见状态。

## 上游依赖
1. [R1](./GOAL-AGENT-NEW-001-R1.md)
2. [R3](./GOAL-AGENT-NEW-001-R3.md)
3. [R5](./GOAL-AGENT-NEW-001-R5.md)
4. [R6](./GOAL-AGENT-NEW-001-R6.md)

## 下游影响
1. 为 R7 提供 Browser MCP 验收面。

## 与最终 PLAN 对应关系
1. 对应总 PLAN 中 `R4. Trading Workbench UI` 与 UI 线框图、桌面/移动端布局规则、微流式反馈、阻断式 modal、四 tab 移动端骨架。
2. 本任务必须落实 `工作台骨架 + 受控多 Agent 讨论线程 + Current Report/Final Decision 收口`，不得退回聊天主舞台。
3. 本任务必须让用户可见 `turn`、`stage`、`role`、`continuationMode`、`reuse/rerun`、`replay`、`fail-fast` 状态。

## 核心工作项
1. 顶部 session header：symbol、sessionId、turn、stage、status、最近更新时间。
2. `Team Progress` 面板：按 stage 分组显示角色状态和并行执行情况。
3. `Discussion Feed`：按 turn 分段，混排 role message、tool event、degraded event、stage transition、user follow-up。
4. `Current Report`：始终作为主阅读区，支持重点块切换与分歧可视化。
5. Follow-up composer：明确“默认延续当前 session”，支持 continuationMode 和 rerun/reuse 预览。
6. Replay 视图：历史 session 切换、进度回放、决策对比。
7. 错误态与 fail-fast modal：关键 MCP 不可用时必须阻断而不是静默 loading。

## 详细开发拆解

### 一、先定工作台骨架，再填内容
1. 先在 `StockInfoTab` 右侧扩展位落一个稳定的 `TradingWorkbench` 容器组件，不要把所有新逻辑继续堆回原有大组件。
2. 主骨架必须长期固定为：顶部 session header、左侧 progress、中部 feed、主舞台 report、底部或侧边 follow-up、可切换 replay。
3. `Current Report` 必须占据主阅读区，而不是作为 feed 里的某一条消息或右下角摘要卡。
4. 即使移动端切换成 tabs，也必须保持 `Progress | Feed | Report | Follow-up` 四个信息面都可达，不能因为屏幕小就把 report 或 progress 省掉。

### 二、组件拆解建议
1. `TradingWorkbenchHeader`：展示 symbol、sessionId、turn、active stage、status、updatedAt、degraded badge、主要操作入口。
2. `TradingWorkbenchProgressPanel`：按阶段分组展示 role 状态、并行执行状态、复用标记、blocked/degraded 原因。
3. `TradingWorkbenchFeed`：按 turn 分段渲染 feed items，支持角色消息、工具事件、阶段切换、系统解释、用户 follow-up。
4. `TradingWorkbenchReportStage`：展示 authoritative report blocks、重点块切换、证据与分歧抽屉、nextAction。
5. `TradingWorkbenchFollowUpComposer`：显示默认 continuation 提示、reuse/rerun 预览、发送按钮、新建会话按钮。
6. `TradingWorkbenchReplayPanel`：提供 session 列表、turn 切换、阶段回放和决策对比。
7. `TradingWorkbenchFailFastModal`：当核心 MCP 不可用或 session blocked 时弹出阻断说明和恢复建议。

### 三、状态来源拆解
1. 前端不允许自己推导阶段顺序，必须直接消费 R1/R3/R5/R6 输出的 session detail authoritative 对象。
2. header 所有字段都来自 session + active turn + active stage，禁止另设一份本地“当前进度”镜像状态。
3. progress panel 应消费 `StageSnapshot` 和 `RoleState`，按 stage/role 真状态渲染，而不是用 feed 文本倒推角色状态。
4. feed 应直接消费统一 feed items，禁止在组件内通过 report 或 role outputs 重新拼聊天记录。
5. report panel 只读 `CurrentReportSnapshot` 和 `DecisionSnapshot`，不能让前端自己做 summary 合成。

### 四、交互流拆解
1. 首轮研究时，用户在 follow-up composer 或初始入口发起请求，前端创建或读取 active session，然后进入实时更新态。
2. follow-up 默认续接当前 session，composer 必须在发送前可见地展示 `continuationMode` 与 `reuse/rerun` 预览，而不是提交后才知道发生了什么。
3. `新建会话` 是显式动作，点击后需要清楚提示“将不复用当前 session 状态”。
4. replay 切换时，页面应只切换观察对象，不应误触发新一轮执行。
5. nextAction 点击后必须带上 sessionId/turnId/reportBlockId 或等价上下文，直接联动到图表、证据、本地事实或交易计划入口。

### 五、可视化约束拆解
1. 并行 stage 要有明确的并行感，例如同一 stage 内多个 role 同时 `running`，而不是只显示一个总 loading。
2. degraded 与 blocked 必须视觉区分，前者表示带风险继续执行，后者表示流程中止等待处理。
3. 当前 active stage、active role、last completed step 要一眼可见，防止用户只看到很多消息却不知道现在卡在哪里。
4. `Current Report` 中要能清楚展示分歧、风险限制、反证与 nextAction，不允许只显示一段总结话术。

### 六、错误态与恢复态拆解
1. 关键 MCP 失败时必须弹 fail-fast modal，同时在 progress/feed/report 中同步显示 blocked 原因。
2. 若 session 处于 degraded 但未阻断，UI 也必须在 header 和 report 中同时标识影响范围。
3. replay 历史 session 若数据不完整，要显示“历史对象缺失”而不是静默空白。
4. follow-up 发送失败时要保留用户输入和本轮 scope 选择，不能让用户重新填写。

### 七、桌面端工作台分区下一层拆解
1. 顶部 header 必须固定显示 symbol、sessionId、turn、active stage、整体状态、最近更新时间和主要动作按钮。
2. 左侧 `Team Progress` 必须按五大阶段分组展示，不允许只渲染一个扁平角色列表。
3. 中部 `Discussion Feed` 必须按 turn 分段，每个 turn 顶部至少显示：用户问题、continuationMode、复用范围、重跑范围、变化摘要。
4. 主舞台 `Current Report` 必须始终可见，且当前激活 block 与当前 active stage 有明确视觉关联。
5. `Follow-up` 区必须明确写出“默认延续当前 session”，并提供 `仅刷新新闻分析`、`仅重新跑风险评估`、`同 session 全量重跑` 等入口或等价交互。

### 八、移动端与响应式拆解
1. 移动端必须改为 `Progress | Chat Feed | Report | Follow-up` 四 tab，而不是简单把桌面布局压缩成窄列。
2. 默认聚焦 `Report` tab，但用户切到其他 tab 后不能丢失当前运行态信息。
3. 关键状态如 blocked/degraded、active stage、sessionId、turnIndex 在移动端也必须可见，不允许被隐藏到不可达二级页面。
4. replay 入口和新建会话入口在移动端同样必须保留，不能因为空间不足而删除。

### 九、空态 / 首屏 / 历史态拆解
1. 空态必须明确告诉用户这不是普通聊天助手，而是多角色研究工作台。
2. 首次进入某股票详情页时，若无 active session，应显示可发起研究的工作台空态，而不是默认聊天消息历史空白。
3. 历史 replay 态必须能重建 progress/feed/report/decision，而不是只展示 session 摘要卡。
4. replay 切换必须与“当前 active session 运行态”视觉上区分，避免用户误把历史回放当成当前执行。

### 十、manager 收口消息与 authoritative state 关系
1. 群聊式 feed 中允许存在 manager 的用户可读收口消息，但该消息只能投影 authoritative report/decision，不能自己成为权威来源。
2. 若 feed 收口消息与 `Current Report` 或 `Final Decision` 冲突，必须以前者结构化对象为准，并在 UI 上避免双源展示。

## 建议实施顺序
1. 先做组件骨架和状态接口定义，用静态 mock 数据验证布局，不急着接真实运行态。
2. 再接 session detail、feed、report、progress 真实数据源，先完成只读观察能力。
3. 然后接 follow-up composer 与 replay 切换，完成交互闭环。
4. 最后补 fail-fast modal、nextAction handoff、响应式细节和 Browser MCP 验收。

## 交付物
1. Trading Workbench 布局：至少包含桌面端骨架、移动端四 tab 骨架和状态来源说明。
2. `Team Progress` 面板：至少包含阶段分组、角色状态、并行批次、reused 标记、blocked/degraded 原因展示规则。
3. `Discussion Feed` 面板：至少包含 turn 分段、feed item 类型映射、展开/折叠规则、微流式事件展示规则。
4. `Current Report` 主舞台：至少包含 block 切换、evidence/disagreements/risk limits/invalidations/next actions 展示约束。
5. `Follow-up` 输入区：至少包含默认 continuation 提示、scope 预览、局部 rerun 入口、错误恢复规则。
6. `Replay` 视图：至少包含 session 列表、turn 切换、decision 对比、历史数据不完整时的降级表现。
7. `Fail-fast` 阻断体验：至少包含 modal 行为、错误说明、建议操作和与 progress/feed/report 的联动规则。

## 测试目标
1. 组件单测：progress、feed、report、follow-up、replay、fail-fast modal 的状态映射与显示逻辑。
2. 交互测试：继续当前会话、新建会话、局部 rerun、切换 replay、展开 turn 详情、查看 nextAction、错误后重试。
3. Browser MCP：真实点击关键动作，检查阶段推进、并行 running、report 更新、follow-up、replay、nextAction、console 和 network。
4. 响应式测试：桌面端和移动端 pane/tab 行为一致，信息不丢失，blocked/degraded 仍可见。
5. 一致性测试：header/progress/feed/report/decision 引用的 sessionId/turnId/stageId 一致，收口消息不与 authoritative state 冲突。

## 回归测试要求
1. 不得退回聊天气泡主舞台。
2. 不得只显示 final answer，隐藏中间过程。
3. 不得把并行 stage 压扁成单一 loading。
4. 不得出现 authoritative report/decision 与群聊收口消息不一致。
5. 不得丢失 `continuationMode`、`reuse/rerun`、`turn` 边界，导致用户看不清这轮更新了什么。
6. 移动端不得删除 `Progress | Chat Feed | Report | Follow-up` 四个核心信息面。
7. fail-fast 场景不得只用 toast 或隐藏在 feed 一条小消息里。

## 完成标准
1. 用户能直接看懂当前卡在哪个 stage、哪个 role、为什么推进、为什么阻断、这轮复用了什么。
2. 用户能在同一界面完成研究、追问、回放和动作交接，而不需要跳出到旧聊天面板或隐藏页签。
3. Browser MCP 能稳定验证真实工作流而不是静态 UI，且 feed/report/progress 三块会随运行态真实变化。
4. R7 验收时不需要再补“这个 pane 应该显示什么、移动端怎么退化、blocked 怎么提示”这类基础产品规则。