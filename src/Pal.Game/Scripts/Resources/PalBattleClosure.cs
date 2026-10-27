using Godot;

namespace Pal.Game.Resources;

[GlobalClass] public partial class PalBattleClosure : Resource { [Export] public string Entry {get;set;}=""; [Export] public PalBattleClosureEntry[] Entries {get;set;}=[]; }
