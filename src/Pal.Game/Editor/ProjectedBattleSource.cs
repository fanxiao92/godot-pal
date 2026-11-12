using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pal.Core;
using Pal.Game.Profiles;

namespace Pal.Game.Editor;

/// <summary>Offline receipt/byte consistency, not authentication of upstream M5 data.
/// No files, Godot objects, sessions or projection algorithm are involved.</summary>
public static class ProjectedBattleSource
{
    // Frozen Windows M5 declaration layout (data-schema v1). Kept here rather than
    // referencing Pal.Extract from Game; these are wire-format widths, not projection rules.
    private static readonly (string File, string Archive, int Chunk, int Width, bool Single)[] Tables = [
        ("object.json", "SSS.MKF", 2, 14, false), ("event-object.json", "SSS.MKF", 0, 32, false),
        ("scene.json", "SSS.MKF", 1, 8, false), ("store.json", "DATA.MKF", 0, 18, false),
        ("enemy.json", "DATA.MKF", 1, 70, false), ("enemy-team.json", "DATA.MKF", 2, 10, false),
        ("player-roles.json", "DATA.MKF", 3, 900, true), ("magic.json", "DATA.MKF", 4, 32, false),
        ("battle-field.json", "DATA.MKF", 5, 12, false), ("level-up-magic.json", "DATA.MKF", 6, 20, false),
        ("battle-effect-index.json", "DATA.MKF", 11, 40, true), ("enemy-pos.json", "DATA.MKF", 13, 100, true),
        ("level-up-exp.json", "DATA.MKF", 14, 200, true)];
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private const string Root = "receipt.json.$";

    public static (TriggerBattleContent Content, BattleProjectionSourceInfo Source) Parse(
        ReadOnlyMemory<byte> receiptBytes, ReadOnlyMemory<byte> numericBytes)
    {
        if (numericBytes.Length is < 1 or > 16 * 1024 * 1024)
            throw Error("battle-data.json", "must contain 1..16777216 bytes");
        // Strings and the resulting typed values own their storage. Hash the same
        // supplied original bytes, including any UTF8 BOM, whitespace and newline.
        string receiptJson = Decode(receiptBytes.Span, "receipt.json");
        string numericJson = Decode(numericBytes.Span, "battle-data.json");
        using var receipt = Document(receiptJson, Root);
        JsonElement root = receipt.RootElement;
        Keys(root, Root, false, "formatVersion", "kind", "snapshotKind", "inputTrust", "declaredSources", "declaredTables",
            "modernInputs", "roles", "teams", "enemySlots", "fieldSources", "output", "limits");
        int version = Int(root.GetProperty("formatVersion"), Root + ".formatVersion");
        string kind = Text(root.GetProperty("kind"), Root + ".kind");
        string snapshot = Text(root.GetProperty("snapshotKind"), Root + ".snapshotKind");
        if (Text(root.GetProperty("inputTrust"), Root + ".inputTrust") !=
            "caller-verified-upstream; declarations-and-byte-identities-not-independent-authentication")
            throw Error(Root + ".inputTrust", "unsupported trust declaration");
        _ = Text(root.GetProperty("limits"), Root + ".limits");
        Dictionary<string, int> counts = Declarations(root);
        var inputs = new List<BattleProjectionModernInput>();
        foreach (var (row, i) in Array(root, "modernInputs", Root).Select((row, i) => (row, i)))
        {
            string path = Root + $".modernInputs[{i}]";
            Keys(row, path, false, "file", "sha256", "byteLength");
            inputs.Add(new(Text(row.GetProperty("file"), path + ".file"), Hash(row, path, true),
                Long(row.GetProperty("byteLength"), path + ".byteLength")));
        }
        int[] roles = Array(root, "roles", Root).Select((row, i) => Int(row, Root + $".roles[{i}]")).ToArray();
        int[] teams = Array(root, "teams", Root).Select((row, i) => Int(row, Root + $".teams[{i}]")).ToArray();
        var slots = new List<BattleProjectionEnemySlot>();
        foreach (var (row, i) in Array(root, "enemySlots", Root).Select((row, i) => (row, i)))
        {
            string path = Root + $".enemySlots[{i}]";
            if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("disposition", out var disposition))
                throw Error(path, "expected object with disposition");
            string mode = Text(disposition, path + ".disposition");
            bool loaded = mode == "loaded";
            Keys(row, path, false, loaded
                ? ["teamId", "sourceSlot", "objectId", "disposition", "enemyId", "compactSlot", "battleId"]
                : ["teamId", "sourceSlot", "objectId", "disposition"]);
            slots.Add(new(Int(row.GetProperty("teamId"), path + ".teamId"), Int(row.GetProperty("sourceSlot"), path + ".sourceSlot"),
                Int(row.GetProperty("objectId"), path + ".objectId"), mode,
                loaded ? Int(row.GetProperty("enemyId"), path + ".enemyId") : null,
                loaded ? Int(row.GetProperty("compactSlot"), path + ".compactSlot") : null,
                loaded ? Int(row.GetProperty("battleId"), path + ".battleId") : null));
        }
        JsonElement output = root.GetProperty("output");
        string op = Root + ".output";
        Keys(output, op, false, "file", "byteLength", "sha256");
        if (Text(output.GetProperty("file"), op + ".file") != "numeric/battle-data.json")
            throw Error(op + ".file", "expected numeric/battle-data.json");
        string numericHash = Hash(output, op, true);
        long numericLength = Long(output.GetProperty("byteLength"), op + ".byteLength");
        if (numericLength != numericBytes.Length) throw Error(op + ".byteLength", "does not match original numeric bytes");
        if (numericHash != Digest(numericBytes.Span)) throw Error(op + ".sha256", "does not match original numeric bytes");
        var source = new BattleProjectionSourceInfo(version, kind, snapshot, Digest(receiptBytes.Span), receiptBytes.Length,
            numericHash, numericLength, inputs, roles, teams, slots);
        if (teams.Any(id => id >= counts["enemy-team.json"]))
            throw Error(Root + ".teams", "selection outside declared enemy-team recordCount");
        foreach (var row in source.EnemySlots.Where(row => row.Disposition == "loaded"))
            if (row.ObjectId >= counts["object.json"] || row.EnemyId >= counts["enemy.json"])
                throw Error(Root + ".enemySlots", "loaded identity outside declared object/enemy recordCount");
        TriggerBattleContent content = ModernBattleContent.Parse(numericJson);
        source.ValidateFor(content);
        using var numeric = Document(numericJson, "battle-data.json.$");
        var leaves = new HashSet<string>(StringComparer.Ordinal);
        Leaves(numeric.RootElement, "", leaves);
        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (row, i) in Array(root, "fieldSources", Root).Select((row, i) => (row, i)))
        {
            string path = Root + $".fieldSources[{i}]";
            Keys(row, path, false, "path", "source");
            string field = Text(row.GetProperty("path"), path + ".path");
            _ = Text(row.GetProperty("source"), path + ".source");
            if (!leaves.Contains(field) || !covered.Add(field)) throw Error(path + ".path", "extra or duplicate numeric leaf: " + field);
        }
        if (!covered.SetEquals(leaves)) throw Error(Root + ".fieldSources", "missing numeric leaf: " + leaves.First(leaf => !covered.Contains(leaf)));
        return (content, source);
    }

    private static Dictionary<string, int> Declarations(JsonElement root)
    {
        var archives = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (row, i) in Array(root, "declaredSources", Root).Select((row, i) => (row, i)))
        {
            string path = Root + $".declaredSources[{i}]";
            Keys(row, path, true, "archive", "sha256");
            string archive = Text(row.GetProperty("archive"), path + ".archive");
            if (archive is not "SSS.MKF" and not "DATA.MKF" || !archives.Add(archive))
                throw Error(path + ".archive", "expected unique SSS.MKF and DATA.MKF declarations");
            _ = Hash(row, path, false);
        }
        if (archives.Count != 2) throw Error(Root + ".declaredSources", "both archives required");
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (row, i) in Array(root, "declaredTables", Root).Select((row, i) => (row, i)))
        {
            string path = Root + $".declaredTables[{i}]";
            Keys(row, path, true, "file", "archive", "chunk", "recordCount", "byteLength", "sha256");
            string file = Text(row.GetProperty("file"), path + ".file");
            var layout = Tables.SingleOrDefault(table => table.File == file);
            int count = Int(row.GetProperty("recordCount"), path + ".recordCount");
            long length = Long(row.GetProperty("byteLength"), path + ".byteLength");
            if (layout.File is null || !counts.TryAdd(file, count)) throw Error(path + ".file", "unknown or duplicate table");
            if (Text(row.GetProperty("archive"), path + ".archive") != layout.Archive || Int(row.GetProperty("chunk"), path + ".chunk") != layout.Chunk)
                throw Error(path, "archive/chunk mismatch for " + file);
            if (count is < 1 or > 65536 || (layout.Single && count != 1) || length != (long)count * layout.Width)
                throw Error(path, "recordCount/byteLength mismatch for fixed Windows layout: " + file);
            if (file == "object.json" && length % 12 == 0)
                throw Error(path, "ambiguous Windows/DOS object record width");
            _ = Hash(row, path, false);
        }
        if (counts.Count != 13) throw Error(Root + ".declaredTables", "exact 13 tables required");
        return counts;
    }

    private static void Leaves(JsonElement value, string path, HashSet<string> leaves)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject()) Leaves(property.Value, path.Length == 0 ? property.Name : path + "." + property.Name, leaves);
        }
        else if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0)
        {
            int i = 0;
            foreach (var child in value.EnumerateArray()) Leaves(child, path + $"[{i++}]", leaves);
        }
        else leaves.Add(path);
    }

    private static string Decode(ReadOnlySpan<byte> bytes, string path)
    {
        try { return Utf8.GetString(bytes.StartsWith(Encoding.UTF8.Preamble) ? bytes[3..] : bytes); }
        catch (DecoderFallbackException error) { throw new InvalidDataException(path + ": invalid UTF-8", error); }
    }
    private static JsonDocument Document(string json, string path)
    {
        try
        {
            var document = JsonDocument.Parse(json);
            try { Duplicates(document.RootElement, path); return document; }
            catch { document.Dispose(); throw; }
        }
        catch (JsonException error) { throw new InvalidDataException(path + ": invalid JSON", error); }
    }
    private static void Duplicates(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw Error(path, "duplicate JSON key '" + property.Name + "'");
                Duplicates(property.Value, path + "." + property.Name);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            int i = 0;
            foreach (var child in value.EnumerateArray()) Duplicates(child, path + $"[{i++}]");
        }
    }
    private static void Keys(JsonElement value, string path, bool annotations, params string[] keys)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Error(path, "expected object");
        foreach (var property in value.EnumerateObject())
            if (!keys.Contains(property.Name, StringComparer.Ordinal) && !(annotations && property.Name.StartsWith('_')))
                throw Error(path + "." + property.Name, "unknown field");
        foreach (string key in keys)
            if (!value.TryGetProperty(key, out _)) throw Error(path + "." + key, "missing field");
    }
    private static JsonElement.ArrayEnumerator Array(JsonElement value, string key, string path) =>
        value.GetProperty(key).ValueKind == JsonValueKind.Array ? value.GetProperty(key).EnumerateArray() : throw Error(path + "." + key, "expected array");
    private static string Text(JsonElement value, string path) => value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
        ? value.GetString()! : throw Error(path, "expected nonempty string");
    private static int Int(JsonElement value, string path)
    {
        long number = Long(value, path);
        return number is >= int.MinValue and <= int.MaxValue ? (int)number : throw Error(path, "expected Int32");
    }
    private static long Long(JsonElement value, string path)
    {
        // TryGetInt64 also rejects decimal and exponent spellings (even if mathematically integral).
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number)
            && long.TryParse(value.GetRawText(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)
            ? number : throw Error(path, "expected integer spelling in Int64 range");
    }
    private static string Hash(JsonElement row, string path, bool lowercase)
    {
        string hash = Text(row.GetProperty("sha256"), path + ".sha256");
        if (hash.Length != 64 || hash.Any(c => !char.IsAsciiHexDigit(c) || (lowercase && c is >= 'A' and <= 'F')))
            throw Error(path + ".sha256", lowercase ? "expected lowercase SHA256" : "expected hexadecimal source declaration SHA256");
        return hash;
    }
    private static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static InvalidDataException Error(string path, string detail) => new(path + ": " + detail);
}
