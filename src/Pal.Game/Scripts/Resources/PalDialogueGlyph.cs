using System;
using System.IO;
using Godot;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalDialogueGlyph : Resource
{
    [Export] public int Codepoint { get; set; }
    [Export] public int Width { get; set; }
    [Export] public int Height { get; set; }
    [Export] public int RowBytes { get; set; } = 1;
    [Export] public byte[] Bitmap { get; set; } = System.Array.Empty<byte>();

    public bool IsSet(int x, int y)
    {
        if (Bitmap is null || x < 0 || y < 0 || x >= Width || y >= Height || RowBytes is < 1 or > 2
            || Width is < 1 or > 16 || Height != 15 && Height != 16 || Bitmap.Length != RowBytes * Height)
            throw new InvalidDataException($"Dialogue glyph U+{Codepoint:X4} has invalid bitmap dimensions.");
        int bit = 7 - (x & 7);
        int byteIndex = y * RowBytes + x / 8;
        return (Bitmap[byteIndex] & (1 << bit)) != 0;
    }
}
