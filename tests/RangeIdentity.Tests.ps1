# Literal TRX/discovery fixtures; no SDK, engine, package preparation or process queries.
[CmdletBinding()]
param([string]$DefinitionsPath = (Join-Path $PSScriptRoot '../scripts/TestRunDefinitions.ps1'))
$ErrorActionPreference = 'Stop'
$utf8 = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = $utf8
[Console]::InputEncoding = $utf8
$root = Join-Path ([IO.Path]::GetTempPath()) ('range-identity-' + [guid]::NewGuid().ToString('N'))
$null = [IO.Directory]::CreateDirectory($root)
$assembly = Join-Path $root 'Literal.dll'; [IO.File]::WriteAllText($assembly, 'artificial assembly identity only', $utf8)
. (Join-Path $PSScriptRoot '../scripts/GameTestBatches.ps1')
. $DefinitionsPath
$cases = @(
    @{ id = '11111111-1111-4111-8111-111111111111'; execution = 'aaaaaaaa-1111-4111-8111-111111111111'; class = 'Fixture.Cases'; method = 'Positive' }
    @{ id = '22222222-2222-4222-8222-222222222222'; execution = 'bbbbbbbb-2222-4222-8222-222222222222'; class = 'Pal.Extract.Tests.InspectCliTests'; method = 'Inspect_rejects_ambiguous_case_insensitive_names' }
    @{ id = '33333333-3333-4333-8333-333333333333'; execution = 'cccccccc-3333-4333-8333-333333333333'; class = 'Pal.Extract.Tests.ExportMapCliTests'; method = 'Export_map_rejects_case_ambiguous_archives' }
)
$discovery = [pscustomobject]@{ total = 3; assembly = $assembly; cases = @($cases | ForEach-Object { [pscustomobject]@{ Id = $_.id; FullyQualifiedName = "$($_.class).$($_.method)"; DisplayName = "$($_.class).$($_.method)" } }) }
$allowed = @('Pal.Extract.Tests.InspectCliTests.Inspect_rejects_ambiguous_case_insensitive_names', 'Pal.Extract.Tests.ExportMapCliTests.Export_map_rejects_case_ambiguous_archives')
$count = 0
function Fixture {
    $rows = @(); $defs = @(); $entries = @()
    foreach ($case in $cases) {
        $name = "$($case.class).$($case.method)"; $outcome = if ($case -eq $cases[0]) { 'Passed' } else { 'NotExecuted' }
        $rows += "<UnitTestResult testId='$($case.id)' executionId='$($case.execution)' testName='$name' outcome='$outcome'/>"
        $base = [Security.SecurityElement]::Escape($assembly)
        $defs += "<UnitTest id='$($case.id)' name='$name'><Execution id='$($case.execution)'/><TestMethod className='$($case.class)' name='$($case.method)' codeBase='$base'/></UnitTest>"
        $entries += "<TestEntry testId='$($case.id)' executionId='$($case.execution)'/>"
    }
    [xml]@"
<TestRun xmlns='http://microsoft.com/schemas/VisualStudio/TeamTest/2010' id='dddddddd-4444-4444-8444-444444444444'>
<Times creation='2026-01-01T00:00:00Z' queuing='2026-01-01T00:00:00Z' start='2026-01-01T00:00:00Z' finish='2026-01-01T00:00:01Z'/>
<Results>$($rows -join '')</Results><TestDefinitions>$($defs -join '')</TestDefinitions><TestEntries>$($entries -join '')</TestEntries>
<ResultSummary outcome='Completed'><Counters total='3' executed='1' passed='1' failed='0' error='0' timeout='0' aborted='0' inconclusive='0' passedButRunAborted='0' notRunnable='0' notExecuted='0' disconnected='0' warning='0' completed='0' inProgress='0' pending='0'/></ResultSummary>
</TestRun>
"@
}
function Add-SkipInfo($Doc, [string]$Name = 'Pal.Extract.Tests.InspectCliTests.Inspect_rejects_ambiguous_case_insensitive_names', [string]$Stamp = '2026-01-01T00:00:00.5Z') {
    $uri = $Doc.DocumentElement.NamespaceURI
    $container = @($Doc.TestRun.ResultSummary.ChildNodes | Where-Object LocalName -CEQ 'RunInfos')[0]
    if (-not $container) { $container = $Doc.CreateElement('RunInfos', $uri); $null = $Doc.TestRun.ResultSummary.AppendChild($container) }
    $info = $Doc.CreateElement('RunInfo', $uri); $info.SetAttribute('outcome', 'Warning'); $info.SetAttribute('timestamp', $Stamp)
    $text = $Doc.CreateElement('Text', $uri); $text.InnerText = "[xUnit.net 00:00:00.25]     $Name [SKIP]"
    $null = $info.AppendChild($text); $null = $container.AppendChild($info)
}
function Check([string]$Name, [bool]$Expected, [scriptblock]$Mutation = {}, [string[]]$Whitelist = $allowed) {
    $doc = Fixture; & $Mutation $doc
    $path = Join-Path $root 'current.trx'; [IO.File]::WriteAllText($path, $doc.OuterXml, $utf8)
    $actual = $false; $diagnostic = ''
    try { $null = Assert-Version7TrxRun -Path $path -Discovery $discovery -AllowedSkippedNames $Whitelist; $actual = $true }
    catch { $diagnostic = $_.Exception.Message }
    if ($actual -ne $Expected) { throw "${Name}: expected accepted=$Expected, actual=$actual; $diagnostic" }
    $script:count++; Write-Host "PASS $Name"
}
try {
    Check 'one positive and exact two historical NotExecuted rows' $true
    Check 'eligible skips can pass on another platform' $true { param($d) $d.TestRun.Results.UnitTestResult | ForEach-Object { $_.outcome = 'Passed' }; $d.TestRun.ResultSummary.Counters.executed = '3'; $d.TestRun.ResultSummary.Counters.passed = '3' }
    Check 'xUnit counters can also explicitly count actual skips' $true { param($d) $d.TestRun.ResultSummary.Counters.notExecuted = '2' }
    Check 'additional zero counter preserves original Unit compatibility' $true { param($d) $d.TestRun.ResultSummary.Counters.SetAttribute('futureCounter', '0') }
    Check 'unexpected nonzero counter rejected' $false { param($d) $d.TestRun.ResultSummary.Counters.SetAttribute('futureCounter', '1') }
    Check 'Unit default does not allow skips' $false -Whitelist @()
    Check 'invented Skipped outcome is not xUnit NotExecuted' $false { param($d) $d.TestRun.Results.UnitTestResult[1].outcome = 'Skipped' }
    Check 'positive rows cannot claim executed zero' $false { param($d) $d.TestRun.ResultSummary.Counters.executed = '0' }
    Check 'nonempty numeric GUID required' $false { param($d) $d.TestRun.id = 'not-a-guid' }
    Check 'session must not finish before start' $false { param($d) $d.TestRun.Times.finish = '2025-12-31T23:59:59Z' }
    Check 'missing timestamp rejected' $false { param($d) $d.TestRun.Times.RemoveAttribute('queuing') }
    Check 'display identity bound to independent discovery' $false { param($d) $d.TestRun.Results.UnitTestResult[0].testName = 'substitute'; $d.TestRun.TestDefinitions.UnitTest[0].name = 'substitute' }
    Check 'FQN identity cannot change under same ID' $false { param($d) $d.TestRun.TestDefinitions.UnitTest[0].TestMethod.name = 'OtherMethod' }
    Check 'assembly identity cannot change under same ID' $false { param($d) $d.TestRun.TestDefinitions.UnitTest[0].TestMethod.codeBase = (Join-Path $root 'Other.dll') }
    Check 'TestEntry cannot substitute same-count identity' $false { param($d) $d.TestRun.TestEntries.TestEntry[0].testId = 'eeeeeeee-5555-4555-8555-555555555555' }
    Check 'consistent substituted graph is still not independent discovery' $false { param($d) $new = 'eeeeeeee-5555-4555-8555-555555555555'; $d.TestRun.Results.UnitTestResult[0].testId = $new; $d.TestRun.TestDefinitions.UnitTest[0].id = $new; $d.TestRun.TestEntries.TestEntry[0].testId = $new }
    Check 'required counter cannot be absent' $false { param($d) $d.TestRun.ResultSummary.Counters.RemoveAttribute('passed') }
    Check 'RunInfo error cannot be hidden by completed counters' $false { param($d) $uri = $d.DocumentElement.NamespaceURI; $n = $d.CreateElement('RunInfos', $uri); $r = $d.CreateElement('RunInfo', $uri); $r.SetAttribute('outcome', 'Error'); $t = $d.CreateElement('Text', $uri); $t.InnerText = 'test host process crashed'; $null = $r.AppendChild($t); $null = $n.AppendChild($r); $null = $d.TestRun.ResultSummary.AppendChild($n) }
    Check 'actual eligible skipped row permits its exact xUnit warning' $true { param($d) Add-SkipInfo $d }
    Check 'two different actual skips permit their two warnings' $true { param($d) Add-SkipInfo $d; Add-SkipInfo $d 'Pal.Extract.Tests.ExportMapCliTests.Export_map_rejects_case_ambiguous_archives' }
    Check 'eligible but actually passed cannot carry skip warning' $false { param($d) Add-SkipInfo $d; $d.TestRun.Results.UnitTestResult | ForEach-Object { $_.outcome = 'Passed' }; $d.TestRun.ResultSummary.Counters.executed = '3'; $d.TestRun.ResultSummary.Counters.passed = '3' }
    Check 'unknown case cannot borrow skip warning' $false { param($d) Add-SkipInfo $d 'Fixture.Cases.Unknown' }
    Check 'duplicate skip warnings rejected' $false { param($d) Add-SkipInfo $d; Add-SkipInfo $d }
    Check 'arbitrary warning text is not an expected skip' $false { param($d) Add-SkipInfo $d; $d.TestRun.ResultSummary.RunInfos.RunInfo.Text = 'test host process crashed' }
    Check 'stale skip warning timestamp rejected' $false { param($d) Add-SkipInfo $d -Stamp '2025-12-31T23:00:00Z' }
    Check 'failed row does not qualify as an expected skip' $false { param($d) $d.TestRun.Results.UnitTestResult[1].outcome = 'Failed' }
    # Even a permitted set of skips cannot become a zero-executed success.
    Check 'all eligible rows skipped is never success' $false { param($d) $d.TestRun.Results.UnitTestResult[0].outcome = 'NotExecuted'; $d.TestRun.ResultSummary.Counters.executed = '0'; $d.TestRun.ResultSummary.Counters.passed = '0' } -Whitelist @($allowed + 'Fixture.Cases.Positive')
    Write-Host "PASS range identity regression: $count independent literal fixtures."
} finally { Remove-Item -LiteralPath $root -Recurse -Force }
