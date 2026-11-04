using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Pal.Extract.Tests;

public sealed class ExportDataCliTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"pal-export-data-{Guid.NewGuid():N}");
    private readonly string input;
    private readonly string output;

    public ExportDataCliTests()
    {
        input = Path.Combine(root, "input");
        output = Path.Combine(root, "data");
        Directory.CreateDirectory(input);
        // Chunk 0 must decode as the registered event-object table (64 = 2 x 32 bytes),
        // chunk 1 as the registered scene table (16 = 2 x 8 bytes), and chunk 2 as the
        // registered object union table (28 = 2 x 14 bytes, WIN95 variant; the all-zero
        // DATA fixture makes record 0 the player view via rgwName[0] = 0 and record 1
        // undetermined). DATA chunk 0 is registered as the store table (18 = 1 x 18
        // bytes), DATA chunk 1 as the enemy table (70 = 1 x 70 bytes), DATA chunk 2
        // as the enemy-team table (10 = 1 x 10 bytes), DATA chunk 3 as the
        // player-roles table (900 = 1 x 900 bytes, the #86 attribute group and the
        // #87 equipment/magic/sound/cover group both structured), DATA chunk 4 as
        // the magic table (32 = 1 x 32 bytes; its zero type exports the layerOffset
        // view), and DATA chunk 5 as the battle-field table (12 = 1 x 12 bytes; its
        // zero magicEffect exports five signed zeros); the DATA chunks also feed
        // the object view evidence besides; the other chunks are placeholders until
        // their tables are registered by #90-#91.
        MkfFixture.Write(input, "SSS.MKF", new byte[64], new byte[16], new byte[28], Array.Empty<byte>(), new byte[24]);
        MkfFixture.Write(input, "DATA.MKF", new byte[18], new byte[70], new byte[10], new byte[900], new byte[32], new byte[12]);
    }

    [Fact]
    public async Task Export_data_writes_the_index_manifest_with_the_registered_tables()
    {
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.True(result.ExitCode == 0, result.Stderr);
        string[] files = [.. Directory.GetFiles(output).Order()];
        Assert.Equal(new[] { Path.Combine(output, "battle-field.json"), Path.Combine(output, "enemy-team.json"), Path.Combine(output, "enemy.json"), Path.Combine(output, "event-object.json"), Path.Combine(output, "index.json"), Path.Combine(output, "magic.json"), Path.Combine(output, "object.json"), Path.Combine(output, "player-roles.json"), Path.Combine(output, "scene.json"), Path.Combine(output, "store.json") }, files);
        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "index.json")));
        var rootElement = index.RootElement;
        Assert.Equal(1, rootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal("faithful", rootElement.GetProperty("profile").GetString());
        var sources = rootElement.GetProperty("sources").EnumerateArray().ToArray();
        Assert.Equal(2, sources.Length);
        Assert.Equal("SSS.MKF", sources[0].GetProperty("archive").GetString());
        Assert.Equal("DATA.MKF", sources[1].GetProperty("archive").GetString());
        // Independent expectation: hash the fixture archives again in the test.
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(input, "SSS.MKF")))),
            sources[0].GetProperty("sha256").GetString());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(input, "DATA.MKF")))),
            sources[1].GetProperty("sha256").GetString());
        var tables = rootElement.GetProperty("tables").EnumerateArray().ToArray();
        Assert.Equal(9, tables.Length);
        Assert.Equal("battle-field.json", tables[0].GetProperty("file").GetString()); // lexicographic file order
        Assert.Equal("DATA.MKF", tables[0].GetProperty("archive").GetString());
        Assert.Equal(5, tables[0].GetProperty("chunk").GetInt32());
        Assert.Equal(1, tables[0].GetProperty("recordCount").GetInt32()); // 12 zero bytes = 1 empty record
        Assert.Equal(12, tables[0].GetProperty("byteLength").GetInt32());
        // Independent expectation: the fixture wrote all-zero chunk 5 bytes.
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(new byte[12])), tables[0].GetProperty("sha256").GetString());
        Assert.Equal("enemy-team.json", tables[1].GetProperty("file").GetString()); // lexicographic file order
        Assert.Equal("DATA.MKF", tables[1].GetProperty("archive").GetString());
        Assert.Equal(2, tables[1].GetProperty("chunk").GetInt32());
        Assert.Equal(1, tables[1].GetProperty("recordCount").GetInt32()); // 10 zero bytes = 1 empty team
        Assert.Equal(10, tables[1].GetProperty("byteLength").GetInt32());
        // Independent expectation: the fixture wrote all-zero chunk 2 bytes.
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(new byte[10])), tables[1].GetProperty("sha256").GetString());
        Assert.Equal("enemy.json", tables[2].GetProperty("file").GetString()); // lexicographic file order
        Assert.Equal("DATA.MKF", tables[2].GetProperty("archive").GetString());
        Assert.Equal(1, tables[2].GetProperty("chunk").GetInt32());
        Assert.Equal(1, tables[2].GetProperty("recordCount").GetInt32()); // 70 zero bytes = 1 empty record
        Assert.Equal(70, tables[2].GetProperty("byteLength").GetInt32());
        // Independent expectation: the fixture wrote all-zero chunk 1 bytes.
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(new byte[70])), tables[2].GetProperty("sha256").GetString());
        Assert.Equal("event-object.json", tables[3].GetProperty("file").GetString());
        Assert.Equal("SSS.MKF", tables[3].GetProperty("archive").GetString());
        Assert.Equal(0, tables[3].GetProperty("chunk").GetInt32());
        Assert.Equal(2, tables[3].GetProperty("recordCount").GetInt32()); // 64 zero bytes = 2 empty records
        Assert.Equal(64, tables[3].GetProperty("byteLength").GetInt32());
        // Independent expectation: the fixture wrote all-zero chunk 0 bytes.
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(new byte[64])), tables[3].GetProperty("sha256").GetString());
        Assert.Equal("magic.json", tables[4].GetProperty("file").GetString()); // lexicographic file order
        Assert.Equal("DATA.MKF", tables[4].GetProperty("archive").GetString());
        Assert.Equal(4, tables[4].GetProperty("chunk").GetInt32());
        Assert.Equal(1, tables[4].GetProperty("recordCount").GetInt32()); // 32 zero bytes = 1 empty record (type 0 -> layerOffset view)
        Assert.Equal(32, tables[4].GetProperty("byteLength").GetInt32());
        // Independent expectation: the fixture wrote all-zero chunk 4 bytes.
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(new byte[32])), tables[4].GetProperty("sha256").GetString());
        Assert.Equal("object.json", tables[5].GetProperty("file").GetString()); // lexicographic file order
        Assert.Equal("SSS.MKF", tables[5].GetProperty("archive").GetString());
        Assert.Equal(2, tables[5].GetProperty("chunk").GetInt32());
        Assert.Equal(2, tables[5].GetProperty("recordCount").GetInt32()); // 28 zero bytes = 2 empty WIN95 records
        Assert.Equal(28, tables[5].GetProperty("byteLength").GetInt32());
        // Independent expectation: the fixture wrote all-zero chunk 2 bytes.
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(new byte[28])), tables[5].GetProperty("sha256").GetString());
        Assert.Equal("player-roles.json", tables[6].GetProperty("file").GetString()); // lexicographic file order
        Assert.Equal("DATA.MKF", tables[6].GetProperty("archive").GetString());
        Assert.Equal(3, tables[6].GetProperty("chunk").GetInt32());
        Assert.Equal(1, tables[6].GetProperty("recordCount").GetInt32()); // 900 zero bytes = 1 empty record
        Assert.Equal(900, tables[6].GetProperty("byteLength").GetInt32());
        // Independent expectation: the fixture wrote all-zero chunk 3 bytes.
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(new byte[900])), tables[6].GetProperty("sha256").GetString());
        Assert.Equal("scene.json", tables[7].GetProperty("file").GetString());
        Assert.Equal("SSS.MKF", tables[7].GetProperty("archive").GetString());
        Assert.Equal(1, tables[7].GetProperty("chunk").GetInt32());
        Assert.Equal(2, tables[7].GetProperty("recordCount").GetInt32()); // 16 zero bytes = 2 empty records
        Assert.Equal(16, tables[7].GetProperty("byteLength").GetInt32());
        // Independent expectation: the fixture wrote all-zero chunk 1 bytes.
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(new byte[16])), tables[7].GetProperty("sha256").GetString());
        Assert.Equal("store.json", tables[8].GetProperty("file").GetString()); // lexicographic file order
        Assert.Equal("DATA.MKF", tables[8].GetProperty("archive").GetString());
        Assert.Equal(0, tables[8].GetProperty("chunk").GetInt32());
        Assert.Equal(1, tables[8].GetProperty("recordCount").GetInt32()); // 18 zero bytes = 1 empty store
        Assert.Equal(18, tables[8].GetProperty("byteLength").GetInt32());
        // Independent expectation: the fixture wrote all-zero chunk 0 bytes.
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(new byte[18])), tables[8].GetProperty("sha256").GetString());

        using var objectTable = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "object.json")));
        var objectRecords = objectTable.RootElement.GetProperty("records");
        Assert.Equal(2, objectRecords.GetArrayLength());
        Assert.Equal("player", objectRecords[0].GetProperty("view").GetString()); // rgwName[0] = 0 in the all-zero roles
        Assert.Equal("undetermined", objectRecords[1].GetProperty("view").GetString());
    }

    [Theory]
    [InlineData("SSS.MKF", "DATA.MKF: required archive is missing.")]
    [InlineData("DATA.MKF", "SSS.MKF: required archive is missing.")]
    public async Task Export_data_fails_clearly_when_a_required_archive_is_missing(string present, string diagnostic)
    {
        foreach (string name in new[] { "SSS.MKF", "DATA.MKF" })
            if (name != present) File.Delete(Path.Combine(input, name));
        string destination = Path.Combine(root, "missing-archive-output");
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", destination);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains(diagnostic, result.Stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public async Task Export_data_rejects_a_non_empty_output_directory()
    {
        string destination = Path.Combine(root, "occupied");
        Directory.CreateDirectory(destination);
        string preexisting = Path.Combine(destination, "keep.txt");
        File.WriteAllText(preexisting, "keep");
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", destination);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("not empty", result.Stderr, StringComparison.Ordinal);
        Assert.Equal("keep", File.ReadAllText(preexisting));
        Assert.Equal(new[] { preexisting }, Directory.GetFiles(destination));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
