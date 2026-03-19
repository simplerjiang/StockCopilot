# Extension Interface R2/R3 Development Report / 扩展接口 R2/R3 开发报告

## Development (EN)
- Scope: finish the second and third adoption slices for the `stock-and-fund-chrome-master` interface plan.
- R2 hardened the backend realtime aggregation path in `RealtimeMarketOverviewService` with:
  - per-key single-flight gates
  - fresh vs stale cache windows
  - short request timeouts
  - stale-cache fallback when upstream refresh fails
- R3 integrated `/api/market/realtime/overview` into the existing market dashboard in `MarketSentimentTab.vue`.
- The frontend now renders three realtime cards:
  - index snapshot
  - capital-flow and breadth summary
  - breadth distribution buckets
- The realtime block is isolated from the rest of the page with its own loading, error, refresh, and localStorage-backed visibility state so sector rotation still works even if realtime data fails.

## 开发内容（ZH）
- 范围：完成 `stock-and-fund-chrome-master` 接口吸收计划中的第二、第三个切片。
- R2 重点加固了 `RealtimeMarketOverviewService` 的后端实时聚合链路，补齐：
  - 按 key 单飞锁
  - fresh/stale 双缓存窗口
  - 短超时保护
  - 上游刷新失败时返回 stale cache 的降级能力
- R3 把 `/api/market/realtime/overview` 接入现有 `MarketSentimentTab.vue` 市场页。
- 前端新增三块实时卡片：
  - 指数快照
  - 资金与广度摘要
  - 涨跌分布桶
- 这块实时区域拥有独立的 loading、error、refresh 和 localStorage 可见性状态，因此即使实时接口失败，原有板块轮动面板也不会被拖垮。

## Validation (EN)
- Backend unit test command:
  - `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "RealtimeMarketOverviewServiceTests|EastmoneyRealtimeMarketClientTests"`
- Backend unit test result:
  - build succeeded
  - total 7, failed 0, passed 7, skipped 0
- Frontend unit test command:
  - `npm --prefix .\frontend run test:unit -- src/modules/market/MarketSentimentTab.spec.js`
- Frontend unit test result:
  - total 3, failed 0, passed 3
- Browser MCP runtime validation:
  - launched the local stack at `http://localhost:5119/`
  - switched to the `情绪轮动` tab
  - confirmed runtime rendering of `Realtime Tape`, `指数快照`, `资金与广度`, and `涨跌分布桶`
  - confirmed the backend endpoint returned live market payloads during the same session

## 验证（ZH）
- 后端单测命令：
  - `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "RealtimeMarketOverviewServiceTests|EastmoneyRealtimeMarketClientTests"`
- 后端单测结果：
  - 构建成功
  - 总计 7，失败 0，通过 7，跳过 0
- 前端单测命令：
  - `npm --prefix .\frontend run test:unit -- src/modules/market/MarketSentimentTab.spec.js`
- 前端单测结果：
  - 总计 3，失败 0，通过 3
- Browser MCP 运行时验收：
  - 在 `http://localhost:5119/` 启动并访问本地应用
  - 切换到 `情绪轮动` 页签
  - 实测确认 `Realtime Tape`、`指数快照`、`资金与广度`、`涨跌分布桶` 已渲染
  - 同一会话内也确认后台接口返回了实时市场 payload

## Issues / 问题
- The first browser reload landed back on the stock page and made the new market cards look missing. Re-entering the `情绪轮动` tab in the same live session confirmed the UI was correct; this was a navigation-state issue rather than a code regression.
- 首次浏览器 reload 会回到股票页，导致新卡片看起来像“没生效”。在同一会话中重新进入 `情绪轮动` 后已确认 UI 正常，这属于导航状态问题，不是代码回退。