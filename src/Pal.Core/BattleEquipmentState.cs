using System.Collections.ObjectModel;

namespace Pal.Core;

/// <summary>Per-role effect row of one equipment object's equip script (global.c script 0x0017/0x001A/0x002D word writes).</summary>
public sealed record BattleEquipmentEffect(int Attack, int Defense, int Dexterity, int BattleSprite, IReadOnlyList<int> Statuses);

/// <summary>Authored equip-script closure for one object: exactly six role-indexed effect rows (ObjectId is 1..65535).</summary>
public sealed record BattleEquipmentObject(int ObjectId, IReadOnlyList<BattleEquipmentEffect> Effects);

/// <summary>Formal six-role equipment initialization (PAL_UpdateEquipments, global.c:1352) and effective panel
/// projections (PAL_GetPlayerAttackStrength/Defense/Dexterity global.c:1736/1800/1832 and PAL_GetPlayerBattleSprite
/// global.c:1978-2008). Pure data; battle execution semantics are delivered by a later ticket and are not wired here.</summary>
public sealed class BattleEquipmentState
{
    public const int RoleCount = 6;   // MAX_PLAYER_ROLES (palcommon.h)
    public const int SlotCount = 6;   // MAX_PLAYER_EQUIPMENTS (kBodyPartHead..kBodyPartWear)
    public const int RegisterCount = SlotCount + 1; // kBodyPartExtra register; stays zero until temporary effects are delivered
    public const int StatusCount = 9; // kStatusAll
    internal const int PersistentStatusFloor = 1000; // PAL_RemovePlayerStatus keeps values above 999 (global.c:2297)
    internal const int GoodStatusFirstIndex = 5;     // bravery, protect, haste, dualAttack (global.h tagSTATUS)

    private readonly int[,] equipment = new int[RoleCount, SlotCount];
    private readonly int[,] attack = new int[RoleCount, RegisterCount];
    private readonly int[,] defense = new int[RoleCount, RegisterCount];
    private readonly int[,] dexterity = new int[RoleCount, RegisterCount];
    private readonly int[,] battleSprite = new int[RoleCount, RegisterCount];
    private readonly int[,] statuses = new int[RoleCount, StatusCount];

    private BattleEquipmentState() { }

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

    /// <summary>Clears all registers, then applies every equipped object's effect row to its (role, slot) context.</summary>
    public static BattleEquipmentState Initialize(IReadOnlyList<IReadOnlyList<int>> roleEquipment,
        IReadOnlyDictionary<int, BattleEquipmentObject> equipmentObjects)
    {
        ArgumentNullException.ThrowIfNull(roleEquipment);
        ArgumentNullException.ThrowIfNull(equipmentObjects);
        ValidateEquipmentClosure(roleEquipment, equipmentObjects.Values.ToList(), []);
        var state = new BattleEquipmentState();
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

    /// <summary>WORD sum over the equipment effect registers (global.c:1760/1825/1861); the extra register stays zero in this slice.</summary>
    public int AttackBonus(int roleId) => Bonus(attack, roleId);
    public int DefenseBonus(int roleId) => Bonus(defense, roleId);
    public int DexterityBonus(int roleId) => Bonus(dexterity, roleId);

    public int EffectiveAttack(int roleId, int baseAttack) => unchecked((ushort)(baseAttack + AttackBonus(roleId)));
    public int EffectiveDefense(int roleId, int baseDefense) => unchecked((ushort)(baseDefense + DefenseBonus(roleId)));
    public int EffectiveDexterity(int roleId, int baseDexterity) => unchecked((ushort)(baseDexterity + DexterityBonus(roleId)));

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
