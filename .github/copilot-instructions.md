- [x] Verify that the copilot-instructions.md file in the .github directory is created.

- [x] Clarify Project Requirements

- [x] Scaffold the Project

- [x] Customize the Project

- [x] Install Required Extensions

- [x] Compile the Project

- [x] Create and Run Task

- [ ] Launch the Project

- [x] Ensure Documentation is Complete
- Every change must be validated by running a relevant unit test or script and reporting the result.
- After each change, update or add unit tests when applicable, or run a relevant script test, and report the result.
- Work through each checklist item systematically.
- Keep communication concise and focused.
- Follow development best practices.

# Multi-Agent Automation (Local)
- Use the automation workflow in .automation/README.md.
- Always keep tasks.json and state.json in sync with progress.
- Use the prompts in .automation/prompts for plan, dev, and test.

# Bilingual Reporting (Required)
- After planning and after development, write a bilingual report (EN + ZH) in .automation/reports.
- English is for agents, Chinese is for the user.
- Record all actions, test commands, results, and any issues.

# Testing Order (Required)
- Run unit tests first, then Browser MCP checks.
- If any test fails, fix and re-run both until they pass.
- Prefer CopilotBrowser MCP for UI validation on backend-served pages; use Playwright MCP with Edge (msedge) only when trace/video capture, channel selection, or persistent-profile behavior is explicitly needed.

# Git Workflow (Required)
- After tests pass, commit and push.
- If no remote is configured, request the remote URL before pushing.
- Keep commits focused and include report updates.

# Continuous Rules (Required)
- During each chat, extract at least one actionable rule from observed issues and add it here.
- For split frontend/backend projects, start backend first and confirm it runs before frontend and Browser MCP tests.
- When new features are proposed or accepted, update README.md and .automation/tasks.json immediately with clear descriptions.
- Browser MCP tests must verify UI renders and interactions work, and check backend logs for errors; fix any issues found.
- If new work breaks existing features, fix them in the same task; completion requires all features to work.
- Prefer self-sufficient problem solving (reasoning and research). Only ask the user for decisions or required permissions.
- If Playwright Edge fallback cannot launch due to profile lock, use a dedicated user-data-dir under .automation/edge-profile.
- If required ports are already in use, stop the conflicting process or choose a free port, and record the chosen ports in the report.
- For Browser MCP UI checks, prefer backend-served frontend (build dist and visit backend URL); only use Vite dev server when a proxy for /api is configured, and set explicit backend URLs to avoid port conflicts.
- 在聊天过程中，每次都应该提炼一些规则并新增进去，基于你的思考与观察的问题。
- 分析项目组成，若前后端分离，先启动后端并确认可用，再启动前端与 Browser MCP。
- 沟通或新增新功能时，立即同步更新 README.md 与 .automation/tasks.json，且保证任务描述足够清晰，避免误导开发。
- Browser MCP 测试需验证 UI 正常显示与可交互，同时检查后端日志是否报错并修复。
- 新功能导致旧功能异常时需一并修复；只有全部功能正常时才算任务完成并回复。
- 尽量自我解决（思考与检索），仅在需要用户决策或权限时再求助。
- 涉及外部项目名且不明确时，先确认仓库/链接再做介绍，避免误导。
- 如果没有修改到后端代码，则不需要测试后端；如果没有修改到前端代码，则不需要测试前端。
- 如果 Playwright Edge 回退链路无法启动，提示用户关闭占用的浏览器实例，或改用专用 user-data-dir（.automation/edge-profile）以避免冲突。
- 涉及第三方 API Key/Token 的配置时，必须使用环境变量或本地密钥文件，禁止明文写入仓库文件。
- 涉及密钥设置时，避免把密钥写入终端历史或日志；优先让用户本地设置环境变量后再执行。
- 若由 OpenCode 执行开发，执行完成并自测后，必须由我按既有流程再复测一遍并验收后才能回复用户。
- 在复用同一终端会话执行多步命令时，先确认当前工作目录（cwd）再运行，避免因目录漂移导致相对路径命令误报失败。
- After each commit, clean up useless local modifications immediately (runtime caches, temp outputs, profile artifacts). Keep only useful artifacts, and ignore them instead of deleting when retention is needed.
- 每次提交后，必须立即清理无用的本地改动（运行缓存、临时输出、浏览器配置产物等）；如文件仍有保留价值，应通过忽略策略保留而非删除。
- For charting feature changes, add or update unit tests for each newly introduced series (e.g., candlestick/volume/MA) to verify data mapping and sort order.
- For timeframe-based K-line fetching (day/week/month/year), scale lookback windows and request counts by timeframe before aggregation to avoid sparse higher-timeframe results.
- 如果是要做一些爬虫的代码，例如要爬取数据，你需要先用命令行确认接口是否可用，数据格式是什么样的，再进行开发。
- 当需要为数据库添加新表或字段时，在本地使用Sqlcmd先验证SQL语句的正确性和预期效果，确保数据库变更不会引入错误,并确保本地数据库结构正确,如结构不正确应该修复。
- For any LLM-generated stock suggestion, enforce a structured response schema with evidence sources, confidence score, trigger/invalid conditions, and explicit risk limits before presenting it to users.
- 对任何 LLM 生成的个股建议，必须强制结构化输出：证据来源、置信度、触发/失效条件、风险上限，满足后才可展示给用户。
- For GOAL-007 optimization requests, prefer in-place upgrades to existing multi-agent prompts and displays; do not introduce new modules when existing panels can be enhanced.
- 对 GOAL-007 的优化需求，优先在现有多Agent提示词与展示层就地增强；若现有面板可扩展，则不要新增模块。
- For Browser MCP validation, do not stop at static existence checks: must click key UI actions, wait for visible responses/state changes, and inspect both backend logs and frontend console logs for runtime errors.
- For Vue component tests in this repo, assert rendered copy from the mounted wrapper (`wrapper.text()` or scoped locators) instead of `document.body.textContent`, because detached Vitest mounts can make global body assertions flaky.
- Browser MCP 验证不能只做静态存在性检查：必须点击关键交互、等待可见响应/状态变化，并检查后端日志与前端控制台日志是否有运行时错误。
- 对本仓库的 Vue 组件单测，涉及文案断言时优先读取挂载 wrapper（如 `wrapper.text()` 或局部 locator），不要依赖 `document.body.textContent`，因为 Vitest 的 detached mount 会让全局 body 断言变得不稳定。
- During testing, always verify database schema completeness with SQLCMD (tables/columns/indexes for touched features); if schema mismatches are found, fix the database structure before concluding the task.
- 测试阶段必须使用 SQLCMD 检查数据库结构完整性（涉及功能的表/字段/索引）；若发现结构不匹配，需先修复数据库结构再结束任务。
- For AI chart overlays, only render numeric support/resistance values and provide deterministic fallback precedence (commander recommendation first, then trend forecast extremes) to prevent runtime chart errors.
- 对 AI 图表叠加线，仅渲染数值型支撑/突破价，并使用确定性的回退优先级（先 commander 建议，再 trend 预测极值），避免图表运行时错误。
- For local startup stability, keep `appsettings.Development.json` SQL `ConnectionStrings:Default` pointed to a verified reachable instance (e.g., `Server=.`), and verify with SQLCMD before launch.
- 为保证本地启动稳定，`appsettings.Development.json` 的 SQL `ConnectionStrings:Default` 必须指向已验证可连接的实例（如 `Server=.`），并在启动前用 SQLCMD 验证连通性。
- For news-driven agent outputs, enforce a strict recency window with explicit source + published timestamp on every key evidence item; if timestamps are missing, downgrade the conclusion to neutral/insufficient-data.
- 对新闻驱动的 Agent 输出，必须强制时效窗口，并为每条关键证据给出来源与发布时间；若时间戳缺失，结论降级为中性/信息不足。
- For multi-agent news context assembly, default to a 72-hour trusted-source window and allow 7-day fallback only when evidence count is insufficient, while marking the expansion explicitly in output.
- 对多Agent资讯上下文组装，默认使用72小时可信来源窗口，仅在证据条数不足时扩窗到7天，并在输出中明确标注扩窗。
- For commander consistency upgrades, inject 3-7 day history context to Commander only (never to sub-agents), and require an explicit structured revision block when direction/rating changes.
- 对指挥者一致性升级，仅向 Commander 注入 3-7 天历史上下文（禁止注入子 Agent），并在方向/评级变化时强制输出结构化 revision 改判说明。
- For commander consistency guardrails, always run deterministic unit tests covering divergence tagging, hysteresis suppression on low-confidence flips, and strong-counter-evidence override before marking Problem-2 work complete.
- 对 Commander 一致性守护改动，完工前必须执行确定性单测，覆盖分歧态标注、低置信度改判滞后抑制、强反证覆盖三类场景。
- For any LLM logging enhancement, keep a single sink file with stable `traceId` request/response/error records and content truncation to prevent oversized logs.
- 对任何 LLM 日志增强，必须保持单一落地日志文件，并记录稳定 `traceId` 的请求/响应/错误链路，同时对内容做截断以防日志膨胀。
- For any prompt updates involving news analysis, explicitly require source tiering (authoritative/preferred/fallback/blocked) and enforce neutral downgrade when only blocked or untimestamped evidence is available.
- 对涉及新闻分析的提示词更新，必须显式定义来源分层（权威/优选/回退/屏蔽），且当仅有屏蔽源或无时间戳证据时，结论必须降级为中性。
- For dynamic news-source reliability, maintain a source registry with daily automated health scoring, and allow LLM-discovered sources into production only after programmatic verification and auto-quarantine safeguards.
- 针对动态资讯源稳定性，必须维护来源注册表并执行每日自动健康评分；LLM 新发现来源只有通过程序化验证并具备自动隔离保护后，才可进入生产链路。
- For Gemini/OpenAI JSON parsing (especially streaming SSE chunks), always validate `JsonElement.ValueKind` before `GetArrayLength`/`EnumerateArray`, and gracefully treat `null`/non-array nodes as empty data.
- 对 Gemini/OpenAI 的 JSON 解析（尤其流式 SSE 分片），在调用 `GetArrayLength`/`EnumerateArray` 前必须先校验 `JsonElement.ValueKind`，并将 `null` 或非数组节点按空数据兜底处理。
- Before running backend `dotnet test`, stop any active API process that locks `backend/SimplerJiangAiAgent.Api/bin/Debug/net8.0/SimplerJiangAiAgent.Api.exe` to avoid MSB3021/MSB3027 copy failures.
- 在执行后端 `dotnet test` 前，先停止占用 `backend/SimplerJiangAiAgent.Api/bin/Debug/net8.0/SimplerJiangAiAgent.Api.exe` 的 API 进程，避免 MSB3021/MSB3027 文件锁失败。
- After adding new columns to existing tables via schema initializer, always run SQLCMD column-level checks (`sys.columns`) and apply ALTER patches locally if columns are missing before declaring completion.
- 当通过 schema initializer 为已有表新增字段后，必须使用 SQLCMD 做 `sys.columns` 列级校验；若本地缺列，需先执行 ALTER 补齐再判定任务完成。
- For roadmap requests that promote a future item into current scope, create a dedicated remaining-scope task ID (e.g., `*-R1`) and sync README/tasks/state/report in the same change.
- 对于将“后续计划”前置到当前范围的需求，必须创建独立剩余范围任务ID（如 `*-R1`），并在同一提交内同步 README/tasks/state/report。
- For P0-R1 developer mode deliveries, always verify backend-served frontend with an interaction script covering admin login, developer-mode toggle, trace search, and API status checks before marking test done.
- 对 P0-R1 开发者模式交付，测试完成前必须在后端托管前端页面上执行交互脚本，至少覆盖管理员登录、开发者模式开关、trace 检索及 API 状态码检查。
- For Playwright Edge checks on dense dashboard layouts, use resilient click strategy (`force` click when pointer interception appears) and allow empty-state tolerant branches so validation remains stable across different seed datasets.
- For CopilotBrowser MCP validation in this repo, prefer `http://localhost:<port>` over `http://127.0.0.1:<port>` after server restarts, because stale 127.0.0.1 failures can remain in the browser session logs and pollute diagnosis.
- For frontend LLM audit views, never expose raw log lines as the primary list model when pairing matters; aggregate backend records by `traceId` first so request/response/error are deterministically correlated.
- For GOAL-012 stock terminal refinements, keep the symbol query/history controls in a compact sticky or inline toolbar and prioritize remaining vertical viewport space for K-line and minute charts.
- For GOAL-013 database work in this repo, if a freshly generated EF migration captures schema-initializer-managed legacy tables, trim the migration back to the feature tables, then verify the final tables/columns/indexes with SQLCMD before concluding.
- For GOAL-013 stock news pages, render local fact buckets independently from slower `/api/stocks/news/impact` analysis, and protect concurrent `/api/news` refreshes with per-symbol plus market-level locks so they do not race into 500s.
- For GOAL-013 stock terminal local-fact UX, fetch `level=market` independently of stock selection and never hard-cap visible query history with `slice(...)`; use a scrollable layout when the full list must remain accessible.
- For stock terminal browser validation after the market-news panel moved to root scope, treat `level=market` as an initial page-load dependency: checks must accept either the first `/api/news?level=market` response or already-rendered market cards, and must not require symbol search to trigger a second market-news request.
- For GOAL-013 local-fact refresh flows, never let `hasFresh*` early returns skip pending `IsAiProcessed = false` retries; pending market/stock/sector rows must still pass through AI enrichment before returning.
- For Playwright or Edge validation scripts, insert new interaction/assertion blocks before any `browser.close()` or page teardown call, otherwise late-added checks silently target a closed context.
- For `HttpRequestException` diagnostics in LLM providers, surface both the outer exception message and the inner exception message; some gateway failures only expose the actionable cause on the outer message.
- For local LLM provider settings, keep tracked `App_Data/llm-settings.json` free of plaintext secrets; store runtime API keys only in ignored local override files or environment variables.
- For stock-agent model tiering, route model choice in backend business logic from an explicit `IsPro` flag, force Pro requests onto the approved Pro model, and downgrade any non-Pro request away from Pro even if the caller passes a Pro model name.
- For GOAL-013 sector-context ingestion, do not derive A-share sector news from generic Sina roll keyword matching; use a sector-targeted Eastmoney source for `level=sector`, aggregate multiple verified global business RSS feeds for `level=market`, and keep a deterministic fallback path when an upstream source returns non-JSON or times out.
- 对于密集仪表盘布局的 Playwright Edge 校验，遇到点击被遮挡时使用稳健点击策略（必要时 `force`），并为“空数据”场景提供容错分支，保证不同种子数据下校验稳定。
- 对于本仓库的 CopilotBrowser MCP 校验，在服务重启后优先使用 `http://localhost:<port>`，不要继续复用 `http://127.0.0.1:<port>`，否则浏览器会话里残留的 127.0.0.1 失败记录会干扰诊断。
- 对于股票 Agent 的模型分档，必须在后端业务层基于显式 `IsPro` 标志统一做路由：Pro 请求强制走批准的 Pro 模型，非 Pro 请求即使传入 Pro 模型名也必须降级，禁止把模型选择完全信任给前端或调用方。
- 对于 GOAL-013 的板块上下文采集，禁止再用通用新浪滚动新闻做 A 股板块关键字硬匹配；`level=sector` 必须接定向板块源，`level=market` 必须聚合多个已验证的全球商业 RSS，并在上游返回非 JSON 或超时时保留确定性兜底路径。
- 对于 LLM Provider 的 `HttpRequestException` 诊断，必须同时保留外层异常消息和内层异常消息；有些网关失败的关键信息只存在于外层消息里。
- 对于前端 LLM 审计视图，只要存在“请求-返回配对”需求，就不能以前端逐行猜测 raw 日志；必须先由后端按 `traceId` 聚合，再展示确定性会话记录。
- 对于 GOAL-012 股票终端后续优化，标的查询/历史控制必须保持紧凑的 sticky 或内联工具条形态，并优先把剩余纵向视口空间让给 K 线与分时图。
- 对于本仓库的 GOAL-013 数据库变更，如果新生成的 EF migration 误把 schema initializer 管理的历史表也带上，必须先把 migration 收敛回本次功能表，再用 SQLCMD 校验最终表/字段/索引后才可结束任务。
- 对于 GOAL-013 股票新闻页，本地事实 buckets 必须独立于较慢的 `/api/stocks/news/impact` 分析接口渲染，且 `/api/news` 并发刷新必须采用“每个 symbol + 大盘级别”的锁，避免并发写库打出 500。
- 对于 GOAL-013 股票终端本地事实体验，`level=market` 必须独立于选股状态加载；查询历史禁止再用 `slice(...)` 这类硬截断隐藏数据，列表过长时应改为可滚动布局。
- 对于 GOAL-013 的本地事实刷新流程，禁止让 `hasFresh*` 这类早退分支跳过 `IsAiProcessed = false` 的补标重试；market/stock/sector 只要存在待处理行，都必须先补跑 AI 清洗再返回。
- 对于 Playwright / Edge 验证脚本，新增交互与断言必须放在 `browser.close()` 或页面销毁之前；否则后补的检查会落在已关闭上下文上并产生伪失败。
- For commander schema migrations, update backend history/guardrails, frontend overlay parsing, and unit tests in the same change; do not leave mixed old/new contracts active at once.
- On Windows, when `sqlcmd` targets a named-pipe SQL Server instance containing `$` in the pipe name, pass the `-S` value in single quotes so PowerShell does not treat the instance suffix as a variable and corrupt the connection target.
- For multi-channel LLM settings, always separate the provider key (for channel selection like `default` / `gemini_official`) from the provider type (for transport implementation like `openai`), and keep API keys only in ignored local overrides or environment variables.
- For fast stock switching in the terminal, load `/api/stocks/detail/cache` first and guard all detail-state writes with a per-request token so late responses from an older symbol can never overwrite the current selection.
- For stock-detail cache endpoints that feed charts, return only the latest trading session minute-line data plus the most recent K-line window; never return multi-day minute history in the cache fast path.
- For cache-first stock detail UX, once cached detail has rendered, keep symbol switching interactive and treat the live-detail request as a background refresh instead of a blocking loading state.
- For rapid stock switching in the terminal, preserve per-symbol workspace state (detail, agent runs, news buckets, chat session selection, history selection) so switching away behaves like hiding a tab rather than resetting in-flight work.
- For `StockInfoTab` UI tests, scope duplicate action labels like `刷新` to the intended card container after adding new sidebar cards (for example `.news-impact-header button`), otherwise tests can silently click the trading-plan board refresh button instead of the news-impact refresh.
- For GOAL-008 Step 4.1 high-frequency quote polling, drive the symbol set from a dedicated `ActiveWatchlist` table and gate the worker by China A-share trading sessions so off-hours loops do not waste requests or overwrite cache semantics.
- For stock chart library evaluations in this repo, reject widget wrappers or template-style chart projects as the main terminal engine when the page must consume first-party `minuteLines` / `kLines` data and support future overlays; only prototype candidates that can be isolated behind the existing `frontend/src/modules/stocks/charting/**` adapter boundary.
- 对 commander schema 迁移，必须在同一变更内同步更新后端历史/守护逻辑、前端叠加线解析与相关单测，禁止让新旧 contract 同时半生效。
- 对 GOAL-008 Step 4.1 的高频行情轮询，必须以独立 `ActiveWatchlist` 表作为标的来源，并按中国 A 股交易时段做门控，避免非交易时段空转请求或破坏缓存语义。
- 对 `StockInfoTab` 的 UI 单测，只要页面新增了带重复文案的操作按钮（如多个“刷新”），就必须把选择器收窄到目标卡片容器（例如 `.news-impact-header button`），否则测试会误点到交易计划总览等其他卡片的按钮。
- 对本仓库的股票图表库选型，只要页面需要消费第一方 `minuteLines` / `kLines` 并支持后续叠加层，就不要把 widget 封装或模板式图表项目当作主终端内核；只有能被隔离在现有 `frontend/src/modules/stocks/charting/**` 适配层后的候选，才值得继续做原型验证。
- 对于给图表提供快速首屏数据的股票详情缓存接口，只能返回最新一个交易日的分时数据和最近窗口的 K 线；禁止在 cache 快路中返回跨多日的分时历史，避免前端图表卡死。
- 对于 cache-first 的股票详情体验，只要缓存详情已经上屏，就必须保持切股交互可用；live 详情请求只能表现为后台刷新，不能继续把整个股票区锁在阻塞 loading。
- 对股票终端的快速切换，必须按 symbol 保留独立 workspace 状态（详情、多 Agent 任务、本地资讯、聊天会话选择、历史选择）；切走只能视为隐藏 tab，不能重置进行中的工作。
- 在 Windows 上，若 `sqlcmd` 连接的命名管道实例名包含 `$`，必须把 `-S` 的服务器字符串用单引号包起来，避免 PowerShell 把实例后缀当变量展开并破坏连接目标。
- 对多通道 LLM 配置，必须把 provider key（用于通道选择，如 `default` / `gemini_official`）与 provider type（用于底层实现，如 `openai`）明确分离，且 API Key 只能放在被忽略的本地覆盖文件或环境变量中。
- 对股票终端的快速切换，必须先读 `/api/stocks/detail/cache` 做缓存秒开，并用逐请求 token 保护详情状态写入，禁止旧标的的迟到响应覆盖当前选中的股票。
- For legacy local tables that persist enum values as strings, replace default EF string-enum conversion with tolerant parsing that maps known historical values and degrades unknown values to a safe status instead of crashing list APIs.
- For beta chart-engine replacements, pin the exact package version in both `package.json` and lockfile, and keep the swap isolated behind the existing `charting/**` adapter boundary so rollback remains cheap.
- For accepted chart-engine replacements, remove the superseded frontend package from the manifest/lockfile in the same change, and validate on a fresh backend-served page load before treating browser console errors from older tabs as current regressions.
- For `klinecharts` in this repo, always feed timestamps in Unix milliseconds and normalize minute-line cumulative volumes into per-bar hand counts before rendering the volume pane.
- For GOAL-012 chart legend controls, drive chip active state from a single per-view visibility model and update both runtime chart styles and registry-managed indicators/overlays together; lock the behavior with unit tests that assert active/inactive classes after clicks.
- For StockInfoTab polling or alert-surface changes, update the shared frontend fetch mock defaults for any newly added `/api/stocks/**` reads before asserting existing plan UI, otherwise older tests can fail by entering the generic error state instead of rendering the target card content.
- For GOAL-008 Step 4.3 acceptance, the trigger engine must gate execution by `ActiveWatchlist`, dedupe warning events by ongoing condition rather than raw metadata string equality, and short-poll both the current-stock card and the global plan board before calling the task done.
- 对于本地旧表里以字符串持久化的枚举值，禁止继续直接依赖 EF 默认字符串枚举转换；必须改为宽容解析，兼容已知历史值，并把未知值降级到安全状态，避免列表接口因旧数据直接崩溃。
- 对于 beta 图表引擎替换，必须同时在 `package.json` 与 lockfile 精确锁版本，并把替换限制在既有 `charting/**` 适配层边界内，保证回滚成本可控。
- 对于已确认落地的图表引擎替换，必须在同一变更里从前端依赖与 lockfile 中移除被替代的旧包，并优先在后端托管的新页面会话中做浏览器验收，不能把旧 tab 残留的控制台错误直接当作当前回归。
- 对本仓库的 `klinecharts`，时间戳必须使用 Unix 毫秒；分时接口若返回累计成交量，渲染副图前必须先转换成逐 bar 的“手”数量。
- 对 GOAL-012 图表图例开关，必须用“按视图维度的单一 visibility 状态”驱动按钮 active 状态，并同步更新运行时图表样式与 registry 管理的指标/overlay；同时补单测断言点击后的 active/inactive class，避免再次退化成纯展示文案。
- 对 StockInfoTab 这类带短轮询/告警面的页面，只要新增 `/api/stocks/**` 读取接口，就必须先同步更新前端共享 fetch mock 默认返回，再去断言既有计划卡片内容；否则老用例会先落入通用错误态，产生与真实功能无关的伪失败。
- 对 GOAL-008 Step 4.3 的验收，触发引擎必须以 `ActiveWatchlist` 作为执行边界，warning 事件去重必须基于“持续条件”而不是原始 `MetadataJson` 字符串相等，且短轮询必须同时覆盖当前股票卡与交易计划总览，满足后才可判定完成。


# Agent Collaboration & Product Manager Workflow
- **角色定位 (Persona)**: 我 (当前AI) 是系统的产品经理 (Product Manager)、架构师 (Architect) 和质量监督员 (QA/Reviewer)。我不负责直接编写大量业务代码，而是对项目功能、系统架构和最终质量负责。
- **开发人员定位**: ChatGPT-5.4 扮演“一线开发人员 (Developer)”。它必须听从我的安排并完成我布置的任务。
- **我的核心工作流 (My Responsibilities)**:
  1. **整理需求与设计步骤**: 接收用户的原始需求，审查是否符合 `README.md` 的架构愿景（例如：UI/AI解耦、Local-First 存储等）。将需求转化为架构设计和详细的步骤指令。
  2. **任务下发与文件交接**: 将设计好的详细开发任务写入至专属指令文件（如 `.automation/chatgpt_directives.md` 或 `.automation/ai_review_tracker.md`），以此指挥 ChatGPT-5.4。发现以往 GOAL 设置不合理时，须在此文件中出具重构/纠偏方案。
  3. **验收反馈 (Review)**: 当 ChatGPT-5.4 完成开发后，用户会开启一个新 Session 让我进行“提测”。我必须对照 `ai_review_tracker.md` 中的标准验收代码，并在此记录发现的问题要求其返工，直到测试通过。- For any file modifications, creations, or deletions, ALWAYS prefer using VS Code's built-in Copilot tools (like replace_string_in_file, create_file) rather than running command-line scripts (e.g., Python/PowerShell), unless explicit permission is missing or it's a bulk operation.
- ����AI���޸��ļ����½��ļ���ɾ���ļ�ʱ��������ѡʹ��VS Code Github Copilot�Դ��Ĺ��ߣ���replace_string_in_file, create_file�ȣ���������������ʹ���ն������У���Python�ű���Powershell������д�����ļ�������û��Ȩ�޻��������ع��ϡ�
- For local startup scripts on fixed ports, make reruns idempotent: if the target health endpoint is already healthy, skip restart; if the port is occupied by the same app, stop it first; if occupied by another process, fail fast with a clear message instead of letting Kestrel crash noisily.
- For repo startup scripts launched from shared terminals, prefer absolute script paths or explicitly verify cwd immediately before invocation; otherwise prior `Set-Location` drift can create false “script not found” failures.
- 对于固定端口的本地启动脚本，重复执行必须幂等：若健康检查已通过则跳过重启；若端口被同一应用占用则先停止旧进程；若被其他进程占用则直接明确失败，避免放任 Kestrel 以噪声异常崩溃。
- 对于在共享终端里执行的仓库启动脚本，优先使用绝对路径，或在调用前立刻确认 cwd；否则之前步骤造成的 `Set-Location` 漂移会制造“脚本不存在”的伪失败。
