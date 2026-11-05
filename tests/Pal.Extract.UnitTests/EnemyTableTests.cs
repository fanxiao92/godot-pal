using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pal.Extract;
using Xunit;
using static Pal.Extract.Tests.OpcodeCounterexampleHarness;

namespace Pal.Extract.Tests;

// Enemy table (DATA.MKF chunk 1, ENEMY, 70 bytes) - #84. Expected values are
// hand-computed from independently authored little-endian byte literals; the
// per-field schema is the frozen table in docs/design/data-schema.md, which mirrors
// reference/sdlpal/global.h. This is the first DATA-side table with SHORT members:
// global.h declares the five sound members as SHORT, so they decode signed while
// every other member stays an unsigned WORD. Boundary words: 0, 65535, 0x8000 (as
// -32768 or 32768 depending on the field's signedness), 32767, -1, and fight.c's
// wMagicSound >= 0 domain.
public sealed class EnemyTableTests
{
    private static readonly DataTableDescriptor Table = DataTables.Enemy;

    // One hand-authored record per line; -32768 encodes as 0x8000, -1 as 0xFFFF.
    // Record 1 word slots: 0..5 WORD, 6..10 the five SHORT sounds, 11..16 WORD
    // (magicId 0xFFFF = no magic per fight.c:4660), 17..20 WORD, 21..26 WORD,
    // 27..31 wElemResistance[5], 32..34 WORD.
    private static byte[] Record1 => Words(65535, 0x1234, 0, 32767, 0xABCD, 1,
        -32768, -1, 32767, -15, 0,
        65535, 4660, 0, 32768, 65535, 0,
        3, 65535, 4, 32768,
        0xABCD, 3, 65535, 32767, 0, 5,
        65535, 0, 32767, 0x1234, 65535,
        0, 1, 32768);
    // Record 2 uses small plausible values (record 2's magicId 42 and
    // attackEquivItemId 9 are object.json magic/item-view indices; here they only
    // need to round-trip as WORDs).
    private static byte[] Record2 => Words(6, 7, 5, 3, 8, 0,
        11, -2, 12, 13, -3,
        60, 5, 10, 3, 42, 25,
        9, 40, 0, 0,
        20, 12, 14, 18, 50, 3,
        0, 1, 2, 3, 4,
        5, 0, 0);

    [Fact]
    public void Descriptor_declares_the_frozen_thirty_five_word_layout()
    {
        Assert.Equal("enemy", Table.FileId);
        Assert.Equal("DATA.MKF", Table.Archive);
        Assert.Equal(1, Table.Chunk);
        Assert.Equal(70, Table.RecordByteSize);
        Assert.Equal(31, Table.Fields.Length); // 30 scalars plus the 5-word wElemResistance array
        Assert.Equal(new[]
        {
            "idleFrames", "magicFrames", "attackFrames", "idleAnimSpeed", "actWaitFrames", "yPosOffset",
            "attackSound", "actionSound", "magicSound", "deathSound", "callSound",
            "health", "exp", "cash", "level", "magicId", "magicRate",
            "attackEquivItemId", "attackEquivItemRate", "stealItemId", "stealItemCount",
            "attackStrength", "magicStrength", "defense", "dexterity", "fleeRate", "poisonResistance",
            "elementalResistance", "physicalResistance", "dualMove", "collectValue",
        }, Table.Fields.Select(field => field.Name));
        // Only the five SHORT members of global.h decode signed; wElemResistance is a
        // plain WORD array of NUM_MAGIC_ELEMENTAL (palcommon.h:57) elements.
        Assert.Equal(new[] { "attackSound", "actionSound", "magicSound", "deathSound", "callSound" },
            Table.Fields.Where(field => field.Type == DataFieldType.SignedWord).Select(field => field.Name));
        var elemental = Table.Fields.Single(field => field.Name == "elementalResistance");
        Assert.Equal(new[] { 5 }, elemental.Dimensions);
        Assert.Equal(DataFieldType.Word, elemental.Type);
    }

    [Fact]
    public void Decode_reads_little_endian_boundary_values_from_two_records()
    {
        byte[] chunk = [.. Record1, .. Record2];
        var document = DataTableSchema.DecodeTable(Table, chunk);
        Assert.Equal(1, (int)document["formatVersion"]!);
        Assert.Equal("faithful", (string)document["profile"]!);
        Assert.Equal("enemy", (string)document["fileId"]!);
        Assert.Equal(2, document["records"]!.AsArray().Count);

        // Little-endian proof straight from the byte literals: magicFrames = 0x1234
        // is 34 12; attackSound -32768 is 0x8000 as 00 80; actionSound -1 is ff ff.
        Assert.Equal(0x34, chunk[2]);
        Assert.Equal(0x12, chunk[3]);
        Assert.Equal(0x00, chunk[12]);
        Assert.Equal(0x80, chunk[13]);
        Assert.Equal(0xFF, chunk[14]);
        Assert.Equal(0xFF, chunk[15]);

        var first = document["records"]![0]!.AsObject();
        Assert.Equal(65535, (int)first["idleFrames"]!);
        Assert.Equal(0x1234, (int)first["magicFrames"]!);            // bytes 34 12
        Assert.Equal(0, (int)first["attackFrames"]!);
        Assert.Equal(32767, (int)first["idleAnimSpeed"]!);
        Assert.Equal(0xABCD, (int)first["actWaitFrames"]!);
        Assert.Equal(1, (int)first["yPosOffset"]!);
        Assert.Equal(-32768, (int)first["attackSound"]!);            // 0x8000 as SHORT
        Assert.Equal(-1, (int)first["actionSound"]!);                // 0xFFFF as SHORT
        Assert.Equal(32767, (int)first["magicSound"]!);
        Assert.Equal(-15, (int)first["deathSound"]!);
        Assert.Equal(0, (int)first["callSound"]!);
        Assert.Equal(65535, (int)first["health"]!);
        Assert.Equal(4660, (int)first["exp"]!);                      // 0x1234, bytes 34 12
        Assert.Equal(0, (int)first["cash"]!);
        Assert.Equal(32768, (int)first["level"]!);                   // 0x8000 as WORD
        Assert.Equal(65535, (int)first["magicId"]!);                 // 0xFFFF = no magic
        Assert.Equal(0, (int)first["magicRate"]!);
        Assert.Equal(3, (int)first["attackEquivItemId"]!);
        Assert.Equal(65535, (int)first["attackEquivItemRate"]!);
        Assert.Equal(4, (int)first["stealItemId"]!);
        Assert.Equal(32768, (int)first["stealItemCount"]!);
        Assert.Equal(0xABCD, (int)first["attackStrength"]!);
        Assert.Equal(3, (int)first["magicStrength"]!);
        Assert.Equal(65535, (int)first["defense"]!);
        Assert.Equal(32767, (int)first["dexterity"]!);
        Assert.Equal(0, (int)first["fleeRate"]!);
        Assert.Equal(5, (int)first["poisonResistance"]!);
        var elemental = first["elementalResistance"]!.AsArray();
        Assert.Equal(5, elemental.Count);
        Assert.Equal(65535, (int)elemental[0]!);
        Assert.Equal(0, (int)elemental[1]!);
        Assert.Equal(32767, (int)elemental[2]!);
        Assert.Equal(0x1234, (int)elemental[3]!);                    // bytes 34 12
        Assert.Equal(65535, (int)elemental[4]!);
        Assert.Equal(0, (int)first["physicalResistance"]!);
        Assert.Equal(1, (int)first["dualMove"]!);
        Assert.Equal(32768, (int)first["collectValue"]!);

        var second = document["records"]![1]!.AsObject();
        Assert.Equal(6, (int)second["idleFrames"]!);
        Assert.Equal(7, (int)second["magicFrames"]!);
        Assert.Equal(5, (int)second["attackFrames"]!);
        Assert.Equal(3, (int)second["idleAnimSpeed"]!);
        Assert.Equal(8, (int)second["actWaitFrames"]!);
        Assert.Equal(0, (int)second["yPosOffset"]!);
        Assert.Equal(11, (int)second["attackSound"]!);
        Assert.Equal(-2, (int)second["actionSound"]!);
        Assert.Equal(12, (int)second["magicSound"]!);
        Assert.Equal(13, (int)second["deathSound"]!);
        Assert.Equal(-3, (int)second["callSound"]!);
        Assert.Equal(60, (int)second["health"]!);
        Assert.Equal(5, (int)second["exp"]!);
        Assert.Equal(10, (int)second["cash"]!);
        Assert.Equal(3, (int)second["level"]!);
        Assert.Equal(42, (int)second["magicId"]!);
        Assert.Equal(25, (int)second["magicRate"]!);
        Assert.Equal(9, (int)second["attackEquivItemId"]!);
        Assert.Equal(40, (int)second["attackEquivItemRate"]!);
        Assert.Equal(0, (int)second["stealItemId"]!);
        Assert.Equal(0, (int)second["stealItemCount"]!);
        Assert.Equal(20, (int)second["attackStrength"]!);
        Assert.Equal(12, (int)second["magicStrength"]!);
        Assert.Equal(14, (int)second["defense"]!);
        Assert.Equal(18, (int)second["dexterity"]!);
        Assert.Equal(50, (int)second["fleeRate"]!);
        Assert.Equal(3, (int)second["poisonResistance"]!);
        var resistances = second["elementalResistance"]!.AsArray();
        Assert.Equal(5, resistances.Count);
        for (int index = 0; index < 5; index++)
            Assert.Equal(index, (int)resistances[index]!);
        Assert.Equal(5, (int)second["physicalResistance"]!);
        Assert.Equal(0, (int)second["dualMove"]!);
        Assert.Equal(0, (int)second["collectValue"]!);
    }

    [Fact]
    public void DescribeIndexEntry_reports_the_source_chunk_and_hand_verified_sha256()
    {
        byte[] chunk = new byte[70]; // one all-zero record
        var entry = DataTableSchema.DescribeIndexEntry(Table, chunk);
        Assert.Equal("enemy.json", (string)entry["file"]!);
        Assert.Equal("DATA.MKF", (string)entry["archive"]!);
        Assert.Equal(1, (int)entry["chunk"]!);
        Assert.Equal(1, (int)entry["recordCount"]!);
        Assert.Equal(70, (int)entry["byteLength"]!);
        // Independent expectation: hand-verified SHA-256 of 70 zero bytes (Python
        // hashlib and PowerShell SHA256.Create both agree on this constant).
        Assert.Equal("82fcfd5215175da9e65ca7c4fb927a1fb0e61f09d54987c368e8e16ebd9c2969", (string)entry["sha256"]!);
        // Cross-check against an in-test hash of the two hand-authored records.
        var two = DataTableSchema.DescribeIndexEntry(Table, [.. Record1, .. Record2]);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData([.. Record1, .. Record2])), (string)two["sha256"]!);
        Assert.Equal(2, (int)two["recordCount"]!);
        Assert.Equal(140, (int)two["byteLength"]!);
    }

    [Fact]
    public void Decode_rejects_chunk_1_lengths_that_are_not_a_positive_multiple_of_70()
    {
        var error = Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, new byte[71]));
        Assert.Contains("DATA.MKF", error.Message);
        Assert.Contains("chunk 1", error.Message);
        Assert.Contains("71", error.Message);
        Assert.Contains("70", error.Message);
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DescribeIndexEntry(Table, new byte[71]));
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, Array.Empty<byte>())); // empty chunk is not a table
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, new byte[140 + 1])); // a trailing partial record is still not a table
    }

    [Fact]
    public void ValidateDocument_accepts_decoded_records_and_rejects_unknown_fields()
    {
        DataTableSchema.ValidateDocument(Table, DataTableSchema.DecodeTable(Table, Record1)); // freshly decoded passes

        var unknown = DataTableSchema.DecodeTable(Table, Record1);
        unknown["records"]![0]!["attackEquivItem"] = 1; // C name, not the frozen JSON name
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, unknown));

        var missing = DataTableSchema.DecodeTable(Table, Record1);
        missing["records"]![0]!.AsObject().Remove("deathSound");
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, missing));
    }

    [Fact]
    public void ValidateDocument_rejects_out_of_range_word_and_signed_values_per_field()
    {
        foreach ((string field, int value) in new[] { ("health", -1), ("health", 65_536), ("magicId", -1), ("collectValue", 65_536) })
        {
            var document = DataTableSchema.DecodeTable(Table, Record1);
            document["records"]![0]![field] = value;
            Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, document));
        }
        foreach ((string field, int value) in new[] { ("attackSound", -32_769), ("attackSound", 32_768), ("magicSound", -32_769), ("callSound", 32_768) })
        {
            var document = DataTableSchema.DecodeTable(Table, Record1);
            document["records"]![0]![field] = value;
            Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, document));
        }
    }

    [Fact]
    public void ValidateDocument_rejects_wrong_elemental_resistance_array_shapes()
    {
        foreach (int length in new[] { 4, 6 }) // NUM_MAGIC_ELEMENTAL is exactly 5
        {
            var shape = DataTableSchema.DecodeTable(Table, Record1);
            shape["records"]![0]!["elementalResistance"] = new JsonArray(Enumerable.Range(1, length).Select(value => (JsonNode)value).ToArray());
            Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, shape));
        }
    }

    [Fact]
    public void RejectDuplicateKeys_rejects_duplicate_keys_in_an_enemy_document()
    {
        // Duplicate keys cannot survive JsonNode round-trips, so the strict gate works on the
        // parsed document model (shared counterexample path used by the events contract).
        using var duplicated = JsonDocument.Parse(
            """{ "formatVersion": 1, "records": [ { "health": 60, "health": 60 } ] }""");
        Assert.Throws<InvalidDataException>(() => StrictJson.RejectDuplicateKeys(duplicated.RootElement, "enemy.json"));

        using var clean = JsonDocument.Parse("""{ "formatVersion": 1, "records": [ { "health": 60 } ] }""");
        StrictJson.RejectDuplicateKeys(clean.RootElement, "enemy.json"); // must not throw
    }
}
