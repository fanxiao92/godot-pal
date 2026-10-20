using Godot;
using Godot.Collections;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalSprite : Resource
{
    [Export] public int SpriteId { get; set; } = 0;
    [Export] public int DecodedByteLength { get; set; } = 0;
    [Export] public int FrameCount { get; set; } = 0;
    [Export] public Dictionary Source { get; set; } = new();
    [Export] public Godot.Collections.Array<PalFrame> Frames { get; set; } = new();
}
