using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using Xunit;

namespace Pal.Prepare.Tests;

// Process contracts only (#218): argv/marker/exit/rejection through the real Godot
// editor scene and wrapper. The literal native roundtrip and checked-load contracts
// live in the Pal.Game GdUnit suite; nothing here derives expectations from output.
public sealed class ItemBitmapsPrepareProcessTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "pal-ball-process-" + Guid.NewGuid().ToString("N"));
    private readonly string repo;
    private readonly string output;
    private string Input => Path.Combine(root, "ball");
    private string Project => Path.Combine(repo, "src", "Pal.Game");
    private static string Godot => Environment.GetEnvironmentVariable("PAL_TEST_GODOT") ?? throw new InvalidOperationException("Set PAL_TEST_GODOT.");
    private const string Marker = "PAL_PREPARE_ITEM_BITMAPS:";

    // The artificial ball package mirrors the ball decoder's literal fixture shape: chunk 37
    // is a 4x3 single RLE bitmap, chunk 41 a 2x2 one, chunk 0 stays empty. Chunk ids keep the
    // item.wBitmap identity, never renumbered item indices.
    private const string EmptySha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    public ItemBitmapsPrepareProcessTests()
    {
        repo = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(repo, "GodotPal.sln"))) repo = Directory.GetParent(repo)!.FullName;
        output = Path.Combine(Project, "Assets", "PrepareTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Input);
    }

    [Fact]
    public async Task Editor_publishes_native_item_bitmaps_and_mapping_without_overwrite()
    {
        WritePackage();
        string[] before = Snapshot(Input);
        var result = await Editor("--ball-input", Input, "--ball-output", output);
        Assert.True(result.ExitCode == 0, result.Text);
        Assert.Contains(Marker + "2", result.Text);
        Assert.Equal(new[] { "Sprites", "Textures" }, TopLevel(output));
        Assert.Equal(new[]
        {
            "Sprites/37.tres", "Sprites/41.tres",
            "Textures/37/0.tres", "Textures/41/0.tres",
        }, Files(output));
        Assert.Equal(before, Snapshot(Input));
        string[] published = Snapshot(output);
        var collision = await Editor("--ball-input", Input, "--ball-output", output);
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
            "missing" => ["--ball-input", Input],
            "extra" => ["--ball-input", Input, "--ball-output", output, "--extra", "value"],
            "reordered" => ["--ball-output", output, "--ball-input", Input],
            _ => ["--unknown", Input, "--ball-output", output]
        };
        var result = await Editor(args);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Ball item bitmaps native import failed: invalid item-bitmaps prepare arguments", result.Text);
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
        Assert.Contains("Prepare-ItemBitmaps argument error:", result.Text);
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
        Assert.Contains("Prepare-ItemBitmaps failed:", result.Text);
        Assert.DoesNotContain(unavailable, result.Text);
        Assert.DoesNotContain(Marker, result.Text);
        if (kind == "missing-input") Assert.False(Directory.Exists(Input));
        else Assert.Equal(before, Snapshot(Input));
        if (kind == "nonempty-output") Assert.True(Directory.Exists(Path.Combine(output, "keep")));
        else Assert.False(Directory.Exists(selectedOutput));
    }

    [Fact]
    public async Task Normal_wrapper_publishes_native_item_bitmaps_and_never_overwrites()
    {
        WritePackage();
        string[] before = Snapshot(Input);
        var prepared = await Wrapper("-InputPath", Input, "-OutputPath", output, "-GodotPath", Godot);
        Assert.True(prepared.ExitCode == 0, prepared.Text);
        Assert.Contains(Marker + "2", prepared.Text);
        Assert.Contains("Prepared native Godot item bitmap sprites.", prepared.Text);
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
        Directory.CreateDirectory(Path.Combine(Input, "sprites", "37"));
        Directory.CreateDirectory(Path.Combine(Input, "sprites", "41"));
        File.WriteAllBytes(Path.Combine(Input, "sprites", "37", "0.png"), Png(4, 3, (x, y) => ((byte)((150 + x) & 0xFF), (byte)90, (byte)((60 + y) & 0xFF), (byte)255)));
        File.WriteAllBytes(Path.Combine(Input, "sprites", "41", "0.png"), Png(2, 2, (x, y) => ((byte)160, (byte)80, (byte)(40 + x), (byte)255)));
        File.WriteAllText(Path.Combine(Input, "sprites", "37.json"), JsonSerializer.Serialize(new
        {
            formatVersion = 1,
            profile = "faithful",
            chunk = 37,
            source = new { archive = "ball.mkf", chunk = 37, encoding = "uncompressed PAL RLE bitmap", frameLayout = "single RLE bitmap: optional 0x00000002 header, WORD width/height, RLE runs" },
            decodedByteLength = 16,
            frameCount = 1,
            frames = new object[]
            {
                new { index = 0, sourceOffset = 6, width = 4, height = 3, png = "sprites/37/0.png", indices = B64(37, 0, 41, 0, 42, 0, 43, 0, 44, 0, 45, 0), alpha = B64(255, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255, 0) },
            }
        }));
        File.WriteAllText(Path.Combine(Input, "sprites", "41.json"), JsonSerializer.Serialize(new
        {
            formatVersion = 1,
            profile = "faithful",
            chunk = 41,
            source = new { archive = "ball.mkf", chunk = 41, encoding = "uncompressed PAL RLE bitmap", frameLayout = "single RLE bitmap: optional 0x00000002 header, WORD width/height, RLE runs" },
            decodedByteLength = 10,
            frameCount = 1,
            frames = new object[]
            {
                new { index = 0, sourceOffset = 6, width = 2, height = 2, png = "sprites/41/0.png", indices = B64(41, 42, 43, 44), alpha = B64(255, 255, 0, 0) },
            }
        }));
        File.WriteAllText(Path.Combine(Input, "index.json"), JsonSerializer.Serialize(new
        {
            formatVersion = 1,
            profile = "faithful",
            command = "export-battle-images",
            parameters = new { archive = "ball" },
            sources = new object[]
            {
                new { name = "ball.mkf", byteLength = 4096, sha256 = new string('1', 64) },
                new { name = "pat.mkf", byteLength = 768, sha256 = new string('2', 64) },
            },
            palette = new { archive = "pat.mkf", chunk = 0, lighting = "day", usage = "PNG colorization only; frame indices and alpha are authoritative" },
            chunks = new object[]
            {
                new { index = 0, byteLength = 0, sha256 = EmptySha256, compression = "empty" },
                new { index = 37, byteLength = 16, sha256 = new string('3', 64), compression = "rle", decodedByteLength = 16, frameCount = 1, decodedFrameCount = 1, sprite = "sprites/37.json" },
                new { index = 41, byteLength = 10, sha256 = new string('4', 64), compression = "rle", decodedByteLength = 10, frameCount = 1, decodedFrameCount = 1, sprite = "sprites/41.json" },
            },
            totals = new { chunks = 3, decodedChunks = 2, emptyChunks = 1, frames = 2, files = 4 },
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
        ["--headless", "--path", Project, "res://Editor/PrepareItemBitmaps.tscn", "--", .. args]);
    private Task<PrepareProcessTests.Result> Wrapper(params string[] args) => PrepareProcessTests.Run("pwsh",
        ["-NoProfile", "-NonInteractive", "-File", Path.Combine(repo, "scripts", "Prepare-ItemBitmaps.ps1"), .. args]);

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
