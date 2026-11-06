using Pal.Extract;
using Pal.Core;
using System.Text.Json.Nodes;
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
    [Fact]
    public void Complete_M5_registry_decode_remains_compatible_with_Core_named_content_without_runtime_tool_dependency()
    {
        // Independent frozen165-code expectation from events-schema master table. This is a
        // cross-contract guard, not the semantic oracle: Core's complete manual shell test
        // supplies that separately. Never derive the only coverage expectation from ByOpcode.
        int[] expectedOpcodes =
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
        Assert.Equal(165, expectedOpcodes.Distinct().Count());
        Assert.Equal(expectedOpcodes, OpcodeRegistry.ByOpcode.Keys.Order().ToArray());
        var commands = new JsonArray();
        for (int address = 0; address < expectedOpcodes.Length; address++)
        {
            int opcode = expectedOpcodes[address];
            //1,1,1 is independently legal even for the narrower existing typed Core
            // domains (h<=1, memberIndex<=4, slotIndex<=5), not a reason to widen them.
            commands.Add(OpcodeRegistry.DecodeCommand(address, 165, (ushort)opcode, 1, 1, 1));
        }
        var index = JsonNode.Parse("""
            {"formatVersion":1,"profile":"faithful","source":{"archive":"SSS.MKF","chunk":4,"byteOrder":"little","instructionCount":165,"byteLength":1320,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},"files":["shared"]}
            """)!;
        var file = new JsonObject
        {
            ["formatVersion"] = 1, ["profile"] = "faithful", ["fileId"] = "shared",
            ["entries"] = new JsonArray(), ["commands"] = commands
        };
        var events = TriggerEventCollection.Parse(index.ToJsonString(),
            new Dictionary<string, string> { ["shared"] = file.ToJsonString() });
        Assert.Equal(165, events.InstructionCount);
        for (int address = 0; address < 165; address++)
        {
            int opcode = expectedOpcodes[address];
            Assert.Equal(OpcodeRegistry.ByOpcode[opcode].Op,
                events.GetCommandContent(address).GetProperty("op").GetString());
            Assert.NotEqual("raw", events.GetCommandContent(address).GetProperty("op").GetString());
            Assert.True(JsonNode.DeepEquals(commands[address],
                JsonNode.Parse(events.GetCommandContent(address).GetRawText())));
            Assert.Equal(new TriggerPosition("shared", $"L{address:X4}", address), events.GetSource(address).Position);
            Assert.Equal(opcode, events.GetSource(address).Opcode);
            Assert.Equal(new[] { 1, 1, 1 }, events.GetSource(address).Operands);
        }
    }

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
