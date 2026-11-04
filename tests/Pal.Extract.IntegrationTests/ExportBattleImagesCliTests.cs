using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Pal.Extract.Tests;

// export-battle-images CLI (#92 abc, #93 ball, #94 f): micro abc.mkf/ball.mkf/f.mkf
// + pat.mkf fixtures through the real process. Expected manifest identities (SHA-256,
// totals, file sets) are computed independently in the test, not echoed from tool output.
public sealed class ExportBattleImagesCliTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"pal-battle-images-{Guid.NewGuid():N}");
    private readonly string input;
    private readonly string output;

    // Table [3, 7, 10]: two nominal frames plus the real extra entry at byte 20.
    private static byte[] Sprite => [3, 0, 7, 0, 10, 0,
        4, 0, 1, 0, 1, 7, 0x83, 0,
        1, 0, 1, 0, 1, 9,
        1, 0, 1, 0, 1, 4];

    // Table [2, 6]: one nominal frame at byte 4, zero-sentinel final word.
    private static byte[] SingleFrameSprite => [2, 0, 6, 0,
        2, 0, 1, 0, 1, 11, 0x81, 0];

    // ball.mkf chunk (#93): a raw PAL RLE bitmap read without decompression -
    // 0x00000002 file header, 4x2, runs 1 opaque(7), skip 2, 3 opaque(8,9,10),
    // 2 opaque(11,12). No YJ2 wrapper and no sprite offset table.
    private static byte[] ItemBitmap => [2, 0, 0, 0, 4, 0, 2, 0,
        1, 7, 0x82, 3, 8, 9, 10, 2, 11, 12];

    // ball.mkf 1x1 bitmap: a single opaque pixel (index 5).
    private static byte[] OnePixelBitmap => [2, 0, 0, 0, 1, 0, 1, 0, 1, 5];

    // f.mkf sprite shapes (#94), compressed with YJ2 like every real f chunk: the
    // real archive has no empty chunk and every final table word is the zero
    // sentinel, so chunk 0 is a full player-shaped sprite whose extra entry decodes
    // (table [3, 7, 10]) and chunk 1 carries the sentinel (table [3, 7, 0]).
    private static byte[] ExtraEntrySprite => [3, 0, 7, 0, 10, 0,
        2, 0, 1, 0, 1, 5, 0x81, 0,
        1, 0, 1, 0, 1, 6,
        1, 0, 1, 0, 1, 7];

    private static byte[] SentinelSprite => [3, 0, 7, 0, 0, 0,
        2, 0, 1, 0, 1, 5, 0x81, 0,
        1, 0, 1, 0, 1, 6];

    public ExportBattleImagesCliTests()
    {
        input = Path.Combine(directory, "input");
        output = Path.Combine(directory, "package");
        Directory.CreateDirectory(input);
        var palette = new byte[768];
        palette[0] = 3; palette[1] = 4; palette[2] = 5;
        palette[7 * 3] = 63; palette[7 * 3 + 1] = 0; palette[7 * 3 + 2] = 30;
        WriteArchive("Pat.mkf", palette);
    }

    [Fact]
    public async Task Export_battle_images_decodes_every_chunk_into_a_complete_package_with_a_manifest()
    {
        WriteArchive("Abc.mkf", [], Compress(Sprite), Compress(SingleFrameSprite));
        var result = await Export();
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Empty(result.Stderr);
        string[] files = [.. Directory.GetFiles(output, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(output, path).Replace('\\', '/')).Order()];
        Assert.Equal(new[]
        {
            "index.json",
            "sprites/1.json", "sprites/1/0.png", "sprites/1/1.png", "sprites/1/2.png",
            "sprites/2.json", "sprites/2/0.png",
        }, files);
        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "index.json")));
        var root = index.RootElement;
        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal("faithful", root.GetProperty("profile").GetString());
        Assert.Equal("export-battle-images", root.GetProperty("command").GetString());
        Assert.Equal("abc", root.GetProperty("parameters").GetProperty("archive").GetString());
        var sources = root.GetProperty("sources").EnumerateArray().ToArray();
        Assert.Equal(2, sources.Length);
        Assert.Equal("abc.mkf", sources[0].GetProperty("name").GetString());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(input, "Abc.mkf")))),
            sources[0].GetProperty("sha256").GetString());
        Assert.Equal("pat.mkf", sources[1].GetProperty("name").GetString());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(input, "Pat.mkf")))),
            sources[1].GetProperty("sha256").GetString());
        var palette = root.GetProperty("palette");
        Assert.Equal("pat.mkf", palette.GetProperty("archive").GetString());
        Assert.Equal(0, palette.GetProperty("chunk").GetInt32());
        Assert.Equal("day", palette.GetProperty("lighting").GetString());
        var chunks = root.GetProperty("chunks").EnumerateArray().ToArray();
        Assert.Equal(3, chunks.Length);
        Assert.Equal(0, chunks[0].GetProperty("index").GetInt32());
        Assert.Equal(0, chunks[0].GetProperty("byteLength").GetInt32());
        Assert.Equal("empty", chunks[0].GetProperty("compression").GetString());
        Assert.Throws<KeyNotFoundException>(() => chunks[0].GetProperty("frameCount"));
        Assert.Equal("yj2", chunks[1].GetProperty("compression").GetString());
        Assert.Equal(26, chunks[1].GetProperty("decodedByteLength").GetInt32());
        Assert.Equal(2, chunks[1].GetProperty("frameCount").GetInt32());
        Assert.Equal(3, chunks[1].GetProperty("decodedFrameCount").GetInt32()); // the extra table entry decodes
        Assert.Equal("sprites/1.json", chunks[1].GetProperty("sprite").GetString());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(ChunkBytes(input, "Abc.mkf", 1))),
            chunks[1].GetProperty("sha256").GetString());
        Assert.Equal(1, chunks[2].GetProperty("frameCount").GetInt32());
        Assert.Equal(1, chunks[2].GetProperty("decodedFrameCount").GetInt32());
        var totals = root.GetProperty("totals");
        Assert.Equal(3, totals.GetProperty("chunks").GetInt32());
        Assert.Equal(2, totals.GetProperty("decodedChunks").GetInt32());
        Assert.Equal(1, totals.GetProperty("emptyChunks").GetInt32());
        Assert.Equal(4, totals.GetProperty("frames").GetInt32());
        Assert.Equal(6, totals.GetProperty("files").GetInt32());
        using var sprite = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "sprites", "1.json")));
        var document = sprite.RootElement;
        Assert.Equal(1, document.GetProperty("chunk").GetInt32());
        Assert.Equal("abc.mkf", document.GetProperty("source").GetProperty("archive").GetString());
        Assert.Equal("Windows YJ2", document.GetProperty("source").GetProperty("encoding").GetString());
        var frames = document.GetProperty("frames").EnumerateArray().ToArray();
        Assert.Equal(3, frames.Length);
        Assert.Equal(4, frames[0].GetProperty("width").GetInt32());
        Assert.Equal(1, frames[0].GetProperty("height").GetInt32());
        Assert.Equal(6, frames[0].GetProperty("sourceOffset").GetInt32());
        Assert.Equal(new byte[] { 7, 0, 0, 0 }, frames[0].GetProperty("indices").GetBytesFromBase64());
        Assert.Equal(new byte[] { 255, 0, 0, 0 }, frames[0].GetProperty("alpha").GetBytesFromBase64());
        foreach ((int frame, int width, int height) in new[] { (0, 4, 1), (1, 1, 1), (2, 1, 1) })
        {
            Assert.Equal($"sprites/1/{frame}.png", frames[frame].GetProperty("png").GetString());
            ValidatePng(Path.Combine(output, "sprites", "1", $"{frame}.png"), width, height);
        }
        Assert.DoesNotContain(directory, File.ReadAllText(Path.Combine(output, "index.json")));
    }

    [Fact]
    public async Task Export_battle_images_decodes_ball_chunks_as_single_frame_bitmaps()
    {
        WriteArchive("Ball.mkf", [], ItemBitmap, OnePixelBitmap);
        var result = await ExportBall();
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Empty(result.Stderr);
        string[] files = [.. Directory.GetFiles(output, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(output, path).Replace('\\', '/')).Order()];
        Assert.Equal(new[]
        {
            "index.json",
            "sprites/1.json", "sprites/1/0.png",
            "sprites/2.json", "sprites/2/0.png",
        }, files);
        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "index.json")));
        var root = index.RootElement;
        Assert.Equal("ball", root.GetProperty("parameters").GetProperty("archive").GetString());
        var sources = root.GetProperty("sources").EnumerateArray().ToArray();
        Assert.Equal(2, sources.Length);
        Assert.Equal("ball.mkf", sources[0].GetProperty("name").GetString());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(input, "Ball.mkf")))),
            sources[0].GetProperty("sha256").GetString());
        var chunks = root.GetProperty("chunks").EnumerateArray().ToArray();
        Assert.Equal(3, chunks.Length);
        Assert.Equal("empty", chunks[0].GetProperty("compression").GetString());
        Assert.Throws<KeyNotFoundException>(() => chunks[0].GetProperty("frameCount"));
        // Raw storage: no decompression, so decodedByteLength equals the raw chunk bytes.
        Assert.Equal("rle", chunks[1].GetProperty("compression").GetString());
        Assert.Equal(18, chunks[1].GetProperty("byteLength").GetInt32());
        Assert.Equal(18, chunks[1].GetProperty("decodedByteLength").GetInt32());
        Assert.Equal(1, chunks[1].GetProperty("frameCount").GetInt32());
        Assert.Equal(1, chunks[1].GetProperty("decodedFrameCount").GetInt32());
        Assert.Equal("sprites/1.json", chunks[1].GetProperty("sprite").GetString());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(ChunkBytes(input, "Ball.mkf", 1))),
            chunks[1].GetProperty("sha256").GetString());
        Assert.Equal(1, chunks[2].GetProperty("frameCount").GetInt32());
        var totals = root.GetProperty("totals");
        Assert.Equal(3, totals.GetProperty("chunks").GetInt32());
        Assert.Equal(2, totals.GetProperty("decodedChunks").GetInt32());
        Assert.Equal(1, totals.GetProperty("emptyChunks").GetInt32());
        Assert.Equal(2, totals.GetProperty("frames").GetInt32());
        Assert.Equal(4, totals.GetProperty("files").GetInt32());
        using var sprite = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "sprites", "1.json")));
        var document = sprite.RootElement;
        Assert.Equal("ball.mkf", document.GetProperty("source").GetProperty("archive").GetString());
        Assert.Equal("uncompressed PAL RLE bitmap", document.GetProperty("source").GetProperty("encoding").GetString());
        var frame = Assert.Single(document.GetProperty("frames").EnumerateArray());
        Assert.Equal(4, frame.GetProperty("width").GetInt32());
        Assert.Equal(2, frame.GetProperty("height").GetInt32());
        Assert.Equal(new byte[] { 7, 0, 0, 8, 9, 10, 11, 12 }, frame.GetProperty("indices").GetBytesFromBase64());
        Assert.Equal(new byte[] { 255, 0, 0, 255, 255, 255, 255, 255 }, frame.GetProperty("alpha").GetBytesFromBase64());
        Assert.Equal("sprites/1/0.png", frame.GetProperty("png").GetString());
        ValidatePng(Path.Combine(output, "sprites", "1", "0.png"), 4, 2);
        ValidatePng(Path.Combine(output, "sprites", "2", "0.png"), 1, 1);
    }

    [Fact]
    public async Task Export_battle_images_decodes_f_chunks_as_yj2_sprites()
    {
        WriteArchive("F.mkf", Compress(ExtraEntrySprite), Compress(SentinelSprite));
        var result = await ExportF();
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Empty(result.Stderr);
        string[] files = [.. Directory.GetFiles(output, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(output, path).Replace('\\', '/')).Order()];
        Assert.Equal(new[]
        {
            "index.json",
            "sprites/0.json", "sprites/0/0.png", "sprites/0/1.png", "sprites/0/2.png",
            "sprites/1.json", "sprites/1/0.png", "sprites/1/1.png",
        }, files);
        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "index.json")));
        var root = index.RootElement;
        Assert.Equal("f", root.GetProperty("parameters").GetProperty("archive").GetString());
        var sources = root.GetProperty("sources").EnumerateArray().ToArray();
        Assert.Equal(2, sources.Length);
        Assert.Equal("f.mkf", sources[0].GetProperty("name").GetString());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(input, "F.mkf")))),
            sources[0].GetProperty("sha256").GetString());
        var chunks = root.GetProperty("chunks").EnumerateArray().ToArray();
        Assert.Equal(2, chunks.Length);
        // f chunks are YJ2-compressed sprites (PAL_MKFDecompressChunk, like abc).
        Assert.Equal("yj2", chunks[0].GetProperty("compression").GetString());
        Assert.Equal(26, chunks[0].GetProperty("decodedByteLength").GetInt32());
        Assert.Equal(2, chunks[0].GetProperty("frameCount").GetInt32());
        Assert.Equal(3, chunks[0].GetProperty("decodedFrameCount").GetInt32());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(ChunkBytes(input, "F.mkf", 0))),
            chunks[0].GetProperty("sha256").GetString());
        // The real archive's every chunk ends in the zero sentinel; decoded == nominal.
        Assert.Equal("yj2", chunks[1].GetProperty("compression").GetString());
        Assert.Equal(20, chunks[1].GetProperty("decodedByteLength").GetInt32());
        Assert.Equal(2, chunks[1].GetProperty("frameCount").GetInt32());
        Assert.Equal(2, chunks[1].GetProperty("decodedFrameCount").GetInt32());
        var totals = root.GetProperty("totals");
        Assert.Equal(2, totals.GetProperty("chunks").GetInt32());
        Assert.Equal(2, totals.GetProperty("decodedChunks").GetInt32());
        Assert.Equal(0, totals.GetProperty("emptyChunks").GetInt32());
        Assert.Equal(5, totals.GetProperty("frames").GetInt32());
        Assert.Equal(7, totals.GetProperty("files").GetInt32());
        using var sprite = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "sprites", "0.json")));
        var document = sprite.RootElement;
        Assert.Equal("f.mkf", document.GetProperty("source").GetProperty("archive").GetString());
        Assert.Equal("Windows YJ2", document.GetProperty("source").GetProperty("encoding").GetString());
        Assert.Equal("PAL sprite WORD offset table + RLE frames",
            document.GetProperty("source").GetProperty("frameLayout").GetString());
        var frames = document.GetProperty("frames").EnumerateArray().ToArray();
        Assert.Equal(3, frames.Length);
        Assert.Equal(6, frames[0].GetProperty("sourceOffset").GetInt32());
        Assert.Equal(new byte[] { 5, 0 }, frames[0].GetProperty("indices").GetBytesFromBase64());
        Assert.Equal(new byte[] { 255, 0 }, frames[0].GetProperty("alpha").GetBytesFromBase64());
        foreach ((int frame, int width, int height) in new[] { (0, 2, 1), (1, 1, 1), (2, 1, 1) })
        {
            Assert.Equal($"sprites/0/{frame}.png", frames[frame].GetProperty("png").GetString());
            ValidatePng(Path.Combine(output, "sprites", "0", $"{frame}.png"), width, height);
        }
        Assert.DoesNotContain(directory, File.ReadAllText(Path.Combine(output, "index.json")));
    }

    [Theory]
    [InlineData("yj1")]
    [InlineData("bad-table")]
    public async Task Export_battle_images_rejects_damaged_f_chunks_before_creating_output(string kind)
    {
        byte[] damaged = kind switch
        {
            "yj1" => "YJ_1"u8.ToArray(),
            _ => Compress([4, 0, 8, 0]),
        };
        WriteArchive("F.mkf", Compress(SentinelSprite), damaged);
        var result = await ExportF();
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("f.mkf", result.Stderr);
        Assert.Contains(kind == "yj1" ? "YJ1" : "chunk 1", result.Stderr);
        Assert.DoesNotContain(directory, result.Stderr);
        Assert.DoesNotContain("   at ", result.Stderr);
        Assert.False(Directory.Exists(output));
    }

    [Theory]
    [InlineData("truncated-bitmap")]
    [InlineData("zero-width")]
    [InlineData("trailing-bytes")]
    public async Task Export_battle_images_rejects_damaged_ball_chunks_before_creating_output(string kind)
    {
        byte[] damaged = kind switch
        {
            "truncated-bitmap" => [2, 0, 0, 0, 4, 0, 2, 0, 3, 8],
            "zero-width" => [2, 0, 0, 0, 0, 0, 1, 0],
            _ => [.. ItemBitmap, 0, 0],
        };
        WriteArchive("Ball.mkf", [], ItemBitmap, damaged);
        var result = await ExportBall();
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("ball.mkf", result.Stderr);
        Assert.Contains("chunk 2", result.Stderr);
        Assert.DoesNotContain(directory, result.Stderr);
        Assert.DoesNotContain("   at ", result.Stderr);
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public async Task Export_battle_images_is_byte_deterministic_and_preserves_readonly_inputs()
    {
        WriteArchive("Abc.mkf", [], Compress(Sprite));
        var before = Directory.GetFiles(input).ToDictionary(path => path,
            path => (Bytes: File.ReadAllBytes(path), Modified: File.GetLastWriteTimeUtc(path)));
        foreach (string path in before.Keys) File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            Directory.CreateDirectory(output);
            Assert.Equal(0, (await Export()).ExitCode);
            string repeat = Path.Combine(directory, "repeat");
            Assert.Equal(0, (await Export(repeat)).ExitCode);
            foreach (string file in Directory.GetFiles(output, "*", SearchOption.AllDirectories))
                Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(Path.Combine(repeat, Path.GetRelativePath(output, file))));
            foreach (var (path, snapshot) in before)
            {
                Assert.Equal(snapshot.Bytes, File.ReadAllBytes(path));
                Assert.Equal(snapshot.Modified, File.GetLastWriteTimeUtc(path));
                Assert.Equal(FileAttributes.ReadOnly, File.GetAttributes(path) & FileAttributes.ReadOnly);
            }
            Assert.Equal(1, (await Export()).ExitCode);
        }
        finally { foreach (string path in before.Keys) File.SetAttributes(path, FileAttributes.Normal); }
    }

    [Theory]
    [InlineData("yj1")]
    [InlineData("truncated-yj2")]
    [InlineData("bad-table")]
    [InlineData("empty-sprite")]
    public async Task Export_battle_images_rejects_damaged_chunks_before_creating_output(string kind)
    {
        byte[] damaged = kind switch
        {
            "yj1" => "YJ_1"u8.ToArray(),
            "truncated-yj2" => [1, 0, 0, 0],
            "bad-table" => Compress([4, 0, 8, 0]),
            _ => Compress([1, 0]),
        };
        WriteArchive("Abc.mkf", [], Compress(SingleFrameSprite), damaged);
        var result = await Export();
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("abc.mkf", result.Stderr);
        Assert.Contains(kind == "yj1" ? "YJ1" : kind == "empty-sprite" ? "frame count" : "chunk 2", result.Stderr);
        Assert.DoesNotContain(directory, result.Stderr);
        Assert.DoesNotContain("   at ", result.Stderr);
        Assert.False(Directory.Exists(output));
    }

    [Theory]
    [InlineData("Abc.mkf")]
    [InlineData("Pat.mkf")]
    public async Task Export_battle_images_requires_both_archives(string name)
    {
        WriteArchive("Abc.mkf", [], Compress(SingleFrameSprite));
        File.Delete(Path.Combine(input, name));
        var result = await Export();
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains(name.ToLowerInvariant(), result.Stderr);
        Assert.Contains("missing", result.Stderr);
        Assert.False(Directory.Exists(output));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unknown-archive")]
    [InlineData("wrong-order")]
    [InlineData("empty-input")]
    [InlineData("empty-archive")]
    public async Task Export_battle_images_requires_explicit_valid_options_without_echoing_local_paths(string kind)
    {
        WriteArchive("Abc.mkf", [], Compress(SingleFrameSprite));
        string[] args = kind switch
        {
            "missing" => ["export-battle-images", "--input", input, "--output", output],
            "unknown-archive" => ["export-battle-images", "--input", input, "--archive", "fbp", "--output", output],
            "wrong-order" => ["export-battle-images", "--archive", "abc", "--input", input, "--output", output],
            "empty-input" => ["export-battle-images", "--input", "", "--archive", "abc", "--output", output],
            _ => ["export-battle-images", "--input", input, "--archive", "", "--output", output],
        };
        var result = await CliProcess.Run(directory, args);
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("Unsupported arguments", result.Stderr);
        Assert.Contains("Use --help", result.Stderr);
        Assert.DoesNotContain(directory, result.Stderr);
        Assert.False(Directory.Exists(output));
    }

    [Theory]
    [InlineData("input")]
    [InlineData("nested-input")]
    [InlineData("nonempty")]
    [InlineData("file")]
    [InlineData("output-link")]
    [InlineData("output-linked-ancestor")]
    [InlineData("input-link")]
    public async Task Export_battle_images_refuses_unsafe_output_without_mutating_inputs_or_existing_files(string kind)
    {
        WriteArchive("Abc.mkf", [], Compress(SingleFrameSprite));
        string sentinel = Path.Combine(directory, "keep.txt");
        File.WriteAllText(sentinel, "unchanged");
        if (kind == "nonempty") { Directory.CreateDirectory(output); File.Copy(sentinel, Path.Combine(output, "keep.txt")); }
        string link = Path.Combine(directory, "link");
        bool linked = kind.Contains("link");
        if (linked) DirectoryLinkFixture.Create(link, input);
        var before = Directory.GetFiles(input).ToDictionary(path => path, Hash);
        try
        {
            string destination = kind switch
            {
                "input" => input,
                "nested-input" => Path.Combine(input, "new"),
                "file" => sentinel,
                "output-link" => link,
                "output-linked-ancestor" => Path.Combine(link, "new"),
                _ => output,
            };
            var result = await Export(destination, source: kind == "input-link" ? link : null);
            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Stdout);
            Assert.DoesNotContain(directory, result.Stderr);
            Assert.Equal("unchanged", File.ReadAllText(sentinel));
            foreach (var (path, hash) in before) Assert.Equal(hash, Hash(path));
            Assert.False(Directory.Exists(Path.Combine(input, "new")));
            if (kind == "nonempty") Assert.Equal(new[] { "keep.txt" }, Directory.GetFiles(output).Select(Path.GetFileName));
        }
        finally { if (linked) Directory.Delete(link); }
    }

    [Fact]
    public async Task Help_documents_the_frozen_verb_shape_for_the_remaining_mkf_tickets()
    {
        var result = await CliProcess.Run(directory, "--help");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("export-battle-images --input <directory> --archive <abc|ball|f> --output <new-or-empty-directory>", result.Stdout);
    }

    private Task<(int ExitCode, string Stdout, string Stderr)> Export(string? destination = null, string? source = null) =>
        CliProcess.Run(directory, "export-battle-images", "--input", source ?? input, "--archive", "abc",
            "--output", destination ?? output);

    private Task<(int ExitCode, string Stdout, string Stderr)> ExportBall(string? destination = null) =>
        CliProcess.Run(directory, "export-battle-images", "--input", input, "--archive", "ball",
            "--output", destination ?? output);

    private Task<(int ExitCode, string Stdout, string Stderr)> ExportF(string? destination = null) =>
        CliProcess.Run(directory, "export-battle-images", "--input", input, "--archive", "f",
            "--output", destination ?? output);

    private void WriteArchive(string name, params byte[][] chunks) => MkfFixture.Write(input, name, chunks);

    private static byte[] Compress(byte[] raw)
    {
        var encoder = new Yj2TestData(raw.Length);
        foreach (byte value in raw) encoder.Literal(value);
        return encoder.End();
    }

    private static byte[] ChunkBytes(string input, string name, int index)
    {
        byte[] archive = File.ReadAllBytes(Path.Combine(input, name));
        uint start = BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(4 * index));
        uint end = BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(4 * (index + 1)));
        return archive[(int)start..(int)end];
    }

    // Output identity check, not another image decoder (SceneBundleExporter's rule).
    private static void ValidatePng(string path, int width, int height)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[24];
        stream.ReadExactly(header);
        Assert.True(header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            && header.Slice(12, 4).SequenceEqual("IHDR"u8)
            && BinaryPrimitives.ReadInt32BigEndian(header[16..]) == width
            && BinaryPrimitives.ReadInt32BigEndian(header[20..]) == height, $"{path} dimensions do not match");
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
