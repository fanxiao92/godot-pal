using Godot;
using Godot.Collections;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalFirstMandatoryBattleProfile : Resource
{
    [Export] public int FormatVersion { get; set; }
    [Export] public string ProfileId { get; set; } = "";
    [Export] public string ContextSha256 { get; set; } = "";
    [Export] public string NativeEnvelopeSha256 { get; set; } = "";
    [Export] public PalBattleIdentity? Identity { get; set; }
    [Export] public PalBattleProvenance? Provenance { get; set; }
    [Export] public PalBattleLayout? Layout { get; set; }
    [Export] public int[] PartyRoleIds { get; set; } = [];
    [Export] public PalBattleRole[] Roles { get; set; } = [];
    [Export] public PalBattleWorld? World { get; set; }
    [Export] public PalBattleClosure? Closure { get; set; }
    [Export] public PalBattleResourceReference[] Resources { get; set; } = [];
    [Export] public string StaticResources { get; set; } = "";
    [Export] public string StaticProfile { get; set; } = "";
    [Export] public string StaticSceneSha256 { get; set; } = "";
    [Export] public string StaticProfileSha256 { get; set; } = "";
    /// <summary>#53 real resource closure binding. Empty keeps the pre-#53 profile shape
    /// (no receipt, trigger stays closed); a partial binding is rejected.</summary>
    [Export] public string EventsIndex { get; set; } = "";
    [Export] public string BattleContentUri { get; set; } = "";
    [Export] public string DialogueDirectory { get; set; } = "";
    [Export] public string VisualUri { get; set; } = "";
    [Export] public string GlobalObjectsUri { get; set; } = "";
    [Export] public Dictionary<int, string> Portraits { get; set; } = new();
    [Export] public Dictionary<string, string> ResourceSha256 { get; set; } = new();
    [Export] public string[] ImagePlayerSpriteUris { get; set; } = [];
    [Export] public int ImageEnemyId { get; set; } = -1;
    [Export] public string ImageEnemyArchiveEntry { get; set; } = "";
    [Export] public string[] ImageRoleSpriteIds { get; set; } = [];
    [Export] public int ImageBattlefieldId { get; set; } = -1;
    [Export] public string ImageBattlefieldArchiveEntry { get; set; } = "";
    [Export] public bool ImageIncludesLayoutAndEffectFrames { get; set; }
}
