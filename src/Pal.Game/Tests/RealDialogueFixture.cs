using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Godot;
using Pal.Core;
using Pal.Game.Dialogue;
using Pal.Game.Resources;

namespace Pal.Game.Tests;

// Explicit local GPU acceptance entry; never selected by Main or the headless test suite.
public partial class RealDialogueFixture : Node
{
    private PalDialogueDisplay _display = null!;
    private PalDialogueLanguage _language = null!;
    private PalDialogueFont _font = null!;
    private PalDialogueIndexedImage _portrait = null!;
    private PalDialogueIndexedImage[] _prompts = [];
    private DialogueTextLine[] _lines = [];
    private string _title = "";
    private DialogueDisplaySnapshot? _observed;
    private ulong _updateFrame;
    private ulong _lastRenderedUpdate = ulong.MaxValue;
    private bool _subscribed;
    private Color[] _palette = [];
    private byte[] _background = [];
    private string _evidence = "";
    private string _languageId = "";
    private readonly List<object> _checks = [];
    private int _stage;
    private int _failures;
    private int _waitingRotation;
    private ulong _started;
    private bool _finished;

    public override void _Ready()
    {
        if (OS.GetCmdlineUserArgs().Contains("--m3-dialogue-profile")) { ReadyInteraction(); return; }
        try
        {
            Require(DisplayServer.GetName() != "headless", "This fixture requires a graphical renderer.");
            _evidence = RequiredEnvironment("PAL_DIALOGUE_GPU_EVIDENCE");
            Require(Directory.Exists(_evidence), "Evidence directory must already exist.");
            _languageId = RequiredEnvironment("PAL_DIALOGUE_LANGUAGE");
            Require(_languageId is "zh-Hans" or "en", "Explicit language must be zh-Hans or en.");
            string root = RequiredEnvironment("PAL_DIALOGUE_NATIVE_ROOT").TrimEnd('/');
            _language = Load<PalDialogueLanguage>($"{root}/Language.{_languageId}.tres");
            _font = Load<PalDialogueFont>($"{root}/Font.tres");
            _portrait = Load<PalDialogueIndexedImage>($"{root}/Portrait-059.tres");
            _prompts = Enumerable.Range(0, 3).Select(i => Load<PalDialogueIndexedImage>($"{root}/Prompt-{i:00}.tres")).ToArray();
            var nativePalette = Load<PalPalette>(RequiredEnvironment("PAL_DIALOGUE_PALETTE"));
            Require(nativePalette.PaletteId == 0 && nativePalette.SelectedLighting == "day" && nativePalette.Rgb.Length == 768,
                "This local acceptance explicitly selects palette 0/day.");
            _palette = Enumerable.Range(0, 256).Select(i => new Color(nativePalette.Rgb[i * 3] / 255f,
                nativePalette.Rgb[i * 3 + 1] / 255f, nativePalette.Rgb[i * 3 + 2] / 255f)).ToArray();
            var block = _language.ToCorePackage().Resolve(61, 1);
            Require(block.Parts.Count == 2 && block.Parts.All(part => !part.IsClear && part.Text is not null),
                "Real target must retain its independently established two text parts.");
            // Source text.c recognizes a colon-terminated first message as a title, not body line 0.
            string first = block.Parts[0].Text!;
            Require(first.Length > 0 && first[^1] is ':' or '\uFF1A' or '\u2236', "Real target must begin with its speaker title.");
            // PAL_UnescapeText only strips unescaped controls when at least one backslash exists.
            // Keep the oracle separate from the production scanner.
            _title = first.Contains('\\')
                ? System.Text.RegularExpressions.Regex.Replace(first, @"\\(.)|[-'@""$~)(]",
                    match => match.Groups[1].Value, System.Text.RegularExpressions.RegexOptions.Singleline)
                : first;
            // Core controls have separate deterministic tests. Here they supply body input to an
            // independent compositor; full expected title/body text is never taken from snapshots.
            int color = 0x4F;
            int delay = 24;
            _lines = block.Parts.Skip(1).Select(part =>
            {
                var line = DialogueTextTokenizer.TokenizeLine(part.Text!, color, delay);
                color = line.EndColor;
                delay = line.EndDelayMilliseconds;
                return line;
            }).ToArray();
            Require(_portrait.Width == 78 && _portrait.Height == 102, "RGM59 reference dimensions must be 78x102.");
            _background = Enumerable.Range(0, 320 * 200).Select(i =>
                (byte)(i / 320 >= 176 ? 0xF9 + i % 320 / 54 : 16 + (i % 320 / 40 + i / 320 / 25) % 8)).ToArray();
            var background = new PalDialoguePaletteSurface();
            background.SetImage(new PalDialogueIndexedImage { Width = 320, Height = 200, PaletteIndices = _background,
                Alpha = Enumerable.Repeat((byte)255, 320 * 200).ToArray() });
            background.SetPalette(_palette);
            AddChild(background);
            _display = new PalDialogueDisplay();
            _display.ActivePaletteChanged += background.SetPalette;
            AddChild(_display);
            ProcessPriority = 100; // The display's priority is 0: observe after its real _Process returns.
            _started = Time.GetTicksMsec();
            RenderingServer.FramePostDraw += ObserveRenderedFrame;
            _subscribed = true;
            GD.Print($"PAL_DIALOGUE_GPU_BEGIN:language={_languageId}:start=61:span=1:parts=2:renderer={DisplayServer.GetName()}");
        }
        catch (Exception error) { Fail(error); }
    }

    public override void _Process(double delta)
    {
        if (_interactionMode) { ProcessInteraction(); return; }
        if (_finished) return;
        if (_started > 0 && Time.GetTicksMsec() - _started > 30000)
        {
            Fail(new TimeoutException("No complete dialogue observation within the 30-second liveness bound."));
            return;
        }
        _observed = _display?.Current;
        _updateFrame = Engine.GetProcessFrames();
    }

    // Pair each readback with a distinct completed display update, rather than assuming that
    // another render signal or a test-side simulated frame implies another business update.
    private void ObserveRenderedFrame()
    {
        if (_interactionMode) { ObserveInteraction(); return; }
        if (_finished || _updateFrame == _lastRenderedUpdate) return;
        _lastRenderedUpdate = _updateFrame;
        try
        {
            var snapshot = _observed;
            switch (_stage)
            {
                case 0:
                    Capture("baseline", Background(), null);
                    _display.Start(_language, _font, _portrait, _prompts, _palette, 61, 1, 59, 0, 0x4F);
                    _stage = 1;
                    break;
                case 1 when snapshot is { Phase: DialogueDisplayPhase.Typing, LineIndex: 0, VisibleText.Length: > 0 }:
                    Capture("typing", Expected(snapshot), snapshot);
                    KeyEvent(true); // Real Godot input delivery, not a direct session update.
                    _stage = 2;
                    break;
                case 1 when snapshot is { Phase: DialogueDisplayPhase.WaitingForAdvance }:
                    // Short content may finish before the first observable update. Record the
                    // natural body, not a fabricated typing/acceleration claim.
                    Capture("natural-body", Expected(snapshot), snapshot);
                    _stage = 3;
                    break;
                case 2 when snapshot is { Phase: DialogueDisplayPhase.WaitingForAdvance }:
                    KeyEvent(false);
                    Require(snapshot is { Phase: DialogueDisplayPhase.WaitingForAdvance }
                        && snapshot.VisibleText == string.Concat(_lines[0].Tokens.Select(token => token.Text)),
                        "Acceleration must finish the real target body, without consuming its subsequent advance wait.");
                    Capture("accelerated-body", Expected(snapshot!), snapshot);
                    _stage = 3;
                    break;
                case 3 when snapshot is { Phase: DialogueDisplayPhase.WaitingForAdvance }:
                    Capture("waiting", Expected(snapshot), snapshot);
                    _waitingRotation = snapshot.PaletteRotation;
                    _stage = 4;
                    break;
                case 4 when snapshot is { Phase: DialogueDisplayPhase.WaitingForAdvance } && snapshot.PaletteRotation != _waitingRotation:
                    Capture("waiting-rotated", Expected(snapshot), snapshot);
                    KeyEvent(true);
                    _stage = 5;
                    break;
                case 5 when snapshot is { Phase: DialogueDisplayPhase.Completed }:
                    KeyEvent(false);
                    Require(snapshot is { Phase: DialogueDisplayPhase.Completed, ClearDisplay: true, PaletteRotation: 0 },
                        "Advance must complete and restore the dialogue palette at the actual guarded completion update.");
                    Capture("completed-restored", Background(), snapshot);
                    Finish();
                    break;
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private bool[] _overlayMask = [];
    private byte[] Background()
    {
        if (!_interactionMode) return _background.ToArray();
        _overlayMask = new bool[320 * 200];
        return new byte[320 * 200];
    }

    private byte[] Expected(DialogueDisplaySnapshot snapshot)
    {
        byte[] pixels = Background();
        if (snapshot.Phase == DialogueDisplayPhase.Completed) return pixels;
        Paste(pixels, _portrait, Math.Max(0, 48 - _portrait.Width / 2), Math.Max(0, 55 - _portrait.Height / 2)); // text.c: (48 - 78/2, 55 - 102/2).
        DrawLine(pixels, new[] { (_title, 0x8C) }, 8, startX: 80);
        bool waiting = snapshot.Phase == DialogueDisplayPhase.WaitingForAdvance;
        int completedLines = waiting ? _lines.Length : snapshot.LineIndex;
        for (int line = 0; line < completedLines; line++)
            DrawLine(pixels, _lines[line].Tokens.Select(token => (token.Text, token.Color)), 26 + line * 18);
        if (waiting)
        {
            int promptX = 96 + _lines[^1].Tokens.Sum(token => token.Text.EnumerateRunes().Sum(rune => _font.GetGlyph(rune.Value).Width));
            Paste(pixels, _prompts[_lines[^1].PromptFrame], promptX, 26 + (_lines.Length - 1) * 18);
        }
        else
            DrawLine(pixels, _lines[snapshot.LineIndex].Tokens.Take(snapshot.VisibleText.EnumerateRunes().Count())
                .Select(token => (token.Text, token.Color)), 26 + snapshot.LineIndex * 18);
        return pixels;
    }

    private void DrawLine(byte[] pixels, IEnumerable<(string Text, int Color)> runs, int y, int startX = 96)
    {
        int x = startX;
        foreach (var run in runs)
        foreach (Rune rune in run.Text.EnumerateRunes())
        {
            var glyph = _font.GetGlyph(rune.Value);
            // Independent unpacking of native input bits, not PalDialogueGlyph.IsSet or _Draw output.
            var points = Enumerable.Range(0, glyph.Width * glyph.Height)
                .Where(i => (glyph.Bitmap[(i / glyph.Width) * glyph.RowBytes + (i % glyph.Width) / 8]
                    >> (7 - i % glyph.Width % 8) & 1) != 0).ToArray();
            // text.c PAL_DrawTextUnescape draws three whole shadow masks, then the foreground.
            foreach (var offset in new[] { (X: 1, Y: 0), (X: 0, Y: 1), (X: 1, Y: 1), (X: 0, Y: 0) })
                foreach (int point in points)
                    Put(pixels, x + point % glyph.Width + offset.X, y + point / glyph.Width + offset.Y,
                        (byte)(offset == (0, 0) ? run.Color : 0));
            x += glyph.Width;
        }
    }

    private void Paste(byte[] pixels, PalDialogueIndexedImage input, int left, int top)
    {
        for (int i = 0; i < input.PaletteIndices.Length; i++)
        {
            Require(input.Alpha[i] is 0 or 255, "Pixel oracle requires the reference binary transparency planes.");
            if (input.Alpha[i] == 255) Put(pixels, left + i % input.Width, top + i / input.Width, input.PaletteIndices[i]);
        }
    }

    private void Put(byte[] pixels, int x, int y, byte index)
    {
        if (x is >= 0 and < 320 && y is >= 0 and < 200)
        {
            pixels[y * 320 + x] = index;
            if (_interactionMode) _overlayMask[y * 320 + x] = true;
        }
    }

    private void Capture(string name, byte[] expectedIndices, DialogueDisplaySnapshot? snapshot)
    {
        using var actual = GetViewport().GetTexture().GetImage();
        Require(actual is not null && actual.GetSize() == new Vector2I(320, 200), "GPU readback must be a non-null 320x200 image.");
        Require(actual!.SavePng(Path.Combine(_evidence, name + ".png")) == Error.Ok, "Saving GPU evidence failed.");
        using var expected = Image.CreateEmpty(320, 200, false, Image.Format.Rgba8);
        int rotation = snapshot?.PaletteRotation ?? 0;
        int differences = 0;
        int firstX = -1, firstY = -1;
        for (int y = 0; y < 200; y++)
        for (int x = 0; x < 320; x++)
        {
            int index = expectedIndices[y * 320 + x];
            int mapped = index is >= 0xF9 and <= 0xFE ? 0xF9 + (index - 0xF9 + rotation) % 6 : index;
            bool scenePixel = _interactionMode && !_overlayMask[y * 320 + x];
            Color want = scenePixel ? _mapBaseline!.GetPixel(x, y) : _palette[mapped];
            expected.SetPixel(x, y, want);
            // Native index audit established that this real map/objects/leader have no
            // opaque F9-FE pixels. The saved pose stays unchanged during these rotations;
            // it is a restoration oracle, not an independent complete map renderer.
            Color got = actual.GetPixel(x, y);
            const float tolerance = 1.1f / 255f; // Only byte conversion rounding, not a geometric tolerance.
            if (Math.Abs(want.R - got.R) <= tolerance && Math.Abs(want.G - got.G) <= tolerance
                && Math.Abs(want.B - got.B) <= tolerance && Math.Abs(want.A - got.A) <= tolerance) continue;
            if (differences++ == 0) { firstX = x; firstY = y; }
        }
        Require(expected.SavePng(Path.Combine(_evidence, name + "-expected.png")) == Error.Ok, "Saving expected image failed.");
        _checks.Add(new { stage = name, differences, firstX, firstY, rotation, updateFrame = _updateFrame,
            phase = snapshot?.Phase.ToString(), line = snapshot?.LineIndex, visibleCharacters = snapshot?.VisibleText.Length });
        if (differences > 0) _failures++;
        SaveResult(completed: false);
        GD.Print($"PAL_DIALOGUE_GPU_CHECK:stage={name}:differences={differences}:first={firstX},{firstY}:rotation={rotation}:update={_updateFrame}");
    }

    private void Finish()
    {
        _finished = true;
        DisconnectRenderer();
        SaveResult(completed: true);
        GD.Print($"PAL_DIALOGUE_GPU_END:language={_languageId}:failures={_failures}:completed=true");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Fail(Exception error)
    {
        _finished = true;
        DisconnectRenderer();
        KeyEvent(false);
        if (Directory.Exists(_evidence)) SaveResult(completed: false, error.GetType().Name + ": " + error.Message);
        GD.PrintErr($"PAL_DIALOGUE_GPU_FAILED:{error.GetType().Name}:{error.Message}");
        GetTree().Quit(1);
    }

    private void SaveResult(bool completed, string? failure = null) =>
        File.WriteAllText(Path.Combine(_evidence, "fixture-result.json"), JsonSerializer.Serialize(new
        {
            language = _languageId, failures = _failures, completed, failure, checks = _checks,
            actualMain = _interactionMode, callsCompleted = _callsCompleted,
            scope = _interactionMode ? "Actual Main + full events + programmatic search/input + real update receipts; map GPU readback when non-headless. Not physical keyboard/manual acceptance; map baseline is only a restoration oracle, not an independent complete map renderer." : "Native resources + programmatic input + logical GPU readback on a synthetic indexed background; not map10 or physical keyboard/manual acceptance."
        }, new JsonSerializerOptions { WriteIndented = true }));

    private void DisconnectRenderer()
    {
        if (!_subscribed) return;
        RenderingServer.FramePostDraw -= ObserveRenderedFrame;
        _subscribed = false;
    }

    public override void _ExitTree()
    {
        DisconnectRenderer();
        if (_main is not null) _main.UpdateCompleted -= ObserveMainUpdate;
        if (_interactionMode && _display is not null) _display.UpdateCompleted -= ObserveDisplayUpdate;
        _mapBaseline?.Dispose();
    }

    private static void KeyEvent(bool pressed)
    {
        using var input = new InputEventKey { Keycode = Key.Space, Pressed = pressed, Echo = false };
        Input.ParseInputEvent(input);
        Input.FlushBufferedEvents();
    }

    private static T Load<T>(string uri) where T : Resource => ResourceLoader.Load<T>(uri)
        ?? throw new InvalidDataException("An explicitly selected native resource could not be loaded.");

    private static string RequiredEnvironment(string name) => OS.GetEnvironment(name) is { Length: > 0 } value
        ? value : throw new InvalidDataException($"Explicit {name} is required; no fallback or preparation is performed.");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
