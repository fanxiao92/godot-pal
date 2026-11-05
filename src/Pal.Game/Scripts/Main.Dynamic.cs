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

/// <summary>#52 no-combat dynamic-scene host: DynamicEventSession owns exploration, the selected
/// trigger call and update-driven auto scripts; Main only routes input, drives the real update
/// callback, binds displays and receipts host presentation transitions.</summary>
public partial class Main
{
    private DynamicEventSession? dynamicSession;
    internal PalDialogueFont? dynamicFont;
    internal PalDialogueIndexedImage[]? dynamicPrompts;
    internal IReadOnlyDictionary<int, PalDialogueIndexedImage>? dynamicPortraits;
    private Color[]? dynamicPalette;
    private bool dynamicDrawn;      // searched pose drawn; the trigger may start
    private uint dynamicTransitionDeadline;
    private Guid dynamicBoundDisplay; // the display request already attached to the display

    public DynamicEventSession? Dynamic => dynamicSession;

    private void InitializeDynamic(GameSceneConfiguration selected)
    {
        // The authored story package stays the checked content chain; the dynamic session replaces
        // the battle story flow, so no battle runs and no battle capability is claimed.
        var checkedStory = NativeStoryProfile.Load(selected.DynamicProfile!, selected.Language);
        var map = (PalMapScene)checkedStory.Scene.Instantiate();
        PalDialogueDisplay? display = null;
        try
        {
            var session = new DynamicEventSession(checkedStory.Exploration.Rules, checkedStory.Exploration.Leader,
                checkedStory.Events, checkedStory.Language, checkedStory.Entry,
                map.Data!.SceneId, map.Data.WScriptOnEnter, map.Data.WScriptOnTeleport);
            checkedStory.Exploration.SetPalette(map.Data, checkedStory.Palette.ToArray());
            map.InitializeScene(checkedStory.Exploration.Plan, checkedStory.Exploration.TextureFor);
            display = new PalDialogueDisplay();
            display.Failed += DisplayFailed;
            display.ActivePaletteChanged += colors =>
            {
                if (Failure is null || exited) return;
                checkedStory.Exploration.SetPalette(map.Data, colors);
                if (dynamicSession is { } dynamic) map.UpdateScene(dynamic.Current.Plan);
            };
            display.BackgroundRestoreRequested += () =>
            {
                if (Failure is null && !exited && dynamicSession is { } dynamic) map.UpdateScene(dynamic.Current.Plan);
            };
            display.UpdateCompleted += DynamicDisplayUpdated;
            dynamicSession = session;
            dynamicFont = checkedStory.Font;
            dynamicPrompts = checkedStory.Prompts.ToArray();
            dynamicPortraits = checkedStory.Portraits;
            dynamicPalette = checkedStory.Palette.ToArray();
            mapScene = map;
            Display = display;
            SelectedLanguage = selected.Language;
            AddChild(map); AddChild(display);
            RenderingServer.FramePostDraw += StartDynamicAfterSceneDraw; rendererSubscribed = true;
            GD.Print($"PAL_DYNAMIC_SCENE_LOADED:{selected.DynamicProfile}; language={selected.Language}; battle content is unused data");
            GD.Print($"PAL_MAP_LOADED:{checkedStory.StaticResources}/Scenes/Scene.tscn");
            GD.Print($"PAL_GAME_READY:{Pal.Core.Startup.Status}");
        }
        catch
        {
            dynamicSession = null; dynamicFont = null; dynamicPrompts = null; dynamicPortraits = null; dynamicPalette = null;
            if (display is not null && GodotObject.IsInstanceValid(display))
            {
                display.Failed -= DisplayFailed;
                display.UpdateCompleted -= DynamicDisplayUpdated;
                display.GetParent()?.RemoveChild(display); display.Free();
            }
            if (GodotObject.IsInstanceValid(map)) { map.GetParent()?.RemoveChild(map); map.Free(); }
            mapScene = null; Display = null;
            throw;
        }
    }

    /// <summary>Runs one real callback: the session's due tick (single advancing owner), then the
    /// host's own presentation work (display binding, transition redraw/receipt), then redraw.</summary>
    private void ProcessDynamicScene()
    {
        var dynamic = dynamicSession!;
        bool changed = dynamic.Update(unchecked((uint)Time.GetTicksMsec()));
        BindDynamicDisplay();
        CompleteDynamicTransition();
        if (dynamic.PendingTrigger is null && dynamic.Trigger is null) dynamicDrawn = false;
        else if (dynamic.PendingTrigger is not null && !dynamicDrawn)
        {
            // PAL_Search redraws the searched pose; the call starts only after that draw.
            // The headless runner has no presented frame, so the redraw boundary is the callback.
            if (DisplayServer.GetName() == "headless") StartDynamicTrigger();
        }
        if (changed) mapScene!.UpdateScene(dynamic.Current.Plan);
    }

    private void StartDynamicTrigger()
    {
        if (dynamicSession is not { } dynamic || dynamic.PendingTrigger is not { } request) return;
        try
        {
            dynamicDrawn = true;
            _ = dynamic.StartTrigger(request.Id);
            BindDynamicDisplay();
        }
        catch (Exception e) { Fail($"Dynamic trigger failed: {e.Message}"); }
    }

    private void BindDynamicDisplay()
    {
        if (dynamicSession?.Trigger?.PendingDisplay is not { } request)
        {
            dynamicBoundDisplay = Guid.Empty;
            return;
        }
        // Attaching resets the display's input presses; bind each request exactly once.
        if (dynamicBoundDisplay == request.Id) return;
        dynamicBoundDisplay = request.Id;
        var display = Display!;
        var portrait = request.Context.PortraitId == 0 ? null
            : dynamicPortraits!.TryGetValue(request.Context.PortraitId, out var found) ? found : null;
        try { display.Start(request, dynamicFont!, portrait, dynamicPrompts!, dynamicPalette!); }
        catch (InvalidDataException error) { Fail($"{request.MessageReferences[0].Position}: {error.Message}"); return; }
        if (request.Display.Current.Phase != DialogueDisplayPhase.Completed) return;
        // A synchronously completed display still carries its restoration effect.
        if (dynamicSession.Trigger.AcknowledgeDisplay(request.Id)) BindDynamicDisplay();
    }

    private void DynamicDisplayUpdated()
    {
        if (dynamicSession?.Trigger?.PendingDisplay is not { } request
            || request.Display.Current.Phase != DialogueDisplayPhase.Completed) return;
        if (dynamicSession.Trigger.AcknowledgeDisplay(request.Id)) BindDynamicDisplay();
    }

    /// <summary>Host presentation for the parked 0x0005/0x0073 transition: redraw the retained
    /// scene, then receipt once the redraw delay (60ms units) has actually elapsed.</summary>
    private void CompleteDynamicTransition()
    {
        if (dynamicSession?.Trigger?.PendingTransition is not { } transition)
        {
            dynamicTransitionDeadline = 0;
            return;
        }
        uint now = unchecked((uint)Time.GetTicksMsec());
        if (dynamicTransitionDeadline == 0)
        {
            // redraw carries its 60ms-unit delay; the fade speed word gets the frame boundary.
            int delay = transition.Kind == "redraw" ? Math.Max(60, transition.Parameter) : 60;
            dynamicTransitionDeadline = unchecked(now + (uint)delay);
            mapScene!.UpdateScene(dynamicSession.Current.Plan); // BackupScreen + MakeScene boundary
            return;
        }
        if (unchecked((int)(now - dynamicTransitionDeadline)) < 0) return;
        dynamicTransitionDeadline = 0;
        _ = dynamicSession.Trigger.AcknowledgeTransition(transition.Id);
    }

    private void StartDynamicAfterSceneDraw()
    {
        if (Failure is not null || exited || !dynamicDrawn) return;
        StartDynamicTrigger();
    }

    private void ReleaseDynamic()
    {
        if (Display is not null && dynamicSession is not null) Display.UpdateCompleted -= DynamicDisplayUpdated;
        dynamicSession = null; dynamicFont = null; dynamicPrompts = null; dynamicPortraits = null;
        dynamicPalette = null; dynamicDrawn = false; dynamicTransitionDeadline = 0; dynamicBoundDisplay = Guid.Empty;
    }
}
