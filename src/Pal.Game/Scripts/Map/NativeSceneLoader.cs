using System;
using System.IO;
using System.Linq;
using Godot;
using Pal.Game.Resources;

namespace Pal.Game.Map;

internal static class NativeSceneLoader
{
    internal static string ResourceRoot(string[] arguments)
    {
        const string usage = "--m2-resources requires one normalized res://Assets (or child directory) URI";
        if (arguments.Length == 0) return "res://Assets";
        if (arguments.Length != 2 || arguments[0] != "--m2-resources") throw new InvalidDataException(usage);
        string root = arguments[1];
        if (!(root == "res://Assets" || root.StartsWith("res://Assets/", StringComparison.Ordinal)))
            throw new InvalidDataException(usage);
        foreach (string segment in root[6..].Split('/'))
        {
            if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith('.') || segment != segment.Trim()
                || segment.Any(c => char.IsControl(c) || "\\:%?#*\"<>|".Contains(c)))
                throw new InvalidDataException(usage);
        }
        return root;
    }

    internal static PalMapScene Load(string root)
    {
        string path = root + "/Scenes/Scene.tscn";
        if (!ResourceLoader.Exists(path)) throw new InvalidDataException(path + ": native scene missing; prepare resources explicitly");
        var packed = ResourceLoader.Load<PackedScene>(path)
            ?? throw new InvalidDataException(path + ": expected PackedScene");
        var node = packed.Instantiate();
        if (node is PalMapScene map) return map;
        node?.Free();
        throw new InvalidDataException(path + ": expected PalMapScene root");
    }
}
