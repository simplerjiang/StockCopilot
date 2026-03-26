# GOAL-AGENT-NEW-001-P0-Pre MCP 前置改造闸门与实测验证

## 任务目标
1. 把所有“需要补充 / 优化 / 修改 MCP”的事项提升为最高优先级任务。
2. 通过真实调用和真实模型验证，确认 MCP 能拿到数据。
3. 确认 Prompt 已明确写入 MCP 使用方式，且 LLM 确实理解并执行该规则。
4. 提供关键 MCP 实测可用性报告，确保作为 R1-R7 的前置阻断门禁。

## 上游依赖
无，这是真正动手实现前的绝对前置闸门。

## 下游影响
1. 如果该阶段未通过，直接阻塞 P0 之后的任何任务（不允许进入 R1 及后续任何实现任务）。
2. 提供给 R2 基于事实的 MCP 缺口与降级依据。

## 详细执行拆解

### 一、全面盘点与前置任务总表建立
1. 汇总一张 `MCP 前置任务总表`，把所有需要补充、优化、修改、新增、扩展字段、补权限、补降级协议、补错误处理的 MCP 事项放在所有任务最前列，不允许散落在后续章节里才处理。

### 二、真实可用性检查 (Ground Truth Check)
1. 对所有 `local_required` 和关键 analyst 依赖的 MCP 做**真实可用性检查**：必须实际调用接口或工具，验证是否真能拿到数据，而不是只根据代码里“看起来有 service / endpoint”就判定可用。
2. 对每个关键 MCP 记录最小实测结果：
   - 是否成功？
   - 返回是否为空？
   - 关键字段是否齐全？
   - 时间戳是否新鲜？
   - evidence / source 是否可回溯？
   - 失败时错误码是否可识别？
3. 对角色入口能力逐一判断真实现状。例如 `Company Overview`、`Fundamentals`、`Shareholder`、`Product`、`News`、`Market`：当前到底是“直接可用”、“需要优化字段”、“需要改 adapter”、“需要新增 MCP”，还是“只能 fallback”。

### 三、Prompt 与 LLM 行为契约实测
1. 逐个检查角色 Prompt / 系统 Prompt 是否已经明确写入 MCP 使用规则：
   - 先用哪个 MCP？
   - 何时允许 fallback？
   - 何时必须停止？
   - 哪些角色禁止直接调用工具？
   > 没有写进 Prompt 的，视为未完成。
2. 必须用**环境内可用的 LLM key 实测**这套 Prompt 契约：
   - 验证模型是否会优先选择系统 MCP。
   - 验证模型是否理解 MCP 使用顺序。
   - 验证模型是否能在本地 MCP 无数据时再走 fallback。
   - 验证模型是否会避免越权调用不属于当前角色的工具。
3. 验证标准：对 Prompt 实测不能只看“模型回复看起来像懂了”，必须看真实 tool call / runner 轨迹，确认模型的工具选择与文档约束完全一致。

### 四、Blocker 阻断规则
1. 若发现某个 MCP 拿不到数据（尤其是 `local_required`）。
2. 若发现 Prompt 没写清楚 MCP 用法。
3. 若发现 LLM 实测不能稳定理解并执行 MCP 规则。
**一旦触发以上任意一项，后续任务必须标记为 blocked。必须先回到 MCP / Prompt 层修正，绝对不允许硬着头皮继续做工作流或 UI。**

## 本阶段交付物
1. `MCP 前置任务总表`（此表必须在所有开发任务的最前面）。
2. `关键 MCP 实测可用性报告`。
3. `Prompt 中 MCP 使用规则检查表`。
4. `基于环境内 LLM key 的 MCP 理解与调用实测报告`。

## 完成标准
1. 后续阶段开始前，关键 MCP 已经完成真实拿数验证，而不是停留在代码推断。
2. Prompt 中已经明确写入 MCP 使用顺序、fallback 条件、停止条件和角色权限边界。
3. 使用环境内 LLM key 的实测结果证明：模型能理解并执行 MCP 使用 Prompt，而不是只在文档中看起来正确。
4. 任一关键 MCP、Prompt 或 LLM 实测未通过时，后续任务不得开工。

## 2026-03-26 当前实测结论
1. 当前环境下，P0-Pre 前置 gate 已通过，不再应写为 `blocked`。
2. 本轮 current state 已收敛为三类结论：
   - 当前仓库事实已能确认 `McpToolGateway`、`McpServiceRegistry`、`RoleToolPolicyService`、`IStockCopilotMcpService`、`IStockCopilotSessionService`、`/api/stocks/mcp/*` 与 `/api/stocks/copilot/turns/draft` 均已落地；且独立验收已补证 backend-served 最小运行态 readiness，已实际打通 `GET /api/health`、`GET /api/stocks/mcp/product?symbol=sh600000&taskId=phase-e-acceptance` 与 `POST /api/stocks/copilot/turns/draft`。readiness gate 作为历史前置闸门仍保留记录，但当前不再是 blocker。
   - 当前机器已确认 `backend/SimplerJiangAiAgent.Api/App_Data/llm-settings.local.json` 存在，active provider 为 `default`，有效 key 来自 local secret，而不是 tracked `llm-settings.json` 或环境变量；因此“基于环境内可用 LLM key 的真实工具轨迹验证”已在当前环境完成。
   - `CompanyOverviewMcp`、`StockFundamentalsMcp`、`StockShareholderMcp`、`MarketContextMcp`、`SocialSentimentMcp`、`StockProductMcp` 虽已存在，但部分 MCP 仍有数据覆盖或业务可用性缺口；当前应视为“骨架已通、质量待继续补强”的 backlog，而不是阻断 R1-R7 的结构性 blocker。

## 2026-03-26 当前证据摘要
1. 代码侧已确认存在：
   - `StockCopilotMcpService` 与 `StockCopilotSessionService` 均已在 `StocksModule` 中注册，且 `StockCopilotSessionService` 已体现 planner / governor / commander 的受控 loop、`allowExternalSearch` 开关，以及“先 local-first，后决定是否外部搜索”的约束。
   - `McpToolGateway`、`McpServiceRegistry`、`RoleToolPolicyService` 与 `StockAgentRoleContractRegistry` 已存在，仓库已具备统一工具网关、统一权限边界和统一 tool contract 清单。
   - `/api/stocks/mcp/company-overview`、`/api/stocks/mcp/product`、`/api/stocks/mcp/fundamentals`、`/api/stocks/mcp/shareholder`、`/api/stocks/mcp/market-context`、`/api/stocks/mcp/social-sentiment` 以及既有 `/api/stocks/mcp/kline|minute|strategy|news|search` 路由均已暴露，`/api/stocks/copilot/turns/draft` 也已存在。
   - `IStockFundamentalSnapshotService` / `EastmoneyFundamentalSnapshotService`，说明基本面与股东相关事实已有上游数据能力。
   - `IStockMarketContextService` / `StockMarketContextService`，说明市场上下文已有上游服务能力。
   - `StockSearchService`、`StockCompanyProfiles`、`/api/stocks/detail/cache` 等普通 API / 数据表接缝，说明公司基础画像已有数据底座；对应 `CompanyOverviewMcp` 已形成正式工具入口。
   - `StockMcpToolNames` 与角色 contract 已明确纳入 `CompanyOverviewMcp`、`StockFundamentalsMcp`、`StockShareholderMcp`、`MarketContextMcp`、`SocialSentimentMcp`、`StockProductMcp`，Shareholder Analyst 已不是“待独立 MCP 补齐”的状态。
2. 代码侧当前已确认的剩余缺口：
   - 本轮最小运行态 evidence 已覆盖 `health`、`product mcp`、`draft route` 三个 backend-served 关键入口，足以证明最小 readiness 已补证；但这不等同于全量 Browser MCP 验收，也不应夸大为所有角色链路都已完成端到端实测。
   - `CompanyOverviewMcp`、`StockFundamentalsMcp`、`StockShareholderMcp` 已通跑，但仍存在已知业务缺口：公司主营与赛道文字语料不足、基本面财务指标覆盖不足、股东字段与 fundamentals 职责仍有交叠。
   - `SocialSentimentMcp` 当前是基于本地 proxy 的 v1 降级 contract，不代表真实社媒源已经稳定接入；若未来仍无真实稳定源，必须继续按 degraded 或 blocked 处理，不能假装成功。
3. Prompt 侧当前已存在 15 角色显式 MCP 使用契约与 checklist；且新的 `POST /api/stocks/copilot/live-gate` 已把这些规则真实写入 LLM 可见 prompt，并在后端对模型计划做 contract 校验与 tool trace 回传。
4. 当前机器已复用现有 `http://localhost:5119` 实例完成真实 smoke：`GET /api/health` 返回 200 `{"status":"ok"}`；`POST /api/stocks/copilot/live-gate` 在 `symbol=sh600000`、`allowExternalSearch=false`、问题为“看下浦发银行日线结构和本地新闻证据”下成功返回 `LlmTraceId = 7c254306ff89470d8ca971c08aab3090`、`FinalAnswerStatus = done`、`RejectedToolCallCount = 0`、`Acceptance.ExecutedToolCallCount = 3`，并包含至少 3 个非空 tool trace ids，对应 `MarketContextMcp`、`StockKlineMcp`、`StockNewsMcp`。`LLM-AUDIT` 与 admin trace 聚合均已命中真实 `request/response` 证据。因此当前环境下的 live LLM key / tool trace gate 已通过；需要保留的边界仅剩 tracked `llm-settings.json` 继续保持无 secret，其他机器仍需通过 local secret 或环境变量自行配置 provider。

## 2026-03-26 当前 gate 与缺口分级
### A. 当前环境已完成的绝对前置 gate
1. live LLM key / tool trace gate
   - 现状：代码侧 gate 已落地，新的 live gate runner 会调用 `ILlmService`、校验模型计划、执行授权 MCP，并把 LLM traceId 与 tool traceId 一起回传；当前机器已确认 `llm-settings.local.json` 存在、active provider 为 `default`、有效 key 来自 local secret，并已完成一次真实 provider smoke。
   - 影响：在当前环境下，P0-Pre 不再 blocked，可进入后续 R1-R7。需要继续保持的是 tracked `llm-settings.json` 不存 secret；其他机器若没有 local secret 或环境变量，仍需自行完成 provider 配置后再复现该 smoke。

### B. 已完成最小实测、当前不再阻断的历史闸门
1. backend-served 运行态 readiness gate
   - 现状：独立验收已完成最小运行态补证，已打通 `GET /api/health`、`GET /api/stocks/mcp/product?symbol=sh600000&taskId=phase-e-acceptance`、`POST /api/stocks/copilot/turns/draft`。
   - 影响：该 gate 历史上仍是前置门禁，但截至当前口径已不再构成 R1-R7 的绝对阻断项；后续若继续补 Browser MCP 或更完整链路验收，属于增强置信度的补强而不是当前 preflight blocker。

### C. 非结构性但必须继续补强的 MCP 缺口
1. `CompanyOverviewMcp` / `StockFundamentalsMcp` / `StockShareholderMcp`
   - 现状：三个 MCP 已存在、已接入统一 gateway 和 HTTP 入口，不再属于“当前未实现”。
   - 剩余问题：公司主营与赛道文字语料不足、基本面财务指标覆盖不足、股东字段需继续与 fundamentals 去重分责。
2. `MarketContextMcp`
   - 现状：已存在独立 MCP 与路由，不再属于“需决定是否独立工具化”。
   - 剩余问题：仍需在真实运行态中继续验证 freshness、evidence 与下游复用效果。
3. `SocialSentimentMcp`
   - 现状：已存在正式 MCP 与角色契约，但当前以本地 proxy / 本地新闻情绪构成 v1 降级 contract。
   - 剩余问题：如果未来没有真实稳定社媒源，必须继续保持 degraded 或 blocked，不得在文档或 UI 中假装已完成真实社媒覆盖。

### D. 当前已落地且不应再被写成“缺失”的底座
1. `McpToolGateway` / `McpServiceRegistry` / `RoleToolPolicyService`
   - 现状：已在仓库与 DI 中落地，当前应视为 Trading Workbench 的既有底座。
   - 结论：后续文档不得再把它们写成“仓库内不存在统一工具网关/权限边界”。
2. MCP / draft 运行态入口
   - 现状：`/api/stocks/mcp/*` 与 `/api/stocks/copilot/turns/draft` 路由已存在。
   - 结论：后续文档不得再把这些入口写成“未暴露”；未通过的只是运行态实测 gate。
3. `CompanyOverviewMcp`、`StockFundamentalsMcp`、`StockShareholderMcp`、`MarketContextMcp`、`SocialSentimentMcp`
   - 现状：均已存在正式工具名、统一 gateway 接缝与后端 HTTP 入口。
   - 结论：后续文档应把问题表述为“数据质量 / 覆盖度 / 降级策略仍待补强”，而不是“当前未实现”。

### E. Phase E 当前收口与剩余 backlog
1. `StockProductMcp`
   - 现状：最小 `StockProductMcp` 已存在并可用，当前 contract 以 `经营范围 / 所属行业 / 证监会行业 / 所属地区` 为主。
   - 限制：`主营业务(zyyw)` 在 live probe 中不稳定且普遍为空，richer product composition / segment breakdown 尚未确认稳定上游。
   - 结论：Product 已不再是 P0-Pre blocker；更细粒度的产品结构扩展继续留在 `GOAL-AGENT-NEW-001-P0-Pre-Phase-E-R1` backlog，不允许把当前最小 contract 误写成完整产品拆分能力。

## 2026-03-26 当前已完成能力与剩余开发顺序
### 已完成并应视为 current state 的 Phase A-D 底座
1. Phase A 基础设施已落地：`IStockCopilotMcpService`、`IStockCopilotSessionService`、`IMcpToolGateway`、`IMcpServiceRegistry`、`IRoleToolPolicyService` 已接入 `StocksModule`。
2. Phase B 统一入口已落地：现有 `StockKlineMcp`、`StockMinuteMcp`、`StockStrategyMcp`、`StockNewsMcp`、`StockSearchMcp` 已处于统一 gateway / route 体系下。
3. Phase C 新 MCP 骨架已落地：`CompanyOverviewMcp`、`StockFundamentalsMcp`、`StockShareholderMcp` 已具备正式工具名、HTTP 路由与角色契约。
4. Phase D 边界 MCP 已落地：`MarketContextMcp` 与 `SocialSentimentMcp` 已存在；其中 Social v1 明确按降级 contract 运行，而不是空白缺位。

### Phase E：收口最小 Product contract 并保留 richer segmentation backlog
1. `StockProductMcp` 已作为独立任务收口，不与其他 MCP 混写。
2. 当前最小 contract 仅以 `经营范围 / 所属行业 / 证监会行业 / 所属地区` 为主；`主营业务(zyyw)` 不稳定时不得当作必填。
3. richer product composition / segment breakdown 继续留在 `GOAL-AGENT-NEW-001-P0-Pre-Phase-E-R1`，但 Product 已不再作为 P0-Pre blocker。

### Phase F：Prompt 与 LLM gate
1. 为可取数角色补 Prompt 检查表：先用哪个 MCP、何时 fallback、何时 stop、最低 evidence 数。
2. 为非取数角色补“禁止调用工具”的显式契约。
3. 已新增新的 `POST /api/stocks/copilot/live-gate` runner，直接走 `ILlmService` 生成真实 `LLM-AUDIT` trace，并在后端强校验模型 tool plan，只执行被批准 MCP。
4. 当前环境已完成一次真实 provider smoke，验证新 runner 的 live trace、tool trace 与 acceptance baseline 均能落审计并回传到 runtime 结果。
5. 因当前环境 smoke 已通过，P0-Pre 在本机已从 blocked 变为 pass；跨机器复现时仍要求 local secret 或环境变量先完成 provider 配置。

## 2026-03-26 后续补救顺序
1. backend-served 最小运行态 readiness 已由独立验收补证；如后续需要更高置信度，可继续补 Browser MCP 或更完整链路验收，但这不再是当前 preflight blocker。
2. 对 `CompanyOverviewMcp`、`StockFundamentalsMcp`、`StockShareholderMcp` 做定向补强，优先解决主营描述不足、财务指标缺失与职责交叠问题。
3. 持续完善 `MarketContextMcp` 与 `SocialSentimentMcp` 的 freshness / evidence / degraded 语义；若未来没有真实稳定社媒源，继续按 blocked 或 degraded 管理。
4. 保持最小 `StockProductMcp` contract 当前可用，同时把 richer product composition / segment breakdown 继续放入 `GOAL-AGENT-NEW-001-P0-Pre-Phase-E-R1`。
5. 维持 tracked `llm-settings.json` 无 secret；其他机器若需复现 live gate 验收，须通过 `llm-settings.local.json` 或环境变量补入本地 provider key，再执行同一 smoke 请求。

## 2026-03-26 Phase C 缺陷与实测复核 (sh601899)
本节保留 2026-03-26 基于 sh601899 紫金矿业真实探针得到的**历史缺陷记录**，用于解释 Phase C 当时为何仍需 residual 收口；**以下三项已不应再被理解为当前现状**。后续 Phase C residual 与 Phase E 改动已完成对应修复或缓解，并已有测试覆盖与文档纠偏：

1. **历史缺口：`StockFundamentalsMcp` 财务指标覆盖不足**
   - 当时结论：Phase C 初始版本只打通了骨架与最表层公司概况事实，财报快照事实不足以支撑 Fundamentals Analyst 的最小分析面。
   - 后续收口：已通过 Eastmoney 财报快照接入，把营业收入、净利润、EPS、ROE、毛利率等最小可用财务指标补入 `StockFundamentalsMcp`，不再属于“财务面空白”的状态。
   - 当前口径：该项已从“核心未满足”收敛为“覆盖度仍可继续增强”，不再是 P0-Pre 当前 blocker。
2. **历史缺口：`CompanyOverviewMcp` 主营/经营范围语料不足**
   - 当时结论：Phase C 初始探针暴露出公司画像缺少主营业务与经营范围等关键文字描述，Company Overview Analyst 难以做业务与赛道定性。
   - 后续收口：已通过 `EastmoneyCompanyProfileParser` 与后续字段补齐，把主营相关文字、经营范围及相关公司概况事实纳入 `CompanyOverviewMcp` 的最小可用 contract。
   - 当前口径：该项已从“缺少核心业务描述”修复为“已有最小可用主营/经营范围语料，后续只剩 richer product segmentation 待扩展”。
3. **历史缺口：`Fundamentals` / `Shareholder` 职责边界交叠**
   - 当时结论：Phase C 初始版本里，`StockFundamentalsMcp` 与 `StockShareholderMcp` 曾出现股东户数、集中度、户均持股等事实边界混写，导致工具职责不清。
   - 后续收口：已通过事实边界拆分、字段去重与回归测试，把股东侧事实收敛到 `StockShareholderMcp`，把基本面财务事实收敛到 `StockFundamentalsMcp`。
   - 当前口径：该项已完成职责拆分收口，不再属于当前需要继续用“数据冗余/职责交叠”口吻描述的未修复问题。

**当前结论**：本节记录的是 Phase C 历史缺陷，而非 current state。当前 P0-Pre 的剩余问题已不再是上述三项。当前环境下 live gate 已完成真实 provider smoke 并通过，不再构成 blocker；跨机器复现时仍需通过 local secret 或环境变量完成 provider 配置。另外 richer product segmentation 仍保留在 `GOAL-AGENT-NEW-001-P0-Pre-Phase-E-R1` backlog，但已不再构成当前 preflight blocker。
