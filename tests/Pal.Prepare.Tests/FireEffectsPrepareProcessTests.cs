using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using Xunit;

namespace Pal.Prepare.Tests;

// Process contracts only (#147): argv/marker/exit/rejection through the real Godot
// editor scene and wrapper. The literal native roundtrip and checked-load contracts
// live in the Pal.Game GdUnit suite; nothing here derives expectations from output.
public sealed class FireEffectsPrepareProcessTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "pal-fire-process-" + Guid.NewGuid().ToString("N"));
    private readonly string repo;
    private readonly string output;
    private string Input => Path.Combine(root, "fire");
    private string Project => Path.Combine(repo, "src", "Pal.Game");
    private static string Godot => Environment.GetEnvironmentVariable("PAL_TEST_GODOT") ?? throw new InvalidOperationException("Set PAL_TEST_GODOT.");
    private const string Marker = "PAL_PREPARE_FIRE_EFFECTS:";

    // The artificial fire package mirrors T18's literal fixture shapes: chunk 3 is a
    // two-nominal-frame YJ2 sprite whose final table word decodes as an extra alias
    // frame, chunk 7 is a single-frame sprite, chunk 9 stays empty. Chunk ids are
    // deliberately sparse: FIRE chunks are magic-effect ids, never renumbered items.
    private const string EmptySha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    public FireEffectsPrepareProcessTests()
    {
        repo = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(repo, "GodotPal.sln"))) repo = Directory.GetParent(repo)!.FullName;
        output = Path.Combine(Project, "Assets", "PrepareTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Input);
    }

    [Fact]
    public async Task Editor_publishes_native_fire_sprites_and_mapping_without_overwrite()
    {
        WritePackage();
        string[] before = Snapshot(Input);
        var result = await Editor("--fire-input", Input, "--fire-output", output);
        Assert.True(result.ExitCode == 0, result.Text);
        Assert.Contains(Marker + "2", result.Text);
        Assert.Equal(new[] { "Sprites", "Textures" }, TopLevel(output));
        Assert.Equal(new[]
        {
            "Sprites/3.tres", "Sprites/7.tres",
            "Textures/3/0.tres", "Textures/3/1.tres", "Textures/3/2.tres", "Textures/7/0.tres",
        }, Files(output));
        Assert.Equal(before, Snapshot(Input));
        string[] published = Snapshot(output);
        var collision = await Editor("--fire-input", Input, "--fire-output", output);
        Assert.Equal(1, collision.ExitCode);
        Assert.Contains("new or empty", collision.Text);
        Assert.DoesNotContain(Marker, collision.Text);
        Assert.Equal(published, Snapshot(output));
        Assert.Equal(before, Snapshot(Input));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("reordered")]
    [InlineData("unknown")]
    public async Task Editor_rejects_invalid_argv_with_exit_one_and_no_success_marker(string kind)
    {
        string[] args = kind switch
        {
            "missing" => ["--fire-input", Input],
            "extra" => ["--fire-input", Input, "--fire-output", output, "--extra", "value"],
            "reordered" => ["--fire-output", output, "--fire-input", Input],
            _ => ["--unknown", Input, "--fire-output", output]
        };
        var result = await Editor(args);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Fire effects native import failed: invalid fire-effects prepare arguments", result.Text);
        Assert.DoesNotContain(Marker, result.Text);
        Assert.False(Directory.Exists(output));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("timeout")]
    public async Task Wrapper_rejects_invalid_arguments_with_exit_two_before_tool_resolution(string kind)
    {
        string[] args = kind switch
        {
            "missing" => ["-InputPath", Input],
            "duplicate" => ["-InputPath", Input, "-InputPath", Input, "-OutputPath", output],
            "unknown" => ["-InputPath", Input, "-OutputPath", output, "-Unknown", "value"],
            _ => ["-InputPath", Input, "-OutputPath", output, "-TimeoutSeconds", "601"]
        };
        var result = await Wrapper(args);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Prepare-FireEffects argument error:", result.Text);
        Assert.DoesNotContain(Marker, result.Text);
        Assert.False(Directory.Exists(output));
    }

    [Theory]
    [InlineData("missing-input")]
    [InlineData("nonempty-output")]
    [InlineData("outside-assets")]
    public async Task Wrapper_preflights_paths_before_starting_unavailable_tool_and_preserves_input(string kind)
    {
        WritePackage();
        string selectedOutput = output;
        switch (kind)
        {
            case "nonempty-output": Directory.CreateDirectory(Path.Combine(output, "keep")); break;
            case "outside-assets": selectedOutput = Path.Combine(root, "outside"); break;
        }
        string[] before = Snapshot(Input);
        if (kind == "missing-input") Directory.Delete(Input, true);
        string unavailable = Path.Combine(root, "must-not-start.exe");
        var result = await Wrapper("-InputPath", Input, "-OutputPath", selectedOutput, "-GodotPath", unavailable);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Prepare-FireEffects failed:", result.Text);
        Assert.DoesNotContain(unavailable, result.Text);
        Assert.DoesNotContain(Marker, result.Text);
        if (kind == "missing-input") Assert.False(Directory.Exists(Input));
        else Assert.Equal(before, Snapshot(Input));
        if (kind == "nonempty-output") Assert.True(Directory.Exists(Path.Combine(output, "keep")));
        else Assert.False(Directory.Exists(selectedOutput));
    }

    [Fact]
    public async Task Normal_wrapper_publishes_native_fire_sprites_and_never_overwrites()
    {
        WritePackage();
        string[] before = Snapshot(Input);
        var prepared = await Wrapper("-InputPath", Input, "-OutputPath", output, "-GodotPath", Godot);
        Assert.True(prepared.ExitCode == 0, prepared.Text);
        Assert.Contains(Marker + "2", prepared.Text);
        Assert.Contains("Prepared native Godot fire effects sprites.", prepared.Text);
        Assert.Equal(new[] { "Sprites", "Textures" }, TopLevel(output));
        string[] published = Snapshot(output);
        Assert.Equal(before, Snapshot(Input));
        var collision = await Wrapper("-InputPath", Input, "-OutputPath", output, "-GodotPath", Godot);
        Assert.Equal(1, collision.ExitCode);
        Assert.Contains("new or empty", collision.Text);
        Assert.DoesNotContain(Marker, collision.Text);
        Assert.Equal(published, Snapshot(output));
        Assert.Equal(before, Snapshot(Input));
    }

    private void WritePackage()
    {
        Directory.CreateDirectory(Path.Combine(Input, "sprites", "3"));
        Directory.CreateDirectory(Path.Combine(Input, "sprites", "7"));
        File.WriteAllBytes(Path.Combine(Input, "sprites", "3", "0.png"), Png(4, 1, (x, y) => (160, 80, 40, 255)));
        File.WriteAllBytes(Path.Combine(Input, "sprites", "3", "1.png"), Png(1, 1, (x, y) => (161, 80, 41, 255)));
        File.WriteAllBytes(Path.Combine(Input, "sprites", "3", "2.png"), Png(1, 1, (x, y) => (162, 80, 42, 255)));
        File.WriteAllBytes(Path.Combine(Input, "sprites", "7", "0.png"), Png(2, 1, (x, y) => (163, 80, 43, 255)));
        File.WriteAllText(Path.Combine(Input, "sprites", "3.json"), JsonSerializer.Serialize(new
        {
            formatVersion = 1,
            profile = "faithful",
            chunk = 3,
            source = new { archive = "fire.mkf", chunk = 3, encoding = "Windows YJ2", frameLayout = "PAL sprite WORD offset table + RLE frames" },
            decodedByteLength = 26,
            frameCount = 2,
            frames = new object[]
            {
                new { index = 0, sourceOffset = 6, width = 4, height = 1, png = "sprites/3/0.png", indices = B64(7, 0, 0, 0), alpha = B64(255, 0, 0, 0) },
                new { index = 1, sourceOffset = 14, width = 1, height = 1, png = "sprites/3/1.png", indices = B64(9), alpha = B64(255) },
                new { index = 2, sourceOffset = 20, width = 1, height = 1, png = "sprites/3/2.png", indices = B64(4), alpha = B64(255) },
            }
        }));
        File.WriteAllText(Path.Combine(Input, "sprites", "7.json"), JsonSerializer.Serialize(new
        {
            formatVersion = 1,
            profile = "faithful",
            chunk = 7,
            source = new { archive = "fire.mkf", chunk = 7, encoding = "Windows YJ2", frameLayout = "PAL sprite WORD offset table + RLE frames" },
            decodedByteLength = 12,
            frameCount = 1,
            frames = new object[]
            {
                new { index = 0, sourceOffset = 4, width = 2, height = 1, png = "sprites/7/0.png", indices = B64(11, 0), alpha = B64(255, 0) },
            }
        }));
        File.WriteAllText(Path.Combine(Input, "index.json"), JsonSerializer.Serialize(new
        {
            formatVersion = 1,
            profile = "faithful",
            command = "export-battle-images",
            parameters = new { archive = "fire" },
            sources = new object[]
            {
                new { name = "fire.mkf", byteLength = 4096, sha256 = new string('1', 64) },
                new { name = "pat.mkf", byteLength = 768, sha256 = new string('2', 64) },
            },
            palette = new { archive = "pat.mkf", chunk = 0, lighting = "day", usage = "PNG colorization only; frame indices and alpha are authoritative" },
            chunks = new object[]
            {
                new { index = 3, byteLength = 30, sha256 = new string('3', 64), compression = "yj2", decodedByteLength = 26, frameCount = 2, decodedFrameCount = 3, sprite = "sprites/3.json" },
                new { index = 7, byteLength = 16, sha256 = new string('7', 64), compression = "yj2", decodedByteLength = 12, frameCount = 1, decodedFrameCount = 1, sprite = "sprites/7.json" },
                new { index = 9, byteLength = 0, sha256 = EmptySha256, compression = "empty" },
            },
            totals = new { chunks = 3, decodedChunks = 2, emptyChunks = 1, frames = 4, files = 6 },
        }));
    }

    private static string B64(params byte[] bytes) => Convert.ToBase64String(bytes);

    // Literal PNG writer (BCL zlib + CRC32): the fixture's expected RGBA bytes are
    // produced independently of Godot's image importer and of the exporter's encoder.
    private static byte[] Png(int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> pixel)
    {
        var scanlines = new byte[height * (width * 4 + 1)];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int offset = y * (width * 4 + 1) + 1 + x * 4;
                var (r, g, b, a) = pixel(x, y);
                scanlines[y * (width * 4 + 1)] = 0; // filter type 0
                scanlines[offset] = r; scanlines[offset + 1] = g; scanlines[offset + 2] = b; scanlines[offset + 3] = a;
            }
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(scanlines, 0, scanlines.Length);
        byte[] idat = compressed.ToArray();
        var png = new List<byte>();
        png.AddRange(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8; header[9] = 6; // 8-bit RGBA
        png.AddRange(Chunk("IHDR", header));
        png.AddRange(Chunk("IDAT", idat));
        png.AddRange(Chunk("IEND", Array.Empty<byte>()));
        return png.ToArray();
    }

    private static byte[] Chunk(string type, byte[] data)
    {
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteInt32BigEndian(chunk.AsSpan(0, 4), data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length, 4), Crc32(chunk.AsSpan(4, 4 + data.Length)));
        return chunk;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xffffffff;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ (0xedb88320 & (0u - (crc & 1)));
        }
        return ~crc;
    }

    private Task<PrepareProcessTests.Result> Editor(params string[] args) => PrepareProcessTests.Run(Godot,
        ["--headless", "--path", Project, "res://Editor/PrepareFireEffects.tscn", "--", .. args]);
    private Task<PrepareProcessTests.Result> Wrapper(params string[] args) => PrepareProcessTests.Run("pwsh",
        ["-NoProfile", "-NonInteractive", "-File", Path.Combine(repo, "scripts", "Prepare-FireEffects.ps1"), .. args]);

    private static string[] TopLevel(string directory) => Directory.GetFileSystemEntries(directory)
        .Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;

    private static string[] Files(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray();

    private static string[] Snapshot(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
        .Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/') + ":" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
        if (Directory.Exists(output)) Directory.Delete(output, true);
    }
}
