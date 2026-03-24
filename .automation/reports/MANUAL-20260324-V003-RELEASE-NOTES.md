# v0.0.3 Release Notes

## Stock Copilot v0.0.3

v0.0.3 is the first desktop release that can be downloaded, installed, and used as a practical Stock Copilot build for China A-share workflows.

### Highlights

- Completed the sessionized Stock Copilot workflow with question input, plan steps, tool calls, evidence, and follow-up actions.
- Fixed the trading-plan draft modal visibility issue in the `Draft Trading Plan` flow and validated it in the live runtime page.
- Validated both Windows installer and portable desktop package artifacts, including packaged EXE startup with bundled backend and frontend.
- Enabled GitHub Releases update detection in the packaged desktop build.

### Included In This Release

- Stock terminal workspace with minute chart, day K, month K, year K, strategy overlays, and market context.
- Multi-agent analysis with structured judgment, triggers, risks, and evidence sources.
- Local news library for stock, sector, and market-level aggregation.
- Trading-plan workflow with draft, overview, reminder, and market-context linkage.
- Stock Copilot MCP runtime with bounded K-line, minute, strategy, news, and search tool interfaces.

### Assets

- SimplerJiangAiAgent-Setup-0.0.3.exe
- SimplerJiangAiAgent-portable-0.0.3.zip

### Validation Summary

- Frontend targeted tests: 70/70 passed.
- Backend targeted tests: 15/15 passed.
- `scripts/publish-windows-package.ps1`: passed.
- Packaged EXE launch validation: passed, `/api/health` returned `ok`, `/api/app/version` returned `0.0.3`.

### Full Changelog

https://github.com/simplerjiang/StockCopilot/compare/v0.0.1...v0.0.3