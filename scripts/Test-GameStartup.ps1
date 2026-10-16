# Requires PowerShell 7 and an already-built Debug game. No original data is needed.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GodotPath,
    [ValidateRange(1, 600)][int]$TimeoutSeconds = 60
)
$ErrorActionPreference = 'Stop'
$GodotPath = (Resolve-Path $GodotPath).Path
$project = (Resolve-Path (Join-Path $PSScriptRoot '../src/Pal.Game')).Path
$logs = Join-Path ([IO.Path]::GetTempPath()) ('pal-startup-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $logs | Out-Null
Write-Host "Startup logs: $logs"

function Invoke-CheckedGodot {
    param([string]$Name, [string[]]$Arguments, [switch]$RequireMarker, [string]$ExpectedOutput)
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $GodotPath
    $info.WorkingDirectory = $project
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
    $text | Set-Content (Join-Path $logs "$Name.log") -Encoding utf8
    $plain = $text -replace '\x1B\[[0-?]*[ -/]*[@-~]', ''
    if ($timedOut) { throw "$Name timed out; see $logs" }
    if ($exitCode -ne 0) { throw "$Name exited with $exitCode; see $logs" }
    if ($plain -match '(?im)^\s*(ERROR:|SCRIPT ERROR:|Unhandled exception|.*\berror (MSB|NU|CS)\d+)') {
        throw "$Name logged an error; see $logs"
    }
    if ($ExpectedOutput -and $plain.Trim() -notmatch $ExpectedOutput) {
        throw "$Name does not match the required engine version; see $logs"
    }
    if ($RequireMarker -and $plain -notmatch '(?m)^PAL_GAME_READY:Core ready\s*$') {
        throw "$Name did not report the expected core startup state; see $logs"
    }
    Write-Host "$Name passed (exit 0, no errors or timeout)"
}

Invoke-CheckedGodot 'version' @('--version') -ExpectedOutput '^4\.7\.2\.stable\.mono\.official\.ed1daf0bf$'
Invoke-CheckedGodot 'import' @('--headless', '--path', $project, '--editor', '--import')
Invoke-CheckedGodot 'startup' @('--headless', '--path', $project, '--quit-after', '60') -RequireMarker
Write-Host 'PASS: game loaded C# and displayed the core startup state in its scene tree (headless only).'
