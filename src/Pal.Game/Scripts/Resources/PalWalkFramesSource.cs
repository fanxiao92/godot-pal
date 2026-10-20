using Godot;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalWalkFramesSource : Resource
{
    [Export] public string Archive { get; set; } = "";
    [Export] public string Sha256 { get; set; } = "";
    [Export] public int Chunk { get; set; } = -1;
    [Export] public int ByteOffset { get; set; } = -1;
    [Export] public int RoleId { get; set; } = -1;
    [Export] public string Field { get; set; } = "";
}
