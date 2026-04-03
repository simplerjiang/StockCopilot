# 2026-03-23 未解决 Bug 清单

- 说明：本文件只保留当前仍未完全解决、仍需继续验证或继续跟踪的 Bug。
- 已解决项归档：见 `.automation/buglist-resolved-20260323.md`。
- 当前开放项数量：1

## 当前开放项

（Bug 15 仍在跟踪中，详见归档文件。）

## 本轮已修复项（2026-04-02）

### Bug 16: [P0] 推荐系统追问(DirectAnswer)回复未渲染

- 严重级别：高（P0）
- 影响范围：`frontend/src/modules/stocks/StockRecommendTab.vue` handleFollowUp 函数
- 发现时间：2026-04-02（Test Agent + User Representative Agent 验收确认）
- 问题描述：当追问被路由为 DirectAnswer 策略时，后端 API 返回 `{ DirectAnswer: "回答文本" }`，但前端代码在 `strategy === 'DirectAnswer'` 分支中只执行了 `loadSessionDetail` + 切换到辩论页，完全没有渲染或展示 DirectAnswer 文本。追问结果也未持久化到 DB 的 FeedItem 中，因此切换到辩论页后无新内容出现。
- 用户体验影响：用户追问后看到系统显示"已根据现有辩论生成直接回答"，但辩论页没有任何新内容，表现为"卡死"。
- **已修复** ✅
  - 修复时间：2026-04-02
  - 修复内容：后端 DirectAnswer 分支持久化用户追问和直接回答为 FeedItem；前端 handleFollowUp 添加乐观注入 + 90秒超时保护；RecommendFeed.vue 添加 direct_answer 角色展示。
  - 涉及文件：StocksModule.cs, StockRecommendTab.vue, RecommendFeed.vue

### Bug 17: [P0] 团队进度面板缺少重跑/重试按钮

- 严重级别：高（P0）
- 影响范围：`frontend/src/modules/stocks/recommend/RecommendProgress.vue`
- 发现时间：2026-04-02（Test Agent + User Representative Agent 验收确认）
- 问题描述：RecommendProgress.vue 是纯展示组件，失败角色仅显示 ❌ 图标，没有任何重跑按钮、跳过按钮或"从此阶段重跑"按钮。后端 RunPartialTurnAsync 已实现部分重跑功能，但仅通过追问文本路由（FollowUpRouter）间接触发，没有专用 API endpoint。用户看到多处失败后只能"重新推荐"从头来过。
- 用户体验影响：在角色失败率>70%的环境下，无恢复机制等于系统不可用。
- **已修复** ✅
  - 修复时间：2026-04-02
  - 修复内容：新增 `POST /api/recommend/sessions/{id}/retry-from-stage` API；RecommendProgress.vue 添加失败阶段重试按钮、失败角色错误信息、全局"从失败处继续"按钮；StockRecommendTab.vue 增加 handleRetryFromStage 事件处理。
  - 涉及文件：StocksModule.cs, RecommendDtos.cs, RecommendProgress.vue, StockRecommendTab.vue

### Bug 18: [P1] Director角色输出无JSON校验导致Completed但报告为空

- 严重级别：中（P1）
- 影响范围：`frontend/src/modules/stocks/recommend/RecommendReportCard.vue` directorOutput computed + `backend/.../RecommendationRoleExecutor.cs`
- 发现时间：2026-04-02
- 问题描述：当 Director 角色 LLM 输出不是合规 JSON（混入 Markdown 代码块或前缀文字）时，前端 JSON.parse 失败被 try-catch 吞掉，Turn 已标 Completed 但前端报告为空。后端无 schema 校验强制 Director 返回结构化 JSON。此外，前端报告为空时只显示"推荐报告尚未生成，请等待分析完成"——在 Session 已 Completed 时这是误导。
- **已修复** ✅
  - 修复时间：2026-04-02
  - 修复内容：后端 RoleExecutor 添加 TryCleanJsonOutput 方法清洗 LLM 输出的 markdown 代码块包裹；前端 RecommendReportCard.vue 区分 session 终态时的空报告提示。
  - 涉及文件：RecommendationRoleExecutor.cs, RecommendReportCard.vue

### Bug 19: [P1] 推荐流水线 fire-and-forget 无 fallback 事件

- 严重级别：中（P1）
- 影响范围：`backend/.../Modules/Stocks/StocksModule.cs` 推荐 API endpoints 的 Task.Run 块
- 发现时间：2026-04-02
- 问题描述：推荐流水线通过 Task.Run 做 fire-and-forget 执行。如果 runner 在 DI 解析或 DB 查询级别立刻抛异常（在 Runner 内部 try-catch 之前），外层 catch 只 log 不 publish TurnFailed 事件。前端 SSE 连接永远收不到终态事件，僵尸清理需等 10+ 分钟。
- **已修复** ✅
  - 修复时间：2026-04-02
  - 修复内容：4处 Task.Run catch 块均添加 TurnFailed 事件发布和 MarkTurnTerminal 调用。
  - 涉及文件：StocksModule.cs
## Bug 模板

### Bug X: 标题

- 严重级别：高 / 中 / 低
- 当前无开放项。Bug 6、Bug 7、Bug 8 已于 2026-03-24 归档到 `.automation/buglist-resolved-20260323.md`。
	- 2026-03-22 本轮在 `sh600000` 的 `盘中消息带` 与右侧 `资讯影响` 中未再看到原记录里的错字/失真标题样例。
	- 当前样本下暂未复现，但仅覆盖了 `sh600000` 单一标的，建议后续在 `全量资讯库` 再抽样复核，不在本轮直接关闭。

## 人工发现的：
- 20. "策略分析工具 重试后失败","重试 公司概况工具 (第3次, backoff 5000ms)" 
- 21. 股票分析-AI分析 里面依旧有暴露很多没有转换的JSON，不应该直接给用户，需要转化和美化
- 22. 股票分析->历史记录->乱码
- 23. 股票推荐->角色 recommend_smart_money LLM 调用超时，正在重试 (2/3)...
- 24. Antigeravity接口是不是没有联网搜索功能？要打开联网搜索功能，可以去“opencode-antigravity-auth-updated”代码里查找，或者去网上搜索。
- 25. Antigeracity接口老是超时，查看下LLM日志判断下原因。并想个办法解决。
- 26. 切换了Antigeracity接口，但是超时之后依旧弹出default接口的错误提示“角色 资金分析师 执行失败: OpenAI 请求超时，请检查目标网关或本机代理设置。uri=https://api.bltcy.ai/v1/chat/completions”
- 27. 股票推荐的AI分析，看起来LLM只调用了当天的板块数据用来判断市场趋势，这是很明显的错误，市场趋势应该是长时间的（半个月到一个月，甚至两个月），应该MCP要压缩清洗之后返回一段时间的数据。