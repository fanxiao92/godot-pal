namespace Pal.Core;

/// <summary>Classic physical combat. The host owns advancement; no presentation clock is consulted.</summary>
public sealed class ClassicBattleSession
{
    private readonly ClassicBattlePlayer player;
    private readonly ClassicBattleEnemy enemy;
    private readonly IBattleRandom random;
    private bool playerFirst;
    private int actionIndex;
    public ClassicBattleView Current { get; private set; }
    public CompletionReceipt? Completion { get; private set; }

    public ClassicBattleSession(ClassicBattleDefinition definition, IBattleRandom random)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(random);
        Validate(definition);
        this.random = random;
        player = definition.Players[0];
        enemy = definition.Enemies[0];
        Current = new(ClassicBattlePhase.Selecting, 0, player.Hp, enemy.Hp, new(0, 0), null);
    }

    public void SelectAttack(int targetId)
    {
        if (Current.Phase != ClassicBattlePhase.Selecting) throw new InvalidOperationException("This battle is not selecting an action.");
        if (targetId != enemy.Id) throw new ArgumentOutOfRangeException(nameof(targetId));
        // SDLPal fight.c 1462-1578: enemies enter the queue first; strict comparison retains ties.
        int enemySpeed = unchecked((ushort)((enemy.Level + 6) * 3 + enemy.Dexterity));
        enemySpeed = unchecked((ushort)(enemySpeed * random.NextInclusive(.9f, 1.1f)));
        int playerSpeed = Math.Min(player.Dexterity, 999);
        if (Current.PlayerHp < Math.Min(100, player.MaxHp / 5)) playerSpeed /= 2;
        playerSpeed = unchecked((ushort)(playerSpeed * random.NextInclusive(.9f, 1.1f)));
        playerFirst = (short)playerSpeed > (short)enemySpeed;
        actionIndex = 0;
        Current = Current with { Phase = ClassicBattlePhase.Resolving, Round = Current.Round + 1 };
    }

    public bool ResolveNextAction()
    {
        if (Current.Phase != ClassicBattlePhase.Resolving) return false;
        if ((actionIndex == 0) == playerFirst) AttackEnemy();
        else AttackPlayer();
        actionIndex++;
        if (Current.EnemyHp == 0) Finish(ClassicBattlePhase.Won);
        else if (Current.PlayerHp == 0) Finish(ClassicBattlePhase.Lost);
        else if (actionIndex == 2) Current = Current with { Phase = ClassicBattlePhase.Selecting };
        return true;
    }

    private void AttackEnemy()
    {
        int defense = unchecked((ushort)(enemy.Defense + (enemy.Level + 6) * 4));
        short damage = BaseDamage(player.Attack, defense);
        if (enemy.PhysicalResistance != 0) damage = (short)(damage / enemy.PhysicalResistance);
        damage = unchecked((short)(damage + random.NextInclusive(1, 2)));
        bool critical = random.NextInclusive(0, 5) == 0;
        if (critical) damage = unchecked((short)(damage * 3));
        if (player.RoleId == 0 && random.NextInclusive(0, 11) == 0)
        {
            damage = unchecked((short)(damage * 2));
            critical = true;
        }
        damage = unchecked((short)(damage * random.NextInclusive(1f, 1.125f)));
        if (damage <= 0) damage = 1;
        Current = Current with
        {
            EnemyHp = Math.Max(0, Current.EnemyHp - damage),
            Counts = new(unchecked((ushort)(Current.Counts.Attack + 1)),
                unchecked((ushort)(Current.Counts.Health + random.NextInclusive(2, 3)))),
            LastAction = new(Current.Round, ClassicBattleSide.Player, enemy.Id, damage, critical, false)
        };
    }

    private void AttackPlayer()
    {
        // fight.c 4540, 4938, 5056-5074, 5139: even a zero item rate draws 1..10 on a hit.
        _ = random.NextInclusive(0, 0);
        bool evaded = random.NextInclusive(0, 16) >= 10;
        int damage = 0;
        if (!evaded)
        {
            int strength = Math.Max(0, enemy.Attack + (enemy.Level + 6) * 6);
            short hit = BaseDamage(unchecked((ushort)(strength + random.NextInclusive(0, 2))), player.Defense);
            hit = (short)(hit / 2);
            hit = unchecked((short)(hit + random.NextInclusive(0, 1)));
            damage = Math.Max(1, Math.Min(Current.PlayerHp, hit));
            _ = random.NextInclusive(1, 10);
        }
        Current = Current with
        {
            PlayerHp = Current.PlayerHp - damage,
            LastAction = new(Current.Round, ClassicBattleSide.Enemy, player.RoleId, damage, false, evaded)
        };
    }

    private static short BaseDamage(int attack, int defense) => attack > defense
        ? unchecked((short)(attack * 2 - defense * 1.6 + .5))
        : attack > defense * .6 ? unchecked((short)(attack - defense * .6 + .5)) : (short)0;

    internal static void Validate(ClassicBattleDefinition definition)
    {
        if (definition.Players is not { Count: 1 } || definition.Enemies is not { Count: 1 }
            || definition.Players[0] is not { } p || definition.Enemies[0] is not { } e)
            throw new InvalidDataException("Classic combat currently requires one living player and one living enemy.");
        if (p.RoleId is < 0 or > 5 || p.Hp is < 1 or > 32767 || p.MaxHp is < 1 or > 65535 || p.Hp > p.MaxHp
            || !Word(p.Attack) || !Word(p.Defense) || !Word(p.Dexterity)
            || e.Id is < 1 or > 65535 || !Word(e.Level) || e.Hp is < 1 or > 32767
            || !SignedWord(e.Attack) || !SignedWord(e.Defense) || !SignedWord(e.Dexterity) || !Word(e.PhysicalResistance))
            throw new InvalidDataException("Classic combat has invalid or unsupported numeric data (current HP must be 1..32767).");
        if (!Zero(p.Equipment, 6) || !Zero(p.Statuses, 9) || !Zero(p.Poisons)
            || p.DyingScript != 0 || p.FriendDeathScript != 0 || p.CoveredByRoleId is not null || p.AttackAll
            || !Zero(e.Statuses, 9) || !Zero(e.Poisons) || e.TurnStartScript != 0 || e.ReadyScript != 0 || e.BattleEndScript != 0
            || e.MagicId != 0 || e.DualMove != 0 || e.AttackEquivItem != 0 || e.AttackEquivItemRate != 0
            || e.Experience != 0 || e.Cash != 0)
            throw new InvalidDataException("Classic combat does not yet support equipment, statuses, poisons, hooks, cover, attack-all, magic, dual moves, equivalent items or rewards.");
    }

    private static bool Word(int value) => value is >= 0 and <= 65535;
    private static bool SignedWord(int value) => value is >= -32768 and <= 32767;
    private static bool Zero(IReadOnlyList<int>? values, int? width = null) => values is not null
        && (width is null || values.Count == 0 || values.Count == width) && values.All(value => value == 0);

    private void Finish(ClassicBattlePhase outcome)
    {
        Current = Current with { Phase = outcome };
        Completion = new(this);
    }

    /// <summary>Created only by a finished session. Consumers must also match the active session's exact receipt.</summary>
    public sealed class CompletionReceipt
    {
        public ClassicBattlePhase Outcome { get; }
        internal CompletionReceipt(ClassicBattleSession session) => Outcome = session.Current.Phase;
    }
}
