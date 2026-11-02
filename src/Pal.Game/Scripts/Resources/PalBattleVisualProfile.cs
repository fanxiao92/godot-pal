using Godot;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalBattleVisualProfile : Resource
{
    [Export] public int FormatVersion { get; set; } = -1;
    [Export] public string SourceIdentity { get; set; } = "";
    [Export] public int RoleId { get; set; } = -1;
    [Export] public string PlayerSpriteUri { get; set; } = "";
    [Export] public Vector2I PlayerPosition { get; set; } = new(int.MinValue, int.MinValue);
    [Export] public string PaletteUri { get; set; } = "";
    [Export] public PalBattleVisualTeam[] Teams { get; set; } = [];
    [Export] public Godot.Collections.Dictionary<string, string> ResourceSha256 { get; set; } = new();
}
