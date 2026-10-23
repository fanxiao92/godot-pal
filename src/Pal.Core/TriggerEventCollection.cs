using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pal.Core;

/// <summary>A complete modern faithful event collection. Content is supplied by the I/O host.</summary>
public sealed class TriggerEventCollection
{
    private readonly Dictionary<(string FileId, string Id), TriggerEventEntry> _entries = [];
    private readonly Dictionary<int, TriggerCommand> _commands = [];
    private readonly Dictionary<(string FileId, string Label), int> _labels = [];
    internal TriggerCommand CommandAt(int address) => _commands[address];
    public int InstructionCount { get; private init; }
    public IReadOnlyList<TriggerEventEntry> Entries => Array.AsReadOnly(_entries.Values.ToArray());
    /// <summary>Retained source is diagnostic data, never an execution route for raw opcodes.</summary>
    public TriggerCommandSource GetSource(int sourceAddress) => _commands.TryGetValue(sourceAddress, out var command)
        ? command.Source : throw new ArgumentOutOfRangeException(nameof(sourceAddress));

    public TriggerEventEntry GetEntry(string fileId, string id) => _entries.TryGetValue((fileId, id), out var entry)
        ? entry : throw new InvalidDataException($"{fileId}.json: missing entry '{id}'.");

    public static TriggerEventCollection Parse(string indexText, IReadOnlyDictionary<string, string> eventTexts)
    {
        ArgumentNullException.ThrowIfNull(eventTexts);
        using var index = EventJson.Parse(indexText, "index.json");
        JsonElement root = index.RootElement;
        EventJson.Keys(root, "index.json", "formatVersion", "profile", "source", "files");
        EventJson.Version(root, "index.json");
        JsonElement source = root.GetProperty("source");
        EventJson.Keys(source, "index.json.source", "archive", "chunk", "byteOrder", "instructionCount", "byteLength", "sha256");
        if (EventJson.Text(source, "archive") != "SSS.MKF" || EventJson.Number(source, "chunk") != 4 || EventJson.Text(source, "byteOrder") != "little")
            throw new InvalidDataException("index.json: unsupported source identity.");
        int count = EventJson.Number(source, "instructionCount");
        if (count is < 1 or > 65536 || EventJson.Number(source, "byteLength") != count * 8)
            throw new InvalidDataException("index.json: invalid instructionCount/byteLength.");
        if (!Regex.IsMatch(EventJson.Text(source, "sha256"), @"^[0-9a-f]{64}\z", RegexOptions.CultureInvariant))
            throw new InvalidDataException("index.json: invalid source sha256.");
        string[] fileIds = EventJson.Array(root, "files").Select(value => EventJson.TextValue(value, "index.json.files")).ToArray();
        if (fileIds.Length == 0 || fileIds.Any(id => !EventJson.IsIdentifier(id)) || fileIds.Distinct(StringComparer.Ordinal).Count() != fileIds.Length
            || !fileIds.SequenceEqual(fileIds.Order(StringComparer.Ordinal)))
            throw new InvalidDataException("index.json: files must be sorted unique identifiers.");
        if (!fileIds.ToHashSet(StringComparer.Ordinal).SetEquals(eventTexts.Keys))
            throw new InvalidDataException("index.json: supplied event files do not match the complete files list.");
        var collection = new TriggerEventCollection { InstructionCount = count };
        foreach (string fileId in fileIds)
        {
            using var file = EventJson.Parse(eventTexts[fileId], fileId + ".json");
            JsonElement data = file.RootElement;
            EventJson.Keys(data, fileId + ".json", "formatVersion", "profile", "fileId", "entries", "commands");
            EventJson.Version(data, fileId + ".json");
            if (EventJson.Text(data, "fileId") != fileId) throw new InvalidDataException($"{fileId}.json: mismatched fileId.");
            foreach (var entry in EventJson.Array(data, "entries"))
            {
                try
                {
                    EventJson.Keys(entry, "entry", "id", "owner", "slot", "kind", "target", "sourceAddress");
                    var owner = entry.GetProperty("owner");
                    EventJson.Keys(owner, "owner", "type", "id");
                    string id = EventJson.Text(entry, "id");
                    string type = EventJson.Text(owner, "type"), slot = EventJson.Text(entry, "slot"), kind = EventJson.Text(entry, "kind");
                    int ownerId = EventJson.Word(owner, "id"), address = EventJson.Word(entry, "sourceAddress");
                    string? target = EventJson.Reference(entry, "target");
                    if (!EventJson.IsIdentifier(id) || !ValidSlot(type, slot, kind)) throw new InvalidDataException("Invalid entry id or owner/slot/kind.");
                    if ((address == 0) != (target is null) || address >= count) throw new InvalidDataException($"{id}: invalid zero entry/target or address.");
                    if (!collection._entries.TryAdd((fileId, id), new TriggerEventEntry(fileId, id, type, ownerId, slot, kind, address) { Target = target }))
                        throw new InvalidDataException($"Duplicate entry '{id}'.");
                }
                catch (InvalidDataException exception) { throw new InvalidDataException($"{fileId}.json: {exception.Message}", exception); }
            }
            int previous = -1;
            foreach (var node in EventJson.Array(data, "commands"))
            {
                TriggerCommand command = ParseCommand(node, fileId, count);
                if (command.Position.SourceAddress <= previous) throw new InvalidDataException($"{command.Position}: commands must be ordered by source.index.");
                previous = command.Position.SourceAddress;
                if (!collection._commands.TryAdd(previous, command)) throw new InvalidDataException($"{command.Position}: duplicate source address.");
                if (!collection._labels.TryAdd((fileId, command.Position.Label), previous)) throw new InvalidDataException($"{command.Position}: duplicate label.");
            }
        }
        if (collection._commands.Count != count) throw new InvalidDataException("index.json: commands must cover every source address exactly once.");
        foreach (var command in collection._commands.Values)
        {
            int nextAddress = (command.Position.SourceAddress + 1) & 65535;
            int? expected = nextAddress < count ? nextAddress : null;
            int? actual = command.NextReference is null ? null : collection.Resolve(command.NextReference, command.Position.FileId, command.Position.ToString());
            if (expected != actual) throw new InvalidDataException($"{command.Position}: next must resolve to its WORD-order successor.");
            command.NextAddress = actual;
            if (command.TargetReference is not null) _ = collection.Resolve(command.TargetReference, command.Position.FileId, command.Position.ToString());
        }
        foreach (var entry in collection._entries.Values)
            if (entry.Target is not null && collection.Resolve(entry.Target, entry.FileId, entry.Id) != entry.SourceAddress)
                throw new InvalidDataException($"{entry.FileId}.json entry {entry.Id}: target disagrees with sourceAddress {entry.SourceAddress:X4}.");
        return collection;
    }

    private int Resolve(string reference, string fileId, string context)
    {
        string[] pieces = reference.Split('#');
        var key = pieces.Length == 1 ? (fileId, pieces[0]) : (pieces[0], pieces[1]);
        return _labels.TryGetValue(key, out int address) ? address
            : throw new InvalidDataException($"{fileId}.json {context}: dangling reference '{reference}'.");
    }

    private static bool ValidSlot(string type, string slot, string kind) =>
        type == "scene" && slot is "wScriptOnEnter" or "wScriptOnTeleport" && kind == "trigger"
        || type == "eventObject" && (slot == "wTriggerScript" && kind == "trigger" || slot == "wAutoScript" && kind == "auto")
        || type == "object" && (kind == "trigger" && slot is "player.wScriptOnFriendDeath" or "player.wScriptOnDying"
            or "item.wScriptOnUse" or "item.wScriptOnEquip" or "item.wScriptOnThrow" or "magic.wScriptOnUse" or "magic.wScriptOnSuccess"
            or "enemy.wScriptOnTurnStart" or "enemy.wScriptOnBattleEnd" or "enemy.wScriptOnReady" or "poison.wPlayerScript" or "poison.wEnemyScript"
            || kind == "auto" && slot is "item.wScriptDesc" or "magic.wScriptDesc");

    private static void ReadTarget(JsonElement node, TriggerCommand command)
    {
        command.TargetReference = EventJson.Reference(node, "to") ?? throw new InvalidDataException("Control-flow target must be a reference, including address zero.");
        _ = EventJson.Word(node, "limit");
    }

    private static TriggerCommand ParseCommand(JsonElement node, string fileId, int count)
    {
        string context = fileId + ".json command";
        try
        {
            if (node.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Command must be an object.");
            string label = EventJson.Text(node, "label");
            context += " " + label;
            if (!EventJson.IsIdentifier(label)) throw new InvalidDataException("Invalid label.");
            if (!node.TryGetProperty("source", out var source)) throw new InvalidDataException("Missing source.");
            EventJson.Keys(source, "source", "index", "opcode", "operands");
            int address = EventJson.Word(source, "index"), opcode = EventJson.Word(source, "opcode");
            context += $" @{address:X4}";
            if (address >= count) throw new InvalidDataException("Source index outside instructionCount.");
            var operands = EventJson.Array(source, "operands").ToArray();
            if (operands.Length != 3 || operands.Any(v => v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out int n) || n is < 0 or > 65535))
                throw new InvalidDataException("source.operands must contain exactly three WORD values.");
            string op = EventJson.Text(node, "op");
            string[] fields = op switch
            {
                "raw" => [],
                "end" => EventJson.Text(node, "mode") == "redirect" ? ["mode", "to", "limit"] : ["mode"],
                "goto" => ["to", "limit"], "giveItem" => ["itemId", "count"],
                "setDialog" => EventJson.Text(node, "box") switch
                {
                    "upper" or "lower" => ["box", "portraitId", "color", "rngWord"],
                    "center" => ["box", "color", "rngWord"],
                    "centerWindow" => ["box", "color"],
                    _ => throw new InvalidDataException("Unknown dialogue box.")
                },
                "showDialog" => ["messageId", "descriptionLineWord"],
                _ => throw new InvalidDataException($"Unknown op '{op}'.")
            };
            EventJson.Keys(node, context, ["label", "op", "next", "source", ..fields]);
            var position = new TriggerPosition(fileId, label, address);
            var retainedSource = new TriggerCommandSource(position, opcode, Array.AsReadOnly(operands.Select(value => value.GetInt32()).ToArray()));
            var command = new TriggerCommand(retainedSource, op, EventJson.Reference(node, "next"));
            switch (op)
            {
                case "raw":
                    if (opcode is 0 or 0x3c or 0xffff) throw new InvalidDataException("Registered opcode must use named mapping.");
                    break;
                case "end":
                    command.Mode = EventJson.Text(node, "mode");
                    if (opcode != (command.Mode switch { "keep" => 0, "advance" => 1, "redirect" => 2, _ => -1 }))
                        throw new InvalidDataException("Invalid end mapping.");
                    if (command.Mode == "redirect") ReadTarget(node, command);
                    break;
                case "goto":
                    if (opcode != 3) throw new InvalidDataException("Invalid goto mapping.");
                    ReadTarget(node, command);
                    break;
                case "giveItem":
                    if (opcode != 31) throw new InvalidDataException("Invalid giveItem mapping.");
                    _ = EventJson.Word(node, "itemId");
                    if (EventJson.Number(node, "count") is < -32768 or > 32767) throw new InvalidDataException("giveItem.count must be a signed WORD.");
                    break;
                case "setDialog":
                    command.Box = EventJson.Text(node, "box");
                    if (command.Box is "upper" or "lower") command.PortraitId = EventJson.Word(node, "portraitId");
                    command.Color = EventJson.Number(node, "color");
                    if (command.Box != "centerWindow") command.RngWord = EventJson.Word(node, "rngWord");
                    if (opcode != (command.Box switch { "center" => 59, "upper" => 60, "lower" => 61, "centerWindow" => 62, _ => -1 })
                        || command.Color is < 0 or > 255) throw new InvalidDataException("Invalid setDialog mapping/color.");
                    break;
                case "showDialog":
                    command.MessageId = EventJson.Word(node, "messageId");
                    _ = EventJson.Word(node, "descriptionLineWord");
                    if (opcode != 0xffff) throw new InvalidDataException("Invalid showDialog mapping.");
                    break;
            }
            return command;
        }
        catch (InvalidDataException exception) { throw new InvalidDataException($"{context}: {exception.Message}", exception); }
    }
}

public sealed record TriggerEventEntry(string FileId, string Id, string OwnerType, int OwnerId, string Slot, string Kind, int SourceAddress)
{
    internal string? Target { get; init; }
}

public sealed record TriggerPosition(string FileId, string Label, int SourceAddress)
{
    public override string ToString() => $"{FileId}.json#{Label} @{SourceAddress:X4}";
}

public sealed record TriggerCommandSource(TriggerPosition Position, int Opcode, IReadOnlyList<int> Operands);

internal sealed class TriggerCommand(TriggerCommandSource source, string op, string? nextReference)
{
    internal TriggerCommandSource Source { get; } = source;
    internal TriggerPosition Position => Source.Position;
    internal string Op { get; } = op;
    internal string? NextReference { get; } = nextReference;
    internal int? NextAddress { get; set; }
    internal string? TargetReference { get; set; }
    internal string? Mode { get; set; }
    internal string? Box { get; set; }
    internal int PortraitId { get; set; }
    internal int Color { get; set; }
    internal int RngWord { get; set; }
    internal int MessageId { get; set; }
}

internal static class EventJson
{
    internal static bool IsIdentifier(string value) => Regex.IsMatch(value, @"^[A-Za-z_][A-Za-z0-9_-]*\z", RegexOptions.CultureInvariant);

    internal static JsonDocument Parse(string text, string identity)
    {
        try
        {
            var document = JsonDocument.Parse(text);
            try { CheckDuplicates(document.RootElement, identity); return document; }
            catch { document.Dispose(); throw; }
        }
        catch (JsonException exception) { throw new InvalidDataException($"{identity}: invalid JSON.", exception); }
    }

    private static void CheckDuplicates(JsonElement element, string context)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new InvalidDataException($"{context}: duplicate JSON key '{property.Name}'.");
                CheckDuplicates(property.Value, context + "." + property.Name);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) CheckDuplicates(child, context);
    }

    internal static void Keys(JsonElement element, string context, params string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"{context}: expected an object.");
        foreach (var property in element.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal) && !property.Name.StartsWith('_'))
                throw new InvalidDataException($"{context}: unknown field '{property.Name}'.");
        foreach (string key in allowed)
            if (!element.TryGetProperty(key, out _)) throw new InvalidDataException($"{context}: missing field '{key}'.");
    }

    internal static void Version(JsonElement element, string context)
    {
        if (Number(element, "formatVersion") != 1 || Text(element, "profile") != "faithful")
            throw new InvalidDataException($"{context}: unsupported formatVersion/profile.");
    }

    internal static int Number(JsonElement element, string key)
    {
        if (!element.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number))
            throw new InvalidDataException($"events.json: '{key}' must be an integer.");
        return number;
    }
    internal static int Word(JsonElement element, string key)
    {
        int value = Number(element, key);
        return value is >= 0 and <= 65535 ? value : throw new InvalidDataException($"events.json: '{key}' must be a WORD.");
    }
    internal static string Text(JsonElement element, string key) => element.TryGetProperty(key, out var value)
        ? TextValue(value, "events.json." + key) : throw new InvalidDataException($"events.json: missing '{key}'.");
    internal static string TextValue(JsonElement value, string context) => value.ValueKind == JsonValueKind.String
        ? value.GetString()! : throw new InvalidDataException($"{context}: expected a string.");
    internal static string? Reference(JsonElement element, string key)
    {
        if (!element.TryGetProperty(key, out var value)) throw new InvalidDataException($"Missing '{key}'.");
        if (value.ValueKind == JsonValueKind.Null) return null;
        string reference = TextValue(value, key);
        string[] parts = reference.Split('#');
        if (parts.Length > 2 || parts.Any(part => !IsIdentifier(part))) throw new InvalidDataException($"Malformed reference '{reference}'.");
        return reference;
    }
    internal static JsonElement.ArrayEnumerator Array(JsonElement element, string key) => element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Array
        ? value.EnumerateArray() : throw new InvalidDataException($"events.json: '{key}' must be an array.");
}
