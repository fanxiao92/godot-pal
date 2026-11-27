using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Pal.Core;
using Pal.Game.Dialogue;
using Pal.Game.Profiles;
using Pal.Game.Resources;
using Pal.Core.State;
using Pal.Core.Battle;
using Pal.Core.Events;
using Pal.Core.Menu;
using Pal.Core.Save;
using Pal.Game.Battle;
using Pal.Game.UI;

namespace Pal.Game;

public partial class Main
{
    private CheckedStoryProfile? story;
    private CheckedBattleVisualProfile? storyVisual;
    private CheckedBattleVisualTeam? storyTeam;
    private GodotBattleRandom? storyRandom;
    private PalStoryBattleView? storyView;
    // #214: the story host's shared equipment/poison/inventory owners. The menu, the status page
    // and (with an authored equip context) the next trigger battle consume these same instances.
    private BattleEquipmentState? storyEquipment;
    private PartyPoisonState? storyPoisons;
    private InventoryLiveState? storyInventory;
    // #217: the shared wallet seeded once from the authored cash word ($.cash); the shop and
    // the trigger executor's cash commands consume the same owner.
    private CashLiveState? storyCash;
    // #224 (M6-C leaf 4; parent #148): the story host's save chain — the #222 gate over the
    // shared live owners, the #223 codec and the controlled five-slot user:// storage adapter.
    // The gate's request identity binds to one committed production update boundary, and the
    // whole chain for a confirmed slot runs inside that same boundary.
    private DynamicEventWorld? storyWorld;
    private ExplorationSaveGate? storySaveGate;
    private PlayerSaveStorage? storySaveStorage;
    private int storySaveCount;
    /// <summary>The session's current recovery slot (#148 Decision 27): null in an independent
    /// context; only a completed save (#224) or a completed read (#227 — always this session's
    /// actually selected slot, never the payload's carried one) switches it. Browsing,
    /// rejection, write failure, read failure and cancellation never do.</summary>
    public int? CurrentRecoverySlot { get; private set; }
    /// <summary>The gameplay content identity this session's saves bind to (#221 matrix D1):
    /// the scene bundle hash folded with every verified resource hash, independent of
    /// run-specific resource paths so the same content in another directory keeps its identity
    /// and a different language package never misjudges gameplay identity (#148 Decision 15).</summary>
    public string? SaveContentIdentity { get; private set; }
    /// <summary>The formal chain's controlled storage adapter (tests read slots back through
    /// this exact production instance; no second save chain exists anywhere).</summary>
    public PlayerSaveStorage? SaveStorage => storySaveStorage;
    public Func<PalStoryBattleView> StoryBattleViewFactory { get; set; } = () => new PalStoryBattleView();
    private TriggerBattleRequest? storyRequest;
    private bool battleConfirm;
    // input.c 66-70 kKeyMenu (Escape/Insert/Alt/KP_0): the pending cancel of the active battle menu.
    private bool storyCancel;
    // Enemy-slot highlight while a member picks a target; reference selection has no
    // previous-target memory (fixed initial -1, UI update disabled).
    private int storyTargetSlot;
    private int storyCycle;
    private ClassicBattleAction? storyLastPresentedAction;
    // #51 boss flee: the pending Q press and the newest presented flee attempt.
    private bool storyFlee;
    // input.c 'd' kKeyDefend: the pending D press in the action menu.
    private bool storyDefend;
    // #215 play.c PAL_GameUseItem: the item script run this host started (its request id and the
    // checked entry address its owned receipt must return to) while the menu parks in ItemScript.
    private Guid itemUseId;
    private int itemUseEntryAddress;
    // #216 uigame.c PAL_InGameMagicMenu 807-841: the magic cast's current script run (the cast's
    // request id and the checked entry address its owned receipt must return to) while the menu
    // parks in MagicOnUse/MagicOnSuccess; the id resets between the on-use and on-success runs.
    private Guid magicRunId;
    private int magicRunEntryAddress;
    private ClassicBattleFleeAttempt? storyLastFleeAttempt;
    private Vector2? fleeOrigin;
    private int fleePartyIndex = -1;
    // battle.c 1458-1528: the successful escape's all-member walk-off feedback (vs. the single
    // failed escaper's steps); set while the escape feedback presents.
    private bool fleeEscaped;
    // palette.c PAL_FadeToRed: 32 steps of 75ms; -1 = not started.
    private int storyRedStep = -1;
    private uint storyRedStepAt;
    /// <summary>Current red-fade step (-1 none, 0..31 in progress, 32 converged).</summary>
    public int StoryRedFadeStep => storyRedStep;
    /// <summary>The red-faded world palette the map currently shows; null outside the fade.</summary>
    public Color[]? StoryRedPalette => storyRedStep < 0 ? null : RedFadePalette(story!.Palette, storyRedStep);
    private bool resumeStoryDisplay;
    private StoryBattlePoseClock poseClock;
    private uint presentationNow;
    private StoryEnemyAttackPresentation? enemyPresentation;
    private IStoryPlayerStrikePresentation? playerPresentation;
    private int effectEnemyHeight;
    private StoryEnemyIdleState[]? enemyIdle;
    private List<string>? settlementLines;
    private int settlementStage;
    private uint settlementShownAt;
    private bool settlementPresented;
    private StoryBattlePoseClock outerClock;
    private bool outerDue;
    private readonly StoryBattleNumbers storyNumbers = new();
    private bool feedbackBorn;
    private int feedbackBeforeHp;
    // The presented action's enemies-before-HP snapshot (all-target strikes read it per target).
    private int[] feedbackEnemiesBefore = [];
    public StoryEnemyAttackSnapshot? StoryEnemyPresentation => storyView?.EnemyPresentation;
    public StoryBattleDrawSnapshot? StoryDrawSnapshot => storyView?.DrawSnapshot;
    public uint StoryPresentationTicks => presentationNow;
    public StoryBattlePoseSnapshot? StoryBattlePose => StoryBattleMode == StoryBattlePresentation.Feedback
        ? new(presentationNow, poseClock.PoseStartedAt, poseClock.Pose) : null;
    /// <summary>Per-member sum of the live role-hook boost registers; empty while none applied.</summary>
    public IReadOnlyList<int> StoryBattleBoosts => storyView?.DrawSnapshot.PartyBoosts ?? [];
    public StoryBattlePresentation? StoryBattleMode { get; private set; }
    public ClassicBattleLifecycleSession? StoryBattle => storyRequest?.Lifecycle;
    /// <summary>The current victory-settlement line, or null outside the Settlement presentation.</summary>
    public string? StorySettlementLine => StoryBattleMode == StoryBattlePresentation.Settlement
        ? storyView?.SettlementLine : null;
    /// <summary>Story ledger of the granted victory totals (gpGlobals dwCash, battle.c 1054); one
    /// consumption per owned Won receipt, never re-granted by a repeat acknowledgement.</summary>
    public int StoryCashGranted { get; private set; }
    public int StoryExperienceGranted { get; private set; }
    public int? StoryDisplayedPlayerHp => storyView?.DisplayedPlayerHp;
    public int? StoryDisplayedEnemyHp => storyView?.DisplayedEnemyHp;
    public bool StoryResultVisible => storyView?.ResultVisible ?? false;

    private void InitializeStory(GameSceneConfiguration selected)
    {
        // All capability, hash and native validation completes before publishing children.
        var checkedStory = NativeStoryProfile.Load(selected.StoryProfile!, selected.Language);
        var checkedVisual = NativeBattleVisualProfile.Load(selected.VisualProfile!, checkedStory);
        checkedVisual.RequireReferencePresentation();
        checkedVisual.RequireNumberPresentation();
        PalStoryBattleView.ValidateUi(checkedStory);
        var map = (PalMapScene)checkedStory.Scene.Instantiate();
        PalDialogueDisplay? display = null;
        PalTriggerDialogue? dialogue = null;
        try
        {
            checkedStory.Exploration.SetPalette(map.Data, checkedStory.Palette.ToArray());
            map.InitializeScene(checkedStory.Exploration.Plan, checkedStory.Exploration.TextureFor);
            storyRandom = new GodotBattleRandom(selected.BattleSeed);
            display = new PalDialogueDisplay();
            dialogue = new PalTriggerDialogue();
            // #224: the live role owner carries the complete six-row table the frozen v1
            // contract saves (reference rgPlayerRoles; #221 D2) — authored rows plus empty
            // records for the unused role ids. Party, menu and battle consumers stay
            // authored-roles-driven; the padded rows only complete the save's role domain.
            roleLiveState = new RoleLiveState(Enumerable.Range(0, PlayerSaveContract.RoleCount).Select(roleId =>
                checkedStory.BattleContent.InitialRoles.TryGetValue(roleId, out var authored) ? authored : EmptyRole(roleId)).ToArray());
            storyEquipment = BattleEquipmentState.Initialize(checkedStory.BattleContent);
            storyInventory = new InventoryLiveState(checkedStory.BattleContent.InitialInventory);
            int[] storyPartyRoleIds = checkedStory.BattleContent.Players.Select(player => player.RoleId).ToArray();
            storyPoisons = new PartyPoisonState(storyPartyRoleIds,
                storyPartyRoleIds.Select(_ => (IReadOnlyList<PoisonSlot>)Enumerable.Range(0, PartyPoisonState.Capacity)
                    .Select(_ => new PoisonSlot(0, 0)).ToArray()).ToArray(),
                new Dictionary<int, int>());
            // #214: with an authored equip context the trigger battle consumes the current
            // equipment owner (#187); scenarios without one keep the frozen-author construction.
            bool equipmentPage = checkedStory.BattleContent.EquipMenu.Count > 0;
            // #215: with an authored use context the menu gains the items page; its on-use
            // entries are checked up front with the story profile.
            bool itemsPage = checkedStory.BattleContent.UseMenu.Count > 0;
            // #216: with an authored magic context the menu gains the magic page; its on-use and
            // on-success entries are checked up front with the story profile.
            bool magicPage = checkedStory.BattleContent.MagicMenu.Count > 0;
            // #217: with an authored shop context the host seeds the shared wallet once (the
            // explicit $.cash word; #131's checked immutable initial value) and binds it with the
            // bag to the trigger executor, so the shop commands park on the same shared owners.
            // #224: a context without the authored seed keeps a zero wallet bound to the save
            // chain only — its money cannot change (no shop/trigger binding exists), so the v1
            // cash domain records exactly the authored-or-zero owner state.
            bool shopContext = checkedStory.BattleContent.ShopObjects.Count > 0;
            uint cashSeed = 0;
            if (shopContext)
                cashSeed = checkedStory.BattleContent.InitialCash
                    ?? throw new InvalidDataException("The authored shop context requires the explicit cash seed (battle-data $.cash).");
            storyCash = new CashLiveState(cashSeed);
            // #224: the save chain binds the same shared owners the A/B sessions use; the world
            // instance is the one the trigger executor mutates, so the snapshot reads exactly
            // the live world. The content identity folds the bundle and resource hashes only.
            storyWorld = DynamicEventWorld.ForHost(checkedStory.Exploration.Rules,
                checkedStory.Exploration.Leader, map.Data.SceneId, map.Data.WScriptOnEnter, map.Data.WScriptOnTeleport,
                roleLiveState, storyPartyRoleIds);
            // #234: the interaction's completed trigger publishes its returned entry into the same
            // runtime world the save chain consumes (play.c 153), not the static exploration copy.
            var interaction = NpcInteractionSession.ForSelectedEntry(checkedStory.Exploration.Rules,
                checkedStory.Exploration.Leader, checkedStory.Entry, storyWorld);
            dialogue.FirstBattleWorld = storyWorld;
            storySaveGate = new ExplorationSaveGate(roleLiveState, storyInventory, storyCash, storyWorld);
            storySaveStorage = new PlayerSaveStorage(selected.SaveDirectory);
            SaveContentIdentity = "story:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(checkedStory.BundleSha256 + ";"
                    + string.Join(";", checkedStory.ResourceSha256.Values.Order()))));
            dialogue.Initialize(display, checkedStory, storyRandom,
                equipmentPage ? storyEquipment : null, equipmentPage ? storyPoisons : null,
                shopContext ? storyCash : null, shopContext ? storyInventory : null);
            story = checkedStory; storyVisual = checkedVisual; mapScene = map;
            Interaction = interaction; Display = display; Dialogue = dialogue; SelectedLanguage = selected.Language;
            // #213/#214: the exploration menu host binding. The session resolves the prepared menu,
            // label and role-name words now, and the font preflight covers every glyph the menu
            // can draw, so missing content rejects before the scene publishes ready. The
            // equipment page exists only for scenarios that author an equip context; its bag,
            // bindings and poison owner come from the same shared live owners. #227 extracts the
            // construction so a committed load rebuilds the same session over restored owners.
            menu = BuildStoryMenu(storyPartyRoleIds, roleLiveState, storyEquipment, storyPoisons, storyInventory);
            PalMenuView.ValidateTexts(checkedStory.Font,
                Enumerable.Range(0, menu.Entries.Count).Select(menu.EntryLabel)
                    .Concat(new[]
                    {
                        ExplorationMenuSession.LevelWord, ExplorationMenuSession.HpWord, ExplorationMenuSession.MpWord,
                        ExplorationMenuSession.ExperienceWord, ExplorationMenuSession.AttackPowerWord,
                        ExplorationMenuSession.MagicPowerWord, ExplorationMenuSession.DefenseWord,
                        ExplorationMenuSession.DexterityWord, ExplorationMenuSession.FleeRateWord,
                    }.Select(word => checkedStory.SelectedWords[word]))
                    .Concat(menu.Party.Select(member => checkedStory.SelectedWords[member.NameWord]))
                    .Concat(equipmentPage
                        ? Enumerable.Range(0, BattleEquipmentState.SlotCount)
                            .Select(part => checkedStory.SelectedWords[ExplorationMenuSession.PartWordBase + part])
                            .Concat(checkedStory.BattleContent.EquipMenu
                                .Select(binding => checkedStory.SelectedWords[binding.ObjectId]))
                        : Enumerable.Empty<string>())
                    .Concat(itemsPage
                        ? checkedStory.BattleContent.UseMenu
                            .Select(binding => checkedStory.SelectedWords[binding.ObjectId])
                        : Enumerable.Empty<string>())
                    .Concat(magicPage
                        ? checkedStory.BattleContent.MagicMenu
                            .Select(binding => checkedStory.SelectedWords[binding.ObjectId])
                        : Enumerable.Empty<string>())
                    .Concat(shopContext
                        ? new[] { ExplorationShopSession.ConfirmNoWord, ExplorationShopSession.ConfirmYesWord,
                              ExplorationShopSession.ShopOwnedWord, ExplorationShopSession.ShopPriceWord,
                              ExplorationShopSession.CashWord }
                            .Select(word => checkedStory.SelectedWords[word])
                            .Concat(checkedStory.BattleContent.ShopObjects.Keys
                                .Select(objectId => checkedStory.SelectedWords[objectId]))
                        : Enumerable.Empty<string>())
                    .Append("0123456789:/"));
            if (shopContext)
                shop = new ExplorationShopSession(checkedStory.BattleContent.Stores,
                    checkedStory.BattleContent.ShopObjects, storyCash!, storyInventory,
                    roleLiveState, storyPartyRoleIds, checkedStory.SelectedWords);
            AddChild(map); AddChild(display); AddChild(dialogue);
            display.Failed += DisplayFailed;
            display.ActivePaletteChanged += StoryPaletteChanged;
            display.BackgroundRestoreRequested += RestoreStoryMap;
            display.UpdateCompleted += CompleteTrigger;
            RenderingServer.FramePostDraw += StartAfterSceneDraw; rendererSubscribed = true;
            GD.Print($"PAL_STORY_LOADED:{selected.StoryProfile}; visual={selected.VisualProfile}; language={selected.Language}");
            GD.Print($"PAL_MAP_LOADED:{checkedStory.StaticResources}/Scenes/Scene.tscn");
            GD.Print($"PAL_GAME_READY:{Pal.Core.Startup.Status}");
        }
        catch
        {
            ReleaseStory();
            foreach (Node? node in new Node?[] { dialogue, display, map })
                if (node is not null && GodotObject.IsInstanceValid(node))
                { node.GetParent()?.RemoveChild(node); node.Free(); }
            mapScene = null; Display = null; Dialogue = null; Interaction = null; SelectedLanguage = null;
            throw;
        }
    }
    /// <summary>The story host's exploration menu session over the shared live owners
    /// (#213–#217, #222/#224). #227 extracts the construction so the load commit rebuilds the
    /// exact same session shape over the restored owners and the saved party order — every
    /// restored member must map to an authored player record (its name word, panel declaration
    /// and authored panel words are content words), and the session resolves every label, name
    /// and binding word at construction, so a closure the prepared content cannot rebuild
    /// rejects before the switch applies anything anywhere.</summary>
    private ExplorationMenuSession BuildStoryMenu(IReadOnlyList<int> partyRoleIds, RoleLiveState roles,
        BattleEquipmentState equipment, PartyPoisonState poisons, InventoryLiveState inventory)
    {
        var content = story!.BattleContent;
        var byRole = content.Players.ToDictionary(player => player.RoleId);
        var menuEntries = new List<ExplorationMenuEntry>
        {
            new(ExplorationMenuPage.Status, ExplorationMenuSession.StatusEntryWord),
        };
        if (content.EquipMenu.Count > 0) menuEntries.Add(new(ExplorationMenuPage.Equipment, ExplorationMenuSession.EquipmentEntryWord));
        if (content.UseMenu.Count > 0) menuEntries.Add(new(ExplorationMenuPage.Items, ExplorationMenuSession.ItemsEntryWord));
        if (content.MagicMenu.Count > 0) menuEntries.Add(new(ExplorationMenuPage.Magic, ExplorationMenuSession.MagicEntryWord));
        // #222/#224: the formal M6-C system entry (uigame.c GAMEMENU_LABEL_SYSTEM, the
        // reference's always-present last item) opens the system-menu layer for every story
        // context; #227's SAVE/LOAD item words resolve with the other entries.
        menuEntries.Add(new(ExplorationMenuPage.System, ExplorationMenuSession.SystemEntryWord));
        return new ExplorationMenuSession(
            menuEntries,
            partyRoleIds.Select(roleId => byRole.TryGetValue(roleId, out var player)
                ? new ExplorationMenuMember(roleId, ExplorationMenuSession.RoleNameWordBase + roleId,
                    content.PanelDeclaredRoles.Contains(roleId),
                    player.Attack, player.MagicPower, player.Defense, player.Dexterity, player.FleeRate)
                : throw new InvalidDataException(
                    $"The party member {roleId} has no authored player record in the prepared battle content."))
                .ToArray(),
            roles, equipment, content.GrowthByRole, story.SelectedWords, poisons,
            content.EquipMenu, content.EquipmentObjects, inventory,
            content.UseMenu, content.MagicMenu);
    }

    /// <summary>One empty unused role record (#224): level 0, no vitals, seven zero growth
    /// tracks and zero panels — the reference's unallocated rgPlayerRoles row.</summary>
    private static BattleRoleSnapshot EmptyRole(int roleId) => new(roleId, 0, 0, 0, 0, 0,
        new BattleExperienceSnapshot(0, 0, 0),
        PlayerSaveContract.HiddenExperienceTrackNames.ToDictionary(track => track,
            _ => new BattleExperienceSnapshot(0, 0, 0)),
        new int[BattleEquipmentState.SlotCount], new int[PlayerSaveContract.MaxLearnedMagics],
        new int[BattleEquipmentState.StatusCount], 0, 0, 0, 0, 0, 0);

    private void StoryPaletteChanged(Color[] colors)
    {
        if (Failure is not null || exited || story is null || mapScene is null) return;
        story.Exploration.SetPalette(mapScene.Data, colors);
        mapScene.UpdateScene(Exploration!.Plan);
    }
    private void RestoreStoryMap()
    {
        if (Failure is null && !exited && mapScene is not null) mapScene.UpdateScene(Exploration!.Plan);
    }
    private void ResumeStoryDisplay()
    {
        if (!resumeStoryDisplay) return;
        resumeStoryDisplay = false;
        Display!.SetProcess(true);
    }
    private bool ProcessStoryBattle()
    {
        if (story is null) return false;
        uint now = presentationNow = unchecked((uint)Time.GetTicksMsec());
        if (storyRequest is null)
        {
            if (Dialogue?.Session?.PendingBattle is not { } pending) return false;
            var view = StoryBattleViewFactory();
            try { view.Initialize(story, storyVisual!, pending.EnemyTeamId); }
            catch { view.ReleaseTextures(); view.Free(); throw; }
            var team = storyVisual!.Teams[pending.EnemyTeamId];
            storyTeam = team;
            enemyIdle = team.EnemySlots.Select(slot => new StoryEnemyIdleState(slot.IdleFrames, slot.IdleAnimSpeed)).ToArray();
            // battle.c1782 startup, then the immediately due first StartFrame (battle.c763–792).
            AdvanceStoryEnemies(); AdvanceStoryEnemies();
            outerClock = StoryBattlePoseClock.Start(now); outerDue = false;
            storyRequest = pending; storyView = view; battleConfirm = false; storyCancel = false; settlementPresented = false;
            storyTargetSlot = 0; storyCycle = 0; storyLastPresentedAction = null;
            StoryBattleMode = StoryBattlePresentation.AttackMenu;
            Display!.SetProcess(false);
            mapScene!.Visible = false; AddChild(view);
            view.Present(pending.Lifecycle.Current.Combat, StoryBattleMode.Value, enemyIdle, highlightedEnemySlot: storyTargetSlot);
            return true;
        }
        var lifecycle = storyRequest.Lifecycle;
        if (StoryBattleMode != StoryBattlePresentation.Feedback)
        {
            var nextOuter = outerClock.Advance(now);
            if (outerDue || nextOuter != outerClock)
            {
                AdvanceStoryEnemies(); outerDue = false; outerClock = StoryBattlePoseClock.Start(now);
            }
        }
        bool confirm = battleConfirm; battleConfirm = false;
        bool cancel = storyCancel; storyCancel = false;
        switch (StoryBattleMode)
        {
            case StoryBattlePresentation.AttackMenu:
                if (storyFlee)
                {
                    // input.c kKeyFlee ('q'): the committer's flee covers every later member
                    // (fight.c 1965-1978), so the queue builds without further menus.
                    storyFlee = false;
                    var selectingFlee = lifecycle.Current.Combat.SelectingPlayerIndex
                        ?? throw new InvalidOperationException("Flee menu has no selecting party member.");
                    lifecycle.SelectFlee(selectingFlee);
                    StoryBattleMode = lifecycle.Current.Combat.Phase == ClassicBattlePhase.Resolving
                        ? StoryBattlePresentation.Resolving : StoryBattlePresentation.AttackMenu;
                    break;
                }
                if (storyDefend)
                {
                    // input.c 'd' kKeyDefend (uibattle.c 1166-1171): the defend commit carries no
                    // target menu; the member's pose and count land when the action performs in
                    // the round (fight.c 4110-4116), never at the commit itself.
                    storyDefend = false;
                    var selectingDefender = lifecycle.Current.Combat.SelectingPlayerIndex
                        ?? throw new InvalidOperationException("Defend menu has no selecting party member.");
                    lifecycle.SelectDefend(selectingDefender);
                    StoryBattleMode = lifecycle.Current.Combat.Phase == ClassicBattlePhase.Resolving
                        ? StoryBattlePresentation.Resolving : StoryBattlePresentation.AttackMenu;
                    break;
                }
                if (cancel)
                {
                    // uibattle.c 1225-1252 (PAL_CLASSIC kKeyMenu): revert the newest committed
                    // member selection; the first member has nothing editable above it and the
                    // request stays in the same menu without side effects.
                    lifecycle.CancelSelection();
                    break;
                }
                if (confirm)
                {
                    var selectingMember = lifecycle.Current.Combat.SelectingPlayerIndex
                        ?? throw new InvalidOperationException("Attack menu has no selecting party member.");
                    if (lifecycle.Current.Combat.Players[selectingMember].CanAttackAll)
                    {
                        // uibattle.c 1094-1099 + 1611-1617 (PAL_CLASSIC): an attack-all member's
                        // Attack confirm commits sTarget -1 directly; the classic target menu is
                        // never entered for such a member.
                        lifecycle.SelectAttackAll(selectingMember);
                        StoryBattleMode = lifecycle.Current.Combat.Phase == ClassicBattlePhase.Selecting
                            ? StoryBattlePresentation.AttackMenu : StoryBattlePresentation.Resolving;
                        break;
                    }
                    var enemies = lifecycle.Current.Combat.Enemies;
                    // No previous-target memory: each member starts from the first alive slot.
                    storyTargetSlot = Enumerable.Range(0, enemies.Count).First(slot => enemies[slot].Hp > 0);
                    StoryBattleMode = StoryBattlePresentation.TargetMenu;
                }
                break;
            case StoryBattlePresentation.TargetMenu:
                if (cancel)
                {
                    // uibattle.c 1512: cancelling the target returns to the move menu; nothing was
                    // committed, so no Core call, no queue and no highlight memory remain.
                    storyCycle = 0;
                    StoryBattleMode = StoryBattlePresentation.AttackMenu;
                    break;
                }
                if (storyCycle != 0)
                {
                    var enemies = lifecycle.Current.Combat.Enemies;
                    for (int step = 0; step < enemies.Count; step++)
                    {
                        storyTargetSlot = (storyTargetSlot + (storyCycle > 0 ? 1 : enemies.Count - 1)) % enemies.Count;
                        if (enemies[storyTargetSlot].Hp > 0) break;
                    }
                    storyCycle = 0;
                }
                if (confirm)
                {
                    var selecting = lifecycle.Current.Combat.SelectingPlayerIndex
                        ?? throw new InvalidOperationException("Target menu has no selecting party member.");
                    lifecycle.SelectAttack(selecting, storyTargetSlot);
                    StoryBattleMode = lifecycle.Current.Combat.Phase == ClassicBattlePhase.Selecting
                        ? StoryBattlePresentation.AttackMenu : StoryBattlePresentation.Resolving;
                }
                break;
            case StoryBattlePresentation.Resolving:
            {
                // fight.c 768-880: the paused battle shows its role-hook dialogue before the queue
                // continues; the resume returns to this saved queue position afterwards.
                if (lifecycle.Current.Combat.PendingRoleHook is not null) { BeginStoryBattleHook(); break; }
                // Dead actors' queue entries pass without a new LastAction; queue advancement and
                // action feedback are distinguished by reference so a pass never re-presents it.
                ClassicBattleAction? presented = null;
                ClassicBattleFleeAttempt? presentedFlee = null;
                int[] playersBefore = [], enemiesBefore = [];
                while (lifecycle.Current.Combat.Phase == ClassicBattlePhase.Resolving)
                {
                    var before = lifecycle.Current.Combat;
                    playersBefore = before.Players.Select(member => member.Hp).ToArray();
                    enemiesBefore = before.Enemies.Select(slot => slot.Hp).ToArray();
                    if (!lifecycle.ResolveNextAction())
                        throw new InvalidOperationException("Battle action owner did not resolve an action.");
                    var fleeCandidate = lifecycle.Current.Combat.LastFleeAttempt;
                    if (fleeCandidate is not null && !ReferenceEquals(fleeCandidate, storyLastFleeAttempt))
                    { presentedFlee = fleeCandidate; storyLastFleeAttempt = fleeCandidate; break; }
                    var candidate = lifecycle.Current.Combat.LastAction;
                    if (candidate is not null && !ReferenceEquals(candidate, storyLastPresentedAction))
                    { presented = candidate; storyLastPresentedAction = candidate; break; }
                }
                if (presented is null && presentedFlee is null)
                {
                    // The queue ended on passes; the next round selects without new feedback.
                    StoryBattleMode = StoryBattlePresentation.AttackMenu;
                    break;
                }
                if (presentedFlee is { } flee)
                {
                    // fight.c 4153-4171 (failure): three +4/+2 escape steps, then the failed-escape
                    // label holds for eight frames; battle.c 1458-1528 (success): every living
                    // member walks off the screen together before the battle releases.
                    storyNumbers.BeginAction(); feedbackBorn = true;
                    var origins = storyVisual!.PlayerOrigins(lifecycle.Current.Combat.Players.Count);
                    fleeEscaped = flee.Escaped;
                    fleeOrigin = origins[flee.PartyIndex];
                    fleePartyIndex = flee.PartyIndex;
                    enemyPresentation = null; playerPresentation = null;
                    StoryBattleMode = StoryBattlePresentation.Feedback;
                    poseClock = StoryBattlePoseClock.Start(now);
                    break;
                }
                var action = presented;
                int beforeHp = playersBefore[action.Attacker == ClassicBattleSide.Enemy ? action.TargetIndex : 0];
                int beforeEnemyHp = enemiesBefore[action.Attacker == ClassicBattleSide.Player ? action.TargetIndex : 0];
                storyNumbers.BeginAction(); feedbackBorn = false;
                feedbackBeforeHp = action.Attacker == ClassicBattleSide.Player ? beforeEnemyHp : beforeHp;
                feedbackEnemiesBefore = enemiesBefore;
                var combat = lifecycle.Current.Combat;
                var playerOrigins = storyVisual!.PlayerOrigins(combat.Players.Count);
                var actingEnemy = action.Attacker == ClassicBattleSide.Enemy ? storyTeam!.EnemySlots[action.ActorIndex] : null;
                enemyPresentation = actingEnemy is null ? null : new(
                    actingEnemy.IdleFrames, actingEnemy.MagicFrames, actingEnemy.AttackFrames, actingEnemy.ActWaitFrames,
                    playerOrigins[action.TargetIndex], actingEnemy.EnemyPosition, beforeHp,
                    combat.Players[action.TargetIndex].Hp, story.BattleContent.Players[action.TargetIndex].MaxHp,
                    action, enemyIdle![action.ActorIndex].Frame);
                playerPresentation = null;
                if (action.Attacker == ClassicBattleSide.Player)
                {
                    playerPresentation = action.AttackAll
                        ? new StoryPlayerAttackAllPresentation(playerOrigins[action.ActorIndex],
                            combat.LastActionTargets.Select(record => (Vector2)storyTeam!.EnemySlots[record.TargetIndex].EnemyPosition).ToArray(),
                            combat.LastActionTargets.Select(record => record.TargetIndex).ToArray(),
                            combat.LastActionTargets.Select(record => enemiesBefore[record.TargetIndex]).ToArray(),
                            combat.LastActionTargets.Select(record => combat.Enemies[record.TargetIndex].Hp).ToArray(),
                            StoryEnemyAttackPresentation.RestFrame(combat.Players[action.ActorIndex].Hp,
                                story.BattleContent.Players[action.ActorIndex].MaxHp),
                            action.DualAttack, action.SecondStrike)
                        : new StoryPlayerAttackPresentation(playerOrigins[action.ActorIndex], storyTeam!.EnemySlots[action.TargetIndex].EnemyPosition,
                            beforeEnemyHp, combat.Enemies[action.TargetIndex].Hp,
                            StoryEnemyAttackPresentation.RestFrame(combat.Players[action.ActorIndex].Hp,
                                story.BattleContent.Players[action.ActorIndex].MaxHp));
                }
                StoryBattleMode = StoryBattlePresentation.Feedback;
                poseClock = StoryBattlePoseClock.Start(now);
                AdvanceStoryGesture(0);
                break;
            }
            case StoryBattlePresentation.Feedback:
                var nextPose = poseClock.Advance(now);
                if (nextPose == poseClock) break;
                poseClock = nextPose;
                long poses = fleeOrigin is not null
                    ? (fleeEscaped ? StoryPlayerEscapePresentation.Duration : StoryPlayerFleePresentation.Duration)
                    : enemyPresentation?.Duration ?? StoryPlayerAttackPresentation.Duration;
                if (poseClock.Pose < poses) AdvanceStoryGesture(poseClock.Pose);
                if (poseClock.Pose < poses) break;
                // Enemy action's final UpdateFighters is not another timed pose (fight.c5137).
                if (enemyPresentation is not null) AdvanceStoryEnemies();
                if (fleeOrigin is not null) { fleeOrigin = null; fleeEscaped = false; }
                if (lifecycle.Completion is not null || lifecycle.VictorySettlement is not null || lifecycle.PendingBattleEndScript is not null)
                {
                    HandleStoryVictoryBoundary();
                    return true;
                }
                // The triggering action's feedback is done; a paused role hook shows its script now.
                if (lifecycle.Current.Combat.PendingRoleHook is not null) { BeginStoryBattleHook(); break; }
                // The outer deadline set before the blocking action is already overdue.
                outerDue = true;
                StoryBattleMode = lifecycle.Current.Combat.Phase == ClassicBattlePhase.Selecting
                    ? StoryBattlePresentation.AttackMenu : StoryBattlePresentation.Resolving;
                break;
            case StoryBattlePresentation.Settlement:
                // PAL_WaitForAnyKey (battle.c 1031/1140/1323): confirm or the boss/normal timeout advances.
                if (confirm || unchecked(now - settlementShownAt) >= (storyRequest!.IsBoss ? 5500u : 3000u))
                {
                    settlementStage++;
                    if (settlementStage < settlementLines!.Count)
                    {
                        settlementShownAt = now;
                        PresentStorySettlement();
                    }
                    else
                    {
                        settlementLines = null;
                        HandleStoryVictoryBoundary();
                        return true; // the boundary may complete the battle and free the view
                    }
                }
                break;
            case StoryBattlePresentation.HookDialogue:
                // The hook script runs on the real display; its completion acknowledges the paused
                // battle (TriggerScriptSession.Complete), which is observed through the combat phase.
                // A battle-end script keeps the Won combat phase, so its running record gates the end.
                if (lifecycle.Current.Combat.Phase != ClassicBattlePhase.RoleHook
                    && Dialogue!.Session!.RunningBattleEndScript is null)
                {
                    EndStoryBattleHook();
                    if (lifecycle.PendingBattleEndScript is not null) { BeginStoryBattleEndScript(); break; }
                    if (lifecycle.Completion is { } hookCompletion)
                    {
                        CompleteStoryBattle(hookCompletion);
                        return true;
                    }
                    outerDue = true;
                    StoryBattleMode = lifecycle.Current.Combat.Phase == ClassicBattlePhase.Selecting
                        ? StoryBattlePresentation.AttackMenu : StoryBattlePresentation.Resolving;
                }
                break;
                // The outer deadline set before the blocking action is already overdue.
                outerDue = true;
                StoryBattleMode = lifecycle.Current.Combat.Phase == ClassicBattlePhase.Selecting
                    ? StoryBattlePresentation.AttackMenu : StoryBattlePresentation.Resolving;
                break;
        }
        var shownEnemy = StoryBattleMode == StoryBattlePresentation.Feedback ? enemyPresentation?.At(poseClock.Pose) : null;
        if (shownEnemy is { } shown) shownEnemy = shown with { EnemyFrame = enemyIdle![lifecycle.Current.Combat.LastAction!.ActorIndex].Frame };
        var fleeSnapshot = StoryBattleMode == StoryBattlePresentation.Feedback && fleeOrigin is { } fleeAt && !fleeEscaped
            ? (StoryPlayerFleeSnapshot?)StoryPlayerFleePresentation.At(poseClock.Pose, fleeAt, fleePartyIndex) : null;
        var escapeSnapshots = StoryBattleMode == StoryBattlePresentation.Feedback && fleeOrigin is not null && fleeEscaped
            ? StoryPlayerEscapePresentation.At(poseClock.Pose, lifecycle.Current.Combat.Players) : null;
        if (StoryBattleMode == StoryBattlePresentation.Feedback && fleeSnapshot is null)
        {
            var action = lifecycle.Current.Combat.LastAction!;
            bool playerHit = playerPresentation is not null && poseClock.Pose >= 7;
            // Enemy stat-change queues after the last prehit UIUpdate, before the impact
            // Delay(FALSE) waits40ms. Preserve that real birth even if the next callback is late.
            bool enemyEnqueue = enemyPresentation is { } enemy && poseClock.Pose >= enemy.ImpactPose - 1;
            if (!feedbackBorn && (playerHit || enemyEnqueue))
            {
                var origins = storyVisual!.PlayerOrigins(lifecycle.Current.Combat.Players.Count);
                if (playerHit && action.AttackAll)
                {
                    // fight.c 2198-2212: every struck enemy's stat change displays together at its
                    // own position after the first FX frame — one pending number per struck slot.
                    foreach (var record in lifecycle.Current.Combat.LastActionTargets)
                    {
                        Vector2 target = storyTeam!.EnemySlots[record.TargetIndex].EnemyPosition;
                        target.Y = Math.Max(10, target.Y - 115);
                        storyNumbers.EnqueueAction(feedbackEnemiesBefore[record.TargetIndex], record, target, now, pending: true);
                    }
                    feedbackBorn = true;
                }
                else
                {
                    Vector2 target = playerHit ? storyTeam!.EnemySlots[action.TargetIndex].EnemyPosition
                        : origins[action.TargetIndex];
                    target.Y = Math.Max(10, target.Y - (playerHit ? 115 : 75));
                    storyNumbers.EnqueueAction(feedbackBeforeHp, action, target, now, pending: true);
                    feedbackBorn = true;
                }
            }
            if ((playerPresentation is not null && poseClock.Pose >= 8) || shownEnemy is { ResultVisible: true })
                storyNumbers.RevealPending();
        }
        string? settlementLine = StoryBattleMode == StoryBattlePresentation.Settlement && settlementLines is { } current
            ? current[Math.Clamp(settlementStage, 0, current.Count - 1)] : null;
        storyView!.Present(lifecycle.Current.Combat, StoryBattleMode!.Value, enemyIdle!,
            shownEnemy, StoryBattleMode == StoryBattlePresentation.Feedback ? playerPresentation?.At(poseClock.Pose, effectEnemyHeight) : null,
            storyNumbers.At(now), fleeSnapshot is null ? storyNumbers.LabelAt(now)
                : poseClock.Pose >= StoryPlayerFleePresentation.LabelPose ? PalStoryBattleView.EscapeFailWord : null,
            storyTargetSlot, settlementLine, fleeSnapshot, escapeSnapshots);
        return true;
    }
    /// <summary>True while the failed-escape feedback presents (authored: the scene freezes).</summary>
    public bool StoryFleeFeedback => fleeOrigin is not null;
    /// <summary>True while the successful-escape walk-off presents (battle.c 1458-1528): every
    /// living member leaves the screen before the battle releases with its owned receipt.</summary>
    public bool StoryEscapeFeedback => fleeOrigin is not null && fleeEscaped;
    /// <summary>palette.c PAL_FadeToRed (script.c 0x004F game-over fade): every palette entry
    /// except the preserved 0x4F text color moves red by 8 per step for 32 steps of 75ms;
    /// this is the step-<paramref name="step"/> palette of that reference convergence.</summary>
    public static Color[] RedFadePalette(IReadOnlyList<Color> palette, int step)
    {
        var faded = palette.ToArray();
        for (int index = 0; index < faded.Length; index++)
        {
            if (index == 0x4F) continue; // so that texts will not be affected (palette.c)
            Color color = palette[index];
            int target = (color.R8 + color.G8 + color.B8) / 4 + 64;
            int red = color.R8, green = color.G8, blue = color.B8;
            if (red > target) red -= Math.Min(8 * step, red - target);
            else if (red < target) red += Math.Min(8 * step, target - red);
            faded[index] = Color.Color8((byte)red, (byte)Math.Max(0, green - 8 * step), (byte)Math.Max(0, blue - 8 * step));
        }
        return faded;
    }

    /// <summary>Steps the started red fade once per 75ms until convergence; presentation only.</summary>
    private void AdvanceStoryRedFade(uint now)
    {
        if (storyRedStep is < 0 or >= 32 || unchecked(now - storyRedStepAt) < 75u) return;
        storyRedStepAt = now;
        storyRedStep++;
        story.Exploration.SetPalette(mapScene!.Data, RedFadePalette(story.Palette, storyRedStep));
        mapScene.UpdateScene(Exploration!.Plan);
    }

    private void BeginStoryBattleHook()
    {
        // The paused battle stays visible behind the real dialogue display; the display is parked
        // again when the hook completes so it cannot consume battle-frame time or input.
        StoryBattleMode = StoryBattlePresentation.HookDialogue;
        Display!.ZIndex = 200; // above the battle view's ZIndex 100
        Display!.SetProcess(true);
        Dialogue!.StartBattleHook();
        storyView!.Present(storyRequest!.Lifecycle.Current.Combat, StoryBattleMode.Value,
            enemyIdle!, highlightedEnemySlot: storyTargetSlot);
    }

    private void BeginStoryBattleEndScript()
    {
        // battle.c 1334-1337: the enemy's post-victory script runs its dialogue over the finished
        // battle before the automatic recovery; same presentation rules as the role hook.
        StoryBattleMode = StoryBattlePresentation.HookDialogue;
        Display!.ZIndex = 200;
        Display!.SetProcess(true);
        Dialogue!.StartBattleEndScript();
        storyView!.Present(storyRequest!.Lifecycle.Current.Combat, StoryBattleMode.Value,
            enemyIdle!, highlightedEnemySlot: storyTargetSlot);
    }

    /// <summary>The victory boundary after the final action feedback: the authored settlement boxes
    /// first (exp/cash, level-up, learned magic), then the enemy post-victory scripts, then the
    /// owned completion. PAL_BattleWon order (battle.c 1003-1349).</summary>
    private void HandleStoryVictoryBoundary()
    {
        var lifecycle = storyRequest!.Lifecycle;
        if (!settlementPresented && lifecycle.VictorySettlement is { } victory)
        {
            settlementPresented = true;
            settlementLines = SettlementLines(victory);
            if (settlementLines.Count > 0)
            {
                settlementStage = 0;
                settlementShownAt = presentationNow;
                StoryBattleMode = StoryBattlePresentation.Settlement;
                PresentStorySettlement();
                return;
            }
        }
        if (lifecycle.PendingBattleEndScript is not null) { BeginStoryBattleEndScript(); return; }
        if (lifecycle.Completion is { } completion) { CompleteStoryBattle(completion); return; }
        // A non-victory completion (e.g. Loss) resumes the queue normally.
        outerDue = true;
        StoryBattleMode = lifecycle.Current.Combat.Phase == ClassicBattlePhase.Selecting
            ? StoryBattlePresentation.AttackMenu : StoryBattlePresentation.Resolving;
    }

    private List<string> SettlementLines(ClassicBattleVictorySettlement victory)
    {
        var words = story!.SelectedWords;
        var lines = new List<string>();
        // battle.c 1022-1031: the exp/cash box appears only when experience was gained.
        if (victory.ExperienceGranted > 0)
            lines.Add(words[PalStoryBattleView.ExpWord] + ":" + victory.ExperienceGranted
                + " " + words[PalStoryBattleView.CashWord] + ":" + victory.CashGranted);
        var members = storyRequest.Lifecycle.Current.Combat.Players;
        foreach (var growth in victory.Growth)
        {
            int member = 0;
            for (int i = 0; i < members.Count; i++) if (members[i].RoleId == growth.RoleId) { member = i + 1; break; }
            if (member == 0) continue;
            if (growth.LevelAfter > growth.LevelBefore)
                lines.Add(words[PalStoryBattleView.LevelUpWord] + ":" + member + ":" + growth.LevelAfter);
            foreach (int magic in growth.LearnedMagics)
                lines.Add(words[PalStoryBattleView.LearnedMagicWord] + ":" + member + ":" + magic);
        }
        return lines;
    }

    private void PresentStorySettlement()
    {
        storyView!.Present(storyRequest!.Lifecycle.Current.Combat, StoryBattlePresentation.Settlement,
            enemyIdle!, highlightedEnemySlot: storyTargetSlot,
            settlementLine: settlementLines![settlementStage]);
    }

    private void EndStoryBattleHook()
    {
        Display!.ZIndex = 0;
        Display!.SetProcess(false);
    }

    private void CompleteStoryBattle(ClassicBattleLifecycleSession.CompletionReceipt completion)
    {
        var request = storyRequest;
        // The story ledger consumes the granted totals exactly once per owned Won receipt.
        if (completion.Outcome == ClassicBattlePhase.Won)
        {
            StoryCashGranted += completion.CashGranted;
            StoryExperienceGranted += completion.ExperienceGranted;
        }
        settlementLines = null; settlementStage = 0; settlementPresented = false;
        // Remove the complete feedback overlay and restore the map before the owned receipt.
        storyNumbers.Clear();
        RemoveChild(storyView!); storyView!.Free(); storyView = null;
        mapScene!.Visible = true; RestoreStoryMap();
        storyRequest = null; StoryBattleMode = null; enemyPresentation = null; playerPresentation = null; enemyIdle = null;
        if (!Dialogue!.AcknowledgeBattle(request.Id, completion))
            throw new InvalidOperationException("Story battle completion was not owned by the active trigger.");
        // A child display must not consume this battle callback's delta.
        resumeStoryDisplay = true;
        CompleteTrigger();
        CompleteFirstBattleTrigger();
    }

    private void AdvanceStoryEnemies()
    {
        foreach (var idle in enemyIdle!) idle.Advance();
    }

    private void AdvanceStoryGesture(int pose)
    {
        if (playerPresentation is not null)
        {
            // Four TRUE windup, three TRUE approach, three manual FX, three TRUE recoil,
            // UpdateFighters at13 followed by three TRUE tail frames.
            // ShowPlayerAttackAnim reads target height after windup, before its first TRUE approach delay.
            int target = storyRequest!.Lifecycle.Current.Combat.LastAction!.TargetIndex;
            if (pose == 4) effectEnemyHeight = storyTeam!.EnemySlots[target].EnemySprite!
                .Frames.Single(f => f.Index == enemyIdle![target].Frame).Height;
            if (pose == 13) AdvanceStoryEnemies();
            AdvanceStoryEnemies();
        }
        else if (enemyPresentation is { } enemy)
        {
            long tail = enemy.Duration - 5;
            int actor = storyRequest!.Lifecycle.Current.Combat.LastAction!.ActorIndex;
            if (pose < tail) enemyIdle![actor].SetFrame(enemy.At(pose).EnemyFrame);
            else AdvanceStoryEnemies();
        }
        // Flee feedback (authored presentation): the escaper steps while the held scene
        // otherwise freezes; the enemy idle resumes with the outer clock after the label.
    }
    private void ReleaseStory()
    {
        if (Display is not null)
        {
            Display.ActivePaletteChanged -= StoryPaletteChanged;
            Display.BackgroundRestoreRequested -= RestoreStoryMap;
        }
        if (storyView is not null && GodotObject.IsInstanceValid(storyView))
        {
            storyView.GetParent()?.RemoveChild(storyView); storyView.ReleaseTextures(); storyView.Free();
        }
        storyView = null; storyRequest = null; storyTeam = null; StoryBattleMode = null; battleConfirm = false; storyCancel = false; resumeStoryDisplay = false;
        enemyPresentation = null; playerPresentation = null; enemyIdle = null; outerDue = false;
        storyTargetSlot = 0; storyCycle = 0; storyLastPresentedAction = null;
        storyFlee = false; storyDefend = false; storyLastFleeAttempt = null; fleeOrigin = null; fleePartyIndex = -1; fleeEscaped = false; storyRedStep = -1;
        settlementLines = null; settlementStage = 0; settlementPresented = false;
        storyNumbers.Clear(); feedbackBorn = false;
        storyRandom?.Dispose(); storyRandom = null; story = null; storyVisual = null;
        storyEquipment = null; storyPoisons = null; storyInventory = null; storyCash = null;
        storySaveGate = null; storySaveStorage = null; storyWorld = null; storySaveCount = 0; CurrentRecoverySlot = null;
        itemUseId = Guid.Empty;
        magicRunId = Guid.Empty;
        ReleaseMenu();
        ReleaseShop();
    }

    // #213 exploration menu host (story-profile context): play.c 551 opens PAL_InGameMenu from
    // the clean exploring state after the frame's scene update, and the menu loop blocks the
    // game loop; presses apply at the real update boundary, never inside _Input.
    private void ApplyMenuInput()
    {
        if (menu is null) return;
        try
        {
            if (menu.Mode == ExplorationMenuMode.Closed)
            {
                if (menuOpenPressed && MenuOpenableState())
                {
                    menu.Open();
                    menuView = new PalMenuView();
                    AddChild(menuView);
                    PresentMenu();
                    GD.Print("PAL_MENU_OPENED:exploration main menu; world frozen until close");
                }
            }
            else
            {
                if (menu.Mode == ExplorationMenuMode.ItemScript)
                {
                    // The pending item run advances on this real update boundary: the host either
                    // starts the checked on-use script or receipts its completed call, then the
                    // parked view re-presents behind the script's dialogue.
                    AdvanceItemUse();
                }
                else if (menu.Mode is ExplorationMenuMode.MagicOnUse or ExplorationMenuMode.MagicOnSuccess)
                {
                    // #216: the pending cast's on-use/on-success run advances on this real update
                    // boundary (uigame.c runs both PAL_RunTriggerScript calls inside the cast);
                    // the parked view re-presents the magic list behind the script's dialogue.
                    AdvanceMagicCast();
                }
                else
                {
                    foreach (ExplorationMenuKey key in pendingMenuKeys) menu.Press(key);
                    if (menu.Mode == ExplorationMenuMode.ItemScript) AdvanceItemUse();
                    else if (menu.Mode is ExplorationMenuMode.MagicOnUse or ExplorationMenuMode.MagicOnSuccess) AdvanceMagicCast();
                    else if (menu.Mode == ExplorationMenuMode.SystemMenu && menu.PendingSave is { } pendingSave)
                        // #224: the confirmed save's whole chain completes inside this same
                        // committed production update boundary (the parked layer owns input
                        // until the receipt; duplicates never issue a second).
                        CompleteMenuSave(pendingSave);
                    else if (menu.Mode == ExplorationMenuMode.SystemMenu && menu.PendingLoad is { } pendingLoad)
                        // #227: the confirmed read's whole chain completes inside this same
                        // committed production update boundary; a successful switch retires this
                        // very menu (the Closed branch below frees its view), a failure keeps it.
                        CompleteMenuLoad(pendingLoad);
                }
                if (menu.Mode == ExplorationMenuMode.Closed)
                {
                    menuView?.GetParent()?.RemoveChild(menuView);
                    menuView?.Free();
                    menuView = null;
                    GD.Print("PAL_MENU_CLOSED:exploration input resumes on the next update");
                }
                else PresentMenu();
            }
        }
        catch (InvalidDataException error) { Fail($"Menu failed: {error.Message}"); }
        finally
        {
            menuOpenPressed = false;
            pendingMenuKeys.Clear();
        }
    }

    private bool MenuOpenableState() => menu is not null && menu.Mode == ExplorationMenuMode.Closed
        && storyRequest is null && StoryBattleMode is null && activeTrigger is null && firstBattleActiveTrigger is null
        && !StoryLostTerminal && Interaction is { Phase: NpcInteractionPhase.Exploring }
        && Dialogue?.Session is not { BlocksSceneUpdates: true };

    /// <summary>#215 play.c PAL_GameUseItem: the committed item run executes on the production
    /// trigger executor with the selected member's role as its caller (or the apply-to-all
    /// 0xFFFF which keeps the saved caller); the parked menu keeps its view behind the script's
    /// dialogue (the battle-hook layering) and only the owned, entry-stable receipt completes
    /// the use with the script's saved success flag.</summary>
    private void AdvanceItemUse()
    {
        if (menu!.PendingItemUse is not { } request) return;
        if (itemUseId == Guid.Empty)
        {
            var entry = story!.ItemUseEntries.TryGetValue(request.ObjectId, out var bound)
                ? bound : throw new InvalidDataException($"Use-menu object {request.ObjectId} has no checked on-use entry.");
            Display!.ZIndex = 200;
            Dialogue!.Start(entry.FileId, entry.Id, request.TargetRoleId, request.Id);
            itemUseId = request.Id;
            itemUseEntryAddress = entry.SourceAddress;
        }
        else if (Dialogue!.Session is { Phase: TriggerScriptPhase.Completed } session
            && session.SourceRequestId == itemUseId && session.ReturnEntry == itemUseEntryAddress)
        {
            bool success = session.ScriptSuccess;
            itemUseId = Guid.Empty;
            Display!.ZIndex = 0;
            menu.CompleteItemUse(request.Id, success);
            GD.Print($"PAL_ITEM_USED:object={request.ObjectId};caller={request.TargetRoleId};success={success}");
        }
    }

    /// <summary>#216 uigame.c PAL_InGameMagicMenu 807-841: the committed cast's two script runs
    /// execute in order on the production trigger executor with the selected target's role as
    /// their caller word (or the apply-to-all literal 0 — never the item flow's 0xFFFF keep-
    /// caller word). The parked menu keeps the magic list behind the script's dialogue and only
    /// the owned, entry-stable receipts complete each run with its saved success flag; the
    /// caster's MP moves inside the Core receipt after both gates survived.</summary>
    private void AdvanceMagicCast()
    {
        if (menu!.PendingMagicUse is { } cast)
        {
            if (magicRunId == Guid.Empty)
            {
                var entry = story!.MagicUseEntries.TryGetValue(cast.ObjectId, out var bound)
                    ? bound.OnUse : throw new InvalidDataException($"Magic-menu object {cast.ObjectId} has no checked on-use entry.");
                Display!.ZIndex = 200;
                Dialogue!.Start(entry.FileId, entry.Id, cast.ApplyToAll ? 0 : cast.TargetRoleId, cast.Id);
                magicRunId = cast.Id;
                magicRunEntryAddress = entry.SourceAddress;
            }
            else if (Dialogue!.Session is { Phase: TriggerScriptPhase.Completed } session
                && session.SourceRequestId == magicRunId && session.ReturnEntry == magicRunEntryAddress)
            {
                bool success = session.ScriptSuccess;
                magicRunId = Guid.Empty;
                Display!.ZIndex = 0;
                menu.CompleteMagicUse(cast.Id, success);
                GD.Print($"PAL_MAGIC_ON_USE:object={cast.ObjectId};caller={(cast.ApplyToAll ? 0 : cast.TargetRoleId)};success={success}");
            }
        }
        else if (menu.PendingMagicSuccess is { } after)
        {
            if (magicRunId == Guid.Empty)
            {
                var entry = story!.MagicUseEntries.TryGetValue(after.ObjectId, out var bound)
                    ? bound.OnSuccess : throw new InvalidDataException($"Magic-menu object {after.ObjectId} has no checked on-success entry.");
                Display!.ZIndex = 200;
                Dialogue!.Start(entry.FileId, entry.Id, after.ApplyToAll ? 0 : after.TargetRoleId, after.Id);
                magicRunId = after.Id;
                magicRunEntryAddress = entry.SourceAddress;
            }
            else if (Dialogue!.Session is { Phase: TriggerScriptPhase.Completed } session
                && session.SourceRequestId == magicRunId && session.ReturnEntry == magicRunEntryAddress)
            {
                bool success = session.ScriptSuccess;
                magicRunId = Guid.Empty;
                Display!.ZIndex = 0;
                menu.CompleteMagicSuccess(after.Id, success);
                GD.Print($"PAL_MAGIC_ON_SUCCESS:object={after.ObjectId};success={success}");
            }
        }
    }

    /// <summary>#224 (parent #148 Decision 22/27): the confirmed save's whole chain completes
    /// inside this one committed production update boundary — fresh #222 gate request → one-shot
    /// stable capture → #223 v1 encode → protected five-slot write through the real FileAccess
    /// user:// adapter. Only the completed outcome reports (Story 9); success switches the
    /// session's current recovery slot, and rejection or failure keep it and every live owner
    /// untouched (the gate is side-effect free; the adapter keeps the old valid slot).</summary>
    private void CompleteMenuSave(ExplorationMenuSaveRequest pending)
    {
        var gate = storySaveGate!;
        var activity = new ExplorationSaveActivity(storyRequest?.Lifecycle, Dialogue?.Session,
            Display?.Session, Interaction, menu);
        var outcome = gate.TryCapture(gate.CreateRequest(), activity, pending.Slot,
            storySaveCount + 1, SaveContentIdentity!);
        if (!outcome.Captured)
        {
            menu!.CompleteSave(pending.Id, succeeded: false);
            GD.Print($"PAL_SAVE_REJECTED:slot={pending.Slot};reason={outcome.Rejection}");
            return;
        }
        var write = storySaveStorage!.WriteSlot(pending.Slot, PlayerSaveJson.EncodeToUtf8(outcome.Save!));
        if (!write.Published)
        {
            menu!.CompleteSave(pending.Id, succeeded: false);
            GD.Print($"PAL_SAVE_FAILED:slot={pending.Slot};reason={write.Reason};oldSlotPreserved={(storySaveStorage.SlotExists(pending.Slot) ? 1 : 0)}");
            return;
        }
        storySaveCount++;
        CurrentRecoverySlot = pending.Slot; // #148 Decision 27: only a completed save switches it
        menu!.CompleteSave(pending.Id, succeeded: true);
        GD.Print($"PAL_SAVE_COMPLETED:slot={pending.Slot};savedTimes={storySaveCount};recoverySlot={CurrentRecoverySlot}");
    }

    /// <summary>#227 (parent #148 Stories 27–31, Decisions 24–25): the confirmed read's whole
    /// chain completes inside this one committed production update boundary — real FileAccess
    /// slot read through the #224 adapter, #223 decode/validation against this context's content
    /// identity, then the #225/#226 restore plans' complete candidate set. Any read, validation
    /// or preparation failure keeps the original session and the current recovery slot untouched
    /// (Story 30), un-parks the layer for cancel (Story 35) and reports one located reason;
    /// only a completed switch commits and retires the old session.</summary>
    private void CompleteMenuLoad(ExplorationMenuLoadRequest pending)
    {
        byte[]? bytes;
        try { bytes = storySaveStorage!.ReadSlot(pending.Slot); }
        catch (InvalidDataException error) { MenuLoadFailed(pending, error.Message); return; }
        if (bytes is null) { MenuLoadFailed(pending, "slot-missing"); return; }
        PlayerSaveV1 save;
        try { save = PlayerSaveJson.Decode(bytes, SaveContentIdentity!); }
        catch (InvalidDataException error) { MenuLoadFailed(pending, error.Message); return; }
        var oldMenu = menu!;
        try { CommitLoadedSession(pending.Slot, save); }
        catch (InvalidDataException error) { MenuLoadFailed(pending, error.Message); return; }
        // The retired session still receipts its own request exactly once (Story 33: old
        // receipts never reach the new session — this is the only completion it will accept).
        oldMenu.CompleteLoad(pending.Id, succeeded: true);
    }

    private void MenuLoadFailed(ExplorationMenuLoadRequest pending, string reason)
    {
        // Story 30/35: the original session and the current recovery slot stay untouched; the
        // layer re-opens browsing so the player can cancel back to the gameplay as it was.
        menu!.CompleteLoad(pending.Id, succeeded: false);
        GD.Print($"PAL_LOAD_FAILED:slot={pending.Slot};reason={reason};recoverySlotKept={(CurrentRecoverySlot is { } kept ? kept : 0)}");
    }

    /// <summary>#227 (parent #148 Decision 24–25): builds the complete candidate set first —
    /// the #225 role-domain owners over the prepared battle-content closures, the #226
    /// world/exploration/music candidates over the prepared scene closure, then every rebinding
    /// the switch needs (fresh menu, interaction, shop and a brand-new trigger executor over a
    /// brand-new dialogue node) — and only then swaps ownership in one throwaway-free pass:
    /// nothing is applied anywhere before the complete candidate exists, so any rejection
    /// leaves the old session fully usable. The swap itself happens inside the caller's already
    /// committed production update boundary: the old session is frozen (the menu owns input)
    /// and the new owners publish before any further update can run, so old and new sessions
    /// never update in parallel (Decision 25). The new session re-establishes request/receipt
    /// identity, subscriptions and input ownership (Decision 26): the old dialogue node (its
    /// display subscription and pending state included) is retired, the per-session request ids
    /// are cleared, and the restored setMusic words reseed the fresh D13 owner.</summary>
    private void CommitLoadedSession(int slot, PlayerSaveV1 save)
    {
        var checkedStory = story!;
        var content = checkedStory.BattleContent;
        // The prepared closures the candidates validate against: every magic word the authored
        // content declares (role tables ∪ menu bindings), every item object the context can
        // own (bag seed ∪ shop ∪ use-menu) and the declared equipment objects. An id outside
        // these closures can never correspond to a real capture of this content and rejects
        // fail-closed inside the plans (#148 Decision 19/24).
        var magicClosure = new HashSet<int>(content.MagicMenu.Select(binding => binding.ObjectId));
        foreach (var role in content.InitialRoles.Values)
            foreach (int magic in role.Magics)
                if (magic != 0) magicClosure.Add(magic);
        var itemClosure = new HashSet<int>(content.UseMenu.Select(binding => binding.ObjectId));
        foreach (int objectId in content.ShopObjects.Keys) itemClosure.Add(objectId);
        foreach (var row in content.InitialInventory)
            if (row.ItemId != 0) itemClosure.Add(row.ItemId);
        var roles = PlayerSaveRestorePlan.Build(save, new PlayerSaveRestoreContent(
            content.EquipmentObjects, magicClosure, itemClosure, new Dictionary<int, int>()));
        var world = PlayerSaveWorldRestorePlan.BuildWorld(save, new PlayerSaveWorldRestoreContent(
            storyWorld!.SceneId, checkedStory.Exploration.Rules, checkedStory.Exploration.Leader), roles);
        bool equipmentPage = content.EquipMenu.Count > 0;
        bool shopContext = content.ShopObjects.Count > 0;
        var newInteraction = NpcInteractionSession.ForSelectedEntry(
            world.Rules, world.World.Leader, checkedStory.Entry, world.World);
        var newMenu = BuildStoryMenu(roles.PartyRoleIds, roles.Roles, roles.Equipment, roles.Poisons, roles.Inventory);
        var newShop = shopContext
            ? new ExplorationShopSession(content.Stores, content.ShopObjects,
                roles.Cash, roles.Inventory, roles.Roles, roles.PartyRoleIds, checkedStory.SelectedWords)
            : null;
        var newDialogue = new PalTriggerDialogue();
        try
        {
            newDialogue.FirstBattleWorld = world.World;
            newDialogue.Initialize(Display!, checkedStory, storyRandom!,
                equipmentPage ? roles.Equipment : null, equipmentPage ? roles.Poisons : null,
                shopContext ? roles.Cash : null, shopContext ? roles.Inventory : null);
            if (world.Music is { } music) newDialogue.Session!.RestoreMusic(music);
        }
        catch { newDialogue.Free(); throw; }
        // One-shot ownership commit (Decision 25): field swaps only — nothing below can throw.
        var oldDialogue = Dialogue!;
        roleLiveState = roles.Roles;
        storyEquipment = roles.Equipment;
        storyPoisons = roles.Poisons;
        storyInventory = roles.Inventory;
        storyCash = roles.Cash;
        storyWorld = world.World;
        storySaveGate = new ExplorationSaveGate(roleLiveState, storyInventory, storyCash, storyWorld);
        storySaveCount = save.SavedTimes;
        CurrentRecoverySlot = slot; // Decision 27: this session's actually selected slot, never the payload's
        RemoveChild(oldDialogue);
        oldDialogue.Free();
        AddChild(newDialogue);
        Dialogue = newDialogue;
        Interaction = newInteraction;
        shop = newShop;
        // Decision 26: new request/receipt identities — old item/magic/shop requests, the parked
        // search trigger and every queued key die with the old session; no old receipt can
        // complete anything on the new owners.
        itemUseId = Guid.Empty; magicRunId = Guid.Empty; shopRequestId = Guid.Empty;
        activeTrigger = null;
        menuOpenPressed = false;
        pendingMenuKeys.Clear();
        pendingShopKeys.Clear();
        menu = newMenu;
        mapScene!.UpdateScene(newInteraction.Current.Plan);
        GD.Print($"PAL_LOAD_COMPLETED:slot={slot};savedTimes={storySaveCount};recoverySlot={CurrentRecoverySlot}");
    }

    /// <summary>#217 script.c 0x0026/0x0027: the parked shop request opens the shop over the
    /// shared owners (uigame.c blocks inside PAL_BuyMenu/PAL_SellMenu) at a real update
    /// boundary; the closed menu's owned receipt resumes the call. Input isolation and world
    /// freeze mirror the exploration menu host (#213); the shop never runs inside _Input.</summary>
    private void ApplyShopInput()
    {
        if (story is null) return;
        try
        {
            if (shop is { Mode: not ExplorationShopMode.Closed })
            {
                foreach (ExplorationMenuKey key in pendingShopKeys) shop.Press(key);
                if (shop.Mode == ExplorationShopMode.Closed)
                {
                    if (!Dialogue!.AcknowledgeShop(shopRequestId))
                        throw new InvalidOperationException("The closed shop does not match the active trigger request.");
                    shopView?.GetParent()?.RemoveChild(shopView);
                    shopView?.Free();
                    shopView = null;
                    GD.Print("PAL_SHOP_CLOSED:trigger input resumes on the next update");
                }
                else PresentShop();
            }
            else if (Dialogue?.Session?.PendingShopRequest is { } request)
            {
                if (shop is null) throw new InvalidDataException("The parked shop request has no bound shop context.");
                if (request.IsBuy) shop.OpenBuy(request.StoreId);
                else shop.OpenSell();
                shopRequestId = request.Id;
                shopView = new PalShopView();
                AddChild(shopView);
                PresentShop();
                GD.Print($"PAL_SHOP_OPENED:{(request.IsBuy ? "buy" : "sell")};store={(request.IsBuy ? request.StoreId.ToString(System.Globalization.CultureInfo.InvariantCulture) : "bag")}; world frozen until close");
            }
        }
        catch (InvalidDataException error) { Fail($"Shop failed: {error.Message}"); }
        finally
        {
            pendingShopKeys.Clear();
        }
    }

    private void PresentShop() => shopView!.Present(shop!, story!.Font, story.Palette.ToArray(), story.ItemBitmaps);

    private void ReleaseShop()
    {
        if (shopView is not null && GodotObject.IsInstanceValid(shopView))
        {
            shopView.GetParent()?.RemoveChild(shopView);
            shopView.Free();
        }
        shopView = null; shop = null; shopRequestId = Guid.Empty;
        pendingShopKeys.Clear();
    }

    private void PresentMenu() => menuView!.Present(menu!, story!.Font, story.Palette.ToArray(), story.ItemBitmaps);

    private void ReleaseMenu()
    {
        if (menuView is not null && GodotObject.IsInstanceValid(menuView))
        {
            menuView.GetParent()?.RemoveChild(menuView);
            menuView.Free();
        }
        menuView = null; menu = null; menuOpenPressed = false; pendingMenuKeys.Clear();
    }
}

/// <summary>fight.c 4153-4171 failed-escape presentation: three +4/+2 steps then the escape-fail
/// label holds for eight frames; poses advance on the shared 40ms battle pose clock.</summary>
public static class StoryPlayerFleePresentation
{
    public const int LabelPose = 3;
    public const long Duration = 11;
    public static StoryPlayerFleeSnapshot At(int pose, Vector2 origin, int partyIndex) =>
        new(partyIndex, pose < LabelPose ? 0 : 1,
            new Vector2(4 * Math.Min(pose + 1, LabelPose), 2 * Math.Min(pose + 1, LabelPose)));
}

/// <summary>battle.c 1458-1528 PAL_BattlePlayerEscape: every living member walks off together
/// for sixteen frames (party-column steps +4/+6, +4/+4 or +6/+3, battle.c 1493-1516), then all
/// players — the dead included — leave the screen (pos 9999, battle.c 1519-1526); poses advance
/// on the shared 40ms clock. Only the living walk; the dead stay visible at their home
/// positions through the walk-off (fight.c 952-957 keeps drawing them) and leave with the
/// party at the final removal frame.</summary>
public static class StoryPlayerEscapePresentation
{
    public const long Duration = 17;
    // The snapshots carry only the relative walk-off offset; the view adds each member's own
    // visual origin (PlayerOrigins columns) when drawing.
    public static IReadOnlyList<StoryPlayerFleeSnapshot> At(int pose, IReadOnlyList<ClassicBattlePlayerView> members)
    {
        var walking = new List<StoryPlayerFleeSnapshot>();
        for (int member = 0; member < members.Count; member++)
        {
            if (pose >= Duration - 1)
            {
                // battle.c 1519-1526: the removal loop carries EVERY member, dead included.
                walking.Add(new(member, 0, new Vector2(9999, 9999)));
                continue;
            }
            if (members[member].Hp == 0) continue;
            (int X, int Y) step = member switch { 0 when members.Count > 1 => (4, 6), 2 => (6, 3), _ => (4, 4) };
            int steps = Math.Min(pose + 1, 16);
            walking.Add(new(member, 0, new Vector2(step.X * steps, step.Y * steps)));
        }
        return walking;
    }
}
