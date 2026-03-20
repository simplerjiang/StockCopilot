@echo off
setlocal

set ROOT=%~dp0
set APP_URL=http://localhost:5119
set HEALTH_URL=%APP_URL%/api/health
pushd "%ROOT%"

echo Building frontend...
pushd "frontend"
call npm run build
if errorlevel 1 (
	popd
	popd
	endlocal
	exit /b 1
)
popd

echo Checking backend state on %APP_URL%...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$healthUrl = '%HEALTH_URL%';" ^
  "try { $response = Invoke-WebRequest -UseBasicParsing $healthUrl -TimeoutSec 3; if ($response.Content -match '\"status\"\s*:\s*\"ok\"') { exit 10 } } catch {}" ^
  "$portOwner = Get-NetTCPConnection -LocalPort 5119 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty OwningProcess;" ^
  "if (-not $portOwner) { exit 0 }" ^
  "$process = Get-CimInstance Win32_Process -Filter \"ProcessId = $portOwner\" -ErrorAction SilentlyContinue;" ^
  "if ($process -and (($process.Name -eq 'dotnet.exe' -and $process.CommandLine -like '*SimplerJiangAiAgent.Api*') -or $process.Name -eq 'SimplerJiangAiAgent.Api.exe')) { Stop-Process -Id $portOwner -Force -ErrorAction Stop; Start-Sleep -Seconds 2; exit 0 }" ^
  "Write-Host 'Port 5119 is already occupied by another process.'; exit 20"

set BACKEND_STATUS=%ERRORLEVEL%
if "%BACKEND_STATUS%"=="10" (
	echo Backend already healthy. Reusing existing instance.
) else if "%BACKEND_STATUS%"=="20" (
	echo Backend start aborted because port 5119 is occupied by another process.
	popd
	endlocal
	exit /b 1
) else (
	echo Starting backend in Production mode with SQLite...
	start "SimplerJiangAiAgent.Api" cmd /k "set ASPNETCORE_ENVIRONMENT=Production && set DOTNET_ENVIRONMENT=Production && dotnet run --no-launch-profile --project backend\SimplerJiangAiAgent.Api\SimplerJiangAiAgent.Api.csproj --urls %APP_URL%"
	powershell -NoProfile -ExecutionPolicy Bypass -Command ^
	  "$healthUrl = '%HEALTH_URL%';" ^
	  "$deadline = (Get-Date).AddSeconds(30);" ^
	  "do { try { $response = Invoke-WebRequest -UseBasicParsing $healthUrl -TimeoutSec 2; if ($response.Content -match '\"status\"\s*:\s*\"ok\"') { exit 0 } } catch {}; Start-Sleep -Milliseconds 500 } while ((Get-Date) -lt $deadline);" ^
	  "Write-Host 'Backend did not become healthy in time.'; exit 1"
	if errorlevel 1 (
		echo Backend failed to become healthy. Check the backend terminal window.
		popd
		endlocal
		exit /b 1
	)
)

echo Opening app in browser...
start "SimplerJiangAiAgent.Web" "%APP_URL%"

popd
endlocal
