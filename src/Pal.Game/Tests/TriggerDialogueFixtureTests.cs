using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Pal.Core;
using Pal.Game.Dialogue;
using Pal.Game.Resources;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

[TestSuite, RequireGodotRuntime, GodotExceptionMonitor]
public class TriggerDialogueFixtureTests
{
    private const string Root = "res://Tests/Fixtures/TriggerDialogue";

    [AfterTest]
    public void ReleaseAdvanceKeys()
    {
        SendKey(Godot.Key.Space, false);
        SendKey(Godot.Key.Escape, false);
    }

    [TestCase("zh-Hans", 2, Timeout = 15000)]
    [TestCase("en", 1, Timeout = 15000)]
    public async Task Complete_modern_collection_drives_same_display_wait_completion_and_repeat(string language, int lines)
    {
        using var runner = ISceneRunner.Load("res://Tests/DialogueDisplayFixture.tscn", true);
        var display = (PalDialogueDisplay)runner.Scene()!;
        var trigger = new PalTriggerDialogue(); display.AddChild(trigger);
        trigger.Initialize(display, Root + "/index.json", Root, Palette(), Scene(), language);
        // This callback runs at the end of the display's actual _Process, after the receipt
        // consumer subscribed by Initialize. Coroutine resumption/SimulateFrames is not our evidence.
        DialogueDisplaySnapshot? updated = null;
        TriggerScriptPhase? scriptPhase = null;
        ulong updateFrame = 0;
        display.UpdateCompleted += () => { updated = display.Current; scriptPhase = trigger.Session!.Phase; updateFrame = Engine.GetProcessFrames(); };
        trigger.Start("scene-004", "npc", 60);
        var request = trigger.Session!.PendingDisplay!;
        AssertThat(ReferenceEquals(display.Session, request.Display)).IsTrue();
        AssertThat(trigger.Session.CurrentPosition!.SourceAddress).IsEqual(0x018B);
        AssertThat(trigger.Session.ReturnEntry).IsEqual(0x0188);
        AssertThat(request.MessageReferences[0].StartMessageId).IsEqual(61);
        AssertThat(request.MessageReferences[0].Span).IsEqual(1);
        AssertThat(request.Context.PortraitId).IsEqual(59);
        AssertThat(request.Context.Color).IsEqual(0x4F);
        AssertThat(request.Context.DelayMilliseconds).IsEqual(24);
        var window = display.GetWindow();
        int focusSignals = 0;
        void OnFocus() => focusSignals++;
        window.FocusExited += OnFocus;
        try { AssertThat(window.EmitSignal(Window.SignalName.FocusExited)).IsEqual(Error.Ok); }
        finally { window.FocusExited -= OnFocus; }
        AssertThat(focusSignals).IsEqual(1);
        await Until(runner, () => updated?.Phase == DialogueDisplayPhase.WaitingForAdvance);
        AssertThat(updated!.VisibleLines.Count).IsEqual(lines);
        AssertThat(updated.VisibleLines[0].Runs[0].Text).IsEqual(language == "en" ? "B" : "AB");
        AssertThat(trigger.Session.BlocksSceneUpdates).IsTrue();
        ulong waitingFrame = updateFrame;
        await Key(runner, Godot.Key.Space, true);
        AssertThat(updateFrame > waitingFrame).IsTrue();
        AssertThat(scriptPhase).IsEqual(TriggerScriptPhase.Completed);
        AssertThat(updated!.ClearDisplay).IsTrue();
        AssertThat(trigger.Session.Context.PortraitId).IsEqual(0);
        AssertThat(trigger.Session.Context.Color).IsEqual(0x4F);
        await Key(runner, Godot.Key.Space, false);
        // Idle/completed key delivery must not be queued into the next request.
        SendKey(Godot.Key.Space, true); SendKey(Godot.Key.Space, false);
        updated = null;
        trigger.Start("scene-004", "npc", 65535);
        AssertThat(trigger.Session.Caller).IsEqual(60);
        AssertThat(trigger.Session.PendingDisplay!.Id == request.Id).IsFalse();
        AssertThat(trigger.Session.PendingDisplay!.Display.Current.Phase).IsEqual(DialogueDisplayPhase.Typing);
        await Until(runner, () => updated?.Phase == DialogueDisplayPhase.WaitingForAdvance);
        await Key(runner, Godot.Key.Escape, true);
        AssertThat(scriptPhase).IsEqual(TriggerScriptPhase.Completed);
        AssertThat(trigger.Session.ReturnEntry).IsEqual(0x0188);
    }

    [TestCase(Timeout = 15000)]
    public void Native_display_preflight_reports_font_resource_and_source_before_any_display()
    {
        using var files = new SyntheticFiles();
        files.Edit("Font.tres", text => text.Replace("Codepoint = 66", "Codepoint = 67"));
        using var runner = ISceneRunner.Load("res://Tests/DialogueDisplayFixture.tscn", true);
        var display = (PalDialogueDisplay)runner.Scene()!;
        var trigger = new PalTriggerDialogue(); display.AddChild(trigger);
        trigger.Initialize(display, files.Root + "/index.json", files.Root, Palette(), Scene());
        string error = Failure(() => trigger.Start("scene-004", "npc", 60));
        AssertThat(error.Contains("Font.tres")).IsTrue();
        AssertThat(error.Contains("scene-004.json") && error.Contains("L0189") && error.Contains("0189")).IsTrue();
        AssertThat(display.Session is null).IsTrue();
        AssertThat(trigger.Session!.Phase).IsEqual(TriggerScriptPhase.Ready);
    }

    [TestCase(Timeout = 15000)]
    public async Task Leading_CLEAR_restores_real_background_before_typing_without_input()
    {
        using var files = new SyntheticFiles();
        const string clear = """
            [sub_resource type="Resource" id="Clear"]
            script = ExtResource("3")
            Kind = 1

            [sub_resource type="Resource" id="Range"]
            """;
        files.Edit("Language.zh-Hans.tres", text => text
            .Replace("[sub_resource type=\"Resource\" id=\"Range\"]", clear)
            .Replace("Entries = Array[Resource]([", "Entries = Array[Resource]([SubResource(\"Clear\"), "));
        using var runner = ISceneRunner.Load("res://Tests/DialogueDisplayFixture.tscn", true);
        var display = (PalDialogueDisplay)runner.Scene()!;
        var surface = display.GetNode<PalDialoguePaletteSurface>("IndexedBackground");
        var palette = Palette(); palette[0xF9] = Colors.Red; palette[0xFA] = Colors.Green;
        surface.SetPalette(palette);
        surface.SetImage(ResourceLoader.Load<PalDialogueIndexedImage>(Root + "/Portrait-059.tres"));
        var savedBackground = ResourceLoader.Load<PalDialogueIndexedImage>("res://Tests/Fixtures/DialogueSyntheticBackground.tres");
        int restores = 0;
        display.BackgroundRestoreRequested += () => { restores++; surface.SetImage(savedBackground); };
        display.ActivePaletteChanged += surface.SetPalette;
        var trigger = new PalTriggerDialogue(); display.AddChild(trigger);
        trigger.Initialize(display, files.Root + "/index.json", files.Root, palette, Scene());
        DialogueDisplaySnapshot? updated = null;
        display.UpdateCompleted += () => updated = display.Current;
        trigger.Start("scene-004", "npc", 60);
        AssertThat(restores).IsEqual(1);
        AssertThat(surface.GetRenderedPixel(0, 0)).IsEqual(Colors.Red);
        AssertThat(display.Current!.Phase).IsEqual(DialogueDisplayPhase.Typing);
        AssertThat(display.Current.VisibleText).IsEqual("");
        await Until(runner, () => updated?.Phase == DialogueDisplayPhase.WaitingForAdvance && updated.PaletteRotation == 1);
        AssertThat(surface.GetRenderedPixel(0, 0)).IsEqual(Colors.Green);
        AssertThat(trigger.Session!.Phase).IsEqual(TriggerScriptPhase.WaitingForDisplay);
        await Key(runner, Godot.Key.Space, true);
        AssertThat(updated!.Phase).IsEqual(DialogueDisplayPhase.Completed);
        AssertThat(trigger.Session.Phase).IsEqual(TriggerScriptPhase.Completed);
        AssertThat(restores).IsEqual(2);
        AssertThat(surface.GetRenderedPixel(0, 0)).IsEqual(Colors.Red);
    }

    [TestCase(Timeout = 15000)]
    public async Task One_input_batch_cannot_advance_new_request_and_context_survives_setDialog()
    {
        using var files = new SyntheticFiles();
        files.Json("index.json", root => { root["source"]!["instructionCount"] = 398; root["source"]!["byteLength"] = 3184; });
        files.Json("scene-004.json", root =>
        {
            var commands = root["commands"]!.AsArray();
            commands[3] = JsonNode.Parse("""{"label":"L018B","op":"setDialog","box":"upper","portraitId":0,"color":0,"rngWord":0,"next":"L018C","source":{"index":395,"opcode":60,"operands":[0,0,0]}}""");
            commands.Add(JsonNode.Parse("""{"label":"L018C","op":"showDialog","messageId":63,"descriptionLineWord":0,"next":"L018D","source":{"index":396,"opcode":65535,"operands":[63,0,0]}}"""));
            commands.Add(JsonNode.Parse("""{"label":"L018D","op":"end","mode":"keep","next":null,"source":{"index":397,"opcode":0,"operands":[0,0,0]}}"""));
        });
        const string nextRange = """
            [sub_resource type="Resource" id="NextEntry"]
            script = ExtResource("3")
            Text = "B"

            [sub_resource type="Resource" id="NextRange"]
            script = ExtResource("2")
            StartMessageId = 63
            EndMessageId = 63
            Entries = Array[Resource]([SubResource("NextEntry")])

            [resource]
            """;
        files.Edit("Language.zh-Hans.tres", text => text.Replace("Text = \"A\"", "Text = \"A'$12\"")
            .Replace("[resource]", nextRange).Replace("Messages = Array[Resource]([SubResource(\"Range\")])", "Messages = Array[Resource]([SubResource(\"Range\"), SubResource(\"NextRange\")])"));
        using var runner = ISceneRunner.Load("res://Tests/DialogueDisplayFixture.tscn", true);
        var display = (PalDialogueDisplay)runner.Scene()!;
        var trigger = new PalTriggerDialogue(); display.AddChild(trigger);
        trigger.Initialize(display, files.Root + "/index.json", files.Root, Palette(), Scene());
        DialogueDisplaySnapshot? updated = null;
        Guid? updatedRequest = null;
        display.UpdateCompleted += () => { updated = display.Current; updatedRequest = trigger.Session!.PendingDisplay?.Id; };
        trigger.Start("scene-004", "npc", 60);
        Guid first = trigger.Session!.PendingDisplay!.Id;
        await Until(runner, () => updated?.Phase == DialogueDisplayPhase.WaitingForAdvance);
        SendKey(Godot.Key.Space, true); SendKey(Godot.Key.Escape, true);
        // Capture at the first actual completion callback, not after an arbitrary frame count.
        DialogueDisplaySnapshot? newRequestSnapshot = null;
        display.UpdateCompleted += () =>
        {
            if (newRequestSnapshot is null && trigger.Session!.PendingDisplay is { } pending && pending.Id != first)
                newRequestSnapshot = display.Current;
        };
        await Until(runner, () => newRequestSnapshot is not null);
        AssertThat(newRequestSnapshot!.Phase).IsEqual(DialogueDisplayPhase.Typing);
        AssertThat(newRequestSnapshot.VisibleText).IsEqual("");
        AssertThat(trigger.Session.PendingDisplay!.Context.Color).IsEqual(0x1A);
        AssertThat(trigger.Session.PendingDisplay.Context.DelayMilliseconds).IsEqual(136);
        AssertThat(trigger.Session.PendingDisplay.Context.PortraitId).IsEqual(0);
        AssertThat(trigger.Session.CurrentPosition!.SourceAddress).IsEqual(0x018D);
        SendKey(Godot.Key.Space, false); SendKey(Godot.Key.Escape, false);
        await Until(runner, () => updated?.Phase == DialogueDisplayPhase.WaitingForAdvance && updatedRequest != first);
        await Key(runner, Godot.Key.Space, true);
        AssertThat(trigger.Session.Phase).IsEqual(TriggerScriptPhase.Completed);
    }

    [TestCase("missing-file", "shared.json", Timeout = 15000)]
    [TestCase("cross-file", "scene-004#Missing", Timeout = 15000)]
    [TestCase("raw", "shared.json#L0001 @0001", Timeout = 15000)]
    [TestCase("language", "Language.en.tres", Timeout = 15000)]
    [TestCase("language-identity", "identity differs", Timeout = 15000)]
    [TestCase("portrait", "Portrait-059.tres", Timeout = 15000)]
    [TestCase("portrait-shape", "Portrait-059.tres", Timeout = 15000)]
    [TestCase("owner", "does not match", Timeout = 15000)]
    [TestCase("utf8", "invalid UTF-8", Timeout = 15000)]
    [TestCase("duplicate", "Duplicate", Timeout = 15000)]
    public void Missing_invalid_and_unsupported_resources_reject_without_display(string fault, string diagnostic)
    {
        using var files = new SyntheticFiles();
        using var scene = Scene();
        string language = "zh-Hans";
        switch (fault)
        {
            case "missing-file": File.Delete(files.PathFor("shared.json")); break;
            case "cross-file": files.Edit("shared.json", text => text.Replace("scene-004#L0188", "scene-004#Missing")); break;
            case "raw":
                files.Json("scene-004.json", root => { root["entries"]![0]!["target"] = "shared#L0001"; root["entries"]![0]!["sourceAddress"] = 1; });
                scene.Objects[0].WTriggerScript = 1; break;
            case "language": File.Delete(files.PathFor("Language.en.tres")); language = "en"; break;
            case "language-identity": files.Edit("Language.zh-Hans.tres", text => text.Replace("Language = \"zh-Hans\"", "Language = \"en\"")); break;
            case "portrait": File.Delete(files.PathFor("Portrait-059.tres")); break;
            case "portrait-shape": files.Edit("Portrait-059.tres", text => text.Replace("Width = 1", "Width = 2")); break;
            case "owner": scene.Objects[0].Id = 61; break;
            case "utf8": File.WriteAllBytes(files.PathFor("shared.json"), [0xFF]); break;
            case "duplicate": files.Edit("shared.json", text => text.Replace("\"fileId\":\"shared\"", "\"fileId\":\"shared\",\"fileId\":\"shared\"")); break;
        }
        using var runner = ISceneRunner.Load("res://Tests/DialogueDisplayFixture.tscn", true);
        var display = (PalDialogueDisplay)runner.Scene()!;
        var trigger = new PalTriggerDialogue(); display.AddChild(trigger);
        string error = Failure(() =>
        {
            trigger.Initialize(display, files.Root + "/index.json", files.Root, Palette(), scene, language);
            trigger.Start("scene-004", "npc", 60);
        });
        AssertThat(error.Contains(diagnostic, StringComparison.OrdinalIgnoreCase)).IsTrue();
        AssertThat(display.Session is null).IsTrue();
        AssertThat(trigger.Session is null || trigger.Session.Phase == TriggerScriptPhase.Ready).IsTrue();
    }

    [TestCase("Prompt-01.tres", "Dialogue indexed image has invalid dimensions or planes.", Timeout = 15000)]
    [TestCase("Language.zh-Hans.tres", "Dialogue language resource has an unsupported version or identity.", Timeout = 15000)]
    public void Invalid_initialization_resource_reports_exact_path_reason_and_inner_exception(string fileName, string reason)
    {
        using var files = new SyntheticFiles();
        files.Edit(fileName, text => fileName == "Prompt-01.tres"
            ? text.Replace("Width = 1", "Width = 2")
            : text.Replace("Language = \"zh-Hans\"", "FormatVersion = 2\nLanguage = \"zh-Hans\""));
        using var runner = ISceneRunner.Load("res://Tests/DialogueDisplayFixture.tscn", true);
        var display = (PalDialogueDisplay)runner.Scene()!;
        var trigger = new PalTriggerDialogue(); display.AddChild(trigger);
        using var scene = Scene();
        InvalidDataException? failure = null;
        try { trigger.Initialize(display, files.Root + "/index.json", files.Root, Palette(), scene); }
        catch (InvalidDataException error) { failure = error; }

        AssertThat(failure is not null).IsTrue();
        AssertThat(failure!.Message).IsEqual($"{files.Root}/{fileName}: {reason}");
        AssertThat(failure.InnerException?.GetType() == typeof(InvalidDataException)).IsTrue();
        AssertThat(failure.InnerException?.Message).IsEqual(reason);
        AssertThat(display.Session is null).IsTrue();
        AssertThat(trigger.Session is null).IsTrue();
    }

    [TestCase("clear", Timeout = 15000)]
    [TestCase("name", Timeout = 15000)]
    public void No_wait_restore_content_is_applied_and_receipted_synchronously(string content)
    {
        using var files = new SyntheticFiles();
        files.Edit("Language.zh-Hans.tres", text => text
            .Replace("Text = \"AB)\"", content == "clear" ? "Kind = 1" : "Text = \"AB:\"")
            .Replace("Entries = Array[Resource]([SubResource(\"Entry0\"), SubResource(\"Entry1\")])", "Entries = Array[Resource]([SubResource(\"Entry0\")])"));
        using var runner = ISceneRunner.Load("res://Tests/DialogueDisplayFixture.tscn", true);
        var display = (PalDialogueDisplay)runner.Scene()!;
        var surface = display.GetNode<PalDialoguePaletteSurface>("IndexedBackground");
        var palette = Palette(); palette[0xF9] = Colors.Red;
        surface.SetPalette(palette);
        surface.SetImage(ResourceLoader.Load<PalDialogueIndexedImage>(Root + "/Portrait-059.tres"));
        var background = ResourceLoader.Load<PalDialogueIndexedImage>("res://Tests/Fixtures/DialogueSyntheticBackground.tres");
        int restores = 0;
        display.BackgroundRestoreRequested += () => { restores++; surface.SetImage(background); };
        var trigger = new PalTriggerDialogue(); display.AddChild(trigger);
        trigger.Initialize(display, files.Root + "/index.json", files.Root, palette, Scene());
        trigger.Start("scene-004", "npc", 60);
        AssertThat(restores).IsEqual(1);
        AssertThat(surface.GetRenderedPixel(0, 0)).IsEqual(Colors.Red);
        AssertThat(display.Current!.Phase).IsEqual(DialogueDisplayPhase.Completed);
        AssertThat(trigger.Session!.Phase).IsEqual(TriggerScriptPhase.Completed);
        AssertThat(trigger.Session.PendingDisplay is null).IsTrue();
        AssertThat(trigger.Session.BlocksSceneUpdates).IsFalse();
    }

    [TestCase(Timeout = 15000)]
    public async Task Multiple_no_wait_restore_requests_drain_in_Start_before_first_real_body_wait()
    {
        using var files = new SyntheticFiles();
        files.Json("index.json", root => { root["source"]!["instructionCount"] = 399; root["source"]!["byteLength"] = 3192; });
        files.Json("scene-004.json", root => root["commands"] = JsonNode.Parse("""
            [
              {"label":"L0188","op":"setDialog","box":"upper","portraitId":59,"color":0,"rngWord":0,"next":"L0189","source":{"index":392,"opcode":60,"operands":[59,0,0]}},
              {"label":"L0189","op":"showDialog","messageId":61,"descriptionLineWord":0,"next":"L018A","source":{"index":393,"opcode":65535,"operands":[61,0,0]}},
              {"label":"L018A","op":"setDialog","box":"upper","portraitId":0,"color":0,"rngWord":0,"next":"L018B","source":{"index":394,"opcode":60,"operands":[0,0,0]}},
              {"label":"L018B","op":"showDialog","messageId":62,"descriptionLineWord":0,"next":"L018C","source":{"index":395,"opcode":65535,"operands":[62,0,0]}},
              {"label":"L018C","op":"setDialog","box":"upper","portraitId":0,"color":0,"rngWord":0,"next":"L018D","source":{"index":396,"opcode":60,"operands":[0,0,0]}},
              {"label":"L018D","op":"showDialog","messageId":63,"descriptionLineWord":0,"next":"L018E","source":{"index":397,"opcode":65535,"operands":[63,0,0]}},
              {"label":"L018E","op":"end","mode":"keep","next":null,"source":{"index":398,"opcode":0,"operands":[0,0,0]}}
            ]
            """));
        const string language = """
            [gd_resource type="Resource" script_class="PalDialogueLanguage" format=3]
            [ext_resource type="Script" path="res://Scripts/Resources/PalDialogueLanguage.cs" id="1"]
            [ext_resource type="Script" path="res://Scripts/Resources/PalDialogueMessageRange.cs" id="2"]
            [ext_resource type="Script" path="res://Scripts/Resources/PalDialogueMessageEntry.cs" id="3"]
            [sub_resource type="Resource" id="Clear"]
            script = ExtResource("3")
            Kind = 1
            [sub_resource type="Resource" id="Name"]
            script = ExtResource("3")
            Text = "AB:"
            [sub_resource type="Resource" id="Body"]
            script = ExtResource("3")
            Text = "AB"
            [sub_resource type="Resource" id="Range61"]
            script = ExtResource("2")
            StartMessageId = 61
            EndMessageId = 61
            Entries = Array[Resource]([SubResource("Clear")])
            [sub_resource type="Resource" id="Range62"]
            script = ExtResource("2")
            StartMessageId = 62
            EndMessageId = 62
            Entries = Array[Resource]([SubResource("Name")])
            [sub_resource type="Resource" id="Range63"]
            script = ExtResource("2")
            StartMessageId = 63
            EndMessageId = 63
            Entries = Array[Resource]([SubResource("Body")])
            [resource]
            script = ExtResource("1")
            Language = "zh-Hans"
            SourceIdentity = "synthetic-multiple-restore-blocks"
            Messages = Array[Resource]([SubResource("Range61"), SubResource("Range62"), SubResource("Range63")])
            """;
        File.WriteAllText(files.PathFor("Language.zh-Hans.tres"), language);
        using var runner = ISceneRunner.Load("res://Tests/DialogueDisplayFixture.tscn", true);
        var display = (PalDialogueDisplay)runner.Scene()!;
        int restores = 0; display.BackgroundRestoreRequested += () => restores++;
        var trigger = new PalTriggerDialogue(); display.AddChild(trigger);
        trigger.Initialize(display, files.Root + "/index.json", files.Root, Palette(), Scene());
        DialogueDisplaySnapshot? updated = null;
        display.UpdateCompleted += () => updated = display.Current;
        trigger.Start("scene-004", "npc", 60);
        AssertThat(restores).IsEqual(2);
        AssertThat(trigger.Session!.Phase).IsEqual(TriggerScriptPhase.WaitingForDisplay);
        AssertThat(trigger.Session.PendingDisplay!.Display.Current.Phase).IsEqual(DialogueDisplayPhase.Typing);
        AssertThat(display.Current!.VisibleText).IsEqual("");
        AssertThat(trigger.Session.CurrentPosition!.SourceAddress).IsEqual(0x018E);
        await Until(runner, () => updated?.Phase == DialogueDisplayPhase.WaitingForAdvance);
        await Key(runner, Godot.Key.Space, true);
        AssertThat(trigger.Session.Phase).IsEqual(TriggerScriptPhase.Completed);
        AssertThat(restores).IsEqual(3);
    }

    private static string Failure(Action action)
    {
        try { action(); }
        catch (InvalidDataException error) { return error.Message; }
        throw new InvalidOperationException("Expected a resource/entry rejection, but operation succeeded.");
    }

    private sealed class SyntheticFiles : IDisposable
    {
        public string Root { get; } = "res://Assets/RuntimeTests/" + Guid.NewGuid().ToString("N");
        public string PathFor(string name) => ProjectSettings.GlobalizePath(Root + "/" + name);
        public SyntheticFiles()
        {
            Directory.CreateDirectory(ProjectSettings.GlobalizePath(Root));
            foreach (string path in Directory.EnumerateFiles(ProjectSettings.GlobalizePath(TriggerDialogueFixtureTests.Root)))
                File.Copy(path, PathFor(System.IO.Path.GetFileName(path)));
        }
        public void Edit(string name, Func<string, string> edit) => File.WriteAllText(PathFor(name), edit(File.ReadAllText(PathFor(name))));
        public void Json(string name, Action<JsonNode> edit)
        {
            var root = JsonNode.Parse(File.ReadAllText(PathFor(name)))!;
            edit(root); File.WriteAllText(PathFor(name), root.ToJsonString());
        }
        public void Dispose() => Directory.Delete(ProjectSettings.GlobalizePath(Root), true);
    }

    private static PalSceneData Scene() => new() { SceneId = 4, Objects = new() { new PalSceneObject { Id = 60, WTriggerScript = 0x0188 } } };
    private static Color[] Palette() => Enumerable.Repeat(Colors.White, 256).ToArray();
    private static async Task Until(ISceneRunner runner, Func<bool> condition)
    {
        for (int frame = 0; frame < 120 && !condition(); frame++) await runner.SimulateFrames(1);
        AssertThat(condition()).IsTrue();
    }
    private static void SendKey(Godot.Key key, bool pressed)
    {
        using var input = new InputEventKey { Keycode = key, Pressed = pressed, Echo = false };
        Input.ParseInputEvent(input);
    }
    private static async Task Key(ISceneRunner runner, Godot.Key key, bool pressed)
    {
        SendKey(key, pressed);
        await runner.SimulateFrames(2);
    }
}
