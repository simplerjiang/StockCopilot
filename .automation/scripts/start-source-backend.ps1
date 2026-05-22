param(
    [int]$TimeoutSeconds = 120,
    [switch]$StopAfterHealth
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..")
$runDir = Join-Path $repoRoot ".artifacts\run-logs"
New-Item -ItemType Directory -Force -Path $runDir | Out-Null

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$outLog = Join-Path $runDir "source-backend-$stamp.out.log"
$errLog = Join-Path $runDir "source-backend-$stamp.err.log"

$projectPath = Join-Path $repoRoot "backend\SimplerJiangAiAgent.Api\SimplerJiangAiAgent.Api.csproj"

# Kestrel does not support dynamic ports with localhost. Bind loopback IP and
# expose the equivalent localhost URL for browser validation.
$process = Start-Process `
    -FilePath "dotnet" `
    -ArgumentList @(
        "run",
        "--no-launch-profile",
        "--project",
        $projectPath,
        "--urls",
        "http://127.0.0.1:0"
    ) `
    -WorkingDirectory $repoRoot `
    -PassThru `
    -WindowStyle Hidden `
    -RedirectStandardOutput $outLog `
    -RedirectStandardError $errLog

$baseUrl = $null

try {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $outLog) {
            $text = Get-Content -Raw -LiteralPath $outLog -ErrorAction SilentlyContinue
            if ($text -match "Now listening on:\s*http://127\.0\.0\.1:(\d+)") {
                $baseUrl = "http://localhost:$($Matches[1])"
                break
            }
        }

        if ($process.HasExited) {
            throw "Source backend exited before listening. ExitCode=$($process.ExitCode). stdout=$outLog stderr=$errLog"
        }

        Start-Sleep -Milliseconds 500
    }

    if (-not $baseUrl) {
        throw "Timed out waiting for source backend listening URL. stdout=$outLog stderr=$errLog"
    }

    $health = Invoke-RestMethod -Uri "$baseUrl/api/health" -TimeoutSec 10
    if ($health.status -ne "ok") {
        throw "Health check returned unexpected payload: $($health | ConvertTo-Json -Compress)"
    }

    [pscustomobject]@{
        BaseUrl = $baseUrl
        Health = "ok"
        ProcessId = $process.Id
        StdoutLog = $outLog
        StderrLog = $errLog
    }
}
finally {
    if ($StopAfterHealth -and $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }
}
