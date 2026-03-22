# MANUAL-20260322-START-ALL-FIX

## English
- Scope: repair the root `start-all.bat` launcher so it can reliably start the integrated backend-served frontend again.
- Root cause:
  - The old script only inspected port `5119`.
  - A stale repo backend process was still running on port `5000`, which locked `backend/SimplerJiangAiAgent.Api/bin/Debug/net8.0/SimplerJiangAiAgent.Api.exe`.
  - The script then tried `dotnet run` again, hit the locked executable during rebuild, and timed out waiting for `http://localhost:5119/api/health`.
- Changes:
  - Rewrote `start-all.bat` to stop repo-owned backend processes by command line path instead of only checking one port.
  - Added an explicit backend build step after cleanup.
  - Switched launch from `dotnet run` to running the already-built `SimplerJiangAiAgent.Api.dll` with `ASPNETCORE_URLS=http://localhost:5119` and `Database__Provider=Sqlite`.
  - Extended the health wait loop to 60 seconds and kept the browser launch only after `/api/health` succeeds.
  - Updated `README.llm.md` so the documented quick-start behavior matches the repaired launcher.
- Validation:
    - Command: `.\start-all.bat`
    - Result: passed. The script built the frontend, stopped the stale repo backend, built the API, started a new backend instance on `http://localhost:5119`, and reached the browser launch step.
    - Command: `Invoke-WebRequest http://localhost:5119/api/health`
    - Result: HTTP 200 with `{"status":"ok"}`.
    - Command: `Invoke-WebRequest http://localhost:5119/`
    - Result: HTTP 200 and returned the frontend entry HTML.

## 中文
- 范围：修复根目录 `start-all.bat`，让它重新能稳定启动“后端提供前端静态页”的联调入口。
- 根因：
  - 旧脚本只检查 `5119` 端口。
  - 同仓库有一个残留后端实例仍跑在 `5000`，并锁住了 `backend/SimplerJiangAiAgent.Api/bin/Debug/net8.0/SimplerJiangAiAgent.Api.exe`。
  - 脚本随后再次执行 `dotnet run`，在重建阶段被锁文件卡住，最终一直等不到 `http://localhost:5119/api/health`。
- 变更：
  - 重写 `start-all.bat`，不再只看单一端口，而是按命令行路径精确停止当前仓库的后端进程。
  - 在清理旧进程后显式执行一次后端构建。
  - 启动方式从 `dotnet run` 改成直接运行已构建的 `SimplerJiangAiAgent.Api.dll`，并显式设置 `ASPNETCORE_URLS=http://localhost:5119` 与 `Database__Provider=Sqlite`。
  - 健康检查等待时间延长到 60 秒，只有 `/api/health` 成功后才打开浏览器。
  - 同步更新 `README.llm.md`，让文档里的快速启动说明和修复后的真实行为一致。
- 验证：
    - 命令：`.\start-all.bat`
    - 结果：通过。脚本成功构建前端、停止旧后端、构建 API、在 `http://localhost:5119` 启动新后端，并进入浏览器打开步骤。
    - 命令：`Invoke-WebRequest http://localhost:5119/api/health`
    - 结果：HTTP 200，返回 `{"status":"ok"}`。
    - 命令：`Invoke-WebRequest http://localhost:5119/`
    - 结果：HTTP 200，返回前端首页 HTML。