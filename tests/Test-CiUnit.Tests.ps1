[CmdletBinding()]
param([string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. "$root/scripts/GameTestBatches.ps1"
. "$root/scripts/TestRunDefinitions.ps1"
$evidence = if ($EvidenceDirectory) { [IO.Path]::GetFullPath($EvidenceDirectory) } else { Join-Path ([IO.Path]::GetTempPath()) ('ci-unit-' + [guid]::NewGuid().ToString('N')) }
if (Test-Path $evidence) { throw 'EvidenceDirectory must be new.' }
$null = New-Item -ItemType Directory $evidence
$stamp = [DateTimeOffset]::UtcNow.ToString('O')
$assembly = Join-Path $evidence 'Fixture.dll'
$trx = @"
<TestRun id="11111111-1111-1111-1111-111111111111" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Times creation="$stamp" queuing="$stamp" start="$stamp" finish="$stamp"/><TestDefinitions><UnitTest name="Fixture.Pass" id="case-1"><Execution id="22222222-2222-2222-2222-222222222222"/><TestMethod codeBase="$assembly" className="Fixture" name="Pass"/></UnitTest></TestDefinitions><TestEntries><TestEntry testId="case-1" executionId="22222222-2222-2222-2222-222222222222"/></TestEntries><Results><UnitTestResult executionId="22222222-2222-2222-2222-222222222222" testId="case-1" testName="Fixture.Pass" outcome="Passed"/></Results><ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="1" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0"/><RunInfos/></ResultSummary></TestRun>
"@
$path = Join-Path $evidence 'valid.trx'
[IO.File]::WriteAllText($path, $trx)
$r = Assert-DirectUnitTrxRun -Path $path -Assembly $assembly
if ($r.names.Count -ne 1) { throw 'Valid complete direct Unit result rejected.' }
$cases = @{
    skip = $trx.Replace('outcome="Passed"', 'outcome="NotExecuted"')
    failed = $trx.Replace('outcome="Passed"', 'outcome="Failed"')
    aborted = $trx.Replace('outcome="Completed"', 'outcome="Aborted"')
    counter = $trx.Replace('executed="1"', 'executed="0"')
    missingCounter = $trx.Replace(' warning="0"', '')
    runInfo = $trx.Replace('<RunInfos/>', '<RunInfos><RunInfo outcome="Warning"><Text>unexpected</Text></RunInfo></RunInfos>')
    missingTimes = $trx -replace '<Times[^>]*/>', ''
    duplicateTimes = $trx -replace '(<Times[^>]*/>)', '$1$1'
    wrongAssembly = $trx.Replace('Fixture.dll', 'Wrong.dll')
    missingResult = $trx -replace '<UnitTestResult[^>]*/>', ''
    missingEntry = $trx -replace '<TestEntry [^>]*/>', ''
    wrongExecution = $trx.Replace('executionId="22222222-2222-2222-2222-222222222222"', 'executionId="33333333-3333-3333-3333-333333333333"')
    invalidRun = $trx.Replace('11111111-1111-1111-1111-111111111111', 'not-guid')
    zero = $trx -replace '<UnitTest [\s\S]*?</UnitTest>', '' -replace '<UnitTestResult[^>]*/>', '' -replace '<TestEntry [^>]*/>', ''
    truncated = $trx.Substring(0, $trx.Length - 20)
}
foreach ($name in $cases.Keys) {
    $p = Join-Path $evidence "$name.trx"; [IO.File]::WriteAllText($p, $cases[$name])
    $rejected = $false
    try { $null = Assert-DirectUnitTrxRun -Path $p -Assembly $assembly } catch { $rejected = $true; $_.Exception.Message | Set-Content (Join-Path $evidence "$name.rejection.txt") }
    if (-not $rejected) { throw "Invalid result accepted: $name" }
}
Write-Output "PASS direct Unit result contract: valid + $($cases.Count) rejected shapes (synthetic, not product Unit)."
