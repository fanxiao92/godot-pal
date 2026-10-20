using Godot;
using Godot.Collections;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalPalette : Resource
{
    [Export] public int PaletteId { get; set; } = 0;
    [Export] public string Lighting { get; set; } = "";
    [Export] public string SelectedLighting { get; set; } = "";
    [Export] public byte[] Rgb { get; set; } = System.Array.Empty<byte>();
    [Export] public Dictionary Source { get; set; } = new();
}
