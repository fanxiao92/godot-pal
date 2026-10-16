# Shared local/CI acceptance entrypoint. Run on a clean checkout for clean-build evidence.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GodotPath,
    [string]$EvidenceDirectory
)
$ErrorActionPreference = 'Stop'
$GodotPath = (Resolve-Path $GodotPath).Path
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$evidence = if ($EvidenceDirectory) { [IO.Path]::GetFullPath($EvidenceDirectory) } else {
    Join-Path ([IO.Path]::GetTempPath()) ('pal-m0-' + [guid]::NewGuid().ToString('N'))
}
if (Test-Path $evidence) { throw 'Use a new evidence directory; this run requires an empty package cache.' }
New-Item -ItemType Directory $evidence | Out-Null
Write-Host "M0 evidence: $evidence"
$previousPackages = $env:NUGET_PACKAGES
$previousHttpCache = $env:NUGET_HTTP_CACHE_PATH
$previousFallback = $env:NUGET_FALLBACK_PACKAGES
$transcribing = $false
Push-Location $repository
try {
    Start-Transcript -Path (Join-Path $evidence 'acceptance.log') | Out-Null
    $transcribing = $true
    # Both SDK resolution and restore use an empty package directory and nuget.org.
    $env:NUGET_PACKAGES = Join-Path $evidence 'packages'
    $env:NUGET_HTTP_CACHE_PATH = Join-Path $evidence 'http-cache'
    $env:NUGET_FALLBACK_PACKAGES = $null
    if ($env:DOTNET_ROLL_FORWARD) { throw 'Remove global DOTNET_ROLL_FORWARD before acceptance.' }

    function Invoke-DotnetChecked {
        param([string[]]$Arguments)
        Write-Host "dotnet $($Arguments -join ' ')"
        & dotnet @Arguments
        if ($LASTEXITCODE -ne 0) { throw "dotnet command failed with exit code $LASTEXITCODE" }
    }

    Invoke-DotnetChecked @('--info')
    Invoke-DotnetChecked @('restore', 'GodotPal.sln', '--no-http-cache')
    Invoke-DotnetChecked @('build', 'GodotPal.sln', '--no-restore')
    # Includes pure Core tests and actual CLI processes with output/exit-code assertions.
    Invoke-DotnetChecked @('test', 'GodotPal.sln', '--no-build', '--no-restore', '--logger', 'trx', '--results-directory', (Join-Path $evidence 'tests'))
    & (Join-Path $PSScriptRoot 'Test-GameStartup.ps1') -GodotPath $GodotPath -LogDirectory (Join-Path $evidence 'godot')
    'PASS: restore, build, Core tests, CLI process tests, Godot import/startup.' |
        Set-Content (Join-Path $evidence 'PASS.txt')
    Write-Host 'PASS: all automated M0 acceptance checks completed. Desktop visual acceptance is separate.'
} finally {
    $env:NUGET_PACKAGES = $previousPackages
    $env:NUGET_HTTP_CACHE_PATH = $previousHttpCache
    $env:NUGET_FALLBACK_PACKAGES = $previousFallback
    if ($transcribing) { Stop-Transcript | Out-Null }
    Pop-Location
}
