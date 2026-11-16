using Godot;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalBattleVisualProfile : Resource
{
    [Export] public int FormatVersion { get; set; } = -1;
    [Export] public string SourceIdentity { get; set; } = "";
    // #209 unified per-role bindings in party order: party index -> RoleId -> own battle
    // sprite/effect/position. The legacy scalar single-player declaration is removed with the
    // FormatVersion bump; old shared-sprite resources are re-prepared, never dual-loaded.
    [Export] public PalBattleVisualPlayer[] Players { get; set; } = [];
    [Export] public string NumberSpriteUri { get; set; } = "";
    [Export] public string PaletteUri { get; set; } = "";
    [Export] public PalBattleVisualTeam[] Teams { get; set; } = [];
    [Export] public Godot.Collections.Dictionary<string, string> ResourceSha256 { get; set; } = new();
}
