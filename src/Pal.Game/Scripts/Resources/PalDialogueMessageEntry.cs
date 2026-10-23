using Godot;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalDialogueMessageEntry : Resource
{
    public enum EntryKind { Text, Clear }

    [Export] public EntryKind Kind { get; set; } = EntryKind.Text;
    [Export] public string Text { get; set; } = string.Empty;
}
