using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Pal.Core;
using Xunit;

namespace Pal.Core.Tests;

public sealed class FirstMandatoryBattleProfileTests
{
    [Fact]
    public void Load_exposes_the_frozen_typed_profile()
    {
        var fixture = BattleContextTestFixture.Valid();

        FirstMandatoryBattleProfile profile = FirstMandatoryBattleProfile.Load(fixture.IndexBytes, fixture.Files);

        Assert.Equal("first-mandatory-battle-pre-trigger-v1", profile.ProfileId);
        Assert.Equal(2, profile.Identity.SceneId);
        Assert.Equal(12, profile.Identity.MapId);
        Assert.Equal(28, profile.Identity.OwnerId);
        Assert.Equal(0x09c3, profile.Identity.TriggerAddress);
        Assert.False(profile.Provenance.OptionalGrinding);
        Assert.False(profile.Provenance.PlayerEquipmentChanges);
        Assert.Equal((6, 6, 5), (profile.Layout.EquipmentRoleCount, profile.Layout.EquipmentSlotCount, profile.Layout.LevelUpMagicRoleCount));
        Assert.Equal(new[] { 0 }, profile.PartyRoleIds);
        Assert.Equal(new[] { 196, 225, 208, 166, 235, 249 }, profile.Roles[0].Equipment);
        Assert.Equal(new[] { 296, 345 }, profile.Roles[0].Magics);
        Assert.Equal((5, 28, 240, 10, 240, 0), (profile.Roles[1].Level, profile.Roles[1].Hp, profile.Roles[1].MaxHp, profile.Roles[1].Mp, profile.Roles[1].MaxMp, profile.Roles[1].CoveredByRoleId));
        Assert.Equal(7, profile.Roles[0].HiddenExperience.Count);
        Assert.Equal(9, profile.Roles[0].Statuses.Count);
        Assert.Equal((1248, 256, "east", 0), (profile.World.Leader.X, profile.World.Leader.Y, profile.World.Leader.Direction, profile.World.Leader.Phase));
        Assert.Equal((28, 3, true), (profile.World.SearchProof.OwnerId, profile.World.SearchProof.SampleIndex, profile.World.SearchProof.CollisionPass));
        Assert.Equal(0x09c3, profile.World.EventObjects.Single(x => x.OwnerId == 28).TriggerEntry);
        Assert.Equal("@09C3", profile.Closure.Entry);
        Assert.Equal(9, profile.Closure.Entries.Count);
        Assert.All(profile.Closure.Entries, entry => Assert.Equal("contract-only", entry.Status));
        BattleClosureEntry battle = profile.Closure.Entries.Single(x => x.Id == "battle-team-18");
        Assert.Equal(0x09fe, battle.Identity.Numbers["commandAddress"]);
        Assert.Equal(new[] { 0x0012, 0xa073, 0x0000 }, battle.Identity.NumberLists["operands"]);
        Assert.Equal(new[] { 46, 47, 51, 53 }, battle.OwnerTickets);
        Assert.Equal(new[] { "events", "battle-data" }, battle.ResourceIds);
        Assert.Equal(6, profile.Resources.Count);
        Assert.Equal("battle/data.json", profile.Resources.Single(x => x.Id == "battle-data").Reference);
    }

    [Fact]
    public void Load_hashes_the_exact_context_bytes_and_requires_the_exact_file_set()
    {
        var fixture = BattleContextTestFixture.Valid();
        byte[] changed = [.. fixture.ContextBytes, (byte)' '];
        Assert.Throws<InvalidDataException>(() => FirstMandatoryBattleProfile.Load(fixture.IndexBytes,
            new Dictionary<string, byte[]> { ["battle-context.json"] = changed }));
        Assert.Throws<InvalidDataException>(() => FirstMandatoryBattleProfile.Load(fixture.IndexBytes, new Dictionary<string, byte[]>()));
        Assert.Throws<InvalidDataException>(() => FirstMandatoryBattleProfile.Load(fixture.IndexBytes,
            new Dictionary<string, byte[]> { ["battle-context.json"] = fixture.ContextBytes, ["extra.json"] = [] }));
        Assert.Throws<InvalidDataException>(() => FirstMandatoryBattleProfile.Load(fixture.IndexBytes,
            new Dictionary<string, byte[]> { ["../battle-context.json"] = fixture.ContextBytes }));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("type")]
    public void Load_rejects_non_strict_index_json(string mutation)
    {
        var fixture = BattleContextTestFixture.Valid();
        string index = Encoding.UTF8.GetString(fixture.IndexBytes);
        if (mutation == "duplicate") index = index.Replace("\"formatVersion\":1", "\"formatVersion\":1,\"formatVersion\":1", StringComparison.Ordinal);
        else
        {
            JsonObject node = JsonNode.Parse(index)!.AsObject();
            if (mutation == "unknown") node["unexpected"] = true;
            else node["formatVersion"] = "1";
            index = node.ToJsonString();
        }
        Assert.Throws<InvalidDataException>(() => FirstMandatoryBattleProfile.Load(Encoding.UTF8.GetBytes(index), fixture.Files));
    }

    [Theory]
    [InlineData("formatVersion", "2")]
    [InlineData("profile", "\"other\"")]
    [InlineData("contextFile", "\"other.json\"")]
    [InlineData("contextSha256", "\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"")]
    public void Load_rejects_index_version_profile_context_identity_and_hash_format(string field, string replacement)
    {
        var fixture = BattleContextTestFixture.Valid();
        string index = Encoding.UTF8.GetString(fixture.IndexBytes);
        string current = JsonNode.Parse(index)![field]!.ToJsonString();
        byte[] mutated = Encoding.UTF8.GetBytes(index.Replace($"\"{field}\":{current}", $"\"{field}\":{replacement}", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => FirstMandatoryBattleProfile.Load(mutated, fixture.Files));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("missing")]
    [InlineData("type")]
    [InlineData("range")]
    public void Load_rejects_duplicate_unknown_missing_type_and_range_errors(string mutation)
    {
        var fixture = BattleContextTestFixture.Valid();
        byte[] context;
        if (mutation == "duplicate") context = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(fixture.ContextBytes).Replace("\"formatVersion\":1", "\"formatVersion\":1,\"formatVersion\":1", StringComparison.Ordinal));
        else
        {
            JsonObject document = fixture.Document.DeepClone().AsObject();
            if (mutation == "unknown") document["unexpected"] = true;
            else if (mutation == "missing") document.Remove("closure");
            else if (mutation == "type") document["snapshot"]!["roles"]![0]!["hp"] = "150";
            else document["snapshot"]!["world"]!["eventObjects"]![0]!["vanishTime"] = 32768;
            context = Encoding.UTF8.GetBytes(document.ToJsonString());
        }
        Assert.Throws<InvalidDataException>(() => FirstMandatoryBattleProfile.LoadContext(context));
    }

    [Theory]
    [InlineData("role")]
    [InlineData("provenance")]
    [InlineData("owner28")]
    [InlineData("search")]
    [InlineData("closure")]
    [InlineData("resource")]
    [InlineData("unsafe-reference")]
    [InlineData("unsafe-rooted")]
    [InlineData("unsafe-backslash")]
    public void Load_rejects_changes_to_frozen_contract_groups(string mutation)
    {
        var fixture = BattleContextTestFixture.Valid();
        JsonObject document = fixture.Document.DeepClone().AsObject();
        if (mutation == "role") document["snapshot"]!["roles"]![1]!["hp"] = 29;
        else if (mutation == "provenance") document["provenance"]!["fieldSources"]![0]!["sourceAddress"] = 0x0904;
        else if (mutation == "owner28") document["snapshot"]!["world"]!["eventObjects"]![0]!["spriteId"] = 211;
        else if (mutation == "search") document["snapshot"]!["world"]!["searchProof"]!["sampleIndex"] = 13;
        else if (mutation == "closure") document["closure"]!["entries"]!.AsArray().RemoveAt(0);
        else if (mutation == "resource") document["resources"]![0]!["kind"] = "other";
        else if (mutation == "unsafe-rooted") document["resources"]![0]!["reference"] = "/events/index.json";
        else if (mutation == "unsafe-backslash") document["resources"]![0]!["reference"] = "events\\index.json";
        else document["resources"]![0]!["reference"] = "../events/index.json";
        Assert.Throws<InvalidDataException>(() => FirstMandatoryBattleProfile.LoadContext(Encoding.UTF8.GetBytes(document.ToJsonString())));
    }

    [Fact]
    public void Load_rejects_invalid_utf8_in_index_and_context()
    {
        var fixture = BattleContextTestFixture.Valid();
        Assert.Throws<InvalidDataException>(() => FirstMandatoryBattleProfile.Load([0xff], fixture.Files));
        Assert.Throws<InvalidDataException>(() => FirstMandatoryBattleProfile.LoadContext([0xff]));
    }

    [Fact]
    public void Load_allows_underscore_comments_but_rejects_other_unknown_fields()
    {
        var fixture = BattleContextTestFixture.Valid();
        JsonObject document = fixture.Document.DeepClone().AsObject();
        document["_note"] = "root comment";
        document["snapshot"]!["world"]!["leader"]!["_note"] = "nested comment";
        document["closure"]!["entries"]![0]!["identity"]!["_note"] = "structured identity comment";
        FirstMandatoryBattleProfile.LoadContext(Encoding.UTF8.GetBytes(document.ToJsonString()));
        document["snapshot"]!["world"]!["leader"]!["note"] = "not a comment";
        Assert.Throws<InvalidDataException>(() => FirstMandatoryBattleProfile.LoadContext(Encoding.UTF8.GetBytes(document.ToJsonString())));
    }

    [Fact]
    public void CreateSessionState_deep_copies_all_nested_mutable_state_between_sessions_and_profile()
    {
        var fixture = BattleContextTestFixture.Valid();
        FirstMandatoryBattleProfile profile = FirstMandatoryBattleProfile.Load(fixture.IndexBytes, fixture.Files);

        FirstMandatoryBattleSessionState first = profile.CreateSessionState();
        FirstMandatoryBattleSessionState second = profile.CreateSessionState();
        first.PartyRoleIds[0] = 5;
        first.Roles[0].Equipment[0] = 999;
        first.Roles[0].Magics[0] = 999;
        first.Roles[0].Statuses[0] = 999;
        first.Roles[0].PrimaryExperience.Value = 999;
        first.Roles[0].HiddenExperience["health"].Count = 999;
        first.World.Leader.X = 999;
        first.World.SceneEntries[0].EnterEntry = 999;
        first.World.EventObjects[0].State = -1;

        Assert.Equal(0, second.PartyRoleIds[0]);
        Assert.Equal(196, second.Roles[0].Equipment[0]);
        Assert.Equal(296, second.Roles[0].Magics[0]);
        Assert.Equal(0, second.Roles[0].Statuses[0]);
        Assert.Equal(0, second.Roles[0].PrimaryExperience.Value);
        Assert.Equal(0, second.Roles[0].HiddenExperience["health"].Count);
        Assert.Equal(1248, second.World.Leader.X);
        Assert.Equal(0x0e97, second.World.SceneEntries[0].EnterEntry);
        Assert.Equal(2, second.World.EventObjects[0].State);
        Assert.Equal(196, profile.Roles[0].Equipment[0]);
        Assert.NotSame(first.Roles, second.Roles);
        Assert.NotSame(first.Roles[0].HiddenExperience, second.Roles[0].HiddenExperience);
        Assert.NotSame(first.World.EventObjects, second.World.EventObjects);
    }
}
