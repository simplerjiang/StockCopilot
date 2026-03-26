# GOAL-AGENT-NEW-001 P0-Pre Phase E 开发报告

## English

### Path Chosen
- Path A.
- Live Eastmoney validation proved a minimal product/business dataset is actually available today, so `StockProductMcp` was implemented instead of keeping Product Analyst in an “unconfirmed blocked” state.

### Live Source Probe
- Reused the current Eastmoney foundation instead of introducing a new provider.
- Probed `https://emweb.securities.eastmoney.com/PC_HSF10/CompanySurvey/CompanySurveyAjax?code=<market><symbol>` against `SH600000`, `SZ000001`, `SZ002594`, `SH603259`, `SH600460`.
- Verified stable fields across all sampled symbols:
  - `jyfw` -> business scope
  - `sshy` -> industry
  - `qy` -> region
- Verified a real limitation across the same probe set:
  - `zyyw` -> main business was empty in all sampled responses, so the minimum Product contract cannot depend on it being mandatory.

### Implemented Changes
- Added formal tool `StockProductMcp` to tool names, registry, gateway, backend route, and `IStockCopilotMcpService`.
- Added dedicated DTOs:
  - `StockCopilotProductDataDto`
  - `StockCopilotProductFactDto`
- Implemented `GetProductAsync(...)` in `StockCopilotMcpService` by reusing the existing `ResolveFundamentalSnapshotAsync(...)` path and filtering product/business facts from Eastmoney company-profile facts.
- Kept the product boundary narrow:
  - included `经营范围 / 所属行业 / 证监会行业 / 所属地区`
  - excluded shareholder-only and finance-only facts
- Updated `Product Analyst` role contract from `blocked` to `local_required`, with `StockProductMcp` as the first tool and a stop rule that refuses to fake product conclusions when minimum product facts are absent.
- Added a remaining-scope backlog task `GOAL-AGENT-NEW-001-P0-Pre-Phase-E-R1` for richer product composition / segment sources.

### Tests And Validation
- Live probe command:

```powershell
$symbols = @('SH600000','SZ000001','SZ002594','SH603259','SH600460'); foreach ($symbol in $symbols) { $url = "https://emweb.securities.eastmoney.com/PC_HSF10/CompanySurvey/CompanySurveyAjax?code=$symbol"; $json = Invoke-RestMethod -Uri $url -TimeoutSec 30; $jbzl = $json.jbzl; [pscustomobject]@{ Code = $symbol; Name = $jbzl.agjc; HasMainBusiness = -not [string]::IsNullOrWhiteSpace($jbzl.zyyw); HasBusinessScope = -not [string]::IsNullOrWhiteSpace($jbzl.jyfw); HasIndustry = -not [string]::IsNullOrWhiteSpace($jbzl.sshy); HasRegion = -not [string]::IsNullOrWhiteSpace($jbzl.qy) } | ConvertTo-Json -Compress }
```

- Live probe result summary:
  - all 5 samples returned `HasBusinessScope=true`, `HasIndustry=true`, `HasRegion=true`
  - all 5 samples returned `HasMainBusiness=false`
- Unit test command:

```powershell
dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "FullyQualifiedName~EastmoneyCompanyProfileParserTests|FullyQualifiedName~StockCopilotMcpServiceTests|FullyQualifiedName~StockMcpGatewayPhaseATests|FullyQualifiedName~StockAgentRoleContractRegistryTests|FullyQualifiedName~StockCopilotSessionServiceTests"
```

- Unit test result:
  - `48/48` passed

### Automation Sync
- Updated `.automation/tasks.json` to keep `GOAL-AGENT-NEW-001-P0-Pre-Phase-E-Product-MCP-Blocked` as the historical blocked-governance record and add `GOAL-AGENT-NEW-001-P0-Pre-Phase-E` as the completed implementation task.
- Added `GOAL-AGENT-NEW-001-P0-Pre-Phase-E-R1` as the dedicated remaining-scope task.
- Updated `.automation/state.json` so `lastCompletedTask` and `currentRun.taskId` both point to `GOAL-AGENT-NEW-001-P0-Pre-Phase-E`.

### Remaining Risks
- `主营业务(zyyw)` is still not reliable from the probed Eastmoney survey payload, so current Product MCP is intentionally centered on `经营范围 / 行业 / 地区` instead of pretending to have richer product segmentation.
- Richer business-composition fields such as product-line mix, regional revenue split, or structured segment breakdown remain unverified and are deferred to Phase E R1.
- `P0-Pre` still remains blocked by the live LLM gate, not by Product MCP anymore.

## 中文

### 本轮路径
- 走 A 路径。
- 经过 Eastmoney 真实探针，已经确认当前上游能稳定提供最小产品/业务事实集合，因此不再维持“未确认稳定数据源”的假 blocked，而是直接落最小 `StockProductMcp`。

### 真实源验证
- 严格复用现有 Eastmoney 基础，没有引入新的第三方 provider。
- 实测接口：`https://emweb.securities.eastmoney.com/PC_HSF10/CompanySurvey/CompanySurveyAjax?code=<market><symbol>`。
- 实测样本：`SH600000`、`SZ000001`、`SZ002594`、`SH603259`、`SH600460`。
- 已验证稳定可得字段：
  - `jyfw`，映射为经营范围
  - `sshy`，映射为所属行业
  - `qy`，映射为所属地区
- 已验证当前真实缺口：
  - `zyyw`，即主营业务，在上述样本上普遍为空，不能被设计成 Product MCP 的必填前提

### 本轮实现
- 新增正式工具名 `StockProductMcp`，并接入：
  - `StockMcpToolNames`
  - `McpServiceRegistry`
  - `McpToolGateway`
  - `StocksModule` 路由
  - `IStockCopilotMcpService` / `StockCopilotMcpService`
- 新增独立 DTO：
  - `StockCopilotProductDataDto`
  - `StockCopilotProductFactDto`
- `GetProductAsync(...)` 复用现有 `ResolveFundamentalSnapshotAsync(...)`，从 Eastmoney 公司概况事实中过滤产品业务维度字段，不新开旁路抓取。
- Product MCP 的数据边界保持最小且明确：
  - 纳入 `经营范围 / 所属行业 / 证监会行业 / 所属地区`
  - 不把股东字段和纯财务指标塞进 Product MCP
- 更新 `Product Analyst` contract：
  - 从 `blocked` 改为 `local_required`
  - 首选工具改为 `StockProductMcp`
  - stop rule 明确要求最小产品事实不足时必须停，不允许伪造产品分析
- 新增剩余范围任务 `GOAL-AGENT-NEW-001-P0-Pre-Phase-E-R1`，继续跟踪更细的业务构成 / 产品结构来源扩展。

### 测试与验证
- 实测探针命令：

```powershell
$symbols = @('SH600000','SZ000001','SZ002594','SH603259','SH600460'); foreach ($symbol in $symbols) { $url = "https://emweb.securities.eastmoney.com/PC_HSF10/CompanySurvey/CompanySurveyAjax?code=$symbol"; $json = Invoke-RestMethod -Uri $url -TimeoutSec 30; $jbzl = $json.jbzl; [pscustomobject]@{ Code = $symbol; Name = $jbzl.agjc; HasMainBusiness = -not [string]::IsNullOrWhiteSpace($jbzl.zyyw); HasBusinessScope = -not [string]::IsNullOrWhiteSpace($jbzl.jyfw); HasIndustry = -not [string]::IsNullOrWhiteSpace($jbzl.sshy); HasRegion = -not [string]::IsNullOrWhiteSpace($jbzl.qy) } | ConvertTo-Json -Compress }
```

- 实测结论：
  - 5 个样本全部返回 `HasBusinessScope=true`、`HasIndustry=true`、`HasRegion=true`
  - 5 个样本全部返回 `HasMainBusiness=false`
- 后端单测命令：

```powershell
dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "FullyQualifiedName~EastmoneyCompanyProfileParserTests|FullyQualifiedName~StockCopilotMcpServiceTests|FullyQualifiedName~StockMcpGatewayPhaseATests|FullyQualifiedName~StockAgentRoleContractRegistryTests|FullyQualifiedName~StockCopilotSessionServiceTests"
```

- 单测结果：
  - `48/48` 全部通过

### 自动化同步
- 已更新 `.automation/tasks.json`，保留 `GOAL-AGENT-NEW-001-P0-Pre-Phase-E-Product-MCP-Blocked` 作为历史 blocked 治理记录，并新增 `GOAL-AGENT-NEW-001-P0-Pre-Phase-E` 作为已完成最小实现 task。
- 已新增 `GOAL-AGENT-NEW-001-P0-Pre-Phase-E-R1` 作为 dedicated remaining-scope task。
- 已更新 `.automation/state.json`，让 `lastCompletedTask` 与 `currentRun.taskId` 统一指向 `GOAL-AGENT-NEW-001-P0-Pre-Phase-E`。

### 当前残余风险
- `主营业务(zyyw)` 仍不是可靠字段，因此当前 Product MCP 有意围绕 `经营范围 / 行业 / 地区` 提供最小真实 contract，而不是假装已经拿到更细粒度产品线拆分。
- 更丰富的 `业务构成 / 产品结构 / 地区收入分布` 仍未验证稳定来源，保留到 Phase E R1。
- `P0-Pre` 当前剩余阻断只剩 live LLM gate，不再是 Product MCP。

### 2026-03-26 文档纠偏补记
- 已同步修正权威计划文档中把 `StockProductMcp / Product Analyst` 写成“当前不存在 / 必须 blocked”的现状段落。
- 当前统一口径改为：最小 `StockProductMcp` 已存在并可用，当前 contract 主要基于 `经营范围 / 所属行业 / 证监会行业 / 所属地区`。
- `主营业务(zyyw)` 在 live probe 中仍不稳定且普遍为空，因此 richer product composition / segment breakdown 继续保留在 `GOAL-AGENT-NEW-001-P0-Pre-Phase-E-R1`，不把当前最小 contract 误写成完整实现。
- 本次仅做计划文档与自动化现状描述纠偏；未改功能代码，未重写历史报告，但已把 `.automation/state.json` 统一改指向 `GOAL-AGENT-NEW-001-P0-Pre-Phase-E`。

### 2026-03-26 文档纠偏扩围补记
- 已继续扩大纠偏范围，修正 `.automation/plan/GOAL-AGENT-NEW-001/GOAL-AGENT-NEW-001-P0-Pre.md` 与 `.automation/plan/GOAL-AGENT-NEW-001/GOAL-AGENT-NEW-001-P0.md` 中把 Phase A-D 已完成能力误写成“当前缺失”的段落。
- 当前统一口径改为：`McpToolGateway`、`McpServiceRegistry`、`RoleToolPolicyService`、`/api/stocks/mcp/*`、`/api/stocks/copilot/turns/draft`、`CompanyOverviewMcp`、`StockFundamentalsMcp`、`StockShareholderMcp`、`MarketContextMcp`、`SocialSentimentMcp` 均已存在并属于 current state。
- `Shareholder Analyst` 不再归类为“需要独立 MCP 补齐”；其现状改为已有 `StockShareholderMcp`，后续重点是字段质量、职责去重与证据链补强。
- `MarketContextMcp` 不再被表述为“待决定是否独立工具化”；`SocialSentimentMcp` 不再被表述为“当前未实现”，而是明确定义为已存在但当前以降级 contract 运行。
- 已把 `GOAL-AGENT-NEW-001-P0-Pre.md` 中“2026-03-26 Phase C 缺陷与实测复核 (sh601899)”改写为历史缺陷记录口径，并明确标注后续已修复/已缓解项，避免被误读为 current state。
- backend-served 最小 readiness 已由独立验收补证，因此当前 blocker 口径已收敛到 live LLM gate；本轮最小运行态 evidence 仅覆盖 `health / product mcp / draft route`，不等同于全量 Browser MCP 验收。
- 本次仍只做文档与自动化现状描述纠偏，不改功能代码；`.automation/state.json` 现已统一指向 `GOAL-AGENT-NEW-001-P0-Pre-Phase-E`。