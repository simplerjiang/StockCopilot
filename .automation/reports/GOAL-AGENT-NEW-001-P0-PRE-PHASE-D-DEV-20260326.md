# GOAL-AGENT-NEW-001 P0-Pre Phase D Development Report

## English

### Scope
- Deliver the minimal backend implementation for Phase D only.
- Add `MarketContextMcp` and `SocialSentimentMcp` on top of the existing MCP registry, gateway, service, and `/api/stocks/mcp/*` routing stack.
- Keep Social Sentiment explicitly degraded or blocked; do not pretend that real social-media coverage exists.

### Actions
- Added new structured MCP data contracts in `StockAgentRuntimeModels.cs`:
  - `StockCopilotMarketContextDataDto`
  - `StockCopilotSentimentCountDto`
  - `StockCopilotSocialSentimentMarketProxyDto`
  - `StockCopilotSocialSentimentDataDto`
- Added new formal tool names and registry entries:
  - `MarketContextMcp`
  - `SocialSentimentMcp`
- Extended role policy and gateway forwarding for both tools.
- Added `GetMarketContextAsync(...)` to `StockCopilotMcpService`:
  - wraps `IStockMarketContextService` directly
  - returns independent structured `data`
  - keeps `policyClass = local_required`
- Added `GetSocialSentimentAsync(...)` to `StockCopilotMcpService`:
  - reuses `QueryLocalFactDatabaseTool` for local stock/sector/market news sentiment
  - reuses `ISectorRotationQueryService.GetLatestSummaryAsync(...)` as market sentiment proxy
  - returns `status = degraded` when only local news and/or market proxy evidence exists
  - returns `status = blocked`, `blockedReason = no_data` when both are missing
  - always marks v1 as non-real-social coverage via warnings and degraded flags
- Added new HTTP routes:
  - `/api/stocks/mcp/market-context`
  - `/api/stocks/mcp/social-sentiment`
- Added and updated backend tests for registry, gateway, route pattern, structured data success, degraded contract, and blocked/no-data semantics.

### Test Command
```powershell
$target = Resolve-Path '.\backend\SimplerJiangAiAgent.Api\bin\Debug\net8.0\SimplerJiangAiAgent.Api.exe' -ErrorAction SilentlyContinue; if ($target) { Get-Process | Where-Object { $_.Path -eq $target.Path } | Stop-Process -Force }; dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~StockCopilotMcpServiceTests|FullyQualifiedName~StockMcpGatewayPhaseATests|FullyQualifiedName~StockCopilotSessionServiceTests"
```

### Test Result
- Passed: 38/38
- Failed: 0
- Coverage in this round includes:
  - registry / gateway / route pattern for the two new MCP tools
  - structured `MarketContextMcp` return shape
  - degraded `SocialSentimentMcp` contract when only approximate evidence exists
  - blocked `SocialSentimentMcp` contract when evidence is insufficient

### Remaining P0-Pre Blockers
- Phase E is still not implemented.
- Phase F is still not implemented.
- Live LLM-gated validation is still blocked by the current provider/key gate state.

## 中文

### 本轮范围
- 仅完成 Phase D 的最小后端交付。
- 在现有 MCP registry、gateway、service、`/api/stocks/mcp/*` 路由底座上新增 `MarketContextMcp` 与 `SocialSentimentMcp`。
- `SocialSentimentMcp` 明确保持降级或阻断语义，不伪装成真实社媒能力。

### 本轮动作
- 在 `StockAgentRuntimeModels.cs` 新增结构化 contract：
  - `StockCopilotMarketContextDataDto`
  - `StockCopilotSentimentCountDto`
  - `StockCopilotSocialSentimentMarketProxyDto`
  - `StockCopilotSocialSentimentDataDto`
- 新增正式工具名与 registry 注册：
  - `MarketContextMcp`
  - `SocialSentimentMcp`
- 扩展 role policy 与 gateway 转发。
- 在 `StockCopilotMcpService` 新增 `GetMarketContextAsync(...)`：
  - 直接包装 `IStockMarketContextService`
  - 输出独立结构化 `data`
  - `policyClass` 固定为 `local_required`
- 在 `StockCopilotMcpService` 新增 `GetSocialSentimentAsync(...)`：
  - 复用 `QueryLocalFactDatabaseTool` 的本地个股 / 板块 / 市场新闻情绪
  - 复用 `ISectorRotationQueryService.GetLatestSummaryAsync(...)` 的市场情绪快照作为代理信号
  - 当只有本地新闻情绪和 / 或市场代理情绪时，返回 `status = degraded`
  - 当两者都缺失时，返回 `status = blocked`、`blockedReason = no_data`
  - 通过 warning 和 degraded flag 明确声明 v1 不是“真实社媒覆盖”
- 新增 HTTP 路由：
  - `/api/stocks/mcp/market-context`
  - `/api/stocks/mcp/social-sentiment`
- 新增并调整后端单测，覆盖 registry、gateway、route pattern、结构化成功返回、degraded contract、blocked/no_data 语义。

### 测试命令
```powershell
$target = Resolve-Path '.\backend\SimplerJiangAiAgent.Api\bin\Debug\net8.0\SimplerJiangAiAgent.Api.exe' -ErrorAction SilentlyContinue; if ($target) { Get-Process | Where-Object { $_.Path -eq $target.Path } | Stop-Process -Force }; dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~StockCopilotMcpServiceTests|FullyQualifiedName~StockMcpGatewayPhaseATests|FullyQualifiedName~StockCopilotSessionServiceTests"
```

### 测试结果
- 通过：38/38
- 失败：0
- 本轮覆盖内容包括：
  - 两个新 MCP 的 registry / gateway / route pattern
  - `MarketContextMcp` 的结构化返回
  - 仅有近似证据时 `SocialSentimentMcp` 的 degraded contract
  - 证据不足时 `SocialSentimentMcp` 的 blocked/no_data contract

### 当前剩余 P0-Pre 阻断
- Phase E 尚未实现。
- Phase F 尚未实现。
- 真实 LLM gate 的 live validation 仍受当前 provider/key 状态阻塞。