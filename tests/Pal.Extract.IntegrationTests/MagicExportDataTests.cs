using System.Buffers.Binary;
using System.Text.Json;
using Xunit;

namespace Pal.Extract.Tests;

// CLI-level proof of the magic table registration (#88): a miniature SSS.MKF whose
// three chunks are valid for the registered tables plus a hand-authored DATA.MKF
// chunk 4 must decode through `export-data` with the frozen magic fields and index
// manifest. DATA chunk 4 carries one normal record (type=0, the layerOffset view)
// and one summon record (type=9, the summonEffect view) - each record exports
// exactly its type-selected rgSpecific view, per #78's frozen dual-view contract.
public sealed class MagicExportDataTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"pal-export-data-magic-{Guid.NewGuid():N}");
    private readonly string input;

    public MagicExportDataTests()
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

    // Hand-authored little-endian MAGIC records (same literals as the unit tests):
    // record 1 is a normal magic whose rgSpecific 0xFFFB exports as layerOffset -5,
    // record 2 a summon magic (type=9) whose rgSpecific 1000 exports as the WORD
    // summonEffect; speed/sound decode signed on both.
    private static byte[] MagicRecord1 => Words(0x1234, 0, 65535, 3, -5,
        -32768, 0, 32767, 0xABCD, 1, 65535, 0,
        32768, 7, 2, 32767);
    private static byte[] MagicRecord2 => Words(5, 9, 0xABCD, 0, 1000,
        -1, 2, 65535, 1, 0, 4660, 65535,
        99, 32767, 5, -32768);

    private static byte[] Words(params int[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), unchecked((ushort)values[i]));
        return bytes;
    }

    [Fact]
    public async Task Export_data_extracts_magic_records_with_the_type_selected_view_and_the_index_entry()
    {
        // SSS chunk 2: five all-zero WIN95 records (70 = 5 x 14 bytes, not a multiple
        // of 12, so the variant stays decidable); the all-zero DATA evidence makes
        // record 0 the player view via rgwName[0] = 0 and records 1..4 undetermined.
        MkfFixture.Write(input, "SSS.MKF",
            [.. EventObjectRecord1, .. EventObjectRecord2], [.. SceneRecord1, .. SceneRecord2], new byte[70]);
        MkfFixture.Write(input, "DATA.MKF", new byte[18], new byte[70], new byte[10], new byte[900], [.. MagicRecord1, .. MagicRecord2], new byte[12]);
        string output = Path.Combine(root, "data");
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.True(result.ExitCode == 0, result.Stderr);
        Assert.Equal(new[] { Path.Combine(output, "battle-field.json"), Path.Combine(output, "enemy-team.json"), Path.Combine(output, "enemy.json"), Path.Combine(output, "event-object.json"), Path.Combine(output, "index.json"), Path.Combine(output, "magic.json"), Path.Combine(output, "object.json"), Path.Combine(output, "player-roles.json"), Path.Combine(output, "scene.json"), Path.Combine(output, "store.json") },
            Directory.GetFiles(output).Order());

        using var table = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "magic.json")));
        Assert.Equal(1, table.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal("faithful", table.RootElement.GetProperty("profile").GetString());
        Assert.Equal("magic", table.RootElement.GetProperty("fileId").GetString());
        var records = table.RootElement.GetProperty("records");
        Assert.Equal(2, records.GetArrayLength());

        // Record 1: the normal type=0 record exports the SHORT layerOffset view.
        var normal = records[0];
        Assert.Equal(0x1234, normal.GetProperty("effect").GetInt32());
        Assert.Equal(0, normal.GetProperty("type").GetInt32());
        Assert.Equal(65535, normal.GetProperty("xOffset").GetInt32());
        Assert.Equal(3, normal.GetProperty("yOffset").GetInt32());
        Assert.Equal(-5, normal.GetProperty("layerOffset").GetInt32());
        Assert.Equal(-32768, normal.GetProperty("speed").GetInt32());
        Assert.Equal(0, normal.GetProperty("keepEffect").GetInt32());
        Assert.Equal(32767, normal.GetProperty("fireDelay").GetInt32());
        Assert.Equal(0xABCD, normal.GetProperty("effectTimes").GetInt32());
        Assert.Equal(1, normal.GetProperty("shake").GetInt32());
        Assert.Equal(65535, normal.GetProperty("wave").GetInt32());
        Assert.Equal(0, normal.GetProperty("unknown").GetInt32());
        Assert.Equal(32768, normal.GetProperty("costMp").GetInt32());
        Assert.Equal(7, normal.GetProperty("baseDamage").GetInt32());
        Assert.Equal(2, normal.GetProperty("elemental").GetInt32());
        Assert.Equal(32767, normal.GetProperty("sound").GetInt32());
        Assert.False(normal.TryGetProperty("summonEffect", out _)); // one view per record

        // Record 2: the type=9 summon record exports the WORD summonEffect view.
        var summon = records[1];
        Assert.Equal(5, summon.GetProperty("effect").GetInt32());
        Assert.Equal(9, summon.GetProperty("type").GetInt32());
        Assert.Equal(0xABCD, summon.GetProperty("xOffset").GetInt32());
        Assert.Equal(0, summon.GetProperty("yOffset").GetInt32());
        Assert.Equal(1000, summon.GetProperty("summonEffect").GetInt32());
        Assert.Equal(-1, summon.GetProperty("speed").GetInt32());
        Assert.Equal(2, summon.GetProperty("keepEffect").GetInt32());
        Assert.Equal(65535, summon.GetProperty("fireDelay").GetInt32());
        Assert.Equal(1, summon.GetProperty("effectTimes").GetInt32());
        Assert.Equal(0, summon.GetProperty("shake").GetInt32());
        Assert.Equal(4660, summon.GetProperty("wave").GetInt32());
        Assert.Equal(65535, summon.GetProperty("unknown").GetInt32());
        Assert.Equal(99, summon.GetProperty("costMp").GetInt32());
        Assert.Equal(32767, summon.GetProperty("baseDamage").GetInt32());
        Assert.Equal(5, summon.GetProperty("elemental").GetInt32());
        Assert.Equal(-32768, summon.GetProperty("sound").GetInt32());
        Assert.False(summon.TryGetProperty("layerOffset", out _)); // one view per record

        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "index.json")));
        var tables = index.RootElement.GetProperty("tables").EnumerateArray().ToArray();
        Assert.Equal(9, tables.Length);
        Assert.Equal("battle-field.json", tables[0].GetProperty("file").GetString()); // lexicographic file order
        Assert.Equal("magic.json", tables[4].GetProperty("file").GetString());      // between event-object and object
        var entry = tables.Single(table => table.GetProperty("file").GetString() == "magic.json");
        Assert.Equal("DATA.MKF", entry.GetProperty("archive").GetString());
        Assert.Equal(4, entry.GetProperty("chunk").GetInt32());
        Assert.Equal(2, entry.GetProperty("recordCount").GetInt32());
        Assert.Equal(64, entry.GetProperty("byteLength").GetInt32());
        // Independent expectation: hand-verified SHA-256 of the 64 literal bytes above.
        Assert.Equal("1fb7598fc1d0130c299cdea3e530852d3fe3ca942c3b6ae7e960deda20be5b98",
            entry.GetProperty("sha256").GetString());
    }

    [Fact]
    public async Task Export_data_fails_when_data_chunk_4_is_not_a_multiple_of_32_bytes()
    {
        MkfFixture.Write(input, "SSS.MKF",
            [.. EventObjectRecord1, .. EventObjectRecord2], [.. SceneRecord1, .. SceneRecord2], new byte[70]);
        MkfFixture.Write(input, "DATA.MKF", new byte[18], new byte[70], new byte[10], new byte[900], new byte[33], new byte[12]); // chunk 4 defect only
        string output = Path.Combine(root, "data");
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("DATA.MKF", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("chunk 4", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("33", result.Stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(output)); // no partial output is written
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
