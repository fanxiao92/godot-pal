using Godot;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalBattleContent : Resource
{
    [Export] public int FormatVersion { get; set; } = -1;
    [Export] public PalBattleRole[] Roles { get; set; } = [];
    [Export] public PalBattlePlayer[] Players { get; set; } = [];
    [Export] public PalBattleGrowth[] Growth { get; set; } = [];
    [Export] public PalBattleTeam[] Teams { get; set; } = [];
    [Export] public int[] RoleEquipment { get; set; } = [];
    [Export] public PalBattleEquipment[] Equipments { get; set; } = [];
    // #214 explicit exploration equip context: ordered initial bag rows (parallel item/amount
    // arrays) and equip-menu bindings (object, body part, eligible-role mask per binding).
    [Export] public int[] InventoryItems { get; set; } = [];
    [Export] public int[] InventoryAmounts { get; set; } = [];
    [Export] public int[] EquipMenuObjects { get; set; } = [];
    [Export] public int[] EquipMenuParts { get; set; } = [];
    [Export] public int[] EquipMenuRoleMasks { get; set; } = [];
    // #215 explicit exploration use context: use-menu bindings as parallel object/consuming/
    // apply-to-all/bitmap word rows (bool[] is not a Godot-exportable collection, so flags travel
    // as 0/1; #218 adds the BALL bitmap word item.wBitmap — 0 is the empty-chunk no-draw word).
    [Export] public int[] UseMenuObjects { get; set; } = [];
    [Export] public int[] UseMenuConsuming { get; set; } = [];
    [Export] public int[] UseMenuApplyToAll { get; set; } = [];
    [Export] public int[] UseMenuBitmaps { get; set; } = [];
    // #216 explicit exploration magic context: magic-menu bindings as parallel object/cost/
    // apply-to-all/usable-outside-battle word rows (flags travel as 0/1 like the use menu).
    [Export] public int[] MagicMenuObjects { get; set; } = [];
    [Export] public int[] MagicMenuCostMp { get; set; } = [];
    [Export] public int[] MagicMenuApplyToAll { get; set; } = [];
    [Export] public int[] MagicMenuUsableOutsideBattle { get; set; } = [];
    // #217 explicit exploration shop context: shop-object bindings as parallel object/price/
    // sellable/bitmap word rows (flags travel as 0/1 like the use menu), store rows as nested
    // row resources (each store's 1..9 item words keep store order), and the shared-wallet
    // cash seed — InitialCash travels as a signed word with -1 as the absent spelling, because
    // a real seed may legitimately be 0.
    [Export] public int[] ShopObjectIds { get; set; } = [];
    [Export] public int[] ShopObjectPrices { get; set; } = [];
    [Export] public int[] ShopObjectSellable { get; set; } = [];
    [Export] public int[] ShopObjectBitmaps { get; set; } = [];
    [Export] public PalBattleStore[] Stores { get; set; } = [];
    [Export] public long InitialCash { get; set; } = -1;

    // Optional projection provenance, not an authentication or a runtime JSON dependency.
    [Export] public bool ProjectionPresent { get; set; }
    [Export] public int ProjectionFormatVersion { get; set; } = -1;
    [Export] public string ProjectionKind { get; set; } = "";
    [Export] public string ProjectionSnapshotKind { get; set; } = "";
    [Export] public string ProjectionReceiptSha256 { get; set; } = "";
    [Export] public long ProjectionReceiptByteLength { get; set; } = -1;
    [Export] public string ProjectionNumericSha256 { get; set; } = "";
    [Export] public long ProjectionNumericByteLength { get; set; } = -1;
    [Export] public string[] ProjectionInputFiles { get; set; } = [];
    [Export] public string[] ProjectionInputSha256 { get; set; } = [];
    [Export] public long[] ProjectionInputByteLengths { get; set; } = [];
    [Export] public int[] ProjectionRoleIds { get; set; } = [];
    [Export] public int[] ProjectionTeamIds { get; set; } = [];
    // Seven words per source slot: teamId, sourceSlot, objectId, enemyId, compactSlot, battleId,
    // disposition (0 = skip-FFFF, 1 = loaded). The three absent loaded values use -1, never 0.
    [Export] public int[] ProjectionEnemySlots { get; set; } = [];
}
