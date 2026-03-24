# SimplerJiangAiAgent - OpenCode Project Rules

## Scope
- This repository is split frontend/backend/desktop.
- Follow existing project conventions and keep changes minimal and reviewable.
- Keep single files reasonably sized; when a file starts getting too long and behavior can stay unchanged, prefer splitting code into focused components, modules, or imports instead of continuing to grow one file.

## Mandatory Workflow
1. Analyze task scope first; avoid speculative refactors.
2. If frontend/backend split work is involved, start backend and confirm health first, then frontend.
3. For tests, run in order: unit tests first, then Browser MCP checks when UI is involved.
4. If new features are proposed/accepted, update `README.md` and `.automation/tasks.json` immediately with clear task descriptions.
5. If new work breaks old features, fix both in the same task.
6. Complete only when new and existing features are both working.

## Testing & Verification
- If no backend code is changed, backend tests are optional.
- If no frontend code is changed, frontend tests are optional.
- Before any push to GitHub, verify the packaged Windows desktop chain at least once: run `scripts\publish-windows-package.ps1`, confirm `artifacts\windows-package\SimplerJiangAiAgent.Desktop.exe` exists, and record the result.
- If the change affects desktop startup, packaging, installer, runtime paths, or launch behavior, also launch the packaged desktop EXE once and verify the bundled app actually comes up.
- For UI tasks, verify:
  - UI renders correctly
  - Interaction is clickable/usable
  - Backend logs have no runtime errors
- Prefer CopilotBrowser MCP for routine UI validation on backend-served pages; use Playwright Edge only when trace/video capture or persistent-profile behavior is required.
- If Playwright Edge fallback hits a profile lock, use `.automation/edge-profile` dedicated user-data-dir.

## Security & Secrets
- Never commit API keys, tokens, passwords, or credentials.
- Use environment variables or local secret files only.

## Automation Artifacts
- Keep `.automation/tasks.json` and `.automation/state.json` aligned with progress.
- Write bilingual reports (EN + ZH) under `.automation/reports` for planning and development.
