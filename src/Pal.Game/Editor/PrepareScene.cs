using Godot;
using Godot.Collections;
using Pal.Game.Resources;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using static Pal.Game.Editor.M1ImportBundle;

namespace Pal.Game.Editor;

// Explicit offline scene only. Never referenced by Main or runtime resources.
public partial class PrepareScene : Node
{
    private string output = "";
    public override void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            Require(args.Length == 6 && args[0] == "--prepare-input" && args[2] == "--prepare-output" && args[4] == "--prepare-phase", "invalid offline importer arguments");
            string root = Path.GetFullPath(args[1]); output = ProjectSettings.LocalizePath(Path.GetFullPath(args[3])).TrimEnd('/');
            Require(output == "res://Assets" || output.StartsWith("res://Assets/", StringComparison.Ordinal), "native output must be under project Assets");
            using var bundle = new M1ImportBundle(root);
            bundle.Validate();
            switch (args[5])
            {
                case "textures":
                    foreach (string png in bundle.Images.Keys)
                    {
                        string target = ProjectSettings.GlobalizePath(output + "/" + TexturePath(png));
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(bundle.FilePath(png), target, overwrite: false);
                    }
                    GD.Print("PAL_PREPARE_TEXTURES"); break;
                case "resources": SaveNative(bundle); GD.Print("PAL_PREPARE_NATIVE"); break;
                default: throw new InvalidDataException("invalid offline import phase");
            }
            GetTree().Quit(0);
        }
        catch (Exception e) { GD.PrintErr("M1 native import failed: " + e.Message); GetTree().Quit(1); }
    }
    private static string TexturePath(string png) => png == "preview.png" ? "Maps/Preview.png" : png.StartsWith("tiles/", StringComparison.Ordinal) ? "Tiles/Textures/" + png[6..] : "Sprites/" + png[8..];
    private Texture2D Texture(string png) => ResourceLoader.Load<Texture2D>(output + "/" + TexturePath(png)) ?? throw new InvalidDataException(png + ": Godot Texture2D import missing");
    private T Save<T>(T resource, string relative) where T : Resource
    {
        string uri = output + "/" + relative;
        string full = ProjectSettings.GlobalizePath(uri);
        Require(!File.Exists(full), "native resource already exists: " + relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        Require(ResourceSaver.Save(resource, uri) == Error.Ok, "could not save native resource: " + relative);
        resource.TakeOverPath(uri);
        return resource;
    }
    private Godot.Collections.Array<PalFrame> Frames(JsonElement data)
    {
        var frames = new Godot.Collections.Array<PalFrame>();
        foreach (var frame in data.GetProperty("frames").EnumerateArray()) frames.Add(new PalFrame
        {
            Index = Int(frame, "index"), SourceOffset = Int(frame, "sourceOffset"), Width = Int(frame, "width"), Height = Int(frame, "height"),
            Indices = frame.GetProperty("indices").GetBytesFromBase64(), Alpha = frame.GetProperty("alpha").GetBytesFromBase64(), Texture = Texture(Text(frame, "png"))
        });
        return frames;
    }
    // Descriptive source/coordinate metadata becomes native Variant dictionaries, not JSON strings.
    internal static Dictionary Metadata(JsonElement element)
    {
        var result = new Dictionary();
        foreach (var p in element.EnumerateObject()) result[p.Name] = Value(p.Value);
        return result;
    }
    private static Variant Value(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => Metadata(element),
        JsonValueKind.Array => new Godot.Collections.Array(element.EnumerateArray().Select(Value)),
        JsonValueKind.String => element.GetString()!,
        JsonValueKind.Number => element.GetInt64(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => default,
        _ => throw new InvalidDataException("unsupported metadata value")
    };
    private void SaveNative(M1ImportBundle b)
    {
        var p = b.Palette;
        var palette = Save(new PalPalette
        {
            PaletteId = Int(p, "paletteId"), Lighting = Text(p, "lighting"), SelectedLighting = Text(p, "selectedLighting"), Source = Metadata(p.GetProperty("source")),
            Rgb = p.GetProperty("colors").EnumerateArray().SelectMany(c => new[] { (byte)Int(c, "r"), (byte)Int(c, "g"), (byte)Int(c, "b") }).ToArray()
        }, "Palettes/Palette.tres");
        var tiles = Save(new PalTiles { MapId = Int(b.Tiles, "mapId"), Source = Metadata(b.Tiles.GetProperty("source")), Frames = Frames(b.Tiles) }, "Tiles/Tiles.tres");
        var map = b.Map; var cells = map.GetProperty("cells").EnumerateArray().ToArray();
        var nativeMap = Save(new PalMap
        {
            MapId = Int(map, "mapId"), Width = Int(map, "width"), Height = Int(map, "height"), HalfRows = Int(map, "halfRows"), DecodedByteLength = Int(map, "decodedByteLength"), CellOrder = Text(map, "cellOrder"),
            Source = Metadata(map.GetProperty("source")), Coordinates = Metadata(map.GetProperty("coordinates")), PreviewMetadata = Metadata(map.GetProperty("preview")), Preview = Texture("preview.png"), Tiles = tiles,
            RawCells = cells.Select(c => (long)c.GetProperty("raw").GetUInt32()).ToArray(), BottomFrames = cells.Select(c => Int(c, "bottomFrame")).ToArray(),
            TopFrames = cells.Select(c => c.GetProperty("topFrame").ValueKind == JsonValueKind.Null ? -1 : Int(c, "topFrame")).ToArray(),
            BottomHeights = cells.Select(c => Int(c, "bottomHeight")).ToArray(), TopHeights = cells.Select(c => Int(c, "topHeight")).ToArray(),
            Blocked = cells.Select(c => (byte)(c.GetProperty("blocked").GetBoolean() ? 1 : 0)).ToArray()
        }, "Maps/Map.tres");
        var sprites = new System.Collections.Generic.Dictionary<int, PalSprite>();
        foreach (var entry in b.Sprites)
        {
            var s = entry.Value;
            sprites.Add(entry.Key, Save(new PalSprite { SpriteId = entry.Key, DecodedByteLength = Int(s, "decodedByteLength"), FrameCount = Int(s, "frameCount"), Source = Metadata(s.GetProperty("source")), Frames = Frames(s) }, $"Sprites/{entry.Key}.tres"));
        }
        PalSprite? Sprite(JsonElement record) => Int(record, "wSpriteNum") == 0 ? null : sprites[Int(record, "wSpriteNum")];
        var scene = b.Scene;
        var data = new PalSceneData
        {
            SceneId = Int(scene, "sceneId"), MapId = Int(scene, "mapId"), WScriptOnEnter = Int(scene, "wScriptOnEnter"), WScriptOnTeleport = Int(scene, "wScriptOnTeleport"), WEventObjectIndex = Int(scene, "wEventObjectIndex"), NextEventObjectIndex = Int(scene, "nextEventObjectIndex"),
            Source = Metadata(scene.GetProperty("source")), ObjectsSource = Metadata(scene.GetProperty("objectsSource")), Positioning = Metadata(scene.GetProperty("positioning")),
            BundleSha256 = Text(b.Manifest, "bundleSha256"), SpriteIds = scene.GetProperty("spriteIds").EnumerateArray().Select(x => x.GetInt32()).ToArray(), Map = nativeMap, Palette = palette
        };
        foreach (var source in b.Manifest.GetProperty("sources").EnumerateArray()) data.ArchiveSources.Add(Metadata(source));
        foreach (var s in sprites.Values) data.Sprites.Add(s);
        foreach (var member in scene.GetProperty("defaultParty").EnumerateArray()) data.DefaultParty.Add(new PalPartyMember { RoleId = Int(member, "roleId"), WSpriteNum = Int(member, "wSpriteNum"), Sprite = Sprite(member)!, Source = Metadata(member.GetProperty("source")) });
        foreach (var obj in scene.GetProperty("objects").EnumerateArray()) data.Objects.Add(new PalSceneObject
        {
            Id = Int(obj, "id"), SVanishTime = Int(obj, "sVanishTime"), X = Int(obj, "x"), Y = Int(obj, "y"), SLayer = Int(obj, "sLayer"), WTriggerScript = Int(obj, "wTriggerScript"), WAutoScript = Int(obj, "wAutoScript"), SState = Int(obj, "sState"), WTriggerMode = Int(obj, "wTriggerMode"), WSpriteNum = Int(obj, "wSpriteNum"), NSpriteFrames = Int(obj, "nSpriteFrames"), WDirection = Int(obj, "wDirection"), WCurrentFrameNum = Int(obj, "wCurrentFrameNum"), NScriptIdleFrame = Int(obj, "nScriptIdleFrame"), WSpritePtrOffset = Int(obj, "wSpritePtrOffset"), NSpriteFramesAuto = Int(obj, "nSpriteFramesAuto"), WScriptIdleFrameCountAuto = Int(obj, "wScriptIdleFrameCountAuto"),
            InitialFrameIndex = obj.GetProperty("initialFrameIndex").ValueKind == JsonValueKind.Null ? -1 : Int(obj, "initialFrameIndex"), InitiallyVisible = obj.GetProperty("initiallyVisible").GetBoolean(), Sprite = Sprite(obj)!
        });
        Save(data, "Scenes/Scene.tres");
        using var root = new PalMapScene { Name = "MapScene", Data = data };
        using var packed = new PackedScene();
        Require(packed.Pack(root) == Error.Ok, "could not pack native map scene");
        Save(packed, "Scenes/Scene.tscn");
    }
}
