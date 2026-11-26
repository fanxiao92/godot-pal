using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

/// <summary>#53 unit 3b continuous synthetic full-closure cases (committable; no retail data):
/// the same production binding/prepare/host chain drives fast-paced Won and Lost campaigns
/// end-to-end inside the scene-realfull batch budget.</summary>
[TestSuite, RequireGodotRuntime]
[TestCategory("Battle")]
public class SyntheticFirstBattleClosureTests
{
    [TestCase(Timeout = 280000)]
    public async Task Synthetic_closure_full_flow_Won_continuation_and_changed_exploration()
    {
        using var battleFixture = new NativeBattleProfileFixture();
        var statics = battleFixture.Static;
        // The bound branch re-tints every scene frame when the display palette changes
        // (PalDialogueDisplay.Start fires one palette event unconditionally); the synthetic
        // fixture frames start texture-only, so fill their indices/alpha planes first.
        foreach (var frame in statics.MapScene.Data.Map.Tiles.Frames)
        {
            frame.Indices = Array.Empty<byte>().Concat(Enumerable.Repeat((byte)0, frame.Width * frame.Height)).ToArray();
            frame.Alpha = Array.Empty<byte>().Concat(Enumerable.Repeat((byte)255, frame.Width * frame.Height)).ToArray();
        }
        foreach (var sprite in statics.MapScene.Data.Sprites)
            foreach (var frame in sprite.Frames)
            {
                frame.Indices = Array.Empty<byte>().Concat(Enumerable.Repeat((byte)0, frame.Width * frame.Height)).ToArray();
                frame.Alpha = Array.Empty<byte>().Concat(Enumerable.Repeat((byte)255, frame.Width * frame.Height)).ToArray();
            }
        // The bound branch reads the active scene palette; the synthetic scene data starts
        // without one, so the authored palette leaf fills it before the scene is packed.
        statics.MapScene.Data.Palette = (Pal.Game.Resources.PalPalette)ResourceLoader.Load(
            "res://Samples/AuthoredBattle/Static/Palette.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep)!;
        statics.SavePacked();
        string staticProfile = statics.ResourceRoot + "/Profile.tres";
        NativeProfileFixture.SaveResource(statics.Profile, staticProfile);
        // VerifyRealClosure reads the active scene palette at <static>/Palettes/Palette.tres;
        // the authored sample palette leaf (copy) fills that layout for the synthetic bundle.
        System.IO.Directory.CreateDirectory(ProjectSettings.GlobalizePath(statics.ResourceRoot + "/Palettes"));
        foreach (string file in new[] { "Palette.tres" })
            System.IO.File.Copy(GameProjectMount.GameSamplePath("Samples/AuthoredBattle/Static/" + file),
                ProjectSettings.GlobalizePath(statics.ResourceRoot + "/Palettes/" + file));
        using var fixture = new SyntheticFirstBattleFixture(statics, statics.ResourceRoot, staticProfile, lost: false);
        var frozenProfile = ResourceLoader.Load<Pal.Game.Resources.PalFirstMandatoryBattleProfile>(fixture.ProfileUri)!;
        var frozenContent = ResourceLoader.Load<Pal.Game.Resources.PalBattleContent>(frozenProfile.BattleContentUri)!;
        var frozenVisual = ResourceLoader.Load<Pal.Game.Resources.PalBattleVisualProfile>(frozenProfile.VisualUri)!;
        // String-URI manifests supplement the typed graph (notably modern event JSON).
        // Never walk author temp directories or generated .godot caches.
        var inputUris = Pal.Game.Profiles.NativeContentDependencies.ContentDependencies(new[] {
            fixture.ProfileUri, frozenProfile.StaticProfile, statics.ResourceRoot + "/Scenes/Scene.tscn",
            statics.ResourceRoot + "/Palettes/Palette.tres", frozenProfile.VisualUri });
        inputUris.UnionWith(frozenProfile.ResourceSha256.Keys);
        inputUris.UnionWith(frozenVisual.ResourceSha256.Keys);
        var inputHashes = inputUris.ToDictionary(uri => uri, Godot.FileAccess.GetSha256);
        foreach (string hash in inputHashes.Values) AssertThat(hash.Length).IsEqual(64);
        foreach (var pair in frozenProfile.ResourceSha256)
            AssertThat(inputHashes[pair.Key]).IsEqual(pair.Value);
        foreach (var pair in frozenVisual.ResourceSha256)
            AssertThat(inputHashes[pair.Key]).IsEqual(pair.Value);
        var frozenRoles = Pal.Game.Profiles.NativeBattleContent.CreateCore(frozenContent).InitialRoles;
        AssertRoleNumbers(frozenRoles[0], 100, 20, 0);
        AssertRoleNumbers(frozenRoles[1], 100, 5, 0);
        string frozenRoleValues = System.Text.Json.JsonSerializer.Serialize(frozenRoles);
        void AssertPreparedInputsUnchanged()
        {
            foreach (var pair in inputHashes)
                AssertThat(Godot.FileAccess.GetSha256(pair.Key)).IsEqual(pair.Value);
            AssertThat(System.Text.Json.JsonSerializer.Serialize(
                Pal.Game.Profiles.NativeBattleContent.CreateCore(frozenContent).InitialRoles)).IsEqual(frozenRoleValues);
            AssertRoleNumbers(frozenRoles[0], 100, 20, 0);
            AssertRoleNumbers(frozenRoles[1], 100, 5, 0);
        }
        var main = new Main();
        main.Configure(GameSceneConfiguration.FromArguments("--m4-first-battle-profile", fixture.ProfileUri));
        Pal.Game.Dialogue.GodotBattleRandom? productionRandom = null;
        Pal.Game.Dialogue.PalTriggerDialogue? ownedDialogue = null;
        Pal.Core.DynamicEventWorld? observedWorld = null;
        Pal.Core.RoleLiveState? observedOwner = null;
        Pal.Core.TriggerScriptSession? observedSession = null;
        Pal.Core.BattleRoleSnapshot? initialRoleOne = null, initialRoleZero = null;
        Pal.Core.BattleRoleSnapshot? firstPrefixZero = null, firstPrefixOne = null;
        Pal.Core.BattleRoleSnapshot? secondPrefixZero = null, secondPrefixOne = null;
        Pal.Core.BattleRoleSnapshot? firstReturnedZero = null;
        bool firstPrefix = false, secondPrefix = false, firstOuterReturn = false;
        bool secondReturn = false, secondOuterReturn = false, secondMovement = false;
        bool observeSecondMovement = false;
        Guid secondOrigin = Guid.Empty, secondCall = Guid.Empty;
        int firstRewards = 0, secondRewards = 0;
        Pal.Core.ClassicBattleLifecycleSession? firstLifecycle = null, secondLifecycle = null;
        Exception? observationError = null;
        bool observedBattle = false, observedReturn = false;
        bool observeMovement = false, observedMovement = false;
        Pal.Core.ExplorationState? beforeMovement = null;
        Pal.Core.BattleRoleSnapshot? returnedRoleOne = null;
        Guid firstOrigin = Guid.Empty, firstCall = Guid.Empty;
        int navigation = 0;
        bool observedNavigation = false, secondSearch = false, observedSecondBattle = false;
        bool wrongOwnedRequestRejected = false, firstReplayRejected = false, secondReplayRejected = false;
        bool staleReceiptsRejected = false;
        Guid firstBattleRequest = Guid.Empty, secondBattleRequest = Guid.Empty;
        Pal.Core.ClassicBattleLifecycleSession.CompletionReceipt? firstReceipt = null;
        Action? echoInvariant = null;
        bool echoObserved = false, observeTargetMenu = false, targetMenuObserved = false;
        // Complete records are an invariance oracle, never the numeric expected-value bank.
        Action CaptureStableBusinessState()
        {
            var dialogue = main.Dialogue!;
            var session = dialogue.Session!;
            var world = dialogue.FirstBattleWorld!;
            var owner = world.RoleLiveState!;
            var rolesView = owner.Roles;
            var battleView = session.BattleRoles!;
            var records = rolesView.ToDictionary(pair => pair.Key, pair => pair.Value);
            string values = System.Text.Json.JsonSerializer.Serialize(records);
            var vitals = world.PartyVitals.ToDictionary(pair => pair.Key, pair => (pair.Value.Hp, pair.Value.Mp));
            var phase = session.Phase; var call = session.CallId; var origin = session.SourceRequestId;
            var position = session.CurrentPosition; int returnEntry = session.ReturnEntry;
            bool blocksScene = session.BlocksSceneUpdates;
            var pending = session.PendingBattle; var display = session.PendingDisplay;
            var lifecycle = main.StoryBattle;
            var current = lifecycle?.Current; var receipt = lifecycle?.Completion;
            var settlement = lifecycle?.VictorySettlement;
            var mode = main.StoryBattleMode;
            var outer = main.Battle!.PendingTrigger; var outerId = main.Battle.PendingRequestId;
            var exploration = main.Exploration!; var explorationLeader = exploration.Leader;
            var outerCurrent = main.Battle.Current; var leader = world.Leader;
            var objects = world.Objects.Select(obj => (obj.Id, obj.X, obj.Y, obj.Direction, obj.CurrentFrame,
                obj.State, obj.VanishTime, obj.TriggerEntry, obj.AutoEntry, obj.AutoIdleCount)).ToArray();
            var party = world.PartyRoles.ToArray();
            int cash = main.StoryCashGranted, experience = main.StoryExperienceGranted;
            return () =>
            {
                AssertThat(ReferenceEquals(main.Dialogue, dialogue)).IsTrue();
                AssertThat(ReferenceEquals(dialogue.Session, session)).IsTrue();
                AssertThat(ReferenceEquals(dialogue.FirstBattleWorld, world)).IsTrue();
                AssertThat(ReferenceEquals(world.RoleLiveState, owner)).IsTrue();
                AssertThat(ReferenceEquals(owner.Roles, rolesView)).IsTrue();
                AssertThat(ReferenceEquals(session.BattleRoles, battleView)).IsTrue();
                AssertThat(owner.Roles.Keys.OrderBy(id => id).SequenceEqual(records.Keys.OrderBy(id => id))).IsTrue();
                AssertThat(battleView.Keys.OrderBy(id => id).SequenceEqual(records.Keys.OrderBy(id => id))).IsTrue();
                foreach (var pair in records)
                {
                    AssertThat(ReferenceEquals(owner.Roles[pair.Key], pair.Value)).IsTrue();
                    if (battleView.ContainsKey(pair.Key))
                        AssertThat(ReferenceEquals(battleView[pair.Key], pair.Value)).IsTrue();
                }
                AssertThat(System.Text.Json.JsonSerializer.Serialize(owner.Roles)).IsEqual(values);
                AssertThat(world.PartyVitals.Count).IsEqual(vitals.Count);
                foreach (var pair in vitals)
                    AssertThat((world.PartyVitals[pair.Key].Hp, world.PartyVitals[pair.Key].Mp)).IsEqual(pair.Value);
                AssertThat(session.Phase).IsEqual(phase);
                AssertThat(session.CallId).IsEqual(call);
                AssertThat(session.SourceRequestId).IsEqual(origin);
                AssertThat(session.CurrentPosition).IsEqual(position);
                AssertThat(session.ReturnEntry).IsEqual(returnEntry);
                AssertThat(session.BlocksSceneUpdates).IsEqual(blocksScene);
                AssertThat(ReferenceEquals(session.PendingBattle, pending)).IsTrue();
                AssertThat(ReferenceEquals(session.PendingDisplay, display)).IsTrue();
                AssertThat(ReferenceEquals(main.StoryBattle, lifecycle)).IsTrue();
                AssertThat(ReferenceEquals(lifecycle?.Current, current)).IsTrue();
                AssertThat(ReferenceEquals(lifecycle?.Completion, receipt)).IsTrue();
                AssertThat(ReferenceEquals(lifecycle?.VictorySettlement, settlement)).IsTrue();
                if (main.StoryBattleMode != mode)
                    throw new InvalidOperationException($"Story rejected receipt or Enter echo must preserve presentation mode: expected {mode}, actual {main.StoryBattleMode}.");
                AssertThat(ReferenceEquals(main.Battle.PendingTrigger, outer)).IsTrue();
                AssertThat(main.Battle.PendingRequestId).IsEqual(outerId);
                AssertThat(ReferenceEquals(main.Exploration, exploration)).IsTrue();
                AssertThat(main.Exploration!.Leader).IsEqual(explorationLeader);
                AssertThat(ReferenceEquals(main.Battle.Current, outerCurrent)).IsTrue();
                AssertThat(world.Leader).IsEqual(leader);
                AssertThat(world.PartyRoles.SequenceEqual(party)).IsTrue();
                AssertThat(world.Objects.Select(obj => (obj.Id, obj.X, obj.Y, obj.Direction, obj.CurrentFrame,
                    obj.State, obj.VanishTime, obj.TriggerEntry, obj.AutoEntry, obj.AutoIdleCount)).SequenceEqual(objects)).IsTrue();
                AssertThat(main.StoryCashGranted).IsEqual(cash);
                AssertThat(main.StoryExperienceGranted).IsEqual(experience);
            };
        }
        void RejectReceipt(Guid request, Pal.Core.ClassicBattleLifecycleSession.CompletionReceipt receipt)
        {
            var unchanged = CaptureStableBusinessState();
            AssertThat(main.Dialogue!.AcknowledgeBattle(request, receipt)).IsFalse();
            unchanged();
        }
        async Task ObserveEnterEcho(RealFirstBattleFlowTests.RealFlowScene scene)
        {
            echoObserved = false;
            echoInvariant = CaptureStableBusinessState(); // arm before actual engine dispatch
            try
            {
                using var input = new InputEventKey { Keycode = Key.Enter, PhysicalKeycode = Key.Enter, Pressed = true, Echo = true };
                scene.PushViewportInput(input);
                await scene.WaitFor(() => echoObserved, "first Main completion after Enter echo");
                if (observationError is not null) throw observationError;
            }
            finally { scene.Release(Key.Enter); echoInvariant = null; }
        }
        RealFirstBattleFlowTests.RealFlowScene? activeScene = null;
        void ObserveSharedRoles()
        {
            if (observationError is not null) return;
            try
            {
                if (echoInvariant is { } unchanged)
                {
                    echoInvariant = null;
                    echoObserved = true; // FIRST completion, before any state assertions
                    unchanged();
                }
                var world = main.Dialogue?.FirstBattleWorld;
                var owner = world?.RoleLiveState;
                var session = main.Dialogue?.Session;
                if (owner is null || session is null)
                    throw new InvalidOperationException("Missing shared role owner or trigger session at Main.UpdateCompleted.");
                if (observeTargetMenu && !targetMenuObserved)
                {
                    targetMenuObserved = true;
                    AssertThat(main.StoryBattleMode).IsEqual(Pal.Game.Dialogue.StoryBattlePresentation.TargetMenu);
                }
                if (observedOwner is null)
                {
                    observedWorld = world; observedOwner = owner; observedSession = session;
                    initialRoleOne = owner.Roles[1];
                    initialRoleZero = owner.Roles[0];
                    AssertRoleNumbers(initialRoleZero, 100, 20, 0);
                    AssertThat(world!.PartyRoles.SequenceEqual(new[] { 0, 1 })).IsTrue();
                    AssertThat(initialRoleOne.Hp).IsEqual(100);
                    AssertThat(initialRoleOne.Mp).IsEqual(5);
                    productionRandom = main.FirstBattleRandom;
                    ownedDialogue = main.Dialogue;
                    AssertCurrentPreview(main);
                    // The real factory has already transferred this native lifetime. A caller's
                    // exceptional unwind is not production StartCall/factory-construction coverage.
                    var productionUnchanged = CaptureStableBusinessState();
                    var randomState = productionRandom!.CurrentState;
                    var entry = session.Entries.First();
                    var lease = main.Dialogue!.PreviewSessionFactory!();
                    var fork = (Pal.Game.Dialogue.GodotBattleRandom)lease.Lifetime;
                    const string missingEntry = "U17-missing-preview-entry";
                    bool rejected = false;
                    try
                    {
                        using (lease.Lifetime)
                        {
                            AssertThat(fork.IsDisposed).IsFalse();
                            AssertThat(fork.CurrentState).IsEqual(randomState);
                            AssertThat(lease.Session.Entries.Any(e => e.FileId == entry.FileId && e.Id == missingEntry)).IsFalse();
                            lease.Session.Start(entry.FileId, missingEntry, 28);
                        }
                    }
                    catch (System.IO.InvalidDataException error)
                    {
                        AssertThat(error.Message).IsEqual($"{entry.FileId}.json: missing entry '{missingEntry}'.");
                        rejected = true;
                    }
                    AssertThat(rejected).IsTrue();
                    AssertThat(fork.IsDisposed).IsTrue();
                    AssertThat(productionRandom.IsDisposed).IsFalse();
                    AssertThat(productionRandom.CurrentState).IsEqual(randomState);
                    productionUnchanged();
                    AssertPreparedInputsUnchanged();
                }
                AssertThat(ReferenceEquals(world, observedWorld)).IsTrue();
                AssertThat(ReferenceEquals(owner, observedOwner)).IsTrue();
                AssertThat(ReferenceEquals(session, observedSession)).IsTrue();
                foreach (int roleId in new[] { 0, 1 })
                {
                    AssertThat(ReferenceEquals(session!.BattleRoles![roleId], owner.Roles[roleId])).IsTrue();
                    AssertThat(world!.PartyVitals[roleId].Hp).IsEqual(owner.Roles[roleId].Hp);
                    AssertThat(world.PartyVitals[roleId].Mp).IsEqual(owner.Roles[roleId].Mp);
                }
                // First applicable published Main completion after record replacement,
                // not a wait for correct numbers. Dialogue-only ticks need not publish;
                // this boundary can coincide with the first battle completion callback.
                if (!secondSearch && !firstPrefix && !ReferenceEquals(initialRoleZero, owner.Roles[0]))
                {
                    firstPrefix = true;
                    firstPrefixZero = owner.Roles[0]; firstPrefixOne = owner.Roles[1];
                    AssertRoleNumbers(firstPrefixZero, 97, 17, 0);
                    AssertRoleNumbers(firstPrefixOne, 97, 2, 0);
                    AssertRoleNumbers(initialRoleZero!, 100, 20, 0);
                    AssertRoleNumbers(initialRoleOne!, 100, 5, 0);
                    AssertThat(ReferenceEquals(initialRoleOne, firstPrefixOne)).IsFalse();
                    AssertThat(session!.CallId != Guid.Empty).IsTrue();
                    AssertThat(session.SourceRequestId).IsEqual(main.Battle!.PendingRequestId);
                    AssertThat(session.Phase).IsEqual(Pal.Core.TriggerScriptPhase.WaitingForBattle);
                }
                if (secondSearch && !secondPrefix && !ReferenceEquals(firstReturnedZero, owner.Roles[0]))
                {
                    secondPrefix = true;
                    secondPrefixZero = owner.Roles[0]; secondPrefixOne = owner.Roles[1];
                    AssertRoleNumbers(secondPrefixZero, 120, 30, 52);
                    AssertRoleNumbers(secondPrefixOne, 95, 0, 52);
                    AssertRoleNumbers(firstReturnedZero!, 123, 33, 52);
                    AssertRoleNumbers(returnedRoleOne!, 98, 3, 52);
                    AssertThat(ReferenceEquals(returnedRoleOne, secondPrefixOne)).IsFalse();
                    AssertThat(session!.CallId != firstCall).IsTrue();
                    AssertThat(session.SourceRequestId).IsEqual(main.Battle!.PendingRequestId);
                    AssertThat(session.SourceRequestId != firstOrigin).IsTrue();
                    AssertThat(session.Phase).IsEqual(Pal.Core.TriggerScriptPhase.WaitingForBattle);
                }
                if (main.StoryBattle is { } lifecycle)
                {
                    AssertRoleNumbers(owner.Roles[0], secondSearch ? 120 : 97, secondSearch ? 30 : 17, secondSearch ? 52 : 0);
                    AssertRoleNumbers(owner.Roles[1], secondSearch ? 95 : 97, secondSearch ? 0 : 2, secondSearch ? 52 : 0);
                    // Enemy dex18 * .9..1.1 < player dex100 * .9..1.1;
                    // both HP1 slots die before any enemy action (including dual corpse hits).
                    if (lifecycle.Current.Combat.LastAction is { } action)
                        AssertThat(action.Attacker).IsEqual(Pal.Core.ClassicBattleSide.Player);
                    foreach (var player in lifecycle.Current.Combat.Players)
                        AssertThat(player.Hp).IsEqual(secondSearch ? (player.RoleId == 0 ? 120 : 95) : 97);
                    if (!secondSearch && !wrongOwnedRequestRejected && lifecycle.Completion is { } ownedReceipt)
                    {
                        wrongOwnedRequestRejected = true;
                        AssertThat(lifecycle.OwnsCompletion(ownedReceipt)).IsTrue();
                        AssertThat(session!.Phase).IsEqual(Pal.Core.TriggerScriptPhase.WaitingForBattle);
                        AssertThat(session.PendingBattle!.Id != Guid.Empty).IsTrue();
                        RejectReceipt(Guid.Empty, ownedReceipt); // owned receipt, wrong ID while waiting
                    }
                    if (lifecycle.VictorySettlement is { } reward)
                    {
                        AssertThat(reward.ExperienceGranted).IsEqual(52);
                        AssertThat(reward.CashGranted).IsEqual(96);
                        if (secondSearch) secondRewards = 1; else firstRewards = 1;
                    }
                }
                if (main.StoryBattle is not null && !observedBattle)
                {
                    observedBattle = true;
                    AssertThat(firstPrefix).IsTrue();
                    firstLifecycle = main.StoryBattle;
                    firstBattleRequest = session!.PendingBattle!.Id;
                    AssertRoleNumbers(firstLifecycle.Current.Roles[0], 97, 17, 0);
                    AssertRoleNumbers(firstLifecycle.Current.Roles[1], 97, 2, 0);
                    AssertThat(ReferenceEquals(firstPrefixZero, owner.Roles[0])).IsTrue();
                    AssertThat(ReferenceEquals(firstPrefixOne, owner.Roles[1])).IsTrue();
                    firstOrigin = main.Battle!.PendingRequestId;
                    firstCall = session!.CallId;
                    if (firstOrigin == Guid.Empty || session.SourceRequestId != firstOrigin)
                        throw new InvalidOperationException("U2 first battle must retain the actual committed search origin.");
                    AssertThat(firstCall != Guid.Empty).IsTrue();
                    AssertThat(world!.Leader).IsEqual(main.Exploration!.Leader);
                }
                else if (main.StoryBattle is null && observedBattle && !observedReturn)
                {
                    // First published Main completion after the owned final receipt, not a
                    // coroutine's later read. The held initial record remains frozen.
                    observedReturn = true;
                    AssertThat(firstLifecycle!.Completion).IsNotNull();
                    firstReceipt = firstLifecycle.Completion;
                    AssertThat(firstLifecycle.OwnsCompletion(firstReceipt)).IsTrue();
                    AssertThat(firstReceipt!.CashGranted).IsEqual(96);
                    AssertThat(firstReceipt.ExperienceGranted).IsEqual(52);
                    RejectReceipt(firstBattleRequest, firstReceipt);
                    firstReplayRejected = true;
                    AssertThat(main.StoryCashGranted).IsEqual(96);
                    AssertThat(main.StoryExperienceGranted).IsEqual(52);
                    AssertPreparedInputsUnchanged();
                    AssertThat(firstRewards).IsEqual(1);
                    AssertRoleNumbers(owner.Roles[0], 123, 33, 52);
                    AssertRoleNumbers(owner.Roles[1], 98, 3, 52);
                    firstReturnedZero = owner.Roles[0]; returnedRoleOne = owner.Roles[1];
                    AssertThat(ReferenceEquals(firstPrefixZero, owner.Roles[0])).IsFalse();
                    AssertThat(ReferenceEquals(firstPrefixOne, owner.Roles[1])).IsFalse();
                    AssertThat(ReferenceEquals(initialRoleOne, owner.Roles[1])).IsFalse();
                    AssertThat(initialRoleOne!.Hp).IsEqual(100);
                    AssertThat(initialRoleOne.Mp).IsEqual(5);
                }
                if (main.StoryBattle is null && observedReturn && !secondSearch
                    && session.Phase == Pal.Core.TriggerScriptPhase.Completed && !firstOuterReturn)
                {
                    firstOuterReturn = true;
                    AssertRoleNumbers(owner.Roles[0], 123, 33, 52);
                    AssertRoleNumbers(owner.Roles[1], 98, 3, 52);
                    AssertThat(main.Battle!.PendingTrigger).IsNull();
                    AssertThat(main.Battle.PendingRequestId).IsEqual(Guid.Empty);
                }
                if (observeMovement && !observedMovement)
                {
                    // Sample the FIRST actual completion after Down, even if it is wrong.
                    observedMovement = true;
                    activeScene?.Release(Key.Down);
                    var current = main.Exploration!;
                    AssertThat(firstOuterReturn).IsTrue();
                    AssertThat(session!.Phase).IsEqual(Pal.Core.TriggerScriptPhase.Completed);
                    AssertThat(session.SourceRequestId).IsEqual(firstOrigin);
                    AssertThat(session.CallId).IsEqual(firstCall);
                    AssertThat(main.Battle!.PendingTrigger).IsNull();
                    AssertThat(main.Battle.PendingRequestId).IsEqual(Guid.Empty);
                    AssertThat(current.IsWalking).IsTrue();
                    AssertThat(current.Leader.Direction).IsEqual(0);
                    AssertThat(current.Leader.X).IsEqual((short)(beforeMovement!.Leader.X - 16));
                    AssertThat(current.Leader.Y).IsEqual((short)(beforeMovement.Leader.Y + 8));
                    AssertThat(current.Leader.X).IsEqual((short)1248);
                    AssertThat(current.Leader.Y).IsEqual((short)256);
                    AssertThat(world!.Leader).IsEqual(current.Leader);
                    AssertThat(ReferenceEquals(current.Plan, main.Battle.Current.Plan)).IsTrue();
                    AssertThat(current.Plan.StepPhase).IsEqual(current.Leader.StepPhase);
                    int expectedPhase = (beforeMovement.Leader.StepPhase + 1) % 4;
                    AssertThat(current.Leader.StepPhase).IsEqual(expectedPhase);
                    AssertThat(current.Plan.LeaderFrameIndex).IsEqual(expectedPhase % 2 == 0 ? 0 : (expectedPhase + 1) / 2);
                    AssertThat(ReferenceEquals(returnedRoleOne, owner.Roles[1])).IsTrue();
                    AssertCurrentPreview(main);
                    AssertPreparedInputsUnchanged();
                }
                if (navigation != 0 && !observedNavigation)
                {
                    observedNavigation = true;
                    activeScene?.Release(navigation == 1 ? Key.Up : Key.Right);
                    var current = main.Exploration!;
                    AssertThat(current.Leader.X).IsEqual((short)1264);
                    AssertThat(current.Leader.Y).IsEqual((short)248);
                    AssertThat(current.Leader.Direction).IsEqual(navigation == 1 ? 2 : 3);
                    AssertThat(current.IsWalking).IsEqual(navigation == 1);
                    AssertThat(world!.Leader).IsEqual(current.Leader);
                }
                if (secondSearch && main.StoryBattle is not null && !observedSecondBattle)
                {
                    observedSecondBattle = true;
                    AssertThat(main.Battle!.PendingRequestId != Guid.Empty).IsTrue();
                    AssertThat(main.Battle.PendingRequestId != firstOrigin).IsTrue();
                    AssertThat(session!.SourceRequestId).IsEqual(main.Battle.PendingRequestId);
                    AssertThat(session.CallId != firstCall).IsTrue();
                    AssertThat(secondPrefix).IsTrue();
                    secondOrigin = main.Battle.PendingRequestId; secondCall = session.CallId;
                    secondLifecycle = main.StoryBattle;
                    secondBattleRequest = session.PendingBattle!.Id;
                    AssertThat(ReferenceEquals(firstLifecycle, secondLifecycle)).IsFalse();
                    AssertThat(secondLifecycle.OwnsCompletion(firstReceipt)).IsFalse();
                    RejectReceipt(Guid.Empty, firstReceipt!);
                    RejectReceipt(secondBattleRequest, firstReceipt!); // current ID, foreign lifecycle receipt
                    RejectReceipt(firstBattleRequest, firstReceipt!); // stale ID + consumed receipt
                    staleReceiptsRejected = true;
                    AssertRoleNumbers(secondLifecycle.Current.Roles[0], 120, 30, 52);
                    AssertRoleNumbers(secondLifecycle.Current.Roles[1], 95, 0, 52);
                    AssertThat(ReferenceEquals(secondPrefixZero, owner.Roles[0])).IsTrue();
                    AssertThat(ReferenceEquals(secondPrefixOne, owner.Roles[1])).IsTrue();
                    AssertThat(main.StoryBattle.Current.Combat.Players.Select(player => player.RoleId)
                        .SequenceEqual(new[] { 0, 1 })).IsTrue();
                }
                if (main.StoryBattle is null && observedSecondBattle && !secondReturn)
                {
                    secondReturn = true;
                    AssertThat(secondLifecycle!.Completion).IsNotNull();
                    AssertThat(ReferenceEquals(firstReceipt, secondLifecycle.Completion)).IsFalse();
                    AssertThat(secondLifecycle.OwnsCompletion(secondLifecycle.Completion)).IsTrue();
                    AssertThat(secondLifecycle.Completion!.CashGranted).IsEqual(96);
                    AssertThat(secondLifecycle.Completion.ExperienceGranted).IsEqual(52);
                    RejectReceipt(secondBattleRequest, secondLifecycle.Completion);
                    secondReplayRejected = true;
                    AssertThat(main.StoryCashGranted).IsEqual(192); // host grants, not a persistent wallet
                    AssertThat(main.StoryExperienceGranted).IsEqual(104);
                    AssertPreparedInputsUnchanged();
                    AssertThat(secondRewards).IsEqual(1);
                    AssertRoleNumbers(owner.Roles[0], 135, 40, 104);
                    AssertRoleNumbers(owner.Roles[1], 97, 2, 104);
                    AssertThat(ReferenceEquals(secondPrefixZero, owner.Roles[0])).IsFalse();
                    AssertThat(ReferenceEquals(secondPrefixOne, owner.Roles[1])).IsFalse();
                }
                if (main.StoryBattle is null && secondReturn && !secondOuterReturn
                    && session.Phase == Pal.Core.TriggerScriptPhase.Completed)
                {
                    secondOuterReturn = true;
                    AssertThat(session.SourceRequestId).IsEqual(secondOrigin);
                    AssertThat(session.CallId).IsEqual(secondCall);
                    AssertThat(main.Battle!.PendingTrigger).IsNull();
                    AssertThat(main.Battle.PendingRequestId).IsEqual(Guid.Empty);
                    AssertRoleNumbers(owner.Roles[0], 135, 40, 104);
                    AssertRoleNumbers(owner.Roles[1], 97, 2, 104);
                    AssertRoleNumbers(initialRoleZero!, 100, 20, 0);
                    AssertRoleNumbers(initialRoleOne!, 100, 5, 0);
                    AssertRoleNumbers(firstPrefixZero!, 97, 17, 0);
                    AssertRoleNumbers(firstPrefixOne!, 97, 2, 0);
                    AssertRoleNumbers(firstReturnedZero!, 123, 33, 52);
                    AssertRoleNumbers(returnedRoleOne!, 98, 3, 52);
                    AssertRoleNumbers(secondPrefixZero!, 120, 30, 52);
                    AssertRoleNumbers(secondPrefixOne!, 95, 0, 52);
                }
                if (observeSecondMovement && !secondMovement)
                {
                    secondMovement = true;
                    activeScene?.Release(Key.Down);
                    AssertThat(secondOuterReturn).IsTrue();
                    AssertThat(main.Exploration!.IsWalking).IsTrue();
                    AssertThat(main.Exploration.Leader.X).IsEqual((short)1248);
                    AssertThat(main.Exploration.Leader.Y).IsEqual((short)256);
                    AssertRoleNumbers(owner.Roles[0], 135, 40, 104);
                    AssertRoleNumbers(owner.Roles[1], 97, 2, 104);
                    AssertCurrentPreview(main);
                    AssertPreparedInputsUnchanged();
                }
            }
            catch (Exception error) { observationError = error; }
        }
        main.UpdateCompleted += ObserveSharedRoles; // before RealFlowScene adds Main to the tree
        try
        {
            using var scene = new RealFirstBattleFlowTests.RealFlowScene(main);
            activeScene = scene;
            await scene.WaitFor(() => observedOwner is not null || observationError is not null, "initial committed roles");
            if (observationError is not null) throw observationError;
            scene.Press(Key.Enter); // search owner28
            int pages = 0, frames = 0;
            ulong deadline = Time.GetTicksMsec() + 120000;
            while (main.StoryBattle is null && main.Dialogue?.Session?.PendingLostReload is null && Time.GetTicksMsec() < deadline)
            {
                var display = main.Dialogue?.Session?.PendingDisplay;
                if (display is not null && ((frames & 15) == 0
                    || display.Display.Current.Phase == Pal.Core.DialogueDisplayPhase.WaitingForAdvance && (frames & 3) == 0))
                { scene.Press(Key.Enter); pages++; }
                frames++;
                await scene.RunnerIdle();
            }
            System.Console.WriteLine($"SYN_PARK:pages={pages} phase={main.Dialogue?.Session?.Phase} pos={main.Dialogue?.Session?.CurrentPosition} pending={main.Dialogue?.Session?.PendingBattle is not null} display={main.Dialogue?.Session?.PendingDisplay is not null} failure={main.Failure}");
            if (observationError is not null) throw observationError;
            AssertThat(main.StoryBattle is not null).IsTrue();
            AssertThat(main.StoryBattle!.Current.Combat.Enemies.Count).IsEqual(2);
            AssertThat(main.StoryBattleMode).IsEqual(Pal.Game.Dialogue.StoryBattlePresentation.AttackMenu);
            await ObserveEnterEcho(scene);
            // Reuse the driver's first real confirm, then prove echo cannot select its target.
            observeTargetMenu = true;
            scene.Press(Key.Enter);
            await scene.WaitFor(() => targetMenuObserved || observationError is not null, "first real menu confirm");
            if (observationError is not null) throw observationError;
            observeTargetMenu = false;
            await ObserveEnterEcho(scene);
            int rounds = 1;
            while (main.StoryBattle is not null && rounds++ < 3000)
            { scene.Press(Key.Enter); await scene.RunnerIdle(); }
            // The Won continuation parks on @09FF displays after the battle view releases;
            // keep advancing until the call completes (same cadence as the pre-battle phase).
            ulong wonDeadline = Time.GetTicksMsec() + 60000;
            while (main.Dialogue?.Session is { Phase: not Pal.Core.TriggerScriptPhase.Completed } session
                && main.Dialogue.Session.PendingLostReload is null && Time.GetTicksMsec() < wonDeadline)
            {
                var wonDisplay = session.PendingDisplay;
                if (wonDisplay is not null && ((frames & 15) == 0
                    || wonDisplay.Display.Current.Phase == Pal.Core.DialogueDisplayPhase.WaitingForAdvance && (frames & 3) == 0))
                    scene.Press(Key.Enter);
                frames++;
                await scene.RunnerIdle();
            }
            System.Console.WriteLine($"SYN_WON:pages={pages} rounds={rounds} completed={main.Dialogue?.Session?.Phase} lost={main.StoryLostTerminal}");
            AssertThat(main.StoryLostTerminal).IsFalse();
            AssertThat(main.Dialogue?.Session?.Phase).IsEqual(Pal.Core.TriggerScriptPhase.Completed);
            if (observationError is not null) throw observationError;
            AssertThat(observedBattle).IsTrue();
            AssertThat(observedReturn).IsTrue();
            AssertThat(main.Battle!.PendingTrigger).IsNull();
            AssertThat(main.Battle.PendingRequestId).IsEqual(Guid.Empty);
            beforeMovement = main.Exploration!;
            returnedRoleOne = observedOwner!.Roles[1];
            observeMovement = true;
            scene.Press(Key.Down);
            await scene.WaitFor(() => observedMovement || observationError is not null, "first post-Won committed movement");
            if (observationError is not null) throw observationError;
            AssertThat(firstOuterReturn).IsTrue();
            // The unchanged fixture keeps owner28 at (1280,256), state2/mode1/@09C3.
            // One north step restores the approach; east then faces its blocking cell.
            navigation = 1;
            scene.Press(Key.Up);
            await scene.WaitFor(() => observedNavigation || observationError is not null, "north return step");
            if (observationError is not null) throw observationError;
            navigation = 2; observedNavigation = false;
            scene.Press(Key.Right);
            await scene.WaitFor(() => observedNavigation || observationError is not null, "east search facing");
            if (observationError is not null) throw observationError;
            navigation = 0;
            secondSearch = true;
            scene.Press(Key.Enter);
            ulong secondDeadline = Time.GetTicksMsec() + 120000;
            while (!observedSecondBattle && observationError is null && Time.GetTicksMsec() < secondDeadline)
            {
                var display = main.Dialogue?.Session?.PendingDisplay;
                if (display is not null && ((frames & 15) == 0
                    || display.Display.Current.Phase == Pal.Core.DialogueDisplayPhase.WaitingForAdvance && (frames & 3) == 0))
                    scene.Press(Key.Enter);
                frames++;
                await scene.RunnerIdle();
            }
            if (observationError is not null) throw observationError;
            AssertThat(observedSecondBattle).IsTrue();
            rounds = 0;
            // Completed here only drives input. The outer-return evidence is captured
            // synchronously on the next real committed movement, like the first cycle.
            while ((main.StoryBattle is not null || main.Dialogue?.Session?.Phase != Pal.Core.TriggerScriptPhase.Completed)
                && observationError is null && rounds++ < 3000)
            {
                if (main.StoryBattle is not null) scene.Press(Key.Enter);
                else if (main.Dialogue?.Session?.PendingDisplay is { } display
                    && ((frames & 15) == 0 || display.Display.Current.Phase == Pal.Core.DialogueDisplayPhase.WaitingForAdvance && (frames & 3) == 0))
                    scene.Press(Key.Enter);
                frames++;
                await scene.RunnerIdle();
            }
            if (observationError is not null) throw observationError;
            AssertThat(secondReturn).IsTrue();
            observeSecondMovement = true;
            scene.Press(Key.Down);
            await scene.WaitFor(() => secondMovement || observationError is not null, "second post-Won committed movement");
            if (observationError is not null) throw observationError;
            AssertThat(secondOuterReturn).IsTrue();
            AssertThat(wrongOwnedRequestRejected).IsTrue();
            AssertThat(firstReplayRejected && staleReceiptsRejected && secondReplayRejected).IsTrue();
            AssertPreparedInputsUnchanged(); // before owned Main/runner Dispose
            System.Console.WriteLine($"SYN_TWO_CLOSURES:first={firstOrigin}/{firstCall} second={secondOrigin}/{secondCall} rewards={firstRewards + secondRewards} XP={main.StoryExperienceGranted} hostCashGranted={main.StoryCashGranted} prefixes={firstPrefix}/{secondPrefix} outer={firstOuterReturn}/{secondOuterReturn}");
            scene.AssertHealthy();
        }
        finally { main.UpdateCompleted -= ObserveSharedRoles; }
        AssertPreparedInputsUnchanged(); // after runner Dispose, before fixture deletes its own files
        AssertThat(productionRandom!.IsDisposed).IsTrue();
        AssertThat(ownedDialogue!.PreviewSessionFactory).IsNull();
        AssertThat(ownedDialogue.FirstBattleWorld).IsNull();

        // Reuse these still-alive prepared URIs, never a removed Main or a re-prepared bundle.
        var fresh = new Main();
        fresh.Configure(GameSceneConfiguration.FromArguments("--m4-first-battle-profile", fixture.ProfileUri));
        bool freshInitial = false, activeCaptured = false, exitObserved = false;
        Exception? lifetimeError = null;
        Pal.Game.Dialogue.GodotBattleRandom? freshRandom = null;
        Pal.Game.Dialogue.PalTriggerDialogue? freshAdapter = null;
        Pal.Game.Dialogue.PalDialogueDisplay? freshDisplay = null;
        Pal.Game.Dialogue.PalStoryBattleView? overlay = null;
        Action? assertUnsettled = null;
        void ObserveFreshCompletion()
        {
            if (lifetimeError is not null) return;
            try
            {
                if (!freshInitial)
                {
                    freshInitial = true; // FIRST actual completion, not a wait for correct values.
                    var world = fresh.Dialogue!.FirstBattleWorld!;
                    var owner = world.RoleLiveState!;
                    var session = fresh.Dialogue.Session!;
                    freshRandom = fresh.FirstBattleRandom!;
                    AssertThat(ReferenceEquals(owner, observedOwner)).IsFalse();
                    AssertThat(ReferenceEquals(world, observedWorld)).IsFalse();
                    AssertThat(ReferenceEquals(session, observedSession)).IsFalse();
                    AssertThat(ReferenceEquals(freshRandom, productionRandom)).IsFalse();
                    AssertThat(freshRandom.IsDisposed).IsFalse();
                    AssertThat(session.Phase).IsEqual(Pal.Core.TriggerScriptPhase.Ready);
                    AssertThat(world.PartyRoles.SequenceEqual(new[] { 0, 1 })).IsTrue();
                    AssertRoleNumbers(owner.Roles[0], 100, 20, 0);
                    AssertRoleNumbers(owner.Roles[1], 100, 5, 0);
                    foreach (int id in new[] { 0, 1 })
                    {
                        AssertThat(ReferenceEquals(session.BattleRoles![id], owner.Roles[id])).IsTrue();
                        AssertThat(world.PartyVitals[id].Hp).IsEqual(owner.Roles[id].Hp);
                        AssertThat(world.PartyVitals[id].Mp).IsEqual(owner.Roles[id].Mp);
                    }
                    AssertThat(fresh.StoryCashGranted).IsEqual(0);
                    AssertThat(fresh.StoryExperienceGranted).IsEqual(0);
                    AssertPreparedInputsUnchanged();
                }
                if (!activeCaptured && fresh.StoryBattle is { } lifecycle)
                {
                    activeCaptured = true;
                    freshAdapter = fresh.Dialogue!; freshDisplay = fresh.Display!;
                    overlay = fresh.GetChildren().OfType<Pal.Game.Dialogue.PalStoryBattleView>().Single();
                    var session = freshAdapter.Session!;
                    var world = freshAdapter.FirstBattleWorld!;
                    var owner = world.RoleLiveState!;
                    var records = owner.Roles.ToDictionary(pair => pair.Key, pair => pair.Value);
                    string values = System.Text.Json.JsonSerializer.Serialize(owner.Roles);
                    var pending = session.PendingBattle!;
                    var outer = fresh.Battle!;
                    var outerPending = outer.PendingTrigger;
                    var origin = outer.PendingRequestId; var call = session.CallId;
                    var position = session.CurrentPosition; int returnEntry = session.ReturnEntry;
                    var current = lifecycle.Current; var leader = world.Leader;
                    AssertThat(pending).IsNotNull();
                    AssertThat(ReferenceEquals(pending.Lifecycle, lifecycle)).IsTrue();
                    AssertThat(lifecycle.Completion).IsNull();
                    AssertThat(lifecycle.VictorySettlement).IsNull();
                    AssertThat(origin != Guid.Empty && call != Guid.Empty).IsTrue();
                    AssertThat(outerPending).IsNotNull();
                    AssertThat(session.SourceRequestId).IsEqual(origin);
                    AssertThat(ReferenceEquals(fresh.FirstBattleRandom, freshRandom)).IsTrue();
                    AssertThat(freshRandom!.IsDisposed).IsFalse();
                    AssertRoleNumbers(owner.Roles[0], 97, 17, 0);
                    AssertRoleNumbers(owner.Roles[1], 97, 2, 0);
                    assertUnsettled = () =>
                    {
                        AssertThat(ReferenceEquals(session.PendingBattle, pending)).IsTrue();
                        AssertThat(ReferenceEquals(pending.Lifecycle, lifecycle)).IsTrue();
                        AssertThat(ReferenceEquals(lifecycle.Current, current)).IsTrue();
                        AssertThat(lifecycle.Completion).IsNull();
                        AssertThat(lifecycle.VictorySettlement).IsNull();
                        AssertThat(session.Phase).IsEqual(Pal.Core.TriggerScriptPhase.WaitingForBattle);
                        AssertThat(session.PendingDisplay).IsNull();
                        AssertThat(session.CallId).IsEqual(call);
                        AssertThat(session.SourceRequestId).IsEqual(origin);
                        AssertThat(session.CurrentPosition).IsEqual(position);
                        AssertThat(session.ReturnEntry).IsEqual(returnEntry);
                        AssertThat(session.BlocksSceneUpdates).IsTrue();
                        AssertThat(ReferenceEquals(world.RoleLiveState, owner)).IsTrue();
                        AssertThat(System.Text.Json.JsonSerializer.Serialize(owner.Roles)).IsEqual(values);
                        AssertThat(owner.Roles.Count).IsEqual(records.Count);
                        foreach (var pair in records)
                        {
                            AssertThat(ReferenceEquals(owner.Roles[pair.Key], pair.Value)).IsTrue();
                            AssertThat(ReferenceEquals(session.BattleRoles![pair.Key], pair.Value)).IsTrue();
                            AssertThat(world.PartyVitals[pair.Key].Hp).IsEqual(pair.Value.Hp);
                            AssertThat(world.PartyVitals[pair.Key].Mp).IsEqual(pair.Value.Mp);
                        }
                        AssertThat(world.Leader).IsEqual(leader);
                        AssertThat(ReferenceEquals(outer.PendingTrigger, outerPending)).IsTrue();
                        AssertThat(outer.PendingRequestId).IsEqual(origin);
                        AssertThat(fresh.StoryCashGranted).IsEqual(0);
                        AssertThat(fresh.StoryExperienceGranted).IsEqual(0);
                    };
                    assertUnsettled();
                }
            }
            catch (Exception error) { lifetimeError = error; }
        }
        void ObserveFreshExit()
        {
            exitObserved = true; // TreeExited is after NotificationExitTree; never poll cleanup.
            try
            {
                if (!freshRandom!.IsDisposed)
                    throw new InvalidOperationException("First-battle active exit must dispose its owned native RNG.");
                AssertThat(fresh.FirstBattleRandom).IsNull();
                AssertThat(freshAdapter!.PreviewSessionFactory).IsNull();
                AssertThat(freshAdapter.FirstBattleWorld).IsNull();
                AssertThat(freshAdapter.Session).IsNull();
                AssertThat(GodotObject.IsInstanceValid(overlay)).IsFalse();
                AssertThat(fresh.GetChildren().OfType<Pal.Game.Dialogue.PalStoryBattleView>().Count()).IsEqual(0);
                AssertThat(GodotObject.IsInstanceValid(freshDisplay)).IsTrue();
                AssertThat(fresh.IsInsideTree()).IsFalse();
                AssertThat(fresh.StoryBattle).IsNull();
                AssertThat(fresh.StoryBattleMode).IsNull();
                AssertThat(fresh.StoryDrawSnapshot).IsNull();
                AssertThat(fresh.StoryResultVisible).IsFalse();
                AssertThat(fresh.Dialogue).IsNull();
                AssertThat(fresh.Display).IsNull();
                AssertThat(fresh.Battle).IsNull();
                AssertThat(fresh.Exploration).IsNull();
                AssertThat(fresh.SelectedLanguage).IsNull();
                assertUnsettled!(); // The detached Core call must not acquire a fake ACK/reward/return.
                AssertPreparedInputsUnchanged();
                AssertThat(fresh.Failure).IsNull();
            }
            catch (Exception error) { lifetimeError ??= error; }
        }
        // Like StoryHost, let the runner own a container: removing Main must not detach
        // the runner's own scene and make SceneRunner.Dispose remove it a second time.
        var exitContainer = new Node();
        GdUnit4.ISceneRunner? exitRunner = null;
        var exitWindow = ((SceneTree)Engine.GetMainLoop()).Root;
        var exitMinSize = exitWindow.MinSize; var exitSize = exitWindow.Size;
        double exitTimeScale = Engine.TimeScale; int exitPhysicsTicks = Engine.PhysicsTicksPerSecond;
        var exitFocus = exitWindow.GuiGetFocusOwner();
        var freshFailures = new System.Collections.Generic.List<string>();
        void RecordFreshFailure(string failure) => freshFailures.Add(failure);
        void PressFreshEnter()
        {
            exitRunner!.SimulateKeyPress(Key.Enter);
        }
        fresh.UpdateCompleted += ObserveFreshCompletion;
        fresh.TreeExited += ObserveFreshExit;
        fresh.Failed += RecordFreshFailure;
        try
        {
            exitContainer.AddChild(fresh);
            exitRunner = GdUnit4.ISceneRunner.Load(exitContainer, autoFree: true);
            ulong initialStart = Time.GetTicksMsec();
            while (!freshInitial)
            {
                if (Time.GetTicksMsec() - initialStart > 2000)
                    throw new TimeoutException("Required production boundary not observed: same-URI Main first completion; failure=" + fresh.Failure);
                await exitRunner.AwaitIdleFrame();
            }
            if (lifetimeError is not null) throw lifetimeError;
            PressFreshEnter();
            int frames = 0;
            ulong deadline = Time.GetTicksMsec() + 10000; // Only the additional preparation guard.
            while (!activeCaptured && lifetimeError is null && Time.GetTicksMsec() < deadline)
            {
                if (fresh.Dialogue?.Session?.PendingDisplay is { } display
                    && ((frames & 15) == 0 || display.Display.Current.Phase == Pal.Core.DialogueDisplayPhase.WaitingForAdvance && (frames & 3) == 0))
                    PressFreshEnter();
                frames++;
                await exitRunner.AwaitIdleFrame();
            }
            if (lifetimeError is not null) throw lifetimeError;
            AssertThat(activeCaptured).IsTrue();
            exitRunner!.SimulateKeyRelease(Key.Enter);
            AssertThat(ReferenceEquals(fresh.GetParent(), exitContainer)).IsTrue();
            fresh.GetParent().RemoveChild(fresh); // Actual owned active tree exit, not _ExitTree().
            AssertThat(exitObserved).IsTrue();
            if (lifetimeError is not null) throw lifetimeError;
            AssertThat(exitContainer.IsInsideTree()).IsTrue();
            AssertThat(fresh.Failure).IsNull();
            AssertThat(freshFailures.Count).IsEqual(0);
        }
        finally
        {
            try
            {
                exitRunner?.SimulateKeyRelease(Key.Enter);
                fresh.UpdateCompleted -= ObserveFreshCompletion;
                fresh.TreeExited -= ObserveFreshExit;
                fresh.Failed -= RecordFreshFailure;
                exitRunner?.Dispose(); // Its container is still attached, even when Main was removed.
            }
            finally
            {
                if (GodotObject.IsInstanceValid(fresh)) fresh.Free();
                if (GodotObject.IsInstanceValid(exitContainer)) exitContainer.Free();
                // The completed exit observer records a release failure before this test-only fallback.
                // Release only this owned scene's captured RNG; never mask its recorded assertion.
                if (freshRandom is { IsDisposed: false }) freshRandom.Dispose();
                exitWindow.MinSize = exitMinSize; exitWindow.Size = exitSize;
                Engine.TimeScale = exitTimeScale; Engine.PhysicsTicksPerSecond = exitPhysicsTicks;
                if (GodotObject.IsInstanceValid(exitFocus)) exitFocus!.GrabFocus();
            }
        }
        AssertThat(GodotObject.IsInstanceValid(fresh)).IsFalse();
        AssertThat(GodotObject.IsInstanceValid(exitContainer)).IsFalse();
        AssertPreparedInputsUnchanged();
    }

    [TestCase(Timeout = 280000)]
    public async Task Synthetic_closure_Lost_ends_in_the_real_reload_todo_terminal()
    {
        AssertNativeRandomFork();
        using var battleFixture = new NativeBattleProfileFixture();
        var statics = battleFixture.Static;
        // The bound branch re-tints every scene frame when the display palette changes
        // (PalDialogueDisplay.Start fires one palette event unconditionally); the synthetic
        // fixture frames start texture-only, so fill their indices/alpha planes first.
        foreach (var frame in statics.MapScene.Data.Map.Tiles.Frames)
        {
            frame.Indices = Array.Empty<byte>().Concat(Enumerable.Repeat((byte)0, frame.Width * frame.Height)).ToArray();
            frame.Alpha = Array.Empty<byte>().Concat(Enumerable.Repeat((byte)255, frame.Width * frame.Height)).ToArray();
        }
        foreach (var sprite in statics.MapScene.Data.Sprites)
            foreach (var frame in sprite.Frames)
            {
                frame.Indices = Array.Empty<byte>().Concat(Enumerable.Repeat((byte)0, frame.Width * frame.Height)).ToArray();
                frame.Alpha = Array.Empty<byte>().Concat(Enumerable.Repeat((byte)255, frame.Width * frame.Height)).ToArray();
            }
        // The bound branch reads the active scene palette; the synthetic scene data starts
        // without one, so the authored palette leaf fills it before the scene is packed.
        statics.MapScene.Data.Palette = (Pal.Game.Resources.PalPalette)ResourceLoader.Load(
            "res://Samples/AuthoredBattle/Static/Palette.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep)!;
        statics.SavePacked();
        string staticProfile = statics.ResourceRoot + "/Profile.tres";
        NativeProfileFixture.SaveResource(statics.Profile, staticProfile);
        // VerifyRealClosure reads the active scene palette at <static>/Palettes/Palette.tres;
        // the authored sample palette leaf (copy) fills that layout for the synthetic bundle.
        System.IO.Directory.CreateDirectory(ProjectSettings.GlobalizePath(statics.ResourceRoot + "/Palettes"));
        foreach (string file in new[] { "Palette.tres" })
            System.IO.File.Copy(GameProjectMount.GameSamplePath("Samples/AuthoredBattle/Static/" + file),
                ProjectSettings.GlobalizePath(statics.ResourceRoot + "/Palettes/" + file));
        using var fixture = new SyntheticFirstBattleFixture(statics, statics.ResourceRoot, staticProfile, lost: true);
        // Captured before any input in U6's real Main completion; this selects a Lost
        // scenario, not the unknown seed/cause of U4's original deadline failure.
        const ulong replayInitialSeed = 16843242877940428531UL;
        const ulong replayInitialState = 1605564118000691017UL;
        var selection = GameSceneConfiguration.FromArguments("--m4-first-battle-profile", fixture.ProfileUri);
        var main = new Main();
        Pal.Core.DynamicEventWorld? observedWorld = null;
        Pal.Core.RoleLiveState? observedOwner = null;
        Pal.Core.TriggerScriptSession? observedSession = null;
        Pal.Game.Dialogue.GodotBattleRandom? observedRandom = null;
        Pal.Core.ClassicBattleLifecycleSession? firstLifecycle = null;
        Pal.Core.ClassicBattleLifecycleSession.CompletionReceipt? firstCompletion = null;
        Guid firstCall = Guid.Empty;
        Guid? firstOrigin = null;
        Exception? observationError = null;
        bool observedStartup = false, inputSent = false;
        void ConfigureReplay(ulong seed) => main.Configure(selection, seed);
        void RejectConfigure(Action configure)
        {
            bool rejected = false;
            try { configure(); }
            catch (InvalidOperationException) { rejected = true; }
            AssertThat(rejected).IsTrue();
        }
        void OnMainCompleted()
        {
            if (observationError is not null) return;
            try
            {
                var world = main.Dialogue!.FirstBattleWorld!;
                var owner = world.RoleLiveState!;
                var session = main.Dialogue.Session!;
                var random = main.FirstBattleRandom!;
                if (!observedStartup)
                {
                    observedStartup = true;
                    AssertThat(inputSent).IsFalse();
                    AssertThat(session.Phase).IsEqual(Pal.Core.TriggerScriptPhase.Ready);
                    AssertThat(random.CurrentState).IsEqual((replayInitialSeed, replayInitialState));
                    observedWorld = world; observedOwner = owner;
                    observedSession = session; observedRandom = random;
                }
                AssertThat(ReferenceEquals(world, observedWorld)).IsTrue();
                AssertThat(ReferenceEquals(owner, observedOwner)).IsTrue();
                AssertThat(ReferenceEquals(session, observedSession)).IsTrue();
                AssertThat(ReferenceEquals(random, observedRandom)).IsTrue();
                AssertThat(random.IsDisposed).IsFalse();
                AssertThat(random.CurrentState.Seed).IsEqual(replayInitialSeed);
                foreach (int roleId in new[] { 0, 1 })
                    AssertThat(ReferenceEquals(session.BattleRoles![roleId], owner.Roles[roleId])).IsTrue();
                if (firstLifecycle is null && main.StoryBattle is { } lifecycle)
                {
                    firstLifecycle = lifecycle;
                    firstCall = session.CallId; firstOrigin = session.SourceRequestId;
                    AssertThat(firstCall != Guid.Empty).IsTrue();
                    AssertThat(firstOrigin).IsEqual(main.Battle!.PendingRequestId);
                    AssertThat(firstOrigin != Guid.Empty).IsTrue();
                }
                if (firstLifecycle is not null)
                {
                    AssertThat(session.CallId).IsEqual(firstCall);
                    AssertThat(session.SourceRequestId).IsEqual(firstOrigin);
                    if (main.StoryBattle is { } active)
                        AssertThat(ReferenceEquals(active, firstLifecycle)).IsTrue();
                    if (firstCompletion is null && firstLifecycle.Completion is { } receipt)
                    {
                        // First real owned receipt, sampled inside Main's completion callback.
                        firstCompletion = receipt;
                        AssertThat(firstLifecycle.OwnsCompletion(receipt)).IsTrue();
                        AssertThat(receipt.Outcome).IsEqual(Pal.Core.ClassicBattlePhase.Lost);
                    }
                }
            }
            catch (Exception error) { observationError = error; } // Never become Main.Failure.
        }
        try
        {
            ConfigureReplay(replayInitialSeed);
            RejectConfigure(() => ConfigureReplay(replayInitialSeed + 1));
            RejectConfigure(() => main.Configure(selection));
            main.UpdateCompleted += OnMainCompleted; // Before Load's immediate AddChild/_Ready.
            using var scene = new RealFirstBattleFlowTests.RealFlowScene(main);
            await scene.WaitFor(() => scene.Updates.Count > 0, "first update");
            if (observationError is not null) throw observationError;
            AssertThat(observedStartup).IsTrue();
            var beforeRejectedConfigure = observedRandom!.CurrentState;
            RejectConfigure(() => ConfigureReplay(replayInitialSeed + 1));
            RejectConfigure(() => main.Configure(selection));
            AssertThat(observedRandom.CurrentState).IsEqual(beforeRejectedConfigure);
            AssertThat(ReferenceEquals(main.FirstBattleRandom, observedRandom)).IsTrue();
            inputSent = true;
            scene.Press(Key.Enter);
            int frames = 0;
            ulong deadline = Time.GetTicksMsec() + 120000;
            while (!main.StoryLostTerminal && Time.GetTicksMsec() < deadline)
            {
                var display = main.Dialogue?.Session?.PendingDisplay;
                if (main.StoryBattle is not null) scene.Press(Key.Enter);
                else if (display is not null && ((frames & 15) == 0
                    || display.Display.Current.Phase == Pal.Core.DialogueDisplayPhase.WaitingForAdvance && (frames & 3) == 0))
                    scene.Press(Key.Enter);
                frames++;
                await scene.RunnerIdle();
            }
            System.Console.WriteLine($"SYN_LOST:frames={frames} lost={main.StoryLostTerminal} diagnostics={main.StoryLostReloadDiagnostics}");
            AssertThat(main.StoryLostTerminal).IsTrue();
            AssertThat(main.StoryLostReloadDiagnostics).IsEqual(1);
            scene.AssertHealthy();
            if (observationError is not null) throw observationError;
            AssertThat(firstCompletion).IsNotNull();
        }
        finally
        {
            main.UpdateCompleted -= OnMainCompleted;
            if (GodotObject.IsInstanceValid(main)) main.Free();
        }
    }

    private static void AssertRoleNumbers(Pal.Core.BattleRoleSnapshot role, int hp, int mp, int experience)
    {
        AssertThat(role.Hp).IsEqual(hp);
        AssertThat(role.Mp).IsEqual(mp);
        AssertThat(role.MaxHp).IsEqual(role.RoleId == 0 ? 150 : 100);
        AssertThat(role.MaxMp).IsEqual(role.RoleId == 0 ? 50 : 5);
        AssertThat(role.Level).IsEqual(2);
        AssertThat(role.PrimaryExperience.Value).IsEqual(experience);
    }

    private static void AssertCurrentPreview(Main main)
    {
        var source = main.Dialogue!.Session!;
        var world = main.Dialogue.FirstBattleWorld!;
        var random = main.FirstBattleRandom!;
        var state = random.CurrentState;
        var phase = source.Phase;
        var call = source.CallId;
        var origin = source.SourceRequestId;
        var leader = world.Leader;
        var objects = world.Objects.Select(obj => (obj.Id, obj.X, obj.Y, obj.Direction, obj.CurrentFrame,
            obj.State, obj.VanishTime, obj.TriggerEntry, obj.AutoEntry, obj.AutoIdleCount)).ToArray();
        var roles = world.RoleLiveState!.Roles.ToDictionary(pair => pair.Key, pair => pair.Value);
        var lease = main.Dialogue.PreviewSessionFactory!();
        var fork = (Pal.Game.Dialogue.GodotBattleRandom)lease.Lifetime;
        using (lease.Lifetime)
        {
            var preview = lease.Session;
            AssertThat(fork.CurrentState).IsEqual(state);
            AssertThat(preview.Context.Color).IsEqual(source.Context.Color);
            foreach (int roleId in new[] { 0, 1 })
            {
                var previewRole = preview.BattleRoles![roleId];
                var currentRole = roles[roleId];
                if (previewRole.Level != currentRole.Level || previewRole.Hp != currentRole.Hp
                    || previewRole.Mp != currentRole.Mp || previewRole.MaxHp != currentRole.MaxHp
                    || previewRole.PrimaryExperience != currentRole.PrimaryExperience)
                    throw new InvalidOperationException("U3 preview must copy NOW roles after the owned settlement, not startup templates.");
                AssertThat(ReferenceEquals(previewRole, currentRole)).IsFalse();
                AssertThat(previewRole.HiddenExperience.SequenceEqual(currentRole.HiddenExperience)).IsTrue();
                AssertThat(previewRole.Equipment.SequenceEqual(currentRole.Equipment)).IsTrue();
                AssertThat(previewRole.Magics.SequenceEqual(currentRole.Magics)).IsTrue();
                AssertThat(previewRole.Statuses.SequenceEqual(currentRole.Statuses)).IsTrue();
            }
            // Exercise the actual factory's world verbs and park at battle without issuing a
            // battle receipt. Neither this throwaway call nor its RNG draws own production.
            var entry = preview.Entries.Single(e => e.OwnerId == 28 && e.Slot == "wTriggerScript");
            preview.Start(entry.FileId, entry.Id, 28);
            while (preview.PendingDisplay is { } display)
            {
                while (display.Display.Current.Phase != Pal.Core.DialogueDisplayPhase.Completed)
                    display.Display.Update(display.Display.Current.Phase == Pal.Core.DialogueDisplayPhase.WaitingForAdvance ? 100u : int.MaxValue, true, true);
                preview.AcknowledgeDisplay(display.Id);
            }
            AssertThat(preview.PendingBattle).IsNotNull();
            fork.NextInclusive(0, 1000); fork.NextInclusive(0f, 1f);
        }
        AssertThat(fork.IsDisposed).IsTrue();
        AssertThat(random.IsDisposed).IsFalse();
        AssertThat(random.CurrentState).IsEqual(state);
        AssertThat(source.Phase).IsEqual(phase);
        AssertThat(source.CallId).IsEqual(call);
        AssertThat(source.SourceRequestId).IsEqual(origin);
        AssertThat(world.Leader).IsEqual(leader);
        AssertThat(world.PartyRoles.SequenceEqual(new[] { 0, 1 })).IsTrue();
        AssertThat(world.Objects.Select(obj => (obj.Id, obj.X, obj.Y, obj.Direction, obj.CurrentFrame,
            obj.State, obj.VanishTime, obj.TriggerEntry, obj.AutoEntry, obj.AutoIdleCount)).SequenceEqual(objects)).IsTrue();
        foreach (var pair in roles) AssertThat(ReferenceEquals(pair.Value, world.RoleLiveState.Roles[pair.Key])).IsTrue();
    }

    private static void AssertNativeRandomFork()
    {
        const ulong seed = 928451;
        using var native = new RandomNumberGenerator { Seed = seed };
        using var source = new Pal.Game.Dialogue.GodotBattleRandom(seed);
        for (int i = 0; i < 7; i++)
        {
            AssertThat(source.NextInclusive(-20, 300)).IsEqual(native.RandiRange(-20, 300));
            AssertThat(source.NextInclusive(-1f, 2f)).IsEqual(native.RandfRange(-1f, 2f));
        }
        var state = source.CurrentState;
        var first = source.Fork();
        using var second = source.Fork();
        try
        {
            using var expected = new RandomNumberGenerator { Seed = native.Seed, State = native.State };
            for (int i = 0; i < 12; i++)
            {
                AssertThat(first.NextInclusive(-100, 100)).IsEqual(expected.RandiRange(-100, 100));
                AssertThat(first.NextInclusive(-3f, 7f)).IsEqual(expected.RandfRange(-3f, 7f));
            }
            AssertThat(source.CurrentState).IsEqual(state);
            AssertThat(second.CurrentState).IsEqual(state);
        }
        finally { first.Dispose(); }
        AssertThat(first.IsDisposed).IsTrue();
        AssertThat(second.IsDisposed).IsFalse();
        int next = native.RandiRange(-100, 100);
        AssertThat(second.NextInclusive(-100, 100)).IsEqual(next);
        AssertThat(source.CurrentState).IsEqual(state);
        AssertThat(source.NextInclusive(-100, 100)).IsEqual(next);
    }

    private static string PathCombine(string a, string b) => System.IO.Path.Combine(a, b).Replace(System.IO.Path.DirectorySeparatorChar, '/');
}
