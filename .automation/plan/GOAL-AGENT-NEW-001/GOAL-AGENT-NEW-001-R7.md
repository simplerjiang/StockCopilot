# GOAL-AGENT-NEW-001-R7 测试目标、回归测试与多维验收

## 任务目标
1. 为 `GOAL-AGENT-NEW-001` 建立完整测试目标、回归测试和多维验收体系。
2. 确保新工作台不仅“能演示”，而且能在真实链路、异常场景和历史回放中稳定工作。
3. 把测试从尾部补充项提升为所有子任务的统一质量门禁。

## 上游依赖
1. [P0](./GOAL-AGENT-NEW-001-P0.md)
2. [R1](./GOAL-AGENT-NEW-001-R1.md)
3. [R2](./GOAL-AGENT-NEW-001-R2.md)
4. [R3](./GOAL-AGENT-NEW-001-R3.md)
5. [R4](./GOAL-AGENT-NEW-001-R4.md)
6. [R5](./GOAL-AGENT-NEW-001-R5.md)
7. [R6](./GOAL-AGENT-NEW-001-R6.md)

## 与最终 PLAN 对应关系
1. 对应总 PLAN 中 `R7. Replay / Browser / Desktop 验收` 整节与全局测试门禁要求。
2. 本任务必须覆盖 PLAN 中列出的首轮研究、follow-up、局部 rerun、全量 rerun、历史 replay、交易计划 handoff、MCP readiness、hard-fail、degraded、并行 stage、微流式反馈、manager 收口一致性等场景。
3. 本任务不是附录，而是所有子任务的统一质量门禁；若任何关键维度未被验证，不得宣告完成。

## 测试总目标
1. 证明系统具备真实多轮 session 能力，而不是视觉假连续。
2. 证明系统具备真实多角色辩论与治理链，而不是一次性伪多角色长文本。
3. 证明工具链、报告、持久化、动作交接和 replay 在异常情况下仍可审计。
4. 证明新增 workbench 不会破坏现有股票页、图表、资讯、本地事实和交易计划链路。

## 详细执行拆解

### 一、R7 的职责不是“补测试文档”，而是质量门禁总装配
1. 每个切片在开发时都要把本文件对应的测试目标前置实现，R7 负责把这些测试组合成统一验收面，而不是最后汇总几行说明。
2. R7 需要明确哪些测试属于阻断门禁，哪些属于增强验收，避免后续以“主流程能演示”为理由跳过关键回归。
3. 对前端、后端、工具层、工作流、Browser MCP、桌面链路分别定义最小必过集，防止某一维完全缺失。

### 二、分层测试装配拆解
1. 合同测试必须覆盖 R1/R2/R6 的 schema、枚举、错误码、ID 关联与状态迁移。
2. 单元测试必须覆盖 continuationMode 判定、权限矩阵、并行 barrier、debate/risk 轮次控制、report block 更新、nextAction 构建。
3. 集成测试必须覆盖真实 MCP adapter、runner 到持久化写入、session detail 查询、partial rerun、历史 replay 查询。
4. 工作流测试必须覆盖：首轮完整研究、同 session follow-up、只重跑风险、只补新闻、全量重跑、历史 replay。
5. Browser MCP 必须覆盖从股票页发起研究到看到 stage 推进、report 更新、follow-up、replay、交易计划 handoff 的真实点击链路。
6. 若改动涉及桌面宿主、启动、打包、运行路径或 packaged 链路，必须把 packaged desktop 验证加入同一轮验收。

### 三、Browser MCP 验收拆解
1. 优先使用 backend-served 页面，不接受只在静态 mock 或 Vite dev 页面上截图式验收。
2. Browser MCP 至少要验证：创建或续接 session、观察并行 analyst running、查看 current report 逐块更新、发起 follow-up、切换 replay、点击 nextAction。
3. 每次 Browser MCP 验收都要同时检查 console 与 network，避免 UI 看起来能点但后端实际报错。
4. 对 fail-fast 场景，必须专门验证阻断 modal 或清晰错误提示，而不是只看 loading 停在那里。

### 四、回归清单实施要求
1. session 回归必须锁定“默认 follow-up 不新开 session”。
2. debate 回归必须锁定 bull/bear 至少一轮真实往返，manager 是裁决者而不是摘要器。
3. risk 回归必须锁定三类 risk analyst 都有独立输出，并且 portfolio manager 读取了这些输出。
4. report 回归必须锁定 `Current Report` 持续更新与 `Final Decision` 不脱链。
5. UI 回归必须锁定桌面 workbench 骨架不退回聊天主舞台，移动端四 tab 信息不丢。
6. 旧功能回归必须锁定图表、证据抽屉、本地事实、交易计划和其他股票页区域未被污染。

### 五、验收证据要求
1. 每轮任务完成都要记录运行的命令、结果摘要、是否包含 Browser MCP、是否包含 packaged desktop。
2. 如果某个测试无法执行，必须明确说明阻塞原因、替代验证和剩余风险，不能直接略过。
3. 验收报告要能回答：功能是否真实可运行、是否可回放、是否可审计、是否破坏旧功能。

### 六、MCP readiness 与 Prompt 验收拆解
1. 在业务流 Browser MCP 之前，必须先做关键 MCP readiness 验收：关键 MCP 能否真实拿数、字段是否完整、数据是否新鲜、错误码是否可识别。
2. Prompt 验收必须核对：本地 MCP 优先、fallback 规则、停止条件、角色权限边界、中文输出约束是否已写入实际 Prompt。
3. 基于环境内可用 LLM key 的实测必须证明模型会按约束优先使用 MCP，而不是直接联网或越权调用工具。
4. readiness 未通过时，后续业务流验收应标记 blocked，而不是继续做“看起来能点”的 UI 验收。

### 七、中文输出与产品叙事验收拆解
1. feed、report、decision、replay 中面向用户的正文输出必须以中文为主，固定协议标记与工具参数名可保留英文原文。
2. 不允许出现大段英文原始回答直接落到主产品面；若出现，应视为 Prompt/输出约束未完成。
3. 工作台主叙事必须仍是 stage/role/report/decision，而不是被群聊外观吞没成普通 assistant 聊天流。

### 八、验收执行顺序细化
1. 先跑单元测试与合同测试，确认 schema、规则、权限、枚举、映射没问题。
2. 再跑集成测试与 workflow 测试，确认 runner、持久化、report、replay、handoff 链路真实打通。
3. 然后跑 Browser MCP，验证 backend-served 页面下的真实点击与运行态变化。
4. 若涉及桌面链路，再做 packaged desktop 验收并记录 EXE 启动与页面可达结果。

## 建议实施顺序
1. 各切片开发时同步补单测或合同测试，不要等到 R7 再统一补。
2. 当 R3/R5/R6 形成稳定后端链路时，先补 workflow tests 和集成测试。
3. 当 R4 完成可交互 UI 后，再做 Browser MCP 真实点击验收。
4. 若涉及桌面链路，则在同一轮最后做 packaged desktop 验证并写入报告。

## 测试维度
1. 合同测试
   - Session/Turn/Stage/Decision schema。
   - Tool envelope、evidence schema、report block、nextAction。
2. 单元测试
   - continuationMode 判定。
   - debate/risk round 控制。
   - fail-fast、degraded、fallback 规则。
   - report block 更新和 handoff 参数构建。
3. 集成测试
   - MCP adapter 实拿数据。
   - session runner 编排。
   - 持久化读写与 replay 查询。
   - API contract 与错误传播。
4. 工作流测试
   - 首轮完整研究。
   - follow-up 补信息。
   - 仅重跑风险评审。
   - 同 session 全量重跑。
   - 历史 replay 与差异比较。
5. Browser MCP 测试
   - backend-served 页面创建 session。
   - 观察 stage 推进和并行 running。
   - 执行 follow-up、打开 replay、起草交易计划。
   - 检查 console 与 network。
6. 桌面打包测试
   - 若改动涉及桌面链路，执行 `scripts\publish-windows-package.ps1`。
   - 确认 `artifacts\windows-package\SimplerJiangAiAgent.Desktop.exe` 产出。
   - 需要时实际启动 packaged EXE 并记录观察结果。
7. 稳定性与韧性测试
   - `local_required` MCP 不可用时的 fail-fast 阻断。
   - `external_gated` 降级继续执行。
   - 并行 stage 完成栅栏。
   - 长耗时工具调用期间的微流式反馈。
8. 回归测试
   - 旧图表、资讯、交易计划、市场上下文等既有入口未被破坏。
   - workbench 不退化回聊天助手。
   - authoritative report/decision 不与 feed 收口消息冲突。

## 回归测试清单
1. Session 回归
   - follow-up 默认续接 active session。
   - 显式新建会话时 sessionId 改变，其余场景不得改变。
   - partial rerun 只重跑指定阶段。
2. Debate 回归
   - bull/bear 至少有一轮往返，不是各说一次。
   - research manager 输出的是裁决和投资计划，不是摘要。
3. Risk 回归
   - 三类 risk analyst 都有独立输出。
   - portfolio manager 必须读取 risk discussion 后才拍板。
4. MCP 回归
   - 本地 MCP 优先，Web Search 仅作 fallback。
   - `local_required` 失败时请求中止并弹阻断提示。
5. Report 回归
   - `Current Report` 运行中持续更新。
   - `Final Decision` 与 nextAction 不脱链。
6. UI 回归
   - 桌面端仍保持工作台布局。
   - 移动端 `Progress | Chat Feed | Report | Follow-up` 四 tab 可用。
7. 旧功能回归
   - 图表加载、证据抽屉、本地事实、交易计划草稿入口仍可用。
   - 与现有股票页其他区域的交互不互相污染。

## 验收场景矩阵
1. Happy path
   - 查股 -> 首轮研究 -> 完整决策 -> 起草交易计划。
2. Follow-up path
   - 同 session 下补新闻 -> report 更新 -> decision 变化可见。
3. Partial rerun path
   - 只重跑 risk review -> analyst/research 结果复用。
4. Replay path
   - 打开历史 session -> 重建 progress/feed/report/decision。
5. Hard-fail path
   - 核心 MCP 宕机 -> 请求中止 -> UI 弹阻断 modal。
6. Degraded path
   - 外部源失败 -> 以 degraded 标记继续执行，不伪装完整结论。
7. Desktop path
   - 受影响时验证 packaged desktop 启动和页面可达。

## 测试门禁
1. 先单元测试，再集成测试，再 Browser MCP。
2. UI 改动必须包含 Browser MCP；后端 contract 改动必须包含定向集成测试。
3. 任一回归失败时，不得以“主流程可演示”为由跳过。
4. 涉及桌面链路时，未完成 packaged 验证不得视为完成。

## 完成标准
1. 每个子任务都有可执行测试目标和最小回归面。
2. 最终实现具备可重复运行的多维测试清单。
3. 验收不再停留在静态截图或手工描述，而是有真实链路证据。
4. 验收报告能清楚回答：MCP 是否 ready、模型是否按规则使用工具、session 是否真实续接、debate/risk 是否真实存在、report/decision 是否 authoritative、旧功能是否未坏。