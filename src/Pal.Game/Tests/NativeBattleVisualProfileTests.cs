using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using GdUnit4;
using Godot;
using Pal.Game.Profiles;
using Pal.Game.Resources;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

[TestSuite, RequireGodotRuntime]
public class NativeBattleVisualProfileTests
{
    [TestCase]
    public void DeclaredAttackEffectCannotEscapeExactHashClosure()
    {
        using var f = new NativeBattleVisualProfileFixture();
        f.Story.Save(f.Enemy, "Effect.tres");
        f.Profile.AttackEffectSpriteUri = f.Root + "/Effect.tres";
        f.Profile.AttackEffectIndex = 0;
        f.Save();
        string serialized = f.Story.Read("Visual.tres");
        AssertThat(serialized.Contains("AttackEffectSpriteUri = \"" + f.Root + "/Effect.tres\"")).IsTrue();
        AssertThat(serialized.Contains("AttackEffectIndex = 0")).IsTrue();
        bool rejected = false;
        try { _ = f.Load(); }
        catch (InvalidDataException error) { rejected = error.Message.Contains("SHA-256"); }
        AssertThat(rejected).IsTrue();
    }

    [TestCase]
    public void MissingVisualRootIsRejectedThroughPublicLoader()
    {
        using var fixture = new NativeStoryProfileFixture();
        var story = NativeStoryProfile.Load(fixture.Uri);
        AssertThat(Failure(() => NativeBattleVisualProfile.Load(fixture.Root + "/Missing.tres", story)).Contains("missing")).IsTrue();
    }

    [TestCase]
    public void NativeSaveLoadBindsRoleTeamAndTargetWithoutConflatingSpriteIdentity()
    {
        using var f = new NativeBattleVisualProfileFixture();
        var visual = f.Load();
        AssertThat(visual.RoleId).IsEqual(0);
        AssertThat(visual.PlayerSprite.SpriteId).IsEqual(41);
        AssertThat(visual.Teams[18].TargetId).IsEqual(7);
        AssertThat(visual.Teams[18].EnemySprite.SpriteId).IsEqual(97);
        AssertThat(visual.PlayerPosition).IsEqual(new Vector2I(240, 170));
        AssertThat(visual.Teams[18].EnemyPosition).IsEqual(new Vector2I(100, 120));
        AssertThat(visual.Teams[18].ActWaitFrames).IsEqual(0);
        AssertThat(visual.Teams[18].Background.Alpha[319]).IsEqual((byte)255);
        AssertThat(visual.PlayerSprite.Frames[9].Indices.SequenceEqual(new byte[] { 4, 19 })).IsTrue();
        AssertThat(visual.PlayerSprite.Frames[9].Alpha.SequenceEqual(new byte[] { 0, 255 })).IsTrue();
        AssertThat(visual.PlayerSprite.Frames[9].Texture is null).IsTrue();
        AssertThat(visual.ResourceSha256.Keys.Order().SequenceEqual(f.ExpectedPaths.Order())).IsTrue();
        AssertThat(((IDictionary<int, CheckedBattleVisualTeam>)visual.Teams).IsReadOnly).IsTrue();
        AssertThat(((IDictionary<string, string>)visual.ResourceSha256).IsReadOnly).IsTrue();
        f.Player.Frames[9].Indices[0] = 99; f.Team.TargetId = 44; f.Palette.Rgb[0] = 12; f.Profile.ResourceSha256.Clear();
        AssertThat(visual.PlayerSprite.Frames[9].Indices[0]).IsEqual((byte)4);
        AssertThat(visual.Teams[18].TargetId).IsEqual(7);
        AssertThat(visual.Palette.Rgb[0]).IsEqual((byte)0);
        AssertThat(visual.ResourceSha256.Count).IsEqual(4);
        // A mutation of this isolated native instance must not poison a later load of the same URI.
        visual.PlayerSprite.Frames[0].Indices[0] = 123;
        AssertThat(f.Load().PlayerSprite.Frames[0].Indices[0]).IsEqual((byte)4);
    }

    [TestCase]
    public void MissingExplicitVersionIsNotAcceptedAsVersionOne()
    {
        using var f = new NativeBattleVisualProfileFixture();
        NativeProfileFixture.SaveResource(new PalBattleVisualProfile(), f.Uri);
        AssertThat(Failure(() => f.Load()).Contains("FormatVersion must be 1")).IsTrue();
    }

    [TestCase("version")]
    [TestCase("identity")]
    [TestCase("role")]
    [TestCase("missing-team")]
    [TestCase("extra-team")]
    [TestCase("duplicate-team")]
    [TestCase("target-is-sprite")]
    [TestCase("target-is-team")]
    [TestCase("player-position")]
    [TestCase("enemy-position")]
    [TestCase("idle-zero")]
    [TestCase("magic-negative")]
    [TestCase("attack-overflow")]
    [TestCase("wait-negative")]
    [TestCase("metadata-frames")]
    public void AuthoredMappingAndPresentationMetadataMustMatchSelectedStory(string fault)
    {
        using var f = new NativeBattleVisualProfileFixture();
        string expected;
        switch (fault)
        {
            case "version": f.Profile.FormatVersion = 2; expected = "FormatVersion"; break;
            case "identity": f.Profile.SourceIdentity = " "; expected = "SourceIdentity"; break;
            case "role": f.Profile.RoleId = 41; expected = "RoleId"; break;
            case "missing-team": f.Profile.Teams = []; expected = "exactly cover"; break;
            case "extra-team": f.Profile.Teams = [f.Team, new PalBattleVisualTeam { TeamId = 19 }]; expected = "TeamId/TargetId"; break;
            case "duplicate-team": f.Profile.Teams = [f.Team, f.Team]; expected = "duplicate"; break;
            case "target-is-sprite": f.Team.TargetId = 97; expected = "TargetId"; break;
            case "target-is-team": f.Team.TargetId = 18; expected = "TargetId"; break;
            case "player-position": f.Profile.PlayerPosition = new(32768, 0); expected = "SHORT"; break;
            case "enemy-position": f.Team.EnemyPosition = new(0, -32769); expected = "SHORT"; break;
            case "idle-zero": f.Team.IdleFrames = 0; expected = "metadata WORDs"; break;
            case "magic-negative": f.Team.MagicFrames = -1; expected = "metadata WORDs"; break;
            case "attack-overflow": f.Team.AttackFrames = 65536; expected = "metadata WORDs"; break;
            case "wait-negative": f.Team.ActWaitFrames = -1; expected = "metadata WORDs"; break;
            default: f.Team.AttackFrames = 2; expected = "unaddressable frames"; break;
        }
        f.Save();
        AssertThat(Failure(() => f.Load()).Contains(expected)).IsTrue();
    }

    [TestCase("frame-missing")]
    [TestCase("frame-duplicate")]
    [TestCase("frame-index")]
    [TestCase("frame-width")]
    [TestCase("frame-indices")]
    [TestCase("frame-alpha")]
    [TestCase("frame-source-offset")]
    [TestCase("sprite-source")]
    [TestCase("sprite-id")]
    [TestCase("player-count")]
    [TestCase("palette-size")]
    [TestCase("palette-mismatch")]
    [TestCase("background-identity")]
    [TestCase("background-size")]
    [TestCase("background-plane")]
    public void NativeLeafPlanesFramesPaletteAndDeclarationsAreStrictlyChecked(string fault)
    {
        using var f = new NativeBattleVisualProfileFixture();
        string expected;
        switch (fault)
        {
            case "frame-missing": f.Player.Frames.RemoveAt(5); expected = "nominal frame missing"; break;
            case "frame-duplicate": f.Player.Frames[5].Index = 4; expected = "duplicate"; break;
            case "frame-index": f.Player.Frames[5].Index = 65536; expected = "Index"; break;
            case "frame-width": f.Player.Frames[5].Width = 0; expected = "planes"; break;
            case "frame-indices": f.Player.Frames[5].Indices = [4]; expected = "planes"; break;
            case "frame-alpha": f.Enemy.Frames[2].Alpha = []; expected = "planes"; break;
            case "frame-source-offset": f.Player.Frames[0].SourceOffset = -1; expected = "planes"; break;
            case "sprite-source": f.Player.Source.Clear(); expected = "source declaration"; break;
            case "sprite-id": f.Player.SpriteId = -1; expected = "SpriteId"; break;
            case "player-count": f.Player.FrameCount = 9; f.Player.Frames.RemoveAt(9); expected = "frames 0..9"; break;
            case "palette-size": f.Palette.Rgb = new byte[767]; expected = "RGB768"; break;
            case "palette-mismatch": f.Palette.Rgb[0] = 255; expected = "active story palette"; break;
            case "background-identity": f.Background.SourceIdentity = ""; expected = "SourceIdentity"; break;
            case "background-size": f.Background.Width = 1; f.Background.Height = 64000; expected = "320x200"; break;
            default: f.Background.Alpha = [255]; expected = "planes"; break;
        }
        f.SaveLeaves(); f.RefreshHashes(); f.Save();
        AssertThat(Failure(() => f.Load()).Contains(expected)).IsTrue();
    }

    [TestCase]
    public void ExtraAddressableAliasesZeroAttackAndSignedPositionsArePreserved()
    {
        using var f = new NativeBattleVisualProfileFixture();
        f.Player.Frames.Add(new PalFrame { Index = 65535, Width = 1, Height = 1, Indices = [22], Alpha = [128] });
        // Addressable aliases may satisfy presentation indices beyond the nominal source frame count.
        f.Player.FrameCount = 9;
        f.Enemy.FrameCount = 1;
        f.Team.MagicFrames = 0; f.Team.AttackFrames = 0;
        f.Team.EnemyPosition = new(-32768, 32767); f.Profile.PlayerPosition = new(-2, -1);
        f.SaveLeaves(); f.RefreshHashes(); f.Save();
        var visual = f.Load();
        AssertThat(visual.PlayerSprite.Frames.Last().Index).IsEqual(65535);
        AssertThat(visual.Teams[18].AttackFrames).IsEqual(0);
        AssertThat(visual.Teams[18].EnemyPosition).IsEqual(new Vector2I(-32768, 32767));
        f.Team.MagicFrames = 1; f.Team.AttackFrames = 1; f.Save();
        AssertThat(f.Load().Teams[18].AttackFrames).IsEqual(1);
    }

    [TestCase("missing-hash")]
    [TestCase("extra-hash")]
    [TestCase("changed-bytes")]
    [TestCase("missing-file")]
    [TestCase("wrong-type")]
    [TestCase("bad-uri")]
    [TestCase("root-self-hash")]
    public void ExactHashGraphRejectsMissingMixedAndUnclaimedResources(string fault)
    {
        using var f = new NativeBattleVisualProfileFixture();
        string path = f.Profile.PlayerSpriteUri;
        string expected;
        switch (fault)
        {
            case "missing-hash": f.Profile.ResourceSha256.Remove(path); expected = "SHA-256"; break;
            case "extra-hash": f.Profile.ResourceSha256.Add(f.Story.Root + "/Numeric.tres", Godot.FileAccess.GetSha256(f.Story.Root + "/Numeric.tres")); expected = "exactly"; break;
            case "changed-bytes": File.AppendAllText(ProjectSettings.GlobalizePath(path), "\n;changed\n"); expected = "SHA-256"; break;
            case "missing-file": File.Delete(ProjectSettings.GlobalizePath(path)); expected = "missing"; break;
            case "wrong-type": f.Story.Save(new PalPalette(), "Player.tres"); f.RefreshHashes(); expected = "expected PalSprite"; break;
            case "bad-uri": f.Profile.PlayerSpriteUri = f.Root + "/../Player.tres"; expected = "normalized res://"; break;
            default: f.Profile.ResourceSha256.Add(f.Uri, Godot.FileAccess.GetSha256(f.Uri)); expected = "SHA-256"; break;
        }
        f.Save();
        AssertThat(Failure(() => f.Load()).Contains(expected)).IsTrue();
    }

    [TestCase]
    public void ExternalFrameAndMetadataAreIncludedInExactManifest()
    {
        using var f = new NativeBattleVisualProfileFixture();
        f.Story.Save(f.Player.Frames[0], "Frame.tres");
        f.Player.Frames[0] = ResourceLoader.Load<PalFrame>(f.Root + "/Frame.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
        f.Story.Save(f.Team, "Team.tres");
        f.Profile.Teams = [ResourceLoader.Load<PalBattleVisualTeam>(f.Root + "/Team.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep)];
        f.SaveLeaves(); f.RefreshHashes();
        foreach (string name in new[] { "Frame.tres", "Team.tres" }) f.Profile.ResourceSha256.Add(f.Root + "/" + name, Godot.FileAccess.GetSha256(f.Root + "/" + name));
        f.Save();
        var visual = f.Load();
        AssertThat(visual.ResourceSha256.Count).IsEqual(6);
        AssertThat(visual.PlayerSprite.Frames[0].Alpha[1]).IsEqual((byte)255);
    }

    [TestCase("cycle")]
    [TestCase("unknown-type")]
    [TestCase("external-uri")]
    [TestCase("uid-mismatch")]
    [TestCase("uid-match")]
    [TestCase("missing-external-metadata")]
    public void ExternalDependencyGraphIsCheckedBeforeDeepLoading(string fault)
    {
        using var f = new NativeBattleVisualProfileFixture();
        string extra = f.Root + "/Extra.tres";
        f.Story.Write("Extra.tres", "[gd_resource type=\"Resource\" format=3]\n[resource]\n");
        string target = extra, uidPart = "", expected = "";
        long uid = -1;
        try
        {
            switch (fault)
            {
                case "cycle":
                    f.Story.Write("Extra.tres", $"[gd_resource type=\"Resource\" format=3]\n[ext_resource type=\"Resource\" path=\"{extra}\" id=\"self\"]\n[resource]\nmetadata/next = ExtResource(\"self\")\n"); expected = "cyclic native content"; break;
                case "unknown-type": f.Story.Write("Extra.txt", "not native data"); target = f.Root + "/Extra.txt"; expected = "unsupported native content dependency type"; break;
                case "external-uri": target = "user://outside.tres"; expected = "normalized res://"; break;
                case "missing-external-metadata": File.Delete(ProjectSettings.GlobalizePath(extra)); expected = "missing native resource"; break;
                default:
                    uid = ResourceUid.CreateId();
                    ResourceUid.AddId(uid, fault == "uid-match" ? extra : f.Root + "/Other.tres");
                    uidPart = " uid=\"" + ResourceUid.IdToText(uid) + "\"";
                    expected = "UID/fallback identity mismatch"; break;
            }
            // Attach opaque Resource metadata, not a new business entry or test hook.
            string text = f.Story.Read("Visual.tres");
            text = text.Insert(text.IndexOf('\n') + 1, $"\n[ext_resource type=\"Resource\"{uidPart} path=\"{target}\" id=\"extra\"]\n");
            f.Story.Write("Visual.tres", text + "\nmetadata/extra = ExtResource(\"extra\")\n");
            if (fault == "uid-match")
            {
                f.Profile.ResourceSha256.Add(extra, Godot.FileAccess.GetSha256(extra));
                f.Save();
                text = f.Story.Read("Visual.tres");
                text = text.Insert(text.IndexOf('\n') + 1, $"\n[ext_resource type=\"Resource\"{uidPart} path=\"{target}\" id=\"extra\"]\n");
                f.Story.Write("Visual.tres", text + "\nmetadata/extra = ExtResource(\"extra\")\n");
                AssertThat(f.Load().ResourceSha256.Count).IsEqual(5);
            }
            else AssertThat(Failure(() => f.Load()).Contains(expected)).IsTrue();
        }
        finally { if (uid != -1 && ResourceUid.HasId(uid)) ResourceUid.RemoveId(uid); }
    }

    [TestCase]
    public void WrongRootTypeAndUnselectedNumericTeamDoNotGrantVisualCapabilities()
    {
        using var f = new NativeBattleVisualProfileFixture();
        f.Story.Numeric.Teams = [f.Story.Numeric.Teams[0], new PalBattleTeam { TeamId = 19,
            Enemies = [new PalBattleEnemy { Id = 99, Hp = 43, PhysicalResistance = 1, Cash = 1, Statuses = new int[9], Poisons = [0] }] }];
        f.Story.Save(f.Story.Numeric, "Numeric.tres"); f.Story.RefreshHashes(); f.Story.SaveProfile();
        var story = NativeStoryProfile.Load(f.Story.Uri);
        AssertThat(NativeBattleVisualProfile.Load(f.Uri, story).Teams.Count).IsEqual(1);
        AssertThat(story.BattleContent.Teams[19].Cash).IsEqual(1);
        f.Story.Save(new Resource(), "Visual.tres");
        AssertThat(Failure(() => NativeBattleVisualProfile.Load(f.Uri, story)).Contains("expected PalBattleVisualProfile")).IsTrue();
    }

    [TestCase]
    public void OldFormatRemainsLoadableButDoesNotGrantReferencePresentation()
    {
        using var f = new NativeBattleVisualProfileFixture();
        var visual = f.Load();
        AssertThat(visual.AttackEffectSprite is null && visual.AttackEffectIndex == -1 && visual.AttackEffectFrames.Count == 0).IsTrue();
        AssertThat(visual.Teams[18].IdleAnimSpeed).IsEqual(-1);
        AssertThat(Failure(visual.RequireReferencePresentation).Contains("attack effect")).IsTrue();
        AddEffect(f, 0); f.Save();
        AssertThat(Failure(f.Load().RequireReferencePresentation).Contains("IdleAnimSpeed")).IsTrue();
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(65535)]
    public void EffectGroupsUseIntTriplesNotRoleIdsOrWordWrappedFrames(int group)
    {
        using var f = new NativeBattleVisualProfileFixture();
        AddEffect(f, group); f.Team.IdleAnimSpeed = 0; f.Save();
        if (group == 65535)
        {
            AssertThat(Failure(() => f.Load()).Contains("unaddressable frames")).IsTrue();
            return;
        }
        var visual = f.Load(); visual.RequireReferencePresentation();
        AssertThat(visual.AttackEffectIndex == group && visual.AttackEffectSprite!.SpriteId == 97).IsTrue();
        AssertThat(visual.AttackEffectFrames.Select(x => x.Index).SequenceEqual(group == 0 ? new[] { 0, 1, 2 } : new[] { 3, 4, 5 })).IsTrue();
        AssertThat(visual.Teams[18].IdleAnimSpeed).IsEqual(0);
        AssertThat(((IList<PalFrame>)visual.AttackEffectFrames).IsReadOnly).IsTrue();
        File.Delete(ProjectSettings.GlobalizePath(f.Root + "/Effect.tres"));
        AssertThat(visual.AttackEffectFrames[0].Indices.SequenceEqual(new byte[] { 4, 19 })).IsTrue();
        AssertThat(visual.AttackEffectFrames[0].Alpha.SequenceEqual(new byte[] { 0, 255 })).IsTrue();
    }

    [TestCase("uri-only")]
    [TestCase("index-only")]
    [TestCase("negative")]
    [TestCase("overflow")]
    [TestCase("missing-frame")]
    [TestCase("wrong-type")]
    [TestCase("changed")]
    [TestCase("idle-negative")]
    [TestCase("idle-overflow")]
    public void PresentEffectAndIdleMetadataAreValidatedEvenWithoutCapabilitySelection(string fault)
    {
        using var f = new NativeBattleVisualProfileFixture(); AddEffect(f, 0);
        string expected;
        switch (fault)
        {
            case "uri-only": f.Profile.AttackEffectIndex = -1; expected = "paired"; break;
            case "index-only": f.Profile.AttackEffectSpriteUri = ""; expected = "paired"; break;
            case "negative": f.Profile.AttackEffectIndex = -2; expected = "WORD"; break;
            case "overflow": f.Profile.AttackEffectIndex = 65536; expected = "WORD"; break;
            case "missing-frame": f.Profile.AttackEffectIndex = 1; expected = "unaddressable"; break;
            case "wrong-type": f.Story.Save(f.Palette, "Effect.tres"); f.Profile.ResourceSha256[f.Root + "/Effect.tres"] = Godot.FileAccess.GetSha256(f.Root + "/Effect.tres"); expected = "expected PalSprite"; break;
            case "changed": File.AppendAllText(ProjectSettings.GlobalizePath(f.Root + "/Effect.tres"), "\n; changed"); expected = "SHA-256"; break;
            case "idle-negative": f.Team.IdleAnimSpeed = -2; expected = "WORD"; break;
            default: f.Team.IdleAnimSpeed = 65536; expected = "WORD"; break;
        }
        f.Save(); AssertThat(Failure(() => f.Load()).Contains(expected)).IsTrue();
    }

    private static void AddEffect(NativeBattleVisualProfileFixture f, int group)
    {
        if (group == 1)
            for (int i = 3; i < 6; i++) f.Enemy.Frames.Add(new PalFrame { Index = i, Width = 2, Height = 1, Indices = [4, 19], Alpha = [0, 255] });
        f.Story.Save(f.Enemy, "Effect.tres");
        f.Profile.AttackEffectSpriteUri = f.Root + "/Effect.tres"; f.Profile.AttackEffectIndex = group;
        f.Profile.ResourceSha256.Add(f.Root + "/Effect.tres", Godot.FileAccess.GetSha256(f.Root + "/Effect.tres"));
    }

    private static string Failure(Action action)
    {
        try { action(); }
        catch (InvalidDataException error) { return error.Message; }
        throw new InvalidOperationException("Expected InvalidDataException");
    }
}
