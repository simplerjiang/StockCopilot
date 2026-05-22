# GitHub Issue Drafts - 2026-05-21

> GitHub push status: blocked locally. `gh` CLI is not installed, `GITHUB_TOKEN/GH_TOKEN` is absent, and unauthenticated GitHub API returned 403.
> Follow-up: create these as GitHub Issues when GitHub access is available. Each issue should be claimed before repair, linked from branch/commit/PR, and closed only after the listed verification is written back.

## Verification Run Summary

- Frontend stock workspace targeted tests: 285 passed.
- Frontend financial/PDF targeted tests: 201 passed.
- Frontend focused local run: 36 stock/market tests passed; 58 financial/PDF tests passed.
- Frontend production build: passed, 184 modules transformed; bundle size warning remains.
- Backend Api.Tests full run: 909 passed, 1 failed in one run (`LocalFactIngestionServiceTests.EnsureMarketFreshAsync_WhenOptionalFeedsAreSlow_ShouldNotBlockCoreMarketRefresh`).
- Backend single retry for the failed test: passed once locally, while backend test agent reported the same test failed when run alone. Treat as timing-sensitive/flaky until fixed.
- Source backend + backend-hosted frontend smoke: `/api/health` ok on `http://localhost:52424`; main nav clicks changed `?tab=` state correctly; financial embedding status returned 503 and created console/network errors.

---

## Existing Issues To Push

### ISSUE-DRAFT-001: No Vue Router / deep link support is still backlog

- Source: `.automation/buglist.md` BACKLOG #113.
- Impact: Users and agents cannot reliably share or restore deep links beyond the current query-param tab behavior.
- Repro:
  1. Open the app.
  2. Navigate between functional views.
  3. Try to share a nested state URL for a specific stock/report/detail workflow.
- Expected: Stable route-level deep links for important workflows.
- Actual: Backlog says Vue Router/deep links are not complete.
- Claim: Assign frontend owner before implementation.
- Repair: Introduce router/deep-link design in a focused branch and avoid breaking existing `?tab=` compatibility.
- Verification: Unit tests for route parsing plus browser click/reload checks for at least stock info, financial center, recommendation history, and backtest.

### ISSUE-DRAFT-002: Governance quarantine strategy remains ineffective when error count is high

- Source: `.automation/buglist.md` #110, retained as independent debt.
- Impact: Governance health can show many errors while quarantine remains zero, weakening operational trust.
- Repro:
  1. Open governance/developer diagnostics with historical error data.
  2. Compare error count with quarantine count.
- Expected: Quarantine policy should explain or act on high error counts.
- Actual: Historical note says 670 errors with Quarantine=0.
- Claim: Assign backend/governance owner.
- Repair: Define quarantine rules, thresholds, and visible status semantics.
- Verification: Unit tests for quarantine decision rules and UI/browser check for status wording.

### ISSUE-DRAFT-003: StockSearchService lacks sorting and market filtering

- Source: `.automation/sprint.md` V040-DEBT-4.
- Impact: Search quality and market-specific workflows can return noisy or poorly ordered results.
- Repro:
  1. Search with ambiguous symbols/names.
  2. Compare returned order and market coverage.
- Expected: Deterministic ranking and optional market filtering.
- Actual: Backlog records no sorting and no market filter.
- Claim: Assign stocks/backend owner.
- Repair: Add explicit ranking/filter contract and tests.
- Verification: Backend tests for ranking/filtering and frontend search smoke.

### ISSUE-DRAFT-004: FinancialDbContext uses global LiteDB BsonMapper with concurrency race risk

- Source: `.automation/tasks.json` and `.automation/sprint.md` V041-DEBT-1.
- Impact: Concurrent worker tests or runtime initialization may race on global mapper registration.
- Repro:
  1. Run FinancialWorker tests sequentially: currently passes.
  2. Run parallel/concurrent initialization scenarios.
- Expected: Mapper registration is isolated per context or otherwise concurrency-safe.
- Actual: Debt remains because `BsonMapper.Global` is shared.
- Claim: Assign financial-worker owner.
- Repair: Inject private `BsonMapper` or lock initialization deliberately.
- Verification: Add concurrency test; run `FinancialWorker.Tests`.

### ISSUE-DRAFT-005: API and Worker runtime paths are inconsistent

- Source: `.automation/sprint.md` V041-DEBT-2.
- Impact: Source, packaged desktop, and worker processes may resolve data/frontend/PDF paths differently.
- Repro:
  1. Compare `AppRuntimePaths` behavior in API and worker.
  2. Validate source and packaged runtime directories.
- Expected: API and Worker share a documented runtime path contract.
- Actual: Debt records path inconsistency.
- Claim: Assign backend/desktop owner.
- Repair: Centralize runtime path rules and document source vs packaged behavior.
- Verification: Unit tests for path resolution plus packaged smoke if runtime paths change.

### ISSUE-DRAFT-006: PdfFileDetail lacks voting candidates array

- Source: `.automation/sprint.md` V041-DEBT-3.
- Impact: Frontend cannot fully display extractor voting transparency from the detail payload.
- Repro:
  1. Request PDF file detail.
  2. Inspect whether extractor candidates are present.
- Expected: Detail payload includes voting candidates used by the UI voting panel.
- Actual: Debt records missing candidates array.
- Claim: Assign financial API owner.
- Repair: Extend DTO/storage contract and keep backward compatibility.
- Verification: API tests for detail payload and frontend voting panel tests.

### ISSUE-DRAFT-007: Intraday message feed can duplicate items and lacks after-hours labels

- Source: `.automation/sprint.md` V048-DEBT-11 and `.automation/buglist.md` runtime data watch #44.
- Impact: Users can see repeated or time-context-ambiguous intraday messages.
- Repro:
  1. Open stock intraday message feed.
  2. Inspect repeated titles/timestamps and early-morning records.
- Expected: Deduplicated feed with clear session/after-hours labeling.
- Actual: Backlog records duplicate and label issues.
- Claim: Assign stocks/news owner.
- Repair: Strengthen dedup key and session label logic.
- Verification: Parser/unit tests plus browser feed check.

### ISSUE-DRAFT-008: Recommendation history has high failed/degraded ratio

- Source: `.automation/buglist.md` #50 and `.automation/sprint.md` V048-DEBT-14.
- Impact: Recommendation workflow reliability appears low to users.
- Repro:
  1. Open recommendation history.
  2. Count recent completed/degraded/failed sessions.
- Expected: Failures are rare or clearly attributed to dependency outages with recovery options.
- Actual: Historical note says recent 20 included 15 failed, 4 degraded, 1 completed.
- Claim: Assign recommendation/backend owner.
- Repair: Separate LLM instability from app failure; add retry/recovery where appropriate.
- Verification: Backend session tests and browser recommendation history check.

### ISSUE-DRAFT-009: Trend data can contain only one day due to insufficient collection

- Source: `.automation/buglist.md` #57 runtime data watch.
- Impact: Trend-based analysis can look broken or under-informed.
- Repro:
  1. Query a symbol with sparse collection history.
  2. Inspect trend window length.
- Expected: UI/API explain insufficient collection or backfill enough data.
- Actual: Runtime watch records one-day trend data.
- Claim: Assign market-data owner.
- Repair: Add minimum-window checks and user-visible fallback.
- Verification: API tests for sparse trend data plus UI empty/limited-state check.

---

## New Issues Found During This Run

### ISSUE-DRAFT-010: LocalFactIngestionService soft-timeout test is timing-sensitive/flaky

- Found by: backend/API test agent and local rerun.
- Impact: CI/backend regression can fail; optional feed soft-timeout behavior may not release quickly under load.
- Repro:
  1. Run `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --no-restore --logger "trx;LogFileName=api-tests-20260521.trx" --verbosity quiet`.
  2. Observe the full suite result.
- Expected: `EnsureMarketFreshAsync_WhenOptionalFeedsAreSlow_ShouldNotBlockCoreMarketRefresh` finishes within its threshold.
- Actual: One full run failed with `Expected soft-timeout path to finish quickly, actual=1722.8948ms`; backend agent also saw single-run failures around 1787ms/2049ms.
- Claim: Assign backend/market-data owner.
- Repair: Make the test deterministic or adjust the soft-timeout implementation/threshold with a clear contract.
- Verification: Run the failed test repeatedly and then run full `Api.Tests`.

### ISSUE-DRAFT-011: Backend startup emits many EF `fail` schema logs even when health is ok

- Found by: backend/API test agent and source UI smoke.
- Impact: Startup logs look failed while `/api/health` is ok, which can hide real failures.
- Repro:
  1. Run source backend.
  2. Request `/api/health`.
  3. Inspect startup stdout.
- Expected: Idempotent schema initialization should not log expected existing-column cases as `fail`.
- Actual: Many `fail: Microsoft.EntityFrameworkCore.Database.Command[20102]` entries for `ALTER TABLE ... ADD COLUMN ...`.
- Claim: Assign backend/database owner.
- Repair: Check column existence before ALTER or downgrade expected duplicate-column handling.
- Verification: Source startup log has no expected `fail` schema noise; health remains ok.

### ISSUE-DRAFT-012: Source validation dynamic port binding fails with `http://localhost:0`

- Found by: local source UI smoke.
- Impact: Agents following "read actual source port" may try dynamic ports and hit a Kestrel startup failure.
- Repro:
  1. Run `dotnet run --no-launch-profile --project .\backend\SimplerJiangAiAgent.Api\SimplerJiangAiAgent.Api.csproj --urls http://localhost:0`.
- Expected: Either the command works or repo docs/scripts document the required dynamic-bind form.
- Actual: Kestrel throws `Dynamic port binding is not supported when binding to localhost. You must either bind to 127.0.0.1:0 or [::1]:0`.
- Claim: Assign automation/backend owner.
- Repair: Document source dynamic-port command or provide a script that binds `127.0.0.1:0` and validates through `localhost:<port>`.
- Verification: Source validation script starts, reads port, and health-checks.

### ISSUE-DRAFT-013: Backend-hosted financial pages produce 503 console/network errors for embedding status

- Found by: Playwright Edge source UI smoke.
- Impact: Browser validation reports console errors on normal navigation to financial pages.
- Repro:
  1. Build frontend: `npm.cmd --prefix .\frontend run build`.
  2. Start source backend on an available port.
  3. Open backend-hosted frontend and click main tabs through financial center.
- Expected: Known degraded embedding capability should be represented without browser console/network errors.
- Actual: Playwright captured two errors: `503 /api/stocks/financial/embedding/status`.
- Claim: Assign financial/frontend owner.
- Repair: Return a non-error degraded contract or suppress expected 503 from normal console flow.
- Verification: Browser navigation shows no console errors and displays degraded embedding state clearly.

### ISSUE-DRAFT-014: `App.spec.js` KeepAlive stub emits repeated Vue warnings

- Found by: frontend stock workspace agent.
- Impact: Frontend test logs contain noise that can hide real Vue warnings.
- Repro:
  1. Run `npm.cmd --prefix .\frontend run test:unit -- src/__tests__/App.spec.js`.
- Expected: Tests pass without repeated Vue warnings.
- Actual: Vue warns that extraneous `include` attributes were passed to the KeepAlive stub.
- Claim: Assign frontend test owner.
- Repair: Update the KeepAlive stub to accept/pass `include`, or avoid stubbing in a way that generates warning noise.
- Verification: App spec passes with no Vue warnings.

### ISSUE-DRAFT-015: `.automation/tasks.json` V041 frontend task states are stale

- Found by: financial/PDF frontend agent.
- Impact: Agents may re-claim already implemented work or misjudge v0.4.1 acceptance status.
- Repro:
  1. Inspect `V041-S4` to `V041-S8` in `.automation/tasks.json`.
  2. Run `npm.cmd --prefix .\frontend run test:unit -- src/modules/financial/__tests__`.
- Expected: Task statuses reflect implemented/tested components, or remaining work is split explicitly.
- Actual: Several V041 frontend stories remain `backlog` while corresponding components/tests exist and pass.
- Claim: Assign PM/automation owner.
- Repair: Reconcile tasks with sprint history and create separate remaining browser/package validation tasks if needed.
- Verification: `tasks.json` and `sprint.md` agree with actual code/test state.

### ISSUE-DRAFT-016: `FinancialReportTab` prints debug logs during normal ComparePane refresh

- Found by: financial/PDF frontend agent.
- Impact: Browser console and test output are noisier during normal user paths.
- Repro:
  1. Run a test including `frontend/src/modules/stocks/FinancialReportTab.spec.js`.
  2. Observe stdout.
- Expected: Normal refresh path should not print debug logs unless a dev flag is enabled.
- Actual: `console.debug('[FinancialReportTab] ComparePane refresh', detail)` executes in normal path.
- Claim: Assign frontend/financial owner.
- Repair: Remove debug log or guard it behind a development flag.
- Verification: FinancialReportTab tests pass without debug output.

### ISSUE-DRAFT-017: Frontend production bundle exceeds Vite chunk size warning threshold

- Found by: local and financial/PDF frontend build.
- Impact: Large initial JS can slow desktop/web startup and obscures build warning signal.
- Repro:
  1. Run `npm.cmd --prefix .\frontend run build`.
- Expected: No size warning, or a documented chunking decision.
- Actual: `assets/index-Th3CjuoF.js` is about `1,033.12 kB`, over Vite's 500 kB warning threshold.
- Claim: Assign frontend architecture owner.
- Repair: Add route/component-level dynamic imports or manual chunks if acceptable.
- Verification: Production build passes without unexpected chunk warnings, or warning is intentionally configured with rationale.

### ISSUE-DRAFT-018: Backend tests warn about Pomelo EF Core package version outside dependency constraint

- Found by: local backend tests.
- Impact: Repeated NU1608 warnings may indicate unsupported EF provider/runtime pairing.
- Repro:
  1. Run `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --no-restore`.
- Expected: Dependency graph has no NU1608 warnings.
- Actual: `Pomelo.EntityFrameworkCore.MySql 8.0.2` requires `Microsoft.EntityFrameworkCore.Relational <= 8.0.999`, but `9.0.4` is resolved.
- Claim: Assign backend dependency owner.
- Repair: Align Pomelo/EF versions or document why the warning is acceptable.
- Verification: `dotnet test` no longer prints NU1608 for Pomelo/EF, or dependency decision is captured.

