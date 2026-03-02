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