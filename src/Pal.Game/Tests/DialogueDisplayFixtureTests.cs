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
        var chinese = Language("zh-Hans", "A");
        var english = Language("en", "B");
        var selection = new DialogueLanguageSelection();
        AssertThat(selection.Language).IsEqual("zh-Hans");
        var font = Font('A', 'B');
        var portrait = IndexedImage();
        var prompt = IndexedImage();
        var palette = Enumerable.Repeat(Colors.White, 256).ToArray();

        display!.Start(selection.Select(chinese, english), font, portrait, prompt, palette, 61, 0, 59, 0, 79);
        await WaitFor(runner, display, DialogueDisplayPhase.WaitingForAdvance);
        AssertThat(display.Current!.VisibleText).IsEqual("A");
        AssertThat(display.Current.Color).IsEqual(79);
        for (int frame = 0; frame < 120 && display.Current!.PaletteRotation == 0; frame++)
            await runner.SimulateFrames(1);
        AssertThat(display.Current!.PaletteRotation >= 1 && display.Current.PaletteRotation <= 5).IsTrue();
        await Press(runner, Key.Space, true);
        AssertThat(display.Current!.Phase).IsEqual(DialogueDisplayPhase.Completed);
        AssertThat(display.Current.ClearDisplay).IsTrue();
        AssertThat(display.Current.PaletteRotation).IsEqual(0);

        await Press(runner, Key.Space, false);
        display.Start(new DialogueLanguageSelection("en").Select(chinese, english),
            font, portrait, prompt, palette, 61, 0, 59, 0, 79);
        await WaitFor(runner, display, DialogueDisplayPhase.WaitingForAdvance);
        AssertThat(display.Current!.VisibleText).IsEqual("B");
        await Press(runner, Key.Escape, true);
        AssertThat(display.Current!.Phase).IsEqual(DialogueDisplayPhase.Completed);
        await Press(runner, Key.Escape, false);
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
        await runner.SimulateFrames(2);
    }

    private static PalDialogueLanguage Language(string language, string text)
    {
        var range = new PalDialogueMessageRange { StartMessageId = 61, EndMessageId = 61 };
        range.Entries.Add(new PalDialogueMessageEntry { Kind = PalDialogueMessageEntry.EntryKind.Text, Text = text });
        var package = new PalDialogueLanguage { Language = language };
        package.Messages.Add(range);
        return package;
    }

    private static PalDialogueFont Font(params int[] codepoints)
    {
        var font = new PalDialogueFont();
        foreach (int codepoint in codepoints)
            font.Glyphs.Add(new PalDialogueGlyph
            {
                Codepoint = codepoint,
                Width = 8,
                Height = 15,
                RowBytes = 1,
                Bitmap = Enumerable.Repeat((byte)0x7e, 15).ToArray()
            });
        return font;
    }

    private static PalDialogueIndexedImage IndexedImage() => new()
    {
        Width = 1,
        Height = 1,
        PaletteIndices = new byte[] { 1 },
        Alpha = new byte[] { 255 }
    };
}
