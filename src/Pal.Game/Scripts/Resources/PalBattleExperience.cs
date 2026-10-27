using Godot;

namespace Pal.Game.Resources;

[GlobalClass] public partial class PalBattleExperience : Resource { [Export] public int Value {get;set;} [Export] public int Level {get;set;} [Export] public int Count {get;set;} }
