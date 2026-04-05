# 2026-04-03 未解决 Bug 清单

- 说明：本文件只保留当前仍未完全解决、仍需继续验证或继续跟踪的 Bug。
- 已解决项归档：见 `.automation/buglist-resolved-20260323.md`。
- 当前开放项数量：3

## 当前开放项（2026-04-03 回归测试发现）

### Bug 28: [P2] /api/stocks/research/active-session 返回 404 触发控制台错误
- 严重级别：低（P2）
- 模块：股票终端 API
- 描述：查询没有活跃研究会话的股票时返回 HTTP 404，前端未静默处理，浏览器控制台出现红色错误。应返回 200 + 空数据。
- 影响：不阻塞功能，但每次切股都产生控制台错误
- **已修复** ✅
  - 修复内容：`Results.NotFound()` → `Results.Ok(dto)` 返回 200+null

### Bug 29: [P2] 侧边栏 3 只股票显示"示例名称"占位符
- 严重级别：低（P2）
- 模块：股票终端侧边栏
- 描述：sh688428、sh688331、sz002085 显示为"示例名称"而非真实股票名
- 影响：数据质量问题
- **已修复** ✅
  - 修复内容：fallback 名称从"示例名称"改为空字符串，前端用 symbol 兜底

### Bug 30: [P2] 版本号显示 v0.0.3 而非 v0.2.1
- 严重级别：低（P2）
- 模块：全局 header
- 描述：页面左上角版本号始终显示 v0.0.3
- 影响：用户无法确认版本
- **已修复** ✅
  - 修复内容：`Directory.Build.props` 版本号从 0.0.3 更新为 0.2.1

### Bug 31: [P1-待验证] 推荐系统历史会话大量失败
- 严重级别：中（P1）— 需验证
- 模块：股票推荐
- 描述：18 个历史会话中仅 1 个完成。但这些会话大多在 v0.2.1 修复前创建（B25 超时修复 + B26 路由修复之前）。需要在当前配置下新建一轮推荐来验证是否已改善。
- PM 判断：可能是历史遗留而非当前代码 bug
- **已关闭** (非代码 Bug)
  - 原因：LLM 网关不稳定/API 权限问题导致历史会话失败，非代码 bug。降级容错机制工作正常。

### Bug 32: [P1-待验证] 市场数据面板多项显示零
- 严重级别：中（P1）— 需验证
- 模块：首页市场数据面板
- 描述：主力资金、北向资金、涨跌家数、封板温度均显示为 0。可能是非交易时段的正常行为。
- PM 判断：需在交易时段验证，非交易时段应显示最近有效数据或"收盘"标记
- **已关闭** (非代码 Bug)
  - 原因：非交易时段东方财富接口不返回盘中资金流数据，属正常行为。

### Bug 33: [P1-待验证] 情绪轮动时间戳停留在昨天
- 严重级别：中（P1）— 需验证
- 模块：情绪轮动
- 描述：市场阶段判断时间戳显示 2026/04/02，当天未更新。可能是非交易时段不刷新的预期行为。
- PM 判断：应明确展示"最后更新时间"，避免用户误以为是实时数据
- **已关闭** (非代码 Bug)
  - 原因：SSL 网络问题致今日数据断档，非代码 bug。降级为 P3，建议后续增加"数据滞后"提示标签。

### Bug 34: [P2] 股票名称含多余空格
- 严重级别：低（P2）
- 模块：股票终端
- 描述："五 粮 液""新 和 成"等名称中有额外空格
- 影响：数据源质量问题，影响专业感
- **已修复** ✅
  - 修复内容：`CompositeStockCrawler` 添加 `.Replace(" ", "").Trim()` 清洗中文名空格

### Bug 35: [P2] 基本面快照区域泄露"Step 3"标签
- 严重级别：低（P2）
- 模块：股票终端
- 描述：基本面快照区域显示"Step 3"开发标签，流通市值、股东户数等显示"-"
- 影响：暴露内部技术细节
- **已修复** ✅
  - 修复内容：移除 `StockTerminalSummary.vue` 中的 `<span class="muted">Step 3</span>`

### Bug 36: [P2] 持仓列表部分股票缺少名称和实时价格
- 严重级别：低（P2）
- 模块：交易日志
- 描述：持仓 "000001" 只显示代码不显示"平安银行"，现价和浮盈显示"-"
- 影响：交易员无法快速识别持仓
- **已修复** ✅
  - 修复内容：`PortfolioSnapshotService` 新增 `EnrichMissingNamesAsync` 方法补全持仓名称

### Bug 37: [P2] 快速录入弹窗 ESC 键无法关闭
- 严重级别：低（P2）
- 模块：交易日志
- 描述：快速录入弹窗按 ESC 无响应，必须点取消或导航离开
- 影响：操作效率
- **已修复** ✅
  - 修复内容：`TradeLogTab.vue` 添加全局 `keydown` 监听，ESC 键关闭弹窗

### Bug 38: [P2] 盈亏比全胜时显示 0.00
- 严重级别：低（P2）
- 模块：交易日志
- 描述：胜率 100%、总盈亏+1150 时盈亏比显示 0.00（应为 ∞ 或特殊文案）
- 影响：数据展示矛盾
- **已修复** ✅
  - 修复内容：后端 `TradeAccountingService` 全胜时返回 -1m，前端显示"全胜"

### Bug 39: [P3] /api/stocks/detail/cache 部分股票返回 404
- 严重级别：极低（P3）
- 模块：股票终端 API
- 描述：sh600519、sz000021 等返回 404 而非 200+空数据
- 影响：前端已静默处理，无用户可见影响
- 人类建议：不影响，不修改。

### Bug 40: [P3] 搜索框查询后显示代码而非名称
- 严重级别：极低（P3）
- 模块：股票终端
- 描述：搜索框输入后显示 "sz000001" 而非 "平安银行"
- 影响：轻微不直觉
- 人类建议：不影响，不修改。


### Bug 41: [P3] 新闻中出现与 A 股无关的外国公司
- 严重级别：极低（P3）
- 模块：股票资讯
- 描述：出现 LIXIL（日本建材公司）等与 A 股无关的新闻
- 影响：降低信噪比
- 人类建议：不影响，不修改。

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

### Bug 20-27: [v0.2.1 批量修复] Antigravity 集成 + 分析质量

- 严重级别：高-中
- 发现时间：2026-04-02
- **已修复** ✅（v0.2.1）
  - B20/B23: LLM 超时 → Per-endpoint 30s timeout 替代 180s 全局超时
  - B21: AI 分析 JSON 暴露 → StockAgentCard.vue 改为 markdownToSafeHtml 渲染
  - B22: 历史记录乱码 → 确认为一次性数据损坏（DB id=16），非代码问题
  - B24: Antigravity 联网搜索 → 已启用 Google Search Grounding（Gemini models）
  - B25: Antigravity 超时 → 3 端点 × 30s fallback
  - B26: 错误提示显示 default 而非 active → 3 处硬编码修正为 "active"
  - B27: 市场趋势只看当天 → 扩展到 30 天压缩历史

## Bug 模板

### Bug X: 标题

- 严重级别：高 / 中 / 低
- 当前无开放项。Bug 6、Bug 7、Bug 8 已于 2026-03-24 归档到 `.automation/buglist-resolved-20260323.md`。
	- 2026-03-22 本轮在 `sh600000` 的 `盘中消息带` 与右侧 `资讯影响` 中未再看到原记录里的错字/失真标题样例。
	- 当前样本下暂未复现，但仅覆盖了 `sh600000` 单一标的，建议后续在 `全量资讯库` 再抽样复核，不在本轮直接关闭。

20260405 新Bug(人工添加)
- "LLM设置"页面已经设置了本地模型分析（都设置了），但是"股票信息"页面中的AI分析还是使用的另外的Provider。
- "情绪轮动"页面右边的 "分歧" 一直显示，无法更新，“综合强度榜单”没有时效显示。"快照"一直显示4月2号 已经无法更新。
- "全量资讯库"页面中的"批量清洗待处理"按钮，点击后很快就完成了，但是实际上还有很多新闻都没清洗。
- "股票推荐" 页面发送消息给Agent后提示"流水线中止：阶段全部角色失败"。
- 读取4月5号的LLM日志，检查一下最近有什么问题，我发现最近很多MCP都失效了，要一个一个测试，确保可用。
- "股票信息"页面中的大盘指数 ，点击弹出的弹窗中的分时图，没有开盘线。日K图没有10日线 5日线条，20日线，100日线。美国指数，日本指数，韩国指数，香港指数都无法获得分时图，日K线，这是有问题的。
- 系统中所有的日K线，都是从2025年12月31号开始的，应该允许获取前面更多的历史
- 财务数据测试 不需要管理员token，可以直接查看
