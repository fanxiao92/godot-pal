using System;
using System.IO;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using GdUnit4;
using Godot;
using Pal.Game.Editor;
using Pal.Game.Profiles;
using static GdUnit4.Assertions;

namespace Pal.Game.Tests;

[TestSuite, RequireGodotRuntime]
public class PrepareStoryPackageTests
{
    [TestCase]
    public void PublicPreparePublishesCompleteEventsAndStrictTypedProfilesWithoutChangingSources()
    {
        using var source = new Preparation();
        string[] before = source.Snapshot();
        Directory.CreateDirectory(source.Output);
        var result = PrepareStoryPackage.Prepare(source.Input, source.Output);
        AssertThat(result.StoryUri).IsEqual(source.OutputUri + "/Story.tres");
        AssertThat(result.VisualUri).IsEqual(source.OutputUri + "/Visual.tres");
        var story = NativeStoryProfile.Load(result.StoryUri, "en");
        var visual = NativeBattleVisualProfile.Load(result.VisualUri, story);
        AssertThat(story.Entry.OwnerId).IsEqual(1);
        AssertThat(story.Requirements.BattleTeamIds.ToArray()).IsEqual(new[] { 18 });
        AssertThat(visual.Teams[18].TargetId).IsEqual(7);
        AssertThat(story.ResourceSha256.ContainsKey(source.OutputUri + "/Events/outcomes.json")).IsTrue();
        AssertThat(File.ReadAllBytes(Path.Combine(source.Output, "Events", "shared.json")))
            .IsEqual(File.ReadAllBytes(Path.Combine(source.Input, "events", "shared.json")));
        AssertThat(Directory.GetFileSystemEntries(source.Output).Select(Path.GetFileName).Order().ToArray())
            .IsEqual(new[] { "Events", "Story.tres", "Visual.tres" });
        AssertThat(source.Snapshot()).IsEqual(before);
    }

    [TestCase]
    public void AuthoredPartyPackagesPrepareAndLoadInBothLanguages()
    {
        // Committed #47 two-player authored inputs through the unchanged public prepare chain;
        // outputs are fresh GUID-owned directories, so the old AuthoredBattle* outputs stay untouched.
        string repository = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../.."));
        foreach (string scenario in new[] { "PartyWin", "PartyLoss" })
        {
            string source = Path.Combine(repository, "samples/authored-battle", scenario);
            string[] before = SnapshotDirectory(source);
            using (var fixture = new AuthoredStoryFixture(scenario))
            foreach (string language in new[] { "zh-Hans", "en" })
            {
                var story = NativeStoryProfile.Load(fixture.StoryUri, language);
                var visual = NativeBattleVisualProfile.Load(fixture.VisualUri, story);
                foreach (string path in Directory.GetFiles(Path.Combine(source, "Story/events")))
                    AssertThat(File.ReadAllBytes(ProjectSettings.GlobalizePath(fixture.Root + "/Story/Events/" + Path.GetFileName(path))))
                        .IsEqual(File.ReadAllBytes(path));
                AssertThat(story.Events.InstructionCount).IsEqual(10);
                int[] hp = scenario == "PartyWin" ? [100, 95] : [20, 20];
                AssertThat(story.BattleContent.InitialRoles.OrderBy(p => p.Key).Select(p => p.Value.Hp).ToArray()).IsEqual(hp);
                AssertThat(story.BattleContent.InitialRoles.OrderBy(p => p.Key).Select(p => p.Value.Mp).ToArray()).IsEqual(new[] { 20, 20 });
                AssertThat(story.BattleContent.Players.Select(p => p.Hp).ToArray()).IsEqual(hp);
                AssertThat(story.BattleContent.Players.Count).IsEqual(2);
                AssertThat(story.BattleContent.EnemyTeams[18].Count).IsEqual(2);
                AssertThat(story.BattleContent.EnemyTeams[18].Select(x => x.Id).SequenceEqual(new[] { 7, 7 })).IsTrue();
                AssertThat(visual.PlayerPosition).IsEqual(new Vector2I(200, 176));
                AssertThat(visual.Teams[18].EnemySlots.Select(s => s.TargetId).SequenceEqual(new[] { 7, 7 })).IsTrue();
                AssertThat(visual.Teams[18].EnemySlots.Select(s => s.EnemyPosition)
                    .SequenceEqual(new[] { new Vector2I(100, 120), new Vector2I(128, 104) })).IsTrue();
            }
            AssertThat(SnapshotDirectory(source)).IsEqual(before);
        }
    }

    [TestCase]
    public void AuthoredPartyHpMpPrefixPreparesAndLoadsThroughStrictNativeProfiles()
    {
        string repository = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../.."));
        string source = Path.Combine(repository, "samples/authored-battle/PartyWin");
        string[] before = SnapshotDirectory(source);
        using (var fixture = new AuthoredStoryFixture("PartyWin", partyHpMpPrefixAmount: -3))
        foreach (string language in new[] { "zh-Hans", "en" })
        {
            var story = NativeStoryProfile.Load(fixture.StoryUri, language);
            if (story.Events.InstructionCount != 11)
                throw new InvalidOperationException($"M6-155 prefix instruction count: expected 11, actual {story.Events.InstructionCount}.");
            var visual = NativeBattleVisualProfile.Load(fixture.VisualUri, story);
            visual.RequireReferencePresentation();
            visual.RequireNumberPresentation();
            // Independent complete literal stream reviewed in U12, not derived from the fixture conversion.
            int[][] rows = [ [134, 0, 65535, 8], [29, 1, 65533, 0], [60, 0, 0, 0],
                [65535, 1, 0, 0], [7, 18, 8, 0], [60, 0, 0, 0], [65535, 2, 0, 0],
                [0, 0, 0, 0], [60, 0, 0, 0], [65535, 3, 0, 0], [0, 0, 0, 0] ];
            string[] contents = [
                """{"op":"raw"}""",
                """{"op":"changeHpMp","scope":1,"amount":-3}""",
                """{"op":"setDialog","box":"upper","portraitId":0,"color":0,"rngWord":0}""",
                """{"op":"showDialog","messageId":1,"descriptionLineWord":0}""",
                """{"op":"startBattle","enemyTeamId":18,"lossTarget":"L0008","isBoss":true,"fleeTarget":null}""",
                """{"op":"setDialog","box":"upper","portraitId":0,"color":0,"rngWord":0}""",
                """{"op":"showDialog","messageId":2,"descriptionLineWord":0}""",
                """{"op":"end","mode":"keep"}""",
                """{"op":"setDialog","box":"upper","portraitId":0,"color":0,"rngWord":0}""",
                """{"op":"showDialog","messageId":3,"descriptionLineWord":0}""",
                """{"op":"end","mode":"keep"}"""
            ];
            var bytes = new byte[88];
            for (int address = 0; address < rows.Length; address++)
            {
                var actualSource = story.Events.GetSource(address);
                AssertThat(actualSource.Position.FileId).IsEqual("arena");
                AssertThat(actualSource.Position.SourceAddress).IsEqual(address);
                AssertThat(actualSource.Position.Label).IsEqual($"L{address:D4}");
                AssertThat(actualSource.Opcode).IsEqual(rows[address][0]);
                AssertThat(actualSource.Operands.ToArray()).IsEqual(rows[address].Skip(1).ToArray());
                for (int word = 0; word < 4; word++)
                    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(address * 8 + word * 2), checked((ushort)rows[address][word]));
                var expected = JsonNode.Parse(contents[address])!;
                expected["label"] = $"L{address:D4}";
                expected["next"] = address == 10 ? null : $"L{address + 1:D4}";
                expected["source"] = new JsonObject { ["index"] = address, ["opcode"] = rows[address][0],
                    ["operands"] = new JsonArray(rows[address].Skip(1).Select(n => (JsonNode?)JsonValue.Create(n)).ToArray()) };
                AssertThat(JsonNode.DeepEquals(JsonNode.Parse(story.Events.GetCommandContent(address).GetRawText()), expected)).IsTrue();
            }
            const string streamSha256 = "5fec1c76900dde7668d4810bf9312e4b1b462b2b1c66554101ec81758c343e82";
            AssertThat(Convert.ToHexStringLower(SHA256.HashData(bytes))).IsEqual(streamSha256);
            AssertThat(story.Entry.SourceAddress).IsEqual(1);
            AssertThat(story.Events.GetSource(story.Entry.SourceAddress).Position.Label).IsEqual("L0001");
            AssertThat(story.Entry.OwnerId).IsEqual(1);
            AssertThat(story.SourceIdentity.Contains("Test-owned PartyWin changeHpMp prefix: scope=1, amount=-3; original authored stream derived, not retail SSS.MKF.", StringComparison.Ordinal)).IsTrue();
            AssertThat(story.BundleSha256).IsEqual("a6e7c2b10e379bfc6cd34898302d49ab8a9a81e5b91fd035f5a5b9b241d638fc");
            string eventsUri = fixture.Root + "/Story/Events/";
            var index = JsonNode.Parse(File.ReadAllText(ProjectSettings.GlobalizePath(eventsUri + "index.json")))!;
            AssertThat(index["source"]!["instructionCount"]!.GetValue<int>()).IsEqual(11);
            AssertThat(index["source"]!["byteLength"]!.GetValue<int>()).IsEqual(88);
            AssertThat(index["source"]!["sha256"]!.GetValue<string>()).IsEqual(streamSha256);
            foreach (string name in new[] { "index.json", "arena.json" })
                AssertThat(story.ResourceSha256[eventsUri + name]).IsEqual(Convert.ToHexStringLower(
                    SHA256.HashData(File.ReadAllBytes(ProjectSettings.GlobalizePath(eventsUri + name)))));
            AssertThat(story.BattleContent.InitialRoles[0].Hp).IsEqual(100);
            AssertThat(story.BattleContent.InitialRoles[1].Hp).IsEqual(95);
            AssertThat(story.BattleContent.InitialRoles[0].Mp).IsEqual(20);
            AssertThat(story.BattleContent.InitialRoles[1].Mp).IsEqual(20);
            AssertThat(story.BattleContent.Players.Select(p => p.Hp).ToArray()).IsEqual(new[] { 100, 95 });
            AssertThat(visual.Teams[18].EnemySlots.Select(s => s.TargetId).ToArray()).IsEqual(new[] { 7, 7 });
            AssertThat(story.Language.Resolve(1, 0).Parts[0].Text!).IsEqual(language == "en" ? "FIGHT" : "战");
            AssertThat(story.Language.Resolve(2, 0).Parts[0].Text!).IsEqual(language == "en" ? "WIN" : "胜");
            AssertThat(story.Language.Resolve(3, 0).Parts[0].Text!).IsEqual(language == "en" ? "LOSE" : "败");
        }
        AssertThat(SnapshotDirectory(source)).IsEqual(before);
    }

    private static string[] SnapshotDirectory(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(p => Path.GetRelativePath(directory, p) + ":" +
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p)))).ToArray();

    [TestCase]
    public void PublicPreparePublishesExplicitEnemySlotsForASingleSlotTeam()
    {
        using var source = new Preparation();
        var metadata = JsonNode.Parse(File.ReadAllText(source.Metadata))!;
        var team = (JsonObject)metadata["visual"]!["teams"]![0]!;
        team.Remove("targetId");
        team.Remove("enemyPosition");
        team["enemies"] = new JsonArray(new JsonObject { ["targetId"] = 7, ["enemyPosition"] = new JsonArray(100, 120) });
        File.WriteAllText(source.Metadata, metadata.ToJsonString());
        string[] before = source.Snapshot();
        var result = PrepareStoryPackage.Prepare(source.Input, source.Output);
        var visual = NativeBattleVisualProfile.Load(result.VisualUri, NativeStoryProfile.Load(result.StoryUri));
        AssertThat(visual.Teams[18].EnemySlots.Count).IsEqual(1);
        AssertThat(visual.Teams[18].TargetId).IsEqual(7);
        AssertThat(visual.Teams[18].EnemyPosition).IsEqual(new Vector2I(100, 120));
        AssertThat(source.Snapshot()).IsEqual(before);
    }

    [TestCase("unknown-item-field")]
    [TestCase("mix-legacy")]
    [TestCase("legacy-missing-position")]
    [TestCase("zero-slots")]
    [TestCase("three-slots")]
    [TestCase("slot-position")]
    public void AuthorEnemiesSlotsAreStrictAndNeverMixWithTheLegacyPair(string fault)
    {
        using var source = new Preparation();
        var metadata = JsonNode.Parse(File.ReadAllText(source.Metadata))!;
        var team = (JsonObject)metadata["visual"]!["teams"]![0]!;
        switch (fault)
        {
            case "unknown-item-field":
                team.Remove("targetId"); team.Remove("enemyPosition");
                team["enemies"] = new JsonArray(new JsonObject { ["targetId"] = 7,
                    ["enemyPosition"] = new JsonArray(100, 120), ["unknown"] = 0 });
                break;
            case "mix-legacy":
                team["enemies"] = new JsonArray(new JsonObject { ["targetId"] = 7, ["enemyPosition"] = new JsonArray(100, 120) });
                break;
            case "legacy-missing-position": team.Remove("enemyPosition"); break;
            case "zero-slots":
                team.Remove("targetId"); team.Remove("enemyPosition"); team["enemies"] = new JsonArray(); break;
            case "three-slots":
                team.Remove("targetId"); team.Remove("enemyPosition");
                team["enemies"] = new JsonArray(
                    new JsonObject { ["targetId"] = 7, ["enemyPosition"] = new JsonArray(100, 120) },
                    new JsonObject { ["targetId"] = 7, ["enemyPosition"] = new JsonArray(128, 104) },
                    new JsonObject { ["targetId"] = 7, ["enemyPosition"] = new JsonArray(1, 1) });
                break;
            default:
                team.Remove("targetId"); team.Remove("enemyPosition");
                team["enemies"] = new JsonArray(new JsonObject { ["targetId"] = 7, ["enemyPosition"] = new JsonArray(32768, 120) });
                break;
        }
        File.WriteAllText(source.Metadata, metadata.ToJsonString());
        string[] before = source.Snapshot();
        Reject(source.Input, source.Output, fault switch
        {
            "unknown-item-field" => "unknown fields",
            "mix-legacy" or "legacy-missing-position" => "never both or neither",
            "zero-slots" or "three-slots" => "one or two enemy slots",
            _ => "SHORT",
        });
        AssertThat(source.Snapshot()).IsEqual(before);
        AssertThat(Directory.Exists(source.Output)).IsFalse();
    }

    [TestCase("version")]
    [TestCase("missing")]
    [TestCase("unknown")]
    [TestCase("duplicate")]
    [TestCase("nested-unknown")]
    [TestCase("position")]
    [TestCase("type")]
    [TestCase("utf8")]
    [TestCase("oversize")]
    [TestCase("extra-file")]
    [TestCase("empty-directory")]
    [TestCase("missing-event")]
    [TestCase("unselected-raw-corrupt")]
    public void InvalidModernInputPreservesSourceAndCallerEmptyDirectory(string defect)
    {
        using var source = new Preparation();
        string json = File.ReadAllText(source.Metadata);
        switch (defect)
        {
            case "version": json = json.Replace("\"formatVersion\":1", "\"formatVersion\":2"); break;
            case "missing": json = json.Replace("\"formatVersion\":1,", ""); break;
            case "unknown": json = json.Replace("\"formatVersion\":1", "\"formatVersion\":1,\"unknown\":0"); break;
            case "duplicate": json = json.Replace("\"formatVersion\":1", "\"formatVersion\":1,\"formatVersion\":1"); break;
            case "nested-unknown": json = json.Replace("\"roleId\":0", "\"roleId\":0,\"unknown\":0"); break;
            case "position": json = json.Replace("[240,170]", "[32768,170]"); break;
            case "type": json = json.Replace("\"ownerId\":1", "\"ownerId\":true"); break;
            case "extra-file": File.WriteAllText(Path.Combine(source.Input, "extra"), "keep"); break;
            case "empty-directory": Directory.CreateDirectory(Path.Combine(source.Input, "unexpected")); break;
            case "missing-event": File.Delete(Path.Combine(source.Input, "events", "outcomes.json")); break;
            case "unselected-raw-corrupt":
                string path = Path.Combine(source.Input, "events", "shared.json");
                File.WriteAllText(path, File.ReadAllText(path).Replace("65535,8", "65536,8")); break;
        }
        File.WriteAllText(source.Metadata, json);
        if (defect == "utf8") File.WriteAllBytes(source.Metadata, [0xc3, 0x28]);
        if (defect == "oversize") using (var file = File.OpenWrite(source.Metadata)) file.SetLength(16777217);
        string[] before = source.Snapshot(); Directory.CreateDirectory(source.Output);
        Reject(source.Input, source.Output, defect switch
        {
            "version" => "formatVersion", "missing" or "unknown" or "duplicate" or "nested-unknown" => "fields",
            "position" => "SHORT", "type" => "type", "utf8" => "UTF-8", "oversize" => "16777216",
            "extra-file" => "exactly", "empty-directory" => "events directory", "missing-event" => "missing complete event",
            _ => "operands"
        });
        AssertThat(source.Snapshot()).IsEqual(before);
        AssertThat(Directory.Exists(source.Output)).IsTrue();
        AssertThat(Directory.GetFileSystemEntries(source.Output).Length).IsEqual(0);
    }

    [TestCase("owner")]
    [TestCase("visual-target")]
    [TestCase("missing-language-glyph")]
    public void StrictNativeRejectionCleansOnlyOwnedPublishedFilesAndRetainsCallerDirectory(string defect)
    {
        using var source = new Preparation();
        string json = File.ReadAllText(source.Metadata);
        if (defect == "owner") json = json.Replace("\"ownerId\":1", "\"ownerId\":2");
        if (defect == "visual-target") json = json.Replace("\"targetId\":7", "\"targetId\":8");
        if (defect == "missing-language-glyph")
        {
            source.Native.Story.English.Messages[0].Entries[0].Text = "Z";
            source.Native.Story.Save(source.Native.Story.English, "Language.en.tres");
        }
        File.WriteAllText(source.Metadata, json);
        Directory.CreateDirectory(source.Output); string[] before = source.Snapshot();
        Reject(source.Input, source.Output, defect == "visual-target" ? "TargetId" : defect == "owner" ? "identity" : "glyph");
        AssertThat(Directory.Exists(source.Output)).IsTrue();
        AssertThat(Directory.GetFileSystemEntries(source.Output).Length).IsEqual(0);
        AssertThat(source.Snapshot()).IsEqual(before);
    }

    [TestCase("file")]
    [TestCase("directory")]
    [TestCase("published")]
    public void CallerOutputIsNeverOverwrittenOrRemoved(string kind)
    {
        using var source = new Preparation(); Directory.CreateDirectory(source.Output);
        string path = Path.Combine(source.Output, kind == "published" ? "Story.tres" : "sentinel");
        if (kind == "directory") Directory.CreateDirectory(path); else File.WriteAllText(path, "caller-owned");
        Reject(source.Input, source.Output, "new or empty");
        AssertThat(File.Exists(path) || Directory.Exists(path)).IsTrue();
        if (kind != "directory") AssertThat(File.ReadAllText(path)).IsEqual("caller-owned");
    }

    [TestCase("outside")]
    [TestCase("input-project")]
    [TestCase("overlap")]
    [TestCase("native-overlap")]
    public void PublicApiRejectsUnsafePhysicalBoundariesBeforePublication(string defect)
    {
        using var source = new Preparation();
        string input = source.Input, output = source.Output;
        if (defect == "outside") output = Path.Combine(source.Input, "output");
        if (defect == "input-project") input = ProjectSettings.GlobalizePath("res://Tests");
        if (defect == "overlap") input = Path.GetDirectoryName(output)!;
        if (defect == "native-overlap") output = ProjectSettings.GlobalizePath(source.Native.Story.Profile.StaticResources + "/new-output");
        Reject(input, output, defect == "outside" ? "Assets" : defect == "input-project" ? "outside src/Pal.Game" : "overlap");
        AssertThat(Directory.Exists(output)).IsFalse();
    }

    [TestCase]
    public void NormalizedWindowsOutputUriPreservesCaseAndSupportsMissingAncestors()
    {
        using var source = new Preparation();
        string output = Path.Combine(source.Output, "MixedCase", "Native");
        if (OperatingSystem.IsWindows()) output = output.Replace("Pal.Game", "pal.game");
        var result = PrepareStoryPackage.Prepare(source.Input + "/.", output + "/");
        AssertThat(result.StoryUri).IsEqual(source.OutputUri + "/MixedCase/Native/Story.tres");
        _ = NativeBattleVisualProfile.Load(result.VisualUri, NativeStoryProfile.Load(result.StoryUri));
        Reject(source.Input, output);
        using var exclusive = new FileStream(source.Metadata, FileMode.Open, System.IO.FileAccess.ReadWrite, FileShare.None);
    }

    [TestCase("file-count")]
    [TestCase("total-bytes")]
    public void FixedModernPackageBoundsRejectBeforeParsingOrPublication(string defect)
    {
        using var source = new Preparation();
        if (defect == "file-count")
            for (int i = 0; i < 1025; i++) File.WriteAllText(Path.Combine(source.Input, "events", "extra" + i + ".json"), "{}");
        else
            for (int i = 0; i < 5; i++) using (var file = File.Create(Path.Combine(source.Input, "events", "extra" + i + ".json"))) file.SetLength(16 * 1024 * 1024);
        Reject(source.Input, source.Output, defect == "file-count" ? "1024" : "67108864");
        AssertThat(Directory.Exists(source.Output)).IsFalse();
    }

    [TestCase("input")]
    [TestCase("events")]
    [TestCase("output")]
    [TestCase("native")]
    public void PublicApiRejectsDirectoryReparsePointsWithoutTouchingTargets(string kind)
    {
        using var source = new Preparation();
        string link = ProjectSettings.GlobalizePath(source.OutputUri + "-link"), target = source.Input;
        string input = source.Input, output = source.Output;
        if (kind == "input") { link = source.Input + "-link"; input = link; }
        if (kind == "events")
        {
            target = Path.Combine(source.Input, "events-original");
            Directory.Move(Path.Combine(source.Input, "events"), target);
            link = Path.Combine(source.Input, "events");
        }
        if (kind == "output") output = link;
        if (kind == "native")
        {
            target = ProjectSettings.GlobalizePath(source.Native.Root);
            string json = File.ReadAllText(source.Metadata).Replace(source.Native.Root + "/Player.tres", source.OutputUri + "-link/Player.tres");
            File.WriteAllText(source.Metadata, json);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        if (OperatingSystem.IsWindows())
        {
            var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in new[] { "/c", "mklink", "/J", Path.GetFullPath(link), Path.GetFullPath(target) }) start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            string diagnostic = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            if (process.ExitCode != 0) throw new InvalidOperationException("mklink exit " + process.ExitCode + ": " + diagnostic);
        }
        else Directory.CreateSymbolicLink(link, target);
        try { Reject(input, output, "reparse"); AssertThat(Directory.Exists(target)).IsTrue(); }
        finally
        {
            Directory.Delete(link);
            if (kind == "events") Directory.Move(target, Path.Combine(source.Input, "events"));
        }
        AssertThat(Directory.Exists(source.Output)).IsFalse();
    }

    [TestCase]
    public void PublicPrepareBindsNumberSpriteWithExactNativeClosure()
    {
        using var source = new Preparation();
        source.Native.AddNumberSprite();
        var metadata = JsonNode.Parse(File.ReadAllText(source.Metadata))!;
        metadata["visual"]!["numberSpriteUri"] = source.Native.Profile.NumberSpriteUri;
        File.WriteAllText(source.Metadata, metadata.ToJsonString());
        string[] before = source.Snapshot();
        var result = PrepareStoryPackage.Prepare(source.Input, source.Output);
        var visual = NativeBattleVisualProfile.Load(result.VisualUri, NativeStoryProfile.Load(result.StoryUri));
        visual.RequireNumberPresentation();
        AssertThat(visual.BlueNumberFrames[0].Index).IsEqual(29);
        AssertThat(visual.CyanNumberFrames[9].Index).IsEqual(65);
        AssertThat(visual.ResourceSha256[source.Native.Profile.NumberSpriteUri]).IsEqual(Godot.FileAccess.GetSha256(source.Native.Profile.NumberSpriteUri));
        AssertThat(source.Snapshot()).IsEqual(before);
    }

    [TestCase("blank")]
    [TestCase("missing")]
    [TestCase("duplicate")]
    public void NumberAuthorFieldCannotBypassSchemaOrNativeDependencyChecks(string fault)
    {
        using var source = new Preparation();
        var metadata = JsonNode.Parse(File.ReadAllText(source.Metadata))!;
        metadata["visual"]!["numberSpriteUri"] = fault == "blank" ? " " : source.Native.Root + "/MissingNumbers.tres";
        string json = metadata.ToJsonString();
        if (fault == "duplicate") json = json.Replace("\"numberSpriteUri\":", "\"numberSpriteUri\":\"duplicate\",\"numberSpriteUri\":");
        File.WriteAllText(source.Metadata, json);
        string[] before = source.Snapshot();
        Reject(source.Input, source.Output, fault == "duplicate" ? "fields" : fault == "blank" ? "numberSpriteUri" : "missing");
        AssertThat(source.Snapshot()).IsEqual(before);
        AssertThat(Directory.Exists(source.Output)).IsFalse();
    }

    [TestCase]
    public void PublicPrepareBindsOptionalReferenceDataAndExactEffectClosure()
    {
        using var source = new Preparation();
        var metadata = JsonNode.Parse(File.ReadAllText(source.Metadata))!;
        metadata["visual"]!["attackEffectSpriteUri"] = source.Native.Team.EnemySpriteUri;
        metadata["visual"]!["attackEffectIndex"] = 0;
        metadata["visual"]!["teams"]![0]!["idleAnimSpeed"] = 0;
        File.WriteAllText(source.Metadata, metadata.ToJsonString());
        string[] before = source.Snapshot();
        var result = PrepareStoryPackage.Prepare(source.Input, source.Output);
        var visual = NativeBattleVisualProfile.Load(result.VisualUri, NativeStoryProfile.Load(result.StoryUri));
        visual.RequireReferencePresentation();
        AssertThat(visual.AttackEffectFrames.Select(x => x.Index).SequenceEqual(new[] { 0, 1, 2 })).IsTrue();
        AssertThat(visual.ResourceSha256.ContainsKey(source.Native.Team.EnemySpriteUri)).IsTrue();
        AssertThat(visual.Teams[18].IdleAnimSpeed).IsEqual(0);
        AssertThat(source.Snapshot()).IsEqual(before);
    }

    [TestCase("partial")]
    [TestCase("negative-group")]
    [TestCase("idle-sentinel")]
    [TestCase("idle-overflow")]
    [TestCase("unknown")]
    [TestCase("duplicate")]
    public void OptionalPresentationAuthorFieldsAreNotDefaultsOrSchemaEscapes(string fault)
    {
        using var source = new Preparation();
        var metadata = JsonNode.Parse(File.ReadAllText(source.Metadata))!;
        var visual = metadata["visual"]!;
        visual["attackEffectSpriteUri"] = source.Native.Team.EnemySpriteUri;
        if (fault != "partial") visual["attackEffectIndex"] = fault == "negative-group" ? -1 : 0;
        visual["teams"]![0]!["idleAnimSpeed"] = fault == "idle-sentinel" ? -1 : fault == "idle-overflow" ? 65536 : 0;
        if (fault == "unknown") visual["unknown"] = 0;
        string json = metadata.ToJsonString();
        if (fault == "duplicate") json = json.Replace("\"attackEffectIndex\":0", "\"attackEffectIndex\":0,\"attackEffectIndex\":0");
        File.WriteAllText(source.Metadata, json); Directory.CreateDirectory(source.Output);
        string[] before = source.Snapshot();
        Reject(source.Input, source.Output, fault == "partial" ? "paired" : fault is "unknown" or "duplicate" ? "fields" : "WORD");
        AssertThat(Directory.GetFileSystemEntries(source.Output).Length).IsEqual(0);
        AssertThat(source.Snapshot()).IsEqual(before);
    }

    private static void Reject(string input, string output, string? diagnostic = null)
    {
        Exception? failure = null;
        try { _ = PrepareStoryPackage.Prepare(input, output); } catch (Exception error) { failure = error; }
        AssertThat(failure is not null).IsTrue();
        if (diagnostic is not null && !failure!.Message.Contains(diagnostic, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Expected diagnostic " + diagnostic + "; actual " + failure.GetType().Name + ": " + failure.Message, failure);
    }

    internal sealed class Preparation : IDisposable
    {
        public NativeBattleVisualProfileFixture Native { get; } = new();
        public string Input { get; } = Path.Combine(Path.GetTempPath(), "pal-story-package-" + Guid.NewGuid().ToString("N"));
        public string OutputUri { get; } = "res://Assets/PrepareTests/" + Guid.NewGuid().ToString("N");
        public string Output => ProjectSettings.GlobalizePath(OutputUri);
        public string Metadata => Path.Combine(Input, "story-package.json");
        public Preparation()
        {
            Directory.CreateDirectory(Path.Combine(Input, "events"));
            foreach (string name in new[] { "index.json", "shared.json", "outcomes.json" })
                File.Copy(ProjectSettings.GlobalizePath(Native.Root + "/" + name), Path.Combine(Input, "events", name));
            var s = Native.Story.Profile; var v = Native.Profile; var t = Native.Team;
            File.WriteAllText(Metadata, JsonSerializer.Serialize(new
            {
                formatVersion = 1, sourceIdentity = "synthetic preparation test; not desktop art",
                staticResources = s.StaticResources, staticProfile = s.StaticProfile, bundleSha256 = s.BundleSha256,
                entryFileId = s.EntryFileId, entryId = s.EntryId, ownerId = s.OwnerId, entryAddress = s.EntryAddress,
                battleContentUri = s.BattleContentUri, dialogueDirectory = s.DialogueDirectory,
                portraits = s.Portraits.Select(p => new { id = p.Key, uri = p.Value }).ToArray(),
                visual = new { roleId = v.RoleId, playerSpriteUri = v.PlayerSpriteUri, playerPosition = new[] { 240, 170 }, paletteUri = v.PaletteUri,
                    teams = new[] { new { teamId = t.TeamId, targetId = t.TargetId, backgroundUri = t.BackgroundUri, enemySpriteUri = t.EnemySpriteUri,
                        enemyPosition = new[] { 100, 120 }, idleFrames = 1, magicFrames = 1, attackFrames = 1, actWaitFrames = 0 } } }
            }));
        }
        public string[] Snapshot() => Directory.GetFiles(Input, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(p => Path.GetRelativePath(Input, p) + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p)))).ToArray();
        public void Dispose()
        {
            Native.Dispose();
            if (Directory.Exists(Input)) Directory.Delete(Input, true);
            if (Directory.Exists(Output)) Directory.Delete(Output, true);
        }
    }
}
