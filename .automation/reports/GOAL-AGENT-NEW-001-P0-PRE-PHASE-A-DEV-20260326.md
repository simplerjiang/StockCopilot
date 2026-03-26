# GOAL-AGENT-NEW-001-P0-Pre Phase A 执行报告（2026-03-26）

## 本轮结论
1. Phase A 已完成最小可用落地，不再停留在“代码里有 service 但运行态没有入口”的状态。
2. 本轮已补齐 P0-Pre 第一层基础设施缺口：
   - `IStockCopilotMcpService` / `IStockCopilotSessionService` 的 DI 注册
   - 统一 `IMcpToolGateway` / `IMcpServiceRegistry` / `IRoleToolPolicyService` 骨架
   - `/api/stocks/mcp/*` 与 `/api/stocks/copilot/turns/draft` 最小 HTTP 入口
3. `P0-Pre` 总体仍然是 `blocked`，因为 `CompanyOverviewMcp`、`StockFundamentalsMcp`、`StockShareholderMcp`、`MarketContextMcp`、`SocialSentimentMcp`、`StockProductMcp` 与 Prompt / LLM gate 还未完成。

## 本轮实现内容
1. 在 `backend/SimplerJiangAiAgent.Api/Modules/Stocks/StocksModule.cs` 中新增注册：
   - `IStockChatHistoryService -> StockChatHistoryService`
   - `IStockCopilotMcpService -> StockCopilotMcpService`
   - `IStockCopilotSessionService -> StockCopilotSessionService`
   - `IStockCopilotAcceptanceService -> StockCopilotAcceptanceService`
   - `IStockAgentReplayCalibrationService -> StockAgentReplayCalibrationService`
   - `IMcpToolGateway -> McpToolGateway`
   - `IMcpServiceRegistry -> McpServiceRegistry`
   - `IRoleToolPolicyService -> RoleToolPolicyService`
   - `StockCopilotSearchOptions` 配置绑定
2. 在 `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/Mcp/` 下新增基础设施文件：
   - `StockMcpToolNames.cs`
   - `McpErrorCodes.cs`
   - `McpServiceRegistry.cs`
   - `RoleToolPolicyService.cs`
   - `McpToolGateway.cs`
3. 在 `StocksModule.MapEndpoints(...)` 中新增最小运行态入口：
   - `GET /api/stocks/mcp/kline`
   - `GET /api/stocks/mcp/minute`
   - `GET /api/stocks/mcp/strategy`
   - `GET /api/stocks/mcp/news`
   - `GET /api/stocks/mcp/search`
   - `POST /api/stocks/copilot/turns/draft`
4. 所有新增 MCP / draft 入口都统一通过 `StockMcpEndpointExecutor` 包装，以保持取消请求返回 `499` 的既有行为。

## 新增测试
1. 新增 `backend/SimplerJiangAiAgent.Api.Tests/StockMcpGatewayPhaseATests.cs`，覆盖：
   - registry 内建工具定义
   - role policy system endpoint / unauthorized role
   - gateway 对底层 `IStockCopilotMcpService` 的委托
   - `StocksModule` 的 Phase A 服务注册

## 验证
1. 命令：
   `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~StockMcpGatewayPhaseATests|FullyQualifiedName~StockCopilotMcpServiceTests|FullyQualifiedName~StockCopilotSessionServiceTests|FullyQualifiedName~StockCopilotAcceptanceServiceTests|FullyQualifiedName~StockMcpEndpointExecutorTests|FullyQualifiedName~StocksModuleHttpClientTests" -p:OutDir=D:\SimplerJiangAiAgent\.automation\tmp\phase-a-tests\`
2. 结果：20/20 通过。

## 对 P0-Pre 阻断状态的更新
1. 已解除的阻断：
   - `StockCopilotMcpService` / `StockCopilotSessionService` 无 DI 注册
   - `/api/stocks/mcp/*` 与 `/api/stocks/copilot/turns/draft` 无运行态入口
2. 仍然存在的阻断：
   - `CompanyOverviewMcp`
   - `StockFundamentalsMcp`
   - `StockShareholderMcp`
   - `MarketContextMcp`
   - `SocialSentimentMcp` 或正式降级 contract
   - `StockProductMcp`
   - Prompt 契约检查表
   - 基于环境内可用 key 的真实 LLM 工具轨迹验证

## 下一步建议
1. 直接进入 Phase B：把现有五类 MCP 的调用统一收敛到 `McpToolGateway` 的稳定 envelope / smoke test 入口，而不再视其为“仅代码存在”。
2. 随后进入 Phase C：优先补 `CompanyOverviewMcp`、`StockFundamentalsMcp`、`StockShareholderMcp`。