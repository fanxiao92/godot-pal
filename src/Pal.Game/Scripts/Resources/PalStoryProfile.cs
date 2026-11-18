using Godot;
using Godot.Collections;

namespace Pal.Game.Resources;

/// <summary>Authored native content binding. SourceIdentity is a declaration, not authentication.</summary>
[GlobalClass]
public partial class PalStoryProfile : Resource
{
    [Export] public int FormatVersion { get; set; } = -1;
    [Export] public string StaticResources { get; set; } = "";
    [Export] public string StaticProfile { get; set; } = "";
    [Export] public string BundleSha256 { get; set; } = "";
    [Export] public string EventsIndex { get; set; } = "";
    [Export] public string EntryFileId { get; set; } = "";
    [Export] public string EntryId { get; set; } = "";
    [Export] public int OwnerId { get; set; } = -1;
    [Export] public int EntryAddress { get; set; } = -1;
    [Export] public string BattleContentUri { get; set; } = "";
    [Export] public string DialogueDirectory { get; set; } = "";
    [Export] public string SourceIdentity { get; set; } = "";
    [Export] public Dictionary<int, string> Portraits { get; set; } = new();
    // #218 BALL item bitmaps: object WORD -> prepared native item-bitmap sprite URI (the
    // export-battle-images ball package -> PrepareItemBitmaps chain). Coverage must match the
    // use-menu bindings with a nonzero bitmap word exactly; stories without that context stay empty.
    [Export] public Dictionary<int, string> ItemBitmaps { get; set; } = new();
    [Export] public Dictionary<string, string> ResourceSha256 { get; set; } = new();
}
