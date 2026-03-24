# MANUAL-20260324-MARKET-INGESTION-WRITE-GATE

## EN

### Scope

- Continue the market-sentiment hardening below the query layer.
- Reduce bad zero-valued `MarketSentimentSnapshot` rows at the ingestion write path instead of only masking them later during reads.

### Root Cause

- `SectorRotationIngestionService` intentionally downgrades failed upstream calls to fallback values so sector-rotation sync can continue.
- Before this change, the service still always created and persisted `MarketSentimentSnapshot`, even when critical market sources such as breadth, limit-up, limit-down, broken-board, or max-streak had already failed and been replaced by zero-like fallbacks.
- That behavior allowed broken summary rows to enter the database and then compete with usable rows downstream.

### Actions

- Updated `backend/SimplerJiangAiAgent.Api/Modules/Market/Services/SectorRotationIngestionService.cs`.
- Replaced the old fallback helper with a result wrapper that keeps both:
  - the fallback value,
  - whether the source fetch actually succeeded.
- Added a strict `ShouldPersistMarketSnapshot(...)` gate.
- `MarketSentimentSnapshot` is now persisted only when all critical market sources succeeded and the resulting summary still has basic usable structure:
  - breadth total > 0,
  - total turnover > 0,
  - top-3 or top-10 turnover share > 0.
- When the gate fails, the service logs a warning and skips only `MarketSentimentSnapshot` persistence.
- Sector rotation snapshots and leader snapshots still persist, so partial market-source failure no longer poisons market summary rows and also no longer aborts the full sector sync.
- Updated rolling-metric handling so it safely skips market-summary rolling calculations when the summary row is intentionally not written.

### Tests

- Added `SyncAsync_SkipsMarketSentimentSnapshot_WhenCriticalMarketSourceFails` to `backend/SimplerJiangAiAgent.Api.Tests/SectorRotationIngestionServiceTests.cs`.
- Extended the fake Eastmoney client in the same test file so individual critical market-source calls can throw deterministically.
- The new regression verifies:
  - failed critical market source => no `MarketSentimentSnapshot` persisted,
  - sector snapshot still persists,
  - sector leader snapshot still persists.

### Test Command And Result

- Command: `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "FullyQualifiedName~SectorRotationIngestionServiceTests"`
- Result: passed, 7/7.

### Outcome

- The query layer now has less bad data to defend against because broken zero-filled summary rows are blocked before persistence.
- The sector-rotation sync remains resilient, but market-summary persistence is now quality-gated instead of unconditional.

## ZH

### 范围

- 在查询层兜底之后，继续往下修 `情绪总览` 的 ingestion 写入层。
- 目标不是继续“读取时挑好数据”，而是直接减少坏的 0 值 `MarketSentimentSnapshot` 落库。

### 根因

- `SectorRotationIngestionService` 为了保证同步不中断，会把抓取失败的上游数据降级成 fallback 值继续往下走。
- 但在这次修改之前，即使关键市场源已经失败，服务仍然会无条件创建并保存 `MarketSentimentSnapshot`。
- 这意味着：涨跌家数、涨停跌停、炸板、连板高度等关键字段一旦被 fallback 成 0，坏 summary 也会像正常数据一样进库，后续还会污染查询结果。

### 本轮动作

- 修改了 `backend/SimplerJiangAiAgent.Api/Modules/Market/Services/SectorRotationIngestionService.cs`。
- 把原先只返回 fallback 值的抓取辅助逻辑，改成同时保留：
  - 返回值，
  - 该次抓取是否真实成功。
- 新增严格的 `ShouldPersistMarketSnapshot(...)` 质量门。
- 现在只有在以下条件都满足时，才允许写入 `MarketSentimentSnapshot`：
  - 市场广度抓取成功，
  - 涨停池抓取成功，
  - 跌停池抓取成功，
  - 炸板池抓取成功，
  - 连板高度抓取成功，
  - 且汇总后的 breadth 总量大于 0，
  - 总成交额大于 0，
  - Top3 / Top10 板块成交占比至少有一项大于 0。
- 如果不满足条件：
  - 只跳过 `MarketSentimentSnapshot` 落库，
  - 记录 warning，
  - 板块轮动快照和龙头快照仍然照常保存。
- 同时调整 rolling metrics 逻辑，让它在 summary 被主动跳过时安全退出，不再假设 summary 一定存在。

### 测试

- 在 `backend/SimplerJiangAiAgent.Api.Tests/SectorRotationIngestionServiceTests.cs` 新增：
  - `SyncAsync_SkipsMarketSentimentSnapshot_WhenCriticalMarketSourceFails`
- 同时扩展同一文件里的 fake client，让单个关键市场源可以定向抛异常。
- 新回归用例锁定了三件事：
  - 关键市场源失败时，不再写入坏的 `MarketSentimentSnapshot`；
  - `SectorRotationSnapshot` 仍然会保存；
  - `SectorRotationLeaderSnapshot` 仍然会保存。

### 测试命令与结果

- 命令：`dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "FullyQualifiedName~SectorRotationIngestionServiceTests"`
- 结果：通过，7/7。

### 结果

- 现在查询层不再需要频繁替坏 summary 擦屁股，因为一部分 0 值坏快照已经在落库前被拦下。
- 同步链路仍然保持“尽量不中断”，但 summary 写入已经从“无条件”改成“有质量门才允许落库”。
