using Godot;

namespace Pal.Game.Resources;

[GlobalClass] public partial class PalBattleSceneEntry : Resource { [Export] public int SceneId {get;set;} [Export] public int EnterEntry {get;set;} [Export] public int TeleportEntry {get;set;} }
