# Fundamentals Snapshot Bugfix Report (2026-03-14)

## EN
### Scope
- Investigated the issue where the fundamentals snapshot appeared empty.
- Verified live upstream interfaces and hardened the backend quote aggregation so fundamentals prefer the working Eastmoney source.

### Findings
- Eastmoney live endpoints are working for fundamentals:
  - quote: `https://push2.eastmoney.com/api/qt/stock/get?secid=...&fields=f58,f43,f60,f170,f10,f117,f162`
  - company survey: `https://emweb.securities.eastmoney.com/PC_HSF10/CompanySurvey/CompanySurveyAjax?code=...`
  - shareholder research: `https://emweb.securities.eastmoney.com/PC_HSF10/ShareholderResearch/PageAjax?code=...`
- Local API verification after the fix:
  - `/api/stocks/detail?symbol=sh600519&interval=day&count=20` returned `floatMarketCap`, `peRatio`, `shareholderCount`, and `sectorName`.

### Development
- Updated `CompositeStockCrawler` to separate realtime quote quality from fundamentals quality.
- When a fundamentals-capable source is present, the aggregator now prefers Eastmoney fundamentals over earlier generic sources.
- Added `CompositeStockCrawlerTests` to lock this behavior.

### Validation
- `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "CompositeStockCrawlerTests|EastmoneyStockParserTests|StockSyncServiceTests"` -> passed (6/6)
- `http://127.0.0.1:5119/api/health` -> `{"status":"ok"}`
- `http://127.0.0.1:5119/api/stocks/detail?symbol=sh600519&interval=day&count=20` -> returned fundamentals successfully

## ZH
### 范围
- 排查“基本面快照看起来完全没有信息”的问题。
- 验证真实上游接口是否可用，并增强后端聚合逻辑，让基本面优先使用当前可用的东财来源。

### 发现
- 东方财富当前基本面接口仍可用：
  - 行情接口：`https://push2.eastmoney.com/api/qt/stock/get?secid=...&fields=f58,f43,f60,f170,f10,f117,f162`
  - 公司概况：`https://emweb.securities.eastmoney.com/PC_HSF10/CompanySurvey/CompanySurveyAjax?code=...`
  - 股东户数：`https://emweb.securities.eastmoney.com/PC_HSF10/ShareholderResearch/PageAjax?code=...`
- 修复后本地接口验证：
  - `/api/stocks/detail?symbol=sh600519&interval=day&count=20` 已返回 `floatMarketCap`、`peRatio`、`shareholderCount`、`sectorName`

### 开发
- 更新 `CompositeStockCrawler`，把“实时行情字段质量”和“基本面字段质量”分开选优。
- 只要存在可用基本面来源，聚合器现在会优先采用东方财富的基本面字段，而不是被前序通用源覆盖。
- 新增 `CompositeStockCrawlerTests` 锁住这个行为。

### 验证
- `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "CompositeStockCrawlerTests|EastmoneyStockParserTests|StockSyncServiceTests"` -> 通过（6/6）
- `http://127.0.0.1:5119/api/health` -> `{"status":"ok"}`
- `http://127.0.0.1:5119/api/stocks/detail?symbol=sh600519&interval=day&count=20` -> 已成功返回基本面字段
