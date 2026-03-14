@echo off
setlocal

set ROOT=%~dp0
pushd "%ROOT%"

echo Building frontend...
pushd "frontend"
call npm run build
if errorlevel 1 (
	popd
	popd
	exit /b 1
)
popd

echo Checking backend state on http://localhost:5119...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$healthUrl = 'http://localhost:5119/api/health';" ^
  "try { $response = Invoke-WebRequest -UseBasicParsing $healthUrl -TimeoutSec 3; if ($response.Content -match '\"status\"\s*:\s*\"ok\"') { exit 10 } } catch {}" ^
  "$portOwner = Get-NetTCPConnection -LocalPort 5119 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty OwningProcess;" ^
  "if (-not $portOwner) { exit 0 }" ^
  "$process = Get-CimInstance Win32_Process -Filter \"ProcessId = $portOwner\" -ErrorAction SilentlyContinue;" ^
  "if ($process -and (($process.Name -eq 'dotnet.exe' -and $process.CommandLine -like '*SimplerJiangAiAgent.Api*') -or $process.Name -eq 'SimplerJiangAiAgent.Api.exe')) { Stop-Process -Id $portOwner -Force -ErrorAction Stop; Start-Sleep -Seconds 2; exit 0 }" ^
  "Write-Host 'Port 5119 is already occupied by another process.'; exit 20"

set BACKEND_STATUS=%ERRORLEVEL%
if "%BACKEND_STATUS%"=="10" (
	echo Backend already healthy. Skipping restart.
) else if "%BACKEND_STATUS%"=="20" (
	echo Backend start aborted because port 5119 is occupied by another process.
	popd
	endlocal
	exit /b 1
) else (
	echo Starting backend...
	start "SimplerJiangAiAgent.Api" cmd /k "dotnet run --project backend\SimplerJiangAiAgent.Api\SimplerJiangAiAgent.Api.csproj"
	timeout /t 3 >nul
)

echo Starting desktop...
start "SimplerJiangAiAgent.Desktop" cmd /k "dotnet run --project desktop\SimplerJiangAiAgent.Desktop\SimplerJiangAiAgent.Desktop.csproj"

popd
endlocal
