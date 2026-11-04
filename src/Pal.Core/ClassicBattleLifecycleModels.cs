namespace Pal.Core;

public sealed record BattleLevelUpMagic(int Level, int MagicId);

/// <summary>Shared level-indexed EXP table (0..99), and the complete learnable-magic rows for this role.</summary>
public sealed record ClassicBattleGrowthData(IReadOnlyList<int> LevelUpExperience, IReadOnlyList<BattleLevelUpMagic> LevelUpMagics);

public sealed record ClassicBattleLifecycleView
{
    public bool IsInBattle { get; }
    public IReadOnlyDictionary<int, BattleRoleSnapshot> Roles { get; }
    public ClassicBattleView Combat { get; }
    public BattleRoleSnapshot Role => Roles.Count == 1 ? Roles.Values.Single()
        : throw new InvalidOperationException("Use Roles for a multiplayer lifecycle.");
    public ClassicBattleLifecycleView(bool isInBattle, BattleRoleSnapshot role, ClassicBattleView combat)
        : this(isInBattle, new System.Collections.ObjectModel.ReadOnlyDictionary<int, BattleRoleSnapshot>(
            new Dictionary<int, BattleRoleSnapshot> { [role.RoleId] = role }), combat) { }
    public ClassicBattleLifecycleView(bool isInBattle, IReadOnlyDictionary<int, BattleRoleSnapshot> roles, ClassicBattleView combat)
    { IsInBattle = isInBattle; Roles = roles; Combat = combat; }
}
