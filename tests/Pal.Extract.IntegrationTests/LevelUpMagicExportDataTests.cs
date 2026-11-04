using System.Buffers.Binary;
using System.Text.Json;
using Xunit;

namespace Pal.Extract.Tests;

// CLI-level proof of the level-up-magic table registration (#90): a miniature SSS.MKF
// whose three chunks are valid for the registered tables plus a hand-authored DATA.MKF
// chunk 6 must decode through `export-data` with the frozen roles sub-record array and
// the index manifest. Both 20-byte records cover the WORD boundaries (65535, 0x8000),
// the no-magic 0 sentinel, and the 0x1234/0xABCD byte-swap sentinels, per the #78
// frozen schema (battle.c:1302-1309 reads wLevel/wMagic unsigned, 0 = learns nothing).
public sealed class LevelUpMagicExportDataTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"pal-export-data-level-up-magic-{Guid.NewGuid():N}");
    private readonly string input;

    public LevelUpMagicExportDataTests()
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

    // Hand-authored little-endian LEVELUPMAGIC_ALL records (same literals as the unit
    // tests): record 1's role 1 hits both WORD boundaries and role 2 carries the
    // 0x1234/0xABCD sentinels; record 2's role 0 reuses the 0x8000 pattern in both
    // words and role 4 pairs the level boundary with the no-magic 0.
    private static byte[] LevelUpMagicRecord1 => Words(0, 0, 65535, 65535, 4660, 43981, 1, 0, 99, 295);
    private static byte[] LevelUpMagicRecord2 => Words(32768, 32768, 2, 392, 5, 1, 10, 600, 65535, 0);

    private static byte[] Words(params int[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), unchecked((ushort)values[i]));
        return bytes;
    }

    [Fact]
    public async Task Export_data_extracts_level_up_magic_records_and_the_index_entry()
    {
        // SSS chunk 2: five all-zero WIN95 records (70 = 5 x 14 bytes, not a multiple
        // of 12, so the variant stays decidable); the all-zero DATA evidence makes
        // record 0 the player view via rgwName[0] = 0 and records 1..4 undetermined.
        MkfFixture.Write(input, "SSS.MKF",
            [.. EventObjectRecord1, .. EventObjectRecord2], [.. SceneRecord1, .. SceneRecord2], new byte[70]);
        MkfFixture.Write(input, "DATA.MKF", new byte[18], new byte[70], new byte[10], new byte[900], new byte[32], new byte[12], [.. LevelUpMagicRecord1, .. LevelUpMagicRecord2]);
        string output = Path.Combine(root, "data");
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.True(result.ExitCode == 0, result.Stderr);
        Assert.Equal(new[] { Path.Combine(output, "battle-field.json"), Path.Combine(output, "enemy-team.json"), Path.Combine(output, "enemy.json"), Path.Combine(output, "event-object.json"), Path.Combine(output, "index.json"), Path.Combine(output, "level-up-magic.json"), Path.Combine(output, "magic.json"), Path.Combine(output, "object.json"), Path.Combine(output, "player-roles.json"), Path.Combine(output, "scene.json"), Path.Combine(output, "store.json") },
            Directory.GetFiles(output).Order());

        using var table = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "level-up-magic.json")));
        Assert.Equal(1, table.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal("faithful", table.RootElement.GetProperty("profile").GetString());
        Assert.Equal("level-up-magic", table.RootElement.GetProperty("fileId").GetString());
        var records = table.RootElement.GetProperty("records");
        Assert.Equal(2, records.GetArrayLength());

        var first = records[0].GetProperty("roles");
        Assert.Equal(5, first.GetArrayLength()); // exactly MAX_PLAYABLE_PLAYER_ROLES elements
        Assert.Equal(0, first[0].GetProperty("level").GetInt32());
        Assert.Equal(0, first[0].GetProperty("magicId").GetInt32()); // learns nothing
        Assert.Equal(65535, first[1].GetProperty("level").GetInt32());
        Assert.Equal(65535, first[1].GetProperty("magicId").GetInt32()); // both WORD boundaries
        Assert.Equal(4660, first[2].GetProperty("level").GetInt32());   // 0x1234
        Assert.Equal(43981, first[2].GetProperty("magicId").GetInt32()); // 0xABCD
        Assert.Equal(1, first[3].GetProperty("level").GetInt32());
        Assert.Equal(0, first[3].GetProperty("magicId").GetInt32());
        Assert.Equal(99, first[4].GetProperty("level").GetInt32());
        Assert.Equal(295, first[4].GetProperty("magicId").GetInt32());

        var second = records[1].GetProperty("roles");
        Assert.Equal(32768, second[0].GetProperty("level").GetInt32());   // 0x8000 stays a WORD
        Assert.Equal(32768, second[0].GetProperty("magicId").GetInt32());
        Assert.Equal(2, second[1].GetProperty("level").GetInt32());
        Assert.Equal(392, second[1].GetProperty("magicId").GetInt32());
        Assert.Equal(5, second[2].GetProperty("level").GetInt32());
        Assert.Equal(1, second[2].GetProperty("magicId").GetInt32());
        Assert.Equal(10, second[3].GetProperty("level").GetInt32());
        Assert.Equal(600, second[3].GetProperty("magicId").GetInt32());
        Assert.Equal(65535, second[4].GetProperty("level").GetInt32());
        Assert.Equal(0, second[4].GetProperty("magicId").GetInt32());

        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "index.json")));
        var tables = index.RootElement.GetProperty("tables").EnumerateArray().ToArray();
        Assert.Equal(10, tables.Length);
        Assert.Equal("battle-field.json", tables[0].GetProperty("file").GetString()); // lexicographic file order
        Assert.Equal("level-up-magic.json", tables[4].GetProperty("file").GetString()); // between event-object and magic
        var entry = tables.Single(table => table.GetProperty("file").GetString() == "level-up-magic.json");
        Assert.Equal("DATA.MKF", entry.GetProperty("archive").GetString());
        Assert.Equal(6, entry.GetProperty("chunk").GetInt32());
        Assert.Equal(2, entry.GetProperty("recordCount").GetInt32());
        Assert.Equal(40, entry.GetProperty("byteLength").GetInt32());
        // Independent expectation: hand-verified SHA-256 of the 40 literal bytes above
        // (Python hashlib and PowerShell Get-FileHash both agree on this constant).
        Assert.Equal("e6cc4f51c973a4d5f958f3548a9476ae999d1b1fca7ac4f02b29a79be71f1d14",
            entry.GetProperty("sha256").GetString());
    }

    [Fact]
    public async Task Export_data_fails_when_data_chunk_6_is_not_a_multiple_of_20_bytes()
    {
        MkfFixture.Write(input, "SSS.MKF",
            [.. EventObjectRecord1, .. EventObjectRecord2], [.. SceneRecord1, .. SceneRecord2], new byte[70]);
        MkfFixture.Write(input, "DATA.MKF", new byte[18], new byte[70], new byte[10], new byte[900], new byte[32], new byte[12], new byte[21]); // chunk 6 defect only
        string output = Path.Combine(root, "data");
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("DATA.MKF", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("chunk 6", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("21", result.Stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(output)); // no partial output is written
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
