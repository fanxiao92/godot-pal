using System.Buffers.Binary;
using System.Text.Json;
using Xunit;

namespace Pal.Extract.Tests;

// CLI-level proof of the event-object table registration (#79): a miniature SSS.MKF
// whose chunk 0 is hand-authored EVENTOBJECT records, plus a minimal empty DATA.MKF,
// must decode through `export-data` with the frozen field values and index manifest.
public sealed class EventObjectExportDataTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"pal-export-data-event-object-{Guid.NewGuid():N}");
    private readonly string input;

    public EventObjectExportDataTests()
    {
        input = Path.Combine(root, "input");
        Directory.CreateDirectory(input);
    }

    // Hand-authored little-endian records (same literals as the unit tests): boundaries
    // 0, 65535, 0x8000/-32768, 32767, -1, and the script.c -15 vanish time. Chunk 1 is a
    // minimal all-zero SCENE record so the fixture stays valid now that #80 registers it.
    private static byte[] Record1 => Words(-32768, 0x1234, 0, -1, 0x8000, 65535, unchecked((short)0x8000), 8, 0xABCD, 0, 3, 32767, 1, 65535, 2, 32768);
    private static byte[] Record2 => Words(-15, 1840, 1200, 2, 10000, 0, 32767, 5, 0x1234, 65535, 0, 0, 0, 0, 4660, 0);

    private static byte[] Words(params int[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), unchecked((ushort)values[i]));
        return bytes;
    }

    [Fact]
    public async Task Export_data_extracts_event_object_records_and_the_index_entry()
    {
        MkfFixture.Write(input, "SSS.MKF", [.. Record1, .. Record2], new byte[8]);
        MkfFixture.Write(input, "DATA.MKF"); // minimal legal empty archive (offset table only)
        string output = Path.Combine(root, "data");
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.True(result.ExitCode == 0, result.Stderr);
        Assert.Equal(new[] { Path.Combine(output, "event-object.json"), Path.Combine(output, "index.json"), Path.Combine(output, "scene.json") },
            Directory.GetFiles(output).Order());

        using var table = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "event-object.json")));
        Assert.Equal(1, table.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal("faithful", table.RootElement.GetProperty("profile").GetString());
        Assert.Equal("event-object", table.RootElement.GetProperty("fileId").GetString());
        var records = table.RootElement.GetProperty("records");
        Assert.Equal(2, records.GetArrayLength());
        AssertAllFields(records[0], -32768, 0x1234, 0, -1, 32768, 65535, -32768, 8, 0xABCD, 0, 3, 32767, 1, 65535, 2, 32768);
        AssertAllFields(records[1], -15, 1840, 1200, 2, 10000, 0, 32767, 5, 0x1234, 65535, 0, 0, 0, 0, 4660, 0);

        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "index.json")));
        var tables = index.RootElement.GetProperty("tables").EnumerateArray().ToArray();
        Assert.Equal(2, tables.Length); // scene.json registered by #80; its own tests cover that entry
        Assert.Equal("scene.json", tables[1].GetProperty("file").GetString());
        var entry = tables.Single(table => table.GetProperty("file").GetString() == "event-object.json");
        Assert.Equal("SSS.MKF", entry.GetProperty("archive").GetString());
        Assert.Equal(0, entry.GetProperty("chunk").GetInt32());
        Assert.Equal(2, entry.GetProperty("recordCount").GetInt32());
        Assert.Equal(64, entry.GetProperty("byteLength").GetInt32());
        // Independent expectation: hand-verified SHA-256 of the 64 literal bytes above.
        Assert.Equal("e996031dfa6d0e88b4e101e694f461044650d243bbf3e48c73eec929ec54543f",
            entry.GetProperty("sha256").GetString());
    }

    [Fact]
    public async Task Export_data_fails_when_chunk_0_is_not_a_multiple_of_32_bytes()
    {
        MkfFixture.Write(input, "SSS.MKF", new byte[33], new byte[8]); // chunk 0 defect only
        MkfFixture.Write(input, "DATA.MKF");
        string output = Path.Combine(root, "data");
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("SSS.MKF", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("chunk 0", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("33", result.Stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(output)); // no partial output is written
    }

    private static void AssertAllFields(JsonElement record, params int[] expected)
    {
        string[] fields =
        [
            "vanishTime", "x", "y", "layer", "triggerScript", "autoScript", "state", "triggerMode",
            "spriteNum", "spriteFrames", "direction", "currentFrameNum", "scriptIdleFrame",
            "spritePtrOffset", "spriteFramesAuto", "scriptIdleFrameCountAuto",
        ];
        for (int i = 0; i < fields.Length; i++)
            Assert.Equal(expected[i], record.GetProperty(fields[i]).GetInt32());
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
