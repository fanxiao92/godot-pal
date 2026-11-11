using System.Collections.ObjectModel;

namespace Pal.Core;

/// <summary>Per-role effect row of one equipment object's equip script (global.c script 0x0017/0x001A/0x002D word writes).
/// The optional stat words cover PLAYERROLES stat 18/21/22 equipment writes (magicStrength, fleeRate,
/// poisonResistance). AttackAll and CooperativeMagic retain nullable field 4/65 WORDs in content and
/// equipment registers; selected combat rejects nonzero values rather than executing their effects.</summary>
public sealed record BattleEquipmentEffect(int Attack, int Defense, int Dexterity, int BattleSprite, IReadOnlyList<int> Statuses,
    int? MagicPower = null, int? FleeRate = null, int? PoisonResistance = null)
{
    public int? AttackAll { get; init; }
    public int? CooperativeMagic { get; init; }
}

/// <summary>Authored equip-script closure for one object: exactly six role-indexed effect rows (ObjectId is 1..65535).</summary>
public sealed record BattleEquipmentObject(int ObjectId, IReadOnlyList<BattleEquipmentEffect> Effects);

/// <summary>Formal six-role equipment initialization (PAL_UpdateEquipments, global.c:1352) and effective panel
/// projections (PAL_GetPlayerAttackStrength/Defense/Dexterity global.c:1736/1800/1832 and PAL_GetPlayerBattleSprite
/// global.c:1978-2008). Retains nullable field 4/65 equipment words for selected-combat validation,
/// not attack-all or cooperative-magic execution.</summary>
public sealed class BattleEquipmentState
{
    public const int RoleCount = 6;   // MAX_PLAYER_ROLES (palcommon.h)
    public const int SlotCount = 6;   // MAX_PLAYER_EQUIPMENTS (kBodyPartHead..kBodyPartWear)
    public const int RegisterCount = SlotCount + 1; // kBodyPartExtra: numeric registers stay zero, nullable words stay unwritten
    public const int StatusCount = 9; // kStatusAll
    internal const int PersistentStatusFloor = 1000; // PAL_RemovePlayerStatus keeps values above 999 (global.c:2297)
    internal const int GoodStatusFirstIndex = 5;     // bravery, protect, haste, dualAttack (global.h tagSTATUS)

    private readonly int[,] equipment = new int[RoleCount, SlotCount];
    private readonly int[,] attack = new int[RoleCount, RegisterCount];
    private readonly int[,] defense = new int[RoleCount, RegisterCount];
    private readonly int[,] dexterity = new int[RoleCount, RegisterCount];
    private readonly int[,] battleSprite = new int[RoleCount, RegisterCount];
    private readonly int[,] magicPower = new int[RoleCount, RegisterCount];
    private readonly int[,] fleeRate = new int[RoleCount, RegisterCount];
    private readonly int[,] poisonResistance = new int[RoleCount, RegisterCount];
    private readonly int?[,] attackAll = new int?[RoleCount, RegisterCount];
    private readonly int?[,] cooperativeMagic = new int?[RoleCount, RegisterCount];
    private readonly int[,] statuses = new int[RoleCount, StatusCount];

    private BattleEquipmentState() { }
    /// <summary>True only when the six-role grid was declared; an undeclared zero state keeps the old
    /// zero-equipment domain instead of licensing the exact-grid agreement checks.</summary>
    public bool Declared { get; private init; }

    /// <summary>Zero state when no role carries equipment; throws before any state when non-zero equipment lacks the six-role closure.</summary>
    public static BattleEquipmentState Initialize(TriggerBattleContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.RoleEquipment.Count == 0)
        {
            if (content.InitialRoles.Values.Any(role => role.Equipment.Any(value => value != 0)))
                throw new InvalidDataException("$.roleEquipment: non-zero role equipment requires the declared six-role equipment closure.");
            return new();
        }
        return Initialize(content.RoleEquipment, content.EquipmentObjects);
    }

    /// <summary>Initializes all registers, then copies every equipped object's effect row to its (role, slot)
    /// context. Nullable WORDs preserve explicit zero; item zero and unwritten registers remain null.</summary>
    public static BattleEquipmentState Initialize(IReadOnlyList<IReadOnlyList<int>> roleEquipment,
        IReadOnlyDictionary<int, BattleEquipmentObject> equipmentObjects)
    {
        ArgumentNullException.ThrowIfNull(roleEquipment);
        ArgumentNullException.ThrowIfNull(equipmentObjects);
        ValidateEquipmentClosure(roleEquipment, equipmentObjects.Values.ToList(), []);
        var state = new BattleEquipmentState() { Declared = true };
        for (int role = 0; role < RoleCount; role++)
            for (int slot = 0; slot < SlotCount; slot++)
            {
                int objectId = roleEquipment[role][slot];
                state.equipment[role, slot] = objectId;
                if (objectId == 0) continue;
                if (!equipmentObjects.TryGetValue(objectId, out BattleEquipmentObject? obj))
                    throw new InvalidDataException($"$.roleEquipment[{role}][{slot}]: equipment object {objectId} is not declared.");
                BattleEquipmentEffect effect = obj.Effects[role];
                state.attack[role, slot] = effect.Attack;
                state.defense[role, slot] = effect.Defense;
                state.dexterity[role, slot] = effect.Dexterity;
                state.battleSprite[role, slot] = effect.BattleSprite;
                state.magicPower[role, slot] = effect.MagicPower ?? 0;
                state.fleeRate[role, slot] = effect.FleeRate ?? 0;
                state.poisonResistance[role, slot] = effect.PoisonResistance ?? 0;
                state.attackAll[role, slot] = effect.AttackAll;
                state.cooperativeMagic[role, slot] = effect.CooperativeMagic;
                // PAL_SetPlayerStatus good-status branch (global.c:2255): keep the longer value, never stack.
                for (int status = GoodStatusFirstIndex; status < StatusCount; status++)
                    if (effect.Statuses[status] > state.statuses[role, status])
                        state.statuses[role, status] = effect.Statuses[status];
            }
        return state;
    }

    /// <summary>Shared strict closure validation for the declared sections: shapes, WORD domains, unique object IDs,
    /// declared-object reference closure and party-role grid agreement. No sections declared is legal.</summary>
    internal static void ValidateEquipmentClosure(IReadOnlyList<IReadOnlyList<int>>? roleEquipment,
        IReadOnlyList<BattleEquipmentObject>? equipmentObjects, IEnumerable<BattleRoleSnapshot> roles)
    {
        var declared = new Dictionary<int, BattleEquipmentObject>();
        if (equipmentObjects is not null)
            foreach (BattleEquipmentObject? obj in equipmentObjects)
            {
                if (obj is null) throw Error("$.equipments", "equipment object is null");
                if (obj.ObjectId is < 1 or > 65535) throw Error("$.equipments", "equipment object ID must be 1..65535");
                if (obj.Effects is not { Count: RoleCount })
                    throw Error("$.equipments", $"equipment object {obj.ObjectId} requires exactly {RoleCount} role effect rows");
                foreach (BattleEquipmentEffect? effect in obj.Effects)
                {
                    if (effect is null) throw Error("$.equipments", $"equipment object {obj.ObjectId} has a null effect row");
                    if (!Word(effect.Attack) || !Word(effect.Defense) || !Word(effect.Dexterity) || !Word(effect.BattleSprite))
                        throw Error("$.equipments", $"equipment object {obj.ObjectId} effect words must be 0..65535");
                    if ((effect.MagicPower is < 0 or > 65535) || (effect.FleeRate is < 0 or > 65535) || (effect.PoisonResistance is < 0 or > 65535))
                        throw Error("$.equipments", $"equipment object {obj.ObjectId} optional effect words must be 0..65535");
                    if (effect.Statuses is not { Count: StatusCount })
                        throw Error("$.equipments", $"equipment object {obj.ObjectId} requires {StatusCount}-word status rows");
                    for (int status = 0; status < StatusCount; status++)
                    {
                        int value = effect.Statuses[status];
                        if (value == 0) continue;
                        if (status < GoodStatusFirstIndex)
                            throw Error("$.equipments", $"equipment object {obj.ObjectId} declares status slot {status}; only bravery, protect, haste and dualAttack are equipment statuses");
                        if (value is < PersistentStatusFloor or > 65535)
                            throw Error("$.equipments", $"equipment object {obj.ObjectId} status slot {status} must be a persistent word {PersistentStatusFloor}..65535");
                    }
                    if ((effect.AttackAll is < 0 or > 65535) || (effect.CooperativeMagic is < 0 or > 65535))
                        throw Error("$.equipments", $"equipment object {obj.ObjectId} optional effect words must be 0..65535");
                }
                if (!declared.TryAdd(obj.ObjectId, obj))
                    throw Error("$.equipments", $"duplicate equipment object ID {obj.ObjectId}");
            }
        if (roleEquipment is null || roleEquipment.Count == 0) return;
        if (roleEquipment.Count != RoleCount) throw Error("$.roleEquipment", $"expected exactly {RoleCount} rows");
        for (int role = 0; role < RoleCount; role++)
        {
            IReadOnlyList<int>? row = roleEquipment[role];
            if (row is null || row.Count != SlotCount) throw Error("$.roleEquipment", $"row {role} requires exactly {SlotCount} slots");
            for (int slot = 0; slot < SlotCount; slot++)
            {
                int objectId = row[slot];
                if (!Word(objectId)) throw Error($"$.roleEquipment[{role}][{slot}]", "expected integer in 0..65535");
                if (objectId != 0 && !declared.ContainsKey(objectId))
                    throw Error($"$.roleEquipment[{role}][{slot}]", $"equipment object {objectId} is not declared");
            }
        }
        foreach (BattleRoleSnapshot role in roles)
        {
            if (role is null) continue;
            if (!roleEquipment[role.RoleId].SequenceEqual(role.Equipment))
                throw Error($"$.roleEquipment[{role.RoleId}]", "declared slots must equal the role's equipment snapshot");
        }
    }

    public IReadOnlyList<int> Equipment(int roleId)
    {
        Role(roleId);
        return Array.AsReadOnly(Enumerable.Range(0, SlotCount).Select(slot => equipment[roleId, slot]).ToArray());
    }

    /// <summary>Counts inventory plus equipped copies on the explicit current party (PAL_CountItem,
    /// global.c:957). This is not the inventory-only shortage query and never consumes in-use stock.</summary>
    public int CountItem(int itemId, InventoryLiveState inventory, IReadOnlyList<int> partyRoleIds)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(partyRoleIds);
        if (!Word(itemId)) throw new ArgumentOutOfRangeException(nameof(itemId));
        // MAX_PLAYABLE_PLAYER_ROLES is the exploration-party limit, not combat capacity.
        if (partyRoleIds.Count > 5) throw new ArgumentException("Current party requires at most five roles.", nameof(partyRoleIds));
        var seen = new HashSet<int>();
        foreach (int roleId in partyRoleIds)
        {
            Role(roleId);
            if (!seen.Add(roleId)) throw new ArgumentException("Current party requires distinct role IDs.", nameof(partyRoleIds));
        }
        if (itemId == 0) return 0;
        int count = inventory.GetAmount(itemId);
        foreach (int roleId in partyRoleIds)
            for (int slot = 0; slot < SlotCount; slot++)
                if (equipment[roleId, slot] == itemId) count++;
        return count;
    }

    /// <summary>Retained field 4 WORD for one equipment register; null means no write, not an attack-all capability.</summary>
    public int? AttackAllWord(int roleId, int registerIndex)
    {
        Role(roleId);
        if (registerIndex is < 0 or >= RegisterCount)
            throw new ArgumentOutOfRangeException(nameof(registerIndex), registerIndex, "Equipment register index must be 0..6.");
        return attackAll[roleId, registerIndex];
    }

    /// <summary>Retained field 65 WORD for one equipment register; not a known magic or combat ability.</summary>
    public int? CooperativeMagicWord(int roleId, int registerIndex)
    {
        Role(roleId);
        if (registerIndex is < 0 or >= RegisterCount)
            throw new ArgumentOutOfRangeException(nameof(registerIndex), registerIndex, "Equipment register index must be 0..6.");
        return cooperativeMagic[roleId, registerIndex];
    }

    /// <summary>WORD sum over the equipment effect registers (global.c:1760/1825/1861); the extra register stays zero in this slice.</summary>
    public int AttackBonus(int roleId) => Bonus(attack, roleId);
    public int DefenseBonus(int roleId) => Bonus(defense, roleId);
    public int DexterityBonus(int roleId) => Bonus(dexterity, roleId);

    public int EffectiveAttack(int roleId, int baseAttack) => unchecked((ushort)(baseAttack + AttackBonus(roleId)));
    public int EffectiveDefense(int roleId, int baseDefense) => unchecked((ushort)(baseDefense + DefenseBonus(roleId)));
    public int EffectiveDexterity(int roleId, int baseDexterity) => unchecked((ushort)(baseDexterity + DexterityBonus(roleId)));
    /// <summary>Same register-sum rule for the optional stat-18/21/22 writes
    /// (PAL_GetPlayerMagicStrength/FleeRate/PoisonResistance, global.c:1768/1868/1900).</summary>
    public int MagicPowerBonus(int roleId) => Bonus(magicPower, roleId);
    public int FleeRateBonus(int roleId) => Bonus(fleeRate, roleId);
    public int PoisonResistanceBonus(int roleId) => Bonus(poisonResistance, roleId);
    public int EffectiveMagicPower(int roleId, int baseMagicPower) => unchecked((ushort)(baseMagicPower + MagicPowerBonus(roleId)));
    public int EffectiveFleeRate(int roleId, int baseFleeRate) => unchecked((ushort)(baseFleeRate + FleeRateBonus(roleId)));
    /// <summary>Panel projection only; the battle poison domain still does not consume it.</summary>
    public int EffectivePoisonResistance(int roleId, int basePoisonResistance) => unchecked((ushort)(basePoisonResistance + PoisonResistanceBonus(roleId)));

    /// <summary>Base panel plus equipment contribution, projected as the battle-consumable player record.</summary>
    public ClassicBattlePlayer EffectivePlayer(ClassicBattlePlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return player with
        {
            Attack = EffectiveAttack(player.RoleId, player.Attack),
            Defense = EffectiveDefense(player.RoleId, player.Defense),
            Dexterity = EffectiveDexterity(player.RoleId, player.Dexterity)
        };
    }

    /// <summary>The last non-zero equipment battle-sprite register wins (global.c:1998-2008).</summary>
    public int BattleSprite(int roleId, int baseBattleSprite)
    {
        Role(roleId);
        int sprite = baseBattleSprite;
        for (int slot = 0; slot < RegisterCount; slot++)
            if (battleSprite[roleId, slot] != 0) sprite = battleSprite[roleId, slot];
        return sprite;
    }

    /// <summary>True when any equipment battle-sprite register replaces the base sprite; the battle
    /// chain has no authored replacement sprite, so combat rejects such closures before side effects.</summary>
    public bool OverridesBattleSprite(int roleId)
    {
        Role(roleId);
        for (int slot = 0; slot < RegisterCount; slot++)
            if (battleSprite[roleId, slot] != 0) return true;
        return false;
    }

    /// <summary>Persistent equipment status words (>999) produced by the equip scripts for one role.</summary>
    public IReadOnlyList<int> StatusOverlay(int roleId)
    {
        Role(roleId);
        return Array.AsReadOnly(Enumerable.Range(0, StatusCount).Select(status => statuses[roleId, status]).ToArray());
    }

    /// <summary>Applies the persistent overlay onto nine snapshot words; good statuses only rise, never stack (global.c:2255).</summary>
    public IReadOnlyList<int> ApplyStatuses(int roleId, IReadOnlyList<int> playerStatuses)
    {
        Role(roleId);
        ArgumentNullException.ThrowIfNull(playerStatuses);
        if (playerStatuses.Count != StatusCount) throw Error("$.statuses", $"expected exactly {StatusCount} words");
        int[] result = playerStatuses.ToArray();
        for (int status = GoodStatusFirstIndex; status < StatusCount; status++)
            if (statuses[roleId, status] > result[status]) result[status] = statuses[roleId, status];
        return Array.AsReadOnly(result);
    }

    private int Bonus(int[,] register, int roleId)
    {
        Role(roleId);
        int total = 0;
        for (int slot = 0; slot < RegisterCount; slot++) total += register[roleId, slot];
        return unchecked((ushort)total);
    }

    private static void Role(int roleId)
    {
        if (roleId is < 0 or >= RoleCount) throw new ArgumentOutOfRangeException(nameof(roleId), roleId, "Role ID must be 0..5.");
    }

    private static bool Word(int value) => value is >= 0 and <= 65535;
    private static InvalidDataException Error(string path, string message) => new($"{path}: {message}.");
}
