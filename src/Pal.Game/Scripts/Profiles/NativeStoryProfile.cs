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
            // #218: the object WORD -> prepared sprite URI table validates up front (the seeds
            // concat below consumes it); the content-dependent coverage/identity checks run
            // after the battle content loads.
            var itemBitmapUris = new Dictionary<int, string>();
            if (native.ItemBitmaps is not null)
                foreach (var pair in native.ItemBitmaps)
                {
                    Require(pair.Key is > 0 and <= ushort.MaxValue && !string.IsNullOrWhiteSpace(pair.Value)
                        && itemBitmapUris.TryAdd(pair.Key, pair.Value),
                        "ItemBitmaps requires unique nonzero WORD object IDs with native sprite URIs");
                    NativeExplorationProfile.ValidateUri(pair.Value);
                }
            string sceneUri = native.StaticResources + "/Scenes/Scene.tscn";
            string dialogue = native.DialogueDirectory;
            string[] seeds = new[] { sceneUri, native.StaticProfile, native.BattleContentUri,
                dialogue + "/Font.tres", dialogue + "/Language.zh-Hans.tres", dialogue + "/Language.en.tres",
                dialogue + "/Prompt-00.tres", dialogue + "/Prompt-01.tres", dialogue + "/Prompt-02.tres" }
                .Concat(portraitUris.Values).Concat(itemBitmapUris.Values).ToArray();
            var dependencies = ContentDependencies(seeds, hashes);
            dependencies.UnionWith(eventPaths);
            Require(dependencies.SetEquals(hashes.Keys), "ResourceSha256 must cover exactly complete events and native content dependencies (no missing or extra keys)");

            TriggerEventEntry entry = events.GetEntry(native.EntryFileId, native.EntryId);
            Require(entry is { OwnerType: "eventObject", Slot: "wTriggerScript", Kind: "trigger" }
                && entry.SourceAddress != 0 && entry.SourceAddress == native.EntryAddress
                && entry.OwnerId == native.OwnerId, "story owner-trigger-entry identity mismatch or disabled entry");
            var content = NativeBattleContent.Load(native.BattleContentUri);
            // #215: every authored use-menu binding resolves exactly one enabled item.wScriptOnUse
            // entry now; a usable object without its on-use script rejects before the scene binds.
            var itemUseEntries = new Dictionary<int, TriggerEventEntry>();
            foreach (var binding in content.UseMenu)
            {
                var matches = events.Entries.Where(e =>
                    e.OwnerType == "object" && e.OwnerId == binding.ObjectId
                    && e.Slot == "item.wScriptOnUse" && e.Kind == "trigger").ToArray();
                Require(matches.Length == 1 && matches[0].SourceAddress != 0,
                    $"use-menu object {binding.ObjectId} requires exactly one enabled item.wScriptOnUse trigger entry");
                itemUseEntries.Add(binding.ObjectId, matches[0]);
            }
            // #216: every authored magic-menu binding resolves exactly one enabled
            // magic.wScriptOnUse AND magic.wScriptOnSuccess entry now; a listed magic without
            // either script rejects before the scene binds (uigame.c PAL_InGameMagicMenu runs
            // both entries in order per cast).
            var magicEntries = new Dictionary<int, StoryMagicEntries>();
            foreach (var binding in content.MagicMenu)
            {
                var onUse = events.Entries.Where(e =>
                    e.OwnerType == "object" && e.OwnerId == binding.ObjectId
                    && e.Slot == "magic.wScriptOnUse" && e.Kind == "trigger").ToArray();
                Require(onUse.Length == 1 && onUse[0].SourceAddress != 0,
                    $"magic-menu object {binding.ObjectId} requires exactly one enabled magic.wScriptOnUse trigger entry");
                var onSuccess = events.Entries.Where(e =>
                    e.OwnerType == "object" && e.OwnerId == binding.ObjectId
                    && e.Slot == "magic.wScriptOnSuccess" && e.Kind == "trigger").ToArray();
                Require(onSuccess.Length == 1 && onSuccess[0].SourceAddress != 0,
                    $"magic-menu object {binding.ObjectId} requires exactly one enabled magic.wScriptOnSuccess trigger entry");
                magicEntries.Add(binding.ObjectId, new StoryMagicEntries(onUse[0], onSuccess[0]));
            }
            // #218: every use-menu binding with a nonzero BALL bitmap word resolves exactly one
            // prepared native item-bitmap sprite now; the sprite identity must carry the binding's
            // BALL chunk (itemmenu.c/uigame.c read rgObject item.wBitmap from fpBALL for the
            // selected row, so a wrong chunk or archive is a broken binding, never a placeholder).
            // Stories without that context declare no bitmaps; an extra or missing entry rejects.
            // #218: every use-menu binding with a nonzero BALL bitmap word resolves exactly one
            // prepared native item-bitmap sprite now; the sprite identity must carry the binding's
            // BALL chunk (itemmenu.c/uigame.c read rgObject item.wBitmap from fpBALL for the
            // selected row, so a wrong chunk or archive is a broken binding, never a placeholder).
            // #217: the buy/sell pages read the same word (uigame.c PAL_BuyMenu_OnItemChange/
            // itemmenu.c), so shop objects with a nonzero bitmap join the same coverage; Core
            // already rejects a use-menu/shop disagreement for one object.
            var bitmapSources = new List<(int ObjectId, int Bitmap, string Label)>();
            foreach (var binding in content.UseMenu.Where(binding => binding.Bitmap != 0))
                bitmapSources.Add((binding.ObjectId, binding.Bitmap, "use-menu"));
            foreach (var binding in content.ShopObjects.Values.Where(binding => binding.Bitmap != 0))
                if (!bitmapSources.Any(source => source.ObjectId == binding.ObjectId))
                    bitmapSources.Add((binding.ObjectId, binding.Bitmap, "shop"));
            var itemBitmaps = new Dictionary<int, PalSprite>();
            foreach (var (bitmapObjectId, bitmapWord, bitmapLabel) in bitmapSources)
            {
                string bitmapUri = itemBitmapUris.TryGetValue(bitmapObjectId, out string? bound)
                    ? bound : throw new InvalidDataException(
                        $"{bitmapLabel} object {bitmapObjectId} bitmap {bitmapWord} has no prepared native item bitmap");
                var sprite = NativeBattleVisualProfile.LoadSprite(bitmapUri);
                Require(sprite.SpriteId == bitmapWord && sprite.FrameCount == 1
                    && sprite.Frames.Count == 1 && sprite.Frames[0].Index == 0,
                    bitmapUri + ": item bitmap must be the single frame of BALL chunk " + bitmapWord);
                Require(sprite.Source.TryGetValue("archive", out Variant archive) && archive.VariantType == Variant.Type.String
                    && archive.AsString() == "ball.mkf"
                    && sprite.Source.TryGetValue("chunk", out Variant chunk) && chunk.VariantType == Variant.Type.Int
                    && chunk.AsInt32() == bitmapWord,
                    bitmapUri + ": item bitmap Source must declare the ball.mkf chunk " + bitmapWord);
                itemBitmaps.Add(bitmapObjectId, sprite);
            }
            Require(itemBitmapUris.Keys.ToHashSet().SetEquals(bitmapSources.Select(source => source.ObjectId)),
                content.ShopObjects.Values.Any(binding => binding.Bitmap != 0)
                    ? "ItemBitmaps must cover exactly the use-menu and shop objects with a nonzero bitmap word"
                    : "ItemBitmaps must cover exactly the use-menu objects with a nonzero bitmap word");
            // Party/multi-enemy capability is bounded by the actually selected closure: the
            // language loop below runs TriggerScriptSession.ValidateEntry, which validates every
            // startBattle team of the selected entry (one to three players and enemy slots) and
            // ignores unselected teams as plain data. Only the party head is bound to the static leader.
            var font = LoadResource<PalDialogueFont>(dialogue + "/Font.tres");
            Require(!string.IsNullOrWhiteSpace(font.SourceIdentity) && font.FormatVersion == 1 && font.LineHeight == 16,
                "native font identity/version/line height is invalid");
            var prompts = Enumerable.Range(0, 3).Select(i => LoadImage($"{dialogue}/Prompt-{i:D2}.tres")).ToArray();
            var portraits = portraitUris.ToDictionary(x => x.Key, x => LoadImage(x.Value));
            var packages = new Dictionary<string, DialogueLanguagePackage>(StringComparer.Ordinal);
            var requirements = new Dictionary<string, TriggerEntryRequirements>(StringComparer.Ordinal);
            var itemRequirements = new Dictionary<string, IReadOnlyDictionary<int, TriggerEntryRequirements>>(StringComparer.Ordinal);
            var magicRequirements = new Dictionary<string, IReadOnlyDictionary<int, StoryMagicRequirements>>(StringComparer.Ordinal);
            // #217: the formal host binds the seed-derived shared owners exactly in the authored
            // shop context (Main seeds $.cash into the wallet and binds it with the bag to the
            // trigger executor); the static preflight validates the same entries with the same
            // owners, so a shop command without its explicit owners rejects here, never only at
            // runtime. An empty bag seed is still an explicit owner — uigame.c's sell menu of an
            // empty bag is the empty list — and a missing $.cash keeps the owner absent so the
            // Core gate rejects with the shop-specific diagnostic.
            CashLiveState? shopCashOwner = content.ShopObjects.Count > 0 && content.InitialCash is { } cashSeed
                ? new CashLiveState(cashSeed) : null;
            InventoryLiveState? shopInventoryOwner = content.ShopObjects.Count > 0
                ? new InventoryLiveState(content.InitialInventory) : null;
            IReadOnlyDictionary<int, string>? selectedWords = null;
            foreach (string id in new[] { "zh-Hans", "en" })
            {
                var resource = LoadResource<PalDialogueLanguage>($"{dialogue}/Language.{id}.tres");
                Require(resource.Language == id && !string.IsNullOrWhiteSpace(resource.SourceIdentity), resource.ResourcePath + ": language identity mismatch");
                try
                {
                    packages.Add(id, resource.ToCorePackage());
                    if (id == language) selectedWords = resource.CopyWords();
                    requirements.Add(id, TriggerScriptSession.ValidateEntry(events, packages[id], content, native.EntryFileId, native.EntryId,
                        cashLiveState: shopCashOwner, inventoryLiveState: shopInventoryOwner));
                    var perItem = new Dictionary<int, TriggerEntryRequirements>();
                    foreach (var pair in itemUseEntries)
                        perItem.Add(pair.Key, TriggerScriptSession.ValidateEntry(events, packages[id], content, pair.Value.FileId, pair.Value.Id,
                            cashLiveState: shopCashOwner, inventoryLiveState: shopInventoryOwner));
                    itemRequirements.Add(id, perItem);
                    var perMagic = new Dictionary<int, StoryMagicRequirements>();
                    foreach (var pair in magicEntries)
                        perMagic.Add(pair.Key, new StoryMagicRequirements(
                            TriggerScriptSession.ValidateEntry(events, packages[id], content, pair.Value.OnUse.FileId, pair.Value.OnUse.Id,
                                cashLiveState: shopCashOwner, inventoryLiveState: shopInventoryOwner),
                            TriggerScriptSession.ValidateEntry(events, packages[id], content, pair.Value.OnSuccess.FileId, pair.Value.OnSuccess.Id,
                                cashLiveState: shopCashOwner, inventoryLiveState: shopInventoryOwner)));
                    magicRequirements.Add(id, perMagic);
                }
                catch (InvalidDataException error) { throw new InvalidDataException(resource.ResourcePath + ": " + error.Message, error); }
            }
            var requiredPortraits = requirements.Values.SelectMany(x => x.PortraitIds)
                .Concat(itemRequirements.Values.SelectMany(x => x.Values.SelectMany(item => item.PortraitIds)))
                .Concat(magicRequirements.Values.SelectMany(x => x.Values.SelectMany(magic => magic.OnUse.PortraitIds.Concat(magic.OnSuccess.PortraitIds))))
                .ToHashSet();
            Require(requiredPortraits.SetEquals(portraits.Keys),
                "Portraits must cover exactly the selected, item-use and magic entries' nonzero portrait IDs");

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
                // Both supported languages stay validated together (#215 adds the item entries and
                // #216 the magic on-use/on-success pairs to the same boundary); the unselected
                // language must never lose glyph coverage.
                foreach (var requirement in requirements.Values
                    .Concat(itemRequirements.Values.SelectMany(languageItems => languageItems.Values))
                    .Concat(magicRequirements.Values.SelectMany(languageMagics => languageMagics.Values
                        .SelectMany(entries => new[] { entries.OnUse, entries.OnSuccess }))))
                foreach (var display in requirement.DisplayBlocks)
                {
                    try { PalDialogueDisplay.ValidateContent(display.Content, DialogueTextTokenizer.DefaultColor, font, null, prompts, palette); }
                    catch (InvalidDataException error) { throw new InvalidDataException($"{display.MessageReferences[0].Position}: {error.Message}", error); }
                }
                // Detect byte changes during validation, not future edits/import-cache or GPU changes.
                foreach (string path in hashes.Keys) CheckFile(path, hashes);
                return new(native.StaticResources, native.StaticProfile, native.BundleSha256, native.SourceIdentity,
                    entry, events, packages[language], content, requirements[language], exploration, font, prompts, portraits, palette, hashes, packedScene, selectedWords!, itemUseEntries, magicEntries, itemBitmaps);
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
    /// <summary>#215: the resolved item.wScriptOnUse entry per authored use-menu object; the story
    /// adapter runs these entries for the exploration item-use flow (play.c PAL_GameUseItem).</summary>
    public IReadOnlyDictionary<int, TriggerEventEntry> ItemUseEntries { get; }
    /// <summary>#216: the resolved magic.wScriptOnUse / magic.wScriptOnSuccess entry pair per
    /// authored magic-menu object; the story adapter runs both in order for each exploration
    /// cast (uigame.c PAL_InGameMagicMenu's two PAL_RunTriggerScript calls).</summary>
    public IReadOnlyDictionary<int, StoryMagicEntries> MagicUseEntries { get; }
    /// <summary>#218: the prepared native BALL item-bitmap sprite per use-menu object with a
    /// nonzero bitmap word (fail-closed coverage validated at load; the item pages draw the
    /// selected row's frame with the active scene palette).</summary>
    public IReadOnlyDictionary<int, PalSprite> ItemBitmaps { get; }
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
        PalDialogueIndexedImage[] prompts, Dictionary<int, PalDialogueIndexedImage> portraits, Color[] palette, Dictionary<string, string> hashes, PackedScene scene, IReadOnlyDictionary<int, string> selectedWords,
        Dictionary<int, TriggerEventEntry>? itemUseEntries = null,
        Dictionary<int, StoryMagicEntries>? magicEntries = null,
        Dictionary<int, PalSprite>? itemBitmaps = null)
    {
        Scene = scene; SelectedWords = selectedWords;
        StaticResources = staticResources; StaticProfile = staticProfile; BundleSha256 = bundleSha256; SourceIdentity = sourceIdentity;
        Entry = entry; Events = events; Language = language; BattleContent = battleContent; Requirements = requirements;
        ItemUseEntries = new ReadOnlyDictionary<int, TriggerEventEntry>(itemUseEntries ?? new Dictionary<int, TriggerEventEntry>());
        MagicUseEntries = new ReadOnlyDictionary<int, StoryMagicEntries>(magicEntries ?? new Dictionary<int, StoryMagicEntries>());
        ItemBitmaps = new ReadOnlyDictionary<int, PalSprite>(itemBitmaps ?? new Dictionary<int, PalSprite>());
        Exploration = exploration; Font = font; Prompts = Array.AsReadOnly(prompts.ToArray());
        Portraits = new ReadOnlyDictionary<int, PalDialogueIndexedImage>(new Dictionary<int, PalDialogueIndexedImage>(portraits));
        Palette = Array.AsReadOnly(palette.ToArray());
        ResourceSha256 = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(hashes, StringComparer.Ordinal));
    }
}

/// <summary>#216: the resolved script-entry pair per authored magic-menu object — the
/// magic.wScriptOnUse entry the host runs first and the magic.wScriptOnSuccess entry it runs
/// only after the on-use run survived (uigame.c 807-841).</summary>
public sealed record StoryMagicEntries(TriggerEventEntry OnUse, TriggerEventEntry OnSuccess);

/// <summary>The two entries' validated requirements for one language boundary.</summary>
public sealed record StoryMagicRequirements(TriggerEntryRequirements OnUse, TriggerEntryRequirements OnSuccess);
