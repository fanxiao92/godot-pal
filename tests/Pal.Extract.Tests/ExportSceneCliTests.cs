using System.Buffers.Binary;
using System.Text.Json;
using Xunit;

namespace Pal.Extract.Tests;

public class ExportSceneCliTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"pal-scene-test-{Guid.NewGuid():N}");
    private readonly string input;
    private readonly string output;
    private byte[] objects = Words(-7, 65530, 321, -2, 3545, 65535, -1, 8, 0, 3, 2, 3, 65534, 456, 17, 60000);
    private byte[] scenes = Words(12, 3545, 61234, 0, 99, 0, 0, 1);
    private byte[] roles = new byte[900];

    public ExportSceneCliTests()
    {
        input = Path.Combine(directory, "input 数据");
        output = Path.Combine(directory, "output");
        Directory.CreateDirectory(input);
        WriteRecords();
        WriteArchive("Mgo.mkf", []);
        var palette = new byte[768];
        palette[0] = 3; palette[1] = 4; palette[2] = 5;
        WriteArchive("Pat.mkf", palette);
    }

    [Fact]
    public async Task Export_scene_preserves_every_initial_record_field_and_numeric_entries_without_scripts_or_language_files()
    {
        var result = await Export();
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Empty(result.Stderr);
        using var json = ReadJson("scene.json");
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal(1, root.GetProperty("sceneId").GetInt32());
        Assert.Equal(12, root.GetProperty("mapId").GetInt32());
        Assert.Equal(3545, root.GetProperty("wScriptOnEnter").GetInt32());
        Assert.Equal(61234, root.GetProperty("wScriptOnTeleport").GetInt32());
        Assert.Equal(0, root.GetProperty("wEventObjectIndex").GetInt32());
        Assert.Equal(1, root.GetProperty("nextEventObjectIndex").GetInt32());
        var records = root.GetProperty("objects");
        Assert.Equal(1, records.GetArrayLength());
        var record = records[0];
        var expected = new Dictionary<string, int>
        {
            ["id"] = 1, ["sVanishTime"] = -7, ["x"] = 65530, ["y"] = 321, ["sLayer"] = -2,
            ["wTriggerScript"] = 3545, ["wAutoScript"] = 65535, ["sState"] = -1,
            ["wTriggerMode"] = 8, ["wSpriteNum"] = 0, ["nSpriteFrames"] = 3, ["wDirection"] = 2,
            ["wCurrentFrameNum"] = 3, ["nScriptIdleFrame"] = 65534, ["wSpritePtrOffset"] = 456,
            ["nSpriteFramesAuto"] = 17, ["wScriptIdleFrameCountAuto"] = 60000
        };
        foreach (var (name, value) in expected) Assert.Equal(value, record.GetProperty(name).GetInt32());
        Assert.Equal(JsonValueKind.Null, record.GetProperty("sprite").ValueKind);
        Assert.Equal(JsonValueKind.Null, record.GetProperty("initialFrameIndex").ValueKind);
        Assert.False(record.GetProperty("initiallyVisible").GetBoolean());
        Assert.Empty(root.GetProperty("spriteIds").EnumerateArray());
        var party = root.GetProperty("defaultParty");
        Assert.Equal(1, party.GetArrayLength());
        Assert.Equal(0, party[0].GetProperty("roleId").GetInt32());
        Assert.Equal(0, party[0].GetProperty("wSpriteNum").GetInt32());
        Assert.Equal(JsonValueKind.Null, party[0].GetProperty("sprite").ValueKind);
        Assert.Equal(24, party[0].GetProperty("source").GetProperty("spriteByteOffset").GetInt32());
        Assert.Equal("sss.mkf", root.GetProperty("objectsSource").GetProperty("archive").GetString());
        Assert.Equal(32, root.GetProperty("objectsSource").GetProperty("recordByteLength").GetInt32());
        Assert.DoesNotContain(directory, File.ReadAllText(Path.Combine(output, "scene.json")));
        Assert.Equal(new[] { "palette.json", "scene.json" }, Directory.GetFiles(output).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task Export_scene_exports_hidden_dependencies_and_default_role_frames_with_independent_indices_alpha_and_positioning()
    {
        objects = Words(0, 400, 300, -2, 7, 9, 2, 4, 1, 3, 0, 3, 0, 0, 77, 0,
                        0, 500, 200, 0, 10, 11, 0, 1, 2, 3, 0, 2, 0, 0, 88, 0);
        scenes = Words(12, 3545, 0, 0, 12, 0, 0, 2);
        BinaryPrimitives.WriteUInt16LittleEndian(roles.AsSpan(24), 3);
        // Other five roles must not become members of the initial party or dependencies.
        BinaryPrimitives.WriteUInt16LittleEndian(roles.AsSpan(26), 999);
        WriteRecords();
        // Three frames alias the same 2x2 bitmap; literal pixels include opaque index zero and a skip.
        var sprite = new byte[] { 4,0, 4,0, 4,0, 0,0, 2,0,2,0, 2,0,1, 0x81,1,2 };
        WriteArchive("Mgo.mkf", [], Compress(sprite), Compress(sprite), Compress(sprite));
        var result = await Export();
        Assert.Equal(0, result.ExitCode);
        using var json = ReadJson("scene.json");
        var root = json.RootElement;
        Assert.Equal(new[] { 1, 2, 3 }, root.GetProperty("spriteIds").EnumerateArray().Select(e => e.GetInt32()));
        var records = root.GetProperty("objects");
        Assert.Equal(2, records[0].GetProperty("initialFrameIndex").GetInt32()); // walking current3 -> bitmap2
        Assert.Equal(0, records[1].GetProperty("initialFrameIndex").GetInt32()); // walking current2 -> bitmap0
        Assert.True(records[0].GetProperty("initiallyVisible").GetBoolean());
        Assert.False(records[1].GetProperty("initiallyVisible").GetBoolean());
        Assert.Equal(77, records[0].GetProperty("nSpriteFramesAuto").GetInt32()); // not overwritten by res.c's runtime load
        Assert.Equal("sprites/2.json", records[1].GetProperty("sprite").GetString());
        Assert.Equal("sprites/3.json", root.GetProperty("defaultParty")[0].GetProperty("sprite").GetString());
        var positioning = root.GetProperty("positioning");
        Assert.Equal("short(x)-width/2", positioning.GetProperty("objects").GetProperty("bitmapLeft").GetString());
        Assert.Equal("short(y)+7-height", positioning.GetProperty("objects").GetProperty("bitmapTop").GetString());
        Assert.Equal("short(y)+sLayer*8+9", positioning.GetProperty("objects").GetProperty("sortY").GetString());
        Assert.Equal("sLayer*8+2", positioning.GetProperty("objects").GetProperty("drawLayer").GetString());
        Assert.Equal("y+4-height", positioning.GetProperty("party").GetProperty("bitmapTop").GetString());
        foreach (int id in new[] { 1, 2, 3 })
        {
            using var spriteJson = ReadJson($"sprites/{id}.json");
            var info = spriteJson.RootElement;
            Assert.Equal(id, info.GetProperty("spriteId").GetInt32());
            Assert.Equal(18, info.GetProperty("decodedByteLength").GetInt32());
            Assert.Equal(3, info.GetProperty("frameCount").GetInt32());
            Assert.Equal("mgo.mkf", info.GetProperty("source").GetProperty("archive").GetString());
            Assert.Equal(id, info.GetProperty("source").GetProperty("chunk").GetInt32());
            var frames = info.GetProperty("frames");
            Assert.Equal(3, frames.GetArrayLength());
            Assert.Equal(2, frames[2].GetProperty("index").GetInt32());
            Assert.Equal(8, frames[2].GetProperty("sourceOffset").GetInt32());
            Assert.Equal(2, frames[2].GetProperty("width").GetInt32());
            Assert.Equal(2, frames[2].GetProperty("height").GetInt32());
            Assert.Equal(new byte[] { 0, 1, 0, 2 }, frames[2].GetProperty("indices").GetBytesFromBase64());
            Assert.Equal(new byte[] { 255, 255, 0, 255 }, frames[2].GetProperty("alpha").GetBytesFromBase64());
            Assert.Equal($"sprites/{id}/2.png", frames[2].GetProperty("png").GetString());
            Assert.True(File.Exists(Path.Combine(output, $"sprites/{id}/2.png")));
        }
        using var pat = ReadJson("palette.json");
        Assert.Equal("day", pat.RootElement.GetProperty("selectedLighting").GetString());
        Assert.Equal(12, pat.RootElement.GetProperty("colors")[0].GetProperty("r").GetInt32());
    }

    [Theory]
    [InlineData(1, -1, true)]
    [InlineData(2, 0, true)]
    [InlineData(0, 0, false)]
    [InlineData(-2, 0, false)]
    [InlineData(1, 1, false)]
    public async Task Export_scene_preserves_visibility_and_exports_selected_extra_frame_without_unhiding(short state, short vanish, bool visible)
    {
        objects = Words(vanish, 1, 2, 0, 123, 456, state, 0, 1, 1, 1, 0, 0, 0, 91, 0);
        WriteRecords();
        // Nominal frame0 at4; selected extra frame1 at10. This is legal in PAL_SpriteGetFrame.
        WriteArchive("Mgo.mkf", [], Compress([2,0, 5,0, 1,0,1,0,1,0, 1,0,1,0,1,2]));
        Assert.Equal(0, (await Export()).ExitCode);
        using var scene = ReadJson("scene.json");
        var record = scene.RootElement.GetProperty("objects")[0];
        Assert.Equal(state, record.GetProperty("sState").GetInt32());
        Assert.Equal(vanish, record.GetProperty("sVanishTime").GetInt32());
        Assert.Equal(visible, record.GetProperty("initiallyVisible").GetBoolean());
        Assert.Equal(1, record.GetProperty("initialFrameIndex").GetInt32());
        using var sprite = ReadJson("sprites/1.json");
        Assert.Equal(1, sprite.RootElement.GetProperty("frameCount").GetInt32());
        Assert.Equal(2, sprite.RootElement.GetProperty("frames").GetArrayLength());
        Assert.Equal(new byte[] { 2 }, sprite.RootElement.GetProperty("frames")[1].GetProperty("indices").GetBytesFromBase64());
    }

    [Theory]
    [InlineData("object-truncated", "sss.mkf", "chunk 0")]
    [InlineData("scene-truncated", "sss.mkf", "chunk 1")]
    [InlineData("scene-outside", "sss.mkf", "boundary")]
    [InlineData("range-reversed", "sss.mkf", "range")]
    [InlineData("range-outside", "sss.mkf", "range")]
    [InlineData("roles-truncated", "data.mkf", "900")]
    [InlineData("sprite-outside", "mgo.mkf", "chunk 99")]
    [InlineData("hidden-frame-outside", "mgo.mkf", "frame")]
    [InlineData("frame-sentinel", "mgo.mkf", "sentinel")]
    [InlineData("sprite-truncated", "mgo.mkf", "YJ2")]
    [InlineData("sprite-yj1", "mgo.mkf", "YJ1")]
    [InlineData("sprite-rle", "mgo.mkf", "RLE")]
    [InlineData("palette", "pat.mkf", "palette")]
    public async Task Export_scene_rejects_damaged_records_and_references_before_writing_any_artifacts(string kind, string resource, string error)
    {
        switch (kind)
        {
            case "object-truncated": objects = objects[..^1]; break;
            case "scene-truncated": scenes = scenes[..^1]; break;
            case "scene-outside": scenes = scenes[..8]; break;
            case "range-reversed": scenes = Words(12, 0, 0, 1, 12, 0, 0, 0); break;
            case "range-outside": scenes = Words(12, 0, 0, 0, 12, 0, 0, 2); break;
            case "roles-truncated": roles = roles[..^1]; break;
            case "sprite-outside": BinaryPrimitives.WriteUInt16LittleEndian(objects.AsSpan(16), 99); break;
            case "hidden-frame-outside":
            case "frame-sentinel":
                BinaryPrimitives.WriteUInt16LittleEndian(objects.AsSpan(16), 1);
                WriteArchive("Mgo.mkf", [], Compress([2,0, 0,0, 1,0,1,0,1,0]));
                BinaryPrimitives.WriteUInt16LittleEndian(objects.AsSpan(18), 1);
                BinaryPrimitives.WriteUInt16LittleEndian(objects.AsSpan(20), 0);
                BinaryPrimitives.WriteUInt16LittleEndian(objects.AsSpan(22), (ushort)(kind == "frame-sentinel" ? 1 : 99));
                break;
            case "sprite-truncated":
            case "sprite-yj1":
            case "sprite-rle":
                BinaryPrimitives.WriteUInt16LittleEndian(roles.AsSpan(24), 1);
                WriteArchive("Mgo.mkf", [], kind == "sprite-truncated" ? [1,0,0,0]
                    : kind == "sprite-yj1" ? "YJ_1"u8.ToArray() : Compress([2,0, 0,0, 1,0,1,0,2,0]));
                break;
            case "palette": WriteArchive("Pat.mkf", new byte[767]); break;
        }
        WriteRecords();
        var result = await Export();
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains(resource, result.Stderr);
        Assert.Contains(error, result.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(directory, result.Stderr);
        Assert.DoesNotContain("   at ", result.Stderr);
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public async Task Export_scene_is_deterministic_case_insensitive_and_preserves_readonly_inputs_and_timestamps()
    {
        BinaryPrimitives.WriteUInt16LittleEndian(roles.AsSpan(24), 1);
        WriteRecords();
        WriteArchive("Mgo.mkf", [], Compress([2,0, 0,0, 1,0,1,0,1,0]));
        File.Move(Path.Combine(input, "Sss.mkf"), Path.Combine(input, "SSS.MKF"));
        var before = Directory.GetFiles(input).ToDictionary(path => path,
            path => (Bytes: File.ReadAllBytes(path), Modified: File.GetLastWriteTimeUtc(path)));
        foreach (string path in before.Keys) File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            Directory.CreateDirectory(output);
            Assert.Equal(0, (await Export()).ExitCode);
            string second = Path.Combine(directory, "second");
            Assert.Equal(0, (await Export(second)).ExitCode);
            var files = Directory.GetFiles(output, "*", SearchOption.AllDirectories);
            foreach (string file in files)
                Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(Path.Combine(second, Path.GetRelativePath(output, file))));
            Assert.Equal(files.Length, Directory.GetFiles(second, "*", SearchOption.AllDirectories).Length);
            foreach (var (file, snapshot) in before)
            {
                Assert.Equal(snapshot.Bytes, File.ReadAllBytes(file));
                Assert.Equal(snapshot.Modified, File.GetLastWriteTimeUtc(file));
                Assert.Equal(FileAttributes.ReadOnly, File.GetAttributes(file) & FileAttributes.ReadOnly);
            }
            Assert.Equal(1, (await Export()).ExitCode);
        }
        finally { foreach (string path in before.Keys) File.SetAttributes(path, FileAttributes.Normal); }
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
    public async Task Export_scene_requires_explicit_valid_options_without_echoing_local_paths(string kind)
    {
        var args = new List<string> { "export-scene", "--input", input, "--output", output,
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
        Assert.Contains("Use --help", result.Stderr);
        Assert.DoesNotContain(directory, result.Stderr);
        Assert.False(Directory.Exists(output));
    }

    [Theory]
    [InlineData("input")]
    [InlineData("nested-input")]
    [InlineData("nonempty")]
    [InlineData("file")]
    public async Task Export_scene_rejects_unsafe_output_and_preserves_existing_files(string kind)
    {
        Directory.CreateDirectory(output);
        string sentinel = Path.Combine(output, "keep.txt");
        File.WriteAllText(sentinel, "unchanged");
        string destination = kind switch { "input" => input, "nested-input" => Path.Combine(input, "new"), "file" => sentinel, _ => output };
        var result = await Export(destination);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.DoesNotContain(directory, result.Stderr);
        Assert.Contains("output", result.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("unchanged", File.ReadAllText(sentinel));
        Assert.False(File.Exists(Path.Combine(input, "scene.json")));
        Assert.False(Directory.Exists(Path.Combine(input, "new")));
    }

    [Fact]
    public async Task Export_scene_bounds_aggregate_sprite_pixels_before_writing_even_for_aliased_transparent_frames()
    {
        BinaryPrimitives.WriteUInt16LittleEndian(roles.AsSpan(24), 1);
        WriteRecords();
        // 257 aliases of a 256x256 transparent bitmap exceed the shared 16M pixel budget.
        var sprite = new byte[516 + 4 + 517 + 1];
        for (int i = 0; i < 257; i++) BinaryPrimitives.WriteUInt16LittleEndian(sprite.AsSpan(i * 2), 258);
        BinaryPrimitives.WriteUInt16LittleEndian(sprite.AsSpan(514), (ushort)(sprite.Length / 2));
        BinaryPrimitives.WriteUInt16LittleEndian(sprite.AsSpan(516), 256);
        BinaryPrimitives.WriteUInt16LittleEndian(sprite.AsSpan(518), 256);
        sprite.AsSpan(520, 516).Fill(255); // 516*127 = 65532 transparent pixels.
        sprite[1036] = 132; // final 4 transparent pixels, plus one alignment byte.
        WriteArchive("Mgo.mkf", [], Compress(sprite));
        var result = await Export();
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("mgo.mkf", result.Stderr);
        Assert.Contains("limit", result.Stderr);
        Assert.False(Directory.Exists(output));
    }

    [Theory]
    [InlineData("Sss.mkf")]
    [InlineData("Data.mkf")]
    [InlineData("Mgo.mkf")]
    [InlineData("Pat.mkf")]
    public async Task Export_scene_reports_missing_required_archive_without_requiring_maps_or_event_artifacts(string name)
    {
        File.Delete(Path.Combine(input, name));
        var result = await Export();
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains(name.ToLowerInvariant(), result.Stderr);
        Assert.Contains("missing", result.Stderr);
        Assert.False(Directory.Exists(output));
    }

    private static byte[] Compress(byte[] raw)
    {
        var encoder = new Yj2TestData(raw.Length);
        foreach (byte value in raw) encoder.Literal(value);
        return encoder.End();
    }

    private static byte[] Words(params int[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), unchecked((ushort)values[i]));
        return bytes;
    }
    private void WriteRecords()
    {
        // SSS chunk2 is deliberately absent: it is the unrelated item/magic OBJECT layout, not EVENTOBJECT.
        WriteArchive("Sss.mkf", objects, scenes);
        WriteArchive("Data.mkf", [], [], [], roles);
    }
    private void WriteArchive(string name, params byte[][] chunks)
    {
        using var writer = new BinaryWriter(File.Create(Path.Combine(input, name)));
        int offset = (chunks.Length + 1) * 4;
        writer.Write(offset);
        foreach (var chunk in chunks) { offset += chunk.Length; writer.Write(offset); }
        foreach (var chunk in chunks) writer.Write(chunk);
    }
    private JsonDocument ReadJson(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(output, name)));
    private Task<(int ExitCode, string Stdout, string Stderr)> Export(string? destination = null) =>
        CliProcess.Run(directory, "export-scene", "--input", input, "--output", destination ?? output,
            "--scene", "1", "--palette", "0", "--lighting", "day");
    public void Dispose() => Directory.Delete(directory, recursive: true);
}
