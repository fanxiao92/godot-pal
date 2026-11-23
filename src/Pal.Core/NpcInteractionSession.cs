namespace Pal.Core;

public enum NpcInteractionPhase { Exploring, Trigger, Returning }

public sealed record NpcInteractionRequest(Guid Id, string FileId, string EntryId, int Caller,
    int SourceAddress, int SampleIndex);

/// <summary>Selected owner-trigger interaction context, not a general scene event scheduler.
/// Owns exploration updates; the host alone starts and advances the requested trigger.</summary>
public sealed class NpcInteractionSession
{
    private readonly ExplorationSession exploration;
    private readonly ExplorationInput input;
    private readonly TriggerEventEntry entry;
    // The runtime world the production host consumes (save chain, world->rules projection). When
    // bound, completion writes the returned word to this world's object (play.c 153); the static
    // exploration copy is only the legacy host surface and is left untouched in that mode.
    private readonly DynamicEventWorld? world;
    private readonly bool selectedMode;
    private TriggerScriptSession? boundTrigger;
    private Guid boundCall;
    private bool searchPressed;
    private uint returnDeadline;
    public ExplorationState Current => exploration.Current;
    public IReadOnlyList<SceneObject> Objects => exploration.Objects;
    public NpcInteractionPhase Phase { get; private set; }
    public bool BlocksSceneUpdates => Phase != NpcInteractionPhase.Exploring;
    public NpcInteractionRequest? PendingTrigger { get; private set; }

    public NpcInteractionSession(StaticSceneRules rules, StaticLeader leader, TriggerEventEntry selectedEntry,
        DynamicEventWorld? world = null)
        : this(rules, leader, selectedEntry, false, world) { }

    /// <summary>Uses the current trigger VM support domain: the selected entry's own completed
    /// call is bound by the host, and its published wNextScriptEntry is written back on completion
    /// to the bound runtime world when one is supplied, otherwise to the static exploration copy.
    /// The host must bind the actual successful Start before delivering completion.</summary>
    public static NpcInteractionSession ForSelectedEntry(StaticSceneRules rules, StaticLeader leader, TriggerEventEntry entry,
        DynamicEventWorld? world = null)
        => new(rules, leader, entry, true, world);

    private NpcInteractionSession(StaticSceneRules rules, StaticLeader leader, TriggerEventEntry selectedEntry, bool selectedMode,
        DynamicEventWorld? world = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(selectedEntry);
        if (!selectedMode && selectedEntry is not { OwnerType: "eventObject", OwnerId: 60, Slot: "wTriggerScript", Kind: "trigger", SourceAddress: 0x0188 })
            throw new InvalidDataException("Independent NPC interaction requires owner 60 / wTriggerScript @0188.");
        if (selectedEntry is not { OwnerType: "eventObject", Slot: "wTriggerScript", Kind: "trigger", SourceAddress: > 0 and <= 65535 })
            throw new InvalidDataException(selectedMode ? "Selected owner runtime trigger entry does not match the event entry."
                : "Owner 60 runtime trigger entry does not match the selected event entry @0188.");
        // #227 (matrix D11) restores the retained dispatch word into the bound runtime world: the
        // production completion writes the returned WORD there (play.c 153) and the save chain
        // captures exactly that value, so a committed load must rebuild this interaction over a
        // world whose object word has moved off the authored entry. The restored world object
        // entry is authoritative retained state and is accepted (and left untouched); the authored
        // selected entry stays the checked host dispatch closure, the same split the #125 dynamic
        // host keeps between its world word and its authored entry — never an unvalidated address
        // from file bytes. The legacy static host keeps the exact word/entry equality.
        bool runtimeMatches;
        if (world is null)
        {
            var runtimeObject = rules.Objects.SingleOrDefault(o => o.Id == selectedEntry.OwnerId);
            runtimeMatches = runtimeObject is not null && runtimeObject.TriggerEntry == selectedEntry.SourceAddress;
        }
        else runtimeMatches = world.Find(selectedEntry.OwnerId) is not null;
        if (!runtimeMatches)
            throw new InvalidDataException(selectedMode ? "Selected owner runtime trigger entry does not match the event entry."
                : "Owner 60 runtime trigger entry does not match the selected event entry @0188.");
        this.selectedMode = selectedMode;
        this.world = world;
        entry = selectedEntry;
        exploration = new(rules, leader);
        input = new(exploration);
    }

    public void Press(ExplorationKey key, bool repeat = false) => input.Press(key, repeat);
    public void Release(ExplorationKey key) => input.Release(key);
    // Each delivered search key-down (including OS repeat) is a transient press, not a held direction.
    public void PressSearch() => searchPressed = true;

    /// <summary>Receipt from the host's same trigger after restoration and completion.
    /// Unfinished calls, stale request IDs and caller/entry mismatches do not release exploration.</summary>
    public bool BindTrigger(Guid requestId, TriggerScriptSession trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        if (!selectedMode || boundTrigger is not null || PendingTrigger is not { } request || request.Id != requestId
            || trigger.SourceRequestId != request.Id || trigger.CallId == Guid.Empty
            || trigger.Caller != request.Caller || trigger.StartedEntry != entry) return false;
        boundTrigger = trigger;
        boundCall = trigger.CallId;
        return true;
    }

    /// <summary>Receipt from the host's same bound call; the completed call's published
    /// wNextScriptEntry is written back to the searched object (play.c 153): the runtime world's
    /// object when one is bound, otherwise the static exploration copy. Unfinished, stale, foreign
    /// or rebinding calls are inert.</summary>
    public bool CompleteTrigger(Guid requestId, TriggerScriptSession trigger, uint ticksMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        if (PendingTrigger is not { } request || request.Id != requestId ||
            trigger.Phase != TriggerScriptPhase.Completed || trigger.Caller != request.Caller ||
            trigger.StartedEntry != entry
            || selectedMode && (trigger.SourceRequestId != request.Id
                || !ReferenceEquals(trigger, boundTrigger) || trigger.CallId != boundCall)) return false;
        if (world is null)
        {
            exploration.SaveTriggerEntry((ushort)request.Caller, (ushort)trigger.ReturnEntry);
        }
        else
        {
            var owner = world.Find(request.Caller)
                ?? throw new InvalidDataException($"Completed trigger caller {request.Caller} has no event object in the bound world.");
            owner.TriggerEntry = (ushort)trigger.ReturnEntry;
        }
        PendingTrigger = null;
        boundTrigger = null;
        boundCall = Guid.Empty;
        Phase = NpcInteractionPhase.Returning;
        returnDeadline = unchecked(ticksMilliseconds + 50);
        return true;
    }

    public bool Update(uint ticksMilliseconds)
    {
        if (Phase == NpcInteractionPhase.Trigger) return false;
        if (Phase == NpcInteractionPhase.Returning)
        {
            // SDL_TICKS_PASSED; same < 2^31 ms comparison contract as exploration.
            if (unchecked((int)(returnDeadline - ticksMilliseconds)) > 0) return false;
            searchPressed = false; // PAL_ClearKeyState clears transient presses, not held direction/order.
            Phase = NpcInteractionPhase.Exploring;
        }
        if (!input.Update(ticksMilliseconds)) return false;
        bool search = searchPressed;
        searchPressed = false;
        if (search && exploration.FindSearchTarget() is { } hit)
        {
            if (hit.ObjectId != entry.OwnerId)
                throw new InvalidDataException($"Search hit owner {hit.ObjectId} at sample {hit.SampleIndex}; selected context only supports owner {entry.OwnerId} @{entry.SourceAddress:X4}.");
            exploration.PrepareSearch(hit.ObjectId);
            PendingTrigger = new(Guid.NewGuid(), entry.FileId, entry.Id, hit.ObjectId, entry.SourceAddress, hit.SampleIndex);
            Phase = NpcInteractionPhase.Trigger;
        }
        return true;
    }
}
