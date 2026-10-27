using Godot;

namespace Pal.Game.Resources;

[GlobalClass] public partial class PalBattleEventRangeIdentity : PalBattleClosureIdentity { [Export] public int StartAddress {get;set;} [Export] public int BattleAddress {get;set;} [Export] public int WonStartAddress {get;set;} [Export] public int EndAddress {get;set;} }
