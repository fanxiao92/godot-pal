using Godot;

namespace Pal.Game.Resources;

[GlobalClass] public partial class PalBattleIdentity : Resource { [Export] public string SnapshotKind {get;set;}=""; [Export] public int SceneId {get;set;} [Export] public int MapId {get;set;} [Export] public int OwnerId {get;set;} [Export] public int TriggerAddress {get;set;} }
