using System.Text.Json;

namespace Pal.Extract;

internal static class ExportSupport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal static T DecodeResource<T>(string name, int chunk, Func<T> decode)
    {
        try { return decode(); }
        catch (InvalidDataException exception) { throw new InvalidDataException($"{name}: chunk {chunk}: {exception.Message}", exception); }
    }

    internal static void WriteJson(string output, string name, object value) =>
        File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(value, JsonOptions) + "\n");

    internal static void WritePalette(string output, int paletteId, string lighting, DecodedPalette palette) =>
        WriteJson(output, "palette.json", new
        {
            formatVersion = 1,
            paletteId,
            lighting,
            selectedLighting = palette.UsesNightColors ? "night" : "day",
            colors = palette.Colors,
            source = new { archive = "pat.mkf", chunk = paletteId, componentConversion = "six-bit << 2" }
        });
}
