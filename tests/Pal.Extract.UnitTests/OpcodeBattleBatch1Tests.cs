using Pal.Extract;
using Xunit;
using static Pal.Extract.Tests.OpcodeCounterexampleHarness;

namespace Pal.Extract.Tests;

// M5 opcode battle batch 1 (#103): direct battle-manipulation verbs. startBattle and setBattlefield
// are re-verified entries from #78/#49; inflictDamageToEnemy, changeBattleSprite, collectEnemyForItems,
// transformCollectedEnemies, drainHpFromEnemy, playerFlee, simulateMagic, halveEnemyHp, killEnemy and
// throwWeapon are new. Topology per script.c: 0x0007 lives in the PAL_RunTriggerScript switch
// (script.c:3316, re-verified unchanged); the other eleven live in the shared PAL_InterpretInstruction
// switch (script.c:650) and only 0x0033/0x0034/0x003A jump (wScriptEntry = to - 1 plus the function-tail
// return+1 at script.c:3085), everything else breaks into the sequential tail. Enemy addressing:
//   - 0x0021 operand 0 is the #101 all-enemies boolean (0 = rgEnemy[wEventObjectID], non-zero = every
//     non-empty enemy slot); 0x0033/0x003A/0x005B/0x0060 target rgEnemy[wEventObjectID] with no operand.
//   - 0x0042 operand 2 targets rgEnemy through the roleOperand shape read as (SHORT) (script.c:1634):
//     0 or negative = caller slot wEventObjectID, positive = operand - 1 as a 1-based enemy index.
//   - 0x0039 drains rgEnemy[wEventObjectID] into the moving player (wMovingPlayerIndex), no operand.
// Magic operands are object.json magic-view object IDs: PAL_BattleSimulateMagic consumes operand 0 as
// wMagicObjectID through rgObject[id].magic (fight.c:5301,5321), and throwWeapon's real data values
// (344/360) fall inside the data-schema magic-view evidence range, so the frozen name is magicId.
// Signedness: only simulateMagic's enemyOperand has a (SHORT) cast; every numeric field of the ten
// other cases is an unsigned WORD. Counter-examples follow the #78/#96 skeleton: edit a semantic
// field -> re-encoded bytes differ from the source bytes and equal the hand-computed value; unknown
// fields and out-of-range values are rejected.
public sealed class OpcodeBattleBatch1Tests
{
    // --- 0x0007 startBattle (#78 named, #103 re-verified; trigger switch, script.c:3316-3335):
    //     PAL_StartBattle(operand0, !operand2) - operand 0 is the enemy-team table row
    //     (lprgEnemyTeam[wEnemyTeam], battle.c:1531/1602), not an object union index; lost jumps to
    //     operand 1 when non-zero, fleed to operand 2 when non-zero, isBoss <=> operand 2 == 0.
    //     Existing mapping unchanged. ---
    [Fact]
    public void StartBattle_reverified_edits_enemy_team_word_and_keeps_battle_result_targets()
    {
        byte[] original = Words(0x0007, 18, 4, 3);
        var command = Decode(0, 0x0007, 18, 4, 3);
        Assert.Equal("startBattle", (string)command["op"]!);
        Assert.Equal(18, (int)command["enemyTeamId"]!);
        Assert.Equal("L0004", (string)command["lossTarget"]!);
        Assert.Equal("L0003", (string)command["fleeTarget"]!);
        Assert.False((bool)command["isBoss"]!);

        Assert.Equal(Words(0x0007, 19, 4, 3), EditAndReencode(command, "enemyTeamId", 19)); // enemy-team.json row
        var boss = Decode(0, 0x0007, 18, 4, 3);
        boss["fleeTarget"] = null;
        boss["isBoss"] = true;
        Assert.Equal(Words(0x0007, 18, 4, 0), Reencode(boss)); // boss: no flee target
        Assert.Equal(Words(0x0007, 18, 0, 3), EditAndReencode(Decode(0, 0x0007, 18, 4, 3), "lossTarget", null)); // loss falls through to next

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0007, 18, 4, 3), "enemyTeamId", 20));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0007, 18, 4, 3), "teamId", 19)); // pre-#78 name
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0007, 18, 4, 3), "lossTarget", "L0000")); // address zero must use null
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0007, 18, 4, 3), "enemyTeamId", 65_536));
    }

    // --- 0x0021 inflictDamageToEnemy (shared, script.c:1026-1050): operand 0 is the #101 all-enemies
    //     boolean (0 = rgEnemy[wEventObjectID], non-zero = loop over non-empty enemy slots); operand 1
    //     has no (SHORT) cast and subtracts from wHealth as an unsigned WORD. Operand 2 is unused by
    //     the reference case. ---
    [Fact]
    public void InflictDamageToEnemy_edits_scope_and_damage_and_preserves_operand2()
    {
        byte[] original = Words(0x0021, 0, 90, 0);
        var command = Decode(0, 0x0021, 0, 90, 0);
        Assert.Equal("inflictDamageToEnemy", (string)command["op"]!);
        Assert.Equal(0, (int)command["scope"]!);
        Assert.Equal(90, (int)command["damage"]!);

        Assert.Equal(Words(0x0021, 1, 90, 0), EditAndReencode(command, "scope", 1)); // every non-empty enemy slot
        Assert.Equal(Words(0x0021, 0, 400, 0), EditAndReencode(Decode(0, 0x0021, 0, 90, 0), "damage", 400));
        Assert.Equal(Words(0x0021, 0, 65_535, 0), EditAndReencode(Decode(0, 0x0021, 0, 90, 0), "damage", 65_535)); // full unsigned WORD

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0021, 0, 90, 0), "damage", 91));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0021, 0, 90, 0), "amount", 1)); // unknown field (changeHp/0x0039's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0021, 0, 90, 0), "objectId", 1)); // unknown field
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0021, 0, 90, 0), "damage", -1)); // unsigned WORD, no SHORT cast
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0021, 0, 90, 0), "damage", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0021, 0, 90, 0), "scope", 65_536));
    }

    // --- 0x0031 changeBattleSprite (shared, script.c:1429-1435): writes the extra-body-part
    //     equipment-effect register rgwSpriteNumInBattle[wEventObjectID] - a temporary battle-sprite
    //     override for the caller's player role (same register boostStat writes), cleared after the
    //     battle. Operand 0 is the sprite number; operands 1/2 are unused by the reference case. ---
    [Fact]
    public void ChangeBattleSprite_edits_battle_sprite_and_preserves_operands()
    {
        byte[] original = Words(0x0031, 5, 3, 4);
        var command = Decode(0, 0x0031, 5, 3, 4);
        Assert.Equal("changeBattleSprite", (string)command["op"]!);
        Assert.Equal(5, (int)command["battleSprite"]!);

        Assert.Equal(Words(0x0031, 2, 3, 4), EditAndReencode(command, "battleSprite", 2));
        Assert.Equal(Words(0x0031, 0, 3, 4), EditAndReencode(Decode(0, 0x0031, 5, 3, 4), "battleSprite", 0)); // zero is a legal WORD

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0031, 5, 3, 4), "battleSprite", 6));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0031, 5, 3, 4), "frame", 2)); // unknown field (setObjectGesture's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0031, 5, 3, 4), "field", 1)); // unknown field (PLAYERROLES row name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0031, 5, 3, 4), "sprite", 2)); // unknown field
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0031, 5, 3, 4), "battleSprite", -1));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0031, 5, 3, 4), "battleSprite", 65_536));
    }

    // --- 0x0033 collectEnemyForItems (shared, script.c:1437-1450): rgEnemy[wEventObjectID]'s
    //     wCollectValue accumulates into the global collect counter; a zero value jumps to operand 0.
    //     The target is a required reference - WORD 0 references L0000, unlike a nullable
    //     battleTarget. Operands 1/2 are unused by the reference case. ---
    [Fact]
    public void CollectEnemyForItems_edits_jump_target_and_preserves_operands()
    {
        byte[] original = Words(0x0033, 3, 5, 6);
        var command = Decode(0, 0x0033, 3, 5, 6);
        Assert.Equal("collectEnemyForItems", (string)command["op"]!);
        Assert.Equal("L0003", (string)command["to"]!);

        Assert.Equal(Words(0x0033, 1, 5, 6), EditAndReencode(command, "to", "L0001"));
        Assert.Equal(Words(0x0033, 0, 5, 6), EditAndReencode(Decode(0, 0x0033, 3, 5, 6), "to", "L0000")); // zero target references index 0

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0033, 3, 5, 6), "to", "L0002"));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0033, 3, 5, 6), "to", null)); // required reference, not nullable
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0033, 3, 5, 6), "to", "L0100")); // dangling
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0033, 3, 5, 6), "shortageTarget", "L0001")); // unknown field (removeItem's nullable name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0033, 3, 5, 6), "collectValue", 1)); // unknown field
    }

    // --- 0x0034 transformCollectedEnemies (shared, script.c:1452-1519): with a positive global
    //     collect counter, grants store[0].rgwItems[RandomLong(1, min(9, counter)) - 1] and shows the
    //     obtained-item dialog; a zero counter jumps to operand 0. Operands 1/2 are unused. ---
    [Fact]
    public void TransformCollectedEnemies_edits_jump_target_and_preserves_operands()
    {
        byte[] original = Words(0x0034, 4, 7, 9);
        var command = Decode(0, 0x0034, 4, 7, 9);
        Assert.Equal("transformCollectedEnemies", (string)command["op"]!);
        Assert.Equal("L0004", (string)command["to"]!);

        Assert.Equal(Words(0x0034, 2, 7, 9), EditAndReencode(command, "to", "L0002"));
        Assert.Equal(Words(0x0034, 0, 7, 9), EditAndReencode(Decode(0, 0x0034, 4, 7, 9), "to", "L0000"));

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0034, 4, 7, 9), "to", "L0005"));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0034, 4, 7, 9), "to", "missing#L0001")); // dangling file reference
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0034, 4, 7, 9), "itemId", 1)); // unknown field (granted item comes from the store table, not an operand)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0034, 4, 7, 9), "count", 1)); // unknown field
    }

    // --- 0x0039 drainHpFromEnemy (shared, script.c:1573-1586): rgEnemy[wEventObjectID].e.wHealth
    //     loses operand 0 HP and the moving player (rgParty[wMovingPlayerIndex]) gains the same
    //     amount capped at max HP. Unsigned WORD arithmetic; operands 1/2 are unused. ---
    [Fact]
    public void DrainHpFromEnemy_edits_amount_and_preserves_operands()
    {
        byte[] original = Words(0x0039, 180, 1, 2);
        var command = Decode(0, 0x0039, 180, 1, 2);
        Assert.Equal("drainHpFromEnemy", (string)command["op"]!);
        Assert.Equal(180, (int)command["amount"]!);

        Assert.Equal(Words(0x0039, 999, 1, 2), EditAndReencode(command, "amount", 999));
        Assert.Equal(Words(0x0039, 65_535, 1, 2), EditAndReencode(Decode(0, 0x0039, 180, 1, 2), "amount", 65_535)); // full unsigned WORD

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0039, 180, 1, 2), "amount", 181));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0039, 180, 1, 2), "damage", 1)); // unknown field (0x0021's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0039, 180, 1, 2), "roleOperand", 2)); // unknown field: player is the moving player, no operand
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0039, 180, 1, 2), "amount", -1)); // unsigned WORD, no SHORT cast
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0039, 180, 1, 2), "amount", 65_536));
    }

    // --- 0x003A playerFlee (shared, script.c:1588-1602): boss battles cannot flee -
    //     g_Battle.fIsBoss jumps to operand 0, otherwise PAL_BattlePlayerEscape() runs. Operands 1/2
    //     are unused by the reference case. ---
    [Fact]
    public void PlayerFlee_edits_jump_target_and_preserves_operands()
    {
        byte[] original = Words(0x003A, 5, 6, 7);
        var command = Decode(0, 0x003A, 5, 6, 7);
        Assert.Equal("playerFlee", (string)command["op"]!);
        Assert.Equal("L0005", (string)command["to"]!);

        Assert.Equal(Words(0x003A, 1, 6, 7), EditAndReencode(command, "to", "L0001"));
        Assert.Equal(Words(0x003A, 0, 6, 7), EditAndReencode(Decode(0, 0x003A, 5, 6, 7), "to", "L0000"));

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x003A, 5, 6, 7), "to", "L0004"));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x003A, 5, 6, 7), "to", "L0100")); // dangling
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x003A, 5, 6, 7), "fleeTarget", "L0001")); // unknown field (startBattle's nullable name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x003A, 5, 6, 7), "scope", 1)); // unknown field: no operand picks the fleer
    }

    // --- 0x0042 simulateMagic (shared, script.c:1630-1640): PAL_BattleSimulateMagic(target,
    //     operand0, operand1) - operand 0 is the object.json magic-view object ID, operand 1 the
    //     base damage WORD, operand 2 targets rgEnemy through the roleOperand shape read as (SHORT)
    //     (1634: 0 or negative = caller slot wEventObjectID, positive = operand - 1 as a 1-based
    //     enemy index); an apply-to-all magic overrides the target (fight.c:5321). ---
    [Fact]
    public void SimulateMagic_edits_magic_id_base_damage_and_signed_enemy_operand()
    {
        byte[] original = Words(0x0042, 24, 0, 0);
        var command = Decode(0, 0x0042, 24, 0, 0);
        Assert.Equal("simulateMagic", (string)command["op"]!);
        Assert.Equal(24, (int)command["magicId"]!);
        Assert.Equal(0, (int)command["baseDamage"]!);
        Assert.Equal(0, (int)command["enemyOperand"]!);

        Assert.Equal(Words(0x0042, 323, 0, 0), EditAndReencode(command, "magicId", 323));
        Assert.Equal(Words(0x0042, 24, 50, 0), EditAndReencode(Decode(0, 0x0042, 24, 0, 0), "baseDamage", 50));
        Assert.Equal(Words(0x0042, 24, 0, 2), EditAndReencode(Decode(0, 0x0042, 24, 0, 0), "enemyOperand", 2)); // explicit 1-based enemy index 1
        Assert.Equal(Words(0x0042, 24, 0, 1), EditAndReencode(Decode(0, 0x0042, 24, 0, 0), "enemyOperand", 1)); // explicit enemy index 0
        Assert.Equal(Words(0x0042, 24, 0, unchecked((int)0xFFFF)), EditAndReencode(Decode(0, 0x0042, 24, 0, 0), "enemyOperand", -1)); // negative: runtime falls back to the caller slot
        Assert.Equal(Words(0x0042, 24, 0, unchecked((int)0x8000)), EditAndReencode(Decode(0, 0x0042, 24, 0, 0), "enemyOperand", -32_768)); // full negative range

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0042, 24, 0, 0), "baseDamage", 1));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0042, 24, 0, 0), "roleOperand", 2)); // unknown field (player-role shape name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0042, 24, 0, 0), "scope", 1)); // unknown field
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0042, 24, 0, 0), "objectId", 24)); // unknown field (poison view's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0042, 24, 0, 0), "enemyOperand", 32_768)); // signed WORD bound
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0042, 24, 0, 0), "enemyOperand", 40_000)); // negative-as-SHORT runtime meaning
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0042, 24, 0, 0), "magicId", -1)); // unsigned WORD
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0042, 24, 0, 0), "magicId", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0042, 24, 0, 0), "baseDamage", 65_536));
    }

    // --- 0x004A setBattlefield (#78 named, #103 re-verified; shared, script.c:1719-1723):
    //     gpGlobals->wNumBattleField = operand0 - a plain battlefield number. Existing mapping
    //     unchanged; operands 1/2 are unused by the reference case. ---
    [Fact]
    public void SetBattlefield_reverified_edits_battlefield_id_and_preserves_operands()
    {
        byte[] original = Words(0x004A, 24, 8, 9);
        var command = Decode(0, 0x004A, 24, 8, 9);
        Assert.Equal("setBattlefield", (string)command["op"]!);
        Assert.Equal(24, (int)command["battlefieldId"]!);

        Assert.Equal(Words(0x004A, 12, 8, 9), EditAndReencode(command, "battlefieldId", 12));
        Assert.Equal(Words(0x004A, 0, 8, 9), EditAndReencode(Decode(0, 0x004A, 24, 8, 9), "battlefieldId", 0));

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x004A, 24, 8, 9), "battlefieldId", 13));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x004A, 24, 8, 9), "sceneId", 1)); // unknown field
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x004A, 24, 8, 9), "musicId", 1)); // unknown field
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x004A, 24, 8, 9), "battlefieldId", -1));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x004A, 24, 8, 9), "battlefieldId", 65_536));
    }

    // --- 0x005B halveEnemyHp (shared, script.c:1895-1904): rgEnemy[wEventObjectID] loses
    //     min(wHealth / 2 + 1, operand 0) HP - operand 0 caps the halving damage (unsigned WORD
    //     arithmetic). Operands 1/2 are unused by the reference case. ---
    [Fact]
    public void HalveEnemyHp_edits_damage_cap_and_preserves_operands()
    {
        byte[] original = Words(0x005B, 1000, 4, 5);
        var command = Decode(0, 0x005B, 1000, 4, 5);
        Assert.Equal("halveEnemyHp", (string)command["op"]!);
        Assert.Equal(1000, (int)command["maxDamage"]!);

        Assert.Equal(Words(0x005B, 100, 4, 5), EditAndReencode(command, "maxDamage", 100));
        Assert.Equal(Words(0x005B, 0, 4, 5), EditAndReencode(Decode(0, 0x005B, 1000, 4, 5), "maxDamage", 0)); // cap 0: no damage, still a legal WORD
        Assert.Equal(Words(0x005B, 65_535, 4, 5), EditAndReencode(Decode(0, 0x005B, 1000, 4, 5), "maxDamage", 65_535)); // full unsigned WORD

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x005B, 1000, 4, 5), "maxDamage", 999));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x005B, 1000, 4, 5), "amount", 1)); // unknown field (0x0039's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x005B, 1000, 4, 5), "damage", 1)); // unknown field (0x0021's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x005B, 1000, 4, 5), "percent", 1)); // unknown field (enemyHpAboveJump's name)
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x005B, 1000, 4, 5), "maxDamage", -1)); // unsigned WORD
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x005B, 1000, 4, 5), "maxDamage", 65_536));
    }

    // --- 0x0060 killEnemy (shared, script.c:1950-1954): rgEnemy[wEventObjectID].e.wHealth = 0 -
    //     the immediate-KO counterpart of killPlayer, same no-operand single-target shape. No
    //     operands are mapped. ---
    [Fact]
    public void KillEnemy_round_trips_preserved_operands_and_rejects_unknown_fields()
    {
        byte[] original = Words(0x0060, 3, 4, 5);
        var command = Decode(0, 0x0060, 3, 4, 5);
        Assert.Equal("killEnemy", (string)command["op"]!);

        Assert.Equal(original, Reencode(command)); // no mapped fields: byte-exact

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0060, 3, 4, 5), "scope", 1)); // not the all-enemies flag shape
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0060, 3, 4, 5), "status", 1));
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0060, 3, 4, 5), "damage", 1));
    }

    // --- 0x0066 throwWeapon (shared, script.c:2007-2014): PAL_BattleSimulateMagic((SHORT)
    //     wEventObjectID, operand0, operand1 * 5 + moving-player attackStrength * RandomLong(0, 3))
    //     - operand 0 is the thrown weapon's object.json magic-view object ID (real data 344/360
    //     inside the magic evidence range), operand 1 counts 5-HP damage units; the target is the
    //     caller's enemy slot with no operand. Operand 2 is unused by the reference case. ---
    [Fact]
    public void ThrowWeapon_edits_magic_id_and_damage_units_and_preserves_operand2()
    {
        byte[] original = Words(0x0066, 344, 105, 0);
        var command = Decode(0, 0x0066, 344, 105, 0);
        Assert.Equal("throwWeapon", (string)command["op"]!);
        Assert.Equal(344, (int)command["magicId"]!);
        Assert.Equal(105, (int)command["damageUnits"]!);

        Assert.Equal(Words(0x0066, 360, 105, 0), EditAndReencode(command, "magicId", 360));
        Assert.Equal(Words(0x0066, 344, 10, 0), EditAndReencode(Decode(0, 0x0066, 344, 105, 0), "damageUnits", 10)); // 50 fixed HP
        Assert.Equal(Words(0x0066, 344, 0, 0), EditAndReencode(Decode(0, 0x0066, 344, 105, 0), "damageUnits", 0)); // zero units: only the attack part, real-data form
        Assert.Equal(Words(0x0066, 344, 65_535, 0), EditAndReencode(Decode(0, 0x0066, 344, 105, 0), "damageUnits", 65_535)); // full unsigned WORD

        Assert.NotEqual(original, EditAndReencode(Decode(0, 0x0066, 344, 105, 0), "damageUnits", 106));

        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0066, 344, 105, 0), "itemId", 344)); // unknown field: consumed via the magic view, real data inside the magic range
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0066, 344, 105, 0), "count", 1)); // unknown field
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0066, 344, 105, 0), "damage", 105)); // unknown field (0x0021's name)
        Assert.Throws<InvalidDataException>(() => EditAndReencode(Decode(0, 0x0066, 344, 105, 0), "enemyOperand", 2)); // unknown field: target is the caller's enemy slot
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0066, 344, 105, 0), "magicId", -1)); // unsigned WORD
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0066, 344, 105, 0), "magicId", 65_536));
        Assert.Throws<OverflowException>(() => EditAndReencode(Decode(0, 0x0066, 344, 105, 0), "damageUnits", 65_536));
    }
}
