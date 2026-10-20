using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Pal.Game.Resources;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

[TestSuite, RequireGodotRuntime, GodotExceptionMonitor]
public class PalMapSceneTests
{
    [TestCase(Timeout = 15000)]
    public async Task NegativeTestViewpointUsesTruncationTowardZeroAndReferenceTraversalBounds()
    {
        using var fixture = new NativeMapFixture();
        fixture.Scene.TestViewpoint = new Vector2I(-17, -9);
        using var runner = ISceneRunner.Load(fixture.Save(), true);
        await runner.SimulateFrames(3);
        var sprites = runner.Scene()!.GetChildren().OfType<Sprite2D>().ToArray();
        AssertThat(sprites.Length).IsEqual(338); // 336 bottom + 2 top, literal reference example.
        AssertThat(sprites[0].Position).IsEqual(new Vector2(-31, -15));
        AssertThat(sprites.Single(s => s.Position == new Vector2(97, 33) && s.Texture.GetWidth() == 41).Centered).IsFalse();
    }

    [TestCase(Timeout = 15000)]
    public async Task ByteWrappedTestViewpointDrawsOriginalCellsAtLiteralScreenPositions()
    {
        using var fixture = new NativeMapFixture();
        fixture.Scene.TestViewpoint = new Vector2I(8192, 4096);
        using var runner = ISceneRunner.Load(fixture.Save(), true);
        await runner.SimulateFrames(3);
        var scene = runner.Scene()!;
        AssertThat(scene.IsInsideTree() && scene.IsNodeReady()).IsTrue();
        var sprites = scene.GetChildren().OfType<Sprite2D>().ToArray();
        // Reference BYTE conversion wraps grid (259,258,h0) to special cell (3,2,h0).
        // Literal screen position and unique frame1/2 dimensions/colors, not a production calculation.
        var atCell = sprites.Where(s => s.Position == new Vector2(80, 24)).ToArray();
        AssertThat(atCell.Length).IsEqual(2);
        AssertThat(atCell[0].Texture.GetSize()).IsEqual(new Vector2(41, 25));
        AssertThat(atCell[1].Texture.GetSize()).IsEqual(new Vector2(42, 26));
        using var bottom = atCell[0].Texture.GetImage();
        using var top = atCell[1].Texture.GetImage();
        AssertThat(bottom.GetPixel(0, 0)).IsEqual(new Color(0, 1, 0, 1));
        AssertThat(top.GetPixel(0, 0)).IsEqual(new Color(0, 0, 1, 1));
        AssertThat(System.Array.IndexOf(sprites, atCell[1])).IsGreater(System.Array.IndexOf(sprites, atCell[0]));
    }

    [TestCase(Timeout = 15000)]
    public async Task NativePackedSceneDrawsTwoLayersInReferenceOrderWithoutHeightDisplacement()
    {
        using var fixture = new NativeMapFixture();
        using var runner = ISceneRunner.Load(fixture.Save(), true);
        var scene = runner.Scene() as PalMapScene ?? throw new System.InvalidOperationException("Native map did not load");
        await runner.SimulateFrames(3);
        AssertThat(scene.IsInsideTree() && scene.IsNodeReady()).IsTrue();
        AssertThat(scene.YSortEnabled).IsFalse();
        var sprites = scene.GetChildren().OfType<Sprite2D>().ToArray();
        AssertThat(sprites.Length).IsGreater(0);
        AssertThat(sprites.All(s => s.ZIndex == 0 && s.Visible)).IsTrue();
        var atCell = sprites.Where(s => s.Position == new Vector2(80, 24)).ToArray();
        AssertThat(atCell.Length).IsEqual(2);
        AssertThat(atCell[0].Texture.GetWidth()).IsEqual(41);
        AssertThat(atCell[1].Texture.GetWidth()).IsEqual(42);
        AssertThat(atCell[0].Centered).IsFalse();
        AssertThat(atCell[0].TextureFilter).IsEqual(CanvasItem.TextureFilterEnum.Nearest);
        // Literal traversal: h0 x3, h0 x4, h1 x3, then the entire top layer.
        int a = System.Array.IndexOf(sprites, atCell[0]);
        AssertThat(sprites[a + 1].Position).IsEqual(new Vector2(112, 24));
        AssertThat(sprites[a + 1].Texture.GetWidth()).IsEqual(43);
        var halfRow = sprites.Single(s => s.Position == new Vector2(96, 32) && s.Texture.GetWidth() == 42);
        AssertThat(System.Array.IndexOf(sprites, halfRow)).IsGreater(a + 1);
        AssertThat(System.Array.IndexOf(sprites, atCell[1])).IsGreater(System.Array.IndexOf(sprites, halfRow));
        // Bottom fallback outside grid, no top. Actual frame size retained, not forced to 32x16.
        var outside = sprites.Where(s => s.Position == new Vector2(-48, -24)).ToArray();
        AssertThat(outside.Length).IsEqual(1);
        AssertThat(outside[0].Texture.GetSize()).IsEqual(new Vector2(40, 24));
    }
}
