using System;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using Pal.Core;
using Pal.Game.Resources;

namespace Pal.Game.Dialogue;

public partial class PalDialogueDisplay : Control
{
    private DialogueDisplaySession? _session;
    private PalDialogueFont? _font;
    private PalDialogueIndexedImage? _portrait;
    private PalDialogueIndexedImage[] _prompts = [];
    private Color[] _palette = [];
    private Color[] _activePalette = [];
    private int _portraitId;
    private int _advanceEvents;
    private int _lastPaletteRotation;

    public event Action<Color[]>? ActivePaletteChanged;
    public event Action? BackgroundRestoreRequested;
    // Emitted at the end of the actual update, after palette restoration and redraw scheduling.
    public event Action? UpdateCompleted;
    public DialogueDisplaySession? Session => _session;
    public DialogueDisplaySnapshot? Current => _session?.Current;

    public void Start(
        PalDialogueLanguage language,
        PalDialogueFont font,
        PalDialogueIndexedImage portrait,
        PalDialogueIndexedImage[] prompts,
        Color[] currentScenePalette,
        int startMessageId,
        int span,
        int portraitId,
        int commandColor,
        int inheritedColor)
    {
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(portrait);
        DialogueMessageBlock block = language.ToCorePackage().Resolve(startMessageId, span);
        ValidateContent(block, inheritedColor, font, portrait, prompts, currentScenePalette);
        Attach(new DialogueDisplaySession(block, inheritedColor, commandColor), font, portrait, prompts, currentScenePalette, portraitId);
    }

    public void Start(TriggerDialogueRequest request, PalDialogueFont font, PalDialogueIndexedImage? portrait,
        PalDialogueIndexedImage[] prompts, Color[] currentScenePalette)
    {
        ValidateContent(request.Content, request.Context.Color, font, portrait, prompts, currentScenePalette);
        Attach(request.Display, font, portrait, prompts, currentScenePalette, request.Context.PortraitId);
    }

    internal static void ValidateContent(DialogueMessageBlock block, int color, PalDialogueFont font,
        PalDialogueIndexedImage? portrait, PalDialogueIndexedImage[] prompts, Color[] currentScenePalette)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(prompts);
        if (prompts.Length != 3 || prompts.Any(prompt => prompt is null)) throw new InvalidDataException("Dialogue requires prompt frames 0, 1, and 2.");
        if (currentScenePalette.Length != 256) throw new InvalidDataException("Dialogue rendering requires the active 256-color scene palette.");
        if (portrait is not null) ValidateImage(portrait);
        foreach (PalDialogueIndexedImage prompt in prompts) ValidateImage(prompt);
        // Reuse the Core display rules for preflight instead of interpreting title punctuation
        // as body controls. No input/time is applied to the actual playback session here.
        var preview = new DialogueDisplaySession(block, color, 0);
        var checkedGlyphs = new System.Collections.Generic.HashSet<int>();
        while (preview.Current.Phase != DialogueDisplayPhase.Completed)
        {
            string text = preview.Current.TitleText + string.Concat(preview.Current.VisibleLines
                .SelectMany(line => line.Runs).Select(run => run.Text));
            foreach (Rune rune in text.EnumerateRunes())
                if (checkedGlyphs.Add(rune.Value))
                {
                    try { font.GetGlyph(rune.Value).IsSet(0, 0); }
                    catch (InvalidDataException error) { throw new InvalidDataException($"{font.ResourcePath}: {error.Message}", error); }
                }
            preview.Update(0, advancePressed: true, accelerateHeld: true);
        }
    }

    private static void ValidateImage(PalDialogueIndexedImage image)
    {
        try { image.Validate(); }
        catch (InvalidDataException error) { throw new InvalidDataException($"{image.ResourcePath}: {error.Message}", error); }
    }

    private void Attach(DialogueDisplaySession session, PalDialogueFont font, PalDialogueIndexedImage? portrait,
        PalDialogueIndexedImage[] prompts, Color[] currentScenePalette, int portraitId)
    {
        _advanceEvents = 0;
        _font = font;
        _portrait = portrait;
        _prompts = prompts.ToArray();
        _palette = currentScenePalette.ToArray();
        _activePalette = _palette.ToArray();
        _portraitId = portraitId;
        _session = session;
        _lastPaletteRotation = 0;
        ActivePaletteChanged?.Invoke(_activePalette.ToArray());
        FocusMode = FocusModeEnum.All;
        GrabFocus();
        ApplySnapshot();
    }

    public override void _Input(InputEvent @event)
    {
        if (_session is not null && _session.Current.Phase != DialogueDisplayPhase.Completed
            && @event is InputEventKey { Pressed: true, Echo: false } key && IsDialogueAdvanceKey(key.Keycode))
        {
            _advanceEvents++;
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (_session is null || _session.Current.Phase == DialogueDisplayPhase.Completed) return;
        bool advance = _advanceEvents > 0;
        _advanceEvents = 0;
        bool accelerate = IsAdvanceKeyDown();
        uint elapsed = (uint)Math.Clamp((long)Math.Round(delta * 1000.0), 0L, uint.MaxValue);
        _session.Update(elapsed, advance, accelerate);
        if (_lastPaletteRotation != _session.Current.PaletteRotation)
        {
            _lastPaletteRotation = _session.Current.PaletteRotation;
            _activePalette = CreateActivePalette();
            ActivePaletteChanged?.Invoke(_activePalette.ToArray());
        }
        if (_session.Current.Phase == DialogueDisplayPhase.Completed && _lastPaletteRotation != 0)
        {
            _lastPaletteRotation = 0;
            _activePalette = _palette.ToArray();
            ActivePaletteChanged?.Invoke(_activePalette.ToArray());
        }
        ApplySnapshot();
        UpdateCompleted?.Invoke();
    }

    private void ApplySnapshot()
    {
        if (_session!.Current.ClearDisplay) BackgroundRestoreRequested?.Invoke();
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_session is null || _font is null || _prompts.Length != 3 || _activePalette.Length != 256) return;
        DialogueDisplaySnapshot snapshot = _session.Current;
        if (snapshot.Phase == DialogueDisplayPhase.Completed) return;
        if (_portraitId > 0 && _portrait is not null)
        {
            int x = Math.Max(0, 48 - _portrait.Width / 2);
            int y = Math.Max(0, 55 - _portrait.Height / 2);
            DrawIndexed(_portrait, x, y);
        }

        if (snapshot.TitleText.Length > 0)
            DrawBitmapText(snapshot.TitleText, _portraitId > 0 ? 80 : 12, 8, 0x8C);
        int textX = _portraitId > 0 ? 96 : 44;
        int promptX = textX;
        int promptY = 26;
        foreach (DialogueVisibleLine line in snapshot.VisibleLines)
        {
            promptX = textX;
            promptY = 26 + line.LineIndex * 18;
            foreach (DialogueVisibleRun run in line.Runs)
                promptX = DrawBitmapText(run.Text, promptX, promptY, run.Color);
        }
        if (snapshot.Phase == DialogueDisplayPhase.WaitingForAdvance)
            DrawIndexed(_prompts[snapshot.PromptFrame], promptX, promptY);
    }

    private int DrawBitmapText(string text, int x, int y, int colorIndex)
    {
        foreach (Rune rune in text.EnumerateRunes())
        {
            PalDialogueGlyph glyph = _font!.GetGlyph(rune.Value);
            for (int row = 0; row < glyph.Height; row++)
            for (int column = 0; column < glyph.Width; column++)
            {
                if (!glyph.IsSet(column, row)) continue;
                DrawRect(new Rect2(x + column + 1, y + row, 1, 1), _palette[0]);
                DrawRect(new Rect2(x + column, y + row + 1, 1, 1), _palette[0]);
                DrawRect(new Rect2(x + column + 1, y + row + 1, 1, 1), _palette[0]);
                DrawRect(new Rect2(x + column, y + row, 1, 1), PaletteColor(colorIndex));
            }
            x += glyph.Width;
        }
        return x;
    }

    private void DrawIndexed(PalDialogueIndexedImage image, int left, int top)
    {
        for (int y = 0; y < image.Height; y++)
        for (int x = 0; x < image.Width; x++)
        {
            int index = y * image.Width + x;
            if (image.Alpha[index] == 0) continue;
            byte paletteIndex = image.PaletteIndices[index];
            Color color = _palette[paletteIndex];
            color.A *= image.Alpha[index] / 255.0f;
            DrawRect(new Rect2(left + x, top + y, 1, 1), PaletteColor(paletteIndex) with { A = color.A });
        }
    }

    private Color PaletteColor(int index) => _activePalette[index];

    private Color[] CreateActivePalette()
    {
        var active = _palette.ToArray();
        for (int index = 0xF9; index <= 0xFE; index++)
            active[index] = _palette[0xF9 + (index - 0xF9 + _lastPaletteRotation) % 6];
        return active;
    }

    private bool IsAdvanceKeyDown() =>
        Input.IsKeyPressed(Key.Enter) || Input.IsKeyPressed(Key.Space) || Input.IsKeyPressed(Key.KpEnter)
        || Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Escape) || Input.IsKeyPressed(Key.Insert)
        || Input.IsKeyPressed(Key.Alt) || Input.IsKeyPressed(Key.Kp0);

    private static bool IsDialogueAdvanceKey(Key key) => key is
        Key.Enter or Key.Space or Key.KpEnter or Key.Ctrl or Key.Escape or Key.Insert or Key.Alt or Key.Kp0;
}
