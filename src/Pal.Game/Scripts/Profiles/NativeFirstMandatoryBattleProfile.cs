using System;
using System.IO;
using System.Linq;
using Godot;
using Pal.Core;
using Pal.Game.Map;
using Pal.Game.Resources;

namespace Pal.Game.Profiles;

public static class NativeFirstMandatoryBattleProfile
{
    public static CheckedFirstMandatoryBattleProfile Load(string uri)
    {
        NativeExplorationProfile.ValidateUri(uri);
        if (!ResourceLoader.Exists(uri)) throw new InvalidDataException(uri + ": native first-battle profile missing; prepare it explicitly");
        var native = ResourceLoader.Load(uri, cacheMode: ResourceLoader.CacheMode.IgnoreDeep) as PalFirstMandatoryBattleProfile
            ?? throw new InvalidDataException(uri + ": expected PalFirstMandatoryBattleProfile");
        Require(native.FormatVersion == 1, uri + ": first-battle FormatVersion must be 1");
        Require(native.ProfileId == FirstMandatoryBattleProfile.ProfileIdValue, uri + ": first-battle profile identity mismatch");
        Require(native.IndexBytes is { Length: > 0 } && native.ContextBytes is { Length: > 0 }, uri + ": embedded exact index/context bytes are required");
        var profile = FirstMandatoryBattleProfile.Load(native.IndexBytes,
            new System.Collections.Generic.Dictionary<string, byte[]>(StringComparer.Ordinal) { ["battle-context.json"] = native.ContextBytes });
        Require(profile.ContextSha256 == native.ContextSha256, uri + ": embedded context hash identity mismatch");
        Require((native.SceneId, native.MapId, native.OwnerId, native.TriggerAddress) ==
            (profile.Identity.SceneId, profile.Identity.MapId, profile.Identity.OwnerId, profile.Identity.TriggerAddress),
            uri + ": embedded scene/map/owner/trigger identity mismatch");
        NativeSceneLoader.ValidateRoot(native.StaticResources);
        NativeExplorationProfile.ValidateUri(native.StaticProfile);
        string sceneUri = native.StaticResources + "/Scenes/Scene.tscn";
        CheckNativeFile(sceneUri, native.StaticSceneSha256);
        CheckNativeFile(native.StaticProfile, native.StaticProfileSha256);
        string[] ids = profile.Resources.Select(item => item.Id).ToArray();
        string[] references = profile.Resources.Select(item => item.Reference).ToArray();
        string[] hashes = profile.Resources.Select(item => item.Sha256).ToArray();
        Require(native.ResourceIds.SequenceEqual(ids) && native.ResourceReferences.SequenceEqual(references)
            && native.ResourceSha256.SequenceEqual(hashes), uri + ": embedded contract resource identities mismatch");
        return new(profile, native.StaticResources, native.StaticProfile);
    }

    private static void CheckNativeFile(string uri, string expected)
    {
        NativeExplorationProfile.ValidateUri(uri);
        Require(expected is { Length: 64 } && expected.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'), uri + ": native SHA-256 identity is invalid");
        Require(Godot.FileAccess.FileExists(uri) && Godot.FileAccess.GetSha256(uri) == expected, uri + ": native resource SHA-256 mismatch or missing resource");
    }

    private static void Require(bool value, string diagnostic)
    {
        if (!value) throw new InvalidDataException(diagnostic);
    }
}

public sealed record CheckedFirstMandatoryBattleProfile(FirstMandatoryBattleProfile Profile, string StaticResources, string StaticProfile)
{
    public FirstMandatoryBattleSession CreateSession(CheckedExplorationProfile scene) => new(Profile, scene.Rules, scene.Leader);

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

    private static int Direction(string value) => value switch
    {
        "south" => 0, "west" => 1, "north" => 2, "east" => 3,
        _ => throw new InvalidDataException("First mandatory battle direction identity mismatch.")
    };
}
