namespace Pal.Core;

/// <summary>Mutable event-object word (script.c LPEVENTOBJECT subset): the dynamic fields the
/// #52 slice changes at runtime. Static identity (sprite, frames) stays with the initial record.</summary>
public sealed class DynamicEventObject(SceneObject initial)
{
    public int Id { get; } = initial.Id;
    public int X { get; internal set; } = initial.X;
    public int Y { get; internal set; } = initial.Y;
    public int Direction { get; internal set; } = initial.Direction;
    public int CurrentFrame { get; internal set; } = initial.CurrentFrame;
    public int State { get; internal set; } = initial.State;
    public int VanishTime { get; internal set; } = initial.VanishTime;
    public int TriggerMode { get; internal set; } = initial.TriggerMode;
    public int TriggerEntry { get; internal set; } = initial.TriggerEntry;
    public int AutoEntry { get; internal set; } = initial.AutoEntry;
    // script.c wScriptIdleFrameCountAuto: one counter per object, shared by the auto
    // end-redirect/goto limits and the auto frame wait; reset on fall-through.
    public int AutoIdleCount { get; internal set; }
    internal SceneObject Initial { get; } = initial;
}

public enum DynamicEventPhase { Exploring, Trigger, Returning }

public sealed record DynamicEventTriggerRequest(Guid Id, string FileId, string EntryId, int Caller, int SourceAddress);

/// <summary>The mutable world a dynamic-scene trigger script mutates (script.c/scene.c/play.c
/// subset). It owns the exploration session; the host never mutates objects directly.</summary>
public sealed class DynamicEventWorld
{
    private readonly IReadOnlyList<DynamicEventObject> objects;
    public IReadOnlyList<DynamicEventObject> Objects => objects;
    public int SceneId { get; }
    private readonly Dictionary<int, System.Tuple<int, int>> sceneScripts = new();
    private readonly Dictionary<(int ObjectId, int SlotIndex), int> objectScripts = new();
    public StaticLeader Leader { get; internal set; }
    /// <summary>script.c 0x0075 party roles (role+1 words); retained after the event.</summary>
    public IReadOnlyList<int> PartyRoles { get; internal set; } = [0];
    public IReadOnlyDictionary<int, System.Tuple<int, int>> SceneScripts => sceneScripts;
    public IReadOnlyDictionary<(int ObjectId, int SlotIndex), int> ObjectScripts => objectScripts;

    internal event Action? WorldChanged;

    internal DynamicEventWorld(StaticSceneRules rules, StaticLeader leader, int sceneId, int onEnter, int onTeleport)
    {
        objects = rules.Objects.Select(o => new DynamicEventObject(o)).ToArray().AsReadOnly();
        SceneId = sceneId;
        sceneScripts[sceneId] = System.Tuple.Create(onEnter, onTeleport);
        Leader = leader;
    }

    internal DynamicEventObject? Find(int objectId) => objects.FirstOrDefault(o => o.Id == objectId);

    internal DynamicEventObject Require(int objectId, TriggerPosition? position) =>
        Find(objectId) ?? throw new InvalidDataException($"{position}: event object {objectId} is not in the bound scene.");

    /// <summary>scene.c PAL_NPCWalkOneStep: direction then one step of ±2*iSpeed/±1*iSpeed with
    /// the 3-frame sprite cycling modulo 4.</summary>
    internal void WalkOneStep(int objectId, int direction, int speed)
    {
        var obj = objects.Single(o => o.Id == objectId);
        obj.Direction = direction;
        obj.X = unchecked((short)(obj.X + (direction is 0 or 1 ? -2 : 2) * speed));
        obj.Y = unchecked((short)(obj.Y + (direction is 1 or 2 ? -1 : 1) * speed));
        if (obj.Initial.SpriteFrames > 0)
            obj.CurrentFrame = (obj.CurrentFrame + 1) % (obj.Initial.SpriteFrames == 3 ? 4 : obj.Initial.SpriteFrames);
        WorldChanged?.Invoke();
    }

    /// <summary>script.c PAL_NPCWalkTo: face from the offsets, snap near the target, else one
    /// step; returns true only when both words equal the target (arrival resets the frame).</summary>
    internal bool WalkTowards(int objectId, int targetX, int targetY, int speed)
    {
        var obj = objects.Single(o => o.Id == objectId);
        int xOffset = targetX - obj.X, yOffset = targetY - obj.Y;
        obj.Direction = yOffset < 0 ? (xOffset < 0 ? 1 : 2) : (xOffset < 0 ? 0 : 3);
        if (Math.Abs(xOffset) < speed * 2 || Math.Abs(yOffset) < speed * 2)
        {
            obj.X = targetX;
            obj.Y = targetY;
        }
        else WalkOneStep(objectId, obj.Direction, speed);
        if (obj.X == targetX && obj.Y == targetY)
        {
            obj.CurrentFrame = 0;
            return true;
        }
        return false;
    }

    /// <summary>script.c 0x000F: caller object direction/gesture; 0xFFFF keeps the current word.</summary>
    internal void SetFacing(int objectId, int direction, int frame)
    {
        var obj = objects.Single(o => o.Id == objectId);
        if (direction != 0xffff) obj.Direction = direction;
        if (frame != 0xffff) obj.CurrentFrame = frame;
        WorldChanged?.Invoke();
    }

    /// <summary>script.c 0x0049: pCurrent state write (signed SHORT).</summary>
    internal void SetObjectState(int objectId, int state, TriggerPosition position)
    {
        Require(objectId, position).State = state;
        WorldChanged?.Invoke();
    }

    /// <summary>script.c 0x0052: state *= -1 and vanish time = frames (positive counts down).</summary>
    internal void HideObject(int objectId, int frames)
    {
        var obj = Require(objectId, null);
        obj.State = -obj.State;
        obj.VanishTime = frames;
        WorldChanged?.Invoke();
    }

    /// <summary>script.c 0x0040: pCurrent trigger-mode write.</summary>
    internal void SetTriggerMode(int objectId, int mode, TriggerPosition position)
    {
        Require(objectId, position).TriggerMode = mode;
        WorldChanged?.Invoke();
    }

    /// <summary>script.c 0x006D: per-field scene enter/teleport writes; both zero clears both.</summary>
    internal void SetSceneScripts(int sceneId, int onEnter, int onTeleport)
    {
        var current = sceneScripts.TryGetValue(sceneId, out var value) ? value : System.Tuple.Create(0, 0);
        sceneScripts[sceneId] = onEnter != 0 || onTeleport != 0
            ? System.Tuple.Create(onEnter != 0 ? onEnter : current.Item1, onTeleport != 0 ? onTeleport : current.Item2)
            : System.Tuple.Create(0, 0);
    }

    /// <summary>script.c 0x0090: gpGlobals->g.rgObject[id].rgwData[2+slot] = value; cross-scene
    /// object script slots are retained data, never executed by this slice.</summary>
    internal void SetObjectScript(int objectId, int value, int slotIndex) => objectScripts[(objectId, slotIndex)] = value;

    /// <summary>script.c 0x0046: the party/viewport is placed at the map tile; only trailing
    /// members take direction offsets, which the leader-only world does not model.</summary>
    internal void SetPartyPosition(int x, int y, int h)
    {
        Leader = Leader with { X = unchecked((short)(x * 32 + h * 16)), Y = unchecked((short)(y * 16 + h * 8)) };
        WorldChanged?.Invoke();
    }

    /// <summary>script.c 0x0015: party direction plus the member gesture word.</summary>
    internal void SetPartyFacing(int direction, int gesture, int memberIndex)
    {
        Leader = Leader with { Direction = direction };
        if (memberIndex == 0) Leader = Leader with { StepPhase = 0 };
        WorldChanged?.Invoke();
    }

    /// <summary>script.c 0x0075: replace the party with operand+1 roles (0 = empty); the empty
    /// party keeps role0 (the reference's Dream 2.11 behavior).</summary>
    internal void SetParty(int role0, int role1, int role2)
    {
        var roles = new[] { role0, role1, role2 }.Where(r => r != 0).Select(r => r - 1).ToArray();
        PartyRoles = roles.Length > 0 ? roles : [0];
    }

    /// <summary>play.c PAL_GameUpdate: every object with a nonzero vanish word counts it one step
    /// toward zero (negative counts up); objects skipped entirely this tick.</summary>
    internal void CountVanishTimes()
    {
        foreach (var obj in objects)
            if (obj.VanishTime != 0)
                obj.VanishTime += obj.VanishTime < 0 ? 1 : -1;
    }

    /// <summary>play.c PAL_GameUpdate: a negative state (hidden by 0x0052) restores when the
    /// object leaves the 320x320 viewport around the party.</summary>
    internal void RestoreNegativeStates()
    {
        int viewportX = Leader.X - 160, viewportY = Leader.Y - 112;
        foreach (var obj in objects)
            if (obj.State < 0 && (obj.X < viewportX || obj.X > viewportX + 320 || obj.Y < viewportY || obj.Y > viewportY + 320))
            {
                obj.State = Math.Abs(obj.State);
                obj.CurrentFrame = 0;
            }
    }
}

/// <summary>The no-combat dynamic scene: one selected owner trigger plus update-driven auto
/// scripts (play.c PAL_GameUpdate/PAL_RunAutoScript subset). Update is the single advancing
/// owner: one due tick per call at the 100ms discrete cadence; a late tick never catches up.</summary>
public sealed class DynamicEventSession
{
    private readonly TriggerEventCollection events;
    private readonly DialogueLanguagePackage language;
    private readonly TriggerEventEntry entry;
    private readonly DynamicEventWorld world;
    private readonly ExplorationSession exploration;
    private readonly ExplorationInput input;
    private readonly List<int> autoEntries;
    private bool searchPressed;
    private uint nextDeadline;
    private bool hasUpdated;
    private uint returnDeadline;
    private Guid boundCall;

    public DynamicEventPhase Phase { get; private set; } = DynamicEventPhase.Exploring;
    public TriggerScriptSession? Trigger { get; private set; }
    public DynamicEventTriggerRequest? PendingTrigger { get; private set; }
    public DynamicEventWorld World => world;
    public ExplorationState Current => exploration.Current;
    /// <summary>The success flag of the last completed call (script.c g_fScriptSuccess).</summary>
    public bool? LastScriptSuccess { get; private set; }
    public bool BlocksSceneUpdates => Phase != DynamicEventPhase.Exploring;

    public DynamicEventSession(StaticSceneRules rules, StaticLeader leader, TriggerEventCollection events,
        DialogueLanguagePackage language, TriggerEventEntry selectedEntry, int sceneId, int onEnter, int onTeleport)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(selectedEntry);
        if (selectedEntry is not { OwnerType: "eventObject", Slot: "wTriggerScript", Kind: "trigger", SourceAddress: > 0 and <= 65535 }
            || rules.Objects.SingleOrDefault(o => o.Id == selectedEntry.OwnerId)?.TriggerEntry != selectedEntry.SourceAddress)
            throw new InvalidDataException("Dynamic scene requires an eventObject wTriggerScript entry bound to its scene object.");
        // Unsupported dynamic targets are rejected before any side effect: touch-trigger objects
        // (kTriggerTouchNear and above) need dynamic trigger dispatch this slice does not own.
        if (rules.Objects.Any(o => o.TriggerMode >= 4))
            throw new InvalidDataException("Dynamic scene rejects touch-trigger objects (triggerMode >= 4) before any side effect.");
        this.events = events;
        this.language = language;
        entry = selectedEntry;
        world = new DynamicEventWorld(rules, leader, sceneId, onEnter, onTeleport);
        exploration = new(rules, leader);
        input = new(exploration);
        world.WorldChanged += ApplyWorld;
        autoEntries = rules.Objects.Where(o => o.AutoEntry != 0).Select(o => (int)o.AutoEntry).ToList();
        foreach (int autoEntry in autoEntries) ValidateAutoClosure(autoEntry);
    }

    /// <summary>Preflight the reachable auto closure with no production state and no randomness:
    /// unsupported dynamic actions reject before the first update tick.</summary>
    private void ValidateAutoClosure(int startAddress)
    {
        var pending = new Stack<int>();
        var visited = new HashSet<int>();
        pending.Push(startAddress);
        while (pending.TryPop(out int address))
        {
            if (address == 0 || !visited.Add(address)) continue;
            var command = events.CommandAt(address);
            switch (command.Op)
            {
                case "end" when command.Mode == "keep":
                    break;
                case "walkStep":
                case "face":
                case "setState":
                case "hide":
                    pending.Push(Next(address));
                    break;
                case "walkTo":
                case "waitUpdate":
                    pending.Push(Next(address));
                    break;
                case "end" when command.Mode is "advance" or "redirect":
                case "goto":
                    pending.Push(Next(address));
                    pending.Push(events.ResolveControlTarget(command));
                    break;
                default:
                    throw new InvalidDataException($"{command.Position}: unsupported auto-script command '{command.Op}'.");
            }
        }
    }

    public void Press(ExplorationKey key, bool repeat = false) => input.Press(key, repeat);
    public void Release(ExplorationKey key) => input.Release(key);
    // Each delivered search key-down (including OS repeat) is a transient press.
    public void PressSearch() => searchPressed = true;

    /// <summary>Starts the bound trigger call for the pending search request. The host starts it
    /// only after the searched scene has actually been drawn (PAL_Search redraw boundary).</summary>
    public TriggerScriptSession StartTrigger(Guid requestId)
    {
        if (PendingTrigger is not { } request || request.Id != requestId || Trigger is not null)
            throw new InvalidOperationException("The pending search request does not match this trigger start.");
        var trigger = new TriggerScriptSession(events, language, world);
        trigger.Start(entry.FileId, entry.Id, request.Caller, requestId);
        Trigger = trigger;
        boundCall = trigger.CallId;
        return trigger;
    }

    /// <summary>True when this due update tick changed the world or the leader (redraw signal).</summary>
    public bool Update(uint ticksMilliseconds)
    {
        // SDL_TICKS_PASSED contract shared with exploration; a late tick advances once, never twice.
        if (hasUpdated && unchecked((int)(nextDeadline - ticksMilliseconds)) > 0) return false;
        nextDeadline = unchecked(ticksMilliseconds + 100);
        hasUpdated = true;
        switch (Phase)
        {
            case DynamicEventPhase.Trigger:
                return UpdateTriggerTick(ticksMilliseconds);
            case DynamicEventPhase.Returning:
                if (unchecked((int)(returnDeadline - ticksMilliseconds)) > 0) return false;
                searchPressed = false; // PAL_ClearKeyState clears transient presses.
                Phase = DynamicEventPhase.Exploring;
                return UpdateExploringTick(ticksMilliseconds);
            default:
                return UpdateExploringTick(ticksMilliseconds);
        }
    }

    private bool UpdateTriggerTick(uint ticksMilliseconds)
    {
        var trigger = Trigger!;
        switch (trigger.Phase)
        {
            case TriggerScriptPhase.WaitingForSceneWait:
            {
                // script.c 0x0009: one real scene update per waited frame. The trigger checks
                // (operand1) gate the vanish countdowns; autos always run (play.c PAL_GameUpdate).
                var wait = trigger.PendingSceneWait!;
                if (wait.RunTriggers)
                {
                    world.CountVanishTimes();
                    world.RestoreNegativeStates();
                }
                RunAutoScripts();
                if (wait.UpdateGestures) CycleLeaderGesture();
                ApplyWorld();
                trigger.AcknowledgeSceneWait(wait.Id);
                return true;
            }
            case TriggerScriptPhase.WaitingForMovement:
            {
                // script.c 0x0010 inside a trigger call: the walk advances one step per due tick;
                // the rest of the world stays frozen (the trigger call owns the frame).
                var movement = trigger.PendingMovement!;
                bool arrived = world.WalkTowards(movement.ObjectId, movement.TargetX, movement.TargetY, 3);
                ApplyWorld();
                if (arrived) trigger.AcknowledgeMovement(movement.Id, true);
                return true;
            }
            case TriggerScriptPhase.Completed:
                CompleteTrigger(ticksMilliseconds);
                return true;
            default:
                return false; // display/transition/battle/lost-reload: the world stays frozen.
        }
    }

    private bool UpdateExploringTick(uint ticksMilliseconds)
    {
        world.CountVanishTimes();
        world.RestoreNegativeStates();
        RunAutoScripts();
        ApplyWorld();
        bool changed = input.Update(ticksMilliseconds);
        if (changed) world.Leader = Current.Leader; // the walk moved the party; the world tracks it
        if (Phase == DynamicEventPhase.Trigger) return true;
        bool search = searchPressed;
        searchPressed = false;
        if (search && exploration.FindSearchTarget() is { } hit)
        {
            if (hit.ObjectId != entry.OwnerId)
                throw new InvalidDataException($"Search hit owner {hit.ObjectId} at sample {hit.SampleIndex}; the dynamic scene binds owner {entry.OwnerId} @{entry.SourceAddress:X4}.");
            var searched = world.Find(hit.ObjectId)!;
            searched.CurrentFrame = 0; // PAL_Search: standing gesture, facing the party.
            searched.Direction = (Current.Leader.Direction + 2) % 4;
            ApplyWorld();
            PendingTrigger = new(Guid.NewGuid(), entry.FileId, entry.Id, hit.ObjectId, entry.SourceAddress);
            Phase = DynamicEventPhase.Trigger;
            return true;
        }
        return changed;
    }

    private void CompleteTrigger(uint ticksMilliseconds)
    {
        // The parked outer call's return entry (script.c wNextScriptEntry) and its success flag
        // are both saved; the searched object keeps the entry for the next interaction.
        var trigger = Trigger!;
        world.Require(trigger.Caller, default!).TriggerEntry = trigger.ReturnEntry;
        LastScriptSuccess = trigger.ScriptSuccess;
        Trigger = null;
        boundCall = Guid.Empty;
        PendingTrigger = null;
        Phase = DynamicEventPhase.Returning;
        returnDeadline = unchecked(ticksMilliseconds + 50);
    }

    private void RunAutoScripts()
    {
        foreach (var obj in world.Objects)
        {
            if (obj.State <= 0 || obj.VanishTime != 0 || obj.AutoEntry == 0) continue;
            obj.AutoEntry = RunAutoOneTick(obj);
        }
    }

    /// <summary>play.c PAL_RunAutoScript: one instruction per tick; jumps continue in the same
    /// tick; the returned address is written back to the object's auto entry.</summary>
    private int RunAutoOneTick(DynamicEventObject obj)
    {
        int address = obj.AutoEntry;
        while (address != 0)
        {
            var command = events.CommandAt(address);
            switch (command.Op)
            {
                case "end" when command.Mode == "keep":
                    return address;
                case "end" when command.Mode == "advance":
                    return Next(address);
                case "end" when command.Mode == "redirect":
                    if (AutoIdleGate(obj, command.Limit)) return events.ResolveControlTarget(command);
                    return Next(address);
                case "goto":
                    if (AutoIdleGate(obj, command.Limit)) { address = events.ResolveControlTarget(command); continue; }
                    return Next(address);
                case "waitUpdate":
                    if (++obj.AutoIdleCount >= (command.Frames == 0 ? 1 : command.Frames)) { obj.AutoIdleCount = 0; return Next(address); }
                    return address;
                case "walkStep":
                    world.WalkOneStep(obj.Id, command.Direction, 2);
                    return Next(address);
                case "face":
                    world.SetFacing(obj.Id, command.Direction, command.Frame);
                    return Next(address);
                case "walkTo":
                    return world.WalkTowards(obj.Id, command.TileX * 32 + command.Layer * 16, command.TileY * 16 + command.Layer * 8, 3)
                        ? Next(address) : address;
                case "setState":
                    if (command.ObjectId != 0)
                        world.SetObjectState(command.ObjectId == 0xffff ? obj.Id : command.ObjectId, command.State, command.Position);
                    return Next(address);
                case "hide":
                    world.HideObject(obj.Id, command.Frames == 0 ? 800 : command.Frames);
                    return Next(address);
                default:
                    throw new InvalidDataException($"{command.Position}: unsupported auto-script command '{command.Op}'.");
            }
        }
        return 0;
    }

    // The shared per-object idle counter: gate passes (and keeps counting) until the limit; the
    // first gate failure resets the counter and falls through (script.c 0x0002/0x0003).
    private static bool AutoIdleGate(DynamicEventObject obj, int limit) => limit == 0 || ++obj.AutoIdleCount < limit;
    private int Next(int address) => events.CommandAt(address).NextAddress ?? 0;

    private void CycleLeaderGesture() =>
        world.Leader = world.Leader with { StepPhase = (world.Leader.StepPhase + 1) % 4 };

    private StaticSceneRules BuildRules() => exploration.Rules.WithObjects(world.Objects.Select(ObjectView));

    private static SceneObject ObjectView(DynamicEventObject obj) => obj.Initial with
    {
        X = (ushort)obj.X, Y = (ushort)obj.Y, Direction = (ushort)obj.Direction, CurrentFrame = (ushort)obj.CurrentFrame,
        State = (short)obj.State, VanishTime = (short)obj.VanishTime,
        TriggerMode = (ushort)obj.TriggerMode, TriggerEntry = (ushort)obj.TriggerEntry, AutoEntry = (ushort)obj.AutoEntry,
    };

    private void ApplyWorld() => exploration.ReplaceRules(BuildRules(), world.Leader);
}
