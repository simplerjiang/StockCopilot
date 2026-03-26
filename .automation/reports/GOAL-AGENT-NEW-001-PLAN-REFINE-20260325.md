# GOAL-AGENT-NEW-001 规划细化补充报告（2026-03-25）

## 本轮结论
1. 已按用户要求把 `GOAL-AGENT-NEW-001` 的总入口和 P0、R1-R7 子任务全部从“提纲级”扩成“可执行级”。
2. 本轮重点不是新增需求，而是把原本过于简略的子任务细化成开发代理可以连续执行、不容易丢失主目标的实施规格。
3. 现在每个切片都明确了：具体拆成哪些实现块、先做什么、后做什么、哪些对象必须先定义、哪些异常路径必须显式处理、如何验证完成。
4. 本轮未修改前后端运行时代码，交付物全部是规划文档、自动化台账和报告同步收口。

## 本轮具体补强点

### 总入口 README
1. 新增 `子任务细化约束`，要求每个任务文件必须同时写清边界、开发拆解、对象/接口草图、实施顺序、失败与降级处理、测试门禁。
2. 新增 `统一开发拆解模板`，统一所有子任务的写法，防止不同文件粒度参差不齐。
3. 新增 `跨任务对齐要求` 和 `实施顺序细化`，明确 R1/R2/R3/R4/R5/R6/R7 之间的消费关系与先后顺序。

### P0
1. 补强 `详细执行拆解`，明确参考实现对齐、本仓库底座盘点、术语冻结、偏离警戒线和对下游切片的强制输出。
2. 新增 `P0 完成后的引用规则`，要求后续任务不得绕开 P0 重新发明术语或产品定义。

### R1
1. 把 session/turn/stage/role/report/decision 的对象边界写到具体字段层级。
2. 明确 API contract、数据库对象草图、continuation/reuse/rerun 规则、session 锁与错误传播层级。
3. 强制实施顺序为“先 contract，后 API，后持久化，最后 runner 对接”。

### R2
1. 把工具层补强为统一 `MCP Manager / Tool Gateway` 方案，而不是继续按 tool 零散接入。
2. 明确角色权限矩阵、envelope 字段、evidence 最小字段、errorCode、fail-fast 与 degraded 规则。
3. 明确 CompanyOverview/Fundamentals/Shareholder/Product/Social 各缺口的补齐顺序与 blocked 边界。

### R3
1. 把 runner 细化为真实 staged runtime，而不是 prompt orchestrator。
2. 明确每个阶段的执行方式、事件总线类型、并行 barrier、resume/rerun 规则与角色输入输出边界。
3. 单独列出不能偷懒的实现红线，防止 bull/bear/risk 退化成伪多角色文案。

### R4
1. 把工作台拆成 header、progress、feed、report、follow-up、replay、fail-fast modal 等清晰组件。
2. 明确每块 UI 应消费哪些 authoritative state，禁止前端自己从文本倒推状态。
3. 明确交互流、响应式骨架、错误态与 handoff 入口，避免 UI 再次退回聊天主舞台。

### R5
1. 明确 debate/risk/proposal/decision 都需要独立持久化对象，而不是藏在 report 文本里。
2. 补齐版本关系、引用链、查询面、replay comparison 与 superseded 规则。

### R6
1. 把 `Current Report` 和 `Final Decision` 明确为 authoritative object。
2. 细化 report block、nextAction、degraded/failure 表现和与现有图表/证据/交易计划链路的交接参数。

### R7
1. 明确 R7 是质量门禁总装配，而不是测试附录。
2. 细化合同测试、单测、集成测试、工作流测试、Browser MCP、桌面打包验证和回归清单。
3. 要求每轮验收必须记录命令、结果、阻塞与剩余风险。

## 自动化台账同步
1. 已更新 `.automation/tasks.json` 中 `GOAL-AGENT-NEW-001` 及 R1-R7 的备注，说明任务文件现已细化到可执行级别。
2. 已更新 `.automation/state.json`，当前 run 指向本次规划细化报告，避免状态仍停留在 P0 结项上下文。

## 验证
1. 命令：`Get-Content .\.automation\tasks.json -Raw -Encoding UTF8 | ConvertFrom-Json | Out-Null`
   - 结果：通过，任务台账 JSON 有效。
2. 命令：`Get-Content .\.automation\state.json -Raw -Encoding UTF8 | ConvertFrom-Json | Out-Null`
   - 结果：通过，状态文件 JSON 有效。
3. 诊断：检查本轮修改的 markdown/json 文件
   - 结果：未发现新的解析级错误。

## 后续执行建议
1. 直接按细化后的 R1 与 R2 开发，不要再从 R3 或 R4 倒推 contract。
2. 后续每完成一个切片，都应继续把测试与报告按这里的粒度同步补齐，避免再次出现“代码有了，子任务文档仍然只写几行”的情况。

## 本轮继续细化补充
1. 根据用户进一步要求，又把 R1-R7 全部继续下钻了一层，不再满足于“有详细开发拆解”这一层，而是补到了“每个切片如何与最终 PLAN 一一对应、交付物具体要交什么、测试要测到什么、回归要挡什么、完成要怎么判”。
2. 本轮重点补强的不是总入口，而是每个 R 文件内部的执行深度：
   - R1：补了与最终 PLAN 的对应关系、feed/report/replay 合同边界、API 细化到请求响应层、旧数据隔离规则、并发与 supersede 约束。
   - R2：补了与 MCP Foundation/MCP-First Policy 的直接对应、15 角色能力矩阵下一层拆解、Prompt/LLM readiness 实测要求、MCP 改造责任归属。
   - R3：补了 phase 0/company overview、阶段内控制流、微流式事件持久化、resume/rerun 执行规则与更细的交付物/测试门槛。
   - R4：补了与 UI 线框图和桌面/移动端布局的直接对应、桌面分区、移动端四 tab、空态/历史态、manager 收口消息与 authoritative state 的关系。
   - R5：补了结构化对象引用链、feed 映射、引用追问、superseded/归档规则，以及更细的对象/查询/比较交付物。
   - R6：补了八类 report block 的具体语义边界、manager reply 投影规则、动作交接链路与 block 级 degraded/failure 表达。
   - R7：补了与最终 PLAN 的直接对应、MCP readiness 与 Prompt 验收、中文输出与产品叙事验收、验收执行顺序细化。
3. 现在 R1-R7 不再只是“知道方向”，而是足以让后续开发代理在上下文收缩时仍能沿着同一条冻结主线推进，不容易偏回聊天助手、伪多 Agent、伪 replay 或伪 grounded report。