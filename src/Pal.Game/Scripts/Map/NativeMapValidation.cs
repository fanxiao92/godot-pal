using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics.CodeAnalysis;
using Godot;
using Pal.Game.Resources;

namespace Pal.Game.Map;

internal static class NativeMapValidation
{
    internal static Dictionary<int, Texture2D> Textures(PalSceneData data)
    {
        Require(data != null && data.Map != null, "PalSceneData.Map is required");
        var map = data!.Map;
        Require(data.SceneId > 0 && data.SceneId <= ushort.MaxValue, "invalid SceneId");
        Require(map.MapId > 0 && map.MapId <= ushort.MaxValue && data.MapId == map.MapId
            && map.Tiles != null && map.Tiles.MapId == map.MapId, "scene/map/tiles MapId mismatch");
        Require(map.Width == 64 && map.Height == 128 && map.HalfRows == 2 && map.CellOrder == "[y,x,h]"
            && map.DecodedByteLength == 65536, "map must use fixed 64x128x2 [y,x,h] array structure");
        Require(map.RawCells.Length == 16384, "invalid RawCells array length");
        Require(map.BottomFrames.Length == 16384, "invalid BottomFrames array length");
        Require(map.TopFrames.Length == 16384, "invalid TopFrames array length");
        Require(map.BottomHeights.Length == 16384 && map.TopHeights.Length == 16384 && map.Blocked.Length == 16384,
            "invalid map heights/blocked array length");
        var textures = new Dictionary<int, Texture2D>();
        Require(map.Tiles!.Frames.Count > 0 && map.Tiles.Frames.Count <= 4096, "invalid tile frame array length");
        foreach (var frame in map.Tiles.Frames)
        {
            Require(frame != null && frame.Index >= 0 && frame.Index < 4096 && !textures.ContainsKey(frame.Index), "invalid or duplicate tile frame index");
            Require(frame!.Texture != null, $"tile frame {frame.Index}: texture is required");
            Require(frame.Width > 0 && frame.Height > 0 && frame.Texture!.GetWidth() == frame.Width
                && frame.Texture.GetHeight() == frame.Height, $"tile frame {frame.Index}: texture dimensions mismatch");
            textures.Add(frame.Index, frame.Texture!);
        }
        for (int i = 0; i < 16384; i++)
        {
            Require(textures.ContainsKey(map.BottomFrames[i]), $"cell {i}: invalid bottom frame {map.BottomFrames[i]}");
            Require(map.TopFrames[i] == -1 || textures.ContainsKey(map.TopFrames[i]), $"cell {i}: invalid top frame {map.TopFrames[i]}");
            long raw = map.RawCells[i];
            Require(raw >= 0 && raw <= uint.MaxValue, $"cell {i}: invalid raw cell DWORD");
            int bottom = (int)(raw & 255) | (int)((raw >> 4) & 256);
            long topWord = raw >> 16;
            int top = ((int)(topWord & 255) | (int)((topWord >> 4) & 256)) - 1;
            Require(map.BottomFrames[i] == bottom && map.TopFrames[i] == top
                && map.BottomHeights[i] == ((raw >> 8) & 15) && map.TopHeights[i] == ((raw >> 24) & 15)
                && map.Blocked[i] == ((raw >> 13) & 1), $"cell {i}: raw cell and decoded fields disagree");
        }
        return textures;
    }

    private static void Require([DoesNotReturnIf(false)] bool valid, string diagnostic)
    {
        if (!valid) throw new InvalidDataException(diagnostic);
    }
}
