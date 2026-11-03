namespace Pal.Core;

/// <summary>Classic physical combat. The host owns advancement; no presentation clock is consulted.</summary>
public sealed class ClassicBattleSession
{
    private const int StatusConfused = 0, StatusDisabled = 1, StatusSleep = 2, StatusBravery = 5, StatusProtect = 6,
        StatusHaste = 7, StatusDualAttack = 8;
    private readonly ClassicBattlePlayer[] players;
    private readonly ClassicBattleEnemy[] enemies;
    private readonly int[][] playerStatuses;
    private readonly int[] playerHp;
    private readonly int[] enemyHp;
    private readonly ClassicBattleCounts[] counts;
    private readonly int?[] targets;
    private ClassicBattleQueueEntry[] queue = [];
    private readonly IBattleRandom random;
    private int actionIndex;
    private bool pendingSecondStrike;
    public ClassicBattleView Current { get; private set; }
    public CompletionReceipt? Completion { get; private set; }

    public ClassicBattleSession(ClassicBattleDefinition definition, IBattleRandom random, BattleEquipmentState? equipment = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(random);
        Validate(definition, equipment);
        this.random = random;
        players = definition.Players.ToArray();
        enemies = definition.Enemies.ToArray();
        // The declared equip-script closure is the formal execution semantics of the worn objects
        // (global.c PAL_UpdateEquipments re-runs the scripts): the session consumes the projected
        // effective panel and statuses, never the raw source opcodes.
        playerStatuses = new int[players.Length][];
        for (int i = 0; i < players.Length; i++)
        {
            if (equipment is { Declared: true })
            {
                players[i] = equipment.EffectivePlayer(players[i]);
                // An empty status list is the all-zero nine-word row (ValidatePlayer accepts both spellings).
                playerStatuses[i] = equipment.ApplyStatuses(players[i].RoleId,
                    players[i].Statuses.Count == 0 ? new int[BattleEquipmentState.StatusCount] : players[i].Statuses).ToArray();
            }
            else playerStatuses[i] = players[i].Statuses.Count == 0
                ? new int[BattleEquipmentState.StatusCount] : players[i].Statuses.ToArray();
        }
        playerHp = players.Select(p => p.Hp).ToArray();
        enemyHp = enemies.Select(e => e.Hp).ToArray();
        counts = players.Select(_ => new ClassicBattleCounts(0, 0)).ToArray();
        targets = new int?[players.Length];
        Current = new(ClassicBattlePhase.Selecting, 0, playerHp[0], enemyHp[0], counts[0], null);
        Publish();
    }

    /// <summary>Legacy ID selection is unambiguous only for one player and one enemy.</summary>
    public void SelectAttack(int targetId)
    {
        if (players.Length != 1 || enemies.Length != 1)
            throw new InvalidOperationException("Party combat requires explicit party index and enemy slot.");
        if (Current.Phase != ClassicBattlePhase.Selecting) throw new InvalidOperationException("This battle is not selecting an action.");
        if (targetId != enemies[0].Id) throw new ArgumentOutOfRangeException(nameof(targetId));
        SelectAttack(0, 0);
    }

    public void SelectAttack(int partyIndex, int enemySlot)
    {
        if (Current.Phase != ClassicBattlePhase.Selecting) throw new InvalidOperationException("This battle is not selecting an action.");
        if (partyIndex != Current.SelectingPlayerIndex) throw new ArgumentOutOfRangeException(nameof(partyIndex));
        if (enemySlot < 0 || enemySlot >= enemies.Length || enemyHp[enemySlot] == 0)
            throw new ArgumentOutOfRangeException(nameof(enemySlot));
        targets[partyIndex] = enemySlot;
        if (!Enumerable.Range(0, players.Length).Any(i => playerHp[i] > 0 && targets[i] is null))
        {
            BuildQueue();
            Current = Current with { Phase = ClassicBattlePhase.Resolving, Round = Current.Round + 1 };
        }
        Publish();
    }

    private void BuildQueue()
    {
        var entries = new List<ClassicBattleQueueEntry>();
        // fight.c 1462-1578: enemy slots first, then party indices; WORD before SHORT comparison.
        for (int i = 0; i < enemies.Length; i++)
        {
            if (enemyHp[i] == 0) continue;
            var enemy = enemies[i];
            int speed = unchecked((ushort)((enemy.Level + 6) * 3 + enemy.Dexterity));
            entries.Add(new(ClassicBattleSide.Enemy, i, unchecked((ushort)(speed * random.NextInclusive(.9f, 1.1f)))));
        }
        for (int i = 0; i < players.Length; i++)
        {
            var player = players[i];
            int speed = player.Dexterity;
            // fight.c:336-349: haste triples the actual dexterity (CLASSIC) before the 999 cap.
            if (playerStatuses[i][StatusHaste] > 0) speed = unchecked((ushort)(speed * 3));
            speed = Math.Min(speed, 999);
            if (playerHp[i] < Math.Min(100, player.MaxHp / 5)) speed /= 2;
            entries.Add(new(ClassicBattleSide.Player, i, playerHp[i] == 0 ? (ushort)0
                : unchecked((ushort)(speed * random.NextInclusive(.9f, 1.1f)))));
        }
        // battle.h: MAX_PLAYERS_IN_PARTY(3) + MAX_ENEMIES_IN_TEAM(5) * 2.
        // Empty slots participate as SHORT -1, ahead of negative-speed actors.
        const int referenceQueueCapacity = 3 + 5 * 2;
        queue = Enumerable.Repeat(new ClassicBattleQueueEntry(ClassicBattleSide.Enemy, -1, ushort.MaxValue), referenceQueueCapacity).ToArray();
        entries.CopyTo(queue);
        // Not a stable sort: a later faster entry can displace an earlier tie.
        for (int i = 0; i < queue.Length; i++)
            for (int j = i; j < queue.Length; j++)
                if ((short)queue[i].Dexterity < (short)queue[j].Dexterity)
                    (queue[i], queue[j]) = (queue[j], queue[i]);
        // fight.c 1599-1601 ends the round at the first FFFF, including an actor's WORD collision.
        // The public queue contains only the executable prefix, never synthetic empty actors.
        queue = queue.TakeWhile(entry => entry.Dexterity != ushort.MaxValue).ToArray();
        actionIndex = 0;
    }

    public bool ResolveNextAction()
    {
        if (Current.Phase != ClassicBattlePhase.Resolving) return false;
        var entry = queue[actionIndex];
        if (entry.Side == ClassicBattleSide.Player)
        {
            if (playerHp[entry.ActorIndex] > 0)
            {
                // fight.c:3628: kStatusDualAttack doubles the strike loop of one attack action.
                if (pendingSecondStrike)
                {
                    pendingSecondStrike = false;
                    AttackEnemy(entry.ActorIndex, secondStrike: true);
                    CompletePlayerAction(entry.ActorIndex);
                    actionIndex++;
                }
                else if (playerStatuses[entry.ActorIndex][StatusDualAttack] > 0)
                {
                    pendingSecondStrike = true;
                    AttackEnemy(entry.ActorIndex, secondStrike: false);
                }
                else
                {
                    AttackEnemy(entry.ActorIndex, secondStrike: false);
                    CompletePlayerAction(entry.ActorIndex);
                    actionIndex++;
                }
            }
            else actionIndex++;
        }
        else
        {
            if (enemyHp[entry.ActorIndex] > 0) AttackPlayer(entry.ActorIndex);
            actionIndex++;
        }
        // A dual attack is one action: outcome and the next round only settle after both strikes,
        // so a first-strike kill does not skip the second strike or end the battle early.
        if (pendingSecondStrike) { Publish(); return true; }
        if (enemyHp.All(hp => hp == 0)) Finish(ClassicBattlePhase.Won);
        else if (playerHp.All(hp => hp == 0)) Finish(ClassicBattlePhase.Lost);
        else if (actionIndex == queue.Length)
        {
            Array.Clear(targets);
            queue = [];
            Current = Current with { Phase = ClassicBattlePhase.Selecting };
        }
        Publish();
        return true;
    }

    private void Publish()
    {
        Current = Current with
        {
            PlayerHp = playerHp[0], EnemyHp = enemyHp[0], Counts = counts[0],
            Players = Array.AsReadOnly(players.Select((p, i) => new ClassicBattlePlayerView(i, p.RoleId, playerHp[i], counts[i], targets[i])
                { Attack = p.Attack, Defense = p.Defense, Dexterity = p.Dexterity }).ToArray()),
            Enemies = Array.AsReadOnly(enemies.Select((e, i) => new ClassicBattleEnemyView(i, e.Id, enemyHp[i])).ToArray()),
            ActionQueue = Array.AsReadOnly(queue.ToArray()),
            SelectingPlayerIndex = Current.Phase == ClassicBattlePhase.Selecting
                ? Enumerable.Range(0, players.Length).Where(i => playerHp[i] > 0 && targets[i] is null).Select(i => (int?)i).FirstOrDefault() : null
        };
    }

    private void AttackEnemy(int partyIndex, bool secondStrike)
    {
        var player = players[partyIndex];
        int slot = targets[partyIndex]!.Value;
        // SelectAutoTargetFrom: fixed reference leaves iPrevEnemyTarget at its initial -1. The
        // reference dual loop keeps the validated slot for its second strike even after the first
        // strike kills it (fight.c:3628-3673); HP is simply not reduced below zero again.
        if (!secondStrike)
        {
            while (enemyHp[slot] == 0) slot = (slot + 1) % enemies.Length;
            targets[partyIndex] = slot;
        }
        var enemy = enemies[slot];
        int defense = unchecked((ushort)(enemy.Defense + (enemy.Level + 6) * 4));
        short damage = BaseDamage(player.Attack, defense);
        if (enemy.PhysicalResistance != 0) damage = (short)(damage / enemy.PhysicalResistance);
        damage = unchecked((short)(damage + random.NextInclusive(1, 2)));
        // Bravery keeps the roll in the stream but always takes the critical branch (fight.c:3633-3641).
        bool critical = random.NextInclusive(0, 5) == 0 || playerStatuses[partyIndex][StatusBravery] > 0;
        if (critical) damage = unchecked((short)(damage * 3));
        if (player.RoleId == 0 && random.NextInclusive(0, 11) == 0)
        {
            damage = unchecked((short)(damage * 2));
            critical = true;
        }
        damage = unchecked((short)(damage * random.NextInclusive(1f, 1.125f)));
        if (damage <= 0) damage = 1;
        enemyHp[slot] = Math.Max(0, enemyHp[slot] - damage);
        Current = Current with { LastAction = new(Current.Round, ClassicBattleSide.Player, enemy.Id, damage, critical, false)
            { ActorIndex = partyIndex, TargetIndex = slot, SecondStrike = secondStrike } };
    }

    /// <summary>fight.c:3753-3755: the attack and health counts grow once per attack action, not per strike.</summary>
    private void CompletePlayerAction(int partyIndex)
    {
        counts[partyIndex] = new(unchecked((ushort)(counts[partyIndex].Attack + 1)),
            unchecked((ushort)(counts[partyIndex].Health + random.NextInclusive(2, 3))));
    }

    private void AttackPlayer(int enemySlot)
    {
        var enemy = enemies[enemySlot];
        int target;
        do { target = random.NextInclusive(0, players.Length - 1); } while (playerHp[target] == 0);
        var player = players[target];
        // fight.c 4540, 4938, 5056-5074, 5139: even a zero item rate draws 1..10 on a hit.
        bool evaded = random.NextInclusive(0, 16) >= 10;
        // fight.c 4943-4973: a dying or disabled target under a successful defend roll is covered by
        // its rgwCoveredBy role when that party member fights beside it and is itself responsive.
        int? coverer = evaded && NeedsCover(target) ? ResponsiveCoverer(player.CoveredByRoleId) : null;
        int damage = 0;
        if (!evaded)
        {
            int strength = Math.Max(0, enemy.Attack + (enemy.Level + 6) * 6);
            short hit = BaseDamage(unchecked((ushort)(strength + random.NextInclusive(0, 2))), player.Defense);
            hit = (short)(hit / 2);
            hit = unchecked((short)(hit + random.NextInclusive(0, 1)));
            // fight.c 5086-5090: protect halves the enemy strike before the HP clamp.
            if (playerStatuses[target][StatusProtect] > 0) hit = (short)(hit / 2);
            damage = Math.Max(1, Math.Min(playerHp[target], hit));
            _ = random.NextInclusive(1, 10);
        }
        playerHp[target] -= damage;
        // A covered strike is not an evade: the coverer intercepted it (fight.c:4959-4973 keeps
        // the target's frame; only a plain defend roll shows the evade frame).
        Current = Current with { LastAction = new(Current.Round, ClassicBattleSide.Enemy, player.RoleId, damage, false, evaded && coverer is null)
            { ActorIndex = enemySlot, TargetIndex = target, CovererPartyIndex = coverer } };
    }

    /// <summary>fight.c 4943-4947: a dying target or one held by confused/paralyzed/sleep needs cover.</summary>
    private bool NeedsCover(int partyIndex) =>
        playerHp[partyIndex] < Math.Min(100, players[partyIndex].MaxHp / 5)
        || playerStatuses[partyIndex][StatusConfused] > 0 || playerStatuses[partyIndex][StatusDisabled] > 0
        || playerStatuses[partyIndex][StatusSleep] > 0;

    /// <summary>fight.c 4948-4967: the coverer must be in this party and itself able to respond.</summary>
    private int? ResponsiveCoverer(int? coveredByRoleId)
    {
        if (coveredByRoleId is not { } role) return null;
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i].RoleId != role) continue;
            return playerHp[i] < Math.Min(100, players[i].MaxHp / 5)
                || playerStatuses[i][StatusConfused] > 0 || playerStatuses[i][StatusDisabled] > 0
                || playerStatuses[i][StatusSleep] > 0 ? null : i;
        }
        return null;
    }

    private static short BaseDamage(int attack, int defense) => attack > defense
        ? unchecked((short)(attack * 2 - defense * 1.6 + .5))
        : attack > defense * .6 ? unchecked((short)(attack - defense * .6 + .5)) : (short)0;

    internal static void Validate(ClassicBattleDefinition definition) => Validate(definition, null);

    internal static void Validate(ClassicBattleDefinition definition, BattleEquipmentState? equipment)
    {
        if (definition.Players is not { Count: >= 1 and <= 2 } || definition.Enemies is not { Count: >= 1 and <= 2 }
            || definition.Players.Any(p => p is null) || definition.Enemies.Any(e => e is null)
            || definition.Players.Select(p => p.RoleId).Distinct().Count() != definition.Players.Count)
            throw new InvalidDataException("Classic combat requires one or two living players with distinct roles and one or two living enemies.");
        foreach (var p in definition.Players) ValidatePlayer(p, equipment);
        foreach (var e in definition.Enemies) ValidateEnemy(e);
    }

    private static void ValidatePlayer(ClassicBattlePlayer p, BattleEquipmentState? equipment)
    {
        if (p.RoleId is < 0 or > 5 || p.Hp is < 1 or > 32767 || p.MaxHp is < 1 or > 65535 || p.Hp > p.MaxHp
            || !Word(p.Attack) || !Word(p.Defense) || !Word(p.Dexterity))
            throw new InvalidDataException("Classic combat has invalid or unsupported player numeric data (current HP must be 1..32767).");
        // A declared closure carries the formal equipment semantics; without one, zero equipment is
        // still the only supported domain. The battle chain binds one authored base sprite, so an
        // equipment battle-sprite override is an unsupported dependency rejected before side effects.
        bool equipmentAgrees = equipment is not { Declared: true }
            ? Zero(p.Equipment, 6)
            : p.Equipment is { Count: 6 } && p.Equipment.SequenceEqual(equipment.Equipment(p.RoleId))
                && !equipment.OverridesBattleSprite(p.RoleId);
        if (!equipmentAgrees || !Zero(p.Statuses, 9) || !Zero(p.Poisons)
            || p.DyingScript != 0 || p.FriendDeathScript != 0 || p.CoveredByRoleId is < 0 or > 5 || p.AttackAll)
            throw new InvalidDataException("Classic combat does not yet support statuses, poisons, hooks, attack-all or an equipment battle-sprite override; equipment requires its declared closure and cover must name a role 0..5.");
    }

    private static void ValidateEnemy(ClassicBattleEnemy e)
    {
        if (e.Id is < 1 or > 65535 || !Word(e.Level) || e.Hp is < 1 or > 32767
            || !SignedWord(e.Attack) || !SignedWord(e.Defense) || !SignedWord(e.Dexterity) || !Word(e.PhysicalResistance))
            throw new InvalidDataException("Classic combat has invalid or unsupported numeric data (current HP must be 1..32767).");
        if (!Zero(e.Statuses, 9) || !Zero(e.Poisons) || e.TurnStartScript != 0 || e.ReadyScript != 0 || e.BattleEndScript != 0
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
