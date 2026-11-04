using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pal.Extract;

// Strict JSON helpers shared by the events and data-table pipelines:
// UTF-8 only, no duplicate keys, no unknown fields, WORD range checks.
public static class StrictJson
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    // Parses a JSON object document from disk; rejects non-UTF-8 bytes and duplicate keys.
    public static JsonObject ParseObject(string path, string resource)
    {
        if (!File.Exists(path)) throw new InvalidDataException($"Missing {resource}: {Path.GetFileName(path)}.");
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

    // Rejects duplicate keys anywhere in the document; JsonNode would otherwise silently keep the last one.
    public static void RejectDuplicateKeys(JsonElement element, string path)
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

    // Allows exactly the listed fields plus '_' comment fields; every listed field must be present.
    public static void ValidateKeys(JsonObject obj, string context, params string[] allowed)
    {
        var accepted = allowed.ToHashSet(StringComparer.Ordinal);
        foreach (var pair in obj)
            if (!accepted.Contains(pair.Key) && !pair.Key.StartsWith('_')) throw new InvalidDataException($"{context}: unknown field '{pair.Key}'.");
        foreach (string key in allowed)
            if (!obj.ContainsKey(key)) throw new InvalidDataException($"{context}: missing field '{key}'.");
    }

    public static JsonObject Required(JsonObject value, string key) => value[key]?.AsObject() ?? throw new InvalidDataException($"Missing or invalid field '{key}'.");
    public static JsonArray RequiredArray(JsonObject value, string key) => value[key]?.AsArray() ?? throw new InvalidDataException($"Missing or invalid array field '{key}'.");
    public static int Int(JsonObject value, string key) => value[key]?.GetValue<int>() ?? throw new InvalidDataException($"Field '{key}' must be an integer.");
    public static string String(JsonObject value, string key) => value[key]?.GetValue<string>() ?? throw new InvalidDataException($"Field '{key}' must be a string.");
    public static string? NullableString(JsonObject value, string key) => value[key] is null ? null : String(value, key);
    public static ushort Word(JsonNode? value)
    {
        int number;
        try { number = value?.GetValue<int>() ?? throw new InvalidDataException("WORD operand must be an integer."); }
        catch (InvalidOperationException) { throw new InvalidDataException("WORD operand must be an integer."); }
        catch (FormatException) { throw new InvalidDataException("WORD operand must be an integer."); }
        return number is >= 0 and <= 65535 ? (ushort)number : throw new InvalidDataException("WORD operand must be 0..65535.");
    }
}
