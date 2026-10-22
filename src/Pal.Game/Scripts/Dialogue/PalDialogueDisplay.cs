using System;
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
    private PalDialogueIndexedImage? _prompt;
    private Color[] _palette = [];
    private int _portraitId;
    private int _advanceEvents;
    private int _lastPaletteRotation;

    public event Action<int>? PaletteRotationChanged;
    public DialogueDisplaySnapshot? Current => _session?.Current;

    public void Start(
        PalDialogueLanguage language,
        PalDialogueFont font,
        PalDialogueIndexedImage portrait,
        PalDialogueIndexedImage prompt,
        Color[] currentScenePalette,
        int startMessageId,
        int span,
        int portraitId,
        int commandColor,
        int inheritedColor)
    {
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(portrait);
        ArgumentNullException.ThrowIfNull(prompt);
        if (currentScenePalette.Length != 256) throw new InvalidDataException("Dialogue rendering requires the active 256-color scene palette.");
        portrait.Validate();
        prompt.Validate();
        DialogueLanguagePackage package = language.ToCorePackage();
        foreach (DialogueMessagePart part in package.Resolve(startMessageId, span).Parts)
            if (part.Text is not null)
                foreach (Rune rune in part.Text.EnumerateRunes()) font.GetGlyph(rune.Value);
        _font = font;
        _portrait = portrait;
        _prompt = prompt;
        _palette = currentScenePalette.ToArray();
        _portraitId = portraitId;
        _session = new DialogueDisplaySession(package.Resolve(startMessageId, span), inheritedColor, commandColor);
        _lastPaletteRotation = 0;
        GrabFocus();
        QueueRedraw();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key && IsDialogueAdvanceKey(key.Keycode))
        {
            _advanceEvents++;
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (_session is null) return;
        bool advance = _advanceEvents > 0;
        _advanceEvents = 0;
        bool accelerate = IsAdvanceKeyDown();
        uint elapsed = (uint)Math.Clamp((long)Math.Round(delta * 1000.0), 0L, uint.MaxValue);
        _session.Update(elapsed, advance, accelerate);
        if (_lastPaletteRotation != _session.Current.PaletteRotation)
        {
            _lastPaletteRotation = _session.Current.PaletteRotation;
            PaletteRotationChanged?.Invoke(_lastPaletteRotation);
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_session is null || _font is null || _portrait is null || _prompt is null || _palette.Length != 256) return;
        if (_portraitId > 0)
        {
            int x = Math.Max(0, 48 - _portrait.Width / 2);
            int y = Math.Max(0, 55 - _portrait.Height / 2);
            DrawIndexed(_portrait, x, y);
        }

        DialogueDisplaySnapshot snapshot = _session.Current;
        if (snapshot.Phase == DialogueDisplayPhase.Completed) return;
        int textX = _portraitId > 0 ? 96 : 44;
        int textY = 26 + snapshot.LineIndex * 18;
        int promptX = DrawBitmapText(snapshot.VisibleText, textX, textY, snapshot.Color);
        if (snapshot.Phase == DialogueDisplayPhase.WaitingForAdvance)
            DrawIndexed(_prompt, promptX, textY);
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

    private Color PaletteColor(int index)
    {
        if (index is >= 0xF9 and <= 0xFE)
            index = 0xF9 + (index - 0xF9 + _lastPaletteRotation) % 6;
        return _palette[index];
    }

    private bool IsAdvanceKeyDown() =>
        Input.IsKeyPressed(Key.Enter) || Input.IsKeyPressed(Key.Space) || Input.IsKeyPressed(Key.KpEnter)
        || Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Escape) || Input.IsKeyPressed(Key.Insert)
        || Input.IsKeyPressed(Key.Alt) || Input.IsKeyPressed(Key.Kp0);

    private static bool IsDialogueAdvanceKey(Key key) => key is
        Key.Enter or Key.Space or Key.KpEnter or Key.Ctrl or Key.Escape or Key.Insert or Key.Alt or Key.Kp0;
}
