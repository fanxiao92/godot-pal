using Godot;

namespace Pal.Game.Resources;

[GlobalClass] public partial class PalBattleProvenance : Resource { [Export] public bool OptionalGrinding {get;set;} [Export] public bool PlayerEquipmentChanges {get;set;} [Export] public PalBattleFieldSource[] FieldSources {get;set;}=[]; }
