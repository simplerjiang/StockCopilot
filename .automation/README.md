# Multi-Agent Automation (Local)

This folder provides a lightweight, local workflow to run a plan -> develop -> test loop with
Git checkpoints, saved logs, and a rollback path. The scripts do not call AI directly; they
prepare the workspace and record state so Copilot can follow the prompts reliably.

## Layout
- tasks.json: Task queue and status
- state.json: Current run metadata (checkpoint tag, branch, log file, report)
- logs/: Run logs (ignored by git)
- reports/: Bilingual work reports (committed)
- prompts/: Role prompts for planner, developer, tester
- scripts/: PowerShell scripts for run, finalize, rollback
- templates/: Report template

## Typical Flow
1) Start a run:
   - .\ .automation\scripts\run.ps1
2) Follow prompts:
   - .automation\prompts\planner.md
   - .automation\prompts\developer.md
   - .automation\prompts\tester.md
3) Update the bilingual report after planning and development:
   - .automation\reports/<TASK_ID>-<TIMESTAMP>.md
4) Finalize (runs tests by default, commits, and pushes):
   - .\ .automation\scripts\finalize.ps1 -TaskId AUTO-001 -Message "auto: complete task"
4) Rollback if needed:
   - .\ .automation\scripts\rollback.ps1 -Force

## Windows Runtime Quick Path
- Choose one Windows runtime mode before launch and keep it fixed for the whole validation round.
- Source validation: launch the current source/backend-served app directly, read the active port from the source startup log, validate against `http://localhost:<port>`, do not assume `5119`, and do not use `start-all.bat`. For dynamic ports, bind Kestrel to `http://127.0.0.1:0` instead of `http://localhost:0`; the recommended helper is `.automation/scripts/start-source-backend.ps1`, which prints the browser-safe `http://localhost:<port>` URL after `/api/health` succeeds.
- Packaged desktop validation: run `.\start-all.bat`. It stops repo-owned processes, runs `scripts\publish-windows-package.ps1`, launches `artifacts\windows-package\SimplerJiangAiAgent.Desktop.exe`, and waits for `http://localhost:5119/api/health`.
- If you switch modes, stop old repo-owned processes first and re-read the active port from the new startup log before continuing.
- If packaging fails because files are locked, stop any process under `artifacts\windows-package` before rerunning `scripts\publish-windows-package.ps1`.

## Browser MCP
Use CopilotBrowser MCP as the default UI validation tool against the backend-served frontend.
It provides structured page snapshots, targeted clicks, console logs, and network request evidence with less setup than the Playwright Edge flow.
Use Playwright MCP with Edge only when you explicitly need trace/video capture, channel selection, or persistent-profile behavior.
See prompts/tester.md for the exact checklist.
Typical Edge profile path for the fallback flow (Windows):
- %LOCALAPPDATA%\Microsoft\Edge\User Data

## Mandatory Rules (Bilingual Logging & Tests)
- After planning and after development, record all actions in a bilingual report
   (English for agents, Chinese for you).
- After planning and after development, run unit tests and Browser MCP checks in order.
- If any test fails, fix and re-run both tests until they pass.
- Before any GitHub push, run one packaged desktop verification round: execute `scripts\publish-windows-package.ps1`, confirm `artifacts\windows-package\SimplerJiangAiAgent.Desktop.exe` was produced, and write the command plus result into the report.
- If the current change touches desktop startup, packaging, installer, runtime paths, or launch flow, the packaged verification round must also include one real launch of the packaged desktop EXE and the observed result.
- After both tests pass, update git (commit + push).
