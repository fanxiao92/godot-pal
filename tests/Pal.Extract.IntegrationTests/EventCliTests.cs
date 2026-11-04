using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Pal.Extract.Tests;

public sealed class EventCliTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"pal-events-{Guid.NewGuid():N}");
    private readonly string input;
    private readonly string exported;
    private readonly string rebuilt;

    public EventCliTests()
    {
        input = Path.Combine(root, "input");
        exported = Path.Combine(root, "events");
        rebuilt = Path.Combine(root, "rebuilt.bin");
        Directory.CreateDirectory(input);
        WriteArchive("SSS.MKF", Words(0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            Words(12, 1, 0, 0, 0, 0, 0, 1),
            Words(0, 0, 0, 0, 0, 0, 0, 0),
            [],
            Words(0x003c, 59, 79, 1, 0xffff, 61, 17, 7, 0, 0, 0, 0, 0x0086, 0, 65535, 7));
    }

    [Fact]
    public async Task StartBattle_export_roundtrip_and_semantic_edits_use_resolved_targets_not_source_words()
    {
        byte[] original = Words(7, 65535, 0, 0, 7, 18, 4, 3, 0x0086, 65535, 7, 9, 0, 5, 6, 7, 0, 8, 9, 10);
        WriteArchive("SSS.MKF", new byte[32], Words(0, 1, 0, 0), [], [], original);
        var export = await CliProcess.Run(root, "export-events", "--input", input, "--output", exported);
        Assert.True(export.ExitCode == 0, export.Stderr);
        string path = Path.Combine(exported, "shared.json");
        var events = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var commands = events["commands"]!.AsArray();
        Assert.Equal(5, commands.Count);
        Assert.Equal("startBattle", commands[0]!["op"]!.GetValue<string>());
        Assert.Equal(65535, commands[0]!["enemyTeamId"]!.GetValue<int>());
        Assert.True(commands[0]!["isBoss"]!.GetValue<bool>());
        Assert.Null(commands[0]!["lossTarget"]);
        Assert.Null(commands[0]!["fleeTarget"]);
        Assert.Equal("L0004", commands[1]!["lossTarget"]!.GetValue<string>());
        Assert.Equal("L0003", commands[1]!["fleeTarget"]!.GetValue<string>());
        Assert.False(commands[1]!["isBoss"]!.GetValue<bool>());
        Assert.Equal("L0002", commands[1]!["next"]!.GetValue<string>());
        Assert.Equal(new[] { 18, 4, 3 }, commands[1]!["source"]!["operands"]!.AsArray().Select(n => n!.GetValue<int>()));
        var rebuild = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", rebuilt);
        Assert.True(rebuild.ExitCode == 0, rebuild.Stderr);
        Assert.Equal(original, File.ReadAllBytes(rebuilt));

        // Split the target into another file, preserving the complete address array and source.
        var last = commands[4]!.DeepClone();
        commands.RemoveAt(4);
        commands[3]!["next"] = "targets#L0004";
        commands[0]!["enemyTeamId"] = 19;
        commands[0]!["lossTarget"] = "targets#L0004";
        commands[0]!["isBoss"] = false;
        commands[0]!["fleeTarget"] = "L0001";
        commands[1]!["enemyTeamId"] = 20;
        commands[1]!["lossTarget"] = null;
        commands[1]!["isBoss"] = true;
        commands[1]!["fleeTarget"] = null;
        var targets = new System.Text.Json.Nodes.JsonObject { ["formatVersion"] = 1, ["profile"] = "faithful", ["fileId"] = "targets",
            ["entries"] = new System.Text.Json.Nodes.JsonArray(), ["commands"] = new System.Text.Json.Nodes.JsonArray(last) };
        var index = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(exported, "index.json")))!;
        index["files"] = new System.Text.Json.Nodes.JsonArray("shared", "targets");
        File.WriteAllText(Path.Combine(exported, "index.json"), index.ToJsonString());
        File.WriteAllText(Path.Combine(exported, "targets.json"), targets.ToJsonString());
        File.WriteAllText(path, events.ToJsonString());
        var edited = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", Path.Combine(root, "edited.bin"));
        Assert.True(edited.ExitCode == 0, edited.Stderr);
        Assert.Contains("differs from the recorded source SHA-256", edited.Stdout);
        Assert.Equal(Words(7, 19, 4, 1, 7, 20, 0, 0, 0x0086, 65535, 7, 9, 0, 5, 6, 7, 0, 8, 9, 10), File.ReadAllBytes(Path.Combine(root, "edited.bin")));
    }

    [Fact]
    public async Task Legacy_raw7_package_rebuilds_without_a_named_mapping_migration()
    {
        byte[] original = Words(0x0086, 0, 65535, 8, 7, 18, 65535, 0, 0, 7, 8, 9);
        WriteArchive("SSS.MKF", new byte[32], Words(0, 1, 0, 0), [], [], Words(0x0086, 0, 65535, 8, 0x0086, 18, 65535, 0, 0, 7, 8, 9));
        var export = await CliProcess.Run(root, "export-events", "--input", input, "--output", exported);
        Assert.True(export.ExitCode == 0, export.Stderr);
        string path = Path.Combine(exported, "shared.json");
        var events = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        events["commands"]![1]!["source"]!["opcode"] = 7;
        File.WriteAllText(path, events.ToJsonString());
        var rebuild = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", rebuilt);
        Assert.True(rebuild.ExitCode == 0, rebuild.Stderr);
        Assert.Equal(original, File.ReadAllBytes(rebuilt)); // Legacy raw operands are not interpreted as references.
    }

    [Theory]
    [InlineData("team-negative")]
    [InlineData("team-overflow")]
    [InlineData("team-type")]
    [InlineData("boss-type")]
    [InlineData("boss-flee")]
    [InlineData("nonboss-null")]
    [InlineData("loss-zero")]
    [InlineData("flee-zero")]
    [InlineData("loss-dangling")]
    [InlineData("flee-dangling")]
    [InlineData("loss-malformed")]
    [InlineData("flee-type")]
    [InlineData("unknown-field")]
    [InlineData("missing-field")]
    [InlineData("opcode")]
    [InlineData("ffff-outside")]
    [InlineData("duplicate-key")]
    public async Task StartBattle_at_true_chunk_end_rejects_invalid_fields_and_result_references_without_output(string fault)
    {
        WriteArchive("SSS.MKF", new byte[32], Words(0, 1, 0, 0), [], [], Words(0x0086, 0, 65535, 8, 7, 18, 0, 0));
        var export = await CliProcess.Run(root, "export-events", "--input", input, "--output", exported);
        Assert.True(export.ExitCode == 0, export.Stderr);
        string path = Path.Combine(exported, "shared.json");
        var events = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        var command = events["commands"]![1]!.AsObject();
        switch (fault)
        {
            case "team-negative": command["enemyTeamId"] = -1; break;
            case "team-overflow": command["enemyTeamId"] = 65536; break;
            case "team-type": command["enemyTeamId"] = "18"; break;
            case "boss-type": command["isBoss"] = 1; break;
            case "boss-flee": command["fleeTarget"] = "L0001"; break;
            case "nonboss-null": command["isBoss"] = false; break;
            case "loss-zero": command["lossTarget"] = "L0000"; break;
            case "flee-zero": command["isBoss"] = false; command["fleeTarget"] = "shared#L0000"; break;
            case "loss-dangling": command["lossTarget"] = "missing#L0001"; break;
            case "flee-dangling": command["isBoss"] = false; command["fleeTarget"] = "missing"; break;
            case "loss-malformed": command["lossTarget"] = "shared##L0001"; break;
            case "flee-type": command["fleeTarget"] = 1; break;
            case "unknown-field": command["limit"] = 0; break;
            case "missing-field": command.Remove("lossTarget"); break;
            case "opcode": command["source"]!["opcode"] = 8; break;
            case "ffff-outside": command["lossTarget"] = "LFFFF"; break;
        }
        string text = events.ToJsonString();
        if (fault == "duplicate-key") text = text.Replace("\"enemyTeamId\":18", "\"enemyTeamId\":18,\"enemyTeamId\":19");
        File.WriteAllText(path, text);
        var result = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", rebuilt);
        Assert.Equal(1, result.ExitCode);
        Assert.NotEmpty(result.Stderr);
        Assert.False(File.Exists(rebuilt));
    }

    [Fact]
    public async Task Role_hook_verbs_export_decode_semantic_edit_and_rebuild_roundtrip()
    {
        // Original authored stream in the #49 role-hook shape: random bands, dialog contexts and
        // per-WORD effects; not retail data. Four-WORD rows are little-endian.
        byte[] original = Words(
            6, 75, 2, 0,        // randomJump rate 75 -> @2
            61, 0, 5, 0,        // setDialog lower, no portrait, color 5
            0xffff, 101, 0, 0,  // showDialog 101
            27, 0, 9999, 0,     // changeHp caller +9999
            48, 17, 5, 0,       // boostStat attack +5% of the caller
            62, 15, 0, 0,       // setDialog centerWindow color 15
            0xffff, 102, 0, 0,
            28, 0, 9999, 0,     // changeMp caller +9999
            0, 0, 0, 0);        // end keep
        WriteArchive("SSS.MKF", new byte[32], Words(0, 1, 0, 0), [], [], original);
        var export = await CliProcess.Run(root, "export-events", "--input", input, "--output", exported);
        Assert.True(export.ExitCode == 0, export.Stderr);
        string path = Path.Combine(exported, "shared.json");
        var events = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        var commands = events["commands"]!.AsArray();
        Assert.Equal("randomJump", commands[0]!["op"]!.GetValue<string>());
        Assert.Equal(75, commands[0]!["rate"]!.GetValue<int>());
        Assert.Equal("L0002", commands[0]!["to"]!.GetValue<string>());
        Assert.Equal("setDialog", commands[1]!["op"]!.GetValue<string>());
        Assert.Equal("lower", commands[1]!["box"]!.GetValue<string>());
        Assert.Equal(5, commands[1]!["color"]!.GetValue<int>());
        Assert.Equal("changeHp", commands[3]!["op"]!.GetValue<string>());
        Assert.Equal(9999, commands[3]!["amount"]!.GetValue<int>());
        Assert.Equal("boostStat", commands[4]!["op"]!.GetValue<string>());
        Assert.Equal(17, commands[4]!["field"]!.GetValue<int>());
        Assert.Equal(5, commands[4]!["percent"]!.GetValue<int>());
        Assert.Equal(0, commands[4]!["roleOperand"]!.GetValue<int>());
        Assert.Equal("setDialog", commands[5]!["op"]!.GetValue<string>());
        Assert.Equal("centerWindow", commands[5]!["box"]!.GetValue<string>());
        Assert.Equal("changeMp", commands[7]!["op"]!.GetValue<string>());
        var rebuild = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", rebuilt);
        Assert.True(rebuild.ExitCode == 0, rebuild.Stderr);
        Assert.Equal(original, File.ReadAllBytes(rebuilt));

        // Semantic edits re-encode from fields, not source words: a new band rate, a larger heal,
        // a negative percent and a re-targeted jump all rewrite their WORDs.
        commands[0]!["rate"] = 50;
        commands[0]!["to"] = "L0006";
        commands[3]!["amount"] = -300;
        commands[4]!["percent"] = -25;
        commands[4]!["field"] = 20;
        commands[7]!["scope"] = 1;
        File.WriteAllText(path, events.ToJsonString());
        var edited = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", Path.Combine(root, "edited.bin"));
        Assert.True(edited.ExitCode == 0, edited.Stderr);
        Assert.Equal(Words(
            6, 50, 6, 0,
            61, 0, 5, 0,
            0xffff, 101, 0, 0,
            27, 0, 65236, 0,
            48, 20, 65511, 0,
            62, 15, 0, 0,
            0xffff, 102, 0, 0,
            28, 1, 9999, 0,
            0, 0, 0, 0), File.ReadAllBytes(Path.Combine(root, "edited.bin")));
    }

    [Theory]
    [InlineData("rate-overflow")]
    [InlineData("rate-type")]
    [InlineData("to-dangling")]
    [InlineData("amount-overflow")]
    [InlineData("percent-type")]
    [InlineData("field-missing")]
    [InlineData("raw-registered")]
    [InlineData("wrong-opcode")]
    [InlineData("unknown-field")]
    public async Task Role_hook_verbs_validate_strictly_on_rebuild(string fault)
    {
        byte[] original = Words(6, 75, 2, 0, 27, 0, 9999, 0);
        WriteArchive("SSS.MKF", new byte[32], Words(0, 1, 0, 0), [], [], original);
        var export = await CliProcess.Run(root, "export-events", "--input", input, "--output", exported);
        Assert.True(export.ExitCode == 0, export.Stderr);
        string path = Path.Combine(exported, "shared.json");
        var events = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        var command = events["commands"]![0]!.AsObject();
        switch (fault)
        {
            case "rate-overflow": command["rate"] = 65536; break;
            case "rate-type": command["rate"] = "75"; break;
            case "to-dangling": command["to"] = "L0009"; break;
            case "amount-overflow": events["commands"]![1]!["amount"] = 32768; break;
            case "percent-type": events["commands"]![1] = null; break;
            case "field-missing": events["commands"]![1]!.AsObject().Remove("amount"); break;
            case "raw-registered": events["commands"]![1] = new System.Text.Json.Nodes.JsonObject { ["label"] = "L0001", ["op"] = "raw", ["next"] = "L0002", ["source"] = new System.Text.Json.Nodes.JsonObject { ["index"] = 1, ["opcode"] = 27, ["operands"] = new System.Text.Json.Nodes.JsonArray(0, 9999, 0) } }; break;
            case "wrong-opcode": command["source"]!["opcode"] = 28; break;
            case "unknown-field": command["limit"] = 0; break;
        }
        File.WriteAllText(path, events.ToJsonString());
        var result = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", rebuilt);
        Assert.Equal(1, result.ExitCode);
        Assert.NotEmpty(result.Stderr);
        Assert.False(File.Exists(rebuilt));
    }

    [Fact]
    public async Task Control_flow_batch1_verbs_export_roundtrip_and_reencode_jump_targets_from_labels()
    {
        // M5 batch 1 (#96): end advance/redirect, goto, call, advanceEntry, confirmNoJump plus the
        // re-verified end keep / randomJump / waitUpdate family. Hand-built stream, not retail data.
        byte[] original = Words(
            0x0002, 2, 3, 7,   // end redirect: resume at L0002 unless the idle limit 3 trips
            0x0003, 5, 2, 0,   // goto L0005
            0x0004, 4, 9, 1,   // call L0004 with event object 9 as the script context
            0x0008, 0, 0, 0,   // advanceEntry
            0x000a, 3, 0, 0,   // confirmNoJump L0003 when the player selects No
            0x0009, 30, 1, 1,  // waitUpdate 30 frames with game updates and gestures
            0x0001, 0, 0, 0,   // end advance
            0x0000, 0, 0, 0);  // end keep
        WriteArchive("SSS.MKF", new byte[32], Words(0, 1, 0, 0), [], [], original);
        var export = await CliProcess.Run(root, "export-events", "--input", input, "--output", exported);
        Assert.True(export.ExitCode == 0, export.Stderr);
        string path = Path.Combine(exported, "shared.json");
        var events = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        var commands = events["commands"]!.AsArray();
        Assert.Equal(8, commands.Count);
        Assert.Equal("end", commands[0]!["op"]!.GetValue<string>());
        Assert.Equal("redirect", commands[0]!["mode"]!.GetValue<string>());
        Assert.Equal("L0002", commands[0]!["to"]!.GetValue<string>());
        Assert.Equal(3, commands[0]!["limit"]!.GetValue<int>());
        Assert.Equal("goto", commands[1]!["op"]!.GetValue<string>());
        Assert.Equal("L0005", commands[1]!["to"]!.GetValue<string>());
        Assert.Equal("call", commands[2]!["op"]!.GetValue<string>());
        Assert.Equal("L0004", commands[2]!["to"]!.GetValue<string>());
        Assert.Equal(9, commands[2]!["eventObjectId"]!.GetValue<int>());
        Assert.Equal("advanceEntry", commands[3]!["op"]!.GetValue<string>());
        Assert.Equal("confirmNoJump", commands[4]!["op"]!.GetValue<string>());
        Assert.Equal("L0003", commands[4]!["to"]!.GetValue<string>());
        Assert.Equal("waitUpdate", commands[5]!["op"]!.GetValue<string>());
        Assert.Equal(30, commands[5]!["frames"]!.GetValue<int>());
        Assert.Equal("end", commands[6]!["op"]!.GetValue<string>());
        Assert.Equal("advance", commands[6]!["mode"]!.GetValue<string>());
        Assert.Equal("end", commands[7]!["op"]!.GetValue<string>());
        Assert.Equal("keep", commands[7]!["mode"]!.GetValue<string>());
        var rebuild = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", rebuilt);
        Assert.True(rebuild.ExitCode == 0, rebuild.Stderr);
        Assert.Equal(original, File.ReadAllBytes(rebuilt)); // byte-exact round trip of the named batch

        // Semantic edits re-encode jump WORDs from resolved labels: goto to L0000 exercises the
        // zero-target rule (target 0 references the label at index 0 and re-encodes as WORD 0).
        commands[0]!["limit"] = 0; // always take the branch
        commands[1]!["to"] = "L0000";
        commands[2]!["eventObjectId"] = 0; // inherit the caller's event object id
        commands[4]!["to"] = "L0001";
        File.WriteAllText(path, events.ToJsonString());
        var edited = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", Path.Combine(root, "edited.bin"));
        Assert.True(edited.ExitCode == 0, edited.Stderr);
        Assert.Contains("differs from the recorded source SHA-256", edited.Stdout);
        Assert.Equal(Words(
            0x0002, 2, 0, 7,
            0x0003, 0, 2, 0,
            0x0004, 4, 0, 1,
            0x0008, 0, 0, 0,
            0x000a, 1, 0, 0,
            0x0009, 30, 1, 1,
            0x0001, 0, 0, 0,
            0x0000, 0, 0, 0), File.ReadAllBytes(Path.Combine(root, "edited.bin")));

        // A dangling jump target is rejected without producing output.
        commands[1]!["to"] = "L0100";
        File.WriteAllText(path, events.ToJsonString());
        var rejected = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", Path.Combine(root, "rejected.bin"));
        Assert.Equal(1, rejected.ExitCode);
        Assert.False(File.Exists(Path.Combine(root, "rejected.bin")));
    }

    [Fact]
    public async Task Conditional_jump_batch2_verbs_export_roundtrip_and_reencode_jump_targets_from_labels()
    {
        // M5 batch 2 (#97): conditional jumps part 1 plus the re-verified markFailed /
        // requestReload (stop-class successor) and waitForAnyKey. Hand-built stream, not retail data.
        byte[] original = Words(
            0x0041, 0, 0, 0,     // markFailed
            0x004D, 0, 0, 0,     // waitForAnyKey
            0x004E, 0, 0, 0,     // requestReload (stops the run)
            0x0058, 1, 2, 9,     // itemShortageJump: fewer than 2 of item 1 -> L0009
            0x005D, 560, 8, 0,   // playerLacksPoisonJump: poison object 560 -> L0008
            0x005E, 561, 7, 0,   // enemyLacksPoisonJump: poison object 561 -> L0007
            0x0061, 6, 0, 0,     // notPoisonedJump: no poison at all -> L0006
            0x0064, 30, 5, 0,    // enemyHpAboveJump: HP above 30% -> L0005
            0x0068, 4, 0, 0,     // enemyTurnJump: enemy's turn -> L0004
            0x0074, 0, 0, 0);    // partyHpNotFullJump: someone below max HP -> L0000
        WriteArchive("SSS.MKF", new byte[32], Words(0, 1, 0, 0), [], [], original);
        var export = await CliProcess.Run(root, "export-events", "--input", input, "--output", exported);
        Assert.True(export.ExitCode == 0, export.Stderr);
        string path = Path.Combine(exported, "shared.json");
        var events = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        var commands = events["commands"]!.AsArray();
        Assert.Equal(10, commands.Count);
        Assert.Equal("markFailed", commands[0]!["op"]!.GetValue<string>());
        Assert.Equal("waitForAnyKey", commands[1]!["op"]!.GetValue<string>());
        Assert.Equal("requestReload", commands[2]!["op"]!.GetValue<string>());
        Assert.Equal("itemShortageJump", commands[3]!["op"]!.GetValue<string>());
        Assert.Equal(1, commands[3]!["itemId"]!.GetValue<int>());
        Assert.Equal(2, commands[3]!["count"]!.GetValue<int>());
        Assert.Equal("L0009", commands[3]!["to"]!.GetValue<string>());
        Assert.Equal("playerLacksPoisonJump", commands[4]!["op"]!.GetValue<string>());
        Assert.Equal(560, commands[4]!["objectId"]!.GetValue<int>());
        Assert.Equal("L0008", commands[4]!["to"]!.GetValue<string>());
        Assert.Equal("enemyLacksPoisonJump", commands[5]!["op"]!.GetValue<string>());
        Assert.Equal(561, commands[5]!["objectId"]!.GetValue<int>());
        Assert.Equal("L0007", commands[5]!["to"]!.GetValue<string>());
        Assert.Equal("notPoisonedJump", commands[6]!["op"]!.GetValue<string>());
        Assert.Equal("L0006", commands[6]!["to"]!.GetValue<string>());
        Assert.Equal("enemyHpAboveJump", commands[7]!["op"]!.GetValue<string>());
        Assert.Equal(30, commands[7]!["percent"]!.GetValue<int>());
        Assert.Equal("L0005", commands[7]!["to"]!.GetValue<string>());
        Assert.Equal("enemyTurnJump", commands[8]!["op"]!.GetValue<string>());
        Assert.Equal("L0004", commands[8]!["to"]!.GetValue<string>());
        Assert.Equal("partyHpNotFullJump", commands[9]!["op"]!.GetValue<string>());
        Assert.Equal("L0000", commands[9]!["to"]!.GetValue<string>());
        var rebuild = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", rebuilt);
        Assert.True(rebuild.ExitCode == 0, rebuild.Stderr);
        Assert.Equal(original, File.ReadAllBytes(rebuilt)); // byte-exact round trip of the named batch

        // Semantic edits re-encode the signed threshold, the poison object ids, the percent and
        // the resolved jump WORDs; enemyTurnJump's target moves to L0004 by resolving its label.
        commands[3]!["count"] = -1; // signed threshold: two's-complement WORD 0xFFFF
        commands[3]!["to"] = "L0001";
        commands[4]!["objectId"] = 551;
        commands[4]!["to"] = "L0000"; // zero-target rule: references index 0, re-encodes as WORD 0
        commands[5]!["objectId"] = 564;
        commands[7]!["percent"] = 99;
        commands[8]!["to"] = "L0006";
        File.WriteAllText(path, events.ToJsonString());
        var edited = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", Path.Combine(root, "edited.bin"));
        Assert.True(edited.ExitCode == 0, edited.Stderr);
        Assert.Contains("differs from the recorded source SHA-256", edited.Stdout);
        Assert.Equal(Words(
            0x0041, 0, 0, 0,
            0x004D, 0, 0, 0,
            0x004E, 0, 0, 0,
            0x0058, 1, unchecked((int)0xFFFF), 1,
            0x005D, 551, 0, 0,
            0x005E, 564, 7, 0,
            0x0061, 6, 0, 0,
            0x0064, 99, 5, 0,
            0x0068, 6, 0, 0,
            0x0074, 0, 0, 0), File.ReadAllBytes(Path.Combine(root, "edited.bin")));

        // A dangling jump target is rejected without producing output.
        commands[8]!["to"] = "L0100";
        File.WriteAllText(path, events.ToJsonString());
        var rejected = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", Path.Combine(root, "rejected.bin"));
        Assert.Equal(1, rejected.ExitCode);
        Assert.False(File.Exists(Path.Combine(root, "rejected.bin")));
    }

    [Theory]
    [InlineData(65535, 0)]
    [InlineData(0, 65535)]
    public async Task StartBattle_export_rejects_result_targets_outside_the_actual_chunk(int loss, int flee)
    {
        WriteArchive("SSS.MKF", new byte[32], Words(0, 0, 0, 0), [], [], Words(7, 18, loss, flee));
        var export = await CliProcess.Run(root, "export-events", "--input", input, "--output", exported);
        Assert.Equal(1, export.ExitCode);
        Assert.Contains("startBattle @0000 result target is outside chunk 4", export.Stderr);
        Assert.False(Directory.Exists(exported));
    }

    [Fact]
    public async Task Export_names_runtime_registered_verbs_and_rebuilds_original_chunk4_exactly()
    {
        // One row per runtime-registered verb family, including the retail waitUpdate shape
        // whose truthy operands are 0/0xFFFF and a negative setState SHORT.
        byte[] original = Words(
            0x43, 34, 0, 0,
            9, 12, 0, 65535,
            0x0f, 2, 0, 0,
            0x0b, 0, 0, 0,
            0x10, 9, 21, 0,
            0x46, 10, 20, 0,
            0x15, 3, 0, 0,
            0x49, 2, unchecked((int)0xff85), 0,
            0x52, 15, 0, 0,
            0x40, 1, 0, 0,
            0x6d, 1, 0, 18,
            0x90, 777, 18, 4,
            0x73, 1, 0, 0,
            5, 1, 0, 0,
            0x41, 0, 0, 0,
            0x4f, 0, 0, 0,
            0x4e, 0, 0, 0,
            0x13, 28, 1280, 256,
            0x14, 3, 0, 0,
            0x1d, 1, 9999, 0,
            0x22, 1, 10, 0,
            0x24, 28, 4242, 0,
            0x25, 28, 2500, 0,
            0x4a, 24, 0, 0,
            0x50, 0, 0, 0,
            0x75, 1, 2, 0,
            0x78, 0, 0, 0,
            0x9a, 1, 5, 2,
            0, 0, 0, 0);
        WriteArchive("SSS.MKF", new byte[32], Words(0, 1, 0, 0), [], [], original);
        var export = await CliProcess.Run(root, "export-events", "--input", input, "--output", exported);
        Assert.True(export.ExitCode == 0, export.Stderr);
        using var events = JsonDocument.Parse(File.ReadAllText(Path.Combine(exported, "shared.json")));
        var commands = events.RootElement.GetProperty("commands");
        string[] ops = commands.EnumerateArray().Select(c => c.GetProperty("op").GetString()).ToArray();
        Assert.Equal(new[] { "setMusic", "waitUpdate", "face", "walkStep", "walkTo", "setPartyPos", "setPartyFacing",
            "setState", "hide", "setTriggerMode", "setSceneScripts", "setObjectScript", "fadeScene", "redraw",
            "markFailed", "fadeToRed", "requestReload",
            "setObjectPosition", "setObjectGesture", "changeHpMp", "revivePlayer", "setAutoEntry", "setTriggerEntry",
            "setBattlefield", "fadeOut", "setParty", "noop", "setStateRange", "end" }, ops);
        var wait = commands[1];
        Assert.Equal(12, wait.GetProperty("frames").GetInt32());
        Assert.Equal(0, wait.GetProperty("runTriggers").GetInt32());
        Assert.Equal(65535, wait.GetProperty("updateGestures").GetInt32());
        Assert.Equal(2, commands[2].GetProperty("direction").GetInt32());
        Assert.Equal(0, commands[3].GetProperty("direction").GetInt32());
        Assert.Equal(-123, commands[7].GetProperty("state").GetInt32());
        Assert.Equal(4, commands[11].GetProperty("slotIndex").GetInt32());
        Assert.Equal(1, commands[12].GetProperty("mode").GetInt32());
        Assert.Equal(1, commands[13].GetProperty("delayUnits").GetInt32());
        Assert.Equal(new[] { 28, 1280, 256 }, commands[17].GetProperty("source").GetProperty("operands").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal(4242, commands[21].GetProperty("entry").GetInt32());
        Assert.Equal(28, commands[22].GetProperty("eventObjectId").GetInt32());
        Assert.Equal(2500, commands[22].GetProperty("entry").GetInt32());
        Assert.Equal(24, commands[23].GetProperty("battlefieldId").GetInt32());
        Assert.Equal(new[] { 1, 2, 0 }, commands[25].GetProperty("source").GetProperty("operands").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal(2, commands[27].GetProperty("state").GetInt32());

        var rebuild = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", rebuilt);
        Assert.True(rebuild.ExitCode == 0, rebuild.Stderr);
        Assert.Equal(original, File.ReadAllBytes(rebuilt));
    }

    [Fact]
    public async Task Export_events_preserves_complete_script_and_rebuilds_original_chunk4()
    {
        byte[] original = ReadChunk4();
        byte[] originalArchive = File.ReadAllBytes(Path.Combine(input, "SSS.MKF"));
        var export = await CliProcess.Run(root, "export-events", "--input", input, "--output", exported);
        Assert.True(export.ExitCode == 0, export.Stderr);
        Assert.Empty(export.Stdout);
        Assert.Empty(export.Stderr);

        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(exported, "index.json")));
        Assert.Equal(1, index.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal("faithful", index.RootElement.GetProperty("profile").GetString());
        Assert.Equal(4, index.RootElement.GetProperty("source").GetProperty("chunk").GetInt32());
        Assert.Equal(4, index.RootElement.GetProperty("source").GetProperty("instructionCount").GetInt32());
        using var events = JsonDocument.Parse(File.ReadAllText(Path.Combine(exported, "shared.json")));
        var commands = events.RootElement.GetProperty("commands");
        Assert.Equal(4, commands.GetArrayLength());
        var entries = events.RootElement.GetProperty("entries");
        Assert.Contains(entries.EnumerateArray(), entry => entry.GetProperty("owner").GetProperty("type").GetString() == "scene"
            && entry.GetProperty("slot").GetString() == "wScriptOnEnter" && entry.GetProperty("target").GetString() == "L0001");
        Assert.Contains(entries.EnumerateArray(), entry => entry.GetProperty("owner").GetProperty("type").GetString() == "eventObject"
            && entry.GetProperty("slot").GetString() == "wTriggerScript" && entry.GetProperty("target").GetString() == "L0001");
        Assert.Contains(entries.EnumerateArray(), entry => entry.GetProperty("slot").GetString() == "wScriptOnTeleport"
            && entry.GetProperty("sourceAddress").GetInt32() == 0 && entry.GetProperty("target").ValueKind == JsonValueKind.Null);
        Assert.Equal("setDialog", commands[0].GetProperty("op").GetString());
        Assert.Equal("upper", commands[0].GetProperty("box").GetString());
        Assert.Equal(59, commands[0].GetProperty("portraitId").GetInt32());
        Assert.Equal("end", commands[2].GetProperty("op").GetString());
        Assert.Equal("keep", commands[2].GetProperty("mode").GetString());
        Assert.Equal("raw", commands[3].GetProperty("op").GetString());
        Assert.Equal("showDialog", commands[1].GetProperty("op").GetString());
        Assert.Equal(61, commands[1].GetProperty("messageId").GetInt32());
        Assert.Equal(17, commands[1].GetProperty("descriptionLineWord").GetInt32());
        Assert.Equal(new[] { 61, 17, 7 }, commands[1].GetProperty("source").GetProperty("operands").EnumerateArray().Select(x => x.GetInt32()));

        var rebuild = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", rebuilt);
        Assert.Equal(0, rebuild.ExitCode);
        Assert.Empty(rebuild.Stdout);
        Assert.Empty(rebuild.Stderr);
        Assert.Equal(original, File.ReadAllBytes(rebuilt));
        Assert.Equal(originalArchive, File.ReadAllBytes(Path.Combine(input, "SSS.MKF")));
    }

    [Theory]
    [InlineData("index.json", "invalid-byte")]
    [InlineData("shared.json", "invalid-byte")]
    [InlineData("index.json", "truncated-sequence")]
    [InlineData("shared.json", "truncated-sequence")]
    [InlineData("index.json", "utf16-bom")]
    [InlineData("shared.json", "utf16-bom")]
    public async Task Rebuild_events_rejects_non_utf8_comment_bytes_without_replacing_or_writing_output(string file, string encoding)
    {
        var export = await CliProcess.Run(root, "export-events", "--input", input, "--output", exported);
        Assert.True(export.ExitCode == 0, export.Stderr);
        string path = Path.Combine(exported, file);
        string json = File.ReadAllText(path).TrimEnd();
        string prefix = json[..^1] + ", \"_note\": \"";
        byte[] bytes = encoding switch
        {
            "invalid-byte" => [.. Encoding.UTF8.GetBytes(prefix), 0xff, .. Encoding.UTF8.GetBytes("\"}\n")],
            "truncated-sequence" => [.. Encoding.UTF8.GetBytes(prefix), 0xe2, 0x82, .. Encoding.UTF8.GetBytes("\"}\n")],
            _ => [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(prefix + "中文\"}\n")]
        };
        File.WriteAllBytes(path, bytes);
        var before = Directory.GetFiles(exported).ToDictionary(name => name, File.ReadAllBytes);
        byte[] originalArchive = File.ReadAllBytes(Path.Combine(input, "SSS.MKF"));

        var result = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", rebuilt);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains(file, result.Stderr, StringComparison.Ordinal);
        Assert.Contains("not valid UTF-8", result.Stderr, StringComparison.Ordinal);
        Assert.False(File.Exists(rebuilt));
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(exported).Order());
        foreach (var pair in before) Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key));
        Assert.Equal(originalArchive, File.ReadAllBytes(Path.Combine(input, "SSS.MKF")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rebuild_events_accepts_unicode_comments_and_optional_utf8_bom_without_changing_script(bool bom)
    {
        var export = await CliProcess.Run(root, "export-events", "--input", input, "--output", exported);
        Assert.True(export.ExitCode == 0, export.Stderr);
        foreach (string path in Directory.GetFiles(exported))
        {
            string json = File.ReadAllText(path).TrimEnd();
            // A literal U+FFFD is valid Unicode; reject malformed bytes, not the character.
            byte[] text = Encoding.UTF8.GetBytes(json[..^1] + ", \"_note\": \"中文 �\"}\n");
            File.WriteAllBytes(path, bom ? [.. Encoding.UTF8.GetPreamble(), .. text] : text);
        }
        var before = Directory.GetFiles(exported).ToDictionary(name => name, File.ReadAllBytes);

        var result = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", rebuilt);

        Assert.True(result.ExitCode == 0, result.Stderr);
        Assert.Empty(result.Stdout);
        Assert.Empty(result.Stderr);
        Assert.Equal(ReadChunk4(), File.ReadAllBytes(rebuilt));
        foreach (var pair in before) Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key));
    }

    [Theory]
    [InlineData("entry-id", "Entry ids must be unique identifiers.")]
    [InlineData("label", "invalid command label.")]
    [InlineData("local-label", "malformed reference")]
    [InlineData("cross-file-label", "malformed file#label reference")]
    [InlineData("sha256", "index.json: sha256 must be lowercase hexadecimal.")]
    public async Task Rebuild_events_rejects_final_line_feed_in_identifiers_and_hash(string mutation, string diagnostic)
    {
        var export = await CliProcess.Run(root, "export-events", "--input", input, "--output", exported);
        Assert.True(export.ExitCode == 0, export.Stderr);
        string eventPath = Path.Combine(exported, "shared.json");
        string indexPath = Path.Combine(exported, "index.json");
        var events = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(eventPath))!.AsObject();
        var index = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(indexPath))!.AsObject();
        if (mutation == "entry-id")
            events["entries"]![0]!["id"] = events["entries"]![0]!["id"]!.GetValue<string>() + "\n";
        else if (mutation == "sha256")
            index["source"]!["sha256"] = index["source"]!["sha256"]!.GetValue<string>() + "\n";
        else if (mutation == "label")
            events["commands"]![0]!["label"] = "L0000\n"; // No incoming reference to address zero in this package.
        else
        {
            // Keep every reference resolved and the complete original instruction bytes intact.
            const string label = "L0001\n";
            events["commands"]![1]!["label"] = label;
            string reference = mutation == "cross-file-label" ? "shared#" + label : label;
            events["commands"]![0]!["next"] = reference;
            foreach (var entry in events["entries"]!.AsArray())
                if (entry!["sourceAddress"]!.GetValue<int>() == 1) entry["target"] = reference;
        }
        File.WriteAllText(eventPath, events.ToJsonString());
        File.WriteAllText(indexPath, index.ToJsonString());
        var result = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", rebuilt);
        // In RED, the old validator accepts these and writes unchanged bytes (hash LF
        // misleadingly reports a semantic edit); this is not a dangling-reference failure.
        if (result.ExitCode == 0) Assert.Equal(ReadChunk4(), File.ReadAllBytes(rebuilt));
        Assert.True(result.ExitCode == 1, $"Expected schema rejection; exit={result.ExitCode}; stdout={result.Stdout}; stderr={result.Stderr}");
        Assert.Empty(result.Stdout);
        Assert.Contains(diagnostic, result.Stderr, StringComparison.Ordinal);
        Assert.False(File.Exists(rebuilt));
    }

    [Fact]
    public async Task Rebuild_events_reencodes_edited_named_fields_and_preserves_unmapped_operands()
    {
        var export = await CliProcess.Run(root, "export-events", "--input", input, "--output", exported);
        Assert.True(export.ExitCode == 0, export.Stderr);
        string path = Path.Combine(exported, "shared.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var rootElement = document.RootElement;
        var command = JsonNodeForEdit(rootElement);
        command["commands"]![0]!["color"] = 12;
        File.WriteAllText(path, command.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        var rebuild = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", rebuilt);
        Assert.Equal(0, rebuild.ExitCode);
        Assert.Contains("differs from the recorded source SHA-256", rebuild.Stdout);
        byte[] bytes = File.ReadAllBytes(rebuilt);
        Assert.Equal((ushort)59, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)));
        Assert.Equal((ushort)12, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4)));
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6)));
        Assert.Equal((ushort)7, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(14)));
    }

    [Fact]
    public async Task Rebuild_events_resolves_local_and_cross_file_references_after_splitting_commands()
    {
        var export = await CliProcess.Run(root, "export-events", "--input", input, "--output", exported);
        Assert.Equal(0, export.ExitCode);
        string sharedPath = Path.Combine(exported, "shared.json");
        var shared = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(sharedPath))!.AsObject();
        var indexPath = Path.Combine(exported, "index.json");
        var index = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(indexPath))!.AsObject();
        index["files"] = new System.Text.Json.Nodes.JsonArray("scene-001", "shared");
        File.WriteAllText(indexPath, index.ToJsonString());

        var scene = new System.Text.Json.Nodes.JsonObject
        {
            ["formatVersion"] = 1, ["profile"] = "faithful", ["fileId"] = "scene-001",
            ["entries"] = shared["entries"]!.DeepClone(),
            ["commands"] = new System.Text.Json.Nodes.JsonArray()
        };
        shared["entries"] = new System.Text.Json.Nodes.JsonArray();
        var commands = shared["commands"]!.AsArray();
        var all = commands.Select(node => node!.DeepClone().AsObject()).ToArray();
        commands.Clear();
        string[] labels = ["zero", "one", "two", "three"];
        for (int address = 0; address < all.Length; address++)
        {
            var command = all[address];
            command["label"] = labels[address];
            bool currentIsShared = address < 2;
            command["next"] = address == all.Length - 1 ? null
                : ((address + 1 < 2) == currentIsShared ? labels[address + 1]
                    : ((address + 1 < 2) ? "shared#" : "scene-001#") + labels[address + 1]);
            (currentIsShared ? shared["commands"]!.AsArray() : scene["commands"]!.AsArray()).Add(command);
        }
        foreach (var node in scene["entries"]!.AsArray())
        {
            var entry = node!.AsObject();
            entry["target"] = entry["sourceAddress"]!.GetValue<int>() == 0 ? null : "shared#one";
        }
        File.WriteAllText(sharedPath, shared.ToJsonString());
        File.WriteAllText(Path.Combine(exported, "scene-001.json"), scene.ToJsonString());

        var rebuild = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", rebuilt);
        Assert.True(rebuild.ExitCode == 0, rebuild.Stderr);
        Assert.Equal(ReadChunk4(), File.ReadAllBytes(rebuilt));
    }

    [Theory]
    [InlineData("dangling-next")]
    [InlineData("duplicate-address")]
    [InlineData("dangling-entry")]
    [InlineData("malformed-next")]
    [InlineData("duplicate-label")]
    [InlineData("entry-address-mismatch")]
    public async Task Rebuild_events_rejects_dangling_or_duplicate_global_references(string mutation)
    {
        var export = await CliProcess.Run(root, "export-events", "--input", input, "--output", exported);
        Assert.Equal(0, export.ExitCode);
        string eventPath = Path.Combine(exported, "shared.json");
        var events = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(eventPath))!.AsObject();
        if (mutation == "dangling-next") events["commands"]![0]! ["next"] = "missing#L0001";
        else if (mutation == "duplicate-address")
        {
            var duplicate = events["commands"]!.AsArray()[0]!.DeepClone();
            events["commands"]!.AsArray().Add(duplicate);
        }
        else if (mutation == "duplicate-label") events["commands"]![1]!["label"] = "L0000";
        else if (mutation == "malformed-next") events["commands"]![0]!["next"] = "shared##L0001";
        else if (mutation == "entry-address-mismatch") events["entries"]!.AsArray()[0]!["sourceAddress"] = 2;
        else events["entries"]!.AsArray()[0]!["target"] = "missing#L0001";
        File.WriteAllText(eventPath, events.ToJsonString());
        var result = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", rebuilt);
        Assert.Equal(1, result.ExitCode);
        Assert.NotEmpty(result.Stderr);
        Assert.False(File.Exists(rebuilt));
    }

    [Fact]
    public async Task Export_events_preserves_preexisting_empty_output_on_invalid_archive()
    {
        string outputDirectory = Path.Combine(root, "preexisting-empty");
        Directory.CreateDirectory(outputDirectory);
        byte[] original = File.ReadAllBytes(Path.Combine(input, "SSS.MKF"));
        File.WriteAllBytes(Path.Combine(input, "SSS.MKF"), [1, 2, 3]);
        var result = await CliProcess.Run(root, "export-events", "--input", input, "--output", outputDirectory);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outputDirectory));
        Assert.True(Directory.Exists(outputDirectory));
        File.WriteAllBytes(Path.Combine(input, "SSS.MKF"), original);
    }

    [Fact]
    public async Task Rebuild_events_rejects_unknown_fields_and_does_not_create_output()
    {
        var export = await CliProcess.Run(root, "export-events", "--input", input, "--output", exported);
        Assert.True(export.ExitCode == 0, export.Stderr);
        string path = Path.Combine(exported, "shared.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var node = System.Text.Json.Nodes.JsonNode.Parse(doc.RootElement.GetRawText())!.AsObject();
        node["commands"]![0]!["colorrr"] = 7;
        File.WriteAllText(path, node.ToJsonString());
        var result = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", rebuilt);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("unknown field", result.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(rebuilt));
    }

    [Fact]
    public async Task Rebuild_events_rejects_missing_address_without_writing_partial_chunk()
    {
        var export = await CliProcess.Run(root, "export-events", "--input", input, "--output", exported);
        Assert.True(export.ExitCode == 0, export.Stderr);
        string path = Path.Combine(exported, "shared.json");
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        node["commands"]!.AsArray().RemoveAt(3);
        File.WriteAllText(path, node.ToJsonString());
        var result = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", rebuilt);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("cover every source address", result.Stderr);
        Assert.False(File.Exists(rebuilt));
    }

    [Fact]
    public async Task Rebuild_events_rejects_duplicate_json_keys_and_invalid_contract_values()
    {
        var export = await CliProcess.Run(root, "export-events", "--input", input, "--output", exported);
        Assert.Equal(0, export.ExitCode);
        string indexPath = Path.Combine(exported, "index.json");
        string originalIndex = File.ReadAllText(indexPath);
        File.WriteAllText(indexPath, originalIndex.Replace("\"formatVersion\": 1", "\"formatVersion\": 1,\n  \"formatVersion\": 1", StringComparison.Ordinal));
        var duplicate = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", rebuilt);
        Assert.Equal(1, duplicate.ExitCode);
        Assert.Contains("duplicate JSON key", duplicate.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(rebuilt));

        File.WriteAllText(indexPath, originalIndex);
        string eventPath = Path.Combine(exported, "shared.json");
        var events = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(eventPath))!.AsObject();
        events["commands"]![0]!["portraitId"] = 65536;
        File.WriteAllText(eventPath, events.ToJsonString());
        var outOfRange = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", rebuilt);
        Assert.Equal(1, outOfRange.ExitCode);
        Assert.Contains("overflow", outOfRange.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(rebuilt));
    }

    [Fact]
    public async Task Rebuild_events_supports_full_word_address_space_and_wraps_next_to_zero()
    {
        byte[] fullScript = new byte[65536 * 8];
        for (int index = 0; index < 65536; index++)
            BinaryPrimitives.WriteUInt16LittleEndian(fullScript.AsSpan(index * 8), 0x0086);
        // Literal last instruction exercises both FFFF result addresses and WORD next wrap.
        Words(7, 65535, 65535, 65535).CopyTo(fullScript, 65535 * 8);
        WriteArchive("SSS.MKF", new byte[32], Words(0, 0, 0, 0), Words(0, 0, 0, 0), [], fullScript);
        var export = await CliProcess.Run(root, "export-events", "--input", input, "--output", exported);
        Assert.True(export.ExitCode == 0, export.Stderr);
        using var events = JsonDocument.Parse(File.ReadAllText(Path.Combine(exported, "shared.json")));
        var commands = events.RootElement.GetProperty("commands");
        Assert.Equal(65536, commands.GetArrayLength());
        Assert.Equal("L0000", commands[65535].GetProperty("next").GetString());
        Assert.Equal("LFFFF", commands[65534].GetProperty("next").GetString());
        Assert.Equal("startBattle", commands[65535].GetProperty("op").GetString());
        Assert.Equal("LFFFF", commands[65535].GetProperty("lossTarget").GetString());
        Assert.Equal("LFFFF", commands[65535].GetProperty("fleeTarget").GetString());
        Assert.False(commands[65535].GetProperty("isBoss").GetBoolean());

        var rebuild = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", rebuilt);
        Assert.Equal(0, rebuild.ExitCode);
        Assert.Equal(fullScript, File.ReadAllBytes(rebuilt));
    }

    private static System.Text.Json.Nodes.JsonObject JsonNodeForEdit(JsonElement root)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(root.GetRawText())!.AsObject();
        return node;
    }

    private byte[] ReadChunk4()
    {
        var archives = Directory.GetFiles(input);
        var archive = File.ReadAllBytes(archives[0]);
        int offset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(16)));
        return archive[offset..];
    }

    private static byte[] Words(params int[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), unchecked((ushort)values[i]));
        return bytes;
    }

    private void WriteArchive(string name, params byte[][] chunks)
    {
        int dataOffset = (chunks.Length + 1) * 4;
        var result = new byte[dataOffset + chunks.Sum(chunk => chunk.Length)];
        BinaryPrimitives.WriteUInt32LittleEndian(result, (uint)dataOffset);
        int cursor = dataOffset;
        for (int i = 0; i < chunks.Length; i++)
        {
            chunks[i].CopyTo(result, cursor);
            cursor += chunks[i].Length;
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan((i + 1) * 4), (uint)cursor);
        }
        File.WriteAllBytes(Path.Combine(input, name), result);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
