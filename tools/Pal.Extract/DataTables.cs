namespace Pal.Extract;

// Registered data-table structure descriptors (data-schema v1), one entry per M5 table
// ticket (#79-#91). Field names, order, and signedness follow the frozen per-field
// schema in docs/design/data-schema.md, which mirrors the C layout declared in
// reference/sdlpal/global.h: only SHORT members (s-prefixed) decode as SignedWord;
// plain WORD/USHORT members stay unsigned.
public static class DataTables
{
    // event-object.json - SSS.MKF chunk 0, EVENTOBJECT, 16 WORD (32 bytes) per record.
    // Record count = chunk byte length / 32; sVanishTime/sLayer/sState are the three
    // SHORT members (script.c writes sVanishTime = -15 and casts sLayer/sState).
    public static readonly DataTableDescriptor EventObject = new(
        "event-object", "SSS.MKF", 0,
        [
            new DataField("vanishTime", DataFieldType.SignedWord),
            new DataField("x", DataFieldType.Word),
            new DataField("y", DataFieldType.Word),
            new DataField("layer", DataFieldType.SignedWord),
            new DataField("triggerScript", DataFieldType.Word),
            new DataField("autoScript", DataFieldType.Word),
            new DataField("state", DataFieldType.SignedWord),
            new DataField("triggerMode", DataFieldType.Word),
            new DataField("spriteNum", DataFieldType.Word),
            new DataField("spriteFrames", DataFieldType.Word),
            new DataField("direction", DataFieldType.Word),
            new DataField("currentFrameNum", DataFieldType.Word),
            new DataField("scriptIdleFrame", DataFieldType.Word),
            new DataField("spritePtrOffset", DataFieldType.Word),
            new DataField("spriteFramesAuto", DataFieldType.Word),
            new DataField("scriptIdleFrameCountAuto", DataFieldType.Word),
        ]);

    // scene.json - SSS.MKF chunk 1, SCENE, 4 WORD (8 bytes) per record. Record count =
    // chunk byte length / 8; global.h declares all four members as plain WORD, so no
    // field decodes as SignedWord (unlike the SHORT members of EVENTOBJECT).
    public static readonly DataTableDescriptor Scene = new(
        "scene", "SSS.MKF", 1,
        [
            new DataField("mapNum", DataFieldType.Word),
            new DataField("scriptOnEnter", DataFieldType.Word),
            new DataField("scriptOnTeleport", DataFieldType.Word),
            new DataField("eventObjectIndex", DataFieldType.Word),
        ]);

    // store.json - DATA.MKF chunk 0, STORE, 9 WORD (18 bytes) per record. Record count =
    // chunk byte length / 18; global.h declares rgwItems as a plain WORD array (each
    // element is an object.json item-view index, 0 = empty slot per uigame.c:1644), so
    // unlike EVENTOBJECT nothing here decodes as SignedWord.
    public static readonly DataTableDescriptor Store = new(
        "store", "DATA.MKF", 0,
        [
            new DataField("items", DataFieldType.Word, [9]),
        ]);

    // enemy.json - DATA.MKF chunk 1, ENEMY, 35 WORD (70 bytes) per record. Record
    // count = chunk byte length / 70; global.h declares the five sound members as
    // SHORT (wAttackSound/wActionSound/wMagicSound/wDeathSound/wCallSound; fight.c
    // tests wMagicSound >= 0 and plays the others through AUDIO_PlaySound's signed
    // parameter), so exactly those five decode as SignedWord - the first DATA-side
    // table with signed members. magicId/attackEquivItemId/stealItemId are object.json
    // magic/item-view indices that double as the ObjectUnionTable evidence source
    // (0xFFFF wMagic = no magic per fight.c:4660).
    public static readonly DataTableDescriptor Enemy = new(
        "enemy", "DATA.MKF", 1,
        [
            new DataField("idleFrames", DataFieldType.Word),
            new DataField("magicFrames", DataFieldType.Word),
            new DataField("attackFrames", DataFieldType.Word),
            new DataField("idleAnimSpeed", DataFieldType.Word),
            new DataField("actWaitFrames", DataFieldType.Word),
            new DataField("yPosOffset", DataFieldType.Word),
            new DataField("attackSound", DataFieldType.SignedWord),
            new DataField("actionSound", DataFieldType.SignedWord),
            new DataField("magicSound", DataFieldType.SignedWord),
            new DataField("deathSound", DataFieldType.SignedWord),
            new DataField("callSound", DataFieldType.SignedWord),
            new DataField("health", DataFieldType.Word),
            new DataField("exp", DataFieldType.Word),
            new DataField("cash", DataFieldType.Word),
            new DataField("level", DataFieldType.Word),
            new DataField("magicId", DataFieldType.Word),
            new DataField("magicRate", DataFieldType.Word),
            new DataField("attackEquivItemId", DataFieldType.Word),
            new DataField("attackEquivItemRate", DataFieldType.Word),
            new DataField("stealItemId", DataFieldType.Word),
            new DataField("stealItemCount", DataFieldType.Word),
            new DataField("attackStrength", DataFieldType.Word),
            new DataField("magicStrength", DataFieldType.Word),
            new DataField("defense", DataFieldType.Word),
            new DataField("dexterity", DataFieldType.Word),
            new DataField("fleeRate", DataFieldType.Word),
            new DataField("poisonResistance", DataFieldType.Word),
            new DataField("elementalResistance", DataFieldType.Word, [5]),
            new DataField("physicalResistance", DataFieldType.Word),
            new DataField("dualMove", DataFieldType.Word),
            new DataField("collectValue", DataFieldType.Word),
        ]);

    // enemy-team.json - DATA.MKF chunk 2, ENEMYTEAM, 5 WORD (10 bytes) per record.
    // Record count = chunk byte length / 10; global.h declares rgwEnemy as a plain
    // WORD array of MAX_ENEMIES_IN_TEAM = 5 (palcommon.h:60) elements, so nothing
    // decodes as SignedWord. Each element is an object.json enemy-view index that
    // battle.c:1601-1615 loads through rgObject[w].enemy, where 0 = empty slot and
    // 0xFFFF = skip - the same 10-byte layout ObjectUnionTable reads as its
    // enemy-view evidence (DATA chunk 2 is deliberately read twice; see DataGameData).
    public static readonly DataTableDescriptor EnemyTeam = new(
        "enemy-team", "DATA.MKF", 2,
        [
            new DataField("enemies", DataFieldType.Word, [5]),
        ]);

    // player-roles.json - DATA.MKF chunk 3, PLAYERROLES, 450 WORD (900 bytes), one
    // single record. The #86/#87 ticket pair delivered this table over the same
    // single-record document (the field union frozen in data-schema.md's
    // player-roles section): #86 structured the attribute group (avatar/sprites/
    // name/attackAll/level/HP/MP and the strength/defense/dexterity/flee/
    // poison-resistance/elemental-resistance members plus the unknown/walk-frame
    // slots) and exported the #87 members as raw WORD-array placeholders; #87
    // then replaced those placeholders losslessly with the structured names below
    // - same layout dimensions, same strict validation, every WORD value
    // unchanged - so the whole 450-WORD record is structured with no placeholder
    // left. global.h declares every member as a plain WORD array (PLAYERS =
    // WORD[6]) - the PLAYERROLES sounds are WORD, unlike the ENEMY SHORT sounds -
    // so nothing here decodes as SignedWord. equipment holds item object indices
    // (0 = unequipped, itemmenu.c:363), magic and cooperativeMagic magic object
    // indices (0 = not learned / no cooperative magic, magicmenu.c:339-354 and
    // fight.c:3859-3861), coveredBy holds role indices (fight.c:784/839/4948), and
    // the seven sound rows feed AUDIO_PlaySound (fight.c:850/4816/5014 etc.).
    // rgwName (WORD 18..23) doubles as the object.json player-view evidence source
    // that ObjectUnionTable reads from this same chunk (fight.c:800-813/848-876);
    // rgwEquipment (WORD 66), rgwMagic (WORD 192), and rgwCooperativeMagic
    // (WORD 390) feed the item/magic evidence besides (DATA chunk 3 is
    // deliberately read twice; see DataGameData).
    public static readonly DataTableDescriptor PlayerRoles = new(
        "player-roles", "DATA.MKF", 3,
        [
            new DataField("avatar", DataFieldType.Word, [6]),
            new DataField("battleSprite", DataFieldType.Word, [6]),
            new DataField("spriteNum", DataFieldType.Word, [6]),
            new DataField("name", DataFieldType.Word, [6]),
            new DataField("attackAll", DataFieldType.Word, [6]),
            new DataField("unknown1", DataFieldType.Word, [6]),
            new DataField("level", DataFieldType.Word, [6]),
            new DataField("maxHp", DataFieldType.Word, [6]),
            new DataField("maxMp", DataFieldType.Word, [6]),
            new DataField("hp", DataFieldType.Word, [6]),
            new DataField("mp", DataFieldType.Word, [6]),
            new DataField("equipment", DataFieldType.Word, [6, 6]),
            new DataField("attackStrength", DataFieldType.Word, [6]),
            new DataField("magicStrength", DataFieldType.Word, [6]),
            new DataField("defense", DataFieldType.Word, [6]),
            new DataField("dexterity", DataFieldType.Word, [6]),
            new DataField("fleeRate", DataFieldType.Word, [6]),
            new DataField("poisonResistance", DataFieldType.Word, [6]),
            new DataField("elementalResistance", DataFieldType.Word, [5, 6]),
            new DataField("unknown2", DataFieldType.Word, [6]),
            new DataField("unknown3", DataFieldType.Word, [6]),
            new DataField("unknown4", DataFieldType.Word, [6]),
            new DataField("coveredBy", DataFieldType.Word, [6]),
            new DataField("magic", DataFieldType.Word, [32, 6]),
            new DataField("walkFrames", DataFieldType.Word, [6]),
            new DataField("cooperativeMagic", DataFieldType.Word, [6]),
            new DataField("unknown5", DataFieldType.Word, [6]),
            new DataField("unknown6", DataFieldType.Word, [6]),
            new DataField("deathSound", DataFieldType.Word, [6]),
            new DataField("attackSound", DataFieldType.Word, [6]),
            new DataField("weaponSound", DataFieldType.Word, [6]),
            new DataField("criticalSound", DataFieldType.Word, [6]),
            new DataField("magicSound", DataFieldType.Word, [6]),
            new DataField("coverSound", DataFieldType.Word, [6]),
            new DataField("dyingSound", DataFieldType.Word, [6]),
        ]);

    // magic.json - DATA.MKF chunk 4, MAGIC, 16 WORD (32 bytes) per record. Record
    // count = chunk byte length / 32; global.h declares wSpeed and wSound as SHORT
    // (fight.c:2513/2730/2955/3171 compute the effect frame delay as (wSpeed + 5) * 10
    // and fight.c:2501/2671/2713/2929/3114 pass wSound to AUDIO_PlaySound's signed INT
    // parameter, audio.c:517), so exactly those two decode as SignedWord. rgSpecific is
    // #78's frozen dual view, expressed as the descriptor's one dispatched slot at
    // word 4: wType (word 1) == 9 (kMagicTypeSummon) exports the WORD wSummonEffect
    // view read only by PAL_BattleShowPlayerSummonMagicAnim (fight.c:3135), which is
    // invoked exclusively under wType == kMagicTypeSummon checks (fight.c:3865-3868
    // cooperative and 4255-4257 offensive magic); every other type exports the SHORT
    // sLayerOffset view the non-summon effect paths read through explicit (SHORT)
    // casts (fight.c:2518 PAL_BattleShowPlayerDefMagicAnim, fight.c:2735
    // PAL_BattleShowPlayerOffMagicAnim, fight.c:2960 PAL_BattleShowEnemyMagicAnim).
    // Each record therefore exports exactly one of summonEffect/layerOffset - the two
    // views of one physical WORD, never both - and this table is not the object.json
    // magic view (that is SSS.MKF chunk 2's wMagicNumber; #82 verified the direction:
    // the object magic view points INTO this table, not the other way round).
    public static readonly DataTableDescriptor Magic = new(
        "magic", "DATA.MKF", 4,
        [
            new DataField("effect", DataFieldType.Word),
            new DataField("type", DataFieldType.Word),
            new DataField("xOffset", DataFieldType.Word),
            new DataField("yOffset", DataFieldType.Word),
            new DataField("speed", DataFieldType.SignedWord),
            new DataField("keepEffect", DataFieldType.Word),
            new DataField("fireDelay", DataFieldType.Word),
            new DataField("effectTimes", DataFieldType.Word),
            new DataField("shake", DataFieldType.Word),
            new DataField("wave", DataFieldType.Word),
            new DataField("unknown", DataFieldType.Word),
            new DataField("costMp", DataFieldType.Word),
            new DataField("baseDamage", DataFieldType.Word),
            new DataField("elemental", DataFieldType.Word),
            new DataField("sound", DataFieldType.SignedWord),
        ],
        new DispatchedWordSlot("rgSpecific", "type", 4,
        [
            new DispatchedWordOption("summonEffect", DataFieldType.Word, 9),
            new DispatchedWordOption("layerOffset", DataFieldType.SignedWord),
        ]));

    // battle-field.json - DATA.MKF chunk 5, BATTLEFIELD, 6 WORD (12 bytes) per record.
    // Record count = chunk byte length / 12; global.h declares wScreenWave as a plain
    // WORD (battle.c:1563 copies it into gpGlobals->wScreenWave) and rgsMagicEffect as
    // a SHORT array of NUM_MAGIC_ELEMENTAL = 5 (palcommon.h:57) elements, so exactly
    // the five-element array decodes as SignedWord and nothing else does. The
    // signedness matters at the single read point fight.c:244 (PAL_CalcMagicDamage):
    // sDamage *= 10 + rgsMagicEffect[wElem - 1] - a value below -10 flips the
    // multiplier's sign, so decoding the array as WORD would change real damage
    // results for negative multipliers.
    public static readonly DataTableDescriptor BattleField = new(
        "battle-field", "DATA.MKF", 5,
        [
            new DataField("screenWave", DataFieldType.Word),
            new DataField("magicEffect", DataFieldType.SignedWord, [5]),
        ]);

    // object.json - SSS.MKF chunk 2 - is the OBJECT union and therefore not a flat
    // DataTableDescriptor: every record declares one union view (player/item by #81,
    // magic/enemy/poison by #82) decided from cross-archive evidence. Its variant
    // detection, evidence reader, decode, validation, and index entry live in
    // ObjectUnionTable and are registered alongside these descriptors by DataGameData.
}
