# MANUAL-20260320-VUE-READABILITY

## English

### Scope
- Split long Vue pages into smaller child components without changing data flow or user-visible behavior.

### Actions
- Extracted `frontend/src/modules/stocks/StockAgentCard.vue` from `StockAgentPanels.vue` to isolate per-agent rendering, evidence cards, metrics, tags, and raw JSON toggles.
- Extracted `frontend/src/modules/market/MarketRealtimeOverview.vue` from `MarketSentimentTab.vue` to isolate the realtime deck for indices, capital flow, and breadth buckets.
- Extracted `frontend/src/modules/stocks/StockSourceLoadProgress.vue` from `StockInfoTab.vue` to reuse the stock loading progress block in both loaded and empty terminal states.
- Updated parent pages to delegate rendering to the new child components while preserving existing state ownership and event flow.

### Verification
- Command: `npm --prefix .\frontend run test:unit -- src/modules/stocks/StockAgentPanels.spec.js src/modules/market/MarketSentimentTab.spec.js src/modules/stocks/StockInfoTab.spec.js`
- Result: passed, 57/57 tests.
- Browser MCP:
  - `http://localhost:5119/?tab=stock-info` rendered normally after the split.
  - `http://localhost:5119/?tab=market-sentiment` rendered the extracted realtime overview section, including `资金与广度` and `涨跌分布桶`.

### Issues
- Browser MCP captured an existing runtime/backend issue on `GET /api/market/sectors?boardType=concept&page=1&pageSize=12&sort=strength` during market page validation.
- The page still showed the extracted realtime overview correctly, so this is recorded as a pre-existing service-state issue rather than a refactor regression.

## 中文

### 范围
- 在不改数据流和交互行为的前提下，把过长的 Vue 页面拆成更容易阅读的小组件。

### 动作
- 从 `frontend/src/modules/stocks/StockAgentPanels.vue` 中提取 `StockAgentCard.vue`，收口单个 Agent 卡片的证据、指标、标签和原始 JSON 展示。
- 从 `frontend/src/modules/market/MarketSentimentTab.vue` 中提取 `MarketRealtimeOverview.vue`，收口实时总览区的指数快照、资金与广度、涨跌分布桶。
- 从 `frontend/src/modules/stocks/StockInfoTab.vue` 中提取 `StockSourceLoadProgress.vue`，复用股票加载进度区块，覆盖已加载和空态两种终端展示。
- 父页面只保留状态、请求和事件编排，模板阅读密度明显下降。

### 验证
- 命令：`npm --prefix .\frontend run test:unit -- src/modules/stocks/StockAgentPanels.spec.js src/modules/market/MarketSentimentTab.spec.js src/modules/stocks/StockInfoTab.spec.js`
- 结果：通过，57/57。
- Browser MCP：
  - `http://localhost:5119/?tab=stock-info` 页面渲染正常。
  - `http://localhost:5119/?tab=market-sentiment` 页面中拆出的实时总览区正常显示，`资金与广度`、`涨跌分布桶` 都能看到。

### 问题
- Browser MCP 验证情绪页时，`/api/market/sectors?boardType=concept&page=1&pageSize=12&sort=strength` 返回了现有服务端错误。
- 拆出的实时总览区仍然正常渲染，因此记录为当前运行环境中的已有后端问题，不判定为本次模板拆分回归。