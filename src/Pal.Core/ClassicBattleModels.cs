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
    public int? NameObjectId { get; init; }
    public int DyingScript { get; init; }
    public int FriendDeathScript { get; init; }
    public int? CoveredByRoleId { get; init; }
    /// <summary>The authored attack-all word (PLAYERROLES rgwAttackAll, data-schema "是否全体攻击"):
    /// the transportable capability declaration through the formal authored/native chain. The
    /// fixed reference engine itself scans only equipment effects (PAL_PlayerCanAttackAll,
    /// global.c 2048-2080); a declared true word here is an authored extension of that same
    /// capability, not retail engine behavior.</summary>
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
    /// <summary>PAL_StartBattle's zero source row (battle.c:1617-1717): a real runtime slot whose
    /// wObjectID stays 0. Only an empty slot admits the canonical id 0/hp 0 spelling; it never
    /// reads the enemy table, draws action random, runs hooks or contributes rewards. A killed
    /// non-empty enemy keeps its slot identity and stays IsEmpty=false.</summary>
    public bool IsEmpty { get; init; }
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
    /// <summary>True when this strike targeted every applicable enemy at once (fight.c 3686-3735):
    /// the record is one per-target result in reference hit order; Damage is the applied WORD
    /// delta (previous HP minus the float-truncated new HP), unlike the single-target branch's
    /// possibly-overkill raw strike value.</summary>
    public bool AttackAll { get; init; }
    /// <summary>True on both strikes of a dual attack action (kStatusDualAttack); presentation
    /// keeps a dual attack-all actor at its home position (fight.c 2080-2101).</summary>
    public bool DualAttack { get; init; }
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
    /// <summary>PAL_PlayerCanAttackAll (global.c 2048-2080): the authored player word or any
    /// non-zero equipment rgwAttackAll register grants the all-target attack selection.</summary>
    public bool CanAttackAll { get; init; }
    /// <summary>True when this member committed the round's all-target attack selection
    /// (reference sTarget -1); SelectedEnemySlot stays null for such a commitment.</summary>
    public bool SelectedAttackAll { get; init; }
}
public sealed record ClassicBattleEnemyView(int Slot, int Id, int Hp)
{
    /// <summary>True only for a source-empty runtime slot (wObjectID 0); a killed real enemy stays
    /// IsEmpty=false with its original battle id, so the two zero-HP kinds never merge.</summary>
    public bool IsEmpty { get; init; }
}
public sealed record ClassicBattleQueueEntry(ClassicBattleSide Side, int ActorIndex, ushort Dexterity);

/// <summary>One enemy's post-victory script. PAL_BattleWon runs wScriptOnBattleEnd for every enemy
/// slot in order with that slot as the caller (battle.c 1334-1337), between growth and recovery.</summary>
public sealed record ClassicBattleEndScript(int EnemySlot, int EnemyId, int ScriptAddress);

/// <summary>One role's victory growth facts: the level before/after the settlement and the magic
/// rows learned now (battle.c 1098-1330). Dead members carry unchanged levels and no magics.</summary>
public sealed record ClassicBattleRoleGrowth(int RoleId, int LevelBefore, int LevelAfter, IReadOnlyList<int> LearnedMagics);

/// <summary>The victory settlement facts published when growth settles, before the post-battle
/// scripts and the automatic recovery run: the granted totals (gpGlobals dwCash/iExpGained at
/// battle.c 1050-1054) and every party role's growth.</summary>
public sealed record ClassicBattleVictorySettlement(int ExperienceGranted, int CashGranted, IReadOnlyList<ClassicBattleRoleGrowth> Growth);
public sealed record ClassicBattleCounts(int Attack, int Health)
{
    /// <summary>Failed escape attempts this battle (fight.c 4171: rgFleeExp.wCount += 2 per attempt);
    /// the attack/health counters grow only on real attack actions.</summary>
    public int Flee { get; init; }
}
/// <summary>One resolved flee attempt (fight.c 4119-4173): the reference always consumes the
/// RandomLong(0, defense) draw, and a boss battle fails the escape regardless of the draw.</summary>
public sealed record ClassicBattleFleeAttempt(int Round, int PartyIndex, int RoleId, int FleeRate,
    int EnemyFleeDefense, int Draw, bool Escaped);
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
    /// <summary>The newest resolved flee attempt; published once per failed boss escape, replacing any prior attempt.</summary>
    public ClassicBattleFleeAttempt? LastFleeAttempt { get; init; }
    /// <summary>Per-target results of the newest presented strike in hit order (single-element for
    /// single-target and enemy actions); one all-target strike publishes one record per hit slot.</summary>
    public IReadOnlyList<ClassicBattleAction> LastActionTargets { get; init; } = [];
}
