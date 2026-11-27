using Pal.Core;
using Xunit;
using Pal.Core.State;
using Pal.Core.Battle;
using Pal.Core.Menu;
using Pal.Core.Save;

namespace Pal.Core.Tests;

/// <summary>M6-C leaf #225 (parent #148): the role/party/inventory/economy restore plan.
/// Independent oracles are the frozen #221 matrix (docs/milestones/m6/m6c-save-matrix.md:
/// D2/D3/D4/D5/D6/D7 retain domains, D7/D8 clear domains, D9 recompute domain and the frozen
/// restore order), the fixed SDLPal reference load rules (global.c PAL_LoadGame_Common
/// rgPoisonStatus memset, PAL_InitGameData rgPlayerStatus memset + PAL_UpdateEquipments
/// replay, PAL_StartBattle's dead-entrant entry normalization) and hand-written literal
/// fixture data — never values produced by the plan under test.</summary>
public sealed class PlayerSaveRestorePlanTests
{
    [Fact]
    public void Restore_rebuilds_the_full_role_domain_from_the_literal_v1_snapshot()
    {
        var fixture = new RestoreFixture();
        var candidate = PlayerSaveRestorePlan.Build(fixture.Save, fixture.Content);
        Assert.True(candidate is not null,
            "PLAYER_SAVE_RESTORE_RED: a decoded v1 snapshot must rebuild the complete role-domain candidate through the A/B owner construction chains");

        // D2: six role records reseeded by identity (never by party position), every permanent
        // word field-by-field against independent literals.
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, candidate.Roles.Roles.Keys.Order().ToArray());
        var two = candidate.Roles.Roles[2];
        Assert.Equal(5, two.Level);
        Assert.Equal((200, 210, 30, 60), (two.Hp, two.MaxHp, two.Mp, two.MaxMp));
        Assert.Equal(new BattleExperienceSnapshot(77, 5, 4), two.PrimaryExperience);
        Assert.Equal(7, two.HiddenExperience.Count);
        Assert.Equal(new BattleExperienceSnapshot(123, 4, 5), two.HiddenExperience["health"]);
        Assert.Equal(new BattleExperienceSnapshot(2, 1, 1), two.HiddenExperience["magic"]);
        Assert.Equal(new BattleExperienceSnapshot(45, 3, 2), two.HiddenExperience["attack"]);
        Assert.Equal(new BattleExperienceSnapshot(7, 2, 1), two.HiddenExperience["magicPower"]);
        Assert.Equal(new BattleExperienceSnapshot(0, 2, 0), two.HiddenExperience["defense"]);
        Assert.Equal(new BattleExperienceSnapshot(9, 1, 1), two.HiddenExperience["dexterity"]);
        Assert.Equal(new BattleExperienceSnapshot(1, 1, 0), two.HiddenExperience["flee"]);
        Assert.Equal(fixture.Save.Roles[2].Equipment, two.Equipment);
        Assert.Equal(MagicsRow(65, 0, 71), two.Magics); // slot order and 0 holes preserved
        Assert.Equal(0, two.CoveredByRoleId);
        Assert.Equal((61, 62, 63, 64, 65), (two.Attack, two.MagicPower, two.Defense, two.Dexterity, two.FleeRate));
        // D2/D7 clear domain: the nine status words load cleared (PAL_InitGameData memset).
        Assert.Equal(new int[9], two.Statuses);
        var zero = candidate.Roles.Roles[0];
        Assert.Equal((3, 44, 44, 7, 20), (zero.Level, zero.Hp, zero.MaxHp, zero.Mp, zero.MaxMp));
        Assert.Equal(2, zero.CoveredByRoleId);
        // A dead role stays dead words in the live owner: restore never applies the battle-entry
        // normalization (battle.c 1568-1583 is the battle-start boundary, not the load boundary).
        var dead = candidate.Roles.Roles[4];
        Assert.Equal((8, 0, 500, 2, 90), (dead.Level, dead.Hp, dead.MaxHp, dead.Mp, dead.MaxMp));
        Assert.Equal(new int[9], dead.Statuses);

        // D5: the party reseeds in saved identity order in both owners of that order.
        Assert.Equal(new[] { 2, 4, 0 }, candidate.PartyRoleIds);
        Assert.Equal(new[] { 2, 4, 0 }, candidate.Poisons.PartyRoleIds);

        // D8 clear domain: poison columns load zeroed (PAL_LoadGame_Common memset rgPoisonStatus),
        // one 16-slot row per current-party member in saved order.
        foreach (int roleId in candidate.Poisons.PartyRoleIds)
            Assert.Equal(Enumerable.Repeat(new PoisonSlot(0, 0), PlayerSaveContract.PoisonCapacity).ToArray(),
                candidate.Poisons.Slots(roleId));

        // D9 recompute domain: the saved grid drives one PAL_UpdateEquipments replay — registers
        // start at zero, the declared effect rows install exactly once, the persistent good
        // statuses come back only through the overlay, and effective panels stay projections.
        Assert.Equal(new[] { 7, 0, 0, 0, 0, 0 }, candidate.Equipment.Equipment(2));
        Assert.Equal(new[] { 0, 0, 0, 3, 0, 0 }, candidate.Equipment.Equipment(1));
        Assert.Equal(101, candidate.Equipment.EffectiveAttack(2, 61)); // 61 + 40, projected once
        Assert.Equal(67, candidate.Equipment.EffectiveMagicPower(2, 62)); // 62 + 5
        Assert.Equal(63, candidate.Equipment.EffectiveDefense(2, 63)); // no declared defense word: base only
        var overlay = candidate.Equipment.StatusOverlay(2);
        Assert.Equal(1200, overlay[7]); // haste: persistent equipment status (>999)
        Assert.Equal(0, overlay[8]);
        Assert.Equal(1500, candidate.Equipment.StatusOverlay(1)[8]); // dualAttack from the hand object
        Assert.Equal(0, candidate.Equipment.StatusOverlay(0)[7]);
        // The overlay projects onto the cleared words; the live record itself stays cleared.
        Assert.Equal(overlay, candidate.Equipment.ApplyStatuses(2, two.Statuses));

        // D6: occupied prefix in saved order; the depleted row stays occupied with its id (the
        // #221 modern deviation from PAL_CompressInventory — no implicit compaction on load);
        // trailing rows are empty and the full 256-slot capacity holds.
        Assert.Equal(PlayerSaveContract.InventoryCapacity, candidate.Inventory.Slots.Count);
        Assert.Equal(new InventorySlot(7, 3, 2), candidate.Inventory.Slots[0]);
        Assert.Equal(new InventorySlot(12, 0, 0), candidate.Inventory.Slots[1]);
        Assert.Equal(new InventorySlot(65, 99, 0), candidate.Inventory.Slots[2]);
        Assert.Equal(new InventorySlot(0, 0, 0), candidate.Inventory.Slots[3]);
        Assert.Equal(0, candidate.Inventory.GetAmount(12));
        Assert.Equal(99, candidate.Inventory.GetAmount(65));

        // D7: the wallet reseeds the saved DWORD verbatim.
        Assert.Equal(3210987654u, candidate.Cash.Cash);
    }

    [Fact]
    public void Repeated_restore_of_one_snapshot_stays_identical_and_never_stacks()
    {
        var fixture = new RestoreFixture();
        var first = PlayerSaveRestorePlan.Build(fixture.Save, fixture.Content);
        var second = PlayerSaveRestorePlan.Build(fixture.Save, fixture.Content);

        // Story 19 (secondary-restore isolation, #148 Testing Decision 3): every domain word of
        // the two candidates is identical...
        foreach (int roleId in first.Roles.Roles.Keys)
        {
            var a = first.Roles.Roles[roleId];
            var b = second.Roles.Roles[roleId];
            Assert.Equal((a.Level, a.Hp, a.Mp, a.CoveredByRoleId), (b.Level, b.Hp, b.Mp, b.CoveredByRoleId));
            Assert.Equal(a.Statuses, b.Statuses);
            Assert.Equal(a.Equipment, b.Equipment);
            Assert.Equal(a.Magics, b.Magics);
        }
        Assert.Equal(first.Inventory.Slots.Take(4).ToArray(), second.Inventory.Slots.Take(4).ToArray());
        Assert.Equal(first.Cash.Cash, second.Cash.Cash);
        Assert.Equal(first.Poisons.Slots(2), second.Poisons.Slots(2));
        Assert.Equal(first.PartyRoleIds, second.PartyRoleIds);

        // ...and the recomputed projections never stack: the base panel plus the declared bonus
        // exactly once per candidate, the persistent status overlay rebuilt at its authored word.
        Assert.Equal(101, first.Equipment.EffectiveAttack(2, 61));
        Assert.Equal(101, second.Equipment.EffectiveAttack(2, 61)); // not 61 + 40 + 40
        Assert.Equal(first.Equipment.StatusOverlay(2), second.Equipment.StatusOverlay(2));
        Assert.Equal(1200, second.Equipment.StatusOverlay(2)[7]); // not 2400

        // The two candidates own independent storage: writing one never dirties the other
        // (Story 36's slot-isolation property at the owner level).
        first.Cash.Credit(1);
        Assert.Equal(3210987654u, second.Cash.Cash);
    }

    [Fact]
    public void Restored_owners_feed_the_menu_pages_and_the_next_battle()
    {
        var fixture = new RestoreFixture();
        var candidate = PlayerSaveRestorePlan.Build(fixture.Save, fixture.Content);

        // Story 15: the menu's status and magic pages are pure projections over the shared
        // owners, so they read the restored progress — level, vitals, experience, the effective
        // panel (base + recomputed bonus) and the learned magic slots with their live-MP gating.
        var menu = new ExplorationMenuSession(
        [
            new ExplorationMenuEntry(ExplorationMenuPage.Status, ExplorationMenuSession.StatusEntryWord),
            new ExplorationMenuEntry(ExplorationMenuPage.Magic, ExplorationMenuSession.MagicEntryWord),
        ],
        [
            new ExplorationMenuMember(2, 60102, PanelDeclared: true, 0, 0, 0, 0, 0),
            new ExplorationMenuMember(4, 60104, PanelDeclared: true, 0, 0, 0, 0, 0),
            new ExplorationMenuMember(0, 60100, PanelDeclared: true, 0, 0, 0, 0, 0),
        ],
        candidate.Roles, candidate.Equipment, GrowthTables(), MenuWords(), candidate.Poisons,
        magicMenu:
        [
            new BattleMagicMenuBinding(65, 5, ApplyToAll: false, UsableOutsideBattle: true),
            new BattleMagicMenuBinding(71, 50, ApplyToAll: false, UsableOutsideBattle: true),
        ]);
        menu.Open();
        menu.Press(ExplorationMenuKey.Confirm); // main-menu entry 0: the status page
        Assert.Equal(ExplorationMenuMode.Status, menu.Mode);
        var status = menu.StatusView();
        Assert.Equal(2, status.RoleId);
        Assert.Equal((5, 200, 210, 30, 60), (status.Level, status.Hp, status.MaxHp, status.Mp, status.MaxMp));
        Assert.Equal(77, status.Experience);
        Assert.Equal(600, status.NextLevelExperience); // the authored table's level-5 threshold
        Assert.False(status.IsDead);
        Assert.Equal(101, status.Attack); // restored base 61 + the replayed equipment bonus 40
        Assert.Equal(67, status.MagicPower);
        Assert.Equal(new[] { new ExplorationMenuEquipment(7, "乌金簪") }, status.Equipment);
        Assert.Empty(status.Poisons);
        Assert.Equal(new int[9], status.Statuses);
        menu.Press(ExplorationMenuKey.Cancel); // the status page closes the whole menu
        menu.Open();
        menu.Press(ExplorationMenuKey.Down);
        menu.Press(ExplorationMenuKey.Confirm); // the magic entry opens the caster select
        Assert.Equal(ExplorationMenuMode.MagicCasters, menu.Mode);
        Assert.Equal(new[] { (2, true), (4, false), (0, true) },
            menu.MagicCastersView().Select(caster => (caster.RoleId, caster.Alive)));
        menu.Press(ExplorationMenuKey.Confirm); // caster 0: the alive restored role 2
        var magics = menu.MagicListView();
        Assert.Equal(new[] { 65, 71 }, magics.Select(row => row.ObjectId)); // saved slot order, 0 holes skipped
        Assert.Equal(new[] { (65, 5, true), (71, 50, false) },
            magics.Select(row => (row.ObjectId, row.CostMp, row.Enabled))); // 50 > restored Mp 30

        // Story 26: the next battle consumes the same restored owners. The dead member enters
        // through the battle-start normalization (revive to 1, overlay projected once) inside
        // the lifecycle's own copies, while the restored live owner keeps its dead identity and
        // cleared words — restore itself never applies the entry rules.
        var lifecycle = new ClassicBattleLifecycleSession(
            candidate.PartyRoleIds.Select(roleId => candidate.Roles.Roles[roleId]).ToArray(),
            candidate.PartyRoleIds.Select(roleId => PlayerOf(candidate.Roles.Roles[roleId])).ToArray(),
            [new ClassicBattleEnemy(7, 3, 60, 10, 8, 9, 0)],
            GrowthTables(), new FirstDrawRandom(), candidate.Equipment);
        Assert.Equal(1, lifecycle.Current.Roles[4].Hp); // battle.c 1568-1583 entry normalization
        Assert.Equal(1200, lifecycle.Current.Roles[2].Statuses[7]); // the overlay, projected once
        Assert.Equal(0, lifecycle.Current.Roles[2].Statuses[8]);
        Assert.Equal(0, candidate.Roles.Roles[4].Hp); // the live owner stays dead
        Assert.Equal(new int[9], candidate.Roles.Roles[2].Statuses); // and stays cleared
    }

    [Fact]
    public void Unknown_content_references_reject_fail_closed_without_clamping()
    {
        var fixture = new RestoreFixture();
        var save = fixture.Save;

        // A learned magic outside the prepared closure rejects with a field-locatable diagnostic;
        // Build either returns the complete candidate or throws — never a partial application.
        var missingMagic = fixture.Content with { MagicObjects = new HashSet<int> { 65 } }; // 71 unknown
        Assert.StartsWith("$.roles.magics", Assert.Throws<InvalidDataException>(() =>
            PlayerSaveRestorePlan.Build(save, missingMagic)).Message);
        // An occupied inventory id outside the prepared item closure rejects the same way.
        var missingItem = fixture.Content with { ItemObjects = new HashSet<int> { 7, 65 } }; // 12 unknown
        Assert.StartsWith("$.inventory.itemId", Assert.Throws<InvalidDataException>(() =>
            PlayerSaveRestorePlan.Build(save, missingItem)).Message);
        // An equipped object outside the declared equipment closure rejects through the
        // Initialize replay's own closure check.
        var missingEquipment = fixture.Content with
        {
            EquipmentObjects = new Dictionary<int, BattleEquipmentObject> { [3] = fixture.Content.EquipmentObjects[3] },
        };
        Assert.StartsWith("$.roleEquipment[2][0]", Assert.Throws<InvalidDataException>(() =>
            PlayerSaveRestorePlan.Build(save, missingEquipment)).Message);

        // Out-of-domain words never get clamped into a candidate (Decision 19): the frozen
        // contract rejects them before any restore runs, so the plan only ever sees validated
        // WORDs and applies no repair of its own.
        Assert.StartsWith("$.roles.hp", Assert.Throws<InvalidDataException>(() => RestoreFixture.Role(
            0, level: 3, hp: 70000, maxHp: 70000, mp: 7, maxMp: 20, primary: (30, 3, 1),
            equipment: new int[6], magics: Array.Empty<int>(), covered: 0,
            panel: (20, 19, 18, 17, 16), hidden: (15, 25))).Message);
    }

    /// <summary>A 32-word learned-magic row with the given leading words and 0 holes elsewhere.</summary>
    private static int[] MagicsRow(params int[] leading)
    {
        var row = new int[PlayerSaveContract.MaxLearnedMagics];
        for (int i = 0; i < leading.Length; i++) row[i] = leading[i];
        return row;
    }

    /// <summary>The shared 100-entry level-up table for the restored party (battle requires the
    /// full 100-word domain; the menu reads the current level's threshold).</summary>
    private static Dictionary<int, ClassicBattleGrowthData> GrowthTables() => new()
    {
        [2] = Growth(), [4] = Growth(), [0] = Growth(),
    };

    private static ClassicBattleGrowthData Growth() =>
        new(Enumerable.Range(0, 100).Select(level => 100 * (level + 1)).ToArray(), []);

    private static ClassicBattlePlayer PlayerOf(BattleRoleSnapshot role) =>
        new(role.RoleId, role.Hp, role.MaxHp, role.Attack, role.Defense, role.Dexterity)
        { Equipment = role.Equipment }; // the battle entry binds the player's declared gear

    private static Dictionary<int, string> MenuWords() => new()
    {
        [ExplorationMenuSession.StatusEntryWord] = "状态",
        [ExplorationMenuSession.MagicEntryWord] = "魔法",
        [ExplorationMenuSession.LevelWord] = "等级",
        [ExplorationMenuSession.HpWord] = "体力",
        [ExplorationMenuSession.MpWord] = "真气",
        [ExplorationMenuSession.ExperienceWord] = "经验",
        [ExplorationMenuSession.AttackPowerWord] = "攻击",
        [ExplorationMenuSession.MagicPowerWord] = "灵力",
        [ExplorationMenuSession.DefenseWord] = "防御",
        [ExplorationMenuSession.DexterityWord] = "身法",
        [ExplorationMenuSession.FleeRateWord] = "吉运",
        [60100] = "大侠",
        [60102] = "侠三",
        [60104] = "剑客",
        [7] = "乌金簪",
        [65] = "火咒",
        [71] = "雷咒",
    };

    private sealed class FirstDrawRandom : IBattleRandom
    {
        public int NextInclusive(int from, int to) => from;
        public float NextInclusive(float from, float to) => from;
    }

    /// <summary>Hand-written literal snapshot over the frozen v1 contract: six non-default role
    /// records (one dead, two geared, magic rows with 0 holes), a three-member party in identity
    /// order [2, 4, 0], a bag with a depleted occupied row, and a non-default wallet. The world
    /// domain is a minimal legal shape — this leaf restores the role domain only. Every value is
    /// an independently hand-picked literal; none is derived from the plan under test.</summary>
    private sealed class RestoreFixture
    {
        public PlayerSaveV1 Save { get; }
        public PlayerSaveRestoreContent Content { get; }

        public RestoreFixture()
        {
            Save = new PlayerSaveV1(PlayerSaveContract.CurrentVersion, saveSlot: 4, savedTimes: 12,
                "story/scene77@sha256:11aa22bb",
                new PlayerSaveRole[]
                {
                    Role(0, level: 3, hp: 44, maxHp: 44, mp: 7, maxMp: 20, primary: (30, 3, 1),
                        equipment: [0, 0, 0, 0, 0, 0], magics: [], covered: 2, panel: (20, 19, 18, 17, 16), hidden: (15, 25)),
                    Role(1, level: 6, hp: 120, maxHp: 150, mp: 18, maxMp: 40, primary: (66, 6, 2),
                        equipment: [0, 0, 0, 3, 0, 0], magics: [65], covered: 0, panel: (41, 40, 39, 38, 37), hidden: (16, 26)),
                    Role(2, level: 5, hp: 200, maxHp: 210, mp: 30, maxMp: 60, primary: (77, 5, 4),
                        equipment: [7, 0, 0, 0, 0, 0], magics: [65, 0, 71], covered: 0, panel: (61, 62, 63, 64, 65), hidden: (123, 45)),
                    Role(3, level: 2, hp: 55, maxHp: 66, mp: 9, maxMp: 30, primary: (11, 2, 3),
                        equipment: [0, 0, 0, 0, 0, 0], magics: [], covered: 1, panel: (50, 49, 48, 47, 46), hidden: (17, 27)),
                    Role(4, level: 8, hp: 0, maxHp: 500, mp: 2, maxMp: 90, primary: (88, 8, 5),
                        equipment: [0, 0, 0, 0, 0, 0], magics: [], covered: 5, panel: (70, 71, 72, 73, 74), hidden: (18, 28)),
                    Role(5, level: 12, hp: 640, maxHp: 640, mp: 40, maxMp: 80, primary: (99, 12, 6),
                        equipment: [0, 0, 0, 0, 0, 0], magics: [71], covered: 4, panel: (80, 81, 82, 83, 84), hidden: (19, 29)),
                },
                [2, 4, 0],
                new PlayerSaveInventorySlot[]
                {
                    new(7, 3, 2),
                    new(12, 0, 0), // depleted but still occupied: restored as-is
                    new(65, 99, 0),
                },
                3210987654u,
                new PlayerSaveWorld(77, new PlayerSaveLeader(10, 20, 2, 0, 1),
                    Array.Empty<PlayerSaveObject>(), Array.Empty<PlayerSaveSceneScript>(),
                    Array.Empty<PlayerSaveObjectScript>(), 3, null));
            Content = new PlayerSaveRestoreContent(
                new Dictionary<int, BattleEquipmentObject>
                {
                    // Object 7 (head): the role-2 effect row carries attack +40, magic power +5
                    // and the persistent haste word 1200; every other role's row stays zero.
                    [7] = new(7, Enumerable.Range(0, PlayerSaveContract.RoleCount).Select(Role7Row).ToArray()),
                    // Object 3 (hand): the role-1 effect row carries the persistent dualAttack
                    // word 1500; every other role's row stays zero.
                    [3] = new(3, Enumerable.Range(0, PlayerSaveContract.RoleCount).Select(Role3Row).ToArray()),
                },
                new HashSet<int> { 65, 71 },
                new HashSet<int> { 7, 12, 65 },
                new Dictionary<int, int> { [9] = 5, [10] = 99 });
        }

        private static BattleEquipmentEffect Role7Row(int roleId) => roleId == 2
            ? new(40, 0, 0, 0, Statuses(haste: 1200), MagicPower: 5)
            : new(0, 0, 0, 0, Statuses());

        private static BattleEquipmentEffect Role3Row(int roleId) => roleId == 1
            ? new(0, 0, 0, 0, Statuses(dualAttack: 1500))
            : new(0, 0, 0, 0, Statuses());

        private static int[] Statuses(int haste = 0, int dualAttack = 0)
        {
            var statuses = new int[PlayerSaveContract.RoleStatusCount];
            statuses[7] = haste; // kStatusHaste
            statuses[8] = dualAttack; // kStatusDualAttack
            return statuses;
        }

        internal static PlayerSaveRole Role(int roleId, int level, int hp, int maxHp, int mp, int maxMp,
            (int Value, int Level, int Count) primary, int[] equipment, int[] magics, int covered,
            (int Attack, int MagicPower, int Defense, int Dexterity, int FleeRate) panel,
            (int HealthValue, int AttackValue) hidden) => new(
            roleId, level, hp, maxHp, mp, maxMp,
            new PlayerSaveExperience(primary.Value, primary.Level, primary.Count),
            HiddenTracks(hidden.HealthValue, hidden.AttackValue),
            equipment, MagicsRow(magics), covered,
            panel.Attack, panel.MagicPower, panel.Defense, panel.Dexterity, panel.FleeRate);

        private static IReadOnlyDictionary<string, PlayerSaveExperience> HiddenTracks(int healthValue, int attackValue) =>
            new Dictionary<string, PlayerSaveExperience>
            {
                ["health"] = new(healthValue, 4, 5),
                ["magic"] = new(2, 1, 1),
                ["attack"] = new(attackValue, 3, 2),
                ["magicPower"] = new(7, 2, 1),
                ["defense"] = new(0, 2, 0),
                ["dexterity"] = new(9, 1, 1),
                ["flee"] = new(1, 1, 0),
            };
    }
}
