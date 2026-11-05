using System.Buffers.Binary;
using System.Text.Json.Nodes;
using Pal.Extract;
using Xunit;

namespace Pal.Extract.Tests;

// M5 opcode scene-movement batch 1 (#105, movement part 1) - walkStep 0x000B-0x000E, face 0x000F,
// walkTo 0x0010, walkToSlow 0x0011 (new), setObjectPositionRelativeToParty 0x0012 (new),
// setObjectPosition 0x0013 and setObjectGesture 0x0014. Topology per script.c: all ten opcode bits
// live in the shared PAL_InterpretInstruction switch (script.c:650; trigger switch 3206-3440 and
// auto switch 3522-3641 have no case for any of them), and every case breaks into the sequential
// tail (script.c:3085 return wScriptEntry + 1) - the walkTo/walkToSlow arrival loops re-execute the
// same entry via wScriptEntry-- (script.c:684/697/702), a runtime stall rather than a jump.
// Addressing per the reference cases:
//   - 0x000B-0x000E fall through one "walk one step" block: direction = opcode - 0x000B
//     (script.c:659, kDirSouth=0/West=1/North=2/East=3 per palcommon.h:92-95); the caller object
//     pEvtObj/wEventObjectID walks (script.c:660); no operand is consumed by the case body.
//   - 0x000F sets the caller object's direction/frame unless the operand is the 0xFFFF no-change
//     sentinel (script.c:667-674); 0 is an explicit direction/frame, not the sentinel.
//   - 0x0010/0x0011 walk the caller object (wEventObjectID, not the preamble-selected pCurrent) to
//     absolute map coordinates at speeds 3/2 (script.c:681/694), 0x0011 only on frames whose parity
//     differs from the object id (script.c:692).
//   - 0x0013/0x0012 write pCurrent->x/y - the event object selected by the shared preamble from
//     operand 0 (0/0xFFFF = caller, else operand-1 1-based, script.c:626-638); 0x0013 is absolute,
//     0x0012 adds the offsets to the party's map position (viewport + partyoffset, script.c:710-713).
//   - 0x0014 sets the caller object's frame and hardcodes direction kDirSouth (script.c:728-729);
//     there is no direction or object operand.
// Signedness: no case has a (SHORT) cast; every numeric field is an unsigned WORD (0x000F keeps
// 0xFFFF sentinels as plain WORDs; 0x0012's real data encodes the -128 party-relative offset as
// 0xFF80, preserved as the unsigned WORD like the reference store into EVENTOBJECT.x/y).
// Counter-examples follow the #78/#96 skeleton: edit a semantic field -> re-encoded bytes differ
// from the source bytes and equal the hand-computed value; unknown fields and out-of-range values
// are rejected.
public sealed class OpcodeSceneMovementBatch1Tests
{
    private const int ChunkCount = 8;

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Labels =
        new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            ["shared"] = Enumerable.Range(0, ChunkCount).ToDictionary(OpcodeRegistry.LabelAt, address => address),
        };

    private static byte[] Words(params int[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), unchecked((ushort)values[i]));
        return bytes;
    }

    private static JsonObject Decode(int index, params int[] instruction)
    {
        ushort opcode = unchecked((ushort)instruction[0]);
        ushort a = unchecked((ushort)instruction[1]), b = unchecked((ushort)instruction[2]), c = unchecked((ushort)instruction[3]);
        return OpcodeRegistry.DecodeCommand(index, ChunkCount, opcode, a, b, c);
    }

    private static byte[] Reencode(JsonObject command)
    {
        OpcodeRegistry.ValidateCommand(command, "shared", ChunkCount);
        var bytes = new byte[8];
        OpcodeRegistry.EncodeCommand(command, "shared", Labels, bytes);
        return bytes;
    }

    private static byte[] EditAndReencode(JsonObject command, string field, JsonNode? value)
    {
        command[field] = value;
        return Reencode(command);
    }

    // --- 0x000B-0x000E walkStep (shared, script.c:652-661): one fall-through "walk one step"
    //     block - pEvtObj->wDirection = opcode - 0x000B (0..3 = south/west/north/east), then
    //     PAL_NPCWalkOneStep(wEventObjectID, 2) walks the caller object; no operand is consumed by
    //     the case body (real data: all three operands are 0 in every hit). ---
    [Fact]
    public void WalkStep_family_decodes_opcode_offset_direction_and_round_trips_byte_exact()
    {
        int[] opcodes = [0x000B, 0x000C, 0x000D, 0x000E];
        for (int direction = 0; direction < 4; direction++)
        {
            byte[] original = Words(opcodes[direction], 0, 0, 0);
            var command = Decode(direction, opcodes[direction], 0, 0, 0);
            Assert.Equal("walkStep", (string)command["op"]!);
            Assert.Equal(direction, (int)command["direction"]!);

            Assert.Equal(original, Reencode(command)); // direction re-derives the opcode: byte-exact
        }

        // Editing direction away from the source opcode contradicts the opcodeOffset binding.
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x000B, 0, 0, 0), "direction", 2)); // opcode stays 0x000B
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x000D, 0, 0, 0), "direction", 4)); // no opcode maps to 4
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x000E, 0, 0, 0), "direction", -1)); // below the base
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x000C, 0, 0, 0), "x", 24)); // unknown field: no coordinate operands
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x000C, 0, 0, 0), "directionWord", 1)); // unknown field name
    }

    // --- 0x000F face (shared, script.c:663-675): set the caller object's direction and/or frame -
    //     each operand is applied unless it is the 0xFFFF no-change sentinel (667/671); 0 is an
    //     explicit direction/frame, not a sentinel. Real data: direction 0..3 plus 0xFFFF x5,
    //     frame 0..23. ---
    [Fact]
    public void Face_edits_direction_and_frame_and_keeps_the_ffff_sentinels_as_words()
    {
        byte[] original = Words(0x000F, 2, unchecked((int)0xFFFF), 5);
        var command = Decode(0, 0x000F, 2, unchecked((int)0xFFFF), 5);
        Assert.Equal("face", (string)command["op"]!);
        Assert.Equal(2, (int)command["direction"]!);
        Assert.Equal(65_535, (int)command["frame"]!); // no-change sentinel kept as the plain WORD

        Assert.Equal(Words(0x000F, 3, unchecked((int)0xFFFF), 5), EditAndReencode(command, "direction", 3)); // north -> east
        Assert.Equal(Words(0x000F, 0, unchecked((int)0xFFFF), 5), EditAndReencode(Decode(0, 0x000F, 2, unchecked((int)0xFFFF), 5), "direction", 0)); // explicit south, not the sentinel
        Assert.Equal(Words(0x000F, 2, 23, 5), EditAndReencode(Decode(0, 0x000F, 2, unchecked((int)0xFFFF), 5), "frame", 23)); // real-data frame top
        Assert.Equal(Words(0x000F, 2, 0, 5), EditAndReencode(Decode(0, 0x000F, 2, unchecked((int)0xFFFF), 5), "frame", 0)); // explicit frame 0
        Assert.Equal(Words(0x000F, unchecked((int)0xFFFF), 0, 5), EditAndReencode(Decode(0, 0x000F, 2, 0, 5), "direction", 65_535)); // sentinel form re-encodes

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x000F, 2, unchecked((int)0xFFFF), 5), "direction", 3));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x000F, 2, 0, 5), "gesture", 1)); // unknown field (setPartyFacing's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x000F, 2, 0, 5), "eventObjectId", 9)); // unknown field: acts on the caller object
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x000F, 2, 0, 5), "direction", -1)); // unsigned WORD, no SHORT cast
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x000F, 2, 0, 5), "direction", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x000F, 2, 0, 5), "frame", 65_536));
    }

    // --- 0x0010 walkTo (shared, script.c:677-686): walk the caller object straight to the
    //     absolute map position (x, y, h) at speed 3; until arrival the entry re-executes
    //     (wScriptEntry--), a runtime stall rather than a jump. Real data: x 5..55, y 7..116,
    //     h 0/1. ---
    [Fact]
    public void WalkTo_edits_coordinates_and_height()
    {
        byte[] original = Words(0x0010, 24, 60, 1);
        var command = Decode(0, 0x0010, 24, 60, 1);
        Assert.Equal("walkTo", (string)command["op"]!);
        Assert.Equal(24, (int)command["x"]!);
        Assert.Equal(60, (int)command["y"]!);
        Assert.Equal(1, (int)command["h"]!);

        Assert.Equal(Words(0x0010, 40, 60, 1), EditAndReencode(command, "x", 40));
        Assert.Equal(Words(0x0010, 24, 116, 1), EditAndReencode(Decode(0, 0x0010, 24, 60, 1), "y", 116)); // real-data top
        Assert.Equal(Words(0x0010, 24, 60, 0), EditAndReencode(Decode(0, 0x0010, 24, 60, 1), "h", 0)); // the other real-data layer form
        Assert.Equal(Words(0x0010, 0, 60, 1), EditAndReencode(Decode(0, 0x0010, 24, 60, 1), "x", 0)); // zero is a legal WORD
        Assert.Equal(Words(0x0010, 65_535, 60, 1), EditAndReencode(Decode(0, 0x0010, 24, 60, 1), "x", 65_535)); // full unsigned WORD

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0010, 24, 60, 1), "y", 61));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0010, 24, 60, 1), "xOffset", 24)); // unknown field (0x0012's party-relative name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0010, 24, 60, 1), "speed", 3)); // unknown field: speed 3 is a runtime constant
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0010, 24, 60, 1), "frame", 24)); // unknown field (face's name)
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0010, 24, 60, 1), "x", -1)); // unsigned WORD, no SHORT cast
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0010, 24, 60, 1), "h", 65_536));
    }

    // --- 0x0011 walkToSlow (new, shared, script.c:688-704): same x/y/h operands as walkTo but at a
    //     lower speed - the frame-parity gate ((wEventObjectID & 1) ^ (dwFrameNum & 1), 692) walks
    //     only every other frame and PAL_NPCWalkTo runs at speed 2 instead of 3 (694); like walkTo,
    //     until arrival the entry re-executes (wScriptEntry-- 697/702). Real data: x 1..57,
    //     y 7..115, h 0/1. ---
    [Fact]
    public void WalkToSlow_edits_coordinates_and_stays_opcode_0x0011()
    {
        byte[] original = Words(0x0011, 33, 48, 0);
        var command = Decode(0, 0x0011, 33, 48, 0);
        Assert.Equal("walkToSlow", (string)command["op"]!);
        Assert.Equal(33, (int)command["x"]!);
        Assert.Equal(48, (int)command["y"]!);
        Assert.Equal(0, (int)command["h"]!);

        Assert.Equal(Words(0x0011, 35, 48, 0), EditAndReencode(command, "x", 35));
        Assert.Equal(Words(0x0011, 33, 50, 0), EditAndReencode(Decode(0, 0x0011, 33, 48, 0), "y", 50));
        Assert.Equal(Words(0x0011, 33, 48, 1), EditAndReencode(Decode(0, 0x0011, 33, 48, 0), "h", 1)); // the other real-data layer form
        Assert.Equal(Words(0x0011, 0, 48, 0), EditAndReencode(Decode(0, 0x0011, 33, 48, 0), "x", 0));
        Assert.Equal(Words(0x0011, 65_535, 48, 0), EditAndReencode(Decode(0, 0x0011, 33, 48, 0), "x", 65_535));

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0011, 33, 48, 0), "x", 34));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0011, 33, 48, 0), "speed", 2)); // unknown field: speeds 2/3 are runtime constants
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0011, 33, 48, 0), "eventObjectId", 33)); // unknown field: walker is the caller, x is a coordinate
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0011, 33, 48, 0), "to", "L0001")); // unknown field: the retry loop is not a jump
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0011, 33, 48, 0), "y", -1)); // unsigned WORD, no SHORT cast
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0011, 33, 48, 0), "h", 65_536));
    }

    // --- 0x0012 setObjectPositionRelativeToParty (new, shared, script.c:706-714): place the
    //     operand-0-selected event object (shared preamble pCurrent: 0/0xFFFF = caller, else
    //     operand-1 as a 1-based index) at xOffset + viewport.x + partyoffset.x, yOffset likewise -
    //     offsets relative to the party's map position. The store is the unsigned WORD x/y with no
    //     (SHORT) cast; the single real-data hit encodes the -128 offset as 0xFF80 next to +56. ---
    [Fact]
    public void SetObjectPositionRelativeToParty_edits_object_and_party_relative_offsets()
    {
        byte[] original = Words(0x0012, 84, unchecked((int)0xFF80), 56); // the real-data form
        var command = Decode(0, 0x0012, 84, unchecked((int)0xFF80), 56);
        Assert.Equal("setObjectPositionRelativeToParty", (string)command["op"]!);
        Assert.Equal(84, (int)command["eventObjectId"]!);
        Assert.Equal(65_408, (int)command["xOffset"]!); // 0xFF80: the wrapped encoding of -128
        Assert.Equal(56, (int)command["yOffset"]!);

        Assert.Equal(original, Reencode(command)); // all three WORDs re-encode byte-exact
        Assert.Equal(Words(0x0012, 0, unchecked((int)0xFF80), 56), EditAndReencode(command, "eventObjectId", 0)); // 0 = the caller's object
        Assert.Equal(Words(0x0012, unchecked((int)0xFFFF), unchecked((int)0xFF80), 56), EditAndReencode(Decode(0, 0x0012, 84, unchecked((int)0xFF80), 56), "eventObjectId", 65_535)); // 0xFFFF = the caller's object too
        Assert.Equal(Words(0x0012, 84, 0, 56), EditAndReencode(Decode(0, 0x0012, 84, unchecked((int)0xFF80), 56), "xOffset", 0)); // exactly at the party
        Assert.Equal(Words(0x0012, 84, unchecked((int)0xFF80), 64), EditAndReencode(Decode(0, 0x0012, 84, unchecked((int)0xFF80), 56), "yOffset", 64));

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0012, 84, unchecked((int)0xFF80), 56), "yOffset", 57));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0012, 84, 0, 56), "x", 65_408)); // unknown field (0x0013's absolute name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0012, 84, 0, 56), "y", 56)); // unknown field (0x0013's absolute name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0012, 84, 0, 56), "objectId", 84)); // unknown field (object union's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0012, 84, 0, 56), "h", 1)); // unknown field: no layer write in the case
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0012, 84, 0, 56), "xOffset", -128)); // unsigned WORD: the -128 intent must stay 0xFF80
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0012, 84, 0, 56), "eventObjectId", 65_536));
    }

    // --- 0x0013 setObjectPosition (shared, script.c:716-722): set the operand-0-selected event
    //     object's absolute map position (pCurrent->x/y = operands 1/2; no layer write). The
    //     eventObjectId binding re-verified: pCurrent comes from the shared preamble (script.c:
    //     626-638, 0/0xFFFF = caller, else operand-1 1-based), the #78 rename precedent. Real
    //     data: eventObjectId 9..1783, x 85..1616, y 0..1712. ---
    [Fact]
    public void SetObjectPosition_edits_object_and_absolute_coordinates()
    {
        byte[] original = Words(0x0013, 28, 1280, 256);
        var command = Decode(0, 0x0013, 28, 1280, 256);
        Assert.Equal("setObjectPosition", (string)command["op"]!);
        Assert.Equal(28, (int)command["eventObjectId"]!);
        Assert.Equal(1280, (int)command["x"]!);
        Assert.Equal(256, (int)command["y"]!);

        Assert.Equal(Words(0x0013, 29, 1280, 256), EditAndReencode(command, "eventObjectId", 29));
        Assert.Equal(Words(0x0013, 0, 1280, 256), EditAndReencode(Decode(0, 0x0013, 28, 1280, 256), "eventObjectId", 0)); // 0 = the caller's object
        Assert.Equal(Words(0x0013, 65_535, 1280, 256), EditAndReencode(Decode(0, 0x0013, 28, 1280, 256), "eventObjectId", 65_535)); // 0xFFFF = the caller's object too
        Assert.Equal(Words(0x0013, 28, 1360, 256), EditAndReencode(Decode(0, 0x0013, 28, 1280, 256), "x", 1360)); // real-data values
        Assert.Equal(Words(0x0013, 28, 1280, 1376), EditAndReencode(Decode(0, 0x0013, 28, 1280, 256), "y", 1376)); // real-data values

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0013, 28, 1280, 256), "y", 257));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0013, 28, 1280, 256), "xOffset", 1280)); // unknown field (0x0012's relative name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0013, 28, 1280, 256), "h", 1)); // unknown field: the case writes no layer
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0013, 28, 1280, 256), "objectId", 28)); // unknown field (object union's name)
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0013, 28, 1280, 256), "x", -1)); // unsigned WORD, no SHORT cast
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0013, 28, 1280, 256), "eventObjectId", 65_536));
    }

    // --- 0x0014 setObjectGesture (shared, script.c:724-730): set the caller object's frame
    //     (pEvtObj->wCurrentFrameNum = operand 0) and hardcode direction kDirSouth (palcommon.h:
    //     92, value 0); there is no direction or event-object operand - the re-verification kept
    //     the single frame field. Real data: frame 0..35, operands 1/2 all 0. ---
    [Fact]
    public void SetObjectGesture_edits_frame_and_rejects_direction_and_object_fields()
    {
        byte[] original = Words(0x0014, 7, 0, 0);
        var command = Decode(0, 0x0014, 7, 0, 0);
        Assert.Equal("setObjectGesture", (string)command["op"]!);
        Assert.Equal(7, (int)command["frame"]!);

        Assert.Equal(Words(0x0014, 35, 0, 0), EditAndReencode(command, "frame", 35)); // real-data top
        Assert.Equal(Words(0x0014, 0, 0, 0), EditAndReencode(Decode(0, 0x0014, 7, 0, 0), "frame", 0));
        Assert.Equal(Words(0x0014, 65_535, 0, 0), EditAndReencode(Decode(0, 0x0014, 7, 0, 0), "frame", 65_535)); // full unsigned WORD (no sentinel in this case)

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0014, 7, 0, 0), "frame", 8));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0014, 7, 0, 0), "direction", 0)); // unknown field: direction is hardcoded kDirSouth
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0014, 7, 0, 0), "eventObjectId", 28)); // unknown field: acts on the caller object
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0014, 7, 0, 0), "gesture", 7)); // unknown field name
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0014, 7, 0, 0), "frame", -1)); // unsigned WORD, no SHORT cast
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0014, 7, 0, 0), "frame", 65_536));
    }
}
