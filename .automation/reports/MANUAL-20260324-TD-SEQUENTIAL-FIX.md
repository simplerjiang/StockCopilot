# MANUAL-20260324-TD-SEQUENTIAL-FIX

## EN

### Scope

- Investigate whether the current daily TD Sequential / TD9 implementation is wrong.
- If not plainly wrong, optimize it to reduce the observed lag versus common market implementations.

### Root Cause

- The existing implementation in both frontend and backend used strict comparisons only:
  - sell setup required `close > close[4]`
  - buy setup required `close < close[4]`
- When `close == close[4]`, the sequence was fully reset.
- On A-share daily data, equal closes four bars apart are not rare because price increments are discrete.
- That strict reset can eat one or two setup bars and make the chart lag common “神奇九转” implementations by about 1 to 2 bars.

There was also a secondary frontend issue:

- after reaching 9, the chart marker logic reset immediately,
- which allowed the same-direction trend to start emitting a fresh setup again,
- producing repeated TD sequences in an uninterrupted trend.

### Decision

- Treat equality as continuation of the currently active setup direction.
- Keep setup count capped at 9.
- Do not emit repeated same-direction TD markers after the sequence has already completed 9, unless the direction breaks and a new setup starts.

This is a pragmatic alignment with the common A-share “九转 / 神奇九转” expectation, while still staying close to the existing setup-only implementation.

### Code Changes

#### Frontend

- Updated `buildTdSequentialMarkers(...)` in `frontend/src/modules/stocks/charting/chartStrategyRegistry.js`
- Replaced the separate `buyCount` / `sellCount` reset-heavy logic with:
  - `activeDirection`
  - `activeCount`
  - equality-continuation resolution
- Result:
  - equal closes no longer wipe the setup,
  - setup can still progress to 9,
  - repeated same-direction post-9 marker storms are prevented.

#### Backend

- Updated `CalculateTdSequential(...)` in `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/StockCopilotMcpService.cs`
- Added `ResolveTdSetupDirection(...)`
- Backend `td` MCP signal now follows the same equality-continuation rule as the chart.

#### Tests

- Added frontend regression in `frontend/src/modules/stocks/StockCharts.spec.js`
  - synthetic sample includes two equality bars,
  - expected result still reaches `TD卖9`
- Added backend regression in `backend/SimplerJiangAiAgent.Api.Tests/StockCopilotMcpServiceTests.cs`
  - same equality sample,
  - expected MCP `td` signal count is `9`

### Validation

#### Unit Tests

- Command:
  - `npm --prefix .\frontend run test:unit -- src/modules/stocks/StockCharts.spec.js`
- Result:
  - passed, `21/21`

- Command:
  - `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "FullyQualifiedName~StockCopilotMcpServiceTests"`
- Result:
  - passed, `4/4`

#### Browser MCP

- Started runtime with:
  - `.\start-all.bat`
- Verified backend-served stock page at:
  - `http://localhost:5119/?tab=stock-info`
- Observed:
  - page loads successfully,
  - querying `sh600000` succeeds,
  - `TD九转` text is visible on the page,
  - CopilotBrowser console error count is `0`

### Outcome

- The previous lag was not just “user perception”; there was a real implementation bias caused by equality resets.
- The current implementation is now closer to how users commonly interpret A-share TD9 on retail charting tools.
- Frontend chart and backend MCP `td` signal are now aligned on the same continuation rule.

## ZH

### 范围

- 检查当前日 K 九转实现是不是代码写错了。
- 如果不属于纯粹写错，而是口径过严，则做优化，尽量贴近用户常见的九转认知。

### 根因结论

- 前端图表和后端 MCP 的现有实现都用了严格比较：
  - 卖出结构要求 `close > close[4]`
  - 买入结构要求 `close < close[4]`
- 一旦出现 `close == close[4]`，代码就直接把计数清零。
- 在 A 股日线里，四天前收盘价相等并不罕见，因为价格最小变动单位固定。
- 这种“相等即重置”的实现会硬生生吞掉 1 到 2 根 setup bar，所以相对常见“神奇九转”显示会慢 1 到 2 根。

另外前端还有一个附带问题：

- 到 9 以后立刻重置，
- 导致同方向单边趋势里会重新起算一套新的 TD，
- 产生不必要的重复九转标记。

### 本次处理原则

- 相等价位不再直接清零，而是沿用当前正在进行的 setup 方向。
- 计数最多推进到 9。
- 到 9 之后，同方向不再重复打出新一轮标记；只有方向被打断后，才允许下一轮 setup 真正重新开始。

这个处理不是随意“补两根”，而是把原来过严、过脆的 setup 连续性规则改成更符合 A 股常见九转口径的实现。

### 代码改动

#### 前端

- 修改 `frontend/src/modules/stocks/charting/chartStrategyRegistry.js` 中的 `buildTdSequentialMarkers(...)`
- 不再使用原先容易重置的 `buyCount` / `sellCount` 双计数写法，而是改成：
  - `activeDirection`
  - `activeCount`
  - equality-continuation 判定
- 结果：
  - `close == close[4]` 不再把 setup 直接打断，
  - 九转仍能顺利推进到 9，
  - 同方向到 9 后也不会重复刷一轮新的 TD 标记。

#### 后端

- 修改 `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/StockCopilotMcpService.cs`
- 重写 `CalculateTdSequential(...)`
- 新增 `ResolveTdSetupDirection(...)`
- 后端 MCP 的 `td` 信号现在与前端图表采用同一套“相等沿用当前方向”的规则。

#### 测试

- 前端回归测试：`frontend/src/modules/stocks/StockCharts.spec.js`
  - 人工构造了两根 equality bar，
  - 锁定这种情况下依然能到 `TD卖9`
- 后端回归测试：`backend/SimplerJiangAiAgent.Api.Tests/StockCopilotMcpServiceTests.cs`
  - 使用同一组样本，
  - 锁定 MCP `td` 输出计数为 `9`

### 验证

#### 单测

- 命令：
  - `npm --prefix .\frontend run test:unit -- src/modules/stocks/StockCharts.spec.js`
- 结果：
  - 通过，`21/21`

- 命令：
  - `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "FullyQualifiedName~StockCopilotMcpServiceTests"`
- 结果：
  - 通过，`4/4`

#### Browser MCP

- 启动命令：
  - `.\start-all.bat`
- 验证页面：
  - `http://localhost:5119/?tab=stock-info`
- 结果：
  - 页面可正常打开，
  - 查询 `sh600000` 成功，
  - 页面中可见 `TD九转` 文本，
  - CopilotBrowser console 错误数为 `0`

### 结果

- 这次不是单纯“看起来不对”，而是实现层面确实存在会导致滞后的口径偏差。
- 修完后，当前日 K 九转更接近 A 股用户常见的九转显示习惯。
- 前端图表和后端 MCP 的 `td` 口径也已经统一。 