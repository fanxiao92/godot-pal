using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Pal.Core;
using Pal.Game.Resources;
using Pal.Game.Profiles;

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

    private CheckedStoryProfile? _story;
    private TriggerBattleContent? _previewContent;
    private IBattleRandom? _previewRandom;
    /// <summary>Host factory for the preflight executor; the real @09C3 mixes world verbs with
    /// battles, so its preview needs its own throwaway world (never the production one).</summary>
    internal Func<TriggerScriptSession>? PreviewSessionFactory { private get; set; }

    /// <summary>The active 256-color scene palette, set by the host before the first-battle
    /// initialization (the #53 host owns the scene, not a story profile).</summary>
    public Color[]? PaletteFromScene { private get; set; }

    /// <summary>#53 first-battle host: the same production dialogue/battle executor over the
    /// strict-loaded resources the verified closure bound.</summary>
    /// <summary>The dynamic world the first-battle host bound (script verbs mutate it); null
    /// keeps the plain session whose dynamic verbs reject precisely at execution.</summary>
    public Pal.Core.DynamicEventWorld? FirstBattleWorld { internal get; set; }

    public void Initialize(PalDialogueDisplay display, FirstBattleRealResources resources, string language, IBattleRandom random,
        PalSceneData? scene = null)
    {
        ArgumentNullException.ThrowIfNull(display); ArgumentNullException.ThrowIfNull(resources); ArgumentNullException.ThrowIfNull(random);
        _scene = scene;
        if (language is not ("zh-Hans" or "en")) throw new InvalidDataException($"Unsupported dialogue language: {language}");
        if (_display is not null || display.Session is { Current.Phase: not DialogueDisplayPhase.Completed })
            throw new InvalidOperationException("Trigger dialogue or display is already in use.");
        _events = resources.Events;
        _language = language == "zh-Hans" ? resources.Chinese : resources.English;
        _font = resources.Font;
        _prompts = resources.Prompts.ToArray();
        _palette = PaletteFromScene ?? throw new InvalidOperationException("The first-battle host must supply the active scene palette first.");
        foreach (var pair in resources.Portraits) _portraits.Add(pair.Key, pair.Value);
        _previewContent = resources.BattleContent; _previewRandom = random;
        Session = FirstBattleWorld is { } world
            ? new TriggerScriptSession(resources.Events, _language, resources.BattleContent, random, world)
            : new TriggerScriptSession(resources.Events, _language, resources.BattleContent, random);
        _display = display; display.UpdateCompleted += OnDisplayUpdated;
    }

    public void Initialize(PalDialogueDisplay display, CheckedStoryProfile story, IBattleRandom random)
    {
        ArgumentNullException.ThrowIfNull(display); ArgumentNullException.ThrowIfNull(story); ArgumentNullException.ThrowIfNull(random);
        if (_display is not null || display.Session is { Current.Phase: not DialogueDisplayPhase.Completed })
            throw new InvalidOperationException("Trigger dialogue or display is already in use.");
        _story = story; _events = story.Events; _language = story.Language; _font = story.Font;
        _prompts = story.Prompts.ToArray(); _palette = story.Palette.ToArray();
        foreach (var pair in story.Portraits) _portraits.Add(pair.Key, pair.Value);
        Session = new TriggerScriptSession(story.Events, story.Language, story.BattleContent, random);
        _display = display; display.UpdateCompleted += OnDisplayUpdated;
    }

    public bool AcknowledgeBattle(Guid requestId, ClassicBattleLifecycleSession.CompletionReceipt? completion)
    {
        if (Session is null || !Session.AcknowledgeBattle(requestId, completion)) return false;
        BindPendingDisplay();
        return true;
    }

    /// <summary>Runs the paused battle's role-hook script on the real display (fight.c 768-880);
    /// the parked outer call stays on its startBattle command until the hook completes.</summary>
    public void StartBattleHook()
    {
        if (Session is null) throw new InvalidOperationException("Initialize trigger dialogue before starting a battle hook.");
        Session.StartBattleHook();
        BindPendingDisplay();
    }

    /// <summary>Runs the finished battle's enemy post-victory script on the real display
    /// (battle.c 1334-1337); the parked outer call stays on its startBattle command.</summary>
    public void StartBattleEndScript()
    {
        if (Session is null) throw new InvalidOperationException("Initialize trigger dialogue before starting a battle-end script.");
        Session.StartBattleEndScript();
        BindPendingDisplay();
    }

    public void Start(string fileId, string entryId, int caller) => StartCall(fileId, entryId, caller, null);

    public void Start(string fileId, string entryId, int caller, Guid sourceRequestId)
    {
        if (sourceRequestId == Guid.Empty) throw new ArgumentException("A source request ID must be nonempty.", nameof(sourceRequestId));
        StartCall(fileId, entryId, caller, sourceRequestId);
    }

    private void StartCall(string fileId, string entryId, int caller, Guid? sourceRequestId)
    {
        if (Session is null || _events is null || _language is null || _display is null)
            throw new InvalidOperationException("Initialize trigger dialogue before starting an entry.");
        if (Session.BlocksSceneUpdates) throw new InvalidOperationException("A trigger call is already active.");
        if (_display.Session is { Current.Phase: not DialogueDisplayPhase.Completed })
            throw new InvalidOperationException("The display is already in use.");
        TriggerEventEntry entry = _events.GetEntry(fileId, entryId);
        if (_story is not null)
        {
            if (entry != _story.Entry) throw new InvalidDataException("Story adapter only supports its checked selected entry.");
            if (sourceRequestId is not { } requestId)
                throw new InvalidOperationException("A story trigger requires its source interaction request.");
            Session.Start(fileId, entryId, caller, requestId);
            BindPendingDisplay();
            return;
        }
        ValidateOwner(entry);
        // Preflight every selected display and native resource before modifying the real call.
        // This separate executor uses the same public rules; it never advances request.Display.
        var preview = PreviewSessionFactory is { } factory
            ? factory()
            : _previewContent is { } content && _previewRandom is { } previewRandom
                ? new TriggerScriptSession(_events, _language, content, previewRandom, Session.Context.Color)
                : new TriggerScriptSession(_events, _language, Session.Context.Color);
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
        _display = null; Session = null; _story = null; _events = null; _language = null;
        _font = null; _scene = null; _prompts = []; _portraits.Clear(); _palette = [];
    }
}
