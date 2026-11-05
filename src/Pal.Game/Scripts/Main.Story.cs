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
    private TriggerBattleRequest? storyRequest;
    private bool battleConfirm;
    // Enemy-slot highlight while a member picks a target; reference selection has no
    // previous-target memory (fixed initial -1, UI update disabled).
    private int storyTargetSlot;
    private int storyCycle;
    private ClassicBattleAction? storyLastPresentedAction;
    // #51 boss flee: the pending Q press and the newest presented flee attempt.
    private bool storyFlee;
    private ClassicBattleFleeAttempt? storyLastFleeAttempt;
    private Vector2? fleeOrigin;
    private int fleePartyIndex = -1;
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
    private StoryPlayerAttackPresentation? playerPresentation;
    private int effectEnemyHeight;
    private StoryEnemyIdleState? enemyIdle;
    private List<string>? settlementLines;
    private int settlementStage;
    private uint settlementShownAt;
    private bool settlementPresented;
    private StoryBattlePoseClock outerClock;
    private bool outerDue;
    private readonly StoryBattleNumbers storyNumbers = new();
    private bool feedbackBorn;
    private int feedbackBeforeHp;
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
            var view = new PalStoryBattleView();
            try { view.Initialize(story, storyVisual!, pending.EnemyTeamId); }
            catch { view.ReleaseTextures(); view.Free(); throw; }
            var team = storyVisual!.Teams[pending.EnemyTeamId];
            storyTeam = team;
            enemyIdle = new(team.IdleFrames, team.IdleAnimSpeed);
            // battle.c1782 startup, then the immediately due first StartFrame (battle.c763–792).
            enemyIdle.Advance(); enemyIdle.Advance();
            outerClock = StoryBattlePoseClock.Start(now); outerDue = false;
            storyRequest = pending; storyView = view; battleConfirm = false; settlementPresented = false;
            storyTargetSlot = 0; storyCycle = 0; storyLastPresentedAction = null;
            StoryBattleMode = StoryBattlePresentation.AttackMenu;
            Display!.SetProcess(false);
            mapScene!.Visible = false; AddChild(view);
            view.Present(pending.Lifecycle.Current.Combat, StoryBattleMode.Value, enemyIdle.Frame, enemyIdle.Counter, highlightedEnemySlot: storyTargetSlot);
            return true;
        }
        var lifecycle = storyRequest.Lifecycle;
        if (StoryBattleMode != StoryBattlePresentation.Feedback)
        {
            var nextOuter = outerClock.Advance(now);
            if (outerDue || nextOuter != outerClock)
            {
                enemyIdle!.Advance(); outerDue = false; outerClock = StoryBattlePoseClock.Start(now);
            }
        }
        bool confirm = battleConfirm; battleConfirm = false;
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
                if (confirm)
                {
                    var enemies = lifecycle.Current.Combat.Enemies;
                    // No previous-target memory: each member starts from the first alive slot.
                    storyTargetSlot = Enumerable.Range(0, enemies.Count).First(slot => enemies[slot].Hp > 0);
                    StoryBattleMode = StoryBattlePresentation.TargetMenu;
                }
                break;
            case StoryBattlePresentation.TargetMenu:
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
                    // fight.c 4153-4171: three +4/+2 escape steps, then the failed-escape label
                    // holds for eight frames (BATTLE_LABEL_ESCAPEFAIL, fight.c 4168).
                    storyNumbers.BeginAction(); feedbackBorn = true;
                    var origins = storyVisual!.PlayerOrigins(lifecycle.Current.Combat.Players.Count);
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
                var combat = lifecycle.Current.Combat;
                var playerOrigins = storyVisual!.PlayerOrigins(combat.Players.Count);
                enemyPresentation = action.Attacker != ClassicBattleSide.Enemy ? null : new(
                    storyTeam!.IdleFrames, storyTeam.MagicFrames, storyTeam.AttackFrames, storyTeam.ActWaitFrames,
                    playerOrigins[action.TargetIndex], storyTeam.EnemySlots[action.ActorIndex].EnemyPosition, beforeHp,
                    combat.Players[action.TargetIndex].Hp, story.BattleContent.Players[action.TargetIndex].MaxHp,
                    action, enemyIdle!.Frame);
                playerPresentation = action.Attacker != ClassicBattleSide.Player ? null : new(
                    playerOrigins[action.ActorIndex], storyTeam!.EnemySlots[action.TargetIndex].EnemyPosition,
                    beforeEnemyHp, combat.Enemies[action.TargetIndex].Hp,
                    StoryEnemyAttackPresentation.RestFrame(combat.Players[action.ActorIndex].Hp,
                        story.BattleContent.Players[action.ActorIndex].MaxHp));
                StoryBattleMode = StoryBattlePresentation.Feedback;
                poseClock = StoryBattlePoseClock.Start(now);
                AdvanceStoryGesture(0);
                break;
            }
            case StoryBattlePresentation.Feedback:
                var nextPose = poseClock.Advance(now);
                if (nextPose == poseClock) break;
                poseClock = nextPose;
                long poses = fleeOrigin is not null ? StoryPlayerFleePresentation.Duration
                    : enemyPresentation?.Duration ?? StoryPlayerAttackPresentation.Duration;
                if (poseClock.Pose < poses) AdvanceStoryGesture(poseClock.Pose);
                if (poseClock.Pose < poses) break;
                // Enemy action's final UpdateFighters is not another timed pose (fight.c5137).
                if (enemyPresentation is not null) enemyIdle!.Advance();
                if (fleeOrigin is not null) fleeOrigin = null;
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
        if (shownEnemy is { } shown) shownEnemy = shown with { EnemyFrame = enemyIdle!.Frame };
        var fleeSnapshot = StoryBattleMode == StoryBattlePresentation.Feedback && fleeOrigin is { } fleeAt
            ? (StoryPlayerFleeSnapshot?)StoryPlayerFleePresentation.At(poseClock.Pose, fleeAt, fleePartyIndex) : null;
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
                Vector2 target = playerHit ? storyTeam!.EnemySlots[action.TargetIndex].EnemyPosition
                    : origins[action.TargetIndex];
                target.Y = Math.Max(10, target.Y - (playerHit ? 115 : 75));
                storyNumbers.EnqueueAction(feedbackBeforeHp, action, target, now, pending: true);
                feedbackBorn = true;
            }
            if ((playerPresentation is not null && poseClock.Pose >= 8) || shownEnemy is { ResultVisible: true })
                storyNumbers.RevealPending();
        }
        string? settlementLine = StoryBattleMode == StoryBattlePresentation.Settlement && settlementLines is { } current
            ? current[Math.Clamp(settlementStage, 0, current.Count - 1)] : null;
        storyView!.Present(lifecycle.Current.Combat, StoryBattleMode!.Value, enemyIdle!.Frame, enemyIdle.Counter,
            shownEnemy, StoryBattleMode == StoryBattlePresentation.Feedback ? playerPresentation?.At(poseClock.Pose, effectEnemyHeight) : null,
            storyNumbers.At(now), fleeSnapshot is null ? storyNumbers.LabelAt(now)
                : poseClock.Pose >= StoryPlayerFleePresentation.LabelPose ? PalStoryBattleView.EscapeFailWord : null,
            storyTargetSlot, settlementLine, fleeSnapshot);
        return true;
    }
    /// <summary>True while the failed-escape feedback presents (authored: the scene freezes).</summary>
    internal bool StoryFleeFeedback => fleeOrigin is not null;
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
            enemyIdle!.Frame, enemyIdle!.Counter, highlightedEnemySlot: storyTargetSlot);
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
            enemyIdle!.Frame, enemyIdle!.Counter, highlightedEnemySlot: storyTargetSlot);
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
            enemyIdle!.Frame, enemyIdle!.Counter, highlightedEnemySlot: storyTargetSlot,
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
    }

    private void AdvanceStoryGesture(int pose)
    {
        if (playerPresentation is not null)
        {
            // Four TRUE windup, three TRUE approach, three manual FX, three TRUE recoil,
            // UpdateFighters at13 followed by three TRUE tail frames.
            // ShowPlayerAttackAnim reads target height after windup, before its first TRUE approach delay.
            if (pose == 4) effectEnemyHeight = storyTeam!.EnemySprite.Frames
                .Single(f => f.Index == enemyIdle!.Frame).Height;
            if (pose == 13) enemyIdle!.Advance();
            enemyIdle!.Advance();
        }
        else if (enemyPresentation is { } enemy)
        {
            long tail = enemy.Duration - 5;
            if (pose < tail) enemyIdle!.SetFrame(enemy.At(pose).EnemyFrame);
            else enemyIdle!.Advance();
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
        storyView = null; storyRequest = null; storyTeam = null; StoryBattleMode = null; battleConfirm = false; resumeStoryDisplay = false;
        enemyPresentation = null; playerPresentation = null; enemyIdle = null; outerDue = false;
        storyTargetSlot = 0; storyCycle = 0; storyLastPresentedAction = null;
        storyFlee = false; storyLastFleeAttempt = null; fleeOrigin = null; fleePartyIndex = -1; storyRedStep = -1;
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
