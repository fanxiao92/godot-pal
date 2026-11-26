using Godot;
using GdUnit4;

namespace Pal.Game.Tests;

internal static class SceneRunnerInput
{
    internal static void Send(ISceneRunner runner, Key key, bool pressed, bool echo = false, KeyLocation location = KeyLocation.Unspecified)
    {
        if (!echo && location == KeyLocation.Unspecified)
        {
            if (pressed) runner.SimulateKeyPress(key);
            else runner.SimulateKeyRelease(key);
            return;
        }
        using var input = new InputEventKey { Keycode = key, Pressed = pressed, Echo = echo, Location = location };
        runner.Scene()?.GetViewport().PushInput(input);
    }
}
