# Offline scene4 profile authoring. Derives role0's raw WORD from read-only DATA.MKF and never overwrites.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$DataMkf,
    [Parameter(Mandatory)][string]$GodotPath,
    [string]$ResourceRoot = 'res://Assets/StaticScene4',
    [string]$ProfileUri = 'res://Assets/StaticScene4/Profiles/Scene4Static.tres'
)
$ErrorActionPreference = 'Stop'
$DataMkf = (Resolve-Path -LiteralPath $DataMkf).Path
$GodotPath = (Resolve-Path -LiteralPath $GodotPath).Path
if ($ResourceRoot -cne 'res://Assets/StaticScene4' -or
    $ProfileUri -cne 'res://Assets/StaticScene4/Profiles/Scene4Static.tres') {
    throw 'Static profile output must use the documented new StaticScene4 profile URI.'
}
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../src/Pal.Game'))
$scenePath = Join-Path $project 'Assets/StaticScene4/Scenes/Scene.tscn'
$profilePath = Join-Path $project 'Assets/StaticScene4/Profiles/Scene4Static.tres'
if (-not [IO.File]::Exists($scenePath)) { throw 'Prepared scene4 native PackedScene is missing.' }
if ([IO.File]::Exists($profilePath)) { throw 'Refusing to overwrite existing static scene4 profile.' }

# The M1 exporter validates DATA.MKF chunk 3 as the 900-byte raw PLAYERROLES table.
# rgwWalkFrames for role0 is at byte offset 768 in that table.
$bytes = [IO.File]::ReadAllBytes($DataMkf)
if ($bytes.Length -lt 20) { throw 'DATA.MKF is too short to contain its chunk offset table.' }
$firstOffset = [BitConverter]::ToUInt32($bytes, 0)
if ($firstOffset -lt 20 -or ($firstOffset % 4) -ne 0 -or $firstOffset -gt $bytes.Length) {
    throw 'DATA.MKF has an invalid offset table.'
}
$chunkCount = [int]($firstOffset / 4) - 1
if ($chunkCount -lt 4) { throw 'DATA.MKF does not contain PLAYERROLES chunk 3.' }
$chunkStart = [BitConverter]::ToUInt32($bytes, 3 * 4)
$chunkEnd = [BitConverter]::ToUInt32($bytes, 4 * 4)
if ($chunkStart -gt $chunkEnd -or $chunkEnd -gt $bytes.Length -or $chunkEnd - $chunkStart -ne 900) {
    throw 'DATA.MKF chunk 3 is not the expected 900-byte raw PLAYERROLES record.'
}
$walkFramesWord = [BitConverter]::ToUInt16($bytes, [int]$chunkStart + 768)
$dataSha256 = (Get-FileHash -LiteralPath $DataMkf -Algorithm SHA256).Hash.ToLowerInvariant()
[Array]::Clear($bytes, 0, $bytes.Length)

# Required privacy-safe global process gate immediately before starting the Godot authoring process.
$processes = @(& (Join-Path $PSScriptRoot 'Get-TestProcesses.ps1'))
if (@($processes | Where-Object Name -Like 'godot*.exe').Count -gt 0) {
    throw 'Existing Godot process; static profile authoring was not started.'
}
if (@($processes | Where-Object {
    $_.Name -in @('dotnet.exe','testhost.exe') -and
    (($_.CommandLine -like '*godot-pal*') -or ($_.CommandLine -like '*Pal.Game*') -or ($_.CommandLine -like '*Pal.Prepare*'))
}).Count -gt 0) {
    throw 'Existing repository test/prepare process; static profile authoring was not started.'
}
$info = [Diagnostics.ProcessStartInfo]::new()
$info.FileName = $GodotPath
$info.WorkingDirectory = $project
$info.UseShellExecute = $false
$info.RedirectStandardOutput = $true
$info.RedirectStandardError = $true
foreach ($arg in @('--headless','--path',$project,'--script','res://Editor/WriteStaticScene4Profile.gd','--',
    '--resource-root',$ResourceRoot,'--profile-uri',$ProfileUri,'--walk-frames-word',"$walkFramesWord",'--data-sha256',$dataSha256)) {
    $info.ArgumentList.Add($arg)
}
$process = [Diagnostics.Process]::Start($info)
if ($null -eq $process) { throw 'Godot profile author process did not start.' }
try {
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(60000)) {
        $process.Kill($true); $process.WaitForExit()
        throw 'Godot profile author process timed out; preserve all outputs and inspect diagnostics.'
    }
    $text = $stdout.GetAwaiter().GetResult() + "`n" + $stderr.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0 -or $text -match '(?im)^\s*(ERROR:|SCRIPT ERROR:|Unhandled exception)') {
        throw "Godot profile author failed (exit $($process.ExitCode)): $text"
    }
    if ($text -notmatch ('(?m)^PAL_STATIC_PROFILE_WRITTEN:' + [regex]::Escape($ProfileUri) + '\s*$')) {
        throw 'Godot profile author did not confirm the selected profile URI.'
    }
} finally { $process.Dispose() }
Write-Host "Authored source-bound static profile: $ProfileUri"
