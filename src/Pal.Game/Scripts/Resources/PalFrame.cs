using Godot;
using Godot.Collections;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalFrame : Resource
{
    [Export] public int Index { get; set; } = 0;
    [Export] public int SourceOffset { get; set; } = 0;
    [Export] public int Width { get; set; } = 0;
    [Export] public int Height { get; set; } = 0;
    [Export] public byte[] Indices { get; set; } = System.Array.Empty<byte>();
    [Export] public byte[] Alpha { get; set; } = System.Array.Empty<byte>();
    [Export] public Texture2D Texture { get; set; } = null!;
}
