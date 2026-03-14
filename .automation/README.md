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
- After both tests pass, update git (commit + push).
