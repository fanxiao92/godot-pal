using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pal.Extract;
using Xunit;

namespace Pal.Extract.Tests;

// In-memory self-proof of the shared data-table skeleton with a synthetic descriptor:
// deliberately NOT one of the 13 real tables (those belong to #79-#91). Expected
// values are hand-computed from independently authored little-endian byte literals.
public sealed class DataTableSchemaTests
{
    // Synthetic table: { code WORD, delta SHORT, tags WORD[2] } = 8 bytes per record.
    private static readonly DataTableDescriptor Demo = new(
        "demo-widget", "TEST.MKF", 3,
        [new DataField("code", DataFieldType.Word), new DataField("delta", DataFieldType.SignedWord), new DataField("tags", DataFieldType.Word, [2])]);

    private static byte[] Words(params int[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), unchecked((ushort)values[i]));
        return bytes;
    }

    [Fact]
    public void Decode_reads_little_endian_words_signed_values_and_array_dimensions()
    {
        // Byte-swapped-looking pairs prove little-endian interpretation: 0x34,0x12 -> 0x1234.
        byte[] chunk = [0x34, 0x12, 0xFE, 0xFF, 0x01, 0x00, 0x02, 0x00];
        var document = DataTableSchema.DecodeTable(Demo, chunk);
        Assert.Equal(1, (int)document["formatVersion"]!);
        Assert.Equal("faithful", (string)document["profile"]!);
        Assert.Equal("demo-widget", (string)document["fileId"]!);
        var record = document["records"]![0]!.AsObject();
        Assert.Equal(0x1234, (int)record["code"]!);
        Assert.Equal(-2, (int)record["delta"]!); // 0xFFFE as signed WORD
        Assert.Equal(new[] { 1, 2 }, record["tags"]!.AsArray().Select(node => node!.GetValue<int>()));
    }

    [Fact]
    public void DescribeIndexEntry_records_source_chunk_record_count_length_and_sha256()
    {
        byte[] chunk = [.. Words(7, 300, 1, 2), .. Words(9, 65_535, 3, 4)]; // two 8-byte records
        var entry = DataTableSchema.DescribeIndexEntry(Demo, chunk);
        Assert.Equal("demo-widget.json", (string)entry["file"]!);
        Assert.Equal("TEST.MKF", (string)entry["archive"]!);
        Assert.Equal(3, (int)entry["chunk"]!);
        Assert.Equal(2, (int)entry["recordCount"]!);
        Assert.Equal(16, (int)entry["byteLength"]!);
        // Independent expectation: hash the literal bytes again in the test.
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(chunk)), (string)entry["sha256"]!);
    }

    [Fact]
    public void Decode_rejects_byte_lengths_not_divisible_by_the_record_size()
    {
        byte[] chunk = Words(1, 2, 3, 4, 5); // 10 bytes: not a multiple of 8
        var error = Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Demo, chunk));
        Assert.Contains("TEST.MKF", error.Message);
        Assert.Contains("chunk 3", error.Message);
        Assert.Contains("10", error.Message);
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DescribeIndexEntry(Demo, chunk));
    }

    [Fact]
    public void ValidateDocument_accepts_a_freshly_decoded_document()
    {
        var document = DataTableSchema.DecodeTable(Demo, Words(1, -1, 2, 3));
        DataTableSchema.ValidateDocument(Demo, document); // must not throw
    }

    [Fact]
    public void ValidateDocument_rejects_unknown_and_missing_fields()
    {
        var document = DataTableSchema.DecodeTable(Demo, Words(1, 0, 0, 0));
        document["records"]![0]!["bogus"] = 1;
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Demo, document));

        var missing = DataTableSchema.DecodeTable(Demo, Words(1, 0, 0, 0));
        missing["records"]![0]!.AsObject().Remove("delta");
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Demo, missing));
    }

    [Fact]
    public void ValidateDocument_rejects_out_of_range_word_and_signed_values()
    {
        foreach (int value in new[] { -1, 65_536 })
        {
            var document = DataTableSchema.DecodeTable(Demo, Words(1, 0, 0, 0));
            document["records"]![0]!["code"] = value;
            Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Demo, document));
        }
        foreach (int value in new[] { -32_769, 32_768 })
        {
            var document = DataTableSchema.DecodeTable(Demo, Words(1, 0, 0, 0));
            document["records"]![0]!["delta"] = value;
            Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Demo, document));
        }
        var negativeArrayElement = DataTableSchema.DecodeTable(Demo, Words(1, 0, 0, 0));
        negativeArrayElement["records"]![0]!["tags"]![0] = -1;
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Demo, negativeArrayElement));
    }

    [Fact]
    public void ValidateDocument_rejects_wrong_version_profile_file_id_and_array_shape()
    {
        var version = DataTableSchema.DecodeTable(Demo, Words(1, 0, 0, 0));
        version["formatVersion"] = 2;
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Demo, version));

        var profile = DataTableSchema.DecodeTable(Demo, Words(1, 0, 0, 0));
        profile["profile"] = "loose";
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Demo, profile));

        var identifier = DataTableSchema.DecodeTable(Demo, Words(1, 0, 0, 0));
        identifier["fileId"] = "enemy";
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Demo, identifier));

        var shape = DataTableSchema.DecodeTable(Demo, Words(1, 0, 0, 0));
        shape["records"]![0]!["tags"] = new JsonArray(1);
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Demo, shape));
    }

    [Fact]
    public void RejectDuplicateKeys_rejects_duplicate_keys_anywhere_in_the_document()
    {
        // Duplicate keys cannot survive JsonNode round-trips, so the strict check works
        // on the parsed document model; this is the shared duplicate-key gate.
        using var document = JsonDocument.Parse("""{ "formatVersion": 1, "formatVersion": 1, "records": [ { "code": 1, "code": 2 } ] }""");
        Assert.Throws<InvalidDataException>(() => StrictJson.RejectDuplicateKeys(document.RootElement, "demo-widget.json"));

        using var clean = JsonDocument.Parse("""{ "formatVersion": 1, "records": [ { "code": 1 } ] }""");
        StrictJson.RejectDuplicateKeys(clean.RootElement, "demo-widget.json"); // must not throw
    }
}
