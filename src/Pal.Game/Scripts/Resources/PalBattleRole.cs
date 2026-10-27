using Godot;

namespace Pal.Game.Resources;

[GlobalClass] public partial class PalBattleRole : Resource { [Export] public int RoleId {get;set;} [Export] public int Level {get;set;} [Export] public int Hp {get;set;} [Export] public int MaxHp {get;set;} [Export] public int Mp {get;set;} [Export] public int MaxMp {get;set;} [Export] public PalBattleExperience? PrimaryExperience {get;set;} [Export] public PalBattleHiddenExperience? HiddenExperience {get;set;} [Export] public int[] Equipment {get;set;}=[]; [Export] public int[] Magics {get;set;}=[]; [Export] public int[] Statuses {get;set;}=[]; [Export] public int CoveredByRoleId {get;set;} }
