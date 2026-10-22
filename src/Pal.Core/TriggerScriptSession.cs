namespace Pal.Core;

public enum TriggerScriptPhase { Ready, WaitingForDisplay, Completed }

/// <summary>Runs the supported trigger slice to a real display boundary, never one opcode per tick.</summary>
public sealed class TriggerScriptSession
{
    private readonly TriggerEventCollection _events;
    private readonly DialogueLanguagePackage _language;
    private int _address;
    private int _lastCaller;

    // Metadata discovery for the host's selected interaction; does not start or preview a call.
    public IReadOnlyList<TriggerEventEntry> Entries => _events.Entries;
    public TriggerScriptPhase Phase { get; private set; }
    public bool BlocksSceneUpdates => Phase == TriggerScriptPhase.WaitingForDisplay;
    public int Caller { get; private set; }
    public int ReturnEntry { get; private set; }
    public TriggerPosition? CurrentPosition { get; private set; }
    public TriggerDialogueRequest? PendingDisplay { get; private set; }
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

    public void Start(string fileId, string entryId, int caller)
    {
        if (Phase == TriggerScriptPhase.WaitingForDisplay) throw new InvalidOperationException("A trigger call is already active.");
        if (caller is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(caller));
        var entry = _events.GetEntry(fileId, entryId);
        if (entry.Kind != "trigger") throw new InvalidDataException($"{fileId}.json entry {entryId} @{entry.SourceAddress:X4}: auto context is unsupported.");
        ValidateCapabilityClosure(entry.SourceAddress);
        Caller = caller == 65535 ? _lastCaller : caller;
        _lastCaller = Caller;
        ReturnEntry = _address = entry.SourceAddress;
        Context = Context with { DelayMilliseconds = 24 };
        Continue();
    }

    /// <summary>Accepts only the current, actually completed display; stale or premature receipts are inert.</summary>
    public bool AcknowledgeDisplay(Guid requestId)
    {
        if (PendingDisplay is not { } request || request.Id != requestId || request.Display.Current.Phase != DialogueDisplayPhase.Completed)
            return false;
        Context = Context with { Color = request.Display.TextColor, DelayMilliseconds = request.Display.DelayMilliseconds };
        PendingDisplay = null;
        Continue();
        return true;
    }

    private void ValidateCapabilityClosure(int address)
    {
        // Only a finite sequential slice is supported. Any branch/call/unknown action rejects
        // the whole call before side effects; it cannot conceal another executable path.
        int startAddress = address;
        while (address != 0)
        {
            var command = _events.CommandAt(address);
            if (command.Op == "end" && command.Mode == "keep") break;
            if (command.Op != "showDialog" && !(command.Op == "setDialog" && command.Box == "upper" && command.RngWord == 0))
                throw new InvalidDataException($"{command.Position}: unsupported trigger command/context '{command.Op}'.");
            address = Next(command);
        }
        address = startAddress;
        while (address != 0)
        {
            var command = _events.CommandAt(address);
            if (command.Op == "end") break;
            if (command.Op == "showDialog")
            {
                var content = ReadDisplay(ref address);
                try
                {
                    // Exercise the existing display rules on a separate preview, so even a later
                    // unsupported control fails before the real call changes caller or shows text.
                    var preview = new DialogueDisplaySession(content.Block, Context.Color, 0);
                    while (preview.Current.Phase != DialogueDisplayPhase.Completed) preview.Update(preview.Current.Phase == DialogueDisplayPhase.WaitingForAdvance ? 100u : int.MaxValue, true, true);
                }
                catch (InvalidDataException exception) { throw new InvalidDataException($"{command.Position}: {exception.Message}", exception); }
            }
            else address = Next(command);
        }
    }

    private (DialogueMessageBlock Block, IReadOnlyList<TriggerMessageReference> References) ReadDisplay(ref int address)
    {
        var references = new List<TriggerMessageReference>();
        var parts = new List<DialogueMessagePart>();
        do
        {
            var command = _events.CommandAt(address);
            int start = command.MessageId, last = start;
            var position = command.Position;
            address = Next(command);
            while (address != 0 && _events.CommandAt(address) is { Op: "showDialog" } next && next.MessageId == last + 1)
            {
                last = next.MessageId;
                address = Next(next);
            }
            try { parts.AddRange(_language.Resolve(start, last - start).Parts); }
            catch (InvalidDataException exception) { throw new InvalidDataException($"{position}: {exception.Message}", exception); }
            references.Add(new TriggerMessageReference(position, start, last - start));
        } while (address != 0 && _events.CommandAt(address).Op == "showDialog");
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
                case "setDialog" when command.Box == "upper" && command.RngWord == 0:
                    Context = Context with { Box = command.Box, PortraitId = command.PortraitId, Color = command.Color == 0 ? Context.Color : command.Color };
                    _address = Next(command);
                    break;
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
        Phase = TriggerScriptPhase.Completed;
        Context = new TriggerDialogueContext("upper", 0, DialogueTextTokenizer.DefaultColor, false, Context.DelayMilliseconds);
    }
}

public sealed record TriggerDialogueContext(string Box, int PortraitId, int Color, bool PlayingRng, int DelayMilliseconds);
public sealed record TriggerMessageReference(TriggerPosition Position, int StartMessageId, int Span);
public sealed record TriggerDialogueRequest(Guid Id, TriggerDialogueContext Context, IReadOnlyList<TriggerMessageReference> MessageReferences,
    DialogueMessageBlock Content, DialogueDisplaySession Display);
