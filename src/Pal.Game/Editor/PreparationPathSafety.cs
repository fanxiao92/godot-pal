using System;
using System.IO;
using System.Security.Cryptography;
using Godot;
using Pal.Game.Map;
using Pal.Game.Profiles;

namespace Pal.Game.Editor;

internal static class PreparationPathSafety
{
    internal static string AssetResourcePath(string uri, bool directory)
    {
        if (!(uri == "res://Assets" || uri.StartsWith("res://Assets/", StringComparison.Ordinal)))
            throw new InvalidDataException("selected static resource must be under res://Assets");
        NativeExplorationProfile.ValidateUri(uri);
        string assets = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ProjectSettings.GlobalizePath("res://Assets")));
        string path = Path.GetFullPath(ProjectSettings.GlobalizePath(uri));
        if (!(path.Equals(assets, Comparison) || path.StartsWith(assets + Path.DirectorySeparatorChar, Comparison)))
            throw new InvalidDataException("selected static resource must remain inside project Assets");
        CheckNoReparse(path);
        if (directory ? !Directory.Exists(path) : !File.Exists(path))
            throw new InvalidDataException("selected static resource is missing: " + uri);
        return path;
    }

    internal static void CheckNoReparse(string path)
    {
        string? cursor = Path.GetFullPath(path);
        while (cursor is not null)
        {
            if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("selected static resource contains a link/reparse point: " + cursor);
            cursor = Path.GetDirectoryName(cursor);
        }
    }

    internal static void CheckTreeNoReparse(string root)
    {
        CheckNoReparse(root);
        foreach (string entry in Directory.EnumerateFileSystemEntries(root))
        {
            FileAttributes attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("selected static resource contains a link/reparse point: " + entry);
            if ((attributes & FileAttributes.Directory) != 0) CheckTreeNoReparse(entry);
        }
    }

    internal static FileStream OpenStableRead(string path)
    {
        CheckNoReparse(path);
        return new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read);
    }

    internal static bool MatchesStableRead(string path, FileStream lease, string expectedHash)
    {
        CheckNoReparse(path);
        using var current = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read);
        return Sha256(lease) == expectedHash && Sha256(current) == expectedHash;
    }

    internal static string Sha256(FileStream stream)
    {
        stream.Position = 0;
        string hash = Convert.ToHexStringLower(SHA256.HashData(stream));
        stream.Position = 0;
        return hash;
    }

    private static StringComparison Comparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
