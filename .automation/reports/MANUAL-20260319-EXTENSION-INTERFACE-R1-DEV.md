# Extension Interface R1 Development Report / 扩展接口 R1 开发报告

## Development (EN)
- Scope: deliver the first backend-owned adoption slice from `stock-and-fund-chrome-master` without changing the frontend yet.
- Added a new Eastmoney realtime adapter pair:
  - `IEastmoneyRealtimeMarketClient` / `EastmoneyRealtimeMarketClient`
  - `IRealtimeMarketOverviewService` / `RealtimeMarketOverviewService`
- Added backend DTOs for:
  - batch quotes
  - main capital flow
  - northbound flow
  - market breadth distribution
  - aggregated realtime market overview
- Exposed two new APIs:
  - `/api/stocks/quotes/batch`
  - `/api/market/realtime/overview`
- The implementation keeps the existing backend-owned architecture and does not let the frontend call third-party endpoints directly.

## 开发内容（ZH）
- 范围：先把 `stock-and-fund-chrome-master` 里有价值的实时公开接口以“后端自管”的方式吸收到系统里，本轮不改前端。
- 新增东财实时适配与聚合服务：
  - `IEastmoneyRealtimeMarketClient` / `EastmoneyRealtimeMarketClient`
  - `IRealtimeMarketOverviewService` / `RealtimeMarketOverviewService`
- 新增后端 DTO，覆盖：
  - 批量行情
  - 主力资金
  - 北向资金
  - 涨跌分布
  - 聚合后的市场实时总览
- 新增两个 API：
  - `/api/stocks/quotes/batch`
  - `/api/market/realtime/overview`
- 实现策略保持现有后端自主管理架构，不允许前端直接打第三方接口。

## Validation (EN)
- Unit test command:
  - `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "EastmoneyRealtimeMarketClientTests|RealtimeMarketOverviewServiceTests|EastmoneySectorRotationClientTests|StockSearchServiceTests|StockDataServiceSourceRoutingTests"`
- Unit test result:
  - build succeeded
  - total 15, failed 0, passed 15, skipped 0
- Runtime smoke commands:
  - `Invoke-RestMethod http://localhost:5119/api/health`
  - `Invoke-RestMethod "http://localhost:5119/api/stocks/quotes/batch?symbols=sh600000,sz000021,sh000001,sz399001"`
  - `Invoke-RestMethod "http://localhost:5119/api/market/realtime/overview?symbols=sh600000,sz000021,sh000001,sz399001"`
- Runtime smoke result:
  - health returned `{"status":"ok"}`
  - batch quotes returned live quote payloads for stocks and indexes
  - realtime overview returned aggregated quote, capital-flow, northbound, and breadth payloads successfully
  - northbound values were all zero on this date, and a direct raw-source probe confirmed the upstream Eastmoney payload itself was zero-filled rather than misparsed

## 验证（ZH）
- 单测命令：
  - `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "EastmoneyRealtimeMarketClientTests|RealtimeMarketOverviewServiceTests|EastmoneySectorRotationClientTests|StockSearchServiceTests|StockDataServiceSourceRoutingTests"`
- 单测结果：
  - 构建成功
  - 总计 15，失败 0，通过 15，跳过 0
- 运行时 smoke 命令：
  - `Invoke-RestMethod http://localhost:5119/api/health`
  - `Invoke-RestMethod "http://localhost:5119/api/stocks/quotes/batch?symbols=sh600000,sz000021,sh000001,sz399001"`
  - `Invoke-RestMethod "http://localhost:5119/api/market/realtime/overview?symbols=sh600000,sz000021,sh000001,sz399001"`
- 运行时结果：
  - health 返回 `{"status":"ok"}`
  - 批量行情接口返回了股票和指数的实时行情数据
  - 市场总览接口成功返回报价、资金流、北向资金和涨跌分布聚合结果
  - 当日北向资金值全为 0，经直连东财原始端点确认，上游 payload 本身就是全 0，不是解析错误

## Remaining Scope (EN)
- R2: standardize cache, timeout, retry, and failure-degradation policy for these realtime sources.
- R3: wire the data into existing frontend surfaces and complete Browser MCP validation before any default-source change.

## 剩余范围（ZH）
- R2：继续补齐这些实时源的缓存、超时、重试和降级策略。
- R3：把数据接到现有前端页面，并在任何默认来源切换前完成 Browser MCP 验收。