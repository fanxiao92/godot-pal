using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pal.Extract;
using Xunit;

namespace Pal.Extract.Tests;

// Event-object table (SSS.MKF chunk 0, EVENTOBJECT, 32 bytes) - #79. Expected values
// are hand-computed from independently authored little-endian byte literals; the
// per-field schema is the frozen table in docs/design/data-schema.md, which mirrors
// reference/sdlpal/global.h. Boundary words: 0, 65535, 0x8000 (as -32768 or 32768
// depending on the field's signedness), 32767, -1, and script.c's -15 vanish time.
public sealed class EventObjectTableTests
{
    private static readonly DataTableDescriptor Table = DataTables.EventObject;

    // One hand-authored record per line; -32768 encodes as 0x8000, -1 as 0xFFFF.
    private static byte[] Record1 => Words(-32768, 0x1234, 0, -1, 0x8000, 65535, unchecked((short)0x8000), 8, 0xABCD, 0, 3, 32767, 1, 65535, 2, 32768);
    private static byte[] Record2 => Words(-15, 1840, 1200, 2, 10000, 0, 32767, 5, 0x1234, 65535, 0, 0, 0, 0, 4660, 0);

    private static byte[] Words(params int[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), unchecked((ushort)values[i]));
        return bytes;
    }

    [Fact]
    public void Descriptor_declares_the_frozen_sixteen_word_layout()
    {
        Assert.Equal("event-object", Table.FileId);
        Assert.Equal("SSS.MKF", Table.Archive);
        Assert.Equal(0, Table.Chunk);
        Assert.Equal(32, Table.RecordByteSize);
        Assert.Equal(16, Table.Fields.Length);
        Assert.Equal(new[]
        {
            "vanishTime", "x", "y", "layer", "triggerScript", "autoScript", "state", "triggerMode",
            "spriteNum", "spriteFrames", "direction", "currentFrameNum", "scriptIdleFrame",
            "spritePtrOffset", "spriteFramesAuto", "scriptIdleFrameCountAuto",
        }, Table.Fields.Select(field => field.Name));
        // Only the three SHORT members of global.h (sVanishTime/sLayer/sState) decode signed.
        Assert.Equal(new[] { "vanishTime", "layer", "state" },
            Table.Fields.Where(field => field.Type == DataFieldType.SignedWord).Select(field => field.Name));
    }

    [Fact]
    public void Decode_reads_little_endian_boundary_values_from_two_records()
    {
        byte[] chunk = [.. Record1, .. Record2];
        var document = DataTableSchema.DecodeTable(Table, chunk);
        Assert.Equal(1, (int)document["formatVersion"]!);
        Assert.Equal("faithful", (string)document["profile"]!);
        Assert.Equal("event-object", (string)document["fileId"]!);
        Assert.Equal(2, document["records"]!.AsArray().Count);

        // Little-endian proof straight from the byte literals: x = 0x1234 is 34 12.
        Assert.Equal(0x34, chunk[2]);
        Assert.Equal(0x12, chunk[3]);

        var first = document["records"]![0]!.AsObject();
        Assert.Equal(-32768, (int)first["vanishTime"]!);            // 0x8000 as SHORT
        Assert.Equal(0x1234, (int)first["x"]!);                     // bytes 34 12
        Assert.Equal(0, (int)first["y"]!);
        Assert.Equal(-1, (int)first["layer"]!);                     // 0xFFFF as SHORT
        Assert.Equal(32768, (int)first["triggerScript"]!);          // 0x8000 as WORD
        Assert.Equal(65535, (int)first["autoScript"]!);
        Assert.Equal(-32768, (int)first["state"]!);                 // 0x8000 as SHORT
        Assert.Equal(8, (int)first["triggerMode"]!);
        Assert.Equal(0xABCD, (int)first["spriteNum"]!);
        Assert.Equal(0, (int)first["spriteFrames"]!);
        Assert.Equal(3, (int)first["direction"]!);
        Assert.Equal(32767, (int)first["currentFrameNum"]!);
        Assert.Equal(1, (int)first["scriptIdleFrame"]!);
        Assert.Equal(65535, (int)first["spritePtrOffset"]!);
        Assert.Equal(2, (int)first["spriteFramesAuto"]!);
        Assert.Equal(32768, (int)first["scriptIdleFrameCountAuto"]!);

        var second = document["records"]![1]!.AsObject();
        Assert.Equal(-15, (int)second["vanishTime"]!);              // script.c 0x004B writes -15
        Assert.Equal(1840, (int)second["x"]!);
        Assert.Equal(1200, (int)second["y"]!);
        Assert.Equal(2, (int)second["layer"]!);
        Assert.Equal(10000, (int)second["triggerScript"]!);
        Assert.Equal(0, (int)second["autoScript"]!);
        Assert.Equal(32767, (int)second["state"]!);
        Assert.Equal(5, (int)second["triggerMode"]!);
        Assert.Equal(0x1234, (int)second["spriteNum"]!);
        Assert.Equal(65535, (int)second["spriteFrames"]!);
        Assert.Equal(0, (int)second["direction"]!);
        Assert.Equal(0, (int)second["currentFrameNum"]!);
        Assert.Equal(0, (int)second["scriptIdleFrame"]!);
        Assert.Equal(0, (int)second["spritePtrOffset"]!);
        Assert.Equal(4660, (int)second["spriteFramesAuto"]!);
        Assert.Equal(0, (int)second["scriptIdleFrameCountAuto"]!);
    }

    [Fact]
    public void DescribeIndexEntry_reports_the_source_chunk_and_hand_verified_sha256()
    {
        byte[] chunk = new byte[32]; // one all-zero record
        var entry = DataTableSchema.DescribeIndexEntry(Table, chunk);
        Assert.Equal("event-object.json", (string)entry["file"]!);
        Assert.Equal("SSS.MKF", (string)entry["archive"]!);
        Assert.Equal(0, (int)entry["chunk"]!);
        Assert.Equal(1, (int)entry["recordCount"]!);
        Assert.Equal(32, (int)entry["byteLength"]!);
        // Independent expectation: hand-verified SHA-256 of 32 zero bytes.
        Assert.Equal("66687aadf862bd776c8fc18b8e9f8e20089714856ee233b3902a591d0d5f2925", (string)entry["sha256"]!);
        // Cross-check against an in-test hash of the two hand-authored records.
        var two = DataTableSchema.DescribeIndexEntry(Table, [.. Record1, .. Record2]);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData([.. Record1, .. Record2])), (string)two["sha256"]!);
        Assert.Equal(2, (int)two["recordCount"]!);
        Assert.Equal(64, (int)two["byteLength"]!);
    }

    [Fact]
    public void Decode_rejects_chunk_0_lengths_that_are_not_a_positive_multiple_of_32()
    {
        var error = Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, new byte[33]));
        Assert.Contains("SSS.MKF", error.Message);
        Assert.Contains("chunk 0", error.Message);
        Assert.Contains("33", error.Message);
        Assert.Contains("32", error.Message);
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DescribeIndexEntry(Table, new byte[33]));
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, Array.Empty<byte>())); // empty chunk is not a table
    }

    [Fact]
    public void ValidateDocument_accepts_decoded_records_and_rejects_unknown_fields()
    {
        DataTableSchema.ValidateDocument(Table, DataTableSchema.DecodeTable(Table, Record1)); // freshly decoded passes

        var unknown = DataTableSchema.DecodeTable(Table, Record1);
        unknown["records"]![0]!["spriteFramesTotal"] = 1;
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, unknown));

        var missing = DataTableSchema.DecodeTable(Table, Record1);
        missing["records"]![0]!.AsObject().Remove("vanishTime");
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, missing));
    }

    [Fact]
    public void ValidateDocument_rejects_out_of_range_word_and_signed_values_per_field()
    {
        foreach ((string field, int value) in new[] { ("x", -1), ("x", 65_536), ("triggerScript", -1), ("spriteNum", 65_536) })
        {
            var document = DataTableSchema.DecodeTable(Table, Record1);
            document["records"]![0]![field] = value;
            Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, document));
        }
        foreach ((string field, int value) in new[] { ("vanishTime", -32_769), ("vanishTime", 32_768), ("layer", -32_769), ("state", 32_768) })
        {
            var document = DataTableSchema.DecodeTable(Table, Record1);
            document["records"]![0]![field] = value;
            Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, document));
        }
    }

    [Fact]
    public void RejectDuplicateKeys_rejects_duplicate_keys_in_an_event_object_document()
    {
        // Duplicate keys cannot survive JsonNode round-trips, so the strict gate works on the
        // parsed document model (shared counterexample path used by the events contract).
        using var duplicated = JsonDocument.Parse(
            """{ "formatVersion": 1, "records": [ { "vanishTime": -15, "vanishTime": -15 } ] }""");
        Assert.Throws<InvalidDataException>(() => StrictJson.RejectDuplicateKeys(duplicated.RootElement, "event-object.json"));

        using var clean = JsonDocument.Parse("""{ "formatVersion": 1, "records": [ { "vanishTime": -15 } ] }""");
        StrictJson.RejectDuplicateKeys(clean.RootElement, "event-object.json"); // must not throw
    }
}
