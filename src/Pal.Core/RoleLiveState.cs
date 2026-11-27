using System.Collections.ObjectModel;

namespace Pal.Core;

/// <summary>Host-owned live role records, seeded once. Role identity is independent of party
/// order or combat capacity; outer content loading owns reconstruction and rebinding.</summary>
public sealed class RoleLiveState
{
    private readonly Dictionary<int, BattleRoleSnapshot> roles;
    public IReadOnlyDictionary<int, BattleRoleSnapshot> Roles { get; }

    public RoleLiveState(IReadOnlyCollection<BattleRoleSnapshot> initialRoles)
    {
        ArgumentNullException.ThrowIfNull(initialRoles);
        roles = new();
        foreach (var role in initialRoles)
        {
            if (role is null || role.RoleId is < 0 or > 5 || roles.ContainsKey(role.RoleId)
                || !Word(role.Hp) || !Word(role.MaxHp) || !Word(role.Mp) || !Word(role.MaxMp)
                || role.Hp > role.MaxHp || role.Mp > role.MaxMp)
                throw new InvalidDataException("Live roles require unique RoleId 0..5 and valid HP/MP WORD values.");
            roles.Add(role.RoleId, BattleRoleSnapshotHelper.Freeze(role));
        }
        Roles = new ReadOnlyDictionary<int, BattleRoleSnapshot>(roles);
    }

    internal void SetVitals(int roleId, int hp, int mp) => roles[roleId] = roles[roleId] with { Hp = hp, Mp = mp };

    // Freeze and validate every participant before publishing any row; spectators remain untouched.
    internal void MergeSettledRoles(IReadOnlyCollection<BattleRoleSnapshot> settledRoles)
    {
        var settled = new RoleLiveState(settledRoles);
        if (settled.Roles.Keys.Any(roleId => !roles.ContainsKey(roleId)))
            throw new InvalidDataException("Settled roles must already belong to the live owner.");
        foreach (var (roleId, role) in settled.Roles) roles[roleId] = role;
    }

    private static bool Word(int value) => value is >= 0 and <= 65535;
}
