using Godot;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalDialogueIndexedImage : Resource
{
    [Export] public int Width { get; set; }
    [Export] public int Height { get; set; }
    [Export] public byte[] PaletteIndices { get; set; } = Array.Empty<byte>();
    [Export] public byte[] Alpha { get; set; } = Array.Empty<byte>();

    public void Validate()
    {
        if (PaletteIndices is null || Alpha is null || Width <= 0 || Height <= 0 || (long)Width * Height > 16 * 1024 * 1024
            || PaletteIndices.Length != Width * Height || Alpha.Length != PaletteIndices.Length)
            throw new InvalidDataException("Dialogue indexed image has invalid dimensions or planes.");
    }
}
