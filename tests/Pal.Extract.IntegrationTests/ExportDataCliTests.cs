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
        // Chunk contents are irrelevant for the index skeleton; arbitrary valid MKF payloads suffice.
        MkfFixture.Write(input, "SSS.MKF", new byte[64], new byte[16], new byte[28], Array.Empty<byte>(), new byte[24]);
        MkfFixture.Write(input, "DATA.MKF", new byte[18], new byte[70]);
    }

    [Fact]
    public async Task Export_data_writes_the_index_skeleton_with_source_hashes()
    {
        var result = await CliProcess.Run(root, "export-data", "--input", input, "--output", output);
        Assert.True(result.ExitCode == 0, result.Stderr);
        string[] files = [.. Directory.GetFiles(output).Order()];
        Assert.Equal(new[] { Path.Combine(output, "index.json") }, files);
        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "index.json")));
        var rootElement = index.RootElement;
        Assert.Equal(1, rootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal("faithful", rootElement.GetProperty("profile").GetString());
        Assert.Empty(rootElement.GetProperty("tables").EnumerateArray());
        var sources = rootElement.GetProperty("sources").EnumerateArray().ToArray();
        Assert.Equal(2, sources.Length);
        Assert.Equal("SSS.MKF", sources[0].GetProperty("archive").GetString());
        Assert.Equal("DATA.MKF", sources[1].GetProperty("archive").GetString());
        // Independent expectation: hash the fixture archives again in the test.
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(input, "SSS.MKF")))),
            sources[0].GetProperty("sha256").GetString());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(input, "DATA.MKF")))),
            sources[1].GetProperty("sha256").GetString());
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
