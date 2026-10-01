<#
One-click: restore + build + test + selfcheck for the lite edition. Usage:
  powershell -ExecutionPolicy Bypass -File .\build.ps1 [-Configuration Debug|Release] [-SkipTests]
NOTE: keep this file ASCII-only to avoid PowerShell codepage issues.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$scriptsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = $scriptsDir
$sln = Join-Path $root 'FtpJumperLite.sln'
$appProject = Join-Path $root 'src\FtpJumperLite\FtpJumperLite.csproj'
$testProject = Join-Path $root 'tests\FtpJumperLite.Tests\FtpJumperLite.Tests.csproj'
$serverProject = Join-Path $root 'tests\FtpJumperLite.TestFtpServer\FtpJumperLite.TestFtpServer.csproj'
$toolProject = Join-Path $root 'tools\FtpLiteTestServer\FtpLiteTestServer.csproj'

# locate dotnet
$dotnet = $null
foreach ($candidate in @(
        (Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'),
        (Join-Path $root '.tools\dotnet\dotnet.exe'),
        (Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)
    )) {
    if ($candidate -and (Test-Path $candidate)) { $dotnet = $candidate; break }
}
if (-not $dotnet) {
    throw 'dotnet SDK not found. Install the .NET 8 SDK first (https://aka.ms/dotnet/download, x64).'
}
Write-Host "Using dotnet: $dotnet"

# NuGet package folder: prefer one inside this edition so the build stays self-contained.
$packages = Join-Path $root '.packages'
if (Test-Path $packages) {
    $env:NUGET_PACKAGES = $packages
    Write-Host "Using local NuGet packages: $packages"
}

# solution: create if needed, then add any missing projects (idempotent)
if (-not (Test-Path $sln)) {
    Push-Location $root
    try {
        & $dotnet new sln -n FtpJumperLite | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'solution creation failed.' }
    }
    finally { Pop-Location }
}
$listed = (& $dotnet sln $sln list) -join "`n"
foreach ($project in @($appProject, $testProject, $serverProject, $toolProject)) {
    if (-not (Test-Path $project)) { continue }
    if ($listed -notlike "*$(Split-Path $project -Leaf)*") {
        & $dotnet sln $sln add $project | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "failed to add project to solution: $project" }
    }
}

Write-Host '== restore =='
& $dotnet restore $sln
if ($LASTEXITCODE -ne 0) { throw 'restore failed.' }

Write-Host "== build ($Configuration) =="
# UseSharedCompilation=false: 避免 csc 编译器服务器在某些受限环境里被取消（MSB5021）。
& $dotnet build $sln -c $Configuration --no-restore -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'build failed.' }

if (-not $SkipTests) {
    Write-Host '== test =='
    & $dotnet test $testProject -c $Configuration --no-build
    if ($LASTEXITCODE -ne 0) { throw 'test failed.' }
}

Write-Host '== selfcheck =='
$exe = Join-Path $root "src\FtpJumperLite\bin\$Configuration\net48\FtpJumperLite.exe"
if (-not (Test-Path $exe)) { throw "build output not found: $exe" }
$outFile = Join-Path $root 'selfcheck.out.txt'
$errFile = Join-Path $root 'selfcheck.err.txt'
$p = Start-Process -FilePath $exe -ArgumentList '--selfcheck' -Wait -PassThru `
    -RedirectStandardOutput $outFile -RedirectStandardError $errFile -NoNewWindow
if ($p.ExitCode -ne 0) {
    Get-Content $outFile -ErrorAction SilentlyContinue
    Get-Content $errFile -ErrorAction SilentlyContinue
    throw "selfcheck failed (exit code $($p.ExitCode))."
}

Write-Host 'ALL DONE.'
Write-Host "Main program: $exe"
Write-Host "Selfcheck output: $outFile"
$toolExe = Join-Path $root "tools\FtpLiteTestServer\bin\$Configuration\net48\FtpLiteTestServer.exe"
if (Test-Path $toolExe) {
    Write-Host "Local test FTP server: $toolExe"
}
