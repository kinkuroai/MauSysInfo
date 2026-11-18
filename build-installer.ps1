<#
.SYNOPSIS
    Builds publish\MauSysInfo-<version>-Setup.exe and the publish\update.json that announces it.
.DESCRIPTION
    Publishes the single-file app, then compiles installer\MauSysInfo.iss with Inno Setup 6.
    The version comes from <Version> in src\MauSysInfo\MauSysInfo.csproj, so bump it there.
    Needs Inno Setup 6:  winget install JRSoftware.InnoSetup

    To release: upload both files to the release folder on the official site
    (https://downloads.kinkuroshideout.com/mau-sysinfo/). Upload the installer first and update.json
    last: installed copies offer the update as soon as update.json changes.
.PARAMETER SkipPublish
    Package the existing publish\MauSysInfo.exe without rebuilding it.
.PARAMETER Notes
    "What's new" text shown in the app's update prompt (optional; use `n for line breaks).
#>
param([switch]$SkipPublish, [string]$Notes = "")

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
# Must match UpdateService.ReleaseFolder in the app.
$releaseFolder = 'https://downloads.kinkuroshideout.com/mau-sysinfo/'

[xml]$project = Get-Content "$root\src\MauSysInfo\MauSysInfo.csproj"
$version = $project.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> found in MauSysInfo.csproj." }

if (-not $SkipPublish) {
    Write-Host "Publishing Mau SysInfo $version..."
    dotnet publish "$root\src\MauSysInfo" -p:PublishProfile=SingleFile -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
}
if (-not (Test-Path "$root\publish\MauSysInfo.exe")) { throw "publish\MauSysInfo.exe is missing (run without -SkipPublish)." }

$iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
              "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
              "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) { throw "Inno Setup 6 was not found. Install it with: winget install JRSoftware.InnoSetup" }

Write-Host "Building the installer..."
& $iscc /Qp "/DAppVersion=$version" "$root\installer\MauSysInfo.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed (exit code $LASTEXITCODE)." }

$setup = Get-Item "$root\publish\MauSysInfo-$version-Setup.exe"

# What installed copies read to find this release (see UpdateService in the app).
$manifest = [ordered]@{
    version   = $version
    url       = $releaseFolder + $setup.Name
    sha256    = (Get-FileHash $setup.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    published = (Get-Date).ToString('yyyy-MM-dd')
    notes     = $Notes
}
$manifestPath = Join-Path $root 'publish\update.json'
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))

Write-Host ("Done: {0} ({1:N1} MB)" -f $setup.FullName, ($setup.Length / 1MB))
Write-Host "      $manifestPath"
Write-Host "Upload both to $releaseFolder (installer first, update.json last)."
