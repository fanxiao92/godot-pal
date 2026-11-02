using System.Collections.ObjectModel;

namespace Pal.Core;

/// <summary>Explicit single-role battle content. The player HP is checked once against the initial role;
/// subsequent battles use the trigger session's settled role, never this initial HP.</summary>
public sealed class TriggerBattleContent
{
    public BattleRoleSnapshot InitialRole { get; }
    public ClassicBattlePlayer Player { get; }
    public ClassicBattleGrowthData Growth { get; }
    public IReadOnlyDictionary<int, ClassicBattleEnemy> Teams { get; }

    public TriggerBattleContent(BattleRoleSnapshot role, ClassicBattlePlayer player, ClassicBattleGrowthData growth,
        IReadOnlyDictionary<int, ClassicBattleEnemy> teams)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(growth);
        ArgumentNullException.ThrowIfNull(teams);
        if (role.RoleId != player.RoleId || role.Hp != player.Hp || role.MaxHp != player.MaxHp)
            throw new InvalidDataException("Initial role and combat player must have the same role, HP and maxHP.");
        InitialRole = role with
        {
            HiddenExperience = new ReadOnlyDictionary<string, BattleExperienceSnapshot>(role.HiddenExperience.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal)),
            Equipment = Copy(role.Equipment), Magics = Copy(role.Magics), Statuses = Copy(role.Statuses)
        };
        Player = player with { Equipment = Copy(player.Equipment), Statuses = Copy(player.Statuses), Poisons = Copy(player.Poisons) };
        Growth = new(Copy(growth.LevelUpExperience), Array.AsReadOnly(growth.LevelUpMagics.ToArray()));
        Teams = new ReadOnlyDictionary<int, ClassicBattleEnemy>(teams.ToDictionary(x => x.Key, x => x.Value with
        {
            Statuses = Copy(x.Value.Statuses), Poisons = Copy(x.Value.Poisons)
        }));
    }

    internal ClassicBattleEnemy Enemy(int teamId) => Teams.TryGetValue(teamId, out var enemy) ? enemy
        : throw new InvalidDataException($"Missing battle team {teamId}.");

    internal void Validate(int teamId, BattleRoleSnapshot role)
    {
        // This VM promises the complete multi-battle closure. Half recovery must remain in combat's HP domain.
        if (role.MaxHp > 32767) throw new InvalidDataException("Trigger battle content requires maxHP <= 32767 for subsequent battles.");
        ClassicBattleLifecycleSession.Validate(role, Player with { Hp = role.Hp }, Enemy(teamId), Growth);
    }

    private static IReadOnlyList<int> Copy(IReadOnlyList<int> source) => Array.AsReadOnly(source.ToArray());
}

public sealed record TriggerBattleRequest(Guid Id, int Caller, TriggerPosition Position, int TeamId, bool IsBoss,
    int NextAddress, int? LossAddress, int? FleeAddress, ClassicBattleLifecycleSession Lifecycle);
