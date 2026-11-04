using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;
using Pal.Core;

namespace Pal.Game.Dialogue;

/// <summary>Reads only the complete modern event collection, never an original archive.</summary>
public static class TriggerEventLoader
{
    public static TriggerEventCollection Load(string indexPath)
    {
        indexPath = indexPath.Replace('\\', '/');
        try
        {
            string indexText = ReadUtf8(indexPath);
            using var index = JsonDocument.Parse(indexText);
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonElement value in index.RootElement.GetProperty("files").EnumerateArray())
            {
                string? id = value.GetString();
                // Validate before constructing paths; Core performs the full strict validation.
                if (id is null || !Regex.IsMatch(id, @"^[A-Za-z_][A-Za-z0-9_-]*\z", RegexOptions.CultureInvariant))
                    throw new InvalidDataException("index.json: invalid event file identifier.");
                string directory = indexPath[..(indexPath.LastIndexOf('/') + 1)];
                texts.TryAdd(id, ReadUtf8(directory + id + ".json"));
            }
            return TriggerEventCollection.Parse(indexText, texts);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            throw new InvalidDataException($"{indexPath}: invalid modern event collection: {error.Message}", error);
        }
        catch (InvalidDataException error)
        {
            throw new InvalidDataException($"{indexPath}: {error.Message}", error);
        }
    }

    /// <summary>The index plus every event file it lists, for native dependency-hash closures.
    /// The uri is already validated (forward slashes only) by the caller's profile validation.</summary>
    public static IReadOnlyList<string> DependencyUris(string indexPath)
    {
        string indexText = ReadUtf8(indexPath);
        using var index = JsonDocument.Parse(indexText);
        string directory = indexPath[..(indexPath.LastIndexOf('/') + 1)];
        var uris = new List<string> { indexPath };
        foreach (JsonElement value in index.RootElement.GetProperty("files").EnumerateArray())
            uris.Add(directory + value.GetString() + ".json");
        return uris;
    }

    private static string ReadUtf8(string path)
    {
        if (!Godot.FileAccess.FileExists(path)) throw new InvalidDataException($"Missing modern event file: {path}");
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read)
            ?? throw new InvalidDataException($"Cannot read modern event file {path}: {Godot.FileAccess.GetOpenError()}");
        byte[] bytes = file.GetBuffer((long)file.GetLength());
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException error) { throw new InvalidDataException($"{path}: invalid UTF-8.", error); }
    }
}
