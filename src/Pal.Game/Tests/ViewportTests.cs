using System.Threading.Tasks;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

[TestSuite, RequireGodotRuntime, GodotExceptionMonitor]
public class ViewportTests
{
    [TestCase(Timeout = 15000)]
    public async Task LogicalViewportKeeps320By200AtIntegerScaleWithBlackMargins()
    {
        using var fixture = new NativeMapFixture();
        var (scene, _) = PalMapSceneTests.LoadPlan(fixture, 160, 112);
        using var runner = ISceneRunner.Load(scene, true);
        var window = runner.Scene()!.GetWindow();
        var oldSize = window.Size;
        try
        {
            window.Size = new Vector2I(901, 701);
            await runner.SimulateFrames(3);
            AssertThat(window.GetVisibleRect().Size).IsEqual(new Vector2(320, 200));
            AssertThat(window.ContentScaleMode).IsEqual(Window.ContentScaleModeEnum.Viewport);
            AssertThat(window.ContentScaleAspect).IsEqual(Window.ContentScaleAspectEnum.Keep);
            AssertThat(window.ContentScaleStretch).IsEqual(Window.ContentScaleStretchEnum.Integer);
            var transform = window.GetFinalTransform();
            AssertThat(transform.X).IsEqual(new Vector2(2, 0));
            AssertThat(transform.Y).IsEqual(new Vector2(0, 2));
            AssertThat(transform.Origin).IsEqual(new Vector2(131, 151)); // 640x400 centered, odd pixel on left/top.
            window.Size = new Vector2I(1000, 400);
            await runner.SimulateFrames(3);
            AssertThat(window.GetVisibleRect().Size).IsEqual(new Vector2(320, 200));
            AssertThat(window.GetFinalTransform().Origin).IsEqual(new Vector2(180, 0));
            AssertThat(window.GetFinalTransform().X).IsEqual(new Vector2(2, 0));
        }
        finally { window.Size = oldSize; }
    }
}
