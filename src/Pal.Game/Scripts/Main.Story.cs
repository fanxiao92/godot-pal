using System;
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
    private GodotBattleRandom? storyRandom;
    private PalStoryBattleView? storyView;
    private TriggerBattleRequest? storyRequest;
    private bool battleConfirm;
    private bool resumeStoryDisplay;
    private StoryBattlePoseClock poseClock;
    private uint presentationNow;
    private StoryEnemyAttackPresentation? enemyPresentation;
    private StoryPlayerAttackPresentation? playerPresentation;
    private int effectEnemyHeight;
    private StoryEnemyIdleState? enemyIdle;
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
    public StoryBattlePresentation? StoryBattleMode { get; private set; }
    public ClassicBattleLifecycleSession? StoryBattle => storyRequest?.Lifecycle;
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
            try { view.Initialize(story, storyVisual!, pending.TeamId); }
            catch { view.ReleaseTextures(); view.Free(); throw; }
            var team = storyVisual!.Teams[pending.TeamId];
            enemyIdle = new(team.IdleFrames, team.IdleAnimSpeed);
            // battle.c1782 startup, then the immediately due first StartFrame (battle.c763–792).
            enemyIdle.Advance(); enemyIdle.Advance();
            outerClock = StoryBattlePoseClock.Start(now); outerDue = false;
            storyRequest = pending; storyView = view; battleConfirm = false;
            StoryBattleMode = StoryBattlePresentation.AttackMenu;
            Display!.SetProcess(false);
            mapScene!.Visible = false; AddChild(view);
            view.Present(pending.Lifecycle.Current.Combat, StoryBattleMode.Value, enemyIdle.Frame, enemyIdle.Counter);
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
                if (confirm) StoryBattleMode = StoryBattlePresentation.TargetMenu;
                break;
            case StoryBattlePresentation.TargetMenu:
                if (confirm)
                {
                    lifecycle.SelectAttack(storyVisual!.Teams[storyRequest.TeamId].TargetId);
                    StoryBattleMode = StoryBattlePresentation.Resolving;
                }
                break;
            case StoryBattlePresentation.Resolving:
                int beforeHp = lifecycle.Current.Combat.PlayerHp;
                int beforeEnemyHp = lifecycle.Current.Combat.EnemyHp;
                if (!lifecycle.ResolveNextAction()) throw new InvalidOperationException("Battle action owner did not resolve an action.");
                var action = lifecycle.Current.Combat.LastAction!;
                storyNumbers.BeginAction(); feedbackBorn = false;
                feedbackBeforeHp = action.Attacker == ClassicBattleSide.Player ? beforeEnemyHp : beforeHp;
                var actingTeam = storyVisual!.Teams[storyRequest.TeamId];
                enemyPresentation = action.Attacker != ClassicBattleSide.Enemy ? null : new(
                    actingTeam.IdleFrames, actingTeam.MagicFrames, actingTeam.AttackFrames, actingTeam.ActWaitFrames,
                    storyVisual.PlayerPosition, actingTeam.EnemyPosition, beforeHp,
                    lifecycle.Current.Combat.PlayerHp, story.BattleContent.Player.MaxHp, action, enemyIdle!.Frame);
                playerPresentation = action.Attacker != ClassicBattleSide.Player ? null : new(
                    storyVisual.PlayerPosition, actingTeam.EnemyPosition,
                    beforeEnemyHp, lifecycle.Current.Combat.EnemyHp,
                    StoryEnemyAttackPresentation.RestFrame(lifecycle.Current.Combat.PlayerHp, story.BattleContent.Player.MaxHp));
                StoryBattleMode = StoryBattlePresentation.Feedback;
                poseClock = StoryBattlePoseClock.Start(now);
                AdvanceStoryGesture(0);
                break;
            case StoryBattlePresentation.Feedback:
                var nextPose = poseClock.Advance(now);
                if (nextPose == poseClock) break;
                poseClock = nextPose;
                long poses = enemyPresentation?.Duration ?? StoryPlayerAttackPresentation.Duration;
                if (poseClock.Pose < poses) AdvanceStoryGesture(poseClock.Pose);
                if (poseClock.Pose < poses) break;
                // Enemy action's final UpdateFighters is not another timed pose (fight.c5137).
                if (enemyPresentation is not null) enemyIdle!.Advance();
                if (lifecycle.Completion is { } completion)
                {
                    var request = storyRequest;
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
                    return true;
                }
                // The outer deadline set before the blocking action is already overdue.
                outerDue = true;
                StoryBattleMode = lifecycle.Current.Combat.Phase == ClassicBattlePhase.Selecting
                    ? StoryBattlePresentation.AttackMenu : StoryBattlePresentation.Resolving;
                break;
        }
        var shownEnemy = StoryBattleMode == StoryBattlePresentation.Feedback ? enemyPresentation?.At(poseClock.Pose) : null;
        if (shownEnemy is { } shown) shownEnemy = shown with { EnemyFrame = enemyIdle!.Frame };
        if (StoryBattleMode == StoryBattlePresentation.Feedback)
        {
            var action = lifecycle.Current.Combat.LastAction!;
            bool playerHit = playerPresentation is not null && poseClock.Pose >= 7;
            // Enemy stat-change queues after the last prehit UIUpdate, before the impact
            // Delay(FALSE) waits40ms. Preserve that real birth even if the next callback is late.
            bool enemyEnqueue = enemyPresentation is { } enemy && poseClock.Pose >= enemy.ImpactPose - 1;
            if (!feedbackBorn && (playerHit || enemyEnqueue))
            {
                Vector2 target = playerHit ? storyVisual!.Teams[storyRequest.TeamId].EnemyPosition : storyVisual!.PlayerPosition;
                target.Y = Math.Max(10, target.Y - (playerHit ? 115 : 75));
                storyNumbers.EnqueueAction(feedbackBeforeHp, action, target, now, pending: true);
                feedbackBorn = true;
            }
            if ((playerPresentation is not null && poseClock.Pose >= 8) || shownEnemy is { ResultVisible: true })
                storyNumbers.RevealPending();
        }
        storyView!.Present(lifecycle.Current.Combat, StoryBattleMode!.Value, enemyIdle!.Frame, enemyIdle.Counter,
            shownEnemy, StoryBattleMode == StoryBattlePresentation.Feedback ? playerPresentation?.At(poseClock.Pose, effectEnemyHeight) : null,
            storyNumbers.At(now), storyNumbers.LabelAt(now));
        return true;
    }
    private void AdvanceStoryGesture(int pose)
    {
        if (playerPresentation is not null)
        {
            // Four TRUE windup, three TRUE approach, three manual FX, three TRUE recoil,
            // UpdateFighters at13 followed by three TRUE tail frames.
            // ShowPlayerAttackAnim reads target height after windup, before its first TRUE approach delay.
            if (pose == 4) effectEnemyHeight = storyVisual!.Teams[storyRequest!.TeamId].EnemySprite.Frames
                .Single(f => f.Index == enemyIdle!.Frame).Height;
            if (pose == 13) enemyIdle!.Advance();
            enemyIdle!.Advance();
        }
        else
        {
            long tail = enemyPresentation!.Duration - 5;
            if (pose < tail) enemyIdle!.SetFrame(enemyPresentation.At(pose).EnemyFrame);
            else enemyIdle!.Advance();
        }
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
        storyView = null; storyRequest = null; StoryBattleMode = null; battleConfirm = false; resumeStoryDisplay = false;
        enemyPresentation = null; playerPresentation = null; enemyIdle = null; outerDue = false;
        storyNumbers.Clear(); feedbackBorn = false;
        storyRandom?.Dispose(); storyRandom = null; story = null; storyVisual = null;
    }
}
