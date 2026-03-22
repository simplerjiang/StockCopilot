param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$PackageDir = ".\artifacts\windows-package",
    [string]$InstallerOutputDir = ".\artifacts\installer",
    [string]$AppVersion = "",
    [switch]$SkipPackagePublish,
    [bool]$SelfContained = $true
)

$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$packagePath = Join-Path $root $PackageDir
$installerOutputPath = Join-Path $root $InstallerOutputDir
$publishScript = Join-Path $root "scripts/publish-windows-package.ps1"
$installerScript = Join-Path $root "scripts/windows-installer.iss"
$buildPropsPath = Join-Path $root "Directory.Build.props"

function Resolve-AppVersion {
    param(
        [string]$RequestedVersion,
        [string]$PropsPath
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedVersion)) {
        return $RequestedVersion.Trim()
    }

    if (Test-Path $PropsPath) {
        [xml]$props = Get-Content $PropsPath
        $versionNode = $props.Project.PropertyGroup.Version | Select-Object -First 1
        if ($versionNode -and -not [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
            return $versionNode.InnerText.Trim()
        }
    }

    return "0.0.2"
}

function Resolve-IsccPath {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $knownPaths = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )

    foreach ($path in $knownPaths) {
        if (Test-Path $path) {
            return $path
        }
    }

    return $null
}

if (-not $SkipPackagePublish) {
    & $publishScript -Configuration $Configuration -RuntimeIdentifier $RuntimeIdentifier -OutputDir $PackageDir -SelfContained:$SelfContained
}

$resolvedAppVersion = Resolve-AppVersion -RequestedVersion $AppVersion -PropsPath $buildPropsPath

if (-not (Test-Path (Join-Path $packagePath "SimplerJiangAiAgent.Desktop.exe"))) {
    throw "Packaged desktop output was not found at $packagePath. Run scripts/publish-windows-package.ps1 first or remove -SkipPackagePublish."
}

New-Item -ItemType Directory -Force -Path $installerOutputPath | Out-Null

$isccPath = Resolve-IsccPath
if (-not $isccPath) {
    throw "ISCC.exe was not found. Install Inno Setup 6 first, then rerun scripts/build-windows-installer.ps1."
}

Write-Host "Building Setup.exe with Inno Setup..."
& $isccPath "/DSourceDir=$packagePath" "/DOutputDir=$installerOutputPath" "/DAppVersion=$resolvedAppVersion" $installerScript

Write-Host "Installer ready: $installerOutputPath"