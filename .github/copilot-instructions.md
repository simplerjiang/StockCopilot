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
- Run unit tests first, then Edge MCP checks.
- If any test fails, fix and re-run both until they pass.
- Use Playwright MCP with Edge (msedge) and the existing Edge profile when possible.

# Git Workflow (Required)
- After tests pass, commit and push.
- If no remote is configured, request the remote URL before pushing.
- Keep commits focused and include report updates.

# Continuous Rules (Required)
- During each chat, extract at least one actionable rule from observed issues and add it here.
- For split frontend/backend projects, start backend first and confirm it runs before frontend and Edge MCP tests.
- When new features are proposed or accepted, update README.md and .automation/tasks.json immediately with clear descriptions.
- Edge MCP tests must verify UI renders and interactions work, and check backend logs for errors; fix any issues found.
- If new work breaks existing features, fix them in the same task; completion requires all features to work.
- Prefer self-sufficient problem solving (reasoning and research). Only ask the user for decisions or required permissions.
- If Edge MCP cannot launch due to profile lock, use a dedicated user-data-dir under .automation/edge-profile.
- If required ports are already in use, stop the conflicting process or choose a free port, and record the chosen ports in the report.
- For Edge MCP UI checks, prefer backend-served frontend (build dist and visit backend URL); only use Vite dev server when a proxy for /api is configured, and set explicit backend URLs to avoid port conflicts.
- 在聊天过程中，每次都应该提炼一些规则并新增进去，基于你的思考与观察的问题。
- 分析项目组成，若前后端分离，先启动后端并确认可用，再启动前端与 Edge MCP。
- 沟通或新增新功能时，立即同步更新 README.md 与 .automation/tasks.json，且保证任务描述足够清晰，避免误导开发。
- Edge MCP 测试需验证 UI 正常显示与可交互，同时检查后端日志是否报错并修复。
- 新功能导致旧功能异常时需一并修复；只有全部功能正常时才算任务完成并回复。
- 尽量自我解决（思考与检索），仅在需要用户决策或权限时再求助。
- 涉及外部项目名且不明确时，先确认仓库/链接再做介绍，避免误导。
- 如果没有修改到后端代码，则不需要测试后端；如果没有修改到前端代码，则不需要测试前端。
- 如果 Edge MCP 无法启动，提示用户关闭占用的浏览器实例，或改用专用 user-data-dir（.automation/edge-profile）以避免冲突。
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
- For Edge MCP validation, do not stop at static existence checks: must click key UI actions, wait for visible responses/state changes, and inspect both backend logs and frontend console logs for runtime errors.
- Edge MCP 验证不能只做静态存在性检查：必须点击关键交互、等待可见响应/状态变化，并检查后端日志与前端控制台日志是否有运行时错误。
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
- For frontend LLM audit views, never expose raw log lines as the primary list model when pairing matters; aggregate backend records by `traceId` first so request/response/error are deterministically correlated.
- For GOAL-012 stock terminal refinements, keep the symbol query/history controls in a compact sticky or inline toolbar and prioritize remaining vertical viewport space for K-line and minute charts.
- For GOAL-013 database work in this repo, if a freshly generated EF migration captures schema-initializer-managed legacy tables, trim the migration back to the feature tables, then verify the final tables/columns/indexes with SQLCMD before concluding.
- For GOAL-013 stock news pages, render local fact buckets independently from slower `/api/stocks/news/impact` analysis, and protect concurrent `/api/news` refreshes with per-symbol plus market-level locks so they do not race into 500s.
- For GOAL-013 stock terminal local-fact UX, fetch `level=market` independently of stock selection and never hard-cap visible query history with `slice(...)`; use a scrollable layout when the full list must remain accessible.
- For `HttpRequestException` diagnostics in LLM providers, surface both the outer exception message and the inner exception message; some gateway failures only expose the actionable cause on the outer message.
- For stock-agent model tiering, route model choice in backend business logic from an explicit `IsPro` flag, force Pro requests onto the approved Pro model, and downgrade any non-Pro request away from Pro even if the caller passes a Pro model name.
- For GOAL-013 sector-context ingestion, do not derive A-share sector news from generic Sina roll keyword matching; use a sector-targeted Eastmoney source for `level=sector`, aggregate multiple verified global business RSS feeds for `level=market`, and keep a deterministic fallback path when an upstream source returns non-JSON or times out.
- 对于密集仪表盘布局的 Playwright Edge 校验，遇到点击被遮挡时使用稳健点击策略（必要时 `force`），并为“空数据”场景提供容错分支，保证不同种子数据下校验稳定。
- 对于股票 Agent 的模型分档，必须在后端业务层基于显式 `IsPro` 标志统一做路由：Pro 请求强制走批准的 Pro 模型，非 Pro 请求即使传入 Pro 模型名也必须降级，禁止把模型选择完全信任给前端或调用方。
- 对于 GOAL-013 的板块上下文采集，禁止再用通用新浪滚动新闻做 A 股板块关键字硬匹配；`level=sector` 必须接定向板块源，`level=market` 必须聚合多个已验证的全球商业 RSS，并在上游返回非 JSON 或超时时保留确定性兜底路径。
- 对于 LLM Provider 的 `HttpRequestException` 诊断，必须同时保留外层异常消息和内层异常消息；有些网关失败的关键信息只存在于外层消息里。
- 对于前端 LLM 审计视图，只要存在“请求-返回配对”需求，就不能以前端逐行猜测 raw 日志；必须先由后端按 `traceId` 聚合，再展示确定性会话记录。
- 对于 GOAL-012 股票终端后续优化，标的查询/历史控制必须保持紧凑的 sticky 或内联工具条形态，并优先把剩余纵向视口空间让给 K 线与分时图。
- 对于本仓库的 GOAL-013 数据库变更，如果新生成的 EF migration 误把 schema initializer 管理的历史表也带上，必须先把 migration 收敛回本次功能表，再用 SQLCMD 校验最终表/字段/索引后才可结束任务。
- 对于 GOAL-013 股票新闻页，本地事实 buckets 必须独立于较慢的 `/api/stocks/news/impact` 分析接口渲染，且 `/api/news` 并发刷新必须采用“每个 symbol + 大盘级别”的锁，避免并发写库打出 500。
- 对于 GOAL-013 股票终端本地事实体验，`level=market` 必须独立于选股状态加载；查询历史禁止再用 `slice(...)` 这类硬截断隐藏数据，列表过长时应改为可滚动布局。

# Agent Collaboration & Product Manager Workflow
- **角色定位 (Persona)**: 我 (当前AI) 是系统的产品经理 (Product Manager)、架构师 (Architect) 和质量监督员 (QA/Reviewer)。我不负责直接编写大量业务代码，而是对项目功能、系统架构和最终质量负责。
- **开发人员定位**: ChatGPT-5.4 扮演“一线开发人员 (Developer)”。它必须听从我的安排并完成我布置的任务。
- **我的核心工作流 (My Responsibilities)**:
  1. **整理需求与设计步骤**: 接收用户的原始需求，审查是否符合 `README.md` 的架构愿景（例如：UI/AI解耦、Local-First 存储等）。将需求转化为架构设计和详细的步骤指令。
  2. **任务下发与文件交接**: 将设计好的详细开发任务写入至专属指令文件（如 `.automation/chatgpt_directives.md` 或 `.automation/ai_review_tracker.md`），以此指挥 ChatGPT-5.4。发现以往 GOAL 设置不合理时，须在此文件中出具重构/纠偏方案。
  3. **验收反馈 (Review)**: 当 ChatGPT-5.4 完成开发后，用户会开启一个新 Session 让我进行“提测”。我必须对照 `ai_review_tracker.md` 中的标准验收代码，并在此记录发现的问题要求其返工，直到测试通过。