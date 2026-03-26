param(
    [string]$BaseUrl = 'http://localhost:5119',
    [string]$Symbol = 'sh600000',
    [string]$Question = '',
    [string]$TaskId = 'GOAL-AGENT-NEW-001-P0-Pre-Phase-F-R2',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

$defaultQuestion = -join ([char[]](
    0x770B, 0x4E0B, 0x6D66, 0x53D1, 0x94F6, 0x884C, 0x65E5, 0x7EBF, 0x7ED3,
    0x6784, 0x548C, 0x672C, 0x5730, 0x65B0, 0x95FB, 0x8BC1, 0x636E
))

if ([string]::IsNullOrWhiteSpace($Question)) {
    $Question = $defaultQuestion
}

function Get-ArrayOrEmpty {
    param([object]$Value)

    if ($null -eq $Value) {
        return @()
    }

    if ($Value -is [string]) {
        return @($Value)
    }

    if ($Value -is [System.Array] -or $Value -is [System.Collections.IList]) {
        return @($Value)
    }

    return ,$Value
}

function Write-Section {
    param([string]$Title)

    Write-Host ""
    Write-Host "=== $Title ==="
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$testProjectPath = Join-Path $repoRoot 'backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj'
$logPath = Join-Path $repoRoot 'backend\SimplerJiangAiAgent.Api\App_Data\logs\llm-requests.txt'
$normalizedBaseUrl = $BaseUrl.TrimEnd('/')
$effectiveTaskId = if ([string]::IsNullOrWhiteSpace($TaskId)) { 'GOAL-AGENT-NEW-001-P0-Pre-Phase-F-R2' } else { $TaskId }
$testFilter = 'FullyQualifiedName~StockCopilotLiveGateServiceTests|FullyQualifiedName~StockCopilotAcceptanceServiceTests|FullyQualifiedName~StockAgentRoleContractRegistryTests|FullyQualifiedName~StockMcpGatewayPhaseATests|FullyQualifiedName~StockCopilotSessionServiceTests'
$testCommandText = "dotnet test $testProjectPath --no-build --no-restore --filter `"$testFilter`""

$healthSuccess = $false
$healthStatusCode = $null
$healthBody = $null
$testsSuccess = $SkipTests.IsPresent
$testExitCode = $null
$liveGateSuccess = $false
$liveGateStatusCode = $null
$liveGateErrorBody = $null
$llmTraceId = $null
$finalAnswerStatus = $null
$rejectedToolCallCount = 0
$executedToolCallCount = 0
$toolTraceIds = @()
$toolNames = @()
$liveGateAttemptCount = 0
$auditSuccess = $false
$auditStages = @()
$auditMatchCount = 0
$acceptedFinalStatuses = @('done', 'done_with_gaps')
$failures = New-Object System.Collections.Generic.List[string]

Write-Host "Live gate acceptance harness"
Write-Host "BaseUrl: $normalizedBaseUrl"
Write-Host "Symbol: $Symbol"
Write-Host "TaskId: $effectiveTaskId"
Write-Host "SkipTests: $($SkipTests.IsPresent)"

Write-Section 'Health'
try {
    $healthResponse = Invoke-WebRequest -Uri "$normalizedBaseUrl/api/health" -Method Get -UseBasicParsing
    $healthStatusCode = [int]$healthResponse.StatusCode
    $healthBody = $healthResponse.Content
    $healthSuccess = $healthStatusCode -eq 200
    Write-Host "GET /api/health -> $healthStatusCode"
    if (-not [string]::IsNullOrWhiteSpace($healthBody)) {
        Write-Host $healthBody
    }
}
catch {
    $healthStatusCode = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode.value__ } else { $null }
    $healthBody = $_.Exception.Message
    Write-Host "GET /api/health failed: $healthBody"
}

if (-not $healthSuccess) {
    $failures.Add('health check failed')
}

Write-Section 'Targeted Tests'
if ($SkipTests.IsPresent) {
    Write-Host 'SkipTests set: targeted live gate tests were not executed.'
}
else {
    Write-Host 'Running targeted live gate tests with --no-build/--no-restore to reuse the current backend build output and avoid false failures caused by a running API locking SimplerJiangAiAgent.Api.exe.'
    Write-Host $testCommandText
    Push-Location $repoRoot
    try {
        & dotnet test $testProjectPath --no-build --no-restore --filter $testFilter
        $testExitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    $testsSuccess = $testExitCode -eq 0
    Write-Host "Targeted tests exit code: $testExitCode"

    if (-not $testsSuccess) {
        $failures.Add('targeted tests failed')
    }
}

Write-Section 'Live Gate'
$requestBody = [ordered]@{
    symbol = $Symbol
    question = $Question
    sessionKey = $null
    sessionTitle = 'live-gate-acceptance'
    taskId = $effectiveTaskId
    allowExternalSearch = $false
    provider = 'active'
    model = $null
    temperature = 0.1
}

try {
    $liveGateBodyJson = $requestBody | ConvertTo-Json -Depth 6

    for ($attempt = 1; $attempt -le 2; $attempt++) {
        $liveGateAttemptCount = $attempt

        try {
            $liveGateResponse = Invoke-WebRequest -Uri "$normalizedBaseUrl/api/stocks/copilot/live-gate" -Method Post -ContentType 'application/json; charset=utf-8' -Body $liveGateBodyJson -UseBasicParsing
            $liveGateStatusCode = [int]$liveGateResponse.StatusCode
            $liveGateSuccess = $liveGateStatusCode -eq 200
            Write-Host "POST /api/stocks/copilot/live-gate attempt ${attempt} -> $liveGateStatusCode"

            $liveGatePayload = $liveGateResponse.Content | ConvertFrom-Json

            $rejectedToolCalls = @($liveGatePayload.rejectedToolCalls)
            $rejectedToolCallCount = $rejectedToolCalls.Count

            $latestTurn = $null
            if ($null -ne $liveGatePayload.session -and $null -ne $liveGatePayload.session.turns) {
                $turnCandidates = @($liveGatePayload.session.turns)
                if ($turnCandidates.Count -gt 0) {
                    $latestTurn = $turnCandidates[-1]
                }
            }

            $llmTraceId = if ($null -ne $latestTurn -and -not [string]::IsNullOrWhiteSpace([string]$latestTurn.llmTraceId)) {
                [string]$latestTurn.llmTraceId
            } else {
                [string]$liveGatePayload.llmTraceId
            }

            if ($null -ne $latestTurn) {
                $finalAnswerStatus = [string]$latestTurn.finalAnswer.status
                $toolResults = @($latestTurn.toolResults)
            }
            else {
                $finalAnswerStatus = $null
                $toolResults = @()
            }

            $acceptance = $liveGatePayload.acceptance
            if ($null -ne $acceptance) {
                $executedToolCallCount = [int]$acceptance.executedToolCallCount
            }

            $toolTraceIds = @($toolResults | Where-Object { -not [string]::IsNullOrWhiteSpace($_.traceId) } | ForEach-Object { [string]$_.traceId })
            $toolNames = @($toolResults | Where-Object { -not [string]::IsNullOrWhiteSpace($_.traceId) } | ForEach-Object { [string]$_.toolName })

            Write-Host "LlmTraceId: $llmTraceId"
            Write-Host "FinalAnswerStatus: $finalAnswerStatus"
            Write-Host "RejectedToolCallCount: $rejectedToolCallCount"
            Write-Host "Acceptance.ExecutedToolCallCount: $executedToolCallCount"
            Write-Host "ToolNames: $([string]::Join(', ', $toolNames))"
            Write-Host "ToolTraceIds: $([string]::Join(', ', $toolTraceIds))"
            break
        }
        catch {
            $liveGateStatusCode = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode.value__ } else { $null }
            $liveGateErrorBody = $null
            if ($_.Exception.Response) {
                $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
                try {
                    $liveGateErrorBody = $reader.ReadToEnd()
                }
                finally {
                    $reader.Dispose()
                }
            }

            Write-Host "POST /api/stocks/copilot/live-gate attempt ${attempt} failed: $($_.Exception.Message)"
            if (-not [string]::IsNullOrWhiteSpace($liveGateErrorBody)) {
                Write-Host $liveGateErrorBody
            }

            if ($attempt -lt 2) {
                Write-Host 'Retrying live gate once after a transient failure.'
                Start-Sleep -Seconds 2
            }
        }
    }
}
catch {
    $liveGateStatusCode = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode.value__ } else { $null }
    Write-Host "POST /api/stocks/copilot/live-gate failed: $($_.Exception.Message)"
}

if (-not $liveGateSuccess) {
    $failures.Add('live gate request failed')
}

if ([string]::IsNullOrWhiteSpace($llmTraceId)) {
    $failures.Add('LlmTraceId missing')
}

if ($acceptedFinalStatuses -notcontains $finalAnswerStatus) {
    $failures.Add("FinalAnswerStatus was '$finalAnswerStatus'")
}

if ($executedToolCallCount -le 0) {
    $failures.Add('Acceptance.ExecutedToolCallCount was not greater than 0')
}

if ($toolTraceIds.Count -le 0) {
    $failures.Add('no non-empty tool trace ids returned')
}

Write-Section 'Audit Log'
if (-not (Test-Path -LiteralPath $logPath)) {
    Write-Host "Audit log missing: $logPath"
    $failures.Add('llm audit log missing')
}
elseif ([string]::IsNullOrWhiteSpace($llmTraceId)) {
    Write-Host 'Skipping audit lookup because LlmTraceId is empty.'
}
else {
    $escapedTraceId = [regex]::Escape($llmTraceId)
    $auditMatches = Select-String -Path $logPath -Pattern "traceId=$escapedTraceId.*stage=(request|response|request-stream|response-stream)" -CaseSensitive:$false
    $auditStages = @($auditMatches | ForEach-Object {
        if ($_.Line -match 'stage=([^\s]+)') { $matches[1] }
    } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
    $auditMatchCount = $auditMatches.Count
    $hasPlainPair = $auditStages -contains 'request' -and $auditStages -contains 'response'
    $hasStreamPair = $auditStages -contains 'request-stream' -and $auditStages -contains 'response-stream'
    $auditSuccess = $hasPlainPair -or $hasStreamPair

    Write-Host "Audit stages: $([string]::Join(', ', $auditStages))"
    Write-Host "Audit match count: $auditMatchCount"

    if (-not $auditSuccess) {
        $failures.Add('llm audit log did not contain request/response or request-stream/response-stream evidence for the trace id')
    }
}

$overallSuccess = $healthSuccess -and $testsSuccess -and $liveGateSuccess -and -not [string]::IsNullOrWhiteSpace($llmTraceId) -and ($acceptedFinalStatuses -contains $finalAnswerStatus) -and $executedToolCallCount -gt 0 -and $toolTraceIds.Count -gt 0 -and $auditSuccess

$summary = [ordered]@{
    BaseUrl = $normalizedBaseUrl
    Symbol = $Symbol
    TaskId = $effectiveTaskId
    SkipTests = $SkipTests.IsPresent
    Health = [ordered]@{
        Success = $healthSuccess
        StatusCode = $healthStatusCode
        Body = $healthBody
    }
    Tests = [ordered]@{
        Skipped = $SkipTests.IsPresent
        Success = $testsSuccess
        ExitCode = $testExitCode
        Command = if ($SkipTests.IsPresent) { $null } else { $testCommandText }
    }
    LiveGate = [ordered]@{
        Success = $liveGateSuccess
        AttemptCount = $liveGateAttemptCount
        StatusCode = $liveGateStatusCode
        ErrorBody = $liveGateErrorBody
        LlmTraceId = $llmTraceId
        FinalAnswerStatus = $finalAnswerStatus
        RejectedToolCallCount = $rejectedToolCallCount
        ExecutedToolCallCount = $executedToolCallCount
        ToolNames = $toolNames
        ToolTraceIds = $toolTraceIds
    }
    Audit = [ordered]@{
        Success = $auditSuccess
        LogPath = $logPath
        Stages = $auditStages
        MatchCount = $auditMatchCount
    }
    Passed = $overallSuccess
    Failures = @($failures)
}

Write-Section 'Summary'
$summary | ConvertTo-Json -Depth 6

if ($overallSuccess) {
    exit 0
}

exit 1