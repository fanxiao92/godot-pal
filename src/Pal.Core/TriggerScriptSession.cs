namespace Pal.Core;

public enum TriggerScriptPhase { Ready, WaitingForDisplay, Completed, WaitingForBattle }

/// <summary>Runs the supported trigger slice to a real display, battle or stop boundary, never one opcode per tick.</summary>
public sealed class TriggerScriptSession
{
    private readonly TriggerEventCollection _events;
    private readonly DialogueLanguagePackage _language;
    private int _address;
    private int _lastCaller;
    private readonly TriggerBattleContent? _battleContent;
    private readonly IBattleRandom? _battleRandom;
    private ClassicBattleRoleHook? _battleHook;
    private int _parkedCaller;

    // Metadata discovery for the host's selected interaction; does not start or preview a call.
    public IReadOnlyList<TriggerEventEntry> Entries => _events.Entries;
    public TriggerScriptPhase Phase { get; private set; }
    public bool BlocksSceneUpdates => Phase is TriggerScriptPhase.WaitingForDisplay or TriggerScriptPhase.WaitingForBattle;
    public Guid CallId { get; private set; }
    /// <summary>The interaction request which actually started this call; null for legacy callers.</summary>
    public Guid? SourceRequestId { get; private set; }
    public TriggerEventEntry? StartedEntry { get; private set; }
    public int Caller { get; private set; }
    public int ReturnEntry { get; private set; }
    public TriggerPosition? CurrentPosition { get; private set; }
    public TriggerDialogueRequest? PendingDisplay { get; private set; }
    public TriggerBattleRequest? PendingBattle { get; private set; }
    /// <summary>The battle's paused role hook (fight.c 768-880) awaiting StartBattleHook; the paused combat
    /// is the only source of this state, never a host-written input.</summary>
    public ClassicBattleRoleHook? PendingBattleHook => PendingBattle?.Lifecycle.Current.Combat.PendingRoleHook;
    /// <summary>The role hook whose script this executor is currently running; null outside a hook run.</summary>
    public ClassicBattleRoleHook? RunningBattleHook => _battleHook;
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
        ValidateCapabilityClosure(entry.SourceAddress);
        CallId = Guid.NewGuid();
        StartedEntry = entry;
        SourceRequestId = sourceRequestId;
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
        _address = completion!.Outcome == ClassicBattlePhase.Lost ? request.LossAddress ?? request.NextAddress : request.NextAddress;
        PendingBattle = null;
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
        ValidateCapabilityClosure(hook.ScriptAddress, battleHook: true);
        _battleHook = hook;
        _parkedCaller = Caller;
        _lastCaller = Caller = hook.RoleId;
        _address = hook.ScriptAddress;
        Continue();
    }

    private void ValidateCapabilityClosure(int address, bool battleHook = false) =>
        ValidateCapabilityClosure(_events, _language, _battleContent, BattleRoles, Context.Color, address, battleHook: battleHook);

    private sealed class EntryRequirementsCollector
    {
        internal readonly List<TriggerDisplayRequirement> Displays = new();
        internal readonly HashSet<int> Portraits = new();
        internal readonly HashSet<int> Teams = new();
    }

    private static void ValidateCapabilityClosure(TriggerEventCollection events, DialogueLanguagePackage language,
        TriggerBattleContent? battleContent, IReadOnlyDictionary<int, BattleRoleSnapshot>? battleRoles, int color, int address,
        EntryRequirementsCollector? requirements = null, bool battleHook = false)
    {
        var pending = new Stack<(int Address, bool Hook)>();
        var visited = new HashSet<(int Address, bool Hook)>();
        pending.Push((address, battleHook));
        while (pending.TryPop(out var item))
        {
            address = item.Address;
            if (address == 0 || !visited.Add(item)) continue;
            var command = events.CommandAt(address);
            if (command.Op == "end" && command.Mode == "keep") continue;
            if (command.Op == "startBattle" && battleContent is not null && !item.Hook)
            {
                try { battleContent.Validate(command.TeamId, battleRoles!); }
                catch (InvalidDataException exception) { throw new InvalidDataException($"{command.Position}: {exception.Message}", exception); }
                requirements?.Teams.Add(command.TeamId);
                pending.Push((Next(command), false));
                if (command.LossAddress is { } loss) pending.Push((loss, false));
                if (command.FleeAddress is { } flee) pending.Push((flee, false));
                // fight.c 768-880: the party's dying and friend-death scripts may run mid-battle; their
                // whole random closure is preflighted with the battle's declared party players.
                foreach (var player in battleContent.SettledPlayers(battleRoles!))
                {
                    if (player.FriendDeathScript != 0) PushHook(events, pending, command.Position, player.FriendDeathScript);
                    if (player.DyingScript != 0) PushHook(events, pending, command.Position, player.DyingScript);
                }
            }
            else if (command.Op == "randomJump" && battleContent is not null)
            {
                // script.c 3301: both the rate-miss successor and the jump target are real branches.
                pending.Push((Next(command), item.Hook));
                pending.Push((command.TargetAddress!.Value, item.Hook));
            }
            else if (command.Op is "changeHp" or "changeMp" or "boostStat")
            {
                if (!item.Hook) throw new InvalidDataException($"{command.Position}: {command.Op} is only supported inside a battle role hook.");
                if (command.Op != "boostStat" && command.Scope != 0)
                    throw new InvalidDataException($"{command.Position}: party-wide {command.Op} is unsupported; the role hook applies to its caller.");
                if (command.Op == "boostStat" && command.RoleOperand != 0)
                    throw new InvalidDataException($"{command.Position}: boostStat must target the role hook's caller.");
                if (command.Op == "boostStat" && command.StatField is not (17 or 18 or 19 or 20 or 21))
                    throw new InvalidDataException($"{command.Position}: temporary stat boost field {command.StatField} is not a supported panel word.");
                pending.Push((Next(command), true));
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
                pending.Push((address, item.Hook));
            }
            else if (command.Op == "setDialog" && command.Box is "upper" or "lower" or "centerWindow" && command.RngWord == 0)
            {
                if (command.PortraitId != 0) requirements?.Portraits.Add(command.PortraitId);
                pending.Push((Next(command), item.Hook));
            }
            else throw new InvalidDataException($"{command.Position}: unsupported trigger command/context '{command.Op}'.");
        }
    }

    private static void PushHook(TriggerEventCollection events, Stack<(int Address, bool Hook)> pending,
        TriggerPosition position, int scriptAddress)
    {
        if (scriptAddress >= events.InstructionCount)
            throw new InvalidDataException($"{position}: role hook address {scriptAddress:X4} is outside the event collection.");
        pending.Push((scriptAddress, true));
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
                case "setDialog" when command.Box == "centerWindow":
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
                        BattleEquipmentState.Initialize(_battleContent));
                    PendingBattle = new TriggerBattleRequest(Guid.NewGuid(), Caller, command.Position, command.TeamId, command.IsBoss,
                        Next(command), command.LossAddress, command.FleeAddress, lifecycle);
                    Phase = TriggerScriptPhase.WaitingForBattle;
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
        // A finished role hook resumes its paused battle; the outer call stays parked on its startBattle
        // command and keeps the hook's final dialog context (the reference dialog state is global).
        if (_battleHook is not null)
        {
            _battleHook = null;
            PendingBattle!.Lifecycle.AcknowledgeRoleHook();
            Caller = _parkedCaller; // the parked outer call keeps its own caller (PAL_RunTriggerScript state)
            Phase = TriggerScriptPhase.WaitingForBattle;
            return;
        }
        Phase = TriggerScriptPhase.Completed;
        Context = new TriggerDialogueContext("upper", 0, DialogueTextTokenizer.DefaultColor, false, Context.DelayMilliseconds);
    }
}

public sealed record TriggerDialogueContext(string Box, int PortraitId, int Color, bool PlayingRng, int DelayMilliseconds);
public sealed record TriggerMessageReference(TriggerPosition Position, int StartMessageId, int Span);
public sealed record TriggerDialogueRequest(Guid Id, TriggerDialogueContext Context, IReadOnlyList<TriggerMessageReference> MessageReferences,
    DialogueMessageBlock Content, DialogueDisplaySession Display);
