# GOAL-AGENT-NEW-001-P0-Pre 首轮执行报告（2026-03-26）

## 本轮结论
1. 已开始执行 `P0-Pre`，并完成第一轮代码级与有限运行态证据收集。
2. 当前 `P0-Pre` 不能通过，必须判定为 `blocked`。
3. 阻断点已经明确：
   - 当前会话未能取得稳定的本地 backend-served HTTP 实测结果。
   - 当前代码中存在 MCP service / controlled loop service，但未确认有对应 DI 注册与 HTTP 路由暴露。
   - 当前仓库跟踪的 LLM provider 配置处于 `enabled=true` 但 `apiKey=""` 的状态，因此无法在本轮完成“基于环境内可用 key 的真实工具轨迹验证”。

## 本轮已执行动作
1. 复核 `GOAL-AGENT-NEW-001-P0-Pre.md` 的闸门要求，确认本阶段必须同时交付：
   - MCP 前置任务总表
   - 关键 MCP 实测可用性报告
   - Prompt 中 MCP 使用规则检查表
   - 基于环境内 LLM key 的 MCP 理解与调用实测报告
2. 读取并核对现有 MCP 运行时实现：
   - `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/StockCopilotMcpService.cs`
   - `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/StockCopilotSessionService.cs`
3. 读取并核对现有 stocks 路由与注册入口：
   - `backend/SimplerJiangAiAgent.Api/Modules/Stocks/StocksModule.cs`
4. 读取并核对 LLM 能力与可观测链路：
   - `backend/SimplerJiangAiAgent.Api/Infrastructure/Llm/OpenAiProvider.cs`
   - `backend/SimplerJiangAiAgent.Api/Infrastructure/Llm/LlmService.cs`
   - `backend/SimplerJiangAiAgent.Api/Modules/Llm/LlmModule.cs`
   - `backend/SimplerJiangAiAgent.Api/App_Data/llm-settings.json`
5. 尝试通过本地网页抓取验证 `http://localhost:5119/api/health` 与 `http://localhost:5119/api/stocks/fundamental-snapshot?symbol=sh600000`。

## 已确认的事实
1. `StockCopilotMcpService` 内部已经形成一套可复用的 MCP envelope 形态：
   - `StockKlineMcp` -> `local_required`
   - `StockMinuteMcp` -> `local_required`
   - `StockStrategyMcp` -> `local_required`
   - `StockNewsMcp` -> `local_required`
   - `StockSearchMcp` -> `external_gated`
2. `StockCopilotSessionService` 内部已经体现出一部分 P0-Pre 关心的控制规则：
   - planner / governor / commander 分层
   - local-first 摘要文案
   - `allowExternalSearch` 显式开关
   - `external_gated` 工具需要显式授权后才能执行
3. `OpenAiProvider` 与 `LlmService` 已具备 Prompt / tools / traceId 的审计日志能力，理论上支持做真实工具轨迹观测。

## 已确认的阻断项
1. `StocksModule.cs` 当前未发现 `IStockCopilotMcpService` 或 `IStockCopilotSessionService` 的服务注册语句。
2. 当前源码检索不到 `/api/stocks/mcp/*` 或 `/api/stocks/copilot/turns/draft` 路由注册；这与历史文档里“已有 MCP / draft endpoint”的表述存在偏差。
3. `fetch_webpage` 对 `http://localhost:5119/api/health` 与 `http://localhost:5119/api/stocks/fundamental-snapshot?symbol=sh600000` 均未提取出可用内容，因此本轮不能把 backend runtime 判定为“已成功验证”。
4. `backend/SimplerJiangAiAgent.Api/App_Data/llm-settings.json` 中两个 provider 均显示 `enabled=true`，但 `apiKey` 为空；在没有额外正向证据证明环境变量内存在可用 key 的前提下，本轮不能执行 LLM 工具轨迹实测。
5. 当前只能确认到平台级中文输出与旧 Copilot loop 的 local-first / external-gated 规则，尚未确认符合 `Trading Workbench` 15 角色要求的显式 MCP Prompt 契约。

## 详细阻断 MCP 总表

| 优先级 | MCP / 基础设施 | 阻断角色 | 当前代码底座 | 当前缺口 | 为什么阻断 GOAL-AGENT-NEW-001 |
| --- | --- | --- | --- | --- | --- |
| P0 | `McpToolGateway` / `McpServiceRegistry` / `RoleToolPolicyService` | 全部 Analyst 与后续 Runner | 已有零散 service：`StockCopilotMcpService`、`QueryLocalFactDatabaseTool`、`IStockMarketContextService`、`IStockFundamentalSnapshotService` | 没有统一网关、没有统一角色权限 enforcement、没有统一 envelope 路由 | R3 无法按角色安全调度工具，P0-Pre 也无法做统一 readiness 实测 |
| P0 | 运行态验证入口：`/api/stocks/mcp/*` 与 `/api/stocks/copilot/turns/draft` | P0-Pre、R2、R7 | 存在 `StockCopilotMcpService` / `StockCopilotSessionService` 类 | 未确认 DI 注册，未检索到 HTTP 暴露路由 | 没有可稳定调用的入口，就无法完成真实取数验证、Browser MCP 验收和 loop 级联调 |
| P0 | `CompanyOverviewMcp` | Company Overview Analyst、Analyst Team 全体 | `/api/stocks/search`、`/api/stocks/detail/cache`、`StockCompanyProfiles`、`StockSearchService` | 只有普通 API / 数据表，没有标准化 `company_details` MCP envelope | 没有统一公司基础画像，后续 6 个 analyst 的并行输入就不一致，Graph 第 0 阶段无法成立 |
| P0 | `StockFundamentalsMcp` | Fundamentals Analyst、Research Debate | `IStockFundamentalSnapshotService`、`/api/stocks/fundamental-snapshot`、`StockCompanyProfiles.FundamentalFactsJson` | 只有快照接口，没有 analyst 直接可用的 MCP envelope、freshness、evidence 映射 | Fundamentals Analyst 无法作为独立 grounded 角色接入，直接阻断 15 角色骨架闭环 |
| P0 | `StockShareholderMcp` | Shareholder Analyst、Research Debate | `EastmoneyCompanyProfileParser`、`EastmoneyFundamentalSnapshotService`、`ShareholderResearch/PageAjax` | 股东信息被混在快照和 parser 内部，没有独立 MCP contract | Shareholder Analyst 只能停留在文档里，无法给 bull/bear debate 提供独立股权结构证据 |
| P1 | `MarketContextMcp` | Market Analyst、Trader、Portfolio Decision | `IStockMarketContextService`、`MarketSentimentSnapshots`、`SectorRotationSnapshots` | 服务存在但未工具化，缺统一 evidence / degraded / freshness envelope | 没有工具化后，R3 只能私接 service，破坏 R2 的统一工具边界 |
| P1 | `SocialSentimentMcp` 或受控降级 MCP | Social Sentiment Analyst | `QueryLocalFactDatabaseTool` 中的 `AiSentiment` 字段、市场情绪快照 | 没有真实社媒源，没有明确定义“新闻近似替代”的 MCP contract | Social Analyst 无法判定是 blocked 还是 degraded，继续开发会导致伪多 Agent |
| P1 | 现有五类 MCP 的统一接入适配：`StockKlineMcp`、`StockMinuteMcp`、`StockStrategyMcp`、`StockNewsMcp`、`StockSearchMcp` | Market / News / 受控 fallback | `StockCopilotMcpService` 已实现 | 未注册、未暴露、未接入统一网关、未形成 readiness test 入口 | 虽然逻辑存在，但目前不能作为 GOAL-AGENT-NEW-001 的正式基础设施使用 |
| P2 | `StockProductMcp` | Product Analyst、Research Debate | 当前未发现稳定 service / adapter | 完整缺失，连上游数据源都未确定 | Product Analyst 无法落地，只能 blocked；若不单列，会在后续被误判为“实现中” |

## 对 GOAL-AGENT-NEW-001 真正有阻断性的缺失结论
1. **绝对阻断主流程的不是一个，而是 6 项**：
   - `McpToolGateway` / `McpServiceRegistry` / `RoleToolPolicyService`
   - 运行态 MCP / draft 验证入口
   - `CompanyOverviewMcp`
   - `StockFundamentalsMcp`
   - `StockShareholderMcp`
   - 现有五类 MCP 的正式注册与统一接入
2. **若不补这些项，R1/R2 即使继续写 contract，也无法进入真实后端 graph 开发**，因为：
   - Phase 0 没有 `company_details`
   - Fundamentals / Shareholder 两个 analyst 没有正式工具入口
   - 旧 MCP 无法通过统一网关参与 role-based orchestration
   - P0-Pre 无法做真实 readiness gate
3. `MarketContextMcp` 与 `SocialSentimentMcp` 是第二层阻断：
   - 不补 `MarketContextMcp`，R3 仍会被迫私接 service，破坏工具层边界
   - 不补 `SocialSentimentMcp` 或明确 degraded contract，Social Analyst 只能是假角色
4. `StockProductMcp` 是**必须在计划里明确 blocked 的缺口**：短期内不一定要先实现，但必须单独成项，不能再被一句“产品数据缺失”带过。

## 详细开发计划

### Phase A. 先补 MCP 基础设施与验证入口
1. 在 `backend/SimplerJiangAiAgent.Api/Modules/Stocks/StocksModule.cs` 中补注册：
   - `IStockCopilotMcpService -> StockCopilotMcpService`
   - `IStockCopilotSessionService -> StockCopilotSessionService`
   - 新增 `IMcpToolGateway`、`IMcpServiceRegistry`、`IRoleToolPolicyService`
2. 新增统一工具网关层，建议目录：
   - `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/Mcp/`
   - 文件建议：`McpToolGateway.cs`、`McpServiceRegistry.cs`、`RoleToolPolicyService.cs`、`McpErrorCodes.cs`
3. 新增最小 HTTP readiness 入口：
   - `/api/stocks/mcp/kline`
   - `/api/stocks/mcp/minute`
   - `/api/stocks/mcp/strategy`
   - `/api/stocks/mcp/news`
   - `/api/stocks/mcp/search`
   - `/api/stocks/copilot/turns/draft`
4. 本阶段完成标准：P0-Pre 能对已有 MCP 做真实 HTTP smoke test，而不是只看类是否存在。

### Phase B. 把现有 MCP 正式纳入统一网关
1. 保留 `StockCopilotMcpService` 内部已存在的 5 类 envelope 逻辑，但改为通过 `McpToolGateway` 按 tool name 分发。
2. 为这 5 类 MCP 统一补充：
   - `errorCode`
   - `freshnessTag`
   - `sourceTier`
   - `cacheHit`
   - `rolePolicyClass`
3. 为每个工具建立 readiness smoke case：
   - 成功返回
   - 空数据返回
   - 上游失败返回
   - `local_required` fail-fast
4. 本阶段完成标准：Market / News 主链路可以不依赖临时 service 直连，统一从网关拿结果。

### Phase C. 补齐第一批绝对阻断型新 MCP
1. `CompanyOverviewMcp`
   - 输入：`symbol` 或 `query`
   - 上游：`StockSearchService`、`/detail/cache`、`StockCompanyProfiles`、必要时 `IStockFundamentalSnapshotService`
   - 输出：标准化 `company_details`，至少包含 symbol、name、exchange、market、industry、sector、region、website、shareholderCount、fundamentalUpdatedAt
   - 作用：作为 Phase 0 的唯一标准输入
2. `StockFundamentalsMcp`
   - 上游：`IStockFundamentalSnapshotService` + `StockCompanyProfiles.FundamentalFactsJson`
   - 输出：结构化 fundamentals facts、更新时间、freshness、evidence refs
   - 作用：给 Fundamentals Analyst 独立 grounded 输入
3. `StockShareholderMcp`
   - 上游：`EastmoneyCompanyProfileParser` / `ShareholderResearch/PageAjax`
   - 输出：股东户数、股权集中度、户均持股市值、户均流通股、统计截止时间
   - 作用：让 Shareholder Analyst 从 Fundamentals 中独立出来
4. 本阶段完成标准：Company Overview / Fundamentals / Shareholder 三个 analyst 都有真实 MCP，而不是借旧详情接口拼装。

### Phase D. 补齐第二批边界 MCP
1. `MarketContextMcp`
   - 上游：`IStockMarketContextService`
   - 输出：stage、confidence、mainline sector、position scale、execution frequency、counter-trend warning
   - 作用：禁止 R3 私接市场上下文 service
2. `SocialSentimentMcp`
   - 第一版允许只做降级 contract，不强行承诺真实社媒
   - 上游：本地 `AiSentiment` 新闻事实 + 市场情绪快照 + 可选 external gated fallback
   - 输出必须显式带 `degradedFlags: ["social_proxy_from_news"]`
   - 若达不到最低证据阈值，则直接返回 blocked，而不是“中性”糊过去
3. 本阶段完成标准：Social Analyst 的状态能被系统判断为 success / degraded / blocked，而不是假装完成。

### Phase E. 明确 Product MCP 的 blocked 交付
1. 新建 `StockProductMcp` 任务项，但不承诺立即实现。
2. 先输出数据源评估：主营业务、产品线、收入构成、区域分布、产业链定位当前来自哪里。
3. 如果没有稳定源：
   - 在 readiness 报告中标记 `blocked`
   - 在角色矩阵中把 `Product Analyst` 标记为 blocked
   - 在 R3 / R4 中禁止把它当成 active analyst
4. 本阶段完成标准：Product 缺口被显式治理，而不是被遗漏。

### Phase F. Prompt 与 LLM gate
1. 为 7 类可取数角色补独立 Prompt 检查表：
   - 先调用哪个 MCP
   - 何时允许 fallback
   - 何时必须停止
   - 最低 evidence 数
2. 为非取数角色补“禁止工具调用”契约。
3. 在确认可用 key 后，执行真实工具轨迹验证，并把 traceId、tool usage、fallback 路径写回 readiness 报告。
4. 本阶段完成标准：P0-Pre 从 blocked 变为 pass，才允许 R1/R2 进入开发完成态。

## 建议的交付顺序
1. 本周先完成 Phase A + B。
2. 第二步完成 Phase C 的 `CompanyOverviewMcp`、`StockFundamentalsMcp`、`StockShareholderMcp`。
3. 第三步完成 Phase D 的 `MarketContextMcp` 与 `SocialSentimentMcp` 降级 contract。
4. 第四步把 `StockProductMcp` 作为明确 blocked 项单独入表。
5. 最后做 Phase F 的 Prompt / LLM readiness gate。

## 本轮验证记录
1. `fetch_webpage` -> `http://localhost:5119/api/health`
   - 结果：未提取到有意义内容，不能作为通过证据。
2. `fetch_webpage` -> `http://localhost:5119/api/stocks/fundamental-snapshot?symbol=sh600000`
   - 结果：未提取到有意义内容，不能作为通过证据。
3. 工作区检索 `StockCopilotMcpService|StockCopilotSessionService`
   - 结果：命中 service 实现文件，说明运行时骨架存在。
4. 工作区检索 `/api/stocks/mcp/*|/api/stocks/copilot/turns/draft`
   - 结果：未命中，说明当前 HTTP 暴露面缺失或已漂移。
5. 工作区检索 `apiKey": ""|enabled": true|activeProviderKey`
   - 结果：命中 `llm-settings.json`，确认 tracked provider 配置为空 key。

## P0-Pre 判定
1. MCP 前置任务总表：未完成。
2. 关键 MCP 实测可用性报告：未完成，当前只能得出“实测入口不足或运行态未验证”的中间结论。
3. Prompt 中 MCP 使用规则检查表：未完成，当前仅能确认旧 Copilot loop 层的局部规则，不满足 15 角色要求。
4. 基于环境内 LLM key 的 MCP 理解与调用实测报告：未完成，当前缺少可证实可用的 key。
5. 综合结论：`P0-Pre blocked`，不得进入 R1 / R2 开发完成态，更不得进入 R3 / R4。

## 建议的下一轮动作
1. 先补 Phase A：网关、注册、readiness endpoint。
2. 再补 Phase B + C：把现有五类 MCP 正式纳入统一网关，并新增 `CompanyOverviewMcp`、`StockFundamentalsMcp`、`StockShareholderMcp`。
3. 再补 Phase D：`MarketContextMcp` 与 `SocialSentimentMcp` 降级 contract。
4. 最后执行 Phase E + F：`StockProductMcp` blocked 治理、Prompt 契约和 LLM 工具轨迹验证。