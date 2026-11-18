using System.Text.Json.Nodes;
using Xunit;

namespace Pal.Core.Tests;

// Independent oracle: play.c PAL_GameUseItem (item use loop: player-select repeat, script via
// PAL_RunTriggerScript with the selected player role as caller, remove one copy only when the
// item is consuming and g_fScriptSuccess survived), itemmenu.c PAL_ItemSelectMenu over
// kItemFlagUsable (owned stock only, remembered iCurInvMenuItem cursor clamped at the list
// ends, equipped usable items appended with amount 0/in-use -1) and uigame.c PAL_ItemUseMenu
// (party moves wrap, cancel breaks back to the item select). Numbers below are hand-authored
// inputs.
public sealed class ExplorationMenuUseTests
{
    [Fact]
    public void Authored_use_menu_context_parses_as_modern_battle_data()
    {
        var content = ModernBattleContent.Parse(UseContextData().ToJsonString());
        // Ordered bag rows with their amounts; battles never consume this seed.
        Assert.Equal([(300, 2), (301, 1), (302, 1)],
            content.InitialInventory.Select(row => (row.ItemId, row.Amount)).ToArray());
        // Bindings keep authored order with their consuming/apply-to-all and BALL bitmap words.
        Assert.Equal([(300, true, false, 37), (301, true, true, 41), (302, true, false, 0)],
            content.UseMenu.Select(binding => (binding.ObjectId, binding.Consuming, binding.ApplyToAll, binding.Bitmap)).ToArray());
        // Legacy data without the sections stays valid and empty.
        var legacy = ModernBattleContent.Parse(Data().ToJsonString());
        Assert.Empty(legacy.UseMenu);
        // Strict domain rejections with authored paths.
        Assert.Contains("$.useMenu[0].objectId", Reject("objectId0", "expected integer in 1..65535").Message);
        Assert.Contains("unknown field 'throwable'", Reject("typo", "unknown field").Message);
        Assert.Contains("expected boolean", Reject("consumingString", "expected boolean").Message);
        Assert.Contains("unique object WORD ids", Reject("duplicateBinding", "unique").Message);
        Assert.Contains("unique object WORD ids", Reject("duplicateBinding", "unique").Message);
        Assert.Contains("expected integer in 0..65535", Reject("bitmapString", "expected integer").Message);
        Assert.Contains("$.useMenu[0].bitmap", Reject("bitmapRange", "expected integer in 0..65535").Message);
        Assert.Contains("missing field 'bitmap'", Reject("bitmapMissing", "missing field").Message);
    }

    [Fact]
    public void Items_page_lists_available_stock_and_equipped_appendix_and_navigates_the_reference_way()
    {
        var (menu, inventory, roles) = Menu();
        menu.Open();
        menu.Press(ExplorationMenuKey.Down);
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.Items, menu.Mode);
        // Binding order, available amounts (amount minus the reserved in-use word) and the
        // equipped usable appendix (itemmenu.c appends those with amount 0 outside battle).
        Assert.Equal([(300, "丹药", true, false, 2), (301, "符咒", true, false, 1), (901, "头带", false, false, 1),
                (901, "头带", false, true, 0)],
            menu.ItemsView().Select(row => (row.ObjectId, row.Name, row.Consuming, row.Equipped, row.Amount)).ToArray());
        // A reserved in-use word hides its copies from the usable amount without a stock change.
        var reserved = new InventoryLiveState(
            [new InventorySlot(300, 2, 1), new InventorySlot(301, 1, 0), new InventorySlot(901, 1, 0)]);
        var reservedMenu = Menu(inventory: reserved).menu;
        reservedMenu.Open();
        reservedMenu.Press(ExplorationMenuKey.Down);
        reservedMenu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal([(300, 1), (301, 1), (901, 1), (901, 0)],
            reservedMenu.ItemsView().Select(row => (row.ObjectId, row.Amount)).ToArray());
        Assert.Equal(2, reserved.GetAmount(300));
        // itemmenu.c PAL_ItemSelectMenuUpdate: the remembered inventory cursor clamps at both
        // list ends (Up at the first row and Down past the last row stay put; no wrap).
        menu.Press(ExplorationMenuKey.Up);
        Assert.Equal(0, menu.SelectedEquipItem);
        menu.Press(ExplorationMenuKey.Down);
        menu.Press(ExplorationMenuKey.Down);
        menu.Press(ExplorationMenuKey.Down);
        Assert.Equal(3, menu.SelectedEquipItem);
        menu.Press(ExplorationMenuKey.Down);
        Assert.Equal(3, menu.SelectedEquipItem);
        menu.Press(ExplorationMenuKey.Up);
        menu.Press(ExplorationMenuKey.Up);
        menu.Press(ExplorationMenuKey.Up);
        Assert.Equal(0, menu.SelectedEquipItem);
        // 300 is single-target: the player select opens with the remembered player cursor.
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.ItemTargets, menu.Mode);
        var view = menu.ItemTargetsView();
        Assert.Equal((300, "丹药", "生命", "法力"), (view.ObjectId, view.Name, view.HpLabel, view.MpLabel));
        Assert.Equal([("大侠", 100, 150, 20, 50), ("小侠", 95, 150, 20, 50)],
            view.Targets.Select(target => (target.Name, target.Hp, target.MaxHp, target.Mp, target.MaxMp)).ToArray());
        // PAL_ItemUseMenu: Left/Up and Right/Down wrap over the party (sSelectedPlayer).
        menu.Press(ExplorationMenuKey.Up);
        Assert.Equal(1, menu.SelectedUseRole); // 0 wraps to the last member
        menu.Press(ExplorationMenuKey.Down);
        Assert.Equal(0, menu.SelectedUseRole); // 1 wraps to the first member
        menu.Press(ExplorationMenuKey.Down);
        Assert.Equal(1, menu.SelectedUseRole);
        // A cancelled player select breaks back to the item list; nothing ran or changed.
        menu.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationMenuMode.Items, menu.Mode);
        Assert.Equal([(300, 2), (301, 1), (901, 1)], Stock(menu, inventory));
        // Cancelling the item select ends the whole use flow: itemmenu.c kKeyMenu returns 0,
        // play.c PAL_GameUseItem returns and uigame.c case 3 'goto out' closes the menu.
        menu.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationMenuMode.Closed, menu.Mode);
        // The remembered entry and player cursor survive closes (iCurMainMenuItem, sSelectedPlayer).
        menu.Open();
        menu.Press(ExplorationMenuKey.Confirm); // remembered items entry
        Assert.Equal(ExplorationMenuMode.Items, menu.Mode);
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.ItemTargets, menu.Mode);
        Assert.Equal(1, menu.SelectedUseRole);

        // An empty usable list selects object 0: the use flow returns with no open menu
        // (play.c PAL_GameUseItem returns; uigame.c case 3 'goto out' closes the menu).
        var empty = Menu(inventory: new InventoryLiveState([]), wearsUsable: false).menu;
        empty.Open();
        empty.Press(ExplorationMenuKey.Down);
        empty.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.Items, empty.Mode);
        Assert.Empty(empty.ItemsView());
        empty.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.Closed, empty.Mode);

        // Items-entry construction requires the whole shared use context up front.
        Assert.Contains("requires authored use-menu bindings", Assert.Throws<InvalidDataException>(
            () => Menu(noUseMenu: true).menu).Message);
        Assert.Contains("requires the shared live inventory owner", Assert.Throws<InvalidDataException>(
            () => Menu(noInventory: true).menu).Message);
        Assert.Contains("unique object WORD ids", Assert.Throws<InvalidDataException>(
            () => Menu(duplicateBinding: true).menu).Message);
        Assert.Contains("901", Assert.Throws<InvalidDataException>(
            () => Menu(missingItemWord: true).menu).Message);
        Assert.Throws<InvalidOperationException>(() => Menu().menu.ItemsView());
        Assert.Throws<InvalidOperationException>(() => Menu().menu.ItemTargetsView());
        Assert.Throws<InvalidOperationException>(() => Menu().menu.Press(ExplorationMenuKey.Confirm));
    }

    [Fact]
    public void Item_bitmap_word_binds_the_ball_chunk_identity_through_both_item_pages()
    {
        // #218: the authored binding carries rgObject item.wBitmap — the BALL chunk the item
        // pages draw (itemmenu.c/uigame.c); 0 is the reference's empty-chunk no-draw word.
        var content = ModernBattleContent.Parse(UseContextData().ToJsonString());
        Assert.Equal([(300, 37), (301, 41), (302, 0)],
            content.UseMenu.Select(binding => (binding.ObjectId, binding.Bitmap)).ToArray());
        // The projection keeps the binding's bitmap word on every listed row — stock rows and
        // the equipped appendix alike (itemmenu.c reads the selected row's object bitmap).
        var (menu, _, _) = Menu();
        menu.Open();
        menu.Press(ExplorationMenuKey.Down);
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal([(300, 37), (301, 41), (901, 252), (901, 252)],
            menu.ItemsView().Select(row => (row.ObjectId, row.Bitmap)).ToArray());
        // The target page exposes the same item bitmap word (uigame.c PAL_ItemUseMenu draws
        // the item picture beside the party rows while picking).
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(37, menu.ItemTargetsView().Bitmap);
        // Direct constructions keep the WORD domain: an out-of-range bitmap rejects up front.
        Assert.Contains("$.useMenu[0].bitmap", Assert.Throws<InvalidDataException>(()
            => new TriggerBattleContent(content.InitialRoles.Values.ToArray(), content.Players,
                content.GrowthByRole, content.EnemyTeams,
                useMenu: [new BattleUseMenuBinding(300, true, false, 65536)])).Message);
    }

    [Fact]
    public void Item_use_consumes_only_on_success_repeats_the_target_loop_and_returns_after_all()
    {
        var (menu, inventory, roles) = Menu();
        menu.Open();
        menu.Press(ExplorationMenuKey.Down);
        menu.Press(ExplorationMenuKey.Confirm);
        menu.Press(ExplorationMenuKey.Confirm); // 300 opens the player select (single target)
        menu.Press(ExplorationMenuKey.Confirm); // 大侠 commits the use
        Assert.Equal(ExplorationMenuMode.ItemScript, menu.Mode);
        var request = menu.PendingItemUse;
        Assert.Equal((300, false, 0), (request!.ObjectId, request.ApplyToAll, request.TargetRoleId));
        Assert.Throws<InvalidOperationException>(() => menu.Press(ExplorationMenuKey.Cancel));
        Assert.Throws<InvalidOperationException>(() => menu.CompleteItemUse(Guid.NewGuid(), true));
        // The success receipt consumes one copy; the target loop reopens (play.c repeat use).
        menu.CompleteItemUse(request.Id, true);
        Assert.Equal(ExplorationMenuMode.ItemTargets, menu.Mode);
        Assert.Null(menu.PendingItemUse);
        Assert.Equal(1, inventory.GetAmount(300));
        // A failed consuming use keeps the copy (kItemFlagConsuming AND g_fScriptSuccess).
        menu.Press(ExplorationMenuKey.Down);
        menu.Press(ExplorationMenuKey.Confirm);
        var failed = menu.PendingItemUse!;
        Assert.Equal(1, failed.TargetRoleId);
        menu.CompleteItemUse(failed.Id, false);
        Assert.Equal(ExplorationMenuMode.ItemTargets, menu.Mode);
        Assert.Equal(1, inventory.GetAmount(300));
        // The last 300 copy leaves on the next success; the row leaves the compressed list.
        menu.Press(ExplorationMenuKey.Confirm);
        var last = menu.PendingItemUse!;
        menu.CompleteItemUse(last.Id, true);
        Assert.Equal(0, inventory.GetAmount(300));
        menu.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationMenuMode.Items, menu.Mode);
        Assert.Equal([301, 901, 901], menu.ItemsView().Select(row => row.ObjectId).ToArray());
        Assert.Equal(0, menu.SelectedEquipItem); // the remembered cursor clamped to the list

        // An apply-to-all item commits straight from the list with the 0xFFFF caller word;
        // after its one run PAL_GameUseItem returns and uigame.c PAL_InGameMenu case 3
        // 'goto out' closes the whole menu (no player select, no repeat loop).
        menu.Press(ExplorationMenuKey.Confirm); // cursor 0: 301
        Assert.Equal(ExplorationMenuMode.ItemScript, menu.Mode);
        var all = menu.PendingItemUse!;
        Assert.Equal((301, true, 65535), (all.ObjectId, all.ApplyToAll, all.TargetRoleId));
        menu.CompleteItemUse(all.Id, true);
        Assert.Equal(ExplorationMenuMode.Closed, menu.Mode);
        Assert.Equal(0, inventory.GetAmount(301));
        menu.Open(); // the remembered entries cursor reopens the items page
        menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.Items, menu.Mode);
        Assert.Equal([901, 901], menu.ItemsView().Select(row => row.ObjectId).ToArray());

        // The equipped appendix row (object 901, worn by 小侠) stays usable: the non-consuming
        // binding never removes anything and the run repeats the player select like any
        // single-target use (itemmenu.c's amount-0 appendix remains listed).
        menu.Press(ExplorationMenuKey.Confirm); // cursor 0: the stocked 901 row
        Assert.Equal(ExplorationMenuMode.ItemTargets, menu.Mode);
        Assert.Equal(901, menu.ItemSelection);
        menu.Press(ExplorationMenuKey.Confirm); // the remembered player cursor stays on 小侠
        var worn = menu.PendingItemUse!;
        Assert.Equal(1, worn.TargetRoleId);
        menu.CompleteItemUse(worn.Id, true);
        Assert.Equal(ExplorationMenuMode.ItemTargets, menu.Mode);
        Assert.Equal([901, 0, 0, 0, 0, 0], roles.Roles[1].Equipment.ToArray()); // still equipped
        Assert.Equal(1, inventory.GetAmount(901)); // non-consuming: no copy removed
        menu.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationMenuMode.Items, menu.Mode);
        Assert.Equal([(901, 1), (901, 0)],
            menu.ItemsView().Select(row => (row.ObjectId, row.Amount)).ToArray());

        // An equipped-only appendix row (no stock row for the object) still enters the target
        // page, but uigame.c PAL_ItemUseMenu's i = PAL_GetItemAmount <= 0 gate cancels it on any
        // key: no script run is submitted, nothing is deducted and the item list reopens.
        var wornOnly = Menu(inventory: new InventoryLiveState(
            [new InventorySlot(300, 2, 0), new InventorySlot(301, 1, 0)]));
        wornOnly.menu.Open();
        wornOnly.menu.Press(ExplorationMenuKey.Down);
        wornOnly.menu.Press(ExplorationMenuKey.Confirm); // items: 300, 301, then the 901 appendix
        wornOnly.menu.Press(ExplorationMenuKey.Down);
        wornOnly.menu.Press(ExplorationMenuKey.Down);
        wornOnly.menu.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationMenuMode.ItemTargets, wornOnly.menu.Mode); // the page draws once
        wornOnly.menu.Press(ExplorationMenuKey.Confirm); // any key cancels: no run committed
        Assert.Equal(ExplorationMenuMode.Items, wornOnly.menu.Mode);
        Assert.Null(wornOnly.menu.PendingItemUse);
        Assert.Equal([300, 301, 901], wornOnly.menu.ItemsView().Select(row => row.ObjectId).ToArray());
        Assert.Equal(0, wornOnly.inventory.GetAmount(901));

        // A dead target member stays selectable (PAL_ItemUseMenu lists the whole party); the
        // host reports the no-effect run failed, so a consuming copy is kept.
        var dead = Menu(role1Hp: 0);
        dead.menu.Open();
        dead.menu.Press(ExplorationMenuKey.Down);
        dead.menu.Press(ExplorationMenuKey.Confirm);
        dead.menu.Press(ExplorationMenuKey.Confirm); // 300 opens the player select
        dead.menu.Press(ExplorationMenuKey.Down); // target cursor to 小侠
        dead.menu.Press(ExplorationMenuKey.Confirm);
        var onDead = dead.menu.PendingItemUse!;
        Assert.Equal(1, onDead.TargetRoleId);
        dead.menu.CompleteItemUse(onDead.Id, false); // changeHpMp skips dead roles (no change)
        Assert.Equal(2, dead.inventory.GetAmount(300));
    }

    private static List<(int ObjectId, int Amount)> Stock(ExplorationMenuSession session, InventoryLiveState owner) =>
        session.ItemsView().Where(row => !row.Equipped)
            .Select(row => (row.ObjectId, owner.GetAmount(row.ObjectId))).ToList();

    private static InvalidDataException Reject(string mutation, string expected)
    {
        var data = UseContextData();
        switch (mutation)
        {
            case "objectId0": data["useMenu"]![0]!["objectId"] = 0; break;
            case "typo": data["useMenu"]![0]!["throwable"] = true; break;
            case "consumingString": data["useMenu"]![0]!["consuming"] = "yes"; break;
            case "bitmapString": data["useMenu"]![0]!["bitmap"] = "yes"; break;
            case "bitmapRange": data["useMenu"]![0]!["bitmap"] = 65536; break;
            case "bitmapMissing": data["useMenu"]![0]!.AsObject().Remove("bitmap"); break;
            case "duplicateBinding": ((JsonArray)data["useMenu"]!).Add(
                new JsonObject { ["objectId"] = 300, ["consuming"] = false, ["applyToAll"] = false, ["bitmap"] = 0 }); break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        return Assert.Throws<InvalidDataException>(() => ModernBattleContent.Parse(data.ToJsonString()));
    }

    private static (ExplorationMenuSession menu, InventoryLiveState inventory, RoleLiveState roles) Menu(
        InventoryLiveState? inventory = null, bool noUseMenu = false, bool noInventory = false,
        bool duplicateBinding = false, bool missingItemWord = false, int role1Hp = 95, bool wearsUsable = true)
    {
        int[] role1Equipment = wearsUsable ? [901, 0, 0, 0, 0, 0] : [0, 0, 0, 0, 0, 0];
        var roles = new RoleLiveState([Role(0, 100, [0, 0, 0, 0, 0, 0]), Role(1, role1Hp, role1Equipment)]);
        var poisons = new PartyPoisonState([0, 1], [PoisonRow(), PoisonRow()], new Dictionary<int, int>());
        var words = new Dictionary<int, string>
        {
            [ExplorationMenuSession.StatusEntryWord] = "状态",
            [ExplorationMenuSession.ItemsEntryWord] = "物品",
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
            [300] = "丹药",
            [301] = "符咒",
            [901] = "头带",
        };
        if (missingItemWord) words.Remove(901);
        // 300 heals one member (consuming, BALL chunk 37), 301 the party (consuming,
        // apply-to-all, chunk 41), 901 is the equipped usable appendix object (non-consuming,
        // chunk 252); role1 wears one copy of 901.
        IReadOnlyList<BattleUseMenuBinding>? useMenu = noUseMenu ? null
            : duplicateBinding ? [new BattleUseMenuBinding(300, true, false, 37), new BattleUseMenuBinding(300, false, true, 41)]
            : [new BattleUseMenuBinding(300, true, false, 37), new BattleUseMenuBinding(301, true, true, 41),
               new BattleUseMenuBinding(901, false, false, 252)];
        int[][] grid = [[0, 0, 0, 0, 0, 0], role1Equipment, [0, 0, 0, 0, 0, 0], [0, 0, 0, 0, 0, 0], [0, 0, 0, 0, 0, 0], [0, 0, 0, 0, 0, 0]];
        var equipmentObjects = new Dictionary<int, BattleEquipmentObject>
        {
            [901] = new(901, Enumerable.Range(0, 6).Select(_ => new BattleEquipmentEffect(0, 0, 0, 0, new int[9])).ToArray()),
        };
        var equipment = BattleEquipmentState.Initialize(grid, equipmentObjects);
        var owner = inventory ?? new InventoryLiveState(
            [new InventorySlot(300, 2, 0), new InventorySlot(301, 1, 0), new InventorySlot(901, 1, 0)]);
        var session = new ExplorationMenuSession(
            [new(ExplorationMenuPage.Status, ExplorationMenuSession.StatusEntryWord),
             new(ExplorationMenuPage.Items, ExplorationMenuSession.ItemsEntryWord)],
            [new ExplorationMenuMember(0, 60100, false, 40, 6, 20, 100, 3),
             new ExplorationMenuMember(1, 60101, false, 35, 5, 18, 99, 2)],
            roles, equipment,
            new Dictionary<int, ClassicBattleGrowthData>
            {
                [0] = new([90, 100, 111, 12], []), [1] = new([90, 100, 111, 12], []),
            },
            words, poisons, equipMenu: null, equipmentObjects: null, noInventory ? null : owner, useMenu);
        return (session, owner, roles);
    }

    private static IReadOnlyList<PoisonSlot> PoisonRow()
    {
        var row = new PoisonSlot[PartyPoisonState.Capacity];
        for (int i = 0; i < row.Length; i++) row[i] = new(0, 0);
        return row;
    }

    private static BattleRoleSnapshot Role(int roleId, int hp, int[]? equipment = null) => new(roleId, 2, hp, 150, 20, 50,
        new BattleExperienceSnapshot(77, 3, 12), Hidden(), equipment ?? new int[6], new int[32], new int[9], 0,
        0, 0, 0, 0, 0);

    private static IReadOnlyDictionary<string, BattleExperienceSnapshot> Hidden() =>
        new Dictionary<string, BattleExperienceSnapshot>
        {
            ["health"] = new(0, 1, 0), ["magic"] = new(0, 1, 0), ["attack"] = new(0, 1, 0),
            ["magicPower"] = new(0, 1, 0), ["defense"] = new(0, 1, 0), ["dexterity"] = new(0, 1, 0),
            ["flee"] = new(0, 1, 0),
        };

    private static JsonObject UseContextData()
    {
        var data = Data();
        data["inventory"] = new JsonArray(
            new JsonObject { ["itemId"] = 300, ["amount"] = 2 },
            new JsonObject { ["itemId"] = 301, ["amount"] = 1 },
            new JsonObject { ["itemId"] = 302, ["amount"] = 1 });
        data["useMenu"] = new JsonArray(
            new JsonObject { ["objectId"] = 300, ["consuming"] = true, ["applyToAll"] = false, ["bitmap"] = 37 },
            new JsonObject { ["objectId"] = 301, ["consuming"] = true, ["applyToAll"] = true, ["bitmap"] = 41 },
            new JsonObject { ["objectId"] = 302, ["consuming"] = true, ["applyToAll"] = false, ["bitmap"] = 0 });
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
}
