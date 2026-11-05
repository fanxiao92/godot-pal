using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Pal.Extract;
using Xunit;
using static Pal.Extract.Tests.OpcodeCounterexampleHarness;

namespace Pal.Extract.Tests;

// Enemy-pos table (DATA.MKF chunk 13, ENEMYPOS = PALPOS[5][5], 100 bytes) - #91.
// Expected values are hand-computed from independently authored little-endian byte
// literals; the per-field schema is the frozen table in docs/design/data-schema.md,
// which mirrors reference/sdlpal/global.h:395-404 (each PALPOS is a plain WORD x/y
// pair, so nothing decodes signed). global.c:303-305 reads the fixed 100-byte chunk
// and byteswaps it whole; battle.c:936-937 consumes pos[i][wMaxEnemyIndex].x/.y as
// enemy slot i's default battle position - the second index is the team's highest
// occupied slot, so only the j >= i cells carry live coordinates in real data and
// the unused lower cells' zeros export unchanged.
public sealed class EnemyPosTableTests
{
    private static readonly DataTableDescriptor Table = DataTables.EnemyPos;

    // Record 1 is the real chunk's whole 5x5 grid, hand-verified from the local
    // read-only Data.mkf: row i's first live cell sits at column i (enemy i's slot
    // when i+1 enemies stand), all j < i cells are the unused {0,0}. Record 2 layers
    // the WORD boundaries and the byte-swap sentinels onto the same shape with the
    // rest all zero.
    private static byte[] Record1 => Words(
        100, 110, 70, 130, 70, 140, 70, 140, 70, 140,
        0, 0, 140, 106, 100, 110, 100, 110, 100, 110,
        0, 0, 0, 0, 160, 100, 160, 100, 160, 100,
        0, 0, 0, 0, 0, 0, 50, 100, 30, 110,
        0, 0, 0, 0, 0, 0, 0, 0, 90, 80);
    private static byte[] Record2 => Words(
        65535, 65535, 4660, 43981, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 3, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 32768, 32768);

    private static JsonArray Row(JsonObject record, int index) => record["positions"]!.AsArray()[index]!.AsArray();

    [Fact]
    public void Descriptor_declares_the_frozen_fifty_word_layout()
    {
        Assert.Equal("enemy-pos", Table.FileId);
        Assert.Equal("DATA.MKF", Table.Archive);
        Assert.Equal(13, Table.Chunk);
        Assert.Equal(100, Table.RecordByteSize); // 50 WORD: 25 PALPOS pairs of { x, y }
        Assert.Equal(new[] { "positions" }, Table.Fields.Select(field => field.Name));
        Assert.Null(Table.DispatchedSlot); // no union view: the flat-table shape
        // positions is the repeated sub-record grid: one PALPOS object per
        // pos[i][j] cell (first index outer, matching the C layout), members x/y in
        // declaration order; the same Members shape as #90's roles but two-dimensional,
        // like #86's elementalResistance[5][6].
        var positions = Table.Fields.Single(field => field.Name == "positions");
        Assert.Null(positions.Type);
        Assert.NotNull(positions.Dimensions);
        Assert.Equal([5, 5], positions.Dimensions);
        Assert.NotNull(positions.Members);
        Assert.Equal(new[] { "x", "y" }, positions.Members!.Select(member => member.Name));
        foreach (DataField member in positions.Members)
        {
            Assert.Equal(DataFieldType.Word, member.Type);
            Assert.Null(member.Dimensions);
            Assert.Null(member.Members);
        }
    }

    [Fact]
    public void Decode_exports_one_xy_object_per_palpos_cell_in_c_layout_order()
    {
        byte[] chunk = [.. Record1, .. Record2];
        var document = DataTableSchema.DecodeTable(Table, chunk);
        Assert.Equal(1, (int)document["formatVersion"]!);
        Assert.Equal("faithful", (string)document["profile"]!);
        Assert.Equal("enemy-pos", (string)document["fileId"]!);
        Assert.Equal(2, document["records"]!.AsArray().Count);

        // Little-endian proof straight from the byte literals: pos[0][0].x = 100 is
        // 64 00 at byte 0 with .y = 110 right after as 6e 00, pos[0][2].x = 70 is
        // 46 00 at byte 8 (the row-major layout puts column 2 at words 4/5), and
        // pos[4][4].x = 90 is 5a 00 at byte 96. Record 2's pos[0][1].x = 0x1234 is
        // 34 12 at byte 104 with .y = 0xABCD as cd ab at 106.
        Assert.Equal(0x64, chunk[0]);
        Assert.Equal(0x6E, chunk[2]);
        Assert.Equal(0x46, chunk[8]);
        Assert.Equal(0x5A, chunk[96]);
        Assert.Equal(0x50, chunk[98]);
        Assert.Equal(0x34, chunk[104]);
        Assert.Equal(0x12, chunk[105]);
        Assert.Equal(0xCD, chunk[106]);
        Assert.Equal(0xAB, chunk[107]);

        var first = document["records"]![0]!.AsObject();
        Assert.Equal(new[] { "positions" }, first.Select(pair => pair.Key));
        var grid = first["positions"]!.AsArray();
        Assert.Equal(5, grid.Count); // exactly MAX_ENEMIES_IN_TEAM rows
        foreach (JsonNode? row in grid)
        {
            Assert.Equal(5, row!.AsArray().Count); // ...and columns
            foreach (JsonNode? cell in row.AsArray())
                Assert.Equal(new[] { "x", "y" }, cell!.AsObject().Select(pair => pair.Key));
        }

        Assert.Equal(100, (int)Row(first, 0)[0]!["x"]!);       // one enemy: (100, 110)
        Assert.Equal(110, (int)Row(first, 0)[0]!["y"]!);
        Assert.Equal(70, (int)Row(first, 0)[1]!["x"]!);        // two+: (70, 130), (70, 140) x3
        Assert.Equal(130, (int)Row(first, 0)[1]!["y"]!);
        Assert.Equal(140, (int)Row(first, 0)[4]!["y"]!);
        Assert.Equal(0, (int)Row(first, 1)[0]!["x"]!);         // unused cell stays {0,0}
        Assert.Equal(0, (int)Row(first, 1)[0]!["y"]!);
        Assert.Equal(140, (int)Row(first, 1)[1]!["x"]!);       // row 1's first live cell
        Assert.Equal(106, (int)Row(first, 1)[1]!["y"]!);
        Assert.Equal(160, (int)Row(first, 2)[2]!["x"]!);       // row 2 from column 2
        Assert.Equal(100, (int)Row(first, 2)[2]!["y"]!);
        Assert.Equal(50, (int)Row(first, 3)[3]!["x"]!);        // row 3 from column 3
        Assert.Equal(30, (int)Row(first, 3)[4]!["x"]!);
        Assert.Equal(110, (int)Row(first, 3)[4]!["y"]!);
        Assert.Equal(90, (int)Row(first, 4)[4]!["x"]!);        // five enemies: (90, 80)
        Assert.Equal(80, (int)Row(first, 4)[4]!["y"]!);

        var second = document["records"]![1]!.AsObject();
        Assert.Equal(65535, (int)Row(second, 0)[0]!["x"]!);    // both WORD boundaries
        Assert.Equal(65535, (int)Row(second, 0)[0]!["y"]!);
        Assert.Equal(4660, (int)Row(second, 0)[1]!["x"]!);     // 0x1234
        Assert.Equal(43981, (int)Row(second, 0)[1]!["y"]!);    // 0xABCD
        Assert.Equal(3, (int)Row(second, 3)[3]!["x"]!);        // a small interior cell
        Assert.Equal(0, (int)Row(second, 3)[3]!["y"]!);
        Assert.Equal(32768, (int)Row(second, 4)[4]!["x"]!);    // 0x8000 stays a WORD
        Assert.Equal(32768, (int)Row(second, 4)[4]!["y"]!);
    }

    [Fact]
    public void DescribeIndexEntry_reports_the_source_chunk_and_hand_verified_sha256()
    {
        byte[] chunk = new byte[100]; // one all-zero record
        var entry = DataTableSchema.DescribeIndexEntry(Table, chunk);
        Assert.Equal("enemy-pos.json", (string)entry["file"]!);
        Assert.Equal("DATA.MKF", (string)entry["archive"]!);
        Assert.Equal(13, (int)entry["chunk"]!);
        Assert.Equal(1, (int)entry["recordCount"]!);
        Assert.Equal(100, (int)entry["byteLength"]!);
        // Independent expectation: hand-verified SHA-256 of 100 zero bytes (Python
        // hashlib and PowerShell Get-FileHash both agree on this constant).
        Assert.Equal("cd00e292c5970d3c5e2f0ffa5171e555bc46bfc4faddfb4a418b6840b86e79a3", (string)entry["sha256"]!);
        // Cross-check against an in-test hash of the two hand-authored records.
        var two = DataTableSchema.DescribeIndexEntry(Table, [.. Record1, .. Record2]);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData([.. Record1, .. Record2])), (string)two["sha256"]!);
        Assert.Equal(2, (int)two["recordCount"]!);
        Assert.Equal(200, (int)two["byteLength"]!);
    }

    [Fact]
    public void Decode_rejects_chunk_13_lengths_that_are_not_a_positive_multiple_of_100()
    {
        var error = Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, new byte[101]));
        Assert.Contains("DATA.MKF", error.Message);
        Assert.Contains("chunk 13", error.Message);
        Assert.Contains("101", error.Message);
        Assert.Contains("100", error.Message);
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, Array.Empty<byte>())); // empty chunk is not a table
        Assert.Throws<InvalidDataException>(() => DataTableSchema.DecodeTable(Table, new byte[200 + 1])); // a trailing partial record is still not a table
    }

    [Fact]
    public void ValidateDocument_accepts_decoded_records_and_rejects_unknown_or_missing_fields()
    {
        DataTableSchema.ValidateDocument(Table, DataTableSchema.DecodeTable(Table, [.. Record1, .. Record2]));

        var cName = DataTableSchema.DecodeTable(Table, Record1);
        cName["records"]![0]!["pos"] = 0; // C name, not the frozen JSON name
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, cName));

        var unknownMember = DataTableSchema.DecodeTable(Table, Record1);
        unknownMember["records"]![0]!["positions"]![0]![0]!["w"] = 1; // C member name inside the sub-record
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, unknownMember));

        var missing = DataTableSchema.DecodeTable(Table, Record2);
        missing["records"]![0]!.AsObject().Remove("positions");
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, missing));

        var missingMember = DataTableSchema.DecodeTable(Table, Record2);
        missingMember["records"]![0]!["positions"]![3]![4]!.AsObject().Remove("y");
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, missingMember));
    }

    [Fact]
    public void ValidateDocument_rejects_out_of_range_values_and_grid_shapes_per_field()
    {
        // The PALPOS members at both unsigned boundaries, at several cells.
        foreach ((string member, int value) in new[] { ("x", -1), ("x", 65_536), ("y", -1), ("y", 65_536) })
        {
            foreach ((int row, int column) in new[] { (0, 0), (4, 4) })
            {
                var document = DataTableSchema.DecodeTable(Table, Record1);
                document["records"]![0]!["positions"]![row]![column]![member] = value;
                Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, document));
            }
        }
        // The grid must keep exactly five rows of five cells each.
        foreach (int length in new[] { 4, 6 })
        {
            var tall = DataTableSchema.DecodeTable(Table, Record1);
            tall["records"]![0]!["positions"] = new JsonArray(Enumerable.Range(0, length)
                .Select(_ => (JsonNode)new JsonArray(Enumerable.Range(0, 5)
                    .Select(_ => (JsonNode)new JsonObject { ["x"] = 0, ["y"] = 0 }).ToArray())).ToArray());
            Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, tall));
            var wide = DataTableSchema.DecodeTable(Table, Record1);
            wide["records"]![0]!["positions"] = new JsonArray(Enumerable.Range(0, 5)
                .Select(_ => (JsonNode)new JsonArray(Enumerable.Range(0, length)
                    .Select(_ => (JsonNode)new JsonObject { ["x"] = 0, ["y"] = 0 }).ToArray())).ToArray());
            Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, wide));
        }
        // A cell must be an object, not a scalar.
        var scalar = DataTableSchema.DecodeTable(Table, Record1);
        scalar["records"]![0]!["positions"]![2]![2] = 5;
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, scalar));
        // A cell member must stay an integer.
        var text = DataTableSchema.DecodeTable(Table, Record1);
        text["records"]![0]!["positions"]![1]![4]!["x"] = "100";
        Assert.Throws<InvalidDataException>(() => DataTableSchema.ValidateDocument(Table, text));
    }
}
