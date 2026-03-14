# GOAL-015 Fundamental Snapshot Cache Development Report (2026-03-14)

## EN
### Scope
- Completed the cache-first fundamentals snapshot enhancement for GOAL-015 Step 3.
- The stock detail flow now persists richer Eastmoney fundamentals facts, serves them from the database cache on open, and refreshes them live in the background.

### Development
- Backend persistence:
  - Added `FundamentalFactsJson` and `FundamentalUpdatedAt` to `StockCompanyProfiles`.
  - Added `StockFundamentalSnapshotMapper` so cached company-profile rows can be rehydrated into `StockFundamentalSnapshotDto`.
- Eastmoney parsing/fetching:
  - Expanded `EastmoneyCompanyProfileParser` to emit richer fact items such as company full name, exchange, industry, region, address, website, management fields, and shareholder research facts.
  - Added `EastmoneyFundamentalSnapshotService` and `IStockFundamentalSnapshotService` for live snapshot fetching.
- Detail/cache integration:
  - `StockDetailDto` now carries `fundamentalSnapshot`.
  - `/api/stocks/detail/cache` reconstructs the snapshot directly from `StockCompanyProfiles`.
  - `/api/stocks/detail` fetches the live snapshot in parallel with quote/K-line/minute/messages and persists it through `StockSyncService`.
- Frontend:
  - `StockInfoTab.vue` now renders the snapshot refresh time and top fact items under the fundamentals card.
  - Updated `StockInfoTab.spec.js` to cover the new snapshot rendering.
- Database:
  - Generated migration `AddFundamentalSnapshotCacheFacts` and applied it locally.

### Validation
- `dotnet ef migrations add AddFundamentalSnapshotCacheFacts --project backend/SimplerJiangAiAgent.Api/SimplerJiangAiAgent.Api.csproj --startup-project backend/SimplerJiangAiAgent.Api/SimplerJiangAiAgent.Api.csproj --context AppDbContext` -> passed
- `dotnet test backend/SimplerJiangAiAgent.Api.Tests/SimplerJiangAiAgent.Api.Tests.csproj --filter "FullyQualifiedName~CompositeStockCrawlerTests|FullyQualifiedName~EastmoneyCompanyProfileParserTests|FullyQualifiedName~StockSyncServiceTests|FullyQualifiedName~StockDetailCacheQueriesTests"` -> passed (7/7)
- `npm --prefix frontend run test:unit -- --run src/modules/stocks/StockInfoTab.spec.js` -> passed (30/30)
- `npm --prefix frontend run build` -> passed
- `dotnet ef database update --project backend/SimplerJiangAiAgent.Api/SimplerJiangAiAgent.Api.csproj --startup-project backend/SimplerJiangAiAgent.Api/SimplerJiangAiAgent.Api.csproj --context AppDbContext` -> passed
- `sqlcmd -S 'np:\\.\pipe\MSSQL$SQLEXPRESS\sql\query' -d SimplerJiangAiAgent -E -C ...` -> verified `FundamentalFactsJson` and `FundamentalUpdatedAt` exist on `StockCompanyProfiles`
- Runtime checks:
  - `GET /api/health` -> `{"status":"ok"}`
  - first `GET /api/stocks/detail/cache?symbol=sz000021&interval=day&count=20` -> no persisted facts yet (expected before first live refresh)
  - `GET /api/stocks/detail?symbol=sz000021&interval=day&count=20` -> returned 21 fundamental facts, `sectorName=消费电子`, `shareholderCount=225861`
  - second `GET /api/stocks/detail/cache?symbol=sz000021&interval=day&count=20` -> returned 21 persisted facts from the database cache, confirming the cache-first reopen path works

### Notes
- This slice is functionally complete from code, schema, unit-test, build, and runtime API verification perspectives.
- Remaining GOAL-015 work is still broader UI/Edge validation plus any later integration scope outside this fundamentals snapshot path.

## ZH
### 范围
- 已完成 GOAL-015 Step 3 的“缓存优先基本面快照”增强。
- 现在股票详情链路会把更丰富的东方财富基本面事实落库，并在页面打开时优先从数据库缓存返回，再由后台实时刷新补新数据。

### 开发
- 后端持久化：
  - 为 `StockCompanyProfiles` 新增 `FundamentalFactsJson` 与 `FundamentalUpdatedAt`。
  - 新增 `StockFundamentalSnapshotMapper`，把缓存公司资料行还原成 `StockFundamentalSnapshotDto`。
- 东财解析/抓取：
  - 扩展 `EastmoneyCompanyProfileParser`，输出公司全称、上市交易所、行业、地区、地址、网站、管理层、股东研究等更丰富的事实项。
  - 新增 `EastmoneyFundamentalSnapshotService` / `IStockFundamentalSnapshotService` 负责实时抓取 richer snapshot。
- 详情/缓存接入：
  - `StockDetailDto` 已新增 `fundamentalSnapshot`。
  - `/api/stocks/detail/cache` 会直接从 `StockCompanyProfiles` 还原快照。
  - `/api/stocks/detail` 会与 quote/K线/分时/消息并发抓取 live snapshot，并通过 `StockSyncService` 回写数据库。
- 前端：
  - `StockInfoTab.vue` 已在“基本面快照”卡片下展示快照刷新时间和事实列表。
  - `StockInfoTab.spec.js` 已补齐新的快照渲染断言。
- 数据库：
  - 已生成并本地应用 `AddFundamentalSnapshotCacheFacts` migration。

### 验证
- `dotnet ef migrations add AddFundamentalSnapshotCacheFacts --project backend/SimplerJiangAiAgent.Api/SimplerJiangAiAgent.Api.csproj --startup-project backend/SimplerJiangAiAgent.Api/SimplerJiangAiAgent.Api.csproj --context AppDbContext` -> 通过
- `dotnet test backend/SimplerJiangAiAgent.Api.Tests/SimplerJiangAiAgent.Api.Tests.csproj --filter "FullyQualifiedName~CompositeStockCrawlerTests|FullyQualifiedName~EastmoneyCompanyProfileParserTests|FullyQualifiedName~StockSyncServiceTests|FullyQualifiedName~StockDetailCacheQueriesTests"` -> 通过（7/7）
- `npm --prefix frontend run test:unit -- --run src/modules/stocks/StockInfoTab.spec.js` -> 通过（30/30）
- `npm --prefix frontend run build` -> 通过
- `dotnet ef database update --project backend/SimplerJiangAiAgent.Api/SimplerJiangAiAgent.Api.csproj --startup-project backend/SimplerJiangAiAgent.Api/SimplerJiangAiAgent.Api.csproj --context AppDbContext` -> 通过
- `sqlcmd -S 'np:\\.\pipe\MSSQL$SQLEXPRESS\sql\query' -d SimplerJiangAiAgent -E -C ...` -> 已确认 `StockCompanyProfiles` 上存在 `FundamentalFactsJson` 与 `FundamentalUpdatedAt`
- 运行时检查：
  - `GET /api/health` -> `{"status":"ok"}`
  - 第一次 `GET /api/stocks/detail/cache?symbol=sz000021&interval=day&count=20` -> 还没有持久化事实（符合首次 live 刷新前预期）
  - `GET /api/stocks/detail?symbol=sz000021&interval=day&count=20` -> 返回 21 条基本面事实，`sectorName=消费电子`，`shareholderCount=225861`
  - 第二次 `GET /api/stocks/detail/cache?symbol=sz000021&interval=day&count=20` -> 已从数据库缓存返回 21 条事实，证明“先缓存秒开、再实时刷新回写、下次直接秒开”的链路成立

### 说明
- 这一轮从代码、数据库结构、单测、build 到运行时接口验证都已闭环。
- GOAL-015 剩余工作主要还是更大范围的 Edge/UI 验收，以及超出本次基本面快照路径的后续集成项。