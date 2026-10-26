# Shared read-only report implementation. No console output, process exit or test execution.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GameTestBatches.ps1')
function Read-CostJson([string]$Path) {
    return ,([IO.File]::ReadAllText($Path, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json -AsHashtable -Depth 60 -NoEnumerate)
}
function Resolve-CostPath([string]$Base, [string]$Path) {
    if (-not $Path) { throw 'Empty evidence path.' }
    [IO.Path]::GetFullPath($Path, $Base)
}
function Get-CostNumber($Value) {
    if ($null -eq $Value -or ([Type]::GetTypeCode($Value.GetType()) -notin @(
        [TypeCode]::Byte, [TypeCode]::SByte, [TypeCode]::Int16, [TypeCode]::UInt16, [TypeCode]::Int32, [TypeCode]::UInt32,
        [TypeCode]::Int64, [TypeCode]::UInt64, [TypeCode]::Single, [TypeCode]::Double, [TypeCode]::Decimal
    ) -and $Value -isnot [Numerics.BigInteger])) { return $null }
    try { $number = [double]$Value } catch { return $null }
    if ([double]::IsNaN($number) -or [double]::IsInfinity($number) -or $number -lt 0) { return $null }
    return $number
}
function Test-CostExit($Value) {
    return $null -ne $Value -and $Value -is [ValueType] -and $Value -isnot [bool] -and
        $Value -ge [int]::MinValue -and $Value -le [int]::MaxValue -and $Value -eq [Math]::Truncate([double]$Value)
}
function Get-CostDuration([string]$Value) {
    $duration = [TimeSpan]::Zero
    if ($Value -and [TimeSpan]::TryParse($Value, [Globalization.CultureInfo]::InvariantCulture, [ref]$duration) -and $duration.Ticks -ge 0) {
        return $duration.TotalMilliseconds
    }
    return $null
}
function Get-CostTime($Value) {
    if ($Value -is [DateTimeOffset]) { return $Value }
    if ($Value -is [DateTime] -and $Value.Kind -ne [DateTimeKind]::Unspecified) { return [DateTimeOffset]::new($Value) }
    $time = [DateTimeOffset]::MinValue
    if ($Value -match '(Z|[+-]\d\d:\d\d)$' -and [DateTimeOffset]::TryParse($Value, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$time)) { return $time }
    return $null
}
function Test-CostState($Value, [string]$Expected) {
    return $Value -is [string] -and $Value -ceq $Expected
}
function Test-CostCommandCompletion($Record) {
    if ($Record -isnot [Collections.IDictionary] -or -not (Test-CostState $Record.phase 'completed')) { return $false }
    foreach ($key in @('rootExited', 'stdoutComplete', 'stderrComplete', 'outputComplete', 'cleanupComplete')) {
        if ($Record[$key] -isnot [bool] -or -not $Record[$key]) { return $false }
    }
    foreach ($key in @('exitKnown', 'cancellationRequested', 'timedOut')) {
        if ($Record.Contains($key) -and ($Record[$key] -isnot [bool] -or $Record[$key] -ne ($key -eq 'exitKnown'))) { return $false }
    }
    return $true
}
function Test-CostKnownContextValue($Value) {
    return $Value -is [string] -and -not [string]::IsNullOrWhiteSpace($Value) -and $Value.Trim() -ine 'unknown'
}
function Get-CostSelectionSignature($Commands) {
    $rows = @(foreach ($command in $Commands) {
        $record = $command.originalRecord
        if ($record -isnot [Collections.IDictionary] -or $record.command -isnot [string] -or [string]::IsNullOrWhiteSpace($record.command) -or
            -not $record.Contains('arguments') -or $record.arguments -isnot [array] -or
            @($record.arguments | Where-Object { $_ -isnot [string] }).Count) { return $null }
        $arguments = [Collections.Generic.List[string]]::new()
        $raw = @($record.arguments)
        $dotnetTestOptions = $record.command -ceq 'dotnet' -and $raw.Count -gt 0 -and $raw[0] -ceq 'test'
        for ($index = 0; $index -lt $raw.Count; $index++) {
            if ($raw[$index] -ceq '--') { $dotnetTestOptions = $false }
            # Only supported dotnet test option/value pairs identify evidence destinations.
            if ($dotnetTestOptions -and $index -gt 0 -and $index + 1 -lt $raw.Count) {
                $value = $raw[$index + 1]
                if ($raw[$index] -ceq '--results-directory' -and -not [string]::IsNullOrWhiteSpace($value) -and -not $value.StartsWith('-')) {
                    $arguments.Add($raw[$index]); $arguments.Add('<evidence>'); $index++; continue
                }
                if ($raw[$index] -cin @('--logger', '-l') -and $value -match '(?i)^trx;LogFileName=[^;]+$') {
                    $arguments.Add($raw[$index]); $arguments.Add(($value -replace '(?i);LogFileName=[^;]+$', ';LogFileName=<evidence>')); $index++; continue
                }
            }
            $arguments.Add($raw[$index])
        }
        [ordered]@{ command = $command.originalRecord.command; arguments = @($arguments); expectedExit = $command.expectedExit
            category = $command.category; scope = $command.scope; round = $command.round; batch = $command.batch }
    })
    ConvertTo-Json -InputObject $rows -Depth 10 -Compress
}
function Get-CostExecutionSignature($Cases) {
    $rows = @(foreach ($case in $Cases) {
        ConvertTo-Json -InputObject ([ordered]@{ assembly = $case.assembly; class = $case.class; method = $case.method; displayName = $case.displayName
            testId = $case.testId; outcome = $case.outcome; round = $case.round; batch = $case.batch }) -Compress
    })
    ConvertTo-Json -InputObject @($rows | Sort-Object -CaseSensitive) -Compress
}
function Get-CostArtifact([string]$Path) {
    [ordered]@{ path = $Path; sha256 = if (Test-Path -LiteralPath $Path -PathType Leaf) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash } else { $null } }
}
function Get-TestCostReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$InputPath,
        [string]$BaselinePath
    )
    $ErrorActionPreference = 'Stop'
    $InputPath = [IO.Path]::GetFullPath($InputPath)
    $base = [IO.Path]::GetDirectoryName($InputPath)
    $inputRecord = Read-CostJson $InputPath
    if ($inputRecord -isnot [Collections.IDictionary] -or (Get-CostNumber $inputRecord.schemaVersion) -ne 1 -or
        $inputRecord.runId -isnot [string] -or [string]::IsNullOrWhiteSpace($inputRecord.runId) -or
        $inputRecord.evidenceKind -isnot [string] -or $inputRecord.evidenceKind -cnotin @('synthetic', 'historical', 'measured') -or
        $inputRecord.commandsPath -isnot [string] -or [string]::IsNullOrWhiteSpace($inputRecord.commandsPath) -or -not @($inputRecord.selections).Count) {
        throw 'Expected schemaVersion=1, runId, evidenceKind, commandsPath and nonempty selections.'
    }
    $commandsPath = Resolve-CostPath $base $inputRecord.commandsPath
    $rawCommands = Read-CostJson $commandsPath
    $rawCommands = @($rawCommands)
    if (-not $rawCommands.Count -or $null -eq $rawCommands[0]) { throw 'No command records; no success report can be generated.' }
    $selections = @($inputRecord.selections)
    $selectionByLog = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($selection in $selections) {
        if (-not $selection.log -or $selectionByLog.ContainsKey($selection.log) -or
            $selection.category -notin @('restore', 'build', 'prepare', 'test', 'discovery', 'startup', 'other')) { throw 'Invalid/duplicate command selection or category.' }
        $selectionByLog.Add($selection.log, $selection)
    }
    $commands = [Collections.Generic.List[object]]::new()
    $cases = [Collections.Generic.List[object]]::new()
    $artifacts = [Collections.Generic.List[object]]::new()
    $artifacts.Add((Get-CostArtifact $InputPath)); $artifacts.Add((Get-CostArtifact $commandsPath))
    $diagnostics = [Collections.Generic.List[string]]::new()
    $logs = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $runIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $executionIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $incompleteResultLogs = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($record in $rawCommands) {
        if (-not $record.log -or -not $logs.Add($record.log)) { throw 'Empty/duplicate command log identity.' }
        $selection = if ($selectionByLog.ContainsKey($record.log)) { $selectionByLog[$record.log] } else { @{} }
        $issues = [Collections.Generic.List[string]]::new()
        if (-not $selection.Count) { $issues.Add('Command not mapped to an explicit selection.') }
        $start = Get-CostTime $record.startedUtc; $finish = Get-CostTime $record.finishedUtc
        $wall = Get-CostNumber $record.elapsedMs
        if ($null -eq $wall) { $issues.Add('Command wall clock unknown.') }
        $exitKnown = (-not $record.Contains('exitKnown') -or ($record.exitKnown -is [bool] -and $record.exitKnown)) -and (Test-CostExit $record.exitCode)
        $status = 'incomplete'
        if (($record.cancellationRequested -is [bool] -and $record.cancellationRequested) -or ($record.timedOut -is [bool] -and $record.timedOut)) { $issues.Add('Command cancelled/timed out.') }
        elseif ($exitKnown -and ((Test-CostExit $record.expectedExit) -and $record.exitCode -ne $record.expectedExit)) { $status = 'failed'; $issues.Add('Actual exit differs from recorded expected exit.') }
        elseif ($exitKnown -and $record.exitCode -ne 0 -and -not (Test-CostExit $record.expectedExit)) { $status = 'failed' }
        elseif ((Test-CostState $record.phase 'failed') -or $record.failureKind) { $status = 'failed' }
        elseif ($exitKnown -and (Test-CostCommandCompletion $record)) {
            $status = if ($record.exitCode -eq 0) { 'success' } else { 'expected-failure' }
        } else { $issues.Add('Command exit/completion unknown or incomplete.') }
        $logPath = Resolve-CostPath ([IO.Path]::GetDirectoryName($commandsPath)) $record.log
        $artifacts.Add((Get-CostArtifact $logPath))
        if (-not (Test-Path -LiteralPath $logPath -PathType Leaf)) { $issues.Add('Original command log missing.') }
        foreach ($stream in @('stdout', 'stderr')) {
            if ($record[$stream]) {
                $streamArtifact = Get-CostArtifact (Resolve-CostPath ([IO.Path]::GetDirectoryName($commandsPath)) $record[$stream])
                $artifacts.Add($streamArtifact)
                if (-not $streamArtifact.sha256) { $issues.Add("Original $stream evidence missing.") }
            }
        }
        $trxReports = [Collections.Generic.List[object]]::new()
        $references = if ($selection.Contains('trx')) { @($selection.trx) } else { @() }
        $validReferences = @($references | Where-Object { $_ -is [Collections.IDictionary] -and $_.path -is [string] -and -not [string]::IsNullOrWhiteSpace($_.path) })
        $resultsComplete = $true
        if (($selection.category -eq 'test' -or ($record.command -eq 'dotnet' -and $record.arguments[0] -eq 'test' -and '--list-tests' -notin $record.arguments)) -and -not $validReferences.Count) {
            $issues.Add('Test command has no explicit TRX results.'); $resultsComplete = $false
        }
        foreach ($reference in $references) {
            if ($null -eq $reference) { $issues.Add('Null TRX reference is not a result.'); $resultsComplete = $false; continue }
            if ($reference -isnot [Collections.IDictionary] -or $reference.path -isnot [string] -or [string]::IsNullOrWhiteSpace($reference.path)) {
                $issues.Add('Invalid TRX reference: expected a nonempty path.'); $resultsComplete = $false; continue
            }
            $trxPath = Resolve-CostPath $base $reference.path
            $artifact = Get-CostArtifact $trxPath; $artifacts.Add($artifact)
            $trxIssues = [Collections.Generic.List[string]]::new()
            $trxStatus = 'incomplete'; $identity = $null; $rows = @(); $durationSum = $null
            try {
                if (-not $paths.Add($trxPath)) { throw 'TRX path reused across selections/rounds.' }
                $identity = Read-TestRunIdentity $trxPath
                if (-not $runIds.Add($identity.runId)) { throw 'TRX run identity reused across selections/rounds.' }
                foreach ($id in $identity.executionIds) { if (-not $executionIds.Add($id)) { throw 'TRX execution identity reused across selections/rounds.' } }
                if ($reference.runId -and $reference.runId -cne $identity.runId) { throw 'TRX run identity differs from explicit reference.' }
                $document = $identity.document; $ns = $identity.ns
                $times = $document.SelectSingleNode('/t:TestRun/t:Times', $ns)
                $trxStart = if ($times) { Get-CostTime $times.GetAttribute('start') } else { $null }
                $trxFinish = if ($times) { Get-CostTime $times.GetAttribute('finish') } else { $null }
                if (-not $start -or -not $finish -or -not $trxStart -or -not $trxFinish -or $finish -lt $start -or
                    $trxStart -lt $start -or $trxFinish -gt $finish -or $trxFinish -lt $trxStart) { $trxIssues.Add('TRX time envelope cannot bind it to this command; no borrowing old rounds.') }
                $summary = $document.SelectSingleNode('/t:TestRun/t:ResultSummary', $ns)
                $counters = if ($summary) { $summary.SelectSingleNode('t:Counters', $ns) } else { $null }
                if (-not $summary -or -not $counters) { throw 'TRX summary/counters missing.' }
                foreach ($name in @('total', 'executed', 'passed', 'failed', 'error', 'timeout', 'aborted', 'inconclusive', 'passedButRunAborted', 'notRunnable', 'notExecuted', 'disconnected', 'inProgress', 'pending')) {
                    if (-not $counters.HasAttribute($name) -or $counters.GetAttribute($name) -notmatch '^\d+$') { throw "TRX counter missing/invalid: $name." }
                    if ($name -notin @('total', 'executed', 'passed', 'failed', 'notExecuted') -and [long]$counters.GetAttribute($name) -ne 0) { $trxIssues.Add("TRX nonzero session counter: $name.") }
                }
                $rows = @($document.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult', $ns))
                $passed = @($rows | Where-Object { $_.GetAttribute('outcome') -ceq 'Passed' }).Count
                $failed = @($rows | Where-Object { $_.GetAttribute('outcome') -ceq 'Failed' }).Count
                $skipped = @($rows | Where-Object { $_.GetAttribute('outcome') -ceq 'NotExecuted' }).Count
                if ($rows.Count -ne [long]$counters.total -or $passed -ne [long]$counters.passed -or $failed -ne [long]$counters.failed -or
                    [long]$counters.executed -le 0 -or [long]$counters.executed -ne ($passed + $failed) -or $rows.Count -ne ($passed + $failed + $skipped) -or
                    [long]$counters.notExecuted -notin @(0, $skipped)) { $trxIssues.Add('TRX result/counter mismatch or zero execution.') }
                $runInfos = @($summary.SelectNodes('t:RunInfos/t:RunInfo', $ns))
                $failedRows = @($rows | Where-Object { $_.GetAttribute('outcome') -ceq 'Failed' })
                $expectedFailureDiagnostics = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                foreach ($failedRow in $failedRows) {
                    $message = $failedRow.SelectSingleNode('t:Output/t:ErrorInfo/t:Message', $ns)
                    if ($message -and -not [string]::IsNullOrWhiteSpace($message.InnerText)) { [void]$expectedFailureDiagnostics.Add($message.InnerText.Trim()) }
                }
                $unexpectedRunInfo = $false
                foreach ($runInfo in $runInfos) {
                    $texts = @($runInfo.SelectNodes('t:Text', $ns) | ForEach-Object { $_.InnerText.Trim() })
                    $knownExpectedFailure = $status -ceq 'expected-failure' -and $summary.outcome -ceq 'Failed' -and $failedRows.Count -gt 0 -and $runInfo.GetAttribute('outcome') -ceq 'Error' -and $texts.Count -eq 1 -and $expectedFailureDiagnostics.Contains($texts[0])
                    if ($runInfo.InnerText -match '(?i)Aborting test run|Test Run Aborted|test run timeout|crashed|Unhandled exception|WindowsStdOutHook' -or $runInfo.GetAttribute('outcome') -in @('Aborted', 'Timeout') -or ($runInfo.GetAttribute('outcome') -ieq 'Error' -and -not $knownExpectedFailure)) { $unexpectedRunInfo = $true }
                }
                if ($summary.outcome -notin @('Completed', 'Failed') -or $unexpectedRunInfo) { $trxIssues.Add('TRX session not completed cleanly (Summary/RunInfo).') }
                $durationSum = 0.0; $allDurationsKnown = $true
                foreach ($row in $rows) {
                    $duration = Get-CostDuration $row.GetAttribute('duration')
                    if ($null -eq $duration) { $allDurationsKnown = $false } else { $durationSum += $duration }
                    $id = $row.GetAttribute('testId')
                    $definition = @($document.SelectNodes('/t:TestRun/t:TestDefinitions/t:UnitTest', $ns) | Where-Object { $_.GetAttribute('id') -ceq $id })[0]
                    $method = $definition.SelectSingleNode('t:TestMethod', $ns)
                    $cases.Add([ordered]@{ runId = $identity.runId; testId = $id; executionId = $row.GetAttribute('executionId'); displayName = $row.GetAttribute('testName')
                        assembly = if ($definition.GetAttribute('storage')) { $definition.GetAttribute('storage') } else { $null }; class = $method.GetAttribute('className'); method = $method.GetAttribute('name')
                        outcome = $row.GetAttribute('outcome'); durationMs = $duration; trx = $trxPath; commandLog = $record.log; round = $selection.round; batch = $selection.batch })
                }
                if (-not $allDurationsKnown) { $durationSum = $null }
                if (-not $trxIssues.Count) {
                    $trxStatus = if ($failed -gt 0 -or $summary.outcome -eq 'Failed') { 'failed' } else { 'success' }
                }
            } catch { $trxIssues.Add($_.Exception.Message) }
            if ($trxStatus -eq 'failed' -and $status -eq 'success') { $status = 'failed' }
            if ($trxStatus -eq 'incomplete') { $issues.Add("TRX incomplete: $trxPath"); $resultsComplete = $false }
            $trxReports.Add([ordered]@{ artifact = $artifact; runId = if ($identity) { $identity.runId } else { $null }; status = $trxStatus
                resultCount = $rows.Count; caseDurationMs = $durationSum; diagnostics = @($trxIssues) })
        }
        if ($issues.Count -and $status -in @('success', 'expected-failure')) { $status = 'incomplete' }
        # A known failure must retain its status even when its result set is incomplete.
        if (-not $resultsComplete) { [void]$incompleteResultLogs.Add($record.log) }
        $commands.Add([ordered]@{ log = $record.log; category = $selection.category; scope = $selection.scope; round = $selection.round; batch = $selection.batch
            wallClockMs = $wall; exitCode = if ($exitKnown) { $record.exitCode } else { $null }; expectedExit = $record.expectedExit
            status = $status; originalRecord = $record; trx = @($trxReports); diagnostics = @($issues) })
    }
    foreach ($selection in $selections) { if (-not $logs.Contains($selection.log)) { $diagnostics.Add("Selected command missing: $($selection.log)") } }
    $task = if ($inputRecord.task) { $inputRecord.task } else { @{} }
    $taskExit = if (Test-CostExit $task.exitCode) { $task.exitCode } else { $null }
    $taskStatus = 'unknown'
    if (($null -ne $taskExit -and $taskExit -ne 0) -or @($commands | Where-Object { $_.status -eq 'failed' }).Count) { $taskStatus = 'failed' }
    elseif (Test-CostState $task.completion 'cancelled') { $taskStatus = 'cancelled' }
    elseif ($diagnostics.Count -or @($commands | Where-Object { $_.status -eq 'incomplete' }).Count -or -not (Test-CostState $task.completion 'completed')) { $taskStatus = 'incomplete' }
    elseif ($null -ne $taskExit -and $taskExit -eq 0) {
        $taskStatus = if (@($commands | Where-Object { $_.status -eq 'expected-failure' }).Count) { 'completed-with-expected-failures' } else { 'success' }
    }
    $batches = @($commands | Where-Object { $_.batch } | ForEach-Object {
        $batchCommand = $_
        $batchDurations = @($batchCommand.trx)
        $batchDuration = $null
        if ($batchDurations.Count -and -not $incompleteResultLogs.Contains($batchCommand.log) -and
            -not @($batchDurations | Where-Object { $null -eq $_.caseDurationMs -or $_.status -eq 'incomplete' }).Count) {
            $batchDuration = ($batchDurations | ForEach-Object { $_.caseDurationMs } | Measure-Object -Sum).Sum
        }
        [ordered]@{ round = $batchCommand.round; name = $batchCommand.batch; commandLog = $batchCommand.log; wallClockMs = $batchCommand.wallClockMs
            caseDurationMs = $batchDuration; status = $batchCommand.status }
    })
    $knownCaseDuration = $null
    if (@($cases | Where-Object { $null -ne $_.durationMs }).Count) {
        $knownCaseDuration = ($cases | ForEach-Object { $_.durationMs } | Measure-Object -Sum).Sum
    }
    $caseDuration = $null
    if ($cases.Count -and -not $diagnostics.Count -and -not $incompleteResultLogs.Count -and -not @($cases | Where-Object { $null -eq $_.durationMs }).Count -and
        -not @($commands | Where-Object { $_.status -eq 'incomplete' }).Count -and -not @($commands.trx | Where-Object { $_.status -eq 'incomplete' }).Count) {
        $caseDuration = $knownCaseDuration
    }
    $context = [ordered]@{}
    foreach ($key in @('codeIdentity', 'machine', 'toolchain', 'scope', 'cache', 'build')) {
        $context[$key] = if ($inputRecord.context -is [Collections.IDictionary] -and (Test-CostKnownContextValue $inputRecord.context[$key])) { $inputRecord.context[$key] } else { $null }
    }
    $report = [ordered]@{
        schemaVersion = 1; reportStatus = 'generated'; toolExitCode = 0; runId = $inputRecord.runId; evidenceKind = $inputRecord.evidenceKind
        context = $context; declaredTask = $task
        taskResult = @{ status = $taskStatus; exitCode = $taskExit; completion = $task.completion; wallClockMs = (Get-CostNumber $task.wallClockMs) }
        commands = @($commands); batches = $batches; cases = @($cases); caseDurationMs = $caseDuration; knownCaseDurationMs = $knownCaseDuration
        slowestCases = @($cases | Where-Object { $null -ne $_.durationMs } | Sort-Object { $_.durationMs } -Descending | Select-Object -First 10)
        slowestBatches = @($batches | Where-Object { $null -ne $_.wallClockMs } | Sort-Object { $_.wallClockMs } -Descending | Select-Object -First 10)
        artifacts = @($artifacts); diagnostics = @($diagnostics); comparison = @{ status = 'not-requested' }
        notes = @('Milliseconds throughout. Null means unknown, never zero.', 'Command/stage/batch wall clocks may overlap; no sum is a total wall clock.',
            'Task wall clock is only the explicitly supplied own task measurement.', 'This report is not gate validation or an optimization/causality claim.')
    }
    if ($BaselinePath) {
        $BaselinePath = [IO.Path]::GetFullPath($BaselinePath)
        $baseline = Read-CostJson $BaselinePath
        if ($baseline -isnot [Collections.IDictionary] -or (Get-CostNumber $baseline.schemaVersion) -ne 1 -or
            -not (Test-CostState $baseline.reportStatus 'generated') -or (Get-CostNumber $baseline.toolExitCode) -ne 0 -or
            $baseline.runId -isnot [string] -or [string]::IsNullOrWhiteSpace($baseline.runId) -or
            $baseline.evidenceKind -isnot [string] -or $baseline.evidenceKind -cnotin @('synthetic', 'historical', 'measured') -or
            $baseline.commands -isnot [array] -or -not $baseline.commands.Count) {
            throw 'Baseline must be a generated schemaVersion=1 cost report, not a task record.'
        }
        $reasons = [Collections.Generic.List[string]]::new()
        foreach ($key in @('codeIdentity', 'machine', 'toolchain', 'scope', 'cache', 'build')) {
            if (-not (Test-CostKnownContextValue $context[$key]) -or $baseline.context -isnot [Collections.IDictionary] -or
                -not (Test-CostKnownContextValue $baseline.context[$key])) { $reasons.Add("Unknown comparison context: $key.") }
            elseif ($key -ne 'codeIdentity' -and $context[$key] -cne $baseline.context[$key]) { $reasons.Add("Different comparison context: $key.") }
        }
        if (@($report.cases + $baseline.cases | Where-Object { -not $_.assembly }).Count) { $reasons.Add('Unknown executed assembly/artifact identity.') }
        if ($report.evidenceKind -cne $baseline.evidenceKind) { $reasons.Add('Different evidence kinds.') }
        if (-not (Test-CostState $report.taskResult.status 'success') -or -not (Test-CostState $report.taskResult.completion 'completed') -or
            (Get-CostNumber $report.taskResult.exitCode) -ne 0 -or $baseline.taskResult -isnot [Collections.IDictionary] -or
            -not (Test-CostState $baseline.taskResult.status 'success') -or -not (Test-CostState $baseline.taskResult.completion 'completed') -or
            (Get-CostNumber $baseline.taskResult.exitCode) -ne 0) { $reasons.Add('Both tasks must be complete positive successes for comparison.') }
        if (@($report.commands + $baseline.commands | Where-Object { -not (Test-CostState $_.status 'success') -or
            -not (Test-CostCommandCompletion $_.originalRecord) -or $_.originalRecord.failureKind -or (Get-CostNumber $_.exitCode) -ne 0 -or (Get-CostNumber $_.originalRecord.exitCode) -ne 0 }).Count) {
            $reasons.Add('Command completion evidence must be typed and complete on both sides.')
        }
        $selectionSignature = Get-CostSelectionSignature $report.commands
        $baselineSelectionSignature = Get-CostSelectionSignature $baseline.commands
        if ($null -eq $selectionSignature -or $null -eq $baselineSelectionSignature) { $reasons.Add('Unknown command/arguments comparison basis.') }
        elseif ($selectionSignature -cne $baselineSelectionSignature) { $reasons.Add('Different command selection/build/filter.') }
        if ((Get-CostExecutionSignature $report.cases) -cne (Get-CostExecutionSignature $baseline.cases)) { $reasons.Add('Different executed case identities/outcomes.') }
        $currentWall = Get-CostNumber $report.taskResult.wallClockMs
        $baselineWall = Get-CostNumber $baseline.taskResult.wallClockMs
        $currentDuration = Get-CostNumber $report.caseDurationMs
        $baselineDuration = Get-CostNumber $baseline.caseDurationMs
        if ($null -eq $currentWall -or $null -eq $baselineWall -or $null -eq $currentDuration -or $null -eq $baselineDuration) {
            $reasons.Add('Unknown task wall clock or complete case duration.')
        }
        $report.comparison = [ordered]@{ status = if ($reasons.Count) { 'not-comparable' } else { 'comparable' }
            baseline = (Get-CostArtifact $BaselinePath); reasons = @($reasons)
            taskWallClockDeltaMs = if (-not $reasons.Count) { $currentWall - $baselineWall } else { $null }
            caseDurationDeltaMs = if (-not $reasons.Count) { $currentDuration - $baselineDuration } else { $null }
            note = 'Current minus baseline. Descriptive only: same declared basis does not prove a code optimization or statistical trend.' }
    }
    return $report
}
Export-ModuleMember -Function Get-TestCostReport
