# MANUAL-20260322 V0.0.2 RELEASE DEV

## EN

### Actions

- Updated the authoritative application version metadata from `0.0.1` to `0.0.2` in `Directory.Build.props`.
- Updated the frontend package version to `0.0.2` so the desktop-served frontend and release metadata stay aligned.
- Switched desktop auto-update and backend `/api/app/version` release URLs from the moved `AiAgent` repository path to the canonical `StockCopilot` repository path.
- Updated the installer script defaults and installer build fallback version to `0.0.2`.
- Corrected the public README download section so the documented asset names match the actual generated release filenames.
- Installed Inno Setup locally and generated all release assets required for a user-downloadable GitHub release.

### Validation Commands And Results

- Command: `winget install --id JRSoftware.InnoSetup --accept-package-agreements --accept-source-agreements --silent --disable-interactivity`
- Result: passed; Inno Setup 6.7.1 installed successfully.

- Command: `& .\scripts\publish-windows-package.ps1 -Configuration Release -RuntimeIdentifier win-x64 -SelfContained:$true`
- Result: passed; rebuilt `artifacts\windows-package` for `0.0.2`.

- Command: `& .\scripts\build-windows-installer.ps1 -SkipPackagePublish -AppVersion 0.0.2 -SelfContained:$true`
- Result: passed; generated `artifacts\installer\SimplerJiangAiAgent-Setup-0.0.2.exe`.

- Command: `Compress-Archive -Path .\artifacts\windows-package\* -DestinationPath .\artifacts\SimplerJiangAiAgent-portable-0.0.2.zip -Force`
- Result: passed; generated the portable zip for `0.0.2`.

- Command: launch packaged desktop EXE and query `http://localhost:5119/api/health` plus `http://localhost:5119/api/app/version`
- Result: passed; health returned `{"status":"ok"}` and app version returned `{"version":"0.0.2","repositoryUrl":"https://github.com/simplerjiang/StockCopilot","releaseUrl":"https://github.com/simplerjiang/StockCopilot/releases/latest"}`.

### Issues

- The machine initially did not have `ISCC.exe`; local installer generation was blocked until Inno Setup was installed.
- The workspace contained unrelated user changes, so the release commit must be scoped only to the release-version files and this report.

## ZH

### 本轮操作

- 将权威版本源从 `0.0.1` 更新为 `0.0.2`，确保桌面程序、后端和安装器统一使用 `v0.0.2`。
- 将前端包版本同步更新为 `0.0.2`，避免桌面托管前端与程序版本脱节。
- 将桌面自动更新和后端 `/api/app/version` 中的仓库地址，从已迁移的 `AiAgent` 路径切换到正式仓库 `StockCopilot`。
- 将安装器脚本默认版本和安装器构建脚本兜底版本都改为 `0.0.2`。
- 修正 `README.md` 下载区块中的文件名与发布页地址，使文档与实际 release 产物一致。
- 本机安装了 Inno Setup，并实际生成了给用户下载的完整 Release 产物。

### 验证命令与结果

- 命令：`winget install --id JRSoftware.InnoSetup --accept-package-agreements --accept-source-agreements --silent --disable-interactivity`
- 结果：通过；已成功安装 Inno Setup 6.7.1。

- 命令：`& .\scripts\publish-windows-package.ps1 -Configuration Release -RuntimeIdentifier win-x64 -SelfContained:$true`
- 结果：通过；成功重建 `artifacts\windows-package` 的 `0.0.2` 打包目录。

- 命令：`& .\scripts\build-windows-installer.ps1 -SkipPackagePublish -AppVersion 0.0.2 -SelfContained:$true`
- 结果：通过；成功生成 `artifacts\installer\SimplerJiangAiAgent-Setup-0.0.2.exe`。

- 命令：`Compress-Archive -Path .\artifacts\windows-package\* -DestinationPath .\artifacts\SimplerJiangAiAgent-portable-0.0.2.zip -Force`
- 结果：通过；成功生成 `0.0.2` 便携包。

- 命令：启动打包版桌面 EXE 后访问 `http://localhost:5119/api/health` 与 `http://localhost:5119/api/app/version`
- 结果：通过；健康检查返回 `{"status":"ok"}`，版本接口返回 `{"version":"0.0.2","repositoryUrl":"https://github.com/simplerjiang/StockCopilot","releaseUrl":"https://github.com/simplerjiang/StockCopilot/releases/latest"}`。

### 问题说明

- 机器最初缺少 `ISCC.exe`，导致本地无法直接生成安装器；安装 Inno Setup 后已恢复完整发版能力。
- 当前工作区还存在其他与本次发版无关的开发中改动，因此提交时必须严格限制范围，只提交本次发版相关文件和本报告。