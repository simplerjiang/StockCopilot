# MANUAL-20260322-START-ALL-EXE

## English
- Scope: retarget the root `start-all.bat` launcher from a browser integration helper to the actual packaged desktop EXE flow that GitHub users download.
- Runtime issue found during validation:
  - The first packaged launch still failed because `SimplerJiangAiAgent.Desktop.exe` crashed before backend startup.
  - Windows Application/.NET Runtime logs showed `AppUpdateService` throwing `System.TypeInitializationException` caused by a `NullReferenceException` in `CurrentVersionLabel` during static initialization.
  - Fixed `desktop/SimplerJiangAiAgent.Desktop/AppUpdateService.cs` by initializing `CurrentVersion` before `UpdateClient`, removing the startup crash in packaged runs.
- Changes:
  - Reworked `start-all.bat` so it no longer starts the repo backend directly.
  - The script now stops repo-owned desktop/backend leftovers, fails fast if port `5119` is occupied by an unrelated process, runs `scripts\publish-windows-package.ps1`, validates `artifacts\windows-package\SimplerJiangAiAgent.Desktop.exe`, then launches that packaged EXE.
  - Added a post-launch wait loop against `http://localhost:5119/api/health` so the batch file verifies the packaged desktop host actually brought up its bundled backend.
  - Updated `README.llm.md` and `README.md` so the launcher documentation now matches the new packaged-EXE behavior.
- Validation:
  - Command: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish-windows-package.ps1`
  - Result: passed. Produced `artifacts\windows-package\SimplerJiangAiAgent.Desktop.exe`.
  - Command: `.\start-all.bat`
  - Result: passed. The script packaged the latest code and launched the packaged desktop EXE.
  - Command: `Invoke-WebRequest http://localhost:5119/api/health`
  - Result: HTTP 200 with `{"status":"ok"}`.
  - Command: `Get-Process SimplerJiangAiAgent.Desktop`
  - Result: desktop process found after launch.

## 中文
- 范围：把根目录 `start-all.bat` 从浏览器联调辅助脚本改成真正启动 GitHub 用户会下载到的打包桌面 EXE 的入口。
- 验证过程中发现的运行时问题：
  - 首次打包启动仍然失败，因为 `SimplerJiangAiAgent.Desktop.exe` 在拉起后端之前就先崩溃退出了。
  - Windows Application/.NET Runtime 日志显示根因是 `AppUpdateService` 的静态初始化触发 `System.TypeInitializationException`，内部是 `CurrentVersionLabel` 访问链上的空引用。
  - 已修复 `desktop/SimplerJiangAiAgent.Desktop/AppUpdateService.cs`，让 `CurrentVersion` 先于 `UpdateClient` 初始化，打包 EXE 启动崩溃问题已消失。
- 变更：
  - 重写 `start-all.bat`，不再直接启动仓库里的后端开发运行时。
  - 脚本现在会先停止当前仓库残留的桌面/后端进程；如果 `5119` 被无关程序占用则直接失败；然后执行 `scripts\publish-windows-package.ps1`，确认 `artifacts\windows-package\SimplerJiangAiAgent.Desktop.exe` 已生成，最后启动这个打包 EXE。
  - 启动后新增对 `http://localhost:5119/api/health` 的等待校验，确保 bat 不是只把 EXE 点开，而是真的确认桌面宿主已经把内置后端拉起。
  - 已同步更新 `README.llm.md` 与 `README.md`，让启动入口说明和新的 packaged-EXE 行为一致。
- 验证：
  - 命令：`powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish-windows-package.ps1`
  - 结果：通过，成功产出 `artifacts\windows-package\SimplerJiangAiAgent.Desktop.exe`。
  - 命令：`.\start-all.bat`
  - 结果：通过，脚本成功打包最新代码并启动打包版桌面 EXE。
  - 命令：`Invoke-WebRequest http://localhost:5119/api/health`
  - 结果：HTTP 200，返回 `{"status":"ok"}`。
  - 命令：`Get-Process SimplerJiangAiAgent.Desktop`
  - 结果：启动后能看到桌面进程仍在运行。