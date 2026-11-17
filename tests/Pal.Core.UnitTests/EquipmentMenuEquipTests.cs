using System.Text.Json.Nodes;
using Xunit;

namespace Pal.Core.Tests;

// Independent oracle: uigame.c PAL_GameEquipItem/PAL_EquipItemMenu (equipable stock list, role
// eligibility bits, wLastUnequippedItem navigation), itemmenu.c PAL_ItemSelectMenu (owned stock
// only, remembered cursor, cancel exits), script.c 0x0018 (PAL_RemoveEquipmentEffect before the
// same-object guard, inventory arms incl. the single-copy in-place exchange) and global.c
// PAL_SetPlayerStatus's good-status maximum. Numbers below are hand-authored inputs.
public sealed class EquipmentMenuEquipTests
{
    [Fact]
    public void Authored_inventory_and_equip_menu_context_must_parse_as_modern_battle_data()
    {
        var content = ModernBattleContent.Parse(EquipContextData().ToJsonString());
        // Ordered bag rows with their amounts; battles never consume this seed.
        Assert.Equal([(199, 1), (197, 2)], content.InitialInventory.Select(row => (row.ItemId, row.Amount)).ToArray());
        // Bindings keep authored order; masks are the kItemFlagEquipableByPlayerRole bits.
        Assert.Equal([(199, 0, 0b11), (197, 0, 0b01)],
            content.EquipMenu.Select(binding => (binding.ObjectId, binding.Part, binding.EquipableRoleMask)).ToArray());
        Assert.True(content.EquipMenu[0].Eligible(0) && content.EquipMenu[0].Eligible(1));
        Assert.True(content.EquipMenu[1].Eligible(0) && !content.EquipMenu[1].Eligible(1));
        Assert.False(content.EquipMenu[1].Eligible(6)); // outside the role domain
        // Legacy data without the sections stays valid and empty.
        var legacy = ModernBattleContent.Parse(Data().ToJsonString());
        Assert.Empty(legacy.InitialInventory);
        Assert.Empty(legacy.EquipMenu);
        // Strict domain rejections with authored paths.
        Assert.Contains("$.inventory[0].amount", Reject("amount100", "expected integer in 0..99").Message);
        Assert.Contains("$.inventory[0].itemId", Reject("itemId0", "expected integer in 1..65535").Message);
        Assert.Contains("unknown field 'amountTypo'", Reject("rowTypo", "unknown field").Message);
        Assert.Contains("initial bag rows require unique", Reject("duplicateItem", "unique").Message);
        Assert.Contains("$.equipMenu[0].part", Reject("part6", "expected integer in 0..5").Message);
        Assert.Contains("$.equipMenu[0].equipableBy[0]", Reject("role6", "expected integer in 0..5").Message);
        Assert.Contains("duplicate role id 0", Reject("duplicateRole", "duplicate role id").Message);
        Assert.Contains("equipment object 999 is not declared", Reject("undeclared", "is not declared").Message);
        Assert.Contains("unique object WORD ids", Reject("duplicateBinding", "unique object WORD ids").Message);
    }

    [Fact]
    public void Equip_item_installs_registers_moves_stock_and_returns_the_reference_word()
    {
        BattleEquipmentEffect Zero() => new(0, 0, 0, 0, new int[9]);
        var objects = new Dictionary<int, BattleEquipmentObject>
        {
            [901] = new(901, [new(3, 2, 0, 0, new int[9]), new(0, 4, 0, 0, new int[9]), Zero(), Zero(), Zero(), Zero()]),
            [902] = new(902, [new(10, 0, 0, 0, new int[9]), Zero(), Zero(), Zero(), Zero(), Zero()]),
        };
        // Remove/add arms: an empty part consumes one copy (returns 0); an occupied part whose
        // displaced item is already in stock removes one new copy and adds one old copy.
        var (state, roles, inventory, poisons) = Owners(
            role0: [0, 0, 0, 0, 0, 0], role1: [901, 0, 0, 0, 0, 0], objects,
            stock: [new InventorySlot(902, 1, 0), new InventorySlot(901, 2, 0)]);
        Assert.Equal(0, state.EquipItem(0, 0, 901, objects, inventory, roles, poisons));
        Assert.Equal(901, roles.Roles[0].Equipment[0]);
        Assert.Equal((43, 22), (state.EffectiveAttack(0, 40), state.EffectiveDefense(0, 20)));
        Assert.Equal(1, inventory.GetAmount(901));
        // Same-object rewrite: registers are rewritten, never added, and stock does not move.
        Assert.Equal(901, state.EquipItem(0, 0, 901, objects, inventory, roles, poisons));
        Assert.Equal(901, state.EquipItem(0, 0, 901, objects, inventory, roles, poisons));
        Assert.Equal((43, 22), (state.EffectiveAttack(0, 40), state.EffectiveDefense(0, 20)));
        Assert.Equal(1, inventory.GetAmount(901));
        Assert.Equal(901, state.EquipItem(1, 0, 902, objects, inventory, roles, poisons));
        // Role1's head contribution switched from 901's defense 4 to 902's zero row.
        Assert.Equal(18, state.EffectiveDefense(1, 18));
        Assert.Equal((0, 2), (inventory.GetAmount(902), inventory.GetAmount(901)));
        Assert.Equal([902, 0, 0, 0, 0, 0], roles.Roles[1].Equipment.ToArray());
        // Single incoming copy with the displaced item absent: the reference in-place exchange
        // keeps the slot position (script.c 0x0018's direct wItem write, no tail append).
        var (swapState, swapRoles, swapInventory, swapPoisons) = Owners(
            role0: [901, 0, 0, 0, 0, 0], role1: [0, 0, 0, 0, 0, 0], objects,
            stock: [new InventorySlot(902, 1, 0)]);
        Assert.Equal(901, swapState.EquipItem(0, 0, 902, objects, swapInventory, swapRoles, swapPoisons));
        Assert.Equal((901, 1), (swapInventory.Slots[0].ItemId, swapInventory.Slots[0].Amount));
        Assert.Equal((0, 1), (swapInventory.GetAmount(902), swapInventory.GetAmount(901)));
        Assert.Equal(902, swapRoles.Roles[0].Equipment[0]);
        Assert.Equal(50, swapState.EffectiveAttack(0, 40));
        // Parameter domains reject before any write.
        Assert.Throws<ArgumentOutOfRangeException>(() => state.EquipItem(0, 6, 901, objects, inventory, roles, poisons));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.EquipItem(0, 0, 0, objects, inventory, roles, poisons));
        Assert.Throws<InvalidDataException>(() => state.EquipItem(0, 0, 999, objects, inventory, roles, poisons));
    }

    [Fact]
    public void Equip_item_resets_hand_dual_attack_and_wear_poisons_per_remove_equipment_effect()
    {
        BattleEquipmentEffect Zero() => new(0, 0, 0, 0, new int[9]);
        var objects = new Dictionary<int, BattleEquipmentObject>
        {
            [903] = new(903, [Zero(), new(0, 0, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0, 60000]), Zero(), Zero(), Zero(), Zero()]),
            [904] = new(904, [Zero(), new(1, 0, 0, 0, new int[9]), Zero(), Zero(), Zero(), Zero()]),
            [905] = new(905, [new(1, 0, 0, 0, new int[9]), Zero(), Zero(), Zero(), Zero(), Zero()]),
        };
        // Hand: a same-object rewrite restores the dual-attack word everywhere (never stacks);
        // replacing the item clears the actual status and the overlay together.
        var (state, roles, inventory, poisons) = Owners(
            role0: [0, 0, 0, 0, 0, 0], role1: [0, 0, 0, 903, 0, 0], objects,
            stock: [new InventorySlot(904, 1, 0)]);
        Assert.Equal(903, state.EquipItem(1, 3, 903, objects, inventory, roles, poisons));
        Assert.Equal(60000, roles.Roles[1].Statuses[8]);
        Assert.Equal(60000, state.StatusOverlay(1)[8]);
        Assert.Equal(903, state.EquipItem(1, 3, 904, objects, inventory, roles, poisons));
        Assert.Equal(0, roles.Roles[1].Statuses[8]);
        Assert.Equal(0, state.StatusOverlay(1)[8]);
        Assert.Equal(1, inventory.GetAmount(903)); // the displaced dual item returned in place
        // Wear: equipping the part runs the level-99 poison cleanup; lower levels keep order.
        var wearLevels = new Dictionary<int, int> { [800] = 99, [801] = 5 };
        var (wearState, wearRoles, wearInventory, wearPoisons) = Owners(
            role0: [0, 0, 0, 0, 0, 0], role1: [0, 0, 0, 0, 0, 0], objects,
            stock: [new InventorySlot(905, 1, 0)], role1Poisons: [(800, 1), (801, 2)], poisonLevels: wearLevels);
        Assert.Equal(0, wearState.EquipItem(1, 5, 905, objects, wearInventory, wearRoles, wearPoisons));
        Assert.Equal([(801, 2), (0, 0)],
            wearPoisons.Slots(1).Take(2).Select(slot => (slot.PoisonId, slot.ScriptEntry)).ToArray());
    }

    [Fact]
    public void Equipment_menu_page_filters_stock_and_eligibility_without_half_changes()
    {
        var menu = Menu();
        // The equipment entry opens the equipable stock list; only owned stock is listed, in
        // authored binding order with part labels and amounts (itemmenu.c compresses the rest).
        menu.Open();
        menu.Press(ExplorationMenuKey.Down);
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.EquipItems, menu.Mode);
        Assert.Equal([(901, 0, 2, "头带", "头"), (902, 0, 1, "短剑", "头"), (904, 3, 1, "铁手套", "手")],
            menu.EquipItemsView().Select(row => (row.ObjectId, row.Part, row.Amount, row.Name, row.PartLabel)).ToArray());
        // Role eligibility bits and current gear names per member (uigame.c player box).
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.EquipRoles, menu.Mode);
        var selection = menu.EquipRolesView();
        Assert.Equal((901, 0, "头带", "头"), (selection.ObjectId, selection.Part, selection.Name, selection.PartLabel));
        Assert.Equal([("大侠", true, Array.Empty<string>()), ("小侠", true, new[] { "旧盔" })],
            selection.Roles.Select(role => (role.Name, role.Eligible, role.Gear.ToArray())).ToArray());
        // Up wraps over the party (iCurrentPlayer wrap); cancels return one level and close
        // without any gear or stock change.
        menu.Press(ExplorationMenuKey.Up);
        Assert.Equal(1, menu.SelectedEquipRole);
        menu.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationMenuMode.EquipItems, menu.Mode);
        Assert.Equal([(901, 2), (902, 1), (904, 1)], Stock(menu));
        menu.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationMenuMode.MainMenu, menu.Mode);
        menu.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationMenuMode.Closed, menu.Mode);

        // Equipping onto an empty part returns to the item list and reflects on the status page.
        menu.Open(); // the remembered main-menu cursor stays on the equipment entry
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.EquipItems, menu.Mode);
        menu.Press(ExplorationMenuKey.Down); // 902: role0-only head item
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.EquipRoles, menu.Mode);
        menu.Press(ExplorationMenuKey.Confirm); // 大侠 eligible
        Assert.Equal(ExplorationMenuMode.EquipItems, menu.Mode); // wLastUnequippedItem 0
        Assert.Equal([(901, 2), (904, 1)], Stock(menu));
        menu.Press(ExplorationMenuKey.Cancel);
        menu.Press(ExplorationMenuKey.Up); // status entry
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.Status, menu.Mode);
        var view = menu.StatusView();
        Assert.Equal((50, 20), (view.Attack, view.Defense)); // authored 40/20 plus 902's attack 10
        Assert.Equal(new[] { 902 }, view.Equipment.Select(gear => gear.ObjectId).ToArray());
        menu.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationMenuMode.Closed, menu.Mode);

        // A displaced object with a binding stays selected in the role screen (same-object case);
        // one without a binding ends the screen, and its returned copy stays unlisted.
        menu.Open();
        menu.Press(ExplorationMenuKey.Down); // equipment entry
        menu.Press(ExplorationMenuKey.Confirm);
        menu.Press(ExplorationMenuKey.Up); // cursor 0: 901 (stock [901, 904])
        menu.Press(ExplorationMenuKey.Confirm);
        menu.Press(ExplorationMenuKey.Down); // 小侠 wears 906 (uncataloged)
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.EquipItems, menu.Mode); // wLastUnequippedItem 906: no binding
        Assert.Equal([(901, 1), (904, 1)], Stock(menu)); // 906 returned to the bag but unlisted
        menu.Press(ExplorationMenuKey.Confirm); // 901 again; 小侠 now wears 901
        menu.Press(ExplorationMenuKey.Down);
        menu.Press(ExplorationMenuKey.Confirm); // same-object equip
        Assert.Equal(ExplorationMenuMode.EquipRoles, menu.Mode); // wLastUnequippedItem stays 901
        Assert.Equal(901, menu.EquipSelection);
        menu.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationMenuMode.EquipItems, menu.Mode);
        Assert.Equal([(901, 1), (904, 1)], Stock(menu)); // no stock movement

        // An ineligible role (mask bit clear) never equips: no gear, stock or mode change.
        menu.Press(ExplorationMenuKey.Cancel);
        menu.Press(ExplorationMenuKey.Cancel); // closed
        menu.Open();
        menu.Press(ExplorationMenuKey.Confirm); // remembered equipment entry
        menu.Press(ExplorationMenuKey.Down); // 904: zero mask
        menu.Press(ExplorationMenuKey.Confirm);
        menu.Press(ExplorationMenuKey.Down); // 小侠
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.EquipRoles, menu.Mode);
        Assert.Equal(904, menu.EquipSelection);
        menu.Press(ExplorationMenuKey.Up); // 大侠 also ineligible for 904
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.EquipRoles, menu.Mode);
        Assert.Equal(904, menu.EquipSelection);
        menu.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationMenuMode.EquipItems, menu.Mode);
        Assert.Equal([(901, 1), (904, 1)], Stock(menu));

        // An empty equipable stock lists nothing; Search falls straight back (object 0).
        var empty = Menu(inventory: new InventoryLiveState([]));
        empty.Open();
        empty.Press(ExplorationMenuKey.Down);
        empty.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.EquipItems, empty.Mode);
        Assert.Empty(empty.EquipItemsView());
        empty.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.MainMenu, empty.Mode);

        // Equipment-entry construction requires the whole shared equip context up front.
        Assert.Contains("requires authored equip-menu bindings", Assert.Throws<InvalidDataException>(
            () => Menu(noEquipMenu: true)).Message);
        Assert.Contains("requires the shared live inventory owner", Assert.Throws<InvalidDataException>(
            () => Menu(noInventory: true)).Message);
        Assert.Contains("requires the shared poison owner", Assert.Throws<InvalidDataException>(
            () => Menu(withPoisons: false)).Message);
        Assert.Contains("requires the declared equipment objects", Assert.Throws<InvalidDataException>(
            () => Menu(noEquipmentObjects: true)).Message);
        Assert.Contains("is not declared", Assert.Throws<InvalidDataException>(
            () => Menu(undeclaredBinding: true)).Message);
        Assert.Contains("901", Assert.Throws<InvalidDataException>(
            () => Menu(missingItemWord: true)).Message);
        Assert.Contains("60020", Assert.Throws<InvalidDataException>(
            () => Menu(missingPartWord: true)).Message);
        Assert.Throws<InvalidOperationException>(() => Menu().EquipItemsView());
        Assert.Throws<InvalidOperationException>(() => Menu().EquipRolesView());

        List<(int ObjectId, int Amount)> Stock(ExplorationMenuSession session) =>
            session.EquipItemsView().Select(row => (row.ObjectId, row.Amount)).ToList();
    }

    private static InvalidDataException Reject(string mutation, string expected)
    {
        var data = EquipContextData();
        switch (mutation)
        {
            case "amount100": data["inventory"]![0]!["amount"] = 100; break;
            case "itemId0": data["inventory"]![0]!["itemId"] = 0; break;
            case "rowTypo": data["inventory"]![0]!["amountTypo"] = 1; break;
            case "duplicateItem": ((JsonArray)data["inventory"]!).Add(new JsonObject { ["itemId"] = 199, ["amount"] = 1 }); break;
            case "part6": data["equipMenu"]![0]!["part"] = 6; break;
            case "role6": data["equipMenu"]![0]!["equipableBy"] = new JsonArray(6); break;
            case "duplicateRole": data["equipMenu"]![0]!["equipableBy"] = new JsonArray(0, 0); break;
            case "undeclared": data["equipMenu"]![0]!["objectId"] = 999; break;
            case "duplicateBinding": ((JsonArray)data["equipMenu"]!).Add(
                new JsonObject { ["objectId"] = 199, ["part"] = 1, ["equipableBy"] = new JsonArray(0) }); break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        return Assert.Throws<InvalidDataException>(() => ModernBattleContent.Parse(data.ToJsonString()));
    }

    private static (BattleEquipmentState State, RoleLiveState Roles, InventoryLiveState Inventory, PartyPoisonState Poisons) Owners(
        int[] role0, int[] role1, IReadOnlyDictionary<int, BattleEquipmentObject> objects,
        IReadOnlyList<InventorySlot> stock, (int Id, int Entry)[]? role1Poisons = null,
        Dictionary<int, int>? poisonLevels = null)
    {
        int[][] grid = [role0, role1, [0, 0, 0, 0, 0, 0], [0, 0, 0, 0, 0, 0], [0, 0, 0, 0, 0, 0], [0, 0, 0, 0, 0, 0]];
        var roles = new RoleLiveState([Role(0, role0), Role(1, role1)]);
        var poisons = new PartyPoisonState([0, 1],
            [PoisonRow(null), PoisonRow(role1Poisons)],
            poisonLevels ?? new Dictionary<int, int> { [800] = 99, [801] = 5 });
        return (BattleEquipmentState.Initialize(grid, objects), roles, new InventoryLiveState(stock), poisons);
    }

    private static IReadOnlyList<PoisonSlot> PoisonRow((int Id, int Entry)[]? slots)
    {
        var row = new PoisonSlot[PartyPoisonState.Capacity];
        for (int i = 0; i < row.Length; i++)
            row[i] = slots is not null && i < slots.Length ? new(slots[i].Id, slots[i].Entry) : new(0, 0);
        return row;
    }

    private static ExplorationMenuSession Menu(
        IReadOnlyDictionary<int, BattleEquipmentObject>? equipmentObjects = null,
        InventoryLiveState? inventory = null,
        bool withPoisons = true, bool undeclaredBinding = false,
        bool missingItemWord = false, bool missingPartWord = false,
        bool noEquipMenu = false, bool noInventory = false, bool noEquipmentObjects = false)
    {
        BattleEquipmentEffect Zero() => new(0, 0, 0, 0, new int[9]);
        var objects = new Dictionary<int, BattleEquipmentObject>
        {
            [901] = new(901, [new(3, 2, 0, 0, new int[9]), new(0, 4, 0, 0, new int[9]), Zero(), Zero(), Zero(), Zero()]),
            [902] = new(902, [new(10, 0, 0, 0, new int[9]), Zero(), Zero(), Zero(), Zero(), Zero()]),
            [904] = new(904, [Zero(), new(1, 0, 0, 0, new int[9]), Zero(), Zero(), Zero(), Zero()]),
            [906] = new(906, [Zero(), new(0, 1, 0, 0, new int[9]), Zero(), Zero(), Zero(), Zero()]),
        };
        IReadOnlyList<BattleEquipMenuBinding>? equipMenu = undeclaredBinding
            ? [new BattleEquipMenuBinding(999, 0, 0b01)]
            : [new BattleEquipMenuBinding(901, 0, 0b11), new BattleEquipMenuBinding(902, 0, 0b01),
               new BattleEquipMenuBinding(904, 3, 0)];
        int[][] grid =
        [
            [0, 0, 0, 0, 0, 0], [906, 0, 0, 0, 0, 0], [0, 0, 0, 0, 0, 0],
            [0, 0, 0, 0, 0, 0], [0, 0, 0, 0, 0, 0], [0, 0, 0, 0, 0, 0],
        ];
        var roles = new RoleLiveState([Role(0, grid[0]), Role(1, grid[1])]);
        var poisons = new PartyPoisonState([0, 1], [PoisonRow(null), PoisonRow(null)], new Dictionary<int, int>());
        var words = new Dictionary<int, string>
        {
            [ExplorationMenuSession.StatusEntryWord] = "状态",
            [ExplorationMenuSession.EquipmentEntryWord] = "装备",
            [ExplorationMenuSession.LevelWord] = "等级",
            [ExplorationMenuSession.HpWord] = "生命",
            [ExplorationMenuSession.MpWord] = "法力",
            [ExplorationMenuSession.ExperienceWord] = "经验",
            [ExplorationMenuSession.AttackPowerWord] = "攻击力",
            [ExplorationMenuSession.MagicPowerWord] = "灵力",
            [ExplorationMenuSession.DefenseWord] = "防御",
            [ExplorationMenuSession.DexterityWord] = "身法",
            [ExplorationMenuSession.FleeRateWord] = "吉运",
            [60100] = "大侠",
            [60101] = "小侠",
            [901] = "头带",
            [902] = "短剑",
            [904] = "铁手套",
            [906] = "旧盔",
        };
        string[] partNames = ["头", "身", "肩", "手", "脚", "戴"];
        foreach (int part in Enumerable.Range(0, 6))
            words[ExplorationMenuSession.PartWordBase + part] = partNames[part];
        if (missingItemWord) words.Remove(901);
        if (missingPartWord) words.Remove(ExplorationMenuSession.PartWordBase);
        return new ExplorationMenuSession(
            [new(ExplorationMenuPage.Status, ExplorationMenuSession.StatusEntryWord),
             new(ExplorationMenuPage.Equipment, ExplorationMenuSession.EquipmentEntryWord)],
            [new ExplorationMenuMember(0, 60100, false, 40, 6, 20, 100, 3),
             new ExplorationMenuMember(1, 60101, false, 35, 5, 18, 99, 2)],
            roles, BattleEquipmentState.Initialize(grid, objects),
            new Dictionary<int, ClassicBattleGrowthData>
            {
                [0] = new([90, 100, 111, 12], []), [1] = new([90, 100, 111, 12], []),
            },
            words, withPoisons ? poisons : null, noEquipMenu ? null : equipMenu,
            noEquipmentObjects ? null : equipmentObjects ?? objects,
            noInventory ? null : inventory ?? new InventoryLiveState(
                [new InventorySlot(901, 2, 0), new InventorySlot(902, 1, 0), new InventorySlot(904, 1, 0)]));
    }

    private static BattleRoleSnapshot Role(int roleId, int[]? equipment = null) => new(roleId, 2, 100, 150, 20, 50,
        new BattleExperienceSnapshot(77, 3, 12), Hidden(), equipment ?? new int[6], new int[32], new int[9], 0,
        0, 0, 0, 0, 0);

    private static IReadOnlyDictionary<string, BattleExperienceSnapshot> Hidden() =>
        new Dictionary<string, BattleExperienceSnapshot>
        {
            ["health"] = new(0, 1, 0), ["magic"] = new(0, 1, 0), ["attack"] = new(0, 1, 0),
            ["magicPower"] = new(0, 1, 0), ["defense"] = new(0, 1, 0), ["dexterity"] = new(0, 1, 0),
            ["flee"] = new(0, 1, 0),
        };

    private static JsonObject EquipContextData()
    {
        var data = Data();
        data["roleEquipment"] = new JsonArray(
            new JsonArray(0, 0, 0, 0, 0, 0), new JsonArray(0, 0, 0, 0, 0, 0),
            new JsonArray(0, 0, 0, 0, 0, 0), new JsonArray(0, 0, 0, 0, 0, 0),
            new JsonArray(0, 0, 0, 0, 0, 0), new JsonArray(0, 0, 0, 0, 0, 0));
        data["equipments"] = new JsonArray(Equipment(199, attackByRole0: 3), Equipment(197, defenseByRole0: 1));
        data["inventory"] = new JsonArray(
            new JsonObject { ["itemId"] = 199, ["amount"] = 1 },
            new JsonObject { ["itemId"] = 197, ["amount"] = 2 });
        data["equipMenu"] = new JsonArray(
            new JsonObject { ["objectId"] = 199, ["part"] = 0, ["equipableBy"] = new JsonArray(0, 1) },
            new JsonObject { ["objectId"] = 197, ["part"] = 0, ["equipableBy"] = new JsonArray(0) });
        return data;
    }

    private static JsonObject Data()
    {
        var data = JsonNode.Parse("""
        {"formatVersion":1,"roles":[{
          "roleId":0,"level":2,"hp":100,"maxHp":150,"mp":20,"maxMp":50,
          "primaryExperience":{"value":5,"level":2,"count":17},
          "hiddenExperience":{
            "health":{"value":7,"level":3,"count":55},"magic":{"value":7,"level":3,"count":55},
            "attack":{"value":7,"level":3,"count":55},"magicPower":{"value":7,"level":3,"count":55},
            "defense":{"value":7,"level":3,"count":55},"dexterity":{"value":7,"level":3,"count":55},
            "flee":{"value":7,"level":3,"count":55}},
          "equipment":[0,0,0,0,0,0],"magics":[],"statuses":[0,0,0,0,0,0,0,0,0],"coveredByRoleId":0}],
         "players":[{"roleId":0,"hp":100,"maxHp":150,"attack":40,"defense":20,"dexterity":100,
          "equipment":[0,0,0,0,0,0],"statuses":[0,0,0,0,0,0,0,0,0],"poisons":[0],
          "dyingScript":0,"friendDeathScript":0,"coveredByRoleId":null,"attackAll":false}],
         "growth":[{"roleId":0,"levelUpExperience":[],"levelUpMagics":[]}],
         "teams":[{"teamId":18,"enemies":[{"id":7,"level":0,"hp":43,"attack":0,"defense":0,"dexterity":0,"physicalResistance":1,
          "statuses":[0,0,0,0,0,0,0,0,0],"poisons":[0],"turnStartScript":0,"readyScript":0,"battleEndScript":0,
          "magicId":0,"dualMove":0,"attackEquivItem":0,"attackEquivItemRate":0,"experience":0,"cash":0}]}]}
        """)!.AsObject();
        data["growth"]![0]!["levelUpExperience"] = new JsonArray(
            Enumerable.Repeat(0, 100).Select(_ => (JsonNode?)JsonValue.Create(100)).ToArray());
        data["roles"]![0]!["magics"] = new JsonArray(
            Enumerable.Repeat(0, 32).Select(_ => (JsonNode?)JsonValue.Create(0)).ToArray());
        return data;
    }

    private static JsonNode Equipment(int objectId, int attackByRole0 = 0, int defenseByRole0 = 0)
    {
        JsonNode Row(int attack, int defense) => new JsonObject
        {
            ["attack"] = attack, ["defense"] = defense, ["dexterity"] = 0, ["battleSprite"] = 0,
            ["statuses"] = new JsonArray(0, 0, 0, 0, 0, 0, 0, 0, 0)
        };
        return new JsonObject
        {
            ["objectId"] = objectId,
            ["effects"] = new JsonArray(Row(attackByRole0, defenseByRole0),
                Row(0, 0), Row(0, 0), Row(0, 0), Row(0, 0), Row(0, 0))
        };
    }
}
