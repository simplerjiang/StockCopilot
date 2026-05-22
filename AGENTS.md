# SimplerJiangAiAgent 仓库工程规范

本文件是仓库级工程规范。角色定义在 `.github/agents/*.agent.md`，领域专项规则按需从 `/memories/repo/` 加载。

## 仓库结构

- 仓库分为 frontend、backend、desktop 三部分。
- 遵循已有项目惯例，保持改动最小化和可审查。
- 单文件目标控制在 1000 行以内，超出时按职责拆分。

## 核心工作流

1. 先分析任务范围，禁止投机式重构。
2. 前后端同时涉及时，先完成后端并确认健康，再做前端。
3. 验证顺序：先单元测试，再浏览器验收（如涉及 UI）。
4. 新功能被接受后，立即同步更新 `README.md` 和 `.automation/tasks.json`。
5. 新改动破坏旧行为时，必须同一任务内修复。
6. **前后端同步**：浏览器验收前必须确认前端已构建且由后端托管。不要在 Vite dev server 和后端托管之间混用。

## GitHub Issue 问题流程

- 任何工作中发现的新问题、缺陷、回归或待确认风险，必须先记录到 GitHub Issue，再进入修复；如暂时无法访问 GitHub，先在本地记录完整 Issue 草稿并在恢复后补建。
- **推送问题**：创建 Issue 时写清问题标题、现象、影响范围、复现步骤、期望行为、实际行为、日志/截图/错误信息、发现时间和相关分支/提交；不得只写笼统描述。
- **领取问题**：开始处理前在 Issue 中留言领取，确认负责人、处理范围、计划验证方式，并按仓库约定添加 assignee、label、milestone 或项目状态。
- **修复问题**：修复分支、commit、PR 和自动化任务记录必须引用 Issue 编号；实现时保持 focused change，不夹带无关重构。
- **走测问题**：修复后按“测试与验证”和“浏览器验证”要求完成走测，在 Issue/PR 中写明执行的命令、浏览器验收步骤、结果、遗留风险和证据；确认通过后再关闭 Issue。
- **Issue 推送失败处理**：若 `gh`、GitHub token 或网络不可用，必须把 Issue 草稿写入 `.automation/github-issue-drafts-*.md`，恢复访问后优先运行 `.automation/scripts/push-github-issue-drafts.ps1` 补建远端 Issue。
- **标准领取-修复-推送-检查链路**：
  1. 读取 GitHub Issue，确认未被领取；留言“领取”，写明负责人、修复范围、计划验证命令。
  2. 从最新主分支创建修复分支，分支名包含 Issue 编号，例如 `fix/123-short-title`。
  3. 修复时保持 focused change，commit message 和 PR 标题引用 `#123`。
  4. 按影响面运行单元测试、前端构建、浏览器验收；涉及推送前必须按仓库规则验证打包链。
  5. 推送分支并创建 PR；PR/Issue 评论中写明测试命令、浏览器步骤、结果和残余风险。
  6. 合并或确认修复后关闭 Issue；未完成或验证失败不得关闭。

## 自动化与报告

- 遵循 `.automation/README.md`，使用 `.automation/prompts` 中的流程模板。
- 保持 `.automation/tasks.json` 和 `.automation/state.json` 与实际进度一致。
- L 级任务完成后，在 `.automation/reports` 写双语报告（英文给 Agent，中文给用户）。

## 测试与验证

- 每次改动必须有相关测试或验证脚本覆盖。无直接测试时运行最近的验证脚本。
- 未改后端代码则后端测试可选；未改前端代码则前端测试可选。
- Windows 启动验证时，选定一种运行模式（source 或 packaged desktop），不要中途切换。
  - **source 模式**：直接运行源码后端，从启动日志读取端口，不假设 5119，不用 `start-all.bat`。动态端口必须绑定 `http://127.0.0.1:0`，不要用 `http://localhost:0`；推荐用 `.automation/scripts/start-source-backend.ps1` 启动并读取实际端口，浏览器验收仍使用脚本输出的 `http://localhost:<port>`。
  - **packaged desktop 模式**：用 `start-all.bat`，健康检查用 `http://localhost:5119/api/health`。
- 推送到 GitHub 前，至少验证一次打包链：运行 `scripts\publish-windows-package.ps1`，确认 EXE 存在。
- 测试通过后做 focused commit 并推送。推送后清理临时文件。

## 浏览器验证

- 优先使用 DarBot Browser MCP，回退到 Playwright Edge，最后用 VS Code 内置浏览器。
- 优先用 `http://localhost:<port>` 而非 `127.0.0.1`，优先后端托管前端而非 Vite。
- 验证必须包含实际点击和状态变化检查，不能只检查元素存在。同时检查控制台和网络错误。

## 终端安全

- 运行路径相关命令前先确认 `cwd`。前端 npm 命令优先用 `npm --prefix .\frontend ...`。
- 切换运行模式时先停旧进程，再启新模式，重新读取端口。
- 端口已占用时，先停冲突进程或选择空闲端口并记录。
- 打包文件被锁时，先停止 `artifacts\windows-package` 下的进程再重试。

## 安全

- 禁止提交 API 密钥、令牌、密码等凭证。
- 机密只存放在环境变量或被忽略的本地文件中。
- 禁止在终端历史、日志或报告中写入机密。

## 数据库

- 新增或修改表/列前，用 `sqlcmd` 本地验证 SQL 并确认 schema 正确。
- 运行 `dotnet test` 前停止占用 API exe 的进程。
- Windows `sqlcmd` 目标含 `$` 时，用单引号包裹 `-S` 值。
