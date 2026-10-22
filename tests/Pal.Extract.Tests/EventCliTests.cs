using System.Buffers.Binary;
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
        WriteArchive("SSS.MKF", new byte[32], Words(0, 0, 0, 0), Words(0, 0, 0, 0), [], fullScript);
        var export = await CliProcess.Run(root, "export-events", "--input", input, "--output", exported);
        Assert.True(export.ExitCode == 0, export.Stderr);
        using var events = JsonDocument.Parse(File.ReadAllText(Path.Combine(exported, "shared.json")));
        var commands = events.RootElement.GetProperty("commands");
        Assert.Equal(65536, commands.GetArrayLength());
        Assert.Equal("L0000", commands[65535].GetProperty("next").GetString());
        Assert.Equal("LFFFF", commands[65534].GetProperty("next").GetString());

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
