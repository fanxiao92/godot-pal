using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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

    [TestCase("missing", Timeout = 20000)]
    [TestCase("version", Timeout = 20000)]
    [TestCase("identity", Timeout = 20000)]
    public async Task FirstBattleInitializationFailureUsesMainFailureChannelWithoutPartialScene(string fault)
    {
        using var fixture = new NativeBattleProfileFixture();
        string diagnostic;
        if (fault == "missing")
        {
            File.Delete(ProjectSettings.GlobalizePath(fixture.Uri));
            diagnostic = "native first-battle profile missing; prepare it explicitly";
        }
        else if (fault == "version")
        {
            fixture.Native.FormatVersion = 2;
            fixture.Save();
            diagnostic = "first-battle FormatVersion must be 1";
        }
        else
        {
            fixture.Static.Scene.Objects.Single(item => item.Id == 28).WTriggerScript = 0x09fe;
            fixture.Static.SavePacked();
            fixture.Native.StaticSceneSha256 = Godot.FileAccess.GetSha256(fixture.Static.ResourceRoot + "/Scenes/Scene.tscn");
            fixture.Save();
            diagnostic = "owner28 reference identity mismatch";
        }
        string staticSceneUri = fixture.Static.ResourceRoot + "/Scenes/Scene.tscn";
        string staticSceneHash = Godot.FileAccess.GetSha256(staticSceneUri);
        var selection = GameSceneConfiguration.FromArguments("--m4-first-battle-profile", fixture.Uri);
        Main old;
        using (var scene = new EmbeddedScene(Configured(selection)))
        {
            old = scene.Main;
            AssertThat(old.Failure!.Contains(diagnostic, StringComparison.Ordinal)).IsTrue();
            AssertThat(old.Failure.Contains("selected-profile=" + fixture.Uri, StringComparison.Ordinal)).IsTrue();
            if (fault == "identity")
            {
                AssertThat(old.Failure.Contains("scene=" + fixture.Static.ResourceRoot + "/Scenes/Scene.tscn", StringComparison.Ordinal)).IsTrue();
                AssertThat(old.Failure.Contains("static-profile=" + fixture.Static.Uri, StringComparison.Ordinal)).IsTrue();
            }
            AssertThat(old.Failure.Contains("@09FE", StringComparison.OrdinalIgnoreCase)).IsFalse();
            AssertThat(old.Failure.Contains("battle-complete", StringComparison.OrdinalIgnoreCase)).IsFalse();
            AssertThat(scene.Failures.Count).IsEqual(1);
            AssertThat(scene.Failures[0]).IsEqual(old.Failure);
            AssertThat(old.Battle).IsNull();
            AssertThat(old.Exploration).IsNull();
            AssertThat(old.SelectedLanguage).IsNull();
            AssertThat(old.Dialogue).IsNull();
            AssertThat(old.Display).IsNull();
            AssertThat(old.GetChildCount()).IsEqual(0);
            scene.Press(Key.Down);
            scene.Press(Key.Enter);
            await scene.AssertStopped();
        }
        AssertThat(GodotObject.IsInstanceValid(old)).IsFalse();
        AssertThat(Input.IsActionPressed("exploration_south")).IsFalse();
        AssertThat(Input.IsKeyPressed(Key.Enter)).IsFalse();
        AssertThat(Godot.FileAccess.GetSha256(staticSceneUri)).IsEqual(staticSceneHash);
        // Static PackedScene content is immutable; hot reload is not this contract.
        if (fault == "identity") return;
        // Repair only the native profile; reuse all URIs and unchanged static resources.
        fixture.Native.FormatVersion = 1;
        fixture.Save();
        using (var next = new EmbeddedScene(Configured(selection)))
        {
            next.AssertHealthy();
            AssertFirstBattlePreTriggerState(next.Main);
            var first = await next.FirstSouthStep();
            AssertThat((first.Leader.X, first.Leader.Y, first.Leader.StepPhase, first.Plan.LeaderFrameIndex))
                .IsEqual(((short)1248, (short)256, 1, 1));
        }
        AssertThat(Input.IsActionPressed("exploration_south")).IsFalse();
    }

    [TestCase(Timeout = 20000)]
    public async Task FirstBattleContextStartsFromCoreStateAndRejectsOwner28BeforeSideEffects()
    {
        using var fixture = new NativeBattleProfileFixture();
        string nativeHash = Godot.FileAccess.GetSha256(fixture.Uri);
        string staticSceneUri = fixture.Static.ResourceRoot + "/Scenes/Scene.tscn";
        string sceneHash = Godot.FileAccess.GetSha256(staticSceneUri);
        string staticProfileHash = Godot.FileAccess.GetSha256(fixture.Static.Uri);
        var selection = GameSceneConfiguration.FromArguments("--m4-first-battle-profile", fixture.Uri,
            "--m4-first-battle-language", "en");
        Main old;
        GodotObject[] children;
        using (var scene = new EmbeddedScene(Configured(selection)))
        {
            old = scene.Main;
            scene.AssertHealthy();
            AssertThat(old.SelectedLanguage).IsEqual("en");
            AssertFirstBattlePreTriggerState(old);
            children = old.GetChildren().Cast<GodotObject>().ToArray();
            await scene.WaitFor(() => scene.Updates.Count > 0, "first actual M4 update completion");
            AssertIdleBattleUpdates(scene);
            int completedUpdates = scene.Updates.Count;
            int stopPhase = completedUpdates % 2 * 2;
            AssertFirstBattlePreTriggerState(old, stopPhase);
            var before = scene.Updates[^1];
            // Value snapshot saved at the production completion boundary, not after await.
            // Independent literals and the reference idle rule check its baseline.
            string beforeState = scene.BattleSnapshots[^1];
            AssertThat(JsonSerializer.Serialize(old.Battle!.State)).IsEqual(beforeState);
            scene.Press(Key.Enter);
            await scene.WaitFor(() => old.Failure is not null, "owner28 unsupported closure rejection");
            AssertThat(old.Failure).IsEqual("Exploration update failed: " + FirstMandatoryBattleSession.UnsupportedTriggerDiagnostic);
            AssertThat(ReferenceEquals(old.Exploration, before)).IsTrue();
            AssertThat(JsonSerializer.Serialize(old.Battle.State)).IsEqual(beforeState);
            AssertFirstBattlePreTriggerState(old, stopPhase);
            // Rejected @09C3 is not a committed update, nor an @09FE/battle completion.
            AssertThat(scene.Updates.Count).IsEqual(completedUpdates);
            AssertThat(scene.Failures.Count).IsEqual(1);
            AssertThat(scene.Failures[0]).IsEqual(old.Failure);
            scene.Press(Key.Down);
            scene.Press(Key.Enter);
            await scene.AssertStopped();
            AssertThat(JsonSerializer.Serialize(old.Battle.State)).IsEqual(beforeState);
            AssertFirstBattlePreTriggerState(old, stopPhase);
        }
        AssertThat(GodotObject.IsInstanceValid(old)).IsFalse();
        foreach (GodotObject child in children) AssertThat(GodotObject.IsInstanceValid(child)).IsFalse();
        AssertThat(Input.IsActionPressed("exploration_south")).IsFalse();
        AssertThat(Input.IsKeyPressed(Key.Enter)).IsFalse();
        using (var next = new EmbeddedScene(Configured(selection)))
        {
            next.AssertHealthy();
            AssertFirstBattlePreTriggerState(next.Main);
            await next.WaitFor(() => next.Updates.Count > 0, "fresh same-URI M4 update completion");
            AssertIdleBattleUpdates(next);
            int stopPhase = next.Updates.Count % 2 * 2;
            AssertFirstBattlePreTriggerState(next.Main, stopPhase);
            string beforeState = next.BattleSnapshots[^1];
            next.Press(Key.Enter);
            await next.WaitFor(() => next.Main.Failure is not null, "fresh same-URI owner28 search rejection");
            AssertThat(next.Main.Failure).IsEqual("Exploration update failed: " + FirstMandatoryBattleSession.UnsupportedTriggerDiagnostic);
            AssertThat(next.Failures.Count).IsEqual(1);
            AssertThat(JsonSerializer.Serialize(next.Main.Battle!.State)).IsEqual(beforeState);
            AssertFirstBattlePreTriggerState(next.Main, stopPhase);
            await next.AssertStopped();
            AssertThat(JsonSerializer.Serialize(next.Main.Battle.State)).IsEqual(beforeState);
        }
        AssertThat(Input.IsKeyPressed(Key.Enter)).IsFalse();
        AssertThat(Godot.FileAccess.GetSha256(fixture.Uri)).IsEqual(nativeHash);
        AssertThat(Godot.FileAccess.GetSha256(staticSceneUri)).IsEqual(sceneHash);
        AssertThat(Godot.FileAccess.GetSha256(fixture.Static.Uri)).IsEqual(staticProfileHash);
        AssertThat(fixture.Static.Scene.Objects.Single(item => item.Id == 28).SState).IsEqual(1);
        AssertThat(fixture.Static.Scene.Objects.Single(item => item.Id == 28).WTriggerScript).IsEqual(0x09c3);
    }

    [TestCase(Timeout = 20000)]
    public async Task PreparedFirstBattleContextLoadsThroughMainAndKeepsSameUriSessionIndependent()
    {
        using var fixture = new NativeBattleProfileFixture();
        // Offline preparation finishes synchronously before either Main enters the tree.
        string preparedUri = fixture.PrepareProductionProfile();
        AssertThat(preparedUri.EndsWith("/Battle/Profile.tres", StringComparison.Ordinal)).IsTrue();
        string preparedHash = Godot.FileAccess.GetSha256(preparedUri);
        var selection = GameSceneConfiguration.FromArguments("--m4-first-battle-profile", preparedUri);
        Main old;
        GodotObject[] children;
        using (var scene = new EmbeddedScene(Configured(selection)))
        {
            old = scene.Main;
            scene.AssertHealthy();
            AssertThat(old.SelectedLanguage).IsEqual("zh-Hans");
            AssertFirstBattlePreTriggerState(old);
            children = old.GetChildren().Cast<GodotObject>().ToArray();
            var first = await scene.FirstSouthStep();
            AssertThat((first.Leader.X, first.Leader.Y, first.Leader.Direction, first.Leader.StepPhase,
                first.IsWalking, first.Plan.LeaderFrameIndex)).IsEqual(((short)1248, (short)256, 0, 1, true, 1));
            AssertThat((old.Battle!.State.World.Leader.X, old.Battle.State.World.Leader.Y,
                old.Battle.State.World.Leader.Direction)).IsEqual((1248, 256, "south"));
        }
        AssertThat(GodotObject.IsInstanceValid(old)).IsFalse();
        foreach (GodotObject child in children) AssertThat(GodotObject.IsInstanceValid(child)).IsFalse();
        AssertThat(Input.IsActionPressed("exploration_south")).IsFalse();
        // Reuse exactly the prepared URI without clearing the shared ResourceLoader cache.
        using (var next = new EmbeddedScene(Configured(selection)))
        {
            next.AssertHealthy();
            AssertThat(next.Main.SelectedLanguage).IsEqual("zh-Hans");
            AssertFirstBattlePreTriggerState(next.Main);
            await next.WaitFor(() => next.Updates.Count > 0, "prepared same-URI initial update completion");
            AssertIdleBattleUpdates(next);
            int completedUpdates = next.Updates.Count;
            int stopPhase = completedUpdates % 2 * 2;
            AssertFirstBattlePreTriggerState(next.Main, stopPhase);
            var before = next.Updates[^1];
            string beforeState = next.BattleSnapshots[^1];
            next.Press(Key.Enter);
            await next.WaitFor(() => next.Main.Failure is not null, "prepared owner28 @09C3 search rejection");
            AssertThat(next.Main.Failure).IsEqual("Exploration update failed: " + FirstMandatoryBattleSession.UnsupportedTriggerDiagnostic);
            AssertThat(next.Failures.Count).IsEqual(1);
            AssertThat(next.Failures[0]).IsEqual(next.Main.Failure!);
            AssertThat(next.Updates.Count).IsEqual(completedUpdates);
            AssertThat(ReferenceEquals(next.Main.Exploration, before)).IsTrue();
            AssertThat(JsonSerializer.Serialize(next.Main.Battle!.State)).IsEqual(beforeState);
            AssertFirstBattlePreTriggerState(next.Main, stopPhase);
            next.Press(Key.Down);
            await next.AssertStopped();
            AssertThat(JsonSerializer.Serialize(next.Main.Battle.State)).IsEqual(beforeState);
        }
        AssertThat(Input.IsActionPressed("exploration_south")).IsFalse();
        AssertThat(Input.IsKeyPressed(Key.Enter)).IsFalse();
        AssertThat(Godot.FileAccess.GetSha256(preparedUri)).IsEqual(preparedHash);
        foreach (var source in fixture.PreparationSourceHashes)
            AssertThat(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source.Key))))
                .IsEqual(source.Value);
        AssertThat(fixture.Static.Scene.Objects.Single(item => item.Id == 28).SState).IsEqual(1);
        AssertThat(fixture.Static.Scene.Objects.Single(item => item.Id == 28).WTriggerScript).IsEqual(0x09c3);
    }

    private static void AssertIdleBattleUpdates(EmbeddedScene scene)
    {
        AssertThat(scene.BattleSnapshots.Count).IsEqual(scene.Updates.Count);
        for (int i = 0; i < scene.Updates.Count; i++)
        {
            var update = scene.Updates[i];
            // SDLPal scene.c PAL_UpdatePartyGestures(false): phase &= 2; phase ^= 2.
            // Core Every_idle_step_toggles_stop_phase... freezes 0 -> 2 -> 0.
            AssertThat((update.Leader.X, update.Leader.Y, update.Leader.Direction, update.Leader.StepPhase,
                update.IsWalking, update.Plan.LeaderFrameIndex))
                .IsEqual(((short)1264, (short)248, 3, (i + 1) % 2 * 2, false, 9));
        }
    }

    private static void AssertFirstBattlePreTriggerState(Main main, int phase = 0)
    {
        var state = main.Battle!.State;
        AssertThat(string.Join(",", state.PartyRoleIds)).IsEqual("0");
        AssertThat(state.Roles.Length).IsEqual(6);
        string[] roles = ["0,1,150,150,100,100,2", "1,5,28,240,10,240,0",
            "2,1,1,1,0,0,0", "3,1,1,1,0,0,0", "4,1,1,1,0,0,0", "5,1,1,1,0,0,0"];
        string[] equipment = ["196,225,208,166,235,249", "197,0,210,170,237,248",
            "0,0,0,0,0,0", "0,0,0,0,0,0", "0,0,0,0,0,0", "0,0,0,0,0,0"];
        string[] magics = ["296,345", "312,316,321,327,332,306,297,309,303,307", "", "", "", ""];
        for (int i = 0; i < 6; i++)
        {
            var role = state.Roles[i];
            AssertThat(string.Join(",", role.RoleId, role.Level, role.Hp, role.MaxHp, role.Mp, role.MaxMp, role.CoveredByRoleId)).IsEqual(roles[i]);
            AssertThat(string.Join(",", role.Equipment)).IsEqual(equipment[i]);
            AssertThat(string.Join(",", role.Magics)).IsEqual(magics[i]);
            AssertThat(string.Join(",", role.Statuses)).IsEqual("0,0,0,0,0,0,0,0,0");
            int level = i == 1 ? 5 : 1;
            AssertThat((role.PrimaryExperience.Value, role.PrimaryExperience.Level, role.PrimaryExperience.Count)).IsEqual((0, level, 0));
            AssertThat(string.Join(",", role.HiddenExperience.Keys.OrderBy(key => key, StringComparer.Ordinal)))
                .IsEqual("attack,defense,dexterity,flee,health,magic,magicPower");
            foreach (var experience in role.HiddenExperience.Values)
                AssertThat((experience.Value, experience.Level, experience.Count)).IsEqual((0, level, 0));
        }
        var world = state.World;
        AssertThat((world.SceneId, world.MapId, world.PaletteId, world.NightPalette, world.MusicId, world.BattleMusicId))
            .IsEqual((2, 12, 0, false, 31, 37));
        AssertThat((world.Leader.X, world.Leader.Y, world.Leader.Direction, world.Leader.Layer, world.Leader.Phase))
            .IsEqual((1264, 248, "east", 0, phase));
        AssertThat((world.SearchProof.OwnerId, world.SearchProof.SampleIndex, world.SearchProof.CollisionPass)).IsEqual((28, 1, true));
        AssertThat(world.SceneEntries.Length).IsEqual(1);
        AssertThat((world.SceneEntries[0].SceneId, world.SceneEntries[0].EnterEntry, world.SceneEntries[0].TeleportEntry)).IsEqual((2, 0x0e97, 0));
        AssertThat(world.EventObjects.Length).IsEqual(1);
        var owner = world.EventObjects[0];
        AssertThat(string.Join(",", owner.OwnerId, owner.X, owner.Y, owner.Direction, owner.State, owner.VanishTime,
            owner.TriggerMode, owner.TriggerEntry, owner.AutoEntry, owner.AutoIdleFrameCount, owner.SpriteId,
            owner.SpriteFrames, owner.CurrentFrame, owner.ScriptIdleFrameCount, owner.SpritePtrOffset, owner.AutoSpriteFrames, owner.Layer))
            .IsEqual("28,1280,256,0,2,0,1,2499,0,0,210,0,0,0,0,0,0");
        AssertThat((main.Exploration!.Leader.X, main.Exploration.Leader.Y, main.Exploration.Leader.Direction,
            main.Exploration.Leader.Layer, main.Exploration.Leader.StepPhase)).IsEqual(((short)1264, (short)248, 3, (ushort)0, phase));
        AssertThat(main.GetChildren().OfType<Pal.Game.Resources.PalMapScene>().Count()).IsEqual(1);
        AssertThat(main.GetChildCount()).IsEqual(1);
        AssertThat(main.Interaction).IsNull();
        AssertThat(main.Dialogue).IsNull();
        AssertThat(main.Display).IsNull();
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
        public List<string> BattleSnapshots { get; } = [];

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
        private void OnUpdated()
        {
            Updates.Add(Main.Exploration!);
            if (Main.Battle is { } battle) BattleSnapshots.Add(JsonSerializer.Serialize(battle.State));
        }
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
