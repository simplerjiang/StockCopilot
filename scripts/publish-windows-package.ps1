param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputDir = ".\artifacts\windows-package",
    [bool]$SelfContained = $true
)

$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$packageRoot = Join-Path $root $OutputDir
$backendOutput = Join-Path $packageRoot "Backend"
$financialWorkerOutput = Join-Path $packageRoot "FinancialWorker"

if (Test-Path $packageRoot) {
    Remove-Item $packageRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null

Write-Host "Building frontend (forced clean rebuild)..."
$frontendDir = Join-Path $root "frontend"
$frontendDistDir = Join-Path $frontendDir "dist"
# Force clean rebuild so Vite cannot reuse a stale dist from a prior session.
if (Test-Path $frontendDistDir) {
    Remove-Item $frontendDistDir -Recurse -Force
}
# Use cmd /c so that Vite chunk-size warnings on stderr do not trigger
# PowerShell's $ErrorActionPreference = "Stop" for NativeCommandError.
cmd /c "npm --prefix `"$frontendDir`" run build 2>&1"
if ($LASTEXITCODE -ne 0) {
    throw "Frontend build failed with exit code $LASTEXITCODE"
}
if (-not (Test-Path (Join-Path $frontendDistDir "index.html"))) {
    throw "Frontend build did not produce dist/index.html at $frontendDistDir"
}

Write-Host "Publishing backend..."
dotnet publish (Join-Path $root "backend/SimplerJiangAiAgent.Api/SimplerJiangAiAgent.Api.csproj") `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained $SelfContained `
    -o $backendOutput
if ($LASTEXITCODE -ne 0) {
    throw "Backend publish failed with exit code $LASTEXITCODE"
}

Write-Host "Syncing fresh frontend dist into packaged Backend/frontend/dist (runtime path)..."
$backendFrontendDir = Join-Path $backendOutput "frontend\dist"
if (Test-Path $backendFrontendDir) {
    Remove-Item $backendFrontendDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $backendFrontendDir | Out-Null
Copy-Item (Join-Path $frontendDistDir "*") $backendFrontendDir -Recurse -Force

Write-Host "Syncing fresh frontend dist into packaged Backend/wwwroot (overwrite stale source content)..."
# `dotnet publish` copies backend/SimplerJiangAiAgent.Api/wwwroot as static content,
# which can be stale from a previous build. Replace it with the fresh Vite output so
# any tooling that inspects wwwroot sees the same bits the runtime serves.
$backendWwwrootDir = Join-Path $backendOutput "wwwroot"
if (Test-Path $backendWwwrootDir) {
    Remove-Item $backendWwwrootDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $backendWwwrootDir | Out-Null
Copy-Item (Join-Path $frontendDistDir "*") $backendWwwrootDir -Recurse -Force

Write-Host "Seeding default LLM settings without local secrets..."
$backendAppDataDir = Join-Path $backendOutput "App_Data"
New-Item -ItemType Directory -Force -Path $backendAppDataDir | Out-Null
Copy-Item (Join-Path $root "backend/SimplerJiangAiAgent.Api/App_Data/llm-settings.json") $backendAppDataDir -Force
Remove-Item (Join-Path $backendAppDataDir "llm-settings.local.json") -Force -ErrorAction SilentlyContinue

Write-Host "Publishing financial worker..."
dotnet publish (Join-Path $root "backend/SimplerJiangAiAgent.FinancialWorker/SimplerJiangAiAgent.FinancialWorker.csproj") `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained $SelfContained `
    -o $financialWorkerOutput

Write-Host "Publishing desktop host..."
dotnet publish (Join-Path $root "desktop/SimplerJiangAiAgent.Desktop/SimplerJiangAiAgent.Desktop.csproj") `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained $SelfContained `
    -o $packageRoot

Write-Host "Package ready: $packageRoot"
Write-Host "Main executable: $(Join-Path $packageRoot 'SimplerJiangAiAgent.Desktop.exe')"

Write-Host ""
Write-Host "=== Frontend sync sanity check ==="
$wwwrootIndex = Join-Path $backendWwwrootDir "index.html"
$bundledIndex = Join-Path $backendFrontendDir "index.html"
if (Test-Path $wwwrootIndex) {
    $stamp = (Get-Item $wwwrootIndex).LastWriteTime
    Write-Host "Backend/wwwroot/index.html       LastWriteTime: $stamp"
} else {
    throw "Backend/wwwroot/index.html missing after publish"
}
if (Test-Path $bundledIndex) {
    $stamp2 = (Get-Item $bundledIndex).LastWriteTime
    Write-Host "Backend/frontend/dist/index.html LastWriteTime: $stamp2"
} else {
    throw "Backend/frontend/dist/index.html missing after publish"
}
Write-Host "Top entries under Backend/wwwroot:"
$wwwrootPrefix = $backendWwwrootDir.TrimEnd('\') + '\'
Get-ChildItem $backendWwwrootDir -Recurse -File |
    Sort-Object FullName |
    Select-Object -First 20 |
    ForEach-Object {
        $rel = $_.FullName
        if ($rel.StartsWith($wwwrootPrefix)) {
            $rel = $rel.Substring($wwwrootPrefix.Length)
        }
        "{0}  {1}" -f $_.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'), $rel
    }