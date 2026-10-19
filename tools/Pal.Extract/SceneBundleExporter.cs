using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pal.Extract;

internal static class SceneBundleExporter
{
    private static readonly string[] RequiredNames = ["data.mkf", "gop.mkf", "map.mkf", "mgo.mkf", "pat.mkf", "sss.mkf"];

    internal static void Export(string input, string output, int sceneId, int paletteId, string lighting)
    {
        output = ExportPaths.Validate(input, output, RequiredNames);
        // Keep read-only archive handles alive throughout composition and hashing. On Windows
        // their sharing mode also prevents another writer from changing the consumed sources.
        var archives = MkfArchive.OpenRequired(input, RequiredNames, 65536);
        string staging = Path.Combine(Path.GetTempPath(), $"pal-bundle-{Guid.NewGuid():N}");
        try
        {
            string sceneOutput = Path.Combine(staging, "scene");
            string mapOutput = Path.Combine(staging, "map");
            SceneExporter.Export(input, sceneOutput, sceneId, paletteId, lighting);
            using var scene = ReadJson(sceneOutput, "scene.json");
            int mapId = scene.RootElement.GetProperty("mapId").GetInt32();
            MapExporter.Export(input, mapOutput, mapId, paletteId, lighting);
            if (!File.ReadAllBytes(Path.Combine(sceneOutput, "palette.json")).AsSpan()
                .SequenceEqual(File.ReadAllBytes(Path.Combine(mapOutput, "palette.json"))))
                throw new InvalidDataException("Bundle palette outputs do not match.");
            File.Delete(Path.Combine(mapOutput, "palette.json"));
            foreach (string path in Directory.GetFiles(mapOutput, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(sceneOutput, Path.GetRelativePath(mapOutput, path));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(path, target);
            }
            var spriteReferences = ValidateReferences(sceneOutput, scene.RootElement, mapId, paletteId, lighting);
            var artifacts = Directory.GetFiles(sceneOutput, "*", SearchOption.AllDirectories)
                .Select(path => new ArtifactIdentity(Path.GetRelativePath(sceneOutput, path).Replace('\\', '/'),
                    new FileInfo(path).Length, HashFile(path)))
                .OrderBy(artifact => artifact.Path, StringComparer.Ordinal).ToArray();
            string identity = string.Concat(artifacts.Select(artifact => FormattableString.Invariant(
                $"{artifact.Path}\t{artifact.ByteLength}\t{artifact.Sha256}\n")));
            var manifest = new
            {
                formatVersion = 1,
                command = "extract-scene",
                parameters = new { sceneId, mapId, paletteId, lighting },
                sources = archives.Select(archive => new { name = archive.Name, archive.ByteLength, archive.Sha256 }),
                resources = new { scene = "scene.json", map = "map.json", tiles = "tiles.json", palette = "palette.json", preview = "preview.png", sprites = spriteReferences },
                artifacts,
                bundleSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
            };
            File.WriteAllText(Path.Combine(sceneOutput, "manifest.json"), JsonSerializer.Serialize(manifest, MapExporter.JsonOptions) + "\n");
            // Detect source changes on platforms that do not enforce FileShare.Read against writers.
            var after = MkfArchive.OpenRequired(input, RequiredNames, 65536);
            try
            {
                if (!archives.Select(a => (a.Name, a.ByteLength, a.Sha256)).SequenceEqual(after.Select(a => (a.Name, a.ByteLength, a.Sha256))))
                    throw new InvalidDataException("Input archives changed during export.");
            }
            finally { foreach (var archive in after) archive.Dispose(); }
            ExportPaths.Validate(input, output, RequiredNames);
            Directory.CreateDirectory(output);
            foreach (var artifact in artifacts)
            {
                string target = Path.Combine(output, artifact.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Combine(sceneOutput, artifact.Path), target, overwrite: false);
            }
            // Publish the manifest last; an earlier copy failure must not advertise a complete bundle.
            File.Copy(Path.Combine(sceneOutput, "manifest.json"), Path.Combine(output, "manifest.json"), overwrite: false);
        }
        finally
        {
            foreach (var archive in archives) archive.Dispose();
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static SpriteReference[] ValidateReferences(string root, JsonElement scene, int mapId, int paletteId, string lighting)
    {
        using var map = ReadJson(root, "map.json");
        using var tiles = ReadJson(root, "tiles.json");
        using var palette = ReadJson(root, "palette.json");
        Require(map.RootElement.GetProperty("mapId").GetInt32() == mapId && tiles.RootElement.GetProperty("mapId").GetInt32() == mapId,
            "scene/map/tiles IDs do not match");
        Require(palette.RootElement.GetProperty("paletteId").GetInt32() == paletteId
            && palette.RootElement.GetProperty("lighting").GetString() == lighting
            && palette.RootElement.GetProperty("colors").GetArrayLength() == 256, "palette is incomplete");
        var tileFrames = ValidateFrames(root, tiles.RootElement, "tiles");
        foreach (var cell in map.RootElement.GetProperty("cells").EnumerateArray())
        {
            Require(tileFrames.Contains(cell.GetProperty("bottomFrame").GetInt32()), "bottom tile frame is missing");
            var top = cell.GetProperty("topFrame");
            Require(top.ValueKind == JsonValueKind.Null || tileFrames.Contains(top.GetInt32()), "top tile frame is missing");
        }
        var preview = map.RootElement.GetProperty("preview");
        Require(preview.GetProperty("png").GetString() == "preview.png", "preview reference does not match");
        ValidatePng(root, "preview.png", preview.GetProperty("width").GetInt32(), preview.GetProperty("height").GetInt32());
        var references = scene.GetProperty("spriteIds").EnumerateArray()
            .Select(id => new SpriteReference(id.GetInt32(), $"sprites/{id.GetInt32()}.json")).ToArray();
        var spriteFrames = new Dictionary<int, HashSet<int>>();
        foreach (var reference in references)
        {
            using var sprite = ReadJson(root, reference.Path);
            Require(sprite.RootElement.GetProperty("spriteId").GetInt32() == reference.Id, "sprite ID does not match");
            var frames = ValidateFrames(root, sprite.RootElement, $"sprites/{reference.Id}");
            int count = sprite.RootElement.GetProperty("frameCount").GetInt32();
            Require(count > 0 && Enumerable.Range(0, count).All(frames.Contains), "nominal sprite frames are missing");
            spriteFrames.Add(reference.Id, frames);
        }
        foreach (var record in scene.GetProperty("objects").EnumerateArray().Concat(scene.GetProperty("defaultParty").EnumerateArray()))
        {
            int id = record.GetProperty("wSpriteNum").GetInt32();
            var path = record.GetProperty("sprite");
            Require(id == 0 ? path.ValueKind == JsonValueKind.Null
                : path.GetString() == $"sprites/{id}.json" && spriteFrames.ContainsKey(id), "scene sprite reference is missing");
            if (record.TryGetProperty("initialFrameIndex", out var frame) && id != 0)
                Require(spriteFrames[id].Contains(frame.GetInt32()), "selected object frame is missing");
        }
        return references;
    }

    private static HashSet<int> ValidateFrames(string root, JsonElement resource, string prefix)
    {
        var indices = new HashSet<int>();
        foreach (var frame in resource.GetProperty("frames").EnumerateArray())
        {
            int index = frame.GetProperty("index").GetInt32();
            int width = frame.GetProperty("width").GetInt32(), height = frame.GetProperty("height").GetInt32();
            Require(index >= 0 && indices.Add(index), "duplicate or invalid frame index");
            Require(width > 0 && height > 0 && (long)width * height <= IndexedImage.MaxPixels, "invalid frame dimensions");
            Require(frame.GetProperty("indices").GetBytesFromBase64().Length == (long)width * height
                && frame.GetProperty("alpha").GetBytesFromBase64().Length == (long)width * height, "incomplete frame planes");
            string png = $"{prefix}/{index}.png";
            Require(frame.GetProperty("png").GetString() == png, "frame image reference does not match");
            ValidatePng(root, png, width, height);
        }
        return indices;
    }

    // This is an output identity/completeness check, not another image decoder.
    private static void ValidatePng(string root, string path, int width, int height)
    {
        using var stream = File.OpenRead(Path.Combine(root, path));
        Span<byte> header = stackalloc byte[24];
        Require(stream.Length >= header.Length, "PNG artifact is truncated");
        stream.ReadExactly(header);
        Require(header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            && header.Slice(12, 4).SequenceEqual("IHDR"u8)
            && BinaryPrimitives.ReadInt32BigEndian(header[16..]) == width
            && BinaryPrimitives.ReadInt32BigEndian(header[20..]) == height, "PNG dimensions or signature do not match");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException($"Bundle validation: {message}.");
    }
    private sealed record SpriteReference(int Id, string Path);
    private static JsonDocument ReadJson(string root, string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root, name)));
    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
    private sealed record ArtifactIdentity(string Path, long ByteLength, string Sha256);
}
