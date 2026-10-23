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
    private GameSceneConfiguration? configuration;
    private bool initializationAttempted;
    private bool exited;
    public ExplorationState? Exploration => input?.Current;
    public string? Failure { get; private set; }
    public event Action<string>? Failed;
    // Emitted only after this real Main callback and renderer update have completed.
    public event Action? UpdateCompleted;

    /// <summary>Configure once before tree entry. The host owns this node even on failure.
    /// Initialization occurs in _Ready; failed or removed scenes must be freed, not reused.</summary>
    public void Configure(GameSceneConfiguration selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (IsInsideTree() || configuration is not null || initializationAttempted || exited || Failure is not null)
            throw new InvalidOperationException("Configure the scene exactly once before entering the tree.");
        configuration = selection;
    }

    public override void _Ready()
    {
        if (initializationAttempted || exited || Failure is not null) return;
        initializationAttempted = true;
        string? scenePath = null, profilePath = null;
        try
        {
            window = GetWindow();
            var selected = configuration ?? throw new InvalidOperationException("Configure the scene before entering the tree.");
            string root = selected.Root, profile = selected.Profile;
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
            Fail($"Static scene startup failed (scene={scenePath ?? "unselected"}, profile={profilePath ?? "unselected"}): {e.Message}");
        }
    }

    private void OnFocusEntered() => input?.SetFocused(true);
    private void OnFocusExited() => input?.SetFocused(false);

    public override void _ExitTree()
    {
        exited = true;
        DisconnectFocus();
    }

    private void DisconnectFocus()
    {
        if (window == null || !focusSignalsConnected) return;
        window.FocusEntered -= OnFocusEntered;
        window.FocusExited -= OnFocusExited;
        focusSignalsConnected = false;
    }

    public override void _Input(InputEvent @event)
    {
        if (Failure is not null || exited || input == null || @event is not InputEventKey key) return;
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
        if (Failure is not null || exited || input == null || mapScene == null) return;
        try
        {
            if (input.Update(unchecked((uint)Time.GetTicksMsec())))
                mapScene.UpdateScene(input.Current.Plan);
            UpdateCompleted?.Invoke();
        }
        catch (Exception e)
        {
            Fail($"Exploration update failed: {e.Message}");
        }
    }

    protected void Fail(string message)
    {
        if (Failure is not null || exited) return;
        Failure = message;
        ProcessMode = ProcessModeEnum.Disabled;
        SetProcess(false);
        SetProcessInput(false);
        DisconnectFocus();
        Failed?.Invoke(message);
    }
}
