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
    /// <summary>Transfers a throwaway executor and its owned native lifetime to StartCall.
    /// The factory must release its own allocations if construction fails before transfer.</summary>
    internal Func<(TriggerScriptSession Session, IDisposable Lifetime)>? PreviewSessionFactory { get; set; }

    /// <summary>The active 256-color scene palette, set by the host before the first-battle
    /// initialization (the #53 host owns the scene, not a story profile).</summary>
    public Color[]? PaletteFromScene { private get; set; }

    /// <summary>World supplied before initialization by the first-battle or checked Story host.
    /// A shared world also binds its live role owner to the trigger; null preserves the legacy
    /// battle-only session whose dynamic verbs reject precisely at execution.</summary>
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

    public void Initialize(PalDialogueDisplay display, CheckedStoryProfile story, IBattleRandom random,
        BattleEquipmentState? equipmentState = null, PartyPoisonState? partyPoisons = null)
    {
        ArgumentNullException.ThrowIfNull(display); ArgumentNullException.ThrowIfNull(story); ArgumentNullException.ThrowIfNull(random);
        if (_display is not null || display.Session is { Current.Phase: not DialogueDisplayPhase.Completed })
            throw new InvalidOperationException("Trigger dialogue or display is already in use.");
        _story = story; _events = story.Events; _language = story.Language; _font = story.Font;
        _prompts = story.Prompts.ToArray(); _palette = story.Palette.ToArray();
        foreach (var pair in story.Portraits) _portraits.Add(pair.Key, pair.Value);
        // #214: the checked-story host binds its shared equipment/poison owners so the next
        // trigger battle consumes the current gear (#187 ForSharedRoles) instead of the frozen
        // author snapshot. Without the world host there is no shared role owner to bind, so the
        // shared-owner request itself rejects instead of silently dropping the binding.
        Session = FirstBattleWorld is { } world
            ? TriggerScriptSession.ForSharedRoles(story.Events, story.Language, story.BattleContent, random,
                world.RoleLiveState ?? throw new InvalidOperationException("The shared story world must bind its live role owner before trigger initialization."),
                world, equipmentState: equipmentState, partyPoisons: partyPoisons)
            : equipmentState is null && partyPoisons is null
                ? new TriggerScriptSession(story.Events, story.Language, story.BattleContent, random)
                : throw new InvalidOperationException("Shared equipment or poison owners require the shared story world host.");
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
            // The story adapter runs its checked selected entry, the checked item on-use
            // entries (#215 play.c PAL_GameUseItem) and the checked magic on-use/on-success
            // entries (#216 uigame.c PAL_InGameMagicMenu); any other entry stays unsupported.
            if (entry != _story.Entry
                && !(_story.ItemUseEntries.TryGetValue(entry.OwnerId, out var itemEntry) && entry == itemEntry)
                && !(_story.MagicUseEntries.TryGetValue(entry.OwnerId, out var magicEntries)
                    && (entry == magicEntries.OnUse || entry == magicEntries.OnSuccess)))
                throw new InvalidDataException("Story adapter only supports its checked selected entry.");
            if (sourceRequestId is null)
                throw new InvalidOperationException("A story trigger requires its source interaction request.");
        }
        else ValidateOwner(entry);
        var requirements = Session.GetCurrentEntryRequirements(fileId, entryId, caller);
        var stagedPortraits = ValidateResources(entry, requirements);
        if (sourceRequestId is { } origin) Session.Start(fileId, entryId, caller, origin);
        else Session.Start(fileId, entryId, caller);
        foreach (var pair in stagedPortraits) _portraits.Add(pair.Key, pair.Value);
        BindPendingDisplay();
    }

    private Dictionary<int, PalDialogueIndexedImage> ValidateResources(TriggerEventEntry entry, TriggerEntryRequirements requirements)
    {
        var stagedPortraits = new Dictionary<int, PalDialogueIndexedImage>();
        foreach (int id in requirements.PortraitIds)
        {
            if (id == 0) continue;
            if (!_portraits.TryGetValue(id, out var portrait))
            {
                if (_story is not null || _previewContent is not null)
                    throw new InvalidDataException($"{entry.FileId}.json entry {entry.Id} @{entry.SourceAddress:X4}: missing bound dialogue portrait {id}.");
                portrait = Load<PalDialogueIndexedImage>($"Portrait-{id:D3}.tres");
                stagedPortraits.Add(id, portrait);
            }
            try { portrait.Validate(); }
            catch (InvalidDataException error) { throw new InvalidDataException($"{portrait.ResourcePath}: {error.Message}", error); }
        }
        foreach (var display in requirements.DisplayBlocks)
        {
            try { PalDialogueDisplay.ValidateContent(display.Content, Session!.Context.Color, _font!, null, _prompts, _palette); }
            catch (InvalidDataException error) { throw new InvalidDataException($"{display.MessageReferences[0].Position}: {error.Message}", error); }
        }
        return stagedPortraits;
    }

    private (TriggerScriptSession Session, IDisposable? Lifetime) CreatePreviewSession()
    {
        if (PreviewSessionFactory is { } factory) return factory();
        // Unknown legacy IBattleRandom implementations remain borrowed. Only the concrete
        // Godot adapter promises a fork; Main always supplies its owned world + RNG factory.
        var fork = (_previewRandom as GodotBattleRandom)?.Fork();
        try
        {
            var preview = _previewContent is { } content && (fork ?? _previewRandom) is { } random
                ? new TriggerScriptSession(_events!, _language!, content, random, Session!.Context.Color)
                : new TriggerScriptSession(_events!, _language!, Session!.Context.Color);
            return (preview, fork);
        }
        catch { fork?.Dispose(); throw; }
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

    /// <summary>Host hook after advancing a world-wired call (transition/wait/movement
    /// acknowledgements): bind the display the call newly parked on.</summary>
    public void BindPendingDisplays() => BindPendingDisplay();

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
        FirstBattleWorld = null; PreviewSessionFactory = null;
        _previewContent = null; _previewRandom = null; PaletteFromScene = null;
    }
}
