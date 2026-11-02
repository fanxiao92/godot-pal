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
    private uint poseDeadline;
    private int actionPose;
    public StoryBattlePresentation? StoryBattleMode { get; private set; }
    public ClassicBattleLifecycleSession? StoryBattle => storyRequest?.Lifecycle;

    private void InitializeStory(GameSceneConfiguration selected)
    {
        // All capability, hash and native validation completes before publishing children.
        var checkedStory = NativeStoryProfile.Load(selected.StoryProfile!, selected.Language);
        var checkedVisual = NativeBattleVisualProfile.Load(selected.VisualProfile!, checkedStory);
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
        uint now = unchecked((uint)Time.GetTicksMsec());
        if (storyRequest is null)
        {
            if (Dialogue?.Session?.PendingBattle is not { } pending) return false;
            var view = new PalStoryBattleView();
            try { view.Initialize(story, storyVisual!, pending.TeamId); }
            catch { view.ReleaseTextures(); view.Free(); throw; }
            storyRequest = pending; storyView = view; battleConfirm = false;
            StoryBattleMode = StoryBattlePresentation.AttackMenu;
            Display!.SetProcess(false);
            mapScene!.Visible = false; AddChild(view);
            view.Present(pending.Lifecycle.Current.Combat, StoryBattleMode.Value, 0);
            return true;
        }
        var lifecycle = storyRequest.Lifecycle;
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
                if (!lifecycle.ResolveNextAction()) throw new InvalidOperationException("Battle action owner did not resolve an action.");
                StoryBattleMode = StoryBattlePresentation.Feedback;
                actionPose = 0; poseDeadline = unchecked(now + 40);
                break;
            case StoryBattlePresentation.Feedback:
                if (unchecked((int)(poseDeadline - now)) > 0) break;
                actionPose++; poseDeadline = unchecked(now + 40); // late delivery does not skip poses
                var team = storyVisual!.Teams[storyRequest.TeamId];
                int poses = lifecycle.Current.Combat.LastAction!.Attacker == ClassicBattleSide.Player ? 8
                    : Math.Max(8, team.MagicFrames * 2 + Math.Max(1, team.AttackFrames) * Math.Max(1, team.ActWaitFrames));
                if (actionPose < poses) break;
                if (lifecycle.Completion is { } completion)
                {
                    var request = storyRequest;
                    // Remove the complete feedback overlay and restore the map before the owned receipt.
                    RemoveChild(storyView!); storyView!.Free(); storyView = null;
                    mapScene!.Visible = true; RestoreStoryMap();
                    storyRequest = null; StoryBattleMode = null;
                    if (!Dialogue!.AcknowledgeBattle(request.Id, completion))
                        throw new InvalidOperationException("Story battle completion was not owned by the active trigger.");
                    // A child display must not consume this battle callback's delta.
                    resumeStoryDisplay = true;
                    CompleteTrigger();
                    return true;
                }
                StoryBattleMode = lifecycle.Current.Combat.Phase == ClassicBattlePhase.Selecting
                    ? StoryBattlePresentation.AttackMenu : StoryBattlePresentation.Resolving;
                break;
        }
        storyView!.Present(lifecycle.Current.Combat, StoryBattleMode!.Value, actionPose);
        return true;
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
        storyRandom?.Dispose(); storyRandom = null; story = null; storyVisual = null;
    }
}
