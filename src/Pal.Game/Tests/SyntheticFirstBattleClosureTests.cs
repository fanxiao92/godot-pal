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
        using var scene = new RealFirstBattleFlowTests.RealFlowScene(main);
        await scene.WaitFor(() => scene.Updates.Count > 0, "first update");
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
        scene.Press(Key.Down); // post-battle exploration owns committed updates again
        await scene.WaitFor(() => scene.Updates.Count > 0, "changed exploration update");
        scene.AssertHealthy();
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

    private static string PathCombine(string a, string b) => System.IO.Path.Combine(a, b).Replace(System.IO.Path.DirectorySeparatorChar, '/');
}
