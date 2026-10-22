namespace Pal.Core;

public enum NpcInteractionPhase { Exploring, Trigger, Returning }

public sealed record NpcInteractionRequest(Guid Id, string FileId, string EntryId, int Caller,
    int SourceAddress, int SampleIndex);

/// <summary>Independent owner-60 interaction context, not a normal scene event scheduler.
/// Owns exploration updates; the host alone starts and advances the requested trigger.</summary>
public sealed class NpcInteractionSession
{
    private readonly ExplorationSession exploration;
    private readonly ExplorationInput input;
    private readonly TriggerEventEntry entry;
    private bool searchPressed;
    private uint returnDeadline;
    public ExplorationState Current => exploration.Current;
    public IReadOnlyList<SceneObject> Objects => exploration.Objects;
    public NpcInteractionPhase Phase { get; private set; }
    public bool BlocksSceneUpdates => Phase != NpcInteractionPhase.Exploring;
    public NpcInteractionRequest? PendingTrigger { get; private set; }

    public NpcInteractionSession(StaticSceneRules rules, StaticLeader leader, TriggerEventEntry selectedEntry)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(selectedEntry);
        if (selectedEntry is not { OwnerType: "eventObject", OwnerId: 60, Slot: "wTriggerScript", Kind: "trigger", SourceAddress: 0x0188 })
            throw new InvalidDataException("Independent NPC interaction requires owner 60 / wTriggerScript @0188.");
        if (rules.Objects.SingleOrDefault(o => o.Id == 60)?.TriggerEntry != selectedEntry.SourceAddress)
            throw new InvalidDataException("Owner 60 runtime trigger entry does not match the selected event entry @0188.");
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
    public bool CompleteTrigger(Guid requestId, TriggerScriptSession trigger, uint ticksMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        if (PendingTrigger is not { } request || request.Id != requestId ||
            trigger.Phase != TriggerScriptPhase.Completed || trigger.Caller != request.Caller ||
            trigger.ReturnEntry != request.SourceAddress) return false;
        exploration.SaveTriggerEntry((ushort)request.Caller, (ushort)trigger.ReturnEntry);
        PendingTrigger = null;
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
                throw new InvalidDataException($"Search hit owner {hit.ObjectId} at sample {hit.SampleIndex}; independent context only supports owner 60 @0188.");
            exploration.PrepareSearch(hit.ObjectId);
            PendingTrigger = new(Guid.NewGuid(), entry.FileId, entry.Id, hit.ObjectId, entry.SourceAddress, hit.SampleIndex);
            Phase = NpcInteractionPhase.Trigger;
        }
        return true;
    }
}
