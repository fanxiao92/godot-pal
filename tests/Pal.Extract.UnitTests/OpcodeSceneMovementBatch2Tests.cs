using System.Buffers.Binary;
using System.Text.Json.Nodes;
using Pal.Extract;
using Xunit;

namespace Pal.Extract.Tests;

// M5 opcode scene-movement batch 2 (#106, movement part 2) - setPartyFacing 0x0015 (review),
// setObjectFacing 0x0016 (new), teleportParty 0x0038 (new), rideEventObjectSlow 0x003F (new),
// rideEventObject 0x0044 (new), setPartyPos 0x0046 (review), chasePlayer 0x004C (new),
// pauseEnemyChasing 0x0062 (new), speedUpEnemyChasing 0x0063 (new), walkNpcStep 0x006C (new).
// Topology per script.c: all ten opcode bits live in the shared PAL_InterpretInstruction switch
// (trigger switch 3206-3440 and auto switch 3522-3641 have no case for any of them), every case
// appears exactly once, and all successors are sequential (break into the script.c:3085 tail
// return wScriptEntry + 1) except 0x0038 teleportParty, whose failure branch assigns
// wScriptEntry = operand0 - 1 (script.c:1569) - the plain-reference conditional jump of playerFlee.
// Addressing per the reference cases:
//   - 0x0015 writes the party globals: direction = operand 0, memberIndex = operand 2 selects
//     rgParty[slot] (0-based, script.c:736), and gesture = operand 1 is the offset inside the
//     direction's 3-frame row (rgParty[...].wFrame = direction * 3 + gesture, script.c:736-737).
//   - 0x0016 writes the shared-preamble pCurrent (0/0xFFFF = caller, else operand-1 1-based,
//     script.c:626-638): direction = operand 1, frame = operand 2 is the absolute
//     wCurrentFrameNum - operand 0 also gates the case: WORD 0 skips it entirely (script.c:745).
//   - 0x0038 runs the current scene's OnTeleport trigger script; in battle or without one it
//     marks the script failed and jumps to operand 0 (script.c:1556-1569).
//   - 0x003F/0x0044 ride the caller event object with the party to absolute tile coordinates
//     x/y/h at the constant speeds 2/4 (script.c:1608-1610/1651-1654, callee script.c:203).
//   - 0x0046 sets the party globals: viewport = (x*32 + h*16, y*16 + h*8) - partyoffset and the
//     party members are laid out from partyoffset by direction-dependent steps (script.c:1665-1702).
//   - 0x004C makes the caller monster chase: chaseRange (WORD 0 -> 8 at runtime), speed (0 -> 4),
//     floating truthy WORD ignores obstacles (script.c:1733-1752, callee script.c:310).
//   - 0x0062/0x0063 write the chase globals: cycles = operand 0 with the global chase range 0/3
//     (script.c:1967-1971/1975-1979); play.c:235-237 counts cycles down per game update.
//   - 0x006C moves the preamble-selected NPC by the signed pixel offsets - the batch's only
//     (SHORT) casts (script.c:2060-2061) - then animates one walk step (script.c:2062).
// Signedness: every numeric field is an unsigned WORD except 0x006C's xOffset/yOffset (the two
// (SHORT) casts; real data e.g. 65504 = -32, 65524 = -12). Counter-examples follow the #78/#96
// skeleton: edit a semantic field -> re-encoded bytes differ from the source bytes and equal the
// hand-computed value; unknown fields, dangling references and out-of-range values are rejected.
public sealed class OpcodeSceneMovementBatch2Tests
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

    // --- 0x0015 setPartyFacing (shared, script.c:732-738): wPartyDirection = operand0 and
    //     rgParty[operand2].wFrame = direction * 3 + operand1 - memberIndex is a 0-based party
    //     slot (rgParty[MAX_PLAYABLE_PLAYER_ROLES = 5], palcommon.h:48), gesture the 0..2 row
    //     offset of the direction's frames. Real data: direction 0..3, gesture 0..26 (frame top
    //     3*3+26 = 35, the same frame space as setObjectGesture), memberIndex 0..2. ---
    [Fact]
    public void SetPartyFacing_edits_direction_gesture_and_member_and_keeps_the_row_formula_fields()
    {
        byte[] original = Words(0x0015, 2, 5, 1);
        var command = Decode(0, 0x0015, 2, 5, 1);
        Assert.Equal("setPartyFacing", (string)command["op"]!);
        Assert.Equal(2, (int)command["direction"]!);
        Assert.Equal(5, (int)command["gesture"]!);
        Assert.Equal(1, (int)command["memberIndex"]!);

        Assert.Equal(Words(0x0015, 3, 5, 1), EditAndReencode(command, "direction", 3)); // frame row follows at runtime
        Assert.Equal(Words(0x0015, 2, 0, 1), EditAndReencode(Decode(0, 0x0015, 2, 5, 1), "gesture", 0)); // first pose in the row
        Assert.Equal(Words(0x0015, 2, 26, 1), EditAndReencode(Decode(0, 0x0015, 2, 5, 1), "gesture", 26)); // real-data gesture top
        Assert.Equal(Words(0x0015, 2, 5, 0), EditAndReencode(Decode(0, 0x0015, 2, 5, 1), "memberIndex", 0)); // lead member
        Assert.Equal(Words(0x0015, 2, 5, 2), EditAndReencode(Decode(0, 0x0015, 2, 5, 1), "memberIndex", 2)); // real-data top slot

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0015, 2, 5, 1), "gesture", 6));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0015, 2, 5, 1), "frame", 11)); // unknown field: 0x0016/0x000F's absolute-frame name
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0015, 2, 5, 1), "eventObjectId", 1)); // unknown field: party globals, not an event object
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0015, 2, 5, 1), "directionWord", 2)); // unknown field name
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0015, 2, 5, 1), "direction", -1)); // unsigned WORD, no SHORT cast
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0015, 2, 5, 1), "memberIndex", 65_536));
    }

    // --- 0x0016 setObjectFacing (new, shared, script.c:741-749): set the preamble-selected event
    //     object's direction and absolute frame - operand 0 selects pCurrent with the shared
    //     preamble shape (0/0xFFFF = caller, else 1-based index) and additionally gates the case:
    //     WORD 0 skips it entirely (script.c:745), the only caller-form value that does nothing.
    //     Real data: eventObjectId 0x3 / 0xFFFFx3 / 1-based 11..4810x724; direction 0x377,
    //     1x105, 2x105, 3x142 and one 4 (kDirUnknown, palcommon.h:96); frame 0..27. ---
    [Fact]
    public void SetObjectFacing_edits_object_direction_and_absolute_frame()
    {
        byte[] original = Words(0x0016, 84, 1, 6);
        var command = Decode(0, 0x0016, 84, 1, 6);
        Assert.Equal("setObjectFacing", (string)command["op"]!);
        Assert.Equal(84, (int)command["eventObjectId"]!);
        Assert.Equal(1, (int)command["direction"]!);
        Assert.Equal(6, (int)command["frame"]!);

        Assert.Equal(Words(0x0016, 84, 3, 6), EditAndReencode(command, "direction", 3));
        Assert.Equal(Words(0x0016, 84, 4, 6), EditAndReencode(Decode(0, 0x0016, 84, 1, 6), "direction", 4)); // kDirUnknown appears once in real data
        Assert.Equal(Words(0x0016, 84, 1, 27), EditAndReencode(Decode(0, 0x0016, 84, 1, 6), "frame", 27)); // real-data frame top
        Assert.Equal(Words(0x0016, 84, 1, 0), EditAndReencode(Decode(0, 0x0016, 84, 1, 6), "frame", 0));
        Assert.Equal(Words(0x0016, 0, 1, 6), EditAndReencode(Decode(0, 0x0016, 84, 1, 6), "eventObjectId", 0)); // the no-op gate form still encodes
        Assert.Equal(Words(0x0016, unchecked((int)0xFFFF), 1, 6), EditAndReencode(Decode(0, 0x0016, 84, 1, 6), "eventObjectId", 65_535)); // caller's object

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0016, 84, 1, 6), "frame", 7));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0016, 84, 1, 6), "gesture", 6)); // unknown field: this case stores the absolute frame, no direction*3 row
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0016, 84, 1, 6), "memberIndex", 0)); // unknown field (setPartyFacing's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0016, 84, 1, 6), "objectId", 84)); // unknown field (object union's name)
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0016, 84, 1, 6), "frame", -1)); // unsigned WORD, no SHORT cast
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0016, 84, 1, 6), "direction", 65_536));
    }

    // --- 0x0038 teleportParty (new, shared, script.c:1554-1570): run the current scene's
    //     OnTeleport trigger script (rgScene[wNumScene - 1].wScriptOnTeleport, nested
    //     PAL_RunTriggerScript) and continue; in battle or with no teleport script mark the
    //     script failed and jump to operand 0 - a plain reference (WORD 0 = L0000, the
    //     playerFlee/changeCash shape, not the nullable failTarget binding). Real data: one hit
    //     with to = 38780, inside chunk 4. ---
    [Fact]
    public void TeleportParty_edits_the_failure_target_and_rejects_dangling_references()
    {
        byte[] original = Words(0x0038, 4, 0, 0);
        var command = Decode(0, 0x0038, 4, 0, 0);
        Assert.Equal("teleportParty", (string)command["op"]!);
        Assert.Equal("L0004", (string)command["to"]!);

        Assert.Equal(Words(0x0038, 1, 0, 0), EditAndReencode(command, "to", "L0001")); // target re-resolves into operand 0
        Assert.Equal(Words(0x0038, 0, 0, 0), EditAndReencode(Decode(0, 0x0038, 4, 0, 0), "to", "L0000")); // WORD 0 is the L0000 plain-reference form
        Assert.Equal(Words(0x0038, 7, 0, 0), EditAndReencode(Decode(0, 0x0038, 4, 0, 0), "to", "shared#L0007")); // file-qualified form resolves too

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0038, 4, 0, 0), "to", "L0005"));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0038, 4, 0, 0), "to", "L0100")); // dangling
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0038, 4, 0, 0), "to", "missing#L0001")); // dangling file reference
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0038, 4, 0, 0), "to", null)); // required reference, not nullable
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0038, 4, 0, 0), "failTarget", "L0001")); // unknown field (the nullable binding's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0038, 4, 0, 0), "x", 38_780)); // unknown field: no coordinates in this case
    }

    // --- 0x003F rideEventObjectSlow (new, shared, script.c:1605-1611): PAL_PartyRideEventObject(
    //     wEventObjectID, x, y, h, 2) - the party rides the caller event object to the absolute
    //     map tile position at the constant low speed 2 (not an operand); the callee's loop
    //     blocks until arrival, so the successor stays sequential. Real data: x 30/47, y 66/77,
    //     h 0x2. ---
    [Fact]
    public void RideEventObjectSlow_edits_tile_coordinates_and_keeps_opcode_0x003F()
    {
        byte[] original = Words(0x003F, 30, 66, 0);
        var command = Decode(0, 0x003F, 30, 66, 0);
        Assert.Equal("rideEventObjectSlow", (string)command["op"]!);
        Assert.Equal(30, (int)command["x"]!);
        Assert.Equal(66, (int)command["y"]!);
        Assert.Equal(0, (int)command["h"]!);

        Assert.Equal(Words(0x003F, 47, 66, 0), EditAndReencode(command, "x", 47)); // the other real-data column
        Assert.Equal(Words(0x003F, 30, 77, 0), EditAndReencode(Decode(0, 0x003F, 30, 66, 0), "y", 77)); // the other real-data line
        Assert.Equal(Words(0x003F, 30, 66, 1), EditAndReencode(Decode(0, 0x003F, 30, 66, 0), "h", 1)); // the other tile line
        Assert.Equal(Words(0x003F, 0, 66, 0), EditAndReencode(Decode(0, 0x003F, 30, 66, 0), "x", 0));

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x003F, 30, 66, 0), "y", 67));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x003F, 30, 66, 0), "speed", 2)); // unknown field: speed 2 is a runtime constant
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x003F, 30, 66, 0), "eventObjectId", 30)); // unknown field: the caller object is ridden, x is a coordinate
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x003F, 30, 66, 0), "to", "L0001")); // unknown field: the ride blocks, no jump
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x003F, 30, 66, 0), "x", -1)); // unsigned WORD, no SHORT cast
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x003F, 30, 66, 0), "h", 65_536));
    }

    // --- 0x0044 rideEventObject (new, shared, script.c:1650-1656): the normal-speed ride variant
    //     - PAL_PartyRideEventObject(wEventObjectID, x, y, h, 4); same x/y/h tile operands as
    //     0x003F (callee scales x*32 + h*16 / y*16 + h*8 pixels). Real data: x 8..56, y 19..102,
    //     h 0x49/1x63. ---
    [Fact]
    public void RideEventObject_edits_tile_coordinates_and_stays_the_normal_speed_verb()
    {
        byte[] original = Words(0x0044, 16, 45, 1);
        var command = Decode(0, 0x0044, 16, 45, 1);
        Assert.Equal("rideEventObject", (string)command["op"]!);
        Assert.Equal(16, (int)command["x"]!);
        Assert.Equal(45, (int)command["y"]!);
        Assert.Equal(1, (int)command["h"]!);

        Assert.Equal(Words(0x0044, 56, 45, 1), EditAndReencode(command, "x", 56)); // real-data column top
        Assert.Equal(Words(0x0044, 16, 102, 1), EditAndReencode(Decode(0, 0x0044, 16, 45, 1), "y", 102)); // real-data line top
        Assert.Equal(Words(0x0044, 16, 45, 0), EditAndReencode(Decode(0, 0x0044, 16, 45, 1), "h", 0)); // the other tile line
        Assert.Equal(Words(0x0044, 65_535, 45, 1), EditAndReencode(Decode(0, 0x0044, 16, 45, 1), "x", 65_535)); // full unsigned WORD

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0044, 16, 45, 1), "x", 17));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0044, 16, 45, 1), "speed", 4)); // unknown field: speed 4 is a runtime constant
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0044, 16, 45, 1), "xOffset", 16)); // unknown field (walkNpcStep's relative name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0044, 16, 45, 1), "eventObjectId", 16)); // unknown field: the caller object is ridden
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0044, 16, 45, 1), "y", -1)); // unsigned WORD, no SHORT cast
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0044, 16, 45, 1), "h", 65_536));
    }

    // --- 0x0046 setPartyPos (shared, script.c:1665-1702, review): set the party globals from
    //     tile coordinates - viewport = (x*32 + h*16, y*16 + h*8) - partyoffset (1679-1686) and
    //     every party member is laid out from partyoffset by direction-dependent 16/8-pixel steps
    //     (1689-1701, MAX_PLAYABLE_PLAYER_ROLES = 5 slots); x/y/h keep the same tile scale as
    //     the ride/walkTo family, so the x/y/h binding is re-verified unchanged. Real data:
    //     x 0..57, y 0..120, h 0x582/1x483. ---
    [Fact]
    public void SetPartyPos_edits_tile_coordinates_and_rejects_object_and_direction_fields()
    {
        byte[] original = Words(0x0046, 10, 20, 1);
        var command = Decode(0, 0x0046, 10, 20, 1);
        Assert.Equal("setPartyPos", (string)command["op"]!);
        Assert.Equal(10, (int)command["x"]!);
        Assert.Equal(20, (int)command["y"]!);
        Assert.Equal(1, (int)command["h"]!);

        Assert.Equal(Words(0x0046, 57, 20, 1), EditAndReencode(command, "x", 57)); // real-data column top
        Assert.Equal(Words(0x0046, 10, 120, 1), EditAndReencode(Decode(0, 0x0046, 10, 20, 1), "y", 120)); // real-data line top
        Assert.Equal(Words(0x0046, 10, 20, 0), EditAndReencode(Decode(0, 0x0046, 10, 20, 1), "h", 0)); // the other tile line
        Assert.Equal(Words(0x0046, 0, 20, 1), EditAndReencode(Decode(0, 0x0046, 10, 20, 1), "x", 0)); // zero tile columns appear once in real data

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0046, 10, 20, 1), "h", 0));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0046, 10, 20, 1), "direction", 2)); // unknown field: follow steps read wPartyDirection at runtime
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0046, 10, 20, 1), "memberIndex", 0)); // unknown field: the whole party is placed
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0046, 10, 20, 1), "xOffset", 10)); // unknown field (relative names)
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0046, 10, 20, 1), "x", -1)); // unsigned WORD, no SHORT cast
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0046, 10, 20, 1), "y", 65_536));
    }

    // --- 0x004C chasePlayer (new, shared, script.c:1733-1752): make the caller monster chase the
    //     party - PAL_MonsterChasePlayer(wEventObjectID, speed, chaseRange, floating) with
    //     chaseRange = operand 0 (sensitive range in tile units, WORD 0 normalized to 8 at
    //     runtime only), speed = operand 1 (0 -> 4 likewise) and floating = operand 2, a truthy
    //     WORD whose non-zero form floats over obstacles. Real data: chaseRange 0x2/6..128,
    //     speed 0x10/2/4/6/8, floating 0x17/1x2/0xFFFFx2. ---
    [Fact]
    public void ChasePlayer_edits_range_speed_and_floating_truthy_word()
    {
        byte[] original = Words(0x004C, 8, 4, 1);
        var command = Decode(0, 0x004C, 8, 4, 1);
        Assert.Equal("chasePlayer", (string)command["op"]!);
        Assert.Equal(8, (int)command["chaseRange"]!);
        Assert.Equal(4, (int)command["speed"]!);
        Assert.Equal(1, (int)command["floating"]!);

        Assert.Equal(Words(0x004C, 0, 4, 1), EditAndReencode(command, "chaseRange", 0)); // runtime default 8 still encodes 0
        Assert.Equal(Words(0x004C, 128, 4, 1), EditAndReencode(Decode(0, 0x004C, 8, 4, 1), "chaseRange", 128)); // real-data top
        Assert.Equal(Words(0x004C, 8, 0, 1), EditAndReencode(Decode(0, 0x004C, 8, 4, 1), "speed", 0)); // runtime default 4 still encodes 0
        Assert.Equal(Words(0x004C, 8, 4, 0), EditAndReencode(Decode(0, 0x004C, 8, 4, 1), "floating", 0)); // grounded form
        Assert.Equal(Words(0x004C, 8, 4, unchecked((int)0xFFFF)), EditAndReencode(Decode(0, 0x004C, 8, 4, 1), "floating", 65_535)); // the 0xFFFF truthy form of real data

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x004C, 8, 4, 1), "speed", 6));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x004C, 8, 4, 1), "maxDistance", 8)); // unknown field name (the case comment's wording)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x004C, 8, 4, 1), "eventObjectId", 8)); // unknown field: the caller monster chases
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x004C, 8, 4, 1), "isFloating", true)); // unknown field: not a bossFlag boolean
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x004C, 8, 4, 1), "speed", -4)); // unsigned WORD, no SHORT cast
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x004C, 8, 4, 1), "chaseRange", 65_536));
    }

    // --- 0x0062 pauseEnemyChasing (new, shared, script.c:1967-1971): wChasespeedChangeCycles =
    //     operand0 and the global chase range wChaseRange = 0 - PAL_MonsterChasePlayer skips its
    //     whole chase block while the global is 0 (script.c:341); play.c:235-237 decrements the
    //     cycles per game update and resets the range to 1 at zero. Real data: cycles 600. ---
    [Fact]
    public void PauseEnemyChasing_edits_cycles_and_rejects_range_fields()
    {
        byte[] original = Words(0x0062, 600, 0, 0);
        var command = Decode(0, 0x0062, 600, 0, 0);
        Assert.Equal("pauseEnemyChasing", (string)command["op"]!);
        Assert.Equal(600, (int)command["cycles"]!);

        Assert.Equal(Words(0x0062, 0, 0, 0), EditAndReencode(command, "cycles", 0));
        Assert.Equal(Words(0x0062, 65_535, 0, 0), EditAndReencode(Decode(0, 0x0062, 600, 0, 0), "cycles", 65_535)); // full unsigned WORD

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0062, 600, 0, 0), "cycles", 601));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0062, 600, 0, 0), "chaseRange", 0)); // unknown field: the range is hardcoded 0 in this case
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0062, 600, 0, 0), "frames", 600)); // unknown field name
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0062, 600, 0, 0), "speed", 4)); // unknown field: no speed operand
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0062, 600, 0, 0), "cycles", -1)); // unsigned WORD, no SHORT cast
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0062, 600, 0, 0), "cycles", 65_536));
    }

    // --- 0x0063 speedUpEnemyChasing (new, shared, script.c:1975-1979): the same countdown with
    //     the global chase range wChaseRange = 3, tripling every monster's sensitivity
    //     (script.c:393) for the operand-0 cycles. Real data: cycles 600. ---
    [Fact]
    public void SpeedUpEnemyChasing_edits_cycles_and_keeps_the_distinct_verb()
    {
        byte[] original = Words(0x0063, 600, 0, 0);
        var command = Decode(0, 0x0063, 600, 0, 0);
        Assert.Equal("speedUpEnemyChasing", (string)command["op"]!);
        Assert.Equal(600, (int)command["cycles"]!);
        Assert.NotEqual("pauseEnemyChasing", (string)command["op"]!); // the 0/3 range split keeps two verbs

        Assert.Equal(Words(0x0063, 1, 0, 0), EditAndReencode(command, "cycles", 1));
        Assert.Equal(Words(0x0063, 0, 0, 0), EditAndReencode(Decode(0, 0x0063, 600, 0, 0), "cycles", 0));

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0063, 600, 0, 0), "cycles", 599));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0063, 600, 0, 0), "chaseRange", 3)); // unknown field: the range is hardcoded 3 in this case
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0063, 600, 0, 0), "multiplier", 3)); // unknown field name
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0063, 600, 0, 0), "speed", 4)); // unknown field: no speed operand
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0063, 600, 0, 0), "cycles", -1)); // unsigned WORD, no SHORT cast
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0063, 600, 0, 0), "cycles", 65_536));
    }

    // --- 0x006C walkNpcStep (new, shared, script.c:2056-2062): move the preamble-selected NPC
    //     (pCurrent/wCurEventObjectID, 0/0xFFFF = caller, else 1-based) by the signed pixel
    //     offsets pCurrent->x += (SHORT)operand1 / y += (SHORT)operand2 - the batch's only
    //     (SHORT) casts - then animate one walk step. Real data: eventObjectId 0x1/0xFFFFx226/
    //     1-based 9..3348x68; xOffset -32..16 (0xFFE0 = -32), yOffset -12..16 (0xFFF4 = -12). ---
    [Fact]
    public void WalkNpcStep_edits_object_and_signed_pixel_offsets()
    {
        byte[] original = Words(0x006C, unchecked((int)0xFFFF), unchecked((int)0xFFE0), 8); // the real-data caller form with -32/+8
        var command = Decode(0, 0x006C, unchecked((int)0xFFFF), unchecked((int)0xFFE0), 8);
        Assert.Equal("walkNpcStep", (string)command["op"]!);
        Assert.Equal(65_535, (int)command["eventObjectId"]!); // 0xFFFF = the caller's object
        Assert.Equal(-32, (int)command["xOffset"]!); // 0xFFE0 sign-extended through the (SHORT) cast
        Assert.Equal(8, (int)command["yOffset"]!);

        Assert.Equal(original, Reencode(command)); // all three WORDs re-encode byte-exact
        Assert.Equal(Words(0x006C, unchecked((int)0xFFFF), unchecked((int)0xFFF0), 8), EditAndReencode(command, "xOffset", -16)); // -16 -> 0xFFF0
        Assert.Equal(Words(0x006C, unchecked((int)0xFFFF), 0, 8), EditAndReencode(Decode(0, 0x006C, unchecked((int)0xFFFF), unchecked((int)0xFFE0), 8), "xOffset", 0));
        Assert.Equal(Words(0x006C, unchecked((int)0xFFFF), unchecked((int)0xFFE0), unchecked((int)0xFFF4)), EditAndReencode(Decode(0, 0x006C, unchecked((int)0xFFFF), unchecked((int)0xFFE0), 8), "yOffset", -12)); // real-data -12 -> 0xFFF4
        Assert.Equal(Words(0x006C, 9, unchecked((int)0xFFE0), 8), EditAndReencode(Decode(0, 0x006C, unchecked((int)0xFFFF), unchecked((int)0xFFE0), 8), "eventObjectId", 9)); // 1-based real-data form
        Assert.Equal(Words(0x006C, 0, unchecked((int)0xFFE0), 8), EditAndReencode(Decode(0, 0x006C, unchecked((int)0xFFFF), unchecked((int)0xFFE0), 8), "eventObjectId", 0)); // 0 = the caller's object

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x006C, unchecked((int)0xFFFF), unchecked((int)0xFFE0), 8), "yOffset", 9));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x006C, 0xFFFF, unchecked((int)0xFFE0), 8), "x", 16)); // unknown field (absolute names)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x006C, 0xFFFF, unchecked((int)0xFFE0), 8), "y", 8)); // unknown field (absolute names)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x006C, 0xFFFF, unchecked((int)0xFFE0), 8), "direction", 2)); // unknown field: the step direction comes from the offsets
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x006C, 0xFFFF, unchecked((int)0xFFE0), 8), "objectId", 9)); // unknown field (object union's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x006C, 0xFFFF, unchecked((int)0xFFE0), 8), "xOffset", -32_769)); // beyond signed WORD
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x006C, 0xFFFF, unchecked((int)0xFFE0), 8), "yOffset", 32_768)); // beyond signed WORD
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x006C, 0xFFFF, unchecked((int)0xFFE0), 8), "eventObjectId", 65_536)); // object id stays unsigned
    }
}
