using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pal.Extract;
using Xunit;

namespace Pal.Extract.Tests;

// Object union table (SSS.MKF chunk 2, OBJECT, 14 bytes WIN95 / 12 bytes DOS) - #81.
// Expected values are hand-computed from independently authored little-endian byte
// literals; the per-view schema is the frozen table in docs/design/data-schema.md,
// which mirrors reference/sdlpal/global.h (tagOBJECT_PLAYER / tagOBJECT_ITEM).
// Boundary words: 0, 65535, 0x8000 (32768 unsigned), and real-data script entry
// samples (43445 = object[36].wScriptOnFriendDeath, 40661 = object[61].wScriptDesc).
// A record never carries two views: the declared view fixes the exact field set.
public sealed class ObjectTableTests
{
    // Hand-authored WIN95 records (7 WORDs each, little-endian).
    private static byte[] PlayerRecord => Words(0, 0, 43445, 43374, 0x1234, 65535, 32768);
    private static byte[] ItemRecordFlagsZero => Words(197, 150, 43022, 39140, 0, 40661, 0);
    private static byte[] ItemRecordFlagsMax => Words(0xABCD, 2000, 39554, 0, 42890, 40524, 65535);
    private static byte[] UndeterminedRecord => Words(1, 2, 3, 4, 5, 6, 7);

    private static ObjectViewEvidence EvidenceFor(int[] players, int[] items) =>
        new(new HashSet<int>(players), new HashSet<int>(items));

    private static byte[] Words(params int[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), unchecked((ushort)values[i]));
        return bytes;
    }

    [Fact]
    public void DetectVariant_decides_by_divisibility_and_rejects_undecidable_lengths()
    {
        Assert.Equal(ObjectUnionVariant.Win95, ObjectUnionTable.DetectVariant(14));
        Assert.Equal(ObjectUnionVariant.Win95, ObjectUnionTable.DetectVariant(7910)); // real data: 7910 % 12 = 2
        Assert.Equal(ObjectUnionVariant.Dos, ObjectUnionTable.DetectVariant(12));
        Assert.Equal(ObjectUnionVariant.Dos, ObjectUnionTable.DetectVariant(84 + 12)); // 96: 8x12, not 14-divisible

        var ambiguous = Assert.Throws<InvalidDataException>(() => ObjectUnionTable.DetectVariant(84)); // lcm(12,14)
        Assert.Contains("SSS.MKF", ambiguous.Message);
        Assert.Contains("chunk 2", ambiguous.Message);
        Assert.Contains("both 14 (WIN95) and 12 (DOS)", ambiguous.Message);

        var neither = Assert.Throws<InvalidDataException>(() => ObjectUnionTable.DetectVariant(13));
        Assert.Contains("SSS.MKF", neither.Message);
        Assert.Contains("chunk 2", neither.Message);
        Assert.Contains("13", neither.Message);
        Assert.Throws<InvalidDataException>(() => ObjectUnionTable.DetectVariant(0)); // empty chunk is not a table
    }

    [Fact]
    public void Decode_reads_player_view_records_from_little_endian_bytes()
    {
        byte[] chunk = PlayerRecord;
        var document = ObjectUnionTable.DecodeTable(chunk, EvidenceFor([0], []));
        Assert.Equal(1, (int)document["formatVersion"]!);
        Assert.Equal("faithful", (string)document["profile"]!);
        Assert.Equal("object", (string)document["fileId"]!);
        var record = document["records"]![0]!.AsObject();
        Assert.Equal("player", (string)record["view"]!);

        // Little-endian proof straight from the byte literals: scriptOnFriendDeath = 43445
        // (0xA9B5) is stored b5 a9; 0x1234 in the padding is stored 34 12.
        Assert.Equal(0xB5, chunk[4]);
        Assert.Equal(0xA9, chunk[5]);
        Assert.Equal(0x34, chunk[8]);
        Assert.Equal(0x12, chunk[9]);

        Assert.Equal(new[] { 0, 0 }, record["reserved"]!.AsArray().Select(node => (int)node!));
        Assert.Equal(43445, (int)record["scriptOnFriendDeath"]!);   // real object[36].wScriptOnFriendDeath
        Assert.Equal(43374, (int)record["scriptOnDying"]!);         // real object[37].wScriptOnDying
        Assert.Equal(new[] { 0x1234, 65535, 32768 }, record["padding"]!.AsArray().Select(node => (int)node!));
        // Only the player view's fields: no item/magic/enemy/poison member is present.
        Assert.False(record.ContainsKey("bitmap"));
        Assert.False(record.ContainsKey("price"));
        Assert.False(record.ContainsKey("flags"));
    }

    [Fact]
    public void Decode_reads_item_view_records_with_flag_boundaries()
    {
        byte[] chunk = [.. ItemRecordFlagsZero, .. ItemRecordFlagsMax];
        var document = ObjectUnionTable.DecodeTable(chunk, EvidenceFor([], [0, 1]));
        var records = document["records"]!.AsArray();
        Assert.Equal(2, records.Count);

        var zero = records[0]!.AsObject();
        Assert.Equal("item", (string)zero["view"]!);
        Assert.Equal(197, (int)zero["bitmap"]!);                    // real object[61].wBitmap
        Assert.Equal(150, (int)zero["price"]!);                     // real object[61].wPrice
        Assert.Equal(43022, (int)zero["scriptOnUse"]!);             // real object[61].wScriptOnUse
        Assert.Equal(39140, (int)zero["scriptOnEquip"]!);
        Assert.Equal(0, (int)zero["scriptOnThrow"]!);
        Assert.Equal(40661, (int)zero["scriptDesc"]!);              // real object[61].wScriptDesc
        Assert.Equal(0, (int)zero["flags"]!);                       // boundary: no ITEMFLAG bit set

        var max = records[1]!.AsObject();
        Assert.Equal("item", (string)max["view"]!);
        Assert.Equal(0xABCD, (int)max["bitmap"]!);                  // bytes cd ab
        Assert.Equal(2000, (int)max["price"]!);
        Assert.Equal(39554, (int)max["scriptOnUse"]!);
        Assert.Equal(0, (int)max["scriptOnEquip"]!);
        Assert.Equal(42890, (int)max["scriptOnThrow"]!);
        Assert.Equal(40524, (int)max["scriptDesc"]!);
        Assert.Equal(65535, (int)max["flags"]!);                    // boundary: every bit set
        // Only the item view's fields: no player member is present.
        Assert.False(max.ContainsKey("scriptOnFriendDeath"));
        Assert.False(max.ContainsKey("reserved"));
    }

    [Fact]
    public void Decode_marks_records_without_evidence_as_undetermined_raw_words()
    {
        byte[] chunk = [.. PlayerRecord, .. UndeterminedRecord];
        var document = ObjectUnionTable.DecodeTable(chunk, EvidenceFor([0], []));
        var records = document["records"]!.AsArray();
        Assert.Equal(2, records.Count);
        Assert.Equal("player", (string)records[0]!["view"]!);
        var undetermined = records[1]!.AsObject();
        Assert.Equal("undetermined", (string)undetermined["view"]!);
        // The whole physical WORD group is preserved without any view interpretation.
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7 }, undetermined["data"]!.AsArray().Select(node => (int)node!));
        Assert.Equal(2, undetermined.Count); // view + data only
    }

    [Fact]
    public void Decode_reads_dos_records_without_the_script_desc_slot()
    {
        byte[] chunk = [.. Words(0, 0, 5, 6, 0x1234, 0xABCD), .. Words(10, 20, 30, 40, 50, 65535), .. Words(9, 8, 7, 6, 5, 4)];
        var document = ObjectUnionTable.DecodeTable(chunk, EvidenceFor([0], [1]));
        var records = document["records"]!.AsArray();
        Assert.Equal(3, records.Count);

        var player = records[0]!.AsObject();
        Assert.Equal("player", (string)player["view"]!);
        Assert.Equal(5, (int)player["scriptOnFriendDeath"]!);
        Assert.Equal(6, (int)player["scriptOnDying"]!);
        Assert.Equal(new[] { 0x1234, 0xABCD }, player["padding"]!.AsArray().Select(node => (int)node!)); // DOS: 2 padding words

        var item = records[1]!.AsObject();
        Assert.Equal("item", (string)item["view"]!);
        Assert.Equal(10, (int)item["bitmap"]!);
        Assert.Equal(20, (int)item["price"]!);
        Assert.Equal(30, (int)item["scriptOnUse"]!);
        Assert.Equal(40, (int)item["scriptOnEquip"]!);
        Assert.Equal(50, (int)item["scriptOnThrow"]!);
        Assert.Equal(65535, (int)item["flags"]!);                   // DOS layout: wFlags is WORD 5
        Assert.False(item.ContainsKey("scriptDesc"));               // DOS has no description slot

        var undetermined = records[2]!.AsObject();
        Assert.Equal(new[] { 9, 8, 7, 6, 5, 4 }, undetermined["data"]!.AsArray().Select(node => (int)node!));
    }

    [Fact]
    public void ReadViewEvidence_collects_player_and_item_references_from_the_data_chunks()
    {
        // One store record (9 WORDs): slots hold items 10 and 11, zeros are empty slots.
        byte[] store = Words(10, 0, 11, 0, 0, 0, 0, 0, 0);
        // One enemy record (35 WORDs): wAttackEquivItem = WORD 17 = 13, wStealItem = WORD 19 = 14.
        var enemyWords = new int[35];
        enemyWords[17] = 13;
        enemyWords[19] = 14;
        byte[] enemy = Words(enemyWords);
        // PLAYERROLES (450 WORDs): rgwName = WORD 18..23 = 0..5 (a name of 0 is still read
        // as rgObject[0].player by fight.c), rgwEquipment = WORD 66.. = item 9.
        var roleWords = new int[450];
        for (int role = 0; role < 6; role++) roleWords[18 + role] = role;
        roleWords[66] = 9;
        byte[] roles = Words(roleWords);

        var evidence = ObjectUnionTable.ReadViewEvidence(store, enemy, roles);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, evidence.PlayerObjectIndices.Order());
        Assert.Equal(new[] { 9, 10, 11, 13, 14 }, evidence.ItemObjectIndices.Order());
        Assert.Empty(evidence.PlayerObjectIndices.Intersect(evidence.ItemObjectIndices));
    }

    [Fact]
    public void ReadViewEvidence_rejects_malformed_evidence_chunks()
    {
        byte[] store = Words(1, 0, 2, 0, 0, 0, 0, 0, 0);
        var enemyWords = new int[35];
        byte[] enemy = Words(enemyWords);
        var roleWords = new int[450];
        byte[] roles = Words(roleWords);
        var valid = () => ObjectUnionTable.ReadViewEvidence(store, enemy, roles);
        valid(); // the well-formed fixture passes

        var storeError = Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ReadViewEvidence(new byte[19], enemy, roles));
        Assert.Contains("DATA.MKF", storeError.Message);
        Assert.Contains("chunk 0", storeError.Message);
        Assert.Contains("18", storeError.Message);

        var enemyError = Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ReadViewEvidence(store, new byte[69], roles));
        Assert.Contains("DATA.MKF", enemyError.Message);
        Assert.Contains("chunk 1", enemyError.Message);
        Assert.Contains("70", enemyError.Message);

        var rolesError = Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ReadViewEvidence(store, enemy, new byte[899]));
        Assert.Contains("DATA.MKF", rolesError.Message);
        Assert.Contains("chunk 3", rolesError.Message);
        Assert.Contains("900", rolesError.Message);
    }

    [Fact]
    public void ReadViewEvidence_rejects_one_object_referenced_under_two_views()
    {
        // rgwName[0] = 5 makes object 5 player evidence while the store sells item 5.
        byte[] store = Words(5, 0, 0, 0, 0, 0, 0, 0, 0);
        byte[] enemy = Words(new int[35]);
        var roleWords = new int[450];
        roleWords[18] = 5;
        byte[] roles = Words(roleWords);

        var error = Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ReadViewEvidence(store, enemy, roles));
        Assert.Contains("conflict", error.Message);
        Assert.Contains("5", error.Message);
        Assert.Contains("player", error.Message);
        Assert.Contains("item", error.Message);
    }

    [Fact]
    public void Decode_rejects_evidence_indices_outside_the_record_count()
    {
        byte[] chunk = [.. PlayerRecord, .. ItemRecordFlagsZero, .. UndeterminedRecord]; // 3 records
        var playerError = Assert.Throws<InvalidDataException>(
            () => ObjectUnionTable.DecodeTable(chunk, new ObjectViewEvidence(new HashSet<int> { 3 }, new HashSet<int>())));
        Assert.Contains("3", playerError.Message);
        Assert.Contains("player", playerError.Message);
        Assert.Contains("3-record", playerError.Message);

        var itemError = Assert.Throws<InvalidDataException>(
            () => ObjectUnionTable.DecodeTable(chunk, new ObjectViewEvidence(new HashSet<int>(), new HashSet<int> { 600 })));
        Assert.Contains("600", itemError.Message);
        Assert.Contains("item", itemError.Message);
    }

    [Fact]
    public void ValidateDocument_accepts_decoded_records_and_rejects_multi_view_or_unknown_views()
    {
        byte[] chunk = [.. PlayerRecord, .. ItemRecordFlagsZero, .. UndeterminedRecord];
        var evidence = EvidenceFor([0], [1]);
        var document = ObjectUnionTable.DecodeTable(chunk, evidence);
        ObjectUnionTable.ValidateDocument(chunk.Length, document); // freshly decoded passes

        // A player-view record must not also carry item-view fields.
        var crossView = ObjectUnionTable.DecodeTable(chunk, evidence);
        crossView["records"]![0]!["bitmap"] = 1;
        Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ValidateDocument(chunk.Length, crossView));

        // An item-view record must not also carry player-view fields.
        var reverseCrossView = ObjectUnionTable.DecodeTable(chunk, evidence);
        reverseCrossView["records"]![1]!["scriptOnDying"] = 1;
        Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ValidateDocument(chunk.Length, reverseCrossView));

        // Unregistered views are rejected (magic/enemy/poison belong to #82).
        var wrongView = ObjectUnionTable.DecodeTable(chunk, evidence);
        wrongView["records"]![2]!["view"] = "magic";
        Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ValidateDocument(chunk.Length, wrongView));

        // The view declaration itself is mandatory.
        var missingView = ObjectUnionTable.DecodeTable(chunk, evidence);
        missingView["records"]![0]!.AsObject().Remove("view");
        Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ValidateDocument(chunk.Length, missingView));
    }

    [Fact]
    public void ValidateDocument_rejects_wrong_array_lengths_ranges_and_variant_mismatch()
    {
        byte[] chunk = [.. PlayerRecord, .. ItemRecordFlagsZero];
        var evidence = EvidenceFor([0], [1]);
        var shortData = ObjectUnionTable.DecodeTable(chunk, evidence);
        shortData["records"]![0]!["padding"] = new JsonArray(1, 2); // WIN player padding has 3 words
        Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ValidateDocument(chunk.Length, shortData));

        var longData = ObjectUnionTable.DecodeTable(chunk, evidence);
        longData["records"]![0]!["reserved"] = new JsonArray(0);
        Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ValidateDocument(chunk.Length, longData));

        foreach ((string field, int value) in new[] { ("bitmap", -1), ("flags", 65_536), ("scriptDesc", -1), ("price", 65_536) })
        {
            var document = ObjectUnionTable.DecodeTable(chunk, evidence);
            document["records"]![1]![field] = value;
            Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ValidateDocument(chunk.Length, document));
        }

        var outOfRangeArray = ObjectUnionTable.DecodeTable(chunk, evidence);
        outOfRangeArray["records"]![0]!["padding"] = new JsonArray(0, 0, 65_536);
        Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ValidateDocument(chunk.Length, outOfRangeArray));

        // A DOS-length document (12-byte records) must not carry the WIN-only scriptDesc slot.
        var dosChunk = Words(10, 20, 30, 40, 50, 65535);
        var dosDocument = ObjectUnionTable.DecodeTable(dosChunk, new ObjectViewEvidence(new HashSet<int>(), new HashSet<int> { 0 }));
        ObjectUnionTable.ValidateDocument(dosChunk.Length, dosDocument);
        dosDocument["records"]![0]!["scriptDesc"] = 1;
        Assert.Throws<InvalidDataException>(() => ObjectUnionTable.ValidateDocument(dosChunk.Length, dosDocument));
    }

    [Fact]
    public void DescribeIndexEntry_reports_the_source_chunk_and_hand_verified_sha256()
    {
        byte[] winChunk = new byte[14]; // one all-zero WIN95 record
        var winEntry = ObjectUnionTable.DescribeIndexEntry(winChunk);
        Assert.Equal("object.json", (string)winEntry["file"]!);
        Assert.Equal("SSS.MKF", (string)winEntry["archive"]!);
        Assert.Equal(2, (int)winEntry["chunk"]!);
        Assert.Equal(1, (int)winEntry["recordCount"]!);
        Assert.Equal(14, (int)winEntry["byteLength"]!);
        // Independent expectation: hand-verified SHA-256 of 14 zero bytes (Python hashlib
        // and PowerShell SHA256.Create both agree on this constant).
        Assert.Equal("e7ecebbc590bc88b3761fa6cd03d749f87463dabb67021a5c6768c25ec68b3f2", (string)winEntry["sha256"]!);

        byte[] dosChunk = new byte[12];
        var dosEntry = ObjectUnionTable.DescribeIndexEntry(dosChunk);
        Assert.Equal(1, (int)dosEntry["recordCount"]!);
        Assert.Equal("15ec7bf0b50732b49f8228e07d24365338f9e3ab994b00af08e5a3bffe55fd8b", (string)dosEntry["sha256"]!);

        // Cross-check against an in-test hash of the three hand-authored records.
        byte[] literal = [.. PlayerRecord, .. ItemRecordFlagsZero, .. ItemRecordFlagsMax];
        var entry = ObjectUnionTable.DescribeIndexEntry(literal);
        Assert.Equal(3, (int)entry["recordCount"]!);
        Assert.Equal(42, (int)entry["byteLength"]!);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(literal)), (string)entry["sha256"]!);

        Assert.Throws<InvalidDataException>(() => ObjectUnionTable.DescribeIndexEntry(new byte[13]));
    }

    [Fact]
    public void RejectDuplicateKeys_rejects_duplicate_keys_in_an_object_document()
    {
        // Duplicate keys cannot survive JsonNode round-trips, so the strict gate works on the
        // parsed document model (shared counterexample path used by the events contract).
        using var duplicated = JsonDocument.Parse(
            """{ "formatVersion": 1, "records": [ { "view": "item", "view": "item" } ] }""");
        Assert.Throws<InvalidDataException>(() => StrictJson.RejectDuplicateKeys(duplicated.RootElement, "object.json"));

        using var clean = JsonDocument.Parse("""{ "formatVersion": 1, "records": [ { "view": "item" } ] }""");
        StrictJson.RejectDuplicateKeys(clean.RootElement, "object.json"); // must not throw
    }
}
