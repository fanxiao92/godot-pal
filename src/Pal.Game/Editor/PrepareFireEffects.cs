using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Pal.Game.Profiles;
using Pal.Game.Resources;

namespace Pal.Game.Editor;

// Explicit offline FIRE preparation (#147): consumes one complete export-battle-images
// fire package (#146) and publishes native fire-effect sprites through the existing
// PalSprite/PalFrame/ImageTexture format. Runtime startup never calls this node; the
// shipped Main keeps loading prepared native resources only. SpriteId keeps the original
// FIRE chunk number (magic-effect identity, never a renumbered item index); empty chunks
// publish nothing. No animation timing, playback or new texture format is introduced.
public partial class PrepareFireEffects : Node
{
    public const string Marker = "PAL_PREPARE_FIRE_EFFECTS:";

    private const string ArchiveName = "fire.mkf";
    private const string PaletteName = "pat.mkf";
    private const string Encoding = "Windows YJ2";
    private const string FrameLayout = "PAL sprite WORD offset table + RLE frames";
    private const string PaletteUsage = "PNG colorization only; frame indices and alpha are authoritative";
    private const string EmptyChunkSha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private const int MaximumDecodedChunkBytes = 8 * 1024 * 1024;
    private const long MaximumPackageBytes = 512L * 1024 * 1024;

    public override void _Ready()
    {
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            Require(args.Length == 4 && args[0] == "--fire-input" && args[2] == "--fire-output", "invalid fire-effects prepare arguments");
            var mapping = Prepare(args[1], args[3]);
            GD.Print(Marker + mapping.Count);
            GetTree().Quit(0);
        }
        catch (Exception error) { GD.PrintErr("Fire effects native import failed: " + error.Message); GetTree().Quit(1); }
    }

    /// <summary>Publishes native fire-effect sprites for one complete export-battle-images
    /// fire package. Input stays read-only, the output directory must be new or empty under
    /// project Assets, every published sprite is read back through the production checked
    /// loader before success is reported, and failures remove only files this call created.
    /// Returns the FIRE chunk -> native sprite URI mapping for decoded chunks only.</summary>
    public static IReadOnlyDictionary<int, string> Prepare(string inputDirectory, string outputDirectory)
    {
        string input = Normalize(inputDirectory), output = Normalize(outputDirectory);
        var createdDirectories = new List<string>();
        var publishedFiles = new List<string>();
        try
        {
            string assets = Normalize(ProjectSettings.GlobalizePath("res://Assets"));
            ValidatePaths(input, output, assets);
            string relative = Path.GetRelativePath(assets, output);
            // Godot LocalizePath compares the physical project prefix case-sensitively, even on Windows.
            output = Normalize(Path.Combine(assets, relative));
            string outputUri = "res://Assets/" + relative.Replace('\\', '/');
            CheckOutput(output);
            FirePackage package = ReadPackage(input);
            Dictionary<string, string> inputState = SnapshotInput(input);

            CreateOwnedDirectory(output, createdDirectories);
            CreateOwnedDirectory(Path.Combine(output, "Sprites"), createdDirectories);
            CreateOwnedDirectory(Path.Combine(output, "Textures"), createdDirectories);
            var mapping = new SortedDictionary<int, string>();
            foreach (FireChunk chunk in package.Chunks.Where(entry => entry.Compression == "yj2"))
            {
                string spriteUri = outputUri + "/Sprites/" + chunk.Index + ".tres";
                string textureDirectory = Path.Combine(output, "Textures", chunk.Index.ToString());
                CreateOwnedDirectory(textureDirectory, createdDirectories);
                var nativeFrames = new Godot.Collections.Array<PalFrame>();
                foreach (FireFrame frame in chunk.Frames)
                {
                    string textureUri = outputUri + "/Textures/" + chunk.Index + "/" + frame.Index + ".tres";
                    var savedTexture = ImageTexture.CreateFromImage(frame.Preview);
                    Require(ResourceSaver.Save(savedTexture, textureUri) == Error.Ok, "could not save native fire texture: " + textureUri);
                    publishedFiles.Add(Path.Combine(textureDirectory, frame.Index + ".tres"));
                    var texture = ResourceLoader.Load<Texture2D>(textureUri, cacheMode: ResourceLoader.CacheMode.IgnoreDeep)
                        ?? throw new InvalidDataException("native fire texture readback failed: " + textureUri);
                    Require(texture.ResourcePath == textureUri, "native fire texture readback failed: " + textureUri);
                    nativeFrames.Add(new PalFrame
                    {
                        Index = frame.Index,
                        SourceOffset = frame.SourceOffset,
                        Width = frame.Width,
                        Height = frame.Height,
                        Indices = frame.Indices,
                        Alpha = frame.Alpha,
                        Texture = texture,
                    });
                }
                var native = new PalSprite
                {
                    SpriteId = chunk.Index,
                    DecodedByteLength = chunk.DecodedByteLength,
                    FrameCount = chunk.FrameCount,
                    Source = SourceDeclaration(chunk.Index),
                    Frames = nativeFrames,
                };
                Require(ResourceSaver.Save(native, spriteUri) == Error.Ok, "could not save native fire sprite: " + spriteUri);
                publishedFiles.Add(Path.Combine(output, "Sprites", chunk.Index + ".tres"));
                mapping.Add(chunk.Index, spriteUri);
            }
            RequireOutputPublished(output, publishedFiles);

            // Checked readback through the production loader: expectations are built from
            // the validated package data, never from the published target resources.
            foreach (FireChunk chunk in package.Chunks.Where(entry => entry.Compression == "yj2"))
            {
                string spriteUri = mapping[chunk.Index];
                var expected = new PalSprite
                {
                    SpriteId = chunk.Index,
                    DecodedByteLength = chunk.DecodedByteLength,
                    FrameCount = chunk.FrameCount,
                    Source = SourceDeclaration(chunk.Index),
                };
                var hashes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [spriteUri] = Godot.FileAccess.GetSha256(spriteUri),
                };
                foreach (FireFrame frame in chunk.Frames)
                {
                    string textureUri = outputUri + "/Textures/" + chunk.Index + "/" + frame.Index + ".tres";
                    hashes[textureUri] = Godot.FileAccess.GetSha256(textureUri);
                    // Expected texture identity (URI/type/dimensions) is built from the
                    // package-decoded preview, never by loading the published target.
                    var texture = ImageTexture.CreateFromImage(frame.Preview);
                    texture.TakeOverPath(textureUri);
                    expected.Frames.Add(new PalFrame
                    {
                        Index = frame.Index,
                        SourceOffset = frame.SourceOffset,
                        Width = frame.Width,
                        Height = frame.Height,
                        Indices = frame.Indices,
                        Alpha = frame.Alpha,
                        Texture = texture,
                    });
                }
                NativeBattleVisualProfile.LoadSprite(spriteUri, expected, hashes);
            }
            RequireInputUnchanged(input, inputState);
            RequireOutputPublished(output, publishedFiles);
            return mapping;
        }
        catch
        {
            // Never recursively remove output: another caller may have added an entry.
            foreach (string file in publishedFiles)
                try
                {
                    PreparationPathSafety.CheckNoReparse(file, allowMissing: true);
                    if (File.Exists(file)) File.Delete(file);
                }
                catch { }
            for (int index = createdDirectories.Count - 1; index >= 0; index--)
                try
                {
                    PreparationPathSafety.CheckNoReparse(createdDirectories[index], allowMissing: true);
                    if (Directory.Exists(createdDirectories[index])) Directory.Delete(createdDirectories[index], false);
                }
                catch { }
            throw;
        }
    }

    private sealed record FireFrame(int Index, int SourceOffset, int Width, int Height,
        byte[] Indices, byte[] Alpha, string Png, Image Preview);
    private sealed record FireChunk(int Index, string Sha256, string Compression,
        int DecodedByteLength, int FrameCount, List<FireFrame> Frames);
    private sealed record FirePackage(List<FireChunk> Chunks);

    // Strict complete-package validation: every index.json/sprite document field is
    // checked against the frozen export-battle-images v1 shape, chunk/sprite identities
    // are cross-checked, PNG previews must decode with the declared dimensions, and the
    // actual file set must match the declared set exactly. Rejected packages never reach
    // publication.
    private static FirePackage ReadPackage(string input)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            files.Add(Relative(input, path), path);
        long totalBytes = 0;
        byte[] ReadFile(string relative)
        {
            if (!files.TryGetValue(relative, out string? full))
                throw new InvalidDataException(relative + " is missing from the fire package");
            using FileStream lease = PreparationPathSafety.OpenStableRead(full);
            Require(lease.Length <= MaximumPackageBytes, relative + " exceeds the fire package size limit");
            totalBytes += lease.Length;
            Require(totalBytes <= MaximumPackageBytes, "fire package exceeds the 512 MiB preparation limit");
            byte[] bytes = new byte[checked((int)lease.Length)];
            lease.ReadExactly(bytes);
            return bytes;
        }

        using var index = ParseJson(ReadFile("index.json"), "index.json");
        var root = Fields(index.RootElement, "index.json",
            "formatVersion", "profile", "command", "parameters", "sources", "palette", "chunks", "totals");
        _ = Integer(root, "formatVersion", "index.json", 1, 1);
        Text(root, "profile", "index.json", "faithful");
        Text(root, "command", "index.json", "export-battle-images");
        var parameters = Fields(root["parameters"], "index.json.parameters", "archive");
        Text(parameters, "archive", "index.json.parameters", "fire");
        Require(root["sources"].ValueKind == JsonValueKind.Array && root["sources"].GetArrayLength() == 2,
            "index.json.sources must declare exactly " + ArchiveName + " and " + PaletteName);
        for (int source = 0; source < 2; source++)
        {
            string path = "index.json.sources[" + source + "]";
            var entry = Fields(root["sources"][source], path, "name", "byteLength", "sha256");
            Text(entry, "name", path, source == 0 ? ArchiveName : PaletteName);
            _ = Integer(entry, "byteLength", path, 1, int.MaxValue);
            _ = Hex64(entry, "sha256", path);
        }
        var palette = Fields(root["palette"], "index.json.palette", "archive", "chunk", "lighting", "usage");
        Text(palette, "archive", "index.json.palette", PaletteName);
        _ = Integer(palette, "chunk", "index.json.palette", 0, 0);
        Text(palette, "lighting", "index.json.palette", "day");
        Text(palette, "usage", "index.json.palette", PaletteUsage);
        Require(root["chunks"].ValueKind == JsonValueKind.Array && root["chunks"].GetArrayLength() > 0,
            "index.json.chunks must be a nonempty array");

        var chunks = new List<FireChunk>();
        var chunkIndexes = new HashSet<int>();
        for (int ordinal = 0; ordinal < root["chunks"].GetArrayLength(); ordinal++)
        {
            string path = "index.json.chunks[" + ordinal + "]";
            JsonElement element = root["chunks"][ordinal];
            Require(element.ValueKind == JsonValueKind.Object, path + " must be a JSON object");
            string? compression = element.TryGetProperty("compression", out JsonElement value)
                && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            Require(compression is "yj2" or "empty", path + ".compression must be \"yj2\" or \"empty\"");
            var entry = Fields(element, path, compression == "yj2"
                ? new[] { "index", "byteLength", "sha256", "compression", "decodedByteLength", "frameCount", "decodedFrameCount", "sprite" }
                : new[] { "index", "byteLength", "sha256", "compression" });
            int chunkIndex = Integer(entry, "index", path, 0, ushort.MaxValue);
            Require(chunkIndexes.Add(chunkIndex), path + ".index " + chunkIndex + " is declared more than once");
            if (compression == "empty")
            {
                _ = Integer(entry, "byteLength", path, 0, 0);
                Require(Hex64(entry, "sha256", path) == EmptyChunkSha256, path + ": an empty chunk must declare the empty-bytes SHA-256");
                chunks.Add(new(chunkIndex, EmptyChunkSha256, "empty", 0, 0, []));
                continue;
            }
            _ = Integer(entry, "byteLength", path, 1, int.MaxValue);
            string chunkSha256 = Hex64(entry, "sha256", path);
            int decodedByteLength = Integer(entry, "decodedByteLength", path, 1, MaximumDecodedChunkBytes);
            int frameCount = Integer(entry, "frameCount", path, 1, 4096);
            int decodedFrameCount = Integer(entry, "decodedFrameCount", path, frameCount, 4096);
            _ = Text(entry, "sprite", path, "sprites/" + chunkIndex + ".json");
            chunks.Add(new(chunkIndex, chunkSha256, "yj2", decodedByteLength, frameCount,
                ReadSpriteFrames(chunkIndex, decodedByteLength, frameCount, decodedFrameCount, ReadFile)));
        }

        var totals = Fields(root["totals"], "index.json.totals", "chunks", "decodedChunks", "emptyChunks", "frames", "files");
        int decodedChunks = chunks.Count(chunk => chunk.Compression == "yj2");
        int frameTotal = chunks.Sum(chunk => chunk.Frames.Count);
        Require(Integer(totals, "chunks", "index.json.totals", 0, int.MaxValue) == chunks.Count,
            "index.json.totals.chunks must match the chunk entries");
        Require(Integer(totals, "decodedChunks", "index.json.totals", 0, int.MaxValue) == decodedChunks,
            "index.json.totals.decodedChunks must match the decoded chunk entries");
        Require(Integer(totals, "emptyChunks", "index.json.totals", 0, int.MaxValue) == chunks.Count - decodedChunks,
            "index.json.totals.emptyChunks must match the empty chunk entries");
        Require(Integer(totals, "frames", "index.json.totals", 0, int.MaxValue) == frameTotal,
            "index.json.totals.frames must match the decoded frame count");
        Require(Integer(totals, "files", "index.json.totals", 0, int.MaxValue) == frameTotal + decodedChunks,
            "index.json.totals.files must match the exported PNG and sprite document count");

        var declared = new HashSet<string>(StringComparer.Ordinal) { "index.json" };
        foreach (FireChunk chunk in chunks.Where(entry => entry.Compression == "yj2"))
        {
            declared.Add("sprites/" + chunk.Index + ".json");
            foreach (FireFrame frame in chunk.Frames) declared.Add(frame.Png);
        }
        string? extra = files.Keys.Where(file => !declared.Contains(file)).Order(StringComparer.Ordinal).FirstOrDefault();
        Require(extra is null, "unexpected file in fire package: " + extra);
        foreach (string directory in Directory.EnumerateDirectories(input, "*", SearchOption.AllDirectories))
        {
            string[] parts = Relative(input, directory).Split('/');
            Require(parts.Length == 1 && parts[0] == "sprites"
                || parts.Length == 2 && parts[0] == "sprites" && int.TryParse(parts[1], out int owner)
                    && chunks.Any(chunk => chunk.Index == owner && chunk.Compression == "yj2"),
                "unexpected directory in fire package: " + Relative(input, directory));
        }
        return new FirePackage(chunks);
    }

    private static List<FireFrame> ReadSpriteFrames(int chunkIndex, int decodedByteLength, int frameCount,
        int decodedFrameCount, Func<string, byte[]> readFile)
    {
        string spritePath = "sprites/" + chunkIndex + ".json";
        using var document = ParseJson(readFile(spritePath), spritePath);
        var fields = Fields(document.RootElement, spritePath,
            "formatVersion", "profile", "chunk", "source", "decodedByteLength", "frameCount", "frames");
        _ = Integer(fields, "formatVersion", spritePath, 1, 1);
        Text(fields, "profile", spritePath, "faithful");
        Require(Integer(fields, "chunk", spritePath, 0, ushort.MaxValue) == chunkIndex,
            spritePath + ".chunk must match the index chunk " + chunkIndex);
        var source = Fields(fields["source"], spritePath + ".source", "archive", "chunk", "encoding", "frameLayout");
        Text(source, "archive", spritePath + ".source", ArchiveName);
        Require(Integer(source, "chunk", spritePath + ".source", 0, ushort.MaxValue) == chunkIndex,
            spritePath + ".source.chunk must match the index chunk " + chunkIndex);
        Text(source, "encoding", spritePath + ".source", Encoding);
        Text(source, "frameLayout", spritePath + ".source", FrameLayout);
        Require(Integer(fields, "decodedByteLength", spritePath, 1, MaximumDecodedChunkBytes) == decodedByteLength,
            spritePath + ".decodedByteLength must match the index chunk");
        Require(Integer(fields, "frameCount", spritePath, 1, 4096) == frameCount,
            spritePath + ".frameCount must match the index chunk");
        Require(fields["frames"].ValueKind == JsonValueKind.Array && fields["frames"].GetArrayLength() == decodedFrameCount,
            spritePath + ".frames must declare exactly " + decodedFrameCount + " frames");
        var frames = new List<FireFrame>();
        var frameIndexes = new HashSet<int>();
        for (int ordinal = 0; ordinal < decodedFrameCount; ordinal++)
        {
            string path = spritePath + ".frames[" + ordinal + "]";
            var frame = Fields(fields["frames"][ordinal], path,
                "index", "sourceOffset", "width", "height", "png", "indices", "alpha");
            int frameIndex = Integer(frame, "index", path, 0, ushort.MaxValue);
            Require(frameIndexes.Add(frameIndex), path + ".index " + frameIndex + " is declared more than once");
            int sourceOffset = Integer(frame, "sourceOffset", path, 0, decodedByteLength - 1);
            int width = Integer(frame, "width", path, 1, ushort.MaxValue);
            int height = Integer(frame, "height", path, 1, ushort.MaxValue);
            byte[] indices = Base64(frame, "indices", path), alpha = Base64(frame, "alpha", path);
            Require(indices.Length == (long)width * height, path + ".indices length must match width*height");
            Require(alpha.Length == indices.Length, path + ".alpha length must match indices");
            string pngPath = Text(frame, "png", path, "sprites/" + chunkIndex + "/" + frameIndex + ".png");
            byte[] png = readFile(pngPath);
            var preview = new Image();
            Require(preview.LoadPngFromBuffer(png) == Error.Ok, pngPath + " is not a decodable PNG");
            Require(preview.GetWidth() == width && preview.GetHeight() == height,
                pngPath + " dimensions must match the declared frame " + width + "x" + height);
            frames.Add(new(frameIndex, sourceOffset, width, height, indices, alpha, pngPath, preview));
        }
        Require(Enumerable.Range(0, frameCount).All(frameIndexes.Contains),
            spritePath + " must declare nominal frames 0.." + (frameCount - 1));
        return frames;
    }

    private static Godot.Collections.Dictionary SourceDeclaration(int chunkIndex) => new()
    {
        ["archive"] = ArchiveName,
        ["chunk"] = chunkIndex,
        ["encoding"] = Encoding,
        ["frameLayout"] = FrameLayout,
    };

    private static JsonDocument ParseJson(byte[] bytes, string path)
    {
        int bom = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
        try { return JsonDocument.Parse(bytes.AsMemory(bom)); }
        catch (JsonException error) { throw new InvalidDataException(path + " is not valid JSON: " + error.Message, error); }
    }

    private static Dictionary<string, JsonElement> Fields(JsonElement element, string path, params string[] allowed)
    {
        Require(element.ValueKind == JsonValueKind.Object, path + " must be a JSON object");
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            Require(allowed.Contains(property.Name), path + " has unexpected field '" + property.Name + "'");
            Require(fields.TryAdd(property.Name, property.Value), path + " has duplicate field '" + property.Name + "'");
        }
        foreach (string name in allowed)
            Require(fields.ContainsKey(name), path + " is missing required field '" + name + "'");
        return fields;
    }

    private static int Integer(IReadOnlyDictionary<string, JsonElement> fields, string name, string path, int minimum, int maximum)
    {
        if (!fields.TryGetValue(name, out JsonElement value) || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out int number) || number < minimum || number > maximum)
            throw new InvalidDataException(path + "." + name + " must be an integer in " + minimum + ".." + maximum);
        return number;
    }

    private static string Text(IReadOnlyDictionary<string, JsonElement> fields, string name, string path, string expected)
    {
        Require(fields.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            && value.GetString() == expected,
            path + "." + name + " must be \"" + expected + "\"");
        return expected;
    }

    private static string Hex64(IReadOnlyDictionary<string, JsonElement> fields, string name, string path)
    {
        string value = fields.TryGetValue(name, out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? "" : "";
        Require(value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'),
            path + "." + name + " must be a lowercase 64-hex SHA-256");
        return value;
    }

    private static byte[] Base64(IReadOnlyDictionary<string, JsonElement> fields, string name, string path)
    {
        Require(fields.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.String,
            path + "." + name + " must be a base64 string");
        try { return Convert.FromBase64String(value.GetString() ?? ""); }
        catch (FormatException error) { throw new InvalidDataException(path + "." + name + " is not valid base64", error); }
    }

    private static Dictionary<string, string> SnapshotInput(string input)
    {
        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            snapshot.Add(Relative(input, path), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
        return snapshot;
    }

    private static void RequireInputUnchanged(string input, Dictionary<string, string> expected)
    {
        Dictionary<string, string> current = SnapshotInput(input);
        Require(current.Count == expected.Count
            && current.All(entry => expected.TryGetValue(entry.Key, out string? hash) && hash == entry.Value),
            "fire package input changed during preparation");
    }

    private static void RequireOutputPublished(string output, List<string> published)
    {
        var actual = new HashSet<string>(
            Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).Select(path => Relative(output, path)),
            StringComparer.Ordinal);
        var expected = new HashSet<string>(published.Select(path => Relative(output, path)), StringComparer.Ordinal);
        Require(actual.SetEquals(expected), "fire effects native output changed during preparation");
    }

    private static void ValidatePaths(string input, string output, string assets)
    {
        Require(IsInside(output, assets), "fire effects native output must be under project Assets");
        Require(!IsInside(input, output) && !IsInside(output, input), "fire effects input and output must not overlap in either direction");
        Require(!IsInside(input, ProjectSettings.GlobalizePath("res://")), "fire effects input must be outside src/Pal.Game");
        PreparationPathSafety.CheckNoReparse(input, allowMissing: true);
        Require(Directory.Exists(input), "fire effects input must be an existing directory");
        PreparationPathSafety.CheckTreeNoReparse(input);
        PreparationPathSafety.CheckNoReparse(output, allowMissing: true);
        Require(!File.Exists(output), "fire effects native output is an existing file");
    }

    private static void CheckOutput(string output)
    {
        if (!Directory.Exists(output)) return;
        PreparationPathSafety.CheckTreeNoReparse(output);
        Require(!Directory.EnumerateFileSystemEntries(output).Any(),
            "fire effects native output must be new or empty; no overwrite or unexpected entries are supported");
    }

    private static void CreateOwnedDirectory(string path, List<string> created)
    {
        var missing = new Stack<string>();
        string? current = path;
        while (current is not null && !Directory.Exists(current))
        {
            missing.Push(current);
            current = Path.GetDirectoryName(current);
        }
        while (missing.Count > 0)
        {
            current = missing.Pop();
            PreparationPathSafety.CheckNoReparse(current, allowMissing: true);
            if (Directory.Exists(current)) continue;
            Directory.CreateDirectory(current);
            created.Add(current);
        }
    }

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static bool IsInside(string child, string parent)
    {
        child = Normalize(child); parent = Normalize(parent);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string prefix = Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar;
        return child.Equals(parent, comparison) || child.StartsWith(prefix, comparison);
    }
    private static void Require(bool condition, string diagnostic)
    {
        if (!condition) throw new InvalidDataException(diagnostic);
    }
}
