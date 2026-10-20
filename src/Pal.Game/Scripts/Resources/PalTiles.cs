using Godot;
using Godot.Collections;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalTiles : Resource
{
    [Export] public int MapId { get; set; } = 0;
    [Export] public Dictionary Source { get; set; } = new();
    [Export] public Godot.Collections.Array<PalFrame> Frames { get; set; } = new();
}
