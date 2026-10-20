using System;
using Godot;
using Pal.Game.Resources;

namespace Pal.Game.Tests;

// Author native walking frames without changing the static fixture's alias/count contract.
internal sealed class ExplorationFixture : IDisposable
{
    public NativeProfileFixture Native { get; } = new();
    public ExplorationFixture(int raw = 0)
    {
        Native.Profile.Leader.WalkFramesWord = raw;
        var sprite = Native.Scene.Sprites[0];
        sprite.FrameCount = raw == 4 ? 16 : 12;
        sprite.Frames.Clear();
        for (int i = 0; i < sprite.FrameCount; i++)
        {
            using var image = Image.CreateEmpty(11 + i, 19 + i, false, Image.Format.Rgba8);
            image.Fill(i % 3 == 0 ? Colors.Magenta : i % 3 == 1 ? Colors.Cyan : Colors.Yellow);
            sprite.Frames.Add(new PalFrame { Index = i, Width = 11 + i, Height = 19 + i,
                Texture = ImageTexture.CreateFromImage(image) });
        }
    }
    public void AddDynamicCoverExample()
    {
        var map = Native.Scene.Map;
        Array.Fill(map.RawCells, 0); Array.Fill(map.BottomFrames, 0); Array.Fill(map.TopFrames, -1);
        Array.Fill(map.BottomHeights, 0); Array.Fill(map.TopHeights, 0);
        map.Tiles.Frames[0] = Frame(0, 32, 16, Colors.Red);
        map.Tiles.Frames[1] = Frame(1, 32, 16, Colors.Green, cover: true);
        map.Tiles.Frames[2] = Frame(2, 32, 16, Colors.Blue, cover: true, top: true);
        // Same independent native height3 two-layer example as the accepted static fixture.
        map.RawCells[2452] = 0x03030301; map.BottomFrames[2452] = 1; map.TopFrames[2452] = 2;
        map.BottomHeights[2452] = 3; map.TopHeights[2452] = 3;
        map.RawCells[2582] = 0x00030000; map.TopFrames[2582] = 2;
        Native.Scene.Sprites[0].Frames[0] = Frame(0, 11, 19, Colors.Magenta, leader: true);
        var obj = Native.AddObject();
        obj.X = 336; obj.Y = 320;
        obj.WTriggerScript = 123; obj.WAutoScript = 456; obj.NScriptIdleFrame = 7;
        obj.NSpriteFramesAuto = 9; obj.WScriptIdleFrameCountAuto = 11;
    }

    private static PalFrame Frame(int index, int width, int height, Color color,
        bool cover = false, bool top = false, bool leader = false)
    {
        using var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        image.Fill(color);
        if (cover)
            for (int y = 10; y < 16; y++) for (int x = 13; x < 18; x++) image.SetPixel(x, y, Colors.Transparent);
        if (top)
            for (int y = 0; y < 16; y++) for (int x = 19; x < 22; x++) image.SetPixel(x, y, Colors.Transparent);
        if (leader) image.SetPixel(3, 4, Colors.Transparent);
        return new PalFrame { Index = index, Width = width, Height = height, Texture = ImageTexture.CreateFromImage(image) };
    }

    public void Dispose() => Native.Dispose();
}
