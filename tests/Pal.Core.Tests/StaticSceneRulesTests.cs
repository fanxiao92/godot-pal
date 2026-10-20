using Pal.Core;
using Xunit;

namespace Pal.Core.Tests;

public class StaticSceneRulesTests
{
    [Fact]
    public void Static_leader_uses_standing_frame_camera_and_party_anchor_without_advancing_phase()
    {
        var sprite = new SceneSprite(2, [new SceneFrame(6, 21, 30)]);
        var leader = new StaticLeader(192, 128, 2, 8, 3, 0, sprite);
        var scene = new StaticSceneRules(DrawMap());

        var plan = scene.BuildPlan(leader);

        Assert.Equal((32, 16, 6, 3), (plan.CameraX, plan.CameraY, plan.LeaderFrameIndex, plan.StepPhase));
        Assert.Equal(new SceneDrawCommand(SceneDrawSource.Leader, 2, 6, 150, 86, 130, 14),
            Assert.Single(plan.Commands, c => c.Source == SceneDrawSource.Leader));
    }

    [Fact]
    public void Collision_uses_raw_object_coordinates_state_and_global_self_id_not_visibility()
    {
        var blocker = new SceneObject(93, 175, 112, 2, 9, -3, 0, 3, 1, 2);
        var scene = new StaticSceneRules(new SceneMap(12, new uint[16384]), [blocker]);
        Assert.True(scene.IsBlocked(160, 112));
        Assert.False(scene.IsBlocked(160, 112, selfObjectId: 93));
        Assert.True(scene.IsBlocked(160, 112, selfObjectId: 1));
        Assert.False(scene.IsBlocked(160, 112, checkObjects: false));
        Assert.False(new StaticSceneRules(new SceneMap(12, new uint[16384]),
            [blocker with { X = 176 }]).IsBlocked(160, 112));
        Assert.True(new StaticSceneRules(new SceneMap(12, new uint[16384]),
            [blocker with { X = 160, Y = 119 }]).IsBlocked(160, 112));
        Assert.False(new StaticSceneRules(new SceneMap(12, new uint[16384]),
            [blocker with { X = 160, Y = 120 }]).IsBlocked(160, 112));
        Assert.False(new StaticSceneRules(new SceneMap(12, new uint[16384]),
            [blocker with { State = 1 }]).IsBlocked(160, 112));
        Assert.False(new StaticSceneRules(new SceneMap(12, new uint[16384]),
            [blocker with { X = 65535, Y = 0 }]).IsBlocked(-1, 0, checkRange: false));
    }

    [Theory]
    [InlineData(0, 0, false, false)]
    [InlineData(-1, -1, false, false)]
    [InlineData(-31, -15, false, false)]
    [InlineData(-32, 0, false, true)]
    [InlineData(0, -16, false, true)]
    [InlineData(8192, 0, false, false)]
    [InlineData(65536, 65536, false, false)]
    [InlineData(160, 4208, true, false)]
    [InlineData(160, 32640, true, true)]
    [InlineData(160, 112, true, false)]
    [InlineData(159, 112, true, true)]
    [InlineData(1856, 112, true, true)]
    [InlineData(2048, 0, false, true)]
    [InlineData(0, 2048, false, true)]
    public void Collision_preserves_signed_packing_c_division_byte_wrap_and_reference_range(int x, int y, bool range, bool expected)
    {
        var scene = new StaticSceneRules(new SceneMap(12, new uint[16384]));
        Assert.Equal(expected, scene.IsBlocked(x, y, checkRange: range));
    }

    [Fact]
    public void Objects_use_three_frame_mapping_distinct_anchors_and_stable_layer_sort_without_mutation()
    {
        var sprite = new SceneSprite(7, [new SceneFrame(3, 21, 30), new SceneFrame(5, 40, 50)]);
        var first = new SceneObject(93, 160, 112, 1, 0, 1, 7, 3, 1, 2, sprite);
        var objects = new[] { first, first with { Id = 94, CurrentFrame = 3, Layer = 0 },
            first with { Id = 95 }, first with { Id = 96, State = 0 },
            first with { Id = 97, State = -1 }, first with { Id = 98, VanishTime = 1 },
            first with { Id = 99, Sprite = null } };
        var before = objects.ToArray();
        var scene = new StaticSceneRules(DrawMap(), objects);
        var plan = scene.BuildPlan(new StaticLeader(160, 112, 0, 0, 2, 3,
            new SceneSprite(2, [new SceneFrame(0, 20, 30)])));
        Assert.Equal(new[] {
            new SceneDrawCommand(SceneDrawSource.Object, 7, 5, 140, 69, 121, 2, 94),
            new SceneDrawCommand(SceneDrawSource.Leader, 2, 0, 150, 86, 122, 6),
            new SceneDrawCommand(SceneDrawSource.Object, 7, 3, 150, 89, 129, 10, 93),
            new SceneDrawCommand(SceneDrawSource.Object, 7, 3, 150, 89, 129, 10, 95)
        }, plan.Commands.Where(c => c.Source != SceneDrawSource.Background));
        Assert.Equal(before, scene.Objects);
        Assert.Equal(plan.Commands, scene.BuildPlan(new StaticLeader(160, 112, 0, 0, 2, 3,
            new SceneSprite(2, [new SceneFrame(0, 20, 30)]))).Commands);
    }

    [Fact]
    public void Background_draws_complete_bottom_then_top_with_byte_query_fallback_and_no_height_shift()
    {
        var cells = new uint[16384];
        cells[0] = 0x00030401; // bottom frame1 height4, top frame2 height0
        var scene = new StaticSceneRules(new SceneMap(12, cells,
            [new SceneFrame(0, 32, 15), new SceneFrame(1, 32, 23), new SceneFrame(2, 17, 19)]));
        var plan = scene.BuildPlan(Leader());
        var background = plan.Commands.Where(c => c.Source == SceneDrawSource.Background).ToArray();
        Assert.Equal(391, background.Length);
        Assert.Equal(new SceneDrawCommand(SceneDrawSource.Background, 12, 1, -48, -24, -24, 0), background[0]);
        Assert.Equal(new SceneDrawCommand(SceneDrawSource.Background, 12, 0, 352, 208, 208, 0), background[389]);
        Assert.Equal(new SceneDrawCommand(SceneDrawSource.Background, 12, 2, -16, -8, -8, 1), background[390]);
        Assert.Equal(SceneDrawSource.Leader, plan.Commands[^1].Source);
    }

    [Fact]
    public void Cover_scans_both_layers_includes_height_equality_and_preserves_duplicate_candidates()
    {
        var cells = new uint[16384];
        cells[130] = 0x01030101; // (1,1,0): frame1/2, height1 in both layers
        cells[128] = 0x00030001; // (0,1,0): both bitmaps present but height0
        var sprite = new SceneSprite(7, [new SceneFrame(0, 20, 30)]);
        var obj = new SceneObject(93, 32, 25, 1, 0, 0, 7, 1, 0, 0, sprite);
        var scene = new StaticSceneRules(new SceneMap(12, cells,
            [new SceneFrame(0, 32, 15), new SceneFrame(1, 32, 15), new SceneFrame(2, 17, 19)]),
            [obj, obj with { Id = 94 }]);
        var plan = scene.BuildPlan(Leader());
        Assert.Equal(new[] {
            new SceneDrawCommand(SceneDrawSource.Cover, 12, 1, 16, 8, 31, 8),
            new SceneDrawCommand(SceneDrawSource.Cover, 12, 1, 16, 8, 31, 8),
            new SceneDrawCommand(SceneDrawSource.Cover, 12, 2, 16, 4, 32, 9),
            new SceneDrawCommand(SceneDrawSource.Cover, 12, 2, 16, 4, 32, 9),
            new SceneDrawCommand(SceneDrawSource.Object, 7, 0, 22, 2, 34, 2, 93),
            new SceneDrawCommand(SceneDrawSource.Object, 7, 0, 22, 2, 34, 2, 94),
            new SceneDrawCommand(SceneDrawSource.Leader, 2, 0, 150, 86, 122, 6)
        }, plan.Commands.Where(c => c.Source != SceneDrawSource.Background));
    }

    [Fact]
    public void Sprite_list_accepts_2048_entries_and_fails_explicitly_before_a_2049th_entry()
    {
        var sprite = new SceneSprite(7, [new SceneFrame(0, 20, 30)]);
        var objects = Enumerable.Range(1, 2048).Select(id =>
            new SceneObject((ushort)id, 160, 112, 1, 0, 0, 7, 1, 0, 0, sprite)).ToArray();
        var allowed = new StaticSceneRules(DrawMap(), objects.Take(2047)).BuildPlan(Leader());
        Assert.Equal(2048, allowed.Commands.Count(c => c.Source != SceneDrawSource.Background));
        var error = Assert.Throws<InvalidOperationException>(() =>
            new StaticSceneRules(DrawMap(), objects).BuildPlan(Leader()));
        Assert.Contains("2048", error.Message);
    }

    [Theory]
    [InlineData(0, 2, 6, 0)]
    [InlineData(4, 3, 12, 1)]
    [InlineData(5, 2, 10, 2)]
    [InlineData(65535, 3, 65533, 3)]
    public void Standing_uses_raw_stride_word_assignment_and_preserves_each_initial_phase(int word, int direction, int frame, int phase)
    {
        var leader = new StaticLeader(160, 112, direction, 65535, phase, (ushort)word,
            new SceneSprite(2, [new SceneFrame(frame, 20, 30)]));
        var plan = new StaticSceneRules(DrawMap()).BuildPlan(leader);
        Assert.Equal(frame, plan.LeaderFrameIndex);
        Assert.Equal(phase, plan.StepPhase);
        Assert.Equal(new SceneDrawCommand(SceneDrawSource.Leader, 2, frame, 150, 86, 121, 65541),
            Assert.Single(plan.Commands, c => c.Source == SceneDrawSource.Leader));
    }

    [Fact]
    public void Negative_cover_scan_uses_c_truncation_and_null_queries_not_background_fallback()
    {
        var cells = new uint[16384];
        cells[0] = 0x01020100;
        var scene = new StaticSceneRules(new SceneMap(12, cells,
            [new SceneFrame(0, 32, 15), new SceneFrame(1, 32, 23)]));
        var plan = scene.BuildPlan(Leader() with { X = 0, Y = 0 });
        Assert.Equal((-160, -112), (plan.CameraX, plan.CameraY));
        Assert.Equal(new[] {
            new SceneDrawCommand(SceneDrawSource.Cover, 12, 0, 144, 104, 127, 8),
            new SceneDrawCommand(SceneDrawSource.Cover, 12, 1, 144, 96, 128, 9)
        }, plan.Commands.Where(c => c.Source == SceneDrawSource.Cover));
    }

    [Fact]
    public void Object_coordinates_and_sort_keys_unpack_signed_shorts_before_stable_sort_and_final_top_packing()
    {
        var sprite = new SceneSprite(7, [new SceneFrame(0, 20, 30)]);
        var obj = new SceneObject(93, 65535, 112, 1, 0, 4096, 7, 1, 0, 0, sprite);
        var plan = new StaticSceneRules(DrawMap(), [obj, obj with { Id = 94, X = 160, Layer = -4096 }]).BuildPlan(Leader());
        Assert.Equal(new[] {
            new SceneDrawCommand(SceneDrawSource.Object, 7, 0, -11, 89, -32647, 32770, 93),
            new SceneDrawCommand(SceneDrawSource.Object, 7, 0, 150, 89, -32647, -32766, 94),
            new SceneDrawCommand(SceneDrawSource.Leader, 2, 0, 150, 86, 122, 6)
        }, plan.Commands.Where(c => c.Source != SceneDrawSource.Background));
    }

    [Fact]
    public void Missing_required_frames_fail_explicitly_but_hidden_vanished_and_spriteless_objects_stay_unmodified()
    {
        var sprite = new SceneSprite(7, []);
        var obj = new SceneObject(93, 65000, 65000, 1, 0, 0, 7, 3, 1, 3, sprite);
        var error = Assert.Throws<InvalidOperationException>(() => new StaticSceneRules(DrawMap(), [obj]).BuildPlan(Leader()));
        Assert.Contains("Object 93 sprite 7 is missing frame 5", error.Message);
        Assert.Throws<InvalidOperationException>(() => new StaticSceneRules(DrawMap()).BuildPlan(
            Leader() with { Sprite = new SceneSprite(2, []) }));
        Assert.Throws<InvalidOperationException>(() => new StaticSceneRules(new SceneMap(12, new uint[16384])).BuildPlan(Leader()));
        var hidden = new[] { obj with { State = 0 }, obj with { Id = 94, State = -1 },
            obj with { Id = 95, VanishTime = 1 }, obj with { Id = 96, Sprite = null } };
        var scene = new StaticSceneRules(DrawMap(), hidden);
        var plan = scene.BuildPlan(Leader());
        Assert.DoesNotContain(plan.Commands, c => c.Source == SceneDrawSource.Object);
        Assert.Equal(hidden, scene.Objects);
    }

    [Theory]
    [InlineData(15, 0, 0)]
    [InlineData(16, 0, 1)]
    [InlineData(31, 7, 2)]
    [InlineData(31, 8, 1)]
    [InlineData(31, 9, 130)]
    [InlineData(0, 8, 128)]
    [InlineData(0, 7, 0)]
    [InlineData(16, 16, 129)]
    [InlineData(-1, 9, 128)]
    [InlineData(-1, 8, 0)]
    public void Collision_diamond_boundary_hits_the_literal_raw_cell(int x, int y, int blockedIndex)
    {
        var cells = new uint[16384];
        cells[blockedIndex] = 0x2000;
        Assert.True(new StaticSceneRules(new SceneMap(12, cells)).IsBlocked(x, y, checkRange: false));
        Assert.False(new StaticSceneRules(new SceneMap(12, new uint[16384])).IsBlocked(x, y, checkRange: false));
    }

    [Fact]
    public void Cover_queries_wrap_to_byte_but_positions_keep_unwrapped_candidate_coordinates()
    {
        var cells = new uint[16384];
        cells[778] = 0x0201; // (5,6,0), frame1 height2
        var scene = new StaticSceneRules(new SceneMap(12, cells,
            [new SceneFrame(0, 32, 15), new SceneFrame(1, 32, 15)]));
        var plan = scene.BuildPlan(Leader() with { X = -32609 });
        Assert.Equal(32767, plan.CameraX);
        Assert.Equal(new SceneDrawCommand(SceneDrawSource.Cover, 12, 1, 145, 88, 119, 16),
            Assert.Single(plan.Commands, c => c.Source == SceneDrawSource.Cover));
    }

    [Fact]
    public void Equal_sort_keys_keep_leader_cover_then_object_then_its_cover_in_insertion_order()
    {
        var cells = new uint[16384];
        cells[778] = 0x0201;
        var obj = new SceneObject(93, 160, 110, 1, 0, 0, 7, 1, 0, 0,
            new SceneSprite(7, [new SceneFrame(0, 20, 30)]));
        var plan = new StaticSceneRules(new SceneMap(12, cells,
            [new SceneFrame(0, 32, 15), new SceneFrame(1, 32, 15)]), [obj]).BuildPlan(Leader());
        Assert.Equal(new[] {
            new SceneDrawCommand(SceneDrawSource.Cover, 12, 1, 144, 88, 119, 16),
            new SceneDrawCommand(SceneDrawSource.Object, 7, 0, 150, 87, 119, 2, 93),
            new SceneDrawCommand(SceneDrawSource.Cover, 12, 1, 144, 88, 119, 16),
            new SceneDrawCommand(SceneDrawSource.Leader, 2, 0, 150, 86, 122, 6)
        }, plan.Commands.Where(c => c.Source != SceneDrawSource.Background));
    }

    [Fact]
    public void Wide_actor_cover_positions_pack_signed_shorts_and_keep_each_byte_wrapped_candidate()
    {
        var cells = new uint[16384];
        cells[896] = 0x0101; // (0,7,0), frame1 height1
        var plan = new StaticSceneRules(new SceneMap(12, cells,
            [new SceneFrame(0, 32, 15), new SceneFrame(1, 32, 15)])).BuildPlan(
            Leader() with { Sprite = new SceneSprite(2, [new SceneFrame(0, 65535, 1)]) });
        var covers = plan.Commands.Where(c => c.Source == SceneDrawSource.Cover).ToArray();
        Assert.Equal(new[] { 8176, 16368, 24560, 32752, -24592, -16400, -8208, -16 }, covers.Select(c => c.Left));
        Assert.All(covers, c => Assert.Equal((104, 127, 8, 1), (c.Top, c.SortY, c.Layer, c.FrameIndex)));
    }

    [Fact]
    public void Zero_half_scan_continues_with_only_the_two_right_candidates_for_subsequent_columns()
    {
        var cells = new uint[16384];
        cells[132] = 0x0101; // (2,1,0)
        cells[129] = 0x0102; // (0,1,1)
        cells[131] = 0x0103; // (1,1,1)
        var obj = new SceneObject(93, 43, 25, 1, 0, 0, 7, 1, 0, 0,
            new SceneSprite(7, [new SceneFrame(0, 20, 30)]));
        var plan = new StaticSceneRules(new SceneMap(12, cells,
            [new SceneFrame(0, 32, 15), new SceneFrame(1, 32, 15),
             new SceneFrame(2, 32, 15), new SceneFrame(3, 32, 15)]), [obj]).BuildPlan(Leader());
        Assert.Equal(new[] {
            new SceneDrawCommand(SceneDrawSource.Cover, 12, 1, 48, 8, 31, 8),
            new SceneDrawCommand(SceneDrawSource.Object, 7, 0, 33, 2, 34, 2, 93),
            new SceneDrawCommand(SceneDrawSource.Cover, 12, 2, 0, 16, 39, 8),
            new SceneDrawCommand(SceneDrawSource.Cover, 12, 3, 32, 16, 39, 8),
            new SceneDrawCommand(SceneDrawSource.Leader, 2, 0, 150, 86, 122, 6)
        }, plan.Commands.Where(c => c.Source != SceneDrawSource.Background));
    }

    [Fact]
    public void Scene_snapshot_owns_immutable_copies_and_rejects_conflicting_object_sprite_identity()
    {
        var cells = new uint[16384];
        var frames = new[] { new SceneFrame(0, 20, 30) };
        var sprite = new SceneSprite(7, frames);
        var objects = new[] { new SceneObject(93, 160, 112, 2, 0, 0, 7, 1, 0, 0, sprite) };
        var scene = new StaticSceneRules(new SceneMap(12, cells, [new SceneFrame(0, 32, 15)]), objects);
        cells[0] = 0x2000;
        frames[0] = new SceneFrame(0, 50, 60);
        objects[0] = objects[0] with { State = 0 };
        Assert.False(scene.IsBlocked(0, 0, checkRange: false));
        Assert.True(scene.IsBlocked(160, 112));
        Assert.Equal(new SceneDrawCommand(SceneDrawSource.Object, 7, 0, 150, 89, 121, 2, 93),
            Assert.Single(scene.BuildPlan(Leader()).Commands, c => c.Source == SceneDrawSource.Object));
        Assert.Throws<ArgumentException>(() => new StaticSceneRules(DrawMap(),
            [objects[0] with { SpriteNumber = 8 }]));
    }

    [Theory]
    [InlineData(65526, 112, true)]
    [InlineData(65525, 112, false)]
    [InlineData(329, 112, true)]
    [InlineData(330, 112, false)]
    [InlineData(160, 65525, true)]
    [InlineData(160, 65524, false)]
    [InlineData(160, 218, true)]
    [InlineData(160, 219, false)]
    public void Object_culling_uses_reference_vy_and_real_frame_size(int x, int y, bool visible)
    {
        var obj = new SceneObject(93, (ushort)x, (ushort)y, 1, 0, 9, 7, 1, 0, 0,
            new SceneSprite(7, [new SceneFrame(0, 20, 30)]));
        var plan = new StaticSceneRules(DrawMap(), [obj]).BuildPlan(Leader());
        Assert.Equal(visible, plan.Commands.Any(c => c.Source == SceneDrawSource.Object));
    }

    [Fact]
    public void Sprite_list_limit_also_counts_every_cover_and_not_background_commands()
    {
        var cells = new uint[16384];
        cells[778] = 0x0201;
        var map = new SceneMap(12, cells, [new SceneFrame(0, 32, 15), new SceneFrame(1, 32, 15)]);
        var sprite = new SceneSprite(7, [new SceneFrame(0, 20, 30)]);
        var objects = Enumerable.Range(1, 1024).Select(id =>
            new SceneObject((ushort)id, 160, 110, 1, 0, 0, 7, 1, 0, 0, sprite)).ToArray();
        var allowed = new StaticSceneRules(map, objects.Take(1023)).BuildPlan(Leader());
        Assert.Equal(2048, allowed.Commands.Count(c => c.Source != SceneDrawSource.Background));
        Assert.Throws<InvalidOperationException>(() => new StaticSceneRules(map, objects).BuildPlan(Leader()));
    }

    [Fact]
    public void Invalid_metadata_and_unvalidated_direction_or_phase_fail_explicitly()
    {
        Assert.Throws<ArgumentException>(() => new SceneMap(12, new uint[1]));
        Assert.Throws<ArgumentException>(() => new SceneSprite(2, [new SceneFrame(0, 0, 30)]));
        Assert.Throws<ArgumentException>(() => new SceneSprite(2, [new SceneFrame(0, 20, 65536)]));
        Assert.Throws<ArgumentException>(() => new SceneSprite(2, [new SceneFrame(-1, 20, 30)]));
        Assert.Throws<ArgumentException>(() => new SceneSprite(2, [new SceneFrame(0, 20, 30), new SceneFrame(0, 20, 30)]));
        var obj = new SceneObject(1, 160, 112, 2, 0, 0, 0, 0, 0, 0);
        Assert.Throws<ArgumentException>(() => new StaticSceneRules(DrawMap(), [obj with { Id = 0 }]));
        Assert.Throws<ArgumentException>(() => new StaticSceneRules(DrawMap(), [obj, obj]));
        var scene = new StaticSceneRules(DrawMap());
        Assert.Throws<ArgumentOutOfRangeException>(() => scene.BuildPlan(Leader() with { Direction = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => scene.BuildPlan(Leader() with { Direction = 4 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => scene.BuildPlan(Leader() with { StepPhase = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => scene.BuildPlan(Leader() with { StepPhase = 4 }));
    }

    private static SceneMap DrawMap() => new(12, new uint[16384], [new SceneFrame(0, 32, 15)]);
    private static StaticLeader Leader() => new(160, 112, 0, 0, 0, 3,
        new SceneSprite(2, [new SceneFrame(0, 20, 30)]));

    [Fact]
    public void Collision_uses_the_diamond_half_row_blocking_bit()
    {
        var cells = new uint[64 * 128 * 2];
        cells[1] = 0x2000;
        var scene = new StaticSceneRules(new SceneMap(12, cells));

        Assert.False(scene.IsBlocked(0, 0, checkRange: false));
        Assert.True(scene.IsBlocked(16, 0, checkRange: false));
    }
}
