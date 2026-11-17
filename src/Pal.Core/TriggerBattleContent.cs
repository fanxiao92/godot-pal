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
        IReadOnlyList<BattleEquipMenuBinding>? equipMenu = null)
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
        InitialRoles = new ReadOnlyDictionary<int, BattleRoleSnapshot>(byId.ToDictionary(x => x.Key, x => Freeze(x.Value)));
        Players = Array.AsReadOnly(players.Select(x => x with { Equipment = Copy(x.Equipment), Statuses = Copy(x.Statuses), Poisons = Copy(x.Poisons) }).ToArray());
        GrowthByRole = new ReadOnlyDictionary<int, ClassicBattleGrowthData>(growth.ToDictionary(x => x.Key,
            x => new ClassicBattleGrowthData(Copy(x.Value.LevelUpExperience), Array.AsReadOnly(x.Value.LevelUpMagics.ToArray()))));
        EnemyTeams = new ReadOnlyDictionary<int, IReadOnlyList<ClassicBattleEnemy>>(teams.ToDictionary(x => x.Key, x =>
        {
            if (x.Value is null || x.Value.Count is < 1 or > 5 || x.Value.Any(e => e is null))
                throw new InvalidDataException($"Battle team {x.Key} requires one to five nonnull enemy slots.");
            return (IReadOnlyList<ClassicBattleEnemy>)Array.AsReadOnly(x.Value.Select(e => e with { Statuses = Copy(e.Statuses), Poisons = Copy(e.Poisons) }).ToArray());
        }));
        BattleEquipmentState.ValidateEquipmentClosure(roleEquipment, equipmentObjects, roles);
        if (panelDeclaredRoles is not null && panelDeclaredRoles.Any(role => !byId.ContainsKey(role)))
            throw new InvalidDataException("Declared panel roles must name a declared battle role.");
        PanelDeclaredRoles = new HashSet<int>(panelDeclaredRoles ?? []);
        RoleEquipment = roleEquipment is null || roleEquipment.Count == 0 ? Array.Empty<IReadOnlyList<int>>()
            : Array.AsReadOnly(roleEquipment.Select(row => (IReadOnlyList<int>)Array.AsReadOnly(row.ToArray())).ToArray());
        EquipmentObjects = new ReadOnlyDictionary<int, BattleEquipmentObject>((equipmentObjects ?? []).ToDictionary(
            x => x.ObjectId, x => x with { Effects = Array.AsReadOnly(x.Effects.Select(e => e with { Statuses = Copy(e.Statuses) }).ToArray()) }));
        InitialInventory = CopyInventory(initialInventory);
        EquipMenu = CopyEquipMenu(equipMenu, EquipmentObjects);
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

    internal static BattleRoleSnapshot Freeze(BattleRoleSnapshot role) => role with
    {
        HiddenExperience = new ReadOnlyDictionary<string, BattleExperienceSnapshot>(role.HiddenExperience.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal)),
        Equipment = Copy(role.Equipment), Magics = Copy(role.Magics), Statuses = Copy(role.Statuses)
    };
    private static IReadOnlyList<int> Copy(IReadOnlyList<int> source) => source is null ? throw new InvalidDataException("Required numeric array is null.") : Array.AsReadOnly(source.ToArray());
    private static T Single<T>(IEnumerable<T> source) => source.Count() == 1 ? source.Single()
        : throw new InvalidOperationException("Single-actor compatibility access cannot project multiplayer content.");
}

public sealed record TriggerBattleRequest(Guid Id, int Caller, TriggerPosition Position, int EnemyTeamId, bool IsBoss,
    int NextAddress, int? LossAddress, int? FleeAddress, ClassicBattleLifecycleSession Lifecycle);
