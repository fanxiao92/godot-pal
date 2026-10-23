namespace Pal.Core;

/// <summary>Modern, language-specific display content keyed by original message ranges.</summary>
public sealed class DialogueLanguagePackage
{
    private readonly Dictionary<(int StartMessageId, int Span), DialogueMessageBlock> _blocks;

    public string Language { get; }

    public DialogueLanguagePackage(string language, IEnumerable<DialogueMessageBlock> blocks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        if (language is not ("zh-Hans" or "en"))
            throw new ArgumentException("Dialogue language must be zh-Hans or en.", nameof(language));
        ArgumentNullException.ThrowIfNull(blocks);
        Language = language;
        _blocks = new Dictionary<(int, int), DialogueMessageBlock>();
        foreach (DialogueMessageBlock block in blocks)
        {
            ArgumentNullException.ThrowIfNull(block);
            if (!_blocks.TryAdd((block.StartMessageId, block.Span), block))
                throw new InvalidDataException($"Duplicate message range {block.StartMessageId}+{block.Span}.");
        }
    }

    public DialogueMessageBlock Resolve(int startMessageId, int span)
    {
        if (startMessageId < 0 || startMessageId > ushort.MaxValue || span < 0 || span > ushort.MaxValue - startMessageId)
            throw new InvalidDataException("Message reference is outside the original WORD range.");
        return _blocks.TryGetValue((startMessageId, span), out DialogueMessageBlock? block)
            ? block
            : throw new InvalidDataException($"Language '{Language}' is missing message range {startMessageId}+{span}.");
    }
}

/// <summary>A source message section and its ordered text/clear entries.</summary>
public sealed record DialogueMessageBlock
{
    public int StartMessageId { get; }
    public int EndMessageId { get; }
    public int Span => EndMessageId - StartMessageId;
    public IReadOnlyList<DialogueMessagePart> Parts { get; }

    public DialogueMessageBlock(int startMessageId, int endMessageId, IEnumerable<DialogueMessagePart> parts)
    {
        if (startMessageId is < 0 or > ushort.MaxValue || endMessageId < startMessageId || endMessageId > ushort.MaxValue)
            throw new InvalidDataException("Message range is outside the original WORD range.");
        ArgumentNullException.ThrowIfNull(parts);
        StartMessageId = startMessageId;
        EndMessageId = endMessageId;
        Parts = Array.AsReadOnly(parts.ToArray());
        if (Parts.Count == 0) throw new InvalidDataException("Message range has no display entries.");
        if (Parts.Any(part => part is null || (part.IsClear && part.Text is not null) || (!part.IsClear && part.Text is null)))
            throw new InvalidDataException("Message entry must be text or CLEAR, but not both.");
    }
}

/// <summary>One source display entry; an empty string remains distinct from CLEAR.</summary>
public sealed record DialogueMessagePart
{
    public string? Text { get; }
    public bool IsClear { get; }

    private DialogueMessagePart(string? text, bool isClear) => (Text, IsClear) = (text, isClear);

    public static DialogueMessagePart Clear { get; } = new(null, true);
    public static DialogueMessagePart FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new DialogueMessagePart(text, false);
    }
}
