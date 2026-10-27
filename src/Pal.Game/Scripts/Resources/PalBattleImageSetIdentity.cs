using Godot;

namespace Pal.Game.Resources;

[GlobalClass] public partial class PalBattleImageSetIdentity : PalBattleClosureIdentity { [Export] public int EnemyId {get;set;} [Export] public string EnemyArchiveEntry {get;set;}=""; [Export] public string[] RoleSpriteIds {get;set;}=[]; [Export] public int BattlefieldId {get;set;} [Export] public string BattlefieldArchiveEntry {get;set;}=""; [Export] public bool IncludesLayoutAndEffectFrames {get;set;} }
