using Pal.Core.Exploration;
using Pal.Core.Events;

namespace Pal.Core.Legacy;

/// <summary>Pre-battle exploration boundary for the authored scene2/map12/owner28 context.</summary>
public sealed class FirstMandatoryBattleSession
{
    public const string UnsupportedTriggerDiagnostic =
        "First mandatory battle trigger @09C3 is unavailable: the real resource closure was not verified; no side effects were applied.";

    private readonly ExplorationSession exploration;
    private readonly ExplorationInput input;
    private readonly FirstMandatoryBattleClosureReceipt? closure;
    private bool searchPressed;
    private bool hasUpdated;
    private TriggerScriptSession? initializedTrigger;
    private TriggerScriptSession? boundTrigger;
    private Guid boundCall;
    private bool returning;
    private uint returnDeadline;

    public FirstMandatoryBattleSessionState State { get; }
    /// <summary>The active exploration rules (host world wiring reuses them).</summary>
    public StaticSceneRules Rules => exploration.Rules;
    /// <summary>The @09C3 entry opened by a verified real closure at the committed search update.
    /// While set, the opened trigger owns further advancement; the host consumes it.</summary>
    public FirstMandatoryBattleTriggerRequest? PendingTrigger { get; private set; }
    /// <summary>Origin of the current committed search, separate from the two-field request value.</summary>
    public Guid PendingRequestId { get; private set; }
    public ExplorationState Current => exploration.Current;
    public IReadOnlyList<SceneObject> Objects => exploration.Objects;

    public FirstMandatoryBattleSession(FirstMandatoryBattleProfile profile, int sceneId, StaticSceneRules staticRules, StaticLeader staticLeader,
        FirstMandatoryBattleClosureReceipt? closureReceipt = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(staticRules);
        ArgumentNullException.ThrowIfNull(staticLeader);
        if (closureReceipt is not null && !StringComparer.Ordinal.Equals(closureReceipt.ContextSha256, profile.ContextSha256))
            throw new InvalidDataException("First mandatory battle closure receipt belongs to a different battle context.");
        closure = closureReceipt;
        State = profile.CreateSessionState();
        if (sceneId != 2 || State.World is not { SceneId: 2, MapId: 12 } || staticRules.MapId != 12
            || staticRules.Objects.All(item => item.Id != 28))
            throw new InvalidDataException("First mandatory battle requires scene2/map12 static rules containing owner28.");

        var snapshots = State.World.EventObjects.ToDictionary(item => item.OwnerId);
        var objects = staticRules.Objects.Select(item => snapshots.TryGetValue(item.Id, out BattleEventObjectState? snapshot)
            ? item with
            {
                X = checked((ushort)snapshot.X), Y = checked((ushort)snapshot.Y), Direction = checked((ushort)snapshot.Direction),
                State = checked((short)snapshot.State), VanishTime = checked((short)snapshot.VanishTime),
                TriggerMode = checked((ushort)snapshot.TriggerMode), TriggerEntry = checked((ushort)snapshot.TriggerEntry),
                SpriteFrames = checked((ushort)snapshot.SpriteFrames), CurrentFrame = checked((ushort)snapshot.CurrentFrame),
                Layer = checked((short)snapshot.Layer)
            }
            : item).ToArray();
        StaticSceneRules rules = staticRules.WithObjects(objects);
        BattleLeaderState authored = State.World.Leader;
        var leader = staticLeader with
        {
            X = checked((short)authored.X), Y = checked((short)authored.Y), Direction = Direction(authored.Direction),
            Layer = checked((ushort)authored.Layer), StepPhase = authored.Phase
        };
        SceneSearchMatch? hit = rules.FindSearchTarget(leader);
        if (hit is null || hit.ObjectId != 28 || rules.IsBlocked(leader.X, leader.Y))
            throw new InvalidDataException("First mandatory battle start/search identity does not match owner28 collision proof.");
        exploration = new ExplorationSession(rules, leader);
        input = new ExplorationInput(exploration);
    }

    public void Press(ExplorationKey key, bool repeat = false) => input.Press(key, repeat);
    public void Release(ExplorationKey key) => input.Release(key);
    public void PressSearch() => searchPressed = true;

    /// <summary>Associates the production world once, before any exploration update or script Start.</summary>
    public bool InitializeTrigger(TriggerScriptSession trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        if (initializedTrigger is not null || hasUpdated || PendingTrigger is not null
            || trigger.CallId != Guid.Empty || trigger.BoundWorld is not { SceneId: 2 } world
            || world.Leader with { Sprite = Current.Leader.Sprite } != Current.Leader
            || world.Leader.Sprite.SpriteId != Current.Leader.Sprite.SpriteId
            || !trigger.Entries.Any(entry => entry is { OwnerType: "eventObject", OwnerId: 28,
                Slot: "wTriggerScript", Kind: "trigger", SourceAddress: 0x09c3 })
            || Objects.Any(item => world.Find(item.Id) is not { } actual
                || actual.Initial.SpriteNumber != item.SpriteNumber || actual.Initial.SpriteFrames != item.SpriteFrames
                || actual.Initial.Layer != item.Layer)) return false;
        initializedTrigger = trigger;
        return true;
    }

    /// <summary>Binds the actual successful Start once, including synchronous endkeep calls.</summary>
    public bool BindTrigger(Guid requestId, TriggerScriptSession trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        if (boundTrigger is not null || !OwnsRequest(requestId, trigger)) return false;
        boundTrigger = trigger;
        boundCall = trigger.CallId;
        return true;
    }

    /// <summary>Releases only the bound completed outer call, not a battle/display acknowledgement.</summary>
    public bool CompleteTrigger(Guid requestId, TriggerScriptSession trigger, uint ticksMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        if (!OwnsRequest(requestId, trigger) || !ReferenceEquals(trigger, boundTrigger)
            || trigger.CallId != boundCall || trigger.Phase != TriggerScriptPhase.Completed
            || trigger.ReturnEntry != PendingTrigger!.EntryAddress) return false;

        if (trigger.BoundWorld is { } world)
        {
            // Only the original current-scene set belongs in exploration. Global hook contexts
            // remain in the production world, never becoming collision/search/render objects.
            var objects = Objects.Select(item =>
            {
                var actual = world.Find(item.Id)!;
                return item with
                {
                    X = unchecked((ushort)actual.X), Y = unchecked((ushort)actual.Y),
                    Direction = unchecked((ushort)actual.Direction), CurrentFrame = unchecked((ushort)actual.CurrentFrame),
                    State = unchecked((short)actual.State), VanishTime = unchecked((short)actual.VanishTime),
                    TriggerMode = unchecked((ushort)actual.TriggerMode), AutoEntry = unchecked((ushort)actual.AutoEntry),
                    TriggerEntry = item.Id == PendingTrigger.OwnerId ? (ushort)trigger.ReturnEntry : unchecked((ushort)actual.TriggerEntry)
                };
            }).ToArray();
            var rules = Rules.WithObjects(objects);
            var leader = world.Leader with { Sprite = Current.Leader.Sprite };
            // ReplaceRules builds a plan after assigning its rules: validate first so rejection
            // cannot publish a partial rule set or consume the owned request.
            try { rules.BuildPlan(leader); }
            catch (InvalidOperationException) { return false; }
            catch (ArgumentException) { return false; }
            exploration.ReplaceRules(rules, leader);
            world.Find(PendingTrigger.OwnerId)!.TriggerEntry = trigger.ReturnEntry;
            foreach (var row in State.World.EventObjects)
            {
                if (world.Find(row.OwnerId) is not { } actual) continue;
                row.X = unchecked((ushort)actual.X); row.Y = unchecked((ushort)actual.Y);
                row.Direction = unchecked((ushort)actual.Direction); row.CurrentFrame = unchecked((ushort)actual.CurrentFrame);
                row.State = unchecked((short)actual.State); row.VanishTime = unchecked((short)actual.VanishTime);
                row.TriggerMode = unchecked((ushort)actual.TriggerMode); row.TriggerEntry = unchecked((ushort)actual.TriggerEntry);
                row.AutoEntry = unchecked((ushort)actual.AutoEntry);
                // This facade never materializes or executes auto counters; retain the opaque source WORD.
            }
            SaveLegacyLeader(leader);
        }
        else
        {
            exploration.SaveTriggerEntry((ushort)PendingTrigger.OwnerId, (ushort)trigger.ReturnEntry);
            State.World.EventObjects.Single(item => item.OwnerId == PendingTrigger.OwnerId).TriggerEntry = trigger.ReturnEntry;
        }
        PendingTrigger = null;
        PendingRequestId = Guid.Empty;
        boundTrigger = null;
        boundCall = Guid.Empty;
        returning = true;
        returnDeadline = unchecked(ticksMilliseconds + 50);
        return true;
    }

    private bool OwnsRequest(Guid requestId, TriggerScriptSession trigger) =>
        (initializedTrigger is null ? trigger.BoundWorld is null : ReferenceEquals(trigger, initializedTrigger))
        && PendingTrigger is { } request && requestId != Guid.Empty && requestId == PendingRequestId
        && trigger.SourceRequestId == requestId && trigger.CallId != Guid.Empty
        && trigger.Caller == request.OwnerId
        && trigger.StartedEntry is { OwnerType: "eventObject", OwnerId: 28, Slot: "wTriggerScript",
            Kind: "trigger", SourceAddress: 0x09c3 };

    public bool Update(uint ticksMilliseconds)
    {
        hasUpdated = true;
        if (PendingTrigger is not null) return false;
        if (returning)
        {
            // SDL_TICKS_PASSED: tick differences must be smaller than 2^31 ms.
            if (unchecked((int)(returnDeadline - ticksMilliseconds)) > 0) return false;
            searchPressed = false; // PAL_ClearKeyState retains held direction and alias order.
            returning = false;
        }
        if (!input.IsUpdateDue(ticksMilliseconds)) return false;
        if (searchPressed)
        {
            searchPressed = false;
            if (exploration.FindSearchTarget() is { ObjectId: 28 })
            {
                if (closure is null || Objects.Single(item => item.Id == 28).TriggerEntry != 0x09c3)
                    throw new InvalidOperationException(UnsupportedTriggerDiagnostic);
                // The real entry is open: hand @09C3 to the host before any side effect. The
                // unit 2 host executes it; exploration stays parked at this boundary.
                PendingTrigger = new FirstMandatoryBattleTriggerRequest(0x09c3, 28);
                PendingRequestId = Guid.NewGuid();
                return true;
            }
        }
        bool updated = input.Update(ticksMilliseconds);
        if (updated)
        {
            StaticLeader leader = exploration.Current.Leader;
            if (initializedTrigger?.BoundWorld is { } world) world.Leader = leader;
            SaveLegacyLeader(leader);
        }
        return updated;
    }

    private void SaveLegacyLeader(StaticLeader leader)
    {
        State.World.Leader.X = leader.X;
        State.World.Leader.Y = leader.Y;
        State.World.Leader.Direction = DirectionName(leader.Direction);
        State.World.Leader.Layer = leader.Layer;
        State.World.Leader.Phase = leader.StepPhase;
    }

    private static string DirectionName(int direction) => direction switch
    {
        0 => "south", 1 => "west", 2 => "north", 3 => "east",
        _ => throw new InvalidDataException("First mandatory battle leader direction is invalid.")
    };

    private static int Direction(string direction) => direction switch
    {
        "south" => 0, "west" => 1, "north" => 2, "east" => 3,
        _ => throw new InvalidDataException("First mandatory battle leader direction is invalid.")
    };
}
