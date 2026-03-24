# MANUAL-20260324-BUGLIST-CLOSEOUT

## EN

### Scope

- Close the remaining open items in `.automation/buglist.md`.
- Re-validate Bugs 6 and 7 against the current runtime instead of keeping stale historical notes open.
- Fix Bug 8 at the desktop-host root cause level instead of treating it as an unexplained backend crash.

### Root Cause Summary

- Bug 6 and Bug 7 had become stale open items: current runtime checks no longer reproduced the original stock-news contamination or title-distortion symptoms.
- Bug 8 was narrowed down to the desktop host, not the backend itself.
- `desktop/SimplerJiangAiAgent.Desktop/Form1.cs` previously used:
  - `2s` health probe interval,
  - `2s` health request timeout,
  - automatic recovery after `2` consecutive failures,
  - direct recovery on any navigation failure.
- That made short packaged-runtime stalls look like fatal backend loss. The host then killed and restarted the managed backend, which matched the observed transient `ERR_CONNECTION_REFUSED` windows and the logged `backend exited with code -1` symptom.

### Actions

- Updated `desktop/SimplerJiangAiAgent.Desktop/Form1.cs`.
- Hardened the desktop backend monitor by:
  - changing the probe interval from `2s` to `5s`,
  - changing the health timeout from `2s` to `5s`,
  - increasing the consecutive-failure threshold from `2` to `3`,
  - requiring sustained unhealthiness for `20s` before recovery,
  - preventing overlapping health checks with a non-reentrant guard,
  - recording last healthy / launch timestamps so brief outages do not immediately trigger restart,
  - checking backend health again before turning a WebView navigation failure into backend recovery,
  - clearing owned-process state correctly inside `StopOwnedBackendProcess()`.
- Updated `.automation/buglist.md` so the open count is now `0`.
- Archived Bug 6, Bug 7, and Bug 8 into `.automation/buglist-resolved-20260323.md` with updated resolution and retest notes.
- Synced `.automation/tasks.json` and `.automation/state.json` to this closeout round.

### Validation

- Runtime re-check for Bug 6:
  - `sh600000` stock page sample remained limited to strong stock-related announcements/news.
  - The earlier generic market/news pollution no longer reproduced.

- Runtime re-check for Bug 7:
  - stock-page and archive sampling no longer showed the previously recorded distorted Chinese titles.

- Desktop build validation:
  - Command: `dotnet build .\desktop\SimplerJiangAiAgent.Desktop\SimplerJiangAiAgent.Desktop.csproj`
  - Result: passed.

- Packaged desktop validation:
  - Command: `.\scripts\publish-windows-package.ps1`
  - Result: passed. `artifacts\windows-package\SimplerJiangAiAgent.Desktop.exe` generated successfully.

- Packaged runtime smoke:
  - The packaged EXE was launched.
  - Local health endpoint `http://localhost:5119/api/health` returned `{"status":"ok"}`.
  - Result: packaged desktop startup path remained healthy after the host-monitor changes.

### Outcome

- Buglist open count is now zero.
- Bug 6 and Bug 7 were stale and are now formally archived based on current runtime evidence.
- Bug 8 is closed with an actual root-cause fix in the desktop host rather than another layer of retry masking.

## ZH

### 范围

- 收口 `.automation/buglist.md` 中剩余开放项。
- 对 Bug 6、Bug 7 按当前 runtime 重新核验，而不是继续让历史旧结论占着开放位。
- 对 Bug 8 直接修桌面宿主根因，不再把它当成“后端莫名崩溃”。

### 根因结论

- Bug 6、Bug 7 已经变成 stale open item：当前运行态里原始症状都没有再复现。
- Bug 8 的核心问题不在后台自身，而在桌面宿主的健康检查和自动恢复策略过于激进。
- `desktop/SimplerJiangAiAgent.Desktop/Form1.cs` 之前是：
  - `2 秒`探测一次，
  - 健康请求超时也是 `2 秒`，
  - 连续失败 `2 次`就自动恢复，
  - 任意导航失败也会直接触发恢复。
- 这会把 packaged runtime 的短时抖动误判成“后端已经死亡”，然后宿主自己把后台进程杀掉并重启。这和之前看到的瞬时 `ERR_CONNECTION_REFUSED`、日志里的 `backend exited with code -1` 是一致的。

### 本轮动作

- 修改 `desktop/SimplerJiangAiAgent.Desktop/Form1.cs`。
- 桌面宿主健康监控改成：
  - 探测间隔从 `2 秒` 改为 `5 秒`；
  - 探测超时从 `2 秒` 改为 `5 秒`；
  - 连续失败阈值从 `2` 次提高到 `3` 次；
  - 必须持续失联超过 `20 秒` 才触发恢复；
  - 增加非重入保护，避免异步 tick 重叠放大失败计数；
  - 记录最近一次健康时间和启动时间，让短抖动不再立刻重启；
  - WebView 导航失败时先做一次真实健康检查，后端仍健康则不恢复；
  - `StopOwnedBackendProcess()` 正确清理 owned 状态，避免宿主自杀式 stop 被继续当作异常退出。
- 更新 `.automation/buglist.md`，开放项数量改为 `0`。
- 把 Bug 6、Bug 7、Bug 8 归档到 `.automation/buglist-resolved-20260323.md`，补齐新的修复与复测记录。
- 同步 `.automation/tasks.json` 与 `.automation/state.json`。

### 验证

- Bug 6 运行态复核：
  - `sh600000` 股票页 `盘中消息带` 继续只看到强相关公告/资讯，未再出现最初记录的泛新闻污染。

- Bug 7 运行态复核：
  - 股票页与资讯归档抽样都未再看到之前记录的中文标题失真样例。

- 桌面项目构建：
  - 命令：`dotnet build .\desktop\SimplerJiangAiAgent.Desktop\SimplerJiangAiAgent.Desktop.csproj`
  - 结果：通过。

- 打包验证：
  - 命令：`.\scripts\publish-windows-package.ps1`
  - 结果：通过，`artifacts\windows-package\SimplerJiangAiAgent.Desktop.exe` 已生成。

- 打包运行态 smoke：
  - 已拉起 packaged EXE。
  - 本地健康页 `http://localhost:5119/api/health` 返回 `{"status":"ok"}`。
  - 结果：宿主监控改动后，packaged desktop 启动链仍健康。

### 结果

- 当前 buglist 开放项已经清零。
- Bug 6、Bug 7 属于历史遗留但当前不再复现，现已正式归档。
- Bug 8 则不是继续加重试掩盖，而是已经在桌面宿主层完成根因修复并通过打包态 smoke 验证。