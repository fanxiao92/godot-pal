using Pal.Extract;
using Xunit;
using static Pal.Extract.Tests.OpcodeCounterexampleHarness;

namespace Pal.Extract.Tests;

// M5 opcode battle batch 2 (#104) - the battle-domain closing batch plus the two menu verbs:
// showBuyMenu/showSellMenu (0x0026/0x0027), enemyUseMagic (0x0067), enemyEscape (0x0069),
// stealFromEnemy (0x006A), blowAwayEnemies (0x006B), setBattleResult (0x0089),
// enableAutoBattle (0x008A), showMagicCastAnimation (0x0092), enemyDivide (0x009C),
// enemySummon (0x009E) and enemyTransform (0x009F). Topology per script.c: all twelve live in the
// shared PAL_InterpretInstruction switch (script.c:650) and only 0x009C/0x009E jump
// (wScriptEntry = failTarget - 1 plus the function-tail return+1 at script.c:3085); everything else
// breaks into the sequential tail. Addressing per the reference cases:
//   - 0x0067/0x006A/0x0069/0x009E/0x009F target the caller enemy slot rgEnemy[wEventObjectID].
//   - 0x0092 operand 0 is a 1-based battle player slot (rgPlayer[operand0 - 1]); WORD 0 = no single
//     player (0 = none, unlike roleOperand's 0 = caller).
//   - 0x0026 operand 0 is the store table row (lprgStore[operand0].rgwItems, uigame.c:1629).
//   - 0x009E/0x009F operand 0 is an object.json enemy-view object ID (rgObject[w].enemy.wEnemyID,
//     script.c:2919/2968); 0x0067 operand 0 is the magic-view object consumed at fight.c:4671.
// Signedness: blowAwayEnemies.strength (script.c:2053) and enemySummon.count (script.c:2885) read
// (SHORT) - real data uses -3/-2 and 0 (normalized at runtime only); every other numeric field is an
// unsigned WORD (setBattleResult's 0xFFFF fleed value only fits unsigned). 0x009C.operand1 and
// 0x009E.operand2 are nullable references (WORD 0 = no fail jump), so they reuse the battleTarget
// binding: null encodes WORD 0 and a reference to address 0 is rejected.
// Counter-examples follow the #78/#96 skeleton: edit a semantic field -> re-encoded bytes differ
// from the source bytes and equal the hand-computed value; unknown fields and out-of-range values
// are rejected.
public sealed class OpcodeBattleBatch2Tests
{
    // --- 0x0026 showBuyMenu (shared, script.c:1157-1164): PAL_MakeScene + PAL_BuyMenu(operand0) -
    //     operand 0 is the store table row whose rgwItems[0..8] become the purchasable list
    //     (lprgStore[wStoreNum], uigame.c:1629/1648); no item is bought until the menu confirms.
    //     Operands 1/2 are unused by the reference case. ---
    [Fact]
    public void ShowBuyMenu_edits_store_row_and_preserves_operands()
    {
        byte[] original = Words(0x0026, 12, 3, 4);
        var command = Decode(0, 0x0026, 12, 3, 4);
        Assert.Equal("showBuyMenu", (string)command["op"]!);
        Assert.Equal(12, (int)command["storeId"]!);

        Assert.Equal(Words(0x0026, 1, 3, 4), EditAndReencode(command, "storeId", 1)); // store.json row 1
        Assert.Equal(Words(0x0026, 0, 3, 4), EditAndReencode(Decode(0, 0x0026, 12, 3, 4), "storeId", 0)); // zero is a legal WORD
        Assert.Equal(Words(0x0026, 20, 3, 4), EditAndReencode(Decode(0, 0x0026, 12, 3, 4), "storeId", 20)); // real-data top row

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0026, 12, 3, 4), "storeId", 13));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0026, 12, 3, 4), "storeNum", 13)); // reference parameter name
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0026, 12, 3, 4), "itemId", 61)); // unknown field (items come from the store row)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0026, 12, 3, 4), "enemyTeamId", 13)); // unknown field (table-row name of another table)
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0026, 12, 3, 4), "storeId", -1));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0026, 12, 3, 4), "storeId", 65_536));
    }

    // --- 0x0027 showSellMenu (shared, script.c:1166-1173): PAL_MakeScene + PAL_SellMenu() - the
    //     sold list is the player's own inventory (PAL_ItemSelectMenu, kItemFlagSellable), so no
    //     operand selects a table row. All three operands are preserved as source WORDs. ---
    [Fact]
    public void ShowSellMenu_round_trips_preserved_operands_and_rejects_unknown_fields()
    {
        byte[] original = Words(0x0027, 3, 4, 5);
        var command = Decode(0, 0x0027, 3, 4, 5);
        Assert.Equal("showSellMenu", (string)command["op"]!);

        Assert.Equal(original, Reencode(command)); // no mapped fields: byte-exact

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0027, 3, 4, 5), "storeId", 1)); // buy-side only
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0027, 3, 4, 5), "itemId", 61));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0027, 3, 4, 5), "scope", 1));
    }

    // --- 0x0067 enemyUseMagic (shared, script.c:2016-2023): the #103-reserved counterpart of
    //     simulateMagic - instead of calling PAL_BattleSimulateMagic it writes the caller enemy
    //     slot's e.wMagic (operand 0, the magic-view object consumed at fight.c:4671, 0 = no magic
    //     and 0xFFFF = dual magic) and e.wMagicRate (operand 1, WORD 0 normalized to 10 at runtime
    //     only, encoding stays 0). No (SHORT) cast; operand 2 is unused. ---
    [Fact]
    public void EnemyUseMagic_edits_magic_id_and_rate_and_preserves_operand2()
    {
        byte[] original = Words(0x0067, 318, 0, 0);
        var command = Decode(0, 0x0067, 318, 0, 0);
        Assert.Equal("enemyUseMagic", (string)command["op"]!);
        Assert.Equal(318, (int)command["magicId"]!);
        Assert.Equal(0, (int)command["magicRate"]!);

        Assert.Equal(Words(0x0067, 375, 0, 0), EditAndReencode(command, "magicId", 375));
        Assert.Equal(Words(0x0067, unchecked((int)0xFFFF), 0, 0), EditAndReencode(Decode(0, 0x0067, 318, 0, 0), "magicId", 65_535)); // dual-magic sentinel
        Assert.Equal(Words(0x0067, 0, 0, 0), EditAndReencode(Decode(0, 0x0067, 318, 0, 0), "magicId", 0)); // no-magic sentinel
        Assert.Equal(Words(0x0067, 318, 5, 0), EditAndReencode(Decode(0, 0x0067, 318, 0, 0), "magicRate", 5)); // real-data explicit rate
        Assert.Equal(Words(0x0067, 318, 10, 0), EditAndReencode(Decode(0, 0x0067, 318, 0, 0), "magicRate", 10));
        Assert.Equal(Words(0x0067, 318, 0, 0), EditAndReencode(Decode(0, 0x0067, 318, 5, 0), "magicRate", 0)); // runtime-normalized form round-trips

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0067, 318, 0, 0), "magicRate", 1));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0067, 318, 0, 0), "baseDamage", 1)); // unknown field (simulateMagic's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0067, 318, 0, 0), "enemyOperand", 2)); // unknown field: target is the caller slot, no operand
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0067, 318, 0, 0), "objectId", 318)); // unknown field (enemy view's name)
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0067, 318, 0, 0), "magicId", -1)); // unsigned WORD
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0067, 318, 0, 0), "magicId", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0067, 318, 0, 0), "magicRate", 65_536));
    }

    // --- 0x0069 enemyEscape (shared, script.c:2035-2041): PAL_BattleEnemyEscape() (battle.c:1376)
    //     animates every remaining enemy off-screen and terminates the battle (result 0); no
    //     operands are consumed (real data has one operand0 = 1 which only feeds the shared
    //     preamble's object selection, unused by the case body). ---
    [Fact]
    public void EnemyEscape_round_trips_preserved_operands_and_rejects_unknown_fields()
    {
        byte[] original = Words(0x0069, 1, 0, 0); // the real-data form with a non-zero unused operand
        var command = Decode(0, 0x0069, 1, 0, 0);
        Assert.Equal("enemyEscape", (string)command["op"]!);

        Assert.Equal(original, Reencode(command)); // no mapped fields: byte-exact, unused operand preserved

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0069, 1, 0, 0), "scope", 1)); // not the all-enemies flag shape
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0069, 1, 0, 0), "result", 0)); // setBattleResult's name
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0069, 1, 0, 0), "to", "L0001"));
    }

    // --- 0x006A stealFromEnemy (shared, script.c:2042-2048): PAL_BattleStealFromEnemy(
    //     wEventObjectID, operand0) (fight.c:5193) - the moving player steals from the caller enemy
    //     slot; operand 0 is the steal rate, succeeding when RandomLong(0, 10) <= rate, with rate 0
    //     always succeeding (fight.c:5254). Operands 1/2 are unused. ---
    [Fact]
    public void StealFromEnemy_edits_rate_and_preserves_operands()
    {
        byte[] original = Words(0x006A, 6, 1, 2);
        var command = Decode(0, 0x006A, 6, 1, 2);
        Assert.Equal("stealFromEnemy", (string)command["op"]!);
        Assert.Equal(6, (int)command["stealRate"]!);

        Assert.Equal(Words(0x006A, 0, 1, 2), EditAndReencode(command, "stealRate", 0)); // zero = always succeeds, legal WORD
        Assert.Equal(Words(0x006A, 65_535, 1, 2), EditAndReencode(Decode(0, 0x006A, 6, 1, 2), "stealRate", 65_535)); // full unsigned WORD

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x006A, 6, 1, 2), "stealRate", 7));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x006A, 6, 1, 2), "rate", 7)); // unknown field (randomJump's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x006A, 6, 1, 2), "itemId", 61)); // unknown field (loot comes from the enemy table)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x006A, 6, 1, 2), "enemyOperand", 2)); // unknown field: target is the caller slot
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x006A, 6, 1, 2), "stealRate", -1)); // unsigned WORD, no SHORT cast
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x006A, 6, 1, 2), "stealRate", 65_536));
    }

    // --- 0x006B blowAwayEnemies (shared, script.c:2049-2055): g_Battle.iBlow = (SHORT)operand0 -
    //     a signed wind offset applied to every enemy during later magic animations
    //     (fight.c:2681: positive blows right/down, negative left/up), reset at battle boundaries.
    //     Real data uses -3/-2. Operands 1/2 are unused. ---
    [Fact]
    public void BlowAwayEnemies_edits_signed_strength_and_preserves_operands()
    {
        byte[] original = Words(0x006B, unchecked((int)0xFFFD), 0, 0); // real-data form: (SHORT) -3
        var command = Decode(0, 0x006B, unchecked((int)0xFFFD), 0, 0);
        Assert.Equal("blowAwayEnemies", (string)command["op"]!);
        Assert.Equal(-3, (int)command["strength"]!);

        Assert.Equal(Words(0x006B, unchecked((int)0xFFFE), 0, 0), EditAndReencode(command, "strength", -2)); // the other real-data value
        Assert.Equal(Words(0x006B, 3, 0, 0), EditAndReencode(Decode(0, 0x006B, unchecked((int)0xFFFD), 0, 0), "strength", 3)); // positive: blow right/down
        Assert.Equal(Words(0x006B, 0, 0, 0), EditAndReencode(Decode(0, 0x006B, unchecked((int)0xFFFD), 0, 0), "strength", 0)); // still the battle's no-wind form
        Assert.Equal(Words(0x006B, unchecked((int)0x8000), 0, 0), EditAndReencode(Decode(0, 0x006B, unchecked((int)0xFFFD), 0, 0), "strength", -32_768)); // full negative range
        Assert.Equal(Words(0x006B, 32_767, 0, 0), EditAndReencode(Decode(0, 0x006B, unchecked((int)0xFFFD), 0, 0), "strength", 32_767)); // full positive range

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x006B, unchecked((int)0xFFFD), 0, 0), "strength", -4));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x006B, unchecked((int)0xFFFD), 0, 0), "damage", 1)); // unknown field
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x006B, unchecked((int)0xFFFD), 0, 0), "blowUnits", 1)); // unknown field
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x006B, unchecked((int)0xFFFD), 0, 0), "strength", 32_768)); // signed WORD bound
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x006B, unchecked((int)0xFFFD), 0, 0), "strength", -32_769)); // signed WORD bound
    }

    // --- 0x0089 setBattleResult (shared, script.c:2559-2564): g_Battle.BattleResult = operand0 -
    //     the raw BATTLERESULT value (battle.h:33-40: 0 terminated, 1 lost, 3 won, 0xFFFF fleed,
    //     1000..1002 ongoing phases) kept as an unsigned WORD like status rows, no enum names.
    //     Operands 1/2 are unused. ---
    [Fact]
    public void SetBattleResult_edits_result_word_and_preserves_operands()
    {
        byte[] original = Words(0x0089, 0, 4, 5);
        var command = Decode(0, 0x0089, 0, 4, 5);
        Assert.Equal("setBattleResult", (string)command["op"]!);
        Assert.Equal(0, (int)command["result"]!);

        Assert.Equal(Words(0x0089, 3, 4, 5), EditAndReencode(command, "result", 3)); // kBattleResultWon
        Assert.Equal(Words(0x0089, 1, 4, 5), EditAndReencode(Decode(0, 0x0089, 0, 4, 5), "result", 1)); // kBattleResultLost
        Assert.Equal(Words(0x0089, unchecked((int)0xFFFF), 4, 5), EditAndReencode(Decode(0, 0x0089, 0, 4, 5), "result", 65_535)); // kBattleResultFleed: only fits unsigned
        Assert.Equal(Words(0x0089, 1000, 4, 5), EditAndReencode(Decode(0, 0x0089, 0, 4, 5), "result", 1000)); // kBattleResultOnGoing

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0089, 0, 4, 5), "result", 3));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0089, 0, 4, 5), "battleResult", 3)); // no enum-named field
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0089, 0, 4, 5), "won", true)); // no boolean field
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0089, 0, 4, 5), "status", 0)); // unknown field (status-row name)
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0089, 0, 4, 5), "result", -1)); // unsigned WORD
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0089, 0, 4, 5), "result", 65_536));
    }

    // --- 0x008A enableAutoBattle (shared, script.c:2566-2571): gpGlobals->fAutoBattle = TRUE -
    //     no operands, no state read. All three operands are preserved as source WORDs. ---
    [Fact]
    public void EnableAutoBattle_round_trips_preserved_operands_and_rejects_unknown_fields()
    {
        byte[] original = Words(0x008A, 3, 4, 5);
        var command = Decode(0, 0x008A, 3, 4, 5);
        Assert.Equal("enableAutoBattle", (string)command["op"]!);

        Assert.Equal(original, Reencode(command)); // no mapped fields: byte-exact

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x008A, 3, 4, 5), "enabled", true)); // no boolean field
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x008A, 3, 4, 5), "scope", 1));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x008A, 3, 4, 5), "mode", 1));
    }

    // --- 0x0092 showMagicCastAnimation (shared, script.c:2639-2666): operand 0 picks the caster -
    //     a 1-based battle player slot (rgPlayer[operand0 - 1] plays the pre-magic anim and holds
    //     frame 6); WORD 0 picks no single player and only the party-wide color-shift loop runs.
    //     No magic operand exists: the animation is purely visual. Operands 1/2 are unused. ---
    [Fact]
    public void ShowMagicCastAnimation_edits_player_operand_and_preserves_operands()
    {
        byte[] original = Words(0x0092, 2, 1, 2);
        var command = Decode(0, 0x0092, 2, 1, 2);
        Assert.Equal("showMagicCastAnimation", (string)command["op"]!);
        Assert.Equal(2, (int)command["playerOperand"]!);

        Assert.Equal(Words(0x0092, 1, 1, 2), EditAndReencode(command, "playerOperand", 1)); // first party slot
        Assert.Equal(Words(0x0092, 0, 1, 2), EditAndReencode(Decode(0, 0x0092, 2, 1, 2), "playerOperand", 0)); // 0 = none: only the color-shift loop
        Assert.Equal(Words(0x0092, 65_535, 1, 2), EditAndReencode(Decode(0, 0x0092, 2, 1, 2), "playerOperand", 65_535)); // full unsigned WORD

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0092, 2, 1, 2), "playerOperand", 3));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0092, 2, 1, 2), "roleOperand", 3)); // unknown field (caller-shape name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0092, 2, 1, 2), "memberIndex", 1)); // unknown field (0x0015's 0-based shape)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0092, 2, 1, 2), "magicId", 318)); // unknown field: no magic operand
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0092, 2, 1, 2), "playerOperand", -1)); // unsigned WORD, no SHORT cast
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0092, 2, 1, 2), "playerOperand", 65_536));
    }

    // --- 0x009C enemyDivide (shared, script.c:2778-2870): with exactly one non-empty enemy slot
    //     and caller HP above 1, operand 0 new copies are spawned (WORD 0 normalized to 1 at
    //     runtime only; every part then keeps (hp + copies) / (copies + 1) HP); otherwise operand 1
    //     jumps when non-zero - a nullable reference (WORD 0 = no jump), so null encodes WORD 0 and
    //     a reference to address 0 is rejected. Operand 2 is unused. ---
    [Fact]
    public void EnemyDivide_edits_copies_and_nullable_fail_target_and_preserves_operand2()
    {
        byte[] original = Words(0x009C, 0, 0, 7); // real-data form: copies 0 (runtime 1), no fail target
        var command = Decode(0, 0x009C, 0, 0, 7);
        Assert.Equal("enemyDivide", (string)command["op"]!);
        Assert.Equal(0, (int)command["copies"]!);
        Assert.Null(command["failTarget"]);

        Assert.Equal(Words(0x009C, 2, 0, 7), EditAndReencode(command, "copies", 2)); // 2 copies + original, each (hp + 2) / 3
        Assert.Equal(Words(0x009C, 1, 0, 7), EditAndReencode(Decode(0, 0x009C, 0, 0, 7), "copies", 1));
        Assert.Equal(Words(0x009C, 0, 0, 7), EditAndReencode(Decode(0, 0x009C, 2, 0, 7), "copies", 0)); // runtime-normalized form round-trips
        var failed = Decode(0, 0x009C, 0, 0, 7);
        failed["failTarget"] = "L0003";
        Assert.Equal(Words(0x009C, 0, 3, 7), Reencode(failed)); // explicit cannot-divide jump
        Assert.Equal(Words(0x009C, 0, 0, 7), EditAndReencode(Decode(0, 0x009C, 0, 3, 7), "failTarget", null)); // null re-encodes WORD 0

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x009C, 0, 3, 7), "copies", 1));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x009C, 0, 0, 7), "to", "L0001")); // unknown field (required-reference name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x009C, 0, 0, 7), "failTarget", "L0000")); // address zero must use null
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x009C, 0, 0, 7), "failTarget", "L0100")); // dangling
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x009C, 0, 0, 7), "copies", -1)); // unsigned WORD, no SHORT cast
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x009C, 0, 0, 7), "copies", 65_536));
    }

    // --- 0x009E enemySummon (shared, script.c:2872-2955): the caller enemy plays its cast frames,
    //     then operand 1 monsters are summoned into empty slots - operand 0 is the summoned
    //     monster's enemy-view object ID (0/0xFFFF = the summoner's own object), operand 1 is read
    //     as (SHORT) with non-positive values normalized to 1 at runtime only. When too few empty
    //     slots remain, the battle hides enemies, or the summoner is asleep/paralyzed/confused,
    //     operand 2 jumps when non-zero - a nullable reference like enemyDivide.failTarget. ---
    [Fact]
    public void EnemySummon_edits_object_id_signed_count_and_nullable_fail_target()
    {
        byte[] original = Words(0x009E, 433, 2, 0);
        var command = Decode(0, 0x009E, 433, 2, 0);
        Assert.Equal("enemySummon", (string)command["op"]!);
        Assert.Equal(433, (int)command["objectId"]!);
        Assert.Equal(2, (int)command["count"]!);
        Assert.Null(command["failTarget"]);

        Assert.Equal(Words(0x009E, 512, 2, 0), EditAndReencode(command, "objectId", 512)); // real-data enemy-view value
        Assert.Equal(Words(0x009E, 0, 2, 0), EditAndReencode(Decode(0, 0x009E, 433, 2, 0), "objectId", 0)); // 0 = the summoner itself
        Assert.Equal(Words(0x009E, unchecked((int)0xFFFF), 2, 0), EditAndReencode(Decode(0, 0x009E, 433, 2, 0), "objectId", 65_535)); // 0xFFFF = the summoner itself too
        Assert.Equal(Words(0x009E, 433, 4, 0), EditAndReencode(Decode(0, 0x009E, 433, 2, 0), "count", 4)); // real-data 4-monster summon
        Assert.Equal(Words(0x009E, 433, 0, 0), EditAndReencode(Decode(0, 0x009E, 433, 2, 0), "count", 0)); // runtime-normalized 1, encoding stays 0
        Assert.Equal(Words(0x009E, 433, -1, 0), EditAndReencode(Decode(0, 0x009E, 433, 2, 0), "count", -1)); // negative also normalized to 1, encoding keeps the WORD
        var blocked = Decode(0, 0x009E, 433, 2, 0);
        blocked["failTarget"] = "L0005";
        Assert.Equal(Words(0x009E, 433, 2, 5), Reencode(blocked)); // explicit cannot-summon jump
        Assert.Equal(Words(0x009E, 433, 2, 0), EditAndReencode(Decode(0, 0x009E, 433, 2, 5), "failTarget", null));

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x009E, 433, 2, 0), "count", 3));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x009E, 433, 2, 0), "copies", 2)); // unknown field (enemyDivide's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x009E, 433, 2, 0), "enemyTeamId", 433)); // unknown field (enemy-team row, another table)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x009E, 433, 2, 0), "failTarget", "L0000")); // address zero must use null
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x009E, 433, 2, 0), "failTarget", "missing#L0001")); // dangling
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x009E, 433, 2, 0), "count", 32_768)); // signed WORD bound
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x009E, 433, 2, 0), "objectId", -1)); // unsigned WORD
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x009E, 433, 2, 0), "objectId", 65_536));
    }

    // --- 0x009F enemyTransform (shared, script.c:2956-3000): the caller enemy becomes operand 0's
    //     enemy-view object - wObjectID is replaced and the enemy-table row is reloaded through
    //     rgObject[operand0].enemy.wEnemyID while the current HP is kept; blocked silently by
    //     hiding/sleep/paralysis/confusion (no jump). Operands 1/2 are unused. ---
    [Fact]
    public void EnemyTransform_edits_object_id_and_preserves_operands()
    {
        byte[] original = Words(0x009F, 410, 1, 2);
        var command = Decode(0, 0x009F, 410, 1, 2);
        Assert.Equal("enemyTransform", (string)command["op"]!);
        Assert.Equal(410, (int)command["objectId"]!);

        Assert.Equal(Words(0x009F, 434, 1, 2), EditAndReencode(command, "objectId", 434)); // another real-data form
        Assert.Equal(Words(0x009F, 0, 1, 2), EditAndReencode(Decode(0, 0x009F, 410, 1, 2), "objectId", 0)); // zero is a legal WORD (runtime reads rgObject[0])
        Assert.Equal(Words(0x009F, 65_535, 1, 2), EditAndReencode(Decode(0, 0x009F, 410, 1, 2), "objectId", 65_535)); // full unsigned WORD

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x009F, 410, 1, 2), "objectId", 461));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x009F, 410, 1, 2), "magicId", 410)); // unknown field (magic view's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x009F, 410, 1, 2), "battleSprite", 410)); // unknown field
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x009F, 410, 1, 2), "failTarget", "L0001")); // unknown field: blocked transforms do not jump
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x009F, 410, 1, 2), "objectId", -1)); // unsigned WORD
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x009F, 410, 1, 2), "objectId", 65_536));
    }
}
