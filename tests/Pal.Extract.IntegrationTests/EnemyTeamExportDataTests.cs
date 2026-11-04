using System.Buffers.Binary;
using System.Text.Json;
using Xunit;

namespace Pal.Extract.Tests;

// CLI-level proof of the enemy-team table registration (#85): a miniature SSS.MKF
// whose three chunks are valid for the registered tables plus a hand-authored
// DATA.MKF chunk 2 must decode through `export-data` with the frozen enemy lists
// and index manifest. DATA chunk 2 is both the exported enemy-team table and the
// enemy-view evidence source, so the team literals here double as valid evidence:
// the nonzero references 2..4 stay inside the 5-record object chunk and stay
// disjoint from the player evidence (object 0 is named by the all-zero roles).
public sealed class EnemyTeamExportDataTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"pal-export-data-enemy-team-{Guid.NewGuid():N}");
    private readonly string input;

    public EnemyTeamExportDataTests()
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

    // Hand-authored little-endian ENEMYTEAM records: record 0 fields objects 3 and 4
    // around one skip (0xFFFF) and one empty slot (0), record 1 is a team of a single
    // skipped leader with empty slots (battle.c:1601-1615 skips 0 and 0xFFFF).
    private static byte[] EnemyTeamRecord1 => Words(2, 65535, 0, 3, 4);
    private static byte[] EnemyTeamRecord2 => Words(65535, 0, 0, 0, 0);

    private static byte[] Words(params int[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), unchecked((ushort)values[i]));
        return bytes;
    }

    [Fact]
    public async Task Export_data_extracts_enemy_team_records_and_the_index_entry()
    {
        // Chunk 2: five all-zero WIN95 records (70 = 5 x 14 bytes, not a multiple of
        // 12, so the variant stays decidable). Record 0 becomes the player view via
        // rgwName[0] = 0 in the all-zero roles; the team literals below decide
        // records 2, 3, and 4 as enemies; record 1 stays undetermined.
        MkfFixture.Write(input, "SSS.MKF",
            [.. EventObjectRecord1, .. EventObjectRecord2], [.. SceneRecord1, .. SceneRecord2], new byte[70]);
        MkfFixture.Write(input, "DATA.MKF", new byte[18], new byte[70], [.. EnemyTeamRecord1, .. EnemyTeamRecord2], new byte[900], new byte[32]);
        string output = Path.Combine(root, "data");
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.True(result.ExitCode == 0, result.Stderr);
        Assert.Equal(new[] { Path.Combine(output, "enemy-team.json"), Path.Combine(output, "enemy.json"), Path.Combine(output, "event-object.json"), Path.Combine(output, "index.json"), Path.Combine(output, "magic.json"), Path.Combine(output, "object.json"), Path.Combine(output, "player-roles.json"), Path.Combine(output, "scene.json"), Path.Combine(output, "store.json") },
            Directory.GetFiles(output).Order());

        using var table = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "enemy-team.json")));
        Assert.Equal(1, table.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal("faithful", table.RootElement.GetProperty("profile").GetString());
        Assert.Equal("enemy-team", table.RootElement.GetProperty("fileId").GetString());
        var records = table.RootElement.GetProperty("records");
        Assert.Equal(2, records.GetArrayLength());
        AssertEnemies(records[0], 2, 65535, 0, 3, 4);
        AssertEnemies(records[1], 65535, 0, 0, 0, 0);

        // The same DATA chunk 2 feeds the object enemy-view evidence: the nonzero
        // members 2, 3, and 4 mark those objects as enemies (0 is an empty slot and
        // 0xFFFF a skip, so neither is evidence), and record 1 stays undetermined.
        using var objects = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "object.json")));
        var objectRecords = objects.RootElement.GetProperty("records");
        Assert.Equal(5, objectRecords.GetArrayLength());
        Assert.Equal("player", objectRecords[0].GetProperty("view").GetString());
        Assert.Equal("undetermined", objectRecords[1].GetProperty("view").GetString());
        for (int objectIndex = 2; objectIndex <= 4; objectIndex++)
            Assert.Equal("enemy", objectRecords[objectIndex].GetProperty("view").GetString());

        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "index.json")));
        var tables = index.RootElement.GetProperty("tables").EnumerateArray().ToArray();
        Assert.Equal(8, tables.Length);
        Assert.Equal("enemy-team.json", tables[0].GetProperty("file").GetString()); // lexicographic file order
        var entry = tables.Single(table => table.GetProperty("file").GetString() == "enemy-team.json");
        Assert.Equal("DATA.MKF", entry.GetProperty("archive").GetString());
        Assert.Equal(2, entry.GetProperty("chunk").GetInt32());
        Assert.Equal(2, entry.GetProperty("recordCount").GetInt32());
        Assert.Equal(20, entry.GetProperty("byteLength").GetInt32());
        // Independent expectation: hand-verified SHA-256 of the 20 literal bytes above.
        Assert.Equal("da51fe4841baa824677130f87f6e8dcb77e75185e1481ed301fdfe1599ef50cb",
            entry.GetProperty("sha256").GetString());
    }

    [Fact]
    public async Task Export_data_fails_when_data_chunk_2_is_not_a_multiple_of_10_bytes()
    {
        MkfFixture.Write(input, "SSS.MKF",
            [.. EventObjectRecord1, .. EventObjectRecord2], [.. SceneRecord1, .. SceneRecord2], new byte[70]);
        MkfFixture.Write(input, "DATA.MKF", new byte[18], new byte[70], new byte[11], new byte[900], new byte[32]); // chunk 2 defect only
        string output = Path.Combine(root, "data");
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("DATA.MKF", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("chunk 2", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("11", result.Stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(output)); // no partial output is written
    }

    private static void AssertEnemies(JsonElement record, params int[] expected)
    {
        var enemies = record.GetProperty("enemies");
        Assert.Equal(expected.Length, enemies.GetArrayLength());
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], enemies[i].GetInt32());
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
