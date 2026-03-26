# GOAL-AGENT-NEW-001-R3 后端多角色 Graph 编排

## 任务目标
1. 真实落地 staged runtime，而不是大 prompt orchestrator。
2. 实现 analyst 并行、bull/bear debate、trader proposal、risk debate、portfolio decision 的完整执行链。
3. 建立事件总线、阶段推进、stop reason、degraded 传播和阶段恢复能力。

## 上游依赖
1. [R1](./GOAL-AGENT-NEW-001-R1.md)
2. [R2](./GOAL-AGENT-NEW-001-R2.md)

## 下游影响
1. 为 R4 提供实时 feed/progress/report 更新流。
2. 为 R5 提供 debate/risk/approval authoritative 生成链。
3. 为 R6 提供 report block authoritative 更新时机。

## 与最终 PLAN 对应关系
1. 对应总 PLAN 中 `R3. 后端多角色 Graph Orchestration` 整节，必须落实第 0 阶段 `Company Overview Analyst`、6 analyst 并行、bull/bear debate、trader、risk 三角色、portfolio manager 的完整执行链。
2. 本任务必须落实 PLAN 中的 `debateRoundCount`、`riskDebateRoundCount`、微流式事件总线、并行完成栅栏、resume/partial rerun、`local_required` fail-fast 中止规则。
3. 若实现只是在 prompt 中写多角色文本而无真实阶段控制流，则视为未完成。

## 核心工作项
1. 实现 `Company Overview Analyst` 第 0 阶段。
2. 实现 `Analyst Team` 并行 stage 和完成栅栏。
3. 实现 Bull/Bear debate loop 与 `debateRoundCount`。
4. 实现 `Research Manager` 裁决与投资计划输出。
5. 实现 `Trader` 提案输出。
6. 实现 Aggressive/Neutral/Conservative 首轮并行 + 风险辩论轮次 `riskDebateRoundCount`。
7. 实现 `Portfolio Manager` 最终治理出口。
8. 实现统一事件总线：`role started`、`tool dispatched`、`tool progress`、`tool completed`、`summary ready`。
9. 实现 fail-fast、fallback、degraded、超时、重试和 turn 级继续执行。

## 详细开发拆解

### 一、runner 架构必须是真 staged runtime
1. 不允许用一个“大 orchestrator prompt” 一次性写完所有角色内容，然后再在前端拆段显示。
2. 需要显式的 runner 状态机，至少包含：preflight、analyst team、research debate、trader proposal、risk debate、portfolio decision 六段。
3. runner 必须消费 R1 contract 和 R2 工具网关，输出统一事件流、阶段快照、角色状态、report 更新信号和最终 decision。
4. runner 要支持 turn 级执行，不是 session 级一次性跑到底；每次 follow-up 都是在同一 session 下追加一个新的 turn 运行。

### 二、阶段执行拆解
1. `Company Overview Preflight` 负责 symbol 校验、公司基础识别、共享上下文生成，不在 UI 主阶段中强调，但必须真实执行并产出可复用 artifact。
2. `Analyst Team` 要构建并行 fan-out，角色包括 Market、Social、News、Fundamentals、Shareholder、Product；并行完成后再进入 barrier。
3. `Research Debate` 必须是真 debate loop，至少支持 bull -> bear -> manager 的一轮完整链路，并预留多轮 debateRoundCount 控制。
4. `Trader Proposal` 只能消费 research manager 的裁决结果和研究计划，不得绕过 research stage 直接读 analyst 工具结果。
5. `Risk Debate` 首轮允许 aggressive / neutral / conservative 并行，然后根据治理出口决定是否继续第二轮风险辩论。
6. `Portfolio Decision` 必须读取 trader proposal 与 risk artifacts，形成 authoritative final decision，并结束当前 turn。

### 三、事件总线拆解
1. 事件总线要统一定义事件类型，至少包含：`turn.started`、`stage.started`、`role.started`、`tool.dispatched`、`tool.progress`、`tool.completed`、`role.summary_ready`、`stage.completed`、`turn.completed`、`turn.failed`。
2. 每个事件都要带 `sessionId`、`turnId`、`stageId`、`roleType`、`traceId`、`timestamp`，避免 feed 或 replay 无法关联。
3. 事件总线既服务实时 UI，也服务持久化落库和 replay 重建，不允许做成只在 SSE/WebSocket 中存在的瞬时数据。
4. `tool.progress` 不能省略，否则长耗时 MCP 调用期间用户只能看到静态 loading，无法体现工作台推进。

### 四、fail-fast 与恢复拆解
1. 若 `local_required` analyst 工具失败，runner 必须立即把对应 role 标记 failed，再把 stage 标记 blocked，并决定是否终止整个 turn。
2. 若只是 `local_preferred` 或受控 fallback 失败，则允许继续，但要把 degraded 标记透传到 stage/report/decision。
3. runner 需要记录 `resumeFromStage` 或等价状态，保证 follow-up 或 partial rerun 能从正确阶段继续，而不是每次都从 analyst 起步。
4. 对可重试的工具错误，重试逻辑必须封装在运行时或网关层，不能让 prompt 自己“重试”。

### 五、角色输入输出拆解
1. 每个 role 执行前要把可见输入明确组装为 artifact refs，而不是把整段 session 历史原样塞给模型。
2. analyst 输出要形成标准 block，便于 bull/bear 直接引用。
3. bull/bear 输出要包含 claim、evidence refs、counterpoints、open questions，便于 research manager 真实裁决。
4. trader 输出要形成结构化 proposal，供 risk 三角色并行消费。
5. risk 输出要包含 stance、risk limits、invalidations、support/counter arguments，供 portfolio manager 统一收口。

### 六、阶段内控制流下一层拆解
1. `Company Overview Preflight` 结束后，必须形成统一 `company_details` 占位对象，并作为 analyst 并行阶段的共享输入，不允许每个 analyst 各自重新识别公司。
2. `Analyst Team` 阶段必须支持并行批次与 barrier，所有必需 analyst 返回成功或明确降级后才能推进到 `Research Debate`。
3. `Research Debate` 要有显式 loop 控制器，至少支持：初始 bull、bear rebuttal、轮次判断、manager 收敛；而不是硬编码一轮结束。
4. `Risk Debate` 要有独立 loop 控制器，首轮并行后仍允许后续相互补充或反驳，并能记录轮次与依赖关系。
5. `Portfolio Decision` 执行前必须验证 trader proposal 与 risk artifacts 都已可用；若缺失关键输入，则应 blocked 而不是硬生成结论。

### 七、微流式事件与持久化边界拆解
1. 事件总线必须在 `role started`、`tool dispatched`、`tool progress`、`tool completed`、`role summary ready` 五个最小粒度上持续发事件。
2. 每个事件都要能进入 feed 持久化层，保证刷新页面或 replay 时仍可重现微流式推进轨迹。
3. 事件与 role/stage/report 的关系必须稳定可追溯，至少要能知道“哪个事件更新了哪个 report block”。
4. 对长耗时工具调用，必须至少产生一条中间 progress 事件，避免前端只能看到静态 loading。

### 八、continue / rerun / resume 规则拆解
1. 当用户只补新闻、补市场上下文或只重跑风险时，runner 必须根据 R1 的 scope 从正确阶段继续，不得强制全量起跑。
2. 同 session 全量重跑必须生成新 turn，但允许复用 session 级上下文与历史对象引用，不得新开 session。
3. resume 状态必须可持久化，保证进程中断或请求恢复后仍知道上次停在哪个 stage。
4. 若某阶段 blocked，应记录明确 stopReason，供用户后续决定重试、局部 rerun 或新建 session。

## 建议实施顺序
1. 先实现空 runner 骨架和阶段状态流转，不接 LLM，只验证顺序、并行 barrier、事件发射和错误通道。
2. 再接 Company Overview 与 Analyst Team，先让 grounded 输入链跑通。
3. 然后接 Research Debate 与 Trader Proposal，锁定 manager 治理出口。
4. 再接 Risk Debate 与 Portfolio Decision，完成完整闭环。
5. 最后补 partial rerun、resume、retry、degraded 传播与 workflow tests。

## 不能偷懒的实现红线
1. bull/bear 不能合并成一个“正反观点总结”函数。
2. risk 三角色不能共用一份中性输出再换角色名。
3. portfolio decision 不能只重述 trader proposal。
4. 并行 stage 不能只是在 UI 上显示并行，后台实际串行执行。

## 交付物
1. 多阶段 runner：至少覆盖 phase 0、analyst team、research debate、trader proposal、risk debate、portfolio decision。
2. Debate loop 运行时：至少包含 bull/bear 轮次控制、manager 收敛入口、停机条件。
3. Risk loop 运行时：至少包含 aggressive/neutral/conservative 首轮并行、后续补充轮、停机条件。
4. 统一事件总线：至少包含事件定义、发布点、持久化策略、UI/replay 消费约定。
5. 阶段/角色状态推进与 stop reason 记录：至少覆盖 running/completed/degraded/blocked/failed。
6. resume / rerun 执行规则说明：至少覆盖 partial rerun、full rerun in same session、blocked recovery。

## 测试目标
1. 编排单元测试：阶段顺序、phase 0 共享输入、并行阶段完成栅栏、stop condition、debate/risk round 控制。
2. 集成测试：MCP 结果进入 role -> stage -> report -> decision 的路径正确，tool event 与 stage state 对齐。
3. 工作流测试：首轮研究、只重跑风险、只补新闻、补市场上下文、同 session 全量重跑、blocked 后恢复。
4. 稳定性测试：单个 role 降级或失败时，上层状态不静默丢失；`local_required` 失败时流程立即中止。
5. 事件流测试：长耗时角色存在微流式事件，feed 不会只在最后一次性出现结果。
6. 并行测试：Analyst Team 与 Risk Debate 在运行时可同时看到多个 role running，且 barrier 生效。

## 回归测试要求
1. bull/bear debate 不得退化为“一人一段”。
2. trader 不得跳过 research manager。
3. risk team 不得只剩单条风险提示。
4. 并行 stage 不得被误实现成串行等待。
5. `Company Overview Analyst` 不得被删除或省略为前端临时预处理。
6. `local_required` MCP 故障时不得继续推进到后续 stage。
7. 事件流不得只剩 stage 完成事件而失去中间 tool/role progress。

## 完成标准
1. 后端可独立跑完整个多角色链路，并符合 PLAN 冻结的阶段顺序与治理关系。
2. follow-up 能从正确 stage 继续，而不是每次从 analyst 起步；partial/full rerun 均可稳定执行。
3. 运行时事件足够支撑 UI 微流式反馈和 replay，刷新后也不会丢失执行轨迹。
4. 后续 R4/R5/R6 不需要再为 debate/risk/manager 治理顺序补逻辑空洞。