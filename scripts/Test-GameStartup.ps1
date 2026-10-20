# Headless Main regression with synthetic native resources only; no original data is needed.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GodotPath,
    [ValidateRange(1, 600)][int]$TimeoutSeconds = 60,
    [string]$LogDirectory,
    # Optional existing native Assets root. Caller-owned resources are never removed.
    [string]$ResourceRoot
)
$ErrorActionPreference = 'Stop'
$GodotPath = (Resolve-Path $GodotPath).Path
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repository 'src/Pal.Game'
$logs = if ($LogDirectory) { [IO.Path]::GetFullPath($LogDirectory) } else {
    Join-Path ([IO.Path]::GetTempPath()) ('pal-startup-' + [guid]::NewGuid().ToString('N'))
}
New-Item -ItemType Directory -Force $logs | Out-Null
Write-Host "Startup logs: $logs"

# Reuse the existing bounded startup process boundary for build and explicit preparation too.
function Invoke-CheckedGodot {
    param([string]$Name, [string[]]$Arguments, [switch]$RequireMarker, [string]$ExpectedOutput, [string]$File = $GodotPath)
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $File
    $info.WorkingDirectory = $repository
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    try {
        $null = $process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $timedOut = -not $process.WaitForExit($TimeoutSeconds * 1000)
        if ($timedOut) { $process.Kill($true); $process.WaitForExit() }
        $text = $stdout.GetAwaiter().GetResult() + "`n" + $stderr.GetAwaiter().GetResult()
        $exitCode = $process.ExitCode
    } finally { $process.Dispose() }
    "COMMAND=$File $($Arguments -join ' ')`n$text`nEXIT_CODE=$exitCode`nTIMED_OUT=$timedOut" |
        Set-Content (Join-Path $logs "$Name.log") -Encoding utf8
    @{ command = $File; arguments = $Arguments; exitCode = $exitCode; timedOut = $timedOut } |
        ConvertTo-Json -Depth 4 | Set-Content (Join-Path $logs "$Name.json")
    $plain = $text -replace '\x1B\[[0-?]*[ -/]*[@-~]', ''
    if ($timedOut) { throw "$Name timed out (process tree killed); see $logs" }
    if ($exitCode -ne 0) { throw "$Name exited with $exitCode; see $logs" }
    if ($plain -match '(?im)^\s*(ERROR:|SCRIPT ERROR:|Unhandled exception|.*\berror (MSB|NU|CS)\d+)') {
        throw "$Name logged an error; see $logs"
    }
    if ($ExpectedOutput -and $plain.Trim() -notmatch $ExpectedOutput) {
        throw "$Name missing required output; see $logs"
    }
    if ($RequireMarker -and $plain -notmatch '(?m)^PAL_GAME_READY:Core ready\s*$') {
        throw "$Name did not report the expected core startup state; see $logs"
    }
    Write-Host "$Name passed (exit 0, no errors or timeout)"
}
function Get-OwnedSnapshot([string]$Path) {
    @(Get-ChildItem -LiteralPath $Path -File -Recurse -Force | Sort-Object FullName | ForEach-Object {
        "$($_.FullName):$($_.Length):$($_.LastWriteTimeUtc.Ticks):$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash)"
    })
}
$inputRoot = $null
$outputRoot = $null
$inputSnapshot = $null
$outputSnapshot = $null
try {
    Invoke-CheckedGodot 'version' @('--version') -ExpectedOutput '^4\.7\.2\.stable\.mono\.official\.ed1daf0bf$'
    Invoke-CheckedGodot 'import' @('--headless', '--path', $project, '--editor', '--import')
    if (-not $ResourceRoot) {
        $id = [guid]::NewGuid().ToString('N')
        $inputRoot = Join-Path ([IO.Path]::GetTempPath()) "pal-startup-input-$id"
        $outputRoot = Join-Path $project "Assets/Startup-$id"
        if ((Test-Path -LiteralPath $inputRoot) -or (Test-Path -LiteralPath $outputRoot)) { throw 'Unique synthetic roots already exist; refusing to touch them.' }
        Invoke-CheckedGodot 'fixture-build' @('build', (Join-Path $repository 'tests/Pal.Prepare.Tests'), '--nologo') -File 'dotnet'
        $assembly = [Reflection.Assembly]::LoadFrom((Join-Path $repository 'tests/Pal.Prepare.Tests/bin/Debug/net10.0/Pal.Prepare.Tests.dll'))
        $assembly.GetType('Pal.TestFixtures.M1Bundle').GetMethod('Write').Invoke($null, [object[]]@([string]$inputRoot)) | Out-Null
        $inputSnapshot = @(Get-OwnedSnapshot $inputRoot)
        Invoke-CheckedGodot 'prepare' @('-NoProfile', '-NonInteractive', '-File', (Join-Path $PSScriptRoot 'Prepare-GameScene.ps1'), '-InputPath', $inputRoot, '-OutputPath', $outputRoot, '-GodotPath', $GodotPath, '-TimeoutSeconds', "$TimeoutSeconds") -File 'pwsh' -ExpectedOutput 'Prepared native Godot scene: Scenes/Scene.tscn'
        $outputSnapshot = @(Get-OwnedSnapshot $outputRoot)
        @{ inputRoot = $inputRoot; inputFiles = $inputSnapshot; outputRoot = $outputRoot; outputFiles = $outputSnapshot } |
            ConvertTo-Json -Depth 4 | Set-Content (Join-Path $logs 'synthetic-owned.json')
        $ResourceRoot = "res://Assets/Startup-$id"
    }
    # The runtime validates caller-supplied resource URIs through its public Main entrypoint.
    $sceneUri = $ResourceRoot.TrimEnd('/') + '/Scenes/Scene.tscn'
    Invoke-CheckedGodot 'startup' @('--headless', '--path', $project, '--quit-after', '60', '--', '--m2-resources', $ResourceRoot) -RequireMarker -ExpectedOutput "(?m)^PAL_MAP_LOADED:$([regex]::Escape($sceneUri))\s*$"
    Write-Host 'PASS: actual Main loaded the selected native map and Core ready (headless; not graphical acceptance).'
} finally {
    # Only successful, recorded, unique synthetic trees are ours to remove. Preserve unknown/changed output.
    foreach ($owned in @(@{ Path = $inputRoot; Snapshot = $inputSnapshot }, @{ Path = $outputRoot; Snapshot = $outputSnapshot })) {
        if ($owned.Path -and (Test-Path -LiteralPath $owned.Path)) {
            if ($null -eq $owned.Snapshot -or (Compare-Object @($owned.Snapshot) @(Get-OwnedSnapshot $owned.Path))) {
                Write-Warning "Preserved unrecorded or changed synthetic tree: $($owned.Path)"
            } else {
                Remove-Item -LiteralPath $owned.Path -Recurse -Force
                "REMOVED_OWNED_TREE=$($owned.Path)" | Add-Content (Join-Path $logs 'cleanup.log')
            }
        }
    }
}
