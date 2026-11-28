using Xunit;
using Pal.Core.State;
using Pal.Core.Battle;
using Pal.Core.Menu;

namespace Pal.Core.Tests;

// Independent oracle: uigame.c PAL_InGameMenu/PAL_PlayerStatus (main menu loop, remembered
// cursor iCurMainMenuItem, status pages over the party, every exit path closes the whole menu
// via 'goto out'), ui.c PAL_ReadMenu (Up/Down wrap, Search confirm, Menu cancel), and the
// effective-panel projections the battle chain already pins (TriggerBattleContent.SettledPlayers
// base rule plus BattleEquipmentState register words with reference WORD wrap).
public sealed class ExplorationMenuSessionTests
{
    [Fact]
    public void Open_status_and_reference_navigation_read_live_owner_words_and_close_per_uigame()
    {
        var menu = Menu();
        Assert.Equal(ExplorationMenuMode.Closed, menu.Mode);
        menu.Open();
        Assert.True(menu.Mode == ExplorationMenuMode.MainMenu,
            "PAL_MENU_RED: reference uigame.c PAL_InGameMenu - Open must enter MainMenu");
        // ui.c PAL_ReadMenu: the cursor starts at the remembered entry and wraps both ways.
        Assert.Equal(0, menu.SelectedEntry);
        Assert.Equal("状态", menu.EntryLabel(0));
        Assert.Equal("物品", menu.EntryLabel(1));
        menu.Press(ExplorationMenuKey.Down);
        Assert.Equal(1, menu.SelectedEntry);
        menu.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationMenuMode.Closed, menu.Mode);
        // gpGlobals->iCurMainMenuItem survives the close; the next open starts there.
        menu.Open();
        Assert.Equal(ExplorationMenuMode.MainMenu, menu.Mode);
        Assert.Equal(1, menu.SelectedEntry);
        menu.Press(ExplorationMenuKey.Up);
        Assert.Equal(0, menu.SelectedEntry);
        menu.Press(ExplorationMenuKey.Up);
        Assert.Equal(1, menu.SelectedEntry);
        menu.Press(ExplorationMenuKey.Down);
        Assert.Equal(0, menu.SelectedEntry);
        // uigame.c case 1: Confirm runs PAL_PlayerStatus, starting at party member 0.
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.Status, menu.Mode);
        Assert.Equal(0, menu.StatusMemberIndex);
        var view = menu.StatusView();
        Assert.Equal((0, 0, "大侠", false), (view.PartyIndex, view.RoleId, view.Name, view.IsDead));
        Assert.Equal((3, 120, 150, 8, 50, 77, 111),
            (view.Level, view.Hp, view.MaxHp, view.Mp, view.MaxMp, view.Experience, view.NextLevelExperience));
        // Authored player words 40/6/20/100/3 plus equipment registers 10/5/4/20/-.
        Assert.Equal((50, 11, 24, 120, 3),
            (view.Attack, view.MagicPower, view.Defense, view.Dexterity, view.FleeRate));
        Assert.Equal(new[] { 171 }, view.Equipment.Select(entry => entry.ObjectId).ToArray());
        Assert.Equal("木剑", view.Equipment[0].Name);
        Assert.Equal(new[] { 8 }, view.Poisons.Select(poison => poison.PoisonId).ToArray());
        Assert.Equal("毒甲", view.Poisons[0].Name);
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 0, 0, 0, 1000 }, view.Statuses.ToArray());
        Assert.Equal(("等级", "生命", "法力", "经验", "攻击力", "灵力", "防御", "身法", "吉运"),
            (view.LevelLabel, view.HpLabel, view.MpLabel, view.ExperienceLabel, view.AttackLabel,
             view.MagicPowerLabel, view.DefenseLabel, view.DexterityLabel, view.FleeRateLabel));
        // uigame.c PAL_PlayerStatus: Down/Right/Search advance to the next party member.
        menu.Press(ExplorationMenuKey.Down);
        Assert.Equal(1, menu.StatusMemberIndex);
        var dead = menu.StatusView();
        // Panel-declared role1 reads its live role panels (30), not any authored player word.
        Assert.Equal(("小侠", 0, 150, true, 30), (dead.Name, dead.Hp, dead.MaxHp, dead.IsDead, dead.Attack));
        Assert.Equal(77, dead.NextLevelExperience); // role1 growth table threshold at level 2
        // The status page's cancel closes the whole menu (PAL_InGameMenu 'goto out').
        menu.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationMenuMode.Closed, menu.Mode);
        menu.Open();
        Assert.Equal(ExplorationMenuMode.MainMenu, menu.Mode);
        Assert.Throws<InvalidOperationException>(() => menu.Open());
        menu.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationMenuMode.Closed, menu.Mode);
        Assert.Throws<InvalidOperationException>(() => menu.Press(ExplorationMenuKey.Confirm));
        Assert.Throws<InvalidOperationException>(() => menu.StatusView());
    }

    [Fact]
    public void Status_pages_follow_party_order_and_reference_exit_paths_close_the_whole_menu()
    {
        // Non-sequential role ids keep party order distinct from role identity.
        var menu = Menu(party:
        [
            new ExplorationMenuMember(5, 60105, false, 1, 1, 1, 1, 1),
            new ExplorationMenuMember(3, 60103, false, 1, 1, 1, 1, 1),
            new ExplorationMenuMember(1, 60101, false, 1, 1, 1, 1, 1),
        ],
        roles: new RoleLiveState([Role(5), Role(3), Role(1)]),
        growth: new Dictionary<int, ClassicBattleGrowthData>
        {
            [5] = Growth(42, 43, 44, 45), [3] = Growth(42, 43, 44, 45), [1] = Growth(42, 43, 44, 45),
        },
        noPoisons: true);
        menu.Open();
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(5, menu.StatusView().RoleId);
        menu.Press(ExplorationMenuKey.Down);
        Assert.Equal(3, menu.StatusView().RoleId);
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(1, menu.StatusView().RoleId);
        Assert.Empty(menu.StatusView().Poisons); // no poison owner bound: no rows, no placeholders
        // kKeyRight/kKeyDown/kKeySearch past the last member exits and closes the menu.
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.Closed, menu.Mode);
        // kKeyUp/kKeyLeft before the first member exits the same way.
        menu.Open();
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(5, menu.StatusView().RoleId);
        menu.Press(ExplorationMenuKey.Up);
        Assert.Equal(ExplorationMenuMode.Closed, menu.Mode);
        // Left/Right mirror Up/Down (uigame.c status wait loop) and in-range moves stay.
        menu.Open();
        menu.Press(ExplorationMenuKey.Confirm);
        menu.Press(ExplorationMenuKey.Right);
        Assert.Equal(3, menu.StatusView().RoleId);
        menu.Press(ExplorationMenuKey.Right);
        Assert.Equal(1, menu.StatusView().RoleId);
        menu.Press(ExplorationMenuKey.Left);
        Assert.Equal(3, menu.StatusView().RoleId);
        menu.Press(ExplorationMenuKey.Left);
        menu.Press(ExplorationMenuKey.Left);
        Assert.Equal(ExplorationMenuMode.Closed, menu.Mode);
    }

    [Fact]
    public void Missing_words_missing_growth_or_absent_roles_reject_before_any_menu_state_changes()
    {
        // A missing entry label word rejects at construction, before any menu state exists.
        Assert.Contains("60019", Assert.Throws<InvalidDataException>(
            () => Menu(words: Words(withMenuWord19: false))).Message);
        // A missing member name word rejects the same way (member 0's word resolves first).
        Assert.Contains("60100", Assert.Throws<InvalidDataException>(
            () => Menu(words: Words(withRoleNames: false))).Message);
        // The party must reference live owner records and per-role growth tables.
        Assert.Throws<InvalidDataException>(() => Menu(party:
            [new ExplorationMenuMember(0, 60100, false, 1, 1, 1, 1, 1), new ExplorationMenuMember(4, 60104, false, 1, 1, 1, 1, 1)],
            roles: new RoleLiveState([Role(0)]), growth: new Dictionary<int, ClassicBattleGrowthData> { [0] = Growth() }));
        Assert.Throws<InvalidDataException>(() => Menu(growth: new Dictionary<int, ClassicBattleGrowthData> { [0] = Growth() }));
        // Malformed bindings reject: no entries, empty or oversized party, duplicate roles,
        // out-of-range words, and a live level at or beyond its own growth table.
        Assert.Throws<InvalidDataException>(() => Menu(entries: []));
        Assert.Throws<InvalidDataException>(() => Menu(party: []));
        Assert.Throws<InvalidDataException>(() => Menu(party:
        [
            new ExplorationMenuMember(0, 60100, false, 1, 1, 1, 1, 1),
            new ExplorationMenuMember(0, 60100, false, 1, 1, 1, 1, 1),
        ]));
        IReadOnlyList<ExplorationMenuMember> four =
        [
            new ExplorationMenuMember(0, 60100, false, 1, 1, 1, 1, 1),
            new ExplorationMenuMember(1, 60101, false, 1, 1, 1, 1, 1),
            new ExplorationMenuMember(2, 60102, false, 1, 1, 1, 1, 1),
            new ExplorationMenuMember(3, 60103, false, 1, 1, 1, 1, 1),
        ];
        Assert.Throws<InvalidDataException>(() => Menu(party: four,
            roles: new RoleLiveState([Role(0), Role(1), Role(2), Role(3)]),
            growth: new Dictionary<int, ClassicBattleGrowthData>
            {
                [0] = Growth(), [1] = Growth(), [2] = Growth(), [3] = Growth(),
            }));
        Assert.Throws<InvalidDataException>(() => Menu(party: [new ExplorationMenuMember(0, 0, false, 1, 1, 1, 1, 1)]));
        Assert.Throws<InvalidDataException>(() => Menu(party: [new ExplorationMenuMember(0, 60100, false, 65536, 1, 1, 1, 1)]));
        Assert.Throws<InvalidDataException>(() => Menu(roles: new RoleLiveState(
            [Role(0, level: 2, experience: 0), Role(1)]), growth: new Dictionary<int, ClassicBattleGrowthData>
        {
            [0] = Growth(90, 100), [1] = Growth(90, 100, 111, 12),
        }));
        // Live words the view resolves later still reject explicitly, never with placeholders:
        // an equipment object without a language word, and a poison without one.
        var missingEquipment = Menu(words: Words(equipmentWord171: false));
        missingEquipment.Open();
        missingEquipment.Press(ExplorationMenuKey.Confirm);
        Assert.Contains("171", Assert.Throws<InvalidDataException>(() => missingEquipment.StatusView()).Message);
        var missingPoison = Menu(secondPoisonWithoutWord: true);
        missingPoison.Open();
        missingPoison.Press(ExplorationMenuKey.Confirm);
        Assert.Contains("9", Assert.Throws<InvalidDataException>(() => missingPoison.StatusView()).Message);
    }

    [Fact]
    public void Effective_panels_wrap_reference_words_over_the_settled_base_rule()
    {
        // Panel-declared role0 reads live role panels; register words wrap like the reference
        // WORD sums (global.c 1736-1900): 65530+10 -> 4 and 65533+5 -> 2.
        var menu = Menu(roles: new RoleLiveState(
            [Role(0, level: 2, attack: 65530, magicPower: 65533), Role(1)]),
            party: [new ExplorationMenuMember(0, 60100, true, 0, 0, 0, 0, 0), new ExplorationMenuMember(1, 60101, false, 65530, 0, 0, 0, 0)],
            equipment: WrapEquipment());
        menu.Open();
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal((4, 2, 0, 0, 0),
            (menu.StatusView().Attack, menu.StatusView().MagicPower, menu.StatusView().Defense,
             menu.StatusView().Dexterity, menu.StatusView().FleeRate));
        // The undeclared role1 keeps its authored player words and its own register words.
        menu.Press(ExplorationMenuKey.Down);
        Assert.Equal(4, menu.StatusView().Attack);
    }

    private static ExplorationMenuSession Menu(
        IReadOnlyList<ExplorationMenuEntry>? entries = null,
        IReadOnlyList<ExplorationMenuMember>? party = null,
        RoleLiveState? roles = null,
        IReadOnlyDictionary<int, ClassicBattleGrowthData>? growth = null,
        Dictionary<int, string>? words = null,
        BattleEquipmentState? equipment = null,
        PartyPoisonState? poisons = null,
        bool withMenuWord19 = true, bool withRoleNames = true,
        bool equipmentWord171 = true, bool secondPoisonWithoutWord = false, bool noPoisons = false)
    {
        var members = party ?? [new ExplorationMenuMember(0, 60100, false, 40, 6, 20, 100, 3),
            new ExplorationMenuMember(1, 60101, true, 35, 5, 18, 99, 2)];
        return new(entries ?? [new(ExplorationMenuPage.Status, ExplorationMenuSession.StatusEntryWord),
                new(ExplorationMenuPage.Status, 60019)],
            members,
            roles ?? new RoleLiveState([
                Role(0, level: 3, hp: 120, mp: 8, experience: 77,
                    equipment: [171, 0, 0, 0, 0, 0], statuses: [0, 0, 0, 0, 0, 0, 0, 0, 1000]),
                Role(1, hp: 0, attack: 30),
            ]),
            equipment ?? Equipment(), growth ?? new Dictionary<int, ClassicBattleGrowthData>
            {
                [0] = Growth(9, 10, 11, 111), [1] = Growth(5, 6, 77, 8),
            },
            words ?? Words(withMenuWord19, withRoleNames, equipmentWord171),
            noPoisons ? null : poisons ?? new PartyPoisonState([0, 1],
                [secondPoisonWithoutWord ? Row((8, 9001), (9, 9002)) : Row((8, 9001)), Row()],
                new Dictionary<int, int> { [8] = 1, [9] = 2 }));
    }

    private static Dictionary<int, string> Words(bool withMenuWord19 = true, bool withRoleNames = true,
        bool equipmentWord171 = true)
    {
        var words = new Dictionary<int, string>
        {
            [ExplorationMenuSession.HpWord] = "生命",
            [ExplorationMenuSession.ExperienceWord] = "经验",
            [ExplorationMenuSession.StatusEntryWord] = "状态",
            [ExplorationMenuSession.LevelWord] = "等级",
            [ExplorationMenuSession.MpWord] = "法力",
            [ExplorationMenuSession.AttackPowerWord] = "攻击力",
            [ExplorationMenuSession.MagicPowerWord] = "灵力",
            [ExplorationMenuSession.DefenseWord] = "防御",
            [ExplorationMenuSession.DexterityWord] = "身法",
            [ExplorationMenuSession.FleeRateWord] = "吉运",
        };
        if (withMenuWord19) words[60019] = "物品";
        if (withRoleNames)
        {
            words[60100] = "大侠";
            words[60101] = "小侠";
            words[60102] = "侠三";
            words[60103] = "义士";
            words[60104] = "游侠";
            words[60105] = "剑客";
        }
        if (equipmentWord171) words[171] = "木剑";
        words[8] = "毒甲";
        return words;
    }

    private static BattleEquipmentState Equipment() => BattleEquipmentState.Initialize(
        [
            [171, 0, 0, 0, 0, 0], [0, 0, 0, 0, 0, 0], [0, 0, 0, 0, 0, 0],
            [0, 0, 0, 0, 0, 0], [0, 0, 0, 0, 0, 0], [0, 0, 0, 0, 0, 0],
        ],
        new Dictionary<int, BattleEquipmentObject> { [171] = new(171, Rows()) });

    private static BattleEquipmentState WrapEquipment() => BattleEquipmentState.Initialize(
        [
            [171, 0, 0, 0, 0, 0], [171, 0, 0, 0, 0, 0], [0, 0, 0, 0, 0, 0],
            [0, 0, 0, 0, 0, 0], [0, 0, 0, 0, 0, 0], [0, 0, 0, 0, 0, 0],
        ],
        new Dictionary<int, BattleEquipmentObject> { [171] = new(171, WrapRows()) });

    private static IReadOnlyList<BattleEquipmentEffect> Rows()
    {
        BattleEquipmentEffect Zero() => new(0, 0, 0, 0, new int[9]);
        return [new(10, 4, 20, 0, new int[9], MagicPower: 5), Zero(), Zero(), Zero(), Zero(), Zero()];
    }

    private static IReadOnlyList<BattleEquipmentEffect> WrapRows()
    {
        BattleEquipmentEffect Zero() => new(0, 0, 0, 0, new int[9]);
        return [new(10, 0, 0, 0, new int[9], MagicPower: 5), new(10, 0, 0, 0, new int[9]), Zero(), Zero(), Zero(), Zero()];
    }

    private static IReadOnlyList<PoisonSlot> Row(params (int Id, int Entry)[] slots)
    {
        var row = new PoisonSlot[PartyPoisonState.Capacity];
        for (int i = 0; i < row.Length; i++)
            row[i] = i < slots.Length ? new PoisonSlot(slots[i].Id, slots[i].Entry) : new PoisonSlot(0, 0);
        return row;
    }

    private static ClassicBattleGrowthData Growth(params int[] thresholds) =>
        new(thresholds.Length == 0 ? [90, 100, 111, 12] : thresholds, []);

    private static BattleRoleSnapshot Role(int roleId, int level = 2, int hp = 100, int mp = 20,
        int experience = 77, int attack = 0, int magicPower = 0, int defense = 0, int dexterity = 0,
        int fleeRate = 0, int[]? equipment = null, int[]? statuses = null) => new(roleId, level, hp, 150, mp, 50,
        new BattleExperienceSnapshot(experience, 3, 12), Hidden(),
        equipment ?? new int[6], new int[32], statuses ?? new int[9], 0,
        attack, magicPower, defense, dexterity, fleeRate);

    private static IReadOnlyDictionary<string, BattleExperienceSnapshot> Hidden() =>
        new Dictionary<string, BattleExperienceSnapshot>
        {
            ["health"] = new(0, 1, 0), ["magic"] = new(0, 1, 0), ["attack"] = new(0, 1, 0),
            ["magicPower"] = new(0, 1, 0), ["defense"] = new(0, 1, 0), ["dexterity"] = new(0, 1, 0),
            ["flee"] = new(0, 1, 0),
        };
}
