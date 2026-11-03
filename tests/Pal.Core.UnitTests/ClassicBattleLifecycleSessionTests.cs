using Xunit;

namespace Pal.Core.Tests;

public sealed class ClassicBattleLifecycleSessionTests
{
    [Theory]
    [InlineData(100, 150, 20, 50, 125, 35)]
    [InlineData(101, 150, 21, 50, 125, 35)]
    public void Real_won_settles_half_missing_hp_mp_and_keeps_action_counts(
        int hp, int maxHp, int mp, int maxMp, int finalHp, int finalMp)
    {
        var role = Role() with { Hp = hp, MaxHp = maxHp, Mp = mp, MaxMp = maxMp };
        var random = WinningRandom();
        var session = Start(role, random);
        Assert.Null(session.Completion);
        Assert.False(session.OwnsCompletion(null));
        Assert.True(session.Current.IsInBattle);
        Assert.All(session.Current.Role.HiddenExperience.Values, xp => Assert.Equal(0, xp.Count));
        session.SelectAttack(7);
        Assert.True(session.ResolveNextAction());
        Assert.Equal(ClassicBattlePhase.Won, session.Completion!.Outcome);
        Assert.True(session.OwnsCompletion(session.Completion));
        Assert.False(session.Current.IsInBattle);
        Assert.Equal(finalHp, session.Current.Role.Hp);
        Assert.Equal(finalMp, session.Current.Role.Mp);
        Assert.Equal(new BattleExperienceSnapshot(5, 2, 17), session.Current.Role.PrimaryExperience);
        Assert.Equal(new BattleExperienceSnapshot(7, 3, 1), session.Current.Role.HiddenExperience["attack"]);
        Assert.Equal(new BattleExperienceSnapshot(7, 3, 2), session.Current.Role.HiddenExperience["health"]);
        Assert.Equal(2, session.Current.Role.Level);
        Assert.Equal(9, session.Current.Role.Magics[0]);
        Assert.Equal(43, session.Current.Combat.LastAction!.Damage);
        Assert.Equal(hp, role.Hp);
        random.AssertExhausted();
    }

    [Fact]
    public void Lost_keeps_zero_hp_no_mp_recovery_and_real_counts_then_next_start_revives_and_resets()
    {
        var random = new ScriptedRandom([F(.9f, 1.1f, 1), F(.9f, 1.1f, 1),
            I(1, 2, 1), I(0, 5, 1), F(1, 1.125f, 1), I(2, 3, 3),
            I(0, 0, 0), I(0, 16, 9), I(0, 2, 0), I(0, 1, 1), I(1, 10, 10)]);
        var session = Start(Role() with { Hp = 20 }, random, enemy: new(7, 0, 100, 0, 0, 0, 1));
        session.SelectAttack(7);
        session.ResolveNextAction();
        Assert.Null(session.Completion);
        Assert.Equal(1, session.Current.Role.HiddenExperience["attack"].Count);
        session.ResolveNextAction();
        Assert.Equal(ClassicBattlePhase.Lost, session.Completion!.Outcome);
        Assert.Equal(0, session.Current.Role.Hp);
        Assert.Equal(20, session.Current.Role.Mp);
        Assert.Equal(new BattleExperienceSnapshot(7, 3, 3), session.Current.Role.HiddenExperience["health"]);
        Assert.Equal(new BattleExperienceSnapshot(7, 3, 1), session.Current.Role.HiddenExperience["attack"]);
        Assert.False(session.Current.IsInBattle);
        Assert.All(session.Current.Role.Statuses, value => Assert.Equal(0, value));
        random.AssertExhausted();

        var nextRandom = WinningRandom();
        var next = Start(session.Current.Role, nextRandom);
        Assert.Equal(1, next.Current.Role.Hp);
        Assert.Equal(0, session.Current.Role.Hp);
        Assert.All(next.Current.Role.HiddenExperience.Values, xp => Assert.Equal(0, xp.Count));
        Assert.Equal(17, next.Current.Role.PrimaryExperience.Count);
        Assert.False(next.OwnsCompletion(session.Completion));
        next.SelectAttack(7);
        next.ResolveNextAction();
        Assert.Equal(75, next.Current.Role.Hp);
        Assert.Equal(35, next.Current.Role.Mp);
        nextRandom.AssertExhausted();
    }

    [Fact]
    public void Completed_lifecycle_is_immutable_idempotent_and_session_bound()
    {
        var random = WinningRandom();
        var session = Start(Role(), random);
        ClassicBattleLifecycleView initial = session.Current;
        Assert.False(session.ResolveNextAction());
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SelectAttack(8));
        Assert.Same(initial, session.Current);
        session.SelectAttack(7);
        Assert.Throws<InvalidOperationException>(() => session.SelectAttack(7));
        session.ResolveNextAction();
        var terminal = session.Current;
        var receipt = session.Completion;
        Assert.False(session.ResolveNextAction());
        Assert.Throws<InvalidOperationException>(() => session.SelectAttack(7));
        Assert.Same(terminal, session.Current);
        Assert.Same(receipt, session.Completion);
        Assert.Equal(100, initial.Role.Hp);
        Assert.Equal(0, initial.Role.HiddenExperience["attack"].Count);
        var otherRandom = WinningRandom();
        var other = Start(terminal.Role, otherRandom);
        Assert.Equal(125, other.Current.Role.Hp);
        other.SelectAttack(7);
        other.ResolveNextAction();
        Assert.False(session.OwnsCompletion(other.Completion));
        Assert.False(other.OwnsCompletion(receipt));
        Assert.Equal(137, other.Current.Role.Hp);
        random.AssertExhausted();
        otherRandom.AssertExhausted();
    }

    [Fact]
    public void Role_and_growth_inputs_and_old_views_are_deeply_isolated()
    {
        BattleRoleSnapshot role = Role();
        ClassicBattleGrowthData growth = Growth();
        var random = WinningRandom();
        var session = Start(role, random, growth);
        ((int[])role.Equipment)[0] = 99;
        ((int[])role.Magics)[0] = 88;
        ((int[])role.Statuses)[0] = 1000;
        ((Dictionary<string, BattleExperienceSnapshot>)role.HiddenExperience)["attack"] = new(600, 99, 500);
        ((int[])growth.LevelUpExperience)[2] = 1;
        ((BattleLevelUpMagic[])growth.LevelUpMagics)[0] = new(1, 77);
        var before = session.Current;
        Assert.Throws<NotSupportedException>(() => ((IList<int>)before.Role.Magics)[0] = 66);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, BattleExperienceSnapshot>)before.Role.HiddenExperience)["health"] = new(0, 0, 0));
        session.SelectAttack(7);
        session.ResolveNextAction();
        Assert.Equal(9, session.Current.Role.Magics[0]);
        Assert.Equal(0, session.Current.Role.Equipment[0]);
        Assert.Equal(new BattleExperienceSnapshot(7, 3, 1), session.Current.Role.HiddenExperience["attack"]);
        Assert.Equal(0, before.Role.HiddenExperience["attack"].Count);
        random.AssertExhausted();
    }

    [Theory]
    [InlineData("attack", "ATTACK")]
    [InlineData("magicPower", "magicpower")]
    public void Noncanonical_hidden_names_are_rejected_before_random_regardless_of_dictionary_comparer(string canonical, string alias)
    {
        var source = Role();
        var hidden = source.HiddenExperience.ToDictionary(pair => pair.Key == canonical ? alias : pair.Key,
            pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var role = source with { HiddenExperience = hidden };
        var random = new ScriptedRandom([]);
        Assert.Throws<InvalidDataException>(() => Start(role, random));
        Assert.Equal(100, role.Hp);
        Assert.Equal(55, hidden[alias].Count);
        random.AssertExhausted();
    }

    [Fact]
    public void Canonical_hidden_names_remain_supported_with_case_insensitive_input_dictionary()
    {
        var source = Role();
        var role = source with { HiddenExperience = source.HiddenExperience.ToDictionary(pair => pair.Key,
            pair => pair.Value, StringComparer.OrdinalIgnoreCase) };
        var random = new ScriptedRandom([]);
        var session = Start(role, random);
        Assert.Equal(0, session.Current.Role.HiddenExperience["attack"].Count);
        Assert.Equal(55, role.HiddenExperience["attack"].Count);
        Assert.True(session.Current.IsInBattle);
        random.AssertExhausted();
    }

    public static IEnumerable<object[]> UnsupportedGrowth()
    {
        var role = Role();
        var growth = Growth();
        yield return [role with { Level = 100 }, growth];
        yield return [role, growth with { LevelUpExperience = new int[100] }];
        yield return [role, growth with { LevelUpExperience = new int[99] }];
        yield return [role with { Mp = 51 }, growth];
        yield return [role with { Magics = [] }, growth];
        yield return [role with { Statuses = [0, 0, 0, 0, 0, 0, 0, 0, 1000] }, growth];
        yield return [role with { Equipment = [1, 0, 0, 0, 0, 0] }, growth];
        yield return [role with { Attack = 65536 }, growth];
        foreach (string name in new[] { "health", "magic", "attack", "magicPower", "defense", "dexterity", "flee" })
        {
            var hidden = role.HiddenExperience.ToDictionary(x => x.Key, x => x.Value);
            hidden[name] = new(7, 100, 55);
            yield return [role with { HiddenExperience = hidden }, growth];
        }
    }

    [Theory]
    [MemberData(nameof(UnsupportedGrowth))]
    public void Unsupported_growth_rejected_before_start_changes_or_random(BattleRoleSnapshot role, ClassicBattleGrowthData growth)
    {
        var random = new ScriptedRandom([]);
        int hp = role.Hp;
        BattleExperienceSnapshot primary = role.PrimaryExperience;
        var counts = role.HiddenExperience.Values.Select(x => x.Count).ToArray();
        Assert.Throws<InvalidDataException>(() => Start(role, random, growth));
        Assert.Equal(hp, role.Hp);
        Assert.Equal(primary, role.PrimaryExperience);
        Assert.Equal(counts, role.HiddenExperience.Values.Select(x => x.Count));
        random.AssertExhausted();
    }

    [Fact]
    public void Thresholds_use_role_level_for_primary_and_independent_hidden_levels()
    {
        var table = Enumerable.Repeat(1, 100).ToArray();
        table[2] = 6;
        table[3] = 8;
        var random = WinningRandom();
        var session = Start(Role() with { PrimaryExperience = new(5, 90, 17) }, random, Growth() with { LevelUpExperience = table });
        session.SelectAttack(7);
        session.ResolveNextAction();
        Assert.Equal(new BattleExperienceSnapshot(5, 90, 17), session.Current.Role.PrimaryExperience);
        Assert.Equal(new BattleExperienceSnapshot(7, 3, 0), session.Current.Role.HiddenExperience["magicPower"]);
        random.AssertExhausted();
    }

    [Fact]
    public void Two_round_won_keeps_real_weights_and_does_not_confuse_raw_hp_with_settled_hp()
    {
        var random = new ScriptedRandom([
            F(.9f, 1.1f, 1.1f), F(.9f, 1.1f, .9f), I(0, 0, 0), I(0, 16, 0), I(0, 2, 0), I(0, 1, 0), I(1, 10, 10),
            I(1, 2, 1), I(0, 5, 1), F(1, 1.125f, 1), I(2, 3, 2),
            F(.9f, 1.1f, .9f), F(.9f, 1.1f, 1.1f), I(1, 2, 2), I(0, 5, 0), F(1, 1.125f, 1), I(2, 3, 3)]);
        var session = Start(Role() with { Hp = 200, MaxHp = 200 }, random, dexterity: 22, enemy: new(7, 0, 100, 0, 0, 0, 1));
        session.SelectAttack(7);
        session.ResolveNextAction();
        session.ResolveNextAction();
        Assert.Null(session.Completion);
        Assert.Equal(180, session.Current.Role.Hp);
        Assert.Equal(57, session.Current.Combat.EnemyHp);
        session.SelectAttack(7);
        session.ResolveNextAction();
        Assert.Equal(180, session.Current.Combat.PlayerHp);
        Assert.Equal(190, session.Current.Role.Hp);
        Assert.Equal(new BattleExperienceSnapshot(7, 3, 2), session.Current.Role.HiddenExperience["attack"]);
        Assert.Equal(new BattleExperienceSnapshot(7, 3, 5), session.Current.Role.HiddenExperience["health"]);
        foreach (string name in new[] { "magic", "magicPower", "defense", "dexterity", "flee" })
            Assert.Equal(new BattleExperienceSnapshot(7, 3, 0), session.Current.Role.HiddenExperience[name]);
        Assert.Equal(200, session.Current.Role.MaxHp);
        Assert.Equal(50, session.Current.Role.MaxMp);
        random.AssertExhausted();
    }

    [Fact]
    public void Full_magic_slots_cannot_learn_and_current_known_future_and_zero_rows_are_legal()
    {
        var role = Role() with { Magics = Enumerable.Range(1, 32).ToArray() };
        var random = WinningRandom();
        var session = Start(role, random, Growth() with { LevelUpMagics = [new(1, 9), new(2, 0), new(3, 90), new(2, 91)] });
        session.SelectAttack(7);
        session.ResolveNextAction();
        Assert.Equal(Enumerable.Range(1, 32), session.Current.Role.Magics);
        random.AssertExhausted();
    }

    [Fact]
    public void Hp_zero_with_unsupported_combat_is_rejected_without_reviving_or_resetting_source()
    {
        var role = Role() with { Hp = 0 };
        var random = new ScriptedRandom([]);
        Assert.Throws<InvalidDataException>(() => new ClassicBattleLifecycleSession(role,
            new(1, 0, 150, 40, 20, 100), new(7, 0, 43, 0, 0, 0, 1) { MagicId = 1 }, Growth(), random));
        Assert.Equal(0, role.Hp);
        Assert.Equal(55, role.HiddenExperience["health"].Count);
        random.AssertExhausted();
    }

    // The victory settlement below is hand-derived from PAL_BattleWon (battle.c 1003-1349) and
    // PAL_PlayerLevelUp (global.c 2347): every KO accumulates its WORD reward exactly once, only
    // a Won outcome grants them, each surviving member gains the full experience, and the primary
    // level-up consumes its six draws (10+0..7, 8+0..5, 4+0..1, 4+0..1, 2+0..1, 2+0..1) before the
    // hidden tracks distribute (exp*count/total)*2 in the health, magic, attack, magicPower,
    // defense, dexterity, flee order with one 1..2 draw per crossed hidden threshold.
    [Fact]
    public void Won_grants_ko_rewards_with_full_primary_growth_hidden_ratios_and_magic_learning()
    {
        // primary 5+120 crosses the level-2 threshold 100 once (remainder 25); hidden counts 2/1
        // over total 3 carry 7 into (120*2/3)*2=160 and (120*1/3)*2=80; the level-3 row learns at
        // the first free magic slot; the level-up recovery then covers all battle damage.
        var random = new ScriptedRandom([.. WinningDraws(),
            I(0, 7, 7), I(0, 5, 5), I(0, 1, 1), I(0, 1, 0), I(0, 1, 1), I(0, 1, 0), I(1, 2, 2)]);
        var session = Start(Role(), random, enemy: new(7, 0, 43, 0, 0, 0, 1) { Experience = 120, Cash = 250 });
        session.SelectAttack(7);
        Assert.True(session.ResolveNextAction());
        Assert.Equal(ClassicBattlePhase.Won, session.Completion!.Outcome);
        BattleRoleSnapshot settled = session.Current.Role;
        Assert.Equal(3, settled.Level);
        Assert.Equal(169, settled.MaxHp); // 150 +10+7, hidden health +2
        Assert.Equal(169, settled.Hp);
        Assert.Equal(63, settled.MaxMp); // 50 +8+5
        Assert.Equal(63, settled.Mp);
        Assert.Equal(new BattleExperienceSnapshot(25, 3, 17), settled.PrimaryExperience);
        Assert.Equal(new BattleExperienceSnapshot(67, 4, 2), settled.HiddenExperience["health"]);
        Assert.Equal(new BattleExperienceSnapshot(87, 3, 1), settled.HiddenExperience["attack"]);
        foreach (string name in new[] { "magic", "magicPower", "defense", "dexterity", "flee" })
            Assert.Equal(new BattleExperienceSnapshot(7, 3, 0), settled.HiddenExperience[name]);
        Assert.Equal(new[] { 9, 10 }, settled.Magics.Take(2).ToArray());
        random.AssertExhausted();
    }

    [Fact]
    public void Dead_member_skips_growth_but_gets_half_recovery_and_survivor_gains_full_experience()
    {
        // Two KO'd enemies grant 2x50 exp and 2x30 cash; only the surviving member grows (the full
        // total, not a share), while the dead one is still covered by the party-wide half recovery.
        var random = new ScriptedRandom([
            F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), F(.9f, 1.1f, 1),
            I(0, 1, 0), I(0, 16, 0), I(0, 2, 0), I(0, 1, 0), I(1, 10, 1),
            I(0, 1, 0), I(0, 1, 1), I(0, 16, 0), I(0, 2, 0), I(0, 1, 0), I(1, 10, 1),
            I(1, 2, 1), I(0, 5, 1), F(1, 1.125f, 1), I(2, 3, 2),
            F(.9f, 1.1f, 1), F(.9f, 1.1f, 1),
            I(0, 1, 0), I(0, 1, 1), I(0, 16, 0), I(0, 2, 0), I(0, 1, 0), I(1, 10, 1),
            I(1, 2, 1), I(0, 5, 1), F(1, 1.125f, 1), I(2, 3, 2),
            I(0, 7, 7), I(0, 5, 5), I(0, 1, 1), I(0, 1, 0), I(0, 1, 1), I(0, 1, 0), I(1, 2, 2)]);
        var dead = Role(4) with { Hp = 20, Mp = 10 };
        var survivor = Role(2) with { Hp = 150 };
        var session = new ClassicBattleLifecycleSession([dead, survivor],
            [new(4, 20, 150, 40, 20, 100), new(2, 150, 150, 40, 20, 90)],
            [new(7, 0, 43, 0, 0, 100, 1) { Experience = 50, Cash = 30 }, new(7, 0, 43, 0, 0, 90, 1) { Experience = 50, Cash = 30 }],
            new Dictionary<int, ClassicBattleGrowthData> { [4] = Growth() with { LevelUpMagics = [new(2, 11)] }, [2] = Growth() with { LevelUpMagics = [new(3, 11)] } },
            random);
        session.SelectAttack(0, 0);
        session.SelectAttack(1, 0);
        session.ResolveNextAction();
        session.ResolveNextAction();
        session.ResolveNextAction();
        session.ResolveNextAction();
        session.SelectAttack(1, 1);
        session.ResolveNextAction();
        session.ResolveNextAction();
        session.ResolveNextAction();
        Assert.Equal(ClassicBattlePhase.Won, session.Completion!.Outcome);
        var roles = session.Current.Roles;
        Assert.Equal(75, roles[4].Hp); // 0 + (150-0)/2: recovery includes the dead member
        Assert.Equal(30, roles[4].Mp); // 10 + (50-10)/2
        Assert.Equal(2, roles[4].Level);
        Assert.Equal(new BattleExperienceSnapshot(5, 2, 17), roles[4].PrimaryExperience);
        Assert.Equal(new[] { 9 }, roles[4].Magics.Take(1).ToArray()); // dead members learn nothing
        Assert.Equal(3, roles[2].Level);
        Assert.Equal(169, roles[2].MaxHp); // 150 +10+7, hidden health (100*4/6)*2+7 crosses once
        Assert.Equal(169, roles[2].Hp);
        Assert.Equal(63, roles[2].MaxMp);
        Assert.Equal(63, roles[2].Mp);
        Assert.Equal(new BattleExperienceSnapshot(5, 3, 17), roles[2].PrimaryExperience);
        Assert.Equal(new BattleExperienceSnapshot(39, 4, 4), roles[2].HiddenExperience["health"]);
        Assert.Equal(new BattleExperienceSnapshot(73, 3, 2), roles[2].HiddenExperience["attack"]);
        Assert.Equal(new[] { 9, 11 }, roles[2].Magics.Take(2).ToArray());
        random.AssertExhausted();
    }

    [Fact]
    public void Lost_after_a_partial_kill_accumulates_no_grant_and_keeps_every_growth_record()
    {
        // The first enemy is KO'd (its rewards exist in the battle) before the party loses; a
        // non-Won settlement grants nothing and changes no experience record.
        var random = new ScriptedRandom([
            F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), F(.9f, 1.1f, 1),
            I(0, 0, 0), I(0, 16, 0), I(0, 2, 0), I(0, 1, 0), I(1, 10, 1),
            I(1, 2, 1), I(0, 5, 1), F(1, 1.125f, 1), I(2, 3, 2),
            I(0, 0, 0), I(0, 16, 0), I(0, 2, 0), I(0, 1, 0), I(1, 10, 1),
            F(.9f, 1.1f, 1), F(.9f, 1.1f, 1),
            I(1, 2, 1), I(0, 5, 1), F(1, 1.125f, 1), I(2, 3, 2),
            I(0, 0, 0), I(0, 16, 0), I(0, 2, 0), I(0, 1, 0), I(1, 10, 1)]);
        var session = Start(Role() with { Hp = 44 }, random,
            enemy: null, enemies:
            [new(7, 0, 43, 0, 0, 200, 1) { Experience = 50, Cash = 30 }, new(7, 0, 100, 0, 0, 0, 1) { Experience = 70, Cash = 40 }]);
        session.SelectAttack(0, 0);
        session.ResolveNextAction();
        session.ResolveNextAction();
        session.ResolveNextAction();
        session.SelectAttack(0, 1);
        session.ResolveNextAction();
        session.ResolveNextAction();
        Assert.Equal(ClassicBattlePhase.Lost, session.Completion!.Outcome);
        BattleRoleSnapshot settled = session.Current.Role;
        Assert.Equal(0, settled.Hp);
        Assert.Equal(20, settled.Mp);
        Assert.Equal(2, settled.Level);
        Assert.Equal(150, settled.MaxHp);
        Assert.Equal(new BattleExperienceSnapshot(5, 2, 17), settled.PrimaryExperience);
        Assert.Equal(new BattleExperienceSnapshot(7, 3, 2), settled.HiddenExperience["attack"]);
        Assert.Equal(new BattleExperienceSnapshot(7, 3, 4), settled.HiddenExperience["health"]);
        Assert.All(settled.Statuses, value => Assert.Equal(0, value));
        random.AssertExhausted();
    }

    [Theory]
    [InlineData("exact")]
    [InlineData("multi")]
    [InlineData("max")]
    [InlineData("hidden-max")]
    public void Threshold_crossings_follow_the_reference_remainder_and_level_rules(string kind)
    {
        BattleRoleSnapshot role;
        ClassicBattleEnemy enemy;
        Draw[] growthDraws;
        BattleRoleSnapshot expected;
        switch (kind)
        {
            case "exact": // 95+5 hits 100 exactly: one level, zero remainder, 15+exp stays below.
                role = Role() with { PrimaryExperience = new(95, 2, 17) };
                enemy = new(7, 0, 43, 0, 0, 0, 1) { Experience = 5 };
                growthDraws = [I(0, 7, 7), I(0, 5, 5), I(0, 1, 1), I(0, 1, 0), I(0, 1, 1), I(0, 1, 0)];
                expected = Role() with { Level = 3, Hp = 167, MaxHp = 167, Mp = 63, MaxMp = 63,
                    PrimaryExperience = new(0, 3, 17) };
                break;
            case "multi": // 5+295 crosses three consecutive thresholds; hidden health/attack cross too.
                role = Role();
                enemy = new(7, 0, 43, 0, 0, 0, 1) { Experience = 295 };
                growthDraws = [I(0, 7, 7), I(0, 5, 5), I(0, 1, 1), I(0, 1, 1), I(0, 1, 1), I(0, 1, 1),
                    I(0, 7, 7), I(0, 5, 5), I(0, 1, 1), I(0, 1, 1), I(0, 1, 1), I(0, 1, 1),
                    I(0, 7, 7), I(0, 5, 5), I(0, 1, 1), I(0, 1, 1), I(0, 1, 1), I(0, 1, 1),
                    I(1, 2, 2), I(1, 2, 2), I(1, 2, 2), I(1, 2, 2), I(1, 2, 2)];
                expected = Role() with { Level = 5, Hp = 207, MaxHp = 207, Mp = 89, MaxMp = 89,
                    PrimaryExperience = new(0, 5, 17) };
                break;
            case "max": // level 99 wastes the crossing without a level-up, stat draw or restore.
                role = Role() with { Level = 99, PrimaryExperience = new(50, 99, 17) };
                enemy = new(7, 0, 43, 0, 0, 0, 1) { Experience = 50 };
                growthDraws = [];
                expected = Role() with { Level = 99, Hp = 125, Mp = 35, PrimaryExperience = new(0, 99, 17) };
                break;
            default: // hidden track already at level 99 still grows the stat and keeps its level.
                role = Role();
                enemy = new(7, 0, 43, 0, 0, 0, 1) { Experience = 10 };
                growthDraws = [I(1, 2, 2)];
                expected = Role() with { Hp = 126, MaxHp = 152, Mp = 35, PrimaryExperience = new(15, 2, 17) };
                break;
        }
        if (kind == "hidden-max")
        {
            var hidden = role.HiddenExperience.ToDictionary(x => x.Key, x => x.Value);
            hidden["health"] = new(95, 99, 2);
            role = role with { HiddenExperience = hidden };
        }
        var random = new ScriptedRandom([.. WinningDraws(), .. growthDraws]);
        var session = Start(role, random, enemy: enemy);
        session.SelectAttack(7);
        Assert.True(session.ResolveNextAction());
        Assert.Equal(ClassicBattlePhase.Won, session.Completion!.Outcome);
        BattleRoleSnapshot settled = session.Current.Role;
        Assert.Equal(expected.Level, settled.Level);
        Assert.Equal(expected.MaxHp, settled.MaxHp);
        Assert.Equal(expected.Hp, settled.Hp);
        Assert.Equal(expected.MaxMp, settled.MaxMp);
        Assert.Equal(expected.Mp, settled.Mp);
        Assert.Equal(expected.PrimaryExperience, settled.PrimaryExperience);
        random.AssertExhausted();
    }

    [Fact]
    public void Magic_scan_dedupes_rows_skips_zero_and_future_levels_and_fills_the_first_free_slot()
    {
        var role = Role() with { Magics = new[] { 9, 0, 11 }.Concat(new int[29]).ToArray() };
        var random = WinningRandom();
        var session = Start(role, random, Growth() with { LevelUpMagics = [new(2, 10), new(2, 10), new(0, 7), new(4, 12)] });
        session.SelectAttack(7);
        Assert.True(session.ResolveNextAction());
        Assert.Equal(ClassicBattlePhase.Won, session.Completion!.Outcome);
        // The duplicate row learns once at the first free slot; magicId 0 and future-level rows never learn.
        Assert.Equal(new[] { 9, 10, 11 }, session.Current.Role.Magics.Take(3).ToArray());
        random.AssertExhausted();
    }

    private static BattleRoleSnapshot Role(int roleId = 1) => new(roleId, 2, 100, 150, 20, 50, new(5, 2, 17),
        new Dictionary<string, BattleExperienceSnapshot> {
            ["health"] = new(7, 3, 55), ["magic"] = new(7, 3, 55), ["attack"] = new(7, 3, 55),
            ["magicPower"] = new(7, 3, 55), ["defense"] = new(7, 3, 55), ["dexterity"] = new(7, 3, 55), ["flee"] = new(7, 3, 55)
        }, new int[6], new[] { 9 }.Concat(new int[31]).ToArray(), new int[9], 0);
    private static ClassicBattleGrowthData Growth() => new(Enumerable.Repeat(100, 100).ToArray(), new BattleLevelUpMagic[] { new(2, 9), new(3, 10) });
    private static ClassicBattleLifecycleSession Start(BattleRoleSnapshot role, IBattleRandom random,
        ClassicBattleGrowthData? growth = null, int dexterity = 100, ClassicBattleEnemy? enemy = null,
        IReadOnlyList<ClassicBattleEnemy>? enemies = null) => enemies is null
        ? new ClassicBattleLifecycleSession(role, new(1, role.Hp, role.MaxHp, 40, 20, dexterity),
            enemy ?? new(7, 0, 43, 0, 0, 0, 1), growth ?? Growth(), random)
        : new ClassicBattleLifecycleSession([role], [new(1, role.Hp, role.MaxHp, 40, 20, dexterity)],
            enemies, new Dictionary<int, ClassicBattleGrowthData> { [role.RoleId] = growth ?? Growth() }, random);
    private static ScriptedRandom WinningRandom() => new(WinningDraws());
    private static Draw[] WinningDraws() => [F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), I(1, 2, 1), I(0, 5, 1), F(1, 1.125f, 1), I(2, 3, 2)];
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
