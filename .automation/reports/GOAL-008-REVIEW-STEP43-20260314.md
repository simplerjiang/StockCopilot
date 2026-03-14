# GOAL-008 Step 4.3 Review Report (2026-03-14)

## EN
### Review Conclusion
Step 4.3 is not accepted in its current form. The first implementation pass established the main skeleton, endpoint, event table, worker, and frontend alert surface, and the claimed targeted tests do pass. However, the delivered behavior still misses core acceptance requirements from the approved Step 4.3 definition, so this task must return to Dev1 for rework before Step 4.4 can begin.

### What Was Verified
- Backend implementation exists for:
  - `TradingPlanTriggerService`
  - `TradingPlanTriggerWorker`
  - `TradingPlanEvent` entity and `TradingPlanEvents` table path
  - `/api/stocks/plans/alerts`
- Frontend implementation exists for:
  - alert fetch state in `StockInfoTab.vue`
  - board/current-plan alert summaries
  - current-stock short polling path
- Re-run validation in this review:
  - `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "TradingPlanTriggerServiceTests|TradingPlanServicesTests|HighFrequencyQuoteServiceTests" -v minimal`
  - Result: pass `18/18`
  - `npm run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
  - Result: pass `36/36`
  - static diagnostics on touched backend/frontend files: no errors

### Blocking Findings
1. The trigger engine does not use `ActiveWatchlist` as its execution boundary.
   - Current code loads all `Pending` plans directly.
   - This breaks the Step 4.3 scope contract, which required the worker to consume `ActiveWatchlist` plus local cache rather than scanning the full plan set.
2. Warning-event idempotency is insufficient.
   - Volume-divergence warnings are deduplicated only by exact `MetadataJson` string equality.
   - As soon as minute data shifts and the serialized payload changes, the same ongoing divergence can create repeated warnings on later passes.
   - Step 4.3 acceptance requires repeated polling not to keep writing same-kind events for the same ongoing condition.
3. Short polling is incomplete on the frontend.
   - The active-stock plan section refreshes on a timer.
   - The global trading-plan board does not participate in the same continuous refresh path and still depends mainly on initial load or manual refresh.
   - Step 4.3 required both the current-plan surface and board-level summary to stay updated through short polling.

### Required Rework Scope
1. Gate `TradingPlanTriggerService` by `ActiveWatchlist`.
2. Replace warning dedupe-by-raw-JSON with dedupe-by-condition/window semantics.
3. Extend short polling so both:
   - current-stock plan card
   - global trading-plan board
   refresh continuously without SignalR.
4. Add/adjust tests to prove the above three behaviors explicitly.

### Retest Requirements
Dev1 may resubmit Step 4.3 only after all of the following pass again:
- backend targeted tests
- frontend targeted tests
- frontend build
- SQLCMD schema/index verification for `TradingPlanEvents`
- backend-served Browser MCP validation that confirms both board and current-plan alert surfaces refresh correctly

## ZH
### 评审结论
Step 4.3 当前版本不予通过。首版实现已经把主框架、接口、事件表、后台 worker 和前端告警展示壳层搭起来了，且声称的定向测试也确实能跑通；但实际行为仍然没有满足已批准的 Step 4.3 定义，因此必须退回 Dev1 返工，修完后才能进入 Step 4.4。

### 本轮已核对内容
- 后端已看到以下实现：
  - `TradingPlanTriggerService`
  - `TradingPlanTriggerWorker`
  - `TradingPlanEvent` 实体与 `TradingPlanEvents` 表路径
  - `/api/stocks/plans/alerts`
- 前端已看到以下实现：
  - `StockInfoTab.vue` 中的 alert 读取状态
  - 总览卡 / 当前计划卡的告警摘要展示
  - 当前股票计划区的短轮询路径
- 本轮复测结果：
  - `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "TradingPlanTriggerServiceTests|TradingPlanServicesTests|HighFrequencyQuoteServiceTests" -v minimal`
  - 结果：通过 `18/18`
  - `npm run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
  - 结果：通过 `36/36`
  - 相关后端/前端文件静态诊断：无报错

### 阻塞问题
1. 触发引擎没有把 `ActiveWatchlist` 当作执行边界。
   - 当前代码直接读取全部 `Pending` 计划。
   - 这违反了 Step 4.3 的范围定义。该步要求 worker 依赖 `ActiveWatchlist + 本地缓存` 做判断，而不是绕过白名单去全表扫描计划。
2. warning 事件幂等不合格。
   - 量价背离 warning 当前只按 `MetadataJson` 精确字符串相等做去重。
   - 只要后续分钟数据略有变化，序列化 payload 改了，同一段持续背离就可能再次落同类 warning。
   - Step 4.3 的验收要求是：连续轮询不能对同一持续条件反复写入同类事件。
3. 前端短轮询覆盖不完整。
   - 当前股票计划区会定时刷新。
   - 但全局“交易计划总览”没有进入同一条持续刷新链路，主要仍依赖首屏加载或手动点刷新。
   - Step 4.3 要求当前计划区和总览面都能通过短轮询持续看到最新状态/告警。

### 返工要求
1. `TradingPlanTriggerService` 必须接回 `ActiveWatchlist` 白名单门控。
2. warning 去重必须从“原始 JSON 字符串相等”改为“按条件/时间窗语义去重”。
3. 短轮询必须同时覆盖：
   - 当前股票计划卡
   - 交易计划总览
4. 必须补或调整测试，显式锁住以上三条行为。

### 复测要求
Dev1 返工后，至少要重新通过以下验证，才允许再次提测：
- 后端定向单测
- 前端定向单测
- 前端 build
- `TradingPlanEvents` 的 SQLCMD 字段/索引核验
- 后端托管页面 Browser MCP，且必须确认总览与当前计划两处告警都能持续刷新
