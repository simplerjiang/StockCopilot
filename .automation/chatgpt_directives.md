# 给 ChatGPT-5.4 (开发人员) 的当前有效任务书

> 致 ChatGPT-5.4：
> 2026-03-25 更新：GOAL-AGENT-002 已被用户明确废弃；当前主线切换为 GOAL-AGENT-NEW-001，目标是完全参照 `noupload/TradingAgents-main` 重建新的多角色股票 Copilot。如与我们原有需求、旧 README 文案或旧 Copilot 设计不同，以 TradingAgents 为准。GOAL-017 保留为并行量化底座线。
> 你的角色不是产品 owner，而是开发执行者；我负责指挥、拆任务、定边界、做 review。Dev1、Dev2 为并行开发人员。

---

## 当前协作模式

1. 我是指挥者：负责架构、contract、review、验收口径。
2. Dev1、Dev2 是开发执行者：按当前任务书各自完成代码、测试、报告。
3. 当前主线优先围绕 GOAL-AGENT-NEW-001 展开：先锁定 TradingAgents 对齐边界和可复用底座，再做真实多角色 session contract、MCP adapter、graph orchestration、TradingAgents 式 UI 与验收闭环。
4. GOAL-017 继续存在，但当前是并行次优先底座线；不要让量化引擎深度先于新的多角色 Copilot 产品层闭环。
5. GOAL-016-R6 已补成详细设计并进入“待其他 Agent 执行”的并行规划态，但它不是当前主线，不要因此打乱 GOAL-AGENT-NEW-001 的既定开发顺序。
6. 若需要追溯 GOAL-AGENT-001 或旧 Step 4.x 的实现细节，请查 `.automation/reports/`，不要把旧回执重新堆回本文件。

---

## 当前状态

1. GOAL-AGENT-001：已完成并转入归档参考态。
2. GOAL-AGENT-002：已被用户手动彻底删除；除历史报告外，不再作为实现方向、可复用底座或产品参考。
3. GOAL-AGENT-NEW-001：已完成详细规划，当前主线正式切换到该目标。
4. GOAL-AGENT-NEW-001-P0：下一步先做 TradingAgents 对齐规格与底座盘点。
5. GOAL-AGENT-NEW-001-R1/R2：作为首批实现切片，分别负责真实会话 contract 与 MCP adapter matrix。
6. GOAL-017-R1：已完成详细设计，但当前优先级低于 GOAL-AGENT-NEW-001。
7. GOAL-016-R6：已完成详细设计，等待后续桌面/runtime 专线接手。

### 当前分派结果

1. GOAL-AGENT-001 的已完成交付继续作为新 Copilot 的底座，不再单独作为活跃开发主线。
2. 当前新的执行顺序是：`GOAL-AGENT-NEW-001-P0 -> GOAL-AGENT-NEW-001-R1 -> GOAL-AGENT-NEW-001-R2 -> GOAL-AGENT-NEW-001-R3 -> GOAL-AGENT-NEW-001-R4 -> GOAL-AGENT-NEW-001-R5 -> GOAL-AGENT-NEW-001-R6 -> GOAL-AGENT-NEW-001-R7`。
3. 旧 GOAL-AGENT-002 的实现不再作为默认底座前提；即便仓库里残留同名类、测试或 DTO，也只按待清理遗留处理，不能继续按旧 UI 或旧会话模型叠代。
4. GOAL-017 作为量化双引擎底座线，在 GOAL-AGENT-NEW-001 进入稳定实现后并行推进。
5. GOAL-016-R6 作为桌面宿主化深入切片，保持并行待命，不抢当前新 Copilot 主线。

### 新主线原则

1. 产品形态、角色流程和工作台叙事直接以 `TradingAgents-main` 为准。
2. 如与我们原有需求、旧 README 文案或旧 Copilot 方案冲突，以 TradingAgents 为准。
3. 不允许再做“一个 assistant 假装多个角色”的伪多角色实现。
4. 不允许再做“每次追问都自动新开会话”的伪连续对话实现。

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

## GOAL-AGENT-002：归档与删除状态

状态标签：`已被用户手动彻底删除`

1. GOAL-AGENT-002 不再是当前任务书的一部分。
2. 旧 `Stock Copilot`、旧会话模型、旧聊天式主舞台、旧 Copilot UX 不得再作为实现目标或默认参考。
3. 即便仓库里仍残留 `StockCopilot*`、`GOAL-AGENT-002-*`、`/api/stocks/copilot*`、`/api/stocks/mcp*` 等同名代码、测试、DTO 或注释，也只按待清理遗留处理，不视为“当前仍保留的有效底座”。
4. 如需追溯历史过程，只能查 `.automation/reports/` 中的归档报告；当前产品与开发主线统一以 `GOAL-AGENT-NEW-001` 为准。

---

## 归档说明

1. 旧的 Step 4.x、图表策略、GOAL-009、GOAL-012 详细任务书已进入归档，不再作为当前主开发清单。
2. GOAL-AGENT-001 详细实现边界也已进入归档参考态；如需复盘，请去 `.automation/reports/` 查对应报告。
3. 本文件当前只维护 GOAL-AGENT-NEW-001、GOAL-017、GOAL-016-R6 等仍有效的主线与全局架构约束；GOAL-AGENT-002 已转入删除归档态。
