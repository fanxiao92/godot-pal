using Godot;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalBattleGrowth : Resource
{
    [Export] public int RoleId { get; set; } = 0;
    [Export] public int[] LevelUpExperience { get; set; } = [];
    [Export] public PalBattleLearnableMagic[] LevelUpMagics { get; set; } = [];
}
