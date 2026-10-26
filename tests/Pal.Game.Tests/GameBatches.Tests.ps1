# Offline metadata regression: synthetic C#/XML/text only. No SDK, Godot, CIM or network.
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $repository 'scripts/GameTestBatches.ps1')
$temp = Join-Path ([IO.Path]::GetTempPath()) ('pal-batch-metadata-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $temp | Out-Null
$results = [Collections.Generic.List[string]]::new()
function Assert([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Fails([scriptblock]$Action, [string]$Pattern) {
    $message = $null
    try { & $Action | Out-Null } catch { $message = $_.Exception.Message }
    Assert ($message -and $message -match $Pattern) "Expected '$Pattern', got '$message'."
}
function Case([string]$Name, [scriptblock]$Action) { & $Action; $results.Add($Name); Write-Host "PASS: $Name" }
$expected = @('Pal.Game.Tests.Fixture.Alpha', 'Pal.Game.Tests.Fixture.Beta')
$valid = @'
<TestRun id="33333333-3333-3333-3333-333333333333" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
    <UnitTestResult testId="aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" executionId="11111111-1111-1111-1111-111111111111" testName="Pal.Game.Tests.Fixture.Alpha" outcome="Passed" />
    <UnitTestResult testId="bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb" executionId="22222222-2222-2222-2222-222222222222" testName="Pal.Game.Tests.Fixture.Beta" outcome="Passed" />
  </Results>
  <TestDefinitions>
    <UnitTest id="aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" name="Alpha"><Execution id="11111111-1111-1111-1111-111111111111"/><TestMethod adapterTypeName="executor://gdunit4.testadapter/" className="Pal.Game.Tests.Fixture" name="Alpha"/></UnitTest>
    <UnitTest id="bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb" name="Beta"><Execution id="22222222-2222-2222-2222-222222222222"/><TestMethod adapterTypeName="executor://gdunit4.testadapter/" className="Pal.Game.Tests.Fixture" name="Beta"/></UnitTest>
  </TestDefinitions>
  <TestEntries>
    <TestEntry testId="aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" executionId="11111111-1111-1111-1111-111111111111"/>
    <TestEntry testId="bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb" executionId="22222222-2222-2222-2222-222222222222"/>
  </TestEntries>
  <ResultSummary outcome="Completed"><Counters total="2" executed="2" passed="2" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0"/></ResultSummary>
</TestRun>
'@
function Fixture([string]$Text) {
    $path = Join-Path $temp ([guid]::NewGuid().ToString('N') + '.trx')
    [IO.File]::WriteAllText($path, $Text)
    return $path
}
try {
    $plan = Get-GameBatchPlan $repository
    Case 'literal source identities, enum values and parameter indices' {
        $source = Join-Path $temp 'source'
        New-Item -ItemType Directory $source | Out-Null
        [IO.File]::WriteAllText((Join-Path $source 'Fixture.cs'), @'
namespace Pal.Game.Tests;
public class Fixture
{
    [TestCase("a,b", "second.file", Key.Space, Timeout = 15000)]
    [TestCase("other", Key.Escape, Timeout = 15000)]
    public async Task Alpha(string text, Key key) { }
    [TestCase]
    public void Beta() { }
}
'@)
        $inventory = @(Get-GameSourceInventory $source)
        Assert-ExactTestNames $inventory.name @('Pal.Game.Tests.Fixture.Alpha.Alpha:0 ("a,b", "second,file", 32)', 'Pal.Game.Tests.Fixture.Alpha.Alpha:1 ("other", 4194305)', 'Pal.Game.Tests.Fixture.Beta') 'literal source'
        $text = [IO.File]::ReadAllText((Join-Path $source 'Fixture.cs'))
        [IO.File]::WriteAllText((Join-Path $source 'Fixture.cs'), $text.Replace('Key.Space', 'Key.Unknown'))
        Fails { Get-GameSourceInventory $source } 'Unsupported Game test argument'
        [IO.File]::WriteAllText((Join-Path $source 'Fixture.cs'), $text.Replace('public void Beta()', 'public ValueTask Beta()'))
        Fails { Get-GameSourceInventory $source } 'Unparsed Game test attributes'
    }
    Case 'real source partitions 66/79/97, preserves controls and solution test projects' {
        Assert ((($plan.batches | ForEach-Object count) -join ',') -ceq '66,79,97') 'Wrong batch sizes/order.'
        Assert ($plan.inventory.Count -eq 245 -and $plan.controls.Count -eq 3 -and $plan.projects.Count -eq 5) 'Incomplete source inventory.'
        Assert (($plan.projects.name -join ',') -ceq 'core,cli-unit,cli-integration,prepare,game') 'Wrong serial project identities/order.'
        Assert-ExactTestNames @($plan.projects.path) @('tests/Pal.Core.Tests/Pal.Core.Tests.csproj',
            'tests/Pal.Extract.UnitTests/Pal.Extract.UnitTests.csproj', 'tests/Pal.Extract.IntegrationTests/Pal.Extract.IntegrationTests.csproj',
            'tests/Pal.Prepare.Tests/Pal.Prepare.Tests.csproj', 'src/Pal.Game/Pal.Game.csproj') 'literal test project paths'
        $expectedProjects = @{
            core = @('tests/Pal.Core.Tests/Pal.Core.Tests.csproj', 328, 0)
            'cli-unit' = @('tests/Pal.Extract.UnitTests/Pal.Extract.UnitTests.csproj', 142, 0)
            'cli-integration' = @('tests/Pal.Extract.IntegrationTests/Pal.Extract.IntegrationTests.csproj', 247, 2)
            prepare = @('tests/Pal.Prepare.Tests/Pal.Prepare.Tests.csproj', 52, 0)
            game = @('src/Pal.Game/Pal.Game.csproj', 242, 0)
        }
        foreach ($project in $plan.projects) {
            $expectedProject = $expectedProjects[$project.name]
            Assert ($project.path -ceq $expectedProject[0] -and $project.passed -eq $expectedProject[1] -and $project.skipped -eq $expectedProject[2]) "Wrong binding for $($project.name)."
        }
        Assert ($plan.batches[0].filter -ceq 'FullyQualifiedName~Pal.Game.Tests.MainProcessTests.|FullyQualifiedName~Pal.Game.Tests.ExplorationProcessTests.') 'Filter boundary lost.'
        Assert-ExactTestNames @($plan.batches.expected) @($plan.inventory | Where-Object class -NE 'FailureControlTests' | ForEach-Object name) 'all source positives'
    }
    Case 'gate PASS summary exactly reports the verified plan metadata' {
        $summary = Get-GatePassSummary $plan
        $expectedSummary = 'PASS: eight real-process dotnet-boundary prechecks; empty-cache restore/build; Core 328 passed; CLI unit 142 passed; CLI integration 247 passed + 2 skipped (249 total); Prepare 52 passed; Godot source/discovery 245; positives 242 as serial 66/79/97 in each of three complete rounds; 16 identity-verified TRX; 3 separately verified 1-failure controls; native prepare/profile/import/Main startup. Headless automation only; real GPU/manual desktop acceptance is separate.'
        Assert ($summary -ceq $expectedSummary) "Wrong gate PASS summary: $summary"
    }
    Case 'gate PASS summary follows changed plan counts instead of static CLI text' {
        $cli = @($plan.projects | Where-Object name -CEQ 'cli-integration')[0]
        $originalPassed = $cli.passed
        $originalSkipped = $cli.skipped
        try {
            $cli.passed = 246
            $cli.skipped = 3
            $summary = Get-GatePassSummary $plan
            Assert ($summary -match 'CLI integration 246 passed \+ 3 skipped \(249 total\)') 'Changed CLI plan counts were not rendered.'
            Assert ($summary -notmatch 'CLI integration 247|CLI integration 188') 'Static CLI plan count remained in changed summary.'
        } finally {
            $cli.passed = $originalPassed
            $cli.skipped = $originalSkipped
        }
    }
    Case 'Test-M0 writes its PASS summary through the shared plan formatter' {
        $gateSource = [IO.File]::ReadAllText((Join-Path $repository 'scripts/Test-M0.ps1'))
        Assert ($gateSource -match '(?m)^\s*Get-GatePassSummary \$gamePlan \|\s*$') 'Test-M0 does not pipe the verified game plan through Get-GatePassSummary.'
        Assert ($gateSource -notmatch 'CLI integration\s+\d') 'Test-M0 retains a static CLI integration count.'
    }
    Case 'unassigned source classes and new solution projects fail closed' {
        $fixtureRepo = Join-Path $temp 'repository'
        foreach ($relative in @($plan.projects.path + @('src/Pal.Core/Pal.Core.csproj', 'tools/Pal.Extract/Pal.Extract.csproj', 'GodotPal.sln') + @($plan.inventory.source | Select-Object -Unique | ForEach-Object { "src/Pal.Game/Tests/$_" }))) {
            $destination = Join-Path $fixtureRepo $relative
            New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($destination)) | Out-Null
            [IO.File]::Copy((Join-Path $repository $relative), $destination)
        }
        $extra = Join-Path $fixtureRepo 'src/Pal.Game/Tests/ExtraTests.cs'
        [IO.File]::WriteAllText($extra, "namespace Pal.Game.Tests;`npublic class ExtraTests {`n[TestCase]`npublic void Unassigned() {} }")
        Fails { Get-GameBatchPlan $fixtureRepo } 'Game partition: identity set mismatch'
        Remove-Item -LiteralPath $extra
        $solution = Join-Path $fixtureRepo 'GodotPal.sln'
        $originalSolution = [IO.File]::ReadAllText($solution)
        foreach ($project in $plan.projects) {
            $windowsPath = $project.path.Replace('/', '\')
            # Game alone has a dependency section; removing its path still tests omission.
            $omitted = $originalSolution.Replace('"' + $windowsPath + '"', '"omitted-project"')
            [IO.File]::WriteAllText($solution, $omitted)
            Fails { Get-GameBatchPlan $fixtureRepo } 'Solution project classification: identity set mismatch'
            [IO.File]::WriteAllText($solution, $originalSolution + "`nProject(`"ignored`") = `"duplicate`", `"$windowsPath`", `"ignored`"`nEndProject`n")
            Fails { Get-GameBatchPlan $fixtureRepo } 'Solution project classification: empty or duplicate identity'
        }
        [IO.File]::WriteAllText((Join-Path $fixtureRepo 'Extra.csproj'), '<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>')
        [IO.File]::WriteAllText($solution, $originalSolution + "`nProject(`"ignored`") = `"Extra`", `"Extra.csproj`", `"ignored`"`nEndProject`n")
        Fails { Get-GameBatchPlan $fixtureRepo } 'Solution project classification: identity set mismatch'
        # Same project count is not sufficient: replace a known identity with an unknown one.
        [IO.File]::WriteAllText($solution, $originalSolution.Replace('tests\Pal.Extract.UnitTests\Pal.Extract.UnitTests.csproj', 'Extra.csproj'))
        Fails { Get-GameBatchPlan $fixtureRepo } 'Solution project classification: identity set mismatch'
        [IO.File]::WriteAllText($solution, $originalSolution)
        $unitProject = Join-Path $fixtureRepo 'tests/Pal.Extract.UnitTests/Pal.Extract.UnitTests.csproj'
        $unitXml = [IO.File]::ReadAllText($unitProject)
        [IO.File]::WriteAllText($unitProject, $unitXml.Replace('<IsTestProject>true</IsTestProject>', '<IsTestProject>false</IsTestProject>'))
        Fails { Get-GameBatchPlan $fixtureRepo } 'Solution test projects: identity set mismatch'
    }
    Case 'discovery exact names, rejects omission/extra/duplicate despite same counts' {
        $smallPlan = @{ inventory = @(@{ display = 'Alpha' }, @{ display = 'Beta' }) }
        $header = "The following Tests are available:`n"
        $null = Assert-GameDiscovery ($header + "    Alpha`n    Beta`n") $smallPlan
        Fails { Assert-GameDiscovery ($header + "    Alpha`n    Gamma") $smallPlan } 'set mismatch'
        Fails { Assert-GameDiscovery ($header + "    Alpha`n    Alpha") $smallPlan } 'duplicate'
        Fails { Assert-GameDiscovery ($header + '    Alpha') $smallPlan } 'set mismatch'
        Fails { Assert-GameDiscovery ($header + "    Alpha`n    Beta`n    Gamma") $smallPlan } 'set mismatch'
        Fails { Assert-GameDiscovery ($header + "    Alpha`n    Beta`n    未归档测试") $smallPlan } 'set mismatch|unparsed'
    }
    Case 'complete GdUnit result-definition-entry graph and exact FQN set' {
        $identity = Assert-GameBatch (Fixture $valid) $expected
        Assert ($identity.names.Count -eq 2 -and $identity.executionIds.Count -eq 2) 'Identity graph lost.'
        Fails { Assert-GameBatch (Fixture $valid) @('Pal.Game.Tests.Fixture.Alpha', 'Pal.Game.Tests.Fixture.Gamma') } 'set mismatch'
    }
    Case 'xUnit display identities retain parameter names rather than method-only aliases' {
        $text = $valid.Replace('executor://gdunit4.testadapter/', 'executor://xunit/VsTestRunner3/netcore/').Replace('name="Alpha"', 'name="Pal.Game.Tests.Fixture.Alpha"').Replace('name="Beta"', 'name="Pal.Game.Tests.Fixture.Beta"')
        $null = Read-TestRunIdentity (Fixture $text)
    }
    Case 'xUnit truncated displays may coincide while their linked case IDs remain distinct' {
        $text = $valid.Replace('executor://gdunit4.testadapter/', 'executor://xunit/VsTestRunner3/netcore/').Replace('name="Alpha"', 'name="Pal.Game.Tests.Fixture.Alpha"').Replace('name="Beta"', 'name="Pal.Game.Tests.Fixture.Beta"').Replace('Beta', 'Alpha')
        $identity = Read-TestRunIdentity (Fixture $text)
        Assert ($identity.names.Count -eq 2 -and @($identity.names | Select-Object -Unique).Count -eq 1 -and $identity.executionIds.Count -eq 2) 'Conflated distinct xUnit cases with coincident displays.'
        Fails { Read-TestRunIdentity (Fixture $text.Replace('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa')) } 'definition identity'
    }
    Case 'reject duplicate execution IDs and Game FQNs' {
        Fails { Read-TestRunIdentity (Fixture $valid.Replace('22222222-2222-2222-2222-222222222222', '11111111-1111-1111-1111-111111111111')) } 'duplicate identity'
        Fails { Assert-GameBatch (Fixture $valid.Replace('Beta', 'Alpha')) $expected } 'duplicate identity'
    }
    Case 'reject missing definition/entry and disconnected identities' {
        Fails { Read-TestRunIdentity (Fixture ([regex]::Replace($valid, '<UnitTest id="bbbb[^\r\n]+</UnitTest>', ''))) } 'Incomplete TRX identity graph'
        Fails { Read-TestRunIdentity (Fixture ([regex]::Replace($valid, '<TestEntry testId="bbbb[^\r\n]+/>', ''))) } 'Incomplete TRX identity graph'
        Fails { Read-TestRunIdentity (Fixture $valid.Replace('<TestEntry testId="bbbb', '<TestEntry testId="cccc')) } 'entry mismatch'
    }
    Case 'abort/error cannot pass even with zero aborted counter' {
        Fails { Assert-GameBatch (Fixture $valid.Replace('outcome="Completed"', 'outcome="Failed"')) $expected } 'session incomplete'
        $error = $valid.Replace('</ResultSummary>', '<RunInfos><RunInfo outcome="Error"><Text>Test Run Aborted</Text></RunInfo></RunInfos></ResultSummary>')
        Fails { Assert-GameBatch (Fixture $error) $expected } 'session incomplete'
        Fails { Assert-GameBatch (Fixture $valid.Replace('aborted="0"', 'aborted="1"')) $expected } 'Game counter aborted'
        Fails { Assert-GameBatch (Fixture $valid.Replace('outcome="Passed"', 'outcome="NotExecuted"')) $expected } 'Non-passing Game result'
    }
    Case 'reject missing counters and zero result graphs' {
        Fails { Assert-GameBatch (Fixture $valid.Replace(' executed="2"', '')) $expected } 'Missing Game counter executed'
        Fails { Assert-GameBatch (Fixture $valid.Replace(' failed="0"', '')) $expected } 'Missing Game counter failed'
        Fails { Assert-GameBatch (Fixture ([regex]::Replace($valid, '<ResultSummary.*</ResultSummary>', ''))) $expected } 'missing summary'
        Fails { Read-TestRunIdentity (Fixture '<TestRun id="empty" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"/>') } 'Incomplete TRX identity graph'
    }
    Case 'round cannot borrow, repeat or merge overlapping batches' {
        $smallPlan = @{ batches = @(@{ name = 'a'; expected = @('Alpha') }, @{ name = 'b'; expected = @('Beta') }) }
        $round = @(@{ name = 'a'; tests = @('Alpha') }, @{ name = 'b'; tests = @('Beta') })
        Assert-GameRound $round $smallPlan 'synthetic'
        Fails { Assert-GameRound @($round[0]) $smallPlan 'synthetic' } 'Incomplete Game round'
        Fails { Assert-GameRound @($round[0], $round[0]) $smallPlan 'synthetic' } 'duplicate'
        Fails { Assert-GameRound @(@{ name = 'a'; tests = @('Alpha') }, @{ name = 'b'; tests = @('Alpha') }) $smallPlan 'synthetic' } 'set mismatch'
    }
} finally { Remove-Item -LiteralPath $temp -Recurse -Force }
Write-Host "PASS: $($results.Count) offline batch metadata groups; no SDK/engine/CIM/network; synthetic TRX are not runtime passes."
