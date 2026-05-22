param(
    [string]$DraftPath = ".automation/github-issue-drafts-20260521.md",
    [string]$Repository = "",
    [string[]]$Labels = @(),
    [switch]$UseGh,
    [switch]$ForceDuplicate,
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"

function Get-RepositoryFromRemote {
    $remote = git remote get-url origin 2>$null
    if (-not $remote) {
        throw "Cannot determine repository. Pass -Repository owner/name."
    }

    if ($remote -match "github\.com[:/](?<owner>[^/]+)/(?<repo>[^/.]+)(\.git)?$") {
        return "$($Matches.owner)/$($Matches.repo)"
    }

    throw "Cannot parse GitHub repository from origin remote: $remote"
}

function Split-IssueDrafts {
    param([string]$Markdown)

    $pattern = "(?m)^### ISSUE-DRAFT-\d+:\s*(.+)$"
    $matches = [regex]::Matches($Markdown, $pattern)
    $issues = @()

    for ($i = 0; $i -lt $matches.Count; $i++) {
        $match = $matches[$i]
        $title = $match.Groups[1].Value.Trim()
        $bodyStart = $match.Index + $match.Length
        $bodyEnd = if ($i + 1 -lt $matches.Count) { $matches[$i + 1].Index } else { $Markdown.Length }
        $body = $Markdown.Substring($bodyStart, $bodyEnd - $bodyStart).Trim()
        if ($title -and $body) {
            $issues += [pscustomobject]@{
                Title = $title
                Body = $body
            }
        }
    }

    return $issues
}

function New-IssueWithGh {
    param(
        [string]$Repository,
        [string]$Title,
        [string]$Body,
        [string[]]$Labels
    )

    $tempFile = New-TemporaryFile
    try {
        Set-Content -LiteralPath $tempFile -Value $Body -Encoding UTF8

        $args = @("issue", "create", "--repo", $Repository, "--title", $Title, "--body-file", $tempFile)
        foreach ($label in $Labels) {
            if ($label) {
                $args += @("--label", $label)
            }
        }

        & gh @args
        if ($LASTEXITCODE -ne 0) {
            throw "gh issue create failed for: $Title"
        }
    }
    finally {
        Remove-Item -LiteralPath $tempFile -Force -ErrorAction SilentlyContinue
    }
}

function Get-ExistingIssueTitlesWithGh {
    param([string]$Repository)

    $json = & gh issue list --repo $Repository --state all --limit 1000 --json title 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $json) {
        return @()
    }

    return @($json | ConvertFrom-Json | ForEach-Object { $_.title })
}

function New-IssueWithRest {
    param(
        [string]$Repository,
        [string]$Title,
        [string]$Body,
        [string[]]$Labels
    )

    $token = if ($env:GH_TOKEN) { $env:GH_TOKEN } else { $env:GITHUB_TOKEN }
    if (-not $token) {
        throw "No GH_TOKEN or GITHUB_TOKEN found. Set one, or install/login gh and pass -UseGh."
    }

    $payload = @{
        title = $Title
        body = $Body
    }
    if ($Labels.Count -gt 0) {
        $payload.labels = $Labels
    }

    $headers = @{
        Authorization = "Bearer $token"
        Accept = "application/vnd.github+json"
        "X-GitHub-Api-Version" = "2022-11-28"
        "User-Agent" = "StockCopilot-IssueDraftUploader"
    }

    Invoke-RestMethod `
        -Method Post `
        -Uri "https://api.github.com/repos/$Repository/issues" `
        -Headers $headers `
        -Body ($payload | ConvertTo-Json -Depth 5) `
        -ContentType "application/json; charset=utf-8"
}

$resolvedDraftPath = Resolve-Path -LiteralPath $DraftPath
$markdown = Get-Content -Raw -Encoding UTF8 -LiteralPath $resolvedDraftPath
$issues = Split-IssueDrafts -Markdown $markdown

if ($issues.Count -eq 0) {
    throw "No ISSUE-DRAFT entries found in $resolvedDraftPath"
}

if (-not $Repository) {
    $Repository = Get-RepositoryFromRemote
}

Write-Host "Repository: $Repository"
Write-Host "Draft file: $resolvedDraftPath"
Write-Host "Issues found: $($issues.Count)"

$existingTitles = @()
if ($UseGh -and -not $ForceDuplicate) {
    $existingTitles = Get-ExistingIssueTitlesWithGh -Repository $Repository
    Write-Host "Existing issues checked: $($existingTitles.Count)"
}

foreach ($issue in $issues) {
    if (-not $ForceDuplicate -and $existingTitles -contains $issue.Title) {
        Write-Host "Skipping existing issue: $($issue.Title)"
        continue
    }

    if ($WhatIf) {
        Write-Host "[WhatIf] Would create issue: $($issue.Title)"
        continue
    }

    Write-Host "Creating issue: $($issue.Title)"
    if ($UseGh) {
        New-IssueWithGh -Repository $Repository -Title $issue.Title -Body $issue.Body -Labels $Labels
    } else {
        $created = New-IssueWithRest -Repository $Repository -Title $issue.Title -Body $issue.Body -Labels $Labels
        Write-Host "Created: $($created.html_url)"
    }
}
