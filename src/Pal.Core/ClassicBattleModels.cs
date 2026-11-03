namespace Pal.Core;

/// <summary>Inclusive SDLPal random ranges. Equal endpoints return that value without drawing entropy.</summary>
public interface IBattleRandom
{
    int NextInclusive(int from, int to);
    float NextInclusive(float from, float to);
}

public sealed record ClassicBattlePlayer(int RoleId, int Hp, int MaxHp, int Attack, int Defense, int Dexterity)
{
    public IReadOnlyList<int> Equipment { get; init; } = [];
    public IReadOnlyList<int> Statuses { get; init; } = [];
    public IReadOnlyList<int> Poisons { get; init; } = [];
    public int DyingScript { get; init; }
    public int FriendDeathScript { get; init; }
    public int? CoveredByRoleId { get; init; }
    public bool AttackAll { get; init; }
}

/// <summary>Enemy attack, defense and dexterity are signed table offsets, not effective panels.</summary>
public sealed record ClassicBattleEnemy(int Id, int Level, int Hp, int Attack, int Defense, int Dexterity, int PhysicalResistance)
{
    public IReadOnlyList<int> Statuses { get; init; } = [];
    public IReadOnlyList<int> Poisons { get; init; } = [];
    public int TurnStartScript { get; init; }
    public int ReadyScript { get; init; }
    public int BattleEndScript { get; init; }
    public int MagicId { get; init; }
    public int DualMove { get; init; }
    public int AttackEquivItem { get; init; }
    public int AttackEquivItemRate { get; init; }
    public int Experience { get; init; }
    public int Cash { get; init; }
}

public sealed record ClassicBattleDefinition(IReadOnlyList<ClassicBattlePlayer> Players, IReadOnlyList<ClassicBattleEnemy> Enemies);
public enum ClassicBattlePhase { Selecting, Resolving, Won, Lost }
public enum ClassicBattleSide { Player, Enemy }
public sealed record ClassicBattleAction(int Round, ClassicBattleSide Attacker, int TargetId, int Damage, bool Critical, bool Evaded)
{
    public int ActorIndex { get; init; }
    public int TargetIndex { get; init; }
}
public sealed record ClassicBattlePlayerView(int PartyIndex, int RoleId, int Hp, ClassicBattleCounts Counts, int? SelectedEnemySlot);
public sealed record ClassicBattleEnemyView(int Slot, int Id, int Hp);
public sealed record ClassicBattleQueueEntry(ClassicBattleSide Side, int ActorIndex, ushort Dexterity);
public sealed record ClassicBattleCounts(int Attack, int Health);
public sealed record ClassicBattleView(ClassicBattlePhase Phase, int Round, int PlayerHp, int EnemyHp,
    ClassicBattleCounts Counts, ClassicBattleAction? LastAction)
{
    // Positional HP/counts are compatibility projections of index zero, not party totals.
    public IReadOnlyList<ClassicBattlePlayerView> Players { get; init; } = [];
    public IReadOnlyList<ClassicBattleEnemyView> Enemies { get; init; } = [];
    public IReadOnlyList<ClassicBattleQueueEntry> ActionQueue { get; init; } = [];
    public int? SelectingPlayerIndex { get; init; }
}
