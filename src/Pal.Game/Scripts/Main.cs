using System;
using Godot;
using Pal.Game.Map;

namespace Pal.Game;

public partial class Main : Node2D
{
    // Explicit T2 test data, not an original spawn. Replaced by T3's leader configuration.
    [Export] public Vector2I TestViewpoint { get; set; } = Vector2I.Zero;

    public override void _Ready()
    {
        string? scenePath = null;
        try
        {
            GetWindow().MinSize = new Vector2I(320, 200);
            string root = NativeSceneLoader.ResourceRoot(OS.GetCmdlineUserArgs());
            scenePath = root + "/Scenes/Scene.tscn";
            var map = NativeSceneLoader.Load(root);
            try
            {
                map.TestViewpoint = TestViewpoint;
                map.InitializeMap();
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
            GD.PrintErr($"Map startup failed{(scenePath == null ? "" : " (" + scenePath + ")")}: {e.Message}");
            GetTree().Quit(1);
        }
    }
}
