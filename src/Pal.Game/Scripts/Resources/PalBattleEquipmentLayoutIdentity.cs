using Godot;

namespace Pal.Game.Resources;

[GlobalClass] public partial class PalBattleEquipmentLayoutIdentity : PalBattleClosureIdentity { [Export] public int RoleCount {get;set;} [Export] public int SlotCount {get;set;} [Export] public int DualAttackObjectId {get;set;} [Export] public bool PersistentEquipmentStatus {get;set;} }
