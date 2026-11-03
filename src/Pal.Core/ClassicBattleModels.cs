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
    /// <summary>Base magical-attack and flee panels; the #49 temporary percent boosts scale these
    /// (script.c 0x0030 reads the base PLAYERROLES words), so combat content declares them.</summary>
    public int MagicPower { get; init; }
    public int FleeRate { get; init; }
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
public enum ClassicBattlePhase { Selecting, Resolving, Won, Lost, RoleHook }
public enum ClassicBattleSide { Player, Enemy }
public enum ClassicBattleHookKind { Dying, FriendDeath }
/// <summary>One mid-battle role script paused on the fight.c 768-880 post-action check.
/// Dying hooks carry the dying member's own script and role; friend-death hooks carry the
/// coverer's script, party index and role (the caller PAL_RunTriggerScript receives).</summary>
public sealed record ClassicBattleRoleHook(ClassicBattleHookKind Kind, int PartyIndex, int RoleId, int ScriptAddress);
public sealed record ClassicBattleAction(int Round, ClassicBattleSide Attacker, int TargetId, int Damage, bool Critical, bool Evaded)
{
    public int ActorIndex { get; init; }
    public int TargetIndex { get; init; }
    /// <summary>True on the second strike of a dual attack; one queue action still counts once (fight.c:3628).</summary>
    public bool SecondStrike { get; init; }
    /// <summary>Party index of the member that covered the enemy strike (fight.c:4959-4973); null when nobody covered.</summary>
    public int? CovererPartyIndex { get; init; }
}
public sealed record ClassicBattlePlayerView(int PartyIndex, int RoleId, int Hp, ClassicBattleCounts Counts, int? SelectedEnemySlot)
{
    /// <summary>The effective panel the session consumes (equipment and pending boosts included); published per party member.</summary>
    public int Attack { get; init; }
    public int Defense { get; init; }
    public int Dexterity { get; init; }
    /// <summary>Battle-owned temporary percent boosts applied by role-hook scripts (script.c 0x0030,
    /// kBodyPartExtra register). MagicPower/Flee are published as applied boosts; combat actions consume
    /// attack/defense/dexterity, and every boost is dropped with this session at battle end.</summary>
    public int BoostAttack { get; init; }
    public int BoostMagicPower { get; init; }
    public int BoostDefense { get; init; }
    public int BoostDexterity { get; init; }
    public int BoostFlee { get; init; }
}
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
    /// <summary>The role script paused on the post-action check; non-null only in the RoleHook phase.</summary>
    public ClassicBattleRoleHook? PendingRoleHook { get; init; }
}
