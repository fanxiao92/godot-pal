using Pal.Core;
using Xunit;

namespace Pal.Core.Tests;

public class SceneSearchTests
{
    [Fact]
    public void Independent_start_has_no_sample_zero_hit_and_selects_global_owner60_at_sample_one()
    {
        var target = Object(60, 1216, 1344);
        var rules = Rules([Object(57, 1264, 1352) with { TriggerMode = 6 }, target]);
        Assert.Equal(new SceneSearchMatch(60, 1), rules.FindSearchTarget(Leader(1232, 1336, 0)));
    }

    // Independent PAL_GetSearchTriggerRange checkpoints for (320,240), South.
    [Theory]
    [InlineData(0, 320, 240)]
    [InlineData(1, 304, 248)]
    [InlineData(2, 320, 256)]
    [InlineData(3, 288, 240)]
    [InlineData(4, 288, 256)]
    [InlineData(5, 304, 264)]
    [InlineData(6, 272, 248)]
    [InlineData(7, 272, 264)]
    [InlineData(8, 288, 272)]
    [InlineData(9, 256, 256)]
    [InlineData(10, 256, 272)]
    [InlineData(11, 272, 280)]
    [InlineData(12, 240, 264)]
    public void All_thirteen_samples_are_searched_in_reference_order(int sample, ushort x, ushort y)
    {
        Assert.Equal(new SceneSearchMatch(93, sample), Rules([Object(93, x, y)]).FindSearchTarget(Leader()));
    }

    [Theory]
    [InlineData(0, 304, 248)]
    [InlineData(1, 304, 232)]
    [InlineData(2, 336, 232)]
    [InlineData(3, 336, 248)]
    public void Four_directions_use_the_reference_diagonal_offsets(int direction, ushort x, ushort y)
    {
        Assert.Equal(new SceneSearchMatch(93, 1), Rules([Object(93, x, y)]).FindSearchTarget(Leader(direction: direction)));
    }

    [Fact]
    public void Sample_priority_precedes_object_order_and_same_cell_uses_original_order_not_ID_or_nearest()
    {
        var far = Object(10, 240, 264);
        var near = Object(93, 319, 255);
        var sameCell = Object(60, 304, 248);
        var rules = Rules([far, near, sameCell]);
        Assert.Equal(new SceneSearchMatch(93, 1), rules.FindSearchTarget(Leader()));
        Assert.Equal(new SceneSearchMatch(60, 1), Rules([far, sameCell, near]).FindSearchTarget(Leader()));
    }

    [Theory]
    [InlineData(0, 320, 240, -1)]
    [InlineData(1, 304, 248, 1)]
    [InlineData(1, 320, 256, -1)]
    [InlineData(2, 272, 264, 7)]
    [InlineData(2, 288, 272, -1)]
    [InlineData(3, 240, 264, 12)]
    [InlineData(4, 320, 240, -1)]
    [InlineData(65535, 320, 240, -1)]
    public void Mode_threshold_is_strict_and_touch_modes_are_not_manual_search(ushort mode, ushort x, ushort y, int sample)
    {
        var hit = Rules([Object(60, x, y) with { TriggerMode = mode }]).FindSearchTarget(Leader());
        Assert.Equal(sample < 0 ? null : new SceneSearchMatch(60, sample), hit);
    }

    [Theory]
    [InlineData(-1, 0, false)]
    [InlineData(0, 0, false)]
    [InlineData(1, 9, true)]
    [InlineData(2, -1, true)]
    public void Only_positive_state_filters_search_not_vanish_visibility_sprite_collision_or_zero_entry(short state, short vanish, bool found)
    {
        var obj = Object(60, 304, 248) with { State = state, VanishTime = vanish, TriggerEntry = 0 };
        Assert.Equal(found ? new SceneSearchMatch(60, 1) : null, Rules([obj]).FindSearchTarget(Leader()));
    }

    [Theory]
    [InlineData(-1, -1, 0, 16, 0, 0)] // C truncation toward zero, nonzero remainder half.
    [InlineData(32760, 240, 3, 32784, 248, -1)] // sample SHORT wraps; object remains unsigned WORD.
    [InlineData(320, 32760, 0, 304, 32768, -1)]
    [InlineData(320, 240, 0, 240, 280, -1)] // just outside the last checkpoint.
    public void Search_retains_C_division_remainder_and_signed_sample_packing(short x, short y, int direction,
        ushort objectX, ushort objectY, int sample)
    {
        Assert.Equal(sample < 0 ? null : new SceneSearchMatch(60, sample),
            Rules([Object(60, objectX, objectY)]).FindSearchTarget(Leader(x, y, direction)));
    }

    [Fact]
    public void No_matching_objects_returns_no_target_without_changing_metadata()
    {
        var obj = Object(60, 1000, 1000);
        var rules = Rules([obj]);
        Assert.Null(rules.FindSearchTarget(Leader()));
        Assert.Equal(obj, Assert.Single(rules.Objects));
    }

    internal static SceneObject Object(ushort id, ushort x, ushort y) =>
        new(id, x, y, 2, 0, 0, 0, 0, 0, 0) { TriggerMode = 3, TriggerEntry = 0x0188 };
    internal static StaticSceneRules Rules(IEnumerable<SceneObject> objects) =>
        new(new SceneMap(10, new uint[16384], [new SceneFrame(0, 32, 15)]), objects);
    internal static StaticLeader Leader(short x = 320, short y = 240, int direction = 0) =>
        new(x, y, direction, 0, 0, 3, new SceneSprite(2,
            Enumerable.Range(0, 16).Select(i => new SceneFrame(i, 20, 30))));
}
