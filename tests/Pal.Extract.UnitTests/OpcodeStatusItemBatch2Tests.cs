using System.Buffers.Binary;
using System.Text.Json.Nodes;
using Pal.Extract;
using Xunit;

namespace Pal.Extract.Tests;

// M5 opcode status-item batch 2 (#101): revive/remove equipment, poison and status verbs.
// revivePlayer is a re-verified entry from #78; removeEquipment, applyPoisonToEnemy,
// applyPoisonToPlayer, curePoisonByObjectIdForEnemy, curePoisonByObjectIdForPlayer,
// curePoisonsByLevel, setStatusPlayer, setStatusEnemy and clearPlayerStatus are new.
// All ten live in the shared PAL_InterpretInstruction switch (script.c:650) and, except for
// setStatusEnemy's resisted jump, advance sequentially (function tail return+1, script.c:3085).
// Poison operands are object.json poison-view object IDs (#82/#97 precedent); the status
// numbers are raw rgPlayerStatus/rgwStatus array row indices (no enum names hardcoded);
// every numeric operand of this batch is an unsigned WORD - none of the ten cases has a
// (SHORT) cast. The player-targeting operands, per script.c:
//   - 0x0022/0x0029/0x002B/0x002C operand 0 is the changeHp whole-party flag
//     (`if (pScript->rgwOperand[0])`: 0 = one player with wEventObjectID as the player role,
//     non-zero = every party member); 0x0028/0x002A use the same flag shape over battle
//     enemies (0 = g_Battle.rgEnemy[wEventObjectID], non-zero = all enemies).
//   - 0x0023 operand 0 is a fourth shape: a direct 0-based PLAYERROLES role index operand
//     (script.c:1108 `iPlayerRole = rgwOperand[0]`, used with no -1 and no caller fallback),
//     and its operand 1 is 0 = remove every part or 1..6 = that part 1-based (1129).
//   - 0x0022/0x002D/0x002F target the caller's player role (wEventObjectID) with no operand;
//     0x002E targets the battle enemy at wEventObjectID.
// Counter-examples follow the #78/#96 skeleton: edit a semantic field -> re-encoded bytes
// differ from the source bytes and equal the hand-computed value; unknown fields, dangling
// references and out-of-range values are rejected.
public sealed class OpcodeStatusItemBatch2Tests
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

    // --- 0x0022 revivePlayer (#78 named, re-verified; shared, script.c:1052): operand 0 is the
    //     whole-party flag (1057, same shape as changeHp), operand 1 an unsigned WORD scaling
    //     the restored HP: rgwHP = rgwMaxHP * operand1 / 10 (1068-1069/1088-1089, i.e. tenths of
    //     max HP); revival also cures poisons of level <= 3 (1071/1091) and every status
    //     (1072-1074/1092-1094). Operand 2 is unused by the reference case. ---
    [Fact]
    public void RevivePlayer_edits_whole_party_flag_and_health_tenths_and_preserves_operand2()
    {
        byte[] original = Words(0x0022, 0, 5, 9);
        var command = Decode(0, 0x0022, 0, 5, 9);
        Assert.Equal("revivePlayer", (string)command["op"]!);
        Assert.Equal(0, (int)command["scope"]!);
        Assert.Equal(5, (int)command["healthTenths"]!);

        Assert.Equal(Words(0x0022, 1, 5, 9), EditAndReencode(command, "scope", 1)); // whole party
        Assert.Equal(Words(0x0022, 0, 10, 9), EditAndReencode(Decode(0, 0x0022, 0, 5, 9), "healthTenths", 10)); // full heal scale

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0022, 0, 5, 9), "healthTenths", 4));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0022, 0, 5, 9), "amount", 1)); // unknown field
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0022, 0, 5, 9), "healthTenths", -1)); // unsigned WORD, no SHORT cast
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0022, 0, 5, 9), "healthTenths", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0022, 0, 5, 9), "scope", 65_536));
    }

    // --- 0x0023 removeEquipment (shared, script.c:1104): operand 0 is a direct 0-based
    //     PLAYERROLES role index (1108, `iPlayerRole = rgwOperand[0]` with no -1 - a fourth
    //     player-addressing shape beside the #100 three), operand 1 a part WORD: 0 = remove
    //     every part (1109-1123, each returned to inventory then effects removed at 1122),
    //     1..MAX_PLAYER_EQUIPMENTS = that part 1-based (1127-1133 indexes operand1 - 1).
    //     Operand 2 is unused by the reference case. ---
    [Fact]
    public void RemoveEquipment_edits_role_part_and_all_parts_and_preserves_operand2()
    {
        byte[] original = Words(0x0023, 2, 3, 7);
        var command = Decode(0, 0x0023, 2, 3, 7);
        Assert.Equal("removeEquipment", (string)command["op"]!);
        Assert.Equal(2, (int)command["playerRole"]!);
        Assert.Equal(3, (int)command["partWord"]!);

        Assert.Equal(Words(0x0023, 0, 3, 7), EditAndReencode(command, "playerRole", 0)); // role 0 is a legal direct index
        Assert.Equal(Words(0x0023, 2, 0, 7), EditAndReencode(Decode(0, 0x0023, 2, 3, 7), "partWord", 0)); // 0 = remove all parts
        Assert.Equal(Words(0x0023, 2, 6, 7), EditAndReencode(Decode(0, 0x0023, 2, 3, 7), "partWord", 6)); // 1-based part 6

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0023, 2, 3, 7), "partWord", 4));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0023, 2, 3, 7), "roleOperand", 0)); // unknown field (not the 0x0019 shape)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0023, 2, 3, 7), "itemId", 1)); // unknown field
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0023, 2, 3, 7), "playerRole", -1)); // direct index, unsigned WORD
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0023, 2, 3, 7), "partWord", 65_536));
    }

    // --- 0x0028 applyPoisonToEnemy (shared, script.c:1175): operand 0 is the whole-target flag
    //     (1179: 0 = g_Battle.rgEnemy[wEventObjectID], non-zero = every battle enemy skipping
    //     empty slots), operand 1 the object.json poison-view object ID: matched against
    //     rgPoisons[j].wPoisonID (1198/1233) and applied into a free slot by running the poison
    //     object's wEnemyScript trigger script (1211-1213/1246-1248); sorcery resistance gates
    //     the application (1196-1198). Operand 2 is unused. ---
    [Fact]
    public void ApplyPoisonToEnemy_edits_scope_and_poison_object_id_and_preserves_operand2()
    {
        byte[] original = Words(0x0028, 1, 556, 4);
        var command = Decode(0, 0x0028, 1, 556, 4);
        Assert.Equal("applyPoisonToEnemy", (string)command["op"]!);
        Assert.Equal(1, (int)command["scope"]!);
        Assert.Equal(556, (int)command["objectId"]!);

        Assert.Equal(Words(0x0028, 0, 556, 4), EditAndReencode(command, "scope", 0)); // one enemy at wEventObjectID
        Assert.Equal(Words(0x0028, 1, 560, 4), EditAndReencode(Decode(0, 0x0028, 1, 556, 4), "objectId", 560));

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0028, 1, 556, 4), "objectId", 557));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0028, 1, 556, 4), "poisonId", 556)); // unknown field (poison view is objectId)
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0028, 1, 556, 4), "objectId", -1));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0028, 1, 556, 4), "scope", 65_536));
    }

    // --- 0x0029 applyPoisonToPlayer (shared, script.c:1257): operand 0 is the whole-party flag
    //     (1261), operand 1 the poison object ID passed to PAL_AddPoisonForPlayer (1271/1282);
    //     the roll is RandomLong(1, 100) > PAL_GetPlayerPoisonResistance (1269/1280). ---
    [Fact]
    public void ApplyPoisonToPlayer_edits_scope_and_poison_object_id_and_preserves_operand2()
    {
        byte[] original = Words(0x0029, 0, 560, 4);
        var command = Decode(0, 0x0029, 0, 560, 4);
        Assert.Equal("applyPoisonToPlayer", (string)command["op"]!);
        Assert.Equal(0, (int)command["scope"]!);
        Assert.Equal(560, (int)command["objectId"]!);

        Assert.Equal(Words(0x0029, 1, 560, 4), EditAndReencode(command, "scope", 1)); // whole party
        Assert.Equal(Words(0x0029, 0, 555, 4), EditAndReencode(Decode(0, 0x0029, 0, 560, 4), "objectId", 555));

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0029, 0, 560, 4), "objectId", 556));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0029, 0, 560, 4), "poisonId", 560)); // unknown field
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0029, 0, 560, 4), "amount", 1)); // unknown field
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0029, 0, 560, 4), "objectId", 65_536));
    }

    // --- 0x002A curePoisonByObjectIdForEnemy (shared, script.c:1287): operand 0 the all-enemies
    //     flag (1291), operand 1 the poison object ID whose rgPoisons slot is zeroed for the
    //     targeted enemies (1300-1305 all / 1321-1324 one). ---
    [Fact]
    public void CurePoisonByObjectIdForEnemy_edits_scope_and_poison_object_id()
    {
        byte[] original = Words(0x002A, 1, 555, 0);
        var command = Decode(0, 0x002A, 1, 555, 0);
        Assert.Equal("curePoisonByObjectIdForEnemy", (string)command["op"]!);
        Assert.Equal(1, (int)command["scope"]!);
        Assert.Equal(555, (int)command["objectId"]!);

        Assert.Equal(Words(0x002A, 0, 555, 0), EditAndReencode(command, "scope", 0)); // one enemy
        Assert.Equal(Words(0x002A, 1, 558, 0), EditAndReencode(Decode(0, 0x002A, 1, 555, 0), "objectId", 558));

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x002A, 1, 555, 0), "objectId", 556));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x002A, 1, 555, 0), "poisonId", 555)); // unknown field
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x002A, 1, 555, 0), "objectId", 65_536));
    }

    // --- 0x002B curePoisonByObjectIdForPlayer (shared, script.c:1331): operand 0 the
    //     whole-party flag (1335), operand 1 the poison object ID passed to
    //     PAL_CurePoisonByKind for each targeted player (1340/1345). ---
    [Fact]
    public void CurePoisonByObjectIdForPlayer_edits_scope_and_poison_object_id()
    {
        byte[] original = Words(0x002B, 0, 557, 6);
        var command = Decode(0, 0x002B, 0, 557, 6);
        Assert.Equal("curePoisonByObjectIdForPlayer", (string)command["op"]!);
        Assert.Equal(0, (int)command["scope"]!);
        Assert.Equal(557, (int)command["objectId"]!);

        Assert.Equal(Words(0x002B, 1, 557, 6), EditAndReencode(command, "scope", 1)); // whole party
        Assert.Equal(Words(0x002B, 0, 559, 6), EditAndReencode(Decode(0, 0x002B, 0, 557, 6), "objectId", 559));

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x002B, 0, 557, 6), "objectId", 558));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x002B, 0, 557, 6), "poisonId", 557)); // unknown field
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x002B, 0, 557, 6), "objectId", -1));
    }

    // --- 0x002C curePoisonsByLevel (shared, script.c:1349): operand 0 the whole-party flag
    //     (1353), operand 1 an unsigned max level: PAL_CurePoisonByLevel removes every poison
    //     whose object's poison.wPoisonLevel <= the operand (1358/1363; global.c:1608 WORD
    //     comparison). ---
    [Fact]
    public void CurePoisonsByLevel_edits_scope_and_unsigned_max_level()
    {
        byte[] original = Words(0x002C, 1, 2, 0);
        var command = Decode(0, 0x002C, 1, 2, 0);
        Assert.Equal("curePoisonsByLevel", (string)command["op"]!);
        Assert.Equal(1, (int)command["scope"]!);
        Assert.Equal(2, (int)command["maxLevel"]!);

        Assert.Equal(Words(0x002C, 0, 2, 0), EditAndReencode(command, "scope", 0)); // one player
        Assert.Equal(Words(0x002C, 1, 3, 0), EditAndReencode(Decode(0, 0x002C, 1, 2, 0), "maxLevel", 3)); // revivePlayer cures with level 3

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x002C, 1, 2, 0), "maxLevel", 1));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x002C, 1, 2, 0), "level", 3)); // unknown field
        Assert.Equal(Words(0x002C, 1, 32_768, 0), EditAndReencode(Decode(0, 0x002C, 1, 2, 0), "maxLevel", 32_768)); // word, not signedWord: 32768 stays in range
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x002C, 1, 2, 0), "maxLevel", -1));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x002C, 1, 2, 0), "maxLevel", 65_536));
    }

    // --- 0x002D setStatusPlayer (shared, script.c:1367): the caller's player role
    //     (wEventObjectID) is the target with no operand; operand 0 is the rgPlayerStatus row
    //     index, operand 1 the unsigned round count (PAL_SetPlayerStatus's wNumRound,
    //     global.c:2173; a refused set only clears the script-success flag at 1373, no jump).
    //     Operand 2 is unused. ---
    [Fact]
    public void SetStatusPlayer_edits_status_row_rounds_and_preserves_operand2()
    {
        byte[] original = Words(0x002D, 3, 5, 8);
        var command = Decode(0, 0x002D, 3, 5, 8);
        Assert.Equal("setStatusPlayer", (string)command["op"]!);
        Assert.Equal(3, (int)command["status"]!);
        Assert.Equal(5, (int)command["rounds"]!);

        Assert.Equal(Words(0x002D, 6, 5, 8), EditAndReencode(command, "status", 6)); // another status row
        Assert.Equal(Words(0x002D, 3, 999, 8), EditAndReencode(Decode(0, 0x002D, 3, 5, 8), "rounds", 999)); // 999+ marks equipment effects (PAL_RemovePlayerStatus guard)
        Assert.Equal(Words(0x002D, 3, 65535, 8), EditAndReencode(Decode(0, 0x002D, 3, 5, 8), "rounds", 65_535)); // full unsigned WORD

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x002D, 3, 5, 8), "rounds", 4));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x002D, 3, 5, 8), "value", 1)); // unknown field (setStatusEnemy's raw store name)
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x002D, 3, 5, 8), "rounds", 65_536)); // word bound, checked cast
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x002D, 3, 5, 8), "status", -1));
    }

    // --- 0x002E setStatusEnemy (shared, script.c:1377): the battle enemy at wEventObjectID is
    //     the target; operand 0 the rgwStatus row index, operand 1 the unsigned WORD stored
    //     raw (1391) and consumed as a per-round counter (fight.c:1657-1659), operand 2 the
    //     jump target taken when the sorcery-resistance roll fails (1395, standard target-1 /
    //     return+1 idiom; WORD 0 references L0000 like the other conditional jumps). ---
    [Fact]
    public void SetStatusEnemy_edits_status_rounds_and_resisted_target()
    {
        byte[] original = Words(0x002E, 5, 3, 4);
        var command = Decode(0, 0x002E, 5, 3, 4);
        Assert.Equal("setStatusEnemy", (string)command["op"]!);
        Assert.Equal(5, (int)command["status"]!);
        Assert.Equal(3, (int)command["rounds"]!);
        Assert.Equal("L0004", (string)command["to"]!);

        Assert.Equal(Words(0x002E, 0, 3, 4), EditAndReencode(command, "status", 0)); // row 0 (confused) is a legal raw index
        Assert.Equal(Words(0x002E, 5, 0, 4), EditAndReencode(Decode(0, 0x002E, 5, 3, 4), "rounds", 0));
        Assert.Equal(Words(0x002E, 5, 3, 7), EditAndReencode(Decode(0, 0x002E, 5, 3, 4), "to", "L0007"));
        Assert.Equal(Words(0x002E, 5, 3, 0), EditAndReencode(Decode(0, 0x002E, 5, 3, 4), "to", "L0000")); // zero target rule

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x002E, 5, 3, 4), "rounds", 4));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x002E, 5, 3, 4), "target", "L0004")); // unknown field (plain jumps use "to")
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x002E, 5, 3, 4), "to", "L0100")); // dangling
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x002E, 5, 3, 4), "rounds", 65_536)); // word bound, checked cast
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x002E, 5, 3, 4), "status", 65_536));
    }

    // --- 0x002F clearPlayerStatus (shared, script.c:1399): the caller's player role
    //     (wEventObjectID) is the target; operand 0 the rgPlayerStatus row index passed to
    //     PAL_RemovePlayerStatus (1403), which refuses to clear equipment-granted values
    //     above 999 (global.c:2304); operands 1 and 2 are unused. ---
    [Fact]
    public void ClearPlayerStatus_edits_status_row_and_preserves_operands()
    {
        byte[] original = Words(0x002F, 2, 11, 13);
        var command = Decode(0, 0x002F, 2, 11, 13);
        Assert.Equal("clearPlayerStatus", (string)command["op"]!);
        Assert.Equal(2, (int)command["status"]!);

        Assert.Equal(Words(0x002F, 0, 11, 13), EditAndReencode(command, "status", 0)); // confused row

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x002F, 2, 11, 13), "status", 3));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x002F, 2, 11, 13), "state", 2)); // unknown field (event-object state's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x002F, 2, 11, 13), "rounds", 1)); // unknown field (setter's name)
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x002F, 2, 11, 13), "status", -1));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x002F, 2, 11, 13), "status", 65_536));
    }
}
