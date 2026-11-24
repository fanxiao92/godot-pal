using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Pal.Core;
using Pal.Game.Dialogue;
using Pal.Game.Profiles;
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
        await Until(runner, () => scriptPhase == TriggerScriptPhase.Completed);
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
        await Until(runner, () => scriptPhase == TriggerScriptPhase.Completed);
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

    [TestCase("missing-file", Timeout = 15000)]
    [TestCase("missing-identity", Timeout = 15000)]
    [TestCase("later-glyph", Timeout = 15000)]
    [TestCase("clean-control", Timeout = 15000)]
    public async Task Basic_required_portrait_uses_native_file_identity_and_transactional_staging(string fault)
    {
        using var files = new SyntheticFiles();
        using var scene = Scene();
        switch (fault)
        {
            case "missing-file": File.Delete(files.PathFor("Portrait-059.tres")); break;
            case "missing-identity": files.Edit("Portrait-059.tres", text => text.Replace("SourceIdentity = \"synthetic-fixture-portrait\"", "SourceIdentity = \"\"")); break;
            case "later-glyph": files.Edit("Font.tres", text => text.Replace("Codepoint = 66", "Codepoint = 67")); break;
            case "clean-control": break;
            default: throw new ArgumentOutOfRangeException(nameof(fault));
        }
        var runner = ISceneRunner.Load("res://Tests/DialogueDisplayFixture.tscn", true);
        var display = (PalDialogueDisplay)runner.Scene()!;
        var trigger = new PalTriggerDialogue(); display.AddChild(trigger);
        var surface = display.GetNode<PalDialoguePaletteSurface>("IndexedBackground");
        var palette = Palette();
        TriggerScriptSession session = null!; // Assigned by Initialize before observers or snapshots run.
        int palettes = 0, restores = 0, updates = 0, failures = 0;
        DialogueDisplaySnapshot? firstNonTyping = null;
        TriggerScriptPhase? observedPhase = null;
        bool observeNonTyping = false;
        void OnPalette(Color[] colors) { palettes++; surface.SetPalette(colors); }
        void OnRestore() => restores++;
        void OnFailure(Exception error) => failures++;
        // Initialize subscribed the actual receipt consumer before this observer.
        void OnUpdate()
        {
            updates++;
            observedPhase = session.Phase;
            if (observeNonTyping && firstNonTyping is null && display.Current is { } sample && sample.Phase != DialogueDisplayPhase.Typing)
                firstNonTyping = sample;
        }
        object SessionState() => (session.Phase, session.BlocksSceneUpdates, session.CallId,
            session.SourceRequestId, session.StartedEntry, session.Caller, session.ReturnEntry,
            session.CurrentPosition, session.Context, session.ScriptSuccess, session.Music, session.FadedToRed,
            session.PendingDisplay, session.PendingBattle, session.PendingLostReload, session.PendingSceneWait,
            session.PendingMovement, session.PendingTransition, session.PendingBattleHook,
            session.RunningBattleHook, session.RunningBattleEndScript, session.BattleRoles);
        object SceneState() => (scene.SceneId, scene.MapId, scene.WScriptOnEnter, scene.WScriptOnTeleport,
            scene.WEventObjectIndex, scene.NextEventObjectIndex, scene.BundleSha256, scene.Source,
            scene.ObjectsSource, scene.Positioning, scene.ArchiveSources, scene.SpriteIds, scene.Sprites,
            scene.Objects, scene.DefaultParty, scene.Map, scene.Palette);
        object ObjectState(PalSceneObject obj) => (obj.Id, obj.SVanishTime, obj.X, obj.Y, obj.SLayer,
            obj.WTriggerScript, obj.WAutoScript, obj.SState, obj.WTriggerMode, obj.WSpriteNum,
            obj.NSpriteFrames, obj.WDirection, obj.WCurrentFrameNum, obj.NScriptIdleFrame,
            obj.WSpritePtrOffset, obj.NSpriteFramesAuto, obj.WScriptIdleFrameCountAuto,
            obj.InitialFrameIndex, obj.InitiallyVisible, obj.Sprite);
        void Reject(params string[] diagnostics)
        {
            var beforeSession = SessionState();
            var beforeContext = session.Context;
            var beforeScene = SceneState();
            var beforeObjects = scene.Objects.ToArray();
            var beforeObjectWords = scene.Objects.Select(ObjectState).ToArray();
            var beforeSource = scene.Source.ToArray();
            var beforeObjectsSource = scene.ObjectsSource.ToArray();
            var beforePositioning = scene.Positioning.ToArray();
            var beforeArchives = scene.ArchiveSources.ToArray();
            var beforeSpriteIds = scene.SpriteIds.ToArray();
            var beforeSprites = scene.Sprites.ToArray();
            var beforeParty = scene.DefaultParty.ToArray();
            var beforeDisplay = display.Session;
            var beforeCurrent = display.Current;
            var beforeChildren = display.GetChildren().ToArray();
            var beforeOwned = trigger.GetChildren().ToArray();
            var beforePixel = surface.GetRenderedPixel(0, 0);
            var beforePalette = palette.ToArray();
            var beforeEvents = (palettes, restores, updates, failures);
            string error = Failure(() => trigger.Start("scene-004", "npc", 60));
            foreach (string diagnostic in diagnostics) AssertThat(error.Contains(diagnostic, StringComparison.Ordinal)).IsTrue();
            AssertThat(SessionState().Equals(beforeSession)).IsTrue();
            AssertThat(ReferenceEquals(session.Context, beforeContext)).IsTrue();
            AssertThat(SceneState().Equals(beforeScene)).IsTrue();
            AssertThat(scene.Objects.SequenceEqual(beforeObjects)).IsTrue();
            AssertThat(scene.Objects.Select(ObjectState).SequenceEqual(beforeObjectWords)).IsTrue();
            AssertThat(scene.Source.SequenceEqual(beforeSource)).IsTrue();
            AssertThat(scene.ObjectsSource.SequenceEqual(beforeObjectsSource)).IsTrue();
            AssertThat(scene.Positioning.SequenceEqual(beforePositioning)).IsTrue();
            AssertThat(scene.ArchiveSources.SequenceEqual(beforeArchives)).IsTrue();
            AssertThat(scene.SpriteIds.SequenceEqual(beforeSpriteIds)).IsTrue();
            AssertThat(scene.Sprites.SequenceEqual(beforeSprites)).IsTrue();
            AssertThat(scene.DefaultParty.SequenceEqual(beforeParty)).IsTrue();
            AssertThat(ReferenceEquals(display.Session, beforeDisplay)).IsTrue();
            AssertThat(ReferenceEquals(display.Current, beforeCurrent)).IsTrue();
            AssertThat(display.GetChildren().SequenceEqual(beforeChildren)).IsTrue();
            AssertThat(trigger.GetChildren().SequenceEqual(beforeOwned)).IsTrue();
            AssertThat(surface.GetRenderedPixel(0, 0)).IsEqual(beforePixel);
            AssertThat(palette.SequenceEqual(beforePalette)).IsTrue();
            AssertThat((palettes, restores, updates, failures).Equals(beforeEvents)).IsTrue();
        }
        try
        {
            surface.SetPalette(palette);
            surface.SetImage(ResourceLoader.Load<PalDialogueIndexedImage>("res://Tests/Fixtures/DialogueSyntheticBackground.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep));
            trigger.Initialize(display, files.Root + "/index.json", files.Root, palette, scene);
            session = trigger.Session!;
            display.ActivePaletteChanged += OnPalette;
            display.BackgroundRestoreRequested += OnRestore;
            display.UpdateCompleted += OnUpdate;
            display.Failed += OnFailure;
            if (fault == "missing-file")
                Reject($"Missing native dialogue resource: {files.Root}/Portrait-059.tres");
            else if (fault == "missing-identity")
                Reject($"{files.Root}/Portrait-059.tres: missing source identity.");
            else if (fault == "later-glyph")
            {
                Reject(files.Root + "/Font.tres", "scene-004.json#L0189 @0189", "Dialogue font has no glyph for U+0042.");
                File.Delete(files.PathFor("Portrait-059.tres"));
                // If the rejected first Start published portrait59, a second Start would
                // bypass the real-file check and repeat the glyph error instead. Font stays bad;
                // the required missing-file diagnostic is the public cache witness.
                Reject($"Missing native dialogue resource: {files.Root}/Portrait-059.tres");
            }
            else
            {
                observeNonTyping = true;
                trigger.Start("scene-004", "npc", 60);
                var request = session.PendingDisplay!;
                AssertThat(ReferenceEquals(display.Session, request.Display)).IsTrue();
                AssertThat(session.SourceRequestId is null).IsTrue(); // Actual basic legacy entry, not a fabricated world origin.
                AssertThat(session.Caller).IsEqual(60);
                AssertThat(session.ReturnEntry).IsEqual(0x0188);
                AssertThat(session.CurrentPosition!.SourceAddress).IsEqual(0x018B);
                AssertThat(request.Context.PortraitId).IsEqual(59);
                AssertThat(request.MessageReferences[0].StartMessageId).IsEqual(61);
                AssertThat(request.MessageReferences[0].Span).IsEqual(1);
                await Until(runner, () => firstNonTyping is not null || failures != 0);
                AssertThat(failures).IsEqual(0);
                AssertThat(firstNonTyping!.Phase).IsEqual(DialogueDisplayPhase.WaitingForAdvance);
                AssertThat(firstNonTyping.VisibleLines[0].Runs[0].Text).IsEqual("AB");
                AssertThat(observedPhase).IsEqual(TriggerScriptPhase.WaitingForDisplay);
                AssertThat(ReferenceEquals(session.PendingDisplay, request)).IsTrue();
                await Key(runner, Godot.Key.Space, true);
                await Until(runner, () => observedPhase == TriggerScriptPhase.Completed || failures != 0);
                AssertThat(failures).IsEqual(0);
                AssertThat(session.Phase).IsEqual(TriggerScriptPhase.Completed);
                AssertThat(display.Current!.Phase).IsEqual(DialogueDisplayPhase.Completed);
                AssertThat(display.Current.ClearDisplay).IsTrue();
                AssertThat(session.PendingDisplay is null).IsTrue();
                AssertThat(session.BlocksSceneUpdates).IsFalse();
                AssertThat(session.ReturnEntry).IsEqual(0x0188);
                AssertThat(session.Context.PortraitId).IsEqual(0);
                var completed = SessionState();
                AssertThat(session.AcknowledgeDisplay(request.Id)).IsFalse();
                AssertThat(SessionState().Equals(completed)).IsTrue();
            }
        }
        finally
        {
            SendKey(Godot.Key.Space, false);
            display.ActivePaletteChanged -= OnPalette;
            display.BackgroundRestoreRequested -= OnRestore;
            display.UpdateCompleted -= OnUpdate;
            display.Failed -= OnFailure;
            var ownedNodes = display.GetChildren().Concat(trigger.GetChildren()).ToArray();
            runner.Dispose();
            AssertThat(GodotObject.IsInstanceValid(display)).IsFalse();
            AssertThat(GodotObject.IsInstanceValid(trigger)).IsFalse();
            foreach (var node in ownedNodes) AssertThat(GodotObject.IsInstanceValid(node)).IsFalse();
        }
    }

    [TestCase(Timeout = 15000)]
    public void Undersized_glyph_row_stride_is_rejected_before_display_attachment()
    {
        using var files = new SyntheticFiles();
        files.Edit("Font.tres", text => text.Replace("Width = 8", "Width = 16"));
        using var runner = ISceneRunner.Load("res://Tests/DialogueDisplayFixture.tscn", true);
        var display = (PalDialogueDisplay)runner.Scene()!;
        var trigger = new PalTriggerDialogue(); display.AddChild(trigger);
        using var scene = Scene();
        trigger.Initialize(display, files.Root + "/index.json", files.Root, Palette(), scene);
        InvalidDataException? failure = null;
        try { trigger.Start("scene-004", "npc", 60); }
        catch (InvalidDataException error) { failure = error; }
        // Synchronous preflight only: never wait for a malformed glyph to reach _Draw.
        AssertThat(failure is not null).IsTrue();
        AssertThat(failure!.Message.Contains(files.Root + "/Font.tres")).IsTrue();
        AssertThat(failure.Message.Contains("scene-004.json") && failure.Message.Contains("0189")).IsTrue();
        AssertThat(failure.InnerException?.InnerException?.GetType() == typeof(InvalidDataException)).IsTrue();
        AssertThat(failure.InnerException!.InnerException!.Message).IsEqual("Dialogue glyph U+0041 has invalid bitmap dimensions.");
        AssertThat(display.Session is null).IsTrue();
        AssertThat(trigger.Session!.Phase).IsEqual(TriggerScriptPhase.Ready);
    }

    [TestCase(8, 1, Timeout = 15000)]
    [TestCase(9, 2, Timeout = 15000)]
    [TestCase(16, 2, Timeout = 15000)]
    public void Valid_glyph_row_stride_boundaries_pass_normal_trigger_preflight(int width, int rowBytes)
    {
        using var files = new SyntheticFiles();
        files.Edit("Font.tres", text => System.Text.RegularExpressions.Regex.Replace(
            text.Replace("Width = 8", $"Width = {width}").Replace("RowBytes = 1", $"RowBytes = {rowBytes}"),
            @"Bitmap = PackedByteArray\([^)]*\)",
            "Bitmap = PackedByteArray(" + string.Join(", ", Enumerable.Repeat("255", rowBytes * 15)) + ")"));
        using var runner = ISceneRunner.Load("res://Tests/DialogueDisplayFixture.tscn", true);
        var display = (PalDialogueDisplay)runner.Scene()!;
        var trigger = new PalTriggerDialogue(); display.AddChild(trigger);
        using var scene = Scene();
        trigger.Initialize(display, files.Root + "/index.json", files.Root, Palette(), scene);
        trigger.Start("scene-004", "npc", 60);
        AssertThat(display.Session is not null).IsTrue();
        AssertThat(trigger.Session!.Phase).IsEqual(TriggerScriptPhase.WaitingForDisplay);
        var font = ResourceLoader.Load<PalDialogueFont>(files.Root + "/Font.tres");
        AssertThat(font.GetGlyph(65).IsSet(width - 1, 14)).IsTrue();
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
        surface.SetImage(ResourceLoader.Load<PalDialogueIndexedImage>(Root + "/Portrait-059.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep));
        var savedBackground = ResourceLoader.Load<PalDialogueIndexedImage>("res://Tests/Fixtures/DialogueSyntheticBackground.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
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
        await Until(runner, () => updated?.Phase == DialogueDisplayPhase.Completed);
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
        await Until(runner, () => trigger.Session.Phase == TriggerScriptPhase.Completed);
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
        surface.SetImage(ResourceLoader.Load<PalDialogueIndexedImage>(Root + "/Portrait-059.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep));
        var background = ResourceLoader.Load<PalDialogueIndexedImage>("res://Tests/Fixtures/DialogueSyntheticBackground.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
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
        await Until(runner, () => trigger.Session.Phase == TriggerScriptPhase.Completed);
        AssertThat(trigger.Session.Phase).IsEqual(TriggerScriptPhase.Completed);
        AssertThat(restores).IsEqual(3);
    }

    [TestCase(Timeout = 15000)]
    public void Story_current_world_hidden_glyph_is_rejected_before_production_Start() =>
        AssertStoryResourceRejection("hidden-current-glyph");

    [TestCase("missing-used-glyph", Timeout = 15000)]
    [TestCase("duplicate-used-glyph", Timeout = 15000)]
    [TestCase("bad-used-stride", Timeout = 15000)]
    [TestCase("prompt-0", Timeout = 15000)]
    [TestCase("prompt-1", Timeout = 15000)]
    [TestCase("prompt-2", Timeout = 15000)]
    [TestCase("portrait-7", Timeout = 15000)]
    [TestCase("future-portrait-10", Timeout = 15000)]
    public void Story_current_world_bound_resource_faults_reject_before_production_Start(string fault) =>
        AssertStoryResourceRejection(fault);

    [TestCase(Timeout = 15000)]
    public void Story_missing_name_key_rejects_seed_glyph_before_production_Start() =>
        AssertStoryResourceRejection("missing-seed-glyph");

    [TestCase(Timeout = 15000)]
    public void Story_future_literal_0090_glyph_rejects_before_Start() =>
        AssertStoryResourceRejection("future-write-glyph");

    [TestCase(Timeout = 15000)]
    public void Story_second_order_literal_0090_glyph_rejects_before_Start() =>
        AssertStoryResourceRejection("second-order-write-glyph");

    [TestCase("random-miss-glyph", Timeout = 15000)]
    [TestCase("random-target-glyph", Timeout = 15000)]
    public void Story_random_hook_branch_glyphs_reject_before_Start(string fault) =>
        AssertStoryResourceRejection(fault);

    [TestCase("title-missing-glyph", Timeout = 15000)]
    [TestCase("title-duplicate-glyph", Timeout = 15000)]
    [TestCase("title-bad-stride", Timeout = 15000)]
    public void Story_title_only_glyph_faults_reject_before_Start(string fault) =>
        AssertStoryResourceRejection(fault);

    [TestCase(Timeout = 15000)]
    public void Story_hidden_portrait_without_bound_mapping_rejects_before_Start() =>
        AssertStoryResourceRejection("missing-hidden-portrait");

    [TestCase("no-portrait", Timeout = 15000)]
    [TestCase("valid-portrait", Timeout = 15000)]
    [TestCase("bad-portrait", Timeout = 15000)]
    public void Story_zero_display_ignores_unused_font_and_prompts_but_validates_portrait(string mode)
    {
        using var files = new NativeStoryProfileFixture(currentHookPositive: true);
        // Independent physical WORD layout: setDialog@1 -> keep@2, no display or reachable battle.
        var document = System.Text.Json.Nodes.JsonNode.Parse(files.Read("shared.json"))!.AsObject();
        var commands = document["commands"]!.AsArray();
        int portraitId = mode == "no-portrait" ? 0 : 7;
        var dialog = commands.Single(node => (int)node!["source"]!["index"]! == 1)!;
        dialog["portraitId"] = portraitId;
        dialog["source"]!["operands"] = new System.Text.Json.Nodes.JsonArray(portraitId, 0, 0);
        commands[2] = System.Text.Json.Nodes.JsonNode.Parse("""
            {"label":"L0002","op":"end","mode":"keep","next":"L0003","source":{"index":2,"opcode":0,"operands":[0,0,0]}}
            """);
        files.Write("shared.json", document.ToJsonString());
        foreach (int id in new[] { 7, 8, 9, 10 })
            if (id != portraitId) files.Profile.Portraits.Remove(id);
        files.RefreshHashes();
        foreach (int id in new[] { 7, 8, 9, 10 })
            if (id != portraitId) files.Profile.ResourceSha256.Remove(files.Root + $"/Portrait-{id}.tres");
        files.SaveProfile();
        var story = NativeStoryProfile.Load(files.Uri, "en"); // Preserve strict bilingual/static validation.
        AssertThat(story.Portraits.Count).IsEqual(portraitId == 0 ? 0 : 1);
        var roles = new RoleLiveState(story.BattleContent.InitialRoles.Values.ToArray());
        var world = DynamicEventWorld.ForHost(story.Exploration.Rules, story.Exploration.Leader,
            files.Static.Scene.SceneId, files.Static.Scene.WScriptOnEnter, files.Static.Scene.WScriptOnTeleport,
            roles, story.BattleContent.Players.Select(player => player.RoleId).ToArray());
        var interaction = NpcInteractionSession.ForSelectedEntry(story.Exploration.Rules,
            story.Exploration.Leader, story.Entry);
        interaction.PressSearch();
        AssertThat(interaction.Update(0)).IsTrue();
        var request = interaction.PendingTrigger ?? throw new InvalidOperationException("Missing real zero-display Story search.");
        AssertThat(request.Caller).IsEqual(60);
        AssertThat(request.SourceAddress).IsEqual(1);
        AssertThat(request.Id == Guid.Empty).IsFalse();
        var random = new CountingStoryRandom();
        var runner = ISceneRunner.Load("res://Tests/DialogueDisplayFixture.tscn", true);
        var display = (PalDialogueDisplay)runner.Scene()!;
        var trigger = new PalTriggerDialogue { FirstBattleWorld = world };
        display.AddChild(trigger);
        var surface = display.GetNode<PalDialoguePaletteSurface>("IndexedBackground");
        int updates = 0, palettes = 0, restores = 0, failures = 0;
        void OnUpdate() => updates++;
        void OnPalette(Color[] _) => palettes++;
        void OnRestore() => restores++;
        void OnFailure(Exception _) => failures++;
        try
        {
            surface.SetPalette(story.Palette.ToArray());
            surface.SetImage(ResourceLoader.Load<PalDialogueIndexedImage>("res://Tests/Fixtures/DialogueSyntheticBackground.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep));
            trigger.Initialize(display, story, random);
            display.UpdateCompleted += OnUpdate;
            display.ActivePaletteChanged += OnPalette;
            display.BackgroundRestoreRequested += OnRestore;
            display.Failed += OnFailure;
            // Controlled faults in this fresh, already strictly loaded artificial binding only.
            story.Font.FormatVersion = 0;
            foreach (var prompt in story.Prompts) prompt.Alpha = [];
            if (mode == "bad-portrait") story.Portraits[7].Alpha = [];
            var session = trigger.Session!;
            var Capture = () => (
                Call: (session.Phase, session.BlocksSceneUpdates, session.CallId, session.SourceRequestId,
                    session.StartedEntry, session.Caller, session.ReturnEntry, session.CurrentPosition, session.Context,
                    session.ScriptSuccess, session.Music, session.FadedToRed, session.PendingDisplay, session.PendingBattle,
                    session.PendingLostReload, session.PendingSceneWait, session.PendingMovement, session.PendingTransition,
                    session.PendingBattleHook, session.RunningBattleHook, session.RunningBattleEndScript, session.BattleRoles),
                World: (world.Leader, world.Objects, world.PartyRoles, world.RoleLiveState, roles.Roles[0], roles.Roles[1]),
                Words: System.Text.Json.JsonSerializer.Serialize(new {
                    world.SceneId, world.Battlefield, world.Objects, world.PartyRoles, world.PartyVitals, Roles = roles.Roles,
                    Bank = world.ObjectScripts.OrderBy(pair => pair.Key).ToArray(),
                    Scenes = world.SceneScripts.OrderBy(pair => pair.Key).ToArray() }),
                Display: (display.Session, display.Current),
                Nodes: string.Join(",", display.GetChildren().Concat(trigger.GetChildren()).Select(node => node.GetInstanceId())),
                Pixel: surface.GetRenderedPixel(0, 0),
                Outer: (interaction.Phase, interaction.Current, interaction.PendingTrigger, interaction.BlocksSceneUpdates),
                Events: (updates, palettes, restores, failures),
                Random: (random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws));
            var before = Capture();
            if (mode == "bad-portrait")
            {
                string error = Failure(() => trigger.Start(request.FileId, request.EntryId, request.Caller, request.Id));
                AssertThat(error.Contains(story.Portraits[7].ResourcePath, StringComparison.Ordinal)).IsTrue();
                AssertThat(error.Contains("Dialogue indexed image has invalid dimensions or planes.", StringComparison.Ordinal)).IsTrue();
                AssertThat(Capture().Equals(before)).IsTrue();
            }
            else
            {
                trigger.Start(request.FileId, request.EntryId, request.Caller, request.Id);
                AssertThat(session.Phase).IsEqual(TriggerScriptPhase.Completed);
                AssertThat(session.CallId == Guid.Empty).IsFalse();
                AssertThat(session.SourceRequestId).IsEqual(request.Id);
                AssertThat(session.Caller).IsEqual(60);
                AssertThat(session.ReturnEntry).IsEqual(1);
                AssertThat(session.CurrentPosition == new TriggerPosition("shared", "L0002", 2)).IsTrue();
                AssertThat(session.BlocksSceneUpdates).IsFalse();
                AssertThat(session.PendingDisplay is null && session.PendingBattle is null).IsTrue();
                AssertThat(interaction.BindTrigger(request.Id, session)).IsTrue();
                AssertThat(interaction.CompleteTrigger(request.Id, session, 0)).IsTrue();
                AssertThat(interaction.PendingTrigger is null).IsTrue();
                AssertThat(interaction.Phase).IsEqual(NpcInteractionPhase.Returning);
                AssertThat(interaction.Update(49)).IsFalse();
                AssertThat(interaction.Update(50)).IsFalse();
                AssertThat(interaction.Update(100)).IsTrue();
                AssertThat(interaction.Phase).IsEqual(NpcInteractionPhase.Exploring);
            }
            AssertThat(world.ObjectScripts.Count).IsEqual(0); // Unreachable mapped battle must not seed its names.
            AssertThat(display.Session is null && display.Current is null).IsTrue();
            AssertThat((updates, palettes, restores, failures).Equals((0, 0, 0, 0))).IsTrue();
            AssertThat((random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws).Equals((0, 0, 0, 0))).IsTrue();
            AssertThat(story.Font.FormatVersion).IsEqual(0);
            foreach (var prompt in story.Prompts) AssertThat(prompt.Alpha.Length).IsEqual(0);
        }
        finally
        {
            display.UpdateCompleted -= OnUpdate;
            display.ActivePaletteChanged -= OnPalette;
            display.BackgroundRestoreRequested -= OnRestore;
            display.Failed -= OnFailure;
            var ownedNodes = display.GetChildren().Concat(trigger.GetChildren()).ToArray();
            runner.Dispose();
            AssertThat(GodotObject.IsInstanceValid(display)).IsFalse();
            AssertThat(GodotObject.IsInstanceValid(trigger)).IsFalse();
            foreach (var node in ownedNodes) AssertThat(GodotObject.IsInstanceValid(node)).IsFalse();
        }
    }

    private void AssertStoryResourceRejection(string fault)
    {
        bool hidden = fault == "hidden-current-glyph";
        bool missingSeed = fault == "missing-seed-glyph";
        bool secondOrderWrite = fault == "second-order-write-glyph";
        bool randomBranch = fault is "random-miss-glyph" or "random-target-glyph";
        bool titleGlyph = fault is "title-missing-glyph" or "title-duplicate-glyph" or "title-bad-stride";
        bool futureWrite = titleGlyph || randomBranch || fault is "future-write-glyph" or "second-order-write-glyph";
        using var files = new NativeStoryProfileFixture(hiddenCurrentGlyph: hidden, currentHookPositive: !hidden,
            seedDyingGlyph: missingSeed, futureDyingWrite: futureWrite, secondOrderDyingWrite: secondOrderWrite,
            randomDyingBranch: randomBranch, titleDyingGlyph: titleGlyph);
        if (fault == "missing-hidden-portrait")
        {
            files.Edit("shared.json", "\"label\":\"L0014\",\"op\":\"setDialog\",\"box\":\"upper\",\"portraitId\":7",
                "\"label\":\"L0014\",\"op\":\"setDialog\",\"box\":\"upper\",\"portraitId\":11");
            files.Edit("shared.json", "\"index\":20,\"opcode\":60,\"operands\":[7,0,0]",
                "\"index\":20,\"opcode\":60,\"operands\":[11,0,0]");
            files.RefreshHashes(); files.SaveProfile();
        }
        if (futureWrite)
        {
            files.Edit("shared.json", "\"value\":20,\"slotIndex\":1", "\"value\":0,\"slotIndex\":1");
            files.Edit("shared.json", "\"opcode\":144,\"operands\":[0,20,1]", "\"opcode\":144,\"operands\":[0,0,1]");
            files.RefreshHashes(); files.SaveProfile();
        }
        var story = NativeStoryProfile.Load(files.Uri, "en");
        // Strict Load succeeds first. These are controlled faults in this example's artificial
        // bound Resource, not shared assets or a guarantee against arbitrary hot reload.
        // Existing live20 uses U47; the seed negative has a separate fresh absent-seed full-return control.
        switch (fault)
        {
            case "hidden-current-glyph":
            case "missing-hidden-portrait": break;
            case "missing-seed-glyph":
            case "future-write-glyph": story.Font.Glyphs.Remove(story.Font.GetGlyph(69)); break;
            case "second-order-write-glyph": story.Font.Glyphs.Remove(story.Font.GetGlyph(70)); break;
            case "random-miss-glyph": story.Font.Glyphs.Remove(story.Font.GetGlyph(69)); break;
            case "random-target-glyph": story.Font.Glyphs.Remove(story.Font.GetGlyph(71)); break;
            case "title-missing-glyph": story.Font.Glyphs.Remove(story.Font.GetGlyph(71)); break;
            case "title-duplicate-glyph": story.Font.Glyphs.Add(story.Font.GetGlyph(71)); break;
            case "title-bad-stride": story.Font.GetGlyph(71).RowBytes = 0; break;
            case "missing-used-glyph": story.Font.Glyphs.Remove(story.Font.GetGlyph(65)); break;
            case "duplicate-used-glyph": story.Font.Glyphs.Add(story.Font.GetGlyph(65)); break;
            case "bad-used-stride": story.Font.GetGlyph(65).RowBytes = 0; break;
            case "prompt-0": story.Prompts[0].Width = 2; break;
            case "prompt-1": story.Prompts[1].Width = 2; break;
            case "prompt-2": story.Prompts[2].Width = 2; break;
            case "portrait-7": story.Portraits[7].Width = 2; break;
            case "future-portrait-10": story.Portraits[10].Width = 2; break;
            default: throw new ArgumentOutOfRangeException(nameof(fault));
        }
        (string Resource, string Diagnostic, string? Position) expected = fault switch
        {
            "hidden-current-glyph" => ("Font.tres", "no glyph for U+0045", "shared.json#L0015 @0015"),
            "missing-hidden-portrait" => ("", "shared.json entry npc @0001: missing bound dialogue portrait 11.", (string?)null),
            "missing-seed-glyph" => ("Font.tres", "no glyph for U+0045", "shared.json#L0029 @0029"),
            "future-write-glyph" => ("Font.tres", "no glyph for U+0045", "shared.json#L0019 @0019"),
            "second-order-write-glyph" => ("Font.tres", "no glyph for U+0046", "shared.json#L0021 @0021"),
            "random-miss-glyph" => ("Font.tres", "no glyph for U+0045", "shared.json#L001A @001A"),
            "random-target-glyph" => ("Font.tres", "no glyph for U+0047", "shared.json#L0021 @0021"),
            "title-missing-glyph" => ("Font.tres", "no glyph for U+0047", "shared.json#L0019 @0019"),
            "title-duplicate-glyph" => ("Font.tres", "duplicate glyphs for U+0047", "shared.json#L0019 @0019"),
            "title-bad-stride" => ("Font.tres", "invalid bitmap dimensions", "shared.json#L0019 @0019"),
            "missing-used-glyph" => ("Font.tres", "no glyph for U+0041", "shared.json#L0002 @0002"),
            "duplicate-used-glyph" => ("Font.tres", "duplicate glyphs for U+0041", "shared.json#L0002 @0002"),
            "bad-used-stride" => ("Font.tres", "invalid bitmap dimensions", "shared.json#L0002 @0002"),
            "prompt-0" => ("Prompt-00.tres", "invalid dimensions or planes", "shared.json#L0002 @0002"),
            "prompt-1" => ("Prompt-01.tres", "invalid dimensions or planes", "shared.json#L0002 @0002"),
            "prompt-2" => ("Prompt-02.tres", "invalid dimensions or planes", "shared.json#L0002 @0002"),
            // PortraitIds have no exact command positions: require the real mapped URI instead.
            "portrait-7" => ("Portrait-7.tres", "invalid dimensions or planes", (string?)null),
            "future-portrait-10" => ("Portrait-10.tres", "invalid dimensions or planes", (string?)null),
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };
        var roles = new RoleLiveState(story.BattleContent.InitialRoles.Values.ToArray());
        var scene = files.Static.Scene;
        var world = DynamicEventWorld.ForHost(story.Exploration.Rules, story.Exploration.Leader,
            scene.SceneId, scene.WScriptOnEnter, scene.WScriptOnTeleport, roles,
            story.BattleContent.Players.Select(player => player.RoleId).ToArray());
        var random = new CountingStoryRandom();
        // Missing-key rejection must not seed; other faults retain their real live20 preparation.
        if (missingSeed)
            AssertThat(world.ObjectScripts.Count).IsEqual(0);
        else
        {
            var setter = new TriggerScriptSession(story.Events, story.Language, world);
            setter.Start("shared", "setter", 60);
            AssertThat(setter.Phase).IsEqual(TriggerScriptPhase.Completed);
            AssertThat(world.ObjectScripts.Count).IsEqual(1);
            AssertThat(world.ObjectScripts[(0, 1)]).IsEqual(futureWrite ? 0 : 20);
            AssertThat(world.ObjectScripts.ContainsKey((0, 0))).IsFalse();
        }
        var interaction = NpcInteractionSession.ForSelectedEntry(story.Exploration.Rules,
            story.Exploration.Leader, story.Entry);
        interaction.PressSearch();
        AssertThat(interaction.Update(0)).IsTrue();
        var request = interaction.PendingTrigger
            ?? throw new InvalidOperationException("The authored owner60 geometry did not produce a search request.");
        AssertThat(request.Caller).IsEqual(60);
        AssertThat(request.SourceAddress).IsEqual(1);

        using var runner = ISceneRunner.Load("res://Tests/DialogueDisplayFixture.tscn", true);
        var display = (PalDialogueDisplay)runner.Scene()!;
        var trigger = new PalTriggerDialogue { FirstBattleWorld = world };
        display.AddChild(trigger);
        trigger.Initialize(display, story, random);
        var session = trigger.Session!;
        var surface = display.GetNode<PalDialoguePaletteSurface>("IndexedBackground");
        surface.SetPalette(story.Palette.ToArray());
        surface.SetImage(ResourceLoader.Load<PalDialogueIndexedImage>("res://Tests/Fixtures/DialogueSyntheticBackground.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep));
        int palettes = 0, restores = 0, updates = 0, failures = 0;
        void OnPalette(Color[] _) => palettes++;
        void OnRestore() => restores++;
        void OnUpdate() => updates++;
        void OnFailure(Exception _) => failures++;
        display.ActivePaletteChanged += OnPalette;
        display.BackgroundRestoreRequested += OnRestore;
        display.UpdateCompleted += OnUpdate;
        display.Failed += OnFailure;
        try
        {
            object SessionState() => (session.Phase, session.BlocksSceneUpdates, session.CallId,
                session.SourceRequestId, session.StartedEntry, session.Caller, session.ReturnEntry,
                session.CurrentPosition, session.Context, session.ScriptSuccess, session.Music, session.FadedToRed,
                session.PendingDisplay, session.PendingBattle, session.PendingLostReload, session.PendingSceneWait,
                session.PendingMovement, session.PendingTransition, session.PendingBattleHook,
                session.RunningBattleHook, session.RunningBattleEndScript, session.BattleRoles);
            object ObjectState(DynamicEventObject obj) => (obj.Id, obj.X, obj.Y, obj.Direction, obj.CurrentFrame,
                obj.State, obj.VanishTime, obj.TriggerMode, obj.TriggerEntry, obj.AutoEntry, obj.AutoIdleCount);
            if (missingSeed || futureWrite)
            {
                AssertThat(world.ObjectScripts.Count).IsEqual(missingSeed ? 0 : 1);
                if (futureWrite) AssertThat(world.ObjectScripts[(0, 1)]).IsEqual(0);
                AssertThat((random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws).Equals((0, 0, 0, 0))).IsTrue();
            }
            var beforeSession = SessionState();
            var beforeContext = session.Context;
            var beforeBank = world.ObjectScripts.OrderBy(pair => pair.Key).ToArray();
            var beforeScenes = world.SceneScripts.OrderBy(pair => pair.Key).ToArray();
            var beforeWorld = (world.SceneId, world.Leader, world.Battlefield);
            var beforeParty = world.PartyRoles.ToArray();
            var beforePartyReference = world.PartyRoles;
            var beforeObjects = world.Objects.ToArray();
            var beforeObjectWords = world.Objects.Select(ObjectState).ToArray();
            var beforeVitals = world.PartyVitals.OrderBy(pair => pair.Key).ToArray();
            var beforeVitalWords = beforeVitals.Select(pair => (pair.Key, pair.Value.Hp, pair.Value.MaxHp, pair.Value.Mp, pair.Value.MaxMp)).ToArray();
            var beforeRoles = roles.Roles.OrderBy(pair => pair.Key).ToArray();
            var beforeBattleRoles = session.BattleRoles!.OrderBy(pair => pair.Key).ToArray();
            var beforeDisplay = display.Session;
            var beforeCurrent = display.Current;
            var beforeChildren = display.GetChildren().ToArray();
            var beforeOwned = trigger.GetChildren().ToArray();
            var beforePixel = surface.GetRenderedPixel(0, 0);
            var beforeEvents = (palettes, restores, updates, failures);
            var beforeRandom = (random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws);
            var beforeInteraction = (interaction.Current, interaction.Phase, interaction.BlocksSceneUpdates, interaction.PendingTrigger);

            string error = Failure(() => trigger.Start(request.FileId, request.EntryId, request.Caller, request.Id));
            if (expected.Resource.Length != 0)
                AssertThat(error.Contains(files.Root + "/" + expected.Resource, StringComparison.Ordinal)).IsTrue();
            AssertThat(error.Contains(expected.Diagnostic, StringComparison.Ordinal)).IsTrue();
            if (expected.Position is not null)
                AssertThat(error.Contains(expected.Position, StringComparison.Ordinal)).IsTrue();
            AssertThat(Equals(SessionState(), beforeSession)).IsTrue();
            AssertThat(ReferenceEquals(session.Context, beforeContext)).IsTrue();
            AssertThat(world.ObjectScripts.OrderBy(pair => pair.Key).SequenceEqual(beforeBank)).IsTrue();
            AssertThat(world.SceneScripts.OrderBy(pair => pair.Key).SequenceEqual(beforeScenes)).IsTrue();
            AssertThat((world.SceneId, world.Leader, world.Battlefield).Equals(beforeWorld)).IsTrue();
            AssertThat(ReferenceEquals(world.RoleLiveState, roles)).IsTrue();
            AssertThat(ReferenceEquals(world.PartyRoles, beforePartyReference)).IsTrue();
            AssertThat(world.PartyRoles.SequenceEqual(beforeParty)).IsTrue();
            AssertThat(world.Objects.SequenceEqual(beforeObjects)).IsTrue();
            AssertThat(world.Objects.Select(ObjectState).SequenceEqual(beforeObjectWords)).IsTrue();
            AssertThat(world.PartyVitals.OrderBy(pair => pair.Key).SequenceEqual(beforeVitals)).IsTrue();
            AssertThat(world.PartyVitals.OrderBy(pair => pair.Key)
                .Select(pair => (pair.Key, pair.Value.Hp, pair.Value.MaxHp, pair.Value.Mp, pair.Value.MaxMp)).SequenceEqual(beforeVitalWords)).IsTrue();
            AssertThat(roles.Roles.Count).IsEqual(beforeRoles.Length);
            foreach (var pair in beforeRoles) AssertThat(ReferenceEquals(roles.Roles[pair.Key], pair.Value)).IsTrue();
            AssertThat(session.BattleRoles!.Count).IsEqual(beforeBattleRoles.Length);
            foreach (var pair in beforeBattleRoles) AssertThat(ReferenceEquals(session.BattleRoles[pair.Key], pair.Value)).IsTrue();
            AssertThat(ReferenceEquals(display.Session, beforeDisplay)).IsTrue();
            AssertThat(ReferenceEquals(display.Current, beforeCurrent)).IsTrue();
            AssertThat(display.GetChildren().SequenceEqual(beforeChildren)).IsTrue();
            AssertThat(trigger.GetChildren().SequenceEqual(beforeOwned)).IsTrue();
            AssertThat(surface.GetRenderedPixel(0, 0)).IsEqual(beforePixel);
            AssertThat((palettes, restores, updates, failures).Equals(beforeEvents)).IsTrue();
            AssertThat((random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws).Equals(beforeRandom)).IsTrue();
            AssertThat((interaction.Current, interaction.Phase, interaction.BlocksSceneUpdates, interaction.PendingTrigger).Equals(beforeInteraction)).IsTrue();
            AssertThat(ReferenceEquals(interaction.PendingTrigger, request)).IsTrue();
            if (hidden)
            {
                var CapturePriority = () => (
                    Call: SessionState(),
                    World: (world.Leader, world.RoleLiveState, world.PartyRoles, world.Objects),
                    Words: System.Text.Json.JsonSerializer.Serialize(new { world.SceneId, world.Battlefield,
                        world.Objects, world.PartyRoles, world.PartyVitals, Roles = roles.Roles, BattleRoles = session.BattleRoles,
                        Bank = world.ObjectScripts.OrderBy(pair => pair.Key).Select(pair => new { Key = pair.Key.ToString(), pair.Value }),
                        Scenes = world.SceneScripts.OrderBy(pair => pair.Key).Select(pair => new { Key = pair.Key.ToString(), pair.Value }) }),
                    Roles: (roles.Roles[0], roles.Roles.ContainsKey(1) ? roles.Roles[1] : null,
                        session.BattleRoles![0], session.BattleRoles!.ContainsKey(1) ? session.BattleRoles![1] : null),
                    Display: (display.Session, display.Current),
                    Nodes: string.Join(",", display.GetChildren().Concat(trigger.GetChildren()).Select(node => node.GetInstanceId())),
                    Pixel: surface.GetRenderedPixel(0, 0), Events: (palettes, restores, updates, failures),
                    Random: (random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws),
                    Producer: (interaction.Current, interaction.Phase, interaction.BlocksSceneUpdates, interaction.PendingTrigger));
                var uninitialized = new PalTriggerDialogue();
                try
                {
                    AssertStartRejection(CapturePriority, () => uninitialized.Start("shared", "missing-entry", 65536, Guid.Empty),
                        typeof(ArgumentException), "A source request ID must be nonempty.");
                    AssertStartRejection(CapturePriority, () => uninitialized.Start("shared", "missing-entry", 65536, request.Id),
                        typeof(InvalidOperationException), "Initialize trigger dialogue before starting an entry.");
                    AssertThat(uninitialized.Session is null).IsTrue();
                }
                finally { uninitialized.Free(); }
                AssertStartRejection(CapturePriority, () => trigger.Start("shared", "setter", 65536, Guid.Empty),
                    typeof(ArgumentException), "A source request ID must be nonempty.");
                AssertStartRejection(CapturePriority, () => trigger.Start("shared", "setter", 65536),
                    typeof(InvalidDataException), "Story adapter only supports its checked selected entry.");
                AssertStartRejection(CapturePriority, () => trigger.Start(request.FileId, request.EntryId, 65536),
                    typeof(InvalidOperationException), "A story trigger requires its source interaction request.");
                AssertStartRejection(CapturePriority, () => trigger.Start(request.FileId, request.EntryId, 65536, request.Id),
                    typeof(ArgumentOutOfRangeException), "caller");
                // A real independent public display producer, not a fabricated request/phase.
                display.Start(files.English, story.Font, story.Portraits[7], story.Prompts.ToArray(),
                    story.Palette.ToArray(), 61, 0, 7, 0, 0);
                AssertThat(display.Current!.Phase).IsEqual(DialogueDisplayPhase.Typing);
                AssertStartRejection(CapturePriority, () => trigger.Start("shared", "missing-entry", 65536, request.Id),
                    typeof(InvalidOperationException), "The display is already in use.");
                AssertStartRejection(CapturePriority, () => trigger.Start(request.FileId, request.EntryId, 65536),
                    typeof(InvalidOperationException), "The display is already in use.");
                AssertStartRejection(CapturePriority, () => trigger.Start("shared", "setter", 65536, Guid.Empty),
                    typeof(ArgumentException), "A source request ID must be nonempty.");
            }
        }
        finally
        {
            display.ActivePaletteChanged -= OnPalette;
            display.BackgroundRestoreRequested -= OnRestore;
            display.UpdateCompleted -= OnUpdate;
            display.Failed -= OnFailure;
            runner.Dispose();
            AssertThat(GodotObject.IsInstanceValid(display)).IsFalse();
            AssertThat(GodotObject.IsInstanceValid(trigger)).IsFalse();
        }
    }

    [TestCase(Timeout = 15000)]
    public async Task First_battle_current_world_hidden_glyph_is_rejected_before_production_Start() =>
        await AssertFirstBattleResource("hidden-current-glyph");

    [TestCase(Timeout = 15000)]
    public async Task First_battle_current_world_bound_palette_is_rejected_before_production_Start() =>
        await AssertFirstBattleResource("palette-255");

    [TestCase(Timeout = 15000)]
    public async Task First_battle_missing_name_key_rejects_seed_glyph_before_production_Start() =>
        await AssertFirstBattleResource("missing-seed-glyph");

    [TestCase(Timeout = 15000)]
    public async Task First_battle_future_literal_0090_glyph_rejects_before_Start() =>
        await AssertFirstBattleResource("future-write-glyph");

    [TestCase(Timeout = 15000)]
    public async Task First_battle_second_order_literal_0090_glyph_rejects_before_Start() =>
        await AssertFirstBattleResource("second-order-write-glyph");

    [TestCase("clean-control", Timeout = 15000)]
    [TestCase("missing-used-glyph", Timeout = 15000)]
    [TestCase("duplicate-used-glyph", Timeout = 15000)]
    [TestCase("bad-used-stride", Timeout = 15000)]
    [TestCase("prompt-0", Timeout = 15000)]
    [TestCase("prompt-1", Timeout = 15000)]
    [TestCase("prompt-2", Timeout = 15000)]
    public async Task First_battle_current_world_bound_resources_validate_before_production_Start(string fault) =>
        await AssertFirstBattleResource(fault);

    [TestCase("random-miss-glyph", Timeout = 15000)]
    [TestCase("random-target-glyph", Timeout = 15000)]
    public async Task First_battle_random_hook_branch_glyphs_reject_before_Start(string fault) =>
        await AssertFirstBattleResource(fault);

    [TestCase("title-missing-glyph", Timeout = 15000)]
    [TestCase("title-duplicate-glyph", Timeout = 15000)]
    [TestCase("title-bad-stride", Timeout = 15000)]
    public async Task First_battle_title_only_glyph_faults_reject_before_Start(string fault) =>
        await AssertFirstBattleResource(fault);

    [TestCase(Timeout = 15000)]
    public async Task First_battle_hidden_portrait_without_bound_mapping_rejects_before_Start() =>
        await AssertFirstBattleResource("missing-hidden-portrait");

    [TestCase(Timeout = 15000)]
    public async Task First_battle_portrait_without_showDialog_still_requires_bound_mapping() =>
        await AssertFirstBattleResource("portrait-only-mapping");

    private async Task AssertFirstBattleResource(string fault)
    {
        bool hidden = fault == "hidden-current-glyph";
        bool seedGlyph = fault == "missing-seed-glyph";
        bool secondOrderWrite = fault == "second-order-write-glyph";
        bool randomBranch = fault is "random-miss-glyph" or "random-target-glyph";
        bool titleGlyph = fault is "title-missing-glyph" or "title-duplicate-glyph" or "title-bad-stride";
        bool futureWrite = titleGlyph || randomBranch || fault is "future-write-glyph" or "second-order-write-glyph";
        using var battleFixture = new NativeBattleProfileFixture();
        var statics = battleFixture.Static;
        foreach (var frame in statics.MapScene.Data.Map.Tiles.Frames)
        {
            frame.Indices = Enumerable.Repeat((byte)0, frame.Width * frame.Height).ToArray();
            frame.Alpha = Enumerable.Repeat((byte)255, frame.Width * frame.Height).ToArray();
        }
        foreach (var sprite in statics.MapScene.Data.Sprites)
            foreach (var frame in sprite.Frames)
            {
                frame.Indices = Enumerable.Repeat((byte)0, frame.Width * frame.Height).ToArray();
                frame.Alpha = Enumerable.Repeat((byte)255, frame.Width * frame.Height).ToArray();
            }
        statics.MapScene.Data.Palette = ResourceLoader.Load<PalPalette>(
            "res://Samples/AuthoredBattle/Static/Palette.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep)!;
        statics.SavePacked();
        string staticProfile = statics.ResourceRoot + "/Profile.tres";
        NativeProfileFixture.SaveResource(statics.Profile, staticProfile);
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(statics.ResourceRoot + "/Palettes"));
        File.Copy(ProjectSettings.GlobalizePath("res://Samples/AuthoredBattle/Static/Palette.tres"),
            ProjectSettings.GlobalizePath(statics.ResourceRoot + "/Palettes/Palette.tres"));
        using var files = new SyntheticFirstBattleFixture(statics, statics.ResourceRoot, staticProfile,
            lost: false, hiddenCurrentGlyph: hidden, currentHookPositive: !hidden,
            forceCurrentDying: seedGlyph || futureWrite, seedDyingGlyph: seedGlyph, futureDyingWrite: futureWrite,
            secondOrderDyingWrite: secondOrderWrite, randomDyingBranch: randomBranch, titleDyingGlyph: titleGlyph,
            currentHookPortrait: fault is "missing-hidden-portrait" or "portrait-only-mapping" ? 11 : 0,
            currentHookPortraitOnly: fault == "portrait-only-mapping");
        var profile = NativeFirstMandatoryBattleProfile.Load(files.ProfileUri);
        var resources = profile.Resources ?? throw new InvalidOperationException("The synthetic real closure has no resources.");
        // Strict Load succeeded first; fault only this example's actually bound artificial inputs.
        switch (fault)
        {
            case "hidden-current-glyph":
            case "clean-control":
            case "palette-255":
            case "missing-hidden-portrait":
            case "portrait-only-mapping": break;
            case "missing-seed-glyph":
            case "future-write-glyph": resources.Font.Glyphs.Remove(resources.Font.GetGlyph(85)); break;
            case "second-order-write-glyph": resources.Font.Glyphs.Remove(resources.Font.GetGlyph(90)); break;
            case "random-miss-glyph": resources.Font.Glyphs.Remove(resources.Font.GetGlyph(85)); break;
            case "random-target-glyph": resources.Font.Glyphs.Remove(resources.Font.GetGlyph(90)); break;
            case "title-missing-glyph": resources.Font.Glyphs.Remove(resources.Font.GetGlyph(90)); break;
            case "title-duplicate-glyph": resources.Font.Glyphs.Add(resources.Font.GetGlyph(90)); break;
            case "title-bad-stride": resources.Font.GetGlyph(90).RowBytes = 0; break;
            case "missing-used-glyph": resources.Font.Glyphs.Remove(resources.Font.GetGlyph(70)); break;
            case "duplicate-used-glyph": resources.Font.Glyphs.Add(resources.Font.GetGlyph(70)); break;
            case "bad-used-stride": resources.Font.GetGlyph(70).RowBytes = 0; break;
            case "prompt-0": resources.Prompts[0].Width = 2; break;
            case "prompt-1": resources.Prompts[1].Width = 2; break;
            case "prompt-2": resources.Prompts[2].Width = 2; break;
            default: throw new ArgumentOutOfRangeException(nameof(fault));
        }
        (string Resource, string Diagnostic) expected = fault switch
        {
            "hidden-current-glyph" => (resources.Font.ResourcePath, "no glyph for U+0051"),
            "missing-hidden-portrait" or "portrait-only-mapping" => ("", "story.json entry owner28 @09C3: missing bound dialogue portrait 11."),
            "missing-seed-glyph" => (resources.Font.ResourcePath, "no glyph for U+0055"),
            "future-write-glyph" => (resources.Font.ResourcePath, "no glyph for U+0055"),
            "second-order-write-glyph" => (resources.Font.ResourcePath, "no glyph for U+005A"),
            "random-miss-glyph" => (resources.Font.ResourcePath, "no glyph for U+0055"),
            "random-target-glyph" => (resources.Font.ResourcePath, "no glyph for U+005A"),
            "title-missing-glyph" => (resources.Font.ResourcePath, "no glyph for U+005A"),
            "title-duplicate-glyph" => (resources.Font.ResourcePath, "duplicate glyphs for U+005A"),
            "title-bad-stride" => (resources.Font.ResourcePath, "invalid bitmap dimensions"),
            "missing-used-glyph" => (resources.Font.ResourcePath, "no glyph for U+0046"),
            "duplicate-used-glyph" => (resources.Font.ResourcePath, "duplicate glyphs for U+0046"),
            "bad-used-stride" => (resources.Font.ResourcePath, "invalid bitmap dimensions"),
            "prompt-0" => (resources.Prompts[0].ResourcePath, "invalid dimensions or planes"),
            "prompt-1" => (resources.Prompts[1].ResourcePath, "invalid dimensions or planes"),
            "prompt-2" => (resources.Prompts[2].ResourcePath, "invalid dimensions or planes"),
            // The host palette is an array, not a Resource with a diagnostic URI.
            "palette-255" => ("", "Dialogue rendering requires the active 256-color scene palette."),
            "clean-control" => ("", ""),
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };
        var scene = statics.Scene;
        var exploration = NativeExplorationProfile.Load(profile.StaticProfile, scene);
        profile.ValidateStatic(scene, exploration);
        var facade = profile.CreateSession(scene.SceneId, exploration);
        AssertThat(resources.BattleContent.Players.Single(player => player.RoleId == 0).NameObjectId).IsEqual(37);
        AssertThat(resources.BattleContent.Players.Single(player => player.RoleId == 1).NameObjectId).IsEqual(0);
        AssertThat(resources.BattleContent.Players.Single(player => player.RoleId == 0).FriendDeathScript).IsEqual(0xA9B5);
        AssertThat(resources.BattleContent.Players.Single(player => player.RoleId == 1).DyingScript).IsEqual(0xA96E);
        var staticIds = facade.Rules.Objects.Select(obj => (int)obj.Id).ToHashSet();
        var rules = facade.Rules.WithObjects(facade.Rules.Objects
            .Concat(resources.GlobalObjects.Where(obj => !staticIds.Contains(obj.Id))));
        var roles = new RoleLiveState(resources.BattleContent.InitialRoles.Values.ToArray());
        var world = DynamicEventWorld.ForHost(rules, facade.Current.Leader, scene.SceneId,
            scene.WScriptOnEnter, scene.WScriptOnTeleport, roles,
            resources.BattleContent.Players.Select(player => player.RoleId).ToArray());
        byte[] rgb = scene.Palette.Rgb;
        var palette = Enumerable.Range(0, 256).Select(index => new Color(
            rgb[index * 3] / 255f, rgb[index * 3 + 1] / 255f, rgb[index * 3 + 2] / 255f)).ToArray();
        var random = new CountingStoryRandom();
        using var runner = ISceneRunner.Load("res://Tests/DialogueDisplayFixture.tscn", true);
        var display = (PalDialogueDisplay)runner.Scene()!;
        // Keep the background's valid palette separate from the deliberately damaged binding.
        Color[] boundPalette = fault == "palette-255" ? palette[..255] : palette;
        var trigger = new PalTriggerDialogue { FirstBattleWorld = world, PaletteFromScene = boundPalette };
        display.AddChild(trigger);
        int palettes = 0, restores = 0, updates = 0, failures = 0;
        void OnPalette(Color[] _) => palettes++;
        void OnRestore() => restores++;
        DialogueDisplaySnapshot? firstNonTyping = null;
        void OnUpdate()
        {
            updates++;
            // Capture the first non-Typing result, including an incorrect Completed phase.
            if (firstNonTyping is null && display.Current is { Phase: not DialogueDisplayPhase.Typing } sample)
                firstNonTyping = sample;
        }
        void OnFailure(Exception _) => failures++;
        display.ActivePaletteChanged += OnPalette;
        display.BackgroundRestoreRequested += OnRestore;
        display.UpdateCompleted += OnUpdate;
        display.Failed += OnFailure;
        try
        {
            trigger.Initialize(display, resources, "en", random, scene);
            var session = trigger.Session!;
            AssertThat(facade.InitializeTrigger(session)).IsTrue();
            AssertThat(session.CallId).IsEqual(Guid.Empty);
            AssertThat(world.ObjectScripts.Count).IsEqual(0);
            if (!seedGlyph)
            {
                var setter = new TriggerScriptSession(resources.Events, resources.English, world);
                setter.Start("story", "setter", 28);
                AssertThat(setter.Phase).IsEqual(TriggerScriptPhase.Completed);
                AssertThat(world.ObjectScripts.Count).IsEqual(1);
                AssertThat(world.ObjectScripts[(0, 1)]).IsEqual(futureWrite ? 0 : 20);
            }
            AssertThat(world.ObjectScripts.ContainsKey((37, 0))).IsFalse();
            facade.PressSearch();
            AssertThat(facade.Update(0)).IsTrue();
            var request = facade.PendingTrigger
                ?? throw new InvalidOperationException("The authored owner28 geometry did not produce a search request.");
            AssertThat(request.OwnerId).IsEqual(28);
            AssertThat(request.EntryAddress).IsEqual(0x09C3);
            AssertThat(facade.PendingRequestId == Guid.Empty).IsFalse();
            var requestId = facade.PendingRequestId;
            AssertThat(session.CallId).IsEqual(Guid.Empty);
            var surface = display.GetNode<PalDialoguePaletteSurface>("IndexedBackground");
            surface.SetPalette(palette);
            surface.SetImage(ResourceLoader.Load<PalDialogueIndexedImage>("res://Tests/Fixtures/DialogueSyntheticBackground.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep));

            if (fault == "clean-control")
            {
                // A separate clean fresh-load resource control, not a battle/hook/return receipt.
                trigger.Start("story", "owner28", request.OwnerId, facade.PendingRequestId);
                AssertThat(facade.BindTrigger(facade.PendingRequestId, session)).IsTrue();
                AssertThat(session.SourceRequestId).IsEqual(facade.PendingRequestId);
                AssertThat(session.Caller).IsEqual(28);
                AssertThat(session.ReturnEntry).IsEqual(0x09C3);
                var pending = session.PendingDisplay!;
                AssertThat(pending.MessageReferences[0].StartMessageId).IsEqual(1);
                AssertThat(ReferenceEquals(display.Session, pending.Display)).IsTrue();
                await Until(runner, () => firstNonTyping is not null || failures != 0);
                AssertThat(failures).IsEqual(0);
                AssertThat(firstNonTyping!.Phase).IsEqual(DialogueDisplayPhase.WaitingForAdvance);
                AssertThat(firstNonTyping.VisibleText).IsEqual("FIGHT");
                AssertThat(session.Phase).IsEqual(TriggerScriptPhase.WaitingForDisplay);
                AssertThat(ReferenceEquals(session.PendingDisplay, pending)).IsTrue();
                AssertThat(ReferenceEquals(display.Session, pending.Display)).IsTrue();
                AssertThat(ReferenceEquals(facade.PendingTrigger, request)).IsTrue();
                AssertThat(world.ObjectScripts.Count).IsEqual(4);
                AssertThat(world.ObjectScripts[(0, 1)]).IsEqual(20);
                AssertThat(world.ObjectScripts[(0, 0)]).IsEqual(0);
                AssertThat(world.ObjectScripts[(37, 0)]).IsEqual(0xA9B5);
                AssertThat(world.ObjectScripts[(37, 1)]).IsEqual(0);
                AssertThat((random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws).Equals((0, 0, 0, 0))).IsTrue();
                return;
            }

            object SessionState() => (session.Phase, session.BlocksSceneUpdates, session.CallId,
                session.SourceRequestId, session.StartedEntry, session.Caller, session.ReturnEntry,
                session.CurrentPosition, session.Context, session.ScriptSuccess, session.Music, session.FadedToRed,
                session.PendingDisplay, session.PendingBattle, session.PendingLostReload, session.PendingSceneWait,
                session.PendingMovement, session.PendingTransition, session.PendingBattleHook,
                session.RunningBattleHook, session.RunningBattleEndScript, session.BattleRoles);
            object ObjectState(DynamicEventObject obj) => (obj.Id, obj.X, obj.Y, obj.Direction, obj.CurrentFrame,
                obj.State, obj.VanishTime, obj.TriggerMode, obj.TriggerEntry, obj.AutoEntry, obj.AutoIdleCount);
            var beforeSession = SessionState();
            var beforeContext = session.Context;
            var beforeBank = world.ObjectScripts.OrderBy(pair => pair.Key).ToArray();
            var beforeScenes = world.SceneScripts.OrderBy(pair => pair.Key).ToArray();
            var beforeWorld = (world.SceneId, world.Leader, world.Battlefield);
            var beforeParty = world.PartyRoles.ToArray();
            var beforePartyReference = world.PartyRoles;
            var beforeObjectsReference = world.Objects;
            var beforeObjects = world.Objects.ToArray();
            var beforeObjectWords = world.Objects.Select(ObjectState).ToArray();
            var beforeVitals = world.PartyVitals.OrderBy(pair => pair.Key).ToArray();
            var beforeVitalWords = beforeVitals.Select(pair => (pair.Key, pair.Value.Hp, pair.Value.MaxHp, pair.Value.Mp, pair.Value.MaxMp)).ToArray();
            var beforeRoles = roles.Roles.OrderBy(pair => pair.Key).ToArray();
            string beforeRoleWords = System.Text.Json.JsonSerializer.Serialize(roles.Roles);
            var beforeBattleRoles = session.BattleRoles!.OrderBy(pair => pair.Key).ToArray();
            var beforeDisplay = display.Session;
            var beforeCurrent = display.Current;
            var beforeChildren = display.GetChildren().ToArray();
            var beforeOwned = trigger.GetChildren().ToArray();
            var beforePixel = surface.GetRenderedPixel(0, 0);
            var beforeEvents = (palettes, restores, updates, failures);
            var beforeRandom = (random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws);
            var beforeFacade = (facade.State, facade.Current, facade.Objects, facade.PendingTrigger, facade.PendingRequestId, facade.Rules);
            var beforeFacadeObjects = facade.Objects.ToArray();
            var beforeFacadeRoles = facade.State.Roles.ToArray();
            var beforeFacadeSceneEntries = facade.State.World.SceneEntries.ToArray();
            var beforeFacadeEventObjects = facade.State.World.EventObjects.ToArray();
            string beforeFacadeState = System.Text.Json.JsonSerializer.Serialize(facade.State);

            if (seedGlyph || futureWrite)
            {
                AssertThat(world.ObjectScripts.Count).IsEqual(seedGlyph ? 0 : 1);
                if (futureWrite) AssertThat(world.ObjectScripts[(0, 1)]).IsEqual(0);
                AssertThat((random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws).Equals((0, 0, 0, 0))).IsTrue();
            }
            Color[]? beforeInvalidPalette = fault == "palette-255" ? boundPalette.ToArray() : null;
            string error = Failure(() => trigger.Start("story", "owner28", request.OwnerId, facade.PendingRequestId));
            if (beforeInvalidPalette is not null)
            {
                AssertThat(palette.Length).IsEqual(256);
                AssertThat(boundPalette.Length).IsEqual(255);
                AssertThat(boundPalette.SequenceEqual(beforeInvalidPalette)).IsTrue();
            }
            else AssertThat(error.Contains(expected.Resource, StringComparison.Ordinal)).IsTrue();
            AssertThat(error.Contains(expected.Diagnostic, StringComparison.Ordinal)).IsTrue();
            if (fault is not ("missing-hidden-portrait" or "portrait-only-mapping"))
                AssertThat(error.Contains(hidden ? "story.json#L0015 @0015" : seedGlyph
                ? "story.json#LA96F @A96F" : fault == "random-miss-glyph" ? "story.json#L001A @001A"
                : secondOrderWrite || fault == "random-target-glyph" ? "story.json#L0021 @0021"
                : futureWrite ? "story.json#L0019 @0019"
                : "story.json#L09C4 @09C4", StringComparison.Ordinal)).IsTrue();
            AssertThat(ReferenceEquals(trigger.Session, session)).IsTrue();
            AssertThat(Equals(SessionState(), beforeSession)).IsTrue();
            AssertThat(ReferenceEquals(session.Context, beforeContext)).IsTrue();
            AssertThat(world.ObjectScripts.OrderBy(pair => pair.Key).SequenceEqual(beforeBank)).IsTrue();
            AssertThat(world.ObjectScripts.ContainsKey((37, 0))).IsFalse();
            AssertThat(world.SceneScripts.OrderBy(pair => pair.Key).SequenceEqual(beforeScenes)).IsTrue();
            AssertThat((world.SceneId, world.Leader, world.Battlefield).Equals(beforeWorld)).IsTrue();
            AssertThat(ReferenceEquals(world.Leader, beforeWorld.Leader)).IsTrue();
            AssertThat(ReferenceEquals(world.RoleLiveState, roles)).IsTrue();
            AssertThat(ReferenceEquals(world.PartyRoles, beforePartyReference)).IsTrue();
            AssertThat(world.PartyRoles.SequenceEqual(beforeParty)).IsTrue();
            AssertThat(ReferenceEquals(world.Objects, beforeObjectsReference)).IsTrue();
            AssertThat(world.Objects.SequenceEqual(beforeObjects)).IsTrue();
            AssertThat(world.Objects.Select(ObjectState).SequenceEqual(beforeObjectWords)).IsTrue();
            AssertThat(world.PartyVitals.OrderBy(pair => pair.Key).SequenceEqual(beforeVitals)).IsTrue();
            AssertThat(world.PartyVitals.OrderBy(pair => pair.Key)
                .Select(pair => (pair.Key, pair.Value.Hp, pair.Value.MaxHp, pair.Value.Mp, pair.Value.MaxMp)).SequenceEqual(beforeVitalWords)).IsTrue();
            AssertThat(roles.Roles.Count).IsEqual(beforeRoles.Length);
            AssertThat(System.Text.Json.JsonSerializer.Serialize(roles.Roles)).IsEqual(beforeRoleWords);
            foreach (var pair in beforeRoles) AssertThat(ReferenceEquals(roles.Roles[pair.Key], pair.Value)).IsTrue();
            AssertThat(session.BattleRoles!.Count).IsEqual(beforeBattleRoles.Length);
            foreach (var pair in beforeBattleRoles) AssertThat(ReferenceEquals(session.BattleRoles[pair.Key], pair.Value)).IsTrue();
            AssertThat(ReferenceEquals(display.Session, beforeDisplay)).IsTrue();
            AssertThat(ReferenceEquals(display.Current, beforeCurrent)).IsTrue();
            AssertThat(display.GetChildren().SequenceEqual(beforeChildren)).IsTrue();
            AssertThat(trigger.GetChildren().SequenceEqual(beforeOwned)).IsTrue();
            AssertThat(surface.GetRenderedPixel(0, 0)).IsEqual(beforePixel);
            AssertThat((palettes, restores, updates, failures).Equals(beforeEvents)).IsTrue();
            AssertThat((random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws).Equals(beforeRandom)).IsTrue();
            AssertThat((facade.State, facade.Current, facade.Objects, facade.PendingTrigger, facade.PendingRequestId, facade.Rules).Equals(beforeFacade)).IsTrue();
            AssertThat(ReferenceEquals(facade.Current, beforeFacade.Current)).IsTrue();
            AssertThat(ReferenceEquals(facade.Objects, beforeFacade.Objects)).IsTrue();
            AssertThat(ReferenceEquals(facade.PendingTrigger, request)).IsTrue();
            AssertThat(facade.Objects.SequenceEqual(beforeFacadeObjects)).IsTrue();
            AssertThat(System.Text.Json.JsonSerializer.Serialize(facade.State)).IsEqual(beforeFacadeState);
            for (int index = 0; index < beforeFacadeRoles.Length; index++)
                AssertThat(ReferenceEquals(facade.State.Roles[index], beforeFacadeRoles[index])).IsTrue();
            for (int index = 0; index < beforeFacadeSceneEntries.Length; index++)
                AssertThat(ReferenceEquals(facade.State.World.SceneEntries[index], beforeFacadeSceneEntries[index])).IsTrue();
            for (int index = 0; index < beforeFacadeEventObjects.Length; index++)
                AssertThat(ReferenceEquals(facade.State.World.EventObjects[index], beforeFacadeEventObjects[index])).IsTrue();
            if (hidden)
            {
                var CapturePriority = () => (
                    Call: SessionState(),
                    World: (world.Leader, world.RoleLiveState, world.PartyRoles, world.Objects),
                    Words: System.Text.Json.JsonSerializer.Serialize(new { world.SceneId, world.Battlefield,
                        world.Objects, world.PartyRoles, world.PartyVitals, Roles = roles.Roles, BattleRoles = session.BattleRoles,
                        Bank = world.ObjectScripts.OrderBy(pair => pair.Key).Select(pair => new { Key = pair.Key.ToString(), pair.Value }),
                        Scenes = world.SceneScripts.OrderBy(pair => pair.Key).Select(pair => new { Key = pair.Key.ToString(), pair.Value }) }),
                    Roles: (roles.Roles[0], roles.Roles[1], session.BattleRoles![0], session.BattleRoles![1]),
                    Display: (display.Session, display.Current),
                    Nodes: string.Join(",", display.GetChildren().Concat(trigger.GetChildren()).Select(node => node.GetInstanceId())),
                    Pixel: surface.GetRenderedPixel(0, 0), Events: (palettes, restores, updates, failures),
                    Random: (random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws),
                    Producer: (facade.PendingTrigger, facade.PendingRequestId, facade.Current, facade.Objects, facade.Rules),
                    ProducerWords: System.Text.Json.JsonSerializer.Serialize(facade.State));
                var uninitialized = new PalTriggerDialogue();
                try
                {
                    AssertStartRejection(CapturePriority, () => uninitialized.Start("story", "missing-entry", 65536, Guid.Empty),
                        typeof(ArgumentException), "A source request ID must be nonempty.");
                    AssertStartRejection(CapturePriority, () => uninitialized.Start("story", "missing-entry", 65536, requestId),
                        typeof(InvalidOperationException), "Initialize trigger dialogue before starting an entry.");
                    AssertThat(uninitialized.Session is null).IsTrue();
                }
                finally { uninitialized.Free(); }
                AssertStartRejection(CapturePriority, () => trigger.Start("story", "setter", 65536, Guid.Empty),
                    typeof(ArgumentException), "A source request ID must be nonempty.");
                AssertStartRejection(CapturePriority, () => trigger.Start("story", "setter", 65536, requestId),
                    typeof(InvalidDataException), "owner eventObject:28/wTriggerScript does not match the bound native scene.");
                AssertStartRejection(CapturePriority, () => trigger.Start("story", "owner28", 65536, requestId),
                    typeof(ArgumentOutOfRangeException), "caller");
                var busyLanguage = ResourceLoader.Load<PalDialogueLanguage>(resources.ResourceSha256.Keys.Single(
                    uri => uri.EndsWith("/Language.en.tres", StringComparison.Ordinal)), cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
                display.Start(busyLanguage, resources.Font,
                    ResourceLoader.Load<PalDialogueIndexedImage>("res://Tests/Fixtures/DialogueSyntheticBackground.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep),
                    resources.Prompts.ToArray(), palette, 1, 0, 0, 0, 0);
                AssertThat(display.Current!.Phase).IsEqual(DialogueDisplayPhase.Typing);
                AssertStartRejection(CapturePriority, () => trigger.Start("story", "missing-entry", 65536, requestId),
                    typeof(InvalidOperationException), "The display is already in use.");
                AssertStartRejection(CapturePriority, () => trigger.Start("story", "owner28", 65536, requestId),
                    typeof(InvalidOperationException), "The display is already in use.");
                AssertStartRejection(CapturePriority, () => trigger.Start("story", "setter", 65536, Guid.Empty),
                    typeof(ArgumentException), "A source request ID must be nonempty.");
            }
        }
        finally
        {
            display.ActivePaletteChanged -= OnPalette;
            display.BackgroundRestoreRequested -= OnRestore;
            display.UpdateCompleted -= OnUpdate;
            display.Failed -= OnFailure;
            var ownedNodes = display.GetChildren().Concat(trigger.GetChildren()).ToArray();
            runner.Dispose();
            AssertThat(GodotObject.IsInstanceValid(display)).IsFalse();
            AssertThat(GodotObject.IsInstanceValid(trigger)).IsFalse();
            foreach (var node in ownedNodes) AssertThat(GodotObject.IsInstanceValid(node)).IsFalse();
        }
    }

    [TestCase("no-portrait", Timeout = 15000)]
    [TestCase("valid-portrait", Timeout = 15000)]
    [TestCase("bad-portrait", Timeout = 15000)]
    public void First_battle_zero_display_ignores_unused_resources_but_validates_bound_portrait(string mode)
    {
        int portraitId = mode == "no-portrait" ? 0 : 7;
        using var battleFixture = new NativeBattleProfileFixture();
        var statics = battleFixture.Static;
        foreach (var frame in statics.MapScene.Data.Map.Tiles.Frames)
        {
            frame.Indices = Enumerable.Repeat((byte)0, frame.Width * frame.Height).ToArray();
            frame.Alpha = Enumerable.Repeat((byte)255, frame.Width * frame.Height).ToArray();
        }
        foreach (var sprite in statics.MapScene.Data.Sprites)
            foreach (var frame in sprite.Frames)
            {
                frame.Indices = Enumerable.Repeat((byte)0, frame.Width * frame.Height).ToArray();
                frame.Alpha = Enumerable.Repeat((byte)255, frame.Width * frame.Height).ToArray();
            }
        statics.MapScene.Data.Palette = ResourceLoader.Load<PalPalette>(
            "res://Samples/AuthoredBattle/Static/Palette.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep)!;
        statics.SavePacked();
        string staticProfile = statics.ResourceRoot + "/Profile.tres";
        NativeProfileFixture.SaveResource(statics.Profile, staticProfile);
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(statics.ResourceRoot + "/Palettes"));
        File.Copy(ProjectSettings.GlobalizePath("res://Samples/AuthoredBattle/Static/Palette.tres"),
            ProjectSettings.GlobalizePath(statics.ResourceRoot + "/Palettes/Palette.tres"));
        using var files = new SyntheticFirstBattleFixture(statics, statics.ResourceRoot, staticProfile,
            lost: false, currentHookPositive: true, currentHookPortrait: portraitId, zeroDisplay: true);
        var profile = NativeFirstMandatoryBattleProfile.Load(files.ProfileUri);
        var resources = profile.Resources ?? throw new InvalidOperationException("Missing strict zero-display closure.");
        AssertThat(resources.BattleContent.Players.Single(player => player.RoleId == 0).FriendDeathScript).IsEqual(0xA9B5);
        AssertThat(resources.BattleContent.Players.Single(player => player.RoleId == 1).DyingScript).IsEqual(0xA96E);
        AssertThat(resources.Portraits.Keys.SequenceEqual(portraitId == 0 ? Array.Empty<int>() : new[] { 7 })).IsTrue();
        var scene = statics.Scene;
        var exploration = NativeExplorationProfile.Load(profile.StaticProfile, scene);
        profile.ValidateStatic(scene, exploration);
        var facade = profile.CreateSession(scene.SceneId, exploration);
        var ids = facade.Rules.Objects.Select(obj => (int)obj.Id).ToHashSet();
        var rules = facade.Rules.WithObjects(facade.Rules.Objects.Concat(resources.GlobalObjects.Where(obj => !ids.Contains(obj.Id))));
        var roles = new RoleLiveState(resources.BattleContent.InitialRoles.Values.ToArray());
        var world = DynamicEventWorld.ForHost(rules, facade.Current.Leader, scene.SceneId,
            scene.WScriptOnEnter, scene.WScriptOnTeleport, roles, [0, 1]);
        byte[] rgb = scene.Palette.Rgb;
        var palette = Enumerable.Range(0, 256).Select(index => new Color(
            rgb[index * 3] / 255f, rgb[index * 3 + 1] / 255f, rgb[index * 3 + 2] / 255f)).ToArray();
        var random = new CountingStoryRandom();
        using var runner = ISceneRunner.Load("res://Tests/DialogueDisplayFixture.tscn", true);
        var display = (PalDialogueDisplay)runner.Scene()!;
        // The no-portrait control also has a genuinely invalid bound255 palette. Never displayed.
        var trigger = new PalTriggerDialogue { FirstBattleWorld = world, PaletteFromScene = portraitId == 0 ? palette[..255] : palette };
        display.AddChild(trigger);
        trigger.Initialize(display, resources, "en", random, scene);
        var session = trigger.Session!;
        AssertThat(facade.InitializeTrigger(session)).IsTrue();
        var setter = new TriggerScriptSession(resources.Events, resources.English, world);
        setter.Start("story", "setter", 28);
        AssertThat(setter.Phase).IsEqual(TriggerScriptPhase.Completed);
        AssertThat(world.ObjectScripts.Count).IsEqual(1);
        AssertThat(world.ObjectScripts[(0, 1)]).IsEqual(20);
        facade.PressSearch();
        AssertThat(facade.Update(0)).IsTrue();
        var request = facade.PendingTrigger ?? throw new InvalidOperationException("Missing genuine zero-display owner28 request.");
        Guid origin = facade.PendingRequestId;
        AssertThat(origin == Guid.Empty).IsFalse();
        AssertThat(request.OwnerId).IsEqual(28);
        AssertThat(request.EntryAddress).IsEqual(0x09C3);
        // Controlled damage only after strict bilingual Load/Initialize, never shared/retail files.
        resources.Font.FormatVersion = 0;
        foreach (var prompt in resources.Prompts) prompt.Alpha = [];
        if (mode == "bad-portrait") resources.Portraits[7].Alpha = [];
        var surface = display.GetNode<PalDialoguePaletteSurface>("IndexedBackground");
        surface.SetPalette(palette);
        surface.SetImage(ResourceLoader.Load<PalDialogueIndexedImage>("res://Tests/Fixtures/DialogueSyntheticBackground.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep));
        int updates = 0, palettes = 0, restores = 0, failures = 0;
        void OnUpdate() => updates++;
        void OnPalette(Color[] _) => palettes++;
        void OnRestore() => restores++;
        void OnFailure(Exception _) => failures++;
        display.UpdateCompleted += OnUpdate;
        display.ActivePaletteChanged += OnPalette;
        display.BackgroundRestoreRequested += OnRestore;
        display.Failed += OnFailure;
        try
        {
            var Capture = () => (
                Call: (session.Phase, session.BlocksSceneUpdates, session.CallId, session.SourceRequestId,
                    session.StartedEntry, session.Caller, session.ReturnEntry, session.CurrentPosition, session.Context,
                    session.ScriptSuccess, session.Music, session.FadedToRed, session.PendingDisplay, session.PendingBattle,
                    session.PendingLostReload, session.PendingSceneWait, session.PendingMovement, session.PendingTransition,
                    session.PendingBattleHook, session.RunningBattleHook, session.RunningBattleEndScript, session.BattleRoles),
                World: (world.SceneId, world.Leader, world.Battlefield, world.RoleLiveState, world.PartyRoles, world.Objects),
                Words: System.Text.Json.JsonSerializer.Serialize(new { world.Objects, world.PartyRoles, world.PartyVitals,
                    Roles = roles.Roles, Bank = world.ObjectScripts.OrderBy(pair => pair.Key).Select(pair => new { Key = pair.Key.ToString(), pair.Value }),
                    Scenes = world.SceneScripts.OrderBy(pair => pair.Key).Select(pair => new { Key = pair.Key.ToString(), pair.Value }) }),
                Roles: (roles.Roles[0], roles.Roles[1]), Display: (display.Session, display.Current),
                Nodes: string.Join(",", display.GetChildren().Concat(trigger.GetChildren()).Select(node => node.GetInstanceId())),
                Pixel: surface.GetRenderedPixel(0, 0), Events: (updates, palettes, restores, failures),
                Producer: (facade.PendingTrigger, facade.PendingRequestId, facade.Current, facade.Objects, facade.Rules),
                ProducerWords: System.Text.Json.JsonSerializer.Serialize(facade.State),
                Random: (random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws));
            if (mode == "bad-portrait")
            {
                AssertStartRejection(Capture, () => trigger.Start("story", "owner28", request.OwnerId, origin),
                    typeof(InvalidDataException), resources.Portraits[7].ResourcePath + ": Dialogue indexed image has invalid dimensions or planes.");
                AssertThat(world.ObjectScripts.Count).IsEqual(1); // Rejection did not seed other Name/slot keys.
            }
            else
            {
                trigger.Start("story", "owner28", request.OwnerId, origin);
                AssertThat(session.Phase).IsEqual(TriggerScriptPhase.WaitingForBattle);
                AssertThat(session.SourceRequestId).IsEqual(origin);
                AssertThat(session.Caller).IsEqual(28);
                AssertThat(session.CallId == Guid.Empty).IsFalse();
                AssertThat(session.PendingDisplay is null && display.Session is null && display.Current is null).IsTrue();
                AssertThat(world.ObjectScripts.Count).IsEqual(4);
                AssertThat(world.ObjectScripts[(0, 0)]).IsEqual(0);
                AssertThat(world.ObjectScripts[(0, 1)]).IsEqual(20);
                AssertThat(world.ObjectScripts[(37, 0)]).IsEqual(0xA9B5);
                AssertThat(world.ObjectScripts[(37, 1)]).IsEqual(0);
                AssertThat(facade.BindTrigger(origin, session)).IsTrue();
                var battle = session.PendingBattle ?? throw new InvalidOperationException("Zero-display graph must preserve its real battle.");
                AssertThat(battle.Lifecycle.Current.Combat.Enemies.Count).IsEqual(2);
                AssertThat(battle.Lifecycle.Current.Roles.Count).IsEqual(2);
                AssertThat(battle.Lifecycle.Completion is null).IsTrue();
                var before = Capture();
                AssertThat(facade.CompleteTrigger(origin, session, 0)).IsFalse();
                AssertThat(trigger.AcknowledgeBattle(battle.Id, null)).IsFalse();
                AssertThat(Capture().Equals(before)).IsTrue();
            }
            AssertThat((updates, palettes, restores, failures).Equals((0, 0, 0, 0))).IsTrue();
            AssertThat((random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws).Equals((0, 0, 0, 0))).IsTrue();
        }
        finally
        {
            display.UpdateCompleted -= OnUpdate;
            display.ActivePaletteChanged -= OnPalette;
            display.BackgroundRestoreRequested -= OnRestore;
            display.Failed -= OnFailure;
            var owned = display.GetChildren().Concat(trigger.GetChildren()).ToArray();
            runner.Dispose();
            AssertThat(GodotObject.IsInstanceValid(display)).IsFalse();
            AssertThat(GodotObject.IsInstanceValid(trigger)).IsFalse();
            foreach (var node in owned) AssertThat(GodotObject.IsInstanceValid(node)).IsFalse();
        }
    }

    [TestCase(Timeout = 15000)]
    public async Task First_battle_current_world_hook_uses_live_entry_and_owned_display_ack() =>
        await AssertFirstBattleCurrentHookReturn("live20");

    [TestCase("absent-seed", Timeout = 15000)]
    [TestCase("live-zero", Timeout = 15000)]
    public async Task First_battle_declared_seed_and_live_zero_reach_owned_outer_return(string bankMode) =>
        await AssertFirstBattleCurrentHookReturn(bankMode);

    [TestCase(Timeout = 15000)]
    public async Task First_battle_future_literal_0090_reaches_owned_outer_return() =>
        await AssertFirstBattleCurrentHookReturn("future-write");

    [TestCase(Timeout = 15000)]
    public async Task First_battle_second_order_literal_0090_reaches_owned_outer_return() =>
        await AssertFirstBattleCurrentHookReturn("second-order-write");

    [TestCase(Timeout = 15000)]
    public async Task First_battle_second_order_0090_executes_real_coverer_friend_death_and_owned_ack() =>
        await AssertFirstBattleCurrentHookReturn("friend-death");

    [TestCase(Timeout = 15000)]
    public async Task First_battle_random_hook_target_reaches_owned_outer_return() =>
        await AssertFirstBattleCurrentHookReturn("random-target");

    [TestCase(Timeout = 15000)]
    public async Task First_battle_title_and_body_reach_owned_outer_return() =>
        await AssertFirstBattleCurrentHookReturn("title");

    [TestCase(Timeout = 15000)]
    public async Task First_battle_completed_warmup_preserves_rejection_priority_and_caller() =>
        await AssertFirstBattleCurrentHookReturn("warmup");

    private async Task AssertFirstBattleCurrentHookReturn(string bankMode)
    {
        int dyingAddress = bankMode switch
        {
            "live20" => 20,
            "absent-seed" => 0xA96E,
            "live-zero" => 0,
            "future-write" or "second-order-write" or "random-target" or "title" or "warmup" or "friend-death" => 24,
            _ => throw new ArgumentOutOfRangeException(nameof(bankMode))
        };
        bool seedVariant = bankMode is "absent-seed" or "live-zero";
        bool liveZero = bankMode == "live-zero";
        bool friendDeathControl = bankMode == "friend-death";
        bool secondOrderWrite = friendDeathControl || bankMode == "second-order-write";
        bool randomBranch = bankMode == "random-target";
        bool titleGlyph = bankMode == "title";
        bool futureWrite = titleGlyph || randomBranch || secondOrderWrite || bankMode is "future-write" or "warmup";
        int hookInts = randomBranch ? 6 : 5;
        int finalInts = randomBranch ? 13 : 12;
        using var battleFixture = new NativeBattleProfileFixture();
        var statics = battleFixture.Static;
        foreach (var frame in statics.MapScene.Data.Map.Tiles.Frames)
        {
            frame.Indices = Enumerable.Repeat((byte)0, frame.Width * frame.Height).ToArray();
            frame.Alpha = Enumerable.Repeat((byte)255, frame.Width * frame.Height).ToArray();
        }
        foreach (var sprite in statics.MapScene.Data.Sprites)
            foreach (var frame in sprite.Frames)
            {
                frame.Indices = Enumerable.Repeat((byte)0, frame.Width * frame.Height).ToArray();
                frame.Alpha = Enumerable.Repeat((byte)255, frame.Width * frame.Height).ToArray();
            }
        statics.MapScene.Data.Palette = ResourceLoader.Load<PalPalette>(
            "res://Samples/AuthoredBattle/Static/Palette.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep)!;
        statics.SavePacked();
        string staticProfile = statics.ResourceRoot + "/Profile.tres";
        NativeProfileFixture.SaveResource(statics.Profile, staticProfile);
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(statics.ResourceRoot + "/Palettes"));
        File.Copy(ProjectSettings.GlobalizePath("res://Samples/AuthoredBattle/Static/Palette.tres"),
            ProjectSettings.GlobalizePath(statics.ResourceRoot + "/Palettes/Palette.tres"));
        using var files = new SyntheticFirstBattleFixture(statics, statics.ResourceRoot, staticProfile,
            lost: false, currentHookPositive: true, forceCurrentDying: true,
            seedDyingGlyph: seedVariant, liveZeroDying: liveZero, futureDyingWrite: futureWrite,
            secondOrderDyingWrite: secondOrderWrite, randomDyingBranch: randomBranch, titleDyingGlyph: titleGlyph,
            forceCurrentFriendDeath: friendDeathControl);
        var profile = NativeFirstMandatoryBattleProfile.Load(files.ProfileUri);
        var resources = profile.Resources ?? throw new InvalidOperationException("Missing synthetic real closure.");
        // Strict Load checked both languages first. Only this fresh live0 binding then loses U.
        if (liveZero) resources.Font.Glyphs.Remove(resources.Font.GetGlyph(85));
        AssertThat(resources.BattleContent.PanelDeclaredRoles.Count).IsEqual(0);
        AssertThat(resources.BattleContent.Players.Single(player => player.RoleId == 0).NameObjectId).IsEqual(37);
        AssertThat(resources.BattleContent.Players.Single(player => player.RoleId == 1).NameObjectId).IsEqual(0);
        AssertThat(resources.BattleContent.Players.Single(player => player.RoleId == 0).DyingScript).IsEqual(0);
        AssertThat(resources.BattleContent.Players.Single(player => player.RoleId == 0).FriendDeathScript).IsEqual(0xA9B5);
        AssertThat(resources.BattleContent.Players.Single(player => player.RoleId == 1).DyingScript).IsEqual(0xA96E);
        AssertThat(resources.BattleContent.Players.Single(player => player.RoleId == 1).FriendDeathScript).IsEqual(0);
        var scene = statics.Scene;
        var exploration = NativeExplorationProfile.Load(profile.StaticProfile, scene);
        profile.ValidateStatic(scene, exploration);
        var facade = profile.CreateSession(scene.SceneId, exploration);
        var staticIds = facade.Rules.Objects.Select(obj => (int)obj.Id).ToHashSet();
        var rules = facade.Rules.WithObjects(facade.Rules.Objects
            .Concat(resources.GlobalObjects.Where(obj => !staticIds.Contains(obj.Id))));
        var roles = new RoleLiveState(resources.BattleContent.InitialRoles.Values.ToArray());
        var world = DynamicEventWorld.ForHost(rules, facade.Current.Leader, scene.SceneId,
            scene.WScriptOnEnter, scene.WScriptOnTeleport, roles,
            resources.BattleContent.Players.Select(player => player.RoleId).ToArray());
        byte[] rgb = scene.Palette.Rgb;
        var palette = Enumerable.Range(0, 256).Select(index => new Color(
            rgb[index * 3] / 255f, rgb[index * 3 + 1] / 255f, rgb[index * 3 + 2] / 255f)).ToArray();
        // Reference fight.c: four initiative draws, enemy hit, then slot0/slot1 strikes.
        // Player1's damage is7 (20 versus defense24); half-missing recovery has NO draw.
        var random = new CurrentHookPositiveRandom(friendDeathControl ? [
            // Four real initiative draws, followed by two independently authored enemy hits.
            // Both evade rolls are0: no physical interception; coverer still owns FriendDeath.
            (true, .9f, 1.1f, 1), (true, .9f, 1.1f, 1), (true, .9f, 1.1f, 1), (true, .9f, 1.1f, 1),
            (false, 0, 1, 1), (false, 0, 16, 0), (false, 0, 2, 0), (false, 0, 1, 0), (false, 1, 10, 1),
            (false, 0, 1, 1), (false, 0, 16, 0), (false, 0, 2, 0), (false, 0, 1, 0), (false, 1, 10, 1)
        ] : [
            (true, .9f, 1.1f, 1), (true, .9f, 1.1f, 1), (true, .9f, 1.1f, 1), (true, .9f, 1.1f, 1),
            (false, 0, 1, 1), (false, 0, 16, 0), (false, 0, 2, 0), (false, 0, 1, 0), (false, 1, 10, 1),
            (false, 1, 2, 1), (false, 0, 5, 1), (false, 0, 11, 11), (true, 1, 1.125f, 1), (false, 2, 3, 2),
            (false, 1, 2, 1), (false, 0, 5, 1), (true, 1, 1.125f, 1), (false, 2, 3, 2)
        ], randomTarget: randomBranch);
        using var runner = ISceneRunner.Load("res://Tests/DialogueDisplayFixture.tscn", true);
        var display = (PalDialogueDisplay)runner.Scene()!;
        var trigger = new PalTriggerDialogue { FirstBattleWorld = world, PaletteFromScene = palette };
        display.AddChild(trigger);
        DialogueDisplaySnapshot? updated = null;
        Exception? failure = null;
        string? waitViolation = null;
        Action<bool>? armed = null;
        DialogueDisplaySession? firstNonTypingSession = null;
        DialogueDisplaySnapshot? firstNonTypingSnapshot = null;
        DialogueDisplaySession? previousSession = null;
        DialogueDisplayPhase? previousPhase = null;
        uint waitRemainder = 0;
        long updateSequence = 0;
        string updateDiagnostic = "";
        int palettes = 0, restores = 0;
        void OnPalette(Color[] _) => palettes++;
        void OnRestore() => restores++;
        void OnFailure(Exception error) => failure ??= error;
        void SendOwnedKey(bool pressed)
        {
            using var input = new InputEventKey { Keycode = Godot.Key.Space, Pressed = pressed, Echo = false };
            Input.ParseInputEvent(input);
            Input.FlushBufferedEvents();
        }
        void OnUpdate()
        {
            updated = display.Current;
            if (!ReferenceEquals(firstNonTypingSession, display.Session))
            {
                firstNonTypingSession = display.Session;
                firstNonTypingSnapshot = null;
            }
            if (firstNonTypingSnapshot is null && updated is { Phase: not DialogueDisplayPhase.Typing })
                firstNonTypingSnapshot = updated;
            double delta = display.GetProcessDeltaTime();
            uint elapsed = (uint)Math.Clamp((long)Math.Round(delta * 1000.0), 0L, uint.MaxValue);
            uint before = waitRemainder;
            bool poll = false;
            if (ReferenceEquals(previousSession, display.Session) && previousPhase == DialogueDisplayPhase.WaitingForAdvance)
            {
                ulong waiting = (ulong)waitRemainder + elapsed;
                poll = waiting >= 100;
                waitRemainder = (uint)(waiting % 100);
            }
            else waitRemainder = 0;
            previousSession = display.Session;
            previousPhase = updated?.Phase;
            updateSequence++;
            updateDiagnostic = $"seq={updateSequence},frame={Engine.GetProcessFrames()},delta={delta:R},ms={elapsed},remainder={before}->{waitRemainder},poll={poll},phase={updated?.Phase}";
            armed?.Invoke(poll);
        }
        void AssertNoFailure()
        {
            if (failure is not null) throw new InvalidOperationException("First-battle positive display failed.", failure);
            if (waitViolation is not null) throw new InvalidOperationException(waitViolation);
        }
        try
        {
            trigger.Initialize(display, resources, "en", random, scene);
            // The real receipt consumer is subscribed BEFORE the observer.
            display.UpdateCompleted += OnUpdate;
            display.Failed += OnFailure;
            display.ActivePaletteChanged += OnPalette;
            display.BackgroundRestoreRequested += OnRestore;
            var session = trigger.Session!;
            AssertThat(facade.InitializeTrigger(session)).IsTrue();
            AssertThat(world.ObjectScripts.Count).IsEqual(0);
            if (bankMode != "absent-seed")
            {
                var setter = new TriggerScriptSession(resources.Events, resources.English, world);
                setter.Start("story", "setter", 28);
                AssertThat(setter.Phase).IsEqual(TriggerScriptPhase.Completed);
                AssertThat(world.ObjectScripts.Count).IsEqual(1);
                AssertThat(world.ObjectScripts[(0, 1)]).IsEqual(futureWrite ? 0 : dyingAddress);
            }
            AssertThat(world.ObjectScripts.ContainsKey((0, 0))).IsFalse();
            AssertThat(world.ObjectScripts.ContainsKey((37, 0))).IsFalse();
            AssertThat(world.ObjectScripts.ContainsKey((37, 1))).IsFalse();
            facade.PressSearch();
            AssertThat(facade.Update(0)).IsTrue();
            var request = facade.PendingTrigger ?? throw new InvalidOperationException("Missing real owner28 search request.");
            var requestId = facade.PendingRequestId;
            AssertThat(request.OwnerId).IsEqual(28);
            AssertThat(request.EntryAddress).IsEqual(0x09C3);
            AssertThat(requestId == Guid.Empty).IsFalse();
            var surface = display.GetNode<PalDialoguePaletteSurface>("IndexedBackground");
            surface.SetPalette(palette);
            surface.SetImage(ResourceLoader.Load<PalDialogueIndexedImage>("res://Tests/Fixtures/DialogueSyntheticBackground.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep));
            AssertThat(world.ObjectScripts.Count).IsEqual(bankMode == "absent-seed" ? 0 : 1);
            if (bankMode != "absent-seed") AssertThat(world.ObjectScripts[(0, 1)]).IsEqual(futureWrite ? 0 : dyingAddress);
            int friendDeath = 0xA9B5;
            trigger.Start("story", "owner28", request.OwnerId, requestId);
            AssertBank(futureWrite ? 0 : dyingAddress); // Start seeds missing keys; future0090 has not executed.
            AssertThat(facade.BindTrigger(requestId, session)).IsTrue();
            var callId = session.CallId;
            AssertThat(callId == Guid.Empty).IsFalse();
            AssertThat(session.SourceRequestId).IsEqual(requestId);
            void AssertBank(int? initialDying = null)
            {
                AssertThat(world.ObjectScripts.Count).IsEqual(4);
                AssertThat(world.ObjectScripts[(0, 0)]).IsEqual(0);
                AssertThat(world.ObjectScripts[(0, 1)]).IsEqual(initialDying ?? dyingAddress);
                AssertThat(world.ObjectScripts[(37, 0)]).IsEqual(friendDeath);
                AssertThat(world.ObjectScripts[(37, 1)]).IsEqual(0);
            }
            string WorldWords() => System.Text.Json.JsonSerializer.Serialize(new
            {
                world.SceneId, world.Battlefield,
                Leader = new { world.Leader.X, world.Leader.Y, world.Leader.Direction, world.Leader.Layer, world.Leader.StepPhase },
                Scripts = world.ObjectScripts.OrderBy(pair => pair.Key).Select(pair => new { Key = pair.Key.ToString(), pair.Value }),
                Scenes = world.SceneScripts.OrderBy(pair => pair.Key).Select(pair => new { Key = pair.Key.ToString(), pair.Value }),
                Objects = world.Objects.Select(obj => new { obj.Id, obj.X, obj.Y, obj.Direction, obj.CurrentFrame, obj.State,
                    obj.VanishTime, obj.TriggerMode, obj.TriggerEntry, obj.AutoEntry, obj.AutoIdleCount }),
                world.PartyRoles,
                Vitals = world.PartyVitals.OrderBy(pair => pair.Key).Select(pair => new
                    { pair.Key, pair.Value.Hp, pair.Value.MaxHp, pair.Value.Mp, pair.Value.MaxMp }),
                Roles = roles.Roles
            });
            ClassicBattleLifecycleSession? lifecycle = null;
            var Capture = () => (
                // Do not serialize the single-role compatibility getter on a party lifecycle.
                Battle: lifecycle is null ? null : System.Text.Json.JsonSerializer.Serialize(new
                    { lifecycle.Current.IsInBattle, lifecycle.Current.Roles, lifecycle.Current.Combat }),
                Completion: lifecycle?.Completion,
                Call: (session.Phase, session.CallId, session.SourceRequestId, session.StartedEntry, session.Caller,
                    session.ReturnEntry, session.CurrentPosition, session.Context, session.Music, session.ScriptSuccess,
                    session.FadedToRed, session.PendingDisplay, session.PendingBattle, session.PendingLostReload,
                    session.PendingSceneWait, session.PendingMovement, session.PendingTransition, session.PendingBattleHook,
                    session.RunningBattleHook, session.RunningBattleEndScript, session.BattleRoles),
                World: WorldWords(), Leader: world.Leader,
                Facade: (facade.PendingTrigger, facade.PendingRequestId, facade.Current, facade.Rules,
                    System.Text.Json.JsonSerializer.Serialize(facade.State)),
                Display: (display.Session, display.Current?.Phase, display.Current?.VisibleText, display.Current?.ClearDisplay),
                Children: string.Join(",", display.GetChildren().Select(node => node.GetInstanceId())),
                Owned: string.Join(",", trigger.GetChildren().Select(node => node.GetInstanceId())),
                Pixel: surface.GetRenderedPixel(0, 0), Events: (palettes, restores, failure),
                Random: (random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws, random.Remaining));
            void Reject(Func<bool> operation)
            {
                var before = Capture();
                long sequence = updateSequence;
                AssertThat(operation()).IsFalse();
                AssertThat(Capture().Equals(before)).IsTrue();
                AssertThat(updateSequence).IsEqual(sequence);
            }
            Guid? lastDisplayId = null;
            async Task AdvanceOwnedDisplay(string text, TriggerScriptPhase after,
                (int Ints, int Floats, int Remaining) entropy, (int Hp0, int Hp1, int Mp0, int Mp1) owner)
            {
                var pending = session.PendingDisplay ?? throw new InvalidOperationException("Missing owned display.");
                AssertThat(ReferenceEquals(display.Session, pending.Display)).IsTrue();
                AssertThat(pending.Display.Current.Phase).IsEqual(DialogueDisplayPhase.Typing);
                Reject(() => session.AcknowledgeDisplay(pending.Id));
                Reject(() => session.AcknowledgeDisplay(callId));
                if (lastDisplayId is { } stale) Reject(() => session.AcknowledgeDisplay(stale));
                updated = null;
                // Preserve the FIRST result for this actual producer, including an erroneous Completed phase.
                await Until(runner, () => (ReferenceEquals(firstNonTypingSession, pending.Display)
                    && firstNonTypingSnapshot is not null) || failure is not null);
                AssertNoFailure();
                AssertThat(firstNonTypingSnapshot!.Phase).IsEqual(DialogueDisplayPhase.WaitingForAdvance);
                AssertThat(firstNonTypingSnapshot.VisibleText).IsEqual(text);
                if (titleGlyph && text == "U") AssertThat(firstNonTypingSnapshot.TitleText).IsEqual("Z:");
                var Observe = () => (State: Capture(), Producer: display.Session, Snapshot: display.Current,
                    Phase: session.Phase, Caller: session.Caller, Return: session.ReturnEntry,
                    Owners: (roles.Roles[0].Hp, roles.Roles[1].Hp, roles.Roles[0].Mp, roles.Roles[1].Mp),
                    Entropy: (random.IntCalls, random.FloatCalls, random.Remaining),
                    Bank: world.ObjectScripts.OrderBy(pair => pair.Key).ToArray());
                var waiting = Observe();
                var first = waiting;
                var eligible = waiting;
                bool firstCaptured = false, firstPoll = false, captured = false;
                armed = poll =>
                {
                    var sample = Observe();
                    if (!firstCaptured) { first = sample; firstPoll = poll; firstCaptured = true; }
                    if (poll) { eligible = sample; captured = true; armed = null; }
                    else if (!sample.State.Equals(waiting.State))
                        waitViolation ??= $"First-battle {text} advanced before eligible100ms poll: {updateDiagnostic}";
                };
                SendOwnedKey(true);
                SendOwnedKey(false);
                await Until(runner, () => captured || waitViolation is not null || failure is not null);
                AssertNoFailure();
                AssertThat(firstCaptured).IsTrue();
                AssertThat(ReferenceEquals(first.Producer, pending.Display)).IsTrue();
                if (!firstPoll)
                {
                    AssertThat(first.Snapshot!.Phase).IsEqual(DialogueDisplayPhase.WaitingForAdvance);
                    AssertThat(first.State.Equals(waiting.State)).IsTrue();
                }
                AssertThat(ReferenceEquals(eligible.Producer, pending.Display)).IsTrue();
                AssertThat(eligible.Snapshot!.Phase).IsEqual(DialogueDisplayPhase.Completed);
                AssertThat(eligible.Phase).IsEqual(after);
                AssertThat(eligible.Caller).IsEqual(28);
                AssertThat(eligible.Return).IsEqual(0x09C3);
                AssertThat(eligible.Owners.Equals(owner)).IsTrue();
                AssertThat(eligible.Entropy.Equals(entropy)).IsTrue();
                AssertThat(eligible.State.Random.IntDraws).IsEqual(entropy.Ints);
                AssertThat(eligible.State.Random.FloatDraws).IsEqual(entropy.Floats);
                // Literal three-stage bank: Start DY0, prefix DY24, then Y writes FD32.
                if (secondOrderWrite && text == "U") friendDeath = 32;
                AssertThat(eligible.Bank.Select(pair => (pair.Key.ObjectId, pair.Key.SlotIndex, pair.Value))
                    .SequenceEqual(new[] { (0, 0, 0), (0, 1, dyingAddress), (37, 0, friendDeath), (37, 1, 0) })).IsTrue();
                AssertBank();
                AssertThat(ReferenceEquals(facade.PendingTrigger, request)).IsTrue();
                AssertThat(facade.PendingRequestId).IsEqual(requestId);
                Reject(() => session.AcknowledgeDisplay(pending.Id));
                lastDisplayId = pending.Id;
            }
            AssertBank(futureWrite ? 0 : dyingAddress);
            if (bankMode == "warmup")
            {
                AssertStartRejection(Capture, () => trigger.Start("story", "missing-entry", 65536, requestId),
                    typeof(InvalidOperationException), "A trigger call is already active.");
                AssertStartRejection(Capture, () => trigger.Start("story", "missing-entry", 65536, Guid.Empty),
                    typeof(ArgumentException), "A source request ID must be nonempty.");
            }
            await AdvanceOwnedDisplay("FIGHT", TriggerScriptPhase.WaitingForBattle, (0, 0, friendDeathControl ? 14 : randomBranch ? 19 : 18), (97, 57, 17, 7));
            var prefixZero = roles.Roles[0];
            var prefixOne = roles.Roles[1];
            var battle = session.PendingBattle ?? throw new InvalidOperationException("Missing actual battle.");
            lifecycle = battle.Lifecycle;
            AssertThat(battle.Caller).IsEqual(28);
            Reject(() => facade.CompleteTrigger(requestId, session, 0));
            Reject(() => trigger.AcknowledgeBattle(battle.Id, null));
            battle.Lifecycle.SelectAttack(0, 0);
            battle.Lifecycle.SelectAttack(1, 1);
            AssertThat((random.IntCalls, random.FloatCalls, random.Remaining).Equals((0, 4, friendDeathControl ? 10 : randomBranch ? 15 : 14))).IsTrue();
            AssertThat(battle.Lifecycle.ResolveNextAction()).IsTrue();
            AssertThat(battle.Lifecycle.Current.Combat.Players.Select(player => player.Hp).SequenceEqual(new[] { 97, 13 })).IsTrue();
            AssertThat(battle.Lifecycle.Current.Roles[1].Hp).IsEqual(13);
            AssertThat(battle.Lifecycle.Current.Combat.Enemies.Select(enemy => enemy.Hp).SequenceEqual(new[] { 43, 1 })).IsTrue();
            AssertThat(battle.Lifecycle.Completion is null).IsTrue();
            AssertThat((random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws, random.Remaining)
                .Equals((5, 4, 5, 4, friendDeathControl ? 5 : randomBranch ? 10 : 9))).IsTrue();
            if (liveZero)
            {
                // Real 0090 stored zero suppresses seed A96E despite the genuine Dying damage.
                AssertThat(session.PendingBattleHook is null && session.RunningBattleHook is null && session.PendingDisplay is null).IsTrue();
                AssertThat(session.Phase).IsEqual(TriggerScriptPhase.WaitingForBattle);
                AssertThat(session.Caller).IsEqual(28);
                AssertThat(display.Current!.Phase).IsEqual(DialogueDisplayPhase.Completed);
                AssertThat(battle.Lifecycle.Current.Combat.Phase).IsEqual(ClassicBattlePhase.Resolving);
                AssertBank();
            }
            else
            {
                var hook = session.PendingBattleHook ?? throw new InvalidOperationException("Missing genuine Dying hook.");
                AssertThat(hook.Kind).IsEqual(ClassicBattleHookKind.Dying);
                AssertThat(hook.PartyIndex).IsEqual(1);
                AssertThat(hook.RoleId).IsEqual(1);
                AssertThat(hook.ScriptAddress).IsEqual(dyingAddress);
                AssertThat(battle.Lifecycle.Current.Combat.Phase).IsEqual(ClassicBattlePhase.RoleHook);
                trigger.StartBattleHook();
                AssertThat(ReferenceEquals(session.RunningBattleHook, hook)).IsTrue();
                AssertThat(session.Caller).IsEqual(1);
                AssertThat(session.PendingDisplay!.MessageReferences[0].StartMessageId).IsEqual(randomBranch ? 67 : seedVariant || futureWrite ? 66 : 65);
                AssertThat(session.PendingDisplay.Context.PortraitId).IsEqual(0);
                Reject(() => facade.CompleteTrigger(requestId, session, 0));
                await AdvanceOwnedDisplay(randomBranch ? "Z" : seedVariant || futureWrite ? "U" : "FIGHT", TriggerScriptPhase.WaitingForBattle,
                    (hookInts, 4, friendDeathControl ? 5 : 9), (97, 57, 17, 7));
                if (futureWrite)
                    AssertThat(session.CurrentPosition == (randomBranch
                        ? new TriggerPosition("story", "L0022", 34) : secondOrderWrite
                        ? new TriggerPosition("story", "L001B", 27)
                        : new TriggerPosition("story", "L001A", 26))).IsTrue();
            }
            AssertThat(session.PendingBattleHook is null && session.RunningBattleHook is null).IsTrue();
            AssertThat(ReferenceEquals(session.PendingBattle, battle)).IsTrue();
            AssertThat(battle.Lifecycle.Current.Combat.Phase).IsEqual(ClassicBattlePhase.Resolving);
            if (friendDeathControl)
            {
                AssertThat(world.ObjectScripts[(37, 0)]).IsEqual(32); // Actual preceding Y/0090, not a test write.
                AssertThat(battle.Lifecycle.ResolveNextAction()).IsTrue();
                AssertThat(battle.Lifecycle.Current.Combat.Players.Select(player => player.Hp).SequenceEqual(new[] { 97, 0 })).IsTrue();
                AssertThat(battle.Lifecycle.Current.Combat.Enemies.Select(enemy => enemy.Hp).SequenceEqual(new[] { 43, 1 })).IsTrue();
                AssertThat(battle.Lifecycle.Current.Combat.LastAction!.Damage).IsEqual(13); // Clamp the second44-point hit to13 remaining HP.
                AssertThat(battle.Lifecycle.Current.Combat.LastAction.CovererPartyIndex is null).IsTrue();
                var friendHook = session.PendingBattleHook ?? throw new InvalidOperationException("Missing genuine coverer FriendDeath hook.");
                AssertThat(friendHook.Kind).IsEqual(ClassicBattleHookKind.FriendDeath);
                AssertThat(friendHook.PartyIndex).IsEqual(0);
                AssertThat(friendHook.RoleId).IsEqual(0);
                AssertThat(friendHook.ScriptAddress).IsEqual(32);
                AssertThat(battle.Lifecycle.Current.Combat.Phase).IsEqual(ClassicBattlePhase.RoleHook);
                AssertThat((random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws, random.Remaining).Equals((10, 4, 10, 4, 0))).IsTrue();
                trigger.StartBattleHook();
                AssertThat(ReferenceEquals(session.RunningBattleHook, friendHook)).IsTrue();
                AssertThat(session.Caller).IsEqual(0); // The coverer, not dead role1 or outer owner28.
                AssertThat(session.PendingDisplay!.MessageReferences[0].StartMessageId).IsEqual(67);
                Reject(() => facade.CompleteTrigger(requestId, session, 0));
                await AdvanceOwnedDisplay("Z", TriggerScriptPhase.WaitingForBattle, (10, 4, 0), (97, 57, 17, 7));
                AssertThat(session.CurrentPosition).IsEqual(new TriggerPosition("story", "L0022", 34));
                AssertThat(session.PendingBattleHook is null && session.RunningBattleHook is null).IsTrue();
                AssertThat(ReferenceEquals(session.PendingBattle, battle)).IsTrue();
                AssertThat(battle.Lifecycle.Current.Combat.Phase).IsEqual(ClassicBattlePhase.Resolving);
                AssertThat(battle.Lifecycle.Completion is null).IsTrue();
                AssertBank();
                Reject(() => facade.CompleteTrigger(requestId, session, 0));
                // Bounded hook positive: the battle is still active. No fabricated result/outer ACK.
                return;
            }
            AssertThat(battle.Lifecycle.ResolveNextAction()).IsTrue();
            AssertThat(battle.Lifecycle.Current.Combat.LastAction!.Damage).IsEqual(43);
            AssertThat(battle.Lifecycle.Current.Combat.Enemies.Select(enemy => enemy.Hp).SequenceEqual(new[] { 0, 1 })).IsTrue();
            AssertThat(battle.Lifecycle.Completion is null).IsTrue();
            AssertThat((random.IntCalls, random.FloatCalls, random.Remaining).Equals((randomBranch ? 10 : 9, 5, 4))).IsTrue();
            AssertThat(battle.Lifecycle.ResolveNextAction()).IsTrue();
            AssertThat(battle.Lifecycle.Current.Combat.LastAction!.Damage).IsEqual(7);
            AssertThat(battle.Lifecycle.Current.Combat.Phase).IsEqual(ClassicBattlePhase.Won);
            AssertThat(battle.Lifecycle.Current.Combat.Enemies.Select(enemy => enemy.Hp).SequenceEqual(new[] { 0, 0 })).IsTrue();
            var completion = battle.Lifecycle.Completion ?? throw new InvalidOperationException("Missing actual lifecycle completion.");
            AssertBank(); // FD32 persists through real Won completion, before adapter ACK.
            AssertThat(battle.Lifecycle.OwnsCompletion(completion)).IsTrue();
            AssertThat(battle.Lifecycle.Current.IsInBattle).IsFalse();
            AssertThat(completion.ExperienceGranted).IsEqual(0);
            AssertThat(completion.CashGranted).IsEqual(0);
            AssertThat((battle.Lifecycle.Current.Roles[0].Hp, battle.Lifecycle.Current.Roles[1].Hp,
                battle.Lifecycle.Current.Roles[0].Mp, battle.Lifecycle.Current.Roles[1].Mp).Equals((148, 126, 33, 123))).IsTrue();
            AssertThat(ReferenceEquals(roles.Roles[0], prefixZero) && ReferenceEquals(roles.Roles[1], prefixOne)).IsTrue();
            AssertThat((prefixZero.Hp, prefixOne.Hp, prefixZero.Mp, prefixOne.Mp).Equals((97, 57, 17, 7))).IsTrue();
            AssertThat((random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws, random.Remaining).Equals((finalInts, 6, finalInts, 6, 0))).IsTrue();
            Reject(() => facade.CompleteTrigger(requestId, session, 0));
            Reject(() => trigger.AcknowledgeBattle(battle.Id, null));
            Reject(() => trigger.AcknowledgeBattle(callId, completion));
            AssertThat(trigger.AcknowledgeBattle(battle.Id, completion)).IsTrue();
            AssertBank();
            AssertThat(ReferenceEquals(roles.Roles[0], prefixZero) || ReferenceEquals(roles.Roles[1], prefixOne)).IsFalse();
            AssertThat(session.PendingDisplay!.MessageReferences[0].StartMessageId).IsEqual(2);
            Reject(() => trigger.AcknowledgeBattle(battle.Id, completion));
            Reject(() => facade.CompleteTrigger(requestId, session, 0));
            await AdvanceOwnedDisplay("WIN", TriggerScriptPhase.Completed, (finalInts, 6, 0), (148, 126, 33, 123));
            AssertThat(session.PendingDisplay is null && session.PendingBattle is null).IsTrue();
            AssertThat(session.CurrentPosition!.SourceAddress).IsEqual(0x0A32);
            AssertThat(session.CallId).IsEqual(callId);
            AssertThat(session.SourceRequestId).IsEqual(requestId);
            AssertThat(facade.CompleteTrigger(requestId, session, 0)).IsTrue();
            AssertBank();
            AssertThat(facade.PendingTrigger is null).IsTrue();
            AssertThat(facade.PendingRequestId).IsEqual(Guid.Empty);
            AssertThat(world.Objects.Single(obj => obj.Id == 28).TriggerEntry).IsEqual(0x09C3);
            AssertThat(facade.Objects.Single(obj => obj.Id == 28).TriggerEntry).IsEqual(0x09C3);
            Reject(() => facade.CompleteTrigger(requestId, session, 0));
            Reject(() => trigger.AcknowledgeBattle(battle.Id, completion));
            // Search returned before the first exploration update: the 50ms return floor
            // releases that initially-due step, which schedules the next step at150ms.
            AssertThat(facade.Update(49)).IsFalse();
            AssertThat(facade.Update(50)).IsTrue();
            AssertThat(facade.Update(100)).IsFalse();
            AssertThat(facade.Update(150)).IsTrue();
            AssertThat(world.Leader).IsEqual(facade.Current.Leader);
            AssertThat(ReferenceEquals(world.RoleLiveState, roles)).IsTrue();
            AssertThat((roles.Roles[0].Hp, roles.Roles[1].Hp, roles.Roles[0].Mp, roles.Roles[1].Mp).Equals((148, 126, 33, 123))).IsTrue();
            AssertBank();
            AssertThat(random.Remaining).IsEqual(0);
            AssertNoFailure();
            if (bankMode == "warmup")
            {
                facade.PressSearch();
                AssertThat(facade.Update(250)).IsTrue();
                var next = facade.PendingTrigger ?? throw new InvalidOperationException("Missing real post-warmup search.");
                var nextId = facade.PendingRequestId;
                AssertThat(next.OwnerId).IsEqual(28);
                AssertThat(next.EntryAddress).IsEqual(0x09C3);
                AssertThat(nextId == Guid.Empty || nextId == requestId).IsFalse();
                var glyph = resources.Font.GetGlyph(85);
                resources.Font.Glyphs.Remove(glyph);
                void RejectStart(Action operation, Type exceptionType, string diagnostic)
                {
                    var before = Capture();
                    var beforeCurrent = display.Current;
                    long sequence = updateSequence;
                    Exception? rejected = null;
                    try { operation(); } catch (Exception error) { rejected = error; }
                    AssertThat(rejected is not null && rejected.GetType() == exceptionType).IsTrue();
                    AssertThat(rejected!.Message.Contains(diagnostic, StringComparison.Ordinal)).IsTrue();
                    AssertThat(Capture().Equals(before)).IsTrue();
                    AssertThat(ReferenceEquals(display.Current, beforeCurrent)).IsTrue();
                    AssertThat(updateSequence).IsEqual(sequence);
                }
                RejectStart(() => trigger.Start("story", "owner28", 65536, Guid.Empty), typeof(ArgumentException),
                    "A source request ID must be nonempty.");
                RejectStart(() => trigger.Start("story", "setter", 65536, nextId), typeof(InvalidDataException),
                    "owner eventObject:28/wTriggerScript does not match the bound native scene.");
                RejectStart(() => trigger.Start("story", "owner28", 65536, nextId), typeof(ArgumentOutOfRangeException), "caller");
                RejectStart(() => trigger.Start("story", "owner28", 65535, nextId), typeof(InvalidDataException), "no glyph for U+0055");
                var busyLanguage = ResourceLoader.Load<PalDialogueLanguage>(resources.ResourceSha256.Keys.Single(
                    uri => uri.EndsWith("/Language.en.tres", StringComparison.Ordinal)), cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
                display.Start(busyLanguage, resources.Font,
                    ResourceLoader.Load<PalDialogueIndexedImage>("res://Tests/Fixtures/DialogueSyntheticBackground.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep),
                    resources.Prompts.ToArray(), palette, 1, 0, 0, 0, 0);
                RejectStart(() => trigger.Start("story", "missing-entry", 65536, nextId), typeof(InvalidOperationException),
                    "The display is already in use.");
                RejectStart(() => trigger.Start("story", "owner28", 65536, nextId), typeof(InvalidOperationException),
                    "The display is already in use.");
                RejectStart(() => trigger.Start("story", "missing-entry", 65536, Guid.Empty), typeof(ArgumentException),
                    "A source request ID must be nonempty.");
                await Until(runner, () => display.Current?.Phase == DialogueDisplayPhase.WaitingForAdvance || failure is not null);
                AssertNoFailure();
                SendOwnedKey(true); SendOwnedKey(false);
                await Until(runner, () => display.Current?.Phase == DialogueDisplayPhase.Completed || failure is not null);
                AssertNoFailure();
                resources.Font.Glyphs.Add(glyph); // Restore only this case's artificial bound leaf.
                trigger.Start("story", "owner28", 65535, nextId);
                // fight.c:875-876 uses the Dying role1; no enemy battle-end trigger ran in this fixture.
                // script.c:3162-3182 keeps that nested caller globally, not the restored outer owner28.
                AssertThat(session.Caller).IsEqual(1);
                AssertThat(session.CallId == callId || session.CallId == Guid.Empty).IsFalse();
                AssertThat(session.SourceRequestId).IsEqual(nextId);
                var beforeBind = Capture();
                AssertThat(facade.BindTrigger(nextId, session)).IsFalse(); // Real request still requires owner28, not role1.
                AssertThat(Capture().Equals(beforeBind)).IsTrue();
                AssertThat(session.PendingDisplay!.MessageReferences[0].StartMessageId).IsEqual(1);
                AssertThat(session.PendingDisplay.Display.Current.Phase).IsEqual(DialogueDisplayPhase.Typing);
                RejectStart(() => trigger.Start("story", "missing-entry", 65536, nextId), typeof(InvalidOperationException),
                    "A trigger call is already active.");
                RejectStart(() => trigger.Start("story", "missing-entry", 65536, Guid.Empty), typeof(ArgumentException),
                    "A source request ID must be nonempty.");
            }
        }
        finally
        {
            armed = null;
            display.UpdateCompleted -= OnUpdate;
            display.Failed -= OnFailure;
            display.ActivePaletteChanged -= OnPalette;
            display.BackgroundRestoreRequested -= OnRestore;
            SendOwnedKey(false);
            var ownedNodes = display.GetChildren().Concat(trigger.GetChildren()).ToArray();
            runner.Dispose();
            AssertThat(GodotObject.IsInstanceValid(display)).IsFalse();
            AssertThat(GodotObject.IsInstanceValid(trigger)).IsFalse();
            foreach (var node in ownedNodes) AssertThat(GodotObject.IsInstanceValid(node)).IsFalse();
        }
    }

    [TestCase(Timeout = 15000)]
    public async Task Story_current_world_hook_uses_live_entry_and_owned_display_ack() =>
        await AssertStoryCurrentHookReturn("live20");

    [TestCase("absent-seed", Timeout = 15000)]
    [TestCase("live-zero", Timeout = 15000)]
    public async Task Story_declared_seed_and_live_zero_reach_owned_outer_return(string bankMode) =>
        await AssertStoryCurrentHookReturn(bankMode);

    [TestCase(Timeout = 15000)]
    public async Task Story_future_literal_0090_reaches_owned_outer_return() =>
        await AssertStoryCurrentHookReturn("future-write");

    [TestCase(Timeout = 15000)]
    public async Task Story_second_order_literal_0090_reaches_owned_outer_return() =>
        await AssertStoryCurrentHookReturn("second-order-write");

    [TestCase(Timeout = 15000)]
    public async Task Story_second_order_0090_executes_real_coverer_friend_death_and_owned_ack() =>
        await AssertStoryCurrentHookReturn("friend-death");

    [TestCase(Timeout = 15000)]
    public async Task Story_random_hook_target_reaches_owned_outer_return() =>
        await AssertStoryCurrentHookReturn("random-target");

    [TestCase(Timeout = 15000)]
    public async Task Story_title_and_body_reach_owned_outer_return() =>
        await AssertStoryCurrentHookReturn("title");

    [TestCase(Timeout = 15000)]
    public async Task Story_completed_warmup_preserves_rejection_priority_and_caller() =>
        await AssertStoryCurrentHookReturn("warmup");

    private async Task AssertStoryCurrentHookReturn(string bankMode)
    {
        int dyingAddress = bankMode switch
        {
            "live20" => 20,
            "absent-seed" => 40,
            "live-zero" => 0,
            "future-write" or "second-order-write" or "random-target" or "title" or "warmup" or "friend-death" => 24,
            _ => throw new ArgumentOutOfRangeException(nameof(bankMode))
        };
        bool seedVariant = bankMode is "absent-seed" or "live-zero";
        bool liveZero = bankMode == "live-zero";
        bool friendDeathControl = bankMode == "friend-death";
        bool secondOrderWrite = friendDeathControl || bankMode == "second-order-write";
        bool randomBranch = bankMode == "random-target";
        bool titleGlyph = bankMode == "title";
        bool futureWrite = titleGlyph || randomBranch || secondOrderWrite || bankMode is "future-write" or "warmup";
        int hookInts = randomBranch ? 6 : 5;
        int finalInts = randomBranch ? 10 : 9;
        int initiativeFloats = friendDeathControl ? 4 : 3;
        using var files = new NativeStoryProfileFixture(currentHookPositive: true, seedDyingGlyph: seedVariant,
            futureDyingWrite: futureWrite, secondOrderDyingWrite: secondOrderWrite, randomDyingBranch: randomBranch,
            titleDyingGlyph: titleGlyph, forceCurrentFriendDeath: friendDeathControl);
        if (liveZero || futureWrite)
        {
            files.Edit("shared.json", "\"value\":20,\"slotIndex\":1", "\"value\":0,\"slotIndex\":1");
            files.Edit("shared.json", "\"opcode\":144,\"operands\":[0,20,1]", "\"opcode\":144,\"operands\":[0,0,1]");
            files.RefreshHashes(); files.SaveProfile();
        }
        var story = NativeStoryProfile.Load(files.Uri, "en");
        // Strict Load sees the statically required E. Only this fresh live0 instance then loses E.
        if (liveZero) story.Font.Glyphs.Remove(story.Font.GetGlyph(69));
        AssertThat(story.BattleContent.Players.Count).IsEqual(2);
        AssertThat(story.BattleContent.EnemyTeams[18].Count).IsEqual(friendDeathControl ? 2 : 1);
        AssertThat(story.BattleContent.Players[0].NameObjectId).IsEqual(37);
        AssertThat(story.BattleContent.Players[1].NameObjectId).IsEqual(0);
        AssertThat(story.BattleContent.Players[1].CoveredByRoleId).IsEqual(0);
        AssertThat(story.BattleContent.Players[1].DyingScript).IsEqual(40);
        AssertThat(story.BattleContent.InitialRoles[0].Hp).IsEqual(100);
        AssertThat(story.BattleContent.InitialRoles[0].MaxHp).IsEqual(200);
        AssertThat(story.BattleContent.InitialRoles[1].Hp).IsEqual(60);
        AssertThat(story.BattleContent.InitialRoles[1].MaxHp).IsEqual(240);
        AssertThat(story.BattleContent.InitialRoles[1].Mp).IsEqual(10);
        AssertThat(story.BattleContent.InitialRoles[1].MaxMp).IsEqual(240);
        AssertThat(story.BattleContent.PanelDeclaredRoles.Count).IsEqual(0);
        var roles = new RoleLiveState(story.BattleContent.InitialRoles.Values.ToArray());
        var scene = files.Static.Scene;
        var world = DynamicEventWorld.ForHost(story.Exploration.Rules, story.Exploration.Leader,
            scene.SceneId, scene.WScriptOnEnter, scene.WScriptOnTeleport, roles,
            story.BattleContent.Players.Select(player => player.RoleId).ToArray());
        var random = new CurrentHookPositiveRandom(friendDeathControl ? [
            // Independent initiative + two44-point hits, the latter clamped to16 remaining HP.
            (true, .9f, 1.1f, 1), (true, .9f, 1.1f, 1), (true, .9f, 1.1f, 1), (true, .9f, 1.1f, 1),
            (false, 0, 1, 1), (false, 0, 16, 0), (false, 0, 2, 0), (false, 0, 1, 0), (false, 1, 10, 1),
            (false, 0, 1, 1), (false, 0, 16, 0), (false, 0, 2, 0), (false, 0, 1, 0), (false, 1, 10, 1)
        ] : null, randomTarget: randomBranch);
        AssertThat(world.ObjectScripts.Count).IsEqual(0);
        if (bankMode != "absent-seed")
        {
            var setter = new TriggerScriptSession(story.Events, story.Language, world);
            setter.Start("shared", "setter", 60);
            AssertThat(setter.Phase).IsEqual(TriggerScriptPhase.Completed);
            AssertThat(world.ObjectScripts.Count).IsEqual(1);
            AssertThat(world.ObjectScripts[(0, 1)]).IsEqual(futureWrite ? 0 : dyingAddress);
        }
        AssertThat(world.ObjectScripts.ContainsKey((37, 0))).IsFalse();
        AssertThat(world.ObjectScripts.ContainsKey((37, 1))).IsFalse();
        AssertThat(world.ObjectScripts.ContainsKey((0, 0))).IsFalse();
        var interaction = NpcInteractionSession.ForSelectedEntry(story.Exploration.Rules,
            story.Exploration.Leader, story.Entry);
        interaction.PressSearch();
        AssertThat(interaction.Update(0)).IsTrue();
        var request = interaction.PendingTrigger
            ?? throw new InvalidOperationException("The authored owner60 geometry did not produce a search request.");
        AssertThat(request.FileId).IsEqual("shared");
        AssertThat(request.EntryId).IsEqual("npc");
        AssertThat(request.Caller).IsEqual(60);
        AssertThat(request.SourceAddress).IsEqual(1);
        AssertThat(request.Id == Guid.Empty).IsFalse();

        using var runner = ISceneRunner.Load("res://Tests/DialogueDisplayFixture.tscn", true);
        var display = (PalDialogueDisplay)runner.Scene()!;
        var trigger = new PalTriggerDialogue { FirstBattleWorld = world };
        display.AddChild(trigger);
        DialogueDisplaySnapshot? updated = null;
        Exception? failure = null;
        Action<bool>? observeArmedUpdate = null;
        DialogueDisplaySession? firstNonTypingSession = null;
        DialogueDisplaySnapshot? firstNonTyping = null;
        DialogueDisplaySession? previousSession = null;
        DialogueDisplayPhase? previousPhase = null;
        uint waitRemainder = 0;
        long updateSequence = 0;
        string updateDiagnostic = "";
        string waitDiagnostics = "";
        string? waitViolation = null;
        var surface = display.GetNode<PalDialoguePaletteSurface>("IndexedBackground");
        PalDialogueIndexedImage? warmupBackground = null;
        int palettes = 0, restores = 0;
        void OnPalette(Color[] colors) { palettes++; surface.SetPalette(colors); }
        void OnRestore() { restores++; surface.SetImage(warmupBackground!); }
        void SendOwnedKey(bool pressed)
        {
            using var input = new InputEventKey { Keycode = Godot.Key.Space, Pressed = pressed, Echo = false };
            Input.ParseInputEvent(input);
            Input.FlushBufferedEvents();
        }
        void OnUpdate()
        {
            updated = display.Current;
            if (!ReferenceEquals(firstNonTypingSession, display.Session))
            {
                firstNonTypingSession = display.Session;
                firstNonTyping = null;
            }
            if (firstNonTyping is null && updated is { Phase: not DialogueDisplayPhase.Typing })
                firstNonTyping = updated;
            double delta = display.GetProcessDeltaTime();
            uint elapsed = (uint)Math.Clamp((long)Math.Round(delta * 1000.0), 0L, uint.MaxValue);
            uint before = waitRemainder;
            bool poll = false;
            if (ReferenceEquals(previousSession, display.Session)
                && previousPhase == DialogueDisplayPhase.WaitingForAdvance)
            {
                ulong waiting = (ulong)waitRemainder + elapsed;
                poll = waiting >= 100;
                waitRemainder = (uint)(waiting % 100);
            }
            else
            {
                // Typing/new-session elapsed never belongs to the newly entered wait.
                waitRemainder = 0;
            }
            previousSession = display.Session;
            previousPhase = updated?.Phase;
            updateSequence++;
            updateDiagnostic = $"seq={updateSequence},ticks={Godot.Time.GetTicksUsec()},frame={Engine.GetProcessFrames()},"
                + $"delta={delta:R},ms={elapsed},remainder={before}->{waitRemainder},poll={poll},phase={updated?.Phase}";
            observeArmedUpdate?.Invoke(poll);
        }
        void OnFailure(Exception error) => failure ??= error;
        void AssertNoFailure()
        {
            if (failure is not null) throw new InvalidOperationException("Story positive display failed.", failure);
            if (waitViolation is not null) throw new InvalidOperationException(waitViolation);
        }
        try
        {
            trigger.Initialize(display, story, random);
            // Initialize subscribed the production receipt consumer before this observer.
            display.UpdateCompleted += OnUpdate;
            display.Failed += OnFailure;
            if (bankMode == "warmup")
            {
                warmupBackground = ResourceLoader.Load<PalDialogueIndexedImage>("res://Tests/Fixtures/DialogueSyntheticBackground.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
                surface.SetPalette(story.Palette.ToArray());
                surface.SetImage(warmupBackground);
                display.ActivePaletteChanged += OnPalette;
                display.BackgroundRestoreRequested += OnRestore;
            }
            var session = trigger.Session!;
            AssertThat(world.ObjectScripts.Count).IsEqual(bankMode == "absent-seed" ? 0 : 1);
            if (bankMode != "absent-seed") AssertThat(world.ObjectScripts[(0, 1)]).IsEqual(futureWrite ? 0 : dyingAddress);
            trigger.Start(request.FileId, request.EntryId, request.Caller, request.Id);
            AssertBank(futureWrite ? 0 : dyingAddress); // Start seeds only missing keys; future0090 has not executed.
            AssertThat(interaction.BindTrigger(request.Id, session)).IsTrue();
            var callId = session.CallId;
            AssertThat(callId == Guid.Empty).IsFalse();
            AssertThat(session.SourceRequestId).IsEqual(request.Id);
            AssertThat(session.Caller).IsEqual(60);
            AssertThat(session.ReturnEntry).IsEqual(1);
            AssertThat(session.StartedEntry == story.Entry).IsTrue();
            void AssertBank(int? initialDying = null, int friendDeath = 0)
            {
                AssertThat(world.ObjectScripts.Count).IsEqual(4);
                AssertThat(world.ObjectScripts[(37, 0)]).IsEqual(friendDeath);
                AssertThat(world.ObjectScripts[(37, 1)]).IsEqual(0);
                AssertThat(world.ObjectScripts[(0, 0)]).IsEqual(0);
                AssertThat(world.ObjectScripts[(0, 1)]).IsEqual(initialDying ?? dyingAddress);
            }
            AssertBank(futureWrite ? 0 : dyingAddress);
            if (bankMode == "warmup")
            {
                var CaptureInitialActive = () => (
                    Call: (session.Phase, session.BlocksSceneUpdates, session.CallId, session.SourceRequestId,
                        session.StartedEntry, session.Caller, session.ReturnEntry, session.CurrentPosition, session.Context,
                        session.ScriptSuccess, session.Music, session.FadedToRed, session.PendingDisplay, session.PendingBattle,
                        session.PendingLostReload, session.PendingSceneWait, session.PendingMovement, session.PendingTransition,
                        session.PendingBattleHook, session.RunningBattleHook, session.RunningBattleEndScript, session.BattleRoles),
                    World: (world.SceneId, world.Leader, world.Battlefield, world.RoleLiveState, world.PartyRoles, world.Objects),
                    Words: System.Text.Json.JsonSerializer.Serialize(new { world.Objects, world.PartyRoles, world.PartyVitals,
                        Roles = roles.Roles,
                        Bank = world.ObjectScripts.OrderBy(pair => pair.Key).Select(pair => new { Key = pair.Key.ToString(), pair.Value }),
                        Scenes = world.SceneScripts.OrderBy(pair => pair.Key).Select(pair => new { Key = pair.Key.ToString(), pair.Value }) }),
                    Roles: (roles.Roles[0], roles.Roles[1]),
                    Display: (display.Session, display.Current, firstNonTypingSession, firstNonTyping),
                    Nodes: string.Join(",", display.GetChildren().Concat(trigger.GetChildren()).Select(node => node.GetInstanceId())),
                    Pixel: surface.GetRenderedPixel(0, 0),
                    Producer: (interaction.Current, interaction.Phase, interaction.BlocksSceneUpdates, interaction.PendingTrigger),
                    Events: (updateSequence, palettes, restores, failure),
                    Random: (random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws, random.Remaining));
                AssertStartRejection(CaptureInitialActive, () => trigger.Start("shared", "missing-entry", 65536, request.Id),
                    typeof(InvalidOperationException), "A trigger call is already active.");
                AssertStartRejection(CaptureInitialActive, () => trigger.Start("shared", "missing-entry", 65536, Guid.Empty),
                    typeof(ArgumentException), "A source request ID must be nonempty.");
            }
            var prelude = session.PendingDisplay!;
            AssertThat(prelude.MessageReferences[0].StartMessageId).IsEqual(61);
            AssertThat(prelude.Context.PortraitId).IsEqual(7);
            AssertThat(ReferenceEquals(display.Session, prelude.Display)).IsTrue();
            AssertThat((random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws).Equals((0, 0, 0, 0))).IsTrue();
            await Until(runner, () => (ReferenceEquals(firstNonTypingSession, prelude.Display) && firstNonTyping is not null) || failure is not null);
            AssertNoFailure();
            AssertThat(firstNonTyping!.Phase).IsEqual(DialogueDisplayPhase.WaitingForAdvance);
            AssertThat(firstNonTyping.VisibleText).IsEqual("A");
            var observePrelude = () => (
                Display: (display.Session, Producer: prelude.Display, display.Current),
                Call: (session.Phase, session.Caller, session.ReturnEntry, session.CallId, session.SourceRequestId,
                    session.CurrentPosition, session.PendingDisplay, session.PendingBattleHook, session.RunningBattleHook, session.PendingBattle),
                Bank: world.ObjectScripts.OrderBy(pair => pair.Key).ToArray(),
                Random: (random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws, random.Remaining),
                Outer: (interaction.Phase, interaction.PendingTrigger));
            var preludeWaiting = observePrelude();
            var preludeFirst = preludeWaiting;
            var preludeEligible = preludeWaiting;
            bool preludeFirstCaptured = false, preludeFirstPoll = false, preludeCaptured = false;
            observeArmedUpdate = poll =>
            {
                var sample = observePrelude();
                string association = $"nodeProducer={ReferenceEquals(sample.Display.Session, sample.Display.Producer)}";
                if (!preludeFirstCaptured)
                {
                    preludeFirst = sample;
                    preludeFirstPoll = poll;
                    preludeFirstCaptured = true;
                    waitDiagnostics += $"\nprelude first: {updateDiagnostic},{association}";
                }
                if (poll)
                {
                    // Capture the FIRST eligible poll even if its display/owner phase is wrong.
                    preludeEligible = sample;
                    preludeCaptured = true;
                    observeArmedUpdate = null;
                    waitDiagnostics += $"\nprelude eligible: {updateDiagnostic},{association}";
                }
                else if (!ReferenceEquals(sample.Display.Session, prelude.Display)
                    || sample.Display.Current?.Phase != DialogueDisplayPhase.WaitingForAdvance
                    || !sample.Call.Equals(preludeWaiting.Call) || !sample.Bank.SequenceEqual(preludeWaiting.Bank)
                    || !sample.Random.Equals(preludeWaiting.Random) || !sample.Outer.Equals(preludeWaiting.Outer))
                {
                    waitViolation ??= $"Prelude advanced before its 100ms poll: {updateDiagnostic},{association}";
                }
            };
            waitDiagnostics = $"prelude submission: seq={updateSequence},ticks={Godot.Time.GetTicksUsec()},frame={Engine.GetProcessFrames()}";
            SendOwnedKey(true);
            SendOwnedKey(false);
            await Until(runner, () => preludeCaptured || waitViolation is not null || failure is not null);
            AssertNoFailure();
            AssertThat(preludeFirstCaptured).IsTrue();
            AssertThat(ReferenceEquals(preludeFirst.Display.Session, preludeFirst.Display.Producer)).IsTrue();
            if (!preludeFirstPoll)
            {
                AssertThat(preludeFirst.Display.Current!.Phase).IsEqual(DialogueDisplayPhase.WaitingForAdvance);
                AssertThat(preludeFirst.Call.Equals(preludeWaiting.Call) && preludeFirst.Bank.SequenceEqual(preludeWaiting.Bank)
                    && preludeFirst.Random.Equals(preludeWaiting.Random) && preludeFirst.Outer.Equals(preludeWaiting.Outer)).IsTrue();
            }
            AssertThat(ReferenceEquals(preludeEligible.Display.Session, preludeEligible.Display.Producer)).IsTrue();
            AssertThat(preludeEligible.Display.Current!.Phase).IsEqual(DialogueDisplayPhase.Completed);
            AssertThat(preludeEligible.Call.Phase).IsEqual(TriggerScriptPhase.WaitingForBattle);
            AssertThat(preludeEligible.Call.PendingDisplay is null).IsTrue();
            AssertThat(preludeEligible.Call.PendingBattle is not null).IsTrue();
            AssertThat(preludeEligible.Bank.Select(pair => (pair.Key.ObjectId, pair.Key.SlotIndex, pair.Value))
                .SequenceEqual(new[] { (0, 0, 0), (0, 1, dyingAddress), (37, 0, 0), (37, 1, 0) })).IsTrue();
            AssertBank(); // The owned prelude ACK has now executed the future literal0090, if present.
            var battle = preludeEligible.Call.PendingBattle!;
            AssertThat(ReferenceEquals(session.PendingBattle, battle)).IsTrue();
            AssertThat(battle.Caller).IsEqual(60);
            AssertThat(battle.Lifecycle.Completion is null).IsTrue();
            AssertThat((random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws).Equals((0, 0, 0, 0))).IsTrue();
            battle.Lifecycle.SelectAttack(0, 0);
            battle.Lifecycle.SelectAttack(1, 0);
            AssertThat((random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws).Equals((0, initiativeFloats, 0, initiativeFloats))).IsTrue();
            AssertThat(battle.Lifecycle.ResolveNextAction()).IsTrue();
            Guid? previousHookDisplayId = null;
            if (liveZero)
            {
                // Present zero suppresses the declared seed40 hook despite the real Dying damage.
                AssertThat(session.PendingBattleHook is null && session.RunningBattleHook is null && session.PendingDisplay is null).IsTrue();
                AssertThat(session.Phase).IsEqual(TriggerScriptPhase.WaitingForBattle);
                AssertThat(session.Caller).IsEqual(60);
                AssertThat(ReferenceEquals(session.PendingBattle, battle)).IsTrue();
                AssertThat(battle.Lifecycle.Current.Combat.Phase).IsEqual(ClassicBattlePhase.Resolving);
                AssertThat(battle.Lifecycle.Current.Roles[1].Hp).IsEqual(16);
                AssertThat(battle.Lifecycle.Current.Combat.Players[1].Hp).IsEqual(16);
                AssertThat(battle.Lifecycle.Current.Combat.Players[0].Hp).IsEqual(100);
                AssertThat(battle.Lifecycle.Current.Combat.Enemies[0].Hp).IsEqual(43);
                AssertThat(battle.Lifecycle.Completion is null).IsTrue();
                AssertThat((random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws, random.Remaining)
                    .Equals((5, 3, 5, 3, 5))).IsTrue();
                AssertBank();
            }
            for (int hookIndex = 0; !liveZero && hookIndex < (friendDeathControl ? 2 : 1); hookIndex++)
            {
                bool friendHook = hookIndex == 1;
                int expectedHp = friendHook ? 0 : 16;
                int beforeHookInts = friendHook ? 10 : 5;
                int currentHookInts = friendHook ? 10 : hookInts;
                int remaining = friendHook ? 0 : 5;
                if (friendHook)
                {
                    AssertThat(world.ObjectScripts[(37, 0)]).IsEqual(32); // Preceding real Y/0090.
                    AssertThat(battle.Lifecycle.ResolveNextAction()).IsTrue();
                    AssertThat(battle.Lifecycle.Current.Combat.LastAction!.Damage).IsEqual(16);
                    AssertThat(battle.Lifecycle.Current.Combat.LastAction.CovererPartyIndex is null).IsTrue();
                }
                var hook = session.PendingBattleHook!;
                AssertThat(hook is not null).IsTrue();
                AssertThat(hook!.Kind).IsEqual(friendHook ? ClassicBattleHookKind.FriendDeath : ClassicBattleHookKind.Dying);
                AssertThat(hook.PartyIndex).IsEqual(friendHook ? 0 : 1);
                AssertThat(hook.RoleId).IsEqual(friendHook ? 0 : 1);
                AssertThat(hook.ScriptAddress).IsEqual(friendHook ? 32 : dyingAddress);
                AssertThat(battle.Lifecycle.Current.Combat.Phase).IsEqual(ClassicBattlePhase.RoleHook);
                AssertThat(battle.Lifecycle.Current.Roles[1].Hp).IsEqual(expectedHp);
                AssertThat(battle.Lifecycle.Current.Combat.Players[1].Hp).IsEqual(expectedHp);
                AssertThat(battle.Lifecycle.Current.Combat.Players[0].Hp).IsEqual(100);
                AssertThat(battle.Lifecycle.Current.Combat.Enemies[0].Hp).IsEqual(43);
                AssertThat(battle.Lifecycle.Completion is null).IsTrue();
                AssertThat((random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws).Equals((beforeHookInts, initiativeFloats, beforeHookInts, initiativeFloats))).IsTrue();
                AssertThat(random.Remaining).IsEqual(friendHook ? 0 : randomBranch ? 6 : 5);
                trigger.StartBattleHook();
                var hookDisplay = session.PendingDisplay!;
                AssertThat(hookDisplay.Id == previousHookDisplayId).IsFalse();
                previousHookDisplayId = hookDisplay.Id;
                AssertThat(hookDisplay.Id == Guid.Empty || hookDisplay.Id == prelude.Id).IsFalse();
                AssertThat(hookDisplay.MessageReferences[0].StartMessageId).IsEqual(friendHook || randomBranch ? 67 : futureWrite ? 66 : 65);
                AssertThat(hookDisplay.Context.PortraitId).IsEqual(7);
                AssertThat(ReferenceEquals(display.Session, hookDisplay.Display)).IsTrue();
                AssertThat(ReferenceEquals(session.RunningBattleHook, hook)).IsTrue();
                AssertThat(session.Caller).IsEqual(friendHook ? 0 : 1);
                AssertThat(ReferenceEquals(session.PendingBattle, battle)).IsTrue();
                var beforePremature = hookDisplay.Display.Current;
                AssertThat(session.AcknowledgeDisplay(hookDisplay.Id)).IsFalse();
                AssertThat(session.AcknowledgeDisplay(prelude.Id)).IsFalse();
                AssertThat(ReferenceEquals(hookDisplay.Display.Current, beforePremature)).IsTrue();
                AssertThat(session.Phase).IsEqual(TriggerScriptPhase.WaitingForDisplay);
                AssertThat(ReferenceEquals(session.PendingDisplay, hookDisplay)).IsTrue();
                AssertThat(ReferenceEquals(session.PendingBattleHook, hook)).IsTrue();
                AssertBank(friendDeath: friendHook ? 32 : 0);
                AssertThat((random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws).Equals((currentHookInts, initiativeFloats, currentHookInts, initiativeFloats))).IsTrue();
                updated = null;
                await Until(runner, () => (ReferenceEquals(firstNonTypingSession, hookDisplay.Display) && firstNonTyping is not null) || failure is not null);
                AssertNoFailure();
                AssertThat(firstNonTyping!.Phase).IsEqual(DialogueDisplayPhase.WaitingForAdvance);
                AssertThat(firstNonTyping.VisibleText).IsEqual(friendHook ? "F" : randomBranch ? "G" : seedVariant || futureWrite ? "E" : "B");
                if (titleGlyph) AssertThat(firstNonTyping.TitleText).IsEqual("G:");
                var observeHook = () => (
                    Display: (display.Session, Producer: hookDisplay.Display, display.Current),
                    Call: (session.Phase, session.Caller, session.ReturnEntry, session.CallId, session.SourceRequestId,
                        session.CurrentPosition, session.PendingDisplay, session.PendingBattleHook, session.RunningBattleHook, session.PendingBattle),
                    Battle: (battle.Lifecycle.Current.Combat.Phase, battle.Lifecycle.Completion,
                        battle.Lifecycle.Current.Roles[1].Hp, battle.Lifecycle.Current.Combat.Players[0].Hp,
                        battle.Lifecycle.Current.Combat.Players[1].Hp, battle.Lifecycle.Current.Combat.Enemies[0].Hp),
                    Bank: world.ObjectScripts.OrderBy(pair => pair.Key).ToArray(),
                    Random: (random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws, random.Remaining),
                    Outer: (interaction.Phase, interaction.PendingTrigger));
                var hookWaiting = observeHook();
                var hookFirst = hookWaiting;
                var hookEligible = hookWaiting;
                bool firstCaptured = false, firstPoll = false, captured = false;
                observeArmedUpdate = poll =>
                {
                    var sample = observeHook();
                    string association = $"nodeProducer={ReferenceEquals(sample.Display.Session, sample.Display.Producer)}";
                    if (!firstCaptured)
                    {
                        hookFirst = sample;
                        firstPoll = poll;
                        firstCaptured = true;
                        waitDiagnostics += $"\nhook first: {updateDiagnostic},{association}";
                    }
                    if (poll)
                    {
                        hookEligible = sample;
                        captured = true;
                        observeArmedUpdate = null;
                        waitDiagnostics += $"\nhook eligible: {updateDiagnostic},{association}";
                    }
                    else if (!ReferenceEquals(sample.Display.Session, hookDisplay.Display)
                        || sample.Display.Current?.Phase != DialogueDisplayPhase.WaitingForAdvance
                        || !sample.Call.Equals(hookWaiting.Call) || !sample.Battle.Equals(hookWaiting.Battle)
                        || !sample.Bank.SequenceEqual(hookWaiting.Bank) || !sample.Random.Equals(hookWaiting.Random)
                        || !sample.Outer.Equals(hookWaiting.Outer))
                    {
                        waitViolation ??= $"Hook advanced before its 100ms poll: {updateDiagnostic},{association}";
                    }
                };
                waitDiagnostics += $"\nhook submission: seq={updateSequence},ticks={Godot.Time.GetTicksUsec()},frame={Engine.GetProcessFrames()}";
                SendOwnedKey(true);
                SendOwnedKey(false);
                await Until(runner, () => captured || waitViolation is not null || failure is not null);
                AssertNoFailure();
                AssertThat(firstCaptured).IsTrue();
                AssertThat(ReferenceEquals(hookFirst.Display.Session, hookFirst.Display.Producer)).IsTrue();
                if (!firstPoll)
                {
                    AssertThat(hookFirst.Display.Current!.Phase).IsEqual(DialogueDisplayPhase.WaitingForAdvance);
                    AssertThat(hookFirst.Call.Equals(hookWaiting.Call) && hookFirst.Battle.Equals(hookWaiting.Battle)
                        && hookFirst.Bank.SequenceEqual(hookWaiting.Bank) && hookFirst.Random.Equals(hookWaiting.Random)
                        && hookFirst.Outer.Equals(hookWaiting.Outer)).IsTrue();
                }
                var observedDisplay = hookEligible.Display;
                var observedCall = hookEligible.Call;
                var observedBattle = hookEligible.Battle;
                var observedBank = hookEligible.Bank;
                var observedRandom = hookEligible.Random;
                AssertThat(ReferenceEquals(observedDisplay.Session, hookDisplay.Display)).IsTrue();
                AssertThat(observedDisplay.Current!.Phase).IsEqual(DialogueDisplayPhase.Completed);
                AssertThat(observedCall.Phase).IsEqual(TriggerScriptPhase.WaitingForBattle);
                AssertThat(observedCall.Caller).IsEqual(60);
                AssertThat(observedCall.ReturnEntry).IsEqual(1);
                AssertThat(observedCall.CallId).IsEqual(callId);
                AssertThat(observedCall.SourceRequestId).IsEqual(request.Id);
                AssertThat(observedCall.CurrentPosition == (friendHook || randomBranch
                    ? new TriggerPosition("shared", "L0022", 34) : secondOrderWrite
                    ? new TriggerPosition("shared", "L001B", 27) : futureWrite
                    ? new TriggerPosition("shared", "L001A", 26) : seedVariant
                    ? new TriggerPosition("shared", "L002A", 42) : new TriggerPosition("shared", "L0016", 22))).IsTrue();
                AssertThat(observedCall.PendingDisplay is null && observedCall.PendingBattleHook is null && observedCall.RunningBattleHook is null).IsTrue();
                AssertThat(ReferenceEquals(observedCall.PendingBattle, battle)).IsTrue();
                AssertThat(observedBattle.Equals((ClassicBattlePhase.Resolving, (ClassicBattleLifecycleSession.CompletionReceipt?)null, expectedHp, 100, expectedHp, 43))).IsTrue();
                AssertThat(observedBank.Length).IsEqual(4);
                // Y's owned ACK writes the other Name/slot; only the explicit two-hit variant also executes Z.
                AssertThat(observedBank.Select(pair => (pair.Key.ObjectId, pair.Key.SlotIndex, pair.Value))
                    .SequenceEqual(new[] { (0, 0, 0), (0, 1, dyingAddress), (37, 0, secondOrderWrite ? 32 : 0), (37, 1, 0) })).IsTrue();
                AssertThat(observedRandom.Equals((currentHookInts, initiativeFloats, currentHookInts, initiativeFloats, remaining))).IsTrue();
                var beforeStale = (session.Phase, session.Caller, session.ReturnEntry, session.CallId, session.SourceRequestId,
                    session.CurrentPosition, session.PendingDisplay, session.PendingBattleHook, session.RunningBattleHook,
                    session.PendingBattle, battle.Lifecycle.Current, display.Session, display.Current);
                AssertThat(session.AcknowledgeDisplay(hookDisplay.Id)).IsFalse();
                AssertThat(beforeStale.Equals((session.Phase, session.Caller, session.ReturnEntry, session.CallId, session.SourceRequestId,
                    session.CurrentPosition, session.PendingDisplay, session.PendingBattleHook, session.RunningBattleHook,
                    session.PendingBattle, battle.Lifecycle.Current, display.Session, display.Current))).IsTrue();
                AssertBank(friendDeath: secondOrderWrite ? 32 : 0);
                AssertThat((random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws, random.Remaining).Equals((currentHookInts, initiativeFloats, currentHookInts, initiativeFloats, remaining))).IsTrue();
            }
            if (friendDeathControl)
            {
                AssertThat(battle.Lifecycle.Current.Combat.Enemies.Select(enemy => enemy.Hp).SequenceEqual(new[] { 43, 43 })).IsTrue();
                AssertThat(battle.Lifecycle.Current.Combat.Phase).IsEqual(ClassicBattlePhase.Resolving);
                AssertThat(battle.Lifecycle.Completion is null && session.PendingBattleHook is null && session.RunningBattleHook is null).IsTrue();
                AssertBank(friendDeath: 32);
                AssertThat(interaction.CompleteTrigger(request.Id, session, 0)).IsFalse();
                AssertThat(ReferenceEquals(interaction.PendingTrigger, request)).IsTrue();
                // Bounded genuine two-hook positive. No fake victory or premature outer completion.
                return;
            }
            AssertThat(interaction.Phase).IsEqual(NpcInteractionPhase.Trigger);
            AssertThat(ReferenceEquals(interaction.PendingTrigger, request)).IsTrue();
            AssertThat(interaction.CompleteTrigger(request.Id, session, 0)).IsFalse();
            AssertThat(interaction.Phase).IsEqual(NpcInteractionPhase.Trigger);
            AssertThat(ReferenceEquals(interaction.PendingTrigger, request)).IsTrue();

            // Resolve the saved player0 action, not a new round or a fabricated terminal state.
            // fight.c:3630-3662: SHORT(40*2-24*1.6+.5)+1 = 43, no critical/bonus, float1.
            AssertThat(battle.Lifecycle.ResolveNextAction()).IsTrue();
            var completion = battle.Lifecycle.Completion;
            AssertThat(completion is not null).IsTrue();
            AssertThat(battle.Lifecycle.OwnsCompletion(completion)).IsTrue();
            AssertThat(battle.Lifecycle.Current.IsInBattle).IsFalse();
            AssertThat(battle.Lifecycle.Current.Combat.Phase).IsEqual(ClassicBattlePhase.Won);
            AssertThat(battle.Lifecycle.Current.Combat.Enemies[0].Hp).IsEqual(0);
            AssertThat(battle.Lifecycle.Current.Combat.Players[0].Hp).IsEqual(100);
            AssertThat(battle.Lifecycle.Current.Combat.Players[1].Hp).IsEqual(16);
            AssertThat(battle.Lifecycle.Current.Combat.LastAction!.Damage).IsEqual(43);
            AssertThat(battle.Lifecycle.Current.Combat.LastAction.ActorIndex).IsEqual(0);
            AssertThat(battle.Lifecycle.Current.Combat.LastAction.Critical).IsFalse();
            AssertThat(battle.Lifecycle.Current.Combat.Players[0].Counts.Attack).IsEqual(1);
            AssertThat(battle.Lifecycle.Current.Combat.Players[0].Counts.Health).IsEqual(2);
            AssertThat(battle.Lifecycle.VictorySettlement!.ExperienceGranted).IsEqual(0);
            AssertThat(battle.Lifecycle.VictorySettlement.CashGranted).IsEqual(0);
            AssertThat(battle.Lifecycle.PendingBattleEndScript is null).IsTrue();
            // battle.c:1347-1350: half-missing recovery, only published to the owner by owned ACK.
            AssertThat((battle.Lifecycle.Current.Roles[0].Hp, battle.Lifecycle.Current.Roles[0].Mp,
                battle.Lifecycle.Current.Roles[1].Hp, battle.Lifecycle.Current.Roles[1].Mp).Equals((150, 35, 128, 125))).IsTrue();
            AssertThat((roles.Roles[0].Hp, roles.Roles[0].Mp, roles.Roles[1].Hp, roles.Roles[1].Mp)
                .Equals((100, 20, 60, 10))).IsTrue();
            AssertThat((random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws, random.Remaining)
                .Equals((finalInts, 4, finalInts, 4, 0))).IsTrue();
            var beforeBattleAck = (session.Phase, session.Caller, session.ReturnEntry, session.CallId,
                session.SourceRequestId, session.CurrentPosition, session.PendingBattle, session.PendingDisplay,
                battle.Lifecycle.Current, battle.Lifecycle.Completion, roles.Roles[0], roles.Roles[1],
                display.Session, display.Current, interaction.Phase, interaction.PendingTrigger);
            AssertThat(trigger.AcknowledgeBattle(battle.Id, null)).IsFalse();
            AssertThat(trigger.AcknowledgeBattle(callId, completion)).IsFalse();
            AssertThat(interaction.CompleteTrigger(request.Id, session, 0)).IsFalse();
            AssertThat(beforeBattleAck.Equals((session.Phase, session.Caller, session.ReturnEntry, session.CallId,
                session.SourceRequestId, session.CurrentPosition, session.PendingBattle, session.PendingDisplay,
                battle.Lifecycle.Current, battle.Lifecycle.Completion, roles.Roles[0], roles.Roles[1],
                display.Session, display.Current, interaction.Phase, interaction.PendingTrigger))).IsTrue();
            AssertBank(friendDeath: secondOrderWrite ? 32 : 0);

            AssertThat(trigger.AcknowledgeBattle(battle.Id, completion)).IsTrue();
            AssertThat(session.PendingBattle is null).IsTrue();
            AssertThat(session.Phase).IsEqual(TriggerScriptPhase.WaitingForDisplay);
            AssertThat((roles.Roles[0].Hp, roles.Roles[0].Mp, roles.Roles[1].Hp, roles.Roles[1].Mp)
                .Equals((150, 35, 128, 125))).IsTrue();
            AssertThat((session.BattleRoles![0].Hp, session.BattleRoles[0].Mp,
                session.BattleRoles[1].Hp, session.BattleRoles[1].Mp).Equals((150, 35, 128, 125))).IsTrue();
            var victoryDisplay = session.PendingDisplay!;
            AssertThat(victoryDisplay.Id == prelude.Id || victoryDisplay.Id == previousHookDisplayId).IsFalse();
            AssertThat(victoryDisplay.MessageReferences[0].StartMessageId).IsEqual(62);
            AssertThat(victoryDisplay.Context.PortraitId).IsEqual(8);
            AssertThat(ReferenceEquals(display.Session, victoryDisplay.Display)).IsTrue();
            AssertThat(display.Current!.Phase).IsEqual(DialogueDisplayPhase.Typing);
            AssertThat(display.Current.VisibleText).IsEqual("");
            AssertThat(trigger.AcknowledgeBattle(battle.Id, completion)).IsFalse();
            AssertThat(interaction.CompleteTrigger(request.Id, session, 0)).IsFalse();
            AssertBank(friendDeath: secondOrderWrite ? 32 : 0);

            updated = null;
            await Until(runner, () => (ReferenceEquals(firstNonTypingSession, victoryDisplay.Display) && firstNonTyping is not null) || failure is not null);
            AssertNoFailure();
            AssertThat(firstNonTyping!.Phase).IsEqual(DialogueDisplayPhase.WaitingForAdvance);
            AssertThat(firstNonTyping.VisibleText).IsEqual("B");
            var observeVictory = () => (
                Display: (display.Session, display.Current),
                Call: (session.Phase, session.Caller, session.ReturnEntry, session.CallId, session.SourceRequestId,
                    session.CurrentPosition, session.PendingDisplay, session.PendingBattle),
                Bank: world.ObjectScripts.OrderBy(pair => pair.Key).ToArray(),
                Roles: (roles.Roles[0], roles.Roles[1]),
                Random: (random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws, random.Remaining),
                Outer: (interaction.Phase, interaction.PendingTrigger));
            var victoryWaiting = observeVictory();
            var victoryFirst = victoryWaiting;
            var victoryEligible = victoryWaiting;
            bool victoryFirstCaptured = false, victoryFirstPoll = false, victoryCaptured = false;
            observeArmedUpdate = poll =>
            {
                var sample = observeVictory();
                if (!victoryFirstCaptured)
                {
                    victoryFirst = sample;
                    victoryFirstPoll = poll;
                    victoryFirstCaptured = true;
                    waitDiagnostics += $"\nvictory first: {updateDiagnostic}";
                }
                if (poll)
                {
                    // FIRST eligible completion update, regardless of whether its state is correct.
                    victoryEligible = sample;
                    victoryCaptured = true;
                    observeArmedUpdate = null;
                    waitDiagnostics += $"\nvictory eligible: {updateDiagnostic}";
                }
                else if (!ReferenceEquals(sample.Display.Session, victoryDisplay.Display)
                    || sample.Display.Current?.Phase != DialogueDisplayPhase.WaitingForAdvance
                    || !sample.Call.Equals(victoryWaiting.Call) || !sample.Bank.SequenceEqual(victoryWaiting.Bank)
                    || !sample.Roles.Equals(victoryWaiting.Roles) || !sample.Random.Equals(victoryWaiting.Random)
                    || !sample.Outer.Equals(victoryWaiting.Outer))
                {
                    waitViolation ??= $"Victory advanced before its 100ms poll: {updateDiagnostic}";
                }
            };
            SendOwnedKey(true);
            SendOwnedKey(false);
            await Until(runner, () => victoryCaptured || waitViolation is not null || failure is not null);
            AssertNoFailure();
            AssertThat(victoryFirstCaptured).IsTrue();
            AssertThat(ReferenceEquals(victoryFirst.Display.Session, victoryDisplay.Display)).IsTrue();
            if (!victoryFirstPoll)
            {
                AssertThat(victoryFirst.Display.Current!.Phase).IsEqual(DialogueDisplayPhase.WaitingForAdvance);
                AssertThat(victoryFirst.Call.Equals(victoryWaiting.Call) && victoryFirst.Bank.SequenceEqual(victoryWaiting.Bank)
                    && victoryFirst.Roles.Equals(victoryWaiting.Roles) && victoryFirst.Random.Equals(victoryWaiting.Random)
                    && victoryFirst.Outer.Equals(victoryWaiting.Outer)).IsTrue();
            }
            AssertThat(ReferenceEquals(victoryEligible.Display.Session, victoryDisplay.Display)).IsTrue();
            AssertThat(victoryEligible.Display.Current!.Phase).IsEqual(DialogueDisplayPhase.Completed);
            AssertThat(victoryEligible.Call.Phase).IsEqual(TriggerScriptPhase.Completed);
            AssertThat(victoryEligible.Call.Caller).IsEqual(60);
            AssertThat(victoryEligible.Call.ReturnEntry).IsEqual(1);
            AssertThat(victoryEligible.Call.CallId).IsEqual(callId);
            AssertThat(victoryEligible.Call.SourceRequestId).IsEqual(request.Id);
            AssertThat(victoryEligible.Call.PendingDisplay is null && victoryEligible.Call.PendingBattle is null).IsTrue();
            AssertThat(victoryEligible.Bank.SequenceEqual(victoryWaiting.Bank)).IsTrue();
            AssertThat(victoryEligible.Roles.Equals(victoryWaiting.Roles)).IsTrue();
            AssertThat(victoryEligible.Random.Equals((finalInts, 4, finalInts, 4, 0))).IsTrue();
            AssertThat(victoryEligible.Outer.Phase).IsEqual(NpcInteractionPhase.Trigger);
            AssertThat(ReferenceEquals(victoryEligible.Outer.PendingTrigger, request)).IsTrue();
            if (futureWrite)
                AssertThat(session.CurrentPosition == new TriggerPosition("shared", "L0007", 7)).IsTrue();
            AssertThat(session.BlocksSceneUpdates).IsFalse();
            AssertThat(session.AcknowledgeDisplay(victoryDisplay.Id)).IsFalse();
            AssertThat(trigger.AcknowledgeBattle(battle.Id, completion)).IsFalse();
            AssertBank(friendDeath: secondOrderWrite ? 32 : 0);
            AssertThat(interaction.CompleteTrigger(request.Id, session, 0)).IsTrue();
            AssertThat(interaction.PendingTrigger is null).IsTrue();
            AssertThat(interaction.Phase).IsEqual(NpcInteractionPhase.Returning);
            AssertThat(interaction.CompleteTrigger(request.Id, session, 0)).IsFalse();
            AssertThat(interaction.Update(49)).IsFalse();
            AssertThat(interaction.Phase).IsEqual(NpcInteractionPhase.Returning);
            // Return protection expires at50, but the existing exploration100ms poll is not due.
            AssertThat(interaction.Update(50)).IsFalse();
            AssertThat(interaction.Phase).IsEqual(NpcInteractionPhase.Exploring);
            AssertThat(interaction.Update(100)).IsTrue();
            AssertThat(interaction.BlocksSceneUpdates).IsFalse();
            AssertThat(interaction.PendingTrigger is null).IsTrue();
            AssertThat(interaction.Objects.Single(obj => obj.Id == 60).TriggerEntry).IsEqual(1);
            AssertThat((session.Caller, session.ReturnEntry, session.CallId, session.SourceRequestId)
                .Equals((60, 1, callId, (Guid?)request.Id))).IsTrue();
            AssertThat((roles.Roles[0].Hp, roles.Roles[0].Mp, roles.Roles[1].Hp, roles.Roles[1].Mp)
                .Equals((150, 35, 128, 125))).IsTrue();
            AssertBank(friendDeath: secondOrderWrite ? 32 : 0);
            AssertThat((random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws, random.Remaining)
                .Equals((finalInts, 4, finalInts, 4, 0))).IsTrue();
            if (bankMode == "warmup")
            {
                interaction.PressSearch();
                AssertThat(interaction.Update(200)).IsTrue();
                var next = interaction.PendingTrigger ?? throw new InvalidOperationException("Missing real post-warmup Story search.");
                AssertThat(next.Id == Guid.Empty || next.Id == request.Id).IsFalse();
                AssertThat(next.Caller).IsEqual(60);
                AssertThat(next.SourceAddress).IsEqual(1);
                var glyph = story.Font.GetGlyph(69);
                story.Font.Glyphs.Remove(glyph);
                var CaptureWarmup = () => (
                    Call: (session.Phase, session.BlocksSceneUpdates, session.CallId, session.SourceRequestId,
                        session.StartedEntry, session.Caller, session.ReturnEntry, session.CurrentPosition, session.Context,
                        session.ScriptSuccess, session.Music, session.FadedToRed, session.PendingDisplay, session.PendingBattle,
                        session.PendingLostReload, session.PendingSceneWait, session.PendingMovement, session.PendingTransition,
                        session.PendingBattleHook, session.RunningBattleHook, session.RunningBattleEndScript, session.BattleRoles),
                    World: (world.SceneId, world.Leader, world.Battlefield, world.RoleLiveState, world.PartyRoles, world.Objects),
                    Words: System.Text.Json.JsonSerializer.Serialize(new {
                        Roles = roles.Roles, Objects = world.Objects, world.PartyRoles, world.PartyVitals,
                        Bank = world.ObjectScripts.OrderBy(pair => pair.Key).Select(pair => new { Key = pair.Key.ToString(), pair.Value }),
                        Scenes = world.SceneScripts.OrderBy(pair => pair.Key).Select(pair => new { Key = pair.Key.ToString(), pair.Value }) }),
                    Role0: roles.Roles[0], Role1: roles.Roles[1],
                    Display: (display.Session, display.Current, firstNonTypingSession, firstNonTyping),
                    Nodes: string.Join(",", display.GetChildren().Concat(trigger.GetChildren()).Select(node => node.GetInstanceId())),
                    Surface: surface.GetRenderedPixel(0, 0),
                    Outer: (interaction.Current, interaction.Phase, interaction.PendingTrigger, interaction.BlocksSceneUpdates),
                    Random: (random.IntCalls, random.FloatCalls, random.IntDraws, random.FloatDraws, random.Remaining),
                    Events: (updateSequence, palettes, restores, failure));
                void RejectStart(Action operation, Type exceptionType, string diagnostic)
                {
                    var before = CaptureWarmup();
                    Exception? rejected = null;
                    try { operation(); } catch (Exception error) { rejected = error; }
                    AssertThat(rejected is not null && rejected.GetType() == exceptionType).IsTrue();
                    AssertThat(rejected!.Message.Contains(diagnostic, StringComparison.Ordinal)).IsTrue();
                    AssertThat(CaptureWarmup().Equals(before)).IsTrue();
                }
                RejectStart(() => trigger.Start(next.FileId, next.EntryId, 65536, Guid.Empty), typeof(ArgumentException),
                    "A source request ID must be nonempty.");
                RejectStart(() => trigger.Start("shared", "setter", 65536, next.Id), typeof(InvalidDataException),
                    "Story adapter only supports its checked selected entry.");
                RejectStart(() => trigger.Start(next.FileId, next.EntryId, 65536), typeof(InvalidOperationException),
                    "A story trigger requires its source interaction request.");
                RejectStart(() => trigger.Start(next.FileId, next.EntryId, 65536, next.Id), typeof(ArgumentOutOfRangeException), "caller");
                RejectStart(() => trigger.Start(next.FileId, next.EntryId, 65535, next.Id), typeof(InvalidDataException), "no glyph for U+0045");
                display.Start(files.English, story.Font, story.Portraits[7], story.Prompts.ToArray(),
                    story.Palette.ToArray(), 61, 0, 7, 0, 0);
                RejectStart(() => trigger.Start("shared", "missing-entry", 65536, next.Id), typeof(InvalidOperationException),
                    "The display is already in use.");
                RejectStart(() => trigger.Start(next.FileId, next.EntryId, 65536), typeof(InvalidOperationException),
                    "The display is already in use.");
                RejectStart(() => trigger.Start("shared", "missing-entry", 65536, Guid.Empty), typeof(ArgumentException),
                    "A source request ID must be nonempty.");
                await Until(runner, () => display.Current?.Phase == DialogueDisplayPhase.WaitingForAdvance || failure is not null);
                AssertNoFailure();
                SendOwnedKey(true); SendOwnedKey(false);
                await Until(runner, () => display.Current?.Phase == DialogueDisplayPhase.Completed || failure is not null);
                AssertNoFailure();
                story.Font.Glyphs.Add(glyph);
                trigger.Start(next.FileId, next.EntryId, 65535, next.Id);
                // fight.c:875-876 uses the Dying role1; no enemy battle-end trigger ran in this fixture.
                // script.c:3162-3182 keeps that nested caller globally, not the restored outer owner60.
                AssertThat(session.Caller).IsEqual(1);
                AssertThat(session.CallId == callId || session.CallId == Guid.Empty).IsFalse();
                AssertThat(session.SourceRequestId).IsEqual(next.Id);
                var beforeBind = CaptureWarmup();
                AssertThat(interaction.BindTrigger(next.Id, session)).IsFalse(); // Genuine owner60 request cannot bind role1.
                AssertThat(CaptureWarmup().Equals(beforeBind)).IsTrue();
                AssertThat(session.PendingDisplay!.MessageReferences[0].StartMessageId).IsEqual(61);
                AssertThat(session.PendingDisplay.Display.Current.Phase).IsEqual(DialogueDisplayPhase.Typing);
                RejectStart(() => trigger.Start("shared", "missing-entry", 65536, next.Id), typeof(InvalidOperationException),
                    "A trigger call is already active.");
                RejectStart(() => trigger.Start("shared", "missing-entry", 65536, Guid.Empty), typeof(ArgumentException),
                    "A source request ID must be nonempty.");
            }
        }
        catch (Exception error)
        {
            throw new InvalidOperationException($"Story positive wait observations: {waitDiagnostics}\nOriginal failure: {error}", error);
        }
        finally
        {
            SendOwnedKey(false);
            observeArmedUpdate = null;
            display.UpdateCompleted -= OnUpdate;
            display.Failed -= OnFailure;
            display.ActivePaletteChanged -= OnPalette;
            display.BackgroundRestoreRequested -= OnRestore;
            var ownedNodes = display.GetChildren().Concat(trigger.GetChildren()).ToArray();
            runner.Dispose();
            AssertThat(GodotObject.IsInstanceValid(display)).IsFalse();
            AssertThat(GodotObject.IsInstanceValid(trigger)).IsFalse();
            foreach (var node in ownedNodes) AssertThat(GodotObject.IsInstanceValid(node)).IsFalse();
        }
    }

    private sealed class CurrentHookPositiveRandom : IBattleRandom
    {
        // Independent Queue3 + EnemyHit1 literals; do not replay another executor.
        private readonly (bool Float, float From, float To, float Value)[] draws =
        [
            (true, .9f, 1.1f, 1), (true, .9f, 1.1f, 1), (true, .9f, 1.1f, 1),
            (false, 0, 1, 1), (false, 0, 16, 0), (false, 0, 2, 0), (false, 0, 1, 0), (false, 1, 10, 1),
            // Saved player0 strike: damage43 kills the one43HP enemy, then health count+2.
            (false, 1, 2, 1), (false, 0, 5, 1), (false, 0, 11, 11), (true, 1, 1.125f, 1), (false, 2, 3, 2)
        ];
        public CurrentHookPositiveRandom((bool Float, float From, float To, float Value)[]? orderedDraws = null,
            bool randomTarget = false)
        {
            if (orderedDraws is not null) draws = orderedDraws;
            if (randomTarget)
            {
                // One actual hook draw after initiative + enemy hit: Story8 / first-battle9.
                int hookOffset = orderedDraws is null ? 8 : 9;
                draws = [.. draws[..hookOffset], (false, 1, 100, 50), .. draws[hookOffset..]];
            }
        }
        private int next;
        public int IntCalls { get; private set; }
        public int FloatCalls { get; private set; }
        public int IntDraws { get; private set; }
        public int FloatDraws { get; private set; }
        public int Remaining => draws.Length - next;
        public int NextInclusive(int from, int to)
        {
            IntCalls++;
            if (from == to) return from;
            IntDraws++;
            return (int)Take(false, from, to);
        }
        public float NextInclusive(float from, float to)
        {
            FloatCalls++;
            if (from == to) return from;
            FloatDraws++;
            return Take(true, from, to);
        }
        private float Take(bool floating, float from, float to)
        {
            if (next >= draws.Length || draws[next].Float != floating || draws[next].From != from || draws[next].To != to)
                throw new InvalidOperationException($"Unexpected Story positive RNG draw {next}: {floating}/{from}..{to}.");
            return draws[next++].Value;
        }
    }

    private sealed class CountingStoryRandom : IBattleRandom
    {
        public int IntCalls { get; private set; }
        public int FloatCalls { get; private set; }
        public int IntDraws { get; private set; }
        public int FloatDraws { get; private set; }
        public int NextInclusive(int from, int to)
        {
            IntCalls++;
            if (from != to) IntDraws++;
            return from;
        }
        public float NextInclusive(float from, float to)
        {
            FloatCalls++;
            if (from != to) FloatDraws++;
            return from;
        }
    }

    private static void AssertStartRejection<T>(Func<T> capture, Action operation, Type exceptionType, string diagnostic)
    {
        T before = capture();
        Exception? rejected = null;
        try { operation(); } catch (Exception error) { rejected = error; }
        if (rejected is null || rejected.GetType() != exceptionType)
            throw new InvalidOperationException($"Expected {exceptionType.Name}: {diagnostic}; actual: {rejected}", rejected);
        AssertThat(rejected.Message.Contains(diagnostic, StringComparison.Ordinal)).IsTrue();
        AssertThat(Equals(capture(), before)).IsTrue();
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
