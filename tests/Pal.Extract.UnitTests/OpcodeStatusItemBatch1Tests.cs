using System.Buffers.Binary;
using System.Text.Json.Nodes;
using Pal.Extract;
using Xunit;

namespace Pal.Extract.Tests;

// M5 opcode status-item batch 1 (#100): setExtraAttr, equipItem, incPlayerAttr, setPlayerStat,
// changeCash, addItem and removeItem are new; changeHp / changeMp / changeHpMp are re-verified
// entries from #78. All ten live in the shared PAL_InterpretInstruction switch (script.c:650)
// and advance sequentially (function tail return+1, script.c:3085). The player-selection
// operands are this batch's verification focus, per script.c:
//   - 0x0017/0x0018 write through wEventObjectID directly as the 0-based player role
//     (script.c:763/778); their operand 0 is 0x0B + body part (script.c:759/772 subtracts 0x0B,
//     rgEquipmentEffect has MAX_PLAYER_EQUIPMENTS+1 entries so 0x11 = extra is legal).
//   - 0x0019/0x001A operand 2 is the boostStat roleOperand shape (script.c:822-826/855-859):
//     0 = caller (wEventObjectID as player role), non-zero = 1-based player-role index.
//   - 0x001B/0x001C/0x001D operand 0 is a whole-party flag (`if (pScript->rgwOperand[0])`),
//     not a bit mask nor a role id: 0 = one player (wEventObjectID as player role),
//     non-zero = every party member (script.c:874/903/932).
// Counter-examples follow the #78/#96 skeleton: edit a semantic field -> re-encoded bytes
// differ from the source bytes and equal the hand-computed value; unknown fields, dangling
// references and out-of-range values are rejected.
public sealed class OpcodeStatusItemBatch1Tests
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

    // --- 0x0017 setExtraAttr (shared, script.c:752): operand 0 is 0x0B + body part selecting
    //     rgEquipmentEffect[operand0 - 0x0B] (759), operand 1 the PLAYERROLES WORD-row index
    //     (x6 stride, 763), operand 2 a signed value assigned for the caller's player role
    //     (wEventObjectID, 763). Real data: 0x0B..0x11 x rows 17..27 with -20..200 values. ---
    [Fact]
    public void SetExtraAttr_edits_equip_part_field_row_and_signed_value()
    {
        byte[] original = Words(0x0017, 0x0B, 19, unchecked((int)0xFFF6)); // head part, defense row, -10
        var command = Decode(0, 0x0017, 0x0B, 19, unchecked((int)0xFFF6));
        Assert.Equal("setExtraAttr", (string)command["op"]!);
        Assert.Equal(0x0B, (int)command["equipPartWord"]!);
        Assert.Equal(19, (int)command["field"]!);
        Assert.Equal(-10, (int)command["value"]!);

        Assert.Equal(Words(0x0017, 0x11, 19, unchecked((int)0xFFF6)), EditAndReencode(command, "equipPartWord", 0x11)); // 0x11 = extra part (7th effect entry)
        Assert.Equal(Words(0x0017, 0x0B, 21, unchecked((int)0xFFF6)), EditAndReencode(Decode(0, 0x0017, 0x0B, 19, unchecked((int)0xFFF6)), "field", 21)); // fleeRate row
        Assert.Equal(Words(0x0017, 0x0B, 19, 200), EditAndReencode(Decode(0, 0x0017, 0x0B, 19, unchecked((int)0xFFF6)), "value", 200));
        Assert.Equal(Words(0x0017, 0x0B, 19, unchecked((int)0xFFEC)), EditAndReencode(Decode(0, 0x0017, 0x0B, 19, unchecked((int)0xFFF6)), "value", -20)); // two's complement

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0017, 0x0B, 19, unchecked((int)0xFFF6)), "value", 5));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0017, 0x0B, 19, unchecked((int)0xFFF6)), "statIndex", 19)); // unknown field
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0017, 0x0B, 19, unchecked((int)0xFFF6)), "value", 32_768));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0017, 0x0B, 19, unchecked((int)0xFFF6)), "value", -32_769));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0017, 0x0B, 19, unchecked((int)0xFFF6)), "equipPartWord", 65_536));
    }

    // --- 0x0018 equipItem (shared, script.c:768): operand 0 is 0x0B + body part (772) selecting
    //     rgwEquipment[part][role] and g_iCurEquipPart (773) for subsequent setPlayerStat writes,
    //     operand 1 the object-table item id; the equipped role is wEventObjectID directly (778).
    //     Operand 2 is unused by the reference case and keeps its source WORD. ---
    [Fact]
    public void EquipItem_edits_equip_part_and_item_id_and_preserves_operand2()
    {
        byte[] original = Words(0x0018, 0x0D, 208, 7);
        var command = Decode(0, 0x0018, 0x0D, 208, 7);
        Assert.Equal("equipItem", (string)command["op"]!);
        Assert.Equal(0x0D, (int)command["equipPartWord"]!);
        Assert.Equal(208, (int)command["itemId"]!);

        Assert.Equal(Words(0x0018, 0x0D, 209, 7), EditAndReencode(command, "itemId", 209));
        Assert.Equal(Words(0x0018, 0x0E, 208, 7), EditAndReencode(Decode(0, 0x0018, 0x0D, 208, 7), "equipPartWord", 0x0E)); // hand part

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0018, 0x0D, 208, 7), "itemId", 210));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0018, 0x0D, 208, 7), "count", 1)); // unknown field
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0018, 0x0D, 208, 7), "itemId", -1));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0018, 0x0D, 208, 7), "itemId", 65_536));
    }

    // --- 0x0019 incPlayerAttr (shared, script.c:813): operand 0 is the PLAYERROLES WORD-row
    //     index (829), operand 1 a signed delta added (829), operand 2 the roleOperand shape
    //     (822-826): 0 = caller's role, non-zero = 1-based player-role index. ---
    [Fact]
    public void IncPlayerAttr_edits_field_row_signed_delta_and_role_operand()
    {
        byte[] original = Words(0x0019, 7, 100, 2); // maxHP row, +100, role operand 2 = player role 1
        var command = Decode(0, 0x0019, 7, 100, 2);
        Assert.Equal("incPlayerAttr", (string)command["op"]!);
        Assert.Equal(7, (int)command["field"]!);
        Assert.Equal(100, (int)command["delta"]!);
        Assert.Equal(2, (int)command["roleOperand"]!);

        Assert.Equal(Words(0x0019, 6, 100, 2), EditAndReencode(command, "field", 6)); // level row
        Assert.Equal(Words(0x0019, 7, unchecked((int)0xFFF6), 2), EditAndReencode(Decode(0, 0x0019, 7, 100, 2), "delta", -10));
        Assert.Equal(Words(0x0019, 7, 100, 0), EditAndReencode(Decode(0, 0x0019, 7, 100, 2), "roleOperand", 0)); // caller's role

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0019, 7, 100, 2), "delta", 101));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0019, 7, 100, 2), "amount", 1)); // unknown field (changeHp's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0019, 7, 100, 2), "delta", 32_768));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0019, 7, 100, 2), "roleOperand", -1));
    }

    // --- 0x001A setPlayerStat (shared, script.c:834): operand 0/1/2 share the 0x0019 layout but
    //     assign instead of add (862); during item equip scripts the write lands in
    //     rgEquipmentEffect[g_iCurEquipPart] (844-846) instead of PlayerRoles. ---
    [Fact]
    public void SetPlayerStat_edits_field_row_value_and_role_operand()
    {
        byte[] original = Words(0x001A, 64, 3, 0); // walkFrames row, 3, caller's role
        var command = Decode(0, 0x001A, 64, 3, 0);
        Assert.Equal("setPlayerStat", (string)command["op"]!);
        Assert.Equal(64, (int)command["field"]!);
        Assert.Equal(3, (int)command["value"]!);
        Assert.Equal(0, (int)command["roleOperand"]!);

        Assert.Equal(Words(0x001A, 65, 3, 0), EditAndReencode(command, "field", 65)); // cooperativeMagic row
        Assert.Equal(Words(0x001A, 64, 383, 0), EditAndReencode(Decode(0, 0x001A, 64, 3, 0), "value", 383));
        Assert.Equal(Words(0x001A, 64, 3, 2), EditAndReencode(Decode(0, 0x001A, 64, 3, 0), "roleOperand", 2));

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x001A, 64, 3, 0), "value", 4));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x001A, 64, 3, 0), "delta", 1)); // unknown field (incPlayerAttr's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x001A, 64, 3, 0), "value", -32_769));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x001A, 64, 3, 0), "field", 65_536));
    }

    // --- 0x001B changeHp (#78 named, re-verified; shared, script.c:867): operand 0 is a
    //     whole-party flag, 0 = one player with wEventObjectID as the player role (889), non-zero
    //     = every party member (874-880); operand 1 a signed HP delta. Real data uses only 0/1. ---
    [Fact]
    public void ChangeHp_edits_whole_party_flag_and_signed_amount()
    {
        byte[] original = Words(0x001B, 0, 300, 9);
        var command = Decode(0, 0x001B, 0, 300, 9);
        Assert.Equal("changeHp", (string)command["op"]!);
        Assert.Equal(0, (int)command["scope"]!);
        Assert.Equal(300, (int)command["amount"]!);

        Assert.Equal(Words(0x001B, 1, 300, 9), EditAndReencode(command, "scope", 1)); // whole party
        Assert.Equal(Words(0x001B, 0, unchecked((int)0xFED4), 9), EditAndReencode(Decode(0, 0x001B, 0, 300, 9), "amount", -300)); // operand 2 preserved

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x001B, 0, 300, 9), "amount", 299));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x001B, 0, 300, 9), "roleId", 1)); // scope is a flag, not a role id
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x001B, 0, 300, 9), "amount", 32_768));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x001B, 0, 300, 9), "scope", 65_536));
    }

    // --- 0x001C changeMp (#78 named, re-verified; shared, script.c:896): same shape as 0x001B
    //     with the signed delta applied to MP (908/916). ---
    [Fact]
    public void ChangeMp_edits_whole_party_flag_and_signed_amount()
    {
        byte[] original = Words(0x001C, 1, 25, 0);
        var command = Decode(0, 0x001C, 1, 25, 0);
        Assert.Equal("changeMp", (string)command["op"]!);
        Assert.Equal(1, (int)command["scope"]!);
        Assert.Equal(25, (int)command["amount"]!);

        Assert.Equal(Words(0x001C, 0, 25, 0), EditAndReencode(command, "scope", 0)); // one player
        Assert.Equal(Words(0x001C, 1, unchecked((int)0xFFF6), 0), EditAndReencode(Decode(0, 0x001C, 1, 25, 0), "amount", -10));

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x001C, 1, 25, 0), "scope", 2)); // non-zero stays a whole-party flag

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x001C, 1, 25, 0), "mask", 1));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x001C, 1, 25, 0), "amount", -32_769));
    }

    // --- 0x001D changeHpMp (#78 named, re-verified; shared, script.c:923): the signed operand 1
    //     is applied to both HP and MP (935-937/944-945); operand 0 keeps the flag shape. ---
    [Fact]
    public void ChangeHpMp_edits_flag_and_the_shared_signed_amount()
    {
        byte[] original = Words(0x001D, 1, 25, 0);
        var command = Decode(0, 0x001D, 1, 25, 0);
        Assert.Equal("changeHpMp", (string)command["op"]!);
        Assert.Equal(1, (int)command["scope"]!);
        Assert.Equal(25, (int)command["amount"]!);

        Assert.Equal(Words(0x001D, 1, 30, 0), EditAndReencode(command, "amount", 30));
        Assert.Equal(Words(0x001D, 0, 25, 0), EditAndReencode(Decode(0, 0x001D, 1, 25, 0), "scope", 0));

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x001D, 1, 25, 0), "amount", 24));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x001D, 1, 25, 0), "mpAmount", 1)); // one amount serves both stats
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x001D, 1, 25, 0), "amount", 32_768));
    }

    // --- 0x001E changeCash (shared, script.c:952): operand 0 is a signed cash delta (957/966);
    //     when the amount is negative and cash is insufficient the script jumps to operand 1
    //     (962, the standard target-1/return+1 idiom), else the cash changes and execution
    //     continues. Target WORD 0 references L0000 like the other conditional jumps; real data
    //     has 42 zero targets (including silent removals) and 17 in-range targets. ---
    [Fact]
    public void ChangeCash_edits_signed_amount_and_fail_target_and_preserves_operand2()
    {
        byte[] original = Words(0x001E, 65511, 2, 9); // -25 with fail target L0002
        var command = Decode(0, 0x001E, 65511, 2, 9);
        Assert.Equal("changeCash", (string)command["op"]!);
        Assert.Equal(-25, (int)command["amount"]!);
        Assert.Equal("L0002", (string)command["to"]!);

        Assert.Equal(Words(0x001E, 500, 2, 9), EditAndReencode(command, "amount", 500));
        Assert.Equal(Words(0x001E, 65511, 5, 9), EditAndReencode(Decode(0, 0x001E, 65511, 2, 9), "to", "L0005"));
        Assert.Equal(Words(0x001E, 65511, 0, 9), EditAndReencode(Decode(0, 0x001E, 65511, 2, 9), "to", "L0000")); // zero target rule

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x001E, 65511, 2, 9), "amount", 25));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x001E, 65511, 2, 9), "cash", 25)); // unknown field
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x001E, 65511, 2, 9), "to", "L0100")); // dangling
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x001E, 65511, 2, 9), "amount", 32_768));
    }

    // --- 0x001F addItem (shared, script.c:970): PAL_AddItemToInventory(operand0, (SHORT)
    //     operand1) (974) - object-table item id and a signed count (negative removes);
    //     operand 2 is unused and preserved. ---
    [Fact]
    public void AddItem_edits_item_id_and_signed_count_and_preserves_operand2()
    {
        byte[] original = Words(0x001F, 138, 9, 5);
        var command = Decode(0, 0x001F, 138, 9, 5);
        Assert.Equal("addItem", (string)command["op"]!);
        Assert.Equal(138, (int)command["itemId"]!);
        Assert.Equal(9, (int)command["count"]!);

        Assert.Equal(Words(0x001F, 139, 9, 5), EditAndReencode(command, "itemId", 139));
        Assert.Equal(Words(0x001F, 138, 0, 5), EditAndReencode(Decode(0, 0x001F, 138, 9, 5), "count", 0));
        Assert.Equal(Words(0x001F, 138, unchecked((int)0xFFFF), 5), EditAndReencode(Decode(0, 0x001F, 138, 9, 5), "count", -1)); // removal

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x001F, 138, 9, 5), "count", 8));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x001F, 138, 9, 5), "amount", 1)); // unknown field
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x001F, 138, 9, 5), "count", 32_768));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x001F, 138, 9, 5), "itemId", 65_536));
    }

    // --- 0x0020 removeItem (shared, script.c:977): operand 0 the object-table item id, operand 1
    //     an unsigned removal count (981, WORD with no SHORT cast; 0 is normalized to 1 by the
    //     runtime at 983-984 only, the codec keeps 0 like itemNotEquippedJump), operand 2 the
    //     shortage fail target: WORD 0 means "remove anyway even when short" (986) and encodes
    //     null; non-zero words jump when the inventory plus equipped items cannot cover the
    //     count (1023). A reference to address 0 must use null, not a label. ---
    [Fact]
    public void RemoveItem_edits_count_and_nullable_shortage_target()
    {
        byte[] original = Words(0x0020, 148, 1, 4);
        var command = Decode(0, 0x0020, 148, 1, 4);
        Assert.Equal("removeItem", (string)command["op"]!);
        Assert.Equal(148, (int)command["itemId"]!);
        Assert.Equal(1, (int)command["count"]!);
        Assert.Equal("L0004", (string)command["shortageTarget"]!);

        Assert.Equal(Words(0x0020, 148, 0, 4), EditAndReencode(command, "count", 0)); // encodes 0, runtime reads it as 1
        Assert.Equal(Words(0x0020, 148, 36, 4), EditAndReencode(Decode(0, 0x0020, 148, 1, 4), "count", 36));
        Assert.Equal(Words(0x0020, 148, 1, 0), EditAndReencode(Decode(0, 0x0020, 148, 1, 4), "shortageTarget", null)); // null <-> WORD 0
        Assert.Equal(Words(0x0020, 148, 1, 7), EditAndReencode(Decode(0, 0x0020, 148, 1, 4), "shortageTarget", "L0007"));

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0020, 148, 1, 4), "count", 2));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0020, 148, 1, 4), "to", "L0002")); // unknown field (plain jumps use "to")
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0020, 148, 1, 4), "shortageTarget", "L0000")); // address zero must use null
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0020, 148, 1, 4), "shortageTarget", "L0100")); // dangling
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0020, 148, 1, 4), "count", -1)); // unsigned WORD, no SHORT cast
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0020, 148, 1, 4), "itemId", 65_536));
    }
}
