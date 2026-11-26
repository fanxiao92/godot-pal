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

// Literal native roundtrip and checked-load contracts for the #147 FIRE preparation.
// Every expectation comes from the artificial package bytes (independently written PNGs
// and literal planes), never from the published resources or the production loader.
// The chunk numbers stay sparse FIRE magic-effect identities; chunk 9 stays empty and
// publishes nothing. Process argv/marker/exit contracts live in Pal.Prepare.Tests.
[TestSuite, RequireGodotRuntime]
[TestCategory("Prepare")]
public class PrepareFireEffectsTests
{
    private const string FrameLayout = "PAL sprite WORD offset table + RLE frames";

    [TestCase]
    public void PreparePublishesNativeFireSpritesThroughCompatibilityAndCheckedLoads()
    {
        string input = Path.Combine(Path.GetTempPath(), "pal-fire-native-" + Guid.NewGuid().ToString("N"));
        string root = "res://Assets/RuntimeTests/fire-effects-" + Guid.NewGuid().ToString("N");
        string output = ProjectSettings.GlobalizePath(root);
        try
        {
            Directory.CreateDirectory(input);
            WritePackage(input);
            string[] before = Snapshot(input);
            var mapping = PrepareFireEffects.Prepare(input, output);

            AssertThat(mapping.Keys.Order().SequenceEqual(new[] { 3, 7 })).IsTrue();
            AssertThat(mapping[3]).IsEqual(root + "/Sprites/3.tres");
            AssertThat(mapping[7]).IsEqual(root + "/Sprites/7.tres");
            AssertThat(Directory.GetFiles(output, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(output, path).Replace('\\', '/')).Order()
                .SequenceEqual(new[] { "Sprites/3.tres", "Sprites/7.tres", "Textures/3/0.tres", "Textures/3/1.tres", "Textures/3/2.tres", "Textures/7/0.tres" })).IsTrue();

            // Compatibility production load: literal field identity, alias frame kept.
            var sprite = NativeBattleVisualProfile.LoadSprite(mapping[3]);
            AssertThat(sprite.SpriteId).IsEqual(3);
            AssertThat(sprite.DecodedByteLength).IsEqual(26);
            AssertThat(sprite.FrameCount).IsEqual(2);
            AssertThat(sprite.Frames.Select(frame => frame.Index).Order().SequenceEqual(new[] { 0, 1, 2 })).IsTrue();
            foreach (var (index, offset, width, height, indices, alpha) in new[]
            {
                (0, 6, 4, 1, new byte[] { 7, 0, 0, 0 }, new byte[] { 255, 0, 0, 0 }),
                (1, 14, 1, 1, new byte[] { 9 }, new byte[] { 255 }),
                (2, 20, 1, 1, new byte[] { 4 }, new byte[] { 255 }),
            })
            {
                var frame = sprite.Frames.Single(candidate => candidate.Index == index);
                AssertThat(frame.SourceOffset).IsEqual(offset);
                AssertThat(frame.Width).IsEqual(width);
                AssertThat(frame.Height).IsEqual(height);
                AssertThat(frame.Indices.SequenceEqual(indices)).IsTrue();
                AssertThat(frame.Alpha.SequenceEqual(alpha)).IsTrue();
                var texture = frame.Texture;
                AssertThat(texture).IsNotNull();
                AssertThat(texture.ResourcePath).IsEqual(root + "/Textures/3/" + index + ".tres");
                AssertThat(texture.GetClass()).IsEqual("ImageTexture");
                AssertThat(texture.GetWidth()).IsEqual(width);
                AssertThat(texture.GetHeight()).IsEqual(height);
            }
            AssertThat(sprite.Source.Count).IsEqual(4);
            AssertThat(sprite.Source["archive"].VariantType).IsEqual(Variant.Type.String);
            AssertThat(sprite.Source["archive"].AsString()).IsEqual("fire.mkf");
            AssertThat(sprite.Source["chunk"].VariantType).IsEqual(Variant.Type.Int);
            AssertThat(sprite.Source["chunk"].AsInt32()).IsEqual(3);
            AssertThat(sprite.Source["encoding"].AsString()).IsEqual("Windows YJ2");
            AssertThat(sprite.Source["frameLayout"].AsString()).IsEqual(FrameLayout);
            // The published texture carries the package PNG's literal colorized pixels.
            using (var pixels = sprite.Frames.Single(frame => frame.Index == 0).Texture.GetImage())
            {
                var pixel = pixels.GetPixel(0, 0);
                AssertThat((pixel.R8, pixel.G8, pixel.B8, pixel.A8)).IsEqual((160, 80, 40, 255));
            }
            var small = NativeBattleVisualProfile.LoadSprite(mapping[7]);
            {
                AssertThat(small.SpriteId).IsEqual(7);
                AssertThat(small.DecodedByteLength).IsEqual(12);
                AssertThat(small.FrameCount).IsEqual(1);
                var frame = small.Frames.Single(candidate => candidate.Index == 0);
                AssertThat(frame.SourceOffset).IsEqual(4);
                AssertThat(frame.Indices.SequenceEqual(new byte[] { 11, 0 })).IsTrue();
                AssertThat(frame.Alpha.SequenceEqual(new byte[] { 255, 0 })).IsTrue();
                using var image = frame.Texture.GetImage();
                var corner = image.GetPixel(0, 0);
                AssertThat((corner.R8, corner.G8, corner.B8, corner.A8)).IsEqual((163, 80, 43, 255));
            }
            // Checked production load with an expectation built from the package declaration.
            var expected = new PalSprite
            {
                SpriteId = 3, DecodedByteLength = 26, FrameCount = 2,
                Source = new Godot.Collections.Dictionary
                {
                    ["archive"] = "fire.mkf", ["chunk"] = 3, ["encoding"] = "Windows YJ2", ["frameLayout"] = FrameLayout,
                },
            };
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string uri in new[] { mapping[3], root + "/Textures/3/0.tres", root + "/Textures/3/1.tres", root + "/Textures/3/2.tres" })
                hashes[uri] = Godot.FileAccess.GetSha256(uri);
            foreach (var (index, offset, width, height, indices, alpha, color) in new[]
            {
                (0, 6, 4, 1, new byte[] { 7, 0, 0, 0 }, new byte[] { 255, 0, 0, 0 }, (160, 80, 40, 255)),
                (1, 14, 1, 1, new byte[] { 9 }, new byte[] { 255 }, (161, 80, 41, 255)),
                (2, 20, 1, 1, new byte[] { 4 }, new byte[] { 255 }, (162, 80, 42, 255)),
            })
                expected.Frames.Add(new PalFrame
                {
                    Index = index, SourceOffset = offset, Width = width, Height = height, Indices = indices, Alpha = alpha,
                    Texture = ExpectedTexture(root + "/Textures/3/" + index + ".tres", width, height, color),
                });
            AssertThat(NativeBattleVisualProfile.LoadSprite(mapping[3], expected, hashes).Frames.Count).IsEqual(3);
            // The generated resource participates in the production checked chain: a wrong
            // expectation is rejected with the actual URI and field context.
            expected.SpriteId = 99;
            string rejected = Failure(() => NativeBattleVisualProfile.LoadSprite(mapping[3], expected, hashes));
            AssertThat(rejected.Contains("SpriteId")).IsTrue();
            AssertThat(rejected.Contains(mapping[3])).IsTrue();

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
    [TestCase("frame-png-missing")]
    [TestCase("png-undecodable")]
    [TestCase("png-dimension-mismatch")]
    [TestCase("indices-length-mismatch")]
    public void PrepareRejectsFaultyPackagesBeforePublishing(string fault)
    {
        string input = Path.Combine(Path.GetTempPath(), "pal-fire-fault-" + Guid.NewGuid().ToString("N"));
        string root = "res://Assets/RuntimeTests/fire-faults-" + Guid.NewGuid().ToString("N");
        string output = ProjectSettings.GlobalizePath(root);
        string outside = Path.Combine(Path.GetTempPath(), "pal-fire-outside-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(input);
            WritePackage(input);
            string expected = ApplyFault(input, fault, output);
            string[] before = Snapshot(input);
            string failure = Failure(() => PrepareFireEffects.Prepare(input, fault == "output-outside-assets" ? outside : output));
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
                return "unexpected file in fire package: sprites/extra.txt";
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
                Mutate(Path.Combine(input, "sprites", "3.json"), node => node["chunk"] = 8);
                return "must match the index chunk";
            case "source-archive-mismatch":
                Mutate(Path.Combine(input, "sprites", "3.json"), node =>
                {
                    var source = node["source"] ?? throw new InvalidOperationException("fixture source missing");
                    source["archive"] = "f.mkf";
                });
                return "source.archive";
            case "frame-count-mismatch":
                Mutate(Path.Combine(input, "sprites", "3.json"), node => node["frameCount"] = 3);
                return "frameCount";
            case "frame-png-missing":
                File.Delete(Path.Combine(input, "sprites", "3", "2.png"));
                return "sprites/3/2.png";
            case "png-undecodable":
                File.WriteAllBytes(Path.Combine(input, "sprites", "7", "0.png"),
                    [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 1, 2, 3]);
                return "is not a decodable PNG";
            case "png-dimension-mismatch":
                File.WriteAllBytes(Path.Combine(input, "sprites", "3", "0.png"), Png(1, 1, (x, y) => (9, 9, 9, 255)));
                return "dimensions must match";
            default:
                Mutate(Path.Combine(input, "sprites", "3.json"), node =>
                {
                    var frames = node["frames"]?.AsArray() ?? throw new InvalidOperationException("fixture frames missing");
                    var frame = frames[0] ?? throw new InvalidOperationException("fixture frame missing");
                    frame["indices"] = Convert.ToBase64String(new byte[] { 7, 0, 0 });
                });
                return "indices length must match";
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

    // The artificial fire package mirrors T18's literal fixture shapes: chunk 3 is a
    // two-nominal-frame YJ2 sprite whose final table word decodes as an extra alias
    // frame, chunk 7 is a single-frame sprite, chunk 9 stays empty.
    private static void WritePackage(string input)
    {
        Directory.CreateDirectory(Path.Combine(input, "sprites", "3"));
        Directory.CreateDirectory(Path.Combine(input, "sprites", "7"));
        File.WriteAllBytes(Path.Combine(input, "sprites", "3", "0.png"), Png(4, 1, (x, y) => (160, 80, 40, 255)));
        File.WriteAllBytes(Path.Combine(input, "sprites", "3", "1.png"), Png(1, 1, (x, y) => (161, 80, 41, 255)));
        File.WriteAllBytes(Path.Combine(input, "sprites", "3", "2.png"), Png(1, 1, (x, y) => (162, 80, 42, 255)));
        File.WriteAllBytes(Path.Combine(input, "sprites", "7", "0.png"), Png(2, 1, (x, y) => (163, 80, 43, 255)));
        File.WriteAllText(Path.Combine(input, "sprites", "3.json"), JsonSerializerText(new
        {
            formatVersion = 1,
            profile = "faithful",
            chunk = 3,
            source = new { archive = "fire.mkf", chunk = 3, encoding = "Windows YJ2", frameLayout = FrameLayout },
            decodedByteLength = 26,
            frameCount = 2,
            frames = new object[]
            {
                new { index = 0, sourceOffset = 6, width = 4, height = 1, png = "sprites/3/0.png", indices = B64(7, 0, 0, 0), alpha = B64(255, 0, 0, 0) },
                new { index = 1, sourceOffset = 14, width = 1, height = 1, png = "sprites/3/1.png", indices = B64(9), alpha = B64(255) },
                new { index = 2, sourceOffset = 20, width = 1, height = 1, png = "sprites/3/2.png", indices = B64(4), alpha = B64(255) },
            }
        }));
        File.WriteAllText(Path.Combine(input, "sprites", "7.json"), JsonSerializerText(new
        {
            formatVersion = 1,
            profile = "faithful",
            chunk = 7,
            source = new { archive = "fire.mkf", chunk = 7, encoding = "Windows YJ2", frameLayout = FrameLayout },
            decodedByteLength = 12,
            frameCount = 1,
            frames = new object[]
            {
                new { index = 0, sourceOffset = 4, width = 2, height = 1, png = "sprites/7/0.png", indices = B64(11, 0), alpha = B64(255, 0) },
            }
        }));
        File.WriteAllText(Path.Combine(input, "index.json"), JsonSerializerText(new
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
                new { index = 9, byteLength = 0, sha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", compression = "empty" },
            },
            totals = new { chunks = 3, decodedChunks = 2, emptyChunks = 1, frames = 4, files = 6 },
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
