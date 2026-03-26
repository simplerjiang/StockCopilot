# GOAL-AGENT-NEW-001 P0-Pre Phase B 开发记录（2026-03-26）

## 本轮范围
1. 把现有五类 MCP 的统一字段正式落到 envelope 模型，而不是仅散落在 `cache` / `meta` 子对象中。
2. 补齐五类现有 MCP 的 Phase B smoke tests，覆盖成功、空数据降级、上游失败冒泡三类最小验证。
3. 保持现有 `McpToolGateway -> StockCopilotMcpService` 链路不回退。

## 本轮代码改动
1. 在 `StockCopilotMcpEnvelopeDto<T>` 中新增统一字段：
   - `errorCode`
   - `freshnessTag`
   - `sourceTier`
   - `cacheHit`
   - `rolePolicyClass`
2. 在 `StockCopilotMcpService.BuildEnvelope(...)` 中新增统一派生逻辑：
   - `errorCode`：优先取首个 `degradedFlag`，无降级但有 warning 时落 `tool.warning_present`，否则为 `null`
   - `freshnessTag`：按 evidence 最新时间分为 `fresh` / `recent` / `stale` / `no_data`
   - `sourceTier`：按 `policyClass` 与 evidence 来源分为 `external` / `local` / `live` / `cache`
   - `cacheHit`：镜像 `cache.hit`
   - `rolePolicyClass`：镜像当前 policy class
3. 保留 `StockCopilotMcpService` 当前 envelope 生成方式，未回退到散接 service。

## 测试补充
1. 更新 `StockCopilotMcpServiceTests`，新增和补强：
   - `GetKlineAsync_ShouldWrapDataInLocalRequiredEnvelope`
   - `GetKlineAsync_WhenKlineSeriesEmpty_ShouldReturnStableEnvelope`
   - `GetKlineAsync_WhenQuoteFetchFails_ShouldBubbleUpstreamFailure`
   - `GetMinuteAsync_ShouldWrapDataInLocalRequiredEnvelope`
   - `GetMinuteAsync_WhenMinuteSeriesEmpty_ShouldReturnStableEnvelope`
   - `GetMinuteAsync_WhenQuoteFetchFails_ShouldBubbleUpstreamFailure`
   - `GetStrategyAsync_ShouldTreatEqualClosesAsTdContinuation`
   - `GetStrategyAsync_WhenKlineWindowEmpty_ShouldReturnEmptySignalSet`
   - `GetStrategyAsync_WhenQuoteFetchFails_ShouldBubbleUpstreamFailure`
   - `GetNewsAsync_ShouldExposeLocalEvidenceObjects`
   - `GetNewsAsync_WhenLocalQueryFails_ShouldBubbleUpstreamFailure`
   - `SearchAsync_WhenProviderDisabled_ShouldReturnExternalGatedDegradedEnvelope`
   - `SearchAsync_WhenProviderEnabledAndResponseSucceeds_ShouldExposeExternalEvidence`
   - `SearchAsync_WhenProviderEnabledAndResponseIsEmpty_ShouldReturnNoDataEnvelope`
   - `SearchAsync_WhenProviderReturnsFailureStatus_ShouldExposeDegradedEnvelope`
   - `GetNewsAsync_WhenNoLocalEvidence_ShouldReturnNoDataFreshnessAndDegradedFlag`
2. 其中三类最小 smoke test 已覆盖：
   - `kline`：成功 / 空数据 / 上游失败
   - `minute`：成功 / 空数据 / 上游失败
   - `strategy`：成功 / 空数据 / 上游失败
   - `news`：成功 / 空数据 / 上游失败
   - `search`：成功 / 空数据 / 外部失败

## 验证命令
```powershell
dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~StockCopilotMcpServiceTests|FullyQualifiedName~StockCopilotSessionServiceTests|FullyQualifiedName~StockMcpGatewayPhaseATests|FullyQualifiedName~StockMcpEndpointExecutorTests"
```

## 验证结果
1. 28/28 通过。
2. 期间先遇到本地运行中的 API 进程锁定 `SimplerJiangAiAgent.Api.exe`，在释放后台实例后复跑通过；该问题属于本地运行态占用，不是本轮代码缺陷。

## 当前结论
1. Phase B 已按计划文件完成：现有五类 MCP 已通过统一网关出入，统一字段进入 MCP envelope，且每类 MCP 均具备 success / empty / upstream-failure 三类 smoke tests。
2. P0-Pre 仍不能判定通过，剩余阻断仍在：
   - Phase C / D / E / F 尚未完成
   - LLM key 与真实 tool trace gate 仍未完成

## 下一步建议
1. 进入 Phase C，补齐 `CompanyOverviewMcp`、`StockFundamentalsMcp`、`StockShareholderMcp`。
2. 在此基础上进入 `CompanyOverviewMcp`、`StockFundamentalsMcp`、`StockShareholderMcp` 的 Phase C 开发。
