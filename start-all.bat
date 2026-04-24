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

echo [Startup] Checking for orphan processes on ports 5119 and 5120...

REM Kill any process using port 5119 (API)
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":5119.*LISTENING"') do (
    echo   Killing orphan process on port 5119 (PID: %%a)
    taskkill /PID %%a /F >nul 2>&1
)

REM Kill any process using port 5120 (Worker)
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":5120.*LISTENING"') do (
    echo   Killing orphan process on port 5120 (PID: %%a)
    taskkill /PID %%a /F >nul 2>&1
)

timeout /t 2 /nobreak >nul
echo [Startup] Port cleanup complete.

echo Stopping existing desktop and backend instances...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$procs = Get-Process -Name SimplerJiangAiAgent.Desktop,SimplerJiangAiAgent.Api,SimplerJiangAiAgent.FinancialWorker -ErrorAction SilentlyContinue;" ^
  "if ($procs) { $procs | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep -Seconds 2 }" ^
  "$m = @(netstat -ano | Select-String ':5119\s.*LISTEN')[0];" ^
  "if (-not $m) { exit 0 }" ^
  "$portPid = ($m.Line.Trim() -split '\s+')[-1];" ^
  "$p = Get-Process -Id $portPid -ErrorAction SilentlyContinue;" ^
  "Write-Host ('Port 5119 is already occupied by another process: ' + $portPid);" ^
  "if ($p) { Write-Host $p.ProcessName; Write-Host $p.Path }" ^
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
  "$deadline = (Get-Date).AddSeconds(90);" ^
  "do { try { $r = Invoke-WebRequest -UseBasicParsing $healthUrl -TimeoutSec 3; $j = ConvertFrom-Json $r.Content; if ($j.status -eq 'ok') { exit 0 } } catch {}; Start-Sleep -Milliseconds 500 } while ((Get-Date) -lt $deadline);" ^
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
