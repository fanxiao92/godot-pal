using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using GdUnit4;
using Godot;
using Pal.Game.Editor;
using Pal.Game.Profiles;
using Pal.Game.Resources;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

// Literal native roundtrip and checked-load contracts for the #218 BALL item-bitmap
// preparation. Every expectation comes from the artificial package bytes (independently
// written PNGs and literal planes), never from the published resources or the production
// loader. The chunk numbers stay BALL item-bitmap identities (item.wBitmap); chunk 0 stays
// empty and publishes nothing. Process argv/marker/exit contracts live in Pal.Prepare.Tests.
[TestSuite, RequireGodotRuntime]
[TestCategory("Prepare")]
public class PrepareItemBitmapsTests
{
    private const string BallEncoding = "uncompressed PAL RLE bitmap";
    private const string FrameLayout = "single RLE bitmap: optional 0x00000002 header, WORD width/height, RLE runs";

    [TestCase]
    public void PreparePublishesNativeItemBitmapsThroughCompatibilityAndCheckedLoads()
    {
        string input = Path.Combine(Path.GetTempPath(), "pal-ball-native-" + Guid.NewGuid().ToString("N"));
        string root = "res://Assets/RuntimeTests/item-bitmaps-" + Guid.NewGuid().ToString("N");
        string output = ProjectSettings.GlobalizePath(root);
        try
        {
            Directory.CreateDirectory(input);
            WritePackage(input);
            string[] before = Snapshot(input);
            var mapping = PrepareItemBitmaps.Prepare(input, output);

            AssertThat(mapping.Keys.Order().SequenceEqual(new[] { 37, 41 })).IsTrue();
            AssertThat(mapping[37]).IsEqual(root + "/Sprites/37.tres");
            AssertThat(mapping[41]).IsEqual(root + "/Sprites/41.tres");
            AssertThat(Directory.GetFiles(output, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(output, path).Replace('\\', '/')).Order()
                .SequenceEqual(new[] { "Sprites/37.tres", "Sprites/41.tres", "Textures/37/0.tres", "Textures/41/0.tres" })).IsTrue();

            // Compatibility production load: literal field identity; one frame per chunk.
            var sprite = NativeBattleVisualProfile.LoadSprite(mapping[37]);
            AssertThat(sprite.SpriteId).IsEqual(37);
            AssertThat(sprite.DecodedByteLength).IsEqual(16);
            AssertThat(sprite.FrameCount).IsEqual(1);
            AssertThat(sprite.Frames.Select(frame => frame.Index).Order().SequenceEqual(new[] { 0 })).IsTrue();
            var frame = sprite.Frames.Single(candidate => candidate.Index == 0);
            AssertThat(frame.SourceOffset).IsEqual(6);
            AssertThat(frame.Width).IsEqual(4);
            AssertThat(frame.Height).IsEqual(3);
            AssertThat(frame.Indices.SequenceEqual(new byte[] { 37, 0, 41, 0, 42, 0, 43, 0, 44, 0, 45, 0 })).IsTrue();
            AssertThat(frame.Alpha.SequenceEqual(new byte[] { 255, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255, 0 })).IsTrue();
            var texture = frame.Texture;
            AssertThat(texture).IsNotNull();
            AssertThat(texture.ResourcePath).IsEqual(root + "/Textures/37/0.tres");
            AssertThat(texture.GetClass()).IsEqual("ImageTexture");
            AssertThat(texture.GetWidth()).IsEqual(4);
            AssertThat(texture.GetHeight()).IsEqual(3);
            AssertThat(sprite.Source.Count).IsEqual(4);
            AssertThat(sprite.Source["archive"].VariantType).IsEqual(Variant.Type.String);
            AssertThat(sprite.Source["archive"].AsString()).IsEqual("ball.mkf");
            AssertThat(sprite.Source["chunk"].VariantType).IsEqual(Variant.Type.Int);
            AssertThat(sprite.Source["chunk"].AsInt32()).IsEqual(37);
            AssertThat(sprite.Source["encoding"].AsString()).IsEqual(BallEncoding);
            AssertThat(sprite.Source["frameLayout"].AsString()).IsEqual(FrameLayout);
            // The published texture carries the package PNG's literal colorized pixels.
            using (var pixels = frame.Texture.GetImage())
            {
                var pixel = pixels.GetPixel(0, 0);
                AssertThat((pixel.R8, pixel.G8, pixel.B8, pixel.A8)).IsEqual((150, 90, 60, 255));
            }
            var small = NativeBattleVisualProfile.LoadSprite(mapping[41]);
            {
                AssertThat(small.SpriteId).IsEqual(41);
                AssertThat(small.DecodedByteLength).IsEqual(10);
                AssertThat(small.FrameCount).IsEqual(1);
                var smallFrame = small.Frames.Single(candidate => candidate.Index == 0);
                AssertThat(smallFrame.SourceOffset).IsEqual(6);
                AssertThat(smallFrame.Indices.SequenceEqual(new byte[] { 41, 42, 43, 44 })).IsTrue();
                AssertThat(smallFrame.Alpha.SequenceEqual(new byte[] { 255, 255, 0, 0 })).IsTrue();
            }
            // Checked production load with an expectation built from the package declaration.
            var expected = new PalSprite
            {
                SpriteId = 37, DecodedByteLength = 16, FrameCount = 1,
                Source = new Godot.Collections.Dictionary
                {
                    ["archive"] = "ball.mkf", ["chunk"] = 37, ["encoding"] = BallEncoding, ["frameLayout"] = FrameLayout,
                },
            };
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string uri in new[] { mapping[37], root + "/Textures/37/0.tres" })
                hashes[uri] = Godot.FileAccess.GetSha256(uri);
            expected.Frames.Add(new PalFrame
            {
                Index = 0, SourceOffset = 6, Width = 4, Height = 3,
                Indices = [37, 0, 41, 0, 42, 0, 43, 0, 44, 0, 45, 0],
                Alpha = [255, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255, 0],
                Texture = ExpectedTexture(root + "/Textures/37/0.tres", 4, 3, (150, 90, 60, 255)),
            });
            AssertThat(NativeBattleVisualProfile.LoadSprite(mapping[37], expected, hashes).Frames.Count).IsEqual(1);
            // The generated resource participates in the production checked chain: a wrong
            // expectation is rejected with the actual URI and field context.
            expected.SpriteId = 99;
            string rejected = Failure(() => NativeBattleVisualProfile.LoadSprite(mapping[37], expected, hashes));
            AssertThat(rejected.Contains("SpriteId")).IsTrue();
            AssertThat(rejected.Contains(mapping[37])).IsTrue();

            AssertThat(Snapshot(input).SequenceEqual(before)).IsTrue();
        }
        finally
        {
            if (Directory.Exists(input)) Directory.Delete(input, true);
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }
    }

    [TestCase("non-empty-output")]
    [TestCase("output-outside-assets")]
    [TestCase("missing-index")]
    [TestCase("unknown-extra-file")]
    [TestCase("missing-palette-archive")]
    [TestCase("totals-frames-mismatch")]
    [TestCase("chunk-sprite-mismatch")]
    [TestCase("source-archive-mismatch")]
    [TestCase("frame-count-mismatch")]
    [TestCase("compression-mismatch")]
    [TestCase("frame-png-missing")]
    [TestCase("png-dimension-mismatch")]
    public void PrepareRejectsFaultyBallPackagesBeforePublishing(string fault)
    {
        string input = Path.Combine(Path.GetTempPath(), "pal-ball-fault-" + Guid.NewGuid().ToString("N"));
        string root = "res://Assets/RuntimeTests/item-bitmap-faults-" + Guid.NewGuid().ToString("N");
        string output = ProjectSettings.GlobalizePath(root);
        string outside = Path.Combine(Path.GetTempPath(), "pal-ball-outside-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(input);
            WritePackage(input);
            string expected = ApplyFault(input, fault, output);
            string[] before = Snapshot(input);
            string failure = Failure(() => PrepareItemBitmaps.Prepare(input, fault == "output-outside-assets" ? outside : output));
            AssertThat(failure.Contains(expected, StringComparison.Ordinal)).IsTrue();
            if (fault == "non-empty-output") AssertThat(File.Exists(Path.Combine(output, "stray.txt"))).IsTrue();
            else if (fault == "output-outside-assets") AssertThat(Directory.Exists(outside)).IsFalse();
            else AssertThat(Directory.Exists(output)).IsFalse();
            AssertThat(Snapshot(input).SequenceEqual(before)).IsTrue();
        }
        finally
        {
            if (Directory.Exists(input)) Directory.Delete(input, true);
            if (Directory.Exists(output)) Directory.Delete(output, true);
            if (Directory.Exists(outside)) Directory.Delete(outside, true);
        }
    }

    private static string ApplyFault(string input, string fault, string output)
    {
        switch (fault)
        {
            case "non-empty-output":
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "stray.txt"), "occupied");
                return "new or empty";
            case "output-outside-assets":
                return "under project Assets";
            case "missing-index":
                File.Delete(Path.Combine(input, "index.json"));
                return "index.json";
            case "unknown-extra-file":
                File.WriteAllText(Path.Combine(input, "sprites", "extra.txt"), "stray");
                return "unexpected file in ball package: sprites/extra.txt";
            case "missing-palette-archive":
                Mutate(Path.Combine(input, "index.json"), node =>
                {
                    var sources = node["sources"]?.AsArray() ?? throw new InvalidOperationException("fixture sources missing");
                    node["sources"] = new JsonArray(sources[0]?.DeepClone() ?? throw new InvalidOperationException("fixture source missing"));
                });
                return "pat.mkf";
            case "totals-frames-mismatch":
                Mutate(Path.Combine(input, "index.json"), node =>
                {
                    var totals = node["totals"] ?? throw new InvalidOperationException("fixture totals missing");
                    totals["frames"] = 5;
                });
                return "totals.frames";
            case "chunk-sprite-mismatch":
                Mutate(Path.Combine(input, "sprites", "37.json"), node => node["chunk"] = 8);
                return "must match the index chunk";
            case "source-archive-mismatch":
                Mutate(Path.Combine(input, "sprites", "37.json"), node =>
                {
                    var source = node["source"] ?? throw new InvalidOperationException("fixture source missing");
                    source["archive"] = "f.mkf";
                });
                return "source.archive";
            case "frame-count-mismatch":
                Mutate(Path.Combine(input, "sprites", "37.json"), node => node["frameCount"] = 2);
                return "frameCount";
            case "compression-mismatch":
                Mutate(Path.Combine(input, "index.json"), node =>
                {
                    var chunks = node["chunks"]?.AsArray() ?? throw new InvalidOperationException("fixture chunks missing");
                    chunks[0]!["compression"] = "yj2";
                });
                return "compression";
            case "frame-png-missing":
                File.Delete(Path.Combine(input, "sprites", "37", "0.png"));
                return "sprites/37/0.png";
            default:
                File.WriteAllBytes(Path.Combine(input, "sprites", "37", "0.png"), Png(1, 1, (x, y) => (9, 9, 9, 255)));
                return "dimensions must match";
        }
    }

    // Checked expectations are authored from the package declaration (literal URI and
    // declared pixels), never by loading the published target resources.
    private static Texture2D ExpectedTexture(string uri, int width, int height, (int R, int G, int B, int A) color)
    {
        using var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        image.Fill(new Color(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f));
        var texture = ImageTexture.CreateFromImage(image);
        texture.TakeOverPath(uri);
        return texture;
    }

    private static void Mutate(string path, Action<JsonObject> mutate)
    {
        var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidOperationException("fixture JSON object missing: " + path);
        mutate(node);
        File.WriteAllText(path, node.ToJsonString());
    }

    // The artificial ball package mirrors the ball decoder's literal fixture shape: chunk 37
    // is a 4x3 single RLE bitmap, chunk 41 a 2x2 one, chunk 0 stays empty. Chunk ids keep the
    // item.wBitmap identity, never renumbered item indices.
    private static void WritePackage(string input)
    {
        Directory.CreateDirectory(Path.Combine(input, "sprites", "37"));
        Directory.CreateDirectory(Path.Combine(input, "sprites", "41"));
        File.WriteAllBytes(Path.Combine(input, "sprites", "37", "0.png"), Png(4, 3, (x, y) => ((byte)((150 + x) & 0xFF), (byte)90, (byte)((60 + y) & 0xFF), (byte)255)));
        File.WriteAllBytes(Path.Combine(input, "sprites", "41", "0.png"), Png(2, 2, (x, y) => ((byte)160, (byte)80, (byte)(40 + x), (byte)255)));
        File.WriteAllText(Path.Combine(input, "sprites", "37.json"), JsonSerializerText(new
        {
            formatVersion = 1,
            profile = "faithful",
            chunk = 37,
            source = new { archive = "ball.mkf", chunk = 37, encoding = BallEncoding, frameLayout = FrameLayout },
            decodedByteLength = 16,
            frameCount = 1,
            frames = new object[]
            {
                new { index = 0, sourceOffset = 6, width = 4, height = 3, png = "sprites/37/0.png",
                    indices = B64(37, 0, 41, 0, 42, 0, 43, 0, 44, 0, 45, 0),
                    alpha = B64(255, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255, 0) },
            }
        }));
        File.WriteAllText(Path.Combine(input, "sprites", "41.json"), JsonSerializerText(new
        {
            formatVersion = 1,
            profile = "faithful",
            chunk = 41,
            source = new { archive = "ball.mkf", chunk = 41, encoding = BallEncoding, frameLayout = FrameLayout },
            decodedByteLength = 10,
            frameCount = 1,
            frames = new object[]
            {
                new { index = 0, sourceOffset = 6, width = 2, height = 2, png = "sprites/41/0.png",
                    indices = B64(41, 42, 43, 44), alpha = B64(255, 255, 0, 0) },
            }
        }));
        File.WriteAllText(Path.Combine(input, "index.json"), JsonSerializerText(new
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
                new { index = 0, byteLength = 0, sha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", compression = "empty" },
                new { index = 37, byteLength = 16, sha256 = new string('3', 64), compression = "rle", decodedByteLength = 16, frameCount = 1, decodedFrameCount = 1, sprite = "sprites/37.json" },
                new { index = 41, byteLength = 10, sha256 = new string('4', 64), compression = "rle", decodedByteLength = 10, frameCount = 1, decodedFrameCount = 1, sprite = "sprites/41.json" },
            },
            totals = new { chunks = 3, decodedChunks = 2, emptyChunks = 1, frames = 2, files = 4 },
        }));
    }

    private static string JsonSerializerText(object value)
        => System.Text.Json.JsonSerializer.Serialize(value);

    private static string B64(params byte[] bytes) => Convert.ToBase64String(bytes);

    // Literal PNG writer (BCL zlib + CRC32): the package's expected RGBA bytes are
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

    private static string[] Snapshot(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
        .Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/') + ":" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();

    private static string Failure(Action action)
    {
        try { action(); }
        catch (InvalidDataException error) { return error.Message; }
        throw new InvalidOperationException("Expected InvalidDataException");
    }
}
