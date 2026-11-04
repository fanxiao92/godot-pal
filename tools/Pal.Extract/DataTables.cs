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

    // object.json - SSS.MKF chunk 2 - is the OBJECT union and therefore not a flat
    // DataTableDescriptor: every record declares one union view (player/item by #81,
    // magic/enemy/poison by #82) decided from cross-archive evidence. Its variant
    // detection, evidence reader, decode, validation, and index entry live in
    // ObjectUnionTable and are registered alongside these descriptors by DataGameData.
}
