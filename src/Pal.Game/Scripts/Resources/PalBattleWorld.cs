using Godot;

namespace Pal.Game.Resources;

[GlobalClass] public partial class PalBattleWorld : Resource { [Export] public int SceneId {get;set;} [Export] public int MapId {get;set;} [Export] public int PaletteId {get;set;} [Export] public bool NightPalette {get;set;} [Export] public int MusicId {get;set;} [Export] public int BattleMusicId {get;set;} [Export] public PalBattleLeader? Leader {get;set;} [Export] public PalBattleSearchProof? SearchProof {get;set;} [Export] public PalBattleSceneEntry[] SceneEntries {get;set;}=[]; [Export] public PalBattleEventObject[] EventObjects {get;set;}=[]; }
