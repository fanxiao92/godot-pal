using Pal.Core;
using Xunit;
using Pal.Core.State;
using Pal.Core.Battle;

public sealed class EquipmentRemovalTests
{
    [Fact]
    public void Single_slot_removal_synchronizes_live_gear_status_and_wear_poisons_without_reseeding()
    {
        // global.c:1395-1455 and script.c:1006-1007: zero effects, hand status/wear poisons, then gear ID.
        // The same object also grants dualAttack from head/wear: removal must NOT rescan and restore it.
        var (equipment, roles, poisons) = Seed();
        var stock = new InventoryLiveState([new(7, 3, 65535)]);
        var roleView = roles.Roles;
        var oldRole = roles.Roles[5];
        var oldGear = equipment.Equipment(5);
        var oldOverlay = equipment.StatusOverlay(5);
        var oldPoison = poisons.Slots(5)[0];
        var otherRole = roles.Roles[3];
        equipment.RemoveEquippedItem(5, 3, roles, poisons);
        Assert.Equal(0, roleView[5].Statuses[8]); // Designated RED: no-op leaves65535.
        Assert.Equal([7, 0, 0, 0, 0, 7], roleView[5].Equipment);
        Assert.Equal([7, 0, 0, 0, 0, 7], equipment.Equipment(5));
        Assert.Equal([2, 3, 4, 5, 6, 1000, 2000, 3000, 0], roleView[5].Statuses);
        Assert.Equal([0, 0, 0, 0, 0, 1000, 2000, 3000, 0], equipment.StatusOverlay(5));
        Assert.Equal(0, equipment.ApplyStatuses(5, roleView[5].Statuses)[8]);
        Assert.Equal((14, 34, 54, 74, 94, 114), Bonuses(equipment, 5));
        Assert.Equal(700, equipment.BattleSprite(5, 2)); // Wear still supplies the sprite.
        Assert.Equal(0, equipment.AttackAllWord(5, 3));
        Assert.Equal(0, equipment.CooperativeMagicWord(5, 3));
        Assert.Equal(65535, equipment.AttackAllWord(5, 0));
        Assert.Equal(321, equipment.CooperativeMagicWord(5, 5));
        Assert.Equal(new PoisonSlot(7, 1001), poisons.Slots(5)[0]); // Hand does not clean poison.
        Assert.Equal(oldPoison, poisons.Slots(5)[0]);
        Assert.Same(otherRole, roleView[3]);
        Assert.Equal((5, 2, 100, 150, 20, 50, 1, 11, 12, 13, 14, 15),
            (roleView[5].RoleId, roleView[5].Level, roleView[5].Hp, roleView[5].MaxHp, roleView[5].Mp, roleView[5].MaxMp,
                roleView[5].CoveredByRoleId, roleView[5].Attack, roleView[5].MagicPower, roleView[5].Defense,
                roleView[5].Dexterity, roleView[5].FleeRate));
        Assert.Equal(new BattleExperienceSnapshot(5, 2, 17), roleView[5].PrimaryExperience);
        Assert.Equal(new BattleExperienceSnapshot(7, 3, 55), roleView[5].HiddenExperience["attack"]);
        Assert.Equal([9, 0, 0], roleView[5].Magics);
        Assert.Equal([7, 0, 0, 7, 0, 7], oldRole.Equipment);
        Assert.Equal([7, 0, 0, 7, 0, 7], oldGear);
        Assert.Equal(65535, oldRole.Statuses[8]);
        Assert.Equal(60000, oldOverlay[8]);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)roleView[5].Equipment)[0] = 0);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)roleView[5].Statuses)[0] = 0);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<int, BattleRoleSnapshot>)roleView)[5] = oldRole);
        Assert.Equal(5, equipment.CountItem(7, stock, [5])); // Stock3 + remaining gear2.
        Assert.Equal(new InventorySlot(7, 3, 65535), stock.Slots[0]);
        equipment.RemoveEquippedItem(5, 3, roles, poisons); // Empty slot and repeat still legal, no reapplication.
        Assert.Equal(0, roles.Roles[5].Statuses[8]);
        Assert.Equal((14, 34, 54, 74, 94, 114), Bonuses(equipment, 5));

        var poisonView = poisons.Slots(5);
        equipment.RemoveEquippedItem(5, 5, roles, poisons);
        Assert.Equal([7, 0, 0, 0, 0, 0], equipment.Equipment(5));
        Assert.Equal([7, 0, 0, 0, 0, 0], roleView[5].Equipment);
        Assert.Equal(new PoisonSlot(8, 1002), poisonView[0]);
        Assert.All(poisonView.Skip(1), slot => Assert.Equal(new PoisonSlot(0, 0), slot));
        Assert.Equal(new PoisonSlot(7, 1001), oldPoison);
        Assert.Equal(new PoisonSlot(7, 1001), poisons.Slots(3)[0]);
        Assert.Equal((7, 17, 27, 37, 47, 57), Bonuses(equipment, 5));
        Assert.Equal(700, equipment.BattleSprite(5, 2)); // Head effect remains; no reinitialize.
        Assert.Equal(0, equipment.ApplyStatuses(5, new int[9])[8]);
        equipment.RemoveEquippedItem(5, 0, roles, poisons);
        Assert.Equal(new int[6], roleView[5].Equipment);
        Assert.Equal(new int[6], equipment.Equipment(5));
        Assert.Equal((0, 0, 0, 0, 0, 0), Bonuses(equipment, 5));
        Assert.Equal(2, equipment.BattleSprite(5, 2));
        Assert.False(equipment.OverridesBattleSprite(5));
        Assert.Equal([0, 0, 0, 0, 0, 1000, 2000, 3000, 0], equipment.StatusOverlay(5));
        Assert.Equal([2, 3, 4, 5, 6, 1000, 2000, 3000, 0], roleView[5].Statuses);
        Assert.Equal(3, equipment.CountItem(7, stock, [5]));
        Assert.Equal(21, equipment.AttackBonus(3));

        // All role identities and all six actual slots. Literal gear rows are independent oracles.
        int[][] expectedGear = [
            [0, 7, 7, 7, 7, 7], [7, 0, 7, 7, 7, 7], [7, 7, 0, 7, 7, 7],
            [7, 7, 7, 0, 7, 7], [7, 7, 7, 7, 0, 7], [7, 7, 7, 7, 7, 0]
        ];
        for (int role = 0; role < 6; role++)
            for (int slot = 0; slot < 6; slot++)
            {
                var (one, live, poison) = Seed([7, 7, 7, 7, 7, 7]);
                one.RemoveEquippedItem(role, slot, live, poison);
                Assert.Equal(expectedGear[slot], one.Equipment(role));
                Assert.Equal(expectedGear[slot], live.Roles[role].Equipment);
                Assert.Equal((35, 85, 135, 185, 235, 285), Bonuses(one, role));
                Assert.Equal(0, one.AttackAllWord(role, slot));
                Assert.Equal(0, one.CooperativeMagicWord(role, slot));
                Assert.Null(one.AttackAllWord(role, 6)); // No seventh gear slot.
                Assert.Equal(slot == 3 ? 0 : 65535, live.Roles[role].Statuses[8]);
                Assert.Equal(slot == 3 ? 0 : 60000, one.StatusOverlay(role)[8]);
                Assert.Equal(1000, live.Roles[role].Statuses[5]); // No generic persistent-status removal.
                Assert.Equal(slot == 5 && role == 5 ? new PoisonSlot(8, 1002) : new(7, 1001), poison.Slots(5)[0]);
                Assert.Equal(slot == 5 && role == 3 ? new PoisonSlot(8, 1002) : new(7, 1001), poison.Slots(3)[0]);
                int spectator = role == 0 ? 1 : 0;
                Assert.Equal([7, 7, 7, 7, 7, 7], one.Equipment(spectator));
                Assert.Equal(42, one.AttackBonus(spectator));
                Assert.Equal(65535, live.Roles[spectator].Statuses[8]);
                one.RemoveEquippedItem(role, slot, live, poison);
                Assert.Equal(expectedGear[slot], live.Roles[role].Equipment);
                Assert.Equal(35, one.AttackBonus(role));
            }

        // Temporary dualAttack is also cleared, even when the selected hand slot was already empty.
        var (emptyHand, timedRoles, timedPoisons) = Seed([7, 0, 0, 0, 0, 7], 7);
        emptyHand.RemoveEquippedItem(5, 3, timedRoles, timedPoisons);
        Assert.Equal(0, timedRoles.Roles[5].Statuses[8]);
        Assert.Equal(0, emptyHand.ApplyStatuses(5, timedRoles.Roles[5].Statuses)[8]);
        Assert.Equal(14, emptyHand.AttackBonus(5));
        Assert.Equal(new PoisonSlot(7, 1001), timedPoisons.Slots(5)[0]);
        var independent = Seed();
        Assert.Equal(21, independent.Equipment.AttackBonus(5));
        Assert.Equal(65535, independent.Roles.Roles[5].Statuses[8]);
        Assert.Equal(new PoisonSlot(7, 1001), independent.Poisons.Slots(5)[0]);

        // Undeclared legacy zero equipment remains undeclared. Explicit empty poison party is not fabricated by API.
        var legacy = BattleEquipmentState.Initialize(new TriggerBattleContent(Role(5, new int[6]),
            new ClassicBattlePlayer(5, 100, 150, 20, 10, 30),
            new ClassicBattleGrowthData(Enumerable.Repeat(100, 100).ToArray(), []),
            new Dictionary<int, ClassicBattleEnemy> { [1] = new(7, 1, 2, 3, 4, 5, 6) }));
        var legacyRoles = new RoleLiveState([Role(5, new int[6], 3)]);
        legacy.RemoveEquippedItem(5, 3, legacyRoles, new PartyPoisonState([], [], new Dictionary<int, int>()));
        Assert.False(legacy.Declared);
        Assert.Equal(0, legacyRoles.Roles[5].Statuses[8]);
        Assert.Equal(0, legacy.AttackAllWord(5, 3));
        Assert.Equal(new int[6], legacy.Equipment(5));

        // Every refusal is checked against unchanged public records/gear/registers/poisons.
        var (guarded, guardedRoles, guardedPoisons) = Seed();
        var frozen = guardedRoles.Roles[5];
        foreach (int bad in new[] { -1, 6, int.MinValue, int.MaxValue })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => guarded.RemoveEquippedItem(bad, 3, guardedRoles, guardedPoisons));
            Assert.Throws<ArgumentOutOfRangeException>(() => guarded.RemoveEquippedItem(5, bad, guardedRoles, guardedPoisons));
        }
        Assert.Throws<ArgumentNullException>(() => guarded.RemoveEquippedItem(5, 3, null!, guardedPoisons));
        Assert.Throws<ArgumentNullException>(() => guarded.RemoveEquippedItem(5, 3, guardedRoles, null!));
        Assert.Throws<InvalidDataException>(() => guarded.RemoveEquippedItem(5, 5, new RoleLiveState([]), guardedPoisons));
        foreach (var malformed in new[] {
            frozen with { Equipment = new int[5] }, frozen with { Equipment = new int[7] },
            frozen with { Equipment = [7, 0, 0, 8, 0, 7] },
            frozen with { Equipment = [7, 0, 0, -1, 0, 7] },
            frozen with { Equipment = [7, 0, 0, 65536, 0, 7] },
            frozen with { Statuses = new int[8] }, frozen with { Statuses = new int[10] },
            frozen with { Statuses = [0, 0, 0, 0, 0, 0, 0, 0, -1] },
            frozen with { Statuses = [0, 0, 0, 0, 0, 0, 0, 0, 65536] }
        })
        {
            var badOwner = new RoleLiveState([malformed]);
            var before = badOwner.Roles[5];
            Assert.Throws<InvalidDataException>(() => guarded.RemoveEquippedItem(5, 3, badOwner, guardedPoisons));
            Assert.Same(before, badOwner.Roles[5]);
            Assert.Throws<InvalidDataException>(() => guarded.RemoveEquippedItem(5, 5, badOwner, guardedPoisons));
            Assert.Same(before, badOwner.Roles[5]);
        }
        Assert.Same(frozen, guardedRoles.Roles[5]);
        Assert.Equal([7, 0, 0, 7, 0, 7], guarded.Equipment(5));
        Assert.Equal((21, 51, 81, 111, 141, 171), Bonuses(guarded, 5));
        Assert.Equal(60000, guarded.StatusOverlay(5)[8]);
        Assert.Equal(65535, guarded.AttackAllWord(5, 3));
        Assert.Equal(321, guarded.CooperativeMagicWord(5, 5));
        Assert.Equal(new PoisonSlot(7, 1001), guardedPoisons.Slots(5)[0]);
        Assert.Equal(new PoisonSlot(0, 444), guardedPoisons.Slots(5)[2]);
        Assert.Equal(new PoisonSlot(7, 1003), guardedPoisons.Slots(5)[3]);
    }

    private static (BattleEquipmentState Equipment, RoleLiveState Roles, PartyPoisonState Poisons) Seed(
        int[]? gear = null, int dualAttack = 65535)
    {
        gear ??= [7, 0, 0, 7, 0, 7];
        var effect = new BattleEquipmentEffect(7, 17, 27, 700, [0, 0, 0, 0, 0, 1000, 2000, 3000, 60000], 37, 47, 57)
            { AttackAll = 65535, CooperativeMagic = 321 };
        var objects = new Dictionary<int, BattleEquipmentObject> { [7] = new(7, Enumerable.Repeat(effect, 6).ToArray()) };
        var grid = Enumerable.Range(0, 6).Select(_ => (IReadOnlyList<int>)gear.ToArray()).ToArray();
        var equipment = BattleEquipmentState.Initialize(grid, objects);
        var roles = new RoleLiveState(Enumerable.Range(0, 6).Select(id => Role(id, gear, dualAttack)).ToArray());
        IReadOnlyList<PoisonSlot> row = new[] { new PoisonSlot(7, 1001), new(8, 1002), new(0, 444), new(7, 1003) }
            .Concat(Enumerable.Repeat(new PoisonSlot(0, 0), 12)).ToArray();
        var poisons = new PartyPoisonState([5, 3], [row, row], new Dictionary<int, int> { [7] = 99, [8] = 98 });
        // Seed arrays are no longer live truth; modifying these must not affect the initialized owners.
        gear[0] = 0;
        grid[5] = new int[6];
        return (equipment, roles, poisons);
    }

    private static BattleRoleSnapshot Role(int id, IReadOnlyList<int> gear, int dualAttack = 65535) =>
        new(id, 2, 100, 150, 20, 50, new(5, 2, 17),
            new Dictionary<string, BattleExperienceSnapshot> { ["attack"] = new(7, 3, 55) },
            gear, [9, 0, 0], [2, 3, 4, 5, 6, 1000, 2000, 3000, dualAttack], 1, 11, 12, 13, 14, 15);

    private static (int, int, int, int, int, int) Bonuses(BattleEquipmentState state, int role) =>
        (state.AttackBonus(role), state.DefenseBonus(role), state.DexterityBonus(role),
            state.MagicPowerBonus(role), state.FleeRateBonus(role), state.PoisonResistanceBonus(role));
}
