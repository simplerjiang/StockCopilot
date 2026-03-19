# GOAL-015 DEV REPORT - SOURCE PROGRESS - 2026-03-18

## English
- Scope: add real loading progress inside the stock info card so Tencent and Eastmoney stages are visible during stock refresh.
- Backend actions:
  - Added `GET /api/stocks/fundamental-snapshot` to fetch Eastmoney fundamental snapshot independently.
  - Extended `GET /api/stocks/detail` with optional `includeFundamentalSnapshot` so the frontend can skip bundled fundamentals during live detail refresh.
- Frontend actions:
  - Added per-workspace stage state for `cache`, `tencent`, and `eastmoney`.
  - Updated stock loading flow to keep cache replay, track Tencent quote progress, fetch Eastmoney fundamentals separately, and merge returned snapshot into the current detail.
  - Added visible progress UI in the stock info card and in the empty loading state.
  - Added a frontend regression test covering the split detail request and staged progress labels.
  - Follow-up fix: added `detail` stage (`K线/分时图表`) so progress cannot reach 100% before `/api/stocks/detail` returns the actual K-line and minute data.
  - Follow-up optimization: start `/api/stocks/detail/cache` and `/api/stocks/detail` in parallel so a slow cache read no longer delays realtime chart data; late cache responses are ignored once live detail has landed.
- Test commands:
  - `dotnet test backend/SimplerJiangAiAgent.Api.Tests/SimplerJiangAiAgent.Api.Tests.csproj --filter "FullyQualifiedName~TencentStockParserTests|FullyQualifiedName~CompositeStockCrawlerTests"`
  - `npm --prefix frontend test -- StockInfoTab.spec.js --runInBand`
  - Browser MCP on `http://localhost:5119/`: clicked recent stock `sz000021`, confirmed progress panel and checked console/network.
- Results:
  - `dotnet test ...TencentStockParserTests|CompositeStockCrawlerTests`: passed, 2/2.
  - `npm --prefix frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`: passed, 40/40.
  - Browser MCP: progress panel rendered `缓存回显 / 腾讯行情 / 东方财富基本面`; console errors 0; network showed `GET /api/stocks/quote?source=腾讯`, `GET /api/stocks/fundamental-snapshot`, and `GET /api/stocks/detail?...includeFundamentalSnapshot=false` all returning 200.
- Issues:
  - Initial implementation over-counted progress by excluding the realtime detail request; this caused the UI to show 100% while charts still waited on `/api/stocks/detail`. Fixed in the same task.

## 中文
- 范围：给股票信息卡片增加真实加载进度，让腾讯与东方财富两个阶段在刷新过程中可见。
- 后端动作：
  - 新增 `GET /api/stocks/fundamental-snapshot`，单独拉取东方财富基本面快照。
  - 给 `GET /api/stocks/detail` 增加可选参数 `includeFundamentalSnapshot`，前端可在实时详情刷新时跳过内联基本面聚合。
- 前端动作：
  - 为每个股票工作区新增 `缓存回显 / 腾讯行情 / 东方财富基本面` 三段状态。
  - 更新查股加载链路：保留缓存秒开，同时跟踪腾讯行情进度、单独拉取东财基本面，并在返回后合并到当前详情。
  - 在股票信息卡片和空态加载区增加可视化进度条与阶段文案。
  - 新增前端回归测试，锁住拆分后的详情请求与阶段进度展示。
  - 后续修正：补上 `K线/分时图表` 阶段，把 `/api/stocks/detail` 主详情请求纳入进度，避免图表未返回时进度条提前 100%。
  - 后续优化：把 `/api/stocks/detail/cache` 与 `/api/stocks/detail` 改成并发发起，慢缓存不再阻塞实时图表；若缓存更晚返回，也不会覆盖已经拿到的实时详情。
- 测试命令：
  - `dotnet test backend/SimplerJiangAiAgent.Api.Tests/SimplerJiangAiAgent.Api.Tests.csproj --filter "FullyQualifiedName~TencentStockParserTests|FullyQualifiedName~CompositeStockCrawlerTests"`
  - `npm --prefix frontend test -- StockInfoTab.spec.js --runInBand`
  - Browser MCP：打开 `http://localhost:5119/`，点击最近查询 `sz000021`，核对进度面板、控制台与网络请求。
- 结果：
  - `dotnet test ...TencentStockParserTests|CompositeStockCrawlerTests` 通过，2/2。
  - `npm --prefix frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js` 通过，40/40。
  - Browser MCP 实页通过：卡片出现“缓存回显 / 腾讯行情 / 东方财富基本面”三段进度；控制台错误 0；网络中 `GET /api/stocks/quote?source=腾讯`、`GET /api/stocks/fundamental-snapshot`、`GET /api/stocks/detail?...includeFundamentalSnapshot=false` 均返回 200。
- 问题：
  - 首版实现漏算了 `/api/stocks/detail`，会让 UI 在图表仍等待实时详情时先显示 100%；已在同一任务内修复。
