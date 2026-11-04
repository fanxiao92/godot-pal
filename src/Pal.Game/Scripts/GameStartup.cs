using System;
using Godot;

namespace Pal.Game;

/// <summary>The shipped process policy; Main itself never reads argv or quits its host.</summary>
public partial class GameStartup : Main
{
    private readonly Exception? argumentError;
    private readonly string? suppliedProfile;

    public GameStartup()
    {
        // Pure selection while still outside the tree; resource loading belongs to Main._Ready.
        string[] arguments = OS.GetCmdlineUserArgs();
        suppliedProfile = StartupDiagnostics.SuppliedM4Profile(arguments);
        try { Configure(GameSceneConfiguration.FromArguments(arguments)); }
        catch (Exception error) { argumentError = error; }
    }

    public override void _Ready()
    {
        Failed += ExitOnFailure;
        GetWindow().MinSize = new Vector2I(320, 200);
        if (argumentError is not null)
        {
            Fail(StartupDiagnostics.Format(suppliedProfile, null, null, argumentError.Message));
            return;
        }
        base._Ready();
    }

    private void ExitOnFailure(string message)
    {
        GD.PrintErr(message);
        // The failing child has already freed its scene node (Main._Ready's map.Free()); the
        // loaded scene's Godot.Collections.Array wrappers are now unreachable. If their
        // finalizers only run at process exit (GC.RunFinalizers), godotsharp_array_destroy
        // fires after native teardown and the child dies with 0xC0000005 instead of exit 1
        // (load/timing-sensitive). Drain finalizers here, while the engine is still alive.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(1);
    }

    public override void _ExitTree()
    {
        Failed -= ExitOnFailure;
        base._ExitTree();
    }
}
