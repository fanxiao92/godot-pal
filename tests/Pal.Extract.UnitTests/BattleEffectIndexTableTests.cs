using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Pal.Extract;
using Xunit;

namespace Pal.Extract.Tests;

// Battle-effect-index table (DATA.MKF chunk 11, WORD[10][2], 40 bytes) - #91.
// Expected values are hand-computed from independently authored little-endian byte
// literals; the per-field schema is the frozen table in docs/design/data-schema.md,
// which mirrors reference/sdlpal/global.h:444. The row index is the player's battle
// sprite number (PAL_GetPlayerBattleSprite, global.c:1978) and - per this ticket's
// use-point check that settled #78's deferred naming - the two columns are
// effect-sprite group indexes, not weights or counts: [sprite][0] feeds
// PAL_BattleShowPlayerPreMagicAnim (fight.c:2387, x10 + 15, ten frames) and
// [sprite][1] feeds PAL_BattleShowPlayerAttackAnim (fight.c:2055, x3, three frames),
// so the flat rows[10][2] placeholder exports as the sprites sub-record pair
// { preMagicEffectIndex, attackEffectIndex }. Both members are plain WORD, so
// nothing decodes signed.
public sealed class BattleEffectIndexTableTests
{
    private static readonly DataTableDescriptor Table = DataTables.BattleEffectIndex;

    // Record 1 is the real chunk's ten sprite rows, hand-verified from the local
    // read-only Data.mkf (sprite 5 diverges to attack 3, sprite 7 pairs preMagic 4
    // with attack 0, sprite 8 is the 6/4 pair). Record 2 layers the WORD boundaries
    // (sprite 0), the 0x1234/0xABCD byte-swap sentinels (sprite 1), the 0x8000 bit
    // pattern (sprite 2), and a 0/65535 asymmetric pair (sprite 3) onto real-flavored
    // rows (4..9).
    private static byte[] Record1 => Words(0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 3, 2, 2, 4, 0, 6, 4, 3, 3);
    private static byte[] Record2 => Words(65535, 65535, 4660, 43981, 32768, 32768, 0, 65535, 1, 1, 5, 3, 2, 2, 4, 0, 6, 4, 3, 3);

    private static byte[] Words(params int[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), unchecked((ushort)values[i]));
        return bytes;
    }

    private static JsonArray Sprites(JsonObject record) => record["sprites"]!.AsArray();

    [Fact]
    public void Descriptor_declares_the_frozen_twenty_word_layout()
    {
        Assert.Equal("battle-effect-index", Table.FileId);
        Assert.Equal("DATA.MKF", Table.Archive);
        Assert.Equal(11, Table.Chunk);
        Assert.Equal(40, Table.RecordByteSize); // 20 WORD: 10 sprite pairs of { preMagic, attack }
        Assert.Equal(new[] { "sprites" }, Table.Fields.Select(field => field.Name));
        Assert.Null(Table.DispatchedSlot); // no union view: the flat-table shape
        // sprites is the repeated sub-record: one row per battle sprite number, no
        // word type of its own, members preMagicEffectIndex/attackEffectIndex in C
        // column order ([0] then [1], fight.c:2387 / fight.c:2055).
        var sprites = Table.Fields.Single(field => field.Name == "sprites");
        Assert.Null(sprites.Type);
        Assert.NotNull(sprites.Dimensions);
        Assert.Equal([10], sprites.Dimensions);
        Assert.NotNull(sprites.Members);
        Assert.Equal(new[] { "preMagicEffectIndex", "attackEffectIndex" }, sprites.Members!.Select(member => member.Name));
        foreach (DataField member in sprites.Members)
        {
            Assert.Equal(DataFieldType.Word, member.Type);
            Assert.Null(member.Dimensions);
            Assert.Null(member.Members);
        }
    }

    [Fact]
    public void Decode_exports_one_sprite_object_per_battle_effect_index_row()
    {
        byte[] chunk = [.. Record1, .. Record2];
        var document = DataTableSchema.DecodeTable(Table, chunk);
        Assert.Equal(1, (int)document["formatVersion"]!);
        Assert.Equal("faithful", (string)document["profile"]!);
        Assert.Equal("battle-effect-index", (string)document["fileId"]!);
        Assert.Equal(2, document["records"]!.AsArray().Count);

        // Little-endian proof straight from the byte literals: record 1's sprite 5
        // preMagic 5 is 05 00 at byte 20 with attack 3 right after at 22, sprite 8's
        // preMagic 6 is 06 00 at byte 32; record 2 starts at byte 40 with the ff ff
        // boundary pair, its sprite 1 preMagic 0x1234 is 34 12 and attack 0xABCD is
        // cd ab, and its sprite 2 preMagic 0x8000 is 00 80.
        Assert.Equal(0x05, chunk[20]);
        Assert.Equal(0x03, chunk[22]);
        Assert.Equal(0x06, chunk[32]);
        Assert.Equal(0xFF, chunk[40]);
        Assert.Equal(0xFF, chunk[41]);
        Assert.Equal(0x34, chunk[44]);
        Assert.Equal(0x12, chunk[45]);
        Assert.Equal(0xCD, chunk[46]);
        Assert.Equal(0xAB, chunk[47]);
        Assert.Equal(0x00, chunk[48]);
        Assert.Equal(0x80, chunk[49]);

        var first = document["records"]![0]!.AsObject();
        Assert.Equal(new[] { "sprites" }, first.Select(pair => pair.Key));
        Assert.Equal(10, Sprites(first).Count); // exactly ten battle sprite rows
        // Each sprite element is an object with exactly the frozen member pair, and
        // the member order follows the C column layout ([0] preMagic, [1] attack).
        foreach (JsonNode? sprite in Sprites(first))
            Assert.Equal(new[] { "preMagicEffectIndex", "attackEffectIndex" }, sprite!.AsObject().Select(pair => pair.Key));

        Assert.Equal(0, (int)Sprites(first)[0]!["preMagicEffectIndex"]!);   // all-zero row
        Assert.Equal(0, (int)Sprites(first)[0]!["attackEffectIndex"]!);
        Assert.Equal(1, (int)Sprites(first)[1]!["preMagicEffectIndex"]!);
        Assert.Equal(1, (int)Sprites(first)[1]!["attackEffectIndex"]!);
        Assert.Equal(5, (int)Sprites(first)[5]!["preMagicEffectIndex"]!);   // diverging attack row
        Assert.Equal(3, (int)Sprites(first)[5]!["attackEffectIndex"]!);
        Assert.Equal(4, (int)Sprites(first)[7]!["preMagicEffectIndex"]!);   // 4 with attack 0
        Assert.Equal(0, (int)Sprites(first)[7]!["attackEffectIndex"]!);
        Assert.Equal(6, (int)Sprites(first)[8]!["preMagicEffectIndex"]!);   // the 6/4 pair
        Assert.Equal(4, (int)Sprites(first)[8]!["attackEffectIndex"]!);
        Assert.Equal(3, (int)Sprites(first)[9]!["preMagicEffectIndex"]!);
        Assert.Equal(3, (int)Sprites(first)[9]!["attackEffectIndex"]!);

        var second = document["records"]![1]!.AsObject();
        Assert.Equal(65535, (int)Sprites(second)[0]!["preMagicEffectIndex"]!); // both WORD boundaries
        Assert.Equal(65535, (int)Sprites(second)[0]!["attackEffectIndex"]!);
        Assert.Equal(4660, (int)Sprites(second)[1]!["preMagicEffectIndex"]!);  // 0x1234
        Assert.Equal(43981, (int)Sprites(second)[1]!["attackEffectIndex"]!);   // 0xABCD
        Assert.Equal(32768, (int)Sprites(second)[2]!["preMagicEffectIndex"]!); // 0x8000 stays a WORD
        Assert.Equal(32768, (int)Sprites(second)[2]!["attackEffectIndex"]!);
        Assert.Equal(0, (int)Sprites(second)[3]!["preMagicEffectIndex"]!);     // asymmetric pair
        Assert.Equal(65535, (int)Sprites(second)[3]!["attackEffectIndex"]!);
        Assert.Equal(1, (int)Sprites(second)[4]!["preMagicEffectIndex"]!);
        Assert.Equal(1, (int)Sprites(second)[4]!["attackEffectIndex"]!);
    }

    [Fact]
    public void DescribeIndexEntry_reports_the_source_chunk_and_hand_verified_sha256()
    {
        byte[] chunk = new byte[40]; // one all-zero record
        var entry = DataTableSchema.DescribeIndexEntry(Table, chunk);
        Assert.Equal("battle-effect-index.json", (string)entry["file"]!);
        Assert.Equal("DATA.MKF", (string)entry["archive"]!);
        Assert.Equal(11, (int)entry["chunk"]!);
        Assert.Equal(1, (int)entry["recordCount"]!);
        Assert.Equal(40, (int)entry["byteLength"]!);
        // Independent expectation: hand-verified SHA-256 of 40 zero bytes (Python
        // hashlib and PowerShell Get-FileHash both agree on this constant).
        Assert.Equal("2c34ce1df23b838c5abf2a7f6437cca3d3067ed509ff25f11df6b11b582b51eb", (string)entry["sha256"]!);
        // Cross-check against an in-test hash of the two hand-authored records.
        var two = DataTableSchema.DescribeIndexEntry(Table, [.. Record1, .. Record2]);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData([.. Record1, .. Record2])), (string)two["sha256"]!);
        Assert.Equal(2, (int)two["recordCount"]!);
        Assert.Equal(80, (int)two["byteLength"]!);
    }

    [Fact]
    public void Decode_rejects_chunk_11_lengths_that_are_not_a_positive_multiple_of_40()
    {
        var error = Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, new byte[41]));
        Assert.Contains("DATA.MKF", error.Message);
        Assert.Contains("chunk 11", error.Message);
        Assert.Contains("41", error.Message);
        Assert.Contains("40", error.Message);
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, Array.Empty<byte>())); // empty chunk is not a table
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, new byte[80 + 1])); // a trailing partial record is still not a table
    }

    [Fact]
    public void ValidateDocument_accepts_decoded_records_and_rejects_unknown_or_missing_fields()
    {
        DataTableSchema.ValidateDocument(Table, DataTableSchema.DecodeTable(Table, [.. Record1, .. Record2]));

        var cName = DataTableSchema.DecodeTable(Table, Record1);
        cName["records"]![0]!["rgwBattleEffectIndex"] = 0; // C name, not the frozen JSON name
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, cName));

        var unknownMember = DataTableSchema.DecodeTable(Table, Record1);
        unknownMember["records"]![0]!["sprites"]![0]!["weight"] = 1; // guessed column name the use-point check disproved
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, unknownMember));

        var missing = DataTableSchema.DecodeTable(Table, Record2);
        missing["records"]![0]!.AsObject().Remove("sprites");
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, missing));

        var missingMember = DataTableSchema.DecodeTable(Table, Record2);
        missingMember["records"]![0]!["sprites"]![7]!.AsObject().Remove("attackEffectIndex");
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, missingMember));
    }

    [Fact]
    public void ValidateDocument_rejects_out_of_range_values_and_sprite_shapes_per_field()
    {
        // The sprite members at both unsigned boundaries, at several sprite indices.
        foreach ((string member, int value) in new[] { ("preMagicEffectIndex", -1), ("preMagicEffectIndex", 65_536), ("attackEffectIndex", -1), ("attackEffectIndex", 65_536) })
        {
            foreach (int sprite in new[] { 0, 9 })
            {
                var document = DataTableSchema.DecodeTable(Table, Record1);
                document["records"]![0]!["sprites"]![sprite]![member] = value;
                Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, document));
            }
        }
        // The sprites array must keep exactly ten elements.
        foreach (int length in new[] { 9, 11 })
        {
            var document = DataTableSchema.DecodeTable(Table, Record1);
            document["records"]![0]!["sprites"] = new JsonArray(Enumerable.Range(0, length)
                .Select(_ => (JsonNode)new JsonObject { ["preMagicEffectIndex"] = 0, ["attackEffectIndex"] = 0 }).ToArray());
            Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, document));
        }
        // A sprite element must be an object, not a scalar.
        var scalar = DataTableSchema.DecodeTable(Table, Record1);
        scalar["records"]![0]!["sprites"]![4] = 5;
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, scalar));
        // A sprite member must stay an integer.
        var text = DataTableSchema.DecodeTable(Table, Record1);
        text["records"]![0]!["sprites"]![2]!["preMagicEffectIndex"] = "65535";
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, text));
    }
}
