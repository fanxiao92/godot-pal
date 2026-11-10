# Explicit, read-only classification of supplied evidence; never launches tests or grants authority.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$InputPath)
$ErrorActionPreference = 'Stop'
$utf8 = [Text.UTF8Encoding]::new($false, $true)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$report = [ordered]@{ status = 'failed'; kind = $null; classification = $null; failure = $null }
$exitCode = 1
function Fail([string]$Code, [string]$Message) { throw "[agent-evidence:$Code] $Message" }
Import-Module (Join-Path $PSScriptRoot 'AgentEvidenceJson.psm1')
function Read-Manifest([string]$Path) {
    return Read-AgentJson -Path $Path -SchemaFile (Join-Path $PSScriptRoot 'agent-evidence.schema.json')
}
function Stamp([string]$Value) {
    $stamp = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse($Value, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$stamp)) { Fail 'time' "Invalid timestamp: $Value" }
    return $stamp
}
function Resolve-Input([string]$Path) { [IO.Path]::GetFullPath($Path, $script:inputDirectory) }
function Assert-FailEchoes([string[]]$Lines, [string[]]$Names) {
    if (-not $Lines.Count) { return }
    $remaining = [Collections.Generic.List[string]]::new()
    foreach ($name in $Names) { $remaining.Add($name) }
    foreach ($line in $Lines) {
        if ($line -cnotmatch '\A\[xUnit\.net [0-9:.]+\]\s+(?<case>[^\r\n]+) \[FAIL\]\z') { Fail 'echo' 'Unexpected stderr or RunInfo text.' }
        $index = -1
        for ($i = 0; $i -lt $remaining.Count; $i++) { if ($remaining[$i] -ceq $Matches['case']) { $index = $i; break } }
        if ($index -lt 0) { Fail 'echo' 'Unbound or duplicate FAIL echo.' }
        $remaining.RemoveAt($index)
    }
    if ($remaining.Count) { Fail 'echo' 'Partial FAIL echo set.' }
}
try {
    $inputFile = [IO.Path]::GetFullPath($InputPath)
    $script:inputDirectory = Split-Path $inputFile -Parent
    $inputData = Read-Manifest $inputFile
    $report.kind = $inputData['kind']
    if ($report.kind -ceq 'derived_execution_index_not_issue_tracker') {
        $ticket = $inputData['active_ticket']; $authority = $ticket['authority']
        $null = Stamp $inputData['updatedUtc']
        if ($ticket['state'] -ceq 'running') { $null = Stamp $authority['deadlineUtc'] }
        if ($ticket['state'] -ceq 'blocked' -and $inputData['nextAction']['action'] -ceq 'dispatch') { Fail 'authority' 'Blocked state cannot dispatch.' }
        $snapshot = Resolve-Input $inputData['history']['snapshot']
        if ((Get-FileHash -LiteralPath $snapshot -Algorithm SHA256).Hash.ToLowerInvariant() -cne $inputData['history']['sha256']) { Fail 'history' 'History snapshot hash differs.' }
        $report.classification = 'current-state'
        $report.ticket = $ticket['number']; $report.state = $ticket['state']
        $report.head = $ticket['source']['head']; $report.sdkRemaining = $authority['sdkRemaining']
        $report.nextAction = $inputData['nextAction']['action']; $report.updatedUtc = $inputData['updatedUtc']
    } else {
        $native = $inputData['native']; $phase = $inputData['phase']; $expected = @($inputData['expectedFailures'])
        if ($null -eq $native['exitCode'] -or -not $native['rootExited'] -or -not $native['stdoutComplete'] -or
            -not $native['stderrComplete'] -or -not $native['cleanupComplete'] -or $native['timedOut']) {
            $exitCode = 2; Fail 'incomplete' 'Unknown exit, timeout, incomplete streams or cleanup; retain originals and stop.'
        }
        $started = Stamp $native['startedUtc']; $ended = Stamp $native['endedUtc']
        if ($ended -lt $started) { Fail 'time' 'Native finish precedes start.' }
        $expectedExit = if ($phase -ceq 'red') { 1 } else { 0 }
        if ($native['exitCode'] -ne $expectedExit) { Fail 'native' "Expected native exit $expectedExit, not $($native['exitCode'])." }
        $files = $inputData['files']
        # Decode bytes directly so BOM auto-detection cannot bypass strict UTF-8.
        $stdout = $utf8.GetString([IO.File]::ReadAllBytes((Resolve-Input $files['stdout'])))
        $stderr = $utf8.GetString([IO.File]::ReadAllBytes((Resolve-Input $files['stderr'])))
        if ($stdout.StartsWith([string][char]0xFEFF, [StringComparison]::Ordinal)) { $stdout = $stdout.Substring(1) }
        if ($stderr.StartsWith([string][char]0xFEFF, [StringComparison]::Ordinal)) { $stderr = $stderr.Substring(1) }
        foreach ($line in ($stdout -split '\r?\n')) {
            if ($line -match '(?i)(?:^|:\s+)warning(?:\s+[a-z]+[0-9]+)?\s*:') {
                if ($line -cnotin @($inputData['allowedWarnings'])) { Fail 'warning' "Unapproved warning: $line" }
            }
            if ($line -match '(?i):\s+error\s+[a-z]+[0-9]+\s*:') { Fail 'compiler' "Compiler error: $line" }
        }
        . (Join-Path $PSScriptRoot 'TestRunDefinitions.ps1')
        $identity = Read-XUnitTrxIdentity -Path (Resolve-Input $files['trx']) -Assembly (Resolve-Input $inputData['assembly'])
        $doc = $identity.document; $ns = $identity.ns
        foreach ($element in @('Times', 'Results', 'TestDefinitions', 'TestEntries', 'ResultSummary')) {
            if ($doc.SelectNodes("/t:TestRun/t:$element", $ns).Count -ne 1) { Fail 'trx' "Missing/duplicate element: $element" }
        }
        $times = $doc.SelectSingleNode('/t:TestRun/t:Times', $ns)
        foreach ($attribute in @('creation', 'queuing', 'start', 'finish')) { $null = Stamp $times.GetAttribute($attribute) }
        $trxStart = Stamp $times.GetAttribute('start'); $trxEnd = Stamp $times.GetAttribute('finish')
        if ($trxStart -lt $started -or $trxEnd -gt $ended -or $trxEnd -lt $trxStart) { Fail 'time' 'TRX times fall outside the supplied native window.' }
        $rows = @($doc.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult', $ns))
        if ($rows.Count -ne $inputData['total']) { Fail 'count' 'Unexpected total result count.' }
        $remaining = [Collections.Generic.List[object]]::new()
        foreach ($failure in $expected) { $remaining.Add($failure) }
        $failedNames = [Collections.Generic.List[string]]::new()
        foreach ($row in $rows) {
            if ($row.GetAttribute('outcome') -ceq 'Passed') { continue }
            if ($row.GetAttribute('outcome') -cne 'Failed') { Fail 'result' 'Skipped or unknown outcome.' }
            $name = $row.GetAttribute('testName')
            $message = $row.SelectSingleNode('t:Output/t:ErrorInfo/t:Message', $ns)
            $stack = $row.SelectSingleNode('t:Output/t:ErrorInfo/t:StackTrace', $ns)
            $index = -1
            for ($i = 0; $i -lt $remaining.Count; $i++) {
                $failure = $remaining[$i]
                if ($name -ceq $failure['name'] -and $message -and $stack -and
                    $message.InnerText.Contains($failure['messageContains'], [StringComparison]::Ordinal) -and
                    $stack.InnerText.Contains($failure['stackContains'], [StringComparison]::Ordinal)) { $index = $i; break }
            }
            if ($index -lt 0) { Fail 'failure' "Non-designated failure: $name" }
            $remaining.RemoveAt($index); $failedNames.Add($name)
        }
        if ($remaining.Count) { Fail 'failure' 'Designated failure missing.' }
        $summary = $doc.SelectSingleNode('/t:TestRun/t:ResultSummary', $ns)
        $summaryOutcome = if ($phase -ceq 'red') { 'Failed' } else { 'Completed' }
        if ($summary.GetAttribute('outcome') -cne $summaryOutcome -or $summary.SelectNodes('t:Counters', $ns).Count -ne 1) { Fail 'summary' 'Unexpected summary outcome or counters.' }
        $counters = $summary.SelectSingleNode('t:Counters', $ns)
        foreach ($attribute in @('total','executed','passed','failed','error','timeout','aborted','inconclusive','passedButRunAborted','notRunnable','notExecuted','disconnected','warning','completed','inProgress','pending')) {
            $count = switch ($attribute) {
                'total' { $rows.Count }; 'executed' { $rows.Count }; 'passed' { $rows.Count - $failedNames.Count }; 'failed' { $failedNames.Count }; default { 0 }
            }
            if (-not $counters.HasAttribute($attribute) -or $counters.GetAttribute($attribute) -cnotmatch '^\d+$' -or [long]$counters.GetAttribute($attribute) -ne $count) { Fail 'counter' "Unexpected counter: $attribute" }
        }
        foreach ($attribute in $counters.Attributes) {
            if ($attribute.Name -notin @('total','executed','passed','failed') -and $attribute.Value -cne '0') { Fail 'counter' 'Unknown nonzero counter.' }
        }
        $stderrLines = if ($stderr.Length) { @($stderr.TrimEnd("`r", "`n") -split '\r?\n') } else { @() }
        Assert-FailEchoes $stderrLines $failedNames.ToArray()
        $infos = @($summary.SelectNodes('t:RunInfos/t:RunInfo', $ns))
        $infoLines = @(foreach ($info in $infos) {
            $text = $info.SelectSingleNode('t:Text', $ns)
            $stamp = Stamp $info.GetAttribute('timestamp')
            if ($info.GetAttribute('outcome') -cne 'Error' -or -not $text -or $stamp -lt $trxStart -or $stamp -gt $trxEnd) { Fail 'runinfo' 'Unbound or unexpected RunInfo.' }
            $text.InnerText
        })
        Assert-FailEchoes $infoLines $failedNames.ToArray()
        $report.classification = if ($phase -ceq 'red') { 'expected-red' } else { 'green-feedback' }
        $report.total = $rows.Count; $report.failed = $failedNames.Count; $report.runId = $identity.runId
        $report.startedUtc = $native['startedUtc']; $report.endedUtc = $native['endedUtc']
    }
    $report.status = 'valid'; $exitCode = 0
} catch {
    $report.status = if ($exitCode -eq 2) { 'incomplete' } else { 'failed' }
    $report.failure = $_.Exception.Message
    [Console]::Error.WriteLine($report.failure)
}
$report | ConvertTo-Json -Depth 8
exit $exitCode
