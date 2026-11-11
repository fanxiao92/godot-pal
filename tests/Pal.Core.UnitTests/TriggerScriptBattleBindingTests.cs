using System.Text.Json.Nodes;
using Xunit;

namespace Pal.Core.Tests;

public sealed class TriggerScriptBattleBindingTests
{
    [Fact]
    public void Explicit_current_equipment_flows_into_next_trigger_battles_without_author_reseeding()
    {
        // global.c PAL_UpdateEquipments / battle.c:1754 consume current gear, not an old author seed.
        foreach (bool withWorld in new[] { false, true })
        foreach (bool declaredPanels in new[] { false, true })
        {
            var content = EquippedContent(declaredPanels);
            var equipment = BattleEquipmentState.Initialize(content);
            var owner = new RoleLiveState([content.InitialRole, Role() with { RoleId = 5, Equipment = [0, 0, 0, 171, 0, 0] }]);
            var historical = owner.Roles[1];
            var spectator = owner.Roles[5];
            var stock = new InventoryLiveState([new(171, 2, 9)]);
            var poisons = new PartyPoisonState([1], [Enumerable.Repeat(new PoisonSlot(0, 0), 16).ToArray()],
                new Dictionary<int, int>());
            Assert.True(equipment.TryRemoveItem(171, 3, false, stock, owner, poisons));
            Assert.Equal(new[] { 170, 0, 0, 0, 0, 0 }, owner.Roles[1].Equipment);
            var fixture = Fixture();
            fixture.Commands[2] = End(2);
            var random = new BindingRandom();
            var world = withWorld ? SharedWorld(owner) : null;
            var script = TriggerScriptSession.ForSharedRoles(fixture.Load(), TriggerScriptSessionTests.Language(),
                content, random, owner, world, inventoryLiveState: stock, equipmentState: equipment);
            var current = owner.Roles[1];
            var projection = script.BattleRoles!;
            Assert.Equal(new[] { 18 }, script.GetCurrentEntryRequirements("shared", "npc", 60).BattleTeamIds); // Designated RED.
            Assert.Same(equipment, script.EquipmentState);
            Assert.Same(stock, script.InventoryLiveState);
            Assert.Same(current, projection[1]);
            Assert.Same(current, owner.Roles[1]);
            Assert.Equal(Guid.Empty, script.CallId);
            Assert.Equal(0, random.Draws);
            Assert.Equal(new InventorySlot(171, 0, 9), stock.Slots[0]);
            Assert.Equal(10, equipment.AttackBonus(1));
            Assert.Equal(0, equipment.StatusOverlay(1)[8]);
            // Static/legacy checks still describe frozen content, and cannot silently bind current gear.
            Assert.Single(TriggerScriptSession.ValidateEntry(fixture.Load(), TriggerScriptSessionTests.Language(),
                content, "shared", "npc").BattleTeamIds);
            var legacy = TriggerScriptSession.ForSharedRoles(fixture.Load(), TriggerScriptSessionTests.Language(),
                content, new BindingRandom(), owner);
            Assert.Null(legacy.EquipmentState);
            Assert.Throws<InvalidDataException>(() => legacy.Start("shared", "npc", 60));
            script.Start("shared", "npc", 60);
            var first = script.PendingBattle!;
            var firstCall = script.CallId;
            Assert.Equal((declaredPanels ? 50 : 80, 24, 120),
                (first.Lifecycle.Current.Combat.Players[0].Attack, first.Lifecycle.Current.Combat.Players[0].Defense,
                    first.Lifecycle.Current.Combat.Players[0].Dexterity));
            Assert.Equal(new[] { 170, 0, 0, 0, 0, 0 }, first.Lifecycle.Current.Role.Equipment);
            Assert.Equal(new int[9], first.Lifecycle.Current.Role.Statuses);
            Assert.Equal(0, random.Draws);
            Assert.Same(current, owner.Roles[1]);
            Win(first);
            Assert.False(script.AcknowledgeBattle(Guid.NewGuid(), first.Lifecycle.Completion));
            Assert.Same(current, owner.Roles[1]);
            Assert.True(script.AcknowledgeBattle(first.Id, first.Lifecycle.Completion));
            Assert.False(script.AcknowledgeBattle(first.Id, first.Lifecycle.Completion));
            Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
            Assert.Equal((40, 20, 100), (owner.Roles[1].Attack, owner.Roles[1].Defense, owner.Roles[1].Dexterity));
            // Only after completion/ACK: remove the remaining head, then consume current values again.
            equipment.RemoveEquippedItem(1, 0, owner, poisons);
            var afterRemoval = owner.Roles[1];
            int draws = random.Draws;
            script.GetCurrentEntryRequirements("shared", "npc", 60);
            Assert.Equal(draws, random.Draws);
            Assert.Same(afterRemoval, projection[1]);
            script.Start("shared", "npc", 60);
            Assert.NotEqual(firstCall, script.CallId);
            var second = script.PendingBattle!;
            Assert.Equal((declaredPanels ? 40 : 70, 20, 100),
                (second.Lifecycle.Current.Combat.Players[0].Attack, second.Lifecycle.Current.Combat.Players[0].Defense,
                    second.Lifecycle.Current.Combat.Players[0].Dexterity));
            Assert.Equal(new int[6], second.Lifecycle.Current.Role.Equipment);
            Assert.Equal(new int[9], second.Lifecycle.Current.Role.Statuses);
            Assert.False(script.AcknowledgeBattle(second.Id, first.Lifecycle.Completion));
            Win(second);
            Assert.True(script.AcknowledgeBattle(second.Id, second.Lifecycle.Completion));
            Assert.False(script.AcknowledgeBattle(second.Id, second.Lifecycle.Completion));
            Assert.Equal(0, equipment.AttackBonus(1));
            Assert.Same(spectator, owner.Roles[5]);
            Assert.Equal(171, equipment.Equipment(5)[3]);
            Assert.Equal(171, historical.Equipment[3]);
            Assert.Equal(171, content.RoleEquipment[1][3]);
            Assert.Equal(170, content.InitialRole.Equipment[0]);
            Assert.Equal(0, stock.GetAmount(171));
            Assert.All(poisons.Slots(1), slot => Assert.Equal(new PoisonSlot(0, 0), slot));
            if (world is not null) Assert.Equal(owner.Roles[1].Hp, world.PartyVitals[1].Hp);
            var next = TriggerScriptSession.ForSharedRoles(fixture.Load(), TriggerScriptSessionTests.Language(),
                content, random, owner, world, equipmentState: equipment);
            next.Start("shared", "npc", 60);
            Assert.Equal(declaredPanels ? 40 : 70, next.PendingBattle!.Lifecycle.Current.Combat.Players[0].Attack);
            Assert.Equal(new int[6], next.PendingBattle.Lifecycle.Current.Role.Equipment);
        }

        // Selected current capability, not unselected/frozen equipment, governs acceptance.
        foreach (string unsupported in new[] { "attackAll", "cooperativeMagic", "sprite" })
        {
            var content = EquippedContent(true, unsupported);
            var equipment = BattleEquipmentState.Initialize(content);
            var owner = new RoleLiveState([content.InitialRole]);
            var random = new BindingRandom();
            var fixture = Fixture();
            fixture.Commands[2] = End(2);
            var script = TriggerScriptSession.ForSharedRoles(fixture.Load(), TriggerScriptSessionTests.Language(),
                content, random, owner, equipmentState: equipment);
            var before = owner.Roles[1];
            Assert.Throws<InvalidDataException>(() => script.GetCurrentEntryRequirements("shared", "npc", 60));
            Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 60));
            Assert.Same(before, owner.Roles[1]);
            Assert.Equal(0, random.Draws);
            Assert.Equal(Guid.Empty, script.CallId);
            Assert.Null(script.PendingBattle);
            var poisons = new PartyPoisonState([1], [Enumerable.Repeat(new PoisonSlot(0, 0), 16).ToArray()], new Dictionary<int, int>());
            equipment.RemoveEquippedItem(1, 0, owner, poisons);
            script.GetCurrentEntryRequirements("shared", "npc", 60); // Removed unsupported author gear must not resurrect.
            script.Start("shared", "npc", 60);
            Assert.Equal(60, script.PendingBattle!.Lifecycle.Current.Combat.Players[0].Attack); // Only hand +20 remains.
            Assert.Equal(60000, script.PendingBattle.Lifecycle.Current.Role.Statuses[8]);
        }

        var normal = EquippedContent(true);
        foreach (var owner in new[] { new RoleLiveState([]), new RoleLiveState([normal.InitialRole with { Equipment = new int[6] }]) })
        {
            var random = new BindingRandom();
            var equipment = BattleEquipmentState.Initialize(normal);
            var script = TriggerScriptSession.ForSharedRoles(Fixture().Load(), TriggerScriptSessionTests.Language(), normal,
                random, owner, equipmentState: equipment);
            Assert.Throws<InvalidDataException>(() => script.GetCurrentEntryRequirements("shared", "npc", 60));
            Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 60));
            Assert.Equal(0, random.Draws);
            Assert.Equal(Guid.Empty, script.CallId);
            Assert.Equal(30, equipment.AttackBonus(1));
        }
        var zeroContent = Content();
        var zero = BattleEquipmentState.Initialize(zeroContent);
        var zeroScript = TriggerScriptSession.ForSharedRoles(Fixture().Load(), TriggerScriptSessionTests.Language(), zeroContent,
            new BindingRandom(), new RoleLiveState([zeroContent.InitialRole]), equipmentState: zero);
        zeroScript.Start("shared", "npc", 60);
        Assert.False(zero.Declared);
        Assert.Equal(40, zeroScript.PendingBattle!.Lifecycle.Current.Combat.Players[0].Attack);
        var isolated = BattleEquipmentState.Initialize(normal);
        Assert.Equal(30, isolated.AttackBonus(1));
        Assert.Equal(60000, isolated.StatusOverlay(1)[8]);
        var remove = Fixture();
        remove.Commands[1] = new JsonObject { ["label"] = "L0001", ["op"] = "removeItem", ["itemId"] = 170,
            ["count"] = 1, ["shortageTarget"] = null, ["next"] = "L0002",
            ["source"] = new JsonObject { ["index"] = 1, ["opcode"] = 0x20, ["operands"] = new JsonArray(170, 1, 0) } };
        var stillUnsupported = TriggerScriptSession.ForSharedRoles(remove.Load(), TriggerScriptSessionTests.Language(), normal,
            new BindingRandom(), new RoleLiveState([normal.InitialRole]), inventoryLiveState: new InventoryLiveState([]), equipmentState: isolated);
        Assert.Contains("removeItem", Assert.Throws<InvalidDataException>(() => stillUnsupported.Start("shared", "npc", 60)).Message);
        Assert.Equal(30, isolated.AttackBonus(1));

        static TriggerBattleContent EquippedContent(bool declaredPanels, string? unsupported = null)
        {
            int[] worn = [170, 0, 0, 171, 0, 0];
            var role = Role() with { Equipment = worn, Attack = 40, MagicPower = 12, Defense = 20, Dexterity = 100, FleeRate = 6 };
            var grid = Enumerable.Range(0, 6).Select(id => (IReadOnlyList<int>)(id == 1 ? worn : id == 5 ? new[] { 0, 0, 0, 171, 0, 0 } : new int[6])).ToArray();
            var head = Enumerable.Range(0, 6).Select(id => id == 1
                ? new BattleEquipmentEffect(10, 4, 20, unsupported == "sprite" ? 9 : 0, new int[9], 3, 4, 5)
                    { AttackAll = unsupported == "attackAll" ? 2 : 0, CooperativeMagic = unsupported == "cooperativeMagic" ? 7 : 0 }
                : new BattleEquipmentEffect(0, 0, 0, 0, new int[9])).ToArray();
            var hand = Enumerable.Range(0, 6).Select(id => id == 1
                ? new BattleEquipmentEffect(20, 0, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0, 60000])
                : new BattleEquipmentEffect(0, 0, 0, 0, new int[9])).ToArray();
            return new TriggerBattleContent([role], [new(1, 100, 150, 70, 20, 100) { Equipment = worn }],
                new Dictionary<int, ClassicBattleGrowthData> { [1] = Growth() },
                new Dictionary<int, IReadOnlyList<ClassicBattleEnemy>> { [18] = new[] { new ClassicBattleEnemy(7, 0, 1, 0, 0, 0, 1) } },
                grid, [new(170, head), new(171, hand)], panelDeclaredRoles: declaredPanels ? [1] : null);
        }
    }

    [Fact]
    public void ValidateEntry_returns_selected_named_requirements_without_executing_battles()
    {
        var fixture = Fixture(); // EnemyTeamId=18 is edited; source operands deliberately still say 99.
        var requirements = TriggerScriptSession.ValidateEntry(fixture.Load(), TriggerScriptSessionTests.Language(), Content(), "shared", "npc");
        Assert.Equal(new[] { 18 }, requirements.BattleTeamIds);
        Assert.Empty(requirements.PortraitIds);
        var display = Assert.Single(requirements.DisplayBlocks);
        Assert.Equal(new TriggerPosition("shared", "L0002", 2), display.MessageReferences[0].Position);
        Assert.Equal(61, display.Content.StartMessageId);
        Assert.Equal(1, display.Content.Span);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)requirements.BattleTeamIds).Add(99));
        Assert.Throws<NotSupportedException>(() => ((IList<TriggerMessageReference>)display.MessageReferences).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<DialogueMessagePart>)display.Content.Parts).Clear());
        fixture.Commands.Clear();
        Assert.Equal(61, display.MessageReferences[0].StartMessageId);
    }

    [Fact]
    public void ValidateEntry_collects_named_portraits_and_immutable_display_ranges_not_retained_operands()
    {
        var fixture = new EventFixture();
        fixture.Commands[1]!["portraitId"] = 7;
        var requirements = TriggerScriptSession.ValidateEntry(fixture.Load(), TriggerScriptSessionTests.Language(), Content(), "shared", "npc");
        Assert.Equal(new[] { 7 }, requirements.PortraitIds);
        Assert.Empty(requirements.BattleTeamIds);
        Assert.Equal(new TriggerPosition("shared", "L0002", 2), Assert.Single(requirements.DisplayBlocks).MessageReferences[0].Position);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)requirements.PortraitIds)[0] = 59);
        fixture.Commands[1]!["portraitId"] = 8;
        Assert.Equal(7, requirements.PortraitIds[0]);
    }

    [Fact]
    public void ValidateEntry_loop_requirements_are_deduplicated_without_running_any_battle()
    {
        var fixture = Fixture();
        fixture.Commands[1] = Battle(1, boss: false, flee: "L0001");
        fixture.Commands[3] = End(3);
        var language = new DialogueLanguagePackage("en", [new DialogueMessageBlock(61, 61, [DialogueMessagePart.FromText("after battle")])]);
        var requirements = TriggerScriptSession.ValidateEntry(fixture.Load(), language, Content(), "shared", "npc");
        Assert.Equal(new[] { 18 }, requirements.BattleTeamIds);
        Assert.Single(requirements.DisplayBlocks);
        Assert.Equal("after battle", requirements.DisplayBlocks[0].Content.Parts[0].Text);
    }

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
        var requirements = TriggerScriptSession.ValidateEntry(fixture.Load(), TriggerScriptSessionTests.Language(), content, "shared", "npc");
        Assert.Empty(requirements.DisplayBlocks);
        Assert.Empty(requirements.PortraitIds);
        Assert.Empty(requirements.BattleTeamIds);
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
            var unsupported = Content(growth: new(new int[100], []));
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
        Assert.Equal(18, request.EnemyTeamId);
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
            "unsupported" => JsonNode.Parse("""{"label":"L0003","op":"setDialog","box":"upper","portraitId":0,"color":0,"rngWord":1,"next":"L0004","source":{"index":3,"opcode":60,"operands":[0,0,1]}}"""),
            "language" => new EventFixture().Commands[3]!.DeepClone(),
            _ => Battle(3, team: 19)
        };
        var teams = new Dictionary<int, ClassicBattleEnemy> { [18] = new(7, 0, 43, 0, 0, 0, 1) };
        if (fault == "enemy") teams[19] = new(8, 0, 43, 0, 0, 0, 1) { MagicId = 1 };
        var content = new TriggerBattleContent(Role(), new(1, 100, 150, 40, 20, 100), Growth(), teams);
        var language = new DialogueLanguagePackage("en", [new DialogueMessageBlock(62, 62, [DialogueMessagePart.FromText("$7A")])]);
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
            case "threshold": growth = growth with { LevelUpExperience = new int[100] }; break;
            case "magic": growth = growth with { LevelUpMagics = [new(65537, 11)] }; break;
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

    [Fact]
    public void Lost_failure_branch_runs_centered_dialogue_music_red_then_parks_at_reload()
    {
        var fixture = new EventFixture();
        fixture.Index["source"]!["instructionCount"] = 10;
        fixture.Index["source"]!["byteLength"] = 80;
        Pad(fixture, 10);
        fixture.Commands[1] = Battle(1); // Lost jumps to L0004; the won path stops at L0002.
        fixture.Commands[2] = End(2);
        fixture.Commands[3] = End(3);
        fixture.Commands[4] = JsonNode.Parse("""
          {"label":"L0004","op":"setDialog","box":"centerWindow","color":0,"next":"L0005","source":{"index":4,"opcode":62,"operands":[0,0,0]}}
          """);
        fixture.Commands[5] = JsonNode.Parse("""
          {"label":"L0005","op":"showDialog","messageId":61,"descriptionLineWord":0,"next":"L0006","source":{"index":5,"opcode":65535,"operands":[61,0,0]}}
          """);
        fixture.Commands[6] = JsonNode.Parse("""
          {"label":"L0006","op":"setMusic","musicId":0,"mode":0,"next":"L0007","source":{"index":6,"opcode":67,"operands":[0,0,0]}}
          """);
        fixture.Commands[7] = JsonNode.Parse("""
          {"label":"L0007","op":"fadeToRed","next":"L0008","source":{"index":7,"opcode":79,"operands":[0,0,0]}}
          """);
        fixture.Commands[8] = JsonNode.Parse("""
          {"label":"L0008","op":"requestReload","next":"L0009","source":{"index":8,"opcode":78,"operands":[0,0,0]}}
          """);
        var random = new BindingRandom();
        var language = new DialogueLanguagePackage("zh-Hans", [new DialogueMessageBlock(61, 61, [DialogueMessagePart.FromText("lost")])]);
        var script = new TriggerScriptSession(fixture.Load(), language,
            Content(Role() with { Hp = 20 }, new(7, 0, 100, 0, 0, 0, 1)), random);
        script.Start("shared", "npc", 28);
        var request = script.PendingBattle!;
        request.Lifecycle.SelectAttack(7);
        request.Lifecycle.ResolveNextAction();
        request.Lifecycle.ResolveNextAction();
        Assert.Equal(ClassicBattlePhase.Lost, request.Lifecycle.Completion!.Outcome);
        Assert.True(script.AcknowledgeBattle(request.Id, request.Lifecycle.Completion));
        // The failure branch re-enters through the centered dialogue context before the effects.
        Assert.Equal(TriggerScriptPhase.WaitingForDisplay, script.Phase);
        Assert.Equal("centerWindow", script.PendingDisplay!.Context.Box);
        Assert.Null(script.Music);
        Assert.False(script.FadedToRed);
        FinishDisplay(script);
        // setMusic and fadeToRed are pure formal transitions; 0x004E parks the call forever.
        Assert.Equal(new TriggerMusicState(0, 0), script.Music);
        Assert.True(script.FadedToRed);
        Assert.Equal(TriggerScriptPhase.WaitingForLostReload, script.Phase);
        Assert.True(script.BlocksSceneUpdates);
        Assert.Equal(new TriggerLostReloadRequest(new("shared", "L0008", 8), 18, true), script.PendingLostReload);
        Assert.Null(script.PendingDisplay);
        Assert.Equal(new TriggerPosition("shared", "L0008", 8), script.CurrentPosition);
        Assert.Equal(28, script.Caller);
        var parked = (script.Phase, script.PendingLostReload, script.Music, script.FadedToRed, script.CurrentPosition);
        Assert.False(script.AcknowledgeDisplay(Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => script.Start("shared", "npc", 28));
        Assert.Equal(parked, (script.Phase, script.PendingLostReload, script.Music, script.FadedToRed, script.CurrentPosition));
    }

    [Fact]
    public void Reload_without_a_lost_battle_carries_no_enemy_team_context()
    {
        var fixture = new EventFixture();
        fixture.Commands[1] = JsonNode.Parse("""
          {"label":"L0001","op":"requestReload","next":"L0002","source":{"index":1,"opcode":78,"operands":[0,0,0]}}
          """);
        var script = new TriggerScriptSession(fixture.Load(), TriggerScriptSessionTests.Language());
        script.Start("shared", "npc", 60);
        Assert.Equal(TriggerScriptPhase.WaitingForLostReload, script.Phase);
        Assert.Equal(new TriggerLostReloadRequest(new("shared", "L0001", 1), null, false), script.PendingLostReload);
    }

    [Fact]
    public void Preflight_stops_at_the_reload_boundary_and_never_walks_past_it()
    {
        var fixture = new EventFixture();
        fixture.Commands[1] = JsonNode.Parse("""
          {"label":"L0001","op":"requestReload","next":"L0002","source":{"index":1,"opcode":78,"operands":[0,0,0]}}
          """);
        // An unsupported op after the reload never executes (script.c 0x004E returns 0),
        // so the selected-entry preflight accepts the call without requiring it.
        fixture.Commands[2] = JsonNode.Parse("""
          {"label":"L0002","op":"addItem","itemId":5,"count":1,"next":"L0003","source":{"index":2,"opcode":31,"operands":[5,1,0]}}
          """);
        TriggerScriptSession.ValidateEntry(fixture.Load(), TriggerScriptSessionTests.Language(), Content(), "shared", "npc");
        var script = Script(fixture, new BindingRandom());
        script.Start("shared", "npc", 28);
        Assert.Equal(TriggerScriptPhase.WaitingForLostReload, script.Phase);
    }

    private static void Pad(EventFixture fixture, int count)
    {
        while (fixture.Commands.Count < count)
        {
            int index = fixture.Commands.Count;
            string label = "L" + index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
            // WORD order: once more commands exist, a trailing end's null next points at its successor.
            if (index > 0 && fixture.Commands[index - 1]!["next"] is null)
                fixture.Commands[index - 1]!["next"] = label;
            fixture.Commands.Add(JsonNode.Parse(
                "{\"label\":\"" + label + "\",\"op\":\"end\",\"mode\":\"keep\",\"next\":null,\"source\":{\"index\":" + index
                + ",\"opcode\":0,\"operands\":[0,0,0]}}"));
        }
    }

    [Fact]
    public void Battle_end_script_pauses_settlement_runs_with_enemy_slot_caller_then_completes()
    {
        var fixture = Fixture();
        fixture.Index["source"]!["instructionCount"] = 13;
        fixture.Index["source"]!["byteLength"] = 104;
        Pad(fixture, 13);
        fixture.Commands[8] = JsonNode.Parse("""
          {"label":"L0008","op":"setDialog","box":"lower","portraitId":0,"color":0,"rngWord":0,"next":"L0009","source":{"index":8,"opcode":61,"operands":[0,0,0]}}
          """);
        fixture.Commands[9] = JsonNode.Parse("""
          {"label":"L0009","op":"showDialog","messageId":61,"descriptionLineWord":0,"next":"L0010","source":{"index":9,"opcode":65535,"operands":[61,0,0]}}
          """);
        fixture.Commands[10] = JsonNode.Parse("""
          {"label":"L0010","op":"showDialog","messageId":62,"descriptionLineWord":0,"next":"L0011","source":{"index":10,"opcode":65535,"operands":[62,0,0]}}
          """);
        fixture.Commands[11] = JsonNode.Parse("""
          {"label":"L0011","op":"randomJump","rate":50,"to":"L0009","next":"L0012","source":{"index":11,"opcode":6,"operands":[50,9,0]}}
          """);
        fixture.Commands[12] = JsonNode.Parse("""
          {"label":"L0012","op":"end","mode":"keep","next":null,"source":{"index":12,"opcode":0,"operands":[0,0,0]}}
          """);
        var random = new BindingRandom();
        var script = Script(fixture, random, Content(enemy: new(7, 0, 43, 0, 0, 0, 1) { BattleEndScript = 8 }));
        Assert.Throws<InvalidOperationException>(script.StartBattleEndScript);
        script.Start("shared", "npc", 28);
        var request = script.PendingBattle!;
        request.Lifecycle.SelectAttack(7);
        Assert.True(request.Lifecycle.ResolveNextAction());
        Assert.Equal(ClassicBattlePhase.Won, request.Lifecycle.Current.Combat.Phase);
        // battle.c 1334-1337: the settlement pauses on the enemy's declared script before recovery.
        Assert.Null(request.Lifecycle.Completion);
        Assert.Equal(new ClassicBattleEndScript(0, 7, 8), request.Lifecycle.PendingBattleEndScript);
        Assert.NotNull(request.Lifecycle.VictorySettlement);
        int hpBeforeScript = request.Lifecycle.Current.Role.Hp;
        script.StartBattleEndScript();
        Assert.Equal(new ClassicBattleEndScript(0, 7, 8), script.RunningBattleEndScript);
        Assert.Equal(0, script.Caller); // the enemy slot is the run's caller
        Assert.Equal(TriggerScriptPhase.WaitingForDisplay, script.Phase);
        var display = script.PendingDisplay!;
        display.Display.Update(24, false, true);
        display.Display.Update(100, true, false);
        Assert.True(script.AcknowledgeDisplay(display.Id));
        // The finished script acknowledges the settlement: recovery completes and the receipt exists.
        Assert.Null(script.RunningBattleEndScript);
        Assert.Null(request.Lifecycle.PendingBattleEndScript);
        Assert.Equal(ClassicBattlePhase.Won, request.Lifecycle.Completion!.Outcome);
        Assert.Equal(hpBeforeScript + (150 - hpBeforeScript) / 2, request.Lifecycle.Current.Role.Hp);
        Assert.Equal(28, script.Caller);
        Assert.True(script.AcknowledgeBattle(request.Id, request.Lifecycle.Completion));
        FinishDisplay(script);
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
    }

    [Theory]
    [InlineData("fault")]
    [InlineData("changeHp")]
    [InlineData("startBattle")]
    public void Battle_end_script_closure_is_preflighted_and_rejects_before_any_side_effect(string fault)
    {
        var fixture = Fixture();
        fixture.Index["source"]!["instructionCount"] = 10;
        fixture.Index["source"]!["byteLength"] = 80;
        Pad(fixture, 10);
        fixture.Commands[8] = fault switch
        {
            "fault" => JsonNode.Parse("""
              {"label":"L0008","op":"raw","next":"L0009","source":{"index":8,"opcode":134,"operands":[0,65535,8]}}
              """),
            "changeHp" => JsonNode.Parse("""
              {"label":"L0008","op":"changeHp","scope":0,"amount":10,"next":"L0009","source":{"index":8,"opcode":27,"operands":[0,10,0]}}
              """),
            _ => Battle(8)
        };
        fixture.Commands[9] = JsonNode.Parse("""
          {"label":"L0009","op":"end","mode":"keep","next":null,"source":{"index":9,"opcode":0,"operands":[0,0,0]}}
          """);
        var random = new BindingRandom();
        var script = Script(fixture, random, Content(enemy: new(7, 0, 43, 0, 0, 0, 1) { BattleEndScript = 8 }));
        Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 28));
        Assert.Equal(0, random.Draws);
        Assert.Null(script.PendingBattle);
        Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
    }

    [Fact]
    public void Declared_role_panels_flow_into_the_next_battle_base_declaration()
    {
        var fixture = Fixture();
        fixture.Commands[2] = Battle(2);
        fixture.Commands[3] = End(3);
        var role = Role() with { Attack = 40, MagicPower = 12, Defense = 20, Dexterity = 100, FleeRate = 6 };
        var enemy = new ClassicBattleEnemy(7, 0, 43, 0, 0, 0, 1) { Experience = 120 };
        var declared = new TriggerBattleContent([role], [new(1, 100, 150, 40, 20, 100)],
            new Dictionary<int, ClassicBattleGrowthData> { [1] = Growth() },
            new Dictionary<int, IReadOnlyList<ClassicBattleEnemy>> { [18] = new[] { enemy } }, panelDeclaredRoles: [1]);
        var script = Script(fixture, new BindingRandom(), declared);
        script.Start("shared", "npc", 28);
        var first = script.PendingBattle!;
        Win(first);
        // PAL_PlayerLevelUp draw order with the binding draws: attack 40+4+0, defense 20+2+0,
        // dexterity 100+2+0, magicPower 12+4+0, flee 6+2; the settled panels are the next base.
        Assert.True(script.AcknowledgeBattle(first.Id, first.Lifecycle.Completion));
        var second = script.PendingBattle!;
        var settled = first.Lifecycle.Current.Role;
        var basePlayer = second.Lifecycle.Current.Combat.Players[0];
        Assert.Equal(settled.Attack, basePlayer.Attack);
        Assert.Equal(settled.Defense, basePlayer.Defense);
        Assert.Equal(settled.Dexterity, basePlayer.Dexterity);
        // Without the declaration the per-battle player panel stays the base (legacy behavior).
        var legacy = Script(fixture, new BindingRandom(), Content(role, enemy));
        legacy.Start("shared", "npc", 28);
        var firstLegacy = legacy.PendingBattle!;
        Win(firstLegacy);
        Assert.True(legacy.AcknowledgeBattle(firstLegacy.Id, firstLegacy.Lifecycle.Completion));
        var legacySecond = legacy.PendingBattle!.Lifecycle.Current.Combat.Players[0];
        Assert.Equal(40, legacySecond.Attack);
        Assert.Equal(20, legacySecond.Defense);
        Assert.Equal(100, legacySecond.Dexterity);
    }

    [Fact]
    public void Shared_world_event_vitals_feed_real_battle_and_owned_settlement_feeds_world_and_next_battle()
    {
        var owner = new RoleLiveState([Role()]);
        var world = SharedWorld(owner);
        var vitals = world.PartyVitals[1];
        var historical = owner.Roles[1];
        var fixture = Fixture();
        fixture.Commands[1] = ChangeHpMp(1, -20);
        fixture.Commands[2] = Battle(2);
        fixture.Commands[3] = Battle(3);
        var script = new TriggerScriptSession(fixture.Load(), TriggerScriptSessionTests.Language(), Content(), new BindingRandom(), world);
        var projection = script.BattleRoles!;
        script.Start("shared", "npc", 1);
        Assert.Equal(80, projection[1].Hp);
        var first = script.PendingBattle!;
        Assert.Equal((80, 0), (owner.Roles[1].Hp, owner.Roles[1].Mp));
        Assert.Equal((80, 0), (first.Lifecycle.Current.Role.Hp, first.Lifecycle.Current.Role.Mp));
        Assert.Equal(80, script.BattleRole!.Hp);
        Win(first);
        // Classic victory recovery: 80+(150-80)/2 = 115; 0+(50-0)/2 = 25.
        Assert.Equal((115, 25), (first.Lifecycle.Current.Role.Hp, first.Lifecycle.Current.Role.Mp));
        Assert.Equal((80, 0), (vitals.Hp, vitals.Mp)); // no raw combat/settlement publication before ACK
        Assert.True(script.AcknowledgeBattle(first.Id, first.Lifecycle.Completion));
        Assert.Equal((115, 25), (vitals.Hp, vitals.Mp));
        Assert.Equal((115, 25), (projection[1].Hp, projection[1].Mp));
        Assert.Equal(new BattleExperienceSnapshot(7, 3, 1), owner.Roles[1].HiddenExperience["attack"]);
        Assert.Equal(new BattleExperienceSnapshot(7, 3, 2), owner.Roles[1].HiddenExperience["health"]);
        Assert.Equal((100, 20), (historical.Hp, historical.Mp));
        var second = script.PendingBattle!;
        Assert.Equal((115, 25), (second.Lifecycle.Current.Role.Hp, second.Lifecycle.Current.Role.Mp));
        Assert.NotSame(first.Lifecycle, second.Lifecycle);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Explicit_shared_factory_preserves_owner_without_world_and_accepts_only_matching_world(bool withWorld)
    {
        var owner = new RoleLiveState([Role() with { Hp = 60, Mp = 4 }]);
        var script = TriggerScriptSession.ForSharedRoles(Fixture().Load(), TriggerScriptSessionTests.Language(), Content(),
            new BindingRandom(), owner, withWorld ? SharedWorld(owner) : null);
        Assert.Equal((60, 4), (script.BattleRole!.Hp, script.BattleRole.Mp));
        script.Start("shared", "npc", 1);
        var request = script.PendingBattle!;
        Assert.Equal((60, 4), (request.Lifecycle.Current.Role.Hp, request.Lifecycle.Current.Role.Mp));
        Win(request);
        Assert.True(script.AcknowledgeBattle(request.Id, request.Lifecycle.Completion));
        Assert.Equal((105, 27), (owner.Roles[1].Hp, owner.Roles[1].Mp));
        var foreignWorld = SharedWorld(new RoleLiveState([Role()]));
        Assert.Throws<ArgumentException>(() => TriggerScriptSession.ForSharedRoles(Fixture().Load(),
            TriggerScriptSessionTests.Language(), Content(), new BindingRandom(), owner, foreignWorld));
        Assert.Throws<ArgumentNullException>(() => TriggerScriptSession.ForSharedRoles(Fixture().Load(),
            TriggerScriptSessionTests.Language(), Content(), new BindingRandom(), null!));
        var lightweightWorld = DynamicEventWorld.ForHost(
            new StaticSceneRules(new SceneMap(2, new uint[16384], [new SceneFrame(0, 32, 15)]), []),
            new StaticLeader(320, 320, 0, 0, 0, 3,
                new SceneSprite(17, Enumerable.Range(0, 12).Select(i => new SceneFrame(i, 20, 30)))), 2, 0, 0);
        Assert.Throws<ArgumentException>(() => TriggerScriptSession.ForSharedRoles(Fixture().Load(),
            TriggerScriptSessionTests.Language(), Content(), new BindingRandom(), owner, lightweightWorld));
        // This old fifth-argument null call must still compile and select the world constructor.
        Assert.Throws<ArgumentNullException>(() => new TriggerScriptSession(Fixture().Load(),
            TriggerScriptSessionTests.Language(), Content(), new BindingRandom(), null!));
        Assert.Equal((105, 27), (owner.Roles[1].Hp, owner.Roles[1].Mp));
    }

    [Fact]
    public void Six_live_roles_project_two_in_content_order_and_merge_complete_growth_without_touching_spectators()
    {
        var panel = Role() with { Attack = 40, MagicPower = 12, Defense = 20, Dexterity = 100, FleeRate = 6 };
        var roles = Enumerable.Range(0, 6).Select(id => panel with { RoleId = id, Hp = 40000 + id, MaxHp = 60000,
            Equipment = new[] { id + 100, 0, 0, 0, 0, 0 }, Statuses = new[] { 7, 0, 0, 0, 0, 0, 0, 0, 0 } }).ToArray();
        roles[4] = panel with { RoleId = 4, Hp = 60, Mp = 4, Dexterity = 120 };
        roles[1] = panel with { Hp = 80, Mp = 8 };
        var owner = new RoleLiveState(roles);
        var spectators = new[] { 0, 2, 3, 5 }.ToDictionary(id => id, id => owner.Roles[id]);
        var content = new TriggerBattleContent([panel, panel with { RoleId = 4, Dexterity = 120 }],
            [new(4, 100, 150, 40, 20, 120), new(1, 100, 150, 40, 20, 100)],
            new Dictionary<int, ClassicBattleGrowthData> { [1] = Growth(), [4] = Growth() },
            new Dictionary<int, IReadOnlyList<ClassicBattleEnemy>> { [18] = new[] { new ClassicBattleEnemy(7, 0, 43, 0, 0, 0, 1) { Experience = 120 } } },
            panelDeclaredRoles: [1, 4]);
        var fixture = Fixture();
        fixture.Commands[2] = Battle(2);
        fixture.Commands[3] = End(3);
        var world = SharedWorld(owner);
        var script = TriggerScriptSession.ForSharedRoles(fixture.Load(), TriggerScriptSessionTests.Language(), content, new BindingRandom(), owner, world);
        Assert.Equal(new[] { 4, 1 }, script.BattleRoles!.Keys);
        Assert.Throws<InvalidOperationException>(() => script.BattleRole);
        script.Start("shared", "npc", 1);
        var first = script.PendingBattle!;
        Assert.Equal(new[] { 4, 1 }, first.Lifecycle.Current.Combat.Players.Select(player => player.RoleId));
        Assert.Equal(new[] { 60, 80 }, first.Lifecycle.Current.Combat.Players.Select(player => player.Hp));
        first.Lifecycle.SelectAttack(0, 0);
        first.Lifecycle.SelectAttack(1, 0);
        Assert.True(first.Lifecycle.ResolveNextAction());
        Assert.Equal(ClassicBattlePhase.Won, first.Lifecycle.Completion!.Outcome);
        Assert.Equal(60, owner.Roles[4].Hp);
        Assert.True(script.AcknowledgeBattle(first.Id, first.Lifecycle.Completion));
        // PAL_PlayerLevelUp: HP +10, MP +8+1 (binding draw), primary 5+120-100=25;
        // Hidden attack: 7+(120*1/3)*2=87; health 7+(120*2/3)*2=167 -> 67,
        // hidden health level4 and maxHP +1. No other hidden stat crosses a threshold.
        var grown = owner.Roles[4];
        Assert.Equal((3, 161, 161, 59, 59), (grown.Level, grown.Hp, grown.MaxHp, grown.Mp, grown.MaxMp));
        Assert.Equal(new BattleExperienceSnapshot(25, 3, 17), grown.PrimaryExperience);
        Assert.Equal(new BattleExperienceSnapshot(87, 3, 1), grown.HiddenExperience["attack"]);
        Assert.Equal(new BattleExperienceSnapshot(67, 4, 2), grown.HiddenExperience["health"]);
        Assert.Equal((44, 16, 22, 122, 8), (grown.Attack, grown.MagicPower, grown.Defense, grown.Dexterity, grown.FleeRate));
        Assert.Equal(new[] { 9, 10 }, grown.Magics.Take(2));
        Assert.Equal(new int[6], grown.Equipment);
        Assert.Equal(new int[9], grown.Statuses);
        Assert.Equal(0, grown.CoveredByRoleId);
        Assert.Equal((161, 59), (world.PartyVitals[4].Hp, world.PartyVitals[4].Mp));
        foreach (var (id, spectator) in spectators) Assert.Same(spectator, owner.Roles[id]);
        var second = script.PendingBattle!;
        Assert.Equal(new[] { 4, 1 }, second.Lifecycle.Current.Combat.Players.Select(player => player.RoleId));
        Assert.Equal((161, 59, 44, 122), (second.Lifecycle.Current.Roles[4].Hp, second.Lifecycle.Current.Roles[4].Mp,
            second.Lifecycle.Current.Combat.Players[0].Attack, second.Lifecycle.Current.Combat.Players[0].Dexterity));
        Assert.Equal(new[] { 9, 10 }, second.Lifecycle.Current.Roles[4].Magics.Take(2));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("maxHp")]
    [InlineData("equipment")]
    [InlineData("experience")]
    [InlineData("status-count")]
    [InlineData("timed-status")]
    [InlineData("magics-count")]
    [InlineData("magic-word")]
    [InlineData("hidden-alias")]
    [InlineData("growth-threshold")]
    [InlineData("hook-closure")]
    public void Shared_Start_preflight_rejects_participants_without_owner_request_or_random_side_effects(string fault)
    {
        var role = Role();
        if (fault == "maxHp") role = role with { MaxHp = 32768 };
        if (fault == "equipment") role = role with { Equipment = new[] { 8, 0, 0, 0, 0, 0 } };
        if (fault == "experience") role = role with { PrimaryExperience = new(-1, 2, 17) };
        if (fault == "status-count") role = role with { Statuses = new int[8] };
        if (fault == "timed-status") role = role with { Statuses = new[] { 0, 0, 0, 0, 0, 1, 0, 0, 0 } };
        if (fault == "magics-count") role = role with { Magics = new int[31] };
        if (fault == "magic-word") role = role with { Magics = new[] { 65536 }.Concat(new int[31]).ToArray() };
        if (fault == "hidden-alias") role = role with { HiddenExperience = role.HiddenExperience.ToDictionary(
            x => x.Key == "attack" ? "ATTACK" : x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase) };
        var owner = new RoleLiveState(fault == "missing" ? [] : new[] { role });
        var before = owner.Roles.ToArray();
        var random = new BindingRandom();
        var fixture = Fixture();
        fixture.Commands[1] = ChangeHpMp(1, -20); // must not execute before rejecting the reachable battle
        fixture.Commands[2] = Battle(2);
        fixture.Commands[3] = End(3);
        var content = fault == "growth-threshold" ? Content(growth: new(new int[100], [])) : Content();
        if (fault == "hook-closure") content = new TriggerBattleContent(Role(), content.Player with { DyingScript = 6 },
            Growth(), content.Teams);
        var world = SharedWorld(owner);
        var script = TriggerScriptSession.ForSharedRoles(fixture.Load(), TriggerScriptSessionTests.Language(), content, random, owner, world);
        var context = script.Context;
        Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 1, Guid.NewGuid()));
        Assert.Same(owner, world.RoleLiveState);
        Assert.Equal(context, script.Context);
        Assert.Null(script.PendingDisplay);
        Assert.Equal(0, script.Caller);
        Assert.Equal(0, script.ReturnEntry);
        Assert.Equal(before, owner.Roles.ToArray());
        Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
        Assert.Equal(Guid.Empty, script.CallId);
        Assert.Null(script.SourceRequestId);
        Assert.Null(script.StartedEntry);
        Assert.Null(script.CurrentPosition);
        Assert.Null(script.PendingBattle);
        Assert.Equal(0, random.Draws);
    }

    [Fact]
    public void Shared_owner_receipt_rejections_leave_all_records_and_script_unchanged_until_owned_ACK()
    {
        var owner = new RoleLiveState([Role(), Role() with { RoleId = 5, Hp = 60000, MaxHp = 65535 }]);
        var random = new BindingRandom();
        var fixture = Fixture();
        fixture.Commands[2] = Battle(2);
        fixture.Commands[3] = End(3);
        var script = TriggerScriptSession.ForSharedRoles(fixture.Load(), TriggerScriptSessionTests.Language(), Content(), random, owner);
        void Reject(Guid id, ClassicBattleLifecycleSession.CompletionReceipt? receipt)
        {
            var before = owner.Roles.ToArray();
            var state = (script.Phase, script.PendingBattle, script.CurrentPosition, random.Draws);
            Assert.False(script.AcknowledgeBattle(id, receipt));
            Assert.Equal(before, owner.Roles.ToArray());
            Assert.Equal(state, (script.Phase, script.PendingBattle, script.CurrentPosition, random.Draws));
        }
        Reject(Guid.NewGuid(), null);
        script.Start("shared", "npc", 1);
        var first = script.PendingBattle!;
        Reject(first.Id, first.Lifecycle.Completion); // genuinely unfinished lifecycle
        var foreign = Script(Fixture(), new BindingRandom());
        foreign.Start("shared", "npc", 1);
        Win(foreign.PendingBattle!);
        Reject(first.Id, foreign.PendingBattle!.Lifecycle.Completion);
        Win(first);
        Reject(Guid.NewGuid(), first.Lifecycle.Completion);
        Reject(first.Id, null);
        Reject(first.Id, foreign.PendingBattle.Lifecycle.Completion);
        Assert.Equal(100, owner.Roles[1].Hp);
        Assert.True(script.AcknowledgeBattle(first.Id, first.Lifecycle.Completion));
        Assert.Equal((125, 35), (owner.Roles[1].Hp, owner.Roles[1].Mp));
        Reject(first.Id, first.Lifecycle.Completion); // expired request
        Reject(script.PendingBattle!.Id, first.Lifecycle.Completion); // stale receipt, current ID
        var second = script.PendingBattle!;
        Win(second);
        Assert.True(script.AcknowledgeBattle(second.Id, second.Lifecycle.Completion));
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal((137, 42), (owner.Roles[1].Hp, owner.Roles[1].Mp));
        Assert.Equal(60000, owner.Roles[5].Hp);
        Reject(second.Id, second.Lifecycle.Completion); // duplicate after completion
    }

    [Fact]
    public void Shared_equipped_participant_commits_after_post_battle_script_and_rebuilds_effects_without_stacking()
    {
        int[] worn = [0, 0, 0, 170, 0, 0];
        int[] protect = [0, 0, 0, 0, 0, 0, 60000, 0, 0];
        var role = Role() with { Equipment = worn, Attack = 40, MagicPower = 12, Defense = 20, Dexterity = 100, FleeRate = 6 };
        var grid = Enumerable.Range(0, 6).Select(id => (IReadOnlyList<int>)(id == 1 ? worn : new int[6])).ToArray();
        var effects = Enumerable.Range(0, 6).Select(id => id == 1
            ? new BattleEquipmentEffect(10, 4, 20, 0, protect)
            : new BattleEquipmentEffect(0, 0, 0, 0, new int[9])).ToArray();
        var content = new TriggerBattleContent([role], [new(1, 100, 150, 40, 20, 100) { Equipment = worn }],
            new Dictionary<int, ClassicBattleGrowthData> { [1] = Growth() },
            new Dictionary<int, IReadOnlyList<ClassicBattleEnemy>> { [18] = new[] { new ClassicBattleEnemy(7, 0, 43, 0, 0, 0, 1) { BattleEndScript = 5 } } },
            grid, [new(170, effects)], panelDeclaredRoles: [1]);
        var owner = new RoleLiveState([role, Role() with { RoleId = 5 }]);
        var historical = owner.Roles[1];
        var spectator = owner.Roles[5];
        var world = SharedWorld(owner);
        var vitals = world.PartyVitals[1];
        var fixture = Fixture();
        fixture.Index["source"]!["instructionCount"] = 7;
        fixture.Index["source"]!["byteLength"] = 56;
        Pad(fixture, 7);
        fixture.Commands[2] = Battle(2);
        fixture.Commands[3] = End(3);
        fixture.Commands[5] = new JsonObject { ["label"] = "L0005", ["op"] = "showDialog", ["messageId"] = 61,
            ["descriptionLineWord"] = 0, ["next"] = "L0006", ["source"] = new JsonObject { ["index"] = 5, ["opcode"] = 65535, ["operands"] = new JsonArray(61, 0, 0) } };
        var language = new DialogueLanguagePackage("en", [new DialogueMessageBlock(61, 61, [DialogueMessagePart.FromText("settlement")])]);
        var script = TriggerScriptSession.ForSharedRoles(fixture.Load(), language, content, new BindingRandom(), owner, world);
        var projection = script.BattleRoles!;
        script.Start("shared", "npc", 1);
        var first = script.PendingBattle!;
        Assert.Equal((50, 24, 120), (first.Lifecycle.Current.Combat.Players[0].Attack, first.Lifecycle.Current.Combat.Players[0].Defense, first.Lifecycle.Current.Combat.Players[0].Dexterity));
        first.Lifecycle.SelectAttack(7);
        Assert.True(first.Lifecycle.ResolveNextAction());
        Assert.Equal(ClassicBattlePhase.Won, first.Lifecycle.Current.Combat.Phase);
        Assert.Null(first.Lifecycle.Completion);
        Assert.NotNull(first.Lifecycle.PendingBattleEndScript);
        Assert.Same(historical, owner.Roles[1]);
        Assert.False(script.AcknowledgeBattle(first.Id, first.Lifecycle.Completion));
        script.StartBattleEndScript();
        Assert.Equal(TriggerScriptPhase.WaitingForDisplay, script.Phase);
        Assert.Same(historical, owner.Roles[1]);
        Assert.False(script.AcknowledgeBattle(first.Id, null));
        FinishDisplay(script);
        Assert.Equal(ClassicBattlePhase.Won, first.Lifecycle.Completion!.Outcome);
        // Fixed classic recovery and cleanup: persistent protect survives, no panel bonus becomes a base.
        Assert.Equal((125, 35), (first.Lifecycle.Current.Role.Hp, first.Lifecycle.Current.Role.Mp));
        Assert.Equal(protect, first.Lifecycle.Current.Role.Statuses);
        Assert.Same(historical, owner.Roles[1]);
        Assert.Equal((100, 20), (vitals.Hp, vitals.Mp));
        Assert.False(script.AcknowledgeBattle(Guid.NewGuid(), first.Lifecycle.Completion));
        Assert.Same(historical, owner.Roles[1]);
        Assert.True(script.AcknowledgeBattle(first.Id, first.Lifecycle.Completion));
        var settled = owner.Roles[1];
        Assert.Same(settled, projection[1]);
        Assert.Equal((1, 2, 125, 150, 35, 50, 0), (settled.RoleId, settled.Level, settled.Hp, settled.MaxHp, settled.Mp, settled.MaxMp, settled.CoveredByRoleId));
        Assert.Equal((40, 12, 20, 100, 6), (settled.Attack, settled.MagicPower, settled.Defense, settled.Dexterity, settled.FleeRate));
        Assert.Equal(new BattleExperienceSnapshot(5, 2, 17), settled.PrimaryExperience);
        Assert.Equal(new BattleExperienceSnapshot(7, 3, 1), settled.HiddenExperience["attack"]);
        Assert.Equal(new BattleExperienceSnapshot(7, 3, 2), settled.HiddenExperience["health"]);
        Assert.All(settled.HiddenExperience.Where(x => x.Key is not ("attack" or "health")), x => Assert.Equal(new BattleExperienceSnapshot(7, 3, 0), x.Value));
        Assert.Equal(worn, settled.Equipment);
        Assert.Equal(new[] { 9 }.Concat(new int[31]), settled.Magics);
        Assert.Equal(protect, settled.Statuses);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)settled.Statuses)[6] = 0);
        Assert.Equal((125, 35), (vitals.Hp, vitals.Mp));
        Assert.Same(spectator, owner.Roles[5]);
        Assert.Equal(new int[9], historical.Statuses);
        var second = script.PendingBattle!;
        Assert.Equal((125, 35), (second.Lifecycle.Current.Role.Hp, second.Lifecycle.Current.Role.Mp));
        Assert.Equal(protect, second.Lifecycle.Current.Role.Statuses);
        Assert.Equal(worn, second.Lifecycle.Current.Role.Equipment);
        Assert.Equal((50, 24, 120), (second.Lifecycle.Current.Combat.Players[0].Attack, second.Lifecycle.Current.Combat.Players[0].Defense, second.Lifecycle.Current.Combat.Players[0].Dexterity));
        Assert.False(script.AcknowledgeBattle(second.Id, first.Lifecycle.Completion));
        Assert.Same(settled, owner.Roles[1]);
    }

    [Fact]
    public void Shared_Lost_commits_only_on_owned_receipt_before_world_revival_suffix()
    {
        var owner = new RoleLiveState([Role() with { Hp = 20 }, Role() with { RoleId = 5 }]);
        var initial = owner.Roles[1];
        var spectator = owner.Roles[5];
        var world = SharedWorld(owner);
        var vitals = world.PartyVitals[1];
        var fixture = Fixture();
        fixture.Commands[1] = Battle(1, loss: "L0002");
        fixture.Commands[2] = new JsonObject { ["label"] = "L0002", ["op"] = "revivePlayer", ["scope"] = 0,
            ["healthTenths"] = 5, ["next"] = "L0003", ["source"] = new JsonObject { ["index"] = 2, ["opcode"] = 0x22, ["operands"] = new JsonArray(0, 5, 0) } };
        fixture.Commands[3] = ChangeHpMp(3, -5);
        var random = new BindingRandom();
        var script = TriggerScriptSession.ForSharedRoles(fixture.Load(), TriggerScriptSessionTests.Language(),
            Content(enemy: new(7, 0, 100, 0, 0, 0, 1)), random, owner, world);
        script.Start("shared", "npc", 1);
        var request = script.PendingBattle!;
        request.Lifecycle.SelectAttack(7);
        Assert.True(request.Lifecycle.ResolveNextAction());
        Assert.Null(request.Lifecycle.Completion);
        Assert.Same(initial, owner.Roles[1]);
        Assert.False(script.AcknowledgeBattle(request.Id, null));
        Assert.True(request.Lifecycle.ResolveNextAction());
        Assert.Equal(ClassicBattlePhase.Lost, request.Lifecycle.Completion!.Outcome);
        Assert.Equal((0, 20), (request.Lifecycle.Current.Role.Hp, request.Lifecycle.Current.Role.Mp));
        Assert.Equal((20, 20), (vitals.Hp, vitals.Mp));
        Assert.Same(initial, owner.Roles[1]);
        Assert.False(script.AcknowledgeBattle(Guid.NewGuid(), request.Lifecycle.Completion));
        Assert.Same(initial, owner.Roles[1]);
        Assert.Equal(TriggerScriptPhase.WaitingForBattle, script.Phase);
        Assert.True(script.AcknowledgeBattle(request.Id, request.Lifecycle.Completion));
        // Lost must publish HP0 before the real suffix: revive 150*5/10=75, then -5 HP/MP.
        // If Lost never merged, revive would skip the old alive20 and the result would be15, not70.
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal((70, 15), (owner.Roles[1].Hp, owner.Roles[1].Mp));
        Assert.Equal((70, 15), (vitals.Hp, vitals.Mp));
        Assert.Equal((70, 15), (script.BattleRole!.Hp, script.BattleRole.Mp));
        Assert.Equal(new BattleExperienceSnapshot(5, 2, 17), owner.Roles[1].PrimaryExperience);
        Assert.Equal(new BattleExperienceSnapshot(7, 3, 1), owner.Roles[1].HiddenExperience["attack"]);
        Assert.Equal(new BattleExperienceSnapshot(7, 3, 2), owner.Roles[1].HiddenExperience["health"]);
        Assert.Equal(0, request.Lifecycle.Completion.ExperienceGranted);
        Assert.Equal(0, request.Lifecycle.Completion.CashGranted);
        Assert.Same(spectator, owner.Roles[5]);
        var settled = owner.Roles[1];
        int draws = random.Draws;
        Assert.False(script.AcknowledgeBattle(request.Id, request.Lifecycle.Completion));
        Assert.Same(settled, owner.Roles[1]);
        Assert.Equal(draws, random.Draws);
        Assert.Equal(20, initial.Hp);
    }

    [Fact]
    public void Shared_cash_victory_settles_once_before_enemy_scripts_and_owned_completion_never_regrants()
    {
        // battle.c:1054,1334-1337: cash precedes every enemy-end script, even with zero exp.
        var fixture = Fixture();
        fixture.Index["source"]!["instructionCount"] = 14;
        fixture.Index["source"]!["byteLength"] = 112;
        Pad(fixture, 14);
        for (int i = 5; i < 14; i++)
        {
            fixture.Commands[i]!["label"] = $"L{i:X4}";
            fixture.Commands[i]!["next"] = i == 13 ? null : $"L{i + 1:X4}";
        }
        fixture.Commands[2] = CashChange(2, -1);
        fixture.Commands[3] = Battle(3, team: 19);
        fixture.Commands[4] = End(4);
        fixture.Commands[8] = CashChange(8, -28);
        fixture.Commands[9] = JsonNode.Parse("""
          {"label":"L0009","op":"showDialog","messageId":61,"descriptionLineWord":0,"next":"L000A","source":{"index":9,"opcode":65535,"operands":[61,0,0]}}
          """);
        fixture.Commands[10] = End(10);
        fixture.Commands[11] = CashChange(11, 1);
        fixture.Commands[12] = End(12);
        var content = new TriggerBattleContent([Role()], [new(1, 100, 150, 40, 20, 100)],
            new Dictionary<int, ClassicBattleGrowthData> { [1] = Growth() },
            new Dictionary<int, IReadOnlyList<ClassicBattleEnemy>> {
                [18] = new[] { new ClassicBattleEnemy(7, 0, 1, 0, 0, 0, 1) { Cash = 7, BattleEndScript = 8 },
                    new ClassicBattleEnemy(8, 0, 1, 0, 0, 0, 1) { Cash = 11, BattleEndScript = 11 } },
                [19] = new[] { new ClassicBattleEnemy(7, 0, 43, 0, 0, 0, 1) { Cash = 5 } }
            });
        var cash = new CashLiveState(10);
        var random = new BindingRandom();
        var language = new DialogueLanguagePackage("en", [new DialogueMessageBlock(61, 61, [DialogueMessagePart.FromText("After victory")])]);
        var script = TriggerScriptSession.ForSharedRoles(fixture.Load(), language, content,
            random, new RoleLiveState([Role()]), cashLiveState: cash);
        Assert.Equal(new[] { 18, 19 }, script.GetCurrentEntryRequirements("shared", "npc", 28).BattleTeamIds);
        TriggerScriptSession.ValidateEntry(fixture.Load(), language, content,
            "shared", "npc", cashLiveState: cash);
        Assert.Equal(10u, cash.Cash);
        Assert.Equal(0, random.Draws);
        script.Start("shared", "npc", 28);
        var first = script.PendingBattle!;
        Assert.False(script.AcknowledgeBattle(first.Id, null));
        Assert.Equal(10u, cash.Cash);

        var isolatedCash = new CashLiveState(100);
        var foreign = new TriggerScriptSession(Fixture().Load(), TriggerScriptSessionTests.Language(),
            Content(enemy: new(7, 0, 43, 0, 0, 0, 1) { Cash = 7 }), new BindingRandom(), cashLiveState: isolatedCash);
        foreign.Start("shared", "npc", 28);
        var foreignRequest = foreign.PendingBattle!;
        Win(foreignRequest);
        Assert.False(script.AcknowledgeBattle(first.Id, foreignRequest.Lifecycle.Completion));
        Assert.Equal(10u, cash.Cash);

        first.Lifecycle.SelectAttack(0, 0);
        for (int i = 0; i < 8 && first.Lifecycle.Current.Combat.Phase == ClassicBattlePhase.Resolving; i++)
            Assert.True(first.Lifecycle.ResolveNextAction());
        Assert.Equal(ClassicBattlePhase.Selecting, first.Lifecycle.Current.Combat.Phase);
        Assert.Equal(10u, cash.Cash); // A partial KO is not a victory grant.
        first.Lifecycle.SelectAttack(0, 1);
        Assert.True(first.Lifecycle.ResolveNextAction());
        Assert.Equal(ClassicBattlePhase.Won, first.Lifecycle.Current.Combat.Phase);
        Assert.Equal(18, first.Lifecycle.VictorySettlement!.CashGranted);
        Assert.Equal(0, first.Lifecycle.VictorySettlement.ExperienceGranted);
        Assert.Null(first.Lifecycle.Completion);
        Assert.Equal(28u, cash.Cash); // Specified RED: actual10 before the production binding exists.
        Assert.Equal(107u, isolatedCash.Cash);
        Assert.False(first.Lifecycle.ResolveNextAction());
        Assert.Equal(28u, cash.Cash);
        Assert.False(script.AcknowledgeBattle(first.Id, null));
        script.StartBattleEndScript();
        Assert.Equal(0u, cash.Cash); // The first script consumes the entire seed + reward.
        Assert.Equal(TriggerScriptPhase.WaitingForDisplay, script.Phase);
        Assert.Throws<InvalidOperationException>(script.StartBattleEndScript);
        var display = script.PendingDisplay!;
        Assert.False(script.AcknowledgeDisplay(display.Id));
        Assert.False(script.AcknowledgeBattle(first.Id, foreignRequest.Lifecycle.Completion));
        FinishDisplay(script);
        Assert.False(script.AcknowledgeDisplay(display.Id));
        Assert.Equal(0u, cash.Cash);
        Assert.Equal(11, first.Lifecycle.PendingBattleEndScript!.ScriptAddress);
        script.StartBattleEndScript();
        Assert.Equal(1u, cash.Cash);
        var receipt = first.Lifecycle.Completion!;
        Assert.Equal(18, receipt.CashGranted); // Reward facts are not the post-script balance.
        Assert.False(script.AcknowledgeBattle(Guid.NewGuid(), receipt));
        Assert.False(script.AcknowledgeBattle(first.Id, foreignRequest.Lifecycle.Completion));
        Assert.Equal(1u, cash.Cash);
        Assert.True(script.AcknowledgeBattle(first.Id, receipt));
        Assert.True(script.ScriptSuccess);
        Assert.Equal(0u, cash.Cash); // The caller's debit then enters a new real battle.
        var second = script.PendingBattle!;
        Assert.NotEqual(first.Id, second.Id);
        Assert.False(script.AcknowledgeBattle(first.Id, receipt));
        Assert.False(script.AcknowledgeBattle(second.Id, receipt));
        Assert.Equal(0u, cash.Cash);
        Win(second);
        Assert.Equal(5u, cash.Cash);
        Assert.Equal(5, second.Lifecycle.Completion!.CashGranted);
        Assert.False(second.Lifecycle.ResolveNextAction());
        Assert.True(script.AcknowledgeBattle(second.Id, second.Lifecycle.Completion));
        Assert.False(script.AcknowledgeBattle(second.Id, second.Lifecycle.Completion));
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(5u, cash.Cash);
        Assert.Equal(107u, isolatedCash.Cash);

        // No-end-script wins preserve the full DWORD domain; no exp/UI gate or modern cap.
        foreach (var (initial, expected) in new (uint, uint)[] { (10, 17), (4294967292, 3) })
        {
            var owner = new CashLiveState(initial);
            var call = new TriggerScriptSession(Fixture().Load(), TriggerScriptSessionTests.Language(),
                Content(enemy: new(7, 0, 43, 0, 0, 0, 1) { Cash = 7 }), new BindingRandom(), cashLiveState: owner);
            call.Start("shared", "npc", 28);
            var request = call.PendingBattle!;
            Win(request);
            Assert.Equal(expected, owner.Cash);
            Assert.True(call.AcknowledgeBattle(request.Id, request.Lifecycle.Completion));
            Assert.False(call.AcknowledgeBattle(request.Id, request.Lifecycle.Completion));
            Assert.Equal(expected, owner.Cash);
        }
        var legacy = Script(Fixture(), new BindingRandom(), Content(enemy: new(7, 0, 43, 0, 0, 0, 1) { Cash = 7 }));
        legacy.Start("shared", "npc", 28);
        var legacyRequest = legacy.PendingBattle!;
        Win(legacyRequest);
        Assert.Null(legacy.CashLiveState);
        Assert.Equal(7, legacyRequest.Lifecycle.Completion!.CashGranted);
        Assert.True(legacy.AcknowledgeBattle(legacyRequest.Id, legacyRequest.Lifecycle.Completion));

        var lostCash = new CashLiveState(100);
        var lost = new TriggerScriptSession(Fixture().Load(), TriggerScriptSessionTests.Language(),
            Content(role: Role() with { Hp = 20 }, enemy: new(7, 0, 100, 0, 0, 0, 1) { Cash = 99 }),
            new BindingRandom(), cashLiveState: lostCash);
        lost.Start("shared", "npc", 28);
        var loss = lost.PendingBattle!;
        loss.Lifecycle.SelectAttack(7);
        Assert.True(loss.Lifecycle.ResolveNextAction());
        Assert.True(loss.Lifecycle.ResolveNextAction());
        Assert.Equal(ClassicBattlePhase.Lost, loss.Lifecycle.Completion!.Outcome);
        Assert.Null(loss.Lifecycle.VictorySettlement);
        Assert.Equal(0, loss.Lifecycle.Completion.CashGranted);
        Assert.Equal(100u, lostCash.Cash);
        Assert.True(lost.AcknowledgeBattle(loss.Id, loss.Lifecycle.Completion));
        Assert.False(lost.AcknowledgeBattle(loss.Id, loss.Lifecycle.Completion));
        Assert.Equal(100u, lostCash.Cash);

        // The complete enemy-end closure must be accepted before any gameplay/RNG mutation.
        fixture.Commands[8] = JsonNode.Parse("""
          {"label":"L0008","op":"raw","next":"L0009","source":{"index":8,"opcode":134,"operands":[0,65535,8]}}
          """);
        var preflightRandom = new BindingRandom();
        var rejected = new TriggerScriptSession(fixture.Load(), language, content,
            preflightRandom, cashLiveState: cash);
        Assert.Throws<InvalidDataException>(() => rejected.Start("shared", "npc", 28));
        Assert.Equal(Guid.Empty, rejected.CallId);
        Assert.Null(rejected.PendingBattle);
        Assert.Equal(0, preflightRandom.Draws);
        Assert.Equal(5u, cash.Cash);

        static JsonObject CashChange(int address, int amount) => new()
        {
            ["label"] = $"L{address:X4}", ["op"] = "changeCash", ["amount"] = amount, ["to"] = "L0000",
            ["next"] = $"L{address + 1:X4}",
            ["source"] = new JsonObject { ["index"] = address, ["opcode"] = 0x1e, ["operands"] = new JsonArray(123, 0, 0) }
        };
    }

    private static DynamicEventWorld SharedWorld(RoleLiveState owner) => DynamicEventWorld.ForHost(
        new StaticSceneRules(new SceneMap(2, new uint[16384], [new SceneFrame(0, 32, 15)]), []),
        new StaticLeader(320, 320, 0, 0, 0, 3,
            new SceneSprite(17, Enumerable.Range(0, 12).Select(i => new SceneFrame(i, 20, 30)))), 2, 0, 0, owner);

    private static JsonObject ChangeHpMp(int address, int amount) => new()
    {
        ["label"] = $"L{address:X4}", ["op"] = "changeHpMp", ["scope"] = 0, ["amount"] = amount,
        ["next"] = $"L{address + 1:X4}",
        ["source"] = new JsonObject { ["index"] = address, ["opcode"] = 0x1d, ["operands"] = new JsonArray(0, 0, 0) }
    };

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
        ["label"] = $"L{address:X4}", ["op"] = "startBattle", ["enemyTeamId"] = team, ["lossTarget"] = loss,
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
