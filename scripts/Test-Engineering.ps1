# Fixed engineering checks, not the unit/solution/full acceptance gate.
[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [ValidateSet('Source', 'Offline', 'OwnedProcess')][string]$Mode = 'Source',
    [string]$GdUnitInputDirectory,
    [string]$EvidenceDirectory
)
$ErrorActionPreference = 'Stop'
$utf8 = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = $utf8
[Console]::InputEncoding = $utf8
$steps = [Collections.Generic.List[object]]::new()
$ownsEvidence = $false
$evidenceError = $null
$out = $null
$exitCode = 1
$report = [ordered]@{
    schemaVersion = 1; kind = 'engineering'; mode = $Mode; status = 'incomplete'
    repositoryRoot = $RepositoryRoot; evidenceDirectory = $null; temporaryEvidenceCleaned = $false
    startedUtc = [DateTime]::UtcNow.ToString('o'); finishedUtc = $null
    steps = @(); failure = $null
}
function Save-Report {
    $report.steps = $steps.ToArray()
    if ($ownsEvidence) {
        try { [IO.File]::WriteAllText((Join-Path $out 'engineering-run.json'), ($report | ConvertTo-Json -Depth 15), $utf8) }
        catch { $script:evidenceError = $_.Exception.Message; [Console]::Error.WriteLine("[engineering:evidence] $script:evidenceError") }
    }
}
function Fail([string]$Code, [string]$Message) { throw "[engineering:$Code] $Message" }
function Quote([string]$Text) { "'" + $Text.Replace("'", "''") + "'" }
function Invoke-Step([string]$Name, [string]$Script, [string[]]$Arguments, [string[]]$Markers) {
    $file = Join-Path $root $Script
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { Fail 'missing-script' $file }
    $tokens = $null; $errors = $null
    $null = [Management.Automation.Language.Parser]::ParseFile($file, [ref]$tokens, [ref]$errors)
    if ($errors.Count) { Fail 'step-syntax' "$file has $($errors.Count) syntax errors; it was not executed." }
    $dir = Join-Path $out $Name
    $null = [IO.Directory]::CreateDirectory($dir)
    $record = [ordered]@{
        name = $Name; script = $Script; scriptArguments = $Arguments; cwd = $root
        startedUtc = [DateTime]::UtcNow.ToString('o'); finishedUtc = $null
        elapsedMs = $null; exitCode = $null; exitKnown = $false
        rootExited = $false; stdoutComplete = $false; stderrComplete = $false
        cleanupComplete = $true; status = 'incomplete'
        stdout = "$Name/stdout.log"; stderr = "$Name/stderr.log"
    }
    $steps.Add($record); Save-Report
    if ($evidenceError) { Fail 'evidence' 'Cannot record step before execution.' }
    # Set both producer encodings, preserving explicit script exits but not a regression's
    # deliberately failing last native child when the regression itself completed normally.
    # PowerShell named arguments require a hashtable splat; quoted '-Name' strings
    # are positional values, unlike native executable argv. This list is fixed above.
    $bindings = [Collections.Generic.List[string]]::new()
    for ($i = 0; $i -lt $Arguments.Count; $i += 2) {
        if ($i + 1 -ge $Arguments.Count -or $Arguments[$i] -notmatch '^-(RepositoryRoot|EvidenceDirectory|InputDirectory)$') { Fail 'arguments' 'Unexpected engineering step parameter.' }
        $bindings.Add((Quote $Arguments[$i].Substring(1)) + '=' + (Quote $Arguments[$i + 1]))
    }
    $call = '& ' + (Quote $file) + ' @childParameters'
    $parameters = '@{' + ($bindings -join ';') + '}'
    $wrapper = @"
`$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new(`$false)
[Console]::InputEncoding = [Text.UTF8Encoding]::new(`$false)
`$global:LASTEXITCODE = 0
try {
    `$childParameters = $parameters
    $call
    if (-not `$?) { if (`$LASTEXITCODE -ne 0) { exit `$LASTEXITCODE }; exit 1 }
    exit 0
} catch { [Console]::Error.WriteLine((`$_ | Out-String)); exit 1 }
"@
    $null = [Management.Automation.Language.Parser]::ParseInput($wrapper, [ref]$tokens, [ref]$errors)
    if ($errors.Count) { Fail 'wrapper-syntax' 'Generated invocation was not executed.' }
    $hostPath = (Get-Process -Id $PID).Path # This process only, not a system enumeration.
    $psi = [Diagnostics.ProcessStartInfo]::new($hostPath)
    $psi.UseShellExecute = $false; $psi.WorkingDirectory = $root
    $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = $utf8; $psi.StandardErrorEncoding = $utf8
    $psi.ArgumentList.Add('-NoProfile'); $psi.ArgumentList.Add('-EncodedCommand')
    $psi.ArgumentList.Add([Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($wrapper)))
    $record.executable = $hostPath; $record.arguments = @($psi.ArgumentList)
    Save-Report
    if ($evidenceError) { Fail 'evidence' 'Cannot record invocation before execution.' }
    $process = [Diagnostics.Process]::new(); $process.StartInfo = $psi
    $stdout = [IO.File]::Create((Join-Path $out $record.stdout))
    $stderr = [IO.File]::Create((Join-Path $out $record.stderr))
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $started = $false; $copyOut = $null; $copyErr = $null
    try {
        $started = $process.Start()
        if (-not $started) { Fail 'start' "Cannot start $Name" }
        $copyOut = $process.StandardOutput.BaseStream.CopyToAsync($stdout)
        $copyErr = $process.StandardError.BaseStream.CopyToAsync($stderr)
        $all = [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($process.WaitForExitAsync(), $copyOut, $copyErr))
        # Same anti-hang command/cleanup budgets as the existing unit/full entry; not a performance claim.
        if (-not $all.Wait(600000)) { $script:exitCode = 2; Fail 'incomplete' "$Name did not confirm root exit and both EOFs; retain evidence and stop." }
        $record.exitCode = $process.ExitCode; $record.exitKnown = $true; $record.rootExited = $true
        $record.stdoutComplete = $copyOut.IsCompletedSuccessfully; $record.stderrComplete = $copyErr.IsCompletedSuccessfully
        $record.status = 'completed'
    } catch {
        $script:exitCode = 2
        throw
    } finally {
        if ($started) {
            $record.rootExited = $process.HasExited
            if ($process.HasExited) {
                $record.exitCode = $process.ExitCode; $record.exitKnown = $true
                if ($record.exitCode -ne 0) { $script:exitCode = $record.exitCode }
            }
            else {
                try { $process.Kill($true); $record.cleanupComplete = $process.WaitForExit(5000) }
                catch { $record.cleanupComplete = $false; $record.cleanupError = $_.Exception.Message }
            }
            if ($copyOut) { $record.stdoutComplete = $copyOut.IsCompletedSuccessfully }
            if ($copyErr) { $record.stderrComplete = $copyErr.IsCompletedSuccessfully }
        }
        $watch.Stop(); $record.elapsedMs = $watch.Elapsed.TotalMilliseconds; $record.finishedUtc = [DateTime]::UtcNow.ToString('o')
        $stdout.Dispose(); $stderr.Dispose(); $process.Dispose(); Save-Report
    }
    [IO.File]::WriteAllText((Join-Path $dir 'exit'), [string]$record.exitCode, $utf8)
    if (-not $record.exitKnown -or -not $record.rootExited -or -not $record.stdoutComplete -or -not $record.stderrComplete -or -not $record.cleanupComplete) {
        $script:exitCode = 2; Fail 'incomplete' "$Name has unknown completion."
    }
    if ($record.exitCode -ne 0) { $script:exitCode = $record.exitCode; Fail 'step' "$Name exited $($record.exitCode); original streams retained." }
    if ($evidenceError) { Fail 'evidence' 'Recording failed after the step; do not continue.' }
    try {
        $text = [IO.File]::ReadAllText((Join-Path $out $record.stdout), [Text.UTF8Encoding]::new($false, $true))
        $null = [IO.File]::ReadAllText((Join-Path $out $record.stderr), [Text.UTF8Encoding]::new($false, $true))
    }
    catch { $script:exitCode = 2; Fail 'encoding' "$Name output is not UTF-8; original bytes retained." }
    foreach ($marker in $Markers) {
        if ($text -notmatch $marker) { $record.status = 'incomplete'; $script:exitCode = 2; Fail 'missing-result' "$Name omitted its documented completion marker." }
    }
}
try {
    if (-not (Test-Path -LiteralPath $RepositoryRoot -PathType Container)) { Fail 'missing-root' 'RepositoryRoot must exist.' }
    $root = [IO.Path]::GetFullPath($RepositoryRoot); $report.repositoryRoot = $root
    if ($GdUnitInputDirectory -and $Mode -ne 'Offline') { Fail 'selection' 'GdUnitInputDirectory applies only to Offline.' }
    if ($Mode -eq 'Offline') {
        if (-not $GdUnitInputDirectory -or -not (Test-Path -LiteralPath $GdUnitInputDirectory -PathType Container)) { Fail 'bundle' 'Offline requires explicit GdUnitInputDirectory; prepare verified input separately.' }
        $bundle = [IO.Path]::GetFullPath($GdUnitInputDirectory)
        $lockPath = Join-Path $root 'scripts/gdunit-packages.lock.json'
        $lock = [IO.File]::ReadAllText($lockPath, $utf8) | ConvertFrom-Json
        $inputFiles = @(Get-ChildItem -LiteralPath $bundle -Force)
        if ($lock.schemaVersion -ne 1 -or @($lock.assets).Count -ne 4 -or $inputFiles.Count -ne 4) { Fail 'bundle' 'Expected exactly four locked files.' }
        foreach ($asset in $lock.assets) {
            $file = Join-Path $bundle $asset.file
            if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -ne $asset.size -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -cne $asset.sha256.ToUpperInvariant()) { Fail 'bundle' "Length/hash mismatch: $($asset.file)" }
        }
    }
    $out = if ($EvidenceDirectory) { [IO.Path]::GetFullPath($EvidenceDirectory) } else { Join-Path ([IO.Path]::GetTempPath()) ('engineering-' + [guid]::NewGuid().ToString('N')) }
    if (Test-Path -LiteralPath $out) { Fail 'output' 'EvidenceDirectory must be new; existing inputs/evidence will not be overwritten.' }
    $null = [IO.Directory]::CreateDirectory($out); $ownsEvidence = $true; $report.evidenceDirectory = $out
    Save-Report
    Invoke-Step 'ast' 'scripts/Test-PowerShellSyntax.ps1' @('-RepositoryRoot', $root) @('(?m)^PASS syntax: [1-9][0-9]* file\(s\)\r?$')
    Invoke-Step 'unit-boundary' 'scripts/Test-UnitBoundary.ps1' @('-RepositoryRoot', $root) @('PASS unit-boundary: core ', 'PASS unit-boundary: cli-unit ')
    if ($Mode -eq 'Offline') {
        Invoke-Step 'syntax-regression' 'tests/Test-PowerShellSyntax.Tests.ps1' @('-EvidenceDirectory', (Join-Path $out 'syntax-cases')) @('PASS syntax regression: [1-9][0-9]* public invocations')
        Invoke-Step 'engineering-regression' 'tests/Test-Engineering.Tests.ps1' @('-EvidenceDirectory', (Join-Path $out 'engineering-cases')) @('PASS engineering regression: [1-9][0-9]* public invocations')
        Invoke-Step 'unit-boundary-regression' 'tests/UnitBoundary.Tests.ps1' @() @('PASS UnitBoundary public-command regression: [1-9][0-9]* cases; fixture cleaned\.')
        Invoke-Step 'game-batches-regression' 'tests/Pal.Game.Tests/GameBatches.Tests.ps1' @() @('PASS: [1-9][0-9]* offline batch metadata groups;')
        Invoke-Step 'process-snapshot-regression' 'tests/Pal.Game.Tests/ProcessSnapshot.Tests.ps1' @() @('PASS: ProcessSnapshot fake regression; real CIM invocations = 0\.')
        Invoke-Step 'gdunit-packages-regression' 'tests/Pal.Game.Tests/GdUnitPackages.Tests.ps1' @('-InputDirectory', $bundle) @('PASS: [1-9][0-9]* offline GdUnit package regression cases;')
        Invoke-Step 'cost-report-regression' 'tests/Pal.Game.Tests/TestCostReport.Tests.ps1' @('-EvidenceDirectory', (Join-Path $out 'cost-cases')) @('PASS: [1-9][0-9]* offline cost report groups; report invocations=[1-9][0-9]*;')
    }
    if ($Mode -eq 'OwnedProcess') {
        Invoke-Step 'dotnet-boundary' 'tests/Pal.Game.Tests/DotnetBoundary.Tests.ps1' @('-EvidenceDirectory', (Join-Path $out 'dotnet-cases')) @('PASS: [1-9][0-9]* real dotnet-boundary cases;')
        Invoke-Step 'unit-entry' 'tests/UnitEntry.Tests.ps1' @('-EvidenceDirectory', (Join-Path $out 'unit-cases')) @('PASS regression: [1-9][0-9]* public invocations;')
    }
    if ($evidenceError) { Fail 'evidence' 'Cannot complete without the command journal.' }
    $report.status = 'complete'; $exitCode = 0
} catch {
    $report.status = if ($exitCode -eq 2) { 'incomplete' } else { 'failed' }
    $report.failure = $_.Exception.Message
    [Console]::Error.WriteLine($report.failure)
} finally {
    $report.finishedUtc = [DateTime]::UtcNow.ToString('o'); Save-Report
    if ($evidenceError -and $exitCode -eq 0) { $exitCode = 1; $report.status = 'failed'; $report.failure = '[engineering:evidence] Final journal write failed.' }
    if ($exitCode -eq 0 -and $ownsEvidence -and -not $EvidenceDirectory) {
        try {
            Remove-Item -LiteralPath $out -Recurse -Force
            $report.temporaryEvidenceCleaned = $true
        } catch {
            $exitCode = 1; $report.status = 'failed'
            $report.failure = "[engineering:cleanup] $($_.Exception.Message)"
            Save-Report
        }
    }
    $report.steps = $steps.ToArray()
}
$report | ConvertTo-Json -Depth 15
exit $exitCode
