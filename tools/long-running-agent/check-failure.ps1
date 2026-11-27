param(
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
# Ordinary assertion RunInfo Error (xUnit [FAIL], GdUnit "Expecting") is not an abnormal session in any scope.
if (($summary -and $summary.outcome -in @('Aborted', 'Timeout')) -or
    @($rows | Where-Object outcome -In @('Aborted', 'Timeout')).Count -or $abnormalCounters.Count -or
    @($runInfos | Where-Object { $_.outcome -in @('Aborted', 'Timeout') -or
        $_.InnerText -match '(?i)abort|crash|timed out|Unhandled exception|WindowsStdOutHook' }).Count -or
    @($rows | Where-Object { $_.InnerText -match '(?i)TimeoutException|timed out|test host.*crash|WindowsStdOutHook' }).Count) { throw '[check:abnormal] Abnormal or liveness failure requires operator review.' }
$root = $doc.SelectSingleNode('/t:TestRun', $ns)
if (-not $root -or -not $root.GetAttribute('id')) { throw 'Missing TRX run identity.' }
$runId = $root.GetAttribute('id')
if ($Scope -ne 'unit') {
    # Self-contained strong TRX identity validation for Prepare/Game (xUnit theories in Unit may share one definition/testId).
    $definitions = @($root.SelectNodes('t:TestDefinitions/t:UnitTest', $ns))
    $entries = @($root.SelectNodes('t:TestEntries/t:TestEntry', $ns))
    if (-not $rows.Count -or $rows.Count -ne $definitions.Count -or $rows.Count -ne $entries.Count) {
        throw "Incomplete TRX identity graph: $TrxPath."
    }
    $byId = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($definition in $definitions) {
        $id = $definition.GetAttribute('id')
        $method = $definition.SelectSingleNode('t:TestMethod', $ns)
        $execution = $definition.SelectSingleNode('t:Execution', $ns)
        if (-not $id -or -not $method -or -not $execution -or $byId.ContainsKey($id) -or
            -not $method.GetAttribute('className') -or -not $method.GetAttribute('name') -or -not $definition.GetAttribute('name')) {
            throw "Invalid TRX definition identity: $TrxPath."
        }
        $name = $definition.GetAttribute('name')
        if ($method.GetAttribute('adapterTypeName') -eq 'executor://gdunit4.testadapter/') {
            $name = $method.GetAttribute('className') + '.' + $method.GetAttribute('name')
        }
        $byId.Add($id, @{ name = $name; execution = $execution.GetAttribute('id') })
    }
    $rowIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $executionIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($row in $rows) {
        $id = $row.GetAttribute('testId'); $execution = $row.GetAttribute('executionId'); $name = $row.GetAttribute('testName')
        if (-not $id -or -not $execution -or -not $name -or -not $rowIds.Add($id) -or -not $executionIds.Add($execution) -or
            -not $byId.ContainsKey($id) -or $byId[$id].execution -cne $execution -or $byId[$id].name -cne $name) {
            throw "TRX result/definition mismatch or duplicate identity: $TrxPath."
        }
    }
    $entryIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $entries) {
        $id = $entry.GetAttribute('testId')
        if (-not $entryIds.Add($id) -or -not $byId.ContainsKey($id) -or $byId[$id].execution -cne $entry.GetAttribute('executionId')) {
            throw "TRX entry mismatch or duplicate identity: $TrxPath."
        }
    }
    if (-not $entryIds.SetEquals($rowIds)) { throw "TRX entry/result identity sets differ: $TrxPath." }
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
