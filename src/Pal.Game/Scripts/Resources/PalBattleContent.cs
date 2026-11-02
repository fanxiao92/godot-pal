using Godot;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalBattleContent : Resource
{
    [Export] public int FormatVersion { get; set; } = -1;
    [Export] public PalBattleRole[] Roles { get; set; } = [];
    [Export] public PalBattlePlayer[] Players { get; set; } = [];
    [Export] public PalBattleGrowth[] Growth { get; set; } = [];
    [Export] public PalBattleTeam[] Teams { get; set; } = [];
}
