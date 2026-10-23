using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Pal.Extract;

internal static class EventArchive
{
    private const int MaximumChunkBytes = 64 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex Identifier = new(@"^[A-Za-z_][A-Za-z0-9_-]*\z", RegexOptions.CultureInvariant);

    internal static void Export(string input, string output)
    {
        string destination = ExportPaths.Validate(input, output, ["SSS.MKF"]);
        using var archive = MkfArchive.OpenRequired(input, ["SSS.MKF"])[0];
        byte[] eventObjects = archive.ReadChunk(0, MaximumChunkBytes);
        byte[] scenes = archive.ReadChunk(1, MaximumChunkBytes);
        byte[] script = archive.ReadChunk(4, MaximumChunkBytes);
        if (eventObjects.Length % 32 != 0) throw new InvalidDataException("SSS.MKF: chunk 0 length is not a multiple of 32 bytes.");
        if (scenes.Length % 8 != 0) throw new InvalidDataException("SSS.MKF: chunk 1 length is not a multiple of 8 bytes.");
        if (script.Length == 0 || script.Length % 8 != 0) throw new InvalidDataException("SSS.MKF: chunk 4 must contain complete 8-byte instructions.");
        int count = script.Length / 8;
        if (count > 65536) throw new InvalidDataException("SSS.MKF: chunk 4 exceeds the WORD-addressable instruction limit.");
        var commands = new JsonArray();
        for (int index = 0; index < count; index++) commands.Add(Decode(script, index, count));
        var entries = ReadEntries(eventObjects, scenes, count);
        var indexDocument = new JsonObject
        {
            ["formatVersion"] = 1,
            ["profile"] = "faithful",
            ["source"] = new JsonObject
            {
                ["archive"] = "SSS.MKF", ["chunk"] = 4, ["byteOrder"] = "little",
                ["instructionCount"] = count, ["byteLength"] = script.Length,
                ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(script))
            },
            ["files"] = new JsonArray("shared")
        };
        var eventsDocument = new JsonObject
        {
            ["formatVersion"] = 1, ["profile"] = "faithful", ["fileId"] = "shared",
            ["entries"] = entries, ["commands"] = commands
        };
        Directory.CreateDirectory(destination);
        WriteJson(Path.Combine(destination, "shared.json"), eventsDocument);
        WriteJson(Path.Combine(destination, "index.json"), indexDocument);
    }

    internal static bool Rebuild(string input, string output)
    {
        string sourceDirectory = Path.GetFullPath(input);
        if (!Directory.Exists(sourceDirectory)) throw new InvalidDataException("Events input directory does not exist.");
        RejectEventLinks(sourceDirectory);
        string destination = Path.GetFullPath(output);
        RejectLinks(Path.GetDirectoryName(destination) ?? Directory.GetCurrentDirectory(), "Rebuild output");
        if (File.Exists(destination) || Directory.Exists(destination)) throw new InvalidDataException("Rebuild output must be a new file; existing data will not be overwritten.");
        if (destination.StartsWith(Path.TrimEndingDirectorySeparator(sourceDirectory) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Rebuild output must not be inside the events input directory.");
        var index = ParseJson(Path.Combine(sourceDirectory, "index.json"));
        ValidateKeys(index, "index", "formatVersion", "profile", "source", "files");
        if (Int(index, "formatVersion") != 1 || String(index, "profile") != "faithful") throw new InvalidDataException("index.json: unsupported formatVersion or profile.");
        var source = Required(index, "source");
        ValidateKeys(source, "source", "archive", "chunk", "byteOrder", "instructionCount", "byteLength", "sha256");
        if (String(source, "archive") != "SSS.MKF" || Int(source, "chunk") != 4 || String(source, "byteOrder") != "little")
            throw new InvalidDataException("index.json: source identity is unsupported.");
        int count = Int(source, "instructionCount");
        int length = Int(source, "byteLength");
        if (count is < 1 or > 65536 || length != checked(count * 8)) throw new InvalidDataException("index.json: invalid instructionCount/byteLength.");
        var filesNode = RequiredArray(index, "files");
        var fileIds = filesNode.Select(node => node?.GetValue<string>() ?? throw new InvalidDataException("index.json: file id must be a string.")).ToArray();
        if (fileIds.Length == 0 || !fileIds.SequenceEqual(fileIds.Order(StringComparer.Ordinal)) || fileIds.Distinct(StringComparer.Ordinal).Count() != fileIds.Length || fileIds.Any(id => !Identifier.IsMatch(id)))
            throw new InvalidDataException("index.json: files must be unique, valid, and sorted file ids.");
        var allCommands = new SortedDictionary<int, (string FileId, JsonObject Command)>();
        var labelsByFile = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var allEntries = new List<(string FileId, JsonObject Entry)>();
        foreach (string fileId in fileIds)
        {
            var file = ParseJson(Path.Combine(sourceDirectory, fileId + ".json"));
            ValidateKeys(file, fileId, "formatVersion", "profile", "fileId", "entries", "commands");
            if (Int(file, "formatVersion") != 1 || String(file, "profile") != "faithful" || String(file, "fileId") != fileId)
                throw new InvalidDataException($"{fileId}.json: invalid version, profile, or fileId.");
            var entries = RequiredArray(file, "entries");
            ValidateEntries(entries, count);
            allEntries.AddRange(entries.Select(node => (fileId, node!.AsObject())));
            var labels = new Dictionary<string, int>(StringComparer.Ordinal);
            labelsByFile.Add(fileId, labels);
            int previousAddress = -1;
            foreach (var node in RequiredArray(file, "commands"))
            {
                var command = node?.AsObject() ?? throw new InvalidDataException($"{fileId}.json: command must be an object.");
                ValidateCommand(command, fileId, count);
                int address = Int(Required(command, "source"), "index");
                if (address <= previousAddress) throw new InvalidDataException($"{fileId}.json: commands must be ordered by source.index.");
                previousAddress = address;
                string label = String(command, "label");
                if (!labels.TryAdd(label, address)) throw new InvalidDataException($"{fileId}.json: duplicate command label '{label}'.");
                if (!allCommands.TryAdd(address, (fileId, command))) throw new InvalidDataException($"{fileId}.json: duplicate source address {address:X4}.");
            }
        }
        if (allCommands.Count != count || allCommands.Keys.Where((address, ordinal) => address != ordinal).Any())
            throw new InvalidDataException("Event commands must cover every source address exactly once.");
        var expectedFiles = fileIds.Select(id => id + ".json").Append("index.json").ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.GetFiles(sourceDirectory))
            if (!expectedFiles.Remove(Path.GetFileName(path))) throw new InvalidDataException($"Unexpected event collection file: {Path.GetFileName(path)}.");
        if (expectedFiles.Count != 0) throw new InvalidDataException("Event collection is missing an indexed file.");
        foreach (var (fileId, command) in allCommands.Values)
        {
            string? next = NullableString(command, "next");
            int sourceIndex = Int(Required(command, "source"), "index");
            int nextIndex = (sourceIndex + 1) & 0xffff;
            bool hasSuccessor = count == 65536 || nextIndex < count;
            if (!hasSuccessor)
            {
                if (next is not null) throw new InvalidDataException($"{String(command, "label")}: next must be null at the true end of the chunk.");
                continue;
            }
            if (next is null || ResolveReference(next, fileId, labelsByFile, $"{String(command, "label")}.next") != nextIndex)
                throw new InvalidDataException($"{String(command, "label")}: next does not resolve to its WORD-order successor.");
        }
        foreach (var (fileId, entry) in allEntries)
        {
            string? target = NullableString(entry, "target");
            int sourceAddress = Int(entry, "sourceAddress");
            if (target is null) continue;
            if (ResolveReference(target, fileId, labelsByFile, $"{String(entry, "id")}.target") != sourceAddress)
                throw new InvalidDataException($"{String(entry, "id")}: target does not resolve to sourceAddress {sourceAddress:X4}.");
        }
        var bytes = new byte[length];
        foreach (var pair in allCommands) Encode(pair.Value.Command, bytes.AsSpan(pair.Key * 8, 8));
        string expectedHash = String(source, "sha256");
        if (!Regex.IsMatch(expectedHash, @"^[0-9a-f]{64}\z", RegexOptions.CultureInvariant)) throw new InvalidDataException("index.json: sha256 must be lowercase hexadecimal.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var outputStream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        outputStream.Write(bytes);
        return !string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), expectedHash, StringComparison.Ordinal);
    }

    private static JsonObject Decode(byte[] script, int index, int count)
    {
        int offset = index * 8;
        ushort opcode = Read(script, offset);
        ushort a = Read(script, offset + 2), b = Read(script, offset + 4), c = Read(script, offset + 6);
        string label = LabelAt(index);
        var result = new JsonObject { ["label"] = label };
        if (opcode == 0x003c)
        {
            result["op"] = "setDialog"; result["box"] = "upper"; result["portraitId"] = a;
            result["color"] = b & 255; result["rngWord"] = c;
        }
        else if (opcode == 0xffff)
        {
            result["op"] = "showDialog"; result["messageId"] = a; result["descriptionLineWord"] = b;
        }
        else if (opcode == 0)
        {
            result["op"] = "end"; result["mode"] = "keep";
        }
        else result["op"] = "raw";
        result["next"] = (index + 1 < count || count == 65536) ? LabelAt((index + 1) & 0xffff) : null;
        result["source"] = new JsonObject
        {
            ["index"] = index, ["opcode"] = opcode,
            ["operands"] = new JsonArray(a, b, c)
        };
        return result;
    }

    private static JsonArray ReadEntries(byte[] eventObjects, byte[] scenes, int count)
    {
        var entries = new JsonArray();
        for (int index = 0; index < scenes.Length / 8; index++)
        {
            ushort enter = Read(scenes, index * 8 + 2), teleport = Read(scenes, index * 8 + 4);
            AddEntry(entries, "scene-" + (index + 1) + "-enter", "scene", index + 1, "wScriptOnEnter", "trigger", enter, count);
            AddEntry(entries, "scene-" + (index + 1) + "-teleport", "scene", index + 1, "wScriptOnTeleport", "trigger", teleport, count);
        }
        for (int index = 0; index < eventObjects.Length / 32; index++)
        {
            int offset = index * 32;
            AddEntry(entries, "event-object-" + (index + 1) + "-trigger", "eventObject", index + 1, "wTriggerScript", "trigger", Read(eventObjects, offset + 8), count);
            AddEntry(entries, "event-object-" + (index + 1) + "-auto", "eventObject", index + 1, "wAutoScript", "auto", Read(eventObjects, offset + 10), count);
        }
        return entries;
    }

    private static void AddEntry(JsonArray entries, string id, string ownerType, int ownerId, string slot, string kind, ushort address, int count)
    {
        if (address != 0 && address >= count) throw new InvalidDataException($"{id}: entry address {address:X4} is outside chunk 4.");
        entries.Add(new JsonObject
        {
            ["id"] = id, ["owner"] = new JsonObject { ["type"] = ownerType, ["id"] = ownerId }, ["slot"] = slot,
            ["kind"] = kind, ["target"] = address == 0 ? null : LabelAt(address), ["sourceAddress"] = address
        });
    }

    private static void Encode(JsonObject command, Span<byte> destination)
    {
        var source = Required(command, "source");
        ushort opcode = checked((ushort)Int(source, "opcode"));
        var operands = RequiredArray(source, "operands");
        ushort a = Word(operands[0]), b = Word(operands[1]), c = Word(operands[2]);
        string op = String(command, "op");
        if (op == "raw") { }
        else if (opcode == 0 && op == "end" && String(command, "mode") == "keep") { }
        else if (opcode == 0x003c && op == "setDialog" && String(command, "box") == "upper")
        {
            a = checked((ushort)Int(command, "portraitId"));
            int color = Int(command, "color");
            if (color is < 0 or > 255) throw new InvalidDataException("setDialog.color must be 0..255.");
            b = (ushort)((b & 0xff00) | color);
            _ = checked((ushort)Int(command, "rngWord"));
            c = (ushort)Int(command, "rngWord");
        }
        else if (opcode == 0xffff && op == "showDialog")
        {
            a = checked((ushort)Int(command, "messageId")); b = checked((ushort)Int(command, "descriptionLineWord"));
        }
        else throw new InvalidDataException($"{String(command, "label")}: op does not match registered source opcode.");
        Write(destination, opcode); Write(destination[2..], a); Write(destination[4..], b); Write(destination[6..], c);
    }

    private static void ValidateCommand(JsonObject command, string fileId, int count)
    {
        string label = String(command, "label");
        if (!Identifier.IsMatch(label)) throw new InvalidDataException($"{fileId}.json: invalid command label.");
        string op = String(command, "op");
        if (op is not ("raw" or "end" or "setDialog" or "showDialog")) throw new InvalidDataException($"{fileId}.json: unsupported op {op}.");
        string? next = NullableString(command, "next");
        if (next is not null) ValidateReferenceSyntax(next, $"{label}.next");
        var source = Required(command, "source");
        ValidateKeys(source, "source", "index", "opcode", "operands");
        int index = Int(source, "index"), opcodeValue = Int(source, "opcode");
        if (index < 0 || index >= count || opcodeValue is < 0 or > 65535) throw new InvalidDataException($"{label}: source index/opcode out of range.");
        var operands = RequiredArray(source, "operands");
        if (operands.Count != 3) throw new InvalidDataException($"{label}: source.operands must contain exactly three WORD values.");
        foreach (var operand in operands) Word(operand);
        var allowed = op switch
        {
            "raw" => new[] { "label", "op", "next", "source" },
            "end" => new[] { "label", "op", "mode", "next", "source" },
            "setDialog" => new[] { "label", "op", "box", "portraitId", "color", "rngWord", "next", "source" },
            _ => new[] { "label", "op", "messageId", "descriptionLineWord", "next", "source" }
        };
        ValidateKeys(command, label, allowed);
        if (op == "end" && (opcodeValue != 0 || String(command, "mode") != "keep")) throw new InvalidDataException($"{label}: invalid end mapping.");
        if (op == "setDialog" && (opcodeValue != 0x003c || String(command, "box") != "upper")) throw new InvalidDataException($"{label}: invalid setDialog mapping.");
        if (op == "showDialog" && opcodeValue != 0xffff) throw new InvalidDataException($"{label}: invalid showDialog mapping.");
        if (op == "raw" && opcodeValue is 0 or 0x003c or 0xffff) throw new InvalidDataException($"{label}: registered opcode must use its named mapping.");
        if (op == "raw" && (command.ContainsKey("mode") || command.ContainsKey("text"))) throw new InvalidDataException($"{label}: raw command cannot carry semantic fields.");
    }

    private static void ValidateEntries(JsonArray entries, int count)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in entries)
        {
            var entry = node?.AsObject() ?? throw new InvalidDataException("entries must contain objects.");
            ValidateKeys(entry, "entry", "id", "owner", "slot", "kind", "target", "sourceAddress");
            string id = String(entry, "id");
            if (!Identifier.IsMatch(id) || !ids.Add(id)) throw new InvalidDataException("Entry ids must be unique identifiers.");
            var owner = Required(entry, "owner");
            ValidateKeys(owner, "owner", "type", "id");
            int ownerId = Int(owner, "id");
            if (ownerId is < 0 or > 65535) throw new InvalidDataException($"{id}: owner id must be WORD.");
            string type = String(owner, "type"), slot = String(entry, "slot"), kind = String(entry, "kind");
            bool valid = type == "scene" && slot is "wScriptOnEnter" or "wScriptOnTeleport" && kind == "trigger"
                || type == "eventObject" && slot == "wTriggerScript" && kind == "trigger"
                || type == "eventObject" && slot == "wAutoScript" && kind == "auto";
            if (!valid) throw new InvalidDataException($"{id}: invalid owner/slot/kind combination.");
            int address = Int(entry, "sourceAddress");
            if (address is < 0 or > 65535 || address != 0 && address >= count) throw new InvalidDataException($"{id}: sourceAddress is invalid.");
            string? target = NullableString(entry, "target");
            if ((address == 0) != (target is null)) throw new InvalidDataException($"{id}: zero sourceAddress uses null target and nonzero addresses require a target.");
            if (target is not null) ValidateReferenceSyntax(target, $"{id}.target");
        }
    }

    private static int ResolveReference(string reference, string currentFile, IReadOnlyDictionary<string, Dictionary<string, int>> labelsByFile, string context)
    {
        int separator = reference.IndexOf('#');
        string fileId = currentFile;
        string label = reference;
        if (separator >= 0)
        {
            if (separator == 0 || separator == reference.Length - 1 || reference.IndexOf('#', separator + 1) >= 0)
                throw new InvalidDataException($"{context}: malformed file#label reference '{reference}'.");
            fileId = reference[..separator];
            label = reference[(separator + 1)..];
        }
        if (!Identifier.IsMatch(fileId) || !Identifier.IsMatch(label))
            throw new InvalidDataException($"{context}: malformed reference '{reference}'.");
        if (!labelsByFile.TryGetValue(fileId, out var labels) || !labels.TryGetValue(label, out int address))
            throw new InvalidDataException($"{context}: dangling reference '{reference}'.");
        return address;
    }

    private static void ValidateReferenceSyntax(string reference, string context)
    {
        int separator = reference.IndexOf('#');
        if (separator < 0)
        {
            if (!Identifier.IsMatch(reference)) throw new InvalidDataException($"{context}: malformed reference '{reference}'.");
            return;
        }
        if (separator == 0 || separator == reference.Length - 1 || reference.IndexOf('#', separator + 1) >= 0
            || !Identifier.IsMatch(reference[..separator]) || !Identifier.IsMatch(reference[(separator + 1)..]))
            throw new InvalidDataException($"{context}: malformed file#label reference '{reference}'.");
    }

    private static JsonObject ParseJson(string path)
    {
        if (!File.Exists(path)) throw new InvalidDataException($"Missing event file: {Path.GetFileName(path)}.");
        ReadOnlySpan<byte> bytes = File.ReadAllBytes(path);
        // Keep optional UTF-8 BOM compatibility without auto-detecting other encodings.
        if (bytes.StartsWith(Encoding.UTF8.Preamble)) bytes = bytes[Encoding.UTF8.Preamble.Length..];
        string json;
        try { json = StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)} is not valid UTF-8.", exception);
        }
        using var doc = JsonDocument.Parse(json);
        RejectDuplicateKeys(doc.RootElement, Path.GetFileName(path));
        return JsonNode.Parse(doc.RootElement.GetRawText())?.AsObject() ?? throw new InvalidDataException($"{Path.GetFileName(path)}: root must be object.");
    }

    private static void RejectDuplicateKeys(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new InvalidDataException($"{path}: duplicate JSON key '{property.Name}'.");
                RejectDuplicateKeys(property.Value, path);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) RejectDuplicateKeys(child, path);
    }

    private static void ValidateKeys(JsonObject obj, string context, params string[] allowed)
    {
        var accepted = allowed.ToHashSet(StringComparer.Ordinal);
        foreach (var pair in obj)
            if (!accepted.Contains(pair.Key) && !pair.Key.StartsWith('_')) throw new InvalidDataException($"{context}: unknown field '{pair.Key}'.");
        foreach (string key in allowed)
            if (!obj.ContainsKey(key)) throw new InvalidDataException($"{context}: missing field '{key}'.");
    }

    private static void RejectLinks(string path, string resource)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException($"{resource}: links/reparse points are not supported.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static void RejectEventLinks(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Events input links/reparse points are not supported.");
        foreach (string file in Directory.GetFiles(path)) if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Events input links/reparse points are not supported.");
    }

    private static void WriteJson(string path, JsonObject value) => File.WriteAllText(path, value.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    private static JsonObject Required(JsonObject value, string key) => value[key]?.AsObject() ?? throw new InvalidDataException($"Missing or invalid field '{key}'.");
    private static JsonArray RequiredArray(JsonObject value, string key) => value[key]?.AsArray() ?? throw new InvalidDataException($"Missing or invalid array field '{key}'.");
    private static int Int(JsonObject value, string key) => value[key]?.GetValue<int>() ?? throw new InvalidDataException($"Field '{key}' must be an integer.");
    private static string String(JsonObject value, string key) => value[key]?.GetValue<string>() ?? throw new InvalidDataException($"Field '{key}' must be a string.");
    private static string? NullableString(JsonObject value, string key) => value[key] is null ? null : String(value, key);
    private static ushort Word(JsonNode? value) { int number = value?.GetValue<int>() ?? throw new InvalidDataException("WORD operand must be an integer."); return number is >= 0 and <= 65535 ? (ushort)number : throw new InvalidDataException("WORD operand must be 0..65535."); }
    private static ushort Read(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
    private static void Write(Span<byte> bytes, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
    private static string LabelAt(int address) => "L" + address.ToString("X4");
}
