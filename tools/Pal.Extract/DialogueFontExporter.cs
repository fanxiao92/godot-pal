using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pal.Extract;

internal static class DialogueFontExporter
{
    private static readonly Regex Numbers = new(@"0x[0-9a-fA-F]+|\d+", RegexOptions.CultureInvariant);
    private static readonly Regex GlyphRows = new(@"\{([^{}]*)\}", RegexOptions.CultureInvariant);

    internal static void Export(string referenceDirectory, string outputDirectory, IEnumerable<int> codepoints)
    {
        string asciiPath = Path.Combine(referenceDirectory, "ascii.h");
        string glyphPath = Path.Combine(referenceDirectory, "fontglyph.h");
        if (!File.Exists(asciiPath) || !File.Exists(glyphPath))
            throw new InvalidDataException("Reference font inputs ascii.h and fontglyph.h are required.");

        byte[] asciiBytes = File.ReadAllBytes(asciiPath);
        byte[] glyphBytes = File.ReadAllBytes(glyphPath);
        string ascii = new System.Text.UTF8Encoding(false, true).GetString(asciiBytes);
        byte[] iso = ParseArray(ascii, "iso_font[]");
        if (iso.Length < 256 * 15) throw new InvalidDataException("ascii.h ISO font has fewer than 256 15-row glyphs.");

        int[] used = codepoints.Distinct().Order().ToArray();
        List<byte[]>? unifont = null;
        byte[]? widths = null;
        string glyphSource = "fontglyph.h";
        if (used.Any(codepoint => codepoint > 255))
        {
            string glyphs = new System.Text.UTF8Encoding(false, true).GetString(glyphBytes);
            unifont = ParseGlyphRows(glyphs, "unicode_font[65536][32]");
            widths = ParseArray(glyphs, "font_width[65536]");
            if (unifont.Count > 65536 || widths.Length > 65536)
                throw new InvalidDataException("fontglyph.h exceeds the 16-bit default Unifont tables.");
            while (unifont.Count < 65536) unifont.Add(new byte[32]);
            if (widths.Length < 65536) Array.Resize(ref widths, 65536);
        }

        var result = new List<object>(used.Length);
        foreach (int original in used)
        {
            if (original is < 0 or > 0xFFFF || original is >= 0xD800 and < 0xF900)
                throw new InvalidDataException($"No supported reference glyph exists for U+{original:X}.");
            if (original < 256)
            {
                byte[] rows = new byte[15];
                for (int row = 0; row < 15; row++) rows[row] = Reverse(iso[original * 15 + row]);
                result.Add(new { codepoint = original, source = "ISO", width = 8, height = 15, rowBytes = 1, bitmap = Convert.ToHexStringLower(rows) });
                continue;
            }

            int index = original >= 0xF900 ? original - (0xF900 - 0xD800) : original;
            int storageWidth = widths![index];
            if (storageWidth is not (16 or 32))
                throw new InvalidDataException($"Unifont glyph U+{original:X4} has unsupported width metadata.");
            byte[] bitmap = unifont![index][..storageWidth];
            result.Add(new
            {
                codepoint = original,
                source = "Unifont",
                width = storageWidth >> 1,
                height = 16,
                rowBytes = storageWidth == 32 ? 2 : 1,
                bitmap = Convert.ToHexStringLower(bitmap)
            });
        }

        using var stream = new FileStream(Path.Combine(outputDirectory, "font.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, new
        {
            formatVersion = 1,
            source = new[]
            {
                new { file = "ascii.h", role = "ISO", sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(asciiBytes)) },
                new { file = glyphSource, role = "Unifont", sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(glyphBytes)) }
            },
            glyphs = result
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static List<byte[]> ParseGlyphRows(string source, string declaration)
    {
        string body = ArrayBody(source, declaration);
        var rows = new List<byte[]>();
        foreach (Match match in GlyphRows.Matches(body))
        {
            byte[] values = ParseNumbers(match.Groups[1].Value);
            if (values.Length is < 1 or > 32) throw new InvalidDataException("Unifont glyph row exceeds its 32-byte storage.");
            if (values.Length != 32) Array.Resize(ref values, 32);
            rows.Add(values);
        }
        return rows;
    }

    private static byte[] ParseArray(string source, string declaration) => ParseNumbers(ArrayBody(source, declaration));

    private static byte[] ParseNumbers(string source)
    {
        MatchCollection matches = Numbers.Matches(source);
        var values = new byte[matches.Count];
        for (int i = 0; i < matches.Count; i++)
        {
            string token = matches[i].Value;
            int value = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? int.Parse(token.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                : int.Parse(token, NumberStyles.None, CultureInfo.InvariantCulture);
            if (value is < 0 or > 255) throw new InvalidDataException("Reference font table contains a value outside the byte range.");
            values[i] = (byte)value;
        }
        return values;
    }

    private static string ArrayBody(string source, string declaration)
    {
        int declarationStart = source.IndexOf(declaration, StringComparison.Ordinal);
        if (declarationStart < 0) throw new InvalidDataException($"Reference font declaration '{declaration}' is missing.");
        int start = source.IndexOf('{', declarationStart);
        int end = source.IndexOf("};", start, StringComparison.Ordinal);
        if (start < 0 || end < 0) throw new InvalidDataException($"Reference font declaration '{declaration}' is unterminated.");
        string body = source[(start + 1)..end];
        body = Regex.Replace(body, @"/\*.*?\*/|//[^\r\n]*", string.Empty, RegexOptions.Singleline | RegexOptions.CultureInvariant);
        return body;
    }

    private static byte Reverse(byte value)
    {
        value = (byte)(((value & 0x55) << 1) | ((value >> 1) & 0x55));
        value = (byte)(((value & 0x33) << 2) | ((value >> 2) & 0x33));
        return (byte)((value << 4) | (value >> 4));
    }
}
