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
            Words(0x003c, 59, 79, 1, 0xffff, 61, 17, 7, 0, 0, 0, 0, 0x0032, 0, 65535, 7));
    }

    [Fact]
    public async Task StartBattle_export_roundtrip_and_semantic_edits_use_resolved_targets_not_source_words()
    {
        byte[] original = Words(7, 65535, 0, 0, 7, 18, 4, 3, 0x0032, 65535, 7, 9, 0, 5, 6, 7, 0, 8, 9, 10);
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
        Assert.Equal(Words(7, 19, 4, 1, 7, 20, 0, 0, 0x0032, 65535, 7, 9, 0, 5, 6, 7, 0, 8, 9, 10), File.ReadAllBytes(Path.Combine(root, "edited.bin")));
    }

    [Fact]
    public async Task Legacy_raw7_package_rebuilds_without_a_named_mapping_migration()
    {
        byte[] original = Words(0x0032, 0, 65535, 8, 7, 18, 65535, 0, 0, 7, 8, 9);
        WriteArchive("SSS.MKF", new byte[32], Words(0, 1, 0, 0), [], [], Words(0x0032, 0, 65535, 8, 0x0032, 18, 65535, 0, 0, 7, 8, 9));
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
        WriteArchive("SSS.MKF", new byte[32], Words(0, 1, 0, 0), [], [], Words(0x0032, 0, 65535, 8, 7, 18, 0, 0));
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

    [Fact]
    public async Task Conditional_jump_batch3_verbs_export_roundtrip_and_reencode_jump_targets_from_labels()
    {
        // M5 batch 3 (#98): conditional jumps part 2 plus delay, quitGame (stop-class successor)
        // and randomBranch (multi-way: one of the next `count` entries). Hand-built stream, not retail data.
        byte[] original = Words(
            0x0079, 3, 7, 0,     // roleInPartyJump: role name word 3 in party -> L0007
            0x0081, 2, 1, 6,     // notFacingObjectJump: player not facing object 2 zone 1 -> L0006
            0x0083, 5, 2, 4,     // objectOutOfZoneJump: object 5 outside caller zone 2 -> L0004
            0x0085, 25, 0, 0,    // delay 25 units (2000 ms wall clock, no game updates)
            0x0086, 87, 2, 9,    // itemNotEquippedJump: fewer than 2 of item 87 equipped -> L0009
            0x0091, 3, 0, 0,     // notFirstOfKindJump: not first of same kind -> L0003
            0x0094, 4, 1, 8,     // stateMatchJump: object 4 state 1 -> L0008
            0x0095, 13, 5, 0,    // sceneMatchJump: current scene 13 -> L0005
            0x00A0, 0, 0, 0,     // quitGame (stops the run, no operands)
            0x00A2, 3, 0, 0);    // randomBranch: one of the next 3 entries
        WriteArchive("SSS.MKF", new byte[32], Words(0, 1, 0, 0), [], [], original);
        var export = await CliProcess.Run(root, "export-events", "--input", input, "--output", exported);
        Assert.True(export.ExitCode == 0, export.Stderr);
        string path = Path.Combine(exported, "shared.json");
        var events = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        var commands = events["commands"]!.AsArray();
        Assert.Equal(10, commands.Count);
        Assert.Equal("roleInPartyJump", commands[0]!["op"]!.GetValue<string>());
        Assert.Equal(3, commands[0]!["roleNameWord"]!.GetValue<int>());
        Assert.Equal("L0007", commands[0]!["to"]!.GetValue<string>());
        Assert.Equal("notFacingObjectJump", commands[1]!["op"]!.GetValue<string>());
        Assert.Equal(2, commands[1]!["eventObjectId"]!.GetValue<int>());
        Assert.Equal(1, commands[1]!["zoneUnits"]!.GetValue<int>());
        Assert.Equal("L0006", commands[1]!["to"]!.GetValue<string>());
        Assert.Equal("objectOutOfZoneJump", commands[2]!["op"]!.GetValue<string>());
        Assert.Equal(5, commands[2]!["eventObjectId"]!.GetValue<int>());
        Assert.Equal(2, commands[2]!["zoneUnits"]!.GetValue<int>());
        Assert.Equal("L0004", commands[2]!["to"]!.GetValue<string>());
        Assert.Equal("delay", commands[3]!["op"]!.GetValue<string>());
        Assert.Equal(25, commands[3]!["delayUnits"]!.GetValue<int>());
        Assert.Equal("itemNotEquippedJump", commands[4]!["op"]!.GetValue<string>());
        Assert.Equal(87, commands[4]!["itemId"]!.GetValue<int>());
        Assert.Equal(2, commands[4]!["count"]!.GetValue<int>());
        Assert.Equal("L0009", commands[4]!["to"]!.GetValue<string>());
        Assert.Equal("notFirstOfKindJump", commands[5]!["op"]!.GetValue<string>());
        Assert.Equal("L0003", commands[5]!["to"]!.GetValue<string>());
        Assert.Equal("stateMatchJump", commands[6]!["op"]!.GetValue<string>());
        Assert.Equal(4, commands[6]!["eventObjectId"]!.GetValue<int>());
        Assert.Equal(1, commands[6]!["state"]!.GetValue<int>());
        Assert.Equal("L0008", commands[6]!["to"]!.GetValue<string>());
        Assert.Equal("sceneMatchJump", commands[7]!["op"]!.GetValue<string>());
        Assert.Equal(13, commands[7]!["sceneId"]!.GetValue<int>());
        Assert.Equal("L0005", commands[7]!["to"]!.GetValue<string>());
        Assert.Equal("quitGame", commands[8]!["op"]!.GetValue<string>());
        Assert.Equal("randomBranch", commands[9]!["op"]!.GetValue<string>());
        Assert.Equal(3, commands[9]!["count"]!.GetValue<int>());
        var rebuild = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", rebuilt);
        Assert.True(rebuild.ExitCode == 0, rebuild.Stderr);
        Assert.Equal(original, File.ReadAllBytes(rebuilt)); // byte-exact round trip of the named batch

        // Semantic edits re-encode the signed state, the branch count, the role name word and
        // the resolved jump WORDs; roleInPartyJump's target exercises the zero-target rule.
        commands[0]!["roleNameWord"] = 6;
        commands[0]!["to"] = "L0000"; // zero-target rule: references index 0, re-encodes as WORD 0
        commands[1]!["eventObjectId"] = 9;
        commands[1]!["zoneUnits"] = 2;
        commands[2]!["to"] = "L0001";
        commands[3]!["delayUnits"] = 30;
        commands[4]!["itemId"] = 195;
        commands[4]!["count"] = 1;
        commands[4]!["to"] = "L0002";
        commands[5]!["to"] = "L0004";
        commands[6]!["eventObjectId"] = 0; // caller's own object
        commands[6]!["state"] = -1; // signed: two's-complement WORD 0xFFFF
        commands[7]!["sceneId"] = 20;
        commands[7]!["to"] = "L0003";
        commands[9]!["count"] = 5;
        File.WriteAllText(path, events.ToJsonString());
        var edited = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", Path.Combine(root, "edited.bin"));
        Assert.True(edited.ExitCode == 0, edited.Stderr);
        Assert.Contains("differs from the recorded source SHA-256", edited.Stdout);
        Assert.Equal(Words(
            0x0079, 6, 0, 0,
            0x0081, 9, 2, 6,
            0x0083, 5, 2, 1,
            0x0085, 30, 0, 0,
            0x0086, 195, 1, 2,
            0x0091, 4, 0, 0,
            0x0094, 0, unchecked((int)0xFFFF), 8,
            0x0095, 20, 3, 0,
            0x00A0, 0, 0, 0,
            0x00A2, 5, 0, 0), File.ReadAllBytes(Path.Combine(root, "edited.bin")));

        // A dangling jump target is rejected without producing output.
        commands[5]!["to"] = "L0100";
        File.WriteAllText(path, events.ToJsonString());
        var rejected = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", Path.Combine(root, "rejected.bin"));
        Assert.Equal(1, rejected.ExitCode);
        Assert.False(File.Exists(Path.Combine(root, "rejected.bin")));
    }

    [Fact]
    public async Task Dialog_batch4_verbs_export_roundtrip_and_reencode_named_fields_from_labels()
    {
        // M5 batch 4 (#99): dialog presentation - the four setDialog variants, showDialog, redraw
        // (corrected: delay on operand 1, gesture flag on operand 2, operand 0 unused), plus the new
        // restoreScreen / backupScreen. Hand-built stream, not retail data.
        byte[] original = Words(
            0x00A6, 0, 0, 0,           // backupScreen (shared executor, no operands)
            0x003C, 59, 0x4F79, 1,     // setDialog upper: portrait 59, color 0x79 (high byte 0x4F), rng 1
            0xFFFF, 101, 0, 0,         // showDialog 101
            0x008E, 0, 0, 0,           // restoreScreen (trigger switch only)
            0x0005, 77, 2, 1,          // redraw: 2 delay units (120 ms), gestures on, operand 0 unused
            0x003B, 0x3112, 0, 0,      // setDialog center: color 0x12 (high byte 0x31), rng 0
            0x003D, 60, 200, 0,        // setDialog lower: portrait 60, color 200, rng 0
            0x003E, 0x2107, 9, 8,      // setDialog centerWindow: color 7 (high byte 0x21), operands 1/2 preserved
            0, 0, 0, 0);               // end keep
        WriteArchive("SSS.MKF", new byte[32], Words(0, 1, 0, 0), [], [], original);
        var export = await CliProcess.Run(root, "export-events", "--input", input, "--output", exported);
        Assert.True(export.ExitCode == 0, export.Stderr);
        string path = Path.Combine(exported, "shared.json");
        var events = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        var commands = events["commands"]!.AsArray();
        Assert.Equal(9, commands.Count);
        Assert.Equal("backupScreen", commands[0]!["op"]!.GetValue<string>());
        Assert.Equal("setDialog", commands[1]!["op"]!.GetValue<string>());
        Assert.Equal("upper", commands[1]!["box"]!.GetValue<string>());
        Assert.Equal(59, commands[1]!["portraitId"]!.GetValue<int>());
        Assert.Equal(0x79, commands[1]!["color"]!.GetValue<int>());
        Assert.Equal(1, commands[1]!["rngWord"]!.GetValue<int>());
        Assert.Equal("showDialog", commands[2]!["op"]!.GetValue<string>());
        Assert.Equal(101, commands[2]!["messageId"]!.GetValue<int>());
        Assert.Equal(0, commands[2]!["descriptionLineWord"]!.GetValue<int>());
        Assert.Equal("restoreScreen", commands[3]!["op"]!.GetValue<string>());
        Assert.Equal("redraw", commands[4]!["op"]!.GetValue<string>());
        Assert.Equal(2, commands[4]!["delayUnits"]!.GetValue<int>());
        Assert.Equal(1, commands[4]!["updateGestures"]!.GetValue<int>());
        Assert.Equal("setDialog", commands[5]!["op"]!.GetValue<string>());
        Assert.Equal("center", commands[5]!["box"]!.GetValue<string>());
        Assert.Equal(0x12, commands[5]!["color"]!.GetValue<int>());
        Assert.Equal("setDialog", commands[6]!["op"]!.GetValue<string>());
        Assert.Equal("lower", commands[6]!["box"]!.GetValue<string>());
        Assert.Equal("setDialog", commands[7]!["op"]!.GetValue<string>());
        Assert.Equal("centerWindow", commands[7]!["box"]!.GetValue<string>());
        Assert.Equal(7, commands[7]!["color"]!.GetValue<int>());
        var rebuild = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", rebuilt);
        Assert.True(rebuild.ExitCode == 0, rebuild.Stderr);
        Assert.Equal(original, File.ReadAllBytes(rebuilt)); // byte-exact round trip of the named batch

        // Semantic edits re-encode from fields, not source words: low-byte colors keep their source
        // high bytes, the portrait / rng / delay / gesture / message words are rewritten, and the
        // unused redraw operand 0 plus centerWindow operands 1-2 keep their source WORDs.
        commands[1]!["portraitId"] = 61;
        commands[1]!["color"] = 200;
        commands[2]!["messageId"] = 102;
        commands[2]!["descriptionLineWord"] = 1;
        commands[4]!["delayUnits"] = 5;
        commands[4]!["updateGestures"] = 0;
        commands[5]!["rngWord"] = 1;
        commands[6]!["portraitId"] = 58;
        commands[7]!["color"] = 200;
        File.WriteAllText(path, events.ToJsonString());
        var edited = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", Path.Combine(root, "edited.bin"));
        Assert.True(edited.ExitCode == 0, edited.Stderr);
        Assert.Contains("differs from the recorded source SHA-256", edited.Stdout);
        Assert.Equal(Words(
            0x00A6, 0, 0, 0,
            0x003C, 61, 0x4FC8, 1,
            0xFFFF, 102, 1, 0,
            0x008E, 0, 0, 0,
            0x0005, 77, 5, 0,
            0x003B, 0x3112, 0, 1,
            0x003D, 58, 200, 0,
            0x003E, 0x21C8, 9, 8,
            0, 0, 0, 0), File.ReadAllBytes(Path.Combine(root, "edited.bin")));

        // An unrelated semantic field is rejected without producing output.
        commands[2]!["text"] = "hello";
        File.WriteAllText(path, events.ToJsonString());
        var rejected = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", Path.Combine(root, "rejected.bin"));
        Assert.Equal(1, rejected.ExitCode);
        Assert.False(File.Exists(Path.Combine(root, "rejected.bin")));
    }

    [Fact]
    public async Task Status_item_batch1_verbs_export_roundtrip_and_reencode_named_fields_from_labels()
    {
        // M5 status-item batch 1 (#100): the seven new setExtraAttr / equipItem / incPlayerAttr /
        // setPlayerStat / changeCash / addItem / removeItem verbs plus the re-verified changeHp /
        // changeMp / changeHpMp. Hand-built stream, not retail data. The equip rows follow the
        // PLAYERROLES WORD layout (17=attack .. 22=poisonResistance; 64=walkFrames), equip parts
        // are 0x0B+body part, and removeItem's operand 2 is a nullable shortage target.
        byte[] original = Words(
            0x0017, 0x0B, 19, unchecked((int)0xFFF6),  // setExtraAttr: head part, defense row, -10
            0x0018, 0x0D, 208, 0,                      // equipItem: shoulder part, item 208
            0x0019, 7, 100, 2,                         // incPlayerAttr: maxHP row, +100, role operand 2
            0x001A, 64, 3, 0,                          // setPlayerStat: walkFrames row, 3, caller role
            0x001B, 0, 300, 0,                         // changeHp: one player +300
            0x001C, 1, unchecked((int)0xFFF6), 0,      // changeMp: whole party -10
            0x001D, 1, 25, 0,                          // changeHpMp: whole party +25 to both stats
            0x001E, 65511, 8, 7,                        // changeCash: -25, fail target L0008
            0x001F, 138, 9, 5,                         // addItem: item 138 x9
            0x0020, 148, 1, 9,                         // removeItem: item 148 x1, shortage target L0009
            0, 0, 0, 0);                               // end keep
        WriteArchive("SSS.MKF", new byte[32], Words(0, 1, 0, 0), [], [], original);
        var export = await CliProcess.Run(root, "export-events", "--input", input, "--output", exported);
        Assert.True(export.ExitCode == 0, export.Stderr);
        string path = Path.Combine(exported, "shared.json");
        var events = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        var commands = events["commands"]!.AsArray();
        Assert.Equal(11, commands.Count);
        Assert.Equal("setExtraAttr", commands[0]!["op"]!.GetValue<string>());
        Assert.Equal(0x0B, commands[0]!["equipPartWord"]!.GetValue<int>());
        Assert.Equal(19, commands[0]!["field"]!.GetValue<int>());
        Assert.Equal(-10, commands[0]!["value"]!.GetValue<int>());
        Assert.Equal("equipItem", commands[1]!["op"]!.GetValue<string>());
        Assert.Equal(0x0D, commands[1]!["equipPartWord"]!.GetValue<int>());
        Assert.Equal(208, commands[1]!["itemId"]!.GetValue<int>());
        Assert.Equal("incPlayerAttr", commands[2]!["op"]!.GetValue<string>());
        Assert.Equal(7, commands[2]!["field"]!.GetValue<int>());
        Assert.Equal(100, commands[2]!["delta"]!.GetValue<int>());
        Assert.Equal(2, commands[2]!["roleOperand"]!.GetValue<int>());
        Assert.Equal("setPlayerStat", commands[3]!["op"]!.GetValue<string>());
        Assert.Equal(64, commands[3]!["field"]!.GetValue<int>());
        Assert.Equal(0, commands[3]!["roleOperand"]!.GetValue<int>());
        Assert.Equal("changeHp", commands[4]!["op"]!.GetValue<string>());
        Assert.Equal(0, commands[4]!["scope"]!.GetValue<int>());
        Assert.Equal(300, commands[4]!["amount"]!.GetValue<int>());
        Assert.Equal("changeMp", commands[5]!["op"]!.GetValue<string>());
        Assert.Equal(1, commands[5]!["scope"]!.GetValue<int>());
        Assert.Equal(-10, commands[5]!["amount"]!.GetValue<int>());
        Assert.Equal("changeHpMp", commands[6]!["op"]!.GetValue<string>());
        Assert.Equal(25, commands[6]!["amount"]!.GetValue<int>());
        Assert.Equal("changeCash", commands[7]!["op"]!.GetValue<string>());
        Assert.Equal(-25, commands[7]!["amount"]!.GetValue<int>());
        Assert.Equal("L0008", commands[7]!["to"]!.GetValue<string>());
        Assert.Equal("addItem", commands[8]!["op"]!.GetValue<string>());
        Assert.Equal(138, commands[8]!["itemId"]!.GetValue<int>());
        Assert.Equal(9, commands[8]!["count"]!.GetValue<int>());
        Assert.Equal("removeItem", commands[9]!["op"]!.GetValue<string>());
        Assert.Equal(148, commands[9]!["itemId"]!.GetValue<int>());
        Assert.Equal(1, commands[9]!["count"]!.GetValue<int>());
        Assert.Equal("L0009", commands[9]!["shortageTarget"]!.GetValue<string>());
        var rebuild = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", rebuilt);
        Assert.True(rebuild.ExitCode == 0, rebuild.Stderr);
        Assert.Equal(original, File.ReadAllBytes(rebuilt)); // byte-exact round trip of the named batch

        // Semantic edits re-encode from fields: the cash fail target moves to L0000 (zero-target
        // rule), removeItem's shortage target becomes null (WORD 0, removal proceeds even when
        // short) with count 0 kept as 0 (runtime normalizes to 1), and preserved operands stay.
        commands[0]!["equipPartWord"] = 0x11; // extra part
        commands[0]!["value"] = 5;
        commands[1]!["itemId"] = 209;
        commands[2]!["field"] = 6; // level row
        commands[2]!["delta"] = -10;
        commands[2]!["roleOperand"] = 0;
        commands[3]!["value"] = 383;
        commands[3]!["roleOperand"] = 2;
        commands[4]!["scope"] = 1;
        commands[5]!["scope"] = 0;
        commands[6]!["amount"] = 30;
        commands[7]!["amount"] = 500;
        commands[7]!["to"] = "L0000";
        commands[8]!["itemId"] = 139;
        commands[8]!["count"] = -1;
        commands[9]!["count"] = 0;
        commands[9]!["shortageTarget"] = null;
        File.WriteAllText(path, events.ToJsonString());
        var edited = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", Path.Combine(root, "edited.bin"));
        Assert.True(edited.ExitCode == 0, edited.Stderr);
        Assert.Contains("differs from the recorded source SHA-256", edited.Stdout);
        Assert.Equal(Words(
            0x0017, 0x11, 19, 5,
            0x0018, 0x0D, 209, 0,
            0x0019, 6, unchecked((int)0xFFF6), 0,
            0x001A, 64, 383, 2,
            0x001B, 1, 300, 0,
            0x001C, 0, unchecked((int)0xFFF6), 0,
            0x001D, 1, 30, 0,
            0x001E, 500, 0, 7,
            0x001F, 139, unchecked((int)0xFFFF), 5,
            0x0020, 148, 0, 0,
            0, 0, 0, 0), File.ReadAllBytes(Path.Combine(root, "edited.bin")));

        // A shortage target pointing at address zero must use null, not a label.
        commands[9]!["shortageTarget"] = "L0000";
        File.WriteAllText(path, events.ToJsonString());
        var rejected = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", Path.Combine(root, "rejected.bin"));
        Assert.Equal(1, rejected.ExitCode);
        Assert.False(File.Exists(Path.Combine(root, "rejected.bin")));
    }

    [Fact]
    public async Task Status_item_batch2_verbs_export_roundtrip_and_reencode_named_fields_from_labels()
    {
        // M5 status-item batch 2 (#101): the nine new removeEquipment / applyPoisonToEnemy /
        // applyPoisonToPlayer / curePoisonByObjectIdForEnemy / curePoisonByObjectIdForPlayer /
        // curePoisonsByLevel / setStatusPlayer / setStatusEnemy / clearPlayerStatus verbs plus
        // the re-verified revivePlayer. Hand-built stream, not retail data. Poison operands are
        // object.json poison-view object IDs, status numbers are rgPlayerStatus/rgwStatus row
        // indices, every numeric operand is an unsigned WORD, and setStatusEnemy's operand 2 is
        // the resisted conditional-jump target.
        byte[] original = Words(
            0x0022, 0, 5, 9,                      // revivePlayer: one player, half of max HP
            0x0023, 2, 3, 7,                      // removeEquipment: role 2, 1-based part 3
            0x0028, 1, 556, 0,                    // applyPoisonToEnemy: all enemies, poison 556
            0x0029, 0, 560, 4,                    // applyPoisonToPlayer: one player, poison 560
            0x002A, 1, 555, 0,                    // curePoisonByObjectIdForEnemy: all enemies
            0x002B, 0, 557, 6,                    // curePoisonByObjectIdForPlayer: one player
            0x002C, 1, 2, 0,                      // curePoisonsByLevel: whole party, level <= 2
            0x002D, 3, 5, 8,                      // setStatusPlayer: row 3, 5 rounds
            0x002E, 5, 3, 11,                     // setStatusEnemy: row 5, 3 rounds, resist target L000B
            0x002F, 2, 11, 13,                    // clearPlayerStatus: row 2
            0, 0, 0, 0,                           // end keep
            0, 0, 0, 0);                          // end keep (jump target L000B)
        WriteArchive("SSS.MKF", new byte[32], Words(0, 1, 0, 0), [], [], original);
        var export = await CliProcess.Run(root, "export-events", "--input", input, "--output", exported);
        Assert.True(export.ExitCode == 0, export.Stderr);
        string path = Path.Combine(exported, "shared.json");
        var events = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var commands = events["commands"]!.AsArray();
        Assert.Equal(12, commands.Count);
        Assert.Equal("revivePlayer", commands[0]!["op"]!.GetValue<string>());
        Assert.Equal(0, commands[0]!["scope"]!.GetValue<int>());
        Assert.Equal(5, commands[0]!["healthTenths"]!.GetValue<int>());
        Assert.Equal("removeEquipment", commands[1]!["op"]!.GetValue<string>());
        Assert.Equal(2, commands[1]!["playerRole"]!.GetValue<int>());
        Assert.Equal(3, commands[1]!["partWord"]!.GetValue<int>());
        Assert.Equal("applyPoisonToEnemy", commands[2]!["op"]!.GetValue<string>());
        Assert.Equal(1, commands[2]!["scope"]!.GetValue<int>());
        Assert.Equal(556, commands[2]!["objectId"]!.GetValue<int>());
        Assert.Equal("applyPoisonToPlayer", commands[3]!["op"]!.GetValue<string>());
        Assert.Equal(0, commands[3]!["scope"]!.GetValue<int>());
        Assert.Equal(560, commands[3]!["objectId"]!.GetValue<int>());
        Assert.Equal("curePoisonByObjectIdForEnemy", commands[4]!["op"]!.GetValue<string>());
        Assert.Equal("curePoisonByObjectIdForPlayer", commands[5]!["op"]!.GetValue<string>());
        Assert.Equal(557, commands[5]!["objectId"]!.GetValue<int>());
        Assert.Equal("curePoisonsByLevel", commands[6]!["op"]!.GetValue<string>());
        Assert.Equal(2, commands[6]!["maxLevel"]!.GetValue<int>());
        Assert.Equal("setStatusPlayer", commands[7]!["op"]!.GetValue<string>());
        Assert.Equal(3, commands[7]!["status"]!.GetValue<int>());
        Assert.Equal(5, commands[7]!["rounds"]!.GetValue<int>());
        Assert.Equal("setStatusEnemy", commands[8]!["op"]!.GetValue<string>());
        Assert.Equal(5, commands[8]!["status"]!.GetValue<int>());
        Assert.Equal(3, commands[8]!["rounds"]!.GetValue<int>());
        Assert.Equal("L000B", commands[8]!["to"]!.GetValue<string>());
        Assert.Equal("clearPlayerStatus", commands[9]!["op"]!.GetValue<string>());
        Assert.Equal(2, commands[9]!["status"]!.GetValue<int>());
        var rebuild = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", rebuilt);
        Assert.True(rebuild.ExitCode == 0, rebuild.Stderr);
        Assert.Equal(original, File.ReadAllBytes(rebuilt)); // byte-exact round trip of the named batch

        // Semantic edits re-encode from fields: the scope flags flip, poison object IDs move,
        // the level and round counts change, setStatusEnemy's resisted target moves to L0000
        // (zero-target rule), and the untouched preserved operands keep their source WORDs.
        commands[0]!["scope"] = 1;
        commands[0]!["healthTenths"] = 10;
        commands[1]!["playerRole"] = 0;
        commands[1]!["partWord"] = 0; // remove every part
        commands[2]!["scope"] = 0;
        commands[2]!["objectId"] = 560;
        commands[3]!["scope"] = 1;
        commands[3]!["objectId"] = 556;
        commands[4]!["scope"] = 0;
        commands[4]!["objectId"] = 558;
        commands[5]!["scope"] = 1;
        commands[5]!["objectId"] = 559;
        commands[6]!["scope"] = 0;
        commands[6]!["maxLevel"] = 3;
        commands[7]!["status"] = 6;
        commands[7]!["rounds"] = 999;
        commands[8]!["status"] = 0;
        commands[8]!["rounds"] = 65535;
        commands[8]!["to"] = "L0000";
        commands[9]!["status"] = 8;
        File.WriteAllText(path, events.ToJsonString());
        var edited = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", Path.Combine(root, "edited.bin"));
        Assert.True(edited.ExitCode == 0, edited.Stderr);
        Assert.Contains("differs from the recorded source SHA-256", edited.Stdout);
        Assert.Equal(Words(
            0x0022, 1, 10, 9,
            0x0023, 0, 0, 7,
            0x0028, 0, 560, 0,
            0x0029, 1, 556, 4,
            0x002A, 0, 558, 0,
            0x002B, 1, 559, 6,
            0x002C, 0, 3, 0,
            0x002D, 6, 999, 8,
            0x002E, 0, 65535, 0,
            0x002F, 8, 11, 13,
            0, 0, 0, 0,
            0, 0, 0, 0), File.ReadAllBytes(Path.Combine(root, "edited.bin")));

        // A resisted target that dangles is rejected without output.
        commands[8]!["to"] = "L0100";
        File.WriteAllText(path, events.ToJsonString());
        var dangling = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", Path.Combine(root, "dangling.bin"));
        Assert.Equal(1, dangling.ExitCode);
        Assert.False(File.Exists(Path.Combine(root, "dangling.bin")));
    }

    [Fact]
    public async Task Status_item_batch3_verbs_export_roundtrip_and_reencode_named_fields_from_labels()
    {
        // M5 status-item batch 3 (#102): the eight new addMagic / removeMagic /
        // setMagicBaseDamageByMp / halveHp / killPlayer / setMagicBaseDamageByCash / increaseLevel /
        // halveCash verbs plus the re-verified boostStat. Hand-built stream, not retail data.
        // Magic operands are object.json magic-view object IDs (addMagic/removeMagic store them
        // into rgwMagic; the two base-damage verbs dereference .magic.wMagicNumber), operand 1 of
        // 0x0055/0x0056 is the boostStat roleOperand shape, and 0x0057's WORD 0 multiplier
        // normalizes to 8 at runtime only (encodes as 0). The halving and kill verbs map no
        // operands; increaseLevel counts levels; every numeric operand of the batch is an
        // unsigned WORD except boostStat's percent.
        byte[] original = Words(
            0x0030, 17, 50, 2,                     // boostStat: attack row, +50%, role index 1
            0x0055, 300, 0, 7,                     // addMagic: object 300 to the caller role
            0x0056, 311, 2, 0,                     // removeMagic: object 311 from role index 1
            0x0057, 305, 0, 5,                     // setMagicBaseDamageByMp: MP x 8 (encoded 0)
            0x005A, 3, 4, 5,                       // halveHp
            0x005F, 1, 2, 3,                       // killPlayer
            0x0088, 295, 6, 0,                     // setMagicBaseDamageByCash
            0x008D, 1, 2, 3,                       // increaseLevel: one level
            0x008F, 4, 5, 6,                       // halveCash
            0, 0, 0, 0);                           // end keep
        WriteArchive("SSS.MKF", new byte[32], Words(0, 1, 0, 0), [], [], original);
        var export = await CliProcess.Run(root, "export-events", "--input", input, "--output", exported);
        Assert.True(export.ExitCode == 0, export.Stderr);
        string path = Path.Combine(exported, "shared.json");
        var events = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var commands = events["commands"]!.AsArray();
        Assert.Equal(10, commands.Count);
        Assert.Equal("boostStat", commands[0]!["op"]!.GetValue<string>());
        Assert.Equal(17, commands[0]!["field"]!.GetValue<int>());
        Assert.Equal(50, commands[0]!["percent"]!.GetValue<int>());
        Assert.Equal(2, commands[0]!["roleOperand"]!.GetValue<int>());
        Assert.Equal("addMagic", commands[1]!["op"]!.GetValue<string>());
        Assert.Equal(300, commands[1]!["magicId"]!.GetValue<int>());
        Assert.Equal(0, commands[1]!["roleOperand"]!.GetValue<int>());
        Assert.Equal("removeMagic", commands[2]!["op"]!.GetValue<string>());
        Assert.Equal(311, commands[2]!["magicId"]!.GetValue<int>());
        Assert.Equal(2, commands[2]!["roleOperand"]!.GetValue<int>());
        Assert.Equal("setMagicBaseDamageByMp", commands[3]!["op"]!.GetValue<string>());
        Assert.Equal(305, commands[3]!["magicId"]!.GetValue<int>());
        Assert.Equal(0, commands[3]!["mpMultiplier"]!.GetValue<int>());
        Assert.Equal("halveHp", commands[4]!["op"]!.GetValue<string>());
        Assert.Equal("killPlayer", commands[5]!["op"]!.GetValue<string>());
        Assert.Equal("setMagicBaseDamageByCash", commands[6]!["op"]!.GetValue<string>());
        Assert.Equal(295, commands[6]!["magicId"]!.GetValue<int>());
        Assert.Equal("increaseLevel", commands[7]!["op"]!.GetValue<string>());
        Assert.Equal(1, commands[7]!["levels"]!.GetValue<int>());
        Assert.Equal("halveCash", commands[8]!["op"]!.GetValue<string>());
        var rebuild = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", rebuilt);
        Assert.True(rebuild.ExitCode == 0, rebuild.Stderr);
        Assert.Equal(original, File.ReadAllBytes(rebuilt)); // byte-exact round trip of the named batch

        // Semantic edits re-encode from fields: the stat row, percent and role change, the magic
        // object IDs move, the MP multiplier becomes an explicit 8, the level count grows, and
        // the untouched preserved operands keep their source WORDs.
        commands[0]!["field"] = 18; // magicStrength row
        commands[0]!["percent"] = -25;
        commands[0]!["roleOperand"] = 0;
        commands[1]!["magicId"] = 310;
        commands[1]!["roleOperand"] = 2;
        commands[2]!["magicId"] = 300;
        commands[2]!["roleOperand"] = 0;
        commands[3]!["magicId"] = 321;
        commands[3]!["mpMultiplier"] = 8;
        commands[6]!["magicId"] = 300;
        commands[7]!["levels"] = 5;
        File.WriteAllText(path, events.ToJsonString());
        var edited = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", Path.Combine(root, "edited.bin"));
        Assert.True(edited.ExitCode == 0, edited.Stderr);
        Assert.Contains("differs from the recorded source SHA-256", edited.Stdout);
        Assert.Equal(Words(
            0x0030, 18, unchecked((int)0xFFE7), 0,
            0x0055, 310, 2, 7,
            0x0056, 300, 0, 0,
            0x0057, 321, 8, 5,
            0x005A, 3, 4, 5,
            0x005F, 1, 2, 3,
            0x0088, 300, 6, 0,
            0x008D, 5, 2, 3,
            0x008F, 4, 5, 6,
            0, 0, 0, 0), File.ReadAllBytes(Path.Combine(root, "edited.bin")));

        // A negative level count is rejected without output (unsigned WORD, no SHORT cast).
        commands[7]!["levels"] = -1;
        File.WriteAllText(path, events.ToJsonString());
        var rejected = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", Path.Combine(root, "rejected.bin"));
        Assert.Equal(1, rejected.ExitCode);
        Assert.False(File.Exists(Path.Combine(root, "rejected.bin")));
    }

    [Fact]
    public async Task Battle_batch1_verbs_export_roundtrip_and_reencode_named_fields_from_labels()
    {
        // M5 battle batch 1 (#103): the ten new inflictDamageToEnemy / changeBattleSprite /
        // collectEnemyForItems / transformCollectedEnemies / drainHpFromEnemy / playerFlee /
        // simulateMagic / halveEnemyHp / killEnemy / throwWeapon verbs plus the re-verified
        // startBattle / setBattlefield. Hand-built stream, not retail data. The three fail jumps
        // (0x0033/0x0034/0x003A) are required references; simulateMagic's operand 2 is the (SHORT)
        // enemy-target shape (0 or negative = caller slot, positive = operand - 1 enemy index);
        // throwWeapon's operand 0 is the magic-view object of the thrown weapon.
        byte[] original = Words(
            0x0007, 44, 0, 0,        // startBattle: team 44, boss battle (no result targets)
            0x0021, 1, 300, 0,       // inflictDamageToEnemy: every enemy, 300 damage
            0x0031, 5, 0, 0,         // changeBattleSprite: sprite 5 for the caller role
            0x0033, 11, 0, 0,        // collectEnemyForItems: nothing to collect -> L000B
            0x0034, 10, 0, 0,        // transformCollectedEnemies: counter empty -> L000A
            0x0039, 180, 0, 0,       // drainHpFromEnemy: 180 HP to the moving player
            0x003A, 9, 0, 0,         // playerFlee: boss blocks fleeing -> L0009
            0x0042, 323, 0, 0,       // simulateMagic: magic 323, damage 0, caller enemy slot
            0x004A, 12, 0, 0,        // setBattlefield: battlefield 12
            0x005B, 1000, 0, 0,      // halveEnemyHp: halving capped at 1000
            0x0060, 0, 0, 0,         // killEnemy
            0x0066, 344, 105, 0,     // throwWeapon: magic 344, 105 five-HP units
            0, 0, 0, 0);             // end keep
        WriteArchive("SSS.MKF", new byte[32], Words(0, 1, 0, 0), [], [], original);
        var export = await CliProcess.Run(root, "export-events", "--input", input, "--output", exported);
        Assert.True(export.ExitCode == 0, export.Stderr);
        string path = Path.Combine(exported, "shared.json");
        var events = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var commands = events["commands"]!.AsArray();
        Assert.Equal(13, commands.Count);
        Assert.Equal("startBattle", commands[0]!["op"]!.GetValue<string>());
        Assert.Equal(44, commands[0]!["enemyTeamId"]!.GetValue<int>());
        Assert.True(commands[0]!["isBoss"]!.GetValue<bool>());
        Assert.Null(commands[0]!["lossTarget"]);
        Assert.Null(commands[0]!["fleeTarget"]);
        Assert.Equal("inflictDamageToEnemy", commands[1]!["op"]!.GetValue<string>());
        Assert.Equal(1, commands[1]!["scope"]!.GetValue<int>());
        Assert.Equal(300, commands[1]!["damage"]!.GetValue<int>());
        Assert.Equal("changeBattleSprite", commands[2]!["op"]!.GetValue<string>());
        Assert.Equal(5, commands[2]!["battleSprite"]!.GetValue<int>());
        Assert.Equal("collectEnemyForItems", commands[3]!["op"]!.GetValue<string>());
        Assert.Equal("L000B", commands[3]!["to"]!.GetValue<string>());
        Assert.Equal("transformCollectedEnemies", commands[4]!["op"]!.GetValue<string>());
        Assert.Equal("L000A", commands[4]!["to"]!.GetValue<string>());
        Assert.Equal("drainHpFromEnemy", commands[5]!["op"]!.GetValue<string>());
        Assert.Equal(180, commands[5]!["amount"]!.GetValue<int>());
        Assert.Equal("playerFlee", commands[6]!["op"]!.GetValue<string>());
        Assert.Equal("L0009", commands[6]!["to"]!.GetValue<string>());
        Assert.Equal("simulateMagic", commands[7]!["op"]!.GetValue<string>());
        Assert.Equal(323, commands[7]!["magicId"]!.GetValue<int>());
        Assert.Equal(0, commands[7]!["baseDamage"]!.GetValue<int>());
        Assert.Equal(0, commands[7]!["enemyOperand"]!.GetValue<int>());
        Assert.Equal("setBattlefield", commands[8]!["op"]!.GetValue<string>());
        Assert.Equal(12, commands[8]!["battlefieldId"]!.GetValue<int>());
        Assert.Equal("halveEnemyHp", commands[9]!["op"]!.GetValue<string>());
        Assert.Equal(1000, commands[9]!["maxDamage"]!.GetValue<int>());
        Assert.Equal("killEnemy", commands[10]!["op"]!.GetValue<string>());
        Assert.Equal("throwWeapon", commands[11]!["op"]!.GetValue<string>());
        Assert.Equal(344, commands[11]!["magicId"]!.GetValue<int>());
        Assert.Equal(105, commands[11]!["damageUnits"]!.GetValue<int>());
        var rebuild = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", rebuilt);
        Assert.True(rebuild.ExitCode == 0, rebuild.Stderr);
        Assert.Equal(original, File.ReadAllBytes(rebuilt)); // byte-exact round trip of the named batch

        // Semantic edits re-encode from fields: the battle turns non-boss with explicit result
        // targets, the damage/scope/amount/cap fields move, the fail jumps exercise the zero-target
        // rule, simulateMagic gains an explicit 1-based enemy target and damage, and the thrown
        // weapon switches to magic 360 with fewer five-HP units.
        commands[0]!["enemyTeamId"] = 45;
        commands[0]!["lossTarget"] = "L0001";
        commands[0]!["isBoss"] = false;
        commands[0]!["fleeTarget"] = "L0002";
        commands[1]!["scope"] = 0;
        commands[1]!["damage"] = 400;
        commands[2]!["battleSprite"] = 2;
        commands[3]!["to"] = "L0000";
        commands[4]!["to"] = "L0001";
        commands[5]!["amount"] = 999;
        commands[6]!["to"] = "L0000";
        commands[7]!["magicId"] = 349;
        commands[7]!["baseDamage"] = 50;
        commands[7]!["enemyOperand"] = 2;
        commands[8]!["battlefieldId"] = 24;
        commands[9]!["maxDamage"] = 100;
        commands[11]!["magicId"] = 360;
        commands[11]!["damageUnits"] = 10;
        File.WriteAllText(path, events.ToJsonString());
        var edited = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", Path.Combine(root, "edited.bin"));
        Assert.True(edited.ExitCode == 0, edited.Stderr);
        Assert.Contains("differs from the recorded source SHA-256", edited.Stdout);
        Assert.Equal(Words(
            0x0007, 45, 1, 2,
            0x0021, 0, 400, 0,
            0x0031, 2, 0, 0,
            0x0033, 0, 0, 0,
            0x0034, 1, 0, 0,
            0x0039, 999, 0, 0,
            0x003A, 0, 0, 0,
            0x0042, 349, 50, 2,
            0x004A, 24, 0, 0,
            0x005B, 100, 0, 0,
            0x0060, 0, 0, 0,
            0x0066, 360, 10, 0,
            0, 0, 0, 0), File.ReadAllBytes(Path.Combine(root, "edited.bin")));

        // An enemy target beyond the signed WORD bound is rejected without output.
        commands[7]!["enemyOperand"] = 32_768;
        File.WriteAllText(path, events.ToJsonString());
        var rejected = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", Path.Combine(root, "rejected.bin"));
        Assert.Equal(1, rejected.ExitCode);
        Assert.False(File.Exists(Path.Combine(root, "rejected.bin")));
    }

    [Fact]
    public async Task Battle_batch2_verbs_export_roundtrip_and_reencode_named_fields_from_labels()
    {
        // M5 battle batch 2 (#104) - the battle-domain closing batch plus the two menu verbs:
        // showBuyMenu/showSellMenu, enemyUseMagic, enemyEscape, stealFromEnemy, blowAwayEnemies,
        // setBattleResult, enableAutoBattle, showMagicCastAnimation, enemyDivide, enemySummon and
        // enemyTransform. Hand-built stream, not retail data. enemyUseMagic's operand 0 is the
        // magic-view object with the cast rate on operand 1; blowAwayEnemies reads a (SHORT) wind
        // strength (real data uses -3/-2); setBattleResult keeps the raw BATTLERESULT WORD (0xFFFF
        // = fleed); enemyDivide/enemySummon fail jumps are nullable references (WORD 0 = no jump);
        // enemySummon's count is a (SHORT) with non-positive values normalized to 1 at runtime.
        byte[] original = Words(
            0x0026, 12, 0, 0,        // showBuyMenu: store row 12
            0x0027, 0, 0, 0,         // showSellMenu: player inventory, no table row
            0x0067, 318, 5, 0,       // enemyUseMagic: magic 318, rate 5
            0x0069, 0, 0, 0,         // enemyEscape: all enemies flee, battle terminates
            0x006A, 6, 0, 0,         // stealFromEnemy: steal rate 6 from the caller enemy
            0x006B, unchecked((int)0xFFFD), 0, 0, // blowAwayEnemies: (SHORT) -3 wind
            0x0089, 3, 0, 0,         // setBattleResult: won
            0x008A, 0, 0, 0,         // enableAutoBattle
            0x0092, 2, 0, 0,         // showMagicCastAnimation: player slot 2
            0x009C, 0, 0, 0,         // enemyDivide: copies 0 (runtime 1), no fail target
            0x009E, 433, 2, 0,       // enemySummon: enemy 433 x2, no fail target
            0x009F, 410, 0, 0,       // enemyTransform: become enemy 410
            0, 0, 0, 0);             // end keep
        WriteArchive("SSS.MKF", new byte[32], Words(0, 1, 0, 0), [], [], original);
        var export = await CliProcess.Run(root, "export-events", "--input", input, "--output", exported);
        Assert.True(export.ExitCode == 0, export.Stderr);
        string path = Path.Combine(exported, "shared.json");
        var events = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var commands = events["commands"]!.AsArray();
        Assert.Equal(13, commands.Count);
        Assert.Equal("showBuyMenu", commands[0]!["op"]!.GetValue<string>());
        Assert.Equal(12, commands[0]!["storeId"]!.GetValue<int>());
        Assert.Equal("showSellMenu", commands[1]!["op"]!.GetValue<string>());
        Assert.Equal("enemyUseMagic", commands[2]!["op"]!.GetValue<string>());
        Assert.Equal(318, commands[2]!["magicId"]!.GetValue<int>());
        Assert.Equal(5, commands[2]!["magicRate"]!.GetValue<int>());
        Assert.Equal("enemyEscape", commands[3]!["op"]!.GetValue<string>());
        Assert.Equal("stealFromEnemy", commands[4]!["op"]!.GetValue<string>());
        Assert.Equal(6, commands[4]!["stealRate"]!.GetValue<int>());
        Assert.Equal("blowAwayEnemies", commands[5]!["op"]!.GetValue<string>());
        Assert.Equal(-3, commands[5]!["strength"]!.GetValue<int>());
        Assert.Equal("setBattleResult", commands[6]!["op"]!.GetValue<string>());
        Assert.Equal(3, commands[6]!["result"]!.GetValue<int>());
        Assert.Equal("enableAutoBattle", commands[7]!["op"]!.GetValue<string>());
        Assert.Equal("showMagicCastAnimation", commands[8]!["op"]!.GetValue<string>());
        Assert.Equal(2, commands[8]!["playerOperand"]!.GetValue<int>());
        Assert.Equal("enemyDivide", commands[9]!["op"]!.GetValue<string>());
        Assert.Equal(0, commands[9]!["copies"]!.GetValue<int>());
        Assert.Null(commands[9]!["failTarget"]);
        Assert.Equal("enemySummon", commands[10]!["op"]!.GetValue<string>());
        Assert.Equal(433, commands[10]!["objectId"]!.GetValue<int>());
        Assert.Equal(2, commands[10]!["count"]!.GetValue<int>());
        Assert.Null(commands[10]!["failTarget"]);
        Assert.Equal("enemyTransform", commands[11]!["op"]!.GetValue<string>());
        Assert.Equal(410, commands[11]!["objectId"]!.GetValue<int>());
        var rebuild = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", rebuilt);
        Assert.True(rebuild.ExitCode == 0, rebuild.Stderr);
        Assert.Equal(original, File.ReadAllBytes(rebuilt)); // byte-exact round trip of the named batch

        // Semantic edits re-encode from fields: the store row, enemy magic/rate (0 keeps the
        // runtime-normalized encoding), steal rate, a positive wind, the fleed result WORD, no
        // single caster, an explicit divide fail target with 2 copies, a summon of the summoner's
        // own object (WORD 0) with a runtime-normalized count of -1 and an explicit fail target,
        // and another transform target.
        commands[0]!["storeId"] = 1;
        commands[2]!["magicId"] = 375;
        commands[2]!["magicRate"] = 0;
        commands[4]!["stealRate"] = 0;
        commands[5]!["strength"] = 3;
        commands[6]!["result"] = 65_535;
        commands[8]!["playerOperand"] = 0;
        commands[9]!["copies"] = 2;
        commands[9]!["failTarget"] = "L0002";
        commands[10]!["objectId"] = 0;
        commands[10]!["count"] = -1;
        commands[10]!["failTarget"] = "L0001";
        commands[11]!["objectId"] = 434;
        File.WriteAllText(path, events.ToJsonString());
        var edited = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", Path.Combine(root, "edited.bin"));
        Assert.True(edited.ExitCode == 0, edited.Stderr);
        Assert.Contains("differs from the recorded source SHA-256", edited.Stdout);
        Assert.Equal(Words(
            0x0026, 1, 0, 0,
            0x0027, 0, 0, 0,
            0x0067, 375, 0, 0,
            0x0069, 0, 0, 0,
            0x006A, 0, 0, 0,
            0x006B, 3, 0, 0,
            0x0089, unchecked((int)0xFFFF), 0, 0,
            0x008A, 0, 0, 0,
            0x0092, 0, 0, 0,
            0x009C, 2, 2, 0,
            0x009E, 0, unchecked((int)0xFFFF), 1,
            0x009F, 434, 0, 0,
            0, 0, 0, 0), File.ReadAllBytes(Path.Combine(root, "edited.bin")));

        // A summon count beyond the signed WORD bound is rejected without output.
        commands[10]!["count"] = 32_768;
        File.WriteAllText(path, events.ToJsonString());
        var rejected = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", Path.Combine(root, "rejected.bin"));
        Assert.Equal(1, rejected.ExitCode);
        Assert.False(File.Exists(Path.Combine(root, "rejected.bin")));
    }

    [Fact]
    public async Task Scene_movement_batch1_verbs_export_roundtrip_and_reencode_named_fields_from_labels()
    {
        // M5 scene-movement batch 1 (#105, movement part 1): the four walkStep opcode bits,
        // face, walkTo, walkToSlow (new), setObjectPositionRelativeToParty (new),
        // setObjectPosition and setObjectGesture. Hand-built stream, not retail data. Direction
        // comes from the opcode for walkStep (opcode - 0x000B); face's 0xFFFF operands are
        // no-change sentinels kept as plain WORDs; walkToSlow walks every other frame at speed 2
        // (both runtime constants, not operands); the party-relative verb takes the
        // preamble-selected event object id plus offsets from the party's map position (real data
        // encodes -128 as 0xFF80); setObjectGesture hardcodes direction kDirSouth, no operand.
        byte[] original = Words(
            0x000B, 0, 0, 0,                              // walkStep: one step south
            0x000D, 0, 0, 0,                              // walkStep: one step north
            0x000F, 2, unchecked((int)0xFFFF), 5,         // face: north, frame unchanged, operand2 preserved
            0x0010, 24, 60, 1,                            // walkTo: map position, layer 1
            0x0011, 33, 48, 0,                            // walkToSlow: slower walk on layer 0
            0x0012, 84, unchecked((int)0xFF80), 56,       // setObjectPositionRelativeToParty: object 84, -128/+56 around the party
            0x0013, 28, 1280, 256,                        // setObjectPosition: object 28 to the absolute position
            0x0014, 7, 0, 0,                              // setObjectGesture: frame 7 (direction hardcoded south)
            0x000E, 0, 0, 0,                              // walkStep: one step east
            0x000C, 0, 0, 0,                              // walkStep: one step west
            0, 0, 0, 0);                                  // end keep
        WriteArchive("SSS.MKF", new byte[32], Words(0, 1, 0, 0), [], [], original);
        var export = await CliProcess.Run(root, "export-events", "--input", input, "--output", exported);
        Assert.True(export.ExitCode == 0, export.Stderr);
        string path = Path.Combine(exported, "shared.json");
        var events = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var commands = events["commands"]!.AsArray();
        Assert.Equal(11, commands.Count);
        Assert.Equal("walkStep", commands[0]!["op"]!.GetValue<string>());
        Assert.Equal(0, commands[0]!["direction"]!.GetValue<int>());
        Assert.Equal("walkStep", commands[1]!["op"]!.GetValue<string>());
        Assert.Equal(2, commands[1]!["direction"]!.GetValue<int>());
        Assert.Equal("face", commands[2]!["op"]!.GetValue<string>());
        Assert.Equal(2, commands[2]!["direction"]!.GetValue<int>());
        Assert.Equal(65_535, commands[2]!["frame"]!.GetValue<int>()); // no-change sentinel
        Assert.Equal("walkTo", commands[3]!["op"]!.GetValue<string>());
        Assert.Equal(24, commands[3]!["x"]!.GetValue<int>());
        Assert.Equal(1, commands[3]!["h"]!.GetValue<int>());
        Assert.Equal("walkToSlow", commands[4]!["op"]!.GetValue<string>());
        Assert.Equal(33, commands[4]!["x"]!.GetValue<int>());
        Assert.Equal(0, commands[4]!["h"]!.GetValue<int>());
        Assert.Equal("setObjectPositionRelativeToParty", commands[5]!["op"]!.GetValue<string>());
        Assert.Equal(84, commands[5]!["eventObjectId"]!.GetValue<int>());
        Assert.Equal(65_408, commands[5]!["xOffset"]!.GetValue<int>());
        Assert.Equal(56, commands[5]!["yOffset"]!.GetValue<int>());
        Assert.Equal("setObjectPosition", commands[6]!["op"]!.GetValue<string>());
        Assert.Equal(28, commands[6]!["eventObjectId"]!.GetValue<int>());
        Assert.Equal(1280, commands[6]!["x"]!.GetValue<int>());
        Assert.Equal(256, commands[6]!["y"]!.GetValue<int>());
        Assert.Equal("setObjectGesture", commands[7]!["op"]!.GetValue<string>());
        Assert.Equal(7, commands[7]!["frame"]!.GetValue<int>());
        Assert.Equal("walkStep", commands[8]!["op"]!.GetValue<string>());
        Assert.Equal(3, commands[8]!["direction"]!.GetValue<int>());
        Assert.Equal("walkStep", commands[9]!["op"]!.GetValue<string>());
        Assert.Equal(1, commands[9]!["direction"]!.GetValue<int>());
        var rebuild = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", rebuilt);
        Assert.True(rebuild.ExitCode == 0, rebuild.Stderr);
        Assert.Equal(original, File.ReadAllBytes(rebuilt)); // byte-exact round trip of the named batch

        // Semantic edits re-encode from fields: the sentinel frame becomes an explicit 23, the
        // walk targets and layer move, the party-relative verb switches to the caller's object
        // (0) with offsets 0/64, the absolute position moves, and the gesture frame resets to 0.
        commands[2]!["direction"] = 3;
        commands[2]!["frame"] = 23;
        commands[3]!["x"] = 40;
        commands[3]!["h"] = 0;
        commands[4]!["y"] = 50;
        commands[4]!["h"] = 1;
        commands[5]!["eventObjectId"] = 0; // the caller's object via the shared preamble
        commands[5]!["xOffset"] = 0; // exactly at the party
        commands[5]!["yOffset"] = 64;
        commands[6]!["x"] = 1360;
        commands[6]!["y"] = 1376;
        commands[7]!["frame"] = 0;
        File.WriteAllText(path, events.ToJsonString());
        var edited = await CliProcess.Run(root, "rebuild-events", "--input", exported, "--output", Path.Combine(root, "edited.bin"));
        Assert.True(edited.ExitCode == 0, edited.Stderr);
        Assert.Contains("differs from the recorded source SHA-256", edited.Stdout);
        Assert.Equal(Words(
            0x000B, 0, 0, 0,
            0x000D, 0, 0, 0,
            0x000F, 3, 23, 5,
            0x0010, 40, 60, 0,
            0x0011, 33, 50, 1,
            0x0012, 0, 0, 64,
            0x0013, 28, 1360, 1376,
            0x0014, 0, 0, 0,
            0x000E, 0, 0, 0,
            0x000C, 0, 0, 0,
            0, 0, 0, 0), File.ReadAllBytes(Path.Combine(root, "edited.bin")));

        // A walkStep direction that contradicts its source opcode is rejected without output.
        commands[0]!["direction"] = 2;
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
            5, 0, 2, 1,
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
        Assert.Equal(2, commands[13].GetProperty("delayUnits").GetInt32());
        Assert.Equal(1, commands[13].GetProperty("updateGestures").GetInt32());
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
            BinaryPrimitives.WriteUInt16LittleEndian(fullScript.AsSpan(index * 8), 0x0011);
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
