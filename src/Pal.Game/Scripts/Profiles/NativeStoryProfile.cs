using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using Pal.Core;
using Pal.Game.Dialogue;
using Pal.Game.Map;
using Pal.Game.Resources;

namespace Pal.Game.Profiles;

/// <summary>Strict authored story CONTENT binding. Does not start Main, a trigger or a battle.</summary>
public static class NativeStoryProfile
{
    public static CheckedStoryProfile Load(string uri, string language = "zh-Hans")
    {
        try
        {
            NativeExplorationProfile.ValidateUri(uri);
            Require(language is "zh-Hans" or "en", "selected language must be zh-Hans or en");
            var native = LoadResource<PalStoryProfile>(uri);
            Require(native.FormatVersion == 1, "story FormatVersion must be 1");
            Require(!string.IsNullOrWhiteSpace(native.SourceIdentity), "story SourceIdentity is required (author declaration only)");
            NativeSceneLoader.ValidateRoot(native.StaticResources);
            foreach (string path in new[] { native.StaticProfile, native.EventsIndex, native.BattleContentUri, native.DialogueDirectory })
                NativeExplorationProfile.ValidateUri(path);
            Require(IsHash(native.BundleSha256), "story BundleSha256 is invalid");
            Require(native.ResourceSha256 is not null && native.ResourceSha256.Count > 0, "ResourceSha256 is required");
            var hashes = native.ResourceSha256!.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            foreach (string path in hashes.Keys) CheckFile(path, hashes);
            Require(native.Portraits is not null, "Portraits is required");
            var portraitUris = native.Portraits!.ToDictionary(x => x.Key, x => x.Value);
            foreach (var pair in portraitUris)
            {
                Require(pair.Key is > 0 and <= ushort.MaxValue, "Portraits requires nonzero WORD IDs");
                NativeExplorationProfile.ValidateUri(pair.Value);
            }

            CheckFile(native.EventsIndex, hashes);
            var events = TriggerEventLoader.Load(native.EventsIndex);
            // Core has already strictly validated the complete index and all file identifiers.
            using var index = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(native.EventsIndex));
            string directory = native.EventsIndex[..native.EventsIndex.LastIndexOf('/')];
            var eventPaths = index.RootElement.GetProperty("files").EnumerateArray()
                .Select(id => directory + "/" + id.GetString() + ".json").Prepend(native.EventsIndex).ToArray();
            foreach (string path in eventPaths) CheckFile(path, hashes);
            string sceneUri = native.StaticResources + "/Scenes/Scene.tscn";
            string dialogue = native.DialogueDirectory;
            string[] seeds = new[] { sceneUri, native.StaticProfile, native.BattleContentUri,
                dialogue + "/Font.tres", dialogue + "/Language.zh-Hans.tres", dialogue + "/Language.en.tres",
                dialogue + "/Prompt-00.tres", dialogue + "/Prompt-01.tres", dialogue + "/Prompt-02.tres" }
                .Concat(portraitUris.Values).ToArray();
            var dependencies = ContentDependencies(seeds, hashes);
            dependencies.UnionWith(eventPaths);
            Require(dependencies.SetEquals(hashes.Keys), "ResourceSha256 must cover exactly complete events and native content dependencies (no missing or extra keys)");

            TriggerEventEntry entry = events.GetEntry(native.EntryFileId, native.EntryId);
            Require(entry is { OwnerType: "eventObject", Slot: "wTriggerScript", Kind: "trigger" }
                && entry.SourceAddress != 0 && entry.SourceAddress == native.EntryAddress
                && entry.OwnerId == native.OwnerId, "story owner-trigger-entry identity mismatch or disabled entry");
            var content = NativeBattleContent.Load(native.BattleContentUri);
            var font = LoadResource<PalDialogueFont>(dialogue + "/Font.tres");
            Require(!string.IsNullOrWhiteSpace(font.SourceIdentity) && font.FormatVersion == 1 && font.LineHeight == 16,
                "native font identity/version/line height is invalid");
            var prompts = Enumerable.Range(0, 3).Select(i => LoadImage($"{dialogue}/Prompt-{i:D2}.tres")).ToArray();
            var portraits = portraitUris.ToDictionary(x => x.Key, x => LoadImage(x.Value));
            var packages = new Dictionary<string, DialogueLanguagePackage>(StringComparer.Ordinal);
            var requirements = new Dictionary<string, TriggerEntryRequirements>(StringComparer.Ordinal);
            foreach (string id in new[] { "zh-Hans", "en" })
            {
                var resource = LoadResource<PalDialogueLanguage>($"{dialogue}/Language.{id}.tres");
                Require(resource.Language == id && !string.IsNullOrWhiteSpace(resource.SourceIdentity), resource.ResourcePath + ": language identity mismatch");
                try
                {
                    packages.Add(id, resource.ToCorePackage());
                    requirements.Add(id, TriggerScriptSession.ValidateEntry(events, packages[id], content, native.EntryFileId, native.EntryId));
                }
                catch (InvalidDataException error) { throw new InvalidDataException(resource.ResourcePath + ": " + error.Message, error); }
            }
            Require(requirements.Values.SelectMany(x => x.PortraitIds).ToHashSet().SetEquals(portraits.Keys),
                "Portraits must cover exactly the selected entry's nonzero portrait IDs");

            Node? node = null;
            try
            {
                node = LoadResource<PackedScene>(sceneUri).Instantiate();
                Require(node is PalMapScene, sceneUri + ": expected PalMapScene root");
                var scene = ((PalMapScene)node).Data;
                Require(scene is not null && scene.BundleSha256 == native.BundleSha256, "story scene BundleSha256 mismatch");
                CheckedExplorationProfile exploration = NativeExplorationProfile.Load(native.StaticProfile, scene!);
                Require(content.InitialRole.RoleId == 0, "story battle role must match static leader role0");
                Require(scene!.Objects.SingleOrDefault(x => x.Id == entry.OwnerId)?.WTriggerScript == entry.SourceAddress,
                    "story entry does not match bound native scene owner trigger");
                Require(scene.Palette is not null && scene.Palette.Rgb is { Length: 768 }, "story requires the native 256-color scene palette");
                byte[] rgb = scene.Palette!.Rgb;
                var palette = Enumerable.Range(0, 256).Select(i => new Color(rgb[i * 3] / 255f, rgb[i * 3 + 1] / 255f, rgb[i * 3 + 2] / 255f)).ToArray();
                foreach (var requirement in requirements.Values)
                foreach (var display in requirement.DisplayBlocks)
                {
                    try { PalDialogueDisplay.ValidateContent(display.Content, DialogueTextTokenizer.DefaultColor, font, null, prompts, palette); }
                    catch (InvalidDataException error) { throw new InvalidDataException($"{display.MessageReferences[0].Position}: {error.Message}", error); }
                }
                // Detect byte changes during validation, not future edits/import-cache or GPU changes.
                foreach (string path in hashes.Keys) CheckFile(path, hashes);
                return new(native.StaticResources, native.StaticProfile, native.BundleSha256, native.SourceIdentity,
                    entry, events, packages[language], content, requirements[language], exploration, font, prompts, portraits, palette, hashes);
            }
            finally { node?.Free(); }
        }
        catch (InvalidDataException error) { throw new InvalidDataException(uri + ": " + error.Message, error); }
        catch (Exception error) when (error is ArgumentException or NullReferenceException or InvalidCastException or InvalidOperationException or JsonException)
        { throw new InvalidDataException(uri + ": malformed native story content: " + error.Message, error); }
    }

    private static T LoadResource<T>(string uri) where T : Resource
    {
        NativeExplorationProfile.ValidateUri(uri);
        Require(Godot.FileAccess.FileExists(uri), uri + ": missing native resource; prepare it explicitly");
        return ResourceLoader.Load(uri, cacheMode: ResourceLoader.CacheMode.IgnoreDeep) as T
            ?? throw new InvalidDataException(uri + ": expected " + typeof(T).Name);
    }

    private static PalDialogueIndexedImage LoadImage(string uri)
    {
        var image = LoadResource<PalDialogueIndexedImage>(uri);
        Require(!string.IsNullOrWhiteSpace(image.SourceIdentity), uri + ": missing source identity");
        try { image.Validate(); }
        catch (InvalidDataException error) { throw new InvalidDataException(uri + ": " + error.Message, error); }
        return image;
    }

    // Only the authored/prepared data graph. Scripts/engine and generated .godot import outputs are not authenticated.
    private static HashSet<string> ContentDependencies(IEnumerable<string> seeds, IReadOnlyDictionary<string, string> hashes)
    {
        var complete = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<(string Path, bool Exit)>();
        foreach (string seed in seeds) pending.Push((seed, false));
        while (pending.TryPop(out var item))
        {
            string path = item.Path;
            if (item.Exit) { active.Remove(path); complete.Add(path); continue; }
            NativeExplorationProfile.ValidateUri(path);
            if (complete.Contains(path)) continue;
            Require(active.Add(path), path + ": cyclic native content dependency");
            CheckFile(path, hashes);
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
                CheckFile(import, hashes);
                complete.Add(import);
            }
            pending.Push((path, true));
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
                NativeExplorationProfile.ValidateUri(target);
                if (Path.GetExtension(target) is ".cs" or ".gd") continue;
                pending.Push((target, false));
            }
        }
        return complete;
    }

    private static void CheckFile(string path, IReadOnlyDictionary<string, string> hashes)
    {
        NativeExplorationProfile.ValidateUri(path);
        Require(hashes.TryGetValue(path, out string? expected) && IsHash(expected)
            && Godot.FileAccess.FileExists(path) && Godot.FileAccess.GetSha256(path) == expected,
            path + ": story resource SHA-256 mismatch, missing hash or missing resource");
    }
    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidDataException(message); }
}

/// <summary>Core projections are immutable. Native resources are this load's isolated instances;
/// callers must treat prepared resources as read-only. No hot reload or clone-on-get contract.</summary>
public sealed class CheckedStoryProfile
{
    public string StaticResources { get; }
    public string StaticProfile { get; }
    public string BundleSha256 { get; }
    public string SourceIdentity { get; }
    public TriggerEventEntry Entry { get; }
    public TriggerEventCollection Events { get; }
    public DialogueLanguagePackage Language { get; }
    public TriggerBattleContent BattleContent { get; }
    public TriggerEntryRequirements Requirements { get; }
    public CheckedExplorationProfile Exploration { get; }
    public PalDialogueFont Font { get; }
    public IReadOnlyList<PalDialogueIndexedImage> Prompts { get; }
    public IReadOnlyDictionary<int, PalDialogueIndexedImage> Portraits { get; }
    public IReadOnlyList<Color> Palette { get; }
    public IReadOnlyDictionary<string, string> ResourceSha256 { get; }

    internal CheckedStoryProfile(string staticResources, string staticProfile, string bundleSha256, string sourceIdentity,
        TriggerEventEntry entry, TriggerEventCollection events, DialogueLanguagePackage language, TriggerBattleContent battleContent,
        TriggerEntryRequirements requirements, CheckedExplorationProfile exploration, PalDialogueFont font,
        PalDialogueIndexedImage[] prompts, Dictionary<int, PalDialogueIndexedImage> portraits, Color[] palette, Dictionary<string, string> hashes)
    {
        StaticResources = staticResources; StaticProfile = staticProfile; BundleSha256 = bundleSha256; SourceIdentity = sourceIdentity;
        Entry = entry; Events = events; Language = language; BattleContent = battleContent; Requirements = requirements;
        Exploration = exploration; Font = font; Prompts = Array.AsReadOnly(prompts.ToArray());
        Portraits = new ReadOnlyDictionary<int, PalDialogueIndexedImage>(new Dictionary<int, PalDialogueIndexedImage>(portraits));
        Palette = Array.AsReadOnly(palette.ToArray());
        ResourceSha256 = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(hashes, StringComparer.Ordinal));
    }
}
