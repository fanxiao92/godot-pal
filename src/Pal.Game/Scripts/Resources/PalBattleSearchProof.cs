using Godot;

namespace Pal.Game.Resources;

[GlobalClass] public partial class PalBattleSearchProof : Resource { [Export] public int OwnerId {get;set;} [Export] public int SampleIndex {get;set;} [Export] public bool CollisionPass {get;set;} }
