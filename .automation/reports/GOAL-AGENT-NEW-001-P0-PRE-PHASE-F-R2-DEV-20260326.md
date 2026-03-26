# GOAL-AGENT-NEW-001-P0-Pre-Phase-F-R2 Dev Report / 开发报告

## EN

### Scope
- Added a small non-blocking Phase F-R2 hardening slice for live gate regression acceptance noise reduction.
- No live gate business logic was changed.

### Files changed
- `.automation/scripts/accept-live-gate.ps1`
  - New acceptance harness for `GET /api/health`, targeted live-gate tests, `POST /api/stocks/copilot/live-gate`, response summary extraction, and local LLM audit verification.
  - Uses `dotnet test --no-build --no-restore` to reuse current backend build outputs and reduce false failures caused by a running API locking `SimplerJiangAiAgent.Api.exe`.
  - Includes one retry for transient `POST /live-gate` failures.
  - Treats `done` and `done_with_gaps` as valid completion states.
  - Generates the default Chinese question at runtime so the script stays compatible with Windows PowerShell 5 source encoding.
- `.automation/tasks.json`
  - Added non-blocking hardening task `GOAL-AGENT-NEW-001-P0-Pre-Phase-F-R2`.
- `.automation/state.json`
  - Moved `currentRun.taskId` to `GOAL-AGENT-NEW-001-P0-Pre-Phase-F-R2` and updated `reportPath`.
  - `lastCompletedTask` was not rolled back.

### State update rationale
- `currentRun` was switched because this hardening slice became the active dev task in this round.
- `lastCompletedTask` was preserved because this task is additive hardening, not a regression of prior completed scope.

### Verification commands and results
1. Initial local-default run against the existing `http://localhost:5119`
   - Command: `./.automation/scripts/accept-live-gate.ps1`
   - Result: targeted tests passed, live gate returned data, but the existing local instance did not write the returned `llmTraceId` into `backend/SimplerJiangAiAgent.Api/App_Data/logs/llm-requests.txt`, so the harness correctly stayed non-zero.
2. Fresh source-matched backend with repository-local data root
   - Command: `$env:SJAI_DATA_ROOT='D:\SimplerJiangAiAgent\backend\SimplerJiangAiAgent.Api'; dotnet '.\backend\SimplerJiangAiAgent.Api\bin\Debug\net8.0\SimplerJiangAiAgent.Api.dll' --urls http://localhost:5120`
   - Result: backend healthy on `http://localhost:5120`; audit log written into `backend/SimplerJiangAiAgent.Api/App_Data/logs/llm-requests.txt`.
3. Final passing harness run
   - Command: `./.automation/scripts/accept-live-gate.ps1 -BaseUrl 'http://localhost:5120'`
   - Result: exit code `0`.
   - Summary:
     - `LlmTraceId = 34a032ba30c34a2596ce3d0f799b63c3`
     - `FinalAnswerStatus = done_with_gaps`
     - `RejectedToolCallCount = 0`
     - `Acceptance.ExecutedToolCallCount = 4`
     - Tool names: `StockKlineMcp`, `MarketContextMcp`, `StockNewsMcp`, `CompanyOverviewMcp`
     - Tool trace ids: `91a0f4a71fe0493087f4619cdcae19fb`, `e2a1145af4ec427bb83926d2a9d572f1`, `074732fcf89544bc89f2e55b5702eefe`, `5436861424c54a63a687a7b12f10d07a`
     - Audit evidence: `request`, `response` in `backend/SimplerJiangAiAgent.Api/App_Data/logs/llm-requests.txt`
     - Targeted tests: `21 passed / 0 failed`

### Notes
- The script default `BaseUrl` remains `http://localhost:5119` as requested.
- For repo-local audit verification, the runtime must write under the repository backend data root. In this session that required `SJAI_DATA_ROOT=D:\SimplerJiangAiAgent\backend\SimplerJiangAiAgent.Api`.

## 中文

### 本次范围
- 新增一个很小的、非 blocker 的 Phase F-R2 硬化切片，用于降低 live gate 回归验收的人肉噪音。
- 未改动 live gate 主业务逻辑。

### 改动文件
- `.automation/scripts/accept-live-gate.ps1`
  - 新增 live gate 自动化验收脚本：依次执行 `GET /api/health`、targeted tests、`POST /api/stocks/copilot/live-gate`、返回摘要提取、以及本地 `llm-requests.txt` 审计核对。
  - 测试命令默认使用 `dotnet test --no-build --no-restore`，复用当前 backend 构建输出，减少“运行中的 API 锁住 exe”导致的伪失败。
  - 对瞬时 `POST /live-gate` 失败增加一次轻量重试。
  - 将 `done` 与 `done_with_gaps` 都视为合法完成态，避免把“完成但存在缺口”的结果误报成失败。
  - 默认中文问题改为运行时拼接，避免 Windows PowerShell 5 因脚本源码编码问题解析失败。
- `.automation/tasks.json`
  - 新增非阻断 hardening 任务 `GOAL-AGENT-NEW-001-P0-Pre-Phase-F-R2`。
- `.automation/state.json`
  - 将 `currentRun.taskId` 切到 `GOAL-AGENT-NEW-001-P0-Pre-Phase-F-R2`，并同步 `reportPath`。
  - 没有回退 `lastCompletedTask`。

### 为什么改 state
- 这轮开发的活跃任务已经变成 Phase F-R2 硬化，所以切换 `currentRun` 是合理的。
- `lastCompletedTask` 保持不变，因为这次是追加硬化，不是回滚已完成范围。

### 验证命令与结果
1. 先对现有 `http://localhost:5119` 运行默认脚本
   - 命令：`./.automation/scripts/accept-live-gate.ps1`
   - 结果：targeted tests 通过，live gate 也返回了结果，但该现存本地实例没有把返回的 `llmTraceId` 写入 `backend/SimplerJiangAiAgent.Api/App_Data/logs/llm-requests.txt`，因此脚本按设计保持非 0。
2. 启动一个与当前源码一致、并把数据根固定到仓库 backend 目录的临时实例
   - 命令：`$env:SJAI_DATA_ROOT='D:\SimplerJiangAiAgent\backend\SimplerJiangAiAgent.Api'; dotnet '.\backend\SimplerJiangAiAgent.Api\bin\Debug\net8.0\SimplerJiangAiAgent.Api.dll' --urls http://localhost:5120`
   - 结果：`http://localhost:5120` 健康正常；LLM 审计日志落到 `backend/SimplerJiangAiAgent.Api/App_Data/logs/llm-requests.txt`。
3. 对新实例执行最终通过的 harness 验收
   - 命令：`./.automation/scripts/accept-live-gate.ps1 -BaseUrl 'http://localhost:5120'`
   - 结果：exit code `0`。
   - 摘要：
     - `LlmTraceId = 34a032ba30c34a2596ce3d0f799b63c3`
     - `FinalAnswerStatus = done_with_gaps`
     - `RejectedToolCallCount = 0`
     - `Acceptance.ExecutedToolCallCount = 4`
     - Tool names：`StockKlineMcp`、`MarketContextMcp`、`StockNewsMcp`、`CompanyOverviewMcp`
     - Tool trace ids：`91a0f4a71fe0493087f4619cdcae19fb`、`e2a1145af4ec427bb83926d2a9d572f1`、`074732fcf89544bc89f2e55b5702eefe`、`5436861424c54a63a687a7b12f10d07a`
     - 审计证据：在 `backend/SimplerJiangAiAgent.Api/App_Data/logs/llm-requests.txt` 中命中 `request`、`response`
     - targeted tests：`21 passed / 0 failed`

### 备注
- 脚本默认 `BaseUrl` 仍然保持为用户要求的 `http://localhost:5119`。
- 但如果要严格按仓库内 `backend/.../App_Data/logs/llm-requests.txt` 做审计核对，运行实例必须把数据根指向仓库 backend 目录。本次会话里通过设置 `SJAI_DATA_ROOT=D:\SimplerJiangAiAgent\backend\SimplerJiangAiAgent.Api` 实现。