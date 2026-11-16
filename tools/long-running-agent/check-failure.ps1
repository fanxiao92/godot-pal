param(
    [Parameter(Mandatory)][string]$RepositoryRoot,
    [Parameter(Mandatory)][string]$TrxPath,
    [ValidateSet('unit', 'prepare', 'game')][string]$Scope = 'game',
    [string]$ExpectedTest,
    [string]$ExpectedMessage
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
# Inspect explicit liveness evidence before normal completeness/identity checks can mask it.
$doc = [xml]::new()
$doc.XmlResolver = $null
$doc.Load($TrxPath)
$ns = [Xml.XmlNamespaceManager]::new($doc.NameTable)
$ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
$summary = $doc.SelectSingleNode('/t:TestRun/t:ResultSummary', $ns)
$rows = @($doc.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult', $ns))
$runInfos = @($doc.SelectNodes('/t:TestRun/t:ResultSummary/t:RunInfos/t:RunInfo', $ns))
$abnormalCounters = @($doc.SelectNodes('/t:TestRun/t:ResultSummary/t:Counters/@aborted | /t:TestRun/t:ResultSummary/t:Counters/@timeout', $ns) | Where-Object { $_.Value -match '^\d+$' -and [long]$_.Value -gt 0 })
# xUnit's ordinary assertion [FAIL] RunInfo Error is not an abnormal Unit session.
if (($summary -and $summary.outcome -in @('Aborted', 'Timeout')) -or
    @($rows | Where-Object outcome -In @('Aborted', 'Timeout')).Count -or $abnormalCounters.Count -or
    @($runInfos | Where-Object { $_.outcome -in @('Aborted', 'Timeout') -or
        ($Scope -ne 'unit' -and $_.outcome -eq 'Error') -or
        $_.InnerText -match '(?i)abort|crash|timed out|Unhandled exception|WindowsStdOutHook' }).Count -or
    @($rows | Where-Object { $_.InnerText -match '(?i)TimeoutException|timed out|test host.*crash|WindowsStdOutHook' }).Count) { throw '[check:abnormal] Abnormal or liveness failure requires operator review.' }
if ($Scope -eq 'unit') {
    # xUnit theories may legitimately share one definition/testId across executions.
    $root = $doc.SelectSingleNode('/t:TestRun', $ns)
    if (-not $root -or -not $root.GetAttribute('id')) { throw 'Missing TRX run identity.' }
    $runId = $root.GetAttribute('id')
} else {
    # Keep the existing strong identity contract for Prepare/Game unchanged.
    . (Join-Path $RepositoryRoot 'scripts/GameTestBatches.ps1')
    $identity = Read-TestRunIdentity $TrxPath
    $doc = $identity.document; $ns = $identity.ns; $runId = $identity.runId
}
$summary = $doc.SelectSingleNode('/t:TestRun/t:ResultSummary', $ns)
$counters = $summary.SelectSingleNode('t:Counters', $ns)
if (-not $summary -or $summary.outcome -cnotin @('Completed', 'Failed') -or -not $counters) { throw 'Incomplete test session.' }
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
$failures = @($failed | ForEach-Object {
    $message = $_.SelectSingleNode('t:Output/t:ErrorInfo/t:Message', $ns)
    [pscustomobject]@{ test = $_.GetAttribute('testName'); message = if ($message) { $message.InnerText } else { '' } }
})
[pscustomobject]@{ classification = 'normal_test_failure'; expectedRed = $expectedRed; failed = $failed.Count; failures = $failures; runId = $runId } | ConvertTo-Json -Depth 4 -Compress
