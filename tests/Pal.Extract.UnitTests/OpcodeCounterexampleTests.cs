using Pal.Extract;
using Xunit;
using static Pal.Extract.Tests.OpcodeCounterexampleHarness;

namespace Pal.Extract.Tests;

// Reusable counter-example skeleton for the M5 opcode batches: decode a named
// command from hand-built little-endian bytes, edit one semantic field, then
// validate + re-encode through the registry. The re-encoded bytes must differ
// from the source bytes (proving real re-encoding, not copying source WORDs)
// and equal an independently hand-computed expectation. Unknown fields and
// out-of-range values must be rejected. Follow-up opcode tickets reuse Run().
public sealed class OpcodeCounterexampleTests
{
    // --- Representative case 1: a signed field (changeMp.amount). ---
    [Fact]
    public void Signed_amount_edit_reencodes_as_twos_complement_and_rejects_out_of_range()
    {
        byte[] original = Words(0x001C, 0, 100, 0);
        var command = Decode(0, 0x001C, 0, 100, 0);
        Assert.Equal("changeMp", (string)command["op"]!);
        Assert.Equal(100, (int)command["amount"]!);

        byte[] edited = EditAndReencode(command, "amount", -300);

        Assert.NotEqual(original, edited); // operand 1 really re-encoded, not copied from source
        Assert.Equal(Words(0x001C, 0, 65_236, 0), edited); // -300 as WORD

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x001C, 0, 100, 0), "amount", 32_768));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x001C, 0, 100, 0), "amount", -32_769));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x001C, 0, 100, 0), "bogus", 1));
    }

    // --- Representative case 2: a reference field (randomJump.to). ---
    [Fact]
    public void Reference_edit_reencodes_the_resolved_address_and_rejects_dangling_targets()
    {
        byte[] original = Words(0x0006, 75, 2, 0);
        var command = Decode(0, 0x0006, 75, 2, 0);
        Assert.Equal("randomJump", (string)command["op"]!);
        Assert.Equal("L0002", (string)command["to"]!);

        byte[] edited = EditAndReencode(command, "to", "L0005");

        Assert.NotEqual(original, edited);
        Assert.Equal(Words(0x0006, 75, 5, 0), edited); // resolved label address, not source operand 2

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0006, 75, 2, 0), "to", "L0100")); // dangling
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0006, 75, 2, 0), "to", "shared##L0005")); // malformed
        var missing = Decode(0, 0x0006, 75, 2, 0);
        missing.AsObject().Remove("to");
        Assert.Throws<InvalidDataException>(() => Reencode(missing));
    }

    // --- Representative case 3: a low-byte field with the high byte preserved (setDialog.color). ---
    [Fact]
    public void Low_byte_edit_reencodes_the_color_and_preserves_the_source_high_byte()
    {
        // setDialog upper: portrait 59, operand 1 = 0x4F79 -> color 0x79, unmapped high byte 0x4F.
        byte[] original = Words(0x003C, 59, 0x4F79, 1);
        var command = Decode(0, 0x003C, 59, 0x4F79, 1);
        Assert.Equal("setDialog", (string)command["op"]!);
        Assert.Equal("upper", (string)command["box"]!);
        Assert.Equal(0x79, (int)command["color"]!);

        byte[] edited = EditAndReencode(command, "color", 200);

        Assert.NotEqual(original, edited);
        Assert.Equal(Words(0x003C, 59, 0x4FC8, 1), edited); // new low byte 0xC8, source high byte 0x4F preserved

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x003C, 59, 0x4F79, 1), "color", 256));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x003C, 59, 0x4F79, 1), "color", -1));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x003C, 59, 0x4F79, 1), "box", "middle"));
    }

    // --- Battle-target references (startBattle) keep the null/zero and coupling rules on edit. ---
    [Fact]
    public void Battle_target_edit_reencodes_loss_target_and_keeps_the_boss_coupling()
    {
        byte[] original = Words(0x0007, 65535, 0, 0);
        var command = Decode(0, 0x0007, 65535, 0, 0);
        Assert.Equal("startBattle", (string)command["op"]!);
        Assert.Equal(65_535, (int)command["enemyTeamId"]!);

        command["lossTarget"] = "L0003";
        byte[] edited = Reencode(command);

        Assert.NotEqual(original, edited);
        Assert.Equal(Words(0x0007, 65_535, 3, 0), edited);

        // A non-boss result target must pair isBoss=false with a non-null fleeTarget.
        command["isBoss"] = false;
        command["fleeTarget"] = "L0004";
        Assert.Equal(Words(0x0007, 65_535, 3, 4), Reencode(command));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0007, 65535, 0, 0), "lossTarget", "L0000")); // address zero must use null
        var contradicted = Decode(0, 0x0007, 65535, 0, 0);
        contradicted["isBoss"] = false; // contradicts fleeTarget being null
        Assert.Throws<InvalidDataException>(() => Reencode(contradicted));
    }

    // --- Unregistered opcodes stay raw and round-trip their source WORDs unchanged.
    //     0x0011 was the sample until #105 registered it as walkToSlow; 0x0032 has no case
    //     anywhere in script.c, so it stays raw. ---
    [Fact]
    public void Unregistered_opcode_decodes_raw_and_round_trips_source_operands()
    {
        byte[] original = Words(0x0032, 0, 65_535, 8);
        var command = Decode(0, 0x0032, 0, 65_535, 8);
        Assert.Equal("raw", (string)command["op"]!);
        Assert.Equal(original, Reencode(command));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(command, "mode", "keep")); // raw carries no semantic fields
    }
}
