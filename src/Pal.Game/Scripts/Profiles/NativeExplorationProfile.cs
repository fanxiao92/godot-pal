using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Godot;
using Pal.Core;
using Pal.Game.Map;
using Pal.Game.Resources;

namespace Pal.Game.Profiles;

public static class NativeExplorationProfile
{
    public const string DefaultUri = "res://Assets/Profiles/Exploration.tres";

    internal static void ValidateUri(string profileUri)
    {
        Require(profileUri != null && profileUri.StartsWith("res://", StringComparison.Ordinal), "profile requires a normalized res:// resource URI");
        foreach (string segment in profileUri[6..].Split('/'))
            Require(segment.Length > 0 && segment is not ("." or "..") && !segment.EndsWith('.') && segment == segment.Trim()
                && !segment.Any(c => char.IsControl(c) || "\\:%?#*\"<>|".Contains(c)), "profile requires a normalized res:// resource URI");
    }

    public static CheckedExplorationProfile Load(string profileUri, PalSceneData scene)
    {
        ValidateUri(profileUri);
        Require(ResourceLoader.Exists(profileUri), profileUri + ": native profile missing; prepare it explicitly");
        var profile = ResourceLoader.Load(profileUri, cacheMode: ResourceLoader.CacheMode.IgnoreDeep) as PalExplorationProfile;
        Require(profile != null, profileUri + ": expected PalExplorationProfile");
        Require(scene != null, "native PalSceneData is required");
        Require(profile.FormatVersion == 1, "profile FormatVersion must be 1");
        Require(profile.SceneId is > 0 and <= ushort.MaxValue && profile.SceneId == scene.SceneId, "profile SceneId mismatch");
        Require(Hash(profile.BundleSha256) && Hash(scene.BundleSha256) && profile.BundleSha256 == scene.BundleSha256, "profile BundleSha256 mismatch");
        Require(profile.Start != null, "profile Start is required");
        var input = profile.Start;
        Require(input.X is >= short.MinValue and <= ushort.MaxValue && input.Y is >= short.MinValue and <= ushort.MaxValue,
            "profile Start coordinates must fit PAL_POS SHORT/WORD");
        Require(input.Direction is >= 0 and <= 3 && input.StepPhase is >= 0 and <= 3, "profile Direction/StepPhase must be 0..3");
        Require(input.Layer is >= 0 and <= ushort.MaxValue, "profile Layer must be a WORD");
        Require(profile.Leader != null && profile.Leader.RoleId == 0, "profile Leader RoleId must be explicit role0");
        Require(profile.Leader.WalkFramesWord is >= 0 and <= ushort.MaxValue, "profile WalkFramesWord must be an explicit raw WORD");
        var source = profile.Leader.WalkFramesSource;
        Require(source != null && source.Archive == "data.mkf" && source.Chunk == 3 && source.ByteOffset == 768
            && source.RoleId == 0 && source.Field == "rgwWalkFrames" && Hash(source.Sha256), "profile WalkFramesSource invalid");
        Require(scene.ArchiveSources != null, "scene ArchiveSources is required");
        var archives = scene.ArchiveSources.Where(a => Text(a, "name") == "data.mkf").ToArray();
        Require(archives.Length == 1 && Text(archives[0], "sha256") == source.Sha256, "profile DATA full sha256 mismatch");
        var tileTextures = ValidateMap(scene);
        var sprites = ValidateSprites(scene);
        Require(scene.DefaultParty != null && scene.DefaultParty.Count == 1 && scene.DefaultParty[0] != null
            && scene.DefaultParty[0].RoleId == 0, "defaultParty must contain unique role0 only");
        var party = scene.DefaultParty[0];
        Require(Text(party.Source, "archive") == "data.mkf" && Number(party.Source, "chunk") == 3
            && Number(party.Source, "spriteByteOffset") == 24, "defaultParty role0 source mismatch");
        var required = new HashSet<int>();
        var leaderSprite = Reference(party.WSpriteNum, party.Sprite, scene, sprites, required);
        Require(leaderSprite != null, "defaultParty role0 requires a sprite");
        Word(scene.WScriptOnEnter, "WScriptOnEnter"); Word(scene.WScriptOnTeleport, "WScriptOnTeleport");
        int first = Word(scene.WEventObjectIndex, "WEventObjectIndex");
        int end = Word(scene.NextEventObjectIndex, "NextEventObjectIndex");
        Require(scene.Objects != null && end >= first && end - first == scene.Objects.Count, "scene object boundaries mismatch");
        var objects = new List<SceneObject>();
        foreach (var obj in scene.Objects)
        {
            Require(obj != null && obj.Id == first + objects.Count + 1, "scene object ID/order mismatch");
            var sprite = Reference(obj.WSpriteNum, obj.Sprite, scene, sprites, required);
            Require(obj.InitialFrameIndex >= -1 && (sprite == null ? obj.InitialFrameIndex == -1
                : sprite.Frames.ContainsKey(obj.InitialFrameIndex)), "object InitialFrameIndex invalid");
            Word(obj.WTriggerScript, "WTriggerScript"); Word(obj.WAutoScript, "WAutoScript");
            Word(obj.WTriggerMode, "WTriggerMode"); Word(obj.NScriptIdleFrame, "NScriptIdleFrame");
            Word(obj.WSpritePtrOffset, "WSpritePtrOffset"); Word(obj.NSpriteFramesAuto, "NSpriteFramesAuto");
            Word(obj.WScriptIdleFrameCountAuto, "WScriptIdleFrameCountAuto");
            objects.Add(new(Word(obj.Id, "Id"), Word(obj.X, "X"), Word(obj.Y, "Y"),
                Signed(obj.SState, "SState"), Signed(obj.SVanishTime, "SVanishTime"), Signed(obj.SLayer, "SLayer"),
                Word(obj.WSpriteNum, "WSpriteNum"), Word(obj.NSpriteFrames, "NSpriteFrames"),
                Word(obj.WDirection, "WDirection"), Word(obj.WCurrentFrameNum, "WCurrentFrameNum"), sprite));
        }
        Require(required.SetEquals(sprites.Keys), "scene sprites must match complete static dependencies");
        var rules = new StaticSceneRules(new SceneMap(scene.MapId, scene.Map.RawCells.Select(c => checked((uint)c)),
            scene.Map.Tiles.Frames.Select(f => new SceneFrame(f.Index, f.Width, f.Height))), objects);
        var start = profile.Start;
        var leader = new StaticLeader(unchecked((short)start.X), unchecked((short)start.Y), start.Direction, (ushort)start.Layer,
            start.StepPhase, (ushort)profile.Leader.WalkFramesWord, leaderSprite);
        Require(!rules.IsBlocked(leader.X, leader.Y), $"profile start ({leader.X},{leader.Y}) is blocked");
        var neighbors = new List<StartNeighbor>();
        foreach (var (dx, dy) in new[] { (-16, 8), (-16, -8), (16, -8), (16, 8) })
        {
            short x = unchecked((short)(leader.X + dx)), y = unchecked((short)(leader.Y + dy));
            neighbors.Add(new(neighbors.Count, x, y, rules.IsBlocked(x, y)));
        }
        Require(neighbors.Any(n => !n.Blocked), "profile start has no legal neighbor: "
            + string.Join("; ", neighbors.Select(n => $"direction{n.Direction} ({n.X},{n.Y}) blocked={n.Blocked}")));
        StaticScenePlan plan;
        try { plan = rules.BuildPlan(leader); }
        catch (InvalidOperationException e) { throw new InvalidDataException(profileUri + ": " + e.Message, e); }
        var textures = new Dictionary<(bool Tile, int Id, int Frame), Texture2D>();
        foreach (var tile in tileTextures) textures.Add((true, scene.MapId, tile.Key), tile.Value);
        foreach (var sprite in scene.Sprites)
        foreach (var frame in sprite.Frames) textures.Add((false, sprite.SpriteId, frame.Index), frame.Texture);
        return new(rules, leader, plan, neighbors.AsReadOnly(), textures);
    }

    private static Dictionary<int, Texture2D> ValidateMap(PalSceneData scene)
    {
        var map = scene.Map;
        Require(map != null && map.Tiles != null && map.Tiles.Frames != null && map.RawCells != null
            && map.BottomFrames != null && map.TopFrames != null && map.BottomHeights != null
            && map.TopHeights != null && map.Blocked != null, "native map arrays/tiles are required");
        var textures = NativeMapValidation.Textures(scene);
        foreach (var frame in map.Tiles.Frames)
            Require(frame.Width <= ushort.MaxValue && frame.Height <= ushort.MaxValue, "tile dimensions must fit WORDs");
        return textures;
    }

    private static Dictionary<int, SceneSprite> ValidateSprites(PalSceneData scene)
    {
        Require(scene.Sprites != null && scene.SpriteIds != null, "scene sprites are required");
        var sprites = new Dictionary<int, SceneSprite>();
        foreach (var sprite in scene.Sprites)
        {
            Require(sprite != null && sprite.SpriteId is > 0 and <= ushort.MaxValue && !sprites.ContainsKey(sprite.SpriteId), "invalid/duplicate SpriteId");
            Require(Text(sprite.Source, "archive") == "mgo.mkf" && Number(sprite.Source, "chunk") == sprite.SpriteId, "sprite source mismatch");
            Require(sprite.DecodedByteLength is > 0 and <= 8 * 1024 * 1024 && sprite.FrameCount is > 0 and <= 4096
                && sprite.Frames != null, "sprite length/FrameCount invalid");
            var frames = new Dictionary<int, SceneFrame>();
            long pixels = 0;
            foreach (var frame in sprite.Frames)
            {
                Require(frame != null && frame.Index is >= 0 and <= ushort.MaxValue && !frames.ContainsKey(frame.Index), "sprite frame Index invalid/duplicate");
                Require(frame.SourceOffset >= 0 && frame.Width is > 0 and <= ushort.MaxValue && frame.Height is > 0 and <= ushort.MaxValue,
                    "sprite frame size/offset invalid");
                pixels += (long)frame.Width * frame.Height;
                Require(pixels <= 16 * 1024 * 1024 && frame.Texture != null && frame.Texture.GetWidth() == frame.Width
                    && frame.Texture.GetHeight() == frame.Height, "sprite frame texture dimensions/budget mismatch");
                frames.Add(frame.Index, new(frame.Index, frame.Width, frame.Height));
            }
            // Native imports retain extra addressable aliases beyond the nominal frame count.
            Require(Enumerable.Range(0, sprite.FrameCount).All(frames.ContainsKey), "sprite nominal frame missing");
            sprites.Add(sprite.SpriteId, new(sprite.SpriteId, frames.Values));
        }
        Require(scene.SpriteIds.SequenceEqual(sprites.Keys.Order()), "scene SpriteIds/sprite references mismatch");
        return sprites;
    }

    private static SceneSprite? Reference(int id, PalSprite? reference, PalSceneData scene,
        Dictionary<int, SceneSprite> sprites, HashSet<int> required)
    {
        Word(id, "WSpriteNum");
        if (id == 0) { Require(reference == null, "spriteless record has a sprite reference"); return null; }
        Require(reference != null && reference.SpriteId == id && sprites.ContainsKey(id)
            && scene.Sprites.Single(s => s.SpriteId == id) == reference, "native sprite reference mismatch");
        required.Add(id);
        return sprites[id];
    }

    private static ushort Word(int value, string field)
    {
        Require(value is >= 0 and <= ushort.MaxValue, field + " must fit a WORD");
        return (ushort)value;
    }

    private static short Signed(int value, string field)
    {
        Require(value is >= short.MinValue and <= short.MaxValue, field + " must fit a signed SHORT");
        return (short)value;
    }

    private static long Number(Godot.Collections.Dictionary metadata, string field)
    {
        Require(metadata != null && metadata.TryGetValue(field, out var value) && value.VariantType == Variant.Type.Int,
            "source integer field missing/invalid: " + field);
        return metadata[field].AsInt64();
    }

    private static bool Hash(string value) => value != null && value.Length == 64
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string Text(Godot.Collections.Dictionary metadata, string field)
        => metadata != null && metadata.TryGetValue(field, out var value) && value.VariantType == Variant.Type.String
            ? value.AsString() : "";

    private static void Require([DoesNotReturnIf(false)] bool valid, string diagnostic)
    {
        if (!valid) throw new InvalidDataException(diagnostic);
    }
}

public sealed class CheckedExplorationProfile
{
    public StaticSceneRules Rules { get; }
    public StaticLeader Leader { get; }
    public StaticScenePlan Plan { get; }
    public IReadOnlyList<StartNeighbor> Neighbors { get; }
    private readonly Dictionary<(bool Tile, int Id, int Frame), Texture2D> textures;

    internal CheckedExplorationProfile(StaticSceneRules rules, StaticLeader leader, StaticScenePlan plan,
        IReadOnlyList<StartNeighbor> neighbors, Dictionary<(bool Tile, int Id, int Frame), Texture2D> textures)
    {
        Rules = rules; Leader = leader; Plan = plan; Neighbors = neighbors; this.textures = textures;
    }

    // Commands already contain final positions and order; this only resolves their native bitmap.
    public Texture2D TextureFor(SceneDrawCommand command)
    {
        bool tile = command.Source switch
        {
            SceneDrawSource.Background or SceneDrawSource.Cover => true,
            SceneDrawSource.Leader or SceneDrawSource.Object => false,
            _ => throw new InvalidDataException("unknown texture command source")
        };
        return textures.TryGetValue((tile, command.ResourceId, command.FrameIndex), out var texture) ? texture
            : throw new InvalidDataException($"native texture missing: {command.Source}/{command.ResourceId}/{command.FrameIndex}");
    }
}

public sealed record StartNeighbor(int Direction, short X, short Y, bool Blocked);
