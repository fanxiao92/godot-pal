using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Pal.Core;
using GdUnit4;
using Godot;
using Pal.Game.Profiles;
using Pal.Game.Resources;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

[TestSuite, RequireGodotRuntime]
public class NativeStoryProfileTests
{
    [TestCase]
    public void MissingExplicitVersionIsRejectedAfterNativeSaveLoad()
    {
        string root = "res://Tests/RuntimeStory/" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(root));
        try
        {
            string uri = root + "/Story.tres";
            NativeProfileFixture.SaveResource(new PalStoryProfile(), uri);
            string error = Failure(() => NativeStoryProfile.Load(uri));
            AssertThat(error.Contains("FormatVersion must be 1")).IsTrue();
        }
        finally { Directory.Delete(ProjectSettings.GlobalizePath(root), true); }
    }

    [TestCase("zh-Hans")]
    [TestCase("en")]
    public void CompleteNativeBindingReturnsSelectedContentAndAllBranchRequirements(string language)
    {
        using var fixture = new NativeStoryProfileFixture();
        var profile = NativeStoryProfile.Load(fixture.Uri, language);
        AssertThat(profile.Language.Language).IsEqual(language);
        AssertThat(profile.Entry.OwnerId).IsEqual(1);
        AssertThat(profile.Entry.SourceAddress).IsEqual(1);
        AssertThat(profile.Events.InstructionCount).IsEqual(13);
        AssertThat(profile.BattleContent.InitialRole.RoleId).IsEqual(0);
        AssertThat(profile.BattleContent.InitialRole.Hp).IsEqual(100);
        AssertThat(profile.BattleContent.Teams[18].Id).IsEqual(7);
        AssertThat(profile.Exploration.Rules.MapId).IsEqual(12);
        AssertThat(profile.Exploration.Leader.X).IsEqual((short)320);
        AssertThat(profile.Palette.Count).IsEqual(256);
        AssertThat(profile.Prompts.Count).IsEqual(3);
        AssertThat(profile.Requirements.PortraitIds.SequenceEqual(new[] { 7, 8, 9, 10 })).IsTrue();
        AssertThat(profile.Requirements.BattleTeamIds.SequenceEqual(new[] { 18 })).IsTrue();
        AssertThat(profile.Requirements.DisplayBlocks.Select(x => x.MessageReferences[0].Position.SourceAddress).Order().SequenceEqual(new[] { 2, 5, 8, 11 })).IsTrue();
        AssertThat(profile.Requirements.DisplayBlocks.Select(x => x.Content.StartMessageId).Order().SequenceEqual(new[] { 61, 62, 63, 64 })).IsTrue();
        AssertThat(profile.Language.Resolve(64, 0).Parts[0].Text!).IsEqual(language == "en" ? "D" : "丁");
        AssertThat(profile.ResourceSha256.Keys.Order().SequenceEqual(fixture.ExpectedPaths.Order())).IsTrue();
        AssertThat(profile.ResourceSha256.Count).IsEqual(21);
        AssertThat(profile.Portraits.Keys.Order().SequenceEqual(new[] { 7, 8, 9, 10 })).IsTrue();
        AssertThat(profile.Font.GetGlyph(language == "en" ? 68 : 0x4e01).IsSet(0, 0)).IsFalse();
        AssertThat(profile.Events.GetSource(1).Operands[0]).IsEqual(99);
    }

    [TestCase]
    public void SelectedPartyClosureBindsTwoPlayersAndEnemySlots()
    {
        using var fixture = new NativeStoryProfileFixture(party: true);
        var profile = NativeStoryProfile.Load(fixture.Uri, "en");
        AssertThat(profile.BattleContent.Players.Count).IsEqual(2);
        AssertThat(profile.BattleContent.Players.Select(x => x.RoleId).SequenceEqual(new[] { 0, 1 })).IsTrue();
        AssertThat(profile.BattleContent.InitialRoles.Keys.Order().SequenceEqual(new[] { 0, 1 })).IsTrue();
        AssertThat(profile.BattleContent.EnemyTeams[18].Count).IsEqual(2);
        AssertThat(profile.BattleContent.EnemyTeams[18].Select(x => x.Id).SequenceEqual(new[] { 7, 7 })).IsTrue();
        AssertThat(profile.Requirements.BattleTeamIds.SequenceEqual(new[] { 18 })).IsTrue();
    }

    [TestCase("version")]
    [TestCase("identity")]
    [TestCase("bundle")]
    [TestCase("uri")]
    [TestCase("entry-file")]
    [TestCase("entry-id")]
    [TestCase("entry-address")]
    [TestCase("entry-zero")]
    [TestCase("owner")]
    [TestCase("selected-language")]
    public void AuthoredSelectionMustBeExplicitAndMatchBoundContent(string fault)
    {
        using var fixture = new NativeStoryProfileFixture();
        string expected;
        switch (fault)
        {
            case "version": fixture.Profile.FormatVersion = 2; expected = "FormatVersion must be 1"; break;
            case "identity": fixture.Profile.SourceIdentity = " "; expected = "SourceIdentity"; break;
            case "bundle": fixture.Profile.BundleSha256 = new string('c', 64); expected = "BundleSha256 mismatch"; break;
            case "uri": fixture.Profile.StaticProfile = fixture.Root + "/../Profile.tres"; expected = "normalized res://"; break;
            case "entry-file": fixture.Profile.EntryFileId = "missing"; expected = "missing entry"; break;
            case "entry-id": fixture.Profile.EntryId = "missing"; expected = "missing entry"; break;
            case "entry-address": fixture.Profile.EntryAddress = 3; expected = "owner-trigger-entry"; break;
            case "entry-zero": fixture.Profile.EntryAddress = 0; expected = "owner-trigger-entry"; break;
            case "owner": fixture.Profile.OwnerId = 2; expected = "owner-trigger-entry"; break;
            default: expected = "selected language"; break;
        }
        fixture.SaveProfile();
        AssertThat(Failure(() => NativeStoryProfile.Load(fixture.Uri, fault == "selected-language" ? "fr" : "en")).Contains(expected)).IsTrue();
    }

    [TestCase("static")]
    [TestCase("numeric")]
    [TestCase("glyph")]
    [TestCase("role")]
    [TestCase("texture-source")]
    [TestCase("texture-import")]
    [TestCase("other-language")]
    [TestCase("outcomes")]
    public void ExactHashManifestCannotOmitAnyDirectNestedOrTextureDependency(string missing)
    {
        using var fixture = new NativeStoryProfileFixture();
        string path = missing switch
        {
            "static" => fixture.Static.Uri, "numeric" => fixture.Root + "/Numeric.tres", "glyph" => fixture.Root + "/Glyph.tres",
            "role" => fixture.Root + "/Role.tres", "texture-source" => NativeStoryProfileFixture.TextureSource,
            "texture-import" => NativeStoryProfileFixture.TextureSource + ".import",
            "other-language" => fixture.Root + "/Language.zh-Hans.tres", _ => fixture.Root + "/outcomes.json"
        };
        fixture.Profile.ResourceSha256.Remove(path); fixture.SaveProfile();
        string error = Failure(() => NativeStoryProfile.Load(fixture.Uri, "en"));
        AssertThat(error.Contains(path) && error.Contains("missing hash")).IsTrue();
    }

    [TestCase("changed")]
    [TestCase("missing")]
    [TestCase("extra")]
    [TestCase("uppercase-hash")]
    [TestCase("nonnormalized-key")]
    public void DeclaredBytesAndKeysAreCheckedBeforeDeepNativeLoading(string fault)
    {
        using var fixture = new NativeStoryProfileFixture();
        string glyph = fixture.Root + "/Glyph.tres";
        string expected = "SHA-256";
        switch (fault)
        {
            case "changed": fixture.Edit("Glyph.tres", "Codepoint = 65", "Codepoint = 66"); break;
            case "missing": File.Delete(ProjectSettings.GlobalizePath(glyph)); break;
            case "extra": fixture.Write("Extra.json", "{}"); fixture.Profile.ResourceSha256[fixture.Root + "/Extra.json"] = Godot.FileAccess.GetSha256(fixture.Root + "/Extra.json"); expected = "exactly"; break;
            case "uppercase-hash": fixture.Profile.ResourceSha256[glyph] = new string('A', 64); break;
            default: fixture.Profile.ResourceSha256[fixture.Root + "/./Glyph.tres"] = fixture.Profile.ResourceSha256[glyph]; expected = "normalized res://"; break;
        }
        fixture.SaveProfile();
        AssertThat(Failure(() => NativeStoryProfile.Load(fixture.Uri)).Contains(expected)).IsTrue();
    }

    [TestCase("static-scene")]
    [TestCase("static-profile-bundle")]
    [TestCase("native-owner")]
    [TestCase("role")]
    [TestCase("unsupported-battle")]
    [TestCase("multiplayer-presentation")]
    [TestCase("auto-entry")]
    [TestCase("disabled-entry")]
    [TestCase("complete-events")]
    [TestCase("static-frame")]
    [TestCase("palette")]
    public void MatchingHashesDoNotGrantSemanticOrSelectedEntryCapabilities(string fault)
    {
        using var fixture = new NativeStoryProfileFixture();
        string expected;
        switch (fault)
        {
            case "static-scene": fixture.Static.Profile.SceneId = 4; expected = "SceneId mismatch"; break;
            case "static-profile-bundle": fixture.Static.Profile.BundleSha256 = new string('c', 64); expected = "BundleSha256 mismatch"; break;
            case "native-owner": fixture.Static.Scene.Objects[0].WTriggerScript = 2; expected = "native scene owner trigger"; break;
            case "role":
                fixture.Numeric.Roles[0].RoleId = fixture.Numeric.Players[0].RoleId = fixture.Numeric.Growth[0].RoleId = 1;
                fixture.Save(fixture.Numeric.Roles[0], "Role.tres"); fixture.Save(fixture.Numeric, "Numeric.tres"); expected = "static leader role0"; break;
            case "unsupported-battle": fixture.Numeric.Teams[0].Enemies[0].Cash = 1; fixture.Save(fixture.Numeric, "Numeric.tres"); expected = "shared.json#L0003"; break;
            case "multiplayer-presentation": fixture.Numeric.Teams[0].Enemies = [fixture.Numeric.Teams[0].Enemies[0], fixture.Numeric.Teams[0].Enemies[0]]; fixture.Save(fixture.Numeric, "Numeric.tres"); expected = "multiplayer presentation is not connected"; break;
            case "auto-entry": fixture.Edit("shared.json", "\"slot\":\"wTriggerScript\",\"kind\":\"trigger\"", "\"slot\":\"wAutoScript\",\"kind\":\"auto\""); expected = "owner-trigger-entry"; break;
            case "disabled-entry": fixture.Edit("shared.json", "\"target\":\"L0001\",\"sourceAddress\":1", "\"target\":null,\"sourceAddress\":0"); fixture.Profile.EntryAddress = 0; expected = "disabled entry"; break;
            case "complete-events": fixture.Edit("shared.json", "\"opcode\":134", "\"opcode\":0"); expected = "Registered opcode"; break;
            case "static-frame": fixture.Static.Scene.Sprites[0].FrameCount = 2; expected = "nominal frame missing"; break;
            default: fixture.Static.Scene.Palette.Rgb = new byte[3]; fixture.Save(fixture.Static.Scene.Palette, "Palette.tres"); expected = "256-color"; break;
        }
        fixture.Static.SavePacked(); fixture.RefreshHashes(); fixture.SaveProfile();
        double orphans = Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
        AssertThat(Failure(() => NativeStoryProfile.Load(fixture.Uri)).Contains(expected)).IsTrue();
        AssertThat(Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount)).IsEqual(orphans);
    }

    [TestCase("zh-identity")]
    [TestCase("en-identity")]
    [TestCase("zh-version")]
    [TestCase("zh-loss-range")]
    [TestCase("en-flee-range")]
    [TestCase("zh-flee-glyph")]
    [TestCase("en-next-glyph")]
    [TestCase("glyph-bitmap")]
    public void BothLanguagesAndAllPostBattleGlyphsAreActuallyValidated(string fault)
    {
        using var fixture = new NativeStoryProfileFixture();
        string expected;
        switch (fault)
        {
            case "zh-identity": fixture.Chinese.Language = "en"; expected = "language identity mismatch"; break;
            case "en-identity": fixture.English.SourceIdentity = ""; expected = "language identity mismatch"; break;
            case "zh-version": fixture.Chinese.FormatVersion = 2; expected = "unsupported version"; break;
            case "zh-loss-range": fixture.Chinese.Messages.RemoveAt(2); expected = "63+0"; break;
            case "en-flee-range": fixture.English.Messages.RemoveAt(3); expected = "64+0"; break;
            case "zh-flee-glyph": fixture.Font.Glyphs.RemoveAt(7); expected = "U+4E01"; break;
            case "en-next-glyph": fixture.Font.Glyphs.RemoveAt(1); expected = "U+0042"; break;
            default: fixture.Font.Glyphs[2].Width = 16; expected = "invalid bitmap dimensions"; break;
        }
        fixture.Save(fixture.Chinese, "Language.zh-Hans.tres"); fixture.Save(fixture.English, "Language.en.tres"); fixture.Save(fixture.Font, "Font.tres");
        fixture.RefreshHashes(); fixture.SaveProfile();
        string error = Failure(() => NativeStoryProfile.Load(fixture.Uri, "en"));
        AssertThat(error.Contains(expected)).IsTrue();
        if (fault.Contains("glyph")) AssertThat(error.Contains("Font.tres") && error.Contains("json#L")).IsTrue();
    }

    [TestCase("missing-map")]
    [TestCase("extra-map")]
    [TestCase("zero-map")]
    [TestCase("portrait")]
    [TestCase("prompt")]
    [TestCase("font-identity")]
    public void PortraitMappingIsExactAndNativeImagesAndIdentitiesAreReal(string fault)
    {
        using var fixture = new NativeStoryProfileFixture();
        string expected;
        switch (fault)
        {
            case "missing-map": fixture.Profile.Portraits.Remove(10); fixture.Profile.ResourceSha256.Remove(fixture.Root + "/Portrait-10.tres"); expected = "exactly the selected entry"; break;
            case "extra-map": fixture.Profile.Portraits.Add(11, fixture.Root + "/Portrait-10.tres"); expected = "exactly the selected entry"; break;
            case "zero-map": fixture.Profile.Portraits.Add(0, fixture.Root + "/Portrait-10.tres"); expected = "nonzero WORD"; break;
            case "portrait": fixture.Save(new PalDialogueIndexedImage { SourceIdentity = "bad", Width = 2, Height = 1, PaletteIndices = [1], Alpha = [255] }, "Portrait-10.tres"); fixture.RefreshHashes(); expected = "Portrait-10.tres"; break;
            case "prompt": fixture.Save(new PalDialogueIndexedImage { Width = 1, Height = 1, PaletteIndices = [1], Alpha = [255] }, "Prompt-02.tres"); fixture.RefreshHashes(); expected = "Prompt-02.tres"; break;
            default: fixture.Font.SourceIdentity = ""; fixture.Save(fixture.Font, "Font.tres"); fixture.RefreshHashes(); expected = "font identity"; break;
        }
        fixture.SaveProfile();
        AssertThat(Failure(() => NativeStoryProfile.Load(fixture.Uri)).Contains(expected)).IsTrue();
    }

    [TestCase]
    public void CoreProjectionIsDeeplyIsolatedAndNativeResourcesBelongToThisLoad()
    {
        using var fixture = new NativeStoryProfileFixture();
        var cached = ResourceLoader.Load<PalDialogueFont>(fixture.Root + "/Font.tres");
        var loaded = NativeStoryProfile.Load(fixture.Uri, "en");
        AssertThat(ReferenceEquals(loaded.Font, cached) || ReferenceEquals(loaded.Font, fixture.Font)).IsFalse();
        cached.Glyphs[1].Width = 16;
        fixture.English.Messages[0].Entries[0].Text = "changed";
        fixture.Numeric.Roles[0].Equipment[0] = 1;
        fixture.Static.Scene.Objects[0].X = 900;
        fixture.Profile.ResourceSha256.Clear(); fixture.Profile.Portraits.Clear();
        AssertThat(loaded.Font.GetGlyph(66).Width).IsEqual(8);
        AssertThat(loaded.Language.Resolve(61, 0).Parts[0].Text!).IsEqual("A");
        AssertThat(loaded.BattleContent.InitialRole.Equipment[0]).IsEqual(0);
        AssertThat(loaded.Exploration.Rules.Objects[0].X).IsEqual((ushort)600);
        AssertThat(loaded.ResourceSha256.Count).IsEqual(21);
        AssertThat(loaded.Portraits.Count).IsEqual(4);
        ReadOnly(() => ((IList<int>)loaded.BattleContent.InitialRole.Equipment)[0] = 8);
        ReadOnly(() => ((IList<TriggerDisplayRequirement>)loaded.Requirements.DisplayBlocks).Clear());
        ReadOnly(() => ((IList<PalDialogueIndexedImage>)loaded.Prompts).Clear());
        ReadOnly(() => ((IDictionary<int, PalDialogueIndexedImage>)loaded.Portraits).Clear());
        ReadOnly(() => ((IDictionary<string, string>)loaded.ResourceSha256).Clear());
        // Native Resource is intentionally mutable, unlike Core; a fresh load is not this instance.
        loaded.Font.Glyphs[1].Width = 16;
        var second = NativeStoryProfile.Load(fixture.Uri, "en");
        AssertThat(second.Font.GetGlyph(66).Width).IsEqual(8);
        AssertThat(second.Language.Resolve(61, 0).Parts[0].Text!).IsEqual("A");
    }

    [TestCase("cycle")]
    [TestCase("unknown-type")]
    [TestCase("external-uri")]
    [TestCase("uid-mismatch")]
    [TestCase("uid-match")]
    public void ActualExternalDependencyGraphFailsClosedBeforeNativeLoad(string fault)
    {
        using var fixture = new NativeStoryProfileFixture();
        string extra = fixture.Root + "/Extra.tres";
        fixture.Write("Extra.tres", "[gd_resource type=\"Resource\" format=3]\n\n[resource]\n");
        string target = extra, uidPart = "", expected = "";
        long uid = -1;
        try
        {
            switch (fault)
            {
                case "cycle":
                    fixture.Write("Extra.tres", $"[gd_resource type=\"Resource\" format=3]\n[ext_resource type=\"Resource\" path=\"{extra}\" id=\"self\"]\n[resource]\nmetadata/next = ExtResource(\"self\")\n"); expected = "cyclic native content"; break;
                case "unknown-type": fixture.Write("Extra.txt", "not a native resource"); target = fixture.Root + "/Extra.txt"; expected = "unsupported native content dependency type"; break;
                case "external-uri": target = "user://outside.tres"; expected = "normalized res://"; break;
                default:
                    uid = ResourceUid.CreateId();
                    ResourceUid.AddId(uid, fault == "uid-match" ? extra : fixture.Root + "/Other.tres");
                    uidPart = " uid=\"" + ResourceUid.IdToText(uid) + "\"";
                    expected = "UID/fallback identity mismatch"; break;
            }
            string text = fixture.Read("Numeric.tres");
            int firstLine = text.IndexOf('\n');
            text = text.Insert(firstLine + 1, $"\n[ext_resource type=\"Resource\"{uidPart} path=\"{target}\" id=\"extra\"]\n");
            fixture.Write("Numeric.tres", text + "\nmetadata/extra = ExtResource(\"extra\")\n");
            fixture.RefreshHashes();
            fixture.Profile.ResourceSha256[extra] = Godot.FileAccess.GetSha256(extra);
            if (fault == "unknown-type") fixture.Profile.ResourceSha256[target] = Godot.FileAccess.GetSha256(target);
            if (fault is "external-uri" or "unknown-type") fixture.Profile.ResourceSha256.Remove(extra);
            fixture.Profile.ResourceSha256[fixture.Root + "/Numeric.tres"] = Godot.FileAccess.GetSha256(fixture.Root + "/Numeric.tres");
            fixture.SaveProfile();
            if (fault == "uid-match") AssertThat(NativeStoryProfile.Load(fixture.Uri).ResourceSha256.Count).IsEqual(22);
            else AssertThat(Failure(() => NativeStoryProfile.Load(fixture.Uri)).Contains(expected)).IsTrue();
        }
        finally { if (uid != -1 && ResourceUid.HasId(uid)) ResourceUid.RemoveId(uid); }
    }

    [TestCase]
    public void RootAndNumericResourcesRequireTheirActualNativeTypes()
    {
        using var fixture = new NativeStoryProfileFixture();
        fixture.Save(new Resource(), "Wrong.tres");
        AssertThat(Failure(() => NativeStoryProfile.Load(fixture.Root + "/Wrong.tres")).Contains("expected PalStoryProfile")).IsTrue();
        fixture.Save(new Resource(), "Numeric.tres"); fixture.RefreshHashes();
        fixture.Profile.ResourceSha256.Remove(fixture.Root + "/Role.tres");
        fixture.SaveProfile();
        AssertThat(Failure(() => NativeStoryProfile.Load(fixture.Uri)).Contains("expected PalBattleContent")).IsTrue();
    }

    [TestCase]
    public void UnselectedNumericTeamsRemainDataRatherThanGrantedCapabilities()
    {
        using var fixture = new NativeStoryProfileFixture();
        fixture.Numeric.Teams = [fixture.Numeric.Teams[0], new PalBattleTeam
        {
            TeamId = 19, Enemies = [new PalBattleEnemy { Id = 99, Hp = 43, PhysicalResistance = 1,
                Statuses = new int[9], Poisons = [0], Cash = 1 }]
        }];
        fixture.Save(fixture.Numeric, "Numeric.tres"); fixture.RefreshHashes(); fixture.SaveProfile();
        var loaded = NativeStoryProfile.Load(fixture.Uri);
        AssertThat(loaded.BattleContent.Teams[19].Cash).IsEqual(1);
        AssertThat(loaded.Requirements.BattleTeamIds.SequenceEqual(new[] { 18 })).IsTrue();
    }

    private static void ReadOnly(Action action)
    {
        try { action(); }
        catch (NotSupportedException) { return; }
        throw new InvalidOperationException("Expected a read-only collection");
    }

    private static string Failure(Action action)
    {
        try { action(); }
        catch (InvalidDataException error) { return error.Message; }
        throw new InvalidOperationException("Expected InvalidDataException");
    }
}
