using Godot;

namespace Pal.Game.Resources;

/// <summary>Per-role effect row of one equipment object: attribute words, battle-sprite override word and nine status words.</summary>
[GlobalClass] public partial class PalBattleEquipmentEffect : Resource
{
    [Export] public int Attack { get; set; }
    [Export] public int Defense { get; set; }
    [Export] public int Dexterity { get; set; }
    [Export] public int BattleSprite { get; set; }
    [Export] public int[] Statuses { get; set; } = [];
}
