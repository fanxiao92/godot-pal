using System.Text.Json.Nodes;
using Pal.Core;
using Xunit;

namespace Pal.Core.Tests;

public class TriggerEventCollectionTests
{
    [Fact]
    public void Complete_165_named_shells_preserve_all_content_and_supported_selected_entry_executes()
    {
        var fixture = new CompleteNamedEventFixture();
        Assert.Equal(165, fixture.Shells.Count);
        int[] supplied = fixture.Shells.Select(c => c!["source"]!["opcode"]!.GetValue<int>()).ToArray();
        Assert.Equal(165, supplied.Distinct().Count());
        Assert.Equal(CompleteNamedEventFixture.ExpectedOpcodes, supplied);
        var events = fixture.Load();
        Assert.Equal(170, events.InstructionCount);
        for (int address = 0; address < 170; address++)
        {
            string file = address < 165 ? "archive" : "selected";
            var expected = address < 165 ? fixture.Shells[address]! : fixture.Selected[address - 165]!;
            Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(events.GetCommandContent(address).GetRawText())));
            Assert.Equal(new TriggerPosition(file, $"L{address:X4}", address), events.GetSource(address).Position);
            Assert.Equal(expected["source"]!["opcode"]!.GetValue<int>(), events.GetSource(address).Opcode);
            Assert.Equal(new[] { 123, 456, 789 }, events.GetSource(address).Operands);
            Assert.NotEqual("raw", events.GetCommandContent(address).GetProperty("op").GetString());
        }
        Assert.Equal("selected#L00A7", events.GetCommandContent(4).GetProperty("to").GetString());
        Assert.Equal("trigger", events.GetEntry("selected", "itemUse").Kind);
        Assert.Equal("auto", events.GetEntry("selected", "itemDesc").Kind);
        var rules = new DynamicEventSessionTests.DynamicFixture().Rules();
        var world = DynamicEventWorld.ForHost(rules, DynamicEventSessionTests.DynamicFixture.Leader(), 1, 0, 0);
        var script = new TriggerScriptSession(events, TriggerScriptSessionTests.Language(), world);
        script.Start("selected", "itemUse", 5);
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(165, script.ReturnEntry);
        Assert.Equal(1, world.Objects.Single(o => o.Id == 6).State);
        Assert.Null(script.PendingDisplay);
        var autoRules = rules.WithObjects(rules.Objects.Select(o => o with
        {
            TriggerEntry = o.Id == 5 ? (ushort)165 : (ushort)0,
            AutoEntry = o.Id == 6 ? (ushort)168 : (ushort)0,
            State = o.Id == 6 ? (short)1 : o.State
        }));
        var auto = new DynamicEventSession(autoRules, DynamicEventSessionTests.DynamicFixture.Leader(), events,
            TriggerScriptSessionTests.Language(), events.GetEntry("selected", "npc"), 1, 0, 0);
        Assert.True(auto.Update(100));
        Assert.Equal(1, auto.World.Objects.Single(o => o.Id == 6).Direction);
        Assert.Equal(169, auto.World.Objects.Single(o => o.Id == 6).AutoEntry);
        Assert.Equal(DynamicEventPhase.Exploring, auto.Phase);
        Assert.Null(auto.PendingTrigger);
        // Input mutation cannot alter the retained independent semantic/source snapshot.
        fixture.Shells[4]!["eventObjectId"] = 0;
        Assert.Equal(65535, events.GetCommandContent(4).GetProperty("eventObjectId").GetInt32());
    }


    [Theory]
    [InlineData(0x0004, "call", 0)]
    [InlineData(0x0004, "call", 101)]
    [InlineData(0x001B, "changeHp", 0)]
    [InlineData(0x001B, "changeHp", 101)]
    public void Complete_named_random_closure_rejects_cross_file_content_or_wrong_context_before_any_effect(
        int targetOpcode, string verb, int rate)
    {
        var fixture = new CompleteNamedEventFixture();
        int target = Array.IndexOf(CompleteNamedEventFixture.ExpectedOpcodes, targetOpcode);
        fixture.Selected[1] = CompleteNamedEventFixture.Command(166, 6,
            $$"""{"op":"randomJump","rate":{{rate}},"to":"archive#L{{target:X4}}"}""");
        var events = fixture.Load();
        var role = new BattleRoleSnapshot(1, 2, 100, 150, 20, 50, new(5, 2, 17),
            new Dictionary<string, BattleExperienceSnapshot>(), new int[6], new int[32], new int[9], 0);
        var content = new TriggerBattleContent(role, new(1, 100, 150, 40, 20, 100), new([], []),
            new Dictionary<int, ClassicBattleEnemy>());
        var random = new CompleteClosureRandom(); // Draw1 would never take rate101, always take rate0.
        var world = DynamicEventWorld.ForHost(new DynamicEventSessionTests.DynamicFixture().Rules(),
            DynamicEventSessionTests.DynamicFixture.Leader(), 1, 11, 12);
        world.InitializePartyVitals(0, 80, 100, 20, 50);
        var before = WorldSnapshot(world);
        var entries = events.Entries.ToArray();
        var script = new TriggerScriptSession(events, TriggerScriptSessionTests.Language(), content, random, world);
        var context = script.Context;
        var roles = script.BattleRoles;
        var beforeRole = script.BattleRole;
        bool success = script.ScriptSuccess;
        var error = Assert.Throws<InvalidDataException>(() => script.Start("selected", "npc", 5, Guid.NewGuid()));
        Assert.Contains($"archive.json#L{target:X4} @{target:X4}", error.Message);
        Assert.Contains(verb, error.Message);
        Assert.Contains(targetOpcode == 4 ? "trigger context Event" : "battle role hook", error.Message);
        Assert.Equal(0, random.Draws);
        Assert.Equal(before, WorldSnapshot(world)); // Earlier setState must not execute, no rollback.
        Assert.Equal(entries, events.Entries);
        Assert.Same(roles, script.BattleRoles);
        Assert.Same(beforeRole, script.BattleRole);
        Assert.Equal(100, script.BattleRole!.Hp);
        Assert.Equal(20, script.BattleRole.Mp);
        Assert.Equal(context, script.Context);
        Assert.Equal(success, script.ScriptSuccess);
        Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
        Assert.Equal(Guid.Empty, script.CallId);
        Assert.Null(script.SourceRequestId);
        Assert.Null(script.StartedEntry);
        Assert.Null(script.CurrentPosition);
        Assert.Null(script.PendingDisplay);
        Assert.Null(script.PendingBattle);
        Assert.Null(script.PendingLostReload);
        Assert.Null(script.PendingMovement);
        Assert.Null(script.PendingTransition);
        Assert.Null(script.PendingSceneWait);
        Assert.Null(script.Music);
        Assert.False(script.FadedToRed);
        Assert.False(script.BlocksSceneUpdates);
        Assert.Equal(0, script.Caller);
        Assert.Equal(0, script.ReturnEntry);
    }

    [Theory]
    [InlineData(0x0004, "call", 0)]
    [InlineData(0x0004, "call", 1)]
    [InlineData(0x003C, "setDialog", 0)]
    [InlineData(0x003C, "setDialog", 1)]
    public void Complete_named_auto_closure_rejects_both_cross_file_branches_before_construction_or_updates(
        int targetOpcode, string verb, int limit)
    {
        var fixture = new CompleteNamedEventFixture();
        int target = Array.IndexOf(CompleteNamedEventFixture.ExpectedOpcodes, targetOpcode);
        fixture.Selected[1] = CompleteNamedEventFixture.Command(166, 3,
            $$"""{"op":"goto","to":"archive#L{{target:X4}}","limit":{{limit}}}""");
        var events = fixture.Load();
        var original = new DynamicEventSessionTests.DynamicFixture().Rules();
        var rules = original.WithObjects(original.Objects.Select(o => o with
        {
            TriggerEntry = o.Id == 5 ? (ushort)165 : (ushort)0,
            AutoEntry = o.Id == 6 ? (ushort)165 : (ushort)0
        }));
        var leader = DynamicEventSessionTests.DynamicFixture.Leader();
        var objects = rules.Objects.ToArray();
        var entries = events.Entries.ToArray();
        DynamicEventSession? session = null;
        // limit1 would fall through on the first auto execution; its target must still be checked.
        var error = Assert.Throws<InvalidDataException>(() => session = new DynamicEventSession(rules, leader,
            events, TriggerScriptSessionTests.Language(), events.GetEntry("selected", "npc"), 1, 11, 12));
        Assert.Contains($"archive.json#L{target:X4} @{target:X4}", error.Message);
        Assert.Contains(verb, error.Message);
        Assert.Contains("auto", error.Message);
        Assert.Null(session); // No public live world/session exists, no Update was called.
        Assert.Equal(objects, rules.Objects);
        Assert.Equal(entries, events.Entries);
        Assert.Equal(0, rules.Objects.Single(o => o.Id == 6).State);
        Assert.Equal(165, rules.Objects.Single(o => o.Id == 6).AutoEntry);
        Assert.Equal(165, rules.Objects.Single(o => o.Id == 5).TriggerEntry);
        Assert.Equal(320, leader.X);
        Assert.Equal(320, leader.Y);
    }

    private static string WorldSnapshot(DynamicEventWorld world) => System.Text.Json.JsonSerializer.Serialize(new
    {
        world.Objects, world.Leader, world.PartyRoles, world.Battlefield, world.PartyVitals, world.SceneScripts,
        ObjectScripts = world.ObjectScripts.ToArray()
    });

    private sealed class CompleteClosureRandom : IBattleRandom
    {
        internal int Draws { get; private set; }
        public int NextInclusive(int from, int to) { Draws++; return from; }
        public float NextInclusive(float from, float to) { Draws++; return from; }
    }

    // Literal modern fields from events-schema and fixed script.c, not DecodeCommand/profile output.
    [Theory]
    [InlineData(0x0008, "{\"op\":\"advanceEntry\"}")]
    [InlineData(0x000A, "{\"op\":\"confirmNoJump\",\"to\":\"shared#L0000\"}")]
    [InlineData(0x0011, "{\"op\":\"walkToSlow\",\"x\":65535,\"y\":0,\"h\":65535}")]
    [InlineData(0x0012, "{\"op\":\"setObjectPositionRelativeToParty\",\"eventObjectId\":65535,\"xOffset\":65408,\"yOffset\":56}")]
    [InlineData(0x0016, "{\"op\":\"setObjectFacing\",\"eventObjectId\":0,\"direction\":4,\"frame\":27}")]
    public void Early_named_content_retains_literal_fields_without_granting_execution(int opcode, string semanticJson)
        => AssertContentOnlyLiteral(opcode, semanticJson);

    [Theory]
    [InlineData(0x0017, "{\"op\":\"setExtraAttr\",\"equipPartWord\":17,\"field\":27,\"value\":-32768}")]
    [InlineData(0x0018, "{\"op\":\"equipItem\",\"equipPartWord\":11,\"itemId\":65535}")]
    [InlineData(0x0019, "{\"op\":\"incPlayerAttr\",\"field\":65535,\"delta\":32767,\"roleOperand\":0}")]
    [InlineData(0x001A, "{\"op\":\"setPlayerStat\",\"field\":0,\"value\":-1,\"roleOperand\":65535}")]
    [InlineData(0x001E, "{\"op\":\"changeCash\",\"amount\":-32768,\"to\":\"shared#L0000\"}")]
    public void Attribute_named_content_retains_signed_WORDs_and_reference_zero(int opcode, string semanticJson)
        => AssertContentOnlyLiteral(opcode, semanticJson);

    [Theory]
    [InlineData(-32769)]
    [InlineData(32768)]
    [InlineData("-1")]
    [InlineData(true)]
    [InlineData(null)]
    public void Content_signed_WORD_rejects_out_of_range_and_non_integer_values(object? value)
    {
        var fixture = new EventFixture();
        fixture.Commands[0] = JsonNode.Parse("""
            {"label":"L0000","op":"setExtraAttr","equipPartWord":11,"field":17,"value":0,"next":"L0001",
             "source":{"index":0,"opcode":23,"operands":[11,17,0]}}
            """);
        Assert.Equal(0, fixture.Load().GetCommandContent(0).GetProperty("value").GetInt32());
        fixture.Commands[0]!["value"] = System.Text.Json.JsonSerializer.SerializeToNode(value);
        var error = Assert.Throws<InvalidDataException>(() => fixture.Load());
        Assert.Contains("@0000", error.Message);
        Assert.Contains("value", error.Message);
    }

    [Theory]
    [InlineData(0x0021, "{\"op\":\"inflictDamageToEnemy\",\"scope\":65535,\"damage\":65535}")]
    [InlineData(0x0023, "{\"op\":\"removeEquipment\",\"playerRole\":0,\"partWord\":6}")]
    [InlineData(0x0026, "{\"op\":\"showBuyMenu\",\"storeId\":65535}")]
    [InlineData(0x0027, "{\"op\":\"showSellMenu\"}")]
    [InlineData(0x0028, "{\"op\":\"applyPoisonToEnemy\",\"scope\":0,\"objectId\":551}")]
    [InlineData(0x0029, "{\"op\":\"applyPoisonToPlayer\",\"scope\":65535,\"objectId\":564}")]
    [InlineData(0x002A, "{\"op\":\"curePoisonByObjectIdForEnemy\",\"scope\":1,\"objectId\":562}")]
    [InlineData(0x002B, "{\"op\":\"curePoisonByObjectIdForPlayer\",\"scope\":0,\"objectId\":0}")]
    [InlineData(0x002C, "{\"op\":\"curePoisonsByLevel\",\"scope\":1,\"maxLevel\":65535}")]
    [InlineData(0x002D, "{\"op\":\"setStatusPlayer\",\"status\":8,\"rounds\":65535}")]
    [InlineData(0x002E, "{\"op\":\"setStatusEnemy\",\"status\":0,\"rounds\":32760,\"to\":\"shared#L0000\"}")]
    [InlineData(0x002F, "{\"op\":\"clearPlayerStatus\",\"status\":65535}")]
    public void Status_and_menu_named_content_retains_unsigned_fields_and_no_operand_shapes(int opcode, string semanticJson)
        => AssertContentOnlyLiteral(opcode, semanticJson);

    [Theory]
    [InlineData(0x0031, "{\"op\":\"changeBattleSprite\",\"battleSprite\":65535}")]
    [InlineData(0x0033, "{\"op\":\"collectEnemyForItems\",\"to\":\"shared#L0000\"}")]
    [InlineData(0x0034, "{\"op\":\"transformCollectedEnemies\",\"to\":\"L0004\"}")]
    [InlineData(0x0035, "{\"op\":\"shakeScreen\",\"times\":65535,\"level\":0}")]
    [InlineData(0x0036, "{\"op\":\"setPlayingRng\",\"rngId\":0}")]
    [InlineData(0x0037, "{\"op\":\"playRng\",\"startFrame\":65535,\"endFrame\":0,\"speed\":0}")]
    [InlineData(0x0038, "{\"op\":\"teleportParty\",\"to\":\"L0000\"}")]
    [InlineData(0x0039, "{\"op\":\"drainHpFromEnemy\",\"amount\":65535}")]
    [InlineData(0x003A, "{\"op\":\"playerFlee\",\"to\":\"L0004\"}")]
    public void Battle_and_screen_named_content_retains_literal_fields_and_zero_defaults(int opcode, string semanticJson)
        => AssertContentOnlyLiteral(opcode, semanticJson);

    [Theory]
    [InlineData(0x003F, "{\"op\":\"rideEventObjectSlow\",\"x\":65535,\"y\":0,\"h\":65535}")]
    [InlineData(0x0042, "{\"op\":\"simulateMagic\",\"magicId\":65535,\"baseDamage\":0,\"enemyOperand\":-32768}")]
    [InlineData(0x0042, "{\"op\":\"simulateMagic\",\"magicId\":0,\"baseDamage\":65535,\"enemyOperand\":32767}")]
    [InlineData(0x0044, "{\"op\":\"rideEventObject\",\"x\":0,\"y\":65535,\"h\":65535}")]
    [InlineData(0x0045, "{\"op\":\"setBattleMusic\",\"battleMusicId\":65535}")]
    [InlineData(0x0047, "{\"op\":\"playSound\",\"soundId\":65535}")]
    [InlineData(0x004B, "{\"op\":\"nullifyObject\"}")]
    [InlineData(0x004C, "{\"op\":\"chasePlayer\",\"chaseRange\":0,\"speed\":0,\"floating\":65535}")]
    [InlineData(0x004D, "{\"op\":\"waitForAnyKey\"}")]
    [InlineData(0x0051, "{\"op\":\"fadeIn\",\"delay\":65535}")]
    [InlineData(0x0051, "{\"op\":\"fadeIn\",\"delay\":0}")]
    [InlineData(0x0053, "{\"op\":\"useDayPalette\"}")]
    [InlineData(0x0054, "{\"op\":\"useNightPalette\"}")]
    public void Ride_magic_and_palette_named_content_preserves_WORDs_without_runtime_clamps(int opcode, string semanticJson)
        => AssertContentOnlyLiteral(opcode, semanticJson);

    [Theory]
    [InlineData(0x0055, "{\"op\":\"addMagic\",\"magicId\":65535,\"roleOperand\":0}")]
    [InlineData(0x0056, "{\"op\":\"removeMagic\",\"magicId\":0,\"roleOperand\":65535}")]
    [InlineData(0x0057, "{\"op\":\"setMagicBaseDamageByMp\",\"magicId\":65535,\"mpMultiplier\":0}")]
    [InlineData(0x0058, "{\"op\":\"itemShortageJump\",\"itemId\":65535,\"count\":-32768,\"to\":\"shared#L0000\"}")]
    [InlineData(0x0058, "{\"op\":\"itemShortageJump\",\"itemId\":0,\"count\":32767,\"to\":\"L0004\"}")]
    [InlineData(0x0059, "{\"op\":\"changeScene\",\"sceneId\":65535}")]
    [InlineData(0x0059, "{\"op\":\"changeScene\",\"sceneId\":0}")]
    [InlineData(0x005A, "{\"op\":\"halveHp\"}")]
    [InlineData(0x005B, "{\"op\":\"halveEnemyHp\",\"maxDamage\":65535}")]
    [InlineData(0x005C, "{\"op\":\"setBattleHidingTime\",\"units\":65535}")]
    [InlineData(0x005D, "{\"op\":\"playerLacksPoisonJump\",\"objectId\":65535,\"to\":\"L0004\"}")]
    [InlineData(0x005E, "{\"op\":\"enemyLacksPoisonJump\",\"objectId\":0,\"to\":\"shared#L0000\"}")]
    [InlineData(0x005F, "{\"op\":\"killPlayer\"}")]
    [InlineData(0x0060, "{\"op\":\"killEnemy\"}")]
    public void Magic_inventory_and_health_named_content_retains_literal_fields_and_full_offline_domains(int opcode, string semanticJson)
        => AssertContentOnlyLiteral(opcode, semanticJson);

    [Theory]
    [InlineData(0x0061, "{\"op\":\"notPoisonedJump\",\"to\":\"shared#L0000\"}")]
    [InlineData(0x0062, "{\"op\":\"pauseEnemyChasing\",\"cycles\":65535}")]
    [InlineData(0x0063, "{\"op\":\"speedUpEnemyChasing\",\"cycles\":0}")]
    [InlineData(0x0064, "{\"op\":\"enemyHpAboveJump\",\"percent\":65535,\"to\":\"L0004\"}")]
    [InlineData(0x0065, "{\"op\":\"setPlayerSprite\",\"playerRole\":65535,\"spriteNum\":0,\"reload\":65535}")]
    [InlineData(0x0066, "{\"op\":\"throwWeapon\",\"magicId\":65535,\"damageUnits\":0}")]
    [InlineData(0x0067, "{\"op\":\"enemyUseMagic\",\"magicId\":65535,\"magicRate\":0}")]
    [InlineData(0x0068, "{\"op\":\"enemyTurnJump\",\"to\":\"shared#L0000\"}")]
    public void Chase_sprite_and_enemy_named_content_retains_literal_fields_without_execution(int opcode, string semanticJson)
        => AssertContentOnlyLiteral(opcode, semanticJson);

    [Theory]
    [InlineData(0x0042, "{\"op\":\"simulateMagic\",\"magicId\":0,\"baseDamage\":65535,\"enemyOperand\":0}", "enemyOperand")]
    [InlineData(0x0058, "{\"op\":\"itemShortageJump\",\"itemId\":65535,\"count\":0,\"to\":\"L0000\"}", "count")]
    public void U3_signed_fields_reject_values_outside_SHORT_even_with_valid_source(int opcode, string semantics, string field)
    {
        var fixture = new EventFixture();
        var command = JsonNode.Parse(semantics)!.AsObject();
        command["label"] = "L0000";
        command["next"] = "L0001";
        command["source"] = new JsonObject { ["index"] = 0, ["opcode"] = opcode, ["operands"] = new JsonArray(0, 0, 0) };
        fixture.Commands[0] = command;
        Assert.Equal(0, fixture.Load().GetCommandContent(0).GetProperty(field).GetInt32());
        foreach (string invalid in new[] { "-32769", "32768", "65535", "true", "null", "\"1\"", "1.5" })
        {
            command[field] = JsonNode.Parse(invalid);
            var error = Assert.Throws<InvalidDataException>(() => fixture.Load());
            Assert.Contains(field, error.Message);
            Assert.Contains("@0000", error.Message);
        }
    }

    [Theory]
    [InlineData(0x0058, "{\"op\":\"itemShortageJump\",\"itemId\":65535,\"count\":-1,\"to\":\"targets#L0004\"}")]
    [InlineData(0x005D, "{\"op\":\"playerLacksPoisonJump\",\"objectId\":65535,\"to\":\"targets#L0004\"}")]
    [InlineData(0x005E, "{\"op\":\"enemyLacksPoisonJump\",\"objectId\":0,\"to\":\"targets#L0004\"}")]
    [InlineData(0x0061, "{\"op\":\"notPoisonedJump\",\"to\":\"targets#L0004\"}")]
    [InlineData(0x0064, "{\"op\":\"enemyHpAboveJump\",\"percent\":65535,\"to\":\"targets#L0004\"}")]
    [InlineData(0x0068, "{\"op\":\"enemyTurnJump\",\"to\":\"targets#L0004\"}")]
    public void U3_content_references_are_checked_even_at_unselected_zero_and_remain_immutable(int opcode, string semantics)
    {
        var fixture = new EventFixture();
        fixture.Index["files"] = new JsonArray("shared", "targets");
        var last = fixture.Commands[4]!.DeepClone();
        fixture.Commands.RemoveAt(4);
        fixture.Commands[3]!["next"] = "targets#L0004";
        fixture.Files["targets"] = new JsonObject { ["formatVersion"] = 1, ["profile"] = "faithful", ["fileId"] = "targets",
            ["entries"] = new JsonArray(), ["commands"] = new JsonArray(last) };
        var command = JsonNode.Parse(semantics)!.AsObject();
        command["label"] = "L0000";
        command["next"] = "L0001";
        command["source"] = new JsonObject { ["index"] = 0, ["opcode"] = opcode, ["operands"] = new JsonArray(123, 456, 789) };
        fixture.Commands[0] = command;
        var collection = fixture.Load();
        foreach (string invalid in new[] { "\"targets#missing\"", "\"missing#L0004\"", "\"targets##L0004\"", "null", "0" })
        {
            command["to"] = JsonNode.Parse(invalid);
            var error = Assert.Throws<InvalidDataException>(() => fixture.Load());
            Assert.Contains("@0000", error.Message);
        }
        command["to"] = "shared#L0000";
        Assert.Equal("shared#L0000", fixture.Load().GetCommandContent(0).GetProperty("to").GetString());
        Assert.Equal("targets#L0004", collection.GetCommandContent(0).GetProperty("to").GetString());
        Assert.Equal(new[] { 123, 456, 789 }, collection.GetSource(0).Operands);
    }

    [Theory]
    [InlineData(0x0069, "{\"op\":\"enemyEscape\"}")]
    [InlineData(0x006A, "{\"op\":\"stealFromEnemy\",\"stealRate\":65535}")]
    [InlineData(0x006B, "{\"op\":\"blowAwayEnemies\",\"strength\":-32768}")]
    [InlineData(0x006C, "{\"op\":\"walkNpcStep\",\"eventObjectId\":65535,\"xOffset\":-32768,\"yOffset\":32767}")]
    [InlineData(0x006E, "{\"op\":\"moveParty\",\"xOffset\":32767,\"yOffset\":-32768,\"layerUnits\":65535}")]
    [InlineData(0x006F, "{\"op\":\"syncState\",\"eventObjectId\":0,\"state\":-32768}")]
    [InlineData(0x0070, "{\"op\":\"partyWalkToSlow\",\"x\":65535,\"y\":0,\"h\":65535}")]
    [InlineData(0x0071, "{\"op\":\"waveScreen\",\"level\":65535,\"progression\":32767}")]
    [InlineData(0x0074, "{\"op\":\"partyHpNotFullJump\",\"to\":\"shared#L0000\"}")]
    [InlineData(0x0076, "{\"op\":\"showFbp\",\"fbpId\":65535,\"fade\":0}")]
    public void U4_enemy_movement_and_screen_named_content_retains_literal_fields(int opcode, string semanticJson)
        => AssertContentOnlyLiteral(opcode, semanticJson);

    [Theory]
    [InlineData(0x0077, "{\"op\":\"stopMusic\",\"fadeUnits\":0}")]
    [InlineData(0x0079, "{\"op\":\"roleInPartyJump\",\"roleNameWord\":65535,\"to\":\"L0004\"}")]
    [InlineData(0x007A, "{\"op\":\"partyWalkTo\",\"x\":0,\"y\":65535,\"h\":65535}")]
    [InlineData(0x007B, "{\"op\":\"partyWalkToFast\",\"x\":65535,\"y\":0,\"h\":65535}")]
    [InlineData(0x007C, "{\"op\":\"walkToFast\",\"x\":65535,\"y\":65535,\"h\":65535}")]
    [InlineData(0x007D, "{\"op\":\"moveObject\",\"eventObjectId\":0,\"xOffset\":-32768,\"yOffset\":32767}")]
    [InlineData(0x007E, "{\"op\":\"setObjectLayer\",\"eventObjectId\":65535,\"layer\":-32768}")]
    [InlineData(0x007F, "{\"op\":\"moveViewport\",\"xOffset\":32767,\"yOffset\":-32768,\"frames\":65535}")]
    [InlineData(0x007F, "{\"op\":\"moveViewport\",\"xOffset\":0,\"yOffset\":0,\"frames\":0}")]
    [InlineData(0x0080, "{\"op\":\"toggleDayNightPalette\",\"paletteOnly\":65535}")]
    [InlineData(0x0081, "{\"op\":\"notFacingObjectJump\",\"eventObjectId\":65535,\"zoneUnits\":65535,\"to\":\"shared#L0000\"}")]
    public void U4_party_object_and_viewport_named_content_retains_full_offline_domains(int opcode, string semanticJson)
        => AssertContentOnlyLiteral(opcode, semanticJson);

    [Theory]
    [InlineData(0x0082, "{\"op\":\"walkToFastest\",\"x\":65535,\"y\":0,\"h\":65535}")]
    [InlineData(0x0083, "{\"op\":\"objectOutOfZoneJump\",\"eventObjectId\":0,\"zoneUnits\":65535,\"to\":\"L0004\"}")]
    [InlineData(0x0084, "{\"op\":\"placeUsedItem\",\"eventObjectId\":65535,\"state\":-32768,\"to\":\"shared#L0000\"}")]
    [InlineData(0x0085, "{\"op\":\"delay\",\"delayUnits\":65535}")]
    [InlineData(0x0086, "{\"op\":\"itemNotEquippedJump\",\"itemId\":65535,\"count\":0,\"to\":\"L0004\"}")]
    [InlineData(0x0086, "{\"op\":\"itemNotEquippedJump\",\"itemId\":0,\"count\":65535,\"to\":\"shared#L0000\"}")]
    [InlineData(0x0087, "{\"op\":\"animateObject\",\"eventObjectId\":0}")]
    [InlineData(0x0088, "{\"op\":\"setMagicBaseDamageByCash\",\"magicId\":65535}")]
    [InlineData(0x0089, "{\"op\":\"setBattleResult\",\"result\":65535}")]
    [InlineData(0x008A, "{\"op\":\"enableAutoBattle\"}")]
    public void U4_zone_item_and_battle_named_content_retains_literal_fields_without_execution(int opcode, string semanticJson)
        => AssertContentOnlyLiteral(opcode, semanticJson);

    [Theory]
    [InlineData(0x006B, "{\"op\":\"blowAwayEnemies\",\"strength\":0}", "strength")]
    [InlineData(0x006C, "{\"op\":\"walkNpcStep\",\"eventObjectId\":65535,\"xOffset\":0,\"yOffset\":0}", "xOffset,yOffset")]
    [InlineData(0x006E, "{\"op\":\"moveParty\",\"xOffset\":0,\"yOffset\":0,\"layerUnits\":65535}", "xOffset,yOffset")]
    [InlineData(0x006F, "{\"op\":\"syncState\",\"eventObjectId\":0,\"state\":0}", "state")]
    [InlineData(0x0071, "{\"op\":\"waveScreen\",\"level\":65535,\"progression\":0}", "progression")]
    [InlineData(0x007D, "{\"op\":\"moveObject\",\"eventObjectId\":0,\"xOffset\":0,\"yOffset\":0}", "xOffset,yOffset")]
    [InlineData(0x007E, "{\"op\":\"setObjectLayer\",\"eventObjectId\":65535,\"layer\":0}", "layer")]
    [InlineData(0x007F, "{\"op\":\"moveViewport\",\"xOffset\":0,\"yOffset\":0,\"frames\":65535}", "xOffset,yOffset")]
    [InlineData(0x0084, "{\"op\":\"placeUsedItem\",\"eventObjectId\":65535,\"state\":0,\"to\":\"L0000\"}", "state")]
    public void U4_signed_fields_retain_SHORT_boundaries_and_reject_unsigned_or_non_integer_values(int opcode, string semantics, string signedFields)
    {
        var fixture = new EventFixture();
        var command = JsonNode.Parse(semantics)!.AsObject();
        command["label"] = "L0000";
        command["next"] = "L0001";
        command["source"] = new JsonObject { ["index"] = 0, ["opcode"] = opcode, ["operands"] = new JsonArray(123, 456, 789) };
        fixture.Commands[0] = command;
        foreach (string field in signedFields.Split(','))
        {
            foreach (int valid in new[] { -32768, 0, 32767 })
            {
                command[field] = valid;
                Assert.Equal(valid, fixture.Load().GetCommandContent(0).GetProperty(field).GetInt32());
            }
            foreach (string invalid in new[] { "-32769", "32768", "65535", "true", "null", "\"1\"", "1.5" })
            {
                command[field] = JsonNode.Parse(invalid);
                var error = Assert.Throws<InvalidDataException>(() => fixture.Load());
                Assert.Contains(field, error.Message);
                Assert.Contains("@0000", error.Message);
            }
            command[field] = 0;
        }
    }

    [Theory]
    [InlineData(0x0074, "{\"op\":\"partyHpNotFullJump\",\"to\":\"targets#L0004\"}")]
    [InlineData(0x0079, "{\"op\":\"roleInPartyJump\",\"roleNameWord\":65535,\"to\":\"targets#L0004\"}")]
    [InlineData(0x0081, "{\"op\":\"notFacingObjectJump\",\"eventObjectId\":65535,\"zoneUnits\":65535,\"to\":\"targets#L0004\"}")]
    [InlineData(0x0083, "{\"op\":\"objectOutOfZoneJump\",\"eventObjectId\":0,\"zoneUnits\":0,\"to\":\"targets#L0004\"}")]
    [InlineData(0x0084, "{\"op\":\"placeUsedItem\",\"eventObjectId\":65535,\"state\":-32768,\"to\":\"targets#L0004\"}")]
    [InlineData(0x0086, "{\"op\":\"itemNotEquippedJump\",\"itemId\":65535,\"count\":0,\"to\":\"targets#L0004\"}")]
    public void U4_all_supplied_references_resolve_even_when_unselected_and_retain_immutable_content(int opcode, string semantics)
    {
        var fixture = new EventFixture();
        fixture.Index["files"] = new JsonArray("shared", "targets");
        var last = fixture.Commands[4]!.DeepClone();
        fixture.Commands.RemoveAt(4);
        fixture.Commands[3]!["next"] = "targets#L0004";
        fixture.Files["targets"] = new JsonObject { ["formatVersion"] = 1, ["profile"] = "faithful", ["fileId"] = "targets",
            ["entries"] = new JsonArray(), ["commands"] = new JsonArray(last) };
        var command = JsonNode.Parse(semantics)!.AsObject();
        command["label"] = "L0000";
        command["next"] = "L0001";
        command["source"] = new JsonObject { ["index"] = 0, ["opcode"] = opcode, ["operands"] = new JsonArray(123, 456, 789) };
        fixture.Commands[0] = command;
        var collection = fixture.Load();
        foreach (string invalid in new[] { "\"targets#missing\"", "\"missing#L0004\"", "\"targets##L0004\"", "null", "0" })
        {
            command["to"] = JsonNode.Parse(invalid);
            var error = Assert.Throws<InvalidDataException>(() => fixture.Load());
            Assert.Contains("@0000", error.Message);
        }
        command["to"] = "shared#L0000";
        Assert.Equal("shared#L0000", fixture.Load().GetCommandContent(0).GetProperty("to").GetString());
        Assert.Equal("targets#L0004", collection.GetCommandContent(0).GetProperty("to").GetString());
        Assert.Equal(new[] { 123, 456, 789 }, collection.GetSource(0).Operands);
    }

    [Theory]
    [InlineData(0x008B, "{\"op\":\"setPalette\",\"paletteId\":65535}")]
    [InlineData(0x008C, "{\"op\":\"colorFade\",\"color\":255,\"delay\":0,\"fromColor\":65535}")]
    [InlineData(0x008D, "{\"op\":\"increaseLevel\",\"levels\":65535}")]
    [InlineData(0x008E, "{\"op\":\"restoreScreen\"}")]
    [InlineData(0x008F, "{\"op\":\"halveCash\"}")]
    [InlineData(0x0091, "{\"op\":\"notFirstOfKindJump\",\"to\":\"shared#L0000\"}")]
    [InlineData(0x0092, "{\"op\":\"showMagicCastAnimation\",\"playerOperand\":65535}")]
    [InlineData(0x0093, "{\"op\":\"sceneFade\",\"step\":-32768}")]
    [InlineData(0x0094, "{\"op\":\"stateMatchJump\",\"eventObjectId\":65535,\"state\":32767,\"to\":\"L0004\"}")]
    public void U5_palette_level_and_conditional_content_retains_literal_shapes(int opcode, string semanticJson)
        => AssertContentOnlyLiteral(opcode, semanticJson);

    [Fact]
    public void U5_colorFade_lowByte_is_independent_of_source_and_fromColor_stays_a_truthy_WORD()
    {
        var fixture = new EventFixture();
        fixture.Commands[0] = JsonNode.Parse("""
            {"label":"L0000","op":"colorFade","color":26,"delay":0,"fromColor":65535,"next":"L0001",
             "source":{"index":0,"opcode":140,"operands":[65407,2,0]}}
            """);
        var command = fixture.Commands[0]!;
        var original = fixture.Load();
        Assert.Equal(26, original.GetCommandContent(0).GetProperty("color").GetInt32());
        Assert.Equal(65535, original.GetCommandContent(0).GetProperty("fromColor").GetInt32());
        Assert.Equal(new[] { 65407, 2, 0 }, original.GetSource(0).Operands);
        foreach (int valid in new[] { 0, 127, 255 })
        {
            command["color"] = valid;
            Assert.Equal(valid, fixture.Load().GetCommandContent(0).GetProperty("color").GetInt32());
        }
        foreach (string invalid in new[] { "-1", "256", "65535", "true", "null", "\"26\"", "1.5" })
        {
            command["color"] = JsonNode.Parse(invalid);
            var error = Assert.Throws<InvalidDataException>(() => fixture.Load());
            Assert.Contains("color", error.Message);
            Assert.Contains("@0000", error.Message);
        }
        command["color"] = 0;
        foreach (int valid in new[] { 0, 1, 65535 })
        {
            command["fromColor"] = valid;
            Assert.Equal(valid, fixture.Load().GetCommandContent(0).GetProperty("fromColor").GetInt32());
        }
        foreach (string invalid in new[] { "-1", "65536", "true", "null", "\"1\"", "1.5" })
        {
            command["fromColor"] = JsonNode.Parse(invalid);
            Assert.Contains("fromColor", Assert.Throws<InvalidDataException>(() => fixture.Load()).Message);
        }
        Assert.Equal(26, original.GetCommandContent(0).GetProperty("color").GetInt32());
    }

    [Theory]
    [InlineData(0x0095, "{\"op\":\"sceneMatchJump\",\"sceneId\":65535,\"to\":\"shared#L0000\"}")]
    [InlineData(0x0096, "{\"op\":\"showEndingAnimation\"}")]
    [InlineData(0x0097, "{\"op\":\"rideEventObjectFast\",\"x\":65535,\"y\":0,\"h\":65535}")]
    [InlineData(0x0098, "{\"op\":\"setFollower\",\"spriteNum0\":0,\"spriteNum1\":65535}")]
    [InlineData(0x0099, "{\"op\":\"changeSceneMap\",\"sceneId\":65535,\"mapNum\":65535}")]
    [InlineData(0x009B, "{\"op\":\"fadeToScene\"}")]
    [InlineData(0x009C, "{\"op\":\"enemyDivide\",\"copies\":0,\"failTarget\":null}")]
    [InlineData(0x009C, "{\"op\":\"enemyDivide\",\"copies\":65535,\"failTarget\":\"shared#L0004\"}")]
    [InlineData(0x009E, "{\"op\":\"enemySummon\",\"objectId\":65535,\"count\":-32768,\"failTarget\":null}")]
    [InlineData(0x009E, "{\"op\":\"enemySummon\",\"objectId\":0,\"count\":32767,\"failTarget\":\"L0004\"}")]
    [InlineData(0x009F, "{\"op\":\"enemyTransform\",\"objectId\":65535}")]
    public void U5_scene_follower_and_enemy_content_retains_full_offline_domains(int opcode, string semanticJson)
        => AssertContentOnlyLiteral(opcode, semanticJson);

    [Theory]
    [InlineData(0x00A0, "{\"op\":\"quitGame\"}")]
    [InlineData(0x00A1, "{\"op\":\"gatherParty\"}")]
    [InlineData(0x00A2, "{\"op\":\"randomBranch\",\"count\":65535}")]
    [InlineData(0x00A2, "{\"op\":\"randomBranch\",\"count\":0}")]
    [InlineData(0x00A3, "{\"op\":\"playCdMusic\",\"track\":-1,\"musicId\":65535}")]
    [InlineData(0x00A4, "{\"op\":\"scrollFbp\",\"fbpId\":65535,\"speed\":0}")]
    [InlineData(0x00A5, "{\"op\":\"showFbpWithEffectSprite\",\"fbpId\":0,\"effectSprite\":65535,\"fade\":65535}")]
    [InlineData(0x00A6, "{\"op\":\"backupScreen\"}")]
    [InlineData(0x00A7, "{\"op\":\"advanceEntry\"}")]
    public void U5_final_screen_music_and_control_content_preserves_literals_without_execution(int opcode, string semanticJson)
        => AssertContentOnlyLiteral(opcode, semanticJson);

    [Theory]
    [InlineData("{\"label\":\"L0001\",\"op\":\"advanceEntry\",\"next\":\"L0002\",\"source\":{\"index\":1,\"opcode\":8,\"operands\":[65535,17,123]}}", 8)]
    [InlineData("{\"label\":\"L0001\",\"op\":\"advanceEntry\",\"next\":\"L0002\",\"source\":{\"index\":1,\"opcode\":167,\"operands\":[65535,17,123]}}", 167)]
    public void U5_advanceEntry_dual_source_mappings_are_legal_content_but_neither_executor_is_enabled(string literal, int opcode)
    {
        var fixture = new EventFixture();
        fixture.Commands[1] = JsonNode.Parse(literal);
        var events = fixture.Load();
        Assert.Equal("advanceEntry", events.GetCommandContent(1).GetProperty("op").GetString());
        Assert.Equal(opcode, events.GetSource(1).Opcode);
        Assert.Equal(new[] { 65535, 17, 123 }, events.GetSource(1).Operands);
        var trigger = new TriggerScriptSession(events, TriggerScriptSessionTests.Language());
        var error = Assert.Throws<InvalidDataException>(() => trigger.Start("shared", "npc", 60));
        Assert.Contains("content-only command 'advanceEntry'", error.Message);
        Assert.Contains("trigger context", error.Message);
        Assert.Equal(TriggerScriptPhase.Ready, trigger.Phase);
        Assert.Null(trigger.CurrentPosition);

        var autoFixture = new DynamicEventSessionTests.DynamicFixture();
        var autoCommand = JsonNode.Parse(literal)!;
        autoCommand["label"] = "L0016";
        autoCommand["next"] = "L0017";
        autoCommand["source"]!["index"] = 16;
        autoFixture.Commands[16] = autoCommand;
        var autoEvents = autoFixture.Load();
        var rules = autoFixture.Rules();
        var autoError = Assert.Throws<InvalidDataException>(() => new DynamicEventSession(rules,
            DynamicEventSessionTests.DynamicFixture.Leader(), autoEvents, TriggerScriptSessionTests.Language(),
            autoEvents.GetEntry("scene", "npc"), 1, 0, 0));
        Assert.Contains("content-only command 'advanceEntry'", autoError.Message);
        Assert.Contains("auto context", autoError.Message);
        Assert.Contains("@0010", autoError.Message);
        Assert.Equal(352, rules.Objects.Single(o => o.Id == 6).X);
        Assert.Equal(15, rules.Objects.Single(o => o.Id == 6).AutoEntry);
    }

    [Theory]
    [InlineData(0x0093, "{\"op\":\"sceneFade\",\"step\":0}", "step")]
    [InlineData(0x0094, "{\"op\":\"stateMatchJump\",\"eventObjectId\":65535,\"state\":0,\"to\":\"L0000\"}", "state")]
    [InlineData(0x009E, "{\"op\":\"enemySummon\",\"objectId\":65535,\"count\":0,\"failTarget\":null}", "count")]
    [InlineData(0x00A3, "{\"op\":\"playCdMusic\",\"track\":-1,\"musicId\":65535}", "track")]
    public void U5_signed_fields_preserve_SHORT_boundaries_and_sentinels(int opcode, string semantics, string field)
    {
        var fixture = new EventFixture();
        var command = JsonNode.Parse(semantics)!;
        command["label"] = "L0000";
        command["next"] = "L0001";
        command["source"] = new JsonObject { ["index"] = 0, ["opcode"] = opcode, ["operands"] = new JsonArray(123, 456, 789) };
        fixture.Commands[0] = command;
        foreach (int valid in new[] { -32768, -1, 0, 32767 })
        {
            command[field] = valid;
            var events = fixture.Load();
            Assert.Equal(valid, events.GetCommandContent(0).GetProperty(field).GetInt32());
            Assert.Equal(new[] { 123, 456, 789 }, events.GetSource(0).Operands);
        }
        foreach (string invalid in new[] { "-32769", "32768", "65535", "true", "null", "\"1\"", "1.5" })
        {
            command[field] = JsonNode.Parse(invalid);
            var error = Assert.Throws<InvalidDataException>(() => fixture.Load());
            Assert.Contains(field, error.Message);
            Assert.Contains("@0000", error.Message);
        }
    }

    [Theory]
    [InlineData(0x0091, "{\"op\":\"notFirstOfKindJump\",\"to\":\"targets#L0004\"}", "to", false)]
    [InlineData(0x0094, "{\"op\":\"stateMatchJump\",\"eventObjectId\":65535,\"state\":-1,\"to\":\"targets#L0004\"}", "to", false)]
    [InlineData(0x0095, "{\"op\":\"sceneMatchJump\",\"sceneId\":65535,\"to\":\"targets#L0004\"}", "to", false)]
    [InlineData(0x009C, "{\"op\":\"enemyDivide\",\"copies\":0,\"failTarget\":\"targets#L0004\"}", "failTarget", true)]
    [InlineData(0x009E, "{\"op\":\"enemySummon\",\"objectId\":0,\"count\":-1,\"failTarget\":\"targets#L0004\"}", "failTarget", true)]
    public void U5_all_supplied_references_validate_at_unselected_zero_with_exact_nullable_rules(int opcode, string semantics, string field, bool nullable)
    {
        var fixture = new EventFixture();
        fixture.Index["files"] = new JsonArray("shared", "targets");
        var last = fixture.Commands[4]!.DeepClone();
        fixture.Commands.RemoveAt(4);
        fixture.Commands[3]!["next"] = "targets#L0004";
        fixture.Files["targets"] = new JsonObject { ["formatVersion"] = 1, ["profile"] = "faithful", ["fileId"] = "targets",
            ["entries"] = new JsonArray(), ["commands"] = new JsonArray(last) };
        var command = JsonNode.Parse(semantics)!;
        command["label"] = "L0000";
        command["next"] = "L0001";
        command["source"] = new JsonObject { ["index"] = 0, ["opcode"] = opcode, ["operands"] = new JsonArray(123, 456, 789) };
        fixture.Commands[0] = command;
        var original = fixture.Load();
        foreach (string invalid in new[] { "\"targets#missing\"", "\"missing#L0004\"", "\"targets##L0004\"", "0", "true" })
        {
            command[field] = JsonNode.Parse(invalid);
            Assert.Contains("@0000", Assert.Throws<InvalidDataException>(() => fixture.Load()).Message);
        }
        command[field] = "shared#L0000";
        if (nullable)
            Assert.Contains("address zero must use null", Assert.Throws<InvalidDataException>(() => fixture.Load()).Message);
        else
            Assert.Equal("shared#L0000", fixture.Load().GetCommandContent(0).GetProperty(field).GetString());
        command[field] = null;
        if (nullable)
            Assert.Equal(System.Text.Json.JsonValueKind.Null, fixture.Load().GetCommandContent(0).GetProperty(field).ValueKind);
        else
            Assert.Contains("must be a reference", Assert.Throws<InvalidDataException>(() => fixture.Load()).Message);
        Assert.Equal("targets#L0004", original.GetCommandContent(0).GetProperty(field).GetString());
        Assert.Equal(new[] { 123, 456, 789 }, original.GetSource(0).Operands);
    }

    [Theory]
    [InlineData(0x008B, "{\"op\":\"setPalette\",\"paletteId\":0}", "paletteId")]
    [InlineData(0x008C, "{\"op\":\"colorFade\",\"color\":26,\"delay\":0,\"fromColor\":0}", "delay,fromColor")]
    [InlineData(0x008D, "{\"op\":\"increaseLevel\",\"levels\":0}", "levels")]
    [InlineData(0x0092, "{\"op\":\"showMagicCastAnimation\",\"playerOperand\":0}", "playerOperand")]
    [InlineData(0x0094, "{\"op\":\"stateMatchJump\",\"eventObjectId\":0,\"state\":-1,\"to\":\"L0000\"}", "eventObjectId")]
    [InlineData(0x0095, "{\"op\":\"sceneMatchJump\",\"sceneId\":0,\"to\":\"L0000\"}", "sceneId")]
    [InlineData(0x0097, "{\"op\":\"rideEventObjectFast\",\"x\":0,\"y\":0,\"h\":0}", "x,y,h")]
    [InlineData(0x0098, "{\"op\":\"setFollower\",\"spriteNum0\":0,\"spriteNum1\":0}", "spriteNum0,spriteNum1")]
    [InlineData(0x0099, "{\"op\":\"changeSceneMap\",\"sceneId\":0,\"mapNum\":0}", "sceneId,mapNum")]
    [InlineData(0x009C, "{\"op\":\"enemyDivide\",\"copies\":0,\"failTarget\":null}", "copies")]
    [InlineData(0x009E, "{\"op\":\"enemySummon\",\"objectId\":0,\"count\":-1,\"failTarget\":null}", "objectId")]
    [InlineData(0x009F, "{\"op\":\"enemyTransform\",\"objectId\":0}", "objectId")]
    [InlineData(0x00A2, "{\"op\":\"randomBranch\",\"count\":0}", "count")]
    [InlineData(0x00A3, "{\"op\":\"playCdMusic\",\"track\":-1,\"musicId\":0}", "musicId")]
    [InlineData(0x00A4, "{\"op\":\"scrollFbp\",\"fbpId\":0,\"speed\":0}", "fbpId,speed")]
    [InlineData(0x00A5, "{\"op\":\"showFbpWithEffectSprite\",\"fbpId\":0,\"effectSprite\":0,\"fade\":0}", "fbpId,effectSprite,fade")]
    public void U5_WORD_fields_preserve_full_offline_boundaries_without_gameplay_clamps(int opcode, string semantics, string wordFields)
    {
        var fixture = new EventFixture();
        var command = JsonNode.Parse(semantics)!;
        command["label"] = "L0000";
        command["next"] = "L0001";
        command["source"] = new JsonObject { ["index"] = 0, ["opcode"] = opcode, ["operands"] = new JsonArray(123, 456, 789) };
        fixture.Commands[0] = command;
        foreach (string field in wordFields.Split(','))
        {
            foreach (int valid in new[] { 0, 32768, 65535 })
            {
                command[field] = valid;
                Assert.Equal(valid, fixture.Load().GetCommandContent(0).GetProperty(field).GetInt32());
            }
            foreach (string invalid in new[] { "-1", "65536", "true", "null", "\"1\"", "1.5" })
            {
                command[field] = JsonNode.Parse(invalid);
                var error = Assert.Throws<InvalidDataException>(() => fixture.Load());
                Assert.Contains(field, error.Message);
                Assert.Contains("@0000", error.Message);
            }
            command[field] = 0;
        }
    }

    private static void AssertContentOnlyLiteral(int opcode, string semanticJson)
    {
        var fixture = new EventFixture();
        var semantic = JsonNode.Parse(semanticJson)!.AsObject();
        var command = semantic.DeepClone().AsObject();
        command["label"] = "L0001";
        command["next"] = "L0002";
        command["source"] = new JsonObject { ["index"] = 1, ["opcode"] = opcode, ["operands"] = new JsonArray(123, 456, 789) };
        fixture.Commands[1] = command;
        var collection = fixture.Load();
        var retained = collection.GetCommandContent(1);
        foreach (var field in semantic)
            Assert.Equal(field.Value?.ToJsonString() ?? "null", retained.GetProperty(field.Key).GetRawText());
        Assert.Equal(new[] { 123, 456, 789 }, collection.GetSource(1).Operands);
        var script = new TriggerScriptSession(collection, TriggerScriptSessionTests.Language());
        var error = Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 60));
        Assert.Contains("content-only", error.Message);
        Assert.Contains(semantic["op"]!.GetValue<string>(), error.Message);
        Assert.Contains("@0001", error.Message);
        Assert.Contains("trigger context", error.Message);
        Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
        Assert.Null(script.CurrentPosition);

        // Reusable strict-shape regressions for every literal descriptor row.
        foreach (var field in semantic.Where(field => field.Key != "op"))
        {
            command.Remove(field.Key);
            Assert.Contains("missing field", Assert.Throws<InvalidDataException>(() => fixture.Load()).Message);
            command[field.Key] = field.Value?.DeepClone();
            command[field.Key] = true;
            Assert.Throws<InvalidDataException>(() => fixture.Load());
            command[field.Key] = field.Value?.DeepClone();
            if (field.Value is JsonValue value && value.TryGetValue<int>(out _))
            {
                command[field.Key] = 65536;
                Assert.Throws<InvalidDataException>(() => fixture.Load());
                command[field.Key] = -32769;
                Assert.Throws<InvalidDataException>(() => fixture.Load());
                command[field.Key] = field.Value.DeepClone();
            }
        }
        command["unexpected"] = 1;
        Assert.Contains("unknown field", Assert.Throws<InvalidDataException>(() => fixture.Load()).Message);
        command.Remove("unexpected");
        command["source"]!["opcode"] = 0x0000; // Registered end, never any content-only verb.
        Assert.Contains("mapping", Assert.Throws<InvalidDataException>(() => fixture.Load()).Message);
    }

    [Theory]
    [InlineData(0x000A, "confirmNoJump")]
    [InlineData(0x001E, "changeCash")]
    [InlineData(0x002E, "setStatusEnemy")]
    [InlineData(0x0033, "collectEnemyForItems")]
    [InlineData(0x0034, "transformCollectedEnemies")]
    [InlineData(0x0038, "teleportParty")]
    [InlineData(0x003A, "playerFlee")]
    public void Batch_content_references_resolve_across_files_even_when_unselected(int opcode, string op)
    {
        var fixture = new EventFixture();
        fixture.Index["files"] = new JsonArray("shared", "targets");
        var last = fixture.Commands[4]!.DeepClone();
        fixture.Commands.RemoveAt(4);
        fixture.Commands[3]!["next"] = "targets#L0004";
        fixture.Files["targets"] = new JsonObject { ["formatVersion"] = 1, ["profile"] = "faithful", ["fileId"] = "targets",
            ["entries"] = new JsonArray(), ["commands"] = new JsonArray(last) };
        var command = new JsonObject { ["label"] = "L0000", ["op"] = op, ["to"] = "targets#L0004", ["next"] = "L0001",
            ["source"] = new JsonObject { ["index"] = 0, ["opcode"] = opcode, ["operands"] = new JsonArray(0, 0, 0) } };
        if (op == "changeCash") command["amount"] = -1;
        if (op == "setStatusEnemy") { command["status"] = 3; command["rounds"] = 65535; }
        fixture.Commands[0] = command;
        Assert.Equal("targets#L0004", fixture.Load().GetCommandContent(0).GetProperty("to").GetString());
        command["to"] = "targets#missing";
        var error = Assert.Throws<InvalidDataException>(() => fixture.Load());
        Assert.Contains(op + ".to", error.Message);
        Assert.Contains("dangling", error.Message);
        command["to"] = null;
        Assert.Throws<InvalidDataException>(() => fixture.Load());
        command["to"] = "shared#L0000";
        Assert.Equal("shared#L0000", fixture.Load().GetCommandContent(0).GetProperty("to").GetString());
    }

    [Fact]
    public void Unselected_named_call_and_removeItem_load_without_reinterpreting_source()
    {
        var fixture = new EventFixture();
        fixture.Commands[0] = JsonNode.Parse("""
            {"label":"L0000","op":"call","to":"shared#L0004","eventObjectId":65535,"next":"L0001",
             "source":{"index":0,"opcode":4,"operands":[2,3,4]}}
            """);
        fixture.Commands[4] = JsonNode.Parse("""
            {"label":"L0004","op":"removeItem","itemId":76,"count":65535,"shortageTarget":"shared#L0001","next":null,
             "source":{"index":4,"opcode":32,"operands":[99,0,0]}}
            """);
        // End the selected supported path before the archival command.
        fixture.Commands[3] = JsonNode.Parse("""
            {"label":"L0003","op":"end","mode":"keep","next":"L0004","source":{"index":3,"opcode":0,"operands":[0,0,0]}}
            """);
        var collection = fixture.Load();
        Assert.Equal(5, collection.InstructionCount);
        Assert.Equal(new[] { 99, 0, 0 }, collection.GetSource(4).Operands);
        var language = new DialogueLanguagePackage("en", [new DialogueMessageBlock(61, 61, [DialogueMessagePart.FromText("A")])]);
        var script = new TriggerScriptSession(collection, language);
        script.Start("shared", "npc", 60);
        var display = script.PendingDisplay!;
        display.Display.Update(24, false, true);
        display.Display.Update(100, true, false);
        Assert.True(script.AcknowledgeDisplay(display.Id));
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
    }

    [Fact]
    public void Unselected_named_references_are_validated_across_files_including_nullable_zero_rules()
    {
        var fixture = new EventFixture();
        fixture.Index["files"] = new JsonArray("shared", "targets");
        var last = fixture.Commands[4]!.DeepClone();
        fixture.Commands.RemoveAt(4);
        fixture.Commands[3]!["next"] = "targets#L0004";
        fixture.Files["targets"] = new JsonObject { ["formatVersion"] = 1, ["profile"] = "faithful", ["fileId"] = "targets",
            ["entries"] = new JsonArray(), ["commands"] = new JsonArray(last) };
        fixture.Commands[0] = JsonNode.Parse("""
            {"label":"L0000","op":"removeItem","itemId":76,"count":0,"shortageTarget":"targets#L0004","next":"L0001",
             "source":{"index":0,"opcode":32,"operands":[76,0,0]}}
            """);
        Assert.Equal(5, fixture.Load().InstructionCount);
        fixture.Commands[0]!["shortageTarget"] = "targets#missing";
        var error = Assert.Throws<InvalidDataException>(() => fixture.Load());
        Assert.Contains("shared.json#L0000 @0000", error.Message);
        Assert.Contains("removeItem.shortageTarget", error.Message);
        Assert.Contains("dangling", error.Message);
        fixture.Commands[0]!["shortageTarget"] = "shared#L0000";
        Assert.Contains("address zero must use null", Assert.Throws<InvalidDataException>(() => fixture.Load()).Message);
    }

    [Theory]
    [InlineData("unknown-op")]
    [InlineData("mapping")]
    [InlineData("unknown-field")]
    [InlineData("missing-field")]
    [InlineData("word-type")]
    [InlineData("word-negative")]
    [InlineData("word-overflow")]
    [InlineData("reference-null")]
    [InlineData("reference-malformed")]
    [InlineData("reference-dangling")]
    public void Unselected_named_call_requires_exact_mapping_shape_and_complete_references(string fault)
    {
        var fixture = new EventFixture();
        fixture.Commands[0] = JsonNode.Parse("""
            {"label":"L0000","op":"call","to":"L0000","eventObjectId":65535,"next":"L0001",
             "source":{"index":0,"opcode":4,"operands":[2,3,4]}}
            """);
        Assert.Equal(5, fixture.Load().InstructionCount); // Ordinary references may address zero.
        var command = fixture.Commands[0]!.AsObject();
        switch (fault)
        {
            case "unknown-op": command["op"] = "unknownNamed"; break;
            case "mapping": command["source"]!["opcode"] = 32; break;
            case "unknown-field": command["count"] = 1; break;
            case "missing-field": command.Remove("eventObjectId"); break;
            case "word-type": command["eventObjectId"] = "1"; break;
            case "word-negative": command["eventObjectId"] = -1; break;
            case "word-overflow": command["eventObjectId"] = 65536; break;
            case "reference-null": command["to"] = null; break;
            case "reference-malformed": command["to"] = "shared##L0000"; break;
            case "reference-dangling": command["to"] = "missing#L0000"; break;
        }
        var error = Assert.Throws<InvalidDataException>(() => fixture.Load());
        Assert.Contains("shared.json", error.Message);
        Assert.Contains("@0000", error.Message);
    }

    [Fact]
    public void Named_content_keeps_immutable_semantics_after_parse_document_and_input_are_gone()
    {
        var fixture = new EventFixture();
        fixture.Commands[0] = JsonNode.Parse("""
            {"label":"L0000","op":"removeItem","itemId":76,"count":65535,"shortageTarget":null,"next":"L0001",
             "source":{"index":0,"opcode":32,"operands":[99,0,4]},"_note":{"text":"edited"}}
            """);
        var collection = fixture.Load();
        fixture.Commands[0]!["count"] = 1;
        var content = collection.GetCommandContent(0);
        Assert.Equal("removeItem", content.GetProperty("op").GetString());
        Assert.Equal(76, content.GetProperty("itemId").GetInt32());
        Assert.Equal(65535, content.GetProperty("count").GetInt32());
        Assert.Equal("edited", content.GetProperty("_note").GetProperty("text").GetString());
        Assert.Equal(new[] { 99, 0, 4 }, collection.GetSource(0).Operands);
        Assert.Throws<ArgumentOutOfRangeException>(() => collection.GetCommandContent(5));
    }

    [Fact]
    public void Named_startBattle_loads_without_enabling_execution_or_changing_source()
    {
        var fixture = new EventFixture();
        fixture.Commands[1] = JsonNode.Parse("""
            {"label":"L0001","op":"startBattle","enemyTeamId":18,"lossTarget":"L0004","isBoss":true,"fleeTarget":null,
             "next":"L0002","source":{"index":1,"opcode":7,"operands":[99,3,2]}}
            """);
        var collection = fixture.Load();
        Assert.Equal(new[] { 99, 3, 2 }, collection.GetSource(1).Operands);
        var script = new TriggerScriptSession(collection, TriggerScriptSessionTests.Language());
        var before = script.Context;
        var error = Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 60));
        Assert.Contains("startBattle", error.Message);
        Assert.Contains("@0001", error.Message);
        Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
        Assert.Equal(before, script.Context);
        Assert.Null(script.CurrentPosition);
        Assert.Null(script.PendingDisplay);
        Assert.Equal(0, script.Caller);
        Assert.Equal(0, script.ReturnEntry);
    }

    [Theory]
    [InlineData(0, null, true, null)]
    [InlineData(65535, "L0004", false, "L0001")]
    [InlineData(18, null, false, "L0004")]
    public void StartBattle_WORDs_and_zero_sentinels_load_as_named_semantics(int enemyTeamId, string? loss, bool boss, string? flee)
    {
        var fixture = new EventFixture();
        fixture.Commands[1] = BattleCommand(1, enemyTeamId, loss, boss, flee);
        Assert.Equal(5, fixture.Load().InstructionCount);
    }

    [Theory]
    [InlineData("team-negative")]
    [InlineData("team-overflow")]
    [InlineData("team-type")]
    [InlineData("boss-type")]
    [InlineData("boss-flee")]
    [InlineData("nonboss-null")]
    [InlineData("loss-zero")]
    [InlineData("flee-zero")]
    [InlineData("loss-dangling")]
    [InlineData("flee-dangling")]
    [InlineData("loss-malformed")]
    [InlineData("flee-type")]
    [InlineData("unknown-field")]
    [InlineData("missing-field")]
    [InlineData("opcode")]
    [InlineData("ffff-outside")]
    [InlineData("duplicate-key")]
    public void StartBattle_at_true_chunk_end_still_strictly_validates_semantics_and_references(string fault)
    {
        var fixture = new EventFixture();
        var command = BattleCommand(4, 18, null, true, null);
        fixture.Commands[4] = command;
        switch (fault)
        {
            case "team-negative": command["enemyTeamId"] = -1; break;
            case "team-overflow": command["enemyTeamId"] = 65536; break;
            case "team-type": command["enemyTeamId"] = "18"; break;
            case "boss-type": command["isBoss"] = 1; break;
            case "boss-flee": command["fleeTarget"] = "L0001"; break;
            case "nonboss-null": command["isBoss"] = false; break;
            case "loss-zero": command["lossTarget"] = "L0000"; break;
            case "flee-zero": command["isBoss"] = false; command["fleeTarget"] = "shared#L0000"; break;
            case "loss-dangling": command["lossTarget"] = "missing#L0001"; break;
            case "flee-dangling": command["isBoss"] = false; command["fleeTarget"] = "missing"; break;
            case "loss-malformed": command["lossTarget"] = "shared##L0001"; break;
            case "flee-type": command["fleeTarget"] = 1; break;
            case "unknown-field": command["limit"] = 0; break;
            case "missing-field": command.Remove("lossTarget"); break;
            case "opcode": command["source"]!["opcode"] = 8; break;
            case "ffff-outside": command["lossTarget"] = "LFFFF"; break;
        }
        var texts = fixture.Files.ToDictionary(p => p.Key, p => p.Value.ToJsonString());
        if (fault == "duplicate-key") texts["shared"] = texts["shared"].Replace("\"enemyTeamId\":18", "\"enemyTeamId\":18,\"enemyTeamId\":19");
        var error = Assert.Throws<InvalidDataException>(() => TriggerEventCollection.Parse(fixture.Index.ToJsonString(), texts));
        Assert.Contains("shared.json", error.Message);
        if (fault != "duplicate-key") Assert.Contains("@0004", error.Message);
    }

    [Theory]
    [InlineData("{\"op\":\"startBattle\",\"teamId\":18,\"lossTarget\":null,\"isBoss\":true,\"fleeTarget\":null}", 7)]
    [InlineData("{\"op\":\"setObjectPosition\",\"objectId\":28,\"x\":1280,\"y\":256}", 0x13)]
    [InlineData("{\"op\":\"setAutoEntry\",\"objectId\":28,\"entry\":11}", 0x24)]
    [InlineData("{\"op\":\"setTriggerEntry\",\"objectId\":29,\"entry\":2500}", 0x25)]
    [InlineData("{\"op\":\"setState\",\"objectId\":6,\"state\":1}", 0x49)]
    [InlineData("{\"op\":\"setTriggerMode\",\"objectId\":6,\"mode\":2}", 0x40)]
    [InlineData("{\"op\":\"setStateRange\",\"fromObjectId\":29,\"toObjectId\":30,\"state\":2}", 0x9a)]
    public void Pre_114_reference_field_names_stay_rejected_as_unknown_fields(string semanticJson, int opcode)
    {
        // #78 renamed startBattle.teamId to enemyTeamId and the event-object objectIds to
        // eventObjectId (setObjectScript.objectId keeps addressing the object union); the strict
        // loader must keep rejecting the pre-rename names instead of accepting both spellings.
        var fixture = new EventFixture();
        var command = JsonNode.Parse(semanticJson)!.AsObject();
        command["label"] = "L0000";
        command["next"] = "L0001";
        command["source"] = new JsonObject { ["index"] = 0, ["opcode"] = opcode, ["operands"] = new JsonArray(7, 8, 9) };
        fixture.Commands[0] = command;
        Assert.Contains("unknown field", Assert.Throws<InvalidDataException>(() => fixture.Load()).Message);
    }

    [Theory]
    [InlineData("{\"op\":\"fadeOut\",\"delay\":2}", 0x50)]
    [InlineData("{\"op\":\"fadeScene\",\"speed\":3}", 0x73)]
    [InlineData("{\"op\":\"redraw\",\"delayUnits\":5,\"updateGestures\":65535}", 5)]
    public void Screen_verb_99_110_corrected_field_forms_load_strictly(string semanticJson, int opcode)
    {
        // #99/#110 corrected the tool-side spellings: fadeOut.frames -> delay (PAL_FadeOut's
        // fade step unit), fadeScene.mode -> speed (VIDEO_FadeScreen's wSpeed), and redraw
        // gained the operand-2 updateGestures word (its delayUnits re-binds to operand 1 on
        // the export side; the field name and WORD range are unchanged, so only the new
        // field is loader-visible). The FadeOutDelay/FadeSpeed/UpdateGestures property
        // bindings are exercised by the session consumers and the real-package loadproof.
        var fixture = new EventFixture();
        var command = JsonNode.Parse(semanticJson)!.AsObject();
        command["label"] = "L0000";
        command["next"] = "L0001";
        command["source"] = new JsonObject { ["index"] = 0, ["opcode"] = opcode, ["operands"] = new JsonArray(7, 8, 9) };
        fixture.Commands[0] = command;
        Assert.Equal(5, fixture.Load().InstructionCount);
    }

    [Theory]
    [InlineData("{\"op\":\"fadeOut\",\"frames\":2}", 0x50, "unknown field 'frames'")]
    [InlineData("{\"op\":\"fadeScene\",\"mode\":2}", 0x73, "unknown field 'mode'")]
    [InlineData("{\"op\":\"redraw\",\"delayUnits\":2}", 5, "missing field 'updateGestures'")]
    public void Pre_115_reference_field_shapes_stay_rejected(string semanticJson, int opcode, string fault)
    {
        // The pre-correction shapes must keep failing strict load: fadeOut.frames and
        // fadeScene.mode arrive as unknown fields, and the pre-#99 redraw shape (no
        // updateGestures word) as a missing field. The pre-#99 redraw *binding* itself
        // (delayUnits = operand 0) is not expressible as a JSON rejection: the field name
        // and WORD range are identical under both bindings and the loader never sees which
        // source operand produced the value.
        var fixture = new EventFixture();
        var command = JsonNode.Parse(semanticJson)!.AsObject();
        command["label"] = "L0000";
        command["next"] = "L0001";
        command["source"] = new JsonObject { ["index"] = 0, ["opcode"] = opcode, ["operands"] = new JsonArray(7, 8, 9) };
        fixture.Commands[0] = command;
        Assert.Contains(fault, Assert.Throws<InvalidDataException>(() => fixture.Load()).Message);
    }

    [Fact]
    public void StartBattle_cross_file_result_targets_resolve_and_legacy_raw7_stays_non_executable()
    {
        var fixture = new EventFixture();
        fixture.Index["files"] = new JsonArray("shared", "targets");
        var last = fixture.Commands[4]!.DeepClone();
        fixture.Commands.RemoveAt(4);
        fixture.Commands[3]!["next"] = "targets#L0004";
        fixture.Files["targets"] = new JsonObject { ["formatVersion"] = 1, ["profile"] = "faithful", ["fileId"] = "targets",
            ["entries"] = new JsonArray(), ["commands"] = new JsonArray(last) };
        fixture.Commands[2] = BattleCommand(2, 18, "targets#L0004", false, "shared#L0001");
        Assert.Equal(5, fixture.Load().InstructionCount);
        fixture.Commands[2] = JsonNode.Parse("""
            {"label":"L0002","op":"raw","next":"L0003","source":{"index":2,"opcode":7,"operands":[18,4,0]}}
            """);
        var script = new TriggerScriptSession(fixture.Load(), TriggerScriptSessionTests.Language());
        var before = script.Context;
        var error = Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 60));
        Assert.Contains("'raw'", error.Message);
        Assert.Contains("@0002", error.Message);
        Assert.Equal(before, script.Context); // Earlier setDialog must not run before rejection.
        Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
        Assert.Null(script.PendingDisplay);
        Assert.Equal(0, script.Caller);
    }

    private static JsonObject BattleCommand(int address, int enemyTeamId, string? loss, bool boss, string? flee) => new()
    {
        ["label"] = $"L{address:X4}", ["op"] = "startBattle", ["enemyTeamId"] = enemyTeamId,
        ["lossTarget"] = loss, ["isBoss"] = boss, ["fleeTarget"] = flee,
        ["next"] = address == 4 ? null : $"L{address + 1:X4}",
        ["source"] = new JsonObject { ["index"] = address, ["opcode"] = 7, ["operands"] = new JsonArray(18, 0, 0) }
    };

    [Theory]
    [InlineData("version")]
    [InlineData("profile")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("hash")]
    [InlineData("identity")]
    [InlineData("length")]
    [InlineData("missing-file")]
    [InlineData("extra-file")]
    [InlineData("file-id")]
    [InlineData("integer-type")]
    [InlineData("root")]
    public void Invalid_collection_envelopes_are_rejected_with_resource_identity(string fault)
    {
        var fixture = new EventFixture();
        string? index = null;
        switch (fault)
        {
            case "version": fixture.Index["formatVersion"] = 2; break;
            case "profile": fixture.Index["profile"] = "structured"; break;
            case "unknown": fixture.Index["typo"] = 1; break;
            case "duplicate": index = fixture.Index.ToJsonString().Replace("\"formatVersion\":1", "\"formatVersion\":1,\"formatVersion\":1"); break;
            case "hash": fixture.Index["source"]!["sha256"] = "ABC"; break;
            case "identity": fixture.Index["source"]!["chunk"] = 3; break;
            case "length": fixture.Index["source"]!["byteLength"] = 8; break;
            case "missing-file": fixture.Files.Clear(); break;
            case "extra-file": fixture.Files.Add("extra", fixture.Files["shared"].DeepClone().AsObject()); break;
            case "file-id": fixture.Files["shared"]["fileId"] = "other"; break;
            case "integer-type": fixture.Index["formatVersion"] = "1"; break;
            case "root": index = "[]"; break;
        }
        var error = Assert.Throws<InvalidDataException>(() => TriggerEventCollection.Parse(index ?? fixture.Index.ToJsonString(), fixture.Files.ToDictionary(p => p.Key, p => p.Value.ToJsonString())));
        Assert.Contains(".json", error.Message);
    }

    [Theory]
    [InlineData("missing-address")]
    [InlineData("duplicate-address")]
    [InlineData("duplicate-label")]
    [InlineData("order")]
    [InlineData("next")]
    [InlineData("last-next")]
    [InlineData("dangling")]
    [InlineData("source-word")]
    [InlineData("operands-count")]
    [InlineData("field")]
    [InlineData("missing-field")]
    [InlineData("mapping")]
    [InlineData("semantic-range")]
    [InlineData("entry-zero")]
    [InlineData("entry-target")]
    [InlineData("entry-slot")]
    [InlineData("entry-owner")]
    [InlineData("duplicate-entry")]
    [InlineData("raw-known")]
    [InlineData("null-command")]
    [InlineData("identifier-newline")]
    [InlineData("hash-newline")]
    [InlineData("null-target")]
    [InlineData("semantic-type")]
    [InlineData("source-type")]
    [InlineData("entry-type")]
    [InlineData("reference-path")]
    [InlineData("reference-fragments")]
    public void Invalid_commands_entries_and_complete_address_graph_are_rejected(string fault)
    {
        var fixture = new EventFixture();
        switch (fault)
        {
            case "missing-address": fixture.Commands.RemoveAt(0); break;
            case "duplicate-address": fixture.Commands[1]!["source"]!["index"] = 0; break;
            case "duplicate-label": fixture.Commands[1]!["label"] = "L0000"; break;
            case "order": var first = fixture.Commands[0]!.DeepClone(); fixture.Commands[0] = fixture.Commands[1]!.DeepClone(); fixture.Commands[1] = first; break;
            case "next": fixture.Commands[1]!["next"] = "L0003"; break;
            case "last-next": fixture.Commands[4]!["next"] = "L0000"; break;
            case "dangling": fixture.Entry["target"] = "missing#L0001"; break;
            case "source-word": fixture.Commands[0]!["source"]!["operands"]![0] = -1; break;
            case "operands-count": fixture.Commands[0]!["source"]!["operands"]!.AsArray().RemoveAt(0); break;
            case "field": fixture.Commands[2]!["text"] = "not executable"; break;
            case "missing-field": fixture.Commands[2]!.AsObject().Remove("messageId"); break;
            case "mapping": fixture.Commands[1]!["source"]!["opcode"] = 61; break;
            case "semantic-range": fixture.Commands[1]!["color"] = 256; break;
            case "entry-zero": fixture.Entry["sourceAddress"] = 0; fixture.Entry["target"] = "L0000"; break;
            case "entry-target": fixture.Entry["target"] = "L0002"; break;
            case "entry-slot": fixture.Entry["slot"] = "wAutoScript"; break;
            case "entry-owner": fixture.Entry["owner"]!["id"] = 65536; break;
            case "duplicate-entry": fixture.Files["shared"]["entries"]!.AsArray().Add(fixture.Entry.DeepClone()); break;
            case "raw-known": fixture.Commands[0]!["source"]!["opcode"] = 60; break;
            case "null-command": fixture.Commands[0] = null; break;
            case "identifier-newline": fixture.Commands[0]!["label"] = "L0000\n"; break;
            case "hash-newline": fixture.Index["source"]!["sha256"] = new string('a', 64) + "\n"; break;
            case "null-target": fixture.Entry["target"] = null; break;
            case "semantic-type": fixture.Commands[2]!["messageId"] = "61"; break;
            case "source-type": fixture.Commands[0]!["source"] = false; break;
            case "entry-type": fixture.Files["shared"]["entries"]![0] = false; break;
            case "reference-path": fixture.Commands[0]!["next"] = "../shared#L0001"; break;
            case "reference-fragments": fixture.Commands[0]!["next"] = "shared#L0001#x"; break;
        }
        var error = Assert.Throws<InvalidDataException>(() => fixture.Load());
        Assert.Contains(".json", error.Message);
    }

    [Theory]
    [InlineData("{\"op\":\"end\",\"mode\":\"advance\"}", 1)]
    [InlineData("{\"op\":\"end\",\"mode\":\"redirect\",\"to\":\"L0000\",\"limit\":65535}", 2)]
    [InlineData("{\"op\":\"goto\",\"to\":\"L0000\",\"limit\":0}", 3)]
    [InlineData("{\"op\":\"addItem\",\"itemId\":65535,\"count\":-32768}", 31)]
    [InlineData("{\"op\":\"setDialog\",\"box\":\"center\",\"color\":0,\"rngWord\":1}", 59)]
    [InlineData("{\"op\":\"setDialog\",\"box\":\"lower\",\"portraitId\":0,\"color\":255,\"rngWord\":0}", 61)]
    [InlineData("{\"op\":\"setDialog\",\"box\":\"centerWindow\",\"color\":79}", 62)]
    [InlineData("{\"op\":\"setMusic\",\"musicId\":65535,\"mode\":3}", 0x43)]
    [InlineData("{\"op\":\"fadeToRed\"}", 0x4f)]
    [InlineData("{\"op\":\"requestReload\"}", 0x4e)]
    public void Faithful_base_mappings_can_be_archived_without_claiming_execution_support(string semanticJson, int opcode)
    {
        var fixture = new EventFixture();
        var command = JsonNode.Parse(semanticJson)!.AsObject();
        command["label"] = "L0000";
        command["next"] = "L0001";
        command["source"] = new JsonObject { ["index"] = 0, ["opcode"] = opcode, ["operands"] = new JsonArray(7, 8, 9) };
        fixture.Commands[0] = command;
        Assert.Equal(5, fixture.Load().InstructionCount);
        if (command.ContainsKey("to"))
        {
            command["to"] = "missing#L0000";
            Assert.Throws<InvalidDataException>(() => fixture.Load());
        }
    }

    [Fact]
    public void Old_001F_giveItem_verb_stays_rejected_after_the_addItem_rename()
    {
        // #116 (M5 review P1-2): the frozen verb for 0x001F is addItem (named by #100; the M5
        // export and the events-schema contract table both use it). The v1 contract placeholder
        // giveItem must stay rejected so exactly one spelling exists, like the #114 field renames.
        var fixture = new EventFixture();
        fixture.Commands[0] = JsonNode.Parse("""
            {"label":"L0000","op":"giveItem","itemId":5,"count":1,"next":"L0001","source":{"index":0,"opcode":31,"operands":[5,1,0]}}
            """);
        Assert.Contains("Unknown op 'giveItem'", Assert.Throws<InvalidDataException>(() => fixture.Load()).Message);
    }

    [Theory]
    [InlineData("player.wScriptOnFriendDeath", "trigger")]
    [InlineData("player.wScriptOnDying", "trigger")]
    [InlineData("item.wScriptOnUse", "trigger")]
    [InlineData("item.wScriptOnEquip", "trigger")]
    [InlineData("item.wScriptOnThrow", "trigger")]
    [InlineData("magic.wScriptOnUse", "trigger")]
    [InlineData("magic.wScriptOnSuccess", "trigger")]
    [InlineData("enemy.wScriptOnTurnStart", "trigger")]
    [InlineData("enemy.wScriptOnBattleEnd", "trigger")]
    [InlineData("enemy.wScriptOnReady", "trigger")]
    [InlineData("poison.wPlayerScript", "trigger")]
    [InlineData("poison.wEnemyScript", "trigger")]
    [InlineData("item.wScriptDesc", "auto")]
    [InlineData("magic.wScriptDesc", "auto")]
    public void Proven_object_union_entry_view_is_validated_by_slot_not_invented_from_owner(string slot, string kind)
    {
        var fixture = new EventFixture();
        fixture.Entry["owner"]!["type"] = "object";
        fixture.Entry["slot"] = slot;
        fixture.Entry["kind"] = kind;
        Assert.Equal(slot, fixture.Load().GetEntry("shared", "npc").Slot);
    }

    [Theory]
    [InlineData("{\"op\":\"end\",\"mode\":\"redirect\",\"to\":null,\"limit\":0}", 2)]
    [InlineData("{\"op\":\"goto\",\"to\":\"L0000\",\"limit\":65536}", 3)]
    [InlineData("{\"op\":\"addItem\",\"itemId\":1,\"count\":32768}", 31)]
    [InlineData("{\"op\":\"addItem\",\"itemId\":-1,\"count\":0}", 31)]
    [InlineData("{\"op\":\"setDialog\",\"box\":\"center\",\"color\":0,\"rngWord\":0,\"portraitId\":0}", 59)]
    [InlineData("{\"op\":\"setDialog\",\"box\":\"lower\",\"color\":0,\"portraitId\":0,\"rngWord\":65536}", 61)]
    [InlineData("{\"op\":\"setDialog\",\"box\":\"centerWindow\",\"color\":-1}", 62)]
    public void Archived_base_mappings_still_require_exact_fields_ranges_and_nonnull_control_targets(string semantics, int opcode)
    {
        var fixture = new EventFixture();
        var command = JsonNode.Parse(semantics)!;
        command["label"] = "L0000";
        command["next"] = "L0001";
        command["source"] = new JsonObject { ["index"] = 0, ["opcode"] = opcode, ["operands"] = new JsonArray(0, 0, 0) };
        fixture.Commands[0] = command;
        Assert.Contains("shared.json", Assert.Throws<InvalidDataException>(() => fixture.Load()).Message);
    }

    [Fact]
    public void Full_WORD_address_space_requires_explicit_wraparound_to_zero()
    {
        var fixture = new EventFixture();
        fixture.Index["source"]!["instructionCount"] = 65536;
        fixture.Index["source"]!["byteLength"] = 524288;
        fixture.Commands.Clear();
        for (int address = 0; address < 65536; address++)
            fixture.Commands.Add(new JsonObject { ["label"] = $"L{address:X4}", ["op"] = "raw", ["next"] = $"L{((address + 1) & 65535):X4}",
                ["source"] = new JsonObject { ["index"] = address, ["opcode"] = 134, ["operands"] = new JsonArray(0, 1, 65535) } });
        var collection = fixture.Load();
        Assert.Equal(65536, collection.InstructionCount);
        fixture.Entry["target"] = "LFFFF";
        fixture.Entry["sourceAddress"] = 65535;
        fixture.Commands[65535]!["op"] = "setDialog";
        fixture.Commands[65535]!["box"] = "upper";
        fixture.Commands[65535]!["portraitId"] = 0;
        fixture.Commands[65535]!["color"] = 0;
        fixture.Commands[65535]!["rngWord"] = 0;
        fixture.Commands[65535]!["source"]!["opcode"] = 60;
        var script = new TriggerScriptSession(fixture.Load(), TriggerScriptSessionTests.Language());
        script.Start("shared", "npc", 60);
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(0, script.CurrentPosition!.SourceAddress); // Trigger stops at PC zero; raw zero is not executed.
        Assert.Equal(65535, script.ReturnEntry);
        fixture.Commands[65535]!["next"] = null;
        Assert.Contains("FFFF", Assert.Throws<InvalidDataException>(() => fixture.Load()).Message);
        fixture.Commands[65535] = BattleCommand(65535, 65535, "LFFFF", false, "LFFFF");
        fixture.Commands[65535]!["next"] = "L0000";
        Assert.Equal(65536, fixture.Load().InstructionCount); // FFFF is a real result target, never a sentinel.
    }

    [Fact]
    public void Cross_file_next_and_entry_references_are_resolved_not_physical_adjacency()
    {
        var fixture = new EventFixture();
        fixture.Index["files"] = new JsonArray("scene", "shared");
        var scene = fixture.Files["shared"].DeepClone().AsObject();
        scene["fileId"] = "scene";
        scene["commands"] = new JsonArray();
        scene["entries"]![0]!["target"] = "shared#L0001";
        fixture.Files.Add("scene", scene);
        fixture.Files["shared"]["entries"] = new JsonArray();
        Assert.Equal(1, fixture.Load().GetEntry("scene", "npc").SourceAddress);
    }

    [Fact]
    public void Duplicate_keys_inside_ignored_annotations_are_still_invalid_JSON_content()
    {
        var fixture = new EventFixture();
        var texts = fixture.Files.ToDictionary(p => p.Key, p => p.Value.ToJsonString());
        texts["shared"] = texts["shared"].Replace("\"fileId\":\"shared\"", "\"fileId\":\"shared\",\"_note\":{\"a\":1,\"a\":2}");
        var error = Assert.Throws<InvalidDataException>(() => TriggerEventCollection.Parse(fixture.Index.ToJsonString(), texts));
        Assert.Contains("shared.json", error.Message);
        Assert.Contains("duplicate JSON key", error.Message);
    }

    [Fact]
    public void Complete_collection_preserves_zero_and_unselected_raw_and_exposes_entry_identity()
    {
        var fixture = new EventFixture();
        TriggerEventCollection collection = fixture.Load();
        Assert.Equal(5, collection.InstructionCount);
        var zero = collection.GetSource(0);
        Assert.Equal(new TriggerPosition("shared", "L0000", 0), zero.Position);
        Assert.Equal(134, zero.Opcode);
        Assert.Equal(new[] { 0, 65535, 8 }, zero.Operands);
        Assert.Single(collection.Entries);
        var entry = collection.GetEntry("shared", "npc");
        Assert.Equal(60, entry.OwnerId);
        Assert.Equal("eventObject", entry.OwnerType);
        Assert.Equal(1, entry.SourceAddress);
        Assert.Equal("trigger", entry.Kind);
        Assert.Equal("wTriggerScript", entry.Slot);
    }

    [Fact]
    public void Retail_pose_words_parse_beyond_the_authored_small_domains()
    {
        // Retail rows use gesture poses far beyond the authored 0..2/0..3 sample space
        // (script.c 0x0015/0x000F words are plain WORDs; 0xFFFF stays the keep value).
        var events = new EventFixture();
        events.Commands[1] = JsonNode.Parse("""
          {"label":"L0001","op":"setPartyFacing","direction":0,"gesture":26,"memberIndex":0,
           "next":"L0002","source":{"index":1,"opcode":21,"operands":[0,26,0]}}
          """);
        events.Commands[2] = JsonNode.Parse("""
          {"label":"L0002","op":"face","direction":0,"frame":11,
           "next":"L0003","source":{"index":2,"opcode":15,"operands":[0,11,0]}}
          """);
        Assert.NotNull(events.Load());
    }

    [Fact]
    public void WaitUpdate_accepts_the_retail_truthy_operand_words_not_only_zero_and_one()
    {
        // Retail chunk-4 rows carry 0xFFFF for the gesture flag; script.c tests it for truthiness only.
        var events = new EventFixture();
        events.Commands[3] = JsonNode.Parse("""
          {"label":"L0003","op":"waitUpdate","frames":12,"runTriggers":0,"updateGestures":65535,
           "next":"L0004","source":{"index":3,"opcode":9,"operands":[12,0,65535]}}
          """);
        Assert.NotNull(events.Load()); // strict parse accepts the full WORD domain
    }

}

internal sealed class EventFixture
{
    public JsonObject Index { get; } = JsonNode.Parse("""
        {"formatVersion":1,"profile":"faithful","source":{"archive":"SSS.MKF","chunk":4,"byteOrder":"little","instructionCount":5,"byteLength":40,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},"files":["shared"]}
        """)!.AsObject();
    public Dictionary<string, JsonObject> Files { get; } = new()
    {
        ["shared"] = JsonNode.Parse("""
        {"formatVersion":1,"profile":"faithful","fileId":"shared","entries":[
          {"id":"npc","owner":{"type":"eventObject","id":60},"slot":"wTriggerScript","kind":"trigger","target":"L0001","sourceAddress":1}],"commands":[
          {"label":"L0000","op":"raw","next":"L0001","source":{"index":0,"opcode":134,"operands":[0,65535,8]}},
          {"label":"L0001","op":"setDialog","box":"upper","portraitId":59,"color":0,"rngWord":0,"next":"L0002","source":{"index":1,"opcode":60,"operands":[59,0,0]}},
          {"label":"L0002","op":"showDialog","messageId":61,"descriptionLineWord":13,"next":"L0003","source":{"index":2,"opcode":65535,"operands":[61,13,29]}},
          {"label":"L0003","op":"showDialog","messageId":62,"descriptionLineWord":0,"next":"L0004","source":{"index":3,"opcode":65535,"operands":[62,0,0]}},
          {"label":"L0004","op":"end","mode":"keep","next":null,"source":{"index":4,"opcode":0,"operands":[7,8,9]}}]}
        """)!.AsObject()
    };
    public JsonArray Commands => Files["shared"]["commands"]!.AsArray();
    public JsonObject Entry => Files["shared"]["entries"]![0]!.AsObject();
    public TriggerEventCollection Load() => TriggerEventCollection.Parse(Index.ToJsonString(), Files.ToDictionary(p => p.Key, p => p.Value.ToJsonString()));
}

// Independent M5 shell oracle: 46 typed literals from events-schema plus119 reviewed
// hand-written InlineData above. Not generated at runtime from Core profiles or CLI.
// The165 shell rows are separate from5 small supported execution rows (170 addresses).
internal sealed class CompleteNamedEventFixture
{
    internal static readonly int[] ExpectedOpcodes =
    [
        0x0000, 0x0001, 0x0002, 0x0003, 0x0004, 0x0005, 0x0006, 0x0007, 0x0008, 0x0009, 0x000A, 0x000B,
        0x000C, 0x000D, 0x000E, 0x000F, 0x0010, 0x0011, 0x0012, 0x0013, 0x0014, 0x0015, 0x0016, 0x0017,
        0x0018, 0x0019, 0x001A, 0x001B, 0x001C, 0x001D, 0x001E, 0x001F, 0x0020, 0x0021, 0x0022, 0x0023,
        0x0024, 0x0025, 0x0026, 0x0027, 0x0028, 0x0029, 0x002A, 0x002B, 0x002C, 0x002D, 0x002E, 0x002F,
        0x0030, 0x0031, 0x0033, 0x0034, 0x0035, 0x0036, 0x0037, 0x0038, 0x0039, 0x003A, 0x003B, 0x003C,
        0x003D, 0x003E, 0x003F, 0x0040, 0x0041, 0x0042, 0x0043, 0x0044, 0x0045, 0x0046, 0x0047, 0x0049,
        0x004A, 0x004B, 0x004C, 0x004D, 0x004E, 0x004F, 0x0050, 0x0051, 0x0052, 0x0053, 0x0054, 0x0055,
        0x0056, 0x0057, 0x0058, 0x0059, 0x005A, 0x005B, 0x005C, 0x005D, 0x005E, 0x005F, 0x0060, 0x0061,
        0x0062, 0x0063, 0x0064, 0x0065, 0x0066, 0x0067, 0x0068, 0x0069, 0x006A, 0x006B, 0x006C, 0x006D,
        0x006E, 0x006F, 0x0070, 0x0071, 0x0073, 0x0074, 0x0075, 0x0076, 0x0077, 0x0078, 0x0079, 0x007A,
        0x007B, 0x007C, 0x007D, 0x007E, 0x007F, 0x0080, 0x0081, 0x0082, 0x0083, 0x0084, 0x0085, 0x0086,
        0x0087, 0x0088, 0x0089, 0x008A, 0x008B, 0x008C, 0x008D, 0x008E, 0x008F, 0x0090, 0x0091, 0x0092,
        0x0093, 0x0094, 0x0095, 0x0096, 0x0097, 0x0098, 0x0099, 0x009A, 0x009B, 0x009C, 0x009E, 0x009F,
        0x00A0, 0x00A1, 0x00A2, 0x00A3, 0x00A4, 0x00A5, 0x00A6, 0x00A7, 0xFFFF,
    ];

    internal const string ShellLiterals = """
        0000 {"op":"end","mode":"keep"}
        0001 {"op":"end","mode":"advance"}
        0002 {"op":"end","mode":"redirect","to":"selected#L00A7","limit":2}
        0003 {"op":"goto","to":"selected#L00A7","limit":0}
        0004 {"op":"call","to":"selected#L00A7","eventObjectId":65535}
        0005 {"op":"redraw","delayUnits":2,"updateGestures":65535}
        0006 {"op":"randomJump","rate":101,"to":"selected#L00A7"}
        0007 {"op":"startBattle","enemyTeamId":18,"lossTarget":null,"isBoss":true,"fleeTarget":null}
        0008 {"op":"advanceEntry"}
        0009 {"op":"waitUpdate","frames":2,"runTriggers":0,"updateGestures":65535}
        000A {"op":"confirmNoJump","to":"selected#L00A7"}
        000B {"op":"walkStep","direction":0}
        000C {"op":"walkStep","direction":1}
        000D {"op":"walkStep","direction":2}
        000E {"op":"walkStep","direction":3}
        000F {"op":"face","direction":65535,"frame":26}
        0010 {"op":"walkTo","x":9,"y":21,"h":0}
        0011 {"op":"walkToSlow","x":65535,"y":0,"h":65535}
        0012 {"op":"setObjectPositionRelativeToParty","eventObjectId":65535,"xOffset":65408,"yOffset":56}
        0013 {"op":"setObjectPosition","eventObjectId":6,"x":352,"y":320}
        0014 {"op":"setObjectGesture","frame":11}
        0015 {"op":"setPartyFacing","direction":0,"gesture":26,"memberIndex":0}
        0016 {"op":"setObjectFacing","eventObjectId":0,"direction":4,"frame":27}
        0017 {"op":"setExtraAttr","equipPartWord":17,"field":27,"value":-32768}
        0018 {"op":"equipItem","equipPartWord":11,"itemId":65535}
        0019 {"op":"incPlayerAttr","field":65535,"delta":32767,"roleOperand":0}
        001A {"op":"setPlayerStat","field":0,"value":-1,"roleOperand":65535}
        001B {"op":"changeHp","scope":0,"amount":-10}
        001C {"op":"changeMp","scope":0,"amount":-5}
        001D {"op":"changeHpMp","scope":0,"amount":10}
        001E {"op":"changeCash","amount":-32768,"to":"selected#L00A7"}
        001F {"op":"addItem","itemId":76,"count":1}
        0020 {"op":"removeItem","itemId":76,"count":1,"shortageTarget":"selected#L00A7"}
        0021 {"op":"inflictDamageToEnemy","scope":65535,"damage":65535}
        0022 {"op":"revivePlayer","scope":0,"healthTenths":5}
        0023 {"op":"removeEquipment","playerRole":0,"partWord":6}
        0024 {"op":"setAutoEntry","eventObjectId":6,"entry":168}
        0025 {"op":"setTriggerEntry","eventObjectId":5,"entry":165}
        0026 {"op":"showBuyMenu","storeId":65535}
        0027 {"op":"showSellMenu"}
        0028 {"op":"applyPoisonToEnemy","scope":0,"objectId":551}
        0029 {"op":"applyPoisonToPlayer","scope":65535,"objectId":564}
        002A {"op":"curePoisonByObjectIdForEnemy","scope":1,"objectId":562}
        002B {"op":"curePoisonByObjectIdForPlayer","scope":0,"objectId":0}
        002C {"op":"curePoisonsByLevel","scope":1,"maxLevel":65535}
        002D {"op":"setStatusPlayer","status":8,"rounds":65535}
        002E {"op":"setStatusEnemy","status":0,"rounds":32760,"to":"selected#L00A7"}
        002F {"op":"clearPlayerStatus","status":65535}
        0030 {"op":"boostStat","field":17,"percent":50,"roleOperand":0}
        0031 {"op":"changeBattleSprite","battleSprite":65535}
        0033 {"op":"collectEnemyForItems","to":"selected#L00A7"}
        0034 {"op":"transformCollectedEnemies","to":"selected#L00A7"}
        0035 {"op":"shakeScreen","times":65535,"level":0}
        0036 {"op":"setPlayingRng","rngId":0}
        0037 {"op":"playRng","startFrame":65535,"endFrame":0,"speed":0}
        0038 {"op":"teleportParty","to":"selected#L00A7"}
        0039 {"op":"drainHpFromEnemy","amount":65535}
        003A {"op":"playerFlee","to":"selected#L00A7"}
        003B {"op":"setDialog","box":"center","color":79,"rngWord":0}
        003C {"op":"setDialog","box":"upper","portraitId":0,"color":79,"rngWord":0}
        003D {"op":"setDialog","box":"lower","portraitId":0,"color":79,"rngWord":0}
        003E {"op":"setDialog","box":"centerWindow","color":79}
        003F {"op":"rideEventObjectSlow","x":65535,"y":0,"h":65535}
        0040 {"op":"setTriggerMode","eventObjectId":6,"mode":1}
        0041 {"op":"markFailed"}
        0042 {"op":"simulateMagic","magicId":65535,"baseDamage":0,"enemyOperand":-32768}
        0043 {"op":"setMusic","musicId":5,"mode":3}
        0044 {"op":"rideEventObject","x":0,"y":65535,"h":65535}
        0045 {"op":"setBattleMusic","battleMusicId":65535}
        0046 {"op":"setPartyPos","x":9,"y":21,"h":0}
        0047 {"op":"playSound","soundId":65535}
        0049 {"op":"setState","eventObjectId":6,"state":1}
        004A {"op":"setBattlefield","battlefieldId":24}
        004B {"op":"nullifyObject"}
        004C {"op":"chasePlayer","chaseRange":0,"speed":0,"floating":65535}
        004D {"op":"waitForAnyKey"}
        004E {"op":"requestReload"}
        004F {"op":"fadeToRed"}
        0050 {"op":"fadeOut","delay":2}
        0051 {"op":"fadeIn","delay":65535}
        0052 {"op":"hide","frames":800}
        0053 {"op":"useDayPalette"}
        0054 {"op":"useNightPalette"}
        0055 {"op":"addMagic","magicId":65535,"roleOperand":0}
        0056 {"op":"removeMagic","magicId":0,"roleOperand":65535}
        0057 {"op":"setMagicBaseDamageByMp","magicId":65535,"mpMultiplier":0}
        0058 {"op":"itemShortageJump","itemId":65535,"count":-32768,"to":"selected#L00A7"}
        0059 {"op":"changeScene","sceneId":65535}
        005A {"op":"halveHp"}
        005B {"op":"halveEnemyHp","maxDamage":65535}
        005C {"op":"setBattleHidingTime","units":65535}
        005D {"op":"playerLacksPoisonJump","objectId":65535,"to":"selected#L00A7"}
        005E {"op":"enemyLacksPoisonJump","objectId":0,"to":"selected#L00A7"}
        005F {"op":"killPlayer"}
        0060 {"op":"killEnemy"}
        0061 {"op":"notPoisonedJump","to":"selected#L00A7"}
        0062 {"op":"pauseEnemyChasing","cycles":65535}
        0063 {"op":"speedUpEnemyChasing","cycles":0}
        0064 {"op":"enemyHpAboveJump","percent":65535,"to":"selected#L00A7"}
        0065 {"op":"setPlayerSprite","playerRole":65535,"spriteNum":0,"reload":65535}
        0066 {"op":"throwWeapon","magicId":65535,"damageUnits":0}
        0067 {"op":"enemyUseMagic","magicId":65535,"magicRate":0}
        0068 {"op":"enemyTurnJump","to":"selected#L00A7"}
        0069 {"op":"enemyEscape"}
        006A {"op":"stealFromEnemy","stealRate":65535}
        006B {"op":"blowAwayEnemies","strength":-32768}
        006C {"op":"walkNpcStep","eventObjectId":65535,"xOffset":-32768,"yOffset":32767}
        006D {"op":"setSceneScripts","sceneId":1,"onEnter":0,"onTeleport":0}
        006E {"op":"moveParty","xOffset":32767,"yOffset":-32768,"layerUnits":65535}
        006F {"op":"syncState","eventObjectId":0,"state":-32768}
        0070 {"op":"partyWalkToSlow","x":65535,"y":0,"h":65535}
        0071 {"op":"waveScreen","level":65535,"progression":32767}
        0073 {"op":"fadeScene","speed":3}
        0074 {"op":"partyHpNotFullJump","to":"selected#L00A7"}
        0075 {"op":"setParty","roleId0":1,"roleId1":2,"roleId2":0}
        0076 {"op":"showFbp","fbpId":65535,"fade":0}
        0077 {"op":"stopMusic","fadeUnits":0}
        0078 {"op":"noop"}
        0079 {"op":"roleInPartyJump","roleNameWord":65535,"to":"selected#L00A7"}
        007A {"op":"partyWalkTo","x":0,"y":65535,"h":65535}
        007B {"op":"partyWalkToFast","x":65535,"y":0,"h":65535}
        007C {"op":"walkToFast","x":65535,"y":65535,"h":65535}
        007D {"op":"moveObject","eventObjectId":0,"xOffset":-32768,"yOffset":32767}
        007E {"op":"setObjectLayer","eventObjectId":65535,"layer":-32768}
        007F {"op":"moveViewport","xOffset":32767,"yOffset":-32768,"frames":65535}
        0080 {"op":"toggleDayNightPalette","paletteOnly":65535}
        0081 {"op":"notFacingObjectJump","eventObjectId":65535,"zoneUnits":65535,"to":"selected#L00A7"}
        0082 {"op":"walkToFastest","x":65535,"y":0,"h":65535}
        0083 {"op":"objectOutOfZoneJump","eventObjectId":0,"zoneUnits":65535,"to":"selected#L00A7"}
        0084 {"op":"placeUsedItem","eventObjectId":65535,"state":-32768,"to":"selected#L00A7"}
        0085 {"op":"delay","delayUnits":65535}
        0086 {"op":"itemNotEquippedJump","itemId":65535,"count":0,"to":"selected#L00A7"}
        0087 {"op":"animateObject","eventObjectId":0}
        0088 {"op":"setMagicBaseDamageByCash","magicId":65535}
        0089 {"op":"setBattleResult","result":65535}
        008A {"op":"enableAutoBattle"}
        008B {"op":"setPalette","paletteId":65535}
        008C {"op":"colorFade","color":255,"delay":0,"fromColor":65535}
        008D {"op":"increaseLevel","levels":65535}
        008E {"op":"restoreScreen"}
        008F {"op":"halveCash"}
        0090 {"op":"setObjectScript","objectId":76,"value":0,"slotIndex":2}
        0091 {"op":"notFirstOfKindJump","to":"selected#L00A7"}
        0092 {"op":"showMagicCastAnimation","playerOperand":65535}
        0093 {"op":"sceneFade","step":-32768}
        0094 {"op":"stateMatchJump","eventObjectId":65535,"state":32767,"to":"selected#L00A7"}
        0095 {"op":"sceneMatchJump","sceneId":65535,"to":"selected#L00A7"}
        0096 {"op":"showEndingAnimation"}
        0097 {"op":"rideEventObjectFast","x":65535,"y":0,"h":65535}
        0098 {"op":"setFollower","spriteNum0":0,"spriteNum1":65535}
        0099 {"op":"changeSceneMap","sceneId":65535,"mapNum":65535}
        009A {"op":"setStateRange","fromEventObjectId":5,"toEventObjectId":6,"state":1}
        009B {"op":"fadeToScene"}
        009C {"op":"enemyDivide","copies":0,"failTarget":null}
        009E {"op":"enemySummon","objectId":65535,"count":-32768,"failTarget":null}
        009F {"op":"enemyTransform","objectId":65535}
        00A0 {"op":"quitGame"}
        00A1 {"op":"gatherParty"}
        00A2 {"op":"randomBranch","count":65535}
        00A3 {"op":"playCdMusic","track":-1,"musicId":65535}
        00A4 {"op":"scrollFbp","fbpId":65535,"speed":0}
        00A5 {"op":"showFbpWithEffectSprite","fbpId":0,"effectSprite":65535,"fade":65535}
        00A6 {"op":"backupScreen"}
        00A7 {"op":"advanceEntry"}
        FFFF {"op":"showDialog","messageId":61,"descriptionLineWord":13}
        """;

    internal JsonObject Index { get; }
    internal Dictionary<string, JsonObject> Files { get; } = [];
    internal JsonArray Shells => Files["archive"]["commands"]!.AsArray();
    internal JsonArray Selected => Files["selected"]["commands"]!.AsArray();

    internal CompleteNamedEventFixture()
    {
        Index = JsonNode.Parse("""
            {"formatVersion":1,"profile":"faithful","source":{"archive":"SSS.MKF","chunk":4,"byteOrder":"little","instructionCount":170,"byteLength":1360,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},"files":["archive","selected"]}
            """)!.AsObject();
        Files["archive"] = CreateEventFile("archive");
        Files["selected"] = CreateEventFile("selected");
        int address = 0;
        foreach (string row in ShellLiterals.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int opcode = Convert.ToInt32(row[..4], 16);
            Shells.Add(Command(address++, opcode, row[5..]));
        }
        Selected.Add(Command(165, 0x49, """{"op":"setState","eventObjectId":6,"state":1}"""));
        Selected.Add(Command(166, 0x78, """{"op":"noop"}"""));
        Selected.Add(Command(167, 0, """{"op":"end","mode":"keep"}"""));
        Selected.Add(Command(168, 15, """{"op":"face","direction":1,"frame":0}"""));
        Selected.Add(Command(169, 0, """{"op":"end","mode":"keep"}"""));
        Files["selected"]["entries"] = JsonNode.Parse("""
            [{"id":"npc","owner":{"type":"eventObject","id":5},"slot":"wTriggerScript","kind":"trigger","target":"L00A5","sourceAddress":165},
             {"id":"walker","owner":{"type":"eventObject","id":6},"slot":"wAutoScript","kind":"auto","target":"L00A8","sourceAddress":168},
             {"id":"itemUse","owner":{"type":"object","id":76},"slot":"item.wScriptOnUse","kind":"trigger","target":"L00A5","sourceAddress":165},
             {"id":"itemDesc","owner":{"type":"object","id":76},"slot":"item.wScriptDesc","kind":"auto","target":"L00A8","sourceAddress":168}]
            """);
    }

    internal static JsonObject Command(int address, int opcode, string semantics)
    {
        var node = JsonNode.Parse(semantics)!.AsObject();
        node["label"] = $"L{address:X4}";
        node["next"] = address == 169 ? null : address == 164 ? "selected#L00A5" : $"L{address + 1:X4}";
        node["source"] = new JsonObject { ["index"] = address, ["opcode"] = opcode, ["operands"] = new JsonArray(123, 456, 789) };
        return node;
    }

    private static JsonObject CreateEventFile(string id) => new()
    {
        ["formatVersion"] = 1, ["profile"] = "faithful", ["fileId"] = id,
        ["entries"] = new JsonArray(), ["commands"] = new JsonArray()
    };

    internal TriggerEventCollection Load() => TriggerEventCollection.Parse(Index.ToJsonString(),
        Files.ToDictionary(p => p.Key, p => p.Value.ToJsonString()));
}
