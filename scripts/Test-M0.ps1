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
Write-Host "M0/M2 evidence: $evidence"
$previous = @{}
foreach ($key in @('NUGET_PACKAGES', 'NUGET_HTTP_CACHE_PATH', 'NUGET_FALLBACK_PACKAGES', 'GODOT_BIN', 'PAL_TEST_GODOT')) {
    $previous[$key] = [Environment]::GetEnvironmentVariable($key)
}
$transcribing = $false
$commands = [Collections.Generic.List[object]]::new()
$checks = [Collections.Generic.List[object]]::new()
$sequence = 0
function Get-TestProcesses {
    @(Get-CimInstance Win32_Process | Where-Object {
        $_.Name -match '^godot.*\.exe$' -or
        ($_.Name -match '^(dotnet|testhost|vstest.*)\.exe$' -and $_.CommandLine -match '(godot-pal|Pal\.Game|Pal\.Prepare)')
    } |
        Select-Object ProcessId, ParentProcessId, Name, CommandLine, CreationDate)
}
$baseline = @(Get-TestProcesses)
function Save-ProcessSnapshot([string]$Name) {
    $processes = @(Get-TestProcesses)
    @{ utc = [DateTime]::UtcNow; query = 'Win32_Process: all Godot and repository-related dotnet/testhost/vstest'; baselineIds = @($baseline.ProcessId); processes = $processes; count = $processes.Count } |
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
        param([string[]]$Arguments, [int]$ExpectedExit = 0)
        $script:sequence++
        $name = '{0:d2}-{1}' -f $script:sequence, $Arguments[0]
        $info = [Diagnostics.ProcessStartInfo]::new('dotnet')
        $info.WorkingDirectory = $repository
        $info.UseShellExecute = $false
        $info.RedirectStandardOutput = $true
        $info.RedirectStandardError = $true
        foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
        Write-Host "dotnet $($Arguments -join ' ')"
        $process = [Diagnostics.Process]::Start($info)
        try {
            $stdout = $process.StandardOutput.ReadToEndAsync()
            $stderr = $process.StandardError.ReadToEndAsync()
            $timedOut = -not $process.WaitForExit(600000)
            if ($timedOut) { $process.Kill($true); $process.WaitForExit() }
            $text = $stdout.GetAwaiter().GetResult() + "`n" + $stderr.GetAwaiter().GetResult()
            $exitCode = $process.ExitCode
        } finally { $process.Dispose() }
        "COMMAND=dotnet $($Arguments -join ' ')`n$text`nEXIT_CODE=$exitCode`nTIMED_OUT=$timedOut" |
            Set-Content (Join-Path $evidence "$name.log")
        $commands.Add(@{ command = 'dotnet'; arguments = $Arguments; exitCode = $exitCode; expectedExit = $ExpectedExit; timedOut = $timedOut; log = "$name.log" })
        $commands | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $evidence 'commands.json')
        Save-ProcessSnapshot "$name-processes"
        if ($timedOut -or $exitCode -ne $ExpectedExit) { throw "$name expected exit $ExpectedExit, got $exitCode (timeout=$timedOut)." }
        $plain = $text -replace '\x1B\[[0-?]*[ -/]*[@-~]', ''
        if ($ExpectedExit -eq 0 -and $plain -match '(?im)^\s*(ERROR:|SCRIPT ERROR:|Unhandled exception|.*\berror (MSB|NU|CS)\d+)') { throw "$name logged an error." }
        return $plain
    }
    function Assert-Trx([string]$Directory, [int[]]$Passed, [int[]]$Skipped, [int]$Failed = 0) {
        $files = @(Get-ChildItem -LiteralPath $Directory -Filter '*.trx')
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
            $actual += "$p/$s"
            $checks.Add(@{ trx = $file.FullName; outcome = [string]$summary.outcome; total = $total; executed = $executed; passed = $p; failed = $f; skipped = $s })
        }
        $expected = for ($i = 0; $i -lt $Passed.Count; $i++) { "$($Passed[$i])/$($Skipped[$i])" }
        if (Compare-Object @($expected | Sort-Object) @($actual | Sort-Object)) { throw "TRX counts differ: expected $expected, got $actual" }
        $checks | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $evidence 'test-results.json')
    }

    $null = Invoke-DotnetChecked @('--info')
    $null = Invoke-DotnetChecked @('restore', 'GodotPal.sln', '--no-http-cache')
    $null = Invoke-DotnetChecked @('build', 'GodotPal.sln', '--no-restore')
    # The solution dependency orders Prepare before Game even for ordinary dotnet test.
    # Prepare launches Game builds/imports; it must finish before the adapter generates its runner.
    $solutionResults = Join-Path $evidence 'solution'
    $null = Invoke-DotnetChecked @('test', 'GodotPal.sln', '--no-build', '--no-restore', '--logger', 'trx', '--results-directory', $solutionResults)
    Assert-Trx $solutionResults @(129, 300, 52, 170) @(0, 2, 0, 0)
    $list = Invoke-DotnetChecked @('test', 'src/Pal.Game', '--no-build', '--no-restore', '--list-tests')
    $list | Set-Content (Join-Path $evidence 'discovery.log')
    # This adapter's list-tests output uses short display names, including :index parameter cases.
    $discovered = @($list -split "`n" | Where-Object { $_ -match '^    [A-Za-z][A-Za-z0-9_]*(?::\d+ \(.*\))?\s*$' } | ForEach-Object { $_.Trim() })
    $controlNames = @('IntentionalAssertionMismatchPropagates', 'IntentionalTimeoutPropagates', 'NativeReadyExceptionIsNotSwallowed')
    if ($discovered.Count -ne 173 -or @($discovered | Select-Object -Unique).Count -ne 173 -or
        @($controlNames | Where-Object { $_ -in $discovered }).Count -ne 3) { throw "Expected 173 unique tests including 3 controls, got $($discovered.Count)." }
    @{ count = $discovered.Count; tests = $discovered } | ConvertTo-Json | Set-Content (Join-Path $evidence 'discovery.json')
    foreach ($run in @('game', 'game-repeat')) {
        $results = Join-Path $evidence $run
        $null = Invoke-DotnetChecked @('test', 'src/Pal.Game', '--no-build', '--no-restore', '--logger', 'trx', '--results-directory', $results)
        Assert-Trx $results @(170) @(0)
    }
    foreach ($control in @('IntentionalAssertionMismatch', 'IntentionalTimeout', 'NativeReadyException')) {
        $results = Join-Path $evidence $control
        $text = Invoke-DotnetChecked -Arguments @('test', 'src/Pal.Game', '--no-build', '--no-restore', '--filter', $control, '--logger', 'trx', '--results-directory', $results) -ExpectedExit 1
        Assert-Trx $results @(0) @(0) -Failed 1
        $diagnostic = switch ($control) {
            'IntentionalAssertionMismatch' { '321.*320|320.*321' }
            'IntentionalTimeout' { 'timed out after 500ms' }
            'NativeReadyException' { 'ISSUE13_EXPECTED_READY_EXCEPTION' }
        }
        if ($text -notmatch $diagnostic) { throw "Control $control failed without its intended diagnostic." }
    }
    & (Join-Path $PSScriptRoot 'Test-GameStartup.ps1') -GodotPath $GodotPath -LogDirectory (Join-Path $evidence 'godot')
    Save-ProcessSnapshot 'processes-final'
    'PASS: empty-cache restore/build; Core 129; CLI 300 + 2 existing platform skips; Prepare 52; Godot discovery 173 / positives 170 repeated; three separately verified 1-failure controls; native prepare/profile/import/Main startup. Headless automation only; real GPU/manual desktop acceptance is separate.' |
        Set-Content (Join-Path $evidence 'PASS.txt')
    Write-Host 'PASS: automated M0/M2 acceptance completed. Graphical/manual acceptance is separate.'
} finally {
    # Retain a process snapshot on failures as well as successful runs.
    try { Save-ProcessSnapshot 'processes-exit' }
    finally {
        foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key]) }
        if ($transcribing) { Stop-Transcript | Out-Null }
        Pop-Location
    }
}
