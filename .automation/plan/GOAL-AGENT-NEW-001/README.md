# GOAL-AGENT-NEW-001 总需求与约束

## 文档目的
1. 这份文件是 `GOAL-AGENT-NEW-001` 的唯一总入口，负责定义总需求、总约束、执行顺序、测试门禁和分任务链接。
2. 具体实现任务不再继续堆在单一大文件中，而是拆到独立任务文件，便于并行阅读、分工执行和逐项验收。
3. 这份文件不承载详细实现步骤；每个任务的细节、交付物、测试目标和回归要求都写在各自文件里。

## 总目标
1. 以 `noupload/TradingAgents-main` 为唯一主参考，重建一个真正的 `Trading Workbench`，而不是聊天式股票助手。
2. 保留 TradingAgents 的核心执行骨架：阶段推进、多角色讨论、治理出口、持续更新的 `Current Report`。
3. 输出必须结构化、可持久化、可回放、可联动现有图表、本地事实、证据抽屉和交易计划链路。
4. 默认支持同一标的下的多轮 follow-up，且 follow-up 必须承接当前 session，而不是伪连续对话。

## 顶层约束
1. 产品叙事必须是工作台，不得退回 one-to-one assistant。
2. `stage`、`turn`、`role`、`report`、`decision` 必须是真实状态对象，不得只是前端文案。
3. Bull/Bear debate、Risk debate、Manager/Portfolio 治理链路不可省略。
4. MCP 必须优先于通用 Web Search；`local_required` 工具失败时必须 fail-fast。
5. 除 Analyst 外，Researcher、Manager、Trader、Risk 等后置角色默认没有直接取数权限。
6. 输出语言默认中文；固定协议标记和工具参数保留英文原文。
7. 不允许暴露 raw chain-of-thought。
8. 不允许把“群聊式表现层”误当成真实执行模型。

## 当前冻结基线
1. 截至 2026-03-25，P0 已完成并作为后续所有实现切片的前置门禁。
2. 模块名称和产品定义已冻结为 `Trading Workbench`，不再允许回退为已被手动删除的旧 `Stock Copilot` / `GOAL-AGENT-002` 聊天叙事。
3. 用户可见主舞台已冻结为 `Team Progress + Discussion Feed + Current Report + Follow-up`。
4. `Current Report` 已冻结为 authoritative 主阅读区，不得退化为聊天流后的附属摘要。
5. 阶段主链路已冻结为：`Company Overview Preflight -> Analyst Team -> Research Debate -> Trader Proposal -> Risk Debate -> Portfolio Decision`。
6. 阶段内并行边界已冻结：analyst team 可并行，risk 三角色首轮可并行；bull/bear/manager、trader、portfolio decision 不得伪并行。
7. 角色权限边界已冻结：默认只有 analyst/overview 侧可直接取数；Researcher、Manager、Trader、Risk、Portfolio Manager 默认不得直接调用查询类工具。
8. 底座复用结论已冻结：股票页右侧扩展位、图表链路、本地事实、市场上下文、交易计划链路与 `StockAgentAnalysisHistories` 可以继续作为新方案接缝；旧 `Stock Copilot / GOAL-AGENT-002` 产品层不再视为复用前提。
9. 明确缺口已冻结：公司概览、基本面、股东结构、产品业务和 Social Sentiment 降级策略由 R2 负责补齐；即便仓库仍残留旧 `StockCopilot*` 命名实现，也不能把它们当成必须保留的兼容目标。

## 总体禁用清单
1. 禁止恢复 one-to-one assistant 作为默认产品叙事。
2. 禁止把多角色运行实现成“一次性最终回答里分段写 Bull / Bear / Risk / Manager”。
3. 禁止把 manager 角色退化成摘要器或润色器。
4. 禁止 follow-up 在后端偷偷新建 session，再由前端伪装成续接当前会话。
5. 禁止只保留 final answer，而不保留 turn/stage/role/tool/report/decision 的真实状态对象。
6. 禁止在缺乏真实数据源时，让 Product、Shareholder、Social 等角色在 UI 上假装已经完成 grounded 分析。

## 交付边界
1. 本目标只覆盖股票详情页右侧 `Trading Workbench` 模块，不改整个股票终端的主布局。
2. V1 不做真实下单、不做组合交易看板、不做用户自定义 agent graph。
3. V1 必须打通会话、任务编排、报告、回放、动作交接和测试闭环。

## 分任务索引
1. [GOAL-AGENT-NEW-001-P0-Pre](./GOAL-AGENT-NEW-001-P0-Pre.md)
   - 前置改造闸门与实测验证，把全部需要补充或扩展的 MCP 提为首先完成项。
2. [GOAL-AGENT-NEW-001-P0](./GOAL-AGENT-NEW-001-P0.md)
   - 对齐 TradingAgents、盘点可复用底座、冻结术语和偏离警戒线。
3. [GOAL-AGENT-NEW-001-P1](./GOAL-AGENT-NEW-001-P1.md)
   - Agent 提示词固化与全量中文化输出语言契约。
4. [GOAL-AGENT-NEW-001-R1](./GOAL-AGENT-NEW-001-R1.md)
   - 定义真实 session/turn/stage/decision contract 与持久化边界。
5. [GOAL-AGENT-NEW-001-R2](./GOAL-AGENT-NEW-001-R2.md)
   - 完成 MCP 能力矩阵、权限模型、缺口补齐和 fail-fast 规则。
6. [GOAL-AGENT-NEW-001-R3](./GOAL-AGENT-NEW-001-R3.md)
   - 落地后端 graph orchestration、辩论环路、阶段推进和事件总线。
7. [GOAL-AGENT-NEW-001-R4](./GOAL-AGENT-NEW-001-R4.md)
   - 实现 Trading Workbench 工作台 UI、follow-up 体验和 replay 视图。
8. [GOAL-AGENT-NEW-001-R5](./GOAL-AGENT-NEW-001-R5.md)
   - 持久化 debate/risk/approval/proposal versioning，保障可追溯和可比较。      
9. [GOAL-AGENT-NEW-001-R6](./GOAL-AGENT-NEW-001-R6.md)
   - 结构化 `Current Report`、`Final Decision` 和动作交接 contract。
10. [GOAL-AGENT-NEW-001-R7](./GOAL-AGENT-NEW-001-R7.md)
   - 定义测试目标、回归测试、Browser MCP、桌面打包验收和多维质量门禁。

## 执行顺序与依赖
1. P0-Pre和P0、P1 是所有实现任务的前置门禁；未通过前置实测和确认，不允许开始 R1-R7 的开发。
2. R1 和 R2 是 R3 的共同上游：没有 contract 和 MCP 能力矩阵，就不能启动真实编排。
3. R3、R5、R6 互相耦合，但顺序必须是：先 runner，后持久化细化，再 report/handoff authoritative schema。
4. R4 依赖 R1/R3/R5/R6 的对象稳定后再落 UI，避免 UI 先行带来大量返工。
5. R7 不是最后再补的“测试说明”，而是每个任务的验收门禁集合；任何任务完成前都必须满足自己文件中的测试目标。

## 子任务细化约束
1. P0 与 R1-R7 不允许只写“做什么”，必须同时写清“具体拆成哪些实现块、先改哪里、需要什么对象、怎么验证”。
2. 每个任务文件至少要覆盖 6 类内容：范围边界、详细开发拆解、对象或接口草图、实施顺序、失败与降级处理、测试与回归门禁。
3. 每个任务文件都必须明确哪些内容是本任务负责，哪些内容必须等待上游切片完成，防止开发代理跨层临时发明 contract。
4. 每个任务文件都必须写清楚 authoritative source 是什么，避免同一状态同时由 prompt 文本、临时前端状态和数据库三处各自为政。
5. 每个任务文件都必须指出与现有股票页、图表、本地事实、交易计划链路的接缝，防止实现时重新造一套孤立面板。
6. 每个任务文件都必须包含“不能偷懒的点”，明确哪些看似可运行但实际上会偏离 TradingAgents 工作台叙事。

## 统一开发拆解模板
1. 第一层写用户可见目标，说明完成后用户会看到什么变化。
2. 第二层写后端或前端真实状态对象，说明新增 DTO、实体、API、事件、组件或 store 分别负责什么。
3. 第三层写详细实施顺序，至少拆到“先落 contract/骨架，再接运行态，再接 UI 或持久化，再补测试”。
4. 第四层写异常路径，明确 fail-fast、degraded、fallback、retry、resume 分别在哪里处理。
5. 第五层写验收证据，明确应该跑哪些单测、集成测试、Browser MCP、打包验证。

## 跨任务对齐要求
1. R1 负责定义真实状态对象，R3/R4/R5/R6 只能消费或扩展这些对象，不能各自再造命名。
2. R2 负责定义工具权限和 envelope，R3 中任何 role 调用工具都必须通过 R2 的网关，不允许私接现有 service。
3. R5 持久化的 debate/risk/proposal/decision 必须与 R4 Feed 和 R6 Report 使用同一批 ID，不能出现 UI 重新拼接的镜像对象。
4. R6 的 report block 与 final decision 是 authoritative 输出；R4 的任何摘要文案都只能从这些对象派生，不能另写一套前端临时总结。
5. R7 不是额外附录，而是每个切片的门禁母表；开发时应把各任务自己的测试目标提前落成脚本或测试文件，而不是最后补文档。

## 实施顺序细化
1. 第一步先完成 R1：锁 session/turn/stage/role/report/decision 主 contract、API contract、数据库边界、错误传播边界。
2. 第二步完成 R2：把所有 analyst 可用数据源和工具权限收束到统一 MCP Manager / Tool Gateway 下，并明确 blocked 与 degraded 的差别。
3. 第三步完成 R3：按固定阶段顺序落地 runner、事件总线、并行栅栏、resume/rerun 规则，先让后端能独立跑完整流程。
4. 第四步完成 R5 与 R6：把 debate/risk/decision/report 从运行时内存对象变成稳定可查询、可比较、可 handoff 的持久化对象。
5. 第五步完成 R4：前端只消费已经稳定的 authoritative 对象，专注工作台布局、交互流和错误态，不再倒逼后端返工 contract。
6. 第六步执行 R7：把单测、集成测试、Browser MCP、必要时的 packaged desktop 验收收成统一门禁，并补齐回归清单。

## 当前状态
1. `GOAL-AGENT-NEW-001-P0` 已完成，详细规格见 [GOAL-AGENT-NEW-001-P0](./GOAL-AGENT-NEW-001-P0.md)。
2. 下一实现入口是 `R1` 与 `R2`：
   - `R1` 锁真实 `session/turn/stage/decision` contract 与持久化边界。
   - `R2` 锁 `MCP Manager / Tool Gateway`、角色权限模型和能力缺口补齐。
3. 在 `R1` 与 `R2` 完成前，不允许提前进入 `R3` 编排实现或 `R4` UI 实现。

## 统一测试策略
1. 每个任务文件都必须定义自己的：测试目标、最小验证、回归风险、完成标准。
2. 全局必须覆盖 8 类测试：
   - 合同测试：DTO、schema、枚举、字段约束、状态迁移。
   - 单元测试：角色编排、工具 envelope、规则判断、格式化逻辑。
   - 集成测试：MCP adapter、session runner、持久化查询、API contract。
   - 工作流测试：完整首轮研究、follow-up、局部 rerun、同 session 全量重跑。
   - 回归测试：历史 bug 路径、旧功能不被新 workbench 破坏、关键链路不退化为聊天。
   - Browser MCP 测试：backend-served 页面、真实点击、console/network 检查。
   - 桌面打包测试：涉及宿主链路时必须打包并验证 EXE 启动。
   - 稳定性测试：fail-fast、degraded path、并行 stage、长耗时微流式反馈。
3. 回归测试必须同时覆盖“新功能正确”和“旧能力未坏”两条线。
4. 所有实现任务默认需要补定向单测；UI 任务除单测外必须补 Browser MCP。
5. 与桌面启动、打包、运行路径相关的改动，必须触发 packaged desktop 验证。

## 总体验收门禁
1. 用户能创建或续接同一股票的真实 session。
2. 用户能看到真实 stage/role 推进，而不是一次性最终答案。
3. 用户能看到 bull/bear/risk/manager 的分歧和治理结果。
4. `Current Report` 在运行中持续更新，且是 authoritative 主阅读区。
5. `Final Decision` 能正确交接给图表、证据、本地事实和交易计划。
6. follow-up、partial rerun、full rerun、replay 都有真实状态承接。
7. 关键 MCP 失效时会立即中止流程并给出清晰阻断提示。
8. 多维测试、回归测试、Browser MCP 和必要时的桌面打包验证全部通过。

## 关联报告
1. 规划报告：`../../reports/GOAL-AGENT-NEW-001-PLAN-20260325.md`
2. TradingAgents 源码分析：`../../reports/GOAL-AGENT-NEW-001-TRADINGAGENTS-ANALYSIS-20260325.md`
3. P0 执行报告：`../../reports/GOAL-AGENT-NEW-001-P0-DEV-20260325.md`