using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pal.Extract;

internal static class EventArchive
{
    private const int MaximumChunkBytes = 64 * 1024 * 1024;

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
        var index = StrictJson.ParseObject(Path.Combine(sourceDirectory, "index.json"), "event file");
        StrictJson.ValidateKeys(index, "index", "formatVersion", "profile", "source", "files");
        if (StrictJson.Int(index, "formatVersion") != 1 || StrictJson.String(index, "profile") != "faithful") throw new InvalidDataException("index.json: unsupported formatVersion or profile.");
        var source = StrictJson.Required(index, "source");
        StrictJson.ValidateKeys(source, "source", "archive", "chunk", "byteOrder", "instructionCount", "byteLength", "sha256");
        if (StrictJson.String(source, "archive") != "SSS.MKF" || StrictJson.Int(source, "chunk") != 4 || StrictJson.String(source, "byteOrder") != "little")
            throw new InvalidDataException("index.json: source identity is unsupported.");
        int count = StrictJson.Int(source, "instructionCount");
        int length = StrictJson.Int(source, "byteLength");
        if (count is < 1 or > 65536 || length != checked(count * 8)) throw new InvalidDataException("index.json: invalid instructionCount/byteLength.");
        var filesNode = StrictJson.RequiredArray(index, "files");
        var fileIds = filesNode.Select(node => node?.GetValue<string>() ?? throw new InvalidDataException("index.json: file id must be a string.")).ToArray();
        if (fileIds.Length == 0 || !fileIds.SequenceEqual(fileIds.Order(StringComparer.Ordinal)) || fileIds.Distinct(StringComparer.Ordinal).Count() != fileIds.Length || fileIds.Any(id => !OpcodeRegistry.IsIdentifier(id)))
            throw new InvalidDataException("index.json: files must be unique, valid, and sorted file ids.");
        var allCommands = new SortedDictionary<int, (string FileId, JsonObject Command)>();
        var labelsByFile = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var allEntries = new List<(string FileId, JsonObject Entry)>();
        foreach (string fileId in fileIds)
        {
            var file = StrictJson.ParseObject(Path.Combine(sourceDirectory, fileId + ".json"), "event file");
            StrictJson.ValidateKeys(file, fileId, "formatVersion", "profile", "fileId", "entries", "commands");
            if (StrictJson.Int(file, "formatVersion") != 1 || StrictJson.String(file, "profile") != "faithful" || StrictJson.String(file, "fileId") != fileId)
                throw new InvalidDataException($"{fileId}.json: invalid version, profile, or fileId.");
            var entries = StrictJson.RequiredArray(file, "entries");
            ValidateEntries(entries, count);
            allEntries.AddRange(entries.Select(node => (fileId, node!.AsObject())));
            var labels = new Dictionary<string, int>(StringComparer.Ordinal);
            labelsByFile.Add(fileId, labels);
            int previousAddress = -1;
            foreach (var node in StrictJson.RequiredArray(file, "commands"))
            {
                var command = node?.AsObject() ?? throw new InvalidDataException($"{fileId}.json: command must be an object.");
                OpcodeRegistry.ValidateCommand(command, fileId, count);
                int address = StrictJson.Int(StrictJson.Required(command, "source"), "index");
                if (address <= previousAddress) throw new InvalidDataException($"{fileId}.json: commands must be ordered by source.index.");
                previousAddress = address;
                string label = StrictJson.String(command, "label");
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
        var labelViews = labelsByFile.ToDictionary(pair => pair.Key, pair => (IReadOnlyDictionary<string, int>)pair.Value);
        foreach (var (fileId, command) in allCommands.Values)
        {
            string? next = StrictJson.NullableString(command, "next");
            int sourceIndex = StrictJson.Int(StrictJson.Required(command, "source"), "index");
            int nextIndex = (sourceIndex + 1) & 0xffff;
            bool hasSuccessor = count == 65536 || nextIndex < count;
            if (!hasSuccessor)
            {
                if (next is not null) throw new InvalidDataException($"{StrictJson.String(command, "label")}: next must be null at the true end of the chunk.");
                continue;
            }
            if (next is null || OpcodeRegistry.ResolveReference(next, fileId, labelViews, $"{StrictJson.String(command, "label")}.next") != nextIndex)
                throw new InvalidDataException($"{StrictJson.String(command, "label")}: next does not resolve to its WORD-order successor.");
        }
        foreach (var (fileId, entry) in allEntries)
        {
            string? target = StrictJson.NullableString(entry, "target");
            int sourceAddress = StrictJson.Int(entry, "sourceAddress");
            if (target is null) continue;
            if (OpcodeRegistry.ResolveReference(target, fileId, labelViews, $"{StrictJson.String(entry, "id")}.target") != sourceAddress)
                throw new InvalidDataException($"{StrictJson.String(entry, "id")}: target does not resolve to sourceAddress {sourceAddress:X4}.");
        }
        var bytes = new byte[length];
        foreach (var pair in allCommands) OpcodeRegistry.EncodeCommand(pair.Value.Command, pair.Value.FileId, labelViews, bytes.AsSpan(pair.Key * 8, 8));
        string expectedHash = StrictJson.String(source, "sha256");
        if (!System.Text.RegularExpressions.Regex.IsMatch(expectedHash, @"^[0-9a-f]{64}\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant)) throw new InvalidDataException("index.json: sha256 must be lowercase hexadecimal.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var outputStream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        outputStream.Write(bytes);
        return !string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), expectedHash, StringComparison.Ordinal);
    }

    private static JsonObject Decode(byte[] script, int index, int count)
    {
        int offset = index * 8;
        return OpcodeRegistry.DecodeCommand(index, count,
            Read(script, offset), Read(script, offset + 2), Read(script, offset + 4), Read(script, offset + 6));
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
            ["kind"] = kind, ["target"] = address == 0 ? null : OpcodeRegistry.LabelAt(address), ["sourceAddress"] = address
        });
    }

    private static void ValidateEntries(JsonArray entries, int count)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in entries)
        {
            var entry = node?.AsObject() ?? throw new InvalidDataException("entries must contain objects.");
            StrictJson.ValidateKeys(entry, "entry", "id", "owner", "slot", "kind", "target", "sourceAddress");
            string id = StrictJson.String(entry, "id");
            if (!OpcodeRegistry.IsIdentifier(id) || !ids.Add(id)) throw new InvalidDataException("Entry ids must be unique identifiers.");
            var owner = StrictJson.Required(entry, "owner");
            StrictJson.ValidateKeys(owner, "owner", "type", "id");
            int ownerId = StrictJson.Int(owner, "id");
            if (ownerId is < 0 or > 65535) throw new InvalidDataException($"{id}: owner id must be WORD.");
            string type = StrictJson.String(owner, "type"), slot = StrictJson.String(entry, "slot"), kind = StrictJson.String(entry, "kind");
            bool valid = type == "scene" && slot is "wScriptOnEnter" or "wScriptOnTeleport" && kind == "trigger"
                || type == "eventObject" && slot == "wTriggerScript" && kind == "trigger"
                || type == "eventObject" && slot == "wAutoScript" && kind == "auto";
            if (!valid) throw new InvalidDataException($"{id}: invalid owner/slot/kind combination.");
            int address = StrictJson.Int(entry, "sourceAddress");
            if (address is < 0 or > 65535 || address != 0 && address >= count) throw new InvalidDataException($"{id}: sourceAddress is invalid.");
            string? target = StrictJson.NullableString(entry, "target");
            if ((address == 0) != (target is null)) throw new InvalidDataException($"{id}: zero sourceAddress uses null target and nonzero addresses require a target.");
            if (target is not null) OpcodeRegistry.ValidateReferenceSyntax(target, $"{id}.target");
        }
    }

    private static void RejectLinks(string path, string resource) =>
        PathLinkGuard.RejectReparsePoints(path, $"{resource}: links/reparse points are not supported.");

    private static void RejectEventLinks(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Events input links/reparse points are not supported.");
        foreach (string file in Directory.GetFiles(path)) if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Events input links/reparse points are not supported.");
    }

    private static void WriteJson(string path, JsonObject value) => File.WriteAllText(path, value.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    private static ushort Read(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
}
