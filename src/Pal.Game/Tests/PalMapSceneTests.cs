using System;
using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Pal.Core;
using Pal.Game.Resources;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

[TestSuite, RequireGodotRuntime, GodotExceptionMonitor]
public class PalMapSceneTests
{
    [TestCase(Timeout = 15000)]
    public async Task NegativeCameraPlanUsesTruncationTowardZeroAndRendersThroughScene()
    {
        using var fixture = new NativeMapFixture();
        var (scene, plan) = LoadPlan(fixture, 143, 103);
        var background = plan.Commands.Where(c => c.Source == SceneDrawSource.Background).ToArray();
        AssertThat(plan.CameraX).IsEqual(-17);
        AssertThat(plan.CameraY).IsEqual(-9);
        AssertThat(background.Length).IsEqual(338); // 336 bottom + 2 top, literal reference traversal.
        AssertThat((background[0].Left, background[0].Top)).IsEqual((-31, -15));
        AssertThat(background.Any(c => c.Left == 97 && c.Top == 33 && c.FrameIndex == 1)).IsTrue();
        using var runner = ISceneRunner.Load(scene, true);
        await runner.SimulateFrames(3);
        AssertThat(scene.IsInsideTree() && scene.IsNodeReady()).IsTrue();
    }

    [TestCase(Timeout = 15000)]
    public async Task ByteWrappedCameraPlanKeepsLiteralScreenPositionsThroughScene()
    {
        using var fixture = new NativeMapFixture();
        var (scene, plan) = LoadPlan(fixture, 8352, 4208);
        // Reference BYTE conversion wraps grid (259,258,h0) to special cell (3,2,h0).
        var atCell = plan.Commands.Where(c => c.Source == SceneDrawSource.Background && c.Left == 80 && c.Top == 24).ToArray();
        AssertThat(atCell.Select(c => c.FrameIndex).ToArray()).IsEqual(new[] { 1, 2 });
        AssertThat(plan.CameraX).IsEqual(8192);
        AssertThat(plan.CameraY).IsEqual(4096);
        using var runner = ISceneRunner.Load(scene, true);
        await runner.SimulateFrames(3);
        AssertThat(scene.IsInsideTree() && scene.IsNodeReady()).IsTrue();
    }

    [TestCase(Timeout = 15000)]
    public async Task NativePackedSceneConsumesCompleteTwoLayerPlanWithoutAnotherCamera()
    {
        using var fixture = new NativeMapFixture();
        var (scene, plan) = LoadPlan(fixture, 160, 112);
        var background = plan.Commands.Where(c => c.Source == SceneDrawSource.Background).ToArray();
        var atCell = background.Where(c => c.Left == 80 && c.Top == 24).ToArray();
        AssertThat(atCell.Select(c => c.FrameIndex).ToArray()).IsEqual(new[] { 1, 2 });
        int a = Array.IndexOf(background, atCell[0]);
        AssertThat((background[a + 1].Left, background[a + 1].Top, background[a + 1].FrameIndex)).IsEqual((112, 24, 3));
        var halfRow = background.Single(c => c.Left == 96 && c.Top == 32 && c.FrameIndex == 2);
        AssertThat(Array.IndexOf(background, halfRow)).IsGreater(a + 1);
        AssertThat(Array.IndexOf(background, atCell[1])).IsGreater(Array.IndexOf(background, halfRow));
        AssertThat(background.Single(c => c.Left == -48 && c.Top == -24).FrameIndex).IsEqual(0);
        using var runner = ISceneRunner.Load(scene, true);
        await runner.SimulateFrames(3);
        AssertThat(scene.IsInsideTree() && scene.IsNodeReady()).IsTrue();
        AssertThat(scene.YSortEnabled).IsFalse();
        AssertThat(scene.Scale).IsEqual(Vector2.One);
        AssertThat(scene.TextureFilter).IsEqual(CanvasItem.TextureFilterEnum.Nearest);
    }

    // These arbitrary reference views are not legal spawn profiles. They use the public Core-plan
    // -> Godot draw seam, never a camera override or a map-only runtime fallback.
    internal static (PalMapScene Scene, StaticScenePlan Plan) LoadPlan(NativeMapFixture fixture, short leaderX, short leaderY)
    {
        var scene = ResourceLoader.Load<PackedScene>(fixture.Save(), cacheMode: ResourceLoader.CacheMode.IgnoreDeep).Instantiate<PalMapScene>();
        var map = scene.Data.Map;
        var rules = new StaticSceneRules(new SceneMap(scene.Data.MapId, map.RawCells.Select(c => (uint)c),
            map.Tiles.Frames.Select(f => new SceneFrame(f.Index, f.Width, f.Height))));
        var sprite = new SceneSprite(17, [new SceneFrame(0, 40, 24)]);
        var plan = rules.BuildPlan(new StaticLeader(leaderX, leaderY, 0, 0, 0, 0, sprite));
        var textures = map.Tiles.Frames.ToDictionary(f => f.Index, f => f.Texture);
        scene.InitializeScene(plan, c => textures[c.FrameIndex]);
        return (scene, plan);
    }
}
