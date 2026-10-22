using System;
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
    private PalMapScene? mapScene;
    private NpcInteractionRequest? activeTrigger;
    private bool awaitingSceneDraw;
    private bool rendererSubscribed;
    public ExplorationState? Exploration => Interaction?.Current ?? input?.Current;
    public NpcInteractionSession? Interaction { get; private set; }
    public PalTriggerDialogue? Dialogue { get; private set; }
    public PalDialogueDisplay? Display { get; private set; }
    // Actual Main callback completion; display completion has its own production boundary.
    public event Action? UpdateCompleted;

    public override void _Ready()
    {
        string? scenePath = null, profilePath = null;
        try
        {
            GetWindow().MinSize = new Vector2I(320, 200);
            var (root, profile, staticAcceptance, dialogueProfile, language) = NativeSceneLoader.Arguments(OS.GetCmdlineUserArgs());
            PalNpcInteractionProfile? npc = null;
            if (dialogueProfile is not null)
            {
                profilePath = dialogueProfile;
                npc = NativeNpcInteractionProfile.Load(dialogueProfile);
                root = npc.StaticResources; profile = npc.StaticProfile;
            }
            scenePath = root + "/Scenes/Scene.tscn";
            profilePath = profile;
            var map = NativeSceneLoader.Load(root);
            CheckedExplorationProfile checkedProfile;
            try
            {
                checkedProfile = NativeExplorationProfile.Load(profile, map.Data);
                if (staticAcceptance || npc is not null) ValidateStaticAcceptance(map.Data, checkedProfile);
                if (npc is not null)
                {
                    ValidateDialogueScene(map.Data, npc);
                    checkedProfile.SetPalette(map.Data, Palette(map.Data));
                }
                if (npc is null) input = new ExplorationInput(new ExplorationSession(checkedProfile.Rules, checkedProfile.Leader));
                map.InitializeScene(checkedProfile.Plan, checkedProfile.TextureFor);
            }
            catch { map.Free(); throw; }
            mapScene = map;
            AddChild(map);
            if (npc is not null)
            {
                Display = new PalDialogueDisplay(); AddChild(Display);
                Dialogue = new PalTriggerDialogue(); AddChild(Dialogue);
                Display.ActivePaletteChanged += colors =>
                {
                    checkedProfile.SetPalette(map.Data, colors);
                    map.UpdateScene(Exploration!.Plan);
                };
                // Ordinary dialogue blocks exploration. Redraw the retained current scene, not
                // the initial pose or a screenshot; the display removes its own overlay.
                Display.BackgroundRestoreRequested += () => map.UpdateScene(Exploration!.Plan);
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
            if (staticAcceptance) GD.Print("PAL_M3_STATIC_ACCEPTANCE:scene4/map10; events are not scheduled");
            GD.Print($"PAL_GAME_READY:{Pal.Core.Startup.Status}");
        }
        catch (Exception e)
        {
            Fail($"Static scene startup failed (scene={scenePath ?? "unselected"}, profile={profilePath ?? "unselected"}): {e.Message}");
        }
    }

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
        if (Exploration is null || @event is not InputEventKey key) return;
        for (int i = 0; i < DirectionKeys.Length; i++)
        {
            if (key.Keycode != DirectionKeys[i] || !key.IsAction(DirectionActions[i / 2])) continue;
            if (Interaction is { } interaction)
            {
                if (key.Pressed) interaction.Press((ExplorationKey)i, key.Echo);
                else interaction.Release((ExplorationKey)i);
            }
            else if (key.Pressed) input!.Press((ExplorationKey)i, key.Echo);
            else input!.Release((ExplorationKey)i);
            return;
        }
        // Windows reference EnableKeyRepeat=false; no held search repeater or fresh-press gate.
        if (Interaction is { Phase: not NpcInteractionPhase.Trigger } npc && key.Pressed && !key.Echo
            && (key.Keycode is Key.Enter or Key.Space or Key.KpEnter || key.Keycode == Key.Ctrl && key.Location != KeyLocation.Right))
        {
            npc.PressSearch();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (Exploration is null || mapScene is null) return;
        try
        {
            // The headless dummy renderer has no presented frame. Preserve the command/redraw
            // submission boundary across actual callbacks; graphical runs wait for FramePostDraw.
            if (awaitingSceneDraw && DisplayServer.GetName() == "headless") StartAfterSceneDraw();
            bool changed = Interaction?.Update(unchecked((uint)Time.GetTicksMsec()))
                ?? input!.Update(unchecked((uint)Time.GetTicksMsec()));
            if (changed) mapScene.UpdateScene(Exploration.Plan);
            if (activeTrigger is null && Interaction?.PendingTrigger is { } request)
            {
                activeTrigger = request;
                awaitingSceneDraw = true;
            }
            UpdateCompleted?.Invoke();
        }
        catch (Exception e) { Fail($"Exploration update failed: {e.Message}"); }
    }

    private void StartAfterSceneDraw()
    {
        if (!awaitingSceneDraw || activeTrigger is not { } request) return;
        awaitingSceneDraw = false;
        try
        {
            Dialogue!.Start(request.FileId, request.EntryId, request.Caller);
            CompleteTrigger(); // Also handles synchronous, no-wait completion.
        }
        catch (Exception e) { Fail($"NPC trigger failed: {e.Message}"); }
    }

    private void CompleteTrigger()
    {
        if (activeTrigger is not { } request || Dialogue?.Session is not { Phase: TriggerScriptPhase.Completed } session) return;
        // This callback follows the adapter's receipt at the same actual display update, not
        // a later Main observation frame. Palette/background consumers have already completed.
        if (!Interaction!.CompleteTrigger(request.Id, session, unchecked((uint)Time.GetTicksMsec())))
            throw new InvalidOperationException("NPC trigger completion does not match the active search request");
        activeTrigger = null;
    }

    private void Fail(string message)
    {
        awaitingSceneDraw = false;
        SetProcess(false);
        GD.PrintErr(message);
        GetTree().Quit(1);
    }

    public override void _ExitTree()
    {
        if (rendererSubscribed) RenderingServer.FramePostDraw -= StartAfterSceneDraw;
        if (Display is not null) Display.UpdateCompleted -= CompleteTrigger;
    }
}
