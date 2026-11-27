using System.Text.Json.Nodes;
using Xunit;
using Pal.Core.State;
using Pal.Core.Exploration;
using Pal.Core.Battle;
using Pal.Core.Events;

namespace Pal.Core.Tests;

/// <summary>#48 unit 2 business tests: equipment-driven execution in the classic combat and lifecycle.
/// Expected numbers are hand-derived from the fight.c formulas; the closure content is authored input.</summary>
public sealed class ClassicBattleEquipmentSessionTests
{
    // Object 170 for role 1: attack+10/defense+4/dexterity+20 and a persistent dual attack (>999).
    private const int AttackBonus = 10, DefenseBonus = 4, DexterityBonus = 20;

    [Fact]
    public void Session_consumes_the_effective_panel_and_publishes_it_per_member()
    {
        // Base panels 35/18/100 become 45/22/120; the clean member keeps its base panel.
        var random = new ScriptedRandom([]);
        var content = Content();
        var session = new ClassicBattleSession(Definition(content), random, BattleEquipmentState.Initialize(content));
        Assert.Equal(45, session.Current.Players[1].Attack);
        Assert.Equal(22, session.Current.Players[1].Defense);
        Assert.Equal(120, session.Current.Players[1].Dexterity);
        Assert.Equal((40, 20, 100), (session.Current.Players[0].Attack, session.Current.Players[0].Defense, session.Current.Players[0].Dexterity));
        // The source content keeps base panels; the projection is the session's own state.
        Assert.Equal((35, 18, 100), (content.Players[1].Attack, content.Players[1].Defense, content.Players[1].Dexterity));
    }

    [Fact]
    public void Dual_attack_strikes_twice_on_one_action_and_only_wins_after_the_second_strike()
    {
        // Queue draws E0:18,E1:19 then P0:100,P1:120 -> P1,P0,E1,E0. Role1's effective attack 45
        // against defense 24 gives base 52 +1 = 53, so each 43hp slot dies to one strike.
        var random = new ScriptedRandom([.. QueueDraws(), .. Strike(), .. Strike(), I(2, 3, 2), .. Role0Strike(), I(2, 3, 2)]);
        var session = new ClassicBattleSession(Definition(Content()), random, BattleEquipmentState.Initialize(Content()));
        session.SelectAttack(0, 0);
        session.SelectAttack(1, 0);
        // First strike kills slot 0; the battle must not finish mid-action.
        Assert.True(session.ResolveNextAction());
        Assert.Equal(ClassicBattlePhase.Resolving, session.Current.Phase);
        Assert.Null(session.Completion);
        Assert.Equal(0, session.Current.Enemies[0].Hp);
        Assert.False(session.Current.LastAction!.SecondStrike);
        Assert.Equal(53, session.Current.LastAction.Damage);
        // The second strike keeps the validated slot: it lands on the dead slot and cannot
        // reduce HP below zero again (fight.c:3628 keeps sTarget through the t loop).
        Assert.True(session.ResolveNextAction());
        var second = session.Current.LastAction!;
        Assert.True(second.SecondStrike);
        Assert.Equal(0, second.TargetIndex);
        Assert.Equal(53, second.Damage);
        Assert.Equal(0, session.Current.Enemies[0].Hp);
        Assert.Equal(ClassicBattlePhase.Resolving, session.Current.Phase);
        // Player 0's own action retargets from the dead slot 0 to slot 1 (SelectAutoTargetFrom)
        // and ends the battle; the dead enemies then pass without new actions.
        Assert.True(session.ResolveNextAction());
        Assert.Equal(1, session.Current.LastAction!.TargetIndex);
        Assert.Equal(43, session.Current.LastAction.Damage);
        Assert.Equal(ClassicBattlePhase.Won, session.Current.Phase);
        Assert.NotNull(session.Completion);
        Assert.False(session.ResolveNextAction());
        // One action counts once even with two strikes (fight.c:3753 wCount++ after the loop).
        Assert.Equal(new ClassicBattleCounts(1, 2), session.Current.Players[1].Counts);
        Assert.Equal(new ClassicBattleCounts(1, 2), session.Current.Players[0].Counts);
        random.AssertExhausted();
    }

    [Fact]
    public void Effective_defense_is_consumed_when_an_enemy_strikes_the_equipped_member()
    {
        // One fast enemy (dex 500 -> 1518) strikes first; the target draw picks member 1, the evade
        // roll fails, and defense 22 turns base 43/2+0 into 18 (base 18 would give 21).
        var content = Content(enemyCount: 1, enemyDexterity: 500);
        var random = new ScriptedRandom([.. SingleQueueDraws(), I(0, 1, 1), I(0, 16, 9), I(0, 2, 0), I(0, 1, 0), I(1, 10, 1)]);
        var session = new ClassicBattleSession(Definition(content), random, BattleEquipmentState.Initialize(content));
        session.SelectAttack(0, 0);
        session.SelectAttack(1, 0);
        Assert.True(session.ResolveNextAction());
        var hit = session.Current.LastAction!;
        Assert.Equal(ClassicBattleSide.Enemy, hit.Attacker);
        Assert.Equal(1, hit.TargetIndex);
        Assert.Equal(18, hit.Damage);
        Assert.Equal(95 - 18, session.Current.Players[1].Hp);
        random.AssertExhausted();
    }

    [Theory]
    [InlineData(25, 16, true)]   // dying member, defend roll succeeds -> covered
    [InlineData(95, 16, false)]  // healthy member never draws the cover branch
    [InlineData(25, 9, false)]   // dying member, defend roll fails -> hit lands
    public void Cover_interposes_only_for_a_dying_target_behind_a_responsive_party_member(
        int memberHp, int defendRoll, bool covered)
    {
        // role1 covered by role0 (rgwCoveredBy). The enemy (speed 1518) acts first and draws member 1.
        var content = Content(enemyCount: 1, enemyDexterity: 500, member1Hp: memberHp, player1CoveredBy: 0);
        Draw[] draws = covered || defendRoll >= 10
            ? [.. SingleQueueDraws(), I(0, 1, 1), I(0, 16, defendRoll)]
            : [.. SingleQueueDraws(), I(0, 1, 1), I(0, 16, defendRoll), I(0, 2, 0), I(0, 1, 0), I(1, 10, 1)];
        var random = new ScriptedRandom(draws);
        var session = new ClassicBattleSession(Definition(content), random, BattleEquipmentState.Initialize(content));
        session.SelectAttack(0, 0);
        session.SelectAttack(1, 0);
        Assert.True(session.ResolveNextAction());
        var action = session.Current.LastAction!;
        if (covered)
        {
            // fight.c:4959-4973: the coverer steps in, nobody takes damage, this is not an evade.
            Assert.Equal(0, action.Damage);
            Assert.False(action.Evaded);
            Assert.Equal(0, action.CovererPartyIndex);
            Assert.Equal(memberHp, session.Current.Players[1].Hp);
            Assert.Equal(100, session.Current.Players[0].Hp);
        }
        else if (defendRoll >= 10)
        {
            Assert.True(action.Evaded); // healthy member, plain evade without any coverer
            Assert.Null(action.CovererPartyIndex);
            Assert.Equal(0, action.Damage);
        }
        else
        {
            Assert.False(action.Evaded);
            Assert.Null(action.CovererPartyIndex);
            Assert.True(action.Damage > 0);
            Assert.True(session.Current.Players[1].Hp < memberHp);
        }
        random.AssertExhausted();
    }

    [Fact]
    public void A_dying_coverer_cannot_respond_and_the_strike_stays_a_plain_evade()
    {
        // fight.c:4961-4966: a coverer that is itself dying means nobody covers.
        var content = Content(enemyCount: 1, enemyDexterity: 500, member1Hp: 25, member0Hp: 20, player1CoveredBy: 0);
        var random = new ScriptedRandom([.. SingleQueueDraws(), I(0, 1, 1), I(0, 16, 16)]);
        var session = new ClassicBattleSession(Definition(content), random, BattleEquipmentState.Initialize(content));
        session.SelectAttack(0, 0);
        session.SelectAttack(1, 0);
        Assert.True(session.ResolveNextAction());
        Assert.True(session.Current.LastAction!.Evaded);
        Assert.Null(session.Current.LastAction.CovererPartyIndex);
        Assert.Equal(25, session.Current.Players[1].Hp);
        random.AssertExhausted();
    }

    [Fact]
    public void Equipment_bravery_forces_the_critical_branch_and_protect_halves_enemy_damage()
    {
        // Bravery (slot 5) from object 210 worn beside 170: effective attack 45 vs 24 -> 52+1 = 53;
        // the critical roll 1 misses but the status forces the critical *3 = 159 (fight.c:3633
        // keeps the roll in the stream). The dual attack from 170 still gives two strikes.
        var bravery = Content(role1Equipment: [0, 210, 0, 170, 0, 0], extraBravery: 60000);
        var random = new ScriptedRandom([.. QueueDraws(), .. Strike(), .. Strike(), I(2, 3, 2)]);
        var session = new ClassicBattleSession(Definition(bravery), random, BattleEquipmentState.Initialize(bravery));
        session.SelectAttack(0, 0);
        session.SelectAttack(1, 1);
        session.ResolveNextAction(); // P1 first: dex 120 over 100
        Assert.True(session.Current.LastAction!.Critical);
        Assert.Equal(159, session.Current.LastAction.Damage);
        Assert.False(session.Current.LastAction.SecondStrike);
        session.ResolveNextAction();
        Assert.True(session.Current.LastAction!.SecondStrike);
        random.AssertExhausted();
        // Protect (slot 6): enemy base 43/2+0 = 21 halves to 10 (fight.c:5086 before the HP clamp).
        var protect = Content(enemyCount: 1, enemyDexterity: 500, extraProtect: 60000, role1Equipment: [0, 210, 0, 0, 0, 0]);
        var protectRandom = new ScriptedRandom([.. SingleQueueDraws(), I(0, 1, 1), I(0, 16, 9), I(0, 2, 0), I(0, 1, 0), I(1, 10, 1)]);
        var protectSession = new ClassicBattleSession(Definition(protect), protectRandom, BattleEquipmentState.Initialize(protect));
        protectSession.SelectAttack(0, 0);
        protectSession.SelectAttack(1, 0);
        protectSession.ResolveNextAction();
        Assert.Equal(10, protectSession.Current.LastAction!.Damage);
        protectRandom.AssertExhausted();
    }

    [Fact]
    public void Equipment_haste_triples_the_queue_dexterity_before_the_cap()
    {
        // fight.c:336-349 CLASSIC: 100 * 3 = 300 outruns both the clean member (100) and the enemies.
        var haste = Content(role1Equipment: [0, 210, 0, 0, 0, 0], extraHaste: 60000);
        var random = new ScriptedRandom(QueueDraws());
        var session = new ClassicBattleSession(Definition(haste), random, BattleEquipmentState.Initialize(haste));
        session.SelectAttack(0, 0);
        session.SelectAttack(1, 1);
        Assert.Equal(ClassicBattleSide.Player, session.Current.ActionQueue[0].Side);
        Assert.Equal(1, session.Current.ActionQueue[0].ActorIndex);
        Assert.Equal(300, session.Current.ActionQueue[0].Dexterity);
        random.AssertExhausted();
    }

    [Fact]
    public void Lifecycle_keeps_persistent_equipment_statuses_over_battles_without_stacking()
    {
        var content = Content(enemyCount: 1);
        var equipment = BattleEquipmentState.Initialize(content);
        Assert.True(equipment.Declared);
        // One 43hp enemy: role1's dual action (53 then 53 on the dead slot) wins the battle.
        var random = new ScriptedRandom([.. SingleQueueDraws(), .. Strike(), .. Strike(), I(2, 3, 2)]);
        var lifecycle = new ClassicBattleLifecycleSession([content.InitialRoles[0], content.InitialRoles[1]],
            content.Players, content.EnemyTeams[18], content.GrowthByRole, random, equipment);
        Assert.Equal(60000, lifecycle.Current.Roles[1].Statuses[8]);
        lifecycle.SelectAttack(0, 0);
        lifecycle.SelectAttack(1, 0);
        while (lifecycle.Completion is null) Assert.True(lifecycle.ResolveNextAction());
        Assert.Equal(ClassicBattlePhase.Won, lifecycle.Completion.Outcome);
        // The battle-end cleanup only clears <=999 words, so the equipment status survives it
        // (global.c:2304); a wrong implementation clearing all statuses fails right here.
        Assert.Equal(60000, lifecycle.Current.Roles[1].Statuses[8]);
        // A fresh session from the settled roles re-applies the overlay by the max rule only:
        // same value, no stacking, and the dual attack still produces exactly two strikes.
        var settled = lifecycle.Current.Roles;
        var settledPlayers = content.Players.Select(p => p with { Hp = settled[p.RoleId].Hp }).ToArray();
        var secondRandom = new ScriptedRandom([.. SingleQueueDraws(), .. Strike(), .. Strike(), I(2, 3, 2)]);
        var second = new ClassicBattleLifecycleSession([settled[0], settled[1]], settledPlayers,
            content.EnemyTeams[18], content.GrowthByRole, secondRandom, equipment);
        Assert.Equal(60000, second.Current.Roles[1].Statuses[8]);
        second.SelectAttack(0, 0);
        second.SelectAttack(1, 0);
        int strikes = 0, firstStrikes = 0;
        while (second.Completion is null)
        {
            Assert.True(second.ResolveNextAction());
            if (second.Current.Combat.LastAction is { Attacker: ClassicBattleSide.Player, ActorIndex: 1 } action)
            {
                strikes++;
                if (!action.SecondStrike) firstStrikes++;
            }
        }
        Assert.Equal(2, strikes);
        Assert.Equal(1, firstStrikes);
        Assert.Equal(1, second.Current.Roles[1].HiddenExperience["attack"].Count);
        Assert.Equal(60000, second.Current.Roles[1].Statuses[8]);
        secondRandom.AssertExhausted();
    }

    [Theory]
    [InlineData("battle-sprite")]
    [InlineData("stale-status")]
    [InlineData("timed-status")]
    [InlineData("bad-status")]
    [InlineData("equipment-mismatch")]
    public void Unsupported_equipment_dependencies_are_rejected_before_any_random(string fault)
    {
        var content = fault switch
        {
            "battle-sprite" => Content(battleSprite: 5),
            "stale-status" => Content(settledDualAttack: 60001),
            "timed-status" => Content(settledBravery: 500),
            "bad-status" => Content(settledBadStatus: true),
            "equipment-mismatch" => Content(player1EquipmentZero: true),
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };
        var random = new ScriptedRandom([]);
        // The direct session preflight catches the player-level faults before any state.
        if (fault is "battle-sprite" or "equipment-mismatch")
            Assert.Throws<InvalidDataException>(() => new ClassicBattleSession(Definition(content), random,
                BattleEquipmentState.Initialize(content)));
        // The selected entry refuses every fault before RNG or request side effects.
        var script = new TriggerScriptSession(EquipmentEntryRedTests.Events(), TriggerScriptSessionTests.Language(),
            content, random);
        Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 28));
        Assert.Null(script.PendingBattle);
        random.AssertExhausted();
    }

    [Fact]
    public void StartBattle_builds_the_lifecycle_from_the_declared_closure()
    {
        var random = new ScriptedRandom([]);
        var script = new TriggerScriptSession(EquipmentEntryRedTests.Events(), TriggerScriptSessionTests.Language(),
            Content(), random);
        script.Start("shared", "npc", 28);
        var battle = script.PendingBattle!;
        Assert.Equal((45, 22, 120), (battle.Lifecycle.Current.Combat.Players[1].Attack,
            battle.Lifecycle.Current.Combat.Players[1].Defense, battle.Lifecycle.Current.Combat.Players[1].Dexterity));
        Assert.Equal(60000, battle.Lifecycle.Current.Roles[1].Statuses[8]);
        Assert.Equal(0, battle.Lifecycle.Current.Roles[0].Statuses[8]);
        random.AssertExhausted();
    }

    [Fact]
    public void SelectedEquipmentWordsRejectBeforeEffectsAndRandom()
    {
        // ARTIFICIAL selected-guard fixture: object170/role1 is not a retail field4/65 claim.
        // The real 1/351 writes belong to dormant roles2/3; sprite0 isolates the new guard here.
        var fixture = new EventFixture();
        fixture.Commands[1] = JsonNode.Parse("""
          {"label":"L0001","op":"setMusic","musicId":5,"mode":3,"next":"L0002","source":{"index":1,"opcode":67,"operands":[5,3,0]}}
          """);
        fixture.Commands[2] = JsonNode.Parse("""
          {"label":"L0002","op":"changeHpMp","scope":0,"amount":-5,"next":"L0003","source":{"index":2,"opcode":29,"operands":[0,65531,0]}}
          """);
        fixture.Commands[3] = JsonNode.Parse("""
          {"label":"L0003","op":"startBattle","enemyTeamId":18,"lossTarget":"L0004","isBoss":true,"fleeTarget":null,
           "next":"L0004","source":{"index":3,"opcode":7,"operands":[18,4,0]}}
          """);
        var events = fixture.Load();
        var language = TriggerScriptSessionTests.Language();
        var legacy = Content();
        var typedNull = new TriggerBattleContent(legacy.InitialRoles.Values.ToArray(), legacy.Players,
            legacy.GrowthByRole, legacy.EnemyTeams, legacy.RoleEquipment,
            legacy.EquipmentObjects.Values.Select(obj => obj with
            {
                Effects = obj.Effects.Select(effect => effect with { AttackAll = null, CooperativeMagic = null }).ToArray()
            }).ToArray());
        var explicitZero = Content(equipmentAttackAll: 0, equipmentCooperativeMagic: 0);
        Assert.Null(legacy.EquipmentObjects[170].Effects[1].AttackAll);
        Assert.Null(legacy.EquipmentObjects[170].Effects[1].CooperativeMagic);
        Assert.Null(typedNull.EquipmentObjects[170].Effects[1].AttackAll);
        Assert.Null(typedNull.EquipmentObjects[170].Effects[1].CooperativeMagic);
        Assert.Equal(0, explicitZero.EquipmentObjects[170].Effects[1].AttackAll);
        Assert.Equal(0, explicitZero.EquipmentObjects[170].Effects[1].CooperativeMagic);

        // Controls run first, in this order: old omission, explicit typed null, explicit JSON zero.
        foreach (var content in new[] { legacy, typedNull, explicitZero })
        {
            var random = new ScriptedRandom([]);
            var equipment = BattleEquipmentState.Initialize(content);
            Assert.Equal([0, 0, 0, 170, 0, 0], equipment.Equipment(1));
            Assert.Equal([0, 0, 0, 0, 0, 0, 0, 0, 60000], equipment.StatusOverlay(1));
            Assert.Equal(0, content.EquipmentObjects[170].Effects[1].BattleSprite);
            var session = new ClassicBattleSession(Definition(content), random, equipment);
            Assert.Equal((40, 20, 100), (session.Current.Players[0].Attack,
                session.Current.Players[0].Defense, session.Current.Players[0].Dexterity));
            Assert.Equal((45, 22, 120), (session.Current.Players[1].Attack,
                session.Current.Players[1].Defense, session.Current.Players[1].Dexterity));
            var lifecycle = new ClassicBattleLifecycleSession(content.InitialRoles.Values.ToArray(), content.Players,
                content.EnemyTeams[18], content.GrowthByRole, random, equipment);
            Assert.Equal((45, 22, 120), (lifecycle.Current.Combat.Players[1].Attack,
                lifecycle.Current.Combat.Players[1].Defense, lifecycle.Current.Combat.Players[1].Dexterity));
            Assert.Equal([0, 0, 0, 0, 0, 0, 0, 0, 60000], lifecycle.Current.Roles[1].Statuses);
            var owner = new RoleLiveState(content.InitialRoles.Values.ToArray());
            var world = DynamicEventWorld.ForHost(
                new StaticSceneRules(new SceneMap(2, new uint[16384], [new SceneFrame(0, 32, 15)]), []),
                new StaticLeader(320, 320, 0, 0, 0, 3,
                    new SceneSprite(17, Enumerable.Range(0, 12).Select(i => new SceneFrame(i, 20, 30)))), 2, 0, 0, owner);
            var script = TriggerScriptSession.ForSharedRoles(events, language, content, random, owner, world);
            Assert.Equal(new[] { 18 }, script.GetCurrentEntryRequirements("shared", "npc", 1).BattleTeamIds);
            Assert.Null(script.PendingBattle);
            Assert.Null(script.Music);
            script.Start("shared", "npc", 1);
            Assert.Equal(TriggerScriptPhase.WaitingForBattle, script.Phase);
            Assert.Equal(new TriggerMusicState(5, 3), script.Music);
            Assert.Equal((90, 15), (world.PartyVitals[1].Hp, world.PartyVitals[1].Mp));
            var battle = Assert.IsType<TriggerBattleRequest>(script.PendingBattle);
            Assert.Equal(18, battle.EnemyTeamId);
            Assert.Equal((45, 22, 120), (battle.Lifecycle.Current.Combat.Players[1].Attack,
                battle.Lifecycle.Current.Combat.Players[1].Defense, battle.Lifecycle.Current.Combat.Players[1].Dexterity));
            Assert.Equal((90, 15), (battle.Lifecycle.Current.Roles[1].Hp, battle.Lifecycle.Current.Roles[1].Mp));
            Assert.Equal([0, 0, 0, 0, 0, 0, 0, 0, 60000], battle.Lifecycle.Current.Roles[1].Statuses);
            Assert.Equal((35, 18, 100), (content.Players[1].Attack, content.Players[1].Defense, content.Players[1].Dexterity));
            Assert.Equal((95, 20), (content.InitialRoles[1].Hp, content.InitialRoles[1].Mp));
            random.AssertExhausted();
        }

        // #143 (T15): the equipment rgwAttackAll word is the reference all-target capability
        // (PAL_PlayerCanAttackAll global.c 2048-2080) and now opens the combat domain through
        // every entry; the authored player word stays false here, so the capability is the
        // equipment register alone.
        foreach (int attackAll in new[] { 1, 65535 })
        {
            var content = Content(equipmentAttackAll: attackAll);
            var effect = content.EquipmentObjects[170].Effects[1];
            Assert.Equal(attackAll, effect.AttackAll);
            Assert.Null(effect.CooperativeMagic);
            Assert.Equal(0, effect.BattleSprite);
            Assert.False(content.Players[1].AttackAll);
            Assert.Equal(new int[9], content.Players[0].Statuses);
            var equipment = BattleEquipmentState.Initialize(content);
            Assert.Equal(attackAll, equipment.AttackAllWord(1, 3));
            var random = new ScriptedRandom(QueueDraws());
            var session = new ClassicBattleSession(Definition(content), random, equipment);
            Assert.Equal(new[] { false, true }, session.Current.Players.Select(p => p.CanAttackAll).ToArray());
            var uncapable = session.Current;
            Assert.Throws<InvalidOperationException>(() => session.SelectAttackAll(0));
            Assert.Same(uncapable, session.Current);
            session.SelectAttack(0, 0);
            Assert.False(session.Current.Players[0].SelectedAttackAll);
            session.SelectAttackAll(1);
            Assert.Equal(ClassicBattlePhase.Resolving, session.Current.Phase);
            Assert.True(session.Current.Players[1].SelectedAttackAll);
            Assert.Null(session.Current.Players[1].SelectedEnemySlot);
            random.AssertExhausted();
            new ClassicBattleLifecycleSession(content.InitialRoles.Values.ToArray(), content.Players,
                content.EnemyTeams[18], content.GrowthByRole, new ScriptedRandom(QueueDraws()), equipment);
            var owner = new RoleLiveState(content.InitialRoles.Values.ToArray());
            var world = DynamicEventWorld.ForHost(
                new StaticSceneRules(new SceneMap(2, new uint[16384], [new SceneFrame(0, 32, 15)]), []),
                new StaticLeader(320, 320, 0, 0, 0, 3,
                    new SceneSprite(17, Enumerable.Range(0, 12).Select(i => new SceneFrame(i, 20, 30)))), 2, 0, 0, owner);
            var script = TriggerScriptSession.ForSharedRoles(events, language, content, new ScriptedRandom(QueueDraws()), owner, world);
            script.GetCurrentEntryRequirements("shared", "npc", 1);
            script.Start("shared", "npc", 1);
            Assert.True(script.PendingBattle!.Lifecycle.Current.Combat.Players[1].CanAttackAll);
        }

        (int? AttackAll, int? CooperativeMagic, string Word)[] cases =
            [(null, 351, "cooperativeMagic"), (null, 65535, "cooperativeMagic")];
        foreach (var test in cases)
        {
            var content = Content(equipmentAttackAll: test.AttackAll, equipmentCooperativeMagic: test.CooperativeMagic);
            var effect = content.EquipmentObjects[170].Effects[1];
            Assert.Equal(test.AttackAll, effect.AttackAll);
            Assert.Equal(test.CooperativeMagic, effect.CooperativeMagic);
            Assert.Equal(0, effect.BattleSprite);
            Assert.False(content.Players[1].AttackAll);
            Assert.Equal(new int[9], content.Players[1].Statuses);
            var equipment = BattleEquipmentState.Initialize(content);
            var random = new ScriptedRandom([]);
            var directError = Assert.Throws<InvalidDataException>(() =>
                new ClassicBattleSession(Definition(content), random, equipment));
            var lifecycleError = Assert.Throws<InvalidDataException>(() =>
                new ClassicBattleLifecycleSession(content.InitialRoles.Values.ToArray(), content.Players,
                    content.EnemyTeams[18], content.GrowthByRole, random, equipment));
            var entryError = Assert.Throws<InvalidDataException>(() =>
                TriggerScriptSession.ValidateEntry(events, language, content, "shared", "npc"));
            var owner = new RoleLiveState(content.InitialRoles.Values.ToArray());
            var world = DynamicEventWorld.ForHost(
                new StaticSceneRules(new SceneMap(2, new uint[16384], [new SceneFrame(0, 32, 15)]), []),
                new StaticLeader(320, 320, 0, 0, 0, 3,
                    new SceneSprite(17, Enumerable.Range(0, 12).Select(i => new SceneFrame(i, 20, 30)))), 2, 0, 0, owner);
            var script = TriggerScriptSession.ForSharedRoles(events, language, content, random, owner, world);
            var role0 = owner.Roles[0];
            var role1 = owner.Roles[1];
            var before = (script.Phase, script.CallId, script.StartedEntry, script.SourceRequestId, script.Caller,
                script.ReturnEntry, script.CurrentPosition, script.Context, script.Music, script.PendingDisplay, script.PendingBattle);
            var queryError = Assert.Throws<InvalidDataException>(() => script.GetCurrentEntryRequirements("shared", "npc", 1));
            var startError = Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 1));
            foreach (var error in new[] { directError, lifecycleError, entryError, queryError, startError })
            {
                Assert.Contains("equipment", error.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(test.Word, error.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("role 1", error.Message, StringComparison.OrdinalIgnoreCase);
            }
            Assert.Equal(before, (script.Phase, script.CallId, script.StartedEntry, script.SourceRequestId, script.Caller,
                script.ReturnEntry, script.CurrentPosition, script.Context, script.Music, script.PendingDisplay, script.PendingBattle));
            Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
            Assert.Null(script.PendingBattle);
            Assert.Null(script.Music);
            Assert.Same(role0, owner.Roles[0]);
            Assert.Same(role1, owner.Roles[1]);
            Assert.Same(role1, script.BattleRoles![1]);
            Assert.Equal((100, 20), (world.PartyVitals[0].Hp, world.PartyVitals[0].Mp));
            Assert.Equal((95, 20), (world.PartyVitals[1].Hp, world.PartyVitals[1].Mp));
            Assert.Equal([0, 0, 0, 0, 0, 0, 0, 0, 60000], role1.Statuses);
            Assert.Equal(55, role1.HiddenExperience["attack"].Count);
            random.AssertExhausted();
        }
    }

    private static ClassicBattleDefinition Definition(TriggerBattleContent content) =>
        new(content.Players, content.EnemyTeams[18]);

    private static Draw[] QueueDraws() => [F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), F(.9f, 1.1f, 1)];
    private static Draw[] SingleQueueDraws() => [F(.9f, 1.1f, 1), F(.9f, 1.1f, 1), F(.9f, 1.1f, 1)];
    // Role1 (effective attack 45) vs defense 24: (short)(90 - 38.4 + .5) = 52, +1, roll 1 no
    // critical, x1 -> 53. Role0 additionally rolls its 0..11 bonus (roll 1 misses).
    private static Draw[] Strike() => [I(1, 2, 1), I(0, 5, 1), F(1, 1.125f, 1)];
    private static Draw[] Role0Strike() => [I(1, 2, 1), I(0, 5, 1), I(0, 11, 1), F(1, 1.125f, 1)];

    private static TriggerBattleContent Content(int enemyCount = 2, int enemyDexterity = 0, int member1Hp = 95,
        int member0Hp = 100, int? player1CoveredBy = null, int battleSprite = 0, int settledDualAttack = 60000,
        int settledBravery = 0, bool settledBadStatus = false, bool player1EquipmentZero = false,
        int[]? role1Equipment = null, int extraBravery = 0, int extraProtect = 0, int extraHaste = 0,
        int? equipmentAttackAll = null, int? equipmentCooperativeMagic = null)
    {
        role1Equipment ??= [0, 0, 0, 170, 0, 0];
        int[] role1Statuses = [0, 0, 0, 0, 0, settledBravery, 0, 0, settledDualAttack];
        if (settledBadStatus) role1Statuses[3] = 77;
        string zeroEquipment = "[0,0,0,0,0,0]", zeroStatuses = "[0,0,0,0,0,0,0,0,0]";
        string wornEquipment = $"[{string.Join(",", role1Equipment)}]";
        string wornStatuses = $"[{string.Join(",", role1Statuses)}]";
        string player1Equipment = player1EquipmentZero ? zeroEquipment : wornEquipment;
        string player1Cover = player1CoveredBy?.ToString() ?? "null";
        string enemies = enemyCount == 1 ? Enemy(enemyDexterity) : $"{Enemy(enemyDexterity)},{Enemy(enemyDexterity + 1)}";
        string grid = string.Join(",", Enumerable.Repeat(zeroEquipment, 6).Select((row, index) =>
            index == 1 ? wornEquipment : row));
        string dual = Effect(AttackBonus, DefenseBonus, DexterityBonus, battleSprite, 0, 0, 0, 60000);
        string extra = Effect(0, 0, 0, 0, extraBravery, extraProtect, extraHaste);
        string nothing = Effect(0, 0, 0, 0, 0, 0, 0);
        string json = $$"""
        {
          "formatVersion":1,
          "roles":[
            {{Role(0, member0Hp, 40, 20, 100, zeroEquipment, zeroStatuses)}},
            {{Role(1, member1Hp, 35, 18, 100, wornEquipment, wornStatuses)}}],
          "players":[
            {{Player(0, member0Hp, 40, 20, 100, zeroEquipment, "null")}},
            {{Player(1, member1Hp, 35, 18, 100, player1Equipment, player1Cover)}}],
          "growth":[
            {"roleId":0,"levelUpExperience":[],"levelUpMagics":[{"level":3,"magicId":10}]},
            {"roleId":1,"levelUpExperience":[],"levelUpMagics":[{"level":3,"magicId":10}]}],
          "teams":[{"teamId":18,"enemies":[{{enemies}}]}],
          "roleEquipment":[{{grid}}],
          "equipments":[
            {"objectId":170,"effects":[
              {{nothing}},
              {{dual}},
              {{nothing}},
              {{nothing}},
              {{nothing}},
              {{nothing}}]},
            {"objectId":210,"effects":[
              {{nothing}},
              {{extra}},
              {{nothing}},
              {{nothing}},
              {{nothing}},
              {{nothing}}]}]
        }
        """;
        var data = JsonNode.Parse(json)!.AsObject();
        if (equipmentAttackAll is int attackAll)
            data["equipments"]![0]!["effects"]![1]!["attackAll"] = attackAll;
        if (equipmentCooperativeMagic is int cooperativeMagic)
            data["equipments"]![0]!["effects"]![1]!["cooperativeMagic"] = cooperativeMagic;
        foreach (int role in new[] { 0, 1 })
            data["growth"]![role]!["levelUpExperience"] = new JsonArray(
                Enumerable.Repeat(0, 100).Select(_ => JsonValue.Create(100) as JsonNode).ToArray());
        return ModernBattleContent.Parse(data.ToJsonString());
    }

    private static string Role(int roleId, int hp, int attack, int defense, int dexterity, string equipment, string statuses) =>
        $$$"""{"roleId":{{{roleId}}},"level":2,"hp":{{{hp}}},"maxHp":150,"mp":20,"maxMp":50,"primaryExperience":{"value":5,"level":2,"count":17},"hiddenExperience":{"health":{"value":7,"level":3,"count":55},"magic":{"value":7,"level":3,"count":55},"attack":{"value":7,"level":3,"count":55},"magicPower":{"value":7,"level":3,"count":55},"defense":{"value":7,"level":3,"count":55},"dexterity":{"value":7,"level":3,"count":55},"flee":{"value":7,"level":3,"count":55}},"equipment":{{{equipment}}},"magics":[9,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],"statuses":{{{statuses}}},"coveredByRoleId":0}""";

    private static string Player(int roleId, int hp, int attack, int defense, int dexterity, string equipment, string coveredBy) =>
        $$"""{"roleId":{{roleId}},"hp":{{hp}},"maxHp":150,"attack":{{attack}},"defense":{{defense}},"dexterity":{{dexterity}},"equipment":{{equipment}},"statuses":[0,0,0,0,0,0,0,0,0],"poisons":[0],"dyingScript":0,"friendDeathScript":0,"coveredByRoleId":{{coveredBy}},"attackAll":false}""";

    private static string Enemy(int dexterity) =>
        $$"""{"id":7,"level":0,"hp":43,"attack":0,"defense":0,"dexterity":{{dexterity}},"physicalResistance":1,"statuses":[0,0,0,0,0,0,0,0,0],"poisons":[0],"turnStartScript":0,"readyScript":0,"battleEndScript":0,"magicId":0,"dualMove":0,"attackEquivItem":0,"attackEquivItemRate":0,"experience":0,"cash":0}""";

    private static string Effect(int attack, int defense, int dexterity, int battleSprite, int bravery, int protect, int haste, int dualAttack = 0) =>
        $$"""{"attack":{{attack}},"defense":{{defense}},"dexterity":{{dexterity}},"battleSprite":{{battleSprite}},"statuses":[0,0,0,0,0,{{bravery}},{{protect}},{{haste}},{{dualAttack}}]}""";

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
