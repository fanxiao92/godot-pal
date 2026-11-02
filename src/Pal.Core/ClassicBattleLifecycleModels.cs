namespace Pal.Core;

public sealed record BattleLevelUpMagic(int Level, int MagicId);

/// <summary>Shared level-indexed EXP table (0..99), and the complete learnable-magic rows for this role.</summary>
public sealed record ClassicBattleGrowthData(IReadOnlyList<int> LevelUpExperience, IReadOnlyList<BattleLevelUpMagic> LevelUpMagics);

public sealed record ClassicBattleLifecycleView(bool IsInBattle, BattleRoleSnapshot Role, ClassicBattleView Combat);
