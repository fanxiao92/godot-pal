using Godot;
using Godot.Collections;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalDialogueMessageRange : Resource
{
    [Export] public int StartMessageId { get; set; }
    [Export] public int EndMessageId { get; set; }
    [Export] public Array<PalDialogueMessageEntry> Entries { get; set; } = new();
}
