using System;
using Godot;
using Pal.Core;
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
    private Window? window;
    private bool focusSignalsConnected;
    public ExplorationState? Exploration => input?.Current;

    public override void _Ready()
    {
        string? scenePath = null, profilePath = null;
        try
        {
            window = GetWindow();
            window.MinSize = new Vector2I(320, 200);
            var (root, profile) = NativeSceneLoader.Arguments(OS.GetCmdlineUserArgs());
            scenePath = root + "/Scenes/Scene.tscn";
            profilePath = profile;
            var map = NativeSceneLoader.Load(root);
            try
            {
                var checkedProfile = NativeExplorationProfile.Load(profile, map.Data);
                input = new ExplorationInput(new ExplorationSession(checkedProfile.Rules, checkedProfile.Leader));
                map.InitializeScene(input.Current.Plan, checkedProfile.TextureFor);
            }
            catch
            {
                map.Free();
                throw;
            }
            mapScene = map;
            AddChild(map);
            window.FocusEntered += OnFocusEntered;
            window.FocusExited += OnFocusExited;
            focusSignalsConnected = true;
            input.SetFocused(window.HasFocus());
            GD.Print($"PAL_MAP_LOADED:{root}/Scenes/Scene.tscn");
            GD.Print($"PAL_GAME_READY:{Pal.Core.Startup.Status}");
        }
        catch (Exception e)
        {
            GD.PrintErr($"Static scene startup failed (scene={scenePath ?? "unselected"}, profile={profilePath ?? "unselected"}): {e.Message}");
            SetProcess(false);
            GetTree().Quit(1);
        }
    }

    private void OnFocusEntered() => input?.SetFocused(true);
    private void OnFocusExited() => input?.SetFocused(false);

    public override void _ExitTree()
    {
        if (window == null || !focusSignalsConnected) return;
        window.FocusEntered -= OnFocusEntered;
        window.FocusExited -= OnFocusExited;
        focusSignalsConnected = false;
    }

    public override void _Input(InputEvent @event)
    {
        if (input == null || @event is not InputEventKey key) return;
        for (int i = 0; i < DirectionKeys.Length; i++)
        {
            if (key.Keycode != DirectionKeys[i] || !key.IsAction(DirectionActions[i / 2])) continue;
            if (key.Pressed) input.Press((ExplorationKey)i, key.Echo);
            else input.Release((ExplorationKey)i);
            return;
        }
    }

    public override void _Process(double delta)
    {
        if (input == null || mapScene == null) return;
        try
        {
            if (input.Update(unchecked((uint)Time.GetTicksMsec())))
                mapScene.UpdateScene(input.Current.Plan);
        }
        catch (Exception e)
        {
            SetProcess(false);
            GD.PrintErr($"Exploration update failed: {e.Message}");
            GetTree().Quit(1);
        }
    }
}
