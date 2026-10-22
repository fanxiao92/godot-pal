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
