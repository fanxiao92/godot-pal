# Public offline regression: synthetic files only; no SDK, engine, network or process enumeration.
[CmdletBinding()]
param([string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$utf8 = [Text.UTF8Encoding]::new($false)
$entry = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../scripts/Test-AgentEvidence.ps1'))
$hostPath = (Get-Process -Id $PID).Path
$evidence = if ($EvidenceDirectory) { [IO.Path]::GetFullPath($EvidenceDirectory) } else { Join-Path ([IO.Path]::GetTempPath()) ('agent-evidence-' + [guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $evidence) { throw 'Use a new agent evidence regression directory.' }
$null = [IO.Directory]::CreateDirectory($evidence)
$foreign = Join-Path $evidence '中文 foreign cwd'; $null = [IO.Directory]::CreateDirectory($foreign)
$records = [Collections.Generic.List[object]]::new(); $completed = $false
function Assert([bool]$Value, [string]$Message) { if (-not $Value) { throw $Message } }
function Put([string]$Path, [string]$Text) { [IO.File]::WriteAllText($Path, $Text, $utf8) }
function Json($Value) { ConvertTo-Json -InputObject $Value -Depth 30 }
function New-Feedback([string]$Phase = 'red') {
    $failures = @(if ($Phase -ceq 'red') { 1, 2 | ForEach-Object { @{ name = "Fixture.Case(scenario: $_)"; messageContains = 'DESIGNATED_ASSERTION'; stackContains = 'FixtureTests.cs:line 12' } } })
    return @{
        schemaVersion = 1; kind = 'agent_test_feedback'; phase = $Phase; assembly = 'fixture.dll'; total = 3
        native = @{ exitCode = $(if ($Phase -ceq 'red') { 1 } else { 0 }); rootExited = $true; stdoutComplete = $true; stderrComplete = $true; cleanupComplete = $true; timedOut = $false; startedUtc = '2026-11-07T13:00:00.1234567Z'; endedUtc = '2026-11-07T13:01:00.1234567Z' }
        files = @{ trx = 'results.trx'; stdout = 'stdout.txt'; stderr = 'stderr.txt' }; expectedFailures = $failures; allowedWarnings = @()
    }
}
function Trx([string]$Phase) {
    $rows = @(foreach ($i in 1..3) {
        $failed = $Phase -ceq 'red' -and $i -le 2
        $outcome = if ($failed) { 'Failed' } else { 'Passed' }
        $errorInfo = if ($failed) { '<Output><ErrorInfo><Message>DESIGNATED_ASSERTION 中文</Message><StackTrace>FixtureTests.cs:line 12</StackTrace></ErrorInfo></Output>' } else { '' }
        "<UnitTestResult testId='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa' executionId='00000000-0000-0000-0000-00000000000$i' testName='Fixture.Case(scenario: $i)' outcome='$outcome'>$errorInfo</UnitTestResult>"
    }) -join ''
    $entries = @(foreach ($i in 1..3) { "<TestEntry testId='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa' executionId='00000000-0000-0000-0000-00000000000$i'/>" }) -join ''
    $failed = if ($Phase -ceq 'red') { 2 } else { 0 }; $passed = 3 - $failed
    $summary = if ($Phase -ceq 'red') { 'Failed' } else { 'Completed' }
    return @"
<TestRun id='bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb' xmlns='http://microsoft.com/schemas/VisualStudio/TeamTest/2010'>
<Times creation='2026-11-07T13:00:01Z' queuing='2026-11-07T13:00:01Z' start='2026-11-07T13:00:02Z' finish='2026-11-07T13:00:59Z'/>
<Results>$rows</Results>
<TestDefinitions><UnitTest id='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'><TestMethod className='Fixture' name='Case' codeBase='$script:assembly'/></UnitTest></TestDefinitions>
<TestEntries>$entries</TestEntries>
<ResultSummary outcome='$summary'><Counters total='3' executed='3' passed='$passed' failed='$failed' error='0' timeout='0' aborted='0' inconclusive='0' passedButRunAborted='0' notRunnable='0' notExecuted='0' disconnected='0' warning='0' completed='0' inProgress='0' pending='0'/><RunInfos/></ResultSummary>
</TestRun>
"@
}
function Check([string]$Name, [int]$ExpectedExit, [scriptblock]$Change = {}, [string]$Phase = 'red', [string]$Reason = '') {
    $dir = Join-Path $evidence ("{0:D2}" -f ($records.Count + 1)); $null = [IO.Directory]::CreateDirectory($dir)
    $script:assembly = [Security.SecurityElement]::Escape((Join-Path $dir 'fixture.dll'))
    $manifest = New-Feedback $Phase; $trx = Trx $Phase; $stdout = 'native fixture stdout'; $stderr = ''; $inputBytes = $null
    . $Change
    if ($null -ne $inputBytes) { [IO.File]::WriteAllBytes((Join-Path $dir 'input.json'), $inputBytes) }
    else { Put (Join-Path $dir 'input.json') (Json $manifest) }
    Put (Join-Path $dir 'results.trx') $trx
    Put (Join-Path $dir 'stdout.txt') $stdout; Put (Join-Path $dir 'stderr.txt') $stderr
    $arguments = @('-NoProfile', '-File', $entry, '-InputPath', (Join-Path $dir 'input.json'))
    Push-Location $foreign
    try { & $hostPath @arguments 1> (Join-Path $dir 'checker.stdout.json') 2> (Join-Path $dir 'checker.stderr.txt'); $actual = $LASTEXITCODE } finally { Pop-Location }
    Put (Join-Path $dir 'exit.txt') ([string]$actual)
    $text = [IO.File]::ReadAllText((Join-Path $dir 'checker.stdout.json'), $utf8)
    $report = $text | ConvertFrom-Json
    $records.Add(@{ name = $Name; executable = $hostPath; arguments = $arguments; cwd = $foreign; exitCode = $actual; expectedExit = $ExpectedExit })
    Put (Join-Path $evidence 'commands.json') (Json $records.ToArray())
    Assert ($actual -eq $ExpectedExit) "${Name}: expected $ExpectedExit, got $actual; $text"
    Assert (($actual -eq 0) -eq ($report.status -ceq 'valid')) "${Name}: false validity"
    if ($Reason) { Assert ($report.failure -match $Reason) "${Name}: wrong failure reason: $($report.failure)" }
    Write-Output "PASS $Name"
}
try {
    Check 'RED accepts empty diagnostic streams and shared theory testId' 0
    Check 'RED accepts exact FAIL echo multisets' 0 {
        $stderr = "[xUnit.net 00:00:01.00] Fixture.Case(scenario: 1) [FAIL]`n[xUnit.net 00:00:01.01] Fixture.Case(scenario: 2) [FAIL]`n"
        $infos = "<RunInfo outcome='Error' timestamp='2026-11-07T13:00:03Z'><Text>[xUnit.net 00:00:01.00] Fixture.Case(scenario: 1) [FAIL]</Text></RunInfo><RunInfo outcome='Error' timestamp='2026-11-07T13:00:03Z'><Text>[xUnit.net 00:00:01.01] Fixture.Case(scenario: 2) [FAIL]</Text></RunInfo>"
        $trx = $trx.Replace('<RunInfos/>', "<RunInfos>$infos</RunInfos>")
    }
    Check 'GREEN accepts completed native feedback' 0 -Phase green
    Check 'duplicate root JSON property rejected' 1 {
        $inputBytes = $utf8.GetBytes((Json $manifest).Replace('"schemaVersion": 1', '"schemaVersion": 1, "schemaVersion": 1'))
    } -Reason 'agent-evidence:json.*Duplicate property'
    Check 'duplicate nested JSON property rejected' 1 {
        $inputBytes = $utf8.GetBytes((Json $manifest).Replace('"exitCode": 1', '"exitCode": 1, "exitCode": 1'))
    } -Reason 'agent-evidence:json.*Duplicate property'
    Check 'invalid UTF8 manifest rejected' 1 { $inputBytes = [byte[]]@(255) } -Reason 'Unable to translate|invalid|UTF'
    foreach ($encodingName in @('utf-16', 'utf-16BE', 'utf-32', 'utf-32BE')) {
        Check "$encodingName BOM manifest rejected by strict UTF8 reader" 1 {
            $encoding = [Text.Encoding]::GetEncoding($encodingName)
            $inputBytes = [byte[]]($encoding.GetPreamble() + $encoding.GetBytes((Json $manifest)))
        } -Reason 'Unable to translate|invalid|UTF'
    }
    Check 'UTF8 BOM manifest remains supported' 0 {
        $inputBytes = [byte[]]([Text.Encoding]::UTF8.GetPreamble() + $utf8.GetBytes((Json $manifest)))
    }
    foreach ($stream in @('stdout', 'stderr')) {
        Check "UTF8 BOM $stream remains supported" 0 {
            $path = Join-Path $dir 'utf8-bom.txt'
            [IO.File]::WriteAllBytes($path, [Text.Encoding]::UTF8.GetPreamble())
            $manifest.files[$stream] = 'utf8-bom.txt'
        }
        Check "UTF16 BOM $stream rejected by strict UTF8 reader" 1 {
            $path = Join-Path $dir 'utf16.txt'
            [IO.File]::WriteAllBytes($path, [Text.Encoding]::Unicode.GetPreamble())
            $manifest.files[$stream] = 'utf16.txt'
        } -Reason 'Unable to translate|invalid|UTF'
    }
    Check 'repeated display names retain execution multiplicity' 0 {
        $trx = $trx.Replace('Fixture.Case(scenario: 2)', 'Fixture.Case(scenario: 1)'); $manifest.expectedFailures[1].name = 'Fixture.Case(scenario: 1)'
        $stderr = "[xUnit.net 00:00:01.00] Fixture.Case(scenario: 1) [FAIL]`n[xUnit.net 00:00:01.01] Fixture.Case(scenario: 1) [FAIL]"
    }
    Check 'native non-designated exit rejected' 1 { $manifest.native.exitCode = 7 } -Reason 'agent-evidence:native'
    Check 'unknown native exit remains incomplete' 2 { $manifest.native.exitCode = $null } -Reason 'agent-evidence:incomplete'
    foreach ($flag in @('rootExited', 'stdoutComplete', 'stderrComplete', 'cleanupComplete')) {
        Check "incomplete $flag remains incomplete" 2 { $manifest.native[$flag] = $false } -Reason 'agent-evidence:incomplete'
    }
    Check 'timeout remains incomplete' 2 { $manifest.native.timedOut = $true } -Reason 'agent-evidence:incomplete'
    Check 'numeric exit cannot be a string' 1 { $manifest.native.exitCode = '1' } -Reason 'agent-evidence:schema'
    Check 'new nullable warning blocks RED' 1 { $stdout = 'Tests.cs(1,2): warning CS8602: Dereference of a possibly null reference. [fixture.csproj]' } -Reason 'agent-evidence:warning'
    Check 'literal warning allowlist permits only its exact diagnostic' 0 {
        $stdout = 'Tests.cs(101,9): warning xUnit2012: Existing diagnostic. [fixture.csproj]'; $manifest.allowedWarnings = @($stdout)
    }
    Check 'warning allowlist is not a regex' 1 { $stdout = 'Tests.cs(1,2): warning CS8602: New.'; $manifest.allowedWarnings = @('.*') } -Reason 'agent-evidence:warning'
    Check 'compiler failure cannot be RED' 1 { $stdout = 'Tests.cs(1,2): error CS9007: Invalid raw string literal.' } -Reason 'agent-evidence:compiler'
    Check 'new warning also blocks GREEN' 1 { $stdout = 'Tests.cs(1,2): warning CS8602: New.' } -Phase green -Reason 'agent-evidence:warning'
    Check 'non-designated business message rejected' 1 { $trx = $trx.Replace('DESIGNATED_ASSERTION', 'DIFFERENT_ASSERTION') } -Reason 'agent-evidence:failure'
    Check 'non-designated assertion location rejected' 1 { $trx = $trx.Replace('FixtureTests.cs:line 12', 'FixtureTests.cs:line 13') } -Reason 'agent-evidence:failure'
    Check 'skip cannot replace failure' 1 { $trx = $trx.Replace("outcome='Failed'>", "outcome='NotExecuted'>") } -Reason 'agent-evidence:result'
    Check 'unexpected total rejected' 1 { $manifest.total = 4 } -Reason 'agent-evidence:count'
    Check 'partial echo set rejected' 1 { $stderr = '[xUnit.net 00:00:01.00] Fixture.Case(scenario: 1) [FAIL]' } -Reason 'agent-evidence:echo'
    Check 'duplicate echo rejected' 1 { $stderr = "[xUnit.net 00:00:01.00] Fixture.Case(scenario: 1) [FAIL]`n[xUnit.net 00:00:01.00] Fixture.Case(scenario: 1) [FAIL]" } -Reason 'agent-evidence:echo'
    Check 'unexpected stderr rejected' 1 { $stderr = 'Unhandled exception' } -Reason 'agent-evidence:echo'
    Check 'GREEN stderr must be empty' 1 { $stderr = '[xUnit.net 00:00:01.00] Fixture.Case(scenario: 1) [FAIL]' } -Phase green -Reason 'agent-evidence:echo'
    Check 'RunInfo warning is not a FAIL echo' 1 { $trx = $trx.Replace('<RunInfos/>', "<RunInfos><RunInfo outcome='Warning' timestamp='2026-11-07T13:00:03Z'><Text>unexpected</Text></RunInfo></RunInfos>") } -Reason 'agent-evidence:runinfo'
    Check 'GREEN RunInfo cannot be accepted as RED diagnostics' 1 { $trx = $trx.Replace('<RunInfos/>', "<RunInfos><RunInfo outcome='Error' timestamp='2026-11-07T13:00:03Z'><Text>[xUnit.net 00:00:01.00] Fixture.Case(scenario: 1) [FAIL]</Text></RunInfo></RunInfos>") } -Phase green -Reason 'agent-evidence:echo'
    Check 'stale RunInfo rejected' 1 { $trx = $trx.Replace('<RunInfos/>', "<RunInfos><RunInfo outcome='Error' timestamp='2026-11-06T13:00:03Z'><Text>[xUnit.net 00:00:01.00] Fixture.Case(scenario: 1) [FAIL]</Text></RunInfo></RunInfos>") } -Reason 'agent-evidence:runinfo'
    Check 'duplicate execution identity rejected' 1 { $trx = $trx.Replace('00000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000001') } -Reason 'duplicate identity'
    Check 'missing TRX report rejected' 1 { $manifest.files.trx = 'missing.trx' } -Reason 'Could not find|cannot find'
    Check 'missing summary rejected' 1 { $trx = [regex]::Replace($trx, '<ResultSummary.*?</ResultSummary>', '') } -Reason 'agent-evidence:trx'
    Check 'unknown nonzero counter rejected' 1 { $trx = $trx.Replace("pending='0'", "pending='0' extra='1'") } -Reason 'agent-evidence:counter'
    Check 'TRX outside native time window rejected' 1 { $manifest.native.startedUtc = '2026-11-07T13:00:03Z' } -Reason 'agent-evidence:time'
    Check 'invalid UTF8 cannot be classified' 1 {
        Put (Join-Path $dir 'invalid.txt') 'placeholder'; [IO.File]::WriteAllBytes((Join-Path $dir 'invalid.txt'), [byte[]]@(255)); $manifest.files.stdout = 'invalid.txt'
    } -Reason 'Unable to translate|invalid|UTF'
    Check 'blocked state has no execution authority' 0 {
        Put (Join-Path $dir 'history.json') '{"original":true}'
        $manifest = @{
            schemaVersion = 1; kind = 'derived_execution_index_not_issue_tracker'; scope = @{}; integration = @{}
            active_ticket = @{ number = 129; state = 'blocked'; source = @{ worktree = 'C:/fixture'; branch = 'ticket/fixture'; head = 'a' * 40; tree = 'b' * 40; dirty = $false; binding = 'binding.json' }; authority = @{ sdkRemaining = 0; writer = $null; runId = $null; deadlineUtc = $null }; lastAttempt = $null; checks = @(); openContracts = @(); limits = @(); inputs = @{} }
            receipts = @{}; history = @{ snapshot = 'history.json'; sha256 = (Get-FileHash (Join-Path $dir 'history.json')).Hash.ToLowerInvariant() }; nextAction = @{ action = 'wait'; reason = 'quota'; plan = $null }; updatedUtc = '2026-11-07T13:00:00.1234567Z'
        }
        $script:stateFixture = Json $manifest
    }
    foreach ($invalid in @('sdkRemaining', 'writer', 'runId', 'deadlineUtc', 'dispatch', 'history', 'completed')) {
        Check "state rejects $invalid inconsistency" 1 {
            $manifest = $script:stateFixture | ConvertFrom-Json -AsHashtable
            Put (Join-Path $dir 'history.json') '{"original":true}'
            switch ($invalid) {
                'sdkRemaining' { $manifest.active_ticket.authority.sdkRemaining = 1 }
                'writer' { $manifest.active_ticket.authority.writer = 'worker' }
                'runId' { $manifest.active_ticket.authority.runId = 'old-run' }
                'deadlineUtc' { $manifest.active_ticket.authority.deadlineUtc = '2026-11-07T14:00:00Z' }
                'dispatch' { $manifest.nextAction.action = 'dispatch' }
                'history' { Put (Join-Path $dir 'history.json') '{"changed":true}' }
                'completed' { $manifest.active_ticket.completed_u1 = 'history does not belong here' }
            }
        } -Reason $(if ($invalid -eq 'dispatch') { 'agent-evidence:authority' } elseif ($invalid -eq 'history') { 'agent-evidence:history' } else { 'agent-evidence:schema' })
    }
    Check 'ISO dates retain all digits and string representation' 0 {
        $manifest = $script:stateFixture | ConvertFrom-Json -AsHashtable
        $manifest.updatedUtc = '2026-11-07T13:00:00.1234567Z'; Put (Join-Path $dir 'history.json') '{"original":true}'
    }
    $last = Join-Path (Join-Path $evidence ("{0:D2}" -f $records.Count)) 'checker.stdout.json'
    Assert ([IO.File]::ReadAllText($last).Contains('"updatedUtc": "2026-11-07T13:00:00.1234567Z"')) 'Date string was coerced or truncated.'
    $completed = $true
    Write-Output "PASS agent evidence regression: $($records.Count) public invocations; synthetic evidence only, no SDK/engine/network."
} finally {
    if ($completed -and -not $EvidenceDirectory) { Remove-Item -LiteralPath $evidence -Recurse -Force }
    elseif (-not $completed) { Write-Host "Agent evidence regression failed; raw evidence retained: $evidence" }
}
exit 0
