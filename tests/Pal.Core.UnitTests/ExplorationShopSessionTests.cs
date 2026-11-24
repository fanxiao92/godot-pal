using System.Text.Json.Nodes;
using Pal.Core;
using Xunit;

namespace Pal.Core.Tests;

// #217 (M6-B T05): the shop menus over the shared cash/inventory owners. script.c 0x0026/0x0027
// (uigame.c PAL_BuyMenu/PAL_SellMenu/PAL_ConfirmMenu, itemmenu.c PAL_ItemSelectMenu) become
// gated, host-receipted trigger commands; the shop session itself is a pure projection that
// only writes cash/inventory inside a confirmed transaction.
public class ExplorationShopSessionTests
{
    [Fact]
    public void ShowShopMenusPreflightRejectWithoutTheAuthoredShopContext()
    {
        // Without the authored shop context the preflight must reject with the shop-context
        // diagnostic — never the generic content-only rejection the stream carried before #217.
        var fixture = new EventFixture();
        Set(fixture, 1, 0x0026, """{"op":"showBuyMenu","storeId":5}""");
        Set(fixture, 2, 0x0027, """{"op":"showSellMenu"}""");
        Set(fixture, 3, 0, """{"op":"end","mode":"keep"}""");
        var content = MinimalContent();
        var error = Assert.Throws<InvalidDataException>(() => TriggerScriptSession.ValidateEntry(
            fixture.Load(), Language(), content, "shared", "npc",
            cashLiveState: new CashLiveState(10), inventoryLiveState: new InventoryLiveState([])));
        Assert.Contains("showBuyMenu requires the authored shop objects", error.Message);
        // The sell menu needs the shop objects; the buy menu additionally needs its store row,
        // and both need the explicit shared owners before any cash or inventory write.
        var sellOnly = new EventFixture();
        Set(sellOnly, 1, 0x0027, """{"op":"showSellMenu"}""");
        Set(sellOnly, 2, 0, """{"op":"end","mode":"keep"}""");
        var sellError = Assert.Throws<InvalidDataException>(() => TriggerScriptSession.ValidateEntry(
            sellOnly.Load(), Language(), content, "shared", "npc"));
        Assert.Contains("showSellMenu requires the authored shop objects", sellError.Message);
        var objectsOnly = Content(shopObjects: ShopObjects());
        Assert.Contains("showBuyMenu requires the authored store rows and shop objects",
            Assert.Throws<InvalidDataException>(() => TriggerScriptSession.ValidateEntry(
                fixture.Load(), Language(), objectsOnly, "shared", "npc",
                cashLiveState: new CashLiveState(10), inventoryLiveState: new InventoryLiveState([]))).Message);
        var shop = ShopContent();
        var ownerError = Assert.Throws<InvalidDataException>(() => TriggerScriptSession.ValidateEntry(
            fixture.Load(), Language(), shop, "shared", "npc"));
        Assert.Contains("showBuyMenu requires explicit cash and inventory owners", ownerError.Message);
        var missingStore = Assert.Throws<InvalidDataException>(() => TriggerScriptSession.ValidateEntry(
            BuyFixture(9).Load(), Language(), shop, "shared", "npc",
            cashLiveState: new CashLiveState(10), inventoryLiveState: new InventoryLiveState([])));
        Assert.Contains("showBuyMenu store 9 is not authored", missingStore.Message);
    }

    [Fact]
    public void ShopMenusParkTheTriggerUntilTheHostReceiptsEachClosedMenu()
    {
        // script.c 0x0026/0x0027: the script blocks inside PAL_BuyMenu/PAL_SellMenu; the host
        // owns the menu over the shared owners and only the current request's receipt resumes
        // the call — wrong-id receipts stay inert and no receipt is applied twice.
        var fixture = BuyFixture(5);
        Set(fixture, 2, 0x0027, """{"op":"showSellMenu"}""");
        Set(fixture, 3, 0, """{"op":"end","mode":"keep"}""");
        var content = ShopContent();
        var cash = new CashLiveState(600);
        var bag = new InventoryLiveState([]);
        var script = new TriggerScriptSession(fixture.Load(), Language(), content, new CountingRandom(),
            cashLiveState: cash, inventoryLiveState: bag);
        Assert.Empty(TriggerScriptSession.ValidateEntry(fixture.Load(), Language(), content, "shared", "npc",
            cashLiveState: cash, inventoryLiveState: bag).BattleTeamIds);
        script.Start("shared", "npc", 60);
        Assert.Equal(TriggerScriptPhase.WaitingForShop, script.Phase);
        Assert.True(script.BlocksSceneUpdates);
        var buy = Assert.IsType<TriggerShopRequest>(script.PendingShopRequest);
        Assert.True(buy.IsBuy);
        Assert.Equal(5, buy.StoreId);
        Assert.False(script.AcknowledgeShop(Guid.NewGuid()));
        Assert.Equal(TriggerScriptPhase.WaitingForShop, script.Phase);
        Assert.True(script.AcknowledgeShop(buy.Id));
        Assert.False(script.AcknowledgeShop(buy.Id)); // receipts never apply twice
        Assert.Equal(TriggerScriptPhase.WaitingForShop, script.Phase);
        var sell = Assert.IsType<TriggerShopRequest>(script.PendingShopRequest);
        Assert.False(sell.IsBuy);
        Assert.Equal(0, sell.StoreId);
        Assert.True(script.AcknowledgeShop(sell.Id));
        Assert.Null(script.PendingShopRequest);
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(600u, cash.Cash);
    }

    [Fact]
    public void BuyMenuPurchasesDeductCashAndStockOnlyOnConfirmedAffordableRows()
    {
        // Independent numbers: cash 600; store 5 lists 300 (price 120), 199 (250) and 320 (999).
        // PAL_BuyMenu: the affordability gate opens the No/Yes box; No/cancel never write; Yes
        // debits first and adds one copy; the cursor wraps and stays on the purchased row.
        var (shop, cash, bag, _) = Session();
        Assert.Throws<InvalidDataException>(() => shop.OpenBuy(9));
        shop.OpenBuy(5);
        Assert.Equal(ExplorationShopMode.BuyList, shop.Mode);
        Assert.Equal("300:VITAL PILL:120;199:LEATHER CAP:250;320:MEDAL:999", BuyText(shop.BuyRowsView()));
        var status = shop.BuyStatusView();
        Assert.Equal((600u, 0, "CASH", "OWNED"), (status.Cash, status.Owned, status.CashLabel, status.OwnedLabel));
        Assert.Equal((300, "VITAL PILL", 120), (status.ObjectId, status.Name, status.Price));

        // An unaffordable row ignores Search: no confirm box, no deduction (uigame.c 1682-1686).
        shop.Press(ExplorationMenuKey.Down);
        shop.Press(ExplorationMenuKey.Down);
        Assert.Equal(320, shop.BuyRowsView()[shop.SelectedBuyItem].ObjectId);
        shop.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationShopMode.BuyList, shop.Mode);
        Assert.Equal(600u, cash.Cash);

        // 300: confirm-No leaves everything untouched; the confirm cursor starts at No.
        shop.Press(ExplorationMenuKey.Up);
        shop.Press(ExplorationMenuKey.Up); // wrap 320 -> 199 -> 300 (ui.c PAL_ReadMenu wraps)
        Assert.Equal(300, shop.BuyRowsView()[shop.SelectedBuyItem].ObjectId);
        shop.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationShopMode.BuyConfirm, shop.Mode);
        var confirm = shop.ConfirmView();
        Assert.Equal(("NO", "YES", 0), (confirm.NoLabel, confirm.YesLabel, confirm.SelectedIndex));
        shop.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationShopMode.BuyList, shop.Mode);
        Assert.Equal((600u, 0), (cash.Cash, bag.GetAmount(300)));

        // A confirmed purchase debits first, adds one copy and keeps the cursor on the row.
        shop.Press(ExplorationMenuKey.Confirm);
        shop.Press(ExplorationMenuKey.Down);
        Assert.Equal(1, shop.ConfirmView().SelectedIndex);
        shop.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationShopMode.BuyList, shop.Mode);
        Assert.Equal((480u, 1), (cash.Cash, bag.GetAmount(300)));
        Assert.Equal(300, shop.BuyRowsView()[shop.SelectedBuyItem].ObjectId);
        shop.Press(ExplorationMenuKey.Confirm);
        shop.Press(ExplorationMenuKey.Down);
        shop.Press(ExplorationMenuKey.Confirm);
        Assert.Equal((360u, 2), (cash.Cash, bag.GetAmount(300)));

        // 199: the owned panel counts bag copies plus equipped copies across the party
        // (PAL_BuyMenu_OnItemChange's MAX_PLAYER_EQUIPMENTS loop; role 1 wears 199).
        shop.Press(ExplorationMenuKey.Down);
        shop.Press(ExplorationMenuKey.Confirm);
        shop.Press(ExplorationMenuKey.Down);
        shop.Press(ExplorationMenuKey.Confirm);
        Assert.Equal((110u, 2), (cash.Cash, bag.GetAmount(199)));
        Assert.Equal(199, shop.BuyRowsView()[shop.SelectedBuyItem].ObjectId);
        Assert.Equal(3, shop.BuyStatusView().Owned); // 2 bought + 1 equipped on role 1

        // kKeyMenu cancels the whole buy loop (script.c returns from the command).
        shop.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationShopMode.Closed, shop.Mode);

        // A reopened buy menu starts at a fresh cursor (PAL_BuyMenu's local w = 0).
        shop.OpenBuy(5);
        Assert.Equal(0, shop.SelectedBuyItem);
        shop.Press(ExplorationMenuKey.Cancel);
    }

    [Fact]
    public void BuyMenuKeepsTheReferenceCashFirstCommitWhenTheBagIsFull()
    {
        // PAL_BuyMenu 1687-1692 debits dwCash and then ignores PAL_AddItemToInventory's result:
        // a full bag keeps the spent cash — never a refund, never a half purchase.
        var slots = Enumerable.Range(0, InventoryLiveState.Capacity)
            .Select(index => new InventorySlot(1000 + index, 1, 0)).ToArray();
        var bag = new InventoryLiveState(slots);
        var cash = new CashLiveState(600);
        var shop = Shop(bag, cash);
        shop.OpenBuy(5);
        shop.Press(ExplorationMenuKey.Confirm);
        shop.Press(ExplorationMenuKey.Down);
        shop.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationShopMode.BuyList, shop.Mode);
        Assert.Equal(480u, cash.Cash);
        Assert.Equal(0, bag.GetAmount(300));
        Assert.Equal(256, bag.Slots.Count(slot => slot.ItemId != 0));
    }

    [Fact]
    public void SellMenuHalvesPricesRemovesStockAndIgnoresUnsellableRows()
    {
        // Independent numbers: 197 price 40 -> sale 20, 199 price 250 -> 125, 210 price 90 ->
        // 45 but not sellable, 300 price 120 -> 60, 301 price 1 -> 0 (integer division; the
        // copy still leaves the bag before the credit).
        var (shop, cash, bag, _) = Session();
        bag.AddItem(300, 2);
        bag.AddItem(301, 1);
        shop.OpenSell();
        Assert.Equal(ExplorationShopMode.SellList, shop.Mode);
        Assert.Equal("197:HEADBAND:20;199:LEATHER CAP:125;210:CAPE:45;300:VITAL PILL:60;301:LUCK SEED:0",
            SellText(shop.SellRowsView()));

        // A non-sellable row stays listed but Search never confirms it (itemmenu.c's flag gate).
        shop.Press(ExplorationMenuKey.Down);
        shop.Press(ExplorationMenuKey.Down);
        Assert.Equal(210, shop.SellRowsView()[shop.SelectedSellItem].ObjectId);
        shop.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationShopMode.SellList, shop.Mode);
        Assert.Equal(600u, cash.Cash);

        // The sell confirm keeps the No default: No leaves bag and cash untouched.
        shop.Press(ExplorationMenuKey.Up);
        shop.Press(ExplorationMenuKey.Up);
        shop.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationShopMode.SellConfirm, shop.Mode);
        Assert.Equal(0, shop.ConfirmView().SelectedIndex);
        shop.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationShopMode.SellList, shop.Mode);
        Assert.Equal((600u, 2), (cash.Cash, bag.GetAmount(197)));

        // A confirmed sale removes one copy first, then credits the half price.
        var status = shop.SellStatusView();
        Assert.Equal((600u, 20, "CASH", "PRICE"), (status.Cash, status.Price, status.CashLabel, status.PriceLabel));
        shop.Press(ExplorationMenuKey.Confirm);
        shop.Press(ExplorationMenuKey.Down);
        shop.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationShopMode.SellList, shop.Mode);
        Assert.Equal((620u, 1), (cash.Cash, bag.GetAmount(197)));

        // Moves clamp at both ends without wrap (itemmenu.c PAL_ItemSelectMenuUpdate).
        shop.Press(ExplorationMenuKey.Up);
        shop.Press(ExplorationMenuKey.Up);
        shop.Press(ExplorationMenuKey.Up);
        Assert.Equal(0, shop.SelectedSellItem);

        // Selling both 300 copies drops the row; the cursor clamps onto the tail.
        shop.Press(ExplorationMenuKey.Down);
        shop.Press(ExplorationMenuKey.Down);
        shop.Press(ExplorationMenuKey.Down); // 300
        shop.Press(ExplorationMenuKey.Confirm);
        shop.Press(ExplorationMenuKey.Down);
        shop.Press(ExplorationMenuKey.Confirm);
        Assert.Equal((680u, 1), (cash.Cash, bag.GetAmount(300)));
        shop.Press(ExplorationMenuKey.Confirm);
        shop.Press(ExplorationMenuKey.Down);
        shop.Press(ExplorationMenuKey.Confirm);
        Assert.Equal((740u, 0), (cash.Cash, bag.GetAmount(300)));
        Assert.Equal("197:HEADBAND:20;199:LEATHER CAP:125;210:CAPE:45;301:LUCK SEED:0", SellText(shop.SellRowsView()));

        // A price of 1 sells for 0: the copy leaves while the cash stays (wPrice / 2).
        shop.Press(ExplorationMenuKey.Confirm);
        shop.Press(ExplorationMenuKey.Down);
        shop.Press(ExplorationMenuKey.Confirm);
        Assert.Equal((740u, 0), (cash.Cash, bag.GetAmount(301)));

        shop.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationShopMode.Closed, shop.Mode);
        // The remembered cursor survives closes and clamps on the next open.
        int remembered = shop.SelectedSellItem;
        shop.OpenSell();
        Assert.Equal(remembered, shop.SelectedSellItem);
        shop.Press(ExplorationMenuKey.Cancel);
    }

    [Fact]
    public void SellMenuPresentsTheEmptyBagAsTheReferenceEmptyList()
    {
        // itemmenu.c PAL_ItemSelectMenuInit counts an empty compressed bag without rejecting
        // and uigame.c PAL_SellMenu still opens over it: OnItemChange(object 0) draws the cash
        // panel and leaves the price box empty. Moves clamp inside the empty list, Search never
        // confirms it and only kKeyMenu closes — the empty page is presentable, not a failure.
        var cash = new CashLiveState(600);
        var bag = new InventoryLiveState([]);
        var shop = Shop(bag, cash);
        shop.OpenSell();
        Assert.Equal(ExplorationShopMode.SellList, shop.Mode);
        Assert.Empty(shop.SellRowsView());
        Assert.Equal(0, shop.SelectedSellItem);
        var empty = shop.SellStatusView();
        Assert.Equal((600u, "CASH", ""), (empty.Cash, empty.CashLabel, empty.PriceLabel));
        Assert.Equal((0, 0), (empty.ObjectId, empty.Price));
        shop.Press(ExplorationMenuKey.Down);
        shop.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationShopMode.SellList, shop.Mode);
        Assert.Equal(600u, cash.Cash);
        shop.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationShopMode.Closed, shop.Mode);

        // Selling the last copy empties the bag mid-menu (SellConfirm -> SellList clamps the
        // cursor over zero rows): the same cash-only page stays presentable with the credited
        // half price (197 price 40 -> sale 20).
        var lastBag = new InventoryLiveState([new InventorySlot(197, 1, 0)]);
        var lastCash = new CashLiveState(600);
        var lastShop = Shop(lastBag, lastCash);
        lastShop.OpenSell();
        lastShop.Press(ExplorationMenuKey.Confirm);
        lastShop.Press(ExplorationMenuKey.Down);
        lastShop.Press(ExplorationMenuKey.Confirm);
        Assert.Equal(ExplorationShopMode.SellList, lastShop.Mode);
        Assert.Empty(lastBag.Slots.Where(slot => slot.Amount > 0));
        Assert.Empty(lastShop.SellRowsView());
        Assert.Equal(0, lastShop.SelectedSellItem);
        var emptied = lastShop.SellStatusView();
        Assert.Equal((620u, "CASH", ""), (emptied.Cash, emptied.CashLabel, emptied.PriceLabel));
        lastShop.Press(ExplorationMenuKey.Cancel);
        Assert.Equal(ExplorationShopMode.Closed, lastShop.Mode);
    }

    [Fact]
    public void ShopContentValidationRejectsBrokenAuthoring()
    {
        // A store item without its priced shop object rejects before any menu state exists.
        var unpriced = Assert.Throws<InvalidDataException>(() => Content(
            shopObjects: [new BattleShopObjectBinding(320, 999, false, 0)],
            stores: [new BattleShopStoreBinding(5, [300])]));
        Assert.Contains("store item 300 has no authored shop object price", unpriced.Message);
        // Store rows are unique WORD ids with 1..9 items.
        var duplicate = Assert.Throws<InvalidDataException>(() => Content(
            shopObjects: [new BattleShopObjectBinding(300, 120, true, 0), new BattleShopObjectBinding(320, 999, false, 0)],
            stores: [new BattleShopStoreBinding(5, [300]), new BattleShopStoreBinding(5, [320])]));
        Assert.Contains("$.stores[1]: store rows require unique WORD ids", duplicate.Message);
        var tooMany = Assert.Throws<InvalidDataException>(() => Content(
            shopObjects: Enumerable.Range(0, 10).Select(i => new BattleShopObjectBinding(300 + i, 10, true, 0)).ToArray(),
            stores: [new BattleShopStoreBinding(5, Enumerable.Range(0, 10).Select(i => 300 + i).ToArray())]));
        Assert.Contains("a store row lists 1..9 item WORDs", tooMany.Message);
        // rgObject item.wBitmap is one word per object: a use-menu/shop disagreement rejects.
        var disagree = Assert.Throws<InvalidDataException>(() => Content(
            useMenu: [new BattleUseMenuBinding(300, true, false, 41)],
            shopObjects: [new BattleShopObjectBinding(300, 120, true, 37)],
            stores: [new BattleShopStoreBinding(5, [300])]));
        Assert.Contains("bitmap 37 disagrees with its use-menu bitmap 41", disagree.Message);
        // The modern parser carries the same strictness: a bad cash seed and a store row with a
        // zero item WORD reject at the parse boundary.
        var data = Data();
        data["cash"] = -1;
        Assert.Contains("$.cash", Assert.Throws<InvalidDataException>(() => ModernBattleContent.Parse(data.ToJsonString())).Message);
        data["cash"] = 600;
        data["stores"] = new JsonArray(new JsonObject { ["storeId"] = 5, ["items"] = new JsonArray(300, 0) });
        data["shopObjects"] = new JsonArray(
            new JsonObject { ["objectId"] = 300, ["price"] = 120, ["sellable"] = true, ["bitmap"] = 0 });
        Assert.Contains("expected integer in 1..65535",
            Assert.Throws<InvalidDataException>(() => ModernBattleContent.Parse(data.ToJsonString())).Message);
        data["stores"]![0]!["items"] = new JsonArray(300);
        var parsed = ModernBattleContent.Parse(data.ToJsonString());
        Assert.Equal(600u, parsed.InitialCash);
        Assert.Equal(5, parsed.Stores.Keys.Single());
        Assert.Equal([300], parsed.Stores[5].Items);
        Assert.True(parsed.ShopObjects[300].Sellable);
    }

    private static void Set(EventFixture fixture, int address, int opcode, string semantic)
    {
        var command = JsonNode.Parse(semantic)!.AsObject();
        command["label"] = $"L{address:X4}";
        command["next"] = $"L{address + 1:X4}";
        command["source"] = new JsonObject { ["index"] = address, ["opcode"] = opcode, ["operands"] = new JsonArray(123, 456, 789) };
        fixture.Commands[address] = command;
    }

    private static EventFixture BuyFixture(int storeId)
    {
        var fixture = new EventFixture();
        Set(fixture, 1, 0x0026, $$"""{"op":"showBuyMenu","storeId":{{storeId}}}""");
        Set(fixture, 2, 0, """{"op":"end","mode":"keep"}""");
        return fixture;
    }

    private static DialogueLanguagePackage Language() => new("en", [new DialogueMessageBlock(61, 61, [DialogueMessagePart.FromText("A")])]);

    private static string BuyText(IReadOnlyList<ExplorationShopBuyRow> rows) =>
        string.Join(";", rows.Select(row => $"{row.ObjectId}:{row.Name}:{row.Price}"));

    private static string SellText(IReadOnlyList<ExplorationShopSellRow> rows) =>
        string.Join(";", rows.Select(row => $"{row.ObjectId}:{row.Name}:{row.Price}"));

    private static TriggerBattleContent MinimalContent() => Content();

    private static TriggerBattleContent ShopContent() => Content(
        shopObjects: ShopObjects(),
        stores: [new BattleShopStoreBinding(5, [300, 199, 320])]);

    private static TriggerBattleContent Content(
        IReadOnlyList<BattleUseMenuBinding>? useMenu = null,
        IReadOnlyList<BattleShopObjectBinding>? shopObjects = null,
        IReadOnlyList<BattleShopStoreBinding>? stores = null) => new(
        [Role()], [Player()], new Dictionary<int, ClassicBattleGrowthData> { [1] = Growth() },
        new Dictionary<int, IReadOnlyList<ClassicBattleEnemy>>(),
        useMenu: useMenu, shopObjects: shopObjects, stores: stores);

    private static BattleShopObjectBinding[] ShopObjects() =>
    [
        new(300, 120, true, 0), new(199, 250, true, 0), new(320, 999, false, 0),
        new(197, 40, true, 0), new(210, 90, false, 0), new(301, 1, true, 0),
    ];

    private static BattleRoleSnapshot Role() => new(1, 2, 100, 150, 20, 50, new(5, 2, 17),
        new Dictionary<string, BattleExperienceSnapshot>(), [199, 0, 0, 0, 0, 0], new int[32], new int[9], 0);

    private static ClassicBattlePlayer Player() => new(1, 100, 150, 40, 20, 100);

    private static ClassicBattleGrowthData Growth() => new([], []);

    private static Dictionary<int, string> Words() => new()
    {
        [300] = "VITAL PILL", [199] = "LEATHER CAP", [320] = "MEDAL",
        [197] = "HEADBAND", [210] = "CAPE", [301] = "LUCK SEED",
        [ExplorationShopSession.CashWord] = "CASH", [ExplorationShopSession.ShopOwnedWord] = "OWNED",
        [ExplorationShopSession.ShopPriceWord] = "PRICE", [ExplorationShopSession.ConfirmNoWord] = "NO",
        [ExplorationShopSession.ConfirmYesWord] = "YES",
    };

    private static (ExplorationShopSession Shop, CashLiveState Cash, InventoryLiveState Bag, RoleLiveState Roles) Session()
    {
        var cash = new CashLiveState(600);
        var bag = new InventoryLiveState(
        [
            new InventorySlot(197, 2, 0), new InventorySlot(199, 1, 0), new InventorySlot(210, 1, 0),
        ]);
        var roles = new RoleLiveState([Role()]);
        var shop = new ExplorationShopSession(
            new Dictionary<int, BattleShopStoreBinding> { [5] = new(5, [300, 199, 320]) },
            ShopObjects().ToDictionary(binding => binding.ObjectId), cash, bag, roles, [1], Words());
        return (shop, cash, bag, roles);
    }

    private static ExplorationShopSession Shop(InventoryLiveState bag, CashLiveState cash) => new(
        new Dictionary<int, BattleShopStoreBinding> { [5] = new(5, [300, 199, 320]) },
        ShopObjects().ToDictionary(binding => binding.ObjectId), cash, bag, new RoleLiveState([Role()]), [1], Words());

    private static JsonObject Data()
    {
        var data = JsonNode.Parse("""
        {"formatVersion":1,"roles":[{
          "roleId":1,"level":2,"hp":100,"maxHp":150,"mp":20,"maxMp":50,
          "primaryExperience":{"value":5,"level":2,"count":17},
          "hiddenExperience":{
            "health":{"value":7,"level":3,"count":55},"magic":{"value":7,"level":3,"count":55},
            "attack":{"value":7,"level":3,"count":55},"magicPower":{"value":7,"level":3,"count":55},
            "defense":{"value":7,"level":3,"count":55},"dexterity":{"value":7,"level":3,"count":55},
            "flee":{"value":7,"level":3,"count":55}},
          "equipment":[0,0,0,0,0,0],"magics":[],"statuses":[0,0,0,0,0,0,0,0,0],"coveredByRoleId":0}],
         "players":[{"roleId":1,"hp":100,"maxHp":150,"attack":40,"defense":20,"dexterity":100,
          "equipment":[0,0,0,0,0,0],"statuses":[0,0,0,0,0,0,0,0,0],"poisons":[0],
          "dyingScript":0,"friendDeathScript":0,"coveredByRoleId":null,"attackAll":false}],
         "growth":[{"roleId":1,"levelUpExperience":[],"levelUpMagics":[]}],
         "teams":[{"teamId":18,"enemies":[{"id":7,"level":0,"hp":43,"attack":0,"defense":0,"dexterity":0,"physicalResistance":1,
          "statuses":[0,0,0,0,0,0,0,0,0],"poisons":[0],"turnStartScript":0,"readyScript":0,"battleEndScript":0,
          "magicId":0,"dualMove":0,"attackEquivItem":0,"attackEquivItemRate":0,"experience":0,"cash":0}]}]}
        """)!.AsObject();
        data["roles"]![0]!["magics"] = new JsonArray(Enumerable.Range(0, 32).Select(i => JsonValue.Create(0) as JsonNode).ToArray());
        data["growth"]![0]!["levelUpExperience"] = new JsonArray(Enumerable.Repeat(100, 100).Select(x => JsonValue.Create(x) as JsonNode).ToArray());
        return data;
    }

    private sealed class CountingRandom : IBattleRandom
    {
        public int NextInclusive(int from, int to) => from;
        public float NextInclusive(float from, float to) => from;
    }
}
