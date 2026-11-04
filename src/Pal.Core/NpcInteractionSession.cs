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

    public NpcInteractionSession(StaticSceneRules rules, StaticLeader leader, TriggerEventEntry selectedEntry)
        : this(rules, leader, selectedEntry, false) { }

    /// <summary>Uses the current trigger VM support domain: selected-entry calls end with keep.
    /// The host must bind the actual successful Start before delivering completion.</summary>
    public static NpcInteractionSession ForSelectedEntry(StaticSceneRules rules, StaticLeader leader, TriggerEventEntry entry)
        => new(rules, leader, entry, true);

    private NpcInteractionSession(StaticSceneRules rules, StaticLeader leader, TriggerEventEntry selectedEntry, bool selectedMode)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(selectedEntry);
        if (!selectedMode && selectedEntry is not { OwnerType: "eventObject", OwnerId: 60, Slot: "wTriggerScript", Kind: "trigger", SourceAddress: 0x0188 })
            throw new InvalidDataException("Independent NPC interaction requires owner 60 / wTriggerScript @0188.");
        if (selectedEntry is not { OwnerType: "eventObject", Slot: "wTriggerScript", Kind: "trigger", SourceAddress: > 0 and <= 65535 }
            || rules.Objects.SingleOrDefault(o => o.Id == selectedEntry.OwnerId)?.TriggerEntry != selectedEntry.SourceAddress)
            throw new InvalidDataException(selectedMode ? "Selected owner runtime trigger entry does not match the event entry."
                : "Owner 60 runtime trigger entry does not match the selected event entry @0188.");
        this.selectedMode = selectedMode;
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

    public bool CompleteTrigger(Guid requestId, TriggerScriptSession trigger, uint ticksMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        if (PendingTrigger is not { } request || request.Id != requestId ||
            trigger.Phase != TriggerScriptPhase.Completed || trigger.Caller != request.Caller ||
            trigger.ReturnEntry != request.SourceAddress
            || selectedMode && (trigger.SourceRequestId != request.Id || trigger.StartedEntry != entry
                || !ReferenceEquals(trigger, boundTrigger) || trigger.CallId != boundCall)) return false;
        exploration.SaveTriggerEntry((ushort)request.Caller, (ushort)trigger.ReturnEntry);
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
