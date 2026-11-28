using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Godot;

namespace Pal.Game.Resources;

public static class NativeContentDependencies
{
    public const string DefaultExplorationProfileUri = "res://Assets/Profiles/Exploration.tres";

    public static void ValidateUri(string profileUri)
    {
        Require(profileUri != null && profileUri.StartsWith("res://", StringComparison.Ordinal), "profile requires a normalized res:// resource URI");
        foreach (string segment in profileUri[6..].Split('/'))
            Require(segment.Length > 0 && segment is not ("." or "..") && !segment.EndsWith('.') && segment == segment.Trim()
                && !segment.Any(c => char.IsControl(c) || "\\:%?#*\"<>|".Contains(c)), "profile requires a normalized res:// resource URI");
    }

    // Only the authored/prepared data graph. Scripts/engine and generated .godot import outputs are not authenticated.
    public static HashSet<string> ContentDependencies(IEnumerable<string> seeds, IReadOnlyDictionary<string, string>? hashes = null)
    {
        var complete = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<(string Path, bool Exit)>();
        foreach (string seed in seeds) pending.Push((seed, false));
        while (pending.TryPop(out var item))
        {
            string path = item.Path;
            if (item.Exit) { active.Remove(path); complete.Add(path); continue; }
            ValidateUri(path);
            if (complete.Contains(path)) continue;
            Require(active.Add(path), path + ": cyclic native content dependency");
            if (hashes is not null) CheckFile(path, hashes);
            else Require(Godot.FileAccess.FileExists(path), path + ": missing native resource; prepare it explicitly");
            string extension = Path.GetExtension(path);
            Require(extension is ".tres" or ".res" or ".tscn" or ".scn" or ".png" or ".svg" or ".jpg" or ".jpeg" or ".webp" or ".bmp",
                path + ": unsupported native content dependency type");
            Require(!path.StartsWith("res://.godot/", StringComparison.Ordinal), path + ": generated import cache is not authored content");
            // Import remap/settings affect which prepared texture is used; hash their bytes,
            // but never claim the generated .godot destination as authenticated content.
            if (extension is ".png" or ".svg" or ".jpg" or ".jpeg" or ".webp" or ".bmp")
            {
                string import = path + ".import";
                Require(Godot.FileAccess.FileExists(import), import + ": missing prepared texture import remap; import explicitly");
                if (hashes is not null) CheckFile(import, hashes);
                complete.Add(import);
            }
            pending.Push((path, true));
            foreach (string target in Dependencies(path)) pending.Push((target, false));
        }
        return complete;
    }

    public static void CheckFile(string path, IReadOnlyDictionary<string, string> hashes)
    {
        ValidateUri(path);
        Require(hashes.TryGetValue(path, out string? expected) && IsHash(expected)
            && Godot.FileAccess.FileExists(path) && Godot.FileAccess.GetSha256(path) == expected,
            path + ": resource SHA-256 mismatch, missing hash or missing resource");
    }
    public static bool IsHash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static IEnumerable<string> Dependencies(string path)
    {
        foreach (string dependency in ResourceLoader.GetDependencies(path))
        {
            string[] parts = dependency.Split("::", StringSplitOptions.None);
            string target;
            if (parts.Length == 1) target = parts[0];
            else
            {
                Require(parts.Length == 3 && parts[1] == "" && parts[0].StartsWith("uid://", StringComparison.Ordinal), path + ": unsupported dependency encoding: " + dependency);
                target = parts[2];
                long uid = ResourceUid.TextToId(parts[0]);
                Require(uid != -1, path + ": invalid dependency UID");
                Require(!ResourceUid.HasId(uid) || ResourceUid.GetIdPath(uid) == target, path + ": dependency UID/fallback identity mismatch");
            }
            ValidateUri(target);
            if (Path.GetExtension(target) is ".cs" or ".gd") continue;
            yield return target;
        }
    }

    private static void Require([DoesNotReturnIf(false)] bool valid, string message) { if (!valid) throw new InvalidDataException(message); }
}
