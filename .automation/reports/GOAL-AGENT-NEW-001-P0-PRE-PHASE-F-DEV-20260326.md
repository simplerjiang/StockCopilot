# GOAL-AGENT-NEW-001 P0-Pre Phase F 开发报告

## 本轮范围

本轮只实现 Phase F 的最小可交付范围：把“15 角色 MCP / Prompt 使用契约”从规划文档落为后端可审计运行时对象，并接入现有 draft session 输出。

本轮明确不做以下事项：

- 不实现任何 Product MCP 或 Product 数据抓取。
- 不尝试 live LLM gate。
- 不做前端、Browser MCP 或 packaged desktop 验收。

## 已完成内容

1. 在后端新增 `IStockAgentRoleContractRegistry` 与 `StockAgentRoleContractRegistry`。
2. 固化 15 个冻结角色的契约字段：`roleId`、`roleName`、`roleClass`、`toolAccessMode`、`preferredMcpSequence`、`fallbackRule`、`stopRule`、`minimumEvidenceCount`、`allowsDirectQueryTools`、`reason`。
3. 明确保持 `Product Analyst` 为 `blocked`，且不给任何伪造 MCP 列表。
4. 明确 `Bull Researcher`、`Bear Researcher`、`Research Manager`、`Trader`、三类 `Risk Analyst`、`Portfolio Manager` 均无直接查询工具权限。
5. 在 `StockCopilotSessionDto` 新增 `RoleContractChecklist`，并由 `StockCopilotSessionService.BuildDraftTurnAsync(...)` 在现有 draft runtime 输出中直接挂载。
6. 将 `RoleToolPolicyService` 改为直接基于 `IStockAgentRoleContractRegistry` 生成角色授权集合，移除手写偏移矩阵，使运行时 policy 与 contract 共用同一事实源。
7. 新增与更新单元测试，锁定 `market_analyst`、`fundamentals_analyst`、`portfolio_manager` 的授权语义，并增加 contract/policy 一致性回归。
8. 清理误生成的 `.automation/tmp/phase-a-tests` 测试临时产物，避免二进制文件污染变更集。

## 验证命令

```powershell
dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "FullyQualifiedName~StockAgentRoleContractRegistryTests|FullyQualifiedName~StockCopilotSessionServiceTests|FullyQualifiedName~StockMcpGatewayPhaseATests"
```

结果：15/15 通过。

## 验证结论

- `Market Analyst` 契约保持 `local_required`，且不允许自动 external fallback。
- `News Analyst` 契约保持 local-first，只有本地新闻证据不足时才允许受控 external fallback。
- `RoleToolPolicyService` 现在直接从 contract 生成授权集合，不再维护第二份独立 MCP 角色矩阵。
- `Product Analyst` 仍然 blocked。
- 非数据查询角色继续禁止直接调用查询工具。
- 契约清单已经出现在 draft session runtime 输出中，可供后续 runner / acceptance / audit 直接读取。

## 未完成与阻断

1. `StockProductMcp / Product Analyst` 仍受真实上游数据源缺失阻断，本轮没有解除。
2. live LLM gate 仍未完成，本轮没有宣称通过真实模型链路验收。
3. P0-Pre 因上述两项原因仍不能视为完全解锁。

## 影响文件

- `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Models/StockAgentRuntimeModels.cs`
- `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/Mcp/StockAgentRoleContractRegistry.cs`
- `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/Mcp/RoleToolPolicyService.cs`
- `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/StockCopilotSessionService.cs`
- `backend/SimplerJiangAiAgent.Api/Modules/Stocks/StocksModule.cs`
- `backend/SimplerJiangAiAgent.Api.Tests/StockAgentRoleContractRegistryTests.cs`
- `backend/SimplerJiangAiAgent.Api.Tests/StockCopilotSessionServiceTests.cs`
- `backend/SimplerJiangAiAgent.Api.Tests/StockMcpGatewayPhaseATests.cs`
- `.automation/reports/GOAL-AGENT-NEW-001-P0-PRE-PHASE-F-DEV-20260326.md`
- `.automation/tasks.json`
