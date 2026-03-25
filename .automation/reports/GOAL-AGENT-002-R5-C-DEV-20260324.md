# GOAL-AGENT-002-R5-C Development Report (2026-03-24)

## EN

### Scope

- Clean up noisy `Evidence / Source` snippets in Stock Copilot.
- Close Bug 10 by fixing the evidence summary path at the source instead of only masking the UI symptom.

### Actions

- Added reusable evidence-snippet sanitization in `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/LocalFactDisplayPolicy.cs`.
- The new sanitizer:
  - prefers structured `summary` / `excerpt` candidates,
  - strips common navigation noise such as `财经 焦点 股票 新股 期指 期权 行情 数据 全球 美股 港股 基金`,
  - normalizes whitespace,
  - trims output to a short readable snippet instead of a raw scrape fragment.
- Updated `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/StockCopilotMcpService.cs` so both local-news evidence and external-search evidence now emit sanitized `Excerpt` / `Summary` values.
- Added a frontend defensive layer in `frontend/src/modules/stocks/StockCopilotSessionPanel.vue` so the Copilot evidence list renders a cleaned `snippet` field rather than blindly showing raw `excerpt` text.
- 2026-03-25 follow-up: extended `frontend/src/modules/stocks/StockCopilotSessionPanel.vue` so evidence cards stay collapsed by default, then expand inline to reveal a fuller cleaned detail block plus a `查看原文` link.
- Added backend regression coverage in `backend/SimplerJiangAiAgent.Api.Tests/StockCopilotMcpServiceTests.cs`.
- Added frontend regression coverage in `frontend/src/modules/stocks/StockInfoTab.spec.js`.
- The new frontend regression also locks the collapsed-by-default behavior and the expand-on-demand detail view.

### Tests

- Backend targeted test command:
  - `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "StockCopilotMcpServiceTests"`
  - Result: passed, 5/5.
- Frontend targeted test command:
  - `npm --prefix .\frontend run test:unit -- .\src\modules\stocks\StockInfoTab.spec.js`
  - Result: passed, 61/61.
- Frontend follow-up test command:
  - `npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
  - Result: passed, 64/64.
- Frontend build command:
  - `npm --prefix .\frontend run build`
  - Result: passed.

### Issues / Notes

- This slice fixes the evidence-summary readability path for current Copilot cards.
- The current follow-up now covers inline expand/collapse within the evidence card itself.
- A future richer drawer is still optional if the product later needs a dedicated audit/full-text surface.

## ZH

### 范围

- 清理 Stock Copilot 的 `Evidence / Source` 摘要噪音。
- 这次直接修复证据摘要链路本身，关闭 Bug 10，而不是只在界面上掩盖脏文案。

### 本轮动作

- 在 `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/LocalFactDisplayPolicy.cs` 新增可复用的 evidence snippet 清洗逻辑。
- 新清洗器会：
  - 优先使用结构化的 `summary` / `excerpt` 候选，
  - 过滤 `财经 焦点 股票 新股 期指 期权 行情 数据 全球 美股 港股 基金` 这类常见站点导航噪音，
  - 统一空白字符，
  - 把输出裁成 1-2 句可读摘要，而不是直接透传抓取残片。
- 更新 `backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/StockCopilotMcpService.cs`，让本地新闻 evidence 和外部搜索 evidence 的 `Excerpt` / `Summary` 都统一改为清洗后的摘要。
- 在 `frontend/src/modules/stocks/StockCopilotSessionPanel.vue` 增加前端兜底清洗，Evidence 列表默认渲染清洗后的 `snippet`，不再直接盲显原始 `excerpt`。
- 2026-03-25 follow-up：继续补齐 `frontend/src/modules/stocks/StockCopilotSessionPanel.vue` 的展开层，Evidence 卡默认保持摘要态，点击 `展开查看更多` 后会在当前卡片内展开更完整的清洗后 detail 文本，并保留 `查看原文` 链接。
- 在 `backend/SimplerJiangAiAgent.Api.Tests/StockCopilotMcpServiceTests.cs` 新增后端回归测试。
- 在 `frontend/src/modules/stocks/StockInfoTab.spec.js` 新增前端回归测试。
- 新增前端回归同时锁定“默认折叠”和“按需展开 detail”这两个交互语义。

### 测试

- 后端定向测试命令：
  - `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "StockCopilotMcpServiceTests"`
  - 结果：通过，5/5。
- 前端定向测试命令：
  - `npm --prefix .\frontend run test:unit -- .\src\modules\stocks\StockInfoTab.spec.js`
  - 结果：通过，61/61。
- 前端 follow-up 测试命令：
  - `npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`
  - 结果：通过，64/64。
- 前端构建命令：
  - `npm --prefix .\frontend run build`
  - 结果：通过。

### 当前说明

- 本切片已经修复当前 Copilot 证据卡的摘要可读性问题。
- 当前 follow-up 已补上卡片内的展开查看能力，不再只剩纯摘要态。
- 如果后续还要做更强的全文浏览体验，可以再升级成独立详情抽屉，但那已不再是 R5-C 当前缺口。