using Pal.Extract;
using Xunit;
using static Pal.Extract.Tests.OpcodeCounterexampleHarness;

namespace Pal.Extract.Tests;

// M5 opcode scene-movement object batch 1 (#108, the 13-opcode object subdomain's first
// batch of 7) - setAutoEntry 0x0024, setTriggerEntry 0x0025, setTriggerMode 0x0040,
// setState 0x0049, nullifyObject 0x004B, hide 0x0052, setBattleHidingTime 0x005C.
// Topology per script.c: all seven opcode bits live in the shared PAL_InterpretInstruction
// switch (trigger switch 3206-3440 and auto switch 3522-3641 have no case for any of them),
// every case appears exactly once, and every successor is sequential (plain break into the
// script.c:3085 tail return wScriptEntry + 1; no jumps, no entry re-execution stalls).
// Semantics per the reference cases:
//   - 0x0024/0x0025/0x0040/0x0049 are the setState family: the shared preamble selects
//     pCurrent from operand 0 (0/0xFFFF = caller, else a 1-based event-object index,
//     script.c:626-638) and each case additionally gates on operand 0 != 0 (WORD 0 skips
//     the case entirely, so the caller form is 0xFFFF only), then writes
//     pCurrent->wAutoScript/wTriggerScript/wTriggerMode/sState = operand 1
//     (script.c:1137-1145/1147-1155/1613-1621/1711-1717).
//   - 0x004B nullifyObject sets the caller's vanish countdown to the fixed -15 with no
//     operands (script.c:1726-1731): while sVanishTime != 0 the object's trigger-zone
//     checks and auto scripts are skipped (play.c:87-94/178) and negative values count up
//     toward zero while the sprite stays drawn (scene.c:247 hides only sVanishTime > 0).
//   - 0x0052 hide acts on the caller pEvtObj (no object operand): sState *= -1 toggles the
//     hidden sign and sVanishTime = frames with WORD 0 normalized to 800 at runtime only
//     (script.c:1794-1800).
//   - 0x005C setBattleHidingTime writes g_Battle.iHidingTime = -(INT)units
//     (script.c:1907-1912): the negative value is a pending form the battle code negates
//     (and scales by 20 outside PAL_CLASSIC, fight.c:3529-3542); the encoding keeps the
//     positive unit count because the negation is runtime-only.
// Signedness: only setState.state is a SignedWord (sState is a SHORT field, global.h:103;
// the 0x0094 stateMatchJump (SHORT) comparison convention; negative sState = hidden,
// scene.c:247); every other numeric field is an unsigned WORD - the entry fields of
// 0x0024/0x0025 reach 41099/43524 in real data, past the signed range. Counter-examples
// follow the #78/#96 skeleton: edit a semantic field -> re-encoded bytes differ from the
// source bytes and equal the hand-computed value; unknown fields and out-of-range values
// are rejected.
public sealed class OpcodeSceneMovementObjectBatch1Tests
{
    // --- 0x0024 setAutoEntry (#78-named, #108 re-verified, shared, script.c:1137-1145):
    //     pCurrent->wAutoScript = operand1 under the operand0 != 0 gate (1141) - the
    //     preamble-selected object (0/0xFFFF = caller, else 1-based, script.c:626-638), so
    //     WORD 0 eventObjectId is the skipped no-op form and 0xFFFF the caller. Real data:
    //     420 hits; eventObjectId 0x5/0xFFFFx46/1-based 26..4810; entry 0x50 (disables the
    //     auto script), 373..41099 (145 entries past 0x7FFF keep entry unsigned); operand2
    //     all zero. ---
    [Fact]
    public void SetAutoEntry_edits_object_and_unsigned_entry()
    {
        byte[] original = Words(0x0024, 57, 373, 0); // the real-data @0133 form
        var command = Decode(0, 0x0024, 57, 373, 0);
        Assert.Equal("setAutoEntry", (string)command["op"]!);
        Assert.Equal(57, (int)command["eventObjectId"]!);
        Assert.Equal(373, (int)command["entry"]!);

        Assert.Equal(original, Reencode(command)); // both mapped WORDs re-encode byte-exact
        Assert.Equal(Words(0x0024, unchecked((int)0xFFFF), 373, 0), EditAndReencode(Decode(0, 0x0024, 57, 373, 0), "eventObjectId", 65_535)); // the caller form
        Assert.Equal(Words(0x0024, 0, 373, 0), EditAndReencode(Decode(0, 0x0024, 57, 373, 0), "eventObjectId", 0)); // the gated no-op form still encodes
        Assert.Equal(Words(0x0024, 4810, 373, 0), EditAndReencode(Decode(0, 0x0024, 57, 373, 0), "eventObjectId", 4810)); // real-data 1-based top
        Assert.Equal(Words(0x0024, 57, 0, 0), EditAndReencode(Decode(0, 0x0024, 57, 373, 0), "entry", 0)); // entry 0 disables the auto script
        Assert.Equal(Words(0x0024, 57, 41_099, 0), EditAndReencode(Decode(0, 0x0024, 57, 373, 0), "entry", 41_099)); // real-data entry top (0xA08B), past the signed range
        Assert.Equal(Words(0x0024, 57, 65_535, 0), EditAndReencode(Decode(0, 0x0024, 57, 373, 0), "entry", 65_535)); // full unsigned WORD

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0024, 57, 373, 0), "entry", 374));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0024, 57, 373, 0), "objectId", 57)); // unknown field: the object union's name, not event-object
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0024, 57, 373, 0), "autoScript", 373)); // unknown field (the struct member's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0024, 57, 373, 0), "to", "L0001")); // unknown field: plain WORD entry, not this file's label
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0024, 57, 373, 0), "entry", -1)); // unsigned WORD, real entries pass 0x7FFF
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0024, 57, 373, 0), "entry", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0024, 57, 373, 0), "eventObjectId", 65_536));
    }

    // --- 0x0025 setTriggerEntry (#78-named, #108 re-verified, shared, script.c:1147-1155):
    //     pCurrent->wTriggerScript = operand1, same preamble + gate shape as 0x0024. Real
    //     data: 225 hits; eventObjectId 0x1/0xFFFFx2/1-based 4..4725; entry 0x3, 59..43524
    //     (the 0xAA04 top nearly reaches the 43538-entry chunk end); operand2 all zero. ---
    [Fact]
    public void SetTriggerEntry_edits_object_and_unsigned_entry()
    {
        byte[] original = Words(0x0025, unchecked((int)0xFFFF), 355, 0); // the real-data @0180 caller form
        var command = Decode(0, 0x0025, unchecked((int)0xFFFF), 355, 0);
        Assert.Equal("setTriggerEntry", (string)command["op"]!);
        Assert.NotEqual("setAutoEntry", (string)command["op"]!); // the two entry writers keep two verbs
        Assert.Equal(65_535, (int)command["eventObjectId"]!); // 0xFFFF = the caller's object
        Assert.Equal(355, (int)command["entry"]!);

        Assert.Equal(original, Reencode(command)); // both mapped WORDs re-encode byte-exact
        Assert.Equal(Words(0x0025, 10, 355, 0), EditAndReencode(Decode(0, 0x0025, unchecked((int)0xFFFF), 355, 0), "eventObjectId", 10)); // a 1-based object keeps the caller form's entry
        Assert.Equal(Words(0x0025, 4, 355, 0), EditAndReencode(Decode(0, 0x0025, unchecked((int)0xFFFF), 355, 0), "eventObjectId", 4)); // real-data 1-based bottom
        Assert.Equal(Words(0x0025, 4725, 355, 0), EditAndReencode(Decode(0, 0x0025, unchecked((int)0xFFFF), 355, 0), "eventObjectId", 4725)); // real-data 1-based top
        Assert.Equal(Words(0x0025, unchecked((int)0xFFFF), 0, 0), EditAndReencode(Decode(0, 0x0025, unchecked((int)0xFFFF), 355, 0), "entry", 0)); // entry 0 clears the trigger script
        Assert.Equal(Words(0x0025, unchecked((int)0xFFFF), 43_524, 0), EditAndReencode(Decode(0, 0x0025, unchecked((int)0xFFFF), 355, 0), "entry", 43_524)); // real-data entry top (0xAA04)

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0025, unchecked((int)0xFFFF), 355, 0), "entry", 356));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0025, unchecked((int)0xFFFF), 355, 0), "objectId", 10)); // unknown field (object union's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0025, unchecked((int)0xFFFF), 355, 0), "triggerScript", 355)); // unknown field (the struct member's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0025, unchecked((int)0xFFFF), 355, 0), "to", "L0001")); // unknown field: plain WORD entry, not this file's label
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0025, unchecked((int)0xFFFF), 355, 0), "entry", -1)); // unsigned WORD
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0025, unchecked((int)0xFFFF), 355, 0), "entry", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0025, unchecked((int)0xFFFF), 355, 0), "eventObjectId", 65_536));
    }

    // --- 0x0040 setTriggerMode (#78-named, #108 re-verified, shared, script.c:1613-1621):
    //     pCurrent->wTriggerMode = operand1 under the operand0 != 0 gate - the kTrigger*
    //     constant 0..8 (global.h:77-88; real data exactly 0..8, 0 = kTriggerNone,
    //     8 = kTriggerTouchFarthest). Real data: 204 hits; eventObjectId 0x2/0xFFFFx163
    //     (the caller majority)/1-based 10..4674; mode 0x98/2x57/6x19/1/3..8; operand2 all
    //     zero. ---
    [Fact]
    public void SetTriggerMode_edits_object_and_trigger_mode()
    {
        byte[] original = Words(0x0040, unchecked((int)0xFFFF), 4, 0); // the real-data @0066 caller form, kTriggerTouchNear
        var command = Decode(0, 0x0040, unchecked((int)0xFFFF), 4, 0);
        Assert.Equal("setTriggerMode", (string)command["op"]!);
        Assert.Equal(65_535, (int)command["eventObjectId"]!);
        Assert.Equal(4, (int)command["mode"]!);

        Assert.Equal(original, Reencode(command)); // both mapped WORDs re-encode byte-exact
        Assert.Equal(Words(0x0040, unchecked((int)0xFFFF), 0, 0), EditAndReencode(Decode(0, 0x0040, unchecked((int)0xFFFF), 4, 0), "mode", 0)); // kTriggerNone: the real-data @0175 disable form
        Assert.Equal(Words(0x0040, unchecked((int)0xFFFF), 8, 0), EditAndReencode(Decode(0, 0x0040, unchecked((int)0xFFFF), 4, 0), "mode", 8)); // kTriggerTouchFarthest, real-data top
        Assert.Equal(Words(0x0040, unchecked((int)0xFFFF), 2, 0), EditAndReencode(Decode(0, 0x0040, unchecked((int)0xFFFF), 4, 0), "mode", 2)); // the real-data majority mode
        Assert.Equal(Words(0x0040, 10, 4, 0), EditAndReencode(Decode(0, 0x0040, unchecked((int)0xFFFF), 4, 0), "eventObjectId", 10)); // the real-data @00AA 1-based bottom
        Assert.Equal(Words(0x0040, 4674, 4, 0), EditAndReencode(Decode(0, 0x0040, unchecked((int)0xFFFF), 4, 0), "eventObjectId", 4674)); // real-data 1-based top
        Assert.Equal(Words(0x0040, 0, 4, 0), EditAndReencode(Decode(0, 0x0040, unchecked((int)0xFFFF), 4, 0), "eventObjectId", 0)); // the gated no-op form still encodes

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0040, unchecked((int)0xFFFF), 4, 0), "mode", 5));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0040, unchecked((int)0xFFFF), 4, 0), "objectId", 10)); // unknown field (object union's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0040, unchecked((int)0xFFFF), 4, 0), "triggerMethod", 4)); // unknown field (the reference comment's wording)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0040, unchecked((int)0xFFFF), 4, 0), "state", 4)); // unknown field (setState's name)
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0040, unchecked((int)0xFFFF), 4, 0), "mode", -1)); // unsigned WORD
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0040, unchecked((int)0xFFFF), 4, 0), "mode", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0040, unchecked((int)0xFFFF), 4, 0), "eventObjectId", 65_536));
    }

    // --- 0x0049 setState (#78-named, #108 re-verified, shared, script.c:1711-1717):
    //     pCurrent->sState = operand1 under the operand0 != 0 gate - sState is a SHORT
    //     field (global.h:103; kObjStateHidden=0/Normal=1/Blocker=2, global.h:74-81), so
    //     the binding stays signedWord (the 0x0094 stateMatchJump (SHORT) comparison
    //     convention; negative sState = hidden, scene.c:247). Real data: 1374 hits;
    //     eventObjectId 0x20/0xFFFFx326/1-based 4..4809; state 0x838/1x281/2x250/3x5;
    //     operand2 all zero. ---
    [Fact]
    public void SetState_edits_object_and_signed_state()
    {
        byte[] original = Words(0x0049, 20, 2, 0); // the real-data @0185 blocker form
        var command = Decode(0, 0x0049, 20, 2, 0);
        Assert.Equal("setState", (string)command["op"]!);
        Assert.Equal(20, (int)command["eventObjectId"]!);
        Assert.Equal(2, (int)command["state"]!);

        Assert.Equal(original, Reencode(command)); // both mapped WORDs re-encode byte-exact
        Assert.Equal(Words(0x0049, 4, 2, 0), EditAndReencode(Decode(0, 0x0049, 20, 2, 0), "eventObjectId", 4)); // the real-data 1-based bottom keeps the blocker state
        Assert.Equal(Words(0x0049, 20, 1, 0), EditAndReencode(Decode(0, 0x0049, 20, 2, 0), "state", 1)); // kObjStateNormal
        Assert.Equal(Words(0x0049, 20, 0, 0), EditAndReencode(Decode(0, 0x0049, 20, 2, 0), "state", 0)); // kObjStateHidden: the majority real form
        Assert.Equal(Words(0x0049, 20, 3, 0), EditAndReencode(Decode(0, 0x0049, 20, 2, 0), "state", 3)); // the real-data top value
        Assert.Equal(Words(0x0049, 20, unchecked((int)0xFF85), 0), EditAndReencode(Decode(0, 0x0049, 20, 2, 0), "state", -123)); // a negative state encodes two's-complement (hidden sign form)
        Assert.Equal(-123, (int)Decode(0, 0x0049, 20, unchecked((int)0xFF85), 0)["state"]!); // and decodes back through the SHORT cast
        Assert.Equal(Words(0x0049, 20, 32767, 0), EditAndReencode(Decode(0, 0x0049, 20, 2, 0), "state", 32_767)); // signed WORD top
        Assert.Equal(Words(0x0049, unchecked((int)0xFFFF), 2, 0), EditAndReencode(Decode(0, 0x0049, 20, 2, 0), "eventObjectId", 65_535)); // the caller form
        Assert.Equal(Words(0x0049, 0, 2, 0), EditAndReencode(Decode(0, 0x0049, 20, 2, 0), "eventObjectId", 0)); // the gated no-op form still encodes

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0049, 20, 2, 0), "state", 1));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0049, 20, 2, 0), "objectId", 20)); // unknown field (object union's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0049, 20, 2, 0), "objectState", 2)); // unknown field name
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0049, 20, 2, 0), "stateId", 2)); // unknown field name
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0049, 20, 2, 0), "state", -32_769)); // beyond signed WORD
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0049, 20, 2, 0), "state", 32_768)); // beyond signed WORD
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0049, 20, 2, 0), "eventObjectId", 65_536)); // object id stays unsigned
    }

    // --- 0x004B nullifyObject (new, shared, script.c:1726-1731): the caller event
    //     object's vanish countdown becomes the fixed -15 (pEvtObj->sVanishTime = -15,
    //     1730) - no operands; while sVanishTime != 0 the object's trigger-zone checks and
    //     auto scripts are skipped (play.c:87-94/178), and negative values count up toward
    //     zero while the sprite stays drawn (scene.c:247 hides only sVanishTime > 0, unlike
    //     hide 0x0052). Real data: 1 hit with all three operands zero. ---
    [Fact]
    public void NullifyObject_maps_no_fields_and_rejects_any_semantic_field()
    {
        byte[] original = Words(0x004B, 0, 0, 0); // the real-data @A071 single hit
        var command = Decode(0, 0x004B, 0, 0, 0);
        Assert.Equal("nullifyObject", (string)command["op"]!);
        Assert.NotEqual("hide", (string)command["op"]!); // the fixed -15 nullify keeps its own verb
        Assert.Equal(original, Reencode(command)); // no fields: byte-exact by construction, the three WORDs are preserved

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x004B, 0, 0, 0), "vanishTime", -15)); // unknown field: the countdown is a fixed constant
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x004B, 0, 0, 0), "frames", 15)); // unknown field (hide's field name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x004B, 0, 0, 0), "eventObjectId", 57)); // unknown field: acts on the caller, no object operand
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x004B, 0, 0, 0), "state", 0)); // unknown field: sState is untouched
    }

    // --- 0x0052 hide (#78-named, #108 re-verified, shared, script.c:1794-1800): acts on
    //     the caller pEvtObj - sState *= -1 toggles the hidden sign (scene.c:247 draws only
    //     sState > 0) and the vanish countdown sVanishTime becomes frames with WORD 0
    //     normalized to 800 at runtime only (encoding stays 0). Real data: 3 hits - frames
    //     0x1 (the default-800 form)/100/150; operands 1/2 all zero. ---
    [Fact]
    public void Hide_edits_frames_and_keeps_the_caller_scope()
    {
        byte[] original = Words(0x0052, 150, 0, 0); // the real-data @A0D8 form
        var command = Decode(0, 0x0052, 150, 0, 0);
        Assert.Equal("hide", (string)command["op"]!);
        Assert.Equal(150, (int)command["frames"]!);

        Assert.Equal(original, Reencode(command)); // the mapped WORD and the preserved pair re-encode byte-exact
        Assert.Equal(Words(0x0052, 0, 0, 0), EditAndReencode(Decode(0, 0x0052, 150, 0, 0), "frames", 0)); // the real-data @A0A7 default-800 form encodes 0
        Assert.Equal(Words(0x0052, 100, 0, 0), EditAndReencode(Decode(0, 0x0052, 150, 0, 0), "frames", 100)); // the real-data @A0DC form
        Assert.Equal(Words(0x0052, 800, 0, 0), EditAndReencode(Decode(0, 0x0052, 150, 0, 0), "frames", 800)); // the runtime default stays encodable
        Assert.Equal(Words(0x0052, 65_535, 0, 0), EditAndReencode(Decode(0, 0x0052, 150, 0, 0), "frames", 65_535)); // full unsigned WORD

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0052, 150, 0, 0), "frames", 151));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0052, 150, 0, 0), "eventObjectId", 57)); // unknown field: acts on the caller, no object operand
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0052, 150, 0, 0), "vanishTime", 150)); // unknown field (the struct member's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0052, 150, 0, 0), "state", -150)); // unknown field: the sState toggle has no operand
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0052, 150, 0, 0), "frames", -1)); // unsigned WORD, the 0->800 fallback keeps 0 explicit
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0052, 150, 0, 0), "frames", 65_536));
    }

    // --- 0x005C setBattleHidingTime (new, shared, script.c:1907-1912): the battle global
    //     g_Battle.iHidingTime = -(INT)units (1911, battle.h:202) - the negative value is
    //     a pending form PAL_BattleCheckHidingEffect negates and scales by 20 outside
    //     PAL_CLASSIC (fight.c:3529-3542); while the countdown is positive the party's
    //     battle sprites are not drawn (battle.c:207) and enemy actions are skipped
    //     (fight.c:1716); WORD 0 clears the hiding state. The negation is runtime-only, so
    //     the encoding keeps the positive unit count. Real data: 1 hit, units 3. ---
    [Fact]
    public void SetBattleHidingTime_edits_units_and_stays_unsigned()
    {
        byte[] original = Words(0x005C, 3, 0, 0); // the real-data @9A53 single hit
        var command = Decode(0, 0x005C, 3, 0, 0);
        Assert.Equal("setBattleHidingTime", (string)command["op"]!);
        Assert.Equal(3, (int)command["units"]!);

        Assert.Equal(original, Reencode(command)); // the mapped WORD and the preserved pair re-encode byte-exact
        Assert.Equal(Words(0x005C, 0, 0, 0), EditAndReencode(Decode(0, 0x005C, 3, 0, 0), "units", 0)); // WORD 0 clears the hiding state
        Assert.Equal(Words(0x005C, 65_535, 0, 0), EditAndReencode(Decode(0, 0x005C, 3, 0, 0), "units", 65_535)); // full unsigned WORD

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x005C, 3, 0, 0), "units", 4));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x005C, 3, 0, 0), "frames", 3)); // unknown field: the runtime x20 scaling means units, not raw frames
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x005C, 3, 0, 0), "hidingTime", 3)); // unknown field (the global's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x005C, 3, 0, 0), "eventObjectId", 3)); // unknown field: writes a battle global, no object operand
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x005C, 3, 0, 0), "units", -1)); // unsigned WORD: the negation is runtime-only, not an encoding
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x005C, 3, 0, 0), "units", 65_536));
    }
}
