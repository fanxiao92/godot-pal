using Pal.Extract;
using Xunit;
using static Pal.Extract.Tests.OpcodeCounterexampleHarness;

namespace Pal.Extract.Tests;

// M5 opcode batch 3 (#98): conditional jumps part 2 plus delay / quitGame / randomBranch.
// All ten opcodes live in the shared PAL_InterpretInstruction switch, so they are
// shared-executor commands; jump-class opcodes set wScriptEntry = operand - 1 inside the
// interpreter and re-enter at the caller with the target address (target WORD 0 references
// the label at index 0), except 0x00A0 which never returns (PAL_Shutdown) and 0x00A2 which
// advances by RandomLong(0, count - 1) relative to the current entry. Counter-examples
// follow the #78/#96 skeleton: edit a semantic field -> re-encoded bytes differ from the
// source bytes and equal the hand-computed value; dangling or missing jump references are
// rejected.
public sealed class OpcodeControlFlowBatch3Tests
{
    // --- 0x0079 roleInPartyJump: jumps when a party member's role name word matches operand 0
    //     (script.c:2236 compares PlayerRoles.rgwName[rgParty[i].wPlayerRole], a WORD.DAT name
    //     index, not the role index itself). ---
    [Fact]
    public void RoleInPartyJump_edits_role_name_word_and_target_and_rejects_dangling_targets()
    {
        byte[] original = Words(0x0079, 1, 4, 9);
        var command = Decode(0, 0x0079, 1, 4, 9);
        Assert.Equal("roleInPartyJump", (string)command["op"]!);
        Assert.Equal(1, (int)command["roleNameWord"]!);
        Assert.Equal("L0004", (string)command["to"]!);

        Assert.Equal(Words(0x0079, 3, 4, 9), EditAndReencode(command, "roleNameWord", 3)); // role name word, not role index
        Assert.Equal(Words(0x0079, 1, 2, 9), EditAndReencode(Decode(0, 0x0079, 1, 4, 9), "to", "L0002")); // operand 1 resolved
        Assert.Equal(Words(0x0079, 1, 0, 9), EditAndReencode(Decode(0, 0x0079, 1, 4, 9), "to", "L0000")); // zero target

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0079, 1, 4, 9), "roleNameWord", 6));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0079, 1, 4, 9), "to", "L0100")); // dangling
        var missing = Decode(0, 0x0079, 1, 4, 9);
        missing.AsObject().Remove("to");
        Assert.Throws<InvalidDataException>(() => Reencode(missing));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0079, 1, 4, 9), "roleNameWord", 65_536));
    }

    // --- 0x0081 notFacingObjectJump: jumps (with g_fScriptSuccess = FALSE) when the player is
    //     not facing event object operand 0 within zone operand 1 (|dx| + 2|dy| < units*32+16),
    //     or when the object is outside the current scene's event object range; a facing match
    //     with zone > 0 also arms the object's trigger mode for the next frame. ---
    [Fact]
    public void NotFacingObjectJump_edits_event_object_zone_and_target_and_rejects_dangling_targets()
    {
        byte[] original = Words(0x0081, 5, 2, 3);
        var command = Decode(0, 0x0081, 5, 2, 3);
        Assert.Equal("notFacingObjectJump", (string)command["op"]!);
        Assert.Equal(5, (int)command["eventObjectId"]!);
        Assert.Equal(2, (int)command["zoneUnits"]!);
        Assert.Equal("L0003", (string)command["to"]!);

        Assert.Equal(Words(0x0081, 6, 2, 3), EditAndReencode(command, "eventObjectId", 6)); // 1-based global event object index
        Assert.Equal(Words(0x0081, 5, 4, 3), EditAndReencode(Decode(0, 0x0081, 5, 2, 3), "zoneUnits", 4));
        Assert.Equal(Words(0x0081, 5, 2, 7), EditAndReencode(Decode(0, 0x0081, 5, 2, 3), "to", "L0007"));

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0081, 5, 2, 3), "zoneUnits", 1));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0081, 5, 2, 3), "to", "L0100"));
        var missing = Decode(0, 0x0081, 5, 2, 3);
        missing.AsObject().Remove("to");
        Assert.Throws<InvalidDataException>(() => Reencode(missing));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0081, 5, 2, 3), "zone", 4)); // unknown field
    }

    // --- 0x0083 objectOutOfZoneJump: jumps (with g_fScriptSuccess = FALSE) when event object
    //     operand 0 is not in zone operand 1 of the calling object (script.c:2463 measures the
    //     caller against the operand-0-selected object: |dx| + 2|dy| >= units*32+16), or when
    //     the object is outside the current scene's event object range. ---
    [Fact]
    public void ObjectOutOfZoneJump_edits_event_object_zone_and_target_and_rejects_dangling_targets()
    {
        byte[] original = Words(0x0083, 9, 3, 6);
        var command = Decode(0, 0x0083, 9, 3, 6);
        Assert.Equal("objectOutOfZoneJump", (string)command["op"]!);
        Assert.Equal(9, (int)command["eventObjectId"]!);
        Assert.Equal(3, (int)command["zoneUnits"]!);
        Assert.Equal("L0006", (string)command["to"]!);

        Assert.Equal(Words(0x0083, 2, 3, 6), EditAndReencode(command, "eventObjectId", 2));
        Assert.Equal(Words(0x0083, 9, 5, 6), EditAndReencode(Decode(0, 0x0083, 9, 3, 6), "zoneUnits", 5));
        Assert.Equal(Words(0x0083, 9, 3, 1), EditAndReencode(Decode(0, 0x0083, 9, 3, 6), "to", "L0001"));
        Assert.Equal(Words(0x0083, 9, 3, 0), EditAndReencode(Decode(0, 0x0083, 9, 3, 6), "to", "L0000")); // zero target

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0083, 9, 3, 6), "zoneUnits", 1));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0083, 9, 3, 6), "to", "L0100"));
        var missing = Decode(0, 0x0083, 9, 3, 6);
        missing.AsObject().Remove("to");
        Assert.Throws<InvalidDataException>(() => Reencode(missing));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0083, 9, 3, 6), "zoneUnits", 65_536));
    }

    // --- 0x0085 delay: blocks for operand 0 * 80 ms of wall-clock time (UTIL_Delay); unlike
    //     waitUpdate it runs no game updates, scene redraws or gesture updates. ---
    [Fact]
    public void Delay_round_trips_edits_delay_units_and_rejects_unknown_fields()
    {
        byte[] original = Words(0x0085, 25, 7, 8);
        var command = Decode(0, 0x0085, 25, 7, 8);
        Assert.Equal("delay", (string)command["op"]!);
        Assert.Equal(25, (int)command["delayUnits"]!);

        Assert.Equal(Words(0x0085, 30, 7, 8), EditAndReencode(command, "delayUnits", 30)); // operands 1 and 2 keep their source WORDs
        Assert.Equal(Words(0x0085, 65_535, 7, 8), EditAndReencode(Decode(0, 0x0085, 25, 7, 8), "delayUnits", 65_535)); // full WORD is legal

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0085, 25, 7, 8), "delayUnits", 1));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0085, 25, 7, 8), "frames", 30)); // unknown field
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0085, 25, 7, 8), "to", "L0001"));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0085, 25, 7, 8), "delayUnits", 65_536));
    }

    // --- 0x0086 itemNotEquippedJump: jumps when fewer than `count` copies of `itemId` are
    //     equipped across the party (script.c:2536 `y < operand1` compares against the plain
    //     WORD, no SHORT cast -> unsigned); the runtime rewrites a zero count to 1 in the
    //     loaded script entry in memory (2522-2523), which never reaches this codec. ---
    [Fact]
    public void ItemNotEquippedJump_edits_item_count_and_target_and_rejects_dangling_targets()
    {
        byte[] original = Words(0x0086, 87, 2, 5);
        var command = Decode(0, 0x0086, 87, 2, 5);
        Assert.Equal("itemNotEquippedJump", (string)command["op"]!);
        Assert.Equal(87, (int)command["itemId"]!);
        Assert.Equal(2, (int)command["count"]!);
        Assert.Equal("L0005", (string)command["to"]!);

        Assert.Equal(Words(0x0086, 195, 2, 5), EditAndReencode(command, "itemId", 195)); // object.json item view
        Assert.Equal(Words(0x0086, 87, 65_535, 5), EditAndReencode(Decode(0, 0x0086, 87, 2, 5), "count", 65_535)); // full unsigned WORD
        Assert.Equal(Words(0x0086, 87, 2, 1), EditAndReencode(Decode(0, 0x0086, 87, 2, 5), "to", "L0001"));

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0086, 87, 2, 5), "count", 1));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0086, 87, 2, 5), "to", "L0100"));
        var missing = Decode(0, 0x0086, 87, 2, 5);
        missing.AsObject().Remove("count");
        Assert.Throws<InvalidDataException>(() => Reencode(missing));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0086, 87, 2, 5), "count", 65_536));
    }

    // --- 0x0091 notFirstOfKindJump: jumps when this battle enemy is not the first enemy of the
    //     same object kind (script.c:2634 self_pos > 1); outside battle it never jumps. ---
    [Fact]
    public void NotFirstOfKindJump_edits_target_and_rejects_dangling_targets()
    {
        byte[] original = Words(0x0091, 4, 5, 6);
        var command = Decode(0, 0x0091, 4, 5, 6);
        Assert.Equal("notFirstOfKindJump", (string)command["op"]!);
        Assert.Equal("L0004", (string)command["to"]!);

        byte[] edited = EditAndReencode(command, "to", "L0001");
        Assert.NotEqual(original, edited);
        Assert.Equal(Words(0x0091, 1, 5, 6), edited); // operands 1 and 2 keep their source WORDs

        Assert.Equal(Words(0x0091, 0, 5, 6), EditAndReencode(Decode(0, 0x0091, 4, 5, 6), "to", "L0000")); // zero target

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0091, 4, 5, 6), "to", "L0100"));
        var missing = Decode(0, 0x0091, 4, 5, 6);
        missing.AsObject().Remove("to");
        Assert.Throws<InvalidDataException>(() => Reencode(missing));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0091, 4, 5, 6), "enemyId", 4)); // unknown field
    }

    // --- 0x0094 stateMatchJump: jumps when the event object selected by operand 0 (the shared
    //     InterpretInstruction preamble: 0/0xFFFF = caller, else 1-based index) has exactly
    //     `state` (script.c:2679 compares sState == (SHORT)operand1 -> signed). ---
    [Fact]
    public void StateMatchJump_edits_event_object_state_and_target_and_rejects_dangling_targets()
    {
        byte[] original = Words(0x0094, 3, 1, 6);
        var command = Decode(0, 0x0094, 3, 1, 6);
        Assert.Equal("stateMatchJump", (string)command["op"]!);
        Assert.Equal(3, (int)command["eventObjectId"]!);
        Assert.Equal(1, (int)command["state"]!);
        Assert.Equal("L0006", (string)command["to"]!);

        Assert.Equal(Words(0x0094, 0, 1, 6), EditAndReencode(command, "eventObjectId", 0)); // 0 = caller's own object
        Assert.Equal(Words(0x0094, 3, 1, 2), EditAndReencode(Decode(0, 0x0094, 3, 1, 6), "to", "L0002")); // operand 2 resolved

        // The state comparison is signed: -1 re-encodes as its two's-complement WORD and decodes back.
        Assert.Equal(Words(0x0094, 3, unchecked((int)0xFFFF), 6), EditAndReencode(Decode(0, 0x0094, 3, 1, 6), "state", -1));
        Assert.Equal(-1, (int)Decode(0, 0x0094, 3, unchecked((int)0xFFFF), 6)["state"]!);

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0094, 3, 1, 6), "state", 2));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0094, 3, 1, 6), "state", 32_768)); // beyond signed WORD
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0094, 3, 1, 6), "to", "L0100"));
        var missing = Decode(0, 0x0094, 3, 1, 6);
        missing.AsObject().Remove("to");
        Assert.Throws<InvalidDataException>(() => Reencode(missing));
    }

    // --- 0x0095 sceneMatchJump: jumps when the current scene (gpGlobals->wNumScene) equals
    //     `sceneId` (script.c:2689 plain WORD comparison). ---
    [Fact]
    public void SceneMatchJump_edits_scene_and_target_and_rejects_dangling_targets()
    {
        byte[] original = Words(0x0095, 13, 5, 8);
        var command = Decode(0, 0x0095, 13, 5, 8);
        Assert.Equal("sceneMatchJump", (string)command["op"]!);
        Assert.Equal(13, (int)command["sceneId"]!);
        Assert.Equal("L0005", (string)command["to"]!);

        Assert.Equal(Words(0x0095, 20, 5, 8), EditAndReencode(command, "sceneId", 20)); // scene.json index
        Assert.Equal(Words(0x0095, 13, 1, 8), EditAndReencode(Decode(0, 0x0095, 13, 5, 8), "to", "L0001"));
        Assert.Equal(Words(0x0095, 13, 0, 8), EditAndReencode(Decode(0, 0x0095, 13, 5, 8), "to", "L0000")); // zero target

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0095, 13, 5, 8), "sceneId", 1));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0095, 13, 5, 8), "to", "L0100"));
        var missing = Decode(0, 0x0095, 13, 5, 8);
        missing.AsObject().Remove("to");
        Assert.Throws<InvalidDataException>(() => Reencode(missing));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0095, 13, 5, 8), "sceneId", 65_536));
    }

    // --- 0x00A0 quitGame: shows the WIN95 ending screen, plays the additional credits and
    //     shuts the process down (PAL_Shutdown); the script never continues, so the successor
    //     is stop. No operands are mapped. ---
    [Fact]
    public void QuitGame_round_trips_preserved_operands_and_rejects_unknown_fields()
    {
        byte[] original = Words(0x00A0, 5, 6, 7);
        var command = Decode(0, 0x00A0, 5, 6, 7);
        Assert.Equal("quitGame", (string)command["op"]!);

        Assert.Equal(original, Reencode(command)); // no mapped fields: byte-exact

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x00A0, 5, 6, 7), "to", "L0001"));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x00A0, 5, 6, 7), "frames", 1));
    }

    // --- 0x00A2 randomBranch: advances by RandomLong(0, count - 1) relative to the current
    //     entry, i.e. continues at one of the next `count` entries (entry+1 .. entry+count via
    //     the shared return+1); the operand is a branch count, not an address array (unlike
    //     randomJump there is no rate and no target operand). Count 0 or 1 degenerates to the
    //     plain sequential successor. ---
    [Fact]
    public void RandomBranch_edits_branch_count_and_preserves_unmapped_operands()
    {
        byte[] original = Words(0x00A2, 3, 7, 9);
        var command = Decode(0, 0x00A2, 3, 7, 9);
        Assert.Equal("randomBranch", (string)command["op"]!);
        Assert.Equal(3, (int)command["count"]!);

        Assert.Equal(Words(0x00A2, 5, 7, 9), EditAndReencode(command, "count", 5)); // operands 1 and 2 keep their source WORDs
        Assert.Equal(Words(0x00A2, 65_535, 7, 9), EditAndReencode(Decode(0, 0x00A2, 3, 7, 9), "count", 65_535)); // full WORD is legal

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x00A2, 3, 7, 9), "count", 1));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x00A2, 3, 7, 9), "to", "L0001")); // no target operand
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x00A2, 3, 7, 9), "rate", 75)); // not randomJump
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x00A2, 3, 7, 9), "count", 65_536));
    }
}
