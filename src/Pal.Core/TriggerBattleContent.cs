using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Pal.Core;

/// <summary>One authored equip-menu binding (battle-data.json $.equipMenu): the object, its body
/// part slot (global.h kBodyPartHead..kBodyPartWear) and the eligible-role mask — uigame.c's
/// kItemFlagEquipableByPlayerRole bits, one per role. An empty mask is the reference's
/// flagged-but-ineligible item: listed for nobody to equip.</summary>
public sealed record BattleEquipMenuBinding(int ObjectId, int Part, int EquipableRoleMask)
{
    public bool Eligible(int roleId) => roleId is >= 0 and <= 5 && (EquipableRoleMask & (1 << roleId)) != 0;
}

/// <summary>One authored use-menu binding (battle-data.json $.useMenu): the object, its
/// kItemFlagConsuming and kItemFlagApplyToAll words, and the item's BALL bitmap word
/// (rgObject item.wBitmap, the chunk itemmenu.c/uigame.c read from fpBALL for the
/// selected row; 0 is the reference's empty-chunk no-draw word). Listing an object
/// declares kItemFlagUsable for the exploration host (play.c PAL_GameUseItem).</summary>
public sealed record BattleUseMenuBinding(int ObjectId, bool Consuming, bool ApplyToAll, int Bitmap);

/// <summary>One authored magic-menu binding (battle-data.json $.magicMenu): the magic object,
/// its lprgMagic[wMagicNumber].wCostMP and the two kMagicFlag words the exploration menu
/// consumes — kMagicFlagUsableOutsideBattle (global.h 1&lt;&lt;0) and kMagicFlagApplyToAll (1&lt;&lt;4).
/// Listing an object declares its exploration context (uigame.c PAL_InGameMagicMenu).</summary>
public sealed record BattleMagicMenuBinding(int ObjectId, int CostMp, bool ApplyToAll, bool UsableOutsideBattle);

/// <summary>One authored shop-object binding (battle-data.json $.shopObjects): the object's
/// rgObject item words the shop consumes — item.wPrice (uigame.c PAL_BuyMenu's affordability
/// gate and PAL_SellMenu's price/2 sale price), the kItemFlagSellable bit (global.h 1&lt;&lt;5) and
/// the BALL bitmap word item.wBitmap (PAL_BuyMenu_OnItemChange/itemmenu.c draw the selected
/// row's chunk). An object listed here without the sellable bit is the reference's
/// owned-but-never-sellable row: listed in the sell menu, never confirmable.</summary>
public sealed record BattleShopObjectBinding(int ObjectId, int Price, bool Sellable, int Bitmap);

/// <summary>One authored store row (battle-data.json $.stores): the lprgStore WORD id and its
/// rgwItems list (global.h STORE; MAX_STORE_ITEM 9 per palcommon.h, the reference's trailing 0
/// terminator is not authored). Items keep store order and each requires a shop-object price
/// binding — script.c 0x0026 reads lprgStore[storeId].rgwItems, uigame.c PAL_BuyMenu prices
/// every row from rgObject.</summary>
public sealed record BattleShopStoreBinding(int StoreId, IReadOnlyList<int> Items);

/// <summary>Role-keyed numeric content; party order is Players and enemy identity is its team slot.
/// Subsequent battles use settled roles, never the initial player HP.</summary>
public sealed class TriggerBattleContent
{
    public IReadOnlyDictionary<int, BattleRoleSnapshot> InitialRoles { get; }
    public IReadOnlyList<ClassicBattlePlayer> Players { get; }
    public IReadOnlyDictionary<int, ClassicBattleGrowthData> GrowthByRole { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<ClassicBattleEnemy>> EnemyTeams { get; }
    /// <summary>Declared six-role equipment grid (empty = not declared); rows must match party role snapshots.</summary>
    public IReadOnlyList<IReadOnlyList<int>> RoleEquipment { get; }
    /// <summary>Declared equip-script closure objects (empty = none declared).</summary>
    public IReadOnlyDictionary<int, BattleEquipmentObject> EquipmentObjects { get; }
    /// <summary>Roles whose five base panel words are declared as the growth source: their settled
    /// panels flow into every subsequent battle's base declaration (equipment effects recompute
    /// on that base, global.c 1736-1886). Roles outside the set keep their per-battle player
    /// declaration, so legacy content without declared role panels is unchanged.</summary>
    public IReadOnlySet<int> PanelDeclaredRoles { get; }
    /// <summary>Explicit ordered initial bag rows (battle-data.json $.inventory) for the exploration
    /// host's shared inventory owner. Battles never consume this seed.</summary>
    public IReadOnlyList<InventorySlot> InitialInventory { get; }
    /// <summary>Authored equip-menu bindings; empty when the scenario declares no equipment page.</summary>
    public IReadOnlyList<BattleEquipMenuBinding> EquipMenu { get; }
    /// <summary>Authored use-menu bindings; empty when the scenario declares no items page.</summary>
    public IReadOnlyList<BattleUseMenuBinding> UseMenu { get; }
    /// <summary>Authored magic-menu bindings; empty when the scenario declares no magic page.</summary>
    public IReadOnlyList<BattleMagicMenuBinding> MagicMenu { get; }
    /// <summary>Authored shop-object bindings; empty when the scenario declares no shop context.</summary>
    public IReadOnlyDictionary<int, BattleShopObjectBinding> ShopObjects { get; }
    /// <summary>Authored store rows; empty when the scenario declares no shop context.</summary>
    public IReadOnlyDictionary<int, BattleShopStoreBinding> Stores { get; }
    /// <summary>Explicit shared-wallet seed (battle-data.json $.cash); null keeps the legacy
    /// no-cash-owner capability rejection (#131's checked immutable initial value).</summary>
    public uint? InitialCash { get; }

    [JsonIgnore] public BattleRoleSnapshot InitialRole => Single(InitialRoles.Values);
    [JsonIgnore] public ClassicBattlePlayer Player => Single(Players);
    [JsonIgnore] public ClassicBattleGrowthData Growth => Single(GrowthByRole.Values);
    [JsonIgnore] public IReadOnlyDictionary<int, ClassicBattleEnemy> Teams => new ReadOnlyDictionary<int, ClassicBattleEnemy>(
        EnemyTeams.ToDictionary(x => x.Key, x => Single(x.Value)));

    public TriggerBattleContent(BattleRoleSnapshot role, ClassicBattlePlayer player, ClassicBattleGrowthData growth,
        IReadOnlyDictionary<int, ClassicBattleEnemy> teams)
        : this([role], [player], new Dictionary<int, ClassicBattleGrowthData> { [role.RoleId] = growth },
            teams.ToDictionary(x => x.Key, x => (IReadOnlyList<ClassicBattleEnemy>)new[] { x.Value })) { }

    public TriggerBattleContent(IReadOnlyList<BattleRoleSnapshot> roles, IReadOnlyList<ClassicBattlePlayer> players,
        IReadOnlyDictionary<int, ClassicBattleGrowthData> growth,
        IReadOnlyDictionary<int, IReadOnlyList<ClassicBattleEnemy>> teams,
        IReadOnlyList<IReadOnlyList<int>>? roleEquipment = null,
        IReadOnlyList<BattleEquipmentObject>? equipmentObjects = null,
        IReadOnlyCollection<int>? panelDeclaredRoles = null,
        IReadOnlyList<InventorySlot>? initialInventory = null,
        IReadOnlyList<BattleEquipMenuBinding>? equipMenu = null,
        IReadOnlyList<BattleUseMenuBinding>? useMenu = null,
        IReadOnlyList<BattleMagicMenuBinding>? magicMenu = null,
        IReadOnlyList<BattleShopObjectBinding>? shopObjects = null,
        IReadOnlyList<BattleShopStoreBinding>? stores = null,
        uint? initialCash = null)
    {
        ArgumentNullException.ThrowIfNull(roles); ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(growth); ArgumentNullException.ThrowIfNull(teams);
        if (roles.Count is < 1 or > 3 || players.Count != roles.Count || roles.Any(x => x is null)
            || players.Any(x => x is null) || roles.Select(x => x.RoleId).Distinct().Count() != roles.Count
            || players.Select(x => x.RoleId).Distinct().Count() != players.Count
            || !roles.Select(x => x.RoleId).ToHashSet().SetEquals(players.Select(x => x.RoleId))
            || !growth.Keys.ToHashSet().SetEquals(players.Select(x => x.RoleId)) || growth.Values.Any(x => x is null))
            throw new InvalidDataException("roles[0], players[0] and growth[0] require unique matching RoleId keys and one to three roles.");
        var byId = roles.ToDictionary(x => x.RoleId);
        foreach (var (player, index) in players.Select((x, i) => (x, i)))
            if (byId[player.RoleId].Hp != player.Hp || byId[player.RoleId].MaxHp != player.MaxHp)
                throw new InvalidDataException($"roles and players[{index}] must match HP and maxHp by RoleId.");
        InitialRoles = new ReadOnlyDictionary<int, BattleRoleSnapshot>(byId.ToDictionary(x => x.Key, x => BattleRoleSnapshotHelper.Freeze(x.Value)));
        Players = Array.AsReadOnly(players.Select(x => x with { Equipment = BattleRoleSnapshotHelper.Copy(x.Equipment), Statuses = BattleRoleSnapshotHelper.Copy(x.Statuses), Poisons = BattleRoleSnapshotHelper.Copy(x.Poisons) }).ToArray());
        GrowthByRole = new ReadOnlyDictionary<int, ClassicBattleGrowthData>(growth.ToDictionary(x => x.Key,
            x => new ClassicBattleGrowthData(BattleRoleSnapshotHelper.Copy(x.Value.LevelUpExperience), Array.AsReadOnly(x.Value.LevelUpMagics.ToArray()))));
        EnemyTeams = new ReadOnlyDictionary<int, IReadOnlyList<ClassicBattleEnemy>>(teams.ToDictionary(x => x.Key, x =>
        {
            if (x.Value is null || x.Value.Count is < 1 or > 5 || x.Value.Any(e => e is null))
                throw new InvalidDataException($"Battle team {x.Key} requires one to five nonnull enemy slots.");
            return (IReadOnlyList<ClassicBattleEnemy>)Array.AsReadOnly(x.Value.Select(e => e with { Statuses = BattleRoleSnapshotHelper.Copy(e.Statuses), Poisons = BattleRoleSnapshotHelper.Copy(e.Poisons) }).ToArray());
        }));
        BattleEquipmentState.ValidateEquipmentClosure(roleEquipment, equipmentObjects, roles);
        if (panelDeclaredRoles is not null && panelDeclaredRoles.Any(role => !byId.ContainsKey(role)))
            throw new InvalidDataException("Declared panel roles must name a declared battle role.");
        PanelDeclaredRoles = new HashSet<int>(panelDeclaredRoles ?? []);
        RoleEquipment = roleEquipment is null || roleEquipment.Count == 0 ? Array.Empty<IReadOnlyList<int>>()
            : Array.AsReadOnly(roleEquipment.Select(row => (IReadOnlyList<int>)Array.AsReadOnly(row.ToArray())).ToArray());
        EquipmentObjects = new ReadOnlyDictionary<int, BattleEquipmentObject>((equipmentObjects ?? []).ToDictionary(
            x => x.ObjectId, x => x with { Effects = Array.AsReadOnly(x.Effects.Select(e => e with { Statuses = BattleRoleSnapshotHelper.Copy(e.Statuses) }).ToArray()) }));
        InitialInventory = CopyInventory(initialInventory);
        EquipMenu = CopyEquipMenu(equipMenu, EquipmentObjects);
        UseMenu = CopyUseMenu(useMenu);
        MagicMenu = CopyMagicMenu(magicMenu);
        var shop = CopyShopObjects(shopObjects);
        ShopObjects = shop;
        Stores = CopyStores(stores, shop);
        InitialCash = initialCash;
        // rgObject item.wBitmap is one word per object: a use-menu and a shop-object binding of
        // the same object must agree, or the items page and the shop page would draw different
        // chunks for the same object (itemmenu.c/uigame.c read the single wBitmap word).
        foreach (var useBinding in UseMenu)
            if (ShopObjects.TryGetValue(useBinding.ObjectId, out var shopBinding) && shopBinding.Bitmap != useBinding.Bitmap)
                throw new InvalidDataException($"$.shopObjects: object {useBinding.ObjectId} bitmap {shopBinding.Bitmap} disagrees with its use-menu bitmap {useBinding.Bitmap}.");
    }

    private static IReadOnlyList<InventorySlot> CopyInventory(IReadOnlyList<InventorySlot>? rows)
    {
        if (rows is null || rows.Count == 0) return Array.Empty<InventorySlot>();
        var seen = new HashSet<int>();
        foreach (var (slot, index) in rows.Select((x, i) => (x, i)))
        {
            if (slot is null || slot.ItemId is < 1 or > 65535 || !seen.Add(slot.ItemId))
                throw new InvalidDataException($"$.inventory[{index}]: initial bag rows require unique item WORD ids 1..65535.");
            if (slot.Amount is < 0 or > 99)
                throw new InvalidDataException($"$.inventory[{index}].amount: initial bag amounts must be 0..99.");
        }
        return Array.AsReadOnly(rows.Select(slot => slot with { }).ToArray());
    }

    private static IReadOnlyList<BattleUseMenuBinding> CopyUseMenu(IReadOnlyList<BattleUseMenuBinding>? bindings)
    {
        if (bindings is null || bindings.Count == 0) return Array.Empty<BattleUseMenuBinding>();
        var seen = new HashSet<int>();
        foreach (var (binding, index) in bindings.Select((x, i) => (x, i)))
            if (binding is null || binding.ObjectId is < 1 or > 65535 || !seen.Add(binding.ObjectId))
                throw new InvalidDataException($"$.useMenu[{index}]: bindings require unique object WORD ids 1..65535.");
            else if (binding.Bitmap is < 0 or > 65535)
                throw new InvalidDataException($"$.useMenu[{index}].bitmap: the BALL bitmap word must be 0..65535.");
        return Array.AsReadOnly(bindings.ToArray());
    }

    private static IReadOnlyList<BattleMagicMenuBinding> CopyMagicMenu(IReadOnlyList<BattleMagicMenuBinding>? bindings)
    {
        if (bindings is null || bindings.Count == 0) return Array.Empty<BattleMagicMenuBinding>();
        var seen = new HashSet<int>();
        foreach (var (binding, index) in bindings.Select((x, i) => (x, i)))
            if (binding is null || binding.ObjectId is < 1 or > 65535 || !seen.Add(binding.ObjectId))
                throw new InvalidDataException($"$.magicMenu[{index}]: bindings require unique object WORD ids 1..65535.");
            else if (binding.CostMp is < 0 or > 65535)
                throw new InvalidDataException($"$.magicMenu[{index}].costMp: bindings require a cost WORD 0..65535.");
        return Array.AsReadOnly(bindings.ToArray());
    }

    private static IReadOnlyDictionary<int, BattleShopObjectBinding> CopyShopObjects(IReadOnlyList<BattleShopObjectBinding>? bindings)
    {
        if (bindings is null || bindings.Count == 0) return new Dictionary<int, BattleShopObjectBinding>();
        var objects = new Dictionary<int, BattleShopObjectBinding>();
        foreach (var (binding, index) in bindings.Select((x, i) => (x, i)))
        {
            if (binding is null || binding.ObjectId is < 1 or > 65535 || !objects.TryAdd(binding.ObjectId, binding))
                throw new InvalidDataException($"$.shopObjects[{index}]: bindings require unique object WORD ids 1..65535.");
            if (binding.Price is < 0 or > 65535)
                throw new InvalidDataException($"$.shopObjects[{index}].price: the item price must be a WORD 0..65535.");
            if (binding.Bitmap is < 0 or > 65535)
                throw new InvalidDataException($"$.shopObjects[{index}].bitmap: the BALL bitmap word must be 0..65535.");
        }
        return objects;
    }

    private static IReadOnlyDictionary<int, BattleShopStoreBinding> CopyStores(IReadOnlyList<BattleShopStoreBinding>? stores,
        IReadOnlyDictionary<int, BattleShopObjectBinding> shopObjects)
    {
        var rows = new Dictionary<int, BattleShopStoreBinding>();
        if (stores is null || stores.Count == 0) return rows;
        foreach (var (store, index) in stores.Select((x, i) => (x, i)))
        {
            if (store is null || store.StoreId is < 1 or > 65535 || !rows.TryAdd(store.StoreId, store))
                throw new InvalidDataException($"$.stores[{index}]: store rows require unique WORD ids 1..65535.");
            if (store.Items is null || store.Items.Count is < 1 or > 9)
                throw new InvalidDataException($"$.stores[{index}].items: a store row lists 1..9 item WORDs (MAX_STORE_ITEM).");
            foreach (var (item, itemIndex) in store.Items.Select((x, i) => (x, i)))
            {
                if (item is < 1 or > 65535)
                    throw new InvalidDataException($"$.stores[{index}].items[{itemIndex}]: store items are object WORD ids 1..65535.");
                if (!shopObjects.ContainsKey(item))
                    throw new InvalidDataException($"$.stores[{index}].items[{itemIndex}]: store item {item} has no authored shop object price.");
            }
        }
        return rows;
    }

    private static IReadOnlyList<BattleEquipMenuBinding> CopyEquipMenu(IReadOnlyList<BattleEquipMenuBinding>? bindings,
        IReadOnlyDictionary<int, BattleEquipmentObject> equipmentObjects)
    {
        if (bindings is null || bindings.Count == 0) return Array.Empty<BattleEquipMenuBinding>();
        var seen = new HashSet<int>();
        foreach (var (binding, index) in bindings.Select((x, i) => (x, i)))
        {
            if (binding is null || binding.ObjectId is < 1 or > 65535 || !seen.Add(binding.ObjectId))
                throw new InvalidDataException($"$.equipMenu[{index}]: bindings require unique object WORD ids 1..65535.");
            if (binding.Part is < 0 or >= BattleEquipmentState.SlotCount)
                throw new InvalidDataException($"$.equipMenu[{index}].part: bindings require a body part 0..{BattleEquipmentState.SlotCount - 1}.");
            if (binding.EquipableRoleMask is < 0 or > 63)
                throw new InvalidDataException($"$.equipMenu[{index}].equipableBy: bindings require a role mask 0..63.");
            if (!equipmentObjects.ContainsKey(binding.ObjectId))
                throw new InvalidDataException($"$.equipMenu[{index}].objectId: equipment object {binding.ObjectId} is not declared.");
        }
        return Array.AsReadOnly(bindings.ToArray());
    }

    internal IReadOnlyList<ClassicBattleEnemy> Enemies(int enemyTeamId) => EnemyTeams.TryGetValue(enemyTeamId, out var enemies) ? enemies
        : throw new InvalidDataException($"Missing battle team {enemyTeamId}.");

    internal IReadOnlyList<ClassicBattlePlayer> SettledPlayers(IReadOnlyDictionary<int, BattleRoleSnapshot> roles,
        bool useCurrentEquipment = false) =>
        Players.Select(x =>
        {
            BattleRoleSnapshot settled = roles[x.RoleId];
            var player = PanelDeclaredRoles.Contains(x.RoleId)
                ? x with { Hp = settled.Hp, MaxHp = settled.MaxHp, Attack = settled.Attack, Defense = settled.Defense,
                    Dexterity = settled.Dexterity, MagicPower = settled.MagicPower, FleeRate = settled.FleeRate }
                : x with { Hp = settled.Hp, MaxHp = settled.MaxHp };
            // Only explicit current-equipment bindings may replace frozen gear. Keep the player
            // content's status gate; live role statuses and the current overlay are validated and
            // projected separately by the existing lifecycle/combat constructors.
            return useCurrentEquipment ? player with { Equipment = settled.Equipment } : player;
        }).ToArray();

    internal void Validate(int enemyTeamId, IReadOnlyDictionary<int, BattleRoleSnapshot> roles,
        BattleEquipmentState? equipmentState = null)
    {
        if (roles.Values.Any(x => x.MaxHp > 32767))
            throw new InvalidDataException("Trigger battle content requires maxHP <= 32767 for subsequent battles.");
        ClassicBattleLifecycleSession.Validate(roles.Values.ToArray(),
            SettledPlayers(roles, useCurrentEquipment: equipmentState is not null), Enemies(enemyTeamId), GrowthByRole,
            equipmentState ?? BattleEquipmentState.Initialize(this));
    }

    private static T Single<T>(IEnumerable<T> source) => source.Count() == 1 ? source.Single()
        : throw new InvalidOperationException("Single-actor compatibility access cannot project multiplayer content.");
}
