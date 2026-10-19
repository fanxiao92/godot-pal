using System.Text.Json;
using Xunit;

namespace Pal.Extract.Tests;

public class InspectCliTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"pal-inspect-test-{Guid.NewGuid():N}");
    private readonly string input;

    public InspectCliTests()
    {
        input = Path.Combine(directory, "input 数据");
        Directory.CreateDirectory(input);
        foreach (var name in new[] { "Data.mkf", "Gop.mkf", "Pat.mkf" })
        {
            File.WriteAllBytes(Path.Combine(input, name), [8, 0, 0, 0, 12, 0, 0, 0, 1, 2, 3, 4]);
        }
        foreach (var name in new[] { "Map.mkf", "Mgo.mkf" })
        {
            // Three chunks: empty, eight bytes with a declared Windows output length, empty.
            File.WriteAllBytes(Path.Combine(input, name),
                [16, 0, 0, 0, 16, 0, 0, 0, 24, 0, 0, 0, 24, 0, 0, 0, 0, 0, 1, 0, 17, 34, 51, 68]);
        }
        // Five chunks: two empty, one 14-byte object record, empty, one nonzero script record.
        File.WriteAllBytes(Path.Combine(input, "Sss.mkf"),
            [24, 0, 0, 0, 24, 0, 0, 0, 24, 0, 0, 0, 38, 0, 0, 0, 38, 0, 0, 0, 46, 0, 0, 0,
             1, 0, 2, 0, 3, 0, 4, 0, 5, 0, 6, 0, 7, 0, 255, 255, 1, 0, 0, 0, 0, 0]);
    }

    [Fact]
    public async Task Inspect_reports_archive_identity_chunks_and_format_hints_without_text_or_events()
    {
        var result = await Inspect();

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Stderr);
        using var json = JsonDocument.Parse(result.Stdout);
        Assert.Equal(1, json.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal("windows", json.RootElement.GetProperty("versionHint").GetString());
        var archives = json.RootElement.GetProperty("archives").EnumerateArray().ToArray();
        Assert.Equal(new[] { "data.mkf", "gop.mkf", "map.mkf", "mgo.mkf", "pat.mkf", "sss.mkf" },
            archives.Select(a => a.GetProperty("name").GetString()));
        var map = archives[2];
        Assert.Equal(24, map.GetProperty("byteLength").GetInt64());
        Assert.Equal("081c8cb4aac77181b8547eda8c9bbb8a5a463e4430151c4f23b9d88505d94277",
            map.GetProperty("sha256").GetString());
        Assert.Equal(3, map.GetProperty("chunkCount").GetInt32());
        Assert.Equal(2, map.GetProperty("emptyChunkCount").GetInt32());
        var chunks = map.GetProperty("chunks").EnumerateArray().ToArray();
        Assert.Equal("empty", chunks[0].GetProperty("compressionHint").GetString());
        Assert.Equal(1, chunks[1].GetProperty("index").GetInt32());
        Assert.Equal(16, chunks[1].GetProperty("offset").GetInt64());
        Assert.Equal(8, chunks[1].GetProperty("byteLength").GetInt64());
        Assert.Equal("yj2-candidate", chunks[1].GetProperty("compressionHint").GetString());
        Assert.Equal(65536, chunks[1].GetProperty("declaredDecompressedByteLength").GetInt64());
        Assert.Equal("raw", archives[1].GetProperty("chunks")[0].GetProperty("compressionHint").GetString());
        Assert.Equal(JsonValueKind.Null,
            archives[1].GetProperty("chunks")[0].GetProperty("declaredDecompressedByteLength").ValueKind);
        Assert.DoesNotContain(directory, result.Stdout);
    }

    public static TheoryData<byte[], string> InvalidArchives => new()
    {
        { [], "header" },
        { [1, 2, 3], "header" },
        { [0, 0, 0, 0], "offset table" },
        { [9, 0, 0, 0, 12, 0, 0, 0, 1, 2, 3, 4], "aligned" },
        { [16, 0, 0, 0, 12, 0, 0, 0, 1, 2, 3, 4], "offset table" },
        { [12, 0, 0, 0, 8, 0, 0, 0, 16, 0, 0, 0, 1, 2, 3, 4], "chunk 0" },
        { [8, 0, 0, 0, 13, 0, 0, 0, 1, 2, 3, 4], "chunk 0" },
        { [8, 0, 0, 0, 255, 255, 255, 255, 1, 2, 3, 4], "chunk 0" },
        { [8, 0, 0, 0, 11, 0, 0, 0, 1, 2, 3, 4], "final offset" }
    };

    [Theory]
    [MemberData(nameof(InvalidArchives))]
    public async Task Inspect_rejects_malformed_archives_without_partial_reports(byte[] bytes, string error)
    {
        File.WriteAllBytes(Path.Combine(input, "Data.mkf"), bytes);

        var result = await Inspect();

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("data.mkf", result.Stderr);
        Assert.Contains(error, result.Stderr);
        Assert.DoesNotContain("Unhandled exception.", result.Stderr);
        Assert.DoesNotContain(Environment.NewLine + "   at ", result.Stderr);
    }

    [Theory]
    [InlineData("Data.mkf")]
    [InlineData("Gop.mkf")]
    [InlineData("Map.mkf")]
    [InlineData("Mgo.mkf")]
    [InlineData("Pat.mkf")]
    [InlineData("Sss.mkf")]
    public async Task Inspect_reports_missing_required_files_without_partial_reports(string name)
    {
        File.Delete(Path.Combine(input, name));

        var result = await Inspect();

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains(name.ToLowerInvariant(), result.Stderr);
        Assert.Contains("missing", result.Stderr);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Inspect_reports_unreadable_input_directories(bool useFile)
    {
        var path = useFile ? Path.Combine(input, "Map.mkf") : Path.Combine(directory, "missing");

        var result = await CliProcess.Run(directory, "inspect", "--input", path);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("Input directory", result.Stderr);
        Assert.DoesNotContain(directory, result.Stderr);
    }

    [Fact]
    public async Task Inspect_reports_locked_archives_without_an_exception_stack()
    {
        using var locked = File.Open(Path.Combine(input, "Data.mkf"), FileMode.Open, FileAccess.Read, FileShare.None);

        var result = await Inspect();

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("data.mkf", result.Stderr);
        Assert.Contains("unable to read", result.Stderr);
        Assert.DoesNotContain(directory, result.Stderr);
    }

    [Theory]
    [InlineData("inspect")]
    [InlineData("inspect", "--input")]
    [InlineData("inspect", "--input", "")]
    [InlineData("inspect", "--input", " ")]
    [InlineData("inspect", "--input", "--help")]
    [InlineData("inspect", "--unknown", "input")]
    [InlineData("inspect", "--input", "input", "extra")]
    [InlineData("--help", "inspect")]
    public async Task Inspect_requires_exact_arguments_and_a_directory_value(params string[] arguments)
    {
        var result = await CliProcess.Run(directory, arguments);

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("Use --help", result.Stderr);
    }

    [Fact]
    public async Task Inspect_is_repeatable_case_insensitive_and_leaves_read_only_inputs_unchanged()
    {
        File.Move(Path.Combine(input, "Data.mkf"), Path.Combine(input, "DATA.MKF"));
        File.WriteAllText(Path.Combine(input, "unrelated.mkf"), "not an archive");
        var before = Directory.GetFiles(input).ToDictionary(path => path,
            path => (Bytes: File.ReadAllBytes(path), Modified: File.GetLastWriteTimeUtc(path)));
        foreach (var path in before.Keys)
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        }
        try
        {
            var first = await Inspect();
            var second = await Inspect();

            Assert.Equal(0, first.ExitCode);
            Assert.Equal(first, second);
            Assert.Equal(before.Keys.Order(), Directory.GetFiles(input).Order());
            Assert.Single(Directory.GetDirectories(directory));
            Assert.Empty(Directory.GetFiles(directory));
            foreach (var (path, snapshot) in before)
            {
                Assert.Equal(snapshot.Bytes, File.ReadAllBytes(path));
                Assert.Equal(snapshot.Modified, File.GetLastWriteTimeUtc(path));
                Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly));
            }
        }
        finally
        {
            foreach (var path in before.Keys)
            {
                File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
            }
        }
    }

    [Fact]
    public async Task Inspect_accepts_an_archive_with_zero_chunks_and_reports_unknown_version_without_probes()
    {
        foreach (var path in Directory.GetFiles(input))
        {
            File.WriteAllBytes(path, [4, 0, 0, 0]);
        }

        var result = await Inspect();

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Stderr);
        using var json = JsonDocument.Parse(result.Stdout);
        Assert.Equal("unknown", json.RootElement.GetProperty("versionHint").GetString());
        foreach (var archive in json.RootElement.GetProperty("archives").EnumerateArray())
        {
            Assert.Equal(0, archive.GetProperty("chunkCount").GetInt32());
            Assert.Equal(0, archive.GetProperty("emptyChunkCount").GetInt32());
            Assert.Empty(archive.GetProperty("chunks").EnumerateArray());
        }
    }

    [Theory]
    [InlineData(12, true, true, "dos")]
    [InlineData(14, true, true, "inconsistent")]
    [InlineData(12, false, false, "inconsistent")]
    [InlineData(14, true, false, "inconsistent")]
    [InlineData(84, false, false, "unknown")]
    [InlineData(84, true, true, "dos")]
    [InlineData(0, false, false, "unknown")]
    public async Task Inspect_distinguishes_format_hints_from_verified_decompression(
        int objectBytes, bool dosMap, bool dosMgo, string expectedHint)
    {
        WriteArchive("Sss.mkf", [], [], new byte[objectBytes]);
        WriteArchive("Map.mkf", dosMap ? "YJ_1\u0000\u0000\u0001\u0000"u8.ToArray() : [0, 0, 1, 0]);
        WriteArchive("Mgo.mkf", dosMgo ? "YJ_1\u0000\u0000\u0001\u0000"u8.ToArray() : [0, 0, 1, 0]);

        var result = await Inspect();

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Stderr);
        using var json = JsonDocument.Parse(result.Stdout);
        Assert.Equal(expectedHint, json.RootElement.GetProperty("versionHint").GetString());
        var map = json.RootElement.GetProperty("archives")[2].GetProperty("chunks")[0];
        Assert.Equal(dosMap ? "yj1" : "unknown", map.GetProperty("compressionHint").GetString());
        if (dosMap)
        {
            Assert.Equal(65536, map.GetProperty("declaredDecompressedByteLength").GetInt64());
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, map.GetProperty("declaredDecompressedByteLength").ValueKind);
        }
    }

    [Fact]
    public async Task Inspect_does_not_treat_raw_archive_signatures_or_short_chunks_as_compressed_data()
    {
        WriteArchive("Gop.mkf", "YJ_1"u8.ToArray());
        WriteArchive("Map.mkf", [1, 2, 3]);

        var result = await Inspect();

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Stdout);
        Assert.Equal("unknown", json.RootElement.GetProperty("versionHint").GetString());
        Assert.Equal("raw", json.RootElement.GetProperty("archives")[1].GetProperty("chunks")[0]
            .GetProperty("compressionHint").GetString());
        Assert.Equal("unknown", json.RootElement.GetProperty("archives")[2].GetProperty("chunks")[0]
            .GetProperty("compressionHint").GetString());
    }

    [CaseSensitiveFileSystemFact]
    public async Task Inspect_rejects_ambiguous_case_insensitive_names()
    {
        File.Copy(Path.Combine(input, "Data.mkf"), Path.Combine(input, "data.mkf"));

        var result = await Inspect();

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("data.mkf", result.Stderr);
        Assert.Contains("ambiguous", result.Stderr);
    }

    private void WriteArchive(string name, params byte[][] chunks)
    {
        using var stream = File.Create(Path.Combine(input, name));
        using var writer = new BinaryWriter(stream);
        var offset = (chunks.Length + 1) * 4;
        writer.Write(offset);
        foreach (var chunk in chunks)
        {
            offset += chunk.Length;
            writer.Write(offset);
        }
        foreach (var chunk in chunks)
        {
            writer.Write(chunk);
        }
    }

    private Task<(int ExitCode, string Stdout, string Stderr)> Inspect() =>
        CliProcess.Run(directory, "inspect", "--input", input);

    public void Dispose() => Directory.Delete(directory, recursive: true);
}

public sealed class CaseSensitiveFileSystemFactAttribute : FactAttribute
{
    public CaseSensitiveFileSystemFactAttribute()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"pal-case-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "a"), "first");
            File.WriteAllText(Path.Combine(directory, "A"), "second");
            if (Directory.GetFiles(directory).Length != 2)
            {
                Skip = "Case-colliding files cannot coexist on this filesystem.";
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
