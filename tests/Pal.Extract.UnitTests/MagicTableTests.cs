using System.Security.Cryptography;
using Pal.Extract;
using Xunit;
using static Pal.Extract.Tests.OpcodeCounterexampleHarness;

namespace Pal.Extract.Tests;

// Magic table (DATA.MKF chunk 4, MAGIC, 32 bytes) - #88. Expected values are
// hand-computed from independently authored little-endian byte literals; the
// per-field schema is the frozen table in docs/design/data-schema.md, which mirrors
// reference/sdlpal/global.h. Contract core (#78): rgSpecific is one physical WORD
// under two views dispatched by the record's own type - type=9 (kMagicTypeSummon)
// exports the WORD summonEffect view (fight.c:3135, reached only under the
// kMagicTypeSummon checks at fight.c:3865-3868/4255-4257), every other type exports
// the SHORT layerOffset view (fight.c:2518/2735/2960) - one exported name per record,
// never both. global.h declares wSpeed/wSound as SHORT (fight.c:2513 computes
// (wSpeed + 5) * 10; fight.c:2501 passes wSound to AUDIO_PlaySound's INT parameter),
// so exactly those two decode signed besides the layerOffset view.
public sealed class MagicTableTests
{
    private static readonly DataTableDescriptor Table = DataTables.Magic;

    // One hand-authored record per line; -32768 encodes as 0x8000, -1 as 0xFFFF,
    // -5 as 0xFFFB. Record 1 is a normal magic (type=0, the fallback layerOffset
    // view); its word slots: 0 effect, 1 type, 2..3 offsets, 4 rgSpecific
    // (0xFFFB -> layerOffset -5), 5 speed (0x8000 -> -32768), 6..11 plain WORDs
    // (costMp 0x8000 stays unsigned), 12..14 WORD, 15 sound (32767 signed).
    private static byte[] Record1 => Words(0x1234, 0, 65535, 3, -5,
        -32768, 0, 32767, 0xABCD, 1, 65535, 0,
        32768, 7, 2, 32767);
    // Record 2 is a summon magic (type=9, the summonEffect view): word 4 (1000)
    // exports as the WORD summonEffect, speed is -1, sound is -32768 (0x8000).
    private static byte[] Record2 => Words(5, 9, 0xABCD, 0, 1000,
        -1, 2, 65535, 1, 0, 4660, 65535,
        99, 32767, 5, -32768);

    [Fact]
    public void Descriptor_declares_the_frozen_sixteen_word_layout_with_the_dispatched_slot()
    {
        Assert.Equal("magic", Table.FileId);
        Assert.Equal("DATA.MKF", Table.Archive);
        Assert.Equal(4, Table.Chunk);
        Assert.Equal(32, Table.RecordByteSize); // 16 WORD: 15 named fields + the dispatched slot
        Assert.Equal(new[]
        {
            "effect", "type", "xOffset", "yOffset", "speed", "keepEffect", "fireDelay", "effectTimes",
            "shake", "wave", "unknown", "costMp", "baseDamage", "elemental", "sound",
        }, Table.Fields.Select(field => field.Name));
        // Only wSpeed/wSound are SHORT members; the layerOffset view adds the third
        // signed export, and only for records whose type is not 9.
        Assert.Equal(new[] { "speed", "sound" },
            Table.Fields.Where(field => field.Type == DataFieldType.SignedWord).Select(field => field.Name));
        var slot = Table.DispatchedSlot!;
        Assert.Equal("rgSpecific", slot.CName);
        Assert.Equal("type", slot.DispatchFieldName);
        Assert.Equal(4, slot.SlotWordIndex); // global.h: wEffect, wType, wXOffset, wYOffset, then rgSpecific
        Assert.Equal([("summonEffect", DataFieldType.Word, 9), ("layerOffset", DataFieldType.SignedWord, null)],
            slot.Options.Select(option => (option.Name, option.Type, option.MatchValue)));
    }

    [Fact]
    public void Decode_exports_the_frozen_fields_with_the_type_dispatched_view()
    {
        byte[] chunk = [.. Record1, .. Record2];
        var document = DataTableSchema.DecodeTable(Table, chunk);
        Assert.Equal(1, (int)document["formatVersion"]!);
        Assert.Equal("faithful", (string)document["profile"]!);
        Assert.Equal("magic", (string)document["fileId"]!);
        Assert.Equal(2, document["records"]!.AsArray().Count);

        // Little-endian proof straight from the byte literals: record 1's effect
        // 0x1234 is 34 12; its rgSpecific 0xFFFB is fb ff; record 2 starts at byte 32
        // and its rgSpecific 1000 = 0x03E8 is e8 03.
        Assert.Equal(0x34, chunk[0]);
        Assert.Equal(0x12, chunk[1]);
        Assert.Equal(0xFB, chunk[8]);
        Assert.Equal(0xFF, chunk[9]);
        Assert.Equal(0xE8, chunk[40]);
        Assert.Equal(0x03, chunk[41]);

        var first = document["records"]![0]!.AsObject();
        Assert.Equal(0x1234, (int)first["effect"]!);              // bytes 34 12
        Assert.Equal(0, (int)first["type"]!);
        Assert.Equal(65535, (int)first["xOffset"]!);
        Assert.Equal(3, (int)first["yOffset"]!);
        Assert.Equal(-5, (int)first["layerOffset"]!);             // 0xFFFB as the fallback SHORT view
        Assert.Equal(-32768, (int)first["speed"]!);               // 0x8000 as SHORT
        Assert.Equal(0, (int)first["keepEffect"]!);
        Assert.Equal(32767, (int)first["fireDelay"]!);
        Assert.Equal(0xABCD, (int)first["effectTimes"]!);
        Assert.Equal(1, (int)first["shake"]!);
        Assert.Equal(65535, (int)first["wave"]!);
        Assert.Equal(0, (int)first["unknown"]!);
        Assert.Equal(32768, (int)first["costMp"]!);               // 0x8000 stays a WORD
        Assert.Equal(7, (int)first["baseDamage"]!);
        Assert.Equal(2, (int)first["elemental"]!);
        Assert.Equal(32767, (int)first["sound"]!);
        Assert.False(first.ContainsKey("summonEffect"));          // one view per record

        var second = document["records"]![1]!.AsObject();
        Assert.Equal(5, (int)second["effect"]!);
        Assert.Equal(9, (int)second["type"]!);
        Assert.Equal(0xABCD, (int)second["xOffset"]!);
        Assert.Equal(0, (int)second["yOffset"]!);
        Assert.Equal(1000, (int)second["summonEffect"]!);         // the WORD view selected by type=9
        Assert.Equal(-1, (int)second["speed"]!);                  // 0xFFFF as SHORT
        Assert.Equal(2, (int)second["keepEffect"]!);
        Assert.Equal(65535, (int)second["fireDelay"]!);
        Assert.Equal(1, (int)second["effectTimes"]!);
        Assert.Equal(0, (int)second["shake"]!);
        Assert.Equal(4660, (int)second["wave"]!);                 // 0x1234, bytes 34 12
        Assert.Equal(65535, (int)second["unknown"]!);
        Assert.Equal(99, (int)second["costMp"]!);
        Assert.Equal(32767, (int)second["baseDamage"]!);
        Assert.Equal(5, (int)second["elemental"]!);
        Assert.Equal(-32768, (int)second["sound"]!);              // 0x8000 as SHORT
        Assert.False(second.ContainsKey("layerOffset"));          // one view per record

        // The exported view keeps the slot's physical position in the key order:
        // ..., yOffset, layerOffset, speed, ... / ..., yOffset, summonEffect, speed, ...
        Assert.Equal(new[] { "effect", "type", "xOffset", "yOffset", "layerOffset", "speed" },
            first.Select(pair => pair.Key).Take(6));
        Assert.Equal(new[] { "effect", "type", "xOffset", "yOffset", "summonEffect", "speed" },
            second.Select(pair => pair.Key).Take(6));
    }

    [Fact]
    public void Decode_uses_the_layer_offset_fallback_for_every_non_summon_type()
    {
        // kMagicTypeTrance = 8: not 9, so the fallback view applies (global.h MAGIC_TYPE).
        var document = DataTableSchema.DecodeTable(Table, Words(5, 8, 0, 0, -32768, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        var record = document["records"]![0]!.AsObject();
        Assert.Equal(8, (int)record["type"]!);
        Assert.Equal(-32768, (int)record["layerOffset"]!); // 0x8000 as the fallback SHORT view
        Assert.False(record.ContainsKey("summonEffect"));
    }

    [Fact]
    public void DescribeIndexEntry_reports_the_source_chunk_and_hand_verified_sha256()
    {
        byte[] chunk = new byte[32]; // one all-zero record (type 0 -> layerOffset 0)
        var entry = DataTableSchema.DescribeIndexEntry(Table, chunk);
        Assert.Equal("magic.json", (string)entry["file"]!);
        Assert.Equal("DATA.MKF", (string)entry["archive"]!);
        Assert.Equal(4, (int)entry["chunk"]!);
        Assert.Equal(1, (int)entry["recordCount"]!);
        Assert.Equal(32, (int)entry["byteLength"]!);
        // Independent expectation: hand-verified SHA-256 of 32 zero bytes (Python
        // hashlib and PowerShell SHA256.Create both agree on this constant).
        Assert.Equal("66687aadf862bd776c8fc18b8e9f8e20089714856ee233b3902a591d0d5f2925", (string)entry["sha256"]!);
        // Cross-check against an in-test hash of the two hand-authored records.
        var two = DataTableSchema.DescribeIndexEntry(Table, [.. Record1, .. Record2]);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData([.. Record1, .. Record2])), (string)two["sha256"]!);
        Assert.Equal(2, (int)two["recordCount"]!);
        Assert.Equal(64, (int)two["byteLength"]!);
    }

    [Fact]
    public void Decode_rejects_chunk_4_lengths_that_are_not_a_positive_multiple_of_32()
    {
        var error = Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, new byte[33]));
        Assert.Contains("DATA.MKF", error.Message);
        Assert.Contains("chunk 4", error.Message);
        Assert.Contains("33", error.Message);
        Assert.Contains("32", error.Message);
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, Array.Empty<byte>())); // empty chunk is not a table
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, new byte[64 + 1])); // a trailing partial record is still not a table
    }

    [Fact]
    public void ValidateDocument_accepts_decoded_records_and_rejects_unknown_or_missing_fields()
    {
        DataTableSchema.ValidateDocument(Table, DataTableSchema.DecodeTable(Table, Record1)); // type=0 record passes
        DataTableSchema.ValidateDocument(Table, DataTableSchema.DecodeTable(Table, Record2)); // type=9 record passes

        var unknown = DataTableSchema.DecodeTable(Table, Record1);
        unknown["records"]![0]!["wCostMP"] = 1; // C name, not the frozen JSON name
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, unknown));

        var missing = DataTableSchema.DecodeTable(Table, Record2);
        missing["records"]![0]!.AsObject().Remove("costMp");
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, missing));
    }

    [Fact]
    public void ValidateDocument_enforces_exactly_one_dispatched_view_per_record()
    {
        // type=9 record exporting the other view's name instead: 'layerOffset' is
        // unknown for a summon record and 'summonEffect' is missing.
        var summonAsLayer = DataTableSchema.DecodeTable(Table, Record2);
        var summonRecord = summonAsLayer["records"]![0]!.AsObject();
        summonRecord.Remove("summonEffect");
        summonRecord["layerOffset"] = 1000;
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, summonAsLayer));

        // type=0 record exporting the other view's name instead: mirror image.
        var normalAsSummon = DataTableSchema.DecodeTable(Table, Record1);
        var normalRecord = normalAsSummon["records"]![0]!.AsObject();
        normalRecord.Remove("layerOffset");
        normalRecord["summonEffect"] = -5;
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, normalAsSummon));

        // Both views present on one record: the unselected name is always unknown.
        foreach (byte[] record in new[] { Record1, Record2 })
        {
            var both = DataTableSchema.DecodeTable(Table, record);
            var target = both["records"]![0]!.AsObject();
            target[target.ContainsKey("layerOffset") ? "summonEffect" : "layerOffset"] = 0;
            Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, both));
        }

        // The dispatched slot missing entirely: the selected name is a required field.
        var absent = DataTableSchema.DecodeTable(Table, Record2);
        absent["records"]![0]!.AsObject().Remove("summonEffect");
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, absent));
    }

    [Fact]
    public void ValidateDocument_rejects_out_of_range_word_and_signed_values_per_field()
    {
        // Unsigned members, including the dispatch field itself and the summon view.
        foreach ((string field, int value) in new[] { ("effect", -1), ("effect", 65_536), ("type", -1), ("type", 65_536), ("costMp", 65_536) })
        {
            var document = DataTableSchema.DecodeTable(Table, Record1);
            document["records"]![0]![field] = value;
            Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, document));
        }
        // The SHORT members at both signed boundaries.
        foreach ((string field, int value) in new[] { ("speed", -32_769), ("speed", 32_768), ("sound", -32_769), ("sound", 32_768) })
        {
            var document = DataTableSchema.DecodeTable(Table, Record1);
            document["records"]![0]![field] = value;
            Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, document));
        }
        // The dispatched views carry their own ranges: summonEffect is a WORD (on a
        // type=9 record), layerOffset is a SHORT (on a type=0 record).
        var summon = DataTableSchema.DecodeTable(Table, Record2);
        summon["records"]![0]!["summonEffect"] = -1;
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, summon));
        var normal = DataTableSchema.DecodeTable(Table, Record1);
        normal["records"]![0]!["layerOffset"] = 32_768;
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, normal));
    }
}
