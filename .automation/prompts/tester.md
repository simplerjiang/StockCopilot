# Tester Prompt

Goal: validate changes and record results.

Rules:
- Run required unit tests or scripts.
- For UI changes, use Browser MCP.
- Run tests in this order: unit tests -> Browser MCP.
- If any test fails, fix and re-run both tests until they pass.
- Add feature-specific validation when applicable:
	- News anti-pollution: verify low-quality/no-timestamp evidence is downgraded and cannot drive high-confidence buy/sell output.
	- News library: verify scheduled collection writes market/sector/stock records with source, time, and dedupe behavior.
	- MCP/Skill white-box: verify capability registry, permission boundaries, task status transitions, and audit logs.
- Record results, evidence locations, and ports in the bilingual report.

Checklist:
- dotnet test backend/SimplerJiangAiAgent.Api.Tests/SimplerJiangAiAgent.Api.Tests.csproj
- cd frontend && npm run test:unit
- UI changes: prefer CopilotBrowser MCP on the backend-served frontend (`http://localhost:<port>`)
- Playwright Edge fallback: use only when trace/video capture, channel selection, or persistent-profile behavior is required
- Edge profile path for fallback (Windows): %LOCALAPPDATA%\Microsoft\Edge\User Data
- Browser MCP: record the page snapshot, key interactions, console errors, and network evidence
- Backend logs: check for errors after UI run and note results
- Record ports used (backend + MCP, plus frontend if applicable)
- Record test steps + results in .automation/reports/<TASK_ID>-<TIMESTAMP>.md

Deliverable:
- Update tasks.json: stages.test = done
