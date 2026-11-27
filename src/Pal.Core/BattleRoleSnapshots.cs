using System.Collections.ObjectModel;

namespace Pal.Core;

public sealed record BattleExperienceSnapshot(int Value, int Level, int Count);

public sealed record BattleRoleSnapshot(
    int RoleId, int Level, int Hp, int MaxHp, int Mp, int MaxMp,
    BattleExperienceSnapshot PrimaryExperience,
    IReadOnlyDictionary<string, BattleExperienceSnapshot> HiddenExperience,
    IReadOnlyList<int> Equipment, IReadOnlyList<int> Magics, IReadOnlyList<int> Statuses, int CoveredByRoleId,
    int Attack = 0, int MagicPower = 0, int Defense = 0, int Dexterity = 0, int FleeRate = 0);

public sealed class BattleExperienceState
{
    public int Value { get; set; }
    public int Level { get; set; }
    public int Count { get; set; }
    internal BattleExperienceState(BattleExperienceSnapshot source) => (Value, Level, Count) = (source.Value, source.Level, source.Count);
}

public sealed class BattleRoleState
{
    public int RoleId { get; }
    public int Level { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public int Mp { get; set; }
    public int MaxMp { get; set; }
    /// <summary>The five base PLAYERROLES panel words PAL_PlayerLevelUp and the hidden tracks grow
    /// (global.c 2347, battle.c 1258). Absent declarations carry zero, like the combat player's
    /// optional magicPower/fleeRate words.</summary>
    public int Attack { get; set; }
    public int MagicPower { get; set; }
    public int Defense { get; set; }
    public int Dexterity { get; set; }
    public int FleeRate { get; set; }
    public BattleExperienceState PrimaryExperience { get; }
    public Dictionary<string, BattleExperienceState> HiddenExperience { get; }
    public int[] Equipment { get; }
    public int[] Magics { get; set; }
    public int[] Statuses { get; }
    public int CoveredByRoleId { get; set; }

    internal BattleRoleState(BattleRoleSnapshot source)
    {
        (RoleId, Level, Hp, MaxHp, Mp, MaxMp, CoveredByRoleId) =
            (source.RoleId, source.Level, source.Hp, source.MaxHp, source.Mp, source.MaxMp, source.CoveredByRoleId);
        (Attack, MagicPower, Defense, Dexterity, FleeRate) =
            (source.Attack, source.MagicPower, source.Defense, source.Dexterity, source.FleeRate);
        PrimaryExperience = new(source.PrimaryExperience);
        HiddenExperience = source.HiddenExperience.ToDictionary(pair => pair.Key, pair => new BattleExperienceState(pair.Value), StringComparer.Ordinal);
        Equipment = source.Equipment.ToArray();
        Magics = source.Magics.ToArray();
        Statuses = source.Statuses.ToArray();
    }
}

internal static class BattleRoleSnapshotHelper
{
    internal static BattleRoleSnapshot Freeze(BattleRoleSnapshot role) => role with
    {
        HiddenExperience = new ReadOnlyDictionary<string, BattleExperienceSnapshot>(role.HiddenExperience.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal)),
        Equipment = Copy(role.Equipment),
        Magics = Copy(role.Magics),
        Statuses = Copy(role.Statuses)
    };

    internal static IReadOnlyList<int> Copy(IReadOnlyList<int> source) =>
        source is null ? throw new InvalidDataException("Required numeric array is null.") : Array.AsReadOnly(source.ToArray());
}
