using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Pal.Core;
using Pal.Game.Dialogue;
using Pal.Game.Map;
using Pal.Game.Profiles;
using Pal.Game.Resources;

namespace Pal.Game;

public partial class Main : Node2D
{
    private static readonly string[] DirectionActions =
        ["exploration_south", "exploration_west", "exploration_north", "exploration_east"];
    // Matches ExplorationKey order: primary/alternate pairs for each direction action.
    private static readonly Key[] DirectionKeys =
        [Key.Down, Key.S, Key.Left, Key.A, Key.Up, Key.W, Key.Right, Key.D];
    private ExplorationInput? input;
    private FirstMandatoryBattleSession? battle;
    private PalMapScene? mapScene;
    private NpcInteractionRequest? activeTrigger;
    private FirstMandatoryBattleTriggerRequest? firstBattleActiveTrigger;
    private bool awaitingFirstBattleDraw;
    private bool firstBattleRendererSubscribed;
    private GodotBattleRandom? firstBattleRandom;
    private ulong? initialFirstBattleSeed;
    internal GodotBattleRandom? FirstBattleRandom => firstBattleRandom;
    private DynamicEventWorld? firstBattleWorld;
    private RoleLiveState? roleLiveState;
    // #213 exploration menu: the story-profile host binds the Core menu session over the shared
    // live owners; the open menu owns input and freezes exploration updates (uigame.c).
    private ExplorationMenuSession? menu;
    private PalMenuView? menuView;
    private bool menuOpenPressed;
    private readonly List<ExplorationMenuKey> pendingMenuKeys = new();
    // #217 shop (story-profile context): the Core shop session over the shared cash/inventory
    // owners; the open shop owns input and freezes exploration updates while the trigger call
    // parks inside PAL_BuyMenu/PAL_SellMenu (script.c 0x0026/0x0027).
    internal ExplorationShopSession? shop;
    internal PalShopView? shopView;
    internal Guid shopRequestId;
    private readonly List<ExplorationMenuKey> pendingShopKeys = new();
    private bool awaitingSceneDraw;
    private bool rendererSubscribed;
    private GameSceneConfiguration? configuration;
    private bool initializationAttempted;
    private bool exited;
    public string? Failure { get; private set; }
    public event Action<string>? Failed;
    /// <summary>True once the 0x004E lost-reload boundary parked the story call: business
    /// advancement stops for the rest of this run, distinct from an unexpected Failure.</summary>
    public bool StoryLostTerminal { get; private set; }
    /// <summary>How many lost-reload diagnostics this Main printed; the boundary prints exactly once.</summary>
    public int StoryLostReloadDiagnostics { get; private set; }

    /// <summary>Configure once before entering the tree. The host owns this node, including on failure.
    /// Initialization occurs in _Ready; failed or removed scenes must be freed, not reused.</summary>
    public void Configure(GameSceneConfiguration selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (IsInsideTree() || configuration is not null || initializationAttempted || exited || Failure is not null)
            throw new InvalidOperationException("Configure the scene exactly once before entering the tree.");
        configuration = selection;
    }

    internal void Configure(GameSceneConfiguration selection, ulong initialFirstBattleSeed)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.BattleProfile is null)
            throw new ArgumentException("An initial first-battle seed requires a first-battle profile.", nameof(selection));
        Configure(selection);
        this.initialFirstBattleSeed = initialFirstBattleSeed;
    }

    public ExplorationState? Exploration => Battle?.Current ?? Interaction?.Current ?? dynamicSession?.Current ?? input?.Current;
    public NpcInteractionSession? Interaction { get; private set; }
    public FirstMandatoryBattleSession? Battle => battle;
    public string? SelectedLanguage { get; private set; }
    public PalTriggerDialogue? Dialogue { get; private set; }
    public PalDialogueDisplay? Display { get; private set; }
    /// <summary>The #213 exploration menu session bound to the story-profile host, or null on
    /// hosts without the formal shared live-state chain (supported context of this leaf).</summary>
    public ExplorationMenuSession? Menu => menu;
    internal PalMenuView? MenuView => menuView;
    internal ExplorationShopSession? Shop => shop;
    internal PalShopView? ShopView => shopView;
    // Actual Main callback completion; display completion has its own production boundary.
    public event Action? UpdateCompleted;

    public override void _Ready()
    {
        if (initializationAttempted || exited || Failure is not null) return;
        initializationAttempted = true;
        string? selectedProfilePath = null, scenePath = null, staticProfilePath = null;
        try
        {
            var selected = configuration ?? throw new InvalidOperationException("Configure the scene before entering the tree.");
            selectedProfilePath = selected.DynamicProfile ?? selected.StoryProfile;
            if (selected.DynamicProfile is not null) { InitializeDynamic(selected); return; }
            if (selected.StoryProfile is not null) { InitializeStory(selected); return; }
            string root = selected.Root, profile = selected.Profile, language = selected.Language;
            selectedProfilePath = selected.BattleProfile ?? selected.DialogueProfile ?? selected.Profile;
            bool staticAcceptance = selected.StaticAcceptance;
            string? dialogueProfile = selected.DialogueProfile;
            string? battleProfile = selected.BattleProfile;
            PalNpcInteractionProfile? npc = null;
            CheckedFirstMandatoryBattleProfile? firstBattle = null;
            if (dialogueProfile is not null)
            {
                npc = NativeNpcInteractionProfile.Load(dialogueProfile);
                root = npc.StaticResources; profile = npc.StaticProfile;
            }
            else if (battleProfile is not null)
            {
                firstBattle = NativeFirstMandatoryBattleProfile.Load(battleProfile);
                root = firstBattle.StaticResources; profile = firstBattle.StaticProfile;
            }
            scenePath = root + "/Scenes/Scene.tscn";
            staticProfilePath = profile;
            var map = NativeSceneLoader.Load(root);
            CheckedExplorationProfile checkedProfile;
            FirstMandatoryBattleSession? preparedBattle = null;
            ExplorationInput? preparedInput = null;
            try
            {
                if (firstBattle is not null)
                {
                    (checkedProfile, preparedBattle) = NativeFirstMandatoryBattleProfile.ValidateStaticSelection(firstBattle, map);
                }
                else checkedProfile = NativeExplorationProfile.Load(profile, map.Data);
                if (staticAcceptance || npc is not null) ValidateStaticAcceptance(map.Data, checkedProfile);
                if (npc is not null)
                {
                    ValidateDialogueScene(map.Data, npc);
                    checkedProfile.SetPalette(map.Data, Palette(map.Data));
                }
                if (npc is null && preparedBattle is null) preparedInput = new ExplorationInput(new ExplorationSession(checkedProfile.Rules, checkedProfile.Leader));
                map.InitializeScene(preparedBattle?.Current.Plan ?? checkedProfile.Plan, checkedProfile.TextureFor);
            }
            catch { map.Free(); throw; }
            battle = preparedBattle;
            input = preparedInput;
            SelectedLanguage = language;
            mapScene = map;
            AddChild(map);
            if (npc is not null)
            {
                Display = new PalDialogueDisplay();
                Display.Failed += DisplayFailed;
                AddChild(Display);
                Dialogue = new PalTriggerDialogue(); AddChild(Dialogue);
                Display.ActivePaletteChanged += colors =>
                {
                    if (Failure is not null || exited) return;
                    checkedProfile.SetPalette(map.Data, colors);
                    map.UpdateScene(Exploration!.Plan);
                };
                // Ordinary dialogue blocks exploration. Redraw the retained current scene, not
                // the initial pose or a screenshot; the display removes its own overlay.
                Display.BackgroundRestoreRequested += () =>
                {
                    if (Failure is null && !exited) map.UpdateScene(Exploration!.Plan);
                };
                Dialogue.Initialize(Display, npc.EventsIndex, npc.DialogueDirectory, Palette(map.Data), map.Data, language);
                var entries = Dialogue.Session!.Entries.Where(e => e.OwnerType == "eventObject" && e.OwnerId == 60 && e.Slot == "wTriggerScript").ToArray();
                if (entries.Length != 1) throw new InvalidDataException("Independent dialogue requires one owner60 trigger entry");
                Interaction = new NpcInteractionSession(checkedProfile.Rules, checkedProfile.Leader, entries[0]);
                Display.UpdateCompleted += CompleteTrigger; // after adapter receipt/restoration
                RenderingServer.FramePostDraw += StartAfterSceneDraw;
                rendererSubscribed = true;
                GD.Print($"PAL_M3_DIALOGUE_ACCEPTANCE:scene4/map10; owner60/0188; language={language}; not normal scene story/free exploration");
            }
            GD.Print($"PAL_MAP_LOADED:{root}/Scenes/Scene.tscn");
            if (firstBattle is { Closure: not null, Resources: not null } real)
            {
                // #53 real host: the verified closure opens @09C3 through the production
                // dialogue/battle executor; the search-trigger transition rides the same
                // draw boundary as the story host.
                Display = new PalDialogueDisplay();
                Display.Failed += DisplayFailed;
                AddChild(Display);
                Dialogue = new PalTriggerDialogue(); AddChild(Dialogue);
                Display.ActivePaletteChanged += colors =>
                {
                    if (Failure is not null || exited) return;
                    checkedProfile.SetPalette(map.Data, colors);
                    map.UpdateScene(Exploration!.Plan);
                };
                Display.BackgroundRestoreRequested += () =>
                {
                    if (Failure is null && !exited) map.UpdateScene(Exploration!.Plan);
                };
                Dialogue.PaletteFromScene = Palette(map.Data);
                // The real call mutates one shared dynamic world (script verbs) while the battle
                // half runs the same verified content; the world is bound before initialization.
                // The @09C3 performance also mutates global objects outside the static bundle;
                // the closure-bound table extends the world's object set (ids stay unique).
                var staticIds = new System.Collections.Generic.HashSet<int>(Battle!.Rules.Objects.Select(o => (int)o.Id));
                var extendedRules = Battle.Rules.WithObjects(Battle.Rules.Objects
                    .Concat(real.Resources.GlobalObjects.Where(o => !staticIds.Contains(o.Id))));
                // Seed the validated battle declarations once; party order is explicit and
                // independent of how many role records the content declares.
                roleLiveState = new RoleLiveState(real.Resources.BattleContent.InitialRoles.Values.ToArray());
                var world = DynamicEventWorld.ForHost(extendedRules, Battle.Current.Leader,
                    map.Data.SceneId, map.Data.WScriptOnEnter, map.Data.WScriptOnTeleport,
                    roleLiveState, real.Resources.BattleContent.Players.Select(player => player.RoleId).ToArray());
                Dialogue.FirstBattleWorld = world;
                firstBattleWorld = world;
                firstBattleRandom = new GodotBattleRandom(initialFirstBattleSeed ?? selected.BattleSeed);
                Dialogue.PreviewSessionFactory = () =>
                {
                    var fork = firstBattleRandom.Fork();
                    try
                    {
                        return (new TriggerScriptSession(real.Resources.Events,
                            language == "zh-Hans" ? real.Resources.Chinese : real.Resources.English,
                            real.Resources.BattleContent, fork, world.CreateIsolatedCopy(),
                            Dialogue.Session!.Context.Color), fork);
                    }
                    catch { fork.Dispose(); throw; }
                };
                Dialogue.Initialize(Display, real.Resources, language, firstBattleRandom, map.Data);
                if (!Battle.InitializeTrigger(Dialogue.Session!))
                    throw new InvalidOperationException("First battle trigger could not initialize its production world before exploration.");
                // The verified closure reuses the production story-battle machinery: the facade
                // carries the same checked exploration, scene, palette and settlement words, so
                // team18's real battle, hooks and settlement run through the identical host.
                var res = real.Resources;
                var triggerEntry = Dialogue.Session!.Entries.Single(e =>
                    e is { OwnerType: "eventObject", OwnerId: 28, Slot: "wTriggerScript", Kind: "trigger" } && e.SourceAddress == real.Closure.TriggerEntryAddress);
                story = new CheckedStoryProfile(real.StaticResources, real.StaticProfile, map.Data.BundleSha256,
                    "first-mandatory-battle verified closure", triggerEntry, res.Events,
                    language == "zh-Hans" ? res.Chinese : res.English, res.BattleContent, real.Closure.VerifiedRequirements,
                    checkedProfile, res.Font, res.Prompts.ToArray(),
                    res.Portraits.ToDictionary(x => x.Key, x => x.Value), Palette(map.Data),
                    res.ResourceSha256.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal),
                    mapScenePackedScene(real.StaticResources), language == "zh-Hans" ? res.ChineseWords : res.EnglishWords);
                storyVisual = real.Visual;
                Display.UpdateCompleted += CompleteFirstBattleTrigger;
                RenderingServer.FramePostDraw += StartFirstBattleAfterSceneDraw;
                firstBattleRendererSubscribed = true;
                GD.Print($"PAL_M4_FIRST_BATTLE_REAL:scene2/map12; owner28/09C3; language={language}; closure verified; teams=[{string.Join(",", real.Closure.VerifiedBattleTeamIds)}]");
            }
            else if (firstBattle is not null) GD.Print($"PAL_M4_FIRST_BATTLE_CONTEXT:scene2/map12; owner28/09C3; language={language}; trigger closure unavailable");
            if (staticAcceptance) GD.Print("PAL_M3_STATIC_ACCEPTANCE:scene4/map10; events are not scheduled");
            GD.Print($"PAL_GAME_READY:{Pal.Core.Startup.Status}");
        }
        catch (Exception e)
        {
            Fail(StartupDiagnostics.Format(selectedProfilePath, scenePath, staticProfilePath, e.Message));
        }
    }

    private static ExplorationMenuKey DirectionMenuKey(int direction) => direction switch
    {
        0 => ExplorationMenuKey.Down,
        1 => ExplorationMenuKey.Left,
        2 => ExplorationMenuKey.Up,
        _ => ExplorationMenuKey.Right,
    };

    private static Color[] Palette(PalSceneData scene) => Enumerable.Range(0, 256).Select(i =>
        new Color(scene.Palette.Rgb[i * 3] / 255f, scene.Palette.Rgb[i * 3 + 1] / 255f, scene.Palette.Rgb[i * 3 + 2] / 255f)).ToArray();

    private static void ValidateDialogueScene(PalSceneData scene, PalNpcInteractionProfile profile)
    {
        var owner = scene.Objects.Single(o => o.Id == 60);
        if (scene.BundleSha256 != profile.BundleSha256 || scene.WScriptOnEnter != 0 || scene.WScriptOnTeleport != 0
            || owner.WTriggerScript != 0x0188 || owner.WTriggerMode != 3 || owner.WAutoScript != 0)
            throw new InvalidDataException("Independent dialogue scene/source/owner60 trigger identity mismatch");
        if (scene.Palette is not { PaletteId: 0, SelectedLighting: "day", Rgb.Length: 768 })
            throw new InvalidDataException("Independent dialogue requires native palette0/day with 256 colors");
    }

    private static void ValidateStaticAcceptance(PalSceneData scene, CheckedExplorationProfile profile)
    {
        if (scene.SceneId != 4 || scene.MapId != 10 || scene.Objects.Count != 32)
            throw new InvalidDataException("M3 static acceptance requires scene 4 / map 10 and all 32 original objects");
        var owner = scene.Objects.SingleOrDefault(item => item.Id == 60);
        if (owner == null || owner.X != 1216 || owner.Y != 1344 || owner.WSpriteNum != 207
            || owner.SState != 2 || owner.SVanishTime != 0)
            throw new InvalidDataException("M3 static acceptance owner 60 identity/initial state mismatch");
        if (profile.Leader.X != 1232 || profile.Leader.Y != 1336 || profile.Leader.Direction != 0
            || profile.Leader.Layer != 0 || profile.Leader.StepPhase != 0)
            throw new InvalidDataException("M3 static acceptance profile must use start (1232,1336), South0, layer0/phase0, role0");
    }

    public override void _Input(InputEvent @event)
    {
        if (Failure is not null || exited || Exploration is null || @event is not InputEventKey key) return;
        try
        {
            for (int i = 0; i < DirectionKeys.Length; i++)
            {
                if (key.Keycode != DirectionKeys[i] || !key.IsAction(DirectionActions[i / 2])) continue;
                if (storyRequest is not null)
                {
                    // The active story battle owns direction keys: horizontal input cycles the
                    // enemy-slot target highlight only and never bleeds into exploration.
                    if (key.Pressed && !key.Echo && StoryBattleMode == StoryBattlePresentation.TargetMenu
                        && i / 2 is 1 or 3)
                    {
                        storyCycle = i / 2 == 3 ? 1 : -1;
                        GetViewport().SetInputAsHandled();
                    }
                    // input.c: 'd' is kKeyDefend (uibattle.c 1166-1171: the main move menu
                    // commits the defend action directly; no target menu exists for it). The
                    // exploration east alias carries the same physical key, so the battle
                    // consumes it inside this branch — the standalone blocks below are
                    // unreachable for a direction-alias key.
                    else if (key.Pressed && !key.Echo && key.Keycode == Key.D
                        && StoryBattleMode == StoryBattlePresentation.AttackMenu)
                    {
                        storyDefend = true;
                        GetViewport().SetInputAsHandled();
                    }
                    return;
                }
                if (menu is { Mode: ExplorationMenuMode.ItemScript })
                {
                    // #215: the pending item script owns the frame (play.c blocks inside
                    // PAL_RunTriggerScript): direction presses never reach the frozen world and
                    // never queue menu keys; releases keep held-key state truthful for the resume.
                    if (key.Pressed) GetViewport().SetInputAsHandled();
                    else if (dynamicSession is { } dynamicRelease) dynamicRelease.Release((ExplorationKey)i);
                    else if (battle is { } battleRelease) battleRelease.Release((ExplorationKey)i);
                    else if (Interaction is { } interactionRelease) interactionRelease.Release((ExplorationKey)i);
                    else input!.Release((ExplorationKey)i);
                    return;
                }
                if (menu is { Mode: ExplorationMenuMode.MagicOnUse or ExplorationMenuMode.MagicOnSuccess })
                {
                    // #216: the pending magic cast's on-use/on-success script owns the frame
                    // (uigame.c blocks inside PAL_RunTriggerScript): direction presses never
                    // reach the frozen world and never queue menu keys; releases keep held-key
                    // state truthful for the resume.
                    if (key.Pressed) GetViewport().SetInputAsHandled();
                    else if (dynamicSession is { } dynamicRelease) dynamicRelease.Release((ExplorationKey)i);
                    else if (battle is { } battleRelease) battleRelease.Release((ExplorationKey)i);
                    else if (Interaction is { } interactionRelease) interactionRelease.Release((ExplorationKey)i);
                    else input!.Release((ExplorationKey)i);
                    return;
                }
                if (menu is { Mode: not ExplorationMenuMode.Closed })
                {
                    // The open menu owns direction presses (uigame.c menu loops); releases keep
                    // the exploration held-key state truthful for the post-close resume.
                    if (key.Pressed)
                    {
                        if (!key.Echo) pendingMenuKeys.Add(DirectionMenuKey(i / 2));
                        GetViewport().SetInputAsHandled();
                    }
                    else if (dynamicSession is { } dynamicRelease) dynamicRelease.Release((ExplorationKey)i);
                    else if (battle is { } battleRelease) battleRelease.Release((ExplorationKey)i);
                    else if (Interaction is { } interactionRelease) interactionRelease.Release((ExplorationKey)i);
                    else input!.Release((ExplorationKey)i);
                    return;
                }
                if (shop is { Mode: not ExplorationShopMode.Closed })
                {
                    // #217: the open shop owns direction presses (uigame.c menu loops); releases
                    // keep the exploration held-key state truthful for the post-close resume.
                    if (key.Pressed)
                    {
                        if (!key.Echo) pendingShopKeys.Add(DirectionMenuKey(i / 2));
                        GetViewport().SetInputAsHandled();
                    }
                    else if (dynamicSession is { } dynamicRelease) dynamicRelease.Release((ExplorationKey)i);
                    else if (battle is { } battleRelease) battleRelease.Release((ExplorationKey)i);
                    else if (Interaction is { } interactionRelease) interactionRelease.Release((ExplorationKey)i);
                    else input!.Release((ExplorationKey)i);
                    return;
                }
                if (dynamicSession is { } dynamic)
                {
                    if (key.Pressed) dynamic.Press((ExplorationKey)i, key.Echo);
                    else dynamic.Release((ExplorationKey)i);
                }
                else if (battle is { } firstBattle)
                {
                    if (key.Pressed) firstBattle.Press((ExplorationKey)i, key.Echo);
                    else firstBattle.Release((ExplorationKey)i);
                }
                else if (Interaction is { } interaction)
                {
                    if (key.Pressed) interaction.Press((ExplorationKey)i, key.Echo);
                    else interaction.Release((ExplorationKey)i);
                }
                else if (key.Pressed) input!.Press((ExplorationKey)i, key.Echo);
                else input!.Release((ExplorationKey)i);
                return;
            }
            // Windows reference EnableKeyRepeat=false; no held search repeater or fresh-press gate.
            if (key.Pressed && !key.Echo
                && (key.Keycode is Key.Enter or Key.Space or Key.KpEnter || key.Keycode == Key.Ctrl && key.Location != KeyLocation.Right))
            {
                if (menu is { Mode: ExplorationMenuMode.ItemScript })
                {
                    // #215: the item script's display owns Search while the run is pending; the
                    // press propagates to it untouched and never queues a menu confirm or reaches
                    // the frozen exploration search.
                }
                else if (menu is { Mode: ExplorationMenuMode.MagicOnUse or ExplorationMenuMode.MagicOnSuccess })
                {
                    // #216: the magic cast's script display owns Search while a run is pending;
                    // the press propagates to it untouched and never queues a menu confirm or
                    // reaches the frozen exploration search.
                }
                else if (menu is { Mode: not ExplorationMenuMode.Closed })
                {
                    // The open menu consumes Search as its confirm; the press never reaches the
                    // exploration search, so closing cannot leak into an NPC trigger (uigame.c).
                    pendingMenuKeys.Add(ExplorationMenuKey.Confirm);
                    GetViewport().SetInputAsHandled();
                }
                else if (shop is { Mode: not ExplorationShopMode.Closed })
                {
                    // #217: the open shop consumes Search as its confirm (uigame.c); the press
                    // never reaches the frozen exploration search.
                    pendingShopKeys.Add(ExplorationMenuKey.Confirm);
                    GetViewport().SetInputAsHandled();
                }
                else if (storyRequest is not null)
                {
                    // The paused role hook's dialogue display consumes input itself; a menu
                    // confirm must not queue behind the script (fight.c 768-880 pause).
                    if (StoryBattleMode != StoryBattlePresentation.HookDialogue) battleConfirm = true;
                }
                else if (dynamicSession is { Phase: DynamicEventPhase.Exploring } dynamic) dynamic.PressSearch();
                else if (battle is not null) battle.PressSearch();
                else if (Interaction is { Phase: not NpcInteractionPhase.Trigger } npc) npc.PressSearch();
                else return;
                GetViewport().SetInputAsHandled();
            }
            // input.c: 'q' is kKeyFlee. Only the story battle's action menu consumes it; the
            // parked display never sees battle-frame input.
            if (key.Pressed && !key.Echo && key.Keycode == Key.Q && storyRequest is not null
                && StoryBattleMode == StoryBattlePresentation.AttackMenu)
            {
                storyFlee = true;
                GetViewport().SetInputAsHandled();
            }
            // input.c 66-70: the kKeyMenu family (Escape/Insert/Alt/KP_0) cancels the active
            // battle menu. Only the selecting menus consume it; the paused role-hook dialogue
            // display owns its own input and exploration never sees battle-frame keys.
            if (key.Pressed && !key.Echo && storyRequest is not null
                && key.Keycode is Key.Escape or Key.Insert or Key.Alt or Key.Kp0
                && StoryBattleMode is StoryBattlePresentation.AttackMenu or StoryBattlePresentation.TargetMenu)
            {
                storyCancel = true;
                GetViewport().SetInputAsHandled();
            }
            // input.c 66-70 kKeyMenu (Escape/Insert/Alt/KP_0): play.c 551 opens the in-game menu
            // from the clean exploring state after that frame's scene update; the press applies
            // at the real update boundary and the guard re-checks the mode owner there.
            if (key.Pressed && !key.Echo && key.Keycode is Key.Escape or Key.Insert or Key.Alt or Key.Kp0
                && shop is { Mode: not ExplorationShopMode.Closed })
            {
                // #217: the open shop consumes kKeyMenu as its cancel (uigame.c's menu loops);
                // the press never queues an exploration menu open behind the parked trigger.
                pendingShopKeys.Add(ExplorationMenuKey.Cancel);
                GetViewport().SetInputAsHandled();
            }
            if (key.Pressed && !key.Echo && key.Keycode is Key.Escape or Key.Insert or Key.Alt or Key.Kp0
                && storyRequest is null && menu is not null && shop is not { Mode: not ExplorationShopMode.Closed })
            {
                if (menu.Mode is not (ExplorationMenuMode.Closed or ExplorationMenuMode.ItemScript
                    or ExplorationMenuMode.MagicOnUse or ExplorationMenuMode.MagicOnSuccess))
                {
                    pendingMenuKeys.Add(ExplorationMenuKey.Cancel);
                    GetViewport().SetInputAsHandled();
                }
                else if (menu.Mode == ExplorationMenuMode.Closed)
                {
                    menuOpenPressed = true;
                    GetViewport().SetInputAsHandled();
                }
            }
        }
        catch (Exception error) { Fail($"Exploration input failed: {error.Message}"); }
    }

    public override void _Process(double delta)
    {
        if (Failure is not null || exited || Exploration is null || mapScene is null) return;
        try
        {
            ResumeStoryDisplay();
            ObserveStoryLostTerminal();
            if (StoryLostTerminal)
            {
                // The failure terminal only advances its presentation (red fade); no event,
                // battle or exploration business runs and no reward path can trigger.
                AdvanceStoryRedFade(unchecked((uint)Time.GetTicksMsec()));
                UpdateCompleted?.Invoke();
                return;
            }
            if (ProcessStoryBattle()) { UpdateCompleted?.Invoke(); return; }
            if (dynamicSession is not null) { ProcessDynamicScene(); UpdateCompleted?.Invoke(); return; }
            // The headless dummy renderer has no presented frame. Preserve the command/redraw
            // submission boundary across actual callbacks; graphical runs wait for FramePostDraw.
            if (awaitingSceneDraw && DisplayServer.GetName() == "headless") StartAfterSceneDraw();
            if (awaitingFirstBattleDraw && DisplayServer.GetName() == "headless") StartFirstBattleAfterSceneDraw();
            if (Failure is not null) return;
            // play.c PAL_GameLoop: the frame's update runs first, then kKeyMenu opens the menu;
            // while the menu is open the world stays frozen (PAL_InGameMenu blocks the loop). The
            // parked shop request freezes the same way (script.c blocks inside PAL_BuyMenu).
            bool changed = shop is { Mode: not ExplorationShopMode.Closed } || menu is { Mode: not ExplorationMenuMode.Closed } ? false
                : battle?.Update(unchecked((uint)Time.GetTicksMsec()))
                ?? Interaction?.Update(unchecked((uint)Time.GetTicksMsec()))
                ?? input!.Update(unchecked((uint)Time.GetTicksMsec()));
            // World-wired first-battle call: the parked scene waits and counted movements
            // advance one step per committed update tick (the same per-tick ownership the
            // dynamic host uses); the map only redraws when the exploration really changed.
            if (firstBattleWorld is { } world && Dialogue?.Session is { Phase: Pal.Core.TriggerScriptPhase.WaitingForSceneWait or Pal.Core.TriggerScriptPhase.WaitingForMovement } trigger)
            {
                // One world step per 100ms due tick (the dynamic host's cadence), independent
                // of exploration movement commits; the acknowledged park may advance into a
                // display, which is bound immediately.
                uint now = unchecked((uint)Time.GetTicksMsec());
                if (unchecked((int)(now - firstBattleWorldTick)) >= 100)
                {
                    firstBattleWorldTick = now;
                    world.AdvanceTriggerTick(trigger);
                    Dialogue!.BindPendingDisplays();
                    CompleteFirstBattleTrigger();
                }
            }
            ApplyShopInput();
            if (Failure is not null) return;
            ApplyMenuInput();
            if (Failure is not null) return;
            if (shop is { Mode: not ExplorationShopMode.Closed } || menu is { Mode: not ExplorationMenuMode.Closed })
            { UpdateCompleted?.Invoke(); return; }
            if (changed) mapScene.UpdateScene(Exploration.Plan);
            // Presentation transitions (redraw/fadeScene/fadeOut) in the world-wired call:
            // redraw the retained scene, then receipt after the delay elapsed.
            CompleteFirstBattleTransition();
            if (activeTrigger is null && Interaction?.PendingTrigger is { } request)
            {
                activeTrigger = request;
                awaitingSceneDraw = true;
            }
            if (firstBattleActiveTrigger is null && Battle?.PendingTrigger is { } battleRequest)
            {
                firstBattleActiveTrigger = battleRequest;
                awaitingFirstBattleDraw = true;
            }
            // Exploration/dialogue observers consume every real Main callback; first-battle
            // observers only consume committed updates, leaving no-change/rejection unpublished.
            if (battle is null || changed) UpdateCompleted?.Invoke();
        }
        catch (Exception e) { Fail($"Exploration update failed: {e.Message}"); }
    }

    private void StartAfterSceneDraw()
    {
        if (Failure is not null || exited || !awaitingSceneDraw || activeTrigger is not { } request) return;
        awaitingSceneDraw = false;
        try
        {
            if (story is not null) Dialogue!.Start(request.FileId, request.EntryId, request.Caller, request.Id);
            else Dialogue!.Start(request.FileId, request.EntryId, request.Caller);
            if (story is not null && !Interaction!.BindTrigger(request.Id, Dialogue.Session!))
                throw new InvalidOperationException("Selected trigger call could not bind its search request.");
            CompleteTrigger(); // Also handles synchronous, no-wait completion.
        }
        catch (Exception e) { Fail($"NPC trigger failed: {e.Message}"); }
    }

    /// <summary>Starts the verified @09C3 call after the committed search update's draw.</summary>
    private void StartFirstBattleAfterSceneDraw()
    {
        if (Failure is not null || exited || !awaitingFirstBattleDraw || Battle?.PendingTrigger is not { } request) return;
        awaitingFirstBattleDraw = false;
        try
        {
            var entry = Dialogue!.Session!.Entries.Single(e =>
                e is { OwnerType: "eventObject", OwnerId: 28, Slot: "wTriggerScript", Kind: "trigger" } && e.SourceAddress == request.EntryAddress);
            Guid requestId = Battle.PendingRequestId;
            if (requestId == Guid.Empty)
                throw new InvalidOperationException("First battle search request has no origin.");
            Dialogue.Start(entry.FileId, entry.Id, request.OwnerId, requestId);
            if (!Battle.BindTrigger(requestId, Dialogue.Session!))
                throw new InvalidOperationException("First battle trigger call could not bind its search request.");
            CompleteFirstBattleTrigger(); // also handles synchronous, no-display completion
        }
        catch (Exception e) { Fail($"First battle trigger failed: {e.Message}"); }
    }

    private uint firstBattleTransitionDeadline;
    private uint firstBattleWorldTick;

    private void CompleteFirstBattleTransition()
    {
        if (firstBattleWorld is null || Dialogue?.Session?.PendingTransition is not { } transition)
        {
            firstBattleTransitionDeadline = 0;
            return;
        }
        uint now = unchecked((uint)Time.GetTicksMsec());
        if (firstBattleTransitionDeadline == 0)
        {
            int delay = transition.Kind == "redraw" ? Math.Max(60, transition.Parameter) : 60;
            firstBattleTransitionDeadline = unchecked(now + (uint)delay);
            mapScene!.UpdateScene(Exploration!.Plan);
            return;
        }
        if (unchecked((int)(now - firstBattleTransitionDeadline)) < 0) return;
        firstBattleTransitionDeadline = 0;
        _ = Dialogue.Session.AcknowledgeTransition(transition.Id);
        // The call advanced past its transition; a newly parked display must be bound.
        Dialogue.BindPendingDisplays();
        CompleteFirstBattleTrigger();
    }

    private static PackedScene mapScenePackedScene(string staticResources) =>
        (PackedScene)ResourceLoader.Load(staticResources + "/Scenes/Scene.tscn", cacheMode: ResourceLoader.CacheMode.IgnoreDeep);

    private void CompleteFirstBattleTrigger()
    {
        if (Failure is not null || exited || firstBattleActiveTrigger is null
            || Dialogue?.Session is not { Phase: TriggerScriptPhase.Completed } session) return;
        // Core owns the call identity, current scene publication and return deadline. A
        // refused receipt must leave both the facade request and Main's active call intact.
        if (!Battle!.CompleteTrigger(Battle.PendingRequestId, session, unchecked((uint)Time.GetTicksMsec())))
            throw new InvalidOperationException("First battle trigger completion does not match the active search request.");
        firstBattleActiveTrigger = null;
        mapScene!.UpdateScene(Battle.Current.Plan);
    }

    private void CompleteTrigger()
    {
        if (Failure is not null || exited || activeTrigger is not { } request || Dialogue?.Session is not { Phase: TriggerScriptPhase.Completed } session) return;
        // This callback follows the adapter's receipt at the same actual display update, not
        // a later Main observation frame. Palette/background consumers have already completed.
        if (!Interaction!.CompleteTrigger(request.Id, session, unchecked((uint)Time.GetTicksMsec())))
            throw new InvalidOperationException("NPC trigger completion does not match the active search request");
        activeTrigger = null;
    }

    private void DisplayFailed(Exception error) => Fail($"Dialogue update failed: {error.Message}");

    /// <summary>Observes the failure branch's formal state: starts the red fade when the
    /// 0x004F transition lands, and enters the 0x004E failure terminal exactly once.</summary>
    private void ObserveStoryLostTerminal()
    {
        if (story is not null && storyRedStep < 0 && Dialogue?.Session?.FadedToRed == true)
        {
            storyRedStep = 0;
            storyRedStepAt = presentationNow;
        }
        if (StoryLostTerminal || Dialogue?.Session?.PendingLostReload is not { } request) return;
        // TODO(#51): implement the real script.c 0x004E save-slot reload (PAL_FadeOut(1) then
        // PAL_ReloadInNextTick): no save system exists yet, so this run stays in the lost
        // terminal state (red screen, no business advancement) until the window closes.
        StoryLostTerminal = true;
        StoryLostReloadDiagnostics++;
        string team = request.EnemyTeamId is { } enemyTeam ? enemyTeam.ToString(System.Globalization.CultureInfo.InvariantCulture) : "none";
        string music = Dialogue.Session.Music is { } state ? state.MusicId.ToString(System.Globalization.CultureInfo.InvariantCulture) : "unchanged";
        GD.Print($"PAL_LOST_RELOAD_TODO:{request.Position}; team={team}; boss={request.IsBoss}; caller={Dialogue.Session.Caller}; music={music}; "
            + "save-slot reload not implemented; lost terminal until window close");
    }

    protected void Fail(string message)
    {
        if (Failure is not null || exited) return;
        Failure = message;
        awaitingSceneDraw = false;
        ProcessMode = ProcessModeEnum.Disabled;
        SetProcess(false);
        SetProcessInput(false);
        DisconnectRuntime();
        ReleaseStory();
        ReleaseDynamic();
        ReleaseFirstBattleRandom();
        Failed?.Invoke(message);
    }

    private void DisconnectRuntime()
    {
        if (rendererSubscribed)
        {
            RenderingServer.FramePostDraw -= StartAfterSceneDraw;
            rendererSubscribed = false;
        }
        if (Display is not null)
        {
            Display.UpdateCompleted -= CompleteTrigger;
            Display.Failed -= DisplayFailed;
        }
    }

    private void ReleaseFirstBattleRandom()
    {
        if (Dialogue is not null) Dialogue.PreviewSessionFactory = null;
        firstBattleRandom?.Dispose(); firstBattleRandom = null;
    }

    public override void _ExitTree()
    {
        exited = true;
        DisconnectRuntime();
        ReleaseStory();
        ReleaseDynamic();
        ReleaseFirstBattleRandom();
        activeTrigger = null; firstBattleActiveTrigger = null; Interaction = null; input = null; battle = null; mapScene = null;
        if (firstBattleRendererSubscribed)
        {
            RenderingServer.FramePostDraw -= StartFirstBattleAfterSceneDraw;
            firstBattleRendererSubscribed = false;
        }
        Display = null; Dialogue = null; SelectedLanguage = null;
        firstBattleWorld = null; roleLiveState = null;
    }
}
