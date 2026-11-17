namespace Pal.Core;

public enum ExplorationMenuMode { Closed, MainMenu, Status }

/// <summary>Menu verbs as the fixed reference maps them (input.c: direction keys navigate,
/// Search=confirm, Menu=cancel). The host translates its physical keys into these verbs.</summary>
public enum ExplorationMenuKey { Up, Down, Left, Right, Confirm, Cancel }

/// <summary>Deliverable menu pages; #213 binds only the read-only status page. Operative pages
/// (equipment/items/magic/shop) arrive with their later M6-B leaves on the same session.</summary>
public enum ExplorationMenuPage { Status }

/// <summary>One main-menu entry: the page Confirm opens and its label WORD.</summary>
public sealed record ExplorationMenuEntry(ExplorationMenuPage Page, int WordId);

/// <summary>One party member binding: the shared live role, its name WORD, and the authored
/// player panel words used when the role is not panel-declared (TriggerBattleContent.SettledPlayers
/// keeps authored player panels for undeclared roles; panel-declared roles read live role panels).</summary>
public sealed record ExplorationMenuMember(int RoleId, int NameWord, bool PanelDeclared,
    int AuthoredAttack, int AuthoredMagicPower, int AuthoredDefense, int AuthoredDexterity, int AuthoredFleeRate);

/// <summary>One nonempty equipment slot with its resolved object-name word text.</summary>
public sealed record ExplorationMenuEquipment(int ObjectId, string Name);

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

    private readonly RoleLiveState roles;
    private readonly BattleEquipmentState equipment;
    private readonly IReadOnlyDictionary<int, ClassicBattleGrowthData> growth;
    private readonly IReadOnlyDictionary<int, string> words;
    private readonly PartyPoisonState? poisons;

    public ExplorationMenuSession(IReadOnlyList<ExplorationMenuEntry> entries, IReadOnlyList<ExplorationMenuMember> party,
        RoleLiveState roles, BattleEquipmentState equipment, IReadOnlyDictionary<int, ClassicBattleGrowthData> growth,
        IReadOnlyDictionary<int, string> words, PartyPoisonState? poisons = null)
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
    }

    public ExplorationMenuMode Mode { get; private set; }
    /// <summary>The remembered main-menu cursor (gpGlobals->iCurMainMenuItem); it survives closes.</summary>
    public int SelectedEntry { get; private set; }
    public int StatusMemberIndex { get; private set; }
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

    private string Word(int id) => words.TryGetValue(id, out string? text) && text is not null
        ? text
        : throw new InvalidDataException($"Menu word {id} is missing from the selected language package.");

    private static bool IsWord(int value) => value is >= 0 and <= 65535;
}
