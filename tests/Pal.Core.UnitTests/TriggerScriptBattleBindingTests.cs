using System.Text.Json.Nodes;
using Xunit;

namespace Pal.Core.Tests;

public sealed class TriggerScriptBattleBindingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void ValidateEntry_checks_initial_content_without_starting_a_call_or_changing_role(int hp)
    {
        var events = Fixture().Load();
        var content = Content(Role() with { Hp = hp });
        TriggerScriptSession.ValidateEntry(events, TriggerScriptSessionTests.Language(), content, "shared", "npc");
        Assert.Equal(hp, content.InitialRole.Hp);
        Assert.Equal(20, content.InitialRole.Mp);
        Assert.Equal(55, content.InitialRole.HiddenExperience["attack"].Count);
        var random = new BindingRandom();
        var script = new TriggerScriptSession(events, TriggerScriptSessionTests.Language(), content, random);
        Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
        Assert.Null(script.CurrentPosition);
        Assert.Null(script.PendingBattle);
        script.Start("shared", "npc", 28);
        Assert.Equal(hp == 0 ? 1 : 100, script.PendingBattle!.Lifecycle.Current.Role.Hp);
        Assert.Equal(0, random.Draws);
    }

    [Theory]
    [InlineData("events")]
    [InlineData("language")]
    [InlineData("content")]
    public void ValidateEntry_rejects_missing_dependencies(string missing)
    {
        Assert.Throws<ArgumentNullException>(() => TriggerScriptSession.ValidateEntry(
            missing == "events" ? null! : Fixture().Load(),
            missing == "language" ? null! : TriggerScriptSessionTests.Language(),
            missing == "content" ? null! : Content(), "shared", "npc"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    public void ValidateEntry_rejects_out_of_range_inherited_color(int color)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TriggerScriptSession.ValidateEntry(
            Fixture().Load(), TriggerScriptSessionTests.Language(), Content(), "shared", "npc", color));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    public void ValidateEntry_accepts_inherited_color_endpoints(int color)
    {
        TriggerScriptSession.ValidateEntry(Fixture().Load(), TriggerScriptSessionTests.Language(), Content(), "shared", "npc", color);
    }

    [Theory]
    [InlineData("file")]
    [InlineData("entry")]
    [InlineData("auto")]
    public void ValidateEntry_requires_an_existing_trigger_entry(string fault)
    {
        var fixture = Fixture();
        if (fault == "auto")
        {
            fixture.Entry["kind"] = "auto";
            fixture.Entry["slot"] = "wAutoScript";
        }
        var error = Assert.Throws<InvalidDataException>(() => TriggerScriptSession.ValidateEntry(
            fixture.Load(), TriggerScriptSessionTests.Language(), Content(), fault == "file" ? "absent" : "shared",
            fault == "entry" ? "absent" : "npc"));
        Assert.Contains(fault == "auto" ? "auto context is unsupported" : "missing entry", error.Message);
    }

    [Theory]
    [InlineData("en", false)]
    [InlineData("en", true)]
    [InlineData("zh-Hans", false)]
    [InlineData("zh-Hans", true)]
    public void ValidateEntry_uses_only_the_supplied_language_and_exact_message_range(string languageId, bool missing)
    {
        var language = new DialogueLanguagePackage(languageId, [new DialogueMessageBlock(61, missing ? 61 : 62,
            [DialogueMessagePart.FromText(languageId == "en" ? "Victory" : "胜利")])]);
        if (missing)
        {
            var error = Assert.Throws<InvalidDataException>(() => TriggerScriptSession.ValidateEntry(
                Fixture().Load(), language, Content(), "shared", "npc"));
            Assert.Contains("shared.json#L0002 @0002", error.Message);
            Assert.Contains($"Language '{languageId}' is missing message range 61+1", error.Message);
        }
        else TriggerScriptSession.ValidateEntry(Fixture().Load(), language, Content(), "shared", "npc");
    }

    [Fact]
    public void ValidateEntry_disabled_entry_does_not_execute_archived_commands_or_grant_unselected_capabilities()
    {
        var fixture = Fixture();
        fixture.Entry["target"] = null;
        fixture.Entry["sourceAddress"] = 0;
        var content = Content(enemy: new(7, 0, 43, 0, 0, 0, 1) { Experience = 1 });
        TriggerScriptSession.ValidateEntry(fixture.Load(), TriggerScriptSessionTests.Language(), content, "shared", "npc");
        Assert.Equal(1, content.Teams[18].Experience);
        Assert.Equal(100, content.InitialRole.Hp);
    }

    [Theory]
    [InlineData("battle", false)]
    [InlineData("battle", true)]
    [InlineData("display", false)]
    [InlineData("display", true)]
    [InlineData("completed", false)]
    [InlineData("completed", true)]
    public void ValidateEntry_is_independent_of_live_requests_and_never_resets_the_settled_role(string phase, bool reject)
    {
        var events = Fixture().Load();
        var language = TriggerScriptSessionTests.Language();
        var content = Content();
        var random = new BindingRandom();
        var script = new TriggerScriptSession(events, language, content, random);
        script.Start("shared", "npc", 28);
        if (phase != "battle")
        {
            var battle = script.PendingBattle!;
            Win(battle);
            Assert.True(script.AcknowledgeBattle(battle.Id, battle.Lifecycle.Completion));
            Assert.Equal(125, script.BattleRole!.Hp);
            if (phase == "completed") FinishDisplay(script);
        }
        var before = (script.Phase, script.Caller, script.ReturnEntry, script.CurrentPosition, script.Context,
            script.PendingDisplay, script.PendingBattle, script.BattleRole, random.Draws);
        var combat = script.PendingBattle?.Lifecycle.Current;
        var display = script.PendingDisplay?.Display.Current;
        if (reject)
        {
            var unsupported = Content(growth: new(Enumerable.Repeat(5, 100).ToArray(), []));
            Assert.Throws<InvalidDataException>(() => TriggerScriptSession.ValidateEntry(events, language, unsupported, "shared", "npc"));
        }
        else TriggerScriptSession.ValidateEntry(events, language, content, "shared", "npc");
        Assert.Equal(before, (script.Phase, script.Caller, script.ReturnEntry, script.CurrentPosition, script.Context,
            script.PendingDisplay, script.PendingBattle, script.BattleRole, random.Draws));
        Assert.Same(combat, script.PendingBattle?.Lifecycle.Current);
        Assert.Same(display, script.PendingDisplay?.Display.Current);
        Assert.Equal(100, content.InitialRole.Hp);
        if (phase == "completed")
        {
            script.Start("shared", "npc", 65535);
            Assert.Equal(28, script.Caller);
            Assert.Equal(125, script.PendingBattle!.Lifecycle.Current.Role.Hp);
            Assert.Equal(35, script.PendingBattle.Lifecycle.Current.Role.Mp);
        }
    }

    [Fact]
    public void Named_battle_waits_for_real_lifecycle_settlement_then_resumes_dialogue_to_keep_end()
    {
        var random = new BindingRandom();
        var script = Script(Fixture(), random);
        script.Start("shared", "npc", 28);
        var request = Assert.IsType<TriggerBattleRequest>(script.PendingBattle);
        Assert.Equal(TriggerScriptPhase.WaitingForBattle, script.Phase);
        Assert.True(script.BlocksSceneUpdates);
        Assert.Equal(18, request.TeamId);
        Assert.True(request.IsBoss);
        Assert.Null(request.FleeAddress);
        Assert.Equal(4, request.LossAddress);
        Assert.Equal(2, request.NextAddress);
        Assert.Equal(28, request.Caller);
        Assert.Equal(new TriggerPosition("shared", "L0001", 1), request.Position);
        Assert.Equal(request.Position, script.CurrentPosition);
        Assert.Null(script.PendingDisplay);
        Assert.Equal(0, random.Draws);
        Assert.Equal(100, script.BattleRole!.Hp);
        request.Lifecycle.SelectAttack(7);
        Assert.True(request.Lifecycle.ResolveNextAction());
        Assert.Equal(ClassicBattlePhase.Won, request.Lifecycle.Completion!.Outcome);
        Assert.Equal(100, request.Lifecycle.Current.Combat.PlayerHp);
        Assert.Equal(125, request.Lifecycle.Current.Role.Hp);
        Assert.Equal(100, script.BattleRole.Hp);
        Assert.True(script.AcknowledgeBattle(request.Id, request.Lifecycle.Completion));
        Assert.Null(script.PendingBattle);
        Assert.Equal(125, script.BattleRole.Hp);
        Assert.Equal(35, script.BattleRole.Mp);
        Assert.Equal(new BattleExperienceSnapshot(7, 3, 1), script.BattleRole.HiddenExperience["attack"]);
        Assert.Equal(new BattleExperienceSnapshot(7, 3, 2), script.BattleRole.HiddenExperience["health"]);
        Assert.Equal(TriggerScriptPhase.WaitingForDisplay, script.Phase);
        var display = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.Equal(new TriggerPosition("shared", "L0004", 4), script.CurrentPosition);
        display.Display.Update(24, false, true);
        display.Display.Update(100, true, false);
        Assert.True(script.AcknowledgeDisplay(display.Id));
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.False(script.BlocksSceneUpdates);
        Assert.Equal(28, script.Caller);
        Assert.Equal(1, script.ReturnEntry);
        Assert.Equal(6, random.Draws);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Real_lost_uses_nullable_loss_successor_without_recovery_or_return_entry_rewrite(bool explicitLoss)
    {
        var fixture = Fixture();
        fixture.Commands[1]!["lossTarget"] = explicitLoss ? "L0004" : null;
        var random = new BindingRandom();
        var script = Script(fixture, random, Content(Role() with { Hp = 20 }, new(7, 0, 100, 0, 0, 0, 1)));
        script.Start("shared", "npc", 28);
        var request = script.PendingBattle!;
        request.Lifecycle.SelectAttack(7);
        request.Lifecycle.ResolveNextAction();
        Assert.Null(request.Lifecycle.Completion);
        Assert.Equal(1, request.Lifecycle.Current.Role.HiddenExperience["attack"].Count);
        request.Lifecycle.ResolveNextAction();
        Assert.Equal(ClassicBattlePhase.Lost, request.Lifecycle.Completion!.Outcome);
        Assert.True(script.AcknowledgeBattle(request.Id, request.Lifecycle.Completion));
        Assert.Equal(0, script.BattleRole!.Hp);
        Assert.Equal(20, script.BattleRole.Mp);
        Assert.Equal(new BattleExperienceSnapshot(7, 3, 1), script.BattleRole.HiddenExperience["attack"]);
        Assert.Equal(new BattleExperienceSnapshot(7, 3, 2), script.BattleRole.HiddenExperience["health"]);
        if (!explicitLoss) FinishDisplay(script);
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(new TriggerPosition("shared", "L0004", 4), script.CurrentPosition);
        Assert.Equal(1, script.ReturnEntry);
        Assert.Equal(28, script.Caller);
        Assert.Equal(11, random.Draws);
        var settled = script.BattleRole;
        Assert.False(script.AcknowledgeBattle(request.Id, request.Lifecycle.Completion));
        Assert.Same(settled, script.BattleRole);
        script.Start("shared", "npc", 65535);
        Assert.Equal(28, script.Caller);
        Assert.Equal(0, script.BattleRole.Hp);
        Assert.Equal(1, script.PendingBattle!.Lifecycle.Current.Role.Hp);
        Assert.All(script.PendingBattle.Lifecycle.Current.Role.HiddenExperience.Values, xp => Assert.Equal(0, xp.Count));
        Assert.Equal(17, script.PendingBattle.Lifecycle.Current.Role.PrimaryExperience.Count);
    }

    [Fact]
    public void Multiple_battles_and_later_Start_use_settled_role_not_template_HP_and_reject_stale_receipts()
    {
        var fixture = Fixture();
        fixture.Commands[2] = Battle(2);
        fixture.Commands[3] = End(3);
        var random = new BindingRandom();
        var script = Script(fixture, random);
        script.Start("shared", "npc", 28);
        var first = script.PendingBattle!;
        Win(first);
        Assert.True(script.AcknowledgeBattle(first.Id, first.Lifecycle.Completion));
        var second = script.PendingBattle!;
        Assert.NotEqual(first.Id, second.Id);
        Assert.NotSame(first.Lifecycle, second.Lifecycle);
        Assert.Equal(125, second.Lifecycle.Current.Role.Hp);
        Assert.Equal(35, second.Lifecycle.Current.Role.Mp);
        Assert.Equal(0, second.Lifecycle.Current.Role.HiddenExperience["attack"].Count);
        Assert.Equal(1, script.BattleRole!.HiddenExperience["attack"].Count);
        Assert.Equal(new TriggerPosition("shared", "L0002", 2), script.CurrentPosition);
        AssertInert(script, random, () => script.AcknowledgeBattle(first.Id, first.Lifecycle.Completion));
        AssertInert(script, random, () => script.AcknowledgeBattle(second.Id, first.Lifecycle.Completion));
        Win(second);
        Assert.True(script.AcknowledgeBattle(second.Id, second.Lifecycle.Completion));
        Assert.Equal(137, script.BattleRole.Hp);
        Assert.Equal(42, script.BattleRole.Mp);
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(new TriggerPosition("shared", "L0003", 3), script.CurrentPosition);
        Assert.Equal(1, script.ReturnEntry);
        Assert.Equal(12, random.Draws);
        script.Start("shared", "npc", 65535);
        Assert.Equal(28, script.Caller);
        Assert.Equal(137, script.PendingBattle!.Lifecycle.Current.Role.Hp);
        AssertInert(script, random, () => script.AcknowledgeBattle(second.Id, second.Lifecycle.Completion));
        var independent = Script(fixture, new BindingRandom());
        independent.Start("shared", "npc", 28);
        Assert.Equal(100, independent.PendingBattle!.Lifecycle.Current.Role.Hp);
    }

    [Fact]
    public void Premature_null_wrong_ID_cross_lifecycle_cross_script_and_display_receipts_are_inert()
    {
        var random = new BindingRandom();
        var script = Script(Fixture(), random);
        var other = Script(Fixture(), new BindingRandom());
        AssertInert(script, random, () => script.AcknowledgeBattle(Guid.NewGuid(), null));
        script.Start("shared", "npc", 28);
        other.Start("shared", "npc", 28);
        var request = script.PendingBattle!;
        var foreign = other.PendingBattle!;
        AssertInert(script, random, () => script.AcknowledgeBattle(request.Id, request.Lifecycle.Completion));
        AssertInert(script, random, () => script.AcknowledgeDisplay(request.Id));
        var role = script.BattleRole;
        var context = script.Context;
        Assert.Throws<InvalidOperationException>(() => script.Start("missing", "missing", -1));
        Assert.Same(request, script.PendingBattle);
        Assert.Same(role, script.BattleRole);
        Assert.Equal(context, script.Context);
        Assert.Equal(0, random.Draws);
        Win(foreign);
        AssertInert(script, random, () => script.AcknowledgeBattle(request.Id, foreign.Lifecycle.Completion));
        Win(request);
        AssertInert(script, random, () => script.AcknowledgeBattle(Guid.NewGuid(), request.Lifecycle.Completion));
        AssertInert(script, random, () => script.AcknowledgeBattle(foreign.Id, request.Lifecycle.Completion));
        AssertInert(script, random, () => script.AcknowledgeBattle(request.Id, null));
        AssertInert(script, random, () => script.AcknowledgeBattle(request.Id, foreign.Lifecycle.Completion));
        Assert.True(script.AcknowledgeBattle(request.Id, request.Lifecycle.Completion));
        AssertInert(script, random, () => script.AcknowledgeBattle(request.Id, request.Lifecycle.Completion));
        var display = script.PendingDisplay!;
        Assert.Throws<InvalidOperationException>(() => script.Start("shared", "npc", 99));
        Assert.Same(display, script.PendingDisplay);
        FinishDisplay(script);
        AssertInert(script, random, () => script.AcknowledgeBattle(request.Id, request.Lifecycle.Completion));
        AssertInert(script, random, () => script.AcknowledgeDisplay(display.Id));
    }

    [Fact]
    public void Completed_display_receipt_cannot_cross_into_battle_or_consume_the_next_request()
    {
        var fixture = Fixture();
        fixture.Commands[1] = new EventFixture().Commands[2]!.DeepClone();
        fixture.Commands[1]!["label"] = "L0001";
        fixture.Commands[1]!["source"]!["index"] = 1;
        fixture.Commands[1]!["next"] = "L0002";
        fixture.Commands[2] = Battle(2);
        fixture.Commands[3] = End(3);
        var random = new BindingRandom();
        var script = new TriggerScriptSession(fixture.Load(), new DialogueLanguagePackage("en", [
            new DialogueMessageBlock(61, 61, [DialogueMessagePart.Clear])]), Content(), random);
        script.Start("shared", "npc", 28);
        var display = script.PendingDisplay!;
        Assert.Equal(DialogueDisplayPhase.Completed, display.Display.Current.Phase);
        Assert.Null(script.PendingBattle);
        AssertInert(script, random, () => script.AcknowledgeBattle(display.Id, null));
        Assert.True(script.AcknowledgeDisplay(display.Id));
        var battle = script.PendingBattle!;
        AssertInert(script, random, () => script.AcknowledgeDisplay(display.Id));
        AssertInert(script, random, () => script.AcknowledgeDisplay(battle.Id));
        Win(battle);
        AssertInert(script, random, () => script.AcknowledgeBattle(display.Id, battle.Lifecycle.Completion));
        Assert.True(script.AcknowledgeBattle(battle.Id, battle.Lifecycle.Completion));
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
    }

    [Theory]
    [InlineData("next", "raw")]
    [InlineData("next", "unsupported")]
    [InlineData("next", "language")]
    [InlineData("next", "team")]
    [InlineData("next", "enemy")]
    [InlineData("loss", "raw")]
    [InlineData("flee", "raw")]
    [InlineData("loss", "unsupported")]
    [InlineData("flee", "unsupported")]
    [InlineData("loss", "language")]
    [InlineData("flee", "language")]
    [InlineData("loss", "team")]
    [InlineData("flee", "team")]
    [InlineData("loss", "enemy")]
    [InlineData("flee", "enemy")]
    public void Every_encoded_branch_is_preflighted_even_without_flee_input_or_random_draws(string branch, string fault)
    {
        var fixture = Fixture();
        fixture.Commands[1] = Battle(1, loss: branch == "loss" ? "L0003" : null,
            boss: branch != "flee", flee: branch == "flee" ? "L0003" : null);
        fixture.Commands[2] = branch == "next" ? JsonNode.Parse("""{"label":"L0002","op":"setDialog","box":"upper","portraitId":0,"color":0,"rngWord":0,"next":"L0003","source":{"index":2,"opcode":60,"operands":[0,0,0]}}""") : End(2);
        fixture.Commands[3] = fault switch
        {
            "raw" => JsonNode.Parse("""{"label":"L0003","op":"raw","next":"L0004","source":{"index":3,"opcode":7,"operands":[18,0,0]}}"""),
            "unsupported" => JsonNode.Parse("""{"label":"L0003","op":"setDialog","box":"lower","portraitId":0,"color":0,"rngWord":0,"next":"L0004","source":{"index":3,"opcode":61,"operands":[0,0,0]}}"""),
            "language" => new EventFixture().Commands[3]!.DeepClone(),
            _ => Battle(3, team: 19)
        };
        var teams = new Dictionary<int, ClassicBattleEnemy> { [18] = new(7, 0, 43, 0, 0, 0, 1) };
        if (fault == "enemy") teams[19] = new(8, 0, 43, 0, 0, 0, 1) { Experience = 1 };
        var content = new TriggerBattleContent(Role(), new(1, 100, 150, 40, 20, 100), Growth(), teams);
        var language = new DialogueLanguagePackage("en", [new DialogueMessageBlock(62, 62, [DialogueMessagePart.FromText("~01A")])]);
        var random = new BindingRandom();
        var events = fixture.Load();
        var preflightError = Assert.Throws<InvalidDataException>(() => TriggerScriptSession.ValidateEntry(events, language, content, "shared", "npc"));
        Assert.Contains("shared.json#L0003 @0003", preflightError.Message);
        var script = new TriggerScriptSession(events, language, content, random);
        var role = script.BattleRole;
        var context = script.Context;
        var error = Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 28));
        Assert.Contains("shared.json#L0003 @0003", error.Message);
        Assert.Equal(preflightError.Message, error.Message);
        Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
        Assert.Equal(0, script.Caller);
        Assert.Equal(0, script.ReturnEntry);
        Assert.Null(script.CurrentPosition);
        Assert.Null(script.PendingBattle);
        Assert.Null(script.PendingDisplay);
        Assert.Same(role, script.BattleRole);
        Assert.Equal(55, script.BattleRole!.HiddenExperience["attack"].Count);
        Assert.Equal(context, script.Context);
        Assert.Equal(0, random.Draws);
    }

    [Theory]
    [InlineData("hp-domain")]
    [InlineData("threshold")]
    [InlineData("magic")]
    [InlineData("hidden-alias")]
    [InlineData("player-hook")]
    public void Unsupported_role_player_or_growth_is_rejected_before_context_or_role_effects(string fault)
    {
        var role = Role();
        var growth = Growth();
        var player = new ClassicBattlePlayer(1, 100, 150, 40, 20, 100);
        switch (fault)
        {
            case "hp-domain": role = role with { MaxHp = 32768 }; player = player with { MaxHp = 32768 }; break;
            case "threshold": growth = growth with { LevelUpExperience = Enumerable.Repeat(5, 100).ToArray() }; break;
            case "magic": growth = growth with { LevelUpMagics = [new(2, 11)] }; break;
            case "hidden-alias": role = role with { HiddenExperience = role.HiddenExperience.ToDictionary(x => x.Key == "attack" ? "ATTACK" : x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase) }; break;
            case "player-hook": player = player with { DyingScript = 6 }; break;
        }
        var content = new TriggerBattleContent(role, player, growth, new Dictionary<int, ClassicBattleEnemy> { [18] = new(7, 0, 43, 0, 0, 0, 1) });
        var random = new BindingRandom();
        var preflightError = Assert.Throws<InvalidDataException>(() => TriggerScriptSession.ValidateEntry(
            Fixture().Load(), TriggerScriptSessionTests.Language(), content, "shared", "npc"));
        Assert.Contains("shared.json#L0001 @0001", preflightError.Message);
        var script = Script(Fixture(), random, content);
        var initial = script.BattleRole;
        var context = script.Context;
        var error = Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 28));
        Assert.Contains("shared.json#L0001 @0001", error.Message);
        Assert.Equal(preflightError.Message, error.Message);
        Assert.Same(initial, script.BattleRole);
        Assert.Equal(context, script.Context);
        Assert.Null(script.PendingBattle);
        Assert.Null(script.PendingDisplay);
        Assert.Null(script.CurrentPosition);
        Assert.Equal(0, script.Caller);
        Assert.Equal(0, random.Draws);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cross_file_loss_to_another_battle_and_a_cycle_preflight_without_previewing_combat(bool missingTeam)
    {
        var fixture = Fixture();
        fixture.Index["files"] = new JsonArray("shared", "targets");
        var end = fixture.Commands[4]!.DeepClone();
        fixture.Commands.RemoveAt(4);
        fixture.Commands[1] = Battle(1, loss: "targets#L0004", boss: false, flee: "L0001");
        fixture.Commands[2] = End(2);
        fixture.Commands[3] = End(3);
        fixture.Commands[3]!["next"] = "targets#L0004";
        var nextBattle = Battle(4, team: missingTeam ? 19 : 18, loss: "shared#L0001");
        nextBattle["next"] = "L0005";
        end["label"] = "L0005";
        end["source"]!["index"] = 5;
        fixture.Files["targets"] = new JsonObject { ["formatVersion"] = 1, ["profile"] = "faithful", ["fileId"] = "targets",
            ["entries"] = new JsonArray(), ["commands"] = new JsonArray(nextBattle, end) };
        fixture.Index["source"]!["instructionCount"] = 6;
        fixture.Index["source"]!["byteLength"] = 48;
        var random = new BindingRandom();
        var content = Content(Role() with { Hp = 20 }, new(7, 0, 100, 0, 0, 0, 1));
        var script = Script(fixture, random, content);
        if (missingTeam)
        {
            var error = Assert.Throws<InvalidDataException>(() => TriggerScriptSession.ValidateEntry(
                fixture.Load(), TriggerScriptSessionTests.Language(), content, "shared", "npc"));
            Assert.Contains("targets.json#L0004 @0004: Missing battle team 19", error.Message);
            Assert.Equal(error.Message, Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 28)).Message);
            Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
            Assert.Null(script.PendingBattle);
            Assert.Null(script.CurrentPosition);
            Assert.Equal(0, random.Draws);
            return;
        }
        TriggerScriptSession.ValidateEntry(fixture.Load(), TriggerScriptSessionTests.Language(), content, "shared", "npc");
        script.Start("shared", "npc", 28);
        Assert.Equal(0, random.Draws);
        var first = script.PendingBattle!;
        Assert.False(first.IsBoss);
        Assert.Equal(1, first.FleeAddress);
        first.Lifecycle.SelectAttack(7);
        first.Lifecycle.ResolveNextAction();
        first.Lifecycle.ResolveNextAction();
        Assert.Equal(ClassicBattlePhase.Lost, first.Lifecycle.Completion!.Outcome);
        Assert.True(script.AcknowledgeBattle(first.Id, first.Lifecycle.Completion));
        Assert.Equal(new TriggerPosition("targets", "L0004", 4), script.CurrentPosition);
        Assert.Equal(1, script.PendingBattle!.Lifecycle.Current.Role.Hp);
        Assert.Equal(0, script.BattleRole!.Hp);
        Assert.Equal(1, script.ReturnEntry);
        Assert.Equal(28, script.Caller);
        Assert.Equal(11, random.Draws);
        AssertInert(script, random, () => script.AcknowledgeBattle(script.PendingBattle!.Id, first.Lifecycle.Completion));
    }

    [Fact]
    public void Content_deeply_copies_all_inputs_used_by_later_battles_and_exposes_readonly_role()
    {
        var role = Role();
        int[] xp = Enumerable.Repeat(100, 100).ToArray();
        BattleLevelUpMagic[] magics = [new(2, 9)];
        int[] enemyStatus = new int[9], enemyPoison = [0], playerStatus = new int[9], playerPoison = [0], equipment = new int[6];
        var teams = new Dictionary<int, ClassicBattleEnemy> { [18] = new(7, 0, 43, 0, 0, 0, 1) { Statuses = enemyStatus, Poisons = enemyPoison } };
        var content = new TriggerBattleContent(role, new(1, 100, 150, 40, 20, 100)
            { Equipment = equipment, Statuses = playerStatus, Poisons = playerPoison }, new(xp, magics), teams);
        ((int[])role.Equipment)[0] = 8;
        ((int[])role.Magics)[0] = 8;
        ((int[])role.Statuses)[0] = 8;
        ((Dictionary<string, BattleExperienceSnapshot>)role.HiddenExperience)["attack"] = new(100, 99, 500);
        xp[2] = 1;
        magics[0] = new(2, 11);
        enemyStatus[0] = enemyPoison[0] = playerStatus[0] = playerPoison[0] = equipment[0] = 8;
        teams.Clear();
        var fixture = Fixture();
        fixture.Commands[2] = Battle(2);
        fixture.Commands[3] = End(3);
        var script = Script(fixture, new BindingRandom(), content);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)script.BattleRole!.Magics)[0] = 77);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, BattleExperienceSnapshot>)script.BattleRole!.HiddenExperience)["health"] = new(0, 0, 0));
        var initial = script.BattleRole;
        script.Start("shared", "npc", 28);
        var first = script.PendingBattle!;
        Win(first);
        Assert.True(script.AcknowledgeBattle(first.Id, first.Lifecycle.Completion));
        var second = script.PendingBattle!;
        Win(second);
        Assert.True(script.AcknowledgeBattle(second.Id, second.Lifecycle.Completion));
        Assert.Equal(137, script.BattleRole!.Hp);
        Assert.Equal(9, script.BattleRole.Magics[0]);
        Assert.Equal(new BattleExperienceSnapshot(7, 3, 1), script.BattleRole.HiddenExperience["attack"]);
        Assert.Equal(100, initial!.Hp);
        Assert.Equal(55, initial.HiddenExperience["attack"].Count);
    }

    [Fact]
    public void Keep_end_excludes_archived_raw_and_later_battles_and_preserves_phase_numbers()
    {
        var fixture = Fixture();
        fixture.Commands[2] = End(2);
        fixture.Commands[3] = Battle(3, team: 999);
        TriggerScriptSession.ValidateEntry(fixture.Load(), TriggerScriptSessionTests.Language(), Content(), "shared", "npc");
        var script = Script(fixture, new BindingRandom());
        script.Start("shared", "npc", 28);
        Win(script.PendingBattle!);
        Assert.True(script.AcknowledgeBattle(script.PendingBattle!.Id, script.PendingBattle.Lifecycle.Completion));
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(0, (int)TriggerScriptPhase.Ready);
        Assert.Equal(1, (int)TriggerScriptPhase.WaitingForDisplay);
        Assert.Equal(2, (int)TriggerScriptPhase.Completed);
        Assert.Equal(3, (int)TriggerScriptPhase.WaitingForBattle);
    }

    [Theory]
    [InlineData("hp")]
    [InlineData("role")]
    [InlineData("maxhp")]
    public void Initial_player_snapshot_mismatch_is_not_silently_overwritten(string field)
    {
        var player = new ClassicBattlePlayer(1, 100, 150, 40, 20, 100);
        player = field switch { "hp" => player with { Hp = 99 }, "role" => player with { RoleId = 0 }, _ => player with { MaxHp = 151 } };
        Assert.Throws<InvalidDataException>(() => new TriggerBattleContent(Role(), player, Growth(), new Dictionary<int, ClassicBattleEnemy>()));
    }

    private static void AssertInert(TriggerScriptSession script, BindingRandom random, Func<bool> acknowledge)
    {
        var before = (script.Phase, script.Caller, script.ReturnEntry, script.CurrentPosition, script.Context,
            script.PendingDisplay, script.PendingBattle, script.BattleRole, random.Draws);
        Assert.False(acknowledge());
        Assert.Equal(before, (script.Phase, script.Caller, script.ReturnEntry, script.CurrentPosition, script.Context,
            script.PendingDisplay, script.PendingBattle, script.BattleRole, random.Draws));
    }
    private static void Win(TriggerBattleRequest request)
    {
        request.Lifecycle.SelectAttack(7);
        Assert.True(request.Lifecycle.ResolveNextAction());
        Assert.Equal(ClassicBattlePhase.Won, request.Lifecycle.Completion!.Outcome);
    }
    private static void FinishDisplay(TriggerScriptSession script)
    {
        var display = script.PendingDisplay!;
        display.Display.Update(24, false, true);
        display.Display.Update(100, true, false);
        Assert.True(script.AcknowledgeDisplay(display.Id));
    }
    private static JsonObject End(int address) => new()
    {
        ["label"] = $"L{address:X4}", ["op"] = "end", ["mode"] = "keep", ["next"] = $"L{address + 1:X4}",
        ["source"] = new JsonObject { ["index"] = address, ["opcode"] = 0, ["operands"] = new JsonArray(0, 0, 0) }
    };

    private static EventFixture Fixture()
    {
        var fixture = new EventFixture();
        fixture.Commands[1] = Battle(1);
        return fixture;
    }

    private static JsonObject Battle(int address, int team = 18, string? loss = "L0004", bool boss = true, string? flee = null) => new()
    {
        ["label"] = $"L{address:X4}", ["op"] = "startBattle", ["teamId"] = team, ["lossTarget"] = loss,
        ["isBoss"] = boss, ["fleeTarget"] = flee, ["next"] = $"L{address + 1:X4}",
        ["source"] = new JsonObject { ["index"] = address, ["opcode"] = 7, ["operands"] = new JsonArray(99, 3, 2) }
    };

    private static TriggerScriptSession Script(EventFixture fixture, BindingRandom random, TriggerBattleContent? content = null) =>
        new(fixture.Load(), TriggerScriptSessionTests.Language(), content ?? Content(), random);
    private static TriggerBattleContent Content(BattleRoleSnapshot? role = null, ClassicBattleEnemy? enemy = null,
        ClassicBattleGrowthData? growth = null)
    {
        role ??= Role();
        return new(role, new(1, role.Hp, role.MaxHp, 40, 20, 100), growth ?? Growth(),
            new Dictionary<int, ClassicBattleEnemy> { [18] = enemy ?? new(7, 0, 43, 0, 0, 0, 1) });
    }
    private static BattleRoleSnapshot Role() => new(1, 2, 100, 150, 20, 50, new(5, 2, 17),
        new Dictionary<string, BattleExperienceSnapshot> {
            ["health"] = new(7, 3, 55), ["magic"] = new(7, 3, 55), ["attack"] = new(7, 3, 55),
            ["magicPower"] = new(7, 3, 55), ["defense"] = new(7, 3, 55), ["dexterity"] = new(7, 3, 55), ["flee"] = new(7, 3, 55)
        }, new int[6], new[] { 9 }.Concat(new int[31]).ToArray(), new int[9], 0);
    private static ClassicBattleGrowthData Growth() => new(Enumerable.Repeat(100, 100).ToArray(), [new(2, 9), new(3, 10)]);

    // External random seam: unit multipliers, noncritical +1 damage and health weight 2.
    private sealed class BindingRandom : IBattleRandom
    {
        public int Draws { get; private set; }
        public int NextInclusive(int from, int to) { Draws++; return from == 0 && to == 5 ? 1 : from; }
        public float NextInclusive(float from, float to) { Draws++; return 1; }
    }
}
