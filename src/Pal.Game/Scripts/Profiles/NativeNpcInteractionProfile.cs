using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using Pal.Game.Map;
using Pal.Game.Resources;

namespace Pal.Game.Profiles;

public static class NativeNpcInteractionProfile
{
    public static PalNpcInteractionProfile Load(string uri)
    {
        NativeExplorationProfile.ValidateUri(uri);
        if (!ResourceLoader.Exists(uri)) throw new InvalidDataException(uri + ": native interaction profile missing; prepare it explicitly");
        var profile = ResourceLoader.Load<PalNpcInteractionProfile>(uri)
            ?? throw new InvalidDataException(uri + ": expected PalNpcInteractionProfile");
        if (profile.FormatVersion != 1) throw new InvalidDataException(uri + ": interaction FormatVersion must be 1");
        NativeSceneLoader.ValidateRoot(profile.StaticResources);
        foreach (string path in new[] { profile.StaticProfile, profile.EventsIndex, profile.DialogueDirectory })
            NativeExplorationProfile.ValidateUri(path);
        if (!Hash(profile.BundleSha256)) throw new InvalidDataException(uri + ": BundleSha256 required");
        CheckFile(profile, profile.EventsIndex);
        using var index = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(profile.EventsIndex));
        string directory = profile.EventsIndex[..profile.EventsIndex.LastIndexOf('/')];
        var required = index.RootElement.GetProperty("files").EnumerateArray()
            .Select(id => directory + "/" + id.GetString() + ".json")
            .Concat(new[] { profile.StaticProfile, profile.EventsIndex })
            .Concat(new[] { "Font.tres", "Language.zh-Hans.tres", "Language.en.tres", "Portrait-059.tres",
                "Prompt-00.tres", "Prompt-01.tres", "Prompt-02.tres" }.Select(name => profile.DialogueDirectory + "/" + name)).ToArray();
        if (profile.ResourceSha256 is null || !required.ToHashSet(StringComparer.Ordinal).SetEquals(profile.ResourceSha256.Keys))
            throw new InvalidDataException(uri + ": ResourceSha256 must cover exactly static profile, complete event files/index and seven native dialogue files");
        foreach (string path in required) CheckFile(profile, path);
        return profile;
    }

    private static bool Hash(string value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void CheckFile(PalNpcInteractionProfile profile, string path)
    {
        NativeExplorationProfile.ValidateUri(path);
        if (profile.ResourceSha256 is null || !profile.ResourceSha256.TryGetValue(path, out string? expected)
            || !Hash(expected) || !Godot.FileAccess.FileExists(path) || Godot.FileAccess.GetSha256(path) != expected)
            throw new InvalidDataException(path + ": interaction resource SHA-256 mismatch or missing resource");
    }
}
