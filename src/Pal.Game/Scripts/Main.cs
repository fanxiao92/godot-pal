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
    private ExplorationSession? session;
    private PalMapScene? mapScene;
    public ExplorationState? Exploration => session?.Current;

    public override void _Ready()
    {
        string? scenePath = null, profilePath = null;
        try
        {
            GetWindow().MinSize = new Vector2I(320, 200);
            var (root, profile) = NativeSceneLoader.Arguments(OS.GetCmdlineUserArgs());
            scenePath = root + "/Scenes/Scene.tscn";
            profilePath = profile;
            var map = NativeSceneLoader.Load(root);
            try
            {
                var checkedProfile = NativeExplorationProfile.Load(profile, map.Data);
                session = new ExplorationSession(checkedProfile.Rules, checkedProfile.Leader);
                map.InitializeScene(session.Current.Plan, checkedProfile.TextureFor);
            }
            catch
            {
                map.Free();
                throw;
            }
            mapScene = map;
            AddChild(map);
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

    public override void _Process(double delta)
    {
        if (session == null || mapScene == null) return;
        try
        {
            int? direction = null;
            int held = 0;
            for (int i = 0; i < DirectionActions.Length; i++)
            {
                if (!Input.IsActionPressed(DirectionActions[i])) continue;
                direction = i;
                held++;
            }
            if (session.Update(unchecked((uint)Time.GetTicksMsec()), held == 1 ? direction : null))
                mapScene.UpdateScene(session.Current.Plan);
        }
        catch (Exception e)
        {
            SetProcess(false);
            GD.PrintErr($"Exploration update failed: {e.Message}");
            GetTree().Quit(1);
        }
    }
}
