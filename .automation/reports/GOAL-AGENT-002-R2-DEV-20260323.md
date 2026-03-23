# GOAL-AGENT-002-R2 Development Report (2026-03-23)

## EN
### Summary
Completed the first user-facing Stock Copilot product-layer slice on the real stock page.

This round moved GOAL-AGENT-002-R2 from roadmap text into a working sidebar experience on `StockInfoTab`: the page now supports entering a stock-specific Copilot question, reviewing a planner/governor timeline draft, executing approved tools, and reading grounded evidence directly in the UI instead of falling back to a long static analysis block.

### What Changed
1. Added `frontend/src/modules/stocks/StockCopilotSessionPanel.vue` as the dedicated Copilot session surface for the stock sidebar.
2. Extended `frontend/src/modules/stocks/StockInfoTab.vue` with per-symbol Copilot workspace state:
   - question draft
   - external-search toggle
   - current turn/session tracking
   - replay-turn history
   - tool execution busy state
   - tool payload/result persistence for UI replay
3. Wired the stock page to `POST /api/stocks/copilot/turns/draft` and the existing `/api/stocks/mcp/*` endpoints so approved tools can be executed from the panel and summarized back into the current turn.
4. Added follow-up action handling so action chips can trigger approved tools or existing stock-page workflows.
5. Added targeted frontend tests for:
   - draft timeline rendering
   - approved tool execution and evidence rendering
   - replay chips/history
6. Fixed a pre-existing syntax bug in `StockInfoTab.vue` (`buildStockContext` malformed template string) that surfaced while validating the new panel.
7. Fixed a real browser UX defect where the new Copilot submit button could be covered by the sticky left terminal pane by raising the sidebar stacking context in `frontend/src/modules/stocks/CopilotPanel.vue`.

### Browser Acceptance Path
The live packaged runtime now supports this product-layer flow on the real stock page:
1. query a stock such as `sh600000`,
2. see the new `会话化协驾` panel mount in the right sidebar,
3. enter a question and submit `生成 Copilot 草案`,
4. review `本轮草案`, `计划时间线`, `工具调用卡片`, and `下一步动作`,
5. execute an approved tool such as `StockNewsMcp`,
6. see `结果摘要` plus populated `Evidence / Source` entries.

In the final Browser MCP run, the page produced:
1. visible session title `浦发银行 Copilot`,
2. visible timeline with `StockKlineMcp` and `StockNewsMcp` approved steps,
3. `StockNewsMcp` result summary `本地新闻 20 条，最近时间 2026/03/20 17:47:45。`,
4. 20 evidence entries rendered in the evidence/source panel.

### Validation
Commands:
```powershell
npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js
npm --prefix .\frontend run build
.\start-all.bat
```

Results:
1. targeted frontend tests passed: 54/54,
2. frontend production build succeeded,
3. `start-all.bat` rebuilt the package, launched `artifacts\windows-package\SimplerJiangAiAgent.Desktop.exe`, and the packaged backend health check passed.

Browser MCP validation:
1. opened `http://localhost:5119/?tab=stock-info`,
2. queried `sh600000`,
3. confirmed the new `会话化协驾` panel rendered on the real stock page,
4. submitted a Copilot question successfully,
5. confirmed `计划时间线` and `工具调用卡片` became visible,
6. executed `StockNewsMcp`,
7. confirmed tool summary and evidence/source rendering in the panel.

### Issues And Residual Risk
1. Browser console still contains pre-existing runtime noise from unrelated endpoints, including 401s on admin/source-governance routes and 500 / `ERR_CONNECTION_REFUSED` entries on older stock-plan and source/history paths. These were already present in the session and were not introduced by the new Copilot panel.
2. The current final-answer status remains `needs_tool_execution` after only one approved tool is run. That is expected for R2 because this slice focuses on panel UX and controlled tool/evidence flow, not yet on the full R3 workflow orchestration or R4 product metrics.

## ZH
### 摘要
本轮完成了第一个真正面向用户的 Stock Copilot 产品层切片，把 Copilot 从规划文档推进到了真实股票页右侧的可交互面板。

这一步把 GOAL-AGENT-002-R2 从“路线图描述”落成了 `StockInfoTab` 上可运行的会话化体验：用户现在可以直接在股票页输入问题、查看 planner/governor 草案时间线、执行已批准工具，并在同一块界面里看到 grounded evidence，而不是只读一段静态长报告。

### 本轮改动
1. 新增 `frontend/src/modules/stocks/StockCopilotSessionPanel.vue`，作为股票侧栏专用的 Copilot 会话面板。
2. 在 `frontend/src/modules/stocks/StockInfoTab.vue` 中补齐 per-symbol Copilot workspace 状态：
   - 问题草稿
   - 外部搜索开关
   - 当前 turn/session 跟踪
   - 最近回放历史
   - 工具执行 busy 状态
   - tool payload / result 的本地回放状态
3. 把股票页接到了 `POST /api/stocks/copilot/turns/draft` 与现有 `/api/stocks/mcp/*` 域内工具接口上，使用户可以在面板里直接执行已批准工具，并把结果摘要回填到当前 turn。
4. 接上了 follow-up action 逻辑，使动作 chips 可以触发已批准工具或既有股票页工作流。
5. 新增前端定向测试，覆盖：
   - 草案时间线渲染
   - 已批准工具执行与 evidence 渲染
   - 最近一轮回放 chips
6. 在联调过程中顺手修复了 `StockInfoTab.vue` 中原有的语法问题：`buildStockContext` 模板字符串缺少闭合。
7. 修复了真实浏览器里的一个 UX 问题：新的 Copilot 提交按钮会被左侧 sticky terminal 面板盖住，现已通过 `frontend/src/modules/stocks/CopilotPanel.vue` 的层级提升解决。

### 浏览器验收链路
当前打包运行态下，真实股票页已经能跑通下面这条产品层链路：
1. 查询 `sh600000` 这类股票，
2. 右侧出现新的 `会话化协驾` 面板，
3. 输入问题并点击 `生成 Copilot 草案`，
4. 查看 `本轮草案`、`计划时间线`、`工具调用卡片`、`下一步动作`，
5. 执行一张已批准工具卡，例如 `StockNewsMcp`，
6. 在同一面板内看到 `结果摘要` 和 `Evidence / Source` 列表。

最后一轮 Browser MCP 实测里，页面已经稳定给出：
1. 会话标题 `浦发银行 Copilot`，
2. 带有 `StockKlineMcp` 与 `StockNewsMcp` 的可见时间线，
3. `StockNewsMcp` 结果摘要 `本地新闻 20 条，最近时间 2026/03/20 17:47:45。`，
4. evidence/source 面板里共 20 条证据项成功落屏。

### 校验
命令：
```powershell
npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js
npm --prefix .\frontend run build
.\start-all.bat
```

结果：
1. 前端定向测试通过：54/54，
2. 前端生产构建成功，
3. `start-all.bat` 完成重新打包并成功拉起 `artifacts\windows-package\SimplerJiangAiAgent.Desktop.exe`，打包后端健康检查通过。

Browser MCP 验证：
1. 打开 `http://localhost:5119/?tab=stock-info`，
2. 查询 `sh600000`，
3. 确认真实股票页右侧出现 `会话化协驾` 面板，
4. 成功提交一轮 Copilot 问题，
5. 确认 `计划时间线` 与 `工具调用卡片` 出现，
6. 执行 `StockNewsMcp`，
7. 确认面板中出现工具结果摘要与 evidence/source 列表。

### 问题与剩余风险
1. 浏览器 console 里仍然有若干既有噪音，包括 admin/source-governance 的 401，以及 stock plans、sources/history 等旧链路的 500 / `ERR_CONNECTION_REFUSED`。这些问题在本轮开始前就已存在，不是新 Copilot 面板引入的回归。
2. 当前 final-answer 状态在只执行一张工具卡后仍是 `needs_tool_execution`。这符合 R2 预期，因为本切片聚焦的是面板 UX 与受控工具/evidence 流程，完整的动作化编排与产品指标属于后续 R3 / R4 范围。