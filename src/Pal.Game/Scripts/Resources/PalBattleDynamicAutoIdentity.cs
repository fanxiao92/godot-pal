using Godot;

namespace Pal.Game.Resources;

[GlobalClass] public partial class PalBattleDynamicAutoIdentity : PalBattleClosureIdentity { [Export] public int StartAddress {get;set;} [Export] public int EndAddress {get;set;} [Export] public string[] Effects {get;set;}=[]; [Export] public string Advance {get;set;}=""; }
