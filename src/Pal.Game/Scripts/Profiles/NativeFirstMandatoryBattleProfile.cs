using System;
using System.IO;
using System.Linq;
using Godot;
using Pal.Core;
using Pal.Game.Dialogue;
using Pal.Game.Map;
using Pal.Game.Resources;
using static Pal.Game.Profiles.NativeContentDependencies;

namespace Pal.Game.Profiles;

public static class NativeFirstMandatoryBattleProfile
{
    public static CheckedFirstMandatoryBattleProfile Load(string uri)
    {
        try
        {
            NativeExplorationProfile.ValidateUri(uri);
            if (!ResourceLoader.Exists(uri)) throw new InvalidDataException(uri + ": native first-battle profile missing; prepare it explicitly");
            var native = ResourceLoader.Load(uri, cacheMode: ResourceLoader.CacheMode.IgnoreDeep) as PalFirstMandatoryBattleProfile
                ?? throw new InvalidDataException(uri + ": expected PalFirstMandatoryBattleProfile");
            return Validate(native, uri);
        }
        catch (InvalidDataException e) when (!e.Message.Contains(uri, StringComparison.Ordinal))
        { throw new InvalidDataException(uri + ": " + e.Message, e); }
        catch (Exception e) when (e is NullReferenceException or ArgumentNullException or InvalidCastException or ArgumentException)
        { throw new InvalidDataException(uri + ": malformed native first-battle profile", e); }
    }

    internal static CheckedFirstMandatoryBattleProfile ValidateForPreparation(PalFirstMandatoryBattleProfile native) =>
        Validate(native, "in-memory first-battle profile");

    internal static (CheckedExplorationProfile Profile, FirstMandatoryBattleSession Session) ValidateStaticSelection(
        CheckedFirstMandatoryBattleProfile battle, PalMapScene map)
    {
        CheckedExplorationProfile profile = NativeExplorationProfile.Load(battle.StaticProfile, map.Data);
        battle.ValidateStatic(map.Data, profile);
        return (profile, battle.CreateSession(map.Data.SceneId, profile));
    }

    private static CheckedFirstMandatoryBattleProfile Validate(PalFirstMandatoryBattleProfile native, string identity)
    {
        Require(native.FormatVersion == 1, identity + ": first-battle FormatVersion must be 1");
        Require(native.ProfileId == FirstMandatoryBattleProfile.ProfileIdValue, identity + ": first-battle profile identity mismatch");
        FirstMandatoryBattleEnvelope envelope = NativeBattleEnvelopeAdapter.Read(native, identity);
        Require(IsHash(native.NativeEnvelopeSha256), identity + ": NativeEnvelopeSha256 is invalid");
        Require(FirstMandatoryBattleEnvelopeCodec.Sha256(envelope) == native.NativeEnvelopeSha256, identity + ": typed native envelope SHA-256 mismatch");
        FirstMandatoryBattleProfile profile = FirstMandatoryBattleProfile.FromTypedEnvelope(envelope);
        string staticResources = Need(native.StaticResources, identity, "StaticResources");
        string staticProfile = Need(native.StaticProfile, identity, "StaticProfile");
        NativeSceneLoader.ValidateRoot(staticResources);
        NativeExplorationProfile.ValidateUri(staticProfile);
        CheckNativeFile(staticResources + "/Scenes/Scene.tscn", Need(native.StaticSceneSha256, identity, "StaticSceneSha256"));
        CheckNativeFile(staticProfile, Need(native.StaticProfileSha256, identity, "StaticProfileSha256"));
        FirstMandatoryBattleClosureReceipt? closure = null;
        CheckedBattleVisualProfile? visual = null;
        if (HasResourceBinding(native)) (closure, visual) = VerifyRealClosure(native, profile, staticResources, identity);
        return new(profile, staticResources, staticProfile, closure, visual);
    }

    private static bool HasResourceBinding(PalFirstMandatoryBattleProfile native) =>
        native.EventsIndex.Length > 0 || native.BattleContentUri.Length > 0 || native.DialogueDirectory.Length > 0
        || native.VisualUri.Length > 0 || native.ImagePlayerSpriteUris.Length > 0 || native.ImageEnemyId != -1
        || native.ImageEnemyArchiveEntry.Length > 0 || native.ImageRoleSpriteIds.Length > 0
        || native.ImageBattlefieldId != -1 || native.ImageBattlefieldArchiveEntry.Length > 0
        || (native.Portraits?.Count ?? 0) > 0 || (native.ResourceSha256?.Count ?? 0) > 0;

    /// <summary>The #53 real resource closure: strict load of every bound native resource, the
    /// Core capability precheck over the real events/content/languages, and the visual binding.
    /// Any missing or mismatched resource rejects here, before any runtime side effect.</summary>
    private static (FirstMandatoryBattleClosureReceipt Closure, CheckedBattleVisualProfile Visual) VerifyRealClosure(
        PalFirstMandatoryBattleProfile native, FirstMandatoryBattleProfile profile, string staticResources, string identity)
    {
        foreach (string path in new[] { native.EventsIndex, native.BattleContentUri, native.DialogueDirectory, native.VisualUri })
        { Require(path.Length > 0, identity + ": the real closure binding requires EventsIndex, BattleContentUri, DialogueDirectory and VisualUri together"); NativeExplorationProfile.ValidateUri(path); }
        Require(native.ImageEnemyId is > 0 and <= ushort.MaxValue && native.ImageBattlefieldId is > 0 and <= ushort.MaxValue
            && native.ImageEnemyArchiveEntry.Length > 0 && native.ImageBattlefieldArchiveEntry.Length > 0
            && native.ImageRoleSpriteIds.Length == native.ImagePlayerSpriteUris.Length && native.ImageRoleSpriteIds.Length > 0
            && native.ImageRoleSpriteIds.All(id => id.Length > 0), identity + ": the battle image identity binding is incomplete");
        Require(native.ResourceSha256 is { Count: > 0 }, identity + ": ResourceSha256 is required for the real closure");
        var hashes = native.ResourceSha256!.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        foreach (string path in hashes.Keys) CheckFile(path, hashes);
        foreach (var pair in native.Portraits ?? new Godot.Collections.Dictionary<int, string>())
        {
            Require(pair.Key is > 0 and <= ushort.MaxValue, identity + ": Portraits requires nonzero WORD IDs");
            NativeExplorationProfile.ValidateUri(pair.Value);
        }

        var events = TriggerEventLoader.Load(native.EventsIndex);
        using var index = System.Text.Json.JsonDocument.Parse(Godot.FileAccess.GetFileAsString(native.EventsIndex));
        string directory = native.EventsIndex[..native.EventsIndex.LastIndexOf('/')];
        var eventPaths = index.RootElement.GetProperty("files").EnumerateArray()
            .Select(id => directory + "/" + id.GetString() + ".json").Prepend(native.EventsIndex).ToArray();
        foreach (string path in eventPaths) CheckFile(path, hashes);
        string dialogue = native.DialogueDirectory;
        string[] seeds = new[] { native.BattleContentUri, dialogue + "/Font.tres", dialogue + "/Language.zh-Hans.tres",
            dialogue + "/Language.en.tres", dialogue + "/Prompt-00.tres", dialogue + "/Prompt-01.tres", dialogue + "/Prompt-02.tres" }
            .Concat(native.Portraits?.Values ?? Enumerable.Empty<string>()).Concat(native.ImagePlayerSpriteUris).ToArray();
        var dependencies = NativeContentDependencies.ContentDependencies(seeds, hashes);
        dependencies.UnionWith(eventPaths);
        Require(dependencies.SetEquals(hashes.Keys), identity + ": ResourceSha256 must cover exactly the complete events and native content dependencies");

        var content = NativeBattleContent.Load(native.BattleContentUri);
        var font = LoadTyped<PalDialogueFont>(dialogue + "/Font.tres", resource =>
            Require(!string.IsNullOrWhiteSpace(resource.SourceIdentity) && resource.FormatVersion == 1 && resource.LineHeight == 16,
                dialogue + "/Font.tres: font identity/version/line height is invalid"));
        _ = font;
        _ = Enumerable.Range(0, 3).Select(i => LoadImage(string.Format("{0}/Prompt-{1:D2}.tres", dialogue, i))).ToArray();
        DialogueLanguagePackage Package(string id) => LoadTyped<PalDialogueLanguage>(string.Format("{0}/Language.{1}.tres", dialogue, id), resource =>
            Require(resource.Language == id && !string.IsNullOrWhiteSpace(resource.SourceIdentity),
                string.Format("{0}/Language.{1}.tres", dialogue, id) + ": language identity mismatch")).ToCorePackage();
        var closure = FirstMandatoryBattleClosure.Verify(profile, events, Package("zh-Hans"), Package("en"), content,
            new(native.ImageEnemyId, native.ImageEnemyArchiveEntry, native.ImageRoleSpriteIds,
                native.ImageBattlefieldId, native.ImageBattlefieldArchiveEntry, native.ImageIncludesLayoutAndEffectFrames));
        var portraitIds = native.Portraits?.Keys ?? Enumerable.Empty<int>();
        Require(portraitIds.ToHashSet().SetEquals(closure.VerifiedPortraitIds),
            identity + ": Portraits must cover exactly the verified entry portrait IDs");
        for (int i = 0; i < native.ImagePlayerSpriteUris.Length; i++)
        {
            var sprite = LoadTyped<PalSprite>(native.ImagePlayerSpriteUris[i], resource => Require(
                Enumerable.Range(0, 10).All(frame => resource.Frames.Select(x => x.Index).ToHashSet().Contains(frame)),
                native.ImagePlayerSpriteUris[i] + ": player sprite requires addressable frames 0..9"));
            string sourceIdentity = sprite.Source.TryGetValue("identity", out var value) && value.VariantType == Variant.Type.String ? value.AsString() : "";
            Require(sourceIdentity == native.ImageRoleSpriteIds[i],
                native.ImagePlayerSpriteUris[i] + ": player sprite source identity must be " + native.ImageRoleSpriteIds[i]);
        }
        var paletteResource = LoadTyped<PalPalette>(staticResources + "/Palettes/Palette.tres", resource =>
            Require(resource.Rgb is { Length: 768 }, staticResources + "/Palettes/Palette.tres: requires RGB768"));
        byte[] rgb = paletteResource.Rgb!;
        var palette = Enumerable.Range(0, 256).Select(i => new Color(rgb[i * 3] / 255f, rgb[i * 3 + 1] / 255f, rgb[i * 3 + 2] / 255f)).ToArray();
        var visual = NativeBattleVisualProfile.Load(native.VisualUri, content.Players[0].RoleId, content.Players.Count,
            content.EnemyTeams, closure.VerifiedBattleTeamIds, palette);
        var team = visual.Teams[closure.VerifiedBattleTeamIds.Single()];
        Require(team.EnemySprite.SpriteId == native.ImageEnemyId,
            native.VisualUri + ": enemy sprite must be the declared ABC bitmap " + native.ImageEnemyId);
        Require(team.Background.SourceIdentity == native.ImageBattlefieldArchiveEntry,
            native.VisualUri + ": battle background source identity must be " + native.ImageBattlefieldArchiveEntry);
        foreach (string path in hashes.Keys) CheckFile(path, hashes);
        return (closure, visual);
    }

    private static T LoadTyped<T>(string uri, Action<T> validate) where T : Resource
    {
        NativeExplorationProfile.ValidateUri(uri);
        Require(Godot.FileAccess.FileExists(uri), uri + ": missing native resource; prepare it explicitly");
        var resource = ResourceLoader.Load(uri, cacheMode: ResourceLoader.CacheMode.IgnoreDeep) as T
            ?? throw new InvalidDataException(uri + ": expected " + typeof(T).Name);
        validate(resource);
        return resource;
    }

    private static PalDialogueIndexedImage LoadImage(string uri)
    {
        var image = LoadTyped<PalDialogueIndexedImage>(uri, resource => Require(!string.IsNullOrWhiteSpace(resource.SourceIdentity), uri + ": missing source identity"));
        image.Validate();
        return image;
    }

    private static string Need(string? value,string uri,string field)
    {
        if (value is null) throw new InvalidDataException(uri + ": " + field + " is null");
        return value;
    }
    private static bool IsHash(string? value) => value is { Length:64 } && value.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static void CheckNativeFile(string uri, string expected)
    {
        NativeExplorationProfile.ValidateUri(uri);
        Require(IsHash(expected), uri + ": native SHA-256 identity is invalid");
        Require(Godot.FileAccess.FileExists(uri) && Godot.FileAccess.GetSha256(uri) == expected, uri + ": native resource SHA-256 mismatch or missing resource");
    }
    private static void Require(bool value,string diagnostic) { if(!value) throw new InvalidDataException(diagnostic); }
}

public sealed class CheckedFirstMandatoryBattleProfile
{
    public FirstMandatoryBattleProfile Profile { get; }
    public string StaticResources { get; }
    public string StaticProfile { get; }

    /// <summary>The verified real resource closure; null keeps the pre-#53 shape and the @09C3 gate closed.</summary>
    public FirstMandatoryBattleClosureReceipt? Closure { get; }
    public CheckedBattleVisualProfile? Visual { get; }

    internal CheckedFirstMandatoryBattleProfile(FirstMandatoryBattleProfile profile,string staticResources,string staticProfile,
        FirstMandatoryBattleClosureReceipt? closure = null, CheckedBattleVisualProfile? visual = null)
        => (Profile,StaticResources,StaticProfile,Closure,Visual)=(profile,staticResources,staticProfile,closure,visual);

    public FirstMandatoryBattleSession CreateSession(int sceneId, CheckedExplorationProfile scene) => new(Profile, sceneId, scene.Rules, scene.Leader);

    public void ValidateStatic(PalSceneData data, CheckedExplorationProfile scene)
    {
        if (data.SceneId != 2 || data.MapId != 12 || Profile.Identity is not { SceneId: 2, MapId: 12, OwnerId: 28, TriggerAddress: 0x09c3 })
            throw new InvalidDataException("First mandatory battle requires scene2/map12/owner28/@09C3 identity.");
        PalSceneObject owner = data.Objects.SingleOrDefault(item => item.Id == 28)
            ?? throw new InvalidDataException("First mandatory battle static scene is missing owner28.");
        BattleEventObjectSnapshot snapshot = Profile.World.EventObjects.Single(item => item.OwnerId == 28);
        if (owner.X != snapshot.X || owner.Y != snapshot.Y || owner.WSpriteNum != snapshot.SpriteId
            || owner.WTriggerMode != snapshot.TriggerMode || owner.WTriggerScript != snapshot.TriggerEntry || owner.WAutoScript != snapshot.AutoEntry)
            throw new InvalidDataException("First mandatory battle static owner28 reference identity mismatch.");
        BattleLeaderSnapshot leader = Profile.World.Leader;
        if ((scene.Leader.X, scene.Leader.Y, scene.Leader.Direction, scene.Leader.Layer, scene.Leader.StepPhase) !=
            (leader.X, leader.Y, Direction(leader.Direction), leader.Layer, leader.Phase))
            throw new InvalidDataException("First mandatory battle static profile start identity mismatch.");
    }

    private static int Direction(string value) => value switch { "south"=>0,"west"=>1,"north"=>2,"east"=>3,_=>throw new InvalidDataException("First mandatory battle direction identity mismatch.") };
}
