using Godot;

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
}
