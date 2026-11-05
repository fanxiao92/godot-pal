using System.Buffers.Binary;
using System.Text.Json.Nodes;
using Pal.Extract;
using Xunit;

namespace Pal.Extract.Tests;

// M5 opcode scene-movement object batch 2 (#109, the 13-opcode object subdomain's
// closing batch of 6) - syncState 0x006F, setObjectLayer 0x007E, placeUsedItem 0x0084,
// animateObject 0x0087, setObjectScript 0x0090, setStateRange 0x009A.
// Topology per script.c: all six opcode bits live in the shared PAL_InterpretInstruction
// switch (trigger switch 3206-3440 and auto switch 3522-3641 have no case for any of
// them, each case appears exactly once), and every successor except placeUsedItem is
// sequential (plain break into the script.c:3085 tail return wScriptEntry + 1);
// placeUsedItem's two failure paths (event object outside the current scene's
// event-object window, script.c:2475-2481, or the placement tile in front of the party
// blocked, script.c:2493-2500) set wScriptEntry = operand2 - 1 and mark the script
// failed - the standard plain-reference conditional jump.
// Semantics per the reference cases:
//   - 0x006F syncState: if the preamble-selected object's sState (pCurrent,
//     script.c:626-638: 0/0xFFFF = caller, else a 1-based index) equals (SHORT)operand1,
//     the caller's sState becomes operand1 (script.c:2115-2122); no operand0 gate.
//   - 0x007E setObjectLayer: pCurrent->sLayer = (SHORT)operand1 (script.c:2285-2290);
//     sLayer is a SHORT draw-offset/draw-order field (global.h:100, scene.c:302/316).
//   - 0x0084 placeUsedItem: places the operand-0 event object one step in front of the
//     party with sState = (SHORT)operand1, jumping to operand 2 when the object is not
//     in the current scene or the tile is blocked (script.c:2473-2509).
//   - 0x0087 animateObject: PAL_NPCWalkOneStep(wCurEventObjectID, 0) at speed 0 moves
//     nothing and only advances the gesture frame (script.c:2542-2546, scene.c:873-900).
//   - 0x0090 setObjectScript (#78-named, #109 re-verified): rgObject[operand0].
//     rgwData[2 + operand2] = operand1 - the object union's script slots.
//   - 0x009A setStateRange (#78-named, #109 re-verified): the closed 1-based
//     event-object range operand0..operand1 writes lprgEventObject[i - 1].sState
//     (script.c:2758-2766).
// Signedness: the sState writers (syncState.state, placeUsedItem.state,
// setStateRange.state) and setObjectLayer.layer are SignedWord (SHORT storage,
// global.h:100/103; setObjectLayer's real data carries -10); every other field is an
// unsigned WORD. Counter-examples follow the #78/#96 skeleton: edit a semantic field ->
// re-encoded bytes differ from the source bytes and equal the hand-computed value;
// unknown fields and out-of-range values are rejected.
public sealed class OpcodeSceneMovementObjectBatch2Tests
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

    // --- 0x006F syncState (new, shared, script.c:2115-2122): "Sync the state of current
    //     event object with another event object" - if the preamble-selected object's
    //     sState equals (SHORT)operand1, the caller's sState becomes operand1 too; unlike
    //     the 0x0024 setState family there is no operand0 != 0 gate (WORD 0 selects the
    //     caller as pCurrent, a self-compare). Real data: 11 hits; eventObjectId 63..139
    //     all 1-based; state 0x10/1x1; operand2 0x10/1x1 (unused by the case). ---
    [Fact]
    public void SyncState_edits_object_and_signed_state()
    {
        byte[] original = Words(0x006F, 103, 0, 0); // the real-data @00FF form
        var command = Decode(0, 0x006F, 103, 0, 0);
        Assert.Equal("syncState", (string)command["op"]!);
        Assert.NotEqual("setState", (string)command["op"]!); // the conditional sync keeps its own verb
        Assert.Equal(103, (int)command["eventObjectId"]!);
        Assert.Equal(0, (int)command["state"]!);

        Assert.Equal(original, Reencode(command)); // both mapped WORDs re-encode byte-exact
        Assert.Equal(Words(0x006F, 63, 0, 0), EditAndReencode(Decode(0, 0x006F, 103, 0, 0), "eventObjectId", 63)); // real-data 1-based bottom
        Assert.Equal(Words(0x006F, 139, 0, 0), EditAndReencode(Decode(0, 0x006F, 103, 0, 0), "eventObjectId", 139)); // real-data 1-based top
        Assert.Equal(Words(0x006F, 102, 0, 0), EditAndReencode(Decode(0, 0x006F, 103, 0, 0), "eventObjectId", 102)); // the real-data @0102 form's object (its state 1 is the next assertion)
        Assert.Equal(Words(0x006F, 103, 1, 0), EditAndReencode(Decode(0, 0x006F, 103, 0, 0), "state", 1)); // the real-data @0102 form's state
        Assert.Equal(Words(0x006F, unchecked((int)0xFFFF), 0, 0), EditAndReencode(Decode(0, 0x006F, 103, 0, 0), "eventObjectId", 65_535)); // the ungated caller form still encodes
        Assert.Equal(Words(0x006F, 0, 0, 0), EditAndReencode(Decode(0, 0x006F, 103, 0, 0), "eventObjectId", 0)); // WORD 0 is the ungated self-compare form, no no-op skip
        Assert.Equal(Words(0x006F, 103, unchecked((int)0xFF85), 0), EditAndReencode(Decode(0, 0x006F, 103, 0, 0), "state", -123)); // a negative state encodes two's-complement
        Assert.Equal(-123, (int)Decode(0, 0x006F, 103, unchecked((int)0xFF85), 0)["state"]!); // and decodes back through the SHORT cast
        Assert.Equal(Words(0x006F, 103, 32_767, 0), EditAndReencode(Decode(0, 0x006F, 103, 0, 0), "state", 32_767)); // signed WORD top

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x006F, 103, 0, 0), "state", 1));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x006F, 103, 0, 0), "objectId", 103)); // unknown field (object union's name, not event-object)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x006F, 103, 0, 0), "eventState", 0)); // unknown field name
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x006F, 103, 0, 0), "to", "L0001")); // unknown field: the sync is unconditional control flow, no jump
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x006F, 103, 0, 0), "state", -32_769)); // beyond signed WORD
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x006F, 103, 0, 0), "state", 32_768)); // beyond signed WORD
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x006F, 103, 0, 0), "eventObjectId", 65_536)); // object id stays unsigned
    }

    // --- 0x007E setObjectLayer (new, shared, script.c:2285-2290): "Set the layer of
    //     event object" - pCurrent->sLayer = (SHORT)operand1; sLayer is a SHORT field
    //     (global.h:100) consumed as a vertical draw offset and draw-order value in
    //     half-tile units (scene.c:302 y += sLayer * 8 + 9, scene.c:316 draw priority
    //     sLayer * 8 + 2). Real data: 10 hits; eventObjectId 0xFFFF x1 (caller) / 1-based
    //     59..4168; layer -10 (0xFFF6)/0x3/3/7/10/13/33x2; operand2 all zero. ---
    [Fact]
    public void SetObjectLayer_edits_object_and_signed_layer()
    {
        byte[] original = Words(0x007E, 59, 3, 0); // the real-data @0AD5 form
        var command = Decode(0, 0x007E, 59, 3, 0);
        Assert.Equal("setObjectLayer", (string)command["op"]!);
        Assert.Equal(59, (int)command["eventObjectId"]!);
        Assert.Equal(3, (int)command["layer"]!);

        Assert.Equal(original, Reencode(command)); // both mapped WORDs re-encode byte-exact
        Assert.Equal(Words(0x007E, 59, 0, 0), EditAndReencode(Decode(0, 0x007E, 59, 3, 0), "layer", 0)); // the real-data @0AE2 layer-0 form
        Assert.Equal(Words(0x007E, unchecked((int)0xFFFF), 3, 0), EditAndReencode(Decode(0, 0x007E, 59, 3, 0), "eventObjectId", 65_535)); // the caller form keeps the edited layer
        var caller33 = Decode(0, 0x007E, 59, 3, 0);
        caller33["eventObjectId"] = 65_535;
        caller33["layer"] = 33;
        Assert.Equal(Words(0x007E, unchecked((int)0xFFFF), 33, 0), Reencode(caller33)); // the real-data @517C caller form, layer 33
        var negativeLayer = Decode(0, 0x007E, 59, 3, 0);
        negativeLayer["eventObjectId"] = 4168;
        negativeLayer["layer"] = -10;
        Assert.Equal(Words(0x007E, 4168, unchecked((int)0xFFF6), 0), Reencode(negativeLayer)); // the real-data @7DB4 negative-layer form
        Assert.Equal(-10, (int)Decode(0, 0x007E, 4168, unchecked((int)0xFFF6), 0)["layer"]!); // and it decodes back through the SHORT cast
        Assert.Equal(Words(0x007E, 59, unchecked((int)0xFFF6), 0), EditAndReencode(Decode(0, 0x007E, 59, 3, 0), "layer", -10)); // the signed evidence value on another object
        Assert.Equal(Words(0x007E, 59, 32_767, 0), EditAndReencode(Decode(0, 0x007E, 59, 3, 0), "layer", 32_767)); // signed WORD top
        Assert.Equal(Words(0x007E, 0, 3, 0), EditAndReencode(Decode(0, 0x007E, 59, 3, 0), "eventObjectId", 0)); // the caller form

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x007E, 59, 3, 0), "layer", 7));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x007E, 59, 3, 0), "objectId", 59)); // unknown field (object union's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x007E, 59, 3, 0), "layerUnits", 3)); // unknown field: moveParty's party-global name, sLayer has no x8 at write time
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x007E, 59, 3, 0), "sLayer", 3)); // unknown field (the struct member's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x007E, 59, 3, 0), "layer", -32_769)); // beyond signed WORD
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x007E, 59, 3, 0), "layer", 32_768)); // beyond signed WORD
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x007E, 59, 3, 0), "eventObjectId", 65_536)); // object id stays unsigned
    }

    // --- 0x0084 placeUsedItem (new, shared, script.c:2473-2509): "Place the item which
    //     player used as an event object to the scene" - operand0 is a 1-based global
    //     event-object id that must lie in the current scene's event-object window
    //     (2475-2481); the object is placed one step in front of the party (viewport +
    //     partyoffset + 16/-16 px x and 8/-8 px y by facing, 2483-2491) unless
    //     PAL_CheckObstacle blocks (2493-2500), then sState = (SHORT)operand1; either
    //     failure jumps to operand2 (plain reference) with the script marked failed.
    //     Real data: 2 hits (798/2/39656 @9AE6 and 3607/2/39656 @9BB0). ---
    [Fact]
    public void PlaceUsedItem_edits_object_state_and_jump_target()
    {
        var command = Decode(0, 0x0084, 798, 2, 5); // the real-data @9AE6 form, target 39656 mapped to the in-file label L0005
        Assert.Equal("placeUsedItem", (string)command["op"]!);
        Assert.Equal(798, (int)command["eventObjectId"]!);
        Assert.Equal(2, (int)command["state"]!);
        Assert.Equal("L0005", (string)command["to"]!); // the failure target is a chunk-4 reference, not a WORD

        Assert.Equal(Words(0x0084, 798, 2, 5), Reencode(command)); // the reference resolves back to the address
        Assert.Equal(Words(0x0084, 3607, 2, 5), EditAndReencode(Decode(0, 0x0084, 798, 2, 5), "eventObjectId", 3607)); // the real-data @9BB0 form's object
        Assert.Equal(Words(0x0084, 798, 1, 5), EditAndReencode(Decode(0, 0x0084, 798, 2, 5), "state", 1)); // a normal item state
        Assert.Equal(Words(0x0084, 798, unchecked((int)0xFF85), 5), EditAndReencode(Decode(0, 0x0084, 798, 2, 5), "state", -123)); // a negative state encodes two's-complement
        Assert.Equal(-123, (int)Decode(0, 0x0084, 798, unchecked((int)0xFF85), 5)["state"]!); // and decodes back through the SHORT cast
        Assert.Equal(Words(0x0084, 798, 2, 1), EditAndReencode(Decode(0, 0x0084, 798, 2, 5), "to", "L0001")); // the failure target moves
        Assert.Equal(Words(0x0084, 798, 2, 0), EditAndReencode(Decode(0, 0x0084, 798, 2, 5), "to", "L0000")); // WORD 0 is the legal L0000 reference form (real data never uses it)

        Assert.NotEqual(Words(0x0084, 798, 2, 5), EditAndReencode(Decode(0, 0x0084, 798, 2, 5), "to", "L0001"));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0084, 798, 2, 5), "objectId", 798)); // unknown field (object union's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0084, 798, 2, 5), "failTarget", "L0001")); // unknown field: nullable binding is the enemyDivide shape, not this plain reference
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0084, 798, 2, 5), "zoneUnits", 2)); // unknown field: the window check is the scene's object range, not the 0x0081/0x0083 zone metric
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0084, 798, 2, 5), "state", 32_768)); // beyond signed WORD
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0084, 798, 2, 5), "to", "L0008")); // dangling reference: outside the chunk
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0084, 798, 2, 5), "to", "not a label")); // malformed reference
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0084, 798, 2, 5), "eventObjectId", 65_536)); // object id stays unsigned
    }

    // --- 0x0087 animateObject (new, shared, script.c:2542-2546): "Animate the event
    //     object" - PAL_NPCWalkOneStep(wCurEventObjectID, 0) at speed 0 moves nothing
    //     (scene.c:887-888) and only advances the selected object's gesture frame
    //     (scene.c:891-900, 3-frame sprites wrap at 4); the callee returns early for id
    //     0 or past nEventObject (scene.c:873-876). Real data: 189 hits with all three
    //     operands zero - the caller form only. ---
    [Fact]
    public void AnimateObject_edits_object_and_keeps_preserved_operands()
    {
        byte[] original = Words(0x0087, 0, 0, 0); // the real-data @0349 form (all 189 hits share it)
        var command = Decode(0, 0x0087, 0, 0, 0);
        Assert.Equal("animateObject", (string)command["op"]!);
        Assert.Equal(0, (int)command["eventObjectId"]!); // WORD 0: the preamble selects the caller (id 0 = the callee's early return)

        Assert.Equal(original, Reencode(command)); // the mapped WORD and the preserved pair re-encode byte-exact
        Assert.Equal(Words(0x0087, unchecked((int)0xFFFF), 0, 0), EditAndReencode(Decode(0, 0x0087, 0, 0, 0), "eventObjectId", 65_535)); // the caller form
        Assert.Equal(Words(0x0087, 4168, 0, 0), EditAndReencode(Decode(0, 0x0087, 0, 0, 0), "eventObjectId", 4168)); // a 1-based object still encodes (no real hit, the preamble form)

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0087, 0, 0, 0), "eventObjectId", 4168));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0087, 0, 0, 0), "objectId", 0)); // unknown field (object union's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0087, 0, 0, 0), "frames", 1)); // unknown field: the animation advance is not a frame count operand
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0087, 0, 0, 0), "frame", 1)); // unknown field (setObjectFacing's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0087, 0, 0, 0), "speed", 2)); // unknown field: the speed is the fixed 0, not an operand
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0087, 0, 0, 0), "eventObjectId", 65_536)); // object id stays unsigned
    }

    // --- 0x0090 setObjectScript (#78-named, #109 re-verified, shared, script.c:2607-
    //     2613): rgObject[operand0].rgwData[2 + operand2] = operand1 - operand0 is a
    //     0-based object-union index (object.json, no -1), operand2 selects the view's
    //     script slots at union indices 2..4, and operand1 is the written WORD with 0
    //     clearing the slot. Real data: 3 hits, all clearing (objectId 435/454/485,
    //     value 0, slotIndex 0). ---
    [Fact]
    public void SetObjectScript_edits_object_value_and_slot()
    {
        byte[] original = Words(0x0090, 435, 0, 0); // the real-data @552E clearing form
        var command = Decode(0, 0x0090, 435, 0, 0);
        Assert.Equal("setObjectScript", (string)command["op"]!);
        Assert.Equal(435, (int)command["objectId"]!);
        Assert.Equal(0, (int)command["value"]!);
        Assert.Equal(0, (int)command["slotIndex"]!);

        Assert.Equal(original, Reencode(command)); // all three mapped WORDs re-encode byte-exact
        Assert.Equal(Words(0x0090, 454, 0, 0), EditAndReencode(Decode(0, 0x0090, 435, 0, 0), "objectId", 454)); // the real-data @A13F form's object
        Assert.Equal(Words(0x0090, 485, 0, 0), EditAndReencode(Decode(0, 0x0090, 435, 0, 0), "objectId", 485)); // the real-data @A195 form's object
        Assert.Equal(Words(0x0090, 435, 300, 0), EditAndReencode(Decode(0, 0x0090, 435, 0, 0), "value", 300)); // a chunk-4 script entry installs a script
        Assert.Equal(Words(0x0090, 435, 41_099, 0), EditAndReencode(Decode(0, 0x0090, 435, 0, 0), "value", 41_099)); // the setAutoEntry real-data entry top installs too (0x7FFF+ stays unsigned)
        Assert.Equal(Words(0x0090, 435, 65_535, 0), EditAndReencode(Decode(0, 0x0090, 435, 0, 0), "value", 65_535)); // full unsigned WORD
        Assert.Equal(Words(0x0090, 435, 0, 1), EditAndReencode(Decode(0, 0x0090, 435, 0, 0), "slotIndex", 1)); // union index 3
        Assert.Equal(Words(0x0090, 435, 0, 2), EditAndReencode(Decode(0, 0x0090, 435, 0, 0), "slotIndex", 2)); // union index 4, the third script slot

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0090, 435, 0, 0), "value", 300));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0090, 435, 0, 0), "eventObjectId", 435)); // unknown field: the object union's index, not an event object (#78 conclusion)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0090, 435, 0, 0), "entry", 300)); // unknown field (setAutoEntry's name; value stays the written WORD)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0090, 435, 0, 0), "slot", 1)); // unknown field name
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0090, 435, 0, 0), "value", -1)); // unsigned WORD, the 0x7FFF+ installs need it
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0090, 435, 0, 0), "value", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0090, 435, 0, 0), "objectId", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0090, 435, 0, 0), "slotIndex", 65_536));
    }

    // --- 0x009A setStateRange (#78-named, #109 re-verified, shared, script.c:2758-
    //     2766): the closed 1-based event-object range operand0..operand1 writes
    //     lprgEventObject[i - 1].sState = operand2 for every i; operand2 has no (SHORT)
    //     cast but assigns into the SHORT storage, keeping setState.state's signedWord
    //     binding; an empty range (from > to) writes nothing. Real data: 81 hits - from
    //     26..4747, to 27..4749 (one to=0 empty-range hit @3065), state 0x48/1x6/2x27. ---
    [Fact]
    public void SetStateRange_edits_range_and_signed_state()
    {
        byte[] original = Words(0x009A, 102, 103, 2); // the real-data @0678 two-object blocker form
        var command = Decode(0, 0x009A, 102, 103, 2);
        Assert.Equal("setStateRange", (string)command["op"]!);
        Assert.NotEqual("setState", (string)command["op"]!); // the range writer keeps its own verb
        Assert.Equal(102, (int)command["fromEventObjectId"]!);
        Assert.Equal(103, (int)command["toEventObjectId"]!);
        Assert.Equal(2, (int)command["state"]!);

        Assert.Equal(original, Reencode(command)); // all three mapped WORDs re-encode byte-exact
        var bottomPair = Decode(0, 0x009A, 102, 103, 2);
        bottomPair["fromEventObjectId"] = 26;
        bottomPair["toEventObjectId"] = 27;
        Assert.Equal(Words(0x009A, 26, 27, 2), Reencode(bottomPair)); // the real-data 1-based bottom pair
        var topPair = Decode(0, 0x009A, 102, 103, 2);
        topPair["fromEventObjectId"] = 4747;
        topPair["toEventObjectId"] = 4749;
        Assert.Equal(Words(0x009A, 4747, 4749, 2), Reencode(topPair)); // the real-data top pair
        Assert.Equal(Words(0x009A, 102, 103, 0), EditAndReencode(Decode(0, 0x009A, 102, 103, 2), "state", 0)); // kObjStateHidden, the real-data majority
        Assert.Equal(Words(0x009A, 102, 103, 1), EditAndReencode(Decode(0, 0x009A, 102, 103, 2), "state", 1)); // kObjStateNormal
        Assert.Equal(Words(0x009A, 102, 102, 2), EditAndReencode(Decode(0, 0x009A, 102, 103, 2), "toEventObjectId", 102)); // a single-object closed range
        var emptyRange = Decode(0, 0x009A, 102, 103, 2);
        emptyRange["fromEventObjectId"] = 1051;
        emptyRange["toEventObjectId"] = 0;
        emptyRange["state"] = 0;
        Assert.Equal(Words(0x009A, 1051, 0, 0), Reencode(emptyRange)); // the real-data @3065 empty-range form (to=0 < from writes nothing)
        Assert.Equal(Words(0x009A, 102, 103, unchecked((int)0xFF85)), EditAndReencode(Decode(0, 0x009A, 102, 103, 2), "state", -123)); // a negative state encodes two's-complement
        Assert.Equal(-123, (int)Decode(0, 0x009A, 102, 103, unchecked((int)0xFF85))["state"]!); // and decodes back through the SHORT storage

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x009A, 102, 103, 2), "state", 1));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x009A, 102, 103, 2), "eventObjectId", 102)); // unknown field: the range uses the from/to pair, not a single object
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x009A, 102, 103, 2), "fromObjectId", 26)); // unknown field (#78's renamed-away name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x009A, 102, 103, 2), "state", -32_769)); // beyond signed WORD
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x009A, 102, 103, 2), "state", 32_768)); // beyond signed WORD
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x009A, 102, 103, 2), "fromEventObjectId", 65_536)); // object ids stay unsigned
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x009A, 102, 103, 2), "toEventObjectId", 65_536));
    }
}
