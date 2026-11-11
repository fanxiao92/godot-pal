using Xunit;

namespace Pal.Core.Tests;

public sealed class PartyPoisonStateTests
{
    // Independent oracle: global.c:1413-1455, global.h POISONSTATUS and OBJECT_POISON.
    [Fact]
    public void Wear_cleanup_preserves_low_level_poison_script_pairs_in_current_party_order_and_clears_the_tail()
    {
        int[] party = [5, 3];
        var levels = new Dictionary<int, int> { [7] = 99, [8] = 98, [9] = 100, [10] = 0, [11] = 1, [65535] = 65535 };
        var row5 = Row(new(7, 1001), new(8, 1002), new(9, 65535), new(10, 0), new(0, 444), new(11, 1005));
        var row3 = Row(new(65535, 65535), new(11, 65535));
        IReadOnlyList<PoisonSlot>[] rows = [row5, row3];
        var state = new PartyPoisonState(party, rows, levels);
        var isolated = new PartyPoisonState(party, rows, levels);
        var view = state.Slots(5);
        var prior = view[0];
        state.RemoveEquipmentPoisons(5);
        Assert.Equal(new PoisonSlot(8, 1002), view[0]); // Frozen RED: expected8/1002, actual7/1001.
        Assert.Equal(new PoisonSlot(10, 0), view[1]);
        Assert.All(view.Skip(2), slot => Assert.Equal(new PoisonSlot(0, 0), slot));
        Assert.Equal(16, view.Count);
        Assert.Equal(new PoisonSlot(7, 1001), prior);
        Assert.Equal(new PoisonSlot(7, 1001), row5[0]);
        Assert.Equal(new PoisonSlot(11, 1005), row5[5]);
        Assert.Equal(new PoisonSlot(65535, 65535), state.Slots(3)[0]);
        Assert.Equal(new PoisonSlot(11, 65535), state.Slots(3)[1]);
        Assert.Equal(new PoisonSlot(7, 1001), isolated.Slots(5)[0]);
        Assert.Equal(new[] { 5, 3 }, state.PartyRoleIds);
        Assert.Throws<NotSupportedException>(() => ((IList<PoisonSlot>)view)[0] = new(7, 2));
        Assert.Throws<NotSupportedException>(() => ((IList<int>)state.PartyRoleIds)[0] = 0);

        // Neither membership, outer/inner seed arrays, nor static definitions are borrowed.
        party[0] = 0;
        rows[0] = Row();
        row3[1] = new(7, 123);
        levels[11] = 99;
        levels.Remove(65535);
        Assert.Equal(new[] { 5, 3 }, state.PartyRoleIds);
        var shared = state;
        shared.RemoveEquipmentPoisons(3);
        Assert.Equal(new PoisonSlot(11, 65535), state.Slots(3)[0]);
        Assert.All(state.Slots(3).Skip(1), slot => Assert.Equal(new PoisonSlot(0, 0), slot));
        Assert.Equal(new PoisonSlot(65535, 65535), isolated.Slots(3)[0]);
        state.RemoveEquipmentPoisons(5);
        Assert.Equal(new PoisonSlot(8, 1002), view[0]);
        Assert.Equal(new PoisonSlot(10, 0), view[1]);
        Assert.All(view.Skip(2), slot => Assert.Equal(new PoisonSlot(0, 0), slot));
        foreach (int absent in new[] { 0, 1, 2, 4 }) state.RemoveEquipmentPoisons(absent);
        Assert.Equal(new PoisonSlot(8, 1002), view[0]);
        Assert.Equal(new PoisonSlot(11, 65535), state.Slots(3)[0]);
        Assert.Throws<ArgumentException>(() => state.Slots(0));

        // The first ID0 stops scanning, including its stale script and ALL later nonzero IDs.
        var gap = new PartyPoisonState([5], [Row(new(0, 65535), new(8, 77), new(7, 88))], new Dictionary<int, int> { [8] = 98, [7] = 99 });
        gap.RemoveEquipmentPoisons(5);
        Assert.All(gap.Slots(5), slot => Assert.Equal(new PoisonSlot(0, 0), slot));
        var empty = new PartyPoisonState([], [], new Dictionary<int, int>());
        Assert.Empty(empty.PartyRoleIds);
        empty.RemoveEquipmentPoisons(5);
        Assert.Throws<ArgumentException>(() => empty.Slots(5));

        // Full16, final slot retained, repeated IDs and stable script order; no sorting or ID cap.
        var full = new PartyPoisonState([0], [Enumerable.Repeat(new PoisonSlot(8, 12), 15).Append(new(65535, 65535)).ToArray()],
            new Dictionary<int, int> { [8] = 98, [65535] = 1 });
        full.RemoveEquipmentPoisons(0);
        Assert.All(full.Slots(0).Take(15), slot => Assert.Equal(new PoisonSlot(8, 12), slot));
        Assert.Equal(new PoisonSlot(65535, 65535), full.Slots(0)[15]);
        var last = new PartyPoisonState([3], [Enumerable.Repeat(new PoisonSlot(7, 2), 15).Append(new(8, 65535)).ToArray()],
            new Dictionary<int, int> { [7] = 99, [8] = 98 });
        last.RemoveEquipmentPoisons(3);
        Assert.Equal(new PoisonSlot(8, 65535), last.Slots(3)[0]);
        Assert.All(last.Slots(3).Skip(1), slot => Assert.Equal(new PoisonSlot(0, 0), slot));
        var high = new PartyPoisonState([1], [Enumerable.Repeat(new PoisonSlot(65535, 65535), 16).ToArray()],
            new Dictionary<int, int> { [65535] = 65535 });
        high.RemoveEquipmentPoisons(1);
        Assert.All(high.Slots(1), slot => Assert.Equal(new PoisonSlot(0, 0), slot));
        var ordered = new PartyPoisonState([4], [Row(new(8, 9), new(11, 8), new(8, 7))], new Dictionary<int, int> { [8] = 98, [11] = 1 });
        ordered.RemoveEquipmentPoisons(4);
        Assert.Equal(new PoisonSlot(8, 9), ordered.Slots(4)[0]);
        Assert.Equal(new PoisonSlot(11, 8), ordered.Slots(4)[1]);
        Assert.Equal(new PoisonSlot(8, 7), ordered.Slots(4)[2]);
        Assert.All(ordered.Slots(4).Skip(3), slot => Assert.Equal(new PoisonSlot(0, 0), slot));
        var five = new PartyPoisonState([5, 3, 1, 4, 0], [Row(), Row(), Row(), Row(), Row()], new Dictionary<int, int>());
        Assert.Equal(new[] { 5, 3, 1, 4, 0 }, five.PartyRoleIds);
        foreach (int id in five.PartyRoleIds)
        {
            five.RemoveEquipmentPoisons(id);
            Assert.All(five.Slots(id), slot => Assert.Equal(new PoisonSlot(0, 0), slot));
        }

        // Validate before writes, and validate all seed words/definitions even after a hole.
        var before = view.ToArray();
        foreach (int invalid in new[] { -1, 6, int.MinValue, int.MaxValue })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => state.RemoveEquipmentPoisons(invalid));
            Assert.Throws<ArgumentOutOfRangeException>(() => state.Slots(invalid));
        }
        Assert.Equal(before, view);
        Assert.Equal(new PoisonSlot(11, 65535), state.Slots(3)[0]);
        Assert.Throws<ArgumentNullException>(() => new PartyPoisonState(null!, [], new Dictionary<int, int>()));
        Assert.Throws<ArgumentNullException>(() => new PartyPoisonState([], null!, new Dictionary<int, int>()));
        Assert.Throws<ArgumentNullException>(() => new PartyPoisonState([], [], null!));
        foreach (int[] invalid in new int[][] { [-1], [6], [int.MinValue], [int.MaxValue], [3, 3], [0, 1, 2, 3, 4, 5] })
            Assert.Throws<InvalidDataException>(() => new PartyPoisonState(invalid, invalid.Select(_ => (IReadOnlyList<PoisonSlot>)Row()).ToArray(), new Dictionary<int, int>()));
        Assert.Throws<InvalidDataException>(() => new PartyPoisonState([0], [], new Dictionary<int, int>()));
        Assert.Throws<InvalidDataException>(() => new PartyPoisonState([], [Row()], new Dictionary<int, int>()));
        Assert.Throws<InvalidDataException>(() => new PartyPoisonState([0], [null!], new Dictionary<int, int>()));
        Assert.Throws<InvalidDataException>(() => new PartyPoisonState([0], [Array.Empty<PoisonSlot>()], new Dictionary<int, int>()));
        Assert.Throws<InvalidDataException>(() => new PartyPoisonState([0], [Enumerable.Repeat(new PoisonSlot(0, 0), 17).ToArray()], new Dictionary<int, int>()));
        Assert.Throws<InvalidDataException>(() => new PartyPoisonState([0], [Row((PoisonSlot)null!)], new Dictionary<int, int>()));
        foreach (var invalid in new PoisonSlot[] { new(-1, 0), new(65536, 0), new(0, -1), new(0, 65536), new(int.MaxValue, 0), new(0, int.MinValue) })
            Assert.Throws<InvalidDataException>(() => new PartyPoisonState([0], [Row(invalid)], new Dictionary<int, int>()));
        Assert.Throws<InvalidDataException>(() => new PartyPoisonState([0], [Row(new PoisonSlot(7, 2))], new Dictionary<int, int>()));
        Assert.Throws<InvalidDataException>(() => new PartyPoisonState([0], [Row(new(0, 2), new(7, 2))], new Dictionary<int, int>()));
        foreach (var (id, level) in new (int, int)[] { (0, 0), (-1, 0), (65536, 0), (1, -1), (1, 65536), (int.MinValue, 0), (1, int.MaxValue) })
            Assert.Throws<InvalidDataException>(() => new PartyPoisonState([], [], new Dictionary<int, int> { [id] = level }));
        Assert.Equal(before, view);
        Assert.Equal(new PoisonSlot(7, 1001), isolated.Slots(5)[0]);
    }

    private static PoisonSlot[] Row(params PoisonSlot[] prefix) => prefix.Concat(Enumerable.Repeat(new PoisonSlot(0, 0), 16 - prefix.Length)).ToArray();
}
