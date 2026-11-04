using Godot;

namespace Pal.Game.Resources;

[GlobalClass] public partial class PalBattleCommandIdentity : PalBattleClosureIdentity { [Export] public int CommandAddress {get;set;} [Export] public int[] Operands {get;set;}=[]; [Export] public int EnemyTeamId {get;set;} [Export] public int EnemyObjectId {get;set;} [Export] public int EnemyId {get;set;} [Export] public int EnemyCount {get;set;} [Export] public int BattlefieldId {get;set;} [Export] public string SpecialValue {get;set;}=""; [Export] public string ReverseEncoding {get;set;}=""; }
