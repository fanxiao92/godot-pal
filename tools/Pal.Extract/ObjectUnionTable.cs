using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Pal.Extract;

/// Record-size variant of the OBJECT union (reference/sdlpal/global.h tagOBJECT /
/// tagOBJECT_DOS): the WIN95 layout carries 7 WORDs (the wScriptDesc slot), the DOS
/// layout 6 WORDs. The variant follows the chunk-2 byte length exactly like the
/// divisibility confirmation of the reference PAL_IsWINVersion: divisible by 14 and
/// not by 12 => WIN95, the reverse => DOS.
public enum ObjectUnionVariant
{
    /// 14 bytes per record (7 WORD); the item and magic views carry wScriptDesc.
    Win95 = 7,
    /// 12 bytes per record (6 WORD); DOS layout has no wScriptDesc slot.
    Dos = 6,
}

/// View evidence for the OBJECT union table: the object indices the reference reads
/// through exactly one union view. The union itself carries no view tag, so
/// extraction assigns a view only from these independent reference read sites
/// (data-schema v1, object.json view determination); every record without evidence
/// is exported as "undetermined" raw words instead of a guessed view. The poison
/// view has no extraction-time source: every reference .poison read is indexed by
/// the runtime poison state (global.c:1440/1515/1608/1667, script.c:1213/1248,
/// uibattle.c:132-137, uigame.c:1249-1251), which scripts fill at run time, so its
/// evidence set stays empty until script-reference evidence (the opcode registry
/// tickets) can name poison operands.
public sealed record ObjectViewEvidence(
    IReadOnlySet<int> PlayerObjectIndices,
    IReadOnlySet<int> ItemObjectIndices,
    IReadOnlySet<int> MagicObjectIndices,
    IReadOnlySet<int> EnemyObjectIndices,
    IReadOnlySet<int> PoisonObjectIndices);

// object.json - SSS.MKF chunk 2, the OBJECT union (global.h tagOBJECT), 14 bytes
// (7 WORD, WIN95) or 12 bytes (6 WORD, DOS) per record. #81 registers the player and
// item views; #82 completes the union with the magic/enemy/poison views and their
// evidence sources. Unlike the flat tables this decoder is view-aware: each record
// declares "view" and contains only that view's fields, so one physical WORD group
// is never presented under several views at once.
public static class ObjectUnionTable
{
    public const string FileId = "object";
    public const string Archive = "SSS.MKF";
    public const int Chunk = 2;
    public const int Win95RecordBytes = 14;
    public const int DosRecordBytes = 12;

    // View-evidence sources in DATA.MKF: the chunk numbers and the WORD offsets of the
    // members that the reference reads as object.json views (layouts frozen in this
    // schema's store/enemy/enemy-team/player-roles tables; those tables are extracted
    // by their own tickets, the object table only reads them for evidence).
    public const int StoreEvidenceChunk = 0;
    public const int EnemyEvidenceChunk = 1;
    public const int EnemyTeamEvidenceChunk = 2;
    public const int PlayerRolesEvidenceChunk = 3;

    private const int StoreRecordBytes = 18;
    private const int StoreItemWords = 9;
    private const int EnemyRecordBytes = 70;
    private const int EnemyWordsPerRecord = 35;
    private const int EnemyAttackEquivItemWord = 17;
    private const int EnemyStealItemWord = 19;
    private const int EnemyMagicWord = 15;
    private const int EnemyNoMagic = 0xFFFF;   // fight.c:4660: wMagic == 0xFFFF means the enemy casts nothing
    private const int EnemyTeamRecordBytes = 10;
    private const int EnemyTeamWordsPerRecord = 5;
    private const int EnemyTeamSkip = 0xFFFF;  // battle.c:1605: rgwEnemy == 0xFFFF means an empty team slot
    private const int PlayerRolesRecordBytes = 900;
    private const int PlayerRolesNameWord = 18;
    private const int PlayerRolesEquipmentWord = 66;
    private const int MaxPlayerRoles = 6;
    private const int MaxPlayerEquipments = 6;
    private const int MaxPlayerMagics = 32;
    private const int PlayerRolesMagicWord = 192;
    private const int PlayerRolesCooperativeMagicWord = 390;

    /// Decides the record-size variant from the chunk-2 byte length: divisible by 14
    /// and not by 12 => WIN95, divisible by 12 and not by 14 => DOS. A length that is
    /// divisible by both (a multiple of 84) cannot be decided by divisibility (the
    /// reference would score compression signatures of ABC/MAP/F/FBP/FIRE/MGO, which
    /// are outside this export's input), and a length divisible by neither is not a
    /// table; both fail with the source located.
    public static ObjectUnionVariant DetectVariant(int byteLength)
    {
        bool byFourteen = byteLength > 0 && byteLength % Win95RecordBytes == 0;
        bool byTwelve = byteLength > 0 && byteLength % DosRecordBytes == 0;
        if (byFourteen && !byTwelve) return ObjectUnionVariant.Win95;
        if (byTwelve && !byFourteen) return ObjectUnionVariant.Dos;
        if (byFourteen && byTwelve)
            throw new InvalidDataException($"{Archive}: chunk {Chunk}: byte length {byteLength} is divisible by both {Win95RecordBytes} (WIN95) and {DosRecordBytes} (DOS), so the variant cannot be determined from the divisibility rule alone.");
        throw new InvalidDataException($"{Archive}: chunk {Chunk}: byte length {byteLength} is not a positive multiple of the {Win95RecordBytes}- or {DosRecordBytes}-byte record size.");
    }

    /// Collects the view evidence from the raw DATA.MKF evidence chunks.
    /// Player view: PLAYERROLES rgwName[6] (DATA chunk 3) - fight.c:800-813/848-876
    /// reads rgObject[PlayerRoles.rgwName[w]].player (the only .player read sites), so
    /// all six name words are player evidence, 0 included (it is a name word index
    /// that the engine would still read through the player view).
    /// Item view: the nonzero item references the reference reads through .item -
    /// STORE rgwItems (DATA chunk 0; itemmenu.c/play.c inventory flow, 0 = empty
    /// slot), ENEMY wAttackEquivItem/wStealItem (DATA chunk 1; fight.c:5135-5145 and
    /// 5282-5284, 0 = no such item), and PLAYERROLES rgwEquipment (DATA chunk 3;
    /// itemmenu.c:363/global.c:1364-1365, 0 = unequipped).
    /// Magic view: the nonzero magic references the reference reads through .magic -
    /// PLAYERROLES rgwMagic[32][6] (DATA chunk 3; magicmenu.c:339-354 and
    /// uibattle.c:753-759 skip 0 = not learned), PLAYERROLES rgwCooperativeMagic[6]
    /// (DATA chunk 3; fight.c:3859-3861 and uibattle.c:1120-1125 via
    /// PAL_GetPlayerCooperativeMagic, 0 = no cooperative magic), and ENEMY wMagic
    /// (DATA chunk 1; fight.c:4580/4651/4660-4671/4761-4769, where wMagic != 0 guards
    /// the cast and 0xFFFF means no magic). References that only reach rgwMagic
    /// through a run-time write are not evidence: LEVELUPMAGIC wMagic is stored by
    /// PAL_AddMagic (battle.c:1302-1309) and script-fed magics by script.c:1829, both
    /// after extraction time.
    /// Enemy view: ENEMYTEAM rgwEnemy[5] (DATA chunk 2) - battle.c:1601-1615 loads each
    /// battle member as rgObject[w].enemy, skipping 0 (empty slot) and 0xFFFF. The
    /// script-reloaded enemies (script.c:2919/2969) come from run-time operands.
    /// Poison view: no extraction-time source (see ObjectViewEvidence); the set is
    /// returned empty and poison-region records stay undetermined.
    public static ObjectViewEvidence ReadViewEvidence(byte[] storeChunk, byte[] enemyChunk, byte[] enemyTeamChunk, byte[] playerRolesChunk)
    {
        if (storeChunk.Length == 0 || storeChunk.Length % StoreRecordBytes != 0)
            throw new InvalidDataException($"DATA.MKF: chunk {StoreEvidenceChunk}: byte length {storeChunk.Length} is not a positive multiple of the {StoreRecordBytes}-byte STORE record size required for object.json view evidence.");
        if (enemyChunk.Length == 0 || enemyChunk.Length % EnemyRecordBytes != 0)
            throw new InvalidDataException($"DATA.MKF: chunk {EnemyEvidenceChunk}: byte length {enemyChunk.Length} is not a positive multiple of the {EnemyRecordBytes}-byte ENEMY record size required for object.json view evidence.");
        if (enemyTeamChunk.Length == 0 || enemyTeamChunk.Length % EnemyTeamRecordBytes != 0)
            throw new InvalidDataException($"DATA.MKF: chunk {EnemyTeamEvidenceChunk}: byte length {enemyTeamChunk.Length} is not a positive multiple of the {EnemyTeamRecordBytes}-byte ENEMYTEAM record size required for object.json view evidence.");
        if (playerRolesChunk.Length == 0 || playerRolesChunk.Length % PlayerRolesRecordBytes != 0)
            throw new InvalidDataException($"DATA.MKF: chunk {PlayerRolesEvidenceChunk}: byte length {playerRolesChunk.Length} is not a positive multiple of the {PlayerRolesRecordBytes}-byte PLAYERROLES record size required for object.json view evidence.");

        var players = new HashSet<int>();
        var items = new HashSet<int>();
        var magic = new HashSet<int>();
        var enemies = new HashSet<int>();
        for (int role = 0; role < MaxPlayerRoles; role++)
            players.Add(WordAt(playerRolesChunk, PlayerRolesNameWord + role));
        for (int record = 0; record < storeChunk.Length / StoreRecordBytes; record++)
            for (int slot = 0; slot < StoreItemWords; slot++)
            {
                int item = WordAt(storeChunk, record * StoreItemWords + slot);
                if (item != 0) items.Add(item);
            }
        for (int record = 0; record < enemyChunk.Length / EnemyRecordBytes; record++)
        {
            int attackEquivItem = WordAt(enemyChunk, record * EnemyWordsPerRecord + EnemyAttackEquivItemWord);
            int stealItem = WordAt(enemyChunk, record * EnemyWordsPerRecord + EnemyStealItemWord);
            if (attackEquivItem != 0) items.Add(attackEquivItem);
            if (stealItem != 0) items.Add(stealItem);
            int enemyMagic = WordAt(enemyChunk, record * EnemyWordsPerRecord + EnemyMagicWord);
            if (enemyMagic != 0 && enemyMagic != EnemyNoMagic) magic.Add(enemyMagic);
        }
        for (int slot = 0; slot < MaxPlayerRoles * MaxPlayerEquipments; slot++)
        {
            int item = WordAt(playerRolesChunk, PlayerRolesEquipmentWord + slot);
            if (item != 0) items.Add(item);
        }
        for (int slot = 0; slot < MaxPlayerRoles * MaxPlayerMagics; slot++)
        {
            int learned = WordAt(playerRolesChunk, PlayerRolesMagicWord + slot);
            if (learned != 0) magic.Add(learned);
        }
        for (int role = 0; role < MaxPlayerRoles; role++)
        {
            int cooperative = WordAt(playerRolesChunk, PlayerRolesCooperativeMagicWord + role);
            if (cooperative != 0) magic.Add(cooperative);
        }
        for (int record = 0; record < enemyTeamChunk.Length / EnemyTeamRecordBytes; record++)
            for (int slot = 0; slot < EnemyTeamWordsPerRecord; slot++)
            {
                int enemy = WordAt(enemyTeamChunk, record * EnemyTeamWordsPerRecord + slot);
                if (enemy != 0 && enemy != EnemyTeamSkip) enemies.Add(enemy);
            }
        RejectConflicts(("player", players, $"DATA.MKF chunk {PlayerRolesEvidenceChunk} name"),
            ("item", items, $"DATA.MKF chunks {StoreEvidenceChunk}/{EnemyEvidenceChunk}/{PlayerRolesEvidenceChunk} item references"),
            ("magic", magic, $"DATA.MKF chunks {EnemyEvidenceChunk}/{PlayerRolesEvidenceChunk} magic references"),
            ("enemy", enemies, $"DATA.MKF chunk {EnemyTeamEvidenceChunk} enemy-team references"));
        return new ObjectViewEvidence(players, items, magic, enemies, new HashSet<int>());
    }

    /// Fails with the source located when one object index is referenced as two
    /// different views: a physical record must never be expressed under two views.
    private static void RejectConflicts(params (string View, HashSet<int> Indices, string Source)[] views)
    {
        for (int first = 0; first < views.Length; first++)
            for (int second = first + 1; second < views.Length; second++)
                foreach (int index in views[first].Indices.Intersect(views[second].Indices).Order())
                    throw new InvalidDataException($"{FileId}.json: view evidence conflict: object {index} is referenced both as {views[first].View} ({views[first].Source}) and as {views[second].View} ({views[second].Source}); one record must not be expressed under two views.");
    }

    /// Decodes SSS.MKF chunk 2 into the strict object.json document. Each record
    /// declares exactly one view ("player" / "item" / "magic" / "enemy" / "poison"
    /// from evidence, otherwise "undetermined" with the raw little-endian WORDs
    /// preserved) and carries only that view's fields for the detected variant.
    public static JsonObject DecodeTable(byte[] chunk, ObjectViewEvidence evidence)
    {
        ObjectUnionVariant variant = DetectVariant(chunk.Length);
        int wordsPerRecord = (int)variant;
        int recordCount = chunk.Length / (wordsPerRecord * 2);
        foreach (int index in evidence.PlayerObjectIndices.Where(index => index >= recordCount).Order())
            throw new InvalidDataException($"{FileId}.json: player view evidence object {index} is outside the {recordCount}-record table ({Archive} chunk {Chunk}).");
        foreach (int index in evidence.ItemObjectIndices.Where(index => index >= recordCount).Order())
            throw new InvalidDataException($"{FileId}.json: item view evidence object {index} is outside the {recordCount}-record table ({Archive} chunk {Chunk}).");
        foreach (int index in evidence.MagicObjectIndices.Where(index => index >= recordCount).Order())
            throw new InvalidDataException($"{FileId}.json: magic view evidence object {index} is outside the {recordCount}-record table ({Archive} chunk {Chunk}).");
        foreach (int index in evidence.EnemyObjectIndices.Where(index => index >= recordCount).Order())
            throw new InvalidDataException($"{FileId}.json: enemy view evidence object {index} is outside the {recordCount}-record table ({Archive} chunk {Chunk}).");
        foreach (int index in evidence.PoisonObjectIndices.Where(index => index >= recordCount).Order())
            throw new InvalidDataException($"{FileId}.json: poison view evidence object {index} is outside the {recordCount}-record table ({Archive} chunk {Chunk}).");
        var records = new JsonArray();
        for (int index = 0; index < recordCount; index++)
        {
            var words = new int[wordsPerRecord];
            for (int word = 0; word < wordsPerRecord; word++) words[word] = WordAt(chunk, index * wordsPerRecord + word);
            if (evidence.PlayerObjectIndices.Contains(index)) records.Add(PlayerRecord(words));
            else if (evidence.ItemObjectIndices.Contains(index)) records.Add(ItemRecord(variant, words));
            else if (evidence.MagicObjectIndices.Contains(index)) records.Add(MagicRecord(variant, words));
            else if (evidence.EnemyObjectIndices.Contains(index)) records.Add(EnemyRecord(words));
            else if (evidence.PoisonObjectIndices.Contains(index)) records.Add(PoisonRecord(words));
            else records.Add(UndeterminedRecord(words));
        }
        return new JsonObject
        {
            ["formatVersion"] = 1,
            ["profile"] = "faithful",
            ["fileId"] = FileId,
            ["records"] = records,
        };
    }

    /// Builds the index.json registration entry: source archive and chunk, record
    /// count derived from the detected variant, byte length, and the chunk SHA-256.
    public static JsonObject DescribeIndexEntry(byte[] chunk)
    {
        int recordBytes = (int)DetectVariant(chunk.Length) * 2;
        return new JsonObject
        {
            ["file"] = FileId + ".json",
            ["archive"] = Archive,
            ["chunk"] = Chunk,
            ["recordCount"] = chunk.Length / recordBytes,
            ["byteLength"] = chunk.Length,
            ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(chunk)),
        };
    }

    /// Strictly validates an object.json document for the variant implied by the
    /// chunk byte length: version/profile/fileId, a declared view per record
    /// (player/item/magic/enemy/poison/undetermined), exactly that view's fields -
    /// never two views' fields in one record - correct array lengths, and WORD ranges.
    public static void ValidateDocument(int chunkByteLength, JsonObject document)
    {
        ObjectUnionVariant variant = DetectVariant(chunkByteLength);
        int wordsPerRecord = (int)variant;
        StrictJson.ValidateKeys(document, FileId, "formatVersion", "profile", "fileId", "records");
        if (StrictJson.Int(document, "formatVersion") != 1 || StrictJson.String(document, "profile") != "faithful")
            throw new InvalidDataException($"{FileId}.json: unsupported formatVersion or profile.");
        if (StrictJson.String(document, "fileId") != FileId)
            throw new InvalidDataException($"{FileId}.json: fileId does not match the table.");
        var records = StrictJson.RequiredArray(document, "records");
        for (int index = 0; index < records.Count; index++)
        {
            var record = records[index]?.AsObject() ?? throw new InvalidDataException($"{FileId}.json: record {index} must be an object.");
            string context = $"{FileId}.json record {index}";
            string view;
            try { view = record["view"]?.GetValue<string>() ?? ""; }
            catch (InvalidOperationException) { throw new InvalidDataException($"{context}.view must be a string."); }
            switch (view)
            {
                case "player":
                    StrictJson.ValidateKeys(record, context, "view", "reserved", "scriptOnFriendDeath", "scriptOnDying", "padding");
                    ValidateWordArray(record, context, "reserved", 2);
                    ValidateWord(record, context, "scriptOnFriendDeath");
                    ValidateWord(record, context, "scriptOnDying");
                    ValidateWordArray(record, context, "padding", wordsPerRecord - 4);
                    break;
                case "item":
                    if (variant == ObjectUnionVariant.Win95)
                        StrictJson.ValidateKeys(record, context, "view", "bitmap", "price", "scriptOnUse", "scriptOnEquip", "scriptOnThrow", "scriptDesc", "flags");
                    else
                        StrictJson.ValidateKeys(record, context, "view", "bitmap", "price", "scriptOnUse", "scriptOnEquip", "scriptOnThrow", "flags");
                    string[] itemFields = variant == ObjectUnionVariant.Win95
                        ? ["bitmap", "price", "scriptOnUse", "scriptOnEquip", "scriptOnThrow", "scriptDesc", "flags"]
                        : ["bitmap", "price", "scriptOnUse", "scriptOnEquip", "scriptOnThrow", "flags"];
                    foreach (string field in itemFields) ValidateWord(record, context, field);
                    break;
                case "magic":
                    if (variant == ObjectUnionVariant.Win95)
                        StrictJson.ValidateKeys(record, context, "view", "magicId", "reserved1", "scriptOnSuccess", "scriptOnUse", "scriptDesc", "reserved2", "flags");
                    else
                        StrictJson.ValidateKeys(record, context, "view", "magicId", "reserved1", "scriptOnSuccess", "scriptOnUse", "reserved2", "flags");
                    string[] magicFields = variant == ObjectUnionVariant.Win95
                        ? ["magicId", "reserved1", "scriptOnSuccess", "scriptOnUse", "scriptDesc", "reserved2", "flags"]
                        : ["magicId", "reserved1", "scriptOnSuccess", "scriptOnUse", "reserved2", "flags"];
                    foreach (string field in magicFields) ValidateWord(record, context, field);
                    break;
                case "enemy":
                    StrictJson.ValidateKeys(record, context, "view", "enemyId", "resistanceToSorcery", "scriptOnTurnStart", "scriptOnBattleEnd", "scriptOnReady", "padding");
                    foreach (string field in new[] { "enemyId", "resistanceToSorcery", "scriptOnTurnStart", "scriptOnBattleEnd", "scriptOnReady" })
                        ValidateWord(record, context, field);
                    ValidateWordArray(record, context, "padding", wordsPerRecord - 5);
                    break;
                case "poison":
                    StrictJson.ValidateKeys(record, context, "view", "poisonLevel", "color", "playerScript", "reserved", "enemyScript", "padding");
                    foreach (string field in new[] { "poisonLevel", "color", "playerScript", "reserved", "enemyScript" })
                        ValidateWord(record, context, field);
                    ValidateWordArray(record, context, "padding", wordsPerRecord - 5);
                    break;
                case "undetermined":
                    StrictJson.ValidateKeys(record, context, "view", "data");
                    ValidateWordArray(record, context, "data", wordsPerRecord);
                    break;
                default:
                    throw new InvalidDataException($"{context}: view must be declared as 'player', 'item', 'magic', 'enemy', 'poison', or 'undetermined'; a record may not be expressed under several views.");
            }
        }
    }

    private static JsonObject PlayerRecord(int[] words) => new()
    {
        ["view"] = "player",
        ["reserved"] = ArrayNode(words[..2]),
        ["scriptOnFriendDeath"] = words[2],
        ["scriptOnDying"] = words[3],
        ["padding"] = ArrayNode(words[4..]),
    };

    private static JsonObject ItemRecord(ObjectUnionVariant variant, int[] words)
    {
        var record = new JsonObject
        {
            ["view"] = "item",
            ["bitmap"] = words[0],
            ["price"] = words[1],
            ["scriptOnUse"] = words[2],
            ["scriptOnEquip"] = words[3],
            ["scriptOnThrow"] = words[4],
        };
        if (variant == ObjectUnionVariant.Win95) record["scriptDesc"] = words[5];
        record["flags"] = words[^1];
        return record;
    }

    private static JsonObject MagicRecord(ObjectUnionVariant variant, int[] words)
    {
        var record = new JsonObject
        {
            ["view"] = "magic",
            ["magicId"] = words[0],
            ["reserved1"] = words[1],
            ["scriptOnSuccess"] = words[2],
            ["scriptOnUse"] = words[3],
        };
        if (variant == ObjectUnionVariant.Win95) record["scriptDesc"] = words[4];
        record["reserved2"] = variant == ObjectUnionVariant.Win95 ? words[5] : words[4];
        record["flags"] = words[^1];
        return record;
    }

    // OBJECT_ENEMY/OBJECT_POISON define 5 WORDs (global.h); the rest of the physical
    // record is preserved as the padding array like the player view's undefined slots.
    private static JsonObject EnemyRecord(int[] words) => new()
    {
        ["view"] = "enemy",
        ["enemyId"] = words[0],
        ["resistanceToSorcery"] = words[1],
        ["scriptOnTurnStart"] = words[2],
        ["scriptOnBattleEnd"] = words[3],
        ["scriptOnReady"] = words[4],
        ["padding"] = ArrayNode(words[5..]),
    };

    private static JsonObject PoisonRecord(int[] words) => new()
    {
        ["view"] = "poison",
        ["poisonLevel"] = words[0],
        ["color"] = words[1],
        ["playerScript"] = words[2],
        ["reserved"] = words[3],
        ["enemyScript"] = words[4],
        ["padding"] = ArrayNode(words[5..]),
    };

    private static JsonObject UndeterminedRecord(int[] words) => new()
    {
        ["view"] = "undetermined",
        ["data"] = ArrayNode(words),
    };

    private static JsonArray ArrayNode(int[] values)
    {
        var array = new JsonArray();
        foreach (int value in values) array.Add(value);
        return array;
    }

    private static void ValidateWord(JsonObject record, string context, string field)
    {
        int value;
        try { value = record[field]?.GetValue<int>() ?? throw new InvalidDataException($"{context}.{field} must be an integer."); }
        catch (InvalidOperationException) { throw new InvalidDataException($"{context}.{field} must be an integer."); }
        catch (FormatException) { throw new InvalidDataException($"{context}.{field} must be an integer."); }
        if (value is < 0 or > 65535) throw new InvalidDataException($"{context}.{field} must be a WORD (0..65535).");
    }

    private static void ValidateWordArray(JsonObject record, string context, string field, int length)
    {
        var array = record[field]?.AsArray() ?? throw new InvalidDataException($"{context}.{field} must be an array.");
        if (array.Count != length)
            throw new InvalidDataException($"{context}.{field} must have {length} elements.");
        foreach (JsonNode? child in array)
        {
            int value;
            try { value = child?.GetValue<int>() ?? throw new InvalidDataException($"{context}.{field} must contain integers."); }
            catch (InvalidOperationException) { throw new InvalidDataException($"{context}.{field} must contain integers."); }
            catch (FormatException) { throw new InvalidDataException($"{context}.{field} must contain integers."); }
            if (value is < 0 or > 65535) throw new InvalidDataException($"{context}.{field} must contain WORDs (0..65535).");
        }
    }

    private static int WordAt(byte[] chunk, int wordIndex) => BinaryPrimitives.ReadUInt16LittleEndian(chunk.AsSpan(wordIndex * 2));
}
