using System;
using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Godot.Collections;
using Pal.Core;
using Pal.Game.Dialogue;
using Pal.Game.Resources;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

[TestSuite, RequireGodotRuntime, GodotExceptionMonitor]
public class DialogueDisplayFixtureTests
{
    [TestCase(Timeout = 15000)]
    public async Task Native_language_resources_render_wait_for_input_and_recover_in_default_and_explicit_english()
    {
        using var runner = ISceneRunner.Load("res://Tests/DialogueDisplayFixture.tscn", true);
        var display = runner.Scene() as PalDialogueDisplay;
        AssertThat(display is not null).IsTrue();
        var chinese = Load<PalDialogueLanguage>("DialogueSyntheticZh.tres");
        var english = Load<PalDialogueLanguage>("DialogueSyntheticEn.tres");
        var selection = new DialogueLanguageSelection();
        AssertThat(selection.Language).IsEqual("zh-Hans");
        var font = Load<PalDialogueFont>("DialogueSyntheticFont.tres");
        var portrait = Load<PalDialogueIndexedImage>("DialogueSyntheticPortrait.tres");
        var prompt0 = Load<PalDialogueIndexedImage>("DialogueSyntheticPrompt.tres");
        var prompt1 = Load<PalDialogueIndexedImage>("DialogueSyntheticPrompt1.tres");
        var prompt2 = Load<PalDialogueIndexedImage>("DialogueSyntheticPrompt2.tres");
        var prompts = new[] { prompt0, prompt1, prompt2 };
        AssertThat(prompts.Select(prompt => prompt.PaletteIndices[0]).Distinct().Count()).IsEqual(3);
        var palette = Enumerable.Repeat(Colors.White, 256).ToArray();
        palette[10] = Colors.Yellow; palette[11] = Colors.Magenta; palette[12] = Colors.Cyan;
        palette[0xF9] = Colors.Red; palette[0xFA] = Colors.Green; palette[0xFB] = Colors.Blue;

        var surface = runner.Scene()!.GetNode<PalDialoguePaletteSurface>("IndexedBackground");
        surface.SetImage(Load<PalDialogueIndexedImage>("DialogueSyntheticBackground.tres"));
        surface.SetPalette(palette);
        display!.ActivePaletteChanged += surface.SetPalette;
        AssertThat(selection.Select(chinese.ToCorePackage(), english.ToCorePackage()).Language).IsEqual("zh-Hans");
        display.Start(chinese, font, portrait, prompts, palette, 61, 1, 59, 0, 79);
        await WaitFor(runner, display, DialogueDisplayPhase.WaitingForAdvance);
        AssertThat(display.Current!.VisibleText).IsEqual("A");
        AssertThat(display.Current.Color).IsEqual(79);
        AssertThat(display.Current.PromptFrame).IsEqual(1);
        for (int frame = 0; frame < 120 && display.Current!.PaletteRotation == 0; frame++)
            await runner.SimulateFrames(1);
        AssertThat(display.Current!.PaletteRotation >= 1 && display.Current.PaletteRotation <= 5).IsTrue();
        AssertThat(surface.GetRenderedPixel(0, 0)).IsEqual(Colors.Green);
        await Press(runner, Key.Space, true);
        await WaitFor(runner, display, DialogueDisplayPhase.Completed);
        AssertThat(surface.GetRenderedPixel(0, 0)).IsEqual(Colors.Red);
        AssertThat(display.Current!.Phase).IsEqual(DialogueDisplayPhase.Completed);
        AssertThat(display.Current.ClearDisplay).IsTrue();
        AssertThat(display.Current.PaletteRotation).IsEqual(0);

        await Press(runner, Key.Space, false);
        AssertThat(new DialogueLanguageSelection("en").Select(chinese.ToCorePackage(), english.ToCorePackage()).Language).IsEqual("en");
        display.Start(english,
            font, portrait, prompts, palette, 61, 1, 59, 0, 79);
        await WaitFor(runner, display, DialogueDisplayPhase.WaitingForAdvance);
        AssertThat(display.Current!.VisibleText).IsEqual("B");
        AssertThat(display.Current.PromptFrame).IsEqual(2);
        await Press(runner, Key.Escape, true);
        AssertThat(display.Current!.Phase).IsEqual(DialogueDisplayPhase.Completed);
        await Press(runner, Key.Escape, false);
    }

    [TestCase(Timeout = 15000)]
    public async Task Native_speaker_title_stays_separate_from_body_and_is_cleared_on_completion()
    {
        using var runner = ISceneRunner.Load("res://Tests/DialogueDisplayFixture.tscn", true);
        var display = (PalDialogueDisplay)runner.Scene()!;
        display.Start(Load<PalDialogueLanguage>("DialogueSyntheticTitle.tres"),
            Load<PalDialogueFont>("DialogueSyntheticFont.tres"),
            Load<PalDialogueIndexedImage>("DialogueSyntheticPortrait.tres"),
            new[] { Load<PalDialogueIndexedImage>("DialogueSyntheticPrompt.tres"),
                Load<PalDialogueIndexedImage>("DialogueSyntheticPrompt1.tres"),
                Load<PalDialogueIndexedImage>("DialogueSyntheticPrompt2.tres") },
            Enumerable.Repeat(Colors.White, 256).ToArray(), 61, 1, 59, 0, 0x4F);
        AssertThat(display.Current!.TitleText).IsEqual("AB:");
        AssertThat(display.Current.VisibleLines.Count).IsEqual(0);
        await WaitFor(runner, display, DialogueDisplayPhase.WaitingForAdvance);
        AssertThat(display.Current!.TitleText).IsEqual("AB:");
        AssertThat(display.Current.VisibleText).IsEqual("B");
        AssertThat(display.Current.VisibleLines.Count).IsEqual(1);
        AssertThat(display.Current.VisibleLines[0].LineIndex).IsEqual(0);
        AssertThat(display.Current.PromptFrame).IsEqual(1);
        await Press(runner, Key.Space, true);
        AssertThat(display.Current!.Phase).IsEqual(DialogueDisplayPhase.Completed);
        AssertThat(display.Current.TitleText).IsEqual("");
        AssertThat(display.Current.VisibleLines.Count).IsEqual(0);
        await Press(runner, Key.Space, false);
    }

    [TestCase(Timeout = 15000)]
    public void Tracked_synthetic_resources_load_as_native_dialogue_resources()
    {
        var language = Load<PalDialogueLanguage>("DialogueSyntheticZh.tres");
        var font = Load<PalDialogueFont>("DialogueSyntheticFont.tres");
        var portrait = Load<PalDialogueIndexedImage>("DialogueSyntheticPortrait.tres");
        var prompt = Load<PalDialogueIndexedImage>("DialogueSyntheticPrompt.tres");
        AssertThat(language.ToCorePackage().Resolve(61, 1).Parts[0].Text).IsEqual("A)");
        AssertThat(font.GetGlyph('A').Width).IsEqual(8);
        portrait.Validate(); prompt.Validate();
    }

    [TestCase(Timeout = 15000)]
    public async Task Starting_search_held_before_attachment_does_not_accelerate_the_first_actual_update()
    {
        using var runner = ISceneRunner.Load("res://Tests/DialogueDisplayFixture.tscn", true);
        var display = (PalDialogueDisplay)runner.Scene()!;
        Send(Key.Space, true);
        try
        {
            StartText(display, "$99" + new string('A', 100));
            DialogueDisplaySnapshot? first = null;
            display.UpdateCompleted += () => first ??= display.Current;
            for (int frame = 0; frame < 120 && first is null; frame++) await runner.SimulateFrames(1);
            AssertThat(first is not null).IsTrue();
            AssertThat(first!.Phase).IsEqual(DialogueDisplayPhase.Typing);
            // Exact glyph deadlines belong to deterministic Core tests. This actual first
            // update must remain typing even if external scheduling was late; 100*$99
            // cannot naturally finish within the unchanged15s test liveness bound.
            AssertThat(first.VisibleText.Length > 0 && first.VisibleText.Length < 100).IsTrue();
        }
        finally { Send(Key.Space, false); }
    }

    [TestCase(Timeout = 15000)]
    public async Task Fresh_short_tap_latches_skip_across_body_lines_without_consuming_the_wait()
    {
        using var runner = ISceneRunner.Load("res://Tests/DialogueDisplayFixture.tscn", true);
        var display = (PalDialogueDisplay)runner.Scene()!;
        StartText(display, "$12" + new string('A', 100), "BB");
        DialogueDisplaySnapshot? firstLineCompleted = null, nextUpdate = null;
        bool tapped = false;
        display.UpdateCompleted += () =>
        {
            if (!tapped) { tapped = true; Send(Key.Space, true); Send(Key.Space, false); }
            else if (firstLineCompleted is not null) nextUpdate ??= display.Current;
            else if (display.Current is { Phase: DialogueDisplayPhase.Typing, LineIndex: 1 }) firstLineCompleted = display.Current;
        };
        try
        {
            for (int frame = 0; frame < 120 && nextUpdate is null; frame++) await runner.SimulateFrames(1);
            AssertThat(firstLineCompleted is not null && nextUpdate is not null).IsTrue();
            AssertThat(firstLineCompleted!.VisibleLines[0].Runs[0].Text).IsEqual(new string('A', 100));
            AssertThat(nextUpdate!.Phase).IsEqual(DialogueDisplayPhase.WaitingForAdvance);
            AssertThat(nextUpdate.VisibleText).IsEqual("BB");
            await runner.SimulateFrames(2);
            AssertThat(display.Current!.Phase).IsEqual(DialogueDisplayPhase.WaitingForAdvance);
        }
        finally { Send(Key.Space, false); }
    }

    [TestCase(Key.Up, Timeout = 15000)]
    [TestCase(Key.Pageup, Timeout = 15000)]
    public async Task Mapped_non_confirm_short_press_advances_the_wait_on_a_real_update(Key key)
    {
        using var runner = ISceneRunner.Load("res://Tests/DialogueDisplayFixture.tscn", true);
        var display = (PalDialogueDisplay)runner.Scene()!;
        StartText(display, "A");
        DialogueDisplaySnapshot? updated = null;
        display.UpdateCompleted += () => updated = display.Current;
        await WaitFor(runner, display, DialogueDisplayPhase.WaitingForAdvance);
        Send(key, true); Send(key, false);
        try
        {
            for (int frame = 0; frame < 120 && updated?.Phase != DialogueDisplayPhase.Completed; frame++) await runner.SimulateFrames(1);
            AssertThat(updated is not null).IsTrue();
            AssertThat(updated!.Phase).IsEqual(DialogueDisplayPhase.Completed);
        }
        finally { Send(key, false); }
    }

    [TestCase(Timeout = 15000)]
    public async Task Unmapped_echo_and_right_control_do_not_advance_but_left_control_does()
    {
        using var runner = ISceneRunner.Load("res://Tests/DialogueDisplayFixture.tscn", true);
        var display = (PalDialogueDisplay)runner.Scene()!;
        StartText(display, "A");
        DialogueDisplaySnapshot? updated = null;
        display.UpdateCompleted += () => updated = display.Current;
        await WaitFor(runner, display, DialogueDisplayPhase.WaitingForAdvance);
        foreach (var key in new[] { (Key.Shift, false, KeyLocation.Unspecified), (Key.Ctrl, false, KeyLocation.Right),
            (Key.Up, true, KeyLocation.Unspecified), (Key.Space, true, KeyLocation.Unspecified) })
        {
            int rotation = updated!.PaletteRotation;
            Send(key.Item1, true, key.Item2, key.Item3); Send(key.Item1, false, false, key.Item3);
            for (int frame = 0; frame < 120 && updated.Phase == DialogueDisplayPhase.WaitingForAdvance
                && updated.PaletteRotation == rotation; frame++) await runner.SimulateFrames(1);
            AssertThat(updated.Phase).IsEqual(DialogueDisplayPhase.WaitingForAdvance);
            AssertThat(updated.PaletteRotation != rotation).IsTrue();
        }
        Send(Key.Ctrl, true, false, KeyLocation.Left); Send(Key.Ctrl, false, false, KeyLocation.Left);
        await WaitFor(runner, display, DialogueDisplayPhase.Completed);
        AssertThat(updated!.Phase).IsEqual(DialogueDisplayPhase.Completed);
    }

    private static void StartText(PalDialogueDisplay display, params string[] lines)
    {
        var language = new PalDialogueLanguage { Language = "en", SourceIdentity = "reference-input-fixture",
            Messages = new Array<PalDialogueMessageRange> { new() { StartMessageId = 61, EndMessageId = 62,
                Entries = new Array<PalDialogueMessageEntry>(lines.Select(text => new PalDialogueMessageEntry { Text = text })) } } };
        display.Start(language, Load<PalDialogueFont>("DialogueSyntheticFont.tres"),
            Load<PalDialogueIndexedImage>("DialogueSyntheticPortrait.tres"),
            new[] { Load<PalDialogueIndexedImage>("DialogueSyntheticPrompt.tres"), Load<PalDialogueIndexedImage>("DialogueSyntheticPrompt1.tres"), Load<PalDialogueIndexedImage>("DialogueSyntheticPrompt2.tres") },
            Enumerable.Repeat(Colors.White, 256).ToArray(), 61, 1, 59, 0, 79);
    }

    private static void Send(Key key, bool pressed, bool echo = false, KeyLocation location = KeyLocation.Unspecified)
    {
        using var input = new InputEventKey { Keycode = key, Pressed = pressed, Echo = echo, Location = location };
        Input.ParseInputEvent(input); Input.FlushBufferedEvents();
    }

    private static async Task WaitFor(ISceneRunner runner, PalDialogueDisplay display, DialogueDisplayPhase phase)
    {
        for (int frame = 0; frame < 120 && display.Current?.Phase != phase; frame++)
            await runner.SimulateFrames(1);
        AssertThat(display.Current?.Phase).IsEqual(phase);
    }

    private static async Task Press(ISceneRunner runner, Key key, bool pressed)
    {
        using var input = new InputEventKey { Keycode = key, Pressed = pressed, Echo = false };
        Input.ParseInputEvent(input);
        var display = (PalDialogueDisplay)runner.Scene()!;
        if (pressed) await WaitFor(runner, display, DialogueDisplayPhase.Completed);
        else await runner.SimulateFrames(2);
    }

    private static T Load<T>(string name) where T : Resource =>
        ResourceLoader.Load<T>($"res://Tests/Fixtures/{name}")
        ?? throw new InvalidOperationException($"Synthetic dialogue fixture is missing: {name}");
}
