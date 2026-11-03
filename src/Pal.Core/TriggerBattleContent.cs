using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Pal.Core;

/// <summary>Role-keyed numeric content; party order is Players and enemy identity is its team slot.
/// Subsequent battles use settled roles, never the initial player HP.</summary>
public sealed class TriggerBattleContent
{
    public IReadOnlyDictionary<int, BattleRoleSnapshot> InitialRoles { get; }
    public IReadOnlyList<ClassicBattlePlayer> Players { get; }
    public IReadOnlyDictionary<int, ClassicBattleGrowthData> GrowthByRole { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<ClassicBattleEnemy>> EnemyTeams { get; }

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
        IReadOnlyDictionary<int, IReadOnlyList<ClassicBattleEnemy>> teams)
    {
        ArgumentNullException.ThrowIfNull(roles); ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(growth); ArgumentNullException.ThrowIfNull(teams);
        if (roles.Count is < 1 or > 2 || players.Count != roles.Count || roles.Any(x => x is null)
            || players.Any(x => x is null) || roles.Select(x => x.RoleId).Distinct().Count() != roles.Count
            || players.Select(x => x.RoleId).Distinct().Count() != players.Count
            || !roles.Select(x => x.RoleId).ToHashSet().SetEquals(players.Select(x => x.RoleId))
            || !growth.Keys.ToHashSet().SetEquals(players.Select(x => x.RoleId)) || growth.Values.Any(x => x is null))
            throw new InvalidDataException("roles[0], players[0] and growth[0] require unique matching RoleId keys and one or two roles.");
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
            if (x.Value is null || x.Value.Count is < 1 or > 2 || x.Value.Any(e => e is null))
                throw new InvalidDataException("Each team requires one or two nonnull enemy slots.");
            return (IReadOnlyList<ClassicBattleEnemy>)Array.AsReadOnly(x.Value.Select(e => e with { Statuses = Copy(e.Statuses), Poisons = Copy(e.Poisons) }).ToArray());
        }));
    }

    internal IReadOnlyList<ClassicBattleEnemy> Enemies(int teamId) => EnemyTeams.TryGetValue(teamId, out var enemies) ? enemies
        : throw new InvalidDataException($"Missing battle team {teamId}.");

    internal IReadOnlyList<ClassicBattlePlayer> SettledPlayers(IReadOnlyDictionary<int, BattleRoleSnapshot> roles) =>
        Players.Select(x => x with { Hp = roles[x.RoleId].Hp }).ToArray();

    internal void Validate(int teamId, IReadOnlyDictionary<int, BattleRoleSnapshot> roles)
    {
        if (roles.Values.Any(x => x.MaxHp > 32767))
            throw new InvalidDataException("Trigger battle content requires maxHP <= 32767 for subsequent battles.");
        ClassicBattleLifecycleSession.Validate(roles.Values.ToArray(), SettledPlayers(roles), Enemies(teamId), GrowthByRole);
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

public sealed record TriggerBattleRequest(Guid Id, int Caller, TriggerPosition Position, int TeamId, bool IsBoss,
    int NextAddress, int? LossAddress, int? FleeAddress, ClassicBattleLifecycleSession Lifecycle);
