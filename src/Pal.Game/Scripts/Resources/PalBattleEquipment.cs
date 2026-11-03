using Godot;

namespace Pal.Game.Resources;

/// <summary>Typed equip-script closure for one equipment object: exactly six role-indexed effect rows.</summary>
[GlobalClass] public partial class PalBattleEquipment : Resource
{
    [Export] public int ObjectId { get; set; }
    [Export] public PalBattleEquipmentEffect[] Effects { get; set; } = [];
}
