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
using static Pal.Game.Profiles.NativeContentDependencies;

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
            // Party/multi-enemy capability is bounded by the actually selected closure: the
            // language loop below runs TriggerScriptSession.ValidateEntry, which validates every
            // startBattle team of the selected entry (one or two players and enemy slots) and
            // ignores unselected teams as plain data. Only the party head is bound to the static leader.
            var font = LoadResource<PalDialogueFont>(dialogue + "/Font.tres");
            Require(!string.IsNullOrWhiteSpace(font.SourceIdentity) && font.FormatVersion == 1 && font.LineHeight == 16,
                "native font identity/version/line height is invalid");
            var prompts = Enumerable.Range(0, 3).Select(i => LoadImage($"{dialogue}/Prompt-{i:D2}.tres")).ToArray();
            var portraits = portraitUris.ToDictionary(x => x.Key, x => LoadImage(x.Value));
            var packages = new Dictionary<string, DialogueLanguagePackage>(StringComparer.Ordinal);
            var requirements = new Dictionary<string, TriggerEntryRequirements>(StringComparer.Ordinal);
            IReadOnlyDictionary<int, string>? selectedWords = null;
            foreach (string id in new[] { "zh-Hans", "en" })
            {
                var resource = LoadResource<PalDialogueLanguage>($"{dialogue}/Language.{id}.tres");
                Require(resource.Language == id && !string.IsNullOrWhiteSpace(resource.SourceIdentity), resource.ResourcePath + ": language identity mismatch");
                try
                {
                    packages.Add(id, resource.ToCorePackage());
                    if (id == language) selectedWords = resource.CopyWords();
                    requirements.Add(id, TriggerScriptSession.ValidateEntry(events, packages[id], content, native.EntryFileId, native.EntryId));
                }
                catch (InvalidDataException error) { throw new InvalidDataException(resource.ResourcePath + ": " + error.Message, error); }
            }
            Require(requirements.Values.SelectMany(x => x.PortraitIds).ToHashSet().SetEquals(portraits.Keys),
                "Portraits must cover exactly the selected entry's nonzero portrait IDs");

            var packedScene = LoadResource<PackedScene>(sceneUri);
            Node? node = null;
            try
            {
                node = packedScene.Instantiate();
                Require(node is PalMapScene, sceneUri + ": expected PalMapScene root");
                var scene = ((PalMapScene)node).Data;
                Require(scene is not null && scene.BundleSha256 == native.BundleSha256, "story scene BundleSha256 mismatch");
                CheckedExplorationProfile exploration = NativeExplorationProfile.Load(native.StaticProfile, scene!);
                Require(content.Players[0].RoleId == 0, "story battle role must match static leader role0");
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
                    entry, events, packages[language], content, requirements[language], exploration, font, prompts, portraits, palette, hashes, packedScene, selectedWords!);
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

    private static void Require(bool valid, string message) { if (!valid) throw new InvalidDataException(message); }
}

/// <summary>Core projections are immutable. Native resources are this load's isolated instances;
/// callers must treat prepared resources as read-only. No hot reload or clone-on-get contract.</summary>
public sealed class CheckedStoryProfile
{
    internal PackedScene Scene { get; }
    public IReadOnlyDictionary<int, string> SelectedWords { get; }
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
        PalDialogueIndexedImage[] prompts, Dictionary<int, PalDialogueIndexedImage> portraits, Color[] palette, Dictionary<string, string> hashes, PackedScene scene, IReadOnlyDictionary<int, string> selectedWords)
    {
        Scene = scene; SelectedWords = selectedWords;
        StaticResources = staticResources; StaticProfile = staticProfile; BundleSha256 = bundleSha256; SourceIdentity = sourceIdentity;
        Entry = entry; Events = events; Language = language; BattleContent = battleContent; Requirements = requirements;
        Exploration = exploration; Font = font; Prompts = Array.AsReadOnly(prompts.ToArray());
        Portraits = new ReadOnlyDictionary<int, PalDialogueIndexedImage>(new Dictionary<int, PalDialogueIndexedImage>(portraits));
        Palette = Array.AsReadOnly(palette.ToArray());
        ResourceSha256 = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(hashes, StringComparer.Ordinal));
    }
}
