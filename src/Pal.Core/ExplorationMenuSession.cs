namespace Pal.Core;

public enum ExplorationMenuMode { Closed, MainMenu, Status, EquipItems, EquipRoles, Items, ItemTargets, ItemScript }

/// <summary>Menu verbs as the fixed reference maps them (input.c: direction keys navigate,
/// Search=confirm, Menu=cancel). The host translates its physical keys into these verbs.</summary>
public enum ExplorationMenuKey { Up, Down, Left, Right, Confirm, Cancel }

/// <summary>Deliverable menu pages; #213 binds the read-only status page, #214 the equipment
/// page and #215 the items page on the same session. Later operative pages arrive with their
/// own M6-B leaves.</summary>
public enum ExplorationMenuPage { Status, Equipment, Items }

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
/// name word text, the consuming/apply-to-all words and the listed amount. Stock rows carry the
/// available amount (nAmount − nAmountInUse); equipped appendix rows (amount 0 / in-use −1 in the
/// reference) carry zero and never count equipment copies as usable stock.</summary>
public sealed record ExplorationMenuUseRow(int ObjectId, string Name, bool Consuming, bool ApplyToAll,
    bool Equipped, int Amount);

/// <summary>One party row of the item target page (uigame.c PAL_ItemUseMenu): the shared live
/// vitals the reference draws while the player picks whom to use the item on.</summary>
public sealed record ExplorationMenuItemTarget(int RoleId, string Name, int Hp, int MaxHp, int Mp, int MaxMp);

/// <summary>The item target-selection page for one item: the object, the party rows and the
/// resolved HP/MP label words the reference's stat box draws.</summary>
public sealed record ExplorationMenuItemTargetView(int ObjectId, string Name,
    IReadOnlyList<ExplorationMenuItemTarget> Targets, string HpLabel, string MpLabel);

/// <summary>play.c PAL_GameUseItem's one committed script run: the object plus the caller word
/// PAL_RunTriggerScript receives — the selected member's role id, or the apply-to-all 0xFFFF
/// which keeps the previously saved caller. The host runs the item's on-use script entry and
/// receipts exactly this request.</summary>
public sealed record ExplorationMenuItemUseRequest(Guid Id, int ObjectId, bool ApplyToAll, int TargetRoleId);

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

    private readonly RoleLiveState roles;
    private readonly BattleEquipmentState equipment;
    private readonly IReadOnlyDictionary<int, ClassicBattleGrowthData> growth;
    private readonly IReadOnlyDictionary<int, string> words;
    private readonly PartyPoisonState? poisons;
    private readonly IReadOnlyList<BattleEquipMenuBinding>? equipMenu;
    private readonly IReadOnlyDictionary<int, BattleEquipmentObject>? equipmentObjects;
    private InventoryLiveState? inventory;
    private readonly IReadOnlyList<BattleUseMenuBinding>? useMenu;

    public ExplorationMenuSession(IReadOnlyList<ExplorationMenuEntry> entries, IReadOnlyList<ExplorationMenuMember> party,
        RoleLiveState roles, BattleEquipmentState equipment, IReadOnlyDictionary<int, ClassicBattleGrowthData> growth,
        IReadOnlyDictionary<int, string> words, PartyPoisonState? poisons = null,
        IReadOnlyList<BattleEquipMenuBinding>? equipMenu = null,
        IReadOnlyDictionary<int, BattleEquipmentObject>? equipmentObjects = null,
        InventoryLiveState? inventory = null,
        IReadOnlyList<BattleUseMenuBinding>? useMenu = null)
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
        return new(binding.ObjectId, Word(binding.ObjectId),
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
                rows.Add(new(binding.ObjectId, Word(binding.ObjectId), binding.Consuming, binding.ApplyToAll, false, available));
        }
        foreach (var member in Party)
            foreach (int objectId in roles.Roles[member.RoleId].Equipment)
                if (objectId != 0 && useMenu.Any(binding => binding.ObjectId == objectId))
                {
                    var binding = UseBinding(objectId);
                    rows.Add(new(objectId, Word(objectId), binding.Consuming, binding.ApplyToAll, true, 0));
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
