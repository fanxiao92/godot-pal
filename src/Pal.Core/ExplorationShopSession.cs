namespace Pal.Core;

/// <summary>Shop pages of the fixed reference: the buy list and its No/Yes confirm come from
/// uigame.c PAL_BuyMenu/PAL_SelectionMenu, the sell list from PAL_SellMenu over itemmenu.c
/// PAL_ItemSelectMenu(kItemFlagSellable) with its own No/Yes confirm.</summary>
public enum ExplorationShopMode { Closed, BuyList, BuyConfirm, SellList, SellConfirm }

/// <summary>One buy-list row (uigame.c PAL_BuyMenu over lprgStore[store].rgwItems): the object,
/// its resolved name, its rgObject item.wPrice WORD and its BALL bitmap word. Every row is
/// enabled — the reference's affordability check happens at the confirm gate, not the row.</summary>
public sealed record ExplorationShopBuyRow(int ObjectId, string Name, int Price, int Bitmap);

/// <summary>One sell-list row (itemmenu.c PAL_ItemSelectMenu(kItemFlagSellable) over the
/// compressed bag): the object, its resolved name, its sale price (wPrice / 2), its BALL
/// bitmap word, the sellable flag (rows without it stay listed but never confirm) and the
/// owned amount.</summary>
public sealed record ExplorationShopSellRow(int ObjectId, string Name, int Price, int Bitmap, bool Sellable, int Amount);

/// <summary>The selected buy row's side panel (uigame.c PAL_BuyMenu_OnItemChange): the cash
/// WORD, the owned count of the selected object (bag copies plus equipped copies across the
/// party) and the resolved label words.</summary>
public sealed record ExplorationShopBuyStatus(int ObjectId, string Name, int Price, uint Cash, int Owned,
    string CashLabel, string OwnedLabel);

/// <summary>The selected sell row's side panel (uigame.c PAL_SellMenu_OnItemChange): the cash
/// WORD, the sale price and the resolved label words.</summary>
public sealed record ExplorationShopSellStatus(int ObjectId, string Name, int Price, uint Cash,
    string CashLabel, string PriceLabel);

/// <summary>The No/Yes confirm box (uigame.c PAL_ConfirmMenu over PAL_SelectionMenu(2, 0)):
/// the resolved label words and the cursor, which starts at No and wraps both ways.</summary>
public sealed record ExplorationShopConfirmView(string NoLabel, string YesLabel, int SelectedIndex);

/// <summary>Shop state machine per the fixed reference (uigame.c PAL_BuyMenu/PAL_SellMenu,
/// itemmenu.c PAL_ItemSelectMenu, ui.c PAL_ReadMenu). Pure projection over the shared live
/// owners — cash, inventory and the party's equipped objects; the session never mutates the
/// language package and only writes cash/inventory inside a confirmed transaction.</summary>
public sealed class ExplorationShopSession
{
    // Modern shop word contract in the prepared language package, following the existing modern
    // menu ids (ExplorationMenuSession 60011..60027). The cash label reuses the settlement word
    // 60007 the reference shares as CASH_LABEL (ui.h) between battle and shop panels.
    public const int ConfirmNoWord = 60028, ConfirmYesWord = 60029;
    public const int ShopOwnedWord = 60030, ShopPriceWord = 60031;
    public const int CashWord = 60007;

    private readonly CashLiveState cash;
    private readonly InventoryLiveState inventory;
    private readonly RoleLiveState roles;
    private readonly IReadOnlyList<int> partyRoleIds;
    private readonly IReadOnlyDictionary<int, BattleShopObjectBinding> shopObjects;
    private readonly IReadOnlyDictionary<int, BattleShopStoreBinding> stores;
    private readonly IReadOnlyDictionary<int, string> words;

    public ExplorationShopSession(IReadOnlyDictionary<int, BattleShopStoreBinding> stores,
        IReadOnlyDictionary<int, BattleShopObjectBinding> shopObjects, CashLiveState cash,
        InventoryLiveState inventory, RoleLiveState roles, IReadOnlyList<int> partyRoleIds,
        IReadOnlyDictionary<int, string> words)
    {
        ArgumentNullException.ThrowIfNull(stores);
        ArgumentNullException.ThrowIfNull(shopObjects);
        ArgumentNullException.ThrowIfNull(cash);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(partyRoleIds);
        ArgumentNullException.ThrowIfNull(words);
        if (shopObjects.Count == 0)
            throw new InvalidDataException("The shop requires authored shop objects.");
        if (partyRoleIds.Count is < 1 or > 3 || partyRoleIds.Distinct().Count() != partyRoleIds.Count)
            throw new InvalidDataException("The shop follows one to three distinct party roles.");
        foreach (int roleId in partyRoleIds)
            if (!roles.Roles.ContainsKey(roleId))
                throw new InvalidDataException($"Party role {roleId} has no shared live role record.");
        this.stores = stores;
        this.shopObjects = shopObjects;
        this.cash = cash;
        this.inventory = inventory;
        this.roles = roles;
        this.partyRoleIds = partyRoleIds.ToArray();
        this.words = words;
        Stores = stores;
        foreach (var store in stores.Values)
            foreach (int item in store.Items)
                Word(item); // a store item's name word resolves now, before any menu state exists
        foreach (int objectId in shopObjects.Keys)
            Word(objectId);
        Word(ConfirmNoWord);
        Word(ConfirmYesWord);
        Word(ShopOwnedWord);
        Word(ShopPriceWord);
        Word(CashWord);
    }

    public ExplorationShopMode Mode { get; private set; }
    /// <summary>The remembered buy cursor (uigame.c PAL_BuyMenu's local w): a fresh 0 per open,
    /// kept across purchases within the menu and reset by the next open.</summary>
    public int SelectedBuyItem { get; private set; }
    /// <summary>The object selected in the buy flow (uigame.c PAL_BuyMenu's confirmed w).</summary>
    public int BuySelection { get; private set; }
    /// <summary>The confirm cursor (uigame.c PAL_SelectionMenu's nDefault 0 = No).</summary>
    public int ConfirmIndex { get; private set; }
    /// <summary>The remembered sell cursor (the reference's iCurInvMenuItem over the compressed
    /// bag): it survives closes and clamps to the current list, without wrap.</summary>
    public int SelectedSellItem { get; private set; }
    /// <summary>The object selected in the sell flow (uigame.c PAL_SellMenu's confirmed w).</summary>
    public int SellSelection { get; private set; }
    /// <summary>The store row of the open buy flow (lprgStore[wStoreNum]); 0 outside it.</summary>
    private int openedStoreId;
    public IReadOnlyDictionary<int, BattleShopStoreBinding> Stores { get; }

    /// <summary>script.c 0x0026: opens PAL_BuyMenu over the authored store row at a fresh cursor.</summary>
    public void OpenBuy(int storeId)
    {
        if (Mode != ExplorationShopMode.Closed) throw new InvalidOperationException("A shop menu is already open.");
        if (!stores.ContainsKey(storeId))
            throw new InvalidDataException($"Store row {storeId} is not authored.");
        openedStoreId = storeId;
        SelectedBuyItem = 0;
        Mode = ExplorationShopMode.BuyList;
    }

    /// <summary>script.c 0x0027: opens PAL_SellMenu over the player's bag at the remembered
    /// inventory cursor (itemmenu.c PAL_ItemSelectMenuInit clamps it to the compressed list).</summary>
    public void OpenSell()
    {
        if (Mode != ExplorationShopMode.Closed) throw new InvalidOperationException("A shop menu is already open.");
        ClampSellCursor();
        Mode = ExplorationShopMode.SellList;
    }

    public void Press(ExplorationMenuKey key)
    {
        if (Mode == ExplorationShopMode.Closed) throw new InvalidOperationException("The shop is closed.");
        switch (Mode)
        {
            case ExplorationShopMode.BuyList:
            {
                // uigame.c PAL_BuyMenu over ui.c PAL_ReadMenu: Up/Left and Down/Right wrap over
                // the store row; Search reaches the purchase gate only when the cash covers the
                // price; kKeyMenu cancels the whole buy loop. An empty store row is the
                // reference's undefined PAL_ReadMenu state — only the cancel path is defined here.
                var rows = BuyRows();
                switch (key)
                {
                    case ExplorationMenuKey.Up or ExplorationMenuKey.Left:
                        if (rows.Count > 0) SelectedBuyItem = (SelectedBuyItem + rows.Count - 1) % rows.Count;
                        break;
                    case ExplorationMenuKey.Down or ExplorationMenuKey.Right:
                        if (rows.Count > 0) SelectedBuyItem = (SelectedBuyItem + 1) % rows.Count;
                        break;
                    case ExplorationMenuKey.Confirm:
                        if (rows.Count == 0) break;
                        var row = rows[SelectedBuyItem];
                        // PAL_BuyMenu 1682-1686: only wPrice <= dwCash opens PAL_ConfirmMenu;
                        // an unaffordable row ignores the press (no confirm, no deduction).
                        if (ShopObject(row.ObjectId).Price <= cash.Cash)
                        {
                            BuySelection = row.ObjectId;
                            ConfirmIndex = 0; // PAL_SelectionMenu's nDefault = the No row
                            Mode = ExplorationShopMode.BuyConfirm;
                        }
                        break;
                    case ExplorationMenuKey.Cancel:
                        Mode = ExplorationShopMode.Closed;
                        break;
                }
                break;
            }
            case ExplorationShopMode.BuyConfirm:
                // uigame.c PAL_ConfirmMenu: the No/Yes rows wrap both ways; Search returns the
                // selected row, kKeyMenu counts as cancelled (both leave the purchase unmade).
                switch (key)
                {
                    case ExplorationMenuKey.Up or ExplorationMenuKey.Left:
                        ConfirmIndex = (ConfirmIndex + 1) % 2;
                        break;
                    case ExplorationMenuKey.Down or ExplorationMenuKey.Right:
                        ConfirmIndex = (ConfirmIndex + 1) % 2;
                        break;
                    case ExplorationMenuKey.Confirm:
                        if (ConfirmIndex == 1)
                        {
                            // PAL_BuyMenu 1687-1692: the reference debits dwCash first and then
                            // adds the copy, ignoring the add result — a full bag keeps the spent
                            // cash (never a refund, never a half purchase). The debit is the raw
                            // DWORD subtraction: Credit composes its exact two's-complement wrap
                            // (the affordability gate has just held the checked price).
                            var binding = ShopObject(BuySelection);
                            cash.Credit(unchecked((uint)-binding.Price));
                            _ = inventory.AddItem(BuySelection, 1);
                        }
                        Mode = ExplorationShopMode.BuyList;
                        break;
                    case ExplorationMenuKey.Cancel:
                        Mode = ExplorationShopMode.BuyList;
                        break;
                }
                break;
            case ExplorationShopMode.SellList:
            {
                // itemmenu.c PAL_ItemSelectMenuUpdate over the compressed bag: moves clamp at
                // both list ends without wrap; Search confirms only selectable rows (the sellable
                // flag with available stock); kKeyMenu returns 0 and ends the sell loop.
                var rows = SellRows();
                switch (key)
                {
                    case ExplorationMenuKey.Up or ExplorationMenuKey.Left:
                        if (SelectedSellItem > 0) SelectedSellItem--;
                        break;
                    case ExplorationMenuKey.Down or ExplorationMenuKey.Right:
                        if (SelectedSellItem < rows.Count - 1) SelectedSellItem++;
                        break;
                    case ExplorationMenuKey.Confirm:
                        if (rows.Count == 0) break;
                        var row = rows[SelectedSellItem];
                        if (row.Sellable && row.Amount > 0)
                        {
                            SellSelection = row.ObjectId;
                            ConfirmIndex = 0;
                            Mode = ExplorationShopMode.SellConfirm;
                        }
                        break;
                    case ExplorationMenuKey.Cancel:
                        Mode = ExplorationShopMode.Closed;
                        break;
                }
                break;
            }
            case ExplorationShopMode.SellConfirm:
                switch (key)
                {
                    case ExplorationMenuKey.Up or ExplorationMenuKey.Left:
                        ConfirmIndex = (ConfirmIndex + 1) % 2;
                        break;
                    case ExplorationMenuKey.Down or ExplorationMenuKey.Right:
                        ConfirmIndex = (ConfirmIndex + 1) % 2;
                        break;
                    case ExplorationMenuKey.Confirm:
                        if (ConfirmIndex == 1)
                        {
                            // PAL_SellMenu 1781-1789: the copy leaves the bag first; only its
                            // removal credits the half price (DWORD addition wraps like the
                            // reference's dwCash += wPrice / 2).
                            var binding = ShopObject(SellSelection);
                            if (inventory.RemoveItem(SellSelection, 1) == 1)
                                cash.Credit((uint)(binding.Price / 2));
                        }
                        Mode = ExplorationShopMode.SellList;
                        ClampSellCursor();
                        break;
                    case ExplorationMenuKey.Cancel:
                        Mode = ExplorationShopMode.SellList;
                        break;
                }
                break;
        }
    }

    /// <summary>The buy list of the opened store row, in authored order (lprgStore.rgwItems).</summary>
    public IReadOnlyList<ExplorationShopBuyRow> BuyRowsView()
    {
        if (Mode is not (ExplorationShopMode.BuyList or ExplorationShopMode.BuyConfirm))
            throw new InvalidOperationException("The buy menu is not open.");
        return BuyRows();
    }

    /// <summary>The selected buy row's side panel: the live cash WORD and the owned count of the
    /// selected object (PAL_BuyMenu_OnItemChange counts bag copies plus equipped copies).</summary>
    public ExplorationShopBuyStatus BuyStatusView()
    {
        if (Mode is not (ExplorationShopMode.BuyList or ExplorationShopMode.BuyConfirm))
            throw new InvalidOperationException("The buy menu is not open.");
        var rows = BuyRows();
        if (rows.Count == 0)
            throw new InvalidOperationException("The open buy menu has no selected row.");
        var row = rows[SelectedBuyItem];
        return new(row.ObjectId, row.Name, row.Price, cash.Cash, Owned(row.ObjectId),
            Word(CashWord), Word(ShopOwnedWord));
    }

    /// <summary>The sell list over the compressed bag (occupied rows with stock, in slot order);
    /// non-sellable rows stay listed with their flag clear, like the reference's inactive rows.</summary>
    public IReadOnlyList<ExplorationShopSellRow> SellRowsView()
    {
        if (Mode is not (ExplorationShopMode.SellList or ExplorationShopMode.SellConfirm))
            throw new InvalidOperationException("The sell menu is not open.");
        return SellRows();
    }

    /// <summary>The selected sell row's side panel (PAL_SellMenu_OnItemChange): the live cash
    /// WORD and the wPrice / 2 sale price of the selected object. An empty bag stays
    /// presentable (itemmenu.c counts it without rejecting): object 0 keeps the cash panel
    /// and leaves the price box empty — an empty price label, never a host rejection.</summary>
    public ExplorationShopSellStatus SellStatusView()
    {
        if (Mode is not (ExplorationShopMode.SellList or ExplorationShopMode.SellConfirm))
            throw new InvalidOperationException("The sell menu is not open.");
        var rows = SellRows();
        if (rows.Count == 0)
            return new(0, "", 0, cash.Cash, Word(CashWord), "");
        var row = rows[SelectedSellItem];
        return new(row.ObjectId, row.Name, row.Price, cash.Cash, Word(CashWord), Word(ShopPriceWord));
    }

    /// <summary>The No/Yes confirm box of the open purchase or sale.</summary>
    public ExplorationShopConfirmView ConfirmView()
    {
        if (Mode is not (ExplorationShopMode.BuyConfirm or ExplorationShopMode.SellConfirm))
            throw new InvalidOperationException("The shop confirm box is not open.");
        return new(Word(ConfirmNoWord), Word(ConfirmYesWord), ConfirmIndex);
    }

    private IReadOnlyList<ExplorationShopBuyRow> BuyRows()
    {
        var store = stores.TryGetValue(openedStoreId, out var row) ? row
            : throw new InvalidOperationException("The open buy menu has no store row.");
        return store.Items.Select(item =>
        {
            var binding = ShopObject(item);
            return new ExplorationShopBuyRow(item, Word(item), binding.Price, binding.Bitmap);
        }).ToArray();
    }

    private IReadOnlyList<ExplorationShopSellRow> SellRows()
    {
        var rows = new List<ExplorationShopSellRow>();
        foreach (var slot in inventory.Slots)
        {
            if (slot.ItemId == 0 || slot.Amount <= 0) continue; // the compressed bag view
            var binding = shopObjects.TryGetValue(slot.ItemId, out var bound) ? bound : null;
            rows.Add(new(slot.ItemId, Word(slot.ItemId), (binding?.Price ?? 0) / 2, binding?.Bitmap ?? 0,
                binding?.Sellable ?? false, slot.Amount));
        }
        return rows;
    }

    /// <summary>PAL_BuyMenu_OnItemChange's owned count: the bag's copies plus every equipped
    /// copy across the party's six equipment slots.</summary>
    private int Owned(int objectId)
    {
        int count = inventory.GetAmount(objectId);
        foreach (int roleId in partyRoleIds)
            foreach (int equipped in roles.Roles[roleId].Equipment)
                if (equipped == objectId) count++;
        return count;
    }

    private void ClampSellCursor()
    {
        int count = SellRows().Count;
        if (SelectedSellItem >= count) SelectedSellItem = Math.Max(0, count - 1);
    }

    private BattleShopObjectBinding ShopObject(int objectId) =>
        shopObjects.TryGetValue(objectId, out var binding)
            ? binding
            : throw new InvalidDataException($"Shop object {objectId} has no authored binding.");

    private string Word(int id) => words.TryGetValue(id, out string? text) && text is not null
        ? text
        : throw new InvalidDataException($"Shop word {id} is missing from the selected language package.");
}
