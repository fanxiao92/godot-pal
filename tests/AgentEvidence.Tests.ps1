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
function New-GameFeedback([string]$Phase) {
    $manifest = New-Feedback $Phase
    $manifest.kind = 'agent_game_test_feedback'
    $manifest.Remove('assembly'); $manifest.Remove('allowedWarnings')
    # Frozen independently of the TRX writer, not learned from observed results.
    $manifest.expectedTests = @('Pal.Game.Tests.Fixture.Case1', 'Pal.Game.Tests.Fixture.Case2', 'Pal.Game.Tests.Fixture.Case3')
    $manifest.expectedFailures = @(if ($Phase -ceq 'red') {
        @{ name = 'Pal.Game.Tests.Fixture.Case1'; message = 'DESIGNATED_ASSERTION 中文'; stack = 'FixtureTests.cs:line 12' }
        @{ name = 'Pal.Game.Tests.Fixture.Case2'; message = 'DESIGNATED_ASSERTION 中文'; stack = 'FixtureTests.cs:line 12' }
    })
    return $manifest
}
function GameTrx([string]$Phase, [int]$Count = 3) {
    $text = Trx $Phase
    $rows = ''; $definitions = ''; $entries = ''
    foreach ($i in 1..$Count) {
        $outcome = if ($Phase -ceq 'red' -and $i -le 2) { 'Failed' } else { 'Passed' }
        $errorInfo = if ($outcome -ceq 'Failed') { '<Output><ErrorInfo><Message>DESIGNATED_ASSERTION 中文</Message><StackTrace>FixtureTests.cs:line 12</StackTrace></ErrorInfo></Output>' } else { '' }
        $rows += "<UnitTestResult testId='case-$i' executionId='exec-$i' testName='Pal.Game.Tests.Fixture.Case$i' outcome='$outcome'>$errorInfo</UnitTestResult>"
        $definitions += "<UnitTest id='case-$i' name='Case$i'><Execution id='exec-$i'/><TestMethod adapterTypeName='executor://gdunit4.testadapter/' className='Pal.Game.Tests.Fixture' name='Case$i'/></UnitTest>"
        $entries += "<TestEntry testId='case-$i' executionId='exec-$i'/>"
    }
    $text = [regex]::Replace($text, '<Results>.*?</Results>', "<Results>$rows</Results>", 'Singleline')
    $text = [regex]::Replace($text, '<TestDefinitions>.*?</TestDefinitions>', "<TestDefinitions>$definitions</TestDefinitions>", 'Singleline')
    $text = [regex]::Replace($text, '<TestEntries>.*?</TestEntries>', "<TestEntries>$entries</TestEntries>", 'Singleline')
    if ($Count -ne 3) { $text = $text.Replace("total='3'", "total='$Count'").Replace("executed='3'", "executed='$Count'").Replace("passed='3'", "passed='$Count'") }
    return $text
}
function Check([string]$Name, [int]$ExpectedExit, [scriptblock]$Change = {}, [string]$Phase = 'red', [string]$Reason = '', [switch]$Game, [int]$ActualCount = -1, [int]$MissingCount = -1) {
    $dir = Join-Path $evidence ("{0:D2}" -f ($records.Count + 1)); $null = [IO.Directory]::CreateDirectory($dir)
    $script:assembly = [Security.SecurityElement]::Escape((Join-Path $dir 'fixture.dll'))
    $manifest = if ($Game) { New-GameFeedback $Phase } else { New-Feedback $Phase }
    $trx = if ($Game) { GameTrx $Phase } else { Trx $Phase }
    $stdout = 'native fixture stdout'; $stderr = ''; $inputBytes = $null
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
    Assert ($report.classifierExitCode -eq $actual) "${Name}: classifier exit conflated"
    if ($Game -and $report.native) {
        Assert ($report.native.exitCode -ceq $manifest.native.exitCode) "${Name}: native exit inferred or changed"
        Assert ($report.native.cleanupComplete -ceq $manifest.native.cleanupComplete) "${Name}: cleanup state lost"
    }
    if ($ActualCount -ge 0) { Assert ($report.total -eq $ActualCount) "${Name}: observed count lost" }
    if ($MissingCount -ge 0) { Assert (@($report.missingTests).Count -eq $MissingCount) "${Name}: missing identities lost" }
    Write-Output "PASS $Name"
}
function Check-Ledger([string]$Name, [string]$Text, [int]$ExpectedExit = 0, [int]$ExpectedCount = 1, [string]$Reason = 'agent-evidence:ledger') {
    $dir = Join-Path $evidence ("{0:D2}" -f ($records.Count + 1)); $null = [IO.Directory]::CreateDirectory($dir)
    $inputFile = Join-Path $dir 'commands.json'; Put $inputFile $Text
    $arguments = @('-NoProfile', '-File', $ledgerRunner, '-ModulePath', $ledgerModule, '-Path', $inputFile)
    Push-Location $foreign
    try { & $hostPath @arguments 1> (Join-Path $dir 'reader.stdout.json') 2> (Join-Path $dir 'reader.stderr.txt'); $actual = $LASTEXITCODE } finally { Pop-Location }
    Put (Join-Path $dir 'exit.txt') ([string]$actual)
    $output = [IO.File]::ReadAllText((Join-Path $dir 'reader.stdout.json'), $utf8)
    $errorText = [IO.File]::ReadAllText((Join-Path $dir 'reader.stderr.txt'), $utf8)
    $records.Add(@{ name = $Name; executable = $hostPath; arguments = $arguments; cwd = $foreign; exitCode = $actual; expectedExit = $ExpectedExit })
    Put (Join-Path $evidence 'commands.json') (Json $records.ToArray())
    Assert ($actual -eq $ExpectedExit) "${Name}: expected $ExpectedExit, got $actual; $errorText"
    if ($ExpectedExit -eq 0) {
        $report = $output | ConvertFrom-Json -AsHashtable
        Assert ($report.arrayType -ceq 'System.Object[]' -and $report.count -eq $ExpectedCount) "${Name}: unstable array return shape"
        $expectedArray = if ($Text.TrimStart().StartsWith('[')) { $Text } else { '[' + $Text + ']' }
        $expected = ('{"commands":' + $expectedArray + '}') | ConvertFrom-Json -AsHashtable
        Assert ((Json $report.commands) -ceq (Json $expected.commands)) "${Name}: producer fields changed, defaulted or dropped"
        Assert ([string]::IsNullOrWhiteSpace($errorText)) "${Name}: unexpected reader diagnostic"
        if ($Text.Contains('2026-11-07T13:00:00.1234567Z')) {
            Assert ($output.Contains('"2026-11-07T13:00:00.1234567Z"')) "${Name}: date string coerced"
        }
    } else { Assert ($errorText -match $Reason) "${Name}: wrong rejection: $errorText" }
    Write-Output "PASS $Name"
}
try {
    $ledgerModule = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../scripts/AgentEvidenceJson.psm1'))
    $ledgerRunner = Join-Path $evidence 'read-command-ledger.ps1'
    Put $ledgerRunner @'
param([string]$ModulePath, [string]$Path)
$ErrorActionPreference = 'Stop'
try {
    Import-Module $ModulePath -Force
    $commands = Read-AgentCommandLedger -Path $Path
    ConvertTo-Json -InputObject @{ arrayType = $commands.GetType().FullName; count = $commands.Count; commands = $commands } -Depth 100
    exit 0
} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
'@
    $producer = '{"producerSpecific":{"nullable":null,"enabled":false,"items":[1,{"value":"中文"},[2,3]]},"exitCode":0,"startedUtc":"2026-11-07T13:00:00.1234567Z"}'
    Check-Ledger 'ledger single object returns array and preserves producer fields' $producer
    Check-Ledger 'ledger singleton array remains an array' ('[' + $producer + ']')
    Check-Ledger 'ledger object array preserves order and missing fields' ('[' + $producer + ',{"other":"second"}]') -ExpectedCount 2
    Check-Ledger 'ledger container reader does not require producer fields' '{}'
    foreach ($invalid in @('null', '[]', '0', 'false', '"command"', '[null]', '[{},null]', '[{},1]', '[{},"command"]', '[{},false]', '[[],{}]', '[[{}]]')) {
        Check-Ledger "ledger rejects invalid container $invalid" $invalid -ExpectedExit 1
    }
    Check-Ledger 'ledger rejects duplicate root key' '{"a":1,"a":2}' -ExpectedExit 1 -Reason 'agent-evidence:json.*Duplicate property'
    Check-Ledger 'ledger rejects nested duplicate key in array' '[{"producer":{"a":1,"a":2}}]' -ExpectedExit 1 -Reason 'agent-evidence:json.*Duplicate property'
    Check 'RED accepts empty diagnostic streams and shared theory testId' 0
    Check 'RED accepts exact FAIL echo multisets' 0 {
        $stderr = "[xUnit.net 00:00:01.00] Fixture.Case(scenario: 1) [FAIL]`n[xUnit.net 00:00:01.01] Fixture.Case(scenario: 2) [FAIL]`n"
        $infos = "<RunInfo outcome='Error' timestamp='2026-11-07T13:00:03Z'><Text>[xUnit.net 00:00:01.00] Fixture.Case(scenario: 1) [FAIL]</Text></RunInfo><RunInfo outcome='Error' timestamp='2026-11-07T13:00:03Z'><Text>[xUnit.net 00:00:01.01] Fixture.Case(scenario: 2) [FAIL]</Text></RunInfo>"
        $trx = $trx.Replace('<RunInfos/>', "<RunInfos>$infos</RunInfos>")
    }
    Check 'GREEN accepts completed native feedback' 0 -Phase green
    Check 'Game GREEN exact independent FQN set' 0 -Game -Phase green -ActualCount 3 -MissingCount 0
    Check 'Game designated RED exact name message stack' 0 -Game
    Check 'Game wrong message substring is not exact' 1 { $trx = $trx.Replace('DESIGNATED_ASSERTION 中文', 'prefix DESIGNATED_ASSERTION 中文') } -Game -Reason 'agent-evidence:failure'
    Check 'Game stack substring is not exact' 1 { $trx = $trx.Replace('FixtureTests.cs:line 12', 'prefix FixtureTests.cs:line 12') } -Game -Reason 'agent-evidence:failure'
    Check 'Game missing summary rejected' 1 { $trx = [regex]::Replace($trx, '<ResultSummary.*?</ResultSummary>', '') } -Game -Phase green -Reason 'agent-evidence:trx'
    Check 'Game missing report rejected' 1 { $manifest.files.trx = 'absent.trx' } -Game -Reason 'Could not find|cannot find'
    Check 'Game missing required counter rejected' 1 { $trx = $trx.Replace(" executed='3'", '') } -Game -Phase green -Reason 'agent-evidence:counter'
    Check 'Game nonzero aborted counter rejected' 1 { $trx = $trx.Replace("aborted='0'", "aborted='1'") } -Game -Phase green -Reason 'agent-evidence:counter'
    Check 'Game wrong assertion position' 1 { $trx = $trx.Replace('line 12', 'line 13') } -Game -Reason 'agent-evidence:failure'
    Check 'Game wrong designated name' 1 { $manifest.expectedFailures[0].name = 'Case1' } -Game -Reason 'agent-evidence:failure'
    Check 'Game extra failure cannot be designated RED' 1 { $trx = $trx.Replace("Case3' outcome='Passed'", "Case3' outcome='Failed'") } -Game -Reason 'agent-evidence:failure'
    Check 'Game failure multiplicity cannot be replaced by set matching' 1 { $manifest.expectedFailures[1] = $manifest.expectedFailures[0] } -Game -Reason 'agent-evidence:failure'
    Check 'Game missing designated failure' 1 { $manifest.expectedFailures += @{ name = 'Pal.Game.Tests.Fixture.Case3'; message = 'DESIGNATED_ASSERTION 中文'; stack = 'FixtureTests.cs:line 12' } } -Game -Reason 'agent-evidence:failure'
    Check 'Game skip rejected' 1 { $trx = $trx.Replace("outcome='Passed'", "outcome='NotExecuted'") } -Game -Phase green -Reason 'agent-evidence:result'
    Check 'Game independent set detects wrong test with same count' 1 { $manifest.expectedTests[2] = 'Pal.Game.Tests.Fixture.Absent' } -Game -Phase green -Reason 'set mismatch' -ActualCount 3 -MissingCount 1
    Check 'Game missing result records coverage' 1 { $manifest.total = 4; $manifest.expectedTests += 'Pal.Game.Tests.Fixture.Absent' } -Game -Phase green -Reason 'agent-evidence:count' -ActualCount 3 -MissingCount 1
    Check 'Game duplicate expected FQN rejected' 1 { $manifest.expectedTests[2] = $manifest.expectedTests[0] } -Game -Phase green -Reason 'agent-evidence:schema'
    Check 'Game duplicate result graph rejected' 1 { $trx = $trx.Replace('case-2', 'case-1') } -Game -Reason 'duplicate identity|definition identity'
    Check 'Game duplicate execution graph rejected' 1 { $trx = $trx.Replace('exec-2', 'exec-1') } -Game -Reason 'duplicate identity'
    Check 'Game missing entry graph rejected' 1 { $trx = $trx.Replace("<TestEntry testId='case-3' executionId='exec-3'/>", '') } -Game -Reason 'Incomplete TRX identity graph'
    foreach ($phase in @('green', 'red')) {
        Check "Game $phase summary abort cannot be feedback" 1 { $trx = $trx.Replace("outcome='Completed'", "outcome='Aborted'").Replace("outcome='Failed'><Counters", "outcome='Aborted'><Counters") } -Game -Phase $phase -Reason 'agent-evidence:summary'
        Check "Game $phase unknown native exit remains incomplete" 2 { $manifest.native.exitCode = $null } -Game -Phase $phase -Reason 'agent-evidence:incomplete'
        Check "Game $phase incomplete EOF is not child exit" 2 { $manifest.native.stdoutComplete = $false } -Game -Phase $phase -Reason 'agent-evidence:incomplete'
        Check "Game $phase timeout cannot be designated RED" 2 { $manifest.native.timedOut = $true } -Game -Phase $phase -Reason 'agent-evidence:incomplete'
        Check "Game $phase abort 466 of frozen 474 reports eight missing and unknown native" 2 {
            $manifest.total = 474
            $manifest.expectedTests = @(1..474 | ForEach-Object { "Pal.Game.Tests.Fixture.Case$_" })
            $manifest.native.exitCode = $null
            $trx = (GameTrx green 466).Replace("outcome='Completed'", "outcome='Failed'").Replace('<RunInfos/>', "<RunInfos><RunInfo outcome='Error' timestamp='2026-11-07T13:00:03Z'><Text>Aborting test run: session timeout 300000 milliseconds</Text></RunInfo></RunInfos>")
            $stdout = 'child exit code 1; 466 Passed; Aborting after 300000 milliseconds'
        } -Game -Phase $phase -Reason 'agent-evidence:incomplete' -ActualCount 466 -MissingCount 8
        Check "Game $phase 466 of 474 cannot pass even with known phase exit" 1 {
            $manifest.total = 474
            $manifest.expectedTests = @(1..474 | ForEach-Object { "Pal.Game.Tests.Fixture.Case$_" })
            $trx = (GameTrx green 466).Replace("outcome='Completed'", "outcome='Failed'").Replace('<RunInfos/>', "<RunInfos><RunInfo outcome='Error' timestamp='2026-11-07T13:00:03Z'><Text>Aborting test run: 300000 milliseconds</Text></RunInfo></RunInfos>")
        } -Game -Phase $phase -Reason 'agent-evidence:count' -ActualCount 466 -MissingCount 8
        Check "Game $phase unknown RunInfo rejected" 1 { $trx = $trx.Replace('<RunInfos/>', "<RunInfos><RunInfo outcome='Warning' timestamp='2026-11-07T13:00:03Z'><Text>CaseMismatch</Text></RunInfo></RunInfos>") } -Game -Phase $phase -Reason 'agent-evidence:runinfo'
        Check "Game $phase Error RunInfo is not GREEN or designated RED" 1 { $trx = $trx.Replace('<RunInfos/>', "<RunInfos><RunInfo outcome='Error' timestamp='2026-11-07T13:00:03Z'><Text>DESIGNATED_ASSERTION 中文</Text></RunInfo></RunInfos>") } -Game -Phase $phase -Reason 'agent-evidence:runinfo'
        Check "Game $phase stderr cannot be waived by test name" 1 { $stderr = 'Pal.Game.Tests.Fixture.Case1 DESIGNATED_ASSERTION 中文' } -Game -Phase $phase -Reason 'agent-evidence:diagnostic'
        Check "Game $phase warning rejected without CaseMismatch exception" 1 { $stdout = 'WARNING: CaseMismatch: fixture.cs' } -Game -Phase $phase -Reason 'agent-evidence:warning'
        Check "Game $phase compiler error rejected" 1 { $stdout = 'Fixture.cs(1,2): error CS9007: invalid syntax' } -Game -Phase $phase -Reason 'agent-evidence:compiler'
        Check "Game $phase native exit mismatch" 1 { $manifest.native.exitCode = 7 } -Game -Phase $phase -Reason 'agent-evidence:native'
    }
    Check 'Game strict mode has no warning allowlist' 1 { $manifest.allowedWarnings = @('WARNING: CaseMismatch') } -Game -Reason 'agent-evidence:schema'
    Check 'Game GREEN rejects failed summary even all passed' 1 { $trx = $trx.Replace("outcome='Completed'", "outcome='Failed'") } -Game -Phase green -Reason 'agent-evidence:summary'
    Check 'Game GREEN rejects Error stdout' 1 { $stdout = 'ERROR: adapter failure' } -Game -Phase green -Reason 'agent-evidence:compiler'
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
