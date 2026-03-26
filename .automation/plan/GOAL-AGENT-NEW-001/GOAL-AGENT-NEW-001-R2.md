# GOAL-AGENT-NEW-001-R2 MCP 能力矩阵与缺口补齐

## 任务目标
1. 把角色的数据边界映射到本仓库的 MCP 和本地事实能力。
2. 建立统一 `MCP Manager / Tool Gateway`、角色级权限模型和 fail-fast 协议。
3. 补齐 Company Overview、Fundamentals、Shareholder、Product、Social Sentiment 等关键缺口。

## 上游依赖
1. [P0](./GOAL-AGENT-NEW-001-P0.md)

## 下游影响
1. 为 R3 提供可调用、可观测、可降级的工具层。
2. 为 R6 提供 evidence/tool event authoritative 来源。
3. 为 R7 提供 MCP readiness 和容错验收基线。

## 与最终 PLAN 对应关系
1. 对应总 PLAN 中 `R2. MCP Adapter Matrix 与缺口补齐 (本地 MCP 优先策略)`、`P0.5 MCP Foundation`、`P0-Pre MCP 前置改造闸门` 的实现层规格。
2. 本任务必须落实 `MCP-First Policy`：Analyst 家族优先使用本地 MCP，本地无数据或明确错误时才允许受控 fallback；Researcher、Manager、Trader、Risk、Portfolio Manager 默认无查询类工具权限。
3. 本任务还必须落实 PLAN 中的 Prompt 检查、环境内 LLM key 实测、关键 MCP readiness 报告与 `local_required` fail-fast 规则。
4. 若角色表与能力矩阵不闭合，该角色只能标记 blocked，不允许在 UI 或文档里伪装成已支持。

## 核心工作项
1. 定义 `MCP Manager / Tool Gateway` 抽象层。
2. 定义角色级工具权限和 tool group 级授权模型。
3. 形成角色能力矩阵：Company Overview、Market、Social、News、Fundamentals、Shareholder、Product。
4. 补齐缺失 MCP 或 adapter：`CompanyOverviewMcp`、`StockFundamentalsMcp`、`StockShareholderMcp`、`StockProductMcp`，以及 Social Sentiment 的降级方案。
5. 定义 tool envelope、evidence 最小字段、freshness 规则、degraded flags。
6. 对 `local_required` 工具定义 fail-fast 和 machine-readable 错误码。
7. 完成真实 MCP 拿数验证、Prompt 使用规则检查和基于环境 LLM key 的工具调用实测。

## 详细开发拆解

### 一、先统一工具入口，再补缺口工具
1. 所有角色取数必须先经过统一 `MCP Manager / Tool Gateway`，不允许某个 role 直接 new service 或直接查数据库。
2. 工具层要先定义统一请求上下文：`sessionId`、`turnId`、`stageId`、`roleType`、`toolPolicyClass`、`traceId`、`requestedSymbol`、`freshnessRequirement`。
3. 工具层要统一返回 envelope：`status`、`warnings`、`degradedFlags`、`evidence`、`payload`、`latencyMs`、`source`、`errorCode`。
4. 在统一网关稳定之前，不要急着补 Product 或 Shareholder 工具，否则后面仍会出现每个工具各自返回不同字段的问题。

### 二、角色权限模型拆解
1. 先按 P0 角色表把角色分成 3 层：可直接取数层、可受控 fallback 层、禁止直接取数层。
2. Company Overview、Analyst Team 属于可直接取数层，但也要区分 `local_required`、`local_preferred` 与 `external_gated`。
3. Bull/Bear/Manager/Trader/Risk/Portfolio Manager 默认是禁止直接取数层，只能消费前序角色产出的 grounded artifact。
4. 权限不仅要靠 prompt 描述，还要在工具网关做服务端校验；若角色未授权调用工具，必须返回明确错误码而不是悄悄放行。
5. 权限表需要按 tool group 维护，而不是对每个 tool 手写 if/else，避免工具数一多就失控。

### 三、缺口工具补齐顺序
1. 第一优先级是 `CompanyOverviewMcp`，因为它决定 symbol 识别、公司基础上下文和 analyst 输入基线。
2. 第二优先级是 `StockFundamentalsMcp` 和 `StockShareholderMcp`，因为它们已有部分现有能力可封装，开发风险较低。
3. 第三优先级是 Social Sentiment 降级方案，要先写清哪些数据是真的、哪些只是新闻近似替代，不允许 UI 伪装成“已完成社媒分析”。
4. 最后才是 `StockProductMcp`，如果没有真实稳定数据源，本阶段必须明确 blocked，而不是做一个空壳 MCP 欺骗下游角色。

### 四、tool envelope 与 evidence 字段拆解
1. 所有工具统一至少回传：`toolName`、`traceId`、`requestedAt`、`completedAt`、`status`、`sourceType`、`freshness`、`warnings`、`degradedFlags`。
2. `evidence` 最小字段至少包含：`evidenceId`、`title`、`source`、`publishedAt`、`url`、`snippet`、`symbolScope`、`relevanceReason`、`freshnessTag`。
3. 对结构化行情或指标数据，也要生成可引用 evidence，而不只是返回原始 payload；否则后续 debate/report 仍然无法做证据链接。
4. `payload` 可以因工具不同而不同，但 envelope 外层字段必须稳定，供 feed、日志、report、replay 共用。

### 五、fail-fast 与 degraded 规则拆解
1. `local_required` 工具失败时，网关必须立刻返回 machine-readable `errorCode`，例如 `tool.local_required_unavailable`，并阻断当前 role 或 stage。
2. `local_preferred` 工具失败时可以允许受控降级，但必须在 envelope 中写出 `degradedFlags`、`fallbackSource` 与 `confidenceImpact`。
3. `external_gated` 工具不允许自动触发，必须在 role policy 与调用请求中明确被批准，且要记录审批来源或理由。
4. 所有工具失败都必须形成可进入 feed 的 `tool event`，而不是只出现在日志里。

### 六、15 角色能力矩阵下一层拆解
1. `Company Overview Analyst`：必须能获得 symbol resolve、交易所/市场识别、行业、主营概览、公司别名、标准化 `company_details`；缺任何一项都不能视为完成。
2. `Market Analyst`：必须以本地 K 线、分时、技术指标、市场上下文为主数据链，禁止直接退化成 Web Search 分析。
3. `Social Sentiment Analyst`：必须明确是“真实社媒源”还是“新闻近似替代 + external_gated fallback”，不能用模糊文案掩盖数据缺口。
4. `News Analyst`：必须优先走本地事实库、公告解析、新闻 MCP，并写清 market/sector/stock level 的 role-to-level 映射。
5. `Fundamentals Analyst`：必须有独立 fundamentals envelope，而不是依赖 detail 接口残余文本。
6. `Shareholder Analyst`：必须输出股东结构、集中度、变动趋势等结构化结果，不能只复述公司概况。
7. `Product Analyst`：若无真实数据源，则必须 blocked，并在矩阵和 readiness 报告中明确，不允许假实现。
8. Bull/Bear/Research Manager/Trader/Risk/Portfolio Manager：必须只消费上游 artifacts，无直接查询权限。

### 七、Prompt 与模型实测拆解
1. 先做 Prompt 检查表，核对每个 analyst prompt 是否明确写入：本地 MCP 优先、何时 fallback、何时停止、哪些工具不可用。
2. 对非 analyst 角色，Prompt 里必须显式声明不直接取数，只消费 analyst outputs / debate history / investment plan / risk discussion。
3. 使用环境内可用 LLM key 做真实调用验证，检查模型是否会优先选择本地 MCP，而不是绕过系统能力直接联网或错误使用权限外工具。
4. readiness 报告不能只写“代码有这个接口”；必须记录真实拿数结果、关键字段、数据新鲜度、错误码和模型调用表现。

### 八、MCP 改造责任拆解
1. `CompanyOverviewMcp`：统一搜索、详情、基本面摘要，输出标准化 `company_details`。
2. `StockFundamentalsMcp`：统一 `fundamental-snapshot`、本地 `FundamentalFacts`、必要财报结构，输出 analyst 可直接消费对象。
3. `StockShareholderMcp`：封装东方财富股东研究、户数变化、集中度等事实。
4. `StockProductMcp`：新增主营业务、产品线、收入构成、产业链定位 adapter；若无稳定源，必须 blocked。
5. `SocialSentimentMcp` 或等价降级方案：明确真实源、受控 external fallback、degraded 标记与结论降级方式。

## 建议实施顺序
1. 先定义 `ToolGateway` 接口、envelope DTO、角色权限矩阵和错误码字典。
2. 再把现有 `StockKlineMcp`、`StockMinuteMcp`、`StockStrategyMcp`、`StockNewsMcp`、`StockSearchMcp` 适配到统一网关，验证旧能力不退化。
3. 然后补 `CompanyOverviewMcp`、`StockFundamentalsMcp`、`StockShareholderMcp` 三个优先缺口。
4. 再为 Social Sentiment 写明确降级 contract，决定何时显示 blocked、何时显示 degraded。
5. 最后处理 `StockProductMcp`：若没有真实数据源，则在 contract 中明确 blocked，不允许伪成功。

## 角色能力矩阵细化要求
1. 每个角色都要列出：可调用 tool groups、默认 policyClass、失败后是 blocked 还是 degraded、最小 evidence 数量要求。
2. analyst 角色要写清楚输入依赖与输出 block 名称，保证后续 R3 能直接引用。
3. researcher/manager/trader/risk/portfolio 角色要写清楚“只能读哪些上游 artifact”，防止绕过 analyst 重新查数。

## Prompt 与运行时边界要求
1. Prompt 里只声明角色职责、输入摘要和输出格式，不负责工具权限 enforcement。
2. 真实工具调用顺序、权限判断、fallback 选择、evidence 装配都应放在工具网关与运行时层完成。
3. 若环境中缺少 LLM key 或外部 provider 未配置，应在 readiness 报告中明确呈现，而不是把失败混同为普通工具降级。

## 交付物
1. 15 角色能力矩阵：至少包含角色、可调用 tool groups、默认 policyClass、fallback 规则、blocked/degraded 条件、最小 evidence 要求。
2. MCP Manager / Tool Gateway 设计文档：至少包含配置来源、客户端管理、工具发现、统一超时/重试/错误处理、观测字段。
3. MCP 权限模型：至少包含按角色、按 tool group、按 server 的授权规则与默认开关建议。
4. Tool envelope 规范：至少包含 requestSummary、resultSummary、evidenceRefs、warnings、degradedFlags、traceId、latency、sourceTier、errorCode。
5. Evidence 最小字段规范：至少包含 title、source、publishedAt、url、excerpt、readMode、readStatus、symbolScope、level、freshnessTag。
6. freshness / degradation 规则表：至少区分市场、新闻、公告、板块、基本面、股东、产品等时间窗和不新鲜时的处理。
7. MCP 缺口 backlog 与优先级：至少明确直接复用、扩展字段、新增 MCP、仅 fallback、blocked 五类结论。
8. MCP readiness 报告：至少包含关键 MCP 实测结果、Prompt 检查结果、LLM 实测结果和阻断项。

## 测试目标
1. 工具合同测试：请求、响应、warning、degradedFlags、traceId、freshness、errorCode 一致性。
2. 适配器集成测试：CompanyOverview/Fundamentals/Shareholder/Product/Social/News/Market 关键 MCP 能真实拿到数据或明确 blocked。
3. 权限测试：Researcher/Manager/Trader/Risk/Portfolio 不得绕过授权直接取数；Analyst 只能用自己授权范围内的 tool groups。
4. 容错测试：`local_required` 失败时必须立即中止并向上冒泡，`external_gated` 失败时只能在受控降级下继续。
5. readiness 测试：Prompt 中已写入 MCP-first 规则，且模型实测表现与规则一致。
6. 观测性测试：tool event、latency、requestSummary、cacheHit、errorCode 能完整进入日志/事件流。

## 回归测试要求
1. 防止模型直接绕过本地 MCP 去用 Web Search，本地可用时必须优先本地。
2. 防止原有 MCP 在接入新网关后丢失 evidence、warnings、freshness 或 degraded 标记。
3. 防止角色矩阵存在但实际没有真实数据源支撑，尤其是 Product、Social、Shareholder 不能假成功。
4. 防止非 analyst 角色重新获得查询权限，导致数据链路短路。
5. 防止 `local_required` 宕机时系统静默降级继续跑后续阶段。

## 完成标准
1. 15 角色到能力矩阵闭环完成，缺口都有明确实现、fallback 方案或 blocked 标记。
2. MCP 权限、MCP-first、fail-fast、degraded 策略已能被测试验证，并能形成 readiness 报告。
3. R3 不需要再为单个角色私写取数和容错逻辑，只消费统一工具网关。
4. 后续评审不再需要争论“这个角色应该查什么、能不能联网、何时算缺口”，因为本任务已给出可执行定论。