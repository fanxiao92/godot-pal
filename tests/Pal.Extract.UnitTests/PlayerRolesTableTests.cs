using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pal.Extract;
using Xunit;

namespace Pal.Extract.Tests;

// Player-roles table (DATA.MKF chunk 3, PLAYERROLES, 900 bytes) - #86, the
// attribute-group half of the two-ticket split: #86 structures the attribute /
// HP / MP / level / poison-resistance / elemental-resistance / name members (the
// "#86" rows of the frozen field table in docs/design/data-schema.md), while the
// #87 equipment / magic / sound / cover members are exported as raw WORD-array
// placeholders (equipmentRaw[6][6], coveredByRaw[6], magicRaw[32][6],
// cooperativeMagicRaw[6], and seven sound placeholders of [6]) that #87 replaces
// losslessly with the structured names. Expected values are hand-computed from
// independently authored little-endian literals; every member is a plain WORD
// array in global.h (the PLAYERROLES sounds are WORD, unlike the ENEMY SHORT
// sounds), so nothing decodes as SignedWord. rgwName (WORD 18..23) doubles as
// the object.json player-view evidence source, which the evidence test pins to
// the exported name field of this same chunk.
public sealed class PlayerRolesTableTests
{
    private static readonly DataTableDescriptor Table = DataTables.PlayerRoles;

    // The 450 hand-authored WORDs of the single record, one frozen-schema field
    // per row; each row is serialized little-endian. magicRaw's unlearned middle
    // slots (1..30) are uniform zeros (0 = not learned, magicmenu.c:339-354).
    private static int[] Avatar => [1, 2, 3, 4, 5, 6];
    private static int[] BattleSprite => [65535, 0, 0x8000, 32767, 0x1234, 0xABCD];
    private static int[] SpriteNum => [0, 65535, 1, 0x8000, 32767, 0x1234];
    private static int[] Name => [36, 37, 38, 40, 39, 41]; // the real-data name shape
    private static int[] AttackAll => [0, 1, 0, 1, 0, 0];
    private static int[] Unknown1 => [0, 0, 0, 0, 0, 65535];
    private static int[] Level => [1, 10, 99, 5, 28, 34]; // 99 = MAX_LEVELS
    private static int[] MaxHp => [65535, 100, 0, 0x8000, 12345, 999];
    private static int[] MaxMp => [0, 65535, 50, 32768, 100, 777];
    private static int[] Hp => [65535, 1, 0, 32767, 12345, 0];
    private static int[] Mp => [0, 65535, 49, 1, 0, 777];
    private static int[][] EquipmentRows => // part x role; item object indices, 0 = unequipped
    [
        [10, 0, 0, 0, 0, 0],
        [0, 11, 0, 0, 0, 0],
        [10, 11, 0, 0, 0, 0],
        [0, 0, 10, 0, 0, 0],
        [0, 0, 0, 11, 0, 0],
        [0, 0, 0, 0, 10, 11],
    ];
    private static int[] AttackStrength => [100, 200, 300, 65535, 0, 42];
    private static int[] MagicStrength => [50, 0, 65535, 10, 20, 30];
    private static int[] Defense => [25, 35, 45, 55, 65, 75];
    private static int[] Dexterity => [90, 80, 70, 60, 65535, 0];
    private static int[] FleeRate => [10, 20, 30, 40, 50, 60];
    private static int[] PoisonResistance => [0, 5, 10, 65535, 1, 2];
    private static int[][] ElementalRows => // element x role
    [
        [0, 1, 2, 3, 4, 5],
        [10, 20, 30, 40, 50, 60],
        [65535, 0, 65535, 0, 1, 2],
        [100, 200, 300, 400, 500, 600],
        [7, 14, 21, 28, 35, 42],
    ];
    private static int[] Unknown2 => [1, 2, 3, 4, 5, 6];
    private static int[] Unknown3 => [6, 5, 4, 3, 2, 1];
    private static int[] Unknown4 => [0, 65535, 0x8000, 32767, 0x1234, 0xABCD];
    private static int[] CoveredBy => [0, 1, 2, 3, 4, 5]; // player role indices
    private static IEnumerable<int[]> MagicRows()
    {
        yield return [12, 13, 0, 0, 0, 0]; // slot 0: two learned magics
        for (int slot = 1; slot < 31; slot++) yield return [0, 0, 0, 0, 0, 0]; // not learned
        yield return [12, 0, 13, 0, 12, 0]; // slot 31: valid indices in a distinct pattern
    }
    private static int[] WalkFrames => [4, 4, 4, 4, 4, 4];
    private static int[] CooperativeMagic => [13, 0, 0, 0, 0, 0];
    private static int[] Unknown5 => [9, 8, 7, 6, 5, 4];
    private static int[] Unknown6 => [11, 22, 33, 44, 55, 66];
    private static int[] DeathSound => [0, 1, 2, 3, 4, 5];
    private static int[] AttackSound => [10, 20, 30, 40, 50, 60];
    private static int[] WeaponSound => [65535, 0, 1, 2, 3, 4];
    private static int[] CriticalSound => [100, 200, 300, 400, 500, 600];
    private static int[] MagicSound => [7, 14, 21, 28, 35, 42];
    private static int[] CoverSound => [0, 65535, 0x8000, 32767, 0x1234, 0xABCD];
    private static int[] DyingSound => [5, 4, 3, 2, 1, 0];

    // Concatenates the field literals in the frozen C order of global.h
    // (avatar .. dyingSound, WORD 0..449) into the single 900-byte record.
    private static byte[] Record { get; } = Words(
    [
        .. Avatar, .. BattleSprite, .. SpriteNum, .. Name, .. AttackAll, .. Unknown1,
        .. Level, .. MaxHp, .. MaxMp, .. Hp, .. Mp,
        .. EquipmentRows.SelectMany(row => row),
        .. AttackStrength, .. MagicStrength, .. Defense, .. Dexterity, .. FleeRate, .. PoisonResistance,
        .. ElementalRows.SelectMany(row => row),
        .. Unknown2, .. Unknown3, .. Unknown4, .. CoveredBy,
        .. MagicRows().SelectMany(row => row),
        .. WalkFrames, .. CooperativeMagic, .. Unknown5, .. Unknown6,
        .. DeathSound, .. AttackSound, .. WeaponSound, .. CriticalSound, .. MagicSound, .. CoverSound, .. DyingSound,
    ]);

    private static byte[] Words(params int[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), unchecked((ushort)values[i]));
        return bytes;
    }

    [Fact]
    public void Descriptor_declares_the_frozen_split_layout()
    {
        Assert.Equal("player-roles", Table.FileId);
        Assert.Equal("DATA.MKF", Table.Archive);
        Assert.Equal(3, Table.Chunk);
        Assert.Equal(900, Table.RecordByteSize); // 450 WORD x 2 = the whole PLAYERROLES layout

        string[] expectedNames =
        [
            "avatar", "battleSprite", "spriteNum", "name", "attackAll", "unknown1", "level",
            "maxHp", "maxMp", "hp", "mp", "equipmentRaw",
            "attackStrength", "magicStrength", "defense", "dexterity", "fleeRate", "poisonResistance", "elementalResistance",
            "unknown2", "unknown3", "unknown4", "coveredByRaw", "magicRaw",
            "walkFrames", "cooperativeMagicRaw", "unknown5", "unknown6",
            "deathSoundRaw", "attackSoundRaw", "weaponSoundRaw", "criticalSoundRaw", "magicSoundRaw", "coverSoundRaw", "dyingSoundRaw",
        ];
        Assert.Equal(expectedNames, Table.Fields.Select(field => field.Name).ToArray());
        Assert.Equal(35, Table.Fields.Length); // 24 #86 structured fields + 11 #87 raw placeholders

        // The three multi-dimensional members keep the C layout: equipment is
        // [MAX_PLAYER_EQUIPMENTS][MAX_PLAYER_ROLES] = [6][6], magic is
        // [MAX_PLAYER_MAGICS][MAX_PLAYER_ROLES] = [32][6], and the elemental
        // resistance is [NUM_MAGIC_ELEMENTAL][MAX_PLAYER_ROLES] = [5][6].
        Assert.Equal(new[] { 6, 6 }, Table.Fields.Single(field => field.Name == "equipmentRaw").Dimensions);
        Assert.Equal(new[] { 32, 6 }, Table.Fields.Single(field => field.Name == "magicRaw").Dimensions);
        Assert.Equal(new[] { 5, 6 }, Table.Fields.Single(field => field.Name == "elementalResistance").Dimensions);
        foreach (DataField field in Table.Fields.Where(field => field.Dimensions is not null && field.Dimensions.Length == 1))
            Assert.Equal(new[] { 6 }, field.Dimensions); // every flat member is a PLAYERS row

        // global.h declares every PLAYERROLES member as a plain WORD array: nothing
        // decodes signed, not even the sound members (WORD here, SHORT in ENEMY).
        Assert.DoesNotContain(Table.Fields, field => field.Type == DataFieldType.SignedWord);

        // The #87 final names must not appear before their ticket: only the Raw
        // placeholders stand in for the equipment/magic/sound/cover group.
        foreach (string final in new[] { "equipment", "coveredBy", "magic", "cooperativeMagic", "deathSound", "attackSound", "weaponSound", "criticalSound", "magicSound", "coverSound", "dyingSound" })
            Assert.DoesNotContain(final, Table.Fields.Select(field => field.Name).ToArray());
    }

    [Fact]
    public void Decode_reads_the_hand_authored_record_with_boundaries_and_dimensions()
    {
        Assert.Equal(900, Record.Length); // the field literals cover the whole record
        var document = DataTableSchema.DecodeTable(Table, Record);
        Assert.Equal(1, (int)document["formatVersion"]!);
        Assert.Equal("faithful", (string)document["profile"]!);
        Assert.Equal("player-roles", (string)document["fileId"]!);
        var records = document["records"]!.AsArray();
        Assert.Single(records);

        // Little-endian proofs straight from the byte literals at their global.h
        // word offsets: name starts at WORD 18, maxHp at WORD 42, hp at WORD 54,
        // equipment at WORD 66, magic at WORD 192.
        Assert.Equal(0x24, Record[36]);  Assert.Equal(0x00, Record[37]);   // name[0] = 36
        Assert.Equal(0xFF, Record[84]);  Assert.Equal(0xFF, Record[85]);   // maxHp[0] = 65535
        Assert.Equal(0xFF, Record[114]); Assert.Equal(0x7F, Record[115]);  // hp[3] = 32767 (WORD 57)
        Assert.Equal(0x34, Record[20]);  Assert.Equal(0x12, Record[21]);   // battleSprite[4] = 0x1234 (WORD 10)
        Assert.Equal(0x0A, Record[132]); Assert.Equal(0x00, Record[133]);  // equipmentRaw[0][0] = 10
        Assert.Equal(0x0C, Record[384]); Assert.Equal(0x00, Record[385]);  // magicRaw[0][0] = 12

        var record = records[0]!.AsObject();
        AssertRow(record, "avatar", Avatar);
        AssertRow(record, "battleSprite", BattleSprite);
        AssertRow(record, "spriteNum", SpriteNum);
        AssertRow(record, "name", Name);
        AssertRow(record, "attackAll", AttackAll);
        AssertRow(record, "unknown1", Unknown1);
        AssertRow(record, "level", Level);
        AssertRow(record, "maxHp", MaxHp);
        AssertRow(record, "maxMp", MaxMp);
        AssertRow(record, "hp", Hp);
        AssertRow(record, "mp", Mp);
        AssertRows(record, "equipmentRaw", EquipmentRows);
        AssertRow(record, "attackStrength", AttackStrength);
        AssertRow(record, "magicStrength", MagicStrength);
        AssertRow(record, "defense", Defense);
        AssertRow(record, "dexterity", Dexterity);
        AssertRow(record, "fleeRate", FleeRate);
        AssertRow(record, "poisonResistance", PoisonResistance);
        AssertRows(record, "elementalResistance", ElementalRows);
        AssertRow(record, "unknown2", Unknown2);
        AssertRow(record, "unknown3", Unknown3);
        AssertRow(record, "unknown4", Unknown4);
        AssertRow(record, "coveredByRaw", CoveredBy);
        var magic = record["magicRaw"]!.AsArray();
        Assert.Equal(32, magic.Count); // MAX_PLAYER_MAGICS slots
        AssertRows(record, "magicRaw", MagicRows().ToArray());
        AssertRow(record, "walkFrames", WalkFrames);
        AssertRow(record, "cooperativeMagicRaw", CooperativeMagic);
        AssertRow(record, "unknown5", Unknown5);
        AssertRow(record, "unknown6", Unknown6);
        AssertRow(record, "deathSoundRaw", DeathSound);
        AssertRow(record, "attackSoundRaw", AttackSound);
        AssertRow(record, "weaponSoundRaw", WeaponSound);
        AssertRow(record, "criticalSoundRaw", CriticalSound);
        AssertRow(record, "magicSoundRaw", MagicSound);
        AssertRow(record, "coverSoundRaw", CoverSound);
        AssertRow(record, "dyingSoundRaw", DyingSound);
    }

    [Fact]
    public void DescribeIndexEntry_reports_the_source_chunk_and_hand_verified_sha256()
    {
        byte[] chunk = new byte[900]; // one all-zero record
        var entry = DataTableSchema.DescribeIndexEntry(Table, chunk);
        Assert.Equal("player-roles.json", (string)entry["file"]!);
        Assert.Equal("DATA.MKF", (string)entry["archive"]!);
        Assert.Equal(3, (int)entry["chunk"]!);
        Assert.Equal(1, (int)entry["recordCount"]!);
        Assert.Equal(900, (int)entry["byteLength"]!);
        // Independent expectation: hand-verified SHA-256 of 900 zero bytes (Python
        // hashlib and PowerShell SHA256.Create both agree on this constant).
        Assert.Equal("fa807c957eafe34b850cb453a096df2e5899f0902a837fccd59f9aafa869fb44", (string)entry["sha256"]!);
        // Cross-check against in-test hashes of the hand-authored record and of a
        // two-record chunk (the count stays chunk-length-derived, 1800 / 900 = 2).
        var one = DataTableSchema.DescribeIndexEntry(Table, Record);
        Assert.Equal("46115d08c5d2c87a240bfdcc1a74c7cbe480957554b41b82c54b13753935cdef", (string)one["sha256"]!);
        var two = DataTableSchema.DescribeIndexEntry(Table, [.. Record, .. Record]);
        Assert.Equal(2, (int)two["recordCount"]!);
        Assert.Equal(1800, (int)two["byteLength"]!);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData([.. Record, .. Record])), (string)two["sha256"]!);
    }

    [Fact]
    public void Decode_rejects_chunk_3_lengths_that_are_not_a_positive_multiple_of_900()
    {
        foreach (int length in new[] { 899, 901, 1801 })
        {
            var error = Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, new byte[length]));
            Assert.Contains("DATA.MKF", error.Message);
            Assert.Contains("chunk 3", error.Message);
            Assert.Contains(length.ToString(), error.Message);
            Assert.Contains("900", error.Message);
        }
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DescribeIndexEntry(Table, new byte[899]));
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, Array.Empty<byte>())); // empty chunk is not a table
    }

    [Fact]
    public void ValidateDocument_accepts_the_decoded_record_and_rejects_unknown_or_missing_fields()
    {
        DataTableSchema.ValidateDocument(Table, DataTableSchema.DecodeTable(Table, Record)); // freshly decoded passes

        var finalName = DataTableSchema.DecodeTable(Table, Record);
        finalName["records"]![0]!["equipment"] = new JsonArray(0, 0, 0, 0, 0, 0); // #87 final name, not the #86 placeholder
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, finalName));

        var cName = DataTableSchema.DecodeTable(Table, Record);
        cName["records"]![0]!["rgwMagic"] = new JsonArray(0, 0, 0, 0, 0, 0); // C name, not the frozen JSON name
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, cName));

        foreach (string placeholder in new[] { "magicRaw", "coveredByRaw", "dyingSoundRaw" })
        {
            var missing = DataTableSchema.DecodeTable(Table, Record);
            missing["records"]![0]!.AsObject().Remove(placeholder); // dropping a placeholder drops information
            Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, missing));
        }
    }

    [Fact]
    public void ValidateDocument_rejects_out_of_range_values_and_wrong_array_shapes()
    {
        foreach (int value in new[] { -1, 65_536 })
        {
            var document = DataTableSchema.DecodeTable(Table, Record);
            document["records"]![0]!["hp"]![0] = value;
            Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, document));
        }

        var shortRow = DataTableSchema.DecodeTable(Table, Record); // a PLAYERS row is exactly 6 words
        shortRow["records"]![0]!["name"] = new JsonArray(Enumerable.Range(1, 5).Select(value => (JsonNode)value).ToArray());
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, shortRow));

        var fiveParts = DataTableSchema.DecodeTable(Table, Record); // equipmentRaw is exactly [6][6]
        fiveParts["records"]![0]!["equipmentRaw"] = new JsonArray(Enumerable.Range(0, 5).Select(_ => (JsonNode)new JsonArray(0, 0, 0, 0, 0, 0)).ToArray());
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, fiveParts));

        var fiveRoles = DataTableSchema.DecodeTable(Table, Record); // magicRaw inner rows are exactly 6 words
        fiveRoles["records"]![0]!["magicRaw"] = new JsonArray(Enumerable.Range(0, 32).Select(_ => (JsonNode)new JsonArray(0, 0, 0, 0, 0)).ToArray());
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, fiveRoles));

        var fourElements = DataTableSchema.DecodeTable(Table, Record); // NUM_MAGIC_ELEMENTAL is exactly 5
        fourElements["records"]![0]!["elementalResistance"] = new JsonArray(Enumerable.Range(0, 4).Select(_ => (JsonNode)new JsonArray(0, 0, 0, 0, 0, 0)).ToArray());
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, fourElements));
    }

    [Fact]
    public void A_single_placeholder_word_change_reaches_the_document_verbatim()
    {
        var original = DataTableSchema.DecodeTable(Table, Record);
        Assert.Equal(11, (int)original["records"]![0]!["equipmentRaw"]![2]![1]!); // baselines from the literals
        Assert.Equal(0, (int)original["records"]![0]!["magicRaw"]![7]![3]!);

        // equipmentRaw[2][1] is WORD 66 + 2*6 + 1 = 79; magicRaw[7][3] is
        // WORD 192 + 7*6 + 3 = 237.
        byte[] equipmentMutated = [.. Record];
        BinaryPrimitives.WriteUInt16LittleEndian(equipmentMutated.AsSpan(2 * 79), 0x1234);
        var changedEquipment = DataTableSchema.DecodeTable(Table, equipmentMutated);
        Assert.Equal(0x1234, (int)changedEquipment["records"]![0]!["equipmentRaw"]![2]![1]!);

        byte[] magicMutated = [.. Record];
        BinaryPrimitives.WriteUInt16LittleEndian(magicMutated.AsSpan(2 * 237), 65535);
        var changedMagic = DataTableSchema.DecodeTable(Table, magicMutated);
        Assert.Equal(65535, (int)changedMagic["records"]![0]!["magicRaw"]![7]![3]!);

        // Exactly one document element differs: reverting it in the decoded output
        // restores deep equality with the original document, so the placeholder
        // preserves every WORD and hides nothing.
        changedEquipment["records"]![0]!["equipmentRaw"]![2]![1] = 11;
        Assert.True(JsonNode.DeepEquals(changedEquipment, original));
        changedMagic["records"]![0]!["magicRaw"]![7]![3] = 0;
        Assert.True(JsonNode.DeepEquals(changedMagic, original));
    }

    [Fact]
    public void Name_field_matches_the_object_player_view_evidence_words()
    {
        // rgwName (WORD 18..23 of this chunk) is also the object.json player-view
        // evidence source: the evidence reader and the exported name field must
        // agree on the same six words of the same 900-byte record.
        var document = DataTableSchema.DecodeTable(Table, Record);
        var name = document["records"]![0]!["name"]!.AsArray();
        var evidence = ObjectUnionTable.ReadViewEvidence(new byte[18], new byte[70], new byte[10], Record);
        Assert.Equal(6, evidence.PlayerObjectIndices.Count);
        for (int role = 0; role < 6; role++)
            Assert.Contains((int)name[role]!, evidence.PlayerObjectIndices);
    }

    [Fact]
    public void RejectDuplicateKeys_rejects_duplicate_keys_in_a_player_roles_document()
    {
        // Duplicate keys cannot survive JsonNode round-trips, so the strict gate works on the
        // parsed document model (shared counterexample path used by the events contract).
        using var duplicated = JsonDocument.Parse(
            """{ "formatVersion": 1, "records": [ { "name": [0], "name": [0] } ] }""");
        Assert.Throws<InvalidDataException>(() => StrictJson.RejectDuplicateKeys(duplicated.RootElement, "player-roles.json"));

        using var clean = JsonDocument.Parse("""{ "formatVersion": 1, "records": [ { "name": [0] } ] }""");
        StrictJson.RejectDuplicateKeys(clean.RootElement, "player-roles.json"); // must not throw
    }

    private static void AssertRow(JsonObject record, string field, int[] expected)
    {
        var row = record[field]!.AsArray();
        Assert.Equal(expected.Length, row.Count);
        for (int index = 0; index < expected.Length; index++)
            Assert.Equal(expected[index], (int)row[index]!);
    }

    private static void AssertRows(JsonObject record, string field, int[][] expected)
    {
        var rows = record[field]!.AsArray();
        Assert.Equal(expected.Length, rows.Count);
        for (int row = 0; row < expected.Length; row++)
        {
            var values = rows[row]!.AsArray();
            Assert.Equal(expected[row].Length, values.Count);
            for (int index = 0; index < expected[row].Length; index++)
                Assert.Equal(expected[row][index], (int)values[index]!);
        }
    }
}
