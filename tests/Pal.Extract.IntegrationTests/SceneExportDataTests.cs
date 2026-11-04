using System.Buffers.Binary;
using System.Text.Json;
using Xunit;

namespace Pal.Extract.Tests;

// CLI-level proof of the scene table registration (#80): a miniature SSS.MKF whose
// chunk 0 is hand-authored EVENTOBJECT records and whose chunk 1 is hand-authored
// SCENE records, plus the DATA.MKF view-evidence chunks, must decode through
// `export-data` with the frozen field values and index manifest (all registered tables).
public sealed class SceneExportDataTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"pal-export-data-scene-{Guid.NewGuid():N}");
    private readonly string input;

    public SceneExportDataTests()
    {
        input = Path.Combine(root, "input");
        Directory.CreateDirectory(input);
    }

    // Hand-authored little-endian EVENTOBJECT records (same literals as the #79 tests):
    // chunk 0 must stay valid now that export-data registers more than one table.
    private static byte[] EventObjectRecord1 => Words(-32768, 0x1234, 0, -1, 0x8000, 65535, unchecked((short)0x8000), 8, 0xABCD, 0, 3, 32767, 1, 65535, 2, 32768);
    private static byte[] EventObjectRecord2 => Words(-15, 1840, 1200, 2, 10000, 0, 32767, 5, 0x1234, 65535, 0, 0, 0, 0, 4660, 0);

    // Hand-authored little-endian SCENE records (same literals as the unit tests):
    // boundaries 0, 65535, 0x8000, and 0x13D5 = the real event-object count 5077.
    private static byte[] SceneRecord1 => Words(65535, 0x1234, 0, 65535);
    private static byte[] SceneRecord2 => Words(1, 32768, 0xABCD, 5077);

    private static byte[] Words(params int[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), unchecked((ushort)values[i]));
        return bytes;
    }

    [Fact]
    public async Task Export_data_extracts_scene_records_and_the_index_entry()
    {
        MkfFixture.Write(input, "SSS.MKF", [.. EventObjectRecord1, .. EventObjectRecord2], [.. SceneRecord1, .. SceneRecord2], new byte[28]);
        MkfFixture.Write(input, "DATA.MKF", new byte[18], new byte[70], new byte[10], new byte[900]);
        string output = Path.Combine(root, "data");
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.True(result.ExitCode == 0, result.Stderr);
        Assert.Equal(new[] { Path.Combine(output, "enemy.json"), Path.Combine(output, "event-object.json"), Path.Combine(output, "index.json"), Path.Combine(output, "object.json"), Path.Combine(output, "scene.json"), Path.Combine(output, "store.json") },
            Directory.GetFiles(output).Order());

        using var table = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "scene.json")));
        Assert.Equal(1, table.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal("faithful", table.RootElement.GetProperty("profile").GetString());
        Assert.Equal("scene", table.RootElement.GetProperty("fileId").GetString());
        var records = table.RootElement.GetProperty("records");
        Assert.Equal(2, records.GetArrayLength());
        AssertAllFields(records[0], 65535, 0x1234, 0, 65535);
        AssertAllFields(records[1], 1, 32768, 0xABCD, 5077);

        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "index.json")));
        var tables = index.RootElement.GetProperty("tables").EnumerateArray().ToArray();
        Assert.Equal(5, tables.Length);
        Assert.Equal("enemy.json", tables[0].GetProperty("file").GetString()); // lexicographic file order
        Assert.Equal("event-object.json", tables[1].GetProperty("file").GetString());
        var entry = tables.Single(table => table.GetProperty("file").GetString() == "scene.json");
        Assert.Equal("SSS.MKF", entry.GetProperty("archive").GetString());
        Assert.Equal(1, entry.GetProperty("chunk").GetInt32());
        Assert.Equal(2, entry.GetProperty("recordCount").GetInt32());
        Assert.Equal(16, entry.GetProperty("byteLength").GetInt32());
        // Independent expectation: hand-verified SHA-256 of the 16 literal bytes above.
        Assert.Equal("806aa91a764c68a93e3058d9d58624d52f0804981be4bac1d4765b8fa133690d",
            entry.GetProperty("sha256").GetString());
    }

    [Fact]
    public async Task Export_data_fails_when_chunk_1_is_not_a_multiple_of_8_bytes()
    {
        MkfFixture.Write(input, "SSS.MKF", [.. EventObjectRecord1, .. EventObjectRecord2], new byte[9], new byte[28]);
        MkfFixture.Write(input, "DATA.MKF", new byte[18], new byte[70], new byte[10], new byte[900]);
        string output = Path.Combine(root, "data");
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("SSS.MKF", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("chunk 1", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("9", result.Stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(output)); // no partial output is written
    }

    private static void AssertAllFields(JsonElement record, params int[] expected)
    {
        string[] fields = ["mapNum", "scriptOnEnter", "scriptOnTeleport", "eventObjectIndex"];
        for (int i = 0; i < fields.Length; i++)
            Assert.Equal(expected[i], record.GetProperty(fields[i]).GetInt32());
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
