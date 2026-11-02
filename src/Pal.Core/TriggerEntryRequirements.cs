namespace Pal.Core;

/// <summary>Pure conservative selected-entry requirements, not an executed call or a native-resource grant.</summary>
public sealed class TriggerEntryRequirements
{
    public IReadOnlyList<TriggerDisplayRequirement> DisplayBlocks { get; }
    public IReadOnlyList<int> PortraitIds { get; }
    public IReadOnlyList<int> BattleTeamIds { get; }

    internal TriggerEntryRequirements(IEnumerable<TriggerDisplayRequirement> displays, IEnumerable<int> portraits, IEnumerable<int> teams)
    {
        DisplayBlocks = Array.AsReadOnly(displays.ToArray());
        PortraitIds = Array.AsReadOnly(portraits.Order().ToArray());
        BattleTeamIds = Array.AsReadOnly(teams.Order().ToArray());
    }
}

public sealed class TriggerDisplayRequirement
{
    public DialogueMessageBlock Content { get; }
    public IReadOnlyList<TriggerMessageReference> MessageReferences { get; }

    internal TriggerDisplayRequirement(DialogueMessageBlock content, IEnumerable<TriggerMessageReference> references)
    {
        Content = content;
        MessageReferences = Array.AsReadOnly(references.ToArray());
    }
}
