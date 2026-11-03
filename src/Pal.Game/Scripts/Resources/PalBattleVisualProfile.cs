using Godot;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalBattleVisualProfile : Resource
{
    [Export] public int FormatVersion { get; set; } = -1;
    [Export] public string SourceIdentity { get; set; } = "";
    [Export] public int RoleId { get; set; } = -1;
    [Export] public string PlayerSpriteUri { get; set; } = "";
    [Export] public string AttackEffectSpriteUri { get; set; } = "";
    // Resolved column 1 of the effect table for the actual battle sprite, not RoleId.
    [Export] public int AttackEffectIndex { get; set; } = -1;
    [Export] public Vector2I PlayerPosition { get; set; } = new(int.MinValue, int.MinValue);
    [Export] public string PaletteUri { get; set; } = "";
    [Export] public PalBattleVisualTeam[] Teams { get; set; } = [];
    [Export] public Godot.Collections.Dictionary<string, string> ResourceSha256 { get; set; } = new();
}
