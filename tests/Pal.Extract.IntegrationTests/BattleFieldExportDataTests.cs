using System.Buffers.Binary;
using System.Text.Json;
using Xunit;

namespace Pal.Extract.Tests;

// CLI-level proof of the battle-field table registration (#89): a miniature SSS.MKF
// whose three chunks are valid for the registered tables plus a hand-authored DATA.MKF
// chunk 5 must decode through `export-data` with the frozen battle-field fields and
// index manifest. Both records exercise the signed magicEffect[5] array - including
// the 0x8000 bit pattern that stays an unsigned screenWave WORD in record 2 - per the
// #78 frozen signed annotation (fight.c:244 reads it signed).
public sealed class BattleFieldExportDataTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"pal-export-data-battle-field-{Guid.NewGuid():N}");
    private readonly string input;

    public BattleFieldExportDataTests()
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

    // Hand-authored little-endian BATTLEFIELD records (same literals as the unit
    // tests): record 1's magicEffect covers both signed boundaries plus -1/-5, and
    // record 2 reuses the 0x8000 pattern as an unsigned screenWave against a
    // -32768 magicEffect[3].
    private static byte[] BattleFieldRecord1 => Words(65535, -32768, -1, -5, 32767, 4660);
    private static byte[] BattleFieldRecord2 => Words(32768, 1, -2, 0, -32768, 5);

    private static byte[] Words(params int[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), unchecked((ushort)values[i]));
        return bytes;
    }

    [Fact]
    public async Task Export_data_extracts_battle_field_records_and_the_index_entry()
    {
        // SSS chunk 2: five all-zero WIN95 records (70 = 5 x 14 bytes, not a multiple
        // of 12, so the variant stays decidable); the all-zero DATA evidence makes
        // record 0 the player view via rgwName[0] = 0 and records 1..4 undetermined.
        MkfFixture.Write(input, "SSS.MKF",
            [.. EventObjectRecord1, .. EventObjectRecord2], [.. SceneRecord1, .. SceneRecord2], new byte[70]);
        MkfFixture.Write(input, "DATA.MKF", new byte[18], new byte[70], new byte[10], new byte[900], new byte[32], [.. BattleFieldRecord1, .. BattleFieldRecord2]);
        string output = Path.Combine(root, "data");
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.True(result.ExitCode == 0, result.Stderr);
        Assert.Equal(new[] { Path.Combine(output, "battle-field.json"), Path.Combine(output, "enemy-team.json"), Path.Combine(output, "enemy.json"), Path.Combine(output, "event-object.json"), Path.Combine(output, "index.json"), Path.Combine(output, "magic.json"), Path.Combine(output, "object.json"), Path.Combine(output, "player-roles.json"), Path.Combine(output, "scene.json"), Path.Combine(output, "store.json") },
            Directory.GetFiles(output).Order());

        using var table = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "battle-field.json")));
        Assert.Equal(1, table.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal("faithful", table.RootElement.GetProperty("profile").GetString());
        Assert.Equal("battle-field", table.RootElement.GetProperty("fileId").GetString());
        var records = table.RootElement.GetProperty("records");
        Assert.Equal(2, records.GetArrayLength());

        var first = records[0];
        Assert.Equal(65535, first.GetProperty("screenWave").GetInt32());
        Assert.Equal(new[] { -32768, -1, -5, 32767, 4660 },
            first.GetProperty("magicEffect").EnumerateArray().Select(node => node.GetInt32()).ToArray());

        var second = records[1];
        Assert.Equal(32768, second.GetProperty("screenWave").GetInt32()); // 0x8000 stays a WORD
        Assert.Equal(new[] { 1, -2, 0, -32768, 5 },
            second.GetProperty("magicEffect").EnumerateArray().Select(node => node.GetInt32()).ToArray());

        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "index.json")));
        var tables = index.RootElement.GetProperty("tables").EnumerateArray().ToArray();
        Assert.Equal(9, tables.Length);
        Assert.Equal("battle-field.json", tables[0].GetProperty("file").GetString()); // lexicographic file order
        var entry = tables.Single(table => table.GetProperty("file").GetString() == "battle-field.json");
        Assert.Equal("DATA.MKF", entry.GetProperty("archive").GetString());
        Assert.Equal(5, entry.GetProperty("chunk").GetInt32());
        Assert.Equal(2, entry.GetProperty("recordCount").GetInt32());
        Assert.Equal(24, entry.GetProperty("byteLength").GetInt32());
        // Independent expectation: hand-verified SHA-256 of the 24 literal bytes above
        // (Python hashlib and PowerShell Get-FileHash both agree on this constant).
        Assert.Equal("2228e45076473eeadb2fa7726263da0bb386d1a4c00e772514191965ac1a70e5",
            entry.GetProperty("sha256").GetString());
    }

    [Fact]
    public async Task Export_data_fails_when_data_chunk_5_is_not_a_multiple_of_12_bytes()
    {
        MkfFixture.Write(input, "SSS.MKF",
            [.. EventObjectRecord1, .. EventObjectRecord2], [.. SceneRecord1, .. SceneRecord2], new byte[70]);
        MkfFixture.Write(input, "DATA.MKF", new byte[18], new byte[70], new byte[10], new byte[900], new byte[32], new byte[13]); // chunk 5 defect only
        string output = Path.Combine(root, "data");
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("DATA.MKF", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("chunk 5", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("13", result.Stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(output)); // no partial output is written
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
