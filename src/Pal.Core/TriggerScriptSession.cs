namespace Pal.Core;

public enum TriggerScriptPhase { Ready, WaitingForDisplay, Completed, WaitingForBattle, WaitingForLostReload,
    WaitingForSceneWait, WaitingForMovement, WaitingForTransition }

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
    private ClassicBattleRoleHook? _battleHook;
    private ClassicBattleEndScript? _battleEndScript;
    private int _parkedCaller;
    private (int TeamId, bool IsBoss)? _lostBattleContext;

    // Metadata discovery for the host's selected interaction; does not start or preview a call.
    public IReadOnlyList<TriggerEventEntry> Entries => _events.Entries;
    public TriggerScriptPhase Phase { get; private set; }
    public bool BlocksSceneUpdates => Phase is TriggerScriptPhase.WaitingForDisplay or TriggerScriptPhase.WaitingForBattle
        or TriggerScriptPhase.WaitingForLostReload or TriggerScriptPhase.WaitingForSceneWait
        or TriggerScriptPhase.WaitingForMovement or TriggerScriptPhase.WaitingForTransition;
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
    public IReadOnlyDictionary<int, BattleRoleSnapshot>? BattleRoles { get; private set; }
    public BattleRoleSnapshot? BattleRole => BattleRoles is null ? null : BattleRoles.Count == 1 ? BattleRoles.Values.Single()
        : throw new InvalidOperationException("Use BattleRoles for a multiplayer trigger session.");
    public TriggerDialogueContext Context { get; private set; }

    public TriggerScriptSession(TriggerEventCollection events, DialogueLanguagePackage language, int inheritedColor = DialogueTextTokenizer.DefaultColor)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(language);
        if (inheritedColor is < 0 or > 255) throw new ArgumentOutOfRangeException(nameof(inheritedColor));
        _events = events;
        _language = language;
        Context = new TriggerDialogueContext("upper", 0, inheritedColor, false, 24);
    }

    public TriggerScriptSession(TriggerEventCollection events, DialogueLanguagePackage language,
        TriggerBattleContent battleContent, IBattleRandom battleRandom, int inheritedColor = DialogueTextTokenizer.DefaultColor)
        : this(events, language, inheritedColor)
    {
        ArgumentNullException.ThrowIfNull(battleContent);
        ArgumentNullException.ThrowIfNull(battleRandom);
        _battleContent = battleContent;
        _battleRandom = battleRandom;
        BattleRoles = battleContent.InitialRoles;
    }

    /// <summary>Dynamic-scene construction (no battle support): world mutations apply directly to
    /// the supplied world; startBattle stays an unsupported command in this session.</summary>
    public TriggerScriptSession(TriggerEventCollection events, DialogueLanguagePackage language,
        DynamicEventWorld world, int inheritedColor = DialogueTextTokenizer.DefaultColor)
        : this(events, language, inheritedColor)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
    }

    /// <summary>Pure selected-entry capability check against the content's initial role. Does not start a call,
    /// create a battle or require randomness; later Start calls still validate their current settled role.</summary>
    public static TriggerEntryRequirements ValidateEntry(TriggerEventCollection events, DialogueLanguagePackage language,
        TriggerBattleContent battleContent, string fileId, string entryId, int inheritedColor = DialogueTextTokenizer.DefaultColor)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(battleContent);
        if (inheritedColor is < 0 or > 255) throw new ArgumentOutOfRangeException(nameof(inheritedColor));
        var entry = events.GetEntry(fileId, entryId);
        if (entry.Kind != "trigger") throw new InvalidDataException($"{fileId}.json entry {entryId} @{entry.SourceAddress:X4}: auto context is unsupported.");
        var requirements = new EntryRequirementsCollector();
        ValidateCapabilityClosure(events, language, battleContent, battleContent.InitialRoles, inheritedColor, entry.SourceAddress, requirements);
        return new(requirements.Displays, requirements.Portraits, requirements.Teams);
    }

    public void Start(string fileId, string entryId, int caller) => StartCall(fileId, entryId, caller, null);

    public void Start(string fileId, string entryId, int caller, Guid sourceRequestId)
    {
        if (sourceRequestId == Guid.Empty) throw new ArgumentException("A source request ID must be nonempty.", nameof(sourceRequestId));
        StartCall(fileId, entryId, caller, sourceRequestId);
    }

    private void StartCall(string fileId, string entryId, int caller, Guid? sourceRequestId)
    {
        if (BlocksSceneUpdates) throw new InvalidOperationException("A trigger call is already active.");
        if (caller is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(caller));
        var entry = _events.GetEntry(fileId, entryId);
        if (entry.Kind != "trigger") throw new InvalidDataException($"{fileId}.json entry {entryId} @{entry.SourceAddress:X4}: auto context is unsupported.");
        ValidateCapabilityClosure(entry.SourceAddress, TriggerScriptContext.Event);
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
        BattleRoles = request.Lifecycle.Current.Roles;
        // The reload diagnostic carries the enemy team/context of the battle that actually lost.
        if (completion!.Outcome == ClassicBattlePhase.Lost) _lostBattleContext = (request.TeamId, request.IsBoss);
        _address = completion!.Outcome == ClassicBattlePhase.Lost ? request.LossAddress ?? request.NextAddress : request.NextAddress;
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

    /// <summary>Runs the paused battle's role-hook script with the formal caller semantics (the dying
    /// member's role, or the coverer's role for a friend death). The outer call stays parked on its
    /// startBattle command; preflight covers every random branch before any side effect.</summary>
    public void StartBattleHook()
    {
        if (Phase != TriggerScriptPhase.WaitingForBattle || PendingBattle is null)
            throw new InvalidOperationException("No battle is waiting for this trigger call.");
        if (_battleHook is not null) throw new InvalidOperationException("A battle role hook is already running.");
        var hook = PendingBattleHook ?? throw new InvalidOperationException("The waiting battle has no pending role hook.");
        ValidateCapabilityClosure(hook.ScriptAddress, TriggerScriptContext.RoleHook);
        _battleHook = hook;
        _parkedCaller = Caller;
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
        ValidateCapabilityClosure(script.ScriptAddress, TriggerScriptContext.BattleEnd);
        _battleEndScript = script;
        _parkedCaller = Caller;
        _lastCaller = Caller = script.EnemySlot;
        _address = script.ScriptAddress;
        Continue();
    }

    private void ValidateCapabilityClosure(int address, TriggerScriptContext context) =>
        ValidateCapabilityClosure(_events, _language, _battleContent, BattleRoles, Context.Color, address, context: context);

    private sealed class EntryRequirementsCollector
    {
        internal readonly List<TriggerDisplayRequirement> Displays = new();
        internal readonly HashSet<int> Portraits = new();
        internal readonly HashSet<int> Teams = new();
    }

    private static void ValidateCapabilityClosure(TriggerEventCollection events, DialogueLanguagePackage language,
        TriggerBattleContent? battleContent, IReadOnlyDictionary<int, BattleRoleSnapshot>? battleRoles, int color, int address,
        EntryRequirementsCollector? requirements = null, TriggerScriptContext context = TriggerScriptContext.Event)
    {
        var pending = new Stack<(int Address, TriggerScriptContext Context)>();
        var visited = new HashSet<(int Address, TriggerScriptContext Context)>();
        pending.Push((address, context));
        while (pending.TryPop(out var item))
        {
            address = item.Address;
            if (address == 0 || !visited.Add(item)) continue;
            var command = events.CommandAt(address);
            if (command.Op == "end" && command.Mode == "keep") continue;
            if (command.Op == "startBattle" && battleContent is not null && item.Context == TriggerScriptContext.Event)
            {
                try { battleContent.Validate(command.TeamId, battleRoles!); }
                catch (InvalidDataException exception) { throw new InvalidDataException($"{command.Position}: {exception.Message}", exception); }
                requirements?.Teams.Add(command.TeamId);
                pending.Push((Next(command), TriggerScriptContext.Event));
                if (command.LossAddress is { } loss) pending.Push((loss, TriggerScriptContext.Event));
                if (command.FleeAddress is { } flee) pending.Push((flee, TriggerScriptContext.Event));
                // fight.c 768-880: the party's dying and friend-death scripts may run mid-battle; their
                // whole random closure is preflighted with the battle's declared party players.
                foreach (var player in battleContent.SettledPlayers(battleRoles!))
                {
                    if (player.FriendDeathScript != 0) PushHook(events, pending, command.Position, player.FriendDeathScript);
                    if (player.DyingScript != 0) PushHook(events, pending, command.Position, player.DyingScript);
                }
                // battle.c 1334-1337: every enemy slot's post-victory script runs after the growth
                // settlement; the whole closure (both random branches included) is preflighted here.
                foreach (var (enemy, slot) in battleContent.Enemies(command.TeamId).Select((enemy, slot) => (enemy, slot)))
                    if (enemy.BattleEndScript != 0)
                        PushScript(events, pending, command.Position, enemy.BattleEndScript, TriggerScriptContext.BattleEnd);
            }
            else if (command.Op == "randomJump" && battleContent is not null)
            {
                // script.c 3301: both the rate-miss successor and the jump target are real branches.
                pending.Push((Next(command), item.Context));
                pending.Push((command.TargetAddress!.Value, item.Context));
            }
            else if (command.Op == "setMusic" || command.Op == "fadeToRed")
            {
                pending.Push((Next(command), item.Context));
            }
            else if (command.Op is "walkStep" or "face" or "setState" or "hide" or "setTriggerMode"
                or "setSceneScripts" or "setObjectScript" or "setPartyPos" or "setPartyFacing"
                or "waitUpdate" or "redraw" or "fadeScene" or "markFailed" or "walkTo"
                or "setObjectPosition" or "setObjectGesture" or "changeHpMp" or "revivePlayer"
                or "setAutoEntry" or "setTriggerEntry" or "setBattlefield" or "fadeOut" or "setParty"
                or "noop" or "setStateRange")
            {
                // #52 dynamic-story verbs: they are never branch points; execution applies them
                // through the dynamic world (or parks the call). No-world sessions reject them at
                // execution, keeping the structural walk shared.
                pending.Push((Next(command), item.Context));
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
                pending.Push((Next(command), TriggerScriptContext.RoleHook));
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
                pending.Push((address, item.Context));
            }
            else if (command.Op == "setDialog" && command.Box is "upper" or "lower" or "centerWindow" or "center" && command.RngWord == 0)
            {
                if (command.PortraitId != 0) requirements?.Portraits.Add(command.PortraitId);
                pending.Push((Next(command), item.Context));
            }
            else throw new InvalidDataException($"{command.Position}: unsupported trigger command/context '{command.Op}'.");
        }
    }

    private static void PushHook(TriggerEventCollection events, Stack<(int Address, TriggerScriptContext Context)> pending,
        TriggerPosition position, int scriptAddress)
    {
        if (scriptAddress >= events.InstructionCount)
            throw new InvalidDataException($"{position}: role hook address {scriptAddress:X4} is outside the event collection.");
        pending.Push((scriptAddress, TriggerScriptContext.RoleHook));
    }

    private static void PushScript(TriggerEventCollection events, Stack<(int Address, TriggerScriptContext Context)> pending,
        TriggerPosition position, int scriptAddress, TriggerScriptContext context)
    {
        if (scriptAddress >= events.InstructionCount)
            throw new InvalidDataException($"{position}: battle-end script address {scriptAddress:X4} is outside the event collection.");
        pending.Push((scriptAddress, context));
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

    private void Continue()
    {
        while (_address != 0)
        {
            var command = _events.CommandAt(_address);
            CurrentPosition = command.Position;
            switch (command.Op)
            {
                case "end" when command.Mode == "keep": Complete(); return;
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
                    if (command.Op == "changeHp") PendingBattle!.Lifecycle.ApplyRoleHookHp(_battleHook.PartyIndex, command.Amount);
                    else PendingBattle!.Lifecycle.ApplyRoleHookMp(_battleHook.PartyIndex, command.Amount);
                    _address = Next(command);
                    break;
                case "boostStat" when _battleHook is not null:
                    if (command.RoleOperand != 0)
                        throw new InvalidDataException($"{command.Position}: boostStat must target the role hook's caller.");
                    PendingBattle!.Lifecycle.ApplyRoleHookStatBoost(_battleHook.PartyIndex, command.StatField, command.Amount);
                    _address = Next(command);
                    break;
                case "startBattle" when _battleContent is not null:
                    var lifecycle = new ClassicBattleLifecycleSession(BattleRoles!.Values.ToArray(), _battleContent.SettledPlayers(BattleRoles),
                        _battleContent.Enemies(command.TeamId), _battleContent.GrowthByRole, _battleRandom!,
                        BattleEquipmentState.Initialize(_battleContent), runEnemyBattleEndScripts: true, isBossBattle: command.IsBoss);
                    PendingBattle = new TriggerBattleRequest(Guid.NewGuid(), Caller, command.Position, command.TeamId, command.IsBoss,
                        Next(command), command.LossAddress, command.FleeAddress, lifecycle);
                    Phase = TriggerScriptPhase.WaitingForBattle;
                    return;
                case "setMusic":
                    Music = new(command.MusicId, command.MusicMode);
                    _address = Next(command);
                    break;
                case "fadeToRed":
                    FadedToRed = true;
                    _address = Next(command);
                    break;
                case "requestReload":
                    // TODO(#51): implement the real 0x004E save-slot reload (PAL_FadeOut(1) then
                    // PAL_ReloadInNextTick); without a save system the call parks in this terminal
                    // phase and the host prints its one lost-reload diagnostic.
                    PendingLostReload = new(command.Position, _lostBattleContext?.TeamId, _lostBattleContext?.IsBoss ?? false);
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
                    if (command.ObjectId != 0) _world.SetObjectState(command.ObjectId == 0xffff ? Caller : command.ObjectId, command.State, command.Position);
                    _address = Next(command);
                    break;
                case "hide" when _world is not null:
                    // script.c 0x0052: caller's state *= -1 and vanish time = frames (0 -> 800).
                    _world.HideObject(Caller, command.Frames == 0 ? 800 : command.Frames);
                    _address = Next(command);
                    break;
                case "setTriggerMode" when _world is not null:
                    // script.c 0x0040: nonzero operand0 selects the target object.
                    if (command.ObjectId != 0) _world.SetTriggerMode(command.ObjectId == 0xffff ? Caller : command.ObjectId, command.TriggerModeWord, command.Position);
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
                    _world.SetObjectPosition(command.ObjectId is 0 or 0xffff ? Caller : command.ObjectId,
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
                    if (command.ObjectId != 0)
                        _world.SetObjectEntry(command.ObjectId == 0xffff ? Caller : command.ObjectId, command.Value, true, command.Position);
                    _address = Next(command);
                    break;
                case "setAutoEntry" when _world is not null:
                    // script.c 0x0024: same selection rule as the trigger entry write.
                    if (command.ObjectId != 0)
                        _world.SetObjectEntry(command.ObjectId == 0xffff ? Caller : command.ObjectId, command.Value, false, command.Position);
                    _address = Next(command);
                    break;
                case "setBattlefield" when _world is not null:
                    // script.c 0x004A: wNumBattleField word.
                    _world.SetBattlefield(command.Frames);
                    _address = Next(command);
                    break;
                case "fadeOut" when _world is not null:
                    // script.c 0x0050: fade out (0 -> 1 frame unit); the host fades back in after.
                    PendingTransition = new TriggerTransitionRequest(Guid.NewGuid(), command.Position, "fadeOut",
                        command.Frames == 0 ? 1 : command.Frames);
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
                    _world.SetStateRange(command.ObjectId, command.MemberIndex, command.State, command.Position);
                    _address = Next(command);
                    break;
                case "changeHpMp" when _world is not null:
                    // script.c 0x001D: HP and MP move by the same signed amount, clamped at the
                    // words' bounds; scope 0 is the caller role, nonzero the whole party.
                    _world.ChangeHpMp(command.Scope, Caller, command.Amount, command.Position);
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
                    // script.c 0x0073: backup screen, rebuild the scene, fade by mode word.
                    PendingTransition = new TriggerTransitionRequest(Guid.NewGuid(), command.Position, "fadeScene", command.FadeMode);
                    _address = Next(command);
                    Phase = TriggerScriptPhase.WaitingForTransition;
                    return;
                default: throw new InvalidDataException($"{command.Position}: unsupported trigger command/context '{command.Op}'.");
            }
        }
        CurrentPosition = _events.CommandAt(0).Position;
        Complete();
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
            _battleHook = null;
            PendingBattle!.Lifecycle.AcknowledgeRoleHook();
            Caller = _parkedCaller; // the parked outer call keeps its own caller (PAL_RunTriggerScript state)
            Phase = TriggerScriptPhase.WaitingForBattle;
            return;
        }
        if (_battleEndScript is not null)
        {
            _battleEndScript = null;
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
public sealed record TriggerMessageReference(TriggerPosition Position, int StartMessageId, int Span);
public sealed record TriggerDialogueRequest(Guid Id, TriggerDialogueContext Context, IReadOnlyList<TriggerMessageReference> MessageReferences,
    DialogueMessageBlock Content, DialogueDisplaySession Display);
