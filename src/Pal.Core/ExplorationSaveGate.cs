namespace Pal.Core;

/// <summary>The save-request identity bound to one committed production update boundary
/// (#148 Decision 7): the stability check, the snapshot content and the request identity must
/// all belong to the same actual update boundary; a boundary that has since advanced makes the
/// request stale and it is explicitly rejected, never silently re-bound.</summary>
public sealed record ExplorationSaveRequest(Guid Id, long Boundary);

/// <summary>The live session activity the gate observes for one save attempt. Null means the
/// host currently has no such session; every non-null session is checked through its own public
/// read-only state only (never reflection, never private fields — #148 Testing Decision 2).</summary>
public sealed record ExplorationSaveActivity(
    ClassicBattleLifecycleSession? Battle,
    TriggerScriptSession? Trigger,
    DialogueDisplaySession? Dialogue,
    NpcInteractionSession? NpcInteraction,
    ExplorationMenuSession? Menu);

/// <summary>The locatable fail-closed rejection reason for one save attempt. <see cref="None"/>
/// only ever pairs with a captured snapshot; every other value names exactly the blocking state
/// so hosts can report a located reason instead of a generic I/O failure (#148 Story 48).</summary>
public enum ExplorationSaveRejection
{
    /// <summary>Not a rejection: the snapshot was captured.</summary>
    None,
    /// <summary>No production update has committed yet in this gate.</summary>
    UncommittedUpdate,
    /// <summary>The request's boundary is no longer the current committed production boundary.</summary>
    StaleRequest,
    /// <summary>A classic battle lifecycle is in flight (its ClassicBattleSession owns the frame).</summary>
    BattleActive,
    /// <summary>A trigger call is parked in a display, lost-reload (0x004E) or shop wait.</summary>
    TriggerActive,
    /// <summary>A counted movement (0x0010) or scene-updating wait (0x0009) is mid-execution.</summary>
    MovementWaitPending,
    /// <summary>A presentation transition (0x0005 redraw / 0x0073 fade) is pending.</summary>
    TransitionPending,
    /// <summary>A dialogue display is still typing or waiting for the player.</summary>
    DialogueActive,
    /// <summary>The NPC interaction owner is inside its trigger or return window.</summary>
    NpcInteractionActive,
    /// <summary>The exploration menu is not at the system-menu save layer.</summary>
    MenuLayerNotSavable,
}

/// <summary>The one-shot result: either the frozen v1 snapshot, or exactly one rejection reason
/// and never a snapshot. Rejection and capture both leave every owner and session untouched.</summary>
public sealed record ExplorationSaveOutcome(ExplorationSaveRejection Rejection, PlayerSaveV1? Save)
{
    public bool Captured => Rejection == ExplorationSaveRejection.None && Save is not null;
}

/// <summary>M6-C leaf #222 (parent #148): the stable-exploration save gate. Judges whether the
/// current moment is a rebuildable stable exploration boundary — no active battle, trigger,
/// dialogue, NPC interaction, movement wait or transition, the exploration menu at its
/// system-menu save layer, and the current production update committed — and captures the
/// complete immutable <see cref="PlayerSaveV1"/> view from the A/B live owners in that one
/// boundary. Capture and preflight never advance scripts, never run a world tick, never draw
/// gameplay randomness and never mutate an owner (#148 Decision 7). The snapshot is a read-only
/// view over the single live owners, never a second live-state engine (#148 Decision 5).</summary>
public sealed class ExplorationSaveGate
{
    private readonly RoleLiveState roles;
    private readonly InventoryLiveState inventory;
    private readonly CashLiveState cash;
    private readonly DynamicEventWorld world;
    private long boundary;

    /// <summary>Binds the shared live owners the v1 snapshot is captured over. The host owns
    /// their lifetime; this gate never re-seeds, replaces or mutates them.</summary>
    public ExplorationSaveGate(RoleLiveState roles, InventoryLiveState inventory, CashLiveState cash,
        DynamicEventWorld world)
    {
        this.roles = roles ?? throw new ArgumentNullException(nameof(roles));
        this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        this.cash = cash ?? throw new ArgumentNullException(nameof(cash));
        this.world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <summary>The last committed production update boundary; 0 before the first commit.</summary>
    public long CommittedBoundary => boundary;

    /// <summary>True once at least one production update has committed (ExplorationSession.Update
    /// has completed a commit); a save before that is never a rebuildable boundary.</summary>
    public bool HasCommittedProductionUpdate => boundary > 0;

    /// <summary>Host hook, exactly once per committed production update (the world/exploration
    /// update call returned). Advancing the boundary invalidates every earlier save request.</summary>
    public void NotifyProductionUpdateCommitted() => boundary++;

    /// <summary>Issues a fresh save-request identity bound to the current committed boundary.
    /// A request can never exist before the first committed production update.</summary>
    public ExplorationSaveRequest CreateRequest()
    {
        if (!HasCommittedProductionUpdate)
            throw new InvalidOperationException("A save request requires a committed production update boundary.");
        return new(Guid.NewGuid(), boundary);
    }

    /// <summary>One-shot eligibility judgment plus snapshot capture at the request's update
    /// boundary. Rejections are fail-closed, locatable and carry no state side effects; a
    /// captured snapshot validates every domain at PlayerSaveV1 construction (malformed owner
    /// state rejects with the contract's field-located diagnostics instead of clamping or
    /// partial capture). The checks read public session state only (#148 Decision 6: an open
    /// menu alone never means a stable boundary, and recorded-but-unexecuted auto entries never
    /// block one — only half-executed states do).</summary>
    public ExplorationSaveOutcome TryCapture(ExplorationSaveRequest request, ExplorationSaveActivity activity,
        int saveSlot, int savedTimes, string contentIdentity)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(activity);
        // Decision 7: the stability check, the snapshot content and the request identity must
        // belong to the same actual update boundary; anything else is explicitly rejected.
        if (!HasCommittedProductionUpdate)
            return new(ExplorationSaveRejection.UncommittedUpdate, null);
        if (request.Boundary != boundary)
            return new(ExplorationSaveRejection.StaleRequest, null);
        if (activity.Battle is not null)
            return new(ExplorationSaveRejection.BattleActive, null);
        if (activity.Trigger is { } trigger && trigger.Phase is not (TriggerScriptPhase.Ready or TriggerScriptPhase.Completed))
        {
            return trigger.Phase switch
            {
                // A parked startBattle is a battle in flight exactly like the host's lifecycle.
                TriggerScriptPhase.WaitingForBattle => new(ExplorationSaveRejection.BattleActive, null),
                // 0x0010 counted movements and 0x0009 scene-updating waits are half-executed
                // world state: the boundary must not straddle their advancing ticks.
                TriggerScriptPhase.WaitingForMovement or TriggerScriptPhase.WaitingForSceneWait
                    => new(ExplorationSaveRejection.MovementWaitPending, null),
                TriggerScriptPhase.WaitingForTransition => new(ExplorationSaveRejection.TransitionPending, null),
                // Display (dialogue), lost-reload (0x004E) and shop parks keep the call active.
                _ => new(ExplorationSaveRejection.TriggerActive, null),
            };
        }
        if (activity.Dialogue is { Current.Phase: not DialogueDisplayPhase.Completed })
            return new(ExplorationSaveRejection.DialogueActive, null);
        if (activity.NpcInteraction is { Phase: not NpcInteractionPhase.Exploring })
            return new(ExplorationSaveRejection.NpcInteractionActive, null);
        if (activity.Menu is not { Mode: ExplorationMenuMode.SystemMenu })
            return new(ExplorationSaveRejection.MenuLayerNotSavable, null);
        return new(ExplorationSaveRejection.None, Capture(saveSlot, savedTimes, contentIdentity, activity.Trigger));
    }

    /// <summary>Reads every snapshot domain from the bound owners in one pass (#221 matrix D2–D13):
    /// no script runs, no world tick, no randomness draw, no owner write. Dictionary domains are
    /// copied in stable order so the frozen view is deterministic; the resting trigger session
    /// contributes only its last setMusic words (D13 owner TriggerScriptSession.Music).</summary>
    private PlayerSaveV1 Capture(int saveSlot, int savedTimes, string contentIdentity, TriggerScriptSession? trigger)
    {
        var roleRows = roles.Roles.Keys.Order().Select(roleId =>
        {
            var role = roles.Roles[roleId];
            return new PlayerSaveRole(roleId, role.Level, role.Hp, role.MaxHp, role.Mp, role.MaxMp,
                Experience(role.PrimaryExperience),
                role.HiddenExperience.ToDictionary(pair => pair.Key, pair => Experience(pair.Value), StringComparer.Ordinal),
                role.Equipment, role.Magics, role.CoveredByRoleId,
                role.Attack, role.MagicPower, role.Defense, role.Dexterity, role.FleeRate);
        }).ToArray();
        var inventoryRows = inventory.Slots
            .Where(slot => slot.ItemId != 0) // occupied rows only; depleted rows stay occupied (D6)
            .Select(slot => new PlayerSaveInventorySlot(slot.ItemId, slot.Amount, slot.AmountInUse))
            .ToArray();
        var worldSnapshot = new PlayerSaveWorld(world.SceneId,
            new PlayerSaveLeader(world.Leader.X, world.Leader.Y, world.Leader.Direction, world.Leader.Layer, world.Leader.StepPhase),
            world.Objects.Select(obj => new PlayerSaveObject(obj.Id, obj.X, obj.Y, obj.Direction, obj.CurrentFrame,
                obj.State, obj.VanishTime, obj.TriggerMode, obj.TriggerEntry, obj.AutoEntry, obj.AutoIdleCount,
                obj.TriggerIdleCount)).ToArray(),
            world.SceneScripts.Keys.Order().Select(sceneId =>
                new PlayerSaveSceneScript(sceneId, world.SceneScripts[sceneId].Item1, world.SceneScripts[sceneId].Item2)).ToArray(),
            world.ObjectScripts.Keys.OrderBy(key => key.ObjectId).ThenBy(key => key.SlotIndex)
                .Select(key => new PlayerSaveObjectScript(key.ObjectId, key.SlotIndex, world.ObjectScripts[key])).ToArray(),
            world.Battlefield,
            trigger?.Music is { } music ? new PlayerSaveMusic(music.MusicId, music.Mode) : null);
        return new(PlayerSaveContract.CurrentVersion, saveSlot, savedTimes, contentIdentity,
            roleRows, world.PartyRoles, inventoryRows, cash.Cash, worldSnapshot);
    }

    private static PlayerSaveExperience Experience(BattleExperienceSnapshot track) =>
        new(track.Value, track.Level, track.Count);
}
