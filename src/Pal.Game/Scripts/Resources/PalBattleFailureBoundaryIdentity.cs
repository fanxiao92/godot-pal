using Godot;

namespace Pal.Game.Resources;

[GlobalClass] public partial class PalBattleFailureBoundaryIdentity : PalBattleClosureIdentity { [Export] public int LostEntryAddress {get;set;} [Export] public int ReloadCommand {get;set;} [Export] public string Behavior {get;set;}=""; }
