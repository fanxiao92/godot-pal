using Pal.Core.State;
using Pal.Core.Dialogue;
using Pal.Core.Battle;

namespace Pal.Core.Events;

public enum TriggerScriptPhase { Ready, WaitingForDisplay, Completed, WaitingForBattle, WaitingForLostReload,
    WaitingForSceneWait, WaitingForMovement, WaitingForTransition, WaitingForShop }

/// <summary>Which call frame a walked address belongs to: the outer event, a paused battle's role
/// hook (fight.c 768-880) or an enemy post-victory script (battle.c 1334-1337).</summary>
public enum TriggerScriptContext { Event, RoleHook, BattleEnd }

/// <summary>Runs the supported trigger slice to a real display, battle or stop boundary, never one opcode per tick.</summary>
public sealed class TriggerScriptSession
{
    private readonly TriggerEventCollection _events;
    private readonly DialogueLanguagePackage _language;
    private int _address;
    private int _lastCaller;
    private readonly TriggerBattleContent? _battleContent;
    private readonly IBattleRandom? _battleRandom;
    private readonly DynamicEventWorld? _world;
    internal DynamicEventWorld? BoundWorld => _world;
    private RoleLiveState? _roleLiveState;
    private IReadOnlyDictionary<int, BattleRoleSnapshot>? _battleRoles;
    private ClassicBattleRoleHook? _battleHook;
    private readonly record struct CapturedBattleHookOwner(int NameObjectId, int SlotIndex, int ScriptAddress);
    private CapturedBattleHookOwner? _battleHookOwner;
    private ClassicBattleEndScript? _battleEndScript;
    private int _parkedCaller;
    // script.c 0x0004 keeps the author continuation in the interpreter's own frame: the callee's
    // returned entry never replaces it, and only the current frame owns the public return entry.
    private readonly Stack<CallFrame> _callFrames = new();
    private readonly record struct CallFrame(int Continuation, int Caller);
    // fight.c 768-880 / battle.c 1334-1337 call PAL_RunTriggerScript recursively: the battle-owned
    // script starts with its own wScriptEntry/wNextScriptEntry depth, so a root end there never
    // pops the event frame that parked on startBattle. The parked event frames are restored when
    // the battle-owned invocation completes.
    private CallFrame[]? _parkedCallFrames;
    // fight.c 768-880 / battle.c 1334-1337 start a fresh PAL_RunTriggerScript invocation whose own
    // wNextScriptEntry is written back to the Name-owned hook entry (or discarded for battle end).
    // Keeping it apart from the public root ReturnEntry mirrors that per-invocation local word.
    private int _hookReturnEntry;
    private (int EnemyTeamId, bool IsBoss)? _lostBattleContext;

    // Metadata discovery for the host's selected interaction; does not start or preview a call.
    public IReadOnlyList<TriggerEventEntry> Entries => _events.Entries;
    public TriggerScriptPhase Phase { get; private set; }
    public bool BlocksSceneUpdates => Phase is TriggerScriptPhase.WaitingForDisplay or TriggerScriptPhase.WaitingForBattle
        or TriggerScriptPhase.WaitingForLostReload or TriggerScriptPhase.WaitingForSceneWait
        or TriggerScriptPhase.WaitingForMovement or TriggerScriptPhase.WaitingForTransition
        or TriggerScriptPhase.WaitingForShop;
    public Guid CallId { get; private set; }
    /// <summary>The interaction request which actually started this call; null for legacy callers.</summary>
    public Guid? SourceRequestId { get; private set; }
    public TriggerEventEntry? StartedEntry { get; private set; }
    public int Caller { get; private set; }
    public int ReturnEntry { get; private set; }
    public TriggerPosition? CurrentPosition { get; private set; }
    public TriggerDialogueRequest? PendingDisplay { get; private set; }
    public TriggerBattleRequest? PendingBattle { get; private set; }
    /// <summary>The 0x004E save-reload request that parked this call (script.c returns 0 there);
    /// non-null only in the WaitingForLostReload phase, and never acknowledged.</summary>
    public TriggerLostReloadRequest? PendingLostReload { get; private set; }
    /// <summary>The parked 0x0009 scene-updating wait (script.c advances the whole scene once per
    /// frame); the world's update owner is the only advancing authority.</summary>
    public TriggerSceneWaitRequest? PendingSceneWait { get; private set; }
    /// <summary>The parked 0x0010 counted movement (update-driven, never a teleport); the world's
    /// update owner advances one walk step per due update tick until arrival.</summary>
    public TriggerMovementRequest? PendingMovement { get; private set; }
    /// <summary>The parked presentation transition (0x0005 redraw delay or 0x0073 scene fade);
    /// the world stays frozen until the host acknowledges its own presentation.</summary>
    public TriggerTransitionRequest? PendingTransition { get; private set; }
    /// <summary>The parked 0x0026/0x0027 shop menu (script.c 1162-1174): the buy menu's authored
    /// store row, or the sell menu over the player's bag; the host runs the shop over the shared
    /// cash/inventory owners and receipts exactly this request when the menu closes.</summary>
    public TriggerShopRequest? PendingShopRequest { get; private set; }
    /// <summary>script.c g_fScriptSuccess: true at each start; markFailed (0x0041) clears it and
    /// the completion receipt carries the flag so callers observe the saved result.</summary>
    public bool ScriptSuccess { get; private set; }
    /// <summary>The last setMusic transition (script.c 0x0043 wNumMusic); null before the first one.</summary>
    public TriggerMusicState? Music { get; private set; }
    /// <summary>True after the fadeToRed transition (script.c 0x004F game-over fade).</summary>
    public bool FadedToRed { get; private set; }
    /// <summary>The battle's paused role hook (fight.c 768-880) awaiting StartBattleHook; the paused combat
    /// is the only source of this state, never a host-written input.</summary>
    public ClassicBattleRoleHook? PendingBattleHook => PendingBattle?.Lifecycle.Current.Combat.PendingRoleHook;
    /// <summary>The role hook whose script this executor is currently running; null outside a hook run.</summary>
    public ClassicBattleRoleHook? RunningBattleHook => _battleHook;
    /// <summary>The enemy post-victory script this executor is currently running; null outside a battle-end run.</summary>
    public ClassicBattleEndScript? RunningBattleEndScript => _battleEndScript;
    public IReadOnlyDictionary<int, BattleRoleSnapshot>? BattleRoles => _battleRoles;
    public BattleRoleSnapshot? BattleRole => BattleRoles is null ? null : BattleRoles.Count == 1 ? BattleRoles.Values.Single()
        : throw new InvalidOperationException("Use BattleRoles for a multiplayer trigger session.");
    public TriggerDialogueContext Context { get; private set; }

    /// <summary>The host may explicitly share a cash owner; null preserves the legacy cash capability rejection.</summary>
    public CashLiveState? CashLiveState { get; }
    /// <summary>The explicit shared inventory owner; null keeps inventory commands unsupported.</summary>
    public InventoryLiveState? InventoryLiveState { get; }
    /// <summary>The explicit current equipment owner, bound only together with shared live roles.</summary>
    public BattleEquipmentState? EquipmentState { get; private set; }
    /// <summary>The explicit fixed-party poison owner used by complete equipment removal.</summary>
    public PartyPoisonState? PartyPoisons { get; private set; }

    public TriggerScriptSession(TriggerEventCollection events, DialogueLanguagePackage language,
        int inheritedColor = DialogueTextTokenizer.DefaultColor, CashLiveState? cashLiveState = null,
        InventoryLiveState? inventoryLiveState = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(language);
        if (inheritedColor is < 0 or > 255) throw new ArgumentOutOfRangeException(nameof(inheritedColor));
        _events = events;
        _language = language;
        CashLiveState = cashLiveState;
        InventoryLiveState = inventoryLiveState;
        Context = new TriggerDialogueContext("upper", 0, inheritedColor, false, 24);
    }

    public TriggerScriptSession(TriggerEventCollection events, DialogueLanguagePackage language,
        TriggerBattleContent battleContent, IBattleRandom battleRandom, int inheritedColor = DialogueTextTokenizer.DefaultColor,
        CashLiveState? cashLiveState = null, InventoryLiveState? inventoryLiveState = null)
        : this(events, language, inheritedColor, cashLiveState, inventoryLiveState)
    {
        ArgumentNullException.ThrowIfNull(battleContent);
        ArgumentNullException.ThrowIfNull(battleRandom);
        _battleContent = battleContent;
        _battleRandom = battleRandom;
        _battleRoles = battleContent.InitialRoles;
    }

    /// <summary>#53 first-battle construction: the real @09C3 call both executes battles
    /// (startBattle over the verified content) and mutates the supplied world with the dynamic
    /// verbs; both halves use the same executor and the same random source.</summary>
    public TriggerScriptSession(TriggerEventCollection events, DialogueLanguagePackage language,
        TriggerBattleContent battleContent, IBattleRandom battleRandom, DynamicEventWorld world,
        int inheritedColor = DialogueTextTokenizer.DefaultColor, CashLiveState? cashLiveState = null,
        InventoryLiveState? inventoryLiveState = null)
        : this(events, language, battleContent, battleRandom, inheritedColor, cashLiveState, inventoryLiveState)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
        if (world.RoleLiveState is { } owner) BindRoleLiveState(owner);
    }

    /// <summary>Explicit shared-owner battle binding, with or without a world. A supplied world
    /// must already use this owner; construction never seeds or rebinds either side. Optional
    /// current equipment is read at preflight/each battle construction, never reseeded from author.
    /// Shared owners must not be mutated while the battle's local projection is active.</summary>
    public static TriggerScriptSession ForSharedRoles(TriggerEventCollection events, DialogueLanguagePackage language,
        TriggerBattleContent battleContent, IBattleRandom battleRandom, RoleLiveState roleLiveState,
        DynamicEventWorld? world = null, int inheritedColor = DialogueTextTokenizer.DefaultColor,
        CashLiveState? cashLiveState = null, InventoryLiveState? inventoryLiveState = null,
        BattleEquipmentState? equipmentState = null, PartyPoisonState? partyPoisons = null)
    {
        ArgumentNullException.ThrowIfNull(roleLiveState);
        if (world is not null && !ReferenceEquals(world.RoleLiveState, roleLiveState))
            throw new ArgumentException("The trigger and world must share the same live role owner.", nameof(world));
        var session = world is null
            ? new TriggerScriptSession(events, language, battleContent, battleRandom, inheritedColor, cashLiveState, inventoryLiveState)
            : new TriggerScriptSession(events, language, battleContent, battleRandom, world, inheritedColor, cashLiveState, inventoryLiveState);
        session.BindRoleLiveState(roleLiveState);
        session.EquipmentState = equipmentState;
        session.PartyPoisons = partyPoisons;
        return session;
    }

    private void BindRoleLiveState(RoleLiveState owner)
    {
        _roleLiveState = owner;
        _battleRoles = new LiveBattleRoles(owner, _battleContent!.Players.Select(player => player.RoleId).ToArray());
    }

    // A held projection reads replacement records from the owner; it never caches HP/MP snapshots.
    private sealed class LiveBattleRoles(RoleLiveState owner, int[] roleIds) : IReadOnlyDictionary<int, BattleRoleSnapshot>
    {
        public BattleRoleSnapshot this[int roleId] => ContainsKey(roleId) && owner.Roles.TryGetValue(roleId, out var role)
            ? role : throw new InvalidDataException($"Live roles are missing battle participant {roleId}.");
        public IEnumerable<int> Keys => Array.AsReadOnly(roleIds);
        public IEnumerable<BattleRoleSnapshot> Values => roleIds.Select(roleId => this[roleId]);
        public int Count => roleIds.Length;
        public bool ContainsKey(int roleId) => roleIds.Contains(roleId);
        public bool TryGetValue(int roleId, out BattleRoleSnapshot value)
        {
            value = null!;
            return ContainsKey(roleId) && owner.Roles.TryGetValue(roleId, out value!);
        }
        public IEnumerator<KeyValuePair<int, BattleRoleSnapshot>> GetEnumerator() =>
            roleIds.Select(roleId => new KeyValuePair<int, BattleRoleSnapshot>(roleId, this[roleId])).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Dynamic-scene construction (no battle support): world mutations apply directly to
    /// the supplied world; startBattle stays an unsupported command in this session.</summary>
    public TriggerScriptSession(TriggerEventCollection events, DialogueLanguagePackage language,
        DynamicEventWorld world, int inheritedColor = DialogueTextTokenizer.DefaultColor, CashLiveState? cashLiveState = null,
        InventoryLiveState? inventoryLiveState = null)
        : this(events, language, inheritedColor, cashLiveState, inventoryLiveState)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
    }

    /// <summary>Pure selected-entry capability check against the content's initial role. Does not start a call,
    /// create a battle or require randomness; later Start calls still validate their current settled role.
    /// An optional bound world/caller (the formal story preflight) resolves counted gate owners with the
    /// same event-object rule the runtime uses; without them the counter-free contract is unchanged.</summary>
    public static TriggerEntryRequirements ValidateEntry(TriggerEventCollection events, DialogueLanguagePackage language,
        TriggerBattleContent battleContent, string fileId, string entryId, int inheritedColor = DialogueTextTokenizer.DefaultColor,
        CashLiveState? cashLiveState = null, InventoryLiveState? inventoryLiveState = null,
        DynamicEventWorld? world = null, int? caller = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(battleContent);
        if (inheritedColor is < 0 or > 255) throw new ArgumentOutOfRangeException(nameof(inheritedColor));
        var entry = events.GetEntry(fileId, entryId);
        if (entry.Kind != "trigger") throw new InvalidDataException($"{fileId}.json entry {entryId} @{entry.SourceAddress:X4}: auto context is unsupported.");
        var requirements = new EntryRequirementsCollector();
        ValidateCapabilityClosure(events, language, battleContent, battleContent.InitialRoles, inheritedColor, entry.SourceAddress, requirements,
            hasCashOwner: cashLiveState is not null, hasInventoryOwner: inventoryLiveState is not null,
            world: world, caller: caller);
        return new(requirements.Displays, requirements.Portraits, requirements.Teams);
    }

    /// <summary>Pure requirements for the current binding and world, without starting or seeding a call.
    /// A later Start still rechecks its current state; caller FFFF never changes the saved caller.</summary>
    public TriggerEntryRequirements GetCurrentEntryRequirements(string fileId, string entryId, int caller)
    {
        var requirements = new EntryRequirementsCollector();
        ValidateSelectedStart(fileId, entryId, caller, requirements);
        return new(requirements.Displays, requirements.Portraits, requirements.Teams);
    }

    public void Start(string fileId, string entryId, int caller) => StartCall(fileId, entryId, caller, null);

    /// <summary>#227 host hook (matrix D13): seed a freshly constructed session's music owner
    /// from one restored world candidate — the last setMusic words the loaded save carried —
    /// so a subsequent save captures them instead of losing them through the round-trip. Only
    /// a resting session accepts the seed; a running call's music stays its own executed
    /// transition (script.c 0x0043), never a host-written word.</summary>
    public void RestoreMusic(TriggerMusicState music)
    {
        ArgumentNullException.ThrowIfNull(music);
        if (Phase != TriggerScriptPhase.Ready)
            throw new InvalidOperationException("Only a resting trigger session accepts a restored music owner.");
        Music = music;
    }

    public void Start(string fileId, string entryId, int caller, Guid sourceRequestId)
    {
        if (sourceRequestId == Guid.Empty) throw new ArgumentException("A source request ID must be nonempty.", nameof(sourceRequestId));
        StartCall(fileId, entryId, caller, sourceRequestId);
    }

    private TriggerEventEntry ValidateSelectedStart(string fileId, string entryId, int caller,
        EntryRequirementsCollector? requirements = null)
    {
        if (BlocksSceneUpdates) throw new InvalidOperationException("A trigger call is already active.");
        if (caller is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(caller));
        var entry = _events.GetEntry(fileId, entryId);
        if (entry.Kind != "trigger") throw new InvalidDataException($"{fileId}.json entry {entryId} @{entry.SourceAddress:X4}: auto context is unsupported.");
        ValidateCapabilityClosure(entry.SourceAddress, TriggerScriptContext.Event, requirements, caller == 65535 ? _lastCaller : caller);
        return entry;
    }

    private void StartCall(string fileId, string entryId, int caller, Guid? sourceRequestId)
    {
        var requirements = new EntryRequirementsCollector();
        var entry = ValidateSelectedStart(fileId, entryId, caller, requirements);
        // Only a fully validated, structurally reached battle may initialize its Name-owned slots.
        // Query/construction/no-battle calls never enter this write path; a stored zero stays zero.
        if (_world is not null && _battleContent is not null && requirements.Teams.Count != 0)
        {
            foreach (var player in _battleContent.Players)
            {
                if (player.NameObjectId is not int nameObjectId) continue;
                if (!_world.ObjectScripts.TryGetValue((nameObjectId, 0), out _))
                    _world.SetObjectScript(nameObjectId, player.FriendDeathScript, 0);
                if (!_world.ObjectScripts.TryGetValue((nameObjectId, 1), out _))
                    _world.SetObjectScript(nameObjectId, player.DyingScript, 1);
            }
        }
        CallId = Guid.NewGuid();
        StartedEntry = entry;
        SourceRequestId = sourceRequestId;
        ScriptSuccess = true;
        Caller = caller == 65535 ? _lastCaller : caller;
        _lastCaller = Caller;
        ReturnEntry = _address = entry.SourceAddress;
        Context = Context with { DelayMilliseconds = 24 };
        Continue();
    }

    /// <summary>Accepts only the current, actually completed display; stale or premature receipts are inert.</summary>
    public bool AcknowledgeDisplay(Guid requestId)
    {
        if (Phase != TriggerScriptPhase.WaitingForDisplay || PendingDisplay is not { } request || request.Id != requestId || request.Display.Current.Phase != DialogueDisplayPhase.Completed)
            return false;
        Context = Context with { Color = request.Display.TextColor, DelayMilliseconds = request.Display.DelayMilliseconds };
        PendingDisplay = null;
        Continue();
        return true;
    }

    /// <summary>Only this request's settled lifecycle completion can release the event; presentation timing belongs to the host.</summary>
    public bool AcknowledgeBattle(Guid requestId, ClassicBattleLifecycleSession.CompletionReceipt? completion)
    {
        if (Phase != TriggerScriptPhase.WaitingForBattle || PendingBattle is not { } request || request.Id != requestId
            || !request.Lifecycle.OwnsCompletion(completion)) return false;
        if (_roleLiveState is not null)
            _roleLiveState.MergeSettledRoles(request.Lifecycle.Current.Roles.Values.ToArray());
        else _battleRoles = request.Lifecycle.Current.Roles;
        // The reload diagnostic carries the enemy team/context of the battle that actually lost.
        if (completion!.Outcome == ClassicBattlePhase.Lost) _lostBattleContext = (request.EnemyTeamId, request.IsBoss);
        // script.c 0x0007: kBattleResultLost jumps operand[1] when present, kBattleResultFleed
        // jumps operand[2] (the authored flee target), and every other result continues at next.
        _address = completion!.Outcome switch
        {
            ClassicBattlePhase.Lost => request.LossAddress ?? request.NextAddress,
            ClassicBattlePhase.Escaped => request.FleeAddress ?? request.NextAddress,
            _ => request.NextAddress,
        };
        PendingBattle = null;
        Continue();
        return true;
    }

    /// <summary>Only the world's update owner may receipt a scene wait tick; premature, stale or
    /// duplicate ticks are inert. The world tick itself runs before this receipt.</summary>
    public bool AcknowledgeSceneWait(Guid requestId)
    {
        if (Phase != TriggerScriptPhase.WaitingForSceneWait || PendingSceneWait is not { } request || request.Id != requestId)
            return false;
        if (--request.FramesRemaining > 0) return false;
        PendingSceneWait = null;
        Continue();
        return true;
    }

    /// <summary>Receipts one actually-advanced movement step; only arrival completes the request,
    /// premature or duplicate receipts are inert.</summary>
    public bool AcknowledgeMovement(Guid requestId, bool arrived)
    {
        if (Phase != TriggerScriptPhase.WaitingForMovement || PendingMovement is not { } request || request.Id != requestId)
            return false;
        // Premature receipts are rejected against the world itself: only an object actually at
        // the target words completes the movement, never a claimed arrival.
        if (!arrived || _world?.Find(request.ObjectId) is not { } obj || obj.X != request.TargetX || obj.Y != request.TargetY)
            return false;
        _address = request.NextAddress;
        PendingMovement = null;
        Continue();
        return true;
    }

    /// <summary>Receipts the host's completed redraw/fade presentation exactly once.</summary>
    public bool AcknowledgeTransition(Guid requestId)
    {
        if (Phase != TriggerScriptPhase.WaitingForTransition || PendingTransition is not { } request || request.Id != requestId)
            return false;
        PendingTransition = null;
        Continue();
        return true;
    }

    /// <summary>Receipts the host's closed shop menu exactly once; only the current parked
    /// request releases the call (uigame.c PAL_BuyMenu/PAL_SellMenu return when the player
    /// cancels), and stale or premature receipts stay inert.</summary>
    public bool AcknowledgeShop(Guid requestId)
    {
        if (Phase != TriggerScriptPhase.WaitingForShop || PendingShopRequest is not { } request || request.Id != requestId)
            return false;
        PendingShopRequest = null;
        Continue();
        return true;
    }

    /// <summary>Runs the paused battle's role-hook script with the formal caller semantics (the dying
    /// member's role, or the coverer's role for a friend death). The outer call stays parked on its
    /// startBattle command; preflight covers every random branch before any side effect.</summary>
    public void StartBattleHook()
    {
        if (Phase != TriggerScriptPhase.WaitingForBattle || PendingBattle is null)
            throw new InvalidOperationException("No battle is waiting for this trigger call.");
        if (_battleHook is not null) throw new InvalidOperationException("A battle role hook is already running.");
        var hook = PendingBattleHook ?? throw new InvalidOperationException("The waiting battle has no pending role hook.");
        ValidateCapabilityClosure(hook.ScriptAddress, TriggerScriptContext.RoleHook, null, hook.RoleId);
        _battleHookOwner = _world is not null && _battleContent!.Players[hook.PartyIndex].NameObjectId is int nameObjectId
            ? new CapturedBattleHookOwner(nameObjectId, hook.Kind == ClassicBattleHookKind.FriendDeath ? 0 : 1, hook.ScriptAddress)
            : null;
        _battleHook = hook;
        _parkedCaller = Caller;
        EnterBattleOwnedInvocation();
        // script.c PAL_RunTriggerScript entry: wNextScriptEntry starts at this entry, the dialog
        // delay returns to its default and g_fScriptSuccess is reset for this invocation.
        _hookReturnEntry = hook.ScriptAddress;
        ScriptSuccess = true;
        Context = Context with { DelayMilliseconds = 24 };
        _lastCaller = Caller = hook.RoleId;
        _address = hook.ScriptAddress;
        Continue();
    }

    /// <summary>Runs the paused battle's enemy post-victory script (battle.c 1334-1337) with the enemy
    /// slot as its caller. The outer call stays parked on its startBattle command; preflight covers
    /// every random branch before any side effect.</summary>
    public void StartBattleEndScript()
    {
        if (Phase != TriggerScriptPhase.WaitingForBattle || PendingBattle is null)
            throw new InvalidOperationException("No battle is waiting for this trigger call.");
        if (_battleHook is not null || _battleEndScript is not null)
            throw new InvalidOperationException("A battle script is already running.");
        var script = PendingBattle.Lifecycle.PendingBattleEndScript
            ?? throw new InvalidOperationException("The waiting battle has no pending enemy battle-end script.");
        ValidateCapabilityClosure(script.ScriptAddress, TriggerScriptContext.BattleEnd, null, script.EnemySlot);
        _battleEndScript = script;
        _parkedCaller = Caller;
        EnterBattleOwnedInvocation();
        // The enemy post-victory script is its own PAL_RunTriggerScript invocation: same entry
        // globals as any trigger entry, and its returned entry is not consumed by battle.c.
        ScriptSuccess = true;
        Context = Context with { DelayMilliseconds = 24 };
        _lastCaller = Caller = script.EnemySlot;
        _address = script.ScriptAddress;
        Continue();
    }

    private void ValidateCapabilityClosure(int address, TriggerScriptContext context, EntryRequirementsCollector? requirements = null,
        int? caller = null) =>
        ValidateCapabilityClosure(_events, _language, _battleContent, BattleRoles, Context.Color, address, requirements, context,
            rejectMappedBattlesWithoutWorld: _world is null, validateNameMappings: true,
            currentObjectScripts: _world?.ObjectScripts, rejectObjectScriptWithoutWorld: _world is null,
            hasCashOwner: CashLiveState is not null, hasInventoryOwner: InventoryLiveState is not null,
            equipmentState: EquipmentState,
            hasRemovalOwners: InventoryLiveState is not null && EquipmentState is not null && _roleLiveState is not null && PartyPoisons is not null,
            removalPartyMatchesWorld: _world is null || PartyPoisons is not null && PartyPoisons.PartyRoleIds.SequenceEqual(_world.PartyRoles),
            world: _world, caller: caller);

    private sealed class EntryRequirementsCollector
    {
        internal readonly List<TriggerDisplayRequirement> Displays = new();
        internal readonly HashSet<int> Portraits = new();
        internal readonly HashSet<int> Teams = new();
    }

    // Static-closure walk node. The frame trail mirrors the interpreter's 0x0004 stack so a nested
    // entry resumes its caller's own continuation, while EndSlot marks which enemy end script's
    // invocation (battle.c 1334-1337) a work item belongs to.
    private sealed class WalkFrame(int continuation, int? caller, WalkFrame? next)
    {
        public int Continuation { get; } = continuation;
        public int? Caller { get; } = caller;
        public WalkFrame? Next { get; } = next;
    }
    private readonly record struct WalkState(int Address, TriggerScriptContext Context, int? Caller, int? Last,
        WalkFrame? Frames, int? EndSlot);

    private static void ValidateCapabilityClosure(TriggerEventCollection events, DialogueLanguagePackage language,
        TriggerBattleContent? battleContent, IReadOnlyDictionary<int, BattleRoleSnapshot>? battleRoles, int color, int address,
        EntryRequirementsCollector? requirements = null, TriggerScriptContext context = TriggerScriptContext.Event,
        bool rejectMappedBattlesWithoutWorld = false, bool validateNameMappings = false,
        IReadOnlyDictionary<(int ObjectId, int SlotIndex), int>? currentObjectScripts = null,
        bool rejectObjectScriptWithoutWorld = false, bool hasCashOwner = false, bool hasInventoryOwner = false,
        BattleEquipmentState? equipmentState = null, bool hasRemovalOwners = false, bool removalPartyMatchesWorld = true,
        DynamicEventWorld? world = null, int? caller = null)
    {
        // The walk models both words a recursive invocation carries: wEventObjectID (the current
        // caller, which owns idle counters) and wLastEventObject (script.c 3163-3182). A 0x0004
        // operand 0xFFFF resolves the callee from the last event object, and a return restores only
        // the caller, so a caller continuation keeps the callee word as its last event object.
        var pending = new Stack<WalkState>();
        // Each (address, context, caller, last) is validated once; the frame trail only steers how a
        // nested frame's end resumes, so a recursive call graph still terminates while the callee is
        // expanded before its own caller continuation is revisited.
        var visited = new HashSet<(int Address, TriggerScriptContext Context, int? Caller, int? Last)>();
        // script.c 3163-3182/3263-3267: the event continuation resumes after the battle settles and
        // wLastEventObject keeps whatever battle-owned invocation ran last -- fight.c 768-880 enters
        // its role hook and battle.c 1334-1337 its enemy end script with the hook role / enemy slot
        // as both words, and a return never restores the last word. Those entries and their nested
        // callee words are collected while the worklist drains and then re-walked as post-battle last
        // words, so a 0xFFFF call in the continuation is preflighted against every owner the battle
        // can leave behind instead of only the pre-battle word (which would let a battle start and
        // then fail, or reject a script whose real owner is legal).
        var postBattleContinuations = new Queue<(int Address, int? Caller, WalkFrame? Frames, int? EndSlot)>();
        var postBattleSeen = new HashSet<(int Address, int? Caller, int Last)>();
        // Role hooks run with their member's word (fight.c 768-880) and any of their nested 0x0004
        // callee words can be the last one written before the hook returns. battle.c 1334-1337 runs
        // the enemy end scripts in slot order, so only the last authored one can survive; a loss or
        // flee continuation cannot see it at all.
        var hookOwnedLasts = new HashSet<int>();
        // battle.c 1334-1337 runs its enemy end scripts in slot order: only the final authored slot's
        // invocation survives in wLastEventObject, including any nested 0x0004 callee word it ran.
        var battleEndOwnedLasts = new Dictionary<int, HashSet<int>>();
        // Per-walk candidates, not a live bank: a write can precede discovery of its battle/Name.
        var hookWrites = new Dictionary<(int ObjectId, int SlotIndex), List<TriggerCommand>>();
        var relevantHookKeys = new HashSet<(int ObjectId, int SlotIndex)>();
        var hookRoots = new HashSet<(int ObjectId, int SlotIndex, int Entry)>();
        // fight.c 768-880 runs each hook with its member's RoleId; a counted gate inside a hook
        // resolves the counter owner from that caller word, not from the event caller.
        var hookCallers = new Dictionary<(int ObjectId, int SlotIndex), int>();
        TriggerPosition? removalPosition = null, partyChangePosition = null;
        pending.Push(new WalkState(address, context, caller, caller, null, null));
        while (pending.TryPop(out var item) || ExpandPostBattle(out item))
        {
            address = item.Address;
            if (address == 0)
            {
                // script.c's loop exits on word zero, which returns a nested frame to its caller.
                if (item.Frames is { } zeroFrame)
                    pending.Push(item with { Address = zeroFrame.Continuation, Caller = zeroFrame.Caller, Frames = zeroFrame.Next });
                continue;
            }
            if (!visited.Add((address, item.Context, item.Caller, item.Last))) continue;
            // Collect every word a role hook can leave in wLastEventObject: its own entry caller and
            // each nested 0x0004 callee caller, both of which appear as a RoleHook work item's caller.
            if (item.Context == TriggerScriptContext.RoleHook && item.Caller is int hookWord)
                hookOwnedLasts.Add(hookWord);
            // The same trailing-word rule applies inside the enemy end script that actually runs last.
            if (item.EndSlot is int endSlot && item.Caller is int endWord)
            {
                if (!battleEndOwnedLasts.TryGetValue(endSlot, out var endWords))
                    battleEndOwnedLasts.Add(endSlot, endWords = new HashSet<int>());
                endWords.Add(endWord);
            }
            var command = events.CommandAt(address);
            if (command.IsContentOnly || (command.Op is "changeCash" or "halveCash" && !hasCashOwner)
                || (command.Op is "addItem" or "itemShortageJump" && !hasInventoryOwner))
                throw new InvalidDataException($"{command.Position}: content-only command '{command.Op}' is unsupported in trigger context {item.Context}."
                    + (command.Op is "changeCash" or "halveCash" ? " An explicit cash owner is required." : "")
                    + (command.Op is "addItem" or "itemShortageJump" ? " An explicit inventory owner is required." : ""));
            if (item.Context == TriggerScriptContext.RoleHook && item.Caller is int hookCaller
                && (battleRoles is null || !battleRoles.ContainsKey(hookCaller)))
                // fight.c 768-880 always invokes a role hook with a real party role word; a call
                // operand that names someone else must fail before any prefix display or effect.
                throw new InvalidDataException($"{command.Position}: role hook caller {hookCaller} is not a battle participant.");
            if (command.Op == "removeItem")
            {
                if (!hasRemovalOwners || item.Context != TriggerScriptContext.Event)
                    throw new InvalidDataException($"{command.Position}: content-only command 'removeItem' is unsupported in trigger context {item.Context}. Explicit inventory, equipment, live-role and poison owners outside active battle scripts are required.");
                if (!removalPartyMatchesWorld)
                    throw new InvalidDataException($"{command.Position}: removeItem poison party must match the bound world's current party order.");
                removalPosition ??= command.Position;
            }
            if (command.Op == "setParty") partyChangePosition ??= command.Position;
            if (command.Op is "showBuyMenu" or "showSellMenu")
            {
                // #217 script.c 0x0026/0x0027: the shop menus park the script while the host runs
                // PAL_BuyMenu/PAL_SellMenu; the authored shop context and both shared owners must
                // exist up front, and a buy menu's store row must be declared, so a broken shop
                // reference rejects before any cash or inventory write.
                if (battleContent is null || battleContent.ShopObjects.Count == 0)
                    throw new InvalidDataException($"{command.Position}: {command.Op} requires the authored shop objects.");
                if (command.Op == "showBuyMenu" && battleContent.Stores.Count == 0)
                    throw new InvalidDataException($"{command.Position}: showBuyMenu requires the authored store rows and shop objects.");
                if (!hasCashOwner || !hasInventoryOwner)
                    throw new InvalidDataException($"{command.Position}: {command.Op} requires explicit cash and inventory owners.");
                if (command.Op == "showBuyMenu" && !battleContent.Stores.ContainsKey(command.StoreId))
                    throw new InvalidDataException($"{command.Position}: showBuyMenu store {command.StoreId} is not authored.");
                pending.Push(item with { Address = Next(command) });
                continue;
            }
            if (command.Op is "goto" or "call" or "advanceEntry" || command.Op == "end")
            {
                // script.c 0x0000-0x0004/0x0008: end keeps/advances/redirects the frame, goto jumps
                // and call runs a nested frame. A counted gate needs the caller's event-object counter,
                // checked here before any side effect; limit zero never touches a counter.
                if (command.Op is "goto" or "end") ValidateCountedGate(command, item.Caller);
                int target = command.Op is "goto" or "call" || command.Op == "end" && command.Mode == "redirect"
                    ? events.ResolveControlTarget(command) : 0;
                switch (command.Op)
                {
                    case "end" when command.Mode is "keep" or "advance":
                        // The frame ends; advance only publishes its own return entry, and a return
                        // restores the caller word but never wLastEventObject.
                        if (item.Frames is { } popped)
                            pending.Push(item with { Address = popped.Continuation, Caller = popped.Caller, Frames = popped.Next });
                        break;
                    case "end":
                        // script.c 0x0002: a successful gate ends the frame, a failed one falls through.
                        if (command.Limit != 0) pending.Push(item with { Address = Next(command) });
                        if (item.Frames is { } redirected)
                            pending.Push(item with { Address = redirected.Continuation, Caller = redirected.Caller, Frames = redirected.Next });
                        break;
                    case "goto":
                        // script.c 3196/3241-3255: word zero ends the invocation, so a callee's
                        // goto word zero returns to its caller continuation instead of jumping;
                        // an idle-gate failure still resumes the authored successor.
                        if (target != 0) pending.Push(item with { Address = target });
                        else if (item.Frames is { } ended)
                            pending.Push(item with { Address = ended.Continuation, Caller = ended.Caller, Frames = ended.Next });
                        if (command.Limit != 0) pending.Push(item with { Address = Next(command) });
                        break;
                    case "call":
                        // Operand1 is a literal caller; word zero inherits the current one and word
                        // 0xFFFF resolves to the last event object inside the nested entry (never a
                        // literal participant word). The callee keeps the frame context and the
                        // caller continues at its successor, which keeps the callee word as the last
                        // event object because a return never restores wLastEventObject.
                        int? callee = command.EventObjectId switch
                        {
                            0 => item.Caller,
                            0xffff => item.Last,
                            _ => command.EventObjectId,
                        };
                        // The successor is walked with the callee word (the value it holds when the
                        // callee returns without another call), while the frame below also resumes
                        // there with whatever word the callee actually leaves, so a battle-owned last
                        // word or a deeper callee word reaches the caller's own 0xFFFF call.
                        pending.Push(item with { Address = Next(command), Last = callee });
                        if (target != 0)
                            pending.Push(item with
                            {
                                Address = target, Caller = callee, Last = callee,
                                Frames = new WalkFrame(Next(command), item.Caller, item.Frames),
                            });
                        break;
                    default: // advanceEntry 0x0008
                        if (command.Source.Opcode != 0x0008)
                            throw new InvalidDataException($"{command.Position}: advanceEntry 0x{command.Source.Opcode:X4} belongs to the auto switch, not the trigger switch.");
                        pending.Push(item with { Address = Next(command) });
                        break;
                }
                continue;
            }
            if (command.Op == "startBattle" && battleContent is not null && item.Context == TriggerScriptContext.Event)
            {
                if (rejectMappedBattlesWithoutWorld && battleContent.Players.Any(player => player.NameObjectId.HasValue))
                    throw new InvalidDataException($"{command.Position}: a Name-mapped battle requires a bound world.");
                if (validateNameMappings && battleContent.Players.Any(player => player.NameObjectId.HasValue))
                {
                    var players = battleContent.Players;
                    if (players.Any(player => !player.NameObjectId.HasValue))
                        throw new InvalidDataException($"{command.Position}: nameObjectId must be declared together for all players.");
                    foreach (var player in players)
                        if (player.NameObjectId is < 0 or > 65535)
                            throw new InvalidDataException($"{command.Position}: nameObjectId must be a WORD (0..65535).");
                    // TriggerBattleContent admits one to three players; equal Names own every
                    // shared slot, so all same-name members must declare identical hook seeds.
                    foreach (var group in players.Where(p => p.NameObjectId.HasValue)
                        .GroupBy(p => p.NameObjectId!.Value))
                    {
                        if (group.Any(p => p.DyingScript != group.First().DyingScript))
                            throw new InvalidDataException($"{command.Position}: players with the same nameObjectId must declare identical dyingScript seeds.");
                        if (group.Any(p => p.FriendDeathScript != group.First().FriendDeathScript))
                            throw new InvalidDataException($"{command.Position}: players with the same nameObjectId must declare identical friendDeathScript seeds.");
                    }
                }
                try { battleContent.Validate(command.EnemyTeamId, battleRoles!, equipmentState); }
                catch (InvalidDataException exception) { throw new InvalidDataException($"{command.Position}: {exception.Message}", exception); }
                requirements?.Teams.Add(command.EnemyTeamId);
                pending.Push(item with { Address = Next(command) });
                if (command.LossAddress is { } loss) pending.Push(item with { Address = loss });
                if (command.FleeAddress is { } flee) pending.Push(item with { Address = flee });
                // fight.c 768-880: preflight the party's current Name-owned hook entries. A stored
                // entry (including disabled WORD 0) wins; only a missing key uses the declared seed.
                foreach (var player in battleContent.SettledPlayers(battleRoles!, useCurrentEquipment: equipmentState is not null))
                {
                    int friendDeath = player.FriendDeathScript, dying = player.DyingScript;
                    if (currentObjectScripts is not null && player.NameObjectId is int nameObjectId)
                    {
                        if (currentObjectScripts.TryGetValue((nameObjectId, 0), out var friendEntry)) friendDeath = friendEntry;
                        if (currentObjectScripts.TryGetValue((nameObjectId, 1), out var dyingEntry)) dying = dyingEntry;
                    }
                    if (player.NameObjectId is int hookName)
                    {
                        hookCallers[(hookName, 0)] = player.RoleId;
                        hookCallers[(hookName, 1)] = player.RoleId;
                        PushHookCandidate((hookName, 0), command.Position, friendDeath, player.RoleId);
                        PushHookCandidate((hookName, 1), command.Position, dying, player.RoleId);
                        ActivateHookKey((hookName, 0));
                        ActivateHookKey((hookName, 1));
                    }
                    else
                    {
                        if (friendDeath != 0) PushHook(events, pending, command.Position, friendDeath, player.RoleId);
                        if (dying != 0) PushHook(events, pending, command.Position, dying, player.RoleId);
                    }
                }
                // battle.c 1334-1337: every enemy slot's post-victory script runs after the growth
                // settlement; the whole closure (both random branches included) is preflighted here.
                int? finalEndSlot = null;
                foreach (var (enemy, slot) in battleContent.Enemies(command.EnemyTeamId).Select((enemy, slot) => (enemy, slot)))
                    if (enemy.BattleEndScript != 0)
                    {
                        finalEndSlot = slot;
                        PushScript(events, pending, command.Position, enemy.BattleEndScript, TriggerScriptContext.BattleEnd, slot);
                    }
                // The whole remaining caller chain is re-walked for each owner a battle-owned
                // invocation can leave in wLastEventObject, so a 0xFFFF call reached after the
                // enclosing 0x0004 frame returns is preflighted against the runtime owner too. The
                // deferred walk runs once the hooks and end scripts above have been observed; only
                // the normal settlement can have run an end script, while a loss or a flee can still
                // have run a role hook.
                postBattleContinuations.Enqueue((Next(command), item.Caller, item.Frames, finalEndSlot));
                if (command.LossAddress is { } lossTarget) postBattleContinuations.Enqueue((lossTarget, item.Caller, item.Frames, null));
                if (command.FleeAddress is { } fleeTarget) postBattleContinuations.Enqueue((fleeTarget, item.Caller, item.Frames, null));
            }
            else if (command.Op == "randomJump" && battleContent is not null)
            {
                // script.c 3301: both the rate-miss successor and the jump target are real branches.
                pending.Push(item with { Address = Next(command) });
                pending.Push(item with { Address = command.TargetAddress!.Value });
            }
            else if (command.Op is "changeCash" or "itemShortageJump")
            {
                // Check both branches without reading/mutating cash or inventory, even when
                // the current balance/count would make one branch unreachable.
                pending.Push(item with { Address = Next(command) });
                pending.Push(item with { Address = command.TargetAddress!.Value });
            }
            else if (command.Op == "removeItem")
            {
                // A null shortage target permits partial removal; a reference adds a second arm.
                // Never prune either arm from current stock or simulate equipment writes here.
                pending.Push(item with { Address = Next(command) });
                if (command.TargetAddress is int shortage) pending.Push(item with { Address = shortage });
            }
            else if (command.Op is "setMusic" or "fadeToRed" or "halveCash" or "addItem")
            {
                pending.Push(item with { Address = Next(command) });
            }
            else if (command.Op == "setObjectScript")
            {
                if (rejectObjectScriptWithoutWorld)
                    throw new InvalidDataException($"{command.Position}: setObjectScript requires a bound world.");
                if (command.SlotIndex is 0 or 1)
                {
                    var key = (command.ObjectId, command.SlotIndex);
                    if (!hookWrites.TryGetValue(key, out var writes)) hookWrites.Add(key, writes = new());
                    writes.Add(command);
                    if (relevantHookKeys.Contains(key))
                        PushHookCandidate(key, command.Position, command.Value, hookCallers[key]);
                }
                pending.Push(item with { Address = Next(command) });
            }
            else if (command.Op is "walkStep" or "face" or "setState" or "hide" or "setTriggerMode"
                or "setSceneScripts" or "setPartyPos" or "setPartyFacing"
                or "waitUpdate" or "redraw" or "fadeScene" or "markFailed" or "walkTo"
                or "setObjectPosition" or "setObjectGesture" or "changeHpMp" or "revivePlayer"
                or "setAutoEntry" or "setTriggerEntry" or "setBattlefield" or "fadeOut" or "setParty"
                or "noop" or "setStateRange")
            {
                // #52 dynamic-story verbs: they are never branch points; execution applies them
                // through the dynamic world (or parks the call). No-world sessions reject them at
                // execution, keeping the structural walk shared.
                pending.Push(item with { Address = Next(command) });
            }
            else if (command.Op == "requestReload")
            {
                // script.c 0x004E stops the whole script (return 0); nothing after it runs,
                // so the walk does not continue past the reload boundary.
            }
            else if (command.Op is "changeHp" or "changeMp" or "boostStat")
            {
                if (item.Context != TriggerScriptContext.RoleHook)
                    throw new InvalidDataException($"{command.Position}: {command.Op} is only supported inside a battle role hook.");
                if (command.Op != "boostStat" && command.Scope != 0)
                    throw new InvalidDataException($"{command.Position}: party-wide {command.Op} is unsupported; the role hook applies to its caller.");
                if (command.Op == "boostStat" && command.RoleOperand != 0)
                    throw new InvalidDataException($"{command.Position}: boostStat must target the role hook's caller.");
                if (command.Op == "boostStat" && command.StatField is not (17 or 18 or 19 or 20 or 21))
                    throw new InvalidDataException($"{command.Position}: temporary stat boost field {command.StatField} is not a supported panel word.");
                pending.Push(item with { Address = Next(command) });
            }
            else if (command.Op == "showDialog")
            {
                var content = ReadDisplay(events, language, ref address);
                try
                {
                    // Keep the existing language/control rules, on a separate display without production state or RNG.
                    var preview = new DialogueDisplaySession(content.Block, color, 0);
                    while (preview.Current.Phase != DialogueDisplayPhase.Completed) preview.Update(preview.Current.Phase == DialogueDisplayPhase.WaitingForAdvance ? 100u : int.MaxValue, true, true);
                }
                catch (InvalidDataException exception) { throw new InvalidDataException($"{command.Position}: {exception.Message}", exception); }
                requirements?.Displays.Add(new(content.Block, content.References));
                pending.Push(item with { Address = address });
            }
            else if (command.Op == "setDialog" && command.Box is "upper" or "lower" or "centerWindow" or "center" && command.RngWord == 0)
            {
                if (command.PortraitId != 0) requirements?.Portraits.Add(command.PortraitId);
                pending.Push(item with { Address = Next(command) });
            }
            else throw new InvalidDataException($"{command.Position}: unsupported trigger command/context '{command.Op}'.");
        }

        // This binding does not coordinate party poison reset with full equipment-script replay.
        // Even a no-op setParty crosses that seam; reject the selected closure before writes.
        if (removalPosition is not null && partyChangePosition is not null)
            throw new InvalidDataException($"{removalPosition}: removeItem with setParty at {partyChangePosition} requires unsupported poison party migration.");

        void ValidateCountedGate(TriggerCommand command, int? caller)
        {
            if (command.Limit == 0) return;
            // Without a bound caller there is no way to prove the counter owner; counted control
            // flow then stays outside the counter-free contract and Start rechecks the bound
            // owner before any effect. A supplied static world/caller (the story preflight)
            // resolves the same event object the runtime gate will use (script.c 3196/3229).
            if (caller is null)
                throw new InvalidDataException($"{command.Position}: counted {command.Op} requires a bound caller and scene event object.");
            if (world is null)
                throw new InvalidDataException($"{command.Position}: counted {command.Op} requires a bound scene with event object {caller}.");
            if (caller == 0 || world.Find(caller.Value) is null)
                throw new InvalidDataException($"{command.Position}: counted {command.Op} requires event object {caller} in the bound scene.");
        }

        void PushHookCandidate((int ObjectId, int SlotIndex) key, TriggerPosition position, int entry, int caller)
        {
            if (entry != 0 && hookRoots.Add((key.ObjectId, key.SlotIndex, entry)))
                PushHook(events, pending, position, entry, caller);
        }

        void ActivateHookKey((int ObjectId, int SlotIndex) key)
        {
            // Each of at most four related keys consumes its prior writes once; later writes
            // join the same worklist directly, including those discovered inside new hooks.
            if (!relevantHookKeys.Add(key) || !hookWrites.TryGetValue(key, out var writes)) return;
            foreach (var write in writes) PushHookCandidate(key, write.Position, write.Value, hookCallers[key]);
        }

        bool ExpandPostBattle(out WalkState expanded)
        {
            // Only called once the worklist is empty, so every battle-owned entry and its nested
            // callee caller words have been observed; each queued event continuation is then walked
            // once per possible post-battle last word, resuming through the 0x0004 frames that parked
            // on the battle so their own continuations see that same word. A battle with no hook or
            // end script leaves the pre-battle word, which the eager walk already covers.
            while (postBattleContinuations.TryDequeue(out var continuation))
            {
                foreach (int owner in hookOwnedLasts)
                    PushContinuation(continuation.Address, continuation.Caller, continuation.Frames, owner);
                if (continuation.EndSlot is int endSlot && battleEndOwnedLasts.TryGetValue(endSlot, out var endWords))
                    foreach (int owner in endWords)
                        PushContinuation(continuation.Address, continuation.Caller, continuation.Frames, owner);
                if (pending.TryPop(out expanded)) return true;
            }
            expanded = default;
            return false;

            void PushContinuation(int next, int? caller, WalkFrame? frames, int owner)
            {
                if (postBattleSeen.Add((next, caller, owner)))
                    pending.Push(new WalkState(next, TriggerScriptContext.Event, caller, owner, frames, null));
            }
        }
    }

    private static void PushHook(TriggerEventCollection events, Stack<WalkState> pending,
        TriggerPosition position, int scriptAddress, int caller)
    {
        if (scriptAddress >= events.InstructionCount)
            throw new InvalidDataException($"{position}: role hook address {scriptAddress:X4} is outside the event collection.");
        // fight.c 768-880 enters its own invocation with the hook role as both words.
        pending.Push(new WalkState(scriptAddress, TriggerScriptContext.RoleHook, caller, caller, null, null));
    }

    private static void PushScript(TriggerEventCollection events, Stack<WalkState> pending,
        TriggerPosition position, int scriptAddress, TriggerScriptContext context, int? caller)
    {
        if (scriptAddress >= events.InstructionCount)
            throw new InvalidDataException($"{position}: battle-end script address {scriptAddress:X4} is outside the event collection.");
        // battle.c 1334-1337 enters its own invocation with the enemy slot as both words.
        pending.Push(new WalkState(scriptAddress, context, caller, caller, null,
            context == TriggerScriptContext.BattleEnd ? caller : null));
    }

    private (DialogueMessageBlock Block, IReadOnlyList<TriggerMessageReference> References) ReadDisplay(ref int address) =>
        ReadDisplay(_events, _language, ref address);

    private static (DialogueMessageBlock Block, IReadOnlyList<TriggerMessageReference> References) ReadDisplay(
        TriggerEventCollection events, DialogueLanguagePackage language, ref int address)
    {
        var references = new List<TriggerMessageReference>();
        var parts = new List<DialogueMessagePart>();
        do
        {
            var command = events.CommandAt(address);
            int start = command.MessageId, last = start;
            var position = command.Position;
            address = Next(command);
            while (address != 0 && events.CommandAt(address) is { Op: "showDialog" } next && next.MessageId == last + 1)
            {
                last = next.MessageId;
                address = Next(next);
            }
            try { parts.AddRange(language.Resolve(start, last - start).Parts); }
            catch (InvalidDataException exception) { throw new InvalidDataException($"{position}: {exception.Message}", exception); }
            references.Add(new TriggerMessageReference(position, start, last - start));
        } while (address != 0 && events.CommandAt(address).Op == "showDialog");
        // Range lookup and display line count are independent. Separate ranges do not
        // themselves clear/wait; PAL_ClearDialog/EndDialog provide the final boundary.
        var first = references[0];
        return (new DialogueMessageBlock(first.StartMessageId, first.StartMessageId + first.Span, parts), references.AsReadOnly());
    }

    private int ReadRoleHookScript(ClassicBattlePlayer player, ClassicBattleHookKind kind)
    {
        int slotIndex = kind == ClassicBattleHookKind.FriendDeath ? 0 : 1;
        return player.NameObjectId is int nameObjectId && _world is not null
            && _world.ObjectScripts.TryGetValue((nameObjectId, slotIndex), out int entry)
                ? entry : kind == ClassicBattleHookKind.FriendDeath ? player.FriendDeathScript : player.DyingScript;
    }

    private void Continue()
    {
        while (true)
        {
            if (_address == 0)
            {
                // script.c's loop exits on word zero: a nested call frame returns to its caller and
                // only the outermost frame completes the public call.
                if (EndFrame()) continue;
                CurrentPosition = _events.CommandAt(0).Position;
                Complete();
                return;
            }
            var command = _events.CommandAt(_address);
            CurrentPosition = command.Position;
            switch (command.Op)
            {
                case "end" when command.Mode == "keep":
                    if (EndFrame()) continue;
                    Complete();
                    return;
                case "end" when command.Mode == "advance":
                    // script.c 0x0001: the frame returns the instruction after this one.
                    PublishReturn(Next(command));
                    if (EndFrame()) continue;
                    Complete();
                    return;
                case "end" when command.Mode == "redirect":
                    // script.c 0x0002: the counted failure resets the counter and falls through.
                    if (TakeIdleGate(command))
                    {
                        PublishReturn(_events.ResolveControlTarget(command));
                        if (EndFrame()) continue;
                        Complete();
                        return;
                    }
                    _address = Next(command);
                    break;
                case "goto":
                    // script.c 0x0003: an unconditional jump with the same shared idle gate.
                    _address = TakeIdleGate(command) ? _events.ResolveControlTarget(command) : Next(command);
                    break;
                case "call":
                    // script.c 0x0004: each entry resets g_fScriptSuccess, restores the default
                    // dialog delay and advances wLastEventObject; the caller continuation and
                    // caller word stay in this interpreter frame. Operand word zero inherits the
                    // current caller, while 0xFFFF resolves to the global last event object in the
                    // nested entry (wLastEventObject is not restored when the callee returns).
                    int calleeCaller = command.EventObjectId switch
                    {
                        0 => Caller,
                        0xffff => _lastCaller,
                        _ => command.EventObjectId,
                    };
                    _callFrames.Push(new CallFrame(Next(command), Caller));
                    Caller = calleeCaller;
                    _lastCaller = Caller;
                    ScriptSuccess = true;
                    Context = Context with { DelayMilliseconds = 24 };
                    _address = _events.ResolveControlTarget(command);
                    break;
                case "advanceEntry":
                    // script.c 0x0008: replace the returned entry with the next instruction and continue.
                    if (command.Source.Opcode != 0x0008)
                        throw new InvalidDataException($"{command.Position}: advanceEntry 0x{command.Source.Opcode:X4} belongs to the auto switch, not the trigger switch.");
                    _address = Next(command);
                    PublishReturn(_address);
                    break;
                case "setDialog" when command.Box is "upper" or "lower" && command.RngWord == 0:
                    Context = Context with { Box = command.Box, PortraitId = command.PortraitId, Color = command.Color == 0 ? Context.Color : command.Color };
                    _address = Next(command);
                    break;
                case "setDialog" when command.Box is "centerWindow" or "center":
                    // text.c: the centered boxes carry no portrait; the plain center box is the
                    // retail game-over presentation (e.g. the Lost branch after fadeToRed).
                    Context = Context with { Box = command.Box, PortraitId = 0, Color = command.Color == 0 ? Context.Color : command.Color };
                    _address = Next(command);
                    break;
                case "randomJump" when _battleRandom is not null:
                    // script.c 3301-3312: the production draw is taken when at or above the rate; the
                    // other successor stays untouched, consuming exactly one draw per jump command.
                    _address = _battleRandom.NextInclusive(1, 100) >= command.Rate ? command.TargetAddress!.Value : Next(command);
                    break;
                case "changeHp" when _battleHook is not null:
                case "changeMp" when _battleHook is not null:
                    if (command.Scope != 0)
                        throw new InvalidDataException($"{command.Position}: party-wide {command.Op} is unsupported; the role hook applies to its caller.");
                    // script.c 0x001B/0x001C: wEventObjectID names the player role, never the
                    // hook that started the invocation, so a nested callee routes by its caller.
                    int routedParty = HookPartyIndex(command);
                    bool roleHookChanged = command.Op == "changeHp"
                        ? PendingBattle!.Lifecycle.ApplyRoleHookHp(routedParty, command.Amount)
                        : PendingBattle!.Lifecycle.ApplyRoleHookMp(routedParty, command.Amount);
                    if (!roleHookChanged) ScriptSuccess = false;
                    _address = Next(command);
                    break;
                case "boostStat" when _battleHook is not null:
                    if (command.RoleOperand != 0)
                        throw new InvalidDataException($"{command.Position}: boostStat must target the role hook's caller.");
                    PendingBattle!.Lifecycle.ApplyRoleHookStatBoost(HookPartyIndex(command), command.StatField, command.Amount);
                    _address = Next(command);
                    break;
                case "startBattle" when _battleContent is not null:
                    var battleRoles = BattleRoles!;
                    var lifecycle = new ClassicBattleLifecycleSession(battleRoles.Values.ToArray(),
                        _battleContent.SettledPlayers(battleRoles, useCurrentEquipment: EquipmentState is not null),
                        _battleContent.Enemies(command.EnemyTeamId), _battleContent.GrowthByRole, _battleRandom!,
                        EquipmentState ?? BattleEquipmentState.Initialize(_battleContent), runEnemyBattleEndScripts: true, isBossBattle: command.IsBoss,
                        readRoleHookScript: _world is null ? null : ReadRoleHookScript, cashLiveState: CashLiveState);
                    PendingBattle = new TriggerBattleRequest(Guid.NewGuid(), Caller, command.Position, command.EnemyTeamId, command.IsBoss,
                        Next(command), command.LossAddress, command.FleeAddress, lifecycle);
                    Phase = TriggerScriptPhase.WaitingForBattle;
                    return;
                case "addItem" when InventoryLiveState is not null:
                    // script.c 0x001F ignores even a shortage/full return; no equipment fallback.
                    InventoryLiveState.AddItem(command.ObjectId, command.Amount);
                    _address = Next(command);
                    break;
                case "removeItem" when InventoryLiveState is not null && EquipmentState is not null && _roleLiveState is not null && PartyPoisons is not null:
                    // script.c 0x0020 does not write ScriptSuccess, even after allowed partial removal.
                    _address = EquipmentState.TryRemoveItem(command.ObjectId, command.Amount, command.TargetAddress is null,
                        InventoryLiveState, _roleLiveState, PartyPoisons) ? Next(command) : command.TargetAddress!.Value;
                    break;
                case "itemShortageJump" when InventoryLiveState is not null:
                    // script.c 0x0058 compares inventory-only quantity to the literal signed count.
                    _address = InventoryLiveState.GetAmount(command.ObjectId) < command.Amount
                        ? command.TargetAddress!.Value : Next(command);
                    break;
                case "changeCash" when CashLiveState is not null:
                    // script.c 0x001E: shortage jumps without a debit or a ScriptSuccess write.
                    _address = CashLiveState.TryChange(command.Amount) ? Next(command) : command.TargetAddress!.Value;
                    break;
                case "halveCash" when CashLiveState is not null:
                    CashLiveState.Halve();
                    _address = Next(command);
                    break;
                case "setMusic":
                    Music = new(command.MusicId, command.MusicMode);
                    _address = Next(command);
                    break;
                case "fadeToRed":
                    FadedToRed = true;
                    _address = Next(command);
                    break;
                case "requestReload":
                    // #228: the completed failure story parks here (script.c 0x004E: the
                    // PAL_FadeOut(1)/PAL_ReloadInNextTick pair); the host registers the request
                    // and reloads the current recovery slot through the #227 chain at the next
                    // production boundary. The parked call itself is never acknowledged — a
                    // completed reload retires it together with its whole old session.
                    PendingLostReload = new(command.Position, _lostBattleContext?.EnemyTeamId, _lostBattleContext?.IsBoss ?? false);
                    Phase = TriggerScriptPhase.WaitingForLostReload;
                    return;
                case "showDialog":
                    var content = ReadDisplay(ref _address);
                    var display = new DialogueDisplaySession(content.Block, Context.Color, 0, Context.DelayMilliseconds);
                    if (display.Current.Phase == DialogueDisplayPhase.Completed && !display.Current.ClearDisplay)
                    {
                        Context = Context with { Color = display.TextColor, DelayMilliseconds = display.DelayMilliseconds };
                        break;
                    }
                    PendingDisplay = new TriggerDialogueRequest(Guid.NewGuid(), Context, content.References, content.Block, display);
                    CurrentPosition = _events.CommandAt(_address).Position;
                    Phase = TriggerScriptPhase.WaitingForDisplay;
                    return;
                case "walkStep" when _world is not null:
                    // script.c 0x000B-0x000E: caller direction + one speed-2 step (scene.c PAL_NPCWalkOneStep).
                    _world.WalkOneStep(Caller, command.Direction, 2);
                    _address = Next(command);
                    break;
                case "face" when _world is not null:
                    // script.c 0x000F: caller direction/gesture; 0xFFFF keeps the current word.
                    _world.SetFacing(Caller, command.Direction, command.Frame);
                    _address = Next(command);
                    break;
                case "setState" when _world is not null:
                    // script.c 0x0049: operand 0/0xFFFF selects the caller, otherwise that object ID.
                    if (command.EventObjectId != 0) _world.SetObjectState(command.EventObjectId == 0xffff ? Caller : command.EventObjectId, command.State, command.Position);
                    _address = Next(command);
                    break;
                case "hide" when _world is not null:
                    // script.c 0x0052: caller's state *= -1 and vanish time = frames (0 -> 800).
                    _world.HideObject(Caller, command.Frames == 0 ? 800 : command.Frames);
                    _address = Next(command);
                    break;
                case "setTriggerMode" when _world is not null:
                    // script.c 0x0040: nonzero operand0 selects the target object.
                    if (command.EventObjectId != 0) _world.SetTriggerMode(command.EventObjectId == 0xffff ? Caller : command.EventObjectId, command.TriggerModeWord, command.Position);
                    _address = Next(command);
                    break;
                case "setSceneScripts" when _world is not null:
                    _world.SetSceneScripts(command.SceneId, command.OnEnter, command.OnTeleport);
                    _address = Next(command);
                    break;
                case "setObjectScript" when _world is not null:
                    _world.SetObjectScript(command.ObjectId, command.Value, command.SlotIndex);
                    _address = Next(command);
                    break;
                case "setPartyPos" when _world is not null:
                    _world.SetPartyPosition(command.TileX, command.TileY, command.Layer);
                    _address = Next(command);
                    break;
                case "setPartyFacing" when _world is not null:
                    _world.SetPartyFacing(command.Direction, command.Frame, command.MemberIndex);
                    _address = Next(command);
                    break;
                case "setObjectPosition" when _world is not null:
                    // script.c 0x0013: operand 0/0xFFFF selects the caller, otherwise that object.
                    _world.SetObjectPosition(command.EventObjectId is 0 or 0xffff ? Caller : command.EventObjectId,
                        command.TileX, command.TileY, command.Position);
                    _address = Next(command);
                    break;
                case "setObjectGesture" when _world is not null:
                    // script.c 0x0014: the caller's frame word, direction forced south.
                    _world.SetGesture(Caller, command.Frame);
                    _address = Next(command);
                    break;
                case "setTriggerEntry" when _world is not null:
                    // script.c 0x0025: skip when operand0 selects nothing.
                    if (command.EventObjectId != 0)
                        _world.SetObjectEntry(command.EventObjectId == 0xffff ? Caller : command.EventObjectId, command.Value, true, command.Position);
                    _address = Next(command);
                    break;
                case "setAutoEntry" when _world is not null:
                    // script.c 0x0024: same selection rule as the trigger entry write.
                    if (command.EventObjectId != 0)
                        _world.SetObjectEntry(command.EventObjectId == 0xffff ? Caller : command.EventObjectId, command.Value, false, command.Position);
                    _address = Next(command);
                    break;
                case "setBattlefield" when _world is not null:
                    // script.c 0x004A: wNumBattleField word.
                    _world.SetBattlefield(command.Frames);
                    _address = Next(command);
                    break;
                case "fadeOut" when _world is not null:
                    // script.c 0x0050: fade out at the delay step unit (0 -> 1); the host fades back in after.
                    PendingTransition = new TriggerTransitionRequest(Guid.NewGuid(), command.Position, "fadeOut",
                        command.FadeOutDelay == 0 ? 1 : command.FadeOutDelay);
                    _address = Next(command);
                    Phase = TriggerScriptPhase.WaitingForTransition;
                    return;
                case "setParty" when _world is not null:
                    // script.c 0x0075: operand+1 role words; the world keeps role0 when all empty.
                    _world.SetParty(command.Role0, command.Role1, command.Role2);
                    _address = Next(command);
                    break;
                case "setStateRange" when _world is not null:
                    // script.c 0x009A: the same signed state for a contiguous object-ID range.
                    _world.SetStateRange(command.EventObjectId, command.ToEventObjectId, command.State, command.Position);
                    _address = Next(command);
                    break;
                case "changeHpMp" when _world is not null:
                    // script.c 0x001D: only single-target failure clears the flag; the party
                    // branch ignores PAL_IncreaseHPMP's result, preserving any earlier failure.
                    bool changed = _world.ChangeHpMp(command.Scope, Caller, command.Amount, command.Position);
                    if (command.Scope == 0 && !changed) ScriptSuccess = false;
                    _address = Next(command);
                    break;
                case "revivePlayer" when _world is not null:
                    // script.c 0x0022: dead roles return with maxHP * tenths / 10.
                    _world.RevivePlayer(command.Scope, Caller, command.Amount, command.Position);
                    _address = Next(command);
                    break;
                case "noop":
                    // script.c 0x0078: the reference executes nothing for this opcode.
                    _address = Next(command);
                    break;
                case "markFailed":
                    // script.c 0x0041: g_fScriptSuccess = FALSE; carried by the completion receipt.
                    ScriptSuccess = false;
                    _address = Next(command);
                    break;
                case "waitUpdate" when _world is not null:
                    // script.c 0x0009: the real scene update advances once per due tick while the
                    // call parks; the world's update owner is the single advancing authority.
                    PendingSceneWait = new TriggerSceneWaitRequest(Guid.NewGuid(), command.Position,
                        command.Frames == 0 ? 1 : command.Frames, command.RunTriggers != 0, command.UpdateGestures != 0);
                    _address = Next(command); // resume after the wait when its frames complete
                    Phase = TriggerScriptPhase.WaitingForSceneWait;
                    return;
                case "walkTo" when _world is not null:
                    // script.c 0x0010: counted movement to the tile; update-driven, no teleport.
                    PendingMovement = new TriggerMovementRequest(Guid.NewGuid(), command.Position, Caller,
                        command.TileX * 32 + command.Layer * 16, command.TileY * 16 + command.Layer * 8, Next(command));
                    Phase = TriggerScriptPhase.WaitingForMovement;
                    return;
                case "redraw" when _world is not null:
                    // script.c 0x0005: redraw + delay 60ms units (0 -> 1); the world stays frozen.
                    PendingTransition = new TriggerTransitionRequest(Guid.NewGuid(), command.Position, "redraw",
                        (command.Frames == 0 ? 1 : command.Frames) * 60);
                    _address = Next(command);
                    Phase = TriggerScriptPhase.WaitingForTransition;
                    return;
                case "fadeScene" when _world is not null:
                    // script.c 0x0073: backup screen, rebuild the scene, cross-fade at the speed word.
                    PendingTransition = new TriggerTransitionRequest(Guid.NewGuid(), command.Position, "fadeScene", command.FadeSpeed);
                    _address = Next(command);
                    Phase = TriggerScriptPhase.WaitingForTransition;
                    return;
                case "showBuyMenu" when CashLiveState is not null && InventoryLiveState is not null && _battleContent?.ShopObjects.Count > 0:
                    // script.c 0x0026: the script blocks inside PAL_BuyMenu until the player
                    // cancels; the host owns the menu over the shared cash/inventory owners.
                    if (!_battleContent.Stores.TryGetValue(command.StoreId, out _))
                        throw new InvalidDataException($"{command.Position}: showBuyMenu store {command.StoreId} is not authored.");
                    PendingShopRequest = new TriggerShopRequest(Guid.NewGuid(), command.Position, true, command.StoreId);
                    _address = Next(command);
                    Phase = TriggerScriptPhase.WaitingForShop;
                    return;
                case "showSellMenu" when CashLiveState is not null && InventoryLiveState is not null && _battleContent?.ShopObjects.Count > 0:
                    // script.c 0x0027: PAL_SellMenu reads the player's bag (no store row).
                    PendingShopRequest = new TriggerShopRequest(Guid.NewGuid(), command.Position, false, 0);
                    _address = Next(command);
                    Phase = TriggerScriptPhase.WaitingForShop;
                    return;
                default: throw new InvalidDataException($"{command.Position}: unsupported trigger command/context '{command.Op}'.");
            }
        }
    }

    /// <summary>Battle-owned scripts recurse into PAL_RunTriggerScript with their own script-entry
    /// depth, so the caller's 0x0004 frames are set aside for the duration of the hook/end run.</summary>
    private void EnterBattleOwnedInvocation()
    {
        _parkedCallFrames = _callFrames.ToArray();
        _callFrames.Clear();
    }

    /// <summary>Restores the event's call frames after the battle-owned invocation finishes; a
    /// nested callee inside that invocation already popped its own frames.</summary>
    private void ExitBattleOwnedInvocation()
    {
        if (_parkedCallFrames is null) return;
        for (int i = _parkedCallFrames.Length - 1; i >= 0; i--) _callFrames.Push(_parkedCallFrames[i]);
        _parkedCallFrames = null;
    }

    /// <summary>Pops one script.c 0x0004 call frame; the callee's return entry is discarded and the
    /// caller keeps its own word (wLastEventObject is not restored). False for the outermost frame.</summary>
    private bool EndFrame()
    {
        if (_callFrames.Count == 0) return false;
        var frame = _callFrames.Pop();
        Caller = frame.Caller;
        _address = frame.Continuation;
        return true;
    }

    /// <summary>script.c's wNextScriptEntry is local to each PAL_RunTriggerScript invocation: a
    /// nested call's 0x0001/0x0002/0x0008 never overwrites its caller's word, while the active
    /// root (event call, role hook or battle-end script) keeps its own. The public ReturnEntry is
    /// the selected host entry's word and stays untouched by battle-owned invocations.</summary>
    private void PublishReturn(int value)
    {
        if (_callFrames.Count > 0) return;
        if (_battleHook is not null) { _hookReturnEntry = value; return; }
        if (_battleEndScript is not null) return; // battle.c ignores the returned entry
        ReturnEntry = value;
    }

    /// <summary>script.c 0x001B/0x001C/0x0030 apply the single-role word to wEventObjectID, which
    /// the reference documents as the current player role; a callee's literal caller can therefore
    /// target another participant, and a non-participant caller is rejected before any effect.</summary>
    private int HookPartyIndex(TriggerCommand command)
    {
        foreach (var player in PendingBattle!.Lifecycle.Current.Combat.Players)
            if (player.RoleId == Caller) return player.PartyIndex;
        throw new InvalidDataException($"{command.Position}: role hook caller {Caller} is not a battle participant.");
    }

    /// <summary>script.c 0x0002/0x0003 idle gate: zero is unconditional, otherwise the caller's
    /// event object counts up to the limit and the first failure resets it and falls through.</summary>
    private bool TakeIdleGate(TriggerCommand command)
    {
        if (command.Limit == 0) return true;
        var owner = (_world ?? throw new InvalidDataException($"{command.Position}: counted {command.Op} requires a bound scene with event object {Caller}."))
            .Find(Caller) ?? throw new InvalidDataException($"{command.Position}: counted {command.Op} requires event object {Caller} in the bound scene.");
        if (++owner.TriggerIdleCount < command.Limit) return true;
        owner.TriggerIdleCount = 0;
        return false;
    }

    private static int Next(TriggerCommand command) => command.NextAddress
        ?? throw new InvalidDataException($"{command.Position}: execution requires a next command.");

    private void Complete()
    {
        // A finished role hook resumes its paused battle; a finished enemy post-victory script
        // advances the battle's settlement. The outer call stays parked on its startBattle
        // command and keeps the script's final dialog context (the reference dialog state is global).
        if (_battleHook is not null)
        {
            if (_battleHookOwner is { } owner)
                _world!.SetObjectScript(owner.NameObjectId, _hookReturnEntry, owner.SlotIndex);
            _battleHookOwner = null;
            _battleHook = null;
            _hookReturnEntry = 0;
            ExitBattleOwnedInvocation();
            PendingBattle!.Lifecycle.AcknowledgeRoleHook();
            Caller = _parkedCaller; // the parked outer call keeps its own caller (PAL_RunTriggerScript state)
            Phase = TriggerScriptPhase.WaitingForBattle;
            return;
        }
        if (_battleEndScript is not null)
        {
            _battleEndScript = null;
            ExitBattleOwnedInvocation();
            PendingBattle!.Lifecycle.AcknowledgeBattleEndScript();
            Caller = _parkedCaller;
            Phase = TriggerScriptPhase.WaitingForBattle;
            return;
        }
        Phase = TriggerScriptPhase.Completed;
        Context = new TriggerDialogueContext("upper", 0, DialogueTextTokenizer.DefaultColor, false, Context.DelayMilliseconds);
    }
}

public sealed record TriggerDialogueContext(string Box, int PortraitId, int Color, bool PlayingRng, int DelayMilliseconds);
/// <summary>The formal music-state transition (script.c 0x0043): the world WORD and its play mode.</summary>
public sealed record TriggerMusicState(int MusicId, int Mode);
/// <summary>The parked 0x004E reload boundary: where it was requested and which enemy team's
/// battle actually lost; EnemyTeamId is null when no lost battle preceded the request.</summary>
public sealed record TriggerLostReloadRequest(TriggerPosition Position, int? EnemyTeamId, bool IsBoss);
/// <summary>The parked 0x0009 scene-updating wait: frames are real update ticks, each advancing
/// the whole scene (vanish countdowns, autos, optional gestures/triggers) before the receipt.</summary>
public sealed class TriggerSceneWaitRequest(Guid id, TriggerPosition position, int framesRemaining, bool runTriggers, bool updateGestures)
{
    public Guid Id { get; } = id;
    public TriggerPosition Position { get; } = position;
    public int FramesRemaining { get; internal set; } = framesRemaining;
    public bool RunTriggers { get; } = runTriggers;
    public bool UpdateGestures { get; } = updateGestures;
}
/// <summary>The parked 0x0010 counted movement: world tile target in PAL coordinates; the world's
/// update owner advances one PAL_NPCWalkTo step per due tick until the object arrives.</summary>
public sealed record TriggerMovementRequest(Guid Id, TriggerPosition Position, int ObjectId, int TargetX, int TargetY, int NextAddress);
/// <summary>The parked 0x0005 redraw (delay in milliseconds) or 0x0073 scene fade (mode word).</summary>
public sealed record TriggerTransitionRequest(Guid Id, TriggerPosition Position, string Kind, int Parameter);
/// <summary>The parked 0x0026/0x0027 shop menu: IsBuy names PAL_BuyMenu over the authored store
/// row (StoreId = lprgStore index), otherwise PAL_SellMenu over the player's bag (StoreId 0).</summary>
public sealed record TriggerShopRequest(Guid Id, TriggerPosition Position, bool IsBuy, int StoreId);
public sealed record TriggerMessageReference(TriggerPosition Position, int StartMessageId, int Span);
public sealed record TriggerDialogueRequest(Guid Id, TriggerDialogueContext Context, IReadOnlyList<TriggerMessageReference> MessageReferences,
    DialogueMessageBlock Content, DialogueDisplaySession Display);
