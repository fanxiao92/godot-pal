using Godot;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalBattleTeam : Resource
{
    [Export] public int TeamId { get; set; } = 0;
    [Export] public PalBattleEnemy[] Enemies { get; set; } = [];
}
