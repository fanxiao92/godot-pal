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
        Pal.Core.DynamicEventWorld? observedWorld = null;
        Pal.Core.RoleLiveState? observedOwner = null;
        Pal.Core.TriggerScriptSession? observedSession = null;
        Pal.Core.BattleRoleSnapshot? initialRoleOne = null;
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
                if (owner is null)
                    throw new InvalidOperationException("U1 missing shared role owner at Main.UpdateCompleted.");
                if (observedOwner is null)
                {
                    observedWorld = world; observedOwner = owner; observedSession = session;
                    initialRoleOne = owner.Roles[1];
                    AssertThat(world!.PartyRoles.SequenceEqual(new[] { 0, 1 })).IsTrue();
                    AssertThat(initialRoleOne.Hp).IsEqual(100);
                    AssertThat(initialRoleOne.Mp).IsEqual(5);
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
                if (main.StoryBattle is not null && !observedBattle)
                {
                    observedBattle = true;
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
                    AssertThat(ReferenceEquals(initialRoleOne, owner.Roles[1])).IsFalse();
                    AssertThat(initialRoleOne!.Hp).IsEqual(100);
                    AssertThat(initialRoleOne.Mp).IsEqual(5);
                }
                if (observeMovement && !observedMovement)
                {
                    // Sample the FIRST actual completion after Down, even if it is wrong.
                    observedMovement = true;
                    ReleaseKey(Key.Down);
                    var current = main.Exploration!;
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
                    AssertThat(ReferenceEquals(returnedRoleOne, owner.Roles[1])).IsTrue();
                    AssertThat(main.StoryBattle.Current.Combat.Players.Select(player => player.RoleId)
                        .SequenceEqual(new[] { 0, 1 })).IsTrue();
                }
            }
            catch (Exception error) { observationError = error; }
        }
        main.UpdateCompleted += ObserveSharedRoles; // before RealFlowScene adds Main to the tree
        try
        {
            using var scene = new RealFirstBattleFlowTests.RealFlowScene(main);
            await scene.WaitFor(() => scene.Updates.Count > 0, "first update");
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
            scene.AssertHealthy();
        }
        finally { main.UpdateCompleted -= ObserveSharedRoles; }
    }

    [TestCase(Timeout = 280000)]
    public async Task Synthetic_closure_Lost_ends_in_the_real_reload_todo_terminal()
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
        using var fixture = new SyntheticFirstBattleFixture(statics, statics.ResourceRoot, staticProfile, lost: true);
        var main = new Main();
        main.Configure(GameSceneConfiguration.FromArguments("--m4-first-battle-profile", fixture.ProfileUri));
        using var scene = new RealFirstBattleFlowTests.RealFlowScene(main);
        await scene.WaitFor(() => scene.Updates.Count > 0, "first update");
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
    }

    private static void ReleaseKey(Key key)
    {
        using var input = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false };
        Input.ParseInputEvent(input);
        Input.FlushBufferedEvents();
    }

    private static string PathCombine(string a, string b) => System.IO.Path.Combine(a, b).Replace(System.IO.Path.DirectorySeparatorChar, '/');
}
