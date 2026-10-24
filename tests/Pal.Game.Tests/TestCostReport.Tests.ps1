# Offline public-command regression. All timings, commands and TRX below are artificial literals.
[CmdletBinding()]
param([string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
$reportScript = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../scripts/Get-TestCostReport.ps1'))
$root = if ($EvidenceDirectory) { [IO.Path]::GetFullPath($EvidenceDirectory) } else {
    Join-Path ([IO.Path]::GetTempPath()) ('pal-cost-report-' + [guid]::NewGuid().ToString('N'))
}
if (Test-Path -LiteralPath $root) { throw 'Use a new regression evidence directory.' }
$null = New-Item -ItemType Directory $root
$fixture = Join-Path $root '人工 inputs with spaces'
$null = New-Item -ItemType Directory $fixture
$results = [Collections.Generic.List[string]]::new()
function Assert([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Case([string]$Name, [scriptblock]$Action) { & $Action; $results.Add($Name); Write-Host "PASS: $Name" }
function Write-Json([string]$Name, $Value) {
    [IO.File]::WriteAllText((Join-Path $fixture $Name), (ConvertTo-Json -InputObject $Value -Depth 30), [Text.UTF8Encoding]::new($false))
}
function Invoke-Report([string]$Name = 'input.json', [string]$Baseline = '', [switch]$RawStreams) {
    $tokens = $null; $errors = $null
    $null = [Management.Automation.Language.Parser]::ParseFile($reportScript, [ref]$tokens, [ref]$errors)
    Assert (-not $errors.Count) 'Report script must parse before execution.'
    $script:invocations++
    $out = Join-Path $root "report-$script:invocations.json"
    $err = Join-Path $root "report-$script:invocations.stderr.log"
    $arguments = @('-NoProfile', '-File', $reportScript, '-InputPath', (Join-Path $fixture $Name))
    if ($Baseline) { $arguments += @('-BaselinePath', $Baseline) }
    $before = @(Get-ChildItem -LiteralPath $fixture -File | ForEach-Object { "$($_.Name)|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)|$((Get-FileHash -LiteralPath $_.FullName).Hash)" })
    Push-Location ([IO.Path]::GetTempPath())
    $previousEncoding = [Console]::OutputEncoding
    try {
        if ($RawStreams) {
            $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $PSHOME 'pwsh.exe'))
            $start.UseShellExecute = $false; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
            foreach ($argument in $arguments) { $start.ArgumentList.Add($argument) }
            $process = [Diagnostics.Process]::new(); $process.StartInfo = $start
            $stdout = [IO.File]::Create($out); $stderr = [IO.File]::Create($err)
            try {
                Assert ($process.Start()) 'Report process did not start.'
                $copyOut = $process.StandardOutput.BaseStream.CopyToAsync($stdout)
                $copyErr = $process.StandardError.BaseStream.CopyToAsync($stderr)
                $process.WaitForExit()
                [void]$copyOut.GetAwaiter().GetResult(); [void]$copyErr.GetAwaiter().GetResult()
                $code = $process.ExitCode
            } finally { $stdout.Dispose(); $stderr.Dispose(); $process.Dispose() }
        } else {
            # PowerShell native stderr decoding uses the caller's console encoding.
            [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
            & (Join-Path $PSHOME 'pwsh.exe') @arguments 1> $out 2> $err
            $code = $LASTEXITCODE
        }
    } finally { [Console]::OutputEncoding = $previousEncoding; Pop-Location }
    [IO.File]::WriteAllText((Join-Path $root "report-$script:invocations.exit"), [string]$code, [Text.UTF8Encoding]::new($false))
    $after = @(Get-ChildItem -LiteralPath $fixture -File | ForEach-Object { "$($_.Name)|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)|$((Get-FileHash -LiteralPath $_.FullName).Hash)" })
    Assert (-not (Compare-Object $before $after)) 'Report changed input bytes, timestamps or file set.'
    $utf8 = [Text.UTF8Encoding]::new($false, $true)
    $text = [IO.File]::ReadAllText($out, $utf8)
    [pscustomobject]@{ exit = $code; output = $out; error = [IO.File]::ReadAllText($err, $utf8); report = if ($text.Trim()) { $text | ConvertFrom-Json -Depth 40 } else { $null } }
}
$script:invocations = 0
$command = [ordered]@{
    command = 'dotnet'; arguments = @('test', 'tests/Pal.Extract.Tests/Pal.Extract.Tests.csproj', '--no-build', '--no-restore')
    expectedExit = 0; exitCode = 0; exitKnown = $true; phase = 'completed'; rootExited = $true
    stdoutComplete = $true; stderrComplete = $true; outputComplete = $true; cleanupComplete = $true
    timedOut = $false; cancellationRequested = $false; failureKind = $null
    startedUtc = '2026-01-01T00:00:00Z'; finishedUtc = '2026-01-01T00:00:10Z'; elapsedMs = 10000
    log = '01-test.log'
}
$inputRecord = [ordered]@{
    schemaVersion = 1; runId = '人工轮次一'; evidenceKind = 'synthetic'
    context = @{ codeIdentity = 'fixture-code-a'; machine = 'fixture-machine'; toolchain = 'fixture-tools'; scope = 'CLI mixed'; cache = 'warm'; build = 'no-build after explicit build' }
    task = @{ completion = 'completed'; exitCode = 0; wallClockMs = 12000 }
    commandsPath = 'commands.json'
    selections = @(@{ log = '01-test.log'; category = 'test'; scope = 'CLI mixed'; round = 'solution'; batch = 'cli'; trx = @(@{ path = 'mixed.trx'; runId = 'run-a' }) })
}
$valid = @'
<TestRun id="run-a" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
<Times start="2026-01-01T00:00:01Z" finish="2026-01-01T00:00:09Z"/>
<Results>
<UnitTestResult testId="a" executionId="e-a" testName="重复显示中文" outcome="Passed" duration="00:00:02"/>
<UnitTestResult testId="b" executionId="e-b" testName="重复显示中文" outcome="Passed" duration="00:00:03"/>
</Results>
<TestDefinitions>
<UnitTest id="a" name="重复显示中文" storage="Pal.Extract.Tests.dll"><Execution id="e-a"/><TestMethod className="Fixture.Mixed" name="Alpha"/></UnitTest>
<UnitTest id="b" name="重复显示中文" storage="Pal.Extract.Tests.dll"><Execution id="e-b"/><TestMethod className="Fixture.Mixed" name="Beta"/></UnitTest>
</TestDefinitions>
<TestEntries><TestEntry testId="a" executionId="e-a"/><TestEntry testId="b" executionId="e-b"/></TestEntries>
<ResultSummary outcome="Completed"><Counters total="2" executed="2" passed="2" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" inProgress="0" pending="0"/></ResultSummary>
</TestRun>
'@
function Reset-Fixture {
    Write-Json 'commands.json' @($command)
    Write-Json 'input.json' $inputRecord
    [IO.File]::WriteAllText((Join-Path $fixture 'mixed.trx'), $valid, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $fixture '01-test.log'), '原日志：正常，EXIT_CODE=0', [Text.UTF8Encoding]::new($false))
}
$completed = $false
try {
    Reset-Fixture
    Case 'wall clock and case duration remain separate; duplicate display IDs and UTF8 survive arbitrary cwd' {
        $run = Invoke-Report
        Assert ($run.exit -eq 0) "Tool exit $($run.exit): $($run.error)"
        Assert ($run.report.taskResult.status -eq 'success') 'Complete fixture should report observed task success.'
        Assert ($run.report.taskResult.wallClockMs -eq 12000 -and $run.report.commands[0].wallClockMs -eq 10000) 'Wall clock changed.'
        Assert ($run.report.caseDurationMs -eq 5000 -and $run.report.slowestCases[0].durationMs -eq 3000) 'Independent duration literals differ.'
        Assert ($run.report.cases.Count -eq 2 -and $run.report.cases[0].displayName -ceq '重复显示中文') 'Coincident displays conflated or encoding corrupted.'
        Assert ($run.report.batches[0].round -eq 'solution' -and $run.report.batches[0].wallClockMs -eq 10000) 'Batch identity/cost lost.'
        Assert ($run.report.comparison.status -eq 'not-requested') 'Unexpected comparison.'
    }
    Case 'baseline original failureKind cannot be hidden by success text; null and empty remain comparable' {
        Reset-Fixture
        $baseline = Invoke-Report
        foreach ($marker in @($null, '', 'DiagnosticError')) {
            $old = Get-Content -LiteralPath $baseline.output -Raw | ConvertFrom-Json -AsHashtable -Depth 40
            $old.commands[0].originalRecord.failureKind = $marker
            Write-Json 'failurekind-baseline.json' $old
            $run = Invoke-Report 'input.json' (Join-Path $fixture 'failurekind-baseline.json')
            Assert ($run.exit -eq 0) 'Failure marker comparison must remain a diagnostic report.'
            if ($marker) {
                Assert ($run.report.comparison.status -eq 'not-comparable' -and $null -eq $run.report.comparison.taskWallClockDeltaMs -and $null -eq $run.report.comparison.caseDurationDeltaMs) 'Original baseline failure marker was hidden by success text.'
                Assert ($run.report.comparison.reasons -contains 'Command completion evidence must be typed and complete on both sides.') 'Failure marker did not reach the command comparison guard.'
            } else {
                Assert ($run.report.comparison.status -eq 'comparable' -and $run.report.comparison.taskWallClockDeltaMs -eq 0 -and $run.report.comparison.caseDurationDeltaMs -eq 0) 'Null/empty failure marker rejected a healthy baseline.'
            }
            Assert ($run.report.taskResult.status -eq 'success' -and $run.report.taskResult.wallClockMs -eq 12000 -and $run.report.caseDurationMs -eq 5000 -and $run.report.knownCaseDurationMs -eq 5000) 'Baseline marker changed current known costs/status.'
            Assert ($run.report.commands[0].exitCode -eq 0 -and $run.report.commands[0].originalRecord.exitCode -eq 0 -and $null -eq $run.report.commands[0].originalRecord.failureKind) 'Baseline marker changed current original evidence.'
        }
    }
    Case 'completion evidence is boolean and scalar; old baseline completion fields cannot coerce into success' {
        Reset-Fixture
        $baseline = Invoke-Report
        $positive = Invoke-Report 'input.json' $baseline.output
        Assert ($positive.exit -eq 0 -and $positive.report.comparison.status -eq 'comparable') 'Typed normal flags and scalar completion must remain comparable.'
        $flagValues = @(@{ value = 1 }, @{ value = 'true' }, @{ value = @($true) }, @{ value = @{ yes = $true } }, @{ value = $null })
        foreach ($key in @('rootExited', 'stdoutComplete', 'stderrComplete', 'outputComplete', 'cleanupComplete')) {
            foreach ($bad in $flagValues) {
                Reset-Fixture
                $record = $command | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
                $record[$key] = $bad.value
                Write-Json 'commands.json' @($record)
                $run = Invoke-Report 'input.json' $baseline.output
                Assert ($run.exit -eq 0 -and $run.report.commands[0].status -eq 'incomplete' -and $run.report.taskResult.status -eq 'incomplete' -and $run.report.comparison.status -eq 'not-comparable') "Mistyped $key completed a command."
                Assert ($run.report.commands[0].exitCode -eq 0 -and $run.report.commands[0].wallClockMs -eq 10000 -and $run.report.knownCaseDurationMs -eq 5000) "Mistyped $key erased known original costs or exit."
            }
        }
        foreach ($key in @('exitKnown', 'cancellationRequested', 'timedOut')) {
            foreach ($bad in @(@{ value = if ($key -eq 'exitKnown') { 1 } else { 0 } }, @{ value = @($false) }, @{ value = 'false' })) {
                Reset-Fixture
                $record = $command | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
                $record[$key] = $bad.value
                Write-Json 'commands.json' @($record)
                $run = Invoke-Report 'input.json' $baseline.output
                Assert ($run.exit -eq 0 -and $run.report.taskResult.status -eq 'incomplete' -and $run.report.comparison.status -eq 'not-comparable') "Mistyped $key became complete evidence."
                Assert ($run.report.commands[0].originalRecord.exitCode -eq 0 -and $run.report.knownCaseDurationMs -eq 5000) "Mistyped $key erased original evidence."
            }
        }
        foreach ($target in @('phase', 'task.completion')) {
            foreach ($bad in @(@{ value = @('completed') }, @{ value = $true }, @{ value = 1 }, @{ value = @{ state = 'completed' } }, @{ value = $null })) {
                Reset-Fixture
                $input = $inputRecord | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
                if ($target -eq 'phase') {
                    $record = $command | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
                    $record.phase = $bad.value; Write-Json 'commands.json' @($record)
                } else { $input.task.completion = $bad.value }
                Write-Json 'typed-completion.json' $input
                $run = Invoke-Report 'typed-completion.json' $baseline.output
                Assert ($run.exit -eq 0 -and $run.report.taskResult.status -eq 'incomplete' -and $run.report.comparison.status -eq 'not-comparable') "Mistyped $target became scalar completed evidence."
                Assert ($run.report.commands[0].exitCode -eq 0 -and $run.report.knownCaseDurationMs -eq 5000) "Mistyped $target erased known evidence."
            }
        }
        Reset-Fixture
        $knownBaseline = [IO.File]::ReadAllText($baseline.output, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json -AsHashtable -Depth 40
        foreach ($shape in @('status-array', 'status-bool', 'completion-array', 'completion-bool', 'taskExit-bool', 'commandExit-bool', 'originalExit-bool', 'root-number', 'phase-array', 'reportStatus-array', 'reportStatus-bool', 'version-array', 'version-bool', 'toolExit-bool')) {
            $old = $knownBaseline | ConvertTo-Json -Depth 40 | ConvertFrom-Json -AsHashtable -Depth 40
            switch ($shape) {
                'status-array' { $old.taskResult.status = @('success') }
                'status-bool' { $old.taskResult.status = $true }
                'completion-array' { $old.taskResult.completion = @('completed') }
                'completion-bool' { $old.taskResult.completion = $true }
                'taskExit-bool' { $old.taskResult.exitCode = $false }
                'commandExit-bool' { $old.commands[0].exitCode = $false }
                'originalExit-bool' { $old.commands[0].originalRecord.exitCode = $false }
                'root-number' { $old.commands[0].originalRecord.rootExited = 1 }
                'phase-array' { $old.commands[0].originalRecord.phase = @('completed') }
                'reportStatus-array' { $old.reportStatus = @('generated') }
                'reportStatus-bool' { $old.reportStatus = $true }
                'version-array' { $old.schemaVersion = @(1) }
                'version-bool' { $old.schemaVersion = $true }
                'toolExit-bool' { $old.toolExitCode = $false }
            }
            Write-Json 'typed-old-baseline.json' $old
            $run = Invoke-Report 'input.json' (Join-Path $fixture 'typed-old-baseline.json')
            if ($shape -like 'reportStatus-*' -or $shape -like 'version-*' -or $shape -eq 'toolExit-bool') {
                Assert ($run.exit -eq 2 -and $null -eq $run.report) "Malformed baseline boundary $shape masqueraded as generated v1."
            } else {
                Assert ($run.exit -eq 0 -and $run.report.comparison.status -eq 'not-comparable' -and $run.report.caseDurationMs -eq 5000 -and $run.report.commands[0].exitCode -eq 0) "Malformed old completion $shape established comparability or erased current cost."
            }
        }
        foreach ($shape in @('version-bool', 'version-array', 'runId-bool', 'runId-array', 'kind-array', 'commandsPath-array')) {
            $input = $inputRecord | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
            switch ($shape) {
                'version-bool' { $input.schemaVersion = $true }
                'version-array' { $input.schemaVersion = @(1) }
                'runId-bool' { $input.runId = $true }
                'runId-array' { $input.runId = @('run-a') }
                'kind-array' { $input.evidenceKind = @('synthetic') }
                'commandsPath-array' { $input.commandsPath = @('commands.json') }
            }
            Write-Json 'typed-input.json' $input
            $run = Invoke-Report 'typed-input.json'
            Assert ($run.exit -eq 2 -and $null -eq $run.report) "Malformed input boundary $shape was accepted."
        }
    }
    Case 'comparison costs are finite nonnegative numeric scalars on both sides; actual zero stays known' {
        Reset-Fixture
        $baseline = Invoke-Report
        $knownBaseline = [IO.File]::ReadAllText($baseline.output, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json -AsHashtable -Depth 40
        $badCosts = @(
            @{ value = $false }, @{ value = $true }, @{ value = @(0) }, @{ value = @{ number = 0 } }, @{ value = '0' }, @{ value = $null }, @{ value = -1 },
            @{ token = 'NaN' }, @{ token = 'Infinity' }, @{ token = '-Infinity' }, @{ token = '1e400' }
        )
        foreach ($target in @('baseline-wall', 'baseline-duration', 'current-wall', 'current-command-wall')) {
            foreach ($bad in $badCosts) {
                Reset-Fixture
                $input = $inputRecord | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
                $record = $command | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
                $old = $knownBaseline | ConvertTo-Json -Depth 40 | ConvertFrom-Json -AsHashtable -Depth 40
                $value = $bad.value
                if ($bad.ContainsKey('token')) { $value = '__cost_value__' }
                switch ($target) {
                    'baseline-wall' { $old.taskResult.wallClockMs = $value }
                    'baseline-duration' { $old.caseDurationMs = $value }
                    'current-wall' { $input.task.wallClockMs = $value }
                    'current-command-wall' { $record.elapsedMs = $value }
                }
                Write-Json 'typed-cost.json' $input
                Write-Json 'typed-cost-baseline.json' $old
                Write-Json 'commands.json' @($record)
                if ($bad.ContainsKey('token')) {
                    $file = switch ($target) { 'current-wall' { 'typed-cost.json' }; 'current-command-wall' { 'commands.json' }; default { 'typed-cost-baseline.json' } }
                    $path = Join-Path $fixture $file
                    [IO.File]::WriteAllText($path, [IO.File]::ReadAllText($path).Replace('"__cost_value__"', $bad.token), [Text.UTF8Encoding]::new($false))
                }
                $run = Invoke-Report 'typed-cost.json' (Join-Path $fixture 'typed-cost-baseline.json')
                Assert ($run.exit -eq 0 -and $run.report.comparison.status -eq 'not-comparable' -and $null -eq $run.report.comparison.taskWallClockDeltaMs -and $null -eq $run.report.comparison.caseDurationDeltaMs) "Invalid $target became a numeric comparison operand."
                Assert ($run.report.commands[0].exitCode -eq 0 -and $run.report.knownCaseDurationMs -eq 5000) "Invalid $target erased original exit or observed duration."
                if ($target -like 'baseline-*') { Assert ($run.report.taskResult.wallClockMs -eq 12000 -and $run.report.caseDurationMs -eq 5000) 'Invalid baseline cost erased current real costs.' }
            }
        }
        foreach ($duration in @('NaN', 'Infinity', '-00:00:01')) {
            Reset-Fixture
            [IO.File]::WriteAllText((Join-Path $fixture 'mixed.trx'), $valid.Replace('00:00:02', $duration), [Text.UTF8Encoding]::new($false))
            $run = Invoke-Report 'input.json' $baseline.output
            Assert ($run.exit -eq 0 -and $null -eq $run.report.caseDurationMs -and $run.report.knownCaseDurationMs -eq 3000 -and $run.report.comparison.status -eq 'not-comparable') 'Invalid current TRX duration became a finite complete cost.'
        }
        Reset-Fixture
        $zero = $inputRecord | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
        $zero.task.wallClockMs = 0
        $zeroCommand = $command | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
        $zeroCommand.elapsedMs = 0
        Write-Json 'zero.json' $zero
        Write-Json 'commands.json' @($zeroCommand)
        [IO.File]::WriteAllText((Join-Path $fixture 'mixed.trx'), $valid.Replace('00:00:02', '00:00:00').Replace('00:00:03', '00:00:00'), [Text.UTF8Encoding]::new($false))
        $zeroBaseline = Invoke-Report 'zero.json'
        $run = Invoke-Report 'zero.json' $zeroBaseline.output
        Assert ($run.exit -eq 0 -and $run.report.comparison.status -eq 'comparable' -and $run.report.taskResult.wallClockMs -eq 0 -and $run.report.caseDurationMs -eq 0 -and $run.report.commands[0].wallClockMs -eq 0 -and $run.report.comparison.taskWallClockDeltaMs -eq 0 -and $run.report.comparison.caseDurationDeltaMs -eq 0) 'Legitimate numeric zeros became unknown.'
        Reset-Fixture
        $run = Invoke-Report 'input.json' $zeroBaseline.output
        Assert ($run.exit -eq 0 -and $run.report.comparison.status -eq 'comparable' -and $run.report.comparison.taskWallClockDeltaMs -eq 12000 -and $run.report.comparison.caseDurationDeltaMs -eq 5000) 'Independent literal deltas against legitimate numeric zero changed.'
    }
    Case 'missing planned command never turns observed partial duration into a complete total' {
        $partial = $inputRecord | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
        $partial.selections += @{ log = '02-test.log'; category = 'test'; scope = 'CLI unit'; round = 'solution'; batch = 'cli-unit'; trx = @(@{ path = 'absent.trx' }) }
        Write-Json 'partial.json' $partial
        $run = Invoke-Report 'partial.json'
        Assert ($run.exit -eq 0 -and $run.report.taskResult.status -eq 'incomplete') 'Partial task became success or tool failure.'
        Assert ($null -eq $run.report.caseDurationMs -and $run.report.knownCaseDurationMs -eq 5000) 'Missing cost must be unknown; known partial duration is 5000ms.'
        Assert (@($run.report.diagnostics -match 'Selected command missing: 02-test.log').Count -eq 1) 'Missing planned identity not explained.'
    }
    Case 'split assemblies and overlapping build/prepare/test stages preserve identities without summed wall clock' {
        Reset-Fixture
        $split = $inputRecord | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
        $split.context.scope = 'CLI split'; $split.selections[0].scope = 'CLI unit'; $split.selections[0].batch = 'unit'
        $split.selections += @{ log = '02-test.log'; category = 'test'; scope = 'CLI integration'; round = 'solution'; batch = 'integration'; trx = @(@{ path = 'integration.trx'; runId = 'run-b' }) }
        $split.selections += @{ log = '03-build.log'; category = 'build'; scope = 'solution' }
        $split.selections += @{ log = '04-prepare.log'; category = 'prepare'; scope = 'synthetic import' }
        $other = $command | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
        $other.log = '02-test.log'; $other.arguments[1] = 'tests/Pal.Extract.IntegrationTests/Pal.Extract.IntegrationTests.csproj'
        $build = $command | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
        $build.log = '03-build.log'; $build.elapsedMs = 1500; $build.arguments = @('build', 'GodotPal.sln')
        $prepare = $command | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
        $prepare.log = '04-prepare.log'; $prepare.elapsedMs = 2500; $prepare.command = 'pwsh'; $prepare.arguments = @('-File', 'scripts/Prepare-GameScene.ps1')
        Write-Json 'commands.json' @($command, $other, $build, $prepare)
        Write-Json 'split.json' $split
        foreach ($log in @('02-test.log', '03-build.log', '04-prepare.log')) { [IO.File]::WriteAllText((Join-Path $fixture $log), '人工命令日志') }
        [IO.File]::WriteAllText((Join-Path $fixture 'mixed.trx'), $valid.Replace('Pal.Extract.Tests.dll', 'Pal.Extract.UnitTests.dll'))
        [IO.File]::WriteAllText((Join-Path $fixture 'integration.trx'), $valid.Replace('run-a', 'run-b').Replace('e-a', 'e-c').Replace('e-b', 'e-d').Replace('testId="a"', 'testId="c"').Replace('testId="b"', 'testId="d"').Replace('id="a"', 'id="c"').Replace('id="b"', 'id="d"').Replace('Pal.Extract.Tests.dll', 'Pal.Extract.IntegrationTests.dll'))
        $run = Invoke-Report 'split.json'
        Assert ($run.exit -eq 0 -and $run.report.taskResult.status -eq 'success') 'Split structure rejected.'
        Assert ($run.report.cases.Count -eq 4 -and @($run.report.cases.assembly | Select-Object -Unique).Count -eq 2 -and $run.report.caseDurationMs -eq 10000) 'Assembly/case identity conflated.'
        Assert ($run.report.taskResult.wallClockMs -eq 12000 -and $run.report.commands[2].category -eq 'build' -and $run.report.commands[2].wallClockMs -eq 1500 -and $run.report.commands[3].wallClockMs -eq 2500) 'Build/prepare cost merged into test/total.'
        Assert ($run.report.batches[0].caseDurationMs -eq 5000 -and $run.report.batches[1].caseDurationMs -eq 5000) 'Batch duration missing or confused with wall clock.'
        Assert (-not $run.report.PSObject.Properties['summedWallClockMs']) 'Overlapping walls were summed.'
    }
    Case 'old command records retain numeric exits without inventing completion or wall clock' {
        Reset-Fixture
        Write-Json 'commands.json' @(@{ command = 'dotnet'; arguments = @('test', 'GodotPal.sln'); log = '01-test.log'; exitCode = 0; expectedExit = 0; timedOut = $false })
        $run = Invoke-Report
        Assert ($run.exit -eq 0 -and $run.report.commands[0].exitCode -eq 0 -and $null -eq $run.report.commands[0].wallClockMs -and $run.report.taskResult.status -eq 'incomplete') 'Historical known exit or unknown costs misreported.'
    }
    Case 'missing or damaged TRX, cancelled/unknown exits and session aborts remain non-success without erasing evidence' {
        $variants = @(
            @{ name = 'missing-trx'; trx = $null; status = 'incomplete'; pattern = 'TRX incomplete' },
            @{ name = 'damaged-trx'; trx = '<TestRun broken'; status = 'incomplete'; pattern = 'TRX incomplete' },
            @{ name = 'partial-trx'; trx = $valid.Replace('total="2"', 'total="3"'); status = 'incomplete'; pattern = 'TRX incomplete' },
            @{ name = 'aborted-trx'; trx = $valid.Replace('</ResultSummary>', '<RunInfos><RunInfo outcome="Error"><Text>Test Run Aborted 中文</Text></RunInfo></RunInfos></ResultSummary>'); status = 'incomplete'; pattern = 'TRX incomplete' },
            @{ name = 'cancelled'; patch = @{ cancellationRequested = $true; exitKnown = $false; exitCode = $null; phase = 'running' }; status = 'incomplete'; pattern = 'cancelled' },
            @{ name = 'unknown-exit'; patch = @{ exitKnown = $false; exitCode = $null }; status = 'incomplete'; pattern = 'exit/completion unknown' },
            @{ name = 'original-failure'; patch = @{ exitCode = 7; phase = 'failed'; failureKind = 'UnexpectedExit' }; status = 'failed'; pattern = '' }
        )
        foreach ($variant in $variants) {
            Reset-Fixture
            if ($variant.ContainsKey('trx')) {
                if ($null -eq $variant.trx) { Remove-Item -LiteralPath (Join-Path $fixture 'mixed.trx') }
                else { [IO.File]::WriteAllText((Join-Path $fixture 'mixed.trx'), $variant.trx) }
            }
            if ($variant.patch) {
                $changed = $command | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
                foreach ($key in $variant.patch.Keys) { $changed[$key] = $variant.patch[$key] }
                Write-Json 'commands.json' @($changed)
            }
            $run = Invoke-Report
            Assert ($run.exit -eq 0 -and $run.report.taskResult.status -eq $variant.status) "Wrong classification for $($variant.name)."
            if ($variant.pattern) { Assert (@($run.report.commands[0].diagnostics -match $variant.pattern).Count -gt 0) "Missing reason: $($variant.name)." }
            if ($variant.name -eq 'original-failure') {
                Assert ($run.report.commands[0].exitCode -eq 7 -and $run.report.toolExitCode -eq 0) 'Task/report exits conflated.'
            }
        }
        Reset-Fixture
        $normal = Invoke-Report
        Assert ($normal.exit -eq 0 -and $normal.report.taskResult.status -eq 'success') 'Earlier failure leaked into next report.'
    }
    Case 'failed commands retain their exits while result completeness independently controls total and batch duration' {
        Reset-Fixture
        $combined = $inputRecord | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
        $combined.selections += @{ log = '02-failed.log'; category = 'test'; scope = 'CLI integration'; round = 'solution'; batch = 'failed-batch' }
        $failed = $command | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
        $failed.log = '02-failed.log'; $failed.exitCode = 7; $failed.phase = 'failed'; $failed.failureKind = 'UnexpectedExit'; $failed.elapsedMs = 6000
        $failedTrx = $valid.Replace('run-a', 'run-failed').Replace('e-a', 'e-failed-a').Replace('e-b', 'e-failed-b').Replace('testId="a"', 'testId="failed-a"').Replace('testId="b"', 'testId="failed-b"').Replace('id="a"', 'id="failed-a"').Replace('id="b"', 'id="failed-b"').Replace('outcome="Passed" duration="00:00:03"', 'outcome="Failed" duration="00:00:03"').Replace('outcome="Completed"', 'outcome="Failed"').Replace('passed="2" failed="0"', 'passed="1" failed="1"')
        [IO.File]::WriteAllText((Join-Path $fixture '02-failed.log'), '人工原失败日志 EXIT_CODE=7', [Text.UTF8Encoding]::new($false))
        foreach ($shape in @('missing-key', 'invalid-reference', 'incomplete-trx', 'complete-failed', 'complete-expected-failure')) {
            $selection = $combined | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
            $record = $failed | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
            [IO.File]::WriteAllText((Join-Path $fixture 'failed.trx'), $failedTrx, [Text.UTF8Encoding]::new($false))
            if ($shape -ne 'missing-key') { $selection.selections[1].trx = @(@{ path = 'failed.trx'; runId = 'run-failed' }) }
            if ($shape -eq 'invalid-reference') { $selection.selections[1].trx += $null }
            if ($shape -eq 'incomplete-trx') { [IO.File]::WriteAllText((Join-Path $fixture 'failed.trx'), $failedTrx.Replace('total="2"', 'total="3"'), [Text.UTF8Encoding]::new($false)) }
            if ($shape -eq 'complete-expected-failure') { $record.expectedExit = 7; $record.phase = 'completed'; $record.failureKind = $null }
            Write-Json 'commands.json' @($command, $record)
            Write-Json 'combined.json' $selection
            $run = Invoke-Report 'combined.json'
            $expectedStatus = if ($shape -eq 'complete-expected-failure') { 'expected-failure' } else { 'failed' }
            $expectedTaskStatus = if ($shape -eq 'complete-expected-failure') { 'completed-with-expected-failures' } else { 'failed' }
            Assert ($run.exit -eq 0 -and $run.report.commands[1].status -eq $expectedStatus -and $run.report.taskResult.status -eq $expectedTaskStatus) "Failure classification lost for $shape."
            Assert ($run.report.commands[1].exitCode -eq 7 -and $run.report.commands[1].wallClockMs -eq 6000 -and $run.report.batches[1].wallClockMs -eq 6000) "Original failed exit/wall clock lost for $shape."
            Assert ($run.report.batches[0].caseDurationMs -eq 5000 -and $run.report.commands[0].trx[0].caseDurationMs -eq 5000) "Complete command cost lost for $shape."
            $known = if ($shape -eq 'missing-key') { 5000 } else { 10000 }
            Assert ($run.report.knownCaseDurationMs -eq $known) "Observed duration subtotal lost for $shape."
            if ($shape -in @('complete-failed', 'complete-expected-failure')) {
                Assert ($run.report.caseDurationMs -eq 10000 -and $run.report.batches[1].caseDurationMs -eq 5000 -and $run.report.commands[1].trx[0].status -eq 'failed') "Complete failed TRX cost erased for $shape."
            } else {
                Assert ($null -eq $run.report.caseDurationMs -and $null -eq $run.report.batches[1].caseDurationMs) "Incomplete failed results ($shape) fabricated a complete total or batch duration."
            }
        }
    }
    Case 'expected-failure accepts only exact RunInfo diagnostics from failed rows' {
        Reset-Fixture
        $expected = $inputRecord | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
        $expected.selections[0].batch = 'expected-runinfo'
        $record = $command | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
        $record.expectedExit = 7; $record.exitCode = 7; $record.arguments[1] = 'tests/Pal.Extract.Tests/negative.csproj'
        $record.log = 'expected.log'; $record.phase = 'completed'; $record.failureKind = $null
        $expected.selections[0].log = 'expected.log'; $expected.selections[0].trx = @(@{ path = 'expected.trx'; runId = 'expected-run' })
        $failedXml = $valid.Replace('run-a', 'expected-run').Replace('e-a', 'expected-e-a').Replace('e-b', 'expected-e-b').Replace('testId="a"', 'testId="expected-a"').Replace('testId="b"', 'testId="expected-b"').Replace('id="a"', 'id="expected-a"').Replace('id="b"', 'id="expected-b"').Replace('outcome="Passed" duration="00:00:03"', 'outcome="Failed" duration="00:00:03"').Replace('outcome="Completed"', 'outcome="Failed"').Replace('passed="2" failed="0"', 'passed="1" failed="1"')
        $failedXml = $failedXml.Replace('outcome="Failed" duration="00:00:03"/>', 'outcome="Failed" duration="00:00:03"><Output><ErrorInfo><Message>exact diagnostic</Message></ErrorInfo></Output></UnitTestResult>').Replace('</ResultSummary>', '<RunInfos><RunInfo outcome="Error"><Text>exact diagnostic</Text></RunInfo></RunInfos></ResultSummary>')
        Write-Json 'commands.json' @($record); Write-Json 'expected.json' $expected
        [IO.File]::WriteAllText((Join-Path $fixture 'expected.log'), 'expected', [Text.UTF8Encoding]::new($false)); [IO.File]::WriteAllText((Join-Path $fixture 'expected.trx'), $failedXml, [Text.UTF8Encoding]::new($false))
        $run = Invoke-Report 'expected.json'
        Assert ($run.exit -eq 0 -and $run.report.commands[0].status -eq 'expected-failure' -and $run.report.taskResult.status -eq 'completed-with-expected-failures') 'Exact expected failure diagnostic was rejected.'
        foreach ($bad in @('different diagnostic', '')) {
            $badXml = $failedXml.Replace('<Text>exact diagnostic</Text>', "<Text>$bad</Text>")
            [IO.File]::WriteAllText((Join-Path $fixture 'expected.trx'), $badXml, [Text.UTF8Encoding]::new($false))
            $run = Invoke-Report 'expected.json'
            Assert ($run.report.commands[0].status -eq 'incomplete' -and $run.report.taskResult.status -eq 'incomplete') "Invalid RunInfo diagnostic accepted: [$bad]."
        }
        $mutations = [ordered]@{
            caseMismatch = $failedXml.Replace('<Text>exact diagnostic</Text>', '<Text>EXACT DIAGNOSTIC</Text>')
            missingMessage = $failedXml.Replace('<Message>exact diagnostic</Message>', '<StackTrace>exact diagnostic</StackTrace>')
            aggregateOnly = $failedXml.Replace('</Message>', '</Message><StackTrace>extra</StackTrace>').Replace('<Text>exact diagnostic</Text>', '<Text>exact diagnosticextra</Text>')
            crashEvenWhenMatched = $failedXml.Replace('exact diagnostic', 'test host process crashed')
            aborted = $failedXml.Replace('RunInfo outcome="Error"', 'RunInfo outcome="Aborted"')
            timedOut = $failedXml.Replace('RunInfo outcome="Error"', 'RunInfo outcome="Timeout"')
        }
        foreach ($name in $mutations.Keys) {
            [IO.File]::WriteAllText((Join-Path $fixture 'expected.trx'), $mutations[$name], [Text.UTF8Encoding]::new($false))
            $run = Invoke-Report 'expected.json'
            Assert ($run.exit -eq 0 -and $run.report.commands[0].status -eq 'incomplete' -and $run.report.taskResult.status -eq 'incomplete') "Invalid expected-failure session accepted: $name."
        }
        [IO.File]::WriteAllText((Join-Path $fixture 'expected.trx'), $failedXml, [Text.UTF8Encoding]::new($false))
        $record.stdoutComplete = $false; Write-Json 'commands.json' @($record)
        $run = Invoke-Report 'expected.json'
        Assert ($run.report.taskResult.status -eq 'incomplete') 'A matching diagnostic hid incomplete original output.'
        $record.stdoutComplete = $true; $record.expectedExit = 0; Write-Json 'commands.json' @($record)
        $run = Invoke-Report 'expected.json'
        Assert ($run.report.commands[0].status -eq 'failed' -and $run.report.taskResult.status -eq 'failed') 'A matching diagnostic hid an unexpected original exit.'
        $record.exitCode = 0; Write-Json 'commands.json' @($record)
        $warningXml = $valid.Replace('run-a', 'expected-run').Replace('</ResultSummary>', '<RunInfos><RunInfo outcome="Warning"><Text>Ordinary nonfatal report warning</Text></RunInfo></RunInfos></ResultSummary>')
        [IO.File]::WriteAllText((Join-Path $fixture 'expected.trx'), $warningXml, [Text.UTF8Encoding]::new($false))
        $run = Invoke-Report 'expected.json'
        Assert ($run.exit -eq 0 -and $run.report.commands[0].status -eq 'success' -and $run.report.taskResult.status -eq 'success') 'Expected-failure classification rejected an ordinary nonfatal warning.'
    }
    Case 'missing streams or result mappings and contradictory command completion cannot produce success' {
        Reset-Fixture
        $capture = $command | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
        $capture.stdout = 'missing.stdout.log'; $capture.stderr = 'missing.stderr.log'
        Write-Json 'commands.json' @($capture)
        $run = Invoke-Report
        Assert ($run.report.taskResult.status -eq 'incomplete') 'Missing original stream evidence became success.'
        $capture.Remove('stdout'); $capture.Remove('stderr'); $capture.stdoutComplete = $false
        Write-Json 'commands.json' @($capture)
        $run = Invoke-Report
        Assert ($run.report.taskResult.status -eq 'incomplete') 'Contradictory outputComplete flag hid missing EOF.'
        Reset-Fixture
        $unmapped = $inputRecord | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
        $unmapped.selections[0].category = 'prepare'; $unmapped.selections[0].trx = @()
        Write-Json 'unmapped.json' $unmapped
        $run = Invoke-Report 'unmapped.json'
        Assert ($run.report.taskResult.status -eq 'incomplete') 'Test command without TRX bypassed completeness by category rename.'
        Reset-Fixture
        $nullResult = $inputRecord | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
        $nullResult.selections[0].trx = @($null)
        Write-Json 'null-result.json' $nullResult
        $run = Invoke-Report 'null-result.json'
        Assert ($run.report.taskResult.status -eq 'incomplete') 'Null result slot counted as a real TRX.'
        foreach ($shape in @('missing-key', 'null', 'empty', 'missing-path', 'blank-path')) {
            Reset-Fixture
            $missingResult = $inputRecord | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
            switch ($shape) {
                'missing-key' { $missingResult.selections[0].Remove('trx') }
                'null' { $missingResult.selections[0].trx = $null }
                'empty' { $missingResult.selections[0].trx = @() }
                'missing-path' { $missingResult.selections[0].trx = @(@{}) }
                'blank-path' { $missingResult.selections[0].trx = @(@{ path = ' ' }) }
            }
            Write-Json 'missing-result.json' $missingResult
            $run = Invoke-Report 'missing-result.json'
            Assert ($run.exit -eq 0 -and $run.report.commands[0].status -eq 'incomplete' -and $run.report.taskResult.status -eq 'incomplete') "No valid TRX ($shape) became success or lost the diagnostic report."
            Assert ($run.report.commands[0].exitCode -eq 0 -and $run.report.commands[0].wallClockMs -eq 10000 -and $null -eq $run.report.caseDurationMs) "No valid TRX ($shape) erased the original exit/wall clock or invented complete duration."
        }
        Reset-Fixture
        $invalidExit = $inputRecord | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
        $invalidExit.task.exitCode = $false
        Write-Json 'invalid-exit.json' $invalidExit
        $run = Invoke-Report 'invalid-exit.json'
        Assert ($run.report.taskResult.status -ne 'success' -and $null -eq $run.report.taskResult.exitCode) 'Boolean exit treated as numeric zero.'
    }
    Case 'unexpected zero exit cannot pass a negative control; task cancellation stays distinct from report success' {
        Reset-Fixture
        $control = $command | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
        $control.expectedExit = 1
        Write-Json 'commands.json' @($control)
        $run = Invoke-Report
        Assert ($run.exit -eq 0 -and $run.report.taskResult.status -eq 'failed' -and $run.report.commands[0].exitCode -eq 0) 'Unexpected zero exit manufactured negative-control success.'
        Reset-Fixture
        $cancelled = $inputRecord | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
        $cancelled.task.completion = 'cancelled'; $cancelled.task.exitCode = $null
        Write-Json 'cancelled.json' $cancelled
        $run = Invoke-Report 'cancelled.json'
        Assert ($run.exit -eq 0 -and $run.report.taskResult.status -eq 'cancelled' -and $null -eq $run.report.taskResult.exitCode) 'Cancellation became success or zero exit.'
    }
    Case 'TRX identity, time envelopes and explicit rounds reject reuse rather than borrowing results' {
        Reset-Fixture
        $reused = $inputRecord | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
        $reused.selections[0].trx += @{ path = 'mixed.trx'; runId = 'run-a' }
        Write-Json 'reused.json' $reused
        $run = Invoke-Report 'reused.json'
        Assert ($run.report.taskResult.status -eq 'incomplete' -and $run.report.commands[0].trx[1].diagnostics[0] -match 'path reused') 'Same TRX counted twice.'
        $reused.selections[0].trx[1].path = 'copied.trx'
        Write-Json 'reused.json' $reused
        [IO.File]::WriteAllText((Join-Path $fixture 'copied.trx'), $valid)
        $run = Invoke-Report 'reused.json'
        Assert ($run.report.taskResult.status -eq 'incomplete' -and $run.report.commands[0].trx[1].diagnostics[0] -match 'run identity reused') 'Copied run counted twice.'
        [IO.File]::WriteAllText((Join-Path $fixture 'copied.trx'), $valid.Replace('run-a', 'run-b'))
        $reused.selections[0].trx[1].runId = 'run-b'; Write-Json 'reused.json' $reused
        $run = Invoke-Report 'reused.json'
        Assert ($run.report.taskResult.status -eq 'incomplete' -and $run.report.commands[0].trx[1].diagnostics[0] -match 'execution identity reused') 'Copied executions counted twice.'
        Reset-Fixture
        [IO.File]::WriteAllText((Join-Path $fixture 'mixed.trx'), $valid.Replace('2026-01-01', '2025-12-31'))
        $run = Invoke-Report
        Assert ($run.report.taskResult.status -eq 'incomplete' -and $run.report.commands[0].trx[0].diagnostics[0] -match 'time envelope') 'Old round filled this run.'
        Reset-Fixture
        [IO.File]::WriteAllText((Join-Path $fixture 'mixed.trx'), $valid.Replace('run-a', 'different-run'))
        $run = Invoke-Report
        Assert ($run.report.taskResult.status -eq 'incomplete' -and $run.report.commands[0].trx[0].diagnostics[0] -match 'explicit reference') 'Pinned run ID ignored.'
        Reset-Fixture
        [IO.File]::WriteAllText((Join-Path $fixture 'mixed.trx'), $valid.Replace('<TestEntry testId="b" executionId="e-b"/>', ''))
        $run = Invoke-Report
        Assert ($run.report.taskResult.status -eq 'incomplete' -and $run.report.commands[0].trx[0].diagnostics[0] -match 'identity graph') 'Partial identity graph became success.'
    }
    Case 'unknown costs and identities stay null rather than current HEAD, inferred cache or zero' {
        Reset-Fixture
        $unknown = $inputRecord | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
        $unknown.Remove('context'); $unknown.task.Remove('wallClockMs')
        Write-Json 'unknown.json' $unknown
        [IO.File]::WriteAllText((Join-Path $fixture 'mixed.trx'), $valid.Replace(' duration="00:00:02"', ''))
        $run = Invoke-Report 'unknown.json'
        Assert ($run.exit -eq 0 -and $null -eq $run.report.caseDurationMs -and $run.report.knownCaseDurationMs -eq 3000) 'Unknown duration filled in.'
        Assert ($null -eq $run.report.taskResult.wallClockMs -and $null -eq $run.report.context.codeIdentity -and $null -eq $run.report.context.cache -and $null -eq $run.report.context.scope) 'Unknown task/context invented.'
    }
    Case 'comparison requires matching known environment cache build selection and execution; changes are descriptive not optimization claims' {
        Reset-Fixture
        $baseline = Invoke-Report
        $compare = $inputRecord | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
        $compare.context.codeIdentity = 'fixture-code-b'; $compare.task.wallClockMs = 14000
        Write-Json 'compare.json' $compare
        $run = Invoke-Report 'compare.json' $baseline.output
        Assert ($run.exit -eq 0 -and $run.report.comparison.status -eq 'comparable') 'Same known measurement basis not comparable.'
        Assert ($run.report.comparison.taskWallClockDeltaMs -eq 2000 -and $run.report.comparison.caseDurationDeltaMs -eq 0) 'Literal deltas not 2000/0ms.'
        foreach ($key in @('machine', 'toolchain', 'scope', 'cache', 'build', 'codeIdentity')) {
            $mismatch = $compare | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
            $mismatch.context[$key] = if ($key -eq 'codeIdentity') { $null } else { 'different' }
            Write-Json 'mismatch.json' $mismatch
            $run = Invoke-Report 'mismatch.json' $baseline.output
            Assert ($run.report.comparison.status -eq 'not-comparable' -and $null -eq $run.report.comparison.taskWallClockDeltaMs) "Context difference $key treated as optimization evidence."
        }
        $changed = $command | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
        $changed.arguments += @('--filter', 'FullyQualifiedName~Subset')
        Write-Json 'commands.json' @($changed)
        $run = Invoke-Report 'compare.json' $baseline.output
        Assert ($run.report.comparison.status -eq 'not-comparable') 'Changed filter treated as same selection.'
        Reset-Fixture
        [IO.File]::WriteAllText((Join-Path $fixture 'mixed.trx'), $valid.Replace(' storage="Pal.Extract.Tests.dll"', ''))
        $noAssemblyBaseline = Invoke-Report
        $run = Invoke-Report 'compare.json' $noAssemblyBaseline.output
        Assert ($run.report.comparison.status -eq 'not-comparable' -and $null -eq $run.report.cases[0].assembly) 'Unknown artifact identity treated as known comparable.'
        Reset-Fixture
        [IO.File]::WriteAllText((Join-Path $fixture 'mixed.trx'), $valid.Replace('name="Alpha"', 'name="Gamma"'))
        $run = Invoke-Report 'compare.json' $baseline.output
        Assert ($run.report.comparison.status -eq 'not-comparable') 'Same count changed executed cases treated as same set.'
    }
    Case 'all six comparison context fields require known strings on both current and old baseline reports' {
        Reset-Fixture
        $baseline = Invoke-Report
        $knownBaseline = [IO.File]::ReadAllText($baseline.output, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json -AsHashtable -Depth 40
        $differentCode = $inputRecord | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
        $differentCode.context.codeIdentity = 'fixture-code-b'; $differentCode.task.wallClockMs = 14000
        Write-Json 'different-code.json' $differentCode
        $positive = Invoke-Report 'different-code.json' $baseline.output
        Assert ($positive.exit -eq 0 -and $positive.report.comparison.status -eq 'comparable' -and $positive.report.comparison.taskWallClockDeltaMs -eq 2000) 'Known assembly/profile fixture must first prove comparable with different valid code identities.'
        $invalidValues = @(
            @{ name = 'boolean'; value = $true }, @{ name = 'whitespace'; value = '   ' }, @{ name = 'unknown'; value = 'unknown' },
            @{ name = 'number'; value = 123 }, @{ name = 'array'; value = @('fixture-profile') }, @{ name = 'object'; value = @{ identity = 'fixture-profile' } }
        )
        foreach ($key in @('codeIdentity', 'machine', 'toolchain', 'scope', 'cache', 'build')) {
            foreach ($invalid in $invalidValues) {
                foreach ($side in @('both', 'baseline-only', 'current-only')) {
                    $current = $inputRecord | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
                    $oldBaseline = $knownBaseline | ConvertTo-Json -Depth 40 | ConvertFrom-Json -AsHashtable -Depth 40
                    if ($side -ne 'baseline-only') { $current.context[$key] = $invalid.value }
                    if ($side -ne 'current-only') { $oldBaseline.context[$key] = $invalid.value }
                    Write-Json 'typed-context.json' $current
                    Write-Json 'old-baseline.json' $oldBaseline
                    $run = Invoke-Report 'typed-context.json' (Join-Path $fixture 'old-baseline.json')
                    $label = "$key/$($invalid.name)/$side"
                    Assert ($run.exit -eq 0 -and $run.report.comparison.status -eq 'not-comparable' -and $run.report.comparison.reasons -contains "Unknown comparison context: $key.") "Invalid context $label established a known comparison basis."
                    Assert ($null -eq $run.report.comparison.taskWallClockDeltaMs -and $null -eq $run.report.comparison.caseDurationDeltaMs) "Invalid context $label fabricated deltas."
                    Assert ($run.report.taskResult.status -eq 'success' -and $run.report.commands[0].exitCode -eq 0 -and $run.report.caseDurationMs -eq 5000 -and $run.report.taskResult.wallClockMs -eq 12000) "Invalid context $label erased known costs or original exit."
                    if ($side -ne 'baseline-only') { Assert ($null -eq $run.report.context.$key) "Invalid current context $label was not normalized to unknown." }
                }
            }
        }
    }
    Case 'only recognized dotnet test TRX evidence destinations are ignored; settings and build arguments remain literal' {
        $variants = @(
            @{ name = 'settings'; command = 'dotnet'; first = @('test', '--settings', 'C:/settings/pal;LogFileName=A.runsettings'); second = @('test', '--settings', 'C:/settings/pal;LogFileName=B.runsettings') },
            @{ name = 'msbuild'; command = 'dotnet'; first = @('test', '-p:Configuration=Debug;LogFileName=A'); second = @('test', '-p:Configuration=Debug;LogFileName=B') },
            @{ name = 'other-command-logger'; command = 'fixture-runner'; first = @('--logger', 'trx;LogFileName=A.trx'); second = @('--logger', 'trx;LogFileName=B.trx') },
            @{ name = 'build-logger'; command = 'dotnet'; first = @('build', '--logger', 'trx;LogFileName=A.trx'); second = @('build', '--logger', 'trx;LogFileName=B.trx') },
            @{ name = 'other-command-results'; command = 'fixture-runner'; first = @('--results-directory', 'C:/results/A'); second = @('--results-directory', 'C:/results/B') },
            @{ name = 'non-trx-logger'; command = 'dotnet'; first = @('test', '--logger', 'html;LogFileName=A.html'); second = @('test', '--logger', 'html;LogFileName=B.html') },
            @{ name = 'unknown-trx-format'; command = 'dotnet'; first = @('test', '--logger', 'trx;LogFileName=A.trx;UnknownProperty=keep'); second = @('test', '--logger', 'trx;LogFileName=B.trx;UnknownProperty=keep') }
        )
        foreach ($variant in $variants) {
            Reset-Fixture
            $first = $command | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
            $first.command = $variant.command; $first.arguments = $variant.first
            Write-Json 'commands.json' @($first)
            $baseline = Invoke-Report
            $second = $command | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
            $second.command = $variant.command; $second.arguments = $variant.second
            Write-Json 'commands.json' @($second)
            $run = Invoke-Report 'input.json' $baseline.output
            Assert ($baseline.exit -eq 0 -and $run.exit -eq 0 -and $run.report.taskResult.status -eq 'success' -and $run.report.caseDurationMs -eq 5000 -and $run.report.cases[0].assembly -eq 'Pal.Extract.Tests.dll') "Known complete comparison fixture failed for $($variant.name)."
            Assert ($run.report.comparison.status -eq 'not-comparable' -and $run.report.comparison.reasons -contains 'Different command selection/build/filter.') "Non-evidence argument ($($variant.name)) was incorrectly erased."
            Assert ((ConvertTo-Json -InputObject @($baseline.report.commands[0].originalRecord.arguments) -Compress) -ceq (ConvertTo-Json -InputObject @($variant.first) -Compress)) "Original baseline argv changed for $($variant.name)."
            Assert ((ConvertTo-Json -InputObject @($run.report.commands[0].originalRecord.arguments) -Compress) -ceq (ConvertTo-Json -InputObject @($variant.second) -Compress)) "Original current argv changed for $($variant.name)."
        }
        foreach ($loggerFlag in @('--logger', '-l')) {
            Reset-Fixture
            $first = $command | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
            $first.arguments += @($loggerFlag, 'trx;LogFileName=A.trx', '--results-directory', 'C:/results/A')
            Write-Json 'commands.json' @($first)
            $baseline = Invoke-Report
            $second = $command | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
            $second.arguments += @($loggerFlag, 'trx;LogFileName=B.trx', '--results-directory', 'C:/results/B')
            Write-Json 'commands.json' @($second)
            $run = Invoke-Report 'input.json' $baseline.output
            Assert ($run.exit -eq 0 -and $run.report.comparison.status -eq 'comparable' -and $run.report.comparison.taskWallClockDeltaMs -eq 0 -and $run.report.comparison.caseDurationDeltaMs -eq 0) "Recognized $loggerFlag TRX/result destinations were treated as test selection changes."
            Assert ((ConvertTo-Json -InputObject @($run.report.commands[0].originalRecord.arguments) -Compress) -ceq (ConvertTo-Json -InputObject @($second.arguments) -Compress)) 'Normalization rewrote original logger/result argv.'
        }
    }
    Case 'unknown command or argv on both sides cannot establish comparability; explicit empty argv remains known' {
        foreach ($shape in @('missing-arguments', 'null-arguments', 'scalar-arguments', 'object-arguments', 'null-argument', 'numeric-argument', 'missing-command', 'null-command', 'numeric-command', 'array-command')) {
            Reset-Fixture
            $unknownCommand = $command | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
            switch ($shape) {
                'missing-arguments' { $unknownCommand.Remove('arguments') }
                'null-arguments' { $unknownCommand.arguments = $null }
                'scalar-arguments' { $unknownCommand.arguments = 'test' }
                'object-arguments' { $unknownCommand.arguments = @{ filter = 'Subset' } }
                'null-argument' { $unknownCommand.arguments = @('test', $null) }
                'numeric-argument' { $unknownCommand.arguments = @('test', 123) }
                'missing-command' { $unknownCommand.Remove('command') }
                'null-command' { $unknownCommand.command = $null }
                'numeric-command' { $unknownCommand.command = 123 }
                'array-command' { $unknownCommand.command = @('dotnet') }
            }
            Write-Json 'commands.json' @($unknownCommand)
            $unknownBaseline = Invoke-Report
            $run = Invoke-Report 'input.json' $unknownBaseline.output
            Assert ($unknownBaseline.exit -eq 0 -and $run.exit -eq 0 -and $run.report.comparison.status -eq 'not-comparable') "Both sides $shape incorrectly established comparability."
            Assert (($run.report.comparison.reasons -join ' ') -match 'Unknown command/arguments') "Unknown command basis ($shape) not explained."
            Assert ($run.report.taskResult.status -eq 'success' -and $run.report.commands[0].exitCode -eq 0 -and $run.report.commands[0].wallClockMs -eq 10000 -and $run.report.caseDurationMs -eq 5000 -and $run.report.taskResult.wallClockMs -eq 12000) "Unknown comparison basis ($shape) erased observed cost or original exit."
            Assert ($null -eq $run.report.comparison.taskWallClockDeltaMs -and $null -eq $run.report.comparison.caseDurationDeltaMs) "Unknown comparison basis ($shape) invented deltas."
        }
        Reset-Fixture
        $emptyArgv = $command | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
        $emptyArgv.command = 'fixture-test-runner'; $emptyArgv.arguments = @()
        Write-Json 'commands.json' @($emptyArgv)
        $emptyBaseline = Invoke-Report
        $run = Invoke-Report 'input.json' $emptyBaseline.output
        Assert ($run.exit -eq 0 -and $run.report.comparison.status -eq 'comparable' -and $run.report.comparison.taskWallClockDeltaMs -eq 0 -and $run.report.comparison.caseDurationDeltaMs -eq 0) 'Known legitimate empty argv treated as unknown.'
    }
    Case 'report input failure has its own numeric exit and no success JSON; original failed exit and log remain readable' {
        Reset-Fixture
        $failed = $command | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
        $failed.exitCode = 9; $failed.phase = 'failed'; $failed.failureKind = 'UnexpectedExit'
        Write-Json 'commands.json' @($failed)
        $broken = $inputRecord | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
        $broken.commandsPath = '人工 missing commands.json'
        Write-Json 'broken.json' $broken
        $run = Invoke-Report 'broken.json'
        Assert ($run.exit -eq 2 -and $null -eq $run.report -and $run.error -match 'original task exit/logs unchanged') 'Report failure manufactured success or masked error.'
        $expectedMissingPath = Join-Path $fixture '人工 missing commands.json'
        Assert ($run.error.Contains($expectedMissingPath)) "Failure stderr lost original Chinese/spaced path: $expectedMissingPath"
        $raw = Invoke-Report 'broken.json' -RawStreams
        Assert ($raw.exit -eq 2 -and $null -eq $raw.report -and $raw.error.Contains($expectedMissingPath)) 'Raw producer stderr is not strict UTF8 with the original Chinese/spaced path.'
        Assert ((Get-Content -LiteralPath (Join-Path $fixture 'commands.json') -Raw | ConvertFrom-Json)[0].exitCode -eq 9) 'Original failed exit lost.'
        [IO.File]::WriteAllText((Join-Path $fixture 'commands.json'), '{bad JSON')
        $badJson = Invoke-Report
        Assert ($badJson.exit -eq 2 -and $null -eq $badJson.report) 'Corrupt command records produced success JSON.'
        Reset-Fixture
        Write-Json 'commands.json' @()
        $empty = Invoke-Report
        Assert ($empty.exit -eq 2 -and $null -eq $empty.report) 'No command results manufactured success JSON.'
    }
    $completed = $true
} finally {
    if (-not $EvidenceDirectory -and $completed) { Remove-Item -LiteralPath $root -Recurse -Force }
    elseif (-not $completed) { Write-Host "Regression failed; original fixtures/output retained: $root" }
}
if (-not $EvidenceDirectory) { Assert (-not (Test-Path -LiteralPath $root)) 'Successful regression did not clean its own temporary directory.' }
Write-Host "PASS: $($results.Count) offline cost report groups; report invocations=$script:invocations; no SDK/engine/process enumeration."
