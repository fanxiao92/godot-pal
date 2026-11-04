using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Godot.Collections;
using Pal.Core;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalDialogueLanguage : Resource
{
    [Export] public int FormatVersion { get; set; } = 1;
    [Export] public string Language { get; set; } = "zh-Hans";
    [Export] public string SourceIdentity { get; set; } = "";
    [Export] public Array<PalDialogueMessageRange> Messages { get; set; } = new();

    [Export] public Godot.Collections.Dictionary<int, string> Words { get; set; } = new();

    public IReadOnlyDictionary<int, string> CopyWords()
    {
        if (Words is null || Words.Any(x => x.Key is < 0 or > 65535 || x.Value is null))
            throw new InvalidDataException("Language Words requires WORD IDs and non-null text.");
        return new System.Collections.ObjectModel.ReadOnlyDictionary<int, string>(Words.ToDictionary(x => x.Key, x => x.Value));
    }

    public DialogueLanguagePackage ToCorePackage()
    {
        if (FormatVersion != 1 || Language is not ("zh-Hans" or "en"))
            throw new InvalidDataException("Dialogue language resource has an unsupported version or identity.");
        _ = CopyWords();
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
