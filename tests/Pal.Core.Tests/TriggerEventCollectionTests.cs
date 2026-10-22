using System.Text.Json.Nodes;
using Pal.Core;
using Xunit;

namespace Pal.Core.Tests;

public class TriggerEventCollectionTests
{
    [Theory]
    [InlineData("version")]
    [InlineData("profile")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("hash")]
    [InlineData("identity")]
    [InlineData("length")]
    [InlineData("missing-file")]
    [InlineData("extra-file")]
    [InlineData("file-id")]
    [InlineData("integer-type")]
    [InlineData("root")]
    public void Invalid_collection_envelopes_are_rejected_with_resource_identity(string fault)
    {
        var fixture = new EventFixture();
        string? index = null;
        switch (fault)
        {
            case "version": fixture.Index["formatVersion"] = 2; break;
            case "profile": fixture.Index["profile"] = "structured"; break;
            case "unknown": fixture.Index["typo"] = 1; break;
            case "duplicate": index = fixture.Index.ToJsonString().Replace("\"formatVersion\":1", "\"formatVersion\":1,\"formatVersion\":1"); break;
            case "hash": fixture.Index["source"]!["sha256"] = "ABC"; break;
            case "identity": fixture.Index["source"]!["chunk"] = 3; break;
            case "length": fixture.Index["source"]!["byteLength"] = 8; break;
            case "missing-file": fixture.Files.Clear(); break;
            case "extra-file": fixture.Files.Add("extra", fixture.Files["shared"].DeepClone().AsObject()); break;
            case "file-id": fixture.Files["shared"]["fileId"] = "other"; break;
            case "integer-type": fixture.Index["formatVersion"] = "1"; break;
            case "root": index = "[]"; break;
        }
        var error = Assert.Throws<InvalidDataException>(() => TriggerEventCollection.Parse(index ?? fixture.Index.ToJsonString(), fixture.Files.ToDictionary(p => p.Key, p => p.Value.ToJsonString())));
        Assert.Contains(".json", error.Message);
    }

    [Theory]
    [InlineData("missing-address")]
    [InlineData("duplicate-address")]
    [InlineData("duplicate-label")]
    [InlineData("order")]
    [InlineData("next")]
    [InlineData("last-next")]
    [InlineData("dangling")]
    [InlineData("source-word")]
    [InlineData("operands-count")]
    [InlineData("field")]
    [InlineData("missing-field")]
    [InlineData("mapping")]
    [InlineData("semantic-range")]
    [InlineData("entry-zero")]
    [InlineData("entry-target")]
    [InlineData("entry-slot")]
    [InlineData("entry-owner")]
    [InlineData("duplicate-entry")]
    [InlineData("raw-known")]
    [InlineData("null-command")]
    [InlineData("identifier-newline")]
    [InlineData("hash-newline")]
    [InlineData("null-target")]
    [InlineData("semantic-type")]
    [InlineData("source-type")]
    [InlineData("entry-type")]
    [InlineData("reference-path")]
    [InlineData("reference-fragments")]
    public void Invalid_commands_entries_and_complete_address_graph_are_rejected(string fault)
    {
        var fixture = new EventFixture();
        switch (fault)
        {
            case "missing-address": fixture.Commands.RemoveAt(0); break;
            case "duplicate-address": fixture.Commands[1]!["source"]!["index"] = 0; break;
            case "duplicate-label": fixture.Commands[1]!["label"] = "L0000"; break;
            case "order": var first = fixture.Commands[0]!.DeepClone(); fixture.Commands[0] = fixture.Commands[1]!.DeepClone(); fixture.Commands[1] = first; break;
            case "next": fixture.Commands[1]!["next"] = "L0003"; break;
            case "last-next": fixture.Commands[4]!["next"] = "L0000"; break;
            case "dangling": fixture.Entry["target"] = "missing#L0001"; break;
            case "source-word": fixture.Commands[0]!["source"]!["operands"]![0] = -1; break;
            case "operands-count": fixture.Commands[0]!["source"]!["operands"]!.AsArray().RemoveAt(0); break;
            case "field": fixture.Commands[2]!["text"] = "not executable"; break;
            case "missing-field": fixture.Commands[2]!.AsObject().Remove("messageId"); break;
            case "mapping": fixture.Commands[1]!["source"]!["opcode"] = 61; break;
            case "semantic-range": fixture.Commands[1]!["color"] = 256; break;
            case "entry-zero": fixture.Entry["sourceAddress"] = 0; fixture.Entry["target"] = "L0000"; break;
            case "entry-target": fixture.Entry["target"] = "L0002"; break;
            case "entry-slot": fixture.Entry["slot"] = "wAutoScript"; break;
            case "entry-owner": fixture.Entry["owner"]!["id"] = 65536; break;
            case "duplicate-entry": fixture.Files["shared"]["entries"]!.AsArray().Add(fixture.Entry.DeepClone()); break;
            case "raw-known": fixture.Commands[0]!["source"]!["opcode"] = 60; break;
            case "null-command": fixture.Commands[0] = null; break;
            case "identifier-newline": fixture.Commands[0]!["label"] = "L0000\n"; break;
            case "hash-newline": fixture.Index["source"]!["sha256"] = new string('a', 64) + "\n"; break;
            case "null-target": fixture.Entry["target"] = null; break;
            case "semantic-type": fixture.Commands[2]!["messageId"] = "61"; break;
            case "source-type": fixture.Commands[0]!["source"] = false; break;
            case "entry-type": fixture.Files["shared"]["entries"]![0] = false; break;
            case "reference-path": fixture.Commands[0]!["next"] = "../shared#L0001"; break;
            case "reference-fragments": fixture.Commands[0]!["next"] = "shared#L0001#x"; break;
        }
        var error = Assert.Throws<InvalidDataException>(() => fixture.Load());
        Assert.Contains(".json", error.Message);
    }

    [Theory]
    [InlineData("{\"op\":\"end\",\"mode\":\"advance\"}", 1)]
    [InlineData("{\"op\":\"end\",\"mode\":\"redirect\",\"to\":\"L0000\",\"limit\":65535}", 2)]
    [InlineData("{\"op\":\"goto\",\"to\":\"L0000\",\"limit\":0}", 3)]
    [InlineData("{\"op\":\"giveItem\",\"itemId\":65535,\"count\":-32768}", 31)]
    [InlineData("{\"op\":\"setDialog\",\"box\":\"center\",\"color\":0,\"rngWord\":1}", 59)]
    [InlineData("{\"op\":\"setDialog\",\"box\":\"lower\",\"portraitId\":0,\"color\":255,\"rngWord\":0}", 61)]
    [InlineData("{\"op\":\"setDialog\",\"box\":\"centerWindow\",\"color\":79}", 62)]
    public void Faithful_base_mappings_can_be_archived_without_claiming_execution_support(string semanticJson, int opcode)
    {
        var fixture = new EventFixture();
        var command = JsonNode.Parse(semanticJson)!.AsObject();
        command["label"] = "L0000";
        command["next"] = "L0001";
        command["source"] = new JsonObject { ["index"] = 0, ["opcode"] = opcode, ["operands"] = new JsonArray(7, 8, 9) };
        fixture.Commands[0] = command;
        Assert.Equal(5, fixture.Load().InstructionCount);
        if (command.ContainsKey("to"))
        {
            command["to"] = "missing#L0000";
            Assert.Throws<InvalidDataException>(() => fixture.Load());
        }
    }

    [Theory]
    [InlineData("player.wScriptOnFriendDeath", "trigger")]
    [InlineData("player.wScriptOnDying", "trigger")]
    [InlineData("item.wScriptOnUse", "trigger")]
    [InlineData("item.wScriptOnEquip", "trigger")]
    [InlineData("item.wScriptOnThrow", "trigger")]
    [InlineData("magic.wScriptOnUse", "trigger")]
    [InlineData("magic.wScriptOnSuccess", "trigger")]
    [InlineData("enemy.wScriptOnTurnStart", "trigger")]
    [InlineData("enemy.wScriptOnBattleEnd", "trigger")]
    [InlineData("enemy.wScriptOnReady", "trigger")]
    [InlineData("poison.wPlayerScript", "trigger")]
    [InlineData("poison.wEnemyScript", "trigger")]
    [InlineData("item.wScriptDesc", "auto")]
    [InlineData("magic.wScriptDesc", "auto")]
    public void Proven_object_union_entry_view_is_validated_by_slot_not_invented_from_owner(string slot, string kind)
    {
        var fixture = new EventFixture();
        fixture.Entry["owner"]!["type"] = "object";
        fixture.Entry["slot"] = slot;
        fixture.Entry["kind"] = kind;
        Assert.Equal(slot, fixture.Load().GetEntry("shared", "npc").Slot);
    }

    [Theory]
    [InlineData("{\"op\":\"end\",\"mode\":\"redirect\",\"to\":null,\"limit\":0}", 2)]
    [InlineData("{\"op\":\"goto\",\"to\":\"L0000\",\"limit\":65536}", 3)]
    [InlineData("{\"op\":\"giveItem\",\"itemId\":1,\"count\":32768}", 31)]
    [InlineData("{\"op\":\"giveItem\",\"itemId\":-1,\"count\":0}", 31)]
    [InlineData("{\"op\":\"setDialog\",\"box\":\"center\",\"color\":0,\"rngWord\":0,\"portraitId\":0}", 59)]
    [InlineData("{\"op\":\"setDialog\",\"box\":\"lower\",\"color\":0,\"portraitId\":0,\"rngWord\":65536}", 61)]
    [InlineData("{\"op\":\"setDialog\",\"box\":\"centerWindow\",\"color\":-1}", 62)]
    public void Archived_base_mappings_still_require_exact_fields_ranges_and_nonnull_control_targets(string semantics, int opcode)
    {
        var fixture = new EventFixture();
        var command = JsonNode.Parse(semantics)!;
        command["label"] = "L0000";
        command["next"] = "L0001";
        command["source"] = new JsonObject { ["index"] = 0, ["opcode"] = opcode, ["operands"] = new JsonArray(0, 0, 0) };
        fixture.Commands[0] = command;
        Assert.Contains("shared.json", Assert.Throws<InvalidDataException>(() => fixture.Load()).Message);
    }

    [Fact]
    public void Full_WORD_address_space_requires_explicit_wraparound_to_zero()
    {
        var fixture = new EventFixture();
        fixture.Index["source"]!["instructionCount"] = 65536;
        fixture.Index["source"]!["byteLength"] = 524288;
        fixture.Commands.Clear();
        for (int address = 0; address < 65536; address++)
            fixture.Commands.Add(new JsonObject { ["label"] = $"L{address:X4}", ["op"] = "raw", ["next"] = $"L{((address + 1) & 65535):X4}",
                ["source"] = new JsonObject { ["index"] = address, ["opcode"] = 134, ["operands"] = new JsonArray(0, 1, 65535) } });
        var collection = fixture.Load();
        Assert.Equal(65536, collection.InstructionCount);
        fixture.Entry["target"] = "LFFFF";
        fixture.Entry["sourceAddress"] = 65535;
        fixture.Commands[65535]!["op"] = "setDialog";
        fixture.Commands[65535]!["box"] = "upper";
        fixture.Commands[65535]!["portraitId"] = 0;
        fixture.Commands[65535]!["color"] = 0;
        fixture.Commands[65535]!["rngWord"] = 0;
        fixture.Commands[65535]!["source"]!["opcode"] = 60;
        var script = new TriggerScriptSession(fixture.Load(), TriggerScriptSessionTests.Language());
        script.Start("shared", "npc", 60);
        Assert.Equal(TriggerScriptPhase.Completed, script.Phase);
        Assert.Equal(0, script.CurrentPosition!.SourceAddress); // Trigger stops at PC zero; raw zero is not executed.
        Assert.Equal(65535, script.ReturnEntry);
        fixture.Commands[65535]!["next"] = null;
        Assert.Contains("FFFF", Assert.Throws<InvalidDataException>(() => fixture.Load()).Message);
    }

    [Fact]
    public void Cross_file_next_and_entry_references_are_resolved_not_physical_adjacency()
    {
        var fixture = new EventFixture();
        fixture.Index["files"] = new JsonArray("scene", "shared");
        var scene = fixture.Files["shared"].DeepClone().AsObject();
        scene["fileId"] = "scene";
        scene["commands"] = new JsonArray();
        scene["entries"]![0]!["target"] = "shared#L0001";
        fixture.Files.Add("scene", scene);
        fixture.Files["shared"]["entries"] = new JsonArray();
        Assert.Equal(1, fixture.Load().GetEntry("scene", "npc").SourceAddress);
    }

    [Fact]
    public void Duplicate_keys_inside_ignored_annotations_are_still_invalid_JSON_content()
    {
        var fixture = new EventFixture();
        var texts = fixture.Files.ToDictionary(p => p.Key, p => p.Value.ToJsonString());
        texts["shared"] = texts["shared"].Replace("\"fileId\":\"shared\"", "\"fileId\":\"shared\",\"_note\":{\"a\":1,\"a\":2}");
        var error = Assert.Throws<InvalidDataException>(() => TriggerEventCollection.Parse(fixture.Index.ToJsonString(), texts));
        Assert.Contains("shared.json", error.Message);
        Assert.Contains("duplicate JSON key", error.Message);
    }

    [Fact]
    public void Complete_collection_preserves_zero_and_unselected_raw_and_exposes_entry_identity()
    {
        var fixture = new EventFixture();
        TriggerEventCollection collection = fixture.Load();
        Assert.Equal(5, collection.InstructionCount);
        var zero = collection.GetSource(0);
        Assert.Equal(new TriggerPosition("shared", "L0000", 0), zero.Position);
        Assert.Equal(134, zero.Opcode);
        Assert.Equal(new[] { 0, 65535, 8 }, zero.Operands);
        Assert.Single(collection.Entries);
        var entry = collection.GetEntry("shared", "npc");
        Assert.Equal(60, entry.OwnerId);
        Assert.Equal("eventObject", entry.OwnerType);
        Assert.Equal(1, entry.SourceAddress);
        Assert.Equal("trigger", entry.Kind);
        Assert.Equal("wTriggerScript", entry.Slot);
    }
}

internal sealed class EventFixture
{
    public JsonObject Index { get; } = JsonNode.Parse("""
        {"formatVersion":1,"profile":"faithful","source":{"archive":"SSS.MKF","chunk":4,"byteOrder":"little","instructionCount":5,"byteLength":40,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},"files":["shared"]}
        """)!.AsObject();
    public Dictionary<string, JsonObject> Files { get; } = new()
    {
        ["shared"] = JsonNode.Parse("""
        {"formatVersion":1,"profile":"faithful","fileId":"shared","entries":[
          {"id":"npc","owner":{"type":"eventObject","id":60},"slot":"wTriggerScript","kind":"trigger","target":"L0001","sourceAddress":1}],"commands":[
          {"label":"L0000","op":"raw","next":"L0001","source":{"index":0,"opcode":134,"operands":[0,65535,8]}},
          {"label":"L0001","op":"setDialog","box":"upper","portraitId":59,"color":0,"rngWord":0,"next":"L0002","source":{"index":1,"opcode":60,"operands":[59,0,0]}},
          {"label":"L0002","op":"showDialog","messageId":61,"descriptionLineWord":13,"next":"L0003","source":{"index":2,"opcode":65535,"operands":[61,13,29]}},
          {"label":"L0003","op":"showDialog","messageId":62,"descriptionLineWord":0,"next":"L0004","source":{"index":3,"opcode":65535,"operands":[62,0,0]}},
          {"label":"L0004","op":"end","mode":"keep","next":null,"source":{"index":4,"opcode":0,"operands":[7,8,9]}}]}
        """)!.AsObject()
    };
    public JsonArray Commands => Files["shared"]["commands"]!.AsArray();
    public JsonObject Entry => Files["shared"]["entries"]![0]!.AsObject();
    public TriggerEventCollection Load() => TriggerEventCollection.Parse(Index.ToJsonString(), Files.ToDictionary(p => p.Key, p => p.Value.ToJsonString()));
}
