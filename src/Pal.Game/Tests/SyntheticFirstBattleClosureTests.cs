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
            System.IO.File.Copy(ProjectSettings.GlobalizePath("res://Samples/AuthoredBattle/Static/" + file),
                ProjectSettings.GlobalizePath(statics.ResourceRoot + "/Palettes/" + file));
        using var fixture = new SyntheticFirstBattleFixture(statics, statics.ResourceRoot, staticProfile, lost: false);
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
        void ObserveSharedRoles()
        {
            if (observationError is not null) return;
            try
            {
                var world = main.Dialogue?.FirstBattleWorld;
                var owner = world?.RoleLiveState;
                var session = main.Dialogue?.Session;
                if (owner is null || session is null)
                    throw new InvalidOperationException("Missing shared role owner or trigger session at Main.UpdateCompleted.");
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
                    ReleaseKey(Key.Down);
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
                }
                if (navigation != 0 && !observedNavigation)
                {
                    observedNavigation = true;
                    ReleaseKey(navigation == 1 ? Key.Up : Key.Right);
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
                    AssertThat(ReferenceEquals(firstLifecycle, secondLifecycle)).IsFalse();
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
                    ReleaseKey(Key.Down);
                    AssertThat(secondOuterReturn).IsTrue();
                    AssertThat(main.Exploration!.IsWalking).IsTrue();
                    AssertThat(main.Exploration.Leader.X).IsEqual((short)1248);
                    AssertThat(main.Exploration.Leader.Y).IsEqual((short)256);
                    AssertRoleNumbers(owner.Roles[0], 135, 40, 104);
                    AssertRoleNumbers(owner.Roles[1], 97, 2, 104);
                    AssertCurrentPreview(main);
                }
            }
            catch (Exception error) { observationError = error; }
        }
        main.UpdateCompleted += ObserveSharedRoles; // before RealFlowScene adds Main to the tree
        try
        {
            using var scene = new RealFirstBattleFlowTests.RealFlowScene(main);
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
            int rounds = 0;
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
            System.Console.WriteLine($"SYN_TWO_CLOSURES:first={firstOrigin}/{firstCall} second={secondOrigin}/{secondCall} rewards={firstRewards + secondRewards} XP=104 cash=192 prefixes={firstPrefix}/{secondPrefix} outer={firstOuterReturn}/{secondOuterReturn}");
            scene.AssertHealthy();
        }
        finally { main.UpdateCompleted -= ObserveSharedRoles; }
        AssertThat(productionRandom!.IsDisposed).IsTrue();
        AssertThat(ownedDialogue!.PreviewSessionFactory).IsNull();
        AssertThat(ownedDialogue.FirstBattleWorld).IsNull();
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
            System.IO.File.Copy(ProjectSettings.GlobalizePath("res://Samples/AuthoredBattle/Static/" + file),
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

    private static void ReleaseKey(Key key)
    {
        using var input = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false };
        Input.ParseInputEvent(input);
        Input.FlushBufferedEvents();
    }

    private static string PathCombine(string a, string b) => System.IO.Path.Combine(a, b).Replace(System.IO.Path.DirectorySeparatorChar, '/');
}
