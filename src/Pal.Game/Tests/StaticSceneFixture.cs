using System;
using Godot;
using Pal.Game.Resources;

namespace Pal.Game.Tests;

// Worked artificial scene shared with ObserveMain's literal state/pixel oracle.
internal sealed class StaticSceneFixture : IDisposable
{
    public NativeProfileFixture Profile { get; } = new();
    public StaticSceneFixture()
    {
        Profile.Profile.Start.StepPhase = 3;
        var map = Profile.Scene.Map;
        Array.Fill(map.RawCells, 0); Array.Fill(map.BottomFrames, 0); Array.Fill(map.TopFrames, -1);
        Array.Fill(map.BottomHeights, 0); Array.Fill(map.TopHeights, 0);
        map.Tiles.Frames[0] = Frame(0, 32, 16, Colors.Red);
        map.Tiles.Frames[1] = Frame(1, 32, 16, Colors.Green, coverHoles: true);
        map.Tiles.Frames[2] = Frame(2, 32, 16, Colors.Blue, coverHoles: true, topHole: true);
        // (x10,y19,h0): both layers height3; (x11,y20,h0): top height0 behind actors.
        const int cover = 2452, behind = 2582;
        map.RawCells[cover] = 0x03030301; map.BottomFrames[cover] = 1; map.TopFrames[cover] = 2;
        map.BottomHeights[cover] = 3; map.TopHeights[cover] = 3;
        map.RawCells[behind] = 0x00030000; map.TopFrames[behind] = 2;
        Profile.Scene.Sprites[0].Frames[0] = Frame(0, 11, 19, Colors.Magenta, leaderHole: true);
        var sprite = new PalSprite { SpriteId = 23, FrameCount = 3, DecodedByteLength = 100,
            Source = new Godot.Collections.Dictionary { ["archive"] = "mgo.mkf", ["chunk"] = 23 } };
        sprite.Frames.Add(Frame(0, 9, 13, Colors.Orange));
        sprite.Frames.Add(Frame(1, 15, 17, Colors.Yellow));
        sprite.Frames.Add(Frame(2, 21, 29, Colors.Cyan));
        Profile.Scene.Sprites.Add(sprite); Profile.Scene.SpriteIds = [17, 23];
        var first = Profile.AddObject();
        first.X = 320; first.Y = 320; first.Sprite = sprite; first.WSpriteNum = 23;
        first.NSpriteFrames = 3; first.WCurrentFrameNum = 3; first.InitialFrameIndex = 0;
        first.WTriggerScript = 123; first.WAutoScript = 456; first.NScriptIdleFrame = 7;
        first.NSpriteFramesAuto = 9; first.WScriptIdleFrameCountAuto = 11; first.WTriggerMode = 2;
        var second = Profile.AddObject(2);
        second.X = 336; second.Y = 320; second.Sprite = sprite; second.WSpriteNum = 23;
        second.NSpriteFrames = 3; second.WCurrentFrameNum = 1; second.SLayer = 1;
        var hidden = Profile.AddObject(3); hidden.SState = -1; hidden.SVanishTime = 12;
        var noSprite = Profile.AddObject(4); noSprite.WSpriteNum = 0; noSprite.Sprite = null!;
        noSprite.InitialFrameIndex = -1; noSprite.SState = 2; noSprite.X = 600; noSprite.Y = 600;
        Profile.Scene.WScriptOnEnter = 100; Profile.Scene.WScriptOnTeleport = 200;
    }
    private static PalFrame Frame(int index, int width, int height, Color color,
        bool coverHoles = false, bool topHole = false, bool leaderHole = false)
    {
        using var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        image.Fill(color);
        if (coverHoles)
            for (int y = 10; y < 16; y++) for (int x = 13; x < 18; x++) image.SetPixel(x, y, Colors.Transparent);
        if (topHole)
            for (int y = 0; y < 16; y++) for (int x = 19; x < 22; x++) image.SetPixel(x, y, Colors.Transparent);
        if (leaderHole) image.SetPixel(3, 4, Colors.Transparent);
        return new PalFrame { Index = index, Width = width, Height = height, Texture = ImageTexture.CreateFromImage(image) };
    }
    public void Dispose() => Profile.Dispose();
}
