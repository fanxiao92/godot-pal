using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pal.Core;

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

    internal static void Keys(JsonElement element, string context, params string[] allowed) =>
        Keys(element, context, allowed, []);

    /// <summary>Required keys plus optional keys: present optionals are strictly validated, absent ones use their default.</summary>
    internal static void Keys(JsonElement element, string context, string[] required, string[] optional)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"{context}: expected an object.");
        foreach (var property in element.EnumerateObject())
            if (!required.Contains(property.Name, StringComparer.Ordinal) && !optional.Contains(property.Name, StringComparer.Ordinal)
                && !property.Name.StartsWith('_'))
                throw new InvalidDataException($"{context}: unknown field '{property.Name}'.");
        foreach (string key in required)
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
