using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pal.Extract;
using Xunit;

namespace Pal.Extract.Tests;

// Scene table (SSS.MKF chunk 1, SCENE, 8 bytes) - #80. Expected values are
// hand-computed from independently authored little-endian byte literals; the
// per-field schema is the frozen table in docs/design/data-schema.md, which mirrors
// reference/sdlpal/global.h. Boundary words: 0, 65535, 0x8000 (32768 unsigned - unlike
// EVENTOBJECT, SCENE has no SHORT member, so every field stays unsigned), and the
// real-data total event-object count 5077 as an eventObjectIndex sample.
public sealed class SceneTableTests
{
    private static readonly DataTableDescriptor Table = DataTables.Scene;

    // One hand-authored record per line: WORDs are stored little-endian.
    private static byte[] Record1 => Words(65535, 0x1234, 0, 65535);
    private static byte[] Record2 => Words(1, 32768, 0xABCD, 5077);

    private static byte[] Words(params int[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), unchecked((ushort)values[i]));
        return bytes;
    }

    [Fact]
    public void Descriptor_declares_the_frozen_four_word_layout()
    {
        Assert.Equal("scene", Table.FileId);
        Assert.Equal("SSS.MKF", Table.Archive);
        Assert.Equal(1, Table.Chunk);
        Assert.Equal(8, Table.RecordByteSize);
        Assert.Equal(4, Table.Fields.Length);
        Assert.Equal(new[] { "mapNum", "scriptOnEnter", "scriptOnTeleport", "eventObjectIndex" },
            Table.Fields.Select(field => field.Name));
        // global.h declares every SCENE member as plain WORD: nothing decodes signed.
        Assert.DoesNotContain(Table.Fields, field => field.Type == DataFieldType.SignedWord);
    }

    [Fact]
    public void Decode_reads_little_endian_boundary_values_from_two_records()
    {
        byte[] chunk = [.. Record1, .. Record2];
        var document = DataTableSchema.DecodeTable(Table, chunk);
        Assert.Equal(1, (int)document["formatVersion"]!);
        Assert.Equal("faithful", (string)document["profile"]!);
        Assert.Equal("scene", (string)document["fileId"]!);
        Assert.Equal(2, document["records"]!.AsArray().Count);

        // Little-endian proof straight from the byte literals: scriptOnEnter = 0x1234 is 34 12.
        Assert.Equal(0x34, chunk[2]);
        Assert.Equal(0x12, chunk[3]);

        var first = document["records"]![0]!.AsObject();
        Assert.Equal(65535, (int)first["mapNum"]!);
        Assert.Equal(0x1234, (int)first["scriptOnEnter"]!);            // bytes 34 12
        Assert.Equal(0, (int)first["scriptOnTeleport"]!);
        Assert.Equal(65535, (int)first["eventObjectIndex"]!);

        var second = document["records"]![1]!.AsObject();
        Assert.Equal(1, (int)second["mapNum"]!);
        Assert.Equal(32768, (int)second["scriptOnEnter"]!);            // 0x8000 as unsigned WORD
        Assert.Equal(0xABCD, (int)second["scriptOnTeleport"]!);        // bytes cd ab
        Assert.Equal(5077, (int)second["eventObjectIndex"]!);          // 0x13D5, real event-object count
    }

    [Fact]
    public void DescribeIndexEntry_reports_the_source_chunk_and_hand_verified_sha256()
    {
        byte[] chunk = new byte[8]; // one all-zero record
        var entry = DataTableSchema.DescribeIndexEntry(Table, chunk);
        Assert.Equal("scene.json", (string)entry["file"]!);
        Assert.Equal("SSS.MKF", (string)entry["archive"]!);
        Assert.Equal(1, (int)entry["chunk"]!);
        Assert.Equal(1, (int)entry["recordCount"]!);
        Assert.Equal(8, (int)entry["byteLength"]!);
        // Independent expectation: hand-verified SHA-256 of 8 zero bytes (Python hashlib
        // and PowerShell SHA256.Create both agree on this constant).
        Assert.Equal("af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc", (string)entry["sha256"]!);
        // Cross-check against an in-test hash of the two hand-authored records.
        var two = DataTableSchema.DescribeIndexEntry(Table, [.. Record1, .. Record2]);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData([.. Record1, .. Record2])), (string)two["sha256"]!);
        Assert.Equal(2, (int)two["recordCount"]!);
        Assert.Equal(16, (int)two["byteLength"]!);
    }

    [Fact]
    public void Decode_rejects_chunk_1_lengths_that_are_not_a_positive_multiple_of_8()
    {
        var error = Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, new byte[9]));
        Assert.Contains("SSS.MKF", error.Message);
        Assert.Contains("chunk 1", error.Message);
        Assert.Contains("9", error.Message);
        Assert.Contains("8", error.Message);
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DescribeIndexEntry(Table, new byte[9]));
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, Array.Empty<byte>())); // empty chunk is not a table
    }

    [Fact]
    public void ValidateDocument_accepts_decoded_records_and_rejects_unknown_fields()
    {
        DataTableSchema.ValidateDocument(Table, DataTableSchema.DecodeTable(Table, Record1)); // freshly decoded passes

        var unknown = DataTableSchema.DecodeTable(Table, Record1);
        unknown["records"]![0]!["nextEventObjectIndex"] = 1;
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, unknown));

        var missing = DataTableSchema.DecodeTable(Table, Record1);
        missing["records"]![0]!.AsObject().Remove("mapNum");
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, missing));
    }

    [Fact]
    public void ValidateDocument_rejects_out_of_range_word_values_per_field()
    {
        foreach ((string field, int value) in new[]
                 { ("mapNum", -1), ("mapNum", 65_536), ("scriptOnEnter", -1), ("scriptOnTeleport", 65_536), ("eventObjectIndex", -1) })
        {
            var document = DataTableSchema.DecodeTable(Table, Record1);
            document["records"]![0]![field] = value;
            Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, document));
        }
    }

    [Fact]
    public void RejectDuplicateKeys_rejects_duplicate_keys_in_a_scene_document()
    {
        // Duplicate keys cannot survive JsonNode round-trips, so the strict gate works on the
        // parsed document model (shared counterexample path used by the events contract).
        using var duplicated = JsonDocument.Parse(
            """{ "formatVersion": 1, "records": [ { "mapNum": 1, "mapNum": 1 } ] }""");
        Assert.Throws<InvalidDataException>(() => StrictJson.RejectDuplicateKeys(duplicated.RootElement, "scene.json"));

        using var clean = JsonDocument.Parse("""{ "formatVersion": 1, "records": [ { "mapNum": 1 } ] }""");
        StrictJson.RejectDuplicateKeys(clean.RootElement, "scene.json"); // must not throw
    }
}
