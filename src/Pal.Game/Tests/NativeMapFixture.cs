using System;
using System.IO;
using Godot;
using Pal.Game.Resources;

namespace Pal.Game.Tests;

// Independent synthetic native data, not a second M1 JSON fixture.
internal sealed class NativeMapFixture : IDisposable
{
    public string Root { get; } = "res://Assets/RuntimeTests/" + Guid.NewGuid().ToString("N");
    public PalMapScene Scene { get; }
    public NativeMapFixture(bool useMainViewCells = false)
    {
        var map = new PalMap { MapId = 12, Tiles = new PalTiles { MapId = 12 } };
        for (int i = 0; i < 4; i++)
        {
            using var image = Image.CreateEmpty(40 + i, 24 + i, false, Image.Format.Rgba8);
            image.Fill(new Color(i == 0 ? 1 : 0, i == 1 ? 1 : 0, i == 2 ? 1 : 0, 1));
            map.Tiles.Frames.Add(new PalFrame { Index = i, Width = 40 + i, Height = 24 + i,
                Texture = ImageTexture.CreateFromImage(image) });
        }
        const int cells = 16384;
        map.RawCells = new long[cells]; map.BottomFrames = new int[cells]; map.TopFrames = new int[cells];
        Array.Fill(map.TopFrames, -1);
        map.BottomHeights = new int[cells]; map.TopHeights = new int[cells]; map.Blocked = new byte[cells];
        // Map-rule tests use (x3,y2). Shipped Main uses explicit T2 view(512,512),
        // so its literal special cells are (x19,y34), h0/h1, then (x20,y34,h0).
        // Both examples put the special bitmap at screen(80,24); neither is a party spawn.
        int cell = useMainViewCells ? 4390 : 262;
        map.BottomFrames[cell] = 1; map.BottomFrames[cell + 1] = 2; map.BottomFrames[cell + 2] = 3;
        map.TopFrames[cell] = 2; map.TopFrames[cell + 1] = 3;
        map.BottomHeights[cell] = 9; map.TopHeights[cell] = 11;
        map.RawCells[cell] = 0x0B030901; map.RawCells[cell + 1] = 0x00040002; map.RawCells[cell + 2] = 3;
        Scene = new PalMapScene { Name = "SyntheticMap", Data = new PalSceneData { SceneId = 2, MapId = 12, Map = map } };
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(Root + "/Scenes"));
    }
    public string Save(Node? root = null)
    {
        using var packed = new PackedScene();
        if (packed.Pack(root ?? Scene) != Error.Ok || ResourceSaver.Save(packed, Root + "/Scenes/Scene.tscn") != Error.Ok)
            throw new InvalidOperationException("Could not save synthetic native scene");
        return Root + "/Scenes/Scene.tscn";
    }
    public void Dispose()
    {
        Scene.Free();
        Directory.Delete(ProjectSettings.GlobalizePath(Root), true);
    }
}
