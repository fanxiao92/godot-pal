# Shared local/CI acceptance. Synthetic inputs only; graphical acceptance remains separate.
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
Write-Host "M0/M2/M3 evidence: $evidence"
$previous = @{}
foreach ($key in @('NUGET_PACKAGES', 'NUGET_HTTP_CACHE_PATH', 'NUGET_FALLBACK_PACKAGES', 'GODOT_BIN', 'PAL_TEST_GODOT')) {
    $previous[$key] = [Environment]::GetEnvironmentVariable($key)
}
$transcribing = $false
$commands = [Collections.Generic.List[object]]::new()
$checks = [Collections.Generic.List[object]]::new()
$runIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$executionIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$completedBatches = [Collections.Generic.List[object]]::new()
$sequence = 0
& (Join-Path $repository 'tests/Pal.Game.Tests/ProcessSnapshot.Tests.ps1')
$baseline = @(& (Join-Path $PSScriptRoot 'Get-TestProcesses.ps1'))
$existingGodot = @($baseline | Where-Object Name -Like 'godot*.exe' | Select-Object ProcessId, Name)
if ($existingGodot.Count) {
    $existingGodot | ConvertTo-Json | Set-Content (Join-Path $evidence 'processes-baseline.json')
    throw 'Existing Godot process; inspect processes-baseline.json (not querying details or killing it).'
}
function Save-ProcessSnapshot([string]$Name) {
    $processes = @(& (Join-Path $PSScriptRoot 'Get-TestProcesses.ps1'))
    @{ utc = [DateTime]::UtcNow; query = 'Server-filtered Win32_Process: global Godot PID/Name only; explicit repository dotnet/testhost/vstest identities'; baselineIds = @($baseline.ProcessId); processes = $processes; count = $processes.Count } |
        ConvertTo-Json -Depth 6 | Set-Content (Join-Path $evidence "$Name.json")
    $new = @($processes | Where-Object { $_.ProcessId -notin @($baseline.ProcessId) })
    if ($new.Count) { throw "Residual test processes; inspect $Name.json (not killing unrelated processes)." }
}
Push-Location $repository
try {
    Start-Transcript -Path (Join-Path $evidence 'acceptance.log') | Out-Null
    $transcribing = $true
    $env:NUGET_PACKAGES = Join-Path $evidence 'packages'
    $env:NUGET_HTTP_CACHE_PATH = Join-Path $evidence 'http-cache'
    $env:NUGET_FALLBACK_PACKAGES = $null
    $env:GODOT_BIN = $GodotPath
    $env:PAL_TEST_GODOT = $GodotPath
    if ($env:DOTNET_ROLL_FORWARD) { throw 'Remove global DOTNET_ROLL_FORWARD before acceptance.' }

    # Existing dotnet boundary now preserves numeric outcomes, timeout and raw output.
    function Invoke-DotnetChecked {
        param(
            [string[]]$Arguments, [int]$ExpectedExit = 0,
            [ValidateRange(1, 600000)][int]$TimeoutMilliseconds = 600000,
            [ValidateRange(1, 5000)][int]$CleanupTimeoutMilliseconds = 5000
        )
        $script:sequence++
        $name = '{0:d2}-{1}' -f $script:sequence, $Arguments[0]
        $record = [ordered]@{
            command = 'dotnet'; arguments = $Arguments; expectedExit = $ExpectedExit; exitCode = $null
            log = "$name.log"; stdout = "$name.stdout.log"; stderr = "$name.stderr.log"
            startedUtc = [DateTime]::UtcNow.ToString('O'); phase = 'starting'; exitKnown = $false
            rootExited = $false; stdoutComplete = $false; stderrComplete = $false; outputComplete = $false
            timedOut = $false; cancellationRequested = $false; cleanupComplete = $false; failureKind = $null; error = $null
        }
        $commands.Add($record)
        function Write-CommandState {
            $commands | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $evidence 'commands.json')
        }
        Write-CommandState
        $info = [Diagnostics.ProcessStartInfo]::new('dotnet')
        $info.WorkingDirectory = $repository
        $info.UseShellExecute = $false
        $info.RedirectStandardOutput = $true
        $info.RedirectStandardError = $true
        foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
        Write-Host "dotnet $($Arguments -join ' ')"
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $cancel = [Threading.CancellationTokenSource]::new()
        $process = $null; $outFile = $null; $errFile = $null; $copies = @()
        $encodings = @([Text.Encoding]::UTF8; [Text.Encoding]::UTF8)
        try {
            $outFile = [IO.FileStream]::new((Join-Path $evidence $record.stdout), [IO.FileMode]::CreateNew,
                [IO.FileAccess]::Write, [IO.FileShare]::Read, 1, [IO.FileOptions]::Asynchronous)
            $errFile = [IO.FileStream]::new((Join-Path $evidence $record.stderr), [IO.FileMode]::CreateNew,
                [IO.FileAccess]::Write, [IO.FileShare]::Read, 1, [IO.FileOptions]::Asynchronous)
            $process = [Diagnostics.Process]::Start($info)
            # Preserve each original StreamReader's default; raw copies bypass its decoder.
            $encodings = @($process.StandardOutput.CurrentEncoding; $process.StandardError.CurrentEncoding)
            $record.stdoutEncoding = $encodings[0].WebName
            $record.stderrEncoding = $encodings[1].WebName
            $record.pid = $process.Id
            $record.phase = 'running'
            Write-CommandState
            $copies = @(
                $process.StandardOutput.BaseStream.CopyToAsync($outFile, 81920, $cancel.Token)
                $process.StandardError.BaseStream.CopyToAsync($errFile, 81920, $cancel.Token)
            )
            while ($true) {
                if (-not $record.rootExited -and $process.HasExited) {
                    $record.rootExited = $true; $record.exitKnown = $true; $record.exitCode = $process.ExitCode
                    $record.phase = 'root-exited'
                    Write-CommandState # Persist the actual exit before waiting for either EOF.
                }
                $record.stdoutComplete = $copies[0].Status -eq [Threading.Tasks.TaskStatus]::RanToCompletion
                $record.stderrComplete = $copies[1].Status -eq [Threading.Tasks.TaskStatus]::RanToCompletion
                $record.outputComplete = $record.stdoutComplete -and $record.stderrComplete
                if (@($copies | Where-Object { $_.IsFaulted -or $_.IsCanceled }).Count) {
                    throw 'Output capture did not complete successfully.'
                }
                if ($record.rootExited -and $record.outputComplete) { break }
                $remaining = $TimeoutMilliseconds - $clock.ElapsedMilliseconds
                if ($remaining -le 0) {
                    $record.timedOut = $true
                    $record.failureKind = if ($record.rootExited) { 'OutputDrainTimeout' } else { 'ProcessTimeout' }
                    break
                }
                if (-not $record.rootExited) { $null = $process.WaitForExit([int][Math]::Min(25, $remaining)) }
                else { [Threading.Thread]::Sleep([int][Math]::Min(10, $remaining)) }
            }
        } catch {
            if (-not $record.failureKind) { $record.failureKind = 'CaptureFailure' }
            $record.error = $_.Exception.Message
        } finally {
            # One shared failure-cleanup allowance, not a fresh timeout for every operation.
            $cleanup = [Diagnostics.Stopwatch]::StartNew()
            try {
                if ($process -and -not $record.rootExited) {
                    if (-not $process.HasExited) { $process.Kill($true) }
                    $remaining = [Math]::Max(0, $CleanupTimeoutMilliseconds - $cleanup.ElapsedMilliseconds)
                    $null = $process.WaitForExit([int]$remaining)
                    if ($process.HasExited) {
                        $record.rootExited = $true; $record.exitKnown = $true; $record.exitCode = $process.ExitCode
                        Write-CommandState
                    }
                }
                if ($record.failureKind) { $record.cancellationRequested = $true; $cancel.Cancel() }
                while (@($copies | Where-Object { -not $_.IsCompleted }).Count -and
                    $cleanup.ElapsedMilliseconds -lt $CleanupTimeoutMilliseconds) { [Threading.Thread]::Sleep(5) }
                $record.cleanupComplete = $record.rootExited -and @($copies | Where-Object { -not $_.IsCompleted }).Count -eq 0
                if ($copies.Count -eq 2) {
                    $record.stdoutCaptureStatus = [string]$copies[0].Status
                    $record.stderrCaptureStatus = [string]$copies[1].Status
                    $record.stdoutComplete = $copies[0].Status -eq [Threading.Tasks.TaskStatus]::RanToCompletion
                    $record.stderrComplete = $copies[1].Status -eq [Threading.Tasks.TaskStatus]::RanToCompletion
                    $record.outputComplete = $record.stdoutComplete -and $record.stderrComplete
                }
                $record.cleanupElapsedMs = $cleanup.Elapsed.TotalMilliseconds
            } catch {
                $record.cleanupComplete = $false
                if (-not $record.failureKind) { $record.failureKind = 'CleanupFailure' }
                $record.error = $_.Exception.Message
            } finally {
                if ($process) { $process.Dispose() }
                if ($outFile) { $outFile.Dispose() }
                if ($errFile) { $errFile.Dispose() }
                $cancel.Dispose()
            }
        }
        $parts = foreach ($index in 0..1) {
            $file = @($record.stdout, $record.stderr)[$index]
            # This overload also detects a BOM, matching StreamReader's original behavior.
            if (Test-Path (Join-Path $evidence $file)) { [IO.File]::ReadAllText((Join-Path $evidence $file), $encodings[$index]) }
            else { '' }
        }
        $text = $parts -join "`n"
        $plain = $text -replace '\x1B\[[0-?]*[ -/]*[@-~]', ''
        if (-not $record.failureKind -and (-not $record.cleanupComplete -or -not $record.outputComplete)) { $record.failureKind = 'CaptureFailure' }
        if (-not $record.failureKind -and $record.exitCode -ne $ExpectedExit) { $record.failureKind = 'UnexpectedExit' }
        if (-not $record.failureKind -and $ExpectedExit -eq 0 -and
            $plain -match '(?im)^\s*(ERROR:|SCRIPT ERROR:|Unhandled exception|.*\berror (MSB|NU|CS)\d+)') { $record.failureKind = 'LoggedError' }
        $record.phase = if ($record.failureKind) { 'failed' } else { 'completed' }
        $record.finishedUtc = [DateTime]::UtcNow.ToString('O')
        $record.elapsedMs = $clock.Elapsed.TotalMilliseconds
        "COMMAND=dotnet $($Arguments -join ' ')`n$text`nEXIT_CODE=$($record.exitCode)`nTIMED_OUT=$($record.timedOut)`nOUTPUT_COMPLETE=$($record.outputComplete)`nFAILURE=$($record.failureKind)" |
            Set-Content (Join-Path $evidence $record.log)
        Write-CommandState
        Save-ProcessSnapshot "$name-processes"
        if ($record.failureKind) {
            throw "$name $($record.failureKind): expected exit $ExpectedExit, got $($record.exitCode) (timeout=$($record.timedOut), outputComplete=$($record.outputComplete), cleanupComplete=$($record.cleanupComplete)). $($record.error)"
        }
        return $plain
    }
    function Assert-Trx([string]$Directory, [int[]]$Passed, [int[]]$Skipped, [int]$Failed = 0, [string]$FileName = '*.trx') {
        $files = @(Get-ChildItem -LiteralPath $Directory -Filter $FileName)
        if ($files.Count -ne $Passed.Count) { throw "Expected $($Passed.Count) TRX files, got $($files.Count) in $Directory" }
        $actual = @()
        foreach ($file in $files) {
            [xml]$trx = Get-Content -LiteralPath $file.FullName -Raw
            $summary = $trx.TestRun.ResultSummary
            $c = $summary.Counters
            $expectedOutcome = if ($Failed) { 'Failed' } else { 'Completed' }
            # GdUnit reports deliberate assertion failures as RunInfo/Error too.
            # Only controls may have those; session abort/crash diagnostics never qualify.
            if ($summary.outcome -ne $expectedOutcome -or
                @($summary.RunInfos.RunInfo | Where-Object {
                    $_.outcome -in @('Aborted', 'Timeout') -or
                    ($_.outcome -eq 'Error' -and $Failed -eq 0) -or
                    $_.Text -match '(?i)Aborting test run|Test Run Aborted|test run timeout|test host process crashed|Unhandled exception|WindowsStdOutHook'
                }).Count) {
                throw "Test session did not complete as expected: $($file.Name) outcome=$($summary.outcome)"
            }
            foreach ($counter in @('error', 'timeout', 'aborted', 'inconclusive', 'passedButRunAborted', 'notRunnable', 'disconnected', 'inProgress', 'pending')) {
                if ([int]$c.$counter -ne 0) { throw "Invalid test session counter ${counter}: $($file.Name)" }
            }
            $p = [int]$c.passed; $f = [int]$c.failed
            $results = @($trx.TestRun.Results.UnitTestResult)
            # xUnit's TRX counters leave notExecuted=0 for platform skips; inspect actual results too.
            $skips = @($results | Where-Object outcome -eq 'NotExecuted')
            $s = $skips.Count
            $executed = [int]$c.executed; $total = [int]$c.total
            if ($executed -le 0 -or $f -ne $Failed -or $executed -ne ($p + $f) -or $total -ne ($executed + $s) -or
                $results.Count -ne $total -or @($results | Where-Object outcome -eq 'Passed').Count -ne $p -or
                @($results | Where-Object outcome -eq 'Failed').Count -ne $f -or [int]$c.notExecuted -notin @(0, $s)) { throw "Invalid/empty test execution: $($file.Name)" }
            if ($s -and (Compare-Object @($skips.testName | Sort-Object) @(
                'Pal.Extract.Tests.ExportMapCliTests.Export_map_rejects_case_ambiguous_archives',
                'Pal.Extract.Tests.InspectCliTests.Inspect_rejects_ambiguous_case_insensitive_names'
            ))) { throw "Unexpected skipped tests: $($file.Name)" }
            $identity = Read-TestRunIdentity $file.FullName
            if (-not $runIds.Add($identity.runId)) { throw "Reused test run identity: $($file.Name)" }
            foreach ($id in $identity.executionIds) {
                if (-not $executionIds.Add($id)) { throw "Reused execution identity: $($file.Name)" }
            }
            $actual += "$p/$s"
            $checks.Add(@{ trx = $file.FullName; runId = $identity.runId; tests = $identity.names; outcome = [string]$summary.outcome; total = $total; executed = $executed; passed = $p; failed = $f; skipped = $s })
        }
        $expected = for ($i = 0; $i -lt $Passed.Count; $i++) { "$($Passed[$i])/$($Skipped[$i])" }
        if (Compare-Object @($expected | Sort-Object) @($actual | Sort-Object)) { throw "TRX counts differ: expected $expected, got $actual" }
        $checks | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $evidence 'test-results.json')
    }

    . (Join-Path $PSScriptRoot 'GameTestBatches.ps1')
    & (Join-Path $repository 'tests/Pal.Game.Tests/GameBatches.Tests.ps1')
    $gamePlan = Get-GameBatchPlan $repository
    $gameProject = @($gamePlan.projects | Where-Object name -CEQ 'game')
    if ($gameProject.Count -ne 1) { throw 'Expected exactly one Game project identity.' }
    $gamePlan | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $evidence 'game-batch-plan.json')
    function Invoke-GameRound([string]$Round, [string]$Directory) {
        $roundResults = [Collections.Generic.List[object]]::new()
        foreach ($batch in $gamePlan.batches) {
            $fileName = "game-$($batch.name).trx"
            $null = Invoke-DotnetChecked @('test', $gameProject.path, '--no-build', '--no-restore', '--filter', $batch.filter,
                '--logger', "trx;LogFileName=$fileName", '--results-directory', $Directory)
            Assert-Trx $Directory @($batch.count) @(0) -FileName $fileName
            $identity = Assert-GameBatch (Join-Path $Directory $fileName) $batch.expected
            $result = @{ round = $Round; name = $batch.name; tests = $identity.names; runId = $identity.runId; trx = Join-Path $Directory $fileName; filter = $batch.filter }
            $roundResults.Add($result)
            $completedBatches.Add($result)
            $completedBatches | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $evidence 'game-batches.json')
        }
        Assert-GameRound $roundResults.ToArray() $gamePlan $Round
    }
    & (Join-Path $repository 'tests/Pal.Game.Tests/DotnetBoundary.Tests.ps1') -EvidenceDirectory (Join-Path $evidence 'boundary-tests')
    $null = & (Join-Path $PSScriptRoot 'Get-GdUnitPackages.ps1')
    $null = Invoke-DotnetChecked @('--info')
    $null = Invoke-DotnetChecked @('restore', 'GodotPal.sln', '--no-http-cache')
    $null = Invoke-DotnetChecked @('build', 'GodotPal.sln', '--no-restore')
    # Prepare builds/imports Game. Explicit serial projects finish it before any adapter runner.
    # Keep solution restore/build, but never reintroduce an unbatched solution test here.
    $solutionResults = Join-Path $evidence 'solution'
    $projects = @($gamePlan.projects | Where-Object name -CNE 'game')
    foreach ($project in $projects) {
        $fileName = "$($project.name).trx"
        $null = Invoke-DotnetChecked @('test', $project.path, '--no-build', '--no-restore', '--logger', "trx;LogFileName=$fileName", '--results-directory', $solutionResults)
        Assert-Trx $solutionResults @($project.passed) @($project.skipped) -FileName $fileName
    }
    Invoke-GameRound 'solution' $solutionResults
    $list = Invoke-DotnetChecked @('test', $gameProject.path, '--no-build', '--no-restore', '--list-tests')
    $list | Set-Content (Join-Path $evidence 'discovery.log')
    # This pinned adapter lists short names; match every source-derived display identity, not counts alone.
    $discovered = @(Assert-GameDiscovery $list $gamePlan)
    @{ count = $discovered.Count; tests = $discovered } | ConvertTo-Json | Set-Content (Join-Path $evidence 'discovery.json')
    foreach ($run in @('game', 'game-repeat')) {
        $results = Join-Path $evidence $run
        Invoke-GameRound $run $results
    }
    foreach ($control in @('IntentionalAssertionMismatch', 'IntentionalTimeout', 'NativeReadyException')) {
        $results = Join-Path $evidence $control
        $controlCase = @($gamePlan.controls | Where-Object { $_.display.StartsWith($control, [StringComparison]::Ordinal) })
        if ($controlCase.Count -ne 1) { throw "Ambiguous control identity: $control" }
        $text = Invoke-DotnetChecked -Arguments @('test', $gameProject.path, '--no-build', '--no-restore', '--filter', "FullyQualifiedName=$($controlCase[0].name)", '--logger', 'trx', '--results-directory', $results) -ExpectedExit 1
        Assert-Trx $results @(0) @(0) -Failed 1
        $controlTrx = @(Get-ChildItem -LiteralPath $results -Filter '*.trx')[0].FullName
        $controlIdentity = Read-TestRunIdentity $controlTrx
        Assert-ExactTestNames $controlIdentity.names @($controlCase[0].name) "Control $control"
        $diagnostic = switch ($control) {
            'IntentionalAssertionMismatch' { '321.*320|320.*321' }
            'IntentionalTimeout' { 'timed out after 500ms' }
            'NativeReadyException' { 'ISSUE13_EXPECTED_READY_EXCEPTION' }
        }
        if ($text -notmatch $diagnostic -or @($controlIdentity.document.TestRun.ResultSummary.RunInfos.RunInfo | Where-Object {
            $_.outcome -eq 'Error' -and $_.Text -notmatch $diagnostic
        }).Count) { throw "Control $control failed without its intended diagnostic or had an unrelated RunInfo error." }
    }
    $trxFiles = foreach ($directory in @('solution', 'game', 'game-repeat', 'IntentionalAssertionMismatch', 'IntentionalTimeout', 'NativeReadyException')) {
        Get-ChildItem -LiteralPath (Join-Path $evidence $directory) -Filter '*.trx' | ForEach-Object FullName
    }
    if ($checks.Count -ne 16 -or $completedBatches.Count -ne 9) { throw 'Incomplete gate: expected 16 TRX and 9 Game batches.' }
    Assert-ExactTestNames @($trxFiles) @($checks.trx) 'Complete gate TRX files'
    & (Join-Path $PSScriptRoot 'Test-GameStartup.ps1') -GodotPath $GodotPath -LogDirectory (Join-Path $evidence 'godot')
    Save-ProcessSnapshot 'processes-final'
    'PASS: eight real-process dotnet-boundary prechecks; empty-cache restore/build; Core 328; CLI unit 142; CLI integration 188 + 2 existing platform skips; Prepare 52; Godot source/discovery 245; positives 242 as serial 66/79/97 in each of three complete rounds; 16 identity-verified TRX; three separately verified 1-failure controls; native prepare/profile/import/Main startup. Headless automation only; real GPU/manual desktop acceptance is separate.' |
        Set-Content (Join-Path $evidence 'PASS.txt')
    Write-Host 'PASS: automated M0/M2/M3 acceptance completed. Graphical/manual acceptance is separate.'
} finally {
    # Retain a process snapshot on failures as well as successful runs.
    try { Save-ProcessSnapshot 'processes-exit' }
    finally {
        foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key]) }
        if ($transcribing) { Stop-Transcript | Out-Null }
        Pop-Location
    }
}
