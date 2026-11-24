using Xunit;

namespace Pal.Core.Tests;

public sealed class ClassicBattleSessionTests
{
    [Fact]
    public void Player_attack_produces_real_won_completion()
    {
        var random = new ScriptedRandom([F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), I(1, 2, 1), I(0, 5, 1), F(1, 1.125f, 1), I(2, 3, 2)]);
        var session = new ClassicBattleSession(Definition(enemyHp: 43), random);
        Assert.Null(session.Completion);
        session.SelectAttack(7);
        session.ResolveNextAction();
        Assert.Equal(ClassicBattlePhase.Won, session.Current.Phase);
        Assert.Equal(0, session.Current.EnemyHp);
        Assert.Equal(200, session.Current.PlayerHp);
        Assert.Equal(new ClassicBattleCounts(1, 2), session.Current.Counts);
        Assert.Equal(new ClassicBattleAction(1, ClassicBattleSide.Player, 7, 43, false, false), session.Current.LastAction);
        Assert.NotNull(session.Completion);
        random.AssertExhausted();
    }

    [Fact]
    public void Enemy_first_attack_produces_real_lost_without_player_action()
    {
        // Enemy str36, player defense20: base40 /2 +1 =21, clamped to current HP20.
        var random = new ScriptedRandom([F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), I(0, 0, 0), I(0, 16, 9), I(0, 2, 0), I(0, 1, 1), I(1, 10, 10)]);
        var data = Definition();
        data = data with { Players = [data.Players[0] with { Hp = 20, Dexterity = 10 }] };
        var session = new ClassicBattleSession(data, random);
        session.SelectAttack(7);
        session.ResolveNextAction();
        Assert.Equal(ClassicBattlePhase.Lost, session.Current.Phase);
        Assert.Equal(0, session.Current.PlayerHp);
        Assert.Equal(100, session.Current.EnemyHp);
        Assert.Equal(new ClassicBattleCounts(0, 0), session.Current.Counts);
        Assert.Equal(new ClassicBattleAction(1, ClassicBattleSide.Enemy, 1, 20, false, false), session.Current.LastAction);
        Assert.Equal(ClassicBattlePhase.Lost, session.Completion!.Outcome);
        Assert.False(session.ResolveNextAction());
        random.AssertExhausted();
    }

    [Fact]
    public void Ko_accumulates_word_rewards_once_without_random_or_input_mutation()
    {
        var data = Definition();
        data = data with { Enemies = [data.Enemies[0] with { Hp = 43, Experience = 120, Cash = 250 }] };
        var random = new ScriptedRandom([F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), I(1, 2, 1), I(0, 5, 1), F(1, 1.125f, 1), I(2, 3, 2)]);
        var session = new ClassicBattleSession(data, random);
        Assert.Equal(0, session.ExperienceGained);
        Assert.Equal(0, session.CashGained);
        session.SelectAttack(7);
        Assert.True(session.ResolveNextAction());
        // battle.c 753-754: the single KO accumulates both reward words exactly once.
        Assert.Equal(120, session.ExperienceGained);
        Assert.Equal(250, session.CashGained);
        random.AssertExhausted();
    }

    [Fact]
    public void Unsupported_enemy_magic_is_rejected_before_random_or_input_mutation()
    {
        var data = Definition();
        data = data with { Enemies = [data.Enemies[0] with { MagicId = 1 }] };
        var random = new ScriptedRandom([]);
        Assert.Throws<InvalidDataException>(() => new ClassicBattleSession(data, random));
        Assert.Equal(100, data.Enemies[0].Hp);
        Assert.Equal(200, data.Players[0].Hp);
        random.AssertExhausted();
    }

    [Fact]
    public void Invalid_target_is_rejected_without_changing_selection_or_consuming_random()
    {
        var random = new ScriptedRandom([F(.9f, 1.1f, 1), F(.9f, 1.1f, 1)]);
        var session = new ClassicBattleSession(Definition(), random);
        ClassicBattleView before = session.Current;
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SelectAttack(8));
        Assert.Same(before, session.Current);
        session.SelectAttack(7);
        random.AssertExhausted();
    }

    [Theory]
    [InlineData(1, 40, 0, 1, 1, 1, 11, 1f, 43, false)]
    [InlineData(1, 40, 0, 1, 2, 0, 11, 1.125f, 148, true)]
    [InlineData(0, 40, 0, 1, 1, 1, 0, 1.125f, 96, true)]
    [InlineData(0, 40, 0, 1, 1, 0, 0, 1f, 258, true)]
    [InlineData(0, 40, 0, 1, 1, 5, 11, 1f, 43, false)]
    [InlineData(1, 20, 0, 2, 1, 1, 11, 1f, 4, false)]
    [InlineData(1, 14, 0, 1, 2, 1, 11, 1f, 2, false)]
    [InlineData(1, 40, 0, 0, 1, 1, 11, 1f, 43, false)]
    [InlineData(1, 40, -24, 1, 1, 1, 11, 1f, 81, false)]
    public void Player_damage_matches_independent_classic_examples(int role, int attack, int defense, int resistance,
        int additive, int criticalRoll, int bonusRoll, float scale, int damage, bool critical)
    {
        var draws = new List<Draw> { F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), I(1, 2, additive), I(0, 5, criticalRoll) };
        if (role == 0) draws.Add(I(0, 11, bonusRoll));
        draws.Add(F(1, 1.125f, scale));
        draws.Add(I(2, 3, 3));
        var random = new ScriptedRandom(draws);
        var data = Definition(1000);
        data = data with { Players = [data.Players[0] with { RoleId = role, Attack = attack }],
            Enemies = [data.Enemies[0] with { Defense = defense, PhysicalResistance = resistance }] };
        var session = new ClassicBattleSession(data, random);
        session.SelectAttack(7);
        session.ResolveNextAction();
        Assert.Equal(1000 - damage, session.Current.EnemyHp);
        Assert.Equal(new ClassicBattleAction(1, ClassicBattleSide.Player, 7, damage, critical, false), session.Current.LastAction);
        Assert.Equal(new ClassicBattleCounts(1, 3), session.Current.Counts);
        random.AssertExhausted();
    }

    [Theory]
    [InlineData(21, 200, 200, 0, 1f, 1f)] // equal integer speeds: enemy retains first slot
    [InlineData(24, 200, 200, 0, 1f, .9f)] // player21.6 truncates21, still a tie
    [InlineData(100, 39, 200, 39, 1f, 1f)] // below40: player50 vs enemy60
    [InlineData(100, 99, 1000, 39, 1f, 1f)] // dying threshold capped at100
    [InlineData(2000, 200, 200, 979, 1f, 1f)] // cap player999 vs enemy1000
    public void Classic_queue_ties_truncation_cap_and_dying_penalty_allow_enemy_first(
        int dexterity, int hp, int maxHp, int enemyOffset, float enemyRoll, float playerRoll)
    {
        var random = new ScriptedRandom([F(.9f, 1.1f, enemyRoll), F(.9f, 1.1f, playerRoll), I(0, 0, 0), I(0, 16, 10)]);
        var data = Definition();
        data = data with { Players = [data.Players[0] with { Hp = hp, MaxHp = maxHp, Dexterity = dexterity }],
            Enemies = [data.Enemies[0] with { Level = 1, Dexterity = enemyOffset }] };
        var session = new ClassicBattleSession(data, random);
        Assert.False(session.ResolveNextAction());
        session.SelectAttack(7);
        Assert.Throws<InvalidOperationException>(() => session.SelectAttack(7));
        session.ResolveNextAction();
        Assert.Equal(ClassicBattleSide.Enemy, session.Current.LastAction!.Attacker);
        Assert.True(session.Current.LastAction.Evaded);
        Assert.Equal(hp, session.Current.PlayerHp);
        random.AssertExhausted();
    }

    [Theory]
    [InlineData(40, 200)]
    [InlineData(100, 1000)]
    public void Dying_threshold_itself_does_not_halve_player_speed(int hp, int maxHp)
    {
        var random = new ScriptedRandom([F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), I(1, 2, 1), I(0, 5, 1), F(1, 1.125f, 1), I(2, 3, 2)]);
        var data = Definition(43);
        data = data with { Players = [data.Players[0] with { Hp = hp, MaxHp = maxHp }], Enemies = [data.Enemies[0] with { Dexterity = 39 }] };
        var session = new ClassicBattleSession(data, random);
        session.SelectAttack(7);
        session.ResolveNextAction();
        Assert.Equal(ClassicBattlePhase.Won, session.Current.Phase);
        random.AssertExhausted();
    }

    [Theory]
    [InlineData(9, 0, 0, 1, 21)]
    [InlineData(0, -100, 0, 0, 1)]
    [InlineData(10, 0, 0, 0, 0)]
    [InlineData(16, 0, 0, 0, 0)]
    public void Enemy_hit_minimum_and_evasion_consume_only_applicable_random(int evadeRoll, int attack, int strengthRoll, int extra, int damage)
    {
        var draws = new List<Draw> { F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), I(0, 0, 0), I(0, 16, evadeRoll) };
        if (evadeRoll < 10) draws.AddRange([I(0, 2, strengthRoll), I(0, 1, extra), I(1, 10, 1)]);
        var random = new ScriptedRandom(draws);
        var data = Definition();
        data = data with { Players = [data.Players[0] with { Dexterity = 1 }], Enemies = [data.Enemies[0] with { Attack = attack }] };
        var session = new ClassicBattleSession(data, random);
        session.SelectAttack(7);
        session.ResolveNextAction();
        Assert.Equal(200 - damage, session.Current.PlayerHp);
        Assert.Equal(new ClassicBattleAction(1, ClassicBattleSide.Enemy, 1, damage, false, evadeRoll >= 10), session.Current.LastAction);
        random.AssertExhausted();
    }

    [Fact]
    public void Complete_round_returns_to_selection_and_rerolls_order_instead_of_alternating()
    {
        var random = new ScriptedRandom([
            F(.9f, 1.1f, 1.1f), F(.9f, 1.1f, .9f), I(0, 0, 0), I(0, 16, 0), I(0, 2, 0), I(0, 1, 0), I(1, 10, 10),
            I(1, 2, 1), I(0, 5, 1), F(1, 1.125f, 1), I(2, 3, 2),
            F(.9f, 1.1f, .9f), F(.9f, 1.1f, 1.1f), I(1, 2, 2), I(0, 5, 0), F(1, 1.125f, 1), I(2, 3, 3)]);
        var data = Definition();
        data = data with { Players = [data.Players[0] with { Dexterity = 22 }] };
        var session = new ClassicBattleSession(data, random);
        session.SelectAttack(7);
        session.ResolveNextAction();
        Assert.Equal(ClassicBattleSide.Enemy, session.Current.LastAction!.Attacker);
        session.ResolveNextAction();
        Assert.Equal(ClassicBattlePhase.Selecting, session.Current.Phase);
        Assert.Equal(180, session.Current.PlayerHp);
        Assert.Equal(57, session.Current.EnemyHp);
        Assert.Null(session.Completion);
        Assert.False(session.ResolveNextAction());
        session.SelectAttack(7);
        session.ResolveNextAction();
        Assert.Equal(ClassicBattlePhase.Won, session.Current.Phase);
        Assert.Equal(new ClassicBattleAction(2, ClassicBattleSide.Player, 7, 132, true, false), session.Current.LastAction);
        Assert.Equal(new ClassicBattleCounts(2, 5), session.Current.Counts);
        var completion = session.Completion;
        Assert.Throws<InvalidOperationException>(() => session.SelectAttack(7));
        Assert.False(session.ResolveNextAction());
        Assert.Same(completion, session.Completion);
        Assert.Equal(200, data.Players[0].Hp);
        Assert.Equal(100, data.Enemies[0].Hp);
        random.AssertExhausted();
    }

    public static IEnumerable<object[]> UnsupportedDefinitions()
    {
        var d = Definition();
        yield return [d with { Players = [] }];
        yield return [d with { Players = [d.Players[0], d.Players[0] with { RoleId = 0 }, d.Players[0] with { RoleId = 2 }, d.Players[0] with { RoleId = 1 }] }];
        yield return [d with { Enemies = [] }];
        yield return [d with { Enemies = Enumerable.Repeat(d.Enemies[0], 6).ToArray() }];
        foreach (var player in new[] {
            d.Players[0] with { Equipment = [1, 0, 0, 0, 0, 0] }, d.Players[0] with { Equipment = [0] },
            d.Players[0] with { Statuses = [0, 0, 0, 0, 0, 0, 0, 0, 1000] }, d.Players[0] with { Poisons = [1] },
            d.Players[0] with { DyingScript = 65536 }, d.Players[0] with { FriendDeathScript = -1 },
            d.Players[0] with { CoveredByRoleId = 6 },
            d.Players[0] with { Hp = 0 }, d.Players[0] with { Hp = 201 }, d.Players[0] with { RoleId = 6 },
            d.Players[0] with { Attack = -1 }, d.Players[0] with { Defense = 65536 }, d.Players[0] with { Dexterity = -1 } })
            yield return [d with { Players = [player] }];
        foreach (var enemy in new[] {
            d.Enemies[0] with { Statuses = [1, 0, 0, 0, 0, 0, 0, 0, 0] }, d.Enemies[0] with { Poisons = [1] },
            d.Enemies[0] with { TurnStartScript = 1 }, d.Enemies[0] with { ReadyScript = 1 },
            d.Enemies[0] with { MagicId = 1 },
            d.Enemies[0] with { DualMove = 1 }, d.Enemies[0] with { AttackEquivItem = 1 },
            d.Enemies[0] with { AttackEquivItemRate = 1 },
            d.Enemies[0] with { Hp = 0 }, d.Enemies[0] with { Hp = 32768 }, d.Enemies[0] with { PhysicalResistance = -1 },
            d.Enemies[0] with { Attack = -32769 }, d.Enemies[0] with { Level = -1 } })
            yield return [d with { Enemies = [enemy] }];
    }

    [Theory]
    [MemberData(nameof(UnsupportedDefinitions))]
    public void Unsupported_data_is_preflighted_without_random_or_external_state_changes(ClassicBattleDefinition definition)
    {
        var random = new ScriptedRandom([]);
        Assert.Throws<InvalidDataException>(() => new ClassicBattleSession(definition, random));
        random.AssertExhausted();
    }

    [Fact]
    public void Boss_flee_consumes_the_reference_draw_fails_and_stays_in_battle()
    {
        // fight.c 4119-4152: defense = living enemy dex + (level+6)*4 = 0 + 24; RandomLong(0, 24)
        // is consumed even though a boss (battle.c 1544 fIsBoss) can never escape.
        var random = new ScriptedRandom([F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), I(0, 24, 24),
            I(0, 0, 0), I(0, 16, 0), I(0, 2, 0), I(0, 1, 0), I(1, 10, 1)]);
        var session = new ClassicBattleSession(Definition(), random, isBossBattle: true);
        session.SelectFlee(0);
        Assert.Equal(ClassicBattlePhase.Resolving, session.Current.Phase);
        // fight.c 1546: the committed flee halves the member's queue dexterity (100 -> 50).
        Assert.Equal(50, session.Current.ActionQueue.Single(e => e.Side == ClassicBattleSide.Player).Dexterity);
        session.ResolveNextAction();
        Assert.Equal(new ClassicBattleFleeAttempt(1, 0, 1, 0, 24, 24, false), session.Current.LastFleeAttempt);
        Assert.Equal(2, session.Current.Counts.Flee);
        Assert.Equal(0, session.Current.Counts.Attack);
        Assert.Equal(0, session.ExperienceGained);
        Assert.Equal(0, session.CashGained);
        session.ResolveNextAction(); // enemy strike lands; the failed flee spent only its own action
        Assert.Equal(ClassicBattlePhase.Selecting, session.Current.Phase);
        Assert.Null(session.Completion);
        random.AssertExhausted();
    }

    [Fact]
    public void Boss_flee_attempt_reports_the_effective_flee_panel_and_escaped_false()
    {
        var random = new ScriptedRandom([F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), I(0, 24, 0)]);
        var data = Definition();
        data = data with { Players = [data.Players[0] with { FleeRate = 900 }] };
        var session = new ClassicBattleSession(data, random, isBossBattle: true);
        session.SelectFlee(0);
        session.ResolveNextAction();
        // Even str 900 >= draw 0 stays a failure: escape requires !fIsBoss (fight.c 4144).
        Assert.Equal(new ClassicBattleFleeAttempt(1, 0, 1, 900, 24, 0, false), session.Current.LastFleeAttempt);
        Assert.Equal(2, session.Current.Counts.Flee);
        random.AssertExhausted();
    }

    [Fact]
    public void Normal_battle_flee_succeeds_and_ends_the_battle_without_rewards()
    {
        // fight.c 4119-4152 + battle.c 1527: a normal battle's successful escape consumes the
        // single RandomLong(0, def) draw (def 24: dex 0 + (level0+6)*4), ends the battle at the
        // fleer's own queue action and grants no rewards and no flee-experience count.
        var random = new ScriptedRandom([F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), I(0, 24, 10)]);
        var data = Definition();
        data = data with { Players = [data.Players[0] with { FleeRate = 25 }] };
        var session = new ClassicBattleSession(data, random);
        session.SelectFlee(0);
        session.ResolveNextAction();
        Assert.Equal(new ClassicBattleFleeAttempt(1, 0, 1, 25, 24, 10, true), session.Current.LastFleeAttempt);
        Assert.NotNull(session.Completion);
        Assert.Equal(0, session.Current.Counts.Flee);
        Assert.Equal(0, session.ExperienceGained);
        Assert.Equal(0, session.CashGained);
        Assert.False(session.ResolveNextAction());
        random.AssertExhausted();
    }

    [Fact]
    public void Normal_battle_flee_boundary_equality_escapes_at_the_drawn_defense()
    {
        // fight.c 4144: str >= RandomLong(0, def); the equal boundary (24 >= 24) is a success.
        var random = new ScriptedRandom([F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), I(0, 24, 24)]);
        var data = Definition();
        data = data with { Players = [data.Players[0] with { FleeRate = 24 }] };
        var session = new ClassicBattleSession(data, random);
        session.SelectFlee(0);
        session.ResolveNextAction();
        Assert.Equal(new ClassicBattleFleeAttempt(1, 0, 1, 24, 24, 24, true), session.Current.LastFleeAttempt);
        Assert.Equal(ClassicBattlePhase.Escaped, session.Current.Phase);
        Assert.Equal(ClassicBattlePhase.Escaped, session.Completion!.Outcome);
        Assert.Equal(0, session.Current.Counts.Flee);
        random.AssertExhausted();
    }

    [Fact]
    public void Normal_battle_flee_failure_spends_the_action_and_grows_the_flee_track()
    {
        // fight.c 4153-4171: a failed normal escape stays in battle (23 < 24), adds 2 to the
        // flee track and spends only its own action; the enemy strike ends the round normally.
        var random = new ScriptedRandom([F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), I(0, 24, 24),
            I(0, 0, 0), I(0, 16, 0), I(0, 2, 0), I(0, 1, 0), I(1, 10, 1)]);
        var data = Definition();
        data = data with { Players = [data.Players[0] with { FleeRate = 23 }] };
        var session = new ClassicBattleSession(data, random);
        session.SelectFlee(0);
        session.ResolveNextAction();
        Assert.Equal(new ClassicBattleFleeAttempt(1, 0, 1, 23, 24, 24, false), session.Current.LastFleeAttempt);
        Assert.Equal(2, session.Current.Players[0].Counts.Flee);
        Assert.Null(session.Completion);
        session.ResolveNextAction(); // the enemy strike lands (20 damage); the round reopens selection
        Assert.Equal(ClassicBattlePhase.Selecting, session.Current.Phase);
        Assert.Equal(180, session.Current.PlayerHp);
        Assert.Null(session.Completion);
        random.AssertExhausted();
    }

    [Fact]
    public void Normal_battle_flee_defense_sums_every_living_enemy()
    {
        // fight.c 4128-4137: def sums each living enemy's dex + (level+6)*4 (24 + 28 here);
        // the equal boundary 52 >= 52 escapes.
        var random = new ScriptedRandom([F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), I(0, 52, 52)]);
        var data = Definition();
        data = data with { Players = [data.Players[0] with { FleeRate = 52 }],
            Enemies = [data.Enemies[0], data.Enemies[0] with { Level = 1 }] };
        var session = new ClassicBattleSession(data, random);
        session.SelectFlee(0);
        session.ResolveNextAction();
        Assert.Equal(new ClassicBattleFleeAttempt(1, 0, 1, 52, 52, 52, true), session.Current.LastFleeAttempt);
        Assert.Equal(ClassicBattlePhase.Escaped, session.Current.Phase);
        random.AssertExhausted();
    }

    [Fact]
    public void Normal_battle_flee_clamps_a_negative_short_defense_sum_to_zero()
    {
        // fight.c 4139-4143: a (SHORT)negative defense sum (two dex-19988 enemies: 2*(19988+24)
        // = 40024 -> SHORT -25512) becomes 0, so the RandomLong(0, 0) draw equals any panel and
        // even fleeRate 0 escapes. Both enemies act first (speed 20006 vs the halved 50).
        var random = new ScriptedRandom([F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), F(.9f, 1.1f, 1),
            I(0, 0, 0), I(0, 16, 0), I(0, 2, 0), I(0, 1, 0), I(1, 10, 1),
            I(0, 0, 0), I(0, 16, 0), I(0, 2, 0), I(0, 1, 0), I(1, 10, 1), I(0, 0, 0)]);
        var data = Definition();
        data = data with { Enemies = [data.Enemies[0] with { Dexterity = 19988 },
            data.Enemies[0] with { Dexterity = 19988 }] };
        var session = new ClassicBattleSession(data, random);
        session.SelectFlee(0);
        session.ResolveNextAction(); // first enemy strike (20 damage)
        session.ResolveNextAction(); // second enemy strike (20 damage)
        Assert.Equal(160, session.Current.PlayerHp);
        session.ResolveNextAction(); // the member's attempt: defense clamped to zero
        Assert.Equal(new ClassicBattleFleeAttempt(1, 0, 1, 0, 0, 0, true), session.Current.LastFleeAttempt);
        Assert.Equal(ClassicBattlePhase.Escaped, session.Current.Phase);
        random.AssertExhausted();
    }

    [Fact]
    public void Normal_battle_flee_defense_accumulates_on_the_word_like_the_reference()
    {
        // fight.c 3597 + 4128-4137: def is a WORD accumulator — every dex and (level+6)*4 store
        // wraps at 65536, so one level-65535 enemy's 262164 wraps to 20: the draw bound is
        // RandomLong(0, 20), never the unwrapped 262164 of an int sum. The enemy's wrapped queue
        // speed ((65541*3) mod 65536 = 15) stays under the halved 50, so the member acts first.
        var highLevel = new ScriptedRandom([F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), I(0, 20, 20)]);
        var data = Definition();
        data = data with { Players = [data.Players[0] with { FleeRate = 25 }],
            Enemies = [data.Enemies[0] with { Level = 65535 }] };
        var session = new ClassicBattleSession(data, highLevel);
        session.SelectFlee(0);
        session.ResolveNextAction();
        Assert.Equal(new ClassicBattleFleeAttempt(1, 0, 1, 25, 20, 20, true), session.Current.LastFleeAttempt);
        Assert.Equal(ClassicBattlePhase.Escaped, session.Completion!.Outcome);
        highLevel.AssertExhausted();

        // A negative raw sum wraps per store exactly like the reference: 0x8000+24+0x8000+24
        // = 48 on the WORD — never a pre-wrap negative clamp and never a negative RandomLong
        // bound. The two enemies' wrapped queue speeds (18-32768 -> 32786) read as SHORT
        // -32750 in the reference's nested short comparison, so they queue last and the
        // member's boundary attempt 48 >= 48 ends the battle before any enemy strike.
        var negativeSum = new ScriptedRandom([F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), I(0, 48, 48)]);
        data = Definition();
        data = data with { Players = [data.Players[0] with { FleeRate = 48 }],
            Enemies = [data.Enemies[0] with { Dexterity = -32768 }, data.Enemies[0] with { Dexterity = -32768 }] };
        session = new ClassicBattleSession(data, negativeSum);
        session.SelectFlee(0);
        session.ResolveNextAction(); // the member's attempt: the WORD accumulator holds 48
        Assert.Equal(new ClassicBattleFleeAttempt(1, 0, 1, 48, 48, 48, true), session.Current.LastFleeAttempt);
        Assert.Equal(ClassicBattlePhase.Escaped, session.Current.Phase);
        negativeSum.AssertExhausted();
    }

    [Fact]
    public void Normal_battle_flee_success_never_attempts_the_later_fleeing_members()
    {
        // fight.c 1965-1978 + battle.c 774: both members committed the flee (the committer's
        // kKeyFlee re-feed), but the committer's successful attempt ends the battle at his own
        // queue action; the second member's queued attempt never runs and nobody gains a count.
        var random = new ScriptedRandom([F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), I(0, 24, 0)]);
        var data = Definition();
        data = data with { Players = [data.Players[0] with { FleeRate = 5 },
            data.Players[0] with { RoleId = 2, Dexterity = 90 }] };
        var session = new ClassicBattleSession(data, random);
        session.SelectFlee(0);
        Assert.Equal(ClassicBattlePhase.Resolving, session.Current.Phase);
        session.ResolveNextAction();
        Assert.Equal(new ClassicBattleFleeAttempt(1, 0, 1, 5, 24, 0, true), session.Current.LastFleeAttempt);
        Assert.Equal(ClassicBattlePhase.Escaped, session.Current.Phase);
        Assert.Equal(0, session.Current.Players[0].Counts.Flee);
        Assert.Equal(0, session.Current.Players[1].Counts.Flee);
        Assert.False(session.ResolveNextAction());
        random.AssertExhausted();
    }

    [Fact]
    public void Classic_flee_commit_covers_every_later_living_member()
    {
        // fight.c 1965-1978 + 1797-1799: one committed flee feeds kKeyFlee to the later menus,
        // so both members attempt and the round resolves without further selection.
        var random = new ScriptedRandom([F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), F(.9f, 1.1f, 1),
            I(0, 24, 24), I(0, 24, 23), I(0, 1, 0), I(0, 16, 10)]);
        var data = Definition();
        data = data with { Players = [data.Players[0], data.Players[0] with { RoleId = 2, Dexterity = 90 }] };
        var session = new ClassicBattleSession(data, random, isBossBattle: true);
        session.SelectFlee(0);
        Assert.Equal(ClassicBattlePhase.Resolving, session.Current.Phase);
        session.ResolveNextAction();
        Assert.Equal(new ClassicBattleFleeAttempt(1, 0, 1, 0, 24, 24, false), session.Current.LastFleeAttempt);
        session.ResolveNextAction();
        Assert.Equal(new ClassicBattleFleeAttempt(1, 1, 2, 0, 24, 23, false), session.Current.LastFleeAttempt);
        session.ResolveNextAction(); // the enemy strike (evaded here) ends the round
        Assert.Equal(ClassicBattlePhase.Selecting, session.Current.Phase);
        Assert.Equal(2, session.Current.Players[0].Counts.Flee);
        Assert.Equal(2, session.Current.Players[1].Counts.Flee);
        random.AssertExhausted();
    }

    [Fact]
    public void Defend_commit_advances_selection_consumes_nothing_and_reverts_by_cancel()
    {
        // uibattle.c 1166-1171: kKeyDefend commits the defend action directly with no target
        // menu; the commit itself draws no random (only the queue build does) and stays
        // revertible by the classic cancel until the last member commits (uibattle.c 1225-1252).
        var random = new ScriptedRandom([F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), F(.9f, 1.1f, 1)]);
        var data = Definition();
        data = data with { Players = [data.Players[0], data.Players[0] with { RoleId = 2, Dexterity = 90 }] };
        var session = new ClassicBattleSession(data, random);
        session.SelectDefend(0);
        Assert.Equal(ClassicBattlePhase.Selecting, session.Current.Phase);
        Assert.Equal(1, session.Current.SelectingPlayerIndex);
        Assert.Null(session.Current.Players[0].SelectedEnemySlot);
        Assert.Empty(session.Current.ActionQueue);
        Assert.False(session.Current.Players[0].IsDefending);
        // The defend commit is the member's editable selection: one cancel reopens member 0.
        Assert.True(session.CancelSelection());
        Assert.Equal(0, session.Current.SelectingPlayerIndex);
        session.SelectDefend(0);
        session.SelectDefend(1);
        Assert.Equal(ClassicBattlePhase.Resolving, session.Current.Phase);
        // fight.c 1535-1537: the committed defend multiplies the queue dexterity by five
        // (100 -> 500, 90 -> 450) ahead of the enemy's 18; exactly the three queue rolls ran.
        Assert.Equal([500, 450, 18], session.Current.ActionQueue.Select(entry => (int)entry.Dexterity).ToArray());
        random.AssertExhausted();
    }

    [Fact]
    public void Defend_perform_sets_flag_count_priority_and_halves_enemy_strikes()
    {
        // fight.c 4110-4116: the flag and the hidden defense count (+2) land at perform time,
        // never at selection; no strike, attack or health counter exists. The defend priority
        // (fight.c 1535 wDexterity *= 5: 100*5 = 500 vs the enemy's 18) acts first, so the
        // enemy strike that deals 20 to an undefended defense 20 (BaseDamage(36,20)=40 / 2)
        // deals 6 once fDefending doubles the defense (fight.c 4924-4927: BaseDamage(36,40) = 12 / 2).
        var random = new ScriptedRandom([F(.9f, 1.1f, 1), F(.9f, 1.1f, 1),
            I(0, 0, 0), I(0, 16, 0), I(0, 2, 0), I(0, 1, 0), I(1, 10, 1)]);
        var session = new ClassicBattleSession(Definition(), random);
        session.SelectDefend(0);
        Assert.Equal(ClassicBattlePhase.Resolving, session.Current.Phase);
        Assert.Equal(500, session.Current.ActionQueue.Single(entry => entry.Side == ClassicBattleSide.Player).Dexterity);
        session.ResolveNextAction();
        Assert.Null(session.Current.LastAction);
        Assert.True(session.Current.Players[0].IsDefending);
        Assert.Equal(2, session.Current.Players[0].Counts.Defense);
        Assert.Equal(0, session.Current.Players[0].Counts.Attack);
        Assert.Equal(0, session.Current.Players[0].Counts.Health);
        session.ResolveNextAction();
        Assert.Equal(ClassicBattleSide.Enemy, session.Current.LastAction!.Attacker);
        Assert.Equal(6, session.Current.LastAction.Damage);
        Assert.Equal(194, session.Current.PlayerHp);
        // fight.c 1599-1604 (round end) and 1068 (next selection): fDefending lasts its round.
        Assert.Equal(ClassicBattlePhase.Selecting, session.Current.Phase);
        Assert.False(session.Current.Players[0].IsDefending);
        Assert.Equal(2, session.Current.Players[0].Counts.Defense);
        random.AssertExhausted();
    }

    [Fact]
    public void Defend_does_not_halve_enemy_strikes_before_its_own_action_and_only_within_its_round()
    {
        // fDefending is runtime state of the performed action, not of the committed selection:
        // an enemy that outruns the defender this round still deals the full 20, and only a
        // later round in which the member's own defend performed first halves the same strike
        // to 6. Enemy dex 460 -> base speed 478 inside [500*0.9, 500*1.1], so the scripted
        // rolls pick the order: enemy first in round 1 (501 vs 475), member first in round 2 (525 vs 454).
        var random = new ScriptedRandom([
            F(.9f, 1.1f, 1.05f), F(.9f, 1.1f, .95f),
            I(0, 0, 0), I(0, 16, 0), I(0, 2, 0), I(0, 1, 0), I(1, 10, 1),
            F(.9f, 1.1f, .95f), F(.9f, 1.1f, 1.05f),
            I(0, 0, 0), I(0, 16, 0), I(0, 2, 0), I(0, 1, 0), I(1, 10, 1)]);
        var data = Definition();
        data = data with { Enemies = [data.Enemies[0] with { Dexterity = 460 }] };
        var session = new ClassicBattleSession(data, random);
        session.SelectDefend(0);
        Assert.Equal(ClassicBattleSide.Enemy, session.Current.ActionQueue[0].Side);
        session.ResolveNextAction();
        Assert.Equal(20, session.Current.LastAction!.Damage);
        Assert.Equal(180, session.Current.PlayerHp);
        Assert.False(session.Current.Players[0].IsDefending);
        session.ResolveNextAction(); // the member's own defend performs only after the strike;
        // it is the round's last action, so the same resolve ends the round and clears the flag.
        Assert.Equal(2, session.Current.Players[0].Counts.Defense);
        Assert.Equal(ClassicBattlePhase.Selecting, session.Current.Phase);
        Assert.False(session.Current.Players[0].IsDefending);
        session.SelectDefend(0);
        Assert.Equal(ClassicBattleSide.Player, session.Current.ActionQueue[0].Side);
        session.ResolveNextAction();
        Assert.True(session.Current.Players[0].IsDefending); // in-round: the enemy still acts after
        session.ResolveNextAction();
        Assert.Equal(6, session.Current.LastAction!.Damage);
        Assert.Equal(174, session.Current.PlayerHp);
        Assert.Equal(4, session.Current.Players[0].Counts.Defense);
        random.AssertExhausted();
    }

    [Fact]
    public void Defend_count_survives_the_members_later_attack_and_accumulates_in_the_same_battle()
    {
        // fight.c 3753-3755: the attack action grows only rgAttackExp/rgHealthExp — it never
        // touches rgDefenseExp. The defense track keeps its performed defends (+2 each,
        // fight.c 4113-4115) until the victory settlement consumes it (battle.c 1229/1280);
        // PAL_StartBattle (battle.c 1583) is the only reset. So a member that defended round 1
        // and attacks round 2 still shows its defense count afterwards, and a later defend
        // resumes from it instead of restarting at zero.
        var random = new ScriptedRandom([
            F(.9f, 1.1f, 1), F(.9f, 1.1f, 1),
            I(0, 0, 0), I(0, 16, 0), I(0, 2, 0), I(0, 1, 0), I(1, 10, 1),
            F(.9f, 1.1f, 1), F(.9f, 1.1f, 1),
            I(1, 2, 2), I(0, 5, 1), F(1, 1.125f, 1), I(2, 3, 2),
            I(0, 0, 0), I(0, 16, 0), I(0, 2, 0), I(0, 1, 0), I(1, 10, 1),
            F(.9f, 1.1f, 1), F(.9f, 1.1f, 1),
            I(0, 0, 0), I(0, 16, 0), I(0, 2, 0), I(0, 1, 0), I(1, 10, 1)]);
        var session = new ClassicBattleSession(Definition(), random);
        session.SelectDefend(0);
        session.ResolveNextAction(); // the defend performs: +2 defense, no attack or health count
        session.ResolveNextAction(); // the enemy strike (halved to 6) ends round 1
        Assert.Equal(2, session.Current.Players[0].Counts.Defense);
        Assert.Equal(ClassicBattlePhase.Selecting, session.Current.Phase);
        session.SelectAttack(7);
        session.ResolveNextAction(); // the member's plain attack completes: BaseDamage(40,24)=42 +2
        Assert.True(session.Current.Players[0].Counts.Defense == 2,
            "Classic combat must keep the performed defense count when the member later attacks in the same battle.");
        Assert.Equal(1, session.Current.Players[0].Counts.Attack);
        Assert.Equal(2, session.Current.Players[0].Counts.Health);
        Assert.Equal(56, session.Current.EnemyHp);
        session.ResolveNextAction(); // the enemy strike (full 20: no defend this round) ends round 2
        Assert.Equal(174, session.Current.PlayerHp);
        Assert.Equal(ClassicBattlePhase.Selecting, session.Current.Phase);
        session.SelectDefend(0);
        session.ResolveNextAction(); // a second defend resumes from the preserved count: 2 + 2 = 4
        Assert.Equal(4, session.Current.Players[0].Counts.Defense);
        Assert.True(session.Current.Players[0].IsDefending);
        session.ResolveNextAction(); // the enemy strike is halved again and the round ends
        Assert.Equal(168, session.Current.PlayerHp);
        Assert.Equal(ClassicBattlePhase.Selecting, session.Current.Phase);
        Assert.Equal(4, session.Current.Players[0].Counts.Defense);
        Assert.False(session.Current.Players[0].IsDefending);
        random.AssertExhausted();
    }

    [Fact]
    public void Dead_members_defend_commit_performs_the_internal_pass_without_effects()
    {
        // fight.c 1730-1735: a member dead at its queue slot performs the reference's internal
        // pass regardless of its committed action, and the pass branch is empty (fight.c
        // 4409-4410): no strike, no counts, no fDefending, no random. The enemy (dex 999,
        // speed 1017) kills the 20-HP member first; that member's committed defend never lands.
        var random = new ScriptedRandom([F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), F(.9f, 1.1f, 1),
            I(0, 1, 0), I(0, 16, 0), I(0, 2, 0), I(0, 1, 0), I(1, 10, 1)]);
        var data = Definition();
        data = data with
        {
            Players = [data.Players[0] with { Hp = 20, MaxHp = 200, Dexterity = 50 },
                data.Players[0] with { RoleId = 2, Dexterity = 90 }],
            Enemies = [data.Enemies[0] with { Dexterity = 999 }]
        };
        var session = new ClassicBattleSession(data, random);
        session.SelectDefend(0);
        session.SelectDefend(1);
        Assert.Equal(ClassicBattlePhase.Resolving, session.Current.Phase);
        session.ResolveNextAction();
        Assert.Equal(0, session.Current.Players[0].Hp);
        Assert.Equal(20, session.Current.LastAction!.Damage);
        session.ResolveNextAction();
        Assert.Equal(2, session.Current.Players[1].Counts.Defense);
        session.ResolveNextAction();
        Assert.Equal(ClassicBattlePhase.Selecting, session.Current.Phase);
        Assert.Equal(1, session.Current.SelectingPlayerIndex);
        Assert.Equal(0, session.Current.Players[0].Counts.Defense);
        Assert.False(session.Current.Players[0].IsDefending);
        Assert.Equal(0, session.Current.Players[0].Counts.Attack);
        Assert.Equal(ClassicBattleSide.Enemy, session.Current.LastAction!.Attacker);
        random.AssertExhausted();
    }

    [Fact]
    public void Defend_selection_rejects_wrong_member_phase_and_finished_battle_without_state_change()
    {
        var random = new ScriptedRandom([F(.9f, 1.1f, 1), F(.9f, 1.1f, 1),
            I(1, 2, 1), I(0, 5, 1), F(1, 1.125f, 1), I(2, 3, 2)]);
        var session = new ClassicBattleSession(Definition(43), random);
        ClassicBattleView before = session.Current;
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SelectDefend(1));
        Assert.Same(before, session.Current);
        session.SelectAttack(7);
        Assert.Throws<InvalidOperationException>(() => session.SelectDefend(0));
        session.ResolveNextAction();
        Assert.Equal(ClassicBattlePhase.Won, session.Current.Phase);
        ClassicBattleView terminal = session.Current;
        Assert.Throws<InvalidOperationException>(() => session.SelectDefend(0));
        Assert.Same(terminal, session.Current);
        random.AssertExhausted();
    }

    private static ClassicBattleDefinition Definition(int enemyHp = 100) => new(
        [new(1, 200, 200, 40, 20, 100)], [new(7, 0, enemyHp, 0, 0, 0, 1)]);
    private sealed record Draw(bool Floating, float From, float To, float Value);
    private static Draw F(float from, float to, float value) => new(true, from, to, value);
    private static Draw I(int from, int to, int value) => new(false, from, to, value);
    private sealed class ScriptedRandom(IEnumerable<Draw> draws) : IBattleRandom
    {
        private readonly Queue<Draw> remaining = new(draws);
        public int NextInclusive(int from, int to) => (int)Take(false, from, to);
        public float NextInclusive(float from, float to) => Take(true, from, to);
        private float Take(bool floating, float from, float to)
        {
            Assert.NotEmpty(remaining);
            Draw next = remaining.Dequeue();
            Assert.Equal((next.Floating, next.From, next.To), (floating, from, to));
            Assert.InRange(next.Value, from, to);
            return next.Value;
        }
        public void AssertExhausted() => Assert.Empty(remaining);
    }
}
