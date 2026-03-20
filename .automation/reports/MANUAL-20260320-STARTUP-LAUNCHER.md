# MANUAL-20260320-STARTUP-LAUNCHER

## English
- Scope: replace the unstable packaged-desktop quick start with a reliable combined backend + frontend browser launcher for daily testing.
- Changes:
  - Reworked `start-all.bat` into a browser-based integration launcher.
  - The script now builds the frontend, starts the backend with `Production`, `SQLite`, and `--no-launch-profile`, waits for `/api/health`, then opens `http://localhost:5119`.
  - Added port-owner detection so an existing repo backend on `5119` is reused or restarted safely, while unrelated processes still fail fast.
  - Updated `README.md` and `README.llm.md` so the documented quick test entry matches the actual stable workflow.
  - Promoted packaged desktop verification into mandatory repo rules: before any GitHub push, run `scripts\publish-windows-package.ps1`, confirm `artifacts\windows-package\SimplerJiangAiAgent.Desktop.exe`, and if desktop/packaging flow changed, also launch the packaged EXE once.
- Validation:
  - Command: `.\start-all.bat`
  - Result: passed. Frontend build succeeded, backend started, browser launch step executed.
  - Command: `Invoke-WebRequest http://localhost:5119/api/health`
  - Result: `{"status":"ok"}`.
  - Command: `Invoke-WebRequest http://localhost:5119/`
  - Result: HTTP 200 with the frontend `index.html` content returned.

## 中文
- 范围：把不稳定的打包桌面快速启动改成一个稳定的“后端 + 前端页面”联调入口，方便日常测试直接看到页面。
- 变更：
  - 重写 `start-all.bat`，改为浏览器联调启动器。
  - 脚本现在会先构建前端，再以 `Production + SQLite + --no-launch-profile` 启动后端，等待 `/api/health` 成功后自动打开 `http://localhost:5119`。
  - 新增端口占用识别：如果 `5119` 上已经是本仓库后端，就安全复用或重启；如果是其他程序占用，则直接报错退出。
  - 已同步更新 `README.md` 与 `README.llm.md`，确保文档里的快速测试方式和真实可用入口一致。
  - 已把“推送 GitHub 前必须验证打包桌面链路”升级为仓库强制规则：每次推送前都要运行 `scripts\publish-windows-package.ps1`，确认 `artifacts\windows-package\SimplerJiangAiAgent.Desktop.exe` 已产出；若改动涉及桌面/打包/启动链路，还必须额外真实启动一次打包 EXE 并记录结果。
- 验证：
  - 命令：`.\start-all.bat`
  - 结果：通过。前端构建成功，后端成功启动，浏览器打开步骤已执行。
  - 命令：`Invoke-WebRequest http://localhost:5119/api/health`
  - 结果：返回 `{"status":"ok"}`。
  - 命令：`Invoke-WebRequest http://localhost:5119/`
  - 结果：返回 HTTP 200，且已拿到前端 `index.html` 页面内容。