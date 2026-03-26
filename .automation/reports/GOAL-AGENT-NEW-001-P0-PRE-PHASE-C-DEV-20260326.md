# GOAL-AGENT-NEW-001 P0-Pre Phase C 开发报告

## 任务范围
- 目标：继续完成 P0-Pre 的 Phase C，补齐预检要求中的三类 MCP：`CompanyOverviewMcp`、`StockFundamentalsMcp`、`StockShareholderMcp`。
- 范围限制：只在现有 MCP 基础设施上扩展，不重建并行架构；不处理 Phase D / E / F 与 LLM gate。

## 本次改动
- 扩展运行时 DTO：
  - 在 `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Models/StockAgentRuntimeModels.cs` 新增：
    - `StockCopilotCompanyOverviewDataDto`
    - `StockCopilotFundamentalsDataDto`
    - `StockCopilotShareholderDataDto`
- 扩展 MCP 工具注册与权限：
  - `StockMcpToolNames` 新增三类工具名。
  - `McpServiceRegistry` 注册三类新工具，均使用 `local_required`。
  - `RoleToolPolicyService` 为 `company_overview_analyst`、`fundamentals_analyst`、`shareholder_analyst` 增加对应授权。
  - `McpToolGateway` 增加三类转发方法。
- 扩展 HTTP 路由：
  - 在 `StocksModule` 新增：
    - `/api/stocks/mcp/company-overview`
    - `/api/stocks/mcp/fundamentals`
    - `/api/stocks/mcp/shareholder`
- 扩展 MCP service：
  - 在 `StockCopilotMcpService` 中新增：
    - `GetCompanyOverviewAsync(...)`
    - `GetFundamentalsAsync(...)`
    - `GetShareholderAsync(...)`
  - 数据策略：
    - 先走 `QueryLocalFactDatabaseTool` 的本地缓存 facts。
    - 若本地 facts 为空，则回退 `IStockFundamentalSnapshotService`。
    - CompanyOverview 同时复用 quote、sector、shareholder count、fundamental updated time。
    - Shareholder MCP 会过滤股东相关 facts，并在只有股东户数但缺少对应 fact 条目时补合成一条结构化 fact。
  - envelope 行为保持复用现有统一逻辑，继续自动派生 `errorCode`、`freshnessTag`、`sourceTier`、`cacheHit`、`rolePolicyClass`。

## 测试与验证
- 先做编译/编辑器错误检查：
  - `get_errors` 检查以下文件均无错误：
    - `StockCopilotMcpService.cs`
    - `StockCopilotMcpServiceTests.cs`
    - `StockCopilotSessionServiceTests.cs`
    - `StockMcpGatewayPhaseATests.cs`
    - `McpToolGateway.cs`
    - `StocksModule.cs`
- 定向后端测试命令：
```powershell
 dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "StockCopilotMcpServiceTests|StockMcpGatewayPhaseATests|StockCopilotSessionServiceTests"
```
- 第一次结果：29 通过，1 失败。
  - 失败原因：`GetShareholderAsync_WhenOnlyLiveSnapshotAvailable_ShouldReturnSnapshotFacts` 的测试夹具时间戳过旧，`freshnessTag` 按实际逻辑被判成 `stale`，不是实现错误。
- 修正：把 live snapshot 测试时间改为 `DateTime.UtcNow.AddHours(-1)`。
- 第二次结果：30/30 通过。

## Phase C 残留业务数据缺口 follow-up
- 本轮只处理 Phase C 残留业务数据缺口，不重复 Phase A/B 骨架：
  - 修复 `ResolveFundamentalSnapshotAsync(...)` 的本地 facts 短路逻辑，改为本地 facts 与 snapshot 合并；当 snapshot 可用时补齐本地稀疏 facts 缺失的财务指标，当 snapshot 抓取失败或缺失时保留本地 facts 并返回 fallback warning / degraded。
  - 为 `StockCopilotCompanyOverviewDataDto` 显式增加 `MainBusiness` / `BusinessScope`，并把 `BuildCompanyOverviewEvidence(...)` 的 summary 稳定扩展为包含 `主营业务=` / `经营范围=` 文本。
  - 保持 `StockFundamentalsMcp` 继续剥离 shareholder facts，并用回归测试锁住 fundamentals / shareholder 的职责边界。
- 新增/调整的定向单测：
  - `GetFundamentalsAsync_WhenLocalFactsSparse_ShouldMergeSnapshotFacts`
  - `GetCompanyOverviewAsync_ShouldExposeMainBusinessAndBusinessScope`
  - `FundamentalsAndShareholder_ShouldSplitShareholderFactsAcrossTools`
- 本轮实际执行命令：
```powershell
$target = Resolve-Path '.\backend\SimplerJiangAiAgent.Api\bin\Debug\net8.0\SimplerJiangAiAgent.Api.exe' -ErrorAction SilentlyContinue; if ($target) { Get-Process | Where-Object { $_.Path -eq $target.Path } | Stop-Process -Force }; dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~StockCopilotMcpServiceTests"
```
- 实际结果：
  - 第一次：25/25 通过，但 `StockCopilotMcpServiceTests.cs` 新增断言存在 1 个可空警告；随后已修正断言。
  - 第二次：25/25 通过，0 失败，命中本轮新增 company overview / fundamentals merge / shareholder split 回归。
- 本轮后 Phase C 仍未解项：
  - 无新增 Phase C 业务数据 blocker。

## 结果判定
- Phase C 已完成。
- 当前 P0-Pre 仍未整体解锁。
- 剩余阻断已收敛为：
  - Phase D
  - Phase E
  - Phase F
  - LLM gate

## 风险与说明
- 本轮未做 Browser MCP，因为改动范围仅在后端 MCP contract 与路由，且未涉及前端 UI。
- CompanyOverview / Fundamentals / Shareholder 当前采用 cache-first + fallback，能满足预检 MCP completeness 要求；若后续 Phase D/E/F 对 evidence shape 或 planner contract 有更细约束，再在该基础上继续补强即可。
