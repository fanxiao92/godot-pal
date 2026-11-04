using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Pal.Extract;
using Xunit;

namespace Pal.Extract.Tests;

// Level-up-magic table (DATA.MKF chunk 6, LEVELUPMAGIC_ALL, 20 bytes = 10 WORD) - #90.
// Expected values are hand-computed from independently authored little-endian byte
// literals; the per-field schema is the frozen table in docs/design/data-schema.md,
// which mirrors reference/sdlpal/global.h:384-393. One record is one LEVELUPMAGIC_ALL
// row whose m[MAX_PLAYABLE_PLAYER_ROLES = 5 (palcommon.h:48)] member pairs export as
// the roles sub-record array - the first repeated sub-record descriptor, delivered
// with #90's DataField.Members. global.h declares both wLevel and wMagic as plain
// WORD, so nothing decodes signed; magicId is an object.json magic-view index and 0
// means the role learns nothing (battle.c:1302-1303 skips wMagic == 0 rows, #82).
public sealed class LevelUpMagicTableTests
{
    private static readonly DataTableDescriptor Table = DataTables.LevelUpMagic;

    // One hand-authored record per line (5 role pairs: level, magicId). Record 1:
    // role 0 learns nothing at level 0, role 1 hits both WORD boundaries, role 2
    // carries the byte-swap sentinels 0x1234/0xABCD, role 3 reaches level 1 with no
    // magic, role 4 learns object 295 (the first real magic object index per #82).
    // Record 2: role 0 reuses the 0x8000 bit pattern in both words, role 4 pairs the
    // level boundary with magicId 0.
    private static byte[] Record1 => Words(0, 0, 65535, 65535, 4660, 43981, 1, 0, 99, 295);
    private static byte[] Record2 => Words(32768, 32768, 2, 392, 5, 1, 10, 600, 65535, 0);

    private static byte[] Words(params int[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), unchecked((ushort)values[i]));
        return bytes;
    }

    private static JsonArray Roles(JsonObject record) => record["roles"]!.AsArray();

    [Fact]
    public void Descriptor_declares_the_frozen_ten_word_layout()
    {
        Assert.Equal("level-up-magic", Table.FileId);
        Assert.Equal("DATA.MKF", Table.Archive);
        Assert.Equal(6, Table.Chunk);
        Assert.Equal(20, Table.RecordByteSize); // 10 WORD: 5 role pairs of { level, magicId }
        Assert.Equal(new[] { "roles" }, Table.Fields.Select(field => field.Name));
        Assert.Null(Table.DispatchedSlot); // no union view: the flat-table shape
        // roles is the repeated sub-record: one m[5] element per role, no word type of
        // its own, members level/magicId in declaration order.
        var roles = Table.Fields.Single(field => field.Name == "roles");
        Assert.Null(roles.Type);
        Assert.NotNull(roles.Dimensions);
        Assert.Equal([5], roles.Dimensions); // MAX_PLAYABLE_PLAYER_ROLES
        Assert.NotNull(roles.Members);
        Assert.Equal(new[] { "level", "magicId" }, roles.Members!.Select(member => member.Name));
        foreach (DataField member in roles.Members)
        {
            Assert.Equal(DataFieldType.Word, member.Type);
            Assert.Null(member.Dimensions);
            Assert.Null(member.Members);
        }
    }

    [Fact]
    public void Decode_exports_one_role_object_per_levelupmagic_member_pair()
    {
        byte[] chunk = [.. Record1, .. Record2];
        var document = DataTableSchema.DecodeTable(Table, chunk);
        Assert.Equal(1, (int)document["formatVersion"]!);
        Assert.Equal("faithful", (string)document["profile"]!);
        Assert.Equal("level-up-magic", (string)document["fileId"]!);
        Assert.Equal(2, document["records"]!.AsArray().Count);

        // Little-endian proof straight from the byte literals: record 1's role 0
        // (level 0, magic 0) is four zero bytes, role 1's boundary pair is ff ff ff ff,
        // role 2's level 0x1234 is 34 12 and its magicId 0xABCD is cd ab; record 2
        // starts at byte 20 and its role 0 level 0x8000 is 00 80.
        Assert.Equal(0x00, chunk[0]);
        Assert.Equal(0x00, chunk[1]);
        Assert.Equal(0xFF, chunk[4]);
        Assert.Equal(0xFF, chunk[5]);
        Assert.Equal(0xFF, chunk[6]);
        Assert.Equal(0xFF, chunk[7]);
        Assert.Equal(0x34, chunk[8]);
        Assert.Equal(0x12, chunk[9]);
        Assert.Equal(0xCD, chunk[10]);
        Assert.Equal(0xAB, chunk[11]);
        Assert.Equal(0x00, chunk[20]);
        Assert.Equal(0x80, chunk[21]);

        var first = document["records"]![0]!.AsObject();
        Assert.Equal(new[] { "roles" }, first.Select(pair => pair.Key));
        Assert.Equal(5, Roles(first).Count); // exactly MAX_PLAYABLE_PLAYER_ROLES elements
        // Each role element is an object with exactly the frozen member pair, and the
        // member pair order follows the C layout (wLevel then wMagic).
        foreach (JsonNode? role in Roles(first))
            Assert.Equal(new[] { "level", "magicId" }, role!.AsObject().Select(pair => pair.Key));

        Assert.Equal(0, (int)Roles(first)[0]!["level"]!);            // no magic at all
        Assert.Equal(0, (int)Roles(first)[0]!["magicId"]!);
        Assert.Equal(65535, (int)Roles(first)[1]!["level"]!);        // both WORD boundaries
        Assert.Equal(65535, (int)Roles(first)[1]!["magicId"]!);
        Assert.Equal(4660, (int)Roles(first)[2]!["level"]!);         // 0x1234
        Assert.Equal(43981, (int)Roles(first)[2]!["magicId"]!);      // 0xABCD
        Assert.Equal(1, (int)Roles(first)[3]!["level"]!);
        Assert.Equal(0, (int)Roles(first)[3]!["magicId"]!);          // level 1, learns nothing
        Assert.Equal(99, (int)Roles(first)[4]!["level"]!);
        Assert.Equal(295, (int)Roles(first)[4]!["magicId"]!);

        var second = document["records"]![1]!.AsObject();
        Assert.Equal(32768, (int)Roles(second)[0]!["level"]!);       // 0x8000 stays a WORD
        Assert.Equal(32768, (int)Roles(second)[0]!["magicId"]!);
        Assert.Equal(2, (int)Roles(second)[1]!["level"]!);
        Assert.Equal(392, (int)Roles(second)[1]!["magicId"]!);
        Assert.Equal(5, (int)Roles(second)[2]!["level"]!);
        Assert.Equal(1, (int)Roles(second)[2]!["magicId"]!);
        Assert.Equal(10, (int)Roles(second)[3]!["level"]!);
        Assert.Equal(600, (int)Roles(second)[3]!["magicId"]!);
        Assert.Equal(65535, (int)Roles(second)[4]!["level"]!);
        Assert.Equal(0, (int)Roles(second)[4]!["magicId"]!);         // boundary level, no magic
    }

    [Fact]
    public void DescribeIndexEntry_reports_the_source_chunk_and_hand_verified_sha256()
    {
        byte[] chunk = new byte[20]; // one all-zero record
        var entry = DataTableSchema.DescribeIndexEntry(Table, chunk);
        Assert.Equal("level-up-magic.json", (string)entry["file"]!);
        Assert.Equal("DATA.MKF", (string)entry["archive"]!);
        Assert.Equal(6, (int)entry["chunk"]!);
        Assert.Equal(1, (int)entry["recordCount"]!);
        Assert.Equal(20, (int)entry["byteLength"]!);
        // Independent expectation: hand-verified SHA-256 of 20 zero bytes (Python
        // hashlib and PowerShell Get-FileHash both agree on this constant).
        Assert.Equal("de47c9b27eb8d300dbb5f2c353e632c393262cf06340c4fa7f1b40c4cbd36f90", (string)entry["sha256"]!);
        // Cross-check against an in-test hash of the two hand-authored records.
        var two = DataTableSchema.DescribeIndexEntry(Table, [.. Record1, .. Record2]);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData([.. Record1, .. Record2])), (string)two["sha256"]!);
        Assert.Equal(2, (int)two["recordCount"]!);
        Assert.Equal(40, (int)two["byteLength"]!);
    }

    [Fact]
    public void Decode_rejects_chunk_6_lengths_that_are_not_a_positive_multiple_of_20()
    {
        var error = Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, new byte[21]));
        Assert.Contains("DATA.MKF", error.Message);
        Assert.Contains("chunk 6", error.Message);
        Assert.Contains("21", error.Message);
        Assert.Contains("20", error.Message);
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, Array.Empty<byte>())); // empty chunk is not a table
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, new byte[40 + 1])); // a trailing partial record is still not a table
    }

    [Fact]
    public void ValidateDocument_accepts_decoded_records_and_rejects_unknown_or_missing_fields()
    {
        DataTableSchema.ValidateDocument(Table, DataTableSchema.DecodeTable(Table, [.. Record1, .. Record2]));

        var cName = DataTableSchema.DecodeTable(Table, Record1);
        cName["records"]![0]!["m"] = 0; // C name, not the frozen JSON name
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, cName));

        var unknownMember = DataTableSchema.DecodeTable(Table, Record1);
        unknownMember["records"]![0]!["roles"]![0]!["wMagic"] = 1; // C member name inside the sub-record
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, unknownMember));

        var missing = DataTableSchema.DecodeTable(Table, Record2);
        missing["records"]![0]!.AsObject().Remove("roles");
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, missing));

        var missingMember = DataTableSchema.DecodeTable(Table, Record2);
        missingMember["records"]![0]!["roles"]![3]!.AsObject().Remove("magicId");
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, missingMember));
    }

    [Fact]
    public void ValidateDocument_rejects_out_of_range_values_and_role_shapes_per_field()
    {
        // The role members at both unsigned boundaries, at several role indices.
        foreach ((string member, int value) in new[] { ("level", -1), ("level", 65_536), ("magicId", -1), ("magicId", 65_536) })
        {
            foreach (int role in new[] { 0, 4 })
            {
                var document = DataTableSchema.DecodeTable(Table, Record1);
                document["records"]![0]!["roles"]![role]![member] = value;
                Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, document));
            }
        }
        // The roles array must keep exactly MAX_PLAYABLE_PLAYER_ROLES = 5 elements.
        foreach (int length in new[] { 4, 6 })
        {
            var document = DataTableSchema.DecodeTable(Table, Record1);
            document["records"]![0]!["roles"] = new JsonArray(Enumerable.Range(0, length)
                .Select(_ => (JsonNode)new JsonObject { ["level"] = 0, ["magicId"] = 0 }).ToArray());
            Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, document));
        }
        // A role element must be an object, not a scalar.
        var scalar = DataTableSchema.DecodeTable(Table, Record1);
        scalar["records"]![0]!["roles"]![2] = 5;
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, scalar));
        // A role member must stay an integer.
        var text = DataTableSchema.DecodeTable(Table, Record1);
        text["records"]![0]!["roles"]![1]!["magicId"] = "65535";
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, text));
    }
}
