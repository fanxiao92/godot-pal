using Pal.Core;
using Xunit;

namespace Pal.Core.Tests;

public class ExplorationInputTests
{
    [Theory]
    [InlineData(ExplorationKey.SouthPrimary, 304, 248, 0, 144, 136, 1)]
    [InlineData(ExplorationKey.SouthAlternate, 304, 248, 0, 144, 136, 1)]
    [InlineData(ExplorationKey.WestPrimary, 304, 232, 1, 144, 120, 4)]
    [InlineData(ExplorationKey.WestAlternate, 304, 232, 1, 144, 120, 4)]
    [InlineData(ExplorationKey.NorthPrimary, 336, 232, 2, 176, 120, 7)]
    [InlineData(ExplorationKey.NorthAlternate, 336, 232, 2, 176, 120, 7)]
    [InlineData(ExplorationKey.EastPrimary, 336, 248, 3, 176, 136, 10)]
    [InlineData(ExplorationKey.EastAlternate, 336, 248, 3, 176, 136, 10)]
    public void Each_key_moves_one_literal_world_step_only_on_update(
        ExplorationKey key, int x, int y, int direction, int cameraX, int cameraY, int frame)
    {
        var session = new ExplorationSession(new StaticSceneRules(DrawMap()), Leader());
        var input = new ExplorationInput(session);
        var initial = input.Current;
        Assert.Same(session.Current, initial);
        input.Press(key);
        Assert.Same(initial, input.Current);

        Assert.True(input.Update(0));

        Assert.Same(session.Current, input.Current);
        AssertState(input, x, y, direction, 1, frame, true);
        Assert.Equal((cameraX, cameraY), (input.Current.Plan.CameraX, input.Current.Plan.CameraY));
        Assert.Contains(input.Current.Plan.Commands, c => c.Source == SceneDrawSource.Background);
        Assert.Equal(new SceneDrawCommand(SceneDrawSource.Leader, 2, frame, 150, 86, 122, 6),
            Assert.Single(input.Current.Plan.Commands, c => c.Source != SceneDrawSource.Background));
        Assert.Equal((320, 240, 3, 0), ((int)initial.Leader.X, (int)initial.Leader.Y,
            initial.Leader.Direction, initial.Leader.StepPhase));
    }

    [Theory]
    [InlineData(false, 320, 240, 1, 3, 304, 288)]
    [InlineData(true, 352, 240, 2, 6, 336, 320)]
    public void Releasing_latest_direction_falls_back_through_three_or_four_held_groups(
        bool includeNorth, int fallbackX, int fallbackY, int fallbackDirection, int fallbackFrame, int westX, int southX)
    {
        var input = new ExplorationInput(new ExplorationSession(new StaticSceneRules(DrawMap()), Leader()));
        var initial = input.Current;
        input.Press(ExplorationKey.SouthPrimary);
        input.Press(ExplorationKey.WestPrimary);
        if (includeNorth) input.Press(ExplorationKey.NorthPrimary);
        input.Press(ExplorationKey.EastPrimary);
        Assert.Same(initial, input.Current);
        Assert.True(input.Update(0));
        AssertState(input, 336, 248, 3, 1, 10, true);
        input.Release(ExplorationKey.EastPrimary);
        Assert.True(input.Update(100));
        AssertState(input, fallbackX, fallbackY, fallbackDirection, 2, fallbackFrame, true);
        input.Release(ExplorationKey.NorthPrimary);
        Assert.True(input.Update(200));
        AssertState(input, westX, 232, 1, 3, 5, true);
        input.Release(ExplorationKey.WestPrimary);
        Assert.True(input.Update(300));
        AssertState(input, southX, 240, 0, 0, 0, true);
        var moving = input.Current;
        input.Release(ExplorationKey.SouthPrimary);
        Assert.Same(moving, input.Current);
        Assert.True(input.Update(400));
        AssertState(input, southX, 240, 0, 2, 0, false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Down_left_S_keeps_south_group_priority_until_both_south_aliases_are_released(bool releaseDownFirst)
    {
        var input = new ExplorationInput(new ExplorationSession(new StaticSceneRules(DrawMap()), Leader()));
        input.Press(ExplorationKey.SouthPrimary);
        input.Press(ExplorationKey.WestPrimary);
        input.Press(ExplorationKey.SouthAlternate);
        Assert.True(input.Update(0));
        AssertState(input, 304, 248, 0, 1, 1, true);
        input.Release(releaseDownFirst ? ExplorationKey.SouthPrimary : ExplorationKey.SouthAlternate);
        Assert.True(input.Update(100));
        AssertState(input, 288, 256, 0, 2, 0, true);
        input.Release(releaseDownFirst ? ExplorationKey.SouthAlternate : ExplorationKey.SouthPrimary);
        Assert.True(input.Update(200));
        AssertState(input, 272, 248, 1, 3, 5, true);
        input.Release(ExplorationKey.WestPrimary);
        Assert.True(input.Update(300));
        AssertState(input, 272, 248, 1, 0, 3, false);
    }

    [Theory]
    [InlineData(ExplorationKey.SouthPrimary, true)]
    [InlineData(ExplorationKey.SouthAlternate, true)]
    [InlineData(ExplorationKey.SouthPrimary, false)]
    public void Echoes_and_duplicate_presses_do_not_reorder_add_held_aliases_or_accelerate(
        ExplorationKey duplicate, bool repeat)
    {
        var input = new ExplorationInput(new ExplorationSession(new StaticSceneRules(DrawMap()), Leader()));
        input.Press(ExplorationKey.SouthPrimary);
        input.Press(ExplorationKey.WestPrimary);
        input.Press(duplicate, repeat);
        input.Release(ExplorationKey.WestAlternate);
        input.Release(ExplorationKey.NorthPrimary);
        input.Release(ExplorationKey.NorthPrimary);
        Assert.True(input.Update(0));
        AssertState(input, 304, 232, 1, 1, 4, true);
        var moved = input.Current;
        input.Press(duplicate, repeat);
        input.Press(ExplorationKey.EastAlternate, repeat: true);
        Assert.Same(moved, input.Current);
        Assert.False(input.Update(1));
        Assert.False(input.Update(99));
        Assert.Same(moved, input.Current);
        Assert.True(input.Update(100));
        AssertState(input, 288, 224, 1, 2, 3, true);
        input.Release(ExplorationKey.WestPrimary);
        Assert.True(input.Update(200));
        AssertState(input, 272, 232, 0, 3, 2, true);
        input.Release(ExplorationKey.SouthPrimary);
        Assert.True(input.Update(300));
        AssertState(input, 272, 232, 0, 0, 0, false);
    }

    [Fact]
    public void Losing_focus_freezes_the_entire_walking_state_plan_and_static_objects_during_background_input()
    {
        var obj = new SceneObject(93, 336, 246, 1, 0, 0, 7, 1, 0, 0,
            new SceneSprite(7, [new SceneFrame(0, 20, 30)]));
        var rules = new StaticSceneRules(DrawMap(), [obj, obj with { Id = 94, State = -1, VanishTime = 9 }]);
        var input = new ExplorationInput(new ExplorationSession(rules, Leader()));
        input.Press(ExplorationKey.EastPrimary);
        input.Press(ExplorationKey.EastAlternate);
        Assert.True(input.Update(0));
        AssertState(input, 336, 248, 3, 1, 10, true);
        var walking = input.Current;
        var plan = walking.Plan;
        var commands = plan.Commands.ToArray();
        var objects = rules.Objects.ToArray();

        input.SetFocused(false);
        Assert.Same(walking, input.Current);
        Assert.False(input.Update(100));
        input.Press(ExplorationKey.WestPrimary);
        input.Press(ExplorationKey.NorthAlternate, repeat: true);
        input.Release(ExplorationKey.EastPrimary);
        input.Release(ExplorationKey.EastAlternate);
        input.SetFocused(false);
        Assert.False(input.Update(10000));
        Assert.False(input.Update(20000));
        Assert.Same(walking, input.Current);
        Assert.Same(plan, input.Current.Plan);
        Assert.Equal(commands, input.Current.Plan.Commands);
        Assert.Equal(objects, rules.Objects);
        Assert.Equal((176, 136, 10, 1), (plan.CameraX, plan.CameraY, plan.LeaderFrameIndex, plan.StepPhase));
        AssertState(input, 336, 248, 3, 1, 10, true);
    }

    [Fact]
    public void Returning_focus_waits_for_fresh_nonrepeat_press_and_discards_old_and_background_keys()
    {
        var input = new ExplorationInput(new ExplorationSession(new StaticSceneRules(DrawMap()), Leader()));
        input.Press(ExplorationKey.EastPrimary);
        input.Press(ExplorationKey.EastAlternate);
        Assert.True(input.Update(0));
        var walking = input.Current;
        input.SetFocused(false);
        input.Press(ExplorationKey.WestPrimary);
        input.Press(ExplorationKey.NorthAlternate);
        input.SetFocused(true);
        Assert.Same(walking, input.Current);
        Assert.False(input.Update(20000));
        input.Press(ExplorationKey.EastPrimary, repeat: true);
        input.Press(ExplorationKey.SouthAlternate, repeat: true);
        input.Release(ExplorationKey.NorthAlternate);
        input.SetFocused(true);
        Assert.False(input.Update(30000));
        Assert.Same(walking, input.Current);
        AssertState(input, 336, 248, 3, 1, 10, true);

        input.Press(ExplorationKey.EastPrimary);
        Assert.Same(walking, input.Current);
        Assert.True(input.Update(30000));
        AssertState(input, 352, 256, 3, 2, 9, true);
        Assert.False(input.Update(30099));
        Assert.True(input.Update(30100));
        AssertState(input, 368, 264, 3, 3, 11, true);
        input.Release(ExplorationKey.EastPrimary);
        Assert.True(input.Update(30200));
        AssertState(input, 368, 264, 3, 0, 9, false);
        Assert.True(input.Update(30300));
        AssertState(input, 368, 264, 3, 2, 9, false);
    }

    [Theory]
    [InlineData(0u, 1u, 100u, 101u, 350u, 351u, 449u, 450u)]
    [InlineData(4294967246u, 4294967247u, 50u, 51u, 300u, 301u, 399u, 400u)]
    public void Fresh_press_after_focus_resets_old_deadline_then_uses_100ms_wrap_and_no_catchup(
        uint first, uint resume, uint early, uint due, uint delayed, uint afterDelay, uint beforeNext, uint next)
    {
        var input = new ExplorationInput(new ExplorationSession(new StaticSceneRules(DrawMap()), Leader()));
        input.Press(ExplorationKey.EastPrimary);
        Assert.True(input.Update(first));
        var walking = input.Current;
        input.SetFocused(true);
        Assert.False(input.Update(resume));
        Assert.Same(walking, input.Current);
        input.SetFocused(false);
        input.SetFocused(true);
        Assert.False(input.Update(resume));
        input.Press(ExplorationKey.WestAlternate);
        input.SetFocused(true);
        Assert.Same(walking, input.Current);
        Assert.True(input.Update(resume));
        AssertState(input, 320, 240, 1, 2, 3, true);
        var resumed = input.Current;
        input.SetFocused(true);
        Assert.False(input.Update(early));
        Assert.Same(resumed, input.Current);
        Assert.True(input.Update(due));
        AssertState(input, 304, 232, 1, 3, 5, true);
        Assert.True(input.Update(delayed));
        AssertState(input, 288, 224, 1, 0, 3, true);
        var late = input.Current;
        input.SetFocused(true);
        Assert.False(input.Update(afterDelay));
        Assert.False(input.Update(beforeNext));
        Assert.Same(late, input.Current);
        Assert.True(input.Update(next));
        AssertState(input, 272, 216, 1, 1, 4, true);
    }

    [Fact]
    public void Constructor_rejects_null_session_immediately()
    {
        Assert.Equal("session", Assert.Throws<ArgumentNullException>(() => new ExplorationInput(null!)).ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(8)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void Invalid_keys_are_rejected_before_echo_or_focus_gates_without_changing_state_or_schedule(int value)
    {
        var input = new ExplorationInput(new ExplorationSession(new StaticSceneRules(DrawMap()), Leader()));
        input.Press(ExplorationKey.EastPrimary);
        Assert.True(input.Update(0));
        var walking = input.Current;
        var invalid = (ExplorationKey)value;
        Assert.Equal("key", Assert.Throws<ArgumentOutOfRangeException>(() => input.Press(invalid)).ParamName);
        Assert.Equal("key", Assert.Throws<ArgumentOutOfRangeException>(() => input.Press(invalid, repeat: true)).ParamName);
        Assert.Equal("key", Assert.Throws<ArgumentOutOfRangeException>(() => input.Release(invalid)).ParamName);
        Assert.False(input.Update(99));
        Assert.Same(walking, input.Current);
        input.SetFocused(false);
        Assert.Equal("key", Assert.Throws<ArgumentOutOfRangeException>(() => input.Press(invalid)).ParamName);
        Assert.Equal("key", Assert.Throws<ArgumentOutOfRangeException>(() => input.Press(invalid, repeat: true)).ParamName);
        Assert.Equal("key", Assert.Throws<ArgumentOutOfRangeException>(() => input.Release(invalid)).ParamName);
        input.SetFocused(true);
        Assert.Equal("key", Assert.Throws<ArgumentOutOfRangeException>(() => input.Press(invalid)).ParamName);
        Assert.Equal("key", Assert.Throws<ArgumentOutOfRangeException>(() => input.Press(invalid, repeat: true)).ParamName);
        Assert.Equal("key", Assert.Throws<ArgumentOutOfRangeException>(() => input.Release(invalid)).ParamName);
        Assert.False(input.Update(20000));
        Assert.Same(walking, input.Current);
        input.Press(ExplorationKey.WestPrimary);
        Assert.True(input.Update(1));
        AssertState(input, 320, 240, 1, 2, 3, true);
    }

    [Fact]
    public void Initially_idle_and_all_released_after_resume_keep_the_sessions_normal_stop_phase()
    {
        var input = new ExplorationInput(new ExplorationSession(new StaticSceneRules(DrawMap()), Leader()));
        input.SetFocused(true);
        input.Press(ExplorationKey.SouthPrimary, repeat: true);
        input.Release(ExplorationKey.WestAlternate);
        Assert.True(input.Update(0));
        AssertState(input, 320, 240, 3, 2, 9, false);
        var stopped = input.Current;
        Assert.False(input.Update(99));
        Assert.Same(stopped, input.Current);
        Assert.True(input.Update(100));
        AssertState(input, 320, 240, 3, 0, 9, false);
        input.Press(ExplorationKey.EastPrimary);
        input.Release(ExplorationKey.EastPrimary);
        Assert.True(input.Update(200));
        AssertState(input, 320, 240, 3, 2, 9, false);
        input.Press(ExplorationKey.WestPrimary);
        Assert.True(input.Update(300));
        AssertState(input, 304, 232, 1, 3, 5, true);
        input.SetFocused(false);
        input.SetFocused(true);
        input.Press(ExplorationKey.SouthPrimary);
        input.Release(ExplorationKey.SouthPrimary);
        Assert.True(input.Update(301));
        AssertState(input, 304, 232, 1, 0, 3, false);
        Assert.True(input.Update(401));
        AssertState(input, 304, 232, 1, 2, 3, false);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(93, false)]
    [InlineData(93, true)]
    public void Hidden_blockers_including_first_object_are_not_treated_as_leader_self_and_fallback_can_walk(
        int objectId, bool hasSprite)
    {
        var obj = new SceneObject((ushort)objectId, 304, 232, 2, 9, 0, 7, 1, 0, 0,
            hasSprite ? new SceneSprite(7, [new SceneFrame(0, 20, 30)]) : null);
        var rules = new StaticSceneRules(DrawMap(), [obj]);
        var input = new ExplorationInput(new ExplorationSession(rules, Leader() with { StepPhase = 1 }));
        input.Press(ExplorationKey.EastPrimary);
        input.Press(ExplorationKey.WestAlternate);
        Assert.True(input.Update(0));
        AssertState(input, 320, 240, 1, 2, 3, false);
        Assert.Equal((160, 128), (input.Current.Plan.CameraX, input.Current.Plan.CameraY));
        Assert.DoesNotContain(input.Current.Plan.Commands, c => c.Source == SceneDrawSource.Object);
        Assert.True(input.Update(100));
        AssertState(input, 320, 240, 1, 0, 3, false);
        input.Release(ExplorationKey.WestAlternate);
        Assert.True(input.Update(200));
        AssertState(input, 336, 248, 3, 1, 10, true);
        Assert.Equal((176, 136), (input.Current.Plan.CameraX, input.Current.Plan.CameraY));
        Assert.Equal(obj, Assert.Single(rules.Objects));
        Assert.DoesNotContain(input.Current.Plan.Commands, c => c.Source == SceneDrawSource.Object);
    }

    [Theory]
    [InlineData(ExplorationKey.WestPrimary)]
    [InlineData(ExplorationKey.WestAlternate)]
    public void Blocked_map_turn_uses_four_frame_standing_phase_without_sliding(ExplorationKey key)
    {
        var cells = new uint[16384];
        cells[1811] = 0x2000; // west target (304,232), cell (9,14,1)
        var rules = new StaticSceneRules(new SceneMap(12, cells, [new SceneFrame(0, 32, 15)]));
        var input = new ExplorationInput(new ExplorationSession(rules,
            Leader() with { WalkFramesWord = 4, StepPhase = 1 }));
        input.Press(key);
        Assert.True(input.Update(0));
        AssertState(input, 320, 240, 1, 2, 4, false);
        Assert.Equal((160, 128), (input.Current.Plan.CameraX, input.Current.Plan.CameraY));
        Assert.True(input.Update(100));
        AssertState(input, 320, 240, 1, 0, 4, false);
        input.Release(key);
        input.Press(ExplorationKey.NorthAlternate);
        Assert.True(input.Update(200));
        AssertState(input, 336, 232, 2, 1, 9, true);
        Assert.Equal((176, 120), (input.Current.Plan.CameraX, input.Current.Plan.CameraY));
    }

    [Fact]
    public void Movement_rebuilds_complete_dynamic_cover_order_and_focus_preserves_it_until_fresh_input()
    {
        var cells = new uint[16384];
        cells[1425] = 0x05030501; // (8,11,1), bottom frame1 / top frame2, height5
        var sprite = new SceneSprite(2, [new SceneFrame(9, 20, 30), new SceneFrame(10, 64, 62)]);
        var obj = new SceneObject(93, 336, 246, 1, 0, 0, 7, 1, 0, 0,
            new SceneSprite(7, [new SceneFrame(0, 20, 30)]));
        var rules = new StaticSceneRules(new SceneMap(12, cells,
            [new SceneFrame(0, 32, 15), new SceneFrame(1, 32, 15), new SceneFrame(2, 17, 19)]), [obj]);
        var input = new ExplorationInput(new ExplorationSession(rules, Leader() with { Sprite = sprite }));
        Assert.DoesNotContain(input.Current.Plan.Commands, c => c.Source == SceneDrawSource.Cover);
        input.Press(ExplorationKey.EastAlternate);
        Assert.True(input.Update(0));
        AssertState(input, 336, 248, 3, 1, 10, true);
        var walking = input.Current;
        var plan = walking.Plan;
        var commands = plan.Commands.ToArray();
        Assert.Equal((176, 136, 10, 1), (plan.CameraX, plan.CameraY, plan.LeaderFrameIndex, plan.StepPhase));
        Assert.Equal(new[] {
            new SceneDrawCommand(SceneDrawSource.Cover, 12, 1, 80, 40, 95, 40),
            new SceneDrawCommand(SceneDrawSource.Cover, 12, 2, 80, 36, 96, 41),
            new SceneDrawCommand(SceneDrawSource.Object, 7, 0, 150, 87, 119, 2, 93),
            new SceneDrawCommand(SceneDrawSource.Leader, 2, 10, 128, 54, 122, 6)
        }, plan.Commands.Where(c => c.Source != SceneDrawSource.Background));
        var background = plan.Commands.TakeWhile(c => c.Source == SceneDrawSource.Background).ToArray();
        Assert.Equal(417, background.Length);
        Assert.Equal(421, plan.Commands.Count);
        Assert.Equal(new[] {
            new SceneDrawCommand(SceneDrawSource.Background, 12, 1, 80, 40, 40, 0),
            new SceneDrawCommand(SceneDrawSource.Background, 12, 2, 80, 40, 40, 1)
        }, background.Where(c => c.FrameIndex is 1 or 2));

        input.SetFocused(false);
        input.Press(ExplorationKey.WestPrimary);
        Assert.False(input.Update(10000));
        input.SetFocused(true);
        input.Press(ExplorationKey.EastAlternate, repeat: true);
        Assert.False(input.Update(10001));
        Assert.Same(walking, input.Current);
        Assert.Same(plan, input.Current.Plan);
        Assert.Equal(commands, input.Current.Plan.Commands);
        input.Press(ExplorationKey.EastAlternate);
        input.Release(ExplorationKey.EastAlternate);
        Assert.True(input.Update(1));
        AssertState(input, 336, 248, 3, 2, 9, false);
        Assert.Equal((176, 136), (input.Current.Plan.CameraX, input.Current.Plan.CameraY));
        Assert.Equal(new[] {
            new SceneDrawCommand(SceneDrawSource.Object, 7, 0, 150, 87, 119, 2, 93),
            new SceneDrawCommand(SceneDrawSource.Leader, 2, 9, 150, 86, 122, 6)
        }, input.Current.Plan.Commands.Where(c => c.Source != SceneDrawSource.Background));
        Assert.Equal(background, input.Current.Plan.Commands.Where(c => c.Source == SceneDrawSource.Background));
        Assert.Equal(419, input.Current.Plan.Commands.Count);
        Assert.Equal(obj, Assert.Single(rules.Objects));
    }

    [Theory]
    [InlineData(ExplorationKey.SouthPrimary, ExplorationKey.SouthAlternate, 304, 248, 0, 1, 0)]
    [InlineData(ExplorationKey.SouthAlternate, ExplorationKey.SouthPrimary, 304, 248, 0, 1, 0)]
    [InlineData(ExplorationKey.WestPrimary, ExplorationKey.WestAlternate, 304, 232, 1, 4, 3)]
    [InlineData(ExplorationKey.WestAlternate, ExplorationKey.WestPrimary, 304, 232, 1, 4, 3)]
    [InlineData(ExplorationKey.NorthPrimary, ExplorationKey.NorthAlternate, 336, 232, 2, 7, 6)]
    [InlineData(ExplorationKey.NorthAlternate, ExplorationKey.NorthPrimary, 336, 232, 2, 7, 6)]
    [InlineData(ExplorationKey.EastPrimary, ExplorationKey.EastAlternate, 336, 248, 3, 10, 9)]
    [InlineData(ExplorationKey.EastAlternate, ExplorationKey.EastPrimary, 336, 248, 3, 10, 9)]
    public void Every_alias_pair_remains_held_in_either_release_order_without_double_steps(
        ExplorationKey firstReleased, ExplorationKey lastReleased, int x, int y, int direction, int walkingFrame, int standingFrame)
    {
        var input = new ExplorationInput(new ExplorationSession(new StaticSceneRules(DrawMap()), Leader()));
        var initial = input.Current;
        input.Press(firstReleased);
        input.Press(lastReleased);
        input.Release(firstReleased);
        Assert.Same(initial, input.Current);
        Assert.True(input.Update(0));
        AssertState(input, x, y, direction, 1, walkingFrame, true);
        input.Release(lastReleased);
        input.Release(lastReleased);
        Assert.True(input.Update(100));
        AssertState(input, x, y, direction, 2, standingFrame, false);
    }

    private static void AssertState(ExplorationInput input, int x, int y, int direction,
        int phase, int frame, bool walking)
    {
        var current = input.Current;
        Assert.Equal((x, y, direction, phase, frame, walking),
            ((int)current.Leader.X, (int)current.Leader.Y, current.Leader.Direction,
                current.Leader.StepPhase, current.Plan.LeaderFrameIndex, current.IsWalking));
        Assert.Equal(phase, current.Plan.StepPhase);
    }

    private static SceneMap DrawMap() => new(12, new uint[16384], [new SceneFrame(0, 32, 15)]);
    private static StaticLeader Leader() => new(320, 240, 3, 0, 0, 0,
        new SceneSprite(2, Enumerable.Range(0, 16).Select(i => new SceneFrame(i, 20, 30))));
}
