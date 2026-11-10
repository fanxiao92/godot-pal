# Offline metadata regression: synthetic C#/XML/text only. No SDK, Godot, CIM or network.
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
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
function Assert-AdapterCaseOwners($Plan, [string[]]$Names, [string]$Context) {
    foreach ($name in $Names) {
        $batch = if ($name.StartsWith('Pal.Game.Tests.TriggerDialogueFixtureTests.First_battle_', [StringComparison]::Ordinal)) { 'scene-realfull' } else { 'scene-dialogue' }
        $owners = @($Plan.batches | Where-Object { $_.expected -ccontains $name })
        Assert ($owners.Count -eq 1 -and $owners[0].name -ceq $batch) "${Context}: '$name' must belong only to $batch."
    }
}
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
    Case 'filtered native-map without a record requires full verification' {
        $guardRoot = Join-Path $temp 'filtered-missing'
        [IO.Directory]::CreateDirectory((Join-Path $guardRoot 'src/Pal.Game/Scripts/Profiles')) | Out-Null
        [IO.File]::WriteAllText((Join-Path $guardRoot 'src/Pal.Game/Scripts/Profiles/One.cs'), 'class One { }', [Text.UTF8Encoding]::new($false))
        $guard = Test-NativeMapAffectedBatchGuard -RepositoryRoot $guardRoot -Head 'aaa' -GameBatches @('native-map') -Filter 'FullyQualifiedName~Pal.Game.Tests.ViewportTests.'
        Assert ($guard.status -ceq 'required') "Filtered native-map without a record must be required, got '$($guard.status)'."
        Assert (-not [IO.File]::Exists((Get-NativeMapVerificationPath $guardRoot))) 'Guard must not create a record.'
    }
    Case 'filtered native-map rejects unreadable and stale records, preserves matching UTF8 evidence' {
        $guardRoot = Join-Path $temp 'filtered-records'
        $profile = Join-Path $guardRoot 'src/Pal.Game/Scripts/Profiles/One.cs'
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($profile)) | Out-Null
        [IO.File]::WriteAllText($profile, 'class One { }', [Text.UTF8Encoding]::new($false))
        $recordPath = Get-NativeMapVerificationPath $guardRoot
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($recordPath)) | Out-Null
        foreach ($badRecord in @('{broken', '{}', 'null', '{"fingerprint":"stale","head":"old"}')) {
            [IO.File]::WriteAllText($recordPath, $badRecord, [Text.UTF8Encoding]::new($false))
            foreach ($filter in @('FullyQualifiedName=Pal.Game.Tests.ViewportTests.One', 'FullyQualifiedName~Pal.Game.Tests.')) {
                $guard = Test-NativeMapAffectedBatchGuard $guardRoot 'new' @('native-map') $filter
                Assert ($guard.status -ceq 'required') "Bad/stale record must reject filtered native-map: $badRecord / $filter."
                Assert ([IO.File]::ReadAllText($recordPath, [Text.Encoding]::UTF8) -ceq $badRecord) 'Rejected record was overwritten.'
            }
        }
        Write-NativeMapVerification $guardRoot '完整验证'
        [IO.File]::SetLastWriteTimeUtc($recordPath, [DateTime]::new(2020, 1, 1, 0, 0, 0, [DateTimeKind]::Utc))
        $bytes = [Convert]::ToBase64String([IO.File]::ReadAllBytes($recordPath))
        $mtime = [IO.File]::GetLastWriteTimeUtc($recordPath).Ticks
        foreach ($filter in @('FullyQualifiedName=Pal.Game.Tests.ViewportTests.One', 'FullyQualifiedName~Pal.Game.Tests.')) {
            $guard = Test-NativeMapAffectedBatchGuard $guardRoot 'new' @('native-map') $filter
            Assert ($guard.status -ceq 'satisfied' -and $guard.verifiedHead -ceq '完整验证') 'Matching UTF8 record must satisfy even a broad explicit filter, never self-verify.'
        }
        Assert ([Convert]::ToBase64String([IO.File]::ReadAllBytes($recordPath)) -ceq $bytes) 'Matching record bytes changed.'
        Assert ([IO.File]::GetLastWriteTimeUtc($recordPath).Ticks -eq $mtime) 'Matching record mtime changed.'
        Assert ((Test-NativeMapAffectedBatchGuard $guardRoot 'new' @('native-map')).status -ceq 'self-verifying') 'Legacy positional call must remain full/self-verifying.'
        Assert ((Test-NativeMapAffectedBatchGuard -RepositoryRoot $guardRoot -Head 'new' -GameBatches @('native-map') -Filter '').status -ceq 'self-verifying') 'Default/empty optional Filter must preserve full behavior.'
        [IO.File]::WriteAllText($profile, 'class One { int Changed; }', [Text.UTF8Encoding]::new($false))
        Assert ((Test-NativeMapAffectedBatchGuard $guardRoot 'new' @('native-map') 'FullyQualifiedName~Pal.Game.Tests.').status -ceq 'required') 'Actual guarded-content change must invalidate filtered native-map.'
    }
    $plan = Get-GameBatchPlan $repository
    Case 'exact Story party positive moves exclusively to public scene-realfull' {
        $party = 'Pal.Game.Tests.StoryRoundtripSceneTests.PartyTwoPlayersPickTargetsByCyclingExecuteQueueAndWin'
        $owners = @($plan.batches | Where-Object { $_.expected -ccontains $party })
        Assert ($owners.Count -eq 1 -and $owners[0].name -ceq 'scene-realfull') 'Story party positive must belong exclusively to public scene-realfull.'
        $remainingHeavy = @(
            'Pal.Game.Tests.StoryRoundtripSceneTests.PartyLossCoversEnemyActionsMemberDeathPassAndLostClosure',
            'Pal.Game.Tests.StoryRoundtripSceneTests.RemovingTheDualAttackEquipmentPresentsExactlyOneStrikePerAction',
            'Pal.Game.Tests.StoryRoundtripSceneTests.VictorySettlementDisplaysGrowthRunsPostBattleScriptAndFlowsPanels.VictorySettlementDisplaysGrowthRunsPostBattleScriptAndFlowsPanels:0 ("zh-Hans")',
            'Pal.Game.Tests.StoryRoundtripSceneTests.VictorySettlementDisplaysGrowthRunsPostBattleScriptAndFlowsPanels.VictorySettlementDisplaysGrowthRunsPostBattleScriptAndFlowsPanels:1 ("en")')
        $oldPublic = @(
            'Pal.Game.Tests.SyntheticFirstBattleClosureTests.Synthetic_closure_full_flow_Won_continuation_and_changed_exploration',
            'Pal.Game.Tests.SyntheticFirstBattleClosureTests.Synthetic_closure_Lost_ends_in_the_real_reload_todo_terminal')
        $privateReal = 'Pal.Game.Tests.RealFirstBattleFlowTests.Real_phases_search_pages_engagement_and_bounded_damage_trajectory'
        foreach ($scope in @('full', 'committable')) {
            $candidate = Get-GameBatchPlan $repository -Scope $scope
            $heavy = @($candidate.batches | Where-Object name -CEQ 'scene-battle-heavy-d')[0]
            $public = @($candidate.batches | Where-Object name -CEQ 'scene-realfull')[0]
            Assert-ExactTestNames @($heavy.expected) $remainingHeavy "$scope remaining heavy-d"
            $firstBattleNames = @($candidate.inventory | Where-Object { $_.name.StartsWith('Pal.Game.Tests.TriggerDialogueFixtureTests.First_battle_', [StringComparison]::Ordinal) } | ForEach-Object name)
            Assert ($firstBattleNames.Count -eq 31) "$scope must own exactly 31 world-bound first-battle identities, including the coverer and zero-display cases."
            $publicNames = @($oldPublic + $party + $firstBattleNames)
            $publicFilter = 'FullyQualifiedName~Pal.Game.Tests.SyntheticFirstBattleClosureTests.|FullyQualifiedName~' + $party + '|FullyQualifiedName~Pal.Game.Tests.TriggerDialogueFixtureTests.First_battle_'
            $dialogue = @($candidate.batches | Where-Object name -CEQ 'scene-dialogue')[0]
            Assert ($dialogue.count -eq 108 -and $dialogue.sessionTimeoutMs -eq 120000) 'Dialogue membership changes, not its session budget.'
            Assert ($dialogue.filter -ceq '(FullyQualifiedName~Pal.Game.Tests.ExplorationSceneTests.|FullyQualifiedName~Pal.Game.Tests.NpcInteractionHostTests.|FullyQualifiedName~Pal.Game.Tests.DialogueDisplayFixtureTests.|FullyQualifiedName~Pal.Game.Tests.TriggerDialogueFixtureTests.)&FullyQualifiedName!~Pal.Game.Tests.TriggerDialogueFixtureTests.First_battle_') 'Dialogue must exclude only the full first-battle method prefix, with the whole class union grouped.'
            Assert-AdapterCaseOwners $candidate $firstBattleNames "$scope moved first-battle identities"
            if ($scope -ceq 'full') {
                $publicNames += $privateReal
                $publicFilter = 'FullyQualifiedName~Pal.Game.Tests.RealFirstBattleFlowTests.|' + $publicFilter
            }
            Assert-ExactTestNames @($public.expected) $publicNames "$scope exact public positives"
            Assert ($public.filter -ceq $publicFilter) 'Public filter must select only the existing full flows, exact Story method and first-battle adapter prefix.'
            Assert ($public.sessionTimeoutMs -eq 300000) 'Public session budget must remain 300000ms.'
            Assert-ExactTestNames @($public.classes) @('RealFirstBattleFlowTests', 'SyntheticFirstBattleClosureTests', 'StoryRoundtripSceneTests', 'TriggerDialogueFixtureTests') "$scope public class metadata"
            Assert-ExactTestNames @($heavy.expected + $public.expected) @($remainingHeavy + $publicNames) "$scope unchanged moved partition union"
            Assert-ExactTestNames @($candidate.batches.expected) @($candidate.inventory | Where-Object { $_.class -cne 'FailureControlTests' -and $_.name -cnotin $candidate.localOnly } | ForEach-Object name) "$scope disjoint full partition"
            if ($scope -ceq 'committable') {
                Assert ($public.expected -cnotcontains $privateReal -and $public.filter -cnotmatch 'RealFirstBattleFlowTests') 'Private real positive must not enter committable execution.'
                Assert-ExactTestNames @($candidate.localOnly) @($privateReal) 'unchanged private exclusion'
            }
        }
    }
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
    Case 'real source partitions 85/108/2/5/6/3/4/28/473/35, preserves controls and solution test projects' {
        Assert ((($plan.batches | ForEach-Object count) -join ',') -ceq '85,108,2,5,6,3,4,28,473,35') 'Wrong batch sizes/order.'
        Assert ($plan.inventory.Count -eq 752 -and $plan.controls.Count -eq 3 -and $plan.projects.Count -eq 5) 'Incomplete source inventory.'
        $storyHookPositive = 'Pal.Game.Tests.TriggerDialogueFixtureTests.Story_current_world_hook_uses_live_entry_and_owned_display_ack'
        foreach ($scope in @('full', 'committable')) {
            $candidate = Get-GameBatchPlan $repository -Scope $scope
            Assert (@($candidate.inventory | Where-Object name -CEQ $storyHookPositive).Count -eq 1) 'Story current-hook positive must have exactly one no-argument identity.'
            $owners = @($candidate.batches | Where-Object { $_.expected -ccontains $storyHookPositive })
            Assert ($owners.Count -eq 1 -and $owners[0].name -ceq 'scene-dialogue') "$scope Story current-hook positive must belong exclusively to scene-dialogue."
        }
        $firstBattleGlyph = @($plan.inventory | Where-Object name -CEQ 'Pal.Game.Tests.TriggerDialogueFixtureTests.First_battle_current_world_hidden_glyph_is_rejected_before_production_Start')
        Assert ($firstBattleGlyph.Count -eq 1) 'First-battle hidden-glyph regression must have exactly one no-argument identity.'
        $dialogue = @($plan.batches | Where-Object name -CEQ 'scene-dialogue')[0]
        $resourcePrefix = 'Pal.Game.Tests.TriggerDialogueFixtureTests.Story_current_world_bound_resource_faults_reject_before_production_Start.'
        $resourceCases = @(
            $resourcePrefix + 'Story_current_world_bound_resource_faults_reject_before_production_Start:0 ("missing-used-glyph")'
            $resourcePrefix + 'Story_current_world_bound_resource_faults_reject_before_production_Start:1 ("duplicate-used-glyph")'
            $resourcePrefix + 'Story_current_world_bound_resource_faults_reject_before_production_Start:2 ("bad-used-stride")'
            $resourcePrefix + 'Story_current_world_bound_resource_faults_reject_before_production_Start:3 ("prompt-0")'
            $resourcePrefix + 'Story_current_world_bound_resource_faults_reject_before_production_Start:4 ("prompt-1")'
            $resourcePrefix + 'Story_current_world_bound_resource_faults_reject_before_production_Start:5 ("prompt-2")'
            $resourcePrefix + 'Story_current_world_bound_resource_faults_reject_before_production_Start:6 ("portrait-7")'
            $resourcePrefix + 'Story_current_world_bound_resource_faults_reject_before_production_Start:7 ("future-portrait-10")'
        )
        $firstBattlePrefix = 'Pal.Game.Tests.TriggerDialogueFixtureTests.First_battle_current_world_bound_resources_validate_before_production_Start.'
        $firstBattleCases = @(
            $firstBattlePrefix + 'First_battle_current_world_bound_resources_validate_before_production_Start:0 ("clean-control")'
            $firstBattlePrefix + 'First_battle_current_world_bound_resources_validate_before_production_Start:1 ("missing-used-glyph")'
            $firstBattlePrefix + 'First_battle_current_world_bound_resources_validate_before_production_Start:2 ("duplicate-used-glyph")'
            $firstBattlePrefix + 'First_battle_current_world_bound_resources_validate_before_production_Start:3 ("bad-used-stride")'
            $firstBattlePrefix + 'First_battle_current_world_bound_resources_validate_before_production_Start:4 ("prompt-0")'
            $firstBattlePrefix + 'First_battle_current_world_bound_resources_validate_before_production_Start:5 ("prompt-1")'
            $firstBattlePrefix + 'First_battle_current_world_bound_resources_validate_before_production_Start:6 ("prompt-2")'
        )
        foreach ($scope in @('full', 'committable')) {
            $candidate = Get-GameBatchPlan $repository -Scope $scope
            Assert-ExactTestNames @($candidate.inventory | Where-Object { $_.name.StartsWith($resourcePrefix, [StringComparison]::Ordinal) } | ForEach-Object name) $resourceCases "$scope literal eight Resource fault identities"
            Assert-ExactTestNames @($candidate.batches | Where-Object name -CEQ 'scene-dialogue' | ForEach-Object expected | Where-Object { $_.StartsWith($resourcePrefix, [StringComparison]::Ordinal) }) $resourceCases "$scope Resource cases owned by scene-dialogue"
            Assert-ExactTestNames @($candidate.inventory | Where-Object { $_.name.StartsWith($firstBattlePrefix, [StringComparison]::Ordinal) } | ForEach-Object name) $firstBattleCases "$scope literal seven firstbattle Resource identities"
            Assert-ExactTestNames @($candidate.batches | Where-Object name -CEQ 'scene-realfull' | ForEach-Object expected | Where-Object { $_.StartsWith($firstBattlePrefix, [StringComparison]::Ordinal) }) $firstBattleCases "$scope firstbattle Resource cases owned by scene-realfull"
            $fullTail = 'Pal.Game.Tests.TriggerDialogueFixtureTests.First_battle_current_world_hook_uses_live_entry_and_owned_display_ack'
            Assert (@($candidate.inventory | Where-Object name -CEQ $fullTail).Count -eq 1) "$scope must contain exactly one full firstbattle tail."
            Assert (@($candidate.batches | Where-Object { $fullTail -cin $_.expected }).Count -eq 1 -and $fullTail -cin @($candidate.batches | Where-Object name -CEQ 'scene-realfull')[0].expected) "$scope full firstbattle tail must belong exclusively to scene-realfull."
            $paletteFault = 'Pal.Game.Tests.TriggerDialogueFixtureTests.First_battle_current_world_bound_palette_is_rejected_before_production_Start'
            Assert (@($candidate.inventory | Where-Object name -CEQ $paletteFault).Count -eq 1) "$scope must contain exactly one no-argument palette rejection."
            $paletteOwners = @($candidate.batches | Where-Object { $paletteFault -cin $_.expected })
            Assert ($paletteOwners.Count -eq 1 -and $paletteOwners[0].name -ceq 'scene-realfull') "$scope palette rejection must belong exclusively to scene-realfull."
            $basicPrefix = 'Pal.Game.Tests.TriggerDialogueFixtureTests.Basic_required_portrait_uses_native_file_identity_and_transactional_staging'
            $basicCases = @(
                $basicPrefix + '.Basic_required_portrait_uses_native_file_identity_and_transactional_staging:0 ("missing-file")'
                $basicPrefix + '.Basic_required_portrait_uses_native_file_identity_and_transactional_staging:1 ("missing-identity")'
                $basicPrefix + '.Basic_required_portrait_uses_native_file_identity_and_transactional_staging:2 ("later-glyph")'
                $basicPrefix + '.Basic_required_portrait_uses_native_file_identity_and_transactional_staging:3 ("clean-control")'
            )
            Assert-ExactTestNames @($candidate.inventory | Where-Object { $_.name.StartsWith($basicPrefix, [StringComparison]::Ordinal) } | ForEach-Object name) $basicCases "$scope literal four basic portrait cases"
            Assert-ExactTestNames @($candidate.batches | Where-Object name -CEQ 'scene-dialogue' | ForEach-Object expected | Where-Object { $_.StartsWith($basicPrefix, [StringComparison]::Ordinal) }) $basicCases "$scope basic portrait cases owned by scene-dialogue"
            Assert (@($candidate.batches | Where-Object name -CNE 'scene-dialogue' | ForEach-Object expected | Where-Object { $_.StartsWith($basicPrefix, [StringComparison]::Ordinal) }).Count -eq 0) "$scope basic portrait cases must not appear in another batch."
            $seedNegative = 'Pal.Game.Tests.TriggerDialogueFixtureTests.Story_missing_name_key_rejects_seed_glyph_before_production_Start'
            $seedPrefix = 'Pal.Game.Tests.TriggerDialogueFixtureTests.Story_declared_seed_and_live_zero_reach_owned_outer_return.'
            $seedCases = @(
                $seedNegative
                $seedPrefix + 'Story_declared_seed_and_live_zero_reach_owned_outer_return:0 ("absent-seed")'
                $seedPrefix + 'Story_declared_seed_and_live_zero_reach_owned_outer_return:1 ("live-zero")'
            )
            Assert-ExactTestNames @($candidate.inventory | Where-Object { $_.name -ceq $seedNegative -or $_.name.StartsWith($seedPrefix, [StringComparison]::Ordinal) } | ForEach-Object name) $seedCases "$scope literal three Story seed/live0 identities"
            Assert-ExactTestNames @($candidate.batches | Where-Object name -CEQ 'scene-dialogue' | ForEach-Object expected | Where-Object { $_ -ceq $seedNegative -or $_.StartsWith($seedPrefix, [StringComparison]::Ordinal) }) $seedCases "$scope Story seed/live0 cases owned by scene-dialogue"
            Assert (@($candidate.batches | Where-Object name -CNE 'scene-dialogue' | ForEach-Object expected | Where-Object { $_ -ceq $seedNegative -or $_.StartsWith($seedPrefix, [StringComparison]::Ordinal) }).Count -eq 0) "$scope Story seed/live0 cases must not appear in another batch."
            $futurePrefix = 'Pal.Game.Tests.TriggerDialogueFixtureTests.Story_future_literal_0090_'
            $futureCases = @(
                $futurePrefix + 'glyph_rejects_before_Start'
                $futurePrefix + 'reaches_owned_outer_return'
            )
            Assert-ExactTestNames @($candidate.inventory | Where-Object { $_.name.StartsWith($futurePrefix, [StringComparison]::Ordinal) } | ForEach-Object name) $futureCases "$scope literal two Story future0090 identities"
            Assert-ExactTestNames @($candidate.batches | Where-Object name -CEQ 'scene-dialogue' | ForEach-Object expected | Where-Object { $_.StartsWith($futurePrefix, [StringComparison]::Ordinal) }) $futureCases "$scope Story future0090 cases owned by scene-dialogue"
            Assert (@($candidate.batches | Where-Object name -CNE 'scene-dialogue' | ForEach-Object expected | Where-Object { $_.StartsWith($futurePrefix, [StringComparison]::Ordinal) }).Count -eq 0) "$scope Story future0090 cases must not appear in another batch."
            $secondOrderPrefix = 'Pal.Game.Tests.TriggerDialogueFixtureTests.Story_second_order_literal_0090_'
            $secondOrderCases = @(
                $secondOrderPrefix + 'glyph_rejects_before_Start'
                $secondOrderPrefix + 'reaches_owned_outer_return'
            )
            Assert-ExactTestNames @($candidate.inventory | Where-Object { $_.name.StartsWith($secondOrderPrefix, [StringComparison]::Ordinal) } | ForEach-Object name) $secondOrderCases "$scope literal two Story second-order0090 identities"
            Assert-ExactTestNames @($candidate.batches | Where-Object name -CEQ 'scene-dialogue' | ForEach-Object expected | Where-Object { $_.StartsWith($secondOrderPrefix, [StringComparison]::Ordinal) }) $secondOrderCases "$scope Story second-order0090 cases owned by scene-dialogue"
            Assert (@($candidate.batches | Where-Object name -CNE 'scene-dialogue' | ForEach-Object expected | Where-Object { $_.StartsWith($secondOrderPrefix, [StringComparison]::Ordinal) }).Count -eq 0) "$scope Story second-order0090 cases must not appear in another batch."
            $firstFuturePrefix = 'Pal.Game.Tests.TriggerDialogueFixtureTests.First_battle_future_literal_0090_'
            $firstFutureCases = @(
                $firstFuturePrefix + 'glyph_rejects_before_Start'
                $firstFuturePrefix + 'reaches_owned_outer_return'
            )
            Assert-ExactTestNames @($candidate.inventory | Where-Object { $_.name.StartsWith($firstFuturePrefix, [StringComparison]::Ordinal) } | ForEach-Object name) $firstFutureCases "$scope literal two firstbattle future0090 identities"
            Assert-AdapterCaseOwners $candidate $firstFutureCases "$scope firstbattle future0090"
            $firstSecondOrderPrefix = 'Pal.Game.Tests.TriggerDialogueFixtureTests.First_battle_second_order_literal_0090_'
            $firstSecondOrderCases = @(
                $firstSecondOrderPrefix + 'glyph_rejects_before_Start'
                $firstSecondOrderPrefix + 'reaches_owned_outer_return'
            )
            Assert-ExactTestNames @($candidate.inventory | Where-Object { $_.name.StartsWith($firstSecondOrderPrefix, [StringComparison]::Ordinal) } | ForEach-Object name) $firstSecondOrderCases "$scope literal two firstbattle second-order0090 identities"
            Assert-AdapterCaseOwners $candidate $firstSecondOrderCases "$scope firstbattle second-order0090"
            $friendDeathPositive = 'Pal.Game.Tests.TriggerDialogueFixtureTests.First_battle_second_order_0090_executes_real_coverer_friend_death_and_owned_ack'
            Assert (@($candidate.inventory | Where-Object name -CEQ $friendDeathPositive).Count -eq 1) "$scope must contain exactly one real coverer FriendDeath continuation."
            Assert-AdapterCaseOwners $candidate @($friendDeathPositive) "$scope real coverer continuation"
            $storyFriendDeath = 'Pal.Game.Tests.TriggerDialogueFixtureTests.Story_second_order_0090_executes_real_coverer_friend_death_and_owned_ack'
            Assert (@($candidate.inventory | Where-Object name -CEQ $storyFriendDeath).Count -eq 1) "$scope must contain exactly one Story real coverer continuation."
            Assert-AdapterCaseOwners $candidate @($storyFriendDeath) "$scope Story real coverer continuation"
            $firstZeroPrefix = 'Pal.Game.Tests.TriggerDialogueFixtureTests.First_battle_zero_display_ignores_unused_resources_but_validates_bound_portrait.'
            $firstZeroCases = @(
                $firstZeroPrefix + 'First_battle_zero_display_ignores_unused_resources_but_validates_bound_portrait:0 ("no-portrait")'
                $firstZeroPrefix + 'First_battle_zero_display_ignores_unused_resources_but_validates_bound_portrait:1 ("valid-portrait")'
                $firstZeroPrefix + 'First_battle_zero_display_ignores_unused_resources_but_validates_bound_portrait:2 ("bad-portrait")'
            )
            Assert-ExactTestNames @($candidate.inventory | Where-Object { $_.name.StartsWith($firstZeroPrefix, [StringComparison]::Ordinal) } | ForEach-Object name) $firstZeroCases "$scope literal three strict first-battle zero-display cases"
            Assert-AdapterCaseOwners $candidate $firstZeroCases "$scope strict first-battle zero-display"
            $randomCases = @(
                'Pal.Game.Tests.TriggerDialogueFixtureTests.Story_random_hook_branch_glyphs_reject_before_Start.Story_random_hook_branch_glyphs_reject_before_Start:0 ("random-miss-glyph")'
                'Pal.Game.Tests.TriggerDialogueFixtureTests.Story_random_hook_branch_glyphs_reject_before_Start.Story_random_hook_branch_glyphs_reject_before_Start:1 ("random-target-glyph")'
                'Pal.Game.Tests.TriggerDialogueFixtureTests.Story_random_hook_target_reaches_owned_outer_return'
                'Pal.Game.Tests.TriggerDialogueFixtureTests.First_battle_random_hook_branch_glyphs_reject_before_Start.First_battle_random_hook_branch_glyphs_reject_before_Start:0 ("random-miss-glyph")'
                'Pal.Game.Tests.TriggerDialogueFixtureTests.First_battle_random_hook_branch_glyphs_reject_before_Start.First_battle_random_hook_branch_glyphs_reject_before_Start:1 ("random-target-glyph")'
                'Pal.Game.Tests.TriggerDialogueFixtureTests.First_battle_random_hook_target_reaches_owned_outer_return'
            )
            $randomPattern = '^Pal\.Game\.Tests\.TriggerDialogueFixtureTests\.(Story|First_battle)_random_hook_'
            Assert-ExactTestNames @($candidate.inventory | Where-Object name -Match $randomPattern | ForEach-Object name) $randomCases "$scope literal six random hook identities"
            Assert-AdapterCaseOwners $candidate $randomCases "$scope random hook"
            $titleCases = @(
                'Pal.Game.Tests.TriggerDialogueFixtureTests.Story_title_only_glyph_faults_reject_before_Start.Story_title_only_glyph_faults_reject_before_Start:0 ("title-missing-glyph")'
                'Pal.Game.Tests.TriggerDialogueFixtureTests.Story_title_only_glyph_faults_reject_before_Start.Story_title_only_glyph_faults_reject_before_Start:1 ("title-duplicate-glyph")'
                'Pal.Game.Tests.TriggerDialogueFixtureTests.Story_title_only_glyph_faults_reject_before_Start.Story_title_only_glyph_faults_reject_before_Start:2 ("title-bad-stride")'
                'Pal.Game.Tests.TriggerDialogueFixtureTests.Story_title_and_body_reach_owned_outer_return'
                'Pal.Game.Tests.TriggerDialogueFixtureTests.First_battle_title_only_glyph_faults_reject_before_Start.First_battle_title_only_glyph_faults_reject_before_Start:0 ("title-missing-glyph")'
                'Pal.Game.Tests.TriggerDialogueFixtureTests.First_battle_title_only_glyph_faults_reject_before_Start.First_battle_title_only_glyph_faults_reject_before_Start:1 ("title-duplicate-glyph")'
                'Pal.Game.Tests.TriggerDialogueFixtureTests.First_battle_title_only_glyph_faults_reject_before_Start.First_battle_title_only_glyph_faults_reject_before_Start:2 ("title-bad-stride")'
                'Pal.Game.Tests.TriggerDialogueFixtureTests.First_battle_title_and_body_reach_owned_outer_return'
            )
            $titlePattern = '^Pal\.Game\.Tests\.TriggerDialogueFixtureTests\.(Story|First_battle)_title_'
            Assert-ExactTestNames @($candidate.inventory | Where-Object name -Match $titlePattern | ForEach-Object name) $titleCases "$scope literal eight title identities"
            Assert-AdapterCaseOwners $candidate $titleCases "$scope title"
            $missingPortraitCases = @(
                'Pal.Game.Tests.TriggerDialogueFixtureTests.Story_hidden_portrait_without_bound_mapping_rejects_before_Start'
                'Pal.Game.Tests.TriggerDialogueFixtureTests.First_battle_hidden_portrait_without_bound_mapping_rejects_before_Start'
            )
            Assert-ExactTestNames @($candidate.inventory | Where-Object name -Match '_hidden_portrait_without_bound_mapping_rejects_before_Start$' | ForEach-Object name) $missingPortraitCases "$scope literal two missing portrait identities"
            Assert-AdapterCaseOwners $candidate $missingPortraitCases "$scope missing portrait"
            $zeroDisplayCases = @(
                'Pal.Game.Tests.TriggerDialogueFixtureTests.Story_zero_display_ignores_unused_font_and_prompts_but_validates_portrait.Story_zero_display_ignores_unused_font_and_prompts_but_validates_portrait:0 ("no-portrait")'
                'Pal.Game.Tests.TriggerDialogueFixtureTests.Story_zero_display_ignores_unused_font_and_prompts_but_validates_portrait.Story_zero_display_ignores_unused_font_and_prompts_but_validates_portrait:1 ("valid-portrait")'
                'Pal.Game.Tests.TriggerDialogueFixtureTests.Story_zero_display_ignores_unused_font_and_prompts_but_validates_portrait.Story_zero_display_ignores_unused_font_and_prompts_but_validates_portrait:2 ("bad-portrait")'
                'Pal.Game.Tests.TriggerDialogueFixtureTests.First_battle_portrait_without_showDialog_still_requires_bound_mapping'
            )
            $zeroDisplayPattern = 'Story_zero_display_ignores_unused_font_and_prompts_but_validates_portrait\.|First_battle_portrait_without_showDialog_still_requires_bound_mapping$'
            Assert-ExactTestNames @($candidate.inventory | Where-Object name -Match $zeroDisplayPattern | ForEach-Object name) $zeroDisplayCases "$scope literal four zero-display/portrait-only identities"
            Assert-AdapterCaseOwners $candidate $zeroDisplayCases "$scope zero-display/portrait-only"
            $warmupCases = @(
                'Pal.Game.Tests.TriggerDialogueFixtureTests.Story_completed_warmup_preserves_rejection_priority_and_caller'
                'Pal.Game.Tests.TriggerDialogueFixtureTests.First_battle_completed_warmup_preserves_rejection_priority_and_caller'
            )
            Assert-ExactTestNames @($candidate.inventory | Where-Object name -Match '_completed_warmup_preserves_rejection_priority_and_caller$' | ForEach-Object name) $warmupCases "$scope literal two completed warmup identities"
            Assert-AdapterCaseOwners $candidate $warmupCases "$scope warmup"
            $firstSeedNegative = 'Pal.Game.Tests.TriggerDialogueFixtureTests.First_battle_missing_name_key_rejects_seed_glyph_before_production_Start'
            $firstSeedPrefix = 'Pal.Game.Tests.TriggerDialogueFixtureTests.First_battle_declared_seed_and_live_zero_reach_owned_outer_return.'
            $firstSeedCases = @(
                $firstSeedNegative
                $firstSeedPrefix + 'First_battle_declared_seed_and_live_zero_reach_owned_outer_return:0 ("absent-seed")'
                $firstSeedPrefix + 'First_battle_declared_seed_and_live_zero_reach_owned_outer_return:1 ("live-zero")'
            )
            Assert-ExactTestNames @($candidate.inventory | Where-Object { $_.name -ceq $firstSeedNegative -or $_.name.StartsWith($firstSeedPrefix, [StringComparison]::Ordinal) } | ForEach-Object name) $firstSeedCases "$scope literal three firstbattle seed/live0 identities"
            Assert-AdapterCaseOwners $candidate $firstSeedCases "$scope firstbattle seed/live0"
        }
        Assert-AdapterCaseOwners $plan @('Pal.Game.Tests.TriggerDialogueFixtureTests.First_battle_current_world_hidden_glyph_is_rejected_before_production_Start') 'First-battle hidden-glyph regression'
        Assert (($plan.projects.name -join ',') -ceq 'core,cli-unit,cli-integration,prepare,game') 'Wrong serial project identities/order.'
        Assert-ExactTestNames @($plan.projects.path) @('tests/Pal.Core.UnitTests/Pal.Core.UnitTests.csproj',
            'tests/Pal.Extract.UnitTests/Pal.Extract.UnitTests.csproj', 'tests/Pal.Extract.IntegrationTests/Pal.Extract.IntegrationTests.csproj',
            'tests/Pal.Prepare.Tests/Pal.Prepare.Tests.csproj', 'src/Pal.Game/Pal.Game.csproj') 'literal test project paths'
        $expectedProjects = @{
            core = @('tests/Pal.Core.UnitTests/Pal.Core.UnitTests.csproj', 1329, 0)
            'cli-unit' = @('tests/Pal.Extract.UnitTests/Pal.Extract.UnitTests.csproj', 475, 0)
            'cli-integration' = @('tests/Pal.Extract.IntegrationTests/Pal.Extract.IntegrationTests.csproj', 382, 2)
            prepare = @('tests/Pal.Prepare.Tests/Pal.Prepare.Tests.csproj', 89, 0)
            game = @('src/Pal.Game/Pal.Game.csproj', 749, 0)
        }
        foreach ($project in $plan.projects) {
            $expectedProject = $expectedProjects[$project.name]
            Assert ($project.path -ceq $expectedProject[0] -and $project.passed -eq $expectedProject[1] -and $project.skipped -eq $expectedProject[2]) "Wrong binding for $($project.name)."
        }
        Assert ($plan.batches[0].filter -ceq 'FullyQualifiedName~Pal.Game.Tests.StoryMainProcessTests.|FullyQualifiedName~Pal.Game.Tests.MainProcessTests.|FullyQualifiedName~Pal.Game.Tests.ExplorationProcessTests.') 'Filter boundary lost.'
        $longSessions = @($plan.batches | Where-Object { [int]$_.sessionTimeoutMs -gt 120000 } | ForEach-Object name)
        Assert (($longSessions -join ',') -ceq 'native-map,scene-realfull') 'Only native-map and scene-realfull may pin sessions beyond the shared 120s wall.'
        Assert-ExactTestNames @($plan.batches.expected) @($plan.inventory | Where-Object class -NE 'FailureControlTests' | ForEach-Object name) 'all source positives'
    }
    Case 'typed native runtime source cannot regress to embedded JSON factory' {
        $resource = [IO.File]::ReadAllText((Join-Path $repository 'src/Pal.Game/Scripts/Resources/PalFirstMandatoryBattleProfile.cs'))
        $loader = [IO.File]::ReadAllText((Join-Path $repository 'src/Pal.Game/Scripts/Profiles/NativeFirstMandatoryBattleProfile.cs'))
        $adapter = [IO.File]::ReadAllText((Join-Path $repository 'src/Pal.Game/Scripts/Profiles/NativeBattleEnvelopeAdapter.cs'))
        Assert ($resource -notmatch 'IndexBytes|ContextBytes|Json') 'Native profile regained a JSON text/blob property.'
        Assert (($loader + $adapter) -notmatch 'JsonDocument|FirstMandatoryBattleProfile\.Load\s*\(') 'Runtime native adapter regained the JSON factory/parser.'
        Assert ($resource -match 'NativeEnvelopeSha256') 'Native profile lost its canonical typed-envelope hash.'
    }
    Case 'gate PASS summary exactly reports the verified plan metadata' {
        $summary = Get-GatePassSummary $plan
        $expectedSummary = 'PASS: eight real-process dotnet-boundary prechecks; empty-cache restore/build; Core 1329 passed; CLI unit 475 passed; CLI integration 382 passed + 2 skipped (384 total); Prepare 89 passed; Godot source/discovery 752; positives 749 as serial 85/108/2/5/6/3/4/28/473/35 in each of three complete rounds; 37 identity-verified TRX; 3 separately verified 1-failure controls; native prepare/profile/import/Main startup. Headless automation only; real GPU/manual desktop acceptance is separate; this PASS alone does not establish remote-CI execution identity.'
        Assert ($summary -ceq $expectedSummary) "Wrong gate PASS summary: $summary"
        Assert ($summary.EndsWith('this PASS alone does not establish remote-CI execution identity.', [StringComparison]::Ordinal)) 'Gate PASS summary lost the remote-CI evidence boundary.'
    }
    Case 'committable scene-realfull executes synthetic, exact Story and first-battle adapter positives' {
        $plan = Get-GameBatchPlan $repository -Scope committable
        Assert ($plan.scope -ceq 'committable') 'Plan must record its scope.'
        $realfull = @($plan.batches | Where-Object name -CEQ 'scene-realfull')
        Assert ($realfull.Count -eq 1 -and $realfull[0].count -eq 34) 'scene-realfull must drop exactly its local-only real positive.'
        Assert ($realfull[0].filter -ceq 'FullyQualifiedName~Pal.Game.Tests.SyntheticFirstBattleClosureTests.|FullyQualifiedName~Pal.Game.Tests.StoryRoundtripSceneTests.PartyTwoPlayersPickTargetsByCyclingExecuteQueueAndWin|FullyQualifiedName~Pal.Game.Tests.TriggerDialogueFixtureTests.First_battle_') 'Committable scene-realfull filter must exclude the private-asset class.'
        Assert (($plan.batches | ForEach-Object count) -join ',' -ceq '85,108,2,5,6,3,4,28,473,34') 'Wrong committable batch sizes/order.'
        $game = @($plan.projects | Where-Object name -CEQ 'game')[0]
        Assert ($game.passed -eq 748) 'Committable game positives must drop the local-only case.'
        Assert ($plan.inventory.Count -eq 752) 'Committable scope keeps the real class compiled and discoverable.'
        Assert-ExactTestNames @($plan.localOnly) @('Pal.Game.Tests.RealFirstBattleFlowTests.Real_phases_search_pages_engagement_and_bounded_damage_trajectory') 'Local-only positive identity'
        Assert-ExactTestNames @($plan.batches.expected) @($plan.inventory | Where-Object { $_.class -NE 'FailureControlTests' -and $_.name -cnotin $plan.localOnly } | ForEach-Object name) 'committable partition'
    }
    Case 'full scope keeps the real positive alongside public full flows and first-battle adapters, with no exclusions' {
        $full = Get-GameBatchPlan $repository
        Assert ($full.scope -ceq 'full' -and @($full.localOnly).Count -eq 0) 'Full scope must stay the default with no exclusions.'
        $realfull = @($full.batches | Where-Object name -CEQ 'scene-realfull')[0]
        Assert ($realfull.count -eq 35) 'Full scene-realfull count changed.'
        Assert ($realfull.filter -cmatch 'RealFirstBattleFlowTests') 'Full scene-realfull filter must still select the real positive.'
    }
    Case 'committable gate PASS summary declares scope and the private-asset boundary' {
        $summary = Get-GatePassSummary (Get-GameBatchPlan $repository -Scope committable)
        $expected = 'PASS: committable-scope full gate; eight real-process dotnet-boundary prechecks; empty-cache restore/build; Core 1329 passed; CLI unit 475 passed; CLI integration 382 passed + 2 skipped (384 total); Prepare 89 passed; Godot source/discovery 752; positives 748 as serial 85/108/2/5/6/3/4/28/473/34 in each of three complete rounds; 37 identity-verified TRX; 3 separately verified 1-failure controls; native prepare/profile/import/Main startup; 1 local-only real-asset positives excluded from this remote scope (private staged assets are never uploaded; their acceptance receipts remain the local delivery process''s responsibility). Headless automation only; real GPU/manual desktop acceptance is separate; this committable-scope remote PASS does not replace the local full-scope gate for the excluded positives.'
        Assert ($summary -ceq $expected) "Wrong committable gate PASS summary: $summary"
        Assert ($summary -cmatch 'committable-scope full gate') 'Committable identity missing.'
        Assert ($summary -cmatch 'local-only real-asset positives excluded') 'Private-asset boundary missing.'
    }
    Case 'unknown gate scope fails closed' {
        Fails { Get-GameBatchPlan $repository -Scope bogus } 'Cannot validate argument|ValidateSet'
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
            Assert ($summary -notmatch 'CLI integration 359|CLI integration 188|CLI integration 360') 'Static CLI plan count remained in changed summary.'
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
    Case 'native-map affected-batch guard fingerprints guarded content and demands verification' {
        $guardRoot = Join-Path $temp ('guard-' + [guid]::NewGuid().ToString('N'))
        [IO.Directory]::CreateDirectory((Join-Path $guardRoot 'src/Pal.Game/Scripts/Profiles')) | Out-Null
        [IO.Directory]::CreateDirectory((Join-Path $guardRoot 'src/Pal.Core')) | Out-Null
        [IO.File]::WriteAllText((Join-Path $guardRoot 'src/Pal.Game/Scripts/Profiles/One.cs'), 'class One { }')
        Assert ((Get-NativeMapGuardFingerprint $guardRoot) -match '^[0-9a-f]{64}$') 'fingerprint must be a sha256 hex'
        $required = Test-NativeMapAffectedBatchGuard -RepositoryRoot $guardRoot -Head 'aaa' -GameBatches @('scene-dialogue')
        Assert ($required.status -ceq 'required') 'unverified guarded content must require native-map'
        Assert ($required.message -match 'native-map') 'requirement message must name the batch'
        $selfVerify = Test-NativeMapAffectedBatchGuard -RepositoryRoot $guardRoot -Head 'aaa' -GameBatches @('native-map')
        Assert ($selfVerify.status -ceq 'self-verifying') 'selecting native-map self-verifies'
        Write-NativeMapVerification -RepositoryRoot $guardRoot -Head 'aaa'
        $satisfied = Test-NativeMapAffectedBatchGuard -RepositoryRoot $guardRoot -Head 'bbb' -GameBatches @('scene-dialogue')
        Assert ($satisfied.status -ceq 'satisfied') 'matching fingerprint satisfies even after unrelated commits'
        Assert ($satisfied.verifiedHead -ceq 'aaa') 'verified head is diagnostic'
        [IO.File]::WriteAllText((Join-Path $guardRoot 'src/Pal.Game/Scripts/Profiles/One.cs'), 'class One { int Changed; }')
        $changed = Test-NativeMapAffectedBatchGuard -RepositoryRoot $guardRoot -Head 'bbb' -GameBatches @('scene-dialogue')
        Assert ($changed.status -ceq 'required') 'guarded content change must invalidate the record'
        # Unguarded content does not participate in the fingerprint.
        [IO.File]::WriteAllText((Join-Path $guardRoot 'src/Pal.Core/Unrelated.cs'), 'class Unrelated { }')
        $unchanged = Test-NativeMapAffectedBatchGuard -RepositoryRoot $guardRoot -Head 'bbb' -GameBatches @('scene-dialogue')
        Assert ($unchanged.status -ceq 'required') 'guard still required until native-map reruns'
        Write-NativeMapVerification -RepositoryRoot $guardRoot -Head 'bbb'
        $afterUnrelated = Test-NativeMapAffectedBatchGuard -RepositoryRoot $guardRoot -Head 'ccc' -GameBatches @('scene-dialogue')
        Assert ($afterUnrelated.status -ceq 'satisfied') 'unguarded change keeps the record satisfied'
        $emptyRoot = Join-Path $temp ('empty-' + [guid]::NewGuid().ToString('N')); [IO.Directory]::CreateDirectory($emptyRoot) | Out-Null
        Assert ((Test-NativeMapAffectedBatchGuard -RepositoryRoot $emptyRoot -Head 'x' -GameBatches @('scene-dialogue')).status -ceq 'none') 'no guarded content means no guard'
        $neverRoot = Join-Path $temp ('never-' + [guid]::NewGuid().ToString('N')); [IO.Directory]::CreateDirectory($neverRoot) | Out-Null
        Assert ($null -eq (Read-NativeMapVerification $neverRoot)) 'missing record reads as null'
    }
    Case 'Test-Range real AST guard and terminal write enforce full-only evidence without SDK execution' {
        $tokens = $null; $parseErrors = $null
        $rangeAst = [Management.Automation.Language.Parser]::ParseFile((Join-Path $repository 'scripts/Test-Range.ps1'), [ref]$tokens, [ref]$parseErrors)
        Assert (-not $parseErrors.Count) 'Test-Range AST must parse.'
        $outerTry = @($rangeAst.EndBlock.Statements | Where-Object {
            $_ -is [Management.Automation.Language.TryStatementAst] -and
                @($_.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -ceq 'Test-NativeMapAffectedBatchGuard' }, $true)).Count -eq 1
        })
        Assert ($outerTry.Count -eq 1) 'Expected one public Range execution try.'
        $outerStatements = @($outerTry[0].Body.Statements)
        $scopeLoop = @($outerStatements | Where-Object {
            $_ -is [Management.Automation.Language.ForEachStatementAst] -and $_.Variable.Extent.Text -ceq '$s' -and
                @($_.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -ceq 'Test-NativeMapAffectedBatchGuard' }, $true)).Count -eq 1
        })
        Assert ($scopeLoop.Count -eq 1) 'Expected one selected-scope loop.'
        $guardAssignment = @($scopeLoop[0].FindAll({ param($node)
            $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -ceq '$guard' -and
                @($node.FindAll({ param($command) $command -is [Management.Automation.Language.CommandAst] -and $command.GetCommandName() -ceq 'Test-NativeMapAffectedBatchGuard' }, $true)).Count -eq 1
        }, $true))
        Assert ($guardAssignment.Count -eq 1) 'Expected one real guard assignment.'
        $guardStatements = @($guardAssignment[0].Parent.Statements)
        $guardIndex = [array]::IndexOf($guardStatements, $guardAssignment[0])
        # Execute the real assignment, explicit bypass, report assignment and required rejection.
        $guardSlice = @($guardStatements[$guardIndex..($guardIndex + 3)])
        $loopStatements = @($scopeLoop[0].Body.Statements)
        $testAssignment = @($loopStatements | Where-Object {
            $_ -is [Management.Automation.Language.AssignmentStatementAst] -and $_.Left.Extent.Text -ceq '$null' -and
                @($_.FindAll({ param($command) $command -is [Management.Automation.Language.CommandAst] -and $command.GetCommandName() -ceq 'Invoke-RangeCommand' }, $true)).Count -eq 1
        })
        $validation = @($loopStatements | Where-Object {
            $_ -is [Management.Automation.Language.IfStatementAst] -and
                @($_.FindAll({ param($command) $command -is [Management.Automation.Language.CommandAst] -and $command.GetCommandName() -ceq 'Assert-GameBatch' }, $true)).Count -eq 1
        })
        Assert ($testAssignment.Count -eq 1 -and $validation.Count -eq 1) 'Expected the real test command and strict TRX validation.'
        $testIndex = [array]::IndexOf($loopStatements, $testAssignment[0])
        $validationIndex = [array]::IndexOf($loopStatements, $validation[0])
        Assert ($guardSlice[-1].Extent.EndOffset -lt $testAssignment[0].Extent.StartOffset -and $validationIndex -gt $testIndex) 'Guard rejection must precede game.test and its TRX validation.'
        $testSlice = @($loopStatements[$testIndex..$validationIndex])
        $loopIndex = [array]::IndexOf($outerStatements, $scopeLoop[0])
        $completionSlice = @($outerStatements[($loopIndex + 1)..($outerStatements.Count - 1)])
        $sealCommands = @($rangeAst.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -ceq 'Write-NativeMapVerification' }, $true))
        Assert ($sealCommands.Count -eq 1 -and @($completionSlice[-1].FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -ceq 'Write-NativeMapVerification' }, $true)).Count -eq 1) 'Only the terminal successful try body may issue evidence.'
        # No copied guard/seal conditions: these are exact source extents in production order.
        # This focused AST slice omits SDK/build/discovery/artifact work, not a native Range pass.
        $rangeSlice = [scriptblock]::Create((@($guardSlice + $testSlice + $completionSlice | ForEach-Object { $_.Extent.Text }) -join "`n"))
        $definitions = foreach ($name in @('Fail', 'Hash-File', 'Snapshot-Sources', 'Assert-GameSession')) {
            $definition = @($rangeAst.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq $name }, $true))
            Assert ($definition.Count -eq 1) "Missing actual Range function $name."
            [scriptblock]::Create($definition[0].Extent.Text)
        }
        function Invoke-NativeMapRangeSlice([string]$Root, [string]$Filter, [bool]$SkipAffectedBatchGuard = $false, [string]$Mode = 'success', [string[]]$Scope = @('game'), [string[]]$BatchNames = @('native-map')) {
            foreach ($definition in $definitions) { . $definition }
            $s = 'game'; $args = @(); $expected = @('Pal.Game.Tests.Fixture.Alpha', 'Pal.Game.Tests.Fixture.Beta')
            $batches = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($batch in $BatchNames) { $null = $batches.Add($batch) }
            $out = Join-Path $Root ('slice-' + [guid]::NewGuid().ToString('N'))
            $report = @{ head = '当前'; preparation = $null; sourceIdentity = @(Snapshot-Sources); generatedSourceIdentity = @(); toolIdentity = @(); status = 'incomplete' }
            $recordingError = $null; $commands = [Collections.Generic.List[object]]::new()
            function Invoke-RangeCommand([string]$Name, [string[]]$Arguments) {
                if ($Mode -ceq 'command-failure') { throw 'synthetic game.test exit 7' }
                $text = $valid.Replace('<Results>', '<Times creation="2026-01-01T00:00:00Z" queuing="2026-01-01T00:00:00Z" start="2026-01-01T00:00:00Z" finish="2026-01-01T00:00:01Z"/><Results>')
                if ($Mode -ceq 'incomplete') { $text = $text.Replace('outcome="Completed"', 'outcome="Failed"') }
                $directory = Join-Path $out 'game'; [IO.Directory]::CreateDirectory($directory) | Out-Null
                [IO.File]::WriteAllText((Join-Path $directory 'results.trx'), $text, [Text.UTF8Encoding]::new($false))
                $commands.Add(@{ name = $Name })
                if ($Mode -ceq 'source-changed') { [IO.File]::AppendAllText((Join-Path $Root 'src/Pal.Game/Scripts/Profiles/One.cs'), '// changed', [Text.UTF8Encoding]::new($false)) }
            }
            . $rangeSlice
            return $report
        }
        foreach ($scenario in @(
            @{ name = 'filtered missing'; filter = 'FullyQualifiedName~Pal.Game.Tests.'; reject = 'affected-batches'; writes = $false },
            @{ name = 'filtered corrupt'; filter = 'FullyQualifiedName~Pal.Game.Tests.'; record = 'corrupt'; reject = 'affected-batches'; writes = $false },
            @{ name = 'filtered stale'; filter = 'FullyQualifiedName~Pal.Game.Tests.'; record = 'stale'; reject = 'affected-batches'; writes = $false },
            @{ name = 'filtered matching'; filter = 'FullyQualifiedName=Pal.Game.Tests.Fixture.Alpha'; record = 'matching'; writes = $false; status = 'satisfied' },
            @{ name = 'broad filtered matching'; filter = 'FullyQualifiedName~Pal.Game.Tests.'; record = 'matching'; writes = $false; status = 'satisfied' },
            @{ name = 'full success'; writes = $true; status = 'self-verifying' },
            @{ name = 'full refresh'; record = 'matching'; writes = $true; status = 'self-verifying' },
            @{ name = 'full command failure'; mode = 'command-failure'; reject = 'synthetic game.test exit 7'; writes = $false },
            @{ name = 'full incomplete'; mode = 'incomplete'; reject = 'session incomplete'; writes = $false },
            @{ name = 'full source changed'; mode = 'source-changed'; reject = 'source-changed'; writes = $false },
            @{ name = 'filtered matching command failure'; filter = 'FullyQualifiedName~Pal.Game.Tests.'; record = 'matching'; mode = 'command-failure'; reject = 'synthetic game.test exit 7'; writes = $false },
            @{ name = 'full matching incomplete'; record = 'matching'; mode = 'incomplete'; reject = 'session incomplete'; writes = $false },
            @{ name = 'Skip full'; skip = $true; writes = $false; status = 'skipped' },
            @{ name = 'Skip filtered missing'; filter = 'FullyQualifiedName~Pal.Game.Tests.'; skip = $true; writes = $false; status = 'skipped' },
            @{ name = 'Skip matching'; record = 'matching'; skip = $true; writes = $false; status = 'skipped' },
            @{ name = 'other batch matching'; record = 'matching'; batches = @('process'); writes = $false; status = 'satisfied' },
            @{ name = 'non-game scope'; record = 'matching'; scope = @('cli-integration'); writes = $false; status = 'self-verifying' }
        )) {
            $fixtureRoot = Join-Path $temp ('range-slice-' + [guid]::NewGuid().ToString('N'))
            $profile = Join-Path $fixtureRoot 'src/Pal.Game/Scripts/Profiles/One.cs'
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($profile)) | Out-Null
            [IO.File]::WriteAllText($profile, 'class One { }', [Text.UTF8Encoding]::new($false))
            $recordPath = Get-NativeMapVerificationPath $fixtureRoot
            if ($scenario.record) {
                Write-NativeMapVerification $fixtureRoot '原完整记录'
                if ($scenario.record -ceq 'corrupt') { [IO.File]::WriteAllText($recordPath, '{bad', [Text.UTF8Encoding]::new($false)) }
                if ($scenario.record -ceq 'stale') { [IO.File]::AppendAllText($profile, '// stale', [Text.UTF8Encoding]::new($false)) }
                [IO.File]::SetLastWriteTimeUtc($recordPath, [DateTime]::new(2020, 1, 1, 0, 0, 0, [DateTimeKind]::Utc))
            }
            $beforeBytes = if ($scenario.record) { [Convert]::ToBase64String([IO.File]::ReadAllBytes($recordPath)) } else { $null }
            $beforeMtime = if ($scenario.record) { [IO.File]::GetLastWriteTimeUtc($recordPath).Ticks } else { 0 }
            $parameters = @{ Root = $fixtureRoot; Filter = $scenario.filter; SkipAffectedBatchGuard = [bool]$scenario.skip }
            if ($scenario.mode) { $parameters.Mode = $scenario.mode }
            if ($scenario.scope) { $parameters.Scope = $scenario.scope }
            if ($scenario.batches) { $parameters.BatchNames = $scenario.batches }
            if ($scenario.reject) { Fails { Invoke-NativeMapRangeSlice @parameters } $scenario.reject }
            else {
                $sliceReport = Invoke-NativeMapRangeSlice @parameters
                Assert ($sliceReport.status -ceq 'completed' -and $sliceReport.nativeMapGuard.status -ceq $scenario.status) "Unexpected completion/guard: $($scenario.name)."
            }
            if ($scenario.writes) {
                $record = Read-NativeMapVerification $fixtureRoot
                Assert ($record.head -ceq '当前' -and $record.fingerprint -ceq (Get-NativeMapGuardFingerprint $fixtureRoot)) "Full success must issue matching evidence: $($scenario.name)."
                Assert ([IO.File]::GetLastWriteTimeUtc($recordPath).Ticks -gt $beforeMtime) 'Full success must issue/refresh the record.'
            } elseif ($scenario.record) {
                Assert ([Convert]::ToBase64String([IO.File]::ReadAllBytes($recordPath)) -ceq $beforeBytes -and [IO.File]::GetLastWriteTimeUtc($recordPath).Ticks -eq $beforeMtime) "Record bytes/mtime changed: $($scenario.name)."
            } else { Assert (-not [IO.File]::Exists($recordPath)) "Unexpected evidence issued: $($scenario.name)." }
            if ($scenario.reject -ceq 'affected-batches') { Assert (@(Get-ChildItem -LiteralPath $fixtureRoot -Recurse -Filter '*.trx').Count -eq 0) 'Required guard must reject before synthetic game.test.' }
            Write-Host "PASS AST slice: $($scenario.name)"
        }
    }
} finally { Remove-Item -LiteralPath $temp -Recurse -Force }
Write-Host "PASS: $($results.Count) offline batch metadata groups; no SDK/engine/CIM/network; synthetic TRX are not runtime passes."
