using Godot;

namespace Pal.Game.Resources;

[GlobalClass] public partial class PalBattleRoleHooksIdentity : PalBattleClosureIdentity { [Export] public int DyingRoleId {get;set;} [Export] public int DyingStartAddress {get;set;} [Export] public int DyingEndAddress {get;set;} [Export] public int FriendDeathRoleId {get;set;} [Export] public int FriendDeathStartAddress {get;set;} [Export] public int FriendDeathEndAddress {get;set;} [Export] public string BranchCoverage {get;set;}=""; }
