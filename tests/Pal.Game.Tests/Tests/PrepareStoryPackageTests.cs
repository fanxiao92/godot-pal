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
[TestCategory("Prepare")]
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
        AssertThat(visual.Teams[18].EnemySlots[0].TargetId).IsEqual(7);
        var native = ResourceLoader.Load<Pal.Game.Resources.PalBattleVisualProfile>(result.VisualUri,
            cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
        AssertThat(native.Teams[0].EnemySlots.Length).IsEqual(1);
        AssertThat(native.Teams[0].EnemySlots[0].TargetId).IsEqual(7);
        AssertThat(native.Teams[0].EnemySlots[0].EnemyPosition).IsEqual(new Vector2I(100, 120));
        AssertThat(native.Teams[0].TargetId).IsEqual(-1);
        AssertThat(native.Teams[0].EnemyPosition).IsEqual(new Vector2I(int.MinValue, int.MinValue));
        AssertThat(story.ResourceSha256.ContainsKey(source.OutputUri + "/Events/outcomes.json")).IsTrue();
        AssertThat(File.ReadAllBytes(Path.Combine(source.Output, "Events", "shared.json")))
            .IsEqual(File.ReadAllBytes(Path.Combine(source.Input, "events", "shared.json")));
        AssertThat(Directory.GetFileSystemEntries(source.Output).Select(Path.GetFileName).Order().ToArray())
            .IsEqual(new[] { "Events", "Story.tres", "Visual.tres" });
        AssertThat(source.Snapshot()).IsEqual(before);
    }

    [TestCase]
    public void ProjectedBattleContentBindsDeclaredReceiptIdentityThroughPrepareAndStrictLoad()
    {
        using var source = new Preparation(projection: true);
        string[] before = source.Snapshot();
        (string StoryUri, string VisualUri) result;
        try { result = PrepareStoryPackage.Prepare(source.Input, source.Output); }
        catch (Exception error)
        {
            AssertThat(false).OverrideFailureMessage(error.Message).IsTrue();
            return;
        }
        var story = NativeStoryProfile.Load(result.StoryUri, "en");
        var visual = NativeBattleVisualProfile.Load(result.VisualUri, story);
        var nativeStory = ResourceLoader.Load<Pal.Game.Resources.PalStoryProfile>(result.StoryUri, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
        AssertThat(story.BattleContent.Teams[18].Id).IsEqual(1);
        AssertThat(visual.Teams[18].EnemySlots[0].TargetId).IsEqual(1);
        AssertThat(nativeStory.BattleProjectionReceiptSha256).IsEqual(source.Projection!.ReceiptSha256);
        AssertThat(nativeStory.BattleProjectionReceiptByteLength).IsEqual(source.Projection.ReceiptByteLength);
        AssertThat(source.Snapshot()).IsEqual(before);
    }

    [TestCase("missing-sha256")]
    [TestCase("missing-byteLength")]
    [TestCase("empty-sha256")]
    [TestCase("bad-hash")]
    [TestCase("nonpositive-length")]
    [TestCase("unknown-field")]
    public void ProjectedBattleContentDeclarationIsStrictAndFailClosed(string defect)
    {
        using var source = new Preparation(projection: true);
        var metadata = JsonNode.Parse(File.ReadAllText(source.Metadata))!.AsObject();
        var projection = metadata["battleProjection"]!.AsObject();
        switch (defect)
        {
            case "missing-sha256": projection.Remove("receiptSha256"); break;
            case "missing-byteLength": projection.Remove("receiptByteLength"); break;
            case "empty-sha256": projection["receiptSha256"] = ""; break;
            case "bad-hash": projection["receiptSha256"] = "A" + new string('a', 63); break;
            case "nonpositive-length": projection["receiptByteLength"] = 0; break;
            default: projection["receiptPath"] = "ignored.json"; break;
        }
        File.WriteAllText(source.Metadata, metadata.ToJsonString());
        string[] before = source.Snapshot();
        string expected = defect switch
        {
            "missing-sha256" => "missing: receiptSha256",
            "missing-byteLength" => "missing: receiptByteLength",
            "empty-sha256" => "receiptSha256 requires a nonempty string",
            "bad-hash" => "battleProjection.receiptSha256 must be 64 lowercase hexadecimal characters",
            "nonpositive-length" => "battleProjection.receiptByteLength must be a positive Int64",
            _ => "unknown: receiptPath"
        };
        Reject(source.Input, source.Output, expected);
        AssertThat(source.Snapshot()).IsEqual(before);
        AssertThat(Directory.Exists(source.Output)).IsFalse();
    }

    [TestCase("dangling")]
    [TestCase("missing")]
    public void ProjectedBattleContentExpectationAndNativeSourceMustBePaired(string defect)
    {
        using var source = new Preparation(projection: defect == "missing");
        var metadata = JsonNode.Parse(File.ReadAllText(source.Metadata))!.AsObject();
        if (defect == "dangling")
            metadata["battleProjection"] = new JsonObject
            {
                ["receiptSha256"] = new string('a', 64),
                ["receiptByteLength"] = 1
            };
        else metadata.Remove("battleProjection");
        File.WriteAllText(source.Metadata, metadata.ToJsonString());
        string[] before = source.Snapshot();
        Reject(source.Input, source.Output, defect == "dangling"
            ? "story battleProjection receipt expectation requires projected native battle content"
            : "projected native battle content requires a story battleProjection receipt expectation");
        AssertThat(source.Snapshot()).IsEqual(before);
        AssertThat(Directory.Exists(source.Output)).IsFalse();
    }

    [TestCase]
    public void AuthoredFiveSlotTeamsPrepareLoadAndKeepPerSlotIdentity()
    {
        // #210: the author schema and native chain accept five loaded enemy slots; repeated
        // enemy definition 7 stays five independent slots with distinct authored positions.
        using var source = new Preparation(five: true);
        string[] before = source.Snapshot();
        var result = PrepareStoryPackage.Prepare(source.Input, source.Output);
        var story = NativeStoryProfile.Load(result.StoryUri, "en");
        var visual = NativeBattleVisualProfile.Load(result.VisualUri, story);
        AssertThat(story.BattleContent.EnemyTeams[18].Select(enemy => enemy.Id).ToArray()).IsEqual(new[] { 7, 7, 7, 7, 7 });
        AssertThat(story.BattleContent.EnemyTeams[18].Select(enemy => enemy.Hp).ToArray()).IsEqual(new[] { 43, 44, 45, 46, 47 });
        AssertThat(visual.Teams[18].EnemySlots.Select(slot => slot.TargetId).ToArray()).IsEqual(new[] { 7, 7, 7, 7, 7 });
        AssertThat(visual.Teams[18].EnemySlots.Select(slot => slot.EnemyPosition).ToArray()).IsEqual(new[] {
            new Vector2I(100, 120), new Vector2I(128, 104), new Vector2I(156, 88), new Vector2I(184, 72), new Vector2I(212, 56) });
        AssertThat(source.Snapshot()).IsEqual(before);
    }

    [TestCase]
    public void MixedEmptySlotsPrepareLoadAndKeepRuntimeSlotOrder()
    {
        // #141 (T13c): the author schema and formal Prepare carry mixed runtime slots: the numeric
        // empty slot (canonical id 0) keeps its overlay position/order with no sprite dependency.
        using var source = new Preparation(mixed: true);
        string[] before = source.Snapshot();
        var result = PrepareStoryPackage.Prepare(source.Input, source.Output);
        var story = NativeStoryProfile.Load(result.StoryUri, "en");
        var visual = NativeBattleVisualProfile.Load(result.VisualUri, story);
        AssertThat(story.BattleContent.EnemyTeams[18].Select(enemy => (enemy.Id, enemy.IsEmpty)).ToArray())
            .IsEqual(new[] { (0, true), (7, false), (7, false) });
        AssertThat(story.BattleContent.EnemyTeams[18].Select(enemy => enemy.Hp).ToArray()).IsEqual(new[] { 0, 43, 44 });
        var slots = visual.Teams[18].EnemySlots;
        AssertThat(slots.Select(slot => slot.TargetId).ToArray()).IsEqual(new[] { 0, 7, 7 });
        AssertThat(slots.Select(slot => slot.IsEmpty).ToArray()).IsEqual(new[] { true, false, false });
        AssertThat(slots[0].EnemyPosition).IsEqual(new Vector2I(72, 136));
        AssertThat(slots[0].EnemySprite).IsNull();
        AssertThat((slots[0].IdleFrames, slots[0].IdleAnimSpeed, slots[0].MagicFrames, slots[0].AttackFrames, slots[0].ActWaitFrames))
            .IsEqual((0, 0, 0, 0, 0));
        AssertThat(slots[1].EnemySprite!.SpriteId).IsEqual(97);
        AssertThat((slots[1].IdleFrames, slots[1].IdleAnimSpeed, slots[1].MagicFrames, slots[1].AttackFrames, slots[1].ActWaitFrames))
            .IsEqual((1, 0, 1, 1, 0));
        // The empty slot contributes no dependency: both loaded slots share the one authored sprite leaf.
        AssertThat(visual.ResourceSha256.Keys.Count(path => path.EndsWith("/Enemy.tres", StringComparison.Ordinal))).IsEqual(1);
        AssertThat(source.Snapshot()).IsEqual(before);
    }

    [TestCase("sprite-only")]
    [TestCase("frames-only")]
    public void EmptySlotMetadataRejectsPartialPresentationFields(string fault)
    {
        // #141: per-slot presentation is one all-or-none group; a half-declared empty entry is a
        // metadata fault before any output is published, never a silent dense look-alike.
        using var source = new Preparation(mixed: true);
        var metadata = JsonNode.Parse(File.ReadAllText(source.Metadata))!;
        var slot = (JsonObject)metadata["visual"]!["teams"]![0]!["enemies"]![0]!;
        if (fault == "sprite-only") slot["enemySpriteUri"] = source.OutputUri + "/Enemy.tres";
        else slot["idleFrames"] = 1;
        File.WriteAllText(source.Metadata, metadata.ToJsonString());
        string[] before = source.Snapshot();
        Reject(source.Input, source.Output, "enemies[0]: enemy presentation fields must be declared together");
        AssertThat(source.Snapshot()).IsEqual(before);
        AssertThat(Directory.Exists(source.Output)).IsFalse();
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
                AssertThat(visual.Players.Select(p => p.RoleId).SequenceEqual(new[] { 0, 1 })).IsTrue();
                AssertThat(visual.Players.Select(p => p.PlayerPosition)
                    .SequenceEqual(new[] { new Vector2I(200, 176), new Vector2I(256, 152) })).IsTrue();
                AssertThat(visual.Teams[18].EnemySlots.Select(s => s.TargetId).SequenceEqual(new[] { 7, 7 })).IsTrue();
                AssertThat(visual.Teams[18].EnemySlots.Select(s => s.EnemyPosition)
                    .SequenceEqual(new[] { new Vector2I(100, 120), new Vector2I(128, 104) })).IsTrue();
            }
            AssertThat(SnapshotDirectory(source)).IsEqual(before);
        }

        string partySource = Path.Combine(repository, "samples/authored-battle/PartyWin");
        string[] partyBefore = SnapshotDirectory(partySource);
        string[] ownedBefore = SnapshotAuthoredOwnedDirectories();
        string output;
        // Combine the old valid PartyWin positional options with the explicit-zero sixth option.
        // enemyHp and removeDualAttack retain their original, scenario-specific supported inputs.
        using (var fixture = new AuthoredStoryFixture("PartyWin", "number", null, false, 88, 0))
        {
            output = ProjectSettings.GlobalizePath(fixture.Root);
            foreach (string language in new[] { "zh-Hans", "en" })
            {
                var story = NativeStoryProfile.Load(fixture.StoryUri, language);
                var visual = NativeBattleVisualProfile.Load(fixture.VisualUri, story);
                AssertThat(story.Events.InstructionCount).IsEqual(11);
                var prefix = story.Events.GetCommandContent(1);
                AssertThat(prefix.GetProperty("op").GetString()!).IsEqual("changeHpMp");
                AssertThat(prefix.GetProperty("scope").GetInt32()).IsEqual(1);
                AssertThat(prefix.GetProperty("amount").GetInt32()).IsEqual(0);
                AssertThat(story.Events.GetSource(1).Opcode).IsEqual(29);
                AssertThat(story.Events.GetSource(1).Operands.ToArray()).IsEqual(new[] { 1, 0, 0 });
                AssertThat(story.BattleContent.EnemyTeams[18].Select(e => e.Id).ToArray()).IsEqual(new[] { 7, 7 });
                AssertThat(story.BattleContent.EnemyTeams[18].Select(e => e.Hp).ToArray()).IsEqual(new[] { 43, 43 });
                AssertThat(story.BattleContent.InitialRoles[0].Hp).IsEqual(88);
                AssertThat(story.BattleContent.InitialRoles[1].Hp).IsEqual(95);
                AssertThat(story.BattleContent.InitialRoles.OrderBy(p => p.Key).Select(p => p.Value.Mp).ToArray()).IsEqual(new[] { 20, 20 });
                AssertThat(story.BattleContent.Players.Select(p => p.Hp).ToArray()).IsEqual(new[] { 88, 95 });
                AssertThat(story.BattleContent.InitialRoles[1].Equipment[3]).IsEqual(0);
                AssertThat(story.BattleContent.Players[1].Equipment[3]).IsEqual(0);
                AssertThat(story.BattleContent.RoleEquipment.Count).IsEqual(0);
                visual.RequireReferencePresentation();
                AssertThat(visual.NumberSprite is null).IsTrue();
                AssertThat(visual.BlueNumberFrames.Count).IsEqual(0);
                Exception? failure = null;
                try { visual.RequireNumberPresentation(); } catch (Exception error) { failure = error; }
                AssertThat(failure?.GetType() == typeof(InvalidDataException)).IsTrue();
                AssertThat(failure!.Message.Contains("NumberSpriteUri", StringComparison.Ordinal)).IsTrue();
            }
        }
        AssertThat(Directory.Exists(output)).IsFalse();
        AssertThat(SnapshotAuthoredOwnedDirectories()).IsEqual(ownedBefore);
        AssertThat(SnapshotDirectory(partySource)).IsEqual(partyBefore);

        foreach (string scenario in new[] { "Win", "PartyEquip" })
        {
            string source = Path.Combine(repository, "samples/authored-battle", scenario);
            string[] before = SnapshotDirectory(source);
            if (scenario == "PartyEquip")
            {
                var authored = JsonNode.Parse(File.ReadAllText(Path.Combine(source, "Numbers/battle-data.json")))!;
                foreach (string section in new[] { "roles", "players" })
                    AssertThat(authored[section]![1]!["equipment"]!.AsArray().Select(n => n!.GetValue<int>()).ToArray())
                        .IsEqual(new[] { 197, 0, 210, 170, 237, 248 });
                AssertThat(authored["roleEquipment"]![1]!.AsArray().Select(n => n!.GetValue<int>()).ToArray())
                    .IsEqual(new[] { 197, 0, 210, 170, 237, 248 });
            }
            using (var fixture = scenario == "Win"
                ? new AuthoredStoryFixture("Win", "number", 77, false, 88)
                : new AuthoredStoryFixture("PartyEquip", removeDualAttack: true))
            {
                output = ProjectSettings.GlobalizePath(fixture.Root);
                foreach (string language in new[] { "zh-Hans", "en" })
                {
                    var story = NativeStoryProfile.Load(fixture.StoryUri, language);
                    var visual = NativeBattleVisualProfile.Load(fixture.VisualUri, story);
                    foreach (string path in Directory.GetFiles(Path.Combine(source, "Story/events")))
                        AssertThat(File.ReadAllBytes(ProjectSettings.GlobalizePath(fixture.Root + "/Story/Events/" + Path.GetFileName(path))))
                            .IsEqual(File.ReadAllBytes(path));
                    AssertThat(story.Events.InstructionCount).IsEqual(10);
                    visual.RequireReferencePresentation();
                    if (scenario == "Win")
                    {
                        AssertThat(story.BattleContent.EnemyTeams[18].Count).IsEqual(1);
                        AssertThat(story.BattleContent.EnemyTeams[18][0].Id).IsEqual(7);
                        AssertThat(story.BattleContent.EnemyTeams[18][0].Hp).IsEqual(77);
                        AssertThat(story.BattleContent.InitialRoles[0].Hp).IsEqual(88);
                        AssertThat(story.BattleContent.Players[0].Hp).IsEqual(88);
                        AssertThat(story.BattleContent.InitialRoles[0].Mp).IsEqual(20);
                        AssertThat(visual.NumberSprite is null).IsTrue();
                        AssertThat(visual.BlueNumberFrames.Count).IsEqual(0);
                        Exception? failure = null;
                        try { visual.RequireNumberPresentation(); } catch (Exception error) { failure = error; }
                        AssertThat(failure?.GetType() == typeof(InvalidDataException)).IsTrue();
                        AssertThat(failure!.Message.Contains("NumberSpriteUri", StringComparison.Ordinal)).IsTrue();
                    }
                    else
                    {
                        AssertThat(story.BattleContent.InitialRoles[1].Equipment.ToArray()).IsEqual(new[] { 197, 0, 210, 0, 237, 248 });
                        AssertThat(story.BattleContent.Players[1].Equipment.ToArray()).IsEqual(new[] { 197, 0, 210, 0, 237, 248 });
                        AssertThat(story.BattleContent.RoleEquipment[1].ToArray()).IsEqual(new[] { 197, 0, 210, 0, 237, 248 });
                        AssertThat(story.BattleContent.InitialRoles[0].Equipment.ToArray()).IsEqual(new[] { 0, 0, 0, 0, 0, 0 });
                        AssertThat(story.BattleContent.Players[0].Equipment.ToArray()).IsEqual(new[] { 0, 0, 0, 0, 0, 0 });
                        AssertThat(story.BattleContent.RoleEquipment[0].ToArray()).IsEqual(new[] { 0, 0, 0, 0, 0, 0 });
                        AssertThat(story.BattleContent.Players.Select(p => p.Hp).ToArray()).IsEqual(new[] { 100, 25 });
                        visual.RequireNumberPresentation();
                    }
                }
            }
            AssertThat(Directory.Exists(output)).IsFalse();
            AssertThat(SnapshotAuthoredOwnedDirectories()).IsEqual(ownedBefore);
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

    [TestCase]
    public void AuthoredPartyHpMpPrefixPreservesShortEdgesIncludingExplicitZero()
    {
        string repository = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../.."));
        string source = Path.Combine(repository, "samples/authored-battle/PartyWin");
        string[] before = SnapshotDirectory(source);
        string[] ownedBefore = SnapshotAuthoredOwnedDirectories();
        // Explicit literal signed/WORD pairs: no cast or clamp can generate the expected operand.
        foreach (var (amount, sourceWord) in new[] { (-32768, 32768), (0, 0), (32767, 32767) })
        {
            string output;
            using (var fixture = new AuthoredStoryFixture("PartyWin", partyHpMpPrefixAmount: amount))
            {
                output = ProjectSettings.GlobalizePath(fixture.Root);
                foreach (string language in new[] { "zh-Hans", "en" })
                {
                    var story = NativeStoryProfile.Load(fixture.StoryUri, language);
                    if (story.Events.InstructionCount != 11)
                        throw new InvalidOperationException($"M6-155 prefix instruction count: expected 11, actual {story.Events.InstructionCount}.");
                    var visual = NativeBattleVisualProfile.Load(fixture.VisualUri, story);
                    visual.RequireReferencePresentation();
                    visual.RequireNumberPresentation();
                    // Independent complete literal stream reviewed in U12, not derived from the fixture conversion.
                    int[][] rows = [ [134, 0, 65535, 8], [29, 1, sourceWord, 0], [60, 0, 0, 0],
                        [65535, 1, 0, 0], [7, 18, 8, 0], [60, 0, 0, 0], [65535, 2, 0, 0],
                        [0, 0, 0, 0], [60, 0, 0, 0], [65535, 3, 0, 0], [0, 0, 0, 0] ];
                    string[] contents = [
                        """{"op":"raw"}""",
                        new JsonObject { ["op"] = "changeHpMp", ["scope"] = 1, ["amount"] = amount }.ToJsonString(),
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
                    string streamSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
                    AssertThat(story.Entry.SourceAddress).IsEqual(1);
                    AssertThat(story.Events.GetSource(story.Entry.SourceAddress).Position.Label).IsEqual("L0001");
                    AssertThat(story.Entry.OwnerId).IsEqual(1);
                    AssertThat(story.SourceIdentity.Contains($"Test-owned PartyWin changeHpMp prefix: scope=1, amount={amount}; original authored stream derived, not retail SSS.MKF.", StringComparison.Ordinal)).IsTrue();
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
            }
            AssertThat(Directory.Exists(output)).IsFalse();
            AssertThat(SnapshotAuthoredOwnedDirectories()).IsEqual(ownedBefore);
            AssertThat(SnapshotDirectory(source)).IsEqual(before);
        }
    }

    [TestCase]
    public void AuthoredPartyHpMpPrefixRejectsInvalidOptionsBeforeOwnedIo()
    {
        string repository = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../.."));
        string[] scenarios = ["PartyWin", "PartyLoss", "Win", "Loss"];
        string[][] before = scenarios.Select(s => SnapshotDirectory(Path.Combine(repository, "samples/authored-battle", s))).ToArray();
        string[] ownedBefore = SnapshotAuthoredOwnedDirectories();
        foreach (var (scenario, amount) in new[] {
            ("PartyWin", -32769), ("PartyWin", 32768), ("PartyWin", int.MinValue),
            ("PartyWin", int.MaxValue), ("PartyWin", 65533),
            ("PartyLoss", 0), ("Win", 0), ("Loss", 0) })
        {
            AuthoredStoryFixture? unexpected = null;
            Exception? failure = null;
            try { unexpected = new AuthoredStoryFixture(scenario, partyHpMpPrefixAmount: amount); }
            catch (Exception error) { failure = error; }
            finally { unexpected?.Dispose(); }
            Type expectedType = scenario == "PartyWin" ? typeof(ArgumentOutOfRangeException) : typeof(ArgumentException);
            AssertThat(failure?.GetType() == expectedType).IsTrue();
            AssertThat(((ArgumentException)failure!).ParamName!).IsEqual(scenario == "PartyWin" ? "partyHpMpPrefixAmount" : "scenario");
            // Before/after inventory proves no residual owned directories, not absence of transient I/O.
            // The constructor validates these options before its first directory/Prepare operation.
            AssertThat(SnapshotAuthoredOwnedDirectories()).IsEqual(ownedBefore);
            for (int i = 0; i < scenarios.Length; i++)
                AssertThat(SnapshotDirectory(Path.Combine(repository, "samples/authored-battle", scenarios[i]))).IsEqual(before[i]);
        }
    }

    private static string[] SnapshotAuthoredOwnedDirectories() => Directory.GetDirectories(Path.GetTempPath(), "pal-story-c-*")
        .Concat(Directory.GetDirectories(Path.GetTempPath(), "pal-story-numeric-*"))
        .Concat(Directory.GetDirectories(ProjectSettings.GlobalizePath("res://Assets/RuntimeTests"), "story-c-*"))
        .Order(StringComparer.Ordinal).ToArray();

    private static string[] SnapshotDirectory(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(p => Path.GetRelativePath(directory, p) + ":" +
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p)))).ToArray();

    [TestCase]
    public void PublicPreparePublishesExplicitEnemySlotsForASingleSlotTeam()
    {
        using var source = new Preparation();
        var metadata = JsonNode.Parse(File.ReadAllText(source.Metadata))!;
        var team = (JsonObject)metadata["visual"]!["teams"]![0]!;
        File.WriteAllText(source.Metadata, metadata.ToJsonString());
        string[] before = source.Snapshot();
        var result = PrepareStoryPackage.Prepare(source.Input, source.Output);
        var visual = NativeBattleVisualProfile.Load(result.VisualUri, NativeStoryProfile.Load(result.StoryUri));
        AssertThat(visual.Teams[18].EnemySlots.Count).IsEqual(1);
        AssertThat(visual.Teams[18].EnemySlots[0].TargetId).IsEqual(7);
        AssertThat(visual.Teams[18].EnemySlots[0].EnemyPosition).IsEqual(new Vector2I(100, 120));
        AssertThat(source.Snapshot()).IsEqual(before);

        PublicPreparePreservesHeterogeneousEnemySlots();

        // Active inputs all use ordinal addresses, including one-enemy teams. Literal
        // expectations preserve the pre-migration layout, not values derived by Prepare.
        string repository = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../.."));
        foreach (string scenario in new[] { "Win", "Loss", "LostReload", "Rewards", "Midbattle", "PartyEquip", "PartyLoss", "PartyWin" })
        {
            string input = Path.Combine(repository, "samples/authored-battle", scenario);
            string[] inputBefore = SnapshotDirectory(input);
            var author = JsonNode.Parse(File.ReadAllText(Path.Combine(input, "Story/story-package.json")))!;
            int[] teamIds = scenario == "Rewards" ? [18, 19] : [18];
            bool twoSlots = scenario is "Midbattle" or "PartyEquip" or "PartyLoss" or "PartyWin";
            int[] targets = twoSlots ? [7, 7] : [7];
            Vector2I[] positions = twoSlots ? [new(100, 120), new(128, 104)] : [new(100, 120)];
            var teams = author["visual"]!["teams"]!.AsArray();
            AssertThat(teams.Select(t => t!["teamId"]!.GetValue<int>()).ToArray()).IsEqual(teamIds);
            foreach (var authoredTeam in teams)
            {
                // This assertion is deliberately before Prepare: accepting the legacy
                // scalar schema would not prove migration of the active source itself.
                AssertThat(authoredTeam!.AsObject().ContainsKey("targetId") || authoredTeam.AsObject().ContainsKey("enemyPosition")).IsFalse();
                var enemies = authoredTeam["enemies"]!.AsArray();
                AssertThat(enemies.Select(e => e!["targetId"]!.GetValue<int>()).ToArray()).IsEqual(targets);
                AssertThat(enemies.Select(e => new Vector2I(e!["enemyPosition"]![0]!.GetValue<int>(),
                    e["enemyPosition"]![1]!.GetValue<int>())).ToArray()).IsEqual(positions);
                AssertThat(authoredTeam["backgroundUri"]!.GetValue<string>()).IsEqual("res://Samples/AuthoredBattle/Visual/Background.tres");
                string[] slotFields = ["enemySpriteUri", "idleFrames", "idleAnimSpeed", "magicFrames", "attackFrames", "actWaitFrames"];
                AssertThat(slotFields.Any(field => authoredTeam.AsObject().ContainsKey(field))).IsFalse();
                foreach (var enemy in enemies)
                {
                    AssertThat(enemy!["enemySpriteUri"]!.GetValue<string>()).IsEqual("res://Samples/AuthoredBattle/Visual/Enemy.tres");
                    AssertThat(slotFields.Skip(1).Select(field => enemy[field]!.GetValue<int>()).ToArray())
                        .IsEqual(new[] { 1, 2, 1, 1, 0 });
                }
            }
            string[] ownedBefore = SnapshotAuthoredOwnedDirectories();
            string output;
            using (var fixture = new AuthoredStoryFixture(scenario))
            {
                output = ProjectSettings.GlobalizePath(fixture.Root);
                foreach (string language in new[] { "zh-Hans", "en" })
                {
                    var checkedStory = NativeStoryProfile.Load(fixture.StoryUri, language);
                    var checkedVisual = NativeBattleVisualProfile.Load(fixture.VisualUri, checkedStory);
                    var native = ResourceLoader.Load<Pal.Game.Resources.PalBattleVisualProfile>(fixture.VisualUri,
                        cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
                    AssertThat(native.Teams.Select(t => t.TeamId).ToArray()).IsEqual(teamIds);
                    foreach (int teamId in teamIds)
                    {
                        AssertThat(checkedStory.BattleContent.EnemyTeams[teamId].Select(e => e.Id).ToArray()).IsEqual(targets);
                        var nativeTeam = native.Teams.Single(t => t.TeamId == teamId);
                        AssertThat(nativeTeam.TargetId).IsEqual(-1);
                        AssertThat(nativeTeam.EnemyPosition).IsEqual(new Vector2I(int.MinValue, int.MinValue));
                        AssertThat(nativeTeam.EnemySlots.Select(s => s.EnemySpriteUri).ToArray())
                            .IsEqual(Enumerable.Repeat("res://Samples/AuthoredBattle/Visual/Enemy.tres", targets.Length).ToArray());
                        var slots = checkedVisual.Teams[teamId].EnemySlots;
                        AssertThat(slots.Select(s => s.TargetId).ToArray()).IsEqual(targets);
                        AssertThat(slots.Select(s => s.EnemyPosition).ToArray()).IsEqual(positions);
                        foreach (var slot in slots)
                        {
                            AssertThat(slot.EnemySprite!.SpriteId).IsEqual(97); // Image identity, not enemy definition 7.
                            AssertThat((slot.IdleFrames, slot.IdleAnimSpeed, slot.MagicFrames, slot.AttackFrames, slot.ActWaitFrames))
                                .IsEqual((1, 2, 1, 1, 0));
                        }
                    }
                    foreach (string path in Directory.GetFiles(Path.Combine(input, "Story/events")))
                        AssertThat(File.ReadAllBytes(ProjectSettings.GlobalizePath(fixture.Root + "/Story/Events/" + Path.GetFileName(path))))
                            .IsEqual(File.ReadAllBytes(path));
                }
            }
            AssertThat(Directory.Exists(output)).IsFalse();
            AssertThat(SnapshotAuthoredOwnedDirectories()).IsEqual(ownedBefore);
            AssertThat(SnapshotDirectory(input)).IsEqual(inputBefore);
        }
    }

    [TestCase("unknown-item-field")]
    [TestCase("mix-legacy")]
    [TestCase("legacy-missing-position")]
    [TestCase("zero-slots")]
    [TestCase("six-slots")]
    [TestCase("slot-position")]
    public void AuthorEnemiesSlotsAreStrictAndNeverMixWithTheLegacyPair(string fault)
    {
        using var source = new Preparation();
        var metadata = JsonNode.Parse(File.ReadAllText(source.Metadata))!;
        var team = (JsonObject)metadata["visual"]!["teams"]![0]!;
        var slot = team["enemies"]![0]!.AsObject();
        switch (fault)
        {
            case "unknown-item-field": slot["unknown"] = 0; break;
            case "mix-legacy": team["targetId"] = 7; team["enemyPosition"] = new JsonArray(100, 120); break;
            case "legacy-missing-position": team.Remove("enemies"); team["targetId"] = 7; break;
            case "zero-slots": team["enemies"] = new JsonArray(); break;
            case "six-slots": team["enemies"] = new JsonArray(slot.DeepClone(), slot.DeepClone(), slot.DeepClone(), slot.DeepClone(), slot.DeepClone(), slot.DeepClone()); break;
            default: slot["enemyPosition"] = new JsonArray(32768, 120); break;
        }
        File.WriteAllText(source.Metadata, metadata.ToJsonString());
        string[] before = source.Snapshot();
        Reject(source.Input, source.Output, fault switch
        {
            "unknown-item-field" => "unknown fields",
            "mix-legacy" or "legacy-missing-position" => "fields",
            "zero-slots" or "six-slots" => "one to five enemy slots",
            _ => "SHORT",
        });
        AssertThat(source.Snapshot()).IsEqual(before);
        AssertThat(Directory.Exists(source.Output)).IsFalse();
        if (fault == "unknown-item-field") RejectIncompleteOrInvalidAuthorSlots();
        if (fault == "mix-legacy") RejectLegacyAuthorTeamsEvenWithCompleteSlots();
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

    private static void PublicPrepareRequiresExplicitIdleSpeedInsteadOfSynthesizingIt()
    {
        using var source = new Preparation();
        var metadata = JsonNode.Parse(File.ReadAllText(source.Metadata))!;
        metadata["visual"]!["teams"]![0]!["enemies"]![0]!.AsObject().Remove("idleAnimSpeed");
        File.WriteAllText(source.Metadata, metadata.ToJsonString());
        string[] before = source.Snapshot();
        Reject(source.Input, source.Output, "fields");
        AssertThat(source.Snapshot()).IsEqual(before);
        AssertThat(Directory.Exists(source.Output)).IsFalse();
    }

    [TestCase]
    public void PublicPrepareBindsOptionalReferenceDataAndExactEffectClosure()
    {
        PublicPrepareRequiresExplicitIdleSpeedInsteadOfSynthesizingIt();
        using var source = new Preparation();
        var metadata = JsonNode.Parse(File.ReadAllText(source.Metadata))!;
        metadata["visual"]!["players"]![0]!["attackEffectSpriteUri"] = source.Native.Team.EnemySlots[0].EnemySpriteUri;
        metadata["visual"]!["players"]![0]!["attackEffectIndex"] = 0;
        metadata["visual"]!["teams"]![0]!["enemies"]![0]!["idleAnimSpeed"] = 0;
        File.WriteAllText(source.Metadata, metadata.ToJsonString());
        string[] before = source.Snapshot();
        var result = PrepareStoryPackage.Prepare(source.Input, source.Output);
        var visual = NativeBattleVisualProfile.Load(result.VisualUri, NativeStoryProfile.Load(result.StoryUri));
        visual.RequireReferencePresentation();
        AssertThat(visual.Players[0].AttackEffectFrames.Select(x => x.Index).SequenceEqual(new[] { 0, 1, 2 })).IsTrue();
        AssertThat(visual.ResourceSha256.ContainsKey(source.Native.Team.EnemySlots[0].EnemySpriteUri)).IsTrue();
        var slot = visual.Teams[18].EnemySlots[0];
        AssertThat((slot.IdleFrames, slot.IdleAnimSpeed, slot.MagicFrames, slot.AttackFrames, slot.ActWaitFrames))
            .IsEqual((1, 0, 1, 1, 0));
        AssertThat(source.Snapshot()).IsEqual(before);
    }

    [TestCase]
    public void AuthorPlayerSchemaAcceptsThreeReferenceColumnsAndRejectsAFourth()
    {
        using var source = new Preparation(party: true);
        source.Native.Story.AddThirdPartyRole();
        var metadata = JsonNode.Parse(File.ReadAllText(source.Metadata))!;
        // The author columns follow the numeric party: three bindings at g_rgPlayerPos[2]
        // (battle.c:27-31), sharing the second member's authored sprite for role2.
        var players = metadata["visual"]!["players"]!.AsArray();
        players[0]!["playerPosition"] = new JsonArray(180, 180);
        players[1]!["playerPosition"] = new JsonArray(234, 170);
        var third = players[1]!.DeepClone();
        third["roleId"] = 2;
        third["playerPosition"] = new JsonArray(270, 146);
        players.Add(third);
        File.WriteAllText(source.Metadata, metadata.ToJsonString());
        string[] before = source.Snapshot();
        var result = PrepareStoryPackage.Prepare(source.Input, source.Output);
        var visual = NativeBattleVisualProfile.Load(result.VisualUri, NativeStoryProfile.Load(result.StoryUri));
        AssertThat(visual.Players.Select(player => player.RoleId).ToArray()).IsEqual(new[] { 0, 1, 2 });
        AssertThat(visual.Players.Select(player => player.PlayerPosition).ToArray())
            .IsEqual(new[] { new Vector2I(180, 180), new Vector2I(234, 170), new Vector2I(270, 146) });
        AssertThat(visual.PlayerOrigins(3).SequenceEqual(new[] { new Vector2I(180, 180), new Vector2I(234, 170), new Vector2I(270, 146) })).IsTrue();
        AssertThat(source.Snapshot()).IsEqual(before);
        players.Add(players[2]!.DeepClone());
        File.WriteAllText(source.Metadata, metadata.ToJsonString());
        Reject(source.Input, source.Output + "-four", "one to three per-role bindings");
        AssertThat(Directory.Exists(source.Output + "-four")).IsFalse();
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
        var player = visual["players"]![0]!;
        player["attackEffectSpriteUri"] = source.Native.Team.EnemySlots[0].EnemySpriteUri;
        if (fault != "partial") player["attackEffectIndex"] = fault == "negative-group" ? -1 : 0;
        visual["teams"]![0]!["enemies"]![0]!["idleAnimSpeed"] = fault == "idle-sentinel" ? -1 : fault == "idle-overflow" ? 65536 : 0;
        if (fault == "unknown") visual["unknown"] = 0;
        string json = metadata.ToJsonString();
        if (fault == "duplicate") json = json.Replace("\"attackEffectIndex\":0", "\"attackEffectIndex\":0,\"attackEffectIndex\":0");
        File.WriteAllText(source.Metadata, json); Directory.CreateDirectory(source.Output);
        string[] before = source.Snapshot();
        Reject(source.Input, source.Output, fault == "partial" ? "paired" : fault is "unknown" or "duplicate" ? "fields" : "WORD");
        AssertThat(Directory.GetFileSystemEntries(source.Output).Length).IsEqual(0);
        AssertThat(source.Snapshot()).IsEqual(before);
    }

    private static void RejectLegacyAuthorTeamsEvenWithCompleteSlots()
    {
        foreach (string field in new[] { "targetId", "enemyPosition", "enemySpriteUri", "idleFrames", "idleAnimSpeed", "magicFrames", "attackFrames", "actWaitFrames", "scalar-only" })
        {
            using var source = new Preparation();
            var metadata = JsonNode.Parse(File.ReadAllText(source.Metadata))!;
            var team = metadata["visual"]!["teams"]![0]!.AsObject();
            if (field == "scalar-only")
            {
                var slot = team["enemies"]![0]!.AsObject();
                foreach (var property in slot) team[property.Key] = property.Value!.DeepClone();
                team.Remove("enemies");
            }
            else team[field] = team["enemies"]![0]![field]!.DeepClone();
            File.WriteAllText(source.Metadata, metadata.ToJsonString());
            string[] before = source.Snapshot();
            Reject(source.Input, source.Output, "visual.teams[0]");
            AssertThat(Directory.Exists(source.Output)).IsFalse();
            AssertThat(source.Snapshot()).IsEqual(before);
        }
    }

    private static void RejectIncompleteOrInvalidAuthorSlots()
    {
        foreach (string position in new[] { "[false,104]", "[\"128\",104]", "[128,false]", "[128,\"104\"]" })
        {
            using var source = new Preparation(party: true);
            var metadata = JsonNode.Parse(File.ReadAllText(source.Metadata))!;
            metadata["visual"]!["teams"]![0]!["enemies"]![1]!["enemyPosition"] = JsonNode.Parse(position);
            File.WriteAllText(source.Metadata, metadata.ToJsonString());
            string[] before = source.Snapshot();
            Exception? failure = null;
            try { _ = PrepareStoryPackage.Prepare(source.Input, source.Output); } catch (Exception error) { failure = error; }
            if (failure is not InvalidDataException)
                throw new InvalidOperationException("Expected InvalidDataException for enemyPosition " + position
                    + "; actual " + (failure?.GetType().Name ?? "no exception"), failure);
            foreach (string diagnostic in new[] { source.Metadata, "visual.teams[0]", "enemies[1]", "enemyPosition", position.StartsWith("[128,", StringComparison.Ordinal) ? "SHORT y" : "SHORT x" })
                AssertThat(failure.Message.Contains(diagnostic, StringComparison.OrdinalIgnoreCase)).IsTrue();
            AssertThat(source.Snapshot()).IsEqual(before);
            AssertThat(Directory.Exists(source.Output)).IsFalse();
        }
        string[] fields = ["targetId", "enemyPosition", "enemySpriteUri", "idleFrames", "idleAnimSpeed", "magicFrames", "attackFrames", "actWaitFrames"];
        foreach (string field in fields)
        foreach (string fault in new[] { "missing", "duplicate", "wrong-type", "negative", "overflow" })
        {
            if ((fault is "negative" or "overflow") && (field is "targetId" or "enemyPosition" or "enemySpriteUri")) continue;
            using var source = new Preparation(party: true);
            var metadata = JsonNode.Parse(File.ReadAllText(source.Metadata))!;
            var slot = metadata["visual"]!["teams"]![0]!["enemies"]![1]!.AsObject();
            if (fault == "missing") slot.Remove(field);
            else if (fault == "wrong-type") slot[field] = false;
            else if (fault == "negative") slot[field] = -1;
            else if (fault == "overflow") slot[field] = 65536;
            string json = metadata.ToJsonString();
            if (fault == "duplicate")
            {
                // Inject only into slot1 so slot0 remains a complete valid declaration.
                int slot1 = json.LastIndexOf("\"targetId\":7", StringComparison.Ordinal);
                string tail = json[slot1..];
                string token = "\"" + field + "\":";
                tail = tail.Replace(token, token + slot[field]!.ToJsonString() + "," + token);
                json = json[..slot1] + tail;
            }
            File.WriteAllText(source.Metadata, json);
            string[] before = source.Snapshot();
            // #141: the six presentation fields are one all-or-none group, so a missing member
            // rejects by the group diagnostic (still located at the slot path); the identity fields
            // and every duplicate/type/range fault keep naming the exact field.
            bool missingPresentation = fault == "missing" && field is not ("targetId" or "enemyPosition");
            Reject(source.Input, source.Output, source.Metadata, "visual.teams[0]: enemies[1]",
                missingPresentation ? "enemy presentation fields must be declared together" : field);
            AssertThat(Directory.Exists(source.Output)).IsFalse();
            AssertThat(source.Snapshot()).IsEqual(before);
        }
    }

    private static void PublicPreparePreservesHeterogeneousEnemySlots()
    {
        using var source = new Preparation(party: true);
        // Independent literal art: enemy definition7 at both ordinals; actual sprite97/98.
        var other = new Pal.Game.Resources.PalSprite { SpriteId = 98, FrameCount = 4, DecodedByteLength = 200,
            Source = new Godot.Collections.Dictionary { ["archive"] = "test-owned heterogeneous art, not authenticated", ["chunk"] = 98 } };
        for (int frame = 0; frame < 4; frame++)
            other.Frames.Add(new Pal.Game.Resources.PalFrame { Index = frame, SourceOffset = 2 + frame * 4,
                Width = 3, Height = 2, Indices = [4, 19, 4, 19, 4, 19], Alpha = [255, 255, 255, 255, 255, 255] });
        source.Native.Story.Save(other, "Enemy98.tres");
        var metadata = JsonNode.Parse(File.ReadAllText(source.Metadata))!;
        var slot = metadata["visual"]!["teams"]![0]!["enemies"]![1]!;
        slot["enemySpriteUri"] = source.Native.Root + "/Enemy98.tres";
        slot["idleFrames"] = 2; slot["idleAnimSpeed"] = 65535;
        slot["magicFrames"] = 0; slot["attackFrames"] = 2; slot["actWaitFrames"] = 65535;
        File.WriteAllText(source.Metadata, metadata.ToJsonString());
        string[] before = source.Snapshot();
        var result = PrepareStoryPackage.Prepare(source.Input, source.Output);
        var visual = NativeBattleVisualProfile.Load(result.VisualUri, NativeStoryProfile.Load(result.StoryUri));
        var slots = visual.Teams[18].EnemySlots;
        AssertThat(slots.Select(s => s.TargetId).ToArray()).IsEqual(new[] { 7, 7 });
        AssertThat(slots.Select(s => s.EnemySprite!.SpriteId).ToArray()).IsEqual(new[] { 97, 98 });
        AssertThat(slots.Select(s => s.EnemySprite!.FrameCount).ToArray()).IsEqual(new[] { 3, 4 });
        AssertThat(slots.Select(s => s.EnemyPosition).ToArray()).IsEqual(new[] { new Vector2I(100, 120), new Vector2I(128, 104) });
        AssertThat((slots[0].IdleFrames, slots[0].IdleAnimSpeed, slots[0].MagicFrames, slots[0].AttackFrames, slots[0].ActWaitFrames))
            .IsEqual((1, 0, 1, 1, 0));
        AssertThat((slots[1].IdleFrames, slots[1].IdleAnimSpeed, slots[1].MagicFrames, slots[1].AttackFrames, slots[1].ActWaitFrames))
            .IsEqual((2, 65535, 0, 2, 65535));
        AssertThat((slots[0].EnemySprite!.Frames[0].Width, slots[0].EnemySprite!.Frames[0].Height)).IsEqual((2, 1));
        AssertThat((slots[1].EnemySprite!.Frames[0].Width, slots[1].EnemySprite!.Frames[0].Height)).IsEqual((3, 2));
        AssertThat(visual.ResourceSha256[source.Native.Root + "/Enemy98.tres"])
            .IsEqual(Godot.FileAccess.GetSha256(source.Native.Root + "/Enemy98.tres"));
        AssertThat(source.Snapshot()).IsEqual(before);
        // Correct hashes are freshly computed by Prepare; these are semantic failures, not stale hashes.
        foreach (string fault in new[] { "target", "unaddressable", "missing-uri", "idle-zero" })
        {
            var invalid = metadata.DeepClone();
            var bad = invalid["visual"]!["teams"]![0]!["enemies"]![1]!;
            if (fault == "target") bad["targetId"] = 8;
            if (fault == "unaddressable") bad["attackFrames"] = 3;
            if (fault == "missing-uri") bad["enemySpriteUri"] = source.Native.Root + "/Absent98.tres";
            if (fault == "idle-zero") bad["idleFrames"] = 0;
            File.WriteAllText(source.Metadata, invalid.ToJsonString());
            string invalidOutput = source.Output + "-" + fault;
            Reject(source.Input, invalidOutput, fault switch {
                "target" => "EnemySlots[1]", "unaddressable" => "EnemySlots[1]",
                "missing-uri" => "Absent98.tres", _ => "enemies[1]: idleFrames" });
            AssertThat(Directory.Exists(invalidOutput)).IsFalse();
        }
    }

    private static void Reject(string input, string output, params string[] diagnostics)
    {
        Exception? failure = null;
        try { _ = PrepareStoryPackage.Prepare(input, output); } catch (Exception error) { failure = error; }
        AssertThat(failure is not null).IsTrue();
        foreach (string diagnostic in diagnostics)
            if (!failure!.Message.Contains(diagnostic, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Expected diagnostic " + diagnostic + "; actual " + failure.GetType().Name + ": " + failure.Message, failure);
    }

    internal sealed class Preparation : IDisposable
    {
        public NativeBattleVisualProfileFixture Native { get; }
        public string Input { get; } = Path.Combine(Path.GetTempPath(), "pal-story-package-" + Guid.NewGuid().ToString("N"));
        public string OutputUri { get; } = "res://Assets/PrepareTests/" + Guid.NewGuid().ToString("N");
        public string Output => ProjectSettings.GlobalizePath(OutputUri);
        public string Metadata => Path.Combine(Input, "story-package.json");
        public BattleProjectionSourceInfo? Projection { get; }
        public Preparation(bool party = false, bool five = false, bool mixed = false, bool projection = false)
        {
            Native = new NativeBattleVisualProfileFixture(party, five, emptySlots: mixed);
            if (projection)
            {
                Projection = Native.Story.ProjectBattleContent();
                var storyEnemies = Native.Story.Numeric.Teams[0].Enemies;
                for (int slot = 0; slot < Native.Profile.Teams[0].EnemySlots.Length; slot++)
                    Native.Profile.Teams[0].EnemySlots[slot].TargetId = storyEnemies[slot].Id;
                Native.Save();
            }
            Directory.CreateDirectory(Path.Combine(Input, "events"));
            foreach (string name in new[] { "index.json", "shared.json", "outcomes.json" })
                File.Copy(ProjectSettings.GlobalizePath(Native.Root + "/" + name), Path.Combine(Input, "events", name));
            var s = Native.Story.Profile; var v = Native.Profile; var t = Native.Team;
            // #141: a numeric-empty slot's author entry declares only targetId/enemyPosition;
            // loaded slots keep the full six-field presentation group.
            var enemies = new JsonArray();
            foreach (var slot in t.EnemySlots)
            {
                var row = new JsonObject { ["targetId"] = slot.TargetId,
                    ["enemyPosition"] = new JsonArray(slot.EnemyPosition.X, slot.EnemyPosition.Y) };
                if (slot.TargetId != 0)
                {
                    row["enemySpriteUri"] = slot.EnemySpriteUri;
                    row["idleFrames"] = 1; row["idleAnimSpeed"] = 0; row["magicFrames"] = 1;
                    row["attackFrames"] = 1; row["actWaitFrames"] = 0;
                }
                enemies.Add(row);
            }
            var metadata = JsonSerializer.SerializeToNode(new
            {
                formatVersion = 1, sourceIdentity = "synthetic preparation test; not desktop art",
                staticResources = s.StaticResources, staticProfile = s.StaticProfile, bundleSha256 = s.BundleSha256,
                entryFileId = s.EntryFileId, entryId = s.EntryId, ownerId = s.OwnerId, entryAddress = s.EntryAddress,
                battleContentUri = s.BattleContentUri, dialogueDirectory = s.DialogueDirectory,
                portraits = s.Portraits.Select(p => new { id = p.Key, uri = p.Value }).ToArray(),
                visual = new { players = v.Players.Select(p => new { roleId = p.RoleId, playerSpriteUri = p.PlayerSpriteUri,
                        playerPosition = new[] { p.PlayerPosition.X, p.PlayerPosition.Y } }).ToArray(), paletteUri = v.PaletteUri,
                    teams = new[] { new { teamId = t.TeamId, backgroundUri = t.BackgroundUri,
                        enemies } } }
            })!;
            if (Projection is not null)
                metadata["battleProjection"] = new JsonObject
                {
                    ["receiptSha256"] = Projection.ReceiptSha256,
                    ["receiptByteLength"] = Projection.ReceiptByteLength
                };
            File.WriteAllText(Metadata, metadata.ToJsonString());
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
