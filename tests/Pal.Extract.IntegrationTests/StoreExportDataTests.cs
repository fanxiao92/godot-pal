using System.Buffers.Binary;
using System.Text.Json;
using Xunit;

namespace Pal.Extract.Tests;

// CLI-level proof of the store table registration (#83): a miniature SSS.MKF whose
// three chunks are valid for the registered tables plus a hand-authored DATA.MKF
// chunk 0 must decode through `export-data` with the frozen item lists and index
// manifest. DATA chunk 0 is both the exported store table and the item-view evidence
// source, so the store literals here double as valid evidence: items 1..4 stay
// inside the 5-record object chunk and stay disjoint from the player evidence.
public sealed class StoreExportDataTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"pal-export-data-store-{Guid.NewGuid():N}");
    private readonly string input;

    public StoreExportDataTests()
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

    // Hand-authored little-endian STORE records: record 0 sells items 1 and 2 (seven
    // empty slots), record 1 sells items 3 and 4 with empty slots between them
    // (0 = empty slot, uigame.c:1644). Items 1..4 must exist as object records.
    private static byte[] StoreRecord1 => Words(1, 2, 0, 0, 0, 0, 0, 0, 0);
    private static byte[] StoreRecord2 => Words(3, 0, 0, 0, 0, 0, 0, 0, 4);

    private static byte[] Words(params int[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), unchecked((ushort)values[i]));
        return bytes;
    }

    [Fact]
    public async Task Export_data_extracts_store_records_and_the_index_entry()
    {
        // Chunk 2: five all-zero WIN95 records (70 = 5 x 14 bytes, not a multiple of
        // 12, so the variant stays decidable). Record 0 becomes the player view via
        // rgwName[0] = 0 in the all-zero roles; records 1..4 become the item view via
        // the store literals below.
        MkfFixture.Write(input, "SSS.MKF",
            [.. EventObjectRecord1, .. EventObjectRecord2], [.. SceneRecord1, .. SceneRecord2], new byte[70]);
        MkfFixture.Write(input, "DATA.MKF", [.. StoreRecord1, .. StoreRecord2], new byte[70], new byte[10], new byte[900]);
        string output = Path.Combine(root, "data");
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.True(result.ExitCode == 0, result.Stderr);
        Assert.Equal(new[] { Path.Combine(output, "enemy-team.json"), Path.Combine(output, "enemy.json"), Path.Combine(output, "event-object.json"), Path.Combine(output, "index.json"), Path.Combine(output, "object.json"), Path.Combine(output, "player-roles.json"), Path.Combine(output, "scene.json"), Path.Combine(output, "store.json") },
            Directory.GetFiles(output).Order());

        using var table = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "store.json")));
        Assert.Equal(1, table.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal("faithful", table.RootElement.GetProperty("profile").GetString());
        Assert.Equal("store", table.RootElement.GetProperty("fileId").GetString());
        var records = table.RootElement.GetProperty("records");
        Assert.Equal(2, records.GetArrayLength());
        AssertItems(records[0], 1, 2, 0, 0, 0, 0, 0, 0, 0);
        AssertItems(records[1], 3, 0, 0, 0, 0, 0, 0, 0, 4);

        // The same DATA chunk 0 feeds the object item-view evidence: the two nonzero
        // stores declare objects 1..4 as items (record 0 stays the player view).
        using var objects = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "object.json")));
        var objectRecords = objects.RootElement.GetProperty("records");
        Assert.Equal(5, objectRecords.GetArrayLength());
        Assert.Equal("player", objectRecords[0].GetProperty("view").GetString());
        for (int objectIndex = 1; objectIndex <= 4; objectIndex++)
            Assert.Equal("item", objectRecords[objectIndex].GetProperty("view").GetString());

        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "index.json")));
        var tables = index.RootElement.GetProperty("tables").EnumerateArray().ToArray();
        Assert.Equal(7, tables.Length);
        Assert.Equal("enemy-team.json", tables[0].GetProperty("file").GetString()); // lexicographic file order
        Assert.Equal("enemy.json", tables[1].GetProperty("file").GetString());
        var entry = tables.Single(table => table.GetProperty("file").GetString() == "store.json");
        Assert.Equal("DATA.MKF", entry.GetProperty("archive").GetString());
        Assert.Equal(0, entry.GetProperty("chunk").GetInt32());
        Assert.Equal(2, entry.GetProperty("recordCount").GetInt32());
        Assert.Equal(36, entry.GetProperty("byteLength").GetInt32());
        // Independent expectation: hand-verified SHA-256 of the 36 literal bytes above.
        Assert.Equal("fe33f46617121ab670252b6914004ca49e0af51204153f914f773d834d5895a3",
            entry.GetProperty("sha256").GetString());
    }

    [Fact]
    public async Task Export_data_fails_when_data_chunk_0_is_not_a_multiple_of_18_bytes()
    {
        MkfFixture.Write(input, "SSS.MKF",
            [.. EventObjectRecord1, .. EventObjectRecord2], [.. SceneRecord1, .. SceneRecord2], new byte[70]);
        MkfFixture.Write(input, "DATA.MKF", new byte[19], new byte[70], new byte[10], new byte[900]); // chunk 0 defect only
        string output = Path.Combine(root, "data");
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("DATA.MKF", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("chunk 0", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("19", result.Stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(output)); // no partial output is written
    }

    private static void AssertItems(JsonElement record, params int[] expected)
    {
        var items = record.GetProperty("items");
        Assert.Equal(expected.Length, items.GetArrayLength());
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], items[i].GetInt32());
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
