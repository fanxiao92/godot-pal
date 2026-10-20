using Pal.Core;
using Xunit;

namespace Pal.Core.Tests;

public class ExplorationSessionTests
{
    [Theory]
    [InlineData(0, 304, 248, 144, 136, 1)]
    [InlineData(1, 304, 232, 144, 120, 4)]
    [InlineData(2, 336, 232, 176, 120, 7)]
    [InlineData(3, 336, 248, 176, 136, 10)]
    public void First_update_moves_one_reference_step_and_builds_a_complete_walking_plan(
        int direction, int x, int y, int cameraX, int cameraY, int frame)
    {
        var initial = Leader();
        var session = new ExplorationSession(new StaticSceneRules(DrawMap()), initial);
        var before = session.Current;
        Assert.Equal(initial, before.Leader);
        Assert.False(before.IsWalking);
        Assert.Equal((160, 128, 9, 0),
            (before.Plan.CameraX, before.Plan.CameraY, before.Plan.LeaderFrameIndex, before.Plan.StepPhase));

        Assert.True(session.Update(0, direction));

        Assert.Equal((x, y, direction, 1),
            ((int)session.Current.Leader.X, (int)session.Current.Leader.Y,
                session.Current.Leader.Direction, session.Current.Leader.StepPhase));
        Assert.True(session.Current.IsWalking);
        Assert.Equal((cameraX, cameraY, frame, 1),
            (session.Current.Plan.CameraX, session.Current.Plan.CameraY,
                session.Current.Plan.LeaderFrameIndex, session.Current.Plan.StepPhase));
        Assert.Contains(session.Current.Plan.Commands, c => c.Source == SceneDrawSource.Background);
        Assert.Equal(new SceneDrawCommand(SceneDrawSource.Leader, 2, frame, 150, 86, 122, 6),
            Assert.Single(session.Current.Plan.Commands, c => c.Source == SceneDrawSource.Leader));
        Assert.Equal((320, 240, 3, 0),
            ((int)before.Leader.X, (int)before.Leader.Y, before.Leader.Direction, before.Leader.StepPhase));
    }

    [Theory]
    [InlineData(0u, 99u, 100u, 350u, 351u, 449u, 450u)]
    [InlineData(4294967246u, 49u, 50u, 300u, 301u, 399u, 400u)]
    public void Update_is_due_at_equality_wraps_uint_ticks_and_reschedules_from_now_without_catchup(
        uint first, uint early, uint due, uint delayed, uint afterDelay, uint beforeNext, uint next)
    {
        var session = new ExplorationSession(new StaticSceneRules(DrawMap()), Leader());
        Assert.True(session.Update(first, 3));
        var snapshot = session.Current;
        Assert.False(session.Update(early, 1));
        Assert.Same(snapshot, session.Current);
        Assert.True(session.Update(due, 3));
        Assert.Equal((352, 256, 2, 9), ((int)session.Current.Leader.X,
            (int)session.Current.Leader.Y, session.Current.Leader.StepPhase, session.Current.Plan.LeaderFrameIndex));
        Assert.True(session.Update(delayed, 3));
        snapshot = session.Current;
        Assert.Equal((368, 264, 3, 11), ((int)snapshot.Leader.X,
            (int)snapshot.Leader.Y, snapshot.Leader.StepPhase, snapshot.Plan.LeaderFrameIndex));
        Assert.False(session.Update(afterDelay, 0));
        Assert.Same(snapshot, session.Current);
        Assert.False(session.Update(beforeNext, null));
        Assert.Same(snapshot, session.Current);
        Assert.True(session.Update(next, 3));
        Assert.Equal((384, 272, 0, 9), ((int)session.Current.Leader.X,
            (int)session.Current.Leader.Y, session.Current.Leader.StepPhase, session.Current.Plan.LeaderFrameIndex));
    }

    [Theory]
    [InlineData(0, 2, 0)]
    [InlineData(1, 2, 0)]
    [InlineData(2, 0, 2)]
    [InlineData(3, 0, 2)]
    public void Every_idle_step_toggles_stop_phase_without_moving_or_animating_standing(
        int initialPhase, int firstPhase, int secondPhase)
    {
        var initial = Leader() with { StepPhase = initialPhase };
        var session = new ExplorationSession(new StaticSceneRules(DrawMap()), initial);
        Assert.True(session.Update(0, null));
        Assert.Equal(initial with { StepPhase = firstPhase }, session.Current.Leader);
        Assert.False(session.Current.IsWalking);
        Assert.Equal((9, firstPhase), (session.Current.Plan.LeaderFrameIndex, session.Current.Plan.StepPhase));
        var first = session.Current;
        Assert.True(session.Update(100, null));
        Assert.Equal(initial with { StepPhase = secondPhase }, session.Current.Leader);
        Assert.False(session.Current.IsWalking);
        Assert.Equal((9, secondPhase), (session.Current.Plan.LeaderFrameIndex, session.Current.Plan.StepPhase));
        Assert.Equal(first.Plan.Commands, session.Current.Plan.Commands);
    }

    [Theory]
    [InlineData(false, 3, 11)]
    [InlineData(true, 1, 10)]
    public void Restart_foot_depends_on_how_many_idle_steps_elapsed(bool stopTwice, int phase, int frame)
    {
        var session = new ExplorationSession(new StaticSceneRules(DrawMap()), Leader());
        Assert.True(session.Update(0, 3));
        Assert.Equal((1, 10), (session.Current.Leader.StepPhase, session.Current.Plan.LeaderFrameIndex));
        Assert.True(session.Update(100, null));
        Assert.Equal((2, 9), (session.Current.Leader.StepPhase, session.Current.Plan.LeaderFrameIndex));
        if (stopTwice)
        {
            Assert.True(session.Update(200, null));
            Assert.Equal((0, 9), (session.Current.Leader.StepPhase, session.Current.Plan.LeaderFrameIndex));
        }
        Assert.True(session.Update(300, 3));
        Assert.Equal((phase, frame), (session.Current.Leader.StepPhase, session.Current.Plan.LeaderFrameIndex));
        Assert.Equal((352, 256), ((int)session.Current.Leader.X, (int)session.Current.Leader.Y));
    }

    [Theory]
    [InlineData(319, 232, 2, 0, false, true)]
    [InlineData(320, 232, 2, 0, false, false)]
    [InlineData(304, 239, 2, 9, true, true)]
    [InlineData(304, 240, 2, 9, true, false)]
    [InlineData(304, 232, 1, 0, true, false)]
    [InlineData(304, 232, 0, 0, true, false)]
    public void Object_collision_uses_raw_state_and_weighted_distance_not_sprite_or_vanish(
        int x, int y, int state, int vanish, bool hasSprite, bool blocked)
    {
        var sprite = new SceneSprite(7, [new SceneFrame(0, 20, 30)]);
        var obj = new SceneObject(93, (ushort)x, (ushort)y, (short)state, (short)vanish,
            0, 7, 1, 0, 0, hasSprite ? sprite : null);
        var rules = new StaticSceneRules(DrawMap(), [obj]);
        var initial = Leader() with { StepPhase = 1 };
        var session = new ExplorationSession(rules, initial);
        Assert.True(session.Update(0, 1));
        Assert.Equal(blocked ? (320, 240, 1, 2, 3) : (304, 232, 1, 2, 3),
            ((int)session.Current.Leader.X, (int)session.Current.Leader.Y,
                session.Current.Leader.Direction, session.Current.Leader.StepPhase, session.Current.Plan.LeaderFrameIndex));
        Assert.Equal(!blocked, session.Current.IsWalking);
        if (blocked)
        {
            Assert.Equal((160, 128), (session.Current.Plan.CameraX, session.Current.Plan.CameraY));
            Assert.True(session.Update(100, 1));
            Assert.Equal((320, 240, 1, 0, 3), ((int)session.Current.Leader.X, (int)session.Current.Leader.Y,
                session.Current.Leader.Direction, session.Current.Leader.StepPhase, session.Current.Plan.LeaderFrameIndex));
            Assert.False(session.Current.IsWalking);
        }
        Assert.Equal(obj, Assert.Single(rules.Objects));
        Assert.Equal(state > 0 && vanish == 0 && hasSprite,
            session.Current.Plan.Commands.Any(c => c.Source == SceneDrawSource.Object));
    }

    [Fact]
    public void Map_blocking_uses_target_diamond_half_and_turns_without_sliding()
    {
        var cells = new uint[16384];
        cells[1811] = 0x2000; // west target (304,232) is cell (9,14,1)
        var session = new ExplorationSession(new StaticSceneRules(new SceneMap(12, cells,
            [new SceneFrame(0, 32, 15)])), Leader(4) with { StepPhase = 1 });
        Assert.True(session.Update(0, 1));
        Assert.Equal((320, 240, 1, 2, 4), ((int)session.Current.Leader.X, (int)session.Current.Leader.Y,
            session.Current.Leader.Direction, session.Current.Leader.StepPhase, session.Current.Plan.LeaderFrameIndex));
        Assert.False(session.Current.IsWalking);
        Assert.True(session.Update(100, 1));
        Assert.Equal((0, 4), (session.Current.Leader.StepPhase, session.Current.Plan.LeaderFrameIndex));
        Assert.Equal((160, 128), (session.Current.Plan.CameraX, session.Current.Plan.CameraY));
    }

    [Theory]
    [InlineData(160, 112, 0, 0, 0)]
    [InlineData(32760, 240, 3, 32600, 128)]
    [InlineData(320, 32760, 0, 160, 32648)]
    public void Range_and_short_packed_targets_are_blocked_without_camera_clamping(
        int x, int y, int direction, int cameraX, int cameraY)
    {
        var initial = Leader() with { X = (short)x, Y = (short)y };
        var session = new ExplorationSession(new StaticSceneRules(DrawMap()), initial);
        Assert.True(session.Update(0, direction));
        Assert.Equal(initial with { Direction = direction, StepPhase = 2 }, session.Current.Leader);
        Assert.False(session.Current.IsWalking);
        Assert.Equal((cameraX, cameraY), (session.Current.Plan.CameraX, session.Current.Plan.CameraY));
    }

    [Theory]
    [InlineData(0, 6, 7, 6, 8, 6)]
    [InlineData(3, 6, 7, 6, 8, 6)]
    [InlineData(4, 8, 9, 10, 11, 8)]
    [InlineData(5, 10, 7, 6, 8, 6)]
    [InlineData(65535, 65534, 7, 6, 8, 6)]
    public void Only_raw_four_walks_with_four_frames_other_words_walk_three_but_stand_with_raw_stride(
        int word, int standing, int first, int second, int third, int fourth)
    {
        var frames = Enumerable.Range(0, 16).Select(i => new SceneFrame(i, 20, 30)).ToList();
        if (standing > 15) frames.Add(new SceneFrame(standing, 20, 30));
        var leader = Leader((ushort)word) with { Direction = 2, Sprite = new SceneSprite(2, frames) };
        var session = new ExplorationSession(new StaticSceneRules(DrawMap()), leader);
        Assert.Equal(standing, session.Current.Plan.LeaderFrameIndex);
        var expected = new[] { (1, first), (2, second), (3, third), (0, fourth) };
        uint now = 0;
        foreach (var pair in expected)
        {
            Assert.True(session.Update(now, 2));
            Assert.Equal(pair, (session.Current.Leader.StepPhase, session.Current.Plan.LeaderFrameIndex));
            Assert.True(session.Current.IsWalking);
            Assert.Equal((ushort)word, session.Current.Leader.WalkFramesWord);
            now += 100;
        }
        Assert.Equal((384, 208), ((int)session.Current.Leader.X, (int)session.Current.Leader.Y));
        Assert.True(session.Update(400, null));
        Assert.Equal((2, standing), (session.Current.Leader.StepPhase, session.Current.Plan.LeaderFrameIndex));
        Assert.False(session.Current.IsWalking);
        Assert.True(session.Update(500, null));
        Assert.Equal((0, standing), (session.Current.Leader.StepPhase, session.Current.Plan.LeaderFrameIndex));
        Assert.Equal((ushort)word, session.Current.Leader.WalkFramesWord);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void Invalid_direction_is_rejected_even_before_deadline_without_consuming_a_step(int direction)
    {
        var session = new ExplorationSession(new StaticSceneRules(DrawMap()), Leader());
        var initial = session.Current;
        Assert.Equal("direction", Assert.Throws<ArgumentOutOfRangeException>(() => session.Update(0, direction)).ParamName);
        Assert.Same(initial, session.Current);
        Assert.True(session.Update(0, 3));
        var moved = session.Current;
        Assert.Equal("direction", Assert.Throws<ArgumentOutOfRangeException>(() => session.Update(1, direction)).ParamName);
        Assert.Same(moved, session.Current);
        Assert.False(session.Update(99, 1));
        Assert.Same(moved, session.Current);
        Assert.True(session.Update(100, 1));
        Assert.Equal((320, 240, 1, 2), ((int)session.Current.Leader.X, (int)session.Current.Leader.Y,
            session.Current.Leader.Direction, session.Current.Leader.StepPhase));
    }

    [Fact]
    public void Selected_frame_dimensions_rebuild_both_cover_layers_anchors_and_stable_full_draw_order()
    {
        var cells = new uint[16384];
        cells[1425] = 0x05030501; // (8,11,1), bottom frame1 / top frame2, height5
        var sprite = new SceneSprite(2, [new SceneFrame(9, 20, 30), new SceneFrame(10, 64, 62)]);
        var obj = new SceneObject(93, 336, 246, 1, 0, 0, 7, 1, 0, 0,
            new SceneSprite(7, [new SceneFrame(0, 20, 30)]));
        var rules = new StaticSceneRules(new SceneMap(12, cells,
            [new SceneFrame(0, 32, 15), new SceneFrame(1, 32, 15), new SceneFrame(2, 17, 19)]), [obj]);
        var initial = Leader() with { Sprite = sprite };
        var session = new ExplorationSession(rules, initial);
        Assert.DoesNotContain(session.Current.Plan.Commands, c => c.Source == SceneDrawSource.Cover);

        var explicitPlan = rules.BuildPlan(initial, 10);
        Assert.Equal(new[] {
            new SceneDrawCommand(SceneDrawSource.Cover, 12, 1, 96, 48, 103, 40),
            new SceneDrawCommand(SceneDrawSource.Cover, 12, 2, 96, 44, 104, 41),
            new SceneDrawCommand(SceneDrawSource.Leader, 2, 10, 128, 54, 122, 6),
            new SceneDrawCommand(SceneDrawSource.Object, 7, 0, 166, 95, 127, 2, 93)
        }, explicitPlan.Commands.Where(c => c.Source != SceneDrawSource.Background));
        Assert.True(session.Update(0, 3));
        var plan = session.Current.Plan;
        Assert.Equal((176, 136, 10, 1), (plan.CameraX, plan.CameraY, plan.LeaderFrameIndex, plan.StepPhase));
        Assert.Equal(new[] {
            new SceneDrawCommand(SceneDrawSource.Cover, 12, 1, 80, 40, 95, 40),
            new SceneDrawCommand(SceneDrawSource.Cover, 12, 2, 80, 36, 96, 41),
            new SceneDrawCommand(SceneDrawSource.Object, 7, 0, 150, 87, 119, 2, 93),
            new SceneDrawCommand(SceneDrawSource.Leader, 2, 10, 128, 54, 122, 6)
        }, plan.Commands.Where(c => c.Source != SceneDrawSource.Background));
        var background = plan.Commands.TakeWhile(c => c.Source == SceneDrawSource.Background).ToArray();
        Assert.Equal(plan.Commands.Count - 4, background.Length);
        Assert.Equal(new[] {
            new SceneDrawCommand(SceneDrawSource.Background, 12, 1, 80, 40, 40, 0),
            new SceneDrawCommand(SceneDrawSource.Background, 12, 2, 80, 40, 40, 1)
        }, background.Where(c => c.FrameIndex is 1 or 2));

        Assert.True(session.Update(100, null));
        Assert.Equal((176, 136, 9, 2), (session.Current.Plan.CameraX, session.Current.Plan.CameraY,
            session.Current.Plan.LeaderFrameIndex, session.Current.Plan.StepPhase));
        Assert.Equal(new[] {
            new SceneDrawCommand(SceneDrawSource.Object, 7, 0, 150, 87, 119, 2, 93),
            new SceneDrawCommand(SceneDrawSource.Leader, 2, 9, 150, 86, 122, 6)
        }, session.Current.Plan.Commands.Where(c => c.Source != SceneDrawSource.Background));
        Assert.Equal(obj, Assert.Single(rules.Objects));
        Assert.DoesNotContain(rules.BuildPlan(initial).Commands, c => c.Source == SceneDrawSource.Cover);
    }

    [Fact]
    public void Missing_actual_walk_frame_fails_atomically_without_prevalidating_future_frames_or_consuming_deadline()
    {
        var leader = Leader() with { Sprite = new SceneSprite(2, [new SceneFrame(9, 20, 30)]) };
        var session = new ExplorationSession(new StaticSceneRules(DrawMap()), leader);
        var initial = session.Current;
        var error = Assert.Throws<InvalidOperationException>(() => session.Update(0, 3));
        Assert.Contains("missing frame 10", error.Message);
        Assert.Same(initial, session.Current);
        Assert.True(session.Update(0, null));
        var stopped = session.Current;
        Assert.Equal((2, 9), (stopped.Leader.StepPhase, stopped.Plan.LeaderFrameIndex));
        Assert.False(session.Update(99, 3));
        Assert.Same(stopped, session.Current);
        error = Assert.Throws<InvalidOperationException>(() => session.Update(100, 3));
        Assert.Contains("missing frame 11", error.Message);
        Assert.Same(stopped, session.Current);
        Assert.True(session.Update(100, null));
        Assert.Equal((0, 9), (session.Current.Leader.StepPhase, session.Current.Plan.LeaderFrameIndex));
    }

    [Fact]
    public void Missing_standing_frame_on_a_blocked_turn_does_not_publish_half_a_state()
    {
        var leader = Leader() with { Sprite = new SceneSprite(2, [new SceneFrame(9, 20, 30)]) };
        var obj = new SceneObject(93, 304, 232, 2, 7, 0, 0, 0, 0, 0);
        var rules = new StaticSceneRules(DrawMap(), [obj]);
        var session = new ExplorationSession(rules, leader);
        var initial = session.Current;
        var error = Assert.Throws<InvalidOperationException>(() => session.Update(0, 1));
        Assert.Contains("missing frame 3", error.Message);
        Assert.Same(initial, session.Current);
        Assert.True(session.Update(0, null));
        Assert.Equal(leader with { StepPhase = 2 }, session.Current.Leader);
        Assert.Equal(obj, Assert.Single(rules.Objects));
    }

    [Fact]
    public void Initial_state_requires_only_its_standing_frame_and_preserves_short_camera_and_raw_layer()
    {
        var leader = Leader(65535) with { X = -32609, Y = 112, Layer = 65535, StepPhase = 3,
            Sprite = new SceneSprite(2, [new SceneFrame(65533, 21, 30)]) };
        var session = new ExplorationSession(new StaticSceneRules(DrawMap()), leader);
        Assert.Same(leader, session.Current.Leader);
        Assert.False(session.Current.IsWalking);
        Assert.Equal((32767, 0, 65533, 3), (session.Current.Plan.CameraX, session.Current.Plan.CameraY,
            session.Current.Plan.LeaderFrameIndex, session.Current.Plan.StepPhase));
        Assert.Equal(new SceneDrawCommand(SceneDrawSource.Leader, 2, 65533, 150, 86, 121, 65541),
            Assert.Single(session.Current.Plan.Commands, c => c.Source == SceneDrawSource.Leader));
        Assert.Throws<InvalidOperationException>(() => new ExplorationSession(new StaticSceneRules(DrawMap()),
            leader with { Sprite = new SceneSprite(2, []) }));
    }

    private static SceneMap DrawMap() => new(12, new uint[16384], [new SceneFrame(0, 32, 15)]);
    private static StaticLeader Leader(ushort word = 0) => new(320, 240, 3, 0, 0, word,
        new SceneSprite(2, Enumerable.Range(0, 16).Select(i => new SceneFrame(i, 20, 30))));
}
