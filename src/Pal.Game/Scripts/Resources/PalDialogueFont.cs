using Godot;
using Godot.Collections;
using System.Linq;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalDialogueFont : Resource
{
    [Export] public int FormatVersion { get; set; } = 1;
    [Export] public int LineHeight { get; set; } = 16;
    [Export] public Array<PalDialogueGlyph> Glyphs { get; set; } = new();

    public PalDialogueGlyph GetGlyph(int codepoint)
    {
        if (FormatVersion != 1 || LineHeight != 16)
            throw new InvalidDataException("Dialogue font resource has an unsupported version or line height.");
        PalDialogueGlyph[] matches = Glyphs.Where(candidate => candidate is not null && candidate.Codepoint == codepoint).ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException(matches.Length == 0
                ? $"Dialogue font has no glyph for U+{codepoint:X4}."
                : $"Dialogue font has duplicate glyphs for U+{codepoint:X4}.");
        return matches[0];
    }
}
