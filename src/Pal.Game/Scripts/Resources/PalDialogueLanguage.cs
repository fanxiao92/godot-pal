using Godot;
using Godot.Collections;
using Pal.Core;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalDialogueLanguage : Resource
{
    [Export] public int FormatVersion { get; set; } = 1;
    [Export] public string Language { get; set; } = "zh-Hans";
    [Export] public Array<PalDialogueMessageRange> Messages { get; set; } = new();

    public DialogueLanguagePackage ToCorePackage()
    {
        if (FormatVersion != 1 || Language is not ("zh-Hans" or "en"))
            throw new InvalidDataException("Dialogue language resource has an unsupported version or identity.");
        var blocks = new List<DialogueMessageBlock>();
        foreach (PalDialogueMessageRange message in Messages)
        {
            if (message is null) throw new InvalidDataException("Dialogue language resource contains a null message range.");
            if (message.Entries is null) throw new InvalidDataException("Dialogue message range has no entry collection.");
            var parts = new List<DialogueMessagePart>();
            foreach (PalDialogueMessageEntry entry in message.Entries)
            {
                if (entry is null) throw new InvalidDataException("Dialogue message range contains a null entry.");
                parts.Add(entry.Kind switch
                {
                    PalDialogueMessageEntry.EntryKind.Text => DialogueMessagePart.FromText(entry.Text),
                    PalDialogueMessageEntry.EntryKind.Clear => DialogueMessagePart.Clear,
                    _ => throw new InvalidDataException("Dialogue message entry kind is unsupported.")
                });
            }
            blocks.Add(new DialogueMessageBlock(message.StartMessageId, message.EndMessageId, parts));
        }
        return new DialogueLanguagePackage(Language, blocks);
    }
}
