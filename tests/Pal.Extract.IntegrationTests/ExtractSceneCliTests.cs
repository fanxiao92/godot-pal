using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Pal.Extract.Tests;

public class ExtractSceneCliTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"pal-bundle-test-{Guid.NewGuid():N}");
    private readonly string input;
    private readonly string output;
    private readonly byte[] map = new byte[65536];
    private byte[] objects = Words(0, 40, 30, -2, 3545, 65535, 0, 0, 0, 3, 0, 3, 17, 0, 77, 0);
    private byte[] scenes = Words(2, 3545, 61234, 0, 99, 0, 0, 1);
    private readonly byte[] roles = new byte[900];
    private static readonly byte[] Sprite = [2, 0, 0, 0, 2, 0, 2, 0, 2, 0, 1, 0x81, 1, 2];

    public ExtractSceneCliTests()
    {
        input = Path.Combine(directory, "input 数据");
        output = Path.Combine(directory, "bundle");
        Directory.CreateDirectory(input);
        WriteRecords();
        WriteMap();
        WriteArchive("Gop.mkf", [], [], Sprite);
        WriteArchive("Mgo.mkf", []);
        var palette = new byte[768];
        palette[0] = 3; palette[1] = 4; palette[2] = 5;
        WriteArchive("Pat.mkf", palette);
    }

    [Fact]
    public async Task Extract_scene_creates_one_complete_bundle_from_true_scene_map_without_text_fonts_or_events()
    {
        var result = await Export();
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Empty(result.Stderr);
        Assert.Equal(new[] { "manifest.json", "map.json", "palette.json", "preview.png", "scene.json", "tiles.json", "tiles/0.png" }, Files(output));
        using var scene = ReadJson("scene.json");
        Assert.Equal(2, scene.RootElement.GetProperty("mapId").GetInt32());
        Assert.Equal(3545, scene.RootElement.GetProperty("wScriptOnEnter").GetInt32());
        Assert.Equal(65535, scene.RootElement.GetProperty("objects")[0].GetProperty("wAutoScript").GetInt32());
        using var manifest = ReadJson("manifest.json");
        var root = manifest.RootElement;
        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal("extract-scene", root.GetProperty("command").GetString());
        var parameters = root.GetProperty("parameters");
        Assert.Equal(1, parameters.GetProperty("sceneId").GetInt32());
        Assert.Equal(2, parameters.GetProperty("mapId").GetInt32());
        Assert.Equal(0, parameters.GetProperty("paletteId").GetInt32());
        Assert.Equal("day", parameters.GetProperty("lighting").GetString());
        var sources = root.GetProperty("sources").EnumerateArray().ToArray();
        Assert.Equal(new[] { "data.mkf", "gop.mkf", "map.mkf", "mgo.mkf", "pat.mkf", "sss.mkf" }, sources.Select(s => s.GetProperty("name").GetString()));
        foreach (var source in sources)
        {
            string file = Directory.GetFiles(input).Single(p => Path.GetFileName(p).Equals(source.GetProperty("name").GetString(), StringComparison.OrdinalIgnoreCase));
            Assert.Equal(new FileInfo(file).Length, source.GetProperty("byteLength").GetInt64());
            Assert.Equal(Hash(file), source.GetProperty("sha256").GetString());
        }
        var resources = root.GetProperty("resources");
        foreach (string name in new[] { "scene", "map", "tiles", "palette", "preview" })
            Assert.True(File.Exists(Path.Combine(output, resources.GetProperty(name).GetString()!)));
        Assert.Empty(resources.GetProperty("sprites").EnumerateArray());
        AssertArtifactIdentities(root);
        foreach (string path in Files(output).Where(p => p.EndsWith(".json")))
        {
            string text = File.ReadAllText(Path.Combine(output, path));
            Assert.DoesNotContain(directory, text);
            Assert.DoesNotContain("timestamp", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Extract_scene_manifest_resolves_hidden_object_default_role_and_all_frame_images_with_shared_palette()
    {
        BinaryPrimitives.WriteUInt16LittleEndian(objects.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(objects.AsSpan(18), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(objects.AsSpan(22), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(roles.AsSpan(24), 2);
        WriteRecords();
        WriteArchive("Mgo.mkf", [], Compress(Sprite), Compress(Sprite));
        Assert.Equal(0, (await Export(lighting: "night")).ExitCode);
        using var manifest = ReadJson("manifest.json");
        var sprites = manifest.RootElement.GetProperty("resources").GetProperty("sprites");
        Assert.Equal(new[] { 1, 2 }, sprites.EnumerateArray().Select(s => s.GetProperty("id").GetInt32()));
        foreach (var reference in sprites.EnumerateArray())
        {
            int id = reference.GetProperty("id").GetInt32();
            Assert.Equal($"sprites/{id}.json", reference.GetProperty("path").GetString());
            using var sprite = ReadJson(reference.GetProperty("path").GetString()!);
            Assert.Equal(id, sprite.RootElement.GetProperty("spriteId").GetInt32());
            var frame = sprite.RootElement.GetProperty("frames")[0];
            Assert.Equal($"sprites/{id}/0.png", frame.GetProperty("png").GetString());
            Assert.True(File.Exists(Path.Combine(output, frame.GetProperty("png").GetString()!)));
            Assert.Equal(new byte[] { 0, 1, 0, 2 }, frame.GetProperty("indices").GetBytesFromBase64());
            Assert.Equal(new byte[] { 255, 255, 0, 255 }, frame.GetProperty("alpha").GetBytesFromBase64());
        }
        using var scene = ReadJson("scene.json");
        Assert.False(scene.RootElement.GetProperty("objects")[0].GetProperty("initiallyVisible").GetBoolean());
        using var palette = ReadJson("palette.json");
        Assert.Equal("night", palette.RootElement.GetProperty("lighting").GetString());
        Assert.Equal("day", palette.RootElement.GetProperty("selectedLighting").GetString());
        AssertArtifactIdentities(manifest.RootElement);
    }

    [Theory]
    [InlineData("scene-boundary", "sss.mkf")]
    [InlineData("scene-map-missing", "map.mkf")]
    [InlineData("map-truncated", "map.mkf")]
    [InlineData("bottom-frame", "gop.mkf")]
    [InlineData("top-frame", "gop.mkf")]
    [InlineData("hidden-frame", "mgo.mkf")]
    [InlineData("default-role-missing", "mgo.mkf")]
    [InlineData("palette", "pat.mkf")]
    public async Task Extract_scene_rejects_invalid_static_dependencies_without_publishing_a_partial_bundle(string kind, string resource)
    {
        switch (kind)
        {
            case "scene-boundary": scenes = scenes[..8]; WriteRecords(); break;
            case "scene-map-missing": BinaryPrimitives.WriteUInt16LittleEndian(scenes, 99); WriteRecords(); break;
            case "map-truncated": WriteArchive("Map.mkf", [], [], [0, 0, 1, 0]); break;
            case "bottom-frame": BinaryPrimitives.WriteUInt32LittleEndian(map, 7); WriteMap(); break;
            case "top-frame": BinaryPrimitives.WriteUInt32LittleEndian(map, 0x00080000); WriteMap(); break;
            case "hidden-frame":
                BinaryPrimitives.WriteUInt16LittleEndian(objects.AsSpan(16), 1);
                WriteRecords();
                WriteArchive("Mgo.mkf", [], Compress(Sprite));
                break;
            case "default-role-missing": BinaryPrimitives.WriteUInt16LittleEndian(roles.AsSpan(24), 99); WriteRecords(); break;
            case "palette": WriteArchive("Pat.mkf", new byte[767]); break;
        }
        var result = await Export();
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains(resource, result.Stderr);
        Assert.DoesNotContain(directory, result.Stderr);
        Assert.DoesNotContain("   at ", result.Stderr);
        Assert.False(Directory.Exists(output));
    }

    [Theory]
    [InlineData("Sss.mkf")]
    [InlineData("Data.mkf")]
    [InlineData("Mgo.mkf")]
    [InlineData("Map.mkf")]
    [InlineData("Gop.mkf")]
    [InlineData("Pat.mkf")]
    public async Task Extract_scene_requires_exactly_the_six_static_archives(string name)
    {
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
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("zero")]
    [InlineData("negative")]
    [InlineData("palette")]
    [InlineData("lighting")]
    [InlineData("empty")]
    public async Task Extract_scene_requires_explicit_options_and_never_echoes_local_paths_on_argument_errors(string kind)
    {
        var args = new List<string> { "extract-scene", "--input", input, "--output", output,
            "--scene", "1", "--palette", "0", "--lighting", "day" };
        switch (kind)
        {
            case "missing": args.RemoveRange(9, 2); break;
            case "duplicate": args[9] = "--scene"; break;
            case "unknown": args[9] = "--map"; break;
            case "zero": args[6] = "0"; break;
            case "negative": args[6] = "-1"; break;
            case "palette": args[8] = "-1"; break;
            case "lighting": args[10] = "guess"; break;
            case "empty": args[4] = ""; break;
        }
        var result = await CliProcess.Run(directory, args.ToArray());
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.DoesNotContain(directory, result.Stderr);
        Assert.Contains("Use --help", result.Stderr);
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
    public async Task Extract_scene_refuses_unsafe_output_without_mutating_inputs_or_existing_files(string kind)
    {
        string sentinel = Path.Combine(directory, "keep.txt");
        File.WriteAllText(sentinel, "unchanged");
        if (kind == "nonempty") { Directory.CreateDirectory(output); File.Copy(sentinel, Path.Combine(output, "keep.txt")); }
        string link = Path.Combine(directory, "link");
        bool linked = kind.Contains("link");
        if (linked) DirectoryLinkFixture.Create(link, input);
        var before = Directory.GetFiles(input).ToDictionary(p => p, Hash);
        try
        {
            string destination = kind switch
            {
                "input" => input,
                "nested-input" => Path.Combine(input, "new"),
                "file" => sentinel,
                "output-link" => link,
                "output-linked-ancestor" => Path.Combine(link, "new"),
                _ => output
            };
            var result = await Export(destination, source: kind == "input-link" ? link : null);
            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Stdout);
            Assert.DoesNotContain(directory, result.Stderr);
            Assert.Equal("unchanged", File.ReadAllText(sentinel));
            Assert.Equal(before.Keys.Order(), Directory.GetFiles(input).Order());
            foreach (var (path, hash) in before) Assert.Equal(hash, Hash(path));
            Assert.False(Directory.Exists(Path.Combine(input, "new")));
            if (kind == "nonempty") Assert.Equal(new[] { "keep.txt" }, Files(output));
        }
        finally { if (linked) Directory.Delete(link); }
    }

    [Fact]
    public async Task Extract_scene_is_byte_deterministic_and_preserves_readonly_input_bytes_timestamps_and_file_set()
    {
        BinaryPrimitives.WriteUInt16LittleEndian(roles.AsSpan(24), 1);
        WriteRecords();
        WriteArchive("Mgo.mkf", [], Compress(Sprite));
        File.Move(Path.Combine(input, "Sss.mkf"), Path.Combine(input, "SSS.MKF"));
        var before = Directory.GetFiles(input).ToDictionary(p => p, p => (Hash: Hash(p), Modified: File.GetLastWriteTimeUtc(p)));
        foreach (string path in before.Keys) File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            Directory.CreateDirectory(output);
            Assert.Equal(0, (await Export()).ExitCode);
            string repeat = Path.Combine(directory, "repeat");
            Assert.Equal(0, (await Export(repeat)).ExitCode);
            Assert.Equal(Files(output), Files(repeat));
            foreach (string file in Files(output)) Assert.Equal(File.ReadAllBytes(Path.Combine(output, file)), File.ReadAllBytes(Path.Combine(repeat, file)));
            var published = Files(output).ToDictionary(p => p, p => Hash(Path.Combine(output, p)));
            Assert.Equal(1, (await Export()).ExitCode);
            foreach (var (path, hash) in published) Assert.Equal(hash, Hash(Path.Combine(output, path)));
            Assert.Equal(before.Keys.Order(), Directory.GetFiles(input).Order());
            foreach (var (path, snapshot) in before)
            {
                Assert.Equal(snapshot.Hash, Hash(path));
                Assert.Equal(snapshot.Modified, File.GetLastWriteTimeUtc(path));
                Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly));
            }
        }
        finally { foreach (string path in before.Keys) File.SetAttributes(path, FileAttributes.Normal); }
    }

    [Fact]
    public async Task Extract_scene_source_identity_covers_actual_archive_bytes_not_just_selected_chunks_or_local_filenames()
    {
        Assert.Equal(0, (await Export()).ExitCode);
        using var first = ReadJson("manifest.json");
        // A different unselected GOP chunk changes archive identity, not the scene artifact set.
        WriteArchive("Gop.mkf", [9, 8, 7], [], Sprite);
        string second = Path.Combine(directory, "second");
        Assert.Equal(0, (await Export(second)).ExitCode);
        using var next = JsonDocument.Parse(File.ReadAllText(Path.Combine(second, "manifest.json")));
        var a = first.RootElement; var b = next.RootElement;
        Assert.NotEqual(a.GetProperty("sources")[1].GetProperty("sha256").GetString(), b.GetProperty("sources")[1].GetProperty("sha256").GetString());
        Assert.Equal(Hash(Path.Combine(input, "Gop.mkf")), b.GetProperty("sources")[1].GetProperty("sha256").GetString());
        Assert.Equal(a.GetProperty("bundleSha256").GetString(), b.GetProperty("bundleSha256").GetString());
    }

    [Fact]
    public async Task Help_describes_complete_extract_scene_entry_point()
    {
        var result = await CliProcess.Run(directory, "--help");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("extract-scene --input", result.Stdout);
        Assert.Contains("--scene <id> --palette <id> --lighting day|night", result.Stdout);
        Assert.Empty(result.Stderr);
    }

    private void AssertArtifactIdentities(JsonElement manifest)
    {
        var artifacts = manifest.GetProperty("artifacts").EnumerateArray().ToArray();
        Assert.Equal(Files(output).Where(p => p != "manifest.json"), artifacts.Select(a => a.GetProperty("path").GetString()));
        var identity = new StringBuilder();
        foreach (var artifact in artifacts)
        {
            string path = artifact.GetProperty("path").GetString()!;
            Assert.DoesNotContain("\\", path);
            Assert.False(Path.IsPathRooted(path));
            string file = Path.Combine(output, path);
            long length = new FileInfo(file).Length;
            string hash = Hash(file);
            Assert.Equal(length, artifact.GetProperty("byteLength").GetInt64());
            Assert.Equal(hash, artifact.GetProperty("sha256").GetString());
            identity.Append(FormattableString.Invariant($"{path}\t{length}\t{hash}\n"));
        }
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToString()))), manifest.GetProperty("bundleSha256").GetString());
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static string[] Files(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray();
    private JsonDocument ReadJson(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(output, name)));
    private Task<(int ExitCode, string Stdout, string Stderr)> Export(string? destination = null, string lighting = "day", string? source = null) =>
        CliProcess.Run(directory, "extract-scene", "--input", source ?? input, "--output", destination ?? output,
            "--scene", "1", "--palette", "0", "--lighting", lighting);
    private void WriteRecords()
    {
        WriteArchive("Sss.mkf", objects, scenes); // no script chunk or unrelated OBJECT table
        WriteArchive("Data.mkf", [], [], [], roles);
    }
    private void WriteMap() => WriteArchive("Map.mkf", [], [], Compress(map));
    private static byte[] Compress(byte[] bytes)
    {
        var encoder = new Yj2TestData(bytes.Length);
        foreach (byte value in bytes) encoder.Literal(value);
        return encoder.End();
    }
    private void WriteArchive(string name, params byte[][] chunks) => MkfFixture.Write(input, name, chunks);
    private static byte[] Words(params int[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), unchecked((ushort)values[i]));
        return bytes;
    }
    public void Dispose() => Directory.Delete(directory, recursive: true);
}
