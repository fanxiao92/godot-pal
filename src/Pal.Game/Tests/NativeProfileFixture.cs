using System;
using System.IO;
using Godot;
using Pal.Game.Resources;

namespace Pal.Game.Tests;

internal sealed class NativeProfileFixture : IDisposable
{
    private readonly NativeMapFixture map = new();
    // Deliberately outside Assets: --m2-profile accepts normalized project resource URIs.
    public string Root { get; } = "res://Tests/RuntimeProfiles/" + Guid.NewGuid().ToString("N");
    public string Uri => Root + "/Profile.tres";
    public PalSceneData Scene => map.Scene.Data;
    public PalExplorationProfile Profile { get; }
    public NativeProfileFixture()
    {
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(Root));
        Scene.BundleSha256 = new string('a', 64);
        Scene.ArchiveSources.Add(new Godot.Collections.Dictionary
            { ["name"] = "data.mkf", ["sha256"] = new string('b', 64), ["length"] = 900 });
        var sprite = new PalSprite { SpriteId = 17, FrameCount = 1, DecodedByteLength = 100,
            Source = new Godot.Collections.Dictionary { ["archive"] = "mgo.mkf", ["chunk"] = 17 } };
        using var image = Image.CreateEmpty(11, 19, false, Image.Format.Rgba8);
        image.Fill(Colors.Magenta);
        sprite.Frames.Add(new PalFrame { Index = 0, Width = 11, Height = 19,
            Texture = ImageTexture.CreateFromImage(image) });
        Scene.SpriteIds = [17]; Scene.Sprites.Add(sprite);
        Scene.DefaultParty.Add(new PalPartyMember { RoleId = 0, WSpriteNum = 17, Sprite = sprite,
            Source = new Godot.Collections.Dictionary { ["archive"] = "data.mkf", ["chunk"] = 3, ["spriteByteOffset"] = 24 } });
        Profile = new PalExplorationProfile { FormatVersion = 1, SceneId = 2, BundleSha256 = Scene.BundleSha256,
            Start = new PalExplorationStart { X = 320, Y = 320, Direction = 0, Layer = 0, StepPhase = 0 },
            Leader = new PalExplorationLeader { RoleId = 0, WalkFramesWord = 0,
                WalkFramesSource = new PalWalkFramesSource { Archive = "data.mkf", Sha256 = new string('b', 64),
                    Chunk = 3, ByteOffset = 768, RoleId = 0, Field = "rgwWalkFrames" } } };
    }
    public PalSceneObject AddObject(int id = 1)
    {
        var obj = new PalSceneObject { Id = id, X = 600, Y = 600, SState = 1, WSpriteNum = 17,
            Sprite = Scene.Sprites[0], NSpriteFrames = 1, InitialFrameIndex = 0, InitiallyVisible = true };
        Scene.Objects.Add(obj); Scene.NextEventObjectIndex = Scene.WEventObjectIndex + Scene.Objects.Count;
        return obj;
    }
    public void AddAliasFrame(int index)
    {
        var original = Scene.Sprites[0].Frames[0];
        Scene.Sprites[0].Frames.Add(new PalFrame { Index = index, SourceOffset = original.SourceOffset,
            Width = original.Width, Height = original.Height, Texture = original.Texture });
    }
    public PalSceneData Save()
    {
        SaveResource(Profile, Uri); SaveResource(Scene, Root + "/Scene.tres");
        return ResourceLoader.Load<PalSceneData>(Root + "/Scene.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep)
            ?? throw new InvalidOperationException("native scene roundtrip failed");
    }
    public static void SaveResource(Resource resource, string uri)
    {
        if (ResourceSaver.Save(resource, uri) != Error.Ok) throw new InvalidOperationException("native save failed: " + uri);
    }
    public void Dispose()
    {
        map.Dispose();
        Directory.Delete(ProjectSettings.GlobalizePath(Root), true);
    }
}
