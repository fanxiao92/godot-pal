# Shared gate metadata only. No SDK, engine, process or resource operations.
function Assert-ExactTestNames([string[]]$Actual, [string[]]$Expected, [string]$Context) {
    $actualSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $expectedSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($name in $Actual) {
        if (-not $name -or -not $actualSet.Add($name)) { throw "${Context}: empty or duplicate identity '$name'." }
    }
    foreach ($name in $Expected) {
        if (-not $name -or -not $expectedSet.Add($name)) { throw "${Context}: empty or duplicate expected identity '$name'." }
    }
    if (-not $expectedSet.Count -or -not $actualSet.SetEquals($expectedSet)) {
        $missing = @($Expected | Where-Object { -not $actualSet.Contains($_) })
        $extra = @($Actual | Where-Object { -not $expectedSet.Contains($_) })
        throw "${Context}: identity set mismatch; missing=[$($missing -join '; ')]; extra=[$($extra -join '; ')]."
    }
}

function Get-GameSourceInventory([string]$Directory) {
    $cases = [Collections.Generic.List[object]]::new()
    foreach ($file in Get-ChildItem -LiteralPath $Directory -Recurse -Filter '*.cs' -File | Sort-Object FullName) {
        $source = [IO.File]::ReadAllText($file.FullName)
        $markers = [regex]::Matches($source, '(?m)^\s*\[TestCase(?:\(|\])').Count
        if (-not $markers) { continue }
        $classes = [regex]::Matches($source, '(?m)^\s*public (?:partial )?class (\w+)')
        if ($classes.Count -ne 1 -or $source -notmatch 'namespace Pal\.Game\.Tests;') {
            throw "Unsupported Game test class/namespace: $($file.Name)."
        }
        $class = $classes[0].Groups[1].Value
        $source = $source.Replace(', RequireGodotRuntime, GodotExceptionMonitor]', ']')
        $methods = [regex]::Matches($source, '(?m)((?:\s*\[TestCase(?:\([^\r\n]*\))?\])+)[\s\r\n]*public\s+(?:async\s+)?(?:void|Task|Task<[^>]+>|System\.Threading\.Tasks\.Task)\s+(\w+)\s*\(')
        $parsed = 0
        foreach ($method in $methods) {
            $attributes = [regex]::Matches($method.Groups[1].Value, '\[TestCase(?:\(([^\r\n]*)\))?\]')
            $methodName = $method.Groups[2].Value
            for ($index = 0; $index -lt $attributes.Count; $index++) {
                $values = [Collections.Generic.List[string]]::new()
                $attribute = $attributes[$index].Groups[1].Value
                foreach ($token in [regex]::Matches($attribute, '(?:"(?:\\.|[^"\\])*"|[^,"])+')) {
                    $argument = $token.Value.Trim()
                    if (-not $argument -or $argument -match '^Timeout\s*=\s*\d+$') { continue }
                    if ($argument -match '^"(?:\\.|[^"\\])*"$' -or $argument -match '^-?\d+$') { $value = $argument }
                    else {
                        $keys = @{ 'Key.Up' = '4194320'; 'Key.Pageup' = '4194323'; 'Key.Space' = '32'; 'Key.Escape' = '4194305' }
                        if (-not $keys.ContainsKey($argument)) { throw "Unsupported Game test argument '$argument': $($file.Name)." }
                        $value = $keys[$argument]
                    }
                    # This pinned adapter replaces periods in parameter display text with commas.
                    # Keep source identities independent, but use its observed TRX/discovery spelling.
                    $values.Add($value.Replace('.', ','))
                }
                $display = $methodName
                $prefix = "Pal.Game.Tests.$class."
                if ($values.Count) {
                    $display += ':' + $index + ' (' + ($values -join ', ') + ')'
                    $prefix += $methodName + '.'
                }
                $cases.Add([pscustomobject]@{ name = $prefix + $display; display = $display; class = $class; source = $file.Name })
                $parsed++
            }
        }
        if ($parsed -ne $markers) { throw "Unparsed Game test attributes in $($file.Name): $parsed/$markers." }
    }
    Assert-ExactTestNames @($cases.name) @($cases.name) 'Game source inventory'
    return $cases.ToArray()
}

function Get-GameBatchPlan([string]$Repository) {
    $inventory = @(Get-GameSourceInventory (Join-Path $Repository 'src/Pal.Game/Tests'))
    $definitions = @(
        @{ name = 'process'; count = 66; classes = @('MainProcessTests', 'ExplorationProcessTests') }
        @{ name = 'scene-dialogue'; count = 79; classes = @('ExplorationSceneTests', 'MainSceneTests', 'NpcInteractionHostTests', 'DialogueDisplayFixtureTests', 'TriggerDialogueFixtureTests') }
        @{ name = 'native-map'; count = 97; classes = @('NativeExplorationProfileTests', 'PalMapSceneTests', 'ViewportTests') }
    )
    $batches = foreach ($definition in $definitions) {
        $names = @($inventory | Where-Object class -CIn $definition.classes | ForEach-Object name)
        if ($names.Count -ne $definition.count) { throw "Batch $($definition.name) source count differs: $($names.Count)/$($definition.count)." }
        [pscustomobject]@{ name = $definition.name; count = $definition.count; classes = $definition.classes; expected = $names
            filter = ($definition.classes | ForEach-Object { "FullyQualifiedName~Pal.Game.Tests.$_." }) -join '|' }
    }
    $controls = @($inventory | Where-Object class -CEQ 'FailureControlTests')
    $expectedControls = @('Pal.Game.Tests.FailureControlTests.IntentionalAssertionMismatchPropagates',
        'Pal.Game.Tests.FailureControlTests.IntentionalTimeoutPropagates', 'Pal.Game.Tests.FailureControlTests.NativeReadyExceptionIsNotSwallowed')
    Assert-ExactTestNames @($controls.name) $expectedControls 'Game controls'
    Assert-ExactTestNames @($batches.expected) @($inventory | Where-Object class -CNE 'FailureControlTests' | ForEach-Object name) 'Game partition'
    if ($inventory.Count -ne 245) { throw "Game inventory differs: $($inventory.Count)/245." }
    # Explicit serial project execution replaces solution test, not solution restore/build.
    $projects = @(
        [pscustomobject]@{ name = 'core'; path = 'tests/Pal.Core.Tests/Pal.Core.Tests.csproj'; passed = 379; skipped = 0 }
        [pscustomobject]@{ name = 'cli-unit'; path = 'tests/Pal.Extract.UnitTests/Pal.Extract.UnitTests.csproj'; passed = 142; skipped = 0 }
        [pscustomobject]@{ name = 'cli-integration'; path = 'tests/Pal.Extract.IntegrationTests/Pal.Extract.IntegrationTests.csproj'; passed = 247; skipped = 2 }
        [pscustomobject]@{ name = 'prepare'; path = 'tests/Pal.Prepare.Tests/Pal.Prepare.Tests.csproj'; passed = 52; skipped = 0 }
        [pscustomobject]@{ name = 'game'; path = 'src/Pal.Game/Pal.Game.csproj'; passed = 242; skipped = 0 }
    )
    $allProjects = [Collections.Generic.List[string]]::new()
    $solutionProjects = foreach ($match in [regex]::Matches([IO.File]::ReadAllText((Join-Path $Repository 'GodotPal.sln')), '"([^"\r\n]+\.csproj)"')) {
        $relative = $match.Groups[1].Value.Replace('\', '/')
        $allProjects.Add($relative)
        [xml]$project = [IO.File]::ReadAllText((Join-Path $Repository $relative))
        if (@($project.Project.PropertyGroup.IsTestProject | Where-Object { $_ -eq 'true' }).Count) { $relative }
    }
    Assert-ExactTestNames @($allProjects) @($projects.path + @('src/Pal.Core/Pal.Core.csproj', 'tools/Pal.Extract/Pal.Extract.csproj')) 'Solution project classification'
    Assert-ExactTestNames @($solutionProjects) @($projects.path) 'Solution test projects'
    [pscustomobject]@{ inventory = $inventory; batches = @($batches); controls = $controls; projects = $projects }
}

function Get-GatePassSummary($Plan) {
    $projects = @{}
    foreach ($name in @('core', 'cli-unit', 'cli-integration', 'prepare', 'game')) {
        $project = @($Plan.projects | Where-Object name -CEQ $name)
        if ($project.Count -ne 1) { throw "Gate PASS summary requires exactly one '$name' project." }
        $projects[$name] = $project[0]
    }
    foreach ($name in @('core', 'cli-unit', 'prepare', 'game')) {
        if ([int]$projects[$name].skipped -ne 0) { throw "Gate PASS summary cannot omit skips for '$name'." }
    }
    $batchCounts = @($Plan.batches | ForEach-Object { [int]$_.count })
    $positiveCount = ($batchCounts | Measure-Object -Sum).Sum
    if ($positiveCount -ne [int]$projects.game.passed) { throw 'Gate PASS summary Game project and batch counts differ.' }
    if (@($Plan.inventory).Count -ne ($positiveCount + @($Plan.controls).Count)) { throw 'Gate PASS summary Game inventory and partition counts differ.' }

    $cliPassed = [int]$projects['cli-integration'].passed
    $cliSkipped = [int]$projects['cli-integration'].skipped
    $roundCount = 3
    $trxCount = @($Plan.projects | Where-Object name -CNE 'game').Count + ($batchCounts.Count * $roundCount) + @($Plan.controls).Count
    "PASS: eight real-process dotnet-boundary prechecks; empty-cache restore/build; Core $($projects.core.passed) passed; CLI unit $($projects['cli-unit'].passed) passed; CLI integration $cliPassed passed + $cliSkipped skipped ($($cliPassed + $cliSkipped) total); Prepare $($projects.prepare.passed) passed; Godot source/discovery $(@($Plan.inventory).Count); positives $positiveCount as serial $($batchCounts -join '/') in each of three complete rounds; $trxCount identity-verified TRX; $(@($Plan.controls).Count) separately verified 1-failure controls; native prepare/profile/import/Main startup. Headless automation only; real GPU/manual desktop acceptance is separate; this PASS alone does not establish remote-CI execution identity."
}

function Read-TestRunIdentity([string]$Path) {
    $document = [xml]::new()
    $document.XmlResolver = $null
    $document.Load($Path)
    $ns = [Xml.XmlNamespaceManager]::new($document.NameTable)
    $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    $root = $document.SelectSingleNode('/t:TestRun', $ns)
    if (-not $root -or -not $root.GetAttribute('id')) { throw "Missing TRX run identity: $Path." }
    $rows = @($root.SelectNodes('t:Results/t:UnitTestResult', $ns))
    $definitions = @($root.SelectNodes('t:TestDefinitions/t:UnitTest', $ns))
    $entries = @($root.SelectNodes('t:TestEntries/t:TestEntry', $ns))
    if (-not $rows.Count -or $rows.Count -ne $definitions.Count -or $rows.Count -ne $entries.Count) { throw "Incomplete TRX identity graph: $Path." }
    $byId = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($definition in $definitions) {
        $id = $definition.GetAttribute('id')
        $method = $definition.SelectSingleNode('t:TestMethod', $ns)
        $execution = $definition.SelectSingleNode('t:Execution', $ns)
        if (-not $id -or -not $method -or -not $execution -or $byId.ContainsKey($id) -or
            -not $method.GetAttribute('className') -or -not $method.GetAttribute('name') -or -not $definition.GetAttribute('name')) {
            throw "Invalid TRX definition identity: $Path."
        }
        $name = $definition.GetAttribute('name')
        if ($method.GetAttribute('adapterTypeName') -eq 'executor://gdunit4.testadapter/') {
            $name = $method.GetAttribute('className') + '.' + $method.GetAttribute('name')
        }
        $byId.Add($id, @{ name = $name; execution = $execution.GetAttribute('id') })
    }
    $rowIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $executionIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $names = [Collections.Generic.List[string]]::new()
    foreach ($row in $rows) {
        $id = $row.GetAttribute('testId'); $execution = $row.GetAttribute('executionId'); $name = $row.GetAttribute('testName')
        if (-not $id -or -not $execution -or -not $name -or -not $rowIds.Add($id) -or -not $executionIds.Add($execution) -or
            -not $byId.ContainsKey($id) -or $byId[$id].execution -cne $execution -or $byId[$id].name -cne $name) {
            throw "TRX result/definition mismatch or duplicate identity: $Path."
        }
        $names.Add($name)
    }
    # Case IDs identify rows. xUnit may truncate distinct byte-array data to the same display.
    # Game's source-derived FQNs are still required to be unique by Assert-GameBatch.
    $entryIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $entries) {
        $id = $entry.GetAttribute('testId')
        if (-not $entryIds.Add($id) -or -not $byId.ContainsKey($id) -or $byId[$id].execution -cne $entry.GetAttribute('executionId')) {
            throw "TRX entry mismatch or duplicate identity: $Path."
        }
    }
    if (-not $entryIds.SetEquals($rowIds)) { throw "TRX entry/result identity sets differ: $Path." }
    [pscustomobject]@{ runId = $root.GetAttribute('id'); names = $names.ToArray(); executionIds = @($executionIds); document = $document; ns = $ns }
}

function Assert-GameBatch([string]$Path, [string[]]$Expected) {
    $identity = Read-TestRunIdentity $Path
    $summary = $identity.document.SelectSingleNode('/t:TestRun/t:ResultSummary', $identity.ns)
    if (-not $summary) { throw "Game session incomplete, missing summary: $Path." }
    $counters = $summary.SelectSingleNode('t:Counters', $identity.ns)
    if ($summary.GetAttribute('outcome') -cne 'Completed' -or -not $counters -or
        @($summary.SelectNodes('t:RunInfos/t:RunInfo', $identity.ns) | Where-Object {
            $_.GetAttribute('outcome') -in @('Error', 'Aborted', 'Timeout') -or $_.InnerText -match '(?i)Aborting test run|Test Run Aborted|test run timeout|crashed|Unhandled exception|WindowsStdOutHook'
        }).Count) { throw "Game session incomplete: $Path." }
    foreach ($attribute in $counters.Attributes) {
        $expectedCount = if ($attribute.Name -in @('total', 'executed', 'passed')) { $Expected.Count } else { 0 }
        if ([int]$attribute.Value -ne $expectedCount) { throw "Game counter $($attribute.Name) differs: $Path." }
    }
    foreach ($required in @('total', 'executed', 'passed', 'failed', 'error', 'timeout', 'aborted', 'notExecuted')) {
        if (-not $counters.HasAttribute($required)) { throw "Missing Game counter ${required}: $Path." }
    }
    if (@($identity.document.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult', $identity.ns) | Where-Object { $_.GetAttribute('outcome') -cne 'Passed' }).Count) {
        throw "Non-passing Game result: $Path."
    }
    Assert-ExactTestNames $identity.names $Expected "Game batch $Path"
    return $identity
}

function Assert-GameDiscovery([string]$Text, $Plan) {
    # Pinned VSTest header/adapter format: consume every nonempty line after the header.
    # A restrictive method-name regex would silently discard unknown or Unicode extra tests.
    $sections = [regex]::Split($Text, '(?m)^The following Tests are available:\r?$')
    if ($sections.Count -ne 2) { throw 'Unparsed Game discovery header.' }
    $actual = @(foreach ($line in $sections[1] -split "`n") {
        if (-not $line.Trim()) { continue }
        if ($line -notmatch '^    \S.*$') { throw "Game discovery unparsed line: $line" }
        $line.Trim()
    })
    Assert-ExactTestNames $actual @($Plan.inventory.display) 'Game discovery'
    return $actual
}

function Assert-GameRound($BatchResults, $Plan, [string]$Round) {
    if (@($BatchResults).Count -ne $Plan.batches.Count) { throw "Incomplete Game round ${Round}." }
    Assert-ExactTestNames @($BatchResults.name) @($Plan.batches.name) "Game round $Round batches"
    foreach ($batch in $Plan.batches) {
        $actual = @($BatchResults | Where-Object name -CEQ $batch.name)
        Assert-ExactTestNames @($actual.tests) $batch.expected "Game round $Round/$($batch.name)"
    }
    Assert-ExactTestNames @($BatchResults.tests) @($Plan.batches.expected) "Game round $Round union"
}
