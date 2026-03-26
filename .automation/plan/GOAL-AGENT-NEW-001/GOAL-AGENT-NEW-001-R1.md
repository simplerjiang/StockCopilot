# GOAL-AGENT-NEW-001-R1 真实多轮 Session Contract

## 任务目标
1. 定义真实的 session/turn/stage/decision 数据模型与 API contract。
2. 确保 follow-up 默认续接当前 session，而不是伪连续聊天。
3. 为 replay、partial rerun、full rerun 和 authoritative report 提供稳定 ID 与持久化边界。

## 上游依赖
1. [P0](./GOAL-AGENT-NEW-001-P0.md)

## 下游影响
1. 为 R3 提供 runner 可执行状态对象。
2. 为 R4 提供前端工作台状态来源。
3. 为 R5/R6 提供持久化和 authoritative object 边界。

## 与最终 PLAN 对应关系
1. 对应总 PLAN 中 `R1. 真实多轮 Session Contract` 整节，不允许削弱为“只定义几个 DTO”。
2. 本任务必须落实 PLAN 中关于 `active session`、`turn`、`continuationMode`、`reuseScope`、`rerunScope`、`changeSummary`、`session 级锁`、`指定 session 详情`、`历史 session 列表` 的全部要求。
3. 本任务还必须为 PLAN 中的 `Discussion Feed 按 turn 分段`、`Current Report 持续更新`、`Replay 不依赖 prompt 重建` 提供稳定 ID 与读取合同。
4. 若本文件与总 PLAN 存在冲突，以总 PLAN 已冻结版本为准，并应先回改本文件再进入开发。

## 核心工作项
1. 设计 `ResearchSession`、`ResearchTurn`、`StageSnapshot`、`RoleState`、`CurrentReportSnapshot`、`DecisionSnapshot`。
2. 设计 `continuationMode`、`reuseScope`、`rerunScope`、`changeSummary`。
3. 设计 active session 规则、显式新建会话规则和 session 级锁。
4. 设计 API contract：active session 读取、指定 session 详情、历史 session 列表、turn 提交。
5. 明确持久化边界：session、turn、stage、role message、report snapshot、decision snapshot 分层落地。
6. 定义错误和降级如何从 tool -> role -> stage -> session 上行传播。

## 详细开发拆解

### 一、先锁对象边界，再写任何运行时代码
1. 先定义 `ResearchSession` 顶层对象，字段至少包含：`sessionId`、`symbol`、`name`、`status`、`activeTurnId`、`activeStage`、`degradedFlags`、`createdAt`、`updatedAt`、`lastUserIntent`。
2. 再定义 `ResearchTurn`，字段至少包含：`turnId`、`sessionId`、`turnIndex`、`userPrompt`、`continuationMode`、`reuseScope`、`rerunScope`、`changeSummary`、`requestedAt`、`startedAt`、`completedAt`、`stopReason`。
3. 再定义 `StageSnapshot`，字段至少包含：`stageId`、`turnId`、`stageType`、`executionMode`、`status`、`activeRoleIds`、`startedAt`、`completedAt`、`summary`、`degradedFlags`、`stopReason`。
4. 再定义 `RoleState`，字段至少包含：`roleStateId`、`stageId`、`roleType`、`status`、`inputRefs`、`outputRefs`、`toolPolicyClass`、`startedAt`、`completedAt`、`degradedFlags`、`errorCode`。
5. `CurrentReportSnapshot` 与 `DecisionSnapshot` 虽在 R6 扩展，但在 R1 必须先留 stable id 和挂接位置，避免后面追加时破坏 turn/stage 关系。

### 二、API contract 必须一次性定稳
1. 至少定义 4 类 API：读取 active session、按 sessionId 读取详情、按 symbol 读取历史 session 列表、提交新 turn/follow-up。
2. `POST turn` 请求体必须显式带上 `sessionId` 或 `createNewSession` 标记，禁止后端根据是否有历史消息自行猜测是否续接。
3. `POST turn` 响应必须同步回传本轮 `turnId`、`sessionId`、`continuationMode`、`acceptedReuseScope`、`acceptedRerunScope`，让前端能立刻显示“这轮到底复用了什么”。
4. `GET active session` 需要既能返回运行中的 session，也能明确说明“当前无 active session”，不能让前端自己从历史列表推断。
5. `GET session detail` 必须按 authoritative 对象返回 session、turn、stage、role、feed、report、decision 基本骨架，避免前端从多个 endpoint 手工拼装。

### 三、持久化边界拆解
1. `ResearchSession` 与 `ResearchTurn` 必须分表或分实体，不允许把多轮追问全部塞进一个 JSON blob 里。
2. `StageSnapshot` 与 `RoleState` 要支持一对多追加，保证同一 turn 中多个 stage、同一 stage 中多个 role 都可单独追踪。
3. feed 相关对象要区分 `role message` 与 `tool event`，避免 UI 只能从文本内容里猜类型。
4. report snapshot 与 decision snapshot 必须保留版本号或时间戳，保证 replay 时能看到“运行中更新”和“最终收口”的差异。
5. 所有对象都要带稳定外键或引用 ID，供 R4/R5/R6 后续直接链接，不允许依赖数组顺序作为关联关系。

### 四、continuation 与 rerun 规则拆解
1. `continuationMode` 至少区分：`continue-session`、`new-session`、`partial-rerun`、`full-rerun`，并在服务端有明确判定规则。
2. `reuseScope` 至少要能表达 stage 级、role 级或 artifact 级复用，例如复用 analyst outputs 但重跑 risk review。
3. `rerunScope` 需要能精确到固定阶段，禁止只支持“全量重跑”这一种粗粒度模式。
4. `changeSummary` 不只是展示字段，它必须总结本轮相对上轮新增了什么、复用了什么、推翻了什么，供 UI header、feed turn 摘要和 replay diff 共用。
5. 服务端要验证 scope 合法性，例如不能在没有 trader proposal 的前提下直接只跑 portfolio decision。

### 五、错误与降级传播拆解
1. 工具失败或降级先体现在 `tool event`，再汇总到 `RoleState.degradedFlags`，再上卷到 `StageSnapshot.degradedFlags`，最后体现在 session 总状态和 report 不完整标记中。
2. `local_required` 工具失败时不能只记日志，必须让 stage 或 session 明确进入 blocked / failed，并返回 machine-readable error code。
3. `external_gated` 或 `local_preferred` 的失败则可以保留 degraded 执行，但必须写清楚哪些 block 受影响。
4. 所有错误传播都必须可持久化、可 replay、可被 UI 直接消费，不能只靠运行时异常文本。

### 六、feed / report / replay 对齐拆解
1. `ResearchTurn` 必须明确挂接本轮 feed item 集合，而不是让 feed 由时间戳散落对象临时拼装。
2. `StageSnapshot` 必须显式记录本阶段关联的 feed item 范围、report block 更新范围和 decision 影响范围。
3. `CurrentReportSnapshot` 需要区分运行中快照和收口快照，至少要能表达“第几次更新”与“由哪个 stage 触发更新”。
4. `DecisionSnapshot` 必须保留 `supersededByDecisionId` 或等价版本关系，支持多轮 follow-up 之后的比较。
5. replay 查询不能只返回最终 summary，必须能沿着 `session -> turn -> stage -> role/feed/report/decision` 重建完整历史。

### 七、API 细化到请求/响应层
1. `GET /active-session` 或等价接口必须返回：session 摘要、active turn 摘要、active stage 摘要、是否 degraded、是否 blocked、最近更新时间。
2. `GET /sessions/{sessionId}` 或等价详情接口必须返回：session 基本信息、turn 列表、当前 turn 完整快照、历史 turn 摘要、当前 report、当前 decision、必要的 replay 索引信息。
3. `GET /sessions?symbol=` 或等价历史接口必须至少返回：sessionId、createdAt、updatedAt、latestTurnIndex、latestStage、latestRating、latestDecisionHeadline、status。
4. `POST /turns` 或等价提交接口必须支持：显式 `sessionId` 续接、显式 `createNewSession=true`、partial rerun、full rerun in same session、普通 follow-up。
5. 所有写接口都必须回传 `acceptedPlan`，明确后端最终接受的 continuation/reuse/rerun 结果，防止前端与后端理解不一致。

### 八、旧数据隔离与迁移边界
1. 明确与已删除的旧 `GOAL-AGENT-002` 会话数据完全隔离，不复用旧聊天 session 表做伪兼容。
2. 若需要读取旧历史作为参考，只能通过明确的 migration 或 adapter，不得让新合同直接继承旧字段语义。
3. 新合同对象命名必须围绕 `ResearchSession` / `ResearchTurn` / `StageSnapshot` / `RoleState` 展开，不能混入旧 Copilot session 命名。

## 建议实施顺序
1. 先画 DTO、枚举、状态流转图和数据库对象草图，确认命名与 P0 术语完全一致。
2. 再补 API request/response contract 和服务层接口，暂时可用 mock runner 返回空骨架，不急着接真实 LLM/MCP。
3. 然后落数据库迁移与仓储查询，确保 active session、历史 session、session detail 三类读接口都能稳定返回。
4. 最后补 continuationMode 判定、scope 合法性校验、错误传播映射和合同测试。

## 数据库对象草图要求
1. 至少明确 `ResearchSessions`、`ResearchTurns`、`ResearchStageSnapshots`、`ResearchRoleStates` 四类对象的主键、外键和索引。
2. `ResearchSessions` 需要有 `symbol + status` 或等价 active-session 查询索引，保证同一 symbol 只会有一个 active session。
3. `ResearchTurns` 需要 `sessionId + turnIndex` 唯一约束，防止并发 follow-up 造成 turn 序号冲突。
4. `ResearchStageSnapshots` 需要支持按 `turnId + stageType + stageRunIndex` 查询，给 partial rerun 和 replay 准备空间。
5. `ResearchRoleStates` 需要保留 `stageId + roleType + runIndex` 粒度，支持 debate 与 risk 多轮运行。

## continuationMode 规则表补充要求
1. 文件中必须明确“默认 follow-up = continue-session”，除非用户显式新建会话或输入变化已不再属于当前 symbol 上下文。
2. 如果用户只补某个证据域，例如“补最近三天公告”，优先判定为 `partial-rerun`，复用 analyst 之外的稳定对象。
3. 如果用户改写了目标，例如从“值不值得看”切到“直接生成交易计划”，仍然属于同一 symbol 时应先尝试 `continue-session`，但允许上调 rerunScope。
4. 只有 symbol 变化、用户显式点“新建会话”、或当前 session 已 closed 且不允许恢复时，才切 `new-session`。

## 交付物
1. Session contract 文档：至少包含 `ResearchSession` 字段表、状态枚举、生命周期说明。
2. Turn contract 文档：至少包含 `ResearchTurn` 字段表、continuation/reuse/rerun 规则、changeSummary 生成规则。
3. Stage 与 Role contract 文档：至少包含 `StageSnapshot`、`RoleState`、feed item 基础结构、错误传播层级。
4. API contract 文档：至少包含 active session、session detail、session list、turn submit 的 request/response 草图。
5. 数据库对象草图：至少包含表关系、主键/外键、关键索引、并发约束说明。
6. continuationMode 规则表：至少覆盖 `continue-session`、`new-session`、`partial-rerun`、`full-rerun`、symbol 变化、session closed 场景。
7. session 锁与并发策略说明：至少覆盖同 symbol 并发 follow-up、重复提交、turnIndex 冲突和恢复策略。

## 测试目标
1. 合同测试：`ResearchSession`、`ResearchTurn`、`StageSnapshot`、`RoleState`、`CurrentReportSnapshot`、`DecisionSnapshot` 字段、枚举、必填约束、ID 关联稳定。
2. 状态迁移测试：session 从 idle/running/degraded/blocked/completed 的迁移合法，turn 从 draft/queued/running/completed/failed 的迁移合法。
3. API 测试：同 session follow-up、显式新建会话、历史 session 查询、指定 session 详情读取、partial rerun/full rerun 请求合法性。
4. 并发测试：同一 symbol 连续提交 follow-up 时不会生成双 active session，不会产生重复 turnIndex。
5. 持久化测试：session 与 turn 不串数据，stage/role/feed/report/decision 快照可追溯，跨 turn supersede 链有效。
6. 错误传播测试：tool -> role -> stage -> session 的 errorCode、degradedFlags、blocked 状态能稳定落库并被查询返回。

## 回归测试要求
1. 追问不得偷偷新开 session；除显式 `new-session` 外，sessionId 必须保持不变。
2. 不得只保留 final answer 而丢失 turn/stage/feed/report/decision 过程对象。
3. 不得让 UI 只能看到聊天消息却看不到 turn 级边界、continuationMode、复用范围和变化摘要。
4. partial rerun 不得错误新建 session，也不得把不需要重跑的阶段清空。
5. full rerun in same session 不得覆盖旧 turn，只能新增 turn 并保留旧 turn 可回放性。
6. session detail 接口不得因某些对象缺失而 silently fallback 成旧聊天 DTO。

## 完成标准
1. follow-up、partial rerun、full rerun、new session 都能用稳定 contract 表达，且前后端对同一请求的解释一致。
2. replay 不依赖重新拼 prompt 才能恢复历史，单靠持久化对象即可重建 turn/stage/feed/report/decision。
3. 前后端可直接基于同一套对象协作，不需要各自发明命名或临时字段。
4. R3/R4/R5/R6 后续实现不需要再回头补“turn 是什么”“active session 怎么选”“changeSummary 放哪里”这类基础定义。