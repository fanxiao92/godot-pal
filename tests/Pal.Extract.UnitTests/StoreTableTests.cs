using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pal.Extract;
using Xunit;
using static Pal.Extract.Tests.OpcodeCounterexampleHarness;

namespace Pal.Extract.Tests;

// Store table (DATA.MKF chunk 0, STORE, 18 bytes) - #83. Expected values are
// hand-computed from independently authored little-endian byte literals; the
// per-field schema is the frozen table in docs/design/data-schema.md, which mirrors
// reference/sdlpal/global.h (rgwItems[MAX_STORE_ITEM] is the single member, a plain
// WORD array - no SHORT member, so every element stays unsigned). Boundary words:
// 0, 65535, 0x8000 (32768 unsigned), 32767, and small object indices; each element is
// an object.json item-view index where 0 = empty slot (uigame.c:1644), but that
// reference semantics only bounds elements against the object table, not the WORD
// range enforced here.
public sealed class StoreTableTests
{
    private static readonly DataTableDescriptor Table = DataTables.Store;

    // One hand-authored record per line: WORDs are stored little-endian.
    private static byte[] Record1 => Words(65535, 0x1234, 0, 65535, 1, 2, 3, 32768, 0xABCD);
    private static byte[] Record2 => Words(32767, 0, 4660, 0, 0, 0, 0, 0, 21);

    [Fact]
    public void Descriptor_declares_the_frozen_nine_word_array_layout()
    {
        Assert.Equal("store", Table.FileId);
        Assert.Equal("DATA.MKF", Table.Archive);
        Assert.Equal(0, Table.Chunk);
        Assert.Equal(18, Table.RecordByteSize);
        Assert.Single(Table.Fields); // one array member: rgwItems[MAX_STORE_ITEM]
        Assert.Equal("items", Table.Fields[0].Name);
        Assert.Equal(new[] { 9 }, Table.Fields[0].Dimensions); // rgwItems[MAX_STORE_ITEM], palcommon.h:54
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
        Assert.Equal("store", (string)document["fileId"]!);
        Assert.Equal(2, document["records"]!.AsArray().Count);

        // Little-endian proof straight from the byte literals: items[1] = 0x1234 is 34 12.
        Assert.Equal(0x34, chunk[2]);
        Assert.Equal(0x12, chunk[3]);

        var first = document["records"]![0]!["items"]!.AsArray();
        Assert.Equal(9, first.Count);
        Assert.Equal(65535, (int)first[0]!);              // 0xFFFF boundary
        Assert.Equal(0x1234, (int)first[1]!);             // bytes 34 12
        Assert.Equal(0, (int)first[2]!);                  // empty slot
        Assert.Equal(65535, (int)first[3]!);
        Assert.Equal(1, (int)first[4]!);
        Assert.Equal(2, (int)first[5]!);
        Assert.Equal(3, (int)first[6]!);
        Assert.Equal(32768, (int)first[7]!);              // 0x8000 as unsigned WORD
        Assert.Equal(0xABCD, (int)first[8]!);             // bytes cd ab

        var second = document["records"]![1]!["items"]!.AsArray();
        Assert.Equal(9, second.Count);
        Assert.Equal(32767, (int)second[0]!);
        Assert.Equal(0, (int)second[1]!);
        Assert.Equal(4660, (int)second[2]!);              // 0x1234, bytes 34 12
        Assert.Equal(0, (int)second[3]!);
        Assert.Equal(0, (int)second[4]!);
        Assert.Equal(0, (int)second[5]!);
        Assert.Equal(0, (int)second[6]!);
        Assert.Equal(0, (int)second[7]!);
        Assert.Equal(21, (int)second[8]!);                // real-data store count
    }

    [Fact]
    public void DescribeIndexEntry_reports_the_source_chunk_and_hand_verified_sha256()
    {
        byte[] chunk = new byte[18]; // one all-zero record
        var entry = DataTableSchema.DescribeIndexEntry(Table, chunk);
        Assert.Equal("store.json", (string)entry["file"]!);
        Assert.Equal("DATA.MKF", (string)entry["archive"]!);
        Assert.Equal(0, (int)entry["chunk"]!);
        Assert.Equal(1, (int)entry["recordCount"]!);
        Assert.Equal(18, (int)entry["byteLength"]!);
        // Independent expectation: hand-verified SHA-256 of 18 zero bytes (Python
        // hashlib and PowerShell SHA256.Create both agree on this constant).
        Assert.Equal("60daa3a5f7dbfa200f8c82840ecf5b42640b70f3b7218a4c6bbd67db542e75a4", (string)entry["sha256"]!);
        // Cross-check against an in-test hash of the two hand-authored records.
        var two = DataTableSchema.DescribeIndexEntry(Table, [.. Record1, .. Record2]);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData([.. Record1, .. Record2])), (string)two["sha256"]!);
        Assert.Equal(2, (int)two["recordCount"]!);
        Assert.Equal(36, (int)two["byteLength"]!);
    }

    [Fact]
    public void Decode_rejects_chunk_0_lengths_that_are_not_a_positive_multiple_of_18()
    {
        var error = Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, new byte[19]));
        Assert.Contains("DATA.MKF", error.Message);
        Assert.Contains("chunk 0", error.Message);
        Assert.Contains("19", error.Message);
        Assert.Contains("18", error.Message);
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DescribeIndexEntry(Table, new byte[19]));
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, Array.Empty<byte>())); // empty chunk is not a table
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, new byte[36 + 1])); // a trailing partial record is still not a table
    }

    [Fact]
    public void ValidateDocument_accepts_decoded_records_and_rejects_unknown_fields()
    {
        DataTableSchema.ValidateDocument(Table, DataTableSchema.DecodeTable(Table, Record1)); // freshly decoded passes

        var unknown = DataTableSchema.DecodeTable(Table, Record1);
        unknown["records"]![0]!["prices"] = 1;
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, unknown));

        var missing = DataTableSchema.DecodeTable(Table, Record1);
        missing["records"]![0]!.AsObject().Remove("items");
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, missing));
    }

    [Fact]
    public void ValidateDocument_rejects_out_of_range_item_values_and_wrong_array_shapes()
    {
        foreach (int value in new[] { -1, 65_536 })
        {
            var document = DataTableSchema.DecodeTable(Table, Record1);
            document["records"]![0]!["items"]![0] = value;
            Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, document));
        }

        foreach (int length in new[] { 8, 10 }) // MAX_STORE_ITEM is exactly 9 slots
        {
            var shape = DataTableSchema.DecodeTable(Table, Record1);
            shape["records"]![0]!["items"] = new JsonArray(Enumerable.Range(1, length).Select(value => (JsonNode)value).ToArray());
            Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, shape));
        }
    }

    [Fact]
    public void RejectDuplicateKeys_rejects_duplicate_keys_in_a_store_document()
    {
        // Duplicate keys cannot survive JsonNode round-trips, so the strict gate works on the
        // parsed document model (shared counterexample path used by the events contract).
        using var duplicated = JsonDocument.Parse(
            """{ "formatVersion": 1, "records": [ { "items": [0], "items": [0] } ] }""");
        Assert.Throws<InvalidDataException>(() => StrictJson.RejectDuplicateKeys(duplicated.RootElement, "store.json"));

        using var clean = JsonDocument.Parse("""{ "formatVersion": 1, "records": [ { "items": [0] } ] }""");
        StrictJson.RejectDuplicateKeys(clean.RootElement, "store.json"); // must not throw
    }
}
