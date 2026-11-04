using System.Buffers.Binary;
using System.Text.Json;
using Xunit;

namespace Pal.Extract.Tests;

// CLI-level proof of the object union table registration (#81): a miniature SSS.MKF
// whose chunk 2 is hand-authored OBJECT records plus the DATA.MKF view-evidence
// chunks (store/enemy/player-roles) must decode through `export-data` with one
// declared view per record and the frozen index manifest.
public sealed class ObjectExportDataTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"pal-export-data-object-{Guid.NewGuid():N}");
    private readonly string input;

    public ObjectExportDataTests()
    {
        input = Path.Combine(root, "input");
        Directory.CreateDirectory(input);
    }

    // Hand-authored little-endian EVENTOBJECT/SCENE records (same literals as the
    // #79/#80 tests): chunks 0 and 1 must stay valid now that export-data registers
    // more than the flat tables.
    private static byte[] EventObjectRecord1 => Words(-32768, 0x1234, 0, -1, 0x8000, 65535, unchecked((short)0x8000), 8, 0xABCD, 0, 3, 32767, 1, 65535, 2, 32768);
    private static byte[] EventObjectRecord2 => Words(-15, 1840, 1200, 2, 10000, 0, 32767, 5, 0x1234, 65535, 0, 0, 0, 0, 4660, 0);
    private static byte[] SceneRecord1 => Words(65535, 0x1234, 0, 65535);
    private static byte[] SceneRecord2 => Words(1, 32768, 0xABCD, 5077);

    // Hand-authored WIN95 OBJECT records (same literals as the unit tests): a player
    // record (evidence: rgwName[0] = 0), an item record with flags 0 (store sells item
    // 1), and an item record with flags 65535 (store sells item 2).
    private static byte[] PlayerRecord => Words(0, 0, 43445, 43374, 0x1234, 65535, 32768);
    private static byte[] ItemRecordFlagsZero => Words(197, 150, 43022, 39140, 0, 40661, 0);
    private static byte[] ItemRecordFlagsMax => Words(0xABCD, 2000, 39554, 0, 42890, 40524, 65535);

    private static byte[] Words(params int[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), unchecked((ushort)values[i]));
        return bytes;
    }

    // The DATA.MKF view-evidence fixture: one store selling items 1 and 2, one empty
    // enemy, a placeholder chunk 2, and PLAYERROLES whose rgwName[0] = 0 (a name word
    // of 0 is still player evidence; fight.c reads rgObject[rgwName[w]].player).
    private static void WriteDataFixture(string input) =>
        WriteDataFixture(input, Words(1, 2, 0, 0, 0, 0, 0, 0, 0));

    private static void WriteDataFixture(string input, byte[] store)
    {
        byte[] enemy = new byte[70];
        byte[] placeholder = new byte[10];
        byte[] roles = new byte[900];
        MkfFixture.Write(input, "DATA.MKF", store, enemy, placeholder, roles);
    }

    [Fact]
    public async Task Export_data_extracts_object_records_with_views_and_the_index_entry()
    {
        MkfFixture.Write(input, "SSS.MKF",
            [.. EventObjectRecord1, .. EventObjectRecord2], [.. SceneRecord1, .. SceneRecord2],
            [.. PlayerRecord, .. ItemRecordFlagsZero, .. ItemRecordFlagsMax], Array.Empty<byte>(), new byte[24]);
        WriteDataFixture(input);
        string output = Path.Combine(root, "data");
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.True(result.ExitCode == 0, result.Stderr);
        Assert.Equal(new[] { Path.Combine(output, "event-object.json"), Path.Combine(output, "index.json"), Path.Combine(output, "object.json"), Path.Combine(output, "scene.json") },
            Directory.GetFiles(output).Order());

        using var table = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "object.json")));
        Assert.Equal(1, table.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal("faithful", table.RootElement.GetProperty("profile").GetString());
        Assert.Equal("object", table.RootElement.GetProperty("fileId").GetString());
        var records = table.RootElement.GetProperty("records");
        Assert.Equal(3, records.GetArrayLength());

        var player = records[0];
        Assert.Equal("player", player.GetProperty("view").GetString());
        Assert.Equal(43445, player.GetProperty("scriptOnFriendDeath").GetInt32());
        Assert.Equal(43374, player.GetProperty("scriptOnDying").GetInt32());

        var zero = records[1];
        Assert.Equal("item", zero.GetProperty("view").GetString());
        Assert.Equal(197, zero.GetProperty("bitmap").GetInt32());
        Assert.Equal(150, zero.GetProperty("price").GetInt32());
        Assert.Equal(40661, zero.GetProperty("scriptDesc").GetInt32());
        Assert.Equal(0, zero.GetProperty("flags").GetInt32());
        foreach (string playerField in new[] { "reserved", "scriptOnDying", "padding" })
            Assert.False(zero.TryGetProperty(playerField, out _)); // one view per record

        var max = records[2];
        Assert.Equal("item", max.GetProperty("view").GetString());
        Assert.Equal(65535, max.GetProperty("flags").GetInt32());
        Assert.Equal(42890, max.GetProperty("scriptOnThrow").GetInt32());

        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "index.json")));
        var tables = index.RootElement.GetProperty("tables").EnumerateArray().ToArray();
        Assert.Equal(3, tables.Length);
        Assert.Equal("event-object.json", tables[0].GetProperty("file").GetString()); // lexicographic file order
        var entry = tables.Single(table => table.GetProperty("file").GetString() == "object.json");
        Assert.Equal("SSS.MKF", entry.GetProperty("archive").GetString());
        Assert.Equal(2, entry.GetProperty("chunk").GetInt32());
        Assert.Equal(3, entry.GetProperty("recordCount").GetInt32());
        Assert.Equal(42, entry.GetProperty("byteLength").GetInt32());
        // Independent expectation: hand-verified SHA-256 of the 42 literal bytes above.
        Assert.Equal("a709ee29c2bee27c6d72f651d275bb65816f6c4aa714cf368c27eb4a6b6442f7",
            entry.GetProperty("sha256").GetString());
    }

    [Fact]
    public async Task Export_data_extracts_dos_object_records_without_the_script_desc_slot()
    {
        byte[] dosPlayer = Words(0, 0, 5, 6, 0x1234, 0xABCD);
        byte[] dosUndetermined = Words(9, 8, 7, 6, 5, 4);
        MkfFixture.Write(input, "SSS.MKF",
            [.. EventObjectRecord1, .. EventObjectRecord2], [.. SceneRecord1, .. SceneRecord2],
            [.. dosPlayer, .. dosUndetermined], Array.Empty<byte>(), new byte[24]);
        WriteDataFixture(input, new byte[18]); // empty store: record 1 stays undetermined
        string output = Path.Combine(root, "data");
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.True(result.ExitCode == 0, result.Stderr);

        using var table = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "object.json")));
        var records = table.RootElement.GetProperty("records");
        Assert.Equal(2, records.GetArrayLength());
        Assert.Equal("player", records[0].GetProperty("view").GetString());
        Assert.Equal(6, records[0].GetProperty("scriptOnDying").GetInt32());
        Assert.Equal(2, records[0].GetProperty("padding").GetArrayLength()); // DOS: 2 padding words
        Assert.Equal("undetermined", records[1].GetProperty("view").GetString());
        Assert.Equal(6, records[1].GetProperty("data").GetArrayLength());

        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "index.json")));
        var entry = index.RootElement.GetProperty("tables").EnumerateArray()
            .Single(table => table.GetProperty("file").GetString() == "object.json");
        Assert.Equal(2, entry.GetProperty("recordCount").GetInt32());
        Assert.Equal(24, entry.GetProperty("byteLength").GetInt32());
    }

    [Fact]
    public async Task Export_data_fails_when_chunk_2_is_not_a_multiple_of_14_or_12_bytes()
    {
        MkfFixture.Write(input, "SSS.MKF",
            [.. EventObjectRecord1, .. EventObjectRecord2], [.. SceneRecord1, .. SceneRecord2],
            new byte[13], Array.Empty<byte>(), new byte[24]);
        WriteDataFixture(input);
        string output = Path.Combine(root, "data");
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("SSS.MKF", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("chunk 2", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("13", result.Stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(output)); // no partial output is written
    }

    [Fact]
    public async Task Export_data_fails_when_a_data_evidence_chunk_is_missing()
    {
        MkfFixture.Write(input, "SSS.MKF",
            [.. EventObjectRecord1, .. EventObjectRecord2], [.. SceneRecord1, .. SceneRecord2],
            new byte[28], Array.Empty<byte>(), new byte[24]);
        MkfFixture.Write(input, "DATA.MKF", Words(1, 2, 0, 0, 0, 0, 0, 0, 0), new byte[70]); // no chunk 3
        string output = Path.Combine(root, "data");
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("DATA.MKF", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("chunk 3", result.Stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(output));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
