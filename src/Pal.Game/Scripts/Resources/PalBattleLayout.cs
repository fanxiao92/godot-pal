using Godot;

namespace Pal.Game.Resources;

[GlobalClass] public partial class PalBattleLayout : Resource { [Export] public int EquipmentRoleCount {get;set;} [Export] public int EquipmentSlotCount {get;set;} [Export] public int LevelUpMagicRoleCount {get;set;} }
