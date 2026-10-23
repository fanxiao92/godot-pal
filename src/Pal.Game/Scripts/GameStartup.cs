using System;
using Godot;

namespace Pal.Game;

/// <summary>The shipped process policy; Main itself never reads argv or quits its host.</summary>
public partial class GameStartup : Main
{
    private readonly Exception? argumentError;

    public GameStartup()
    {
        // Pure selection while still outside the tree; resource loading belongs to Main._Ready.
        try { Configure(GameSceneConfiguration.FromArguments(OS.GetCmdlineUserArgs())); }
        catch (Exception error) { argumentError = error; }
    }

    public override void _Ready()
    {
        Failed += ExitOnFailure;
        GetWindow().MinSize = new Vector2I(320, 200);
        if (argumentError is not null)
        {
            Fail($"Static scene startup failed (scene=unselected, profile=unselected): {argumentError.Message}");
            return;
        }
        base._Ready();
    }

    private void ExitOnFailure(string message)
    {
        GD.PrintErr(message);
        GetTree().Quit(1);
    }

    public override void _ExitTree()
    {
        Failed -= ExitOnFailure;
        base._ExitTree();
    }
}
