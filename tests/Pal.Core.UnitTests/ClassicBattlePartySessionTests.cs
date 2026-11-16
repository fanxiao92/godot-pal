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

    [Fact]
    public void Three_players_pick_targets_in_party_order_execute_queue_and_win()
    {
        // battle.c:27-31 g_rgPlayerPos[2] and battle.h MAX_PLAYERS_IN_PARTY: the combat domain
        // admits three living players; fight.c's queue keeps its 3+5*2 reference capacity.
        var random = new ScriptedRandom([.. QueueDraws3(), .. PlayerHit(), .. PlayerHit(), .. PlayerHit()]);
        var d = ThreePlayerDefinition();
        d = d with { Enemies = [d.Enemies[0] with { Hp = 86 }, d.Enemies[1] with { Hp = 43 }] };
        var session = new ClassicBattleSession(d, random);
        Assert.Equal(ClassicBattlePhase.Selecting, session.Current.Phase);
        Assert.Equal(0, session.Current.SelectingPlayerIndex);
        session.SelectAttack(0, 0);
        Assert.Equal(1, session.Current.SelectingPlayerIndex);
        Assert.Equal(0, session.Current.Players[0].SelectedEnemySlot);
        session.SelectAttack(1, 1);
        Assert.Equal(2, session.Current.SelectingPlayerIndex);
        Assert.Equal(1, session.Current.Players[1].SelectedEnemySlot);
        session.SelectAttack(2, 0);
        Assert.Equal(ClassicBattlePhase.Resolving, session.Current.Phase);
        Assert.Null(session.Current.SelectingPlayerIndex);
        Assert.Equal(1, session.Current.Round);
        Assert.Equal(5, session.Current.ActionQueue.Count);
        Assert.Equal(new[] { 4, 2, 1 }, session.Current.Players.Select(p => p.RoleId));
        Assert.True(session.ResolveNextAction());
        Assert.Equal((0, 0, 7, 43), (session.Current.LastAction!.ActorIndex, session.Current.LastAction.TargetIndex,
            session.Current.LastAction.TargetId, session.Current.LastAction.Damage));
        Assert.Equal(43, session.Current.Enemies[0].Hp);
        Assert.True(session.ResolveNextAction());
        Assert.Equal((1, 1, 7, 43), (session.Current.LastAction!.ActorIndex, session.Current.LastAction.TargetIndex,
            session.Current.LastAction.TargetId, session.Current.LastAction.Damage));
        Assert.Equal(0, session.Current.Enemies[1].Hp);
        Assert.True(session.ResolveNextAction());
        Assert.Equal((2, 0, 7, 43), (session.Current.LastAction!.ActorIndex, session.Current.LastAction.TargetIndex,
            session.Current.LastAction.TargetId, session.Current.LastAction.Damage));
        Assert.Equal(ClassicBattlePhase.Won, session.Current.Phase);
        Assert.All(session.Current.Enemies, e => Assert.Equal(0, e.Hp));
        Assert.All(session.Current.Players, p => Assert.Equal(new ClassicBattleCounts(1, 2), p.Counts));
        var receipt = session.Completion;
        Assert.Equal(ClassicBattlePhase.Won, receipt!.Outcome);
        Assert.False(session.ResolveNextAction());
        Assert.Same(receipt, session.Completion);
        random.AssertExhausted();
    }

    [Fact]
    public void Party_domain_still_rejects_empty_four_and_duplicate_player_sets()
    {
        // The expanded 1..3 domain keeps the reference bounds; this is not a deleted count check.
        var random = new ScriptedRandom([]);
        var d = ThreePlayerDefinition();
        Assert.Throws<InvalidDataException>(() => new ClassicBattleSession(d with { Players = [] }, random));
        Assert.Throws<InvalidDataException>(() => new ClassicBattleSession(d with
            { Players = [.. d.Players, new(3, 200, 200, 40, 20, 70)] }, random));
        Assert.Throws<InvalidDataException>(() => new ClassicBattleSession(d with
            { Players = [d.Players[0], d.Players[1] with { RoleId = 4 }, d.Players[2]] }, random));
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

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void One_to_five_complete_enemy_slots_accept_one_to_three_players_without_random(int enemyCount)
    {
        foreach (int playerCount in new[] { 1, 2, 3 })
        {
            var base_ = ThreePlayerDefinition() with { Enemies = Enumerable.Repeat(ThreePlayerDefinition().Enemies[0], 5).ToArray() };
            var random = new ScriptedRandom([]);
            var session = new ClassicBattleSession(base_ with
            {
                Players = base_.Players.Take(playerCount).ToArray(), Enemies = base_.Enemies.Take(enemyCount).ToArray()
            }, random);
            Assert.Equal(ClassicBattlePhase.Selecting, session.Current.Phase);
            Assert.Equal(playerCount, session.Current.Players.Count);
            Assert.Equal(Enumerable.Range(0, enemyCount), session.Current.Enemies.Select(e => e.Slot));
            Assert.All(session.Current.Enemies, e => Assert.Equal((7, 100), (e.Id, e.Hp)));
            Assert.Null(session.Completion);
            Assert.False(session.ResolveNextAction());
            random.AssertExhausted();
        }
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("six")]
    [InlineData("null-slot")]
    [InlineData("four-players")]
    [InlineData("magic-slot4")]
    [InlineData("all-empty")]
    [InlineData("empty-id")]
    [InlineData("empty-attack")]
    [InlineData("empty-end-script")]
    public void Expanded_capacity_keeps_invalid_and_unsupported_definitions_out_before_random(string fault)
    {
        var d = FiveEnemyDefinition();
        var enemies = d.Enemies.ToArray();
        if (fault == "null-slot") enemies[4] = null!;
        if (fault == "magic-slot4") enemies[4] = enemies[4] with { MagicId = 1 };
        if (fault == "all-empty") { for (int i = 0; i < 5; i++) enemies[i] = EmptyEnemy(); }
        if (fault == "empty-id") enemies[4] = EmptyEnemy() with { Id = 7 };
        if (fault == "empty-attack") enemies[4] = EmptyEnemy() with { Attack = 1 };
        if (fault == "empty-end-script") enemies[4] = EmptyEnemy() with { BattleEndScript = 1 };
        d = d with { Enemies = fault == "empty" ? [] : fault == "six" ? [.. enemies, enemies[0]] : enemies };
        if (fault == "four-players") d = d with { Players = [.. d.Players,
            d.Players[0] with { RoleId = 0 }, d.Players[0] with { RoleId = 1 }] };
        var random = new ScriptedRandom([]);
        Assert.Throws<InvalidDataException>(() => new ClassicBattleSession(d, random));
        random.AssertExhausted();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Every_repeated_ID_slot_is_selectable_and_invalid_boundaries_leave_snapshot_and_random_unchanged(int slot)
    {
        var random = new ScriptedRandom(QueueDraws(5));
        var session = new ClassicBattleSession(FiveEnemyDefinition(), random);
        var initial = session.Current;
        foreach (int invalid in new[] { -1, 5, 7 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => session.SelectAttack(0, invalid));
            Assert.Same(initial, session.Current);
        }
        session.SelectAttack(0, slot);
        Assert.Equal(slot, session.Current.Players[0].SelectedEnemySlot);
        Assert.Equal(1, session.Current.SelectingPlayerIndex);
        Assert.Null(initial.Players[0].SelectedEnemySlot);
        Assert.Empty(session.Current.ActionQueue);
        Assert.False(session.ResolveNextAction());
        var partial = session.Current;
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SelectAttack(1, 5));
        Assert.Same(partial, session.Current);
        session.SelectAttack(1, slot);
        Assert.Equal(slot, session.Current.Players[1].SelectedEnemySlot);
        Assert.Equal(ClassicBattlePhase.Resolving, session.Current.Phase);
        random.AssertExhausted();
    }

    [Theory]
    [InlineData(18, 18, 1f, 1f, 1f, 1f, 1f, 1f, 1f, "E0:18,E1:18,E2:18,E3:18,E4:18,P0:18,P1:18")]
    [InlineData(1, 100, 1f, 1f, 1f, 1f, 1f, 1f, 1f, "P1:100,E1:18,E2:18,E3:18,E4:18,E0:18,P0:1")]
    [InlineData(22, 22, 1.1f, .9f, 1f, 1.05f, .95f, .9f, 1.1f, "P1:24,P0:19,E0:19,E2:18,E3:18,E4:17,E1:16")]
    public void Five_enemy_queue_matches_reference_enemy_first_draws_and_nonstable_nested_comparisons(
        int dex0, int dex1, float e0, float e1, float e2, float e3, float e4, float p0, float p1, string expected)
    {
        // fight.c:1462-1601. Every enemy starts at (0+6)*3=18; WORD truncates each draw.
        // In row2 the late P1 swap displaces E0 behind E1..E4, unlike a stable sort.
        var d = FiveEnemyDefinition();
        d = d with { Players = [d.Players[0] with { Dexterity = dex0 }, d.Players[1] with { Dexterity = dex1 }] };
        var random = new ScriptedRandom([F(.9f, 1.1f, e0), F(.9f, 1.1f, e1), F(.9f, 1.1f, e2),
            F(.9f, 1.1f, e3), F(.9f, 1.1f, e4), F(.9f, 1.1f, p0), F(.9f, 1.1f, p1)]);
        var session = new ClassicBattleSession(d, random);
        session.SelectAttack(0, 4);
        Assert.Empty(session.Current.ActionQueue);
        session.SelectAttack(1, 0);
        Assert.Equal(expected, string.Join(",", session.Current.ActionQueue.Select(q =>
            $"{(q.Side == ClassicBattleSide.Enemy ? "E" : "P")}{q.ActorIndex}:{q.Dexterity}")));
        random.AssertExhausted();
    }

    [Fact]
    public void Five_enemy_retarget_wraps_four_to_zero_skips_corpses_and_wins_only_on_last_KO_with_unique_receipt()
    {
        // Each hit43: attack40 vs effective defense24 => base42, +1, noncritical, scale1.
        // Enemy speeds22..18 preserve E0..E4 order behind P0=100/P1=90.
        // R1: 7 queue floats; P0 kills4, P1 wraps to0 (four draws per hit).
        // E0/E4 pass with no draws; E1/E2/E3 each draw target0..1 then evade0..16.
        // R2: E1/E2/E3/P0/P1 = 5 floats; kills3 then wraps to1; only E2 evades.
        // R3: E2/P0/P1 = 3 floats; P0 kills last2; no further action draws.
        var random = new ScriptedRandom([.. QueueDraws(5), .. PlayerHit(), .. PlayerHit(),
            I(0, 1, 0), I(0, 16, 16), I(0, 1, 1), I(0, 16, 16), I(0, 1, 0), I(0, 16, 16),
            .. QueueDraws(3), .. PlayerHit(), .. PlayerHit(), I(0, 1, 1), I(0, 16, 16),
            .. QueueDraws(1), .. PlayerHit()]);
        var d = FiveEnemyDefinition();
        var players = d.Players.ToArray();
        var enemies = d.Enemies.Select((e, i) => e with { Hp = 43, Dexterity = 4 - i }).ToArray();
        var session = new ClassicBattleSession(new(players, enemies), random);
        var initial = session.Current;
        players[0] = players[0] with { Attack = 0 };
        enemies[4] = enemies[4] with { Hp = 1 };
        Assert.Throws<NotSupportedException>(() => ((IList<ClassicBattleEnemyView>)initial.Enemies).Clear());
        session.SelectAttack(0, 4);
        session.SelectAttack(1, 4);
        var queued = session.Current;
        Assert.True(session.ResolveNextAction());
        Assert.Equal((0, 4, 7, 43), (session.Current.LastAction!.ActorIndex, session.Current.LastAction.TargetIndex,
            session.Current.LastAction.TargetId, session.Current.LastAction.Damage));
        Assert.True(session.ResolveNextAction());
        Assert.Equal((1, 0, 7, 43), (session.Current.LastAction!.ActorIndex, session.Current.LastAction.TargetIndex,
            session.Current.LastAction.TargetId, session.Current.LastAction.Damage));
        Assert.Equal(new[] { 0, 43, 43, 43, 0 }, session.Current.Enemies.Select(e => e.Hp));
        Assert.Equal(ClassicBattlePhase.Resolving, session.Current.Phase);
        Assert.Null(session.Completion);
        var last = session.Current.LastAction;
        Assert.True(session.ResolveNextAction()); // dead E0
        Assert.Same(last, session.Current.LastAction);
        for (int i = 1; i <= 3; i++)
        {
            Assert.True(session.ResolveNextAction());
            Assert.Equal(i, session.Current.LastAction!.ActorIndex);
            Assert.Equal(ClassicBattleSide.Enemy, session.Current.LastAction.Attacker);
        }
        last = session.Current.LastAction;
        Assert.True(session.ResolveNextAction()); // dead E4
        Assert.Same(last, session.Current.LastAction);
        Assert.Equal(ClassicBattlePhase.Selecting, session.Current.Phase);
        var beforeInvalid = session.Current;
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SelectAttack(0, 4));
        Assert.Same(beforeInvalid, session.Current);
        session.SelectAttack(0, 3);
        session.SelectAttack(1, 3);
        Assert.True(session.ResolveNextAction());
        Assert.Equal(3, session.Current.LastAction!.TargetIndex);
        Assert.True(session.ResolveNextAction());
        Assert.Equal(1, session.Current.LastAction!.TargetIndex);
        Assert.Equal(new[] { 0, 0, 43, 0, 0 }, session.Current.Enemies.Select(e => e.Hp));
        Assert.Null(session.Completion);
        for (int i = 0; i < 3; i++) Assert.True(session.ResolveNextAction());
        Assert.Equal(ClassicBattlePhase.Selecting, session.Current.Phase);
        session.SelectAttack(0, 2);
        session.SelectAttack(1, 2);
        Assert.True(session.ResolveNextAction());
        Assert.Equal(2, session.Current.LastAction!.TargetIndex);
        Assert.Equal(ClassicBattlePhase.Won, session.Current.Phase);
        Assert.All(session.Current.Enemies, e => Assert.Equal(0, e.Hp));
        Assert.Equal(new ClassicBattleCounts(3, 6), session.Current.Players[0].Counts);
        Assert.Equal(new ClassicBattleCounts(2, 4), session.Current.Players[1].Counts);
        Assert.All(initial.Enemies, e => Assert.Equal(43, e.Hp));
        Assert.All(queued.Enemies, e => Assert.Equal(43, e.Hp));
        Assert.All(initial.Players, p => Assert.Equal(new ClassicBattleCounts(0, 0), p.Counts));
        var receipt = Assert.IsType<ClassicBattleSession.CompletionReceipt>(session.Completion);
        Assert.Equal(ClassicBattlePhase.Won, receipt.Outcome);
        var completed = session.Current;
        Assert.False(session.ResolveNextAction());
        Assert.Throws<InvalidOperationException>(() => session.SelectAttack(0, 2));
        Assert.Same(completed, session.Current);
        Assert.Same(receipt, session.Completion);
        random.AssertExhausted();
    }

    [Fact]
    public void Five_fast_enemy_actors_hit_independently_in_slot_order_not_repeated_ID_order()
    {
        // Effective strengths36..40 vs defense20 => base40,42,44,46,48 /2 =20..24.
        // Queue118..114 precedes P0=100/P1=90; target sequence0,1,0,1,0.
        var d = FiveEnemyDefinition();
        d = d with { Enemies = d.Enemies.Select((e, i) => e with { Hp = 100 + i, Attack = i, Dexterity = 100 - i }).ToArray() };
        var random = new ScriptedRandom([.. QueueDraws(5),
            I(0, 1, 0), .. EnemyHit(), I(0, 1, 1), .. EnemyHit(), I(0, 1, 0), .. EnemyHit(),
            I(0, 1, 1), .. EnemyHit(), I(0, 1, 0), .. EnemyHit()]);
        var session = new ClassicBattleSession(d, random);
        var initial = session.Current;
        session.SelectAttack(0, 4);
        session.SelectAttack(1, 0);
        int[] hp0 = [180, 180, 158, 158, 134], hp1 = [200, 179, 179, 156, 156];
        for (int i = 0; i < 5; i++)
        {
            Assert.True(session.ResolveNextAction());
            Assert.Equal(new ClassicBattleAction(1, ClassicBattleSide.Enemy, i % 2 == 0 ? 4 : 2, 20 + i, false, false)
                { ActorIndex = i, TargetIndex = i % 2 }, session.Current.LastAction);
            Assert.Equal(new[] { hp0[i], hp1[i] }, session.Current.Players.Select(p => p.Hp));
            Assert.Equal(new[] { 100, 101, 102, 103, 104 }, session.Current.Enemies.Select(e => e.Hp));
            Assert.Null(session.Completion);
        }
        Assert.All(initial.Players, p => Assert.Equal(200, p.Hp));
        random.AssertExhausted();
    }

    [Fact]
    public void Empty_source_slots_join_the_runtime_domain_with_explicit_views_and_never_draw()
    {
        // battle.c:1617-1717: a zero source row keeps a real runtime slot (wObjectID 0) — it is
        // never a selectable target and consumes no queue randomness; a dense team stays legal.
        var d = Definition();
        d = d with { Enemies = [d.Enemies[0], EmptyEnemy(), d.Enemies[1], EmptyEnemy()] };
        var random = new ScriptedRandom(QueueDraws(2));
        var session = new ClassicBattleSession(d, random);
        Assert.Equal(new[] { (0, 7, 100, false), (1, 0, 0, true), (2, 7, 100, false), (3, 0, 0, true) },
            session.Current.Enemies.Select(e => (e.Slot, e.Id, e.Hp, e.IsEmpty)));
        var initial = session.Current;
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SelectAttack(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SelectAttack(0, 3));
        Assert.Same(initial, session.Current);
        session.SelectAttack(0, 0);
        session.SelectAttack(1, 2);
        Assert.Equal(ClassicBattlePhase.Resolving, session.Current.Phase);
        // Only the two real enemies entered the queue; the empty slots drew no speed.
        Assert.Equal(new[] { 0, 2 }, session.Current.ActionQueue.Where(q => q.Side == ClassicBattleSide.Enemy)
            .Select(q => q.ActorIndex).OrderBy(actor => actor).ToArray());
        random.AssertExhausted();
    }

    [Fact]
    public void Mixed_slots_retarget_across_empties_settle_only_real_KOs_and_keep_dead_identity()
    {
        // Each hit is 43: attack40 against effective defense24, +1, no critical, scale1.
        // Both players target slot2; P0 kills it, P1 wraps across the empty slot3 to slot0 and
        // kills the last real enemy. The empty slots never act, redraw or add their zero rewards.
        var enemy = Definition().Enemies[0] with { Experience = 10, Cash = 5 };
        var d = Definition();
        d = d with { Enemies = [enemy with { Hp = 43 }, EmptyEnemy(), enemy with { Hp = 43 }, EmptyEnemy()] };
        var random = new ScriptedRandom([.. QueueDraws(2), .. PlayerHit(), .. PlayerHit()]);
        var session = new ClassicBattleSession(d, random);
        session.SelectAttack(0, 2);
        session.SelectAttack(1, 2);
        Assert.True(session.ResolveNextAction());
        Assert.Equal(2, session.Current.LastAction!.TargetIndex);
        Assert.Null(session.Completion);
        Assert.True(session.ResolveNextAction());
        Assert.Equal(0, session.Current.LastAction!.TargetIndex); // wrapped over empty slot 3
        Assert.Equal(7, session.Current.LastAction.TargetId);
        Assert.Equal(ClassicBattlePhase.Won, session.Current.Phase);
        // Killed real enemies keep their battle id (IsEmpty=false); only source-empty slots stay empty.
        Assert.Equal(new[] { (7, 0, false), (0, 0, true), (7, 0, false), (0, 0, true) },
            session.Current.Enemies.Select(e => (e.Id, e.Hp, e.IsEmpty)));
        Assert.Equal(20, session.ExperienceGained);
        Assert.Equal(10, session.CashGained);
        var receipt = session.Completion;
        Assert.False(session.ResolveNextAction());
        Assert.Same(receipt, session.Completion);
        random.AssertExhausted();
    }

    private static ClassicBattleDefinition FiveEnemyDefinition()
    {
        var d = Definition();
        return d with { Enemies = Enumerable.Repeat(d.Enemies[0], 5).ToArray() };
    }
    // battle.c:1617-1717: the zero source row's canonical runtime slot (wObjectID 0).
    private static ClassicBattleEnemy EmptyEnemy() => new(0, 0, 0, 0, 0, 0, 0) { IsEmpty = true };
    private static ClassicBattleDefinition ThreePlayerDefinition() => new(
        [new(4, 200, 200, 40, 20, 100), new(2, 200, 200, 40, 20, 90), new(1, 200, 200, 40, 20, 80)],
        [new(7, 0, 100, 0, 0, 0, 1), new(7, 0, 100, 0, 0, 0, 1)]);
    private static Draw[] QueueDraws3() => Enumerable.Repeat(F(.9f, 1.1f, 1), 5).ToArray();
    private static Draw[] QueueDraws(int livingEnemies) => Enumerable.Repeat(F(.9f, 1.1f, 1), livingEnemies + 2).ToArray();
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
