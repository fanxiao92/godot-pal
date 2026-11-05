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

    // Synthetic table with a dispatched slot: { code WORD, [rgPick dispatched on code],
    // tags WORD[2], delta SHORT } = 10 bytes per record. The slot sits at word 1, on
    // the boundary after the scalar dispatch field, with two matched views and one
    // fallback view (the skeleton generalization of MAGIC's rgSpecific, #78/#88).
    private static readonly DataTableDescriptor DispatchedDemo = new(
        "demo-pick", "TEST.MKF", 5,
        [
            new DataField("code", DataFieldType.Word),
            new DataField("tags", DataFieldType.Word, [2]),
            new DataField("delta", DataFieldType.SignedWord),
        ],
        new DispatchedWordSlot("rgPick", "code", 1,
        [
            new DispatchedWordOption("alpha", DataFieldType.Word, 1),
            new DispatchedWordOption("beta", DataFieldType.SignedWord, 2),
            new DispatchedWordOption("gamma", DataFieldType.Word),
        ]));

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

    [Fact]
    public void Dispatched_slot_exports_one_view_per_record_selected_by_the_dispatch_value()
    {
        Assert.Equal(10, DispatchedDemo.RecordByteSize); // 4 field words + the dispatched word
        // Record 1: code=1 selects the matched WORD view; record 2: code=2 the matched
        // SHORT view (0xFFFF -> -1); record 3: code=9 selects the fallback WORD view.
        var document = DataTableSchema.DecodeTable(DispatchedDemo,
            [.. Words(1, 0x8000, 5, 6, -1), .. Words(2, -1, 7, 8, 0), .. Words(9, 7, 0, 0, 0)]);
        var records = document["records"]!.AsArray();
        Assert.Equal(3, records.Count);
        var first = records[0]!.AsObject();
        Assert.Equal(32768, (int)first["alpha"]!); // 0x8000 stays a WORD in this view
        Assert.False(first.ContainsKey("beta"));
        Assert.False(first.ContainsKey("gamma"));
        var second = records[1]!.AsObject();
        Assert.Equal(-1, (int)second["beta"]!);    // 0xFFFF as a SHORT in this view
        Assert.False(second.ContainsKey("alpha"));
        var third = records[2]!.AsObject();
        Assert.Equal(7, (int)third["gamma"]!);     // the fallback view
        // The exported view keeps the slot's physical position in the key order.
        Assert.Equal(new[] { "code", "alpha", "tags", "delta" }, first.Select(pair => pair.Key));
        DataTableSchema.ValidateDocument(DispatchedDemo, document); // freshly decoded passes

        // The unselected view's name is unknown on reload and the selected one required.
        var stray = DataTableSchema.DecodeTable(DispatchedDemo, Words(1, 0, 5, 6, -1));
        stray["records"]![0]!["gamma"] = 0;
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(DispatchedDemo, stray));
        var absent = DataTableSchema.DecodeTable(DispatchedDemo, Words(1, 0, 5, 6, -1));
        absent["records"]![0]!.AsObject().Remove("alpha");
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(DispatchedDemo, absent));
    }

    [Fact]
    public void Dispatched_slot_rejects_inconsistent_descriptors_and_unmatched_values()
    {
        DataField[] fields = [new DataField("code", DataFieldType.Word), new DataField("tags", DataFieldType.Word, [2]), new DataField("delta", DataFieldType.SignedWord)];
        DispatchedWordOption[] options =
        [
            new DispatchedWordOption("alpha", DataFieldType.Word, 1),
            new DispatchedWordOption("gamma", DataFieldType.Word),
        ];
        // Slot word 2 falls inside the two-word tags array: not a field boundary.
        var misaligned = new DataTableDescriptor("demo-bad", "TEST.MKF", 5, fields, new DispatchedWordSlot("rgPick", "code", 2, options));
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(misaligned, new byte[10]));
        // Dispatch field decoded after the slot: its value cannot select the view yet.
        var lateDispatch = new DataTableDescriptor("demo-bad", "TEST.MKF", 5,
            [new DataField("code", DataFieldType.Word), new DataField("delta", DataFieldType.SignedWord)],
            new DispatchedWordSlot("rgPick", "delta", 0, options));
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(lateDispatch, new byte[6]));
        // An array field cannot dispatch, and two fallback views are ambiguous.
        var arrayDispatch = new DataTableDescriptor("demo-bad", "TEST.MKF", 5, fields, new DispatchedWordSlot("rgPick", "tags", 3, options));
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(arrayDispatch, new byte[10]));
        var twoFallbacks = new DataTableDescriptor("demo-bad", "TEST.MKF", 5, fields,
            new DispatchedWordSlot("rgPick", "code", 1,
            [
                new DispatchedWordOption("gamma", DataFieldType.Word),
                new DispatchedWordOption("gamma2", DataFieldType.Word),
            ]));
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(twoFallbacks, new byte[10]));
        // A view name colliding with a field name corrupts the strict key set.
        var collision = new DataTableDescriptor("demo-bad", "TEST.MKF", 5, fields,
            new DispatchedWordSlot("rgPick", "code", 1,
            [
                new DispatchedWordOption("code", DataFieldType.Word, 1),
                new DispatchedWordOption("gamma", DataFieldType.Word),
            ]));
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(collision, new byte[10]));
        // A dispatch value with no matched view and no fallback is not decodable.
        var picky = new DataTableDescriptor("demo-bad", "TEST.MKF", 5, fields,
            new DispatchedWordSlot("rgPick", "code", 1, [new DispatchedWordOption("alpha", DataFieldType.Word, 1)]));
        var error = Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(picky, Words(5, 0, 5, 6, -1)));
        Assert.Contains("rgPick", error.Message);
        Assert.Contains("selects no exported view", error.Message);
    }
}
