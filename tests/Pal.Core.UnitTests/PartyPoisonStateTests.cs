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

    // Independent oracle: script.c:2164-2197 clears ALL poison ID/script pairs before
    // PAL_UpdateEquipments, including unchanged members; this is not role-based migration.
    [Fact]
    public void Party_reset_clears_all_poison_pairs_and_updates_the_same_owner_without_migrating_old_rows()
    {
        int[] initialParty = [5, 3];
        var full = Enumerable.Repeat(new PoisonSlot(65535, 65535), 16).ToArray();
        var sparse = Row(new(7, 123), new(8, 65535), new(0, 444), new(7, 555));
        var definitions = new Dictionary<int, int> { [7] = 99, [8] = 98, [65535] = 65535 };
        var owner = new PartyPoisonState(initialParty, [full, sparse], definitions);
        var isolated = new PartyPoisonState(initialParty, [full, sparse], definitions);
        var membership = owner.PartyRoleIds;
        var retained5 = owner.Slots(5);
        var retained3 = owner.Slots(3);
        var previous = retained5[15];
        int[] next = [3, 5, 1];
        var alias = owner;
        alias.ResetForPartyChange(next);
        Assert.Equal(new[] { 3, 5, 1 }, membership); // Designated RED: actual [5,3].
        Assert.Same(owner, alias);
        Assert.Same(membership, owner.PartyRoleIds);
        Assert.Same(retained5, owner.Slots(5));
        Assert.Same(retained3, owner.Slots(3));
        foreach (int role in membership)
        {
            Assert.Equal(16, owner.Slots(role).Count);
            Assert.All(owner.Slots(role), slot => Assert.Equal(new PoisonSlot(0, 0), slot));
        }
        Assert.Equal(new PoisonSlot(65535, 65535), previous);
        Assert.Equal(new PoisonSlot(65535, 65535), full[15]);
        Assert.Equal(new PoisonSlot(7, 555), sparse[3]);
        Assert.Equal(new PoisonSlot(65535, 65535), isolated.Slots(5)[15]);
        Assert.Equal(new PoisonSlot(0, 444), isolated.Slots(3)[2]);
        Assert.Equal(new[] { 5, 3 }, initialParty);
        Assert.Equal(99, definitions[7]);
        next[0] = 0; // No caller-owned membership array is retained.
        Assert.Equal(new[] { 3, 5, 1 }, membership);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)membership)[0] = 0);
        Assert.Throws<NotSupportedException>(() => ((IList<PoisonSlot>)owner.Slots(1))[0] = new(7, 9));

        // Removing/rejoining a role never restores its prior pairs or rebinds an old view.
        owner.ResetForPartyChange([1, 0, 4, 2, 3]);
        Assert.Equal(new[] { 1, 0, 4, 2, 3 }, membership);
        Assert.Throws<ArgumentException>(() => owner.Slots(5));
        Assert.All(retained5, slot => Assert.Equal(new PoisonSlot(0, 0), slot));
        owner.RemoveEquipmentPoisons(5); // Absent-role cleanup remains a no-op.
        owner.ResetForPartyChange([5, 3]);
        Assert.NotSame(retained5, owner.Slots(5));
        Assert.Same(retained3, owner.Slots(3));
        Assert.All(owner.Slots(5), slot => Assert.Equal(new PoisonSlot(0, 0), slot));
        Assert.All(retained5, slot => Assert.Equal(new PoisonSlot(0, 0), slot));
        owner.ResetForPartyChange([]);
        Assert.Empty(membership);
        Assert.Throws<ArgumentException>(() => owner.Slots(3));
        Assert.All(retained3, slot => Assert.Equal(new PoisonSlot(0, 0), slot));
        owner.ResetForPartyChange([]);
        owner.ResetForPartyChange([0]);
        owner.RemoveEquipmentPoisons(0);
        Assert.All(owner.Slots(0), slot => Assert.Equal(new PoisonSlot(0, 0), slot));
        var empty = new PartyPoisonState([], [], new Dictionary<int, int>());
        empty.ResetForPartyChange([5, 4, 3, 2, 1]); // No poison definitions required for new zero rows.
        foreach (int role in empty.PartyRoleIds)
            Assert.All(empty.Slots(role), slot => Assert.Equal(new PoisonSlot(0, 0), slot));

        // Even a same-party write clears the prefix, ID0's script and hidden suffix.
        var same = new PartyPoisonState([3], [sparse], definitions);
        var sameView = same.Slots(3);
        same.ResetForPartyChange(same.PartyRoleIds); // Aliased read-only input is supported.
        Assert.Equal(new[] { 3 }, same.PartyRoleIds);
        Assert.Same(sameView, same.Slots(3));
        Assert.All(sameView, slot => Assert.Equal(new PoisonSlot(0, 0), slot));
        same.ResetForPartyChange([3]);
        Assert.All(sameView, slot => Assert.Equal(new PoisonSlot(0, 0), slot));

        // Invalid membership rejects before clearing any existing pair or changing live views.
        var guarded = new PartyPoisonState([5, 3], [full, sparse], definitions);
        var guardedParty = guarded.PartyRoleIds;
        var guarded5 = guarded.Slots(5);
        var guarded3 = guarded.Slots(3);
        Assert.Throws<ArgumentNullException>(() => guarded.ResetForPartyChange(null!));
        foreach (int invalid in new[] { -1, 6, int.MinValue, int.MaxValue })
            Assert.Throws<ArgumentOutOfRangeException>(() => guarded.ResetForPartyChange([3, invalid]));
        Assert.Throws<ArgumentException>(() => guarded.ResetForPartyChange([3, 5, 3]));
        Assert.Throws<ArgumentException>(() => guarded.ResetForPartyChange([0, 1, 2, 3, 4, 5]));
        Assert.Same(guardedParty, guarded.PartyRoleIds);
        Assert.Equal(new[] { 5, 3 }, guardedParty);
        Assert.Same(guarded5, guarded.Slots(5));
        Assert.Same(guarded3, guarded.Slots(3));
        Assert.Equal(full, guarded5);
        Assert.Equal(sparse, guarded3);

        // Existing real equipment/count/removal consumers read this owner's NEW party order.
        IReadOnlyList<int>[] grid = [new int[6], new int[6], new int[6],
            [7, 0, 0, 0, 0, 0], new int[6], [7, 0, 0, 0, 0, 0]];
        var zero = new BattleEquipmentEffect(0, 0, 0, 0, new int[9]);
        var gear = BattleEquipmentState.Initialize(grid, new Dictionary<int, BattleEquipmentObject>
            { [7] = new(7, Enumerable.Repeat(zero, 6).ToArray()) });
        var roles = new RoleLiveState(new[] { 3, 5 }.Select(id => new BattleRoleSnapshot(id, 1, 100, 100, 10, 10,
            new(0, 0, 0), new Dictionary<string, BattleExperienceSnapshot>(), grid[id], [], new int[9], 0)).ToArray());
        var inventory = new InventoryLiveState([]);
        guarded.ResetForPartyChange([3, 5]);
        Assert.Equal(2, gear.CountItem(7, inventory, guarded.PartyRoleIds));
        Assert.True(gear.TryRemoveItem(7, 1, false, inventory, roles, guarded));
        Assert.Equal(0, gear.Equipment(3)[0]);
        Assert.Equal(7, gear.Equipment(5)[0]);
        Assert.Equal(0, roles.Roles[3].Equipment[0]);
        Assert.Equal(7, roles.Roles[5].Equipment[0]);
        guarded.ResetForPartyChange([0]);
        Assert.Equal(0, gear.CountItem(7, inventory, guarded.PartyRoleIds));
        Assert.False(gear.TryRemoveItem(7, 1, false, inventory, roles, guarded));
        Assert.Equal(7, gear.Equipment(5)[0]);
        Assert.DoesNotContain(inventory.Slots, slot => slot.ItemId != 0);
    }

    private static PoisonSlot[] Row(params PoisonSlot[] prefix) => prefix.Concat(Enumerable.Repeat(new PoisonSlot(0, 0), 16 - prefix.Length)).ToArray();
}
