using Godot;

namespace Pal.Game.Resources;

// Native static map root; no rendering, scripts, object updates or party placement.
[GlobalClass]
public partial class PalMapScene : Node2D
{
    [Export] public PalSceneData Data { get; set; } = null!;
}
