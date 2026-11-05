using Pal.Extract;
using Xunit;
using static Pal.Extract.Tests.OpcodeCounterexampleHarness;

namespace Pal.Extract.Tests;

// M5 opcode batch 1 (#96): basic control-flow opcodes 0x0000-0x000A, following the
// #78 counter-example skeleton. Jump-class opcodes also prove that reference
// fields resolve to real addresses (dangling targets are rejected), and the
// zero-target rule from events-schema 3.4: operand target 0 still references
// the label at index 0 and re-encodes as WORD 0.
public sealed class OpcodeControlFlowBatch1Tests
{
    // --- 0x0000 end keep: no mapped operands; mode is a fixed constant. ---
    [Fact]
    public void End_keep_round_trips_preserved_operands_and_rejects_mode_edits()
    {
        byte[] original = Words(0x0000, 5, 6, 7);
        var command = Decode(0, 0x0000, 5, 6, 7);
        Assert.Equal("end", (string)command["op"]!);
        Assert.Equal("keep", (string)command["mode"]!);

        Assert.Equal(original, Reencode(command)); // no mapped fields: preserved WORDs, byte-exact

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0000, 5, 6, 7), "mode", "advance"));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0000, 5, 6, 7), "mode", "redirect"));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0000, 5, 6, 7), "to", "L0001"));
    }

    // --- 0x0001 end advance: stops the run but replaces the stored entry with the next line. ---
    [Fact]
    public void End_advance_round_trips_preserved_operands_and_rejects_mode_edits()
    {
        byte[] original = Words(0x0001, 8, 9, 10);
        var command = Decode(0, 0x0001, 8, 9, 10);
        Assert.Equal("end", (string)command["op"]!);
        Assert.Equal("advance", (string)command["mode"]!);

        Assert.Equal(original, Reencode(command));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0001, 8, 9, 10), "mode", "keep"));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0001, 8, 9, 10), "mode", "redirect"));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0001, 8, 9, 10), "limit", 1));
    }

    // --- 0x0002 end redirect: stops with resume target `to`, guarded by the idle `limit`. ---
    [Fact]
    public void End_redirect_edits_limit_and_to_and_rejects_dangling_targets()
    {
        byte[] original = Words(0x0002, 2, 3, 7);
        var command = Decode(0, 0x0002, 2, 3, 7);
        Assert.Equal("end", (string)command["op"]!);
        Assert.Equal("redirect", (string)command["mode"]!);
        Assert.Equal("L0002", (string)command["to"]!);
        Assert.Equal(3, (int)command["limit"]!);

        Assert.Equal(Words(0x0002, 2, 5, 7), EditAndReencode(command, "limit", 5)); // operand 1 re-encoded
        Assert.Equal(Words(0x0002, 4, 3, 7), EditAndReencode(Decode(0, 0x0002, 2, 3, 7), "to", "L0004")); // operand 0 resolved

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0002, 2, 3, 7), "limit", 5));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0002, 2, 3, 7), "to", "L0100")); // dangling
        var missing = Decode(0, 0x0002, 2, 3, 7);
        missing.AsObject().Remove("to");
        Assert.Throws<InvalidDataException>(() => Reencode(missing));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0002, 2, 3, 7), "mode", "keep")); // wrong end variant
        // Out-of-range words are rejected by the generic Word encode path's checked cast.
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0002, 2, 3, 7), "limit", 65_536));
    }

    // --- 0x0003 goto: unconditional jump target with the idle `limit` fail-safe. ---
    [Fact]
    public void Goto_edits_to_and_limit_and_rejects_dangling_targets()
    {
        byte[] original = Words(0x0003, 2, 3, 7);
        var command = Decode(0, 0x0003, 2, 3, 7);
        Assert.Equal("goto", (string)command["op"]!);
        Assert.Equal("L0002", (string)command["to"]!);
        Assert.Equal(3, (int)command["limit"]!);

        byte[] edited = EditAndReencode(command, "to", "L0005");
        Assert.NotEqual(original, edited);
        Assert.Equal(Words(0x0003, 5, 3, 7), edited);

        Assert.Equal(Words(0x0003, 2, 0, 7), EditAndReencode(Decode(0, 0x0003, 2, 3, 7), "limit", 0)); // 0 = always take the branch

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0003, 2, 3, 7), "to", "L0100"));
        var missing = Decode(0, 0x0003, 2, 3, 7);
        missing.AsObject().Remove("to");
        Assert.Throws<InvalidDataException>(() => Reencode(missing));
    }

    // --- 0x0004 call: invokes a trigger script; operand 1 selects the event object context (0 = inherit). ---
    [Fact]
    public void Call_edits_event_object_context_and_target_and_rejects_dangling_targets()
    {
        byte[] original = Words(0x0004, 2, 5, 7);
        var command = Decode(0, 0x0004, 2, 5, 7);
        Assert.Equal("call", (string)command["op"]!);
        Assert.Equal("L0002", (string)command["to"]!);
        Assert.Equal(5, (int)command["eventObjectId"]!);

        byte[] edited = EditAndReencode(command, "eventObjectId", 9);
        Assert.NotEqual(original, edited);
        Assert.Equal(Words(0x0004, 2, 9, 7), edited); // operand 2 stays the source WORD 7

        var inherited = Decode(0, 0x0004, 2, 5, 7);
        inherited["to"] = "L0003";
        inherited["eventObjectId"] = 0; // 0 = run the called script with the caller's event object id
        Assert.Equal(Words(0x0004, 3, 0, 7), Reencode(inherited));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0004, 2, 5, 7), "to", "L0100"));
        var missing = Decode(0, 0x0004, 2, 5, 7);
        missing.AsObject().Remove("to");
        Assert.Throws<InvalidDataException>(() => Reencode(missing));
        // Out-of-range words are rejected by the generic Word encode path's checked cast.
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0004, 2, 5, 7), "eventObjectId", 65_536));
    }

    // --- 0x0006 randomJump (re-verified this batch): rate operand and the zero-target rule. ---
    [Fact]
    public void RandomJump_edits_rate_and_encodes_zero_targets_as_the_index_zero_label()
    {
        byte[] original = Words(0x0006, 75, 2, 0);
        var command = Decode(0, 0x0006, 75, 2, 0);
        Assert.Equal("randomJump", (string)command["op"]!);
        Assert.Equal(75, (int)command["rate"]!);

        byte[] edited = EditAndReencode(command, "rate", 30);
        Assert.NotEqual(original, edited);
        Assert.Equal(Words(0x0006, 30, 2, 0), edited);

        // Real-data pattern (96 of 235 instances): target WORD 0 still references L0000 and re-encodes as 0.
        Assert.Equal(Words(0x0006, 75, 0, 0), EditAndReencode(Decode(0, 0x0006, 75, 2, 0), "to", "L0000"));

        // Out-of-range words are rejected by the generic Word encode path's checked cast.
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0006, 75, 2, 0), "rate", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0006, 75, 2, 0), "rate", -1));
    }

    // --- 0x0008 advanceEntry: no mapped operands; the stored entry becomes the next instruction. ---
    [Fact]
    public void AdvanceEntry_round_trips_preserved_operands_and_rejects_unknown_fields()
    {
        byte[] original = Words(0x0008, 5, 6, 7);
        var command = Decode(0, 0x0008, 5, 6, 7);
        Assert.Equal("advanceEntry", (string)command["op"]!);

        Assert.Equal(original, Reencode(command)); // no mapped fields: byte-exact

        Assert.Throws<InvalidDataException>(() => EditAndReencode(command, "mode", "next"));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(command, "to", "L0001"));
    }

    // --- 0x0009 waitUpdate (re-verified this batch): all three WORD operands remap. ---
    [Fact]
    public void WaitUpdate_edits_frames_and_update_flags()
    {
        byte[] original = Words(0x0009, 10, 1, 1);
        var command = Decode(0, 0x0009, 10, 1, 1);
        Assert.Equal("waitUpdate", (string)command["op"]!);
        Assert.Equal(10, (int)command["frames"]!);
        Assert.Equal(1, (int)command["runTriggers"]!);
        Assert.Equal(1, (int)command["updateGestures"]!);

        byte[] edited = EditAndReencode(command, "frames", 20);
        Assert.NotEqual(original, edited);
        Assert.Equal(Words(0x0009, 20, 1, 1), edited);

        Assert.Equal(Words(0x0009, 10, 0, 1), EditAndReencode(Decode(0, 0x0009, 10, 1, 1), "runTriggers", 0));
        Assert.Equal(Words(0x0009, 10, 1, 0), EditAndReencode(Decode(0, 0x0009, 10, 1, 1), "updateGestures", 0));
        Assert.Equal(Words(0x0009, 0, 1, 1), EditAndReencode(Decode(0, 0x0009, 10, 1, 1), "frames", 0)); // 0 -> one frame in trigger
        // Out-of-range words are rejected by the generic Word encode path's checked cast.
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0009, 10, 1, 1), "frames", 65_536));
    }

    // --- 0x000A confirmNoJump: jumps to the target when the player selects No. ---
    [Fact]
    public void ConfirmNoJump_edits_target_and_rejects_dangling_targets()
    {
        byte[] original = Words(0x000A, 3, 0, 9);
        var command = Decode(0, 0x000A, 3, 0, 9);
        Assert.Equal("confirmNoJump", (string)command["op"]!);
        Assert.Equal("L0003", (string)command["to"]!);

        byte[] edited = EditAndReencode(command, "to", "L0005");
        Assert.NotEqual(original, edited);
        Assert.Equal(Words(0x000A, 5, 0, 9), edited); // operands 1 and 2 keep their source WORDs

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x000A, 3, 0, 9), "to", "L0100"));
        var missing = Decode(0, 0x000A, 3, 0, 9);
        missing.AsObject().Remove("to");
        Assert.Throws<InvalidDataException>(() => Reencode(missing));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x000A, 3, 0, 9), "to", "shared##L0005"));
    }
}
