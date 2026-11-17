using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Pal.Core;
using Pal.Game.Dialogue;
using Pal.Game.Profiles;
using Pal.Game.Resources;

namespace Pal.Game;

public partial class Main
{
    private CheckedStoryProfile? story;
    private CheckedBattleVisualProfile? storyVisual;
    private CheckedBattleVisualTeam? storyTeam;
    private GodotBattleRandom? storyRandom;
    private PalStoryBattleView? storyView;
    internal Func<PalStoryBattleView> StoryBattleViewFactory { get; set; } = () => new PalStoryBattleView();
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
    internal int StoryRedFadeStep => storyRedStep;
    /// <summary>The red-faded world palette the map currently shows; null outside the fade.</summary>
    internal Color[]? StoryRedPalette => storyRedStep < 0 ? null : RedFadePalette(story!.Palette, storyRedStep);
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
    internal StoryEnemyAttackSnapshot? StoryEnemyPresentation => storyView?.EnemyPresentation;
    internal StoryBattleDrawSnapshot? StoryDrawSnapshot => storyView?.DrawSnapshot;
    internal uint StoryPresentationTicks => presentationNow;
    internal StoryBattlePoseSnapshot? StoryBattlePose => StoryBattleMode == StoryBattlePresentation.Feedback
        ? new(presentationNow, poseClock.PoseStartedAt, poseClock.Pose) : null;
    /// <summary>Per-member sum of the live role-hook boost registers; empty while none applied.</summary>
    internal IReadOnlyList<int> StoryBattleBoosts => storyView?.DrawSnapshot.PartyBoosts ?? [];
    public StoryBattlePresentation? StoryBattleMode { get; private set; }
    public ClassicBattleLifecycleSession? StoryBattle => storyRequest?.Lifecycle;
    /// <summary>The current victory-settlement line, or null outside the Settlement presentation.</summary>
    internal string? StorySettlementLine => StoryBattleMode == StoryBattlePresentation.Settlement
        ? storyView?.SettlementLine : null;
    /// <summary>Story ledger of the granted victory totals (gpGlobals dwCash, battle.c 1054); one
    /// consumption per owned Won receipt, never re-granted by a repeat acknowledgement.</summary>
    public int StoryCashGranted { get; private set; }
    public int StoryExperienceGranted { get; private set; }
    internal int? StoryDisplayedPlayerHp => storyView?.DisplayedPlayerHp;
    internal int? StoryDisplayedEnemyHp => storyView?.DisplayedEnemyHp;
    internal bool StoryResultVisible => storyView?.ResultVisible ?? false;

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
            var interaction = NpcInteractionSession.ForSelectedEntry(checkedStory.Exploration.Rules, checkedStory.Exploration.Leader, checkedStory.Entry);
            storyRandom = new GodotBattleRandom(selected.BattleSeed);
            display = new PalDialogueDisplay();
            dialogue = new PalTriggerDialogue();
            roleLiveState = new RoleLiveState(checkedStory.BattleContent.InitialRoles.Values.ToArray());
            dialogue.FirstBattleWorld = DynamicEventWorld.ForHost(checkedStory.Exploration.Rules,
                checkedStory.Exploration.Leader, map.Data.SceneId, map.Data.WScriptOnEnter, map.Data.WScriptOnTeleport,
                roleLiveState, checkedStory.BattleContent.Players.Select(player => player.RoleId).ToArray());
            dialogue.Initialize(display, checkedStory, storyRandom);
            story = checkedStory; storyVisual = checkedVisual; mapScene = map;
            Interaction = interaction; Display = display; Dialogue = dialogue; SelectedLanguage = selected.Language;
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
    internal bool StoryFleeFeedback => fleeOrigin is not null;
    /// <summary>True while the successful-escape walk-off presents (battle.c 1458-1528): every
    /// living member leaves the screen before the battle releases with its owned receipt.</summary>
    internal bool StoryEscapeFeedback => fleeOrigin is not null && fleeEscaped;
    /// <summary>palette.c PAL_FadeToRed (script.c 0x004F game-over fade): every palette entry
    /// except the preserved 0x4F text color moves red by 8 per step for 32 steps of 75ms;
    /// this is the step-<paramref name="step"/> palette of that reference convergence.</summary>
    internal static Color[] RedFadePalette(IReadOnlyList<Color> palette, int step)
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
    }
}

/// <summary>fight.c 4153-4171 failed-escape presentation: three +4/+2 steps then the escape-fail
/// label holds for eight frames; poses advance on the shared 40ms battle pose clock.</summary>
internal static class StoryPlayerFleePresentation
{
    internal const int LabelPose = 3;
    internal const long Duration = 11;
    internal static StoryPlayerFleeSnapshot At(int pose, Vector2 origin, int partyIndex) =>
        new(partyIndex, pose < LabelPose ? 0 : 1,
            new Vector2(4 * Math.Min(pose + 1, LabelPose), 2 * Math.Min(pose + 1, LabelPose)));
}

/// <summary>battle.c 1458-1528 PAL_BattlePlayerEscape: every living member walks off together
/// for sixteen frames (party-column steps +4/+6, +4/+4 or +6/+3, battle.c 1493-1516), then all
/// players — the dead included — leave the screen (pos 9999, battle.c 1519-1526); poses advance
/// on the shared 40ms clock. Only the living walk; the dead stay visible at their home
/// positions through the walk-off (fight.c 952-957 keeps drawing them) and leave with the
/// party at the final removal frame.</summary>
internal static class StoryPlayerEscapePresentation
{
    internal const long Duration = 17;
    // The snapshots carry only the relative walk-off offset; the view adds each member's own
    // visual origin (PlayerOrigins columns) when drawing.
    internal static IReadOnlyList<StoryPlayerFleeSnapshot> At(int pose, IReadOnlyList<ClassicBattlePlayerView> members)
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
