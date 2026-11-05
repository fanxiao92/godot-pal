using System.Buffers.Binary;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Pal.Extract;

/// Binding kind of one semantic field declared by an opcode descriptor
/// (events-schema v1, opcode registry chapter).
public enum OpcodeFieldKind
{
    /// Unsigned operand re-encoded from an integer 0..65535.
    Word,
    /// Signed operand (-32768..32767) re-encoded as a two's-complement WORD.
    SignedWord,
    /// 0..255 field bound to the operand's low byte; the source WORD's unmapped high byte is preserved.
    LowByte,
    /// Required label reference resolving to a chunk-4 address.
    Reference,
    /// Nullable label reference: null encodes WORD 0, and a reference to address 0 is rejected.
    BattleTarget,
    /// Boolean coupled to a BattleTarget field on the same operand (true <=> that reference is null).
    BossFlag,
    /// Value derived from the opcode itself: value = opcode - Base.
    OpcodeOffset,
    /// Fixed JSON string that must match the descriptor (e.g. setDialog.box, end.mode).
    ConstantString,
}

/// One declared field binding of a named opcode.
public sealed record OpcodeField(
    string Name, OpcodeFieldKind Kind, int Operand = -1, int Base = 0,
    string? CoupledTarget = null, string? ConstantString = null);

/// Declared executor handling of an opcode, mirroring the script.c switch topology:
/// PAL_RunTriggerScript and PAL_RunAutoScript each own a small control-flow switch and
/// delegate everything else to the shared PAL_InterpretInstruction.
public enum OpcodeExecutor
{
    /// Handled by PAL_InterpretInstruction for both trigger and auto.
    Shared,
    /// Handled directly by the PAL_RunTriggerScript switch; auto would terminate.
    TriggerSwitchOnly,
    /// Handled by both switches with the same semantics.
    BothSwitches,
    /// Handled by both switches with different behavior (e.g. idle counters, description text).
    BothSwitchesDiffer,
    /// Handled directly by the PAL_RunAutoScript switch; a trigger execution would
    /// terminate (the shared PAL_InterpretInstruction default TerminateOnError).
    AutoSwitchOnly,
}

/// Declared control-flow successor kind of a named command.
public enum OpcodeSuccessor
{
    /// Advances to the WORD-order next entry.
    Sequential,
    /// Stops the script (end keep).
    Stop,
    /// Conditional jump: either the declared reference target or next.
    ConditionalJump,
    /// Battle-result dependent: next / lossTarget / fleeTarget.
    BattleResult,
    /// Sequential with the message-span advancement of dialog text.
    MessageSpan,
    /// Random multi-way jump: one of the next `count` entries (entry+1 .. entry+count).
    RandomBranch,
}

/// One named opcode: verb, operand field bindings, executor and successor declarations.
public sealed record OpcodeDescriptor(
    int Opcode, string Op, OpcodeField[] Fields,
    OpcodeExecutor Executor, OpcodeSuccessor Successor,
    bool AllowLegacyRaw = false, bool DecodeValidatesReferenceRange = false);

/// Table-driven registry for the events.json v1 named opcodes (M5 opcode registry).
/// Decode, validate, and encode all run through the generic paths below; opcode batches
/// only add descriptors and tests. Opcodes without a descriptor stay "raw".
public static class OpcodeRegistry
{
    private static readonly Regex Identifier = new(@"^[A-Za-z_][A-Za-z0-9_-]*\z", RegexOptions.CultureInvariant);

    private static readonly OpcodeField[] NoFields = [];

    private static readonly OpcodeDescriptor[] Registry =
    [
        new(0x0000, "end", [new("mode", OpcodeFieldKind.ConstantString, ConstantString: "keep")], OpcodeExecutor.BothSwitches, OpcodeSuccessor.Stop),
        new(0x0001, "end", [new("mode", OpcodeFieldKind.ConstantString, ConstantString: "advance")], OpcodeExecutor.BothSwitches, OpcodeSuccessor.Stop),
        new(0x0002, "end", [new("mode", OpcodeFieldKind.ConstantString, ConstantString: "redirect"), new("to", OpcodeFieldKind.Reference, 0), new("limit", OpcodeFieldKind.Word, 1)], OpcodeExecutor.BothSwitches, OpcodeSuccessor.ConditionalJump),
        new(0x0003, "goto", [new("to", OpcodeFieldKind.Reference, 0), new("limit", OpcodeFieldKind.Word, 1)], OpcodeExecutor.BothSwitches, OpcodeSuccessor.ConditionalJump),
        new(0x0004, "call", [new("to", OpcodeFieldKind.Reference, 0), new("eventObjectId", OpcodeFieldKind.Word, 1)], OpcodeExecutor.BothSwitches, OpcodeSuccessor.Sequential),
        new(0x0006, "randomJump", [new("rate", OpcodeFieldKind.Word, 0), new("to", OpcodeFieldKind.Reference, 1)], OpcodeExecutor.BothSwitches, OpcodeSuccessor.ConditionalJump),
        new(0x0007, "startBattle",
            [new("enemyTeamId", OpcodeFieldKind.Word, 0), new("lossTarget", OpcodeFieldKind.BattleTarget, 1),
             new("isBoss", OpcodeFieldKind.BossFlag, 2, CoupledTarget: "fleeTarget"), new("fleeTarget", OpcodeFieldKind.BattleTarget, 2)],
            OpcodeExecutor.TriggerSwitchOnly, OpcodeSuccessor.BattleResult, AllowLegacyRaw: true, DecodeValidatesReferenceRange: true),
        new(0x0008, "advanceEntry", NoFields, OpcodeExecutor.TriggerSwitchOnly, OpcodeSuccessor.Sequential),
        new(0x0009, "waitUpdate", [new("frames", OpcodeFieldKind.Word, 0), new("runTriggers", OpcodeFieldKind.Word, 1), new("updateGestures", OpcodeFieldKind.Word, 2)], OpcodeExecutor.BothSwitchesDiffer, OpcodeSuccessor.Sequential),
        new(0x000A, "confirmNoJump", [new("to", OpcodeFieldKind.Reference, 0)], OpcodeExecutor.TriggerSwitchOnly, OpcodeSuccessor.ConditionalJump),
        new(0x000B, "walkStep", [new("direction", OpcodeFieldKind.OpcodeOffset, Base: 0x000B)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x000C, "walkStep", [new("direction", OpcodeFieldKind.OpcodeOffset, Base: 0x000B)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x000D, "walkStep", [new("direction", OpcodeFieldKind.OpcodeOffset, Base: 0x000B)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x000E, "walkStep", [new("direction", OpcodeFieldKind.OpcodeOffset, Base: 0x000B)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x000F, "face", [new("direction", OpcodeFieldKind.Word, 0), new("frame", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0010, "walkTo", [new("x", OpcodeFieldKind.Word, 0), new("y", OpcodeFieldKind.Word, 1), new("h", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #105: same x/y/h operands as walkTo (0x0010) but at a lower speed - the frame-parity
        // gate ((wEventObjectID & 1) ^ (dwFrameNum & 1), script.c:692) runs the walk only every
        // other frame and PAL_NPCWalkTo is called at speed 2 instead of 3 (script.c:694); until
        // arrival the entry re-executes (wScriptEntry--), a runtime stall rather than a jump.
        new(0x0011, "walkToSlow", [new("x", OpcodeFieldKind.Word, 0), new("y", OpcodeFieldKind.Word, 1), new("h", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #105: the operand-0-selected event object (shared preamble pCurrent: 0/0xFFFF = caller,
        // else operand 0 is a 1-based event-object index, script.c:626-638) is placed at
        // xOffset + viewport.x + partyoffset.x, yOffset likewise (script.c:710-713) - offsets
        // relative to the party's map position, stored into the unsigned WORD x/y with no
        // (SHORT) cast (real data encodes -128 as 0xFF80).
        new(0x0012, "setObjectPositionRelativeToParty", [new("eventObjectId", OpcodeFieldKind.Word, 0), new("xOffset", OpcodeFieldKind.Word, 1), new("yOffset", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0013, "setObjectPosition", [new("eventObjectId", OpcodeFieldKind.Word, 0), new("x", OpcodeFieldKind.Word, 1), new("y", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0014, "setObjectGesture", [new("frame", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0015, "setPartyFacing", [new("direction", OpcodeFieldKind.Word, 0), new("gesture", OpcodeFieldKind.Word, 1), new("memberIndex", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #106: like setPartyFacing but for the preamble-selected event object (shared preamble
        // pCurrent: 0/0xFFFF = caller, else a 1-based index, script.c:626-638) - operand 0 also
        // gates the whole case: WORD 0 skips it entirely (script.c:745), and operand 2 is the
        // absolute frame (wCurrentFrameNum), not the direction*3+gesture form of 0x0015.
        new(0x0016, "setObjectFacing", [new("eventObjectId", OpcodeFieldKind.Word, 0), new("direction", OpcodeFieldKind.Word, 1), new("frame", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0017, "setExtraAttr", [new("equipPartWord", OpcodeFieldKind.Word, 0), new("field", OpcodeFieldKind.Word, 1), new("value", OpcodeFieldKind.SignedWord, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0018, "equipItem", [new("equipPartWord", OpcodeFieldKind.Word, 0), new("itemId", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0019, "incPlayerAttr", [new("field", OpcodeFieldKind.Word, 0), new("delta", OpcodeFieldKind.SignedWord, 1), new("roleOperand", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x001A, "setPlayerStat", [new("field", OpcodeFieldKind.Word, 0), new("value", OpcodeFieldKind.SignedWord, 1), new("roleOperand", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x001B, "changeHp", [new("scope", OpcodeFieldKind.Word, 0), new("amount", OpcodeFieldKind.SignedWord, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x001C, "changeMp", [new("scope", OpcodeFieldKind.Word, 0), new("amount", OpcodeFieldKind.SignedWord, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x001D, "changeHpMp", [new("scope", OpcodeFieldKind.Word, 0), new("amount", OpcodeFieldKind.SignedWord, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x001E, "changeCash", [new("amount", OpcodeFieldKind.SignedWord, 0), new("to", OpcodeFieldKind.Reference, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        new(0x001F, "addItem", [new("itemId", OpcodeFieldKind.Word, 0), new("count", OpcodeFieldKind.SignedWord, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #100: removeItem's operand 2 is WORD 0 = no fail target (script.c:986 removes even when
        // short), so it reuses the nullable-reference binding: null encodes WORD 0 and a reference
        // to address 0 is rejected; non-zero words are chunk-4 jump targets (script.c:1023).
        new(0x0020, "removeItem", [new("itemId", OpcodeFieldKind.Word, 0), new("count", OpcodeFieldKind.Word, 1), new("shortageTarget", OpcodeFieldKind.BattleTarget, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        // #103: operand 0 is the #101 all-enemies boolean scope (0 = g_Battle.rgEnemy[wEventObjectID],
        // non-zero = every non-empty enemy slot, script.c:1026-1049); operand 1 has no (SHORT) cast and
        // subtracts from wHealth as an unsigned WORD.
        new(0x0021, "inflictDamageToEnemy", [new("scope", OpcodeFieldKind.Word, 0), new("damage", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0022, "revivePlayer", [new("scope", OpcodeFieldKind.Word, 0), new("healthTenths", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #101: removeEquipment's operand 0 is a fourth player-addressing shape: a direct
        // 0-based PLAYERROLES role index operand (script.c:1108 assigns iPlayerRole with no -1,
        // unlike the 0x0019/0x001A roleOperand shape); operand 1 is 0 = remove every part,
        // 1..MAX_PLAYER_EQUIPMENTS = that part (1-based, script.c:1129 indexes operand1 - 1).
        new(0x0023, "removeEquipment", [new("playerRole", OpcodeFieldKind.Word, 0), new("partWord", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #108 re-verified: the preamble-selected event object (pCurrent, script.c:626-638:
        // 0/0xFFFF = caller, else a 1-based index) gets wAutoScript = operand 1 - an
        // EVENTOBJECT WORD entry into chunk 4 (global.h:102); the case is gated on
        // operand 0 != 0 (script.c:1141), so the caller form is 0xFFFF only and WORD 0
        // skips the case; real data entries reach 41099, past the signed range - entry
        // stays an unsigned WORD, and entry 0 disables the auto script.
        new(0x0024, "setAutoEntry", [new("eventObjectId", OpcodeFieldKind.Word, 0), new("entry", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #108 re-verified: the same preamble/gate shape as setAutoEntry (script.c:626-638,
        // 1151) writing pCurrent->wTriggerScript (global.h:101); real data entries reach
        // 43524 - entry stays an unsigned WORD, entry 0 clears the trigger script.
        new(0x0025, "setTriggerEntry", [new("eventObjectId", OpcodeFieldKind.Word, 0), new("entry", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #104: PAL_MakeScene + PAL_BuyMenu(operand0) - operand 0 is the store table row read as
        // lprgStore[wStoreNum].rgwItems (script.c:1163, uigame.c:1629), i.e. store.json (DATA chunk 0).
        new(0x0026, "showBuyMenu", [new("storeId", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #104: PAL_MakeScene + PAL_SellMenu() (script.c:1172) - no operands; the sold items come
        // from the player's inventory via PAL_ItemSelectMenu, not from a store row.
        new(0x0027, "showSellMenu", NoFields, OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0028, "applyPoisonToEnemy", [new("scope", OpcodeFieldKind.Word, 0), new("objectId", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0029, "applyPoisonToPlayer", [new("scope", OpcodeFieldKind.Word, 0), new("objectId", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x002A, "curePoisonByObjectIdForEnemy", [new("scope", OpcodeFieldKind.Word, 0), new("objectId", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x002B, "curePoisonByObjectIdForPlayer", [new("scope", OpcodeFieldKind.Word, 0), new("objectId", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x002C, "curePoisonsByLevel", [new("scope", OpcodeFieldKind.Word, 0), new("maxLevel", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x002D, "setStatusPlayer", [new("status", OpcodeFieldKind.Word, 0), new("rounds", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x002E, "setStatusEnemy", [new("status", OpcodeFieldKind.Word, 0), new("rounds", OpcodeFieldKind.Word, 1), new("to", OpcodeFieldKind.Reference, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        new(0x002F, "clearPlayerStatus", [new("status", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0030, "boostStat", [new("field", OpcodeFieldKind.Word, 0), new("percent", OpcodeFieldKind.SignedWord, 1), new("roleOperand", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #103: writes rgEquipmentEffect[kBodyPartExtra].rgwSpriteNumInBattle[wEventObjectID] - a
        // temporary battle-sprite override for the caller's player role, cleared after battle
        // (script.c:1429-1435, same extra register as 0x0030 boostStat).
        new(0x0031, "changeBattleSprite", [new("battleSprite", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #103: jumps to operand 0 when g_Battle.rgEnemy[wEventObjectID].e.wCollectValue == 0
        // (script.c:1437-1450); on success the value accumulates into gpGlobals->wCollectValue.
        new(0x0033, "collectEnemyForItems", [new("to", OpcodeFieldKind.Reference, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        // #103: jumps to operand 0 when nothing has been collected (wCollectValue == 0,
        // script.c:1452-1519); otherwise grants store[0].rgwItems[RandomLong(1, min(9, value)) - 1]
        // and shows the obtained-item dialog.
        new(0x0034, "transformCollectedEnemies", [new("to", OpcodeFieldKind.Reference, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        // #110: "Shake the screen" (script.c:1521-1534) - VIDEO_ShakeScreen(times, level)
        // (video.c:1029-1053) sets the g_wShakeTime countdown that decrements once per
        // VIDEO_UpdateScreen blit, alternating the image up/down by the level's scanline
        // offset until it reaches zero (video.c:571-615); operand 1 WORD 0 is normalized
        // to 4 at runtime only (script.c:1523-1526, encoding stays 0), and operand 0 WORD 0
        // skips the countdown and immediately redraws one clean frame (script.c:1531-1533).
        new(0x0035, "shakeScreen", [new("times", OpcodeFieldKind.Word, 0), new("level", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #110: "Set the current playing RNG animation" (script.c:1537-1541) -
        // gpGlobals->iCurPlayingRNG = operand0 (global.h:509): the rng.mkf segment
        // number that playRng 0x0037 and the ending then play (PAL_RNGPlay's iNumRNG,
        // rngplay.c:372-449, reads rng.mkf chunk iNumRNG); the case reads no other
        // operand (naming follows the musicId precedent for MKF segment numbers).
        new(0x0036, "setPlayingRng", [new("rngId", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #110: "Play RNG animation" (script.c:1544-1553) - plays the current RNG movie
        // (the setPlayingRng selection) from the operand-0 start frame; operand 1 is the
        // end frame whose WORD 0 stands for -1 = play until the movie ends, and operand 2
        // the speed whose WORD 0 is normalized to 16 at runtime only (script.c:1546-1550,
        // encodings stay 0); PAL_RNGPlay blocks until the last frame (rngplay.c:409-434)
        // like the blocking partyWalkTo callees - still a sequential successor.
        new(0x0037, "playRng", [new("startFrame", OpcodeFieldKind.Word, 0), new("endFrame", OpcodeFieldKind.Word, 1), new("speed", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #106: runs the current scene's OnTeleport trigger script (rgScene[wNumScene - 1].
        // wScriptOnTeleport via a nested PAL_RunTriggerScript, script.c:1558-1561); in battle or
        // with no teleport script it marks the script failed and jumps to operand 0
        // (script.c:1565-1569) - the same plain-reference jump shape as playerFlee.
        new(0x0038, "teleportParty", [new("to", OpcodeFieldKind.Reference, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        // #103: g_Battle.rgEnemy[wEventObjectID] loses operand 0 HP and the moving player gains the
        // same amount, capped at max HP (script.c:1573-1586); unsigned WORD arithmetic.
        new(0x0039, "drainHpFromEnemy", [new("amount", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #103: boss battles cannot flee - g_Battle.fIsBoss jumps to operand 0, otherwise
        // PAL_BattlePlayerEscape() runs (script.c:1588-1602).
        new(0x003A, "playerFlee", [new("to", OpcodeFieldKind.Reference, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        new(0x003B, "setDialog", [new("box", OpcodeFieldKind.ConstantString, ConstantString: "center"), new("color", OpcodeFieldKind.LowByte, 0), new("rngWord", OpcodeFieldKind.Word, 2)], OpcodeExecutor.TriggerSwitchOnly, OpcodeSuccessor.Sequential),
        new(0x003C, "setDialog", [new("box", OpcodeFieldKind.ConstantString, ConstantString: "upper"), new("portraitId", OpcodeFieldKind.Word, 0), new("color", OpcodeFieldKind.LowByte, 1), new("rngWord", OpcodeFieldKind.Word, 2)], OpcodeExecutor.TriggerSwitchOnly, OpcodeSuccessor.Sequential),
        new(0x003D, "setDialog", [new("box", OpcodeFieldKind.ConstantString, ConstantString: "lower"), new("portraitId", OpcodeFieldKind.Word, 0), new("color", OpcodeFieldKind.LowByte, 1), new("rngWord", OpcodeFieldKind.Word, 2)], OpcodeExecutor.TriggerSwitchOnly, OpcodeSuccessor.Sequential),
        new(0x003E, "setDialog", [new("box", OpcodeFieldKind.ConstantString, ConstantString: "centerWindow"), new("color", OpcodeFieldKind.LowByte, 0)], OpcodeExecutor.TriggerSwitchOnly, OpcodeSuccessor.Sequential),
        // #106: PAL_PartyRideEventObject(wEventObjectID, x, y, h, 2) (script.c:203) - the party
        // rides the caller event object to the absolute map tile position; speed 2 is the low
        // variant (script.c:1608-1610) and the ride loop blocks until arrival, so no stall.
        new(0x003F, "rideEventObjectSlow", [new("x", OpcodeFieldKind.Word, 0), new("y", OpcodeFieldKind.Word, 1), new("h", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #108 re-verified: the same preamble/gate shape as setAutoEntry (script.c:626-638,
        // 1617) writing pCurrent->wTriggerMode (global.h:104) - the kTrigger* constant
        // 0..8 (global.h:84-92; real data exactly 0..8; WORD 0 = kTriggerNone).
        new(0x0040, "setTriggerMode", [new("eventObjectId", OpcodeFieldKind.Word, 0), new("mode", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0041, "markFailed", NoFields, OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #103: PAL_BattleSimulateMagic(target, operand0, operand1) (fight.c:5301) - operand 0 is the
        // object.json magic-view object ID (wMagicObjectID, consumed via rgObject[id].magic) and
        // operand 1 the base damage. Operand 2 targets rgEnemy through the roleOperand shape read as
        // (SHORT): 0 or negative = caller slot wEventObjectID, positive = operand - 1 as a 1-based
        // enemy index (script.c:1634-1636); an apply-to-all magic overrides the target (fight.c:5321).
        new(0x0042, "simulateMagic", [new("magicId", OpcodeFieldKind.Word, 0), new("baseDamage", OpcodeFieldKind.Word, 1), new("enemyOperand", OpcodeFieldKind.SignedWord, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #112 re-verified (mapping holds): "Set background music" (script.c:1642-1648) -
        // gpGlobals->wNumMusic = operand0 and AUDIO_PlayMusic(operand0, operand1 != 1,
        // (operand1 == 3 && operand0 != 9) ? 3.0f : 0.0f) (audio.c:547): operand0 is the RIX
        // music.mkf segment number (the callee's iNumRIX) and operand1 the play-mode selector -
        // mode 1 plays once, anything else loops, and mode 3 additionally fades in over 3
        // seconds except for music 9; real data: mode 0 x148 / 1 x2 / 3 x5, musicId 0..87.
        new(0x0043, "setMusic", [new("musicId", OpcodeFieldKind.Word, 0), new("mode", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #106: the normal-speed ride variant - PAL_PartyRideEventObject(..., 4) (script.c:1653);
        // same x/y/h tile operands as 0x003F, whose callees scale x*32+h*16 / y*16+h*8 pixels.
        new(0x0044, "rideEventObject", [new("x", OpcodeFieldKind.Word, 0), new("y", OpcodeFieldKind.Word, 1), new("h", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #112: "Set battle music" (script.c:1658-1663) - gpGlobals->wNumBattleMusic =
        // operand0 (global.h:535): the RIX music.mkf segment the next battle plays looped
        // (battle.c:728 AUDIO_PlayMusic(wNumBattleMusic, TRUE, 0) - the musicId naming
        // precedent for MKF segments); the case reads no other operand (real data
        // battleMusicId 14..86, operands 1/2 all zero).
        new(0x0045, "setBattleMusic", [new("battleMusicId", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0046, "setPartyPos", [new("x", OpcodeFieldKind.Word, 0), new("y", OpcodeFieldKind.Word, 1), new("h", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #112: "Play sound effect" (script.c:1704-1709) - AUDIO_PlaySound(operand0)
        // (audio.c:518): plays the voc.mkf / sounds.mkf segment operand0 (the parameter doc
        // "number of the sound; the absolute value is used" - the case passes the unsigned
        // WORD with no (SHORT) cast); the case reads no other operand (real data soundId
        // 2..275, operands 1/2 all zero).
        new(0x0047, "playSound", [new("soundId", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #108 re-verified: the same preamble/gate shape as setAutoEntry (script.c:626-638,
        // 1715) writing pCurrent->sState - a SHORT field (global.h:103;
        // kObjStateHidden=0/Normal=1/Blocker=2, global.h:77-79); the signedWord binding
        // follows the 0x0094 stateMatchJump (SHORT) comparison convention and the
        // negative sState meaning hidden (scene.c:247); real data 0..3.
        new(0x0049, "setState", [new("eventObjectId", OpcodeFieldKind.Word, 0), new("state", OpcodeFieldKind.SignedWord, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x004A, "setBattlefield", [new("battlefieldId", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #108: "Nullify the event object for a short while" (script.c:1726-1731) - the
        // caller event object's vanish countdown becomes the fixed -15 with no operands:
        // while sVanishTime != 0 the object's trigger-zone checks and auto scripts are
        // skipped (play.c:87-94, play.c:178) and negative values count up toward zero
        // while the sprite stays drawn (scene.c:247 hides only sVanishTime > 0, unlike
        // hide 0x0052); real data's single hit has all three operands zero.
        new(0x004B, "nullifyObject", NoFields, OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #106: PAL_MonsterChasePlayer(wEventObjectID, speed, chaseRange, floating) (script.c:310) -
        // the caller monster chases the party; operand 0 is the sensitive range in tile units
        // (|dx| + 2|dy| < range * 32 * global wChaseRange, script.c:393; WORD 0 is normalized to 8
        // at runtime only), operand 1 the speed (0 -> 4 likewise), operand 2 a truthy WORD whose
        // non-zero form lets the monster float over obstacles.
        new(0x004C, "chasePlayer", [new("chaseRange", OpcodeFieldKind.Word, 0), new("speed", OpcodeFieldKind.Word, 1), new("floating", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x004D, "waitForAnyKey", NoFields, OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x004E, "requestReload", NoFields, OpcodeExecutor.Shared, OpcodeSuccessor.Stop),
        // #110 re-verified (mapping holds): "Fade the screen to red color (game over)"
        // (script.c:1768-1773) - PAL_FadeToRed() takes no parameters (palette.c:595-667:
        // blends every color of the current pat.mkf palette toward a red-dominant form
        // over up to 32 steps, skipping color 0x4F so dialog text stays readable); the
        // case reads no operand and the no-fields mapping stays.
        new(0x004F, "fadeToRed", NoFields, OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #110 review correction (was #78's "frames"): operand 0 is PAL_FadeOut's iDelay
        // (script.c:1775-1782, palette.c:123-190) - the palette fade-to-black pacing unit
        // (total duration = delay * 600 ms in fixed 10 ms steps), not a frame count; WORD 0
        // is normalized to 1 at runtime only (encoding stays 0 - real data's majority
        // form is 0). The case also sets gpGlobals->fNeedToFadeIn = TRUE so the next scene
        // draw fades back in.
        new(0x0050, "fadeOut", [new("delay", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #110: "screen fade in" (script.c:1784-1792) - PAL_FadeIn(wNumPalette,
        // fNightPalette, delay) fades the screen back into the current globals' palette
        // (global.h:532/533); the delay gate is ((SHORT)operand0 > 0) ? operand0 : 1, so
        // WORD 0 and the 0xFFFF form (SHORT -1) both fall back to the default 1 at
        // runtime while the encoding keeps the raw unsigned WORD (real data: 0 and 0xFFFF
        // only - both normalize to 1); the case also clears fNeedToFadeIn (global.h:513).
        new(0x0051, "fadeIn", [new("delay", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #108 re-verified: "hide the event object for a while, default 800 frames"
        // (script.c:1794-1800) - acts on the caller pEvtObj, not the preamble pCurrent
        // (so no eventObjectId field): sState *= -1 toggles the hidden sign
        // (scene.c:247 draws only sState > 0) and the vanish countdown sVanishTime
        // becomes operand 0 with WORD 0 normalized to 800 at runtime only (encoding
        // stays 0; real data hits the 0 form); the case reads no other operand.
        new(0x0052, "hide", [new("frames", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #110: "use the day palette" (script.c:1802-1806) - gpGlobals->fNightPalette =
        // FALSE (global.h:533 "TRUE if use the darker night palette"): PAL_GetPalette then
        // reads the first half of each pat.mkf palette chunk (palette.c:79-81's
        // fNight ? 256*3 : 0 offset); the case reads no operand.
        new(0x0053, "useDayPalette", NoFields, OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #110: "use the night palette" (script.c:1809-1813) - gpGlobals->fNightPalette =
        // TRUE: PAL_GetPalette reads the second half of each pat.mkf palette chunk (the
        // darker night colors, palette.c:79-81); the case reads no operand.
        new(0x0054, "useNightPalette", NoFields, OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #102: addMagic/removeMagic's operand 0 is the object.json magic-view object ID
        // (PAL_AddMagic/PAL_RemoveMagic store it into rgwMagic and compare by equality,
        // global.c:2084/2139); operand 1 is the boostStat roleOperand shape (script.c:1820-1828:
        // 0 = caller role wEventObjectID, non-zero = operand - 1 as a 1-based role index).
        new(0x0055, "addMagic", [new("magicId", OpcodeFieldKind.Word, 0), new("roleOperand", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0056, "removeMagic", [new("magicId", OpcodeFieldKind.Word, 0), new("roleOperand", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #102: operand 1 multiplies the caller-role player's current MP into the magic table's
        // wBaseDamage, then zeroes that MP; WORD 0 is normalized to 8 at runtime only
        // (script.c:1852) and still encodes as 0.
        new(0x0057, "setMagicBaseDamageByMp", [new("magicId", OpcodeFieldKind.Word, 0), new("mpMultiplier", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0058, "itemShortageJump", [new("itemId", OpcodeFieldKind.Word, 0), new("count", OpcodeFieldKind.SignedWord, 1), new("to", OpcodeFieldKind.Reference, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        // #112: "Change to the specified scene" (script.c:1870-1885) - when operand0 is
        // 1..MAX_SCENES (palcommon.h:69 = 300) and differs from wNumScene, the case sets
        // gpGlobals->wNumScene = operand0 and arms the next-frame scene load
        // (PAL_SetLoadFlags(kLoadScene), fEnteringScene = TRUE, wLayer = 0); operand0 is
        // the 1-based scene.json row (rgScene[sceneId - 1], the #80/#82 sceneId reference
        // direction); an out-of-range or same-scene WORD is a runtime no-op and encodes
        // unchanged (real data sceneId 2..294, operands 1/2 all zero).
        new(0x0059, "changeScene", [new("sceneId", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x005A, "halveHp", NoFields, OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #103: g_Battle.rgEnemy[wEventObjectID] loses min(wHealth / 2 + 1, operand 0) HP - operand 0
        // caps the halving damage (unsigned WORD arithmetic, script.c:1895-1904).
        new(0x005B, "halveEnemyHp", [new("maxDamage", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #108: "Hide for a while" (script.c:1907-1912) - writes the battle global
        // g_Battle.iHidingTime = -(INT)operand0 (battle.h:202): the negative value is a
        // pending form PAL_BattleCheckHidingEffect negates and scales by 20 outside
        // PAL_CLASSIC (fight.c:3529-3542); while the countdown is positive the party's
        // battle sprites are not drawn (battle.c:207) and enemy actions are skipped
        // (fight.c:1716); WORD 0 clears the hiding state (-(INT)0 = 0). The encoding
        // keeps the positive unit count - the negation is runtime-only; operands 1/2
        // are unused by the case and preserved; real data's single hit uses 3 units.
        new(0x005C, "setBattleHidingTime", [new("units", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x005D, "playerLacksPoisonJump", [new("objectId", OpcodeFieldKind.Word, 0), new("to", OpcodeFieldKind.Reference, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        new(0x005E, "enemyLacksPoisonJump", [new("objectId", OpcodeFieldKind.Word, 0), new("to", OpcodeFieldKind.Reference, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        new(0x005F, "killPlayer", NoFields, OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #103: g_Battle.rgEnemy[wEventObjectID].e.wHealth = 0 (script.c:1950-1954).
        new(0x0060, "killEnemy", NoFields, OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0061, "notPoisonedJump", [new("to", OpcodeFieldKind.Reference, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        // #106: wChasespeedChangeCycles = operand 0 and wChaseRange = 0 (script.c:1970-1972) -
        // pauses monster chasing (PAL_MonsterChasePlayer skips its whole chase block while the
        // global range is 0, script.c:341); play.c:235-237 counts the cycles down per game
        // update and resets the range to 1 when it reaches zero.
        new(0x0062, "pauseEnemyChasing", [new("cycles", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #106: the same countdown with wChaseRange = 3 (script.c:1978-1980) - triples every
        // monster's chase sensitivity (script.c:393) for the operand-0 cycles.
        new(0x0063, "speedUpEnemyChasing", [new("cycles", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0064, "enemyHpAboveJump", [new("percent", OpcodeFieldKind.Word, 0), new("to", OpcodeFieldKind.Reference, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        // #112: "Set the player's sprite" (script.c:1995-2005) - g.PlayerRoles.
        // rgwSpriteNum[operand0] = operand1: operand0 is a direct 0-based PLAYERROLES role
        // index (the removeEquipment 0x0023 playerRole shape, real data 0..3) and operand1
        // the MGO.MKF sprite number shown in normal scenes (global.h:303, decompressed as
        // an MGO chunk at res.c:325 - the setFollower spriteNum naming precedent, real data
        // 2..632); a truthy operand2 reloads the player sprites immediately when not in
        // battle (PAL_SetLoadFlags(kLoadPlayerSprite) + PAL_LoadResources(), real data
        // 0 x41 / 0xFFFF x83 - the truthy WORD form).
        new(0x0065, "setPlayerSprite", [new("playerRole", OpcodeFieldKind.Word, 0), new("spriteNum", OpcodeFieldKind.Word, 1), new("reload", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #103: PAL_BattleSimulateMagic((SHORT)wEventObjectID, operand0, operand1 * 5 +
        // moving-player attackStrength * RandomLong(0, 3)) - operand 0 is the thrown weapon's
        // object.json magic-view object ID (real data: 344/360, inside the magic evidence range),
        // operand 1 counts 5-HP damage units (script.c:2007-2014).
        new(0x0066, "throwWeapon", [new("magicId", OpcodeFieldKind.Word, 0), new("damageUnits", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #104: unlike simulateMagic (0x0042) this issues no cast call - it writes the caller enemy
        // slot's e.wMagic/e.wMagicRate (script.c:2020-2022); operand 0 is the object.json magic-view
        // object ID consumed at fight.c:4671 (0xFFFF = dual magic, 0 = no magic), operand 1 is the
        // cast rate with WORD 0 normalized to 10 at runtime only (encoding stays 0).
        new(0x0067, "enemyUseMagic", [new("magicId", OpcodeFieldKind.Word, 0), new("magicRate", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0068, "enemyTurnJump", [new("to", OpcodeFieldKind.Reference, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        // #104: PAL_BattleEnemyEscape() (script.c:2039, battle.c:1376-1435) - animates every
        // remaining enemy off-screen and terminates the battle (result 0); no operands.
        new(0x0069, "enemyEscape", NoFields, OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #104: PAL_BattleStealFromEnemy(wEventObjectID, operand0) (script.c:2046, fight.c:5193) -
        // the target is the caller enemy slot; operand 0 is the steal rate, checked as
        // RandomLong(0, 10) <= rate with rate 0 always succeeding (fight.c:5254).
        new(0x006A, "stealFromEnemy", [new("stealRate", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #104: (SHORT) cast into g_Battle.iBlow (script.c:2053) - a signed wind offset applied to
        // every enemy during magic animations (fight.c:2681: positive blows right/down, negative
        // left/up), reset at battle boundaries (fight.c:3608/4576).
        new(0x006B, "blowAwayEnemies", [new("strength", OpcodeFieldKind.SignedWord, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #106: moves the preamble-selected NPC (pCurrent/wCurEventObjectID, script.c:626-638:
        // 0/0xFFFF = caller, else a 1-based index) by the signed pixel offsets - the batch's only
        // (SHORT) casts (script.c:2060-2061) - then animates one walk step
        // (PAL_NPCWalkOneStep(wCurEventObjectID, 0), script.c:2062).
        new(0x006C, "walkNpcStep", [new("eventObjectId", OpcodeFieldKind.Word, 0), new("xOffset", OpcodeFieldKind.SignedWord, 1), new("yOffset", OpcodeFieldKind.SignedWord, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #99 review correction: script.c:3295 reads the delay from operand 1 ((0 -> 60) ms
        // normalization at runtime) and the gesture flag from operand 2; operand 0 is unused.
        new(0x0005, "redraw", [new("delayUnits", OpcodeFieldKind.Word, 1), new("updateGestures", OpcodeFieldKind.Word, 2)], OpcodeExecutor.TriggerSwitchOnly, OpcodeSuccessor.Sequential),
        // #112 re-verified (mapping holds): "Set the enter script and teleport script for a
        // scene" (script.c:2065-2089) - rgScene[operand0 - 1].wScriptOnEnter/wScriptOnTeleport
        // take operand1/2 when non-zero, a zero operand keeps that script, and both zero
        // clear both (the case's explicit rule); operand0 gates the whole case (WORD 0 skips)
        // and is the 1-based scene.json row (the #80/#82 sceneId direction); onEnter/
        // onTeleport are cross-structure chunk-4 entry WORDs like setAutoEntry.entry - real
        // data onEnter reaches 36203 past the signed range, keeping the unsigned binding.
        new(0x006D, "setSceneScripts", [new("sceneId", OpcodeFieldKind.Word, 0), new("onEnter", OpcodeFieldKind.Word, 1), new("onTeleport", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #107: "Move the player to the specified position in one step" - rgTrail is shifted,
        // the viewport jumps by the signed pixel offsets (script.c:2103-2105) and the party
        // layer global becomes operand2 * 8 (script.c:2107; drawn at rgParty[i].y + wLayer + 10,
        // scene.c:225); gestures update only when an offset is non-zero (script.c:2109-2112).
        new(0x006E, "moveParty", [new("xOffset", OpcodeFieldKind.SignedWord, 0), new("yOffset", OpcodeFieldKind.SignedWord, 1), new("layerUnits", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #109: "Sync the state of current event object with another event object"
        // (script.c:2115-2122) - if the preamble-selected object's sState (pCurrent,
        // script.c:626-638: 0/0xFFFF = caller, else a 1-based index) equals (SHORT)
        // operand 1, the caller's sState (pEvtObj) becomes operand 1 too; unlike the
        // 0x0024 setState family there is no operand0 != 0 gate - WORD 0 selects the
        // caller as pCurrent and the check degrades to a self-compare. sState is a SHORT
        // field (global.h:103) like setState.state, so the binding is signed. Real data:
        // 11 hits - eventObjectId 63..139 (all 1-based), state 0 x10 / 1 x1; operand 2
        // is unused by the case and preserved (one hit carries 1).
        new(0x006F, "syncState", [new("eventObjectId", OpcodeFieldKind.Word, 0), new("state", OpcodeFieldKind.SignedWord, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #107: PAL_PartyWalkTo(x, y, h, 2) (script.c:2129) - the whole party walks to the
        // absolute map tile position at the constant low speed 2; the callee blocks until
        // arrival (script.c:133-197), so the successor stays sequential.
        new(0x0070, "partyWalkToSlow", [new("x", OpcodeFieldKind.Word, 0), new("y", OpcodeFieldKind.Word, 1), new("h", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #110: "Wave the screen" (script.c:2132-2138) - gpGlobals->wScreenWave =
        // operand0 (global.h:379 "level of screen waving"; the wave amplitude scales as
        // level/256 per column, scene.c:415, and the effect stops once the level reaches
        // 0 or >= 256, scene.c:389-397 - real data's top hit 255 sits just below the
        // cutoff) and gpGlobals->sWaveProgression = (SHORT)operand1 (global.h:539, the
        // signed per-update level increment wScreenWave += sWaveProgression, scene.c:389;
        // real data carries -4 (0xFFFC), the signedWord evidence).
        new(0x0071, "waveScreen", [new("level", OpcodeFieldKind.Word, 0), new("progression", OpcodeFieldKind.SignedWord, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #110 review correction (was #78's "mode"): operand 0 is VIDEO_FadeScreen's
        // wSpeed (script.c:2140-2146, video.c:1129-1290) - the pacing of the
        // backup-screen-to-scene cross-fade ("the larger value, the slower"), not a mode
        // selector: VIDEO_BackupScreen(gpScreen) + PAL_MakeScene() rebuild the scene
        // first, then the callee blends the two buffers over 12 passes of (speed + 1) * 10
        // ms (video.c:1175-1179).
        new(0x0073, "fadeScene", [new("speed", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0074, "partyHpNotFullJump", [new("to", OpcodeFieldKind.Reference, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        // #112 re-verified (mapping holds): "Set the player party" (script.c:2164-2197) -
        // each non-zero operand i fills the next rgParty slot with wPlayerRole = operand - 1
        // (1-based PLAYERROLES role id, the roleId0/1/2 mapping), WORD 0 skips the slot, and
        // all-zero operands leave the Dream 2.11 hack party of role 0; the case then reloads
        // the player sprites, clears rgPoisonStatus and updates equipment. Real data:
        // roleId0 1..5 (1 x80 majority), roleId1 0..6, roleId2 0/3/5.
        new(0x0075, "setParty", [new("roleId0", OpcodeFieldKind.Word, 0), new("roleId1", OpcodeFieldKind.Word, 1), new("roleId2", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #111: "Show FBP picture" (script.c:2199-2213) - the WIN95 build only
        // clears the screen; otherwise PAL_EndingSetEffectSprite(0) clears the pending
        // ending effect sprite and PAL_ShowFBP(fbpId, fade) (ending.c:49-150)
        // decompresses fbp.mkf chunk fbpId and shows it: a non-zero fade runs the
        // 16-iteration 6-plane palette blend at (fade + 1) * 10 ms per pass while
        // WORD 0 blits directly (runtime-only distinction, encoding stays 0). Real
        // data's four hits all carry fbpId 0xFFFF - the decompress fails and the
        // buffer clears to black, i.e. the fade-to-black transition form.
        new(0x0076, "showFbp", [new("fbpId", OpcodeFieldKind.Word, 0), new("fade", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #112: "Stop current playing music" (script.c:2215-2222) - the stop form of
        // setMusic: AUDIO_PlayMusic(0, FALSE, (operand0 == 0) ? 2.0f : operand0 * 3) fades
        // the current music out (2 s for WORD 0, otherwise operand0 * 3 s) and plays
        // segment 0, then wNumMusic = 0; operands 1/2 are unused by the case and preserved
        // (real data fadeUnits 0 x20 / 1 x24).
        new(0x0077, "stopMusic", [new("fadeUnits", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #112 re-verified (mapping holds): the upstream "FIXME: ???" case (script.c:2224-2228)
        // - an empty body that breaks straight into the InterpretInstruction tail return,
        // i.e. a genuine no-operation; the case reads no operand and the reference offers no
        // stronger semantics to declare (real data: 35 hits, all three operands zero).
        new(0x0078, "noop", NoFields, OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0079, "roleInPartyJump", [new("roleNameWord", OpcodeFieldKind.Word, 0), new("to", OpcodeFieldKind.Reference, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        // #107: the normal-speed party walk - PAL_PartyWalkTo(x, y, h, 4) (script.c:2249);
        // same x/y/h tile operands as 0x0070 (callee scales x*32 + h*16 / y*16 + h*8 pixels).
        new(0x007A, "partyWalkTo", [new("x", OpcodeFieldKind.Word, 0), new("y", OpcodeFieldKind.Word, 1), new("h", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #107: the fastest party walk - PAL_PartyWalkTo(x, y, h, 8) (script.c:2256).
        new(0x007B, "partyWalkToFast", [new("x", OpcodeFieldKind.Word, 0), new("y", OpcodeFieldKind.Word, 1), new("h", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #107: the gated fast walk of the 0x0010/0x0011 family - the frame-parity gate
        // ((wEventObjectID & 1) ^ (dwFrameNum & 1), script.c:2263) runs PAL_NPCWalkTo(caller,
        // x, y, h, 4) (script.c:2265-2266) only every other frame; until arrival the entry
        // re-executes (wScriptEntry--, script.c:2268/2273) - a runtime stall, not a jump.
        new(0x007C, "walkToFast", [new("x", OpcodeFieldKind.Word, 0), new("y", OpcodeFieldKind.Word, 1), new("h", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #107: moves the preamble-selected event object (pCurrent, script.c:626-638: 0/0xFFFF
        // = caller, else a 1-based index) by the signed pixel offsets pCurrent->x += (SHORT)
        // operand1 / y += (SHORT) operand2 (script.c:2281-2282) - the walkNpcStep shape
        // without the walk-one-step animation call.
        new(0x007D, "moveObject", [new("eventObjectId", OpcodeFieldKind.Word, 0), new("xOffset", OpcodeFieldKind.SignedWord, 1), new("yOffset", OpcodeFieldKind.SignedWord, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #109: "Set the layer of event object" (script.c:2285-2290) - the
        // preamble-selected object's sLayer becomes (SHORT)operand1; sLayer is a SHORT
        // field (global.h:100) consumed as a vertical draw offset and draw-order value
        // in half-tile units (scene.c:302 y += sLayer * 8 + 9, scene.c:316
        // PAL_AddSpriteToDraw(..., sLayer * 8 + 2)). Real data: 10 hits - eventObjectId
        // 0xFFFF (caller, script.c:517C) and 1-based 59..4168; layer -10 (0xFFF6, the
        // signed evidence), 0, 3, 7, 10, 13, 33; operand 2 is unused and preserved.
        new(0x007E, "setObjectLayer", [new("eventObjectId", OpcodeFieldKind.Word, 0), new("layer", OpcodeFieldKind.SignedWord, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #107: "Move the viewport" with three forms (script.c:2296-2378): WORD 0/0 offsets
        // reset the viewport to the normal state (party recentered at 160,112; redrawn unless
        // frames is 0xFFFF, script.c:2296-2318); otherwise the signed pixel offsets scroll the
        // viewport once per frame for the frames units (WORD 0 still runs the single do-while
        // pass, script.c:2377); frames 0xFFFF switches the same operands to unsigned tile
        // coordinates and places the viewport absolutely at (x*32 - 160, y*16 - 112,
        // script.c:2333-2339). The signed scroll form is the field semantics.
        new(0x007F, "moveViewport", [new("xOffset", OpcodeFieldKind.SignedWord, 0), new("yOffset", OpcodeFieldKind.SignedWord, 1), new("frames", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #111: "Toggle day/night palette" (script.c:2381-2389) - flips the
        // fNightPalette global (the 0x0053/0x0054 pair's flag) and fades the video
        // palette to it over 32 blend steps (PAL_PaletteFade, palette.c:381-446);
        // the truthy WORD operand inverts the callee's fUpdateScene: WORD 0
        // re-renders the scene every step (FRAME_TIME = 100 ms pacing), a non-zero
        // form fades the palette only (FRAME_TIME/4 = 25 ms pacing).
        new(0x0080, "toggleDayNightPalette", [new("paletteOnly", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0081, "notFacingObjectJump", [new("eventObjectId", OpcodeFieldKind.Word, 0), new("zoneUnits", OpcodeFieldKind.Word, 1), new("to", OpcodeFieldKind.Reference, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        // #107: the fastest walk of the 0x0010/0x0011 family without the frame-parity gate -
        // PAL_NPCWalkTo(caller, x, y, h, 8) (script.c:2441-2442); until arrival the entry
        // re-executes (wScriptEntry--, script.c:2444) - a runtime stall, not a jump.
        new(0x0082, "walkToFastest", [new("x", OpcodeFieldKind.Word, 0), new("y", OpcodeFieldKind.Word, 1), new("h", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0083, "objectOutOfZoneJump", [new("eventObjectId", OpcodeFieldKind.Word, 0), new("zoneUnits", OpcodeFieldKind.Word, 1), new("to", OpcodeFieldKind.Reference, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        // #109: "Place the item which player used as an event object to the scene"
        // (script.c:2473-2509) - operand 0 is a 1-based global event-object id that must
        // fall inside the current scene's event-object window (rgScene[wNumScene-1].
        // wEventObjectIndex < operand0 <= rgScene[wNumScene].wEventObjectIndex,
        // script.c:2475-2481); the object is placed one step in front of the party
        // (party map position + 16/-16 px x and 8/-8 px y by facing, script.c:2483-2491)
        // unless PAL_CheckObstacle blocks (script.c:2493-2500), then its sState becomes
        // (SHORT)operand1; both failure paths jump to operand 2 with the script marked
        // failed - the same plain-reference jump shape as 0x0038/0x0083. Real data:
        // 2 hits - eventObjectId 798/3607, state 2, target 39656.
        new(0x0084, "placeUsedItem", [new("eventObjectId", OpcodeFieldKind.Word, 0), new("state", OpcodeFieldKind.SignedWord, 1), new("to", OpcodeFieldKind.Reference, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        new(0x0085, "delay", [new("delayUnits", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0086, "itemNotEquippedJump", [new("itemId", OpcodeFieldKind.Word, 0), new("count", OpcodeFieldKind.Word, 1), new("to", OpcodeFieldKind.Reference, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        // #109: "Animate the event object" (script.c:2542-2546) -
        // PAL_NPCWalkOneStep(wCurEventObjectID, 0) at speed 0 moves nothing (scene.c:887-888
        // multiply the step by the speed) and only advances the selected object's gesture
        // frame (wCurrentFrameNum++ wrapping at nSpriteFrames, with 3-frame sprites
        // wrapping at 4, scene.c:891-900); the callee returns early for id 0 or past
        // nEventObject (scene.c:873-876). Real data: 189 hits with all three operands
        // zero - the caller form only.
        new(0x0087, "animateObject", [new("eventObjectId", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #102: unlike 0x0057 (by MP), this has no role/multiplier operand: the base damage is
        // min(cash, 5000) * 2 / 5 written into the magic table entry named by operand 0, and
        // the same amount is deducted from cash (script.c:2549-2557).
        new(0x0088, "setMagicBaseDamageByCash", [new("magicId", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #104: g_Battle.BattleResult = operand0 (script.c:2563) - the raw BATTLERESULT value kept
        // as an unsigned WORD like status rows (battle.h:33-40: 0 terminated, 1 lost, 3 won,
        // 0xFFFF fleed, 1000..1002 ongoing phases).
        new(0x0089, "setBattleResult", [new("result", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #104: gpGlobals->fAutoBattle = TRUE (script.c:2570) - enables auto-battle for the next
        // battle; no operands.
        new(0x008A, "enableAutoBattle", NoFields, OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #111: "change the current palette" (script.c:2573-2582) -
        // gpGlobals->wNumPalette = operand0: the pat.mkf palette segment number
        // every fade callee consumes through PAL_GetPalette (palette.c:40-60); the
        // screen palette is applied immediately (PAL_SetPalette day half,
        // palette.c:93) only while no fade-in is pending - fNeedToFadeIn defers the
        // apply to that fade.
        new(0x008B, "setPalette", [new("paletteId", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #111: "Fade from/to color" (script.c:2584-2592) - PAL_ColorFade(delay,
        // (BYTE)color, from) (palette.c:462-560) blends all 256 palette entries
        // toward (from WORD 0) or from (any non-zero form - real data uses 0xFFFF)
        // the solid palette color over 64 steps of delay * 10 ms (WORD 0
        // normalizes to 10 ms at runtime only); the color takes the operand's low
        // byte through the (BYTE) cast; the case also clears fNeedToFadeIn.
        new(0x008C, "colorFade", [new("color", OpcodeFieldKind.LowByte, 0), new("delay", OpcodeFieldKind.Word, 1), new("fromColor", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x008D, "increaseLevel", [new("levels", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x008E, "restoreScreen", NoFields, OpcodeExecutor.TriggerSwitchOnly, OpcodeSuccessor.Sequential),
        new(0x008F, "halveCash", NoFields, OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0091, "notFirstOfKindJump", [new("to", OpcodeFieldKind.Reference, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        // #104: operand 0 is a 1-based battle player slot (rgPlayer[operand0 - 1], script.c:2645-2648);
        // WORD 0 picks no single player and only the party-wide color-shift loop runs - a fifth
        // player-addressing shape (0 = none, unlike roleOperand's 0 = caller).
        new(0x0092, "showMagicCastAnimation", [new("playerOperand", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #111: "Fade the screen. Update scene in the process." (script.c:2666-2674)
        // - PAL_SceneFade(wNumPalette, fNightPalette, (SHORT)operand0)
        // (palette.c:262-375): a positive step fades in (the palette scale ramps
        // 0 -> 63, a larger magnitude = fewer, faster 100 ms iterations with a
        // scene re-render each pass) and a negative step fades out (63 -> 0); WORD
        // 0 normalizes to 1 at runtime only; a negative step also arms
        // fNeedToFadeIn so the next scene fades back in (script.c:2672). The
        // (SHORT) cast with real hits at -4/-2/-1 (0xFFFC/0xFFFE/0xFFFF) is the
        // signedWord evidence.
        new(0x0093, "sceneFade", [new("step", OpcodeFieldKind.SignedWord, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0094, "stateMatchJump", [new("eventObjectId", OpcodeFieldKind.Word, 0), new("state", OpcodeFieldKind.SignedWord, 1), new("to", OpcodeFieldKind.Reference, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        new(0x0095, "sceneMatchJump", [new("sceneId", OpcodeFieldKind.Word, 0), new("to", OpcodeFieldKind.Reference, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        // #111: "Show the ending animation" (script.c:2695-2700) - non-WIN95 only:
        // PAL_EndingAnimation() (ending.c:282-553) plays the whole ending sequence
        // (RNG movies, FBP stills, the credits and AVI/CD fallbacks) and returns;
        // the case reads no operand (real data has no chunk-4 hit - the ending's
        // own FBP/RNG sequence is hardcoded in ending.c, not scripted).
        new(0x0096, "showEndingAnimation", NoFields, OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #107: the fastest ride - PAL_PartyRideEventObject(wEventObjectID, x, y, h, 8)
        // (script.c:2707-2708); same x/y/h tile operands as the 0x003F/0x0044 speeds.
        new(0x0097, "rideEventObjectFast", [new("x", OpcodeFieldKind.Word, 0), new("y", OpcodeFieldKind.Word, 1), new("h", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #107: party followers - operands 0/1 select follower slots 1/2 (> 0 form) and are
        // stored into rgParty[...].wPlayerRole with no -1 (script.c:2715-2733); a follower's
        // wPlayerRole is consumed directly as an MGO.MKF sprite number (res.c:337), unlike the
        // 1-based role operands of setParty (real data 82/83); WORD 0 skips the slot and
        // all-zero operands clear every follower (nFollower = 0, script.c:2737-2739).
        new(0x0098, "setFollower", [new("spriteNum0", OpcodeFieldKind.Word, 0), new("spriteNum1", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #112: "Change the map for the specified scene" (script.c:2742-2756) - the
        // scene.json row named by operand0 (1-based, rgScene[sceneId - 1]) takes operand1 as
        // wMapNum (global.h:117, the map number PAL_LoadMap consumes at res.c:252); the
        // 0xFFFF sentinel form retargets the current scene (rgScene[wNumScene - 1]) and
        // reloads it immediately (kLoadScene + PAL_LoadResources()) - real data uses only
        // the sentinel form (x2, mapNum 164/165); the sentinel stays the raw unsigned WORD
        // like 0x007F frames.
        new(0x0099, "changeSceneMap", [new("sceneId", OpcodeFieldKind.Word, 0), new("mapNum", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #111: "Fade to the current scene" (script.c:2768-2775, FIXME'd upstream)
        // - the 0x0073 fadeScene shape at the fixed speed 2: VIDEO_BackupScreen +
        // PAL_MakeScene rebuild the scene, then VIDEO_FadeScreen(2) cross-fades
        // from the backup buffer; the case reads no operand (both real hits carry
        // ignored operand words 2/0xFFFF/0, preserved as source WORDs).
        new(0x009B, "fadeToScene", NoFields, OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #104: operand 0 counts the new copies (WORD 0 normalized to 1 at runtime only,
        // script.c:2805-2811: total parts = copies + 1, each keeping (hp + copies) / (copies + 1) HP);
        // operand 1 is the nullable no-division reference - WORD 0 means no jump (script.c:2792-2800),
        // so it reuses the nullable-reference binding like removeItem.shortageTarget.
        new(0x009C, "enemyDivide", [new("copies", OpcodeFieldKind.Word, 0), new("failTarget", OpcodeFieldKind.BattleTarget, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        // #104: operand 0 is the summoned monster's object.json enemy-view object ID (0/0xFFFF = the
        // summoner's own object, script.c:2884-2890, consumed via rgObject[w].enemy.wEnemyID at 2919);
        // operand 1 is read as (SHORT) with non-positive values normalized to 1 (script.c:2885);
        // operand 2 is the nullable cannot-summon reference (WORD 0 = no jump, script.c:2905-2907).
        new(0x009E, "enemySummon", [new("objectId", OpcodeFieldKind.Word, 0), new("count", OpcodeFieldKind.SignedWord, 1), new("failTarget", OpcodeFieldKind.BattleTarget, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        // #104: the caller enemy becomes operand 0's object.json enemy-view object, keeping its
        // current HP (script.c:2967-2972); blocked silently by hiding/sleep/paralysis/confusion
        // with no jump; operands 1/2 are unused by the reference case.
        new(0x009F, "enemyTransform", [new("objectId", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x00A0, "quitGame", NoFields, OpcodeExecutor.Shared, OpcodeSuccessor.Stop),
        // #107: snaps every party member and trail slot onto the leader's position (members
        // stacked at y - 1, script.c:3004-3014, then PAL_UpdatePartyGestures(FALSE)) - no
        // operands (real data all-zero).
        new(0x00A1, "gatherParty", NoFields, OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x00A2, "randomBranch", [new("count", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.RandomBranch),
        // #112: "Play CD music. Use the RIX music for fallback." (script.c:3025-3038) -
        // wNumMusic = operand1 and, with a CD available, AUDIO_PlayCDTrack reads operand0
        // through the (SHORT) cast: -1 (0xFFFF) maps to -2, audio.c:576's "do NOTHING"
        // (leave the CD untouched), any other value is the CD track number; a failed CD
        // play falls back to AUDIO_PlayMusic(operand1, TRUE, 0) - the looped RIX music.mkf
        // segment, also the no-CD path. track follows the simulateMagic 0x0042 enemyOperand /
        // enemySummon 0x009E count signedWord precedent ((SHORT) cast + negative-domain
        // sentinel; real data 4 hits all positive tracks 2/4/8/9 with musicId 14/22/27/67);
        // operand2 is unused by the case and preserved.
        new(0x00A3, "playCdMusic", [new("track", OpcodeFieldKind.SignedWord, 0), new("musicId", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #111: "Scroll FBP to the screen" (script.c:3040-3054) - non-WIN95 only:
        // fbpId 68 first shows chunk 69 directly (the upstream HACKHACK for the
        // ending picture), then PAL_ScrollFBP(fbpId, speed, TRUE)
        // (ending.c:153-278) scrolls the fbp.mkf picture onto the screen line by
        // line downward; speed WORD 0 normalizes to 1 at runtime only
        // (ending.c:211-214); operand 1 is unused and preserved.
        new(0x00A4, "scrollFbp", [new("fbpId", OpcodeFieldKind.Word, 0), new("speed", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #111: "Show FBP picture with sprite effects" (script.c:3057-3070) -
        // non-WIN95 only: unless the effect sprite word is the 0xFFFF
        // keep-sentinel, PAL_EndingSetEffectSprite(operand1) selects the MGO.MKF
        // sprite number that PAL_ShowFBP animates over the picture (frame = ticks /
        // 150 % frames, ending.c:79-129); then PAL_ShowFBP(fbpId, fade) as 0x0076.
        new(0x00A5, "showFbpWithEffectSprite", [new("fbpId", OpcodeFieldKind.Word, 0), new("effectSprite", OpcodeFieldKind.Word, 1), new("fade", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x00A6, "backupScreen", NoFields, OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #112: the one opcode handled by PAL_RunAutoScript's own switch with no shared or
        // trigger case (script.c:3641-3643) - a bare wScriptEntry++ with no upstream comment
        // and no operand read: the standard one-instruction-per-frame advance, i.e. a
        // do-nothing entry; a trigger execution would fall into PAL_InterpretInstruction's
        // default TerminateOnError (script.c:3077), so the executor is auto-only - the first
        // AutoSwitchOnly descriptor. The verb reuses advanceEntry (0x0008's trigger-switch-
        // only mirror - both bodies advance one entry); real data: 295 hits, all operands zero.
        new(0x00A7, "advanceEntry", NoFields, OpcodeExecutor.AutoSwitchOnly, OpcodeSuccessor.Sequential),
        // #109 re-verified against script.c:2607-2613: the #78 conclusion holds - operand 0
        // indexes gpGlobals->g.rgObject directly with no -1, i.e. a 0-based object-union id
        // (object.json), not an event object; operand 2 selects the union's script slots
        // rgwData[2 + slot] (slot 0..2 -> the per-view script WORDs at union indices 2..4:
        // item onUse/onEquip/onThrow, magic onSuccess/onUse, enemy turnStart/battleEnd/ready,
        // poison playerScript/reserved/enemyScript, player friendDeath/dying); operand 1 is
        // the written WORD - a cross-structure chunk-4 script entry like setAutoEntry.entry,
        // WORD 0 clearing the slot (real data: 3 hits all clearing - objectId 435/454/485,
        // value 0, slotIndex 0).
        new(0x0090, "setObjectScript", [new("objectId", OpcodeFieldKind.Word, 0), new("value", OpcodeFieldKind.Word, 1), new("slotIndex", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        // #109 re-verified against script.c:2758-2766: the #78 conclusion holds - the closed
        // 1-based event-object range operand0..operand1 writes lprgEventObject[i - 1].sState
        // for every i; operand 2 has no (SHORT) cast but assigns into the SHORT sState storage
        // (global.h:103), keeping setState.state's signedWord binding; an empty range
        // (operand0 > operand1, real data has one to=0 hit) writes nothing. Real data: 81
        // hits - from 26..4747, to 27..4749, state 0 x48 / 1 x6 / 2 x27.
        new(0x009A, "setStateRange", [new("fromEventObjectId", OpcodeFieldKind.Word, 0), new("toEventObjectId", OpcodeFieldKind.Word, 1), new("state", OpcodeFieldKind.SignedWord, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0xFFFF, "showDialog", [new("messageId", OpcodeFieldKind.Word, 0), new("descriptionLineWord", OpcodeFieldKind.Word, 1)], OpcodeExecutor.BothSwitchesDiffer, OpcodeSuccessor.MessageSpan),
    ];

    /// All registered descriptors keyed by opcode.
    public static IReadOnlyDictionary<int, OpcodeDescriptor> ByOpcode { get; } =
        Registry.ToDictionary(descriptor => descriptor.Opcode);

    /// All registered verb names, including "raw".
    public static IReadOnlyCollection<string> OpNames { get; } =
        Registry.Select(descriptor => descriptor.Op).Append("raw").ToHashSet();

    public static bool IsRegistered(int opcode) => ByOpcode.ContainsKey(opcode);

    public static bool IsIdentifier(string value) => Identifier.IsMatch(value);

    public static string LabelAt(int address) => "L" + address.ToString("X4");

    /// Decodes one instruction into a command object: label, named fields (or "raw"),
    /// WORD-order next, and the source triple.
    public static JsonObject DecodeCommand(int index, int count, ushort opcode, ushort a, ushort b, ushort c)
    {
        ushort[] operands = [a, b, c];
        var result = new JsonObject { ["label"] = LabelAt(index) };
        if (ByOpcode.TryGetValue(opcode, out OpcodeDescriptor? descriptor))
        {
            result["op"] = descriptor.Op;
            foreach (OpcodeField field in descriptor.Fields)
            {
                switch (field.Kind)
                {
                    case OpcodeFieldKind.Word:
                        result[field.Name] = (int)operands[field.Operand];
                        break;
                    case OpcodeFieldKind.SignedWord:
                        result[field.Name] = (int)(short)operands[field.Operand];
                        break;
                    case OpcodeFieldKind.LowByte:
                        result[field.Name] = (int)(operands[field.Operand] & 255);
                        break;
                    case OpcodeFieldKind.Reference:
                        result[field.Name] = LabelAt(operands[field.Operand]);
                        break;
                    case OpcodeFieldKind.BattleTarget:
                        if (descriptor.DecodeValidatesReferenceRange && operands[field.Operand] != 0 && operands[field.Operand] >= count)
                            throw new InvalidDataException($"SSS.MKF: {descriptor.Op} @{index:X4} result target is outside chunk 4.");
                        result[field.Name] = operands[field.Operand] == 0 ? null : LabelAt(operands[field.Operand]);
                        break;
                    case OpcodeFieldKind.BossFlag:
                        result[field.Name] = operands[field.Operand] == 0;
                        break;
                    case OpcodeFieldKind.OpcodeOffset:
                        result[field.Name] = opcode - field.Base;
                        break;
                    case OpcodeFieldKind.ConstantString:
                        result[field.Name] = field.ConstantString;
                        break;
                }
            }
        }
        else result["op"] = "raw";
        result["next"] = (index + 1 < count || count == 65536) ? LabelAt((index + 1) & 0xffff) : null;
        result["source"] = new JsonObject
        {
            ["index"] = index, ["opcode"] = (int)opcode,
            ["operands"] = new JsonArray((int)a, (int)b, (int)c),
        };
        return result;
    }

    /// Strictly validates one command against the registry: identifiers, op vocabulary,
    /// source triple, descriptor mapping, allowed keys, and per-field declarations.
    public static void ValidateCommand(JsonObject command, string fileId, int count)
    {
        string label = StrictJson.String(command, "label");
        if (!IsIdentifier(label)) throw new InvalidDataException($"{fileId}.json: invalid command label.");
        string op = StrictJson.String(command, "op");
        if (!OpNames.Contains(op)) throw new InvalidDataException($"{fileId}.json: unsupported op {op}.");
        string? next = StrictJson.NullableString(command, "next");
        if (next is not null) ValidateReferenceSyntax(next, $"{label}.next");
        var source = StrictJson.Required(command, "source");
        StrictJson.ValidateKeys(source, "source", "index", "opcode", "operands");
        int index = StrictJson.Int(source, "index"), opcodeValue = StrictJson.Int(source, "opcode");
        if (index < 0 || index >= count || opcodeValue is < 0 or > 65535) throw new InvalidDataException($"{label}: source index/opcode out of range.");
        var operands = StrictJson.RequiredArray(source, "operands");
        if (operands.Count != 3) throw new InvalidDataException($"{label}: source.operands must contain exactly three WORD values.");
        foreach (var operand in operands) StrictJson.Word(operand);
        if (op == "raw")
        {
            if (ByOpcode.TryGetValue(opcodeValue, out OpcodeDescriptor? registered) && !registered.AllowLegacyRaw)
                throw new InvalidDataException($"{label}: registered opcode must use its named mapping.");
            StrictJson.ValidateKeys(command, label, "label", "op", "next", "source");
            return;
        }
        if (!ByOpcode.TryGetValue(opcodeValue, out OpcodeDescriptor? descriptor) || descriptor.Op != op)
            throw new InvalidDataException($"{label}: invalid {op} mapping.");
        var allowed = new List<string> { "label", "op" };
        allowed.AddRange(descriptor.Fields.Select(field => field.Name));
        allowed.AddRange(["next", "source"]);
        StrictJson.ValidateKeys(command, label, allowed.ToArray());
        foreach (OpcodeField field in descriptor.Fields)
        {
            switch (field.Kind)
            {
                case OpcodeFieldKind.Reference:
                    ValidateReferenceSyntax(StrictJson.String(command, field.Name), $"{label}.{field.Name}");
                    break;
                case OpcodeFieldKind.BattleTarget:
                    string? target = StrictJson.NullableString(command, field.Name);
                    if (target is not null) ValidateReferenceSyntax(target, $"{label}.{field.Name}");
                    break;
                case OpcodeFieldKind.BossFlag:
                    string context = $"{fileId}.json#{label} @{index:X4}";
                    if (command[field.Name] is not JsonValue value || !value.TryGetValue<bool>(out bool flag))
                        throw new InvalidDataException($"{context}: {descriptor.Op}.{field.Name} must be a boolean.");
                    if (field.CoupledTarget is string coupled && flag != (StrictJson.NullableString(command, coupled) is null))
                        throw new InvalidDataException($"{context}: {descriptor.Op}.{field.Name} must equal {coupled} being null.");
                    break;
            }
        }
    }

    /// Re-encodes one command into its 8 little-endian bytes. Mapped operands are
    /// re-encoded from the semantic fields; unmapped operands and the low-byte fields'
    /// high bits keep their source WORD values.
    public static void EncodeCommand(JsonObject command, string fileId, IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> labelsByFile, Span<byte> destination)
    {
        string label = StrictJson.String(command, "label");
        var source = StrictJson.Required(command, "source");
        ushort opcode = checked((ushort)StrictJson.Int(source, "opcode"));
        var operands = StrictJson.RequiredArray(source, "operands");
        ushort a = StrictJson.Word(operands[0]), b = StrictJson.Word(operands[1]), c = StrictJson.Word(operands[2]);
        string op = StrictJson.String(command, "op");
        if (op != "raw")
        {
            if (!ByOpcode.TryGetValue(opcode, out OpcodeDescriptor? descriptor) || descriptor.Op != op)
                throw new InvalidDataException($"{label}: op does not match registered source opcode.");
            ushort[] words = [a, b, c];
            var bossFlags = new List<(OpcodeField Field, bool Flag)>();
            foreach (OpcodeField field in descriptor.Fields)
            {
                switch (field.Kind)
                {
                    case OpcodeFieldKind.Word:
                        words[field.Operand] = checked((ushort)StrictJson.Int(command, field.Name));
                        break;
                    case OpcodeFieldKind.SignedWord:
                        words[field.Operand] = unchecked((ushort)CheckedSigned(command, field.Name));
                        break;
                    case OpcodeFieldKind.LowByte:
                        int low = StrictJson.Int(command, field.Name);
                        if (low is < 0 or > 255) throw new InvalidDataException($"{descriptor.Op}.{field.Name} must be 0..255.");
                        words[field.Operand] = (ushort)((words[field.Operand] & 0xff00) | low);
                        break;
                    case OpcodeFieldKind.Reference:
                        words[field.Operand] = EncodeReference(command, field.Name, fileId, labelsByFile);
                        break;
                    case OpcodeFieldKind.BattleTarget:
                        words[field.Operand] = EncodeBattleTarget(command, field.Name, fileId, labelsByFile);
                        break;
                    case OpcodeFieldKind.BossFlag:
                        if (command[field.Name] is not JsonValue value || !value.TryGetValue<bool>(out bool flag))
                            throw new InvalidDataException($"{label}: {descriptor.Op}.{field.Name} must be a boolean.");
                        bossFlags.Add((field, flag));
                        break;
                    case OpcodeFieldKind.OpcodeOffset:
                        if (opcode != field.Base + StrictJson.Int(command, field.Name))
                            throw new InvalidDataException($"{label}: {descriptor.Op} {field.Name} does not match its opcode.");
                        break;
                    case OpcodeFieldKind.ConstantString:
                        if (StrictJson.String(command, field.Name) != field.ConstantString)
                            throw new InvalidDataException($"{label}: invalid {descriptor.Op} mapping.");
                        break;
                }
            }
            foreach (var (field, flag) in bossFlags)
                if (field.CoupledTarget is string coupled &&
                    descriptor.Fields.FirstOrDefault(other => other.Name == coupled) is { Operand: int coupledOperand } &&
                    flag != (words[coupledOperand] == 0))
                    throw new InvalidDataException($"{label}: {descriptor.Op}.{field.Name} must equal {coupled} being null.");
            a = words[0]; b = words[1]; c = words[2];
        }
        Write(destination, opcode); Write(destination[2..], a); Write(destination[4..], b); Write(destination[6..], c);
    }

    public static int ResolveReference(string reference, string currentFile, IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> labelsByFile, string context)
    {
        int separator = reference.IndexOf('#');
        string fileId = currentFile;
        string label = reference;
        if (separator >= 0)
        {
            if (separator == 0 || separator == reference.Length - 1 || reference.IndexOf('#', separator + 1) >= 0)
                throw new InvalidDataException($"{context}: malformed file#label reference '{reference}'.");
            fileId = reference[..separator];
            label = reference[(separator + 1)..];
        }
        if (!IsIdentifier(fileId) || !IsIdentifier(label))
            throw new InvalidDataException($"{context}: malformed reference '{reference}'.");
        if (!labelsByFile.TryGetValue(fileId, out var labels) || !labels.TryGetValue(label, out int address))
            throw new InvalidDataException($"{context}: dangling reference '{reference}'.");
        return address;
    }

    public static void ValidateReferenceSyntax(string reference, string context)
    {
        int separator = reference.IndexOf('#');
        if (separator < 0)
        {
            if (!IsIdentifier(reference)) throw new InvalidDataException($"{context}: malformed reference '{reference}'.");
            return;
        }
        if (separator == 0 || separator == reference.Length - 1 || reference.IndexOf('#', separator + 1) >= 0
            || !IsIdentifier(reference[..separator]) || !IsIdentifier(reference[(separator + 1)..]))
            throw new InvalidDataException($"{context}: malformed file#label reference '{reference}'.");
    }

    private static int CheckedSigned(JsonObject command, string field)
    {
        int value = StrictJson.Int(command, field);
        return value is >= -32768 and <= 32767 ? value : throw new InvalidDataException($"{StrictJson.String(command, "label")}.{field} must be a signed WORD.");
    }

    private static ushort EncodeReference(JsonObject command, string field, string fileId, IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> labelsByFile)
    {
        string reference = StrictJson.NullableString(command, field) ?? throw new InvalidDataException($"{StrictJson.String(command, "label")}.{field} must be a reference.");
        string context = $"{fileId}.json#{StrictJson.String(command, "label")} @{StrictJson.Int(StrictJson.Required(command, "source"), "index"):X4}.{field}";
        return checked((ushort)ResolveReference(reference, fileId, labelsByFile, context));
    }

    private static ushort EncodeBattleTarget(JsonObject command, string field, string fileId, IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> labelsByFile)
    {
        string? reference = StrictJson.NullableString(command, field);
        if (reference is null) return 0;
        string context = $"{fileId}.json#{StrictJson.String(command, "label")} @{StrictJson.Int(StrictJson.Required(command, "source"), "index"):X4}.{field}";
        int address = ResolveReference(reference, fileId, labelsByFile, context);
        if (address == 0) throw new InvalidDataException($"{context}: address zero must use null, not a reference.");
        return checked((ushort)address);
    }

    private static void Write(Span<byte> bytes, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
}
