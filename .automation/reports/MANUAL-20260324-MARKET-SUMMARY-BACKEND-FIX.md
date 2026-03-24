# MANUAL-20260324-MARKET-SUMMARY-BACKEND-FIX

## EN

### Scope

- Continue the market-sentiment fix below the UI layer.
- Stop `/api/market/sentiment/latest` from blindly returning the newest persisted row when that row is obviously a degraded zero-filled snapshot caused by transient ingestion failures.

### Root Cause

- `SectorRotationIngestionService` intentionally degrades failed upstream fetches to zero-like fallback values so the sync job can keep running.
- `SectorRotationQueryService.GetLatestSummaryAsync()` previously returned the single newest row by `SnapshotTime` with no quality check.
- That meant a newer broken row with `advancers=0`, `decliners=0`, `top3SectorTurnoverShare=0`, and similar missing fields could override an earlier usable snapshot from the same trading day.
- `GetHistoryAsync()` had the same issue at the daily grouping layer because it also picked the latest row of the day without checking whether the row was usable.

### Actions

- Updated `backend/SimplerJiangAiAgent.Api/Modules/Market/Services/SectorRotationQueryService.cs`.
- `GetLatestSummaryAsync()` now loads a recent window of summary rows instead of only the newest one.
- Added a summary integrity scoring rule that rewards rows with:
  - non-zero breadth totals,
  - non-zero total turnover,
  - non-zero sector concentration shares,
  - non-zero market pulse fields such as limit-up / limit-down / broken-board / max-streak.
- The latest summary endpoint now:
  - prefers the highest-integrity snapshot within the latest trading day,
  - falls back to the most recent usable snapshot if the newest trading-day rows are all degraded,
  - only falls back to the newest row when no usable row exists at all.
- `GetHistoryAsync()` now also selects the best daily snapshot by integrity score instead of always taking the last row of the day.

### Tests

- Added `GetLatestSummaryAsync_PrefersLatestUsableSnapshotOverNewerBrokenRow` to `backend/SimplerJiangAiAgent.Api.Tests/SectorRotationQueryServiceTests.cs`.
- Added `GetHistoryAsync_PrefersUsableDailySnapshotWhenLatestRowIsBroken` to the same test file.

### Test Command And Result

- Command: `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "FullyQualifiedName~SectorRotationQueryServiceTests"`
- Result: passed, 6/6.

### Validation Notes

- Focused backend unit validation passed.
- Live endpoint re-check through terminal HTTP helpers was blocked in this environment by command policy (`Invoke-RestMethod` and `curl` denied), and the integrated browser could open the endpoint but did not expose page contents because chat browser tools were unavailable.
- Because of that, this round has strong code-level and unit-level validation, but not an additional direct runtime payload capture beyond the earlier browser acceptance on the page itself.

## ZH

### 范围

- 在前端页面兜底之后，继续往下修 `情绪总览` 的后端查询层。
- 目标是阻止 `/api/market/sentiment/latest` 继续盲目返回“时间最新但字段已经退化成 0”的坏快照。

### 根因

- `SectorRotationIngestionService` 为了保证同步任务不断，会在上游抓取失败时把部分数据降级成 0 值回退，而不是直接让整轮同步失败。
- `SectorRotationQueryService.GetLatestSummaryAsync()` 之前只按 `SnapshotTime` 取最新一条，没有任何质量判断。
- 结果就是：同一交易日里，稍晚一条抓取失败的坏快照会覆盖掉稍早一条其实已经可用的完整快照。
- `GetHistoryAsync()` 也有同类问题，因为它在按交易日分组后，同样是直接取当天最后一条，而不是当天最完整的一条。

### 本轮动作

- 修改了 `backend/SimplerJiangAiAgent.Api/Modules/Market/Services/SectorRotationQueryService.cs`。
- `GetLatestSummaryAsync()` 不再只查最新一条，而是先取最近一段 summary 快照窗口。
- 新增 summary 完整度评分规则，优先保留这些字段正常的记录：
  - 涨跌家数总量不为 0，
  - 成交额不为 0，
  - 板块成交集中度不为 0，
  - 涨停/跌停/炸板/连板高度等市场脉冲字段不全为 0。
- 新逻辑现在会：
  - 先在最新交易日内部挑“最完整”的快照，
  - 如果最新交易日里的记录都明显退化，再回退到最近一条可用快照，
  - 只有在根本没有可用快照时，才退回到单纯时间最新的记录。
- `GetHistoryAsync()` 也同步改成按完整度挑每天最优快照，而不是盲拿当天最后一条。

### 测试

- 在 `backend/SimplerJiangAiAgent.Api.Tests/SectorRotationQueryServiceTests.cs` 新增：
  - `GetLatestSummaryAsync_PrefersLatestUsableSnapshotOverNewerBrokenRow`
  - `GetHistoryAsync_PrefersUsableDailySnapshotWhenLatestRowIsBroken`

### 测试命令与结果

- 命令：`dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "FullyQualifiedName~SectorRotationQueryServiceTests"`
- 结果：通过，6/6。

### 验证说明

- 本轮后端定向单测已经通过。
- 但终端里的 HTTP 回读工具在当前环境被策略拦截了，`Invoke-RestMethod` 和 `curl` 都被 deny；集成浏览器虽然能打开接口地址，但当前没有可读页面内容的 chat browser tools 权限。
- 所以这轮结论是：代码和单测层已经闭合，额外的运行态接口正文抓取在当前工具限制下没法继续自动化补证。