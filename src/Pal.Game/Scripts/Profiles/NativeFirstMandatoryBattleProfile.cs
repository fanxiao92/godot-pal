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
        return new(profile, staticResources, staticProfile);
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

    internal CheckedFirstMandatoryBattleProfile(FirstMandatoryBattleProfile profile,string staticResources,string staticProfile)
        => (Profile,StaticResources,StaticProfile)=(profile,staticResources,staticProfile);

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
