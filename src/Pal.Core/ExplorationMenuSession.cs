namespace Pal.Core;

public enum ExplorationMenuMode { Closed, MainMenu, Status, EquipItems, EquipRoles, Items, ItemTargets, ItemScript,
    MagicCasters, MagicList, MagicTargets, MagicOnUse, MagicOnSuccess, SystemMenu }

/// <summary>Menu verbs as the fixed reference maps them (input.c: direction keys navigate,
/// Search=confirm, Menu=cancel). The host translates its physical keys into these verbs.</summary>
public enum ExplorationMenuKey { Up, Down, Left, Right, Confirm, Cancel }

/// <summary>Deliverable menu pages; #213 binds the read-only status page, #214 the equipment
/// page and #215 the items page on the same session. Later operative pages arrive with their
/// own M6-B leaves; #222 declares the system layer (uigame.c PAL_SystemMenu) as the formal
/// entry into M6-C save mode — the slot-selection UI itself belongs to the later C leaf.</summary>
public enum ExplorationMenuPage { Status, Equipment, Items, Magic, System }

/// <summary>One main-menu entry: the page Confirm opens and its label WORD.</summary>
public sealed record ExplorationMenuEntry(ExplorationMenuPage Page, int WordId);

/// <summary>One party member binding: the shared live role, its name WORD, and the authored
/// player panel words used when the role is not panel-declared (TriggerBattleContent.SettledPlayers
/// keeps authored player panels for undeclared roles; panel-declared roles read live role panels).</summary>
public sealed record ExplorationMenuMember(int RoleId, int NameWord, bool PanelDeclared,
    int AuthoredAttack, int AuthoredMagicPower, int AuthoredDefense, int AuthoredDexterity, int AuthoredFleeRate);

/// <summary>One nonempty equipment slot with its resolved object-name word text.</summary>
public sealed record ExplorationMenuEquipment(int ObjectId, string Name);

/// <summary>One equip-list row (itemmenu.c PAL_ItemSelectMenu over equipable stock): the object,
/// its body-part slot, the owned amount and the resolved item/part words.</summary>
public sealed record ExplorationMenuEquipRow(int ObjectId, int Part, int Amount, string Name, string PartLabel);

/// <summary>One role row of the equip selection page (uigame.c PAL_EquipItemMenu): the role name,
/// the item's eligibility bit for that role and the role's currently equipped object names.</summary>
public sealed record ExplorationMenuEquipRole(string Name, bool Eligible, IReadOnlyList<string> Gear);

/// <summary>The equip role-selection page for one item: object, part slot, words and the party rows.</summary>
public sealed record ExplorationMenuEquipSelection(int ObjectId, int Part, string Name, string PartLabel,
    IReadOnlyList<ExplorationMenuEquipRole> Roles);

/// <summary>One items-page row (itemmenu.c PAL_ItemSelectMenu over usable stock): the object, its
/// name word text, the consuming/apply-to-all words, the item's BALL bitmap word
/// (item.wBitmap; 0 is the reference's empty-chunk no-draw word — the selected row's
/// box draws no picture) and the listed amount. Stock rows carry the
/// available amount (nAmount − nAmountInUse); equipped appendix rows (amount 0 / in-use −1 in the
/// reference) carry zero and never count equipment copies as usable stock.</summary>
public sealed record ExplorationMenuUseRow(int ObjectId, string Name, bool Consuming, bool ApplyToAll,
    int Bitmap, bool Equipped, int Amount);

/// <summary>One party row of the item target page (uigame.c PAL_ItemUseMenu): the shared live
/// vitals the reference draws while the player picks whom to use the item on.</summary>
public sealed record ExplorationMenuItemTarget(int RoleId, string Name, int Hp, int MaxHp, int Mp, int MaxMp);

/// <summary>The item target-selection page for one item: the object, the item's BALL bitmap
/// word (item.wBitmap; uigame.c PAL_ItemUseMenu draws the item picture beside the party
/// rows while picking), the party rows and the resolved HP/MP label words the reference's
/// stat box draws.</summary>
public sealed record ExplorationMenuItemTargetView(int ObjectId, string Name, int Bitmap,
    IReadOnlyList<ExplorationMenuItemTarget> Targets, string HpLabel, string MpLabel);

/// <summary>play.c PAL_GameUseItem's one committed script run: the object plus the caller word
/// PAL_RunTriggerScript receives — the selected member's role id, or the apply-to-all 0xFFFF
/// which keeps the previously saved caller. The host runs the item's on-use script entry and
/// receipts exactly this request.</summary>
public sealed record ExplorationMenuItemUseRequest(Guid Id, int ObjectId, bool ApplyToAll, int TargetRoleId);

/// <summary>One caster-select row (uigame.c PAL_InGameMagicMenu's PAL_ReadMenu items over the
/// party): the shared live member and its alive flag — dead members (rgwHP == 0) stay listed
/// but disabled, so Search never confirms them.</summary>
public sealed record ExplorationMenuMagicCaster(int RoleId, string Name, bool Alive);

/// <summary>One magic-list row (magicmenu.c PAL_MagicSelectionMenuInit): the learned magic
/// object, its name word text, its lprgMagic cost and the enabled flag — enabled only when the
/// magic is usable outside battle and its cost fits the caster's current live MP. Disabled
/// rows stay listed and reachable by the cursor; Search never confirms them.</summary>
public sealed record ExplorationMenuMagicRow(int ObjectId, string Name, int CostMp, bool Enabled);

/// <summary>One member row of the magic target page (uigame.c PAL_InGameMagicMenu's player
/// loop): the shared live vitals the reference draws while picking whom to cast on.</summary>
public sealed record ExplorationMenuMagicTarget(int RoleId, string Name, int Hp, int MaxHp, int Mp, int MaxMp);

/// <summary>The magic target-selection page for one single-target magic: the object, the party
/// rows and the resolved HP/MP label words the reference's stat box draws.</summary>
public sealed record ExplorationMenuMagicTargetView(int ObjectId, string Name,
    IReadOnlyList<ExplorationMenuMagicTarget> Targets, string HpLabel, string MpLabel);

/// <summary>uigame.c PAL_InGameMagicMenu's committed cast: the object plus the caller word each
/// PAL_RunTriggerScript receives — the selected target's role id for single-target magics, or
/// the literal 0 for apply-to-all magics (unlike the item flow's 0xFFFF keep-caller word). The
/// host runs the magic's on-use and on-success script entries in order and receipts this cast
/// twice; the caster's MP moves only after both runs survived (never battle's deduct-first).</summary>
public sealed record ExplorationMenuMagicCast(Guid Id, int ObjectId, bool ApplyToAll, int CasterRoleId, int TargetRoleId);

/// <summary>One live poison slot with its resolved object-name word text.</summary>
public sealed record ExplorationMenuPoison(int PoisonId, string Name);

/// <summary>Read-only status-page projection of the shared live owners for one party member.</summary>
public sealed record ExplorationMenuStatusView(
    int PartyIndex, int RoleId, string Name, bool IsDead,
    int Level, int Hp, int MaxHp, int Mp, int MaxMp, int Experience, int NextLevelExperience,
    int Attack, int MagicPower, int Defense, int Dexterity, int FleeRate,
    IReadOnlyList<int> Statuses, IReadOnlyList<ExplorationMenuEquipment> Equipment,
    IReadOnlyList<ExplorationMenuPoison> Poisons,
    string LevelLabel, string HpLabel, string MpLabel, string ExperienceLabel,
    string AttackLabel, string MagicPowerLabel, string DefenseLabel, string DexterityLabel, string FleeRateLabel);

/// <summary>One committed save request from the system layer (#224; uigame.c PAL_SaveSlotMenu's
/// confirmed save run): the request identity and the confirmed manual slot. Exactly one exists
/// per confirmed operation; while it exists the layer is parked and no key — a duplicate
/// confirm included — can issue a second (#148 Story 12).</summary>
public sealed record ExplorationMenuSaveRequest(Guid Id, int Slot);

/// <summary>Exploration menu state machine per the fixed reference (uigame.c PAL_InGameMenu /
/// PAL_PlayerStatus, ui.c PAL_ReadMenu). Pure projection of the shared live state owners; the
/// session never mutates roles, equipment registers, poisons or the language package.</summary>
public sealed class ExplorationMenuSession
{
    // Modern menu word contract in the prepared language package, following the existing
    // modern demonstration ids (battle words 60000..60010). Labels reuse 60004/60006.
    public const int StatusEntryWord = 60011;
    public const int LevelWord = 60012, MpWord = 60013, AttackPowerWord = 60014, MagicPowerWord = 60015;
    public const int DefenseWord = 60016, DexterityWord = 60017, FleeRateWord = 60018;
    public const int HpWord = 60004, ExperienceWord = 60006;
    /// <summary>Role display names: the language word 60100+RoleId (modern convention; the
    /// reference reads PLAYERROLES rgwName words, which the authored chain carries here).</summary>
    public const int RoleNameWordBase = 60100;
    /// <summary>Equipment main-menu entry word (#214).</summary>
    public const int EquipmentEntryWord = 60019;
    /// <summary>Body-part label words by slot (reference ui.h EQUIP_LABEL_* 600..605; slot order is
    /// global.h kBodyPartHead,Body,Shoulder,Hand,Feet,Wear — the UI label list reorders shoulder).</summary>
    public const int PartWordBase = 60020;
    /// <summary>Items main-menu entry word (#215).</summary>
    public const int ItemsEntryWord = 60026;
    /// <summary>Magic main-menu entry word (#216; reference GAMEMENU_LABEL_MAGIC).</summary>
    public const int MagicEntryWord = 60027;
    /// <summary>System main-menu entry word (#222/#224; reference GAMEMENU_LABEL_SYSTEM). Confirm on
    /// this entry opens the system-menu layer (uigame.c PAL_SystemMenu → PAL_SaveSlotMenu), the
    /// formal M6-C entry into the five-slot save flow. Word 60032: 60028/60029 stay the shop
    /// confirm pair and 60030/60031 the shop price words (#217), so the system label takes the
    /// next free id; hosts author the word in their prepared language package before adding
    /// the entry, exactly like the equipment/items/magic entries before it.</summary>
    public const int SystemEntryWord = 60032;

    private readonly RoleLiveState roles;
    private readonly BattleEquipmentState equipment;
    private readonly IReadOnlyDictionary<int, ClassicBattleGrowthData> growth;
    private readonly IReadOnlyDictionary<int, string> words;
    private readonly PartyPoisonState? poisons;
    private readonly IReadOnlyList<BattleEquipMenuBinding>? equipMenu;
    private readonly IReadOnlyDictionary<int, BattleEquipmentObject>? equipmentObjects;
    private InventoryLiveState? inventory;
    private readonly IReadOnlyList<BattleUseMenuBinding>? useMenu;
    private readonly IReadOnlyList<BattleMagicMenuBinding>? magicMenu;

    public ExplorationMenuSession(IReadOnlyList<ExplorationMenuEntry> entries, IReadOnlyList<ExplorationMenuMember> party,
        RoleLiveState roles, BattleEquipmentState equipment, IReadOnlyDictionary<int, ClassicBattleGrowthData> growth,
        IReadOnlyDictionary<int, string> words, PartyPoisonState? poisons = null,
        IReadOnlyList<BattleEquipMenuBinding>? equipMenu = null,
        IReadOnlyDictionary<int, BattleEquipmentObject>? equipmentObjects = null,
        InventoryLiveState? inventory = null,
        IReadOnlyList<BattleUseMenuBinding>? useMenu = null,
        IReadOnlyList<BattleMagicMenuBinding>? magicMenu = null)
    {
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(equipment);
        ArgumentNullException.ThrowIfNull(growth);
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(party);
        this.roles = roles;
        this.equipment = equipment;
        this.growth = growth;
        this.words = words;
        this.poisons = poisons;
        if (entries.Count == 0) throw new InvalidDataException("The exploration menu requires at least one entry.");
        foreach (var entry in entries)
        {
            if (entry is null || !IsWord(entry.WordId))
                throw new InvalidDataException("Menu entries require label WORD ids in 0..65535.");
            Word(entry.WordId); // resolve now: a missing label word rejects before any menu state
        }
        if (party.Count is < 1 or > 3)
            throw new InvalidDataException("The status page follows one to three party members.");
        if (party.Select(member => member.RoleId).Distinct().Count() != party.Count)
            throw new InvalidDataException("Party members require distinct role ids.");
        foreach (var member in party)
        {
            if (member is null || !IsWord(member.NameWord))
                throw new InvalidDataException("Party members require name WORD ids in 0..65535.");
            if (member.RoleId is < 0 or > 5 || !roles.Roles.ContainsKey(member.RoleId))
                throw new InvalidDataException($"Party member {member.RoleId} has no shared live role record.");
            if (!growth.TryGetValue(member.RoleId, out var table) || table.LevelUpExperience.Count == 0)
                throw new InvalidDataException($"Party member {member.RoleId} has no level-up experience table.");
            if (new[] { member.AuthoredAttack, member.AuthoredMagicPower, member.AuthoredDefense,
                    member.AuthoredDexterity, member.AuthoredFleeRate }.Any(value => !IsWord(value)))
                throw new InvalidDataException("Authored player panel words must stay in 0..65535.");
            int level = roles.Roles[member.RoleId].Level;
            if (level < 0 || level >= table.LevelUpExperience.Count)
                throw new InvalidDataException($"Role {member.RoleId} level {level} is at or beyond its level-up table.");
            Word(member.NameWord); // resolve now: a missing name word rejects before any menu state
            if (poisons is not null && !poisons.PartyRoleIds.Contains(member.RoleId))
                throw new InvalidDataException($"Party member {member.RoleId} is not in the bound poison owner.");
        }
        Entries = Array.AsReadOnly(entries.ToArray());
        Party = Array.AsReadOnly(party.ToArray());
        if (entries.Any(entry => entry.Page == ExplorationMenuPage.Equipment))
        {
            // The equipment page requires the whole shared equip context up front: bindings over
            // declared objects, the live bag and the poison owner its equip operation composes.
            if (equipMenu is null || equipMenu.Count == 0)
                throw new InvalidDataException("The equipment page requires authored equip-menu bindings.");
            if (equipmentObjects is null || equipmentObjects.Count == 0)
                throw new InvalidDataException("The equipment page requires the declared equipment objects.");
            if (inventory is null)
                throw new InvalidDataException("The equipment page requires the shared live inventory owner.");
            if (poisons is null)
                throw new InvalidDataException("The equipment page requires the shared poison owner.");
            var bound = new HashSet<int>();
            foreach (var binding in equipMenu)
            {
                if (binding is null || binding.ObjectId is < 1 or > 65535 || !bound.Add(binding.ObjectId))
                    throw new InvalidDataException("Equip-menu bindings require unique object WORD ids 1..65535.");
                if (binding.Part is < 0 or > 5)
                    throw new InvalidDataException($"Equip-menu binding for object {binding.ObjectId} requires a body part 0..5.");
                if (binding.EquipableRoleMask is < 0 or > 63)
                    throw new InvalidDataException($"Equip-menu binding for object {binding.ObjectId} requires a role mask 0..63.");
                if (!equipmentObjects.ContainsKey(binding.ObjectId))
                    throw new InvalidDataException($"Equip-menu object {binding.ObjectId} is not declared.");
                // Resolve now: a missing item or part word rejects before any menu state exists.
                Word(binding.ObjectId);
                Word(PartWordBase + binding.Part);
            }
            this.equipMenu = Array.AsReadOnly(equipMenu.ToArray());
            this.equipmentObjects = equipmentObjects;
            this.inventory = inventory;
        }
        if (entries.Any(entry => entry.Page == ExplorationMenuPage.Items))
        {
            // The items page needs the authored usable context and the shared live bag up front;
            // every listed object's name word resolves now so a missing word rejects before any
            // menu state exists (play.c PAL_GameUseItem over kItemFlagUsable).
            if (useMenu is null || useMenu.Count == 0)
                throw new InvalidDataException("The items page requires authored use-menu bindings.");
            if (inventory is null)
                throw new InvalidDataException("The items page requires the shared live inventory owner.");
            var bound = new HashSet<int>();
            foreach (var binding in useMenu)
            {
                if (binding is null || binding.ObjectId is < 1 or > 65535 || !bound.Add(binding.ObjectId))
                    throw new InvalidDataException("Use-menu bindings require unique object WORD ids 1..65535.");
                Word(binding.ObjectId);
            }
            this.useMenu = Array.AsReadOnly(useMenu.ToArray());
            this.inventory = inventory;
        }
        if (entries.Any(entry => entry.Page == ExplorationMenuPage.Magic))
        {
            // The magic page needs the authored magic context up front: one binding per magic
            // object with its cost and kMagicFlag words (uigame.c PAL_InGameMagicMenu over
            // magicmenu.c PAL_MagicSelectionMenuInit). Every listed object's name word resolves
            // now so a missing word rejects before any menu state exists.
            if (magicMenu is null || magicMenu.Count == 0)
                throw new InvalidDataException("The magic page requires authored magic-menu bindings.");
            var bound = new HashSet<int>();
            foreach (var binding in magicMenu)
            {
                if (binding is null || binding.ObjectId is < 1 or > 65535 || !bound.Add(binding.ObjectId))
                    throw new InvalidDataException("Magic-menu bindings require unique object WORD ids 1..65535.");
                if (binding.CostMp is < 0 or > 65535)
                    throw new InvalidDataException($"Magic-menu binding for object {binding.ObjectId} requires a cost WORD 0..65535.");
                Word(binding.ObjectId);
            }
            this.magicMenu = Array.AsReadOnly(magicMenu.ToArray());
        }
    }

    public ExplorationMenuMode Mode { get; private set; }
    /// <summary>The remembered main-menu cursor (gpGlobals->iCurMainMenuItem); it survives closes.</summary>
    public int SelectedEntry { get; private set; }
    public int StatusMemberIndex { get; private set; }
    /// <summary>The remembered equip item-list cursor (gpGlobals->iCurInvMenuItem); it survives closes
    /// and clamps to the current stock list, which shrinks as equips consume the last copies.</summary>
    public int SelectedEquipItem { get; private set; }
    /// <summary>The object selected in the equip role page (uigame.c wItem/wLastUnequippedItem).</summary>
    public int EquipSelection { get; private set; }
    /// <summary>The equip role-selection cursor (uigame.c iCurrentPlayer); it starts at member 0 on entry.</summary>
    public int SelectedEquipRole { get; private set; }
    /// <summary>The object selected in the items flow (play.c PAL_GameUseItem's wObject).</summary>
    public int ItemSelection { get; private set; }
    /// <summary>The item target cursor (uigame.c PAL_ItemUseMenu's static sSelectedPlayer); it is
    /// remembered across uses and closes, and clamps to the party on entry.</summary>
    public int SelectedUseRole { get; private set; }
    /// <summary>The committed item script run awaiting the host's receipt (play.c PAL_GameUseItem's
    /// PAL_RunTriggerScript call); non-null only in ItemScript mode.</summary>
    public ExplorationMenuItemUseRequest? PendingItemUse { get; private set; }
    /// <summary>The remembered magic caster cursor (uigame.c PAL_InGameMagicMenu's static w);
    /// it survives closes and reopens. A single-member party never uses the select (w stays 0).</summary>
    public int SelectedMagicCaster { get; private set; }
    /// <summary>The remembered magic cursor (uigame.c's wMagic): the last selected magic object
    /// WORD, reset to 0 on every entry into the magic menu and kept across casts within it.</summary>
    public int SelectedMagic { get; private set; }
    /// <summary>The magic target cursor (uigame.c's local wPlayer): a fresh 0 for each newly
    /// selected single-target magic, kept across that magic's repeat casts.</summary>
    public int SelectedMagicTarget { get; private set; }
    /// <summary>The committed on-use script run awaiting the host's receipt; non-null only in
    /// MagicOnUse mode.</summary>
    public ExplorationMenuMagicCast? PendingMagicUse { get; private set; }
    /// <summary>The committed on-success script run awaiting the host's receipt; non-null only
    /// in MagicOnSuccess mode (the same cast the on-use receipt advanced).</summary>
    public ExplorationMenuMagicCast? PendingMagicSuccess { get; private set; }
    /// <summary>The committed save request awaiting the host's receipt (#224); non-null only while
    /// the system layer is parked on one confirmed slot. The host completes the whole save chain
    /// inside that committed production update boundary; the session itself never touches
    /// storage, so both receipt outcomes look identical here (#148 Decision 27).</summary>
    public ExplorationMenuSaveRequest? PendingSave { get; private set; }
    /// <summary>The remembered save-slot cursor (uigame.c PAL_SaveSlotMenu's static selection):
    /// one of the five manual slots {<see cref="PlayerSaveContract.FirstSlot"/>}..
    /// {<see cref="PlayerSaveContract.LastSlot"/>}, starts at 1 and survives closes; browsing only
    /// moves it — never a file, never a switch (#148 Stories 3/4).</summary>
    public int SelectedSaveSlot { get; private set; } = PlayerSaveContract.FirstSlot;
    public IReadOnlyList<ExplorationMenuEntry> Entries { get; }
    public IReadOnlyList<ExplorationMenuMember> Party { get; }

    public string EntryLabel(int index)
    {
        if (index is < 0 || index >= Entries.Count) throw new ArgumentOutOfRangeException(nameof(index));
        return Word(Entries[index].WordId);
    }

    /// <summary>uigame.c: kKeyMenu during exploration opens the in-game main menu at the remembered cursor.</summary>
    public void Open()
    {
        if (Mode != ExplorationMenuMode.Closed) throw new InvalidOperationException("The menu is already open.");
        Mode = ExplorationMenuMode.MainMenu;
    }

    public void Press(ExplorationMenuKey key)
    {
        if (Mode == ExplorationMenuMode.Closed) throw new InvalidOperationException("The menu is closed.");
        if (Mode == ExplorationMenuMode.ItemScript)
            throw new InvalidOperationException("The pending item script owns input.");
        if (Mode is ExplorationMenuMode.MagicOnUse or ExplorationMenuMode.MagicOnSuccess)
            throw new InvalidOperationException("The pending magic script owns input.");
        if (Mode == ExplorationMenuMode.SystemMenu && PendingSave is not null)
            throw new InvalidOperationException("The pending save owns input.");
        switch (Mode)
        {
            case ExplorationMenuMode.MainMenu:
                switch (key)
                {
                    // ui.c PAL_ReadMenu: Up/Left and Down/Right move with wrap; Search confirms;
                    // kKeyMenu cancels the whole menu. Moves update the remembered cursor.
                    case ExplorationMenuKey.Up or ExplorationMenuKey.Left:
                        SelectedEntry = (SelectedEntry + Entries.Count - 1) % Entries.Count;
                        break;
                    case ExplorationMenuKey.Down or ExplorationMenuKey.Right:
                        SelectedEntry = (SelectedEntry + 1) % Entries.Count;
                        break;
                    case ExplorationMenuKey.Confirm:
                        if (Entries[SelectedEntry].Page == ExplorationMenuPage.Status)
                        {
                            Mode = ExplorationMenuMode.Status;
                            StatusMemberIndex = 0; // PAL_PlayerStatus starts at party member 0
                        }
                        else if (Entries[SelectedEntry].Page == ExplorationMenuPage.Equipment)
                        {
                            // play.c PAL_GameEquipItem opens the equipable item select at the
                            // remembered inventory cursor (gpGlobals->iCurInvMenuItem).
                            Mode = ExplorationMenuMode.EquipItems;
                            ClampEquipCursor();
                        }
                        else if (Entries[SelectedEntry].Page == ExplorationMenuPage.Items)
                        {
                            // play.c PAL_GameUseItem opens PAL_ItemSelectMenu(kItemFlagUsable) at
                            // the same remembered inventory cursor (gpGlobals->iCurInvMenuItem).
                            Mode = ExplorationMenuMode.Items;
                            ClampUseCursor();
                        }
                        else if (Entries[SelectedEntry].Page == ExplorationMenuPage.Magic)
                        {
                            // uigame.c PAL_InGameMagicMenu: wMagic resets on every entry; a
                            // single-member party skips the caster select entirely (w = 0,
                            // 'goto start_magicmenu') — even a dead sole member, mirroring the
                            // reference, which checks cost and flags but never HP here.
                            SelectedMagic = 0;
                            if (Party.Count == 1) EnterMagicList();
                            else Mode = ExplorationMenuMode.MagicCasters;
                        }
                        else if (Entries[SelectedEntry].Page == ExplorationMenuPage.System)
                        {
                            // uigame.c PAL_InGameMenu's system item opens PAL_SystemMenu; the
                            // world stays frozen while the system layer owns input. #222 declares
                            // this the formal M6-C entry into save mode; the slot-selection and
                            // confirmation UI inside the system layer belongs to the later C leaf.
                            Mode = ExplorationMenuMode.SystemMenu;
                        }
                        break;
                    case ExplorationMenuKey.Cancel:
                        Mode = ExplorationMenuMode.Closed;
                        break;
                }
                break;
            case ExplorationMenuMode.Status:
                switch (key)
                {
                    // uigame.c PAL_PlayerStatus: kKeyUp/kKeyLeft step back (before the first
                    // member exits), kKeyDown/kKeyRight/kKeySearch step forward (past the last
                    // member exits) and kKeyMenu exits; every exit closes the whole menu because
                    // PAL_InGameMenu case 1 continues with 'goto out'.
                    case ExplorationMenuKey.Up or ExplorationMenuKey.Left:
                        if (StatusMemberIndex == 0) Mode = ExplorationMenuMode.Closed;
                        else StatusMemberIndex--;
                        break;
                    case ExplorationMenuKey.Down or ExplorationMenuKey.Right or ExplorationMenuKey.Confirm:
                        if (StatusMemberIndex == Party.Count - 1) Mode = ExplorationMenuMode.Closed;
                        else StatusMemberIndex++;
                        break;
                    case ExplorationMenuKey.Cancel:
                        Mode = ExplorationMenuMode.Closed;
                        break;
                }
                break;
            case ExplorationMenuMode.EquipItems:
            {
                // itemmenu.c PAL_ItemSelectMenu over the equipable stock: Up/Down wrap, Search
                // confirms an item and kKeyMenu cancels back to the in-game menu. Only owned
                // stock (amount > 0) is listed — the reference compresses depleted slots away.
                var stock = Stock();
                switch (key)
                {
                    case ExplorationMenuKey.Up or ExplorationMenuKey.Left:
                        if (stock.Count > 0) SelectedEquipItem = (SelectedEquipItem + stock.Count - 1) % stock.Count;
                        break;
                    case ExplorationMenuKey.Down or ExplorationMenuKey.Right:
                        if (stock.Count > 0) SelectedEquipItem = (SelectedEquipItem + 1) % stock.Count;
                        break;
                    case ExplorationMenuKey.Confirm:
                        if (stock.Count == 0)
                        {
                            // An empty equipable stock selects object 0 (rgInventory[0].wItem);
                            // PAL_GameEquipItem returns to its caller — the in-game menu.
                            Mode = ExplorationMenuMode.MainMenu;
                        }
                        else
                        {
                            EquipSelection = stock[SelectedEquipItem].ObjectId;
                            SelectedEquipRole = 0; // uigame.c iCurrentPlayer = 0 on entry
                            Mode = ExplorationMenuMode.EquipRoles;
                        }
                        break;
                    case ExplorationMenuKey.Cancel:
                        Mode = ExplorationMenuMode.MainMenu;
                        break;
                }
                break;
            }
            case ExplorationMenuMode.EquipRoles:
            {
                // uigame.c PAL_EquipItemMenu: Up/Down/Left/Right wrap over the party; kKeyMenu
                // returns to the item select; kKeySearch on an eligible role runs the equip
                // script (script.c 0x0018) and the loop re-reads wLastUnequippedItem.
                switch (key)
                {
                    case ExplorationMenuKey.Up or ExplorationMenuKey.Left:
                        SelectedEquipRole = (SelectedEquipRole + Party.Count - 1) % Party.Count;
                        break;
                    case ExplorationMenuKey.Down or ExplorationMenuKey.Right:
                        SelectedEquipRole = (SelectedEquipRole + 1) % Party.Count;
                        break;
                    case ExplorationMenuKey.Confirm:
                    {
                        var binding = EquipBinding(EquipSelection);
                        int roleId = Party[SelectedEquipRole].RoleId;
                        // An ineligible role (kItemFlagEquipableByPlayerRole bit clear) does
                        // nothing: no gear, stock or cursor change, no half equip.
                        if (!binding.Eligible(roleId)) break;
                        int next = equipment.EquipItem(roleId, binding.Part, binding.ObjectId,
                            equipmentObjects!, inventory!, roles, poisons!);
                        if (next != 0 && equipMenu!.Any(candidate => candidate.ObjectId == next))
                            EquipSelection = next; // the displaced object stays selected for another role
                        else
                        {
                            // wLastUnequippedItem 0 (or an object without a menu binding) ends
                            // the role screen: PAL_EquipItemMenu returns to the item select.
                            Mode = ExplorationMenuMode.EquipItems;
                            ClampEquipCursor();
                        }
                        break;
                    }
                    case ExplorationMenuKey.Cancel:
                        Mode = ExplorationMenuMode.EquipItems;
                        break;
                }
                break;
            }
            case ExplorationMenuMode.Items:
            {
                // itemmenu.c PAL_ItemSelectMenu over the usable context: only owned available
                // stock (plus the equipped usable appendix) is listed; PAL_ItemSelectMenuUpdate
                // clamps the remembered inventory cursor at both list ends (no wrap) and the
                // cursor survives closes.
                var rows = UseRows();
                switch (key)
                {
                    case ExplorationMenuKey.Up or ExplorationMenuKey.Left:
                        if (SelectedEquipItem > 0) SelectedEquipItem--;
                        break;
                    case ExplorationMenuKey.Down or ExplorationMenuKey.Right:
                        if (SelectedEquipItem < rows.Count - 1) SelectedEquipItem++;
                        break;
                    case ExplorationMenuKey.Confirm:
                        if (rows.Count == 0)
                        {
                            // An empty usable list selects object 0 (itemmenu.c returns 0);
                            // play.c PAL_GameUseItem then returns and uigame.c PAL_InGameMenu
                            // case 3 'goto out' closes the whole menu — no menu stays open.
                            Mode = ExplorationMenuMode.Closed;
                        }
                        else
                        {
                            var row = rows[SelectedEquipItem];
                            ItemSelection = row.ObjectId;
                            if (row.ApplyToAll)
                            {
                                // play.c: an apply-to-all item never opens the player select; the
                                // script runs once with the caller word 0xFFFF.
                                PendingItemUse = new(Guid.NewGuid(), row.ObjectId, true, 65535);
                                Mode = ExplorationMenuMode.ItemScript;
                            }
                            else
                            {
                                // uigame.c PAL_ItemUseMenu: the remembered player cursor, clamped.
                                if (SelectedUseRole >= Party.Count) SelectedUseRole = 0;
                                Mode = ExplorationMenuMode.ItemTargets;
                            }
                        }
                        break;
                    case ExplorationMenuKey.Cancel:
                        // itemmenu.c kKeyMenu returns 0; play.c PAL_GameUseItem returns and
                        // uigame.c PAL_InGameMenu case 3 'goto out' closes the whole menu.
                        Mode = ExplorationMenuMode.Closed;
                        break;
                }
                break;
            }
            case ExplorationMenuMode.ItemTargets:
            {
                // uigame.c PAL_ItemUseMenu redraws first and reads i = PAL_GetItemAmount: with
                // no selectable stock left (an equipped-only appendix row, or the last consuming
                // copy was just used) any key returns MENUITEM_VALUE_CANCELLED — no script run,
                // no deduction, straight back to the item list.
                if (Available(ItemSelection) <= 0)
                {
                    Mode = ExplorationMenuMode.Items;
                    ClampUseCursor();
                    break;
                }
                // Left/Up and Right/Down wrap over the party (sSelectedPlayer wraps at both
                // ends); kKeyMenu breaks back to the item select and kKeySearch runs the script
                // on the selected member (the loop then reopens the player select for another use).
                switch (key)
                {
                    case ExplorationMenuKey.Up or ExplorationMenuKey.Left:
                        SelectedUseRole = (SelectedUseRole + Party.Count - 1) % Party.Count;
                        break;
                    case ExplorationMenuKey.Down or ExplorationMenuKey.Right:
                        SelectedUseRole = (SelectedUseRole + 1) % Party.Count;
                        break;
                    case ExplorationMenuKey.Confirm:
                        PendingItemUse = new(Guid.NewGuid(), ItemSelection, false, Party[SelectedUseRole].RoleId);
                        Mode = ExplorationMenuMode.ItemScript;
                        break;
                    case ExplorationMenuKey.Cancel:
                        Mode = ExplorationMenuMode.Items;
                        ClampUseCursor();
                        break;
                }
                break;
            }
            case ExplorationMenuMode.ItemScript:
                // Host-owned parking; the guard above already rejected the key.
                break;
            case ExplorationMenuMode.MagicCasters:
            {
                // uigame.c 675-724: one PAL_ReadMenu item per party member; dead members
                // (rgwHP == 0) stay listed but disabled, so Search never confirms them. The
                // static caster cursor survives closes (ui.c PAL_ReadMenu wraps both ways).
                switch (key)
                {
                    case ExplorationMenuKey.Up or ExplorationMenuKey.Left:
                        SelectedMagicCaster = (SelectedMagicCaster + Party.Count - 1) % Party.Count;
                        break;
                    case ExplorationMenuKey.Down or ExplorationMenuKey.Right:
                        SelectedMagicCaster = (SelectedMagicCaster + 1) % Party.Count;
                        break;
                    case ExplorationMenuKey.Confirm:
                        if (roles.Roles[Party[SelectedMagicCaster].RoleId].Hp > 0)
                            EnterMagicList();
                        break;
                    case ExplorationMenuKey.Cancel:
                        // PAL_ReadMenu kKeyMenu cancels the select; PAL_InGameMagicMenu returns
                        // and uigame.c PAL_InGameMenu case 2 'goto out' closes the whole menu.
                        Mode = ExplorationMenuMode.Closed;
                        break;
                }
                break;
            }
            case ExplorationMenuMode.MagicList:
            {
                // magicmenu.c PAL_MagicSelectionMenuUpdate: moves clamp at both list ends
                // without wrap (the vertical grid-line width is the host word package's layout,
                // 32/dwWordLength, which this pure projection does not own — each direction key
                // steps one row with the reference's clamp); kKeySearch confirms enabled rows
                // only and kKeyMenu returns 0.
                var rows = MagicRows();
                if (rows.Count == 0)
                {
                    // Zero learned magics make the reference read rgMagicItem[-1] (undefined
                    // behavior); this projection defines only the cancel path and keeps every
                    // other key inert rather than claiming undefined behavior as faithful.
                    if (key == ExplorationMenuKey.Cancel) Mode = ExplorationMenuMode.Closed;
                    break;
                }
                int index = -1;
                for (int row = 0; row < rows.Count; row++)
                    if (rows[row].ObjectId == SelectedMagic) { index = row; break; }
                if (index < 0) index = 0; // a missing/unset default lands on the first row
                SelectedMagic = rows[index].ObjectId; // the cursor property always names its row
                switch (key)
                {
                    case ExplorationMenuKey.Up or ExplorationMenuKey.Left:
                        if (index > 0) SelectedMagic = rows[index - 1].ObjectId;
                        break;
                    case ExplorationMenuKey.Down or ExplorationMenuKey.Right:
                        if (index < rows.Count - 1) SelectedMagic = rows[index + 1].ObjectId;
                        break;
                    case ExplorationMenuKey.Confirm:
                    {
                        var row = rows[index];
                        if (!row.Enabled) break; // disabled rows never confirm
                        if (MagicBinding(row.ObjectId).ApplyToAll)
                        {
                            // uigame.c 740-743: an apply-to-all magic never opens the player
                            // select; the scripts run once with the literal caller word 0.
                            PendingMagicUse = new(Guid.NewGuid(), row.ObjectId, true,
                                Party[SelectedMagicCaster].RoleId, 0);
                            Mode = ExplorationMenuMode.MagicOnUse;
                        }
                        else
                        {
                            SelectedMagicTarget = 0; // a fresh wPlayer local per selected magic
                            Mode = ExplorationMenuMode.MagicTargets;
                        }
                        break;
                    }
                    case ExplorationMenuKey.Cancel:
                        // wMagic = 0 breaks the loop; the magic menu exits and uigame.c case 2
                        // 'goto out' closes the whole menu.
                        Mode = ExplorationMenuMode.Closed;
                        break;
                }
                break;
            }
            case ExplorationMenuMode.MagicTargets:
            {
                // uigame.c 765-803: Left/Up and Right/Down step without wrap over the party —
                // every member stays selectable, including dead ones (revives); kKeyMenu breaks
                // back to the magic selection and kKeySearch commits the cast on the selected
                // member with that member's role id as the script caller word.
                switch (key)
                {
                    case ExplorationMenuKey.Up or ExplorationMenuKey.Left:
                        if (SelectedMagicTarget > 0) SelectedMagicTarget--;
                        break;
                    case ExplorationMenuKey.Down or ExplorationMenuKey.Right:
                        if (SelectedMagicTarget < Party.Count - 1) SelectedMagicTarget++;
                        break;
                    case ExplorationMenuKey.Confirm:
                        PendingMagicUse = new(Guid.NewGuid(), SelectedMagic, false,
                            Party[SelectedMagicCaster].RoleId, Party[SelectedMagicTarget].RoleId);
                        Mode = ExplorationMenuMode.MagicOnUse;
                        break;
                    case ExplorationMenuKey.Cancel:
                        EnterMagicList();
                        break;
                }
                break;
            }
            case ExplorationMenuMode.MagicOnUse or ExplorationMenuMode.MagicOnSuccess:
                // Host-owned parking; the guard above already rejected the key.
                break;
            case ExplorationMenuMode.SystemMenu:
                // uigame.c PAL_SystemMenu/PAL_SaveSlotMenu: Up/Left and Down/Right wrap the five
                // manual slots and only move the remembered cursor — browsing never writes and
                // never switches anything (#148 Stories 3/4). kKeySearch confirms the selected
                // slot as exactly one pending save request and parks the layer until the host's
                // receipt; a duplicate confirm or key echo can never issue a second (Story 12).
                // kKeyMenu returns to the in-game main menu.
                switch (key)
                {
                    case ExplorationMenuKey.Up or ExplorationMenuKey.Left:
                        SelectedSaveSlot = SelectedSaveSlot == PlayerSaveContract.FirstSlot
                            ? PlayerSaveContract.LastSlot : SelectedSaveSlot - 1;
                        break;
                    case ExplorationMenuKey.Down or ExplorationMenuKey.Right:
                        SelectedSaveSlot = SelectedSaveSlot == PlayerSaveContract.LastSlot
                            ? PlayerSaveContract.FirstSlot : SelectedSaveSlot + 1;
                        break;
                    case ExplorationMenuKey.Confirm:
                        PendingSave = new(Guid.NewGuid(), SelectedSaveSlot);
                        break;
                    case ExplorationMenuKey.Cancel:
                        Mode = ExplorationMenuMode.MainMenu;
                        break;
                }
                break;
        }
    }

    /// <summary>The current member's read-only status projection from the live owners. Equipment
    /// and poison object names resolve now; a missing word rejects instead of showing a placeholder.</summary>
    public ExplorationMenuStatusView StatusView()
    {
        if (Mode != ExplorationMenuMode.Status) throw new InvalidOperationException("The status page is not open.");
        var member = Party[StatusMemberIndex];
        var role = roles.Roles[member.RoleId];
        var table = growth[member.RoleId].LevelUpExperience;
        if (role.Level < 0 || role.Level >= table.Count)
            throw new InvalidDataException($"Role {member.RoleId} level {role.Level} is at or beyond its level-up table.");
        int baseAttack = member.PanelDeclared ? role.Attack : member.AuthoredAttack;
        int baseMagicPower = member.PanelDeclared ? role.MagicPower : member.AuthoredMagicPower;
        int baseDefense = member.PanelDeclared ? role.Defense : member.AuthoredDefense;
        int baseDexterity = member.PanelDeclared ? role.Dexterity : member.AuthoredDexterity;
        int baseFleeRate = member.PanelDeclared ? role.FleeRate : member.AuthoredFleeRate;
        var gear = new List<ExplorationMenuEquipment>();
        foreach (int objectId in role.Equipment)
            if (objectId != 0) gear.Add(new(objectId, Word(objectId)));
        var poisonRows = new List<ExplorationMenuPoison>();
        if (poisons is not null)
            foreach (var slot in poisons.Slots(member.RoleId))
                if (slot.PoisonId != 0) poisonRows.Add(new(slot.PoisonId, Word(slot.PoisonId)));
        return new(StatusMemberIndex, member.RoleId, Word(member.NameWord), role.Hp == 0,
            role.Level, role.Hp, role.MaxHp, role.Mp, role.MaxMp,
            role.PrimaryExperience.Value, table[role.Level],
            equipment.EffectiveAttack(member.RoleId, baseAttack),
            equipment.EffectiveMagicPower(member.RoleId, baseMagicPower),
            equipment.EffectiveDefense(member.RoleId, baseDefense),
            equipment.EffectiveDexterity(member.RoleId, baseDexterity),
            equipment.EffectiveFleeRate(member.RoleId, baseFleeRate),
            role.Statuses, gear.AsReadOnly(), poisonRows.AsReadOnly(),
            Word(LevelWord), Word(HpWord), Word(MpWord), Word(ExperienceWord),
            Word(AttackPowerWord), Word(MagicPowerWord), Word(DefenseWord),
            Word(DexterityWord), Word(FleeRateWord));
    }

    /// <summary>The equipable stock list in binding order (itemmenu.c lists owned copies only).
    /// Depleted slots stay unlisted, matching the reference's menu-open compression view.</summary>
    public IReadOnlyList<ExplorationMenuEquipRow> EquipItemsView()
    {
        if (Mode != ExplorationMenuMode.EquipItems)
            throw new InvalidOperationException("The equipment item list is not open.");
        return Stock().Select(binding => new ExplorationMenuEquipRow(binding.ObjectId, binding.Part,
            inventory!.GetAmount(binding.ObjectId), Word(binding.ObjectId), PartLabel(binding.Part))).ToArray();
    }

    /// <summary>The role-selection page for the current item: eligibility bits and each member's
    /// currently equipped object names (uigame.c PAL_EquipItemMenu's player box and gear list).</summary>
    public ExplorationMenuEquipSelection EquipRolesView()
    {
        if (Mode != ExplorationMenuMode.EquipRoles)
            throw new InvalidOperationException("The equipment role selection is not open.");
        var binding = EquipBinding(EquipSelection);
        return new(binding.ObjectId, binding.Part, Word(binding.ObjectId), PartLabel(binding.Part),
            Party.Select(member => new ExplorationMenuEquipRole(Word(member.NameWord),
                binding.Eligible(member.RoleId),
                roles.Roles[member.RoleId].Equipment.Where(objectId => objectId != 0).Select(Word).ToArray())).ToArray());
    }

    /// <summary>The usable list in authored binding order: available stock rows first, then the
    /// equipped usable appendix (itemmenu.c PAL_ItemSelectMenuInit appends those outside battle
    /// with amount 0 / in-use −1). Depleted or absent stock stays unlisted.</summary>
    public IReadOnlyList<ExplorationMenuUseRow> ItemsView()
    {
        if (Mode is not (ExplorationMenuMode.Items or ExplorationMenuMode.ItemScript))
            throw new InvalidOperationException("The items page is not open.");
        return UseRows();
    }

    /// <summary>The target-selection page for the selected item: the shared live party vitals the
    /// reference draws while picking whom to use the item on (uigame.c PAL_ItemUseMenu).</summary>
    public ExplorationMenuItemTargetView ItemTargetsView()
    {
        if (Mode != ExplorationMenuMode.ItemTargets)
            throw new InvalidOperationException("The item target selection is not open.");
        var binding = UseBinding(ItemSelection);
        return new(binding.ObjectId, Word(binding.ObjectId), binding.Bitmap,
            Party.Select(member =>
            {
                var role = roles.Roles[member.RoleId];
                return new ExplorationMenuItemTarget(member.RoleId, Word(member.NameWord),
                    role.Hp, role.MaxHp, role.Mp, role.MaxMp);
            }).ToArray(), Word(HpWord), Word(MpWord));
    }

    /// <summary>Host receipt for the committed script run: consuming items remove one copy only
    /// when the on-use script survived (play.c PAL_GameUseItem's PAL_AddItemToInventory guard);
    /// the single-target loop then reopens the player select while an apply-to-all item returns
    /// from PAL_GameUseItem after one run — uigame.c PAL_InGameMenu case 3 'goto out' closes the
    /// whole menu there.</summary>
    public void CompleteItemUse(Guid requestId, bool scriptSucceeded)
    {
        if (Mode != ExplorationMenuMode.ItemScript || PendingItemUse is not { } request || request.Id != requestId)
            throw new InvalidOperationException("The pending item use does not match the completed script call.");
        var binding = UseBinding(request.ObjectId);
        if (binding.Consuming && scriptSucceeded)
            // script.c's inventory command result stays ignored here (play.c): a shortage removes
            // nothing and equipped-only objects keep their copies.
            inventory!.RemoveItem(binding.ObjectId, 1);
        PendingItemUse = null;
        if (binding.ApplyToAll)
        {
            // play.c: the apply-to-all branch returns from PAL_GameUseItem after its one run;
            // the in-game menu path then closes the whole menu (uigame.c case 3 'goto out').
            Mode = ExplorationMenuMode.Closed;
        }
        else Mode = ExplorationMenuMode.ItemTargets;
    }

    /// <summary>The caster-select rows (uigame.c PAL_InGameMagicMenu's PAL_ReadMenu items): the
    /// whole party in party order with its live alive flag; dead members stay listed but are
    /// never confirmable.</summary>
    public IReadOnlyList<ExplorationMenuMagicCaster> MagicCastersView()
    {
        if (Mode != ExplorationMenuMode.MagicCasters)
            throw new InvalidOperationException("The magic caster selection is not open.");
        return Party.Select(member =>
        {
            var role = roles.Roles[member.RoleId];
            return new ExplorationMenuMagicCaster(member.RoleId, Word(member.NameWord), role.Hp > 0);
        }).ToArray();
    }

    /// <summary>The magic list for the selected caster (magicmenu.c PAL_MagicSelectionMenuInit):
    /// every learned magic of the shared live caster role in slot order, sorted by object WORD
    /// ascending; enabled only when the magic is usable outside battle and its cost fits the
    /// caster's current live MP. A learned magic without an authored binding rejects instead of
    /// listing a placeholder.</summary>
    public IReadOnlyList<ExplorationMenuMagicRow> MagicListView()
    {
        if (Mode is not (ExplorationMenuMode.MagicList or ExplorationMenuMode.MagicOnUse or ExplorationMenuMode.MagicOnSuccess))
            throw new InvalidOperationException("The magic page is not open.");
        return MagicRows();
    }

    /// <summary>The member-selection page for the selected single-target magic: the shared live
    /// party vitals the reference draws while picking whom to cast on.</summary>
    public ExplorationMenuMagicTargetView MagicTargetsView()
    {
        if (Mode != ExplorationMenuMode.MagicTargets)
            throw new InvalidOperationException("The magic target selection is not open.");
        return new(SelectedMagic, Word(SelectedMagic),
            Party.Select(member =>
            {
                var role = roles.Roles[member.RoleId];
                return new ExplorationMenuMagicTarget(member.RoleId, Word(member.NameWord),
                    role.Hp, role.MaxHp, role.Mp, role.MaxMp);
            }).ToArray(), Word(HpWord), Word(MpWord));
    }

    /// <summary>Host receipt for the committed on-use run (uigame.c 810-818): a failed on-use
    /// skips the on-success run and the MP deduction; the flow itself continues — the target
    /// select reopens for single-target magics (repeat use) and the apply-to-all branch returns
    /// to the magic selection loop. A survived on-use advances the same cast to the on-success
    /// phase; nothing is deducted yet.</summary>
    public void CompleteMagicUse(Guid requestId, bool onUseSucceeded)
    {
        if (Mode != ExplorationMenuMode.MagicOnUse || PendingMagicUse is not { } cast || cast.Id != requestId)
            throw new InvalidOperationException("The pending magic use does not match the completed script call.");
        PendingMagicUse = null;
        if (!onUseSucceeded)
        {
            if (cast.ApplyToAll) EnterMagicList();
            else Mode = ExplorationMenuMode.MagicTargets;
            return;
        }
        PendingMagicSuccess = cast;
        Mode = ExplorationMenuMode.MagicOnSuccess;
    }

    /// <summary>Host receipt for the committed on-success run (uigame.c 816-834): only when both
    /// scripts survived does the caster's shared live MP pay the cost — uigame.c's raw rgwMP
    /// WORD subtraction, deducted from the caster, never battle's deduct-first order and never
    /// a general refund of the scripts' executed effects. The apply-to-all branch returns to the
    /// magic selection loop; a single-target loop reopens while the caster can still pay the
    /// cost and otherwise ends at the magic list.</summary>
    public void CompleteMagicSuccess(Guid requestId, bool onSuccessSucceeded)
    {
        if (Mode != ExplorationMenuMode.MagicOnSuccess || PendingMagicSuccess is not { } cast || cast.Id != requestId)
            throw new InvalidOperationException("The pending magic success does not match the completed script call.");
        var binding = MagicBinding(cast.ObjectId);
        var caster = roles.Roles[cast.CasterRoleId];
        if (onSuccessSucceeded)
            roles.SetVitals(cast.CasterRoleId, caster.Hp, unchecked((ushort)(caster.Mp - binding.CostMp)));
        PendingMagicSuccess = null;
        if (cast.ApplyToAll)
        {
            EnterMagicList();
            return;
        }
        // uigame.c 824-834: the exhaustion cancel (wPlayer = MENUITEM_VALUE_CANCELLED) is nested
        // inside the on-success branch's deduction, so only a successful, deducted cast can
        // end the target loop by exhaustion; every other run reopens the target select — even
        // when an executed script already spent the caster's live MP below the cost.
        if (onSuccessSucceeded && roles.Roles[cast.CasterRoleId].Mp < binding.CostMp) EnterMagicList();
        else Mode = ExplorationMenuMode.MagicTargets;
    }

    /// <summary>Host receipt for the confirmed save (#224): only the owned request id completes
    /// the parking, and the completed save — success or failure alike — returns the layer to slot
    /// browsing for another browse, confirm or cancel. The session itself never touches storage,
    /// so the outcome is host-owned: the completed outcome reports and the recovery-slot switch
    /// happen in the host (#148 Story 9; Decision 27), never here.</summary>
    public void CompleteSave(Guid requestId, bool succeeded)
    {
        if (Mode != ExplorationMenuMode.SystemMenu || PendingSave is not { } request || request.Id != requestId)
            throw new InvalidOperationException("The pending save does not match the completed request.");
        _ = succeeded; // both outcomes leave the layer identical; the host owns the outcome effects
        PendingSave = null;
    }

    /// <summary>The bound equip-menu rows whose stock is owned, in authored binding order.</summary>
    private IReadOnlyList<BattleEquipMenuBinding> Stock() =>
        equipMenu!.Where(binding => inventory!.GetAmount(binding.ObjectId) > 0).ToArray();

    private BattleEquipMenuBinding EquipBinding(int objectId) =>
        equipMenu!.SingleOrDefault(binding => binding.ObjectId == objectId)
            ?? throw new InvalidDataException($"Equip-menu object {objectId} has no binding.");

    private string PartLabel(int part) => Word(PartWordBase + part);

    private void ClampEquipCursor()
    {
        int count = Stock().Count;
        if (SelectedEquipItem >= count) SelectedEquipItem = Math.Max(0, count - 1);
    }

    /// <summary>The use-menu rows: available stock in binding order, then the equipped usable
    /// appendix in party order (one row per equipped slot occurrence, itemmenu.c 344-378).</summary>
    private IReadOnlyList<ExplorationMenuUseRow> UseRows()
    {
        var rows = new List<ExplorationMenuUseRow>();
        foreach (var binding in useMenu!)
        {
            int available = Available(binding.ObjectId);
            if (available > 0)
                rows.Add(new(binding.ObjectId, Word(binding.ObjectId), binding.Consuming, binding.ApplyToAll,
                    binding.Bitmap, false, available));
        }
        foreach (var member in Party)
            foreach (int objectId in roles.Roles[member.RoleId].Equipment)
                if (objectId != 0 && useMenu.Any(binding => binding.ObjectId == objectId))
                {
                    var binding = UseBinding(objectId);
                    rows.Add(new(objectId, Word(objectId), binding.Consuming, binding.ApplyToAll,
                        binding.Bitmap, true, 0));
                }
        return rows;
    }

    /// <summary>The reference's selectable amount: (SHORT)nAmount − (SHORT)nAmountInUse over the
    /// first slot carrying the item (PAL_CompressInventory keeps one row per object).</summary>
    private int Available(int itemId)
    {
        foreach (var slot in inventory!.Slots)
            if (slot.ItemId == itemId)
                return (short)slot.Amount - (short)slot.AmountInUse;
        return 0;
    }

    private BattleUseMenuBinding UseBinding(int objectId) =>
        useMenu!.SingleOrDefault(binding => binding.ObjectId == objectId)
            ?? throw new InvalidDataException($"Use-menu object {objectId} has no binding.");

    /// <summary>The caster's learned magics (magicmenu.c reads rgwMagic[i][role]) resolved to
    /// their authored bindings and sorted by object WORD ascending (its bubble sort); the
    /// enabled flag recomputes from the caster's current live MP on every read.</summary>
    private IReadOnlyList<ExplorationMenuMagicRow> MagicRows()
    {
        var caster = roles.Roles[Party[SelectedMagicCaster].RoleId];
        var rows = new List<ExplorationMenuMagicRow>();
        foreach (int magicId in caster.Magics)
        {
            if (magicId == 0) continue;
            var binding = MagicBinding(magicId);
            rows.Add(new(magicId, Word(magicId), binding.CostMp,
                binding.UsableOutsideBattle && binding.CostMp <= caster.Mp));
        }
        rows.Sort((left, right) => left.ObjectId.CompareTo(right.ObjectId));
        return rows;
    }

    /// <summary>Enters the magic selection loop (PAL_MagicSelectionMenu's reopened default):
    /// the remembered cursor resolves to its row, or to the first row when unset or gone.</summary>
    private void EnterMagicList()
    {
        Mode = ExplorationMenuMode.MagicList;
        var rows = MagicRows();
        if (rows.Count > 0 && !rows.Any(row => row.ObjectId == SelectedMagic))
            SelectedMagic = rows[0].ObjectId;
    }

    private BattleMagicMenuBinding MagicBinding(int objectId) =>
        magicMenu!.SingleOrDefault(binding => binding.ObjectId == objectId)
            ?? throw new InvalidDataException($"Magic-menu object {objectId} has no binding.");

    private void ClampUseCursor()
    {
        int count = UseRows().Count;
        if (SelectedEquipItem >= count) SelectedEquipItem = Math.Max(0, count - 1);
    }

    private string Word(int id) => words.TryGetValue(id, out string? text) && text is not null
        ? text
        : throw new InvalidDataException($"Menu word {id} is missing from the selected language package.");

    private static bool IsWord(int value) => value is >= 0 and <= 65535;
}
