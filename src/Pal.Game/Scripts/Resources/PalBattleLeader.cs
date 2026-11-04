using Godot;

namespace Pal.Game.Resources;

[GlobalClass] public partial class PalBattleLeader : Resource { [Export] public int X {get;set;} [Export] public int Y {get;set;} [Export] public string Direction {get;set;}=""; [Export] public int Layer {get;set;} [Export] public int Phase {get;set;} }
