using System;
using System.IO;
using System.Linq;
using Godot;
using Pal.Game.Resources;
using Pal.Game.Profiles;

namespace Pal.Game.Map;

internal static class NativeSceneLoader
{
    internal static (string Root, string Profile, bool StaticAcceptance) Arguments(string[] arguments)
    {
        const string usage = "expected unique --m2-resources/--m2-profile or --m3-static-resources/--m3-static-profile arguments";
        string root = "res://Assets", profile = NativeExplorationProfile.DefaultUri;
        bool hasRoot = false, hasProfile = false;
        string? selectedMode = null;
        for (int i = 0; i < arguments.Length; i += 2)
        {
            string flag = arguments[i];
            if (i + 1 == arguments.Length || arguments[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw new InvalidDataException(usage + ": missing value for " + flag);
            switch (flag)
            {
                case "--m2-resources" when !hasRoot && (selectedMode == null || selectedMode == "m2"):
                    root = arguments[i + 1]; hasRoot = true; selectedMode = "m2"; break;
                case "--m2-profile" when !hasProfile && (selectedMode == null || selectedMode == "m2"):
                    profile = arguments[i + 1]; hasProfile = true; selectedMode = "m2"; break;
                case "--m3-static-resources" when !hasRoot && (selectedMode == null || selectedMode == "m3-static"):
                    root = arguments[i + 1]; hasRoot = true; selectedMode = "m3-static"; break;
                case "--m3-static-profile" when !hasProfile && (selectedMode == null || selectedMode == "m3-static"):
                    profile = arguments[i + 1]; hasProfile = true; selectedMode = "m3-static"; break;
                default: throw new InvalidDataException(usage + ": duplicate, mixed, or unknown " + flag);
            }
        }
        ValidateRoot(root);
        try { NativeExplorationProfile.ValidateUri(profile); }
        catch (InvalidDataException e) { throw new InvalidDataException("--m2-profile " + profile + ": " + e.Message, e); }
        bool staticAcceptance = selectedMode == "m3-static";
        if (staticAcceptance && (!hasRoot || !hasProfile))
            throw new InvalidDataException("M3 static acceptance requires both --m3-static-resources and --m3-static-profile");
        return (root, profile, staticAcceptance);
    }

    private static void ValidateRoot(string root)
    {
        const string usage = "--m2-resources requires one normalized res://Assets (or child directory) URI";
        if (!(root == "res://Assets" || root.StartsWith("res://Assets/", StringComparison.Ordinal)))
            throw new InvalidDataException(usage);
        foreach (string segment in root[6..].Split('/'))
        {
            if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith('.') || segment != segment.Trim()
                || segment.Any(c => char.IsControl(c) || "\\:%?#*\"<>|".Contains(c)))
                throw new InvalidDataException(usage);
        }
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
