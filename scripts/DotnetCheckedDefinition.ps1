# Definitions only. Existing caller context: commands/evidence/repository/script:sequence and Save-ProcessSnapshot.
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
