using System.Text.Json.Nodes;
using Xunit;

namespace Pal.Core.Tests;

/// <summary>#49 mid-battle dying/friend-death role hooks: fight.c 768-880 detection, the executor's
/// hook run with every random branch, and the per-WORD side effects on the live battle.</summary>
public sealed class MidbattleRoleHookTests
{
    // One enemy (level0, dex500 -> speed 518) versus two players (dex 100): the enemy acts first.
    private static readonly Draw[] Queue3 = [F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), F(.9f, 1.1f, 1)];
    private static readonly Draw[] Queue4 = [F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), F(.9f, 1.1f, 1)];
    // AttackPlayer targeting party1: target draw, evade roll 0 (hit), strength +0, damage +0, item rate.
    private static readonly Draw[] EnemyHit1 = [I(0, 1, 1), I(0, 16, 0), I(0, 2, 0), I(0, 1, 0), I(1, 10, 1)];
    private static readonly Draw[] EnemyHit0 = [I(0, 1, 0), I(0, 16, 0), I(0, 2, 0), I(0, 1, 0), I(1, 10, 1)];
    // Party0 is role 0: its strike also draws the reference 0..11 double-damage roll (fight.c:3644).
    private static readonly Draw[] Strike = [I(1, 2, 1), I(0, 5, 1), I(0, 11, 11), F(1f, 1.125f, 1), I(2, 3, 2)];

    [Fact]
    public void Enemy_strike_crossing_the_dying_threshold_pauses_the_battle_on_the_member_hook()
    {
        // role1 60/240: prev 60 >= 240/5 and 16 < min(100,48) after damage 44; coverer role0 alive.
        var random = new ScriptedRandom([.. Queue3, .. EnemyHit1, .. Strike]);
        var session = new ClassicBattleSession(DyingDefinition(), random);
        session.SelectAttack(0, 0);
        session.SelectAttack(1, 0);
        Assert.True(session.ResolveNextAction());
        Assert.Equal(ClassicBattlePhase.RoleHook, session.Current.Phase);
        Assert.Equal(new ClassicBattleRoleHook(ClassicBattleHookKind.Dying, 1, 1, 5), session.Current.PendingRoleHook);
        Assert.Equal(16, session.Current.Players[1].Hp);
        Assert.Equal(ClassicBattleSide.Enemy, session.Current.LastAction!.Attacker); // the strike stays published
        Assert.Throws<InvalidOperationException>(() => session.ResolveNextAction());
        Assert.Throws<InvalidOperationException>(() => session.SelectAttack(0, 0));
        session.AcknowledgeRoleHook();
        Assert.Null(session.Current.PendingRoleHook);
        Assert.Equal(ClassicBattlePhase.Resolving, session.Current.Phase);
        Assert.True(session.ResolveNextAction()); // party0's saved queue position, not a new round
        Assert.Equal(ClassicBattlePhase.Won, session.Current.Phase);
        random.AssertExhausted();
    }

    [Theory]
    // fight.c 833-837: prevHP must be at or above the raw maxHP/5, the new HP below min(100, maxHP/5).
    [InlineData(240, 48, 44, 4, true)]    // prev exactly maxHP/5 crosses into dying
    [InlineData(240, 47, 37, 10, false)]  // already dying before the strike: no second hook
    [InlineData(1000, 200, 120, 80, true)] // 1000/5 = 200 band with a min(100,200)=100 threshold
    [InlineData(1000, 150, 70, 80, false)] // not dying before, dying now, but prev below the raw band
    public void Dying_hook_requires_the_reference_prevHP_band_not_just_a_dying_state(int maxHp, int before, int damage, int after, bool hooks)
    {
        var definition = DyingDefinition() with
        {
            Players =
            [
                DyingDefinition().Players[0] with { MaxHp = maxHp, Hp = 200 },
                DyingDefinition().Players[1] with { MaxHp = maxHp, Hp = before }
            ],
            Enemies = [DyingDefinition().Enemies[0] with { Attack = damage - 36 }]
        };
        var random = new ScriptedRandom([.. Queue3, .. EnemyHit1]);
        var session = new ClassicBattleSession(definition, random);
        session.SelectAttack(0, 0);
        session.SelectAttack(1, 0);
        Assert.True(session.ResolveNextAction());
        Assert.Equal(after, session.Current.Players[1].Hp);
        if (hooks)
        {
            Assert.Equal(ClassicBattlePhase.RoleHook, session.Current.Phase);
            Assert.Equal(new ClassicBattleRoleHook(ClassicBattleHookKind.Dying, 1, 1, 5), session.Current.PendingRoleHook);
            session.AcknowledgeRoleHook();
        }
        else Assert.Null(session.Current.PendingRoleHook);
        Assert.Equal(ClassicBattlePhase.Resolving, session.Current.Phase);
        random.AssertExhausted();
    }

    [Fact]
    public void Member_death_runs_the_coverer_friend_death_hook_and_is_not_lost_while_it_is_pending()
    {
        // role1 40/240 dies; its coverer role0 carries FriendDeathScript 29; the battle stays open
        // while the hook is pending even though the strike emptied role1's HP.
        var random = new ScriptedRandom([.. Queue3, .. EnemyHit1]);
        var session = new ClassicBattleSession(DeathDefinition(), random);
        session.SelectAttack(0, 0);
        session.SelectAttack(1, 0);
        Assert.True(session.ResolveNextAction());
        Assert.Equal(ClassicBattlePhase.RoleHook, session.Current.Phase);
        Assert.Equal(new ClassicBattleRoleHook(ClassicBattleHookKind.FriendDeath, 0, 0, 31), session.Current.PendingRoleHook);
        Assert.Equal(0, session.Current.Players[1].Hp);
        Assert.Null(session.Completion);
        session.AcknowledgeRoleHook();
        Assert.Equal(ClassicBattlePhase.Resolving, session.Current.Phase);
        random.AssertExhausted();
    }

    [Fact]
    public void Absent_or_dead_coverer_produces_no_hook_and_the_battle_settles_normally()
    {
        // Both enemies hit for 100: E0 kills role0 (its coverer role2 is absent), E1 kills role1
        // (its coverer role0 is now dead) - neither death finds a runnable script, so Lost settles.
        // Attack 80 pierces defense 20 for exactly 100 damage; role1's zero defense takes 116.
        var definition = DeathDefinition() with { Enemies = [DeathDefinition().Enemies[0] with { Attack = 80 }, DeathDefinition().Enemies[0] with { Id = 8, Attack = 80 }] };
        var random = new ScriptedRandom([.. Queue4, .. EnemyHit0, .. EnemyHit1]);
        var session = new ClassicBattleSession(definition, random);
        session.SelectAttack(0, 0);
        session.SelectAttack(1, 0);
        session.ResolveNextAction(); // E0 kills role0: coverer role2 not in this party, no hook
        Assert.Equal(ClassicBattlePhase.Resolving, session.Current.Phase);
        Assert.Equal(0, session.Current.Players[0].Hp);
        Assert.Null(session.Current.PendingRoleHook);
        session.ResolveNextAction(); // E1 kills role1: coverer role0 dead, no hook, all players down
        Assert.Equal(ClassicBattlePhase.Lost, session.Current.Phase);
        Assert.Equal(ClassicBattlePhase.Lost, session.Completion!.Outcome);
        random.AssertExhausted();
    }

    [Fact]
    public void Selected_entry_preflights_every_hook_branch_and_accepts_nonzero_scripts()
    {
        var events = Fixture().Load();
        var requirements = TriggerScriptSession.ValidateEntry(events, Language(), Content(60), "hooks", "npc");
        Assert.Equal(new[] { 18 }, requirements.BattleTeamIds);
        Assert.Equal(new[] { 11, 15 }, requirements.PortraitIds.OrderBy(id => id));
        // All four dying bands and all four friend-death bands are required displays.
        Assert.Equal(new[] { 11, 12, 13, 15, 17, 19, 21, 23, 24, 25, 26, 27, 28, 29 },
            requirements.DisplayBlocks.Select(block => block.Content.StartMessageId).OrderBy(id => id));
    }

    [Fact]
    public void StartBattleHook_runs_the_dying_paths_full_random_closure_with_one_production_draw_per_jump()
    {
        // draw 74 < 75 falls, 66 >= 66 jumps: path3 = lower block 15+1 then upper portrait11 block 17+1.
        var random = new ScriptedRandom([.. Queue3, .. EnemyHit1, I(1, 100, 74), I(1, 100, 66), .. Strike]);
        var script = new TriggerScriptSession(Fixture().Load(), Language(), Content(60), random);
        script.Start("hooks", "npc", 28);
        var battle = script.PendingBattle!;
        battle.Lifecycle.SelectAttack(0, 0);
        battle.Lifecycle.SelectAttack(1, 0);
        battle.Lifecycle.ResolveNextAction();
        Assert.Equal(new ClassicBattleRoleHook(ClassicBattleHookKind.Dying, 1, 1, 5), script.PendingBattleHook);
        Assert.Throws<InvalidOperationException>(() => battle.Lifecycle.SelectAttack(0, 0));
        script.StartBattleHook();
        Assert.Equal(new ClassicBattleRoleHook(ClassicBattleHookKind.Dying, 1, 1, 5), script.RunningBattleHook);
        Assert.Equal(1, script.Caller); // the dying member's role is the hook caller
        var display = script.PendingDisplay!;
        Assert.Equal(("lower", 0, 2), (display.Context.Box, display.Context.PortraitId, display.Context.Color));
        Assert.Equal(new TriggerPosition("hooks", "L0014", 20), script.CurrentPosition);
        // The consecutive 15/16 commands merge into one range with one reference, as in the retail pair.
        Assert.Equal(new[] { new TriggerMessageReference(new("hooks", "L0012", 18), 15, 1) }, display.MessageReferences);
        Assert.Equal((15, 16), (display.Content.StartMessageId, display.Content.EndMessageId));
        AssertInert(script, random, () => script.AcknowledgeBattle(battle.Id, battle.Lifecycle.Completion));
        FinishDisplay(script);
        var second = script.PendingDisplay!;
        Assert.Equal(("upper", 11, 2), (second.Context.Box, second.Context.PortraitId, second.Context.Color));
        Assert.Equal((17, 18), (second.Content.StartMessageId, second.Content.EndMessageId));
        FinishDisplay(script);
        Assert.Null(script.RunningBattleHook);
        Assert.Null(script.PendingBattleHook);
        Assert.Equal(TriggerScriptPhase.WaitingForBattle, script.Phase);
        Assert.Equal(ClassicBattlePhase.Resolving, battle.Lifecycle.Current.Combat.Phase);
        battle.Lifecycle.ResolveNextAction();
        Assert.Equal(ClassicBattlePhase.Won, battle.Lifecycle.Completion!.Outcome);
        Assert.True(script.AcknowledgeBattle(battle.Id, battle.Lifecycle.Completion));
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(new TriggerPosition("hooks", "L0004", 4), script.CurrentPosition);
        Assert.Equal(28, script.Caller);
        Assert.Equal(1, script.ReturnEntry);
        random.AssertExhausted();
    }

    [Theory]
    [InlineData("100", "12,13")]    // >= 75 jumps at the first command: path2
    [InlineData("75", "12,13")]     // the rate boundary itself jumps
    [InlineData("74,66", "15,17")]  // falls 74, jumps 66: path3
    [InlineData("74,65,50", "19,21")] // falls twice, jumps 50: path4
    [InlineData("74,65,49", "11")]  // all three fall: path1
    public void Dying_random_bands_select_each_authored_path(string jumpDraws, string expectedStarts)
    {
        List<Draw> draws = [.. Queue3, .. EnemyHit1];
        draws.AddRange(jumpDraws.Split(',').Select(value => I(1, 100, int.Parse(value))));
        var random = new ScriptedRandom(draws);
        var script = new TriggerScriptSession(Fixture().Load(), Language(), Content(60), random);
        script.Start("hooks", "npc", 28);
        var battle = script.PendingBattle!;
        battle.Lifecycle.SelectAttack(0, 0);
        battle.Lifecycle.SelectAttack(1, 0);
        battle.Lifecycle.ResolveNextAction();
        script.StartBattleHook();
        foreach (int start in expectedStarts.Split(',').Select(int.Parse))
        {
            var display = script.PendingDisplay!;
            Assert.Equal(start, display.Content.StartMessageId);
            FinishDisplay(script);
        }
        Assert.Null(script.PendingDisplay);
        Assert.Equal(TriggerScriptPhase.WaitingForBattle, script.Phase);
        random.AssertExhausted();
    }

    [Fact]
    public void Friend_death_path1_heals_the_caller_to_maximum_and_boosts_subsequent_attack_damage()
    {
        // All three bands fall (path1): after its two displays, +9999 HP clamps at max and attack
        // gains 5% of base 40 = 2, so party0's next strike deals 47 (attack42 vs defense24, +1)
        // and clears a 44 HP enemy that would survive the unboosted 43-damage strike.
        var random = new ScriptedRandom([.. Queue3, .. EnemyHit1, I(1, 100, 74), I(1, 100, 65), I(1, 100, 49), .. Strike]);
        var script = new TriggerScriptSession(Fixture().Load(), Language(), Content(40, enemyHp: 44), random);
        script.Start("hooks", "npc", 28);
        var battle = script.PendingBattle!;
        battle.Lifecycle.SelectAttack(0, 0);
        battle.Lifecycle.SelectAttack(1, 0);
        battle.Lifecycle.ResolveNextAction();
        Assert.Equal(new ClassicBattleRoleHook(ClassicBattleHookKind.FriendDeath, 0, 0, 31), script.PendingBattleHook);
        script.StartBattleHook();
        Assert.Equal(0, script.Caller); // the coverer's role is the friend-death caller
        while (script.PendingDisplay is not null) FinishDisplay(script);
        Assert.Equal(TriggerScriptPhase.WaitingForBattle, script.Phase);
        Assert.Equal(28, script.Caller); // the parked outer call keeps its own caller
        var player = battle.Lifecycle.Current.Combat.Players[0];
        Assert.Equal(200, player.Hp);
        Assert.Equal(200, battle.Lifecycle.Current.Roles[0].Hp);
        Assert.Equal(2, player.BoostAttack);
        Assert.Equal(42, player.Attack);
        battle.Lifecycle.ResolveNextAction();
        Assert.Equal(ClassicBattlePhase.Won, battle.Lifecycle.Completion!.Outcome);
        Assert.Equal(47, battle.Lifecycle.Current.Combat.LastAction!.Damage);
        random.AssertExhausted();
    }

    [Theory]
    [InlineData("74,65,49", 200, 20, 2, 0, 0, 0)] // fall-through path1: +9999 HP clamps at 200, attack +5%
    [InlineData("100", 100, 50, 0, 6, 0, 0)]     // path2: +9999 MP clamps at 50, magicPower +10% of 64
    [InlineData("74,66", 100, 20, 10, 16, 0, 0)] // path3: attack +25%, magicPower +25%
    [InlineData("74,65,50", 100, 20, 0, 0, 90, 16)] // path4: dexterity +90%, flee +90%
    public void Friend_death_bands_apply_their_per_word_effects_on_the_caller(string jumpDraws, int hp, int mp,
        int boostAttack, int boostMagicPower, int boostDexterity, int boostFlee)
    {
        List<Draw> draws = [.. Queue3, .. EnemyHit1];
        draws.AddRange(jumpDraws.Split(',').Select(value => I(1, 100, int.Parse(value))));
        draws.AddRange(Strike);
        var random = new ScriptedRandom(draws);
        var script = new TriggerScriptSession(Fixture().Load(), Language(), Content(40), random);
        script.Start("hooks", "npc", 28);
        var battle = script.PendingBattle!;
        battle.Lifecycle.SelectAttack(0, 0);
        battle.Lifecycle.SelectAttack(1, 0);
        battle.Lifecycle.ResolveNextAction();
        script.StartBattleHook();
        while (script.PendingDisplay is not null) FinishDisplay(script);
        Assert.Equal(28, script.Caller); // the parked outer call keeps its own caller
        Assert.Equal(TriggerScriptPhase.WaitingForBattle, script.Phase);
        var player = battle.Lifecycle.Current.Combat.Players[0];
        Assert.Equal((hp, mp, boostAttack, boostMagicPower, boostDexterity, boostFlee),
            (player.Hp, battle.Lifecycle.Current.Roles[0].Mp, player.BoostAttack, player.BoostMagicPower,
                player.BoostDexterity, player.BoostFlee));
        // The boosts are battle-owned extra-register words: the settled role state never carries them.
        battle.Lifecycle.ResolveNextAction();
        Assert.Equal(ClassicBattlePhase.Won, battle.Lifecycle.Completion!.Outcome);
        Assert.True(script.AcknowledgeBattle(battle.Id, battle.Lifecycle.Completion));
        Assert.All(script.BattleRoles![0].Equipment, value => Assert.Equal(0, value));
        Assert.All(script.BattleRoles[0].Statuses, value => Assert.Equal(0, value));
        random.AssertExhausted();
    }

    [Fact]
    public void Boosted_dexterity_is_the_panel_the_next_round_queue_consumes()
    {
        // Path4 raises party0's dexterity 100 -> 190; the published effective panel feeds BuildQueue.
        List<Draw> draws = [.. Queue3, .. EnemyHit1, I(1, 100, 74), I(1, 100, 65), I(1, 100, 50), .. Strike];
        var random = new ScriptedRandom(draws);
        var script = new TriggerScriptSession(Fixture().Load(), Language(), Content(40, enemyHp: 300), random);
        script.Start("hooks", "npc", 28);
        var battle = script.PendingBattle!;
        battle.Lifecycle.SelectAttack(0, 0);
        battle.Lifecycle.SelectAttack(1, 0);
        battle.Lifecycle.ResolveNextAction();
        script.StartBattleHook();
        while (script.PendingDisplay is not null) FinishDisplay(script);
        var player = battle.Lifecycle.Current.Combat.Players[0];
        Assert.Equal((90, 190), (player.BoostDexterity, player.Dexterity));
        battle.Lifecycle.ResolveNextAction(); // party0's saved strike (81 damage) leaves the enemy alive
        battle.Lifecycle.ResolveNextAction(); // party1's dead pass ends the round
        Assert.Equal(ClassicBattlePhase.Selecting, battle.Lifecycle.Current.Combat.Phase);
        random.AssertExhausted();
    }

    [Fact]
    public void Hook_preflight_rejects_a_missing_random_branch_language_before_any_side_effect_or_draw()
    {
        var fixture = Fixture();
        var language = new DialogueLanguagePackage("en",
            Blocks().Where(block => block.StartMessageId != 13));
        var random = new ScriptedRandom([.. Queue3, .. EnemyHit1]);
        var script = new TriggerScriptSession(fixture.Load(), language, Content(60), random);
        Assert.Throws<InvalidDataException>(() =>
            TriggerScriptSession.ValidateEntry(fixture.Load(), language, Content(60), "hooks", "npc"));
        // The selected entry's own preflight carries the hook closure: Start rejects before any battle
        // or draw; when a hook is already pending the same closure gates StartBattleHook.
        var error = Assert.Throws<InvalidDataException>(() => script.Start("hooks", "npc", 28));
        Assert.Contains("hooks.json#L000E @000E", error.Message);
        Assert.Contains("Language 'en' is missing message range 13+1", error.Message);
        Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
        Assert.Null(script.RunningBattleHook);
        Assert.Null(script.PendingBattle);
    }

    [Fact]
    public void Role_hook_verbs_outside_a_battle_hook_and_unknown_hook_addresses_are_rejected()
    {
        var fixture = Fixture();
        fixture.Commands[2] = ChangeHp(2, 9999, next: "L0003");
        var error = Assert.Throws<InvalidDataException>(() =>
            TriggerScriptSession.ValidateEntry(fixture.Load(), Language(), Content(60), "hooks", "npc"));
        Assert.Contains("hooks.json#L0002 @0002: changeHp is only supported inside a battle role hook", error.Message);
        var random = new ScriptedRandom([]);
        var script = new TriggerScriptSession(fixture.Load(), Language(), Content(60), random);
        Assert.Throws<InvalidDataException>(() => script.Start("hooks", "npc", 28));
        Assert.Equal(TriggerScriptPhase.Ready, script.Phase);

        var baseContent = Content(60);
        var outOfRange = new TriggerBattleContent(baseContent.InitialRoles.Values.ToArray(),
            baseContent.Players.Select(p => p with { DyingScript = 60000 }).ToArray(),
            baseContent.GrowthByRole, baseContent.EnemyTeams);
        var fault = Assert.Throws<InvalidDataException>(() =>
            TriggerScriptSession.ValidateEntry(Fixture().Load(), Language(), outOfRange, "hooks", "npc"));
        Assert.Contains("role hook address EA60 is outside the event collection", fault.Message);
    }

    [Fact]
    public void Hook_scripts_cannot_start_battles_or_use_party_wide_or_foreign_role_effects()
    {
        var fixture = Fixture();
        fixture.Commands[39] = StartBattle(39);
        var nested = Assert.Throws<InvalidDataException>(() =>
            TriggerScriptSession.ValidateEntry(fixture.Load(), Language(), Content(60), "hooks", "npc"));
        Assert.Contains("unsupported trigger command/context 'startBattle'", nested.Message);

        var fixture2 = Fixture();
        fixture2.Commands[38]!["scope"] = 1;
        var partyWide = Assert.Throws<InvalidDataException>(() =>
            TriggerScriptSession.ValidateEntry(fixture2.Load(), Language(), Content(60), "hooks", "npc"));
        Assert.Contains("party-wide changeHp is unsupported", partyWide.Message);

        var fixture3 = Fixture();
        fixture3.Commands[39]!["roleOperand"] = 1;
        var foreignRole = Assert.Throws<InvalidDataException>(() =>
            TriggerScriptSession.ValidateEntry(fixture3.Load(), Language(), Content(60), "hooks", "npc"));
        Assert.Contains("boostStat must target the role hook's caller", foreignRole.Message);
    }

    [Fact]
    public void Named_hook_verbs_parse_strictly_and_reject_wrong_opcodes_raw_and_ranges()
    {
        var collection = Fixture().Load();
        Assert.Equal(60, collection.InstructionCount);
        Assert.Equal(5, collection.GetEntry("hooks", "role1-dying").SourceAddress);
        Assert.Equal(31, collection.GetEntry("hooks", "role0-friend-death").SourceAddress);
        Assert.Equal(new[] { 0, 9999, 0 }, collection.GetSource(38).Operands);
        Assert.Equal(new[] { 75, 11, 0 }, collection.GetSource(5).Operands);

        var fixture = Fixture();
        fixture.Commands[38]!["source"]!["opcode"] = 28; // changeMp opcode under a changeHp verb
        Assert.Throws<InvalidDataException>(() => fixture.Load());
        var fixture2 = Fixture();
        fixture2.Commands[38] = new JsonObject { ["label"] = "L0026", ["op"] = "raw", ["next"] = "L0027",
            ["source"] = new JsonObject { ["index"] = 38, ["opcode"] = 27, ["operands"] = new JsonArray(0, 9999, 0) } };
        Assert.Contains("Registered opcode must use named mapping",
            Assert.Throws<InvalidDataException>(() => fixture2.Load()).Message);
        var fixture3 = Fixture();
        fixture3.Commands[38]!["amount"] = 32768;
        Assert.Throws<InvalidDataException>(() => fixture3.Load());
        var fixture4 = Fixture();
        fixture4.Commands[5]!["to"] = "L9999";
        Assert.Throws<InvalidDataException>(() => fixture4.Load());
    }

    [Fact]
    public void Modern_battle_data_players_take_optional_magicPower_and_fleeRate_words()
    {
        var data = JsonNode.Parse(BattleDataJson(60))!.AsObject();
        data["players"]![0]!["magicPower"] = 64;
        data["players"]![0]!["fleeRate"] = 18;
        var content = ModernBattleContent.Parse(data.ToJsonString());
        Assert.Equal((64, 18), (content.Players[0].MagicPower, content.Players[0].FleeRate));
        Assert.Equal((0, 0), (content.Players[1].MagicPower, content.Players[1].FleeRate));
        data["players"]![0]!["magicPower"] = 65536;
        Assert.Throws<InvalidDataException>(() => ModernBattleContent.Parse(data.ToJsonString()));
        data["players"]![0]!["magicPower"] = 64;
        data["players"]![0]!["flee"] = 18;
        Assert.Contains("unknown field 'flee'",
            Assert.Throws<InvalidDataException>(() => ModernBattleContent.Parse(data.ToJsonString())).Message);
    }

    private static void FinishDisplay(TriggerScriptSession script)
    {
        var display = script.PendingDisplay!;
        // Merged hook blocks carry several display entries: finish the whole block, then acknowledge.
        while (display.Display.Current.Phase != DialogueDisplayPhase.Completed)
            display.Display.Update(display.Display.Current.Phase == DialogueDisplayPhase.WaitingForAdvance ? 100u : int.MaxValue, true, true);
        Assert.True(script.AcknowledgeDisplay(display.Id));
    }

    private static void AssertInert(TriggerScriptSession script, ScriptedRandom random, Func<bool> acknowledge)
    {
        var before = (script.Phase, script.PendingDisplay, script.PendingBattle, script.RunningBattleHook);
        Assert.False(acknowledge());
        Assert.Equal(before, (script.Phase, script.PendingDisplay, script.PendingBattle, script.RunningBattleHook));
    }

    internal static TriggerBattleContent Content(int hp1, int enemyHp = 43)
    {
        var players = new[]
        {
            new ClassicBattlePlayer(0, 100, 200, 40, 20, 100)
            { CoveredByRoleId = 2, FriendDeathScript = 31, MagicPower = 64, FleeRate = 18 },
            new ClassicBattlePlayer(1, hp1, 240, 20, 0, 100)
            { CoveredByRoleId = 0, DyingScript = 5, MagicPower = 64, FleeRate = 18 }
        };
        return new([Role(0, 100, 200, 20, 50, 2), Role(1, hp1, 240, 10, 240, 0)], players,
            Growth(), new Dictionary<int, IReadOnlyList<ClassicBattleEnemy>> { [18] = [new(7, 0, enemyHp, 8, 0, 500, 1)] });
    }

    private static ClassicBattleDefinition DyingDefinition() => new(
        [new ClassicBattlePlayer(0, 200, 200, 40, 20, 100) { CoveredByRoleId = 2, FriendDeathScript = 31 },
            new ClassicBattlePlayer(1, 60, 240, 20, 0, 100) { CoveredByRoleId = 0, DyingScript = 5 }],
        [new ClassicBattleEnemy(7, 0, 43, 8, 0, 500, 1)]);

    private static ClassicBattleDefinition DeathDefinition() => new(
        [new ClassicBattlePlayer(0, 100, 200, 40, 20, 100) { CoveredByRoleId = 2, FriendDeathScript = 31 },
            new ClassicBattlePlayer(1, 40, 240, 20, 0, 100) { CoveredByRoleId = 0, DyingScript = 5 }],
        [new ClassicBattleEnemy(7, 0, 43, 8, 0, 500, 1)]);

    private static BattleRoleSnapshot Role(int id, int hp, int maxHp, int mp, int maxMp, int coveredBy) => new(id, 2, hp, maxHp, mp, maxMp,
        new(5, 2, 17),
        new Dictionary<string, BattleExperienceSnapshot>
        {
            ["health"] = new(7, 3, 55), ["magic"] = new(7, 3, 55), ["attack"] = new(7, 3, 55),
            ["magicPower"] = new(7, 3, 55), ["defense"] = new(7, 3, 55), ["dexterity"] = new(7, 3, 55), ["flee"] = new(7, 3, 55)
        }, new int[6], new int[32], new int[9], coveredBy);

    private static Dictionary<int, ClassicBattleGrowthData> Growth() => new()
    {
        [0] = new(Enumerable.Repeat(100, 100).ToArray(), []),
        [1] = new(Enumerable.Repeat(100, 100).ToArray(), [])
    };

    internal static IEnumerable<DialogueMessageBlock> Blocks() =>
    [
        new(10, 10, [DialogueMessagePart.FromText("pre-battle")]),
        new(11, 11, [DialogueMessagePart.FromText("d1")]),
        new(12, 12, [DialogueMessagePart.FromText("d2a")]),
        new(13, 14, [DialogueMessagePart.FromText("d2b"), DialogueMessagePart.FromText("d2c")]),
        new(15, 16, [DialogueMessagePart.FromText("d3a"), DialogueMessagePart.FromText("d3b")]),
        new(17, 18, [DialogueMessagePart.FromText("d3c"), DialogueMessagePart.FromText("d3d")]),
        new(19, 20, [DialogueMessagePart.FromText("d4a"), DialogueMessagePart.FromText("d4b")]),
        new(21, 22, [DialogueMessagePart.FromText("d4c"), DialogueMessagePart.FromText("d4d")]),
        new(23, 23, [DialogueMessagePart.FromText("f1a")]),
        new(24, 24, [DialogueMessagePart.FromText("f1b")]),
        new(25, 25, [DialogueMessagePart.FromText("f2a")]),
        new(26, 26, [DialogueMessagePart.FromText("f2b")]),
        new(27, 27, [DialogueMessagePart.FromText("f3a")]),
        new(28, 28, [DialogueMessagePart.FromText("f3b")]),
        new(29, 29, [DialogueMessagePart.FromText("f4a")])
    ];

    internal static DialogueLanguagePackage Language() => new("zh-Hans", Blocks());

    private static string BattleDataJson(int hp1)
    {
        string thresholds = string.Join(",", Enumerable.Repeat(100, 100));
        string growth = "[{\"roleId\":0,\"levelUpExperience\":[" + thresholds + "],\"levelUpMagics\":[]}," +
            "{\"roleId\":1,\"levelUpExperience\":[" + thresholds + "],\"levelUpMagics\":[]}]";
        return "{\"formatVersion\":1,\"roles\":[" + RoleJson(0, 100, 200, 20, 50, 2) + "," + RoleJson(1, hp1, 240, 10, 240, 0) + "]," +
            "\"players\":[{\"roleId\":0,\"hp\":100,\"maxHp\":200,\"attack\":40,\"defense\":20,\"dexterity\":100," +
            "\"equipment\":[0,0,0,0,0,0],\"statuses\":[0,0,0,0,0,0,0,0,0],\"poisons\":[0]," +
            "\"dyingScript\":0,\"friendDeathScript\":29,\"coveredByRoleId\":2,\"attackAll\":false}," +
            "{\"roleId\":1,\"hp\":" + hp1 + ",\"maxHp\":240,\"attack\":20,\"defense\":0,\"dexterity\":100," +
            "\"equipment\":[0,0,0,0,0,0],\"statuses\":[0,0,0,0,0,0,0,0,0],\"poisons\":[0]," +
            "\"dyingScript\":5,\"friendDeathScript\":0,\"coveredByRoleId\":0,\"attackAll\":false}]," +
            "\"growth\":" + growth + "," +
            "\"teams\":[{\"teamId\":18,\"enemies\":[{\"id\":7,\"level\":0,\"hp\":43,\"attack\":8,\"defense\":0,\"dexterity\":500," +
            "\"physicalResistance\":1,\"statuses\":[0,0,0,0,0,0,0,0,0],\"poisons\":[0],\"turnStartScript\":0," +
            "\"readyScript\":0,\"battleEndScript\":0,\"magicId\":0,\"dualMove\":0,\"attackEquivItem\":0," +
            "\"attackEquivItemRate\":0,\"experience\":0,\"cash\":0}]}]}";
    }

    private static string RoleJson(int id, int hp, int maxHp, int mp, int maxMp, int coveredBy)
    {
        string xp = "{\"value\":7,\"level\":3,\"count\":55}";
        string hidden = "{\"health\":" + xp + ",\"magic\":" + xp + ",\"attack\":" + xp + ",\"magicPower\":" + xp +
            ",\"defense\":" + xp + ",\"dexterity\":" + xp + ",\"flee\":" + xp + "}";
        return "{\"roleId\":" + id + ",\"level\":2,\"hp\":" + hp + ",\"maxHp\":" + maxHp + ",\"mp\":" + mp + ",\"maxMp\":" + maxMp + "," +
            "\"primaryExperience\":{\"value\":5,\"level\":2,\"count\":17}," +
            "\"hiddenExperience\":" + hidden + "," +
            "\"equipment\":[0,0,0,0,0,0],\"magics\":[" + string.Join(",", Enumerable.Repeat(0, 32)) + "]," +
            "\"statuses\":[0,0,0,0,0,0,0,0,0]," +
            "\"coveredByRoleId\":" + coveredBy + "}";
    }

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

    internal static HookFixture Fixture() => new();

    internal static JsonObject ChangeHp(int address, int amount, int scope = 0, string? next = null) => new()
    {
        ["label"] = $"L{address:X4}", ["op"] = "changeHp", ["scope"] = scope, ["amount"] = amount,
        ["next"] = next ?? $"L{address + 1:X4}",
        ["source"] = new JsonObject { ["index"] = address, ["opcode"] = 27, ["operands"] = new JsonArray(scope, amount, 0) }
    };

    private static JsonObject StartBattle(int address) => new()
    {
        ["label"] = $"L{address:X4}", ["op"] = "startBattle", ["enemyTeamId"] = 18, ["lossTarget"] = null,
        ["isBoss"] = true, ["fleeTarget"] = null, ["next"] = $"L{address + 1:X4}",
        ["source"] = new JsonObject { ["index"] = address, ["opcode"] = 7, ["operands"] = new JsonArray(18, 0, 0) }
    };
}

/// <summary>Original authored hook stream: the retail role1-dying / role0-friend-death structure
/// (four random bands each) with original addresses, messages and effects; not retail data.</summary>
internal sealed class HookFixture
{
    public JsonObject Index { get; } = new()
    {
        ["formatVersion"] = 1, ["profile"] = "faithful",
        ["source"] = new JsonObject { ["archive"] = "SSS.MKF", ["chunk"] = 4, ["byteOrder"] = "little",
            ["instructionCount"] = 60, ["byteLength"] = 480,
            ["sha256"] = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" },
        ["files"] = new JsonArray("hooks")
    };
    public Dictionary<string, JsonObject> Files { get; }
    public JsonArray Commands => Files["hooks"]["commands"]!.AsArray();

    public HookFixture()
    {
        var commands = new JsonArray();
        JsonObject Raw(int i) => new() { ["label"] = $"L{i:X4}", ["op"] = "raw", ["next"] = $"L{i + 1:X4}",
            ["source"] = new JsonObject { ["index"] = i, ["opcode"] = 134, ["operands"] = new JsonArray(0, 65535, 8) } };
        JsonObject Upper(int i, int portrait, int color, string next) => new()
        {
            ["label"] = $"L{i:X4}", ["op"] = "setDialog", ["box"] = "upper", ["portraitId"] = portrait,
            ["color"] = color, ["rngWord"] = 0, ["next"] = next,
            ["source"] = new JsonObject { ["index"] = i, ["opcode"] = 60, ["operands"] = new JsonArray(portrait, color, 0) }
        };
        JsonObject Lower(int i, int portrait, int color, string next) => new()
        {
            ["label"] = $"L{i:X4}", ["op"] = "setDialog", ["box"] = "lower", ["portraitId"] = portrait,
            ["color"] = color, ["rngWord"] = 0, ["next"] = next,
            ["source"] = new JsonObject { ["index"] = i, ["opcode"] = 61, ["operands"] = new JsonArray(portrait, color, 0) }
        };
        JsonObject CenterWindow(int i, int color, string next) => new()
        {
            ["label"] = $"L{i:X4}", ["op"] = "setDialog", ["box"] = "centerWindow", ["color"] = color, ["next"] = next,
            ["source"] = new JsonObject { ["index"] = i, ["opcode"] = 62, ["operands"] = new JsonArray(color, 0, 0) }
        };
        JsonObject Show(int i, int message, string next) => new()
        {
            ["label"] = $"L{i:X4}", ["op"] = "showDialog", ["messageId"] = message, ["descriptionLineWord"] = 0,
            ["next"] = next, ["source"] = new JsonObject { ["index"] = i, ["opcode"] = 65535, ["operands"] = new JsonArray(message, 0, 0) }
        };
        JsonObject Jump(int i, int rate, int to) => new()
        {
            ["label"] = $"L{i:X4}", ["op"] = "randomJump", ["rate"] = rate, ["to"] = $"L{to:X4}", ["next"] = $"L{i + 1:X4}",
            ["source"] = new JsonObject { ["index"] = i, ["opcode"] = 6, ["operands"] = new JsonArray(rate, to, 0) }
        };
        JsonObject End(int i) => new() { ["label"] = $"L{i:X4}", ["op"] = "end", ["mode"] = "keep", ["next"] = $"L{i + 1:X4}",
            ["source"] = new JsonObject { ["index"] = i, ["opcode"] = 0, ["operands"] = new JsonArray(0, 0, 0) } };
        JsonObject HealHp(int i, int amount, string next) => new()
        {
            ["label"] = $"L{i:X4}", ["op"] = "changeHp", ["scope"] = 0, ["amount"] = amount, ["next"] = next,
            ["source"] = new JsonObject { ["index"] = i, ["opcode"] = 27, ["operands"] = new JsonArray(0, amount, 0) }
        };
        JsonObject HealMp(int i, int amount, string next) => new()
        {
            ["label"] = $"L{i:X4}", ["op"] = "changeMp", ["scope"] = 0, ["amount"] = amount, ["next"] = next,
            ["source"] = new JsonObject { ["index"] = i, ["opcode"] = 28, ["operands"] = new JsonArray(0, amount, 0) }
        };
        JsonObject Boost(int i, int field, int percent, string next) => new()
        {
            ["label"] = $"L{i:X4}", ["op"] = "boostStat", ["field"] = field, ["percent"] = percent,
            ["roleOperand"] = 0, ["next"] = next,
            ["source"] = new JsonObject { ["index"] = i, ["opcode"] = 48, ["operands"] = new JsonArray(field, percent, 0) }
        };

        // Outer call: pre-battle dialog, battle, keep end.
        commands.Add(Raw(0));
        commands.Add(Upper(1, 0, 0, "L0002"));
        commands.Add(Upper(2, 0, 0, "L0003")); // a second context word keeps the battle as the first stop
        commands.Add(new JsonObject { ["label"] = "L0003", ["op"] = "startBattle", ["enemyTeamId"] = 18, ["lossTarget"] = null,
            ["isBoss"] = true, ["fleeTarget"] = null, ["next"] = "L0004",
            ["source"] = new JsonObject { ["index"] = 3, ["opcode"] = 7, ["operands"] = new JsonArray(18, 0, 0) } });
        commands.Add(End(4));
        // role1 dying script @5: four bands mirroring @A96E-A987 (jump when the draw is at or above the rate).
        commands.Add(Jump(5, 75, 11));
        commands.Add(Jump(6, 66, 17));
        commands.Add(Jump(7, 50, 24));
        commands.Add(Lower(8, 0, 15, "L0009"));
        commands.Add(Show(9, 11, "L000A"));
        commands.Add(End(10));
        commands.Add(Lower(11, 0, 2, "L000C"));
        commands.Add(Show(12, 12, "L000D"));
        commands.Add(Upper(13, 15, 0, "L000E"));
        commands.Add(Show(14, 13, "L000F"));
        commands.Add(Show(15, 14, "L0010"));
        commands.Add(End(16));
        commands.Add(Lower(17, 0, 2, "L0012"));
        commands.Add(Show(18, 15, "L0013"));
        commands.Add(Show(19, 16, "L0014"));
        commands.Add(Upper(20, 11, 0, "L0015"));
        commands.Add(Show(21, 17, "L0016"));
        commands.Add(Show(22, 18, "L0017"));
        commands.Add(End(23));
        commands.Add(Upper(24, 15, 0, "L0019"));
        commands.Add(Show(25, 19, "L001A"));
        commands.Add(Show(26, 20, "L001B"));
        commands.Add(Lower(27, 0, 9, "L001C"));
        commands.Add(Show(28, 21, "L001D"));
        commands.Add(Show(29, 22, "L001E"));
        commands.Add(End(30));
        // role0 friend-death script @31: four bands mirroring @A9B5-A9D1.
        commands.Add(Jump(31, 75, 41));
        commands.Add(Jump(32, 66, 48));
        commands.Add(Jump(33, 50, 55));
        commands.Add(Lower(34, 0, 5, "L0023"));
        commands.Add(Show(35, 23, "L0024"));
        commands.Add(CenterWindow(36, 0, "L0025"));
        commands.Add(Show(37, 24, "L0026"));
        commands.Add(HealHp(38, 9999, "L0027"));
        commands.Add(Boost(39, 17, 5, "L0028"));
        commands.Add(End(40));
        commands.Add(Lower(41, 0, 5, "L002A"));
        commands.Add(Show(42, 25, "L002B"));
        commands.Add(CenterWindow(43, 0, "L002C"));
        commands.Add(Show(44, 26, "L002D"));
        commands.Add(HealMp(45, 9999, "L002E"));
        commands.Add(Boost(46, 18, 10, "L002F"));
        commands.Add(End(47));
        commands.Add(Lower(48, 0, 5, "L0031"));
        commands.Add(Show(49, 27, "L0032"));
        commands.Add(CenterWindow(50, 0, "L0033"));
        commands.Add(Show(51, 28, "L0034"));
        commands.Add(Boost(52, 17, 25, "L0035"));
        commands.Add(Boost(53, 18, 25, "L0036"));
        commands.Add(End(54));
        commands.Add(Lower(55, 0, 10, "L0038"));
        commands.Add(Show(56, 29, "L0039"));
        commands.Add(Boost(57, 20, 90, "L003A"));
        commands.Add(Boost(58, 21, 90, "L003B"));
        commands.Add(End(59));
        commands[59]!["next"] = null; // the true end of the chunk has no WORD successor

        Files = new Dictionary<string, JsonObject>
        {
            ["hooks"] = new JsonObject
            {
                ["formatVersion"] = 1, ["profile"] = "faithful", ["fileId"] = "hooks",
                ["entries"] = new JsonArray(
                    new JsonObject { ["id"] = "npc", ["owner"] = new JsonObject { ["type"] = "eventObject", ["id"] = 28 },
                        ["slot"] = "wTriggerScript", ["kind"] = "trigger", ["target"] = "L0001", ["sourceAddress"] = 1 },
                    new JsonObject { ["id"] = "role1-dying", ["owner"] = new JsonObject { ["type"] = "object", ["id"] = 37 },
                        ["slot"] = "player.wScriptOnDying", ["kind"] = "trigger", ["target"] = "L0005", ["sourceAddress"] = 5 },
                    new JsonObject { ["id"] = "role0-friend-death", ["owner"] = new JsonObject { ["type"] = "object", ["id"] = 36 },
                        ["slot"] = "player.wScriptOnFriendDeath", ["kind"] = "trigger", ["target"] = "L001F", ["sourceAddress"] = 31 }),
                ["commands"] = commands
            }
        };
    }

    public TriggerEventCollection Load() => TriggerEventCollection.Parse(Index.ToJsonString(),
        Files.ToDictionary(pair => pair.Key, pair => pair.Value.ToJsonString()));
}
