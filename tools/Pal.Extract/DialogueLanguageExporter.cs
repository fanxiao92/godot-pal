using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pal.Extract;

internal static class DialogueLanguageExporter
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static void Export(string input, string reference, string output)
    {
        string destination = ExportPaths.Validate(input, output, ["m_chs.slf", "m_eng.slf", "RGM.MKF", "DATA.MKF"]);
        var chinese = Parse(Path.Combine(input, "m_chs.slf"), "zh-Hans");
        var english = Parse(Path.Combine(input, "m_eng.slf"), "en");
        Directory.CreateDirectory(destination);
        DialogueImageExporter.Export(input, destination);
        Write(Path.Combine(destination, "zh-Hans.json"), chinese);
        Write(Path.Combine(destination, "en.json"), english);
        DialogueFontExporter.Export(reference, destination, ReadCodepoints(Path.Combine(destination, "zh-Hans.json"), Path.Combine(destination, "en.json")));
        Write(Path.Combine(destination, "index.json"), new
        {
            formatVersion = 1,
            defaultLanguage = "zh-Hans",
            languages = new[] { "en", "zh-Hans" },
            messageReferences = "original-start-message-id-and-span"
        });
    }

    private static object Parse(string path, string language)
    {
        if (!File.Exists(path)) throw new InvalidDataException($"Missing required language source '{Path.GetFileName(path)}'.");
        byte[] bytes = File.ReadAllBytes(path);
        string source;
        try { source = StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)} is not valid UTF-8.", exception);
        }

        var messages = new List<object>();
        var identities = new HashSet<(int Start, int End)>();
        using var reader = new StringReader(source);
        string? line;
        string state = "outside";
        int sectionStart = 0;
        bool hasTargetRange = false;
        List<object>? entries = null;
        int lineNumber = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (state == "outside")
            {
                if (line.StartsWith("[BEGIN MESSAGE]", StringComparison.Ordinal))
                {
                    if (!TryMarker(line, "[BEGIN MESSAGE]", out int start))
                        throw Invalid(path, lineNumber, "message start ID is invalid");
                    if (start is < 0 or > ushort.MaxValue)
                        throw Invalid(path, lineNumber, "message start ID is outside the WORD range");
                    sectionStart = start;
                    entries = [];
                    state = "message";
                }
                else if (line.StartsWith("[BEGIN ", StringComparison.Ordinal))
                {
                    state = OtherSectionName(line);
                }
                continue;
            }
            if (state == "message")
            {
                if (line.StartsWith("[END MESSAGE]", StringComparison.Ordinal))
                {
                    if (!TryMarker(line, "[END MESSAGE]", out int end))
                        throw Invalid(path, lineNumber, "message end ID is invalid");
                    if (end < sectionStart || end > ushort.MaxValue)
                        throw Invalid(path, lineNumber, "message range is invalid");
                    if (entries!.Count == 0)
                        throw Invalid(path, lineNumber, "message range has no entries");
                    if (!identities.Add((sectionStart, end)))
                        throw Invalid(path, lineNumber, "duplicate message range");
                    messages.Add(new
                    {
                        startMessageId = sectionStart,
                        endMessageId = end,
                        entries = entries
                    });
                    if (sectionStart == 61 && end == 62) hasTargetRange = true;
                    entries = null;
                    state = "outside";
                }
                else if (line == "[CLEAR MESSAGE]")
                {
                    entries!.Add(new { kind = "clear", text = (string?)null });
                }
                else
                {
                    entries!.Add(new { kind = "text", text = line });
                }
                continue;
            }
            if (line.StartsWith("[END ", StringComparison.Ordinal)) state = "outside";
        }
        if (state == "message") throw Invalid(path, lineNumber, "message section is not terminated");
        if (messages.Count == 0) throw new InvalidDataException($"{Path.GetFileName(path)} contains no message sections.");
        if (!hasTargetRange) throw new InvalidDataException($"{Path.GetFileName(path)} is missing required target message range 61+1.");

        return new
        {
            formatVersion = 1,
            language,
            source = new { file = Path.GetFileName(path), sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) },
            messages
        };
    }

    private static string OtherSectionName(string marker)
    {
        int end = marker.IndexOf(']');
        return end > 7 ? marker[7..end] : "ignored";
    }

    private static bool TryMarker(string line, string marker, out int value)
    {
        value = 0;
        if (!line.StartsWith(marker, StringComparison.Ordinal)) return false;
        string remainder = line[marker.Length..].Trim();
        return int.TryParse(remainder, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static InvalidDataException Invalid(string path, int line, string reason) =>
        new($"{Path.GetFileName(path)}:{line}: {reason}.");

    private static IEnumerable<int> ReadCodepoints(params string[] paths)
    {
        var codepoints = new HashSet<int>();
        foreach (string path in paths)
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
            foreach (JsonElement message in document.RootElement.GetProperty("messages").EnumerateArray())
                foreach (JsonElement entry in message.GetProperty("entries").EnumerateArray())
                    if (entry.GetProperty("kind").GetString() == "text")
                        foreach (Rune rune in entry.GetProperty("text").GetString()!.EnumerateRunes())
                            codepoints.Add(rune.Value);
        }
        return codepoints;
    }

    private static void Write(string path, object document)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, document, new JsonSerializerOptions { WriteIndented = true });
    }
}
