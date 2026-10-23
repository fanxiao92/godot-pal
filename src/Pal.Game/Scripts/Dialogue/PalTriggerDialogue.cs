using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Pal.Core;
using Pal.Game.Resources;

namespace Pal.Game.Dialogue;

/// <summary>Modern event I/O and display receipts. PalDialogueDisplay alone advances each display.</summary>
public partial class PalTriggerDialogue : Node
{
    private PalDialogueDisplay? _display;
    private TriggerEventCollection? _events;
    private DialogueLanguagePackage? _language;
    private PalSceneData? _scene;
    private PalDialogueFont? _font;
    private PalDialogueIndexedImage[] _prompts = [];
    private readonly Dictionary<int, PalDialogueIndexedImage> _portraits = new();
    private Color[] _palette = [];
    private string _directory = "";
    public TriggerScriptSession? Session { get; private set; }

    public void Initialize(PalDialogueDisplay display, string indexPath, string dialogueDirectory,
        Color[] currentScenePalette, PalSceneData scene, string language = "zh-Hans")
    {
        if (_display is not null) throw new InvalidOperationException("Trigger dialogue is already initialized.");
        if (display.Session is { Current.Phase: not DialogueDisplayPhase.Completed })
            throw new InvalidOperationException("The display is already in use.");
        if (language is not ("zh-Hans" or "en")) throw new InvalidDataException($"Unsupported dialogue language: {language}");
        ArgumentNullException.ThrowIfNull(scene);
        if (currentScenePalette.Length != 256) throw new InvalidDataException("Trigger dialogue requires the active 256-color scene palette.");
        _directory = dialogueDirectory.TrimEnd('/');
        var events = TriggerEventLoader.Load(indexPath.Replace('\\', '/'));
        var nativeLanguage = Load<PalDialogueLanguage>($"Language.{language}.tres");
        if (nativeLanguage.Language != language) throw new InvalidDataException($"{nativeLanguage.ResourcePath}: selected language identity differs from {language}.");
        DialogueLanguagePackage package;
        try { package = nativeLanguage.ToCorePackage(); }
        catch (InvalidDataException error) { throw new InvalidDataException($"{nativeLanguage.ResourcePath}: {error.Message}", error); }
        var font = Load<PalDialogueFont>("Font.tres");
        var prompts = Enumerable.Range(0, 3).Select(frame => Load<PalDialogueIndexedImage>($"Prompt-{frame:D2}.tres")).ToArray();
        foreach (var prompt in prompts)
        {
            try { prompt.Validate(); }
            catch (InvalidDataException error) { throw new InvalidDataException($"{prompt.ResourcePath}: {error.Message}", error); }
        }
        _events = events;
        _language = package;
        _font = font;
        _prompts = prompts;
        _palette = currentScenePalette.ToArray();
        _scene = scene;
        Session = new TriggerScriptSession(events, package);
        _display = display;
        _display.UpdateCompleted += OnDisplayUpdated;
    }

    public void Start(string fileId, string entryId, int caller)
    {
        if (Session is null || _events is null || _language is null || _display is null)
            throw new InvalidOperationException("Initialize trigger dialogue before starting an entry.");
        if (Session.BlocksSceneUpdates) throw new InvalidOperationException("A trigger call is already active.");
        if (_display.Session is { Current.Phase: not DialogueDisplayPhase.Completed })
            throw new InvalidOperationException("The display is already in use.");
        TriggerEventEntry entry = _events.GetEntry(fileId, entryId);
        ValidateOwner(entry);
        // Preflight every selected display and native resource before modifying the real call.
        // This separate executor uses the same public rules; it never advances request.Display.
        var preview = new TriggerScriptSession(_events, _language, Session.Context.Color);
        preview.Start(fileId, entryId, caller);
        while (preview.PendingDisplay is { } request)
        {
            ValidateRequest(request);
            while (request.Display.Current.Phase != DialogueDisplayPhase.Completed) request.Display.Update(request.Display.Current.Phase == DialogueDisplayPhase.WaitingForAdvance ? 100u : int.MaxValue, true, true);
            preview.AcknowledgeDisplay(request.Id);
        }
        Session.Start(fileId, entryId, caller);
        BindPendingDisplay();
    }

    private void ValidateOwner(TriggerEventEntry entry)
    {
        int? sourceEntry = entry.OwnerType switch
        {
            "eventObject" when entry.Slot == "wTriggerScript" => _scene!.Objects.SingleOrDefault(obj => obj.Id == entry.OwnerId)?.WTriggerScript,
            "scene" when entry.OwnerId == _scene!.SceneId && entry.Slot == "wScriptOnEnter" => _scene.WScriptOnEnter,
            "scene" when entry.OwnerId == _scene!.SceneId && entry.Slot == "wScriptOnTeleport" => _scene.WScriptOnTeleport,
            _ => null
        };
        if (sourceEntry != entry.SourceAddress)
            throw new InvalidDataException($"{entry.FileId}.json entry {entry.Id} @{entry.SourceAddress:X4}: owner {entry.OwnerType}:{entry.OwnerId}/{entry.Slot} does not match the bound native scene.");
    }

    private PalDialogueIndexedImage? Portrait(TriggerDialogueRequest request)
    {
        int id = request.Context.PortraitId;
        if (id == 0) return null;
        if (!_portraits.TryGetValue(id, out var portrait))
        {
            portrait = Load<PalDialogueIndexedImage>($"Portrait-{id:D3}.tres");
            _portraits.Add(id, portrait);
        }
        return portrait;
    }

    private void ValidateRequest(TriggerDialogueRequest request)
    {
        try { PalDialogueDisplay.ValidateContent(request.Content, request.Context.Color, _font!, Portrait(request), _prompts, _palette); }
        catch (InvalidDataException error) { throw RequestError(request, error); }
    }

    private void BindPendingDisplay()
    {
        while (Session!.PendingDisplay is { } request)
        {
            try { _display!.Start(request, _font!, Portrait(request), _prompts, _palette); }
            catch (InvalidDataException error) { throw RequestError(request, error); }
            // A completed display can still carry a required restoration effect. Apply it
            // synchronously, then receipt it without input, time or an extra engine frame.
            if (request.Display.Current.Phase != DialogueDisplayPhase.Completed) return;
            Session.AcknowledgeDisplay(request.Id);
        }
    }

    private static InvalidDataException RequestError(TriggerDialogueRequest request, InvalidDataException error) =>
        new($"{request.MessageReferences[0].Position}: {error.Message}", error);

    private void OnDisplayUpdated()
    {
        if (Session?.PendingDisplay is not { } request || request.Display.Current.Phase != DialogueDisplayPhase.Completed) return;
        // Called after the display's real _Process has applied completion/palette restoration.
        // Binding a new request here does not forward this update's input or elapsed time.
        if (Session.AcknowledgeDisplay(request.Id)) BindPendingDisplay();
    }

    private T Load<T>(string name) where T : Resource
    {
        string path = _directory + "/" + name;
        if (!Godot.FileAccess.FileExists(path)) throw new InvalidDataException($"Missing native dialogue resource: {path}");
        T resource = ResourceLoader.Load<T>(path) ?? throw new InvalidDataException($"Invalid native dialogue resource: {path}");
        string identity = resource switch
        {
            PalDialogueLanguage value => value.SourceIdentity,
            PalDialogueFont value => value.SourceIdentity,
            PalDialogueIndexedImage value => value.SourceIdentity,
            _ => ""
        };
        if (string.IsNullOrWhiteSpace(identity)) throw new InvalidDataException($"{path}: missing source identity.");
        return resource;
    }

    public override void _ExitTree()
    {
        if (_display is not null) _display.UpdateCompleted -= OnDisplayUpdated;
    }
}
