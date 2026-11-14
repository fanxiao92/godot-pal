param(
    [Parameter(Mandatory)][string]$RepositoryRoot,
    [Parameter(Mandatory)][string]$TrxPath,
    [string]$ExpectedTest,
    [string]$ExpectedMessage
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
# Reuse the repository's identity reader, not a parallel TRX graph parser.
. (Join-Path $RepositoryRoot 'scripts/GameTestBatches.ps1')
$identity = Read-TestRunIdentity $TrxPath
$doc = $identity.document; $ns = $identity.ns
$summary = $doc.SelectSingleNode('/t:TestRun/t:ResultSummary', $ns)
$counters = $summary.SelectSingleNode('t:Counters', $ns)
if ($summary.outcome -cnotin @('Completed', 'Failed') -or -not $counters) { throw 'Incomplete test session.' }
$rows = @($doc.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult', $ns))
$failed = @($rows | Where-Object outcome -CEQ 'Failed')
if (-not $failed.Count -or @($rows | Where-Object { $_.outcome -cnotin @('Passed', 'Failed') }).Count) { throw 'Not a complete normal assertion/fixture failure.' }
foreach ($name in @('total', 'executed', 'passed', 'failed', 'error', 'timeout', 'aborted', 'notExecuted')) {
    $expected = switch ($name) { total { $rows.Count } executed { $rows.Count } passed { $rows.Count - $failed.Count } failed { $failed.Count } default { 0 } }
    if (-not $counters.HasAttribute($name) -or $counters.GetAttribute($name) -cne [string]$expected) { throw "Incomplete counter: $name" }
}
foreach ($attribute in $counters.Attributes) {
    if ($attribute.Name -notin @('total', 'executed', 'passed', 'failed') -and $attribute.Value -cne '0') { throw 'Unexpected nonzero session diagnostic.' }
}
$runInfos = @($summary.SelectNodes('t:RunInfos/t:RunInfo', $ns))
if (@($runInfos | Where-Object { $_.outcome -in @('Error', 'Aborted', 'Timeout') -or $_.InnerText -match '(?i)abort|crash|timed out|WindowsStdOutHook' }).Count -or
    @($failed | Where-Object { $_.InnerText -match '(?i)TimeoutException|timed out|test host.*crash|WindowsStdOutHook' }).Count) { throw 'Abnormal or liveness failure requires operator review.' }
$expectedRed = $false
if ($ExpectedTest -or $ExpectedMessage) {
    if (-not $ExpectedTest -or -not $ExpectedMessage -or $failed.Count -ne 1) { throw 'Expected RED requires exactly one specified failure.' }
    $definition = @($doc.SelectNodes('/t:TestRun/t:TestDefinitions/t:UnitTest', $ns) | Where-Object id -CEQ $failed[0].testId)[0]
    $method = $definition.SelectSingleNode('t:TestMethod', $ns)
    $fqn = ($method.className -split ',')[0] + '.' + $method.name
    $message = $failed[0].SelectSingleNode('t:Output/t:ErrorInfo/t:Message', $ns)
    if (($fqn -cne $ExpectedTest -and $failed[0].testName -cne $ExpectedTest) -or -not $message -or -not $message.InnerText.Contains($ExpectedMessage, [StringComparison]::Ordinal)) { throw 'Failure does not match the declared test and literal diagnostic.' }
    $expectedRed = $true
}
[pscustomobject]@{ classification = 'normal_test_failure'; expectedRed = $expectedRed; failed = $failed.Count; runId = $identity.runId } | ConvertTo-Json -Compress
