# 给 ChatGPT-5.4 (开发人员) 的当前有效任务书

> 致 ChatGPT-5.4：
> 2026-03-22 更新：GOAL-AGENT-001 已完成，当前主线切换为 GOAL-AGENT-002 的 Copilot 产品层规划；GOAL-017 保留为并行量化底座线。
> 你的角色不是产品 owner，而是开发执行者；我负责指挥、拆任务、定边界、做 review。Dev1、Dev2 为并行开发人员。

---

## 当前协作模式

1. 我是指挥者：负责架构、contract、review、验收口径。
2. Dev1、Dev2 是开发执行者：按当前任务书各自完成代码、测试、报告。
3. 当前主线优先围绕 GOAL-AGENT-002 展开：先做运行态稳定性与输出安全闸门，再做股票 Copilot 的 session runtime、planner/governor 时间线、面板化 UX 和动作化工作流。
4. GOAL-017 继续存在，但当前是并行次优先底座线；不要让量化引擎深度先于 Copilot session contract 与产品层闭环。
5. GOAL-016-R6 已补成详细设计并进入“待其他 Agent 执行”的并行规划态，但它不是当前主线，不要因此打乱 GOAL-AGENT-002 的既定开发顺序。
6. 若需要追溯 GOAL-AGENT-001 或旧 Step 4.x 的实现细节，请查 `.automation/reports/`，不要把旧回执重新堆回本文件。

---

## 当前状态

1. GOAL-AGENT-001：已完成并转入归档参考态。
2. GOAL-AGENT-002：已完成父级规划，开发已启动。
3. GOAL-AGENT-002-P0：并行高优先级，当前由另一条修复线处理。
4. GOAL-AGENT-002-R1：已完成后端 session contract 与 draft timeline 切片。
5. GOAL-AGENT-002-R2：当前最自然的后续切片。
6. GOAL-017-R1：已完成详细设计，但当前优先级低于 GOAL-AGENT-002。
7. GOAL-016-R6：已完成详细设计，等待后续桌面/runtime 专线接手。

### 当前分派结果

1. GOAL-AGENT-001 的已完成交付继续作为 Copilot 产品层的底座，不再单独作为活跃开发主线。
2. 当前新的执行顺序是：`GOAL-AGENT-002-P0(并行) -> GOAL-AGENT-002-R2 -> GOAL-AGENT-002-R3 -> GOAL-AGENT-002-R4`。
3. GOAL-AGENT-002-R1 已作为后端 contract 基础完成，前端和 runtime 后续直接消费 `/api/stocks/copilot/turns/draft`。
4. GOAL-017 作为量化双引擎底座线，在 `GOAL-AGENT-002-R2` 开始后并行推进。
5. GOAL-016-R6 作为桌面宿主化深入切片，保持并行待命，不抢当前 Copilot 主线。

---

## GOAL-016-R6：单宿主单进程 packaged runtime

状态标签：`已规划，待其他 Agent 开发`

### 目标

把当前“桌面宿主 EXE + 独立 Backend 进程”的发布形态，收敛成真正的“一个主 EXE 统一控制应用生命周期”的单宿主、单进程本地应用，同时保持用户与测试者无需预装 SDK 或 .NET runtime。

### 非目标边界

1. 不把“磁盘上绝对只有一个文件”作为硬目标。
2. 允许应用携带自身管理的附属文件、前端静态资源、原生依赖和 Fixed Version WebView2 Runtime。
3. 本切片的重点是宿主化、生命周期、打包结构和 runtime 策略，不是重写整套前端为原生 UI。

### 核心约束

1. 主 EXE 必须成为唯一用户入口，并统一控制启动和关闭。
2. 后端不得再以独立后台进程长期存在；关闭桌面窗口时，后端 host 与后台服务应在同进程内一起停止。
3. 保留现有 localhost + WebView2 契约，优先避免大规模重写前端请求层。
4. 发布版仍需 self-contained，用户机器不需要额外安装 .NET runtime。
5. 如果继续使用 WebView2，则必须把 Fixed Version Runtime 的打包、升级与回滚策略纳入交付链路。

### 建议执行顺序

1. 先抽离后端宿主构建逻辑，把当前顶层 `Program.cs` 重构为可被桌面宿主调用的 host builder/runtime service。
2. 再让 `SimplerJiangAiAgent.Desktop` 直接在进程内启动和停止 ASP.NET Core host，删除 `Process.Start` 拉起 `Backend/` 的路径。
3. 然后重做 `publish-windows-package.ps1` 与安装器，使桌面 EXE 成为唯一主入口，不再输出独立可运行的 `Backend` 子进程形态。
4. 最后补齐 WebView2 Fixed Version Runtime 与升级/回滚验证。

### 验收口径

1. 干净 Windows 机器上，用户不需要安装 SDK/.NET runtime 即可启动。
2. 用户只操作一个主 EXE；关闭窗口后没有残留独立 Backend 进程。
3. 桌面启动后核心健康检查、首页、管理员登录和 LLM 首启引导都保持可用。
4. 安装包与便携包都能在不依赖系统预装 WebView2 的前提下稳定运行，或至少在文档/安装器中明确处理该依赖。

---

## 仍然生效的全局架构约束

1. 国内 A 股事实必须坚持 Local-First：公告、个股资讯、板块资讯、大盘事实优先由本地 C# 采集和数据库查询提供；不要把事实控制权重新交回“让 LLM 自己自由联网”。
2. 当前重构的目标不是“让模型更会说”，而是“让模型少猜、少编、少抢结论”。
3. commander 只能做综合判断层，不能继续做第二个新闻生成层，也不能引入上游没有引用过的新证据。
4. 高置信度结论必须依赖可回溯 evidence object，而不是只依赖漂亮文案。
5. evidence 的外部主字段应是 URL，但 URL 不是唯一约束；必须同时有 `source`、`publishedAt`、`url`、`title`、`excerpt`、`readMode`、`readStatus`，必要时再有内部 local record key。
6. “要求阅读全文”不是默认行为；只对公告、财报、监管文件、重大合同、业绩预告、以及会直接影响交易计划失效条件的新闻触发全文抓取。
7. 盘中或 degraded path 下，系统必须保守。JSON 修复、正文缺失、上游失败、证据不可追溯、信号冲突大时，confidence 必须被系统性压低。
8. 子 Agent 必须专职化，避免每个 Agent 都输出半套方向、风控和交易条件，从而制造伪共识。
9. 确定性特征优先在代码中计算，再交给 LLM 解释，不要继续让模型直接生吞长段原始 K 线和分时数组。
10. R3 上线前，系统仍然只能算“结构化分析组件”，不能对外宣称已经具备经过真实校准的概率判断能力。

---

## GOAL-AGENT-002：股票 Copilot 会话化编排与产品层

状态标签：`已规划，待开发`

### 目标

把现有 evidence / replay / MCP 底座收口成真正类似 GitHub Copilot 的股票协驾产品层，让系统从“可生成结构化分析”升级为“可会话化规划、可显示工具时间线、可给出下一步动作建议”的受控协驾体验。

### 活跃开发顺序

1. `GOAL-AGENT-002-P0`：运行态稳定性与输出安全闸门。
2. `GOAL-AGENT-002-R1`：会话 contract 与 planner/governor 时间线。
3. `GOAL-AGENT-002-R2`：股票 Copilot 面板 UX。
4. `GOAL-AGENT-002-R3`：动作化工作流集成。
5. `GOAL-AGENT-002-R4`：Copilot 验收与 replay 指标。

### 持续约束

1. Local-First 事实策略继续生效，CN-A 事实不可回退到自由联网主导。
2. planner 可以提议工具调用，但 commander 不得绕过 tool result/evidence 自行补新事实。
3. 产品层允许展示 plan、tool usage、evidence 和 degraded 状态，但不允许展示 raw chain-of-thought。
4. 图表/策略/数值推导继续来自确定性代码链路。
5. GOAL-017 是并行底座线，不能抢在 GOAL-AGENT-002 的会话 runtime 与产品闭环之前成为主开发顺序。

---

## GOAL-AGENT-002-P0：运行态稳定性与输出安全闸门

状态标签：`待开发（当前第一优先级）`

### 目标

在正式开放更复杂的 Copilot 会话 UX 前，先把当前运行态高优先级问题和输出安全问题收口，避免未来产品层建立在不稳定或脏输出之上。

### 输入

1. `.automation/buglist.md` 中当前高优先级问题。
2. Browser MCP 对 `股票信息`、`情绪轮动`、`股票推荐`、`治理开发者模式` 的真实运行态表现。
3. 已完成的 MCP、chart、news、developer-mode 现有链路。

### 输出

1. 修复直接阻塞 Copilot 的高优先级运行问题。
2. 修复图表轻链路失效与后端稳定性问题。
3. 修复用户面向结果中的 raw reasoning / non-JSON 泄漏。
4. 修复 developer-mode 审计 UI 对脏输出的无收口展示。

### 核心任务

1. 收掉 `情绪轮动` sectors API 500 之类会破坏市场上下文采集的运行问题。
2. 收掉股票图表不真正走 `/api/stocks/chart` 的链路回退问题。
3. 阻断股票推荐、developer-mode 等用户面向结果中的原始推理文本外泄。
4. 对首次查股后疑似后端崩溃风险做稳定性复现与修复。
5. 用 Browser MCP 做真实点击验收，不只做命令行 smoke test。

### 验收

1. `情绪轮动` 页面恢复可用。
2. 股票图表重新命中轻量 `/api/stocks/chart` 链路。
3. 推荐结果与开发者模式展示不再暴露 thought leak。
4. 查询与图表交互后后端稳定，健康检查持续通过。

---

## GOAL-AGENT-002-R1：会话 Contract 与 planner/governor 时间线

状态标签：`已完成（2026-03-22）`

### 目标

定义一轮股票 Copilot 会话的结构化 contract，让 planner、governor、commander 在同一 turn 内职责清晰、可审计、可复跑。

### 核心输出

1. `StockCopilotSessionDto`
2. `StockCopilotTurnDto`
3. `StockCopilotPlanStepDto`
4. `StockCopilotToolCallDto`
5. `StockCopilotToolResultDto`
6. `StockCopilotFinalAnswerDto`
7. `StockCopilotFollowUpActionDto`

### 已完成实现

1. 后端已新增上述 DTO，并统一放入 `StockAgentRuntimeModels.cs`。
2. 已新增 `IStockCopilotSessionService` / `StockCopilotSessionService`。
3. 已新增 `POST /api/stocks/copilot/turns/draft`，可把用户问题直接转成 planner/governor draft timeline。
4. draft service 已具备基础 question routing：识别 K 线 / 分时 / 策略 / 新闻 / 搜索意图。
5. `StockSearchMcp` 已在 draft 阶段应用 `external_gated` 审批，不会默认放行。
6. final answer contract 已明确标记 `needs_tool_execution`，并限制只能引用 tool result/evidence 中存在的事实。

### 验收结果

1. 隔离输出目录构建成功，避免运行中的本地 API 锁定默认 bin 目录。
2. `StockCopilotSessionServiceTests` 3/3 通过，覆盖：
	- session + timeline draft 生成，
	- 外部搜索默认拦截，
	- 显式授权后外部搜索可放行。

### 核心任务

1. planner 只负责分步计划与工具意图，不负责最终结论。
2. governor 负责审批哪些 MCP/tool 可以调用，以及何时必须走 local-first / external-gated。
3. commander 只消费已返回的 tool result、evidence、degradedFlags 给出结论。
4. final answer 中的事实必须都能回到 tool result 或历史 evidence。
5. 把 degradedFlags 对 confidence 和 action strength 的约束做成系统逻辑。

### 验收

1. 一轮会话内的 plan/tool/result/final answer 边界清晰。
2. 无工具结果支撑的事实无法进入最终回答。
3. 开发者可以复盘一轮 turn 中 planner 提了什么、governor 放行了什么、tool 返回了什么、commander 最后如何得出结论。

---

## GOAL-AGENT-002-R2：股票 Copilot 面板 UX

状态标签：`待开发`

### 目标

把股票页右侧侧栏从静态卡片升级为会话化 Copilot 面板。

### 核心任务

1. 增加问题输入框。
2. 增加 plan / timeline 可视化。
3. 增加工具调用卡片与结果摘要。
4. 增加 evidence/source 展示。
5. 增加 follow-up action chips。
6. 增加最近一轮会话回放能力。

### 验收

1. 用户体验从“点击一个分析按钮”升级为“提问 -> 看计划 -> 看工具 -> 看回答”。
2. 用户可以直接看到本轮用了哪些工具、证据来自哪里、下一步还能点什么。

---

## GOAL-AGENT-002-R3：动作化工作流集成

状态标签：`待开发`

### 目标

把 Copilot 回答接到图表、市场上下文、新闻证据和交易计划等真实工作流，形成可执行下一步动作。

### 核心任务

1. 统一 judgment / trigger / invalidation / risk / next-step answer contract。
2. 提供动作卡，例如 `看 60 日 K 线结构`、`检查今日分时承接`、`查看主线板块共振`、`起草交易计划`。
3. 让 Copilot 回答能驱动图表叠加、新闻证据查看、市场上下文查看、计划草稿生成。

### 验收

1. Copilot 不只是回答问题，还能建议下一步怎么查、怎么验证、怎么起草动作。
2. 用户能从一轮问答自然进入图表、新闻和交易计划工作流。

---

## GOAL-AGENT-002-R4：Copilot 验收与 replay 指标

状态标签：`待开发`

### 目标

把股票 Copilot 的新产品层纳入可量化验收，形成后续迭代的硬基线。

### 核心任务

1. 统计工具调用效率与无效调用比例。
2. 统计 evidence 覆盖率与 final answer traceability。
3. 统计 local-first 命中率与外部搜索触发率。
4. 统计 action card 使用质量与 session replay 可复跑性。

### 验收

1. 后续改 prompt/runtime/UX 时，可以明确回答哪些指标变好了、哪些变差了。
2. Copilot 层不再只靠主观体验验收，而有可留档、可对比的产品指标。

---

## 归档说明

1. 旧的 Step 4.x、图表策略、GOAL-009、GOAL-012 详细任务书已进入归档，不再作为当前主开发清单。
2. GOAL-AGENT-001 详细实现边界也已进入归档参考态；如需复盘，请去 `.automation/reports/` 查对应报告。
3. 本文件当前只维护 GOAL-AGENT-002 的活跃指挥信息，以及与之直接相关的全局架构约束。
