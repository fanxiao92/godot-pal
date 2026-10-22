using Godot;
using Godot.Collections;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalNpcInteractionProfile : Resource
{
    [Export] public int FormatVersion { get; set; } = -1;
    [Export] public string StaticResources { get; set; } = "";
    [Export] public string StaticProfile { get; set; } = "";
    [Export] public string BundleSha256 { get; set; } = "";
    [Export] public string EventsIndex { get; set; } = "";
    [Export] public string DialogueDirectory { get; set; } = "";
    [Export] public Dictionary<string, string> ResourceSha256 { get; set; } = new();
}
