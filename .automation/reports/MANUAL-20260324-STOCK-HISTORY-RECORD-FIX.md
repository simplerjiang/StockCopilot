# MANUAL-20260324-STOCK-HISTORY-RECORD-FIX

## EN

### Scope

- Fix the `Recent Queries` behavior in the stock query toolbar.
- Decouple history recording from `/api/stocks/detail` and move it to an explicit record path triggered only after a successful user-initiated query.

### Root Cause

- The frontend stock query flow no longer uses `/api/stocks/detail` as its main path.
- Recent-query persistence still only happened inside the backend `/api/stocks/detail` endpoint.
- As a result, successful manual queries fetched chart/detail data but never wrote `StockQueryHistories`.
- The frontend also did not prepend or refresh recent history after a successful query, so even a future backend write would not have shown up immediately in the current page state.

### Actions

- Added `StockHistoryRecordRequestDto` in `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Models/StockHistoryRecordRequestDto.cs`.
- Extended `IStockHistoryService` and `StockHistoryService` with `RecordAsync(...)` so explicit history recording can reuse the same normalization and upsert behavior as the legacy quote-based path.
- Added `POST /api/stocks/history` in `backend/SimplerJiangAiAgent.Api/Modules/Stocks/StocksModule.cs` as the dedicated record endpoint.
- Kept the existing `/api/stocks/detail` persistence path intact so current callers are not broken, but history recording is no longer coupled to that endpoint only.
- Updated `frontend/src/modules/stocks/stockInfoTabQuoteRuntime.js` so `fetchQuote()` records history only after the live manual query succeeds.
- Kept `refreshChartData()` untouched so auto refresh, chart polling, and other non-user-triggered refresh paths do not pollute recent history.
- Added `appendRecordedHistory(...)` in `frontend/src/modules/stocks/StockInfoTab.vue` to call the new endpoint and prepend the returned record into `historyList` immediately.
- Added a failure-isolation path so a history-save failure updates `historyError` only and does not overwrite an already successful quote result as a main query failure.

### Tests

- Added backend regression coverage in `backend/SimplerJiangAiAgent.Api.Tests/StockHistoryServiceTests.cs` for explicit record insertion and symbol normalization.
- Added frontend regression coverage in `frontend/src/modules/stocks/StockInfoTab.spec.js` for:
  - recording history after successful manual query,
  - prepending the returned history item immediately,
  - not recording history during background `refreshChartData()`,
  - keeping quote data visible when history recording fails.

### Test Commands And Results

- Command: `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter StockHistoryServiceTests`
- Result: passed, 5/5.

- Command: `npm --prefix .\frontend run test:unit -- .\src\modules\stocks\StockInfoTab.spec.js`
- Result: passed, 63/63.

### Outcome

- Recent queries are now recorded through a dedicated explicit path instead of relying on `/api/stocks/detail` side effects.
- Only successful user-initiated stock queries are persisted.
- The recent-history ribbon is updated immediately in the current page state.
- Auto refresh and chart refresh paths remain excluded from recent-history persistence.

## ZH

### 范围

- 修复“标的查询”工具条里的“最近查询”行为。
- 把历史记录从 `/api/stocks/detail` 的隐式副作用中解耦出来，改成只有“用户主动查询成功后”才走显式记录入口。

### 根因

- 当前前端主查询流程已经不再以 `/api/stocks/detail` 作为主入口。
- 但最近查询的持久化仍然只挂在后端 `/api/stocks/detail` 里。
- 结果就是：用户手动查询虽然成功拿到了图表和详情，但并没有写入 `StockQueryHistories`。
- 同时前端在查询成功后也没有把最新历史前插到当前 `historyList`，所以即使未来某个后端路径写进去了，当前页也不会立刻看到。

### 本轮动作

- 新增 `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Models/StockHistoryRecordRequestDto.cs`，作为独立历史记录请求 DTO。
- 扩展 `IStockHistoryService` 与 `StockHistoryService`，新增 `RecordAsync(...)`，复用既有 symbol 归一化与 upsert 逻辑。
- 在 `backend/SimplerJiangAiAgent.Api/Modules/Stocks/StocksModule.cs` 新增 `POST /api/stocks/history`，作为专门的历史记录入口。
- 保留原有 `/api/stocks/detail` 内的写历史逻辑，避免影响现有调用方，但历史记录不再只依赖这一条旧路径。
- 修改 `frontend/src/modules/stocks/stockInfoTabQuoteRuntime.js`，让 `fetchQuote()` 只在“手动查询成功”后调用历史记录。
- 保持 `refreshChartData()` 不接入历史写入，确保自动刷新、图表轮询等非用户主动行为不会污染最近查询。
- 在 `frontend/src/modules/stocks/StockInfoTab.vue` 新增 `appendRecordedHistory(...)`，调用新接口并把返回记录立即前插到 `historyList`。
- 增加失败隔离：如果历史保存失败，只更新 `historyError`，不把已经成功显示的行情详情误判成主查询失败。

### 测试

- 在 `backend/SimplerJiangAiAgent.Api.Tests/StockHistoryServiceTests.cs` 增加后端回归测试，覆盖显式历史写入与 symbol 归一化。
- 在 `frontend/src/modules/stocks/StockInfoTab.spec.js` 增加前端回归测试，覆盖：
  - 手动查询成功后写入最近查询，
  - 返回记录立即前插显示，
  - 后台 `refreshChartData()` 不应写入最近查询，
  - 历史保存失败时，行情详情仍正常可见。

### 测试命令与结果

- 命令：`dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter StockHistoryServiceTests`
- 结果：通过，5/5。

- 命令：`npm --prefix .\frontend run test:unit -- .\src\modules\stocks\StockInfoTab.spec.js`
- 结果：通过，63/63。

### 结果

- 最近查询现在通过专门的显式入口记录，不再依赖 `/api/stocks/detail` 的副作用。
- 只有用户主动且成功的股票查询会进入最近查询。
- 当前页面的最近查询带会立刻更新，不需要重开页面才能看到。
- 自动刷新和图表刷新路径仍然不会写入最近查询。