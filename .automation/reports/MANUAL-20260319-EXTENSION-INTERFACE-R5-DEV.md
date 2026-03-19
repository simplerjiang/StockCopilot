# Extension Interface R5 Development Report / 扩展接口 R5 开发报告

## Development (EN)
- Scope: continue the extension-interface absorption work with the next realtime sector slice and push the new market context into more decision-heavy frontend surfaces.
- Added backend-owned realtime sector board support:
  - `IRealtimeSectorBoardService` / `RealtimeSectorBoardService`
  - `GET /api/market/sectors/realtime`
- Shifted the stock data default preference further toward Eastmoney:
  - `StockDataService.ResolveMinuteSources(...)` now prefers Eastmoney before Tencent
  - `CompositeStockCrawler` reorders sources Eastmoney-first and short-circuits when the Eastmoney quote is already complete
  - `StocksModule` registers `EastmoneyStockCrawler` before Tencent
- Extended realtime market context into more frontend decision surfaces:
  - `MarketSentimentTab.vue` now overlays a realtime board on top of the existing sector list and supports realtime rank refresh/hide controls
  - `StockRecommendTab.vue` now shows `推荐前市场快照` with index, fund-flow, breadth, and concept quick-board context before recommendation runs
  - `StockInfoTab.vue` now adds `市场快链路` to the trading-plan overview while keeping the earlier sidebar realtime card
- Hardened UX for live empty-board conditions by rendering an explicit fallback message instead of breaking the recommendation flow.

## 开发内容（ZH）
- 范围：继续推进扩展接口吸收计划，完成下一批“实时板块快链路”后端切片，并把新的市场实时上下文继续下沉到更多高频决策入口。
- 新增后端自管实时板块榜能力：
  - `IRealtimeSectorBoardService` / `RealtimeSectorBoardService`
  - `GET /api/market/sectors/realtime`
- 进一步把默认行情来源收口到东方财富优先：
  - `StockDataService.ResolveMinuteSources(...)` 改为默认东方财富优先、腾讯回退
  - `CompositeStockCrawler` 改为东方财富优先排序，并在东财行情已经完整时直接走快路径返回
  - `StocksModule` 调整为先注册 `EastmoneyStockCrawler`
- 将实时市场上下文继续下沉到更多前端高频入口：
  - `MarketSentimentTab.vue` 在原有板块榜上叠加 `东财实时榜`，支持独立刷新和显隐
  - `StockRecommendTab.vue` 新增 `推荐前市场快照`，在触发推荐前先展示指数、资金、广度和概念快榜
  - `StockInfoTab.vue` 在交易计划总览顶部新增 `市场快链路`，同时保留此前右侧 `市场实时上下文` 卡片
- 针对实时板块榜在线返回空数组的场景，前端已补上明确空态提示，避免推荐流程被错误中断。

## Validation (EN)
- Backend unit test command:
  - `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "RealtimeSectorBoardServiceTests|StockDataServiceSourceRoutingTests|CompositeStockCrawlerTests"`
- Backend unit test result:
  - build succeeded
  - total 9, failed 0, passed 9, skipped 0
- Frontend unit test command:
  - `npm --prefix .\frontend run test:unit -- src/modules/market/MarketSentimentTab.spec.js src/modules/stocks/StockRecommendTab.spec.js src/modules/stocks/StockInfoTab.spec.js`
- Frontend unit test result:
  - total 50, failed 0, passed 50
- Browser MCP runtime validation:
  - opened the backend-served frontend at `http://localhost:5119/`
  - confirmed `情绪轮动` shows `东财实时榜`
  - confirmed `股票推荐` shows `推荐前市场快照`
  - confirmed the stock terminal / trading-plan overview shows `市场快链路`

## 验证（ZH）
- 后端单测命令：
  - `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "RealtimeSectorBoardServiceTests|StockDataServiceSourceRoutingTests|CompositeStockCrawlerTests"`
- 后端单测结果：
  - 构建成功
  - 总计 9，失败 0，通过 9，跳过 0
- 前端单测命令：
  - `npm --prefix .\frontend run test:unit -- src/modules/market/MarketSentimentTab.spec.js src/modules/stocks/StockRecommendTab.spec.js src/modules/stocks/StockInfoTab.spec.js`
- 前端单测结果：
  - 总计 50，失败 0，通过 50
- Browser MCP 运行时验收：
  - 访问后端托管前端 `http://localhost:5119/`
  - 实测确认 `情绪轮动` 页签出现 `东财实时榜`
  - 实测确认 `股票推荐` 页签出现 `推荐前市场快照`
  - 实测确认股票终端/交易计划总览出现 `市场快链路`

## Issues / 问题
- The live realtime sector endpoint returned empty arrays for both `concept` and `industry` during one validation pass. The frontend now handles this as a valid empty state and keeps recommendation/market views usable.
- 在线验收期间，实时板块榜接口对 `concept` 和 `industry` 一度返回空数组。前端现已把该情况视为合法空态并明确提示，保证推荐页和市场页继续可用。