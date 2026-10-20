using Godot;
using Godot.Collections;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalSceneData : Resource
{
    [Export] public int SceneId { get; set; } = 0;
    [Export] public int MapId { get; set; } = 0;
    [Export] public int WScriptOnEnter { get; set; } = 0;
    [Export] public int WScriptOnTeleport { get; set; } = 0;
    [Export] public int WEventObjectIndex { get; set; } = 0;
    [Export] public int NextEventObjectIndex { get; set; } = 0;
    [Export] public string BundleSha256 { get; set; } = "";
    [Export] public Dictionary Source { get; set; } = new();
    [Export] public Dictionary ObjectsSource { get; set; } = new();
    [Export] public Dictionary Positioning { get; set; } = new();
    [Export] public Godot.Collections.Array<Dictionary> ArchiveSources { get; set; } = new();
    [Export] public int[] SpriteIds { get; set; } = System.Array.Empty<int>();
    [Export] public Godot.Collections.Array<PalSprite> Sprites { get; set; } = new();
    [Export] public Godot.Collections.Array<PalSceneObject> Objects { get; set; } = new();
    [Export] public Godot.Collections.Array<PalPartyMember> DefaultParty { get; set; } = new();
    [Export] public PalMap Map { get; set; } = null!;
    [Export] public PalPalette Palette { get; set; } = null!;
}
