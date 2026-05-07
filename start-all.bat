@echo off
setlocal EnableExtensions

set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"
set "PACKAGE_SCRIPT=%ROOT%\scripts\publish-windows-package.ps1"
set "PACKAGE_ROOT=%ROOT%\artifacts\windows-package"
set "PACKAGE_EXE=%PACKAGE_ROOT%\SimplerJiangAiAgent.Desktop.exe"

pushd "%ROOT%" || exit /b 1

echo [1/3] Stopping existing instances...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Get-Process -Name SimplerJiangAiAgent.Desktop,SimplerJiangAiAgent.Api,SimplerJiangAiAgent.FinancialWorker -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep -Seconds 2"

echo [2/3] Packaging latest build...
powershell -NoProfile -ExecutionPolicy Bypass -File "%PACKAGE_SCRIPT%"
if errorlevel 1 (
    echo ERROR: Packaging failed. Check build output above.
    goto :fail
)

if not exist "%PACKAGE_EXE%" (
    echo ERROR: Desktop executable not found: %PACKAGE_EXE%
    goto :fail
)

echo [3/3] Starting SimplerJiangAiAgent Desktop...
start "" "%PACKAGE_EXE%"
echo Done. Desktop application launched. Backend will be started automatically.

popd
endlocal
exit /b 0

:fail
popd
endlocal
exit /b 1
