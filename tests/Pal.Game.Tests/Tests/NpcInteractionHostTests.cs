using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Pal.Core;
using Pal.Game.Dialogue;
using Pal.Game.Map;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

[TestSuite, RequireGodotRuntime]
[TestCategory("Exploration")]
public class NpcInteractionHostTests
{

    [TestCase("mixed")]
    [TestCase("duplicate-profile")]
    [TestCase("duplicate-language")]
    [TestCase("missing-value")]
    [TestCase("missing-profile")]
    [TestCase("unsupported-language")]
    [TestCase("empty-profile")]
    public void Dialogue_arguments_reject_ambiguous_or_incomplete_modes(string kind)
    {
        string[] args = kind switch
        {
            "mixed" => ["--m3-dialogue-profile", "res://Npc.tres", "--m3-static-resources", "res://Assets"],
            "duplicate-profile" => ["--m3-dialogue-profile", "res://Npc.tres", "--m3-dialogue-profile", "res://Npc.tres"],
            "duplicate-language" => ["--m3-dialogue-profile", "res://Npc.tres", "--m3-dialogue-language", "en", "--m3-dialogue-language", "en"],
            "missing-value" => ["--m3-dialogue-profile"],
            "missing-profile" => ["--m3-dialogue-language", "en"],
            "unsupported-language" => ["--m3-dialogue-profile", "res://Npc.tres", "--m3-dialogue-language", "fr"],
            _ => ["--m3-dialogue-profile", ""]
        };
        bool rejected = false;
        try { NativeSceneLoader.Arguments(args); } catch (InvalidDataException) { rejected = true; }
        AssertThat(rejected).IsTrue();
    }

    [TestCase("version", "FormatVersion", Timeout = 20000)]
    [TestCase("lockset", "ResourceSha256", Timeout = 20000)]
    [TestCase("hash", "SHA-256 mismatch", Timeout = 20000)]
    [TestCase("bundle", "identity mismatch", Timeout = 20000)]
    [TestCase("owner", "identity mismatch", Timeout = 20000)]
    [TestCase("palette", "palette0/day", Timeout = 20000)]
    [TestCase("planes", "Indices/Alpha", Timeout = 20000)]
    [TestCase("language", "selected language identity", Timeout = 20000)]
    [TestCase("missing", "profile missing", Timeout = 20000)]
    public async Task Main_rejects_missing_mismatched_or_invalid_interaction_resources(string kind, string diagnostic)
    {
        using var fixture = new NpcInteractionFixture();
        switch (kind)
        {
            case "version": fixture.Profile.FormatVersion = 2; break;
            case "lockset": fixture.Profile.ResourceSha256.Remove(fixture.Static.Uri); break;
            case "hash": fixture.Profile.ResourceSha256[fixture.Static.Uri] = new string('0', 64); break;
            case "bundle": fixture.Profile.BundleSha256 = new string('c', 64); break;
            case "owner": fixture.Static.Scene.Objects[31].WTriggerScript = 0x0189; fixture.Static.SavePacked(); break;
            case "palette": fixture.Static.Scene.Palette.SelectedLighting = "night"; fixture.Static.SavePacked(); break;
            case "planes": fixture.Static.Scene.Sprites[0].Frames[0].Indices = []; fixture.Static.SavePacked(); break;
            case "language":
                fixture.CopyDialogue();
                string path = ProjectSettings.GlobalizePath(fixture.Profile.DialogueDirectory + "/Language.zh-Hans.tres");
                File.WriteAllText(path, File.ReadAllText(path).Replace("Language = \"zh-Hans\"", "Language = \"en\""));
                fixture.Rehash(); break;
        }
        fixture.Save();
        var result = await MainProcessTests.RunMain("--m3-dialogue-profile", kind == "missing" ? fixture.Static.Root + "/Missing.tres" : fixture.Uri);
        AssertThat(result.ExitCode).IsEqual(1);
        AssertThat(result.Text.Contains(diagnostic, StringComparison.OrdinalIgnoreCase)).IsTrue();
        AssertThat(result.Text.Contains("PAL_GAME_READY")).IsFalse();
    }

    [TestCase("non-target", "only supports owner 60", Timeout = 20000)]
    [TestCase("raw", "unsupported", Timeout = 20000)]
    public async Task Actual_search_rejects_non_target_or_unsupported_closure_without_displaying(string kind, string diagnostic)
    {
        using var fixture = new NpcInteractionFixture();
        if (kind == "non-target")
        {
            var first = fixture.Static.Scene.Objects[30];
            first.X = 1216; first.Y = 1344; first.WTriggerMode = 3;
            fixture.Static.SavePacked();
        }
        else
        {
            fixture.CopyDialogue();
            string path = ProjectSettings.GlobalizePath(fixture.Profile.DialogueDirectory + "/scene-004.json");
            var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            var command = root["commands"]![0]!;
            foreach (string key in new[] { "box", "portraitId", "color", "rngWord" }) command.AsObject().Remove(key);
            command["op"] = "raw"; command["source"]!["opcode"] = 0x7777;
            File.WriteAllText(path, root.ToJsonString()); fixture.Rehash();
        }
        var selection = GameSceneConfiguration.FromArguments("--m3-dialogue-profile", fixture.Uri);
        using var host = new InteractionHost(selection);
        await host.Wait(() => host.Updates > 0);
        host.Tap(Key.Enter);
        await host.Wait(() => host.Main.Failure is not null || host.Failures.Count > 0);
        string failure = host.Main.Failure ?? host.Failures[0];
        AssertThat(failure.Contains(diagnostic, StringComparison.OrdinalIgnoreCase)).IsTrue();
        AssertThat(host.Main.Display?.Current?.Phase == DialogueDisplayPhase.WaitingForAdvance).IsFalse();
    }

    [TestCase(Timeout = 20000)]
    public async Task Main_receipts_no_wait_content_synchronously_then_returns_and_can_search_again()
    {
        using var fixture = new NpcInteractionFixture(); fixture.CopyDialogue();
        string path = ProjectSettings.GlobalizePath(fixture.Profile.DialogueDirectory + "/Language.zh-Hans.tres");
        File.WriteAllText(path, File.ReadAllText(path).Replace("Text = \"AB)\"", "Text = \"AB:\"")
            .Replace("Entries = Array[Resource]([SubResource(\"Entry0\"), SubResource(\"Entry1\")])", "Entries = Array[Resource]([SubResource(\"Entry0\")])"));
        fixture.Rehash();
        var selection = GameSceneConfiguration.FromArguments("--m3-dialogue-profile", fixture.Uri);
        using var host = new InteractionHost(selection);
        await host.Wait(() => host.Updates > 0);
        for (int call = 0; call < 2; call++)
        {
            host.Tap(Key.Enter);
            await host.Wait(() => host.Main.Dialogue!.Session!.Phase == TriggerScriptPhase.Completed);
            AssertThat(host.Main.Dialogue!.Session!.ReturnEntry).IsEqual(0x0188);
            AssertThat(host.Main.Dialogue.Session.Caller).IsEqual(60);
            await host.Wait(() => host.Main.Interaction!.Phase == NpcInteractionPhase.Exploring);
        }
        host.AssertHealthy();
    }

    [TestCase(Timeout = 20000)]
    public async Task Actual_Main_preserves_direction_pressed_to_end_wait_through_return_and_processes_release()
    {
        using var fixture = new NpcInteractionFixture();
        var selection = GameSceneConfiguration.FromArguments("--m3-dialogue-profile", fixture.Uri);
        using var host = new InteractionHost(selection);
        await host.Wait(() => host.Updates > 0);
        host.Tap(Key.Enter);
        await host.Wait(() => host.Main.Display?.Current?.Phase == DialogueDisplayPhase.WaitingForAdvance);
        host.Press(Key.Right);
        await host.Wait(() => host.Main.Interaction!.Phase == NpcInteractionPhase.Exploring
            && host.Main.Exploration!.Leader.X == 1248 && host.Main.Exploration.Leader.Y == 1344);
        host.Release(Key.Right);
        short xAfter = host.Main.Exploration!.Leader.X;
        short yAfter = host.Main.Exploration.Leader.Y;
        int updatesAfter = host.Updates;
        await host.Wait(() => host.Updates > updatesAfter + 3);
        AssertThat(host.Main.Exploration.Leader.X).IsEqual(xAfter);
        AssertThat(host.Main.Exploration.Leader.Y).IsEqual(yAfter);
        host.AssertHealthy();
    }

    [TestCase("zh-Hans", Timeout = 20000)]
    [TestCase("en", Timeout = 20000)]
    public async Task Actual_Main_search_executes_full_event_returns_and_repeats(string language)
    {
        using var fixture = new NpcInteractionFixture();
        var selection = GameSceneConfiguration.FromArguments("--m3-dialogue-profile", fixture.Uri, "--m3-dialogue-language", language);
        using var host = new InteractionHost(selection);
        await host.Wait(() => host.Updates > 0);
        AssertThat(host.Main.SelectedLanguage).IsEqual(language);
        for (int call = 0; call < 2; call++)
        {
            host.Tap(Key.Enter);
            await host.Wait(() => host.Main.Display?.Current?.Phase == DialogueDisplayPhase.WaitingForAdvance);
            host.Tap(Key.Enter);
            await host.Wait(() => host.Main.Dialogue!.Session!.Phase == TriggerScriptPhase.Completed);
            AssertThat(host.Main.Dialogue!.Session!.ReturnEntry).IsEqual(0x0188);
            AssertThat(host.Main.Dialogue.Session.Caller).IsEqual(60);
            await host.Wait(() => host.Main.Interaction!.Phase == NpcInteractionPhase.Exploring);
        }
        host.AssertHealthy();
    }

    private sealed class InteractionHost : IDisposable
    {
        internal Main Main { get; } = new();
        private readonly ISceneRunner runner;
        private readonly HashSet<Key> held = [];
        internal int Updates { get; private set; }
        internal List<string> Failures { get; } = [];

        public InteractionHost(GameSceneConfiguration selection)
        {
            Main.Configure(selection);
            Main.Failed += msg => Failures.Add(msg);
            Main.UpdateCompleted += () => Updates++;
            var container = new Node();
            container.AddChild(Main);
            runner = ISceneRunner.Load(container, autoFree: true);
        }

        public void AssertHealthy()
        {
            AssertThat(Main.Failure).IsNull();
            AssertThat(Failures.Count).IsEqual(0);
        }

        public void Press(Key key) { held.Add(key); runner.SimulateKeyPress(key); }
        public void Release(Key key) { runner.SimulateKeyRelease(key); held.Remove(key); }
        public void Tap(Key key) { Press(key); Release(key); }

        public async Task Wait(Func<bool> predicate, int timeoutMs = 15000)
        {
            ulong start = Time.GetTicksMsec();
            while (!predicate())
            {
                if (Time.GetTicksMsec() - start > (ulong)timeoutMs)
                    throw new TimeoutException("Timed out waiting for interaction condition; failure=" + Main.Failure);
                await runner.AwaitIdleFrame();
            }
        }

        public void Dispose()
        {
            foreach (var key in held.ToArray()) runner.SimulateKeyRelease(key);
            runner.Dispose();
            if (GodotObject.IsInstanceValid(Main)) Main.Free();
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    [TestCase(Timeout = 20000)]
    public async Task Main_loads_explicit_independent_dialogue_profile_without_replacing_static_mode()
    {
        using var fixture = new NpcInteractionFixture();
        var result = await MainProcessTests.RunMain("--m3-dialogue-profile", fixture.Uri);
        AssertThat(result.ExitCode).IsEqual(0);
        AssertThat(result.Text.Contains("PAL_M3_DIALOGUE_ACCEPTANCE:scene4/map10; owner60/0188; language=zh-Hans")).IsTrue();
        AssertThat(result.Text.Contains("PAL_GAME_READY:Core ready")).IsTrue();
        AssertThat(result.Text.Contains("ERROR:")).IsFalse();
    }

    [TestCase]
    public void Native_objects_keep_real_search_mode_and_trigger_entry()
    {
        using var fixture = new NativeProfileFixture();
        fixture.ConfigureStaticScene4();
        var owner = fixture.Scene.Objects[31];
        owner.WTriggerMode = 3; owner.WTriggerScript = 0x0188;
        fixture.Scene.Objects[0].WTriggerMode = 6; fixture.Scene.Objects[0].WTriggerScript = 0x1234;
        var profile = Pal.Game.Profiles.NativeExplorationProfile.Load(fixture.Uri, fixture.Save());
        AssertThat(profile.Rules.Objects[31].TriggerMode).IsEqual((ushort)3);
        AssertThat(profile.Rules.Objects[31].TriggerEntry).IsEqual((ushort)0x0188);
        AssertThat(profile.Rules.Objects[0].TriggerMode).IsEqual((ushort)6);
        AssertThat(profile.Rules.Objects[0].TriggerEntry).IsEqual((ushort)0x1234);
        AssertThat(profile.Rules.FindSearchTarget(profile.Leader)!.ObjectId).IsEqual((ushort)60);
    }

    [TestCase]
    public void Scene_palette_updates_native_indexed_tiles_objects_and_leader_without_RGBA_inference()
    {
        using var fixture = new NativeProfileFixture();
        fixture.AddObject();
        foreach (var frame in fixture.Scene.Map.Tiles.Frames) SetPlanes(frame);
        foreach (var frame in fixture.Scene.Sprites[0].Frames) SetPlanes(frame);
        var profile = Pal.Game.Profiles.NativeExplorationProfile.Load(fixture.Uri, fixture.Save());
        var palette = new Color[256]; Array.Fill(palette, Colors.Black);
        palette[0xF9] = Colors.Red; palette[0xFA] = Colors.Green;
        profile.SetPalette(fixture.Scene, palette);
        foreach (var command in profile.Plan.Commands)
        {
            using var image = profile.TextureFor(command).GetImage();
            AssertThat(image.GetPixel(0, 0)).IsEqual(Colors.Red);
            AssertThat(image.GetPixel(1, 0)).IsEqual(Colors.Green);
            AssertThat(image.GetPixel(2, 0).A).IsEqual(0f);
        }
        palette[0xF9] = Colors.Blue;
        profile.SetPalette(fixture.Scene, palette);
        foreach (var command in profile.Plan.Commands)
        {
            using var image = profile.TextureFor(command).GetImage();
            AssertThat(image.GetPixel(0, 0)).IsEqual(Colors.Blue);
        }
    }

    private static void SetPlanes(Pal.Game.Resources.PalFrame frame)
    {
        frame.Indices = new byte[frame.Width * frame.Height];
        frame.Alpha = new byte[frame.Width * frame.Height];
        Array.Fill(frame.Indices, (byte)0xF9); Array.Fill(frame.Alpha, (byte)255);
        frame.Indices[1] = 0xFA; frame.Alpha[2] = 0;
    }

    [TestCase]
    public void Dialogue_mode_is_explicit_and_language_defaults_to_Chinese()
    {
        var args = NativeSceneLoader.Arguments(["--m3-dialogue-profile", "res://Tests/Npc.tres"]);
        AssertThat(args.DialogueProfile).IsEqual("res://Tests/Npc.tres");
        AssertThat(args.Language).IsEqual("zh-Hans");
        AssertThat(args.StaticAcceptance).IsFalse();
        AssertThat(NativeSceneLoader.Arguments(["--m3-dialogue-language", "en", "--m3-dialogue-profile", "res://Tests/Npc.tres"]).Language).IsEqual("en");
    }
}
