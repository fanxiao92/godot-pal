using Pal.Extract;
using Xunit;
using static Pal.Extract.Tests.OpcodeCounterexampleHarness;

namespace Pal.Extract.Tests;

// M5 opcode status-item batch 3 (#102, closing batch of the 29-opcode status-item domain):
// magic-list, level, cash and halving verbs. boostStat is a re-verified entry from #78/#49;
// addMagic, removeMagic, setMagicBaseDamageByMp, halveHp, killPlayer,
// setMagicBaseDamageByCash, increaseLevel and halveCash are new. All nine live in the shared
// PAL_InterpretInstruction switch (script.c:650) and advance sequentially (break, then the
// function tail return+1, script.c:3085); the trigger/auto switches have no case for any of
// them. Player addressing, per script.c:
//   - 0x0055/0x0056 operand 1 is the boostStat roleOperand shape (1820-1828/1836-1844:
//     0 = caller role wEventObjectID, non-zero = operand - 1 as a 1-based role index), passed
//     as PAL_AddMagic/PAL_RemoveMagic's wPlayerRole (global.c:2084/2139).
//   - 0x0057/0x005A/0x005F/0x008D target the caller's player role (wEventObjectID) with no
//     operand: rgwMP[wEventObjectID] (1855-1856), rgwHP[wEventObjectID] (1892/1947) and
//     PAL_PlayerLevelUp's wPlayerRole (2597, global.c:2347).
//   - 0x0088 touches no player at all, only the global cash and the magic table.
// Magic operands are object.json magic-view object IDs: PAL_AddMagic/PAL_RemoveMagic store and
// compare the raw operand inside rgwMagic (global.c:2084/2139), and 0x0057/0x0088 read
// rgObject[operand].magic.wMagicNumber to reach the magic-table entry whose wBaseDamage they
// overwrite (1853-1855/2555-2556). Signedness: only re-verified boostStat has a (SHORT) cast
// (1425, percent); the eight new cases have none, so every numeric field is an unsigned WORD.
// Counter-examples follow the #78/#96 skeleton: edit a semantic field -> re-encoded bytes
// differ from the source bytes and equal the hand-computed value; unknown fields and
// out-of-range values are rejected.
public sealed class OpcodeStatusItemBatch3Tests
{
    // --- 0x0030 boostStat (#78 named, re-verified; shared, script.c:1406): writes the extra
    //     equipment-effect register (rgEquipmentEffect[kBodyPartExtra] flattened as WORDs) with
    //     baseStat x percent / 100. Operand 0 is the PLAYERROLES row index (1423-1424 field x
    //     MAX_PLAYER_ROLES + role), operand 1 the only signed field of this batch (1425
    //     (SHORT) cast), operand 2 the roleOperand shape (1414-1421). ---
    [Fact]
    public void BoostStat_edits_field_signed_percent_and_role_operand()
    {
        byte[] original = Words(0x0030, 17, 50, 2);
        var command = Decode(0, 0x0030, 17, 50, 2);
        Assert.Equal("boostStat", (string)command["op"]!);
        Assert.Equal(17, (int)command["field"]!);
        Assert.Equal(50, (int)command["percent"]!);
        Assert.Equal(2, (int)command["roleOperand"]!);

        Assert.Equal(Words(0x0030, 18, 50, 2), EditAndReencode(command, "field", 18)); // magicStrength row
        Assert.Equal(Words(0x0030, 17, unchecked((int)0xFFE7), 2), EditAndReencode(Decode(0, 0x0030, 17, 50, 2), "percent", -25)); // signed WORD
        Assert.Equal(Words(0x0030, 17, 50, 0), EditAndReencode(Decode(0, 0x0030, 17, 50, 2), "roleOperand", 0)); // 0 = caller role
        Assert.Equal(Words(0x0030, 17, 0, 2), EditAndReencode(Decode(0, 0x0030, 17, 50, 2), "percent", 0)); // zero percent is a legal WORD

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0030, 17, 50, 2), "percent", 49));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0030, 17, 50, 2), "value", 1)); // unknown field (setExtraAttr's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0030, 17, 50, 2), "playerRole", 1)); // unknown field (0x0023's direct-index name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0030, 17, 50, 2), "percent", 32_768)); // signed WORD bound
        Assert.Equal(Words(0x0030, 17, unchecked((int)0x8000), 2), EditAndReencode(Decode(0, 0x0030, 17, 50, 2), "percent", -32_768)); // full negative range
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0030, 17, 50, 2), "roleOperand", -1)); // unsigned WORD
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0030, 17, 50, 2), "roleOperand", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0030, 17, 50, 2), "field", 65_536));
    }

    // --- 0x0055 addMagic (shared, script.c:1816): operand 0 is the object.json magic-view
    //     object ID stored into the player's rgwMagic slot (PAL_AddMagic, global.c:2084:
    //     compares by equality against the raw operand, first free slot wins, duplicates
    //     refused); operand 1 the roleOperand shape (1820-1828). Operand 2 is unused by the
    //     reference case. ---
    [Fact]
    public void AddMagic_edits_magic_object_id_and_role_operand_and_preserves_operand2()
    {
        byte[] original = Words(0x0055, 300, 0, 7);
        var command = Decode(0, 0x0055, 300, 0, 7);
        Assert.Equal("addMagic", (string)command["op"]!);
        Assert.Equal(300, (int)command["magicId"]!);
        Assert.Equal(0, (int)command["roleOperand"]!);

        Assert.Equal(Words(0x0055, 310, 0, 7), EditAndReencode(command, "magicId", 310)); // level-up-magic-only object 310 stays a raw object ID
        Assert.Equal(Words(0x0055, 300, 2, 7), EditAndReencode(Decode(0, 0x0055, 300, 0, 7), "roleOperand", 2)); // 1-based role index 1
        Assert.Equal(Words(0x0055, 300, 1, 7), EditAndReencode(Decode(0, 0x0055, 300, 0, 7), "roleOperand", 1));

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0055, 300, 0, 7), "magicId", 301));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0055, 300, 0, 7), "objectId", 300)); // unknown field (poison view's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0055, 300, 0, 7), "playerRole", 0)); // unknown field (not the 0x0023 shape)
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0055, 300, 0, 7), "magicId", -1)); // unsigned WORD
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0055, 300, 0, 7), "magicId", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0055, 300, 0, 7), "roleOperand", 65_536));
    }

    // --- 0x0056 removeMagic (shared, script.c:1832): same operand shape as 0x0055; the first
    //     rgwMagic slot equal to the operand is zeroed (PAL_RemoveMagic, global.c:2139;
    //     missing magics silently do nothing). Operand 2 is unused by the reference case. ---
    [Fact]
    public void RemoveMagic_edits_magic_object_id_and_role_operand_and_preserves_operand2()
    {
        byte[] original = Words(0x0056, 311, 2, 0);
        var command = Decode(0, 0x0056, 311, 2, 0);
        Assert.Equal("removeMagic", (string)command["op"]!);
        Assert.Equal(311, (int)command["magicId"]!);
        Assert.Equal(2, (int)command["roleOperand"]!);

        Assert.Equal(Words(0x0056, 300, 2, 0), EditAndReencode(command, "magicId", 300));
        Assert.Equal(Words(0x0056, 311, 0, 0), EditAndReencode(Decode(0, 0x0056, 311, 2, 0), "roleOperand", 0)); // 0 = caller role
        Assert.Equal(Words(0x0056, 311, 3, 0), EditAndReencode(Decode(0, 0x0056, 311, 2, 0), "roleOperand", 3)); // 1-based role index 2

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0056, 311, 2, 0), "magicId", 312));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0056, 311, 2, 0), "amount", 1)); // unknown field
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0056, 311, 2, 0), "roleId", 1)); // unknown field
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0056, 311, 2, 0), "magicId", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0056, 311, 2, 0), "roleOperand", 65_536));
    }

    // --- 0x0057 setMagicBaseDamageByMp (shared, script.c:1848): operand 0 is the magic-view
    //     object ID whose magic.wMagicNumber row in the magic table gets wBaseDamage =
    //     rgwMP[wEventObjectID] x operand1 (1852-1855), then the caller-role player's MP is
    //     zeroed (1856). Operand 1's WORD 0 normalizes to 8 at runtime only (1852), so 0 still
    //     encodes as 0. The targeted player is wEventObjectID with no operand; operand 2 is
    //     unused by the reference case. ---
    [Fact]
    public void SetMagicBaseDamageByMp_edits_magic_object_id_and_mp_multiplier_and_preserves_operand2()
    {
        byte[] original = Words(0x0057, 305, 0, 5);
        var command = Decode(0, 0x0057, 305, 0, 5);
        Assert.Equal("setMagicBaseDamageByMp", (string)command["op"]!);
        Assert.Equal(305, (int)command["magicId"]!);
        Assert.Equal(0, (int)command["mpMultiplier"]!);

        Assert.Equal(Words(0x0057, 321, 0, 5), EditAndReencode(command, "magicId", 321)); // level-up-magic-only object 321
        Assert.Equal(Words(0x0057, 305, 8, 5), EditAndReencode(Decode(0, 0x0057, 305, 0, 5), "mpMultiplier", 8)); // explicit 8 = runtime meaning of 0
        Assert.Equal(Words(0x0057, 305, 1, 5), EditAndReencode(Decode(0, 0x0057, 305, 0, 5), "mpMultiplier", 1));
        Assert.Equal(Words(0x0057, 305, 0, 5), EditAndReencode(Decode(0, 0x0057, 305, 8, 5), "mpMultiplier", 0)); // 0 round-trips as 0

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0057, 305, 0, 5), "mpMultiplier", 16));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0057, 305, 0, 5), "multiplier", 8)); // unknown field (full name is mpMultiplier)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0057, 305, 0, 5), "percent", 8)); // unknown field (boostStat's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0057, 305, 0, 5), "roleOperand", 2)); // unknown field: target is wEventObjectID, no operand
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0057, 305, 0, 5), "mpMultiplier", -1)); // unsigned WORD
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0057, 305, 0, 5), "mpMultiplier", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0057, 305, 0, 5), "magicId", 65_536));
    }

    // --- 0x005A halveHp (shared, script.c:1887): rgwHP[wEventObjectID] /= 2 (1892) - the
    //     reference comment states wEventObjectID indicates the player role; unsigned WORD
    //     division. No operands are mapped. ---
    [Fact]
    public void HalveHp_round_trips_preserved_operands_and_rejects_unknown_fields()
    {
        byte[] original = Words(0x005A, 3, 4, 5);
        var command = Decode(0, 0x005A, 3, 4, 5);
        Assert.Equal("halveHp", (string)command["op"]!);

        Assert.Equal(original, Reencode(command)); // no mapped fields: byte-exact

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x005A, 3, 4, 5), "to", "L0001"));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x005A, 3, 4, 5), "amount", 1));
    }

    // --- 0x005F killPlayer (shared, script.c:1942): rgwHP[wEventObjectID] = 0 (1947) - the
    //     reference comment states wEventObjectID indicates the player role. No operands are
    //     mapped. ---
    [Fact]
    public void KillPlayer_round_trips_preserved_operands_and_rejects_unknown_fields()
    {
        byte[] original = Words(0x005F, 1, 2, 3);
        var command = Decode(0, 0x005F, 1, 2, 3);
        Assert.Equal("killPlayer", (string)command["op"]!);

        Assert.Equal(original, Reencode(command)); // no mapped fields: byte-exact

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x005F, 1, 2, 3), "scope", 1));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x005F, 1, 2, 3), "status", 1));
    }

    // --- 0x0088 setMagicBaseDamageByCash (shared, script.c:2549): operand 0 is the magic-view
    //     object ID whose magic.wMagicNumber row gets wBaseDamage = min(cash, 5000) x 2 / 5
    //     (2553-2556); the same min amount is deducted from the DWORD cash (2554). Unlike
    //     0x0057 there is no player-role and no multiplier operand; operands 1 and 2 are
    //     unused by the reference case. ---
    [Fact]
    public void SetMagicBaseDamageByCash_edits_magic_object_id_and_preserves_operands()
    {
        byte[] original = Words(0x0088, 295, 6, 0);
        var command = Decode(0, 0x0088, 295, 6, 0);
        Assert.Equal("setMagicBaseDamageByCash", (string)command["op"]!);
        Assert.Equal(295, (int)command["magicId"]!);

        Assert.Equal(Words(0x0088, 300, 6, 0), EditAndReencode(command, "magicId", 300));
        Assert.Equal(Words(0x0088, 295, 6, 0), EditAndReencode(Decode(0, 0x0088, 300, 6, 0), "magicId", 295));

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0088, 295, 6, 0), "magicId", 296));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0088, 295, 6, 0), "mpMultiplier", 8)); // unknown field (0x0057's operand; byCash has none)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0088, 295, 6, 0), "amount", 1)); // unknown field
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0088, 295, 6, 0), "roleOperand", 2)); // unknown field: no player operand
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0088, 295, 6, 0), "magicId", -1)); // unsigned WORD
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0088, 295, 6, 0), "magicId", 65_536));
    }

    // --- 0x008D increaseLevel (shared, script.c:2593): PAL_PlayerLevelUp(wEventObjectID,
    //     operand0) - the caller-role player gains operand0 levels (global.c:2347: WORD
    //     wNumLevel, per-level random stat gains capped at MAX_LEVELS/999, experience reset).
    //     Operands 1 and 2 are unused by the reference case. ---
    [Fact]
    public void IncreaseLevel_edits_levels_and_preserves_operands()
    {
        byte[] original = Words(0x008D, 1, 2, 3);
        var command = Decode(0, 0x008D, 1, 2, 3);
        Assert.Equal("increaseLevel", (string)command["op"]!);
        Assert.Equal(1, (int)command["levels"]!);

        Assert.Equal(Words(0x008D, 5, 2, 3), EditAndReencode(command, "levels", 5));
        Assert.Equal(Words(0x008D, 0, 2, 3), EditAndReencode(Decode(0, 0x008D, 1, 2, 3), "levels", 0)); // zero levels: loop runs zero times, still a legal WORD
        Assert.Equal(Words(0x008D, 65_535, 2, 3), EditAndReencode(Decode(0, 0x008D, 1, 2, 3), "levels", 65_535)); // full unsigned WORD

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x008D, 1, 2, 3), "levels", 2));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x008D, 1, 2, 3), "maxLevel", 1)); // unknown field (curePoisonsByLevel's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x008D, 1, 2, 3), "count", 1)); // unknown field
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x008D, 1, 2, 3), "roleOperand", 2)); // unknown field: target is wEventObjectID, no operand
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x008D, 1, 2, 3), "levels", -1)); // unsigned WORD
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x008D, 1, 2, 3), "levels", 65_536));
    }

    // --- 0x008F halveCash (shared, script.c:2600): dwCash /= 2 (2604) - the DWORD cash halves.
    //     No operands are mapped. ---
    [Fact]
    public void HalveCash_round_trips_preserved_operands_and_rejects_unknown_fields()
    {
        byte[] original = Words(0x008F, 4, 5, 6);
        var command = Decode(0, 0x008F, 4, 5, 6);
        Assert.Equal("halveCash", (string)command["op"]!);

        Assert.Equal(original, Reencode(command)); // no mapped fields: byte-exact

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x008F, 4, 5, 6), "amount", 1));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x008F, 4, 5, 6), "magicId", 1));
    }
}
