using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Pal.Core;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

[TestSuite, RequireGodotRuntime]
public class MainSceneTests
{
    [TestCase(Timeout = 20000)]
    public async Task ConfigurationIsRequiredAndCanOnlyBeProvidedOnceBeforeTree()
    {
        using (var missing = new EmbeddedScene(new Main()))
        {
            AssertThat(missing.Failures.Count).IsEqual(1);
            AssertThat(missing.Main.Failure!.Contains("Configure the scene before entering the tree.")).IsTrue();
            await missing.AssertStopped();
        }
        using var fixture = new ExplorationFixture();
        fixture.Native.SavePacked();
        var selection = Select(fixture.Native);
        var main = new Main();
        try
        {
            main.Configure(selection);
            RejectConfigure(main, selection);
            using var scene = new EmbeddedScene(main);
            RejectConfigure(main, selection);
            var initial = main.Exploration;
            AssertThat(initial).IsNotNull();
            AssertThat((initial!.Leader.X, initial.Leader.Y)).IsEqual(((short)320, (short)320));
            await scene.WaitFor(() => scene.Updates.Count > 0, "actual initial update");
            var firstUpdate = main.Exploration;
            int initialCompletions = scene.Updates.Count;
            await scene.WaitFor(() => scene.Updates.Count > initialCompletions,
                "non-battle no-change callback completion");
            AssertThat(ReferenceEquals(main.Exploration, firstUpdate)).IsTrue();
            scene.AssertHealthy();
            AssertThat(main.GetChildren().OfType<Pal.Game.Resources.PalMapScene>().Count()).IsEqual(1);
        }
        finally { if (GodotObject.IsInstanceValid(main)) main.Free(); }
        AssertThat(GodotObject.IsInstanceValid(main)).IsFalse();
    }

    [TestCase(Timeout = 20000)]
    public async Task ResourceFailureReportsOnceAndStopsWithoutQuittingHost()
    {
        string root = "res://Assets/RuntimeTests/Missing-Embedded-" + Guid.NewGuid().ToString("N");
        var selection = GameSceneConfiguration.FromArguments("--m2-resources", root,
            "--m2-profile", "res://Tests/Missing-Embedded.tres");
        using (var scene = new EmbeddedScene(Configured(selection)))
        {
            AssertThat(scene.Failures.Count).IsEqual(1);
            AssertThat(scene.Main.Failure!.Contains(root + "/Scenes/Scene.tscn")).IsTrue();
            AssertThat(scene.Main.Failure.Contains("profile=res://Tests/Missing-Embedded.tres")).IsTrue();
            AssertThat(scene.Main.Exploration).IsNull();
            RejectConfigure(scene.Main, selection);
            await scene.AssertStopped();
        }
        AssertThat(Directory.Exists(ProjectSettings.GlobalizePath(root))).IsFalse();
        // Fail after the map/display/adapter have been added, not only before any children exist.
        using var partial = new NpcInteractionFixture();
        partial.CopyDialogue();
        string language = ProjectSettings.GlobalizePath(partial.Profile.DialogueDirectory + "/Language.zh-Hans.tres");
        File.WriteAllText(language, File.ReadAllText(language).Replace("Language = \"zh-Hans\"", "Language = \"en\""));
        partial.Rehash();
        Main failed;
        Pal.Game.Dialogue.PalDialogueDisplay display;
        using (var scene = new EmbeddedScene(Configured(GameSceneConfiguration.FromArguments("--m3-dialogue-profile", partial.Uri))))
        {
            failed = scene.Main;
            display = failed.Display!;
            AssertThat(display).IsNotNull();
            AssertThat(failed.Dialogue).IsNotNull();
            AssertThat(failed.Failure!.Contains("selected language identity")).IsTrue();
            AssertThat(scene.Failures.Count).IsEqual(1);
            await scene.AssertStopped();
        }
        AssertThat(GodotObject.IsInstanceValid(failed)).IsFalse();
        AssertThat(GodotObject.IsInstanceValid(display)).IsFalse();
    }

    [TestCase(Timeout = 20000)]
    public async Task RuntimeFailureStopsAndFollowingSceneStartsWithIndependentInputAndResources()
    {
        using var broken = new ExplorationFixture();
        var sprite = broken.Native.Scene.Sprites[0];
        sprite.FrameCount = 1;
        while (sprite.Frames.Count > 1) sprite.Frames.RemoveAt(1);
        broken.Native.SavePacked();
        Main old;
        using (var scene = new EmbeddedScene(Configured(Select(broken.Native))))
        {
            old = scene.Main;
            scene.AssertHealthy();
            scene.Press(Key.Down);
            await scene.WaitFor(() => old.Failure is not null, "walking frame failure");
            AssertThat(old.Failure).IsEqual("Exploration update failed: Leader sprite 17 is missing frame 1.");
            AssertThat(scene.Failures.Count).IsEqual(1);
            await scene.AssertStopped();
        }
        AssertThat(GodotObject.IsInstanceValid(old)).IsFalse();
        AssertThat(Input.IsActionPressed("exploration_south")).IsFalse();
        using var next = new ExplorationFixture();
        next.Native.SavePacked();
        string uri = next.Native.ResourceRoot + "/Scenes/Scene.tscn";
        string hash = Godot.FileAccess.GetSha256(uri);
        using (var scene = new EmbeddedScene(Configured(Select(next.Native))))
        {
            AssertThat((scene.Main.Exploration!.Leader.X, scene.Main.Exploration.Leader.Y)).IsEqual(((short)320, (short)320));
            var first = await scene.FirstSouthStep();
            AssertThat((first.Leader.X, first.Leader.Y, first.Plan.LeaderFrameIndex)).IsEqual(((short)304, (short)328, 1));
            AssertThat(next.Native.Scene.Sprites[0].FrameCount).IsEqual(12);
            AssertThat(Godot.FileAccess.GetSha256(uri)).IsEqual(hash);
        }
        // Same URI is intentionally reused to exercise the shared ResourceLoader cache.
        using (var scene = new EmbeddedScene(Configured(Select(next.Native))))
        {
            AssertThat((scene.Main.Exploration!.Leader.X, scene.Main.Exploration.Leader.Y)).IsEqual(((short)320, (short)320));
            var first = await scene.FirstSouthStep();
            AssertThat((first.Leader.X, first.Leader.Y)).IsEqual(((short)304, (short)328));
            AssertThat(Godot.FileAccess.GetSha256(uri)).IsEqual(hash);
        }
    }

    [TestCase(Timeout = 20000)]
    public async Task ProfileFailureFreesLoadedMapAndFollowingSceneCanMove()
    {
        using var broken = new ExplorationFixture();
        broken.Native.Profile.FormatVersion = 2;
        broken.Native.SavePacked();
        Main old;
        using (var scene = new EmbeddedScene(Configured(Select(broken.Native))))
        {
            old = scene.Main;
            AssertThat(old.Failure!.Contains("FormatVersion")).IsTrue();
            AssertThat(old.Failure.Contains(broken.Native.ResourceRoot + "/Scenes/Scene.tscn")).IsTrue();
            AssertThat(old.Failure.Contains(broken.Native.Uri)).IsTrue();
            AssertThat(scene.Failures.Count).IsEqual(1);
            AssertThat(old.Exploration).IsNull();
            AssertThat(old.GetChildCount()).IsEqual(0);
            await scene.AssertStopped();
        }
        AssertThat(GodotObject.IsInstanceValid(old)).IsFalse();
        using var next = new ExplorationFixture();
        next.Native.SavePacked();
        using var healthy = new EmbeddedScene(Configured(Select(next.Native)));
        var first = await healthy.FirstSouthStep();
        AssertThat((first.Leader.X, first.Leader.Y, first.Leader.StepPhase, first.Plan.LeaderFrameIndex))
            .IsEqual(((short)304, (short)328, 1, 1));
    }

    [TestCase(Timeout = 20000)]
    public async Task NpcSceneFailureStopsDisplayAndFollowingSceneCanInteract()
    {
        using var broken = new NpcInteractionFixture();
        broken.CopyDialogue();
        string path = ProjectSettings.GlobalizePath(broken.Profile.DialogueDirectory + "/scene-004.json");
        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        var command = root["commands"]![0]!;
        foreach (string key in new[] { "box", "portraitId", "color", "rngWord" }) command.AsObject().Remove(key);
        command["op"] = "raw"; command["source"]!["opcode"] = 0x7777;
        File.WriteAllText(path, root.ToJsonString()); broken.Rehash();
        Main old;
        using (var scene = new EmbeddedScene(Configured(GameSceneConfiguration.FromArguments("--m3-dialogue-profile", broken.Uri))))
        {
            old = scene.Main;
            scene.AssertHealthy();
            scene.Press(Key.Enter);
            await scene.WaitFor(() => old.Failure is not null, "unsupported trigger failure");
            AssertThat(old.Failure!.Contains("unsupported", StringComparison.OrdinalIgnoreCase)).IsTrue();
            AssertThat(scene.Failures.Count).IsEqual(1);
            await scene.AssertStopped();
        }
        AssertThat(GodotObject.IsInstanceValid(old)).IsFalse();
        using var next = new NpcInteractionFixture();
        string hash = Godot.FileAccess.GetSha256(next.Uri);
        using var healthy = new EmbeddedScene(Configured(GameSceneConfiguration.FromArguments("--m3-dialogue-profile", next.Uri)));
        healthy.Press(Key.Enter);
        await healthy.WaitFor(() => healthy.Main.Display!.Session is not null, "next scene starts real dialogue");
        healthy.AssertHealthy();
        AssertThat(healthy.Main.Interaction!.Phase).IsEqual(NpcInteractionPhase.Trigger);
        AssertThat(Godot.FileAccess.GetSha256(next.Uri)).IsEqual(hash);
        // A consumer failure at the actual display boundary must use the same failure channel.
        Action rejectedReceipt = () => throw new InvalidOperationException("MAIN_SCENE_EXPECTED_DISPLAY_RECEIPT_FAILURE");
        healthy.Main.Display!.UpdateCompleted += rejectedReceipt;
        try
        {
            await healthy.WaitFor(() => healthy.Main.Failure is not null, "display receipt failure");
            AssertThat(healthy.Main.Failure).IsEqual("Dialogue update failed: MAIN_SCENE_EXPECTED_DISPLAY_RECEIPT_FAILURE");
            AssertThat(healthy.Failures.Count).IsEqual(1);
            await healthy.AssertStopped();
        }
        finally { healthy.Main.Display.UpdateCompleted -= rejectedReceipt; }
    }

    [TestCase(Timeout = 20000)]
    public async Task FirstBattleInitializationFailureUsesMainFailureChannelWithoutPartialScene()
    {
        using var fixture = new NativeBattleProfileFixture();
        fixture.Static.Scene.Objects.Single(item => item.Id == 28).WTriggerScript = 0x09fe;
        fixture.Static.SavePacked();
        fixture.Native.StaticSceneSha256 = Godot.FileAccess.GetSha256(fixture.Static.ResourceRoot + "/Scenes/Scene.tscn");
        fixture.Save();
        using var scene = new EmbeddedScene(Configured(GameSceneConfiguration.FromArguments("--m4-first-battle-profile", fixture.Uri)));
        AssertThat(scene.Main.Failure!.Contains("owner28 reference identity mismatch", StringComparison.Ordinal)).IsTrue();
        AssertThat(scene.Main.Failure.Contains("selected-profile=" + fixture.Uri, StringComparison.Ordinal)).IsTrue();
        AssertThat(scene.Main.Failure.Contains("scene=" + fixture.Static.ResourceRoot + "/Scenes/Scene.tscn", StringComparison.Ordinal)).IsTrue();
        AssertThat(scene.Main.Failure.Contains("static-profile=" + fixture.Static.Uri, StringComparison.Ordinal)).IsTrue();
        AssertThat(scene.Main.Failure.Contains("@09FE", StringComparison.OrdinalIgnoreCase)).IsFalse();
        AssertThat(scene.Main.Failure.Contains("battle-complete", StringComparison.OrdinalIgnoreCase)).IsFalse();
        AssertThat(scene.Failures.Count).IsEqual(1);
        AssertThat(scene.Main.Battle).IsNull();
        AssertThat(scene.Main.Exploration).IsNull();
        AssertThat(scene.Main.SelectedLanguage).IsNull();
        AssertThat(scene.Main.GetChildCount()).IsEqual(0);
        await scene.AssertStopped();
    }

    [TestCase(Timeout = 20000)]
    public async Task FirstBattleContextStartsFromCoreStateAndRejectsOwner28BeforeSideEffects()
    {
        using var fixture = new NativeBattleProfileFixture();
        string nativeHash = Godot.FileAccess.GetSha256(fixture.Uri);
        var selection = GameSceneConfiguration.FromArguments("--m4-first-battle-profile", fixture.Uri,
            "--m4-first-battle-language", "en");
        Main old;
        using (var scene = new EmbeddedScene(Configured(selection)))
        {
            old = scene.Main;
            scene.AssertHealthy();
            AssertThat(old.SelectedLanguage).IsEqual("en");
            AssertThat((old.Exploration!.Leader.X, old.Exploration.Leader.Y, old.Exploration.Leader.Direction))
                .IsEqual(((short)1264, (short)248, 3));
            AssertThat(old.Battle!.State.PartyRoleIds.SequenceEqual(new[] { 0 })).IsTrue();
            AssertThat(old.Battle.State.World.EventObjects.Single(item => item.OwnerId == 28).State).IsEqual(2);
            await scene.WaitFor(() => scene.Updates.Count > 0, "first actual M4 update completion");
            int completedUpdates = scene.Updates.Count;
            var before = old.Exploration;
            scene.Press(Key.Enter);
            await scene.WaitFor(() => old.Failure is not null, "owner28 unsupported closure rejection");
            AssertThat(old.Failure).IsEqual("Exploration update failed: " + FirstMandatoryBattleSession.UnsupportedTriggerDiagnostic);
            AssertThat(ReferenceEquals(old.Exploration, before)).IsTrue();
            // First-battle preflight callbacks without a committed update, including the
            // rejected @09C3 callback, must not publish completion.
            AssertThat(scene.Updates.Count).IsEqual(completedUpdates);
            AssertThat(old.Battle.State.World.EventObjects.Single(item => item.OwnerId == 28).TriggerEntry).IsEqual(0x09c3);
            AssertThat(scene.Failures.Count).IsEqual(1);
        }
        AssertThat(GodotObject.IsInstanceValid(old)).IsFalse();
        using (var next = new EmbeddedScene(Configured(selection)))
        {
            next.AssertHealthy();
            AssertThat(next.Main.Battle!.State.Roles[0].Hp).IsEqual(150);
            AssertThat(next.Main.Battle.State.World.EventObjects.Single(item => item.OwnerId == 28).State).IsEqual(2);
            AssertThat((next.Main.Exploration!.Leader.X, next.Main.Exploration.Leader.Y)).IsEqual(((short)1264, (short)248));
        }
        AssertThat(Godot.FileAccess.GetSha256(fixture.Uri)).IsEqual(nativeHash);
        AssertThat(fixture.Static.Scene.Objects.Single(item => item.Id == 28).SState).IsEqual(1);
    }

    private static GameSceneConfiguration Select(NativeProfileFixture fixture) =>
        GameSceneConfiguration.FromArguments("--m2-resources", fixture.ResourceRoot, "--m2-profile", fixture.Uri);

    private static Main Configured(GameSceneConfiguration selection)
    {
        var main = new Main();
        main.Configure(selection);
        return main;
    }

    private static void RejectConfigure(Main main, GameSceneConfiguration selection)
    {
        bool rejected = false;
        try { main.Configure(selection); }
        catch (InvalidOperationException) { rejected = true; }
        AssertThat(rejected).IsTrue();
    }

    // Narrow host for the initialization contract only; no alternative game assembly or update path.
    private sealed class EmbeddedScene : IDisposable
    {
        private readonly ISceneRunner runner;
        private readonly Window window = ((SceneTree)Engine.GetMainLoop()).Root;
        private readonly Vector2I minSize;
        private readonly Vector2I size;
        private readonly double timeScale;
        private readonly int physicsTicks;
        private readonly Control? focus;
        private readonly HashSet<Key> keys = [];
        public Main Main { get; }
        public List<string> Failures { get; } = [];
        public List<ExplorationState> Updates { get; } = [];

        public EmbeddedScene(Main main)
        {
            Main = main;
            minSize = window.MinSize; size = window.Size;
            timeScale = Engine.TimeScale; physicsTicks = Engine.PhysicsTicksPerSecond;
            focus = window.GuiGetFocusOwner();
            main.Failed += OnFailed;
            main.UpdateCompleted += OnUpdated;
            try { runner = ISceneRunner.Load(main, autoFree: true); }
            catch
            {
                main.Failed -= OnFailed;
                main.UpdateCompleted -= OnUpdated;
                runner?.Dispose();
                if (GodotObject.IsInstanceValid(main)) main.Free();
                RestoreGlobals();
                throw;
            }
        }

        private void OnFailed(string message) => Failures.Add(message);
        private void OnUpdated() => Updates.Add(Main.Exploration!);
        public void AssertHealthy()
        {
            AssertThat(Main.Failure).IsNull();
            AssertThat(Failures.Count).IsEqual(0);
        }
        public void Press(Key key)
        {
            keys.Add(key);
            SendKey(key, true);
        }
        private void Release(Key key)
        {
            SendKey(key, false);
            keys.Remove(key);
        }
        private static void SendKey(Key key, bool pressed)
        {
            using var input = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed };
            Input.ParseInputEvent(input);
            Input.FlushBufferedEvents();
        }
        public async Task WaitFor(Func<bool> predicate, string boundary)
        {
            ulong start = Time.GetTicksMsec();
            while (!predicate())
            {
                if (Time.GetTicksMsec() - start > 2000)
                    throw new TimeoutException("Required production boundary not observed: " + boundary + "; failure=" + Main.Failure);
                await runner.AwaitIdleFrame();
            }
        }
        public async Task<ExplorationState> FirstSouthStep()
        {
            ExplorationState? first = null;
            void Capture()
            {
                if (first is not null) return;
                first = Main.Exploration;
                Release(Key.Down);
            }
            Main.UpdateCompleted += Capture;
            try
            {
                Press(Key.Down);
                await WaitFor(() => first is not null || Main.Failure is not null, "first actual south update");
                AssertHealthy();
                AssertThat(first).IsNotNull();
                return first!;
            }
            finally { Main.UpdateCompleted -= Capture; Release(Key.Down); }
        }
        public async Task AssertStopped()
        {
            AssertThat(Main.ProcessMode).IsEqual(Node.ProcessModeEnum.Disabled);
            AssertThat(Main.IsProcessing()).IsFalse();
            AssertThat(Main.IsProcessingInput()).IsFalse();
            if (Main.Display is not null) AssertThat(Main.Display.CanProcess()).IsFalse();
            int updates = Updates.Count, failures = Failures.Count;
            var state = Main.Exploration;
            // Frame waits are only an opportunity for forbidden callbacks; state evidence is
            // collected exclusively by the production completion event above.
            for (int i = 0; i < 3; i++) await runner.AwaitIdleFrame();
            AssertThat(Updates.Count).IsEqual(updates);
            AssertThat(Failures.Count).IsEqual(failures);
            AssertThat(ReferenceEquals(Main.Exploration, state)).IsTrue();
        }
        public void Dispose()
        {
            try
            {
                foreach (Key key in keys.ToArray()) Release(key);
                Main.Failed -= OnFailed;
                Main.UpdateCompleted -= OnUpdated;
                runner.Dispose();
                AssertThat(GodotObject.IsInstanceValid(Main)).IsFalse();
            }
            finally
            {
                if (GodotObject.IsInstanceValid(Main)) Main.Free();
                RestoreGlobals();
            }
        }
        private void RestoreGlobals()
        {
            window.MinSize = minSize; window.Size = size;
            Engine.TimeScale = timeScale; Engine.PhysicsTicksPerSecond = physicsTicks;
            if (GodotObject.IsInstanceValid(focus)) focus!.GrabFocus();
        }
    }
}
