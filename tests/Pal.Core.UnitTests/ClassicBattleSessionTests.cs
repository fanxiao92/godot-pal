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
        yield return [d with { Players = [d.Players[0], d.Players[0] with { RoleId = 0 }, d.Players[0] with { RoleId = 2 }] }];
        yield return [d with { Enemies = [] }];
        yield return [d with { Enemies = [d.Enemies[0], d.Enemies[0] with { Id = 8 }, d.Enemies[0] with { Id = 9 }] }];
        foreach (var player in new[] {
            d.Players[0] with { Equipment = [1, 0, 0, 0, 0, 0] }, d.Players[0] with { Equipment = [0] },
            d.Players[0] with { Statuses = [0, 0, 0, 0, 0, 0, 0, 0, 1000] }, d.Players[0] with { Poisons = [1] },
            d.Players[0] with { DyingScript = 65536 }, d.Players[0] with { FriendDeathScript = -1 },
            d.Players[0] with { CoveredByRoleId = 6 }, d.Players[0] with { AttackAll = true },
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
    public void Non_boss_flee_is_an_unsupported_domain_and_consumes_nothing()
    {
        var random = new ScriptedRandom([]);
        var session = new ClassicBattleSession(Definition(), random);
        ClassicBattleView before = session.Current;
        Assert.Throws<InvalidOperationException>(() => session.SelectFlee(0));
        Assert.Same(before, session.Current);
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
