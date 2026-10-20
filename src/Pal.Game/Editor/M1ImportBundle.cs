using Godot;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pal.Game.Editor;

// Offline, fixed M1 v1 contract. This is deliberately not a runtime JSON loader.
internal sealed class M1ImportBundle : IDisposable
{
    internal readonly string Root;
    internal JsonElement Manifest, Scene, Map, Tiles, Palette;
    internal readonly Dictionary<int, JsonElement> Sprites = new();
    internal readonly Dictionary<string, Image> Images = new(StringComparer.Ordinal);
    private readonly HashSet<string> artifacts = new(StringComparer.Ordinal);
    private readonly HashSet<string> dependencies = new(StringComparer.Ordinal);
    private static readonly string[] ArchiveNames = { "data.mkf", "gop.mkf", "map.mkf", "mgo.mkf", "pat.mkf", "sss.mkf" };
    internal M1ImportBundle(string root) { Root = root; }
    internal static int Int(JsonElement e, string field) => e.GetProperty(field).GetInt32();
    internal static string Text(JsonElement e, string field) => e.GetProperty(field).GetString() ?? throw new InvalidDataException(field + " must not be null");
    internal static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    internal string FilePath(string relative)
    {
        Require(!string.IsNullOrEmpty(relative) && !relative.Contains('\\') && !relative.Contains(':') && !relative.Any(char.IsControl), $"Invalid root-relative path: {relative}");
        var parts = relative.Split('/');
        Require(parts.All(p => p.Length > 0 && p != "." && p != ".." && p.TrimEnd(' ', '.') == p && p.IndexOfAny(Path.GetInvalidFileNameChars()) < 0), $"Invalid root-relative path: {relative}");
        string full = Path.GetFullPath(Path.Combine(Root, relative));
        Require(full.StartsWith(Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal), $"Path escapes package root: {relative}");
        return full;
    }
    private static bool IsHash(string text) => Regex.IsMatch(text, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal JsonElement Read(string path)
    {
        if (path != "manifest.json") { Require(artifacts.Contains(path), $"Unlisted dependency: {path}"); dependencies.Add(path); }
        var file = new FileInfo(FilePath(path));
        Require(file.Exists && file.Length <= 64 * 1024 * 1024, $"Missing or oversized JSON: {path}");
        using var doc = JsonDocument.Parse(File.ReadAllBytes(file.FullName));
        CheckProperties(doc.RootElement);
        var data = doc.RootElement.Clone();
        Require(Int(data, "formatVersion") == 1, $"{path}: unsupported formatVersion (expected 1)");
        return data;
    }
    private static void CheckProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in value.EnumerateObject()) { Require(names.Add(p.Name), $"Duplicate JSON property: {p.Name}"); CheckProperties(p.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) CheckProperties(item);
    }
    internal void Validate()
    {
        Manifest = Read("manifest.json");
        Require(Text(Manifest, "command") == "extract-scene", "manifest command must be extract-scene");
        var entries = Manifest.GetProperty("artifacts").EnumerateArray().ToArray();
        Require(entries.Length is > 0 and <= 8192, "manifest artifact count is invalid");
        var paths = entries.Select(e => Text(e, "path")).ToArray();
        Require(paths.SequenceEqual(paths.Order(StringComparer.Ordinal)) && paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() == paths.Length, "manifest artifacts must be unique and ordinal sorted");
        var identity = new StringBuilder();
        foreach (var entry in entries)
        {
            string path = Text(entry, "path"), hash = Text(entry, "sha256");
            Require(path != "manifest.json", "manifest cannot hash itself");
            string full = FilePath(path);
            long length = entry.GetProperty("byteLength").GetInt64();
            Require(length is >= 0 and <= 128 * 1024 * 1024 && IsHash(hash), $"{path}: invalid artifact identity");
            Require(File.Exists(full), $"{path}: missing artifact");
            Require(new FileInfo(full).Length == length, $"{path}: byteLength mismatch");
            Require(Hash(File.ReadAllBytes(full)) == hash, $"{path}: SHA-256 mismatch");
            artifacts.Add(path);
            identity.Append(path).Append('\t').Append(length.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(hash).Append('\n');
        }
        string bundleHash = Text(Manifest, "bundleSha256");
        Require(IsHash(bundleHash) && Hash(Encoding.UTF8.GetBytes(identity.ToString())) == bundleHash, "manifest bundleSha256 mismatch");
        var actual = Directory.GetFiles(Root, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(Root, p).Replace('\\', '/')).Where(p => p != "manifest.json").ToHashSet(StringComparer.Ordinal);
        Require(actual.SetEquals(artifacts), "manifest does not describe the complete package file set");
        var sources = Manifest.GetProperty("sources").EnumerateArray().ToArray();
        Require(sources.Select(s => Text(s, "name")).SequenceEqual(ArchiveNames), "manifest sources must identify the six M1 archives in ordinal order");
        foreach (var source in sources) Require(source.GetProperty("byteLength").GetInt64() >= 0 && IsHash(Text(source, "sha256")), "manifest source identity invalid");
        // Decode EVERY PNG, including any not reached by static references. Header checks alone are insufficient.
        foreach (string path in artifacts.Where(p => p.EndsWith(".png", StringComparison.OrdinalIgnoreCase))) DecodePng(path);
        var resources = Manifest.GetProperty("resources");
        foreach (string name in new[] { "scene", "map", "tiles", "palette" }) Require(Text(resources, name) == name + ".json", $"manifest {name} entry must be root-relative {name}.json");
        Require(Text(resources, "preview") == "preview.png", "manifest preview entry is invalid");
        Scene = Read("scene.json"); Map = Read("map.json"); Tiles = Read("tiles.json"); Palette = Read("palette.json");
        var parameters = Manifest.GetProperty("parameters");
        int sceneId = Int(parameters, "sceneId"), mapId = Int(parameters, "mapId"), paletteId = Int(parameters, "paletteId");
        string lighting = Text(parameters, "lighting");
        Require(sceneId is > 0 and <= 65535 && mapId is > 0 and <= 65535 && paletteId is >= 0 and <= 65535 && lighting is "day" or "night", "manifest parameters invalid");
        Require(Int(Scene, "sceneId") == sceneId && Int(Scene, "mapId") == mapId && Int(Map, "mapId") == mapId && Int(Tiles, "mapId") == mapId, "scene/map/tiles ID mismatch");
        Require(Int(Palette, "paletteId") == paletteId && Text(Palette, "lighting") == lighting && Text(Palette, "selectedLighting") is "day" or "night", "palette ID/lighting mismatch");
        Require(lighting != "day" || Text(Palette, "selectedLighting") == "day", "day palette cannot select night");
        var colors = Palette.GetProperty("colors").EnumerateArray().ToArray();
        Require(colors.Length == 256, "palette requires 256 colors");
        foreach (var color in colors) foreach (string field in new[] { "r", "g", "b" }) Require(Int(color, field) is >= 0 and <= 255, "palette component out of range");
        Source(Scene.GetProperty("source"), "sss.mkf", 1); Source(Scene.GetProperty("objectsSource"), "sss.mkf", 0);
        Require(Int(Scene.GetProperty("source"), "recordIndex") == sceneId - 1 && Int(Scene.GetProperty("source"), "recordByteLength") == 8, "scene source recordIndex/recordByteLength mismatch");
        Require(Int(Scene.GetProperty("objectsSource"), "recordByteLength") == 32, "scene objectsSource recordByteLength mismatch");
        Source(Map.GetProperty("source"), "map.mkf", mapId); Source(Tiles.GetProperty("source"), "gop.mkf", mapId); Source(Palette.GetProperty("source"), "pat.mkf", paletteId);
        var tiles = Frames(Tiles, "tiles", colors);
        Require(Int(Map, "width") == 64 && Int(Map, "height") == 128 && Int(Map, "halfRows") == 2 && Int(Map, "decodedByteLength") == 65536 && Text(Map, "cellOrder") == "[y,x,h]", "map v1 dimensions/order invalid");
        var cells = Map.GetProperty("cells").EnumerateArray().ToArray();
        Require(cells.Length == 16384, "map requires 16384 cells");
        for (int i = 0; i < cells.Length; i++)
        {
            var c = cells[i]; uint raw = c.GetProperty("raw").GetUInt32();
            int bottom = (int)((raw & 255) | ((raw >> 4) & 256));
            int top = (int)(((raw >> 16) & 255) | ((raw >> 20) & 256)) - 1;
            var t = c.GetProperty("topFrame");
            Require(Int(c, "y") == i / 128 && Int(c, "x") == i / 2 % 64 && Int(c, "h") == i % 2, $"map cell {i}: coordinate/order mismatch");
            Require(Int(c, "bottomFrame") == bottom && (top < 0 ? t.ValueKind == JsonValueKind.Null : t.GetInt32() == top), $"map cell {i}: raw frame mismatch");
            Require(tiles.Contains(bottom) && (top < 0 || tiles.Contains(top)), $"map cell {i}: missing tile frame");
            Require(Int(c, "bottomHeight") == (int)((raw >> 8) & 15) && Int(c, "topHeight") == (int)((raw >> 24) & 15) && c.GetProperty("blocked").GetBoolean() == ((raw & 0x2000) != 0), $"map cell {i}: raw height/blocked mismatch");
        }
        var preview = Map.GetProperty("preview");
        Require(Text(preview, "png") == "preview.png" && Int(preview, "width") == 2048 && Int(preview, "height") == 2048 && Int(preview, "originX") == 0 && Int(preview, "originY") == 0, "map preview metadata invalid");
        Png("preview.png", 2048, 2048);
        var ids = Scene.GetProperty("spriteIds").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        Require(ids.All(id => id is > 0 and <= 65535) && ids.SequenceEqual(ids.Distinct().Order()), "scene spriteIds must be unique and increasing");
        var refs = resources.GetProperty("sprites").EnumerateArray().ToArray();
        Require(refs.Select(r => Int(r, "id")).SequenceEqual(ids), "manifest/scene sprite IDs mismatch");
        var spriteFrames = new Dictionary<int, HashSet<int>>();
        foreach (var r in refs)
        {
            int id = Int(r, "id"); string path = $"sprites/{id}.json";
            Require(Text(r, "path") == path, $"sprite {id}: invalid root-relative reference");
            var sprite = Read(path);
            Require(Int(sprite, "spriteId") == id && Int(sprite, "decodedByteLength") is > 0 and <= 8 * 1024 * 1024, $"sprite {id}: ID/length invalid");
            Source(sprite.GetProperty("source"), "mgo.mkf", id);
            var frames = Frames(sprite, $"sprites/{id}", colors);
            int count = Int(sprite, "frameCount");
            Require(count is > 0 and <= 4096 && Enumerable.Range(0, count).All(frames.Contains), $"sprite {id}: nominal frames missing");
            Sprites.Add(id, sprite); spriteFrames.Add(id, frames);
        }
        var objects = Scene.GetProperty("objects").EnumerateArray().ToArray();
        int start = Word(Scene, "wEventObjectIndex"), end = Word(Scene, "nextEventObjectIndex");
        Word(Scene, "wScriptOnEnter"); Word(Scene, "wScriptOnTeleport");
        Require(end >= start && end - start == objects.Length, "scene object boundaries invalid");
        var requiredSprites = new HashSet<int>();
        for (int i = 0; i < objects.Length; i++)
        {
            var obj = objects[i]; Require(Int(obj, "id") == start + i + 1, "scene object ID/order mismatch");
            foreach (string field in new[] { "x", "y", "wTriggerScript", "wAutoScript", "wTriggerMode", "wSpriteNum", "nSpriteFrames", "wDirection", "wCurrentFrameNum", "nScriptIdleFrame", "wSpritePtrOffset", "nSpriteFramesAuto", "wScriptIdleFrameCountAuto" }) Word(obj, field);
            foreach (string field in new[] { "sVanishTime", "sLayer", "sState" }) Require(Int(obj, field) is >= -32768 and <= 32767, "object signed SHORT out of range: " + field);
            int id = SpriteReference(obj, spriteFrames, requiredSprites);
            var initial = obj.GetProperty("initialFrameIndex");
            int current = Int(obj, "wCurrentFrameNum"), count = Int(obj, "nSpriteFrames");
            int selected = Int(obj, "wDirection") * count + (count == 3 ? current switch { 2 => 0, 3 => 2, _ => current } : current);
            Require(id == 0 ? initial.ValueKind == JsonValueKind.Null : initial.GetInt32() == selected && spriteFrames[id].Contains(selected), "object selected initial frame missing/mismatched");
            Require(obj.GetProperty("initiallyVisible").GetBoolean() == (id != 0 && Int(obj, "sState") > 0 && Int(obj, "sVanishTime") <= 0), "object initial visibility mismatch");
        }
        var party = Scene.GetProperty("defaultParty").EnumerateArray().ToArray();
        Require(party.Length == 1 && Int(party[0], "roleId") == 0, "M1 defaultParty must contain role0 only");
        SpriteReference(party[0], spriteFrames, requiredSprites);
        Source(party[0].GetProperty("source"), "data.mkf", 3);
        Require(Int(party[0].GetProperty("source"), "spriteByteOffset") == 24, "defaultParty role0 spriteByteOffset mismatch");
        Require(requiredSprites.SetEquals(ids), "scene spriteIds do not match complete static dependencies (including hidden objects)");
        Require(dependencies.SetEquals(artifacts), "package has artifacts outside the complete static resource set");
    }
    private static int Word(JsonElement data, string field) { int value = Int(data, field); Require(value is >= 0 and <= 65535, field + " is not a WORD"); return value; }
    private static void Source(JsonElement data, string archive, int chunk) { Require(Text(data, "archive") == archive && Int(data, "chunk") == chunk, "resource source archive/chunk mismatch"); }
    private static int SpriteReference(JsonElement record, Dictionary<int, HashSet<int>> frames, HashSet<int> required)
    {
        int id = Word(record, "wSpriteNum"); var path = record.GetProperty("sprite");
        Require(id == 0 ? path.ValueKind == JsonValueKind.Null : path.GetString() == $"sprites/{id}.json" && frames.ContainsKey(id), "missing/invalid root-relative scene sprite dependency");
        if (id != 0) required.Add(id); return id;
    }
    private HashSet<int> Frames(JsonElement data, string prefix, JsonElement[] colors)
    {
        var result = new HashSet<int>(); long totalPixels = 0;
        foreach (var frame in data.GetProperty("frames").EnumerateArray())
        {
            int id = Int(frame, "index"), w = Int(frame, "width"), h = Int(frame, "height");
            Require(id is >= 0 and <= 65535 && result.Add(id), prefix + ": duplicate/invalid frame index");
            Require(Int(frame, "sourceOffset") >= 0 && w > 0 && h > 0 && (long)w * h <= 16 * 1024 * 1024, prefix + ": invalid frame size/offset");
            totalPixels += (long)w * h; Require(totalPixels <= 16 * 1024 * 1024, prefix + ": frame pixel budget exceeded");
            byte[] indices = frame.GetProperty("indices").GetBytesFromBase64(), alpha = frame.GetProperty("alpha").GetBytesFromBase64();
            Require(indices.Length == (long)w * h && alpha.Length == indices.Length, prefix + ": incomplete frame planes");
            string png = $"{prefix}/{id}.png";
            Require(Text(frame, "png") == png, prefix + ": invalid root-relative PNG reference");
            var image = Png(png, w, h); image.Convert(Image.Format.Rgba8); byte[] pixels = image.GetData();
            for (int i = 0; i < indices.Length; i++)
            {
                var color = colors[indices[i]];
                Require(pixels[i * 4] == Int(color, "r") && pixels[i * 4 + 1] == Int(color, "g") && pixels[i * 4 + 2] == Int(color, "b") && pixels[i * 4 + 3] == alpha[i], $"{png}: PNG RGBA disagrees with palette/indices/independent alpha at pixel {i}");
            }
        }
        Require(result.Count > 0, prefix + ": no frames"); return result;
    }
    private void DecodePng(string path)
    {
        byte[] bytes = File.ReadAllBytes(FilePath(path));
        Require(bytes.Length >= 24 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), path + ": invalid PNG signature");
        int w = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16)), h = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20));
        Require(w > 0 && h > 0 && (long)w * h <= 16 * 1024 * 1024, path + ": PNG dimensions exceed budget");
        var image = new Image();
        try { Require(image.LoadPngFromBuffer(bytes) == Error.Ok && !image.IsEmpty(), path + ": PNG decode failed"); Images.Add(path, image); }
        catch { image.Dispose(); throw; }
    }
    private Image Png(string path, int width, int height)
    {
        Require(artifacts.Contains(path) && Images.ContainsKey(path), path + ": missing PNG"); dependencies.Add(path);
        var image = Images[path]; Require(image.GetWidth() == width && image.GetHeight() == height, path + ": PNG dimension mismatch"); return image;
    }
    public void Dispose() { foreach (var image in Images.Values) image.Dispose(); }
}
