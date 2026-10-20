using System;
using Godot;
using Pal.Game.Map;
using Pal.Game.Profiles;

namespace Pal.Game;

public partial class Main : Node2D
{
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
                map.InitializeScene(checkedProfile.Plan, checkedProfile.TextureFor);
            }
            catch
            {
                map.Free();
                throw;
            }
            AddChild(map);
            GD.Print($"PAL_MAP_LOADED:{root}/Scenes/Scene.tscn");
            GD.Print($"PAL_GAME_READY:{Pal.Core.Startup.Status}");
        }
        catch (Exception e)
        {
            GD.PrintErr($"Static scene startup failed (scene={scenePath ?? "unselected"}, profile={profilePath ?? "unselected"}): {e.Message}");
            GetTree().Quit(1);
        }
    }
}
