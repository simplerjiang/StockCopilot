# GOAL-015 Fundamental Snapshot Cache Planning Report (2026-03-14)

## EN
### Summary
Planned a cache-first enhancement for the existing GOAL-015 Step 3 fundamentals work: persist richer Eastmoney company-profile/shareholder facts in the database, return them immediately from `/api/stocks/detail/cache`, and refresh them through `/api/stocks/detail` without blocking the first paint.

### Planned Scope
- Extend `StockCompanyProfiles` with a persisted facts JSON blob and an explicit refresh timestamp.
- Expand Eastmoney company-profile parsing so the snapshot includes multiple human-readable facts instead of only sector/shareholder count.
- Add a mapper/service path so cached detail responses can reconstruct a `fundamentalSnapshot` DTO directly from the database.
- Update `/api/stocks/detail` to fetch the richer snapshot in parallel with quote/K-line/minute/messages and persist it together with the live detail payload.
- Update `StockInfoTab.vue` and its unit tests to render the snapshot refresh time plus the top fact items.

### Planned Validation
1. `dotnet ef migrations add AddFundamentalSnapshotCacheFacts ...`
2. `dotnet test backend/SimplerJiangAiAgent.Api.Tests/SimplerJiangAiAgent.Api.Tests.csproj --filter "FullyQualifiedName~CompositeStockCrawlerTests|FullyQualifiedName~EastmoneyCompanyProfileParserTests|FullyQualifiedName~StockSyncServiceTests|FullyQualifiedName~StockDetailCacheQueriesTests"`
3. `npm --prefix frontend run test:unit -- --run src/modules/stocks/StockInfoTab.spec.js`
4. `npm --prefix frontend run build`
5. `dotnet ef database update ...` + SQLCMD column verification + runtime `/api/stocks/detail/cache` / `/api/stocks/detail` verification.

### Risks Noted
- The first cache hit may legitimately be empty before the first live refresh persists facts, so runtime validation must check both “before live refresh” and “after live refresh”.
- The repo already uses both EF migrations and schema initializer patches; both paths need the new columns to avoid future drift.
- Frontend tests that assign `detail` directly must include `fundamentalSnapshot` explicitly, otherwise the new UI assertions will fail for the wrong reason.

## ZH
### 摘要
本轮规划是在 GOAL-015 Step 3 基础上继续补完“缓存优先基本面快照”：把东方财富公司概况/股东研究里的更多可读事实落到数据库里，`/api/stocks/detail/cache` 先直接回放这些事实，`/api/stocks/detail` 再并发做实时刷新并回写，保证首屏快且后续数据新。

### 规划范围
- 为 `StockCompanyProfiles` 增加事实 JSON 与快照刷新时间两个持久化字段。
- 扩展东财公司资料解析，让快照不再只有板块/股东户数，而是包含多条人类可读事实。
- 增加 mapper / service 链路，使缓存详情接口可以直接从数据库还原 `fundamentalSnapshot` DTO。
- 更新 `/api/stocks/detail`，与 quote/K线/分时/消息并行抓取 richer snapshot，并在保存详情时一并持久化。
- 更新 `StockInfoTab.vue` 及其单测，展示快照刷新时间和重点事实列表。

### 计划校验
1. `dotnet ef migrations add AddFundamentalSnapshotCacheFacts ...`
2. `dotnet test backend/SimplerJiangAiAgent.Api.Tests/SimplerJiangAiAgent.Api.Tests.csproj --filter "FullyQualifiedName~CompositeStockCrawlerTests|FullyQualifiedName~EastmoneyCompanyProfileParserTests|FullyQualifiedName~StockSyncServiceTests|FullyQualifiedName~StockDetailCacheQueriesTests"`
3. `npm --prefix frontend run test:unit -- --run src/modules/stocks/StockInfoTab.spec.js`
4. `npm --prefix frontend run build`
5. `dotnet ef database update ...` + SQLCMD 列级校验 + `/api/stocks/detail/cache` / `/api/stocks/detail` 运行时验证。

### 风险记录
- 首次缓存命中在实时刷新前可能本来就是空的，所以运行验证必须区分“实时抓取前”和“实时抓取回写后”两个阶段。
- 仓库同时维护 EF migration 与 schema initializer，两条路径都要补列，避免后续结构漂移。
- 前端单测里如果直接赋值 `detail` 却忘了带 `fundamentalSnapshot`，会导致新断言失败，但那不是真实功能错误。