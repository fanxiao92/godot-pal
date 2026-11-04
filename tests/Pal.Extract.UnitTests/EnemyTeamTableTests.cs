using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pal.Extract;
using Xunit;

namespace Pal.Extract.Tests;

// Enemy-team table (DATA.MKF chunk 2, ENEMYTEAM, 10 bytes) - #85. Expected values
// are hand-computed from independently authored little-endian byte literals; the
// per-field schema is the frozen table in docs/design/data-schema.md, which mirrors
// reference/sdlpal/global.h (rgwEnemy[MAX_ENEMIES_IN_TEAM] is the single member, a
// plain WORD array - no SHORT member, so every element stays unsigned). Boundary
// words: 0, 65535, 0x8000, 32767, and small object indices; each element is an
// object.json enemy-view index where 0 = empty slot and 0xFFFF = skip
// (battle.c:1601-1615), but that reference semantics only bounds elements against
// the object table, not the WORD range enforced here.
public sealed class EnemyTeamTableTests
{
    private static readonly DataTableDescriptor Table = DataTables.EnemyTeam;

    // One hand-authored record per line: WORDs are stored little-endian.
    private static byte[] Record1 => Words(65535, 0x1234, 0, 32768, 0xABCD);
    private static byte[] Record2 => Words(32767, 0, 4660, 0, 380);

    private static byte[] Words(params int[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), unchecked((ushort)values[i]));
        return bytes;
    }

    [Fact]
    public void Descriptor_declares_the_frozen_five_word_array_layout()
    {
        Assert.Equal("enemy-team", Table.FileId);
        Assert.Equal("DATA.MKF", Table.Archive);
        Assert.Equal(2, Table.Chunk);
        Assert.Equal(10, Table.RecordByteSize);
        Assert.Single(Table.Fields); // one array member: rgwEnemy[MAX_ENEMIES_IN_TEAM]
        Assert.Equal("enemies", Table.Fields[0].Name);
        Assert.Equal(new[] { 5 }, Table.Fields[0].Dimensions); // rgwEnemy[MAX_ENEMIES_IN_TEAM], palcommon.h:60
        // global.h declares the only member as a plain WORD array: nothing decodes signed.
        Assert.DoesNotContain(Table.Fields, field => field.Type == DataFieldType.SignedWord);
    }

    [Fact]
    public void Decode_reads_little_endian_boundary_values_from_two_records()
    {
        byte[] chunk = [.. Record1, .. Record2];
        var document = DataTableSchema.DecodeTable(Table, chunk);
        Assert.Equal(1, (int)document["formatVersion"]!);
        Assert.Equal("faithful", (string)document["profile"]!);
        Assert.Equal("enemy-team", (string)document["fileId"]!);
        Assert.Equal(2, document["records"]!.AsArray().Count);

        // Little-endian proof straight from the byte literals: enemies[1] = 0x1234 is 34 12.
        Assert.Equal(0x34, chunk[2]);
        Assert.Equal(0x12, chunk[3]);

        var first = document["records"]![0]!["enemies"]!.AsArray();
        Assert.Equal(5, first.Count);
        Assert.Equal(65535, (int)first[0]!);              // 0xFFFF skip boundary
        Assert.Equal(0x1234, (int)first[1]!);             // bytes 34 12
        Assert.Equal(0, (int)first[2]!);                  // empty slot
        Assert.Equal(32768, (int)first[3]!);              // 0x8000 as unsigned WORD
        Assert.Equal(0xABCD, (int)first[4]!);             // bytes cd ab

        var second = document["records"]![1]!["enemies"]!.AsArray();
        Assert.Equal(5, second.Count);
        Assert.Equal(32767, (int)second[0]!);
        Assert.Equal(0, (int)second[1]!);
        Assert.Equal(4660, (int)second[2]!);              // 0x1234, bytes 34 12
        Assert.Equal(0, (int)second[3]!);
        Assert.Equal(380, (int)second[4]!);               // real-data team count
    }

    [Fact]
    public void DescribeIndexEntry_reports_the_source_chunk_and_hand_verified_sha256()
    {
        byte[] chunk = new byte[10]; // one all-zero record (five empty slots)
        var entry = DataTableSchema.DescribeIndexEntry(Table, chunk);
        Assert.Equal("enemy-team.json", (string)entry["file"]!);
        Assert.Equal("DATA.MKF", (string)entry["archive"]!);
        Assert.Equal(2, (int)entry["chunk"]!);
        Assert.Equal(1, (int)entry["recordCount"]!);
        Assert.Equal(10, (int)entry["byteLength"]!);
        // Independent expectation: hand-verified SHA-256 of 10 zero bytes (Python
        // hashlib and PowerShell SHA256.Create both agree on this constant).
        Assert.Equal("01d448afd928065458cf670b60f5a594d735af0172c8d67f22a81680132681ca", (string)entry["sha256"]!);
        // Cross-check against an in-test hash of the two hand-authored records.
        var two = DataTableSchema.DescribeIndexEntry(Table, [.. Record1, .. Record2]);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData([.. Record1, .. Record2])), (string)two["sha256"]!);
        Assert.Equal(2, (int)two["recordCount"]!);
        Assert.Equal(20, (int)two["byteLength"]!);
    }

    [Fact]
    public void Decode_rejects_chunk_2_lengths_that_are_not_a_positive_multiple_of_10()
    {
        var error = Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, new byte[11]));
        Assert.Contains("DATA.MKF", error.Message);
        Assert.Contains("chunk 2", error.Message);
        Assert.Contains("11", error.Message);
        Assert.Contains("10", error.Message);
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DescribeIndexEntry(Table, new byte[11]));
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, Array.Empty<byte>())); // empty chunk is not a table
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, new byte[20 + 1])); // a trailing partial record is still not a table
    }

    [Fact]
    public void ValidateDocument_accepts_decoded_records_and_rejects_unknown_fields()
    {
        DataTableSchema.ValidateDocument(Table, DataTableSchema.DecodeTable(Table, Record1)); // freshly decoded passes

        var unknown = DataTableSchema.DecodeTable(Table, Record1);
        unknown["records"]![0]!["rgwEnemy"] = new JsonArray(0, 0, 0, 0, 0); // C name, not the frozen JSON name
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, unknown));

        var missing = DataTableSchema.DecodeTable(Table, Record1);
        missing["records"]![0]!.AsObject().Remove("enemies");
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, missing));
    }

    [Fact]
    public void ValidateDocument_rejects_out_of_range_enemy_values_and_wrong_array_shapes()
    {
        foreach (int value in new[] { -1, 65_536 })
        {
            var document = DataTableSchema.DecodeTable(Table, Record1);
            document["records"]![0]!["enemies"]![0] = value;
            Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, document));
        }

        foreach (int length in new[] { 4, 6 }) // MAX_ENEMIES_IN_TEAM is exactly 5 slots
        {
            var shape = DataTableSchema.DecodeTable(Table, Record1);
            shape["records"]![0]!["enemies"] = new JsonArray(Enumerable.Range(1, length).Select(value => (JsonNode)value).ToArray());
            Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, shape));
        }
    }

    [Fact]
    public void RejectDuplicateKeys_rejects_duplicate_keys_in_an_enemy_team_document()
    {
        // Duplicate keys cannot survive JsonNode round-trips, so the strict gate works on the
        // parsed document model (shared counterexample path used by the events contract).
        using var duplicated = JsonDocument.Parse(
            """{ "formatVersion": 1, "records": [ { "enemies": [0], "enemies": [0] } ] }""");
        Assert.Throws<InvalidDataException>(() => StrictJson.RejectDuplicateKeys(duplicated.RootElement, "enemy-team.json"));

        using var clean = JsonDocument.Parse("""{ "formatVersion": 1, "records": [ { "enemies": [0] } ] }""");
        StrictJson.RejectDuplicateKeys(clean.RootElement, "enemy-team.json"); // must not throw
    }
}
