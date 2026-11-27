using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Pal.Extract.Tests;

// export-battle-images CLI (#92 abc, #93 ball, #94 f, #95 fbp, #146 fire): micro
// abc.mkf/ball.mkf/f.mkf/fbp.mkf/fire.mkf + pat.mkf fixtures through the real process. Expected
// manifest identities (SHA-256, totals, file sets) are computed independently in the
// test, not echoed from tool output.
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

    // fbp.mkf screens (#95), compressed with YJ2 like every real fbp chunk: each chunk
    // decompresses to exactly the 64000 raw palette indices of one 320x200 screen
    // (PAL_FBPBlitToSurface linear copy). Row y repeats index (y + base) & 0xff; the
    // encoder emits one literal plus distance-1 matches per row. The real archive has
    // empty chunks 5 and 58, so chunk 1 of the fixture is empty.
    private static byte[] CompressScreen(byte baseIndex)
    {
        var encoder = new Yj2TestData(320 * 200);
        for (int y = 0; y < 200; y++)
        {
            encoder.Literal((byte)(y + baseIndex));
            encoder.Match(67, 1).Match(67, 1).Match(67, 1).Match(67, 1).Match(51, 1);
        }
        return encoder.End();
    }

    private static byte[] ExpectedScreen(byte baseIndex)
    {
        var plane = new byte[320 * 200];
        for (int y = 0; y < 200; y++)
            for (int x = 0; x < 320; x++)
                plane[y * 320 + x] = (byte)(y + baseIndex);
        return plane;
    }

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
    public async Task Export_fire_decodes_complete_indexed_package()
    {
        // These are literal sprite bytes (not decoded expected data). The offset
        // tables place A's frames at 6, 14, 20 and B's sole frame at 4.
        byte[][] rawChunks = [[], Compress(Sprite), Compress(SingleFrameSprite)];
        WriteArchive("FiRe.mkf", rawChunks);
        var result = await ExportFire();
        // Designated RED: without the production fire strategy the child exits 2.
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Empty(result.Stderr);
        Assert.Equal(new[]
        {
            "index.json",
            "sprites/1.json", "sprites/1/0.png", "sprites/1/1.png", "sprites/1/2.png",
            "sprites/2.json", "sprites/2/0.png",
        }, PackageFiles(output));
        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "index.json")));
        var root = index.RootElement;
        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal("faithful", root.GetProperty("profile").GetString());
        Assert.Equal("export-battle-images", root.GetProperty("command").GetString());
        Assert.Equal("fire", root.GetProperty("parameters").GetProperty("archive").GetString());
        var sources = root.GetProperty("sources").EnumerateArray().ToArray();
        Assert.Equal(2, sources.Length);
        foreach ((int sourceIndex, string fileName, string archiveName) in new[]
            { (0, "FiRe.mkf", "fire.mkf"), (1, "Pat.mkf", "pat.mkf") })
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(input, fileName));
            Assert.Equal(archiveName, sources[sourceIndex].GetProperty("name").GetString());
            Assert.Equal(bytes.LongLength, sources[sourceIndex].GetProperty("byteLength").GetInt64());
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), sources[sourceIndex].GetProperty("sha256").GetString());
        }
        var palette = root.GetProperty("palette");
        Assert.Equal("pat.mkf", palette.GetProperty("archive").GetString());
        Assert.Equal(0, palette.GetProperty("chunk").GetInt32());
        Assert.Equal("day", palette.GetProperty("lighting").GetString());
        Assert.Equal("PNG colorization only; frame indices and alpha are authoritative", palette.GetProperty("usage").GetString());
        var chunks = root.GetProperty("chunks").EnumerateArray().ToArray();
        Assert.Equal(3, chunks.Length);
        for (int chunk = 0; chunk < 3; chunk++)
        {
            Assert.Equal(chunk, chunks[chunk].GetProperty("index").GetInt32());
            Assert.Equal(rawChunks[chunk].Length, chunks[chunk].GetProperty("byteLength").GetInt32());
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(rawChunks[chunk])), chunks[chunk].GetProperty("sha256").GetString());
            Assert.Equal(chunk == 0 ? "empty" : "yj2", chunks[chunk].GetProperty("compression").GetString());
        }
        Assert.Equal(new[] { "byteLength", "compression", "index", "sha256" },
            chunks[0].EnumerateObject().Select(property => property.Name).Order());
        foreach ((int chunk, int decodedBytes, int nominal, int actual) in new[] { (1, 26, 2, 3), (2, 12, 1, 1) })
        {
            Assert.Equal(decodedBytes, chunks[chunk].GetProperty("decodedByteLength").GetInt32());
            Assert.Equal(nominal, chunks[chunk].GetProperty("frameCount").GetInt32());
            Assert.Equal(actual, chunks[chunk].GetProperty("decodedFrameCount").GetInt32());
            Assert.Equal($"sprites/{chunk}.json", chunks[chunk].GetProperty("sprite").GetString());
            using var sprite = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "sprites", $"{chunk}.json")));
            var document = sprite.RootElement;
            Assert.Equal(1, document.GetProperty("formatVersion").GetInt32());
            Assert.Equal("faithful", document.GetProperty("profile").GetString());
            Assert.Equal(chunk, document.GetProperty("chunk").GetInt32());
            Assert.Equal(decodedBytes, document.GetProperty("decodedByteLength").GetInt32());
            Assert.Equal(nominal, document.GetProperty("frameCount").GetInt32());
            var source = document.GetProperty("source");
            Assert.Equal("fire.mkf", source.GetProperty("archive").GetString());
            Assert.Equal(chunk, source.GetProperty("chunk").GetInt32());
            Assert.Equal("Windows YJ2", source.GetProperty("encoding").GetString());
            Assert.Equal("PAL sprite WORD offset table + RLE frames", source.GetProperty("frameLayout").GetString());
            Assert.Equal(actual, document.GetProperty("frames").GetArrayLength());
            Assert.DoesNotContain(directory, sprite.RootElement.GetRawText());
        }
        // Every exported frame has an independent hand-walked expectation, including
        // the optional extra frame and the next chunk's original identity.
        using var first = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "sprites", "1.json")));
        using var second = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "sprites", "2.json")));
        AssertFireFrame(first.RootElement, 1, 0, 6, 4, [7, 0, 0, 0], [255, 0, 0, 0]);
        AssertFireFrame(first.RootElement, 1, 1, 14, 1, [9], [255]);
        AssertFireFrame(first.RootElement, 1, 2, 20, 1, [4], [255]);
        AssertFireFrame(second.RootElement, 2, 0, 4, 2, [11, 0], [255, 0]);
        var totals = root.GetProperty("totals");
        Assert.Equal(3, totals.GetProperty("chunks").GetInt32());
        Assert.Equal(2, totals.GetProperty("decodedChunks").GetInt32());
        Assert.Equal(1, totals.GetProperty("emptyChunks").GetInt32());
        Assert.Equal(4, totals.GetProperty("frames").GetInt32());
        Assert.Equal(6, totals.GetProperty("files").GetInt32());
        // PAT7=(63,0,30) -> RGBA=(252,0,120,255); skipped pixels retain PAT0
        // RGB=(12,16,20), but alpha=0. This checks the preview, not GPU rendering.
        Assert.Equal(new byte[]
        {
            0, 252, 0, 120, 255, 12, 16, 20, 0, 12, 16, 20, 0, 12, 16, 20, 0,
        }, PngScanlines(Path.Combine(output, "sprites", "1", "0.png")));
        Assert.DoesNotContain(directory, root.GetRawText());
    }

    [Fact]
    public async Task Export_fire_is_byte_deterministic_and_preserves_readonly_inputs()
    {
        WriteArchive("Fire.mkf", [], Compress(Sprite), Compress(SingleFrameSprite));
        var before = Directory.GetFiles(input).ToDictionary(path => path,
            path => (Bytes: File.ReadAllBytes(path), Modified: File.GetLastWriteTimeUtc(path), Attributes: File.GetAttributes(path)));
        var readonlyAttributes = new Dictionary<string, FileAttributes>(before.Count);
        foreach (var (path, snapshot) in before)
        {
            File.SetAttributes(path, snapshot.Attributes | FileAttributes.ReadOnly);
            FileAttributes afterSet = File.GetAttributes(path);
            Assert.True((afterSet & FileAttributes.ReadOnly) != 0);
            readonlyAttributes[path] = afterSet;
        }
        try
        {
            Directory.CreateDirectory(output); // Empty output is allowed; the main test uses a new path.
            var first = await ExportFire();
            Assert.Equal(0, first.ExitCode);
            Assert.Empty(first.Stdout);
            Assert.Empty(first.Stderr);
            string repeat = Path.Combine(directory, "repeat");
            var second = await ExportFire(repeat);
            Assert.Equal(0, second.ExitCode);
            Assert.Empty(second.Stdout);
            Assert.Empty(second.Stderr);
            Assert.Equal(PackageFiles(output), PackageFiles(repeat));
            foreach (string file in PackageFiles(output))
                Assert.Equal(File.ReadAllBytes(Path.Combine(output, file)), File.ReadAllBytes(Path.Combine(repeat, file)));
            var packageBefore = PackageFiles(output).ToDictionary(file => file, file => Hash(Path.Combine(output, file)));
            var overwrite = await ExportFire();
            Assert.Equal(1, overwrite.ExitCode);
            Assert.Empty(overwrite.Stdout);
            Assert.Equal($"Battle image export failed: Output directory is not empty; existing data will not be overwritten.{Environment.NewLine}", overwrite.Stderr);
            Assert.Equal(packageBefore.Keys.Order(), PackageFiles(output));
            foreach (var (file, hash) in packageBefore) Assert.Equal(hash, Hash(Path.Combine(output, file)));
            Assert.Equal(before.Keys.Order(), Directory.GetFiles(input).Order());
            foreach (var (path, snapshot) in before)
            {
                Assert.Equal(snapshot.Bytes, File.ReadAllBytes(path));
                Assert.Equal(snapshot.Modified, File.GetLastWriteTimeUtc(path));
                Assert.Equal(readonlyAttributes[path], File.GetAttributes(path));
            }
        }
        finally { foreach (var (path, snapshot) in before) File.SetAttributes(path, snapshot.Attributes); }
    }

    [Theory]
    [InlineData("yj1", "YJ1 compression is not supported; expected YJ2.")]
    [InlineData("truncated-yj2", "Truncated YJ2 bitstream.")]
    [InlineData("bad-table", "Sprite offset table length is invalid.")]
    [InlineData("nominal-rle", "Sprite RLE run makes no progress or exceeds the image.")]
    [InlineData("decoded-budget", "YJ2 declared length exceeds the output limit.")]
    [InlineData("pixel-budget", "Sprite dimensions exceed the pixel limit or are empty.")]
    public async Task Export_fire_rejects_damaged_or_over_budget_chunks_without_publishing_a_manifest(string kind, string message)
    {
        byte[] nominal = [3, 0, 7, 0, 0, 0,
            4, 0, 1, 0, 1, 7, 0x83, 0,
            1, 0, 1, 0, 0x82, 0];
        byte[] damaged = kind switch
        {
            "yj1" => "YJ_1"u8.ToArray(),
            "truncated-yj2" => [1, 0, 0, 0],
            "bad-table" => Compress([4, 0, 8, 0]),
            "nominal-rle" => Compress(nominal),
            "decoded-budget" => [1, 0, 128, 0], // 8 MiB + 1, before allocation.
            _ => Compress([2, 0, 0, 0, 255, 255, 255, 255]), // 65535x65535 before RLE/allocation.
        };
        WriteArchive("Fire.mkf", [], Compress(SingleFrameSprite), damaged);
        var before = Directory.GetFiles(input).ToDictionary(path => path, Hash);
        var result = await ExportFire();
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Equal($"Battle image export failed: fire.mkf: chunk 2: {message}{Environment.NewLine}", result.Stderr);
        Assert.DoesNotContain(directory, result.Stderr);
        Assert.DoesNotContain("   at ", result.Stderr);
        Assert.False(Directory.Exists(output)); // A valid earlier chunk must not be published either.
        foreach (var (path, hash) in before) Assert.Equal(hash, Hash(path));
    }

    [Theory]
    [InlineData("Fire.mkf")]
    [InlineData("Pat.mkf")]
    public async Task Export_fire_requires_its_own_archive_and_palette(string name)
    {
        WriteArchive("Fire.mkf", Compress(SingleFrameSprite));
        // Other sprite archives cannot substitute for a missing FIRE.
        WriteArchive("Abc.mkf", Compress(SingleFrameSprite));
        WriteArchive("F.mkf", Compress(SingleFrameSprite));
        File.Delete(Path.Combine(input, name));
        var result = await ExportFire();
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Equal($"Battle image export failed: {name.ToLowerInvariant()}: required archive is missing.{Environment.NewLine}", result.Stderr);
        Assert.False(Directory.Exists(output));
    }

    [Theory]
    [InlineData("input", "Output must not be inside the input directory.")]
    [InlineData("nested-input", "Output must not be inside the input directory.")]
    [InlineData("nonempty", "Output directory is not empty; existing data will not be overwritten.")]
    [InlineData("file", "Output must be a new or empty directory, not a file.")]
    [InlineData("output-link", "Output: links/reparse points in paths are not supported for safe export.")]
    [InlineData("output-linked-ancestor", "Output: links/reparse points in paths are not supported for safe export.")]
    [InlineData("input-link", "Input: links/reparse points in paths are not supported for safe export.")]
    public async Task Export_fire_refuses_unsafe_paths_without_mutating_existing_content(string kind, string message)
    {
        WriteArchive("Fire.mkf", [], Compress(SingleFrameSprite));
        string sentinel = Path.Combine(directory, "keep.txt");
        File.WriteAllText(sentinel, "unchanged");
        if (kind == "nonempty") { Directory.CreateDirectory(output); File.Copy(sentinel, Path.Combine(output, "keep.txt")); }
        string link = Path.Combine(directory, "link");
        bool linked = kind.Contains("link");
        if (linked) DirectoryLinkFixture.Create(link, input);
        var before = Directory.GetFiles(input).ToDictionary(path => path,
            path => (Hash: Hash(path), Modified: File.GetLastWriteTimeUtc(path), Attributes: File.GetAttributes(path)));
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
            var result = await ExportFire(destination, source: kind == "input-link" ? link : null);
            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Stdout);
            Assert.Equal($"Battle image export failed: {message}{Environment.NewLine}", result.Stderr);
            Assert.DoesNotContain(directory, result.Stderr);
            Assert.DoesNotContain("   at ", result.Stderr);
            Assert.Equal("unchanged", File.ReadAllText(sentinel));
            Assert.Equal(before.Keys.Order(), Directory.GetFiles(input).Order());
            foreach (var (path, snapshot) in before)
            {
                Assert.Equal(snapshot.Hash, Hash(path));
                Assert.Equal(snapshot.Modified, File.GetLastWriteTimeUtc(path));
                Assert.Equal(snapshot.Attributes, File.GetAttributes(path));
            }
            Assert.False(Directory.Exists(Path.Combine(input, "new")));
            Assert.False(File.Exists(Path.Combine(destination, "index.json")));
            if (kind == "nonempty")
            {
                Assert.Equal(new[] { "keep.txt" }, Directory.GetFiles(output).Select(Path.GetFileName));
                Assert.Equal("unchanged", File.ReadAllText(Path.Combine(output, "keep.txt")));
            }
            else if (kind is not ("input" or "file" or "output-link")) Assert.False(Directory.Exists(destination));
        }
        finally { if (linked) Directory.Delete(link); }
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

    [Fact]
    public async Task Export_battle_images_decodes_fbp_chunks_as_fullscreen_screens()
    {
        WriteArchive("Fbp.mkf", CompressScreen(0), [], CompressScreen(160));
        var result = await ExportFbp();
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Empty(result.Stderr);
        string[] files = [.. Directory.GetFiles(output, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(output, path).Replace('\\', '/')).Order()];
        Assert.Equal(new[]
        {
            "index.json",
            "sprites/0.json", "sprites/0/0.png",
            "sprites/2.json", "sprites/2/0.png",
        }, files);
        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "index.json")));
        var root = index.RootElement;
        Assert.Equal("fbp", root.GetProperty("parameters").GetProperty("archive").GetString());
        var sources = root.GetProperty("sources").EnumerateArray().ToArray();
        Assert.Equal(2, sources.Length);
        Assert.Equal("fbp.mkf", sources[0].GetProperty("name").GetString());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(input, "Fbp.mkf")))),
            sources[0].GetProperty("sha256").GetString());
        var chunks = root.GetProperty("chunks").EnumerateArray().ToArray();
        Assert.Equal(3, chunks.Length);
        // fbp chunks are YJ2 screens (PAL_MKFDecompressChunk, fixed 320x200 buffer).
        Assert.Equal("yj2", chunks[0].GetProperty("compression").GetString());
        Assert.Equal(64000, chunks[0].GetProperty("decodedByteLength").GetInt32());
        Assert.Equal(1, chunks[0].GetProperty("frameCount").GetInt32());
        Assert.Equal(1, chunks[0].GetProperty("decodedFrameCount").GetInt32());
        Assert.Equal("sprites/0.json", chunks[0].GetProperty("sprite").GetString());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(ChunkBytes(input, "Fbp.mkf", 0))),
            chunks[0].GetProperty("sha256").GetString());
        // Real fbp.mkf carries empty chunks 5 and 58; they stay bare entries.
        Assert.Equal("empty", chunks[1].GetProperty("compression").GetString());
        Assert.Throws<KeyNotFoundException>(() => chunks[1].GetProperty("frameCount"));
        Assert.Equal("yj2", chunks[2].GetProperty("compression").GetString());
        Assert.Equal(64000, chunks[2].GetProperty("decodedByteLength").GetInt32());
        var totals = root.GetProperty("totals");
        Assert.Equal(3, totals.GetProperty("chunks").GetInt32());
        Assert.Equal(2, totals.GetProperty("decodedChunks").GetInt32());
        Assert.Equal(1, totals.GetProperty("emptyChunks").GetInt32());
        Assert.Equal(2, totals.GetProperty("frames").GetInt32());
        Assert.Equal(4, totals.GetProperty("files").GetInt32());
        using var sprite = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "sprites", "0.json")));
        var document = sprite.RootElement;
        Assert.Equal("fbp.mkf", document.GetProperty("source").GetProperty("archive").GetString());
        Assert.Equal("Windows YJ2", document.GetProperty("source").GetProperty("encoding").GetString());
        Assert.Equal("single raw 320x200 indexed bitmap, linear row-major (no RLE, no offset table)",
            document.GetProperty("source").GetProperty("frameLayout").GetString());
        var frame = Assert.Single(document.GetProperty("frames").EnumerateArray());
        Assert.Equal(0, frame.GetProperty("index").GetInt32());
        Assert.Equal(320, frame.GetProperty("width").GetInt32());
        Assert.Equal(200, frame.GetProperty("height").GetInt32());
        Assert.Equal(0, frame.GetProperty("sourceOffset").GetInt32());
        Assert.Equal(ExpectedScreen(0), frame.GetProperty("indices").GetBytesFromBase64());
        Assert.All(frame.GetProperty("alpha").GetBytesFromBase64(), a => Assert.Equal(255, a));
        Assert.Equal("sprites/0/0.png", frame.GetProperty("png").GetString());
        ValidatePng(Path.Combine(output, "sprites", "0", "0.png"), 320, 200);
        using var second = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "sprites", "2.json")));
        var secondFrame = Assert.Single(second.RootElement.GetProperty("frames").EnumerateArray());
        Assert.Equal(ExpectedScreen(160), secondFrame.GetProperty("indices").GetBytesFromBase64());
        ValidatePng(Path.Combine(output, "sprites", "2", "0.png"), 320, 200);
        Assert.DoesNotContain(directory, File.ReadAllText(Path.Combine(output, "index.json")));
    }

    [Theory]
    [InlineData("yj1")]
    [InlineData("truncated-yj2")]
    [InlineData("not-a-screen")]
    public async Task Export_battle_images_rejects_damaged_fbp_chunks_before_creating_output(string kind)
    {
        byte[] damaged = kind switch
        {
            "yj1" => "YJ_1"u8.ToArray(),
            "truncated-yj2" => [1, 0, 0, 0],
            _ => Compress(new byte[8]),
        };
        WriteArchive("Fbp.mkf", damaged);
        var result = await ExportFbp();
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("fbp.mkf", result.Stderr);
        Assert.Contains(kind == "yj1" ? "YJ1" : kind == "truncated-yj2" ? "YJ2" : "320x200", result.Stderr);
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
    [InlineData("uppercase-fire")]
    [InlineData("duplicate-archive")]
    [InlineData("empty-output")]
    [InlineData("option-value")]
    [InlineData("missing-output")]
    public async Task Export_battle_images_requires_explicit_valid_options_without_echoing_local_paths(string kind)
    {
        WriteArchive("Abc.mkf", [], Compress(SingleFrameSprite));
        string[] args = kind switch
        {
            "missing" => ["export-battle-images", "--input", input, "--output", output],
            "unknown-archive" => ["export-battle-images", "--input", input, "--archive", "unknown-archive", "--output", output],
            "wrong-order" => ["export-battle-images", "--archive", "abc", "--input", input, "--output", output],
            "empty-input" => ["export-battle-images", "--input", "", "--archive", "abc", "--output", output],
            "uppercase-fire" => ["export-battle-images", "--input", input, "--archive", "FIRE", "--output", output],
            "duplicate-archive" => ["export-battle-images", "--input", input, "--archive", "fire", "--archive", "fire", "--output", output],
            "empty-output" => ["export-battle-images", "--input", input, "--archive", "fire", "--output", ""],
            "option-value" => ["export-battle-images", "--input", input, "--archive", "fire", "--output", "--invalid"],
            "missing-output" => ["export-battle-images", "--input", input, "--archive", "fire"],
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
        Assert.Contains("export-battle-images --input <directory> --archive <abc|ball|f|fbp|fire> --output <new-or-empty-directory>", result.Stdout);
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

    private Task<(int ExitCode, string Stdout, string Stderr)> ExportFbp(string? destination = null) =>
        CliProcess.Run(directory, "export-battle-images", "--input", input, "--archive", "fbp",
            "--output", destination ?? output);

    private Task<(int ExitCode, string Stdout, string Stderr)> ExportFire(string? destination = null, string? source = null) =>
        CliProcess.Run(directory, "export-battle-images", "--input", source ?? input, "--archive", "fire",
            "--output", destination ?? output);

    private static string[] PackageFiles(string root) => [.. Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')).Order()];

    private void AssertFireFrame(JsonElement sprite, int chunk, int frameIndex, int offset, int width, byte[] indices, byte[] alpha)
    {
        var frame = sprite.GetProperty("frames")[frameIndex];
        Assert.Equal(frameIndex, frame.GetProperty("index").GetInt32());
        Assert.Equal(offset, frame.GetProperty("sourceOffset").GetInt32());
        Assert.Equal(width, frame.GetProperty("width").GetInt32());
        Assert.Equal(1, frame.GetProperty("height").GetInt32());
        Assert.Equal(indices, frame.GetProperty("indices").GetBytesFromBase64());
        Assert.Equal(alpha, frame.GetProperty("alpha").GetBytesFromBase64());
        Assert.Equal($"sprites/{chunk}/{frameIndex}.png", frame.GetProperty("png").GetString());
        ValidatePng(Path.Combine(output, "sprites", $"{chunk}", $"{frameIndex}.png"), width, 1);
    }

    // Read the PNG's filtered scanline bytes with BCL zlib, as PngWriterTests does;
    // the expected RGBA bytes above are literal, not produced by image/palette code.
    private static byte[] PngScanlines(string path)
    {
        using var stream = File.OpenRead(path);
        stream.Position = 8;
        using var compressed = new MemoryStream();
        using var reader = new BinaryReader(stream);
        while (stream.Position < stream.Length)
        {
            int length = BinaryPrimitives.ReverseEndianness(reader.ReadInt32());
            byte[] type = reader.ReadBytes(4);
            byte[] payload = reader.ReadBytes(length);
            Assert.Equal(length, payload.Length);
            _ = reader.ReadUInt32(); // CRC is independently covered by PngWriterTests.
            if (type.AsSpan().SequenceEqual("IDAT"u8)) compressed.Write(payload);
        }
        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var scanlines = new MemoryStream();
        zlib.CopyTo(scanlines);
        return scanlines.ToArray();
    }

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
