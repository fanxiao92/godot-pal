using Xunit;

namespace Pal.Core.Tests;

public sealed class ClassicBattlePartySessionTests
{
    [Fact]
    public void Two_players_and_two_identical_enemies_start_selecting_without_random_or_resolution()
    {
        var random = new ScriptedRandom([]);
        var session = new ClassicBattleSession(Definition(), random);
        Assert.Equal(ClassicBattlePhase.Selecting, session.Current.Phase);
        Assert.False(session.ResolveNextAction());
        random.AssertExhausted();
    }

    [Fact]
    public void First_selection_has_no_queue_or_random_and_indices_are_not_role_or_enemy_ids()
    {
        var random = new ScriptedRandom([]);
        var session = new ClassicBattleSession(Definition(), random);
        var before = session.Current;
        Assert.Equal(0, before.SelectingPlayerIndex);
        Assert.Throws<InvalidOperationException>(() => session.SelectAttack(7));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SelectAttack(4, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SelectAttack(0, 7));
        Assert.Same(before, session.Current);
        session.SelectAttack(0, 1);
        Assert.Equal(ClassicBattlePhase.Selecting, session.Current.Phase);
        Assert.Equal(1, session.Current.SelectingPlayerIndex);
        Assert.Equal(0, session.Current.Round);
        Assert.Equal(1, session.Current.Players[0].SelectedEnemySlot);
        Assert.Null(before.Players[0].SelectedEnemySlot);
        Assert.Empty(session.Current.ActionQueue);
        Assert.False(session.ResolveNextAction());
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SelectAttack(0, 0));
        random.AssertExhausted();
    }

    [Theory]
    [InlineData(18, 18, 0, 0, 1f, 1f, 1f, 1f, "E0:18,E1:18,P0:18,P1:18")]
    [InlineData(1, 100, 0, 0, 1f, 1f, 1f, 1f, "P1:100,E1:18,E0:18,P0:1")]
    [InlineData(22, 22, 0, 0, 1.1f, .9f, .9f, 1.1f, "P1:24,P0:19,E0:19,E1:16")]
    [InlineData(24, 1, 3, 0, 1f, 1f, .9f, 1f, "E0:21,P0:21,E1:18,P1:1")]
    [InlineData(100, 90, -20, 32767, 1f, 1f, 1f, 1f, "P0:100,P1:90")]
    public void Queue_uses_enemy_then_party_draws_WORD_truncation_SHORT_and_exact_nested_comparisons(
        int dex0, int dex1, int enemyDex0, int enemyDex1, float e0, float e1, float p0, float p1, string expected)
    {
        var d = Definition();
        d = d with { Players = [d.Players[0] with { Dexterity = dex0 }, d.Players[1] with { Dexterity = dex1 }],
            Enemies = [d.Enemies[0] with { Dexterity = enemyDex0 }, d.Enemies[1] with { Dexterity = enemyDex1 }] };
        var random = new ScriptedRandom([F(.9f, 1.1f, e0), F(.9f, 1.1f, e1), F(.9f, 1.1f, p0), F(.9f, 1.1f, p1)]);
        var session = new ClassicBattleSession(d, random);
        session.SelectAttack(0, 0);
        session.SelectAttack(1, 1);
        Assert.Equal(ClassicBattlePhase.Resolving, session.Current.Phase);
        Assert.Null(session.Current.SelectingPlayerIndex);
        Assert.Equal(expected, string.Join(",", session.Current.ActionQueue.Select(q =>
            $"{(q.Side == ClassicBattleSide.Enemy ? "E" : "P")}{q.ActorIndex}:{q.Dexterity}")));
        random.AssertExhausted();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Two_selected_attacks_retarget_dead_slot_circularly_and_win_only_after_all_enemies_KO(int slot)
    {
        // Each hit is 43: attack40 against effective defense24, +1, no critical, scale1.
        var random = new ScriptedRandom([.. QueueDraws(), .. PlayerHit(), .. PlayerHit()]);
        var d = Definition();
        d = d with { Enemies = [d.Enemies[0] with { Hp = 43 }, d.Enemies[1] with { Hp = 43 }] };
        var session = new ClassicBattleSession(d, random);
        session.SelectAttack(0, slot);
        session.SelectAttack(1, slot);
        var queued = session.Current;
        Assert.True(session.ResolveNextAction());
        Assert.Null(session.Completion);
        Assert.Equal(ClassicBattlePhase.Resolving, session.Current.Phase);
        Assert.Equal(slot, session.Current.LastAction!.TargetIndex);
        Assert.Equal(7, session.Current.LastAction.TargetId);
        Assert.True(session.ResolveNextAction());
        Assert.Equal(ClassicBattlePhase.Won, session.Current.Phase);
        Assert.Equal(1 - slot, session.Current.LastAction!.TargetIndex);
        Assert.Equal(1, session.Current.LastAction.ActorIndex);
        Assert.Equal(7, session.Current.LastAction.TargetId);
        Assert.Equal(1 - slot, session.Current.Players[1].SelectedEnemySlot);
        Assert.All(session.Current.Players, p => Assert.Equal(new ClassicBattleCounts(1, 2), p.Counts));
        Assert.All(session.Current.Enemies, e => Assert.Equal(0, e.Hp));
        Assert.All(queued.Enemies, e => Assert.Equal(43, e.Hp));
        var completed = session.Current;
        var receipt = session.Completion;
        Assert.False(session.ResolveNextAction());
        Assert.Same(completed, session.Current);
        Assert.Same(receipt, session.Completion);
        random.AssertExhausted();
    }

    [Fact]
    public void KO_player_passes_without_random_counts_or_duplicate_action_and_next_round_skips_selection()
    {
        // E0=118,E1=108,P1=90,P0=50(dying). E0 kills party0, E1 redraws dead0 then hits1.
        var random = new ScriptedRandom([.. QueueDraws(),
            I(0, 1, 0), .. EnemyHit(), I(0, 1, 0), I(0, 1, 1), .. EnemyHit(), .. PlayerHit(),
            F(.9f, 1.1f, 1), F(.9f, 1.1f, 1),
            I(0, 1, 0), I(0, 1, 1), .. EnemyHit()]);
        var d = Definition();
        // Baseline enemy hit20 leaves party1 at1, then its next hit clamps to1.
        d = d with { Players = [d.Players[0] with { Hp = 20 }, d.Players[1] with { Hp = 21, MaxHp = 21 }],
            Enemies = [d.Enemies[0] with { Hp = 43, Dexterity = 100 }, d.Enemies[1] with { Dexterity = 90 }] };
        var session = new ClassicBattleSession(d, random);
        session.SelectAttack(0, 0);
        session.SelectAttack(1, 0);
        session.ResolveNextAction();
        Assert.Equal(0, session.Current.Players[0].Hp);
        Assert.Null(session.Completion);
        session.ResolveNextAction();
        Assert.Equal(1, session.Current.Players[1].Hp);
        Assert.Equal(2, session.Current.LastAction!.TargetId); // role2, not party1
        Assert.Equal(1, session.Current.LastAction.TargetIndex);
        session.ResolveNextAction();
        var last = session.Current.LastAction;
        session.ResolveNextAction(); // party0's now-dead queued action is a pass
        Assert.Same(last, session.Current.LastAction);
        Assert.Equal(new ClassicBattleCounts(0, 0), session.Current.Players[0].Counts);
        Assert.Equal(new ClassicBattleCounts(1, 2), session.Current.Players[1].Counts);
        Assert.Equal(ClassicBattlePhase.Selecting, session.Current.Phase);
        Assert.Equal(1, session.Current.SelectingPlayerIndex);
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SelectAttack(1, 0));
        session.SelectAttack(1, 1);
        Assert.Equal(3, session.Current.ActionQueue.Count); // dead enemy omitted; dead player retained at speed0
        session.ResolveNextAction();
        Assert.Equal(ClassicBattlePhase.Lost, session.Current.Phase);
        Assert.All(session.Current.Players, p => Assert.Equal(0, p.Hp));
        Assert.Equal(100, session.Current.Enemies[1].Hp);
        Assert.Equal(ClassicBattlePhase.Lost, session.Completion!.Outcome);
        Assert.False(session.ResolveNextAction());
        random.AssertExhausted();
    }

    [Fact]
    public void Enemy_KO_before_its_queue_position_passes_without_target_random_or_duplicate_hit()
    {
        var random = new ScriptedRandom([.. QueueDraws(), .. PlayerHit(), I(0, 1, 1), I(0, 16, 16), .. PlayerHit()]);
        var d = Definition();
        d = d with { Players = [d.Players[0], d.Players[1] with { Dexterity = 60 }],
            Enemies = [d.Enemies[0] with { Hp = 43, Dexterity = 62 }, d.Enemies[1] with { Hp = 43, Dexterity = 52 }] };
        var session = new ClassicBattleSession(d, random);
        session.SelectAttack(0, 0);
        session.SelectAttack(1, 0);
        session.ResolveNextAction();
        var action = session.Current.LastAction;
        session.ResolveNextAction(); // dead enemy0 at speed80
        Assert.Same(action, session.Current.LastAction);
        Assert.Null(session.Completion);
        session.ResolveNextAction(); // enemy1 at70 targets party1/role2 and evades
        Assert.Equal(new ClassicBattleAction(1, ClassicBattleSide.Enemy, 2, 0, false, true)
            { ActorIndex = 1, TargetIndex = 1 }, session.Current.LastAction);
        session.ResolveNextAction();
        Assert.Equal(ClassicBattlePhase.Won, session.Current.Phase);
        Assert.All(session.Current.Players, p => Assert.Equal(200, p.Hp));
        random.AssertExhausted();
    }

    [Fact]
    public void Definition_lists_and_published_party_views_are_isolated_and_read_only()
    {
        var d = Definition();
        var players = d.Players.ToArray();
        var enemies = d.Enemies.ToArray();
        var random = new ScriptedRandom([.. QueueDraws(), .. PlayerHit()]);
        var session = new ClassicBattleSession(new(players, enemies), random);
        players[0] = players[0] with { Attack = 0 };
        enemies[0] = enemies[0] with { Hp = 1 };
        var initial = session.Current;
        Assert.Throws<NotSupportedException>(() => ((IList<ClassicBattlePlayerView>)initial.Players).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<ClassicBattleEnemyView>)initial.Enemies).Clear());
        session.SelectAttack(0, 0);
        session.SelectAttack(1, 1);
        Assert.Throws<NotSupportedException>(() => ((IList<ClassicBattleQueueEntry>)session.Current.ActionQueue).Clear());
        session.ResolveNextAction();
        Assert.Equal(57, session.Current.Enemies[0].Hp);
        Assert.Equal(100, initial.Enemies[0].Hp);
        Assert.Equal(new ClassicBattleCounts(0, 0), initial.Players[0].Counts);
        random.AssertExhausted();
    }

    [Fact]
    public void Whole_party_round_resets_selection_then_rerolls_instead_of_reusing_queue()
    {
        var random = new ScriptedRandom([.. QueueDraws(), .. PlayerHit(), .. PlayerHit(),
            I(0, 1, 0), I(0, 16, 10), I(0, 1, 1), I(0, 16, 16),
            F(.9f, 1.1f, 1.1f), F(.9f, 1.1f, 1.1f), F(.9f, 1.1f, .9f), F(.9f, 1.1f, .9f)]);
        var d = Definition();
        d = d with { Players = [d.Players[0] with { Dexterity = 22 }, d.Players[1] with { Dexterity = 20 }] };
        var session = new ClassicBattleSession(d, random);
        session.SelectAttack(0, 1);
        session.SelectAttack(1, 0);
        for (int i = 0; i < 4; i++) Assert.True(session.ResolveNextAction());
        Assert.Equal(ClassicBattlePhase.Selecting, session.Current.Phase);
        Assert.Equal(0, session.Current.SelectingPlayerIndex);
        Assert.All(session.Current.Players, p => Assert.Null(p.SelectedEnemySlot));
        Assert.All(session.Current.Players, p => Assert.Equal(new ClassicBattleCounts(1, 2), p.Counts));
        Assert.All(session.Current.Enemies, e => Assert.Equal(57, e.Hp));
        Assert.Empty(session.Current.ActionQueue);
        Assert.False(session.ResolveNextAction());
        session.SelectAttack(0, 0);
        session.SelectAttack(1, 1);
        Assert.Equal(2, session.Current.Round);
        Assert.Equal(new[] { ClassicBattleSide.Enemy, ClassicBattleSide.Enemy, ClassicBattleSide.Player, ClassicBattleSide.Player },
            session.Current.ActionQueue.Select(q => q.Side));
        random.AssertExhausted();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Either_multi_side_requires_explicit_indices(bool multiPlayers)
    {
        var d = Definition();
        d = multiPlayers ? d with { Enemies = [d.Enemies[0]] } : d with { Players = [d.Players[0]] };
        var random = new ScriptedRandom([]);
        var session = new ClassicBattleSession(d, random);
        Assert.Throws<InvalidOperationException>(() => session.SelectAttack(7));
        random.AssertExhausted();
    }

    [Fact]
    public void Duplicate_role_identity_is_rejected_before_random_even_when_party_indices_differ()
    {
        var d = Definition();
        d = d with { Players = [d.Players[0], d.Players[1] with { RoleId = 4 }] };
        var random = new ScriptedRandom([]);
        Assert.Throws<InvalidDataException>(() => new ClassicBattleSession(d, random));
        random.AssertExhausted();
    }

    [Theory]
    [InlineData(false, -20)] // WORD65534 / SHORT-2 sorts after empty SHORT-1
    [InlineData(false, -19)] // WORD65535 itself collides with the round-end sentinel
    [InlineData(true, -20)]
    [InlineData(true, -19)]
    public void Reference_sentinel_ends_round_before_negative_SHORT_or_FFFF_actor_without_action_random(bool party, int offset)
    {
        var d = Definition();
        d = d with { Enemies = [d.Enemies[0] with { Dexterity = offset }, d.Enemies[1] with { Dexterity = offset }] };
        if (!party) d = d with { Players = [d.Players[0]], Enemies = [d.Enemies[0]] };
        Draw[] queueDraws = party ? QueueDraws() : [F(.9f, 1.1f, 1), F(.9f, 1.1f, 1)];
        Draw[] attacks = party ? [.. PlayerHit(), .. PlayerHit()] : PlayerHit();
        var random = new ScriptedRandom([.. queueDraws, .. attacks, .. queueDraws]);
        var session = new ClassicBattleSession(d, random);
        if (party) { session.SelectAttack(0, 0); session.SelectAttack(1, 1); }
        else session.SelectAttack(7);
        Assert.Equal(party ? 2 : 1, session.Current.ActionQueue.Count);
        Assert.All(session.Current.ActionQueue, q => Assert.Equal(ClassicBattleSide.Player, q.Side));
        session.ResolveNextAction();
        if (party) session.ResolveNextAction();
        Assert.Equal(ClassicBattlePhase.Selecting, session.Current.Phase);
        Assert.All(session.Current.Players, p =>
        {
            Assert.Equal(200, p.Hp);
            Assert.Equal(new ClassicBattleCounts(1, 2), p.Counts);
        });
        var last = session.Current.LastAction;
        Assert.Equal(ClassicBattleSide.Player, last!.Attacker);
        Assert.False(session.ResolveNextAction());
        Assert.Same(last, session.Current.LastAction);
        if (party) { session.SelectAttack(0, 0); session.SelectAttack(1, 1); }
        else session.SelectAttack(7);
        Assert.Equal(2, session.Current.Round);
        random.AssertExhausted();
    }

    private static Draw[] QueueDraws() => [F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), F(.9f, 1.1f, 1)];
    private static Draw[] PlayerHit() => [I(1, 2, 1), I(0, 5, 1), F(1, 1.125f, 1), I(2, 3, 2)];
    private static Draw[] EnemyHit() => [I(0, 16, 0), I(0, 2, 0), I(0, 1, 0), I(1, 10, 1)];

    private static ClassicBattleDefinition Definition() => new(
        [new(4, 200, 200, 40, 20, 100), new(2, 200, 200, 40, 20, 90)],
        [new(7, 0, 100, 0, 0, 0, 1), new(7, 0, 100, 0, 0, 0, 1)]);
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
