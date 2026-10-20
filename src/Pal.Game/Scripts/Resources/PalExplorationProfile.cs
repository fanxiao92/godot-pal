using Godot;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalExplorationProfile : Resource
{
    [Export] public int FormatVersion { get; set; } = -1;
    [Export] public int SceneId { get; set; } = -1;
    [Export] public string BundleSha256 { get; set; } = "";
    [Export] public PalExplorationStart Start { get; set; } = null!;
    [Export] public PalExplorationLeader Leader { get; set; } = null!;
}
