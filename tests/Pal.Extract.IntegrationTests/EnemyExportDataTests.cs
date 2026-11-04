using System.Buffers.Binary;
using System.Text.Json;
using Xunit;

namespace Pal.Extract.Tests;

// CLI-level proof of the enemy table registration (#84): a miniature SSS.MKF whose
// three chunks are valid for the registered tables plus a hand-authored DATA.MKF
// chunk 1 must decode through `export-data` with the frozen enemy fields and index
// manifest. DATA chunk 1 is both the exported enemy table and the magic/item-view
// evidence source, so the enemy literals here double as valid evidence: record 0's
// attackEquivItemId 3 / stealItemId 4 stay inside the 5-record object chunk (its
// magicId 0xFFFF means no magic and is not evidence), record 1's magicId 2 names the
// object-2 magic view; both records' negative sound WORDs must survive the evidence
// read untouched.
public sealed class EnemyExportDataTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"pal-export-data-enemy-{Guid.NewGuid():N}");
    private readonly string input;

    public EnemyExportDataTests()
    {
        input = Path.Combine(root, "input");
        Directory.CreateDirectory(input);
    }

    // Hand-authored little-endian EVENTOBJECT/SCENE records (same literals as the
    // #79/#80 tests): chunks 0 and 1 must stay valid now that export-data registers
    // more than the SSS tables.
    private static byte[] EventObjectRecord1 => Words(-32768, 0x1234, 0, -1, 0x8000, 65535, unchecked((short)0x8000), 8, 0xABCD, 0, 3, 32767, 1, 65535, 2, 32768);
    private static byte[] EventObjectRecord2 => Words(-15, 1840, 1200, 2, 10000, 0, 32767, 5, 0x1234, 65535, 0, 0, 0, 0, 4660, 0);
    private static byte[] SceneRecord1 => Words(65535, 0x1234, 0, 65535);
    private static byte[] SceneRecord2 => Words(1, 32768, 0xABCD, 5077);

    // Hand-authored little-endian ENEMY records (same literals as the unit tests,
    // except that the evidence members stay inside the 5-record object chunk):
    // record 0 carries all boundary sounds (-32768, -1, 32767, -15, 0) and sells no
    // magic (magicId 0xFFFF), record 1 a plausible small enemy casting object 2.
    private static byte[] EnemyRecord1 => Words(65535, 0x1234, 0, 32767, 0xABCD, 1,
        -32768, -1, 32767, -15, 0,
        65535, 4660, 0, 32768, 65535, 0,
        3, 65535, 4, 32768,
        0xABCD, 3, 65535, 32767, 0, 5,
        65535, 0, 32767, 0x1234, 65535,
        0, 1, 32768);
    private static byte[] EnemyRecord2 => Words(6, 7, 5, 3, 8, 0,
        11, -2, 12, 13, -3,
        60, 5, 10, 3, 2, 25,
        0, 40, 0, 0,
        20, 12, 14, 18, 50, 3,
        0, 1, 2, 3, 4,
        5, 0, 0);

    private static byte[] Words(params int[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), unchecked((ushort)values[i]));
        return bytes;
    }

    [Fact]
    public async Task Export_data_extracts_enemy_records_and_the_index_entry()
    {
        // SSS chunk 2: five all-zero WIN95 records (70 = 5 x 14 bytes, not a multiple
        // of 12, so the variant stays decidable). Record 0 becomes the player view via
        // rgwName[0] = 0 in the all-zero roles; the enemy evidence below decides
        // records 2 (magic) and 3/4 (item); record 1 stays undetermined.
        MkfFixture.Write(input, "SSS.MKF",
            [.. EventObjectRecord1, .. EventObjectRecord2], [.. SceneRecord1, .. SceneRecord2], new byte[70]);
        MkfFixture.Write(input, "DATA.MKF", new byte[18], [.. EnemyRecord1, .. EnemyRecord2], new byte[10], new byte[900], new byte[32], new byte[12]);
        string output = Path.Combine(root, "data");
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.True(result.ExitCode == 0, result.Stderr);
        Assert.Equal(new[] { Path.Combine(output, "battle-field.json"), Path.Combine(output, "enemy-team.json"), Path.Combine(output, "enemy.json"), Path.Combine(output, "event-object.json"), Path.Combine(output, "index.json"), Path.Combine(output, "magic.json"), Path.Combine(output, "object.json"), Path.Combine(output, "player-roles.json"), Path.Combine(output, "scene.json"), Path.Combine(output, "store.json") },
            Directory.GetFiles(output).Order());

        using var table = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "enemy.json")));
        Assert.Equal(1, table.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal("faithful", table.RootElement.GetProperty("profile").GetString());
        Assert.Equal("enemy", table.RootElement.GetProperty("fileId").GetString());
        var records = table.RootElement.GetProperty("records");
        Assert.Equal(2, records.GetArrayLength());
        AssertScalarFields(records[0], 65535, 0x1234, 0, 32767, 0xABCD, 1,
            -32768, -1, 32767, -15, 0,
            65535, 4660, 0, 32768, 65535, 0,
            3, 65535, 4, 32768,
            0xABCD, 3, 65535, 32767, 0, 5,
            0, 1, 32768);
        AssertElementalResistance(records[0], 65535, 0, 32767, 0x1234, 65535);
        AssertScalarFields(records[1], 6, 7, 5, 3, 8, 0,
            11, -2, 12, 13, -3,
            60, 5, 10, 3, 2, 25,
            0, 40, 0, 0,
            20, 12, 14, 18, 50, 3,
            5, 0, 0);
        AssertElementalResistance(records[1], 0, 1, 2, 3, 4);

        // The same DATA chunk 1 feeds the object view evidence: record 0's
        // attackEquivItemId 3 / stealItemId 4 mark objects 3 and 4 as items, record
        // 1's magicId 2 marks object 2 as a magic (the 0xFFFF of record 0 is no
        // magic and no evidence), and record 1's zero item members add nothing.
        using var objects = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "object.json")));
        var objectRecords = objects.RootElement.GetProperty("records");
        Assert.Equal(5, objectRecords.GetArrayLength());
        Assert.Equal("player", objectRecords[0].GetProperty("view").GetString());
        Assert.Equal("undetermined", objectRecords[1].GetProperty("view").GetString());
        Assert.Equal("magic", objectRecords[2].GetProperty("view").GetString());
        Assert.Equal("item", objectRecords[3].GetProperty("view").GetString());
        Assert.Equal("item", objectRecords[4].GetProperty("view").GetString());

        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "index.json")));
        var tables = index.RootElement.GetProperty("tables").EnumerateArray().ToArray();
        Assert.Equal(9, tables.Length);
        Assert.Equal("battle-field.json", tables[0].GetProperty("file").GetString()); // lexicographic file order
        Assert.Equal("enemy-team.json", tables[1].GetProperty("file").GetString());
        Assert.Equal("enemy.json", tables[2].GetProperty("file").GetString());
        var entry = tables.Single(table => table.GetProperty("file").GetString() == "enemy.json");
        Assert.Equal("DATA.MKF", entry.GetProperty("archive").GetString());
        Assert.Equal(1, entry.GetProperty("chunk").GetInt32());
        Assert.Equal(2, entry.GetProperty("recordCount").GetInt32());
        Assert.Equal(140, entry.GetProperty("byteLength").GetInt32());
        // Independent expectation: hand-verified SHA-256 of the 140 literal bytes above.
        Assert.Equal("ae8dd25192f6fe8911b6c1de3bf4fbdfc07fdf3844f0a28b47ef73f867334f57",
            entry.GetProperty("sha256").GetString());
    }

    [Fact]
    public async Task Export_data_fails_when_data_chunk_1_is_not_a_multiple_of_70_bytes()
    {
        MkfFixture.Write(input, "SSS.MKF",
            [.. EventObjectRecord1, .. EventObjectRecord2], [.. SceneRecord1, .. SceneRecord2], new byte[70]);
        MkfFixture.Write(input, "DATA.MKF", new byte[18], new byte[71], new byte[10], new byte[900], new byte[32], new byte[12]); // chunk 1 defect only
        string output = Path.Combine(root, "data");
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("DATA.MKF", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("chunk 1", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("71", result.Stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(output)); // no partial output is written
    }

    // Asserts the 30 scalar fields in frozen-schema order; elementalResistance is
    // checked separately because it is the one array member.
    private static void AssertScalarFields(JsonElement record, params int[] expected)
    {
        string[] fields =
        [
            "idleFrames", "magicFrames", "attackFrames", "idleAnimSpeed", "actWaitFrames", "yPosOffset",
            "attackSound", "actionSound", "magicSound", "deathSound", "callSound",
            "health", "exp", "cash", "level", "magicId", "magicRate",
            "attackEquivItemId", "attackEquivItemRate", "stealItemId", "stealItemCount",
            "attackStrength", "magicStrength", "defense", "dexterity", "fleeRate", "poisonResistance",
            "physicalResistance", "dualMove", "collectValue",
        ];
        Assert.Equal(fields.Length, expected.Length);
        for (int i = 0; i < fields.Length; i++)
            Assert.Equal(expected[i], record.GetProperty(fields[i]).GetInt32());
    }

    private static void AssertElementalResistance(JsonElement record, params int[] expected)
    {
        var elemental = record.GetProperty("elementalResistance");
        Assert.Equal(expected.Length, elemental.GetArrayLength());
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], elemental[i].GetInt32());
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
