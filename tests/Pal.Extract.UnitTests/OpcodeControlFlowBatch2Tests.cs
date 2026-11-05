using Pal.Extract;
using Xunit;
using static Pal.Extract.Tests.OpcodeCounterexampleHarness;

namespace Pal.Extract.Tests;

// M5 opcode batch 2 (#97): conditional jumps part 1 plus the re-verified markFailed /
// requestReload. All ten opcodes live in the shared PAL_InterpretInstruction switch, so
// they are shared-executor commands; jump-class opcodes set wScriptEntry = operand - 1
// inside the interpreter and re-enter at the caller with the target address (target WORD 0
// references the label at index 0). Counter-examples follow the #78/#96 skeleton: edit a
// semantic field -> re-encoded bytes differ from the source bytes and equal the hand-computed
// value; dangling or missing jump references are rejected.
public sealed class OpcodeControlFlowBatch2Tests
{
    // --- 0x0041 markFailed (#78 已具名，复核): clears g_fScriptSuccess; no operands. ---
    [Fact]
    public void MarkFailed_round_trips_preserved_operands_and_rejects_unknown_fields()
    {
        byte[] original = Words(0x0041, 5, 6, 7);
        var command = Decode(0, 0x0041, 5, 6, 7);
        Assert.Equal("markFailed", (string)command["op"]!);

        Assert.Equal(original, Reencode(command)); // no mapped fields: byte-exact

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0041, 5, 6, 7), "to", "L0001"));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0041, 5, 6, 7), "frames", 1));
    }

    // --- 0x004D waitForAnyKey: blocks on KeySearch/KeyMenu (PAL_WaitForKey(0)); no operands. ---
    [Fact]
    public void WaitForAnyKey_round_trips_preserved_operands_and_rejects_unknown_fields()
    {
        byte[] original = Words(0x004D, 8, 9, 10);
        var command = Decode(0, 0x004D, 8, 9, 10);
        Assert.Equal("waitForAnyKey", (string)command["op"]!);

        Assert.Equal(original, Reencode(command)); // no mapped fields: byte-exact

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x004D, 8, 9, 10), "to", "L0001"));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x004D, 8, 9, 10), "frames", 0));
    }

    // --- 0x004E requestReload (#78 已具名，复核): reloads the current save slot next tick and
    //     returns 0, stopping the run; no operands. The stop-class successor is declarative;
    //     the codec mapping itself is operand-free and byte-exact. ---
    [Fact]
    public void RequestReload_round_trips_preserved_operands_and_rejects_unknown_fields()
    {
        byte[] original = Words(0x004E, 3, 4, 5);
        var command = Decode(0, 0x004E, 3, 4, 5);
        Assert.Equal("requestReload", (string)command["op"]!);

        Assert.Equal(original, Reencode(command)); // no mapped fields: byte-exact

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x004E, 3, 4, 5), "to", "L0001"));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x004E, 3, 4, 5), "saveSlot", 1));
    }

    // --- 0x0058 itemShortageJump: jumps when the inventory holds fewer than `count` of
    //     `itemId`; the count comparison is signed ((SHORT)operand1 in script.c:1862). ---
    [Fact]
    public void ItemShortageJump_edits_itemId_count_and_target_and_rejects_dangling_targets()
    {
        byte[] original = Words(0x0058, 1, 2, 7);
        var command = Decode(0, 0x0058, 1, 2, 7);
        Assert.Equal("itemShortageJump", (string)command["op"]!);
        Assert.Equal(1, (int)command["itemId"]!);
        Assert.Equal(2, (int)command["count"]!);
        Assert.Equal("L0007", (string)command["to"]!);

        Assert.Equal(Words(0x0058, 1, 2, 4), EditAndReencode(command, "to", "L0004")); // operand 2 resolved
        Assert.Equal(Words(0x0058, 9, 2, 7), EditAndReencode(Decode(0, 0x0058, 1, 2, 7), "itemId", 9)); // object id, item view

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0058, 1, 2, 7), "count", 5));

        // The reference comparison is signed: a negative threshold re-encodes as its two's-complement WORD
        // (never taken: an unsigned inventory amount is never below a negative bound).
        Assert.Equal(Words(0x0058, 1, unchecked((int)0xFFFF), 7), EditAndReencode(Decode(0, 0x0058, 1, 2, 7), "count", -1));
        Assert.Equal(-1, (int)Decode(0, 0x0058, 1, unchecked((int)0xFFFF), 7)["count"]!); // and decodes back signed

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0058, 1, 2, 7), "count", 32768)); // beyond signed WORD
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0058, 1, 2, 7), "to", "L0100")); // dangling
        var missing = Decode(0, 0x0058, 1, 2, 7);
        missing.AsObject().Remove("to");
        Assert.Throws<InvalidDataException>(() => Reencode(missing));
        // Out-of-range words are rejected by the generic Word encode path's checked cast.
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0058, 1, 2, 7), "itemId", 65_536));
    }

    // --- 0x005D playerLacksPoisonJump: jumps when the current player role does not carry
    //     the poison object `objectId` (rgPoisonStatus scan by exact object index). ---
    [Fact]
    public void PlayerLacksPoisonJump_edits_poison_object_and_target_and_rejects_dangling_targets()
    {
        byte[] original = Words(0x005D, 560, 3, 6);
        var command = Decode(0, 0x005D, 560, 3, 6);
        Assert.Equal("playerLacksPoisonJump", (string)command["op"]!);
        Assert.Equal(560, (int)command["objectId"]!);
        Assert.Equal("L0003", (string)command["to"]!);

        Assert.Equal(Words(0x005D, 560, 5, 6), EditAndReencode(command, "to", "L0005")); // operand 1 resolved
        Assert.Equal(Words(0x005D, 564, 3, 6), EditAndReencode(Decode(0, 0x005D, 560, 3, 6), "objectId", 564)); // poison view of object.json

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x005D, 560, 3, 6), "objectId", 551));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x005D, 560, 3, 6), "to", "L0100"));
        var missing = Decode(0, 0x005D, 560, 3, 6);
        missing.AsObject().Remove("to");
        Assert.Throws<InvalidDataException>(() => Reencode(missing));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x005D, 560, 3, 6), "objectId", 65_536));
    }

    // --- 0x005E enemyLacksPoisonJump: same poison check against the enemy's rgPoisons. ---
    [Fact]
    public void EnemyLacksPoisonJump_edits_poison_object_and_target_and_rejects_dangling_targets()
    {
        byte[] original = Words(0x005E, 555, 2, 9);
        var command = Decode(0, 0x005E, 555, 2, 9);
        Assert.Equal("enemyLacksPoisonJump", (string)command["op"]!);
        Assert.Equal(555, (int)command["objectId"]!);
        Assert.Equal("L0002", (string)command["to"]!);

        Assert.Equal(Words(0x005E, 555, 7, 9), EditAndReencode(command, "to", "L0007"));
        Assert.Equal(Words(0x005E, 558, 2, 9), EditAndReencode(Decode(0, 0x005E, 555, 2, 9), "objectId", 558));

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x005E, 555, 2, 9), "to", "L0006"));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x005E, 555, 2, 9), "to", "L0100"));
        var missing = Decode(0, 0x005E, 555, 2, 9);
        missing.AsObject().Remove("to");
        Assert.Throws<InvalidDataException>(() => Reencode(missing));
    }

    // --- 0x0061 notPoisonedJump: jumps when the current player role carries no poison at
    //     all (PAL_IsPlayerPoisonedByLevel(role, 0)); the target is the only operand. ---
    [Fact]
    public void NotPoisonedJump_edits_target_and_encodes_the_zero_target_as_index_zero_label()
    {
        byte[] original = Words(0x0061, 4, 5, 6);
        var command = Decode(0, 0x0061, 4, 5, 6);
        Assert.Equal("notPoisonedJump", (string)command["op"]!);
        Assert.Equal("L0004", (string)command["to"]!);

        byte[] edited = EditAndReencode(command, "to", "L0002");
        Assert.NotEqual(original, edited);
        Assert.Equal(Words(0x0061, 2, 5, 6), edited); // operands 1 and 2 keep their source WORDs

        // Zero-target rule: WORD 0 references the label at index 0 and re-encodes as 0.
        Assert.Equal(Words(0x0061, 0, 5, 6), EditAndReencode(Decode(0, 0x0061, 4, 5, 6), "to", "L0000"));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0061, 4, 5, 6), "to", "L0100"));
        var missing = Decode(0, 0x0061, 4, 5, 6);
        missing.AsObject().Remove("to");
        Assert.Throws<InvalidDataException>(() => Reencode(missing));
    }

    // --- 0x0064 enemyHpAboveJump: jumps when current HP * 100 exceeds max HP * `percent`;
    //     the percent operand multiplies as an unsigned WORD (no SHORT cast in script.c:1988). ---
    [Fact]
    public void EnemyHpAboveJump_edits_percent_and_target_and_rejects_dangling_targets()
    {
        byte[] original = Words(0x0064, 30, 3, 8);
        var command = Decode(0, 0x0064, 30, 3, 8);
        Assert.Equal("enemyHpAboveJump", (string)command["op"]!);
        Assert.Equal(30, (int)command["percent"]!);
        Assert.Equal("L0003", (string)command["to"]!);

        Assert.Equal(Words(0x0064, 30, 6, 8), EditAndReencode(command, "to", "L0006"));
        Assert.Equal(Words(0x0064, 99, 3, 8), EditAndReencode(Decode(0, 0x0064, 30, 3, 8), "percent", 99));
        Assert.Equal(Words(0x0064, unchecked((int)0xFFFF), 3, 8), EditAndReencode(Decode(0, 0x0064, 30, 3, 8), "percent", 65_535)); // full unsigned WORD is legal

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0064, 30, 3, 8), "percent", 50));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0064, 30, 3, 8), "to", "L0100"));
        var missing = Decode(0, 0x0064, 30, 3, 8);
        missing.AsObject().Remove("to");
        Assert.Throws<InvalidDataException>(() => Reencode(missing));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0064, 30, 3, 8), "percent", 65_536));
    }

    // --- 0x0068 enemyTurnJump: jumps when g_Battle.fEnemyMoving is set (enemy's turn). ---
    [Fact]
    public void EnemyTurnJump_edits_target_and_rejects_dangling_targets()
    {
        byte[] original = Words(0x0068, 5, 7, 9);
        var command = Decode(0, 0x0068, 5, 7, 9);
        Assert.Equal("enemyTurnJump", (string)command["op"]!);
        Assert.Equal("L0005", (string)command["to"]!);

        byte[] edited = EditAndReencode(command, "to", "L0001");
        Assert.NotEqual(original, edited);
        Assert.Equal(Words(0x0068, 1, 7, 9), edited); // operands 1 and 2 keep their source WORDs

        Assert.Equal(Words(0x0068, 0, 7, 9), EditAndReencode(Decode(0, 0x0068, 5, 7, 9), "to", "L0000")); // zero target

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0068, 5, 7, 9), "to", "L0100"));
        var missing = Decode(0, 0x0068, 5, 7, 9);
        missing.AsObject().Remove("to");
        Assert.Throws<InvalidDataException>(() => Reencode(missing));
    }

    // --- 0x0074 partyHpNotFullJump: jumps when any party member's HP is below their max. ---
    [Fact]
    public void PartyHpNotFullJump_edits_target_and_rejects_dangling_targets()
    {
        byte[] original = Words(0x0074, 3, 0, 9);
        var command = Decode(0, 0x0074, 3, 0, 9);
        Assert.Equal("partyHpNotFullJump", (string)command["op"]!);
        Assert.Equal("L0003", (string)command["to"]!);

        byte[] edited = EditAndReencode(command, "to", "L0005");
        Assert.NotEqual(original, edited);
        Assert.Equal(Words(0x0074, 5, 0, 9), edited); // operands 1 and 2 keep their source WORDs

        Assert.Equal(Words(0x0074, 0, 0, 9), EditAndReencode(Decode(0, 0x0074, 3, 0, 9), "to", "L0000")); // zero target

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0074, 3, 0, 9), "to", "L0100"));
        var missing = Decode(0, 0x0074, 3, 0, 9);
        missing.AsObject().Remove("to");
        Assert.Throws<InvalidDataException>(() => Reencode(missing));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0074, 3, 0, 9), "to", "shared##L0005"));
    }
}
