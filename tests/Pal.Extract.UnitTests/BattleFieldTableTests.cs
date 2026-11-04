using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Pal.Extract;
using Xunit;

namespace Pal.Extract.Tests;

// Battle-field table (DATA.MKF chunk 5, BATTLEFIELD, 12 bytes) - #89. Expected values
// are hand-computed from independently authored little-endian byte literals; the
// per-field schema is the frozen table in docs/design/data-schema.md, which mirrors
// reference/sdlpal/global.h. global.h declares wScreenWave as a plain WORD
// (battle.c:1563) and rgsMagicEffect as a SHORT[5] (NUM_MAGIC_ELEMENTAL = 5,
// palcommon.h:57; read signed by fight.c:244's sDamage *= 10 + rgsMagicEffect[...]),
// so exactly the magicEffect array decodes signed, per #78's frozen annotation.
public sealed class BattleFieldTableTests
{
    private static readonly DataTableDescriptor Table = DataTables.BattleField;

    // One hand-authored record per line; -32768 encodes as 0x8000, -1 as 0xFFFF,
    // -5 as 0xFFFB, -2 as 0xFFFE. Record 1: screenWave 0xFFFF (WORD max) and
    // magicEffect = [-32768, -1, -5, 32767, 4660] hitting both signed boundaries,
    // a negative sample, and 0x1234. Record 2: the same 0x8000 bit pattern appears
    // as screenWave (WORD, stays 32768) and inside magicEffect (SHORT, -32768) -
    // the signedness contrast in one record.
    private static byte[] Record1 => Words(65535, -32768, -1, -5, 32767, 4660);
    private static byte[] Record2 => Words(32768, 1, -2, 0, -32768, 5);

    private static byte[] Words(params int[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), unchecked((ushort)values[i]));
        return bytes;
    }

    [Fact]
    public void Descriptor_declares_the_frozen_six_word_layout()
    {
        Assert.Equal("battle-field", Table.FileId);
        Assert.Equal("DATA.MKF", Table.Archive);
        Assert.Equal(5, Table.Chunk);
        Assert.Equal(12, Table.RecordByteSize); // 6 WORD: 1 scalar + the 5-element array
        Assert.Equal(new[] { "screenWave", "magicEffect" }, Table.Fields.Select(field => field.Name));
        Assert.Null(Table.DispatchedSlot); // no union view: the flat-table shape
        // Only rgsMagicEffect is a SHORT member; wScreenWave stays a WORD.
        var magicEffect = Table.Fields.Single(field => field.Name == "magicEffect");
        Assert.Equal(DataFieldType.SignedWord, magicEffect.Type);
        Assert.NotNull(magicEffect.Dimensions);
        Assert.Equal([5], magicEffect.Dimensions);
        Assert.Equal(DataFieldType.Word, Table.Fields.Single(field => field.Name == "screenWave").Type);
    }

    [Fact]
    public void Decode_exports_the_frozen_fields_with_signed_magic_effect()
    {
        byte[] chunk = [.. Record1, .. Record2];
        var document = DataTableSchema.DecodeTable(Table, chunk);
        Assert.Equal(1, (int)document["formatVersion"]!);
        Assert.Equal("faithful", (string)document["profile"]!);
        Assert.Equal("battle-field", (string)document["fileId"]!);
        Assert.Equal(2, document["records"]!.AsArray().Count);

        // Little-endian proof straight from the byte literals: record 1's screenWave
        // 0xFFFF is ff ff, its magicEffect[0] 0x8000 is 00 80; record 2 starts at byte
        // 12 and its screenWave 0x8000 is 00 80 too - same bytes, different signedness.
        Assert.Equal(0xFF, chunk[0]);
        Assert.Equal(0xFF, chunk[1]);
        Assert.Equal(0x00, chunk[2]);
        Assert.Equal(0x80, chunk[3]);
        Assert.Equal(0x00, chunk[12]);
        Assert.Equal(0x80, chunk[13]);
        Assert.Equal(0x00, chunk[20]);
        Assert.Equal(0x80, chunk[21]);

        var first = document["records"]![0]!.AsObject();
        Assert.Equal(65535, (int)first["screenWave"]!);          // bytes ff ff
        Assert.Equal(new[] { -32768, -1, -5, 32767, 4660 },
            first["magicEffect"]!.AsArray().Select(node => (int)node!).ToArray());
        Assert.Equal(new[] { "screenWave", "magicEffect" }, first.Select(pair => pair.Key));

        var second = document["records"]![1]!.AsObject();
        Assert.Equal(32768, (int)second["screenWave"]!);         // 0x8000 stays a WORD
        Assert.Equal(new[] { 1, -2, 0, -32768, 5 },
            second["magicEffect"]!.AsArray().Select(node => (int)node!).ToArray());
    }

    [Fact]
    public void DescribeIndexEntry_reports_the_source_chunk_and_hand_verified_sha256()
    {
        byte[] chunk = new byte[12]; // one all-zero record
        var entry = DataTableSchema.DescribeIndexEntry(Table, chunk);
        Assert.Equal("battle-field.json", (string)entry["file"]!);
        Assert.Equal("DATA.MKF", (string)entry["archive"]!);
        Assert.Equal(5, (int)entry["chunk"]!);
        Assert.Equal(1, (int)entry["recordCount"]!);
        Assert.Equal(12, (int)entry["byteLength"]!);
        // Independent expectation: hand-verified SHA-256 of 12 zero bytes (Python
        // hashlib and PowerShell Get-FileHash both agree on this constant).
        Assert.Equal("15ec7bf0b50732b49f8228e07d24365338f9e3ab994b00af08e5a3bffe55fd8b", (string)entry["sha256"]!);
        // Cross-check against an in-test hash of the two hand-authored records.
        var two = DataTableSchema.DescribeIndexEntry(Table, [.. Record1, .. Record2]);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData([.. Record1, .. Record2])), (string)two["sha256"]!);
        Assert.Equal(2, (int)two["recordCount"]!);
        Assert.Equal(24, (int)two["byteLength"]!);
    }

    [Fact]
    public void Decode_rejects_chunk_5_lengths_that_are_not_a_positive_multiple_of_12()
    {
        var error = Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, new byte[13]));
        Assert.Contains("DATA.MKF", error.Message);
        Assert.Contains("chunk 5", error.Message);
        Assert.Contains("13", error.Message);
        Assert.Contains("12", error.Message);
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, Array.Empty<byte>())); // empty chunk is not a table
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, new byte[24 + 1])); // a trailing partial record is still not a table
    }

    [Fact]
    public void ValidateDocument_accepts_decoded_records_and_rejects_unknown_or_missing_fields()
    {
        DataTableSchema.ValidateDocument(Table, DataTableSchema.DecodeTable(Table, [.. Record1, .. Record2]));

        var unknown = DataTableSchema.DecodeTable(Table, Record1);
        unknown["records"]![0]!["rgsMagicEffect"] = 0; // C name, not the frozen JSON name
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, unknown));

        var missing = DataTableSchema.DecodeTable(Table, Record2);
        missing["records"]![0]!.AsObject().Remove("magicEffect");
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, missing));
    }

    [Fact]
    public void ValidateDocument_rejects_out_of_range_values_and_array_shapes_per_field()
    {
        // The WORD member at both unsigned boundaries.
        foreach (int value in new[] { -1, 65_536 })
        {
            var document = DataTableSchema.DecodeTable(Table, Record1);
            document["records"]![0]!["screenWave"] = value;
            Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, document));
        }
        // The SHORT array elements at both signed boundaries.
        foreach ((int index, int value) in new[] { (0, -32_769), (0, 32_768), (2, -32_769), (4, 32_768) })
        {
            var document = DataTableSchema.DecodeTable(Table, Record1);
            document["records"]![0]!["magicEffect"]![index] = value;
            Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, document));
        }
        // The array must keep exactly NUM_MAGIC_ELEMENTAL = 5 elements.
        foreach (int length in new[] { 4, 6 })
        {
            var document = DataTableSchema.DecodeTable(Table, Record1);
            document["records"]![0]!["magicEffect"] = new JsonArray(Enumerable.Range(0, length).Select(_ => (JsonNode)0).ToArray());
            Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, document));
        }
    }
}
