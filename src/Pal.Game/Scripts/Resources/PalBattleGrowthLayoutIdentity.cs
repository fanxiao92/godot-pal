using Godot;

namespace Pal.Game.Resources;

[GlobalClass] public partial class PalBattleGrowthLayoutIdentity : PalBattleClosureIdentity { [Export] public int RoleCount {get;set;} [Export] public bool IncludesPrimaryExperience {get;set;} [Export] public bool IncludesHiddenExperience {get;set;} [Export] public bool IncludesRecoveryAndCleanup {get;set;} }
