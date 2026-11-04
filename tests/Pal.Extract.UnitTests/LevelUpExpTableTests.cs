using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Pal.Extract;
using Xunit;

namespace Pal.Extract.Tests;

// Level-up-exp table (DATA.MKF chunk 14, WORD[MAX_LEVELS + 1] = WORD[100], 200
// bytes) - #91. Expected values are hand-computed from independently authored
// little-endian byte literals; the per-field schema is the frozen table in
// docs/design/data-schema.md, which mirrors reference/sdlpal/global.h:407/442
// (LEVELUPEXP is a plain WORD typedef, so nothing decodes signed). global.c:306-308
// reads the fixed 200-byte chunk and byteswaps it whole; battle.c:1106-1112 consumes
// rgLevelUpExp[rgwLevel[w]] as the exp threshold to leave the current level, so the
// JSON index is the level (index 0 unused). The hand-authored literal pairs the
// hand-verified real first five values (0, 15, 40, 90, 165) and the real 32000
// plateau with identity word indexes in between and the byte-swap sentinels at
// indexes 47..49.
public sealed class LevelUpExpTableTests
{
    private static readonly DataTableDescriptor Table = DataTables.LevelUpExp;

    // 100 hand-authored words: 0..4 are the real chunk's first five thresholds,
    // 5..46 and 50..94 equal their index (value == word index makes the offset math
    // checkable by hand), 47..49 carry 0x1234/0xABCD/0x8000, 95..98 the real 32000
    // plateau, and 99 the WORD boundary.
    private static int[] Values
    {
        get
        {
            var values = Enumerable.Range(0, 100).ToArray();
            values[1] = 15;
            values[2] = 40;
            values[3] = 90;
            values[4] = 165;
            values[47] = 4660;
            values[48] = 43981;
            values[49] = 32768;
            for (int index = 95; index < 99; index++) values[index] = 32000;
            values[99] = 65535;
            return values;
        }
    }

    private static byte[] Record => Words(Values);

    private static byte[] Words(params int[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), unchecked((ushort)values[i]));
        return bytes;
    }

    [Fact]
    public void Descriptor_declares_the_frozen_hundred_word_layout()
    {
        Assert.Equal("level-up-exp", Table.FileId);
        Assert.Equal("DATA.MKF", Table.Archive);
        Assert.Equal(14, Table.Chunk);
        Assert.Equal(200, Table.RecordByteSize); // 100 WORD: MAX_LEVELS + 1 = 100 thresholds
        Assert.Equal(new[] { "levelUpExp" }, Table.Fields.Select(field => field.Name));
        Assert.Null(Table.DispatchedSlot); // no union view: the flat-table shape
        // levelUpExp is a flat plain-WORD array - no sub-record members, unlike the
        // neighboring #90/#91 sibling tables - with exactly 100 elements.
        var levelUpExp = Table.Fields.Single(field => field.Name == "levelUpExp");
        Assert.Equal(DataFieldType.Word, levelUpExp.Type);
        Assert.NotNull(levelUpExp.Dimensions);
        Assert.Equal([100], levelUpExp.Dimensions);
        Assert.Null(levelUpExp.Members);
    }

    [Fact]
    public void Decode_exports_the_hundred_threshold_words_little_endian()
    {
        byte[] chunk = Record;
        var document = DataTableSchema.DecodeTable(Table, chunk);
        Assert.Equal(1, (int)document["formatVersion"]!);
        Assert.Equal("faithful", (string)document["profile"]!);
        Assert.Equal("level-up-exp", (string)document["fileId"]!);
        Assert.Single(document["records"]!.AsArray());

        // Little-endian proof straight from the byte literals: threshold 0 is the
        // 00 00 pair at byte 0, threshold 1 = 15 is 0f 00 at byte 2, threshold 4 =
        // 165 is a5 00 at byte 8, threshold 47 = 0x1234 is 34 12 at byte 94 with
        // 0xABCD as cd ab and 0x8000 as 00 80 right behind, threshold 95 = 32000 is
        // 00 7d at byte 190, and threshold 99 = 65535 is ff ff at byte 198.
        Assert.Equal(0x00, chunk[0]);
        Assert.Equal(0x0F, chunk[2]);
        Assert.Equal(0xA5, chunk[8]);
        Assert.Equal(0x34, chunk[94]);
        Assert.Equal(0x12, chunk[95]);
        Assert.Equal(0xCD, chunk[96]);
        Assert.Equal(0xAB, chunk[97]);
        Assert.Equal(0x00, chunk[98]);
        Assert.Equal(0x80, chunk[99]);
        Assert.Equal(0x00, chunk[190]);
        Assert.Equal(0x7D, chunk[191]);
        Assert.Equal(0xFF, chunk[198]);
        Assert.Equal(0xFF, chunk[199]);

        var first = document["records"]![0]!.AsObject();
        Assert.Equal(new[] { "levelUpExp" }, first.Select(pair => pair.Key));
        var levelUpExp = first["levelUpExp"]!.AsArray();
        Assert.Equal(100, levelUpExp.Count); // exactly MAX_LEVELS + 1 thresholds
        Assert.Equal(0, (int)levelUpExp[0]!);          // index 0 unused
        Assert.Equal(15, (int)levelUpExp[1]!);         // real: exp to leave level 1
        Assert.Equal(40, (int)levelUpExp[2]!);
        Assert.Equal(90, (int)levelUpExp[3]!);
        Assert.Equal(165, (int)levelUpExp[4]!);
        Assert.Equal(5, (int)levelUpExp[5]!);          // identity tail: value == index
        Assert.Equal(10, (int)levelUpExp[10]!);
        Assert.Equal(46, (int)levelUpExp[46]!);
        Assert.Equal(4660, (int)levelUpExp[47]!);      // 0x1234
        Assert.Equal(43981, (int)levelUpExp[48]!);     // 0xABCD
        Assert.Equal(32768, (int)levelUpExp[49]!);     // 0x8000 stays a WORD
        Assert.Equal(50, (int)levelUpExp[50]!);
        Assert.Equal(94, (int)levelUpExp[94]!);
        Assert.Equal(32000, (int)levelUpExp[95]!);     // real plateau
        Assert.Equal(32000, (int)levelUpExp[98]!);
        Assert.Equal(65535, (int)levelUpExp[99]!);     // the WORD boundary
    }

    [Fact]
    public void DescribeIndexEntry_reports_the_source_chunk_and_hand_verified_sha256()
    {
        byte[] chunk = new byte[200]; // one all-zero record
        var entry = DataTableSchema.DescribeIndexEntry(Table, chunk);
        Assert.Equal("level-up-exp.json", (string)entry["file"]!);
        Assert.Equal("DATA.MKF", (string)entry["archive"]!);
        Assert.Equal(14, (int)entry["chunk"]!);
        Assert.Equal(1, (int)entry["recordCount"]!);
        Assert.Equal(200, (int)entry["byteLength"]!);
        // Independent expectation: hand-verified SHA-256 of 200 zero bytes (Python
        // hashlib and PowerShell Get-FileHash both agree on this constant).
        Assert.Equal("6d9c54dee5660c46886f32d80e57e9dd0ffa57ee0cd2a762b036d9c8e0c3a33a", (string)entry["sha256"]!);
        // Cross-check against an in-test hash of the hand-authored record.
        var one = DataTableSchema.DescribeIndexEntry(Table, Record);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Record)), (string)one["sha256"]!);
        // Two concatenated records double the length and count, proving both derive
        // from the chunk length rather than a hardcoded constant.
        var two = DataTableSchema.DescribeIndexEntry(Table, [.. Record, .. Record]);
        Assert.Equal(2, (int)two["recordCount"]!);
        Assert.Equal(400, (int)two["byteLength"]!);
    }

    [Fact]
    public void Decode_rejects_chunk_14_lengths_that_are_not_a_positive_multiple_of_200()
    {
        var error = Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, new byte[201]));
        Assert.Contains("DATA.MKF", error.Message);
        Assert.Contains("chunk 14", error.Message);
        Assert.Contains("201", error.Message);
        Assert.Contains("200", error.Message);
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, Array.Empty<byte>())); // empty chunk is not a table
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, new byte[400 + 1])); // a trailing partial record is still not a table
    }

    [Fact]
    public void ValidateDocument_accepts_decoded_records_and_rejects_unknown_or_missing_fields()
    {
        DataTableSchema.ValidateDocument(Table, DataTableSchema.DecodeTable(Table, Record));

        var cName = DataTableSchema.DecodeTable(Table, Record);
        cName["records"]![0]!["rgLevelUpExp"] = 0; // C name, not the frozen JSON name
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, cName));

        var missing = DataTableSchema.DecodeTable(Table, Record);
        missing["records"]![0]!.AsObject().Remove("levelUpExp");
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, missing));
    }

    [Fact]
    public void ValidateDocument_rejects_out_of_range_values_and_array_shapes()
    {
        // The thresholds at both unsigned boundaries, at several level indexes.
        foreach ((int index, int value) in new[] { (0, -1), (0, 65_536), (47, -1), (47, 65_536), (99, -1), (99, 65_536) })
        {
            var document = DataTableSchema.DecodeTable(Table, Record);
            document["records"]![0]!["levelUpExp"]![index] = value;
            Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, document));
        }
        // The array must keep exactly MAX_LEVELS + 1 = 100 elements.
        foreach (int length in new[] { 99, 101 })
        {
            var document = DataTableSchema.DecodeTable(Table, Record);
            document["records"]![0]!["levelUpExp"] = new JsonArray(Enumerable.Range(0, length).Select(value => (JsonNode)value).ToArray());
            Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, document));
        }
        // A threshold must stay an integer, not a string.
        var text = DataTableSchema.DecodeTable(Table, Record);
        text["records"]![0]!["levelUpExp"]![10] = "10";
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, text));
        // The whole field must stay an array, not a scalar.
        var scalar = DataTableSchema.DecodeTable(Table, Record);
        scalar["records"]![0]!["levelUpExp"] = 0;
        Assert.Throws<InvalidOperationException>(() => DataTableSchema.ValidateDocument(Table, scalar)); // AsArray() fails first, as in the #78-established behavior
    }
}
