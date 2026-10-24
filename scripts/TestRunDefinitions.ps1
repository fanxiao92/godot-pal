# Definitions-only shared test identity readers. This file performs no process, SDK, or file writes.
function Read-Version7Discovery {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$DiagnosticPath,
        [Parameter(Mandatory)][string]$Assembly
    )
    if (-not [IO.File]::Exists($DiagnosticPath)) { throw "Missing discovery diagnostics: $DiagnosticPath" }
    $cases = [Collections.Generic.List[object]]::new()
    $completed = $null
    $warnings = [Collections.Generic.List[string]]::new()
    foreach ($line in [IO.File]::ReadLines($DiagnosticPath, [Text.UTF8Encoding]::new($false))) {
        if ($line -notmatch 'TestRequestSender.OnDiscoveryMessageReceived: Received message: (.*)$') { continue }
        $message = $Matches[1] | ConvertFrom-Json
        if ($message.Version -ne 7) { throw "Unsupported VSTest diagnostic protocol (expected Version 7): $Name" }
        switch -CaseSensitive ($message.MessageType) {
            'TestDiscovery.TestFound' {
                if ($null -ne $completed) { throw "Cases arrived after discovery completion: $Name" }
                foreach ($case in @($message.Payload)) { $cases.Add($case) }
            }
            'TestDiscovery.Completed' {
                if ($null -ne $completed -or $message.Payload.IsAborted -ne $false) { throw "Duplicate or aborted discovery completion: $Name" }
                $completed = $message.Payload
                foreach ($case in @($completed.LastDiscoveredTests)) {
                    if ($null -ne $case) { $cases.Add($case) }
                }
            }
            'TestSession.Message' {
                if ($message.Payload.MessageLevel -ne 0) { $warnings.Add([string]$message.Payload.Message) }
            }
            default { throw "Unrecognized discovery message: $($message.MessageType)" }
        }
    }
    if ($null -eq $completed -or $cases.Count -ne $completed.TotalTests) { throw "Missing/incomplete discovery completion or count mismatch: $Name" }
    if (-not $cases.Count) { throw "Zero discovery: $Name" }
    if ($warnings.Count) { throw ('Discovery warning/error: ' + ($warnings -join '; ')) }
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($case in $cases) {
        $serialization = @($case.Properties | Where-Object { $_.Key.Id -ceq 'XunitTestCaseSerialization' })
        $unique = @($case.Properties | Where-Object { $_.Key.Id -ceq 'XunitTestCaseUniqueID' })
        $guid = [guid]::Empty
        if (-not [guid]::TryParse($case.Id, [ref]$guid) -or $guid -eq [guid]::Empty -or
            -not $ids.Add([string]$case.Id) -or -not $case.FullyQualifiedName -or -not $case.DisplayName -or
            [IO.Path]::GetFullPath($case.Source) -cne [IO.Path]::GetFullPath($Assembly) -or
            $case.ExecutorUri -cne 'executor://xunit/VsTestRunner3/netcore/' -or
            $serialization.Count -ne 1 -or -not $serialization[0].Value -or
            $unique.Count -ne 1 -or -not $unique[0].Value) { throw "Invalid/incomplete xUnit identity: $Name" }
    }
    [pscustomobject]@{
        name = $Name
        assembly = [IO.Path]::GetFullPath($Assembly)
        sha256 = (Get-FileHash -LiteralPath $Assembly -Algorithm SHA256).Hash
        total = $cases.Count
        completed = $completed
        cases = $cases.ToArray()
    }
}

function Assert-Version7TrxRun {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)]$Discovery,
        [string[]]$AllowedSkippedIds = @(),
        [string[]]$AllowedSkippedNames = @()
    )
    $identity = Read-TestRunIdentity $Path
    $doc = $identity.document; $ns = $identity.ns
    foreach ($element in @('Times','Results','TestDefinitions','TestEntries','ResultSummary')) {
        if ($doc.SelectNodes("/t:TestRun/t:$element", $ns).Count -ne 1) { throw "Missing/duplicate TRX element: $element" }
    }
    $times = $doc.SelectSingleNode('/t:TestRun/t:Times', $ns)
    if (-not $times) { throw "Missing TRX Times: $Path" }
    foreach ($attribute in @('creation', 'queuing', 'start', 'finish')) {
        $stamp = [DateTimeOffset]::MinValue
        if (-not [DateTimeOffset]::TryParse($times.GetAttribute($attribute), [ref]$stamp)) { throw "Invalid TRX timestamp '$attribute': $Path" }
    }
    if ([DateTimeOffset]::Parse($times.GetAttribute('finish')) -lt [DateTimeOffset]::Parse($times.GetAttribute('start'))) { throw "TRX finish precedes start: $Path" }
    foreach ($id in @($identity.runId) + @($identity.executionIds)) {
        $guid = [guid]::Empty
        if (-not [guid]::TryParse($id, [ref]$guid) -or $guid -eq [guid]::Empty) { throw "Invalid TRX GUID identity: $Path" }
    }
    $summary = $doc.SelectSingleNode('/t:TestRun/t:ResultSummary', $ns)
    $counters = $summary.SelectSingleNode('t:Counters', $ns)
    if ($summary.GetAttribute('outcome') -cne 'Completed' -or -not $counters) { throw "Incomplete summary: $Path" }
    $rows = @($doc.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult', $ns))
    if ($rows.Count -ne $Discovery.total -or $rows.Count -eq 0) { throw "TRX execution count differs from frozen discovery: $Path" }
    $expectedTotal = $Discovery.total
    $allowedIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($case in $Discovery.cases) {
        if ($case.Id -in $AllowedSkippedIds -or $case.FullyQualifiedName -in $AllowedSkippedNames -or $case.DisplayName -in $AllowedSkippedNames) { $null = $allowedIds.Add([string]$case.Id) }
    }
    # Eligibility to skip is not evidence that a skip happened. Count actual rows;
    # xUnit emits NotExecuted rows but may leave Counters.notExecuted at zero.
    $skipped = @($rows | Where-Object { $_.GetAttribute('outcome') -ceq 'NotExecuted' })
    foreach ($row in $skipped) {
        if (-not $allowedIds.Contains($row.GetAttribute('testId'))) { throw "Unexpected skipped case: $($row.GetAttribute('testId'))" }
    }
    # The pinned xUnit adapter emits Warning RunInfo for each platform skip.
    # Permit only an exact warning for an eligible, actually NotExecuted row;
    # arbitrary warnings, errors, duplicates and stale timestamps remain failures.
    $skipNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($row in $skipped) { $null = $skipNames.Add($row.GetAttribute('testName')) }
    $seenWarnings = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($info in $summary.SelectNodes('t:RunInfos/t:RunInfo', $ns)) {
        $text = $info.SelectSingleNode('t:Text', $ns)
        if ($info.GetAttribute('outcome') -cne 'Warning' -or -not $text -or
            $text.InnerText -cnotmatch '\A\[xUnit\.net \d{2,}:\d{2}:\d{2}\.\d{2}\]\s+(?<case>[^\r\n]+) \[SKIP\]\z') { throw "Unexpected RunInfo: $Path" }
        $name = $Matches['case']; $stamp = [DateTimeOffset]::MinValue
        if (-not $skipNames.Contains($name) -or -not $seenWarnings.Add($name) -or
            -not [DateTimeOffset]::TryParse($info.GetAttribute('timestamp'), [ref]$stamp) -or
            $stamp -lt [DateTimeOffset]::Parse($times.start) -or $stamp -gt [DateTimeOffset]::Parse($times.finish)) { throw "Unbound/duplicate/stale skip RunInfo: $Path" }
    }
    $passed = @($rows | Where-Object { $_.GetAttribute('outcome') -ceq 'Passed' }).Count
    if ($passed -le 0 -or $passed + $skipped.Count -ne $rows.Count) { throw "Non-passing or zero executed result: $Path" }
    foreach ($attribute in @('total','executed','passed','failed','error','timeout','aborted','inconclusive','passedButRunAborted','notRunnable','notExecuted','disconnected','warning','completed','inProgress','pending')) {
        if (-not $counters.HasAttribute($attribute) -or $counters.GetAttribute($attribute) -notmatch '^\d+$') { throw "Missing/invalid counter: $attribute" }
        $value = [long]$counters.GetAttribute($attribute)
        if ($attribute -eq 'notExecuted') {
            if ($value -notin @(0, $skipped.Count)) { throw 'Counter notExecuted differs from actual skipped rows' }
        } else {
            $expected = if ($attribute -eq 'total') { $expectedTotal } elseif ($attribute -in @('executed','passed')) { $passed } else { 0 }
            if ($value -ne $expected) { throw "Counter $attribute differs: expected $expected, actual $value" }
        }
    }
    foreach ($attribute in $counters.Attributes) {
        # Preserve the original Unit contract: additional zero counters are benign,
        # but an unknown nonzero (or nonnumeric) counter cannot hide bad completion.
        if ($attribute.Name -notin @('total','executed','passed','notExecuted') -and $attribute.Value -cne '0') { throw "Unexpected nonzero TRX counter: $($attribute.Name)" }
    }
    $expectedIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($case in $Discovery.cases) { $null = $expectedIds.Add([string]$case.Id) }
    $actualIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($row in $rows) {
        if (-not $actualIds.Add($row.GetAttribute('testId')) -or -not $expectedIds.Contains($row.GetAttribute('testId'))) { throw "TRX case IDs differ from frozen discovery: $Path" }
        $case = @($Discovery.cases | Where-Object Id -CEQ $row.GetAttribute('testId'))[0]
        $definition = @($doc.SelectNodes('/t:TestRun/t:TestDefinitions/t:UnitTest', $ns) | Where-Object id -CEQ $row.GetAttribute('testId'))[0]
        $method = $definition.SelectSingleNode('t:TestMethod', $ns)
        if ($row.GetAttribute('testName') -cne $case.DisplayName -or ($method.className + '.' + $method.name) -cne $case.FullyQualifiedName -or -not $method.codeBase -or [IO.Path]::GetFullPath($method.codeBase) -cne [IO.Path]::GetFullPath($Discovery.assembly)) { throw "TRX case display/FQN/assembly differs from discovery: $Path" }
        if ($row.GetAttribute('outcome') -ceq 'NotExecuted') {
            if (-not $allowedIds.Contains($row.GetAttribute('testId'))) { throw "Unexpected skipped case: $($row.GetAttribute('testId'))" }
        } elseif ($row.GetAttribute('outcome') -cne 'Passed') { throw "Non-passing result: $($row.GetAttribute('outcome'))" }
    }
    if (-not $actualIds.SetEquals($expectedIds)) { throw "TRX case IDs missing or extra: $Path" }
    [pscustomobject]@{ identity = $identity; runId = $identity.runId; executionIds = $identity.executionIds; cases = $rows; fqn = @($identity.names) }
}
