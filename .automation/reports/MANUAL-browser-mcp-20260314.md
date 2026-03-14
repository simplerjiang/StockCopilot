# Browser MCP Experiment Report (2026-03-14)

## EN
### Scope
- Evaluated the newly available browser MCP tooling against the live local app.
- Compared practical usability for UI testing versus the existing Playwright Edge-first rule.
- Updated the active repository rules to prefer the better default path.

### Experiment
- Backend launch validation:
  - `dotnet run --project .\backend\SimplerJiangAiAgent.Api\SimplerJiangAiAgent.Api.csproj --launch-profile http`
  - `Invoke-WebRequest http://127.0.0.1:5119/api/health`
- CopilotBrowser MCP validation:
  - Navigated to the backend-served frontend.
  - Captured structured page snapshots.
  - Clicked a recent stock item (`sh600000`) and confirmed the terminal, fundamentals, charts, news impact, and chat/history endpoints all loaded.
  - Collected console errors and network requests.
- Secondary MCP validation:
  - Attempted the Darbot browser MCP tools.
  - They could not obtain a current page snapshot in this session and were not usable as a default testing tool.

### Findings
- CopilotBrowser MCP is usable as a primary UI test tool in this repo.
- It is easier than the Playwright Edge flow for routine validation because it provides:
  - structured DOM/page snapshots
  - precise element refs for targeted clicks
  - console error collection
  - network request evidence
- It was sufficient to verify a real stock-page interaction end to end.
- Darbot MCP is not ready to replace the default flow because it consistently returned `No current snapshot available` during this experiment.
- One operational issue was confirmed and turned into a rule:
  - after backend restarts, `http://localhost:<port>` is more reliable than `http://127.0.0.1:<port>` for CopilotBrowser MCP because stale 127.0.0.1 failures remain in session logs and pollute diagnosis.

### Decision
- Replaced the active default UI testing rule with:
  - primary: CopilotBrowser MCP
  - fallback: Playwright MCP with Edge only when trace/video capture, channel selection, or persistent-profile behavior is explicitly needed

### Rule Files Updated
- `.github/copilot-instructions.md`
- `AGENTS.md`
- `.automation/README.md`
- `.automation/prompts/tester.md`
- `.automation/templates/report.md`

### Validation Result
- Browser MCP experiment: passed for CopilotBrowser MCP
- Secondary Darbot browser MCP: failed to become operational in this session
- Markdown/rule file validation: no editor errors
- Final backend health check: `{"status":"ok"}`

## ZH
### 范围
- 对最新可用的浏览器 MCP 做了一轮真实本地应用实验。
- 按“实际 UI 测试可用性”对比了它和现有 Playwright Edge 优先规则。
- 根据实验结果更新了当前仓库的生效规则。

### 实验过程
- 后端启动验证：
  - `dotnet run --project .\backend\SimplerJiangAiAgent.Api\SimplerJiangAiAgent.Api.csproj --launch-profile http`
  - `Invoke-WebRequest http://127.0.0.1:5119/api/health`
- CopilotBrowser MCP 验证：
  - 访问后端托管前端页面。
  - 抓取结构化页面快照。
  - 点击最近查询股票 `sh600000`，确认终端、基本面、图表、资讯影响以及聊天/历史接口都已加载。
  - 采集前端控制台错误与网络请求证据。
- 第二套 MCP 验证：
  - 尝试 Darbot 浏览器 MCP。
  - 该套工具在本轮会话内始终拿不到当前页面快照，无法作为默认测试工具使用。

### 结论
- CopilotBrowser MCP 可以作为本仓库默认 UI 测试工具。
- 它比 Playwright Edge 更适合日常验证，因为它直接提供：
  - 结构化页面/DOM 快照
  - 可复用的元素 ref 精准点击
  - 控制台错误采集
  - 网络请求证据
- 它已经足够覆盖一次真实股票页交互链路。
- Darbot MCP 当前不适合作为默认链路，因为本轮实验中它持续返回 `No current snapshot available`。
- 本次还提炼出一条新规则：
  - 后端重启后，CopilotBrowser MCP 优先使用 `http://localhost:<port>`，不要优先用 `http://127.0.0.1:<port>`；否则浏览器会话中残留的 127 失败记录会干扰诊断。

### 决策
- 已把当前生效的 UI 测试默认规则改为：
  - 首选：CopilotBrowser MCP
  - 回退：只有在明确需要 trace/video、channel 选择或持久 profile 行为时，才使用 Playwright MCP + Edge

### 已更新规则文件
- `.github/copilot-instructions.md`
- `AGENTS.md`
- `.automation/README.md`
- `.automation/prompts/tester.md`
- `.automation/templates/report.md`

### 验证结果
- CopilotBrowser MCP 实验：通过
- Darbot 浏览器 MCP：本轮会话内未能进入可用状态
- Markdown / 规则文件校验：无编辑器错误
- 最终后端健康检查：`{"status":"ok"}`