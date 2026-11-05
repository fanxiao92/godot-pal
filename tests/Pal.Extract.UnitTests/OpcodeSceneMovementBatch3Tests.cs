using Pal.Extract;
using Xunit;
using static Pal.Extract.Tests.OpcodeCounterexampleHarness;

namespace Pal.Extract.Tests;

// M5 opcode scene-movement batch 3 (#107, movement part 3 - the 31-opcode movement subdomain's
// closing batch) - moveParty 0x006E, partyWalkToSlow 0x0070, partyWalkTo 0x007A,
// partyWalkToFast 0x007B, walkToFast 0x007C, moveObject 0x007D, moveViewport 0x007F,
// walkToFastest 0x0082, rideEventObjectFast 0x0097, setFollower 0x0098, gatherParty 0x00A1.
// Topology per script.c: all eleven opcode bits live in the shared PAL_InterpretInstruction
// switch (trigger switch 3206-3440 and auto switch 3522-3641 have no case for any of them),
// every case appears exactly once, and all successors are sequential - the partyWalkTo and
// ride families finish inside their callees' blocking loops (script.c:133-197 / 203-310),
// 0x007C/0x0082 re-execute their own entry until arrival (wScriptEntry--, script.c:2268/
// 2273/2444 - a runtime stall like walkTo, not a jump) and 0x007F finishes its inline
// do-while (script.c:2331-2377); everything else breaks into the script.c:3085 tail
// return wScriptEntry + 1. Semantics per the reference cases:
//   - 0x006E moves the party (viewport) by signed pixel offsets in one step and writes the
//     layer global wLayer = operand2 * 8 (script.c:2103-2107) - operand 2 is the layer, not
//     the walkTo family's tile half-line h (no x/y tile scaling happens in this case).
//   - 0x0070/0x007A/0x007B pass absolute x/y/h tile coordinates to PAL_PartyWalkTo at the
//     constant speeds 2/4/8 (script.c:2129/2249/2256) - the party-walk counterparts of the
//     walkTo/ride speed ladders.
//   - 0x007C is the gated speed-4 walkTo form (frame-parity gate script.c:2263, speed 4 at
//     2265-2266); 0x0082 is the ungated speed-8 form (script.c:2441-2444).
//   - 0x007D moves the preamble-selected event object by signed pixel offsets
//     (pCurrent->x += (SHORT)operand1, script.c:2281-2282) - the walkNpcStep shape without
//     the walk-one-step animation.
//   - 0x007F scrolls the viewport by signed per-frame pixel offsets for the operand-2 frame
//     count (WORD 0 still runs one do-while pass); the two sentinel forms reuse the same
//     WORDs differently: offsets 0/0 reset the viewport to the normal state and frames
//     0xFFFF switches the operands to unsigned tile coordinates for an absolute placement
//     (script.c:2296-2378) - the signed scroll form names the fields.
//   - 0x0097 is the speed-8 ride (PAL_PartyRideEventObject(..., 8), script.c:2707-2708).
//   - 0x0098 stores operands 0/1 into the follower slots' wPlayerRole with no -1; a
//     follower's wPlayerRole is consumed directly as an MGO.MKF sprite number (res.c:337),
//     so the fields are sprite numbers, not the 1-based roles of setParty; WORD 0 skips a
//     slot and all-zero operands clear every follower (script.c:2715-2739).
//   - 0x00A1 snaps all party members onto the leader's position; no operands
//     (script.c:3004-3015).
// Signedness: six (SHORT)-cast fields across 0x006E/0x007D/0x007F (xOffset/yOffset pairs,
// script.c:2104-2105/2281-2282/2326-2327; real data e.g. 65520 = -16, 65280 = -128); every
// other numeric field is an unsigned WORD (0x007F's frames keeps the unsigned-only 0xFFFF
// sentinel). Counter-examples follow the #78/#96 skeleton: edit a semantic field ->
// re-encoded bytes differ from the source bytes and equal the hand-computed value; unknown
// fields and out-of-range values are rejected.
public sealed class OpcodeSceneMovementBatch3Tests
{
    // --- 0x006E moveParty (new, shared, script.c:2091-2113): one-step party move - rgTrail is
    //     shifted, viewport += the signed pixel offsets (2103-2105) and wLayer = layerUnits * 8
    //     (2107); gestures update only when an offset is non-zero (2109-2112). Real data:
    //     xOffset -32..16 (65504=-32 x2, 65520=-16 x53), yOffset -16..24, layerUnits 0x294
    //     1..10x19. ---
    [Fact]
    public void MoveParty_edits_signed_offsets_and_layer_units()
    {
        byte[] original = Words(0x006E, unchecked((int)0xFFF0), 8, 1); // the real-data -16/+8 pixel form
        var command = Decode(0, 0x006E, unchecked((int)0xFFF0), 8, 1);
        Assert.Equal("moveParty", (string)command["op"]!);
        Assert.Equal(-16, (int)command["xOffset"]!); // 0xFFF0 sign-extended through the (SHORT) cast
        Assert.Equal(8, (int)command["yOffset"]!);
        Assert.Equal(1, (int)command["layerUnits"]!);

        Assert.Equal(original, Reencode(command)); // all three WORDs re-encode byte-exact
        Assert.Equal(Words(0x006E, 16, 8, 1), EditAndReencode(command, "xOffset", 16)); // -16 -> +16, the other real-data tail
        Assert.Equal(Words(0x006E, unchecked((int)0xFFE0), 8, 1), EditAndReencode(Decode(0, 0x006E, unchecked((int)0xFFF0), 8, 1), "xOffset", -32)); // real-data -32 -> 0xFFE0
        Assert.Equal(Words(0x006E, unchecked((int)0xFFF0), unchecked((int)0xFFF8), 1), EditAndReencode(Decode(0, 0x006E, unchecked((int)0xFFF0), 8, 1), "yOffset", -8));
        Assert.Equal(Words(0x006E, unchecked((int)0xFFF0), 8, 0), EditAndReencode(Decode(0, 0x006E, unchecked((int)0xFFF0), 8, 1), "layerUnits", 0)); // the zero-layer majority form
        Assert.Equal(Words(0x006E, unchecked((int)0xFFF0), 8, 10), EditAndReencode(Decode(0, 0x006E, unchecked((int)0xFFF0), 8, 1), "layerUnits", 10)); // real-data top
        var layerOnly = Decode(0, 0x006E, unchecked((int)0xFFF0), 8, 1);
        layerOnly["xOffset"] = 0;
        layerOnly["yOffset"] = 0;
        Assert.Equal(Words(0x006E, 0, 0, 1), Reencode(layerOnly)); // the layer-only form appears in real data
        Assert.Equal(Words(0x006E, 0, 8, 1), EditAndReencode(Decode(0, 0x006E, unchecked((int)0xFFF0), 8, 1), "xOffset", 0)); // a lone zero offset only drops the gesture update

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x006E, unchecked((int)0xFFF0), 8, 1), "yOffset", 9));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x006E, unchecked((int)0xFFF0), 8, 1), "h", 1)); // unknown field: operand 2 is the layer, no tile half-line scaling here
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x006E, unchecked((int)0xFFF0), 8, 1), "x", 16)); // unknown field (absolute names)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x006E, unchecked((int)0xFFF0), 8, 1), "memberIndex", 0)); // unknown field: the whole party moves
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x006E, unchecked((int)0xFFF0), 8, 1), "xOffset", -32_769)); // beyond signed WORD
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x006E, unchecked((int)0xFFF0), 8, 1), "yOffset", 32_768)); // beyond signed WORD
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x006E, unchecked((int)0xFFF0), 8, 1), "layerUnits", 65_536)); // unsigned WORD
    }

    // --- 0x0070 partyWalkToSlow (new, shared, script.c:2125-2130): PAL_PartyWalkTo(x, y, h, 2)
    //     - the whole party walks to the absolute tile position at the constant low speed 2;
    //     the callee blocks until arrival (script.c:133-197), so the successor is sequential.
    //     Real data: x 8..50, y 16..111, h 0x20/1x13. ---
    [Fact]
    public void PartyWalkToSlow_edits_tile_coordinates_and_keeps_opcode_0x0070()
    {
        byte[] original = Words(0x0070, 12, 28, 1);
        var command = Decode(0, 0x0070, 12, 28, 1);
        Assert.Equal("partyWalkToSlow", (string)command["op"]!);
        Assert.Equal(12, (int)command["x"]!);
        Assert.Equal(28, (int)command["y"]!);
        Assert.Equal(1, (int)command["h"]!);

        Assert.Equal(Words(0x0070, 50, 28, 1), EditAndReencode(command, "x", 50)); // real-data column top
        Assert.Equal(Words(0x0070, 12, 111, 1), EditAndReencode(Decode(0, 0x0070, 12, 28, 1), "y", 111)); // real-data line top
        Assert.Equal(Words(0x0070, 12, 28, 0), EditAndReencode(Decode(0, 0x0070, 12, 28, 1), "h", 0)); // the other tile line
        Assert.Equal(Words(0x0070, 8, 28, 1), EditAndReencode(Decode(0, 0x0070, 12, 28, 1), "x", 8)); // real-data column bottom keeps the line

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0070, 12, 28, 1), "x", 13));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0070, 12, 28, 1), "speed", 2)); // unknown field: speed 2 is a runtime constant
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0070, 12, 28, 1), "xOffset", 12)); // unknown field (relative names)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0070, 12, 28, 1), "memberIndex", 0)); // unknown field: the whole party walks
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0070, 12, 28, 1), "x", -1)); // unsigned WORD, no SHORT cast
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0070, 12, 28, 1), "h", 65_536));
    }

    // --- 0x007A partyWalkTo (new, shared, script.c:2245-2250): the normal-speed party walk -
    //     PAL_PartyWalkTo(x, y, h, 4); same x/y/h tile operands as 0x0070. Real data: x 5..56,
    //     y 8..105, h 0x187/1x194. ---
    [Fact]
    public void PartyWalkTo_edits_tile_coordinates_and_stays_the_normal_speed_verb()
    {
        byte[] original = Words(0x007A, 5, 8, 0);
        var command = Decode(0, 0x007A, 5, 8, 0);
        Assert.Equal("partyWalkTo", (string)command["op"]!);
        Assert.NotEqual("partyWalkToSlow", (string)command["op"]!); // the 2/4/8 speed ladder keeps three verbs
        Assert.Equal(5, (int)command["x"]!);
        Assert.Equal(8, (int)command["y"]!);
        Assert.Equal(0, (int)command["h"]!);

        Assert.Equal(Words(0x007A, 56, 8, 0), EditAndReencode(command, "x", 56)); // real-data column top
        Assert.Equal(Words(0x007A, 5, 105, 0), EditAndReencode(Decode(0, 0x007A, 5, 8, 0), "y", 105)); // real-data line top
        Assert.Equal(Words(0x007A, 5, 8, 1), EditAndReencode(Decode(0, 0x007A, 5, 8, 0), "h", 1)); // the majority tile line
        Assert.Equal(Words(0x007A, 65_535, 8, 0), EditAndReencode(Decode(0, 0x007A, 5, 8, 0), "x", 65_535)); // full unsigned WORD

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x007A, 5, 8, 0), "y", 9));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x007A, 5, 8, 0), "speed", 4)); // unknown field: speed 4 is a runtime constant
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x007A, 5, 8, 0), "to", "L0001")); // unknown field: the callee blocks, no jump
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x007A, 5, 8, 0), "eventObjectId", 5)); // unknown field: the party globals walk
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x007A, 5, 8, 0), "y", -1)); // unsigned WORD, no SHORT cast
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x007A, 5, 8, 0), "h", 65_536));
    }

    // --- 0x007B partyWalkToFast (new, shared, script.c:2252-2257): the fastest party walk -
    //     PAL_PartyWalkTo(x, y, h, 8). Real data: x 10..49, y 15..96, h 0x27/1x29. ---
    [Fact]
    public void PartyWalkToFast_edits_tile_coordinates_and_keeps_the_distinct_verb()
    {
        byte[] original = Words(0x007B, 10, 15, 0);
        var command = Decode(0, 0x007B, 10, 15, 0);
        Assert.Equal("partyWalkToFast", (string)command["op"]!);
        Assert.NotEqual("partyWalkTo", (string)command["op"]!); // the constant 8 keeps its own verb
        Assert.Equal(10, (int)command["x"]!);
        Assert.Equal(15, (int)command["y"]!);
        Assert.Equal(0, (int)command["h"]!);

        Assert.Equal(Words(0x007B, 49, 15, 0), EditAndReencode(command, "x", 49)); // real-data column top
        Assert.Equal(Words(0x007B, 10, 96, 0), EditAndReencode(Decode(0, 0x007B, 10, 15, 0), "y", 96)); // real-data line top
        Assert.Equal(Words(0x007B, 10, 15, 1), EditAndReencode(Decode(0, 0x007B, 10, 15, 0), "h", 1)); // the other tile line

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x007B, 10, 15, 0), "x", 11));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x007B, 10, 15, 0), "speed", 8)); // unknown field: speed 8 is a runtime constant
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x007B, 10, 15, 0), "yOffset", 15)); // unknown field (relative names)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x007B, 10, 15, 0), "frames", 1)); // unknown field: the callee blocks, no frame count
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x007B, 10, 15, 0), "x", -1)); // unsigned WORD, no SHORT cast
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x007B, 10, 15, 0), "y", 65_536));
    }

    // --- 0x007C walkToFast (new, shared, script.c:2259-2275): the gated speed-4 walkTo form -
    //     the frame-parity gate ((wEventObjectID & 1) ^ (dwFrameNum & 1), 2263) runs
    //     PAL_NPCWalkTo(caller, x, y, h, 4) (2265-2266) every other frame; until arrival the
    //     entry re-executes (wScriptEntry--, 2268/2273) - a runtime stall, not a jump.
    //     Real data: x 6..51, y 20..115, h 0x21/1x11. ---
    [Fact]
    public void WalkToFast_edits_tile_coordinates_and_keeps_the_gated_speed4_form()
    {
        byte[] original = Words(0x007C, 22, 49, 0);
        var command = Decode(0, 0x007C, 22, 49, 0);
        Assert.Equal("walkToFast", (string)command["op"]!);
        Assert.Equal(22, (int)command["x"]!);
        Assert.Equal(49, (int)command["y"]!);
        Assert.Equal(0, (int)command["h"]!);

        Assert.Equal(Words(0x007C, 51, 49, 0), EditAndReencode(command, "x", 51)); // real-data column top
        Assert.Equal(Words(0x007C, 22, 115, 0), EditAndReencode(Decode(0, 0x007C, 22, 49, 0), "y", 115)); // real-data line top
        Assert.Equal(Words(0x007C, 22, 49, 1), EditAndReencode(Decode(0, 0x007C, 22, 49, 0), "h", 1)); // the other tile line
        Assert.Equal(Words(0x007C, 6, 49, 0), EditAndReencode(Decode(0, 0x007C, 22, 49, 0), "x", 6)); // real-data column bottom keeps the line

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x007C, 22, 49, 0), "y", 50));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x007C, 22, 49, 0), "speed", 4)); // unknown field: speed 4 is a runtime constant
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x007C, 22, 49, 0), "gate", 1)); // unknown field: the frame parity is not an operand
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x007C, 22, 49, 0), "eventObjectId", 22)); // unknown field: the caller object walks
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x007C, 22, 49, 0), "x", -1)); // unsigned WORD, no SHORT cast
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x007C, 22, 49, 0), "h", 65_536));
    }

    // --- 0x007D moveObject (new, shared, script.c:2277-2283): moves the preamble-selected
    //     event object (pCurrent, 0/0xFFFF = caller, else a 1-based index, script.c:626-638;
    //     no operand-0 guard) by the signed pixel offsets pCurrent->x += (SHORT)operand1 /
    //     y += (SHORT)operand2 (2281-2282) - the walkNpcStep shape without the animation call.
    //     Real data: eventObjectId 0xFFFFx210 (caller) / 1-based 57..3114x67; xOffset -128..48
    //     (65280=-128 x2, 65504=-32 x7), yOffset -80..40. ---
    [Fact]
    public void MoveObject_edits_object_and_signed_pixel_offsets()
    {
        byte[] original = Words(0x007D, unchecked((int)0xFFFF), unchecked((int)0xFFF0), 4); // the real-data caller form with -16/+4
        var command = Decode(0, 0x007D, unchecked((int)0xFFFF), unchecked((int)0xFFF0), 4);
        Assert.Equal("moveObject", (string)command["op"]!);
        Assert.Equal(65_535, (int)command["eventObjectId"]!); // 0xFFFF = the caller's object
        Assert.Equal(-16, (int)command["xOffset"]!); // 0xFFF0 sign-extended through the (SHORT) cast
        Assert.Equal(4, (int)command["yOffset"]!);

        Assert.Equal(original, Reencode(command)); // all three WORDs re-encode byte-exact
        Assert.Equal(Words(0x007D, unchecked((int)0xFFFF), unchecked((int)0xFF80), 4), EditAndReencode(command, "xOffset", -128)); // real-data -128 -> 0xFF80
        Assert.Equal(Words(0x007D, unchecked((int)0xFFFF), 16, 4), EditAndReencode(Decode(0, 0x007D, unchecked((int)0xFFFF), unchecked((int)0xFFF0), 4), "xOffset", 16));
        Assert.Equal(Words(0x007D, unchecked((int)0xFFFF), unchecked((int)0xFFF0), unchecked((int)0xFFB0)), EditAndReencode(Decode(0, 0x007D, unchecked((int)0xFFFF), unchecked((int)0xFFF0), 4), "yOffset", -80)); // real-data -80 -> 0xFFB0
        Assert.Equal(Words(0x007D, 57, unchecked((int)0xFFF0), 4), EditAndReencode(Decode(0, 0x007D, unchecked((int)0xFFFF), unchecked((int)0xFFF0), 4), "eventObjectId", 57)); // 1-based real-data bottom
        Assert.Equal(Words(0x007D, 3114, unchecked((int)0xFFF0), 4), EditAndReencode(Decode(0, 0x007D, unchecked((int)0xFFFF), unchecked((int)0xFFF0), 4), "eventObjectId", 3114)); // 1-based real-data top
        Assert.Equal(Words(0x007D, 0, unchecked((int)0xFFF0), 4), EditAndReencode(Decode(0, 0x007D, unchecked((int)0xFFFF), unchecked((int)0xFFF0), 4), "eventObjectId", 0)); // 0 = the caller's object, the preamble's own form

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x007D, unchecked((int)0xFFFF), unchecked((int)0xFFF0), 4), "yOffset", 5));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x007D, unchecked((int)0xFFFF), unchecked((int)0xFFF0), 4), "x", 16)); // unknown field (absolute names)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x007D, unchecked((int)0xFFFF), unchecked((int)0xFFF0), 4), "y", 4)); // unknown field (absolute names)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x007D, unchecked((int)0xFFFF), unchecked((int)0xFFF0), 4), "objectId", 57)); // unknown field (object union's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x007D, unchecked((int)0xFFFF), unchecked((int)0xFFF0), 4), "direction", 2)); // unknown field: a pure shift, no direction
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x007D, unchecked((int)0xFFFF), unchecked((int)0xFFF0), 4), "xOffset", -32_769)); // beyond signed WORD
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x007D, unchecked((int)0xFFFF), unchecked((int)0xFFF0), 4), "yOffset", 32_768)); // beyond signed WORD
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x007D, unchecked((int)0xFFFF), unchecked((int)0xFFF0), 4), "eventObjectId", 65_536)); // object id stays unsigned
    }

    // --- 0x007F moveViewport (new, shared, script.c:2292-2379): three forms over the same
    //     WORDs - offsets 0/0 reset the viewport to the normal state (party recentered at
    //     160,112, redrawn unless frames is 0xFFFF, 2296-2318); otherwise the signed pixel
    //     offsets scroll the viewport once per frame for the frames units (WORD 0 still runs
    //     the single do-while pass, 2331-2377); frames 0xFFFF switches the operands to
    //     unsigned tile coordinates for the absolute placement (x*32-160, y*16-112,
    //     2333-2339). Real data: reset x128 / scroll x180 / absolute x9; xOffset -16..192,
    //     yOffset -96..96, frames 0x141, 1..150, 0xFFFFx10. ---
    [Fact]
    public void MoveViewport_edits_signed_scroll_offsets_and_frames_with_sentinel_forms()
    {
        byte[] original = Words(0x007F, 4, 8, 16);
        var command = Decode(0, 0x007F, 4, 8, 16);
        Assert.Equal("moveViewport", (string)command["op"]!);
        Assert.Equal(4, (int)command["xOffset"]!);
        Assert.Equal(8, (int)command["yOffset"]!);
        Assert.Equal(16, (int)command["frames"]!); // the most common real frame count

        var reset = Decode(0, 0x007F, 4, 8, 16);
        reset["xOffset"] = 0;
        reset["yOffset"] = 0;
        Assert.Equal(Words(0x007F, 0, 0, 16), Reencode(reset)); // the reset form: both offsets WORD 0 recenter the party
        Assert.Equal(Words(0x007F, 4, 0, 16), EditAndReencode(Decode(0, 0x007F, 4, 8, 16), "yOffset", 0)); // a lone zero offset still scrolls on the other axis
        Assert.Equal(Words(0x007F, 0, 8, 16), EditAndReencode(Decode(0, 0x007F, 4, 8, 16), "xOffset", 0));
        Assert.Equal(Words(0x007F, unchecked((int)0xFFF0), 8, 16), EditAndReencode(Decode(0, 0x007F, 4, 8, 16), "xOffset", -16)); // real-data -16 -> 0xFFF0
        Assert.Equal(Words(0x007F, 4, unchecked((int)0xFFA0), 16), EditAndReencode(Decode(0, 0x007F, 4, 8, 16), "yOffset", -96)); // real-data -96 -> 0xFFA0
        Assert.Equal(Words(0x007F, 4, 8, 0), EditAndReencode(Decode(0, 0x007F, 4, 8, 16), "frames", 0)); // WORD 0 runs the single do-while pass
        Assert.Equal(Words(0x007F, 4, 8, unchecked((int)0xFFFF)), EditAndReencode(Decode(0, 0x007F, 4, 8, 16), "frames", 65_535)); // the 0xFFFF absolute-mode sentinel
        Assert.Equal(Words(0x007F, 4, 8, 150), EditAndReencode(Decode(0, 0x007F, 4, 8, 16), "frames", 150)); // real-data frame top

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x007F, 4, 8, 16), "frames", 17));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x007F, 4, 8, 16), "h", 1)); // unknown field: the absolute mode's tile scale is a form, not a field
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x007F, 4, 8, 16), "x", 4)); // unknown field (absolute names)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x007F, 4, 8, 16), "delayUnits", 16)); // unknown field name (delay's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x007F, 4, 8, 16), "to", "L0001")); // unknown field: no jump
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x007F, 4, 8, 16), "xOffset", -32_769)); // beyond signed WORD
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x007F, 4, 8, 16), "yOffset", 32_768)); // beyond signed WORD
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x007F, 4, 8, 16), "frames", 65_536)); // unsigned WORD: the 0xFFFF sentinel is unsigned-only
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x007F, 4, 8, 16), "frames", -1)); // -1 would collide with the unsigned sentinel reading; frames is not signed
    }

    // --- 0x0082 walkToFastest (new, shared, script.c:2437-2446): the ungated speed-8 walkTo
    //     form - PAL_NPCWalkTo(caller, x, y, h, 8) (2441-2442); until arrival the entry
    //     re-executes (wScriptEntry--, 2444). Real data: x 8..61, y 8..112, h 0x64/1x33. ---
    [Fact]
    public void WalkToFastest_edits_tile_coordinates_and_stays_the_speed8_verb()
    {
        byte[] original = Words(0x0082, 24, 20, 1);
        var command = Decode(0, 0x0082, 24, 20, 1);
        Assert.Equal("walkToFastest", (string)command["op"]!);
        Assert.NotEqual("walkToFast", (string)command["op"]!); // the 4/8 constants keep two verbs
        Assert.Equal(24, (int)command["x"]!);
        Assert.Equal(20, (int)command["y"]!);
        Assert.Equal(1, (int)command["h"]!);

        Assert.Equal(Words(0x0082, 61, 20, 1), EditAndReencode(command, "x", 61)); // real-data column top
        Assert.Equal(Words(0x0082, 24, 112, 1), EditAndReencode(Decode(0, 0x0082, 24, 20, 1), "y", 112)); // real-data line top
        Assert.Equal(Words(0x0082, 24, 20, 0), EditAndReencode(Decode(0, 0x0082, 24, 20, 1), "h", 0)); // the majority tile line
        Assert.Equal(Words(0x0082, 8, 20, 1), EditAndReencode(Decode(0, 0x0082, 24, 20, 1), "x", 8)); // real-data column bottom keeps the line

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0082, 24, 20, 1), "h", 0));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0082, 24, 20, 1), "speed", 8)); // unknown field: speed 8 is a runtime constant
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0082, 24, 20, 1), "xOffset", 24)); // unknown field (relative names)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0082, 24, 20, 1), "eventObjectId", 24)); // unknown field: the caller object walks
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0082, 24, 20, 1), "y", -1)); // unsigned WORD, no SHORT cast
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0082, 24, 20, 1), "x", 65_536));
    }

    // --- 0x0097 rideEventObjectFast (new, shared, script.c:2703-2709): the speed-8 ride -
    //     PAL_PartyRideEventObject(wEventObjectID, x, y, h, 8); same x/y/h tile operands as
    //     0x003F/0x0044, completing the 2/4/8 ride ladder. Real data: one hit, x 45 y 120 h 0. ---
    [Fact]
    public void RideEventObjectFast_edits_tile_coordinates_and_completes_the_ride_ladder()
    {
        byte[] original = Words(0x0097, 45, 120, 0); // the single real-data hit
        var command = Decode(0, 0x0097, 45, 120, 0);
        Assert.Equal("rideEventObjectFast", (string)command["op"]!);
        Assert.NotEqual("rideEventObject", (string)command["op"]!); // the 2/4/8 ladder keeps three verbs
        Assert.Equal(45, (int)command["x"]!);
        Assert.Equal(120, (int)command["y"]!);
        Assert.Equal(0, (int)command["h"]!);

        Assert.Equal(Words(0x0097, 46, 120, 0), EditAndReencode(command, "x", 46)); // a neighbouring tile
        Assert.Equal(Words(0x0097, 45, 119, 0), EditAndReencode(Decode(0, 0x0097, 45, 120, 0), "y", 119));
        Assert.Equal(Words(0x0097, 45, 120, 1), EditAndReencode(Decode(0, 0x0097, 45, 120, 0), "h", 1)); // the other tile line
        Assert.Equal(Words(0x0097, 0, 120, 0), EditAndReencode(Decode(0, 0x0097, 45, 120, 0), "x", 0)); // zero column with the real line

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0097, 45, 120, 0), "x", 44));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0097, 45, 120, 0), "speed", 8)); // unknown field: speed 8 is a runtime constant
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0097, 45, 120, 0), "eventObjectId", 45)); // unknown field: the caller object is ridden
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0097, 45, 120, 0), "xOffset", 45)); // unknown field (relative names)
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0097, 45, 120, 0), "y", -1)); // unsigned WORD, no SHORT cast
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0097, 45, 120, 0), "h", 65_536));
    }

    // --- 0x0098 setFollower (new, shared, script.c:2711-2740): operands 0/1 select follower
    //     slots 1/2 (> 0) and are stored into rgParty[...].wPlayerRole with no -1 (2715-2733);
    //     a follower's wPlayerRole is consumed directly as an MGO.MKF sprite number (res.c:337),
    //     so spriteNum0/spriteNum1 are sprite numbers, not setParty's 1-based roles; WORD 0
    //     skips a slot and all-zero operands clear every follower (nFollower = 0, 2737-2739);
    //     the follower walks in from the rgTrail position. Real data: (0,0) x1 clear form,
    //     (82,0) and (83,0) slot-1 sprites. ---
    [Fact]
    public void SetFollower_edits_both_sprite_slots_and_the_clear_form()
    {
        byte[] original = Words(0x0098, 82, 0, 0); // a real-data slot-1 follower
        var command = Decode(0, 0x0098, 82, 0, 0);
        Assert.Equal("setFollower", (string)command["op"]!);
        Assert.Equal(82, (int)command["spriteNum0"]!);
        Assert.Equal(0, (int)command["spriteNum1"]!); // slot 2 not set

        Assert.Equal(Words(0x0098, 83, 0, 0), EditAndReencode(command, "spriteNum0", 83)); // the other real-data sprite
        Assert.Equal(Words(0x0098, 82, 83, 0), EditAndReencode(Decode(0, 0x0098, 82, 0, 0), "spriteNum1", 83)); // the two-follower form
        Assert.Equal(Words(0x0098, 0, 0, 0), EditAndReencode(Decode(0, 0x0098, 82, 0, 0), "spriteNum0", 0)); // the all-zero clear form still encodes
        Assert.Equal(Words(0x0098, unchecked((int)0xFFFF), 0, 0), EditAndReencode(Decode(0, 0x0098, 82, 0, 0), "spriteNum0", 65_535)); // full unsigned WORD

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0098, 82, 0, 0), "spriteNum1", 1));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0098, 82, 0, 0), "roleId0", 82)); // unknown field: follower operands are sprite numbers, not setParty's 1-based roles
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0098, 82, 0, 0), "spriteId0", 82)); // unknown field name (Id vs Num)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0098, 82, 0, 0), "followerCount", 1)); // unknown field: the slot count is derived, not an operand
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0098, 82, 0, 0), "spriteNum0", -1)); // unsigned WORD, no SHORT cast
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0098, 82, 0, 0), "spriteNum1", 65_536));
    }

    // --- 0x00A1 gatherParty (new, shared, script.c:3000-3016): snaps every party member and
    //     trail slot onto the leader's position (members at y - 1, 3004-3014, then
    //     PAL_UpdatePartyGestures(FALSE) at 3015) - the case reads no operands. Real data:
    //     50 hits, all three operands zero. ---
    [Fact]
    public void GatherParty_maps_no_fields_and_rejects_any_semantic_field()
    {
        byte[] original = Words(0x00A1, 0, 0, 0);
        var command = Decode(0, 0x00A1, 0, 0, 0);
        Assert.Equal("gatherParty", (string)command["op"]!);
        Assert.Equal(original, Reencode(command)); // no fields: byte-exact by construction

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x00A1, 0, 0, 0), "x", 1)); // unknown field: no coordinates in this case
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x00A1, 0, 0, 0), "memberIndex", 0)); // unknown field: every member snaps
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x00A1, 0, 0, 0), "layerUnits", 1)); // unknown field (moveParty's name)
    }
}
