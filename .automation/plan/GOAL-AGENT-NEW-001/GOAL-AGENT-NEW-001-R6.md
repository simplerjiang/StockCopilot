# GOAL-AGENT-NEW-001-R6 Grounded Report 与动作交接

## 任务目标
1. 把 `Current Report` 做成 authoritative、结构化、可动作化的主输出。
2. 让 `Final Decision` 可直接交接给图表、证据、本地事实和交易计划链路。
3. 保证 report 既服务运行态，也服务 replay 和验收。

## 上游依赖
1. [R3](./GOAL-AGENT-NEW-001-R3.md)
2. [R5](./GOAL-AGENT-NEW-001-R5.md)

## 下游影响
1. 为 R4 提供 report block authoritative 数据。
2. 为 R7 提供 report/assertion/handoff 测试基线。

## 与最终 PLAN 对应关系
1. 对应总 PLAN 中 `R6. Grounded Report 与动作交接` 整节，必须落实八类 report block、final decision schema、nextAction contract、manager reply 与 authoritative schema 的关系、degraded/failure 表达。
2. 本任务还必须落实 PLAN 中“report 既服务运行态，也服务 replay 和验收”“交易计划不得绕过 risk/manager”“图表/证据/本地事实 handoff 必须带 session/turn/reportBlock 上下文”的要求。

## 核心工作项
1. 定义 report block：Market、Social、News、Fundamentals、Research Debate、Trader Proposal、Risk Review、Portfolio Decision。
2. 定义 report block 字段：headline、summary、keyPoints、evidenceRefs、counterEvidenceRefs、disagreements、riskLimits、invalidations、recommendedActions。
3. 定义 `Final Decision` schema：direction、rating、confidence、thesis、supportingEvidence、counterEvidence、riskLimits、invalidations、nextActions。
4. 定义 nextAction contract：看日K、看分时、看证据、看本地事实、起草交易计划、继续追问争议点。
5. 定义 manager reply 与 authoritative schema 的关系。
6. 定义 degraded/failure 在 report 中的表现方式。

## 详细开发拆解

### 一、report 必须是 authoritative object，不是富文本摘要
1. `Current Report` 的每个 block 都要有稳定 blockId、blockType、所属 session/turn/stage、更新时间和版本信息。
2. report block 必须能在运行中逐块更新，而不是只在 turn 结束时一次性生成整份结果。
3. `Current Report` 与 `Final Decision` 不能依赖前端临时拼接；后端需要明确 authoritative schema，前端只负责渲染与交互。

### 二、report block 拆解
1. analyst 相关 block 至少包括 Market、Social、News、Fundamentals，必要时为 Shareholder、Product 预留 block 类型。
2. debate 相关 block 要能记录 bullish/bearish 分歧、manager 裁决结果和未解决争议。
3. trader proposal block 要能表达仓位、节奏、条件、观察点与修正触发器。
4. risk review block 要明确三类风险立场、共同结论、主要冲突、限制条件与失效条件。
5. portfolio decision block 要成为 report 中的权威收口，和独立 `Final Decision` snapshot 一一对应。

### 三、Final Decision schema 拆解
1. `direction`、`rating`、`confidence` 只是摘要字段，必须同时带 `thesis`、`supportingEvidence`、`counterEvidence`、`riskLimits`、`invalidations`。
2. `confidence` 要有可解释来源，例如 evidence 覆盖度、冲突强度、降级标记，而不是仅一个裸分数。
3. `nextActions` 必须结构化，至少包含 actionType、label、target、requiredContext、reportBlockId、sessionId、turnId。
4. `Final Decision` 与 manager 收口消息必须一致；如果 manager 输出文本与结构化 decision 不一致，以结构化 decision 为准，并应在测试中锁定这种一致性。

### 四、nextAction 与现有链路交接拆解
1. 日 K、分时、证据、本地事实、交易计划、继续追问都要定义统一 nextAction contract，而不是每个按钮临时传不同参数。
2. nextAction 至少要能携带 symbol、sessionId、turnId、blockId、artifact refs、建议原因与是否需要新页面聚焦。
3. 对交易计划起草动作，要明确它消费的是 `Final Decision` 还是 trader proposal 的哪个版本，避免动作链接到旧对象。
4. 对“继续追问争议点”动作，要能回填到 follow-up composer，带上争议对象引用，而不是只复制一段文本。

### 五、degraded 与 failure 表现拆解
1. 若某个 report block 因工具失败而不完整，必须在 block 级别写明 `status`、`degradedFlags`、`missingEvidence` 和 `confidenceImpact`。
2. blocked 场景下，report 不能假装完整；应明确标注哪些 block 未生成、为什么未生成、是否允许用户重试或 rerun。
3. failure/degraded 的表达要同时服务运行态和 replay，保证历史会话回看时依然知道当时有哪些缺口。

### 六、八类 block 的下一层要求
1. `Market` block：至少体现趋势判断、关键信号、量价或结构要点、主要证据、反证、对交易节奏的影响。
2. `Social` block：至少体现情绪方向、样本来源、噪音与可信度判断、降级说明、与新闻的差异。
3. `News` block：至少体现近期事件、时间窗、source tier、对 thesis 的支持与反证、过期风险。
4. `Fundamentals` block：至少体现估值/财务/质量要点、缺失科目、证据来源、可信度限制。
5. `Research Debate` block：至少体现 bull/bear 主张、核心分歧、manager 裁决、未解决问题。
6. `Trader Proposal` block：至少体现方向、仓位、入场/退出、触发条件、节奏、失效条件。
7. `Risk Review` block：至少体现 aggressive/neutral/conservative 三方观点、共同限制、未达成一致之处。
8. `Portfolio Decision` block：至少体现 rating、headline、executive summary、investment thesis、nextActions。

### 七、manager reply 与用户可读投影拆解
1. `Research Manager` 与 `Portfolio Manager` 在 feed 中可以有用户可读消息，但它们必须是结构化 report/decision 的投影，而不是另一套并列输出源。
2. 用户可读 reply 必须可追溯到 blockId 或 decisionId，方便回放与一致性校验。
3. 如果用户可读 reply 需要简化表述，也不能丢掉关键风险限制、失效条件和 nextAction 主体。

### 八、动作交接链路拆解
1. `看日K`、`看分时`、`查看证据`、`查看本地事实`、`起草交易计划`、`继续追问争议点` 都必须定义成统一 nextAction contract。
2. 每个动作至少包含：actionType、label、targetSurface、sessionId、turnId、reportBlockId、decisionId 或 artifact refs、reasonSummary。
3. 图表动作必须能把当前 report/decision 的上下文传给图表区，而不是只做表层跳转。
4. 交易计划 handoff 必须明确来源于 `Final Decision + Trader Proposal + Risk Limits`，不能只读取某一条聊天消息。

## 建议实施顺序
1. 先定义 report block、final decision、nextAction 的 schema 和 DTO。
2. 再把 R3/R5 的运行时与持久化对象映射到 report 更新流程，先让 block 能持续写入。
3. 然后接 UI 所需的 action handoff 参数与 block 交互层。
4. 最后补一致性测试、降级测试和跨 turn 引用测试。

## 不能偷懒的点
1. 不能把 report 当成一段 markdown 文本字段存库。
2. 不能让 nextAction 只是一段提示语，没有可执行 contract。
3. 不能让 report block 与 evidence/tool event 脱链，导致用户看得到结论却追不到依据。

## 交付物
1. report block schema：至少包含 block 类型枚举、公共字段、每类 block 的必填语义字段。
2. final decision schema：至少包含 direction、rating、confidence、thesis、supporting/counter evidence、risk limits、invalidations、nextActions。
3. nextAction schema：至少包含动作类型、上下文参数、目标 surface、是否可用、禁用原因。
4. manager reply 投影规则：至少明确 feed 消息与 authoritative report/decision 的映射方式。
5. handoff 规则：至少覆盖图表、证据、本地事实、交易计划、继续追问五类入口的参数链。
6. degraded/failure 表达规则：至少覆盖 block 级状态、缺失证据、confidenceImpact、动作禁用策略。

## 测试目标
1. schema 测试：block/decision/action 字段齐全、可序列化、可跨 turn 引用、版本关系稳定。
2. 集成测试：tool/evidence -> role -> report block -> final decision 的写入链正确，manager reply 与 decision 一致。
3. 动作测试：nextAction 能带着正确 sessionId/turnId/reportBlockId/decisionId 跳到目标链路。
4. 降级测试：部分 block 缺证据或工具失败时，report 明确标示不完整状态，相关动作正确禁用或降级。
5. replay 测试：历史 session 中的 report blocks 与 final decision 可直接重建，不依赖重新生成文案。

## 回归测试要求
1. 不得重新退化成一段最终总结。
2. 不得绕过 risk/manager 直接生成交易计划。
3. 不得让 report 与 evidence/tool event 脱节。
4. 不得让 feed 中 manager 收口消息与 `Current Report` / `Final Decision` 冲突。
5. 不得让 nextAction 只有文案、没有可执行参数。

## 完成标准
1. `Current Report` 成为 authoritative 主输出，而不是附属文案；八类 block 均有明确语义边界。
2. `Final Decision` 能直接驱动现有系统动作入口，并与用户可读收口消息保持一致。
3. report/handoff 可被测试稳定断言，R7 可直接据此写 Browser MCP 与 workflow 验收。
4. 后续评审不再需要争论“到底以聊天回复还是 report 为准”，因为本任务已把权威来源冻结为结构化对象。