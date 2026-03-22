# GOAL-AGENT-002-P0 Plan Report - 2026-03-22

## English

### Scope

- Close the buglist items that directly block Stock Copilot runtime stability and output safety.

### Plan

- Fix local SQLite schema drift for market sentiment and sector rotation tables so `/api/market/sectors` no longer fails when Development falls back to SQLite.
- Verify the stock terminal chart path still uses `/api/stocks/chart` and fix any remaining runtime stale-asset path if the browser serves outdated bundles.
- Remove unfinished placeholder tabs from the main navigation.
- Fix LLM settings persistence so clearing optional fields such as `Project` actually persists.
- Stop chat history fake-success behavior by serializing chat saves and removing chunk-by-chunk save storms during streaming.
- Sanitize user-facing and developer-mode LLM output so raw reasoning scaffolding is not rendered directly.
- Filter local fact stock news to strong symbol matches only and suppress distorted translated titles on already-clear Chinese headlines.
- Validate with targeted backend tests, targeted frontend tests, and Browser MCP runtime checks.

### Acceptance

- No top-level placeholder social tabs are visible.
- Developer mode shows redacted request/response summaries instead of raw prompt or reasoning text.
- Chat history persistence no longer depends on overlapping save calls.
- LLM settings fields can be explicitly cleared.
- Stock local-news surfaces show only strong stock matches and do not display distorted translated titles.

## 中文

### 范围

- 收口直接阻塞 Stock Copilot 运行稳定性与输出安全的 buglist 条目。

### 计划

- 修复 `情绪轮动` SQLite 本地回退场景下的 schema 漂移，保证 `/api/market/sectors` 不再因为旧表缺列失败。
- 复核股票终端图表是否继续走 `/api/stocks/chart` 轻链路，并处理运行态旧静态资源遮挡新前端的问题。
- 从主导航移除未完成的占位页签。
- 修复 LLM 设置页中 `Project` 等可选字段无法真正清空的持久化问题。
- 将聊天历史保存改为串行落库，消除流式输出期间重复保存造成的假成功与 500。
- 对用户聊天输出和开发者模式日志做脱敏，禁止直接展示 raw reasoning / 中间推理脚手架。
- 对本地事实个股资讯增加强相关过滤，并抑制已是清晰中文标题时的失真翻译展示。
- 用定向后端单测、前端单测和 Browser MCP 做运行态复验。

### 验收标准

- 顶层导航不再出现社媒占位页签。
- 治理开发者模式展示的是脱敏后的请求/返回摘要，而不是原始 prompt 或推理文本。
- 聊天历史保存不再依赖重叠写请求。
- LLM 设置字段可被显式清空。
- 股票页本地资讯只展示强相关个股内容，且不显示失真翻译标题。