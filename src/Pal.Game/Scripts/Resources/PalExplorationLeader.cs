using Godot;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalExplorationLeader : Resource
{
    [Export] public int RoleId { get; set; } = -1;
    [Export] public int WalkFramesWord { get; set; } = -1;
    [Export] public PalWalkFramesSource WalkFramesSource { get; set; } = null!;
}
