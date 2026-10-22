using System.Text.Json.Nodes;
using Pal.Core;
using Xunit;

namespace Pal.Core.Tests;

public class TriggerScriptSessionTests
{
    [Fact]
    public void Real_script_shape_runs_to_display_boundary_and_returns_saved_entry_only_after_completion_receipt()
    {
        var fixture = RealShape();
        var script = new TriggerScriptSession(fixture.Load(), Language());
        script.Start("shared", "npc", caller: 60);

        Assert.Equal(TriggerScriptPhase.WaitingForDisplay, script.Phase);
        Assert.Equal(60, script.Caller);
        Assert.Equal(0x0188, script.ReturnEntry);
        Assert.Equal(new TriggerPosition("shared", "L018B", 0x018B), script.CurrentPosition);
        var request = Assert.IsType<TriggerDialogueRequest>(script.PendingDisplay);
        Assert.Equal(59, request.Context.PortraitId);
        Assert.Equal("upper", request.Context.Box);
        Assert.Equal(0x4F, request.Context.Color);
        Assert.Equal(24, request.Context.DelayMilliseconds);
        Assert.False(request.Context.PlayingRng);
        var reference = Assert.Single(request.MessageReferences);
        Assert.Equal(61, reference.StartMessageId);
        Assert.Equal(1, reference.Span);
        Assert.Equal(new TriggerPosition("shared", "L0189", 0x0189), reference.Position);
        Assert.False(script.AcknowledgeDisplay(request.Id));
        request.Display.Update(0, true, false);
        Assert.Equal(DialogueDisplayPhase.WaitingForAdvance, request.Display.Current.Phase);
        Assert.Equal(TriggerScriptPhase.WaitingForDisplay, script.Phase);
        Assert.False(script.AcknowledgeDisplay(request.Id));
        request.Display.Update(0, true, false);
        Assert.True(script.AcknowledgeDisplay(request.Id));
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Null(script.PendingDisplay);
        Assert.Equal(0x0188, script.ReturnEntry);
    }

    [Theory]
    [InlineData("raw")]
    [InlineData("rng")]
    [InlineData("lower")]
    [InlineData("giveItem")]
    [InlineData("goto")]
    [InlineData("advance")]
    public void Entire_selected_capability_closure_is_rejected_before_any_display_or_caller_change(string fault)
    {
        var fixture = new EventFixture();
        var unsupported = fault switch
        {
            "raw" => JsonNode.Parse("""{"op":"raw","source":{"index":3,"opcode":142,"operands":[0,0,0]}}""")!,
            "rng" => JsonNode.Parse("""{"op":"setDialog","box":"upper","portraitId":59,"color":0,"rngWord":1,"source":{"index":3,"opcode":60,"operands":[59,0,0]}}""")!,
            "lower" => JsonNode.Parse("""{"op":"setDialog","box":"lower","portraitId":59,"color":0,"rngWord":0,"source":{"index":3,"opcode":61,"operands":[59,0,0]}}""")!,
            "giveItem" => JsonNode.Parse("""{"op":"giveItem","itemId":5,"count":-1,"source":{"index":3,"opcode":31,"operands":[0,0,0]}}""")!,
            "goto" => JsonNode.Parse("""{"op":"goto","to":"L0000","limit":0,"source":{"index":3,"opcode":3,"operands":[0,0,0]}}""")!,
            _ => JsonNode.Parse("""{"op":"end","mode":"advance","source":{"index":3,"opcode":1,"operands":[0,0,0]}}""")!
        };
        unsupported["label"] = "L0003";
        unsupported["next"] = "L0004";
        fixture.Commands[3] = unsupported;
        var language = new DialogueLanguagePackage("en", [new DialogueMessageBlock(61, 61, [DialogueMessagePart.FromText("A")])]);
        var script = new TriggerScriptSession(fixture.Load(), language);
        var error = Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 60));
        Assert.Contains("shared.json", error.Message);
        Assert.Contains("L0003", error.Message);
        Assert.Contains("0003", error.Message);
        Assert.Null(script.PendingDisplay);
        Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
        Assert.Equal(0, script.Caller);
    }

    [Fact]
    public void Zero_entry_is_disabled_not_an_invocation_of_archived_instruction_zero_and_auto_is_not_trigger()
    {
        var fixture = new EventFixture();
        fixture.Entry["target"] = null;
        fixture.Entry["sourceAddress"] = 0;
        var script = new TriggerScriptSession(fixture.Load(), Language());
        script.Start("shared", "npc", 65535);
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(0, script.Caller);
        Assert.Equal(0, script.ReturnEntry);
        Assert.Null(script.PendingDisplay);
        fixture.Entry["kind"] = "auto";
        fixture.Entry["slot"] = "wAutoScript";
        var auto = new TriggerScriptSession(fixture.Load(), Language());
        Assert.Throws<InvalidDataException>(() => auto.Start("shared", "npc", 60));
    }

    [Theory]
    [InlineData(90)]
    [InlineData(61)]
    [InlineData(60)]
    public void Discontinuous_or_repeated_message_IDs_resolve_separate_ranges_but_do_not_invent_intermediate_waits(int secondId)
    {
        var fixture = new EventFixture();
        fixture.Commands[3]!["messageId"] = secondId; // The source operand remains 62; runtime uses the semantic field.
        var blocks = new List<DialogueMessageBlock> { new(61, 61, [DialogueMessagePart.FromText("A")]) };
        if (secondId != 61) blocks.Add(new(secondId, secondId, [DialogueMessagePart.FromText("B"), DialogueMessagePart.FromText("C")]));
        var script = new TriggerScriptSession(fixture.Load(), new DialogueLanguagePackage("en", blocks));
        script.Start("shared", "npc", 60);
        var request = script.PendingDisplay!;
        Assert.Collection(request.MessageReferences,
            first => { Assert.Equal(61, first.StartMessageId); Assert.Equal(0, first.Span); },
            second => { Assert.Equal(secondId, second.StartMessageId); Assert.Equal(0, second.Span); });
        request.Display.Update(24, false, false);
        Assert.Equal(DialogueDisplayPhase.Typing, request.Display.Current.Phase);
        Assert.Equal("A", Assert.Single(Assert.Single(request.Display.Current.VisibleLines).Runs).Text);
        request.Display.Update(24, false, false);
        if (secondId != 61) request.Display.Update(24, false, false);
        Assert.Equal(DialogueDisplayPhase.WaitingForAdvance, request.Display.Current.Phase);
        Assert.Equal(secondId == 61 ? 2 : 3, request.Display.Current.VisibleLines.Count);
        request.Display.Update(0, true, false);
        Assert.True(script.AcknowledgeDisplay(request.Id));
        Assert.Equal(4, script.CurrentPosition!.SourceAddress);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    public void Translation_line_count_and_CLEAR_do_not_advance_script_addresses(int lines)
    {
        var fixture = new EventFixture();
        var parts = Enumerable.Range(0, lines).Select(_ => DialogueMessagePart.FromText("A")).ToList();
        parts.Insert(0, DialogueMessagePart.Clear);
        var language = new DialogueLanguagePackage("en", [new DialogueMessageBlock(61, 62, parts)]);
        var script = new TriggerScriptSession(fixture.Load(), language);
        script.Start("shared", "npc", 60);
        var request = script.PendingDisplay!;
        Assert.Equal(1, Assert.Single(request.MessageReferences).Span);
        Assert.Equal(4, script.CurrentPosition!.SourceAddress);
        for (int i = 0; i < 20 && request.Display.Current.Phase != DialogueDisplayPhase.Completed; i++)
            request.Display.Update(24, request.Display.Current.Phase == DialogueDisplayPhase.WaitingForAdvance, false);
        Assert.True(script.AcknowledgeDisplay(request.Id));
        Assert.Equal(1, script.ReturnEntry);
        Assert.Equal(4, script.CurrentPosition!.SourceAddress);
    }

    [Fact]
    public void Completed_display_receipt_preserves_trailing_color_and_speed_across_setDialog_and_end_restores_defaults_for_reinvocation()
    {
        var fixture = TwoDisplays();
        var language = new DialogueLanguagePackage("en", [
            new DialogueMessageBlock(61, 61, [DialogueMessagePart.FromText("A'$12")]),
            new DialogueMessageBlock(90, 90, [DialogueMessagePart.FromText("B")])]);
        var script = new TriggerScriptSession(fixture.Load(), language, inheritedColor: 7);
        script.Start("shared", "npc", 60);
        var first = script.PendingDisplay!;
        first.Display.Update(24, false, false);
        first.Display.Update(0, true, false);
        Assert.True(script.AcknowledgeDisplay(first.Id));
        var second = script.PendingDisplay!;
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(0x1A, second.Context.Color);
        Assert.Equal(136, second.Context.DelayMilliseconds);
        Assert.Equal(0, second.Context.PortraitId);
        Assert.Equal("", second.Display.Current.VisibleText);
        second.Display.Update(135, false, false);
        Assert.Equal("", second.Display.Current.VisibleText);
        second.Display.Update(1, false, false);
        Assert.Equal("B", second.Display.Current.VisibleText);
        Assert.Equal(0x1A, second.Display.Current.Color);
        second.Display.Update(0, true, false);
        Assert.True(script.AcknowledgeDisplay(second.Id));
        Assert.Equal(0x4F, script.Context.Color);
        Assert.Equal(0, script.Context.PortraitId);
        Assert.False(script.Context.PlayingRng);
        script.Start("shared", "npc", 65535);
        Assert.Equal(60, script.Caller);
        Assert.Equal(24, script.PendingDisplay!.Context.DelayMilliseconds);
        Assert.Equal(0x4F, script.PendingDisplay.Context.Color);
    }

    [Fact]
    public void Missing_later_language_range_is_rejected_before_the_first_display()
    {
        var fixture = TwoDisplays();
        var language = new DialogueLanguagePackage("en", [new DialogueMessageBlock(61, 61, [DialogueMessagePart.FromText("A")])]);
        var script = new TriggerScriptSession(fixture.Load(), language);
        var error = Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 60));
        Assert.Contains("shared.json", error.Message);
        Assert.Contains("L0004", error.Message);
        Assert.Contains("0004", error.Message);
        Assert.Null(script.PendingDisplay);
        Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
    }

    [Fact]
    public void Title_only_content_has_no_player_wait_and_disabled_entry_does_not_block_scene_updates()
    {
        var fixture = new EventFixture();
        var language = new DialogueLanguagePackage("en", [new DialogueMessageBlock(61, 62, [DialogueMessagePart.FromText("NPC:")])]);
        var script = new TriggerScriptSession(fixture.Load(), language);
        Assert.False(script.BlocksSceneUpdates);
        script.Start("shared", "npc", 60);
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.False(script.BlocksSceneUpdates);
        Assert.Null(script.PendingDisplay);
    }

    [Fact]
    public void Duplicate_stale_and_foreign_receipts_cannot_cross_waits_calls_or_executor_instances()
    {
        var fixture = TwoDisplays();
        var language = new DialogueLanguagePackage("en", [
            new DialogueMessageBlock(61, 61, [DialogueMessagePart.FromText("A")]),
            new DialogueMessageBlock(90, 90, [DialogueMessagePart.FromText("B")])]);
        var script = new TriggerScriptSession(fixture.Load(), language);
        var foreign = new TriggerScriptSession(fixture.Load(), language);
        script.Start("shared", "npc", 60);
        foreign.Start("shared", "npc", 61);
        var first = script.PendingDisplay!;
        Assert.Throws<InvalidOperationException>(() => script.Start("shared", "npc", 9));
        Assert.True(script.BlocksSceneUpdates);
        first.Display.Update(24, false, false);
        first.Display.Update(0, true, false);
        Assert.False(script.AcknowledgeDisplay(foreign.PendingDisplay!.Id));
        Assert.True(script.AcknowledgeDisplay(first.Id));
        var second = script.PendingDisplay!;
        Assert.False(script.AcknowledgeDisplay(first.Id));
        Assert.Same(second, script.PendingDisplay);
        Assert.Empty(second.Display.Current.VisibleText); // The preceding completion input was not forwarded.
        second.Display.Update(24, false, false);
        second.Display.Update(0, true, false);
        Assert.True(script.AcknowledgeDisplay(second.Id));
        Assert.False(script.AcknowledgeDisplay(second.Id));
        Assert.False(script.BlocksSceneUpdates);
        script.Start("shared", "npc", 65535);
        Assert.Equal(60, script.Caller);
        Assert.False(script.AcknowledgeDisplay(first.Id));
        Assert.False(script.AcknowledgeDisplay(second.Id));
        Assert.Equal(1, script.ReturnEntry);
        Assert.Empty(script.PendingDisplay!.Display.Current.VisibleText);
    }

    [Fact]
    public void Repeated_real_entry_calls_run_all_messages_again_with_default_speed_and_no_focus_gate()
    {
        var script = new TriggerScriptSession(RealShape().Load(), Language());
        for (int call = 0; call < 2; call++)
        {
            script.Start("shared", "npc", 60);
            var request = script.PendingDisplay!;
            Assert.Equal(61, Assert.Single(request.MessageReferences).StartMessageId);
            request.Display.Update(23, false, false);
            Assert.Empty(request.Display.Current.VisibleText);
            request.Display.Update(1, false, false);
            Assert.Equal("A", request.Display.Current.VisibleText);
            request.Display.Update(1000, false, false); // A late elapsed interval is not frozen by a desktop focus state.
            Assert.Equal("AB", request.Display.Current.VisibleText);
            Assert.Equal(DialogueDisplayPhase.WaitingForAdvance, request.Display.Current.Phase);
            Assert.False(script.AcknowledgeDisplay(request.Id));
            request.Display.Update(0, true, false);
            Assert.True(script.AcknowledgeDisplay(request.Id));
            Assert.Equal(0x0188, script.ReturnEntry);
            Assert.Equal(0x018B, script.CurrentPosition!.SourceAddress);
        }
    }

    [Fact]
    public void Cross_file_message_run_uses_semantic_portrait_color_and_message_references_not_source_operands()
    {
        var fixture = new EventFixture();
        fixture.Commands[1]!["portraitId"] = 7;
        fixture.Commands[1]!["color"] = 9;
        fixture.Commands[2]!["messageId"] = 70;
        fixture.Commands[3]!["messageId"] = 71;
        fixture.Index["files"] = new JsonArray("other", "shared");
        var other = fixture.Files["shared"].DeepClone().AsObject();
        other["fileId"] = "other";
        other["entries"] = new JsonArray();
        other["commands"] = new JsonArray(fixture.Commands[2]!.DeepClone(), fixture.Commands[3]!.DeepClone());
        other["commands"]![1]!["next"] = "shared#L0004";
        fixture.Commands.RemoveAt(3);
        fixture.Commands.RemoveAt(2);
        fixture.Commands[1]!["next"] = "other#L0002";
        fixture.Files.Add("other", other);
        var language = new DialogueLanguagePackage("en", [new DialogueMessageBlock(70, 71, [DialogueMessagePart.FromText("A")])]);
        var script = new TriggerScriptSession(fixture.Load(), language);
        script.Start("shared", "npc", 0);
        var request = script.PendingDisplay!;
        Assert.Equal(7, request.Context.PortraitId);
        Assert.Equal(9, request.Context.Color);
        Assert.Equal(new TriggerPosition("other", "L0002", 2), Assert.Single(request.MessageReferences).Position);
        Assert.Equal(new TriggerPosition("shared", "L0004", 4), script.CurrentPosition);
        request.Display.Update(24, false, false);
        request.Display.Update(0, true, false);
        Assert.True(script.AcknowledgeDisplay(request.Id));
        Assert.Equal(0, script.Caller);
    }

    [Fact]
    public void Entry_inside_a_message_range_uses_the_remaining_original_ID_span()
    {
        var fixture = new EventFixture();
        fixture.Entry["sourceAddress"] = 3;
        fixture.Entry["target"] = "L0003";
        var language = new DialogueLanguagePackage("en", [new DialogueMessageBlock(62, 62, [DialogueMessagePart.FromText("B")])]);
        var script = new TriggerScriptSession(fixture.Load(), language, inheritedColor: 0);
        script.Start("shared", "npc", 60);
        var request = script.PendingDisplay!;
        Assert.Equal(0, request.Context.Color);
        Assert.Equal(0, request.Context.PortraitId);
        Assert.Equal(62, Assert.Single(request.MessageReferences).StartMessageId);
        Assert.Equal(0, Assert.Single(request.MessageReferences).Span);
        request.Display.Update(24, false, false);
        request.Display.Update(0, true, false);
        Assert.True(script.AcknowledgeDisplay(request.Id));
        Assert.Equal(3, script.ReturnEntry);
    }

    [Fact]
    public void Unsupported_later_text_control_fails_preflight_with_event_location_not_after_partial_display()
    {
        var language = new DialogueLanguagePackage("en", [new DialogueMessageBlock(61, 62, [
            DialogueMessagePart.FromText("A"), DialogueMessagePart.FromText("~01B")])]);
        var script = new TriggerScriptSession(new EventFixture().Load(), language);
        var error = Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 60));
        Assert.Contains("shared.json", error.Message);
        Assert.Contains("L0002", error.Message);
        Assert.Contains("0002", error.Message);
        Assert.Contains("~", error.Message);
        Assert.Null(script.PendingDisplay);
        Assert.Equal(TriggerScriptPhase.Ready, script.Phase);
    }

    [Fact]
    public void Consumers_can_preflight_all_glyphs_from_the_public_request_without_advancing_actual_display()
    {
        var script = new TriggerScriptSession(new EventFixture().Load(), Language());
        script.Start("shared", "npc", 60);
        var request = script.PendingDisplay!;
        Assert.Equal("AB", Assert.Single(request.Content.Parts).Text);
        var preview = new DialogueDisplaySession(request.Content, request.Context.Color, 0, request.Context.DelayMilliseconds);
        preview.Update(0, true, true);
        Assert.Equal("AB", preview.Current.VisibleText);
        Assert.Empty(request.Display.Current.VisibleText);
    }

    [Fact]
    public void Required_missing_successor_is_rejected_before_any_display()
    {
        var fixture = new EventFixture();
        fixture.Commands[4] = fixture.Commands[2]!.DeepClone();
        fixture.Commands[4]!["label"] = "L0004";
        fixture.Commands[4]!["source"]!["index"] = 4;
        fixture.Commands[4]!["messageId"] = 90;
        fixture.Commands[4]!["next"] = null;
        var script = new TriggerScriptSession(fixture.Load(), Language());
        var error = Assert.Throws<InvalidDataException>(() => script.Start("shared", "npc", 60));
        Assert.Contains("shared.json#L0004 @0004", error.Message);
        Assert.Contains("next", error.Message);
        Assert.Null(script.PendingDisplay);
    }

    [Fact]
    public void Raw_after_an_unconditional_keep_end_is_archived_but_not_in_the_selected_closure()
    {
        var fixture = new EventFixture();
        fixture.Commands[3] = fixture.Commands[4]!.DeepClone();
        fixture.Commands[3]!["label"] = "L0003";
        fixture.Commands[3]!["source"]!["index"] = 3;
        fixture.Commands[3]!["next"] = "L0004";
        fixture.Commands[4] = fixture.Commands[0]!.DeepClone();
        fixture.Commands[4]!["label"] = "L0004";
        fixture.Commands[4]!["source"]!["index"] = 4;
        fixture.Commands[4]!["next"] = null;
        var language = new DialogueLanguagePackage("en", [new DialogueMessageBlock(61, 61, [DialogueMessagePart.FromText("A")])]);
        var script = new TriggerScriptSession(fixture.Load(), language);
        script.Start("shared", "npc", 60);
        var request = script.PendingDisplay!;
        request.Display.Update(24, false, false);
        request.Display.Update(0, true, false);
        Assert.True(script.AcknowledgeDisplay(request.Id));
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(3, script.CurrentPosition!.SourceAddress);
        Assert.Equal(1, script.ReturnEntry);
    }

    [Fact]
    public void CLEAR_without_body_lines_is_not_a_player_or_display_wait_boundary()
    {
        var language = new DialogueLanguagePackage("en", [new DialogueMessageBlock(61, 62, [
            DialogueMessagePart.Clear, DialogueMessagePart.FromText("NPC:"), DialogueMessagePart.Clear])]);
        var script = new TriggerScriptSession(new EventFixture().Load(), language);
        script.Start("shared", "npc", 60);
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Null(script.PendingDisplay);
        Assert.False(script.BlocksSceneUpdates);
        Assert.Equal(1, script.ReturnEntry);
    }

    internal static EventFixture TwoDisplays()
    {
        var fixture = new EventFixture();
        fixture.Commands[3] = fixture.Commands[1]!.DeepClone();
        fixture.Commands[3]!["label"] = "L0003";
        fixture.Commands[3]!["source"]!["index"] = 3;
        fixture.Commands[3]!["portraitId"] = 0;
        fixture.Commands[3]!["next"] = "L0004";
        var end = fixture.Commands[4]!.DeepClone();
        fixture.Commands[4] = fixture.Commands[2]!.DeepClone();
        fixture.Commands[4]!["label"] = "L0004";
        fixture.Commands[4]!["source"]!["index"] = 4;
        fixture.Commands[4]!["messageId"] = 90;
        fixture.Commands[4]!["next"] = "L0005";
        end["label"] = "L0005";
        end["source"]!["index"] = 5;
        fixture.Commands.Add(end);
        fixture.Index["source"]!["instructionCount"] = 6;
        fixture.Index["source"]!["byteLength"] = 48;
        return fixture;
    }

    internal static DialogueLanguagePackage Language() => new("zh-Hans", [new DialogueMessageBlock(61, 62, [DialogueMessagePart.FromText("AB")])]);

    internal static EventFixture RealShape()
    {
        var fixture = new EventFixture();
        var target = fixture.Commands.Skip(1).Select(n => n!.DeepClone()).ToArray();
        fixture.Commands.Clear();
        for (int address = 0; address < 0x0188; address++)
            fixture.Commands.Add(new JsonObject { ["label"] = $"L{address:X4}", ["op"] = "raw", ["next"] = $"L{address + 1:X4}",
                ["source"] = new JsonObject { ["index"] = address, ["opcode"] = 134, ["operands"] = new JsonArray(0, 65535, 8) } });
        for (int i = 0; i < target.Length; i++)
        {
            target[i]!["label"] = $"L{0x0188 + i:X4}";
            target[i]!["source"]!["index"] = 0x0188 + i;
            target[i]!["next"] = i == 3 ? null : $"L{0x0189 + i:X4}";
            fixture.Commands.Add(target[i]);
        }
        fixture.Entry["target"] = "L0188";
        fixture.Entry["sourceAddress"] = 0x0188;
        fixture.Index["source"]!["instructionCount"] = 396;
        fixture.Index["source"]!["byteLength"] = 3168;
        return fixture;
    }
}
