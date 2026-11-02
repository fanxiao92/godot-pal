using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Godot;
using Pal.Core;
using Pal.Game.Resources;
using Pal.Game.Profiles;
using Pal.TestFixtures;

namespace Pal.Game.Tests;

internal sealed class NativeBattleProfileFixture : IDisposable
{
    public NativeProfileFixture Static { get; } = new();
    public string Root { get; } = "res://Tests/RuntimeBattleProfiles/" + Guid.NewGuid().ToString("N");
    public string Uri => Root + "/Battle.tres";
    public PalFirstMandatoryBattleProfile Native { get; }
    public FirstMandatoryBattleProfile Core { get; }
    private readonly byte[] indexBytes;
    private readonly byte[] contextBytes;
    private readonly string inputDirectory = Path.Combine(Path.GetTempPath(), "pal-battle-bridge-" + Guid.NewGuid().ToString("N"));
    private readonly string preparedRoot = "res://Assets/RuntimeTests/BattlePrepared-" + Guid.NewGuid().ToString("N");
    public Dictionary<string, string> PreparationSourceHashes { get; } = new(StringComparer.Ordinal);

    public NativeBattleProfileFixture()
    {
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(Root));
        for (int frame = 1; frame <= 11; frame++) Static.AddAliasFrame(frame);
        PalSprite sprite = Static.Scene.Sprites[0];
        sprite.SpriteId = 210; sprite.Source["chunk"] = 210; Static.Scene.SpriteIds = [210];
        Static.Scene.DefaultParty[0].WSpriteNum = 210; Static.Scene.DefaultParty[0].Sprite = sprite;
        Static.Scene.WEventObjectIndex = 27;
        PalSceneObject owner = Static.AddObject(28);
        owner.X = 1280; owner.Y = 256; owner.SState = 1; owner.WTriggerMode = 1; owner.WTriggerScript = 0x09c3;
        owner.WAutoScript = 0; owner.WSpriteNum = 210; owner.Sprite = sprite; owner.NSpriteFrames = 0;
        Static.Profile.Start.X = 1264; Static.Profile.Start.Y = 248; Static.Profile.Start.Direction = 3;
        Static.SavePacked();

        BattleContextTestFixture fixture = BattleContextTestFixture.Valid();
        fixture.Document["snapshot"]!["world"]!["leader"]!["x"] = 1264;
        fixture.Document["snapshot"]!["world"]!["leader"]!["y"] = 248;
        fixture.Document["snapshot"]!["world"]!["searchProof"]!["sampleIndex"] = 1;
        byte[] context = Encoding.UTF8.GetBytes(fixture.Document.ToJsonString());
        string contextHash = Convert.ToHexStringLower(SHA256.HashData(context));
        byte[] index = Encoding.UTF8.GetBytes($"{{\"formatVersion\":1,\"profile\":\"first-mandatory-battle-pre-trigger-v1\",\"contextFile\":\"battle-context.json\",\"contextSha256\":\"{contextHash}\"}}");
        indexBytes = index;
        contextBytes = context;
        Core = FirstMandatoryBattleProfile.Load(index, new Dictionary<string, byte[]> { ["battle-context.json"] = context });
        Native = new PalFirstMandatoryBattleProfile
        {
            FormatVersion = 1, ProfileId = Core.ProfileId, StaticResources = Static.ResourceRoot, StaticProfile = Static.Uri,
            StaticSceneSha256 = Godot.FileAccess.GetSha256(Static.ResourceRoot + "/Scenes/Scene.tscn"),
            StaticProfileSha256 = Godot.FileAccess.GetSha256(Static.Uri)
        };
        NativeBattleEnvelopeAdapter.Write(Native, Core.ToTypedEnvelope());
        Save();
    }

    public void Save() => NativeProfileFixture.SaveResource(Native, Uri);

    public string PrepareProductionProfile()
    {
        Directory.CreateDirectory(inputDirectory);
        File.WriteAllBytes(Path.Combine(inputDirectory, "index.json"), indexBytes);
        File.WriteAllBytes(Path.Combine(inputDirectory, "battle-context.json"), contextBytes);
        string staticProfile = Static.ResourceRoot + "/Profile.tres";
        NativeProfileFixture.SaveResource(Static.Profile, staticProfile);
        foreach (string path in Directory.GetFiles(inputDirectory).Concat(
            Directory.GetFiles(ProjectSettings.GlobalizePath(Static.ResourceRoot), "*", SearchOption.AllDirectories)))
            PreparationSourceHashes.Add(path, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
        return Editor.PrepareBattleContext.Prepare(inputDirectory, ProjectSettings.GlobalizePath(preparedRoot),
            Static.ResourceRoot, staticProfile);
    }

    public void Dispose()
    {
        Static.Dispose();
        if (Directory.Exists(inputDirectory)) Directory.Delete(inputDirectory, true);
        if (Directory.Exists(ProjectSettings.GlobalizePath(preparedRoot))) Directory.Delete(ProjectSettings.GlobalizePath(preparedRoot), true);
        if (Directory.Exists(ProjectSettings.GlobalizePath(Root))) Directory.Delete(ProjectSettings.GlobalizePath(Root), true);
    }
}
