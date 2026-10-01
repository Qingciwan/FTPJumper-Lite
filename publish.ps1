<#
Build Release and assemble a clean deploy folder for the classroom PC (lite edition).

Usage:
  powershell -ExecutionPolicy Bypass -File .\publish.ps1 [-OutDir <folder>] [-IncludeTeacherConfig]

Compliance notes:
  * The payload always gets LICENSE.txt and THIRD-PARTY-NOTICES.txt.
  * By default the payload gets the PLACEHOLDER subjects.json (from .\subjects.json).
    If the target folder already holds a real teacher config, that file is first backed up to
    .\_private\ (outside the payload) and then replaced by the placeholder, so a
    published folder never ships real FTP credentials by accident.
  * Pass -IncludeTeacherConfig to keep the real config in the payload (classroom deployment copy).
  * Extra files left in the target folder (old zips, logs, *.bak) are reported, never deleted.

NOTE: keep this file ASCII-only to avoid PowerShell codepage issues.
#>
[CmdletBinding()]
param(
    [string]$OutDir = '',
    [switch]$IncludeTeacherConfig
)

$ErrorActionPreference = 'Stop'
$scriptsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = $scriptsDir
$payload = @('FtpJumperLite.exe', 'FtpJumperLite.exe.config', 'Newtonsoft.Json.dll')
$docs = @('README.md', 'LICENSE.txt', 'THIRD-PARTY-NOTICES.txt')
$binDir = Join-Path $root 'src\FtpJumperLite\bin\Release\net48'

Write-Host '== build Release =='
& (Join-Path $scriptsDir 'build.ps1') -Configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }

if (-not (Test-Path $binDir)) { throw "Build output not found: $binDir" }

function Publish-To([string]$dest) {
    New-Item -ItemType Directory -Force -Path $dest | Out-Null

    foreach ($file in $payload) {
        Copy-Item (Join-Path $binDir $file) (Join-Path $dest $file) -Force
    }

    foreach ($doc in $docs) {
        $source = Join-Path $root $doc
        if (-not (Test-Path $source)) { throw "Required file missing: $source" }
        Copy-Item $source (Join-Path $dest $doc) -Force
    }

    # subjects.json: placeholder by default, real config only on request.
    $templateConfig = Join-Path $root 'subjects.json'
    $destConfig = Join-Path $dest 'subjects.json'
    if ($IncludeTeacherConfig) {
        if (Test-Path $destConfig) {
            Write-Host 'Kept the existing subjects.json in the payload (-IncludeTeacherConfig).'
        }
        else {
            Copy-Item $templateConfig $destConfig -Force
            Write-Host 'Seeded subjects.json from the placeholder template.'
        }
    }
    else {
        if (Test-Path $destConfig) {
            $privateDir = Join-Path $root '_private'
            New-Item -ItemType Directory -Force -Path $privateDir | Out-Null
            $backup = Join-Path $privateDir ('subjects.teacher-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.json')
            Copy-Item $destConfig $backup -Force
            Write-Host "Real config moved out of the payload, backup: $backup"
        }

        Copy-Item $templateConfig $destConfig -Force
        Write-Host 'Published placeholder subjects.json (no real credentials).'
    }

    # Report anything else sitting in the payload folder instead of deleting it.
    $allowed = @($payload + $docs + @('subjects.json'))
    $extra = Get-ChildItem $dest -Force | Where-Object { $allowed -notcontains $_.Name }
    if ($extra) {
        Write-Host ''
        Write-Host 'WARNING: extra files in the payload folder (review before shipping):'
        $extra | ForEach-Object { Write-Host ('  ' + $_.Name) }
    }
}

if ([string]::IsNullOrWhiteSpace($OutDir)) { $OutDir = Join-Path $root 'release' }

try {
    Publish-To $OutDir
}
catch [System.IO.IOException] {
    $OutDir = Join-Path $root ('release-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    Write-Host "Target is in use (app running?). Publishing to: $OutDir"
    Publish-To $OutDir
}

Write-Host ''
Write-Host "Deploy folder ready: $OutDir"
Get-ChildItem $OutDir -Name | ForEach-Object { Write-Host "  $_" }
