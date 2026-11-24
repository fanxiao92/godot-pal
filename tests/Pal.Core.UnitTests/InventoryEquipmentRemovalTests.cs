using Pal.Core;
using Xunit;

public sealed class InventoryEquipmentRemovalTests
{
    [Fact]
    public void Count_gated_removal_consumes_stock_then_current_party_gear_in_reference_order_without_refund()
    {
        // script.c:977-1025: stock2 + role5's first three matching slots supply count5.
        // Party identity/order is [5,3], NOT all six stored roles or numeric role order.
        var state = Seed();
        var oldStock = state.Stock.Slots[0];
        var oldRole = state.Roles.Roles[5];
        var oldGear = state.Gear.Equipment(5);
        var roleView = state.Roles.Roles;
        Assert.True(state.Gear.TryRemoveItem(7, 5, false, state.Stock, state.Roles, state.Poisons));
        Assert.Equal(0, state.Stock.GetAmount(7)); // Designated RED: no-op leaves2.
        Assert.Equal(new InventorySlot(7, 0, 65535), state.Stock.Slots[0]);
        Assert.Equal(new InventorySlot(7, 2, 65535), oldStock);
        Assert.Equal([0, 0, 0, 0, 0, 7], state.Gear.Equipment(5));
        Assert.Equal([0, 0, 0, 0, 0, 7], roleView[5].Equipment);
        Assert.Equal([7, 7, 0, 0, 0, 0], state.Gear.Equipment(3));
        Assert.Equal([7, 0, 0, 0, 0, 0], state.Gear.Equipment(0));
        Assert.Equal(0, roleView[5].Statuses[8]);
        Assert.Equal(0, state.Gear.ApplyStatuses(5, roleView[5].Statuses)[8]);
        Assert.Equal(7, state.Gear.AttackBonus(5));
        Assert.Equal(14, state.Gear.AttackBonus(3));
        Assert.Equal(0, state.Gear.AttackAllWord(5, 3));
        Assert.Equal(0, state.Gear.CooperativeMagicWord(5, 2));
        Assert.Equal(new PoisonSlot(10, 1001), state.Poisons.Slots(5)[0]); // Wear not yet removed.
        Assert.Equal([7, 0, 7, 7, 0, 7], oldGear);
        Assert.Equal([7, 0, 7, 7, 0, 7], oldRole.Equipment);
        Assert.Equal(60000, oldRole.Statuses[8]);
        Assert.Equal((100, 150, 20, 50, 11, 12, 13, 14, 15),
            (roleView[5].Hp, roleView[5].MaxHp, roleView[5].Mp, roleView[5].MaxMp,
                roleView[5].Attack, roleView[5].MagicPower, roleView[5].Defense, roleView[5].Dexterity, roleView[5].FleeRate));
        Assert.Equal([9, 0], roleView[5].Magics);
        Assert.Equal(1000, roleView[5].Statuses[5]);
        Assert.Equal(new BattleExperienceSnapshot(5, 2, 17), roleView[5].PrimaryExperience);
        Assert.Equal(3, state.Gear.CountItem(7, state.Stock, state.Poisons.PartyRoleIds));

        // A later explicit call consumes CURRENT gear, not a reseeded copy. Wear prunes poison pairs.
        Assert.True(state.Gear.TryRemoveItem(7, 0, false, state.Stock, state.Roles, state.Poisons));
        Assert.Equal(new int[6], state.Gear.Equipment(5));
        Assert.Equal(new int[6], roleView[5].Equipment);
        Assert.Equal(0, state.Gear.AttackBonus(5));
        Assert.Equal(2, state.Gear.BattleSprite(5, 2));
        Assert.Equal(new PoisonSlot(11, 65535), state.Poisons.Slots(5)[0]);
        Assert.All(state.Poisons.Slots(5).Skip(1), slot => Assert.Equal(new PoisonSlot(0, 0), slot));
        Assert.Equal(new PoisonSlot(10, 1001), state.Poisons.Slots(3)[0]);
        Assert.True(state.Gear.TryRemoveItem(7, 1, false, state.Stock, state.Roles, state.Poisons));
        Assert.Equal([0, 7, 0, 0, 0, 0], state.Gear.Equipment(3));
        Assert.True(state.Gear.TryRemoveItem(7, 1, false, state.Stock, state.Roles, state.Poisons));
        Assert.False(state.Gear.TryRemoveItem(7, 1, false, state.Stock, state.Roles, state.Poisons));
        Assert.Equal(7, state.Gear.AttackBonus(0)); // Spectator survives all calls.
        Assert.True(state.Gear.TryRemoveItem(7, 65535, true, state.Stock, state.Roles, state.Poisons));
        Assert.Equal(new InventorySlot(7, 0, 65535), state.Stock.Slots[0]);
        Assert.Equal(7, state.Gear.AttackBonus(0));

        var rejected = Seed();
        var rejectedRole = rejected.Roles.Roles[5];
        Assert.False(rejected.Gear.TryRemoveItem(7, 9, false, rejected.Stock, rejected.Roles, rejected.Poisons));
        Assert.Equal(2, rejected.Stock.GetAmount(7)); // Eight total: no partial removal when target exists.
        Assert.Same(rejectedRole, rejected.Roles.Roles[5]);
        Assert.Equal(28, rejected.Gear.AttackBonus(5));
        Assert.Equal(new PoisonSlot(10, 1001), rejected.Poisons.Slots(5)[0]);
        Assert.True(rejected.Gear.TryRemoveItem(7, 9, true, rejected.Stock, rejected.Roles, rejected.Poisons));
        Assert.Equal(0, rejected.Stock.GetAmount(7)); // No target: consume all eight, no refund for missing ninth.
        Assert.Equal(new int[6], rejected.Gear.Equipment(5));
        Assert.Equal(new int[6], rejected.Gear.Equipment(3));
        Assert.Equal(0, rejected.Roles.Roles[5].Statuses[8]);
        Assert.Equal(7, rejected.Gear.AttackBonus(0));
        Assert.Equal(new PoisonSlot(11, 65535), rejected.Poisons.Slots(5)[0]);

        // Stock-only path needs no fabricated live role rows. Exact and zero-count removal do not touch gear.
        var onlyStock = Seed(9);
        var emptyRoles = new RoleLiveState([]);
        Assert.True(onlyStock.Gear.TryRemoveItem(7, 8, false, onlyStock.Stock, emptyRoles, onlyStock.Poisons));
        Assert.Equal(1, onlyStock.Stock.GetAmount(7));
        Assert.True(onlyStock.Gear.TryRemoveItem(7, 0, false, onlyStock.Stock, emptyRoles, onlyStock.Poisons));
        Assert.Equal(0, onlyStock.Stock.GetAmount(7));
        Assert.Equal(28, onlyStock.Gear.AttackBonus(5));
        Assert.Equal(60000, onlyStock.Roles.Roles[5].Statuses[8]);
        Assert.Equal(new PoisonSlot(10, 1001), onlyStock.Poisons.Slots(5)[0]);

        // Missing stock returns0; occupied depleted stock returns-negative. Both keep the full remainder.
        foreach (bool occupied in new[] { false, true })
        {
            var emptyStock = Seed(0, occupied);
            Assert.True(emptyStock.Gear.TryRemoveItem(7, 2, false, emptyStock.Stock, emptyStock.Roles, emptyStock.Poisons));
            Assert.Equal([0, 0, 0, 7, 0, 7], emptyStock.Gear.Equipment(5));
            Assert.Equal(60000, emptyStock.Roles.Roles[5].Statuses[8]); // Stops before hand3.
            Assert.Equal(occupied ? new InventorySlot(7, 0, 65535) : new(0, 0, 0), emptyStock.Stock.Slots[0]);
        }
        var boundary = Seed(1, item: 65535);
        Assert.False(boundary.Gear.TryRemoveItem(65535, 65535, false, boundary.Stock, boundary.Roles, boundary.Poisons));
        Assert.True(boundary.Gear.TryRemoveItem(65535, 65535, true, boundary.Stock, boundary.Roles, boundary.Poisons));
        Assert.Equal(0, boundary.Stock.GetAmount(65535));
        Assert.Equal(new int[6], boundary.Gear.Equipment(5));
        Assert.Equal(new int[6], boundary.Gear.Equipment(3));
        Assert.Equal([65535, 0, 0, 0, 0, 0], boundary.Gear.Equipment(0));
        var fullStock = new InventoryLiveState(Enumerable.Range(1, 256).Select(id => new InventorySlot(id, 0, 42)).ToArray());
        Assert.Equal(1, fullStock.AddItem(256, 3));
        Assert.True(boundary.Gear.TryRemoveItem(256, 2, false, fullStock, emptyRoles, boundary.Poisons));
        Assert.Equal(new InventorySlot(256, 1, 42), fullStock.Slots[255]);
        Assert.Equal(new InventorySlot(1, 0, 42), fullStock.Slots[0]);

        // Reversed party order changes the first removal, never role identity or the grid's other rows.
        var reverse = Seed(0, party: [3, 5]);
        Assert.True(reverse.Gear.TryRemoveItem(7, 1, false, reverse.Stock, reverse.Roles, reverse.Poisons));
        Assert.Equal([0, 7, 0, 0, 0, 0], reverse.Gear.Equipment(3));
        Assert.Equal([7, 0, 7, 7, 0, 7], reverse.Gear.Equipment(5));
        var five = Seed(0, party: [4, 2, 1, 3, 5]);
        Assert.True(five.Gear.TryRemoveItem(7, 6, false, five.Stock, five.Roles, five.Poisons));
        Assert.Equal(new int[6], five.Gear.Equipment(3));
        Assert.Equal(new int[6], five.Gear.Equipment(5));
        Assert.Equal(7, five.Gear.AttackBonus(0));
        var noParty = Seed(party: []);
        Assert.False(noParty.Gear.TryRemoveItem(7, 3, false, noParty.Stock, emptyRoles, noParty.Poisons));
        Assert.True(noParty.Gear.TryRemoveItem(7, 3, true, noParty.Stock, emptyRoles, noParty.Poisons));
        Assert.Equal(0, noParty.Stock.GetAmount(7));
        Assert.Equal(28, noParty.Gear.AttackBonus(5));

        // Literal item0: CountItem0 rejects with a target, but the no-target reference loop matches
        // EMPTY gear slots. It reaches role3 hand/wear after role5's two empty slots; don't optimize it away.
        var zero = Seed();
        Assert.False(zero.Gear.TryRemoveItem(0, 0, false, zero.Stock, emptyRoles, zero.Poisons));
        Assert.True(zero.Gear.TryRemoveItem(0, 6, true, zero.Stock, zero.Roles, zero.Poisons));
        Assert.Equal(2, zero.Stock.GetAmount(7));
        Assert.Equal([7, 0, 7, 7, 0, 7], zero.Gear.Equipment(5));
        Assert.Equal([7, 7, 0, 0, 0, 0], zero.Gear.Equipment(3));
        Assert.Equal(60000, zero.Roles.Roles[5].Statuses[8]);
        Assert.Equal(0, zero.Roles.Roles[3].Statuses[8]);
        Assert.Equal(new PoisonSlot(10, 1001), zero.Poisons.Slots(5)[0]);
        Assert.Equal(new PoisonSlot(11, 65535), zero.Poisons.Slots(3)[0]);
        Assert.All(zero.Poisons.Slots(3).Skip(1), slot => Assert.Equal(new PoisonSlot(0, 0), slot));
        Assert.Equal(0, zero.Gear.AttackAllWord(3, 5));
        Assert.Equal(0, zero.Gear.ApplyStatuses(3, zero.Roles.Roles[3].Statuses)[8]);
        Assert.True(zero.Gear.TryRemoveItem(0, 0, true, zero.Stock, zero.Roles, zero.Poisons));
        Assert.Equal(0, zero.Gear.AttackAllWord(5, 1));

        // Validate every selected fallback row BEFORE stock or the first valid role is changed.
        // Modern binding-integrity refusal is not a rollback of a valid, partially satisfied command.
        foreach (var badRow in new[] {
            Role(3, [8, 7, 0, 0, 0, 0]), Role(3, [7, 7, 0, 0, 0]),
            Role(3, [7, 7, 0, 0, 0, 0]) with { Statuses = new int[8] },
            Role(3, [7, 7, 0, 0, 0, 0]) with { Statuses = [0, 0, 0, 0, 0, 0, 0, 0, 65536] }
        })
        {
            var guarded = Seed();
            var badRoles = new RoleLiveState([guarded.Roles.Roles[5], badRow]);
            var before = badRoles.Roles[5];
            Assert.Throws<InvalidDataException>(() => guarded.Gear.TryRemoveItem(7, 7, false, guarded.Stock, badRoles, guarded.Poisons));
            Assert.Equal(2, guarded.Stock.GetAmount(7));
            Assert.Same(before, badRoles.Roles[5]);
            Assert.Equal([7, 0, 7, 7, 0, 7], guarded.Gear.Equipment(5));
            Assert.Equal(28, guarded.Gear.AttackBonus(5));
            Assert.Equal(new PoisonSlot(10, 1001), guarded.Poisons.Slots(5)[0]);
            // Untouched malformed row is not a license to require a full six-role seed.
            Assert.True(guarded.Gear.TryRemoveItem(7, 3, false, guarded.Stock, badRoles, guarded.Poisons));
            Assert.Equal([0, 0, 7, 7, 0, 7], guarded.Gear.Equipment(5));
        }
        var missing = Seed();
        Assert.Throws<InvalidDataException>(() => missing.Gear.TryRemoveItem(7, 3, false, missing.Stock, emptyRoles, missing.Poisons));
        Assert.Equal(2, missing.Stock.GetAmount(7));
        Assert.False(missing.Gear.TryRemoveItem(7, 65535, false, missing.Stock, emptyRoles, missing.Poisons));
        foreach (int invalid in new[] { -1, 65536, int.MinValue, int.MaxValue })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => missing.Gear.TryRemoveItem(invalid, 1, true, missing.Stock, missing.Roles, missing.Poisons));
            Assert.Throws<ArgumentOutOfRangeException>(() => missing.Gear.TryRemoveItem(7, invalid, true, missing.Stock, missing.Roles, missing.Poisons));
        }
        Assert.Throws<ArgumentNullException>(() => missing.Gear.TryRemoveItem(7, 1, true, null!, missing.Roles, missing.Poisons));
        Assert.Throws<ArgumentNullException>(() => missing.Gear.TryRemoveItem(7, 1, true, missing.Stock, null!, missing.Poisons));
        Assert.Throws<ArgumentNullException>(() => missing.Gear.TryRemoveItem(7, 1, true, missing.Stock, missing.Roles, null!));
        Assert.Equal(new InventorySlot(7, 2, 65535), missing.Stock.Slots[0]);
        Assert.Equal([7, 0, 7, 7, 0, 7], missing.Gear.Equipment(5));
        Assert.Equal(28, missing.Gear.AttackBonus(5));
        Assert.Equal(60000, missing.Roles.Roles[5].Statuses[8]);
        Assert.Equal(new PoisonSlot(10, 1001), missing.Poisons.Slots(5)[0]);
        Assert.Equal(2, Seed().Stock.GetAmount(7)); // Independent owners are untouched.
    }

    private static (InventoryLiveState Stock, BattleEquipmentState Gear, RoleLiveState Roles, PartyPoisonState Poisons) Seed(
        int stock = 2, bool occupied = true, int item = 7, int[]? party = null)
    {
        party ??= [5, 3];
        IReadOnlyList<int>[] grid = [ [item, 0, 0, 0, 0, 0], new int[6], new int[6],
            [item, item, 0, 0, 0, 0], new int[6], [item, 0, item, item, 0, item] ];
        var effect = new BattleEquipmentEffect(7, 17, 27, 700, [0, 0, 0, 0, 0, 1000, 0, 0, 60000], 37, 47, 57)
            { AttackAll = 65535, CooperativeMagic = 321 };
        var gear = BattleEquipmentState.Initialize(grid, new Dictionary<int, BattleEquipmentObject>
            { [item] = new(item, Enumerable.Repeat(effect, 6).ToArray()) });
        var roles = new RoleLiveState(Enumerable.Range(0, 6).Select(id => Role(id, grid[id])).ToArray());
        IReadOnlyList<PoisonSlot> row = new[] { new PoisonSlot(10, 1001), new(11, 65535), new(0, 444), new(10, 1003) }
            .Concat(Enumerable.Repeat(new PoisonSlot(0, 0), 12)).ToArray();
        var poisons = new PartyPoisonState(party, party.Select(_ => row).ToArray(), new Dictionary<int, int> { [10] = 99, [11] = 98 });
        var inventory = new InventoryLiveState(occupied ? [new(item, stock, 65535)] : []);
        return (inventory, gear, roles, poisons);
    }

    private static BattleRoleSnapshot Role(int id, IReadOnlyList<int> gear) =>
        new(id, 2, 100, 150, 20, 50, new(5, 2, 17), new Dictionary<string, BattleExperienceSnapshot>(),
            gear, [9, 0], [2, 3, 4, 5, 6, 1000, 0, 0, 60000], 1, 11, 12, 13, 14, 15);
}
