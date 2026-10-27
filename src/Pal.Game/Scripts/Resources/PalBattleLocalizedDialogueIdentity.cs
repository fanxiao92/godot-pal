using Godot;

namespace Pal.Game.Resources;

[GlobalClass] public partial class PalBattleLocalizedDialogueIdentity : PalBattleClosureIdentity { [Export] public string[] Languages {get;set;}=[]; [Export] public string[] Contents {get;set;}=[]; }
