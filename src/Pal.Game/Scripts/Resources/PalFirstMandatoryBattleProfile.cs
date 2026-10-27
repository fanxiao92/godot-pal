using Godot;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalFirstMandatoryBattleProfile : Resource
{
    [Export] public int FormatVersion { get; set; }
    [Export] public string ProfileId { get; set; } = "";
    [Export] public string ContextSha256 { get; set; } = "";
    [Export] public byte[] IndexBytes { get; set; } = System.Array.Empty<byte>();
    [Export] public byte[] ContextBytes { get; set; } = System.Array.Empty<byte>();
    [Export] public string StaticResources { get; set; } = "";
    [Export] public string StaticProfile { get; set; } = "";
    [Export] public string StaticSceneSha256 { get; set; } = "";
    [Export] public string StaticProfileSha256 { get; set; } = "";
    [Export] public int SceneId { get; set; }
    [Export] public int MapId { get; set; }
    [Export] public int OwnerId { get; set; }
    [Export] public int TriggerAddress { get; set; }
    [Export] public string[] ResourceIds { get; set; } = System.Array.Empty<string>();
    [Export] public string[] ResourceReferences { get; set; } = System.Array.Empty<string>();
    [Export] public string[] ResourceSha256 { get; set; } = System.Array.Empty<string>();
}
