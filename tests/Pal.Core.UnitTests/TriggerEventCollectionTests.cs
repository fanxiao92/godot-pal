using System.Text.Json.Nodes;
using Pal.Core;
using Xunit;

namespace Pal.Core.Tests;

public class TriggerEventCollectionTests
{
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
        command["source"]!["opcode"] = 0x00A7; // Same verb as 0008, but outside this batch.
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
