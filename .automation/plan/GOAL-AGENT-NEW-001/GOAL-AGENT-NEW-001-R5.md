# GOAL-AGENT-NEW-001-R5 Debate、Risk 与 Approval 持久化

## 任务目标
1. 把 debate、risk、manager approval 和 trader proposal 变成真实持久化对象。
2. 支持多轮追问、历史回放、版本比较和引用上轮争议点继续追问。
3. 保证前端不需要再从大段文本里脆弱解析结构。

## 上游依赖
1. [R1](./GOAL-AGENT-NEW-001-R1.md)
2. [R3](./GOAL-AGENT-NEW-001-R3.md)

## 下游影响
1. 为 R4 提供结构化查询接口。
2. 为 R6 提供 authoritative object 链。
3. 为 R7 提供 replay 和比较测试对象。

## 与最终 PLAN 对应关系
1. 对应总 PLAN 中 `R5. Debate / Risk / Approval Persistence` 整节，必须落实 bull/bear debate、research manager 收敛、trader proposal versioning、risk 三角色、portfolio manager decision snapshot 的独立持久化。
2. 本任务必须落实 PLAN 中“新 turn 只能追加或 supersede，不允许覆盖旧历史”“follow-up 可引用上轮争议点继续问”“feed 消息必须能追溯到真实对象”的要求。

## 核心工作项
1. 定义 bull/bear debate message 模型：speaker、roundIndex、claim、supportingEvidenceRefs、counterTargetRole、counterPoints、openQuestions。
2. 定义 research manager 收敛结果对象。
3. 定义 trader proposal versioning 和 superseded 规则。
4. 定义三类 risk 输出和 risk discussion round 模型。
5. 定义 portfolio manager decision snapshot。
6. 绑定 stageId、turnId、sessionId、toolCallRefs、evidenceRefs。
7. 为 replay comparison 和 follow-up 引用保留结构化字段。

## 详细开发拆解

### 一、不要把结构化对象藏回 report 文本
1. bull/bear debate、risk review、research manager decision、trader proposal、portfolio decision 都要有独立实体或独立持久化对象，不能只埋在一段长文本里。
2. R5 的核心价值是让 replay、比较、追问引用和 UI 展示都基于真实对象，而不是靠字符串解析或 prompt 重建。
3. 任何对象一旦对用户可见，就必须有稳定 ID、版本关系和 turn/stage 归属，不允许出现“当前看得见，刷新后就只剩 summary”。

### 二、debate 持久化拆解
1. bull 与 bear 每条正式发言都要记录：`messageId`、`sessionId`、`turnId`、`stageId`、`speakerRole`、`roundIndex`、`stance`、`claim`、`supportingEvidenceRefs`、`counterTargetRole`、`counterPoints`、`openQuestions`。
2. `claim` 与 `counterPoints` 不只是文本数组，它们需要可以被 research manager 在后续对象中按引用收敛。
3. debate 对象要允许多轮追加，不能默认一轮结束；因此要有 `roundIndex` 和 `sequenceInRound` 或等价字段。
4. 如果某轮 debate 因缺失 evidence 被降级，也要在对象上记录 `degradedFlags` 和 `confidenceImpact`。

### 三、research manager 与 trader proposal 拆解
1. research manager 输出要独立成收敛对象，至少包含：采纳的 bull 点、采纳的 bear 点、被搁置争议、最终研究结论、投资计划草案。
2. trader proposal 要有版本化模型，字段至少包含：`proposalId`、`sessionId`、`turnId`、`version`、`status`、`direction`、`entryPlan`、`exitPlan`、`positionSizing`、`timeframe`、`rationale`、`supersededByProposalId`。
3. 每次 follow-up 若只更新风险或新闻，也可能导致 trader proposal 被 supersede，因此 proposal 版本关系必须稳定，而不是覆盖原记录。
4. trader proposal 必须能回指 research manager artifact，保证链路可追溯。

### 四、risk 与 approval 持久化拆解
1. risk 三角色输出要独立持久化，字段至少包含：`riskArtifactId`、`roleType`、`roundIndex`、`stanceSummary`、`riskLimits`、`invalidations`、`supportingArguments`、`opposingArguments`、`recommendedAdjustments`。
2. 如果风险组内部有第二轮讨论，需要能表达“上一轮谁反对了谁、这轮怎么修正”，因此至少要有 `respondsToArtifactId` 或等价引用链。
3. portfolio manager 最终 decision 要独立存为 snapshot，不能只是 report 的最后一个 block 文本。
4. decision snapshot 需要稳定引用 proposal、risk artifacts、research decision 和 evidence refs，为后续交易计划 handoff 提供权威来源。

### 五、查询与 replay 比较拆解
1. 必须提供按 session、turn、stage 查询 debate/risk/proposal/decision 的接口或查询层能力。
2. replay 不应依赖重新运行 LLM；它应该只重放这些持久化对象与事件流。
3. 比较视图至少要能比较相邻两个 turn 的 proposal/decision 差异，并指出哪些争议点被保留、推翻或新增。
4. follow-up 引用上一轮争议点时，应直接引用 `messageId` 或 artifact id，而不是靠匹配文本相似度。

### 六、结构化对象之间的引用链拆解
1. bull/bear message 必须能被 research manager 收敛对象按引用采纳或驳回，不允许只在 manager summary 里写一段“综合来看”。
2. research manager 对象必须能回指本轮 debate artifacts，并为 trader proposal 提供权威输入引用。
3. trader proposal 必须能回指 research decision，并被 risk artifacts 与 portfolio decision 引用。
4. risk artifacts 必须既能引用 proposal，也能互相引用上一轮风险意见，形成真实辩论链。
5. portfolio decision 必须回指 proposal、risk artifacts、research decision 和关键 evidence，形成 handoff 可追溯链。

### 七、feed 映射与引用追问拆解
1. 每条对用户可见的群聊式消息都必须能映射回某个 debate/risk/manager/proposal/decision 对象，不能成为纯展示层孤儿节点。
2. follow-up 如果针对“上一轮 Bear 的某条反驳”继续追问，系统应直接记录被引用对象 ID，而不是只保存一段自然语言文本。
3. replay 比较时，应能指出“本轮是对哪条历史对象进行回应或修正”。

### 八、superseded 与归档规则拆解
1. proposal、decision、report block 的 supersede 关系必须显式存储，不能靠时间戳猜测谁覆盖了谁。
2. 历史对象默认只归档不删除，除非有单独清理策略；新 turn 不得物理覆盖旧 turn 的结构化对象。
3. 如果某轮 follow-up 只是补证据而未改变 decision，也要明确记录“decision 未变”的结构化事实，而不是完全不落对象。

## 建议实施顺序
1. 先定义 debate/risk/proposal/decision 对象 schema 与数据库模型。
2. 再把 R3 运行时输出接到这些持久化对象，先实现写入，不急着做复杂查询。
3. 然后补查询接口与 replay comparison 视图所需的读取模型。
4. 最后补 superseded 规则、引用链、差异摘要和回归测试。

## 不能偷懒的点
1. 不能把 debate/risk 压成 report 文本再反向解析。
2. 不能让新 turn 覆盖旧 proposal 或 decision。
3. 不能只有最终 decision 有 ID，而 debate/risk message 没有稳定引用。

## 交付物
1. Debate 持久化模型：至少包含 bull/bear message schema、roundIndex、counterTargetRole、openQuestions、引用链字段。
2. Research manager 收敛模型：至少包含采纳点、拒绝点、未解决分歧、研究结论、investment plan 输入。
3. Trader proposal versioning 方案：至少包含 version、status、supersededBy、引用 research decision 的方式。
4. Risk 持久化模型：至少包含三类 risk artifact schema、roundIndex、respondsToArtifactId、risk limits、adjustments。
5. Approval/decision 模型：至少包含 rating/direction/confidence、proposal/risk/research 引用链、supersede 关系。
6. 查询与 replay comparison 读取模型：至少包含按 session/turn/stage 读取、按相邻 turn 比较、按对象引用追问的能力说明。

## 测试目标
1. 持久化测试：新 turn 追加不覆盖旧 proposal/decision/debate/risk 对象，superseded 关系正确。
2. 查询测试：按 session、turn、stage 能正确读出 debate/risk/proposal/decision，并保持引用链完整。
3. 比较测试：同一 session 多轮结果可对比差异，能指出新增、保留、推翻的争议点和决策变化。
4. Replay 测试：不重新跑 LLM 也能重建讨论与治理过程，并恢复 feed 对应的结构化来源对象。
5. 引用追问测试：用户针对上一轮某条 bull/bear/risk 消息继续追问时，系统能引用正确对象 ID。

## 回归测试要求
1. 不得把 discussion 压成 report 文本后再反向解析。
2. 不得在 follow-up 时覆盖历史 proposal/decision。
3. 不得让结构化对象和 feed 显示脱钩。
4. 不得只保留 manager 最终结论而丢失 bull/bear/risk 的原始辩论对象。
5. 不得让 replay 比较依赖字符串 diff，而不是结构化对象差异。

## 完成标准
1. debate、risk、proposal、decision 都可独立查询、回放、比较、引用，且带稳定 ID 与引用链。
2. 新旧 turn 的变化能被清晰表达，至少能看见哪些争议点、风险限制、proposal/decision 发生了变化。
3. UI 和 handoff 不再依赖长文本解析，R4/R6 可直接消费 authoritative object。
4. 后续实现不再需要争论“某条消息到底对应哪个真实对象”，因为本任务已定义稳定映射。