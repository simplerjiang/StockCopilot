@echo off
setlocal EnableExtensions

set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"
set "APP_URL=http://localhost:5119"
set "HEALTH_URL=%APP_URL%/api/health"
set "BACKEND_ROOT=%ROOT%\backend\SimplerJiangAiAgent.Api"
set "PACKAGE_ROOT=%ROOT%\artifacts\windows-package"
set "PACKAGE_SCRIPT=%ROOT%\scripts\publish-windows-package.ps1"
set "PACKAGE_EXE=%PACKAGE_ROOT%\SimplerJiangAiAgent.Desktop.exe"

pushd "%ROOT%" || exit /b 1

echo Stopping existing desktop and backend instances...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$backendRoot = [System.IO.Path]::GetFullPath('%BACKEND_ROOT%');" ^
  "$packageRoot = [System.IO.Path]::GetFullPath('%PACKAGE_ROOT%');" ^
  "$escapedBackendRoot = [Regex]::Escape($backendRoot);" ^
  "$escapedPackageRoot = [Regex]::Escape($packageRoot);" ^
  "$repoProcesses = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match $escapedBackendRoot -or $_.CommandLine -match $escapedPackageRoot) -and $_.CommandLine -match 'SimplerJiangAiAgent\.(Desktop|Api)' };" ^
  "foreach ($process in $repoProcesses) { Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue }" ^
  "if ($repoProcesses) { Start-Sleep -Seconds 2 }" ^
  "$portOwner = Get-NetTCPConnection -LocalPort 5119 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty OwningProcess;" ^
  "if (-not $portOwner) { exit 0 }" ^
  "$portProcess = Get-CimInstance Win32_Process -Filter \"ProcessId = $portOwner\" -ErrorAction SilentlyContinue;" ^
  "Write-Host ('Port 5119 is already occupied by another process: ' + $portOwner);" ^
  "if ($portProcess) { Write-Host $portProcess.Name; Write-Host $portProcess.CommandLine }" ^
  "exit 20"
if errorlevel 20 goto :port_conflict
if errorlevel 1 goto :fail

echo Packaging latest desktop build...
powershell -NoProfile -ExecutionPolicy Bypass -File "%PACKAGE_SCRIPT%"
if errorlevel 1 goto :fail

if not exist "%PACKAGE_EXE%" (
	echo Packaged desktop executable was not produced: %PACKAGE_EXE%
	goto :fail
)

echo Starting packaged desktop EXE...
start "SimplerJiangAiAgent.Desktop" "%PACKAGE_EXE%"

echo Waiting for packaged backend health check...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$healthUrl = '%HEALTH_URL%';" ^
  "$deadline = (Get-Date).AddSeconds(60);" ^
  "do { try { $response = Invoke-WebRequest -UseBasicParsing $healthUrl -TimeoutSec 3; if ($response.Content -match '\"status\"\s*:\s*\"ok\"') { exit 0 } } catch {}; Start-Sleep -Milliseconds 500 } while ((Get-Date) -lt $deadline);" ^
  "Write-Host 'Packaged desktop backend did not become healthy in time.'; exit 1"
if errorlevel 1 (
	echo Packaged desktop failed to become healthy. Check the desktop window and packaged backend logs.
	goto :fail
)

echo Packaged desktop started successfully.
goto :success

:port_conflict
echo Backend start aborted because port 5119 is occupied by another process.
goto :fail

:fail
popd
endlocal
exit /b 1

:success
popd
endlocal
exit /b 0
